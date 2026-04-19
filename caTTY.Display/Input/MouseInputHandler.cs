using System;
using Brutal.ImGuiApi;
using Brutal.Numerics;
using caTTY.Core.Input;
using caTTY.Core.Managers;
using caTTY.Core.Types;
using caTTY.Display.Utils;
using ImGui = Brutal.ImGuiApi.ImGui;

namespace caTTY.Display.Input;

/// <summary>
///     Handles ImGui mouse input detection and processing for terminal mouse support.
///     Implements mouse event detection (press, release, move, wheel), mouse capture for drag operations,
///     and focus-based event filtering. Validates requirements R4.1-R4.5, R7.1, R7.3, R8.2.
/// </summary>
public class MouseInputHandler
{
    private readonly MouseEventProcessor _eventProcessor;
    private readonly CoordinateConverter _coordinateConverter;
    private readonly IMouseStateManager _stateManager;
    private readonly IMouseTrackingManager _trackingManager;

    private bool _hasTerminalFocus;
    private bool _isMouseCaptured;
    private float2 _terminalSize;
    private int _terminalWidth;
    private int _terminalHeight;
    private float2 _lastMousePosition;
    private bool _lastMousePositionValid;

    /// <summary>
    ///     Event raised when an error occurs during mouse input processing.
    /// </summary>
    public event EventHandler<MouseInputErrorEventArgs>? InputError;

    /// <summary>
    ///     Creates a new mouse input handler.
    /// </summary>
    /// <param name="eventProcessor">The mouse event processor</param>
    /// <param name="coordinateConverter">The coordinate converter</param>
    /// <param name="stateManager">The mouse state manager</param>
    /// <param name="trackingManager">The mouse tracking manager</param>
    /// <exception cref="ArgumentNullException">Thrown when required dependencies are null</exception>
    public MouseInputHandler(
        MouseEventProcessor eventProcessor,
        CoordinateConverter coordinateConverter,
        IMouseStateManager stateManager,
        IMouseTrackingManager trackingManager)
    {
        _eventProcessor = eventProcessor ?? throw new ArgumentNullException(nameof(eventProcessor));
        _coordinateConverter = coordinateConverter ?? throw new ArgumentNullException(nameof(coordinateConverter));
        _stateManager = stateManager ?? throw new ArgumentNullException(nameof(stateManager));
        _trackingManager = trackingManager ?? throw new ArgumentNullException(nameof(trackingManager));

        _hasTerminalFocus = false;
        _isMouseCaptured = false;
        _terminalSize = new float2(800, 600); // Default size
        _terminalWidth = 80;
        _terminalHeight = 24;
        _lastMousePosition = new float2(0, 0);
        _lastMousePositionValid = false;
    }

    /// <summary>
    ///     Sets the terminal focus state.
    ///     Implements requirement R7.1: capture mouse events only when terminal has focus.
    /// </summary>
    /// <param name="hasFocus">True if the terminal has focus</param>
    public void SetTerminalFocus(bool hasFocus)
    {
        if (_hasTerminalFocus != hasFocus)
        {
            _hasTerminalFocus = hasFocus;

            // R7.2, R7.4, R7.5: Reset mouse state when focus changes
            if (!hasFocus)
            {
                // Release mouse capture and reset state when losing focus
                if (_isMouseCaptured)
                {
                    ReleaseMouseCapture();
                }
                _stateManager.Reset();
                _lastMousePositionValid = false;
            }
        }
    }

    /// <summary>
    ///     Updates the terminal size and dimensions for coordinate conversion.
    /// </summary>
    /// <param name="terminalSize">Terminal size in pixels</param>
    /// <param name="terminalWidth">Terminal width in columns</param>
    /// <param name="terminalHeight">Terminal height in rows</param>
    public void UpdateTerminalSize(float2 terminalSize, int terminalWidth, int terminalHeight)
    {
        _terminalSize = terminalSize;
        _terminalWidth = Math.Max(1, terminalWidth);
        _terminalHeight = Math.Max(1, terminalHeight);
    }

    /// <summary>
    ///     Handles mouse input from ImGui. Should be called each frame.
    ///     Implements requirements R4.1-R4.5 for mouse event detection.
    /// </summary>
    public void HandleMouseInput()
    {
        try
        {
            // R7.1, R7.3: Only process mouse events when terminal has focus
            if (!_hasTerminalFocus)
            {
                return;
            }

            var io = ImGui.GetIO();
            var mousePos = io.MousePos;

            // Check if mouse is within terminal bounds
            if (!_coordinateConverter.IsWithinBounds(mousePos, _terminalSize))
            {
                // Mouse is outside terminal bounds
                if (_isMouseCaptured)
                {
                    // Continue processing during capture (drag operations)
                    ProcessMouseMove(mousePos);
                }
                return;
            }

            // Process mouse button events
            ProcessMouseButtons(mousePos);

            // Process mouse movement
            ProcessMouseMove(mousePos);

            // Process mouse wheel
            ProcessMouseWheel(mousePos, io.MouseWheel);

            // Handle mouse capture for drag operations
            HandleMouseCapture();
        }
        catch (Exception ex)
        {
            OnInputError(new MouseInputErrorEventArgs(
                $"Error handling mouse input: {ex.Message}",
                MouseInputErrorType.InputProcessingError,
                ex));
        }
    }

