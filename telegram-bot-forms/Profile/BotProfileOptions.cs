namespace Drenalol.Telegram.Bot.Forms.Profile;

/// <summary>
/// The BotFather profile texts the framework owns and pushes on every start (see
/// <see cref="BotProfile"/>). Leave everything unset to keep BotFather in charge.
/// </summary>
public sealed class BotProfileOptions
{
  /// <summary>Bot display name (the default variant; names are not localized).</summary>
  public string? Name { get; set; }

  /// <summary>Language whose texts become the default (null-language) variant.</summary>
  public string DefaultLanguage { get; set; } = "en";

  /// <summary>Per-language profile texts, keyed by Telegram language code ("en", "ru", "pt-BR", ...).</summary>
  public IDictionary<string, BotProfileTexts> Languages { get; } = new Dictionary<string, BotProfileTexts>(StringComparer.OrdinalIgnoreCase);

  internal bool IsConfigured => !string.IsNullOrWhiteSpace(Name) || Languages.Count > 0;
}

/// <summary>One language variant of the bot profile.</summary>
public sealed class BotProfileTexts
{
  /// <summary>Short description shown on the bot's profile page.</summary>
  public string? ShortDescription { get; init; }

  /// <summary>The welcome card shown in an empty chat.</summary>
  public string? Welcome { get; init; }

  /// <summary>The "/" command menu (the start command should be first).</summary>
  public IList<BotProfileCommand> Commands { get; } = new List<BotProfileCommand>();
}

/// <summary>A slash-command menu entry.</summary>
public sealed record BotProfileCommand(string Command, string Description);
