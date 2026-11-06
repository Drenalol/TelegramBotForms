using Drenalol.Telegram.Bot.Forms;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace ExampleBot.Bot;

public enum PlaceState
{
  Wait,
}

/// <summary>
/// A free-input funnel accepting three kinds of turns in its one waiting state: typed
/// text, the "📍 My location" button (context.Location carries the coordinates, the text
/// holds their "lat,lon" encoding) and the "👤 My contact" button (context.Contact, the
/// text holds the phone number). The structured payloads are checked first and the raw
/// text is the fallback, so one state consumes every source.
/// </summary>
public sealed class PlaceForm : ExtendedFormBase<PlaceState>
{
  public PlaceForm()
  {
    AddRenderOn(PlaceState.Wait, string.Empty, WaitAsync);
  }

  protected override async Task<MessageContext> RenderInitAsync(MessageContext context, CancellationToken cancellationToken)
  {
    State = PlaceState.Wait;

    var language = await GetLanguageAsync(context.Chat, cancellationToken);

    return context.Reply(ExampleTexts.PlaceTitle(language), await BuildRowsAsync(context.Chat, language, cancellationToken));
  }

  protected override IRender GetRender(MessageContext context) =>
    State == PlaceState.Wait
      ? CustomRender(WaitAsync)
      : throw new InvalidOperationException($"Nothing routes the text: {context.Text}");

  private async Task<MessageContext> WaitAsync(MessageContext context, CancellationToken cancellationToken)
  {
    var language = await GetLanguageAsync(context.Chat, cancellationToken);
    var rows = await BuildRowsAsync(context.Chat, language, cancellationToken);

    if (context.Location is { } location)
      return context.Reply(ExampleTexts.PlaceGotLocation(language, location.Latitude, location.Longitude), rows);

    if (context.Contact is { } contact)
      return context.Reply(ExampleTexts.PlaceGotContact(language, contact.PhoneNumber, contact.FirstName), rows);

    return context.Reply(ExampleTexts.PlaceGotText(language, context.Text.Trim()), rows);
  }

  // The share buttons live in the form's own keyboard: Telegram sends the payload
  // (not the button text) when they are pressed.
  private async Task<List<KeyboardButton[]>> BuildRowsAsync(Chat chat, string language, CancellationToken cancellationToken) =>
  [
    [new KeyboardButton(ExampleTexts.PlaceLocationButton(language)) { RequestLocation = true }],
    [new KeyboardButton(ExampleTexts.PlaceContactButton(language)) { RequestContact = true }],
    await CreateBackButtonRowAsync(chat, cancellationToken),
  ];
}
