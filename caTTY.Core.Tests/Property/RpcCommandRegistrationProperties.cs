using caTTY.Core.Rpc;
using FsCheck;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace caTTY.Core.Tests.Property;

/// <summary>
/// Property-based tests for RPC command registration interface.
/// These tests verify universal properties that should hold for command registration and routing.
/// </summary>
[TestFixture]
[Category("Property")]
public class RpcCommandRegistrationProperties
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
    /// Generator for invalid command IDs (outside valid ranges).
    /// </summary>
    public static Arbitrary<int> InvalidCommandIdArb =>
        Arb.From(Gen.OneOf(
            Gen.Choose(int.MinValue, 999),
            Gen.Choose(3000, int.MaxValue)
        ));

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
    /// **Feature: term-sequence-rpc, Property 11: Command Registration Interface**
    /// **Validates: Requirements 5.1, 5.2, 5.3, 5.4**
    /// Property: For any command registration, the system should accept commands with valid 
    /// Command_ID ranges (1000-1999 for fire-and-forget, 2000-2999 for queries) and route 
    /// registered commands to their appropriate handlers with parsed parameters.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property CommandRegistrationInterface()
    {
        return Prop.ForAll(ValidFireAndForgetCommandIdArb, ValidQueryCommandIdArb, ValidRpcParametersArb,
            (int fireAndForgetId, int queryId, RpcParameters parameters) =>
        {
            // Arrange - Create router and handlers
            var logger = new TestLogger();
            var router = new RpcCommandRouter(logger);
            var fireAndForgetHandler = new TestFireAndForgetHandler();
            var queryHandler = new TestQueryHandler();

            // Act - Register commands with valid ID ranges
            bool fireAndForgetRegistered = router.RegisterCommand(fireAndForgetId, fireAndForgetHandler);
            bool queryRegistered = router.RegisterCommand(queryId, queryHandler);

            // Assert - Both registrations should succeed for valid ranges
            bool registrationSuccess = fireAndForgetRegistered && queryRegistered;

            // Verify commands are registered
            bool commandsRegistered = router.IsCommandRegistered(fireAndForgetId) && 
                                    router.IsCommandRegistered(queryId);

            // Test routing to appropriate handlers
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

            // Execute commands and verify routing
            var fireAndForgetTask = router.RouteCommandAsync(fireAndForgetMessage);
            var queryTask = router.RouteCommandAsync(queryMessage);

            // Wait for completion (should be fast for test handlers)
            var fireAndForgetResult = fireAndForgetTask.GetAwaiter().GetResult();
            var queryResult = queryTask.GetAwaiter().GetResult();

            // Verify successful execution
            bool routingSuccess = fireAndForgetResult.Success && queryResult.Success;

            // Verify handlers were called with correct parameters
            bool handlersInvoked = fireAndForgetHandler.ExecuteCallCount == 1 && 
                                 queryHandler.ExecuteCallCount == 1;

            // Verify parameters were passed correctly
            bool parametersPassedCorrectly = 
                fireAndForgetHandler.LastParameters?.NumericParameters.SequenceEqual(parameters.NumericParameters) == true &&
                queryHandler.LastParameters?.NumericParameters.SequenceEqual(parameters.NumericParameters) == true;

            return registrationSuccess && commandsRegistered && routingSuccess && 
                   handlersInvoked && parametersPassedCorrectly;
        });
    }

    /// <summary>
    /// **Feature: term-sequence-rpc, Property 11b: Invalid Command ID Rejection**
    /// **Validates: Requirements 5.1, 5.4**
    /// Property: For any command registration with invalid Command_ID ranges, 
    /// the system should reject the registration and not route commands to unregistered handlers.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property InvalidCommandIdRejection()
    {
        return Prop.ForAll(InvalidCommandIdArb, (int invalidId) =>
        {
            // Arrange
            var logger = new TestLogger();
            var router = new RpcCommandRouter(logger);
            var handler = new TestFireAndForgetHandler();

            // Act - Try to register command with invalid ID
            bool registrationResult = router.RegisterCommand(invalidId, handler);

            // Assert - Registration should fail
            bool registrationFailed = !registrationResult;
            bool commandNotRegistered = !router.IsCommandRegistered(invalidId);

            return registrationFailed && commandNotRegistered;
        });
    }

    /// <summary>
    /// **Feature: term-sequence-rpc, Property 11c: Handler Type Validation**
    /// **Validates: Requirements 5.2, 5.4**
    /// Property: For any command registration, fire-and-forget handlers should only accept 
    /// fire-and-forget command IDs (1000-1999) and query handlers should only accept 
    /// query command IDs (2000-2999).
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property HandlerTypeValidation()
    {
        return Prop.ForAll(ValidFireAndForgetCommandIdArb, ValidQueryCommandIdArb,
            (int fireAndForgetId, int queryId) =>
        {
            // Arrange
            var logger = new TestLogger();
            var router = new RpcCommandRouter(logger);
            var fireAndForgetHandler = new TestFireAndForgetHandler();
            var queryHandler = new TestQueryHandler();

            // Act - Try correct and incorrect registrations
            bool correctFireAndForgetReg = router.RegisterCommand(fireAndForgetId, fireAndForgetHandler);
            bool correctQueryReg = router.RegisterCommand(queryId, queryHandler);
            
            // Clear router for incorrect registrations test
            router.ClearAllCommands();
            
            // Try incorrect registrations (should fail)
            bool incorrectFireAndForgetReg = router.RegisterCommand(queryId, fireAndForgetHandler);
            bool incorrectQueryReg = router.RegisterCommand(fireAndForgetId, queryHandler);

            // Assert - Correct registrations should succeed, incorrect should fail
            bool correctRegistrationsSucceed = correctFireAndForgetReg && correctQueryReg;
            bool incorrectRegistrationsFail = !incorrectFireAndForgetReg && !incorrectQueryReg;

            return correctRegistrationsSucceed && incorrectRegistrationsFail;
        });
    }

    /// <summary>
    /// **Feature: term-sequence-rpc, Property 11d: Command Unregistration**
    /// **Validates: Requirements 5.1, 5.3**
    /// Property: For any registered command, unregistering it should remove it from the router
    /// and subsequent routing attempts should fail.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property CommandUnregistration()
    {
        return Prop.ForAll(ValidFireAndForgetCommandIdArb, ValidRpcParametersArb,
            (int commandId, RpcParameters parameters) =>
        {
            // Arrange
            var logger = new TestLogger();
            var router = new RpcCommandRouter(logger);
            var handler = new TestFireAndForgetHandler();

            // Register command
            router.RegisterCommand(commandId, handler);

            // Act - Unregister command
            bool unregistrationResult = router.UnregisterCommand(commandId);

            // Assert - Unregistration should succeed
            bool unregistrationSucceeded = unregistrationResult;
            bool commandNotRegistered = !router.IsCommandRegistered(commandId);

            // Try to route to unregistered command
            var message = new RpcMessage
            {
                CommandId = commandId,
                Version = 1,
                CommandType = RpcCommandType.FireAndForget,
                Parameters = parameters,
                Raw = $"\x1b[>{commandId};1;F"
            };

            var routingTask = router.RouteCommandAsync(message);
            var routingResult = routingTask.GetAwaiter().GetResult();

            // Routing should fail for unregistered command
            bool routingFailed = !routingResult.Success;

            return unregistrationSucceeded && commandNotRegistered && routingFailed;
        });
    }

    /// <summary>
    /// **Feature: term-sequence-rpc, Property 11e: Multiple Command Registration**
    /// **Validates: Requirements 5.1, 5.3, 5.4**
    /// Property: For any set of unique command IDs, the system should allow registering 
    /// multiple commands and route each to its appropriate handler.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property MultipleCommandRegistration()
    {
        var fireAndForgetIdsGen = Gen.ListOf(ValidFireAndForgetCommandIdArb.Generator)
            .Select(ids => ids.Distinct().Take(3).ToArray())
            .Where(ids => ids.Length > 0);
        
        var queryIdsGen = Gen.ListOf(ValidQueryCommandIdArb.Generator)
            .Select(ids => ids.Distinct().Take(3).ToArray())
            .Where(ids => ids.Length > 0);

        return Prop.ForAll(Arb.From(fireAndForgetIdsGen), Arb.From(queryIdsGen),
            (int[] fireAndForgetIds, int[] queryIds) =>
        {
            // Arrange
            var logger = new TestLogger();
            var router = new RpcCommandRouter(logger);
            var fireAndForgetHandlers = fireAndForgetIds.Select(_ => new TestFireAndForgetHandler()).ToArray();
            var queryHandlers = queryIds.Select(_ => new TestQueryHandler()).ToArray();

            // Act - Register all commands
            bool allFireAndForgetRegistered = fireAndForgetIds
                .Zip(fireAndForgetHandlers, (id, handler) => router.RegisterCommand(id, handler))
                .All(result => result);

            bool allQueriesRegistered = queryIds
                .Zip(queryHandlers, (id, handler) => router.RegisterCommand(id, handler))
                .All(result => result);

            // Assert - All registrations should succeed
            bool allRegistrationsSucceeded = allFireAndForgetRegistered && allQueriesRegistered;

            // Verify all commands are registered
            bool allCommandsRegistered = fireAndForgetIds.All(router.IsCommandRegistered) &&
                                       queryIds.All(router.IsCommandRegistered);

            // Get registered commands and verify count
            var registeredCommands = router.GetRegisteredCommands().ToArray();
            bool correctCommandCount = registeredCommands.Length == fireAndForgetIds.Length + queryIds.Length;

            return allRegistrationsSucceeded && allCommandsRegistered && correctCommandCount;
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

    private class TestQueryHandler : IRpcCommandHandler
    {
        public int ExecuteCallCount { get; private set; }
        public RpcParameters? LastParameters { get; private set; }
        public bool IsFireAndForget => false;
        public TimeSpan Timeout => TimeSpan.FromSeconds(5);
        public string Description => "Test query handler";

        public Task<object?> ExecuteAsync(RpcParameters parameters)
        {
            ExecuteCallCount++;
            LastParameters = parameters;
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