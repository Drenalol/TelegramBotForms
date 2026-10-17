using System.Collections.Concurrent;

namespace ExampleBot.Bot;

/// <summary>Fake per-chat profile storage for the input-wizard demo.</summary>
public sealed class ProfileStore
{
  private readonly ConcurrentDictionary<long, string> _names = new();

  public string GetName(long chatId) => _names.GetOrAdd(chatId, _ => "Anonymous");

  public void SetName(long chatId, string name) => _names[chatId] = name;
}
