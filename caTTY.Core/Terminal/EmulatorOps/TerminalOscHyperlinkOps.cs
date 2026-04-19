using caTTY.Core.Managers;
using caTTY.Core.Types;
using Microsoft.Extensions.Logging;

namespace caTTY.Core.Terminal.EmulatorOps;

/// <summary>
///     Handles OSC hyperlink operations for the terminal emulator.
///     Extracted from TerminalEmulator to reduce file size and improve maintainability.
/// </summary>
internal class TerminalOscHyperlinkOps
{
    private readonly ILogger _logger;
    private readonly IAttributeManager _attributeManager;
    private readonly Func<TerminalState> _getState;

    /// <summary>
    ///     Creates a new OSC hyperlink operations handler.
    /// </summary>
    /// <param name="logger">Logger instance for diagnostic output</param>
    /// <param name="attributeManager">Attribute manager for setting hyperlink URLs</param>
    /// <param name="getState">Function to get the current terminal state</param>
    public TerminalOscHyperlinkOps(
        ILogger logger,
        IAttributeManager attributeManager,
        Func<TerminalState> getState)
    {
        _logger = logger;
        _attributeManager = attributeManager;
        _getState = getState;
    }

    /// <summary>
    ///     Handles hyperlink operations from OSC 8 sequences.
    ///     Associates URLs with character ranges by setting current hyperlink state.
    ///     Clears hyperlink state when empty URL is provided.
    /// </summary>
    /// <param name="url">The hyperlink URL, or empty string to clear hyperlink state</param>
    public void HandleHyperlink(string url)
    {
        // OSC 8 format: ESC ] 8 ; [params] ; [url] BEL/ST
        // where params can include id=<id> and other key=value pairs
        // For now, we only handle the URL part and ignore parameters

        if (string.IsNullOrEmpty(url))
        {
            // Clear hyperlink state - OSC 8 ;; ST
            _attributeManager.SetHyperlinkUrl(null);
            _getState().CurrentHyperlinkUrl = null;
            _logger.LogDebug("Cleared hyperlink state");
        }
        else
        {
            // Set hyperlink URL for subsequent characters
            _attributeManager.SetHyperlinkUrl(url);
            _getState().CurrentHyperlinkUrl = url;
            _logger.LogDebug("Set hyperlink URL: {Url}", url);
        }
    }
}
