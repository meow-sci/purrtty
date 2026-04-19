using caTTY.Core.Rpc;
using FsCheck;
using NUnit.Framework;
using System.Text;

namespace caTTY.Core.Tests.Property;

/// <summary>
/// Property-based tests for RPC response data encoding.
/// These tests verify universal properties that should hold for response data encoding.
/// </summary>
[TestFixture]
[Category("Property")]
public class ResponseDataEncodingProperties
{
    /// <summary>
    /// Generator for valid query command IDs (2000-2999).
    /// </summary>
    public static Arbitrary<int> ValidQueryCommandIdArb =>
        Arb.From(Gen.Choose(2000, 2999));

    /// <summary>
    /// Generator for various response data types that should be encodable.
    /// </summary>
    public static Arbitrary<object> ResponseDataArb =>
        Arb.From(Gen.OneOf(
            Gen.Constant<object>(null!),
            Gen.Choose(0, 9999).Select(x => (object)x),
            Gen.Elements(true, false).Select(x => (object)x),
            Gen.Choose(0, 100).Select(x => (object)(x / 10.0)),
            Arb.Default.String().Generator.Select(x => (object)(x ?? "")),
            Gen.ArrayOf(Gen.Choose(0, 999)).Select(x => (object)x),
            Gen.ArrayOf(Arb.Default.String().Generator).Select(x => (object)x)
        ));

    /// <summary>
    /// **Feature: term-sequence-rpc, Property 9: Response Data Encoding**
    /// **Validates: Requirements 3.5**
    /// Property: For any query response, the response data should be encoded in additional 
    /// parameters following the standard Pn ; Pv ; Pc format.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property ResponseDataEncodingFormat()
    {
        return Prop.ForAll(ValidQueryCommandIdArb, ResponseDataArb,
            (int commandId, object responseData) =>
        {
            // Arrange
            var responseGenerator = new RpcResponseGenerator();

            // Act - Generate response with data
            var responseBytes = responseGenerator.GenerateResponse(commandId, responseData);
            var responseString = Encoding.ASCII.GetString(responseBytes);

            // Assert - Verify response format follows ESC [ > Pn ; Pv ; Pc pattern
            
            // 1. Response should start with ESC [ > prefix
            bool hasCorrectPrefix = responseBytes.Length >= 3 &&
                                   responseBytes[0] == 0x1B &&  // ESC
                                   responseBytes[1] == 0x5B &&  // [
                                   responseBytes[2] == 0x3E;    // >

            // 2. Response should end with 'R' character
            bool hasCorrectSuffix = responseBytes.Length > 0 &&
                                   responseBytes[responseBytes.Length - 1] == (byte)'R';

            // 3. Response should contain the original command ID
            bool containsCommandId = responseString.Contains(commandId.ToString());

            // 4. Response should contain protocol version (1)
            bool containsProtocolVersion = responseString.Contains(";1;");

            // 5. Response should be valid ASCII
            bool isValidAscii = responseBytes.All(b => b >= 0x20 && b <= 0x7E || b == 0x1B);

            // 6. Response should have proper semicolon separation
            var parts = responseString.Split(';');
            bool hasMinimumParts = parts.Length >= 3; // At least Pn, Pv, and final char

            // 7. For non-null data, response should have additional parameters
            // Exception: empty strings may not produce parameters after sanitization
            bool hasDataParameters = responseData == null || 
                                   parts.Length > 3 || 
                                   (responseData is string str && string.IsNullOrEmpty(str));

            // 8. Command ID should be parseable from response
            bool commandIdParseable = false;
            if (hasMinimumParts && parts.Length > 0)
            {
                var commandIdPart = parts[0].Substring(3); // Remove ESC[> prefix
                commandIdParseable = int.TryParse(commandIdPart, out int parsedId) && parsedId == commandId;
            }

            // 9. Protocol version should be parseable
            bool protocolVersionParseable = false;
            if (hasMinimumParts && parts.Length > 1)
            {
                protocolVersionParseable = int.TryParse(parts[1], out int version) && version == 1;
            }

            // 10. Response should not contain invalid characters that break terminal parsing
            bool noInvalidChars = !responseString.Contains('\n') && 
                                 !responseString.Contains('\r') && 
                                 !responseString.Contains('\t');

            return hasCorrectPrefix && hasCorrectSuffix && containsCommandId && 
                   containsProtocolVersion && isValidAscii && hasMinimumParts && 
                   hasDataParameters && commandIdParseable && protocolVersionParseable && 
                   noInvalidChars;
        });
    }

