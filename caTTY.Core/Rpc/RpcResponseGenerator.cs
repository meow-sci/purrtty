using System.Text;

namespace caTTY.Core.Rpc;

/// <summary>
/// Generates RPC response sequences in the proper terminal escape sequence format.
/// Handles encoding response data and error messages back to the terminal.
/// </summary>
public class RpcResponseGenerator : IRpcResponseGenerator
{
    private const byte ESC = 0x1B;
    private const byte LEFT_BRACKET = 0x5B;
    private const byte GREATER_THAN = 0x3E;
    private const byte SEMICOLON = 0x3B;
    private const byte RESPONSE_CHAR = (byte)'R';
    private const byte ERROR_CHAR = (byte)'E';
    private const int PROTOCOL_VERSION = 1;
    private const int ERROR_COMMAND_ID = 9999;

    /// <summary>
    /// Generates a response sequence for a successful query command.
    /// Uses the format: ESC [ > Pn ; 1 ; R [; additional parameters]
    /// </summary>
    /// <param name="commandId">The original command ID from the query</param>
    /// <param name="data">The response data to encode (can be null for simple acknowledgments)</param>
    /// <returns>The formatted response sequence as bytes</returns>
    public byte[] GenerateResponse(int commandId, object? data)
    {
        var sequence = new List<byte>();
        
        // Add ESC [ > prefix
        sequence.Add(ESC);
        sequence.Add(LEFT_BRACKET);
        sequence.Add(GREATER_THAN);
        
        // Add command ID
        sequence.AddRange(Encoding.ASCII.GetBytes(commandId.ToString()));
        sequence.Add(SEMICOLON);
        
        // Add protocol version
        sequence.AddRange(Encoding.ASCII.GetBytes(PROTOCOL_VERSION.ToString()));
        sequence.Add(SEMICOLON);
        
        // Encode response data as additional parameters
        if (data != null)
        {
            var encodedData = EncodeResponseData(data);
            if (encodedData.Length > 0)
            {
                sequence.AddRange(encodedData);
                sequence.Add(SEMICOLON);
            }
        }
        
        // Add final character
        sequence.Add(RESPONSE_CHAR);
        
        return sequence.ToArray();
    }

    /// <summary>
    /// Generates an error response sequence for a failed command.
    /// Uses the format: ESC [ > 9999 ; 1 ; E [; error details]
    /// </summary>
    /// <param name="commandId">The original command ID that failed</param>
    /// <param name="errorMessage">The error message to include</param>
    /// <returns>The formatted error sequence as bytes</returns>
    public byte[] GenerateError(int commandId, string errorMessage)
    {
        var sequence = new List<byte>();
        
        // Add ESC [ > prefix
        sequence.Add(ESC);
        sequence.Add(LEFT_BRACKET);
        sequence.Add(GREATER_THAN);
        
        // Use error command ID (9999)
        sequence.AddRange(Encoding.ASCII.GetBytes(ERROR_COMMAND_ID.ToString()));
        sequence.Add(SEMICOLON);
        
        // Add protocol version
        sequence.AddRange(Encoding.ASCII.GetBytes(PROTOCOL_VERSION.ToString()));
        sequence.Add(SEMICOLON);
        
        // Add original command ID as parameter
        sequence.AddRange(Encoding.ASCII.GetBytes(commandId.ToString()));
        sequence.Add(SEMICOLON);
        
        // Encode error message if provided
        if (!string.IsNullOrEmpty(errorMessage))
        {
            var encodedError = EncodeErrorMessage(errorMessage);
            if (encodedError.Length > 0)
            {
                sequence.AddRange(encodedError);
                sequence.Add(SEMICOLON);
            }
        }
        
        // Add final character
        sequence.Add(ERROR_CHAR);
        
        return sequence.ToArray();
    }

    /// <summary>
    /// Generates a timeout error response for a query command that exceeded its timeout.
    /// Uses the format: ESC [ > 9999 ; 1 ; E with timeout-specific error details
    /// </summary>
    /// <param name="commandId">The original command ID that timed out</param>
    /// <returns>The formatted timeout error sequence as bytes</returns>
    public byte[] GenerateTimeout(int commandId)
    {
        return GenerateError(commandId, "TIMEOUT");
    }

    /// <summary>
    /// Generates a system error response for general RPC system failures.
    /// Uses the format: ESC [ > 9999 ; 1 ; E with system error details
    /// </summary>
    /// <param name="errorMessage">The system error message</param>
    /// <returns>The formatted system error sequence as bytes</returns>
    public byte[] GenerateSystemError(string errorMessage)
    {
        return GenerateError(0, errorMessage ?? "SYSTEM_ERROR");
    }

    /// <summary>
    /// Sanitizes a string for safe inclusion in terminal escape sequences.
    /// Removes or replaces control characters and other problematic characters.
    /// </summary>
    /// <param name="input">The string to sanitize</param>
    /// <returns>A sanitized string safe for terminal sequences</returns>
    private string SanitizeStringForTerminal(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }
        
