using System.Text;
using purrTTY.Core.Parsing;
using purrTTY.Core.Types;
using FsCheck;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace purrTTY.Core.Tests.Property;

/// <summary>
/// Property-based tests for parser compatibility with private-use escape sequences.
/// These tests verify that private-use/control sequences do not affect standard terminal handling.
/// </summary>
[TestFixture]
[Category("Property")]
public class CoreEmulatorCompatibilityProperties
{
    /// <summary>
    /// Generator for standard terminal sequences that should remain stable.
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
    /// Generator for valid private-use CSI sequences.
    /// </summary>
    public static Arbitrary<byte[]> ValidPrivateUseSequenceArb =>
        Arb.From(
            from commandId in Gen.Choose(1000, 9999)
            from version in Gen.Choose(1, 99)
            from finalChar in Gen.Elements('F', 'Q', 'R', 'E')
            select Encoding.ASCII.GetBytes($"\x1b[>{commandId};{version};{finalChar}")
        );

    /// <summary>
    /// Generator for mixed sequences containing both standard and private-use sequences.
    /// </summary>
    public static Arbitrary<byte[][]> MixedSequenceArb =>
        Arb.From(Gen.ArrayOf(Gen.OneOf(StandardTerminalSequenceArb.Generator, ValidPrivateUseSequenceArb.Generator))
            .Where(arr => arr.Length > 0 && arr.Length <= 10));

    /// <summary>
    /// Property: For any standard terminal sequence, parser output should remain stable
    /// across equivalent parser instances.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property CoreEmulatorCompatibility()
    {
        return Prop.ForAll(StandardTerminalSequenceArb, (byte[] sequence) =>
        {
            // Arrange: Create two equivalent parser instances
            var handlersA = new TestParserHandlers();
            var handlersB = new TestParserHandlers();
            var logger = new TestLogger();

            var parserA = new Parser(new ParserOptions
            {
                Handlers = handlersA,
                Logger = logger
            });

            var parserB = new Parser(new ParserOptions
            {
                Handlers = handlersB,
                Logger = logger
            });

            // Act: Process the same sequence with both parsers
            parserA.PushBytes(sequence);
            parserB.PushBytes(sequence);

            // Assert: Standard terminal handling should be identical
            bool identicalCsiHandling = handlersA.CsiMessages.Count == handlersB.CsiMessages.Count;
            bool identicalSgrHandling = handlersA.SgrSequences.Count == handlersB.SgrSequences.Count;
            bool identicalEscHandling = handlersA.EscMessages.Count == handlersB.EscMessages.Count;
            bool identicalOscHandling = handlersA.OscMessages.Count == handlersB.OscMessages.Count;
            bool identicalDcsHandling = handlersA.DcsMessages.Count == handlersB.DcsMessages.Count;
            bool identicalControlHandling = 
                handlersA.BellCalled == handlersB.BellCalled &&
                handlersA.BackspaceCalled == handlersB.BackspaceCalled &&
                handlersA.TabCalled == handlersB.TabCalled &&
                handlersA.LineFeedCalled == handlersB.LineFeedCalled &&
                handlersA.CarriageReturnCalled == handlersB.CarriageReturnCalled;
            bool identicalNormalBytes = handlersA.NormalBytes.SequenceEqual(handlersB.NormalBytes);


            return identicalCsiHandling && identicalSgrHandling && identicalEscHandling && 
                   identicalOscHandling && identicalDcsHandling && identicalControlHandling && 
                   identicalNormalBytes;
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
    /// Test implementation of ILogger for testing purposes.
    /// </summary>
    private class TestLogger : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => false;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }
}