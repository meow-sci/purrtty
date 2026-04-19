using System;
using caTTY.Core.Terminal;

namespace caTTY.Display.Controllers.TerminalUi.Resize;

/// <summary>
///     Applies terminal resize operations to sessions.
///     Handles updating terminal emulator dimensions, PTY process dimensions, and session metadata.
/// </summary>
internal class SessionResizeApplicator
{
  private readonly SessionManager _sessionManager;

  public SessionResizeApplicator(SessionManager sessionManager)
  {
    _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
  }

  /// <summary>
  ///     Applies a terminal resize to all sessions.
  ///     This updates the headless terminal dimensions for every session and resizes any running PTY processes.
  /// </summary>
  /// <param name="cols">New terminal width in columns</param>
  /// <param name="rows">New terminal height in rows</param>
  public void ApplyTerminalDimensionsToAllSessions(int cols, int rows)
  {
    // NOTE: This method is intentionally ImGui-free so it can be unit-tested.
    // Dimension validation is performed by callers (window resize/font resize/manual paths).
    try
    {
      var sessions = _sessionManager.Sessions;
      foreach (var session in sessions)
      {
        try
        {
          session.Terminal.Resize(cols, rows);
          session.UpdateTerminalDimensions(cols, rows);

          if (session.ProcessManager.IsRunning)
          {
            try
            {
              session.ProcessManager.Resize(cols, rows);
            }
            catch (Exception ex)
            {
              Console.WriteLine($"TerminalController: Failed to resize PTY process for session {session.Id}: {ex.Message}");
            }
          }
        }
        catch (Exception ex)
        {
          Console.WriteLine($"TerminalController: Error resizing session {session.Id}: {ex.Message}");
        }
      }

      // Persist dimensions for future sessions
      _sessionManager.UpdateLastKnownTerminalDimensions(cols, rows);
    }
    catch (Exception ex)
    {
      Console.WriteLine($"TerminalController: Error applying resize to all sessions: {ex.Message}");
    }
  }

  /// <summary>
  ///     Manually triggers a terminal resize to the specified dimensions for the active session.
  ///     This method can be used for testing or external resize requests.
  /// </summary>
  /// <param name="cols">New width in columns</param>
  /// <param name="rows">New height in rows</param>
  /// <exception cref="ArgumentException">Thrown when dimensions are invalid</exception>
  /// <exception cref="InvalidOperationException">Thrown when no active session exists or resize fails</exception>
  public void ResizeActiveSession(int cols, int rows)
  {
    if (cols < 1 || rows < 1 || cols > 1000 || rows > 1000)
    {
      throw new ArgumentException($"Invalid terminal dimensions: {cols}x{rows}. Must be between 1x1 and 1000x1000.");
    }

    var activeSession = _sessionManager.ActiveSession;
    if (activeSession == null)
    {
      throw new InvalidOperationException("No active session to resize");
    }

    try
    {
      // Console.WriteLine($"TerminalController: Manual terminal resize requested: {cols}x{rows}");

      // Resize the headless terminal emulator
      activeSession.Terminal.Resize(cols, rows);

      // Persist dimensions for session metadata + future sessions
      activeSession.UpdateTerminalDimensions(cols, rows);
      _sessionManager.UpdateLastKnownTerminalDimensions(cols, rows);

      // Resize the PTY process if running
      if (activeSession.ProcessManager.IsRunning)
      {
        activeSession.ProcessManager.Resize(cols, rows);
        // Console.WriteLine($"TerminalController: PTY process resized to {cols}x{rows}");
      }

      // Console.WriteLine($"TerminalController: Manual terminal resize completed successfully");
    }
    catch (Exception ex)
    {
      Console.WriteLine($"TerminalController: Error during manual terminal resize: {ex.Message}");
      throw new InvalidOperationException($"Failed to resize terminal to {cols}x{rows}: {ex.Message}", ex);
    }
  }

  /// <summary>
  ///     Gets the current terminal dimensions for external access.
  ///     Useful for debugging and integration testing.
  /// </summary>
  /// <returns>Current terminal dimensions (width, height)</returns>
  public (int width, int height) GetTerminalDimensions()
  {
    var activeSession = _sessionManager.ActiveSession;
    return activeSession != null ? (activeSession.Terminal.Width, activeSession.Terminal.Height) : (0, 0);
  }
}
