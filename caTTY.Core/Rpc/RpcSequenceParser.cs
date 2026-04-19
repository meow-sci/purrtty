using System.Text;

namespace caTTY.Core.Rpc;

/// <summary>
/// Parses RPC sequences into structured messages.
/// Handles the ESC [ > Pn ; Pv ; Pc format parsing with graceful error handling.
/// </summary>
public class RpcSequenceParser : IRpcSequenceParser
{
    private const byte EscapeByte = 0x1B;
    private const byte LeftBracketByte = 0x5B;
    private const byte GreaterThanByte = 0x3E;
    
    private const int MinCommandId = 1000;
    private const int MaxCommandId = 9999;
    private const int ExpectedVersion = 1;
    
    private const byte MinFinalCharacter = 0x40;
    private const byte MaxFinalCharacter = 0x7E;

    /// <summary>
    /// Attempts to parse a complete RPC sequence into an RpcMessage.
    /// </summary>
    /// <param name="sequence">The complete RPC sequence bytes (including ESC [ >)</param>
    /// <param name="message">The parsed RPC message if successful</param>
    /// <returns>True if parsing was successful</returns>
    public bool TryParseRpcSequence(ReadOnlySpan<byte> sequence, out RpcMessage? message)
    {
        message = null;

        // Validate minimum length and prefix
        if (!IsValidRpcPrefix(sequence))
        {
            return false;
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
            return false;
        }

        // Parse the parameters
        if (!TryParseParameters(parameterString, out var parameters))
        {
            return false;
        }

        // Extract command ID and version from parameters
        if (!TryExtractCommandId(parameterString, out int commandId) ||
            !TryExtractVersion(parameterString, out int version))
        {
            return false;
        }

        // Get the final character and determine command type
        if (parameterString.Length == 0)
        {
            return false;
        }

        byte finalChar = (byte)parameterString[^1];
        if (!TryGetCommandType(finalChar, out var commandType))
        {
            return false;
        }

        // Validate command ID range
        if (!IsValidCommandId(commandId))
        {
            return false;
        }

        // Create the RPC message
        message = new RpcMessage
        {
            CommandId = commandId,
            Version = version,
            CommandType = commandType,
            Parameters = parameters ?? new RpcParameters(),
            Raw = Encoding.ASCII.GetString(sequence)
        };

        return true;
    }

    /// <summary>
    /// Attempts to parse RPC parameters from a parameter string.
    /// Extracts Pn (command ID), Pv (version), and additional parameters.
    /// </summary>
    /// <param name="parameterString">The parameter portion of the RPC sequence</param>
    /// <param name="parameters">The parsed RPC parameters if successful</param>
    /// <returns>True if parsing was successful</returns>
    public bool TryParseParameters(ReadOnlySpan<char> parameterString, out RpcParameters? parameters)
    {
        parameters = null;

        if (parameterString.IsEmpty)
        {
            return false;
        }

        // Remove the final character to get the parameter portion
        var paramPortion = parameterString[..^1].ToString();
        
        // Split by semicolon to get parameters
        var parts = paramPortion.Split(';');
        
        // We need at least 2 parts (command ID and version)
        if (parts.Length < 2)
        {
            return false;
        }

        var parameterList = new List<int>();
        
        // Parse command ID (first part) - handle gracefully
        if (!int.TryParse(parts[0], out int commandId))
        {
            // Treat invalid command ID as 0 for graceful handling
            commandId = 0;
        }
        parameterList.Add(commandId);
        
        // Parse version (second part) - handle gracefully
        if (!int.TryParse(parts[1], out int version))
        {
            // Invalid version - treat as 0 for graceful handling
            version = 0;
        }
        parameterList.Add(version);
        
        // Parse remaining parts as numeric parameters
        for (int i = 2; i < parts.Length; i++)
        {
            if (int.TryParse(parts[i], out int value))
            {
                parameterList.Add(value);
            }
            else
            {
                // Invalid numeric parameter - treat as 0 for graceful handling
                parameterList.Add(0);
            }
        }

        parameters = new RpcParameters
        {
            NumericParameters = parameterList.ToArray(),
            StringParameters = Array.Empty<string>(),
            ExtendedParameters = new Dictionary<string, object>()
        };

        return true;
    }

