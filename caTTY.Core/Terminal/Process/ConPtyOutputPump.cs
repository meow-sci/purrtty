using System.Runtime.InteropServices;

namespace caTTY.Core.Terminal.Process;

/// <summary>
///     Handles asynchronous reading from ConPTY output pipe.
///     Manages the read loop, buffer allocation, and error handling for ConPTY output.
/// </summary>
internal static class ConPtyOutputPump
{
    /// <summary>
    ///     Reads data from the ConPTY output pipe asynchronously.
    /// </summary>
    /// <param name="outputHandle">The output pipe handle to read from</param>
    /// <param name="getProcessId">Function to get the current process ID</param>
    /// <param name="onDataReceived">Callback invoked when data is received (receives byte array)</param>
    /// <param name="onProcessError">Callback invoked when an error occurs (receives exception and message)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    internal static async Task ReadOutputAsync(
        IntPtr outputHandle,
        Func<int?> getProcessId,
        Action<byte[]> onDataReceived,
        Action<Exception, string> onProcessError,
        CancellationToken cancellationToken)
    {
        const int bufferSize = 4096;
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
                    if (error == 109) // ERROR_BROKEN_PIPE
                    {
                        // Process has exited
                        break;
                    }

                    onProcessError(
                        new InvalidOperationException($"ReadFile failed with error {error}"),
                        $"Error reading from ConPTY output: Win32 error {error}");
                    break;
                }

                // Small delay to prevent tight loop
                await Task.Delay(1, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when cancellation is requested
        }
        catch (Exception ex)
        {
            onProcessError(ex, $"Error reading from ConPTY output: {ex.Message}");
        }
    }
}
