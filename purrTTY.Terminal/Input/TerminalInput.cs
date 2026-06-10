namespace PurrTTY.Terminal.Input;

/// <summary>A viewport grid coordinate (0-indexed column/row).</summary>
public readonly struct GridPoint
{
    public int Col { get; }
    public int Row { get; }

    public GridPoint(int col, int row)
    {
        Col = col;
        Row = row;
    }

    public override string ToString() => $"({Col},{Row})";
}

/// <summary>How a selection drag/click should grow.</summary>
public enum SelectMode
{
    Cell = 0,
    Word = 1,
    Line = 2,
}

/// <summary>Keyboard action, matching the engine's release/press/repeat trio.</summary>
public enum KeyAction
{
    Release = 0,
    Press = 1,
    Repeat = 2,
}

/// <summary>Keyboard / mouse modifier bitmask (matches libghostty GHOSTTY_MODS_*).</summary>
[Flags]
public enum KeyModifiers
{
    None = 0,
    Shift = 1 << 0,
    Ctrl = 1 << 1,
    Alt = 1 << 2,
    Super = 1 << 3,
    CapsLock = 1 << 4,
    NumLock = 1 << 5,
}

/// <summary>
/// A renderer-neutral key event. <see cref="Key"/> is a physical key code
/// (see <see cref="TerminalKey"/>); <see cref="Text"/> carries the typed UTF-8
/// text for printable input. The backend turns this into PTY bytes via the
/// engine's mode-aware key encoder.
/// </summary>
public readonly struct TerminalKeyEvent
{
    public TerminalKey Key { get; init; }
    public KeyAction Action { get; init; }
    public KeyModifiers Modifiers { get; init; }
    public string? Text { get; init; }

    public TerminalKeyEvent(TerminalKey key, KeyAction action = KeyAction.Press,
        KeyModifiers modifiers = KeyModifiers.None, string? text = null)
    {
        Key = key;
        Action = action;
        Modifiers = modifiers;
        Text = text;
    }
}

/// <summary>
/// Renderer-neutral mouse button identity. These are purrtty's own values — they
/// are NOT libghostty's wire/enum codes. The backend translates them to the
/// engine's native button enum in <see cref="PurrTTY.Terminal.Ghostty.GhosttyTerminalSurface"/>.
/// </summary>
public enum MouseButton
{
    None = -1,
    Left = 0,
    Middle = 1,
    Right = 2,
    ScrollUp = 3,
    ScrollDown = 4,
}

/// <summary>Mouse action.</summary>
public enum MouseAction
{
    Press = 0,
    Release = 1,
    Motion = 2,
}

/// <summary>
/// A renderer-neutral mouse event in surface (pixel) space. The backend maps it
/// to a terminal mouse report using the engine's mouse encoder + current modes.
/// </summary>
public readonly struct TerminalMouseEvent
{
    public MouseAction Action { get; init; }
    public MouseButton Button { get; init; }
    public KeyModifiers Modifiers { get; init; }

    /// <summary>Pointer X in surface pixels.</summary>
    public float X { get; init; }

    /// <summary>Pointer Y in surface pixels.</summary>
    public float Y { get; init; }
}
