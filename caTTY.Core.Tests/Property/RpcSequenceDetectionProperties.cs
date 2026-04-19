using System.Text;
using caTTY.Core.Rpc;
using FsCheck;
using NUnit.Framework;

namespace caTTY.Core.Tests.Property;

/// <summary>
/// Property-based tests for RPC sequence detection functionality.
/// These tests verify universal properties that should hold for all RPC sequence detection operations.
/// </summary>
[TestFixture]
[Category("Property")]
public class RpcSequenceDetectionProperties
{
    private RpcSequenceDetector _detector = null!;

    [SetUp]
    public void SetUp()
    {
        _detector = new RpcSequenceDetector();
    }

    /// <summary>
    /// Generator for valid command IDs in the RPC range (1000-9999).
    /// </summary>
    public static Arbitrary<int> ValidCommandIdArb =>
        Arb.From(Gen.Choose(1000, 9999));

    /// <summary>
    /// Generator for invalid command IDs outside the RPC range.
    /// </summary>
    public static Arbitrary<int> InvalidCommandIdArb =>
        Arb.From(Gen.OneOf(
            Gen.Choose(int.MinValue, 999),
            Gen.Choose(10000, int.MaxValue)
        ).Where(x => x != int.MinValue && x != int.MaxValue)); // Avoid overflow edge cases

    /// <summary>
    /// Generator for valid final characters in the private use area range (0x40-0x7E).
    /// </summary>
    public static Arbitrary<byte> ValidFinalCharArb =>
        Arb.From(Gen.Choose(0x40, 0x7E).Select(x => (byte)x));

    /// <summary>
    /// Generator for invalid final characters outside the private use area range.
    /// </summary>
    public static Arbitrary<byte> InvalidFinalCharArb =>
        Arb.From(Gen.OneOf(
            Gen.Choose(0, 0x3F),
            Gen.Choose(0x7F, 255)
        ).Select(x => (byte)x));

    /// <summary>
    /// Generator for valid RPC command type final characters (F, Q, R, E).
    /// </summary>
    public static Arbitrary<char> ValidRpcFinalCharArb =>
        Arb.From(Gen.Elements('F', 'Q', 'R', 'E'));

    /// <summary>
    /// Generator for valid RPC sequences with proper format.
    /// </summary>
    public static Arbitrary<byte[]> ValidRpcSequenceArb =>
        Arb.From(
            from commandId in ValidCommandIdArb.Generator
            from version in Gen.Choose(1, 99)
            from finalChar in ValidRpcFinalCharArb.Generator
            select Encoding.ASCII.GetBytes($"\x1b[>{commandId};{version};{finalChar}")
        );

    /// <summary>
    /// Generator for non-RPC sequences (standard terminal sequences).
    /// </summary>
    public static Arbitrary<byte[]> NonRpcSequenceArb =>
        Arb.From(Gen.OneOf(
            // Standard CSI sequences
            Gen.Constant(Encoding.ASCII.GetBytes("\x1b[H")),
            Gen.Constant(Encoding.ASCII.GetBytes("\x1b[2J")),
            Gen.Constant(Encoding.ASCII.GetBytes("\x1b[31m")),
            // OSC sequences
            Gen.Constant(Encoding.ASCII.GetBytes("\x1b]0;Title\x07")),
            // Simple escape sequences
            Gen.Constant(Encoding.ASCII.GetBytes("\x1bM")),
            // Regular text
            Gen.Constant(Encoding.ASCII.GetBytes("Hello World"))
        ));

    /// <summary>
    /// **Feature: term-sequence-rpc, Property 2: Sequence Parsing Consistency**
    /// **Validates: Requirements 1.1, 1.3**
    /// Property: For any valid RPC sequence, IsRpcSequence should return true and 
    /// GetSequenceType should return a non-None type. The results should be consistent.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property SequenceParsingConsistency()
    {
        return Prop.ForAll(ValidRpcSequenceArb, (byte[] sequence) =>
        {
            // Act
            bool isRpcSequence = _detector.IsRpcSequence(sequence);
            RpcSequenceType sequenceType = _detector.GetSequenceType(sequence);

            // Assert - For valid RPC sequences, both methods should agree
            bool consistentDetection = isRpcSequence == (sequenceType != RpcSequenceType.None);

            // Valid RPC sequences should be detected as RPC
            bool correctDetection = isRpcSequence;

            // Sequence type should be Valid for properly formatted sequences
            bool correctType = sequenceType == RpcSequenceType.Valid;

            return consistentDetection && correctDetection && correctType;
        });
    }

