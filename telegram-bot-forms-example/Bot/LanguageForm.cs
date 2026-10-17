using Drenalol.Telegram.Bot.Forms;
using Drenalol.Telegram.Bot.Forms.Localization;

namespace ExampleBot.Bot;

public enum LanguageState
{
  /// <summary>
  /// The single state of this form: "choose the language this button stands for" —
  /// the language code comes from the pressed button's text, so every supported
  /// language maps here and adding one touches no switch.
  /// </summary>
  OnSelect,
}

/// <summary>
/// Per-chat language picker: rows are the native language names, the current language
/// carries a check mark. The form is stateful and sits atop the chat's history, so a
/// language press (an unregistered text) is delivered back to this form, not swallowed
/// by the router.
/// </summary>
public sealed class LanguageForm : NavigationFormBase<LanguageState>
{
  private readonly ChatLanguageStore _store;

  public LanguageForm(ChatLanguageStore store)
  {
    _store = store;

    AddRenderOn(LanguageState.OnSelect, string.Empty, (context, cancellationToken) => SelectAsync(context.Text, context, cancellationToken));
  }

  protected override async Task<MessageContext> RenderInitAsync(MessageContext context, CancellationToken cancellationToken)
  {
    Rows.Clear();
    _buttons.Clear();

    var current = await GetLanguageAsync(context.Chat, cancellationToken);

    foreach (var code in ExampleTexts.LanguageCodes)
      AddRow(Mark(ExampleTexts.NativeName(code), current, code), LanguageState.OnSelect);

    AddBackButton(Localizer.Back(current));

    return context.Reply(ExampleTexts.LanguageTitle(current), replyMarkup: BuildKeyboard());
  }

  private async Task<MessageContext> SelectAsync(string option, MessageContext context, CancellationToken cancellationToken)
  {
    var chosen = ExampleTexts.LanguageCodes.FirstOrDefault(code =>
      string.Equals(Unmark(option), ExampleTexts.NativeName(code), StringComparison.OrdinalIgnoreCase));

    if (chosen is null)
      return await RenderInitAsync(context, cancellationToken);

    _store.Set(context.Chat.Id, chosen);

    // Completed: "Back" later skips this picker instead of showing it again.
    IsCompleted = true;

    // re-enter the start form so the whole UI switches language right away
    return await NavigateToAsync<StartForm>(context, cancellationToken);
  }

  private static string Mark(string option, string current, string code) =>
    code == current ? $"✓ {option}" : option;

  private static string Unmark(string option) =>
    option.StartsWith("✓ ", StringComparison.Ordinal) ? option[2..].Trim() : option.Trim();
}
