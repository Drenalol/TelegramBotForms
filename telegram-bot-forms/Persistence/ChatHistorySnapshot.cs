namespace Drenalol.Telegram.Bot.Forms.Persistence;

/// <summary>
/// A serializable picture of one chat's navigation history: the forms the user visited,
/// top (current) first, each with the state the form itself agreed to carry across a
/// restart (<see cref="FormBase.CaptureState"/>). The store keeps the latest snapshot
/// per chat; on first contact after a restart the router materializes the live stack
/// from it.
/// </summary>
/// <param name="ChatId">The chat the history belongs to.</param>
/// <param name="SavedAt">When the snapshot was written; drives store-side expiry.</param>
/// <param name="Entries">Visited forms, top of the stack (the current form) first.</param>
/// <param name="Version">Snapshot format version; readers must accept older versions.</param>
public sealed record ChatHistorySnapshot(
  long ChatId,
  DateTimeOffset SavedAt,
  IReadOnlyList<FormHistoryEntry> Entries,
  int Version = 1
);

/// <summary>
/// One history entry: <see cref="FormKey"/> identifies the form type by its
/// <see cref="Type.FullName"/> (an unknown key - a renamed or removed form - is skipped
/// on restore), <see cref="State"/> is the form's own JSON written by
/// <see cref="FormBase.CaptureState"/>.
/// </summary>
public sealed record FormHistoryEntry(string FormKey, string? State);
