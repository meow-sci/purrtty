using System;
using System.Threading.Tasks;
using caTTY.Core.Rpc;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace caTTY.Core.Tests.Unit.Rpc;

[TestFixture]
[Category("Unit")]
public class RpcHandlerTests
{
    private TestCommandRouter _commandRouter = null!;
    private TestResponseGenerator _responseGenerator = null!;
    private TestLogger _logger = null!;
    private List<byte[]> _outputBuffer = null!;
    private RpcHandler _rpcHandler = null!;

    [SetUp]
    public void SetUp()
    {
        _commandRouter = new TestCommandRouter();
        _responseGenerator = new TestResponseGenerator();
        _logger = new TestLogger();
        _outputBuffer = new List<byte[]>();

        _rpcHandler = new RpcHandler(
            _commandRouter,
            _responseGenerator,
            bytes => _outputBuffer.Add(bytes),
            _logger);
    }

    [Test]
    public void IsEnabled_DefaultsToTrue()
    {
        // Assert
        Assert.That(_rpcHandler.IsEnabled, Is.True);
    }

    [Test]
    public void HandleRpcMessage_WhenDisabled_IgnoresMessage()
    {
        // Arrange
        _rpcHandler.IsEnabled = false;
        var message = new RpcMessage
        {
            CommandId = 1001,
            CommandType = RpcCommandType.FireAndForget,
            Raw = "ESC[>1001;1;F"
        };

        // Act
        _rpcHandler.HandleRpcMessage(message);

        // Assert
        Assert.That(_commandRouter.RouteCallCount, Is.EqualTo(0));
    }

    [Test]
    public void HandleRpcMessage_WithInvalidCommandIdRange_LogsWarning()
    {
        // Arrange
        var message = new RpcMessage
        {
            CommandId = 500, // Invalid for fire-and-forget (should be 1000-1999)
            CommandType = RpcCommandType.FireAndForget,
            Raw = "ESC[>500;1;F"
        };

        // Act
        _rpcHandler.HandleRpcMessage(message);

        // Assert
        Assert.That(_logger.WarningMessages, Has.Count.EqualTo(1));
        Assert.That(_logger.WarningMessages[0], Does.Contain("Invalid command ID"));
    }

    [Test]
    public void HandleRpcMessage_WithValidFireAndForgetCommand_RoutesToCommandRouter()
    {
        // Arrange
        var message = new RpcMessage
        {
            CommandId = 1001,
            CommandType = RpcCommandType.FireAndForget,
            Raw = "ESC[>1001;1;F"
        };

        _commandRouter.SetResult(RpcResult.CreateSuccess());

        // Act
        _rpcHandler.HandleRpcMessage(message);

        // Allow async task to complete
        Task.Delay(100).Wait();

        // Assert
        Assert.That(_commandRouter.RouteCallCount, Is.EqualTo(1));
        Assert.That(_commandRouter.LastMessage, Is.EqualTo(message));
    }

    [Test]
    public void HandleRpcMessage_WithValidQueryCommand_RoutesToCommandRouterAndGeneratesResponse()
    {
        // Arrange
        var message = new RpcMessage
        {
            CommandId = 2001,
            CommandType = RpcCommandType.Query,
            Raw = "ESC[>2001;1;Q"
        };

        var responseData = new { Status = "Active", Throttle = 75 };
        var result = RpcResult.CreateSuccess(responseData);
        var responseBytes = System.Text.Encoding.ASCII.GetBytes("ESC[>2001;1;R");

        _commandRouter.SetResult(result);
        _responseGenerator.SetResponseBytes(responseBytes);

        // Act
        _rpcHandler.HandleRpcMessage(message);

        // Allow async task to complete
        Task.Delay(100).Wait();

        // Assert
        Assert.That(_commandRouter.RouteCallCount, Is.EqualTo(1));
        Assert.That(_responseGenerator.GenerateResponseCallCount, Is.EqualTo(1));
        Assert.That(_outputBuffer, Has.Count.EqualTo(1));
        Assert.That(_outputBuffer[0], Is.EqualTo(responseBytes));
    }

