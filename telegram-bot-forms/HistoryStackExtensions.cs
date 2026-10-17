using System.Collections.Concurrent;

namespace Drenalol.Telegram.Bot.Forms;

internal static class HistoryStackExtensions
{
  public static IForm? TryGetLastForm(this ConcurrentStack<IForm> history) =>
    !history.IsEmpty && history.TryPeek(out var form)
      ? form
      : null;
}
