namespace Ghostty.Vt.Enums;

// DEC private modes only (values verified against the pinned ghostty
// src/terminal/modes.zig). ANSI modes (e.g. IRM/insert, mode 4) are NOT
// addressable with a bare number: GhosttyMode packs `ansi` into bit 15, so an
// ANSI member would need `value | (1 << 15)`. None are wrapped because purrtty
// never queries one.
public enum TerminalMode
{
    CursorKeys = 1,
    AutoWrap = 7,
    MouseX10 = 9,
    MouseNormal = 1000,
    MouseButton = 1002,
    MouseAny = 1003,
    FocusEvent = 1004,
    MouseSGR = 1006,
    AltScreen = 1049,
    BracketedPaste = 2004,
    SynchronizedOutput = 2026,
}
