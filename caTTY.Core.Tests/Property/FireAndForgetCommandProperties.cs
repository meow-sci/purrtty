using caTTY.Core.Rpc;
using FsCheck;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace caTTY.Core.Tests.Property;

/// <summary>
/// Property-based tests for fire-and-forget command execution.
/// These tests verify universal properties that should hold for fire-and-forget RPC commands.
/// </summary>
[TestFixture]
[Category("Property")]
public class FireAndForgetCommandProperties
{
    /// <summary>
    /// Generator for valid fire-and-forget command IDs (1000-1999).
    /// </summary>
    public static Arbitrary<int> ValidFireAndForgetCommandIdArb =>
        Arb.From(Gen.Choose(1000, 1999));

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
    /// **Feature: term-sequence-rpc, Property 5: Fire-and-Forget Command Execution**
    /// **Validates: Requirements 2.1, 2.2**
    /// Property: For any fire-and-forget command (1000-1999 range with final character 'F'), 
    /// the RPC system should invoke the corresponding game action immediately and never send a response.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property FireAndForgetCommandExecution()
    {
        return Prop.ForAll(ValidFireAndForgetCommandIdArb, ValidRpcParametersArb,
            (int commandId, RpcParameters parameters) =>
        {
            // Arrange - Create router and fire-and-forget handler
            var logger = new TestLogger();
            var router = new RpcCommandRouter(logger);
            var handler = new TestFireAndForgetHandler();

            // Register the fire-and-forget command
            bool registrationSuccess = router.RegisterCommand(commandId, handler);

            // Create a fire-and-forget RPC message
            var message = new RpcMessage
            {
                CommandId = commandId,
                Version = 1,
                CommandType = RpcCommandType.FireAndForget,
                Parameters = parameters,
                Raw = $"\x1b[>{commandId};1;F"
            };

            // Act - Execute the fire-and-forget command
            var routingTask = router.RouteCommandAsync(message);
            var result = routingTask.GetAwaiter().GetResult();

            // Assert - Verify fire-and-forget behavior
            
            // 1. Registration should succeed for valid fire-and-forget command ID
            bool validRegistration = registrationSuccess;

            // 2. Command should execute successfully (Requirements 2.1)
            bool executionSuccess = result.Success;

            // 3. Handler should be invoked exactly once
            bool handlerInvoked = handler.ExecuteCallCount == 1;

            // 4. Parameters should be passed correctly to handler
            bool parametersPassedCorrectly = 
                handler.LastParameters?.NumericParameters.SequenceEqual(parameters.NumericParameters) == true;

            // 5. Fire-and-forget commands should not return response data (Requirements 2.2)
            bool noResponseData = result.Data == null;

            // 6. Command should execute immediately (no timeout handling for fire-and-forget)
            bool executedImmediately = result.ExecutionTime < TimeSpan.FromSeconds(1);

            // 7. Message should be correctly identified as fire-and-forget
            bool correctCommandType = message.IsFireAndForget && !message.IsQuery;

            // 8. Command ID should be in valid fire-and-forget range
            bool validCommandIdRange = message.IsValidCommandIdRange();

            return validRegistration && executionSuccess && handlerInvoked && 
                   parametersPassedCorrectly && noResponseData && executedImmediately &&
                   correctCommandType && validCommandIdRange;
        });
    }

    /// <summary>
    /// **Feature: term-sequence-rpc, Property 5b: Fire-and-Forget Handler Validation**
    /// **Validates: Requirements 2.1, 2.2**
    /// Property: For any fire-and-forget handler, it should be correctly identified as 
    /// fire-and-forget and should not expect responses.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property FireAndForgetHandlerValidation()
    {
        return Prop.ForAll(ValidFireAndForgetCommandIdArb, ValidRpcParametersArb,
            (int commandId, RpcParameters parameters) =>
        {
            // Arrange
            var logger = new TestLogger();
            var router = new RpcCommandRouter(logger);
            var handler = new TestFireAndForgetHandler();

            // Act - Register and execute
            router.RegisterCommand(commandId, handler);
            
            var message = new RpcMessage
            {
                CommandId = commandId,
                Version = 1,
                CommandType = RpcCommandType.FireAndForget,
                Parameters = parameters,
                Raw = $"\x1b[>{commandId};1;F"
            };

            var result = router.RouteCommandAsync(message).GetAwaiter().GetResult();

            // Assert - Verify handler characteristics
            
            // 1. Handler should be identified as fire-and-forget
            bool isFireAndForget = handler.IsFireAndForget;

            // 2. Handler should execute successfully
            bool executionSuccess = result.Success;

            // 3. Handler should not return response data
            bool noResponseData = result.Data == null;

            // 4. Handler should have reasonable timeout (even though not used for fire-and-forget)
            bool hasReasonableTimeout = handler.Timeout > TimeSpan.Zero && handler.Timeout <= TimeSpan.FromMinutes(5);

            // 5. Handler should have a description
            bool hasDescription = !string.IsNullOrEmpty(handler.Description);

            return isFireAndForget && executionSuccess && noResponseData && 
                   hasReasonableTimeout && hasDescription;
        });
    }

