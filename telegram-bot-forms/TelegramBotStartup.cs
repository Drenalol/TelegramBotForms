using Drenalol.Telegram.Bot.Forms.Profile;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace Drenalol.Telegram.Bot.Forms;

/// <summary>
/// The bot host: optionally pushes the code-owned BotFather profile texts, then runs
/// long polling (Telegram.Bot <c>ReceiveAsync</c>). Incoming
/// updates go to <see cref="TelegramBotBus.Income"/>; rendered replies subscribed from
/// <see cref="TelegramBotBus.Outcome"/> are sent back to the chats.
/// <para>
/// One poller per bot token: Telegram hands every update to a single long poller, so
/// two processes (e.g. dev and prod) sharing a token steal each other's messages.
/// </para>
/// </summary>
public class TelegramBotStartup : BackgroundService
{
  private readonly ITelegramBotClient _telegramBotClient;
  private readonly TelegramBotBus _bus;
  private readonly IFormRetriever _formRetriever;
  private readonly BotProfileOptions _botProfileOptions;
  private readonly ILogger<TelegramBotStartup> _logger;

  private readonly IDisposable _subscription;
  private readonly TelegramBotProvider _telegramBotProvider;

  public TelegramBotStartup(
    ITelegramBotClient telegramBotClient,
    TelegramBotBus bus,
    IFormRetriever formRetriever,
    IOptions<BotProfileOptions> botProfileOptions,
    ILogger<TelegramBotStartup> logger
  )
  {
    _telegramBotClient = telegramBotClient;
    _bus = bus;
    _formRetriever = formRetriever;
    _botProfileOptions = botProfileOptions.Value;
    _logger = logger;

    _subscription = bus.Outcome.Subscribe(PublishAsync);
    _telegramBotProvider = new TelegramBotProvider(telegramBotClient, bus, formRetriever);
  }

  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    // BotFather profile texts (name, short description, welcome card, "/" menu) are
    // code-owned when configured: pushed on every start before polling begins;
    // failures inside never reach the loop.
    if (_botProfileOptions.IsConfigured)
      await BotProfile.ApplyAsync(_telegramBotClient, _botProfileOptions, _logger, stoppingToken);

    // GetMe validates the token up front (a bad one fails the host here, loudly) and
    // the line below is the visible "the bot is alive and polling" marker - polling
    // itself is otherwise completely silent.
    var botProfile = await _telegramBotClient.GetMe(stoppingToken);
    _logger.LogInformation("Telegram bot @{Username} (id {BotId}) is polling", botProfile.Username, botProfile.Id);

    // The awaited update handler is what gives polling its backpressure: the loop waits
    // for the update to enter the bus before requesting the next one.
    Func<ITelegramBotClient, Update, CancellationToken, Task> handleUpdateAsync =
      (_, update, token) => _bus.Income.SendAsync(update, token);

