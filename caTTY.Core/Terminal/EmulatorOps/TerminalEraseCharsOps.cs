using caTTY.Core.Managers;
using caTTY.Core.Types;

namespace caTTY.Core.Terminal.EmulatorOps;

/// <summary>
///     Handles erase characters operations for the terminal emulator.
///     Extracted from TerminalEmulator to reduce file size and improve maintainability.
/// </summary>
internal class TerminalEraseCharsOps
{
    private readonly ICursorManager _cursorManager;
    private readonly IScreenBufferManager _screenBufferManager;
    private readonly IAttributeManager _attributeManager;
    private readonly Func<TerminalState> _getState;

    /// <summary>
    ///     Creates a new erase characters operations handler.
    /// </summary>
    /// <param name="cursorManager">The cursor manager for cursor operations</param>
    /// <param name="screenBufferManager">The screen buffer manager for character operations</param>
    /// <param name="attributeManager">The attribute manager for current attributes</param>
    /// <param name="getState">Function to get the current terminal state</param>
    public TerminalEraseCharsOps(
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
    ///     Erases characters at the cursor position within the current line.
    ///     Implements CSI X (Erase Character) sequence.
    ///     Erases characters by replacing them with blank characters using current SGR attributes.
    ///     Does not move the cursor or shift other characters.
    /// </summary>
    /// <param name="count">Number of characters to erase (minimum 1)</param>
    public void EraseCharactersInLine(int count)
    {
        count = Math.Max(1, count);

        // Clear wrap pending state
        _cursorManager.SetWrapPending(false);

        // Use screen buffer manager for character erasure within current line
        _screenBufferManager.EraseCharactersInLine(count, _cursorManager.Row, _cursorManager.Column, _attributeManager.CurrentAttributes, _attributeManager.CurrentCharacterProtection);

        // Sync state with managers
        _getState().CursorX = _cursorManager.Column;
        _getState().CursorY = _cursorManager.Row;
        _getState().WrapPending = _cursorManager.WrapPending;
    }
}
