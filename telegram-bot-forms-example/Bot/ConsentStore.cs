using System.Collections.Concurrent;

namespace ExampleBot.Bot;

/// <summary>Per-chat consent in memory: "Back" re-enters a fresh start form, and a user who already accepted should not be asked again.</summary>
public sealed class ConsentStore
{
  private readonly ConcurrentDictionary<long, byte> _accepted = new();

  public bool IsAccepted(long chatId) => _accepted.ContainsKey(chatId);

  public void Accept(long chatId) => _accepted[chatId] = 0;
}
