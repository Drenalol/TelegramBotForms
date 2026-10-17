using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;

namespace Drenalol.Telegram.Bot.Forms;

/// <summary>
/// The bot's two message buses: <see cref="Income"/> carries updates arriving from
/// Telegram, <see cref="Outcome"/> carries rendered replies waiting to be sent.
/// </summary>
public sealed class TelegramBotBus
{
  public Bus<Update> Income { get; }
  public Bus<MessageContext> Outcome { get; }

  public TelegramBotBus(ILogger<TelegramBotBus> logger)
  {
    Income = new Bus<Update>(logger);
    Outcome = new Bus<MessageContext>(logger);
  }
}