    /// <summary>
    /// **Feature: term-sequence-rpc, Property 5c: Fire-and-Forget Command Type Validation**
    /// **Validates: Requirements 2.1, 2.2**
    /// Property: For any command with fire-and-forget command type, it should only be accepted 
    /// if the command ID is in the fire-and-forget range (1000-1999).
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property FireAndForgetCommandTypeValidation()
    {
        var invalidCommandIdGen = Gen.OneOf(
            Gen.Choose(int.MinValue, 999),
            Gen.Choose(2000, int.MaxValue)
        );

        return Prop.ForAll(Arb.From(invalidCommandIdGen), ValidRpcParametersArb,
            (int invalidCommandId, RpcParameters parameters) =>
        {
            // Arrange
            var logger = new TestLogger();
            var router = new RpcCommandRouter(logger);
            var handler = new TestFireAndForgetHandler();

            // Act - Try to register fire-and-forget handler with invalid command ID
            bool registrationResult = router.RegisterCommand(invalidCommandId, handler);

            // Create message with invalid command ID but fire-and-forget type
            var message = new RpcMessage
            {
                CommandId = invalidCommandId,
                Version = 1,
                CommandType = RpcCommandType.FireAndForget,
                Parameters = parameters,
                Raw = $"\x1b[>{invalidCommandId};1;F"
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
    /// **Feature: term-sequence-rpc, Property 5d: Fire-and-Forget Exception Handling**
    /// **Validates: Requirements 2.1, 2.2**
    /// Property: For any fire-and-forget command that throws an exception during execution,
    /// the RPC system should handle the exception gracefully and return a failure result.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property FireAndForgetExceptionHandling()
    {
        return Prop.ForAll(ValidFireAndForgetCommandIdArb, ValidRpcParametersArb,
            (int commandId, RpcParameters parameters) =>
        {
            // Arrange - Create handler that throws exception
            var logger = new TestLogger();
            var router = new RpcCommandRouter(logger);
            var handler = new ThrowingFireAndForgetHandler();

            router.RegisterCommand(commandId, handler);

            var message = new RpcMessage
            {
                CommandId = commandId,
                Version = 1,
                CommandType = RpcCommandType.FireAndForget,
                Parameters = parameters,
                Raw = $"\x1b[>{commandId};1;F"
            };

            // Act - Execute command that will throw
            var result = router.RouteCommandAsync(message).GetAwaiter().GetResult();

            // Assert - Exception should be handled gracefully
            
            // 1. Result should indicate failure
            bool executionFailed = !result.Success;

            // 2. Error message should be present
            bool hasErrorMessage = !string.IsNullOrEmpty(result.ErrorMessage);

            // 3. No response data should be returned
            bool noResponseData = result.Data == null;

            // 4. Execution time should be recorded
            bool hasExecutionTime = result.ExecutionTime >= TimeSpan.Zero;

            // 5. Handler should have been called
            bool handlerCalled = handler.ExecuteCallCount == 1;

            return executionFailed && hasErrorMessage && noResponseData && 
                   hasExecutionTime && handlerCalled;
        });
    }

    // Test helper classes
    private class TestFireAndForgetHandler : IRpcCommandHandler
    {
        public int ExecuteCallCount { get; private set; }
        public RpcParameters? LastParameters { get; private set; }
        public bool IsFireAndForget => true;
        public TimeSpan Timeout => TimeSpan.FromSeconds(5);
        public string Description => "Test fire-and-forget handler";

        public Task<object?> ExecuteAsync(RpcParameters parameters)
        {
            ExecuteCallCount++;
            LastParameters = parameters;
            return Task.FromResult<object?>(null);
        }
    }

    private class ThrowingFireAndForgetHandler : IRpcCommandHandler
    {
        public int ExecuteCallCount { get; private set; }
        public bool IsFireAndForget => true;
        public TimeSpan Timeout => TimeSpan.FromSeconds(5);
        public string Description => "Test throwing fire-and-forget handler";

        public Task<object?> ExecuteAsync(RpcParameters parameters)
        {
            ExecuteCallCount++;
            throw new InvalidOperationException("Test exception from fire-and-forget handler");
        }
    }

    private class TestLogger : ILogger<RpcCommandRouter>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }
}