    [Test]
    public void HandleRpcMessage_WithFailedQueryCommand_GeneratesErrorResponse()
    {
        // Arrange
        var message = new RpcMessage
        {
            CommandId = 2001,
            CommandType = RpcCommandType.Query,
            Raw = "ESC[>2001;1;Q"
        };

        var result = RpcResult.CreateFailure("Command not found");
        var errorBytes = System.Text.Encoding.ASCII.GetBytes("ESC[>9999;1;E");

        _commandRouter.SetResult(result);
        _responseGenerator.SetErrorBytes(errorBytes);

        // Act
        _rpcHandler.HandleRpcMessage(message);

        // Allow async task to complete
        Task.Delay(100).Wait();

        // Assert
        Assert.That(_responseGenerator.GenerateErrorCallCount, Is.EqualTo(1));
        Assert.That(_outputBuffer, Has.Count.EqualTo(1));
        Assert.That(_outputBuffer[0], Is.EqualTo(errorBytes));
    }

    [Test]
    public void HandleMalformedRpcSequence_WhenDisabled_DoesNotLog()
    {
        // Arrange
        _rpcHandler.IsEnabled = false;
        var rawSequence = System.Text.Encoding.ASCII.GetBytes("ESC[>invalid");

        // Act
        _rpcHandler.HandleMalformedRpcSequence(rawSequence, RpcSequenceType.Malformed);

        // Assert
        Assert.That(_logger.WarningMessages, Has.Count.EqualTo(0));
    }

    [Test]
    public void HandleMalformedRpcSequence_WithMalformedSequence_LogsWarning()
    {
        // Arrange
        var rawSequence = System.Text.Encoding.ASCII.GetBytes("ESC[>invalid");

        // Act
        _rpcHandler.HandleMalformedRpcSequence(rawSequence, RpcSequenceType.Malformed);

        // Assert
        Assert.That(_logger.WarningMessages, Has.Count.EqualTo(1));
        Assert.That(_logger.WarningMessages[0], Does.Contain("Malformed RPC sequence"));
    }

    [Test]
    public void HandleMalformedRpcSequence_WithInvalidCommandId_LogsAppropriateMessage()
    {
        // Arrange
        var rawSequence = System.Text.Encoding.ASCII.GetBytes("ESC[>500;1;F");

        // Act
        _rpcHandler.HandleMalformedRpcSequence(rawSequence, RpcSequenceType.InvalidCommandId);

        // Assert
        Assert.That(_logger.DebugMessages, Has.Count.EqualTo(1));
        Assert.That(_logger.DebugMessages[0], Does.Contain("invalid command ID range"));
    }

