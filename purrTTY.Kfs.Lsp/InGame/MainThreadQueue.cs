// RULE FOR THIS FILE: no game type.

using System.Diagnostics;

namespace purrTTY.Kfs.Lsp.InGame;

/// <summary>The main thread did not run a queued call in time (a loading screen, a stalled UI, shutdown).</summary>
public sealed class MainThreadTimeoutException(string message) : TimeoutException(message);

/// <summary>The queue was shut down (the mod is unloading or the service stopped) before the call ran.</summary>
public sealed class MainThreadQueueClosedException(string message) : InvalidOperationException(message);

/// <summary>Too many calls are already waiting for the main thread; nothing was queued.</summary>
public sealed class MainThreadBusyException(string message) : InvalidOperationException(message);

/// <summary>
/// Calls marshalled to the game's main thread (plan §6.7: <c>RunVerb</c> runs in the UI phase).
/// Any thread may <see cref="Post{T}"/>; the main thread calls <see cref="Pump"/> once per frame.
/// <list type="bullet">
/// <item><b>Where.</b> Work runs only inside <see cref="Pump"/>, on the pumping thread.</item>
/// <item><b>A frame's budget.</b> A pump runs at least one call and then more only while it has been
/// running for less than its budget (default 4 ms): a burst of slow commands (<c>kfs build</c>) costs
/// one of them per frame, never every one of them in one frame.</item>
/// <item><b>Result.</b> The task completes with the work's value, or faults with the exception it
/// threw — which never reaches the pumping thread.</item>
/// <item><b>Bounded wait.</b> Work that has not <i>started</i> within its timeout fails with
/// <see cref="MainThreadTimeoutException"/> and never runs afterwards; work that has started runs to
/// its end (it cannot be interrupted).</item>
/// <item><b>Cancellation</b> before it starts cancels the caller's task; the work never runs once
/// every caller sharing it has cancelled.</item>
/// <item><b>Coalescing.</b> A post with a <c>coalesceKey</c> equal to that of the last call still
/// waiting joins it: one run, every caller gets its result. Only for work whose second run straight
/// after the first changes nothing (the caller decides; see <see cref="InGameLanguageHost"/>).</item>
/// <item><b>Shutdown</b> (<see cref="Shutdown"/>) fails every pending call with
/// <see cref="MainThreadQueueClosedException"/>; later posts fail the same way at once.</item>
/// <item><b>Re-entrancy.</b> Work posted while a pump runs (by the work itself or another thread)
/// runs at a later pump, in order: a pump runs only what was queued when it began, so it is
/// bounded. A <see cref="Pump"/> called from inside work does nothing.</item>
/// <item><b>Back-pressure.</b> At most <c>maxPending</c> calls wait; further posts fail at once with
/// <see cref="MainThreadBusyException"/>.</item>
/// </list>
/// An empty <see cref="Pump"/> costs one volatile read and allocates nothing.
/// </summary>
public sealed class MainThreadQueue
{
    private const int Pending_ = 0, Running = 1, Done = 2;

    /// <summary>The default most calls waiting.</summary>
    public const int DefaultMaxPending = 8;

    /// <summary>The default budget of one pump.</summary>
    public static readonly TimeSpan DefaultBudget = TimeSpan.FromMilliseconds(4);

    private readonly LinkedList<Item> _queue = new();
    private readonly int _maxPending;
    private readonly TimeSpan _budget;
    private readonly Func<TimeSpan> _clock;
    private readonly object _gate = new();
    private int _pending;
    private bool _closed;
    private string _closedReason = "";
    private int _pumpThread = -1;
    private bool _pumping;

