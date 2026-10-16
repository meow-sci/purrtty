namespace purrTTY.Core.Terminal;

/// <summary>
///     A handle to one terminal opened through <see cref="ITerminalLauncher"/>. Properties are
///     cheap snapshots safe to read from any thread. The <c>...Async</c> methods are marshalled
///     onto the game's main thread and may be called from any thread; their tasks complete after
///     the work ran there (or fault with the reason). On a closed handle they complete immediately:
///     <see cref="CloseAsync"/> and <see cref="FocusAsync"/> succeed as no-ops, the others fault
///     with <see cref="ObjectDisposedException"/>.
/// </summary>
public interface ITerminalHandle
{
    /// <summary>Launcher-assigned unique id of this terminal; stable for the handle's lifetime.</summary>
    Guid Id { get; }

    /// <summary>
    ///     The terminal's registry name. May differ from the name requested (made unique) and
    ///     changes after <see cref="RenameAsync"/>. A tab is not a registry entry: its name is the
    ///     requested name, or <c>"&lt;window name&gt;#&lt;8 hex digits of Id&gt;"</c> when none was given.
    /// </summary>
    string Name { get; }

    /// <summary>
    ///     Where the terminal is placed. A <see cref="TerminalPlacementKind.Tab"/> request that
    ///     finds no open window opens a window instead and reports
    ///     <see cref="TerminalPlacementKind.Window"/>.
    /// </summary>
    TerminalPlacementKind Placement { get; }

    /// <summary>True from creation until the terminal is closed.</summary>
    bool IsOpen { get; }

    /// <summary>True while the program inside the terminal is running.</summary>
    bool IsRunning { get; }

    /// <summary>The program's exit code once it exited; null while running or when it never started.</summary>
    int? ExitCode { get; }

    /// <summary>Current grid width in columns; 0 until the first measurement.</summary>
    int Cols { get; }

    /// <summary>Current grid height in rows; 0 until the first measurement.</summary>
    int Rows { get; }

    /// <summary>
    ///     Raised on the game's main (ImGui) thread for every lifecycle transition of this terminal.
    ///     Handlers that need another thread must marshal themselves; a throwing handler is
    ///     logged and does not affect other handlers.
    /// </summary>
    event Action<ITerminalHandle, TerminalLifecycleEvent>? Lifecycle;

    /// <summary>
    ///     Gives the terminal input focus. Does not show a hidden terminal UI. A no-op on a closed handle.
    /// </summary>
    Task FocusAsync();

    /// <summary>Closes the terminal. Completes immediately when it is already closed.</summary>
    Task CloseAsync();

    /// <summary>
    ///     Renames the terminal. Faults with <see cref="InvalidOperationException"/> when the name is
    ///     blank or already used by another terminal.
    /// </summary>
    /// <param name="name">The new registry name.</param>
    Task RenameAsync(string name);

    /// <summary>
    ///     Resizes the terminal to the requested grid. In-world terminals resize live to exactly the
    ///     requested grid (clamped); a window is resized to the approximate pixel size for it; a tab
    ///     cannot be resized separately from its window. Resolves to false when the resize could
    ///     not be applied.
    /// </summary>
    Task<bool> ResizeAsync(int cols, int rows);

    /// <summary>
    ///     Updates an in-world terminal's anchor and transform live: the vehicle/part/sub-part ids,
    ///     offset, rotation and size (and, for a billboard, distance, always-on-top and click-to-focus)
    ///     are all replaced by the given options (unset members take their defaults), and any
    ///     opacity that is set is applied. The anchor mode (part or billboard) is fixed at launch, and
    ///     <c>Name</c>, <c>ThemeName</c>, <c>Cols</c>/<c>Rows</c> and <c>Focus</c> are ignored here (use
    ///     the dedicated methods). Window and tab handles complete with
    ///     <see cref="InvalidOperationException"/>.
    /// </summary>
    Task SetPlacementAsync(TerminalInWorldOptions placement);

    /// <summary>
    ///     Applies a colour theme by name. Faults with <see cref="KeyNotFoundException"/> when no
    ///     such theme exists, and with <see cref="InvalidOperationException"/> for a tab (it uses
    ///     its window's theme).
    /// </summary>
    Task ApplyThemeAsync(string themeName);

    /// <summary>
    ///     Injects bytes into the program's input as if typed. Thread-safe and non-blocking: the bytes
    ///     are copied and delivered on the main thread, in call order. A no-op on a closed handle.
    /// </summary>
    void WriteInput(ReadOnlyMemory<byte> bytes);
}
