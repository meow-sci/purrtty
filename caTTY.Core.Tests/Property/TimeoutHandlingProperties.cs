using caTTY.Core.Rpc;
using FsCheck;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using System.Text;

namespace caTTY.Core.Tests.Property;

/// <summary>
/// Property-based tests for RPC timeout handling.
/// These tests verify universal properties that should hold for RPC command timeouts.
/// </summary>
[TestFixture]
[Category("Property")]
public class TimeoutHandlingProperties
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
    /// Generator for very short timeout values that will cause timeouts.
    /// </summary>
    public static Arbitrary<TimeSpan> ShortTimeoutArb =>
        Arb.From(Gen.Choose(1, 10).Select(ms => TimeSpan.FromMilliseconds(ms)));

    /// <summary>
    /// **Feature: term-sequence-rpc, Property 8: Timeout Handling**
    /// **Validates: Requirements 3.4**
    /// Property: For any query command that exceeds the timeout period, the RPC system should 
    /// send an error response using ESC [ > 9999 ; 1 ; E format and log the timeout.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property TimeoutHandling()
    {
        return Prop.ForAll(ValidQueryCommandIdArb, ValidRpcParametersArb, ShortTimeoutArb,
            (int commandId, RpcParameters parameters, TimeSpan timeout) =>
        {
            // Arrange - Create router with timeout handler and response generator
            var logger = new TestLogger();
            var router = new RpcCommandRouter(logger);
            var responseGenerator = new RpcResponseGenerator();
            var handler = new TimeoutTestHandler(timeout);

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

            // Act - Execute the query command that will timeout
            var routingTask = router.RouteCommandAsync(message);
            var result = routingTask.GetAwaiter().GetResult();

            // Generate the expected timeout response
            var expectedTimeoutResponse = responseGenerator.GenerateTimeout(commandId);
            var expectedTimeoutString = Encoding.ASCII.GetString(expectedTimeoutResponse);

            // Assert - Verify timeout handling behavior
            
            // 1. Registration should succeed for valid query command ID
            bool validRegistration = registrationSuccess;

            // 2. Command should fail due to timeout (Requirements 3.4)
            bool executionFailed = !result.Success;

            // 3. Result should be marked as timeout
            bool isTimeoutResult = result.IsTimeout;

            // 4. Command ID should be preserved in timeout result
            bool correctCommandId = result.CommandId == commandId;

            // 5. Error message should indicate timeout
            bool hasTimeoutError = !string.IsNullOrEmpty(result.ErrorMessage) && 
                                  (result.ErrorMessage.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
                                   result.ErrorMessage.Contains("timed out", StringComparison.OrdinalIgnoreCase));

            // 6. No response data should be returned for timed out queries
            bool noResponseData = result.Data == null;

            // 7. Execution time should be recorded and should be close to timeout
            bool hasExecutionTime = result.ExecutionTime >= TimeSpan.Zero;

            // 8. Handler should have been called (started execution)
            bool handlerCalled = handler.ExecuteCallCount > 0;

            // 9. Expected timeout response should use ESC [ > 9999 ; 1 ; E format (Requirements 3.4)
            bool correctTimeoutFormat = expectedTimeoutString.StartsWith("\x1b[>9999;1;") && 
                                       expectedTimeoutString.Contains(commandId.ToString()) &&
                                       expectedTimeoutString.EndsWith("E");

            // 10. Logger should have recorded timeout warning
            bool timeoutLogged = logger.LoggedMessages.Any(msg => 
                msg.Contains("timeout", StringComparison.OrdinalIgnoreCase) && 
                msg.Contains(commandId.ToString()));

            return validRegistration && executionFailed && isTimeoutResult && 
                   correctCommandId && hasTimeoutError && noResponseData && 
                   hasExecutionTime && handlerCalled && correctTimeoutFormat && timeoutLogged;
        });
    }

    /// <summary>
    /// **Feature: term-sequence-rpc, Property 8b: Timeout Response Format Validation**
    /// **Validates: Requirements 3.4**
    /// Property: For any command ID that times out, the generated timeout response should 
    /// follow the exact ESC [ > 9999 ; 1 ; E format with the original command ID included.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property TimeoutResponseFormat()
    {
        return Prop.ForAll(ValidQueryCommandIdArb,
            (int commandId) =>
        {
            // Arrange
            var responseGenerator = new RpcResponseGenerator();

            // Act - Generate timeout response
            var timeoutResponse = responseGenerator.GenerateTimeout(commandId);
            var responseString = Encoding.ASCII.GetString(timeoutResponse);

            // Assert - Verify timeout response format (Requirements 3.4)
            
            // 1. Response should start with ESC [ > prefix
            bool hasCorrectPrefix = responseString.StartsWith("\x1b[>");

            // 2. Response should use error command ID 9999
            bool hasErrorCommandId = responseString.Contains("9999");

            // 3. Response should include protocol version 1
            bool hasProtocolVersion = responseString.Contains(";1;");

            // 4. Response should include original command ID as parameter
            bool hasOriginalCommandId = responseString.Contains(commandId.ToString());

            // 5. Response should end with error final character 'E'
            bool hasErrorFinalChar = responseString.EndsWith("E");

            // 6. Response should contain TIMEOUT error message
            bool hasTimeoutMessage = responseString.Contains("TIMEOUT");

            // 7. Response should be properly formatted sequence
            bool isProperSequence = responseString.StartsWith("\x1b[>9999;1;") && 
                                   responseString.Contains(commandId.ToString()) &&
                                   responseString.EndsWith("E");

            // 8. Response should not be empty
            bool isNotEmpty = timeoutResponse.Length > 0;

            // 9. Response should contain only ASCII characters
            bool isAsciiOnly = timeoutResponse.All(b => b >= 0x20 && b <= 0x7E || b == 0x1B || b == 0x5B || b == 0x3E || b == 0x3B);

            return hasCorrectPrefix && hasErrorCommandId && hasProtocolVersion && 
                   hasOriginalCommandId && hasErrorFinalChar && hasTimeoutMessage && 
                   isProperSequence && isNotEmpty && isAsciiOnly;
        });
    }

    /// <summary>
    /// **Feature: term-sequence-rpc, Property 8c: Timeout Logging Verification**
    /// **Validates: Requirements 3.4**
    /// Property: For any query command that times out, the system should log appropriate 
    /// timeout messages with relevant context information.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property TimeoutLogging()
    {
        return Prop.ForAll(ValidQueryCommandIdArb, ValidRpcParametersArb, ShortTimeoutArb,
            (int commandId, RpcParameters parameters, TimeSpan timeout) =>
        {
            // Arrange
            var logger = new TestLogger();
            var router = new RpcCommandRouter(logger);
            var handler = new TimeoutTestHandler(timeout);

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

            // Assert - Verify timeout logging (Requirements 3.4)
            
            // 1. Command should have timed out
            bool didTimeout = result.IsTimeout;

            // 2. Logger should have recorded timeout message
            bool hasTimeoutLog = logger.LoggedMessages.Any(msg => 
                msg.Contains("timeout", StringComparison.OrdinalIgnoreCase));

            // 3. Log should include command ID
            bool logIncludesCommandId = logger.LoggedMessages.Any(msg => 
                msg.Contains(commandId.ToString()));

            // 4. Log should include timeout duration
            bool logIncludesTimeout = logger.LoggedMessages.Any(msg => 
                msg.Contains(timeout.TotalMilliseconds.ToString(), StringComparison.OrdinalIgnoreCase) ||
                msg.Contains("ms", StringComparison.OrdinalIgnoreCase));

            // 5. Log should include handler description
            bool logIncludesHandler = logger.LoggedMessages.Any(msg => 
                msg.Contains(handler.Description, StringComparison.OrdinalIgnoreCase));

            // 6. Log should include raw sequence for debugging
            bool logIncludesRawSequence = logger.LoggedMessages.Any(msg => 
                msg.Contains(message.Raw));

            // 7. Log level should be warning or error (not debug/info)
            bool hasWarningOrError = logger.LoggedLevels.Any(level => 
                level == LogLevel.Warning || level == LogLevel.Error);

            return didTimeout && hasTimeoutLog && logIncludesCommandId && 
                   logIncludesTimeout && logIncludesHandler && logIncludesRawSequence && 
                   hasWarningOrError;
        });
    }

    // Test helper classes
    private class TimeoutTestHandler : IRpcCommandHandler
    {
        public TimeoutTestHandler(TimeSpan timeout)
        {
            Timeout = timeout;
        }

        public int ExecuteCallCount { get; private set; }
        public bool IsFireAndForget => false;
        public TimeSpan Timeout { get; }
        public string Description => "Test timeout handler";

        public async Task<object?> ExecuteAsync(RpcParameters parameters)
        {
            ExecuteCallCount++;
            // Delay longer than the timeout to trigger timeout handling
            await Task.Delay(TimeSpan.FromMilliseconds(100));
            return "should-not-return";
        }
    }

    private class TestLogger : ILogger<RpcCommandRouter>
    {
        public List<string> LoggedMessages { get; } = new();
        public List<LogLevel> LoggedLevels { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            LoggedMessages.Add(message);
            LoggedLevels.Add(logLevel);
        }
    }
}