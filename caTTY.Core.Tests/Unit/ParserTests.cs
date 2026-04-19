using System.Text;
using caTTY.Core.Parsing;
using caTTY.Core.Types;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace caTTY.Core.Tests.Unit;

/// <summary>
///     Unit tests for the escape sequence parser state machine.
/// </summary>
[TestFixture]
public class ParserTests
{
    [SetUp]
    public void SetUp()
    {
        _handlers = new TestParserHandlers();
        _logger = new TestLogger();

        var options = new ParserOptions
        {
            Logger = _logger,
            Handlers = _handlers,
            EmitNormalBytesDuringEscapeSequence = false,
            ProcessC0ControlsDuringEscapeSequence = true
        };

        _parser = new Parser(options);
    }

    private TestParserHandlers _handlers = null!;
    private Parser _parser = null!;
    private ILogger _logger = null!;

    [Test]
    public void PushByte_NormalAsciiCharacter_CallsHandleNormalByte()
    {
        // Arrange
        byte testByte = (byte)'A';

        // Act
        _parser.PushByte(testByte);

        // Assert
        Assert.That(_handlers.NormalBytes, Has.Count.EqualTo(1));
        Assert.That(_handlers.NormalBytes[0], Is.EqualTo(testByte));
    }

    [Test]
    public void PushByte_BellCharacter_CallsHandleBell()
    {
        // Arrange
        byte bellByte = 0x07;

        // Act
        _parser.PushByte(bellByte);

        // Assert
        Assert.That(_handlers.BellCalled, Is.True);
    }

    [Test]
    public void PushByte_TabCharacter_CallsHandleTab()
    {
        // Arrange
        byte tabByte = 0x09;

        // Act
        _parser.PushByte(tabByte);

        // Assert
        Assert.That(_handlers.TabCalled, Is.True);
    }

    [Test]
    public void PushByte_EscapeSequence_CallsHandleEsc()
    {
        // Arrange - ESC 7 (save cursor)
        byte[] escapeSequence = { 0x1b, 0x37 };

        // Act
        _parser.PushBytes(escapeSequence);

        // Assert
        Assert.That(_handlers.EscMessages, Has.Count.EqualTo(1));
        Assert.That(_handlers.EscMessages[0].Type, Is.EqualTo("esc.saveCursor"));
        Assert.That(_handlers.EscMessages[0].Raw, Is.EqualTo("\x1b\x37"));
        Assert.That(_handlers.EscMessages[0].Implemented, Is.True);
    }

    [Test]
    public void PushByte_CsiSequence_CallsHandleCsi()
    {
        // Arrange - CSI A (cursor up)
        byte[] csiSequence = { 0x1b, 0x5b, 0x41 };

        // Act
        _parser.PushBytes(csiSequence);

        // Assert
        Assert.That(_handlers.CsiMessages, Has.Count.EqualTo(1));
        Assert.That(_handlers.CsiMessages[0].Raw, Is.EqualTo("\x1b[A"));
        Assert.That(_handlers.CsiMessages[0].FinalByte, Is.EqualTo(0x41));
    }

    [Test]
    public void PushByte_OscSequence_CallsHandleXtermOsc()
    {
        // Arrange - OSC 0;title BEL
        string oscSequence = "\x1b]0;Test Title\x07";
        byte[] oscBytes = Encoding.UTF8.GetBytes(oscSequence);

        // Act
        _parser.PushBytes(oscBytes);

        // Assert - Should call HandleXtermOsc for recognized xterm sequences
        Assert.That(_handlers.XtermOscMessages, Has.Count.EqualTo(1));
        Assert.That(_handlers.XtermOscMessages[0].Raw, Is.EqualTo(oscSequence));
        Assert.That(_handlers.XtermOscMessages[0].Terminator, Is.EqualTo("BEL"));
        Assert.That(_handlers.XtermOscMessages[0].Type, Is.EqualTo("osc.setTitleAndIcon"));
        Assert.That(_handlers.XtermOscMessages[0].Title, Is.EqualTo("Test Title"));
    }

    [Test]
    public void PushByte_UnrecognizedOscSequence_CallsHandleOsc()
    {
        // Arrange - OSC 999;unknown BEL (unrecognized command)
        string oscSequence = "\x1b]999;unknown\x07";
        byte[] oscBytes = Encoding.UTF8.GetBytes(oscSequence);

        // Act
        _parser.PushBytes(oscBytes);

        // Assert - Should call HandleOsc for unrecognized sequences
        Assert.That(_handlers.OscMessages, Has.Count.EqualTo(1));
        Assert.That(_handlers.OscMessages[0].Raw, Is.EqualTo(oscSequence));
        Assert.That(_handlers.OscMessages[0].Terminator, Is.EqualTo("BEL"));
        Assert.That(_handlers.OscMessages[0].Implemented, Is.False);
        Assert.That(_handlers.XtermOscMessages, Has.Count.EqualTo(0));
    }

    [Test]
    public void PushByte_SgrSequence_CallsHandleSgr()
    {
        // Arrange - CSI 31 m (red foreground)
        byte[] sgrSequence = { 0x1b, 0x5b, 0x33, 0x31, 0x6d };

        // Act
        _parser.PushBytes(sgrSequence);

        // Assert
        Assert.That(_handlers.SgrSequences, Has.Count.EqualTo(1));
        Assert.That(_handlers.SgrSequences[0].Raw, Is.EqualTo("\x1b[31m"));
        Assert.That(_handlers.SgrSequences[0].Type, Is.EqualTo("sgr"));
    }

