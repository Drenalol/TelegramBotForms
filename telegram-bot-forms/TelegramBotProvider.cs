using Telegram.Bot;
using Telegram.Bot.Types;

namespace Drenalol.Telegram.Bot.Forms;

/// <summary>
/// Consumes <see cref="TelegramBotBus.Income"/>: acknowledges inline-button taps
/// (replaying their callback data as the routing text, so callbacks navigate exactly
/// like reply-keyboard taps), routes incoming messages through the form retriever and
/// renders the resolved form under its own timeout.
/// </summary>
internal sealed class TelegramBotProvider : IDisposable
{
  private readonly ITelegramBotClient _client;
  private readonly TelegramBotBus _bus;
  private readonly IFormRetriever _formRetriever;
  private readonly IDisposable _subscription;

  public TelegramBotProvider(ITelegramBotClient client, TelegramBotBus bus, IFormRetriever formRetriever)
  {
    _client = client;
    _bus = bus;
    _formRetriever = formRetriever;
    _subscription = _bus.Income.Subscribe(ReceiveAsync);
  }

  private async Task ReceiveAsync(Update update)
  {
    var context = await TryGetContextAsync(update);

    if (context is null)
      return;

    var form = await _formRetriever.GetAsync(context.Chat, context.Text);

    if (form != null)
      await RenderFormAsync(form, context);
  }

  /// <summary>
  /// The single acceptance gate: turns an incoming update into the input context a form
  /// sees, null for updates the forms do not consume. A tapped button replays its data
  /// as the routing text; a shared location or contact enters as its human-readable text
  /// encoding (which never collides with a registered key, so it reaches the form
  /// waiting in the history) plus the structured <see cref="Location"/>/<see cref="Contact"/>
  /// payload. New message types extend this switch only.
  /// </summary>
  private async Task<MessageContext?> TryGetContextAsync(Update update)
  {
    if (update.CallbackQuery is { Data: not null } callback && callback.Message?.Chat is { } callbackChat)
    {
      await AcknowledgeCallbackAsync(callback.Id);

      // The query id travels with the turn: it is what lets the form answer with an
      // ephemeral message (MessageContext.Ephemeral) instead of a stored one.
      return new MessageContext(callbackChat, callback.Data, callbackQueryId: callback.Id);
    }

    return update.Message switch
    {
      { Text: not null } message => new MessageContext(message.Chat, message.Text),
      { Location: not null } message => new MessageContext(message.Chat, EncodeLocation(message.Location), message.Location),
      { Contact: not null } message => new MessageContext(message.Chat, message.Contact.PhoneNumber, contact: message.Contact),
      _ => null
    };
  }

  private async Task AcknowledgeCallbackAsync(string callbackQueryId)
  {
    // The tap must be acknowledged or the client keeps the button pending; an expired
    // query just answers with nothing.
    try
    {
      await _client.AnswerCallbackQuery(callbackQueryId, cancellationToken: CancellationToken.None);
    }
    catch
    {
      /* query already expired */
    }
  }

  // "latitude,longitude" in the round-trip format ("55.7558,37.6173" - the map-link
  // format), invariant so the text never depends on the server culture.
  private static string EncodeLocation(Location location) =>
    FormattableString.Invariant($"{location.Latitude:R},{location.Longitude:R}");

  private async Task RenderFormAsync(IForm form, MessageContext context)
  {
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(form.TimeoutSec));
    var renderResult = await form.RenderAsync(context, cts.Token);
    await _bus.Outcome.SendAsync(renderResult, CancellationToken.None);
  }

  public void Dispose() => _subscription.Dispose();
}
