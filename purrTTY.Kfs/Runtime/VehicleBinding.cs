using KSA.Kfs.Host;

namespace purrTTY.Kfs.Runtime;

/// <summary>Kinds of event the extension writes to a program's log on its own, each rate-limited separately.</summary>
public enum BindingEvent
{
    /// <summary>An <c>x_parts_describe</c> table.</summary>
    Describe = 0,
    /// <summary>The <c>parts rescanned: …</c> line.</summary>
    Rescanned = 1,
    /// <summary>A command whose part left between accept and apply.</summary>
    Discarded = 2,
    /// <summary>A command the back end failed to apply.</summary>
    ApplyFailure = 3,
}

/// <summary>
/// What purrTTY keeps per <see cref="VehicleComputer"/> (plan §4.7): the parts back end, the queue
/// of accepted commands between the dispatch thread and the pump, the run the lists were frozen
/// for, a per-kind rate limiter for the events the extension itself writes, and counters. Created
/// only by <see cref="PartsRegistry.Bind"/>.
/// </summary>
/// <remarks>
/// <para><b>Runs.</b> Every run of a program starts from fresh lists and fresh rate limits
/// (<see cref="SyncRun"/>). A run is identified by the computer's <see cref="VehicleComputer.Grid"/>:
/// <c>VehicleComputer.TryStart</c> — behind start, <c>kfs reset</c> and stop → start alike —
/// assigns a new <see cref="TickGrid"/>; <c>Load</c>, <c>Stop</c> and <c>Fault</c> set it to
/// null; and ticks, hence every service call, happen only while it is set. The router (dispatch
/// thread) and the pump (main thread, solver idle) both call <see cref="SyncRun"/> first, so a new
/// run is noticed at its first service call or the next frame, whichever comes first.</para>
/// <para>
/// The queue has the lock-swap-drain shape: <see cref="Enqueue"/> (dispatch) adds under a lock;
/// <see cref="Take"/> (pump) swaps the pending list for the spare one under the same lock and
/// returns it; <see cref="Recycle"/> clears it and keeps it as the next spare. No allocation once
/// both lists have grown to the working size.</para>
/// </remarks>
public sealed class VehicleBinding
{
    /// <summary>Most commands waiting for the pump; above it the oldest are dropped.</summary>
    public const int QueueLimit = 4096;

    private static readonly List<PartCommand> NothingTaken = new(0);

    private readonly object _gate = new();
    private List<PartCommand> _pending = new(32);
    private List<PartCommand>? _spare = new(32);
    private bool _overflowReported;
    private readonly long[] _lastReportSecond = [-1, -1, -1, -1];
    private readonly object _runGate = new();
    private TickGrid? _run;
    private volatile bool _bound = true;

    private long _runs, _accepted, _rejected, _answered, _applied, _applyFailures, _discarded, _dropped, _faults;

    internal VehicleBinding(VehicleComputer computer, IVehicleParts parts)
    {
        Computer = computer;
        Parts = parts;
    }

    /// <summary>The computer this binding serves.</summary>
    public VehicleComputer Computer { get; }

    /// <summary>The parts back end.</summary>
    public IVehicleParts Parts { get; }

    /// <summary>False once <see cref="PartsRegistry.Unbind"/> removed the binding.</summary>
    public bool IsBound => _bound;

    /// <summary>Runs started since the binding was created (each rebuilt the lists).</summary>
    public long Runs => Interlocked.Read(ref _runs);

    /// <summary>Requests accepted: queued (setters, rescans) or served at once (a describe table).</summary>
    public long Accepted => Interlocked.Read(ref _accepted);

    /// <summary>Requests the router rejected (host checks; issue-time domain rejections never reach the router).</summary>
    public long Rejected => Interlocked.Read(ref _rejected);

    /// <summary>Result-bearing calls answered.</summary>
    public long Answered => Interlocked.Read(ref _answered);

    /// <summary>Commands applied by the pump (a rescan counts as one).</summary>
    public long Applied => Interlocked.Read(ref _applied);

    /// <summary>Commands whose application threw.</summary>
    public long ApplyFailures => Interlocked.Read(ref _applyFailures);

    /// <summary>Commands discarded because their part left before they were applied.</summary>
    public long Discarded => Interlocked.Read(ref _discarded);

    /// <summary>Commands dropped because the queue was full.</summary>
    public long Dropped => Interlocked.Read(ref _dropped);

    /// <summary>Exceptions caught inside the router for this computer (the call then answered 0 or did nothing).</summary>
    public long RouterFaults => Interlocked.Read(ref _faults);

    /// <summary>Commands waiting for the pump.</summary>
    public int PendingCount
    {
        get { lock (_gate) return _pending.Count; }
    }

