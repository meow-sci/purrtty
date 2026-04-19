using System;
using Brutal.Numerics;

namespace caTTY.Display.Controllers.TerminalUi.Resize;

/// <summary>
///     Manages automatic window size snapping after resize operations.
///     Calculates the exact window size needed to fit the terminal content
///     based on font metrics and terminal dimensions, then snaps to that size.
/// </summary>
internal class WindowAutoSizeSnapper
{
  private bool _snapPending = false;
  private float2 _targetWindowSize;
  private DateTime _lastSnapTriggerTime = DateTime.MinValue;
  private const float MIN_SNAP_INTERVAL_SECONDS = 0.5f; // Minimum time between snaps (500ms)

  /// <summary>
  ///     Gets whether a snap should occur this frame.
  /// </summary>
  public bool ShouldSnapThisFrame => _snapPending;

  /// <summary>
  ///     Gets the target window size to snap to.
  /// </summary>
  public float2 TargetWindowSize => _targetWindowSize;

  /// <summary>
  ///     Triggers window size snapping for the next render frame.
  ///     Calculates the exact window size needed to fit the terminal content.
  /// </summary>
  /// <param name="cols">Terminal width in columns</param>
  /// <param name="rows">Terminal height in rows</param>
  /// <param name="charWidth">Character width in pixels</param>
  /// <param name="lineHeight">Line height in pixels</param>
  /// <param name="headerHeight">Total height of menu bar, tabs, and settings in pixels</param>
  public void TriggerSnap(int cols, int rows, float charWidth, float lineHeight, float headerHeight)
  {
    DateTime now = DateTime.Now;

    // Prevent rapid successive snaps - only allow one snap every MIN_SNAP_INTERVAL_SECONDS
    if ((now - _lastSnapTriggerTime).TotalSeconds < MIN_SNAP_INTERVAL_SECONDS)
    {
      return;
    }

    // Calculate exact window size needed:
    // Width: cols * charWidth (no padding, ImGui WindowPadding is 0)
    // Height: rows * lineHeight + headerHeight
    //   With NoTitleBar flag, window size directly matches our layout
    //   Subtract 1px from height to account for window border (WindowBorderSize = 1.0f)
    //   Use exact floating point - don't round!

    float terminalWidth = cols * charWidth;
    float terminalHeight = (rows * lineHeight) + headerHeight - 1.0f; // we subtract 1px because of unknown reasons .. always have a 1px gap for some reason (window border?)

    _targetWindowSize = new float2(
      terminalWidth,
      terminalHeight
    );

    _snapPending = true;
    _lastSnapTriggerTime = now;
  }

  /// <summary>
  ///     Clears the snap pending flag after it has been applied.
  ///     Call this after using SetNextWindowSize.
  /// </summary>
  public void ClearSnap()
  {
    if (_snapPending)
    {
      _snapPending = false;
    }
  }
}
