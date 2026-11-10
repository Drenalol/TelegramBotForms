using Drenalol.Telegram.Bot.Forms;
using Drenalol.Telegram.Bot.Forms.Localization;
using ExampleBot.Data;

namespace ExampleBot.Bot;

/// <summary>
/// The command the rich product card's watch button carries ("watch 42"). A button inside
/// a message must hand the router something it resolves on its own - commands beat keyboard
/// keys and the form waiting in the chat's history, so the tap lands here from any screen.
/// <para>
/// The answer is <b>ephemeral</b>: only the user who pressed sees it and nothing is added
/// to the chat, which is right because the flag itself is stored in <see cref="WatchStore"/>.
/// Pass <c>Ephemeral(replaceCallbackQueryMessage: true)</c> to have it take the place of the
/// message with the button instead of sitting next to it.
/// </para>
/// </summary>
public sealed class WatchCommand(WatchStore watched, IChatLanguageResolver languages) : IBotCommand
{
  public string Prefix => "watch";

  public async Task<MessageContext> ExecuteAsync(MessageContext context, CancellationToken cancellationToken)
  {
    var language = await languages.ResolveAsync(context.Chat, cancellationToken);

    // "watch 42" -> 42; the product name is only worth a lookup when the id is real
    if (!int.TryParse(context.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ElementAtOrDefault(1), out var id))
      return context.Reply(ExampleTexts.WatchNeedsId(language)).Ephemeral();

    var isWatched = watched.Toggle(context.Chat.Id, id);

    return context.Reply(ExampleTexts.WatchResult(language, id, isWatched)).Ephemeral();
  }
}
