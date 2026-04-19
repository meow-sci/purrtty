using caTTY.Core.Types;

namespace caTTY.Core.Terminal.EmulatorOps;

/// <summary>
///     Handles OSC title and icon name operations for the terminal emulator.
///     Extracted from TerminalEmulator to reduce file size and improve maintainability.
/// </summary>
internal class TerminalOscTitleIconOps
{
    private readonly Func<TerminalState> _getState;
    private readonly Action<string> _onTitleChanged;
    private readonly Action<string> _onIconNameChanged;

    /// <summary>
    ///     Creates a new OSC title/icon operations handler.
    /// </summary>
    /// <param name="getState">Function to get the current terminal state</param>
    /// <param name="onTitleChanged">Callback to raise TitleChanged event</param>
    /// <param name="onIconNameChanged">Callback to raise IconNameChanged event</param>
    public TerminalOscTitleIconOps(
        Func<TerminalState> getState,
        Action<string> onTitleChanged,
        Action<string> onIconNameChanged)
    {
        _getState = getState;
        _onTitleChanged = onTitleChanged;
        _onIconNameChanged = onIconNameChanged;
    }

    /// <summary>
    ///     Sets the window title and emits a title change event.
    ///     Handles empty titles and title reset.
    /// </summary>
    /// <param name="title">The new window title</param>
    public void SetWindowTitle(string title)
    {
        title ??= string.Empty;
        _getState().WindowProperties.Title = title;
        _onTitleChanged(title);
    }

    /// <summary>
    ///     Sets the icon name and emits an icon name change event.
    ///     Handles empty icon names and icon name reset.
    /// </summary>
    /// <param name="iconName">The new icon name</param>
    public void SetIconName(string iconName)
    {
        iconName ??= string.Empty;
        _getState().WindowProperties.IconName = iconName;
        _onIconNameChanged(iconName);
    }

    /// <summary>
    ///     Sets both window title and icon name to the same value.
    ///     Emits both title change and icon name change events.
    /// </summary>
    /// <param name="title">The new title and icon name</param>
    public void SetTitleAndIcon(string title)
    {
        title ??= string.Empty;
        _getState().WindowProperties.Title = title;
        _getState().WindowProperties.IconName = title;
        _onTitleChanged(title);
        _onIconNameChanged(title);
    }

    /// <summary>
    ///     Gets the current window title.
    /// </summary>
    /// <returns>The current window title</returns>
    public string GetWindowTitle()
    {
        return _getState().WindowProperties.Title;
    }

    /// <summary>
    ///     Gets the current icon name.
    /// </summary>
    /// <returns>The current icon name</returns>
    public string GetIconName()
    {
        return _getState().WindowProperties.IconName;
    }
}
