using System;
using caTTY.Core.Types;

namespace caTTY.Display.Types;

/// <summary>
/// Represents a text selection in the terminal with start and end positions.
/// Supports both single-line and multi-line selections.
/// </summary>
public readonly struct TextSelection : IEquatable<TextSelection>
{
    /// <summary>
    /// Gets the start position of the selection (inclusive).
    /// </summary>
    public SelectionPosition Start { get; }

    /// <summary>
    /// Gets the end position of the selection (inclusive).
    /// </summary>
    public SelectionPosition End { get; }

    /// <summary>
    /// Gets whether this selection is empty (start equals end).
    /// </summary>
    public bool IsEmpty => Start.Equals(End);

    /// <summary>
    /// Gets whether this selection spans multiple rows.
    /// </summary>
    public bool IsMultiLine => Start.Row != End.Row;

    /// <summary>
    /// Creates a new text selection with the specified start and end positions.
    /// The positions will be normalized so that Start is always before or equal to End.
    /// </summary>
    /// <param name="start">The start position of the selection</param>
    /// <param name="end">The end position of the selection</param>
    public TextSelection(SelectionPosition start, SelectionPosition end)
    {
        // Normalize positions so Start is always before or equal to End
        if (start.CompareTo(end) <= 0)
        {
            Start = start;
            End = end;
        }
        else
        {
            Start = end;
            End = start;
        }
    }

    /// <summary>
    /// Creates a new text selection from row/column coordinates.
    /// </summary>
    /// <param name="startRow">The start row (0-based)</param>
    /// <param name="startCol">The start column (0-based)</param>
    /// <param name="endRow">The end row (0-based)</param>
    /// <param name="endCol">The end column (0-based)</param>
    /// <returns>A normalized text selection</returns>
    public static TextSelection FromCoordinates(int startRow, int startCol, int endRow, int endCol)
    {
        return new TextSelection(
            new SelectionPosition(startRow, startCol),
            new SelectionPosition(endRow, endCol)
        );
    }

    /// <summary>
    /// Creates an empty selection at the specified position.
    /// </summary>
    /// <param name="row">The row position</param>
    /// <param name="col">The column position</param>
    /// <returns>An empty selection at the specified position</returns>
    public static TextSelection Empty(int row, int col)
    {
        var pos = new SelectionPosition(row, col);
        return new TextSelection(pos, pos);
    }

    /// <summary>
    /// Gets an empty selection at position (0, 0).
    /// </summary>
    public static TextSelection None => new(new SelectionPosition(0, 0), new SelectionPosition(0, 0));

    /// <summary>
    /// Determines whether the specified position is within this selection.
    /// </summary>
    /// <param name="position">The position to check</param>
    /// <returns>True if the position is within the selection, false otherwise</returns>
    public bool Contains(SelectionPosition position)
    {
        return position.CompareTo(Start) >= 0 && position.CompareTo(End) <= 0;
    }

    /// <summary>
    /// Determines whether the specified row/column is within this selection.
    /// </summary>
    /// <param name="row">The row to check (0-based)</param>
    /// <param name="col">The column to check (0-based)</param>
    /// <returns>True if the position is within the selection, false otherwise</returns>
    public bool Contains(int row, int col)
    {
        return Contains(new SelectionPosition(row, col));
    }

    /// <summary>
    /// Returns true if the given row might contain selected cells.
    /// This is a fast check that avoids per-cell Contains() calls for rows
    /// entirely outside the selection range.
    /// </summary>
    /// <param name="row">The row to check (0-based)</param>
    /// <returns>True if the row might contain selected cells, false if definitely not</returns>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public bool RowMightBeSelected(int row)
    {
        if (IsEmpty) return false;
        return row >= Start.Row && row <= End.Row;
    }

    /// <summary>
    /// Extends this selection to include the specified position.
    /// </summary>
    /// <param name="position">The position to extend to</param>
    /// <returns>A new selection extended to include the position</returns>
    public TextSelection ExtendTo(SelectionPosition position)
    {
        // Keep the original start position and extend to the new position
        return new TextSelection(Start, position);
    }

    /// <summary>
    /// Extends this selection to include the specified row/column.
    /// </summary>
    /// <param name="row">The row to extend to (0-based)</param>
    /// <param name="col">The column to extend to (0-based)</param>
    /// <returns>A new selection extended to include the position</returns>
    public TextSelection ExtendTo(int row, int col)
    {
        return ExtendTo(new SelectionPosition(row, col));
    }

    /// <summary>
    /// Determines whether this selection equals another selection.
    /// </summary>
    /// <param name="other">The other selection to compare</param>
    /// <returns>True if the selections are equal, false otherwise</returns>
    public bool Equals(TextSelection other)
    {
        return Start.Equals(other.Start) && End.Equals(other.End);
    }

    /// <summary>
    /// Determines whether this selection equals another object.
    /// </summary>
    /// <param name="obj">The object to compare</param>
    /// <returns>True if the object is an equal selection, false otherwise</returns>
    public override bool Equals(object? obj)
    {
        return obj is TextSelection other && Equals(other);
    }

    /// <summary>
    /// Gets the hash code for this selection.
    /// </summary>
    /// <returns>The hash code</returns>
    public override int GetHashCode()
    {
        return HashCode.Combine(Start, End);
    }

    /// <summary>
    /// Returns a string representation of this selection.
    /// </summary>
    /// <returns>A string representation of the selection</returns>
    public override string ToString()
    {
        if (IsEmpty)
        {
            return $"Selection(Empty at {Start})";
        }
        return $"Selection({Start} to {End})";
    }

    /// <summary>
    /// Determines whether two selections are equal.
    /// </summary>
    /// <param name="left">The first selection</param>
    /// <param name="right">The second selection</param>
    /// <returns>True if the selections are equal, false otherwise</returns>
    public static bool operator ==(TextSelection left, TextSelection right)
    {
        return left.Equals(right);
    }

    /// <summary>
    /// Determines whether two selections are not equal.
    /// </summary>
    /// <param name="left">The first selection</param>
    /// <param name="right">The second selection</param>
    /// <returns>True if the selections are not equal, false otherwise</returns>
    public static bool operator !=(TextSelection left, TextSelection right)
    {
        return !left.Equals(right);
    }
}

