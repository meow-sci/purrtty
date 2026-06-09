using Brutal.ImGuiApi;
using PurrTTY.Terminal.Rendering;

namespace purrTTY.Display.Ghostty;

/// <summary>
/// The four monospace font variants the grid renderer draws with. Each variant
/// falls back to <see cref="Regular"/> when the font family does not ship it, so
/// <see cref="Select"/> always returns a usable font.
/// </summary>
internal readonly struct FrameFonts
{
    public readonly ImFontPtr Regular;
    public readonly ImFontPtr Bold;
    public readonly ImFontPtr Italic;
    public readonly ImFontPtr BoldItalic;

    public FrameFonts(ImFontPtr regular, ImFontPtr bold, ImFontPtr italic, ImFontPtr boldItalic)
    {
        Regular = regular;
        Bold = bold;
        Italic = italic;
        BoldItalic = boldItalic;
    }

    /// <summary>Picks the variant matching a cell's bold/italic flags.</summary>
    public ImFontPtr Select(CellFlags flags)
    {
        bool bold = (flags & CellFlags.Bold) != 0;
        bool italic = (flags & CellFlags.Italic) != 0;
        return (bold, italic) switch
        {
            (true, true) => BoldItalic,
            (true, false) => Bold,
            (false, true) => Italic,
            _ => Regular,
        };
    }
}
