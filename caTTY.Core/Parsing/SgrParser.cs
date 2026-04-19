using System.Text;
using caTTY.Core.Parsing.Sgr;
using caTTY.Core.Types;
using Microsoft.Extensions.Logging;

namespace caTTY.Core.Parsing;

/// <summary>
///     SGR (Select Graphic Rendition) sequence parser.
///     Handles parsing of CSI ... m sequences and parameter processing.
///     Based on the TypeScript ParseSgr implementation with identical behavior.
/// </summary>
public class SgrParser : ISgrParser
{
    private readonly ILogger _logger;
    private readonly ICursorPositionProvider? _cursorPositionProvider;
    private readonly SgrParamTokenizer _tokenizer;
    private readonly SgrColorParsers _colorParsers;
    private readonly SgrAttributeApplier _attributeApplier;
    private readonly SgrMessageFactory _messageFactory;

    /// <summary>
    ///     Creates a new SGR parser.
    /// </summary>
    /// <param name="logger">Logger for diagnostic messages</param>
    /// <param name="cursorPositionProvider">Optional cursor position provider for tracing</param>
    public SgrParser(ILogger logger, ICursorPositionProvider? cursorPositionProvider = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _cursorPositionProvider = cursorPositionProvider;
        _tokenizer = new SgrParamTokenizer();
        _colorParsers = new SgrColorParsers();
        _attributeApplier = new SgrAttributeApplier();
        _messageFactory = new SgrMessageFactory(_colorParsers);
    }

    /// <summary>
    ///     Creates a new SGR parser.
    /// </summary>
    /// <param name="logger">Logger for diagnostic messages</param>
    public SgrParser(ILogger? logger = null)
    {
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
        _tokenizer = new SgrParamTokenizer();
        _colorParsers = new SgrColorParsers();
        _attributeApplier = new SgrAttributeApplier();
        _messageFactory = new SgrMessageFactory(_colorParsers);
    }

    /// <summary>
    ///     Parses an SGR sequence from CSI parameters.
    /// </summary>
    /// <param name="escapeSequence">The raw escape sequence bytes</param>
    /// <param name="raw">The raw sequence string</param>
    /// <returns>The parsed SGR sequence with individual messages</returns>
    public SgrSequence ParseSgrSequence(byte[] escapeSequence, string raw)
    {
        var parseResult = ParseSgrParamsAndSeparators(raw);
        var messages = ParseSgr(parseResult.Params, parseResult.Separators, parseResult.Prefix, parseResult.Intermediate);
        
        bool allImplemented = messages.All(m => m.Implemented);
        
        return new SgrSequence
        {
            Type = "sgr",
            Implemented = allImplemented,
            Raw = raw,
            Messages = messages.ToArray()
        };
    }

    /// <summary>
    ///     Parses SGR parameters with support for both semicolon and colon separators.
    /// </summary>
    /// <param name="parameterString">The parameter string to parse</param>
    /// <param name="parameters">The parsed parameters</param>
    /// <returns>True if parsing was successful</returns>
    public bool TryParseParameters(ReadOnlySpan<char> parameterString, out int[] parameters)
    {
        return _tokenizer.TryParseParameters(parameterString, out parameters);
    }

    /// <summary>
    ///     Applies SGR attributes to the current state.
    /// </summary>
    /// <param name="current">The current SGR attributes</param>
    /// <param name="messages">The SGR messages to apply</param>
    /// <returns>The updated SGR attributes</returns>
    public SgrAttributes ApplyAttributes(SgrAttributes current, ReadOnlySpan<SgrMessage> messages)
    {
        return _attributeApplier.ApplyAttributes(current, messages);
    }

    /// <summary>
    ///     Parses SGR parameters and separators from raw sequence.
    /// </summary>
    private SgrParseResult ParseSgrParamsAndSeparators(string raw)
    {
        return _tokenizer.ParseSgrParamsAndSeparators(raw);
    }

    /// <summary>
    ///     Parses SGR parameters into individual messages.
    /// </summary>
    private List<SgrMessage> ParseSgr(int[] parameters, string[] separators, string? prefix, string? intermediate)
    {
        return _messageFactory.BuildMessages(parameters, separators, prefix, intermediate);
    }

}