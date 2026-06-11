using System.Collections.Concurrent;

namespace purrTTY.Core.Terminal.Process;

/// <summary>
///     Bounded PTY input queue drained by a dedicated writer thread — the input-side
///     mirror of the output pumps. A PTY write can block indefinitely when the child
///     stops reading (full slave input queue, SIGSTOP, XOFF, a large paste), and
///     <c>IProcessManager.Write</c> is called on the render tick thread (user input
///     and engine PtyReply during BuildFrame), so the tick thread must never issue
///     the blocking write itself. Enqueue never blocks; overflow drops the incoming
///     chunk and reports one error per overflow episode; write failures surface
///     through the error callback (and subsequent chunks are dropped — the owning
///     manager tears the session down via its own exit/error paths).
/// </summary>
internal sealed class PtyInputQueue : IDisposable
{
    /// <summary>
    ///     Cap on bytes queued but not yet written. Interactive input is tiny; only a
    ///     huge paste into a stopped child can hit this, and dropping beats unbounded
    ///     growth while the writer is blocked.
    /// </summary>
    internal const int MaxPendingBytes = 1024 * 1024;

    private readonly BlockingCollection<byte[]> _queue = new();
    private readonly Thread _thread;
    private readonly Action<byte[]> _writeChunk;
    private readonly Action<Exception, string> _onError;
    private int _pendingBytes;
    private bool _overflowReported;
    private volatile bool _failed;
    private bool _disposed;

    /// <param name="name">Writer thread name (diagnostics).</param>
    /// <param name="writeChunk">
    ///     Performs the actual blocking write; runs on the writer thread only. May
    ///     throw — the first failure is reported and later chunks are dropped.
    /// </param>
    /// <param name="onError">Receives enqueue-overflow and write failures (writer/caller thread).</param>
    internal PtyInputQueue(string name, Action<byte[]> writeChunk, Action<Exception, string> onError)
    {
        _writeChunk = writeChunk;
        _onError = onError;
        _thread = new Thread(WriteLoop) { IsBackground = true, Name = name };
        _thread.Start();
    }

    /// <summary>Queues a copy of <paramref name="data"/> for the writer thread. Never blocks.</summary>
    internal void Write(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty || _failed)
        {
            return;
        }

        if (Volatile.Read(ref _pendingBytes) + data.Length > MaxPendingBytes)
        {
            if (!_overflowReported)
            {
                _overflowReported = true;
                _onError(
                    new ProcessWriteException(
                        $"PTY input queue overflow (> {MaxPendingBytes} bytes pending); dropping input"),
                    "PTY input queue overflow; the child is not reading its input");
            }

            return;
        }

        _overflowReported = false;
        Interlocked.Add(ref _pendingBytes, data.Length);
        try
        {
            _queue.Add(data.ToArray());
        }
        catch (InvalidOperationException)
        {
            // CompleteAdding raced this enqueue during teardown — the session is
            // going away, dropping the chunk is fine.
            Interlocked.Add(ref _pendingBytes, -data.Length);
        }
    }

    private void WriteLoop()
    {
        foreach (byte[] chunk in _queue.GetConsumingEnumerable())
        {
            Interlocked.Add(ref _pendingBytes, -chunk.Length);

            if (_failed)
            {
                continue; // drain-and-drop after a fatal write error
            }

            try
            {
                _writeChunk(chunk);
            }
            catch (Exception ex)
            {
                _failed = true;
                _onError(ex, $"PTY input write failed: {ex.Message}");
            }
        }
    }

    /// <summary>
    ///     Completes the queue and joins the writer thread. Returns false if the thread
    ///     did not exit in time — i.e. it is still blocked inside a write. The caller
    ///     must then LEAK the write target (fd/handle) rather than close it: closing
    ///     out from under a blocked writer races fd/handle reuse elsewhere in the
    ///     process, which is the exact bug class this layer documents and avoids.
    /// </summary>
    internal bool Shutdown(int timeoutMs)
    {
        try
        {
            _queue.CompleteAdding();
        }
        catch (ObjectDisposedException)
        {
            return true;
        }

        return _thread.Join(timeoutMs);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Only reclaim the collection once the writer thread is out of it; if the
        // join timed out the thread is abandoned (background) and the collection
        // is intentionally leaked with it.
        if (Shutdown(2000))
        {
            _queue.Dispose();
        }
    }
}
