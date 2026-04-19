using System.Text;
using caTTY.Core.Parsing;
using caTTY.Core.Types;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace caTTY.Core.Tests.Unit.Parsing;

/// <summary>
///     Unit tests for the DCS sequence parser.
/// </summary>
[TestFixture]
[Category("Unit")]
public class DcsParserTests
{
    private DcsParser _parser = null!;
    private TestLogger _logger = null!;

    [SetUp]
    public void SetUp()
    {
        _logger = new TestLogger();
        _parser = new DcsParser(_logger);
    }

    [Test]
    public void ProcessDcsByte_CanAbort_ReturnsTrue()
    {
        // Arrange
        var escapeSequence = new List<byte> { 0x1b, 0x50 }; // ESC P
        string? dcsCommand = null;
        var dcsParamBuffer = new StringBuilder();
        string[] dcsParameters = Array.Empty<string>();
        byte b = 0x18; // CAN

        // Act
        bool isComplete = _parser.ProcessDcsByte(b, escapeSequence, ref dcsCommand, dcsParamBuffer, ref dcsParameters, out DcsMessage? message);

        // Assert
        Assert.That(isComplete, Is.True);
        Assert.That(message, Is.Null);
        Assert.That(dcsCommand, Is.Null);
    }

    [Test]
    public void ProcessDcsByte_SubAbort_ReturnsTrue()
    {
        // Arrange
        var escapeSequence = new List<byte> { 0x1b, 0x50 }; // ESC P
        string? dcsCommand = null;
        var dcsParamBuffer = new StringBuilder();
        string[] dcsParameters = Array.Empty<string>();
        byte b = 0x1a; // SUB

        // Act
        bool isComplete = _parser.ProcessDcsByte(b, escapeSequence, ref dcsCommand, dcsParamBuffer, ref dcsParameters, out DcsMessage? message);

        // Assert
        Assert.That(isComplete, Is.True);
        Assert.That(message, Is.Null);
        Assert.That(dcsCommand, Is.Null);
    }

    [Test]
    public void ProcessDcsByte_ParameterByte_BuffersParameter()
    {
        // Arrange
        var escapeSequence = new List<byte> { 0x1b, 0x50 }; // ESC P
        string? dcsCommand = null;
        var dcsParamBuffer = new StringBuilder();
        string[] dcsParameters = Array.Empty<string>();
        byte b = 0x31; // 1

        // Act
        bool isComplete = _parser.ProcessDcsByte(b, escapeSequence, ref dcsCommand, dcsParamBuffer, ref dcsParameters, out DcsMessage? message);

        // Assert
        Assert.That(isComplete, Is.False);
        Assert.That(message, Is.Null);
        Assert.That(dcsCommand, Is.Null);
        Assert.That(dcsParamBuffer.ToString(), Is.EqualTo("1"));
        Assert.That(escapeSequence, Has.Count.EqualTo(3));
        Assert.That(escapeSequence[2], Is.EqualTo(0x31));
    }

    [Test]
    public void ProcessDcsByte_FinalByte_SetsCommandAndParameters()
    {
        // Arrange
        var escapeSequence = new List<byte> { 0x1b, 0x50 }; // ESC P
        string? dcsCommand = null;
        var dcsParamBuffer = new StringBuilder("1$");
        string[] dcsParameters = Array.Empty<string>();
        byte b = 0x71; // q (final byte)

        // Act
        bool isComplete = _parser.ProcessDcsByte(b, escapeSequence, ref dcsCommand, dcsParamBuffer, ref dcsParameters, out DcsMessage? message);

        // Assert
        Assert.That(isComplete, Is.False); // Continue parsing data
        Assert.That(message, Is.Null);
        Assert.That(dcsCommand, Is.EqualTo("q"));
        Assert.That(dcsParameters, Is.EqualTo(new[] { "1$" }));
        Assert.That(escapeSequence, Has.Count.EqualTo(3));
        Assert.That(escapeSequence[2], Is.EqualTo(0x71));
    }

    [Test]
    public void ProcessDcsByte_FinalByteNoParameters_SetsCommandEmptyParameters()
    {
        // Arrange
        var escapeSequence = new List<byte> { 0x1b, 0x50 }; // ESC P
        string? dcsCommand = null;
        var dcsParamBuffer = new StringBuilder();
        string[] dcsParameters = Array.Empty<string>();
        byte b = 0x71; // q (final byte)

        // Act
        bool isComplete = _parser.ProcessDcsByte(b, escapeSequence, ref dcsCommand, dcsParamBuffer, ref dcsParameters, out DcsMessage? message);

        // Assert
        Assert.That(isComplete, Is.False); // Continue parsing data
        Assert.That(message, Is.Null);
        Assert.That(dcsCommand, Is.EqualTo("q"));
        Assert.That(dcsParameters, Is.EqualTo(Array.Empty<string>()));
    }

