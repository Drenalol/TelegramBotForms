namespace Drenalol.Telegram.Bot.Forms.Persistence;

/// <summary>
/// Stores the navigation history of a chat so it survives a restart: the router loads
/// the latest snapshot of a chat on first contact and saves it after every rendered
/// turn. Plug in one of the built-in stores
/// (<see cref="FileChatHistoryStore"/>, <see cref="DistributedCacheChatHistoryStore"/>)
/// with <see cref="BotConfigurator.UseFileHistory"/> /
/// <see cref="BotConfigurator.UseDistributedHistory"/>, or register your own
/// <see cref="IChatHistoryStore"/> implementation as a singleton.
/// <para>
/// A store is a cache of the conversation position, not a source of truth: every
/// failure (load or save) must be swallowed by the framework and only costs the chat
/// its "Back" - the bot keeps answering.
/// </para>
/// </summary>
public interface IChatHistoryStore
{
  /// <summary>Returns the latest snapshot of the chat, null when there is none (or it expired).</summary>
  Task<ChatHistorySnapshot?> LoadAsync(long chatId, CancellationToken cancellationToken = default);

  /// <summary>Replaces the snapshot of the chat (last write wins).</summary>
  Task SaveAsync(ChatHistorySnapshot snapshot, CancellationToken cancellationToken = default);
}
