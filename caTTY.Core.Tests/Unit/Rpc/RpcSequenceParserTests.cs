using System.Text;
using caTTY.Core.Rpc;
using NUnit.Framework;

namespace caTTY.Core.Tests.Unit.Rpc;

/// <summary>
/// Unit tests for RpcSequenceParser functionality.
/// Tests specific examples and edge cases for RPC sequence parsing.
/// </summary>
[TestFixture]
[Category("Unit")]
public class RpcSequenceParserTests
{
    private RpcSequenceParser _parser = null!;

    [SetUp]
    public void SetUp()
    {
        _parser = new RpcSequenceParser();
    }

    [Test]
    public void TryParseRpcSequence_ValidFireAndForgetCommand_ReturnsTrue()
    {
        // Arrange - Valid fire-and-forget command: ESC [ > 1001 ; 1 ; F
        byte[] sequence = Encoding.ASCII.GetBytes("\x1b[>1001;1;F");

        // Act
        bool result = _parser.TryParseRpcSequence(sequence, out RpcMessage? message);

        // Assert
        Assert.That(result, Is.True, "Valid RPC sequence should parse successfully");
        Assert.That(message, Is.Not.Null, "Message should not be null");
        Assert.That(message!.CommandId, Is.EqualTo(1001), "Command ID should be 1001");
        Assert.That(message.Version, Is.EqualTo(1), "Version should be 1");
        Assert.That(message.CommandType, Is.EqualTo(RpcCommandType.FireAndForget), "Command type should be FireAndForget");
        Assert.That(message.IsFireAndForget, Is.True, "Should be fire-and-forget command");
        Assert.That(message.Raw, Is.EqualTo("\x1b[>1001;1;F"), "Raw sequence should be preserved");
    }

    [Test]
    public void TryParseRpcSequence_ValidQueryCommand_ReturnsTrue()
    {
        // Arrange - Valid query command: ESC [ > 2001 ; 1 ; Q
        byte[] sequence = Encoding.ASCII.GetBytes("\x1b[>2001;1;Q");

        // Act
        bool result = _parser.TryParseRpcSequence(sequence, out RpcMessage? message);

        // Assert
        Assert.That(result, Is.True, "Valid query sequence should parse successfully");
        Assert.That(message, Is.Not.Null, "Message should not be null");
        Assert.That(message!.CommandId, Is.EqualTo(2001), "Command ID should be 2001");
        Assert.That(message.Version, Is.EqualTo(1), "Version should be 1");
        Assert.That(message.CommandType, Is.EqualTo(RpcCommandType.Query), "Command type should be Query");
        Assert.That(message.IsQuery, Is.True, "Should be query command");
    }

    [Test]
    public void TryParseRpcSequence_ValidResponseCommand_ReturnsTrue()
    {
        // Arrange - Valid response: ESC [ > 2001 ; 1 ; R
        byte[] sequence = Encoding.ASCII.GetBytes("\x1b[>2001;1;R");

        // Act
        bool result = _parser.TryParseRpcSequence(sequence, out RpcMessage? message);

        // Assert
        Assert.That(result, Is.True, "Valid response sequence should parse successfully");
        Assert.That(message, Is.Not.Null, "Message should not be null");
        Assert.That(message!.CommandId, Is.EqualTo(2001), "Command ID should be 2001");
        Assert.That(message.CommandType, Is.EqualTo(RpcCommandType.Response), "Command type should be Response");
        Assert.That(message.IsResponse, Is.True, "Should be response command");
    }

