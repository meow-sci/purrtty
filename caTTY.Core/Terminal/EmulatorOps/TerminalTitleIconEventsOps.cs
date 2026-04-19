using caTTY.Core.Terminal;

namespace caTTY.Core.Terminal.EmulatorOps;

/// <summary>
///     Handles title and icon name event operations for the terminal emulator.
///     Extracted from TerminalEmulator to reduce file size and improve maintainability.
/// </summary>
internal class TerminalTitleIconEventsOps
{
    private readonly Action<TitleChangeEventArgs> _onTitleChanged;
    private readonly Action<IconNameChangeEventArgs> _onIconNameChanged;

    /// <summary>
    ///     Creates a new title/icon event operations handler.
    /// </summary>
    /// <param name="onTitleChanged">Action to invoke when the title changes</param>
    /// <param name="onIconNameChanged">Action to invoke when the icon name changes</param>
    public TerminalTitleIconEventsOps(
        Action<TitleChangeEventArgs> onTitleChanged,
        Action<IconNameChangeEventArgs> onIconNameChanged)
    {
        _onTitleChanged = onTitleChanged;
        _onIconNameChanged = onIconNameChanged;
    }

    /// <summary>
    ///     Raises the TitleChanged event.
    /// </summary>
    /// <param name="newTitle">The new window title</param>
    public void OnTitleChanged(string newTitle)
    {
        _onTitleChanged(new TitleChangeEventArgs(newTitle));
    }

    /// <summary>
    ///     Raises the IconNameChanged event.
    /// </summary>
    /// <param name="newIconName">The new icon name</param>
    public void OnIconNameChanged(string newIconName)
    {
        _onIconNameChanged(new IconNameChangeEventArgs(newIconName));
    }
}
