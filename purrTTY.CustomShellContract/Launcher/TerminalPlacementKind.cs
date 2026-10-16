namespace purrTTY.Core.Terminal;

/// <summary>
///     Where a launched terminal is shown. Part of the cross-mod launcher ABI
///     (<see cref="ITerminalLauncher"/>); values are append-only.
/// </summary>
public enum TerminalPlacementKind
{
    /// <summary>A new standalone 2D terminal window.</summary>
    Window,

    /// <summary>
    ///     A new tab in the currently focused 2D terminal window. Falls back to
    ///     <see cref="Window"/> when no 2D window is open.
    /// </summary>
    Tab,

    /// <summary>
    ///     An in-world (render-to-texture) terminal anchored to a vehicle part
    ///     (see <see cref="TerminalInWorldOptions"/>).
    /// </summary>
    InWorldPart,

    /// <summary>
    ///     An in-world terminal pinned in the camera's view (a HUD panel; see
    ///     <see cref="TerminalInWorldOptions"/>).
    /// </summary>
    InWorldBillboard,
}
