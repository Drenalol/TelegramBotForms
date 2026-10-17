using Drenalol.Telegram.Bot.Forms.Localization;
using Microsoft.Extensions.DependencyInjection;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace Drenalol.Telegram.Bot.Forms.Tests;

/// <summary>Router-level helpers: build a service provider with registered forms and drive chats through it.</summary>
internal static class TestBot
{
  public static ServiceProvider BuildProvider(Action<BotConfigurator> register) => BuildProvider(_ => { }, register);

  public static ServiceProvider BuildProvider(Action<IServiceCollection> beforeFramework, Action<BotConfigurator> register)
  {
    var services = new ServiceCollection();
    services.AddLogging();
    beforeFramework(services);

    var configurator = services.AddTelegramBotForms(options => options.Token = "test-token");
    register(configurator);
    configurator.Build();

    return services.BuildServiceProvider();
  }

  public static IFormRetriever Router(Action<BotConfigurator> register) =>
    BuildProvider(register).GetRequiredService<IFormRetriever>();

  public static IFormRetriever Router(Action<IServiceCollection> beforeFramework, Action<BotConfigurator> register) =>
    BuildProvider(beforeFramework, register).GetRequiredService<IFormRetriever>();

  public static Chat Chat(long id = 1) => new() { Id = id };

  /// <summary>Routes <paramref name="text"/> as the chat and renders the resolved form; null when nothing routes it.</summary>
  public static async Task<MessageContext?> RenderAsync(this IFormRetriever retriever, Chat chat, string text)
  {
    var form = await retriever.GetAsync(chat, text);

    return form is null ? null : await form.RenderAsync(new MessageContext(chat, text), CancellationToken.None);
  }

  public static string[] Labels(this MessageContext context) =>
    (context.ReplyMarkup as ReplyKeyboardMarkup)?.Keyboard.SelectMany(row => row.Select(button => button.Text ?? string.Empty)).ToArray()
    ?? [];
}

// --- form stubs ---------------------------------------------------------------------

internal sealed class StartStub : FormBase
{
  public override bool ClearHistoryOnNavigate => true;

  public override Task<MessageContext> RenderAsync(MessageContext context, CancellationToken cancellationToken) =>
    Task.FromResult(context.Reply("start"));
}

internal sealed class MenuStub : FormBase
{
  public override Task<MessageContext> RenderAsync(MessageContext context, CancellationToken cancellationToken) =>
    Task.FromResult(context.Reply("menu"));
}

/// <summary>A root form that clears the history on navigation but (unlike StartForm) is pushed onto it.</summary>
internal sealed class RootClearStub : FormBase
{
  public override bool ClearHistoryOnNavigate => true;

  public override async Task<MessageContext> RenderAsync(MessageContext context, CancellationToken cancellationToken)
  {
    // touches the owned DI scope: renders as an error reply once the scope is disposed
    var language = await GetLanguageAsync(context.Chat, cancellationToken);

    return context.Reply($"root:{language}");
  }
}

/// <summary>Tracks its instances and whether the DI scope they own is still alive.</summary>
internal sealed class ProbeStub : FormBase
{
  public static readonly List<ProbeStub> Created = [];

  public ProbeStub() => Created.Add(this);

  public bool ScopeAlive
  {
    get
    {
      try
      {
        _ = Services.GetRequiredService<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>();

        return true;
      }
      catch (ObjectDisposedException)
      {
        return false;
      }
    }
  }

  public override Task<MessageContext> RenderAsync(MessageContext context, CancellationToken cancellationToken) =>
    Task.FromResult(context.Reply("probe"));
}

internal sealed class DetailStub : FormBase
{
  public override Task<MessageContext> RenderAsync(MessageContext context, CancellationToken cancellationToken) =>
    Task.FromResult(context.Reply("detail"));
}

/// <summary>Root-form pattern (like StartForm): never pushed onto the history, clears it on entry.</summary>
internal sealed class NoHistoryStub : FormBase
{
  public override bool ClearHistoryOnNavigate => true;
  public override bool IgnoreHistory => true;

  public override Task<MessageContext> RenderAsync(MessageContext context, CancellationToken cancellationToken) =>
    Task.FromResult(context.Reply("no-history"));
}

internal sealed class BoomStub : FormBase
{
  public override Task<MessageContext> RenderAsync(MessageContext context, CancellationToken cancellationToken) =>
    throw new InvalidOperationException("boom");
}

internal sealed class HelpStub : IBotCommand
{
  public string Prefix => "help";

  public Task<MessageContext> ExecuteAsync(MessageContext context, CancellationToken cancellationToken) =>
    Task.FromResult(context.Reply($"help:{context.Text}"));
}

