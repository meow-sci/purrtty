using System;
using System.Linq;
using Brutal.ImGuiApi;
using caTTY.Core.Input;
using caTTY.Core.Terminal;
using caTTY.Core.Utils;
using caTTY.Display.Configuration;
using caTTY.Display.Controllers;

namespace caTTY.Display.Controllers.TerminalUi.Input;

/// <summary>
///     Handles mouse wheel input for scrolling through terminal history.
///     Processes mouse wheel events with smooth scrolling, accumulation, and error handling.
/// </summary>
public class MouseWheelHandler
{
  private readonly TerminalController _controller;
  private readonly SessionManager _sessionManager;
  private readonly MouseWheelScrollConfig _scrollConfig;
  private readonly InputFocusManager _focusManager;
  private readonly Action<string> _sendToProcess;
  private float _wheelAccumulator = 0.0f;

  public MouseWheelHandler(
      TerminalController controller,
      SessionManager sessionManager,
      MouseWheelScrollConfig scrollConfig,
      InputFocusManager focusManager,
      Action<string> sendToProcess)
  {
    _controller = controller;
    _sessionManager = sessionManager;
    _scrollConfig = scrollConfig;
    _focusManager = focusManager;
    _sendToProcess = sendToProcess;
  }

  /// <summary>
  ///     Handles mouse wheel input for scrolling through terminal history.
  ///     Only processes wheel events when the terminal window has focus and the wheel delta
  ///     exceeds the minimum threshold to prevent micro-movements.
  ///     Includes comprehensive error handling and input validation.
  /// </summary>
  public void HandleMouseWheelInput()
  {
    try
    {
      // Only process mouse wheel events when terminal has focus
      if (!_focusManager.HasFocusForMouseWheel())
      {
        return;
      }

      var io = ImGui.GetIO();
      float wheelDelta = io.MouseWheel;

      // Check if wheel delta exceeds minimum threshold to prevent micro-movements
      if (Math.Abs(wheelDelta) < _scrollConfig.MinimumWheelDelta)
      {
        return;
      }

      // Validate wheel delta for NaN/infinity - critical for robustness
      if (!float.IsFinite(wheelDelta))
      {
        Console.WriteLine($"TerminalController: Invalid wheel delta detected (NaN/Infinity): {wheelDelta}, ignoring");

        // Reset accumulator to prevent corruption from invalid values
        _wheelAccumulator = 0.0f;
        return;
      }

      // Additional validation for extreme values that could cause issues
      if (Math.Abs(wheelDelta) > 1000.0f)
      {
        Console.WriteLine($"TerminalController: Extreme wheel delta detected: {wheelDelta}, clamping");
        wheelDelta = Math.Sign(wheelDelta) * 10.0f; // Clamp to reasonable range
      }

      // Process the wheel scroll with validated input
      ProcessMouseWheelScroll(wheelDelta);
    }
    catch (Exception ex)
    {
      // Log detailed error information for debugging
      Console.WriteLine($"TerminalController: Mouse wheel handling error: {ex.GetType().Name}: {ex.Message}");

      // Reset accumulator to prevent stuck state - critical for recovery
      _wheelAccumulator = 0.0f;

      // Log stack trace for debugging in development builds
#if DEBUG
      Console.WriteLine($"TerminalController: Stack trace: {ex.StackTrace}");
#endif
    }
  }

