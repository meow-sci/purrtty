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

    public IReadOnlyList<TerminalSession> Sessions
    {
        get
        {
            lock (_lock)
            {
                return _sessionOrder.Where(_sessions.ContainsKey).Select(id => _sessions[id]).ToList();
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

        var effectiveOptions = launchOptions ?? _defaultLaunchOptions;
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
            await session.InitializeAsync(effectiveOptions, cancellationToken);
        }
        catch
        {
            session.Dispose();
            throw;
        }

        TerminalSession? previousActive;
        lock (_lock)
        {
            previousActive = ActiveSessionNoLock();
            _sessions[sessionId] = session;
            _sessionOrder.Add(sessionId);
            _activeSessionId = sessionId;
        }

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
            previous?.Deactivate();
            _activeSessionId = sessionId;
            next.Activate();
        }

        ActiveSessionChanged?.Invoke(this, new ActiveSessionChangedEventArgs(previous, next));
    }

    public void SwitchToNextSession() => SwitchRelative(1);

    public void SwitchToPreviousSession() => SwitchRelative(-1);

    private void SwitchRelative(int direction)
    {
        ThrowIfDisposed();
        Guid target;
        lock (_lock)
        {
            if (_sessionOrder.Count <= 1 || _activeSessionId is not { } current)
            {
                return;
            }

            int index = _sessionOrder.IndexOf(current);
            if (index < 0)
            {
                return;
            }

            int nextIndex = (index + direction + _sessionOrder.Count) % _sessionOrder.Count;
            target = _sessionOrder[nextIndex];
        }

        SwitchToSession(target);
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

            if (_activeSessionId == sessionId)
            {
                previousActive = session;
                _activeSessionId = _sessionOrder.Count > 0 ? _sessionOrder[^1] : null;
                newActive = _activeSessionId is { } id ? _sessions[id] : null;
                newActive?.Activate();
                activeChanged = true;
            }
        }

        await session.CloseAsync(cancellationToken);

        SessionClosed?.Invoke(this, new SessionClosedEventArgs(session));
        if (activeChanged)
        {
            ActiveSessionChanged?.Invoke(this, new ActiveSessionChangedEventArgs(previousActive, newActive));
        }
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

        var effective = launchOptions ?? _defaultLaunchOptions;
        effective.InitialWidth = session.Surface.Cols;
        effective.InitialHeight = session.Surface.Rows;

        if (session.ProcessManager.IsRunning)
        {
            await session.ProcessManager.StopAsync(cancellationToken);
        }

        await session.ProcessManager.StartAsync(effective, cancellationToken);
    }

    /// <summary>Kept for API compatibility; font config is applied at the display layer.</summary>
    public void ApplyFontConfigToAllSessions(object fontConfig)
    {
        ArgumentNullException.ThrowIfNull(fontConfig);
        ThrowIfDisposed();
    }

    /// <summary>Kept for API compatibility; resize is driven by the display layer.</summary>
    public void TriggerTerminalResizeForAllSessions(float newCharacterWidth, float newLineHeight, (float width, float height) windowSize)
    {
        ThrowIfDisposed();
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
        }

        foreach (var session in toDispose)
        {
            session.Dispose();
        }
    }
}
