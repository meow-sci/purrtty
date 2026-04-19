using caTTY.Core.Types;
using Microsoft.Extensions.Logging;

namespace caTTY.Core.Terminal.ParserHandlers;

/// <summary>
///     Handles DCS (Device Control String) sequence processing.
/// </summary>
internal class DcsHandler
{
    private readonly ILogger _logger;
    private readonly TerminalEmulator _terminal;

    public DcsHandler(TerminalEmulator terminal, ILogger logger)
    {
        _terminal = terminal;
        _logger = logger;
    }

    /// <summary>
    ///     Handles DECRQSS (Request Status String) DCS sequences.
    ///     Responds with current terminal state for supported requests.
    /// </summary>
    /// <param name="message">The DCS message containing the request</param>
    public void HandleDecrqss(DcsMessage message)
    {
        // Extract payload from the raw DCS sequence
        // The raw string contains the full sequence: DCS [params] [intermediates] Final [payload] ST
        // We need to find the 'q' that terminates the header and extract the payload after it

        string? payload = ExtractDecrqssPayload(message.Raw);
        if (payload == null)
        {
            _logger.LogWarning("Failed to extract DECRQSS payload from: {Raw}", message.Raw);
            return;
        }

        // DECRQSS response: DCS <status> $ r <response> ST
        // status: 1 = valid, 0 = invalid (following xterm convention)

        string response;
        bool valid;

        switch (payload)
        {
            case "\"q": // DECSCA - Select Character Protection Attribute
                // Not implemented yet
                valid = false;
                response = payload;
                break;

            case "\"p": // DECSCL - Set Conformance Level
                // Not implemented yet
                valid = false;
                response = payload;
                break;

            case "m": // SGR - Select Graphic Rendition
                // Request current SGR state
                response = GenerateSgrStateResponse();
                valid = true;
                break;

            case "r": // DECSTBM - Set Top and Bottom Margins
                // Request current scroll region
                int top = _terminal.State.ScrollTop + 1; // Convert to 1-indexed
                int bottom = _terminal.State.ScrollBottom + 1; // Convert to 1-indexed
                response = $"{top};{bottom}r";
                valid = true;
                break;

            default:
                // Unknown request
                valid = false;
                response = payload;
                break;
        }

        // Generate DECRQSS response
        string status = valid ? "1" : "0";
        string decrqssResponse = $"\x1bP{status}$r{response}\x1b\\";

        _logger.LogDebug("DECRQSS request '{Payload}' -> response: {Response}", payload, decrqssResponse);
        _terminal.EmitResponse(decrqssResponse);
    }

    /// <summary>
    ///     Extracts the payload from a DECRQSS DCS sequence.
    /// </summary>
    /// <param name="raw">The raw DCS sequence</param>
    /// <returns>The extracted payload, or null if extraction failed</returns>
    private string? ExtractDecrqssPayload(string raw)
    {
        int payloadStart = -1;

        // Skip DCS initiator (ESC P or 0x90)
        int i = 0;
        if (raw.Length >= 2 && raw[0] == '\x1b' && raw[1] == 'P')
        {
            i = 2;
        }
        else if (raw.Length >= 1 && raw[0] == '\x90')
        {
            i = 1;
        }
        else
        {
            return null; // Invalid DCS sequence
        }

        // Scan for the Final Byte (0x40-0x7E) which is the command ('q')
        for (; i < raw.Length; i++)
        {
            int code = raw[i];
            if (code >= 0x40 && code <= 0x7e)
            {
                // Found the command byte. Payload starts after this.
                payloadStart = i + 1;
                break;
            }
        }

        if (payloadStart == -1)
        {
            return null; // Should not happen if parser is correct
        }

        // Payload ends before the terminator
        // Terminator is ST (ESC \ or 0x9C) or BEL (0x07)
        int payloadEnd = raw.Length;
        if (raw.EndsWith("\x1b\\"))
        {
            payloadEnd -= 2;
        }
        else if (raw.EndsWith("\x9c"))
        {
            payloadEnd -= 1;
        }
        else if (raw.EndsWith("\x07"))
        {
            payloadEnd -= 1;
        }

        if (payloadStart >= payloadEnd)
        {
            return string.Empty; // Empty payload
        }

        return raw.Substring(payloadStart, payloadEnd - payloadStart);
    }

