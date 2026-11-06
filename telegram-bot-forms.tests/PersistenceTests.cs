using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Drenalol.Telegram.Bot.Forms.Persistence;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Drenalol.Telegram.Bot.Forms.Tests;

/// <summary>
/// Restart is simulated as "build a second router over the same store": the first
/// provider (process) routes turns, the second one restores from what was written.
/// </summary>
[TestFixture]
public class PersistenceTests
{
  private MemoryChatHistoryStore _store = null!;

  [SetUp]
  public void SetUp() => _store = new MemoryChatHistoryStore();

  private IFormRetriever Router(Action<BotConfigurator>? extra = null) =>
    TestBot.Router(
      services => services.AddSingleton<IChatHistoryStore>(_store),
      configurator =>
      {
        configurator
          .RegisterStartForm<StartStub>()
          .RegisterFormWithKey<MenuStub>("Menu")
          .RegisterFormWithKey<DetailStub>("Detail")
          .RegisterFormWithKey<NoHistoryStub>("NoHistory")
          .RegisterFormWithKey<WizardStub>("Wizard")
          .RegisterFormWithKey<PersistentWizardStub>("Persistent")
          .RegisterFormWithKey<PagedStub>("Paged");

        extra?.Invoke(configurator);
      });

  [Test]
  public async Task Back_Returns_To_The_Previous_Form_After_A_Restart()
  {
    var chat = TestBot.Chat();

    var before = Router();
    await before.RenderAsync(chat, "Menu");
    await before.RenderAsync(chat, "Detail");

    var after = Router();
    var back = await after.RenderAsync(chat, "Back");

    Assert.That(back!.ReplyText, Is.EqualTo("menu"));
  }

  [Test]
  public async Task Snapshot_Keeps_The_Stack_Top_First()
  {
    var chat = TestBot.Chat();

    var router = Router();
    await router.RenderAsync(chat, "Menu");
    await router.RenderAsync(chat, "Detail");

    var keys = _store.Load(chat.Id)!.Entries.Select(entry => entry.FormKey).ToArray();

    Assert.That(keys, Is.EqualTo(new[] { typeof(DetailStub).FullName, typeof(MenuStub).FullName }));
  }

  [Test]
  public async Task IgnoringHistory_Form_Clears_The_Persisted_Snapshot_Too()
  {
    var chat = TestBot.Chat();

    var router = Router();
    await router.RenderAsync(chat, "Menu");
    await router.RenderAsync(chat, "NoHistory");

    Assert.That(_store.Load(chat.Id)!.Entries, Is.Empty);
  }

  [Test]
  public async Task Wizard_State_Awaits_Input_Again_After_A_Restart()
  {
    var chat = TestBot.Chat();

    var before = Router();
    await before.RenderAsync(chat, "Wizard");
    await before.RenderAsync(chat, "edit");

    // the wizard was waiting for free text: after the restart the next message is its
    // answer, not a fresh init
    var after = Router();
    var context = await after.RenderAsync(chat, "free input");

    Assert.That(context!.ReplyText, Is.EqualTo("wizard-saved:free input"));
  }

  [Test]
  public async Task Captured_Context_Survives_Any_Number_Of_Restarts()
    {
    var chat = TestBot.Chat();

    var first = Router();
    await first.RenderAsync(chat, "Persistent");
    await first.RenderAsync(chat, "edit");
    await first.RenderAsync(chat, "Bob");

    var second = Router();
    Assert.That((await second.RenderAsync(chat, "show"))!.ReplyText, Is.EqualTo("pw-show:Bob"));

    await second.RenderAsync(chat, "edit");
    await second.RenderAsync(chat, "Kate");

    var third = Router();
    Assert.That((await third.RenderAsync(chat, "show"))!.ReplyText, Is.EqualTo("pw-show:Kate"));
  }

  [Test]
  public async Task Paged_Browser_Restores_Page_And_Localized_Navigation()
  {
    var chat = TestBot.Chat();

    var before = Router();
    await before.RenderAsync(chat, "Paged");
    await before.RenderAsync(chat, "Next ▶");

    var after = Router();

    // the restored browser re-armed the localized "◀ Prev" button: it walks back to page 1
    var context = await after.RenderAsync(chat, "◀ Prev");

    Assert.That(context!.ReplyText, Is.EqualTo("page 1/2 count 5"));
  }

