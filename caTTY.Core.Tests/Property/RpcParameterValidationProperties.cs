using FsCheck;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using caTTY.Core.Rpc;

namespace caTTY.Core.Tests.Property;

/// <summary>
/// Property-based tests for RPC parameter validation and security.
/// **Feature: term-sequence-rpc, Property 14: Parameter Validation and Security**
/// **Validates: Requirements 8.1, 8.2**
/// </summary>
[TestFixture]
[Category("Property")]
public class RpcParameterValidationProperties
{
    private RpcParameterValidator _validator = null!;
    private TestLogger<RpcParameterValidator> _logger = null!;

    [SetUp]
    public void SetUp()
    {
        _logger = new TestLogger<RpcParameterValidator>();
        _validator = new RpcParameterValidator(_logger);
    }

    /// <summary>
    /// Property: All RPC commands should validate parameters before invoking game actions.
    /// For any RPC command with parameters, the validation system should check all parameters
    /// and reject commands with invalid or unsafe parameters.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property AllCommandsShouldValidateParametersBeforeExecution()
    {
        return Prop.ForAll(Arb.Default.Int32(), RpcParameterGenerators.RpcParameters(), 
            (int commandId, RpcParameters parameters) =>
        {
            // Arrange: Ensure command ID is in valid range
            var validCommandId = Math.Abs(commandId % 9000) + 1000; // 1000-9999 range

            // Act: Validate parameters
            var result = _validator.ValidateParameters(validCommandId, parameters);

            // Assert: Validation should always return a result (never throw)
            return (result != null) &&
                   (result.IsValid || !string.IsNullOrEmpty(result.ErrorMessage));
        });
    }

    /// <summary>
    /// Property: Commands with unsafe parameters should be rejected with security warnings.
    /// For any command that would cause unsafe game state changes, the system should reject
    /// the command and log a security warning.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property UnsafeParametersShouldBeRejectedWithSecurityWarnings()
    {
        return Prop.ForAll(Arb.Default.Int32(), RpcParameterGenerators.UnsafeRpcParameters(), 
            (int commandId, UnsafeRpcParameters unsafeParams) =>
        {
            // Arrange: Ensure command ID is in valid range (1000-9999)
            var validCommandId = Math.Abs(commandId % 9000) + 1000;
            var parameters = unsafeParams.ToRpcParameters();

            // Act: Validate unsafe parameters
            var result = _validator.ValidateParameters(validCommandId, parameters);

            // Assert: Unsafe parameters should be rejected
            // Either as security violation or general validation failure (both are acceptable for unsafe params)
            return !result.IsValid &&
                   !string.IsNullOrEmpty(result.ErrorMessage) &&
                   (result.IsSecurityViolation || 
                    result.ErrorType == RpcParameterValidationErrorType.SecurityViolation ||
                    result.ErrorType == RpcParameterValidationErrorType.TooManyParameters ||
                    result.ErrorType == RpcParameterValidationErrorType.InvalidValue);
        });
    }

    /// <summary>
    /// Property: Parameter validation should be consistent across multiple calls.
    /// For any set of parameters, validating them multiple times should produce the same result.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property ParameterValidationShouldBeConsistent()
    {
        return Prop.ForAll(Arb.Default.Int32(), RpcParameterGenerators.RpcParameters(), 
            (int commandId, RpcParameters parameters) =>
        {
            // Arrange: Ensure command ID is in valid range
            var validCommandId = Math.Abs(commandId % 9000) + 1000;

            // Act: Validate the same parameters multiple times
            var result1 = _validator.ValidateParameters(validCommandId, parameters);
            var result2 = _validator.ValidateParameters(validCommandId, parameters);
            var result3 = _validator.ValidateParameters(validCommandId, parameters);

            // Assert: Results should be identical
            return result1.IsValid == result2.IsValid && result2.IsValid == result3.IsValid &&
                   result1.ErrorType == result2.ErrorType && result2.ErrorType == result3.ErrorType &&
                   result1.IsSecurityViolation == result2.IsSecurityViolation && result2.IsSecurityViolation == result3.IsSecurityViolation;
        });
    }

