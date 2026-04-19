using NUnit.Framework;
using caTTY.Core.Rpc;

namespace caTTY.Core.Tests.Unit.Rpc;

[TestFixture]
[Category("Unit")]
public class RpcCommandHandlerTests
{
    [Test]
    public void RpcCommandHandler_Constructor_SetsPropertiesCorrectly()
    {
        // Arrange & Act
        var handler = new TestRpcCommandHandler("Test Command", isFireAndForget: true, TimeSpan.FromSeconds(10));

        // Assert
        Assert.That(handler.Description, Is.EqualTo("Test Command"));
        Assert.That(handler.IsFireAndForget, Is.True);
        Assert.That(handler.Timeout, Is.EqualTo(TimeSpan.FromSeconds(10)));
    }

    [Test]
    public void RpcCommandHandler_Constructor_WithDefaultTimeout_SetsDefaultValue()
    {
        // Arrange & Act
        var handler = new TestRpcCommandHandler("Test Command", isFireAndForget: false);

        // Assert
        Assert.That(handler.Timeout, Is.EqualTo(TimeSpan.FromSeconds(5)));
    }

    [Test]
    public void RpcCommandHandler_Constructor_WithNullDescription_ThrowsArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new TestRpcCommandHandler(null!, isFireAndForget: true));
    }

    [Test]
    public void RpcCommandHandler_ValidateParameters_WithNullParameters_ReturnsFalse()
    {
        // Arrange
        var handler = new TestRpcCommandHandler("Test Command", isFireAndForget: true);

        // Act
        var result = handler.TestValidateParameters(null!);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void RpcCommandHandler_ValidateParameters_WithValidParameters_ReturnsTrue()
    {
        // Arrange
        var handler = new TestRpcCommandHandler("Test Command", isFireAndForget: true);
        var parameters = new RpcParameters();

        // Act
        var result = handler.TestValidateParameters(parameters);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void RpcCommandHandler_EnsureValidParameters_WithInvalidParameters_ThrowsArgumentException()
    {
        // Arrange
        var handler = new TestRpcCommandHandler("Test Command", isFireAndForget: true);

        // Act & Assert
        var ex = Assert.Throws<ArgumentNullException>(() => 
            handler.TestEnsureValidParameters(null!));
        Assert.That(ex.ParamName, Is.EqualTo("parameters"));
        Assert.That(ex.Message, Does.Contain("Parameters cannot be null for command: Test Command"));
    }

    [Test]
    public void RpcCommandHandler_EnsureValidParameters_WithValidParameters_DoesNotThrow()
    {
        // Arrange
        var handler = new TestRpcCommandHandler("Test Command", isFireAndForget: true);
        var parameters = new RpcParameters();

        // Act & Assert
        Assert.DoesNotThrow(() => handler.TestEnsureValidParameters(parameters));
    }

    // Test helper class to expose protected members
    private class TestRpcCommandHandler : RpcCommandHandler
    {
        public TestRpcCommandHandler(string description, bool isFireAndForget, TimeSpan timeout = default)
            : base(description, isFireAndForget, timeout)
        {
        }

        public override Task<object?> ExecuteAsync(RpcParameters parameters)
        {
            return Task.FromResult<object?>("test result");
        }

        public bool TestValidateParameters(RpcParameters parameters)
        {
            return ValidateParameters(parameters);
        }

        public void TestEnsureValidParameters(RpcParameters parameters)
        {
            EnsureValidParameters(parameters);
        }
    }
}