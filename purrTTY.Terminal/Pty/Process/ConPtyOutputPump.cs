using System.Runtime.InteropServices;

namespace purrTTY.Core.Terminal.Process;

/// <summary>
///     Handles reading from the ConPTY output pipe.
///     Runs a blocking read loop on a dedicated long-running thread: ReadFile on
///     the pipe blocks until data is available, so the loop needs no delay — and
///     must not have one. (A previous version slept after every 4 KB read, which
///     capped throughput at single-digit MB/s and smeared full-screen TUI frames
///     across many render ticks, showing up as tearing.)
/// </summary>
internal static class ConPtyOutputPump
{
    /// <summary>
    ///     Reads from the ConPTY output pipe until cancellation, broken pipe, or error.
    ///     Returns a task representing the dedicated reader thread.
    /// </summary>
    /// <param name="outputHandle">The output pipe handle to read from</param>
    /// <param name="onDataReceived">Callback invoked when data is received (receives byte array)</param>
    /// <param name="onProcessError">Callback invoked when an error occurs (receives exception and message)</param>
    /// <param name="cancellationToken">Cancellation token (checked between reads; teardown closes the pipe to unblock a pending read)</param>
    internal static Task ReadOutputAsync(
        IntPtr outputHandle,
        Action<byte[]> onDataReceived,
        Action<Exception, string> onProcessError,
        CancellationToken cancellationToken)
    {
        return Task.Factory.StartNew(
            () => ReadOutputLoop(outputHandle, onDataReceived, onProcessError, cancellationToken),
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
    }

    private static void ReadOutputLoop(
        IntPtr outputHandle,
        Action<byte[]> onDataReceived,
        Action<Exception, string> onProcessError,
        CancellationToken cancellationToken)
    {
        // Large enough to drain a fast producer (full-screen TUI redraws) in few
        // syscalls; ConPTY's internal pipe buffer is on this order anyway.
        const int bufferSize = 64 * 1024;
        byte[] buffer = new byte[bufferSize];

        try
        {
            while (!cancellationToken.IsCancellationRequested && outputHandle != IntPtr.Zero)
            {
                if (ConPtyNative.ReadFile(outputHandle, buffer, bufferSize, out uint bytesRead, IntPtr.Zero))
                {
                    if (bytesRead == 0)
                    {
                        // End of stream
                        break;
                    }

                    // Create a copy of the data to avoid buffer reuse issues
                    byte[] data = new byte[bytesRead];
                    Array.Copy(buffer, 0, data, 0, (int)bytesRead);

                    // Raise the DataReceived event (ConPTY output is never "error" stream)
                    onDataReceived(data);
                }
                else
                {
                    int error = Marshal.GetLastWin32Error();
                    if (error is 109 or 6) // ERROR_BROKEN_PIPE / ERROR_INVALID_HANDLE (teardown closed the pipe)
                    {
                        break;
                    }

                    onProcessError(
                        new InvalidOperationException($"ReadFile failed with error {error}"),
                        $"Error reading from ConPTY output: Win32 error {error}");
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            onProcessError(ex, $"Error reading from ConPTY output: {ex.Message}");
        }
    }
}
