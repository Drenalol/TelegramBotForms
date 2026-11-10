using Drenalol.Telegram.Bot.Forms.Profile;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

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