    /// <summary>
    /// Extracts the command ID (Pn) from the parameter string.
    /// </summary>
    /// <param name="parameterString">The parameter string</param>
    /// <param name="commandId">The extracted command ID</param>
    /// <returns>True if command ID was successfully extracted</returns>
    public bool TryExtractCommandId(ReadOnlySpan<char> parameterString, out int commandId)
    {
        commandId = 0;

        if (parameterString.IsEmpty)
        {
            return false;
        }

        // Find the first semicolon to get the command ID
        int semicolonIndex = parameterString.IndexOf(';');
        if (semicolonIndex <= 0)
        {
            return false;
        }

        var commandIdSpan = parameterString[..semicolonIndex];
        return int.TryParse(commandIdSpan, out commandId);
    }

    /// <summary>
    /// Extracts the version (Pv) from the parameter string.
    /// </summary>
    /// <param name="parameterString">The parameter string</param>
    /// <param name="version">The extracted version</param>
    /// <returns>True if version was successfully extracted</returns>
    public bool TryExtractVersion(ReadOnlySpan<char> parameterString, out int version)
    {
        version = 0;

        if (parameterString.IsEmpty)
        {
            return false;
        }

        // Find the first and second semicolons to get the version
        int firstSemicolon = parameterString.IndexOf(';');
        if (firstSemicolon <= 0 || firstSemicolon >= parameterString.Length - 1)
        {
            return false;
        }

        var remainingSpan = parameterString[(firstSemicolon + 1)..];
        int secondSemicolon = remainingSpan.IndexOf(';');
        
        ReadOnlySpan<char> versionSpan;
        if (secondSemicolon > 0)
        {
            // There are more parameters after version
            versionSpan = remainingSpan[..secondSemicolon];
        }
        else
        {
            // Version is the last parameter before final character
            // Remove the final character
            versionSpan = remainingSpan[..^1];
        }

        return int.TryParse(versionSpan, out version);
    }

    /// <summary>
    /// Determines the command type from the final character.
    /// </summary>
    /// <param name="finalChar">The final character of the sequence</param>
    /// <param name="commandType">The determined command type</param>
    /// <returns>True if the final character maps to a valid command type</returns>
    public bool TryGetCommandType(byte finalChar, out RpcCommandType commandType)
    {
        commandType = default;

        // Validate final character is in private use area range
        if (finalChar < MinFinalCharacter || finalChar > MaxFinalCharacter)
        {
            return false;
        }

        // Map final character to command type
        commandType = (char)finalChar switch
        {
            'F' => RpcCommandType.FireAndForget,
            'Q' => RpcCommandType.Query,
            'R' => RpcCommandType.Response,
            'E' => RpcCommandType.Error,
            _ => default
        };

        // Check if we found a valid mapping
        return commandType != default;
    }

    /// <summary>
    /// Validates that the sequence has the correct RPC prefix (ESC [ >).
    /// </summary>
    /// <param name="sequence">The sequence to validate</param>
    /// <returns>True if the sequence has a valid RPC prefix</returns>
    private bool IsValidRpcPrefix(ReadOnlySpan<byte> sequence)
    {
        // Minimum sequence: ESC [ > Pn ; Pv ; Pc (at least 8 bytes: ESC[>1000;1;F)
        if (sequence.Length < 8)
        {
            return false;
        }

        // Check for ESC [ > prefix
        return sequence[0] == EscapeByte && 
               sequence[1] == LeftBracketByte && 
               sequence[2] == GreaterThanByte;
    }

    /// <summary>
    /// Validates that the command ID is in the valid range (1000-9999).
    /// </summary>
    /// <param name="commandId">The command ID to validate</param>
    /// <returns>True if the command ID is in the valid range</returns>
    private bool IsValidCommandId(int commandId)
    {
        return commandId >= MinCommandId && commandId <= MaxCommandId;
    }
}