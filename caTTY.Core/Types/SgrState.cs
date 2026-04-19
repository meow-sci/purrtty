using System;

namespace caTTY.Core.Types;

/// <summary>
///     Represents the current SGR state for a terminal cell.
///     This is a mutable state class that tracks all SGR attributes.
///     Based on the TypeScript SgrState interface.
/// </summary>
public class SgrState : IEquatable<SgrState>
{
    /// <summary>
    ///     Bold or increased intensity.
    /// </summary>
    public bool Bold { get; set; }

    /// <summary>
    ///     Faint, decreased intensity, or dim.
    /// </summary>
    public bool Faint { get; set; }

    /// <summary>
    ///     Italic text.
    /// </summary>
    public bool Italic { get; set; }

    /// <summary>
    ///     Underline text.
    /// </summary>
    public bool Underline { get; set; }

    /// <summary>
    ///     Underline style (single, double, curly, etc.).
    /// </summary>
    public UnderlineStyle? UnderlineStyle { get; set; }

    /// <summary>
    ///     Blinking text.
    /// </summary>
    public bool Blink { get; set; }

    /// <summary>
    ///     Reverse video / inverse colors.
    /// </summary>
    public bool Inverse { get; set; }

    /// <summary>
    ///     Hidden / concealed text.
    /// </summary>
    public bool Hidden { get; set; }

    /// <summary>
    ///     Strikethrough / crossed-out text.
    /// </summary>
    public bool Strikethrough { get; set; }

    /// <summary>
    ///     Foreground color. Null means use default.
    /// </summary>
    public Color? ForegroundColor { get; set; }

    /// <summary>
    ///     Background color. Null means use default.
    /// </summary>
    public Color? BackgroundColor { get; set; }

    /// <summary>
    ///     Underline color. Null means use foreground color.
    /// </summary>
    public Color? UnderlineColor { get; set; }

    /// <summary>
    ///     Font selection (0 = primary, 1-9 = alternative fonts).
    /// </summary>
    public int Font { get; set; }

    /// <summary>
    ///     Creates a new SGR state with default values.
    /// </summary>
    public SgrState()
    {
        Reset();
    }

    /// <summary>
    ///     Creates a new SGR state by copying from another state.
    /// </summary>
    /// <param name="other">The state to copy from</param>
    public SgrState(SgrState other)
    {
        Bold = other.Bold;
        Faint = other.Faint;
        Italic = other.Italic;
        Underline = other.Underline;
        UnderlineStyle = other.UnderlineStyle;
        Blink = other.Blink;
        Inverse = other.Inverse;
        Hidden = other.Hidden;
        Strikethrough = other.Strikethrough;
        ForegroundColor = other.ForegroundColor;
        BackgroundColor = other.BackgroundColor;
        UnderlineColor = other.UnderlineColor;
        Font = other.Font;
    }

    /// <summary>
    ///     Resets all attributes to their default values.
    /// </summary>
    public void Reset()
    {
        Bold = false;
        Faint = false;
        Italic = false;
        Underline = false;
        UnderlineStyle = null;
        Blink = false;
        Inverse = false;
        Hidden = false;
        Strikethrough = false;
        ForegroundColor = null;
        BackgroundColor = null;
        UnderlineColor = null;
        Font = 0;
    }

    /// <summary>
    ///     Creates a default/reset SGR state.
    /// </summary>
    /// <returns>A new SGR state with default values</returns>
    public static SgrState CreateDefault()
    {
        return new SgrState();
    }

    /// <summary>
    ///     Converts this SGR state to immutable SGR attributes.
    /// </summary>
    /// <returns>Immutable SGR attributes representing this state</returns>
    public SgrAttributes ToSgrAttributes()
    {
        return new SgrAttributes(
            bold: Bold,
            faint: Faint,
            italic: Italic,
            underline: Underline,
            underlineStyle: UnderlineStyle ?? Types.UnderlineStyle.None,
            blink: Blink,
            inverse: Inverse,
            hidden: Hidden,
            strikethrough: Strikethrough,
            foregroundColor: ForegroundColor,
            backgroundColor: BackgroundColor,
            underlineColor: UnderlineColor,
            font: Font);
    }

    /// <summary>
    ///     Creates an SGR state from immutable SGR attributes.
    /// </summary>
    /// <param name="attributes">The SGR attributes to convert</param>
    /// <returns>A new SGR state with the same values</returns>
    public static SgrState FromSgrAttributes(SgrAttributes attributes)
    {
        return new SgrState
        {
            Bold = attributes.Bold,
            Faint = attributes.Faint,
            Italic = attributes.Italic,
            Underline = attributes.Underline,
            UnderlineStyle = attributes.UnderlineStyle == Types.UnderlineStyle.None ? null : attributes.UnderlineStyle,
            Blink = attributes.Blink,
            Inverse = attributes.Inverse,
            Hidden = attributes.Hidden,
            Strikethrough = attributes.Strikethrough,
            ForegroundColor = attributes.ForegroundColor,
            BackgroundColor = attributes.BackgroundColor,
            UnderlineColor = attributes.UnderlineColor,
            Font = attributes.Font
        };
    }

    /// <summary>
    ///     Determines whether the specified SgrState is equal to the current SgrState.
    /// </summary>
    public bool Equals(SgrState? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;

        return Bold == other.Bold &&
               Faint == other.Faint &&
               Italic == other.Italic &&
               Underline == other.Underline &&
               UnderlineStyle == other.UnderlineStyle &&
               Blink == other.Blink &&
               Inverse == other.Inverse &&
               Hidden == other.Hidden &&
               Strikethrough == other.Strikethrough &&
               Equals(ForegroundColor, other.ForegroundColor) &&
               Equals(BackgroundColor, other.BackgroundColor) &&
               Equals(UnderlineColor, other.UnderlineColor) &&
               Font == other.Font;
    }

    /// <summary>
    ///     Determines whether the specified object is equal to the current SgrState.
    /// </summary>
    public override bool Equals(object? obj)
    {
        return obj is SgrState other && Equals(other);
    }

    /// <summary>
    ///     Returns the hash code for this SgrState.
    /// </summary>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Bold);
        hash.Add(Faint);
        hash.Add(Italic);
        hash.Add(Underline);
        hash.Add(UnderlineStyle);
        hash.Add(Blink);
        hash.Add(Inverse);
        hash.Add(Hidden);
        hash.Add(Strikethrough);
        hash.Add(ForegroundColor);
        hash.Add(BackgroundColor);
        hash.Add(UnderlineColor);
        hash.Add(Font);
        return hash.ToHashCode();
    }

    /// <summary>
    ///     Returns a string representation of the SgrState.
    /// </summary>
    public override string ToString()
    {
        var parts = new List<string>();

        if (Bold) parts.Add("Bold");
        if (Faint) parts.Add("Faint");
        if (Italic) parts.Add("Italic");
        if (Underline) parts.Add($"Underline({UnderlineStyle})");
        if (Blink) parts.Add("Blink");
        if (Inverse) parts.Add("Inverse");
        if (Hidden) parts.Add("Hidden");
        if (Strikethrough) parts.Add("Strikethrough");
        if (ForegroundColor.HasValue) parts.Add($"FG({ForegroundColor})");
        if (BackgroundColor.HasValue) parts.Add($"BG({BackgroundColor})");
        if (UnderlineColor.HasValue) parts.Add($"UL({UnderlineColor})");
        if (Font != 0) parts.Add($"Font({Font})");

        return parts.Count > 0 ? string.Join(", ", parts) : "Default";
    }
}