namespace Drenalol.Telegram.Bot.Forms.Localization;

/// <summary>
/// Russian and English texts for the framework-owned UI, the zero-config default.
/// Replace it with a custom <see cref="ITelegramTextLocalizer"/> for more languages
/// or different wording.
/// </summary>
public class DefaultTextLocalizer : ITelegramTextLocalizer
{
  /// <summary>Russian.</summary>
  public const string Russian = "ru";

  /// <summary>English.</summary>
  public const string English = "en";

  private static readonly Dictionary<(TextKey Key, string Language), string> Catalog = new()
  {
    [(TextKey.Back, Russian)] = "Назад",
    [(TextKey.Back, English)] = "Back",
    [(TextKey.PreviousPage, Russian)] = "◀ Назад",
    [(TextKey.PreviousPage, English)] = "◀ Prev",
    [(TextKey.NextPage, Russian)] = "Вперёд ▶",
    [(TextKey.NextPage, English)] = "Next ▶",
    [(TextKey.UnhandledError, Russian)] = "Что-то пошло не так. Попробуйте ещё раз или напишите разработчикам.",
    [(TextKey.UnhandledError, English)] = "Something went wrong. Please try again or contact the developers.",
  };

  public string DefaultLanguage => English;

  public IReadOnlyList<string> SupportedLanguages { get; } = [Russian, English];

  public string Get(TextKey key, string language) =>
    Catalog.TryGetValue((key, language), out var text)
      ? text
      : Catalog[(key, DefaultLanguage)];
}
