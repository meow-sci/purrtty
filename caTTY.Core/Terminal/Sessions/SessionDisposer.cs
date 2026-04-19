namespace caTTY.Core.Terminal;

/// <summary>
///     Handles disposal of session manager resources.
/// </summary>
internal class SessionDisposer
{
    /// <summary>
    ///     Disposes all sessions in the manager and cleans up resources.
    /// </summary>
    /// <param name="sessions">Dictionary of sessions to dispose</param>
    /// <param name="sessionOrder">List of session IDs to clear</param>
    /// <param name="activeSessionId">Active session ID to clear</param>
    /// <param name="onSessionStateChanged">Event handler to unsubscribe</param>
    /// <param name="onSessionTitleChanged">Event handler to unsubscribe</param>
    /// <param name="onSessionProcessExited">Event handler to unsubscribe</param>
    /// <param name="logAction">Action to log disposal events</param>
    public static void DisposeAllSessions(
        Dictionary<Guid, TerminalSession> sessions,
        List<Guid> sessionOrder,
        ref Guid? activeSessionId,
        EventHandler<SessionStateChangedEventArgs> onSessionStateChanged,
        EventHandler<SessionTitleChangedEventArgs> onSessionTitleChanged,
        EventHandler<SessionProcessExitedEventArgs> onSessionProcessExited,
        Action<string, Exception?> logAction)
    {
        var sessionCount = sessions.Count;
        logAction($"Disposing {sessionCount} sessions", null);

        foreach (var session in sessions.Values)
        {
            try
            {
                logAction($"Disposing session {session.Id}", null);

                // Unsubscribe from events
                session.StateChanged -= onSessionStateChanged;
                session.TitleChanged -= onSessionTitleChanged;
                session.ProcessExited -= onSessionProcessExited;

                session.Dispose();
            }
            catch (Exception ex)
            {
                logAction($"Error disposing session {session.Id}", ex);
                // Continue with other sessions even if one fails
            }
        }

        sessions.Clear();
        sessionOrder.Clear();
        activeSessionId = null;
    }
}