  [Test]
  public async Task Tapping_A_Stale_Row_Repaints_The_Restored_Page_Then_Works()
  {
    var chat = TestBot.Chat();

    var before = Router();
    await before.RenderAsync(chat, "Paged");
    await before.RenderAsync(chat, "Next ▶");

    var after = Router();

    // page 2's labels were rendered before the restart: the first tap repaints the
    // restored page (it does not leak into the search), the second tap opens the item
    var repaint = await after.RenderAsync(chat, "I6");
    Assert.That(repaint!.ReplyText, Is.EqualTo("page 2/2 count 5"));

    var item = await after.RenderAsync(chat, "I6");
    Assert.That(item!.ReplyText, Is.EqualTo("item:6"));
  }

  [Test]
  public async Task Unknown_Form_Key_Is_Skipped_While_The_Rest_Stays()
  {
    var chat = TestBot.Chat();

    _store.Save(chat.Id, new ChatHistorySnapshot(
      chat.Id,
      DateTimeOffset.UtcNow,
      [
        new("WasRemoved.OrphanedForm", null),
        new(typeof(DetailStub).FullName!, null),
      ]));

    var router = Router();
    var context = await router.RenderAsync(chat, "Back");

    // the orphan left the history, Detail became the current form and "Back" popped it
    Assert.That(context!.ReplyText, Is.EqualTo("start"));
  }

  [Test]
  public async Task History_Order_Restores_Top_First()
  {
    var chat = TestBot.Chat();

    _store.Save(chat.Id, new ChatHistorySnapshot(
      chat.Id,
      DateTimeOffset.UtcNow,
      [
        new(typeof(MenuStub).FullName!, null),
        new(typeof(DetailStub).FullName!, null),
      ]));

    var router = Router();
    var back = await router.RenderAsync(chat, "Back");

    // Menu was the top of the restored stack, Detail the one under it
    Assert.That(back!.ReplyText, Is.EqualTo("detail"));
  }

  [Test]
  public async Task File_Store_RoundTrips_Erases_Expired_And_Damaged_Entries()
  {
    var directory = Path.Combine(TestContext.CurrentContext.TestDirectory, "history-files", Guid.NewGuid().ToString("N"));
    var store = new FileChatHistoryStore(new FileChatHistoryStoreOptions { Directory = directory, TimeToLive = TimeSpan.FromDays(7) });
    var snapshot = new ChatHistorySnapshot(42, DateTimeOffset.UtcNow, [new("Some.Form", "{\"state\":\"X\"}")]);

    await store.SaveAsync(snapshot);
    var loaded = await store.LoadAsync(42);

    Assert.That(loaded!.ChatId, Is.EqualTo(42));
    Assert.That(loaded.Entries.Select(entry => (entry.FormKey, entry.State)), Is.EquivalentTo([(snapshot.Entries[0].FormKey, snapshot.Entries[0].State)]));

    // a snapshot written past the TTL is dropped on load, and with it the file
    await store.SaveAsync(snapshot with { SavedAt = DateTimeOffset.UtcNow.AddDays(-8) });
    Assert.That(await store.LoadAsync(42), Is.Null);
    Assert.That(File.Exists(Path.Combine(directory, "42.json")), Is.False);

    // damaged JSON is a lost history, never a throwing store
    await File.WriteAllTextAsync(Path.Combine(directory, "43.json"), "{ not json");
    Assert.That(await store.LoadAsync(43), Is.Null);
  }

  [Test]
  public async Task Distributed_Cache_Store_RoundTrips_Under_The_Configured_Prefix()
  {
    var cache = new MemoryDistributedCache();
    var store = new DistributedCacheChatHistoryStore(cache, new ChatHistoryPersistenceOptions { KeyPrefix = "bot:", TimeToLive = TimeSpan.FromHours(1) });
    var snapshot = new ChatHistorySnapshot(7, DateTimeOffset.UtcNow, [new("Some.Form", null)]);

    await store.SaveAsync(snapshot);
    var loaded = await store.LoadAsync(7);

    Assert.That(cache.Keys.Single(), Is.EqualTo("bot:7"));
    Assert.That(loaded!.Entries.Single().FormKey, Is.EqualTo("Some.Form"));

    Assert.That(await store.LoadAsync(8), Is.Null);
  }

