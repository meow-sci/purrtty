using caTTY.Core.Parsing;
using NUnit.Framework;

namespace caTTY.Core.Tests.Unit.Parsing;

[TestFixture]
[Category("Unit")]
public class Utf8DecoderTests
{
    [SetUp]
    public void SetUp()
    {
        _decoder = new Utf8Decoder();
    }

    private Utf8Decoder _decoder = null!;

    [Test]
    public void TryDecodeSequence_SingleAsciiBytes_ReturnsCorrectCodePoints()
    {
        // Test ASCII characters
        byte[] testBytes = new byte[] { 0x41, 0x42, 0x43 }; // "ABC"

        Assert.That(_decoder.TryDecodeSequence(testBytes.AsSpan(0, 1), out int codePoint, out int bytesConsumed),
            Is.True);
        Assert.That(codePoint, Is.EqualTo(0x41));
        Assert.That(bytesConsumed, Is.EqualTo(1));
    }

    [Test]
    public void TryDecodeSequence_TwoByteUtf8_ReturnsCorrectCodePoint()
    {
        // Test 2-byte UTF-8: é (U+00E9) = 0xC3 0xA9
        byte[] testBytes = new byte[] { 0xC3, 0xA9 };

        Assert.That(_decoder.TryDecodeSequence(testBytes, out int codePoint, out int bytesConsumed), Is.True);
        Assert.That(codePoint, Is.EqualTo(0x00E9));
        Assert.That(bytesConsumed, Is.EqualTo(2));
    }

    [Test]
    public void TryDecodeSequence_ThreeByteUtf8_ReturnsCorrectCodePoint()
    {
        // Test 3-byte UTF-8: € (U+20AC) = 0xE2 0x82 0xAC
        byte[] testBytes = new byte[] { 0xE2, 0x82, 0xAC };

        Assert.That(_decoder.TryDecodeSequence(testBytes, out int codePoint, out int bytesConsumed), Is.True);
        Assert.That(codePoint, Is.EqualTo(0x20AC));
        Assert.That(bytesConsumed, Is.EqualTo(3));
    }

    [Test]
    public void TryDecodeSequence_FourByteUtf8_ReturnsCorrectCodePoint()
    {
        // Test 4-byte UTF-8: 𝄞 (U+1D11E) = 0xF0 0x9D 0x84 0x9E
        byte[] testBytes = new byte[] { 0xF0, 0x9D, 0x84, 0x9E };

        Assert.That(_decoder.TryDecodeSequence(testBytes, out int codePoint, out int bytesConsumed), Is.True);
        Assert.That(codePoint, Is.EqualTo(0x1D11E));
        Assert.That(bytesConsumed, Is.EqualTo(4));
    }

    [Test]
    public void TryDecodeSequence_IncompleteSequence_ReturnsFalse()
    {
        // Test incomplete 2-byte sequence
        byte[] testBytes = new byte[] { 0xC3 };

        Assert.That(_decoder.TryDecodeSequence(testBytes, out int codePoint, out int bytesConsumed), Is.False);
        Assert.That(bytesConsumed, Is.EqualTo(0));
    }

    [Test]
    public void TryDecodeSequence_InvalidContinuationByte_ReturnsFalse()
    {
        // Test invalid continuation byte
        byte[] testBytes = new byte[] { 0xC3, 0x41 }; // Second byte should be 0x80-0xBF

        Assert.That(_decoder.TryDecodeSequence(testBytes, out int codePoint, out int bytesConsumed), Is.False);
        Assert.That(bytesConsumed, Is.EqualTo(0));
    }

    [Test]
    public void IsValidUtf8Start_AsciiBytes_ReturnsTrue()
    {
        Assert.That(_decoder.IsValidUtf8Start(0x41), Is.True); // 'A'
        Assert.That(_decoder.IsValidUtf8Start(0x7F), Is.True); // DEL
        Assert.That(_decoder.IsValidUtf8Start(0x00), Is.True); // NUL
    }

    [Test]
    public void IsValidUtf8Start_ValidMultiByteStarts_ReturnsTrue()
    {
        Assert.That(_decoder.IsValidUtf8Start(0xC2), Is.True); // Valid 2-byte start
        Assert.That(_decoder.IsValidUtf8Start(0xDF), Is.True); // Valid 2-byte start
        Assert.That(_decoder.IsValidUtf8Start(0xE0), Is.True); // Valid 3-byte start
        Assert.That(_decoder.IsValidUtf8Start(0xEF), Is.True); // Valid 3-byte start
        Assert.That(_decoder.IsValidUtf8Start(0xF0), Is.True); // Valid 4-byte start
        Assert.That(_decoder.IsValidUtf8Start(0xF4), Is.True); // Valid 4-byte start
    }

