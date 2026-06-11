namespace PurrTTY.Terminal.Rendering;

/// <summary>
/// Boolean per-cell rendering attributes. Underline is modeled separately by
/// <see cref="UnderlineStyle"/> since it has six distinct styles.
/// </summary>
[Flags]
public enum CellFlags
{
    None = 0,
    Bold = 1 << 0,
    Italic = 1 << 1,
    Faint = 1 << 2,
    Blink = 1 << 3,
    Inverse = 1 << 4,
    Strikethrough = 1 << 5,
    Invisible = 1 << 6,
    Overline = 1 << 7,
}

/// <summary>Cell underline style (maps 1:1 to libghostty SGR underline 0-5).</summary>
public enum UnderlineStyle
{
    None = 0,
    Single = 1,
    Double = 2,
    Curly = 3,
    Dotted = 4,
    Dashed = 5,
}

/// <summary>
/// Display width of a cell. <see cref="Spacer"/> covers both the trailing cell
/// of a wide grapheme and head spacers; frontends render nothing for it.
/// </summary>
public enum CellWidth
{
    Narrow = 0,
    Wide = 1,
    Spacer = 2,
}

/// <summary>Cursor shape, renderer-neutral (maps to libghostty cursor visual style).</summary>
public enum CursorShape
{
    Block = 0,
    Bar = 1,
    Underline = 2,
    BlockHollow = 3,
}