    /// <summary>
    /// **Feature: term-sequence-rpc, Property 2a: Non-RPC Sequence Rejection**
    /// **Validates: Requirements 1.1**
    /// Property: For any non-RPC sequence, IsRpcSequence should return false and 
    /// GetSequenceType should return None.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property NonRpcSequenceRejection()
    {
        return Prop.ForAll(NonRpcSequenceArb, (byte[] sequence) =>
        {
            // Act
            bool isRpcSequence = _detector.IsRpcSequence(sequence);
            RpcSequenceType sequenceType = _detector.GetSequenceType(sequence);

            // Assert - Non-RPC sequences should be rejected consistently
            bool correctRejection = !isRpcSequence;
            bool correctType = sequenceType == RpcSequenceType.None;
            bool consistentRejection = isRpcSequence == (sequenceType != RpcSequenceType.None);

            return correctRejection && correctType && consistentRejection;
        });
    }

    /// <summary>
    /// **Feature: term-sequence-rpc, Property 2b: Invalid Command ID Detection**
    /// **Validates: Requirements 1.3**
    /// Property: For any RPC sequence with invalid command ID, GetSequenceType should 
    /// return InvalidCommandId while IsRpcSequence may still return true (format check).
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property InvalidCommandIdDetection()
    {
        return Prop.ForAll(InvalidCommandIdArb, ValidRpcFinalCharArb, 
            (int invalidCommandId, char finalChar) =>
        {
            // Arrange - Create RPC sequence with invalid command ID
            byte[] sequence = Encoding.ASCII.GetBytes($"\x1b[>{invalidCommandId};1;{finalChar}");

            // Act
            bool isRpcSequence = _detector.IsRpcSequence(sequence);
            RpcSequenceType sequenceType = _detector.GetSequenceType(sequence);

            // Assert - Should detect format but classify as invalid command ID
            bool formatDetected = isRpcSequence; // May be true due to format
            bool correctClassification = sequenceType == RpcSequenceType.InvalidCommandId;

            return correctClassification;
        });
    }

    /// <summary>
    /// **Feature: term-sequence-rpc, Property 2c: Invalid Final Character Detection**
    /// **Validates: Requirements 1.3**
    /// Property: For any RPC sequence with invalid final character, GetSequenceType should 
    /// return InvalidFinalCharacter while IsRpcSequence may still return true (format check).
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property InvalidFinalCharacterDetection()
    {
        return Prop.ForAll(ValidCommandIdArb, InvalidFinalCharArb, 
            (int commandId, byte invalidFinalChar) =>
        {
            // Arrange - Create RPC sequence with invalid final character
            byte[] sequence = Encoding.ASCII.GetBytes($"\x1b[>{commandId};1;{(char)invalidFinalChar}");

            // Act
            bool isRpcSequence = _detector.IsRpcSequence(sequence);
            RpcSequenceType sequenceType = _detector.GetSequenceType(sequence);

            // Assert - Should detect format but classify as invalid final character
            bool formatDetected = isRpcSequence; // May be true due to format
            bool correctClassification = sequenceType == RpcSequenceType.InvalidFinalCharacter;

            return correctClassification;
        });
    }

    /// <summary>
    /// **Feature: term-sequence-rpc, Property 2d: Malformed Sequence Detection**
    /// **Validates: Requirements 1.1, 1.3**
    /// Property: For any malformed RPC sequence (wrong format), GetSequenceType should 
    /// return Malformed and IsRpcSequence behavior should be consistent.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property MalformedSequenceDetection()
    {
        return Prop.ForAll(ValidCommandIdArb, (int commandId) =>
        {
            // Arrange - Create various malformed sequences
            var malformedSequences = new[]
            {
                Encoding.ASCII.GetBytes($"\x1b[>{commandId}F"), // Missing semicolons
                Encoding.ASCII.GetBytes($"\x1b[>{commandId};F"), // Missing version
                Encoding.ASCII.GetBytes($"\x1b[>{commandId};;F"), // Empty version
                Encoding.ASCII.GetBytes($"\x1b[>abc;1;F"), // Non-numeric command ID
                Encoding.ASCII.GetBytes($"\x1b[>{commandId};abc;F"), // Non-numeric version
            };

            bool allMalformedCorrectly = true;

            foreach (var sequence in malformedSequences)
            {
                // Act
                bool isRpcSequence = _detector.IsRpcSequence(sequence);
                RpcSequenceType sequenceType = _detector.GetSequenceType(sequence);

                // Assert - Should be classified as malformed
                if (sequenceType != RpcSequenceType.Malformed)
                {
                    allMalformedCorrectly = false;
                    break;
                }
            }

            return allMalformedCorrectly;
        });
    }

