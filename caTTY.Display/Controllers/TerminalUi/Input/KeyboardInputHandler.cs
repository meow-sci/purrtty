using System;
using Brutal.ImGuiApi;
using caTTY.Core.Input;
using caTTY.Core.Managers;
using caTTY.Core.Terminal;
using caTTY.Core.Utils;
using caTTY.Display.Rendering;

namespace caTTY.Display.Controllers.TerminalUi.Input;

/// <summary>
///     Handles keyboard input processing for the terminal including special keys and text input.
///     Processes key events, modifiers, and character input.
/// </summary>
public class KeyboardInputHandler
{
  private readonly SessionManager _sessionManager;
  private readonly CursorRenderer _cursorRenderer;
  private readonly Action<string> _sendToProcess;
  private readonly SpecialKeyHandler _specialKeyHandler;

  public KeyboardInputHandler(
      SessionManager sessionManager,
      CursorRenderer cursorRenderer,
      Action<string> sendToProcess)
  {
    _sessionManager = sessionManager;
    _cursorRenderer = cursorRenderer;
    _sendToProcess = sendToProcess;
    _specialKeyHandler = new SpecialKeyHandler(sendToProcess);
  }

  /// <summary>
  ///     Handles all keyboard input for the terminal including special keys and text input.
  /// </summary>
  public void HandleKeyboardInput()
  {
    ImGuiIOPtr io = ImGui.GetIO();

    // Any user input (typing/keypresses that generate terminal input) should snap to the latest output.
    // This is intentionally independent from new-content behavior.
    bool userProvidedInputThisFrame = false;
    void MarkUserInput()
    {
      if (userProvidedInputThisFrame)
      {
        return;
      }

      userProvidedInputThisFrame = true;
      var activeSession = _sessionManager.ActiveSession;
      activeSession?.Terminal.ScrollbackManager?.OnUserInput();

      // Make cursor immediately visible when user provides input
      _cursorRenderer.ForceVisible();
    }

    // Get current terminal state for input encoding
    var activeSession = _sessionManager.ActiveSession;
    if (activeSession == null) return;

    var terminalState = ((TerminalEmulator)activeSession.Terminal).State;
    bool applicationCursorKeys = terminalState.ApplicationCursorKeys;

    // Create modifier state from ImGui
    var modifiers = new KeyModifiers(
        shift: io.KeyShift,
        alt: io.KeyAlt,
        ctrl: io.KeyCtrl,
        meta: false // ImGui doesn't expose Meta key directly
    );

    // Handle special keys first (they take priority over text input)
    bool specialKeyHandled = _specialKeyHandler.HandleSpecialKeys(modifiers, applicationCursorKeys, MarkUserInput);

    // Only handle text input if no special key was processed
    // This prevents double-sending when a key produces both a key event and text input
    if (!specialKeyHandled && io.InputQueueCharacters.Count > 0)
    {
      for (int i = 0; i < io.InputQueueCharacters.Count; i++)
      {
        char ch = (char)io.InputQueueCharacters[i];
        if (ch >= 32 && ch < 127) // Printable ASCII
        {
          MarkUserInput();
          _sendToProcess(ch.ToString());
        }
      }
    }
  }

}