    /// <summary>
    ///     Processes mouse button press and release events.
    ///     Implements requirements R4.1, R4.2, R4.3 for button detection.
    /// </summary>
    /// <param name="mousePos">Current mouse position</param>
    private void ProcessMouseButtons(float2 mousePos)
    {
        try
        {
            var io = ImGui.GetIO();

            // Check each mouse button for press/release events
            ProcessMouseButton(ImGuiMouseButton.Left, MouseButton.Left, mousePos, io);
            ProcessMouseButton(ImGuiMouseButton.Right, MouseButton.Right, mousePos, io);
            ProcessMouseButton(ImGuiMouseButton.Middle, MouseButton.Middle, mousePos, io);
        }
        catch (Exception ex)
        {
            OnInputError(new MouseInputErrorEventArgs(
                $"Error processing mouse buttons: {ex.Message}",
                MouseInputErrorType.ButtonProcessingError,
                ex));
        }
    }

    /// <summary>
    ///     Processes a specific mouse button for press/release events.
    /// </summary>
    /// <param name="imguiButton">ImGui mouse button</param>
    /// <param name="mouseButton">Terminal mouse button</param>
    /// <param name="mousePos">Current mouse position</param>
    /// <param name="io">ImGui IO context</param>
    private void ProcessMouseButton(ImGuiMouseButton imguiButton, MouseButton mouseButton, float2 mousePos, ImGuiIOPtr io)
    {
        // Convert mouse position to terminal coordinates
        var coords = _coordinateConverter.PixelToCell(mousePos, _terminalWidth, _terminalHeight);
        if (!coords.HasValue)
        {
            return; // Invalid coordinates
        }

        var (x1, y1) = coords.Value;
        var modifiers = GetCurrentModifiers(io);

        // Check for button press
        if (ImGui.IsMouseClicked(imguiButton))
        {
            var pressEvent = new MouseEvent(
                MouseEventType.Press,
                mouseButton,
                x1, y1,
                modifiers);

            _eventProcessor.ProcessMouseEvent(pressEvent);
        }

        // Check for button release
        if (ImGui.IsMouseReleased(imguiButton))
        {
            var releaseEvent = new MouseEvent(
                MouseEventType.Release,
                mouseButton,
                x1, y1,
                modifiers);

            _eventProcessor.ProcessMouseEvent(releaseEvent);
        }
    }

    /// <summary>
    ///     Processes mouse movement events.
    ///     Implements requirements R4.4, R4.5 for motion detection.
    /// </summary>
    /// <param name="mousePos">Current mouse position</param>
    private void ProcessMouseMove(float2 mousePos)
    {
        try
        {
            // Check if mouse position has changed
            if (_lastMousePositionValid && 
                Math.Abs(mousePos.X - _lastMousePosition.X) < 0.5f && 
                Math.Abs(mousePos.Y - _lastMousePosition.Y) < 0.5f)
            {
                return; // Position hasn't changed significantly
            }

            _lastMousePosition = mousePos;
            _lastMousePositionValid = true;

            // Convert to terminal coordinates
            var coords = _coordinateConverter.PixelToCell(mousePos, _terminalWidth, _terminalHeight);
            if (!coords.HasValue)
            {
                return; // Invalid coordinates
            }

            var (x1, y1) = coords.Value;
            var io = ImGui.GetIO();
            var modifiers = GetCurrentModifiers(io);

            // Create motion event
            var motionEvent = new MouseEvent(
                MouseEventType.Motion,
                MouseButton.Left, // Button doesn't matter for motion events
                x1, y1,
                modifiers);

            _eventProcessor.ProcessMouseEvent(motionEvent);
        }
        catch (Exception ex)
        {
            OnInputError(new MouseInputErrorEventArgs(
                $"Error processing mouse movement: {ex.Message}",
                MouseInputErrorType.MotionProcessingError,
                ex));
        }
    }

    /// <summary>
    ///     Processes mouse wheel events.
    /// </summary>
    /// <param name="mousePos">Current mouse position</param>
    /// <param name="wheelDelta">Wheel delta from ImGui</param>
    private void ProcessMouseWheel(float2 mousePos, float wheelDelta)
    {
        try
        {
            if (Math.Abs(wheelDelta) < 0.1f)
            {
                return; // No significant wheel movement
            }

            // Convert to terminal coordinates
            var coords = _coordinateConverter.PixelToCell(mousePos, _terminalWidth, _terminalHeight);
            if (!coords.HasValue)
            {
                return; // Invalid coordinates
            }

            var (x1, y1) = coords.Value;
            var io = ImGui.GetIO();
            var modifiers = GetCurrentModifiers(io);

            // Determine wheel direction
            var wheelButton = wheelDelta > 0 ? MouseButton.WheelUp : MouseButton.WheelDown;

            // Create wheel event
            var wheelEvent = new MouseEvent(
                MouseEventType.Wheel,
                wheelButton,
                x1, y1,
                modifiers);

            _eventProcessor.ProcessMouseEvent(wheelEvent);
        }
        catch (Exception ex)
        {
            OnInputError(new MouseInputErrorEventArgs(
                $"Error processing mouse wheel: {ex.Message}",
                MouseInputErrorType.WheelProcessingError,
                ex));
        }
    }

