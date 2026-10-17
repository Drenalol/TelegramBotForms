using Drenalol.Telegram.Bot.Forms;
using Drenalol.Telegram.Bot.Forms.TimeZones;
using Telegram.Bot.Types.ReplyMarkups;

namespace ExampleBot.Bot;

/// <summary>
/// A settings-style picker built on the framework's curated <see cref="TelegramTimeZones"/>
/// quick-pick list (IANA id + current UTC offset in the label).
/// </summary>
public sealed class TimeZoneForm : FormBase
{
  public override async Task<MessageContext> RenderAsync(MessageContext context, CancellationToken cancellationToken)
  {
    var language = await GetLanguageAsync(context.Chat, cancellationToken);
    var zones = TelegramTimeZones.Popular();

    var picked = zones.FirstOrDefault(zone =>
      string.Equals(zone.Label, context.Text, StringComparison.OrdinalIgnoreCase) ||
      string.Equals(zone.Id, context.Text, StringComparison.OrdinalIgnoreCase));

    if (picked.Id is not null)
      return context.Reply(ExampleTexts.TimeZoneSaved(language, picked.Id), [await CreateBackButtonRowAsync(context.Chat, cancellationToken)]);

    return context.Reply(
      ExampleTexts.TimeZoneTitle(language),
      [
        .. zones.Select(zone => new[] { new KeyboardButton(zone.Label) }),
        await CreateBackButtonRowAsync(context.Chat, cancellationToken),
      ]
    );
  }
}