/// <summary>
/// Represents a position within a text selection.
/// </summary>
public readonly struct SelectionPosition : IEquatable<SelectionPosition>, IComparable<SelectionPosition>
{
    /// <summary>
    /// Gets the row position (0-based).
    /// </summary>
    public int Row { get; }

    /// <summary>
    /// Gets the column position (0-based).
    /// </summary>
    public int Col { get; }

    /// <summary>
    /// Creates a new selection position.
    /// </summary>
    /// <param name="row">The row position (0-based)</param>
    /// <param name="col">The column position (0-based)</param>
    public SelectionPosition(int row, int col)
    {
        Row = Math.Max(0, row);
        Col = Math.Max(0, col);
    }

    /// <summary>
    /// Compares this position to another position.
    /// </summary>
    /// <param name="other">The other position to compare</param>
    /// <returns>A value indicating the relative order of the positions</returns>
    public int CompareTo(SelectionPosition other)
    {
        int rowComparison = Row.CompareTo(other.Row);
        if (rowComparison != 0)
        {
            return rowComparison;
        }
        return Col.CompareTo(other.Col);
    }

    /// <summary>
    /// Determines whether this position equals another position.
    /// </summary>
    /// <param name="other">The other position to compare</param>
    /// <returns>True if the positions are equal, false otherwise</returns>
    public bool Equals(SelectionPosition other)
    {
        return Row == other.Row && Col == other.Col;
    }

    /// <summary>
    /// Determines whether this position equals another object.
    /// </summary>
    /// <param name="obj">The object to compare</param>
    /// <returns>True if the object is an equal position, false otherwise</returns>
    public override bool Equals(object? obj)
    {
        return obj is SelectionPosition other && Equals(other);
    }

    /// <summary>
    /// Gets the hash code for this position.
    /// </summary>
    /// <returns>The hash code</returns>
    public override int GetHashCode()
    {
        return HashCode.Combine(Row, Col);
    }

    /// <summary>
    /// Returns a string representation of this position.
    /// </summary>
    /// <returns>A string representation of the position</returns>
    public override string ToString()
    {
        return $"({Row}, {Col})";
    }

    /// <summary>
    /// Determines whether two positions are equal.
    /// </summary>
    /// <param name="left">The first position</param>
    /// <param name="right">The second position</param>
    /// <returns>True if the positions are equal, false otherwise</returns>
    public static bool operator ==(SelectionPosition left, SelectionPosition right)
    {
        return left.Equals(right);
    }

    /// <summary>
    /// Determines whether two positions are not equal.
    /// </summary>
    /// <param name="left">The first position</param>
    /// <param name="right">The second position</param>
    /// <returns>True if the positions are not equal, false otherwise</returns>
    public static bool operator !=(SelectionPosition left, SelectionPosition right)
    {
        return !left.Equals(right);
    }

    /// <summary>
    /// Determines whether the first position is less than the second position.
    /// </summary>
    /// <param name="left">The first position</param>
    /// <param name="right">The second position</param>
    /// <returns>True if the first position is less than the second, false otherwise</returns>
    public static bool operator <(SelectionPosition left, SelectionPosition right)
    {
        return left.CompareTo(right) < 0;
    }

    /// <summary>
    /// Determines whether the first position is greater than the second position.
    /// </summary>
    /// <param name="left">The first position</param>
    /// <param name="right">The second position</param>
    /// <returns>True if the first position is greater than the second, false otherwise</returns>
    public static bool operator >(SelectionPosition left, SelectionPosition right)
    {
        return left.CompareTo(right) > 0;
    }

    /// <summary>
    /// Determines whether the first position is less than or equal to the second position.
    /// </summary>
    /// <param name="left">The first position</param>
    /// <param name="right">The second position</param>
    /// <returns>True if the first position is less than or equal to the second, false otherwise</returns>
    public static bool operator <=(SelectionPosition left, SelectionPosition right)
    {
        return left.CompareTo(right) <= 0;
    }

    /// <summary>
    /// Determines whether the first position is greater than or equal to the second position.
    /// </summary>
    /// <param name="left">The first position</param>
    /// <param name="right">The second position</param>
    /// <returns>True if the first position is greater than or equal to the second, false otherwise</returns>
    public static bool operator >=(SelectionPosition left, SelectionPosition right)
    {
        return left.CompareTo(right) >= 0;
    }
}