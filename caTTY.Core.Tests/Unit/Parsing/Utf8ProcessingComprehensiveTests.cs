using caTTY.Core.Parsing;
using NUnit.Framework;

namespace caTTY.Core.Tests.Unit.Parsing;

/// <summary>
/// Comprehensive UTF-8 processing tests, matching Utf8.test.ts
/// Tests UTF-8 decoding, validation, error handling, and multi-byte sequences.
/// </summary>
[TestFixture]
[Category("Unit")]
public class Utf8ProcessingComprehensiveTests
{
    private Utf8Decoder _decoder = null!;

    [SetUp]
    public void SetUp()
    {
        _decoder = new Utf8Decoder();
    }

    [Test]
    public void Utf8_ProcessSingleByteAscii_DecodesCorrectly()
    {
        // Arrange - ASCII character 'A'
        byte asciiA = 0x41;

        // Act
        bool result = _decoder.ProcessByte(asciiA, out int codePoint);

        // Assert
        Assert.That(result, Is.True);
        Assert.That(codePoint, Is.EqualTo(0x41));
    }

    [Test]
    public void Utf8_ProcessTwoByteSequence_DecodesCorrectly()
    {
        // Arrange - UTF-8 sequence for 'é' (U+00E9): 0xC3 0xA9
        byte[] utf8Sequence = { 0xC3, 0xA9 };

        // Act - Process first byte
        bool result1 = _decoder.ProcessByte(utf8Sequence[0], out int codePoint1);
        Assert.That(result1, Is.False); // Should need more bytes

        // Process second byte
        bool result2 = _decoder.ProcessByte(utf8Sequence[1], out int codePoint2);

        // Assert
        Assert.That(result2, Is.True);
        Assert.That(codePoint2, Is.EqualTo(0x00E9)); // Unicode for 'é'
    }

    [Test]
    public void Utf8_ProcessThreeByteSequence_DecodesCorrectly()
    {
        // Arrange - UTF-8 sequence for '€' (U+20AC): 0xE2 0x82 0xAC
        byte[] utf8Sequence = { 0xE2, 0x82, 0xAC };

        // Act - Process bytes sequentially
        bool result1 = _decoder.ProcessByte(utf8Sequence[0], out int codePoint1);
        Assert.That(result1, Is.False);

        bool result2 = _decoder.ProcessByte(utf8Sequence[1], out int codePoint2);
        Assert.That(result2, Is.False);

        bool result3 = _decoder.ProcessByte(utf8Sequence[2], out int codePoint3);

        // Assert
        Assert.That(result3, Is.True);
        Assert.That(codePoint3, Is.EqualTo(0x20AC)); // Unicode for '€'
    }

    [Test]
    public void Utf8_ProcessFourByteSequence_DecodesCorrectly()
    {
        // Arrange - UTF-8 sequence for '𝄞' (U+1D11E): 0xF0 0x9D 0x84 0x9E
        byte[] utf8Sequence = { 0xF0, 0x9D, 0x84, 0x9E };

        // Act - Process bytes sequentially
        bool result1 = _decoder.ProcessByte(utf8Sequence[0], out int codePoint1);
        Assert.That(result1, Is.False);

        bool result2 = _decoder.ProcessByte(utf8Sequence[1], out int codePoint2);
        Assert.That(result2, Is.False);

        bool result3 = _decoder.ProcessByte(utf8Sequence[2], out int codePoint3);
        Assert.That(result3, Is.False);

        bool result4 = _decoder.ProcessByte(utf8Sequence[3], out int codePoint4);

        // Assert
        Assert.That(result4, Is.True);
        Assert.That(codePoint4, Is.EqualTo(0x1D11E)); // Unicode for '𝄞'
    }

    [Test]
    public void Utf8_ProcessInvalidStartByte_HandlesGracefully()
    {
        // Arrange - Invalid UTF-8 start byte (continuation byte used as start)
        byte invalidStart = 0x80;

        // Act
        bool result = _decoder.ProcessByte(invalidStart, out int codePoint);

        // Assert - Should treat as single byte
        Assert.That(result, Is.True);
        Assert.That(codePoint, Is.EqualTo(0x80));
    }

    [Test]
    public void Utf8_ProcessIncompleteSequence_HandlesGracefully()
    {
        // Arrange - Start of 2-byte sequence without continuation
        byte incompleteStart = 0xC3;

        // Act - Process start byte
        bool result1 = _decoder.ProcessByte(incompleteStart, out int codePoint1);
        Assert.That(result1, Is.False);

        // Flush incomplete sequence
        bool hasIncomplete = _decoder.FlushIncompleteSequence(out var invalidBytes);

        // Assert
        Assert.That(hasIncomplete, Is.True);
        Assert.That(invalidBytes.Length, Is.EqualTo(1));
        Assert.That(invalidBytes[0], Is.EqualTo(0xC3));
    }

    [Test]
    public void Utf8_ProcessInvalidContinuation_HandlesGracefully()
    {
        // Arrange - Valid start byte followed by invalid continuation
        byte validStart = 0xC3;
        byte invalidContinuation = 0x41; // ASCII 'A' instead of continuation

        // Act - Process start byte
        bool result1 = _decoder.ProcessByte(validStart, out int codePoint1);
        Assert.That(result1, Is.False);

        // Process invalid continuation
        bool result2 = _decoder.ProcessByte(invalidContinuation, out int codePoint2);

        // Assert - Should emit the invalid start byte and process 'A' normally
        Assert.That(result2, Is.True);
        Assert.That(codePoint2, Is.EqualTo(0xC3)); // The invalid start byte
    }

