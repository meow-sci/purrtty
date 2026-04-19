using System;

namespace caTTY.Display.Configuration;

/// <summary>
///     Represents character metrics for terminal font rendering.
///     Contains dimensions and positioning information for character cells.
/// </summary>
public class CharacterMetrics
{
    /// <summary>
    ///     Gets or sets the character width in pixels.
    ///     For monospace fonts, this is the width of each character cell.
    /// </summary>
    public float Width { get; set; }

    /// <summary>
    ///     Gets or sets the character height in pixels.
    ///     This is the height of each character cell (line height).
    /// </summary>
    public float Height { get; set; }

    /// <summary>
    ///     Gets or sets the baseline offset in pixels from the top of the character cell.
    ///     This is where the baseline of text characters should be positioned.
    /// </summary>
    public float BaselineOffset { get; set; }

    /// <summary>
    ///     Gets or sets the font size used to calculate these metrics.
    /// </summary>
    public float FontSize { get; set; }

    /// <summary>
    ///     Gets or sets the font name used to calculate these metrics.
    /// </summary>
    public string FontName { get; set; } = string.Empty;

    /// <summary>
    ///     Gets the aspect ratio of the character (width / height).
    ///     For typical monospace fonts, this is usually around 0.6.
    /// </summary>
    public float AspectRatio => Height > 0 ? Width / Height : 0;

    /// <summary>
    ///     Gets the descent (distance from baseline to bottom of character cell).
    /// </summary>
    public float Descent => Height - BaselineOffset;

    /// <summary>
    ///     Gets the ascent (distance from top of character cell to baseline).
    /// </summary>
    public float Ascent => BaselineOffset;

    /// <summary>
    ///     Creates a new CharacterMetrics instance with default values.
    /// </summary>
    public CharacterMetrics()
    {
    }

    /// <summary>
    ///     Creates a new CharacterMetrics instance with specified dimensions.
    /// </summary>
    /// <param name="width">Character width in pixels</param>
    /// <param name="height">Character height in pixels</param>
    /// <param name="baselineOffset">Baseline offset from top in pixels</param>
    /// <param name="fontSize">Font size used</param>
    /// <param name="fontName">Font name used</param>
    public CharacterMetrics(float width, float height, float baselineOffset, float fontSize, string fontName)
    {
        Width = width;
        Height = height;
        BaselineOffset = baselineOffset;
        FontSize = fontSize;
        FontName = fontName ?? string.Empty;
    }

    /// <summary>
    ///     Scales the character metrics by the specified factor.
    /// </summary>
    /// <param name="scaleFactor">Scaling factor (1.0 = no change, 2.0 = double size, etc.)</param>
    /// <returns>New CharacterMetrics instance with scaled values</returns>
    public CharacterMetrics Scale(float scaleFactor)
    {
        return new CharacterMetrics
        {
            Width = Width * scaleFactor,
            Height = Height * scaleFactor,
            BaselineOffset = BaselineOffset * scaleFactor,
            FontSize = FontSize * scaleFactor,
            FontName = FontName
        };
    }

    /// <summary>
    ///     Returns a string representation of the character metrics.
    /// </summary>
    public override string ToString()
    {
        return $"CharacterMetrics(Width={Width:F1}, Height={Height:F1}, Baseline={BaselineOffset:F1}, Font={FontName} {FontSize:F1}pt)";
    }

    /// <summary>
    ///     Determines whether the specified object is equal to the current CharacterMetrics.
    /// </summary>
    public override bool Equals(object? obj)
    {
        if (obj is not CharacterMetrics other)
            return false;

        return Math.Abs(Width - other.Width) < 0.001f &&
               Math.Abs(Height - other.Height) < 0.001f &&
               Math.Abs(BaselineOffset - other.BaselineOffset) < 0.001f &&
               Math.Abs(FontSize - other.FontSize) < 0.001f &&
               FontName == other.FontName;
    }

    /// <summary>
    ///     Returns a hash code for the current CharacterMetrics.
    /// </summary>
    public override int GetHashCode()
    {
        return HashCode.Combine(Width, Height, BaselineOffset, FontSize, FontName);
    }
}