    [Test]
    public void IsValidUtf8Start_InvalidBytes_ReturnsFalse()
    {
        Assert.That(_decoder.IsValidUtf8Start(0xC0), Is.False); // Invalid overlong
        Assert.That(_decoder.IsValidUtf8Start(0xC1), Is.False); // Invalid overlong
        Assert.That(_decoder.IsValidUtf8Start(0xF5), Is.False); // Beyond Unicode range
        Assert.That(_decoder.IsValidUtf8Start(0xFF), Is.False); // Invalid
        Assert.That(_decoder.IsValidUtf8Start(0x80), Is.False); // Continuation byte
        Assert.That(_decoder.IsValidUtf8Start(0xBF), Is.False); // Continuation byte
    }

    [Test]
    public void GetExpectedLength_ReturnsCorrectLengths()
    {
        Assert.That(_decoder.GetExpectedLength(0x41), Is.EqualTo(1)); // ASCII
        Assert.That(_decoder.GetExpectedLength(0xC2), Is.EqualTo(2)); // 2-byte
        Assert.That(_decoder.GetExpectedLength(0xE0), Is.EqualTo(3)); // 3-byte
        Assert.That(_decoder.GetExpectedLength(0xF0), Is.EqualTo(4)); // 4-byte
        Assert.That(_decoder.GetExpectedLength(0xC0), Is.EqualTo(0)); // Invalid
        Assert.That(_decoder.GetExpectedLength(0xF5), Is.EqualTo(0)); // Invalid
    }

    [Test]
    public void ProcessByte_AsciiCharacter_ReturnsImmediately()
    {
        Assert.That(_decoder.ProcessByte(0x41, out int codePoint), Is.True);
        Assert.That(codePoint, Is.EqualTo(0x41));
    }

    [Test]
    public void ProcessByte_MultiByteSequence_ReturnsWhenComplete()
    {
        // Process 2-byte UTF-8: é (U+00E9) = 0xC3 0xA9
        Assert.That(_decoder.ProcessByte(0xC3, out int codePoint), Is.False); // First byte
        Assert.That(_decoder.ProcessByte(0xA9, out codePoint), Is.True); // Second byte
        Assert.That(codePoint, Is.EqualTo(0x00E9));
    }

    [Test]
    public void ProcessByte_InvalidContinuation_FlushesAndRetries()
    {
        // Start a 2-byte sequence then send invalid continuation
        Assert.That(_decoder.ProcessByte(0xC3, out int codePoint), Is.False); // Start sequence
        Assert.That(_decoder.ProcessByte(0x41, out codePoint), Is.True); // Invalid continuation, should flush
        Assert.That(codePoint, Is.EqualTo(0xC3)); // Should return the invalid start byte
    }

    [Test]
    public void FlushIncompleteSequence_WithIncompleteData_ReturnsInvalidBytes()
    {
        // Start a sequence but don't complete it
        _decoder.ProcessByte(0xC3, out _);

        Assert.That(_decoder.FlushIncompleteSequence(out ReadOnlySpan<byte> invalidBytes), Is.True);
        Assert.That(invalidBytes.Length, Is.EqualTo(1));
        Assert.That(invalidBytes[0], Is.EqualTo(0xC3));
    }

    [Test]
    public void FlushIncompleteSequence_WithNoData_ReturnsFalse()
    {
        Assert.That(_decoder.FlushIncompleteSequence(out ReadOnlySpan<byte> invalidBytes), Is.False);
        Assert.That(invalidBytes.Length, Is.EqualTo(0));
    }

    [Test]
    public void Reset_ClearsInternalState()
    {
        // Start a sequence
        _decoder.ProcessByte(0xC3, out _);

        // Reset should clear state
        _decoder.Reset();

        // Should not have incomplete data after reset
        Assert.That(_decoder.FlushIncompleteSequence(out ReadOnlySpan<byte> invalidBytes), Is.False);
        Assert.That(invalidBytes.Length, Is.EqualTo(0));
    }
}
