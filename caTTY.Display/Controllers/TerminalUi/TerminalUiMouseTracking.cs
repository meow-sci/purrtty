using System;
using System.Text;
using Brutal.ImGuiApi;
using caTTY.Core.Input;
using caTTY.Core.Managers;
using caTTY.Core.Terminal;
using caTTY.Core.Types;
using caTTY.Display.Input;
using caTTY.Display.Types;
using caTTY.Display.Utils;
using float2 = Brutal.Numerics.float2;

namespace caTTY.Display.Controllers.TerminalUi;

/// <summary>
///     Handles mouse tracking for applications, coordinate conversion, and mouse event processing.
///     This class manages mouse tracking modes, escape sequence generation, and coordinate transformations.
/// </summary>
public class TerminalUiMouseTracking
{
  private readonly TerminalController _controller;
  private readonly SessionManager _sessionManager;
  private readonly MouseTrackingManager _mouseTrackingManager;
  private readonly MouseStateManager _mouseStateManager;
  private readonly CoordinateConverter _coordinateConverter;
  private readonly MouseEventProcessor _mouseEventProcessor;
  private readonly MouseInputHandler _mouseInputHandler;

  public TerminalUiMouseTracking(
      TerminalController controller,
      SessionManager sessionManager,
      MouseTrackingManager mouseTrackingManager,
      MouseStateManager mouseStateManager,
      CoordinateConverter coordinateConverter,
      MouseEventProcessor mouseEventProcessor,
      MouseInputHandler mouseInputHandler)
  {
    _controller = controller;
    _sessionManager = sessionManager;
    _mouseTrackingManager = mouseTrackingManager;
    _mouseStateManager = mouseStateManager;
    _coordinateConverter = coordinateConverter;
    _mouseEventProcessor = mouseEventProcessor;
    _mouseInputHandler = mouseInputHandler;
  }

  /// <summary>
  /// Handles mouse tracking for applications (separate from local selection).
  /// This method processes mouse events for terminal applications that request mouse tracking.
  /// </summary>
  public void HandleMouseTrackingForApplications()
  {
    try
    {
      // Sync mouse tracking configuration from terminal state
      SyncMouseTrackingConfiguration();

      // Only process if mouse tracking is enabled
      var config = _mouseTrackingManager.Configuration;
      if (config.Mode == MouseTrackingMode.Off)
      {
        return; // No mouse tracking requested
      }

      // Update mouse input handler with current terminal state
      _mouseInputHandler.SetTerminalFocus(_controller.HasFocus);

      // Update coordinate converter with current terminal metrics
      UpdateCoordinateConverterMetrics();

      // Update terminal size for mouse input handler
      var terminalSize = new float2(_controller._lastTerminalSize.X, _controller._lastTerminalSize.Y);
      var activeSession = _sessionManager.ActiveSession;
      if (activeSession != null)
      {
        _mouseInputHandler.UpdateTerminalSize(terminalSize, activeSession.Terminal.Width, activeSession.Terminal.Height);
      }

      // Process mouse input through the mouse input handler
      _mouseInputHandler.HandleMouseInput();
    }
    catch (Exception ex)
    {
      Console.WriteLine($"Error in mouse tracking for applications: {ex.Message}");
    }
  }

  /// <summary>
  /// Handles integrated mouse input for both application tracking and local selection.
  /// This method coordinates between mouse tracking for applications and local selection handling.
  /// </summary>
  public void HandleMouseInputIntegrated(Action handleMouseInputForTerminal)
  {
    try
    {
      // Sync mouse tracking configuration from terminal state
      SyncMouseTrackingConfiguration();

      // Update mouse input handler with current terminal state
      _mouseInputHandler.SetTerminalFocus(_controller.HasFocus);

      // Update coordinate converter with current terminal metrics
      UpdateCoordinateConverterMetrics();

      // Update terminal size for mouse input handler
      var terminalSize = new float2(_controller._lastTerminalSize.X, _controller._lastTerminalSize.Y);
      var activeSession = _sessionManager.ActiveSession;
      if (activeSession != null)
      {
        _mouseInputHandler.UpdateTerminalSize(terminalSize, activeSession.Terminal.Width, activeSession.Terminal.Height);
      }

      // Process mouse input through the mouse input handler
      _mouseInputHandler.HandleMouseInput();

      // Also handle local selection (existing functionality)
      // This runs after mouse tracking to allow shift-key bypass
      handleMouseInputForTerminal();
    }
    catch (Exception ex)
    {
      Console.WriteLine($"Error in integrated mouse input handling: {ex.Message}");
    }
  }

