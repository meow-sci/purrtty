namespace caTTY.Core.Terminal;

/// <summary>
///     Bridges session events to the SessionManager's event handling logic.
///     Handles state changes, title changes, and process exit notifications.
/// </summary>
internal class SessionEventBridge
{
    /// <summary>
    ///     Handles session state change events.
    /// </summary>
    /// <param name="sender">The session that raised the event</param>
    /// <param name="e">Event arguments containing state change details</param>
    /// <param name="logAction">Action to log lifecycle events</param>
    public static void HandleSessionStateChanged(
        object? sender,
        SessionStateChangedEventArgs e,
        Action<string, Exception?> logAction)
    {
        if (sender is TerminalSession session)
        {
            // Log session state changes for debugging and monitoring
            logAction($"Session {session.Id} state changed to {e.NewState}", e.Error);

            // Handle failed session states
            if (e.NewState == SessionState.Failed && e.Error != null)
            {
                logAction($"Session {session.Id} failed during operation", e.Error);

                // Attempt graceful cleanup of failed session resources
                try
                {
                    // Don't dispose immediately - let the session remain for potential restart
                    // Just ensure process resources are cleaned up
                    if (session.ProcessManager.IsRunning)
                    {
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await session.ProcessManager.StopAsync();
                            }
                            catch (Exception cleanupEx)
                            {
                                logAction($"Error cleaning up failed session {session.Id} process", cleanupEx);
                            }
                        });
                    }
                }
                catch (Exception cleanupEx)
                {
                    logAction($"Error during failed session {session.Id} cleanup", cleanupEx);
                }
            }
        }
    }

    /// <summary>
    ///     Handles session title change events.
    /// </summary>
    /// <param name="sender">The session that raised the event</param>
    /// <param name="e">Event arguments containing title change details</param>
    /// <param name="logAction">Action to log lifecycle events</param>
    public static void HandleSessionTitleChanged(
        object? sender,
        SessionTitleChangedEventArgs e,
        Action<string, Exception?> logAction)
    {
        if (sender is TerminalSession session)
        {
            logAction($"Session {session.Id} title changed from '{e.OldTitle}' to '{e.NewTitle}'", null);
        }
    }

    /// <summary>
    ///     Handles session process exit events.
    /// </summary>
    /// <param name="sender">The session that raised the event</param>
    /// <param name="e">Event arguments containing process exit details</param>
    /// <param name="logAction">Action to log lifecycle events</param>
    public static void HandleSessionProcessExited(
        object? sender,
        SessionProcessExitedEventArgs e,
        Action<string, Exception?> logAction)
    {
        if (sender is TerminalSession session)
        {
            logAction($"Session {session.Id} process {e.ProcessId} exited with code {e.ExitCode}", null);

            // Update session state to reflect process exit
            // The session itself handles updating its settings with exit code
            // We just need to ensure the session state is properly managed

            // If the session is still active but process exited, mark it as inactive
            // This allows the user to see the exit status while keeping the session available
            if (session.State == SessionState.Active)
            {
                // Don't change to inactive automatically - let user see the exit status
                // The session will remain active but with a terminated process
            }
        }
    }
}
