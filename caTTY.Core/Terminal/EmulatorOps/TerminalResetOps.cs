using caTTY.Core.Managers;
using caTTY.Core.Types;
using Microsoft.Extensions.Logging;

namespace caTTY.Core.Terminal.EmulatorOps;

/// <summary>
///     Handles reset operations for the terminal emulator.
///     Extracted from TerminalEmulator to reduce file size and improve maintainability.
/// </summary>
internal class TerminalResetOps
{
    private readonly Func<TerminalState> _getState;
    private readonly Func<IScreenBuffer> _getScreenBuffer;
    private readonly Func<ICursor> _getCursor;
    private readonly ICursorManager _cursorManager;
    private readonly IAttributeManager _attributeManager;
    private readonly IModeManager _modeManager;
    private readonly Func<int> _getWidth;
    private readonly Func<int> _getHeight;
    private readonly ILogger _logger;

    /// <summary>
    ///     Creates a new reset operations handler.
    /// </summary>
    /// <param name="getState">Function to get the terminal state</param>
    /// <param name="getScreenBuffer">Function to get the screen buffer</param>
    /// <param name="getCursor">Function to get the cursor</param>
    /// <param name="cursorManager">Cursor manager for cursor operations</param>
    /// <param name="attributeManager">Attribute manager for SGR attributes</param>
    /// <param name="modeManager">Mode manager for terminal modes</param>
    /// <param name="getWidth">Function to get terminal width</param>
    /// <param name="getHeight">Function to get terminal height</param>
    /// <param name="logger">Logger for debugging</param>
    public TerminalResetOps(
        Func<TerminalState> getState,
        Func<IScreenBuffer> getScreenBuffer,
        Func<ICursor> getCursor,
        ICursorManager cursorManager,
        IAttributeManager attributeManager,
        IModeManager modeManager,
        Func<int> getWidth,
        Func<int> getHeight,
        ILogger logger)
    {
        _getState = getState;
        _getScreenBuffer = getScreenBuffer;
        _getCursor = getCursor;
        _cursorManager = cursorManager;
        _attributeManager = attributeManager;
        _modeManager = modeManager;
        _getWidth = getWidth;
        _getHeight = getHeight;
        _logger = logger;
    }

    /// <summary>
    ///     Resets the terminal to its initial state.
    ///     Implements ESC c (Reset to Initial State) sequence.
    /// </summary>
    public void ResetToInitialState()
    {
        var state = _getState();
        var screenBuffer = _getScreenBuffer();
        var cursor = _getCursor();

        // Reset terminal state
        state.Reset();

        // Clear the screen buffer
        screenBuffer.Clear();

        // Update cursor to match reset state
        cursor.SetPosition(state.CursorY, state.CursorX);

        // Reset cursor manager style to match state
        _cursorManager.Style = state.CursorStyle;
    }

    /// <summary>
    ///     Performs a soft reset of the terminal.
    ///     Implements CSI ! p (DECSTR - DEC Soft Terminal Reset) sequence.
    ///     Resets terminal modes and state without clearing the screen buffer or cursor position.
    /// </summary>
    public void SoftReset()
    {
        var state = _getState();
        var width = _getWidth();
        var height = _getHeight();

        // Reset cursor position to home (0,0)
        state.CursorX = 0;
        state.CursorY = 0;

        // Clear saved cursor positions
        state.SavedCursor = null;
        state.AnsiSavedCursor = null;

        // Reset wrap pending state
        state.WrapPending = false;

        // Reset cursor style and visibility to defaults
        state.CursorStyle = CursorStyle.BlinkingBlock;
        state.CursorVisible = true;

        // Reset terminal modes to defaults
        state.ApplicationCursorKeys = false;
        state.OriginMode = false;
        state.AutoWrapMode = true;

        // Reset scroll region to full screen
        state.ScrollTop = 0;
        state.ScrollBottom = height - 1;

        // Reset character protection to unprotected
        state.CurrentCharacterProtection = false;
        _attributeManager.CurrentCharacterProtection = false;

        // Reset SGR attributes to defaults
        state.CurrentSgrState = SgrAttributes.Default;
        _attributeManager.ResetAttributes();

        // Reset character sets to defaults (ASCII)
        state.CharacterSets = new CharacterSetState();

        // Reset UTF-8 mode to enabled
        state.Utf8Mode = true;

        // Reset tab stops to default (every 8 columns)
        state.InitializeTabStops(width);

        // Update cursor manager to match reset state
        _cursorManager.MoveTo(state.CursorY, state.CursorX);
        _cursorManager.Visible = state.CursorVisible;
        _cursorManager.Style = state.CursorStyle;

        // Update mode manager to match reset state
        _modeManager.AutoWrapMode = state.AutoWrapMode;
        _modeManager.ApplicationCursorKeys = state.ApplicationCursorKeys;
        _modeManager.CursorVisible = state.CursorVisible;
        _modeManager.OriginMode = state.OriginMode;

        _logger.LogDebug("Soft reset completed - modes and state reset without clearing screen");
    }
}
