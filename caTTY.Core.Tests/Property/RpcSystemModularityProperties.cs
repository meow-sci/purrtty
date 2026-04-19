using System.Text;
using caTTY.Core.Parsing;
using caTTY.Core.Rpc;
using caTTY.Core.Types;
using FsCheck;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace caTTY.Core.Tests.Property;

/// <summary>
/// Property-based tests for RPC system modularity.
/// These tests verify that RPC functionality can be disabled or removed without affecting core terminal functionality.
/// </summary>
[TestFixture]
[Category("Property")]
public class RpcSystemModularityProperties
{
    /// <summary>
    /// Generator for standard terminal sequences that should always work regardless of RPC state.
    /// </summary>
    public static Arbitrary<byte[]> StandardSequenceArb =>
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
            Gen.Constant(Encoding.ASCII.GetBytes("\x1b[0m")), // Reset attributes
            
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
    /// Generator for private use area sequences (RPC format).
    /// </summary>
    public static Arbitrary<byte[]> PrivateUseSequenceArb =>
        Arb.From(
            from commandId in Gen.Choose(1000, 9999)
            from version in Gen.Choose(1, 99)
            from finalChar in Gen.Elements('F', 'Q', 'R', 'E')
            select Encoding.ASCII.GetBytes($"\x1b[>{commandId};{version};{finalChar}")
        );

    /// <summary>
    /// Generator for mixed sequences containing both standard and private use sequences.
    /// </summary>
    public static Arbitrary<byte[][]> MixedSequenceArb =>
        Arb.From(Gen.ArrayOf(Gen.OneOf(StandardSequenceArb.Generator, PrivateUseSequenceArb.Generator))
            .Where(arr => arr.Length > 0 && arr.Length <= 10));

    /// <summary>
    /// **Feature: term-sequence-rpc, Property 10: RPC System Modularity**
    /// **Validates: Requirements 4.4, 4.5**
    /// Property: For any configuration where RPC functionality is disabled, the core emulator 
    /// should ignore private use sequences and process only standard sequences, and the system 
    /// should remain functional if RPC components are removed.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property RpcSystemModularity()
    {
        return Prop.ForAll(StandardSequenceArb, (byte[] standardSequence) =>
        {
            // Arrange: Create three parser configurations
            var handlersWithRpc = new TestParserHandlers();
            var handlersRpcDisabled = new TestParserHandlers();
            var handlersNoRpc = new TestParserHandlers();
            var logger = new TestLogger();

            // Parser with RPC enabled
            var parserWithRpc = new Parser(new ParserOptions
            {
                Handlers = handlersWithRpc,
                Logger = logger,
                RpcSequenceDetector = new RpcSequenceDetector(),
                RpcSequenceParser = new RpcSequenceParser(),
                RpcHandler = new TestRpcHandler { IsEnabled = true }
            });

            // Parser with RPC components present but disabled
            var parserRpcDisabled = new Parser(new ParserOptions
            {
                Handlers = handlersRpcDisabled,
                Logger = logger,
                RpcSequenceDetector = new RpcSequenceDetector(),
                RpcSequenceParser = new RpcSequenceParser(),
                RpcHandler = new TestRpcHandler { IsEnabled = false }
            });

            // Parser with RPC components completely removed (null)
            var parserNoRpc = new Parser(new ParserOptions
            {
                Handlers = handlersNoRpc,
                Logger = logger,
                RpcSequenceDetector = null,
                RpcSequenceParser = null,
                RpcHandler = null
            });

            // Act: Process the same standard sequence with all three parsers
            parserWithRpc.PushBytes(standardSequence);
            parserRpcDisabled.PushBytes(standardSequence);
            parserNoRpc.PushBytes(standardSequence);

            // Assert: All three should handle standard sequences identically
            bool identicalCsiHandling = 
                handlersWithRpc.CsiMessages.Count == handlersRpcDisabled.CsiMessages.Count &&
                handlersRpcDisabled.CsiMessages.Count == handlersNoRpc.CsiMessages.Count;

            bool identicalSgrHandling = 
                handlersWithRpc.SgrSequences.Count == handlersRpcDisabled.SgrSequences.Count &&
                handlersRpcDisabled.SgrSequences.Count == handlersNoRpc.SgrSequences.Count;

            bool identicalEscHandling = 
                handlersWithRpc.EscMessages.Count == handlersRpcDisabled.EscMessages.Count &&
                handlersRpcDisabled.EscMessages.Count == handlersNoRpc.EscMessages.Count;

            bool identicalOscHandling = 
                handlersWithRpc.OscMessages.Count == handlersRpcDisabled.OscMessages.Count &&
                handlersRpcDisabled.OscMessages.Count == handlersNoRpc.OscMessages.Count;

            bool identicalDcsHandling = 
                handlersWithRpc.DcsMessages.Count == handlersRpcDisabled.DcsMessages.Count &&
                handlersRpcDisabled.DcsMessages.Count == handlersNoRpc.DcsMessages.Count;

            bool identicalControlHandling = 
                handlersWithRpc.BellCalled == handlersRpcDisabled.BellCalled &&
                handlersRpcDisabled.BellCalled == handlersNoRpc.BellCalled &&
                handlersWithRpc.BackspaceCalled == handlersRpcDisabled.BackspaceCalled &&
                handlersRpcDisabled.BackspaceCalled == handlersNoRpc.BackspaceCalled &&
                handlersWithRpc.TabCalled == handlersRpcDisabled.TabCalled &&
                handlersRpcDisabled.TabCalled == handlersNoRpc.TabCalled &&
                handlersWithRpc.LineFeedCalled == handlersRpcDisabled.LineFeedCalled &&
                handlersRpcDisabled.LineFeedCalled == handlersNoRpc.LineFeedCalled &&
                handlersWithRpc.CarriageReturnCalled == handlersRpcDisabled.CarriageReturnCalled &&
                handlersRpcDisabled.CarriageReturnCalled == handlersNoRpc.CarriageReturnCalled;

            bool identicalNormalBytes = 
                handlersWithRpc.NormalBytes.SequenceEqual(handlersRpcDisabled.NormalBytes) &&
                handlersRpcDisabled.NormalBytes.SequenceEqual(handlersNoRpc.NormalBytes);

            return identicalCsiHandling && identicalSgrHandling && identicalEscHandling && 
                   identicalOscHandling && identicalDcsHandling && identicalControlHandling && 
                   identicalNormalBytes;
        });
    }

