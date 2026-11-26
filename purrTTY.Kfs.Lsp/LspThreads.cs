using System.Collections.Concurrent;

namespace purrTTY.Kfs.Lsp;

/// <summary>
/// Every thread the language-server library starts goes through here, so a host (and the tests) can
/// tell that a stopped server or listener left nothing behind: the connection writers, the listener's
/// accept and client threads and the build scheduler's compile threads (retired ones included, until
/// their compile returns).
/// <list type="bullet">
/// <item><b>Nothing escapes.</b> An exception that leaves a thread's body is caught here and logged
/// (<see cref="Log"/>); it never reaches the runtime, which would end the process — the game's.</item>
/// <item><b>A failed start</b> (the process is at its thread limit) is logged and reported as null,
/// never thrown: the caller carries on without the thread.</item>
/// <item><b>Retired threads</b> (an abandoned compile, which cannot be interrupted) are counted
/// apart (<see cref="Retired"/>): a stop waits only for the others (<see cref="Active"/>), and the
/// number of retired threads is capped process-wide by the build schedulers.</item>
/// </list>
/// </summary>
public static class LspThreads
{
    private const int Running = 0, RetiredState = 1, Ended = 2;

    private static readonly ConcurrentDictionary<Thread, Slot> Slots = new();
    private static int _alive;
    private static int _retired;
    private static int _failNextStarts;

    /// <summary>Threads started by this library that have not ended yet, process-wide.</summary>
    public static int Alive => Volatile.Read(ref _alive);

    /// <summary>Of <see cref="Alive"/>, the retired ones: abandoned compiles that end when (if ever) their compile returns.</summary>
    public static int Retired => Volatile.Read(ref _retired);

    /// <summary><see cref="Alive"/> without the <see cref="Retired"/> threads: what a stop waits for.</summary>
    public static int Active => Math.Max(0, Alive - Retired);

    /// <summary>Where an exception that left a thread's body, or a failed thread start, is logged (default: purrTTY's game log).</summary>
    public static LspLogSink Log { get; set; } = LspLog.ModLog;

    /// <summary>Tests: the next <paramref name="count"/> starts fail as if the process were at its thread limit.</summary>
    internal static void FailNextStarts(int count) => Volatile.Write(ref _failNextStarts, count);

    /// <summary>
    /// Starts a background thread whose life is counted in <see cref="Alive"/>; null (logged) when the
    /// thread could not be started. An exception escaping <paramref name="body"/> is logged and the
    /// thread ends; it never reaches the runtime.
    /// </summary>
    internal static Thread? Start(string name, Action body, int maxStackSize = 0, ThreadPriority priority = ThreadPriority.Normal)
    {
        var slot = new Slot();
        Thread thread;
        try
        {
            if (Volatile.Read(ref _failNextStarts) > 0 && Interlocked.Decrement(ref _failNextStarts) >= 0)
                throw new OutOfMemoryException("injected thread start failure (test)");
            thread = new Thread(() => Body(name, body, slot), maxStackSize)
            {
                IsBackground = true,
                Name = name,
                Priority = priority,
            };
            slot.Thread = thread;
            Slots[thread] = slot;
            Interlocked.Increment(ref _alive);
            try
            {
                thread.Start();
            }
            catch
            {
                Slots.TryRemove(thread, out _);
                Interlocked.Decrement(ref _alive);
                throw;
            }
        }
        catch (Exception e)
        {
            // OutOfMemoryException (no thread could be created), ThreadStartException, …
            Log.Safe(LspLogLevel.Error, $"could not start the thread '{name}': {e.GetType().Name}: {e.Message}");
            return null;
        }
        return thread;
    }

    /// <summary>
    /// Marks <paramref name="thread"/> (started here, still running) as retired: it no longer counts
    /// in <see cref="Active"/>. True when it was running and is now retired.
    /// </summary>
    internal static bool Retire(Thread thread)
    {
        if (!Slots.TryGetValue(thread, out Slot? slot)) return false;
        if (Interlocked.CompareExchange(ref slot.State, RetiredState, Running) != Running) return false;
        Interlocked.Increment(ref _retired);
        return true;
    }

    /// <summary>Waits until <see cref="Alive"/> is at most <paramref name="count"/> (true) or the timeout passes (false).</summary>
    public static bool WaitUntilAtMost(int count, TimeSpan timeout) => WaitFor(() => Alive <= count, timeout);

    /// <summary>Waits until <see cref="Active"/> is at most <paramref name="count"/> (true) or the timeout passes (false).</summary>
    public static bool WaitUntilActiveAtMost(int count, TimeSpan timeout) => WaitFor(() => Active <= count, timeout);

    private static bool WaitFor(Func<bool> done, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        var spin = new SpinWait();
        while (!done())
        {
            if (DateTime.UtcNow >= deadline) return false;
            if (spin.NextSpinWillYield) Thread.Sleep(5); // polling interval inside a deadline-bounded wait
            else spin.SpinOnce();
        }
        return true;
    }

    private static void Body(string name, Action body, Slot slot)
    {
        try
        {
            body();
        }
        catch (Exception e)
        {
            // The last line of defence: an unhandled exception on any thread ends the process.
            try { Log.Safe(LspLogLevel.Error, $"the thread '{name}' failed and ended: {e.GetType().Name}: {e.Message}"); }
            catch { /* never throw from here */ }
        }
        finally
        {
            if (slot.Thread is { } t) Slots.TryRemove(t, out _);
            if (Interlocked.Exchange(ref slot.State, Ended) == RetiredState) Interlocked.Decrement(ref _retired);
            Interlocked.Decrement(ref _alive);
        }
    }

    private sealed class Slot
    {
        public int State;
        public Thread? Thread;
    }
}