    /// <summary>
    ///     Handles mouse capture for drag operations.
    ///     Implements requirements R8.2, R8.3 for mouse capture during drag.
    /// </summary>
    private void HandleMouseCapture()
    {
        try
        {
            var shouldCapture = _stateManager.IsDragging && _hasTerminalFocus;

            if (shouldCapture && !_isMouseCaptured)
            {
                // Start mouse capture
                CaptureMouseInput();
            }
            else if (!shouldCapture && _isMouseCaptured)
            {
                // Release mouse capture
                ReleaseMouseCapture();
            }
        }
        catch (Exception ex)
        {
            OnInputError(new MouseInputErrorEventArgs(
                $"Error handling mouse capture: {ex.Message}",
                MouseInputErrorType.CaptureError,
                ex));
        }
    }

    /// <summary>
    ///     Captures mouse input for drag operations.
    /// </summary>
    private void CaptureMouseInput()
    {
        try
        {
            // In ImGui, we don't need explicit mouse capture like Win32
            // ImGui handles this automatically when we process mouse events
            // We just track the capture state for our logic
            _isMouseCaptured = true;
        }
        catch (Exception ex)
        {
            OnInputError(new MouseInputErrorEventArgs(
                $"Failed to capture mouse input: {ex.Message}",
                MouseInputErrorType.CaptureError,
                ex));
        }
    }

    /// <summary>
    ///     Releases mouse input capture.
    /// </summary>
    private void ReleaseMouseCapture()
    {
        try
        {
            _isMouseCaptured = false;
        }
        catch (Exception ex)
        {
            OnInputError(new MouseInputErrorEventArgs(
                $"Failed to release mouse capture: {ex.Message}",
                MouseInputErrorType.CaptureError,
                ex));
        }
    }

    /// <summary>
    ///     Gets the current modifier key state from ImGui.
    /// </summary>
    /// <param name="io">ImGui IO context</param>
    /// <returns>Current modifier key flags</returns>
    private static MouseKeyModifiers GetCurrentModifiers(ImGuiIOPtr io)
    {
        var modifiers = MouseKeyModifiers.None;

        if (io.KeyShift)
        {
            modifiers |= MouseKeyModifiers.Shift;
        }

        if (io.KeyAlt)
        {
            modifiers |= MouseKeyModifiers.Alt;
        }

        if (io.KeyCtrl)
        {
            modifiers |= MouseKeyModifiers.Ctrl;
        }

        return modifiers;
    }

    /// <summary>
    ///     Gets whether the terminal currently has focus.
    /// </summary>
    public bool HasTerminalFocus => _hasTerminalFocus;

    /// <summary>
    ///     Gets whether mouse input is currently captured.
    /// </summary>
    public bool IsMouseCaptured => _isMouseCaptured;

    /// <summary>
    ///     Raises the InputError event.
    /// </summary>
    /// <param name="e">The error event arguments</param>
    protected virtual void OnInputError(MouseInputErrorEventArgs e)
    {
        InputError?.Invoke(this, e);
    }
}

/// <summary>
///     Event arguments for mouse input errors.
/// </summary>
public class MouseInputErrorEventArgs : EventArgs
{
    /// <summary>
    ///     Creates new mouse input error event arguments.
    /// </summary>
    /// <param name="message">The error message</param>
    /// <param name="errorType">The type of error</param>
    /// <param name="exception">The underlying exception, if any</param>
    public MouseInputErrorEventArgs(
        string message,
        MouseInputErrorType errorType,
        Exception? exception = null)
    {
        Message = message ?? string.Empty;
        ErrorType = errorType;
        Exception = exception;
        Timestamp = DateTime.UtcNow;
    }

    /// <summary>
    ///     Gets the error message.
    /// </summary>
    public string Message { get; }

    /// <summary>
    ///     Gets the type of error.
    /// </summary>
    public MouseInputErrorType ErrorType { get; }

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
///     Types of mouse input errors.
/// </summary>
public enum MouseInputErrorType
{
    /// <summary>
    ///     Error during general input processing.
    /// </summary>
    InputProcessingError,

    /// <summary>
    ///     Error processing mouse button events.
    /// </summary>
    ButtonProcessingError,

    /// <summary>
    ///     Error processing mouse motion events.
    /// </summary>
    MotionProcessingError,

    /// <summary>
    ///     Error processing mouse wheel events.
    /// </summary>
    WheelProcessingError,

    /// <summary>
    ///     Error with mouse capture operations.
    /// </summary>
    CaptureError
}