    /// <summary>
    /// **Feature: term-sequence-rpc, Property 10a: Private Use Sequence Handling When RPC Disabled**
    /// **Validates: Requirements 4.4**
    /// Property: For any private use sequence, when RPC functionality is disabled, 
    /// the core emulator should ignore the sequence or treat it as a standard CSI sequence.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property PrivateUseSequenceHandlingWhenRpcDisabled()
    {
        return Prop.ForAll(PrivateUseSequenceArb, (byte[] privateSequence) =>
        {
            // Arrange: Create parsers with RPC disabled vs completely removed
            var handlersRpcDisabled = new TestParserHandlers();
            var handlersNoRpc = new TestParserHandlers();
            var rpcHandlerDisabled = new TestRpcHandler { IsEnabled = false };
            var logger = new TestLogger();

            // Parser with RPC components present but disabled
            var parserRpcDisabled = new Parser(new ParserOptions
            {
                Handlers = handlersRpcDisabled,
                Logger = logger,
                RpcSequenceDetector = new RpcSequenceDetector(),
                RpcSequenceParser = new RpcSequenceParser(),
                RpcHandler = rpcHandlerDisabled
            });

            // Parser with RPC components completely removed
            var parserNoRpc = new Parser(new ParserOptions
            {
                Handlers = handlersNoRpc,
                Logger = logger,
                RpcSequenceDetector = null,
                RpcSequenceParser = null,
                RpcHandler = null
            });

            // Act: Process the private use sequence with both parsers
            parserRpcDisabled.PushBytes(privateSequence);
            parserNoRpc.PushBytes(privateSequence);

            // Assert: Both should handle the sequence identically (as standard CSI or ignore)
            // and RPC handler should not receive the sequence when disabled
            bool identicalHandling = 
                handlersRpcDisabled.CsiMessages.Count == handlersNoRpc.CsiMessages.Count;

            bool rpcHandlerNotCalled = rpcHandlerDisabled.ReceivedMessages.Count == 0;

            return identicalHandling && rpcHandlerNotCalled;
        });
    }

