using caTTY.Core.Types;

namespace caTTY.Core.Terminal.ParserHandlers;

/// <summary>
///     Handles scroll-related CSI sequences.
/// </summary>
internal class CsiScrollHandler
{
    private readonly TerminalEmulator _terminal;

    public CsiScrollHandler(TerminalEmulator terminal)
    {
        _terminal = terminal;
    }

    /// <summary>
    ///     Handles scroll up (CSI S).
    /// </summary>
    public void HandleScrollUp(CsiMessage message)
    {
        _terminal.ScrollScreenUp(message.Lines ?? 1);
    }

    /// <summary>
    ///     Handles scroll down (CSI T).
    /// </summary>
    public void HandleScrollDown(CsiMessage message)
    {
        _terminal.ScrollScreenDown(message.Lines ?? 1);
    }

    /// <summary>
    ///     Handles set scroll region (CSI r).
    /// </summary>
    public void HandleSetScrollRegion(CsiMessage message)
    {
        _terminal.SetScrollRegion(message.Top, message.Bottom);
    }
}
