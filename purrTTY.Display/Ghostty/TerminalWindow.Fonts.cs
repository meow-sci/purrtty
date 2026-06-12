using Brutal.ImGuiApi;
using purrTTY.Display.Rendering;

namespace purrTTY.Display.Ghostty;

// Font resolution, cell-metric measurement, and the per-(family,size) ASCII
// monospace validation that gates the renderer's run batching.
public sealed partial class TerminalWindow
{
    // Cached font resolution + ASCII run-batch validation. Recomputed when the
    // family/size changes or when more fonts finish loading (variant fallbacks
    // can resolve differently once their font appears).
    private readonly GlyphBatchCache _glyphBatchCache = new();
    private FrameFonts _cachedFonts;
    private bool _hasCachedFonts;
    private string? _cachedFontFamily;
    private float _cachedFontSize;
    private int _cachedLoadedFontCount;

    private FrameFonts ResolveFontsCached(float fontSize)
    {
        int loadedCount = PurrTTYFontManager.LoadedFonts.Count;
        if (_hasCachedFonts
            && _cachedFontFamily == Settings.FontFamily
            && _cachedFontSize == fontSize
            && _cachedLoadedFontCount == loadedCount)
        {
            return _cachedFonts;
        }

        var fonts = ResolveFonts();
        ComputeCellMetrics(fonts.Regular, fontSize);
        _glyphBatchCache.Clear();
        _cachedFonts = new FrameFonts(
            fonts.Regular, fonts.Bold, fonts.Italic, fonts.BoldItalic,
            IsAsciiMonospace(fonts.Regular, fontSize, _cellWidth),
            IsAsciiMonospace(fonts.Bold, fontSize, _cellWidth),
            IsAsciiMonospace(fonts.Italic, fontSize, _cellWidth),
            IsAsciiMonospace(fonts.BoldItalic, fontSize, _cellWidth),
            _glyphBatchCache);
        _hasCachedFonts = true;
        _cachedFontFamily = Settings.FontFamily;
        _cachedFontSize = fontSize;
        _cachedLoadedFontCount = loadedCount;
        return _cachedFonts;
    }

    private void ComputeCellMetrics(ImFontPtr font, float fontSize)
    {
        ImGui.PushFont(font, fontSize);
        var size = ImGui.CalcTextSize("M");
        ImGui.PopFont();

        _cellWidth = size.X > 0.5f ? size.X : fontSize * 0.6f;
        _cellHeight = size.Y > 0.5f ? size.Y : fontSize * 1.2f;
    }

    private static readonly string AsciiSample = CreateAsciiSample();

    // Includes ' ' (0x20): run batching bridges blank cells with spaces, so the
    // space advance must be validated along with the printable glyphs.
    private static string CreateAsciiSample()
    {
        Span<char> chars = stackalloc char[95];
        for (int i = 0; i < chars.Length; i++)
        {
            chars[i] = (char)(' ' + i);
        }

        return new string(chars);
    }

    // A variant may batch ASCII runs only when its measured printable-ASCII
    // advance matches the grid cell width exactly; otherwise the renderer
    // falls back to per-cell placement to keep columns aligned.
    private static bool IsAsciiMonospace(ImFontPtr font, float fontSize, float cellWidth)
    {
        ImGui.PushFont(font, fontSize);
        var size = ImGui.CalcTextSize(AsciiSample);
        ImGui.PopFont();
        return Math.Abs(size.X - AsciiSample.Length * cellWidth) <= 0.5f;
    }

    private FrameFonts ResolveFonts()
    {
        var config = PurrTTYFontManager.CreateFontConfigForFamily(Settings.FontFamily);
        var regular = ResolveFontByName(config.RegularFontName) ?? ImGui.GetFont();
        var bold = ResolveFontByName(config.BoldFontName) ?? regular;
        var italic = ResolveFontByName(config.ItalicFontName) ?? regular;
        var boldItalic = ResolveFontByName(config.BoldItalicFontName) ?? regular;
        return new FrameFonts(regular, bold, italic, boldItalic);
    }

    private static ImFontPtr? ResolveFontByName(string? name)
        => !string.IsNullOrEmpty(name) && PurrTTYFontManager.LoadedFonts.TryGetValue(name, out var font)
            ? font
            : null;
}
