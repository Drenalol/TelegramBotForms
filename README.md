# Drenalol.Telegram.Bot.Forms

[![NuGet](https://img.shields.io/nuget/v/Drenalol.Telegram.Bot.Forms.svg)](https://www.nuget.org/packages/Drenalol.Telegram.Bot.Forms/)
[![CI](https://github.com/Drenalol/TelegramBotForms/actions/workflows/ci.yml/badge.svg)](https://github.com/Drenalol/TelegramBotForms/actions/workflows/ci.yml)
[![license](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

A chat-first Telegram bot framework on top of [Telegram.Bot](https://github.com/TelegramBots/Telegram.Bot):
conversations are **forms** resolved from DI, every chat has a **navigation history**
with a working "Back", browsers come with **pagination and search** out of the box.

Born in a production trading-bot ([stock-indicators.app](https://stockindicators.app)) where
it serves a localized menu bot with subscription/instrument/indicator browsers, in-bot
registration and settings.

## Features

- **Forms with DI** — every conversation step resolves its form from a fresh DI scope;
  constructor injection for repositories, mediators, whatever your app uses.
- **Navigation history** — a per-chat stack of visited forms; the "Back" button pops it,
  skips completed wizards, resets waiting ones and falls back to `/start`.
- **Stateful forms (wizards)** — `ExtendedFormBase<TState>`: named states, render actions,
  navigation between forms; a form waiting for free-text input receives the next message
  of that chat (input is not eaten by routing).
- **Keyboard-driven navigation** — `NavigationFormBase<TState>` maps button texts to states;
  button texts double as routing keys, so a localized bot just registers every language
  variant of a button with the same form (collisions fail fast at startup).
- **Pagination** — `PagedFormBase<TItem>`: pages with ◀ Prev / Next ▶ in the chat's
  language, free-text search, page-scoped readable row labels (with `#2` uniquification —
  raw GUIDs overflow the reply-markup limits), stale-row protection.
- **Free-text commands** — `IBotCommand` matched by the first token (`help`, `/help ...`),
  winning over keyboards and over the waiting form.
- **Per-chat localization** — `ITelegramTextLocalizer` + `IChatLanguageResolver`, ru/en
  default texts included; your catalog plugs in with one interface.
- **Code-owned BotFather profile** — name, descriptions and the `/` menu are pushed on
  every start (read-before-write, 429-aware), so BotFather never drifts from your repo.
- **Deep links** — `t.me/<bot>?start=<param>` routes to your start form with the parameter.
- **Inline-button friendly** — callback taps are acknowledged and replayed as routing text,
  so inline keyboards navigate exactly like reply-keyboard taps.
- **Utilities** — curated `TelegramTimeZones` quick-pick list, `KeyboardCache`,
  `MessageContext` fluent replies, per-form render timeouts.

No ASP.NET needed — the bot runs long polling as a `BackgroundService` in any host
(console, worker, web). Multi-targets `netstandard2.1` and `net10.0`.

## How it works

```
Telegram ──long polling──► TelegramBotBus.Income ──► IFormRetriever (router)
                                                       │ keys / commands / chat history
                                                       ▼
                                            form.RenderAsync(context)
                                                       │
                                   TelegramBotBus.Outcome ──► SendMessage ──► Telegram
```

- `TelegramBotStartup` (a hosted service) polls and feeds `Income`;
- the router resolves the incoming text to a form: registered commands win, then
  registered keyboard keys, then the stateful form atop the chat's history;
- the rendered `MessageContext` goes to `Outcome` and is sent back to the chat.

## Quick start

```shell
dotnet add package Drenalol.Telegram.Bot.Forms
```

```csharp
using Drenalol.Telegram.Bot.Forms;

var builder = Host.CreateApplicationBuilder(args);

var bot = builder.Services.AddTelegramBotForms(options =>
{
  options.Token = builder.Configuration["TelegramBotForms:Token"]!;
  options.BotLink = "https://t.me/my_example_bot";
});

bot.RegisterStartForm<MainForm>()
   .RegisterFormWithKey<ProductsForm>("Products")
   .RegisterCommand<HelpCommand>("help")
   .Build();

await builder.Build().RunAsync();
```

```csharp
public sealed class MainForm : FormBase
{
  public override Task<MessageContext> RenderAsync(MessageContext context, CancellationToken cancellationToken) =>
    Task.FromResult(context.Reply("Hello! The buttons below open the bot.",
      [[new KeyboardButton("Products")], [new KeyboardButton("help")]]));
}
```

> **One poller per bot token.** Telegram hands every update to a single long poller —
> running dev and prod with the same token steals messages between environments. Use a
> separate dev bot.

## Forms and DI

Extend `FormBase`; dependencies come from a service scope owned by the form instance.
Stateful forms keep that scope alive while they sit in the chat's history; the scope is
disposed when the form leaves the history ("Back", clear-on-navigate).

```csharp
public sealed class ProductForm(IProductRepository repository) : FormBase
{
  public override async Task<MessageContext> RenderAsync(MessageContext context, CancellationToken cancellationToken)
  {
    var product = await repository.GetAsync(context.Chat, cancellationToken);

    return context.Reply(product.Description, [await CreateBackButtonRowAsync(context.Chat, cancellationToken)]);
  }
}
```

Register navigation-only forms (no keyboard text) with `RegisterForm<T>()`; reach them
with `NavigateToAsync<TForm>(...)` from another form, forwarding a new routing text:

```csharp
return NavigateToAsync<ProductForm>(context.Forward(productId.ToString()), cancellationToken);
```

`ClearHistoryOnNavigate => true` makes a form a navigation root (a "home" page): entering
it empties the history, so "Back" from anywhere below returns to it.

## History and "Back"

Every chat owns a stack of visited forms (`FormCollection` state, keyed by chat id).
Pressing "Back" (any registered language variant of the localized `Back` text):

1. pops the current form;
2. completed stateful forms (`IsCompleted`) are skipped — there is no conversation to
   return to;
3. a waiting stateful form is `Reset()` and re-entered;
4. an exhausted history falls back to the start form.

## Stateful forms (wizards)

`ExtendedFormBase<TState>` turns any enum into a conversation state machine. The first
render initialises; later renders route the incoming text via `GetRender`. While a state
awaits free input, any unregistered text of that chat reaches the form — that is how
in-bot "type your name / email / city" flows work.

```csharp
public enum ProfileState { Show, EditName }

public sealed class ProfileForm(ProfileStore store) : ExtendedFormBase<ProfileState>
{
  private readonly Dictionary<string, ProfileState> _buttons = new(StringComparer.OrdinalIgnoreCase);

  public ProfileForm()
  {
    AddRenderOn(ProfileState.Show, string.Empty, ShowAsync);
    AddRenderOn(ProfileState.EditName, string.Empty, AskNameAsync);
    _buttons["Change name"] = ProfileState.EditName;
  }

  protected override Task<MessageContext> RenderInitAsync(MessageContext context, CancellationToken ct) =>
    ShowAsync(context, ct);

  protected override IRender GetRender(MessageContext context) =>
    _buttons.TryGetValue(context.Text, out var state) ? Renders[state]
    // the wizard is waiting for free input: any other text is the new name
    : State == ProfileState.EditName ? CustomRender(SaveNameAsync)
    : throw new InvalidOperationException($"Nothing routes: {context.Text}");

  // ... ShowAsync / AskNameAsync / SaveNameAsync as in example-bot/Bot/ProfileForm.cs
}
```

`NavigationFormBase<TState>` is the keyboard-driven flavour: `AddRow(text, state)` maps
button presses to states, `BuildKeyboard()` renders them, `AddFormRenderOn<TForm>(state)`
navigates to another form.

## Pagination

`PagedFormBase<TItem>` implements the whole browser; the application describes the data:

```csharp
public sealed class ProductsForm(IProductService products) : PagedFormBase<Product>
{
  protected override int PageSize => 20;

  protected override Task<PagedPage<Product>> LoadPageAsync(string? search, int page, int pageSize, CancellationToken ct) =>
    products.SearchAsync(search, page, pageSize, ct);

  protected override string EncodeRow(Product item) => $"{item.Ticker} · {item.Exchange}";

  protected override Task<MessageContext> RenderItemAsync(Product item, MessageContext context, CancellationToken ct) =>
    NavigateToAsync<ProductForm>(context.Forward(item.Id.ToString()), ct);

  protected override Task<string> BuildTextAsync(IReadOnlyList<Product> items, int page, int totalPages, CancellationToken ct) =>
    Task.FromResult($"Products — page {page} of {totalPages}");
}
```

You get: readable row labels made unique per page (`"label #2"`), a page-scoped
label→item map (so no GUIDs ever enter the keyboard), ◀ Prev / Next ▶ built only when
needed, page clamping when the last item of a page is deleted, free-text search
(any unregistered message filters; override `SupportsSearch` to turn it off), a "Back"
row in the chat's language, and `AddActionRow` for custom rows with their own routing.

## Commands

```csharp
public sealed class LinkCommand(IAccountService accounts) : IBotCommand
{
  public string Prefix => "link";

  public async Task<MessageContext> ExecuteAsync(MessageContext context, CancellationToken ct)
  {
    var token = context.Text.Split(' ')[1]; // "link <token>" or "/link <token>"
    await accounts.LinkAsync(context.Chat.Id, token, ct);

    return context.Reply("This chat is linked.");
  }
}
```

Register with `.RegisterCommand<LinkCommand>("link")`. Commands are resolved from DI in
their own scope and win over keyboard keys and over the waiting form.

## Localization

`ITelegramTextLocalizer` supplies the four framework-owned texts (`Back`, `PreviousPage`,
`NextPage`, `UnhandledError`) per language; `DefaultTextLocalizer` covers Russian and
English. `IChatLanguageResolver` decides which language each chat speaks (default: always
the default language — store per-chat or per-user languages yourself).

```csharp
services.AddSingleton<ITelegramTextLocalizer, MyCatalog>();     // replace the default texts
services.AddSingleton<IChatLanguageResolver, MyChatLanguages>(); // resolve per chat
```

Register **every language variant** of a button text with the form it opens — texts are
routing keys; two different forms claiming one text throw at startup:

```csharp
foreach (var language in catalog.Languages)
  bot.RegisterFormWithKey<ProductsForm>(catalog.ProductsButton(language));
```

Forms ask their language with `GetLanguageAsync(context.Chat, ct)` and build localized
keyboards per render.

## BotFather profile

Keep the bot's name, descriptions and `/` menu in code:

```csharp
services.Configure<BotProfileOptions>(profile =>
{
  profile.Name = "My Bot";
  profile.Languages["en"] = new BotProfileTexts
  {
    ShortDescription = "Tracks my tickers.",
    Welcome = "Press /start to begin.",
    Commands = { new BotProfileCommand("start", "Open the menu") },
  };
});
```

Texts are pushed on every start, read-before-write (Telegram throttles these methods
hard; a 429 aborts the run instead of deepening the penalty), regional codes fall back
to their bare prefix. Failures never stop polling. Leave `BotProfileOptions` empty to
keep BotFather in charge.

## Deep links

`TelegramBotFormsOptions.DeepLink("confirm_42")` builds `t.me/<bot>?start=confirm_42`;
the press arrives as the message `/start confirm_42` and routes to your start form,
which reads the parameter from `context.Text`.

## Utilities

- `TelegramTimeZones.Popular()` — IANA quick-pick list with current UTC offsets for
  settings forms; `FormatLabel(id)` for arbitrary ids.
- `KeyboardCache` — process-wide static keyboards; `GlobalKeyboardFormBase` builds one
  keyboard per form type.
- `MessageContext` — `Reply(text, keyboardRows)`, `Reply(text, replyMarkup: ...)`,
  `Forward(newRoutingText)`; parse mode, entities and reply-to are carried to the sender.
- Per-form render timeout via `IForm.TimeoutSec` (default 30 s).

## Example bot

[`example-bot/`](example-bot) demonstrates every feature end to end: consent-gated start
with a deep-link parameter, a ru/en main menu, a paginated + searchable products list
with a custom action row, a detail page reached by navigation, an input wizard, a
per-chat language switcher, a time zone picker, a `/help` command and a code-owned
bot profile.

Put a bot token (create one with [@BotFather](https://t.me/BotFather)) into user secrets
and run:

```shell
cd example-bot
dotnet user-secrets init
dotnet user-secrets set "TelegramBotForms:Token" "123456:ABC-your-token"
dotnet run
```

## Repository layout

| Path | What |
|---|---|
| `telegram-bot-forms/` | the library (multi-targets `netstandard2.1` + `net10.0`) |
| `telegram-bot-forms.tests/` | NUnit tests for routing, history, pagination, bus |
| `example-bot/` | console bot demonstrating every feature |
| `.github/workflows/ci.yml` | build + test on PR; pack & publish to nuget.org and GitHub Packages on master |

## Publishing

The version lives in `telegram-bot-forms/telegram-bot-forms.csproj` (`<Version>`).
Bump it, commit and push to `master`: CI packs and pushes the `.nupkg`/`.snupkg` to
**nuget.org** (Trusted Publishing OIDC — one-time setup on nuget.org under
*your profile → API Keys → Configure Trusted Publishing*: owner `Drenalol`, project
`TelegramBotForms`, workflow `ci.yml`; the repo needs a `NUGET_USER` secret with your
nuget.org **profile name**) and mirrors the package to **GitHub Packages** with the
built-in `GITHUB_TOKEN`. A `v<Version>` git tag and a GitHub Release with the packages
attached are created too (skipped when already present).

## License

[MIT](LICENSE) © 2026 Bogdan Yanysh