    await _telegramBotClient.ReceiveAsync(
      handleUpdateAsync,
      (_, exception, _) =>
      {
        // A RequestException is the long-poll request itself timing out or dropping
        // (Telegram hangs the connection up to ~100 s and a quiet bot leaves it empty);
        // the polling loop retries on its own, so this is transport noise, not a failure.
        if (exception is RequestException)
          _logger.LogWarning(exception, "TelegramBot request failed, polling continues: {ErrorText}", exception.Message);
        else
          _logger.LogError(exception, "TelegramBot caught error");

        return Task.CompletedTask;
      },
      cancellationToken: stoppingToken
    );
  }

  private async Task PublishAsync(MessageContext context)
  {
    if (context.Exception != null)
      _logger.LogError(context.Exception, "Chat {ChatId} render failed: {ErrorText}", context.Chat.Id, context.Exception.Message);

    // An in-place reply is the kind of answer that has to stay in the chat: it edits the
    // message whose button was pressed, and only a refused edit makes it a new message.
    if (context.ReplacesMessage && TryGetMessageToReplace(context) is { } messageId && await TryReplaceMessageAsync(context, messageId))
      return;

    var ephemeral = TryGetEphemeralParameters(context);

    // One turn sends one message, and a rich reply wins over the text one: a form that
    // produced tables/details/in-message buttons has nothing for the text send to add.
    // The text is what the turn falls back to when the rich message cannot be delivered.
    if (context.RichReply is { } rich && await TrySendRichAsync(context, rich, ephemeral))
      return;

    if (context.ReplyText == null)
      return;

    await _telegramBotClient.SendMessage(
      new ChatId(context.Chat.Id),
      context.ReplyText,
      parseMode: context.ParseMode ?? default,
      replyParameters: GetReplyParameters(context),
      replyMarkup: context.ReplyMarkup,
      // bot answers are utility texts - a link card preview only eats the screen
      linkPreviewOptions: LinkPreviewOptions.Disabled,
      entities: context.Entities?.ToList(),
      ephemeralMessageParameters: ephemeral
    );
  }

  /// <summary>
  /// The message an in-place reply (<see cref="MessageContext.ReplaceMessage"/>) edits: the
  /// one that carried the pressed button. Null, loudly, when the turn did not come from a
  /// button of a stored message — a command that was typed by hand, a button of an ephemeral
  /// message — because then there is nothing to edit and the reply is sent as a new message.
  /// </summary>
  private int? TryGetMessageToReplace(MessageContext context)
  {
    if (context.CallbackMessageId is not { } messageId)
    {
      _logger.LogWarning(
        "Chat {ChatId} asked to answer in place of the pressed message, but the turn came from no message, sending the reply as a new one",
        context.Chat.Id);

      return null;
    }

    return messageId;
  }

  /// <summary>
  /// Edits the pressed message into the reply instead of adding a message: the chat keeps the
  /// answer where the question was, and keeps it after a restart, which an ephemeral
  /// replacement does not. The inline keyboard of the edited message does not survive the edit
  /// unless the reply carries one of its own, so an answered question is never tappable twice.
  /// False means the turn is sent as a new message instead: the chat is more than two days old
  /// for the bot, the message was deleted, the server refused the rich content.
  /// </summary>
  private async Task<bool> TryReplaceMessageAsync(MessageContext context, int messageId)
  {
    // only an inline keyboard can travel with an edit; a reply keyboard belongs to the chat,
    // not to the message being edited, so it is dropped here
    var inlineKeyboard = context.ReplyMarkup as InlineKeyboardMarkup;

    if (context.RichReply is { } rich && await TryEditRichAsync(context, messageId, rich, inlineKeyboard))
      return true;

    return context.ReplyText is { } text && await TryEditTextAsync(context, messageId, text, inlineKeyboard);
  }

  private async Task<bool> TryEditRichAsync(MessageContext context, int messageId, InputRichMessage rich, InlineKeyboardMarkup? inlineKeyboard)
  {
    try
    {
      await _telegramBotClient.EditMessageText(
        new ChatId(context.Chat.Id),
        messageId,
        text: null,
        replyMarkup: inlineKeyboard,
        richMessage: rich
      );

      return true;
    }
    catch (ApiRequestException exception) when (IsUnmodified(exception))
    {
      return true; // the message already says exactly this: the answer of an earlier tap
    }
    catch (ApiRequestException exception)
    {
      _logger.LogWarning(
        exception,
        "Chat {ChatId} message {MessageId} refused the rich edit{Fallback}",
        context.Chat.Id,
        messageId,
        context.ReplyText is null ? " and has no text fallback" : ", editing it into the text one");

      return false;
    }
  }

  private async Task<bool> TryEditTextAsync(MessageContext context, int messageId, string text, InlineKeyboardMarkup? inlineKeyboard)
  {
    try
    {
      await _telegramBotClient.EditMessageText(
        new ChatId(context.Chat.Id),
        messageId,
        text,
        parseMode: context.ParseMode ?? default,
        replyMarkup: inlineKeyboard,
        // bot answers are utility texts - a link card preview only eats the screen
        linkPreviewOptions: LinkPreviewOptions.Disabled,
        entities: context.Entities?.ToList()
      );

      return true;
    }
    catch (ApiRequestException exception) when (IsUnmodified(exception))
    {
      return true; // the message already says exactly this: the answer of an earlier tap
    }
    catch (ApiRequestException exception)
    {
      _logger.LogWarning(
        exception,
        "Chat {ChatId} message {MessageId} could not be edited, sending the reply as a new message",
        context.Chat.Id,
        messageId);

      return false;
    }
  }

  /// <summary>Whether an edit failed only because nothing would change — the usual answer to a
  /// second tap on a question that has already been turned into its result.</summary>
  private static bool IsUnmodified(ApiRequestException exception) =>
    exception.Message.Contains("not modified", StringComparison.OrdinalIgnoreCase);

  /// <summary>Sends a rich reply, false when the answer was rejected: rich messages are a
  /// recent Bot API feature and a block the server dislikes fails the whole send, so the
  /// turn still deserves its text. Only an answer of the server counts as a failure worth
  /// degrading over - a cancelled turn propagates instead.</summary>
  private async Task<bool> TrySendRichAsync(MessageContext context, InputRichMessage rich, EphemeralMessageParameters? ephemeral)
  {
    try
    {
      await _telegramBotClient.SendRichMessage(
        new ChatId(context.Chat.Id),
        rich,
        replyParameters: GetReplyParameters(context),
        replyMarkup: context.ReplyMarkup,
        ephemeralMessageParameters: ephemeral
      );

      return true;
    }
    catch (ApiRequestException exception)
    {
      _logger.LogWarning(
        exception,
        "Chat {ChatId} could not be sent the rich reply{Fallback}",
        context.Chat.Id,
        context.ReplyText is null ? " and has no text fallback" : ", sending its text fallback");

      return false;
    }
  }

  private static ReplyParameters? GetReplyParameters(MessageContext context) =>
    context.ReplyToMessageId.HasValue ? new ReplyParameters { MessageId = context.ReplyToMessageId.Value } : null;

  /// <summary>
  /// The ephemeral parameters of a reply, null when it is a regular message. An ephemeral
  /// reply is addressed through the callback query it answers - naming a receiver user
  /// would let a form write into somebody else's screen - so a turn that did not come
  /// from a button tap cannot be ephemeral and degrades to a regular message, loudly.
  /// </summary>
  private EphemeralMessageParameters? TryGetEphemeralParameters(MessageContext context)
  {
    if (!context.EphemeralReply)
      return null;

    if (context.CallbackQueryId is not { } callbackQueryId)
    {
      _logger.LogWarning(
        "Chat {ChatId} asked for an ephemeral reply to a turn that came from a written message, sending it as a regular one",
        context.Chat.Id);

      return null;
    }

    return new EphemeralMessageParameters
    {
      CallbackQueryId = callbackQueryId,
      ReplaceCallbackQueryMessage = context.ReplaceCallbackQueryMessage,
    };
  }

  public override Task StopAsync(CancellationToken cancellationToken)
  {
    _telegramBotProvider.Dispose();
    _subscription.Dispose();

    return base.StopAsync(cancellationToken);
  }
}
