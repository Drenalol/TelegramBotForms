using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Drenalol.Telegram.Bot.Forms.Persistence;

/// <summary>
/// The persistence side of the router when an <see cref="IChatHistoryStore"/> is
/// registered: turns the live history stack into a snapshot after every rendered turn
/// and materializes the stack back from a snapshot on the chat's first contact.
/// Every store or form failure is contained here: an entry that cannot be restored is
/// skipped, and history never breaks message processing.
/// </summary>
internal sealed class ChatHistoryCoordinator
{
  private readonly IChatHistoryStore _store;
  private readonly IReadOnlyDictionary<string, Type> _formTypes;
  private readonly Func<Type, FormBase> _createForm;
  private readonly ILogger _logger;

  public ChatHistoryCoordinator(
    IChatHistoryStore store,
    IReadOnlyList<Type> registeredFormTypes,
    Func<Type, FormBase> createForm,
    ILogger logger
  )
  {
    _store = store;
    _createForm = createForm;
    _logger = logger;

    // a snapshot names forms by Type.FullName; renames are handled by skipping unknown
    // keys, duplicate registrations of one type simply map to themselves
    var types = new Dictionary<string, Type>(StringComparer.Ordinal);

    foreach (var formType in registeredFormTypes)
      types[formType.FullName ?? formType.Name] = formType;

    _formTypes = types;
  }

  /// <summary>
  /// Rebuilds the stack from the stored snapshot, bottom to top (entries are saved top
  /// first). Unknown form types and forms whose state no longer fits are skipped with a
  /// log; everything that still restores stays in the history.
  /// </summary>
  public async Task RestoreAsync(long chatId, ConcurrentStack<IForm> history, CancellationToken cancellationToken = default)
  {
    ChatHistorySnapshot? snapshot;

    try
    {
      snapshot = await _store.LoadAsync(chatId, cancellationToken);
    }
    catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
    {
      _logger.LogWarning(exception, "Chat {ChatId} history could not be loaded, the history starts empty", chatId);

      return;
    }

    if (snapshot?.Entries is not { Count: > 0 })
      return;

    foreach (var entry in snapshot.Entries.Reverse())
    {
      if (cancellationToken.IsCancellationRequested)
        return;

      if (!_formTypes.TryGetValue(entry.FormKey, out var formType))
      {
        _logger.LogInformation("Chat {ChatId} history entry {FormKey} is not a registered form anymore, skipped", chatId, entry.FormKey);

        continue;
      }

      FormBase? form = null;

      try
      {
        form = _createForm(formType);
        form.RestoreState(entry.State);
        history.Push(form);
        form = null;
      }
      catch (Exception exception)
      {
        _logger.LogWarning(exception, "Chat {ChatId} history entry {FormKey} failed to restore, skipped", chatId, entry.FormKey);

        if (form != null)
          await form.DisposeAsync();
      }
    }
  }

  /// <summary>Captures the stack (top first) and hands the snapshot to the store.</summary>
  public async Task PersistAsync(long chatId, ConcurrentStack<IForm> history, CancellationToken cancellationToken = default)
  {
    var entries = new List<FormHistoryEntry>();

    foreach (var form in history)
    {
      var formType = form.GetType();

      entries.Add(new FormHistoryEntry(formType.FullName ?? formType.Name, CaptureState(chatId, form)));
    }

    var snapshot = new ChatHistorySnapshot(chatId, DateTimeOffset.UtcNow, entries);

    try
    {
      await _store.SaveAsync(snapshot, cancellationToken);
    }
    catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
    {
      // the conversation position of one chat is not worth a broken turn: log and move on
      _logger.LogWarning(exception, "Chat {ChatId} history could not be saved", chatId);
    }
  }

  private string? CaptureState(long chatId, IForm form)
  {
    try
    {
      // every routed form is a FormBase; its own CaptureState defines what it persists
      return form is FormBase formBase ? formBase.CaptureState() : null;
    }
    catch (Exception exception)
    {
      _logger.LogWarning(exception, "Chat {ChatId} state of {FormKey} could not be captured, saved without it", chatId, form.GetType().Name);

      return null;
    }
  }
}
