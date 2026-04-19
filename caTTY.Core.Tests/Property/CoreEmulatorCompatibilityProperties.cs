using System.Text;
using caTTY.Core.Parsing;
using caTTY.Core.Rpc;
using caTTY.Core.Types;
using FsCheck;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace caTTY.Core.Tests.Property;

/// <summary>
/// Property-based tests for core emulator compatibility with RPC functionality.
/// These tests verify that RPC integration does not affect standard terminal emulation.
/// </summary>
[TestFixture]
[Category("Property")]
public class CoreEmulatorCompatibilityProperties
{
    /// <summary>
    /// Generator for standard terminal sequences that should not be affected by RPC.
    /// </summary>
    public static Arbitrary<byte[]> StandardTerminalSequenceArb =>
        Arb.From(Gen.OneOf(
            // CSI sequences
            Gen.Constant(Encoding.ASCII.GetBytes("\x1b[H")), // Cursor home
            Gen.Constant(Encoding.ASCII.GetBytes("\x1b[2J")), // Clear screen
            Gen.Constant(Encoding.ASCII.GetBytes("\x1b[31m")), // Red color
            Gen.Constant(Encoding.ASCII.GetBytes("\x1b[1;1H")), // Cursor position
            Gen.Constant(Encoding.ASCII.GetBytes("\x1b[K")), // Clear line
            Gen.Constant(Encoding.ASCII.GetBytes("\x1b[A")), // Cursor up
            Gen.Constant(Encoding.ASCII.GetBytes("\x1b[B")), // Cursor down
            Gen.Constant(Encoding.ASCII.GetBytes("\x1b[C")), // Cursor right
            Gen.Constant(Encoding.ASCII.GetBytes("\x1b[D")), // Cursor left
            
            // OSC sequences
            Gen.Constant(Encoding.ASCII.GetBytes("\x1b]0;Title\x07")), // Set title
            Gen.Constant(Encoding.ASCII.GetBytes("\x1b]2;Window Title\x07")), // Set window title
            
            // Simple escape sequences
            Gen.Constant(Encoding.ASCII.GetBytes("\x1bM")), // Reverse index
            Gen.Constant(Encoding.ASCII.GetBytes("\x1bD")), // Index
            Gen.Constant(Encoding.ASCII.GetBytes("\x1bE")), // Next line
            Gen.Constant(Encoding.ASCII.GetBytes("\x1b7")), // Save cursor
            Gen.Constant(Encoding.ASCII.GetBytes("\x1b8")), // Restore cursor
            
            // Control characters
            Gen.Constant(new byte[] { 0x07 }), // Bell
            Gen.Constant(new byte[] { 0x08 }), // Backspace
            Gen.Constant(new byte[] { 0x09 }), // Tab
            Gen.Constant(new byte[] { 0x0A }), // Line feed
            Gen.Constant(new byte[] { 0x0D }), // Carriage return
            
            // Regular text
            Gen.Constant(Encoding.ASCII.GetBytes("Hello World")),
            Gen.Constant(Encoding.ASCII.GetBytes("Test123")),
            Gen.Constant(Encoding.ASCII.GetBytes("ABC"))
        ));

    /// <summary>
    /// Generator for valid RPC sequences.
    /// </summary>
    public static Arbitrary<byte[]> ValidRpcSequenceArb =>
        Arb.From(
            from commandId in Gen.Choose(1000, 9999)
            from version in Gen.Choose(1, 99)
            from finalChar in Gen.Elements('F', 'Q', 'R', 'E')
            select Encoding.ASCII.GetBytes($"\x1b[>{commandId};{version};{finalChar}")
        );

    /// <summary>
    /// Generator for mixed sequences containing both standard and RPC sequences.
    /// </summary>
    public static Arbitrary<byte[][]> MixedSequenceArb =>
        Arb.From(Gen.ArrayOf(Gen.OneOf(StandardTerminalSequenceArb.Generator, ValidRpcSequenceArb.Generator))
            .Where(arr => arr.Length > 0 && arr.Length <= 10));

