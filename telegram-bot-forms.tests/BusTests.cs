using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Drenalol.Telegram.Bot.Forms.Tests;

[TestFixture]
public class BusTests
{
  [Test]
  public async Task Subscriber_Receives_Sent_Messages()
  {
    var bus = new Bus<string>(NullLogger.Instance);
    var received = new TaskCompletionSource<string>();

    using (bus.Subscribe(message =>
           {
             received.TrySetResult(message);

             return Task.CompletedTask;
           }))
    {
      await bus.SendAsync("hello");

      using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
      Assert.That(await received.Task.WaitAsync(timeout.Token), Is.EqualTo("hello"));
    }
  }

  [Test]
  public async Task Throwing_Subscriber_Logs_And_Keeps_Receiving()
  {
    var bus = new Bus<int>(NullLogger.Instance);
    var seen = new List<int>();
    var secondArrived = new TaskCompletionSource();

    using (bus.Subscribe(async message =>
           {
             if (message == 1)
               throw new InvalidOperationException("subscriber failure");

             lock (seen) seen.Add(message);
             if (message == 2)
               secondArrived.TrySetResult();

             await Task.CompletedTask;
           }))
    {
      await bus.SendAsync(1);
      await bus.SendAsync(2);

      using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
      await secondArrived.Task.WaitAsync(timeout.Token);

      Assert.That(seen, Is.EqualTo(new[] { 2 }));
    }
  }

  [Test]
  public async Task Disposed_Subscription_Stops_Consuming()
  {
    var bus = new Bus<string>(NullLogger.Instance);
    var seen = new List<string>();

    var subscription = bus.Subscribe(message =>
    {
      lock (seen) seen.Add(message);

      return Task.CompletedTask;
    });

    subscription.Dispose();
    await bus.SendAsync("after dispose");

    await Task.Delay(100);

    Assert.That(seen, Is.Empty);
  }
}
