namespace Drenalol.Telegram.Bot.Forms;

/// <summary>Bot connection options (bound from configuration or set inline).</summary>
public class TelegramBotFormsOptions
{
  /// <summary>The bot token from BotFather. Required; one poller per token.</summary>
  public string Token { get; set; } = null!;

  /// <summary>
  /// Link to this bot, e.g. https://t.me/my_example_bot. Used to build deep links
  /// (<see cref="DeepLink"/>) shown by the application.
  /// </summary>
  public string BotLink { get; set; } = string.Empty;

  /// <summary>
  /// Deep link that opens a private chat with the bot and passes a start parameter
  /// (<c>t.me/&lt;bot&gt;?start=&lt;parameter&gt;</c>); the parameter arrives as
  /// "/start &lt;parameter&gt;" and routes to the start form.
  /// </summary>
  public string DeepLink(string startParameter) => $"{BotBaseUri}?start={Uri.EscapeDataString(startParameter)}";

  private string BotBaseUri
  {
    get
    {
      if (!Uri.TryCreate(BotLink, UriKind.Absolute, out var uri))
        return string.Empty;

      return $"{uri.Scheme}://{uri.Authority}{uri.AbsolutePath.TrimEnd('/')}";
    }
  }
}
