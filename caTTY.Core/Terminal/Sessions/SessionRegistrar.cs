namespace caTTY.Core.Terminal;

/// <summary>
///     Handles session registration and activation logic.
/// </summary>
internal class SessionRegistrar
{
    /// <summary>
    ///     Represents the state needed for session registration.
    /// </summary>
    public class RegistrationState
    {
        public TerminalSession? PreviousActiveSession { get; set; }
    }

    /// <summary>
    ///     Registers a new session and activates it (must be called within lock).
    /// </summary>
    /// <param name="sessionId">ID of the new session</param>
    /// <param name="session">The session to register</param>
    /// <param name="sessions">Dictionary of all sessions</param>
    /// <param name="sessionOrder">List of session IDs in tab order</param>
    /// <param name="activeSessionId">Current active session ID (will be updated)</param>
    /// <returns>State containing the previous active session</returns>
    public static RegistrationState RegisterSession(
        Guid sessionId,
        TerminalSession session,
        Dictionary<Guid, TerminalSession> sessions,
        List<Guid> sessionOrder,
        ref Guid? activeSessionId)
    {
        sessions[sessionId] = session;
        sessionOrder.Add(sessionId);

        TerminalSession? previousActiveSession = null;

        // Deactivate current session
        if (activeSessionId.HasValue && sessions.TryGetValue(activeSessionId.Value, out var currentSession))
        {
            previousActiveSession = currentSession;
            currentSession.Deactivate();
        }

        activeSessionId = sessionId;

        return new RegistrationState
        {
            PreviousActiveSession = previousActiveSession
        };
    }

    /// <summary>
    ///     Handles cleanup after session creation failure (must be called within lock).
    /// </summary>
    /// <param name="sessionId">ID of the failed session</param>
    /// <param name="sessions">Dictionary of all sessions</param>
    /// <param name="sessionOrder">List of session IDs in tab order</param>
    /// <param name="activeSessionId">Current active session ID (will be updated)</param>
    /// <param name="previousActiveSession">Previous active session to restore</param>
    /// <param name="logAction">Action to log lifecycle events</param>
    public static void HandleCreationFailure(
        Guid sessionId,
        Dictionary<Guid, TerminalSession> sessions,
        List<Guid> sessionOrder,
        ref Guid? activeSessionId,
        TerminalSession? previousActiveSession,
        Action<string, Exception?> logAction)
    {
        sessions.Remove(sessionId);
        sessionOrder.Remove(sessionId);
        if (activeSessionId == sessionId)
        {
            activeSessionId = null;
            // Restore previous active session if it exists
            if (previousActiveSession != null)
            {
                activeSessionId = previousActiveSession.Id;
                previousActiveSession.Activate();
                logAction($"Restored previous active session {previousActiveSession.Id} after creation failure", null);
            }
        }
    }
}