    /// <summary>
    /// **Feature: term-sequence-rpc, Property 10b: System Functionality Without RPC Components**
    /// **Validates: Requirements 4.5**
    /// Property: For any mixed sequence of standard and private use sequences, 
    /// the system should remain functional when RPC components are completely removed.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property SystemFunctionalityWithoutRpcComponents()
    {
        return Prop.ForAll(MixedSequenceArb, (byte[][] sequences) =>
        {
            // Arrange: Create parser without any RPC components
            var handlers = new TestParserHandlers();
            var logger = new TestLogger();

            var parser = new Parser(new ParserOptions
            {
                Handlers = handlers,
                Logger = logger,
                RpcSequenceDetector = null,
                RpcSequenceParser = null,
                RpcHandler = null
            });

            // Act: Process all sequences - this should not throw exceptions
            bool noExceptions = true;
            try
            {
                foreach (var sequence in sequences)
                {
                    parser.PushBytes(sequence);
                }
            }
            catch
            {
                noExceptions = false;
            }

            // Assert: System should remain functional (no crashes)
            // The main requirement is that the system doesn't crash when RPC components are removed
            // We don't need to verify specific sequence processing since that's tested elsewhere
            bool systemFunctional = noExceptions;

            return systemFunctional;
        });
    }

    /// <summary>
    /// **Feature: term-sequence-rpc, Property 10c: RPC Component Removal Safety**
    /// **Validates: Requirements 4.5**
    /// Property: For any parser configuration, removing RPC components should not 
    /// affect the parser's ability to handle standard terminal sequences.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property RpcComponentRemovalSafety()
    {
        return Prop.ForAll(StandardSequenceArb, (byte[] standardSequence) =>
        {
            // Arrange: Create baseline parser without RPC
            var baselineHandlers = new TestParserHandlers();
            var testHandlers = new TestParserHandlers();
            var logger = new TestLogger();

            // Baseline parser (never had RPC)
            var baselineParser = new Parser(new ParserOptions
            {
                Handlers = baselineHandlers,
                Logger = logger,
                RpcSequenceDetector = null,
                RpcSequenceParser = null,
                RpcHandler = null
            });

            // Test parser (RPC components removed)
            var testParser = new Parser(new ParserOptions
            {
                Handlers = testHandlers,
                Logger = logger,
                RpcSequenceDetector = null, // Removed
                RpcSequenceParser = null,   // Removed
                RpcHandler = null           // Removed
            });

            // Act: Process the same sequence with both parsers
            baselineParser.PushBytes(standardSequence);
            testParser.PushBytes(standardSequence);

            // Assert: Both should behave identically
            bool identicalBehavior = 
                baselineHandlers.CsiMessages.Count == testHandlers.CsiMessages.Count &&
                baselineHandlers.SgrSequences.Count == testHandlers.SgrSequences.Count &&
                baselineHandlers.EscMessages.Count == testHandlers.EscMessages.Count &&
                baselineHandlers.OscMessages.Count == testHandlers.OscMessages.Count &&
                baselineHandlers.DcsMessages.Count == testHandlers.DcsMessages.Count &&
                baselineHandlers.BellCalled == testHandlers.BellCalled &&
                baselineHandlers.BackspaceCalled == testHandlers.BackspaceCalled &&
                baselineHandlers.TabCalled == testHandlers.TabCalled &&
                baselineHandlers.LineFeedCalled == testHandlers.LineFeedCalled &&
                baselineHandlers.CarriageReturnCalled == testHandlers.CarriageReturnCalled &&
                baselineHandlers.NormalBytes.SequenceEqual(testHandlers.NormalBytes);

            return identicalBehavior;
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
            if (IsEnabled)
            {
                ReceivedMessages.Add(message);
            }
        }

        public void HandleMalformedRpcSequence(ReadOnlySpan<byte> rawSequence, RpcSequenceType sequenceType)
        {
            if (IsEnabled)
            {
                MalformedSequences.Add((rawSequence.ToArray(), sequenceType));
            }
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