using caTTY.Core.Rpc;
using FsCheck;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace caTTY.Core.Tests.Property;

/// <summary>
/// Property-based tests for RPC exception safety.
/// These tests verify that the RPC system handles exceptions gracefully without crashing.
/// </summary>
[TestFixture]
[Category("Property")]
public class RpcExceptionSafetyProperties
{
    /// <summary>
    /// Generator for valid fire-and-forget command IDs (1000-1999).
    /// </summary>
    public static Arbitrary<int> ValidFireAndForgetCommandIdArb =>
        Arb.From(Gen.Choose(1000, 1999));

    /// <summary>
    /// Generator for valid query command IDs (2000-2999).
    /// </summary>
    public static Arbitrary<int> ValidQueryCommandIdArb =>
        Arb.From(Gen.Choose(2000, 2999));

    /// <summary>
    /// Generator for valid RPC parameters with numeric values.
    /// </summary>
    public static Arbitrary<RpcParameters> ValidRpcParametersArb =>
        Arb.From(Gen.ArrayOf(Gen.Choose(0, 9999))
            .Select(nums => new RpcParameters
            {
                NumericParameters = nums ?? Array.Empty<int>()
            }));

    /// <summary>
    /// Generator for different types of exceptions that handlers might throw.
    /// </summary>
    public static Arbitrary<Exception> ExceptionArb =>
        Arb.From(Gen.OneOf<Exception>(
            Gen.Constant<Exception>(new InvalidOperationException("Test invalid operation")),
            Gen.Constant<Exception>(new ArgumentException("Test argument exception")),
            Gen.Constant<Exception>(new NotSupportedException("Test not supported")),
            Gen.Constant<Exception>(new TimeoutException("Test timeout")),
            Gen.Constant<Exception>(new ApplicationException("Test application exception")),
            Gen.Constant<Exception>(new SystemException("Test system exception")),
            Gen.Constant<Exception>(new Exception("Test generic exception"))
        ));

    /// <summary>
    /// **Feature: term-sequence-rpc, Property 12: Exception Safety**
    /// **Validates: Requirements 6.2**
    /// Property: For any command handler that throws an exception, the RPC system should 
    /// catch it, log the error, and continue operating without crashing the terminal.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property ExceptionSafetyForFireAndForgetCommands()
    {
        return Prop.ForAll(ValidFireAndForgetCommandIdArb, ValidRpcParametersArb, ExceptionArb,
            (int commandId, RpcParameters parameters, Exception exceptionToThrow) =>
        {
            // Arrange - Create router with exception-throwing handler
            var logger = new TestLogger();
            var router = new RpcCommandRouter(logger);
            var throwingHandler = new ThrowingFireAndForgetHandler(exceptionToThrow);

            // Register the exception-throwing handler
            bool registered = router.RegisterCommand(commandId, throwingHandler);

            // Create RPC message
            var message = new RpcMessage
            {
                CommandId = commandId,
                Version = 1,
                CommandType = RpcCommandType.FireAndForget,
                Parameters = parameters,
                Raw = $"\x1b[>{commandId};1;F"
            };

            // Act - Execute command that will throw exception
            var routingTask = router.RouteCommandAsync(message);
            var result = routingTask.GetAwaiter().GetResult();

            // Assert - System should handle exception gracefully
            bool registrationSucceeded = registered;
            bool executionCompleted = routingTask.IsCompleted && !routingTask.IsFaulted;
            bool resultIndicatesFailure = !result.Success;
            bool errorMessagePresent = !string.IsNullOrEmpty(result.ErrorMessage);
            bool handlerWasCalled = throwingHandler.ExecuteCallCount == 1;

            // Verify router is still functional after exception
            var testHandler = new TestFireAndForgetHandler();
            int testCommandId = commandId == 1001 ? 1002 : 1001; // Use different ID
            bool canRegisterAfterException = router.RegisterCommand(testCommandId, testHandler);

            var testMessage = new RpcMessage
            {
                CommandId = testCommandId,
                Version = 1,
                CommandType = RpcCommandType.FireAndForget,
                Parameters = parameters,
                Raw = $"\x1b[>{testCommandId};1;F"
            };

            var testTask = router.RouteCommandAsync(testMessage);
            var testResult = testTask.GetAwaiter().GetResult();
            bool routerStillFunctional = testResult.Success && testHandler.ExecuteCallCount == 1;

            return registrationSucceeded && executionCompleted && resultIndicatesFailure && 
                   errorMessagePresent && handlerWasCalled && canRegisterAfterException && 
                   routerStillFunctional;
        });
    }