internal enum WizardState
{
  Show,
  WaitName,
  Finish,
  GoStart,
}

/// <summary>A wizard: button-routed states plus a free-text input state, a "finish" that completes it and a navigation to the start form.</summary>
internal sealed class WizardStub : ExtendedFormBase<WizardState>
{
  private readonly Dictionary<string, WizardState> _buttons = new(StringComparer.OrdinalIgnoreCase);

  public string? SavedName { get; private set; }

  public WizardStub()
  {
    AddRenderOn(WizardState.Show, string.Empty, ShowAsync);
    AddRenderOn(WizardState.WaitName, string.Empty, WaitAsync);
    AddRenderOn(WizardState.Finish, string.Empty, FinishAsync);
    AddFormRenderOn<StartStub>(WizardState.GoStart);

    _buttons["edit"] = WizardState.WaitName;
    _buttons["finish"] = WizardState.Finish;
    _buttons["to-start"] = WizardState.GoStart;
  }

  protected override Task<MessageContext> RenderInitAsync(MessageContext context, CancellationToken cancellationToken) =>
    Task.FromResult(context.Reply("wizard-init"));

  protected override IRender GetRender(MessageContext context)
  {
    if (_buttons.TryGetValue(context.Text, out var state))
      return Renders[state];

    if (State == WizardState.WaitName)
      return CustomRender(SaveAsync);

    throw new InvalidOperationException($"Nothing routes the text: {context.Text}");
  }

  private Task<MessageContext> ShowAsync(MessageContext context, CancellationToken cancellationToken)
  {
    State = WizardState.Show;

    return Task.FromResult(context.Reply("wizard-show"));
  }

  private Task<MessageContext> WaitAsync(MessageContext context, CancellationToken cancellationToken)
  {
    State = WizardState.WaitName;

    return Task.FromResult(context.Reply("wizard-waiting"));
  }

  private Task<MessageContext> SaveAsync(MessageContext context, CancellationToken cancellationToken)
  {
    SavedName = context.Text;
    State = WizardState.Show;

    return Task.FromResult(context.Reply($"wizard-saved:{context.Text}"));
  }

  private Task<MessageContext> FinishAsync(MessageContext context, CancellationToken cancellationToken)
  {
    IsCompleted = true;

    return Task.FromResult(context.Reply("wizard-finished"));
  }
}

internal sealed record TestItem(int Id, string Name);

/// <summary>A paged browser over a fixed in-memory list (two duplicate names on the first page).</summary>
internal sealed class PagedStub : PagedFormBase<TestItem>
{
  private static readonly List<TestItem> Items =
  [
    new(1, "Dup"), new(2, "Dup"), new(3, "I3"), new(4, "I4"), new(5, "I5"),
    new(6, "I6"), new(7, "I7"), new(8, "I8"), new(9, "I9"), new(10, "I10"),
  ];

  protected override int PageSize => 5;

  protected override Task<PagedPage<TestItem>> LoadPageAsync(string? search, int page, int pageSize, CancellationToken cancellationToken)
  {
    var filtered = string.IsNullOrWhiteSpace(search)
      ? Items
      : Items.Where(item => item.Name.Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();

    return Task.FromResult(new PagedPage<TestItem>(filtered.Skip((page - 1) * pageSize).Take(pageSize).ToList(), filtered.Count));
  }

  protected override string EncodeRow(TestItem item) => item.Name;

  protected override Task<MessageContext> RenderItemAsync(TestItem item, MessageContext context, CancellationToken cancellationToken) =>
    Task.FromResult(context.Reply($"item:{item.Id}"));

  protected override Task<string> BuildTextAsync(IReadOnlyList<TestItem> items, int page, int totalPages, CancellationToken cancellationToken) =>
    Task.FromResult($"page {page}/{totalPages} count {items.Count}");

  protected override Task BuildExtraRowsAsync(IReadOnlyList<TestItem> items, MessageContext context, CancellationToken cancellationToken)
  {
    AddActionRow("refresh", RefreshAsync);

    return Task.CompletedTask;
  }

  private Task<MessageContext> RefreshAsync(MessageContext context, CancellationToken cancellationToken) =>
    RenderCurrentPageAsync(context, cancellationToken);
}

/// <summary>Chat language stub: answers in Russian for chat 99, English elsewhere.</summary>
internal sealed class RussianFor99Resolver : IChatLanguageResolver
{
  public Task<string> ResolveAsync(Chat chat, CancellationToken cancellationToken = default) =>
    Task.FromResult(chat.Id == 99 ? DefaultTextLocalizer.Russian : DefaultTextLocalizer.English);
}
