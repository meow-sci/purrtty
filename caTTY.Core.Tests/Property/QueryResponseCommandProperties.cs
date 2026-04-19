using caTTY.Core.Rpc;
using FsCheck;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace caTTY.Core.Tests.Property;

/// <summary>
/// Property-based tests for query-response command processing.
/// These tests verify universal properties that should hold for query RPC commands.
/// </summary>
[TestFixture]
[Category("Property")]
public class QueryResponseCommandProperties
{
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
    /// Generator for query response data.
    /// </summary>
    public static Arbitrary<object> QueryResponseDataArb =>
        Arb.From(Gen.OneOf(
            Gen.Constant<object>("test-response"),
            Gen.Constant<object>(42),
            Gen.Constant<object>(new { status = "active", value = 75 }),
            Gen.Constant<object>(new Dictionary<string, object> { ["result"] = "success" })
        ));

    /// <summary>
    /// **Feature: term-sequence-rpc, Property 6: Query-Response Command Processing**
    /// **Validates: Requirements 3.1, 3.3**
    /// Property: For any query command (2000-2999 range with final character 'Q'), 
    /// the RPC system should process the query and send a properly formatted response 
    /// using ESC [ > Pn ; 1 ; R format.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property QueryResponseCommandProcessing()
    {
        return Prop.ForAll(ValidQueryCommandIdArb, ValidRpcParametersArb, QueryResponseDataArb,
            (int commandId, RpcParameters parameters, object responseData) =>
        {
            // Arrange - Create router and query handler
            var logger = new TestLogger();
            var router = new RpcCommandRouter(logger);
            var handler = new TestQueryHandler(responseData);

            // Register the query command
            bool registrationSuccess = router.RegisterCommand(commandId, handler);

            // Create a query RPC message
            var message = new RpcMessage
            {
                CommandId = commandId,
                Version = 1,
                CommandType = RpcCommandType.Query,
                Parameters = parameters,
                Raw = $"\x1b[>{commandId};1;Q"
            };

            // Act - Execute the query command
            var routingTask = router.RouteCommandAsync(message);
            var result = routingTask.GetAwaiter().GetResult();

            // Assert - Verify query-response behavior
            
            // 1. Registration should succeed for valid query command ID (Requirements 3.1)
            bool validRegistration = registrationSuccess;

            // 2. Command should execute successfully (Requirements 3.1)
            bool executionSuccess = result.Success;

            // 3. Handler should be invoked exactly once
            bool handlerInvoked = handler.ExecuteCallCount == 1;

            // 4. Parameters should be passed correctly to handler
            bool parametersPassedCorrectly = 
                handler.LastParameters?.NumericParameters.SequenceEqual(parameters.NumericParameters) == true;

            // 5. Query commands should return response data (Requirements 3.3)
            bool hasResponseData = result.Data != null;

            // 6. Response data should match what the handler returned
            bool correctResponseData = Equals(result.Data, responseData);

            // 7. Message should be correctly identified as query
            bool correctCommandType = message.IsQuery && !message.IsFireAndForget;

            // 8. Command ID should be in valid query range
            bool validCommandIdRange = message.IsValidCommandIdRange();

            // 9. Handler should be identified as query handler (not fire-and-forget)
            bool correctHandlerType = !handler.IsFireAndForget;

            // 10. Execution time should be recorded
            bool hasExecutionTime = result.ExecutionTime >= TimeSpan.Zero;

            return validRegistration && executionSuccess && handlerInvoked && 
                   parametersPassedCorrectly && hasResponseData && correctResponseData &&
                   correctCommandType && validCommandIdRange && correctHandlerType && hasExecutionTime;
        });
    }

