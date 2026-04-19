namespace caTTY.Core.Types;

/// <summary>
///     Represents the color type for SGR (Select Graphic Rendition) attributes.
///     Supports named colors, indexed colors (0-255), and RGB colors.
/// </summary>
public readonly struct Color : IEquatable<Color>
{
    /// <summary>
    ///     The type of color representation.
    /// </summary>
    public ColorType Type { get; }

    /// <summary>
    ///     Named color value (only valid when Type is Named).
    /// </summary>
    public NamedColor NamedColor { get; }

    /// <summary>
    ///     Color index (only valid when Type is Indexed).
    /// </summary>
    public byte Index { get; }

    /// <summary>
    ///     Red component (only valid when Type is Rgb).
    /// </summary>
    public byte Red { get; }

    /// <summary>
    ///     Green component (only valid when Type is Rgb).
    /// </summary>
    public byte Green { get; }

    /// <summary>
    ///     Blue component (only valid when Type is Rgb).
    /// </summary>
    public byte Blue { get; }

    /// <summary>
    ///     Creates a named color.
    /// </summary>
    /// <param name="namedColor">The named color</param>
    public Color(NamedColor namedColor)
    {
        Type = ColorType.Named;
        NamedColor = namedColor;
        Index = 0;
        Red = 0;
        Green = 0;
        Blue = 0;
    }

    /// <summary>
    ///     Creates an indexed color (0-255).
    /// </summary>
    /// <param name="index">The color index</param>
    public Color(byte index)
    {
        Type = ColorType.Indexed;
        NamedColor = NamedColor.Black;
        Index = index;
        Red = 0;
        Green = 0;
        Blue = 0;
    }

    /// <summary>
    ///     Creates an RGB color.
    /// </summary>
    /// <param name="red">Red component (0-255)</param>
    /// <param name="green">Green component (0-255)</param>
    /// <param name="blue">Blue component (0-255)</param>
    public Color(byte red, byte green, byte blue)
    {
        Type = ColorType.Rgb;
        NamedColor = NamedColor.Black;
        Index = 0;
        Red = red;
        Green = green;
        Blue = blue;
    }

    /// <summary>
    ///     Determines whether the specified Color is equal to the current Color.
    /// </summary>
    public bool Equals(Color other)
    {
        return Type == other.Type &&
               NamedColor == other.NamedColor &&
               Index == other.Index &&
               Red == other.Red &&
               Green == other.Green &&
               Blue == other.Blue;
    }

    /// <summary>
    ///     Determines whether the specified object is equal to the current Color.
    /// </summary>
    public override bool Equals(object? obj)
    {
        return obj is Color other && Equals(other);
    }

    /// <summary>
    ///     Returns the hash code for this Color.
    /// </summary>
    public override int GetHashCode()
    {
        return HashCode.Combine(Type, NamedColor, Index, Red, Green, Blue);
    }

    /// <summary>
    ///     Determines whether two Color instances are equal.
    /// </summary>
    public static bool operator ==(Color left, Color right)
    {
        return left.Equals(right);
    }

    /// <summary>
    ///     Determines whether two Color instances are not equal.
    /// </summary>
    public static bool operator !=(Color left, Color right)
    {
        return !left.Equals(right);
    }

    /// <summary>
    ///     Returns a string representation of the Color.
    /// </summary>
    public override string ToString()
    {
        return Type switch
        {
            ColorType.Named => $"Named({NamedColor})",
            ColorType.Indexed => $"Indexed({Index})",
            ColorType.Rgb => $"RGB({Red},{Green},{Blue})",
            _ => "Unknown"
        };
    }
}

/// <summary>
///     Specifies the type of color representation.
/// </summary>
public enum ColorType
{
    /// <summary>
    ///     Named color (e.g., red, blue, brightWhite).
    /// </summary>
    Named,

    /// <summary>
    ///     Indexed color (0-255 palette).
    /// </summary>
    Indexed,

    /// <summary>
    ///     RGB color with red, green, and blue components.
    /// </summary>
    Rgb
}

/// <summary>
///     Named colors for SGR 30-37, 40-47, 90-97, 100-107.
/// </summary>
public enum NamedColor
{
    /// <summary>
    ///     Black color (SGR 30/40).
    /// </summary>
    Black,

