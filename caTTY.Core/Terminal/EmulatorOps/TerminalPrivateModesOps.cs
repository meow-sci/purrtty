using caTTY.Core.Managers;

namespace caTTY.Core.Terminal.EmulatorOps;

/// <summary>
///     Handles private mode save/restore operations for the terminal emulator.
///     Extracted from TerminalEmulator to reduce file size and improve maintainability.
/// </summary>
internal class TerminalPrivateModesOps
{
    private readonly IModeManager _modeManager;

    /// <summary>
    ///     Creates a new private modes operations handler.
    /// </summary>
    /// <param name="modeManager">The mode manager for saving and restoring private modes</param>
    public TerminalPrivateModesOps(IModeManager modeManager)
    {
        _modeManager = modeManager;
    }

    /// <summary>
    ///     Saves the current state of specified private modes for later restoration.
    /// </summary>
    /// <param name="modes">Array of private mode numbers to save</param>
    public void SavePrivateModes(int[] modes)
    {
        // Save the current state of each specified mode
        _modeManager.SavePrivateModes(modes);
    }

    /// <summary>
    ///     Restores the previously saved state of specified private modes.
    /// </summary>
    /// <param name="modes">Array of private mode numbers to restore</param>
    public void RestorePrivateModes(int[] modes)
    {
        // Restore the saved state of each specified mode
        _modeManager.RestorePrivateModes(modes);
    }
}
