using System.Collections.Concurrent;
using caTTY.Core.Rpc;
using caTTY.Core.Rpc.Socket;
using Microsoft.Extensions.Logging.Abstractions;

namespace caTTY.Core.Terminal;

/// <summary>
///     Manages multiple terminal sessions and coordinates their lifecycle.
///     Provides session creation, switching, and cleanup functionality.
/// </summary>
public class SessionManager : IDisposable
{
    private readonly Dictionary<Guid, TerminalSession> _sessions = new();
    private readonly List<Guid> _sessionOrder = new(); // For tab ordering
    private Guid? _activeSessionId;
    private readonly object _lock = new();
    private bool _disposed = false;

    // Configuration
    private readonly int _maxSessions;
    private readonly SessionDimensionTracker _dimensionTracker;
    private readonly IRpcHandler? _rpcHandler;
    private readonly IOscRpcHandler? _oscRpcHandler;

    /// <summary>
    ///     Creates a new session manager with the specified configuration.
    /// </summary>
    /// <param name="maxSessions">Maximum number of concurrent sessions (default: 20)</param>
    /// <param name="defaultLaunchOptions">Default options for launching new sessions</param>
    /// <param name="rpcHandler">Optional RPC handler for CSI RPC commands (null disables CSI RPC)</param>
    /// <param name="oscRpcHandler">Optional OSC RPC handler for OSC-based RPC commands (null disables OSC RPC)</param>
    public SessionManager(
        int maxSessions = 20,
        ProcessLaunchOptions? defaultLaunchOptions = null,
        IRpcHandler? rpcHandler = null,
        IOscRpcHandler? oscRpcHandler = null)
    {
        SessionValidator.ValidateMaxSessions(maxSessions);

        _maxSessions = maxSessions;
        _dimensionTracker = new SessionDimensionTracker(defaultLaunchOptions ?? ProcessLaunchOptions.CreateDefault());
        _rpcHandler = rpcHandler;
        _oscRpcHandler = oscRpcHandler;
    }

    /// <summary>
    ///     Updates the default launch options for new sessions.
    /// </summary>
    /// <param name="launchOptions">New default launch options</param>
    public void UpdateDefaultLaunchOptions(ProcessLaunchOptions launchOptions)
    {
        _dimensionTracker.UpdateDefaultLaunchOptions(launchOptions);
    }

    /// <summary>
    ///     Gets the most recently known terminal dimensions (cols, rows).
    ///     Used to seed new sessions so they start at the current UI size instead of a fixed default.
    /// </summary>
    public (int cols, int rows) LastKnownTerminalDimensions => _dimensionTracker.LastKnownTerminalDimensions;

    /// <summary>
    ///     Updates the manager's notion of the current terminal dimensions.
    ///     This also updates the default launch options so newly created processes start at the latest size.
    /// </summary>
    /// <param name="cols">Terminal width in columns</param>
    /// <param name="rows">Terminal height in rows</param>
    public void UpdateLastKnownTerminalDimensions(int cols, int rows)
    {
        _dimensionTracker.UpdateLastKnownTerminalDimensions(cols, rows);
    }

    /// <summary>
    ///     Gets the current default launch options.
    /// </summary>
    public ProcessLaunchOptions DefaultLaunchOptions => _dimensionTracker.DefaultLaunchOptions;


    /// <summary>
    ///     Event raised when a new session is created.
    /// </summary>
    public event EventHandler<SessionCreatedEventArgs>? SessionCreated;

    /// <summary>
    ///     Event raised when a session is closed.
    /// </summary>
    public event EventHandler<SessionClosedEventArgs>? SessionClosed;

    /// <summary>
    ///     Event raised when the active session changes.
    /// </summary>
    public event EventHandler<ActiveSessionChangedEventArgs>? ActiveSessionChanged;

    /// <summary>
    ///     Gets the currently active session, or null if no sessions exist.
    /// </summary>
    public TerminalSession? ActiveSession
    {
        get
        {
            lock (_lock)
            {
                return _activeSessionId.HasValue && _sessions.TryGetValue(_activeSessionId.Value, out var session)
                    ? session
                    : null;
            }
        }
    }

    /// <summary>
    ///     Gets all sessions in tab order.
    /// </summary>
    public IReadOnlyList<TerminalSession> Sessions
    {
        get
        {
            lock (_lock)
            {
                return _sessionOrder
                    .Where(id => _sessions.ContainsKey(id))
                    .Select(id => _sessions[id])
                    .ToList();
            }
        }
    }

    /// <summary>
    ///     Gets the number of active sessions.
    /// </summary>
    public int SessionCount
    {
        get { lock (_lock) { return _sessions.Count; } }
    }