    /// <summary>
    ///     Red color (SGR 31/41).
    /// </summary>
    Red,

    /// <summary>
    ///     Green color (SGR 32/42).
    /// </summary>
    Green,

    /// <summary>
    ///     Yellow color (SGR 33/43).
    /// </summary>
    Yellow,

    /// <summary>
    ///     Blue color (SGR 34/44).
    /// </summary>
    Blue,

    /// <summary>
    ///     Magenta color (SGR 35/45).
    /// </summary>
    Magenta,

    /// <summary>
    ///     Cyan color (SGR 36/46).
    /// </summary>
    Cyan,

    /// <summary>
    ///     White color (SGR 37/47).
    /// </summary>
    White,

    /// <summary>
    ///     Bright black color (SGR 90/100).
    /// </summary>
    BrightBlack,

    /// <summary>
    ///     Bright red color (SGR 91/101).
    /// </summary>
    BrightRed,

    /// <summary>
    ///     Bright green color (SGR 92/102).
    /// </summary>
    BrightGreen,

    /// <summary>
    ///     Bright yellow color (SGR 93/103).
    /// </summary>
    BrightYellow,

    /// <summary>
    ///     Bright blue color (SGR 94/104).
    /// </summary>
    BrightBlue,

    /// <summary>
    ///     Bright magenta color (SGR 95/105).
    /// </summary>
    BrightMagenta,

    /// <summary>
    ///     Bright cyan color (SGR 96/106).
    /// </summary>
    BrightCyan,

    /// <summary>
    ///     Bright white color (SGR 97/107).
    /// </summary>
    BrightWhite
}

/// <summary>
///     Underline style for SGR underline attributes.
/// </summary>
public enum UnderlineStyle
{
    /// <summary>
    ///     No underline.
    /// </summary>
    None,

    /// <summary>
    ///     Single underline (SGR 4).
    /// </summary>
    Single,

    /// <summary>
    ///     Double underline (SGR 21).
    /// </summary>
    Double,

    /// <summary>
    ///     Curly underline (SGR 4:3).
    /// </summary>
    Curly,

    /// <summary>
    ///     Dotted underline (SGR 4:4).
    /// </summary>
    Dotted,

    /// <summary>
    ///     Dashed underline (SGR 4:5).
    /// </summary>
    Dashed
}