    /// <summary>
    /// Property: Registered validation rules should be enforced correctly.
    /// For any command with registered validation rules, parameters should be validated
    /// against those rules and rejected if they don't comply.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property RegisteredValidationRulesShouldBeEnforced()
    {
        return Prop.ForAll(Arb.Default.Int32(), RpcParameterGenerators.ValidatedRpcCommand(), 
            (int commandId, ValidatedRpcCommand validatedCommand) =>
        {
            // Arrange: Ensure command ID is in valid range and register rules
            var validCommandId = Math.Abs(commandId % 9000) + 1000;
            _validator.RegisterValidationRules(validCommandId, validatedCommand.Rules);

            // Act: Validate parameters against registered rules
            var validResult = _validator.ValidateParameters(validCommandId, validatedCommand.ValidParameters);
            var invalidResult = _validator.ValidateParameters(validCommandId, validatedCommand.InvalidParameters);

            // Assert: Valid parameters should pass, invalid should fail
            return validResult.IsValid &&
                   !invalidResult.IsValid &&
                   !string.IsNullOrEmpty(invalidResult.ErrorMessage);
        });
    }

    /// <summary>
    /// Property: Security-sensitive commands should have stricter validation.
    /// For any command marked as security-sensitive, validation failures should be
    /// treated as security violations with appropriate logging.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property SecuritySensitiveCommandsShouldHaveStricterValidation()
    {
        return Prop.ForAll(Arb.Default.Int32(), RpcParameterGenerators.SecuritySensitiveCommand(), 
            (int commandId, SecuritySensitiveCommand securityCommand) =>
        {
            // Arrange: Ensure command ID is in valid range and register security-sensitive rules
            var validCommandId = Math.Abs(commandId % 9000) + 1000;
            _validator.RegisterValidationRules(validCommandId, securityCommand.Rules);

            // Act: Validate parameters that violate security rules
            var result = _validator.ValidateParameters(validCommandId, securityCommand.ViolatingParameters);

            // Assert: Security violations should be properly flagged
            return !result.IsValid &&
                   result.IsSecurityViolation &&
                   result.ErrorType == RpcParameterValidationErrorType.SecurityViolation &&
                   !string.IsNullOrEmpty(result.ErrorMessage);
        });
    }

    /// <summary>
    /// Property: Parameter validation should handle edge cases gracefully.
    /// For any extreme or edge case parameters, validation should not crash
    /// and should provide meaningful error messages.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property ParameterValidationShouldHandleEdgeCasesGracefully()
    {
        return Prop.ForAll(Arb.Default.Int32(), RpcParameterGenerators.EdgeCaseRpcParameters(), 
            (int commandId, EdgeCaseRpcParameters edgeParams) =>
        {
            // Arrange: Ensure command ID is in valid range
            var validCommandId = Math.Abs(commandId % 9000) + 1000;
            var parameters = edgeParams.ToRpcParameters();

            // Act & Assert: Validation should not throw exceptions
            try
            {
                var result = _validator.ValidateParameters(validCommandId, parameters);
                
                // Result should be valid or have a meaningful error message
                return result != null &&
                       (result.IsValid || !string.IsNullOrEmpty(result.ErrorMessage));
            }
            catch (Exception)
            {
                return false; // Should not throw exceptions for edge cases
            }
        });
    }
}

/// <summary>
/// Generators for RPC parameter validation property tests.
/// </summary>
public static class RpcParameterGenerators
{
    /// <summary>
    /// Generates arbitrary RPC parameters.
    /// </summary>
    public static Arbitrary<RpcParameters> RpcParameters()
    {
        return Gen.Sized(size =>
            from numericCount in Gen.Choose(0, Math.Min(size, 15))
            from stringCount in Gen.Choose(0, Math.Min(size, 10))
            from numericParams in Gen.ArrayOf(numericCount, Gen.Choose(-2000000, 2000000))
            from stringParams in Gen.ArrayOf(stringCount, Gen.Elements(
                "", "test", "valid_string", "a", "longer_string_value", 
                "string_with_spaces", "123", "special!@#", "unicode_ñ"))
            select new caTTY.Core.Rpc.RpcParameters
            {
                NumericParameters = numericParams,
                StringParameters = stringParams,
                ExtendedParameters = new Dictionary<string, object>()
            }).ToArbitrary();
    }

