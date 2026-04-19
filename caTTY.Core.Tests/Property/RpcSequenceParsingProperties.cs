using System.Text;
using caTTY.Core.Rpc;
using FsCheck;
using NUnit.Framework;

namespace caTTY.Core.Tests.Property;

/// <summary>
/// Property-based tests for RPC sequence parsing functionality.
/// These tests verify universal properties that should hold for all RPC sequence parsing operations.
/// </summary>
[TestFixture]
[Category("Property")]
public class RpcSequenceParsingProperties
{
    private RpcSequenceParser _parser = null!;

    [SetUp]
    public void SetUp()
    {
        _parser = new RpcSequenceParser();
    }

    /// <summary>
    /// Generator for valid command IDs in the RPC range (1000-9999).
    /// </summary>
    public static Arbitrary<int> ValidCommandIdArb =>
        Arb.From(Gen.OneOf(
            Gen.Choose(1000, 1999), // Fire-and-forget range
            Gen.Choose(2000, 2999), // Query range
            Gen.Choose(3000, 8999), // Reserved range
            Gen.Choose(9000, 9999)  // System/error range
        ));

    /// <summary>
    /// Generator for valid final characters (F, Q, R, E).
    /// </summary>
    public static Arbitrary<char> ValidFinalCharArb =>
        Arb.From(Gen.Elements('F', 'Q', 'R', 'E'));

    /// <summary>
    /// Generator for valid RPC sequences in ESC [ > Pn ; Pv ; Pc format.
    /// </summary>
    public static Arbitrary<byte[]> ValidRpcSequenceArb =>
        Arb.From(ValidCommandIdArb.Generator.SelectMany(commandId =>
            ValidFinalCharArb.Generator.Select(finalChar =>
            {
                string sequence = $"\x1b[>{commandId};1;{finalChar}";
                return Encoding.ASCII.GetBytes(sequence);
            })));

    /// <summary>
    /// Generator for valid RPC sequences with additional parameters.
    /// </summary>
    public static Arbitrary<byte[]> ValidRpcSequenceWithParamsArb =>
        Arb.From(ValidCommandIdArb.Generator.SelectMany(commandId =>
            ValidFinalCharArb.Generator.SelectMany(finalChar =>
                Gen.ArrayOf(Gen.Choose(0, 9999)).Select(additionalParams =>
                {
                    var paramString = $"{commandId};1";
                    if (additionalParams != null && additionalParams.Length > 0)
                    {
                        paramString += ";" + string.Join(";", additionalParams);
                    }
                    string sequence = $"\x1b[>{paramString};{finalChar}";
                    return Encoding.ASCII.GetBytes(sequence);
                }))));

    /// <summary>
    /// Generator for invalid command IDs (outside 1000-9999 range).
    /// </summary>
    public static Arbitrary<int> InvalidCommandIdArb =>
        Arb.From(Gen.OneOf(
            Gen.Choose(int.MinValue, 999),
            Gen.Choose(10000, int.MaxValue)
        ));

    /// <summary>
    /// Generator for invalid final characters (not F, Q, R, E).
    /// </summary>
    public static Arbitrary<char> InvalidFinalCharArb =>
        Arb.From(Gen.Elements('A', 'B', 'C', 'D', 'G', 'H', 'X', 'Y', 'Z', '1', '2', '3'));

    /// <summary>
    /// **Feature: term-sequence-rpc, Property 1: Private Use Area Sequence Format Validation**
    /// **Validates: Requirements 1.2, 1.6, 2.6, 3.6**
    /// Property: For any valid RPC sequence following ESC [ > Pn ; Pv ; Pc format where 
    /// Pn is in valid command ranges (1000-9999), Pv is 1, and Pc is a valid final character 
    /// (F, Q, R, E) in the range 0x40-0x7E, the parser should successfully parse it.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property ValidRpcSequenceParsingSuccess()
    {
        return Prop.ForAll(ValidRpcSequenceArb, (byte[] sequence) =>
        {
            // Act
            bool parseResult = _parser.TryParseRpcSequence(sequence, out RpcMessage? message);

            // Assert - Valid sequences should always parse successfully
            if (!parseResult || message == null)
                return false;

            // Validate the parsed message structure
            bool validCommandIdRange = message.CommandId >= 1000 && message.CommandId <= 9999;
            bool validVersion = message.Version == 1;
            bool validCommandType = Enum.IsDefined(typeof(RpcCommandType), message.CommandType);
            bool validFinalCharacter = IsValidFinalCharacter(message.CommandType);
            bool rawPreserved = !string.IsNullOrEmpty(message.Raw);

            return validCommandIdRange && validVersion && validCommandType && 
                   validFinalCharacter && rawPreserved;
        });
    }

    /// <summary>
    /// **Feature: term-sequence-rpc, Property 1b: Invalid Command ID Rejection**
    /// **Validates: Requirements 1.2, 1.6**
    /// Property: For any RPC sequence with command ID outside the valid range (1000-9999),
    /// the parser should reject it.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property InvalidCommandIdRejection()
    {
        return Prop.ForAll(InvalidCommandIdArb, ValidFinalCharArb, 
            (int invalidCommandId, char finalChar) =>
        {
            // Arrange - Create sequence with invalid command ID
            string sequenceString = $"\x1b[>{invalidCommandId};1;{finalChar}";
            byte[] sequence = Encoding.ASCII.GetBytes(sequenceString);

            // Act
            bool parseResult = _parser.TryParseRpcSequence(sequence, out RpcMessage? message);

            // Assert - Invalid command IDs should be rejected
            return !parseResult && message == null;
        });
    }