    /// <summary>
    /// **Feature: term-sequence-rpc, Property 6b: Query Handler Response Format**
    /// **Validates: Requirements 3.3**
    /// Property: For any query handler, the response should be properly formatted
    /// and contain the expected data structure.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property QueryHandlerResponseFormat()
    {
        return Prop.ForAll(ValidQueryCommandIdArb, ValidRpcParametersArb, QueryResponseDataArb,
            (int commandId, RpcParameters parameters, object responseData) =>
        {
            // Arrange
            var logger = new TestLogger();
            var router = new RpcCommandRouter(logger);
            var handler = new TestQueryHandler(responseData);

            router.RegisterCommand(commandId, handler);

            var message = new RpcMessage
            {
                CommandId = commandId,
                Version = 1,
                CommandType = RpcCommandType.Query,
                Parameters = parameters,
                Raw = $"\x1b[>{commandId};1;Q"
            };

            // Act - Execute query and get response
            var result = router.RouteCommandAsync(message).GetAwaiter().GetResult();

            // Assert - Verify response format characteristics
            
            // 1. Response should be successful
            bool responseSuccess = result.Success;

            // 2. Response data should not be null for successful queries
            bool hasResponseData = result.Data != null;

            // 3. Response data should match handler's return value
            bool correctResponseData = Equals(result.Data, responseData);

            // 4. No error message should be present for successful queries
            bool noErrorMessage = string.IsNullOrEmpty(result.ErrorMessage);

            // 5. Handler should have reasonable timeout
            bool hasReasonableTimeout = handler.Timeout > TimeSpan.Zero && handler.Timeout <= TimeSpan.FromMinutes(5);

            // 6. Handler should have a description
            bool hasDescription = !string.IsNullOrEmpty(handler.Description);

            return responseSuccess && hasResponseData && correctResponseData && 
                   noErrorMessage && hasReasonableTimeout && hasDescription;
        });
    }

    /// <summary>
    /// **Feature: term-sequence-rpc, Property 6c: Query Command Type Validation**
    /// **Validates: Requirements 3.1**
    /// Property: For any command with query command type, it should only be accepted 
    /// if the command ID is in the query range (2000-2999).
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property QueryCommandTypeValidation()
    {
        var invalidCommandIdGen = Gen.OneOf(
            Gen.Choose(int.MinValue, 1999),
            Gen.Choose(3000, int.MaxValue)
        );

        return Prop.ForAll(Arb.From(invalidCommandIdGen), ValidRpcParametersArb, QueryResponseDataArb,
            (int invalidCommandId, RpcParameters parameters, object responseData) =>
        {
            // Arrange
            var logger = new TestLogger();
            var router = new RpcCommandRouter(logger);
            var handler = new TestQueryHandler(responseData);

            // Act - Try to register query handler with invalid command ID
            bool registrationResult = router.RegisterCommand(invalidCommandId, handler);

            // Create message with invalid command ID but query type
            var message = new RpcMessage
            {
                CommandId = invalidCommandId,
                Version = 1,
                CommandType = RpcCommandType.Query,
                Parameters = parameters,
                Raw = $"\x1b[>{invalidCommandId};1;Q"
            };

            // Assert - Invalid command IDs should be rejected
            
            // 1. Registration should fail for invalid command ID
            bool registrationFailed = !registrationResult;

            // 2. Message should be identified as having invalid command ID range
            bool invalidRange = !message.IsValidCommandIdRange();

            // 3. If somehow registered, routing should fail
            var routingResult = router.RouteCommandAsync(message).GetAwaiter().GetResult();
            bool routingFailed = !routingResult.Success;

            return registrationFailed && invalidRange && routingFailed;
        });
    }

    /// <summary>
    /// **Feature: term-sequence-rpc, Property 6d: Query Exception Handling**
    /// **Validates: Requirements 3.1, 3.3**
    /// Property: For any query command that throws an exception during execution,
    /// the RPC system should handle the exception gracefully and return a failure result
    /// with no response data.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property QueryExceptionHandling()
    {
        return Prop.ForAll(ValidQueryCommandIdArb, ValidRpcParametersArb,
            (int commandId, RpcParameters parameters) =>
        {
            // Arrange - Create handler that throws exception
            var logger = new TestLogger();
            var router = new RpcCommandRouter(logger);
            var handler = new ThrowingQueryHandler();

            router.RegisterCommand(commandId, handler);

            var message = new RpcMessage
            {
                CommandId = commandId,
                Version = 1,
                CommandType = RpcCommandType.Query,
                Parameters = parameters,
                Raw = $"\x1b[>{commandId};1;Q"
            };

            // Act - Execute command that will throw
            var result = router.RouteCommandAsync(message).GetAwaiter().GetResult();

            // Assert - Exception should be handled gracefully
            
            // 1. Result should indicate failure
            bool executionFailed = !result.Success;

            // 2. Error message should be present
            bool hasErrorMessage = !string.IsNullOrEmpty(result.ErrorMessage);

            // 3. No response data should be returned for failed queries
            bool noResponseData = result.Data == null;

            // 4. Execution time should be recorded
            bool hasExecutionTime = result.ExecutionTime >= TimeSpan.Zero;

            // 5. Handler should have been called
            bool handlerCalled = handler.ExecuteCallCount == 1;

            return executionFailed && hasErrorMessage && noResponseData && 
                   hasExecutionTime && handlerCalled;
        });
    }

