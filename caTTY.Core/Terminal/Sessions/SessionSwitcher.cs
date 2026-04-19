namespace caTTY.Core.Terminal;

/// <summary>
///     Handles session switching logic for tab navigation.
/// </summary>
internal class SessionSwitcher
{
    /// <summary>
    ///     Switches to the specified session, making it active.
    /// </summary>
    /// <param name="sessionId">ID of the session to activate</param>
    /// <param name="sessions">Dictionary of all sessions</param>
    /// <param name="activeSessionId">Current active session ID (will be updated)</param>
    /// <param name="onActiveSessionChanged">Event to raise when active session changes</param>
    /// <exception cref="ArgumentException">Thrown if session ID is not found</exception>
    public static void SwitchToSession(
        Guid sessionId,
        Dictionary<Guid, TerminalSession> sessions,
        ref Guid? activeSessionId,
        EventHandler<ActiveSessionChangedEventArgs>? onActiveSessionChanged)
    {
        if (!sessions.TryGetValue(sessionId, out var targetSession))
        {
            throw new ArgumentException($"Session {sessionId} not found", nameof(sessionId));
        }

        if (activeSessionId == sessionId)
        {
            return; // Already active
        }

        var previousSession = activeSessionId.HasValue && sessions.TryGetValue(activeSessionId.Value, out var prev)
            ? prev
            : null;

        // Deactivate current session
        previousSession?.Deactivate();

        // Activate target session
        activeSessionId = sessionId;
        targetSession.Activate();

        onActiveSessionChanged?.Invoke(null, new ActiveSessionChangedEventArgs(previousSession, targetSession));
    }

    /// <summary>
    ///     Switches to the next session in tab order.
    /// </summary>
    /// <param name="sessions">Dictionary of all sessions</param>
    /// <param name="sessionOrder">List of session IDs in tab order</param>
    /// <param name="activeSessionId">Current active session ID (will be updated)</param>
    /// <param name="onActiveSessionChanged">Event to raise when active session changes</param>
    public static void SwitchToNextSession(
        Dictionary<Guid, TerminalSession> sessions,
        List<Guid> sessionOrder,
        ref Guid? activeSessionId,
        EventHandler<ActiveSessionChangedEventArgs>? onActiveSessionChanged)
    {
        if (sessions.Count <= 1 || !activeSessionId.HasValue)
        {
            return;
        }

        var currentIndex = sessionOrder.IndexOf(activeSessionId.Value);
        var nextIndex = (currentIndex + 1) % sessionOrder.Count;
        var nextSessionId = sessionOrder[nextIndex];

        SwitchToSession(nextSessionId, sessions, ref activeSessionId, onActiveSessionChanged);
    }

    /// <summary>
    ///     Switches to the previous session in tab order.
    /// </summary>
    /// <param name="sessions">Dictionary of all sessions</param>
    /// <param name="sessionOrder">List of session IDs in tab order</param>
    /// <param name="activeSessionId">Current active session ID (will be updated)</param>
    /// <param name="onActiveSessionChanged">Event to raise when active session changes</param>
    public static void SwitchToPreviousSession(
        Dictionary<Guid, TerminalSession> sessions,
        List<Guid> sessionOrder,
        ref Guid? activeSessionId,
        EventHandler<ActiveSessionChangedEventArgs>? onActiveSessionChanged)
    {
        if (sessions.Count <= 1 || !activeSessionId.HasValue)
        {
            return;
        }

        var currentIndex = sessionOrder.IndexOf(activeSessionId.Value);
        var prevIndex = currentIndex == 0 ? sessionOrder.Count - 1 : currentIndex - 1;
        var prevSessionId = sessionOrder[prevIndex];

        SwitchToSession(prevSessionId, sessions, ref activeSessionId, onActiveSessionChanged);
    }
}
