using SysProcess = System.Diagnostics.Process;

namespace caTTY.Core.Terminal.Process;

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

    /// <summary>
    ///     Validates that a process is running and the input handle is available.
    /// </summary>
    /// <param name="process">The process to check</param>
    /// <param name="inputWriteHandle">The input write handle to check</param>
    /// <exception cref="InvalidOperationException">Thrown if no process is running or input handle is not available</exception>
    internal static void ValidateProcessRunning(SysProcess? process, IntPtr inputWriteHandle)
    {
        if (process == null || process.HasExited)
        {
            throw new InvalidOperationException("No process is currently running");
        }

        if (inputWriteHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException("Input handle is not available");
        }
    }

    /// <summary>
    ///     Validates that a process is running and the pseudoconsole is available for resizing.
    /// </summary>
    /// <param name="process">The process to check</param>
    /// <param name="pseudoConsole">The pseudoconsole handle to check</param>
    /// <exception cref="InvalidOperationException">Thrown if no process is running or pseudoconsole is not available</exception>
    internal static void ValidateProcessForResize(SysProcess? process, IntPtr pseudoConsole)
    {
        if (process == null || process.HasExited)
        {
            throw new InvalidOperationException("No process is currently running");
        }

        if (pseudoConsole == IntPtr.Zero)
        {
            throw new InvalidOperationException("Pseudoconsole is not available");
        }
    }
}
