using caTTY.Core.Managers;
using caTTY.Core.Types;

namespace caTTY.Core.Terminal.EmulatorOps;

/// <summary>
///     Handles scroll region operations for the terminal emulator.
///     Extracted from TerminalEmulator to reduce file size and improve maintainability.
/// </summary>
internal class TerminalScrollRegionOps
{
    private readonly ICursorManager _cursorManager;
    private readonly Func<TerminalState> _getState;
    private readonly Func<int> _getHeight;

    /// <summary>
    ///     Creates a new scroll region operations handler.
    /// </summary>
    /// <param name="cursorManager">The cursor manager for cursor operations</param>
    /// <param name="getState">Function to get the current terminal state</param>
    /// <param name="getHeight">Function to get the current terminal height</param>
    public TerminalScrollRegionOps(
        ICursorManager cursorManager,
        Func<TerminalState> getState,
        Func<int> getHeight)
    {
        _cursorManager = cursorManager;
        _getState = getState;
        _getHeight = getHeight;
    }

    /// <summary>
    ///     Sets the scroll region (DECSTBM - Set Top and Bottom Margins).
    ///     Implements CSI Ps ; Ps r sequence.
    /// </summary>
    /// <param name="top">Top boundary (1-indexed, null for default)</param>
    /// <param name="bottom">Bottom boundary (1-indexed, null for default)</param>
    public void SetScrollRegion(int? top, int? bottom)
    {
        // DECSTBM - Set Top and Bottom Margins
        if (top == null && bottom == null)
        {
            // Reset to full screen
            _getState().ScrollTop = 0;
            _getState().ScrollBottom = _getHeight() - 1;
        }
        else
        {
            // Convert from 1-indexed to 0-indexed and validate bounds
            int newTop = top.HasValue ? Math.Max(0, Math.Min(_getHeight() - 1, top.Value - 1)) : 0;
            int newBottom = bottom.HasValue ? Math.Max(0, Math.Min(_getHeight() - 1, bottom.Value - 1)) : _getHeight() - 1;

            // Ensure top < bottom
            if (newTop < newBottom)
            {
                _getState().ScrollTop = newTop;
                _getState().ScrollBottom = newBottom;
            }
        }

        // Move cursor to home position within scroll region (following TypeScript behavior)
        _cursorManager.MoveTo(_getState().ScrollTop, 0);
        _cursorManager.SetWrapPending(false);

        // Sync state with cursor manager
        _getState().CursorX = _cursorManager.Column;
        _getState().CursorY = _cursorManager.Row;
        _getState().WrapPending = _cursorManager.WrapPending;
    }
}
