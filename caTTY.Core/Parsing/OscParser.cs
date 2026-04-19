using System.Text;
using caTTY.Core.Types;
using caTTY.Core.Tracing;
using Microsoft.Extensions.Logging;

namespace caTTY.Core.Parsing;

/// <summary>
///     Parser for OSC (Operating System Command) sequences.
///     Handles OSC sequence parsing and termination detection.
///     Based on the TypeScript ParseOsc.ts implementation with identical behavior and robustness.
/// </summary>
public class OscParser : IOscParser
{
  private readonly ILogger _logger;
  private readonly ICursorPositionProvider? _cursorPositionProvider;

  /// <summary>
  ///     Maximum allowed OSC payload length to prevent memory blowups.
  /// </summary>
  private const int MaxOscPayloadLength = 1024;

  /// <summary>
  ///     Creates a new OSC parser.
  /// </summary>
  /// <param name="logger">Logger for diagnostic messages</param>
  /// <param name="cursorPositionProvider">Optional cursor position provider for tracing</param>
  public OscParser(ILogger logger, ICursorPositionProvider? cursorPositionProvider = null)
  {
    _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    _cursorPositionProvider = cursorPositionProvider;
  }

  /// <summary>
  ///     Processes a byte in the OSC sequence parsing state.
  /// </summary>
  /// <param name="b">The byte to process</param>
  /// <param name="escapeSequence">The current escape sequence buffer</param>
  /// <param name="message">The parsed OSC message if sequence is complete</param>
  /// <returns>True if the sequence is complete, false if more bytes are needed</returns>
  public bool ProcessOscByte(byte b, List<byte> escapeSequence, out OscMessage? message)
  {
    message = null;

    // Allow UTF-8 bytes in OSC sequences
    // Only reject control characters (0x00-0x1F) except for BEL (0x07) and ESC (0x1b) which are terminators
    if (b < 0x20 && b != 0x07 && b != 0x1b)
    {
      _logger.LogWarning("OSC: control character byte 0x{Byte:X2}", b);
      return false; // Continue parsing (caller should handle this)
    }

    escapeSequence.Add(b);

    // OSC terminators: BEL or ST (ESC \)
    if (b == 0x07) // BEL
    {
      message = CreateOscMessage(escapeSequence, "BEL");

      // Add tracing for completed OSC sequence
      if (message?.XtermMessage != null)
      {
        TraceHelper.TraceOscSequence(message.XtermMessage.Command, message.XtermMessage.Payload, TraceDirection.Output, _cursorPositionProvider?.Row, _cursorPositionProvider?.Column);
      }

      return true; // Sequence complete
    }

    return false; // Continue parsing
  }

  /// <summary>
  ///     Processes a byte in the OSC escape state (checking for ST terminator).
  /// </summary>
  /// <param name="b">The byte to process</param>
  /// <param name="escapeSequence">The current escape sequence buffer</param>
  /// <param name="message">The parsed OSC message if sequence is complete</param>
  /// <returns>True if the sequence is complete, false if more bytes are needed</returns>
  public bool ProcessOscEscapeByte(byte b, List<byte> escapeSequence, out OscMessage? message)
  {
    message = null;

    // We just saw an ESC while inside OSC. If next byte is "\" then it's ST terminator.
    // Otherwise, it was a literal ESC in the OSC payload and we continue in OSC.
    escapeSequence.Add(b);

    if (b == 0x5c) // \
    {
      message = CreateOscMessage(escapeSequence, "ST");

      // Add tracing for completed OSC sequence
      if (message?.XtermMessage != null)
      {
        TraceHelper.TraceOscSequence(message.XtermMessage.Command, message.XtermMessage.Payload, TraceDirection.Output, _cursorPositionProvider?.Row, _cursorPositionProvider?.Column);
      }

      return true; // Sequence complete
    }

    if (b == 0x07) // BEL
    {
      message = CreateOscMessage(escapeSequence, "BEL");

      // Add tracing for completed OSC sequence
      if (message?.XtermMessage != null)
      {
        TraceHelper.TraceOscSequence(message.XtermMessage.Command, message.XtermMessage.Payload, TraceDirection.Output, _cursorPositionProvider?.Row, _cursorPositionProvider?.Column);
      }

      return true; // Sequence complete
    }

    return false; // Continue OSC payload
  }

