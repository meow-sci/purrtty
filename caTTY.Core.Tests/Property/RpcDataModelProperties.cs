using caTTY.Core.Rpc;
using FsCheck;
using NUnit.Framework;

namespace caTTY.Core.Tests.Property;

/// <summary>
/// Property-based tests for RPC data model validation.
/// These tests verify universal properties that should hold for all valid RPC sequences.
/// </summary>
[TestFixture]
[Category("Property")]
public class RpcDataModelProperties
{
    /// <summary>
    /// Generator for valid command IDs in the RPC range (1000-9999).
    /// </summary>
    public static Arbitrary<int> ValidCommandIdArb =>
        Arb.From(Gen.OneOf(
            Gen.Choose(1000, 1999), // Fire-and-forget range
            Gen.Choose(2000, 2999), // Query range
            Gen.Choose(9000, 9999)  // System/error range
        ));

    /// <summary>
    /// Generator for valid RPC command types.
    /// </summary>
    public static Arbitrary<RpcCommandType> ValidCommandTypeArb =>
        Arb.From(Gen.Elements(
            RpcCommandType.FireAndForget,
            RpcCommandType.Query,
            RpcCommandType.Response,
            RpcCommandType.Error
        ));

    /// <summary>
    /// Generator for valid numeric parameters.
    /// </summary>
    public static Arbitrary<int[]> ValidParametersArb =>
        Arb.From(Gen.ArrayOf(Gen.Choose(0, 9999)));

    /// <summary>
    /// Generator for valid command ID and command type pairs.
    /// </summary>
    public static Arbitrary<(int CommandId, RpcCommandType CommandType)> ValidCommandIdTypeArb =>
        Arb.From(Gen.OneOf(
            // Fire-and-forget commands (1000-1999)
            Gen.Choose(1000, 1999).Select(id => (id, RpcCommandType.FireAndForget)),
            // Query commands (2000-2999)
            Gen.Choose(2000, 2999).Select(id => (id, RpcCommandType.Query)),
            // Response commands (can be for any query or fire-and-forget)
            Gen.Choose(1000, 2999).Select(id => (id, RpcCommandType.Response)),
            // Error commands (9000-9999)
            Gen.Choose(9000, 9999).Select(id => (id, RpcCommandType.Error))
        ));

    /// <summary>
    /// **Feature: term-sequence-rpc, Property 1: Private Use Area Sequence Format Validation**
    /// **Validates: Requirements 1.2, 1.6, 2.6, 3.6**
    /// Property: For any RPC sequence, it must follow the ESC [ > Pn ; Pv ; Pc format where 
    /// Pn is in valid command ranges (1000-9999), Pv is 1, and Pc is a valid final character 
    /// (F, Q, R, E) in the range 0x40-0x7E.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property RpcSequenceFormatValidation()
    {
        return Prop.ForAll(ValidCommandIdTypeArb, 
            ((int CommandId, RpcCommandType CommandType) pair) =>
        {
            // Arrange - Create an RPC message with valid components
            var rpcParameters = new RpcParameters
            {
                NumericParameters = Array.Empty<int>()
            };

            var message = new RpcMessage
            {
                CommandId = pair.CommandId,
                Version = 1,
                CommandType = pair.CommandType,
                Parameters = rpcParameters,
                Raw = $"\x1b[>{pair.CommandId};1;{(char)pair.CommandType}"
            };

            // Act & Assert - Validate the message structure
            bool validCommandIdRange = pair.CommandId >= 1000 && pair.CommandId <= 9999;
            bool validVersion = message.Version == 1;
            bool validCommandType = Enum.IsDefined(typeof(RpcCommandType), pair.CommandType);
            bool validFinalCharacter = (byte)pair.CommandType >= 0x40 && (byte)pair.CommandType <= 0x7E;
            bool validCommandIdForType = message.IsValidCommandIdRange();

            // All should be true for valid pairs
            return validCommandIdRange && validVersion && validCommandType && 
                   validFinalCharacter && validCommandIdForType;
        });
    }

    /// <summary>
    /// **Feature: term-sequence-rpc, Property 1b: RPC Parameters Access Consistency**
    /// **Validates: Requirements 1.2**
    /// Property: For any RPC parameters, accessing parameters by index should be consistent
    /// and return fallback values for out-of-range indices.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property RpcParametersAccessConsistency()
    {
        return Prop.ForAll(ValidParametersArb, (int[] parameters) =>
        {
            // Arrange
            var rpcParameters = new RpcParameters
            {
                NumericParameters = parameters ?? Array.Empty<int>()
            };

            // Act & Assert - Test parameter access
            for (int i = 0; i < rpcParameters.NumericParameters.Length; i++)
            {
                int value = rpcParameters.GetNumericParameter(i, -1);
                if (value != rpcParameters.NumericParameters[i])
                    return false;
            }

            // Test out-of-range access returns fallback
            int fallbackValue = 12345;
            int outOfRangeValue = rpcParameters.GetNumericParameter(rpcParameters.NumericParameters.Length + 10, fallbackValue);
            bool fallbackWorks = outOfRangeValue == fallbackValue;

            // Test negative index returns fallback
            int negativeIndexValue = rpcParameters.GetNumericParameter(-1, fallbackValue);
            bool negativeIndexWorks = negativeIndexValue == fallbackValue;

            return fallbackWorks && negativeIndexWorks;
        });
    }