  /// <summary>
  /// Updates the coordinate converter with current terminal metrics.
  /// </summary>
  public void UpdateCoordinateConverterMetrics()
  {
    // Update coordinate converter with current font metrics
    _coordinateConverter.UpdateMetrics(
      _controller.CurrentCharacterWidth,
      _controller.CurrentLineHeight,
      _controller._lastTerminalOrigin);
  }

  /// <summary>
  /// Synchronizes mouse tracking configuration from terminal state to mouse tracking manager.
  /// </summary>
  public void SyncMouseTrackingConfiguration()
  {
    try
    {
      var activeSession = _sessionManager.ActiveSession;
      if (activeSession == null) return;

      var terminalState = ((TerminalEmulator)activeSession.Terminal).State;

      // Convert terminal state mouse tracking bits to MouseTrackingMode
      MouseTrackingMode mode = MouseTrackingMode.Off;

      // Check bits in priority order (highest mode wins)
      if ((terminalState.MouseTrackingModeBits & 4) != 0) // 1003 bit
      {
        mode = MouseTrackingMode.Any;
      }
      else if ((terminalState.MouseTrackingModeBits & 2) != 0) // 1002 bit
      {
        mode = MouseTrackingMode.Button;
      }
      else if ((terminalState.MouseTrackingModeBits & 1) != 0) // 1000 bit
      {
        mode = MouseTrackingMode.Click;
      }

      // Only update if mode changed
      if (_mouseTrackingManager.CurrentMode != mode)
      {
        Console.WriteLine($"[DEBUG] Mouse tracking mode changed: {_mouseTrackingManager.CurrentMode} -> {mode} (bits={terminalState.MouseTrackingModeBits})");
        _mouseTrackingManager.SetTrackingMode(mode);
      }

      // Only update if SGR encoding changed
      if (_mouseTrackingManager.SgrEncodingEnabled != terminalState.MouseSgrEncodingEnabled)
      {
        Console.WriteLine($"[DEBUG] SGR encoding changed: {_mouseTrackingManager.SgrEncodingEnabled} -> {terminalState.MouseSgrEncodingEnabled}");
        _mouseTrackingManager.SetSgrEncoding(terminalState.MouseSgrEncodingEnabled);
      }
    }
    catch (Exception ex)
    {
      Console.WriteLine($"Error syncing mouse tracking configuration: {ex.Message}");
    }
  }

  /// <summary>
  /// Converts mouse coordinates to terminal cell coordinates (0-based).
  /// </summary>
  /// <returns>The cell coordinates, or null if the mouse is outside the terminal area</returns>
  public SelectionPosition? GetMouseCellCoordinates()
  {
    var mouse = ImGui.GetMousePos();

    float relX = mouse.X - _controller._lastTerminalOrigin.X;
    float relY = mouse.Y - _controller._lastTerminalOrigin.Y;

    // Check if mouse is within terminal bounds
    if (relX < 0 || relY < 0 || relX >= _controller._lastTerminalSize.X || relY >= _controller._lastTerminalSize.Y)
    {
      return null;
    }

    int col = (int)Math.Floor(relX / Math.Max(1e-6f, _controller.CurrentCharacterWidth));
    int row = (int)Math.Floor(relY / Math.Max(1e-6f, _controller.CurrentLineHeight));

    var activeSession = _sessionManager.ActiveSession;
    if (activeSession == null) return null;

    col = Math.Max(0, Math.Min(activeSession.Terminal.Width - 1, col));
    row = Math.Max(0, Math.Min(activeSession.Terminal.Height - 1, row));

    return new SelectionPosition(row, col);
  }

