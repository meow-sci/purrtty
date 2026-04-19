using caTTY.Core.Managers;

namespace caTTY.Core.Terminal.EmulatorOps;

/// <summary>
///     Viewport scrolling operations for terminal emulator.
///     Handles user-initiated scrolling through scrollback history.
/// </summary>
internal class TerminalViewportOps
{
    private readonly IScrollbackManager _scrollbackManager;
    private readonly Action _onScreenUpdated;

    /// <summary>
    ///     Creates a new viewport operations handler.
    /// </summary>
    /// <param name="scrollbackManager">The scrollback manager for viewport operations</param>
    /// <param name="onScreenUpdated">Callback to notify when screen needs refresh</param>
    public TerminalViewportOps(IScrollbackManager scrollbackManager, Action onScreenUpdated)
    {
        _scrollbackManager = scrollbackManager;
        _onScreenUpdated = onScreenUpdated;
    }

    /// <summary>
    ///     Scrolls the viewport up by the specified number of lines.
    ///     Disables auto-scroll if not already at the top.
    /// </summary>
    /// <param name="lines">Number of lines to scroll up</param>
    public void ScrollViewportUp(int lines)
    {
        _scrollbackManager.ScrollUp(lines);
        _onScreenUpdated();
    }

    /// <summary>
    ///     Scrolls the viewport down by the specified number of lines.
    ///     Re-enables auto-scroll if scrolled to the bottom.
    /// </summary>
    /// <param name="lines">Number of lines to scroll down</param>
    public void ScrollViewportDown(int lines)
    {
        _scrollbackManager.ScrollDown(lines);
        _onScreenUpdated();
    }

    /// <summary>
    ///     Scrolls to the top of the scrollback buffer.
    ///     Disables auto-scroll.
    /// </summary>
    public void ScrollViewportToTop()
    {
        _scrollbackManager.ScrollToTop();
        _onScreenUpdated();
    }

    /// <summary>
    ///     Scrolls to the bottom of the scrollback buffer.
    ///     Re-enables auto-scroll.
    /// </summary>
    public void ScrollViewportToBottom()
    {
        _scrollbackManager.ScrollToBottom();
        _onScreenUpdated();
    }
}