    [Test]
    public void PushByte_Utf8Sequence_CallsHandleNormalByteWithCodePoint()
    {
        // Arrange - UTF-8 sequence for 'é' (U+00E9)
        byte[] utf8Sequence = { 0xc3, 0xa9 };

        // Act
        _parser.PushBytes(utf8Sequence);

        // Assert
        Assert.That(_handlers.NormalBytes, Has.Count.EqualTo(1));
        Assert.That(_handlers.NormalBytes[0], Is.EqualTo(0x00E9)); // Unicode code point for 'é'
    }

    [Test]
    public void PushByte_CharacterSetDesignation_CallsHandleEsc()
    {
        // Arrange - ESC ( B (designate G0 to ASCII)
        byte[] charsetSequence = { 0x1b, 0x28, 0x42 };

        // Act
        _parser.PushBytes(charsetSequence);

        // Assert
        Assert.That(_handlers.EscMessages, Has.Count.EqualTo(1));
        Assert.That(_handlers.EscMessages[0].Type, Is.EqualTo("esc.designateCharacterSet"));
        Assert.That(_handlers.EscMessages[0].Slot, Is.EqualTo("G0"));
        Assert.That(_handlers.EscMessages[0].Charset, Is.EqualTo("B"));
        Assert.That(_handlers.EscMessages[0].Raw, Is.EqualTo("\x1b(B"));
    }

    [Test]
    public void PushByte_DcsSequence_CallsHandleDcs()
    {
        // Arrange - DCS sequence terminated by ST
        string dcsSequence = "\x1bP1$q\x1b\\";
        byte[] dcsBytes = Encoding.UTF8.GetBytes(dcsSequence);

        // Act
        _parser.PushBytes(dcsBytes);

        // Assert
        Assert.That(_handlers.DcsMessages, Has.Count.EqualTo(1));
        Assert.That(_handlers.DcsMessages[0].Raw, Is.EqualTo(dcsSequence));
        Assert.That(_handlers.DcsMessages[0].Terminator, Is.EqualTo("ST"));
        Assert.That(_handlers.DcsMessages[0].Command, Is.EqualTo("q"));
        Assert.That(_handlers.DcsMessages[0].Parameters, Is.EqualTo(new[] { "1$" }));
    }

    [Test]
    public void PushByte_ControlCharactersDuringEscape_ProcessedWhenEnabled()
    {
        // Arrange - ESC followed by BEL, then continue ESC sequence
        byte[] sequence = { 0x1b, 0x07, 0x37 }; // ESC BEL 7

        // Act
        _parser.PushBytes(sequence);

        // Assert
        Assert.That(_handlers.BellCalled, Is.True, "BEL should be processed during ESC sequence");
        Assert.That(_handlers.EscMessages, Has.Count.EqualTo(1), "ESC 7 should complete after BEL");
        Assert.That(_handlers.EscMessages[0].Type, Is.EqualTo("esc.saveCursor"));
    }

    [Test]
    public void PushByte_CanAndSubAbortControlString_ResetsState()
    {
        // Arrange - Start DCS sequence, then send CAN to abort
        byte[] sequence = { 0x1b, 0x50, 0x31, 0x18 }; // ESC P 1 CAN

        // Act
        _parser.PushBytes(sequence);

        // Follow with normal character to verify state reset
        _parser.PushByte((byte)'A');

        // Assert
        Assert.That(_handlers.DcsMessages, Has.Count.EqualTo(0), "DCS should be aborted by CAN");
        Assert.That(_handlers.NormalBytes, Has.Count.EqualTo(1), "Normal character should be processed after abort");
        Assert.That(_handlers.NormalBytes[0], Is.EqualTo((byte)'A'));
    }
}

/// <summary>
///     Test implementation of IParserHandlers for capturing parser events.
/// </summary>
public class TestParserHandlers : IParserHandlers
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

    public void HandleBell()
    {
        BellCalled = true;
    }

    public void HandleBackspace()
    {
        BackspaceCalled = true;
    }

    public void HandleTab()
    {
        TabCalled = true;
    }

    public void HandleLineFeed()
    {
        LineFeedCalled = true;
    }

    public void HandleFormFeed()
    {
        FormFeedCalled = true;
    }

    public void HandleCarriageReturn()
    {
        CarriageReturnCalled = true;
    }

    public void HandleShiftIn()
    {
        ShiftInCalled = true;
    }

    public void HandleShiftOut()
    {
        ShiftOutCalled = true;
    }

    public void HandleNormalByte(int codePoint)
    {
        NormalBytes.Add(codePoint);
    }

    public void HandleEsc(EscMessage message)
    {
        EscMessages.Add(message);
    }

    public void HandleCsi(CsiMessage message)
    {
        CsiMessages.Add(message);
    }

    public void HandleOsc(OscMessage message)
    {
        OscMessages.Add(message);
    }

    public void HandleDcs(DcsMessage message)
    {
        DcsMessages.Add(message);
    }

    public void HandleSgr(SgrSequence sequence)
    {
        SgrSequences.Add(sequence);
    }

    public void HandleXtermOsc(XtermOscMessage message)
    {
        XtermOscMessages.Add(message);
    }
}

/// <summary>
///     Test implementation of ILogger for capturing log messages.
/// </summary>
public class TestLogger : ILogger
{
    public List<string> LogMessages { get; } = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        return null;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return true;
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        string message = formatter(state, exception);
        LogMessages.Add($"[{logLevel}] {message}");
    }
}
