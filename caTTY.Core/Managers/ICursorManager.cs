using caTTY.Core.Types;

namespace caTTY.Core.Managers;

/// <summary>
///     Interface for managing cursor positioning, visibility, and movement operations.
/// </summary>
public interface ICursorManager
{
    /// <summary>
    ///     Gets or sets the current cursor row (0-based).
    /// </summary>
    int Row { get; set; }

    /// <summary>
    ///     Gets or sets the current cursor column (0-based).
    /// </summary>
    int Column { get; set; }

    /// <summary>
    ///     Gets or sets whether the cursor is visible.
    /// </summary>
    bool Visible { get; set; }

    /// <summary>
    ///     Gets or sets the cursor style (DECSCUSR values 0-6).
    /// </summary>
    CursorStyle Style { get; set; }

    /// <summary>
    ///     Gets whether wrap pending is active (next character will wrap to next line).
    /// </summary>
    bool WrapPending { get; }

    /// <summary>
    ///     Moves the cursor to the specified absolute position.
    /// </summary>
    /// <param name="row">Target row (0-based)</param>
    /// <param name="col">Target column (0-based)</param>
    void MoveTo(int row, int col);

    /// <summary>
    ///     Moves the cursor up by the specified number of lines.
    /// </summary>
    /// <param name="lines">Number of lines to move up</param>
    void MoveUp(int lines);

    /// <summary>
    ///     Moves the cursor down by the specified number of lines.
    /// </summary>
    /// <param name="lines">Number of lines to move down</param>
    void MoveDown(int lines);

    /// <summary>
    ///     Moves the cursor left by the specified number of columns.
    /// </summary>
    /// <param name="columns">Number of columns to move left</param>
    void MoveLeft(int columns);

    /// <summary>
    ///     Moves the cursor right by the specified number of columns.
    /// </summary>
    /// <param name="columns">Number of columns to move right</param>
    void MoveRight(int columns);

    /// <summary>
    ///     Saves the current cursor position for later restoration.
    /// </summary>
    void SavePosition();

    /// <summary>
    ///     Restores the previously saved cursor position.
    /// </summary>
    void RestorePosition();

    /// <summary>
    ///     Clamps the cursor position to stay within the specified bounds.
    /// </summary>
    /// <param name="width">Maximum width (exclusive)</param>
    /// <param name="height">Maximum height (exclusive)</param>
    void ClampToBuffer(int width, int height);

    /// <summary>
    ///     Sets wrap pending state for line overflow handling.
    /// </summary>
    /// <param name="pending">Whether wrap is pending</param>
    void SetWrapPending(bool pending);

    /// <summary>
    ///     Advances the cursor after writing a character, handling wrap pending.
    /// </summary>
    /// <param name="width">Terminal width for wrap detection</param>
    /// <param name="autoWrapMode">Whether auto-wrap mode is enabled</param>
    /// <returns>True if a line wrap occurred</returns>
    bool AdvanceCursor(int width, bool autoWrapMode);

    /// <summary>
    ///     Handles cursor positioning when writing at the right edge.
    /// </summary>
    /// <param name="width">Terminal width</param>
    /// <param name="autoWrapMode">Whether auto-wrap mode is enabled</param>
    /// <returns>True if wrap pending was set</returns>
    bool HandleRightEdgeWrite(int width, bool autoWrapMode);
}