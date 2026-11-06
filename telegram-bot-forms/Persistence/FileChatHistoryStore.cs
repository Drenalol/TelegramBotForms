using Microsoft.Extensions.Logging;

namespace Drenalol.Telegram.Bot.Forms.Persistence;

/// <summary>
/// The built-in file store: one JSON file per chat (<c>{Directory}/{chatId}.json</c>),
/// written atomically (temp file + move), so a crash can never leave a half-written
/// snapshot. Files older than <see cref="ChatHistoryPersistenceOptions.TimeToLive"/>
/// are swept once per process on the first store operation.
/// <para>
/// Single-instance by design: the bot runs one poller per token anyway, and a local
/// directory is not shared between replicas - run several instances behind a
/// distributed cache store instead.
/// </para>
/// </summary>
public sealed class FileChatHistoryStore : IChatHistoryStore
{
  private const string Extension = ".json";

  private readonly string _directory;
  private readonly TimeSpan _timeToLive;
  private readonly ILogger? _logger;

  private int _swept;

  public FileChatHistoryStore(FileChatHistoryStoreOptions options, ILogger? logger = null)
  {
    if (string.IsNullOrWhiteSpace(options.Directory))
      throw new InvalidOperationException(
        "Drenalol.Telegram.Bot.Forms: FileChatHistoryStoreOptions.Directory must be configured.");

    _directory = options.Directory;
    _timeToLive = options.TimeToLive;
    _logger = logger;
  }

  public async Task<ChatHistorySnapshot?> LoadAsync(long chatId, CancellationToken cancellationToken = default)
  {
    Sweep();

    var path = PathFor(chatId);

    if (!File.Exists(path))
      return null;

    try
    {
      var json = await File.ReadAllTextAsync(path, cancellationToken);
      var snapshot = ChatHistoryCodec.Deserialize(json);

      if (snapshot != null && IsExpired(snapshot.SavedAt))
      {
        TryDelete(path);

        return null;
      }

      return snapshot;
    }
    catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
    {
      // a damaged or half-written file is a lost history, never a broken bot
      _logger?.LogWarning(exception, "Chat {ChatId} history file is unreadable, the history starts empty", chatId);
      TryDelete(path);

      return null;
    }
  }

  public async Task SaveAsync(ChatHistorySnapshot snapshot, CancellationToken cancellationToken = default)
  {
    Sweep();

    Directory.CreateDirectory(_directory);

    var path = PathFor(snapshot.ChatId);
    var tempPath = Path.Combine(_directory, $"{snapshot.ChatId}.{Guid.NewGuid():N}.tmp");

    await File.WriteAllBytesAsync(tempPath, ChatHistoryCodec.ToUtf8(snapshot), cancellationToken);

    // same-directory replacement stays a single atomic rename: Replace over an
    // existing snapshot, Move when this chat is saved for the first time
    if (File.Exists(path))
      File.Replace(tempPath, path, null);
    else
      File.Move(tempPath, path);
  }

  private string PathFor(long chatId) => Path.Combine(_directory, $"{chatId}{Extension}");

  private bool IsExpired(DateTimeOffset savedAt) => DateTimeOffset.UtcNow - savedAt > _timeToLive;

  private void Sweep()
  {
    if (Interlocked.Exchange(ref _swept, 1) == 1 || !Directory.Exists(_directory))
      return;

    try
    {
      foreach (var path in Directory.EnumerateFiles(_directory))
        if (IsExpired(new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero)))
          TryDelete(path);
    }
    catch (Exception exception)
    {
      _logger?.LogWarning(exception, "Chat history directory {Directory} could not be swept", _directory);
    }
  }

  private void TryDelete(string path)
  {
    try
    {
      File.Delete(path);
    }
    catch (Exception exception)
    {
      _logger?.LogWarning(exception, "Chat history file {Path} could not be deleted", path);
    }
  }
}