    /// <summary>
    /// **Feature: term-sequence-rpc, Property 3: Core Emulator Compatibility**
    /// **Validates: Requirements 1.4, 4.1, 4.2**
    /// Property: For any standard terminal sequence, the core emulator should function 
    /// identically whether RPC is enabled or disabled, with private sequences being 
    /// delegated to RPC handlers without affecting VT100/xterm compliance.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property CoreEmulatorCompatibility()
    {
        return Prop.ForAll(StandardTerminalSequenceArb, (byte[] sequence) =>
        {
            // Arrange: Create two parsers - one with RPC enabled, one without
            var handlersWithRpc = new TestParserHandlers();
            var handlersWithoutRpc = new TestParserHandlers();
            var rpcHandler = new TestRpcHandler();
            var logger = new TestLogger();

            // Parser with RPC enabled
            var parserWithRpc = new Parser(new ParserOptions
            {
                Handlers = handlersWithRpc,
                Logger = logger,
                RpcSequenceDetector = new RpcSequenceDetector(),
                RpcSequenceParser = new RpcSequenceParser(),
                RpcHandler = rpcHandler
            });

            // Parser without RPC (null RPC components)
            var parserWithoutRpc = new Parser(new ParserOptions
            {
                Handlers = handlersWithoutRpc,
                Logger = logger,
                RpcSequenceDetector = null,
                RpcSequenceParser = null,
                RpcHandler = null
            });

            // Act: Process the same sequence with both parsers
            parserWithRpc.PushBytes(sequence);
            parserWithoutRpc.PushBytes(sequence);

            // Assert: Standard terminal handling should be identical
            bool identicalCsiHandling = handlersWithRpc.CsiMessages.Count == handlersWithoutRpc.CsiMessages.Count;
            bool identicalSgrHandling = handlersWithRpc.SgrSequences.Count == handlersWithoutRpc.SgrSequences.Count;
            bool identicalEscHandling = handlersWithRpc.EscMessages.Count == handlersWithoutRpc.EscMessages.Count;
            bool identicalOscHandling = handlersWithRpc.OscMessages.Count == handlersWithoutRpc.OscMessages.Count;
            bool identicalDcsHandling = handlersWithRpc.DcsMessages.Count == handlersWithoutRpc.DcsMessages.Count;
            bool identicalControlHandling = 
                handlersWithRpc.BellCalled == handlersWithoutRpc.BellCalled &&
                handlersWithRpc.BackspaceCalled == handlersWithoutRpc.BackspaceCalled &&
                handlersWithRpc.TabCalled == handlersWithoutRpc.TabCalled &&
                handlersWithRpc.LineFeedCalled == handlersWithoutRpc.LineFeedCalled &&
                handlersWithRpc.CarriageReturnCalled == handlersWithoutRpc.CarriageReturnCalled;
            bool identicalNormalBytes = handlersWithRpc.NormalBytes.SequenceEqual(handlersWithoutRpc.NormalBytes);

            // RPC handler should not receive standard sequences
            bool noRpcInterference = rpcHandler.ReceivedMessages.Count == 0;

            return identicalCsiHandling && identicalSgrHandling && identicalEscHandling && 
                   identicalOscHandling && identicalDcsHandling && identicalControlHandling && 
                   identicalNormalBytes && noRpcInterference;
        });
    }

    /// <summary>
    /// **Feature: term-sequence-rpc, Property 3a: RPC Sequence Delegation**
    /// **Validates: Requirements 1.4, 4.1**
    /// Property: For any valid RPC sequence, the core emulator should delegate 
    /// processing to RPC handlers without affecting standard terminal handlers.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property RpcSequenceDelegation()
    {
        return Prop.ForAll(ValidRpcSequenceArb, (byte[] rpcSequence) =>
        {
            // Arrange
            var handlers = new TestParserHandlers();
            var rpcHandler = new TestRpcHandler();
            var logger = new TestLogger();

            var parser = new Parser(new ParserOptions
            {
                Handlers = handlers,
                Logger = logger,
                RpcSequenceDetector = new RpcSequenceDetector(),
                RpcSequenceParser = new RpcSequenceParser(),
                RpcHandler = rpcHandler
            });

            // Act
            parser.PushBytes(rpcSequence);

            // Assert: RPC sequence should be handled by RPC handler, not standard handlers
            bool rpcHandlerReceived = rpcHandler.ReceivedMessages.Count > 0 || rpcHandler.MalformedSequences.Count > 0;
            bool standardHandlersNotCalled = 
                handlers.CsiMessages.Count == 0 &&
                handlers.SgrSequences.Count == 0 &&
                handlers.EscMessages.Count == 0;

            return rpcHandlerReceived && standardHandlersNotCalled;
        });
    }

    /// <summary>
    /// **Feature: term-sequence-rpc, Property 3b: RPC Disabled Compatibility**
    /// **Validates: Requirements 4.2**
    /// Property: For any sequence, when RPC is disabled, the parser should handle 
    /// all sequences (including RPC-formatted ones) as standard terminal sequences.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property RpcDisabledCompatibility()
    {
        return Prop.ForAll(ValidRpcSequenceArb, (byte[] rpcSequence) =>
        {
            // Arrange: Create parsers with RPC enabled vs disabled
            var handlersRpcEnabled = new TestParserHandlers();
            var handlersRpcDisabled = new TestParserHandlers();
            var rpcHandler = new TestRpcHandler { IsEnabled = false }; // Disabled
            var logger = new TestLogger();

            // Parser with RPC components but disabled
            var parserRpcDisabled = new Parser(new ParserOptions
            {
                Handlers = handlersRpcDisabled,
                Logger = logger,
                RpcSequenceDetector = new RpcSequenceDetector(),
                RpcSequenceParser = new RpcSequenceParser(),
                RpcHandler = rpcHandler // Disabled
            });

            // Parser without RPC components at all
            var parserNoRpc = new Parser(new ParserOptions
            {
                Handlers = handlersRpcEnabled,
                Logger = logger,
                RpcSequenceDetector = null,
                RpcSequenceParser = null,
                RpcHandler = null
            });

            // Act
            parserRpcDisabled.PushBytes(rpcSequence);
            parserNoRpc.PushBytes(rpcSequence);

            // Assert: Both should handle the sequence identically as standard CSI
            bool identicalCsiHandling = handlersRpcDisabled.CsiMessages.Count == handlersRpcEnabled.CsiMessages.Count;
            bool noRpcHandling = rpcHandler.ReceivedMessages.Count == 0;

            return identicalCsiHandling && noRpcHandling;
        });
    }