  [Test]
  public async Task Router_Without_A_Store_Behaves_As_Without_Persistence()
  {
    var chat = TestBot.Chat();

    var before = TestBot.Router(configurator => configurator
      .RegisterStartForm<StartStub>()
      .RegisterFormWithKey<MenuStub>("Menu"));
    await before.RenderAsync(chat, "Menu");

    var after = TestBot.Router(configurator => configurator
      .RegisterStartForm<StartStub>()
      .RegisterFormWithKey<MenuStub>("Menu"));
    var back = await after.RenderAsync(chat, "Back");

    Assert.That(back!.ReplyText, Is.EqualTo("start"));
  }
}

// --- persistence stubs ----------------------------------------------------------------

internal enum PersistentWizardState
{
  Show,
  WaitName,
}

internal sealed record PersistentWizardContext(string? Name);

/// <summary>A wizard that declares its own data for persistence through CaptureContext.</summary>
internal sealed class PersistentWizardStub : ExtendedFormBase<PersistentWizardState>
{
  private readonly Dictionary<string, PersistentWizardState> _buttons = new(StringComparer.OrdinalIgnoreCase);

  public PersistentWizardStub()
  {
    AddRenderOn(PersistentWizardState.Show, string.Empty, ShowAsync);
    AddRenderOn(PersistentWizardState.WaitName, string.Empty, WaitAsync);

    _buttons["edit"] = PersistentWizardState.WaitName;
    _buttons["show"] = PersistentWizardState.Show;
  }

  public string? SavedName { get; private set; }

  protected override Task<MessageContext> RenderInitAsync(MessageContext context, CancellationToken cancellationToken) =>
    Task.FromResult(context.Reply("pw-init"));

  protected override IRender GetRender(MessageContext context)
  {
    if (_buttons.TryGetValue(context.Text, out var state))
      return state == PersistentWizardState.WaitName ? CustomRender(WaitAsync) : CustomRender(ShowAsync);

    if (State == PersistentWizardState.WaitName)
      return CustomRender(SaveAsync);

    throw new InvalidOperationException($"Nothing routes the text: {context.Text}");
  }

  protected override object? CaptureContext() => new PersistentWizardContext(SavedName);

  protected override void RestoreContext(JsonNode context) =>
    SavedName = context.Deserialize<PersistentWizardContext>()?.Name;

  private Task<MessageContext> ShowAsync(MessageContext context, CancellationToken cancellationToken) =>
    Task.FromResult(context.Reply($"pw-show:{SavedName ?? "none"}"));

  private Task<MessageContext> WaitAsync(MessageContext context, CancellationToken cancellationToken)
  {
    State = PersistentWizardState.WaitName;

    return Task.FromResult(context.Reply("pw-waiting"));
  }

  private Task<MessageContext> SaveAsync(MessageContext context, CancellationToken cancellationToken)
  {
    SavedName = context.Text;
    State = PersistentWizardState.Show;

    return Task.FromResult(context.Reply($"pw-saved:{context.Text}"));
  }
}

// --- stores ---------------------------------------------------------------------------

/// <summary>In-process history store: records every saved snapshot like a real store would.</summary>
internal sealed class MemoryChatHistoryStore : IChatHistoryStore
{
  private readonly ConcurrentDictionary<long, ChatHistorySnapshot> _snapshots = new();

  public Task<ChatHistorySnapshot?> LoadAsync(long chatId, CancellationToken cancellationToken = default) =>
    Task.FromResult(_snapshots.GetValueOrDefault(chatId));

  public Task SaveAsync(ChatHistorySnapshot snapshot, CancellationToken cancellationToken = default)
  {
    _snapshots[snapshot.ChatId] = snapshot;

    return Task.CompletedTask;
  }

  public ChatHistorySnapshot? Load(long chatId) => _snapshots.GetValueOrDefault(chatId);

  public void Save(long chatId, ChatHistorySnapshot snapshot) => _snapshots[chatId] = snapshot;
}

/// <summary>Minimal in-memory IDistributedCache for the distributed store tests.</summary>
internal sealed class MemoryDistributedCache : IDistributedCache
{
  private readonly ConcurrentDictionary<string, byte[]> _entries = new();

  public IEnumerable<string> Keys => _entries.Keys;

  public byte[]? Get(string key) => _entries.GetValueOrDefault(key);

  public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => Task.FromResult(Get(key));

  public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => _entries[key] = value;

  public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
  {
    Set(key, value, options);

    return Task.CompletedTask;
  }

  public void Refresh(string key)
  {
  }

  public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;

  public void Remove(string key) => _entries.TryRemove(key, out _);

  public Task RemoveAsync(string key, CancellationToken token = default)
  {
    Remove(key);

    return Task.CompletedTask;
  }
}
