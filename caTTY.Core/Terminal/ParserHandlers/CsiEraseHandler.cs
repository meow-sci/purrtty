using caTTY.Core.Types;

namespace caTTY.Core.Terminal.ParserHandlers;

/// <summary>
///     Handles CSI erase-related sequences (ED, EL, DECSED, DECSEL, ECH).
/// </summary>
internal class CsiEraseHandler
{
    private readonly TerminalEmulator _terminal;

    public CsiEraseHandler(TerminalEmulator terminal)
    {
        _terminal = terminal;
    }

    /// <summary>
    ///     Handles Erase in Display (ED) - CSI J
    /// </summary>
    public void HandleEraseInDisplay(CsiMessage message)
    {
        _terminal.ClearDisplay(message.Mode ?? 0);
    }

    /// <summary>
    ///     Handles Erase in Line (EL) - CSI K
    /// </summary>
    public void HandleEraseInLine(CsiMessage message)
    {
        _terminal.ClearLine(message.Mode ?? 0);
    }

    /// <summary>
    ///     Handles Selective Erase in Display (DECSED) - CSI ? J
    /// </summary>
    public void HandleSelectiveEraseInDisplay(CsiMessage message)
    {
        _terminal.ClearDisplaySelective(message.Mode ?? 0);
    }

    /// <summary>
    ///     Handles Selective Erase in Line (DECSEL) - CSI ? K
    /// </summary>
    public void HandleSelectiveEraseInLine(CsiMessage message)
    {
        _terminal.ClearLineSelective(message.Mode ?? 0);
    }

    /// <summary>
    ///     Handles Erase Character (ECH) - CSI X
    /// </summary>
    public void HandleEraseCharacter(CsiMessage message)
    {
        _terminal.EraseCharactersInLine(message.Count ?? 1);
    }
}
