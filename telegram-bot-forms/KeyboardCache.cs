using System.Collections.Concurrent;
using Telegram.Bot.Types.ReplyMarkups;

namespace Drenalol.Telegram.Bot.Forms;

/// <summary>
/// Process-wide cache of static keyboards, keyed by an arbitrary string (typically the
/// form type name), so identical keyboards are allocated once.
/// </summary>
public static class KeyboardCache
{
  private static readonly ConcurrentDictionary<string, ReplyKeyboardMarkup> Keyboards = new();

  public static ReplyKeyboardMarkup Get(string key) => Keyboards[key];

  public static void Add(string key, ReplyKeyboardMarkup keyboard) => Keyboards.TryAdd(key, keyboard);

  public static ReplyKeyboardMarkup GetOrAdd(string key, Func<ReplyKeyboardMarkup> keyboardFactory) =>
    Keyboards.GetOrAdd(key, _ => keyboardFactory());
}
