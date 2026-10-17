using Telegram.Bot;
using Telegram.Bot.Types;

namespace Drenalol.Telegram.Bot.Forms;

/// <summary>
/// Consumes <see cref="TelegramBotBus.Income"/>: acknowledges inline-button taps
/// (replaying their callback data as the routing text, so callbacks navigate exactly
/// like reply-keyboard taps), routes text messages through the form retriever and
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
    if (update.CallbackQuery is { Data: not null } callback && callback.Message?.Chat is { } callbackChat)
    {
      // The tap must be acknowledged or the client keeps the button pending; an expired
      // query just answers with nothing.
      try
      {
        await _client.AnswerCallbackQuery(callback.Id, cancellationToken: CancellationToken.None);
      }
      catch
      {
        /* query already expired */
      }

      var callbackContext = new MessageContext(callbackChat, callback.Data);

      var callbackForm = await _formRetriever.GetAsync(callbackChat, callbackContext.Text);

      if (callbackForm != null)
        await RenderFormAsync(callbackForm, callbackContext);

      return;
    }

    if (update.Message is not { Text: not null })
      return;

    var chat = update.Message.Chat;
    var context = new MessageContext(chat, update.Message.Text);

    var form = await _formRetriever.GetAsync(chat, context.Text);

    if (form != null)
      await RenderFormAsync(form, context);
  }

  private async Task RenderFormAsync(IForm form, MessageContext context)
  {
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(form.TimeoutSec));
    var renderResult = await form.RenderAsync(context, cts.Token);
    await _bus.Outcome.SendAsync(renderResult, CancellationToken.None);
  }

  public void Dispose() => _subscription.Dispose();
}
