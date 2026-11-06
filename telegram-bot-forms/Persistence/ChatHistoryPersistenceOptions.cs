namespace Drenalol.Telegram.Bot.Forms.Persistence;

/// <summary>Options of a chat-history store (see <see cref="IChatHistoryStore"/>).</summary>
public class ChatHistoryPersistenceOptions
{
  /// <summary>
  /// How long a saved history may live without being rewritten; every rendered turn
  /// renews it. An expired (or lost) history only costs the chat its "Back".
  /// </summary>
  public TimeSpan TimeToLive { get; set; } = TimeSpan.FromDays(7);

  /// <summary>Prefix of the keys the store writes (distributed-cache stores only).</summary>
  public string KeyPrefix { get; set; } = "tgbf:history:";
}

/// <summary>Options of <see cref="FileChatHistoryStore"/>.</summary>
public class FileChatHistoryStoreOptions : ChatHistoryPersistenceOptions
{
  /// <summary>
  /// Directory holding one JSON file per chat (<c>{Directory}/{chatId}.json</c>).
  /// Local to the process: a file store cannot serve more than the one bot instance
  /// that writes it (use a distributed cache store when several replicas run).
  /// </summary>
  public string Directory { get; set; } = "telegram-bot-forms-history";
}
