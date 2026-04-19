using System;
using Brutal.ImGuiApi;
using caTTY.Display.Controllers;

namespace caTTY.Display.Controllers.TerminalUi.Input;

/// <summary>
///     Manages input focus state and input capture for the terminal window.
///     This class is responsible for determining when the terminal should capture input
///     and managing ImGui's input capture mechanism.
/// </summary>
public class InputFocusManager
{
  private readonly TerminalController _controller;

  public InputFocusManager(TerminalController controller)
  {
    _controller = controller;
  }

  /// <summary>
  ///     Manages input capture state using ImGui's keyboard capture mechanism.
  ///     This ensures the terminal receives keyboard input when focused and visible.
  /// </summary>
  public void ManageInputCapture()
  {
    try
    {

      // Invisible input widget
      // even this doesn't fully prevent KSA from processing global hot keys like 'm'

      // ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.5f);
      // // Dummy buffer for InputText - we don't need the captured text so use a local stack buffer
      // ReadOnlySpan<byte> dummySpan = stackalloc byte[64];
      // ImGui.InputText("##hidden", dummySpan, ImGuiInputTextFlags.None);
      // ImGui.PopStyleVar();

      // Console.WriteLine($"IsInputCaptureActive={IsInputCaptureActive}");

      // ImGui.GetIO().WantCaptureKeyboard = true;
      // Console.WriteLine($"ImGui.GetIO().WantCaptureKeyboard {ImGui.GetIO().WantCaptureKeyboard}");
      // ImGui.SetNextFrameWantCaptureKeyboard(true);
      // ImGui.SetKeyboardFocusHere();

      // Use SetNextFrameWantCaptureKeyboard when terminal should capture input
      // This tells ImGui (and the game) that we want exclusive keyboard access for the next frame
      // This is the proper way to capture keyboard input in KSA game context
      if (_controller.IsInputCaptureActive)
      {
        // ImGui.SetKeyboardFocusHere();

        // TODO: FIXME: this still doesn't prevent global hotkeys like 'm' from taking place
        // ImGui.SetNextFrameWantCaptureKeyboard(true);
        // ImGui.SetKeyboardFocusHere();
        // Console.WriteLine("TerminalController: Capturing keyboard input (suppressing game hotkeys)");
      }
      // Note: No need to explicitly set to false due to ImGui immediate mode design
      // Just don't call SetNextFrameWantCaptureKeyboard when terminal shouldn't capture input
    }
    catch (Exception ex)
    {
      Console.WriteLine($"TerminalController: Error managing input capture: {ex.Message}");
    }
  }

  /// <summary>
  ///     Determines whether the terminal should capture input.
  ///     Terminal captures input only when both focused and visible.
  /// </summary>
  public bool ShouldCaptureInput()
  {
    // Terminal captures input only when both focused and visible
    // This matches the TypeScript implementation's input priority management
    return _controller.IsInputCaptureActive;
  }

  /// <summary>
  ///     Verifies that the terminal has focus and is visible before processing input.
  ///     This is a defensive check to ensure input should be processed.
  /// </summary>
  /// <returns>True if the terminal should process input, false otherwise</returns>
  public bool ShouldProcessInput()
  {
    // Verify focus state before processing input (defensive programming)
    return _controller.HasFocus && _controller.IsVisible;
  }

  /// <summary>
  ///     Checks if the terminal currently has focus for mouse wheel input.
  /// </summary>
  /// <returns>True if the terminal has focus, false otherwise</returns>
  public bool HasFocusForMouseWheel()
  {
    // Only process mouse wheel events when terminal has focus
    return _controller.HasFocus;
  }
}
