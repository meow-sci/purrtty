using System;

namespace caTTY.Display.Configuration;

/// <summary>
/// Configuration class that encapsulates all font-related settings for terminal rendering.
/// Supports different font variants for regular, bold, italic, and bold+italic text styling.
/// </summary>
public class TerminalFontConfig
{
    /// <summary>
    /// Gets or sets the font name for regular text rendering.
    /// </summary>
    /// <value>The font name for regular text. Defaults to "HackNerdFontMono-Regular".</value>
    public string RegularFontName { get; set; } = "HackNerdFontMono-Regular";

    /// <summary>
    /// Gets or sets the font name for bold text rendering.
    /// </summary>
    /// <value>The font name for bold text. Defaults to "HackNerdFontMono-Bold".</value>
    public string BoldFontName { get; set; } = "HackNerdFontMono-Bold";

    /// <summary>
    /// Gets or sets the font name for italic text rendering.
    /// </summary>
    /// <value>The font name for italic text. Defaults to "HackNerdFontMono-Italic".</value>
    public string ItalicFontName { get; set; } = "HackNerdFontMono-Italic";

    /// <summary>
    /// Gets or sets the font name for bold+italic text rendering.
    /// </summary>
    /// <value>The font name for bold+italic text. Defaults to "HackNerdFontMono-BoldItalic".</value>
    public string BoldItalicFontName { get; set; } = "HackNerdFontMono-BoldItalic";

    /// <summary>
    /// Gets or sets the font size in points.
    /// </summary>
    /// <value>The font size in points. Must be between 8.0f and 72.0f. Defaults to 16.0f.</value>
    public float FontSize { get; set; } = 16.0f;

    /// <summary>
    /// Gets or sets whether to automatically detect the execution context and apply appropriate font settings.
    /// </summary>
    /// <value>True to enable automatic context detection, false to use explicit configuration. Defaults to true.</value>
    public bool AutoDetectContext { get; set; } = true;

    /// <summary>
    /// Creates a font configuration optimized for TestApp development context.
    /// Uses development-friendly font defaults with larger size for better readability.
    /// </summary>
    /// <returns>A TerminalFontConfig instance configured for TestApp usage.</returns>
    public static TerminalFontConfig CreateForTestApp()
    {
        return new TerminalFontConfig
        {
            RegularFontName = "HackNerdFontMono-Regular",
            BoldFontName = "HackNerdFontMono-Bold",
            ItalicFontName = "HackNerdFontMono-Italic",
            BoldItalicFontName = "HackNerdFontMono-BoldItalic",
            FontSize = 32.0f,
            AutoDetectContext = false
        };
    }

    /// <summary>
    /// Creates a font configuration optimized for GameMod context.
    /// Uses game-appropriate font defaults with slightly smaller size for game integration.
    /// </summary>
    /// <returns>A TerminalFontConfig instance configured for GameMod usage.</returns>
    public static TerminalFontConfig CreateForGameMod()
    {
        return new TerminalFontConfig
        {
            RegularFontName = "HackNerdFontMono-Regular",
            BoldFontName = "HackNerdFontMono-Bold",
            ItalicFontName = "HackNerdFontMono-Italic",
            BoldItalicFontName = "HackNerdFontMono-BoldItalic",
            FontSize = 32.0f, // Slightly smaller for game context
            AutoDetectContext = false
        };
    }

    /// <summary>
    /// Validates the font configuration and ensures all required properties are set correctly.
    /// Performs bounds checking for font size and null checking for font names.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when RegularFontName is null or empty, or when FontSize is outside valid bounds.</exception>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(RegularFontName))
            throw new ArgumentException("RegularFontName cannot be null or empty", nameof(RegularFontName));

        if (FontSize <= 0 || FontSize > 72)
            throw new ArgumentException("FontSize must be between 0 and 72", nameof(FontSize));

        // Bold, Italic, BoldItalic can fall back to Regular if not specified or empty
        if (string.IsNullOrWhiteSpace(BoldFontName))
            BoldFontName = RegularFontName;
        if (string.IsNullOrWhiteSpace(ItalicFontName))
            ItalicFontName = RegularFontName;
        if (string.IsNullOrWhiteSpace(BoldItalicFontName))
            BoldItalicFontName = RegularFontName;
    }

    /// <summary>
    ///     Calculates character metrics for the configured font.
    ///     Returns estimated metrics based on font size if actual font metrics are unavailable.
    /// </summary>
    /// <returns>Character metrics including width, height, and baseline offset</returns>
    public CharacterMetrics CalculateCharacterMetrics()
    {
        // For monospace fonts, character width is typically 60% of font size
        // Character height is typically equal to font size
        // These are reasonable estimates for terminal fonts
        var characterWidth = FontSize * 0.6f;
        var characterHeight = FontSize;
        var baselineOffset = FontSize * 0.8f; // Baseline is typically 80% down from top
        
        return new CharacterMetrics
        {
            Width = characterWidth,
            Height = characterHeight,
            BaselineOffset = baselineOffset,
            FontSize = FontSize,
            FontName = RegularFontName
        };
    }

    /// <summary>
    ///     Calculates character metrics with DPI scaling applied.
    /// </summary>
    /// <param name="dpiScale">DPI scaling factor (1.0 = 100%, 1.5 = 150%, etc.)</param>
    /// <returns>Scaled character metrics</returns>
    public CharacterMetrics CalculateScaledCharacterMetrics(float dpiScale = 1.0f)
    {
        var baseMetrics = CalculateCharacterMetrics();
        
        return new CharacterMetrics
        {
            Width = baseMetrics.Width * dpiScale,
            Height = baseMetrics.Height * dpiScale,
            BaselineOffset = baseMetrics.BaselineOffset * dpiScale,
            FontSize = baseMetrics.FontSize * dpiScale,
            FontName = baseMetrics.FontName
        };
    }
}