  /// <summary>
  /// Checks if the mouse is currently over the terminal content area.
  /// This is used to prevent window dragging when selecting text in the terminal.
  /// </summary>
  /// <param name="mousePos">The current mouse position in screen coordinates</param>
  /// <returns>True if the mouse is over the terminal content area, false otherwise</returns>
  public bool IsMouseOverTerminal(float2 mousePos)
  {
    float relX = mousePos.X - _controller._lastTerminalOrigin.X;
    float relY = mousePos.Y - _controller._lastTerminalOrigin.Y;

    // Check if mouse is within terminal bounds
    return relX >= 0 && relY >= 0 && relX < _controller._lastTerminalSize.X && relY < _controller._lastTerminalSize.Y;
  }

  /// <summary>
  ///     Handles mouse events that should be sent to the application as escape sequences.
  /// </summary>
  public void OnMouseEventGenerated(object? sender, MouseEventArgs e)
  {
    try
    {
      var mouseEvent = e.MouseEvent;
      var config = _mouseTrackingManager.Configuration;

      // Generate the appropriate escape sequence
      string? escapeSequence = mouseEvent.Type switch
      {
        MouseEventType.Press => EscapeSequenceGenerator.GenerateMousePress(
          mouseEvent.Button, mouseEvent.X1, mouseEvent.Y1, mouseEvent.Modifiers, config.SgrEncodingEnabled),
        MouseEventType.Release => EscapeSequenceGenerator.GenerateMouseRelease(
          mouseEvent.Button, mouseEvent.X1, mouseEvent.Y1, mouseEvent.Modifiers, config.SgrEncodingEnabled),
        MouseEventType.Motion => EscapeSequenceGenerator.GenerateMouseMotion(
          mouseEvent.Button, mouseEvent.X1, mouseEvent.Y1, mouseEvent.Modifiers, config.SgrEncodingEnabled),
        MouseEventType.Wheel => EscapeSequenceGenerator.GenerateMouseWheel(
          mouseEvent.Button == MouseButton.WheelUp, mouseEvent.X1, mouseEvent.Y1, mouseEvent.Modifiers, config.SgrEncodingEnabled),
        _ => null
      };

      if (escapeSequence != null)
      {
        // Send directly to active session's process manager (primary data path)
        var activeSession = _sessionManager.ActiveSession;
        if (activeSession?.ProcessManager.IsRunning == true)
        {
          activeSession.ProcessManager.Write(escapeSequence);
        }

        // Also raise the DataInput event for external subscribers (monitoring/logging)
        byte[] bytes = Encoding.UTF8.GetBytes(escapeSequence);
        _controller.RaiseDataInput(escapeSequence, bytes);
      }
      else
      {
        Console.WriteLine($"[WARN] No escape sequence generated for event type: {mouseEvent.Type}");
      }
    }
    catch (Exception ex)
    {
      Console.WriteLine($"Error generating mouse escape sequence: {ex.Message}");
    }
  }

  /// <summary>
  ///     Handles mouse events that should be processed locally (selection, scrolling).
  /// </summary>
  public void OnLocalMouseEvent(object? sender, MouseEventArgs e)
  {
    // Local mouse events are handled by the existing selection system
    // This is where we could extend local mouse handling if needed
    // For now, selection is handled directly in HandleMouseInputForTerminal()
  }

  /// <summary>
  ///     Handles mouse processing errors.
  /// </summary>
  public void OnMouseProcessingError(object? sender, MouseProcessingErrorEventArgs e)
  {
    Console.WriteLine($"[ERROR] Mouse processing error: {e.Message}");
    if (e.Exception != null)
    {
      Console.WriteLine($"Exception: {e.Exception}");
    }
  }

  /// <summary>
  ///     Handles mouse input errors.
  /// </summary>
  public void OnMouseInputError(object? sender, MouseInputErrorEventArgs e)
  {
    Console.WriteLine($"Mouse input error: {e.Message}");
    if (e.Exception != null)
    {
      Console.WriteLine($"Exception: {e.Exception}");
    }
  }
}