    [Test]
    public void Utf8_ProcessMixedValidInvalid_HandlesCorrectly()
    {
        // Arrange - Mix of valid ASCII and invalid UTF-8
        byte[] mixedBytes = { 0x41, 0xC3, 0xA9, 0x42, 0xFF, 0x43 }; // A, é, B, invalid, C

        var results = new List<int>();

        // Act - Process all bytes
        foreach (byte b in mixedBytes)
        {
            if (_decoder.ProcessByte(b, out int codePoint))
            {
                results.Add(codePoint);
            }
        }

        // Assert - Should get A, é, B, invalid byte, C
        Assert.That(results, Has.Count.EqualTo(5));
        Assert.That(results[0], Is.EqualTo(0x41)); // A
        Assert.That(results[1], Is.EqualTo(0x00E9)); // é
        Assert.That(results[2], Is.EqualTo(0x42)); // B
        Assert.That(results[3], Is.EqualTo(0xFF)); // Invalid byte
        Assert.That(results[4], Is.EqualTo(0x43)); // C
    }

    [Test]
    public void Utf8_ValidateStartBytes_WorksCorrectly()
    {
        // Test valid start bytes
        Assert.That(_decoder.IsValidUtf8Start(0x41), Is.True); // ASCII
        Assert.That(_decoder.IsValidUtf8Start(0xC2), Is.True); // 2-byte start
        Assert.That(_decoder.IsValidUtf8Start(0xE0), Is.True); // 3-byte start
        Assert.That(_decoder.IsValidUtf8Start(0xF0), Is.True); // 4-byte start

        // Test invalid start bytes
        Assert.That(_decoder.IsValidUtf8Start(0x80), Is.False); // Continuation byte
        Assert.That(_decoder.IsValidUtf8Start(0xC0), Is.False); // Overlong encoding
        Assert.That(_decoder.IsValidUtf8Start(0xC1), Is.False); // Overlong encoding
        Assert.That(_decoder.IsValidUtf8Start(0xF5), Is.False); // Beyond Unicode range
        Assert.That(_decoder.IsValidUtf8Start(0xFF), Is.False); // Invalid
    }

    [Test]
    public void Utf8_GetExpectedLength_ReturnsCorrectValues()
    {
        // Test expected lengths
        Assert.That(_decoder.GetExpectedLength(0x41), Is.EqualTo(1)); // ASCII
        Assert.That(_decoder.GetExpectedLength(0xC2), Is.EqualTo(2)); // 2-byte
        Assert.That(_decoder.GetExpectedLength(0xE0), Is.EqualTo(3)); // 3-byte
        Assert.That(_decoder.GetExpectedLength(0xF0), Is.EqualTo(4)); // 4-byte

        // Test invalid bytes
        Assert.That(_decoder.GetExpectedLength(0x80), Is.EqualTo(0)); // Continuation
        Assert.That(_decoder.GetExpectedLength(0xC0), Is.EqualTo(0)); // Overlong
        Assert.That(_decoder.GetExpectedLength(0xFF), Is.EqualTo(0)); // Invalid
    }

    [Test]
    public void Utf8_TryDecodeSequence_WorksWithCompleteSequences()
    {
        // Test complete 2-byte sequence
        byte[] twoByteSeq = { 0xC3, 0xA9 }; // é
        bool result = _decoder.TryDecodeSequence(twoByteSeq, out int codePoint, out int bytesConsumed);
        
        Assert.That(result, Is.True);
        Assert.That(codePoint, Is.EqualTo(0x00E9));
        Assert.That(bytesConsumed, Is.EqualTo(2));
    }

    [Test]
    public void Utf8_TryDecodeSequence_FailsWithIncompleteSequences()
    {
        // Test incomplete 2-byte sequence
        byte[] incompleteSeq = { 0xC3 }; // Missing continuation byte
        bool result = _decoder.TryDecodeSequence(incompleteSeq, out int codePoint, out int bytesConsumed);
        
        Assert.That(result, Is.False);
        Assert.That(codePoint, Is.EqualTo(0));
        Assert.That(bytesConsumed, Is.EqualTo(0));
    }

    [Test]
    public void Utf8_Reset_ClearsInternalState()
    {
        // Arrange - Start processing a multi-byte sequence
        _decoder.ProcessByte(0xC3, out _); // Start 2-byte sequence

        // Act - Reset decoder
        _decoder.Reset();

        // Assert - Should be able to process new sequence from clean state
        bool result = _decoder.ProcessByte(0x41, out int codePoint); // ASCII A
        Assert.That(result, Is.True);
        Assert.That(codePoint, Is.EqualTo(0x41));
    }

    [Test]
    public void Utf8_ProcessLongSequence_HandlesCorrectly()
    {
        // Arrange - Long sequence with mixed UTF-8 content
        string testString = "Hello 世界 🌍 Test";
        byte[] utf8Bytes = System.Text.Encoding.UTF8.GetBytes(testString);

        var decodedCodePoints = new List<int>();

        // Act - Process all bytes
        foreach (byte b in utf8Bytes)
        {
            if (_decoder.ProcessByte(b, out int codePoint))
            {
                decodedCodePoints.Add(codePoint);
            }
        }

        // Assert - Should decode back to original string
        string reconstructed = string.Concat(decodedCodePoints.Select(cp => char.ConvertFromUtf32(cp)));
        Assert.That(reconstructed, Is.EqualTo(testString));
    }
}