    /// <summary>
    ///     Creates a new terminal session and makes it active.
    /// </summary>
    /// <param name="title">Optional title for the session (auto-generated if null)</param>
    /// <param name="launchOptions">Optional launch options (uses default if null)</param>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>The newly created session</returns>
    /// <exception cref="InvalidOperationException">Thrown if maximum sessions reached</exception>
    /// <exception cref="ObjectDisposedException">Thrown if manager has been disposed</exception>
    public async Task<TerminalSession> CreateSessionAsync(string? title = null, ProcessLaunchOptions? launchOptions = null, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        lock (_lock)
        {
            SessionValidator.ValidateMaxSessionsNotReached(_sessions.Count, _maxSessions);
        }

        var sessionId = Guid.NewGuid();
        string sessionTitle;
        lock (_lock)
        {
            sessionTitle = title ?? SessionTitleGenerator.GenerateSessionTitle(_sessions.Count);
        }

        ProcessLaunchOptions effectiveLaunchOptions = LaunchOptionsPreparator.PrepareEffectiveLaunchOptions(
            launchOptions,
            _dimensionTracker);

        TerminalSession session;
        SessionRegistrar.RegistrationState? registrationState = null;

        try
        {
            // Create and initialize session
            session = await SessionCreator.CreateSessionAsync(
                sessionId,
                sessionTitle,
                effectiveLaunchOptions,
                OnSessionStateChanged,
                OnSessionTitleChanged,
                OnSessionProcessExited,
                _rpcHandler,
                _oscRpcHandler,
                cancellationToken);

            // Add session to manager and switch active session
            lock (_lock)
            {
                registrationState = SessionRegistrar.RegisterSession(
                    sessionId,
                    session,
                    _sessions,
                    _sessionOrder,
                    ref _activeSessionId);
            }

            LogSessionLifecycleEvent($"Successfully created session {sessionId} with title '{sessionTitle}'");

            SessionCreated?.Invoke(this, new SessionCreatedEventArgs(session));
            ActiveSessionChanged?.Invoke(this, new ActiveSessionChangedEventArgs(registrationState.PreviousActiveSession, session));

            return session;
        }
        catch (Exception ex)
        {
            LogSessionLifecycleEvent($"Failed to create session {sessionId} with title '{sessionTitle}'", ex);

            // Comprehensive cleanup on failure
            lock (_lock)
            {
                SessionRegistrar.HandleCreationFailure(
                    sessionId,
                    _sessions,
                    _sessionOrder,
                    ref _activeSessionId,
                    registrationState?.PreviousActiveSession,
                    LogSessionLifecycleEvent);
            }

            // Re-throw with more context for the caller
            throw new InvalidOperationException($"Failed to create session '{sessionTitle}': {ex.Message}", ex);
        }
    }

    /// <summary>
    ///     Switches to the specified session, making it active.
    /// </summary>
    /// <param name="sessionId">ID of the session to activate</param>
    /// <exception cref="ArgumentException">Thrown if session ID is not found</exception>
    /// <exception cref="ObjectDisposedException">Thrown if manager has been disposed</exception>
    public void SwitchToSession(Guid sessionId)
    {
        ThrowIfDisposed();

        lock (_lock)
        {
            SessionSwitcher.SwitchToSession(sessionId, _sessions, ref _activeSessionId, ActiveSessionChanged);
        }
    }

    /// <summary>
    ///     Closes the specified session and cleans up its resources.
    /// </summary>
    /// <param name="sessionId">ID of the session to close</param>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>A task that completes when the session is closed</returns>
    /// <exception cref="InvalidOperationException">Thrown if trying to close the last session</exception>
    /// <exception cref="ObjectDisposedException">Thrown if manager has been disposed</exception>
    public async Task CloseSessionAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        SessionCloser.CloseSessionState? state;

        lock (_lock)
        {
            state = SessionCloser.PrepareClose(sessionId, _sessions, _sessionOrder, ref _activeSessionId);
        }

