using caTTY.Core.Parsing;
using caTTY.Core.Types;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace caTTY.Core.Tests.Unit.Parsing;

/// <summary>
///     Unit tests for the ESC sequence parser.
/// </summary>
[TestFixture]
[Category("Unit")]
public class EscParserTests
{
    private EscParser _parser = null!;
    private TestLogger _logger = null!;

    [SetUp]
    public void SetUp()
    {
        _logger = new TestLogger();
        _parser = new EscParser(_logger);
    }

    [Test]
    public void ProcessEscByte_SaveCursor_ReturnsCompleteMessage()
    {
        // Arrange
        var escapeSequence = new List<byte> { 0x1b }; // ESC already added
        byte b = 0x37; // 7

        // Act
        bool isComplete = _parser.ProcessEscByte(b, escapeSequence, out EscMessage? message);

        // Assert
        Assert.That(isComplete, Is.True);
        Assert.That(message, Is.Not.Null);
        Assert.That(message!.Type, Is.EqualTo("esc.saveCursor"));
        Assert.That(message.Raw, Is.EqualTo("\x1b\x37"));
        Assert.That(message.Implemented, Is.True);
        Assert.That(escapeSequence, Has.Count.EqualTo(2));
        Assert.That(escapeSequence[1], Is.EqualTo(0x37));
    }

    [Test]
    public void ProcessEscByte_RestoreCursor_ReturnsCompleteMessage()
    {
        // Arrange
        var escapeSequence = new List<byte> { 0x1b }; // ESC already added
        byte b = 0x38; // 8

        // Act
        bool isComplete = _parser.ProcessEscByte(b, escapeSequence, out EscMessage? message);

        // Assert
        Assert.That(isComplete, Is.True);
        Assert.That(message, Is.Not.Null);
        Assert.That(message!.Type, Is.EqualTo("esc.restoreCursor"));
        Assert.That(message.Raw, Is.EqualTo("\x1b\x38"));
        Assert.That(message.Implemented, Is.True);
    }

    [Test]
    public void ProcessEscByte_ReverseIndex_ReturnsCompleteMessage()
    {
        // Arrange
        var escapeSequence = new List<byte> { 0x1b }; // ESC already added
        byte b = 0x4d; // M

        // Act
        bool isComplete = _parser.ProcessEscByte(b, escapeSequence, out EscMessage? message);

        // Assert
        Assert.That(isComplete, Is.True);
        Assert.That(message, Is.Not.Null);
        Assert.That(message!.Type, Is.EqualTo("esc.reverseIndex"));
        Assert.That(message.Raw, Is.EqualTo("\x1b\x4d"));
        Assert.That(message.Implemented, Is.True);
    }

    [Test]
    public void ProcessEscByte_Index_ReturnsCompleteMessage()
    {
        // Arrange
        var escapeSequence = new List<byte> { 0x1b }; // ESC already added
        byte b = 0x44; // D

        // Act
        bool isComplete = _parser.ProcessEscByte(b, escapeSequence, out EscMessage? message);

        // Assert
        Assert.That(isComplete, Is.True);
        Assert.That(message, Is.Not.Null);
        Assert.That(message!.Type, Is.EqualTo("esc.index"));
        Assert.That(message.Raw, Is.EqualTo("\x1b\x44"));
        Assert.That(message.Implemented, Is.True);
    }

    [Test]
    public void ProcessEscByte_NextLine_ReturnsCompleteMessage()
    {
        // Arrange
        var escapeSequence = new List<byte> { 0x1b }; // ESC already added
        byte b = 0x45; // E

        // Act
        bool isComplete = _parser.ProcessEscByte(b, escapeSequence, out EscMessage? message);

        // Assert
        Assert.That(isComplete, Is.True);
        Assert.That(message, Is.Not.Null);
        Assert.That(message!.Type, Is.EqualTo("esc.nextLine"));
        Assert.That(message.Raw, Is.EqualTo("\x1b\x45"));
        Assert.That(message.Implemented, Is.True);
    }

