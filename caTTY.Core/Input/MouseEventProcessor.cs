using System;
using caTTY.Core.Managers;
using caTTY.Core.Terminal;
using caTTY.Core.Types;

namespace caTTY.Core.Input;

/// <summary>
///     Processes mouse events and routes them between application and local handlers.
///     Implements event routing logic, shift-key bypass for selection priority, and error handling.
///     Validates requirements R1.4, R6.1, R6.2, R11.3.
/// </summary>
public class MouseEventProcessor
{
    private readonly IMouseTrackingManager _trackingManager;
    private readonly IMouseStateManager _stateManager;

    /// <summary>
    ///     Event raised when a mouse event should be sent to the application as an escape sequence.
    /// </summary>
    public event EventHandler<MouseEventArgs>? MouseEventGenerated;

    /// <summary>
    ///     Event raised when a mouse event should be handled locally (selection, scrolling).
    /// </summary>
    public event EventHandler<MouseEventArgs>? LocalMouseEvent;

    /// <summary>
    ///     Event raised when an error occurs during mouse event processing.
    /// </summary>
    public event EventHandler<MouseProcessingErrorEventArgs>? ProcessingError;

    /// <summary>
    ///     Creates a new mouse event processor.
    /// </summary>
    /// <param name="trackingManager">The mouse tracking manager</param>
    /// <param name="stateManager">The mouse state manager</param>
    /// <exception cref="ArgumentNullException">Thrown when required dependencies are null</exception>
    public MouseEventProcessor(IMouseTrackingManager trackingManager, IMouseStateManager stateManager)
    {
        _trackingManager = trackingManager ?? throw new ArgumentNullException(nameof(trackingManager));
        _stateManager = stateManager ?? throw new ArgumentNullException(nameof(stateManager));
    }

    /// <summary>
    ///     Processes a mouse event and routes it to the appropriate handler.
    ///     Implements requirements R1.4, R6.1, R6.2 for event routing and selection priority.
    /// </summary>
    /// <param name="mouseEvent">The mouse event to process</param>
    public void ProcessMouseEvent(MouseEvent mouseEvent)
    {
        try
        {
            // Validate the mouse event
            if (!ValidateMouseEvent(mouseEvent))
            {
                OnProcessingError(new MouseProcessingErrorEventArgs(
                    "Invalid mouse event parameters",
                    mouseEvent,
                    MouseProcessingErrorType.InvalidEvent));
                return;
            }

            // Update mouse state for press/release events
            UpdateMouseState(mouseEvent);

            // Determine if this event should be handled locally or sent to application
            bool shouldHandleLocally = ShouldHandleLocally(mouseEvent);
            
            if (shouldHandleLocally)
            {
                // Route to local handlers (selection, scrolling)
                OnLocalMouseEvent(new MouseEventArgs(mouseEvent));
            }
            else
            {
                // Route to application via escape sequence generation
                OnMouseEventGenerated(new MouseEventArgs(mouseEvent));
            }
        }
        catch (Exception ex)
        {
            // Implement requirement R11.3: graceful error handling
            OnProcessingError(new MouseProcessingErrorEventArgs(
                $"Error processing mouse event: {ex.Message}",
                mouseEvent,
                MouseProcessingErrorType.ProcessingException,
                ex));
        }
    }

    /// <summary>
    ///     Determines whether a mouse event should be handled locally instead of sent to the application.
    ///     Implements requirements R1.4, R6.1, R6.2 for local handling priority.
    /// </summary>
    /// <param name="mouseEvent">The mouse event to evaluate</param>
    /// <returns>True if the event should be handled locally, false if it should be sent to the application</returns>
    public bool ShouldHandleLocally(MouseEvent mouseEvent)
    {
        try
        {
            var currentMode = _trackingManager.CurrentMode;
            var selectionPriority = _trackingManager.Configuration.SelectionPriority;
            var hasShift = mouseEvent.Modifiers.HasFlag(MouseKeyModifiers.Shift);
            var hasButtonPressed = _stateManager.HasButtonPressed;
            
            // R6.1: When shift key is held, handle selection locally instead of reporting to application
            if (selectionPriority && hasShift)
            {
                return true;
            }

            // R1.4: When mouse tracking is disabled, handle all events locally
            if (currentMode == MouseTrackingMode.Off)
            {
                // Only log this once when tracking is first disabled to avoid spam
                return true;
            }

            // R6.2: When mouse tracking is disabled, handle all events for selection
            // (This is the same as the above check, but kept separate for clarity)

            // Check if the tracking mode supports this event type
            if (!_trackingManager.ShouldReportEvent(mouseEvent.Type, hasButtonPressed))
            {
                return true;
            }

            // If we reach here, the event should be sent to the application
            return false;
        }
        catch (Exception ex)
        {
            // On error, default to local handling for safety
            OnProcessingError(new MouseProcessingErrorEventArgs(
                $"Error determining event routing: {ex.Message}",
                mouseEvent,
                MouseProcessingErrorType.RoutingError,
                ex));
            return true;
        }
    }

