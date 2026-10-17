using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Drenalol.Telegram.Bot.Forms;

/// <summary>
/// An unbounded pub/sub bus over a channel. Each message is handled by exactly one
/// active subscriber (competing consumers) and processed sequentially per subscriber;
/// a subscriber that throws logs and keeps receiving.
/// </summary>
public sealed class Bus<T>
{
  private readonly Channel<T> _channel = Channel.CreateUnbounded<T>();
  private readonly ILogger _logger;

  public Bus(ILogger logger) => _logger = logger;

  public async Task SendAsync(T message, CancellationToken cancellationToken = default) =>
    await _channel.Writer.WriteAsync(message, cancellationToken);

  public IDisposable Subscribe(Func<T, Task> action) => new Subscription(this, action);

  private sealed class Subscription : IDisposable
  {
    private readonly Bus<T> _bus;
    private readonly Func<T, Task> _action;
    private readonly CancellationTokenSource _cts = new();

    public Subscription(Bus<T> bus, Func<T, Task> action)
    {
      _bus = bus;
      _action = action;

      _ = Task.Run(() => ConsumeAsync(_cts.Token));
    }

    private async Task ConsumeAsync(CancellationToken cancellationToken)
    {
      try
      {
        while (await _bus._channel.Reader.WaitToReadAsync(cancellationToken))
        {
          while (_bus._channel.Reader.TryRead(out var message))
          {
            try
            {
              await _action(message);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
              return;
            }
            catch (Exception exception)
            {
              _bus._logger.LogError(exception, "Bus subscriber failed to handle a message");
            }
          }
        }
      }
      catch (OperationCanceledException)
      {
        /* the subscription was disposed */
      }
    }

    public void Dispose()
    {
      _cts.Cancel();
      _cts.Dispose();
    }
  }
}
