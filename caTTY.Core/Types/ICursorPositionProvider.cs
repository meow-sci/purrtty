namespace caTTY.Core.Types;

/// <summary>
/// Interface for providing current cursor position to parsers for tracing purposes.
/// </summary>
public interface ICursorPositionProvider
{
    /// <summary>
    /// Gets the current cursor row position (0-based).
    /// </summary>
    int Row { get; }

    /// <summary>
    /// Gets the current cursor column position (0-based).
    /// </summary>
    int Column { get; }
}