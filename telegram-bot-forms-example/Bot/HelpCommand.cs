using Drenalol.Telegram.Bot.Forms;
using Drenalol.Telegram.Bot.Forms.Localization;

namespace ExampleBot.Bot;

/// <summary>
/// A free-text command: matched by the first message token ("help" or "/help", with
/// any arguments), wins over keyboard keys and over the waiting form in the history.
/// Commands are resolved from DI in their own scope, so they take dependencies too.
/// </summary>
public sealed class HelpCommand(IChatLanguageResolver languageResolver) : IBotCommand
{
  public string Prefix => "help";

  public async Task<MessageContext> ExecuteAsync(MessageContext context, CancellationToken cancellationToken)
  {
    var language = await languageResolver.ResolveAsync(context.Chat, cancellationToken);

    return context.Reply(ExampleTexts.Help(language));
  }
}