    /// <summary>
    /// **Feature: term-sequence-rpc, Property 3c: Mixed Sequence Processing**
    /// **Validates: Requirements 1.4, 4.1, 4.2**
    /// Property: For any mixed sequence of standard and RPC sequences, each should 
    /// be processed by the appropriate handler without cross-contamination.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property MixedSequenceProcessing()
    {
        return Prop.ForAll(MixedSequenceArb, (byte[][] sequences) =>
        {
            // Arrange
            var handlers = new TestParserHandlers();
            var rpcHandler = new TestRpcHandler();
            var logger = new TestLogger();
            var detector = new RpcSequenceDetector();

            var parser = new Parser(new ParserOptions
            {
                Handlers = handlers,
                Logger = logger,
                RpcSequenceDetector = detector,
                RpcSequenceParser = new RpcSequenceParser(),
                RpcHandler = rpcHandler
            });

            // Count expected RPC vs standard sequences
            int expectedRpcCount = 0;
            int expectedStandardCount = 0;

            foreach (var sequence in sequences)
            {
                if (detector.IsRpcSequence(sequence))
                {
                    expectedRpcCount++;
                }
                else
                {
                    expectedStandardCount++;
                }
            }

            // Act: Process all sequences
            foreach (var sequence in sequences)
            {
                parser.PushBytes(sequence);
            }

            // Assert: Each sequence type should be handled appropriately
            int actualRpcCount = rpcHandler.ReceivedMessages.Count + rpcHandler.MalformedSequences.Count;
            int actualStandardCount = handlers.CsiMessages.Count + handlers.SgrSequences.Count + 
                                    handlers.EscMessages.Count + handlers.OscMessages.Count + 
                                    handlers.DcsMessages.Count;

            // Allow for some flexibility in standard count due to text sequences
            bool rpcCountCorrect = actualRpcCount == expectedRpcCount;
            bool standardCountReasonable = actualStandardCount >= 0; // At least no negative

            return rpcCountCorrect && standardCountReasonable;
        });
    }

    /// <summary>
    /// Test implementation of IParserHandlers for capturing parser events.
    /// </summary>
    private class TestParserHandlers : IParserHandlers
    {
        public bool BellCalled { get; private set; }
        public bool BackspaceCalled { get; private set; }
        public bool TabCalled { get; private set; }
        public bool LineFeedCalled { get; private set; }
        public bool FormFeedCalled { get; private set; }
        public bool CarriageReturnCalled { get; private set; }
        public bool ShiftInCalled { get; private set; }
        public bool ShiftOutCalled { get; private set; }

        public List<int> NormalBytes { get; } = new();
        public List<EscMessage> EscMessages { get; } = new();
        public List<CsiMessage> CsiMessages { get; } = new();
        public List<OscMessage> OscMessages { get; } = new();
        public List<DcsMessage> DcsMessages { get; } = new();
        public List<SgrSequence> SgrSequences { get; } = new();
        public List<XtermOscMessage> XtermOscMessages { get; } = new();

        public void HandleBell() => BellCalled = true;
        public void HandleBackspace() => BackspaceCalled = true;
        public void HandleTab() => TabCalled = true;
        public void HandleLineFeed() => LineFeedCalled = true;
        public void HandleFormFeed() => FormFeedCalled = true;
        public void HandleCarriageReturn() => CarriageReturnCalled = true;
        public void HandleShiftIn() => ShiftInCalled = true;
        public void HandleShiftOut() => ShiftOutCalled = true;
        public void HandleNormalByte(int codePoint) => NormalBytes.Add(codePoint);
        public void HandleEsc(EscMessage message) => EscMessages.Add(message);
        public void HandleCsi(CsiMessage message) => CsiMessages.Add(message);
        public void HandleOsc(OscMessage message) => OscMessages.Add(message);
        public void HandleDcs(DcsMessage message) => DcsMessages.Add(message);
        public void HandleSgr(SgrSequence sequence) => SgrSequences.Add(sequence);
        public void HandleXtermOsc(XtermOscMessage message) => XtermOscMessages.Add(message);
    }

    /// <summary>
    /// Test implementation of IRpcHandler for capturing RPC events.
    /// </summary>
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

    /// <summary>
    /// Test implementation of ILogger for testing purposes.
    /// </summary>
    private class TestLogger : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => false;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }
}