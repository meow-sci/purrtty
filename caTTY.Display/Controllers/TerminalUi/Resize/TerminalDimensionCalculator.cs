using System;
using caTTY.Core.Terminal;
using caTTY.Display.Controllers.TerminalUi;
using float2 = Brutal.Numerics.float2;

namespace caTTY.Display.Controllers.TerminalUi.Resize;

/// <summary>
///     Calculates terminal dimensions (columns/rows) from pixel-based window sizes using font metrics.
///     Handles UI layout overhead calculations and provides dimension validation.
/// </summary>
internal class TerminalDimensionCalculator
{
  private readonly TerminalUiFonts _fonts;
  private readonly SessionManager _sessionManager;

  /// <summary>
  ///     Creates a new TerminalDimensionCalculator.
  /// </summary>
  /// <param name="fonts">Font manager for accessing character metrics</param>
  /// <param name="sessionManager">Session manager for accessing session count</param>
  public TerminalDimensionCalculator(TerminalUiFonts fonts, SessionManager sessionManager)
  {
    _fonts = fonts ?? throw new ArgumentNullException(nameof(fonts));
    _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
  }

  /// <summary>
  ///     Gets the current character width from font metrics.
  /// </summary>
  public float CharacterWidth => _fonts.CurrentCharacterWidth;

  /// <summary>
  ///     Gets the current line height from font metrics.
  /// </summary>
  public float LineHeight => _fonts.CurrentLineHeight;

  /// <summary>
  ///     Calculates optimal terminal dimensions based on available window space.
  ///     Uses character metrics to determine how many columns and rows can fit.
  ///     Accounts for the complete UI layout structure: menu bar, tab area (when 2+ sessions), terminal info, and padding.
  ///     Matches the approach used in the TypeScript implementation and playground experiments.
  /// </summary>
  /// <param name="availableSize">The available window content area size</param>
  /// <returns>Terminal dimensions (cols, rows) or null if invalid</returns>
  public (int cols, int rows)? CalculateTerminalDimensions(float2 availableSize)
  {
    try
    {
      // Calculate UI overhead for multi-session UI layout
      // Multi-session UI includes menu bar and conditionally tab area
      int sessionCount = _sessionManager.Sessions.Count;
      float menuBarHeight = LayoutConstants.MENU_BAR_HEIGHT;     // 25.0f
      float tabAreaHeight = CalculateTabAreaHeight(sessionCount); // 0.0f when 1 session, ~50.0f when 2+ sessions
      float windowPadding = LayoutConstants.WINDOW_PADDING * 2;  // Top and bottom padding

      float totalUIOverheadHeight = menuBarHeight + tabAreaHeight + windowPadding;

      // Debug logging for multi-session UI overhead calculation
      // Console.WriteLine($"TerminalController: Multi-session UI Overhead - Menu: {menuBarHeight}, Tab: {tabAreaHeight}, Padding: {windowPadding}, Total: {totalUIOverheadHeight}");

      float horizontalPadding = LayoutConstants.WINDOW_PADDING * 2; // Left and right padding

      float availableWidth = availableSize.X - horizontalPadding;
      float availableHeight = availableSize.Y - totalUIOverheadHeight;

      // Ensure we have positive dimensions
      if (availableWidth <= 0 || availableHeight <= 0)
      {
        return null;
      }

      // Calculate dimensions using current character metrics
      if (_fonts.CurrentCharacterWidth <= 0 || _fonts.CurrentLineHeight <= 0)
      {
        Console.WriteLine($"TerminalController: Invalid character metrics: width={_fonts.CurrentCharacterWidth}, height={_fonts.CurrentLineHeight}");
        return null;
      }

      int cols = (int)Math.Floor(availableWidth / _fonts.CurrentCharacterWidth);
      int rows = (int)Math.Floor(availableHeight / _fonts.CurrentLineHeight);

      // Apply reasonable bounds (matching TypeScript validation)
      cols = Math.Max(10, Math.Min(1000, cols));
      rows = Math.Max(3, Math.Min(1000, rows));

      // Reduce rows by 1 to account for ImGui widget spacing that causes bottom clipping
      // This prevents the bottom row from being cut off due to ImGui layout overhead
      rows = Math.Max(3, rows - 1);

      return (cols, rows);
    }
    catch (Exception ex)
    {
      Console.WriteLine($"TerminalController: Error calculating terminal dimensions: {ex.Message}");
      return null;
    }
  }

