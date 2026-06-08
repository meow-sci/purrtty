namespace PurrTTY.Terminal.Rendering;

/// <summary>
/// One rendered grid cell. Colors are pre-resolved by the engine (theme +
/// palette + inverse already applied), so a frontend draws <see cref="Bg"/> /
/// <see cref="Fg"/> directly. <see cref="Grapheme"/> carries the full grapheme
/// cluster string (emoji / ZWJ safe); <see langword="null"/> means blank.
/// </summary>
public struct FrameCell
{
    public string? Grapheme;
    public RgbaColor Fg;
    public RgbaColor Bg;
    public CellFlags Flags;
    public UnderlineStyle Underline;
    public CellWidth Width;

    /// <summary>Underline color, or <see langword="null"/> to use <see cref="Fg"/>.</summary>
    public RgbaColor? UnderlineColor;
}
