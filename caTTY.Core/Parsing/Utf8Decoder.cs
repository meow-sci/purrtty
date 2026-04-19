using System.Text;

namespace caTTY.Core.Parsing;

/// <summary>
///     UTF-8 multi-byte sequence decoder with state management.
///     Handles UTF-8 validation, decoding, and error recovery.
/// </summary>
public class Utf8Decoder : IUtf8Decoder
{
    private readonly List<byte> _buffer = new();
    private int _expectedLength;

    /// <summary>
    ///     Attempts to decode a UTF-8 sequence from the provided bytes.
    /// </summary>
    /// <param name="bytes">The byte sequence to decode</param>
    /// <param name="codePoint">The decoded Unicode code point if successful</param>
    /// <param name="bytesConsumed">The number of bytes consumed from the input</param>
    /// <returns>True if a complete sequence was decoded successfully</returns>
    public bool TryDecodeSequence(ReadOnlySpan<byte> bytes, out int codePoint, out int bytesConsumed)
    {
        codePoint = 0;
        bytesConsumed = 0;

        if (bytes.IsEmpty)
        {
            return false;
        }

        // Single ASCII byte
        if (bytes[0] < 0x80)
        {
            codePoint = bytes[0];
            bytesConsumed = 1;
            return true;
        }

        // Determine expected length from first byte
        int expectedLength = GetExpectedLength(bytes[0]);
        if (expectedLength == 0 || bytes.Length < expectedLength)
        {
            return false;
        }

        // Validate all continuation bytes
        for (int i = 1; i < expectedLength; i++)
        {
            if ((bytes[i] & 0xC0) != 0x80)
            {
                return false;
            }
        }

        // Decode the sequence
        try
        {
            byte[] utf8Array = bytes.Slice(0, expectedLength).ToArray();
            string decoded = Encoding.UTF8.GetString(utf8Array);

            if (decoded.Length > 0)
            {
                codePoint = char.ConvertToUtf32(decoded, 0);
                bytesConsumed = expectedLength;
                return true;
            }
        }
        catch
        {
            // Decoding failed
        }

        return false;
    }

    /// <summary>
    ///     Checks if a byte is a valid UTF-8 start byte.
    /// </summary>
    /// <param name="b">The byte to check</param>
    /// <returns>True if the byte can start a UTF-8 sequence</returns>
    public bool IsValidUtf8Start(byte b)
    {
        // ASCII (0x00-0x7F)
        if (b < 0x80)
        {
            return true;
        }

        // 2-byte sequence start (0xC2-0xDF)
        if ((b & 0xE0) == 0xC0)
        {
            return b >= 0xC2; // 0xC0 and 0xC1 are invalid (overlong encodings)
        }

        // 3-byte sequence start (0xE0-0xEF)
        if ((b & 0xF0) == 0xE0)
        {
            return true;
        }

        // 4-byte sequence start (0xF0-0xF4)
        if ((b & 0xF8) == 0xF0)
        {
            return b <= 0xF4; // 0xF5-0xFF are invalid (beyond Unicode range)
        }

        return false;
    }

    /// <summary>
    ///     Gets the expected length of a UTF-8 sequence based on the start byte.
    /// </summary>
    /// <param name="startByte">The first byte of the sequence</param>
    /// <returns>The expected total length (1-4 bytes), or 0 if invalid</returns>
    public int GetExpectedLength(byte startByte)
    {
        // ASCII (0x00-0x7F)
        if (startByte < 0x80)
        {
            return 1;
        }

        // 2-byte sequence (0xC2-0xDF)
        if ((startByte & 0xE0) == 0xC0)
        {
            return startByte >= 0xC2 ? 2 : 0; // 0xC0 and 0xC1 are invalid
        }

        // 3-byte sequence (0xE0-0xEF)
        if ((startByte & 0xF0) == 0xE0)
        {
            return 3;
        }

        // 4-byte sequence (0xF0-0xF4)
        if ((startByte & 0xF8) == 0xF0)
        {
            return startByte <= 0xF4 ? 4 : 0; // 0xF5-0xFF are invalid
        }

        return 0; // Invalid start byte
    }

    /// <summary>
    ///     Processes a single byte for UTF-8 decoding.
    ///     Maintains internal state for multi-byte sequences.
    /// </summary>
    /// <param name="b">The byte to process</param>
    /// <param name="codePoint">The decoded code point if a sequence is complete</param>
    /// <returns>True if a complete sequence was decoded</returns>
    public bool ProcessByte(byte b, out int codePoint)
    {
        codePoint = 0;

        // If we're not in a UTF-8 sequence and this is ASCII, handle immediately
        if (_expectedLength == 0 && b < 0x80)
        {
            codePoint = b;
            return true;
        }

        // Start of a new UTF-8 sequence
        if (_expectedLength == 0)
        {
            _expectedLength = GetExpectedLength(b);
            if (_expectedLength == 0)
            {
                // Invalid start byte, treat as single byte
                codePoint = b;
                return true;
            }

            _buffer.Clear();
            _buffer.Add(b);
            return false; // Need more bytes
        }

        // Continuation byte in UTF-8 sequence
        if ((b & 0xC0) != 0x80)
        {
            // Invalid continuation byte, flush buffer and start over
            byte[] invalidBytes = _buffer.ToArray();
            Reset();

            // Emit each invalid byte as a separate character
            if (invalidBytes.Length > 0)
            {
                codePoint = invalidBytes[0];

                // Re-add remaining bytes to buffer for next processing
                for (int i = 1; i < invalidBytes.Length; i++)
                {
                    _buffer.Add(invalidBytes[i]);
                }

                return true;
            }

            // Retry with this byte
            return ProcessByte(b, out codePoint);
        }

        _buffer.Add(b);

        // Check if we have a complete UTF-8 sequence
        if (_buffer.Count == _expectedLength)
        {
            return DecodeCompleteSequence(out codePoint);
        }

        return false; // Need more bytes
    }

    /// <summary>
    ///     Flushes any incomplete UTF-8 sequence and resets the decoder state.
    /// </summary>
    /// <param name="invalidBytes">The bytes from the incomplete sequence</param>
    /// <returns>True if there were incomplete bytes to flush</returns>
    public bool FlushIncompleteSequence(out ReadOnlySpan<byte> invalidBytes)
    {
        if (_buffer.Count > 0)
        {
            invalidBytes = _buffer.ToArray().AsSpan();
            Reset();
            return true;
        }

        invalidBytes = ReadOnlySpan<byte>.Empty;
        return false;
    }

    /// <summary>
    ///     Resets the decoder to its initial state.
    /// </summary>
    public void Reset()
    {
        _buffer.Clear();
        _expectedLength = 0;
    }

    /// <summary>
    ///     Decodes a complete UTF-8 sequence from the internal buffer.
    /// </summary>
    /// <param name="codePoint">The decoded code point</param>
    /// <returns>True if decoding was successful</returns>
    private bool DecodeCompleteSequence(out int codePoint)
    {
        codePoint = 0;

        try
        {
            byte[] utf8Array = _buffer.ToArray();
            string decoded = Encoding.UTF8.GetString(utf8Array);

            if (decoded.Length > 0)
            {
                codePoint = char.ConvertToUtf32(decoded, 0);
                Reset();
                return true;
            }
        }
        catch
        {
            // Decoding failed, treat first byte as single character
            if (_buffer.Count > 0)
            {
                codePoint = _buffer[0];
                Reset();
                return true;
            }
        }

        Reset();
        return false;
    }
}
