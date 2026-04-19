using System;
using Brutal.ImGuiApi;
using caTTY.Core.Terminal;
using caTTY.Display.Controllers.TerminalUi.Resize;
using KSA;
using float2 = Brutal.Numerics.float2;

namespace caTTY.Display.Controllers.TerminalUi;

/// <summary>
///     Handles window resize detection, dimension calculations, and terminal resize operations for the terminal UI.
/// </summary>
internal class TerminalUiResize
{
  private readonly WindowResizeHandler _windowResizeHandler;
  private readonly TerminalDimensionCalculator _dimensionCalculator;
  private readonly FontResizeProcessor _fontResizeProcessor;
  private readonly SessionResizeApplicator _sessionResizeApplicator;
  private readonly WindowAutoSizeSnapper _autoSizeSnapper;

  public TerminalUiResize(SessionManager sessionManager, TerminalUiFonts fonts)
  {
    if (sessionManager == null) throw new ArgumentNullException(nameof(sessionManager));
    if (fonts == null) throw new ArgumentNullException(nameof(fonts));

    // Initialize components
    _dimensionCalculator = new TerminalDimensionCalculator(fonts, sessionManager);
    _sessionResizeApplicator = new SessionResizeApplicator(sessionManager);
    _autoSizeSnapper = new WindowAutoSizeSnapper();

    // Create callback for when resize completes - triggers window snap
    Action<int, int> onResizeComplete = (cols, rows) =>
    {
      // Calculate header height to match what CalculateTerminalDimensions uses:
      // Menu bar + Tab area (conditional on session count) + Window padding (top & bottom)
      // This MUST match the calculation in TerminalDimensionCalculator.CalculateTerminalDimensions
      int sessionCount = sessionManager.Sessions.Count;
      float menuBarHeight = LayoutConstants.MENU_BAR_HEIGHT;     // 25.0f
      float tabAreaHeight = CalculateTabAreaHeight(sessionCount); // 0.0f when 1 session, ~50.0f when 2+ sessions
      float windowPadding = LayoutConstants.WINDOW_PADDING * 2;  // 20.0f (top + bottom)

      float headerHeight = menuBarHeight + tabAreaHeight + windowPadding;

      TriggerSnapToSize(cols, rows, headerHeight);
    };

    // Initialize window resize handler with dependencies
    _windowResizeHandler = new WindowResizeHandler(
      sessionManager,
      _dimensionCalculator.CalculateTerminalDimensions,
      _sessionResizeApplicator.ApplyTerminalDimensionsToAllSessions,
      onResizeComplete
    );

    // Initialize font resize processor
    _fontResizeProcessor = new FontResizeProcessor(
      sessionManager,
      _dimensionCalculator,
      _windowResizeHandler,
      onResizeComplete
    );
  }

  /// <summary>
  ///     Gets whether a font-triggered resize is pending.
  /// </summary>
  public bool IsFontResizePending => _fontResizeProcessor.IsFontResizePending;

  /// <summary>
  ///     Gets whether a snap should occur this frame.
  /// </summary>
  public bool ShouldSnapThisFrame => _autoSizeSnapper.ShouldSnapThisFrame;

  /// <summary>
  ///     Gets the target window size for snapping.
  /// </summary>
  public float2 TargetWindowSize => _autoSizeSnapper.TargetWindowSize;

  /// <summary>
  ///     Clears the snap pending flag after it has been applied.
  ///     Also notifies the resize handler to ignore window changes caused by the snap.
  /// </summary>
  public void ClearSnap()
  {
    _autoSizeSnapper.ClearSnap();
    _windowResizeHandler.NotifySnapApplied();
  }

  /// <summary>
  ///     Triggers window size snapping with current terminal dimensions and font metrics.
  /// </summary>
  /// <param name="cols">Terminal width in columns</param>
  /// <param name="rows">Terminal height in rows</param>
  /// <param name="headerHeight">Total height of headers (menu + tabs + settings)</param>
  public void TriggerSnapToSize(int cols, int rows, float headerHeight)
  {
    // Get current font metrics
    float charWidth = _dimensionCalculator.CharacterWidth;
    float lineHeight = _dimensionCalculator.LineHeight;

    _autoSizeSnapper.TriggerSnap(cols, rows, charWidth, lineHeight, headerHeight);
  }