    /// <summary>
    /// **Feature: term-sequence-rpc, Property 12b: Exception Safety for Query Commands**
    /// **Validates: Requirements 6.2**
    /// Property: For any query command handler that throws an exception, the RPC system should 
    /// catch it, log the error, return a failure result, and continue operating normally.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property ExceptionSafetyForQueryCommands()
    {
        return Prop.ForAll(ValidQueryCommandIdArb, ValidRpcParametersArb, ExceptionArb,
            (int commandId, RpcParameters parameters, Exception exceptionToThrow) =>
        {
            // Arrange - Create router with exception-throwing query handler
            var logger = new TestLogger();
            var router = new RpcCommandRouter(logger);
            var throwingHandler = new ThrowingQueryHandler(exceptionToThrow);

            // Register the exception-throwing handler
            bool registered = router.RegisterCommand(commandId, throwingHandler);

            // Create RPC message
            var message = new RpcMessage
            {
                CommandId = commandId,
                Version = 1,
                CommandType = RpcCommandType.Query,
                Parameters = parameters,
                Raw = $"\x1b[>{commandId};1;Q"
            };

            // Act - Execute query that will throw exception
            var routingTask = router.RouteCommandAsync(message);
            var result = routingTask.GetAwaiter().GetResult();

            // Assert - System should handle exception gracefully
            bool registrationSucceeded = registered;
            bool executionCompleted = routingTask.IsCompleted && !routingTask.IsFaulted;
            bool resultIndicatesFailure = !result.Success;
            bool errorMessagePresent = !string.IsNullOrEmpty(result.ErrorMessage);
            bool handlerWasCalled = throwingHandler.ExecuteCallCount == 1;

            // Verify router is still functional after exception
            var testHandler = new TestQueryHandler();
            int testCommandId = commandId == 2001 ? 2002 : 2001; // Use different ID
            bool canRegisterAfterException = router.RegisterCommand(testCommandId, testHandler);

            var testMessage = new RpcMessage
            {
                CommandId = testCommandId,
                Version = 1,
                CommandType = RpcCommandType.Query,
                Parameters = parameters,
                Raw = $"\x1b[>{testCommandId};1;Q"
            };

            var testTask = router.RouteCommandAsync(testMessage);
            var testResult = testTask.GetAwaiter().GetResult();
            bool routerStillFunctional = testResult.Success && testHandler.ExecuteCallCount == 1;

            return registrationSucceeded && executionCompleted && resultIndicatesFailure && 
                   errorMessagePresent && handlerWasCalled && canRegisterAfterException && 
                   routerStillFunctional;
        });
    }

