using System;
using System.Linq;
using caTTY.Core.Input;
using caTTY.Core.Managers;
using caTTY.Core.Terminal;
using caTTY.Core.Types;
using caTTY.Display.Input;
using caTTY.Display.Rendering;

namespace caTTY.Display.Controllers.TerminalUi;

/// <summary>
///     Handles event management for terminal UI.
///     Processes terminal events, session events, mouse events, focus events, and theme events.
/// </summary>
public class TerminalUiEvents
{
  private readonly SessionManager _sessionManager;
  private readonly CursorRenderer _cursorRenderer;
  private readonly TerminalUiSelection _selection;
  private readonly TerminalUiMouseTracking _mouseTracking;
  private readonly Action _resetCursorToThemeDefaults;

  /// <summary>
  ///     Creates a new terminal UI events handler.
  /// </summary>
  /// <param name="sessionManager">The session manager instance</param>
  /// <param name="cursorRenderer">The cursor renderer instance</param>
  /// <param name="selection">The selection subsystem instance</param>
  /// <param name="mouseTracking">The mouse tracking subsystem instance</param>
  /// <param name="resetCursorToThemeDefaults">Callback to reset cursor to theme defaults</param>
  public TerminalUiEvents(
    SessionManager sessionManager,
    CursorRenderer cursorRenderer,
    TerminalUiSelection selection,
    TerminalUiMouseTracking mouseTracking,
    Action resetCursorToThemeDefaults)
  {
    _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
    _cursorRenderer = cursorRenderer ?? throw new ArgumentNullException(nameof(cursorRenderer));
    _selection = selection ?? throw new ArgumentNullException(nameof(selection));
    _mouseTracking = mouseTracking ?? throw new ArgumentNullException(nameof(mouseTracking));
    _resetCursorToThemeDefaults = resetCursorToThemeDefaults ?? throw new ArgumentNullException(nameof(resetCursorToThemeDefaults));
  }

  /// <summary>
  ///     Handles focus gained event.
  ///     Called when the terminal window gains focus.
  /// </summary>
  public void OnFocusGained()
  {
    try
    {
      // Make cursor immediately visible when gaining focus
      _cursorRenderer.ForceVisible();

      // Clear any existing selection when gaining focus (matches TypeScript behavior)
      // This prevents stale selections from interfering with new input
      if (!_selection.GetCurrentSelection().IsEmpty)
      {
        Console.WriteLine("TerminalController: Clearing selection on focus gained");
        _selection.ClearSelection();
      }

      // Reset cursor blink state to ensure it's visible
      _cursorRenderer.ResetBlinkState();
    }
    catch (Exception ex)
    {
      Console.WriteLine($"TerminalController: Error handling focus gained: {ex.Message}");
    }
  }

  /// <summary>
  ///     Handles focus lost event.
  ///     Called when the terminal window loses focus.
  /// </summary>
  public void OnFocusLost()
  {
    try
    {
      // Stop any ongoing selection when losing focus
      if (_selection.IsSelecting)
      {
        Console.WriteLine("TerminalController: Stopping selection on focus lost");
        _selection.IsSelecting = false;
      }

      // Reset mouse wheel accumulator to prevent stuck scrolling
      // Note: Wheel accumulator is managed by TerminalController

      Console.WriteLine("TerminalController: Terminal lost focus - input capture inactive");
    }
    catch (Exception ex)
    {
      Console.WriteLine($"TerminalController: Error handling focus lost: {ex.Message}");
    }
  }

  /// <summary>
  ///     Handles screen updated events from the terminal.
  /// </summary>
  public void OnScreenUpdated(object? sender, ScreenUpdatedEventArgs e)
  {
    // Screen will be redrawn on next frame
  }

  /// <summary>
  ///     Handles response emitted events from the terminal.
  /// </summary>
  public void OnResponseEmitted(object? sender, ResponseEmittedEventArgs e)
  {
    // Find the session that emitted this response
    var emittingSession = _sessionManager.Sessions.FirstOrDefault(s => s.Terminal == sender);
    if (emittingSession?.ProcessManager.IsRunning == true)
    {
      try
      {
        emittingSession.ProcessManager.Write(e.ResponseData.Span);
      }
      catch (Exception ex)
      {
        Console.WriteLine($"Failed to send terminal response to process: {ex.Message}");
      }
    }
  }

