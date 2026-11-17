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

  /// <summary>Identifier of the callback query a tapped button produced, null for a
  /// message. The host acknowledged the query already; the id is what lets the reply be
  /// sent back as an ephemeral message (see <see cref="IOutputContext.EphemeralReply"/>).</summary>
  public string? CallbackQueryId { get; }

  /// <summary>Identifier of the message the tapped button was pressed in, null when the turn
  /// did not come from a button of a stored message. It is what lets the reply edit that very
  /// message (see <see cref="IOutputContext.ReplacesMessage"/>).</summary>
  public int? CallbackMessageId { get; }
}

/// <summary>The outgoing side of a conversation turn: what the bot answers.</summary>
public interface IOutputContext
{
  public string? ReplyText { get; }
  public ParseMode? ParseMode { get; }
  public IEnumerable<MessageEntity>? Entities { get; }
  public int? ReplyToMessageId { get; }
  public ReplyMarkup? ReplyMarkup { get; }

  /// <summary>A rich message (Bot API rich messages) to send instead of
  /// <see cref="ReplyText"/>: real tables, collapsible details, buttons inside the
  /// message. Set by <see cref="MessageContext.ReplyRich(InputRichMessage,ReplyMarkup,int?,Exception?)"/>;
  /// <see cref="ReplyText"/> then only serves as its fallback (see
  /// <see cref="MessageContext.WithTextFallback"/>).
  /// One turn sends one message.</summary>
  public InputRichMessage? RichReply { get; }

  /// <summary>The reply is an ephemeral message: it goes only to the user who pressed the
  /// button and is not stored in the chat, so it must not carry anything worth reading
  /// later. Requires <see cref="IInputContext.CallbackQueryId"/>.</summary>
  public bool EphemeralReply { get; }

  /// <summary>The ephemeral reply replaces the message that carries the pressed button
  /// instead of being shown next to it.</summary>
  public bool ReplaceCallbackQueryMessage { get; }

  /// <summary>The reply <b>edits the pressed message in place</b> instead of being sent as a
  /// new one, so the answer is stored in the chat and stays readable after a restart and on
  /// the user's other devices — none of which an ephemeral replacement
  /// (<see cref="ReplaceCallbackQueryMessage"/>, a client-side view that the app forgets)
  /// gives. Set by <see cref="MessageContext.ReplaceMessage"/>; needs
  /// <see cref="IInputContext.CallbackMessageId"/>, and the host sends a new message
  /// when the turn has none.</summary>
  public bool ReplacesMessage { get; }
}

/// <summary>An exception captured while rendering, logged by the bot host.</summary>
public interface IErrorContext
{
  Exception? Exception { get; }
}

/// <summary>
/// One conversation turn: the incoming chat message (or a replayed callback), mutated
/// in place by the form into the outgoing reply via <see cref="Reply(string?,ParseMode?,IEnumerable{MessageEntity}?,int?,ReplyMarkup?,Exception?)"/>
/// and friends. The host sends <see cref="RichReply"/> when the form produced a rich
/// message, otherwise <see cref="ReplyText"/> (with the markup and entities) back to the
/// chat; <see cref="EphemeralReply"/> turns whatever it sends into a message that only
/// the user who pressed the button sees, and <see cref="ReplacesMessage"/> makes the send an
/// edit of the message whose button was pressed.
/// </summary>
public class MessageContext : IInputContext, IOutputContext, IErrorContext
{
  public Chat Chat { get; }
  public string Text { get; private set; }

  /// <inheritdoc cref="IInputContext.Location"/>
  public Location? Location { get; }

  /// <inheritdoc cref="IInputContext.Contact"/>
  public Contact? Contact { get; }

  /// <inheritdoc cref="IInputContext.CallbackQueryId"/>
  public string? CallbackQueryId { get; }

  /// <inheritdoc cref="IInputContext.CallbackMessageId"/>
  public int? CallbackMessageId { get; }

  public string? ReplyText { get; private set; }
  public ParseMode? ParseMode { get; private set; }
  public IEnumerable<MessageEntity>? Entities { get; private set; }
  public int? ReplyToMessageId { get; private set; }
  public ReplyMarkup? ReplyMarkup { get; private set; }
  public InputRichMessage? RichReply { get; private set; }
  public bool EphemeralReply { get; private set; }
  public bool ReplaceCallbackQueryMessage { get; private set; }
  public bool ReplacesMessage { get; private set; }
  public Exception? Exception { get; private set; }

  /// <param name="chat">The chat the turn came from.</param>
  /// <param name="argument">The routing text; for location and contact messages the text
  /// encoding of the shared payload ("lat,lon" / the phone number), never empty.</param>
  /// <param name="location">The structured payload of a location message, null otherwise.</param>
  /// <param name="contact">The structured payload of a contact message, null otherwise.</param>
  /// <param name="callbackQueryId">Identifier of the callback query when the turn is a
  /// tapped inline button replayed as routing text, null for a written message.</param>
  /// <param name="callbackMessageId">Identifier of the message the tapped button was pressed
  /// in, null when the turn is not a tap on a button of a stored message.</param>
  public MessageContext(
    Chat chat,
    string argument,
    Location? location = null,
    Contact? contact = null,
    string? callbackQueryId = null,
    int? callbackMessageId = null
  )
  {
    Chat = chat;
    Text = argument;
    Location = location;
    Contact = contact;
    CallbackQueryId = callbackQueryId;
    CallbackMessageId = callbackMessageId;
  }

