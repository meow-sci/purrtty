using System.Text;
using caTTY.Core.Rpc;
using NUnit.Framework;

namespace caTTY.Core.Tests.Unit.Rpc;

/// <summary>
/// Unit tests for RpcSequenceDetector functionality.
/// Tests specific examples and edge cases for RPC sequence detection.
/// </summary>
[TestFixture]
[Category("Unit")]
public class RpcSequenceDetectorTests
{
    private RpcSequenceDetector _detector = null!;

    [SetUp]
    public void SetUp()
    {
        _detector = new RpcSequenceDetector();
    }

    [Test]
    public void IsRpcSequence_ValidRpcSequence_ReturnsTrue()
    {
        // Arrange - Valid RPC sequence: ESC [ > 1001 ; 1 ; F
        byte[] sequence = Encoding.ASCII.GetBytes("\x1b[>1001;1;F");

        // Act
        bool result = _detector.IsRpcSequence(sequence);

        // Assert
        Assert.That(result, Is.True, "Valid RPC sequence should be detected");
    }

    [Test]
    public void IsRpcSequence_TooShort_ReturnsFalse()
    {
        // Arrange - Too short sequence
        byte[] sequence = Encoding.ASCII.GetBytes("\x1b[>");

        // Act
        bool result = _detector.IsRpcSequence(sequence);

        // Assert
        Assert.That(result, Is.False, "Too short sequence should not be detected as RPC");
    }

    [Test]
    public void IsRpcSequence_WrongPrefix_ReturnsFalse()
    {
        // Arrange - Wrong prefix (standard CSI instead of private use area)
        byte[] sequence = Encoding.ASCII.GetBytes("\x1b[1001;1;F");

        // Act
        bool result = _detector.IsRpcSequence(sequence);

        // Assert
        Assert.That(result, Is.False, "Standard CSI sequence should not be detected as RPC");
    }

    [Test]
    public void GetSequenceType_ValidRpcSequence_ReturnsValid()
    {
        // Arrange - Valid RPC sequence: ESC [ > 1001 ; 1 ; F
        byte[] sequence = Encoding.ASCII.GetBytes("\x1b[>1001;1;F");

        // Act
        RpcSequenceType result = _detector.GetSequenceType(sequence);

        // Assert
        Assert.That(result, Is.EqualTo(RpcSequenceType.Valid), "Valid RPC sequence should return Valid type");
    }

    [Test]
    public void GetSequenceType_InvalidCommandId_ReturnsInvalidCommandId()
    {
        // Arrange - Invalid command ID (outside 1000-9999 range)
        byte[] sequence = Encoding.ASCII.GetBytes("\x1b[>500;1;F");

        // Act
        RpcSequenceType result = _detector.GetSequenceType(sequence);

        // Assert
        Assert.That(result, Is.EqualTo(RpcSequenceType.InvalidCommandId), "Invalid command ID should return InvalidCommandId type");
    }

    [Test]
    public void GetSequenceType_InvalidFinalCharacter_ReturnsInvalidFinalCharacter()
    {
        // Arrange - Invalid final character (outside 0x40-0x7E range)
        byte[] sequence = Encoding.ASCII.GetBytes("\x1b[>1001;1;\x30"); // 0x30 is '0', outside valid range

        // Act
        RpcSequenceType result = _detector.GetSequenceType(sequence);

        // Assert
        Assert.That(result, Is.EqualTo(RpcSequenceType.InvalidFinalCharacter), "Invalid final character should return InvalidFinalCharacter type");
    }

    [Test]
    public void GetSequenceType_MalformedSequence_ReturnsMalformed()
    {
        // Arrange - Malformed sequence (missing semicolons)
        byte[] sequence = Encoding.ASCII.GetBytes("\x1b[>1001F");

        // Act
        RpcSequenceType result = _detector.GetSequenceType(sequence);

        // Assert
        Assert.That(result, Is.EqualTo(RpcSequenceType.Malformed), "Malformed sequence should return Malformed type");
    }

    [Test]
    public void GetSequenceType_NotRpcSequence_ReturnsNone()
    {
        // Arrange - Standard terminal sequence
        byte[] sequence = Encoding.ASCII.GetBytes("\x1b[H");

        // Act
        RpcSequenceType result = _detector.GetSequenceType(sequence);

        // Assert
        Assert.That(result, Is.EqualTo(RpcSequenceType.None), "Non-RPC sequence should return None type");
    }

    [Test]
    public void IsValidCommandId_ValidRanges_ReturnsTrue()
    {
        // Test valid command ID ranges
        Assert.That(_detector.IsValidCommandId(1000), Is.True, "1000 should be valid");
        Assert.That(_detector.IsValidCommandId(1500), Is.True, "1500 should be valid");
        Assert.That(_detector.IsValidCommandId(2000), Is.True, "2000 should be valid");
        Assert.That(_detector.IsValidCommandId(9999), Is.True, "9999 should be valid");
    }

    [Test]
    public void IsValidCommandId_InvalidRanges_ReturnsFalse()
    {
        // Test invalid command ID ranges
        Assert.That(_detector.IsValidCommandId(999), Is.False, "999 should be invalid");
        Assert.That(_detector.IsValidCommandId(10000), Is.False, "10000 should be invalid");
        Assert.That(_detector.IsValidCommandId(0), Is.False, "0 should be invalid");
        Assert.That(_detector.IsValidCommandId(-1), Is.False, "-1 should be invalid");
    }

    [Test]
    public void IsValidFinalCharacter_ValidRange_ReturnsTrue()
    {
        // Test valid final character range (0x40-0x7E)
        Assert.That(_detector.IsValidFinalCharacter(0x40), Is.True, "0x40 should be valid");
        Assert.That(_detector.IsValidFinalCharacter((byte)'F'), Is.True, "'F' should be valid");
        Assert.That(_detector.IsValidFinalCharacter((byte)'Q'), Is.True, "'Q' should be valid");
        Assert.That(_detector.IsValidFinalCharacter((byte)'R'), Is.True, "'R' should be valid");
        Assert.That(_detector.IsValidFinalCharacter((byte)'E'), Is.True, "'E' should be valid");
        Assert.That(_detector.IsValidFinalCharacter(0x7E), Is.True, "0x7E should be valid");
    }

    [Test]
    public void IsValidFinalCharacter_InvalidRange_ReturnsFalse()
    {
        // Test invalid final character range
        Assert.That(_detector.IsValidFinalCharacter(0x3F), Is.False, "0x3F should be invalid");
        Assert.That(_detector.IsValidFinalCharacter(0x7F), Is.False, "0x7F should be invalid");
        Assert.That(_detector.IsValidFinalCharacter(0x20), Is.False, "0x20 should be invalid");
    }

    [Test]
    public void GetSequenceType_AllValidCommandTypes_ReturnsValid()
    {
        // Test all valid RPC command types
        var testCases = new[]
        {
            ("\x1b[>1001;1;F", "Fire-and-forget command"),
            ("\x1b[>2001;1;Q", "Query command"),
            ("\x1b[>1001;1;R", "Response"),
            ("\x1b[>9001;1;E", "Error response")
        };

        foreach (var (sequence, description) in testCases)
        {
            // Arrange
            byte[] sequenceBytes = Encoding.ASCII.GetBytes(sequence);

            // Act
            RpcSequenceType result = _detector.GetSequenceType(sequenceBytes);

            // Assert
            Assert.That(result, Is.EqualTo(RpcSequenceType.Valid), $"{description} should be valid");
        }
    }
}