    /// <summary>
    ///     Generates an SGR state response for DECRQSS.
    /// </summary>
    /// <returns>The SGR sequence representing current state</returns>
    private string GenerateSgrStateResponse()
    {
        SgrAttributes sgr = _terminal.State.CurrentSgrState;
        var parts = new List<string> { "0" }; // Always start with reset

        if (sgr.Bold)
        {
            parts.Add("1");
        }

        if (sgr.Faint)
        {
            parts.Add("2");
        }

        if (sgr.Italic)
        {
            parts.Add("3");
        }

        if (sgr.Underline)
        {
            parts.Add("4");
        }

        if (sgr.Blink)
        {
            parts.Add("5"); // Slow blink
        }

        if (sgr.Inverse)
        {
            parts.Add("7");
        }

        if (sgr.Hidden)
        {
            parts.Add("8");
        }

        if (sgr.Strikethrough)
        {
            parts.Add("9");
        }

        // Add foreground color if set
        if (sgr.ForegroundColor.HasValue)
        {
            Color fg = sgr.ForegroundColor.Value;
            switch (fg.Type)
            {
                case ColorType.Named:
                    int fgCode = fg.NamedColor switch
                    {
                        NamedColor.Black => 30,
                        NamedColor.Red => 31,
                        NamedColor.Green => 32,
                        NamedColor.Yellow => 33,
                        NamedColor.Blue => 34,
                        NamedColor.Magenta => 35,
                        NamedColor.Cyan => 36,
                        NamedColor.White => 37,
                        NamedColor.BrightBlack => 90,
                        NamedColor.BrightRed => 91,
                        NamedColor.BrightGreen => 92,
                        NamedColor.BrightYellow => 93,
                        NamedColor.BrightBlue => 94,
                        NamedColor.BrightMagenta => 95,
                        NamedColor.BrightCyan => 96,
                        NamedColor.BrightWhite => 97,
                        _ => 39 // Default
                    };
                    parts.Add(fgCode.ToString());
                    break;

                case ColorType.Indexed:
                    parts.Add($"38;5;{fg.Index}");
                    break;

                case ColorType.Rgb:
                    parts.Add($"38;2;{fg.Red};{fg.Green};{fg.Blue}");
                    break;
            }
        }

        // Add background color if set
        if (sgr.BackgroundColor.HasValue)
        {
            Color bg = sgr.BackgroundColor.Value;
            switch (bg.Type)
            {
                case ColorType.Named:
                    int bgCode = bg.NamedColor switch
                    {
                        NamedColor.Black => 40,
                        NamedColor.Red => 41,
                        NamedColor.Green => 42,
                        NamedColor.Yellow => 43,
                        NamedColor.Blue => 44,
                        NamedColor.Magenta => 45,
                        NamedColor.Cyan => 46,
                        NamedColor.White => 47,
                        NamedColor.BrightBlack => 100,
                        NamedColor.BrightRed => 101,
                        NamedColor.BrightGreen => 102,
                        NamedColor.BrightYellow => 103,
                        NamedColor.BrightBlue => 104,
                        NamedColor.BrightMagenta => 105,
                        NamedColor.BrightCyan => 106,
                        NamedColor.BrightWhite => 107,
                        _ => 49 // Default
                    };
                    parts.Add(bgCode.ToString());
                    break;

                case ColorType.Indexed:
                    parts.Add($"48;5;{bg.Index}");
                    break;

                case ColorType.Rgb:
                    parts.Add($"48;2;{bg.Red};{bg.Green};{bg.Blue}");
                    break;
            }
        }

        return string.Join(";", parts) + "m";
    }
}
