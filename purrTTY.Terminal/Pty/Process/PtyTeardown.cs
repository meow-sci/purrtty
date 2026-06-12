namespace purrTTY.Core.Terminal.Process;

/// <summary>
///     The teardown disciplines shared by both PTY backends (<c>ProcessManager</c>
///     and <c>UnixProcessManager</c>). Each backend keeps its own resource-close
///     policy — which handles/fds close under which pump-exit conditions is
///     genuinely platform-specific — but these steps are identical and subtle
///     enough that they drifted apart once before being unified:
///     cancel-outside-the-lock, bounded never-throwing pump waits, and
///     join-before-dispose for the input writer (gotchas 14/16/20).
/// </summary>
internal static class PtyTeardown
{
    /// <summary>
    ///     Cancels and disposes a read-cancellation source. Must be called
    ///     OUTSIDE the manager's state lock: a CTS callback running while the
    ///     lock is held invites re-entrancy deadlocks.
    /// </summary>
    internal static void CancelAndDispose(CancellationTokenSource? cts)
    {
        if (cts == null)
        {
            return;
        }

        try
        {
            cts.Cancel();
        }
        finally
        {
            cts.Dispose();
        }
    }

    /// <summary>
    ///     Bounded wait for a pump task. Returns true only when the task has
    ///     provably finished (a faulted task counts — its thread is out of the
    ///     handle/fd). Never throws: pump errors are reported through
    ///     <c>ProcessError</c>, not here. A false return means the caller must
    ///     leak the associated handle/fd rather than close it (closing a
    ///     handle another thread is blocked on races handle/fd reuse).
    /// </summary>
    internal static bool WaitForPump(Task? task, int timeoutMs)
    {
        if (task == null || task.IsCompleted)
        {
            return true;
        }

        try
        {
            return task.Wait(timeoutMs);
        }
        catch
        {
            return task.IsCompleted;
        }
    }

    /// <summary>
    ///     Stops the input queue's writer thread and disposes the queue only
    ///     once the thread has provably exited. Returns whether it exited —
    ///     false means the writer may still be blocked in a write, so the
    ///     caller must leak the write handle/fd it uses.
    /// </summary>
    internal static bool ShutdownWriter(PtyInputQueue? queue, int timeoutMs)
    {
        bool writerExited = queue?.Shutdown(timeoutMs) ?? true;
        if (writerExited)
        {
            queue?.Dispose();
        }

        return writerExited;
    }
}