  /// <summary>
  ///     Handles window resize events by detecting size changes and triggering terminal dimension updates.
  ///     Called on every render frame to detect when the ImGui window size has changed.
  ///     Debounces rapid resize events and validates new dimensions before applying changes.
  ///     Matches the TypeScript implementation's approach of detecting display size changes
  ///     and updating both the headless terminal and the PTY process dimensions.
  /// </summary>
  public void HandleWindowResize()
  {
    _windowResizeHandler.HandleWindowResize();
  }

  /// <summary>
  ///     Applies a terminal resize to all sessions.
  ///     This updates the headless terminal dimensions for every session and resizes any running PTY processes.
  /// </summary>
  /// <param name="cols">New terminal width in columns</param>
  /// <param name="rows">New terminal height in rows</param>
  internal void ApplyTerminalDimensionsToAllSessions(int cols, int rows)
  {
    _sessionResizeApplicator.ApplyTerminalDimensionsToAllSessions(cols, rows);
  }


  /// <summary>
  ///     Gets the current terminal dimensions for external access.
  ///     Useful for debugging and integration testing.
  /// </summary>
  /// <returns>Current terminal dimensions (width, height)</returns>
  public (int width, int height) GetTerminalDimensions()
  {
    return _sessionResizeApplicator.GetTerminalDimensions();
  }

  /// <summary>
  ///     Gets the current window size for debugging purposes.
  /// </summary>
  /// <returns>Current window content area size</returns>
  public float2 GetCurrentWindowSize()
  {
    return _windowResizeHandler.GetCurrentWindowSize();
  }

  /// <summary>
  ///     Triggers terminal resize calculation based on current window size and updated character metrics.
  ///     This method is called when font configuration changes to ensure terminal dimensions
  ///     are recalculated with the new character metrics without requiring manual window resize.
  /// </summary>
  public void TriggerTerminalResize()
  {
    _fontResizeProcessor.TriggerTerminalResize();
  }

  /// <summary>
  ///     Performs the actual terminal resize calculation when font changes are pending.
  ///     Called during render frame when ImGui context is available.
  /// </summary>
  public void ProcessPendingFontResize()
  {
    _fontResizeProcessor.ProcessPendingFontResize();
  }

  /// <summary>
  ///     Triggers terminal resize for all sessions when font configuration changes.
  ///     This method ensures all sessions recalculate their dimensions with new character metrics.
  /// </summary>
  public void TriggerTerminalResizeForAllSessions()
  {
    _fontResizeProcessor.TriggerTerminalResizeForAllSessions();
  }

  /// <summary>
  ///     Manually triggers a terminal resize to the specified dimensions.
  ///     This method can be used for testing or external resize requests.
  /// </summary>
  /// <param name="cols">New width in columns</param>
  /// <param name="rows">New height in rows</param>
  /// <exception cref="ArgumentException">Thrown when dimensions are invalid</exception>
  public void ResizeTerminal(int cols, int rows)
  {
    _sessionResizeApplicator.ResizeActiveSession(cols, rows);
  }

  /// <summary>
  /// Calculates the current tab area height based on the number of terminal instances.
  /// Uses constrained sizing to prevent excessive height growth.
  /// </summary>
  /// <param name="tabCount">Number of terminal tabs (defaults to 1 for current single terminal)</param>
  /// <returns>Tab area height in pixels</returns>
  public static float CalculateTabAreaHeight(int tabCount = 1)
    => TerminalDimensionCalculator.CalculateTabAreaHeight(tabCount);

  /// <summary>
  /// Calculates the current settings area height based on the number of control rows.
  /// Uses constrained sizing to prevent excessive height growth.
  /// </summary>
  /// <param name="controlRows">Number of control rows (defaults to 1 for basic settings)</param>
  /// <returns>Settings area height in pixels</returns>
  public static float CalculateSettingsAreaHeight(int controlRows = 1)
    => TerminalDimensionCalculator.CalculateSettingsAreaHeight(controlRows);

