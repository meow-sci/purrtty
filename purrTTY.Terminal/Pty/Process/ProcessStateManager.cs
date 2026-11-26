using SysProcess = System.Diagnostics.Process;

namespace purrTTY.Core.Terminal.Process;

/// <summary>
///     Manages process state queries with proper locking and error handling.
///     Provides safe access to process properties like IsRunning, ProcessId, and ExitCode.
/// </summary>
internal static class ProcessStateManager
{
    /// <summary>
    ///     Gets whether a shell process is currently running.
    /// </summary>
    /// <param name="process">The process to check</param>
    /// <param name="processLock">Lock object for thread safety</param>
    /// <returns>True if the process is running, false otherwise</returns>
    internal static bool IsRunning(SysProcess? process, object processLock)
    {
        lock (processLock)
        {
            if (process == null)
            {
                return false;
            }

            try
            {
                return !process.HasExited;
            }
            catch (InvalidOperationException)
            {
                // Process has been disposed
                return false;
            }
        }
    }

    /// <summary>
    ///     Gets the process ID of the running shell, or null if no process is running.
    /// </summary>
    /// <param name="process">The process to query</param>
    /// <param name="processLock">Lock object for thread safety</param>
    /// <returns>The process ID, or null if no process is running</returns>
    internal static int? GetProcessId(SysProcess? process, object processLock)
    {
        lock (processLock)
        {
            return process?.Id;
        }
    }

    /// <summary>
    ///     Gets the exit code of the last process, or null if no process has exited.
    /// </summary>
    /// <param name="process">The process to query</param>
    /// <param name="processLock">Lock object for thread safety</param>
    /// <returns>The exit code, or null if no process has exited</returns>
    internal static int? GetExitCode(SysProcess? process, object processLock)
    {
        lock (processLock)
        {
            return process?.HasExited == true ? process.ExitCode : null;
        }
    }
}
