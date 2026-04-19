using System.Text;
using caTTY.Core.Rpc;
using NUnit.Framework;

namespace caTTY.Core.Tests.Unit.Rpc;

[TestFixture]
[Category("Unit")]
public class RpcResponseGeneratorTests
{
    private RpcResponseGenerator _generator = null!;

    [SetUp]
    public void SetUp()
    {
        _generator = new RpcResponseGenerator();
    }

    [Test]
    public void GenerateResponse_WithSimpleData_ShouldFormatCorrectly()
    {
        // Arrange
        const int commandId = 2001;
        const int responseData = 75;

        // Act
        var result = _generator.GenerateResponse(commandId, responseData);

        // Assert
        var resultString = Encoding.ASCII.GetString(result);
        Assert.That(resultString, Is.EqualTo("\x1B[>2001;1;75;R"));
    }

    [Test]
    public void GenerateResponse_WithNullData_ShouldFormatCorrectly()
    {
        // Arrange
        const int commandId = 2001;

        // Act
        var result = _generator.GenerateResponse(commandId, null);

        // Assert
        var resultString = Encoding.ASCII.GetString(result);
        Assert.That(resultString, Is.EqualTo("\x1B[>2001;1;R"));
    }

    [Test]
    public void GenerateResponse_WithBooleanData_ShouldEncodeAsNumeric()
    {
        // Arrange
        const int commandId = 2001;

        // Act
        var resultTrue = _generator.GenerateResponse(commandId, true);
        var resultFalse = _generator.GenerateResponse(commandId, false);

        // Assert
        var resultTrueString = Encoding.ASCII.GetString(resultTrue);
        var resultFalseString = Encoding.ASCII.GetString(resultFalse);
        Assert.That(resultTrueString, Is.EqualTo("\x1B[>2001;1;1;R"));
        Assert.That(resultFalseString, Is.EqualTo("\x1B[>2001;1;0;R"));
    }

    [Test]
    public void GenerateResponse_WithArrayData_ShouldEncodeLength()
    {
        // Arrange
        const int commandId = 2001;
        var arrayData = new[] { 10, 20, 30 };

        // Act
        var result = _generator.GenerateResponse(commandId, arrayData);

        // Assert
        var resultString = Encoding.ASCII.GetString(result);
        Assert.That(resultString, Is.EqualTo("\x1B[>2001;1;3;10;20;30;R"));
    }

    [Test]
    public void GenerateError_WithMessage_ShouldFormatCorrectly()
    {
        // Arrange
        const int commandId = 2001;
        const string errorMessage = "INVALID_PARAMETER";

        // Act
        var result = _generator.GenerateError(commandId, errorMessage);

        // Assert
        var resultString = Encoding.ASCII.GetString(result);
        Assert.That(resultString, Is.EqualTo("\x1B[>9999;1;2001;INVALID_PARAMETER;E"));
    }

    [Test]
    public void GenerateError_WithNullMessage_ShouldFormatCorrectly()
    {
        // Arrange
        const int commandId = 2001;

        // Act
        var result = _generator.GenerateError(commandId, string.Empty);

        // Assert
        var resultString = Encoding.ASCII.GetString(result);
        Assert.That(resultString, Is.EqualTo("\x1B[>9999;1;2001;E"));
    }

    [Test]
    public void GenerateTimeout_ShouldFormatCorrectly()
    {
        // Arrange
        const int commandId = 2001;

        // Act
        var result = _generator.GenerateTimeout(commandId);

        // Assert
        var resultString = Encoding.ASCII.GetString(result);
        Assert.That(resultString, Is.EqualTo("\x1B[>9999;1;2001;TIMEOUT;E"));
    }

    [Test]
    public void GenerateSystemError_ShouldFormatCorrectly()
    {
        // Arrange
        const string errorMessage = "SYSTEM_FAILURE";

        // Act
        var result = _generator.GenerateSystemError(errorMessage);

        // Assert
        var resultString = Encoding.ASCII.GetString(result);
        Assert.That(resultString, Is.EqualTo("\x1B[>9999;1;0;SYSTEM_FAILURE;E"));
    }

    [Test]
    public void GenerateError_WithLongMessage_ShouldTruncate()
    {
        // Arrange
        const int commandId = 2001;
        var longMessage = new string('A', 150); // 150 characters

        // Act
        var result = _generator.GenerateError(commandId, longMessage);

        // Assert
        var resultString = Encoding.ASCII.GetString(result);
        Assert.That(resultString, Does.Contain("AAA..."));
        Assert.That(resultString.Length, Is.LessThan(200)); // Should be truncated
    }

    [Test]
    public void GenerateError_WithSpecialCharacters_ShouldSanitize()
    {
        // Arrange
        const int commandId = 2001;
        const string messageWithSpecialChars = "Error;with\nnewlines\tand\rsemicolons";

        // Act
        var result = _generator.GenerateError(commandId, messageWithSpecialChars);

        // Assert
        var resultString = Encoding.ASCII.GetString(result);
        Assert.That(resultString, Does.Not.Contain("Error;"));  // Should not contain semicolon after "Error"
        Assert.That(resultString, Does.Not.Contain("\n"));
        Assert.That(resultString, Does.Not.Contain("\r"));
        Assert.That(resultString, Does.Not.Contain("\t"));
    }

    [Test]
    public void GenerateResponse_WithDoubleData_ShouldEncodeAsInteger()
    {
        // Arrange
        const int commandId = 2001;
        const double doubleData = 75.5;

        // Act
        var result = _generator.GenerateResponse(commandId, doubleData);

        // Assert
        var resultString = Encoding.ASCII.GetString(result);
        Assert.That(resultString, Is.EqualTo("\x1B[>2001;1;755;R")); // 75.5 * 10 = 755
    }

    [Test]
    public void GenerateResponse_WithStringData_ShouldEncodeLengthAndContent()
    {
        // Arrange
        const int commandId = 2001;
        const string stringData = "TEST";

        // Act
        var result = _generator.GenerateResponse(commandId, stringData);

        // Assert
        var resultString = Encoding.ASCII.GetString(result);
        Assert.That(resultString, Is.EqualTo("\x1B[>2001;1;4;TEST;R"));
    }
}