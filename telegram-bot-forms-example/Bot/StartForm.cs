using Drenalol.Telegram.Bot.Forms;
using Telegram.Bot.Types.ReplyMarkups;

namespace ExampleBot.Bot;

/// <summary>
/// The bot entry point ("/start", with or without a deep-link parameter): shows the
/// welcome + consent first, then the main keyboard. <see cref="ClearHistoryOnNavigate"/>
/// makes every entry a fresh navigation root, so "Back" from anywhere lands here.
/// </summary>
public sealed class StartForm : FormBase
{
  public override bool ClearHistoryOnNavigate => true;

  public override async Task<MessageContext> RenderAsync(MessageContext context, CancellationToken cancellationToken)
  {
    var language = await GetLanguageAsync(context.Chat, cancellationToken);

    // The consent button replays its text, so a press re-enters this form: the second
    // pass shows the greeting with the main keyboard.
    if (ExampleTexts.IsConsent(context.Text) || ExampleTexts.IsMainButton(context.Text))
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
