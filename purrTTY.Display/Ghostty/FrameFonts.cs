using Brutal.ImGuiApi;
using PurrTTY.Terminal.Rendering;

namespace purrTTY.Display.Ghostty;

/// <summary>
/// Caches, per font variant, which non-ASCII codepoints have a measured
/// advance exactly matching the grid cell width — those may join batched
/// glyph runs (critical for half-block "pixel" output and box-drawing TUIs,
/// which would otherwise pay one draw call per cell). Owned by the window and
/// reset whenever the font/size resolution changes.
/// </summary>
internal sealed class GlyphBatchCache
{
    private readonly Dictionary<uint, bool>[] _byVariant =
    {
        new(), new(), new(), new(),
    };

    public void Clear()
    {
        foreach (var map in _byVariant)
        {
            map.Clear();
        }
    }

    /// <summary>
    /// Whether <paramref name="codepoint"/> advances by exactly one cell in
    /// the given variant font. Measured once per codepoint and cached.
    /// </summary>
    public bool CanBatch(uint codepoint, int variant, ImFontPtr font, float fontSize, float cellWidth)
    {
        var map = _byVariant[variant & 3];
        if (!map.TryGetValue(codepoint, out bool ok))
        {
            ImGui.PushFont(font, fontSize);
            var size = ImGui.CalcTextSize(char.ConvertFromUtf32((int)codepoint));
            ImGui.PopFont();
            // Tight per-char tolerance: drift accumulates along a run.
            ok = Math.Abs(size.X - cellWidth) <= 0.005f;
            map[codepoint] = ok;
        }

        return ok;
    }
}

/// <summary>
/// The four monospace font variants the grid renderer draws with, plus a
/// per-variant "ASCII run batching is safe" flag (the variant's printable-ASCII
/// advance was measured to exactly match the cell width, so a run of cells can
/// be drawn with a single AddText instead of one call per cell). Each variant
/// falls back to <see cref="Regular"/> when the font family does not ship it,
/// so <see cref="Select"/> always returns a usable font.
/// </summary>
internal readonly struct FrameFonts
{
    public readonly ImFontPtr Regular;
    public readonly ImFontPtr Bold;
    public readonly ImFontPtr Italic;
    public readonly ImFontPtr BoldItalic;

    /// <summary>Per-codepoint batch validation for non-ASCII glyphs; null disables it.</summary>
    public readonly GlyphBatchCache? BatchCache;

    private readonly bool _batchRegular;
    private readonly bool _batchBold;
    private readonly bool _batchItalic;
    private readonly bool _batchBoldItalic;

    public FrameFonts(ImFontPtr regular, ImFontPtr bold, ImFontPtr italic, ImFontPtr boldItalic)
        : this(regular, bold, italic, boldItalic, false, false, false, false, null)
    {
    }

    public FrameFonts(
        ImFontPtr regular,
        ImFontPtr bold,
        ImFontPtr italic,
        ImFontPtr boldItalic,
        bool batchRegular,
        bool batchBold,
        bool batchItalic,
        bool batchBoldItalic,
        GlyphBatchCache? batchCache)
    {
        Regular = regular;
        Bold = bold;
        Italic = italic;
        BoldItalic = boldItalic;
        _batchRegular = batchRegular;
        _batchBold = batchBold;
        _batchItalic = batchItalic;
        _batchBoldItalic = batchBoldItalic;
        BatchCache = batchCache;
    }

    /// <summary>Picks the variant matching a cell's bold/italic flags.</summary>
    public ImFontPtr Select(CellFlags flags) => SelectVariant(VariantIndex(flags));

    /// <summary>Variant index for a cell's flags: bit 0 = bold, bit 1 = italic.</summary>
    public static int VariantIndex(CellFlags flags)
        => ((flags & CellFlags.Bold) != 0 ? 1 : 0) | ((flags & CellFlags.Italic) != 0 ? 2 : 0);

    public ImFontPtr SelectVariant(int variant) => variant switch
    {
        1 => Bold,
        2 => Italic,
        3 => BoldItalic,
        _ => Regular,
    };

    /// <summary>Whether ASCII run batching is metric-safe for the variant.</summary>
    public bool CanBatchVariant(int variant) => variant switch
    {
        1 => _batchBold,
        2 => _batchItalic,
        3 => _batchBoldItalic,
        _ => _batchRegular,
    };
}