    /// <summary>A queue holding at most <paramref name="maxPending"/> waiting calls, running calls for at most <paramref name="budget"/> per pump (at least one).</summary>
    /// <param name="maxPending">The most calls waiting (default 8).</param>
    /// <param name="budget">How long one pump keeps starting calls (default 4 ms; zero: one call per pump).</param>
    /// <param name="clock">Elapsed time for the budget (tests); default a monotonic clock.</param>
    public MainThreadQueue(int maxPending = DefaultMaxPending, TimeSpan? budget = null, Func<TimeSpan>? clock = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxPending, 1);
        _maxPending = maxPending;
        _budget = budget ?? DefaultBudget;
        if (_budget < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(budget));
        _clock = clock ?? StopwatchClock();
    }

    private static Func<TimeSpan> StopwatchClock()
    {
        long start = Stopwatch.GetTimestamp();
        return () => Stopwatch.GetElapsedTime(start);
    }

    /// <summary>Calls waiting to run.</summary>
    public int Pending => Volatile.Read(ref _pending);

    /// <summary>The most calls that may wait.</summary>
    public int MaxPending => _maxPending;

    /// <summary>The managed thread id of the last pump, or -1 before the first.</summary>
    public int PumpThreadId => Volatile.Read(ref _pumpThread);

    /// <summary>True after <see cref="Shutdown"/>.</summary>
    public bool IsClosed
    {
        get { lock (_gate) return _closed; }
    }

    /// <summary>Queues <paramref name="work"/> for the main thread; the task completes when it has run, failed, timed out, been cancelled or been shut down.</summary>
    /// <param name="work">What to run on the main thread.</param>
    /// <param name="timeout">How long it may wait to start.</param>
    /// <param name="cancellationToken">Cancels this caller's wait (and the work, while no other caller shares it).</param>
    /// <param name="coalesceKey">Joins the last waiting call with the same key instead of queuing another (null: never).</param>
    public Task<T> Post<T>(Func<T> work, TimeSpan timeout, CancellationToken cancellationToken = default, string? coalesceKey = null)
    {
        ArgumentNullException.ThrowIfNull(work);
        var waiter = new Waiter<T>();
        lock (_gate)
        {
            if (_closed)
            {
                waiter.Fail(new MainThreadQueueClosedException(_closedReason));
                return waiter.Task;
            }
            if (coalesceKey is not null && _queue.Last?.Value is Item<T> last && last.Key == coalesceKey && last.TryJoin(waiter, cancellationToken))
                return waiter.Task;
            if (Volatile.Read(ref _pending) >= _maxPending)
            {
                waiter.Fail(new MainThreadBusyException($"the game is busy: {_maxPending} commands are already waiting for its main thread; nothing was run, try again"));
                return waiter.Task;
            }
            var item = new Item<T>(work, coalesceKey);
            // Armed before the pump can see it, so the timer and the registration are never raced.
            item.Arm(timeout);
            if (!item.TryJoin(waiter, cancellationToken)) return waiter.Task;   // already cancelled
            Interlocked.Increment(ref _pending);
            _queue.AddLast(item);
        }
        return waiter.Task;
    }

    /// <summary>
    /// Runs calls queued before this pump began, on the calling thread (the game's main thread, UI
    /// phase): at least one, then more while within the budget. Returns how many ran. Never throws.
    /// </summary>
    public int Pump()
    {
        if (Volatile.Read(ref _pending) == 0 || _pumping) return 0;
        _pumping = true;
        Volatile.Write(ref _pumpThread, Environment.CurrentManagedThreadId);
        int ran = 0;
        try
        {
            TimeSpan start = _clock();
            int count;
            lock (_gate) count = _queue.Count;
            for (int i = 0; i < count; i++)
            {
                if (ran > 0 && _clock() - start >= _budget) break;
                Item? item;
                lock (_gate)
                {
                    if (_queue.First is not { } first) break;
                    item = first.Value;
                    _queue.RemoveFirst();
                    Interlocked.Decrement(ref _pending);
                }
                if (item.TryStart()) { item.Run(); ran++; }
            }
        }
        finally
        {
            _pumping = false;
        }
        return ran;
    }

    /// <summary>Fails every pending call and refuses further posts (unload, service stop). Idempotent.</summary>
    public void Shutdown(string reason)
    {
        List<Item> pending;
        lock (_gate)
        {
            if (!_closed)
            {
                _closed = true;
                _closedReason = string.IsNullOrWhiteSpace(reason) ? "the main-thread queue was shut down" : reason;
            }
            pending = [.. _queue];
            _queue.Clear();
            Volatile.Write(ref _pending, 0);
        }
        foreach (Item item in pending) item.TryFail(new MainThreadQueueClosedException(_closedReason));
    }

    // ------------------------------------------------------------------ items

    private abstract class Item
    {
        private int _state = Pending_;
        private Timer? _timer;

        protected bool IsPending => Volatile.Read(ref _state) == Pending_;

        public bool TryStart()
        {
            if (Interlocked.CompareExchange(ref _state, Running, Pending_) != Pending_) return false;
            Disarm();
            return true;
        }

        public void TryFail(Exception e)
        {
            if (Interlocked.CompareExchange(ref _state, Done, Pending_) != Pending_) return;
            Disarm();
            FailAll(e);
        }

        /// <summary>Every caller gave up (cancelled): the work never runs.</summary>
        protected void Abandon()
        {
            if (Interlocked.CompareExchange(ref _state, Done, Pending_) == Pending_) Disarm();
        }

        public void Arm(TimeSpan timeout)
        {
            if (timeout == Timeout.InfiniteTimeSpan || timeout < TimeSpan.Zero) return;
            string seconds = timeout.TotalSeconds.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
            _timer = new Timer(_ =>
            {
                // A timer callback: never throws.
                try
                {
                    TryFail(new MainThreadTimeoutException(
                        $"the game's main thread did not run the command within {seconds} s (a loading screen, or the game is shutting down); nothing was run"));
                }
                catch { /* completing a task never throws here; never let it reach the timer thread */ }
            }, null, timeout, Timeout.InfiniteTimeSpan);
        }

        protected void Finish() => Volatile.Write(ref _state, Done);

        private void Disarm() => Interlocked.Exchange(ref _timer, null)?.Dispose();

        public abstract void Run();

        protected abstract void FailAll(Exception e);
    }

    private sealed class Waiter<T>
    {
        private readonly TaskCompletionSource<T> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<T> Task => _completion.Task;

        public CancellationTokenRegistration Registration { get; set; }

        public void Fail(Exception e) => _completion.TrySetException(e);

        public void Set(T value) => _completion.TrySetResult(value);

        public bool Cancel(CancellationToken token) => _completion.TrySetCanceled(token);
    }

    private sealed class Item<T>(Func<T> work, string? key) : Item
    {
        private readonly List<Waiter<T>> _waiters = [];

        public string? Key { get; } = key;

        /// <summary>Adds a caller while the work has not started; false when it could not (started, done, or the token is already cancelled).</summary>
        public bool TryJoin(Waiter<T> waiter, CancellationToken token)
        {
            lock (_waiters)
            {
                if (!IsPending) return false;
                if (token.IsCancellationRequested)
                {
                    waiter.Cancel(token);
                    return _waiters.Count > 0 || Abandoned();
                }
                _waiters.Add(waiter);
            }
            if (token.CanBeCanceled) waiter.Registration = token.Register(() => Leave(waiter, token));
            return true;
        }

        private bool Abandoned()
        {
            Abandon();
            return false;
        }

        private void Leave(Waiter<T> waiter, CancellationToken token)
        {
            bool last;
            lock (_waiters)
            {
                if (!IsPending || !_waiters.Remove(waiter)) return;
                last = _waiters.Count == 0;
            }
            waiter.Cancel(token);
            if (last) Abandon();
        }

        public override void Run()
        {
            Waiter<T>[] waiters;
            lock (_waiters) waiters = [.. _waiters];
            try
            {
                T value = work();
                foreach (Waiter<T> w in waiters) w.Set(value);
            }
            catch (Exception e)
            {
                foreach (Waiter<T> w in waiters) w.Fail(e);
            }
            finally
            {
                Finish();
                foreach (Waiter<T> w in waiters) w.Registration.Dispose();
            }
        }

        protected override void FailAll(Exception e)
        {
            Waiter<T>[] waiters;
            lock (_waiters) waiters = [.. _waiters];
            foreach (Waiter<T> w in waiters)
            {
                w.Registration.Dispose();
                w.Fail(e);
            }
        }
    }
}
