using caTTY.Core.Terminal;

namespace caTTY.Core.Terminal.EmulatorOps;

/// <summary>
///     Handles screen update operations for the terminal emulator.
///     Extracted from TerminalEmulator to reduce file size and improve maintainability.
/// </summary>
internal class TerminalScreenUpdateOps
{
    private readonly Action<ScreenUpdatedEventArgs> _onScreenUpdated;

    /// <summary>
    ///     Creates a new screen update operations handler.
    /// </summary>
    /// <param name="onScreenUpdated">Action to invoke when the screen is updated</param>
    public TerminalScreenUpdateOps(Action<ScreenUpdatedEventArgs> onScreenUpdated)
    {
        _onScreenUpdated = onScreenUpdated;
    }

    /// <summary>
    ///     Raises the ScreenUpdated event.
    /// </summary>
    public void OnScreenUpdated()
    {
        _onScreenUpdated(new ScreenUpdatedEventArgs());
    }
}
