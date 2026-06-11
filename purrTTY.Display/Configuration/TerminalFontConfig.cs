namespace purrTTY.Display.Configuration;

/// <summary>
/// The four font-variant names (regular/bold/italic/bold-italic) plus size that
/// a window resolves against the loaded font atlas. Produced by
/// <c>PurrTTYFontManager.CreateFontConfigForFamily</c>.
/// </summary>
public class TerminalFontConfig
{
    public string RegularFontName { get; set; } = "HackNerdFontMono-Regular";

    public string BoldFontName { get; set; } = "HackNerdFontMono-Bold";

    public string ItalicFontName { get; set; } = "HackNerdFontMono-Italic";

    public string BoldItalicFontName { get; set; } = "HackNerdFontMono-BoldItalic";

    public float FontSize { get; set; } = 32.0f;

    /// <summary>The fallback configuration for an unknown font family (Hack).</summary>
    public static TerminalFontConfig CreateDefault() => new();
}
