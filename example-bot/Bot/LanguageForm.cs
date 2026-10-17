using Drenalol.Telegram.Bot.Forms;
using Telegram.Bot.Types.ReplyMarkups;

namespace ExampleBot.Bot;

/// <summary>
/// Per-chat language switch. The picker lists the native language names; a press is
/// picked up by this form through the chat history (the names are not registered
/// routing keys), stores the choice and re-renders the start form in the new language.
/// </summary>
public sealed class LanguageForm : FormBase
{
  private readonly ChatLanguageStore _store;

  public LanguageForm(ChatLanguageStore store) => _store = store;

  public override async Task<MessageContext> RenderAsync(MessageContext context, CancellationToken cancellationToken)
  {
    foreach (var language in ExampleTexts.LanguageCodes)
    {
      if (string.Equals(context.Text.Trim(), ExampleTexts.NativeName(language), StringComparison.OrdinalIgnoreCase))
      {
        _store.Set(context.Chat.Id, language);

        // re-enter the start form so the whole UI switches language right away
        return await NavigateToAsync<StartForm>(context, cancellationToken);
      }
    }

    var current = await GetLanguageAsync(context.Chat, cancellationToken);

    return context.Reply(
      ExampleTexts.LanguageTitle(current),
      [
        .. ExampleTexts.LanguageCodes.Select(language => new[] { new KeyboardButton(ExampleTexts.NativeName(language)) }),
        await CreateBackButtonRowAsync(context.Chat, cancellationToken),
      ]
    );
  }
}
