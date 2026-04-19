using SysProcess = System.Diagnostics.Process;

namespace caTTY.Core.Terminal.Process;

/// <summary>
///     Handles cleanup of ConPTY and process resources.
///     Manages disposal of handles, pseudoconsole, and process objects.
/// </summary>
internal static class ProcessCleanup
{
    /// <summary>
    ///     Cleans up a process by unhooking events and disposing.
    /// </summary>
    /// <param name="process">The process to clean up</param>
    /// <param name="onProcessExited">The event handler to unhook (if any)</param>
    internal static void CleanupProcess(SysProcess? process, EventHandler? onProcessExited)
    {
        if (process != null)
        {
            if (onProcessExited != null)
            {
                process.Exited -= onProcessExited;
            }
            process.Dispose();
        }
    }

    /// <summary>
    ///     Cleans up ConPTY-specific resources (pseudoconsole and pipe handles).
    /// </summary>
    /// <param name="pseudoConsole">The pseudoconsole handle to close</param>
    /// <param name="inputReadHandle">The input read pipe handle</param>
    /// <param name="inputWriteHandle">The input write pipe handle</param>
    /// <param name="outputReadHandle">The output read pipe handle</param>
    /// <param name="outputWriteHandle">The output write pipe handle</param>
    internal static void CleanupPseudoConsole(
        IntPtr pseudoConsole,
        IntPtr inputReadHandle,
        IntPtr inputWriteHandle,
        IntPtr outputReadHandle,
        IntPtr outputWriteHandle)
    {
        if (pseudoConsole != IntPtr.Zero)
        {
            ConPtyNative.ClosePseudoConsole(pseudoConsole);
        }

        CleanupHandles(inputReadHandle, inputWriteHandle, outputReadHandle, outputWriteHandle);
    }

    /// <summary>
    ///     Cleans up pipe handles.
    /// </summary>
    /// <param name="inputReadHandle">The input read pipe handle</param>
    /// <param name="inputWriteHandle">The input write pipe handle</param>
    /// <param name="outputReadHandle">The output read pipe handle</param>
    /// <param name="outputWriteHandle">The output write pipe handle</param>
    internal static void CleanupHandles(
        IntPtr inputReadHandle,
        IntPtr inputWriteHandle,
        IntPtr outputReadHandle,
        IntPtr outputWriteHandle)
    {
        if (inputWriteHandle != IntPtr.Zero)
        {
            ConPtyNative.CloseHandle(inputWriteHandle);
        }

        if (outputReadHandle != IntPtr.Zero)
        {
            ConPtyNative.CloseHandle(outputReadHandle);
        }

        if (inputReadHandle != IntPtr.Zero)
        {
            ConPtyNative.CloseHandle(inputReadHandle);
        }

        if (outputWriteHandle != IntPtr.Zero)
        {
            ConPtyNative.CloseHandle(outputWriteHandle);
        }
    }
}
