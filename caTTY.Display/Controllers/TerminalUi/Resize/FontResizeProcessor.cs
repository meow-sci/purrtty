using System;
using Brutal.ImGuiApi;
using caTTY.Core.Terminal;
using float2 = Brutal.Numerics.float2;

namespace caTTY.Display.Controllers.TerminalUi.Resize;

/// <summary>
///     Handles font-triggered terminal resize operations.
///     Manages the pending resize flag and processes font-driven dimension changes
///     for both single sessions and all sessions.
/// </summary>
internal class FontResizeProcessor
{
  private readonly SessionManager _sessionManager;
  private readonly TerminalDimensionCalculator _dimensionCalculator;
  private readonly WindowResizeHandler _windowResizeHandler;
  private readonly Action<int, int>? _onResizeComplete;

  // Font resize tracking
  private bool _fontResizePending = false; // Flag to trigger resize on next render frame

  public FontResizeProcessor(
    SessionManager sessionManager,
    TerminalDimensionCalculator dimensionCalculator,
    WindowResizeHandler windowResizeHandler,
    Action<int, int>? onResizeComplete = null)
  {
    _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
    _dimensionCalculator = dimensionCalculator ?? throw new ArgumentNullException(nameof(dimensionCalculator));
    _windowResizeHandler = windowResizeHandler ?? throw new ArgumentNullException(nameof(windowResizeHandler));
    _onResizeComplete = onResizeComplete;
  }

  /// <summary>
  ///     Gets whether a font-triggered resize is pending.
  /// </summary>
  public bool IsFontResizePending => _fontResizePending;

  /// <summary>
  ///     Triggers terminal resize calculation based on current window size and updated character metrics.
  ///     This method is called when font configuration changes to ensure terminal dimensions
  ///     are recalculated with the new character metrics without requiring manual window resize.
  /// </summary>
  public void TriggerTerminalResize()
  {
    try
    {
      // Set flag to trigger resize on next render frame instead of immediately
      // This ensures we're in the proper ImGui context when calculating dimensions
      _fontResizePending = true;
      // Console.WriteLine("TerminalController: Font-triggered terminal resize scheduled for next render frame");
    }
    catch (Exception ex)
    {
      Console.WriteLine($"TerminalController: Error scheduling font-triggered terminal resize: {ex.Message}");

#if DEBUG
      Console.WriteLine($"TerminalController: Font-triggered resize scheduling error stack trace: {ex.StackTrace}");
#endif
    }
  }

  /// <summary>
  ///     Performs the actual terminal resize calculation when font changes are pending.
  ///     Called during render frame when ImGui context is available.
  /// </summary>
  public void ProcessPendingFontResize()
  {
    if (!_fontResizePending)
      return;

    try
    {
      // Get current window size (we're now in ImGui render context)
      float2 currentWindowSize = ImGui.GetWindowSize();

      // Skip if window size is not initialized or invalid
      if (!_windowResizeHandler.IsWindowSizeInitialized || currentWindowSize.X <= 0 || currentWindowSize.Y <= 0)
      {
        Console.WriteLine("TerminalController: Cannot process pending font resize - window size not initialized or invalid");
        _fontResizePending = false; // Clear flag to avoid infinite retries
        return;
      }

      // Calculate new terminal dimensions with updated character metrics
      var newDimensions = _dimensionCalculator.CalculateTerminalDimensions(currentWindowSize);
      if (!newDimensions.HasValue)
      {
        Console.WriteLine("TerminalController: Cannot process pending font resize - invalid dimensions calculated");
        _fontResizePending = false; // Clear flag to avoid infinite retries
        return;
      }

      var (newCols, newRows) = newDimensions.Value;

      var activeSession = _sessionManager.ActiveSession;
      if (activeSession == null)
      {
        Console.WriteLine("TerminalController: Cannot process pending font resize - no active session");
        _fontResizePending = false;
        return;
      }

      // Check if terminal dimensions would actually change
      if (newCols == activeSession.Terminal.Width && newRows == activeSession.Terminal.Height)
      {
        Console.WriteLine($"TerminalController: Terminal dimensions unchanged ({newCols}x{newRows}), no resize needed");
        _fontResizePending = false;
        return;
      }

      // Validate new dimensions are reasonable
      if (newCols < 10 || newRows < 3 || newCols > 1000 || newRows > 1000)
      {
        Console.WriteLine($"TerminalController: Invalid terminal dimensions calculated: {newCols}x{newRows}, ignoring font-triggered resize");
        _fontResizePending = false;
        return;
      }

      // Log the resize operation
      Console.WriteLine($"TerminalController: Processing pending font resize from {activeSession.Terminal.Width}x{activeSession.Terminal.Height} to {newCols}x{newRows}");

      // Resize the headless terminal emulator
      activeSession.Terminal.Resize(newCols, newRows);

      // Persist dimensions for session metadata + future sessions
      activeSession.UpdateTerminalDimensions(newCols, newRows);
      _sessionManager.UpdateLastKnownTerminalDimensions(newCols, newRows);

      // Resize the PTY process if running
      if (activeSession.ProcessManager.IsRunning)
      {
        try
        {
          activeSession.ProcessManager.Resize(newCols, newRows);
          Console.WriteLine($"TerminalController: PTY process resized to {newCols}x{newRows}");
        }
        catch (Exception ex)
        {
          Console.WriteLine($"TerminalController: Failed to resize PTY process during font-triggered resize: {ex.Message}");
          // Continue anyway - terminal emulator resize succeeded
        }
      }

      Console.WriteLine($"TerminalController: Font-triggered terminal resize completed successfully");
      _fontResizePending = false; // Clear the flag

      // Notify that resize is complete so snap can be triggered
      _onResizeComplete?.Invoke(newCols, newRows);
    }
    catch (Exception ex)
    {
      Console.WriteLine($"TerminalController: Error during pending font-triggered terminal resize: {ex.Message}");
      _fontResizePending = false; // Clear flag to avoid infinite retries

#if DEBUG
      Console.WriteLine($"TerminalController: Pending font-triggered resize error stack trace: {ex.StackTrace}");
#endif
    }
  }

