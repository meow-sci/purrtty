namespace PurrTTY.Terminal.Sessions;

/// <summary>Raised when a new session is created.</summary>
public sealed class SessionCreatedEventArgs : EventArgs
{
    public SessionCreatedEventArgs(TerminalSession session)
    {
        Session = session ?? throw new ArgumentNullException(nameof(session));
        Timestamp = DateTime.UtcNow;
    }

    public TerminalSession Session { get; }
    public DateTime Timestamp { get; }
}

/// <summary>Raised when a session is closed.</summary>
public sealed class SessionClosedEventArgs : EventArgs
{
    public SessionClosedEventArgs(TerminalSession session)
    {
        Session = session ?? throw new ArgumentNullException(nameof(session));
        Timestamp = DateTime.UtcNow;
    }

    public TerminalSession Session { get; }
    public DateTime Timestamp { get; }
}

/// <summary>Raised when the active session changes.</summary>
public sealed class ActiveSessionChangedEventArgs : EventArgs
{
    public ActiveSessionChangedEventArgs(TerminalSession? previousSession, TerminalSession? newSession)
    {
        PreviousSession = previousSession;
        NewSession = newSession;
        Timestamp = DateTime.UtcNow;
    }

    public TerminalSession? PreviousSession { get; }
    public TerminalSession? NewSession { get; }
    public DateTime Timestamp { get; }
}
