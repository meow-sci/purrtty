namespace caTTY.Core.Parsing;

/// <summary>
///     Interface for UTF-8 multi-byte sequence decoding.
///     Provides methods for detecting, validating, and decoding UTF-8 sequences.
/// </summary>
public interface IUtf8Decoder
{
    /// <summary>
    ///     Attempts to decode a UTF-8 sequence from the provided bytes.
    /// </summary>
    /// <param name="bytes">The byte sequence to decode</param>
    /// <param name="codePoint">The decoded Unicode code point if successful</param>
    /// <param name="bytesConsumed">The number of bytes consumed from the input</param>
    /// <returns>True if a complete sequence was decoded successfully</returns>
    bool TryDecodeSequence(ReadOnlySpan<byte> bytes, out int codePoint, out int bytesConsumed);

    /// <summary>
    ///     Checks if a byte is a valid UTF-8 start byte.
    /// </summary>
    /// <param name="b">The byte to check</param>
    /// <returns>True if the byte can start a UTF-8 sequence</returns>
    bool IsValidUtf8Start(byte b);

    /// <summary>
    ///     Gets the expected length of a UTF-8 sequence based on the start byte.
    /// </summary>
    /// <param name="startByte">The first byte of the sequence</param>
    /// <returns>The expected total length (1-4 bytes), or 0 if invalid</returns>
    int GetExpectedLength(byte startByte);

    /// <summary>
    ///     Processes a single byte for UTF-8 decoding.
    ///     Maintains internal state for multi-byte sequences.
    /// </summary>
    /// <param name="b">The byte to process</param>
    /// <param name="codePoint">The decoded code point if a sequence is complete</param>
    /// <returns>True if a complete sequence was decoded</returns>
    bool ProcessByte(byte b, out int codePoint);

    /// <summary>
    ///     Flushes any incomplete UTF-8 sequence and resets the decoder state.
    /// </summary>
    /// <param name="invalidBytes">The bytes from the incomplete sequence</param>
    /// <returns>True if there were incomplete bytes to flush</returns>
    bool FlushIncompleteSequence(out ReadOnlySpan<byte> invalidBytes);

    /// <summary>
    ///     Resets the decoder to its initial state.
    /// </summary>
    void Reset();
}
