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

    if (context.ReplyText == null)
      return;

    await _telegramBotClient.SendMessage(
      new ChatId(context.Chat.Id),
      context.ReplyText,
      parseMode: context.ParseMode ?? default,
      replyParameters: context.ReplyToMessageId.HasValue ? new ReplyParameters { MessageId = context.ReplyToMessageId.Value } : null,
      replyMarkup: context.ReplyMarkup,
      // bot answers are utility texts - a link card preview only eats the screen
      linkPreviewOptions: LinkPreviewOptions.Disabled,
      entities: context.Entities?.ToList()
    );
  }

  public override Task StopAsync(CancellationToken cancellationToken)
  {
    _telegramBotProvider.Dispose();
    _subscription.Dispose();

    return base.StopAsync(cancellationToken);
  }
}