    [Test]
    public void TryParseRpcSequence_ValidErrorCommand_ReturnsTrue()
    {
        // Arrange - Valid error response: ESC [ > 9999 ; 1 ; E
        byte[] sequence = Encoding.ASCII.GetBytes("\x1b[>9999;1;E");

        // Act
        bool result = _parser.TryParseRpcSequence(sequence, out RpcMessage? message);

        // Assert
        Assert.That(result, Is.True, "Valid error sequence should parse successfully");
        Assert.That(message, Is.Not.Null, "Message should not be null");
        Assert.That(message!.CommandId, Is.EqualTo(9999), "Command ID should be 9999");
        Assert.That(message.CommandType, Is.EqualTo(RpcCommandType.Error), "Command type should be Error");
        Assert.That(message.IsError, Is.True, "Should be error command");
    }

    [Test]
    public void TryParseRpcSequence_WithAdditionalParameters_ReturnsTrue()
    {
        // Arrange - Command with additional parameters: ESC [ > 1012 ; 1 ; 75 ; 100 F
        byte[] sequence = Encoding.ASCII.GetBytes("\x1b[>1012;1;75;100F");

        // Act
        bool result = _parser.TryParseRpcSequence(sequence, out RpcMessage? message);

        // Assert
        Assert.That(result, Is.True, "Sequence with additional parameters should parse successfully");
        Assert.That(message, Is.Not.Null, "Message should not be null");
        Assert.That(message!.CommandId, Is.EqualTo(1012), "Command ID should be 1012");
        Assert.That(message.Version, Is.EqualTo(1), "Version should be 1");
        Assert.That(message.Parameters.NumericParameters.Length, Is.EqualTo(4), "Should have 4 parameters");
        Assert.That(message.Parameters.NumericParameters[2], Is.EqualTo(75), "Third parameter should be 75");
        Assert.That(message.Parameters.NumericParameters[3], Is.EqualTo(100), "Fourth parameter should be 100");
    }

    [Test]
    public void TryParseRpcSequence_TooShort_ReturnsFalse()
    {
        // Arrange - Too short sequence
        byte[] sequence = Encoding.ASCII.GetBytes("\x1b[>");

        // Act
        bool result = _parser.TryParseRpcSequence(sequence, out RpcMessage? message);

        // Assert
        Assert.That(result, Is.False, "Too short sequence should fail to parse");
        Assert.That(message, Is.Null, "Message should be null for failed parse");
    }

    [Test]
    public void TryParseRpcSequence_WrongPrefix_ReturnsFalse()
    {
        // Arrange - Wrong prefix (standard CSI instead of private use area)
        byte[] sequence = Encoding.ASCII.GetBytes("\x1b[1001;1;F");

        // Act
        bool result = _parser.TryParseRpcSequence(sequence, out RpcMessage? message);

        // Assert
        Assert.That(result, Is.False, "Standard CSI sequence should fail to parse");
        Assert.That(message, Is.Null, "Message should be null for failed parse");
    }

    [Test]
    public void TryParseRpcSequence_InvalidCommandId_ReturnsFalse()
    {
        // Arrange - Invalid command ID (outside 1000-9999 range)
        byte[] sequence = Encoding.ASCII.GetBytes("\x1b[>500;1;F");

        // Act
        bool result = _parser.TryParseRpcSequence(sequence, out RpcMessage? message);

        // Assert
        Assert.That(result, Is.False, "Invalid command ID should fail to parse");
        Assert.That(message, Is.Null, "Message should be null for failed parse");
    }

    [Test]
    public void TryParseRpcSequence_InvalidFinalCharacter_ReturnsFalse()
    {
        // Arrange - Invalid final character (not F/Q/R/E)
        byte[] sequence = Encoding.ASCII.GetBytes("\x1b[>1001;1;X");

        // Act
        bool result = _parser.TryParseRpcSequence(sequence, out RpcMessage? message);

        // Assert
        Assert.That(result, Is.False, "Invalid final character should fail to parse");
        Assert.That(message, Is.Null, "Message should be null for failed parse");
    }