        var sanitized = new StringBuilder();
        
        foreach (char c in input)
        {
            // Replace control characters (0x00-0x1F and 0x7F-0x9F) with underscores
            if (c < 0x20 || (c >= 0x7F && c <= 0x9F))
            {
                sanitized.Append('_');
            }
            // Replace semicolons to avoid parameter confusion
            else if (c == ';')
            {
                sanitized.Append('_');
            }
            // Keep printable ASCII characters
            else if (c >= 0x20 && c <= 0x7E)
            {
                sanitized.Append(c);
            }
            // Replace other characters with underscores
            else
            {
                sanitized.Append('_');
            }
        }
        
        var result = sanitized.ToString();
        
        // Limit length to prevent overly long sequences
        if (result.Length > 100)
        {
            result = result.Substring(0, 97) + "...";
        }
        
        return result;
    }

    /// <summary>
    /// Encodes response data into parameters suitable for terminal sequences.
    /// Handles various data types and converts them to numeric parameters.
    /// </summary>
    /// <param name="data">The data to encode</param>
    /// <returns>Encoded data as bytes</returns>
    private byte[] EncodeResponseData(object data)
    {
        var parameters = new List<byte>();
        
        switch (data)
        {
            case null:
                break;
                
            case int intValue:
                parameters.AddRange(Encoding.ASCII.GetBytes(intValue.ToString()));
                break;
                
            case bool boolValue:
                parameters.AddRange(Encoding.ASCII.GetBytes(boolValue ? "1" : "0"));
                break;
                
            case double doubleValue:
                // Convert to integer representation (e.g., 75.5% becomes 755)
                var intRepresentation = (int)(doubleValue * 10);
                parameters.AddRange(Encoding.ASCII.GetBytes(intRepresentation.ToString()));
                break;
                
            case string stringValue:
                // For strings, sanitize and encode length followed by ASCII values
                if (!string.IsNullOrEmpty(stringValue))
                {
                    var sanitizedString = SanitizeStringForTerminal(stringValue);
                    var bytes = Encoding.ASCII.GetBytes(sanitizedString);
                    parameters.AddRange(Encoding.ASCII.GetBytes(bytes.Length.ToString()));
                    parameters.Add(SEMICOLON);
                    parameters.AddRange(bytes);
                }
                break;
                
            case int[] intArray:
                // Encode array length followed by values
                parameters.AddRange(Encoding.ASCII.GetBytes(intArray.Length.ToString()));
                foreach (var value in intArray)
                {
                    parameters.Add(SEMICOLON);
                    parameters.AddRange(Encoding.ASCII.GetBytes(value.ToString()));
                }
                break;
                
            case string[] stringArray:
                // Encode string array with sanitization
                parameters.AddRange(Encoding.ASCII.GetBytes(stringArray.Length.ToString()));
                foreach (var str in stringArray)
                {
                    parameters.Add(SEMICOLON);
                    var sanitizedStr = SanitizeStringForTerminal(str ?? "");
                    var strBytes = Encoding.ASCII.GetBytes(sanitizedStr);
                    parameters.AddRange(Encoding.ASCII.GetBytes(strBytes.Length.ToString()));
                    parameters.Add(SEMICOLON);
                    parameters.AddRange(strBytes);
                }
                break;
                
            case object[] objectArray:
                // Encode array length followed by recursive encoding
                parameters.AddRange(Encoding.ASCII.GetBytes(objectArray.Length.ToString()));
                foreach (var item in objectArray)
                {
                    parameters.Add(SEMICOLON);
                    var encodedItem = EncodeResponseData(item);
                    parameters.AddRange(encodedItem);
                }
                break;
                
            default:
                // For unknown types, convert to string representation and sanitize
                var stringRep = data.ToString() ?? "";
                if (!string.IsNullOrEmpty(stringRep))
                {
                    var sanitizedRep = SanitizeStringForTerminal(stringRep);
                    var bytes = Encoding.ASCII.GetBytes(sanitizedRep);
                    parameters.AddRange(Encoding.ASCII.GetBytes(bytes.Length.ToString()));
                    parameters.Add(SEMICOLON);
                    parameters.AddRange(bytes);
                }
                break;
        }
        
        return parameters.ToArray();
    }

    /// <summary>
    /// Encodes error messages for inclusion in error responses.
    /// Converts error messages to ASCII and handles special characters.
    /// </summary>
    /// <param name="errorMessage">The error message to encode</param>
    /// <returns>Encoded error message as bytes</returns>
    private byte[] EncodeErrorMessage(string errorMessage)
    {
        if (string.IsNullOrEmpty(errorMessage))
        {
            return Array.Empty<byte>();
        }
        
        // Use the comprehensive sanitization method
        var sanitized = SanitizeStringForTerminal(errorMessage);
        
        return Encoding.ASCII.GetBytes(sanitized);
    }
}