using System.Text;

namespace caTTY.Core.Terminal;

/// <summary>
///     Event arguments for screen update notifications.
/// </summary>
public class ScreenUpdatedEventArgs : EventArgs
{
    /// <summary>
    ///     Creates a new ScreenUpdatedEventArgs for a full screen refresh.
    /// </summary>
    public ScreenUpdatedEventArgs()
    {
        UpdatedRegion = null;
    }

    /// <summary>
    ///     Creates a new ScreenUpdatedEventArgs for a specific region update.
    /// </summary>
    /// <param name="updatedRegion">The region that was updated</param>
    public ScreenUpdatedEventArgs(ScreenRegion updatedRegion)
    {
        UpdatedRegion = updatedRegion;
    }

    /// <summary>
    ///     Gets the region that was updated, or null if the entire screen should be refreshed.
    /// </summary>
    public ScreenRegion? UpdatedRegion { get; }
}

/// <summary>
///     Event arguments for terminal response emissions.
/// </summary>
public class ResponseEmittedEventArgs : EventArgs
{
    /// <summary>
    ///     Creates a new ResponseEmittedEventArgs with the specified response data.
    /// </summary>
    /// <param name="responseData">The response data to send</param>
    public ResponseEmittedEventArgs(ReadOnlyMemory<byte> responseData)
    {
        ResponseData = responseData;
    }

    /// <summary>
    ///     Creates a new ResponseEmittedEventArgs with string response data.
    /// </summary>
    /// <param name="responseText">The response text to send (will be converted to UTF-8)</param>
    public ResponseEmittedEventArgs(string responseText)
    {
        ResponseData = Encoding.UTF8.GetBytes(responseText);
    }

    /// <summary>
    ///     Gets the response data that should be sent back to the shell.
    /// </summary>
    public ReadOnlyMemory<byte> ResponseData { get; }
}

/// <summary>
///     Represents a rectangular region of the screen.
/// </summary>
public readonly struct ScreenRegion
{
    /// <summary>
    ///     Gets the starting row (inclusive).
    /// </summary>
    public int StartRow { get; }

    /// <summary>
    ///     Gets the starting column (inclusive).
    /// </summary>
    public int StartCol { get; }

    /// <summary>
    ///     Gets the ending row (inclusive).
    /// </summary>
    public int EndRow { get; }

    /// <summary>
    ///     Gets the ending column (inclusive).
    /// </summary>
    public int EndCol { get; }

    /// <summary>
    ///     Creates a new screen region.
    /// </summary>
    /// <param name="startRow">Starting row (inclusive)</param>
    /// <param name="startCol">Starting column (inclusive)</param>
    /// <param name="endRow">Ending row (inclusive)</param>
    /// <param name="endCol">Ending column (inclusive)</param>
    public ScreenRegion(int startRow, int startCol, int endRow, int endCol)
    {
        StartRow = startRow;
        StartCol = startCol;
        EndRow = endRow;
        EndCol = endCol;
    }

    /// <summary>
    ///     Creates a screen region for a single cell.
    /// </summary>
    /// <param name="row">The row</param>
    /// <param name="col">The column</param>
    /// <returns>A screen region covering the single cell</returns>
    public static ScreenRegion SingleCell(int row, int col)
    {
        return new ScreenRegion(row, col, row, col);
    }

    /// <summary>
    ///     Creates a screen region for an entire row.
    /// </summary>
    /// <param name="row">The row</param>
    /// <param name="width">The width of the screen</param>
    /// <returns>A screen region covering the entire row</returns>
    public static ScreenRegion EntireRow(int row, int width)
    {
        return new ScreenRegion(row, 0, row, width - 1);
    }
}

/// <summary>
///     Event arguments for bell character notifications.
/// </summary>
public class BellEventArgs : EventArgs
{
    /// <summary>
    ///     Creates new bell event arguments.
    /// </summary>
    public BellEventArgs()
    {
        Timestamp = DateTime.UtcNow;
    }

    /// <summary>
    ///     Gets the time when the bell was triggered.
    /// </summary>
    public DateTime Timestamp { get; }
}

/// <summary>
///     Event arguments for window title change notifications.
/// </summary>
public class TitleChangeEventArgs : EventArgs
{
    /// <summary>
    ///     Creates new title change event arguments.
    /// </summary>
    /// <param name="newTitle">The new window title</param>
    public TitleChangeEventArgs(string newTitle)
    {
        NewTitle = newTitle ?? string.Empty;
        Timestamp = DateTime.UtcNow;
    }

    /// <summary>
    ///     Gets the new window title.
    /// </summary>
    public string NewTitle { get; }

    /// <summary>
    ///     Gets the time when the title was changed.
    /// </summary>
    public DateTime Timestamp { get; }
}

/// <summary>
///     Event arguments for icon name change notifications.
/// </summary>
public class IconNameChangeEventArgs : EventArgs
{
    /// <summary>
    ///     Creates new icon name change event arguments.
    /// </summary>
    /// <param name="newIconName">The new icon name</param>
    public IconNameChangeEventArgs(string newIconName)
    {
        NewIconName = newIconName ?? string.Empty;
        Timestamp = DateTime.UtcNow;
    }

    /// <summary>
    ///     Gets the new icon name.
    /// </summary>
    public string NewIconName { get; }

    /// <summary>
    ///     Gets the time when the icon name was changed.
    /// </summary>
    public DateTime Timestamp { get; }
}

/// <summary>
///     Event arguments for clipboard operation notifications.
/// </summary>
public class ClipboardEventArgs : EventArgs
{
    /// <summary>
    ///     Creates new clipboard event arguments.
    /// </summary>
    /// <param name="selectionTarget">The selection target (e.g., "c" for clipboard, "p" for primary)</param>
    /// <param name="data">The clipboard data (decoded from base64)</param>
    /// <param name="isQuery">Whether this is a clipboard query (data will be null)</param>
    public ClipboardEventArgs(string selectionTarget, string? data, bool isQuery = false)
    {
        SelectionTarget = selectionTarget ?? string.Empty;
        Data = data;
        IsQuery = isQuery;
        Timestamp = DateTime.UtcNow;
    }

    /// <summary>
    ///     Gets the selection target (e.g., "c" for clipboard, "p" for primary selection).
    /// </summary>
    public string SelectionTarget { get; }

    /// <summary>
    ///     Gets the clipboard data (null for queries).
    /// </summary>
    public string? Data { get; }

    /// <summary>
    ///     Gets whether this is a clipboard query operation.
    /// </summary>
    public bool IsQuery { get; }

    /// <summary>
    ///     Gets the time when the clipboard operation was requested.
    /// </summary>
    public DateTime Timestamp { get; }
}
