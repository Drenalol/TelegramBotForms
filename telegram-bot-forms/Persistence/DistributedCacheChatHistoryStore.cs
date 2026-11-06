using System.Globalization;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace Drenalol.Telegram.Bot.Forms.Persistence;

/// <summary>
/// The distributed-cache store: one key per chat
/// (<c>{KeyPrefix}{chatId}</c>) holding the JSON snapshot, expiring after
/// <see cref="ChatHistoryPersistenceOptions.TimeToLive"/> and renewed on every saved
/// turn. Works with any <see cref="IDistributedCache"/>: Redis through
/// <c>AddStackExchangeRedisCache</c>, in-memory through <c>AddDistributedMemoryCache</c>,
/// SQL, or your own.
/// <para>
/// The cache semantics fit the history exactly: a flushed or evicted snapshot only costs
/// the chat its "Back", the same degradation as a restart without persistence.
/// </para>
/// </summary>
public sealed class DistributedCacheChatHistoryStore : IChatHistoryStore
{
  private readonly IDistributedCache _cache;
  private readonly string _keyPrefix;
  private readonly TimeSpan _timeToLive;
  private readonly ILogger? _logger;

  public DistributedCacheChatHistoryStore(IDistributedCache cache, ChatHistoryPersistenceOptions options, ILogger? logger = null)
  {
    _cache = cache;
    _keyPrefix = options.KeyPrefix;
    _timeToLive = options.TimeToLive;
    _logger = logger;
  }

  public async Task<ChatHistorySnapshot?> LoadAsync(long chatId, CancellationToken cancellationToken = default)
  {
    try
    {
      var bytes = await _cache.GetAsync(Key(chatId), cancellationToken);

      return bytes is null or { Length: 0 } ? null : ChatHistoryCodec.FromUtf8(bytes);
    }
    catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
    {
      // an unreachable cache is a lost history, never a broken bot
      _logger?.LogWarning(exception, "Chat {ChatId} history could not be loaded, the history starts empty", chatId);

      return null;
    }
  }

  public Task SaveAsync(ChatHistorySnapshot snapshot, CancellationToken cancellationToken = default) =>
    _cache.SetAsync(
      Key(snapshot.ChatId),
      ChatHistoryCodec.ToUtf8(snapshot),
      new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = _timeToLive },
      cancellationToken
    );

  private string Key(long chatId) => _keyPrefix + chatId.ToString(CultureInfo.InvariantCulture);
}