  /// <summary>
  ///     Processes mouse wheel scroll by accumulating wheel deltas and converting to line scrolls.
  ///     Implements smooth scrolling with fractional accumulation and overflow protection.
  ///     Integrates with ScrollbackManager for proper scrolling behavior and boundary handling.
  ///     Includes comprehensive error handling and recovery mechanisms.
  /// </summary>
  /// <param name="wheelDelta">The mouse wheel delta value from ImGui</param>
  private void ProcessMouseWheelScroll(float wheelDelta)
  {
    try
    {
      // Additional input validation - should already be done in HandleMouseWheelInput,
      // but defensive programming requires validation at each level
      if (!float.IsFinite(wheelDelta))
      {
        Console.WriteLine($"TerminalController: Invalid wheel delta in ProcessMouseWheelScroll: {wheelDelta}");
        _wheelAccumulator = 0.0f;
        return;
      }

      // Accumulate wheel delta for smooth scrolling
      _wheelAccumulator += wheelDelta * _scrollConfig.LinesPerStep;

      // Prevent accumulator overflow - critical for stability
      if (Math.Abs(_wheelAccumulator) > 100.0f)
      {
        Console.WriteLine($"TerminalController: Wheel accumulator overflow detected: {_wheelAccumulator}, clamping");
        _wheelAccumulator = Math.Sign(_wheelAccumulator) * 10.0f;
      }

      // Extract integer scroll lines
      int scrollLines = (int)Math.Floor(Math.Abs(_wheelAccumulator));
      if (scrollLines == 0)
      {
        return;
      }

      // Determine scroll direction (positive wheel delta = scroll up)
      bool scrollUp = _wheelAccumulator > 0;

      // Clamp to maximum lines per operation - prevents excessive scrolling
      scrollLines = Math.Min(scrollLines, _scrollConfig.MaxLinesPerOperation);

      var activeSession = _sessionManager.ActiveSession;
      if (activeSession == null)
      {
        _wheelAccumulator = 0.0f;
        return;
      }

      var emulator = (TerminalEmulator)activeSession.Terminal;
      var state = emulator.State;

      // Match catty-web behavior:
      // - If mouse reporting is enabled, wheel events go to the running app (PTY), not local scrollback.
      // - If alternate screen is active and mouse reporting is off, translate wheel into arrow/page keys.
      // - Otherwise, wheel scrolls local scrollback.
      if (state.IsMouseReportingEnabled)
      {
        var (x1, y1) = GetMouseCellCoordinates1Based();

        // ImGui: wheelDelta > 0 means scroll up; xterm wheel uses button 64 for up.
        string seq = MouseInputEncoder.EncodeMouseWheel(
            directionUp: scrollUp,
            x1: x1,
            y1: y1,
            shift: ImGui.GetIO().KeyShift,
            alt: ImGui.GetIO().KeyAlt,
            ctrl: ImGui.GetIO().KeyCtrl,
            sgrEncoding: state.MouseSgrEncodingEnabled
        );

        _sendToProcess(seq);

        // Consume the delta since we've emitted input.
        float consumedDelta = scrollLines * (scrollUp ? 1 : -1);
        _wheelAccumulator -= consumedDelta;
        return;
      }

      if (state.IsAlternateScreenActive)
      {
        string seq = EncodeAltScreenWheelAsKeys(scrollUp, scrollLines, activeSession.Terminal.Height, state.ApplicationCursorKeys);
        if (!string.IsNullOrEmpty(seq))
        {
          _sendToProcess(seq);
        }

        float consumedDelta = scrollLines * (scrollUp ? 1 : -1);
        _wheelAccumulator -= consumedDelta;
        return;
      }

      // Store current viewport state for boundary condition handling and error recovery
      var scrollbackManager = activeSession.Terminal.ScrollbackManager;
      if (scrollbackManager == null)
      {
        Console.WriteLine("TerminalController: ScrollbackManager is null, cannot process wheel scroll");
        _wheelAccumulator = 0.0f;
        return;
      }

      int previousOffset = scrollbackManager.ViewportOffset;
      bool wasAtBottom = scrollbackManager.IsAtBottom;

      // Apply scrolling via ScrollbackManager with comprehensive error handling
      try
      {
        if (scrollUp)
        {
          scrollbackManager.ScrollUp(scrollLines);
        }
        else
        {
          scrollbackManager.ScrollDown(scrollLines);
        }

        // Check if scrolling actually occurred (boundary condition handling)
        int newOffset = scrollbackManager.ViewportOffset;
        bool actuallyScrolled = (newOffset != previousOffset);

        if (!actuallyScrolled)
        {
          // Hit boundary - clear accumulator to prevent stuck scrolling
          // This is critical for user experience at scroll boundaries
          _wheelAccumulator = 0.0f;

#if DEBUG
          if (scrollUp)
          {
            // Console.WriteLine("TerminalController: Scroll up hit top boundary, clearing accumulator");
          }
          else if (!wasAtBottom)
          {
            // Console.WriteLine("TerminalController: Scroll down hit bottom boundary, clearing accumulator");
          }
#endif
        }
        else
        {
          // Successfully scrolled - consume the delta that was actually processed
          // This maintains fractional accumulation for smooth scrolling
          float consumedDelta = scrollLines * (scrollUp ? 1 : -1);
          _wheelAccumulator -= consumedDelta;

          // Clamp accumulator to prevent excessive buildup in one direction
          // This prevents issues with rapid scrolling and ensures responsive reversal
          float maxAccumulator = _scrollConfig.LinesPerStep * 2.0f;
          if (Math.Abs(_wheelAccumulator) > maxAccumulator)
          {
            _wheelAccumulator = Math.Sign(_wheelAccumulator) * maxAccumulator;
          }
        }
      }
      catch (Exception ex)
      {
        // Catch any errors during scrolling and reset to safe state
        Console.WriteLine($"TerminalController: Error during scrollback scroll: {ex.GetType().Name}: {ex.Message}");
        _wheelAccumulator = 0.0f;

        // Attempt to recover to bottom position (most common expected state)
        try
        {
          scrollbackManager?.ScrollToBottom();
        }
        catch
        {
          // If recovery fails, just continue - we've already logged the error
        }
      }
    }
    catch (Exception ex)
    {
      // Outer catch for any unexpected errors in scroll processing logic itself
      Console.WriteLine($"TerminalController: Unexpected error in ProcessMouseWheelScroll: {ex.GetType().Name}: {ex.Message}");
      _wheelAccumulator = 0.0f;

#if DEBUG
      Console.WriteLine($"TerminalController: Stack trace: {ex.StackTrace}");
#endif
    }
  }

