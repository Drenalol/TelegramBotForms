using System.Collections.Concurrent;
using Drenalol.Telegram.Bot.Forms.Localization;
using Microsoft.Extensions.DependencyInjection;
using Telegram.Bot.Types;

namespace Drenalol.Telegram.Bot.Forms;

/// <summary>
/// The bot router: maps incoming texts to registered forms and commands, keeps a
/// per-chat navigation history (a stack of the forms the user visited) and implements
/// the "Back" action over it.
/// </summary>
internal sealed class FormCollection : IFormRetriever
{
  private readonly IServiceScopeFactory _serviceScopeFactory;
  private readonly ITelegramTextLocalizer _localizer;
  private readonly IReadOnlyDictionary<string, object> _forms;
  private readonly IReadOnlyDictionary<string, Type> _commands;
  private readonly ConcurrentDictionary<Chat, ConcurrentStack<IForm>> _state;

  public FormCollection(
    IServiceScopeFactory serviceScopeFactory,
    ITelegramTextLocalizer localizer,
    IReadOnlyDictionary<string, Type> registeredForms,
    IReadOnlyDictionary<string, Type> registeredCommands
  )
  {
    _serviceScopeFactory = serviceScopeFactory;
    _localizer = localizer;
    _forms = Register(registeredForms);
    _commands = registeredCommands;
    _state = new ConcurrentDictionary<Chat, ConcurrentStack<IForm>>(ChatComparer.Instance);
  }

  public async Task<IForm?> GetAsync(Chat chat, string key)
  {
    // "/start <parameter>" (Telegram deep links, t.me/<bot>?start=<parameter>) opens the
    // start form; the form itself reads the parameter after "/start".
    var lookupKey = FirstToken(key) == BotCommands.Start ? "/start" : key;

    return TryGetCommand(chat, key) ?? await GetByTargetAsync(chat, TryGet(lookupKey));
  }

  async ValueTask<IForm> IFormRetriever.GetFormAsync(Chat chat, Type formType)
  {
    var history = GetHistory(chat);

    // NavigateToAsync from inside a form follows the same rule as a keyboard press:
    // an already open target is rewound to, never stacked as a duplicate
    if (await RewindToAsync(history, formType) is { } target)
      return new FormWrapper(target, history, _localizer, true);

    return CreateNavigationWrapper(chat, formType);
  }

  /// <summary>
  /// Navigation inside the existing history: when the target form is already open in
  /// the chat (a list sitting under a card), the forms above it are popped and disposed
  /// and the waiting instance is <see cref="IStatefulForm.Reset"/> and re-rendered in
  /// place — instead of stacking a second copy of the screen, which would make "Back"
  /// paint the same form once per copy. A completed stateful form carries no live
  /// conversation to re-enter, so it leaves the history too and the caller pushes a
  /// fresh instance.
  /// </summary>
  private static async ValueTask<IForm?> RewindToAsync(ConcurrentStack<IForm> history, Type formType)
  {
    IForm? target = null;

    foreach (var form in history)
      if (form.GetType() == formType)
      {
        target = form;
        break;
      }

    if (target is null)
      return null;

    var completed = target is IStatefulForm { IsCompleted: true };

    while (history.TryPeek(out var top) && !ReferenceEquals(top, target))
      await PopAndDisposeAsync(history);

    if (completed)
    {
      // nothing live to rewind to: the completed instance leaves the history and the
      // caller renders a fresh one on top of what remains
      await PopAndDisposeAsync(history);

      return null;
    }

    if (target is IStatefulForm statefulForm)
      statefulForm.Reset();

    return target;
  }

  private static async ValueTask PopAndDisposeAsync(ConcurrentStack<IForm> history)
  {
    if (!history.TryPop(out var popped))
      return;

    // a form leaving the history loses its DI scope with it
    if (popped is IAsyncDisposable disposable)
      await disposable.DisposeAsync();
  }

  private static string? FirstToken(string key) =>
    key.Split(' ', StringSplitOptions.RemoveEmptyEntries)
      .FirstOrDefault()
      ?.Trim()
      .TrimStart('/');