  /// <summary>
  ///     Handles theme change events from the ThemeManager.
  ///     Updates cursor style and other theme-dependent settings when theme changes.
  /// </summary>
  public void OnThemeChanged(object? sender, ThemeChangedEventArgs e)
  {
    try
    {
      Console.WriteLine($"TerminalController: Theme changed from '{e.PreviousTheme.Name}' to '{e.NewTheme.Name}'");

      // Reset cursor style to match new theme defaults
      _resetCursorToThemeDefaults();

      // Force cursor to be visible immediately after theme change
      _cursorRenderer.ForceVisible();

      // Reset cursor blink state to ensure proper timing with new theme
      _cursorRenderer.ResetBlinkState();

      Console.WriteLine($"TerminalController: Theme change handling completed for '{e.NewTheme.Name}'");
    }
    catch (Exception ex)
    {
      Console.WriteLine($"TerminalController: Error handling theme change: {ex.Message}");
    }
  }

  /// <summary>
  ///     Handles mouse events that should be sent to the application as escape sequences.
  /// </summary>
  public void OnMouseEventGenerated(object? sender, MouseEventArgs e)
  {
    _mouseTracking.OnMouseEventGenerated(sender, e);
  }

  /// <summary>
  ///     Handles mouse events that should be processed locally (selection, scrolling).
  /// </summary>
  public void OnLocalMouseEvent(object? sender, MouseEventArgs e)
  {
    _mouseTracking.OnLocalMouseEvent(sender, e);
  }

  /// <summary>
  ///     Handles mouse processing errors.
  /// </summary>
  public void OnMouseProcessingError(object? sender, MouseProcessingErrorEventArgs e)
  {
    _mouseTracking.OnMouseProcessingError(sender, e);
  }

  /// <summary>
  ///     Handles mouse input errors.
  /// </summary>
  public void OnMouseInputError(object? sender, MouseInputErrorEventArgs e)
  {
    _mouseTracking.OnMouseInputError(sender, e);
  }

  /// <summary>
  ///     Handles session creation events from the SessionManager.
  /// </summary>
  public void OnSessionCreated(object? sender, SessionCreatedEventArgs e)
  {
    // Wire up events for the new session
    var session = e.Session;
    session.Terminal.ScreenUpdated += OnScreenUpdated;
    session.Terminal.ResponseEmitted += OnResponseEmitted;
    session.TitleChanged += OnSessionTitleChanged;

    Console.WriteLine($"TerminalController: Session created - {session.Title} ({session.Id})");
  }

  /// <summary>
  ///     Handles session closure events from the SessionManager.
  /// </summary>
  public void OnSessionClosed(object? sender, SessionClosedEventArgs e)
  {
    // Unwire events for the closed session
    var session = e.Session;
    session.Terminal.ScreenUpdated -= OnScreenUpdated;
    session.Terminal.ResponseEmitted -= OnResponseEmitted;
    session.TitleChanged -= OnSessionTitleChanged;

    Console.WriteLine($"TerminalController: Session closed - {session.Title} ({session.Id})");
  }

  /// <summary>
  ///     Handles active session change events from the SessionManager.
  /// </summary>
  public void OnActiveSessionChanged(object? sender, ActiveSessionChangedEventArgs e)
  {
    Console.WriteLine($"TerminalController: Active session changed from {e.PreviousSession?.Title} to {e.NewSession?.Title}");

    // Clear any existing selection when switching sessions
    if (!_selection.GetCurrentSelection().IsEmpty)
    {
      _selection.ClearSelection();
    }

    // Reset cursor blink state for new active session
    _cursorRenderer.ResetBlinkState();
  }

  /// <summary>
  ///     Handles session title change events from individual sessions.
  ///     This ensures the UI updates when applications like htop change the terminal title.
  /// </summary>
  public void OnSessionTitleChanged(object? sender, SessionTitleChangedEventArgs e)
  {
    // Note: No explicit UI refresh needed here since ImGui re-renders every frame
    // The tab labels will automatically show the updated session titles on next render
    Console.WriteLine($"TerminalController: Session title changed from '{e.OldTitle}' to '{e.NewTitle}'");
  }
}
