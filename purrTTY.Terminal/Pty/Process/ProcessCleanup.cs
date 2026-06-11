using SysProcess = System.Diagnostics.Process;

namespace purrTTY.Core.Terminal.Process;

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

}