    [Test]
    public void Constructor_WithNullCommandRouter_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new RpcHandler(
            null!,
            _responseGenerator,
            bytes => { },
            _logger));
    }

    [Test]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new RpcHandler(
            _commandRouter,
            _responseGenerator,
            bytes => { },
            null!));
    }

    [Test]
    public void HandleRpcMessage_WithCommandRouterException_LogsErrorAndHandlesGracefully()
    {
        // Arrange
        var message = new RpcMessage
        {
            CommandId = 1001,
            CommandType = RpcCommandType.FireAndForget,
            Raw = "ESC[>1001;1;F"
        };

        _commandRouter.SetException(new InvalidOperationException("Router failure"));

        // Act
        _rpcHandler.HandleRpcMessage(message);

        // Allow async task to complete
        Task.Delay(100).Wait();

        // Assert
        Assert.That(_logger.ErrorMessages, Has.Count.GreaterThanOrEqualTo(1));
        Assert.That(_logger.ErrorMessages[0], Does.Contain("Unhandled exception while processing RPC command"));
    }

    [Test]
    public void HandleRpcMessage_WithTimeoutResult_GeneratesTimeoutResponse()
    {
        // Arrange
        var message = new RpcMessage
        {
            CommandId = 2001,
            CommandType = RpcCommandType.Query,
            Raw = "ESC[>2001;1;Q"
        };

        var timeoutResult = RpcResult.CreateTimeout(2001, "Query timed out", TimeSpan.FromMilliseconds(100));
        var timeoutBytes = System.Text.Encoding.ASCII.GetBytes("ESC[>9999;1;E");

        _commandRouter.SetResult(timeoutResult);
        _responseGenerator.SetTimeoutBytes(timeoutBytes);

        // Act
        _rpcHandler.HandleRpcMessage(message);

        // Allow async task to complete
        Task.Delay(100).Wait();

        // Assert
        Assert.That(_responseGenerator.GenerateTimeoutCallCount, Is.EqualTo(1));
        Assert.That(_outputBuffer, Has.Count.EqualTo(1));
        Assert.That(_outputBuffer[0], Is.EqualTo(timeoutBytes));
        Assert.That(_logger.WarningMessages, Has.Count.EqualTo(1));
        Assert.That(_logger.WarningMessages[0], Does.Contain("timed out"));
    }

    [Test]
    public void HandleRpcMessage_WithResponseGenerationFailure_LogsErrorAndAttemptsErrorResponse()
    {
        // Arrange
        var message = new RpcMessage
        {
            CommandId = 2001,
            CommandType = RpcCommandType.Query,
            Raw = "ESC[>2001;1;Q"
        };

        var result = RpcResult.CreateSuccess("test-data");
        _commandRouter.SetResult(result);
        _responseGenerator.SetResponseException(new InvalidOperationException("Response generation failed"));

        // Act
        _rpcHandler.HandleRpcMessage(message);

        // Allow async task to complete
        Task.Delay(100).Wait();

        // Assert
        Assert.That(_logger.ErrorMessages, Has.Count.GreaterThanOrEqualTo(1));
        Assert.That(_logger.ErrorMessages[0], Does.Contain("Failed to generate or send response"));
    }

    // Test helper classes
    private class TestCommandRouter : IRpcCommandRouter
    {
        public int RouteCallCount { get; private set; }
        public RpcMessage? LastMessage { get; private set; }
        private RpcResult _result = RpcResult.CreateSuccess();
        private Exception? _exception;

        public void SetResult(RpcResult result)
        {
            _result = result;
            _exception = null;
        }

        public void SetException(Exception exception)
        {
            _exception = exception;
        }

        public Task<RpcResult> RouteCommandAsync(RpcMessage message)
        {
            RouteCallCount++;
            LastMessage = message;
            
            if (_exception != null)
            {
                throw _exception;
            }
            
            return Task.FromResult(_result);
        }

        public bool RegisterCommand(int commandId, IRpcCommandHandler handler) => true;
        public bool UnregisterCommand(int commandId) => true;
        public bool IsCommandRegistered(int commandId) => true;
        public IEnumerable<int> GetRegisteredCommands() => Array.Empty<int>();
        public void ClearAllCommands() { }
    }

    private class TestResponseGenerator : IRpcResponseGenerator
    {
        public int GenerateResponseCallCount { get; private set; }
        public int GenerateErrorCallCount { get; private set; }
        public int GenerateTimeoutCallCount { get; private set; }
        private byte[] _responseBytes = Array.Empty<byte>();
        private byte[] _errorBytes = Array.Empty<byte>();
        private byte[] _timeoutBytes = Array.Empty<byte>();
        private Exception? _responseException;

        public void SetResponseBytes(byte[] bytes)
        {
            _responseBytes = bytes;
        }

        public void SetErrorBytes(byte[] bytes)
        {
            _errorBytes = bytes;
        }

        public void SetTimeoutBytes(byte[] bytes)
        {
            _timeoutBytes = bytes;
        }

        public void SetResponseException(Exception exception)
        {
            _responseException = exception;
        }

        public byte[] GenerateResponse(int commandId, object? data)
        {
            GenerateResponseCallCount++;
            if (_responseException != null)
            {
                throw _responseException;
            }
            return _responseBytes;
        }

        public byte[] GenerateError(int commandId, string errorMessage)
        {
            GenerateErrorCallCount++;
            return _errorBytes;
        }

        public byte[] GenerateTimeout(int commandId)
        {
            GenerateTimeoutCallCount++;
            return _timeoutBytes;
        }

        public byte[] GenerateSystemError(string errorMessage) => _errorBytes;
    }

    private class TestLogger : ILogger
    {
        public List<string> WarningMessages { get; } = new();
        public List<string> DebugMessages { get; } = new();
        public List<string> ErrorMessages { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            string message = formatter(state, exception);
            switch (logLevel)
            {
                case LogLevel.Warning:
                    WarningMessages.Add(message);
                    break;
                case LogLevel.Debug:
                    DebugMessages.Add(message);
                    break;
                case LogLevel.Error:
                    ErrorMessages.Add(message);
                    break;
            }
        }
    }
}