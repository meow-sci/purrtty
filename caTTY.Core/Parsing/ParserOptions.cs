using caTTY.Core.Types;
using caTTY.Core.Rpc;
using Microsoft.Extensions.Logging;

namespace caTTY.Core.Parsing;

/// <summary>
///     Configuration options for the escape sequence parser.
///     Based on the TypeScript ParserOptions interface.
/// </summary>
public class ParserOptions
{
    /// <summary>
    ///     Logger instance for parser debugging and warnings.
    /// </summary>
    public ILogger Logger { get; set; } = null!;

    /// <summary>
    ///     Whether to emit normal bytes during escape sequences (undefined behavior).
    ///     Default is false.
    /// </summary>
    public bool EmitNormalBytesDuringEscapeSequence { get; set; } = false;

    /// <summary>
    ///     Whether to process C0 controls during escape sequences (common terminal behavior).
    ///     Default is true.
    /// </summary>
    public bool ProcessC0ControlsDuringEscapeSequence { get; set; } = true;

    /// <summary>
    ///     Handlers for processing parsed sequences and control characters.
    /// </summary>
    public IParserHandlers Handlers { get; set; } = null!;

    /// <summary>
    ///     UTF-8 decoder for multi-byte sequence handling.
    ///     If null, a default Utf8Decoder will be created.
    /// </summary>
    public IUtf8Decoder? Utf8Decoder { get; set; }

    /// <summary>
    ///     CSI parser for CSI sequence handling.
    ///     If null, a default CsiParser will be created.
    /// </summary>
    public ICsiParser? CsiParser { get; set; }

    /// <summary>
    ///     ESC parser for ESC sequence handling.
    ///     If null, a default EscParser will be created.
    /// </summary>
    public IEscParser? EscParser { get; set; }

    /// <summary>
    ///     DCS parser for DCS sequence handling.
    ///     If null, a default DcsParser will be created.
    /// </summary>
    public IDcsParser? DcsParser { get; set; }

    /// <summary>
    ///     OSC parser for OSC sequence handling.
    ///     If null, a default OscParser will be created.
    /// </summary>
    public IOscParser? OscParser { get; set; }

    /// <summary>
    ///     SGR parser for SGR sequence handling.
    ///     If null, a default SgrParser will be created.
    /// </summary>
    public ISgrParser? SgrParser { get; set; }

    /// <summary>
    ///     Cursor position provider for tracing purposes.
    ///     If null, parsers will trace without position information.
    /// </summary>
    public ICursorPositionProvider? CursorPositionProvider { get; set; }

    /// <summary>
    ///     RPC sequence detector for identifying private use area RPC sequences.
    ///     If null, RPC detection will be disabled.
    /// </summary>
    public IRpcSequenceDetector? RpcSequenceDetector { get; set; }

    /// <summary>
    ///     RPC sequence parser for parsing private use area RPC sequences.
    ///     If null, RPC parsing will be disabled.
    /// </summary>
    public IRpcSequenceParser? RpcSequenceParser { get; set; }

    /// <summary>
    ///     RPC handler for processing parsed RPC messages.
    ///     If null, RPC handling will be disabled.
    /// </summary>
    public IRpcHandler? RpcHandler { get; set; }
}