  /// <summary>
  /// Commands win over keyboard keys and over the form waiting in the history.
  /// </summary>
  private IForm? TryGetCommand(Chat chat, string key)
  {
    var firstToken = FirstToken(key);

    if (firstToken is null || firstToken.Length == 0 || !_commands.TryGetValue(firstToken, out var commandType))
      return null;

    var scope = _serviceScopeFactory.CreateScope();
    var command = (IBotCommand)scope.ServiceProvider.GetRequiredService(commandType);

    return new FormWrapper(new CommandFormAdapter(command, scope), GetHistory(chat), _localizer, skipPush: true);
  }

  private async Task<IForm?> GetByTargetAsync(Chat chat, object? formObj)
  {
    var history = GetHistory(chat);

    // Nothing registered claims this text: the stateful form waiting in the history (a
    // wizard awaiting input, a paged list awaiting a search) takes it.
    if (formObj == null && history.TryGetLastForm() is IStatefulForm statefulForm)
      return new FormWrapper(statefulForm, history, _localizer, true);

    if (formObj is Type navigationType)
    {
      // Tapping the button of a form that is already open somewhere in the history
      // (double taps, retries after a lost reply, a card navigating back to its list)
      // must not stack a second identical screen - "Back" would paint the same form
      // once per copy. Rewind to the waiting instance instead: everything above it
      // leaves the history and the instance is Reset (a browser like PagedFormBase
      // returns to page one) and re-rendered in place.
      if (await RewindToAsync(history, navigationType) is { } target)
        return new FormWrapper(target, history, _localizer, true);

      return CreateNavigationWrapper(chat, navigationType);
    }

    if (formObj is BackForm backFormAction && await backFormAction(history) is { } backForm)
      return new FormWrapper(backForm, history, _localizer, true);

    return null;
  }

  /// <summary>Resolves a form for navigation: fresh DI scope, pushed onto the history unless it ignores it.</summary>
  private FormWrapper CreateNavigationWrapper(Chat chat, Type formType)
  {
    var form = CreateForm(formType);

    return new FormWrapper(form, GetHistory(chat), _localizer, form.IgnoreHistory);
  }

  private object? TryGet(string key) => _forms.TryGetValue(key, out var obj) ? obj : null;

  private FormBase CreateForm(Type formType)
  {
    var scope = _serviceScopeFactory.CreateScope();
    var form = (FormBase)scope.ServiceProvider.GetRequiredService(formType);
    form.SetData(scope, _localizer);

    return form;
  }

  private IReadOnlyDictionary<string, object> Register(IReadOnlyDictionary<string, Type> formTypes)
  {
    var dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

    // The back button is localized per chat, so every language variant of the "Back"
    // text must resolve to the back action.
    var backKeys = _localizer.SupportedLanguages.Select(language => _localizer.Back(language)).Distinct().ToArray();

    foreach (var backKey in backKeys)
      dict[backKey] = new BackForm(BackFormAction);

    foreach (var (key, value) in formTypes)
      dict.Add(key, value);

    return dict;
  }

  /// <summary>
  /// Pops the current form and returns the previous one. A form leaving the history
  /// loses its DI scope with it. Completed stateful forms carry no live conversation to
  /// return to, so they are skipped; a waiting stateful one is <see cref="IStatefulForm.Reset"/>
  /// and re-entered; an exhausted history falls back to the start form.
  /// </summary>
  private async Task<IForm?> BackFormAction(ConcurrentStack<IForm> history)
  {
    if (history.IsEmpty)
      return TryGetStartForm();

    IForm? form = null;

    while (history.TryPop(out var popped))
    {
      // the popped form leaves the history - its owned DI scope goes with it
      if (popped is IAsyncDisposable disposable)
        await disposable.DisposeAsync();

      form = history.TryGetLastForm();

      switch (form)
      {
        case null:
          return TryGetStartForm();
        case IStatefulForm { IsCompleted: true }:
        {
          form = null;

          continue;
        }
        case IStatefulForm statefulForm:
        {
          statefulForm.Reset();

          return form;
        }
      }

      break;
    }

    return form;
  }

  private IForm? TryGetStartForm() =>
    TryGet("/start") is Type formType
      ? CreateForm(formType)
      : null;

  private ConcurrentStack<IForm> GetHistory(Chat chat) => _state.GetOrAdd(chat, static _ => new ConcurrentStack<IForm>());
}