  /// <summary>
  ///     Creates an OSC message from the escape sequence.
  /// </summary>
  private OscMessage CreateOscMessage(List<byte> escapeSequence, string terminator)
  {
    string raw = BytesToString(escapeSequence);

    // Try to parse as xterm OSC extension
    var xtermMessage = ParseOsc(raw, terminator);
    if (xtermMessage != null)
    {
      _logger.LogDebug("OSC (xterm, {Terminator}): {Raw}", terminator, raw);
      return new OscMessage
      {
        Type = "osc",
        Raw = raw,
        Terminator = terminator,
        Implemented = xtermMessage.Implemented,
        XtermMessage = xtermMessage
      };
    }

    // Fall back to generic OSC handling - trace unknown OSC sequences
    _logger.LogDebug("OSC (opaque, {Terminator}): {Raw}", terminator, raw);

    if (TerminalTracer.Enabled)
    {
      // Trace unknown OSC sequence - extract command if possible for better tracing
      string payload = raw.Length > 2 ? raw[2..] : "";
      if (terminator == "BEL" && payload.EndsWith('\x07'))
        payload = payload[..^1];
      else if (terminator == "ST" && payload.EndsWith("\x1b\\"))
        payload = payload[..^2];

      int semicolonIndex = payload.IndexOf(';');
      if (semicolonIndex >= 0 && int.TryParse(payload[..semicolonIndex], out int unknownCommand))
      {
        string unknownData = payload[(semicolonIndex + 1)..];
        TraceHelper.TraceOscSequence(unknownCommand, unknownData, TraceDirection.Output, _cursorPositionProvider?.Row, _cursorPositionProvider?.Column);
      }
      else
      {
        // Malformed OSC - trace the raw sequence
        TraceHelper.TraceEscapeBytes(System.Text.Encoding.UTF8.GetBytes(raw), TraceDirection.Output, _cursorPositionProvider?.Row, _cursorPositionProvider?.Column);
      }
    }

    return new OscMessage { Type = "osc", Raw = raw, Terminator = terminator, Implemented = false };
  }

  /// <summary>
  ///     Parse OSC (Operating System Command) sequences for xterm extensions.
  ///     OSC sequences have the format: ESC ] Ps ; Pt BEL/ST
  ///     where Ps is the command number and Pt is the text parameter.
  ///     Based on the TypeScript parseOsc function.
  /// </summary>
  private XtermOscMessage? ParseOsc(string raw, string terminator)
  {
    // OSC sequences start with ESC ] (0x1b 0x5d)
    if (!raw.StartsWith("\x1b]"))
    {
      return null;
    }

    // Extract the payload (everything after ESC ])
    string payload = raw[2..];

    // Remove terminator from payload
    if (terminator == "BEL")
    {
      // Remove BEL (0x07) from end
      payload = payload[..^1];
    }
    else if (terminator == "ST")
    {
      // Remove ST (ESC \) from end
      payload = payload[..^2];
    }

    // Cap payload length to prevent memory blowups
    if (payload.Length > MaxOscPayloadLength)
    {
      _logger.LogWarning("OSC payload exceeds maximum length ({Length} > {Max}), ignoring",
          payload.Length, MaxOscPayloadLength);
      return null;
    }

    // Parse command number and text parameter
    int semicolonIndex = payload.IndexOf(';');
    if (semicolonIndex == -1)
    {
      // No semicolon found - could be a query command
      if (!int.TryParse(payload, out int commandNum) || !IsValidCommandNumber(commandNum))
      {
        return null;
      }

      // Handle query commands
      return commandNum switch
      {
        21 => new XtermOscMessage
        {
          Type = "osc.queryWindowTitle",
          Raw = raw,
          Terminator = terminator,
          Command = commandNum,
          Payload = string.Empty,
          Implemented = true
        },
        _ => null
      };
    }

    string commandStr = payload[..semicolonIndex];
    if (!int.TryParse(commandStr, out int command) || !IsValidCommandNumber(command))
    {
      return null;
    }

    string textParam = payload[(semicolonIndex + 1)..];

    // Handle color queries (OSC 10;? and OSC 11;?)
    if (textParam == "?" || textParam.StartsWith("?;"))
    {
      return command switch
      {
        10 => new XtermOscMessage
        {
          Type = "osc.queryForegroundColor",
          Raw = raw,
          Terminator = terminator,
          Command = command,
          Payload = textParam,
          Implemented = true
        },
        11 => new XtermOscMessage
        {
          Type = "osc.queryBackgroundColor",
          Raw = raw,
          Terminator = terminator,
          Command = command,
          Payload = textParam,
          Implemented = true
        },
        _ => null
      };
    }

    // Validate text parameter
    if (!ValidateOscParameters(command, textParam))
    {
      _logger.LogWarning("Invalid OSC parameters: command={Command}, textParam='{TextParam}' (length={Length})",
          command, textParam, textParam.Length);
      return null;
    }

    // Decode UTF-8 text parameter
    string decodedText = DecodeUtf8Text(textParam);

    // Handle title setting commands
    return command switch
    {
      0 => new XtermOscMessage
      {
        Type = "osc.setTitleAndIcon",
        Raw = raw,
        Terminator = terminator,
        Command = command,
        Payload = decodedText,
        Title = decodedText,
        Implemented = true
      },
      1 => new XtermOscMessage
      {
        Type = "osc.setIconName",
        Raw = raw,
        Terminator = terminator,
        Command = command,
        Payload = decodedText,
        IconName = decodedText,
        Implemented = true
      },
      2 => new XtermOscMessage
      {
        Type = "osc.setWindowTitle",
        Raw = raw,
        Terminator = terminator,
        Command = command,
        Payload = decodedText,
        Title = decodedText,
        Implemented = true
      },
      52 => new XtermOscMessage
      {
        Type = "osc.clipboard",
        Raw = raw,
        Terminator = terminator,
        Command = command,
        Payload = payload, // Use original payload for clipboard parsing
        ClipboardData = payload, // Store original payload for parsing
        Implemented = true
      },
      8 => ParseOsc8Hyperlink(raw, terminator, command, textParam),
      // Private-use OSC commands (1000+) are handled by the RPC layer
      >= 1000 => new XtermOscMessage
      {
        Type = "osc.private",
        Raw = raw,
        Terminator = terminator,
        Command = command,
        Payload = textParam,
        Implemented = true
      },
      _ => null
    };
  }

