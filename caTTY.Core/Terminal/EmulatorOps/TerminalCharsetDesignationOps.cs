using caTTY.Core.Managers;
using caTTY.Core.Types;

namespace caTTY.Core.Terminal.EmulatorOps;

/// <summary>
///     Handles character set designation operations for the terminal emulator.
///     Provides methods for designating character sets to G0/G1/G2/G3 slots.
///     Extracted from TerminalEmulator to reduce file size and improve maintainability.
/// </summary>
internal class TerminalCharsetDesignationOps
{
    private readonly ICharacterSetManager _characterSetManager;

    /// <summary>
    ///     Creates a new charset designation operations handler.
    /// </summary>
    /// <param name="characterSetManager">The character set manager for managing charset state</param>
    public TerminalCharsetDesignationOps(ICharacterSetManager characterSetManager)
    {
        _characterSetManager = characterSetManager;
    }

    /// <summary>
    ///     Designates a character set to a specific G slot.
    ///     Implements ESC ( X, ESC ) X, ESC * X, ESC + X sequences.
    /// </summary>
    /// <param name="slot">The G slot to designate (G0, G1, G2, G3)</param>
    /// <param name="charset">The character set identifier</param>
    public void DesignateCharacterSet(string slot, string charset)
    {
        CharacterSetKey slotKey = slot switch
        {
            "G0" => CharacterSetKey.G0,
            "G1" => CharacterSetKey.G1,
            "G2" => CharacterSetKey.G2,
            "G3" => CharacterSetKey.G3,
            _ => CharacterSetKey.G0
        };

        _characterSetManager.DesignateCharacterSet(slotKey, charset);
    }
}
