namespace caTTY.Core.Terminal;

/// <summary>
///     Handles session restart logic for terminated processes.
/// </summary>
internal class SessionRestarter
{
    /// <summary>
    ///     Represents the state needed for restarting a session.
    /// </summary>
    public class RestartSessionState
    {
        public TerminalSession Session { get; set; } = null!;
    }

    /// <summary>
    ///     Validates that a session can be restarted (must be called within lock).
    /// </summary>
    /// <param name="sessionId">ID of the session to restart</param>
    /// <param name="sessions">Dictionary of all sessions</param>
    /// <returns>State needed for restart operation</returns>
    /// <exception cref="ArgumentException">Thrown if session ID is not found</exception>
    /// <exception cref="InvalidOperationException">Thrown if session is not in a restartable state</exception>
    public static RestartSessionState PrepareRestart(
        Guid sessionId,
        Dictionary<Guid, TerminalSession> sessions)
    {
        if (!sessions.TryGetValue(sessionId, out var session))
        {
            throw new ArgumentException($"Session {sessionId} not found", nameof(sessionId));
        }

        // Can only restart sessions that have terminated processes
        if (session.ProcessManager.IsRunning)
        {
            throw new InvalidOperationException("Cannot restart a session with a running process");
        }

        return new RestartSessionState
        {
            Session = session
        };
    }

    /// <summary>
    ///     Performs the async restart operation (must be called outside lock).
    /// </summary>
    /// <param name="sessionId">ID of the session being restarted</param>
    /// <param name="state">State from PrepareRestart</param>
    /// <param name="launchOptions">Launch options for the new process</param>
    /// <param name="logAction">Action to log lifecycle events</param>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <exception cref="InvalidOperationException">Thrown if restart fails</exception>
    public static async Task PerformRestartAsync(
        Guid sessionId,
        RestartSessionState state,
        ProcessLaunchOptions launchOptions,
        Action<string, Exception?> logAction,
        CancellationToken cancellationToken)
    {
        var session = state.Session;

        try
        {
            logAction($"Restarting session {sessionId}", null);

            // Clear the terminal screen for the restart
            session.Terminal.ScreenBuffer.Clear();

            // Start a new process with the same or provided launch options
            await session.ProcessManager.StartAsync(launchOptions, cancellationToken);

            // Update session state and settings
            session.UpdateProcessState(session.ProcessManager.ProcessId, session.ProcessManager.IsRunning);

            logAction($"Successfully restarted session {sessionId}", null);

            // If this session was not active, we don't need to change its state
            // The session will remain in its current state (Active/Inactive)
        }
        catch (Exception ex)
        {
            logAction($"Failed to restart session {sessionId}", ex);

            // Update session settings to reflect restart failure
            session.UpdateProcessState(null, false, null);
            throw new InvalidOperationException($"Failed to restart session {sessionId}: {ex.Message}", ex);
        }
    }
}