        if (state != null)
        {
            await SessionCloser.PerformCleanupAsync(
                sessionId,
                state,
                OnSessionStateChanged,
                OnSessionTitleChanged,
                OnSessionProcessExited,
                SessionClosed,
                ActiveSessionChanged,
                LogSessionLifecycleEvent,
                cancellationToken);
        }
    }

    /// <summary>
    ///     Switches to the next session in tab order.
    /// </summary>
    /// <exception cref="ObjectDisposedException">Thrown if manager has been disposed</exception>
    public void SwitchToNextSession()
    {
        ThrowIfDisposed();

        lock (_lock)
        {
            SessionSwitcher.SwitchToNextSession(_sessions, _sessionOrder, ref _activeSessionId, ActiveSessionChanged);
        }
    }

    /// <summary>
    ///     Switches to the previous session in tab order.
    /// </summary>
    /// <exception cref="ObjectDisposedException">Thrown if manager has been disposed</exception>
    public void SwitchToPreviousSession()
    {
        ThrowIfDisposed();

        lock (_lock)
        {
            SessionSwitcher.SwitchToPreviousSession(_sessions, _sessionOrder, ref _activeSessionId, ActiveSessionChanged);
        }
    }

    /// <summary>
    ///     Restarts a terminated session by starting a new shell process.
    /// </summary>
    /// <param name="sessionId">ID of the session to restart</param>
    /// <param name="launchOptions">Optional launch options (uses default if null)</param>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>A task that completes when the session is restarted</returns>
    /// <exception cref="ArgumentException">Thrown if session ID is not found</exception>
    /// <exception cref="InvalidOperationException">Thrown if session is not in a restartable state</exception>
    /// <exception cref="ObjectDisposedException">Thrown if manager has been disposed</exception>
    public async Task RestartSessionAsync(Guid sessionId, ProcessLaunchOptions? launchOptions = null, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        SessionRestarter.RestartSessionState state;

        lock (_lock)
        {
            state = SessionRestarter.PrepareRestart(sessionId, _sessions);
        }

        await SessionRestarter.PerformRestartAsync(
            sessionId,
            state,
            launchOptions ?? _dimensionTracker.DefaultLaunchOptions,
            LogSessionLifecycleEvent,
            cancellationToken);
    }

    /// <summary>
    ///     Applies font configuration changes to all sessions simultaneously.
    ///     Font configuration is applied at the display layer (TerminalController).
    ///     This method serves as an API coordination point.
    /// </summary>
    /// <param name="fontConfig">The font configuration to apply to all sessions</param>
    /// <exception cref="ArgumentNullException">Thrown if fontConfig is null</exception>
    /// <exception cref="ObjectDisposedException">Thrown if manager has been disposed</exception>
    public void ApplyFontConfigToAllSessions(object fontConfig)
    {
        ArgumentNullException.ThrowIfNull(fontConfig);
        ThrowIfDisposed();
        // Font configuration is applied at the display layer (TerminalController)
    }

    /// <summary>
    ///     Triggers terminal resize for all sessions when font metrics change.
    ///     Kept for API compatibility. Actual resize logic is in TerminalController.
    /// </summary>
    /// <param name="newCharacterWidth">New character width in pixels</param>
    /// <param name="newLineHeight">New line height in pixels</param>
    /// <param name="windowSize">Current window size for dimension calculations</param>
    /// <exception cref="ObjectDisposedException">Thrown if manager has been disposed</exception>
    public void TriggerTerminalResizeForAllSessions(float newCharacterWidth, float newLineHeight, (float width, float height) windowSize)
    {
        ThrowIfDisposed();
        // Actual resize logic is handled by TerminalController
    }


    /// <summary>Handles session state change events.</summary>
    private void OnSessionStateChanged(object? sender, SessionStateChangedEventArgs e) =>
        SessionEventBridge.HandleSessionStateChanged(sender, e, LogSessionLifecycleEvent);

    /// <summary>Handles session title change events.</summary>
    private void OnSessionTitleChanged(object? sender, SessionTitleChangedEventArgs e) =>
        SessionEventBridge.HandleSessionTitleChanged(sender, e, LogSessionLifecycleEvent);

    /// <summary>Handles session process exit events.</summary>
    private void OnSessionProcessExited(object? sender, SessionProcessExitedEventArgs e) =>
        SessionEventBridge.HandleSessionProcessExited(sender, e, LogSessionLifecycleEvent);

    /// <summary>
    ///     Logs session lifecycle events for debugging and monitoring.
    /// </summary>
    private void LogSessionLifecycleEvent(string message, Exception? exception = null) =>
        SessionLogging.LogSessionLifecycleEvent(message, exception);

    /// <summary>
    ///     Determines if debug logging is enabled for session lifecycle events.
    /// </summary>
    private bool IsDebugLoggingEnabled() => SessionLogging.IsDebugLoggingEnabled();

    /// <summary>
    ///     Throws ObjectDisposedException if the manager has been disposed.
    /// </summary>
    private void ThrowIfDisposed() => SessionValidator.ThrowIfDisposed(_disposed, nameof(SessionManager));

    /// <summary>
    ///     Disposes the session manager and all its sessions.
    /// </summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            LogSessionLifecycleEvent("Disposing SessionManager");

            lock (_lock)
            {
                SessionDisposer.DisposeAllSessions(
                    _sessions,
                    _sessionOrder,
                    ref _activeSessionId,
                    OnSessionStateChanged,
                    OnSessionTitleChanged,
                    OnSessionProcessExited,
                    LogSessionLifecycleEvent);
            }

            LogSessionLifecycleEvent("SessionManager disposed successfully");
            _disposed = true;
        }
    }
}
