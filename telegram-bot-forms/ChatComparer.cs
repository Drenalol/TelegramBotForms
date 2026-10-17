using Telegram.Bot.Types;

namespace Drenalol.Telegram.Bot.Forms;

/// <summary>
/// Chats are identified by their id: the same chat seen through different
/// <see cref="Chat"/> instances shares one navigation history.
/// </summary>
internal sealed class ChatComparer : IEqualityComparer<Chat>
{
  public static readonly ChatComparer Instance = new();

  public bool Equals(Chat? x, Chat? y) => x?.Id == y?.Id;

  public int GetHashCode(Chat obj) => obj.Id.GetHashCode();
}
