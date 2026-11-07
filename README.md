# Drenalol.Telegram.Bot.Forms

[![NuGet](https://img.shields.io/nuget/v/Drenalol.Telegram.Bot.Forms.svg)](https://www.nuget.org/packages/Drenalol.Telegram.Bot.Forms/)
[![CI](https://github.com/Drenalol/TelegramBotForms/actions/workflows/ci.yml/badge.svg)](https://github.com/Drenalol/TelegramBotForms/actions/workflows/ci.yml)
[![license](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

A chat-first Telegram bot framework on top of [Telegram.Bot](https://github.com/TelegramBots/Telegram.Bot):
conversations are **forms** resolved from DI, every chat has a **navigation history**
with a working "Back", browsers come with **pagination and search** out of the box.

## Features

- **Forms with DI** — every conversation step resolves its form from a fresh DI scope;
  constructor injection for repositories, mediators, whatever your app uses.
- **Navigation history** — a per-chat stack of visited forms; the "Back" button pops it,
  skips completed wizards, resets waiting ones and falls back to `/start`.
- **No duplicate screens** — re-tapping the key of a form that is already open, or
  navigating to it, rewinds to the waiting instance instead of stacking a copy: the
  forms above are popped and disposed, the target is reset and re-rendered in place,
  so "Back" never paints the same screen twice.
- **Durable history (optional)** — the same stack, plus the state your forms choose to
  capture, survives a restart: plug in a file store or any `IDistributedCache` (Redis),
  and after the start every chat resumes where it left off.
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
- **Shared location & contact** — reply-keyboard buttons requesting a location or a contact
  arrive at the waiting form as the structured `context.Location` / `context.Contact` plus
  their readable text encoding (`"55.7558,37.6173"` / the phone number).
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

- `TelegramBotStartup` (a hosted service) polls and feeds `Income`; shared locations and
  contacts enter the pipeline as their text encoding plus the structured payload;
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

Navigating to a form that is **already open** in the chat — by re-tapping its keyboard
key or with `NavigateToAsync<TForm>` — does not stack a second copy either: the target
is located anywhere in the history, everything above it is popped and disposed, and the
waiting instance is `Reset()` and re-rendered in place (a completed stateful target
leaves the history and a fresh instance is pushed). One screen in the stack means one
"Back" press to leave it.

## Durable history (optional)

By default the history is in-memory and a restart empties it (the bot keeps answering,
every chat just starts fresh). To make histories survive restarts, register a store —
the router then restores a chat's stack lazily on its first message after the start and
writes the position back after every settled turn:

```csharp
// one JSON file per chat under Directory (atomic writes, single instance):
bot.UseFileHistory(history => history.Directory = "/var/lib/mybot/history");

// ...or any IDistributedCache - Redis here, in-memory works too (great for tests):
builder.Services.AddStackExchangeRedisCache(o => o.Configuration = "localhost:6379");
bot.UseDistributedHistory(history => history.TimeToLive = TimeSpan.FromDays(7));
```

Both stores keep one JSON snapshot per chat (form type keys + state, top first) and
expire it after `TimeToLive`, renewed on every turn. Registering your own
`IChatHistoryStore` as a singleton works as well. A store is treated as a cache:
every failure (disk full, Redis down) costs a chat its "Back" and never breaks message
processing.

**What is saved.** The navigation stack itself (so "Back" works again), the framework
state of stateful forms (current wizard state, page and search of `PagedFormBase` —
a restored browser re-arms its navigation buttons, and a tap on a pre-restart row just
repaints the page) and the user data each form opts into:

```csharp
public sealed class PlaceForm : ExtendedFormBase<PlaceState>
{
  // declare what leaves the process, as a simple record of serializable values:
  protected override object? CaptureContext() => new Draft(_address, _phone);

  protected override void RestoreContext(JsonNode context)
  {
    var draft = context.Deserialize<Draft>();
    (_address, _phone) = (draft.Address, draft.Phone);
  }

  private record Draft(string? Address, string? Phone);
}
```

**Pain-free upgrade notes.**

- A form that overrides nothing persists only its position: wizards restore to the
  right state but without collected answers. Anything nontrivial needs `CaptureContext`.
- Forms are identified by `Type.FullName` — renaming or removing a form type silently
  drops that entry from stored snapshots (the rest of the history still restores).
  Renaming a wizard state value or a `CaptureContext` field has the same effect.
- Snapshots may hold personal data collected by your wizards: TTL limits the exposure,
  the choice of disk/directory (permissions, encryption) and any GDPR retention beyond
  the TTL is yours.
- The file store is per-machine: run a single bot instance with it (long polling is
  one-poller-per-token anyway) and use the distributed-cache store with replicas.
- A restored wizard can resume onto data that changed while the bot slept; forms that
  load entity state from repositories on render already behave correctly.
- The in-memory histories of never-restored chats are not eagerly loaded — a snapshot
  is read only when the chat writes again.

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

  // ... ShowAsync / AskNameAsync / SaveNameAsync as in telegram-bot-forms-example/Bot/ProfileForm.cs
}
```

`NavigationFormBase<TState>` is the keyboard-driven flavour: `AddRow(text, state)` maps
button presses to states, `BuildKeyboard()` renders them, `AddFormRenderOn<TForm>(state)`
navigates to another form. Input the keyboard does not register re-shows the current
state (`ReenterOnUnknownText`) instead of erroring — `PagedFormBase` overrides the hook
to filter the list.

## Location and contact

A reply-keyboard button with `RequestLocation` / `RequestContact` makes Telegram send
the payload instead of the button text. Such a message reaches the free-input form
waiting in the history from both sides: the structured `context.Location` /
`context.Contact` and a readable text encoding of the payload (`"55.7558,37.6173"`,
the phone number) in `context.Text` — encodings never collide with registered keys,
and text-only flows keep working untouched. A funnel that prefers the structured
payload and falls back to text:

```csharp
private Task<MessageContext> WaitAsync(MessageContext context, CancellationToken ct)
{
  var rows = new[]
  {
    new[] { new KeyboardButton("📍 My location") { RequestLocation = true } },
    new[] { new KeyboardButton("👤 My contact") { RequestContact = true } },
  };

  if (context.Location is { } location)
    return Task.FromResult(context.Reply($"Got {location.Latitude}, {location.Longitude}", rows));

  if (context.Contact is { } contact)
    return Task.FromResult(context.Reply($"Got {contact.FirstName} {contact.PhoneNumber}", rows));

  return Task.FromResult(context.Reply($"Got text: {context.Text}", rows));
}
```

Everything else (photos, stickers, ...) is not consumed; the acceptance switch lives in
one method of `TelegramBotProvider`, so new message types are a single branch there.

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
  `Location` / `Contact` carry the structured payload of a shared location / contact
  (null for any other incoming message).
- Per-form render timeout via `IForm.TimeoutSec` (default 30 s).

## Example bot

[`telegram-bot-forms-example/`](telegram-bot-forms-example) demonstrates every feature end to end: consent-gated start
with a deep-link parameter, a ru/en main menu, a paginated + searchable products list
with a custom action row, a detail page reached by navigation, an input wizard, a
location/contact/text funnel, a per-chat language switcher, a time zone picker, a
`/help` command and a code-owned bot profile.

Put a bot token (create one with [@BotFather](https://t.me/BotFather)) into the gitignored
`appsettings.Local.json` (next to the committed placeholder `appsettings.json`) and run:

```shell
cd telegram-bot-forms-example
cat > appsettings.Local.json <<'EOF'
{
  "TelegramBotForms": { "Token": "123456:ABC-your-token" }
}
EOF
dotnet run
```

## Repository layout

| Path | What |
|---|---|
| `telegram-bot-forms/` | the library (multi-targets `netstandard2.1` + `net10.0`) |
| `telegram-bot-forms.tests/` | NUnit tests for routing, history, pagination, bus |
| `telegram-bot-forms-example/` | console bot demonstrating every feature |
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
