using System.Collections.Concurrent;

namespace Drenalol.Telegram.Bot.Forms;

/// <summary>Picks the form the "Back" button should re-render from the chat's history.</summary>
internal delegate IForm? BackForm(ConcurrentStack<IForm> history);