    /// <summary>
    /// **Feature: term-sequence-rpc, Property 2e: Command ID Range Validation**
    /// **Validates: Requirements 1.3**
    /// Property: For any command ID, IsValidCommandId should return true only for 
    /// values in the range 1000-9999.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property CommandIdRangeValidation()
    {
        return Prop.ForAll(Arb.Default.Int32(), (int commandId) =>
        {
            // Act
            bool isValid = _detector.IsValidCommandId(commandId);

            // Assert - Should be valid only in correct range
            bool expectedValid = commandId >= 1000 && commandId <= 9999;

            return isValid == expectedValid;
        });
    }

    /// <summary>
    /// **Feature: term-sequence-rpc, Property 2f: Final Character Range Validation**
    /// **Validates: Requirements 1.3**
    /// Property: For any final character, IsValidFinalCharacter should return true only 
    /// for values in the private use area range (0x40-0x7E).
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property FinalCharacterRangeValidation()
    {
        return Prop.ForAll(Arb.Default.Byte(), (byte finalChar) =>
        {
            // Act
            bool isValid = _detector.IsValidFinalCharacter(finalChar);

            // Assert - Should be valid only in correct range
            bool expectedValid = finalChar >= 0x40 && finalChar <= 0x7E;

            return isValid == expectedValid;
        });
    }

    /// <summary>
    /// **Feature: term-sequence-rpc, Property 2g: Sequence Length Validation**
    /// **Validates: Requirements 1.1**
    /// Property: For any sequence shorter than minimum RPC length, IsRpcSequence should 
    /// return false and GetSequenceType should return None.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property SequenceLengthValidation()
    {
        return Prop.ForAll(Arb.From(Gen.Choose(0, 7)), (int length) =>
        {
            // Arrange - Create sequence shorter than minimum (8 bytes)
            byte[] shortSequence = new byte[length];
            if (length > 0) shortSequence[0] = 0x1B; // ESC
            if (length > 1) shortSequence[1] = 0x5B; // [
            if (length > 2) shortSequence[2] = 0x3E; // >

            // Act
            bool isRpcSequence = _detector.IsRpcSequence(shortSequence);
            RpcSequenceType sequenceType = _detector.GetSequenceType(shortSequence);

            // Assert - Short sequences should be rejected
            bool correctRejection = !isRpcSequence;
            bool correctType = sequenceType == RpcSequenceType.None;

            return correctRejection && correctType;
        });
    }

    /// <summary>
    /// **Feature: term-sequence-rpc, Property 2h: Prefix Validation**
    /// **Validates: Requirements 1.1**
    /// Property: For any sequence without the correct ESC [ > prefix, IsRpcSequence should 
    /// return false and GetSequenceType should return None.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property PrefixValidation()
    {
        return Prop.ForAll(ValidCommandIdArb, ValidRpcFinalCharArb, 
            (int commandId, char finalChar) =>
        {
            // Arrange - Create sequences with wrong prefixes
            var wrongPrefixSequences = new[]
            {
                Encoding.ASCII.GetBytes($"\x1b[{commandId};1;{finalChar}"), // Missing >
                Encoding.ASCII.GetBytes($"\x1b]{commandId};1;{finalChar}"), // Wrong bracket
                Encoding.ASCII.GetBytes($"\x1c[>{commandId};1;{finalChar}"), // Wrong escape
                Encoding.ASCII.GetBytes($"[>{commandId};1;{finalChar}"), // Missing escape
            };

            bool allRejectedCorrectly = true;

            foreach (var sequence in wrongPrefixSequences)
            {
                // Act
                bool isRpcSequence = _detector.IsRpcSequence(sequence);
                RpcSequenceType sequenceType = _detector.GetSequenceType(sequence);

                // Assert - Should be rejected
                if (isRpcSequence || sequenceType != RpcSequenceType.None)
                {
                    allRejectedCorrectly = false;
                    break;
                }
            }

            return allRejectedCorrectly;
        });
    }
}