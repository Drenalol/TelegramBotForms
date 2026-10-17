using System.Collections.Concurrent;

namespace ExampleBot.Bot;

/// <summary>Fake per-chat time zone storage in memory (a user profile field would be the real store).</summary>
public sealed class TimeZoneStore
{
  public const string DefaultTimeZone = "UTC";

  private readonly ConcurrentDictionary<long, string> _zones = new();

  public string Get(long chatId) => _zones.GetOrAdd(chatId, _ => DefaultTimeZone);

  public void Set(long chatId, string id) => _zones[chatId] = id;
}
