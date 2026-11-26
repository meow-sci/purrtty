using purrTTY.Core.Terminal;
using float2 = Brutal.Numerics.float2;

namespace purrTTY.Display.Ghostty;

/// <summary>
/// A 2D terminal-window spec for the layout system: position + size (px), the named
/// theme to apply, and the shell to launch. Deliberately carries <b>no</b> cols/rows or
/// font — the grid is derived live from the window's pixel size and the resolved theme,
/// so it re-flows if the theme/font changes. Symmetric to the in-world record; the
/// GameMod layout manager maps a saved <c>TerminalEntry</c> to/from this.
/// </summary>
public sealed class WindowLayoutRecord
{
    public string Name { get; set; } = "";
    public float2? Position { get; set; }
    public float2? Size { get; set; }
    public string? ThemeName { get; set; }
    public ProcessLaunchOptions? Launch { get; set; }

    /// <summary>
    /// The shell a window is captured with, as a copy: its active session's launch options, or —
    /// while no session is active yet (still starting) — the options the window was opened with,
    /// so a window opened for a special program (a kFS editor, which layouts leave out) is not
    /// saved as a default-shell window under that program's name. Null: the default shell.
    /// </summary>
    public static ProcessLaunchOptions? LaunchOf(ProcessLaunchOptions? activeSession, ProcessLaunchOptions? openedWith) =>
        (activeSession ?? openedWith)?.Clone();
}
