using caTTY.Core.Terminal;

namespace caTTY.Core.Terminal.EmulatorOps;

/// <summary>
///     Handles bell operations for the terminal emulator.
///     Extracted from TerminalEmulator to reduce file size and improve maintainability.
/// </summary>
internal class TerminalBellOps
{
    private readonly Action _onBell;

    /// <summary>
    ///     Creates a new bell operations handler.
    /// </summary>
    /// <param name="onBell">Action to invoke when bell is triggered</param>
    public TerminalBellOps(Action onBell)
    {
        _onBell = onBell;
    }

    /// <summary>
    ///     Handles the bell character (BEL, 0x07).
    ///     Triggers the bell event.
    /// </summary>
    public void HandleBell()
    {
        _onBell();
    }
}
