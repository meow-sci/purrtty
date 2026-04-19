using caTTY.Core.Types;

namespace caTTY.Core.Managers;

/// <summary>
///     Interface for managing mouse button state and drag operations.
///     Tracks mouse button state, drag operations, and position for motion event optimization.
/// </summary>
public interface IMouseStateManager
{
    /// <summary>
    ///     Gets the currently pressed mouse button, if any.
    /// </summary>
    MouseButton? PressedButton { get; }

    /// <summary>
    ///     Gets whether a drag operation is currently in progress.
    /// </summary>
    bool IsDragging { get; }

    /// <summary>
    ///     Gets the last known mouse position in 1-based terminal coordinates.
    /// </summary>
    (int X1, int Y1)? LastPosition { get; }

    /// <summary>
    ///     Gets whether any mouse button is currently pressed.
    /// </summary>
    bool HasButtonPressed { get; }

    /// <summary>
    ///     Sets a mouse button as pressed and records the position.
    /// </summary>
    /// <param name="button">The mouse button that was pressed</param>
    /// <param name="x1">X coordinate in 1-based terminal coordinates</param>
    /// <param name="y1">Y coordinate in 1-based terminal coordinates</param>
    void SetButtonPressed(MouseButton button, int x1, int y1);

    /// <summary>
    ///     Sets a mouse button as released.
    /// </summary>
    /// <param name="button">The mouse button that was released</param>
    void SetButtonReleased(MouseButton button);

    /// <summary>
    ///     Updates the current mouse position and determines if dragging should start.
    /// </summary>
    /// <param name="x1">X coordinate in 1-based terminal coordinates</param>
    /// <param name="y1">Y coordinate in 1-based terminal coordinates</param>
    /// <returns>True if this position update should trigger a drag event</returns>
    bool UpdatePosition(int x1, int y1);

    /// <summary>
    ///     Resets all mouse state to initial values.
    /// </summary>
    void Reset();

    /// <summary>
    ///     Checks whether the current mouse state is consistent and valid.
    /// </summary>
    /// <returns>True if the state is consistent, false if corruption is detected</returns>
    bool IsConsistent();

    /// <summary>
    ///     Attempts to recover from an inconsistent state by resetting to a known good state.
    /// </summary>
    void RecoverFromInconsistentState();
}