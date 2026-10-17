using Microsoft.Extensions.DependencyInjection;

namespace Drenalol.Telegram.Bot.Forms;

/// <summary>
/// Adapts an <see cref="IBotCommand"/> to the form pipeline. The DI scope is owned here
/// and released when the command finishes, so command use does not accumulate scopes in
/// the form history.
/// </summary>
internal sealed class CommandFormAdapter : IForm
{
  private readonly IBotCommand _command;
  private readonly IServiceScope _scope;

  public CommandFormAdapter(IBotCommand command, IServiceScope scope)
  {
    _command = command;
    _scope = scope;
  }

  public bool ClearHistoryOnNavigate => false;

  public int TimeoutSec => 30;

  public async Task<MessageContext> RenderAsync(MessageContext context, CancellationToken cancellationToken)
  {
    try
    {
      return await _command.ExecuteAsync(context, cancellationToken);
    }
    finally
    {
      _scope.Dispose();
    }
  }
}
