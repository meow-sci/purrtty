using NUnit.Framework;
using purrTTY.Kfs.Lsp.InGame;

namespace purrTTY.Kfs.Tools.Tests.Lsp;

/// <summary>
/// <see cref="MainThreadQueue"/> (no game): where work runs, its result, exception, timeout,
/// cancellation, shutdown, re-entrancy and back-pressure. Waits are on conditions, never sleeps.
/// </summary>
[TestFixture]
public sealed class MainThreadQueueTests
{
    private static readonly TimeSpan Long = TimeSpan.FromSeconds(30);

    [Test]
    public void Work_Runs_On_The_Pumping_Thread_Only()
    {
        var queue = new MainThreadQueue();
        int poster = Environment.CurrentManagedThreadId;
        Task<int> task = queue.Post(() => Environment.CurrentManagedThreadId, Long);
        Assert.That(task.IsCompleted, Is.False, "posting never runs the work");
        Assert.That(queue.Pending, Is.EqualTo(1));

        int pumper = -1;
        var thread = new Thread(() =>
        {
            pumper = Environment.CurrentManagedThreadId;
            queue.Pump();
        });
        thread.Start();
        Assert.That(thread.Join(Long), Is.True);
        Assert.That(task.Wait(Long), Is.True);
        Assert.That(task.Result, Is.EqualTo(pumper));
        Assert.That(task.Result, Is.Not.EqualTo(poster));
        Assert.That(queue.PumpThreadId, Is.EqualTo(pumper));
        Assert.That(queue.Pending, Is.Zero);
        Assert.That(queue.Pump(), Is.Zero, "an empty pump runs nothing");
    }

    [Test]
    public void Result_And_Exception_Reach_The_Caller_Not_The_Pump()
    {
        var queue = new MainThreadQueue();
        Task<string> ok = queue.Post(() => "built", Long);
        Task<string> bad = queue.Post<string>(() => throw new InvalidOperationException("the verb broke"), Long);
        Assert.DoesNotThrow(() => queue.Pump(), "an exception in work never reaches the pumping thread");
        Assert.That(ok.Result, Is.EqualTo("built"));
        var e = Assert.Throws<AggregateException>(() => bad.Wait(Long));
        Assert.That(e!.InnerException, Is.TypeOf<InvalidOperationException>().With.Message.EqualTo("the verb broke"));
    }

    [Test]
    public void Work_Not_Started_In_Time_Times_Out_And_Never_Runs()
    {
        var queue = new MainThreadQueue();
        int ran = 0;
        Task<int> task = queue.Post(() => ++ran, TimeSpan.FromMilliseconds(20));
        var e = Assert.Throws<AggregateException>(() => task.Wait(Long));
        Assert.That(e!.InnerException, Is.TypeOf<MainThreadTimeoutException>());
        Assert.That(e.InnerException!.Message, Does.Contain("did not run the command within").And.Contain("nothing was run"));
        Assert.That(queue.Pump(), Is.Zero, "the timed-out work is skipped");
        Assert.That(ran, Is.Zero);
    }

    [Test]
    public void Cancelled_Work_Never_Runs()
    {
        var queue = new MainThreadQueue();
        using var cts = new CancellationTokenSource();
        int ran = 0;
        Task<int> task = queue.Post(() => ++ran, Long, cts.Token);
        cts.Cancel();
        Assert.That(task.IsCanceled, Is.True);
        Assert.That(queue.Pump(), Is.Zero);
        Assert.That(ran, Is.Zero);

        Task<int> already = queue.Post(() => ++ran, Long, cts.Token);
        Assert.That(already.IsCanceled, Is.True, "a cancelled token cancels at once");
        queue.Pump();
        Assert.That(ran, Is.Zero);
    }

    [Test]
    public void Shutdown_Fails_Pending_And_Later_Work()
    {
        var queue = new MainThreadQueue();
        int ran = 0;
        Task<int> pending = queue.Post(() => ++ran, Long);
        queue.Shutdown("purrTTY is unloading");
        Assert.That(queue.IsClosed, Is.True);
        var e = Assert.Throws<AggregateException>(() => pending.Wait(Long));
        Assert.That(e!.InnerException, Is.TypeOf<MainThreadQueueClosedException>().With.Message.EqualTo("purrTTY is unloading"));

        Task<int> later = queue.Post(() => ++ran, Long);
        Assert.That(later.IsFaulted && later.Exception!.InnerException is MainThreadQueueClosedException, Is.True);
        Assert.That(queue.Pump(), Is.Zero);
        Assert.That(ran, Is.Zero);
        Assert.DoesNotThrow(() => queue.Shutdown("again"), "idempotent");
    }

    [Test]
    public void Work_That_Queues_Work_Runs_It_At_The_Next_Pump()
    {
        var queue = new MainThreadQueue(budget: TimeSpan.FromSeconds(30));   // the budget is not what this test is about
        var order = new List<string>();
        Task<int>? inner = null;
        int nested = -1;
        Task<int> outer = queue.Post(() =>
        {
            order.Add("outer");
            inner = queue.Post(() => { order.Add("inner"); return 2; }, Long);
            nested = queue.Pump();   // a pump inside work does nothing
            return 1;
        }, Long);
        Task<int> second = queue.Post(() => { order.Add("second"); return 3; }, Long);

        Assert.That(queue.Pump(), Is.EqualTo(2), "the pump runs what was queued when it began");
        Assert.That(outer.Result + second.Result, Is.EqualTo(4));
        Assert.That(nested, Is.Zero);
        Assert.That(inner!.IsCompleted, Is.False, "queued during the pump: waits for the next one");
        Assert.That(queue.Pump(), Is.EqualTo(1));
        Assert.That(inner.Result, Is.EqualTo(2));
        Assert.That(order, Is.EqualTo(new[] { "outer", "second", "inner" }));
    }

    [Test]
    public void Too_Many_Waiting_Calls_Are_Refused_At_Once()
    {
        var queue = new MainThreadQueue(maxPending: 2, budget: TimeSpan.FromSeconds(30));
        Task<int> a = queue.Post(() => 1, Long), b = queue.Post(() => 2, Long);
        Task<int> c = queue.Post(() => 3, Long);
        Assert.That(c.IsFaulted && c.Exception!.InnerException is MainThreadBusyException, Is.True);
        Assert.That(c.Exception!.InnerException!.Message, Does.StartWith("the game is busy: 2 commands are already waiting"));
        Assert.That(queue.Pump(), Is.EqualTo(2));
        Assert.That(a.Result + b.Result, Is.EqualTo(3));
        Assert.That(queue.Post(() => 4, Long).IsFaulted, Is.False, "room again after the pump");
    }
}