    [Test]
    public void ProcessDcsByte_MultipleParameters_SplitsCorrectly()
    {
        // Arrange
        var escapeSequence = new List<byte> { 0x1b, 0x50 }; // ESC P
        string? dcsCommand = null;
        var dcsParamBuffer = new StringBuilder("1;2;3");
        string[] dcsParameters = Array.Empty<string>();
        byte b = 0x71; // q (final byte)

        // Act
        bool isComplete = _parser.ProcessDcsByte(b, escapeSequence, ref dcsCommand, dcsParamBuffer, ref dcsParameters, out DcsMessage? message);

        // Assert
        Assert.That(isComplete, Is.False); // Continue parsing data
        Assert.That(message, Is.Null);
        Assert.That(dcsCommand, Is.EqualTo("q"));
        Assert.That(dcsParameters, Is.EqualTo(new[] { "1", "2", "3" }));
    }

    [Test]
    public void ProcessDcsByte_DataAfterCommand_ContinuesParsing()
    {
        // Arrange - command already set
        var escapeSequence = new List<byte> { 0x1b, 0x50, 0x71 }; // ESC P q
        string? dcsCommand = "q";
        var dcsParamBuffer = new StringBuilder();
        string[] dcsParameters = Array.Empty<string>();
        byte b = 0x6d; // m (data byte)

        // Act
        bool isComplete = _parser.ProcessDcsByte(b, escapeSequence, ref dcsCommand, dcsParamBuffer, ref dcsParameters, out DcsMessage? message);

        // Assert
        Assert.That(isComplete, Is.False); // Continue parsing data
        Assert.That(message, Is.Null);
        Assert.That(dcsCommand, Is.EqualTo("q"));
        Assert.That(escapeSequence, Has.Count.EqualTo(4));
        Assert.That(escapeSequence[3], Is.EqualTo(0x6d));
    }

    [Test]
    public void CreateDcsMessage_ValidInput_ReturnsCorrectMessage()
    {
        // Arrange
        var escapeSequence = new List<byte> { 0x1b, 0x50, 0x31, 0x24, 0x71, 0x1b, 0x5c }; // ESC P 1$q ESC \
        string terminator = "ST";
        string dcsCommand = "q";
        string[] dcsParameters = { "1$" };

        // Act
        DcsMessage message = _parser.CreateDcsMessage(escapeSequence, terminator, dcsCommand, dcsParameters);

        // Assert
        Assert.That(message.Type, Is.EqualTo("dcs"));
        Assert.That(message.Raw, Is.EqualTo("\x1bP1$q\x1b\\"));
        Assert.That(message.Terminator, Is.EqualTo("ST"));
        Assert.That(message.Implemented, Is.False);
        Assert.That(message.Command, Is.EqualTo("q"));
        Assert.That(message.Parameters, Is.EqualTo(new[] { "1$" }));
    }

    [Test]
    public void CreateDcsMessage_NullCommand_UsesEmptyString()
    {
        // Arrange
        var escapeSequence = new List<byte> { 0x1b, 0x50, 0x1b, 0x5c }; // ESC P ESC \
        string terminator = "ST";
        string? dcsCommand = null;
        string[] dcsParameters = Array.Empty<string>();

        // Act
        DcsMessage message = _parser.CreateDcsMessage(escapeSequence, terminator, dcsCommand, dcsParameters);

        // Assert
        Assert.That(message.Command, Is.EqualTo(string.Empty));
        Assert.That(message.Parameters, Is.EqualTo(Array.Empty<string>()));
    }

    [Test]
    public void CreateDcsMessage_LogsDebugMessage()
    {
        // Arrange
        var escapeSequence = new List<byte> { 0x1b, 0x50, 0x71, 0x1b, 0x5c }; // ESC P q ESC \
        string terminator = "ST";
        string dcsCommand = "q";
        string[] dcsParameters = Array.Empty<string>();

        // Act
        _parser.CreateDcsMessage(escapeSequence, terminator, dcsCommand, dcsParameters);

        // Assert
        Assert.That(_logger.LogMessages, Has.Some.Contains("DCS (ST): \x1bPq\x1b\\"));
    }

    [Test]
    public void Reset_DoesNotThrow()
    {
        // Act & Assert
        Assert.DoesNotThrow(() => _parser.Reset());
    }
}