    /// <summary>
    ///     Updates the mouse state based on the event type.
    /// </summary>
    /// <param name="mouseEvent">The mouse event</param>
    private void UpdateMouseState(MouseEvent mouseEvent)
    {
        try
        {
            switch (mouseEvent.Type)
            {
                case MouseEventType.Press:
                    _stateManager.SetButtonPressed(mouseEvent.Button, mouseEvent.X1, mouseEvent.Y1);
                    break;

                case MouseEventType.Release:
                    _stateManager.SetButtonReleased(mouseEvent.Button);
                    break;

                case MouseEventType.Motion:
                    _stateManager.UpdatePosition(mouseEvent.X1, mouseEvent.Y1);
                    break;

                case MouseEventType.Wheel:
                    // Wheel events don't affect button state
                    break;
            }

            // Check for state consistency and recover if needed
            if (!_stateManager.IsConsistent())
            {
                OnProcessingError(new MouseProcessingErrorEventArgs(
                    "Mouse state inconsistency detected, recovering",
                    mouseEvent,
                    MouseProcessingErrorType.StateInconsistency));
                _stateManager.RecoverFromInconsistentState();
            }
        }
        catch (Exception ex)
        {
            OnProcessingError(new MouseProcessingErrorEventArgs(
                $"Error updating mouse state: {ex.Message}",
                mouseEvent,
                MouseProcessingErrorType.StateUpdateError,
                ex));
        }
    }

    /// <summary>
    ///     Validates a mouse event for basic correctness.
    /// </summary>
    /// <param name="mouseEvent">The mouse event to validate</param>
    /// <returns>True if the event is valid, false otherwise</returns>
    private static bool ValidateMouseEvent(MouseEvent mouseEvent)
    {
        // Check coordinate validity (1-based coordinates)
        if (mouseEvent.X1 < 1 || mouseEvent.Y1 < 1)
        {
            return false;
        }

        // Check for reasonable coordinate limits (prevent overflow)
        if (mouseEvent.X1 > 32767 || mouseEvent.Y1 > 32767)
        {
            return false;
        }

        // Validate button for press/release events
        if (mouseEvent.Type == MouseEventType.Press || mouseEvent.Type == MouseEventType.Release)
        {
            // Ensure valid button values
            if (!Enum.IsDefined(typeof(MouseButton), mouseEvent.Button))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    ///     Raises the MouseEventGenerated event.
    /// </summary>
    /// <param name="e">The event arguments</param>
    protected virtual void OnMouseEventGenerated(MouseEventArgs e)
    {
        MouseEventGenerated?.Invoke(this, e);
    }

    /// <summary>
    ///     Raises the LocalMouseEvent event.
    /// </summary>
    /// <param name="e">The event arguments</param>
    protected virtual void OnLocalMouseEvent(MouseEventArgs e)
    {
        LocalMouseEvent?.Invoke(this, e);
    }

    /// <summary>
    ///     Raises the ProcessingError event.
    /// </summary>
    /// <param name="e">The error event arguments</param>
    protected virtual void OnProcessingError(MouseProcessingErrorEventArgs e)
    {
        ProcessingError?.Invoke(this, e);
    }
}

/// <summary>
///     Event arguments for mouse events processed by the MouseEventProcessor.
/// </summary>
public class MouseEventArgs : EventArgs
{
    /// <summary>
    ///     Creates new mouse event arguments.
    /// </summary>
    /// <param name="mouseEvent">The mouse event</param>
    public MouseEventArgs(MouseEvent mouseEvent)
    {
        MouseEvent = mouseEvent;
        Timestamp = DateTime.UtcNow;
    }

    /// <summary>
    ///     Gets the mouse event.
    /// </summary>
    public MouseEvent MouseEvent { get; }

    /// <summary>
    ///     Gets the time when the event was processed.
    /// </summary>
    public DateTime Timestamp { get; }
}

/// <summary>
///     Event arguments for mouse processing errors.
/// </summary>
public class MouseProcessingErrorEventArgs : EventArgs
{
    /// <summary>
    ///     Creates new mouse processing error event arguments.
    /// </summary>
    /// <param name="message">The error message</param>
    /// <param name="mouseEvent">The mouse event that caused the error</param>
    /// <param name="errorType">The type of error</param>
    /// <param name="exception">The underlying exception, if any</param>
    public MouseProcessingErrorEventArgs(
        string message,
        MouseEvent mouseEvent,
        MouseProcessingErrorType errorType,
        Exception? exception = null)
    {
        Message = message ?? string.Empty;
        MouseEvent = mouseEvent;
        ErrorType = errorType;
        Exception = exception;
        Timestamp = DateTime.UtcNow;
    }

    /// <summary>
    ///     Gets the error message.
    /// </summary>
    public string Message { get; }

    /// <summary>
    ///     Gets the mouse event that caused the error.
    /// </summary>
    public MouseEvent MouseEvent { get; }

    /// <summary>
    ///     Gets the type of error.
    /// </summary>
    public MouseProcessingErrorType ErrorType { get; }

    /// <summary>
    ///     Gets the underlying exception, if any.
    /// </summary>
    public Exception? Exception { get; }

    /// <summary>
    ///     Gets the time when the error occurred.
    /// </summary>
    public DateTime Timestamp { get; }
}

/// <summary>
///     Types of mouse processing errors.
/// </summary>
public enum MouseProcessingErrorType
{
    /// <summary>
    ///     Invalid mouse event parameters.
    /// </summary>
    InvalidEvent,

    /// <summary>
    ///     Error during event processing.
    /// </summary>
    ProcessingException,

    /// <summary>
    ///     Error determining event routing.
    /// </summary>
    RoutingError,

    /// <summary>
    ///     Mouse state inconsistency detected.
    /// </summary>
    StateInconsistency,

    /// <summary>
    ///     Error updating mouse state.
    /// </summary>
    StateUpdateError
}