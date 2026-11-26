using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using purrTTY.Core.Terminal;

namespace PurrTTY.Terminal.Sessions;

/// <summary>
/// Manages the lifecycle of multiple <see cref="TerminalSession"/> instances:
/// creation, switching, closing, restart, and tab ordering. A clean,
/// self-contained replacement for the legacy emulator-based session manager.
/// </summary>
public sealed class SessionManager : IDisposable
{
    private readonly Dictionary<Guid, TerminalSession> _sessions = new();
    private readonly List<Guid> _sessionOrder = new();
    private readonly object _lock = new();
    private readonly int _maxSessions;
    private readonly ILogger _logger;

    private Guid? _activeSessionId;
    private ProcessLaunchOptions _defaultLaunchOptions;
    private int _lastCols = 80;
    private int _lastRows = 25;
    private bool _disposed;

    public SessionManager(
        int maxSessions = 20,
        ProcessLaunchOptions? defaultLaunchOptions = null,
        ILogger? logger = null)
    {
        if (maxSessions <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxSessions));
        }

        _maxSessions = maxSessions;
        _defaultLaunchOptions = defaultLaunchOptions ?? ProcessLaunchOptions.CreateDefault();
        _logger = logger ?? NullLogger.Instance;
    }

    public event EventHandler<SessionCreatedEventArgs>? SessionCreated;
    public event EventHandler<SessionClosedEventArgs>? SessionClosed;
    public event EventHandler<ActiveSessionChangedEventArgs>? ActiveSessionChanged;

    /// <summary>
    /// Invoked with each newly constructed session <b>before</b> it is
    /// initialized and published (added to the session list / made active).
    /// This is the safe place to push the theme and subscribe surface events:
    /// nothing else can be touching the unpublished surface yet, so the calls
    /// cannot race a tick-thread <c>BuildFrame</c>. (A <see cref="SessionCreated"/>
    /// subscriber runs after publication — possibly on a pool thread — and must
    /// not call into the surface.)
    /// </summary>
    public Action<TerminalSession>? SessionConfigurator { get; set; }

    public ProcessLaunchOptions DefaultLaunchOptions => _defaultLaunchOptions;

    public void UpdateDefaultLaunchOptions(ProcessLaunchOptions launchOptions)
    {
        ArgumentNullException.ThrowIfNull(launchOptions);
        _defaultLaunchOptions = launchOptions;
    }

    public (int cols, int rows) LastKnownTerminalDimensions
    {
        get { lock (_lock) { return (_lastCols, _lastRows); } }
    }

    public void UpdateLastKnownTerminalDimensions(int cols, int rows)
    {
        if (cols <= 0 || rows <= 0)
        {
            return;
        }

        lock (_lock)
        {
            _lastCols = cols;
            _lastRows = rows;
        }
    }

    public TerminalSession? ActiveSession
    {
        get
        {
            lock (_lock)
            {
                return _activeSessionId is { } id && _sessions.TryGetValue(id, out var session) ? session : null;
            }
        }
    }

    // Snapshot of the ordered session list, rebuilt only when sessions are
    // added/removed. The frontend reads Sessions every frame (tab bar, ticking
    // background tabs); allocating a fresh list per access was steady-state
    // render-path garbage.
    private TerminalSession[] _sessionsSnapshot = Array.Empty<TerminalSession>();
    private bool _sessionsSnapshotStale;

    public IReadOnlyList<TerminalSession> Sessions
    {
        get
        {
            lock (_lock)
            {
                if (_sessionsSnapshotStale)
                {
                    _sessionsSnapshot = _sessionOrder
                        .Where(_sessions.ContainsKey)
                        .Select(id => _sessions[id])
                        .ToArray();
                    _sessionsSnapshotStale = false;
                }

                return _sessionsSnapshot;
            }
        }
    }

    public int SessionCount
    {
        get { lock (_lock) { return _sessions.Count; } }
    }

    public async Task<TerminalSession> CreateSessionAsync(
        string? title = null,
        ProcessLaunchOptions? launchOptions = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        int cols, rows;
        string sessionTitle;
        lock (_lock)
        {
            if (_sessions.Count >= _maxSessions)
            {
                throw new InvalidOperationException($"Maximum number of sessions ({_maxSessions}) reached.");
            }

            cols = _lastCols;
            rows = _lastRows;
            sessionTitle = title ?? $"Terminal {_sessions.Count + 1}";
        }

        // Clone before stamping dimensions: stamping the shared default (or a
        // caller-cached) instance cross-contaminates later sessions in other
        // windows with this window's grid size.
        var effectiveOptions = (launchOptions ?? _defaultLaunchOptions).Clone();
        effectiveOptions.InitialWidth = cols;
        effectiveOptions.InitialHeight = rows;

        var sessionId = Guid.NewGuid();
        var session = TerminalSessionFactory.CreateSession(
            sessionId,
            sessionTitle,
            cols,
            rows,
            OnSessionStateChanged,
            OnSessionTitleChanged,
            OnSessionProcessExited,
            effectiveOptions,
            _logger);

        try
        {
            // Configure (theme push, surface event wiring) before the session is
            // published: after publication the tick thread may already be inside
            // BuildFrame on this surface, and the engine is single-threaded.
            SessionConfigurator?.Invoke(session);
            await session.InitializeAsync(effectiveOptions, cancellationToken);
        }
        catch
        {
            session.Dispose();
            throw;
        }

        TerminalSession? previousActive = null;
        bool disposedDuringInit = false;
        bool capExceededDuringInit = false;
        lock (_lock)
        {
            // Re-check disposal: shell spawn (WSL spin-up etc.) can take seconds
            // and the owning window may have closed in the meantime. Publishing
            // into a disposed manager would leak a live PTY process forever.
            if (_disposed)
            {
                disposedDuringInit = true;
            }
            else if (_sessions.Count >= _maxSessions)
            {
                // Two overlapping creates can both pass the entry cap check —
                // the lock is released across the slow spawn await — so the cap
                // must be re-validated at publication, like disposal above.
                capExceededDuringInit = true;
            }
            else
            {
                previousActive = ActiveSessionNoLock();
                _sessions[sessionId] = session;
                _sessionOrder.Add(sessionId);
                _activeSessionId = sessionId;
                _sessionsSnapshotStale = true;
            }
        }

        if (disposedDuringInit || capExceededDuringInit)
        {
            // Never published, so nothing else can be touching the surface.
            session.Dispose();
            throw disposedDuringInit
                ? new ObjectDisposedException(nameof(SessionManager))
                : new InvalidOperationException($"Maximum number of sessions ({_maxSessions}) reached.");
        }

        // State transitions raise StateChanged; keep them outside _lock.
        previousActive?.Deactivate();

        _logger.LogDebug("Created session {SessionId} '{Title}'", sessionId, sessionTitle);
        SessionCreated?.Invoke(this, new SessionCreatedEventArgs(session));
        ActiveSessionChanged?.Invoke(this, new ActiveSessionChangedEventArgs(previousActive, session));
        return session;
    }

    public void SwitchToSession(Guid sessionId)
    {
        ThrowIfDisposed();
        TerminalSession? previous, next;
        lock (_lock)
        {
            if (!_sessions.TryGetValue(sessionId, out next) || _activeSessionId == sessionId)
            {
                return;
            }

            previous = ActiveSessionNoLock();
            _activeSessionId = sessionId;
        }

        // Activate/Deactivate raise StateChanged; raising under _lock invites
        // re-entrancy deadlocks if a handler calls back into the manager.
        previous?.Deactivate();
        next.Activate();
        ActiveSessionChanged?.Invoke(this, new ActiveSessionChangedEventArgs(previous, next));
    }

    public async Task CloseSessionAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        TerminalSession? session;
        TerminalSession? newActive = null;
        bool activeChanged = false;
        TerminalSession? previousActive = null;

        lock (_lock)
        {
            if (!_sessions.TryGetValue(sessionId, out session))
            {
                return;
            }

            _sessions.Remove(sessionId);
            _sessionOrder.Remove(sessionId);
            _sessionsSnapshotStale = true;

            if (_activeSessionId == sessionId)
            {
                previousActive = session;
                _activeSessionId = _sessionOrder.Count > 0 ? _sessionOrder[^1] : null;
                newActive = _activeSessionId is { } id ? _sessions[id] : null;
                activeChanged = true;
            }
        }

        // Raises StateChanged; keep outside _lock.
        newActive?.Activate();

        await session.CloseAsync(cancellationToken);

        SessionClosed?.Invoke(this, new SessionClosedEventArgs(session));
        if (activeChanged)
        {
            ActiveSessionChanged?.Invoke(this, new ActiveSessionChangedEventArgs(previousActive, newActive));
        }
    }

    /// <summary>
    /// Closes and removes every published session that was launched with
    /// <see cref="ProcessLaunchOptions.CloseOnExit"/> and whose process has exited
    /// (<see cref="TerminalSession.HasProcessExited"/>), or with
    /// <see cref="ProcessLaunchOptions.CloseOnSuccessfulExit"/> and whose process exited
    /// with code 0 (<see cref="TerminalSession.ShouldCloseAfterExit"/>). Returns the ids closed (an
    /// empty array when none — the common case, allocation-free), so a frontend
    /// can retire a window/instance left with no sessions.
    ///
    /// <para>
    /// MUST be called on the tick thread, outside any surface iteration: closing
    /// disposes the native surface synchronously (gotcha 19). This is how an exit
    /// — raised on a PTY pump thread — reaches UI state: the latch is set there and
    /// polled here, so no cross-thread marshalling is needed. Before each close the
    /// surface is ticked once (<c>BuildFrame</c>): the latch is only set after the
    /// backend drained the tail into the inbox, so this applies the program's final
    /// output and its side effects (title, an OSC 52 clipboard write such as a yank
    /// just before <c>:q</c>) before the surface goes away. Sessions without the
    /// option are never touched: they keep their last screen and exit code.
    /// </para>
    /// </summary>
    public IReadOnlyList<Guid> CloseExitedSessions()
    {
        if (_disposed)
        {
            return Array.Empty<Guid>();
        }

        var sessions = Sessions;
        List<Guid>? closed = null;
        for (int i = 0; i < sessions.Count; i++)
        {
            var session = sessions[i];
            if (!session.ShouldCloseAfterExit)
            {
                continue;
            }

            session.Surface.BuildFrame();
            _logger.LogDebug(
                "Closing session {SessionId}: process exited with code {ExitCode} (close on exit)",
                session.Id, session.ExitCode);

            // Completes synchronously (TerminalSession.CloseAsync), on this thread.
            CloseSessionAsync(session.Id).GetAwaiter().GetResult();
            (closed ??= new List<Guid>()).Add(session.Id);
        }

        return closed is null ? Array.Empty<Guid>() : closed;
    }

    /// <summary>
    /// Restarts the shell process for a session that has exited, reusing the
    /// surface. Restart support depends on the underlying process manager.
    /// </summary>
    public async Task RestartSessionAsync(
        Guid sessionId,
        ProcessLaunchOptions? launchOptions = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        TerminalSession? session;
        lock (_lock)
        {
            _sessions.TryGetValue(sessionId, out session);
        }

        if (session is null)
        {
            throw new ArgumentException($"Session {sessionId} not found.", nameof(sessionId));
        }

        // Clone before stamping dimensions (see CreateSessionAsync).
        var effective = (launchOptions ?? _defaultLaunchOptions).Clone();
        effective.InitialWidth = session.Surface.Cols;
        effective.InitialHeight = session.Surface.Rows;

        if (session.ProcessManager.IsRunning)
        {
            await session.ProcessManager.StopAsync(cancellationToken);
        }

        // Before the start: a new process that exits at once must re-arm the latch.
        // (CloseOnExit keeps following the session's original launch options.)
        session.ResetProcessExited();
        await session.ProcessManager.StartAsync(effective, cancellationToken);
    }

    private TerminalSession? ActiveSessionNoLock()
        => _activeSessionId is { } id && _sessions.TryGetValue(id, out var s) ? s : null;

    private void OnSessionStateChanged(object? sender, SessionStateChangedEventArgs e)
        => _logger.LogDebug("Session state changed to {State}", e.NewState);

    private void OnSessionTitleChanged(object? sender, SessionTitleChangedEventArgs e)
        => _logger.LogDebug("Session title changed to '{Title}'", e.NewTitle);

    private void OnSessionProcessExited(object? sender, SessionProcessExitedEventArgs e)
        => _logger.LogDebug("Session process {ProcessId} exited with code {ExitCode}", e.ProcessId, e.ExitCode);

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        List<TerminalSession> toDispose;
        lock (_lock)
        {
            toDispose = _sessions.Values.ToList();
            _sessions.Clear();
            _sessionOrder.Clear();
            _activeSessionId = null;
            _sessionsSnapshot = Array.Empty<TerminalSession>();
            _sessionsSnapshotStale = false;
        }

        foreach (var session in toDispose)
        {
            session.Dispose();
        }
    }
}
