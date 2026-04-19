namespace caTTY.Core.Terminal;

/// <summary>
///     Represents the current state of a terminal session.
/// </summary>
public enum SessionState
{
    /// <summary>
    ///     Session is being created and initialized.
    /// </summary>
    Creating,

    /// <summary>
    ///     Session is active and can receive input.
    /// </summary>
    Active,

    /// <summary>
    ///     Session exists but is not currently active.
    /// </summary>
    Inactive,

    /// <summary>
    ///     Session is being terminated and cleaned up.
    /// </summary>
    Terminating,

    /// <summary>
    ///     Session creation or operation failed.
    /// </summary>
    Failed,

    /// <summary>
    ///     Session has been disposed and resources cleaned up.
    /// </summary>
    Disposed
}

/// <summary>
///     Event arguments for session creation notifications.
/// </summary>
public class SessionCreatedEventArgs : EventArgs
{
    /// <summary>
    ///     Creates new session created event arguments.
    /// </summary>
    /// <param name="session">The session that was created</param>
    public SessionCreatedEventArgs(TerminalSession session)
    {
        Session = session ?? throw new ArgumentNullException(nameof(session));
        Timestamp = DateTime.UtcNow;
    }

    /// <summary>
    ///     Gets the session that was created.
    /// </summary>
    public TerminalSession Session { get; }

    /// <summary>
    ///     Gets the time when the session was created.
    /// </summary>
    public DateTime Timestamp { get; }
}

/// <summary>
///     Event arguments for session closure notifications.
/// </summary>
public class SessionClosedEventArgs : EventArgs
{
    /// <summary>
    ///     Creates new session closed event arguments.
    /// </summary>
    /// <param name="session">The session that was closed</param>
    public SessionClosedEventArgs(TerminalSession session)
    {
        Session = session ?? throw new ArgumentNullException(nameof(session));
        Timestamp = DateTime.UtcNow;
    }

    /// <summary>
    ///     Gets the session that was closed.
    /// </summary>
    public TerminalSession Session { get; }

    /// <summary>
    ///     Gets the time when the session was closed.
    /// </summary>
    public DateTime Timestamp { get; }
}

/// <summary>
///     Event arguments for active session change notifications.
/// </summary>
public class ActiveSessionChangedEventArgs : EventArgs
{
    /// <summary>
    ///     Creates new active session changed event arguments.
    /// </summary>
    /// <param name="previousSession">The previously active session, or null if none</param>
    /// <param name="newSession">The newly active session, or null if none</param>
    public ActiveSessionChangedEventArgs(TerminalSession? previousSession, TerminalSession? newSession)
    {
        PreviousSession = previousSession;
        NewSession = newSession;
        Timestamp = DateTime.UtcNow;
    }

    /// <summary>
    ///     Gets the previously active session, or null if there was none.
    /// </summary>
    public TerminalSession? PreviousSession { get; }

    /// <summary>
    ///     Gets the newly active session, or null if there is none.
    /// </summary>
    public TerminalSession? NewSession { get; }

    /// <summary>
    ///     Gets the time when the active session changed.
    /// </summary>
    public DateTime Timestamp { get; }
}

/// <summary>
///     Event arguments for session state change notifications.
/// </summary>
public class SessionStateChangedEventArgs : EventArgs
{
    /// <summary>
    ///     Creates new session state changed event arguments.
    /// </summary>
    /// <param name="newState">The new session state</param>
    /// <param name="error">The error that caused the state change, if any</param>
    public SessionStateChangedEventArgs(SessionState newState, Exception? error = null)
    {
        NewState = newState;
        Error = error;
        Timestamp = DateTime.UtcNow;
    }

    /// <summary>
    ///     Gets the new session state.
    /// </summary>
    public SessionState NewState { get; }

    /// <summary>
    ///     Gets the error that caused the state change, if any.
    /// </summary>
    public Exception? Error { get; }

    /// <summary>
    ///     Gets the time when the state changed.
    /// </summary>
    public DateTime Timestamp { get; }
}

/// <summary>
///     Event arguments for session title change notifications.
/// </summary>
public class SessionTitleChangedEventArgs : EventArgs
{
    /// <summary>
    ///     Creates new session title changed event arguments.
    /// </summary>
    /// <param name="oldTitle">The previous title</param>
    /// <param name="newTitle">The new title</param>
    public SessionTitleChangedEventArgs(string oldTitle, string newTitle)
    {
        OldTitle = oldTitle ?? string.Empty;
        NewTitle = newTitle ?? string.Empty;
        Timestamp = DateTime.UtcNow;
    }

    /// <summary>
    ///     Gets the previous title.
    /// </summary>
    public string OldTitle { get; }

    /// <summary>
    ///     Gets the new title.
    /// </summary>
    public string NewTitle { get; }

    /// <summary>
    ///     Gets the time when the title changed.
    /// </summary>
    public DateTime Timestamp { get; }
}

/// <summary>
///     Event arguments for session process exit notifications.
/// </summary>
public class SessionProcessExitedEventArgs : EventArgs
{
    /// <summary>
    ///     Creates new session process exited event arguments.
    /// </summary>
    /// <param name="exitCode">The process exit code</param>
    /// <param name="processId">The process ID that exited</param>
    public SessionProcessExitedEventArgs(int exitCode, int processId)
    {
        ExitCode = exitCode;
        ProcessId = processId;
        Timestamp = DateTime.UtcNow;
    }

    /// <summary>
    ///     Gets the process exit code.
    /// </summary>
    public int ExitCode { get; }

    /// <summary>
    ///     Gets the process ID that exited.
    /// </summary>
    public int ProcessId { get; }

    /// <summary>
    ///     Gets the time when the process exited.
    /// </summary>
    public DateTime Timestamp { get; }
}