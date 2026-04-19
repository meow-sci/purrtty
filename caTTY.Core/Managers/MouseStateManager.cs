using caTTY.Core.Types;

namespace caTTY.Core.Managers;

/// <summary>
///     Manages mouse button state and drag operations for terminal mouse input.
///     Tracks button state, drag operations, and position for motion event optimization.
///     Implements requirements R8.1, R8.3, R8.4, R8.5 for consistent mouse state management.
/// </summary>
public class MouseStateManager : IMouseStateManager
{
    private MouseButton? _pressedButton;
    private bool _isDragging;
    private (int X1, int Y1)? _lastPosition;
    private (int X1, int Y1)? _pressPosition;

    /// <summary>
    ///     Creates a new mouse state manager with initial state.
    /// </summary>
    public MouseStateManager()
    {
        Reset();
    }

    /// <summary>
    ///     Gets the currently pressed mouse button, if any.
    /// </summary>
    public MouseButton? PressedButton => _pressedButton;

    /// <summary>
    ///     Gets whether a drag operation is currently in progress.
    /// </summary>
    public bool IsDragging => _isDragging;

    /// <summary>
    ///     Gets the last known mouse position in 1-based terminal coordinates.
    /// </summary>
    public (int X1, int Y1)? LastPosition => _lastPosition;

    /// <summary>
    ///     Gets whether any mouse button is currently pressed.
    /// </summary>
    public bool HasButtonPressed => _pressedButton.HasValue;

    /// <summary>
    ///     Sets a mouse button as pressed and records the position.
    ///     Implements requirement R8.1: track which button is currently pressed.
    /// </summary>
    /// <param name="button">The mouse button that was pressed</param>
    /// <param name="x1">X coordinate in 1-based terminal coordinates</param>
    /// <param name="y1">Y coordinate in 1-based terminal coordinates</param>
    public void SetButtonPressed(MouseButton button, int x1, int y1)
    {
        // Validate coordinates
        if (x1 < 1 || y1 < 1)
        {
            return;
        }

        // Only handle regular mouse buttons for press/drag operations
        if (button == MouseButton.WheelUp || button == MouseButton.WheelDown)
        {
            return;
        }

        _pressedButton = button;
        _pressPosition = (x1, y1);
        _lastPosition = (x1, y1);
        _isDragging = false; // Drag starts on first motion, not on press
    }

    /// <summary>
    ///     Sets a mouse button as released.
    ///     Implements requirement R8.3: release mouse capture and update state.
    /// </summary>
    /// <param name="button">The mouse button that was released</param>
    public void SetButtonReleased(MouseButton button)
    {
        // Only clear state if the released button matches the pressed button
        if (_pressedButton == button)
        {
            _pressedButton = null;
            _pressPosition = null;
            _isDragging = false;
            // Keep last position for motion event optimization
        }
    }

    /// <summary>
    ///     Updates the current mouse position and determines if dragging should start.
    ///     Implements requirement R8.4: position tracking for motion event optimization.
    /// </summary>
    /// <param name="x1">X coordinate in 1-based terminal coordinates</param>
    /// <param name="y1">Y coordinate in 1-based terminal coordinates</param>
    /// <returns>True if this position update should trigger a drag event</returns>
    public bool UpdatePosition(int x1, int y1)
    {
        // Validate coordinates
        if (x1 < 1 || y1 < 1)
        {
            return false;
        }

        var newPosition = (x1, y1);
        var previousPosition = _lastPosition;
        _lastPosition = newPosition;

        // If no button is pressed, this is just motion tracking
        if (!_pressedButton.HasValue)
        {
            return false;
        }

        // If position hasn't changed, no need to report
        if (previousPosition.HasValue && 
            previousPosition.Value.X1 == x1 && 
            previousPosition.Value.Y1 == y1)
        {
            return false;
        }

        // If we have a pressed button and position changed, start dragging
        if (!_isDragging && _pressPosition.HasValue)
        {
            // Start dragging on first motion after button press
            _isDragging = true;
        }

        return _isDragging;
    }

    /// <summary>
    ///     Resets all mouse state to initial values.
    /// </summary>
    public void Reset()
    {
        _pressedButton = null;
        _isDragging = false;
        _lastPosition = null;
        _pressPosition = null;
    }

    /// <summary>
    ///     Checks whether the current mouse state is consistent and valid.
    ///     Implements requirement R8.5: state consistency checking.
    /// </summary>
    /// <returns>True if the state is consistent, false if corruption is detected</returns>
    public bool IsConsistent()
    {
        // Check basic state consistency rules
        
        // If dragging, must have a pressed button
        if (_isDragging && !_pressedButton.HasValue)
        {
            return false;
        }

        // If dragging, must have both press position and last position
        if (_isDragging && (!_pressPosition.HasValue || !_lastPosition.HasValue))
        {
            return false;
        }

        // If we have a pressed button, we should have a press position
        if (_pressedButton.HasValue && !_pressPosition.HasValue)
        {
            return false;
        }

        // Wheel buttons should never be in pressed state
        if (_pressedButton == MouseButton.WheelUp || _pressedButton == MouseButton.WheelDown)
        {
            return false;
        }

        // Position coordinates should be valid (1-based)
        if (_lastPosition.HasValue && (_lastPosition.Value.X1 < 1 || _lastPosition.Value.Y1 < 1))
        {
            return false;
        }

        if (_pressPosition.HasValue && (_pressPosition.Value.X1 < 1 || _pressPosition.Value.Y1 < 1))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    ///     Attempts to recover from an inconsistent state by resetting to a known good state.
    ///     Implements requirement R8.5: recovery from corrupted state.
    /// </summary>
    public void RecoverFromInconsistentState()
    {
        // Log the inconsistent state before recovery (in a real implementation, this would use a logger)
        // For now, we'll just reset to a known good state
        Reset();
    }
}