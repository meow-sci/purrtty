using caTTY.Core.Managers;
using caTTY.Core.Types;

namespace caTTY.Core.Terminal.EmulatorOps;

/// <summary>
///     Handles insert lines operations for the terminal emulator.
///     Extracted from TerminalEmulator to reduce file size and improve maintainability.
/// </summary>
internal class TerminalInsertLinesOps
{
    private readonly ICursorManager _cursorManager;
    private readonly IScreenBufferManager _screenBufferManager;
    private readonly IAttributeManager _attributeManager;
    private readonly Func<TerminalState> _getState;

    /// <summary>
    ///     Creates a new insert lines operations handler.
    /// </summary>
    /// <param name="cursorManager">The cursor manager for cursor operations</param>
    /// <param name="screenBufferManager">The screen buffer manager for line operations</param>
    /// <param name="attributeManager">The attribute manager for current attributes</param>
    /// <param name="getState">Function to get the current terminal state</param>
    public TerminalInsertLinesOps(
        ICursorManager cursorManager,
        IScreenBufferManager screenBufferManager,
        IAttributeManager attributeManager,
        Func<TerminalState> getState)
    {
        _cursorManager = cursorManager;
        _screenBufferManager = screenBufferManager;
        _attributeManager = attributeManager;
        _getState = getState;
    }

    /// <summary>
    ///     Inserts blank lines at the cursor position within the scroll region.
    ///     Implements CSI L (Insert Lines) sequence.
    ///     Lines below the cursor are shifted down, and lines that would go beyond
    ///     the scroll region bottom are lost.
    /// </summary>
    /// <param name="count">Number of lines to insert (minimum 1)</param>
    public void InsertLinesInRegion(int count)
    {
        count = Math.Max(1, count);

        // Clear wrap pending state
        _cursorManager.SetWrapPending(false);

        // Use screen buffer manager for line insertion within scroll region
        _screenBufferManager.InsertLinesInRegion(count, _cursorManager.Row, _getState().ScrollTop, _getState().ScrollBottom, _attributeManager.CurrentAttributes, _attributeManager.CurrentCharacterProtection);

        // Sync state with managers
        _getState().CursorX = _cursorManager.Column;
        _getState().CursorY = _cursorManager.Row;
        _getState().WrapPending = _cursorManager.WrapPending;
    }
}
