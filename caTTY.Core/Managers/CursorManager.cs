using caTTY.Core.Types;

namespace caTTY.Core.Managers;

/// <summary>
///     Manages cursor positioning, visibility, and movement operations.
///     Handles cursor state including position, visibility, wrap pending, and saved positions.
/// </summary>
public class CursorManager : ICursorManager
{
    private readonly ICursor _cursor;
    private (int Row, int Column)? _savedPosition;
    private bool _wrapPending;

    /// <summary>
    ///     Creates a new cursor manager with the specified cursor.
    /// </summary>
    /// <param name="cursor">The cursor to manage</param>
    /// <exception cref="ArgumentNullException">Thrown when cursor is null</exception>
    public CursorManager(ICursor cursor)
    {
        _cursor = cursor ?? throw new ArgumentNullException(nameof(cursor));
        _savedPosition = null;
        _wrapPending = false;
    }

    /// <summary>
    ///     Gets or sets the current cursor row (0-based).
    /// </summary>
    public int Row
    {
        get => _cursor.Row;
        set => _cursor.SetPosition(value, _cursor.Col);
    }

    /// <summary>
    ///     Gets or sets the current cursor column (0-based).
    /// </summary>
    public int Column
    {
        get => _cursor.Col;
        set => _cursor.SetPosition(_cursor.Row, value);
    }

    /// <summary>
    ///     Gets or sets whether the cursor is visible.
    /// </summary>
    public bool Visible
    {
        get => _cursor.Visible;
        set => _cursor.SetVisible(value);
    }

    /// <summary>
    ///     Gets or sets the cursor style (DECSCUSR values 0-6).
    /// </summary>
    public CursorStyle Style { get; set; } = CursorStyle.BlinkingBlock;

    /// <summary>
    ///     Gets whether wrap pending is active (next character will wrap to next line).
    /// </summary>
    public bool WrapPending => _wrapPending;

    /// <summary>
    ///     Moves the cursor to the specified absolute position.
    /// </summary>
    /// <param name="row">Target row (0-based)</param>
    /// <param name="col">Target column (0-based)</param>
    public void MoveTo(int row, int col)
    {
        _cursor.SetPosition(row, col);
        _wrapPending = false;
    }

    /// <summary>
    ///     Moves the cursor up by the specified number of lines.
    /// </summary>
    /// <param name="lines">Number of lines to move up</param>
    public void MoveUp(int lines)
    {
        if (lines <= 0)
        {
            return;
        }

        int newRow = Math.Max(0, _cursor.Row - lines);
        _cursor.SetPosition(newRow, _cursor.Col);
        _wrapPending = false;
    }

    /// <summary>
    ///     Moves the cursor down by the specified number of lines.
    /// </summary>
    /// <param name="lines">Number of lines to move down</param>
    public void MoveDown(int lines)
    {
        if (lines <= 0)
        {
            return;
        }

        int newRow = _cursor.Row + lines;
        _cursor.SetPosition(newRow, _cursor.Col);
        _wrapPending = false;
    }

    /// <summary>
    ///     Moves the cursor left by the specified number of columns.
    /// </summary>
    /// <param name="columns">Number of columns to move left</param>
    public void MoveLeft(int columns)
    {
        if (columns <= 0)
        {
            return;
        }

        int newCol = Math.Max(0, _cursor.Col - columns);
        _cursor.SetPosition(_cursor.Row, newCol);
        _wrapPending = false;
    }

    /// <summary>
    ///     Moves the cursor right by the specified number of columns.
    /// </summary>
    /// <param name="columns">Number of columns to move right</param>
    public void MoveRight(int columns)
    {
        if (columns <= 0)
        {
            return;
        }

        int newCol = _cursor.Col + columns;
        _cursor.SetPosition(_cursor.Row, newCol);
        _wrapPending = false;
    }

    /// <summary>
    ///     Saves the current cursor position for later restoration.
    /// </summary>
    public void SavePosition()
    {
        _savedPosition = (_cursor.Row, _cursor.Col);
    }

    /// <summary>
    ///     Restores the previously saved cursor position.
    /// </summary>
    public void RestorePosition()
    {
        if (_savedPosition.HasValue)
        {
            var (row, col) = _savedPosition.Value;
            _cursor.SetPosition(row, col);
            _wrapPending = false;
        }
    }

    /// <summary>
    ///     Clamps the cursor position to stay within the specified bounds.
    /// </summary>
    /// <param name="width">Maximum width (exclusive)</param>
    /// <param name="height">Maximum height (exclusive)</param>
    public void ClampToBuffer(int width, int height)
    {
        int clampedRow = Math.Max(0, Math.Min(height - 1, _cursor.Row));
        int clampedCol = Math.Max(0, Math.Min(width - 1, _cursor.Col));
        
        if (clampedRow != _cursor.Row || clampedCol != _cursor.Col)
        {
            _cursor.SetPosition(clampedRow, clampedCol);
            _wrapPending = false;
        }
    }

    /// <summary>
    ///     Sets wrap pending state for line overflow handling.
    /// </summary>
    /// <param name="pending">Whether wrap is pending</param>
    public void SetWrapPending(bool pending)
    {
        _wrapPending = pending;
    }

    /// <summary>
    ///     Advances the cursor after writing a character, handling wrap pending.
    /// </summary>
    /// <param name="width">Terminal width for wrap detection</param>
    /// <param name="autoWrapMode">Whether auto-wrap mode is enabled</param>
    /// <returns>True if a line wrap occurred</returns>
    public bool AdvanceCursor(int width, bool autoWrapMode)
    {
        // Handle wrap pending first
        if (_wrapPending)
        {
            _wrapPending = false;
            _cursor.SetPosition(_cursor.Row + 1, 0);
            return true;
        }

        // Normal advancement
        if (_cursor.Col < width - 1)
        {
            _cursor.SetPosition(_cursor.Row, _cursor.Col + 1);
            return false;
        }

        // At right edge
        if (autoWrapMode)
        {
            _wrapPending = true;
        }
        
        return false;
    }

    /// <summary>
    ///     Handles cursor positioning when writing at the right edge.
    /// </summary>
    /// <param name="width">Terminal width</param>
    /// <param name="autoWrapMode">Whether auto-wrap mode is enabled</param>
    /// <returns>True if wrap pending was set</returns>
    public bool HandleRightEdgeWrite(int width, bool autoWrapMode)
    {
        if (_cursor.Col >= width - 1)
        {
            if (autoWrapMode)
            {
                _wrapPending = true;
                return true;
            }
            
            // Stay at right edge
            _cursor.SetPosition(_cursor.Row, width - 1);
        }
        
        return false;
    }
}