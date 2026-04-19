using caTTY.Core.Terminal;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace caTTY.Core.Tests.Unit.Terminal;

/// <summary>
///     Integration tests for unknown OSC sequence handling in the terminal.
///     Verifies that unknown OSC sequences are handled gracefully without errors.
/// </summary>
[TestFixture]
[Category("Unit")]
public class UnknownOscHandlingTests
{
    private TerminalEmulator _terminal = null!;

    [SetUp]
    public void SetUp()
    {
        _terminal = TerminalEmulator.Create(80, 24, NullLogger.Instance);
    }

    [TearDown]
    public void TearDown()
    {
        _terminal?.Dispose();
    }

    /// <summary>
    ///     Test that unknown OSC commands are handled gracefully.
    /// </summary>
    [Test]
    public void UnknownOscCommand_HandledGracefully()
    {
        // Arrange & Act - Send unknown OSC command 999
        Assert.DoesNotThrow(() => _terminal.Write("\x1b]999;unknown command\x07"));
        
        // Assert - Terminal should still be functional
        Assert.That(_terminal.Width, Is.EqualTo(80));
        Assert.That(_terminal.Height, Is.EqualTo(24));
        
        // Verify terminal can still process normal text
        _terminal.Write("Hello");
        var cell = _terminal.ScreenBuffer.GetCell(0, 0);
        Assert.That(cell.Character, Is.EqualTo('H'));
    }

    /// <summary>
    ///     Test that invalid OSC command numbers are handled gracefully.
    /// </summary>
    [Test]
    public void InvalidOscCommandNumber_HandledGracefully()
    {
        // Arrange & Act - Send invalid OSC command number (> 999)
        Assert.DoesNotThrow(() => _terminal.Write("\x1b]1000;invalid\x07"));
        
        // Assert - Terminal should still be functional
        Assert.That(_terminal.Width, Is.EqualTo(80));
        Assert.That(_terminal.Height, Is.EqualTo(24));
        
        // Verify terminal can still process normal text
        _terminal.Write("Test");
        var cell = _terminal.ScreenBuffer.GetCell(0, 0);
        Assert.That(cell.Character, Is.EqualTo('T'));
    }

    /// <summary>
    ///     Test that malformed OSC sequences are handled gracefully.
    /// </summary>
    [Test]
    public void MalformedOscSequence_HandledGracefully()
    {
        // Arrange & Act - Send malformed OSC sequence
        Assert.DoesNotThrow(() => _terminal.Write("\x1b]abc;malformed\x07"));
        
        // Assert - Terminal should still be functional
        Assert.That(_terminal.Width, Is.EqualTo(80));
        Assert.That(_terminal.Height, Is.EqualTo(24));
        
        // Verify terminal can still process normal text
        _terminal.Write("OK");
        var cell = _terminal.ScreenBuffer.GetCell(0, 0);
        Assert.That(cell.Character, Is.EqualTo('O'));
    }

    /// <summary>
    ///     Test that multiple unknown OSC sequences don't affect terminal state.
    /// </summary>
    [Test]
    public void MultipleUnknownOscSequences_HandledGracefully()
    {
        // Arrange & Act - Send multiple unknown OSC sequences
        Assert.DoesNotThrow(() =>
        {
            _terminal.Write("\x1b]999;first unknown\x07");
            _terminal.Write("\x1b]888;second unknown\x1b\\"); // ST terminator
            _terminal.Write("\x1b]1001;third invalid\x07");
        });
        
        // Assert - Terminal should still be functional
        Assert.That(_terminal.Width, Is.EqualTo(80));
        Assert.That(_terminal.Height, Is.EqualTo(24));
        
        // Verify terminal can still process normal text
        _terminal.Write("Working");
        var cell = _terminal.ScreenBuffer.GetCell(0, 0);
        Assert.That(cell.Character, Is.EqualTo('W'));
    }

    /// <summary>
    ///     Test that unknown OSC sequences mixed with known sequences work correctly.
    /// </summary>
    [Test]
    public void UnknownOscMixedWithKnownOsc_HandledCorrectly()
    {
        // Arrange
        string? receivedTitle = null;
        _terminal.TitleChanged += (sender, args) => receivedTitle = args.NewTitle;

        // Act - Mix unknown and known OSC sequences
        Assert.DoesNotThrow(() =>
        {
            _terminal.Write("\x1b]999;unknown\x07");           // Unknown
            _terminal.Write("\x1b]2;Test Title\x07");          // Known: set window title
            _terminal.Write("\x1b]888;another unknown\x07");   // Unknown
        });
        
        // Assert - Known OSC should work, unknown should be ignored
        Assert.That(receivedTitle, Is.EqualTo("Test Title"));
        
        // Terminal should still be functional
        _terminal.Write("Text");
        var cell = _terminal.ScreenBuffer.GetCell(0, 0);
        Assert.That(cell.Character, Is.EqualTo('T'));
    }

    /// <summary>
    ///     Test that unknown OSC sequences with very long payloads are handled safely.
    /// </summary>
    [Test]
    public void UnknownOscWithLongPayload_HandledSafely()
    {
        // Arrange - Create OSC with very long payload
        string longPayload = new('A', 2000); // Exceeds MaxOscPayloadLength
        string oscSequence = $"\x1b]999;{longPayload}\x07";

        // Act & Assert - Should not throw exceptions
        Assert.DoesNotThrow(() => _terminal.Write(oscSequence));
        
        // Terminal should still be functional
        Assert.That(_terminal.Width, Is.EqualTo(80));
        Assert.That(_terminal.Height, Is.EqualTo(24));
        
        // Verify terminal can still process normal text
        _terminal.Write("Safe");
        var cell = _terminal.ScreenBuffer.GetCell(0, 0);
        Assert.That(cell.Character, Is.EqualTo('S'));
    }
}