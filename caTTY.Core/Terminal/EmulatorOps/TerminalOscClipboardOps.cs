using Microsoft.Extensions.Logging;

namespace caTTY.Core.Terminal.EmulatorOps;

/// <summary>
///     Handles OSC clipboard operations for the terminal emulator.
///     Extracted from TerminalEmulator to reduce file size and improve maintainability.
/// </summary>
internal class TerminalOscClipboardOps
{
    private readonly ILogger _logger;
    private readonly Action<string, string?, bool> _onClipboardRequest;

    /// <summary>
    ///     Creates a new OSC clipboard operations handler.
    /// </summary>
    /// <param name="logger">Logger instance for diagnostic output</param>
    /// <param name="onClipboardRequest">Callback to raise ClipboardRequest event (selectionTarget, data, isQuery)</param>
    public TerminalOscClipboardOps(
        ILogger logger,
        Action<string, string?, bool> onClipboardRequest)
    {
        _logger = logger;
        _onClipboardRequest = onClipboardRequest;
    }

    /// <summary>
    ///     Handles clipboard operations from OSC 52 sequences.
    ///     Parses selection targets and clipboard data, applies safety limits,
    ///     and emits clipboard events for game integration.
    /// </summary>
    /// <param name="payload">The OSC 52 payload (selection;data)</param>
    public void HandleClipboard(string payload)
    {
        if (string.IsNullOrEmpty(payload))
        {
            return;
        }

        // OSC 52 format: ESC ] 52 ; selection ; data BEL/ST
        // where selection can be: c (clipboard), p (primary), s (secondary), 0-7 (cut buffers)
        // and data can be: base64 encoded data, ? (query), or empty (clear)

        // Parse selection target and data
        string[] parts = payload.Split(';', 3); // Limit to 3 parts to handle data with semicolons
        if (parts.Length < 2)
        {
            _logger.LogWarning("Invalid OSC 52 format: missing selection or data part");
            return;
        }

        string selectionTarget = parts[1];
        string? data = parts.Length > 2 ? parts[2] : null;

        // Validate selection target
        if (string.IsNullOrEmpty(selectionTarget))
        {
            _logger.LogWarning("Invalid OSC 52: empty selection target");
            return;
        }

        // Handle clipboard query
        if (data == "?")
        {
            OnClipboardRequest(selectionTarget, null, isQuery: true);
            _logger.LogDebug("Clipboard query for selection: {Selection}", selectionTarget);
            return;
        }

        // Handle clipboard clear
        if (string.IsNullOrEmpty(data))
        {
            OnClipboardRequest(selectionTarget, string.Empty, isQuery: false);
            _logger.LogDebug("Clipboard clear for selection: {Selection}", selectionTarget);
            return;
        }

        // Handle clipboard data - decode from base64
        try
        {
            // Apply safety limit: cap base64 data length before decoding
            const int MaxBase64Length = 4096; // ~3KB decoded data
            if (data.Length > MaxBase64Length)
            {
                _logger.LogWarning("OSC 52 base64 data too long ({Length} > {Max}), ignoring",
                    data.Length, MaxBase64Length);
                return;
            }

            // Decode base64 data
            byte[] decodedBytes = Convert.FromBase64String(data);

            // Apply safety limit: cap decoded data size
            const int MaxDecodedSize = 2048; // 2KB max decoded size
            if (decodedBytes.Length > MaxDecodedSize)
            {
                _logger.LogWarning("OSC 52 decoded data too large ({Size} > {Max}), ignoring",
                    decodedBytes.Length, MaxDecodedSize);
                return;
            }

            // Convert to UTF-8 string
            string decodedText = System.Text.Encoding.UTF8.GetString(decodedBytes);

            // Emit clipboard event
            OnClipboardRequest(selectionTarget, decodedText, isQuery: false);
            _logger.LogDebug("Clipboard data for selection {Selection}: {Length} bytes",
                selectionTarget, decodedBytes.Length);
        }
        catch (FormatException)
        {
            // Invalid base64 - ignore gracefully
            _logger.LogWarning("OSC 52 invalid base64 data, ignoring gracefully");
        }
        catch (Exception ex)
        {
            // Other decoding errors - ignore gracefully
            _logger.LogWarning(ex, "OSC 52 clipboard decoding error, ignoring gracefully");
        }
    }

    /// <summary>
    ///     Raises the ClipboardRequest event.
    /// </summary>
    /// <param name="selectionTarget">The selection target (e.g., "c" for clipboard, "p" for primary)</param>
    /// <param name="data">The clipboard data (null for queries)</param>
    /// <param name="isQuery">Whether this is a clipboard query operation</param>
    public void OnClipboardRequest(string selectionTarget, string? data, bool isQuery = false)
    {
        _onClipboardRequest(selectionTarget, data, isQuery);
    }
}
