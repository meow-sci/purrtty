using System.Runtime.InteropServices;

namespace purrTTY.Core.Terminal.Process;

/// <summary>
///     Performs the blocking WriteFile to the ConPTY input pipe. Called only from the
///     <see cref="PtyInputQueue"/> writer thread — never from the render tick thread,
///     because the pipe write blocks while the child is not reading its input.
/// </summary>
internal static class ConPtyInputWriter
{
    /// <summary>
    ///     Writes the whole buffer to the pipe, looping on partial writes.
    /// </summary>
    /// <param name="inputWriteHandle">The input pipe handle to write to</param>
    /// <param name="buffer">The bytes to write</param>
    /// <param name="processId">Process id for error reporting (may be null)</param>
    /// <exception cref="ProcessWriteException">Thrown if writing fails</exception>
    internal static void WriteAll(IntPtr inputWriteHandle, byte[] buffer, int? processId)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            // WriteFile has no offset parameter for byte[]; re-slice on the rare
            // partial write (anonymous pipes normally complete in one call).
            byte[] chunk = offset == 0 ? buffer : buffer[offset..];
            if (!ConPtyNative.WriteFile(inputWriteHandle, chunk, (uint)chunk.Length, out uint bytesWritten, IntPtr.Zero))
            {
                int error = Marshal.GetLastWin32Error();
                throw new ProcessWriteException($"Failed to write to ConPTY input: Win32 error {error}", processId);
            }

            if (bytesWritten == 0)
            {
                throw new ProcessWriteException("Failed to write to ConPTY input: pipe closed", processId);
            }

            offset += (int)bytesWritten;
        }
    }
}
