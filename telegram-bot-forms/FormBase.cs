using System.Text.Json.Nodes;
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
  protected async Task<KeyboardButton[]> CreateBackButtonRowAsync(Chat chat, CancellationToken cancellationToken = default)
  {
    var language = await GetLanguageAsync(chat, cancellationToken).ConfigureAwait(false);

    return [new KeyboardButton(_localizer.Back(language))];
  }

  /// <summary>The language this chat should be answered in.</summary>
  protected Task<string> GetLanguageAsync(Chat chat, CancellationToken cancellationToken = default) =>
    Services.GetRequiredService<IChatLanguageResolver>().ResolveAsync(chat, cancellationToken);

  /// <summary>Resolves <typeparamref name="TForm"/> from DI in a new scope and renders it in place.</summary>
  protected Task<MessageContext> NavigateToAsync<TForm>(MessageContext context, CancellationToken cancellationToken) where TForm : FormBase =>
    NavigateToAsync(typeof(TForm), context, cancellationToken);

  /// <summary>Resolves <paramref name="formType"/> from DI in a new scope and renders it in place.</summary>
  protected async Task<MessageContext> NavigateToAsync(Type formType, MessageContext context, CancellationToken cancellationToken) =>
    await (await Services
      .GetRequiredService<IFormRetriever>()
      .GetFormAsync(context.Chat, formType))
      .RenderAsync(context, cancellationToken);

  /// <summary>
  /// Captures the serializable state of this form for history persistence
  /// (<see cref="Persistence.IChatHistoryStore"/>); null saves nothing beyond the
  /// position in the stack. <see cref="ExtendedFormBase{TState}"/> overrides this to
  /// carry the wizard state, and a form adds its own data through
  /// <see cref="CaptureContext"/>.
  /// </summary>
  protected internal virtual string? CaptureState() => null;

  /// <summary>
  /// Puts a snapshot written by <see cref="CaptureState"/> onto a fresh instance built
  /// from DI during history restore. Throwing skips this entry instead of failing the
  /// whole history.
  /// </summary>
  protected internal virtual void RestoreState(string? state)
  {
  }

  /// <summary>
  /// The form's own data to persist (collected wizard answers, a picked entity id) -
  /// serialized into the state envelope as <c>data</c>. Return an immutable snapshot
  /// object of simple serializable values; JSON is System.Text.Json.
  /// <para>
  /// <see cref="object"/> is the deliberate contract, not a shortcut: the value is a
  /// write-once pass-through - the framework immediately serializes its runtime type
  /// with System.Text.Json and never stores, reads back or dispatches on it as object,
  /// so the only promise it must keep is "serializable". A generic return type is not
  /// possible because the base class serializes the envelope through the non-generic
  /// <see cref="CaptureState"/> path, and returning <c>JsonNode</c> instead would drag
  /// System.Text.Json types into user code on the capture side (restore deliberately
  /// hands a <see cref="JsonNode"/> over, where the form itself decides the shape).
  /// </para>
  /// </summary>
  protected virtual object? CaptureContext() => null;

  /// <summary>
  /// Re-applies the <see cref="CaptureContext"/> data after a restart. Called only when
  /// a snapshot carries data; throwing skips the whole entry.
  /// </summary>
  protected virtual void RestoreContext(JsonNode context)
  {
  }

  public async ValueTask DisposeAsync()
  {
    GC.SuppressFinalize(this);
    await _scope.DisposeAsync();
  }
}
