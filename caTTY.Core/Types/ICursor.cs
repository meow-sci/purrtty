namespace caTTY.Core.Types;

/// <summary>
///     Interface for cursor position and state management in the terminal.
/// </summary>
public interface ICursor
{
    /// <summary>
    ///     Gets the current row position (0-based).
    /// </summary>
    int Row { get; }

    /// <summary>
    ///     Gets the current column position (0-based).
    /// </summary>
    int Col { get; }

    /// <summary>
    ///     Gets whether the cursor is currently visible.
    /// </summary>
    bool Visible { get; }

    /// <summary>
    ///     Sets the cursor position.
    /// </summary>
    /// <param name="row">The row position (0-based)</param>
    /// <param name="col">The column position (0-based)</param>
    void SetPosition(int row, int col);

    /// <summary>
    ///     Moves the cursor by the specified offset.
    /// </summary>
    /// <param name="deltaRow">Row offset (can be negative)</param>
    /// <param name="deltaCol">Column offset (can be negative)</param>
    void Move(int deltaRow, int deltaCol);

    /// <summary>
    ///     Sets the cursor visibility.
    /// </summary>
    /// <param name="visible">True to show cursor, false to hide</param>
    void SetVisible(bool visible);

    /// <summary>
    ///     Clamps the cursor position to stay within the specified bounds.
    /// </summary>
    /// <param name="maxRow">Maximum row (exclusive)</param>
    /// <param name="maxCol">Maximum column (exclusive)</param>
    void ClampToBounds(int maxRow, int maxCol);

    /// <summary>
    ///     Saves the current cursor position for later restoration.
    /// </summary>
    void Save();

    /// <summary>
    ///     Restores the cursor to the previously saved position.
    ///     If no position was saved, moves to (0, 0).
    /// </summary>
    void Restore();
}
