namespace Drenalol.Telegram.Bot.Forms;

/// <summary>
/// A free-text bot command, matched by its prefix (the first whitespace-separated
/// token of the message, with or without a leading "/"), e.g. "link abc-123".
/// Commands win over keyboard keys and over the form waiting in the chat history.
/// </summary>
public interface IBotCommand
{
  string Prefix { get; }

  Task<MessageContext> ExecuteAsync(MessageContext context, CancellationToken cancellationToken);
}
