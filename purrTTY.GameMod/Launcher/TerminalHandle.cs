using purrTTY.Core.Terminal;
using purrTTY.Display.Launcher;
using purrTTY.Logging;
using PurrTTY.Terminal.Sessions;

namespace purrTTY.GameMod.Launcher;

/// <summary>
/// Shared behaviour of every launcher terminal handle: the lifecycle state machine
/// (<see cref="TerminalHandleState"/>), the any-thread property snapshot, event raising, session
/// binding (exit/start detection), deferred input injection and the main-thread marshalling of every
/// operation. The placement-specific pieces (a 2D window, a tab, an in-world quad) are the abstract
/// <c>...Core</c> methods of the subclasses.
/// <para>
/// Threading: everything that touches purrTTY objects or <see cref="_state"/> runs on the main
/// thread (via <see cref="MainThreadPump"/>); the public property getters read an immutable
/// snapshot that is republished after each state change, so they are safe from any thread.
/// </para>
/// </summary>
internal abstract class TerminalHandle : ITerminalHandle
{
    // Input written before the session is published (it starts on a pool thread) is held here and
    // flushed on bind; capped so a runaway writer cannot grow memory without bound.
    private const int MaxPendingInputBytes = 1 << 20;

    private readonly TerminalLauncher _launcher;
    private readonly TerminalHandleState _state = new();
    private readonly List<byte[]> _pendingInput = new();
    private int _pendingInputBytes;
    private volatile StateSnapshot _snapshot = StateSnapshot.Initial;
    private TerminalSession? _session;

    protected TerminalHandle(TerminalLauncher launcher, MainThreadPump pump)
    {
        _launcher = launcher;
        Pump = pump;
        Id = Guid.NewGuid();
    }

    /// <summary>The pump all operations are marshalled through.</summary>
    protected MainThreadPump Pump { get; }

    /// <summary>The launcher that owns this handle.</summary>
    protected TerminalLauncher Launcher => _launcher;

    /// <summary>The session this handle is bound to (the first one for a window); main thread only.</summary>
    protected TerminalSession? Session => _session;

    /// <summary>True while the handle is open; main thread only (the state machine is not thread-safe).</summary>
    protected bool IsOpenOnMain => _state.IsOpen;

    /// <inheritdoc/>
    public Guid Id { get; }

    /// <inheritdoc/>
    public abstract string Name { get; }

    /// <inheritdoc/>
    public abstract TerminalPlacementKind Placement { get; }

    /// <inheritdoc/>
    public bool IsOpen => _snapshot.IsOpen;

    /// <inheritdoc/>
    public bool IsRunning => _snapshot.IsRunning;

    /// <inheritdoc/>
    public int? ExitCode => _snapshot.ExitCode;

    /// <inheritdoc/>
    public int Cols => _snapshot.Cols;

    /// <inheritdoc/>
    public int Rows => _snapshot.Rows;

    /// <inheritdoc/>
    public event Action<ITerminalHandle, TerminalLifecycleEvent>? Lifecycle;

    // ---- placement-specific operations (main thread, handle open) ----

    protected abstract void FocusCore();

    protected abstract void CloseCore();

    /// <returns>False when the name is blank or taken.</returns>
    protected abstract bool RenameCore(string name);

    protected abstract bool ResizeCore(int cols, int rows);

    protected abstract void SetPlacementCore(TerminalInWorldOptions placement);

    protected abstract void ApplyThemeCore(string themeName);

    /// <summary>Per-frame polling specific to the placement (closure detection, session binding).</summary>
    protected abstract void TickCore();

    /// <summary>True once the placement's grid has settled enough that a size change is a real resize.</summary>
    protected abstract bool IsGridReady { get; }

    /// <summary>Unsubscribes from the placement's own events.</summary>
    protected abstract void DetachCore();

    // ---- ITerminalHandle operations ----

    /// <inheritdoc/>
    public Task FocusAsync()
        => IsOpen
            ? Pump.RunAsync(() =>
            {
                if (_state.IsOpen)
                {
                    FocusCore();
                }
            })
            : Task.CompletedTask;

    /// <inheritdoc/>
    public Task CloseAsync()
        => IsOpen
            ? Pump.RunAsync(() =>
            {
                if (_state.IsOpen)
                {
                    CloseCore();
                    // Synchronously, so the caller sees IsOpen == false when the task completes; the
                    // placement's own closed signal later finds the state machine already closed.
                    CloseState();
                }
            })
            : Task.CompletedTask;

    /// <inheritdoc/>
    public Task RenameAsync(string name)
        => RunIfOpen(() =>
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new InvalidOperationException("A terminal name cannot be blank.");
            }