    /// <summary>
    /// **Feature: term-sequence-rpc, Property 1c: RPC Message Type Classification**
    /// **Validates: Requirements 2.6, 3.6**
    /// Property: For any RPC message, the type classification methods should be consistent
    /// with the command type and mutually exclusive.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property RpcMessageTypeClassification()
    {
        return Prop.ForAll(ValidCommandIdArb, ValidCommandTypeArb, 
            (int commandId, RpcCommandType commandType) =>
        {
            // Arrange
            var message = new RpcMessage
            {
                CommandId = commandId,
                Version = 1,
                CommandType = commandType,
                Parameters = new RpcParameters(),
                Raw = $"\x1b[>{commandId};1;{(char)commandType}"
            };

            // Act - Check type classification methods
            bool isFireAndForget = message.IsFireAndForget;
            bool isQuery = message.IsQuery;
            bool isResponse = message.IsResponse;
            bool isError = message.IsError;

            // Assert - Exactly one should be true based on command type
            int trueCount = (isFireAndForget ? 1 : 0) + (isQuery ? 1 : 0) + 
                           (isResponse ? 1 : 0) + (isError ? 1 : 0);
            bool exactlyOneTrue = trueCount == 1;

            // Verify correct classification
            bool correctClassification = commandType switch
            {
                RpcCommandType.FireAndForget => isFireAndForget && !isQuery && !isResponse && !isError,
                RpcCommandType.Query => !isFireAndForget && isQuery && !isResponse && !isError,
                RpcCommandType.Response => !isFireAndForget && !isQuery && isResponse && !isError,
                RpcCommandType.Error => !isFireAndForget && !isQuery && !isResponse && isError,
                _ => false
            };

            return exactlyOneTrue && correctClassification;
        });
    }

    /// <summary>
    /// **Feature: term-sequence-rpc, Property 1d: RPC Result Factory Methods**
    /// **Validates: Requirements 1.2**
    /// Property: For any RPC result created using factory methods, the properties should
    /// be set correctly and consistently.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property RpcResultFactoryMethods()
    {
        return Prop.ForAll(Arb.Default.String(), Arb.Default.Int32(), 
            (string? errorMessage, int dataValue) =>
        {
            // Arrange & Act - Test success factory method
            var successResult = RpcResult.CreateSuccess(dataValue, TimeSpan.FromMilliseconds(100));
            
            // Test failure factory method
            var failureResult = RpcResult.CreateFailure(errorMessage ?? "test error", TimeSpan.FromMilliseconds(200));

            // Assert - Success result properties
            bool successPropertiesCorrect = 
                successResult.Success == true &&
                successResult.Data?.Equals(dataValue) == true &&
                successResult.ErrorMessage == null &&
                successResult.ExecutionTime == TimeSpan.FromMilliseconds(100);

            // Failure result properties
            bool failurePropertiesCorrect =
                failureResult.Success == false &&
                failureResult.Data == null &&
                failureResult.ErrorMessage == (errorMessage ?? "test error") &&
                failureResult.ExecutionTime == TimeSpan.FromMilliseconds(200);

            return successPropertiesCorrect && failurePropertiesCorrect;
        });
    }

    /// <summary>
    /// **Feature: term-sequence-rpc, Property 1e: Command ID Range Validation**
    /// **Validates: Requirements 1.6**
    /// Property: For any command ID outside the valid range (1000-9999), the validation
    /// should correctly identify it as invalid.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property CommandIdRangeValidation()
    {
        return Prop.ForAll(Arb.Default.Int32(), (int commandId) =>
        {
            // Arrange
            var message = new RpcMessage
            {
                CommandId = commandId,
                Version = 1,
                CommandType = RpcCommandType.FireAndForget,
                Parameters = new RpcParameters(),
                Raw = $"\x1b[>{commandId};1;F"
            };

            // Act
            bool isValidRange = message.IsValidCommandIdRange();

            // Assert - Should be valid only if in correct range for command type
            bool expectedValid = commandId >= 1000 && commandId <= 1999; // Fire-and-forget range

            return isValidRange == expectedValid;
        });
    }

    /// <summary>
    /// **Feature: term-sequence-rpc, Property 1f: Final Character Range Validation**
    /// **Validates: Requirements 1.6**
    /// Property: For any final character, it should be valid only if it's in the 
    /// private use area range (0x40-0x7E) and corresponds to a valid RPC command type.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property FinalCharacterRangeValidation()
    {
        return Prop.ForAll(Arb.Default.Byte(), (byte finalChar) =>
        {
            // Act - Check if the character is in valid range
            bool inValidRange = finalChar >= 0x40 && finalChar <= 0x7E;
            bool isValidCommandType = finalChar == (byte)'F' || finalChar == (byte)'Q' || 
                                    finalChar == (byte)'R' || finalChar == (byte)'E';

            // Assert - Character should be valid for RPC if it's both in range and a valid command type
            bool shouldBeValid = inValidRange && isValidCommandType;

            // Test with actual RpcCommandType enum
            bool canParseAsCommandType = Enum.IsDefined(typeof(RpcCommandType), (int)finalChar);

            return shouldBeValid == canParseAsCommandType;
        });
    }
}