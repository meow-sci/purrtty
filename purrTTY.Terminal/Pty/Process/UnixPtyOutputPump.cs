using System.Runtime.InteropServices;

namespace purrTTY.Core.Terminal.Process;

/// <summary>
///     Reads PTY master output on a dedicated long-running thread (the Unix
///     counterpart of <see cref="ConPtyOutputPump"/> — same rule applies: the loop
///     must never sleep while data is flowing). poll() with a short timeout is used
///     only as the blocking primitive so cancellation can be observed without
///     closing the fd under a blocked read (close-while-read is undefined and racy
///     against fd reuse); when data is available poll returns immediately, so
///     throughput is unthrottled.
/// </summary>
internal static class UnixPtyOutputPump
{
    private const int PollTimeoutMs = 100;

    internal static Task ReadOutputAsync(
        int masterFd,
        Action<byte[]> onDataReceived,
        Action<Exception, string> onProcessError,
        CancellationToken cancellationToken)
    {
        return Task.Factory.StartNew(
            () => ReadOutputLoop(masterFd, onDataReceived, onProcessError, cancellationToken),
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
    }

    private static void ReadOutputLoop(
        int masterFd,
        Action<byte[]> onDataReceived,
        Action<Exception, string> onProcessError,
        CancellationToken cancellationToken)
    {
        // Same sizing rationale as ConPtyOutputPump: drain fast TUI redraws in few syscalls.
        const int bufferSize = 64 * 1024;
        byte[] buffer = new byte[bufferSize];

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var pollFd = new UnixPtyNative.PollFd { fd = masterFd, events = UnixPtyNative.POLLIN };
                int pollResult = UnixPtyNative.poll(ref pollFd, 1, PollTimeoutMs);

                if (pollResult < 0)
                {
                    if (Marshal.GetLastPInvokeError() == UnixPtyNative.EINTR)
                    {
                        continue;
                    }

                    onProcessError(
                        new InvalidOperationException($"poll failed: {Marshal.GetLastPInvokeErrorMessage()}"),
                        "Error polling PTY master");
                    break;
                }

                if (pollResult == 0)
                {
                    continue; // timeout — re-check cancellation
                }

                if ((pollFd.revents & UnixPtyNative.POLLNVAL) != 0)
                {
                    // fd is not open — without this exit the loop would spin at the
                    // poll cadence until cancellation.
                    onProcessError(
                        new InvalidOperationException("poll reported POLLNVAL: PTY master fd is not open"),
                        "PTY master fd invalid (POLLNVAL)");
                    break;
                }

                if ((pollFd.revents & (UnixPtyNative.POLLIN | UnixPtyNative.POLLHUP | UnixPtyNative.POLLERR)) == 0)
                {
                    continue;
                }

                // POLLHUP can coexist with buffered output (Linux reports POLLIN|POLLHUP
                // until drained), so always read: data > 0 delivers the tail, then a
                // final read returns 0 (macOS EOF) or -EIO (Linux, slave side gone).
                nint bytesRead = UnixPtyNative.read(masterFd, buffer, bufferSize);

                if (bytesRead > 0)
                {
                    byte[] data = new byte[bytesRead];
                    Array.Copy(buffer, 0, data, 0, (int)bytesRead);
                    onDataReceived(data);
                    continue;
                }

                if (bytesRead == 0)
                {
                    break; // EOF
                }

                int error = Marshal.GetLastPInvokeError();
                if (error == UnixPtyNative.EINTR)
                {
                    continue;
                }

                if (error == UnixPtyNative.EIO)
                {
                    break; // all slave fds closed — child (and its tty holders) exited
                }

                onProcessError(
                    new InvalidOperationException($"read failed: errno {error}"),
                    $"Error reading from PTY master: errno {error}");
                break;
            }
        }
        catch (Exception ex)
        {
            onProcessError(ex, $"Error reading from PTY master: {ex.Message}");
        }
    }
}
