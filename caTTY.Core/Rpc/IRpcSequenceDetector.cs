namespace caTTY.Core.Rpc;

/// <summary>
/// Interface for detecting RPC sequences in terminal input.
/// Responsible for identifying private use area sequences with RPC format.
/// </summary>
public interface IRpcSequenceDetector
{
    /// <summary>
    /// Determines if the given sequence is an RPC sequence.
    /// Checks for the ESC [ > prefix and basic format validation.
    /// </summary>
    /// <param name="sequence">The byte sequence to examine</param>
    /// <returns>True if this appears to be an RPC sequence</returns>
    bool IsRpcSequence(ReadOnlySpan<byte> sequence);

    /// <summary>
    /// Gets the type of RPC sequence (valid, malformed, etc.).
    /// Performs more detailed validation than IsRpcSequence.
    /// </summary>
    /// <param name="sequence">The byte sequence to classify</param>
    /// <returns>The RPC sequence type classification</returns>
    RpcSequenceType GetSequenceType(ReadOnlySpan<byte> sequence);

    /// <summary>
    /// Validates that the final character is in the valid range (0x40-0x7E).
    /// </summary>
    /// <param name="finalChar">The final character to validate</param>
    /// <returns>True if the final character is valid for private use area</returns>
    bool IsValidFinalCharacter(byte finalChar);

    /// <summary>
    /// Validates that the command ID is in the valid range (1000-9999).
    /// </summary>
    /// <param name="commandId">The command ID to validate</param>
    /// <returns>True if the command ID is in the valid range</returns>
    bool IsValidCommandId(int commandId);
}