    [Test]
    public void TryParseRpcSequence_MalformedParameters_ReturnsFalse()
    {
        // Arrange - Malformed parameters (missing semicolons)
        byte[] sequence = Encoding.ASCII.GetBytes("\x1b[>1001F");

        // Act
        bool result = _parser.TryParseRpcSequence(sequence, out RpcMessage? message);

        // Assert
        Assert.That(result, Is.False, "Malformed parameters should fail to parse");
        Assert.That(message, Is.Null, "Message should be null for failed parse");
    }

    [Test]
    public void TryExtractCommandId_ValidCommandId_ReturnsTrue()
    {
        // Arrange
        string parameterString = "1001;1;F";

        // Act
        bool result = _parser.TryExtractCommandId(parameterString, out int commandId);

        // Assert
        Assert.That(result, Is.True, "Valid command ID should be extracted");
        Assert.That(commandId, Is.EqualTo(1001), "Command ID should be 1001");
    }

    [Test]
    public void TryExtractCommandId_NoSemicolon_ReturnsFalse()
    {
        // Arrange
        string parameterString = "1001F";

        // Act
        bool result = _parser.TryExtractCommandId(parameterString, out int commandId);

        // Assert
        Assert.That(result, Is.False, "Missing semicolon should fail extraction");
        Assert.That(commandId, Is.EqualTo(0), "Command ID should be 0 for failed extraction");
    }

    [Test]
    public void TryExtractVersion_ValidVersion_ReturnsTrue()
    {
        // Arrange
        string parameterString = "1001;1;F";

        // Act
        bool result = _parser.TryExtractVersion(parameterString, out int version);

        // Assert
        Assert.That(result, Is.True, "Valid version should be extracted");
        Assert.That(version, Is.EqualTo(1), "Version should be 1");
    }

    [Test]
    public void TryExtractVersion_WithAdditionalParameters_ReturnsTrue()
    {
        // Arrange
        string parameterString = "1012;1;75;100F";

        // Act
        bool result = _parser.TryExtractVersion(parameterString, out int version);

        // Assert
        Assert.That(result, Is.True, "Version should be extracted with additional parameters");
        Assert.That(version, Is.EqualTo(1), "Version should be 1");
    }

    [Test]
    public void TryGetCommandType_ValidCommandTypes_ReturnsTrue()
    {
        // Test all valid command types
        var testCases = new[]
        {
            ((byte)'F', RpcCommandType.FireAndForget, "Fire-and-forget"),
            ((byte)'Q', RpcCommandType.Query, "Query"),
            ((byte)'R', RpcCommandType.Response, "Response"),
            ((byte)'E', RpcCommandType.Error, "Error")
        };

        foreach (var (finalChar, expectedType, description) in testCases)
        {
            // Act
            bool result = _parser.TryGetCommandType(finalChar, out RpcCommandType commandType);

            // Assert
            Assert.That(result, Is.True, $"{description} should be valid command type");
            Assert.That(commandType, Is.EqualTo(expectedType), $"{description} should map to correct type");
        }
    }

    [Test]
    public void TryGetCommandType_InvalidCommandType_ReturnsFalse()
    {
        // Arrange - Invalid command type
        byte finalChar = (byte)'X';

        // Act
        bool result = _parser.TryGetCommandType(finalChar, out RpcCommandType commandType);

        // Assert
        Assert.That(result, Is.False, "Invalid command type should return false");
        Assert.That(commandType, Is.EqualTo(default(RpcCommandType)), "Command type should be default for invalid input");
    }

    [Test]
    public void TryGetCommandType_OutOfRange_ReturnsFalse()
    {
        // Test characters outside private use area range (0x40-0x7E)
        var invalidChars = new byte[] { 0x3F, 0x7F, 0x20, 0x00 };

        foreach (byte invalidChar in invalidChars)
        {
            // Act
            bool result = _parser.TryGetCommandType(invalidChar, out RpcCommandType commandType);

            // Assert
            Assert.That(result, Is.False, $"Character 0x{invalidChar:X2} should be invalid");
            Assert.That(commandType, Is.EqualTo(default(RpcCommandType)), "Command type should be default for invalid input");
        }
    }

