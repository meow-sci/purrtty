using caTTY.Core.Rpc;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace caTTY.Core.Tests.Unit.Rpc;

[TestFixture]
[Category("Unit")]
public class RpcCommandRouterTests
{
    private RpcCommandRouter _router = null!;
    private TestLogger _logger = null!;

    [SetUp]
    public void SetUp()
    {
        _logger = new TestLogger();
        _router = new RpcCommandRouter(_logger);
    }

    [Test]
    public void RegisterCommand_ValidFireAndForgetCommand_ReturnsTrue()
    {
        // Arrange
        var handler = new TestFireAndForgetHandler();
        const int commandId = 1001;

        // Act
        var result = _router.RegisterCommand(commandId, handler);

        // Assert
        Assert.That(result, Is.True);
        Assert.That(_router.IsCommandRegistered(commandId), Is.True);
    }

    [Test]
    public void RegisterCommand_ValidQueryCommand_ReturnsTrue()
    {
        // Arrange
        var handler = new TestQueryHandler();
        const int commandId = 2001;

        // Act
        var result = _router.RegisterCommand(commandId, handler);

        // Assert
        Assert.That(result, Is.True);
        Assert.That(_router.IsCommandRegistered(commandId), Is.True);
    }

    [Test]
    public void RegisterCommand_InvalidCommandId_ReturnsFalse()
    {
        // Arrange
        var handler = new TestFireAndForgetHandler();
        const int invalidCommandId = 500; // Outside valid range

        // Act
        var result = _router.RegisterCommand(invalidCommandId, handler);

        // Assert
        Assert.That(result, Is.False);
        Assert.That(_router.IsCommandRegistered(invalidCommandId), Is.False);
    }