  /// <summary>
  /// Calculates the current tab area height based on the number of terminal instances.
  /// Returns 0 when there is only one session (tab bar is hidden).
  /// Returns the fixed TAB_AREA_HEIGHT constant when there are 2+ sessions.
  /// </summary>
  /// <param name="tabCount">Number of terminal tabs (defaults to 1 for current single terminal)</param>
  /// <returns>Tab area height in pixels (0 when tabCount <= 1, 50.0f when tabCount >= 2)</returns>
  public static float CalculateTabAreaHeight(int tabCount = 1)
  {
    // Don't include tab area height when there's only one session
    if (tabCount <= 1)
    {
      return 0.0f;
    }

    // When there are 2+ sessions, use the fixed constant to match original behavior
    return LayoutConstants.TAB_AREA_HEIGHT; // 50.0f
  }

  /// <summary>
  /// Calculates the current settings area height based on the number of control rows.
  /// Uses constrained sizing to prevent excessive height growth.
  /// </summary>
  /// <param name="controlRows">Number of control rows (defaults to 1 for basic settings)</param>
  /// <returns>Settings area height in pixels</returns>
  public static float CalculateSettingsAreaHeight(int controlRows = 1)
  {
    float baseHeight = LayoutConstants.MIN_SETTINGS_AREA_HEIGHT;
    float extraHeight = Math.Max(0, (controlRows - 1) * LayoutConstants.SETTINGS_HEIGHT_PER_CONTROL_ROW);
    return Math.Min(LayoutConstants.MAX_SETTINGS_AREA_HEIGHT, baseHeight + extraHeight);
  }

  /// <summary>
  /// Calculates the total height of all header areas (menu bar, tab area, settings area).
  /// Uses current terminal state to determine variable area heights.
  /// </summary>
  /// <param name="tabCount">Number of terminal tabs (defaults to 1)</param>
  /// <param name="settingsControlRows">Number of settings control rows (defaults to 1)</param>
  /// <returns>Total height of header areas in pixels</returns>
  public static float CalculateHeaderHeight(int tabCount = 1, int settingsControlRows = 1)
  {
    return LayoutConstants.MENU_BAR_HEIGHT +
           CalculateTabAreaHeight(tabCount) +
           CalculateSettingsAreaHeight(settingsControlRows);
  }

  /// <summary>
  /// Calculates the minimum possible header height (all areas at minimum size).
  /// Used for minimum window size calculations and initial estimates.
  /// </summary>
  /// <returns>Minimum header height in pixels</returns>
  public static float CalculateMinHeaderHeight()
  {
    return LayoutConstants.MENU_BAR_HEIGHT +
           LayoutConstants.MIN_TAB_AREA_HEIGHT +
           LayoutConstants.MIN_SETTINGS_AREA_HEIGHT;
  }

  /// <summary>
  /// Calculates the maximum possible header height (all areas at maximum size).
  /// Used for layout validation and bounds checking.
  /// </summary>
  /// <returns>Maximum header height in pixels</returns>
  public static float CalculateMaxHeaderHeight()
  {
    return LayoutConstants.MENU_BAR_HEIGHT +
           LayoutConstants.MAX_TAB_AREA_HEIGHT +
           LayoutConstants.MAX_SETTINGS_AREA_HEIGHT;
  }

  /// <summary>
  /// Calculates the available space for the terminal canvas after accounting for header areas.
  /// Uses current header configuration for accurate space calculation.
  /// </summary>
  /// <param name="windowSize">Total window size</param>
  /// <param name="tabCount">Number of terminal tabs (defaults to 1)</param>
  /// <param name="settingsControlRows">Number of settings control rows (defaults to 1)</param>
  /// <returns>Available size for terminal canvas</returns>
  public static float2 CalculateTerminalCanvasSize(float2 windowSize, int tabCount = 1, int settingsControlRows = 1)
  {
    float headerHeight = CalculateHeaderHeight(tabCount, settingsControlRows);
    float availableWidth = Math.Max(0, windowSize.X - LayoutConstants.WINDOW_PADDING * 2);
    float availableHeight = Math.Max(0, windowSize.Y - headerHeight - LayoutConstants.WINDOW_PADDING * 2);

    return new float2(availableWidth, availableHeight);
  }

