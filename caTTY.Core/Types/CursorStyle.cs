namespace caTTY.Core.Types;

/// <summary>
///     Cursor style enumeration for DECSCUSR (Set Cursor Style) sequence.
///     Matches the TypeScript implementation and terminal specification.
/// </summary>
public enum CursorStyle
{
    /// <summary>
    ///     Default cursor style (blinking block) - DECSCUSR parameter 0.
    ///     Maps to BlinkingBlock for compatibility.
    /// </summary>
    Default = 0,

    /// <summary>
    ///     Blinking block cursor - DECSCUSR parameter 1.
    ///     Standard block cursor that blinks on/off.
    /// </summary>
    BlinkingBlock = 1,

    /// <summary>
    ///     Steady block cursor - DECSCUSR parameter 2.
    ///     Solid block cursor that does not blink.
    /// </summary>
    SteadyBlock = 2,

    /// <summary>
    ///     Blinking underline cursor - DECSCUSR parameter 3.
    ///     Horizontal line at bottom of character cell that blinks.
    /// </summary>
    BlinkingUnderline = 3,

    /// <summary>
    ///     Steady underline cursor - DECSCUSR parameter 4.
    ///     Horizontal line at bottom of character cell that does not blink.
    /// </summary>
    SteadyUnderline = 4,

    /// <summary>
    ///     Blinking bar cursor - DECSCUSR parameter 5.
    ///     Vertical line at left edge of character cell that blinks.
    /// </summary>
    BlinkingBar = 5,

    /// <summary>
    ///     Steady bar cursor - DECSCUSR parameter 6.
    ///     Vertical line at left edge of character cell that does not blink.
    /// </summary>
    SteadyBar = 6
}

/// <summary>
///     Cursor shape enumeration for rendering purposes.
/// </summary>
public enum CursorShape
{
    /// <summary>
    ///     Block cursor - fills entire character cell.
    /// </summary>
    Block,

    /// <summary>
    ///     Hollow block cursor - outline of character cell.
    /// </summary>
    BlockHollow,

    /// <summary>
    ///     Underline cursor - horizontal line at bottom.
    /// </summary>
    Underline,

    /// <summary>
    ///     Bar cursor - vertical line at left edge.
    /// </summary>
    Bar
}

/// <summary>
///     Utility methods for cursor style handling.
/// </summary>
public static class CursorStyleExtensions
{
    /// <summary>
    ///     Validates and normalizes a DECSCUSR cursor style parameter.
    ///     Matches the TypeScript validateCursorStyle function behavior.
    /// </summary>
    /// <param name="style">Raw cursor style parameter from DECSCUSR sequence</param>
    /// <returns>Validated cursor style, defaults to BlinkingBlock for invalid values</returns>
    public static CursorStyle ValidateStyle(int style)
    {
        // Validate style is a non-negative integer (matches TypeScript)
        if (style < 0)
        {
            return CursorStyle.Default; // Maps to BlinkingBlock
        }

        // Valid cursor styles are 0-6 (matches TypeScript)
        if (style > 6)
        {
            return CursorStyle.Default; // Maps to BlinkingBlock
        }

        return (CursorStyle)style;
    }

    /// <summary>
    ///     Determines if the cursor should blink based on its style.
    /// </summary>
    /// <param name="style">The cursor style</param>
    /// <returns>True if the cursor should blink</returns>
    public static bool ShouldBlink(this CursorStyle style)
    {
        return style switch
        {
            CursorStyle.Default => true,        // Default is blinking
            CursorStyle.BlinkingBlock => true,
            CursorStyle.SteadyBlock => false,
            CursorStyle.BlinkingUnderline => true,
            CursorStyle.SteadyUnderline => false,
            CursorStyle.BlinkingBar => true,
            CursorStyle.SteadyBar => false,
            _ => true // Fallback to blinking
        };
    }

    /// <summary>
    ///     Gets the cursor shape for rendering purposes.
    /// </summary>
    /// <param name="style">The cursor style</param>
    /// <returns>The cursor shape to render</returns>
    public static CursorShape GetShape(this CursorStyle style)
    {
        return style switch
        {
            CursorStyle.Default => CursorShape.Block,
            CursorStyle.BlinkingBlock => CursorShape.Block,
            CursorStyle.SteadyBlock => CursorShape.Block,
            CursorStyle.BlinkingUnderline => CursorShape.Underline,
            CursorStyle.SteadyUnderline => CursorShape.Underline,
            CursorStyle.BlinkingBar => CursorShape.Bar,
            CursorStyle.SteadyBar => CursorShape.Bar,
            _ => CursorShape.Block // Fallback to block
        };
    }

    /// <summary>
    ///     Converts cursor style to integer for compatibility with existing code.
    /// </summary>
    /// <param name="style">The cursor style</param>
    /// <returns>Integer representation of the cursor style</returns>
    public static int ToInt(this CursorStyle style)
    {
        return (int)style;
    }

    /// <summary>
    ///     Converts integer to cursor style with validation.
    /// </summary>
    /// <param name="value">Integer value to convert</param>
    /// <returns>Validated cursor style</returns>
    public static CursorStyle FromInt(int value)
    {
        return ValidateStyle(value);
    }
}