  /// <summary>
  ///     Decode UTF-8 text from OSC parameter.
  ///     Since the string should already be properly decoded from UTF-8 bytes,
  ///     we just return it as-is. The BytesToString method handles UTF-8 decoding.
  /// </summary>
  private string DecodeUtf8Text(string text)
  {
    // In C#, if the string was properly decoded from UTF-8 bytes by BytesToString,
    // we don't need additional decoding. Just return the text as-is.
    return text;
  }

  /// <summary>
  ///     Parse OSC 8 hyperlink sequence with parameters and URL.
  ///     Format: ESC ] 8 ; [params] ; [url] BEL/ST
  ///     where params can include id=<id> and other key=value pairs.
  /// </summary>
  /// <param name="raw">The raw OSC sequence</param>
  /// <param name="terminator">The terminator used</param>
  /// <param name="command">The OSC command number (8)</param>
  /// <param name="textParam">The text parameter after the command</param>
  /// <returns>Parsed OSC 8 message</returns>
  private XtermOscMessage ParseOsc8Hyperlink(string raw, string terminator, int command, string textParam)
  {
    // OSC 8 format: ESC ] 8 ; [params] ; [url] BEL/ST
    // The textParam contains: [params];[url]
    // We need to split on the second semicolon to separate params from URL

    int secondSemicolon = textParam.IndexOf(';');
    string parameters = "";
    string url = "";

    if (secondSemicolon >= 0)
    {
      parameters = textParam[..secondSemicolon];
      url = textParam[(secondSemicolon + 1)..];
    }
    else
    {
      // No second semicolon - treat entire textParam as URL (malformed but handle gracefully)
      url = textParam;
    }

    // Decode UTF-8 URL
    string decodedUrl = DecodeUtf8Text(url);

    return new XtermOscMessage
    {
      Type = "osc.hyperlink",
      Raw = raw,
      Terminator = terminator,
      Command = command,
      Payload = textParam, // Store original payload for debugging
      HyperlinkUrl = decodedUrl, // Store the extracted URL
      Implemented = true
    };
  }

  /// <summary>
  ///     Validate OSC parameter format and ranges.
  ///     Based on the TypeScript validateOscParameters function.
  /// </summary>
  private bool ValidateOscParameters(int commandNum, string textParam)
  {
    // Command number validation
    if (!IsValidCommandNumber(commandNum))
    {
      _logger.LogWarning("Invalid OSC command number: {CommandNum}", commandNum);
      return false;
    }

    // Text parameter validation
    if (textParam.Length > MaxOscPayloadLength)
    {
      _logger.LogWarning("OSC payload too long: {Length} > {Max}", textParam.Length, MaxOscPayloadLength);
      return false;
    }

    // Check for control characters that shouldn't be in titles
    for (int i = 0; i < textParam.Length; i++)
    {
      char c = textParam[i];
      int charCode = c;
      if (charCode < 0x20 && charCode != 0x09)
      {
        // Allow tab (0x09) but reject other control characters
        _logger.LogWarning("OSC parameter contains invalid control character at position {Position}: 0x{CharCode:X4}", i, charCode);
        return false;
      }
    }

    _logger.LogDebug("OSC parameters validated successfully: command={CommandNum}, textParam length={Length}", commandNum, textParam.Length);
    return true;
  }

  /// <summary>
  ///     Validates that a command number is within acceptable range.
  ///     Standard xterm OSC codes are 0-119, but private/application use
  ///     codes can go higher (e.g., 1010 for KSA JSON actions).
  /// </summary>
  private static bool IsValidCommandNumber(int commandNum)
  {
    return commandNum >= 0 && commandNum <= 9999;
  }

  /// <summary>
  ///     Converts a list of bytes to a string representation using UTF-8 decoding.
  /// </summary>
  private static string BytesToString(IEnumerable<byte> bytes)
  {
    try
    {
      byte[] byteArray = bytes.ToArray();
      return Encoding.UTF8.GetString(byteArray);
    }
    catch (Exception)
    {
      // Fallback to Latin1 if UTF-8 decoding fails
      return string.Concat(bytes.Select(b => (char)b));
    }
  }
}