  /// <summary>
  /// Validates that window dimensions are sufficient for the layout with current configuration.
  /// Accounts for variable header heights in validation.
  /// </summary>
  /// <param name="windowSize">Window size to validate</param>
  /// <param name="tabCount">Number of terminal tabs (defaults to 1)</param>
  /// <param name="settingsControlRows">Number of settings control rows (defaults to 1)</param>
  /// <returns>True if window size is valid for layout</returns>
  public static bool ValidateWindowSize(float2 windowSize, int tabCount = 1, int settingsControlRows = 1)
  {
    // Check basic minimum dimensions
    if (windowSize.X < LayoutConstants.MIN_WINDOW_WIDTH ||
        windowSize.Y < LayoutConstants.MIN_WINDOW_HEIGHT)
    {
      return false;
    }

    // Check that window can accommodate current header configuration
    float currentHeaderHeight = CalculateHeaderHeight(tabCount, settingsControlRows);
    float minRequiredHeight = currentHeaderHeight + LayoutConstants.WINDOW_PADDING * 2 + 50.0f; // 50px minimum for terminal content

    return windowSize.Y >= minRequiredHeight;
  }

  /// <summary>
  /// Calculates the position for the terminal canvas area.
  /// Accounts for current header height configuration.
  /// </summary>
  /// <param name="windowPos">Window position</param>
  /// <param name="tabCount">Number of terminal tabs (defaults to 1)</param>
  /// <param name="settingsControlRows">Number of settings control rows (defaults to 1)</param>
  /// <returns>Position where terminal canvas should be rendered</returns>
  public static float2 CalculateTerminalCanvasPosition(float2 windowPos, int tabCount = 1, int settingsControlRows = 1)
  {
    float headerHeight = CalculateHeaderHeight(tabCount, settingsControlRows);
    return new float2(
      windowPos.X + LayoutConstants.WINDOW_PADDING,
      windowPos.Y + headerHeight + LayoutConstants.WINDOW_PADDING
    );
  }

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
  {
    try
    {
      // Validate inputs
      if (charWidth <= 0 || lineHeight <= 0 || windowSize.X <= 0 || windowSize.Y <= 0)
      {
        return null;
      }

      // Pass 1: Estimate with minimum header height for conservative sizing
      float minHeaderHeight = CalculateMinHeaderHeight();
      float estimatedAvailableWidth = windowSize.X - LayoutConstants.WINDOW_PADDING * 2;
      float estimatedAvailableHeight = windowSize.Y - minHeaderHeight - LayoutConstants.WINDOW_PADDING * 2;

      if (estimatedAvailableWidth <= 0 || estimatedAvailableHeight <= 0)
      {
        return null;
      }

      int estimatedCols = (int)Math.Floor(estimatedAvailableWidth / charWidth);
      int estimatedRows = (int)Math.Floor(estimatedAvailableHeight / lineHeight);

      // Pass 2: Calculate with actual header height
      float actualHeaderHeight = CalculateHeaderHeight(tabCount, settingsControlRows);
      float actualAvailableWidth = windowSize.X - LayoutConstants.WINDOW_PADDING * 2;
      float actualAvailableHeight = windowSize.Y - actualHeaderHeight - LayoutConstants.WINDOW_PADDING * 2;

      if (actualAvailableWidth <= 0 || actualAvailableHeight <= 0)
      {
        return null;
      }

      int actualCols = (int)Math.Floor(actualAvailableWidth / charWidth);
      int actualRows = (int)Math.Floor(actualAvailableHeight / lineHeight);

      // Use the more conservative (smaller) result to prevent oscillation
      int finalCols = Math.Min(estimatedCols, actualCols);
      int finalRows = Math.Min(estimatedRows, actualRows);

      // Apply reasonable bounds
      finalCols = Math.Max(10, Math.Min(1000, finalCols));
      finalRows = Math.Max(3, Math.Min(1000, finalRows));

      return (finalCols, finalRows);
    }
    catch (Exception ex)
    {
      Console.WriteLine($"TerminalController: Error calculating optimal terminal dimensions: {ex.Message}");
      return null;
    }
  }
}