    [Test]
    public void ProcessEscByte_HorizontalTabSet_ReturnsCompleteMessage()
    {
        // Arrange
        var escapeSequence = new List<byte> { 0x1b }; // ESC already added
        byte b = 0x48; // H

        // Act
        bool isComplete = _parser.ProcessEscByte(b, escapeSequence, out EscMessage? message);

        // Assert
        Assert.That(isComplete, Is.True);
        Assert.That(message, Is.Not.Null);
        Assert.That(message!.Type, Is.EqualTo("esc.horizontalTabSet"));
        Assert.That(message.Raw, Is.EqualTo("\x1b\x48"));
        Assert.That(message.Implemented, Is.True);
    }

    [Test]
    public void ProcessEscByte_ResetToInitialState_ReturnsCompleteMessage()
    {
        // Arrange
        var escapeSequence = new List<byte> { 0x1b }; // ESC already added
        byte b = 0x63; // c

        // Act
        bool isComplete = _parser.ProcessEscByte(b, escapeSequence, out EscMessage? message);

        // Assert
        Assert.That(isComplete, Is.True);
        Assert.That(message, Is.Not.Null);
        Assert.That(message!.Type, Is.EqualTo("esc.resetToInitialState"));
        Assert.That(message.Raw, Is.EqualTo("\x1b\x63"));
        Assert.That(message.Implemented, Is.True);
    }

    [Test]
    public void ProcessEscByte_CharacterSetDesignationG0_NeedsMoreBytes()
    {
        // Arrange
        var escapeSequence = new List<byte> { 0x1b }; // ESC already added
        byte b = 0x28; // (

        // Act
        bool isComplete = _parser.ProcessEscByte(b, escapeSequence, out EscMessage? message);

        // Assert
        Assert.That(isComplete, Is.False);
        Assert.That(message, Is.Null);
        Assert.That(escapeSequence, Has.Count.EqualTo(2)); // ESC + (
        Assert.That(escapeSequence[1], Is.EqualTo(0x28));
    }

    [Test]
    public void ProcessEscByte_CharacterSetDesignationG0Complete_ReturnsCompleteMessage()
    {
        // Arrange - simulate ESC ( B sequence
        var escapeSequence = new List<byte> { 0x1b, 0x28 }; // ESC ( already processed
        byte b = 0x42; // B

        // Act
        bool isComplete = _parser.ProcessEscByte(b, escapeSequence, out EscMessage? message);

        // Assert
        Assert.That(isComplete, Is.True);
        Assert.That(message, Is.Not.Null);
        Assert.That(message!.Type, Is.EqualTo("esc.designateCharacterSet"));
        Assert.That(message.Raw, Is.EqualTo("\x1b(B"));
        Assert.That(message.Slot, Is.EqualTo("G0"));
        Assert.That(message.Charset, Is.EqualTo("B"));
        Assert.That(message.Implemented, Is.True);
    }

    [Test]
    public void ProcessEscByte_CharacterSetDesignationG1Complete_ReturnsCompleteMessage()
    {
        // Arrange - simulate ESC ) 0 sequence
        var escapeSequence = new List<byte> { 0x1b, 0x29 }; // ESC ) already processed
        byte b = 0x30; // 0

        // Act
        bool isComplete = _parser.ProcessEscByte(b, escapeSequence, out EscMessage? message);

        // Assert
        Assert.That(isComplete, Is.True);
        Assert.That(message, Is.Not.Null);
        Assert.That(message!.Type, Is.EqualTo("esc.designateCharacterSet"));
        Assert.That(message.Raw, Is.EqualTo("\x1b)0"));
        Assert.That(message.Slot, Is.EqualTo("G1"));
        Assert.That(message.Charset, Is.EqualTo("0"));
        Assert.That(message.Implemented, Is.True);
    }

