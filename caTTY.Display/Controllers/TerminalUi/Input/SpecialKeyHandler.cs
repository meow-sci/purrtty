using System;
using Brutal.ImGuiApi;
using caTTY.Core.Input;
using caTTY.Core.Utils;

namespace caTTY.Display.Controllers.TerminalUi.Input;

/// <summary>
///     Handles special key presses including function keys, navigation keys, and control sequences.
///     Translates ImGui key events into terminal escape sequences.
/// </summary>
public class SpecialKeyHandler
{
  private readonly Action<string> _sendToProcess;

  public SpecialKeyHandler(Action<string> sendToProcess)
  {
    _sendToProcess = sendToProcess;
  }

  /// <summary>
  ///     Handles special key presses (arrows, function keys, Enter, etc.).
  ///     Provides comprehensive key handling matching TypeScript implementation.
  /// </summary>
  /// <returns>True if a special key was handled, false otherwise</returns>
  public bool HandleSpecialKeys(KeyModifiers modifiers, bool applicationCursorKeys, Action markUserInput)
  {
    // Define key mappings from ImGuiKey to string for non-text keys
    var keyMappings = new[]
    {
            // Basic keys
            (ImGuiKey.Enter, "Enter"),
            (ImGuiKey.Backspace, "Backspace"),
            (ImGuiKey.Tab, "Tab"),
            (ImGuiKey.Escape, "Escape"),

            // Arrow keys
            (ImGuiKey.UpArrow, "ArrowUp"),
            (ImGuiKey.DownArrow, "ArrowDown"),
            (ImGuiKey.RightArrow, "ArrowRight"),
            (ImGuiKey.LeftArrow, "ArrowLeft"),

            // Navigation keys
            (ImGuiKey.Home, "Home"),
            (ImGuiKey.End, "End"),
            (ImGuiKey.Delete, "Delete"),
            (ImGuiKey.Insert, "Insert"),
            (ImGuiKey.PageUp, "PageUp"),
            (ImGuiKey.PageDown, "PageDown"),

            // Function keys
            (ImGuiKey.F1, "F1"),
            (ImGuiKey.F2, "F2"),
            (ImGuiKey.F3, "F3"),
            (ImGuiKey.F4, "F4"),
            (ImGuiKey.F5, "F5"),
            (ImGuiKey.F6, "F6"),
            (ImGuiKey.F7, "F7"),
            (ImGuiKey.F8, "F8"),
            (ImGuiKey.F9, "F9"),
            (ImGuiKey.F10, "F10"),
            (ImGuiKey.F11, "F11"),
            (ImGuiKey.F12, "F12")
        };

    // Process each key mapping
    foreach (var (imguiKey, keyString) in keyMappings)
    {
      if (ImGui.IsKeyPressed(imguiKey))
      {
          // Special case: Don't process F12 in GameMod context to avoid conflict with terminal toggle
        // In GameMod, F12 is reserved for terminal visibility toggle
        if (keyString == "F12")
        {
          // Console.WriteLine($"DEBUG: F12 pressed in GameMod context, skipping F12 processing to avoid conflict");
          return false; // Let F12 be handled by GameMod
        }

        // Use the keyboard input encoder to get the proper sequence
        string? encoded = KeyboardInputEncoder.EncodeKeyEvent(keyString, modifiers, applicationCursorKeys);

        if (encoded != null)
        {
          markUserInput();
          _sendToProcess(encoded);
          return true; // Special key was handled
        }
      }
    }

    // Handle Ctrl+letter combinations separately (only when Ctrl is pressed)
    if (modifiers.Ctrl)
    {
      var letterKeys = new[]
      {
        (ImGuiKey.A, "a"), (ImGuiKey.B, "b"), (ImGuiKey.C, "c"), (ImGuiKey.D, "d"),
        (ImGuiKey.E, "e"), (ImGuiKey.F, "f"), (ImGuiKey.G, "g"), (ImGuiKey.H, "h"),
        (ImGuiKey.I, "i"), (ImGuiKey.J, "j"), (ImGuiKey.K, "k"), (ImGuiKey.L, "l"),
        (ImGuiKey.M, "m"), (ImGuiKey.N, "n"), (ImGuiKey.O, "o"), (ImGuiKey.P, "p"),
        (ImGuiKey.Q, "q"), (ImGuiKey.R, "r"), (ImGuiKey.S, "s"), (ImGuiKey.T, "t"),
        (ImGuiKey.U, "u"), (ImGuiKey.V, "v"), (ImGuiKey.W, "w"), (ImGuiKey.X, "x"),
        (ImGuiKey.Y, "y"), (ImGuiKey.Z, "z")
      };

      foreach (var (imguiKey, keyString) in letterKeys)
      {
        if (ImGui.IsKeyPressed(imguiKey))
        {
          // Use the keyboard input encoder to get the proper Ctrl+letter sequence
          string? encoded = KeyboardInputEncoder.EncodeKeyEvent(keyString, modifiers, applicationCursorKeys);

          if (encoded != null)
          {
            markUserInput();
            _sendToProcess(encoded);
            return true; // Ctrl+letter was handled
          }
        }
      }
    }

    // Handle keypad keys (minimal implementation as requested)
    if (ImGui.IsKeyPressed(ImGuiKey.KeypadEnter))
    {
      markUserInput();
      _sendToProcess("\r"); // Treat keypad Enter same as regular Enter for now
      return true;
    }

    return false; // No special key was handled
  }
}
