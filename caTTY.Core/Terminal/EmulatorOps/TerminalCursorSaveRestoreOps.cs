using caTTY.Core.Managers;
using caTTY.Core.Types;
using Microsoft.Extensions.Logging;

namespace caTTY.Core.Terminal.EmulatorOps;

/// <summary>
///     Handles cursor save and restore operations for the terminal emulator.
///     Extracted from TerminalEmulator to reduce file size and improve maintainability.
/// </summary>
internal class TerminalCursorSaveRestoreOps
{
    private readonly ICursorManager _cursorManager;
    private readonly Func<TerminalState> _getState;
    private readonly Func<int> _getWidth;
    private readonly Func<int> _getHeight;
    private readonly ILogger _logger;

    /// <summary>
    ///     Creates a new cursor save/restore operations handler.
    /// </summary>
    /// <param name="cursorManager">The cursor manager for cursor operations</param>
    /// <param name="getState">Function to get the current terminal state</param>
    /// <param name="getWidth">Function to get the current terminal width</param>
    /// <param name="getHeight">Function to get the current terminal height</param>
    /// <param name="logger">Logger for debugging</param>
    public TerminalCursorSaveRestoreOps(ICursorManager cursorManager, Func<TerminalState> getState, Func<int> getWidth, Func<int> getHeight, ILogger logger)
    {
        _cursorManager = cursorManager;
        _getState = getState;
        _getWidth = getWidth;
        _getHeight = getHeight;
        _logger = logger;
    }

    /// <summary>
    ///     Saves the current cursor position for later restoration with ESC 8.
    ///     Implements ESC 7 (Save Cursor) sequence.
    /// </summary>
    public void SaveCursorPosition()
    {
        _cursorManager.SavePosition();

        // Also save in terminal state for compatibility
        _getState().SavedCursor = (_cursorManager.Column, _cursorManager.Row);
    }

    /// <summary>
    ///     Restores the previously saved cursor position.
    ///     Implements ESC 8 (Restore Cursor) sequence.
    /// </summary>
    public void RestoreCursorPosition()
    {
        var state = _getState();
        _cursorManager.RestorePosition();

        // Sync state with cursor manager
        state.CursorX = _cursorManager.Column;
        state.CursorY = _cursorManager.Row;
        state.WrapPending = _cursorManager.WrapPending;

        // Update saved cursor in state for compatibility
        if (state.SavedCursor.HasValue)
        {
            state.SavedCursor = (_cursorManager.Column, _cursorManager.Row);
        }
    }

    /// <summary>
    ///     Saves the current cursor position using ANSI style (CSI s).
    ///     This is separate from DEC cursor save/restore (ESC 7/8) to maintain compatibility.
    ///     Implements CSI s sequence.
    /// </summary>
    public void SaveCursorPositionAnsi()
    {
        // Save current cursor position in ANSI saved cursor field
        _getState().AnsiSavedCursor = (_cursorManager.Column, _cursorManager.Row);

        _logger.LogDebug("ANSI cursor saved at position ({X}, {Y})", _cursorManager.Column, _cursorManager.Row);
    }

    /// <summary>
    ///     Restores the previously saved ANSI cursor position.
    ///     This is separate from DEC cursor save/restore (ESC 7/8) to maintain compatibility.
    ///     Implements CSI u sequence.
    /// </summary>
    public void RestoreCursorPositionAnsi()
    {
        var state = _getState();
        if (state.AnsiSavedCursor.HasValue)
        {
            var (savedX, savedY) = state.AnsiSavedCursor.Value;

            // Validate and clamp the saved position to current buffer dimensions
            int clampedX = Math.Max(0, Math.Min(savedX, _getWidth() - 1));
            int clampedY = Math.Max(0, Math.Min(savedY, _getHeight() - 1));

            // Move cursor to the saved position
            _cursorManager.MoveTo(clampedY, clampedX);
            _cursorManager.SetWrapPending(false);

            // Sync state with cursor manager
            state.CursorX = _cursorManager.Column;
            state.CursorY = _cursorManager.Row;
            state.WrapPending = _cursorManager.WrapPending;

            _logger.LogDebug("ANSI cursor restored to position ({X}, {Y})", _cursorManager.Column, _cursorManager.Row);
        }
        else
        {
            // No saved position - this is a no-op (following xterm behavior)
            _logger.LogDebug("ANSI cursor restore called but no saved position available");
        }
    }
}
