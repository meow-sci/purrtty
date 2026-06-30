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
}