    [Test]
    public void TryParseParameters_ValidParameters_ReturnsTrue()
    {
        // Arrange
        string parameterString = "1001;1;75;100F";

        // Act
        bool result = _parser.TryParseParameters(parameterString, out RpcParameters? parameters);

        // Assert
        Assert.That(result, Is.True, "Valid parameters should parse successfully");
        Assert.That(parameters, Is.Not.Null, "Parameters should not be null");
        Assert.That(parameters!.NumericParameters.Length, Is.EqualTo(4), "Should have 4 parameters");
        Assert.That(parameters.NumericParameters[0], Is.EqualTo(1001), "First parameter should be 1001");
        Assert.That(parameters.NumericParameters[1], Is.EqualTo(1), "Second parameter should be 1");
        Assert.That(parameters.NumericParameters[2], Is.EqualTo(75), "Third parameter should be 75");
        Assert.That(parameters.NumericParameters[3], Is.EqualTo(100), "Fourth parameter should be 100");
    }

    [Test]
    public void TryParseParameters_GracefulHandling_ReturnsTrue()
    {
        // Arrange - Parameters with invalid characters (should be handled gracefully)
        string parameterString = "1001;abc;75F";

        // Act
        bool result = _parser.TryParseParameters(parameterString, out RpcParameters? parameters);

        // Assert
        Assert.That(result, Is.True, "Should handle invalid parameters gracefully");
        Assert.That(parameters, Is.Not.Null, "Parameters should not be null");
        Assert.That(parameters!.NumericParameters.Length, Is.EqualTo(3), "Should have 3 parameters");
        Assert.That(parameters.NumericParameters[0], Is.EqualTo(1001), "First parameter should be 1001");
        Assert.That(parameters.NumericParameters[1], Is.EqualTo(0), "Invalid parameter should default to 0");
        Assert.That(parameters.NumericParameters[2], Is.EqualTo(75), "Third parameter should be 75");
    }

    [Test]
    public void TryParseParameters_InsufficientParameters_ReturnsFalse()
    {
        // Arrange - Not enough parameters (need at least command ID and version)
        string parameterString = "1001F";

        // Act
        bool result = _parser.TryParseParameters(parameterString, out RpcParameters? parameters);

        // Assert
        Assert.That(result, Is.False, "Insufficient parameters should fail to parse");
        Assert.That(parameters, Is.Null, "Parameters should be null for failed parse");
    }

    [Test]
    public void RpcMessage_IsValidCommandIdRange_ValidatesCorrectly()
    {
        // Test valid command ID ranges for different command types
        var testCases = new[]
        {
            (1001, RpcCommandType.FireAndForget, true, "Fire-and-forget in valid range"),
            (1999, RpcCommandType.FireAndForget, true, "Fire-and-forget at upper bound"),
            (2000, RpcCommandType.FireAndForget, false, "Fire-and-forget outside range"),
            (2001, RpcCommandType.Query, true, "Query in valid range"),
            (2999, RpcCommandType.Query, true, "Query at upper bound"),
            (1000, RpcCommandType.Query, false, "Query outside range"),
            (1500, RpcCommandType.Response, true, "Response for fire-and-forget command"),
            (2500, RpcCommandType.Response, true, "Response for query command"),
            (9001, RpcCommandType.Error, true, "Error in valid range"),
            (9999, RpcCommandType.Error, true, "Error at upper bound"),
            (1000, RpcCommandType.Error, false, "Error outside range")
        };

        foreach (var (commandId, commandType, expectedValid, description) in testCases)
        {
            // Arrange
            var message = new RpcMessage
            {
                CommandId = commandId,
                CommandType = commandType,
                Version = 1,
                Parameters = new RpcParameters()
            };

            // Act
            bool isValid = message.IsValidCommandIdRange();

            // Assert
            Assert.That(isValid, Is.EqualTo(expectedValid), description);
        }
    }
}