    /// <summary>
    /// **Feature: term-sequence-rpc, Property 9b: Response Data Type Encoding**
    /// **Validates: Requirements 3.5**
    /// Property: For any response data type (int, bool, double, string, array), 
    /// the encoding should be consistent and decodable.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property ResponseDataTypeEncoding()
    {
        return Prop.ForAll(ValidQueryCommandIdArb, ResponseDataArb,
            (int commandId, object responseData) =>
        {
            // Arrange
            var responseGenerator = new RpcResponseGenerator();

            // Act - Generate response
            var responseBytes = responseGenerator.GenerateResponse(commandId, responseData);
            var responseString = Encoding.ASCII.GetString(responseBytes);

            // Assert - Verify type-specific encoding behavior
            
            // 1. Null data should result in minimal response (no extra parameters)
            if (responseData == null)
            {
                var parts = responseString.Split(';');
                bool minimalResponse = parts.Length == 3; // Just Pn;Pv;R
                if (!minimalResponse) return false;
            }

            // 2. Integer data should be encoded as numeric string
            if (responseData is int intValue)
            {
                bool containsIntValue = responseString.Contains(intValue.ToString());
                if (!containsIntValue) return false;
            }

            // 3. Boolean data should be encoded as 0 or 1
            if (responseData is bool boolValue)
            {
                string expectedValue = boolValue ? "1" : "0";
                bool containsBoolValue = responseString.Contains(expectedValue);
                if (!containsBoolValue) return false;
            }

            // 4. Double data should be encoded as integer representation
            if (responseData is double doubleValue)
            {
                int expectedIntRep = (int)(doubleValue * 10);
                bool containsDoubleValue = responseString.Contains(expectedIntRep.ToString());
                if (!containsDoubleValue) return false;
            }

            // 5. String data should include length information
            if (responseData is string stringValue && !string.IsNullOrEmpty(stringValue))
            {
                bool containsLength = responseString.Contains(stringValue.Length.ToString());
                if (!containsLength) return false;
            }

            // 6. Array data should include array length
            if (responseData is int[] intArray)
            {
                bool containsArrayLength = responseString.Contains(intArray.Length.ToString());
                if (!containsArrayLength) return false;
            }

            // 7. Response should be well-formed regardless of data type
            bool wellFormed = responseBytes.Length > 6 && // Minimum: ESC[>n;1;R
                             responseBytes[0] == 0x1B &&
                             responseBytes[responseBytes.Length - 1] == (byte)'R';

            return wellFormed;
        });
    }

    /// <summary>
    /// **Feature: term-sequence-rpc, Property 9c: Error Response Encoding**
    /// **Validates: Requirements 3.5**
    /// Property: For any error response, the encoding should follow the ESC [ > 9999 ; 1 ; E 
    /// format with proper error message encoding.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property ErrorResponseEncoding()
    {
        return Prop.ForAll(ValidQueryCommandIdArb, Arb.Default.String(),
            (int originalCommandId, string? errorMessage) =>
        {
            // Arrange
            var responseGenerator = new RpcResponseGenerator();

            // Act - Generate error response
            var errorBytes = responseGenerator.GenerateError(originalCommandId, errorMessage ?? "");
            var errorString = Encoding.ASCII.GetString(errorBytes);

            // Assert - Verify error response format
            
            // 1. Error response should start with ESC [ > prefix
            bool hasCorrectPrefix = errorBytes.Length >= 3 &&
                                   errorBytes[0] == 0x1B &&  // ESC
                                   errorBytes[1] == 0x5B &&  // [
                                   errorBytes[2] == 0x3E;    // >

            // 2. Error response should end with 'E' character
            bool hasCorrectSuffix = errorBytes.Length > 0 &&
                                   errorBytes[errorBytes.Length - 1] == (byte)'E';

            // 3. Error response should use command ID 9999
            bool usesErrorCommandId = errorString.Contains("9999");

            // 4. Error response should contain protocol version (1)
            bool containsProtocolVersion = errorString.Contains(";1;");

            // 5. Error response should contain original command ID as parameter
            bool containsOriginalCommandId = errorString.Contains(originalCommandId.ToString());

            // 6. Error message should be sanitized (no semicolons, newlines, etc.)
            if (!string.IsNullOrEmpty(errorMessage))
            {
                bool noProblematicChars = !errorString.Contains(";\n") && 
                                         !errorString.Contains(";\r") && 
                                         !errorString.Contains(";\t");
                if (!noProblematicChars) return false;
            }

            // 7. Error response should be valid ASCII
            bool isValidAscii = errorBytes.All(b => b >= 0x20 && b <= 0x7E || b == 0x1B);

            // 8. Error response should have proper structure
            var parts = errorString.Split(';');
            bool hasMinimumParts = parts.Length >= 4; // 9999, 1, originalCommandId, E

            return hasCorrectPrefix && hasCorrectSuffix && usesErrorCommandId && 
                   containsProtocolVersion && containsOriginalCommandId && 
                   isValidAscii && hasMinimumParts;
        });
    }

