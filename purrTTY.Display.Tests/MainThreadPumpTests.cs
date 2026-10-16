using NUnit.Framework;
using purrTTY.GameMod.Launcher;

namespace purrTTY.Display.Tests;

/// <summary>
/// Pins <see cref="MainThreadPump"/> (compiled into this assembly from the GameMod source — it has
/// no game dependency): FIFO draining, the "snapshot at entry" rule that stops a self-posting item
/// from looping, fault isolation, inline execution while pumping, cancellation, and shutdown.
/// Cross-thread tests wait on task completion with a deadline — never a fixed sleep.
/// </summary>
[TestFixture]
public sealed class MainThreadPumpTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    // The producer enqueues from a pool thread; wait (condition + deadline, never a fixed sleep)
    // until its item is visible in the queue.
    private static void WaitUntilQueued(MainThreadPump pump)
        => Assert.That(SpinWait.SpinUntil(() => pump.PendingCount >= 1, Deadline), Is.True, "the producer never queued its work");

    [Test]
    public void Drain_RunsPostedActionsInOrder()
    {
        var pump = new MainThreadPump();
        var ran = new List<int>();
        pump.Post(() => ran.Add(1));
        pump.Post(() => ran.Add(2));
        pump.Post(() => ran.Add(3));

        pump.Drain();

        Assert.That(ran, Is.EqualTo(new[] { 1, 2, 3 }));
        Assert.That(pump.PendingCount, Is.EqualTo(0));
    }

    [Test]
    public void Drain_OnAnEmptyQueue_DoesNothing()
    {
        var pump = new MainThreadPump();

        Assert.DoesNotThrow(pump.Drain);
        Assert.That(pump.IsPumping, Is.False);
    }

    [Test]
    public void Drain_RunsOnlyWhatWasQueuedAtEntry_ItemsPostedMeanwhileWaitForTheNextDrain()
    {
        var pump = new MainThreadPump();
        int runs = 0;
        void SelfPosting()
        {
            runs++;
            pump.Post(SelfPosting); // would loop forever if the drain re-read the queue
        }

        pump.Post(SelfPosting);

        pump.Drain();
        Assert.That(runs, Is.EqualTo(1));
        Assert.That(pump.PendingCount, Is.EqualTo(1));

        pump.Drain();
        Assert.That(runs, Is.EqualTo(2));
    }

    [Test]
    public void Drain_AThrowingItem_DoesNotStopTheRest()
    {
        var pump = new MainThreadPump();
        int after = 0;
        pump.Post(() => throw new InvalidOperationException("boom"));
        pump.Post(() => after++);

        Assert.DoesNotThrow(pump.Drain);

        Assert.That(after, Is.EqualTo(1));
    }

    [Test]
    public void IsPumping_IsTrueOnlyInsideDrain()
    {
        var pump = new MainThreadPump();
        bool during = false;
        pump.Post(() => during = pump.IsPumping);

        Assert.That(pump.IsPumping, Is.False);
        pump.Drain();

        Assert.That(during, Is.True);
        Assert.That(pump.IsPumping, Is.False);
    }

    [Test]
    public void EnterPumpScope_MarksPumping_AndRestoresThePreviousValue()
    {
        var pump = new MainThreadPump();

        using (pump.EnterPumpScope())
        {
            Assert.That(pump.IsPumping, Is.True);
            using (pump.EnterPumpScope())
            {
                Assert.That(pump.IsPumping, Is.True);
            }

            Assert.That(pump.IsPumping, Is.True, "the inner scope must restore the outer scope's value");
        }

        Assert.That(pump.IsPumping, Is.False);
    }

    [Test]
    public void RunAsync_FromAnotherThread_CompletesOnlyAfterTheMainThreadDrains()
    {
        var pump = new MainThreadPump();
        int mainThreadId = Environment.CurrentManagedThreadId;
        int ranOn = -1;

        Task<int> task = Task.Run(() => pump.RunAsync(() =>
        {
            ranOn = Environment.CurrentManagedThreadId;
            return 42;
        }));

        WaitUntilQueued(pump);
        Assert.That(task.IsCompleted, Is.False, "nothing may run before the main thread drains");

        pump.Drain();

        Assert.That(task.Wait(Deadline), Is.True);
        Assert.That(task.Result, Is.EqualTo(42));
        Assert.That(ranOn, Is.EqualTo(mainThreadId));
    }

    [Test]
    public void RunAsync_ContinuationsDoNotRunInsideTheDrain()
    {
        var pump = new MainThreadPump();
        int mainThreadId = Environment.CurrentManagedThreadId;
        var continuationThread = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Queue from a pool thread, attach a synchronous continuation, then drain on this thread.
        Task<int> task = Task.Run(() => pump.RunAsync(() => 1));
        WaitUntilQueued(pump);
        _ = task.ContinueWith(
            _ => continuationThread.TrySetResult(Environment.CurrentManagedThreadId),
            TaskContinuationOptions.ExecuteSynchronously);

        pump.Drain();

        Assert.That(continuationThread.Task.Wait(Deadline), Is.True);
        Assert.That(continuationThread.Task.Result, Is.Not.EqualTo(mainThreadId),
            "a consumer continuation must never run on (and block) the main thread");
    }

    [Test]
    public void RunAsync_FaultFromTheWork_FaultsTheTask()
    {
        var pump = new MainThreadPump();
        Task<int> task = Task.Run(() => pump.RunAsync<int>(() => throw new InvalidOperationException("nope")));
        WaitUntilQueued(pump);

        pump.Drain();

        var ex = Assert.Throws<AggregateException>(() => task.Wait(Deadline));
        Assert.That(ex!.InnerException, Is.TypeOf<InvalidOperationException>());
    }

    [Test]
    public void RunAsync_WhilePumping_RunsInline()
    {
        var pump = new MainThreadPump();
        Task<int>? inner = null;
        bool ranInline = false;
        pump.Post(() =>
        {
            inner = pump.RunAsync(() =>
            {
                ranInline = true;
                return 7;
            });

            // Inline: already complete by the time RunAsync returns, with no further drain.
            Assert.That(inner.IsCompletedSuccessfully, Is.True);
        });

        pump.Drain();

        Assert.That(ranInline, Is.True);
        Assert.That(inner!.Result, Is.EqualTo(7));
        Assert.That(pump.PendingCount, Is.EqualTo(0));
    }

    [Test]
    public void RunAsync_InlineFault_FaultsTheTaskInsteadOfThrowing()
    {
        var pump = new MainThreadPump();
        Task<int>? inner = null;
        pump.Post(() => inner = pump.RunAsync<int>(() => throw new InvalidOperationException("inline")));

        pump.Drain();

        Assert.That(inner!.IsFaulted, Is.True);
    }

    [Test]
    public void RunAsync_NonGenericOverload_RunsTheAction()
    {
        var pump = new MainThreadPump();
        int ran = 0;
        Task task = Task.Run(() => pump.RunAsync(() => ran++));
        WaitUntilQueued(pump);

        pump.Drain();

        Assert.That(task.Wait(Deadline), Is.True);
        Assert.That(ran, Is.EqualTo(1));
    }

    [Test]
    public void RunAsync_CancelledBeforeEnqueue_ReturnsACancelledTask_WithoutRunning()
    {
        var pump = new MainThreadPump();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        bool ran = false;

        Task<int> task = pump.RunAsync(() =>
        {
            ran = true;
            return 1;
        }, cts.Token);
        pump.Drain();

        Assert.That(task.IsCanceled, Is.True);
        Assert.That(ran, Is.False);
    }

    [Test]
    public void RunAsync_CancelledWhileQueued_IsCancelledAtDrain_WithoutRunning()
    {
        var pump = new MainThreadPump();
        using var cts = new CancellationTokenSource();
        bool ran = false;
        Task<int> task = Task.Run(() => pump.RunAsync(() =>
        {
            ran = true;
            return 1;
        }, cts.Token));
        WaitUntilQueued(pump);

        cts.Cancel();
        pump.Drain();

        Assert.That(((IAsyncResult)task).AsyncWaitHandle.WaitOne(Deadline), Is.True);
        Assert.That(task.IsCanceled, Is.True);
        Assert.That(ran, Is.False);
    }

    [Test]
    public void Shutdown_CancelsQueuedRunAsyncTasks_AndDropsPosts()
    {
        var pump = new MainThreadPump();
        bool postRan = false;
        Task<int> task = Task.Run(() => pump.RunAsync(() => 1));
        WaitUntilQueued(pump);
        pump.Post(() => postRan = true);

        pump.Shutdown();
        pump.Drain();

        Assert.That(((IAsyncResult)task).AsyncWaitHandle.WaitOne(Deadline), Is.True);
        Assert.That(task.IsCanceled, Is.True, "no awaiter may hang on a pump that will never drain again");
        Assert.That(postRan, Is.False);
        Assert.That(pump.PendingCount, Is.EqualTo(0));
    }

    [Test]
    public void AfterShutdown_PostIsIgnored_AndRunAsyncIsCancelled()
    {
        var pump = new MainThreadPump();
        pump.Shutdown();
        bool postRan = false;

        pump.Post(() => postRan = true);
        Task<int> task = pump.RunAsync(() => 1);
        pump.Drain();

        Assert.That(postRan, Is.False);
        Assert.That(task.IsCanceled, Is.True);
    }
}
