using System.Runtime.InteropServices;
using SysProcess = System.Diagnostics.Process;

namespace caTTY.Core.Terminal.Process;

/// <summary>
///     Manages process lifecycle operations including graceful shutdown.
///     Handles process creation validation and shutdown orchestration.
/// </summary>
internal static class ProcessLifecycleManager
{
    /// <summary>
    ///     Validates that a process started successfully after a brief delay.
    /// </summary>
    /// <param name="process">The process to validate</param>
    /// <param name="shellPath">The shell path (for error reporting)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>A task that completes when validation is done</returns>
    /// <exception cref="ProcessStartException">Thrown if the process exited immediately</exception>
    internal static async Task ValidateProcessStartAsync(SysProcess process, string shellPath, CancellationToken cancellationToken)
    {
        // Wait a short time to ensure the process started successfully
        await Task.Delay(100, cancellationToken);

        // Check if process exited immediately
        try
        {
            if (process.HasExited)
            {
                int exitCode = process.ExitCode;
                throw new ProcessStartException(
                    $"Shell process exited immediately with code {exitCode}: {shellPath}", shellPath);
            }
        }
        catch (InvalidOperationException)
        {
            // Process has already exited and been disposed - let the exit handler deal with cleanup
        }
    }

    /// <summary>
    ///     Performs graceful shutdown of a process with fallback to forced termination.
    /// </summary>
    /// <param name="process">The process to stop</param>
    /// <param name="readCancellationSource">Cancellation source for read operations</param>
    /// <param name="outputReadTask">The output read task to wait for</param>
    /// <returns>A task that completes when the process has stopped</returns>
    internal static async Task StopProcessGracefullyAsync(
        SysProcess? process,
        CancellationTokenSource? readCancellationSource,
        Task? outputReadTask)
    {
        if (process == null)
        {
            return; // No process running
        }

        // Cancel read operations
        readCancellationSource?.Cancel();

        // Try graceful shutdown first
        if (!process.HasExited)
        {
            try
            {
                // For ConPTY processes, we can try CloseMainWindow first, then Kill if needed
                process.CloseMainWindow();

                // Wait a short time for graceful shutdown
                if (!process.WaitForExit(2000))
                {
                    process.Kill(true);
                }
            }
            catch (InvalidOperationException)
            {
                // Process already exited
            }
        }

        // Wait for read task to complete
        if (outputReadTask != null)
        {
            try
            {
                await outputReadTask;
            }
            catch (OperationCanceledException)
            {
                // Expected when cancellation is requested
            }
        }
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

        try
        {
            // Merge current environment with additional variables
            var envVars = Environment.GetEnvironmentVariables();
            foreach (var kvp in additionalVariables)
            {
                if (!string.IsNullOrEmpty(kvp.Key))
                {
                    envVars[kvp.Key] = kvp.Value ?? string.Empty;
                }
            }

            // Build environment block string: "NAME=VALUE\0NAME2=VALUE2\0\0"
            var envBlock = new System.Text.StringBuilder();
            foreach (System.Collections.DictionaryEntry entry in envVars)
            {
                // Skip null keys or values
                if (entry.Key == null || entry.Value == null)
                    continue;
                
                string key = entry.Key.ToString() ?? string.Empty;
                string value = entry.Value.ToString() ?? string.Empty;
                
                if (!string.IsNullOrEmpty(key))
                {
                    envBlock.Append($"{key}={value}\0");
                }
            }
            envBlock.Append("\0"); // Final null terminator

            // Allocate unmanaged memory for Unicode environment block
            string envString = envBlock.ToString();
            IntPtr envBlockPtr = Marshal.StringToHGlobalUni(envString);
            return envBlockPtr;
        }
        catch (Exception)
        {
            // If environment block creation fails, return IntPtr.Zero to use parent environment
            return IntPtr.Zero;
        }
    }

    /// <summary>
    ///     Wraps a Windows process handle in a managed Process object with event handling.
    /// </summary>
    /// <param name="processInfo">The process information from CreateProcessW</param>
    /// <param name="onProcessExited">Event handler for process exit</param>
    /// <returns>A managed Process object</returns>
    internal static SysProcess WrapProcessHandle(ConPtyNative.PROCESS_INFORMATION processInfo, EventHandler onProcessExited)
    {
        var process = SysProcess.GetProcessById(processInfo.dwProcessId);
        process.EnableRaisingEvents = true;
        process.Exited += onProcessExited;

        // Close process and thread handles (we have the Process object now)
        ConPtyNative.CloseHandle(processInfo.hProcess);
        ConPtyNative.CloseHandle(processInfo.hThread);

        return process;
    }
}
