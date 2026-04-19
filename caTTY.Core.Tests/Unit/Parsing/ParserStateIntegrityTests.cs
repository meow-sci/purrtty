using System.Text;
using caTTY.Core.Parsing;
using caTTY.Core.Types;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace caTTY.Core.Tests.Unit.Parsing;

/// <summary>
/// Unit tests for parser state integrity, matching Parser.state.property.test.ts
/// Tests that parser state remains consistent during complex sequence processing.
/// </summary>
[TestFixture]
[Category("Unit")]
public class ParserStateIntegrityTests
{
    private Parser _parser = null!;
    private TestParserHandlers _handlers = null!;

    [SetUp]
    public void SetUp()
    {
        _handlers = new TestParserHandlers();
        var options = new ParserOptions
        {
            Logger = NullLogger.Instance,
            Handlers = _handlers,
            EmitNormalBytesDuringEscapeSequence = false,
            ProcessC0ControlsDuringEscapeSequence = true
        };
        _parser = new Parser(options);
    }

    [Test]
    public void Parser_StateRemainsConsistent_AfterCompleteSequence()
    {
        // Arrange - Complete CSI sequence
        byte[] sequence = new byte[] { 0x1b, 0x5b, 0x33, 0x31, 0x6d }; // ESC[31m

        // Act
        _parser.PushBytes(sequence);

        // Assert - Parser should process complete sequence
        Assert.That(_handlers.SgrSequences, Has.Count.EqualTo(1));
    }

    [Test]
    public void Parser_StateRemainsConsistent_AfterIncompleteSequence()
    {
        // Arrange - Incomplete CSI sequence (missing final byte)
        byte[] incompleteSequence = { 0x1b, 0x5b, 0x33, 0x31 }; // ESC [ 3 1

        // Act
        _parser.PushBytes(incompleteSequence);

        // Assert - Parser should not have processed incomplete sequence
        Assert.That(_handlers.SgrSequences, Has.Count.EqualTo(0));

        // Complete the sequence
        _parser.PushByte((byte)'m');
        Assert.That(_handlers.SgrSequences, Has.Count.EqualTo(1));
    }

    [Test]
    public void Parser_StateRemainsConsistent_AfterAbortedSequence()
    {
        // Arrange - Start CSI sequence then abort with CAN
        byte[] sequence = { 0x1b, 0x5b, 0x33, 0x31, 0x18 }; // ESC [ 3 1 CAN

        // Act
        _parser.PushBytes(sequence);

        // Assert - Parser should handle abort gracefully
        Assert.That(_handlers.SgrSequences, Has.Count.EqualTo(0));
    }

    [Test]
    public void Parser_StateRemainsConsistent_WithControlCharactersDuringSequence()
    {
        // Arrange - CSI sequence with BEL in the middle
        byte[] sequence = { 0x1b, 0x5b, 0x07, 0x33, 0x31, 0x6d }; // ESC [ BEL 3 1 m

        // Act
        _parser.PushBytes(sequence);

        // Assert - BEL should be processed, sequence should complete
        Assert.That(_handlers.BellCalled, Is.True);
        Assert.That(_handlers.SgrSequences, Has.Count.EqualTo(1));
    }

    [Test]
    public void Parser_StateRemainsConsistent_WithNestedSequences()
    {
        // Arrange - ESC sequence followed immediately by CSI sequence
        byte[] sequence = new byte[] { 0x1b, 0x37, 0x1b, 0x5b, 0x33, 0x31, 0x6d }; // ESC 7 + ESC [ 3 1 m

        // Act
        _parser.PushBytes(sequence);

        // Assert - Both sequences should be processed
        Assert.That(_handlers.EscMessages, Has.Count.EqualTo(1));
        Assert.That(_handlers.EscMessages[0].Type, Is.EqualTo("esc.saveCursor"));
        Assert.That(_handlers.SgrSequences, Has.Count.EqualTo(1));
    }

    [Test]
    public void Parser_StateRemainsConsistent_WithLongParameterString()
    {
        // Arrange - CSI sequence with many parameters
        string longParams = string.Join(";", Enumerable.Range(1, 50));
        string sequenceString = $"[{longParams}m";
        byte[] sequenceBytes = new byte[sequenceString.Length + 1];
        sequenceBytes[0] = 0x1b; // ESC
        Encoding.ASCII.GetBytes(sequenceString, 0, sequenceString.Length, sequenceBytes, 1);
        string expectedRaw = $"\x1b[{longParams}m"; // Expected raw string for assertion

        // Act
        _parser.PushBytes(sequenceBytes);

        // Assert - Should handle long parameter strings without corruption
        Assert.That(_handlers.SgrSequences, Has.Count.EqualTo(1));
        Assert.That(_handlers.SgrSequences[0].Raw, Is.EqualTo(expectedRaw));
    }