    /// <summary>
    /// **Feature: term-sequence-rpc, Property 1c: Invalid Final Character Rejection**
    /// **Validates: Requirements 1.6, 2.6, 3.6**
    /// Property: For any RPC sequence with invalid final character (not F, Q, R, E),
    /// the parser should reject it.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property InvalidFinalCharacterRejection()
    {
        return Prop.ForAll(ValidCommandIdArb, InvalidFinalCharArb, 
            (int commandId, char invalidFinalChar) =>
        {
            // Arrange - Create sequence with invalid final character
            string sequenceString = $"\x1b[>{commandId};1;{invalidFinalChar}";
            byte[] sequence = Encoding.ASCII.GetBytes(sequenceString);

            // Act
            bool parseResult = _parser.TryParseRpcSequence(sequence, out RpcMessage? message);

            // Assert - Invalid final characters should be rejected
            return !parseResult && message == null;
        });
    }

    /// <summary>
    /// **Feature: term-sequence-rpc, Property 1d: Malformed Sequence Rejection**
    /// **Validates: Requirements 1.2, 1.3**
    /// Property: For any malformed RPC sequence (wrong prefix, missing parameters, etc.),
    /// the parser should reject it gracefully.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property MalformedSequenceRejection()
    {
        return Prop.ForAll(ValidCommandIdArb, ValidFinalCharArb, 
            (int commandId, char finalChar) =>
        {
            // Test various malformed sequences
            var malformedSequences = new[]
            {
                // Wrong prefix (standard CSI instead of private use area)
                $"\x1b[{commandId};1;{finalChar}",
                // Missing version parameter
                $"\x1b[>{commandId};{finalChar}",
                // Missing command ID
                $"\x1b[>;1;{finalChar}",
                // Too short
                $"\x1b[>",
                // No parameters at all
                $"\x1b[>{finalChar}",
                // Invalid prefix
                $"\x1b]{commandId};1;{finalChar}"
            };

            foreach (string malformedString in malformedSequences)
            {
                byte[] sequence = Encoding.ASCII.GetBytes(malformedString);
                
                // Act
                bool parseResult = _parser.TryParseRpcSequence(sequence, out RpcMessage? message);
                
                // Assert - All malformed sequences should be rejected
                if (parseResult || message != null)
                    return false;
            }

            return true;
        });
    }

    /// <summary>
    /// **Feature: term-sequence-rpc, Property 1e: Additional Parameters Preservation**
    /// **Validates: Requirements 1.2**
    /// Property: For any valid RPC sequence with additional parameters, the parser should
    /// preserve all parameters correctly.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property AdditionalParametersPreservation()
    {
        return Prop.ForAll(ValidRpcSequenceWithParamsArb, (byte[] sequence) =>
        {
            // Act
            bool parseResult = _parser.TryParseRpcSequence(sequence, out RpcMessage? message);

            // Assert - Should parse successfully and preserve parameters
            if (!parseResult || message == null)
                return false;

            // Validate that parameters are preserved
            bool hasParameters = message.Parameters != null;
            bool hasNumericParameters = message.Parameters?.NumericParameters != null;
            
            // At minimum should have command ID and version
            bool hasMinimumParameters = message.Parameters?.NumericParameters?.Length >= 2;

            // First parameter should be command ID, second should be version
            bool correctCommandId = message.Parameters?.NumericParameters?[0] == message.CommandId;
            bool correctVersion = message.Parameters?.NumericParameters?[1] == message.Version;

            return hasParameters && hasNumericParameters && hasMinimumParameters && 
                   correctCommandId && correctVersion;
        });
    }

    /// <summary>
    /// **Feature: term-sequence-rpc, Property 1f: Graceful Error Handling**
    /// **Validates: Requirements 1.3**
    /// Property: For any sequence that causes parsing errors (invalid encoding, etc.),
    /// the parser should handle them gracefully without throwing exceptions.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property GracefulErrorHandling()
    {
        return Prop.ForAll(Arb.Default.Array<byte>(), (byte[] randomBytes) =>
        {
            try
            {
                // Act - Try to parse random bytes (should not throw)
                bool parseResult = _parser.TryParseRpcSequence(randomBytes, out RpcMessage? message);

                // Assert - Should either succeed or fail gracefully, never throw
                return true; // If we get here, no exception was thrown
            }
            catch
            {
                // Any exception means the property failed
                return false;
            }
        });
    }

    /// <summary>
    /// Helper method to validate final character is in correct range and type.
    /// </summary>
    private static bool IsValidFinalCharacter(RpcCommandType commandType)
    {
        byte finalChar = (byte)commandType;
        bool inValidRange = finalChar >= 0x40 && finalChar <= 0x7E;
        bool isValidType = commandType == RpcCommandType.FireAndForget ||
                          commandType == RpcCommandType.Query ||
                          commandType == RpcCommandType.Response ||
                          commandType == RpcCommandType.Error;
        
        return inValidRange && isValidType;
    }
}