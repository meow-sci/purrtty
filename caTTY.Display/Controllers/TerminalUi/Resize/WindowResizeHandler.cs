using System;
using Brutal.ImGuiApi;
using caTTY.Core.Terminal;
using float2 = Brutal.Numerics.float2;

namespace caTTY.Display.Controllers.TerminalUi.Resize;

/// <summary>
///     Handles window-level resize detection for the terminal UI.
///     Detects ImGui window size changes, applies debouncing, and triggers terminal dimension updates.
/// </summary>
internal class WindowResizeHandler
{
  private readonly SessionManager _sessionManager;
  private readonly Func<float2, (int cols, int rows)?> _calculateDimensions;
  private readonly Action<int, int> _applyResize;
  private readonly Action<int, int>? _onResizeComplete;

  // Window resize detection state
  private float2 _lastWindowSize = new(0, 0);
  private bool _windowSizeInitialized = false;
  private DateTime _lastResizeTime = DateTime.MinValue;
  private DateTime _lastSnapTime = DateTime.MinValue;
  private const float RESIZE_DEBOUNCE_SECONDS = 0.1f; // Debounce rapid resize events
  private const float SNAP_IGNORE_WINDOW_SECONDS = 0.5f; // Ignore window changes for this long after snap
  private bool _resizePending = false;
  private bool _wasMouseDragging = false;
  private bool _initialSnapDone = false;

  /// <summary>
  ///     Creates a new WindowResizeHandler.
  /// </summary>
  /// <param name="sessionManager">Session manager for accessing terminal dimensions</param>
  /// <param name="calculateDimensions">Function to calculate terminal dimensions from window size</param>
  /// <param name="applyResize">Action to apply resize to all sessions</param>
  /// <param name="onResizeComplete">Optional callback invoked when resize completes, with new cols/rows</param>
  public WindowResizeHandler(
    SessionManager sessionManager,
    Func<float2, (int cols, int rows)?> calculateDimensions,
    Action<int, int> applyResize,
    Action<int, int>? onResizeComplete = null)
  {
    _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
    _calculateDimensions = calculateDimensions ?? throw new ArgumentNullException(nameof(calculateDimensions));
    _applyResize = applyResize ?? throw new ArgumentNullException(nameof(applyResize));
    _onResizeComplete = onResizeComplete;
  }

  /// <summary>
  ///     Notifies the handler that a snap was just applied.
  ///     This prevents the handler from detecting the size change caused by the snap
  ///     and triggering another resize in a feedback loop.
  /// </summary>
  public void NotifySnapApplied()
  {
    _lastSnapTime = DateTime.Now;
  }

  /// <summary>
  ///     Handles window resize events by detecting size changes and triggering terminal dimension updates.
  ///     Called on every render frame to detect when the ImGui window size has changed.
  ///     Uses ImGui.IsMouseDragging to detect when resize drag operation completes.
  ///     Triggers snap immediately when mouse is released after a resize.
  ///     Matches the TypeScript implementation's approach of detecting display size changes
  ///     and updating both the headless terminal and the PTY process dimensions.
  /// </summary>
  public void HandleWindowResize()
  {
    try
    {
      // Get current window size (total window including title bar, borders, etc.)
      float2 currentWindowSize = ImGui.GetWindowSize();

      // Initialize window size tracking on first frame
      if (!_windowSizeInitialized)
      {
        _lastWindowSize = currentWindowSize;
        _windowSizeInitialized = true;
        return;
      }

      DateTime now = DateTime.Now;

      // Trigger initial snap on second frame (after window is initialized)
      // This ensures the window starts at the correct size for the terminal grid
      if (!_initialSnapDone)
      {
        _initialSnapDone = true;

        // Get current terminal dimensions
        var (cols, rows) = _sessionManager.LastKnownTerminalDimensions;

        // Trigger snap for initial window sizing
        _onResizeComplete?.Invoke(cols, rows);

        // Set snap time to prevent immediate resize detection
        _lastSnapTime = now;
        return;
      }

      // Ignore window changes for a period after snap to prevent feedback loop
      if ((now - _lastSnapTime).TotalSeconds < SNAP_IGNORE_WINDOW_SECONDS)
      {
        // Update tracking but don't trigger resize
        _lastWindowSize = currentWindowSize;
        return;
      }

      // Check if mouse is currently dragging (resize in progress)
      bool isMouseDragging = ImGui.IsMouseDragging(ImGuiMouseButton.Left);

      // Check if window size has changed significantly (avoid floating point precision issues)
      float deltaX = Math.Abs(currentWindowSize.X - _lastWindowSize.X);
      float deltaY = Math.Abs(currentWindowSize.Y - _lastWindowSize.Y);

      bool sizeChanged = deltaX > 1.0f || deltaY > 1.0f;

      if (sizeChanged)
      {
        // Size is actively changing - update tracking
        _lastWindowSize = currentWindowSize;

        // Debounce rapid resize events to avoid excessive processing
        if ((now - _lastResizeTime).TotalSeconds < RESIZE_DEBOUNCE_SECONDS)
        {
          _resizePending = true;  // Still mark as pending even if debounced
          return;
        }

        // Calculate new terminal dimensions based on available space
        var newDimensions = _calculateDimensions(currentWindowSize);
        if (!newDimensions.HasValue)
        {
          return; // Invalid dimensions
        }

        var (newCols, newRows) = newDimensions.Value;

        // Check if terminal dimensions would actually change
        var (lastCols, lastRows) = _sessionManager.LastKnownTerminalDimensions;
        if (newCols == lastCols && newRows == lastRows)
        {
          return; // No terminal dimension change needed
        }

        // Validate new dimensions are reasonable
        if (newCols < 10 || newRows < 3 || newCols > 1000 || newRows > 1000)
        {
          Console.WriteLine($"TerminalController: Invalid terminal dimensions calculated: {newCols}x{newRows}, ignoring resize");
          return;
        }

        // Apply resize immediately while dragging (this updates terminal and sends signals)
        _applyResize(newCols, newRows);
        _lastResizeTime = now;
        _resizePending = true;  // Mark that a resize happened
      }

      // Detect when mouse drag ends (transition from dragging to not dragging)
      bool mouseJustReleased = _wasMouseDragging && !isMouseDragging;
      _wasMouseDragging = isMouseDragging;

      // Trigger snap ONLY when mouse is released after a resize
      if (_resizePending && mouseJustReleased)
      {
        // Get current terminal dimensions to snap to
        var (cols, rows) = _sessionManager.LastKnownTerminalDimensions;

        _resizePending = false;

        // Notify that resize is complete so snap can be triggered
        _onResizeComplete?.Invoke(cols, rows);
      }
    }
    catch (Exception ex)
    {
      Console.WriteLine($"TerminalController: Error during window resize handling: {ex.Message}");

#if DEBUG
      Console.WriteLine($"TerminalController: Resize error stack trace: {ex.StackTrace}");
#endif
    }
  }

  /// <summary>
  ///     Gets the current window size for debugging purposes.
  /// </summary>
  /// <returns>Current window content area size</returns>
  public float2 GetCurrentWindowSize()
  {
    if (!_windowSizeInitialized)
    {
      return new float2(0, 0);
    }
    return _lastWindowSize;
  }

  /// <summary>
  ///     Gets whether the window size has been initialized.
  /// </summary>
  public bool IsWindowSizeInitialized => _windowSizeInitialized;

  /// <summary>
  ///     Gets the last window size for use in calculations.
  ///     Used by font resize operations that need the last known window size.
  /// </summary>
  public float2 LastWindowSize => _lastWindowSize;
}
