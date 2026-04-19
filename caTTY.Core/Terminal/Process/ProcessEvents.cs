using SysProcess = System.Diagnostics.Process;

namespace caTTY.Core.Terminal.Process;

/// <summary>
///     Handles process event logic and event raising.
///     Provides helpers for raising ProcessExited, DataReceived, and ProcessError events.
/// </summary>
internal static class ProcessEvents
{
    /// <summary>
    ///     Handles process exited event by cleaning up and raising ProcessExited event.
    /// </summary>
    /// <param name="sender">The event sender (should be a Process)</param>
    /// <param name="e">The event arguments</param>
    /// <param name="cleanupProcess">Callback to perform process cleanup</param>
    /// <param name="processExitedHandler">The ProcessExited event handler to invoke</param>
    /// <param name="eventSender">The object to use as sender when raising ProcessExited event</param>
    internal static void HandleProcessExited(
        object? sender,
        EventArgs e,
        Action cleanupProcess,
        EventHandler<ProcessExitedEventArgs>? processExitedHandler,
        object eventSender)
    {
        if (sender is SysProcess process)
        {
            int exitCode = process.ExitCode;
            int processId = process.Id;

            // Clean up resources
            cleanupProcess();

            // Raise the ProcessExited event
            processExitedHandler?.Invoke(eventSender, new ProcessExitedEventArgs(exitCode, processId));
        }
    }

    /// <summary>
    ///     Raises the DataReceived event.
    /// </summary>
    /// <param name="handler">The DataReceived event handler to invoke</param>
    /// <param name="sender">The event sender</param>
    /// <param name="args">The event arguments</param>
    internal static void RaiseDataReceived(
        EventHandler<DataReceivedEventArgs>? handler,
        object sender,
        DataReceivedEventArgs args)
    {
        handler?.Invoke(sender, args);
    }

    /// <summary>
    ///     Raises the ProcessError event.
    /// </summary>
    /// <param name="handler">The ProcessError event handler to invoke</param>
    /// <param name="sender">The event sender</param>
    /// <param name="args">The event arguments</param>
    internal static void RaiseProcessError(
        EventHandler<ProcessErrorEventArgs>? handler,
        object sender,
        ProcessErrorEventArgs args)
    {
        handler?.Invoke(sender, args);
    }
}
