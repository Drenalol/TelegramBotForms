namespace Drenalol.Telegram.Bot.Forms.Localization;

/// <summary>
/// Supplies the framework-owned UI texts (<see cref="TextKey"/>) per language code.
/// Register your own implementation to control every button the framework renders;
/// the application's own texts live in the application, this interface only covers
/// what the framework itself draws (Back, pagination, the error reply).
/// </summary>
public interface ITelegramTextLocalizer
{
  /// <summary>Language code used when a chat has no explicit language.</summary>
  string DefaultLanguage { get; }

  /// <summary>
  /// All language codes the bot renders in. Their "Back" variants are all registered
  /// as the back-navigation button, so a localized back press works in every language.
  /// </summary>
  IReadOnlyList<string> SupportedLanguages { get; }

  /// <summary>Text of <paramref name="key"/> in <paramref name="language"/>.</summary>
  string Get(TextKey key, string language);
}

public static class TelegramTextLocalizerExtensions
{
  public static string Back(this ITelegramTextLocalizer localizer, string? language = null) =>
    localizer.Get(TextKey.Back, language ?? localizer.DefaultLanguage);

  public static string PreviousPage(this ITelegramTextLocalizer localizer, string language) =>
    localizer.Get(TextKey.PreviousPage, language);

  public static string NextPage(this ITelegramTextLocalizer localizer, string language) =>
    localizer.Get(TextKey.NextPage, language);

  public static string UnhandledError(this ITelegramTextLocalizer localizer, string? language = null) =>
    localizer.Get(TextKey.UnhandledError, language ?? localizer.DefaultLanguage);
}