    [Test]
    public void RegisterCommand_FireAndForgetHandlerWithQueryId_ReturnsFalse()
    {
        // Arrange
        var handler = new TestFireAndForgetHandler();
        const int queryCommandId = 2001; // Query range but fire-and-forget handler

        // Act
        var result = _router.RegisterCommand(queryCommandId, handler);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void RegisterCommand_QueryHandlerWithFireAndForgetId_ReturnsFalse()
    {
        // Arrange
        var handler = new TestQueryHandler();
        const int fireAndForgetCommandId = 1001; // Fire-and-forget range but query handler

        // Act
        var result = _router.RegisterCommand(fireAndForgetCommandId, handler);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void UnregisterCommand_ExistingCommand_ReturnsTrue()
    {
        // Arrange
        var handler = new TestFireAndForgetHandler();
        const int commandId = 1001;
        _router.RegisterCommand(commandId, handler);

        // Act
        var result = _router.UnregisterCommand(commandId);

        // Assert
        Assert.That(result, Is.True);
        Assert.That(_router.IsCommandRegistered(commandId), Is.False);
    }

    [Test]
    public void UnregisterCommand_NonExistentCommand_ReturnsFalse()
    {
        // Arrange
        const int commandId = 1001;

        // Act
        var result = _router.UnregisterCommand(commandId);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public async Task RouteCommandAsync_ValidFireAndForgetCommand_ExecutesSuccessfully()
    {
        // Arrange
        var handler = new TestFireAndForgetHandler();
        const int commandId = 1001;
        _router.RegisterCommand(commandId, handler);

        var message = new RpcMessage
        {
            CommandId = commandId,
            Version = 1,
            CommandType = RpcCommandType.FireAndForget,
            Parameters = new RpcParameters(),
            Raw = "ESC[>1001;1;F"
        };

        // Act
        var result = await _router.RouteCommandAsync(message);

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(handler.ExecuteCallCount, Is.EqualTo(1));
    }

    [Test]
    public async Task RouteCommandAsync_ValidQueryCommand_ExecutesSuccessfully()
    {
        // Arrange
        var handler = new TestQueryHandler();
        const int commandId = 2001;
        _router.RegisterCommand(commandId, handler);

        var message = new RpcMessage
        {
            CommandId = commandId,
            Version = 1,
            CommandType = RpcCommandType.Query,
            Parameters = new RpcParameters(),
            Raw = "ESC[>2001;1;Q"
        };

        // Act
        var result = await _router.RouteCommandAsync(message);

        // Assert
        Assert.That(result.Success, Is.True);
        Assert.That(result.Data, Is.EqualTo("test-response"));
        Assert.That(handler.ExecuteCallCount, Is.EqualTo(1));
    }

    [Test]
    public async Task RouteCommandAsync_UnregisteredCommand_ReturnsFailure()
    {
        // Arrange
        var message = new RpcMessage
        {
            CommandId = 1001,
            Version = 1,
            CommandType = RpcCommandType.FireAndForget,
            Parameters = new RpcParameters(),
            Raw = "ESC[>1001;1;F"
        };

        // Act
        var result = await _router.RouteCommandAsync(message);

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.ErrorMessage, Does.Contain("No handler registered"));
    }

    [Test]
    public async Task RouteCommandAsync_InvalidCommandIdRange_ReturnsFailure()
    {
        // Arrange
        var message = new RpcMessage
        {
            CommandId = 500, // Invalid range
            Version = 1,
            CommandType = RpcCommandType.FireAndForget,
            Parameters = new RpcParameters(),
            Raw = "ESC[>500;1;F"
        };

        // Act
        var result = await _router.RouteCommandAsync(message);

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.ErrorMessage, Does.Contain("Invalid command ID"));
    }

    [Test]
    public void GetRegisteredCommands_MultipleCommands_ReturnsAllIds()
    {
        // Arrange
        var handler1 = new TestFireAndForgetHandler();
        var handler2 = new TestQueryHandler();
        _router.RegisterCommand(1001, handler1);
        _router.RegisterCommand(2001, handler2);

        // Act
        var commands = _router.GetRegisteredCommands().ToArray();

        // Assert
        Assert.That(commands, Has.Length.EqualTo(2));
        Assert.That(commands, Does.Contain(1001));
        Assert.That(commands, Does.Contain(2001));
    }

    [Test]
    public async Task RouteCommandAsync_HandlerThrowsArgumentException_ReturnsFailureWithSpecificError()
    {
        // Arrange
        var handler = new ThrowingHandler(new ArgumentException("Invalid parameter value"), isFireAndForget: true);
        const int commandId = 1001;
        _router.RegisterCommand(commandId, handler);

        var message = new RpcMessage
        {
            CommandId = commandId,
            Version = 1,
            CommandType = RpcCommandType.FireAndForget,
            Parameters = new RpcParameters(),
            Raw = "ESC[>1001;1;F"
        };

        // Act
        var result = await _router.RouteCommandAsync(message);

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.ErrorMessage, Does.Contain("Invalid parameters for command"));
        Assert.That(result.ErrorMessage, Does.Contain("Invalid parameter value"));
    }

