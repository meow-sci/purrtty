using caTTY.Core.Terminal;
using caTTY.Display.Configuration;
using caTTY.Display.Controllers.TerminalUi.Input;
using caTTY.Display.Rendering;

namespace caTTY.Display.Controllers.TerminalUi;

/// <summary>
///     Coordinates input handling for the terminal including keyboard, mouse wheel, and focus management.
///     Delegates to specialized component classes for input processing.
/// </summary>
public class TerminalUiInput
{
  private readonly KeyboardInputHandler _keyboardInputHandler;
  private readonly InputFocusManager _focusManager;
  private readonly MouseWheelHandler _mouseWheelHandler;

  public TerminalUiInput(
      TerminalController controller,
      SessionManager sessionManager,
      CursorRenderer cursorRenderer,
      MouseWheelScrollConfig scrollConfig)
  {
    _focusManager = new InputFocusManager(controller);
    _keyboardInputHandler = new KeyboardInputHandler(
        sessionManager,
        cursorRenderer,
        controller.SendToProcess);
    _mouseWheelHandler = new MouseWheelHandler(
        controller,
        sessionManager,
        scrollConfig,
        _focusManager,
        controller.SendToProcess);
  }

  /// <summary>
  ///     Manages input capture state using ImGui's keyboard capture mechanism.
  ///     This ensures the terminal receives keyboard input when focused and visible.
  /// </summary>
  public void ManageInputCapture()
  {
    _focusManager.ManageInputCapture();
  }

  /// <summary>
  ///     Determines whether the terminal should capture input.
  ///     Terminal captures input only when both focused and visible.
  /// </summary>
  public bool ShouldCaptureInput()
  {
    return _focusManager.ShouldCaptureInput();
  }

  /// <summary>
  ///     Handles all input for the terminal including keyboard and mouse wheel.
  ///     Delegates to specialized input handler components.
  /// </summary>
  public void HandleInput()
  {
    // Verify focus state before processing input (defensive programming)
    if (!_focusManager.ShouldProcessInput())
    {
      return;
    }

    // Note: Input capture is now managed centrally in ManageInputCapture() using SetNextFrameWantCaptureKeyboard()
    // Note: Mouse input for selection is now handled in RenderTerminalContent()
    // via the invisible button approach to prevent window dragging

    // Handle mouse wheel input first
    _mouseWheelHandler.HandleMouseWheelInput();

    // Handle keyboard input via KeyboardInputHandler
    _keyboardInputHandler.HandleKeyboardInput();
  }
}