/// <summary>
///     SGR (Select Graphic Rendition) attributes for text styling.
///     This struct represents the complete set of text attributes that can be applied to terminal cells.
/// </summary>
public readonly struct SgrAttributes : IEquatable<SgrAttributes>
{
    /// <summary>
    ///     Bold or increased intensity.
    /// </summary>
    public bool Bold { get; }

    /// <summary>
    ///     Faint, decreased intensity, or dim.
    /// </summary>
    public bool Faint { get; }

    /// <summary>
    ///     Italic text.
    /// </summary>
    public bool Italic { get; }

    /// <summary>
    ///     Underline text.
    /// </summary>
    public bool Underline { get; }

    /// <summary>
    ///     Underline style (single, double, curly, etc.).
    /// </summary>
    public UnderlineStyle UnderlineStyle { get; }

    /// <summary>
    ///     Blinking text.
    /// </summary>
    public bool Blink { get; }

    /// <summary>
    ///     Reverse video / inverse colors.
    /// </summary>
    public bool Inverse { get; }

    /// <summary>
    ///     Hidden / concealed text.
    /// </summary>
    public bool Hidden { get; }

    /// <summary>
    ///     Strikethrough / crossed-out text.
    /// </summary>
    public bool Strikethrough { get; }

    /// <summary>
    ///     Foreground color. Null means use default.
    /// </summary>
    public Color? ForegroundColor { get; }

    /// <summary>
    ///     Background color. Null means use default.
    /// </summary>
    public Color? BackgroundColor { get; }

    /// <summary>
    ///     Underline color. Null means use foreground color.
    /// </summary>
    public Color? UnderlineColor { get; }

    /// <summary>
    ///     Font selection (0 = primary, 1-9 = alternative fonts).
    /// </summary>
    public int Font { get; }

    /// <summary>
    ///     Creates SGR attributes with the specified values.
    /// </summary>
    public SgrAttributes(
        bool bold = false,
        bool faint = false,
        bool italic = false,
        bool underline = false,
        UnderlineStyle underlineStyle = UnderlineStyle.None,
        bool blink = false,
        bool inverse = false,
        bool hidden = false,
        bool strikethrough = false,
        Color? foregroundColor = null,
        Color? backgroundColor = null,
        Color? underlineColor = null,
        int font = 0)
    {
        Bold = bold;
        Faint = faint;
        Italic = italic;
        Underline = underline;
        UnderlineStyle = underlineStyle;
        Blink = blink;
        Inverse = inverse;
        Hidden = hidden;
        Strikethrough = strikethrough;
        ForegroundColor = foregroundColor;
        BackgroundColor = backgroundColor;
        UnderlineColor = underlineColor;
        Font = font;
    }

    /// <summary>
    ///     Gets the default SGR attributes (no styling).
    /// </summary>
    public static SgrAttributes Default => new();

    /// <summary>
    ///     Returns true if these attributes are completely default (no styling that requires rendering).
    ///     Used for early-exit optimization in render loop.
    /// </summary>
    public bool IsDefault =>
        !Bold &&
        !Faint &&
        !Italic &&
        !Underline &&
        !Blink &&
        !Inverse &&
        !Hidden &&
        !Strikethrough &&
        !ForegroundColor.HasValue &&
        !BackgroundColor.HasValue &&
        !UnderlineColor.HasValue;

    /// <summary>
    ///     Determines whether the specified SgrAttributes is equal to the current SgrAttributes.
    /// </summary>
    public bool Equals(SgrAttributes other)
    {
        return Bold == other.Bold &&
               Faint == other.Faint &&
               Italic == other.Italic &&
               Underline == other.Underline &&
               UnderlineStyle == other.UnderlineStyle &&
               Blink == other.Blink &&
               Inverse == other.Inverse &&
               Hidden == other.Hidden &&
               Strikethrough == other.Strikethrough &&
               Nullable.Equals(ForegroundColor, other.ForegroundColor) &&
               Nullable.Equals(BackgroundColor, other.BackgroundColor) &&
               Nullable.Equals(UnderlineColor, other.UnderlineColor) &&
               Font == other.Font;
    }

    /// <summary>
    ///     Determines whether the specified object is equal to the current SgrAttributes.
    /// </summary>
    public override bool Equals(object? obj)
    {
        return obj is SgrAttributes other && Equals(other);
    }

    /// <summary>
    ///     Returns the hash code for this SgrAttributes.
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
    ///     Determines whether two SgrAttributes instances are equal.
    /// </summary>
    public static bool operator ==(SgrAttributes left, SgrAttributes right)
    {
        return left.Equals(right);
    }

    /// <summary>
    ///     Determines whether two SgrAttributes instances are not equal.
    /// </summary>
    public static bool operator !=(SgrAttributes left, SgrAttributes right)
    {
        return !left.Equals(right);
    }

    /// <summary>
    ///     Returns a string representation of the SgrAttributes.
    /// </summary>
    public override string ToString()
    {
        var parts = new List<string>();

        if (Bold)
        {
            parts.Add("Bold");
        }

        if (Faint)
        {
            parts.Add("Faint");
        }

        if (Italic)
        {
            parts.Add("Italic");
        }

        if (Underline)
        {
            parts.Add($"Underline({UnderlineStyle})");
        }

        if (Blink)
        {
            parts.Add("Blink");
        }

        if (Inverse)
        {
            parts.Add("Inverse");
        }

        if (Hidden)
        {
            parts.Add("Hidden");
        }

        if (Strikethrough)
        {
            parts.Add("Strikethrough");
        }

        if (ForegroundColor.HasValue)
        {
            parts.Add($"FG({ForegroundColor})");
        }

        if (BackgroundColor.HasValue)
        {
            parts.Add($"BG({BackgroundColor})");
        }

        if (UnderlineColor.HasValue)
        {
            parts.Add($"UL({UnderlineColor})");
        }

        if (Font != 0)
        {
            parts.Add($"Font({Font})");
        }

        return parts.Count > 0 ? string.Join(", ", parts) : "Default";
    }
}