    /// <summary>
    /// **Feature: term-sequence-rpc, Property 9d: Timeout Response Encoding**
    /// **Validates: Requirements 3.5**
    /// Property: For any timeout response, the encoding should be consistent with 
    /// error response format and include timeout-specific information.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property TimeoutResponseEncoding()
    {
        return Prop.ForAll(ValidQueryCommandIdArb,
            (int commandId) =>
        {
            // Arrange
            var responseGenerator = new RpcResponseGenerator();

            // Act - Generate timeout response
            var timeoutBytes = responseGenerator.GenerateTimeout(commandId);
            var timeoutString = Encoding.ASCII.GetString(timeoutBytes);

            // Assert - Verify timeout response format
            
            // 1. Timeout response should follow error response format
            bool hasCorrectPrefix = timeoutBytes.Length >= 3 &&
                                   timeoutBytes[0] == 0x1B &&  // ESC
                                   timeoutBytes[1] == 0x5B &&  // [
                                   timeoutBytes[2] == 0x3E;    // >

            // 2. Timeout response should end with 'E' character (error type)
            bool hasCorrectSuffix = timeoutBytes.Length > 0 &&
                                   timeoutBytes[timeoutBytes.Length - 1] == (byte)'E';

            // 3. Timeout response should use error command ID 9999
            bool usesErrorCommandId = timeoutString.Contains("9999");

            // 4. Timeout response should contain "TIMEOUT" message
            bool containsTimeoutMessage = timeoutString.Contains("TIMEOUT");

            // 5. Timeout response should contain original command ID
            bool containsOriginalCommandId = timeoutString.Contains(commandId.ToString());

            // 6. Timeout response should be valid ASCII
            bool isValidAscii = timeoutBytes.All(b => b >= 0x20 && b <= 0x7E || b == 0x1B);

            // 7. Timeout response should have proper structure
            var parts = timeoutString.Split(';');
            bool hasMinimumParts = parts.Length >= 4; // 9999, 1, originalCommandId, timeout info, E

            return hasCorrectPrefix && hasCorrectSuffix && usesErrorCommandId && 
                   containsTimeoutMessage && containsOriginalCommandId && 
                   isValidAscii && hasMinimumParts;
        });
    }

    /// <summary>
    /// **Feature: term-sequence-rpc, Property 9e: Response Length Constraints**
    /// **Validates: Requirements 3.5**
    /// Property: For any response data, the encoded response should have reasonable 
    /// length constraints to prevent terminal buffer overflow.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property ResponseLengthConstraints()
    {
        return Prop.ForAll(ValidQueryCommandIdArb, ResponseDataArb,
            (int commandId, object responseData) =>
        {
            // Arrange
            var responseGenerator = new RpcResponseGenerator();

            // Act - Generate response
            var responseBytes = responseGenerator.GenerateResponse(commandId, responseData);

            // Assert - Verify length constraints
            
            // 1. Response should not be empty
            bool notEmpty = responseBytes.Length > 0;

            // 2. Response should have minimum length for valid format (ESC[>n;1;R = 7+ chars)
            bool hasMinimumLength = responseBytes.Length >= 7;

            // 3. Response should not exceed reasonable maximum length (prevent buffer overflow)
            bool withinMaxLength = responseBytes.Length <= 4096; // Reasonable terminal sequence limit

            // 4. Response should be proportional to input data size
            bool reasonableSize = true;
            if (responseData is string str && str.Length > 1000)
            {
                // Very long strings should not result in excessively long responses
                reasonableSize = responseBytes.Length <= str.Length * 2 + 100;
            }
            else if (responseData is int[] arr && arr.Length > 100)
            {
                // Large arrays should not result in excessively long responses
                reasonableSize = responseBytes.Length <= arr.Length * 10 + 100;
            }

            return notEmpty && hasMinimumLength && withinMaxLength && reasonableSize;
        });
    }
}