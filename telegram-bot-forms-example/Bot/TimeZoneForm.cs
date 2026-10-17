using Drenalol.Telegram.Bot.Forms;
using Drenalol.Telegram.Bot.Forms.Localization;
using Drenalol.Telegram.Bot.Forms.TimeZones;

namespace ExampleBot.Bot;

public enum TimeZoneState
{
  /// <summary>The single state of this form: "choose the zone this button stands for".</summary>
  OnSelect,
}

/// <summary>
/// A settings-style time zone picker built on the framework's curated
/// <see cref="TelegramTimeZones"/> quick-pick list (IANA id + current UTC offset in the
/// label), with the chat's current zone checkmarked. Stateful like the language picker,
/// so a zone press is delivered back to this form.
/// </summary>
public sealed class TimeZoneForm : NavigationFormBase<TimeZoneState>
{
  private readonly TimeZoneStore _store;

  public TimeZoneForm(TimeZoneStore store)
  {
    _store = store;

    AddRenderOn(TimeZoneState.OnSelect, string.Empty, (context, cancellationToken) => SelectAsync(context.Text, context, cancellationToken));
  }

  protected override async Task<MessageContext> RenderInitAsync(MessageContext context, CancellationToken cancellationToken)
  {
    Rows.Clear();
    _buttons.Clear();

    var language = await GetLanguageAsync(context.Chat, cancellationToken);
    var current = _store.Get(context.Chat.Id);

    foreach (var zone in TelegramTimeZones.Popular())
      AddRow(Mark(zone.Label, current), TimeZoneState.OnSelect);

    AddBackButton(Localizer.Back(language));

    return context.Reply(ExampleTexts.TimeZoneTitle(language, current), replyMarkup: BuildKeyboard());
  }

  private async Task<MessageContext> SelectAsync(string option, MessageContext context, CancellationToken cancellationToken)
  {
    var label = Unmark(option);

    var zone = TelegramTimeZones.Popular().FirstOrDefault(candidate =>
      string.Equals(candidate.Label, label, StringComparison.OrdinalIgnoreCase) ||
      string.Equals(candidate.Id, label, StringComparison.OrdinalIgnoreCase));

    if (zone.Id is null)
      return await RenderInitAsync(context, cancellationToken);

    _store.Set(context.Chat.Id, zone.Id);

    // Completed: "Back" later skips this picker instead of showing it again.
    IsCompleted = true;

    var language = await GetLanguageAsync(context.Chat, cancellationToken);

    return context.Reply(
      ExampleTexts.TimeZoneSaved(language, zone.Id),
      [await CreateBackButtonRowAsync(context.Chat, cancellationToken)]
    );
  }

  private static string Mark(string label, string currentId) =>
    label == currentId || label.StartsWith(currentId + " · ", StringComparison.Ordinal) ? $"✓ {label}" : label;

  private static string Unmark(string option) =>
    option.StartsWith("✓ ", StringComparison.Ordinal) ? option[2..].Trim() : option.Trim();
}
