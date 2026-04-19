namespace caTTY.Core.Terminal;

/// <summary>
///     Handles session closing and cleanup logic.
/// </summary>
internal class SessionCloser
{
    /// <summary>
    ///     Represents the state needed for async cleanup after removing a session.
    /// </summary>
    public class CloseSessionState
    {
        public TerminalSession? SessionToClose { get; set; }
        public TerminalSession? NewActiveSession { get; set; }
    }

    /// <summary>
    ///     Prepares a session for closing by removing it from collections (must be called within lock).
    /// </summary>
    /// <param name="sessionId">ID of the session to close</param>
    /// <param name="sessions">Dictionary of all sessions</param>
    /// <param name="sessionOrder">List of session IDs in tab order</param>
    /// <param name="activeSessionId">Current active session ID (will be updated)</param>
    /// <returns>State needed for async cleanup, or null if session doesn't exist</returns>
    /// <exception cref="InvalidOperationException">Thrown if trying to close the last session</exception>
    public static CloseSessionState? PrepareClose(
        Guid sessionId,
        Dictionary<Guid, TerminalSession> sessions,
        List<Guid> sessionOrder,
        ref Guid? activeSessionId)
    {
        if (!sessions.TryGetValue(sessionId, out var sessionToClose))
        {
            return null; // Session doesn't exist
        }

        // Prevent closing the last session
        if (sessions.Count == 1)
        {
            throw new InvalidOperationException("Cannot close the last remaining session");
        }

        sessions.Remove(sessionId);
        sessionOrder.Remove(sessionId);

        TerminalSession? newActiveSession = null;

        // If this was the active session, find a new active session
        if (activeSessionId == sessionId)
        {
            activeSessionId = null;

            // Find the next session in order, or the previous one
            var remainingIds = sessionOrder.Where(id => sessions.ContainsKey(id)).ToList();
            if (remainingIds.Any())
            {
                var newActiveId = remainingIds.First();
                activeSessionId = newActiveId;
                newActiveSession = sessions[newActiveId];
            }
        }

        return new CloseSessionState
        {
            SessionToClose = sessionToClose,
            NewActiveSession = newActiveSession
        };
    }

    /// <summary>
    ///     Performs async cleanup after session has been removed from collections (must be called outside lock).
    /// </summary>
    /// <param name="sessionId">ID of the session being closed</param>
    /// <param name="state">State from PrepareClose</param>
    /// <param name="onSessionStateChanged">Event handler for session state changes</param>
    /// <param name="onSessionTitleChanged">Event handler for session title changes</param>
    /// <param name="onSessionProcessExited">Event handler for process exit</param>
    /// <param name="sessionClosedEvent">Event to raise when session is closed</param>
    /// <param name="activeSessionChangedEvent">Event to raise when active session changes</param>
    /// <param name="logAction">Action to log lifecycle events</param>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    public static async Task PerformCleanupAsync(
        Guid sessionId,
        CloseSessionState state,
        EventHandler<SessionStateChangedEventArgs> onSessionStateChanged,
        EventHandler<SessionTitleChangedEventArgs> onSessionTitleChanged,
        EventHandler<SessionProcessExitedEventArgs> onSessionProcessExited,
        EventHandler<SessionClosedEventArgs>? sessionClosedEvent,
        EventHandler<ActiveSessionChangedEventArgs>? activeSessionChangedEvent,
        Action<string, Exception?> logAction,
        CancellationToken cancellationToken)
    {
        var sessionToClose = state.SessionToClose;
        var newActiveSession = state.NewActiveSession;

        try
        {
            logAction($"Closing session {sessionId}", null);

            // Unsubscribe from events
            sessionToClose!.StateChanged -= onSessionStateChanged;
            sessionToClose.TitleChanged -= onSessionTitleChanged;
            sessionToClose.ProcessExited -= onSessionProcessExited;

            await sessionToClose.CloseAsync(cancellationToken);

            logAction($"Successfully closed session {sessionId}", null);
            sessionClosedEvent?.Invoke(null, new SessionClosedEventArgs(sessionToClose));

            if (newActiveSession != null)
            {
                newActiveSession.Activate();
                logAction($"Activated session {newActiveSession.Id} after closing {sessionId}", null);
                activeSessionChangedEvent?.Invoke(null, new ActiveSessionChangedEventArgs(sessionToClose, newActiveSession));
            }
        }
        catch (Exception ex)
        {
            logAction($"Error closing session {sessionId}", ex);

            // Even if closing failed, the session has been removed from the manager
            // Still notify about the closure and active session change
            try
            {
                sessionClosedEvent?.Invoke(null, new SessionClosedEventArgs(sessionToClose!));

                if (newActiveSession != null)
                {
                    newActiveSession.Activate();
                    logAction($"Activated session {newActiveSession.Id} after failed close of {sessionId}", null);
                    activeSessionChangedEvent?.Invoke(null, new ActiveSessionChangedEventArgs(sessionToClose, newActiveSession));
                }
            }
            catch (Exception eventEx)
            {
                logAction($"Error raising events after failed session {sessionId} close", eventEx);
            }

            // Don't re-throw - session cleanup errors should not prevent operation
            // The session has been removed from the manager regardless
        }
    }
}
