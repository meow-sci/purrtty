using purrTTY.Display.Theming;
using PurrTTY.Terminal.Rendering;

namespace purrTTY.Display.Ghostty;

/// <summary>
/// The live display settings of one terminal window. Mutated by the game menus
/// (font/opacity sliders, theme application) and snapshotted by "save current
/// settings as theme".
/// </summary>
public sealed class TerminalWindowSettings
{
    /// <summary>Default hot zone fill color (also the fallback when a config value is absent).</summary>
    public static readonly RgbaColor DefaultHotZoneColor = new(0x4E, 0x9A, 0xE9);

    public string ThemeName { get; set; } = "Default";
    public ThemeColors Colors { get; set; } = new();
    public string FontFamily { get; set; } = "Hack";
    public float FontSize { get; set; } = 32f;
    public float BackgroundOpacity { get; set; } = 1f;
    public float ForegroundOpacity { get; set; } = 1f;
    public float CellBackgroundOpacity { get; set; } = 1f;

    /// <summary>Default cursor shape (Block/Bar/Underline); apps may still override via DECSCUSR.</summary>
    public CursorShape CursorStyle { get; set; } = CursorShape.Block;

    /// <summary>Whether the default cursor blinks (animated by the controller's shared phase).</summary>
    public bool CursorBlink { get; set; } = true;

    /// <summary>Draw a border around the window while it holds ImGui focus.</summary>
    public bool BorderOnFocus { get; set; }

    /// <summary>Draw a border around the window while the mouse is over it.</summary>
    public bool BorderOnHover { get; set; }

    /// <summary>Opacity of the focus/hover border (0..1).</summary>
    public float BorderOpacity { get; set; } = 0.5f;

    /// <summary>
    /// Lock mode: while the window is not focused, mouse input passes through
    /// the terminal to the game. Refocus via the hot zone, menu, or hotkey.
    /// </summary>
    public bool LockMode { get; set; }

    /// <summary>Show the click-to-focus hot zone while lock mode has the window click-through.</summary>
    public bool HotZoneEnabled { get; set; } = true;

    public HotZonePlacement HotZonePlacement { get; set; } = HotZonePlacement.TopRight;
    public float HotZoneWidth { get; set; } = 28f;
    public float HotZoneHeight { get; set; } = 28f;
    public RgbaColor HotZoneColor { get; set; } = DefaultHotZoneColor;

    /// <summary>Hot zone fill opacity while idle (0 = invisible until hovered).</summary>
    public float HotZoneOpacity { get; set; } = 0.25f;

    /// <summary>Hot zone fill opacity while hovered.</summary>
    public float HotZoneHoverOpacity { get; set; } = 0.6f;

    /// <summary>
    /// Applies a theme's <b>optional</b> display settings over these settings,
    /// clamped to their valid ranges; absent fields keep their current values
    /// (bundled themes are colors-only). The single implementation behind both
    /// "apply theme to a window" and "build a new window's settings from the
    /// configured defaults" — name and colors are applied by those callers,
    /// since their sourcing differs.
    /// </summary>
    public void ApplyThemeOverrides(ThemeDefinition theme)
    {
        if (theme.FontFamily is { } family)
        {
            FontFamily = family;
        }

        if (theme.FontSize is { } size)
        {
            FontSize = Math.Clamp(size, Controllers.LayoutConstants.MIN_FONT_SIZE, Controllers.LayoutConstants.MAX_FONT_SIZE);
        }

        if (theme.BackgroundOpacity is { } bg)
        {
            BackgroundOpacity = Math.Clamp(bg, 0f, 1f);
        }

        if (theme.ForegroundOpacity is { } fg)
        {
            ForegroundOpacity = Math.Clamp(fg, 0f, 1f);
        }

        if (theme.CellBackgroundOpacity is { } cell)
        {
            CellBackgroundOpacity = Math.Clamp(cell, 0f, 1f);
        }

        if (theme.CursorStyle is { } cursorStyle)
        {
            CursorStyle = cursorStyle;
        }

        if (theme.CursorBlink is { } cursorBlink)
        {
            CursorBlink = cursorBlink;
        }

        if (theme.BorderOnFocus is { } borderOnFocus)
        {
            BorderOnFocus = borderOnFocus;
        }

        if (theme.BorderOnHover is { } borderOnHover)
        {
            BorderOnHover = borderOnHover;
        }

        if (theme.BorderOpacity is { } borderOpacity)
        {
            BorderOpacity = Math.Clamp(borderOpacity, 0f, 1f);
        }

        if (theme.LockMode is { } lockMode)
        {
            LockMode = lockMode;
        }

        if (theme.HotZoneEnabled is { } hotZoneEnabled)
        {
            HotZoneEnabled = hotZoneEnabled;
        }

        if (theme.HotZonePlacement is { } hotZonePlacement)
        {
            HotZonePlacement = hotZonePlacement;
        }

        if (theme.HotZoneWidth is { } hotZoneWidth)
        {
            HotZoneWidth = Math.Clamp(hotZoneWidth, TerminalWindow.MinHotZoneSize, TerminalWindow.MaxHotZoneSize);
        }

        if (theme.HotZoneHeight is { } hotZoneHeight)
        {
            HotZoneHeight = Math.Clamp(hotZoneHeight, TerminalWindow.MinHotZoneSize, TerminalWindow.MaxHotZoneSize);
        }

        if (theme.HotZoneColor is { } hotZoneColor)
        {
            HotZoneColor = hotZoneColor;
        }

        if (theme.HotZoneOpacity is { } hotZoneOpacity)
        {
            HotZoneOpacity = Math.Clamp(hotZoneOpacity, 0f, 1f);
        }

        if (theme.HotZoneHoverOpacity is { } hotZoneHoverOpacity)
        {
            HotZoneHoverOpacity = Math.Clamp(hotZoneHoverOpacity, 0f, 1f);
        }
    }

    /// <summary>
    /// Snapshots these settings as a complete, named theme bundle: a clone of the
    /// colors plus <b>every</b> optional display field. This is the inverse of
    /// <see cref="ApplyThemeOverrides"/> (which restores the display fields) plus
    /// the colors the applying window assigns separately — so a definition produced
    /// here, saved, reloaded, and applied to fresh settings reproduces this exact
    /// appearance. Keeping it next to <see cref="ApplyThemeOverrides"/> makes the
    /// "a theme encompasses everything" round-trip unit-testable without ImGui.
    /// </summary>
    public ThemeDefinition ToThemeDefinition(string name) => new()
    {
        Name = name,
        Source = ThemeSource.UserFile,
        Colors = Colors.Clone(),
        FontFamily = FontFamily,
        FontSize = FontSize,
        BackgroundOpacity = BackgroundOpacity,
        ForegroundOpacity = ForegroundOpacity,
        CellBackgroundOpacity = CellBackgroundOpacity,
        CursorStyle = CursorStyle,
        CursorBlink = CursorBlink,
        BorderOnFocus = BorderOnFocus,
        BorderOnHover = BorderOnHover,
        BorderOpacity = BorderOpacity,
        LockMode = LockMode,
        HotZoneEnabled = HotZoneEnabled,
        HotZonePlacement = HotZonePlacement,
        HotZoneWidth = HotZoneWidth,
        HotZoneHeight = HotZoneHeight,
        HotZoneColor = HotZoneColor,
        HotZoneOpacity = HotZoneOpacity,
        HotZoneHoverOpacity = HotZoneHoverOpacity,
    };
}
