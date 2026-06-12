namespace purrTTY.Display.Configuration;

/// <summary>
/// The four font-variant names (regular/bold/italic/bold-italic) that a window
/// resolves against the loaded font atlas. Produced by
/// <c>PurrTTYFontManager.CreateFontConfigForFamily</c>. (Size is not part of
/// this: fonts are sized at draw time via PushFont.)
/// </summary>
public class TerminalFontConfig
{
    public string RegularFontName { get; set; } = "HackNerdFontMono-Regular";

    public string BoldFontName { get; set; } = "HackNerdFontMono-Bold";

    public string ItalicFontName { get; set; } = "HackNerdFontMono-Italic";

    public string BoldItalicFontName { get; set; } = "HackNerdFontMono-BoldItalic";

    /// <summary>The fallback configuration for an unknown font family (Hack).</summary>
    public static TerminalFontConfig CreateDefault() => new();
}
