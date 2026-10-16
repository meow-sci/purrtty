namespace purrTTY.Core.Terminal;

/// <summary>
///     Options for a 2D terminal window or tab (<see cref="TerminalPlacementKind.Window"/> /
///     <see cref="TerminalPlacementKind.Tab"/>). Every member is optional.
///     <para>
///         A tab lives inside an existing window, so for <see cref="TerminalPlacementKind.Tab"/> only
///         <see cref="Title"/>, <see cref="Focus"/> and <see cref="ShowTerminal"/> apply (and
///         <see cref="Name"/>, which labels the handle); the theme, geometry and window-behaviour
///         options belong to the host window and are ignored.
///     </para>
/// </summary>
public sealed class TerminalWindowOptions
{
    /// <summary>
    ///     Registry name shown in the theme picker; null picks an automatic one. A name already in
    ///     use is made unique with a numeric suffix, so read the actual name from
    ///     <see cref="ITerminalHandle.Name"/>.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>Initial session (tab) title; a program may replace it later with an OSC 0/2 sequence.</summary>
    public string? Title { get; set; }

    /// <summary>Name of the colour theme to apply; null (or an unknown name) uses the user's default.</summary>
    public string? ThemeName { get; set; }

    /// <summary>Window X position in pixels; used only together with <see cref="PosY"/>. Null cascades.</summary>
    public float? PosX { get; set; }

    /// <summary>Window Y position in pixels; used only together with <see cref="PosX"/>.</summary>
    public float? PosY { get; set; }

    /// <summary>Window width in pixels; used only together with <see cref="Height"/>. Null uses the default size.</summary>
    public float? Width { get; set; }

    /// <summary>Window height in pixels; used only together with <see cref="Width"/>.</summary>
    public float? Height { get; set; }

    /// <summary>
    ///     Requested grid width in columns, converted to a window size from the theme's font cell.
    ///     Ignored when <see cref="Width"/>/<see cref="Height"/> are both set; needs
    ///     <see cref="Rows"/> as well. The conversion is approximate (the user can still resize).
    /// </summary>
    public int? Cols { get; set; }

    /// <summary>Requested grid height in rows; see <see cref="Cols"/>.</summary>
    public int? Rows { get; set; }

    /// <summary>Lock mode: while unfocused the window passes mouse input through to the game. Null keeps the default.</summary>
    public bool? LockMode { get; set; }

    /// <summary>
    ///     True hides the window chrome (title strip, tabs, border) while the mouse is not over the
    ///     window; false keeps it always visible. Null follows the user's global setting.
    /// </summary>
    public bool? HideChrome { get; set; }

    /// <summary>True prevents the user from resizing the window. Null keeps the default (resizable).</summary>
    public bool? NoResize { get; set; }

    /// <summary>Give the new terminal input focus. Default true.</summary>
    public bool Focus { get; set; } = true;

    /// <summary>
    ///     Make the terminal UI visible after the window exists (equivalent to the user toggling it
    ///     on). Default true. Set false to open the window while the terminal UI stays hidden.
    /// </summary>
    public bool ShowTerminal { get; set; } = true;
}
