using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using purrTTY.Core.Terminal;

namespace PurrTTY.Terminal.Sessions;

/// <summary>
/// A single terminal session: an <see cref="ITerminalSurface"/> (libghostty-vt
/// backend) plus an <see cref="IProcessManager"/> (PTY/process or custom shell),
/// wired together. Replaces the legacy emulator-based session.
///
/// Data flow:
/// <list type="bullet">
/// <item>PTY output → <c>ProcessManager.DataReceived</c> → <c>Surface.Write</c> (thread-safe enqueue).</item>
/// <item>Engine replies (DA/DSR) → <c>Surface.PtyReply</c> → <c>ProcessManager.Write</c> (tick thread).</item>
/// <item>User input → frontend encodes via the surface → <see cref="SendInput"/> → PTY.</item>
/// </list>
/// The frontend drives <c>Surface.BuildFrame()</c> each tick; the session only wires the streams.
/// </summary>
public sealed class TerminalSession : IDisposable
{
    private readonly ILogger _logger;
    private bool _disposed;

    public TerminalSession(
        Guid id,
        string title,
        ITerminalSurface surface,
        IProcessManager processManager,
        ILogger? logger = null)
    {
        Id = id;
        Surface = surface ?? throw new ArgumentNullException(nameof(surface));
        ProcessManager = processManager ?? throw new ArgumentNullException(nameof(processManager));
        _logger = logger ?? NullLogger.Instance;

        Settings = new SessionSettings { Title = title ?? throw new ArgumentNullException(nameof(title)) };
        State = SessionState.Creating;
        CreatedAt = DateTime.UtcNow;

        ProcessManager.ProcessExited += OnProcessExited;
        ProcessManager.DataReceived += OnProcessDataReceived;
        ProcessManager.ProcessError += OnProcessError;
        Surface.PtyReply += OnSurfacePtyReply;
        Surface.TitleChanged += OnSurfaceTitleChanged;
    }

    public Guid Id { get; }

    /// <summary>The renderer-neutral terminal surface the frontend draws and drives.</summary>
    public ITerminalSurface Surface { get; }

    public IProcessManager ProcessManager { get; }
    public SessionSettings Settings { get; }
    public SessionState State { get; private set; }
    public DateTime CreatedAt { get; }
    public DateTime? LastActiveAt { get; set; }

    public string Title
    {
        get => Settings.Title;
        set
        {
            if (Settings.Title == value)
            {
                return;
            }

            var oldTitle = Settings.Title;
            Settings.Title = value ?? string.Empty;
            TitleChanged?.Invoke(this, new SessionTitleChangedEventArgs(oldTitle, Settings.Title));
        }
    }

    public event EventHandler<SessionStateChangedEventArgs>? StateChanged;
    public event EventHandler<SessionTitleChangedEventArgs>? TitleChanged;
    public event EventHandler<SessionProcessExitedEventArgs>? ProcessExited;

    /// <summary>Starts the shell process and transitions the session to Active.</summary>
    public async Task InitializeAsync(ProcessLaunchOptions launchOptions, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (State != SessionState.Creating)
        {
            throw new InvalidOperationException($"Cannot initialize session in state {State}");
        }

        try
        {
            await ProcessManager.StartAsync(launchOptions, cancellationToken);

            // Auto-run command: type it as stdin once the shell is up. The PTY line
            // discipline buffers the bytes until the shell reads them, so no readiness
            // wait/sleep is needed; for a remote (gatOS) shell the interactive login PTY
            // is already established with its env. Newline-terminated so it executes.
            if (!string.IsNullOrEmpty(launchOptions.StartupCommand))
            {
                ProcessManager.Write(
                    launchOptions.StartupCommand.EndsWith('\n')
                        ? launchOptions.StartupCommand
                        : launchOptions.StartupCommand + "\n");
            }

            ChangeState(SessionState.Active);
            LastActiveAt = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            ChangeState(SessionState.Failed, ex);
            throw;
        }
    }

    public void Activate()
    {
        ThrowIfDisposed();
        if (State == SessionState.Inactive)
        {
            ChangeState(SessionState.Active);
            LastActiveAt = DateTime.UtcNow;
            Settings.MarkAsActive();
        }
    }

