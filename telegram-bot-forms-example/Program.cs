using Drenalol.Telegram.Bot.Forms;
using Drenalol.Telegram.Bot.Forms.Localization;
using Drenalol.Telegram.Bot.Forms.Profile;
using ExampleBot.Bot;
using ExampleBot.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

// Real bot tokens live in the gitignored appsettings.Local.json (next to the committed
// placeholder appsettings.json), so secrets never reach git.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);

// Application services. The custom localizer and chat-language resolver are registered
// BEFORE AddTelegramBotForms, so its TryAdd defaults (English-only texts, single
// language) do not win over the application's own implementations.
builder.Services.AddSingleton<ExampleTexts>();
builder.Services.AddSingleton<ITelegramTextLocalizer>(sp => sp.GetRequiredService<ExampleTexts>());
builder.Services.AddSingleton<ChatLanguageStore>();
builder.Services.AddSingleton<IChatLanguageResolver>(sp => sp.GetRequiredService<ChatLanguageStore>());
builder.Services.AddSingleton<ProfileStore>();
builder.Services.AddSingleton<ConsentStore>();
builder.Services.AddSingleton<TimeZoneStore>();
builder.Services.AddSingleton<ProductCatalog>();
builder.Services.AddSingleton<WatchStore>();

var bot = builder.Services.AddTelegramBotForms(options =>
{
  // The token comes from appsettings.Local.json (gitignored, loaded above);
  // never commit real tokens to the placeholder appsettings.json.
  options.Token = builder.Configuration["TelegramBotForms:Token"] ?? string.Empty;
  options.BotLink = builder.Configuration["TelegramBotForms:BotLink"] ?? "https://t.me/your_example_bot";
});

// The BotFather profile (name, descriptions, "/" menu) is code-owned: pushed on every
// start, so BotFather itself never drifts from the repository.
builder.Services.Configure<BotProfileOptions>(profile =>
{
  profile.Name = "Example Forms Bot";
  profile.DefaultLanguage = ExampleTexts.English;

  profile.Languages[ExampleTexts.English] = new BotProfileTexts
  {
    ShortDescription = "Shows what the forms framework can do: history, pagination, wizards.",
    Welcome = "This bot demonstrates Drenalol.Telegram.Bot.Forms. Press /start to begin.",
    Commands = { new BotProfileCommand("start", "Open the main menu"), new BotProfileCommand("language", "Change the language") },
  };

  profile.Languages[ExampleTexts.Russian] = new BotProfileTexts
  {
    ShortDescription = "Демонстрация фреймворка: история, пагинация, мастера ввода.",
    Welcome = "Этот бот показывает Drenalol.Telegram.Bot.Forms. Нажмите /start.",
    Commands = { new BotProfileCommand("start", "Открыть главное меню"), new BotProfileCommand("language", "Сменить язык") },
  };
});

bot.RegisterStartForm<StartForm>();

// Keyboard texts are routing keys, so every language variant of a menu button must be
// registered with the form it opens.
foreach (var language in ExampleTexts.LanguageCodes)
{
  bot
    .RegisterFormWithKey<ProductsForm>(ExampleTexts.ProductsButton(language))
    .RegisterFormWithKey<ProfileForm>(ExampleTexts.ProfileButton(language))
    .RegisterFormWithKey<LanguageForm>(ExampleTexts.LanguageButton(language))
    .RegisterFormWithKey<TimeZoneForm>(ExampleTexts.TimeZoneButton(language))
    .RegisterFormWithKey<PlaceForm>(ExampleTexts.PlaceButton(language))
    // the consent button replays its text, so every language variant routes the start form
    .RegisterFormWithKey<StartForm>(ExampleTexts.ConsentButton(language));
}

// the picker is reachable as /language too (typed without the slash behaves the same)
bot
  .RegisterFormWithKey<LanguageForm>("/language")
  .RegisterFormWithKey<LanguageForm>("language");

// reachable by navigation only (from the products list)
bot.RegisterForm<ProductForm>();

// free-text command: "help" or "/help"
bot.RegisterCommand<HelpCommand>("help");

// the same command is what the watch button of the rich product card carries
// ("watch 42"): a callback inside a message has to route on its own, and commands beat
// keyboard keys and the form waiting in the chat's history
bot.RegisterCommand<WatchCommand>("watch");

// Optional chat-history persistence: "Back", wizard steps and paged lists survive a
// restart. This keeps one JSON file per chat locally; for several replicas or a
// centralized store use Redis instead:
//   builder.Services.AddStackExchangeRedisCache(o => o.Configuration = "...");
//   bot.UseDistributedHistory(o => o.TimeToLive = TimeSpan.FromDays(7));
bot.UseFileHistory(history => history.Directory = Path.Combine(AppContext.BaseDirectory, "bot-history"));

bot.Build();

var host = builder.Build();

await host.RunAsync();
