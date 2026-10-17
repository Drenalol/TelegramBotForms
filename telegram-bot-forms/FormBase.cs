using Drenalol.Telegram.Bot.Forms.Localization;
using Microsoft.Extensions.DependencyInjection;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace Drenalol.Telegram.Bot.Forms;

/// <summary>
/// Base class for all bot forms. Every conversation step resolves the form from DI in
/// its own scope: constructor dependencies come from a fresh service scope that lives
/// as long as the form stays in the chat's navigation history (stateful forms) and is
/// disposed when the form leaves it.
/// </summary>
public abstract class FormBase : IForm, IAsyncDisposable
{
  private AsyncServiceScope _scope;
  private ITelegramTextLocalizer _localizer = null!;

  /// <summary>The service scope owned by this form instance.</summary>
  protected IServiceProvider Services => _scope.ServiceProvider;

  /// <summary>The framework text localizer.</summary>
  protected ITelegramTextLocalizer Localizer => _localizer;

  public virtual bool ClearHistoryOnNavigate => false;

  /// <summary>When true, rendering this form does not push it onto the chat's history.</summary>
  public virtual bool IgnoreHistory => false;

  public abstract Task<MessageContext> RenderAsync(MessageContext context, CancellationToken cancellationToken);

  internal void SetData(IServiceScope scope, ITelegramTextLocalizer localizer)
  {
    _scope = scope is AsyncServiceScope asyncServiceScope ? asyncServiceScope : new AsyncServiceScope(scope);
    _localizer = localizer;
  }

  /// <summary>A one-button "Back" row in the default language.</summary>
  protected KeyboardButton[] CreateBackButtonRow() => [new KeyboardButton(_localizer.Back())];

  /// <summary>A one-button "Back" row with explicit (localized) text.</summary>
  protected KeyboardButton[] CreateBackButtonRow(string text) => [new KeyboardButton(text)];

  /// <summary>A one-button "Back" row in the chat's language.</summary>
  protected async Task<KeyboardButton[]> CreateBackButtonRowAsync(Chat chat, CancellationToken cancellationToken = default) =>
    [new KeyboardButton(await GetLanguageAsync(chat, cancellationToken).ConfigureAwait(false))];

  /// <summary>The language this chat should be answered in.</summary>
  protected Task<string> GetLanguageAsync(Chat chat, CancellationToken cancellationToken = default) =>
    Services.GetRequiredService<IChatLanguageResolver>().ResolveAsync(chat, cancellationToken);

  /// <summary>Resolves <typeparamref name="TForm"/> from DI in a new scope and renders it in place.</summary>
  protected Task<MessageContext> NavigateToAsync<TForm>(MessageContext context, CancellationToken cancellationToken) where TForm : FormBase =>
    NavigateToAsync(typeof(TForm), context, cancellationToken);

  /// <summary>Resolves <paramref name="formType"/> from DI in a new scope and renders it in place.</summary>
  protected Task<MessageContext> NavigateToAsync(Type formType, MessageContext context, CancellationToken cancellationToken) =>
    Services
      .GetRequiredService<IFormRetriever>()
      .Get(context.Chat, formType)
      .RenderAsync(context, cancellationToken);

  public async ValueTask DisposeAsync()
  {
    GC.SuppressFinalize(this);
    await _scope.DisposeAsync();
  }
}
