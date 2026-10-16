using purrTTY.Core.Terminal;

namespace purrTTY.Display.Launcher;

/// <summary>
/// The pure state machine behind a launcher terminal handle: it records what has happened to one
/// terminal and tells the caller which <see cref="TerminalLifecycleEvent"/>s to raise for each
/// observation (empty when nothing changed), so duplicate or out-of-order signals from the
/// game's polling and off-thread callbacks can never produce a duplicate or impossible event.
/// <para>
/// Rules: <c>Created</c>, <c>Started</c>, <c>Exited</c> and <c>Closed</c> are each raised at most
/// once; <c>Started</c> never follows <c>Exited</c>/<c>Closed</c>; focus and resize events are
/// raised only on real transitions and only while the terminal is open; a first grid observation
/// just records the size; once <see cref="MarkClosed"/> ran, every method returns empty.
/// </para>
/// Not thread-safe by design: the launcher drives it from the main thread only (off-thread signals
/// are queued to the main thread first).
/// </summary>
public sealed class TerminalHandleState
{
    private static readonly TerminalLifecycleEvent[] NoEvents = [];
    private static readonly TerminalLifecycleEvent[] CreatedEvent = [TerminalLifecycleEvent.Created];
    private static readonly TerminalLifecycleEvent[] StartedEvent = [TerminalLifecycleEvent.Started];
    private static readonly TerminalLifecycleEvent[] FocusedEvent = [TerminalLifecycleEvent.Focused];
    private static readonly TerminalLifecycleEvent[] UnfocusedEvent = [TerminalLifecycleEvent.Unfocused];
    private static readonly TerminalLifecycleEvent[] ResizedEvent = [TerminalLifecycleEvent.Resized];
    private static readonly TerminalLifecycleEvent[] ExitedEvent = [TerminalLifecycleEvent.Exited];
    private static readonly TerminalLifecycleEvent[] ClosedEvent = [TerminalLifecycleEvent.Closed];

    private bool _created;
    private bool _started;
    private bool _exited;
    private bool _closed;

    /// <summary>True from <see cref="MarkCreated"/> until <see cref="MarkClosed"/>.</summary>
    public bool IsOpen { get; private set; }

    /// <summary>True from <see cref="MarkStarted"/> until the program exits or the terminal closes.</summary>
    public bool IsRunning { get; private set; }

    /// <summary>The exit code passed to <see cref="MarkExited"/> (null while running, or when it is unknown).</summary>
    public int? ExitCode { get; private set; }

    /// <summary>Last observed grid width in columns; 0 until observed.</summary>
    public int Cols { get; private set; }

    /// <summary>Last observed grid height in rows; 0 until observed.</summary>
    public int Rows { get; private set; }

    /// <summary>True while the terminal has input focus.</summary>
    public bool Focused { get; private set; }

    /// <summary>The terminal now exists. Returns <c>[Created]</c> the first time, empty afterwards.</summary>
    public IReadOnlyList<TerminalLifecycleEvent> MarkCreated()
    {
        if (_closed || _created)
        {
            return NoEvents;
        }

        _created = true;
        IsOpen = true;
        return CreatedEvent;
    }

    /// <summary>
    /// The program started. Returns <c>[Started]</c> the first time; empty when already started or
    /// after <see cref="MarkExited"/>/<see cref="MarkClosed"/>.
    /// </summary>
    public IReadOnlyList<TerminalLifecycleEvent> MarkStarted()
    {
        if (_closed || _exited || _started)
        {
            return NoEvents;
        }

        _started = true;
        IsRunning = true;
        return StartedEvent;
    }

    /// <summary>
    /// Records the current focus. Returns <c>[Focused]</c> or <c>[Unfocused]</c> only when the value
    /// changed and the terminal is open.
    /// </summary>
    public IReadOnlyList<TerminalLifecycleEvent> ObserveFocus(bool focused)
    {
        if (!IsOpen || focused == Focused)
        {
            return NoEvents;
        }

        Focused = focused;
        return focused ? FocusedEvent : UnfocusedEvent;
    }

    /// <summary>
    /// Records the current grid. The first valid observation only records it; afterwards returns
    /// <c>[Resized]</c> when the size changed. Ignored while closed/not yet open and for a
    /// non-positive size.
    /// </summary>
    public IReadOnlyList<TerminalLifecycleEvent> ObserveGrid(int cols, int rows)
    {
        if (!IsOpen || cols <= 0 || rows <= 0)
        {
            return NoEvents;
        }

        bool hadGrid = Cols > 0 && Rows > 0;
        bool changed = cols != Cols || rows != Rows;
        Cols = cols;
        Rows = rows;
        return hadGrid && changed ? ResizedEvent : NoEvents;
    }

    /// <summary>
    /// The program exited. Returns <c>[Exited]</c> the first time (clearing
    /// <see cref="IsRunning"/> and recording <paramref name="exitCode"/>); empty afterwards or once closed.
    /// </summary>
    public IReadOnlyList<TerminalLifecycleEvent> MarkExited(int? exitCode)
    {
        if (_closed || _exited)
        {
            return NoEvents;
        }

        _exited = true;
        IsRunning = false;
        ExitCode = exitCode;
        return ExitedEvent;
    }

    /// <summary>
    /// The terminal closed. Returns <c>[Closed]</c> the first time (clearing open, running and focus
    /// state); empty afterwards. Every other method returns empty from now on.
    /// </summary>
    public IReadOnlyList<TerminalLifecycleEvent> MarkClosed()
    {
        if (_closed)
        {
            return NoEvents;
        }

        _closed = true;
        IsOpen = false;
        IsRunning = false;
        Focused = false;
        return ClosedEvent;
    }
}
