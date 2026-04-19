using Microsoft.Extensions.Logging;
using NUnit.Framework;
using caTTY.Core.Rpc;

namespace caTTY.Core.Tests.Unit.Rpc;

[TestFixture]
[Category("Unit")]
public class RpcParameterValidatorTests
{
    private RpcParameterValidator _validator = null!;
    private TestLogger<RpcParameterValidator> _logger = null!;

    [SetUp]
    public void SetUp()
    {
        _logger = new TestLogger<RpcParameterValidator>();
        _validator = new RpcParameterValidator(_logger);
    }

    [Test]
    public void ValidateParameters_WithNullParameters_ReturnsFailure()
    {
        // Act
        var result = _validator.ValidateParameters(1001, null!);

        // Assert
        Assert.That(result.IsValid, Is.False);
        Assert.That(result.ErrorType, Is.EqualTo(RpcParameterValidationErrorType.MissingParameter));
        Assert.That(result.ErrorMessage, Contains.Substring("Parameters cannot be null"));
    }

    [Test]
    public void ValidateParameters_WithNoRulesRegistered_PerformsBasicValidation()
    {
        // Arrange
        var parameters = new RpcParameters
        {
            NumericParameters = new[] { 1, 2, 3 },
            StringParameters = new[] { "test" }
        };

        // Act
        var result = _validator.ValidateParameters(1001, parameters);

        // Assert
        Assert.That(result.IsValid, Is.True);
    }

    [Test]
    public void ValidateParameters_WithTooManyNumericParameters_ReturnsFailure()
    {
        // Arrange
        var parameters = new RpcParameters
        {
            NumericParameters = new int[15] // More than the basic limit of 10
        };

        // Act
        var result = _validator.ValidateParameters(1001, parameters);

        // Assert
        Assert.That(result.IsValid, Is.False);
        Assert.That(result.ErrorType, Is.EqualTo(RpcParameterValidationErrorType.TooManyParameters));
        Assert.That(result.ErrorMessage, Contains.Substring("too many numeric parameters"));
    }

    [Test]
    public void ValidateParameters_WithDangerousNumericValue_ReturnsSecurityViolation()
    {
        // Arrange
        var parameters = new RpcParameters
        {
            NumericParameters = new[] { 2000000 } // Above the basic safety limit
        };

        // Act
        var result = _validator.ValidateParameters(1001, parameters);

        // Assert
        Assert.That(result.IsValid, Is.False);
        Assert.That(result.IsSecurityViolation, Is.True);
        Assert.That(result.ErrorMessage, Contains.Substring("potentially unsafe numeric parameter"));
    }

    [Test]
    public void ValidateParameters_WithDangerousStringValue_ReturnsSecurityViolation()
    {
        // Arrange
        var parameters = new RpcParameters
        {
            StringParameters = new[] { "test\x1b[31m" } // Contains escape sequence
        };

        // Act
        var result = _validator.ValidateParameters(1001, parameters);

        // Assert
        Assert.That(result.IsValid, Is.False);
        Assert.That(result.IsSecurityViolation, Is.True);
        Assert.That(result.ErrorMessage, Contains.Substring("dangerous control characters"));
    }

    [Test]
    public void RegisterValidationRules_WithValidRules_RegistersSuccessfully()
    {
        // Arrange
        var rules = new RpcParameterValidationRules
        {
            ExpectedNumericParameterCount = 1,
            Description = "Test command"
        };

        // Act
        _validator.RegisterValidationRules(1001, rules);

        // Assert
        Assert.That(_validator.HasValidationRules(1001), Is.True);
    }

    [Test]
    public void ValidateParameters_WithRegisteredRules_ValidatesAgainstRules()
    {
        // Arrange
        var rules = new RpcParameterValidationRules
        {
            ExpectedNumericParameterCount = 2
        }.AddNumericRule(0, NumericParameterRule.Range(0, 100, "throttle"))
         .AddNumericRule(1, NumericParameterRule.Range(0, 359, "heading"));

        _validator.RegisterValidationRules(1001, rules);

        var validParameters = new RpcParameters
        {
            NumericParameters = new[] { 50, 180 }
        };

        var invalidParameters = new RpcParameters
        {
            NumericParameters = new[] { 150, 180 } // First parameter out of range
        };

        // Act
        var validResult = _validator.ValidateParameters(1001, validParameters);
        var invalidResult = _validator.ValidateParameters(1001, invalidParameters);

        // Assert
        Assert.That(validResult.IsValid, Is.True);
        Assert.That(invalidResult.IsValid, Is.False);
        Assert.That(invalidResult.ErrorMessage, Contains.Substring("above maximum 100"));
    }