    /// <summary>
    /// **Feature: term-sequence-rpc, Property 12c: Multiple Exception Resilience**
    /// **Validates: Requirements 6.2**
    /// Property: For any sequence of commands where multiple handlers throw exceptions, 
    /// the RPC system should handle each exception independently and continue processing 
    /// subsequent commands normally.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property MultipleExceptionResilience()
    {
        return Prop.ForAll(ValidFireAndForgetCommandIdArb, ValidQueryCommandIdArb, ValidRpcParametersArb,
            (int fireAndForgetId, int queryId, RpcParameters parameters) =>
        {
            // Arrange - Create router with multiple exception-throwing handlers
            var logger = new TestLogger();
            var router = new RpcCommandRouter(logger);
            
            var throwingFireAndForgetHandler = new ThrowingFireAndForgetHandler(new InvalidOperationException("Fire-and-forget error"));
            var throwingQueryHandler = new ThrowingQueryHandler(new ArgumentException("Query error"));
            var normalHandler = new TestFireAndForgetHandler();

            // Register handlers
            int normalCommandId = fireAndForgetId == 1001 ? 1003 : 1001;
            router.RegisterCommand(fireAndForgetId, throwingFireAndForgetHandler);
            router.RegisterCommand(queryId, throwingQueryHandler);
            router.RegisterCommand(normalCommandId, normalHandler);

            // Create messages
            var fireAndForgetMessage = new RpcMessage
            {
                CommandId = fireAndForgetId,
                Version = 1,
                CommandType = RpcCommandType.FireAndForget,
                Parameters = parameters,
                Raw = $"\x1b[>{fireAndForgetId};1;F"
            };

            var queryMessage = new RpcMessage
            {
                CommandId = queryId,
                Version = 1,
                CommandType = RpcCommandType.Query,
                Parameters = parameters,
                Raw = $"\x1b[>{queryId};1;Q"
            };

            var normalMessage = new RpcMessage
            {
                CommandId = normalCommandId,
                Version = 1,
                CommandType = RpcCommandType.FireAndForget,
                Parameters = parameters,
                Raw = $"\x1b[>{normalCommandId};1;F"
            };

            // Act - Execute commands in sequence
            var fireAndForgetResult = router.RouteCommandAsync(fireAndForgetMessage).GetAwaiter().GetResult();
            var queryResult = router.RouteCommandAsync(queryMessage).GetAwaiter().GetResult();
            var normalResult = router.RouteCommandAsync(normalMessage).GetAwaiter().GetResult();

            // Assert - Exception handlers should fail, normal handler should succeed
            bool fireAndForgetFailed = !fireAndForgetResult.Success && !string.IsNullOrEmpty(fireAndForgetResult.ErrorMessage);
            bool queryFailed = !queryResult.Success && !string.IsNullOrEmpty(queryResult.ErrorMessage);
            bool normalSucceeded = normalResult.Success;

            // Verify all handlers were called
            bool allHandlersCalled = throwingFireAndForgetHandler.ExecuteCallCount == 1 &&
                                   throwingQueryHandler.ExecuteCallCount == 1 &&
                                   normalHandler.ExecuteCallCount == 1;

            // Verify router is still functional
            var additionalResult = router.RouteCommandAsync(normalMessage).GetAwaiter().GetResult();
            bool routerStillFunctional = additionalResult.Success && normalHandler.ExecuteCallCount == 2;

            return fireAndForgetFailed && queryFailed && normalSucceeded && 
                   allHandlersCalled && routerStillFunctional;
        });
    }

    // Test helper classes
    private class ThrowingFireAndForgetHandler : IRpcCommandHandler
    {
        private readonly Exception _exceptionToThrow;

        public ThrowingFireAndForgetHandler(Exception exceptionToThrow)
        {
            _exceptionToThrow = exceptionToThrow;
        }

        public int ExecuteCallCount { get; private set; }
        public bool IsFireAndForget => true;
        public TimeSpan Timeout => TimeSpan.FromSeconds(5);
        public string Description => "Test exception-throwing fire-and-forget handler";

        public Task<object?> ExecuteAsync(RpcParameters parameters)
        {
            ExecuteCallCount++;
            throw _exceptionToThrow;
        }
    }

    private class ThrowingQueryHandler : IRpcCommandHandler
    {
        private readonly Exception _exceptionToThrow;

        public ThrowingQueryHandler(Exception exceptionToThrow)
        {
            _exceptionToThrow = exceptionToThrow;
        }

        public int ExecuteCallCount { get; private set; }
        public bool IsFireAndForget => false;
        public TimeSpan Timeout => TimeSpan.FromSeconds(5);
        public string Description => "Test exception-throwing query handler";

        public Task<object?> ExecuteAsync(RpcParameters parameters)
        {
            ExecuteCallCount++;
            throw _exceptionToThrow;
        }
    }

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

    private class TestLogger : ILogger<RpcCommandRouter>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }
}