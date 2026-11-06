using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace Drenalol.Telegram.Bot.Forms;

/// <summary>The incoming side of a conversation turn: who wrote and what.</summary>
public interface IInputContext
{
  public Chat Chat { get; }
  public string Text { get; }

  /// <summary>The shared location of a location message, null for any other incoming message.</summary>
  public Location? Location { get; }

  /// <summary>The shared contact of a contact message, null for any other incoming message.</summary>
  public Contact? Contact { get; }
}

/// <summary>The outgoing side of a conversation turn: what the bot answers.</summary>
public interface IOutputContext
{
  public string? ReplyText { get; }
  public ParseMode? ParseMode { get; }
  public IEnumerable<MessageEntity>? Entities { get; }
  public int? ReplyToMessageId { get; }
  public ReplyMarkup? ReplyMarkup { get; }
}

/// <summary>An exception captured while rendering, logged by the bot host.</summary>
public interface IErrorContext
{
  Exception? Exception { get; }
}

/// <summary>
/// One conversation turn: the incoming chat message (or a replayed callback), mutated
/// in place by the form into the outgoing reply via <see cref="Reply(string?,ParseMode?,IEnumerable{MessageEntity}?,int?,ReplyMarkup?,Exception?)"/>
/// and friends. The host sends <see cref="ReplyText"/> (with the markup and entities)
/// back to the chat.
/// </summary>
public class MessageContext : IInputContext, IOutputContext, IErrorContext
{
  public Chat Chat { get; }
  public string Text { get; private set; }

  /// <inheritdoc cref="IInputContext.Location"/>
  public Location? Location { get; }

  /// <inheritdoc cref="IInputContext.Contact"/>
  public Contact? Contact { get; }

  public string? ReplyText { get; private set; }
  public ParseMode? ParseMode { get; private set; }
  public IEnumerable<MessageEntity>? Entities { get; private set; }
  public int? ReplyToMessageId { get; private set; }
  public ReplyMarkup? ReplyMarkup { get; private set; }
  public Exception? Exception { get; private set; }

  /// <param name="chat">The chat the turn came from.</param>
  /// <param name="argument">The routing text; for location and contact messages the text
  /// encoding of the shared payload ("lat,lon" / the phone number), never empty.</param>
  /// <param name="location">The structured payload of a location message, null otherwise.</param>
  /// <param name="contact">The structured payload of a contact message, null otherwise.</param>
  public MessageContext(Chat chat, string argument, Location? location = null, Contact? contact = null)
  {
    Chat = chat;
    Text = argument;
    Location = location;
    Contact = contact;
  }

  /// <summary>Replies with text and a reply keyboard built from the given rows.</summary>
  public MessageContext Reply(
    string? text,
    IEnumerable<IEnumerable<KeyboardButton>>? keyboardRows,
    ParseMode? parseMode = null,
    IEnumerable<MessageEntity>? entities = null,
    int? replyToMessageId = null,
    Exception? exception = null
  )
  {
    ReplyMarkup? replyMarkup = null;

    if (keyboardRows != null && keyboardRows.Any())
    {
      replyMarkup = new ReplyKeyboardMarkup(keyboardRows.Select(row => row.ToArray()))
      {
        ResizeKeyboard = true,
        OneTimeKeyboard = false
      };
    }

    return Reply(
      text,
      parseMode,
      entities,
      replyToMessageId,
      replyMarkup,
      exception
    );
  }

  /// <summary>Replies with text and an arbitrary markup.</summary>
  public MessageContext Reply(
    string? replyText = null,
    ParseMode? parseMode = null,
    IEnumerable<MessageEntity>? entities = null,
    int? replyToMessageId = null,
    ReplyMarkup? replyMarkup = null,
    Exception? exception = null
  )
  {
    ReplyText = replyText;
    ParseMode = parseMode;
    Entities = entities;
    ReplyToMessageId = replyToMessageId;
    ReplyMarkup = replyMarkup;
    Exception = exception;

    return this;
  }

  /// <summary>
  /// Replaces the routing text before forwarding the turn to another form
  /// (e.g. a list form swaps a row label for the entity id the detail form expects).
  /// </summary>
  public MessageContext Forward(string argument)
  {
    Text = argument;

    return this;
  }
}