  /// <summary>
  ///     Converts mouse coordinates to terminal cell coordinates (1-based for mouse reporting).
  /// </summary>
  private (int x1, int y1) GetMouseCellCoordinates1Based()
  {
    // Mouse position is in screen coordinates.
    var mouse = ImGui.GetMousePos();

    float relX = mouse.X - _controller._lastTerminalOrigin.X;
    float relY = mouse.Y - _controller._lastTerminalOrigin.Y;

    int col0 = (int)Math.Floor(relX / Math.Max(1e-6f, _controller.CurrentCharacterWidth));
    int row0 = (int)Math.Floor(relY / Math.Max(1e-6f, _controller.CurrentLineHeight));

    var activeSession = _sessionManager.ActiveSession;
    if (activeSession == null) return (1, 1);

    col0 = Math.Max(0, Math.Min(activeSession.Terminal.Width - 1, col0));
    row0 = Math.Max(0, Math.Min(activeSession.Terminal.Height - 1, row0));

    return (col0 + 1, row0 + 1);
  }

  /// <summary>
  ///     Encodes mouse wheel scrolling in alternate screen mode as arrow or page keys.
  /// </summary>
  private static string EncodeAltScreenWheelAsKeys(bool directionUp, int lines, int rows, bool applicationCursorKeys)
  {
    if (lines <= 0)
    {
      return string.Empty;
    }

    rows = Math.Max(1, rows);

    // If the wheel delta is effectively a full page, use PageUp/PageDown.
    if (lines >= rows)
    {
      int pages = Math.Max(1, Math.Min(10, (int)Math.Round(lines / (double)rows)));
      string seq = directionUp ? "\x1b[5~" : "\x1b[6~";
      return string.Concat(Enumerable.Repeat(seq, pages));
    }

    int absLines = Math.Max(1, Math.Min(rows * 3, lines));
    string arrow = directionUp
        ? (applicationCursorKeys ? "\x1bOA" : "\x1b[A")
        : (applicationCursorKeys ? "\x1bOB" : "\x1b[B");
    return string.Concat(Enumerable.Repeat(arrow, absLines));
  }
}