    [Test]
    public void ValidateParameters_WithSecuritySensitiveRule_ReturnsSecurityViolation()
    {
        // Arrange
        var rules = new RpcParameterValidationRules
        {
            ExpectedNumericParameterCount = 1
        }.AddNumericRule(0, NumericParameterRule.Range(0, 100, "throttle", isSecuritySensitive: true))
         .AsSecuritySensitive("Throttle control is safety-critical");

        _validator.RegisterValidationRules(1001, rules);

        var parameters = new RpcParameters
        {
            NumericParameters = new[] { 150 } // Out of range
        };

        // Act
        var result = _validator.ValidateParameters(1001, parameters);

        // Assert
        Assert.That(result.IsValid, Is.False);
        Assert.That(result.IsSecurityViolation, Is.True);
        Assert.That(result.ErrorMessage, Contains.Substring("above maximum 100"));
    }

    [Test]
    public void ValidateParameters_WithCustomValidator_ExecutesCustomLogic()
    {
        // Arrange
        var rules = new RpcParameterValidationRules
        {
            ExpectedNumericParameterCount = 2
        }.AddCustomValidator(parameters =>
        {
            // Custom rule: first parameter must be greater than second
            if (parameters.NumericParameters[0] <= parameters.NumericParameters[1])
            {
                return RpcParameterValidationResult.Failure("First parameter must be greater than second");
            }
            return RpcParameterValidationResult.Success();
        });

        _validator.RegisterValidationRules(1001, rules);

        var validParameters = new RpcParameters
        {
            NumericParameters = new[] { 10, 5 }
        };

        var invalidParameters = new RpcParameters
        {
            NumericParameters = new[] { 5, 10 }
        };

        // Act
        var validResult = _validator.ValidateParameters(1001, validParameters);
        var invalidResult = _validator.ValidateParameters(1001, invalidParameters);

        // Assert
        Assert.That(validResult.IsValid, Is.True);
        Assert.That(invalidResult.IsValid, Is.False);
        Assert.That(invalidResult.ErrorMessage, Contains.Substring("First parameter must be greater than second"));
    }

    [Test]
    public void ValidateParameters_WithStringRules_ValidatesStringParameters()
    {
        // Arrange
        var rules = new RpcParameterValidationRules
        {
            ExpectedStringParameterCount = 1
        }.AddStringRule(0, StringParameterRule.Length(1, 10, "command_name"));

        _validator.RegisterValidationRules(1001, rules);

        var validParameters = new RpcParameters
        {
            StringParameters = new[] { "test" }
        };

        var invalidParameters = new RpcParameters
        {
            StringParameters = new[] { "this_is_too_long_for_the_rule" }
        };

        // Act
        var validResult = _validator.ValidateParameters(1001, validParameters);
        var invalidResult = _validator.ValidateParameters(1001, invalidParameters);

        // Assert
        Assert.That(validResult.IsValid, Is.True);
        Assert.That(invalidResult.IsValid, Is.False);
        Assert.That(invalidResult.ErrorMessage, Contains.Substring("above maximum 10"));
    }

    [Test]
    public void UnregisterValidationRules_WithExistingRules_RemovesRules()
    {
        // Arrange
        var rules = new RpcParameterValidationRules
        {
            ExpectedNumericParameterCount = 1
        };
        _validator.RegisterValidationRules(1001, rules);

        // Act
        _validator.UnregisterValidationRules(1001);

        // Assert
        Assert.That(_validator.HasValidationRules(1001), Is.False);
    }

    [Test]
    public void ValidateParameters_WithAllowedValues_ValidatesCorrectly()
    {
        // Arrange
        var rules = new RpcParameterValidationRules
        {
            ExpectedNumericParameterCount = 1
        }.AddNumericRule(0, NumericParameterRule.WithAllowedValues(new[] { 1, 2, 3 }, "mode"));

        _validator.RegisterValidationRules(1001, rules);

        var validParameters = new RpcParameters
        {
            NumericParameters = new[] { 2 }
        };

        var invalidParameters = new RpcParameters
        {
            NumericParameters = new[] { 5 }
        };

        // Act
        var validResult = _validator.ValidateParameters(1001, validParameters);
        var invalidResult = _validator.ValidateParameters(1001, invalidParameters);

        // Assert
        Assert.That(validResult.IsValid, Is.True);
        Assert.That(invalidResult.IsValid, Is.False);
        Assert.That(invalidResult.ErrorMessage, Contains.Substring("allowed values: [1, 2, 3]"));
    }
}

/// <summary>
/// Test implementation of ILogger for capturing log messages.
/// </summary>
internal class TestLogger<T> : ILogger<T>
{
    public List<string> LoggedMessages { get; } = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var message = formatter(state, exception);
        LoggedMessages.Add($"[{logLevel}] {message}");
    }
}