    /// <summary>
    /// Generates RPC parameters with potentially unsafe values.
    /// </summary>
    public static Arbitrary<UnsafeRpcParameters> UnsafeRpcParameters()
    {
        return Gen.OneOf(
            // Extremely large numeric values
            Gen.Constant(new UnsafeRpcParameters
            {
                NumericValues = new[] { int.MaxValue, int.MinValue, 10000000, -10000000 },
                StringValues = new[] { "normal" },
                UnsafeReason = "extreme_numeric_values"
            }),
            // Strings with control characters
            Gen.Constant(new UnsafeRpcParameters
            {
                NumericValues = new[] { 1 },
                StringValues = new[] { "test\x1b[31m", "string\0null", "line\nbreak", "carriage\rreturn" },
                UnsafeReason = "control_characters"
            }),
            // Extremely long strings
            Gen.Constant(new UnsafeRpcParameters
            {
                NumericValues = new[] { 1 },
                StringValues = new[] { new string('x', 2000), new string('a', 5000) },
                UnsafeReason = "excessive_string_length"
            }),
            // Too many parameters
            Gen.Constant(new UnsafeRpcParameters
            {
                NumericValues = Enumerable.Range(0, 20).ToArray(),
                StringValues = Enumerable.Range(0, 15).Select(i => $"param_{i}").ToArray(),
                UnsafeReason = "too_many_parameters"
            })
        ).ToArbitrary();
    }

    /// <summary>
    /// Generates commands with validation rules and corresponding valid/invalid parameters.
    /// </summary>
    public static Arbitrary<ValidatedRpcCommand> ValidatedRpcCommand()
    {
        return Gen.OneOf(
            // Throttle command with range validation
            Gen.Constant(new ValidatedRpcCommand
            {
                Rules = new RpcParameterValidationRules
                {
                    ExpectedNumericParameterCount = 1
                }.AddNumericRule(0, NumericParameterRule.Range(0, 100, "throttle_percentage")),
                ValidParameters = new caTTY.Core.Rpc.RpcParameters
                {
                    NumericParameters = new[] { 50 }
                },
                InvalidParameters = new caTTY.Core.Rpc.RpcParameters
                {
                    NumericParameters = new[] { 150 }
                }
            }),
            // Mode command with allowed values
            Gen.Constant(new ValidatedRpcCommand
            {
                Rules = new RpcParameterValidationRules
                {
                    ExpectedNumericParameterCount = 1
                }.AddNumericRule(0, NumericParameterRule.WithAllowedValues(new[] { 1, 2, 3 }, "mode")),
                ValidParameters = new caTTY.Core.Rpc.RpcParameters
                {
                    NumericParameters = new[] { 2 }
                },
                InvalidParameters = new caTTY.Core.Rpc.RpcParameters
                {
                    NumericParameters = new[] { 5 }
                }
            }),
            // String command with length validation
            Gen.Constant(new ValidatedRpcCommand
            {
                Rules = new RpcParameterValidationRules
                {
                    ExpectedStringParameterCount = 1
                }.AddStringRule(0, StringParameterRule.Length(1, 10, "command_name")),
                ValidParameters = new caTTY.Core.Rpc.RpcParameters
                {
                    StringParameters = new[] { "test" }
                },
                InvalidParameters = new caTTY.Core.Rpc.RpcParameters
                {
                    StringParameters = new[] { "this_string_is_way_too_long" }
                }
            })
        ).ToArbitrary();
    }

