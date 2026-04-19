namespace caTTY.Core.Types;

/// <summary>
///     Implementation of ICursor for basic cursor position tracking.
///     Includes save/restore functionality and bounds clamping.
/// </summary>
public class Cursor : ICursor
{
    private (int Row, int Col)? _savedPosition;

    /// <summary>
    ///     Creates a new cursor at position (0, 0) with visibility enabled.
    /// </summary>
    public Cursor()
    {
        Row = 0;
        Col = 0;
        Visible = true;
        _savedPosition = null;
    }

    /// <summary>
    ///     Creates a new cursor at the specified position.
    /// </summary>
    /// <param name="row">Initial row position</param>
    /// <param name="col">Initial column position</param>
    /// <param name="visible">Initial visibility state</param>
    public Cursor(int row, int col, bool visible = true)
    {
        Row = Math.Max(0, row);
        Col = Math.Max(0, col);
        Visible = visible;
        _savedPosition = null;
    }

    /// <summary>
    ///     Gets the current row position (0-based).
    /// </summary>
    public int Row { get; private set; }

    /// <summary>
    ///     Gets the current column position (0-based).
    /// </summary>
    public int Col { get; private set; }

    /// <summary>
    ///     Gets whether the cursor is currently visible.
    /// </summary>
    public bool Visible { get; private set; }

    /// <summary>
    ///     Sets the cursor position.
    /// </summary>
    /// <param name="row">The row position (0-based)</param>
    /// <param name="col">The column position (0-based)</param>
    public void SetPosition(int row, int col)
    {
        Row = Math.Max(0, row);
        Col = Math.Max(0, col);
    }

    /// <summary>
    ///     Moves the cursor by the specified offset.
    /// </summary>
    /// <param name="deltaRow">Row offset (can be negative)</param>
    /// <param name="deltaCol">Column offset (can be negative)</param>
    public void Move(int deltaRow, int deltaCol)
    {
        Row = Math.Max(0, Row + deltaRow);
        Col = Math.Max(0, Col + deltaCol);
    }

    /// <summary>
    ///     Sets the cursor visibility.
    /// </summary>
    /// <param name="visible">True to show cursor, false to hide</param>
    public void SetVisible(bool visible)
    {
        Visible = visible;
    }

    /// <summary>
    ///     Clamps the cursor position to stay within the specified bounds.
    /// </summary>
    /// <param name="maxRow">Maximum row (exclusive)</param>
    /// <param name="maxCol">Maximum column (exclusive)</param>
    public void ClampToBounds(int maxRow, int maxCol)
    {
        if (maxRow <= 0 || maxCol <= 0)
        {
            return;
        }

        Row = Math.Max(0, Math.Min(Row, maxRow - 1));
        Col = Math.Max(0, Math.Min(Col, maxCol - 1));
    }

    /// <summary>
    ///     Saves the current cursor position for later restoration.
    /// </summary>
    public void Save()
    {
        _savedPosition = (Row, Col);
    }

    /// <summary>
    ///     Restores the cursor to the previously saved position.
    ///     If no position was saved, moves to (0, 0).
    /// </summary>
    public void Restore()
    {
        if (_savedPosition.HasValue)
        {
            Row = _savedPosition.Value.Row;
            Col = _savedPosition.Value.Col;
        }
        else
        {
            Row = 0;
            Col = 0;
        }
    }

    /// <summary>
    ///     Returns a string representation of the cursor state.
    /// </summary>
    public override string ToString()
    {
        return $"Cursor(Row={Row}, Col={Col}, Visible={Visible})";
    }
}
