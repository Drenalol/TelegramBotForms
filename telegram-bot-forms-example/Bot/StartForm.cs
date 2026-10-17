using Drenalol.Telegram.Bot.Forms;
using Telegram.Bot.Types.ReplyMarkups;

namespace ExampleBot.Bot;

/// <summary>
/// The bot entry point ("/start", with or without a deep-link parameter): shows the
/// welcome + consent first, then the main keyboard. The root-form pattern:
/// <see cref="ClearHistoryOnNavigate"/> makes every entry a fresh navigation root and
/// <see cref="IgnoreHistory"/> keeps the form out of the history, so "Back" from
/// anywhere exhausts it and lands on a fresh start form (consent is remembered, so a
/// returning user gets the menu right away).
/// </summary>
public sealed class StartForm(ConsentStore consent) : FormBase
{
  public override bool ClearHistoryOnNavigate => true;
  public override bool IgnoreHistory => true;

  public override async Task<MessageContext> RenderAsync(MessageContext context, CancellationToken cancellationToken)
  {
    var language = await GetLanguageAsync(context.Chat, cancellationToken);

    if (ExampleTexts.IsConsent(context.Text))
      consent.Accept(context.Chat.Id);

    // the consent button replays its text, so a press re-enters this form: from the
    // second pass (and for anyone who already accepted) it shows the greeting with the
    // main keyboard
    if (consent.IsAccepted(context.Chat.Id) || ExampleTexts.IsMainButton(context.Text))
      return context.Reply(ExampleTexts.Greeting(language, null), replyMarkup: ExampleTexts.MainKeyboard(language));

    // "/start <parameter>" arrives with the deep-link parameter after "/start"
    var parameter = context.Text.Length > "/start".Length
      ? context.Text["/start".Length..].Trim()
      : null;

    return context.Reply(
      ExampleTexts.Welcome(language, parameter is { Length: > 0 } deepLink ? deepLink : null),
      [[new KeyboardButton(ExampleTexts.ConsentButton(language))]]
    );
  }
}