            if (!RenameCore(name))
            {
                throw new InvalidOperationException($"A terminal named '{name.Trim()}' already exists.");
            }
        });

    /// <inheritdoc/>
    public Task<bool> ResizeAsync(int cols, int rows)
        => RunIfOpen(() => ResizeCore(cols, rows));

    /// <inheritdoc/>
    public Task SetPlacementAsync(TerminalInWorldOptions placement)
    {
        if (placement is null)
        {
            return Task.FromException(new ArgumentNullException(nameof(placement)));
        }

        return RunIfOpen(() => SetPlacementCore(placement));
    }

    /// <inheritdoc/>
    public Task ApplyThemeAsync(string themeName)
    {
        if (string.IsNullOrWhiteSpace(themeName))
        {
            return Task.FromException(new KeyNotFoundException("No theme name was given."));
        }

        return RunIfOpen(() => ApplyThemeCore(themeName));
    }

    /// <inheritdoc/>
    public void WriteInput(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.IsEmpty || !IsOpen)
        {
            return;
        }

        // Copy now: the caller may reuse its buffer the moment this returns.
        byte[] copy = bytes.ToArray();
        Pump.Post(() => DeliverInput(copy));
    }

    private Task RunIfOpen(Action action)
        => IsOpen
            ? Pump.RunAsync(() =>
            {
                ThrowIfClosedOnMain();
                action();
            })
            : Task.FromException(ClosedException());

    private Task<T> RunIfOpen<T>(Func<T> func)
        => IsOpen
            ? Pump.RunAsync(() =>
            {
                ThrowIfClosedOnMain();
                return func();
            })
            : Task.FromException<T>(ClosedException());

    private void ThrowIfClosedOnMain()
    {
        if (!_state.IsOpen)
        {
            throw ClosedException();
        }
    }

    private ObjectDisposedException ClosedException() => new($"terminal '{Name}'", "The terminal is closed.");

    // ---- lifecycle (main thread) ----

    /// <summary>Marks the handle created and raises <c>Created</c>; called once by the launcher.</summary>
    internal void Initialize() => Apply(_state.MarkCreated());

    /// <summary>
    /// Closes the state machine (idempotent): unregisters the handle from the launcher, unsubscribes
    /// from the placement and raises <c>Closed</c> once.
    /// </summary>
    protected void CloseState()
    {
        var events = _state.MarkClosed();
        if (events.Count == 0)
        {
            return;
        }

        Detach();
        _launcher.Unregister(this);
        _pendingInput.Clear();
        Apply(events);
    }

    /// <summary>Launcher teardown: closes the handle without raising anything.</summary>
    internal void Shutdown()
    {
        _state.MarkClosed();
        Detach();
        _pendingInput.Clear();
        _snapshot = StateSnapshot.From(_state);
    }

    /// <summary>Per-frame poll: placement-specific checks, then the grid-size observation.</summary>
    internal void Tick()
    {
        if (!_state.IsOpen)
        {
            return;
        }

        TickCore();

        if (_state.IsOpen && _session is { } session && IsGridReady)
        {
            var surface = session.Surface;
            Apply(_state.ObserveGrid(surface.Cols, surface.Rows));
        }
    }

    /// <summary>Re-reads the live focus state through the state machine (events are re-checked, not trusted).</summary>
    protected void ObserveFocus(bool focused) => Apply(_state.ObserveFocus(focused));

    /// <summary>Records that the program inside could not be started or is gone, with no known exit code.</summary>
    protected void MarkExited(int? exitCode) => Apply(_state.MarkExited(exitCode));

    /// <summary>
    /// Binds the (single) session this handle represents: reports <c>Started</c>, flushes input that
    /// arrived early and catches a process that already exited before the exit event was subscribed.
    /// </summary>
    protected void BindSession(TerminalSession session)
    {
        if (_session != null || !_state.IsOpen)
        {
            return;
        }

        _session = session;
        session.ProcessExited += OnProcessExited;
        Apply(_state.MarkStarted());

        foreach (byte[] chunk in _pendingInput)
        {
            session.SendInput(chunk);
        }

        _pendingInput.Clear();
        _pendingInputBytes = 0;

        // A short-lived program may have exited before the subscription above; ExitCode is set by then.
        var process = session.ProcessManager;
        if (!process.IsRunning && process.ExitCode is { } code)
        {
            Apply(_state.MarkExited(code));
        }
    }

    private void Detach()
    {
        if (_session != null)
        {
            _session.ProcessExited -= OnProcessExited;
        }

        DetachCore();
    }

    // Raised OFF the main thread (the PTY exit watcher / the custom shell's thread): queue to the main
    // thread before touching any state or raising an event.
    private void OnProcessExited(object? sender, SessionProcessExitedEventArgs e)
    {
        int code = e.ExitCode;
        Pump.Post(() =>
        {
            if (ReferenceEquals(sender, _session))
            {
                Apply(_state.MarkExited(code));
            }
        });
    }

    private void DeliverInput(byte[] data)
    {
        if (!_state.IsOpen)
        {
            return;
        }

        if (_session is { } session)
        {
            session.SendInput(data);
            return;
        }

        if (_pendingInputBytes + data.Length > MaxPendingInputBytes)
        {
            ModLog.Log.Warning($"purrTTY launcher: dropped {data.Length} bytes of input for '{Name}' (session not started yet, buffer full)");
            return;
        }

        _pendingInput.Add(data);
        _pendingInputBytes += data.Length;
    }

    private void Apply(IReadOnlyList<TerminalLifecycleEvent> events)
    {
        if (events.Count == 0)
        {
            return;
        }

        // Publish first so a handler reading the properties sees the state its event announces.
        _snapshot = StateSnapshot.From(_state);
        for (int i = 0; i < events.Count; i++)
        {
            // A handler may close the terminal while we are still delivering a batch (Started, Exited,
            // ...): Closed is always the last event, so drop what would follow it.
            if (events[i] != TerminalLifecycleEvent.Closed && !_state.IsOpen)
            {
                break;
            }

            _launcher.Raise(this, events[i]);
        }
    }

    /// <summary>Raises this handle's own <c>Lifecycle</c> event; every subscriber is isolated from the others.</summary>
    internal void InvokeLifecycle(TerminalLifecycleEvent lifecycleEvent)
        => TerminalLauncher.InvokeIsolated(Lifecycle, this, lifecycleEvent, Name);

    private sealed record StateSnapshot(bool IsOpen, bool IsRunning, int? ExitCode, int Cols, int Rows)
    {
        public static readonly StateSnapshot Initial = new(false, false, null, 0, 0);

        public static StateSnapshot From(TerminalHandleState state)
            => new(state.IsOpen, state.IsRunning, state.ExitCode, state.Cols, state.Rows);
    }
}
