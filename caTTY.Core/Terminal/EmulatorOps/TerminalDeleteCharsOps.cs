using caTTY.Core.Managers;
using caTTY.Core.Types;

namespace caTTY.Core.Terminal.EmulatorOps;

/// <summary>
///     Handles delete characters operations for the terminal emulator.
///     Extracted from TerminalEmulator to reduce file size and improve maintainability.
/// </summary>
internal class TerminalDeleteCharsOps
{
    private readonly ICursorManager _cursorManager;
    private readonly IScreenBufferManager _screenBufferManager;
    private readonly IAttributeManager _attributeManager;
    private readonly Func<TerminalState> _getState;

    /// <summary>
    ///     Creates a new delete characters operations handler.
    /// </summary>
    /// <param name="cursorManager">The cursor manager for cursor operations</param>
    /// <param name="screenBufferManager">The screen buffer manager for character operations</param>
    /// <param name="attributeManager">The attribute manager for current attributes</param>
    /// <param name="getState">Function to get the current terminal state</param>
    public TerminalDeleteCharsOps(
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
    ///     Deletes characters at the cursor position within the current line.
    ///     Implements CSI P (Delete Characters) sequence.
    ///     Characters to the right of the cursor are shifted left, and blank characters
    ///     are added at the end of the line.
    /// </summary>
    /// <param name="count">Number of characters to delete (minimum 1)</param>
    public void DeleteCharactersInLine(int count)
    {
        count = Math.Max(1, count);

        // Clear wrap pending state
        _cursorManager.SetWrapPending(false);

        // Use screen buffer manager for character deletion within current line
        _screenBufferManager.DeleteCharactersInLine(count, _cursorManager.Row, _cursorManager.Column, _attributeManager.CurrentAttributes, _attributeManager.CurrentCharacterProtection);

        // Sync state with managers
        _getState().CursorX = _cursorManager.Column;
        _getState().CursorY = _cursorManager.Row;
        _getState().WrapPending = _cursorManager.WrapPending;
    }
}
