using System.Runtime.InteropServices;
using System.Text;
using SysProcess = System.Diagnostics.Process;

namespace caTTY.Core.Terminal.Process;

/// <summary>
///     Handles synchronous writing to ConPTY input pipe.
///     Manages the write operation, buffer handling, and error reporting for ConPTY input.
/// </summary>
internal static class ConPtyInputWriter
{
    /// <summary>
    ///     Writes data to the ConPTY input pipe.
    /// </summary>
    /// <param name="data">The data to write</param>
    /// <param name="inputWriteHandle">The input pipe handle to write to</param>
    /// <param name="currentProcess">The current process (for error reporting)</param>
    /// <param name="onProcessError">Callback invoked when an error occurs (receives ProcessErrorEventArgs)</param>
    /// <exception cref="ProcessWriteException">Thrown if writing fails</exception>
    internal static void Write(
        ReadOnlySpan<byte> data,
        IntPtr inputWriteHandle,
        SysProcess currentProcess,
        Action<ProcessErrorEventArgs> onProcessError)
    {
        try
        {
            byte[] buffer = data.ToArray();
            if (!ConPtyNative.WriteFile(inputWriteHandle, buffer, (uint)buffer.Length, out uint bytesWritten, IntPtr.Zero))
            {
                int error = Marshal.GetLastWin32Error();
                int processId = currentProcess.Id;
                var writeException =
                    new ProcessWriteException($"Failed to write to ConPTY input: Win32 error {error}", processId);
                onProcessError(new ProcessErrorEventArgs(writeException, writeException.Message, processId));
                throw writeException;
            }
        }
        catch (Exception ex) when (!(ex is ProcessWriteException))
        {
            int processId = currentProcess.Id;
            var writeException =
                new ProcessWriteException($"Failed to write to ConPTY input: {ex.Message}", ex, processId);
            onProcessError(new ProcessErrorEventArgs(writeException, writeException.Message, processId));
            throw writeException;
        }
    }

    /// <summary>
    ///     Writes string data to the ConPTY input pipe.
    /// </summary>
    /// <param name="text">The text to write (will be converted to UTF-8)</param>
    /// <param name="inputWriteHandle">The input pipe handle to write to</param>
    /// <param name="currentProcess">The current process (for error reporting)</param>
    /// <param name="onProcessError">Callback invoked when an error occurs (receives ProcessErrorEventArgs)</param>
    /// <exception cref="ProcessWriteException">Thrown if writing fails</exception>
    internal static void Write(
        string text,
        IntPtr inputWriteHandle,
        SysProcess currentProcess,
        Action<ProcessErrorEventArgs> onProcessError)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        byte[] bytes = Encoding.UTF8.GetBytes(text);
        Write(bytes.AsSpan(), inputWriteHandle, currentProcess, onProcessError);
    }
}