  /// <summary>
  /// Calculates the total height of all header areas (menu bar, tab area, settings area).
  /// Uses current terminal state to determine variable area heights.
  /// </summary>
  /// <param name="tabCount">Number of terminal tabs (defaults to 1)</param>
  /// <param name="settingsControlRows">Number of settings control rows (defaults to 1)</param>
  /// <returns>Total height of header areas in pixels</returns>
  public static float CalculateHeaderHeight(int tabCount = 1, int settingsControlRows = 1)
    => TerminalDimensionCalculator.CalculateHeaderHeight(tabCount, settingsControlRows);

  /// <summary>
  /// Calculates the minimum possible header height (all areas at minimum size).
  /// Used for minimum window size calculations and initial estimates.
  /// </summary>
  /// <returns>Minimum header height in pixels</returns>
  public static float CalculateMinHeaderHeight()
    => TerminalDimensionCalculator.CalculateMinHeaderHeight();

  /// <summary>
  /// Calculates the maximum possible header height (all areas at maximum size).
  /// Used for layout validation and bounds checking.
  /// </summary>
  /// <returns>Maximum header height in pixels</returns>
  public static float CalculateMaxHeaderHeight()
    => TerminalDimensionCalculator.CalculateMaxHeaderHeight();

  /// <summary>
  /// Calculates the available space for the terminal canvas after accounting for header areas.
  /// Uses current header configuration for accurate space calculation.
  /// </summary>
  /// <param name="windowSize">Total window size</param>
  /// <param name="tabCount">Number of terminal tabs (defaults to 1)</param>
  /// <param name="settingsControlRows">Number of settings control rows (defaults to 1)</param>
  /// <returns>Available size for terminal canvas</returns>
  public static float2 CalculateTerminalCanvasSize(float2 windowSize, int tabCount = 1, int settingsControlRows = 1)
    => TerminalDimensionCalculator.CalculateTerminalCanvasSize(windowSize, tabCount, settingsControlRows);

  /// <summary>
  /// Validates that window dimensions are sufficient for the layout with current configuration.
  /// Accounts for variable header heights in validation.
  /// </summary>
  /// <param name="windowSize">Window size to validate</param>
  /// <param name="tabCount">Number of terminal tabs (defaults to 1)</param>
  /// <param name="settingsControlRows">Number of settings control rows (defaults to 1)</param>
  /// <returns>True if window size is valid for layout</returns>
  public static bool ValidateWindowSize(float2 windowSize, int tabCount = 1, int settingsControlRows = 1)
    => TerminalDimensionCalculator.ValidateWindowSize(windowSize, tabCount, settingsControlRows);

  /// <summary>
  /// Calculates the position for the terminal canvas area.
  /// Accounts for current header height configuration.
  /// </summary>
  /// <param name="windowPos">Window position</param>
  /// <param name="tabCount">Number of terminal tabs (defaults to 1)</param>
  /// <param name="settingsControlRows">Number of settings control rows (defaults to 1)</param>
  /// <returns>Position where terminal canvas should be rendered</returns>
  public static float2 CalculateTerminalCanvasPosition(float2 windowPos, int tabCount = 1, int settingsControlRows = 1)
    => TerminalDimensionCalculator.CalculateTerminalCanvasPosition(windowPos, tabCount, settingsControlRows);

  /// <summary>
  /// Calculates optimal terminal dimensions using two-pass approach for stability.
  /// Prevents sizing oscillation by using conservative estimates.
  /// </summary>
  /// <param name="windowSize">Current window size</param>
  /// <param name="charWidth">Character width in pixels</param>
  /// <param name="lineHeight">Line height in pixels</param>
  /// <param name="tabCount">Number of terminal tabs (defaults to 1)</param>
  /// <param name="settingsControlRows">Number of settings control rows (defaults to 1)</param>
  /// <returns>Terminal dimensions (cols, rows) or null if invalid</returns>
  public static (int cols, int rows)? CalculateOptimalTerminalDimensions(
    float2 windowSize,
    float charWidth,
    float lineHeight,
    int tabCount = 1,
    int settingsControlRows = 1)
    => TerminalDimensionCalculator.CalculateOptimalTerminalDimensions(windowSize, charWidth, lineHeight, tabCount, settingsControlRows);
}