    public void Deactivate()
    {
        // No ThrowIfDisposed: deactivating a session that was concurrently
        // closed is a benign no-op (it is already not Active), and the session
        // manager may race a close when it deactivates the previous session.
        if (State == SessionState.Active)
        {
            ChangeState(SessionState.Inactive);
            Settings.MarkAsInactive();
        }
    }

    /// <summary>
    /// Closes the session. Completes synchronously on the calling thread:
    /// the native surface is detached and disposed before this returns, so it
    /// MUST be called on the tick thread (disposing the surface from a pool
    /// thread races a concurrent <c>BuildFrame</c> on the same native handles).
    /// The potentially slow PTY/process teardown is backgrounded by
    /// <see cref="Dispose"/> and never blocks the caller.
    /// </summary>
    public Task CloseAsync(CancellationToken cancellationToken = default)
    {
        if (State == SessionState.Disposed)
        {
            return Task.CompletedTask;
        }

        ChangeState(SessionState.Terminating);
        Dispose();
        return Task.CompletedTask;
    }

    /// <summary>Writes user-input bytes (already encoded by the surface) to the shell.</summary>
    public void SendInput(ReadOnlySpan<byte> data)
    {
        if (!_disposed && ProcessManager.IsRunning && !data.IsEmpty)
        {
            ProcessManager.Write(data);
        }
    }

    /// <summary>Updates the recorded session dimensions (call alongside Surface.Resize).</summary>
    public void UpdateTerminalDimensions(int columns, int rows)
    {
        ThrowIfDisposed();
        try
        {
            Settings.UpdateDimensions(columns, rows);
        }
        catch (ArgumentException ex)
        {
            _logger.LogDebug(ex, "Invalid dimensions for session {SessionId}", Id);
        }
    }

    private void ChangeState(SessionState newState, Exception? error = null)
    {
        if (State == newState)
        {
            return;
        }

        State = newState;
        StateChanged?.Invoke(this, new SessionStateChangedEventArgs(newState, error));
    }

    // PTY output → surface (runs on the PTY read thread; Surface.Write is thread-safe).
    private void OnProcessDataReceived(object? sender, DataReceivedEventArgs e) => Surface.Write(e.Data.Span);

    // Engine replies → PTY (runs on the tick thread during BuildFrame).
    private void OnSurfacePtyReply(byte[] bytes)
    {
        if (ProcessManager.IsRunning)
        {
            ProcessManager.Write(bytes);
        }
    }

    private void OnSurfaceTitleChanged(string title) => Title = title;

    private void OnProcessExited(object? sender, ProcessExitedEventArgs e)
    {
        Settings.UpdateProcessState(e.ProcessId, false, e.ExitCode);
        ProcessExited?.Invoke(this, new SessionProcessExitedEventArgs(e.ExitCode, e.ProcessId));
    }

    // PTY write failures and input-queue overflow are reported here rather than
    // thrown (gotcha 20) — without this subscription the error channel
    // dead-ends and dropped input is silent.
    private void OnProcessError(object? sender, ProcessErrorEventArgs e)
        => _logger.LogWarning(e.Error, "PTY error for session {SessionId}: {Message}", Id, e.Message);

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        ChangeState(SessionState.Disposed);

        // Detach both streams first so neither the surface nor the (about to be
        // orphaned) process manager can raise events back into a disposed session.
        ProcessManager.ProcessExited -= OnProcessExited;
        ProcessManager.DataReceived -= OnProcessDataReceived;
        ProcessManager.ProcessError -= OnProcessError;
        Surface.PtyReply -= OnSurfacePtyReply;
        Surface.TitleChanged -= OnSurfaceTitleChanged;

        // The libghostty surface is native and single-threaded (CLAUDE.md gotcha #1),
        // so dispose it synchronously on the current (tick) thread.
        Surface.Dispose();

        // Fire-and-forget the PTY/process teardown. Stopping a shell can block for
        // seconds — graceful WaitForExit(2s) then Kill, plus ClosePseudoConsole,
        // which can wedge on a slow child such as WSL2 — and closing a terminal or
        // exiting the game must not stall on it. The manager is reaped on a
        // background thread (or by the OS if the game exits first); its events are
        // already detached so the orphan can't call back in.
        _ = Task.Run(() =>
        {
            try
            {
                ProcessManager.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Background PTY teardown for session {SessionId} failed", Id);
            }
        });
    }
}