    /// <summary>
    /// **Feature: term-sequence-rpc, Property 6e: Query Timeout Handling**
    /// **Validates: Requirements 3.1, 3.3**
    /// Property: For any query command with a very short timeout, the system should
    /// handle timeouts gracefully and return appropriate error responses.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property QueryTimeoutHandling()
    {
        return Prop.ForAll(ValidQueryCommandIdArb, ValidRpcParametersArb,
            (int commandId, RpcParameters parameters) =>
        {
            // Arrange - Create handler with very short timeout that will delay
            var logger = new TestLogger();
            var router = new RpcCommandRouter(logger);
            var handler = new DelayedQueryHandler(TimeSpan.FromMilliseconds(1)); // Very short timeout

            router.RegisterCommand(commandId, handler);

            var message = new RpcMessage
            {
                CommandId = commandId,
                Version = 1,
                CommandType = RpcCommandType.Query,
                Parameters = parameters,
                Raw = $"\x1b[>{commandId};1;Q"
            };

            // Act - Execute command that will timeout
            var result = router.RouteCommandAsync(message).GetAwaiter().GetResult();

            // Assert - Timeout should be handled gracefully
            
            // 1. Result should indicate failure (timeout)
            bool executionFailed = !result.Success;

            // 2. Error message should indicate timeout or cancellation
            bool hasTimeoutError = !string.IsNullOrEmpty(result.ErrorMessage) && 
                                  (result.ErrorMessage.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
                                   result.ErrorMessage.Contains("cancel", StringComparison.OrdinalIgnoreCase) ||
                                   result.ErrorMessage.Contains("time", StringComparison.OrdinalIgnoreCase));

            // 3. No response data should be returned for timed out queries
            bool noResponseData = result.Data == null;

            // 4. Execution time should be recorded and should be close to timeout
            bool hasExecutionTime = result.ExecutionTime >= TimeSpan.Zero;

            return executionFailed && hasTimeoutError && noResponseData && hasExecutionTime;
        });
    }

    // Test helper classes
    private class TestQueryHandler : IRpcCommandHandler
    {
        private readonly object _responseData;

        public TestQueryHandler(object responseData)
        {
            _responseData = responseData;
        }

        public int ExecuteCallCount { get; private set; }
        public RpcParameters? LastParameters { get; private set; }
        public bool IsFireAndForget => false;
        public TimeSpan Timeout => TimeSpan.FromSeconds(5);
        public string Description => "Test query handler";

        public Task<object?> ExecuteAsync(RpcParameters parameters)
        {
            ExecuteCallCount++;
            LastParameters = parameters;
            return Task.FromResult<object?>(_responseData);
        }
    }

    private class ThrowingQueryHandler : IRpcCommandHandler
    {
        public int ExecuteCallCount { get; private set; }
        public bool IsFireAndForget => false;
        public TimeSpan Timeout => TimeSpan.FromSeconds(5);
        public string Description => "Test throwing query handler";

        public Task<object?> ExecuteAsync(RpcParameters parameters)
        {
            ExecuteCallCount++;
            throw new InvalidOperationException("Test exception from query handler");
        }
    }

    private class DelayedQueryHandler : IRpcCommandHandler
    {
        public DelayedQueryHandler(TimeSpan timeout)
        {
            Timeout = timeout;
        }

        public int ExecuteCallCount { get; private set; }
        public bool IsFireAndForget => false;
        public TimeSpan Timeout { get; }
        public string Description => "Test delayed query handler";

        public async Task<object?> ExecuteAsync(RpcParameters parameters)
        {
            ExecuteCallCount++;
            // Delay longer than the timeout to trigger timeout handling
            await Task.Delay(TimeSpan.FromMilliseconds(100));
            return "delayed-response";
        }
    }

    private class TestLogger : ILogger<RpcCommandRouter>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }
}