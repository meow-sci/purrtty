using System.Collections.Concurrent;
using purrTTY.Logging;

namespace purrTTY.GameMod.Launcher;

/// <summary>
/// Marshals work from any thread onto the game's main thread, inside the ImGui frame.
/// Producers <see cref="Post"/> (or <see cref="RunAsync{T}"/>) from anywhere; the owner calls
/// <see cref="Drain"/> once per frame at the very top of <c>TerminalMod.OnAfterUi</c> — before the
/// controller renders and before the in-world coordinator updates — so queued work runs while an
/// ImGui frame is active (a precondition for <c>InWorldTerminalManager.Create</c>,
/// <c>TerminalWindow</c> construction and <c>TerminalTargetRegistry</c>).
/// <para>
/// Work posted <i>while</i> a drain is running (including from the work itself) runs on the next
/// frame: the drain only runs the items queued when it started, which rules out an unbounded loop.
/// A <see cref="RunAsync{T}"/> call made on the main thread while the pump is running executes
/// inline instead of queueing.
/// </para>
/// </summary>
internal sealed class MainThreadPump
{
    // Per-thread (not per-instance): "this thread is currently running pump work" is a property of the
    // call stack. There is one pump in practice.
    [ThreadStatic]
    private static bool _pumping;

    private readonly ConcurrentQueue<Work> _queue = new();
    private volatile bool _shutDown;

    /// <summary>True while the calling thread is inside <see cref="Drain"/> or a <see cref="EnterPumpScope"/>.</summary>
    public bool IsPumping => _pumping;

    /// <summary>Number of queued items (diagnostics and tests).</summary>
    public int PendingCount => _queue.Count;

    /// <summary>
    /// Queues <paramref name="action"/> to run on the main thread at the next <see cref="Drain"/>.
    /// Ignored after <see cref="Shutdown"/>.
    /// </summary>
    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (_shutDown)
        {
            return;
        }

        _queue.Enqueue(new Work(action, null));
    }

    /// <summary>
    /// Runs the items that are queued right now (not ones posted meanwhile). A throwing item is logged
    /// and does not stop the rest. Main thread only.
    /// </summary>
    public void Drain()
    {
        int count = _queue.Count;
        if (count == 0)
        {
            return;
        }

        using var scope = EnterPumpScope();
        for (int i = 0; i < count && _queue.TryDequeue(out var work); i++)
        {
            try
            {
                work.Run();
            }
            catch (Exception ex)
            {
                LogError($"purrTTY launcher: queued main-thread work threw: {ex}");
            }
        }
    }

    /// <summary>
    /// Marks the calling (main) thread as pumping for the scope, so <see cref="RunAsync{T}"/> inside
    /// it runs inline. <see cref="Drain"/> uses this; the launcher's per-frame tick does too.
    /// </summary>
    public PumpScope EnterPumpScope() => new(_pumping);

    /// <summary>
    /// Runs <paramref name="func"/> on the main thread and completes the returned task with its
    /// result (or fault). Executes inline when already pumping. Cancelling <paramref name="ct"/>
    /// before the work runs cancels the task instead.
    /// </summary>
    public Task<T> RunAsync<T>(Func<T> func, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(func);
        if (ct.IsCancellationRequested)
        {
            return Task.FromCanceled<T>(ct);
        }

        if (_pumping)
        {
            try
            {
                return Task.FromResult(func());
            }
            catch (Exception ex)
            {
                return Task.FromException<T>(ex);
            }
        }

        if (_shutDown)
        {
            return Task.FromCanceled<T>(new CancellationToken(true));
        }

        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _queue.Enqueue(new Work(
            () =>
            {
                if (ct.IsCancellationRequested)
                {
                    tcs.TrySetCanceled(ct);
                    return;
                }

                try
                {
                    tcs.TrySetResult(func());
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            },
            () => tcs.TrySetCanceled()));
        return tcs.Task;
    }

    /// <summary>Non-generic <see cref="RunAsync{T}"/> for work with no result.</summary>
    public Task RunAsync(Action action, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        return RunAsync<object?>(
            () =>
            {
                action();
                return null;
            },
            ct);
    }

    /// <summary>
    /// Stops accepting work and cancels every queued <see cref="RunAsync{T}"/> task (so no awaiter
    /// hangs on a pump that will never drain again); queued plain <see cref="Post"/> items are dropped.
    /// </summary>
    public void Shutdown()
    {
        _shutDown = true;
        while (_queue.TryDequeue(out var work))
        {
            try
            {
                work.Cancel?.Invoke();
            }
            catch (Exception ex)
            {
                LogError($"purrTTY launcher: cancelling queued work threw: {ex.Message}");
            }
        }
    }

    // Logging is best-effort and must never turn into a second failure inside the drain loop. The
    // ModLog reference lives in its own non-inlined method so that a missing logging assembly chain
    // (a test host: Brutal.Logging needs Microsoft.Extensions.ObjectPool, gotcha 30) surfaces as an
    // exception caught here rather than as a JIT-time failure of the caller.
    private static void LogError(string message)
    {
        try
        {
            WriteLog(message);
        }
        catch
        {
            // best-effort
        }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void WriteLog(string message) => ModLog.Log.Error(message);

    private readonly record struct Work(Action Run, Action? Cancel);

    /// <summary>Restores the previous pumping flag on dispose (a struct: the per-frame path allocates nothing).</summary>
    public readonly struct PumpScope : IDisposable
    {
        private readonly bool _previous;

        internal PumpScope(bool previous)
        {
            _previous = previous;
            _pumping = true;
        }

        /// <inheritdoc/>
        public void Dispose() => _pumping = _previous;
    }
}
