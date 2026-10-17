using Telegram.Bot.Types;

namespace Drenalol.Telegram.Bot.Forms;

/// <summary>
/// Routes an incoming message to the form that should answer it: registered keys and
/// commands first, then the stateful form waiting in the chat's navigation history.
/// </summary>
public interface IFormRetriever
{
  public Task<IForm?> GetAsync(Chat chat, string key);

  internal IForm Get(Chat chat, Type formType);
}