  /// <summary>
  ///     Triggers terminal resize for all sessions when font configuration changes.
  ///     This method ensures all sessions recalculate their dimensions with new character metrics.
  /// </summary>
  public void TriggerTerminalResizeForAllSessions()
  {
    try
    {
      // Use the last known window size instead of trying to get current window size
      // This avoids ImGui context issues when called from font configuration updates
      float2 currentWindowSize = _windowResizeHandler.LastWindowSize;

      // Skip if window size is not initialized or invalid
      if (!_windowResizeHandler.IsWindowSizeInitialized || currentWindowSize.X <= 0 || currentWindowSize.Y <= 0)
      {
        Console.WriteLine("TerminalController: Cannot trigger resize for all sessions - window size not initialized or invalid");
        // Set flag to trigger resize on next render frame instead
        _fontResizePending = true;
        return;
      }

      var sessions = _sessionManager.Sessions;
      Console.WriteLine($"TerminalController: Triggering font-based resize for {sessions.Count} sessions");

      foreach (var session in sessions)
      {
        try
        {
          // Calculate new terminal dimensions with updated character metrics
          var newDimensions = _dimensionCalculator.CalculateTerminalDimensions(currentWindowSize);
          if (!newDimensions.HasValue)
          {
            Console.WriteLine($"TerminalController: Cannot resize session {session.Id} - invalid dimensions calculated");
            continue;
          }

          var (newCols, newRows) = newDimensions.Value;

          // Check if terminal dimensions would actually change
          if (newCols == session.Terminal.Width && newRows == session.Terminal.Height)
          {
            Console.WriteLine($"TerminalController: Session {session.Id} dimensions unchanged ({newCols}x{newRows}), no resize needed");
            continue;
          }

          // Validate new dimensions are reasonable
          if (newCols < 10 || newRows < 3 || newCols > 1000 || newRows > 1000)
          {
            Console.WriteLine($"TerminalController: Invalid terminal dimensions calculated for session {session.Id}: {newCols}x{newRows}, skipping resize");
            continue;
          }

          // Log the resize operation
          Console.WriteLine($"TerminalController: Resizing session {session.Id} from {session.Terminal.Width}x{session.Terminal.Height} to {newCols}x{newRows}");

          // Resize the headless terminal emulator
          session.Terminal.Resize(newCols, newRows);

          // Update session settings with new dimensions
          session.UpdateTerminalDimensions(newCols, newRows);

          // Resize the PTY process if running
          if (session.ProcessManager.IsRunning)
          {
            try
            {
              session.ProcessManager.Resize(newCols, newRows);
              Console.WriteLine($"TerminalController: PTY process for session {session.Id} resized to {newCols}x{newRows}");
            }
            catch (Exception ex)
            {
              Console.WriteLine($"TerminalController: Failed to resize PTY process for session {session.Id}: {ex.Message}");
              // Continue anyway - terminal emulator resize succeeded
            }
          }
        }
        catch (Exception ex)
        {
          Console.WriteLine($"TerminalController: Error resizing session {session.Id}: {ex.Message}");
          // Continue with other sessions
        }
      }

      // Ensure newly created sessions start at the latest calculated dimensions.
      // All sessions share the same UI-space-derived size here, so updating once is sufficient.
      var active = _sessionManager.ActiveSession;
      if (active != null)
      {
        _sessionManager.UpdateLastKnownTerminalDimensions(active.Terminal.Width, active.Terminal.Height);
      }

      Console.WriteLine($"TerminalController: Font-triggered resize completed for all sessions");

      // Notify that resize is complete so snap can be triggered
      // Use active session dimensions if available
      if (active != null)
      {
        _onResizeComplete?.Invoke(active.Terminal.Width, active.Terminal.Height);
      }
    }
    catch (Exception ex)
    {
      Console.WriteLine($"TerminalController: Error during font-triggered resize for all sessions: {ex.Message}");

#if DEBUG
      Console.WriteLine($"TerminalController: Font-triggered resize for all sessions error stack trace: {ex.StackTrace}");
#endif
    }
  }
}
