using Microsoft.Extensions.Logging;
using NUnit.Framework;
using caTTY.Core.Rpc;

namespace caTTY.Core.Tests.Unit.Rpc;

[TestFixture]
[Category("Unit")]
public class RpcParameterValidationIntegrationTests
{
    private RpcCommandRouter _router = null!;
    private RpcParameterValidator _validator = null!;
    private TestLogger<RpcCommandRouter> _routerLogger = null!;
    private TestLogger<RpcParameterValidator> _validatorLogger = null!;

    [SetUp]
    public void SetUp()
    {
        _routerLogger = new TestLogger<RpcCommandRouter>();
        _validatorLogger = new TestLogger<RpcParameterValidator>();
        _validator = new RpcParameterValidator(_validatorLogger);
        _router = new RpcCommandRouter(_routerLogger, _validator);
    }

    [Test]
    public async Task RouteCommandAsync_WithValidationEnabled_ValidatesParameters()
    {
        // Arrange
        var handler = new TestFireAndForgetHandler();
        const int commandId = 1001;
        
        // Register command and validation rules
        _router.RegisterCommand(commandId, handler);
        _validator.RegisterValidationRules(commandId, new RpcParameterValidationRules
        {
            ExpectedNumericParameterCount = 1
        }.AddNumericRule(0, NumericParameterRule.Range(0, 100, "throttle")));

        var validMessage = new RpcMessage
        {
            CommandId = commandId,
            Version = 1,
            CommandType = RpcCommandType.FireAndForget,
            Parameters = new RpcParameters
            {
                NumericParameters = new[] { 50 }
            },
            Raw = "ESC[>1001;1;F"
        };

        var invalidMessage = new RpcMessage
        {
            CommandId = commandId,
            Version = 1,
            CommandType = RpcCommandType.FireAndForget,
            Parameters = new RpcParameters
            {
                NumericParameters = new[] { 150 } // Out of range
            },
            Raw = "ESC[>1001;1;F"
        };

        // Act
        var validResult = await _router.RouteCommandAsync(validMessage);
        var invalidResult = await _router.RouteCommandAsync(invalidMessage);

        // Assert
        Assert.That(validResult.Success, Is.True);
        Assert.That(invalidResult.Success, Is.False);
        Assert.That(invalidResult.ErrorMessage, Contains.Substring("Parameter validation failed"));
        Assert.That(invalidResult.ErrorMessage, Contains.Substring("above maximum 100"));
    }

    [Test]
    public async Task RouteCommandAsync_WithSecurityViolation_LogsSecurityWarning()
    {
        // Arrange
        var handler = new TestFireAndForgetHandler();
        const int commandId = 1001;
        
        // Register command with security-sensitive validation
        _router.RegisterCommand(commandId, handler);
        _validator.RegisterValidationRules(commandId, new RpcParameterValidationRules
        {
            ExpectedNumericParameterCount = 1
        }.AddNumericRule(0, NumericParameterRule.Range(0, 100, "throttle", isSecuritySensitive: true))
         .AsSecuritySensitive("Throttle control is safety-critical"));

        var securityViolationMessage = new RpcMessage
        {
            CommandId = commandId,
            Version = 1,
            CommandType = RpcCommandType.FireAndForget,
            Parameters = new RpcParameters
            {
                NumericParameters = new[] { 200 } // Security violation
            },
            Raw = "ESC[>1001;1;F"
        };

        // Act
        var result = await _router.RouteCommandAsync(securityViolationMessage);

        // Assert
        Assert.That(result.Success, Is.False);
        Assert.That(_routerLogger.LoggedMessages.Any(m => m.Contains("RPC security violation")), Is.True);
    }

    [Test]
    public async Task RouteCommandAsync_WithoutValidator_SkipsValidation()
    {
        // Arrange - Create router without validator
        var routerWithoutValidator = new RpcCommandRouter(_routerLogger);
        var handler = new TestFireAndForgetHandler();
        const int commandId = 1001;
        
        routerWithoutValidator.RegisterCommand(commandId, handler);

        var message = new RpcMessage
        {
            CommandId = commandId,
            Version = 1,
            CommandType = RpcCommandType.FireAndForget,
            Parameters = new RpcParameters
            {
                NumericParameters = new[] { 999999 } // Would normally be invalid
            },
            Raw = "ESC[>1001;1;F"
        };

        // Act
        var result = await routerWithoutValidator.RouteCommandAsync(message);

        // Assert
        Assert.That(result.Success, Is.True); // Should succeed without validation
    }

    [Test]
    public async Task RouteCommandAsync_WithCustomValidator_ExecutesCustomLogic()
    {
        // Arrange
        var handler = new TestQueryHandler();
        const int commandId = 2001;
        
        _router.RegisterCommand(commandId, handler);
        _validator.RegisterValidationRules(commandId, new RpcParameterValidationRules
        {
            ExpectedNumericParameterCount = 2
        }.AddCustomValidator(parameters =>
        {
            // Custom rule: sum of parameters must be less than 100
            var sum = parameters.NumericParameters.Sum();
            if (sum >= 100)
            {
                return RpcParameterValidationResult.SecurityViolation(
                    $"Sum of parameters ({sum}) exceeds safety limit of 100",
                    "parameter_sum", sum);
            }
            return RpcParameterValidationResult.Success();
        }));

        var safeMessage = new RpcMessage
        {
            CommandId = commandId,
            Version = 1,
            CommandType = RpcCommandType.Query,
            Parameters = new RpcParameters
            {
                NumericParameters = new[] { 30, 40 } // Sum = 70, safe
            },
            Raw = "ESC[>2001;1;Q"
        };

        var unsafeMessage = new RpcMessage
        {
            CommandId = commandId,
            Version = 1,
            CommandType = RpcCommandType.Query,
            Parameters = new RpcParameters
            {
                NumericParameters = new[] { 60, 50 } // Sum = 110, unsafe
            },
            Raw = "ESC[>2001;1;Q"
        };

        // Act
        var safeResult = await _router.RouteCommandAsync(safeMessage);
        var unsafeResult = await _router.RouteCommandAsync(unsafeMessage);

        // Assert
        Assert.That(safeResult.Success, Is.True);
        Assert.That(unsafeResult.Success, Is.False);
        Assert.That(unsafeResult.ErrorMessage, Contains.Substring("exceeds safety limit"));
        Assert.That(_routerLogger.LoggedMessages.Any(m => m.Contains("RPC security violation")), Is.True);
    }
}

/// <summary>
/// Test fire-and-forget command handler.
/// </summary>
internal class TestFireAndForgetHandler : FireAndForgetCommandHandler
{
    public TestFireAndForgetHandler() : base("Test Fire-and-Forget Command")
    {
    }

    protected override void ExecuteAction(RpcParameters parameters)
    {
        // Test implementation - do nothing
    }
}

/// <summary>
/// Test query command handler.
/// </summary>
internal class TestQueryHandler : QueryCommandHandler
{
    public TestQueryHandler() : base("Test Query Command")
    {
    }

    protected override object? ExecuteQuery(RpcParameters parameters)
    {
        return CreateValueResponse("test_result");
    }
}