using caTTY.Core.Types;

namespace caTTY.Core.Terminal.ParserHandlers;

/// <summary>
///     Handles CSI DEC mode setting sequences.
///     Processes DEC private mode set/reset and save/restore operations.
/// </summary>
internal class CsiDecModeHandler
{
    private readonly TerminalEmulator _terminal;

    public CsiDecModeHandler(TerminalEmulator terminal)
    {
        _terminal = terminal;
    }

    /// <summary>
    ///     Handles DEC private mode set (CSI ? Pm h).
    /// </summary>
    public void HandleDecModeSet(CsiMessage message)
    {
        if (message.DecModes != null)
        {
            foreach (int mode in message.DecModes)
            {
                _terminal.SetDecMode(mode, true);
            }
        }
    }

    /// <summary>
    ///     Handles DEC private mode reset (CSI ? Pm l).
    /// </summary>
    public void HandleDecModeReset(CsiMessage message)
    {
        if (message.DecModes != null)
        {
            foreach (int mode in message.DecModes)
            {
                _terminal.SetDecMode(mode, false);
            }
        }
    }

    /// <summary>
    ///     Handles save private modes (CSI ? Pm s).
    /// </summary>
    public void HandleSavePrivateMode(CsiMessage message)
    {
        if (message.DecModes != null)
        {
            _terminal.SavePrivateModes(message.DecModes);
        }
    }

    /// <summary>
    ///     Handles restore private modes (CSI ? Pm r).
    /// </summary>
    public void HandleRestorePrivateMode(CsiMessage message)
    {
        if (message.DecModes != null)
        {
            _terminal.RestorePrivateModes(message.DecModes);
        }
    }
}
