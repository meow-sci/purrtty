using caTTY.Core.Types;

namespace caTTY.Core.Managers;

/// <summary>
///     Manages alternate screen buffer operations with proper state isolation.
///     Handles switching between primary and alternate screen buffers while preserving
///     cursor positions and attributes independently per buffer.
/// </summary>
public class AlternateScreenManager : IAlternateScreenManager
{
    private readonly TerminalState _state;
    private readonly ICursorManager _cursorManager;
    private readonly DualScreenBuffer _dualScreenBuffer;
    private readonly IScreenBufferManager _primaryBufferManager;
    private readonly IScreenBufferManager _alternateBufferManager;

    /// <summary>
    ///     Creates a new alternate screen manager.
    /// </summary>
    /// <param name="state">Terminal state for tracking alternate screen status and cursor positions</param>
    /// <param name="cursorManager">Cursor manager for cursor operations</param>
    /// <param name="dualScreenBuffer">Dual screen buffer containing primary and alternate buffers</param>
    public AlternateScreenManager(TerminalState state, ICursorManager cursorManager, DualScreenBuffer dualScreenBuffer)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _cursorManager = cursorManager ?? throw new ArgumentNullException(nameof(cursorManager));
        _dualScreenBuffer = dualScreenBuffer ?? throw new ArgumentNullException(nameof(dualScreenBuffer));

        // Create buffer managers for each screen buffer
        _primaryBufferManager = new ScreenBufferManager(_dualScreenBuffer.Primary);
        _alternateBufferManager = new ScreenBufferManager(_dualScreenBuffer.Alternate);
    }

    /// <summary>
    ///     Gets whether the alternate screen buffer is currently active.
    /// </summary>
    public bool IsAlternateActive => _state.IsAlternateScreenActive;

    /// <summary>
    ///     Activates the alternate screen buffer.
    ///     Preserves current cursor position and attributes in primary buffer state.
    /// </summary>
    public void ActivateAlternate()
    {
        if (_state.IsAlternateScreenActive)
        {
            return; // Already active
        }

        // Save current cursor position to primary buffer state
        _state.PrimaryCursorX = _cursorManager.Column;
        _state.PrimaryCursorY = _cursorManager.Row;
        _state.PrimaryWrapPending = _state.WrapPending;

        // Switch to alternate screen
        _state.IsAlternateScreenActive = true;

        // Restore alternate screen cursor position
        _cursorManager.MoveTo(_state.AlternateCursorY, _state.AlternateCursorX);
        _state.WrapPending = _state.AlternateWrapPending;
        
        // Sync terminal state with cursor manager
        _state.CursorX = _cursorManager.Column;
        _state.CursorY = _cursorManager.Row;
    }

    /// <summary>
    ///     Deactivates the alternate screen buffer and returns to primary.
    ///     Restores cursor position and attributes from primary buffer state.
    /// </summary>
    public void DeactivateAlternate()
    {
        if (!_state.IsAlternateScreenActive)
        {
            return; // Already on primary
        }

        // Save current cursor position to alternate buffer state
        _state.AlternateCursorX = _cursorManager.Column;
        _state.AlternateCursorY = _cursorManager.Row;
        _state.AlternateWrapPending = _state.WrapPending;

        // Switch to primary screen
        _state.IsAlternateScreenActive = false;

        // Restore primary screen cursor position
        _cursorManager.MoveTo(_state.PrimaryCursorY, _state.PrimaryCursorX);
        _state.WrapPending = _state.PrimaryWrapPending;
        
        // Sync terminal state with cursor manager
        _state.CursorX = _cursorManager.Column;
        _state.CursorY = _cursorManager.Row;
    }

    /// <summary>
    ///     Activates alternate screen with cursor save (mode 1047).
    ///     Saves current cursor position before switching to alternate buffer.
    /// </summary>
    public void ActivateAlternateWithCursorSave()
    {
        // Save cursor position using terminal's save mechanism
        _state.SavedCursor = (_cursorManager.Column, _cursorManager.Row);
        
        ActivateAlternate();
    }

    /// <summary>
    ///     Deactivates alternate screen with cursor restore (mode 1047).
    ///     Restores saved cursor position after switching back to primary buffer.
    /// </summary>
    public void DeactivateAlternateWithCursorRestore()
    {
        DeactivateAlternate();
        
        // Restore saved cursor position if available
        if (_state.SavedCursor.HasValue)
        {
            var (x, y) = _state.SavedCursor.Value;
            _cursorManager.MoveTo(y, x);
            _state.SavedCursor = null;
        }
    }

    /// <summary>
    ///     Activates alternate screen with clear and cursor save (mode 1049).
    ///     Clears alternate buffer and saves cursor position before switching.
    /// </summary>
    public void ActivateAlternateWithClearAndCursorSave()
    {
        // Save cursor position using terminal's save mechanism
        _state.SavedCursor = (_cursorManager.Column, _cursorManager.Row);
        
        ActivateAlternate();
        ClearAlternateBuffer();
        
        // Move cursor to origin after clearing
        _cursorManager.MoveTo(0, 0);
        _state.AlternateCursorX = 0;
        _state.AlternateCursorY = 0;
        _state.AlternateWrapPending = false;
    }

    /// <summary>
    ///     Clears the alternate screen buffer and resets cursor to origin.
    /// </summary>
    public void ClearAlternateBuffer()
    {
        _dualScreenBuffer.ClearAlternate();
        
        // If currently on alternate screen, reset cursor position
        if (_state.IsAlternateScreenActive)
        {
            _cursorManager.MoveTo(0, 0);
            _state.AlternateCursorX = 0;
            _state.AlternateCursorY = 0;
            _state.AlternateWrapPending = false;
            
            // Sync terminal state with cursor manager
            _state.CursorX = _cursorManager.Column;
            _state.CursorY = _cursorManager.Row;
            _state.WrapPending = _state.AlternateWrapPending;
        }
    }

    /// <summary>
    ///     Gets the current active screen buffer manager.
    /// </summary>
    /// <returns>The screen buffer manager for the currently active buffer</returns>
    public IScreenBufferManager GetCurrentBuffer()
    {
        return _state.IsAlternateScreenActive ? _alternateBufferManager : _primaryBufferManager;
    }

    /// <summary>
    ///     Gets the primary screen buffer manager.
    /// </summary>
    /// <returns>The primary screen buffer manager</returns>
    public IScreenBufferManager GetPrimaryBuffer()
    {
        return _primaryBufferManager;
    }

    /// <summary>
    ///     Gets the alternate screen buffer manager.
    /// </summary>
    /// <returns>The alternate screen buffer manager</returns>
    public IScreenBufferManager GetAlternateBuffer()
    {
        return _alternateBufferManager;
    }
}