    [Test]
    public void ProcessEscByte_CharacterSetDesignationG2Complete_ReturnsCompleteMessage()
    {
        // Arrange - simulate ESC * A sequence
        var escapeSequence = new List<byte> { 0x1b, 0x2a }; // ESC * already processed
        byte b = 0x41; // A

        // Act
        bool isComplete = _parser.ProcessEscByte(b, escapeSequence, out EscMessage? message);

        // Assert
        Assert.That(isComplete, Is.True);
        Assert.That(message, Is.Not.Null);
        Assert.That(message!.Type, Is.EqualTo("esc.designateCharacterSet"));
        Assert.That(message.Raw, Is.EqualTo("\x1b*A"));
        Assert.That(message.Slot, Is.EqualTo("G2"));
        Assert.That(message.Charset, Is.EqualTo("A"));
        Assert.That(message.Implemented, Is.True);
    }

    [Test]
    public void ProcessEscByte_CharacterSetDesignationG3Complete_ReturnsCompleteMessage()
    {
        // Arrange - simulate ESC + B sequence
        var escapeSequence = new List<byte> { 0x1b, 0x2b }; // ESC + already processed
        byte b = 0x42; // B

        // Act
        bool isComplete = _parser.ProcessEscByte(b, escapeSequence, out EscMessage? message);

        // Assert
        Assert.That(isComplete, Is.True);
        Assert.That(message, Is.Not.Null);
        Assert.That(message!.Type, Is.EqualTo("esc.designateCharacterSet"));
        Assert.That(message.Raw, Is.EqualTo("\x1b+B"));
        Assert.That(message.Slot, Is.EqualTo("G3"));
        Assert.That(message.Charset, Is.EqualTo("B"));
        Assert.That(message.Implemented, Is.True);
    }

    [Test]
    public void ProcessEscByte_UnknownSequence_ReturnsCompleteWithLogging()
    {
        // Arrange
        var escapeSequence = new List<byte> { 0x1b }; // ESC already added
        byte b = 0x5a; // Z (unknown)

        // Act
        bool isComplete = _parser.ProcessEscByte(b, escapeSequence, out EscMessage? message);

        // Assert
        Assert.That(isComplete, Is.True);
        Assert.That(message, Is.Null);
        Assert.That(escapeSequence, Has.Count.EqualTo(2)); // ESC + Z
        Assert.That(escapeSequence[1], Is.EqualTo(0x5a));
        Assert.That(_logger.LogMessages, Has.Some.Contains("ESC (opaque): \x1bZ"));
    }

    [Test]
    public void ProcessEscByte_ByteOutOfRange_ReturnsCompleteWithWarning()
    {
        // Arrange
        var escapeSequence = new List<byte> { 0x1b }; // ESC already added
        byte b = 0x10; // Control character (out of range)

        // Act
        bool isComplete = _parser.ProcessEscByte(b, escapeSequence, out EscMessage? message);

        // Assert
        Assert.That(isComplete, Is.True);
        Assert.That(message, Is.Null);
        Assert.That(_logger.LogMessages, Has.Some.Contains("ESC: byte out of range 0x10"));
    }

    [Test]
    public void ProcessEscByte_MultiByteUnknownSequence_ReturnsCompleteWhenFinalByte()
    {
        // Arrange - simulate ESC # 8 sequence (unknown multi-byte)
        var escapeSequence = new List<byte> { 0x1b, 0x23 }; // ESC # already processed
        byte b = 0x38; // 8 (final byte in range 0x30-0x7E)

        // Act
        bool isComplete = _parser.ProcessEscByte(b, escapeSequence, out EscMessage? message);

        // Assert
        Assert.That(isComplete, Is.True);
        Assert.That(message, Is.Null);
        Assert.That(escapeSequence, Has.Count.EqualTo(3));
        Assert.That(_logger.LogMessages, Has.Some.Contains("ESC (opaque): \x1b#8"));
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