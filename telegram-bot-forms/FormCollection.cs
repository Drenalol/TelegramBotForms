using System.Collections.Concurrent;
using Drenalol.Telegram.Bot.Forms.Localization;
using Drenalol.Telegram.Bot.Forms.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;

namespace Drenalol.Telegram.Bot.Forms;

/// <summary>
/// The bot router: maps incoming texts to registered forms and commands, keeps a
/// per-chat navigation history (a stack of the forms the user visited) and implements
/// the "Back" action over it.
/// <para>
/// When an <see cref="IChatHistoryStore"/> is registered the history also survives a
/// restart: a chat's stored snapshot is restored lazily on its first message after the
/// start, and every settled turn is written back (see <see cref="ChatHistoryCoordinator"/>).
/// </para>
/// </summary>
internal sealed class FormCollection : IFormRetriever
{
  private readonly IServiceScopeFactory _serviceScopeFactory;
  private readonly ITelegramTextLocalizer _localizer;
  private readonly IReadOnlyDictionary<string, object> _forms;
  private readonly IReadOnlyDictionary<string, Type> _commands;
  private readonly ChatHistoryCoordinator? _history;
  private readonly ConcurrentDictionary<Chat, ChatState> _state;

  public FormCollection(
    IServiceScopeFactory serviceScopeFactory,
    ITelegramTextLocalizer localizer,
    IReadOnlyDictionary<string, Type> registeredForms,
    IReadOnlyDictionary<string, Type> registeredCommands,
    IReadOnlyList<Type> registeredFormTypes,
    IChatHistoryStore? historyStore = null,
    ILoggerFactory? loggerFactory = null
  )
  {
    _serviceScopeFactory = serviceScopeFactory;
    _localizer = localizer;
    _forms = Register(registeredForms);
    _commands = registeredCommands;
    _state = new ConcurrentDictionary<Chat, ChatState>(ChatComparer.Instance);

    _history = historyStore is null
      ? null
      : new ChatHistoryCoordinator(
        historyStore,
        registeredFormTypes,
        CreateForm,
        loggerFactory?.CreateLogger("Drenalol.Telegram.Bot.Forms.Persistence") ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance
      );
  }

  public async Task<IForm?> GetAsync(Chat chat, string key)
  {
    // "/start <parameter>" (Telegram deep links, t.me/<bot>?start=<parameter>) opens the
    // start form; the form itself reads the parameter after "/start".
    var lookupKey = FirstToken(key) == BotCommands.Start ? "/start" : key;

    var state = await GetStateAsync(chat);

    return await TryGetCommandAsync(chat, state, key) ?? await GetByTargetAsync(chat, state, TryGet(lookupKey));
  }

  async ValueTask<IForm> IFormRetriever.GetFormAsync(Chat chat, Type formType)
  {
    var state = await GetStateAsync(chat);

    // NavigateToAsync from inside a form follows the same rule as a keyboard press:
    // an already open target is rewound to, never stacked as a duplicate
    if (await RewindToAsync(state.History, formType) is { } target)
      return Wrap(chat, state, target, skipPush: true);

    return CreateNavigationWrapper(chat, state, formType);
  }

  /// <summary>
  /// The chat's live state, created empty on first contact. When a store is configured
  /// the first contact restores the stored snapshot under a per-chat gate, so two
  /// messages arriving together restore the history exactly once.
  /// </summary>
  private async Task<ChatState> GetStateAsync(Chat chat)
  {
    var state = _state.GetOrAdd(chat, _ => new ChatState(_history is null));

    if (state.Restored)
      return state;

    await state.Gate.WaitAsync();

    try
    {
      if (!state.Restored)
      {
        await _history!.RestoreAsync(chat.Id, state.History);
        state.Restored = true;
      }
    }
    finally
    {
      state.Gate.Release();
    }

    return state;
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
  private async Task<IForm?> TryGetCommandAsync(Chat chat, ChatState state, string key)
  {
    var firstToken = FirstToken(key);

    if (firstToken is null || firstToken.Length == 0 || !_commands.TryGetValue(firstToken, out var commandType))
      return null;

    var scope = _serviceScopeFactory.CreateScope();
    var command = (IBotCommand)scope.ServiceProvider.GetRequiredService(commandType);

    return Wrap(chat, state, new CommandFormAdapter(command, scope), skipPush: true);
  }

  private async Task<IForm?> GetByTargetAsync(Chat chat, ChatState state, object? formObj)
  {
    var history = state.History;

    // Nothing registered claims this text: the stateful form waiting in the history (a
    // wizard awaiting input, a paged list awaiting a search) takes it.
    if (formObj == null && history.TryGetLastForm() is IStatefulForm statefulForm)
      return Wrap(chat, state, statefulForm, skipPush: true);

    if (formObj is Type navigationType)
    {
      // Tapping the button of a form that is already open somewhere in the history
      // (double taps, retries after a lost reply, a card navigating back to its list)
      // must not stack a second identical screen - "Back" would paint the same form
      // once per copy. Rewind to the waiting instance instead: everything above it
      // leaves the history and the instance is Reset (a browser like PagedFormBase
      // returns to page one) and re-rendered in place.
      if (await RewindToAsync(history, navigationType) is { } target)
        return Wrap(chat, state, target, skipPush: true);

      return CreateNavigationWrapper(chat, state, navigationType);
    }

    if (formObj is BackForm backFormAction && await backFormAction(history) is { } backForm)
      return Wrap(chat, state, backForm, skipPush: true);

    return null;
  }

  /// <summary>Resolves a form for navigation: fresh DI scope, pushed onto the history unless it ignores it.</summary>
  private FormWrapper CreateNavigationWrapper(Chat chat, ChatState state, Type formType)
  {
    var form = CreateForm(formType);

    return Wrap(chat, state, form, form.IgnoreHistory);
  }

  private FormWrapper Wrap(Chat chat, ChatState state, IForm form, bool skipPush) =>
    new(form, chat, state.History, _localizer, skipPush, _history);

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

  /// <summary>
  /// One chat's runtime state: the live history stack plus the restore bookkeeping -
  /// <see cref="Restored"/> starts false when a history store exists and flips under
  /// <see cref="Gate"/> once the stored snapshot has been replayed onto the stack.
  /// </summary>
  private sealed class ChatState
  {
    public ChatState(bool restored) => Restored = restored;

    public ConcurrentStack<IForm> History { get; } = new();
    public SemaphoreSlim Gate { get; } = new(1, 1);

    public volatile bool Restored;
  }
}
