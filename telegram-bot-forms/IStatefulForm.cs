namespace Drenalol.Telegram.Bot.Forms;

/// <summary>
/// A form that carries conversation state across messages (a wizard, a paged list).
/// The per-chat history keeps such instances alive between turns; "Back" skips
/// completed ones and <see cref="Reset"/>s the rest before re-entering them.
/// </summary>
public interface IStatefulForm : IForm
{
  bool IsCompleted { get; }
  void Reset();
}
