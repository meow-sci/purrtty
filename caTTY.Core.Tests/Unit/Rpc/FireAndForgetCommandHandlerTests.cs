using NUnit.Framework;
using caTTY.Core.Rpc;

namespace caTTY.Core.Tests.Unit.Rpc;

[TestFixture]
[Category("Unit")]
public class FireAndForgetCommandHandlerTests
{
    [Test]
    public void FireAndForgetCommandHandler_Constructor_SetsPropertiesCorrectly()
    {
        // Arrange & Act
        var handler = new TestFireAndForgetHandler("Test Fire-and-Forget Command");

        // Assert
        Assert.That(handler.Description, Is.EqualTo("Test Fire-and-Forget Command"));
        Assert.That(handler.IsFireAndForget, Is.True);
        Assert.That(handler.Timeout, Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public async Task FireAndForgetCommandHandler_ExecuteAsync_ReturnsNull()
    {
        // Arrange
        var handler = new TestFireAndForgetHandler("Test Command");
        var parameters = new RpcParameters();

        // Act
        var result = await handler.ExecuteAsync(parameters);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task FireAndForgetCommandHandler_ExecuteAsync_CallsExecuteActionAsync()
    {
        // Arrange
        var handler = new TestFireAndForgetHandler("Test Command");
        var parameters = new RpcParameters();

        // Act
        await handler.ExecuteAsync(parameters);

        // Assert
        Assert.That(handler.ExecuteActionAsyncCalled, Is.True);
        Assert.That(handler.ReceivedParameters, Is.EqualTo(parameters));
    }

    [Test]
    public async Task FireAndForgetCommandHandler_ExecuteAsync_WithSynchronousImplementation_CallsExecuteAction()
    {
        // Arrange
        var handler = new TestSynchronousFireAndForgetHandler("Test Command");
        var parameters = new RpcParameters();

        // Act
        await handler.ExecuteAsync(parameters);

        // Assert
        Assert.That(handler.ExecuteActionCalled, Is.True);
        Assert.That(handler.ReceivedParameters, Is.EqualTo(parameters));
    }

    [Test]
    public void FireAndForgetCommandHandler_ExecuteAsync_WithInvalidParameters_ThrowsArgumentException()
    {
        // Arrange
        var handler = new TestFireAndForgetHandler("Test Command");

        // Act & Assert
        var ex = Assert.ThrowsAsync<ArgumentNullException>(async () => 
            await handler.ExecuteAsync(null!));
        Assert.That(ex.ParamName, Is.EqualTo("parameters"));
    }

    // Test helper classes
    private class TestFireAndForgetHandler : FireAndForgetCommandHandler
    {
        public bool ExecuteActionAsyncCalled { get; private set; }
        public RpcParameters? ReceivedParameters { get; private set; }

        public TestFireAndForgetHandler(string description) : base(description)
        {
        }

        protected override Task ExecuteActionAsync(RpcParameters parameters)
        {
            ExecuteActionAsyncCalled = true;
            ReceivedParameters = parameters;
            return Task.CompletedTask;
        }
    }

    private class TestSynchronousFireAndForgetHandler : FireAndForgetCommandHandler
    {
        public bool ExecuteActionCalled { get; private set; }
        public RpcParameters? ReceivedParameters { get; private set; }

        public TestSynchronousFireAndForgetHandler(string description) : base(description)
        {
        }

        protected override void ExecuteAction(RpcParameters parameters)
        {
            ExecuteActionCalled = true;
            ReceivedParameters = parameters;
        }
    }
}