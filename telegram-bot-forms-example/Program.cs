using Drenalol.Telegram.Bot.Forms;
using Drenalol.Telegram.Bot.Forms.Localization;
using Drenalol.Telegram.Bot.Forms.Profile;
using ExampleBot.Bot;
using ExampleBot.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

// Application services. The custom localizer and chat-language resolver are registered
// BEFORE AddTelegramBotForms, so its TryAdd defaults (English-only texts, single
// language) do not win over the application's own implementations.
builder.Services.AddSingleton<ExampleTexts>();
builder.Services.AddSingleton<ITelegramTextLocalizer>(sp => sp.GetRequiredService<ExampleTexts>());
builder.Services.AddSingleton<ChatLanguageStore>();
builder.Services.AddSingleton<IChatLanguageResolver>(sp => sp.GetRequiredService<ChatLanguageStore>());
builder.Services.AddSingleton<ProfileStore>();
builder.Services.AddSingleton<ProductCatalog>();

var bot = builder.Services.AddTelegramBotForms(options =>
{
  // Put your own bot token into appsettings.Development.json (gitignored) or
  // `dotnet user-secrets set "TelegramBotForms:Token" "123:abc"` - never into git.
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

bot.Build();

var host = builder.Build();

await host.RunAsync();