    [Test]
    public void Parser_StateRemainsConsistent_WithInvalidUtf8Sequence()
    {
        // Arrange - Invalid UTF-8 sequence (incomplete multi-byte)
        byte[] invalidUtf8 = { 0xc3 }; // Start of 2-byte sequence without continuation

        // Act
        _parser.PushBytes(invalidUtf8);
        _parser.PushByte((byte)'A'); // Follow with valid ASCII

        // Assert - Should handle invalid UTF-8 gracefully
        Assert.That(_handlers.NormalBytes, Has.Count.GreaterThan(0));
    }

    [Test]
    public void Parser_StateRemainsConsistent_WithMixedSequenceTypes()
    {
        // Arrange - Mix of different sequence types
        // ESC 7 + ESC[31m + ESC]0;Title BEL + ESC P 1$q ESC\ + Hello
        byte[] sequenceBytes = new byte[] 
        { 
            0x1b, 0x37,                           // ESC 7 (save cursor)
            0x1b, 0x5b, 0x33, 0x31, 0x6d,        // ESC[31m (red color)
            0x1b, 0x5d, 0x30, 0x3b, 0x54, 0x69, 0x74, 0x6c, 0x65, 0x07, // ESC]0;Title BEL
            0x1b, 0x50, 0x31, 0x24, 0x71, 0x1b, 0x5c, // ESC P 1$q ESC\
            0x48, 0x65, 0x6c, 0x6c, 0x6f          // Hello
        };

        // Act
        _parser.PushBytes(sequenceBytes);

        // Assert - All sequences should be processed correctly
        Assert.That(_handlers.EscMessages, Has.Count.EqualTo(1)); // Save cursor
        Assert.That(_handlers.SgrSequences, Has.Count.EqualTo(1)); // Red color
        Assert.That(_handlers.XtermOscMessages, Has.Count.EqualTo(1)); // Title
        Assert.That(_handlers.DcsMessages, Has.Count.EqualTo(1)); // DCS query
        Assert.That(_handlers.NormalBytes, Has.Count.EqualTo(5)); // "Hello"
    }

    [Test]
    public void Parser_StateRemainsConsistent_WithRepeatedSequences()
    {
        // Arrange - Same sequence repeated multiple times
        byte[] sequenceBytes = new byte[] 
        { 
            0x1b, 0x5b, 0x33, 0x31, 0x6d,        // ESC[31m (red)
            0x1b, 0x5b, 0x33, 0x32, 0x6d,        // ESC[32m (green)
            0x1b, 0x5b, 0x33, 0x33, 0x6d,        // ESC[33m (yellow)
            0x1b, 0x5b, 0x33, 0x34, 0x6d         // ESC[34m (blue)
        };

        // Act
        _parser.PushBytes(sequenceBytes);

        // Assert - All sequences should be processed
        Assert.That(_handlers.SgrSequences, Has.Count.EqualTo(4));
    }

    [Test]
    public void Parser_StateRemainsConsistent_WithEmptyParameters()
    {
        // Arrange - CSI sequence with empty parameters
        byte[] sequenceBytes = new byte[] { 0x1b, 0x5b, 0x3b, 0x3b, 0x6d }; // ESC[;;m

        // Act
        _parser.PushBytes(sequenceBytes);

        // Assert - Should handle empty parameters gracefully
        Assert.That(_handlers.SgrSequences, Has.Count.EqualTo(1));
    }

    [Test]
    public void Parser_StateRemainsConsistent_WithMaxParameterCount()
    {
        // Arrange - CSI sequence at parameter limit
        string manyParams = string.Join(";", Enumerable.Range(1, 16)); // Typical limit
        string sequenceString = $"[{manyParams}m";
        byte[] sequenceBytes = new byte[sequenceString.Length + 1];
        sequenceBytes[0] = 0x1b; // ESC
        Encoding.ASCII.GetBytes(sequenceString, 0, sequenceString.Length, sequenceBytes, 1);

        // Act
        _parser.PushBytes(sequenceBytes);

        // Assert - Should handle parameter limit gracefully
        Assert.That(_handlers.SgrSequences, Has.Count.EqualTo(1));
    }
}