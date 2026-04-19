using caTTY.Core.Types;

namespace caTTY.Core.Terminal.EmulatorOps;

/// <summary>
///     Handles bracketed paste operations for the terminal emulator.
///     Extracted from TerminalEmulator to reduce file size and improve maintainability.
/// </summary>
internal class TerminalBracketedPasteOps
{
    private readonly Func<TerminalState> _getState;

    /// <summary>
    ///     Creates a new bracketed paste operations handler.
    /// </summary>
    /// <param name="getState">Function to get the current terminal state</param>
    public TerminalBracketedPasteOps(Func<TerminalState> getState)
    {
        _getState = getState;
    }

    /// <summary>
    ///     Wraps paste content with bracketed paste escape sequences if bracketed paste mode is enabled.
    ///     When bracketed paste mode is enabled (DECSET 2004), paste content is wrapped with:
    ///     - Start marker: ESC[200~
    ///     - End marker: ESC[201~
    /// </summary>
    /// <param name="pasteContent">The content to be pasted</param>
    /// <returns>The paste content, optionally wrapped with bracketed paste markers</returns>
    public string WrapPasteContent(string pasteContent)
    {
        if (string.IsNullOrEmpty(pasteContent))
        {
            return pasteContent;
        }

        if (_getState().BracketedPasteMode)
        {
            return $"\x1b[200~{pasteContent}\x1b[201~";
        }

        return pasteContent;
    }

    /// <summary>
    ///     Wraps paste content with bracketed paste escape sequences if bracketed paste mode is enabled.
    ///     This overload accepts ReadOnlySpan&lt;char&gt; for performance-sensitive scenarios.
    /// </summary>
    /// <param name="pasteContent">The content to be pasted</param>
    /// <returns>The paste content, optionally wrapped with bracketed paste markers</returns>
    public string WrapPasteContent(ReadOnlySpan<char> pasteContent)
    {
        if (pasteContent.IsEmpty)
        {
            return string.Empty;
        }

        if (_getState().BracketedPasteMode)
        {
            return $"\x1b[200~{pasteContent.ToString()}\x1b[201~";
        }

        return pasteContent.ToString();
    }

    /// <summary>
    ///     Checks if bracketed paste mode is currently enabled.
    ///     This is a convenience method for external components that need to check paste mode state.
    /// </summary>
    /// <returns>True if bracketed paste mode is enabled, false otherwise</returns>
    public bool IsBracketedPasteModeEnabled()
    {
        return _getState().BracketedPasteMode;
    }
}
