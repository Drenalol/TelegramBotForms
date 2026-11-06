using System.Collections.Concurrent;
using Drenalol.Telegram.Bot.Forms.Localization;
using Drenalol.Telegram.Bot.Forms.Persistence;
using Telegram.Bot.Types;

namespace Drenalol.Telegram.Bot.Forms;

/// <summary>
/// Decorates every form resolution with the cross-cutting pipeline behaviour: the
/// history bookkeeping (clear-on-navigate, push after a successful render, persist when
/// a history store is configured) and the exception-to-friendly-reply guard.
/// </summary>
internal sealed class FormWrapper : FormBase
{
  private readonly IForm _wrapped;
  private readonly Chat _chat;
  private readonly ConcurrentStack<IForm> _history;
  private readonly bool _skipPush;
  private readonly ITelegramTextLocalizer _localizer;
  private readonly ChatHistoryCoordinator? _coordinator;

  public FormWrapper(
    IForm wrapped,
    Chat chat,
    ConcurrentStack<IForm> history,
    ITelegramTextLocalizer localizer,
    bool skipPush = false,
    ChatHistoryCoordinator? coordinator = null
  )
  {
    _wrapped = wrapped;
    _chat = chat;
    _history = history;
    _localizer = localizer;
    _skipPush = skipPush;
    _coordinator = coordinator;
  }

  public override async Task<MessageContext> RenderAsync(MessageContext context, CancellationToken cancellationToken)
  {
    if (_wrapped.ClearHistoryOnNavigate && !_history.IsEmpty)
    {
      foreach (var form in _history)
        // the wrapped form can itself sit in the history (re-entered via "Back"): it is
        // the navigation destination and must never be disposed by its own clear
        if (form is IAsyncDisposable disposable && !ReferenceEquals(form, _wrapped))
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

    // the turn settled the history (pushed, popped by "Back" or rewound): write the
    // new position through so a restart resumes exactly here
    if (_coordinator != null)
      await _coordinator.PersistAsync(_chat.Id, _history, cancellationToken);

    return resultContext;
  }
}
