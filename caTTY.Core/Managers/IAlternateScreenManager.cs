using caTTY.Core.Types;

namespace caTTY.Core.Managers;

/// <summary>
///     Interface for managing alternate screen buffer operations.
///     Handles switching between primary and alternate screen buffers with proper state isolation.
/// </summary>
public interface IAlternateScreenManager
{
    /// <summary>
    ///     Gets whether the alternate screen buffer is currently active.
    /// </summary>
    bool IsAlternateActive { get; }

    /// <summary>
    ///     Activates the alternate screen buffer.
    ///     Preserves current cursor position and attributes in primary buffer state.
    /// </summary>
    void ActivateAlternate();

    /// <summary>
    ///     Deactivates the alternate screen buffer and returns to primary.
    ///     Restores cursor position and attributes from primary buffer state.
    /// </summary>
    void DeactivateAlternate();

    /// <summary>
    ///     Activates alternate screen with cursor save (mode 1047).
    ///     Saves current cursor position before switching to alternate buffer.
    /// </summary>
    void ActivateAlternateWithCursorSave();

    /// <summary>
    ///     Activates alternate screen with clear and cursor save (mode 1049).
    ///     Clears alternate buffer and saves cursor position before switching.
    /// </summary>
    void ActivateAlternateWithClearAndCursorSave();

    /// <summary>
    ///     Deactivates alternate screen with cursor restore (mode 1047/1049).
    ///     Restores saved cursor position after switching back to primary buffer.
    /// </summary>
    void DeactivateAlternateWithCursorRestore();

    /// <summary>
    ///     Clears the alternate screen buffer and resets cursor to origin.
    /// </summary>
    void ClearAlternateBuffer();

    /// <summary>
    ///     Gets the current active screen buffer manager.
    /// </summary>
    /// <returns>The screen buffer manager for the currently active buffer</returns>
    IScreenBufferManager GetCurrentBuffer();

    /// <summary>
    ///     Gets the primary screen buffer manager.
    /// </summary>
    /// <returns>The primary screen buffer manager</returns>
    IScreenBufferManager GetPrimaryBuffer();

    /// <summary>
    ///     Gets the alternate screen buffer manager.
    /// </summary>
    /// <returns>The alternate screen buffer manager</returns>
    IScreenBufferManager GetAlternateBuffer();
}