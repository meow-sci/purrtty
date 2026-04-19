using caTTY.Core.Parsing;
using caTTY.Core.Rpc;
using caTTY.Core.Types;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace caTTY.Core.Tests.Unit.Parsing;

/// <summary>
/// Tests for RPC integration in the Parser class.
/// Validates that RPC sequences are properly detected and delegated without affecting core terminal functionality.
/// </summary>
[TestFixture]
[Category("Unit")]
public class ParserRpcIntegrationTests
{
    private TestParserHandlers _handlers = null!;
    private TestRpcHandler _rpcHandler = null!;
    private RpcSequenceDetector _rpcDetector = null!;
    private RpcSequenceParser _rpcParser = null!;
    private Parser _parser = null!;
    private ILogger _logger = null!;

    [SetUp]
    public void SetUp()
    {
        _handlers = new TestParserHandlers();
        _rpcHandler = new TestRpcHandler();
        _rpcDetector = new RpcSequenceDetector();
        _rpcParser = new RpcSequenceParser();
        _logger = new TestLogger();

        var options = new ParserOptions
        {
            Handlers = _handlers,
            Logger = _logger,
            RpcSequenceDetector = _rpcDetector,
            RpcSequenceParser = _rpcParser,
            RpcHandler = _rpcHandler
        };

        _parser = new Parser(options);
    }

    [Test]
    public void Parser_WithRpcSequence_DelegatesToRpcHandler()
    {
        // Arrange: RPC sequence ESC [ > 1001 ; 1 ; F
        byte[] rpcSequence = { 0x1B, 0x5B, 0x3E, 0x31, 0x30, 0x30, 0x31, 0x3B, 0x31, 0x3B, 0x46 };

        // Debug: Check if sequence is detected as RPC
        bool isRpc = _rpcDetector.IsRpcSequence(rpcSequence);
        var sequenceType = _rpcDetector.GetSequenceType(rpcSequence);
        Assert.That(isRpc, Is.True, "Sequence should be detected as RPC");
        Assert.That(sequenceType, Is.EqualTo(RpcSequenceType.Valid), "Sequence should be valid RPC");

        // Debug: Check if parser can parse the sequence
        bool canParse = _rpcParser.TryParseRpcSequence(rpcSequence, out var testMessage);
        Assert.That(canParse, Is.True, "Sequence should be parseable");
        Assert.That(testMessage, Is.Not.Null, "Parsed message should not be null");

        // Act
        _parser.PushBytes(rpcSequence);

        // Assert
        Assert.That(_rpcHandler.ReceivedMessages.Count, Is.EqualTo(1));
        Assert.That(_rpcHandler.ReceivedMessages[0].CommandId, Is.EqualTo(1001));
        Assert.That(_rpcHandler.ReceivedMessages[0].Version, Is.EqualTo(1));
        Assert.That(_rpcHandler.ReceivedMessages[0].CommandType, Is.EqualTo(RpcCommandType.FireAndForget));
        
        // Verify standard CSI handler was not called
        Assert.That(_handlers.CsiMessages.Count, Is.EqualTo(0));
    }

    [Test]
    public void Parser_WithStandardCsiSequence_UsesStandardHandler()
    {
        // Arrange: Standard CSI sequence ESC [ 2 J (clear screen)
        byte[] csiSequence = { 0x1B, 0x5B, 0x32, 0x4A };

        // Act
        _parser.PushBytes(csiSequence);

        // Assert
        Assert.That(_handlers.CsiMessages.Count, Is.EqualTo(1));
        Assert.That(_rpcHandler.ReceivedMessages.Count, Is.EqualTo(0));
    }

    [Test]
    public void Parser_WithRpcDisabled_UsesStandardHandler()
    {
        // Arrange: Disable RPC handling
        _rpcHandler.IsEnabled = false;
        
        // RPC sequence ESC [ > 1001 ; 1 ; F
        byte[] rpcSequence = { 0x1B, 0x5B, 0x3E, 0x31, 0x30, 0x30, 0x31, 0x3B, 0x31, 0x3B, 0x46 };

        // Act
        _parser.PushBytes(rpcSequence);

        // Assert: Should be handled as standard CSI sequence
        Assert.That(_handlers.CsiMessages.Count, Is.EqualTo(1));
        Assert.That(_rpcHandler.ReceivedMessages.Count, Is.EqualTo(0));
    }

    [Test]
    public void Parser_WithMalformedRpcSequence_HandlesMalformed()
    {
        // Arrange: Malformed RPC sequence ESC [ > 999 ; 1 ; F (command ID out of range)
        byte[] malformedSequence = { 0x1B, 0x5B, 0x3E, 0x39, 0x39, 0x39, 0x3B, 0x31, 0x3B, 0x46 };

        // Act
        _parser.PushBytes(malformedSequence);

        // Assert
        Assert.That(_rpcHandler.MalformedSequences.Count, Is.EqualTo(1));
        Assert.That(_rpcHandler.ReceivedMessages.Count, Is.EqualTo(0));
        Assert.That(_handlers.CsiMessages.Count, Is.EqualTo(0));
    }

    private class TestRpcHandler : IRpcHandler
    {
        public List<RpcMessage> ReceivedMessages { get; } = new();
        public List<(byte[] Sequence, RpcSequenceType Type)> MalformedSequences { get; } = new();
        public bool IsEnabled { get; set; } = true;

        public void HandleRpcMessage(RpcMessage message)
        {
            ReceivedMessages.Add(message);
        }

        public void HandleMalformedRpcSequence(ReadOnlySpan<byte> rawSequence, RpcSequenceType sequenceType)
        {
            MalformedSequences.Add((rawSequence.ToArray(), sequenceType));
        }
    }

    private class TestParserHandlers : IParserHandlers
    {
        public List<CsiMessage> CsiMessages { get; } = new();
        public List<SgrSequence> SgrSequences { get; } = new();

        public void HandleBell() { }
        public void HandleBackspace() { }
        public void HandleTab() { }
        public void HandleLineFeed() { }
        public void HandleFormFeed() { }
        public void HandleCarriageReturn() { }
        public void HandleShiftIn() { }
        public void HandleShiftOut() { }
        public void HandleNormalByte(int codePoint) { }
        public void HandleEsc(EscMessage message) { }
        public void HandleCsi(CsiMessage message) => CsiMessages.Add(message);
        public void HandleOsc(OscMessage message) { }
        public void HandleDcs(DcsMessage message) { }
        public void HandleSgr(SgrSequence sequence) => SgrSequences.Add(sequence);
        public void HandleXtermOsc(XtermOscMessage message) { }
    }

    private class TestLogger : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => false;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }
}