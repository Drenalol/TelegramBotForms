namespace Drenalol.Telegram.Bot.Forms;

/// <summary>
/// A single conversational surface of the bot: one chat message (or button press)
/// arrives, the form renders a reply. An instance serves one conversation step;
/// stateful forms live longer and are re-entered through the chat history.
/// </summary>
public interface IForm
{
  /// <summary>When navigating to this form, the chat's navigation history is emptied first.</summary>
  public bool ClearHistoryOnNavigate => false;

  /// <summary>How long a single render may take before it is cancelled.</summary>
  public int TimeoutSec => 30;

  public Task<MessageContext> RenderAsync(MessageContext context, CancellationToken cancellationToken);
}
