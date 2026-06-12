using System.Runtime.InteropServices;
using SysProcess = System.Diagnostics.Process;

namespace purrTTY.Core.Terminal.Process;

/// <summary>
///     Manages process lifecycle operations including graceful shutdown.
/// </summary>
internal static class ProcessLifecycleManager
{
    /// <summary>
    ///     Performs graceful shutdown of a process with fallback to forced termination.
    ///     Deliberately does NOT touch the output pump or its cancellation source:
    ///     the pump must keep reading until the pseudoconsole is closed (in
    ///     CleanupProcess, which always follows) so the dying shell's tail output is
    ///     drained, not raced — cancelling reads here lost the tail whenever the
    ///     pump was between reads.
    /// </summary>
    /// <param name="process">The process to stop</param>
    /// <returns>A task that completes when the process has stopped</returns>
    internal static Task StopProcessGracefullyAsync(SysProcess? process)
    {
        if (process == null)
        {
            return Task.CompletedTask; // No process running
        }

        // Try graceful shutdown first. Every process call can throw if the
        // child exits (and the exit handler disposes the Process) mid-stop:
        // HasExited/CloseMainWindow/WaitForExit throw InvalidOperationException
        // on a disposed process, and Kill can throw Win32Exception when the
        // process is already terminating.
        try
        {
            if (!process.HasExited)
            {
                // For ConPTY processes, we can try CloseMainWindow first, then Kill if needed
                process.CloseMainWindow();

                // Wait a short time for graceful shutdown
                if (!process.WaitForExit(2000))
                {
                    process.Kill(true);
                }
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException or System.ComponentModel.Win32Exception)
        {
            // Process already exited / disposed by the concurrent exit handler.
        }

        return Task.CompletedTask;
    }

    /// <summary>
    ///     Creates a Windows process using ConPTY with the specified command and startup info.
    /// </summary>
    /// <param name="commandLine">The command line to execute</param>
    /// <param name="workingDirectory">The working directory for the process</param>
    /// <param name="startupInfo">The startup information with attribute list</param>
    /// <param name="environmentBlock">Optional pointer to environment block for the new process</param>
    /// <returns>The process information structure</returns>
    /// <exception cref="ProcessStartException">Thrown if process creation fails</exception>
    internal static ConPtyNative.PROCESS_INFORMATION CreateProcess(
        string commandLine,
        string workingDirectory,
        ref ConPtyNative.STARTUPINFOEX startupInfo,
        IntPtr environmentBlock = default)
    {
        var processInfo = new ConPtyNative.PROCESS_INFORMATION();

        // Include CREATE_UNICODE_ENVIRONMENT flag when environment block is provided
        uint creationFlags = ConPtyNative.EXTENDED_STARTUPINFO_PRESENT;
        if (environmentBlock != IntPtr.Zero)
        {
            creationFlags |= ConPtyNative.CREATE_UNICODE_ENVIRONMENT;
        }

        if (!ConPtyNative.CreateProcessW(
                null,
                commandLine,
                IntPtr.Zero,
                IntPtr.Zero,
                false,
                creationFlags,
                environmentBlock,
                workingDirectory,
                ref startupInfo,
                out processInfo))
        {
            int error = Marshal.GetLastWin32Error();
            throw new ProcessStartException($"Failed to create process: {error}");
        }

        return processInfo;
    }

    /// <summary>
    ///     Creates an environment block from the current environment plus additional variables.
    ///     Environment blocks for CreateProcess must be Unicode (UTF-16) null-terminated strings
    ///     in the format "NAME=VALUE\0NAME2=VALUE2\0\0".
    /// </summary>
    /// <param name="additionalVariables">Additional environment variables to add/override</param>
    /// <returns>Pointer to the environment block (must be freed with Marshal.FreeHGlobal)</returns>
    internal static IntPtr CreateEnvironmentBlock(Dictionary<string, string>? additionalVariables)
    {
        if (additionalVariables == null || additionalVariables.Count == 0)
        {
            return IntPtr.Zero; // Use parent process environment
        }

        // Merge current environment with additional variables. Windows env var
        // names are case-insensitive — an Ordinal merge can produce "Path" and
        // "PATH" duplicates in the block.
        var envVars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key?.ToString() is { Length: > 0 } key)
            {
                envVars[key] = entry.Value?.ToString() ?? string.Empty;
            }
        }

        foreach (var kvp in additionalVariables)
        {
            if (!string.IsNullOrEmpty(kvp.Key))
            {
                envVars[kvp.Key] = kvp.Value ?? string.Empty;
            }
        }

        // Build environment block string: "NAME=VALUE\0NAME2=VALUE2\0\0".
        // CreateProcessW requires the block sorted alphabetically by name,
        // case-insensitively.
        var envBlock = new System.Text.StringBuilder();
        foreach (string key in envVars.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
        {
            envBlock.Append($"{key}={envVars[key]}\0");
        }
        envBlock.Append('\0'); // Final null terminator

        // Allocate unmanaged memory for Unicode environment block
        return Marshal.StringToHGlobalUni(envBlock.ToString());
    }

    /// <summary>
    ///     Wraps a Windows process handle in a managed Process object with event handling.
    /// </summary>
    /// <param name="processInfo">The process information from CreateProcessW</param>
    /// <param name="onProcessExited">Event handler for process exit</param>
    /// <returns>A managed Process object</returns>
    internal static SysProcess WrapProcessHandle(ConPtyNative.PROCESS_INFORMATION processInfo, EventHandler onProcessExited)
    {
        try
        {
            var process = SysProcess.GetProcessById(processInfo.dwProcessId);
            process.EnableRaisingEvents = true;
            process.Exited += onProcessExited;
            return process;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            // The child exited before we could re-attach by PID. Read the exit code
            // off the raw handle (still valid — closed in the finally) and report a
            // failed start instead of leaking the handles.
            uint exitCode = 0;
            _ = ConPtyNative.GetExitCodeProcess(processInfo.hProcess, out exitCode);
            throw new ProcessStartException(
                $"Shell process exited immediately with code {exitCode} before it could be attached", ex);
        }
        finally
        {
            // Close process and thread handles on every path (success keeps the
            // managed Process object's own handle).
            ConPtyNative.CloseHandle(processInfo.hProcess);
            ConPtyNative.CloseHandle(processInfo.hThread);
        }
    }
}