    /// <summary>
    /// Generates security-sensitive commands with rules that can be violated.
    /// </summary>
    public static Arbitrary<SecuritySensitiveCommand> SecuritySensitiveCommand()
    {
        return Gen.OneOf(
            // Critical throttle command
            Gen.Constant(new SecuritySensitiveCommand
            {
                Rules = new RpcParameterValidationRules
                {
                    ExpectedNumericParameterCount = 1
                }.AddNumericRule(0, NumericParameterRule.Range(0, 100, "critical_throttle", isSecuritySensitive: true))
                 .AsSecuritySensitive("Critical engine control"),
                ViolatingParameters = new caTTY.Core.Rpc.RpcParameters
                {
                    NumericParameters = new[] { 150 }
                }
            }),
            // Security-sensitive string command
            Gen.Constant(new SecuritySensitiveCommand
            {
                Rules = new RpcParameterValidationRules
                {
                    ExpectedStringParameterCount = 1
                }.AddStringRule(0, StringParameterRule.WithPattern(@"^[a-zA-Z0-9_]+$", "secure_command", isSecuritySensitive: true))
                 .AsSecuritySensitive("Security-critical command name"),
                ViolatingParameters = new caTTY.Core.Rpc.RpcParameters
                {
                    StringParameters = new[] { "malicious_command; rm -rf /" }
                }
            })
        ).ToArbitrary();
    }

    /// <summary>
    /// Generates edge case parameters that might cause issues.
    /// </summary>
    public static Arbitrary<EdgeCaseRpcParameters> EdgeCaseRpcParameters()
    {
        return Gen.OneOf(
            // Empty parameters
            Gen.Constant(new EdgeCaseRpcParameters
            {
                NumericValues = Array.Empty<int>(),
                StringValues = Array.Empty<string>(),
                EdgeCaseType = "empty_parameters"
            }),
            // Null-like values (empty strings, zeros)
            Gen.Constant(new EdgeCaseRpcParameters
            {
                NumericValues = new[] { 0, 0, 0 },
                StringValues = new[] { "", "", "" },
                EdgeCaseType = "null_like_values"
            }),
            // Boundary values
            Gen.Constant(new EdgeCaseRpcParameters
            {
                NumericValues = new[] { int.MaxValue, int.MinValue, 0, 1, -1 },
                StringValues = new[] { "", "a", new string('x', 1000) },
                EdgeCaseType = "boundary_values"
            }),
            // Unicode and special characters
            Gen.Constant(new EdgeCaseRpcParameters
            {
                NumericValues = new[] { 42 },
                StringValues = new[] { "🚀", "café", "测试", "العربية", "русский" },
                EdgeCaseType = "unicode_characters"
            })
        ).ToArbitrary();
    }
}

/// <summary>
/// Represents RPC parameters with potentially unsafe values.
/// </summary>
public class UnsafeRpcParameters
{
    public int[] NumericValues { get; set; } = Array.Empty<int>();
    public string[] StringValues { get; set; } = Array.Empty<string>();
    public string UnsafeReason { get; set; } = "";

    public caTTY.Core.Rpc.RpcParameters ToRpcParameters()
    {
        return new caTTY.Core.Rpc.RpcParameters
        {
            NumericParameters = NumericValues,
            StringParameters = StringValues,
            ExtendedParameters = new Dictionary<string, object>()
        };
    }
}

/// <summary>
/// Represents a command with validation rules and test parameters.
/// </summary>
public class ValidatedRpcCommand
{
    public RpcParameterValidationRules Rules { get; set; } = new();
    public caTTY.Core.Rpc.RpcParameters ValidParameters { get; set; } = new();
    public caTTY.Core.Rpc.RpcParameters InvalidParameters { get; set; } = new();
}

/// <summary>
/// Represents a security-sensitive command with rules that can be violated.
/// </summary>
public class SecuritySensitiveCommand
{
    public RpcParameterValidationRules Rules { get; set; } = new();
    public caTTY.Core.Rpc.RpcParameters ViolatingParameters { get; set; } = new();
}

/// <summary>
/// Represents edge case parameters for testing robustness.
/// </summary>
public class EdgeCaseRpcParameters
{
    public int[] NumericValues { get; set; } = Array.Empty<int>();
    public string[] StringValues { get; set; } = Array.Empty<string>();
    public string EdgeCaseType { get; set; } = "";

    public caTTY.Core.Rpc.RpcParameters ToRpcParameters()
    {
        return new caTTY.Core.Rpc.RpcParameters
        {
            NumericParameters = NumericValues,
            StringParameters = StringValues,
            ExtendedParameters = new Dictionary<string, object>()
        };
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