namespace caTTY.Core.Types;

/// <summary>
///     Interface for a scrollback buffer that stores historical terminal lines.
///     Provides access to lines that have scrolled off the top of the screen.
/// </summary>
public interface IScrollbackBuffer : IDisposable
{
    /// <summary>
    ///     Maximum number of lines that can be stored in the buffer.
    /// </summary>
    int MaxLines { get; }

    /// <summary>
    ///     Current number of lines stored in the buffer.
    /// </summary>
    int CurrentLines { get; }

    /// <summary>
    ///     Adds a line to the scrollback buffer. If the buffer is full, the oldest line is removed.
    /// </summary>
    /// <param name="line">The line to add to the buffer</param>
    void AddLine(ReadOnlySpan<Cell> line);

    /// <summary>
    ///     Gets a line from the scrollback buffer by index.
    /// </summary>
    /// <param name="index">Index of the line (0 = oldest, CurrentLines-1 = newest)</param>
    /// <returns>The requested line as a read-only span</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when index is out of range</exception>
    ReadOnlySpan<Cell> GetLine(int index);

    /// <summary>
    ///     Clears all lines from the scrollback buffer.
    /// </summary>
    void Clear();
}