    [Test]
    public async Task RouteCommandAsync_HandlerThrowsInvalidOperationException_ReturnsFailureWithSpecificError()
    {
        // Arrange
        var handler = new ThrowingHandler(new InvalidOperationException("Operation not allowed"), isFireAndForget: false);
        const int commandId = 2001;
        _router.RegisterCommand(commandId, handler);

        var message = new RpcMessage
        {
            CommandId = commandId,
            Version = 1,
            CommandType = RpcCommandType.Query,
            Parameters = new RpcParameters(),
            Raw = "ESC[>2001;1;Q"
        };

        // Act
        var result = await _router.RouteCommandAsync(message);

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.ErrorMessage, Does.Contain("Invalid operation for query"));
        Assert.That(result.ErrorMessage, Does.Contain("Operation not allowed"));
    }

    [Test]
    public async Task RouteCommandAsync_QueryCommandTimesOut_ReturnsTimeoutResult()
    {
        // Arrange
        var handler = new SlowQueryHandler(TimeSpan.FromMilliseconds(50)); // Very short timeout
        const int commandId = 2001;
        _router.RegisterCommand(commandId, handler);

        var message = new RpcMessage
        {
            CommandId = commandId,
            Version = 1,
            CommandType = RpcCommandType.Query,
            Parameters = new RpcParameters(),
            Raw = "ESC[>2001;1;Q"
        };

        // Act
        var result = await _router.RouteCommandAsync(message);

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.IsTimeout, Is.True);
        Assert.That(result.CommandId, Is.EqualTo(commandId));
        Assert.That(result.ErrorMessage, Does.Contain("timed out"));
    }

    [Test]
    public async Task RouteCommandAsync_HandlerThrowsUnexpectedException_ReturnsFailureWithGenericError()
    {
        // Arrange
        var handler = new ThrowingHandler(new NotImplementedException("Feature not implemented"), isFireAndForget: true);
        const int commandId = 1001;
        _router.RegisterCommand(commandId, handler);

        var message = new RpcMessage
        {
            CommandId = commandId,
            Version = 1,
            CommandType = RpcCommandType.FireAndForget,
            Parameters = new RpcParameters(),
            Raw = "ESC[>1001;1;F"
        };

        // Act
        var result = await _router.RouteCommandAsync(message);

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(result.ErrorMessage, Does.Contain("Unexpected error executing command"));
        Assert.That(result.ErrorMessage, Does.Contain("Feature not implemented"));
    }

    [Test]
    public void ClearAllCommands_RemovesAllRegisteredCommands()
    {
        // Arrange
        var handler1 = new TestFireAndForgetHandler();
        var handler2 = new TestQueryHandler();
        _router.RegisterCommand(1001, handler1);
        _router.RegisterCommand(2001, handler2);

        // Act
        _router.ClearAllCommands();

        // Assert
        Assert.That(_router.GetRegisteredCommands(), Is.Empty);
        Assert.That(_router.IsCommandRegistered(1001), Is.False);
        Assert.That(_router.IsCommandRegistered(2001), Is.False);
    }

    // Test helper classes
    private class TestFireAndForgetHandler : IRpcCommandHandler
    {
        public int ExecuteCallCount { get; private set; }
        public bool IsFireAndForget => true;
        public TimeSpan Timeout => TimeSpan.FromSeconds(5);
        public string Description => "Test fire-and-forget handler";

        public Task<object?> ExecuteAsync(RpcParameters parameters)
        {
            ExecuteCallCount++;
            return Task.FromResult<object?>(null);
        }
    }

    private class TestQueryHandler : IRpcCommandHandler
    {
        public int ExecuteCallCount { get; private set; }
        public bool IsFireAndForget => false;
        public TimeSpan Timeout => TimeSpan.FromSeconds(5);
        public string Description => "Test query handler";

        public Task<object?> ExecuteAsync(RpcParameters parameters)
        {
            ExecuteCallCount++;
            return Task.FromResult<object?>("test-response");
        }
    }

    private class ThrowingHandler : IRpcCommandHandler
    {
        private readonly Exception _exceptionToThrow;
        private readonly bool _isFireAndForget;

        public ThrowingHandler(Exception exceptionToThrow, bool isFireAndForget = true)
        {
            _exceptionToThrow = exceptionToThrow;
            _isFireAndForget = isFireAndForget;
        }

        public bool IsFireAndForget => _isFireAndForget;
        public TimeSpan Timeout => TimeSpan.FromSeconds(5);
        public string Description => "Test throwing handler";

        public Task<object?> ExecuteAsync(RpcParameters parameters)
        {
            throw _exceptionToThrow;
        }
    }

    private class SlowQueryHandler : IRpcCommandHandler
    {
        private readonly TimeSpan _timeout;

        public SlowQueryHandler(TimeSpan timeout)
        {
            _timeout = timeout;
        }

        public bool IsFireAndForget => false;
        public TimeSpan Timeout => _timeout;
        public string Description => "Test slow query handler";

        public async Task<object?> ExecuteAsync(RpcParameters parameters)
        {
            // Simulate a slow operation that will timeout
            await Task.Delay(TimeSpan.FromSeconds(1));
            return "slow-response";
        }
    }

    private class TestLogger : ILogger<RpcCommandRouter>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }
}