  /// <summary>Replies with text and a reply keyboard built from the given rows.</summary>
  public MessageContext Reply(
    string? text,
    IEnumerable<IEnumerable<KeyboardButton>>? keyboardRows,
    ParseMode? parseMode = null,
    IEnumerable<MessageEntity>? entities = null,
    int? replyToMessageId = null,
    Exception? exception = null
  ) => Reply(
    text,
    parseMode,
    entities,
    replyToMessageId,
    ToReplyKeyboard(keyboardRows),
    exception
  );

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

    // one turn sends one message: a rich reply produced earlier does not survive a text one
    RichReply = null;

    return this;
  }

  /// <summary>
  /// Replies with a <b>rich message</b> (Bot API rich messages: real tables, collapsible
  /// details, buttons inside the message) and a reply keyboard built from the given rows.
  /// The text reply of the turn is dropped — a turn sends one message, and the forms
  /// pipeline only ever sends that one.
  /// </summary>
  public MessageContext ReplyRich(
    InputRichMessage rich,
    IEnumerable<IEnumerable<KeyboardButton>>? keyboardRows,
    int? replyToMessageId = null,
    Exception? exception = null
  ) => ReplyRich(rich, ToReplyKeyboard(keyboardRows), replyToMessageId, exception);

  /// <summary>Replies with a rich message and an arbitrary markup.</summary>
  public MessageContext ReplyRich(
    InputRichMessage rich,
    ReplyMarkup? replyMarkup = null,
    int? replyToMessageId = null,
    Exception? exception = null
  )
  {
    RichReply = rich;
    ReplyMarkup = replyMarkup;
    ReplyToMessageId = replyToMessageId;
    Exception = exception;

    ReplyText = null;
    ParseMode = null;
    Entities = null;

    return this;
  }

  /// <summary>
  /// The text to send when the rich message <em>cannot</em> be delivered — an older client
  /// behind an outdated Bot API, a block the server rejected. Without it a failed rich
  /// reply simply is not sent, so a screen whose numbers matter carries the same verdict as
  /// text (an ordinary message has no tables, a <c>&lt;pre&gt;</c> block is the closest).
  /// </summary>
  public MessageContext WithTextFallback(string text, ParseMode? parseMode = null, IEnumerable<MessageEntity>? entities = null)
  {
    ReplyText = text;
    ParseMode = parseMode;
    Entities = entities;

    return this;
  }

  /// <summary>
  /// Turns the reply into an <b>ephemeral message</b>: shown only to the user who pressed
  /// the button and never kept in the chat, so it must not carry anything worth reading
  /// later (a confirmation of an action that is already stored is a good fit, a verdict
  /// is not). Only a turn that came from a button tap can be ephemeral; the host logs and
  /// sends the reply as usual otherwise.
  /// </summary>
  /// <param name="replaceCallbackQueryMessage">Show the reply in place of the message that
  /// carries the pressed button instead of next to it.</param>
  public MessageContext Ephemeral(bool replaceCallbackQueryMessage = false)
  {
    EphemeralReply = true;
    ReplaceCallbackQueryMessage = replaceCallbackQueryMessage;

    return this;
  }

  /// <summary>
  /// Turns the reply into an <b>edit of the message the pressed button sits in</b>: the chat
  /// keeps one message that now says what happened instead of a question that has already been
  /// answered, and, unlike <see cref="Ephemeral"/>, it keeps it <em>for good</em> — the edit is
  /// stored, so the answer survives an app restart and appears on the user's other devices.
  /// That is the right answer to a tap whose result must be readable later (a rating, a mute);
  /// a confirmation the reader never needs again fits <see cref="Ephemeral"/> better.
  /// The buttons of the replaced message do not survive the edit: an answered question is not
  /// tappable twice. Only a turn that came from a button of a stored message can be replaced;
  /// the host logs and sends the reply as a new message otherwise.
  /// </summary>
  public MessageContext ReplaceMessage()
  {
    ReplacesMessage = true;

    return this;
  }

  /// <summary>Builds the reply keyboard the framework sends with a reply, null for no
  /// keyboard — the shape every screen of a bot repeats, so it lives here.</summary>
  private static ReplyMarkup? ToReplyKeyboard(IEnumerable<IEnumerable<KeyboardButton>>? keyboardRows) =>
    keyboardRows != null && keyboardRows.Any()
      ? new ReplyKeyboardMarkup(keyboardRows.Select(row => row.ToArray()))
      {
        ResizeKeyboard = true,
        OneTimeKeyboard = false,
      }
      : null;

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
