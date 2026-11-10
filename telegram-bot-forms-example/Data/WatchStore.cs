using System.Collections.Concurrent;

namespace ExampleBot.Data;

/// <summary>
/// In-memory "watching this product" flags per chat: the state <c>WatchCommand</c> flips
/// and its ephemeral confirmation talks about. Being stored here, the answer to the tap is
/// worth showing ephemerally - the fact itself survives the message.
/// </summary>
public sealed class WatchStore
{
  private readonly ConcurrentDictionary<long, ConcurrentDictionary<int, byte>> _watched = new();

  /// <summary>Flips the flag of one product in one chat and returns the new value.</summary>
  public bool Toggle(long chatId, int productId)
  {
    var products = _watched.GetOrAdd(chatId, _ => new ConcurrentDictionary<int, byte>());

    if (products.TryRemove(productId, out _))
      return false;

    products[productId] = 0;

    return true;
  }

  public bool IsWatched(long chatId, int productId) =>
    _watched.TryGetValue(chatId, out var products) && products.ContainsKey(productId);
}
