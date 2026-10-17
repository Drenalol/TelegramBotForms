using Drenalol.Telegram.Bot.Forms.Localization;
using ExampleBot.Data;
using Telegram.Bot.Types.ReplyMarkups;

namespace ExampleBot.Bot;

/// <summary>
/// The application's own catalog (ru + en). Implements the framework's
/// <see cref="ITelegramTextLocalizer"/> for the framework-owned keys and adds the
/// example's own strings as statics, so registration can use them before DI is built.
/// </summary>
public sealed class ExampleTexts : ITelegramTextLocalizer
{
  public const string Russian = "ru";
  public const string English = "en";

  public static readonly IReadOnlyList<string> LanguageCodes = [Russian, English];

  private static readonly Dictionary<(TextKey Key, string Language), string> FrameworkTexts = new()
  {
    [(TextKey.Back, Russian)] = "Назад",
    [(TextKey.Back, English)] = "Back",
    [(TextKey.PreviousPage, Russian)] = "◀ Назад",
    [(TextKey.PreviousPage, English)] = "◀ Prev",
    [(TextKey.NextPage, Russian)] = "Вперёд ▶",
    [(TextKey.NextPage, English)] = "Next ▶",
    [(TextKey.UnhandledError, Russian)] = "Что-то пошло не так. Попробуйте ещё раз.",
    [(TextKey.UnhandledError, English)] = "Something went wrong. Please try again.",
  };

  public string DefaultLanguage => English;

  public IReadOnlyList<string> SupportedLanguages => LanguageCodes;

  public string Get(TextKey key, string language) =>
    FrameworkTexts.TryGetValue((key, language), out var text) ? text : FrameworkTexts[(key, DefaultLanguage)];

  public static string NativeName(string language) => language == Russian ? "Русский" : "English";

  // --- start / consent -------------------------------------------------------------

  public static string Welcome(string language, string? deepLinkParameter) =>
    (language == Russian
      ? "Добро пожаловать в Example Forms Bot!\n\nЯ показываю возможности Drenalol.Telegram.Bot.Forms: формы с DI, историю навигации с «Назад», список с пагинацией и поиском, мастера ввода, команды и бот-профиль.\n\nЧтобы продолжить, примите «правила» (кнопка ниже)."
      : "Welcome to the Example Forms Bot!\n\nI demonstrate Drenalol.Telegram.Bot.Forms: DI-scoped forms, navigation history with \"Back\", a paged and searchable list, input wizards, commands and the bot profile.\n\nTo continue, accept the \"terms\" (button below).")
    + DeepLinkNote(language, deepLinkParameter);

  public static string ConsentButton(string language) => language == Russian ? "Принять правила" : "I accept the Terms";

  public static bool IsConsent(string text) =>
    LanguageCodes.Any(language => string.Equals(text.Trim(), ConsentButton(language), StringComparison.OrdinalIgnoreCase));

  public static string Greeting(string language, string? deepLinkParameter) => language == Russian
    ? $"Готово! Главное меню ниже — кнопки открывают формы, «{FrameworkTexts[(TextKey.Back, Russian)]}» возвращает назад." + DeepLinkNote(language, deepLinkParameter)
    : "Done! The main menu is below - the buttons open forms and \"Back\" walks the history." + DeepLinkNote(language, deepLinkParameter);

  private static string DeepLinkNote(string language, string? parameter) => parameter is null
    ? string.Empty
    : language == Russian
      ? $"\n\nПараметр deep-link: {parameter}"
      : $"\n\nDeep-link parameter: {parameter}";

  // --- main menu -------------------------------------------------------------------

  public static string ProductsButton(string language) => language == Russian ? "Товары" : "Products";
  public static string ProfileButton(string language) => language == Russian ? "Профиль" : "Profile";
  public static string LanguageButton(string language) => language == Russian ? "Язык" : "Language";
  public static string TimeZoneButton(string language) => language == Russian ? "Часовой пояс" : "Time zone";

  public static bool IsMainButton(string text) =>
    LanguageCodes.Any(language =>
      ProductsButton(language) == text ||
      ProfileButton(language) == text ||
      LanguageButton(language) == text ||
      TimeZoneButton(language) == text);

  public static ReplyKeyboardMarkup MainKeyboard(string language) =>
    new([[new KeyboardButton(ProductsButton(language))], [new KeyboardButton(ProfileButton(language))], [new KeyboardButton(LanguageButton(language))], [new KeyboardButton(TimeZoneButton(language))]])
    {
      ResizeKeyboard = true,
      OneTimeKeyboard = false,
    };

  // --- products --------------------------------------------------------------------

  public static string ProductsTitle(string language, int count, int page, int totalPages, string? search) => language == Russian
    ? $"Товары: {count}" + (search is null ? "" : $" по запросу «{search}»") + $"\nСтраница {page} из {totalPages}. Напишите любой текст, чтобы искать."
    : $"Products: {count}" + (search is null ? "" : $" matching \"{search}\"") + $"\nPage {page} of {totalPages}. Type any text to search.";

  public static string RefreshButton(string language) => language == Russian ? "Обновить" : "Refresh";

  public static string ProductCard(string language, Product product) => language == Russian
    ? $"{product.Name}\nКатегория: {product.Category}\nЦена: {product.Price:0.##} $\nID: {product.Id}"
    : $"{product.Name}\nCategory: {product.Category}\nPrice: {product.Price:0.##} $\nID: {product.Id}";

  // --- profile wizard ----------------------------------------------------------------

  public static string ProfileTitle(string language, string name) => language == Russian
    ? $"Ваш профиль.\nИмя: {name}\n\n«Изменить имя» - мастер ввода: бот попросит написать новое имя свободным текстом (свободный текст не съедается историей, пока форма ждёт ввод)."
    : $"Your profile.\nName: {name}\n\n\"Change name\" starts an input wizard: the bot waits for the new name as free text (input is not eaten by the history while a form awaits it).";

  public static string ChangeNameButton(string language) => language == Russian ? "Изменить имя" : "Change name";

  public static string AskName(string language) => language == Russian
    ? "Напишите новое имя одним сообщением:"
    : "Type the new name as a single message:";

  public static string NameSaved(string language, string name) => language == Russian
    ? $"Имя сохранено: {name}"
    : $"Name saved: {name}";

  // --- language / timezone / help ----------------------------------------------------

  public static string LanguageTitle(string language) => language == Russian
    ? "Выберите язык бота:"
    : "Choose the bot language:";

  public static string TimeZoneTitle(string language, string current) => language == Russian
    ? $"Ваш часовой пояс: {current}\nВыберите другой:"
    : $"Your time zone: {current}\nPick another one:";

  public static string TimeZoneSaved(string language, string id) => language == Russian
    ? $"Часовой пояс сохранён: {id}"
    : $"Time zone saved: {id}";

  public static string Help(string language) => language == Russian
    ? "Команды:\n/help - эта справка\n/language - выбор языка\n/start - главное меню\n\nСовет: напишите любой текст, когда открыт список товаров, - он станет поисковым запросом."
    : "Commands:\n/help - this help\n/language - pick a language\n/start - main menu\n\nTip: type any text while the products list is open - it becomes a search query.";
}

