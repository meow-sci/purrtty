using System.Text;

namespace caTTY.Core.Rpc;

/// <summary>
/// Detects and classifies RPC sequences in terminal input.
/// Handles private use area sequences with ESC [ > format.
/// </summary>
public class RpcSequenceDetector : IRpcSequenceDetector
{
    private const byte EscapeByte = 0x1B;
    private const byte LeftBracketByte = 0x5B;
    private const byte GreaterThanByte = 0x3E;

    private const int MinCommandId = 1000;
    private const int MaxCommandId = 9999;

    private const byte MinFinalCharacter = 0x40;
    private const byte MaxFinalCharacter = 0x7E;

    /// <summary>
    /// Determines if the given sequence is an RPC sequence.
    /// Checks for the ESC [ > prefix and basic format validation.
    /// </summary>
    /// <param name="sequence">The byte sequence to examine</param>
    /// <returns>True if this appears to be an RPC sequence</returns>
    public bool IsRpcSequence(ReadOnlySpan<byte> sequence)
    {
        // Minimum sequence: ESC [ > Pn ; Pv ; Pc (at least 8 bytes: ESC[>1000;1;F)
        if (sequence.Length < 8)
        {
            return false;
        }

        // Check for ESC [ > prefix
        var matched = sequence[0] == EscapeByte &&
               sequence[1] == LeftBracketByte &&
               sequence[2] == GreaterThanByte;
        return matched;
    }

    /// <summary>
    /// Gets the type of RPC sequence (valid, malformed, etc.).
    /// Performs more detailed validation than IsRpcSequence.
    /// </summary>
    /// <param name="sequence">The byte sequence to classify</param>
    /// <returns>The RPC sequence type classification</returns>
    public RpcSequenceType GetSequenceType(ReadOnlySpan<byte> sequence)
    {
        // First check if it's even an RPC sequence
        if (!IsRpcSequence(sequence))
        {
            return RpcSequenceType.None;
        }

        // Extract the parameter portion (skip ESC [ >)
        var parameterSpan = sequence[3..];

        // Convert to string for parsing
        string parameterString;
        try
        {
            parameterString = Encoding.ASCII.GetString(parameterSpan);
        }
        catch
        {
            return RpcSequenceType.Malformed;
        }

        // Parse the parameters: Pn ; Pv ; Pc
        if (!TryParseRpcParameters(parameterString, out int commandId, out int version, out char finalChar))
        {
            return RpcSequenceType.Malformed;
        }

        // Validate command ID range
        if (!IsValidCommandId(commandId))
        {
            return RpcSequenceType.InvalidCommandId;
        }

        // Validate final character
        if (!IsValidFinalCharacter((byte)finalChar))
        {
            return RpcSequenceType.InvalidFinalCharacter;
        }

        return RpcSequenceType.Valid;
    }

    /// <summary>
    /// Validates that the final character is in the valid range (0x40-0x7E).
    /// </summary>
    /// <param name="finalChar">The final character to validate</param>
    /// <returns>True if the final character is valid for private use area</returns>
    public bool IsValidFinalCharacter(byte finalChar)
    {
        return finalChar >= MinFinalCharacter && finalChar <= MaxFinalCharacter;
    }

    /// <summary>
    /// Validates that the command ID is in the valid range (1000-9999).
    /// </summary>
    /// <param name="commandId">The command ID to validate</param>
    /// <returns>True if the command ID is in the valid range</returns>
    public bool IsValidCommandId(int commandId)
    {
        return commandId >= MinCommandId && commandId <= MaxCommandId;
    }

    /// <summary>
    /// Attempts to parse RPC parameters from the parameter string.
    /// Expected format: Pn ; Pv ; Pc (e.g., "1001;1;F")
    /// </summary>
    /// <param name="parameterString">The parameter string to parse</param>
    /// <param name="commandId">The parsed command ID (Pn)</param>
    /// <param name="version">The parsed version (Pv)</param>
    /// <param name="finalChar">The parsed final character (Pc)</param>
    /// <returns>True if parsing was successful</returns>
    private bool TryParseRpcParameters(string parameterString, out int commandId, out int version, out char finalChar)
    {
        commandId = 0;
        version = 0;
        finalChar = '\0';

        if (string.IsNullOrEmpty(parameterString))
        {
            return false;
        }

        // Find the final character (last character in the sequence)
        finalChar = parameterString[^1];

        // Remove the final character to get the parameter portion
        string paramPortion = parameterString[..^1];

        // Split by semicolon to get Pn and Pv
        string[] parts = paramPortion.Split(';');

        // We need at least Pn ; Pv
        if (parts.Length < 2)
        {
            return false;
        }

        // Parse command ID (Pn)
        if (!int.TryParse(parts[0], out commandId))
        {
            return false;
        }

        // Parse version (Pv)
        if (!int.TryParse(parts[1], out version))
        {
            return false;
        }

        return true;
    }
}
