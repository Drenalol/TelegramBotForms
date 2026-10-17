using System.Collections.Concurrent;
using Drenalol.Telegram.Bot.Forms.Localization;

namespace Drenalol.Telegram.Bot.Forms;

/// <summary>
/// Decorates every form resolution with the cross-cutting pipeline behaviour: the
/// history bookkeeping (clear-on-navigate, push after a successful render) and the
/// exception-to-friendly-reply guard.
/// </summary>
internal sealed class FormWrapper : FormBase
{
  private readonly IForm _wrapped;
  private readonly ConcurrentStack<IForm> _history;
  private readonly bool _skipPush;
  private readonly ITelegramTextLocalizer _localizer;

  public FormWrapper(IForm wrapped, ConcurrentStack<IForm> history, ITelegramTextLocalizer localizer, bool skipPush = false)
  {
    _wrapped = wrapped;
    _history = history;
    _localizer = localizer;
    _skipPush = skipPush;
  }

  public override async Task<MessageContext> RenderAsync(MessageContext context, CancellationToken cancellationToken)
  {
    if (_wrapped.ClearHistoryOnNavigate && !_history.IsEmpty)
    {
      foreach (var form in _history)
        if (form is IAsyncDisposable disposable)
          await disposable.DisposeAsync();

      _history.Clear();
    }

    MessageContext resultContext;

    try
    {
      resultContext = await _wrapped.RenderAsync(context, cancellationToken);
    }
    catch (Exception exception)
    {
      return context.Reply(_localizer.UnhandledError(), exception: exception);
    }

    if (!_skipPush)
      _history.Push(_wrapped);

    return resultContext;
  }
}
