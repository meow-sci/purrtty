namespace caTTY.Core.Types;

/// <summary>
///     Represents a single character cell in the terminal screen buffer.
///     Contains the character, its associated SGR attributes, and protection status.
/// </summary>
public readonly struct Cell : IEquatable<Cell>
{
    /// <summary>
    ///     The character stored in this cell. Default is space character.
    /// </summary>
    public char Character { get; }

    /// <summary>
    ///     The SGR attributes applied to this cell.
    /// </summary>
    public SgrAttributes Attributes { get; }

    /// <summary>
    ///     Whether this cell is protected from selective erase operations (DECSCA).
    /// </summary>
    public bool IsProtected { get; }

    /// <summary>
    ///     Hyperlink URL associated with this cell (OSC 8 sequences). Null if no hyperlink.
    /// </summary>
    public string? HyperlinkUrl { get; }

    /// <summary>
    ///     Whether this character is wide (CJK characters that occupy two cells).
    /// </summary>
    public bool IsWide { get; }

    /// <summary>
    ///     Creates a new cell with the specified character and default attributes.
    /// </summary>
    /// <param name="character">The character to store in this cell</param>
    public Cell(char character) : this(character, SgrAttributes.Default, false, null, false)
    {
    }

    /// <summary>
    ///     Creates a new cell with the specified character and attributes.
    /// </summary>
    /// <param name="character">The character to store in this cell</param>
    /// <param name="attributes">The SGR attributes for this cell</param>
    public Cell(char character, SgrAttributes attributes) : this(character, attributes, false, null, false)
    {
    }

    /// <summary>
    ///     Creates a new cell with the specified character, attributes, and protection status.
    /// </summary>
    /// <param name="character">The character to store in this cell</param>
    /// <param name="attributes">The SGR attributes for this cell</param>
    /// <param name="isProtected">Whether this cell is protected from selective erase</param>
    public Cell(char character, SgrAttributes attributes, bool isProtected) : this(character, attributes, isProtected, null, false)
    {
    }

    /// <summary>
    ///     Creates a new cell with all properties specified.
    /// </summary>
    /// <param name="character">The character to store in this cell</param>
    /// <param name="attributes">The SGR attributes for this cell</param>
    /// <param name="isProtected">Whether this cell is protected from selective erase</param>
    /// <param name="hyperlinkUrl">Hyperlink URL associated with this cell</param>
    /// <param name="isWide">Whether this character is wide (CJK)</param>
    public Cell(char character, SgrAttributes attributes, bool isProtected, string? hyperlinkUrl, bool isWide)
    {
        Character = character;
        Attributes = attributes;
        IsProtected = isProtected;
        HyperlinkUrl = hyperlinkUrl;
        IsWide = isWide;
    }

    /// <summary>
    ///     Gets the default empty cell (space character with default attributes).
    ///     This represents both "unset" and "space" - we treat them the same.
    /// </summary>
    public static Cell Empty => new(' ', SgrAttributes.Default, false, null, false);

    /// <summary>
    ///     Creates a cell with a space character and default attributes.
    /// </summary>
    public static Cell Space => new(' ', SgrAttributes.Default, false, null, false);

    /// <summary>
    ///     Determines whether the specified Cell is equal to the current Cell.
    /// </summary>
    /// <param name="other">The Cell to compare with the current Cell</param>
    /// <returns>True if the specified Cell is equal to the current Cell; otherwise, false</returns>
    public bool Equals(Cell other)
    {
        return Character == other.Character && 
               Attributes.Equals(other.Attributes) && 
               IsProtected == other.IsProtected &&
               HyperlinkUrl == other.HyperlinkUrl &&
               IsWide == other.IsWide;
    }

    /// <summary>
    ///     Determines whether the specified object is equal to the current Cell.
    /// </summary>
    /// <param name="obj">The object to compare with the current Cell</param>
    /// <returns>True if the specified object is equal to the current Cell; otherwise, false</returns>
    public override bool Equals(object? obj)
    {
        return obj is Cell other && Equals(other);
    }

    /// <summary>
    ///     Returns the hash code for this Cell.
    /// </summary>
    /// <returns>A 32-bit signed integer hash code</returns>
    public override int GetHashCode()
    {
        return HashCode.Combine(Character, Attributes, IsProtected, HyperlinkUrl, IsWide);
    }

    /// <summary>
    ///     Determines whether two Cell instances are equal.
    /// </summary>
    /// <param name="left">The first Cell to compare</param>
    /// <param name="right">The second Cell to compare</param>
    /// <returns>True if the Cell instances are equal; otherwise, false</returns>
    public static bool operator ==(Cell left, Cell right)
    {
        return left.Equals(right);
    }

    /// <summary>
    ///     Determines whether two Cell instances are not equal.
    /// </summary>
    /// <param name="left">The first Cell to compare</param>
    /// <param name="right">The second Cell to compare</param>
    /// <returns>True if the Cell instances are not equal; otherwise, false</returns>
    public static bool operator !=(Cell left, Cell right)
    {
        return !left.Equals(right);
    }

    /// <summary>
    ///     Returns a string representation of the Cell.
    /// </summary>
    /// <returns>A string that represents the current Cell</returns>
    public override string ToString()
    {
        var parts = new List<string> { $"'{Character}'" };
        
        if (Attributes != SgrAttributes.Default)
        {
            parts.Add(Attributes.ToString());
        }
        
        if (IsProtected)
        {
            parts.Add("Protected");
        }
        
        if (HyperlinkUrl != null)
        {
            parts.Add($"Link({HyperlinkUrl})");
        }
        
        if (IsWide)
        {
            parts.Add("Wide");
        }
        
        return $"Cell({string.Join(", ", parts)})";
    }
}