    /// <summary>
    /// Queues an accepted command (dispatch thread). Above <see cref="QueueLimit"/> the oldest
    /// command is dropped; the first drop since the last <see cref="Take"/> writes one event.
    /// </summary>
    public void Enqueue(in PartCommand command)
    {
        bool report = false;
        lock (_gate)
        {
            if (_pending.Count >= QueueLimit)
            {
                _pending.RemoveAt(0);
                _dropped++;
                report = !_overflowReported;
                _overflowReported = true;
            }
            _pending.Add(command);
        }
        Interlocked.Increment(ref _accepted);
        if (!report) return;
        // Enqueue runs on the dispatch thread, where the computer's log may be written directly.
        // The command is queued already: a failure to write the event must not look like a
        // failure to accept it.
        try { Computer.Event(PartsText.QueueFull(QueueLimit)); }
        catch (Exception e) { KfsLog.Once("queue full event", e); }
    }

    /// <summary>Takes every queued command (pump). Give the list back with <see cref="Recycle"/>.</summary>
    public List<PartCommand> Take()
    {
        lock (_gate)
        {
            if (_pending.Count == 0) return NothingTaken;
            List<PartCommand> taken = _pending;
            _pending = _spare ?? new List<PartCommand>(taken.Capacity);
            _spare = null;
            _overflowReported = false;
            return taken;
        }
    }

    /// <summary>Returns a list from <see cref="Take"/> for reuse.</summary>
    public void Recycle(List<PartCommand> list)
    {
        if (ReferenceEquals(list, NothingTaken)) return;
        list.Clear();
        lock (_gate)
        {
            _spare ??= list;
        }
    }

    /// <summary>
    /// Rate limit for events the extension writes on its own: true at most once per second of the
    /// current run's ticks (<paramref name="k"/> / <paramref name="tickHz"/>; <c>k</c> restarts at 0
    /// with every run) for each <paramref name="kind"/>. Every run starts with nothing reported
    /// (<see cref="SyncRun"/> clears the entries), so a second of one run is never mistaken for
    /// the same second of an earlier one. Thread-safe.
    /// </summary>
    public bool ShouldReport(BindingEvent kind, ulong k, int tickHz)
    {
        long second = tickHz > 0 ? (long)(k / (ulong)tickHz) : 0;
        return Interlocked.Exchange(ref _lastReportSecond[(int)kind], second) != second;
    }

    /// <summary>As <see cref="ShouldReport(BindingEvent, ulong, int)"/> at the computer's last tick.</summary>
    public bool ShouldReport(BindingEvent kind) => ShouldReport(kind, Computer.LastK, Computer.TickHz);

    /// <summary>
    /// Starts a new run from fresh state when the computer has started one since the last call:
    /// rebuilds the lists (a failure is counted and logged once; the old lists stay) and clears the
    /// rate limits. Returns true when it did. Called first by the router on every call (dispatch
    /// thread) and by the pump on every drain (main thread); with no new run it is one reference
    /// compare and allocates nothing.
    /// </summary>
    internal bool SyncRun()
    {
        TickGrid? grid = Computer.Grid;
        if (grid is null || ReferenceEquals(grid, Volatile.Read(ref _run))) return false;
        lock (_runGate)
        {
            if (ReferenceEquals(grid, _run)) return false;
            for (int i = 0; i < _lastReportSecond.Length; i++) Interlocked.Exchange(ref _lastReportSecond[i], -1);
            try
            {
                Parts.Rebuild();
            }
            catch (Exception e)
            {
                CountFault();
                KfsLog.Once("rebuild", e);
            }
            Interlocked.Increment(ref _runs);
            // Holding the grid keeps it alive, so a later run can never get an equal reference.
            Volatile.Write(ref _run, grid);
            return true;
        }
    }

    /// <summary>
    /// Writes <paramref name="text"/> to the program's log from the main thread: queued on the
    /// computer and written on the dispatch thread at its next dispatch (the stock path for
    /// main-thread work).
    /// </summary>
    public void Report(string text) => Computer.Enqueue(c => (c as VehicleComputer)?.Event(text));

    internal void CountAccepted() => Interlocked.Increment(ref _accepted);
    internal void CountRejected() => Interlocked.Increment(ref _rejected);
    internal void CountAnswered() => Interlocked.Increment(ref _answered);
    internal void CountApplied() => Interlocked.Increment(ref _applied);
    internal void CountApplyFailure() => Interlocked.Increment(ref _applyFailures);
    internal void CountDiscarded() => Interlocked.Increment(ref _discarded);
    internal void CountFault() => Interlocked.Increment(ref _faults);

    internal void MarkUnbound() => _bound = false;
}
