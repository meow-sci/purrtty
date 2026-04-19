using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace caTTY.Core.Rpc;

/// <summary>
/// Validates RPC command parameters before execution.
/// Provides security validation and parameter safety checks.
/// </summary>
public class RpcParameterValidator : IRpcParameterValidator
{
    private readonly ConcurrentDictionary<int, RpcParameterValidationRules> _validationRules = new();
    private readonly ILogger<RpcParameterValidator> _logger;

    /// <summary>
    /// Initializes a new instance of the RpcParameterValidator.
    /// </summary>
    /// <param name="logger">Logger for security warnings and validation errors</param>
    public RpcParameterValidator(ILogger<RpcParameterValidator> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public RpcParameterValidationResult ValidateParameters(int commandId, RpcParameters parameters)
    {
        if (parameters == null)
        {
            var error = $"Parameters cannot be null for command {commandId}";
            _logger.LogWarning($"RPC parameter validation failed: {error}");
            return RpcParameterValidationResult.Failure(error, RpcParameterValidationErrorType.MissingParameter);
        }

        // If no validation rules are registered, perform basic validation
        if (!_validationRules.TryGetValue(commandId, out var rules))
        {
            _logger.LogDebug($"No validation rules registered for command {commandId}, performing basic validation");
            return PerformBasicValidation(commandId, parameters);
        }

        // Perform comprehensive validation using registered rules
        return PerformRuleBasedValidation(commandId, parameters, rules);
    }

    /// <inheritdoc />
    public void RegisterValidationRules(int commandId, RpcParameterValidationRules validationRules)
    {
        if (validationRules == null)
        {
            throw new ArgumentNullException(nameof(validationRules));
        }

        _validationRules[commandId] = validationRules;
        _logger.LogDebug($"Registered validation rules for command {commandId}: {validationRules.Description ?? "no description"}");
    }

    /// <inheritdoc />
    public void UnregisterValidationRules(int commandId)
    {
        var removed = _validationRules.TryRemove(commandId, out var rules);
        if (removed)
        {
            _logger.LogDebug($"Unregistered validation rules for command {commandId}: {rules?.Description ?? "no description"}");
        }
        else
        {
            _logger.LogWarning($"Attempted to unregister validation rules for command {commandId}, but no rules were registered");
        }
    }

    /// <inheritdoc />
    public bool HasValidationRules(int commandId)
    {
        return _validationRules.ContainsKey(commandId);
    }

    /// <summary>
    /// Performs basic validation when no specific rules are registered.
    /// </summary>
    /// <param name="commandId">The command ID</param>
    /// <param name="parameters">The parameters to validate</param>
    /// <returns>The validation result</returns>
    private RpcParameterValidationResult PerformBasicValidation(int commandId, RpcParameters parameters)
    {
        // Basic validation: check for reasonable parameter counts
        if (parameters.NumericParameters.Length > 10)
        {
            var error = $"Command {commandId} has too many numeric parameters ({parameters.NumericParameters.Length}), maximum is 10";
            _logger.LogWarning($"RPC parameter validation failed: {error}");
            return RpcParameterValidationResult.Failure(error, RpcParameterValidationErrorType.TooManyParameters);
        }

        if (parameters.StringParameters.Length > 5)
        {
            var error = $"Command {commandId} has too many string parameters ({parameters.StringParameters.Length}), maximum is 5";
            _logger.LogWarning($"RPC parameter validation failed: {error}");
            return RpcParameterValidationResult.Failure(error, RpcParameterValidationErrorType.TooManyParameters);
        }

        // Check for potentially dangerous numeric values
        foreach (var value in parameters.NumericParameters)
        {
            // Check for extreme values that could cause overflow or other issues
            if (value < -1000000 || value > 1000000)
            {
                var error = $"Command {commandId} has potentially unsafe numeric parameter: {value}";
                _logger.LogWarning($"RPC security warning: {error}");
                return RpcParameterValidationResult.SecurityViolation(error, "numeric_parameter", value);
            }
        }

        // Check for potentially dangerous string values
        foreach (var value in parameters.StringParameters)
        {
            // Check for excessively long strings
            if (value.Length > 1000)
            {
                var error = $"Command {commandId} has potentially unsafe string parameter length: {value.Length}";
                _logger.LogWarning($"RPC security warning: {error}");
                return RpcParameterValidationResult.SecurityViolation(error, "string_parameter", value.Length);
            }

            // Check for potentially dangerous characters
            if (value.Contains('\0') || value.Contains('\x1b') || value.Contains('\r') || value.Contains('\n'))
            {
                var error = $"Command {commandId} has string parameter with potentially dangerous control characters";
                _logger.LogWarning($"RPC security warning: {error}");
                return RpcParameterValidationResult.SecurityViolation(error, "string_parameter", value);
            }
        }

        return RpcParameterValidationResult.Success();
    }

    /// <summary>
    /// Performs rule-based validation using registered validation rules.
    /// </summary>
    /// <param name="commandId">The command ID</param>
    /// <param name="parameters">The parameters to validate</param>
    /// <param name="rules">The validation rules to apply</param>
    /// <returns>The validation result</returns>
    private RpcParameterValidationResult PerformRuleBasedValidation(int commandId, RpcParameters parameters, RpcParameterValidationRules rules)
    {
        // Validate parameter counts
        var countResult = ValidateParameterCounts(commandId, parameters, rules);
        if (!countResult.IsValid)
        {
            return countResult;
        }

        // Validate numeric parameters
        var numericResult = ValidateNumericParameters(commandId, parameters, rules);
        if (!numericResult.IsValid)
        {
            return numericResult;
        }

        // Validate string parameters
        var stringResult = ValidateStringParameters(commandId, parameters, rules);
        if (!stringResult.IsValid)
        {
            return stringResult;
        }

        // Run custom validators
        foreach (var customValidator in rules.CustomValidators)
        {
            var customResult = customValidator(parameters);
            if (!customResult.IsValid)
            {
                if (customResult.IsSecurityViolation || rules.IsSecuritySensitive)
                {
                    _logger.LogWarning($"RPC security violation in command {commandId}: {customResult.ErrorMessage}");
                }
                else
                {
                    _logger.LogWarning($"RPC parameter validation failed for command {commandId}: {customResult.ErrorMessage}");
                }
                return customResult;
            }
        }

        _logger.LogDebug($"RPC parameter validation successful for command {commandId}");
        return RpcParameterValidationResult.Success();
    }

    /// <summary>
    /// Validates parameter counts against the rules.
    /// </summary>
    private RpcParameterValidationResult ValidateParameterCounts(int commandId, RpcParameters parameters, RpcParameterValidationRules rules)
    {
        // Check exact numeric parameter count
        if (rules.ExpectedNumericParameterCount.HasValue)
        {
            if (parameters.NumericParameters.Length != rules.ExpectedNumericParameterCount.Value)
            {
                var error = $"Command {commandId} expects {rules.ExpectedNumericParameterCount.Value} numeric parameters, but got {parameters.NumericParameters.Length}";
                _logger.LogWarning($"RPC parameter validation failed: {error}");
                return RpcParameterValidationResult.Failure(error, RpcParameterValidationErrorType.InvalidValue);
            }
        }

        // Check minimum numeric parameter count
        if (rules.MinNumericParameterCount.HasValue)
        {
            if (parameters.NumericParameters.Length < rules.MinNumericParameterCount.Value)
            {
                var error = $"Command {commandId} requires at least {rules.MinNumericParameterCount.Value} numeric parameters, but got {parameters.NumericParameters.Length}";
                _logger.LogWarning($"RPC parameter validation failed: {error}");
                return RpcParameterValidationResult.Failure(error, RpcParameterValidationErrorType.MissingParameter);
            }
        }

        // Check maximum numeric parameter count
        if (rules.MaxNumericParameterCount.HasValue)
        {
            if (parameters.NumericParameters.Length > rules.MaxNumericParameterCount.Value)
            {
                var error = $"Command {commandId} allows at most {rules.MaxNumericParameterCount.Value} numeric parameters, but got {parameters.NumericParameters.Length}";
                _logger.LogWarning($"RPC parameter validation failed: {error}");
                return RpcParameterValidationResult.Failure(error, RpcParameterValidationErrorType.TooManyParameters);
            }
        }

        // Check exact string parameter count
        if (rules.ExpectedStringParameterCount.HasValue)
        {
            if (parameters.StringParameters.Length != rules.ExpectedStringParameterCount.Value)
            {
                var error = $"Command {commandId} expects {rules.ExpectedStringParameterCount.Value} string parameters, but got {parameters.StringParameters.Length}";
                _logger.LogWarning($"RPC parameter validation failed: {error}");
                return RpcParameterValidationResult.Failure(error, RpcParameterValidationErrorType.InvalidValue);
            }
        }

        return RpcParameterValidationResult.Success();
    }

    /// <summary>
    /// Validates numeric parameters against their individual rules.
    /// </summary>
    private RpcParameterValidationResult ValidateNumericParameters(int commandId, RpcParameters parameters, RpcParameterValidationRules rules)
    {
        foreach (var (parameterIndex, rule) in rules.NumericParameterRules)
        {
            // Check if parameter is provided
            if (parameterIndex >= parameters.NumericParameters.Length)
            {
                if (rule.IsRequired)
                {
                    var error = $"Command {commandId} missing required numeric parameter at index {parameterIndex} ({rule.ParameterName ?? "unnamed"})";
                    _logger.LogWarning($"RPC parameter validation failed: {error}");
                    return RpcParameterValidationResult.Failure(error, RpcParameterValidationErrorType.MissingParameter, rule.ParameterName);
                }
                continue; // Optional parameter not provided, skip validation
            }

            var value = parameters.NumericParameters[parameterIndex];

            // Validate against allowed values
            if (rule.AllowedValues != null && !rule.AllowedValues.Contains(value))
            {
                var error = $"Command {commandId} parameter '{rule.ParameterName ?? $"param_{parameterIndex}"}' has invalid value {value}, allowed values: [{string.Join(", ", rule.AllowedValues)}]";
                _logger.LogWarning($"RPC parameter validation failed: {error}");
                
                var result = rule.IsSecuritySensitive 
                    ? RpcParameterValidationResult.SecurityViolation(error, rule.ParameterName, value)
                    : RpcParameterValidationResult.Failure(error, RpcParameterValidationErrorType.InvalidValue, rule.ParameterName, value);
                
                if (rule.IsSecuritySensitive)
                {
                    _logger.LogWarning($"RPC security violation in command {commandId}: {error}");
                }
                
                return result;
            }

            // Validate against forbidden values
            if (rule.ForbiddenValues != null && rule.ForbiddenValues.Contains(value))
            {
                var error = $"Command {commandId} parameter '{rule.ParameterName ?? $"param_{parameterIndex}"}' has forbidden value {value}";
                _logger.LogWarning($"RPC security violation in command {commandId}: {error}");
                return RpcParameterValidationResult.SecurityViolation(error, rule.ParameterName, value);
            }

            // Validate range
            if (rule.MinValue.HasValue && value < rule.MinValue.Value)
            {
                var error = $"Command {commandId} parameter '{rule.ParameterName ?? $"param_{parameterIndex}"}' value {value} is below minimum {rule.MinValue.Value}";
                _logger.LogWarning($"RPC parameter validation failed: {error}");
                
                var result = rule.IsSecuritySensitive 
                    ? RpcParameterValidationResult.SecurityViolation(error, rule.ParameterName, value)
                    : RpcParameterValidationResult.Failure(error, RpcParameterValidationErrorType.InvalidValue, rule.ParameterName, value);
                
                if (rule.IsSecuritySensitive)
                {
                    _logger.LogWarning($"RPC security violation in command {commandId}: {error}");
                }
                
                return result;
            }

            if (rule.MaxValue.HasValue && value > rule.MaxValue.Value)
            {
                var error = $"Command {commandId} parameter '{rule.ParameterName ?? $"param_{parameterIndex}"}' value {value} is above maximum {rule.MaxValue.Value}";
                _logger.LogWarning($"RPC parameter validation failed: {error}");
                
                var result = rule.IsSecuritySensitive 
                    ? RpcParameterValidationResult.SecurityViolation(error, rule.ParameterName, value)
                    : RpcParameterValidationResult.Failure(error, RpcParameterValidationErrorType.InvalidValue, rule.ParameterName, value);
                
                if (rule.IsSecuritySensitive)
                {
                    _logger.LogWarning($"RPC security violation in command {commandId}: {error}");
                }
                
                return result;
            }
        }

        return RpcParameterValidationResult.Success();
    }

    /// <summary>
    /// Validates string parameters against their individual rules.
    /// </summary>
    private RpcParameterValidationResult ValidateStringParameters(int commandId, RpcParameters parameters, RpcParameterValidationRules rules)
    {
        foreach (var (parameterIndex, rule) in rules.StringParameterRules)
        {
            // Check if parameter is provided
            if (parameterIndex >= parameters.StringParameters.Length)
            {
                if (rule.IsRequired)
                {
                    var error = $"Command {commandId} missing required string parameter at index {parameterIndex} ({rule.ParameterName ?? "unnamed"})";
                    _logger.LogWarning($"RPC parameter validation failed: {error}");
                    return RpcParameterValidationResult.Failure(error, RpcParameterValidationErrorType.MissingParameter, rule.ParameterName);
                }
                continue; // Optional parameter not provided, skip validation
            }

            var value = parameters.StringParameters[parameterIndex];

            // Validate against allowed values
            if (rule.AllowedValues != null && !rule.AllowedValues.Contains(value))
            {
                var error = $"Command {commandId} parameter '{rule.ParameterName ?? $"param_{parameterIndex}"}' has invalid value '{value}', allowed values: [{string.Join(", ", rule.AllowedValues)}]";
                _logger.LogWarning($"RPC parameter validation failed: {error}");
                
                var result = rule.IsSecuritySensitive 
                    ? RpcParameterValidationResult.SecurityViolation(error, rule.ParameterName, value)
                    : RpcParameterValidationResult.Failure(error, RpcParameterValidationErrorType.InvalidValue, rule.ParameterName, value);
                
                if (rule.IsSecuritySensitive)
                {
                    _logger.LogWarning($"RPC security violation in command {commandId}: {error}");
                }
                
                return result;
            }

            // Validate length
            if (rule.MinLength.HasValue && value.Length < rule.MinLength.Value)
            {
                var error = $"Command {commandId} parameter '{rule.ParameterName ?? $"param_{parameterIndex}"}' length {value.Length} is below minimum {rule.MinLength.Value}";
                _logger.LogWarning($"RPC parameter validation failed: {error}");
                return RpcParameterValidationResult.Failure(error, RpcParameterValidationErrorType.InvalidValue, rule.ParameterName, value);
            }

            if (rule.MaxLength.HasValue && value.Length > rule.MaxLength.Value)
            {
                var error = $"Command {commandId} parameter '{rule.ParameterName ?? $"param_{parameterIndex}"}' length {value.Length} is above maximum {rule.MaxLength.Value}";
                _logger.LogWarning($"RPC parameter validation failed: {error}");
                
                var result = rule.IsSecuritySensitive 
                    ? RpcParameterValidationResult.SecurityViolation(error, rule.ParameterName, value)
                    : RpcParameterValidationResult.Failure(error, RpcParameterValidationErrorType.InvalidValue, rule.ParameterName, value);
                
                if (rule.IsSecuritySensitive)
                {
                    _logger.LogWarning($"RPC security violation in command {commandId}: {error}");
                }
                
                return result;
            }

            // Validate pattern
            if (!string.IsNullOrEmpty(rule.Pattern))
            {
                try
                {
                    if (!Regex.IsMatch(value, rule.Pattern))
                    {
                        var error = $"Command {commandId} parameter '{rule.ParameterName ?? $"param_{parameterIndex}"}' value '{value}' does not match required pattern '{rule.Pattern}'";
                        _logger.LogWarning($"RPC parameter validation failed: {error}");
                        
                        var result = rule.IsSecuritySensitive 
                            ? RpcParameterValidationResult.SecurityViolation(error, rule.ParameterName, value)
                            : RpcParameterValidationResult.Failure(error, RpcParameterValidationErrorType.InvalidFormat, rule.ParameterName, value);
                        
                        if (rule.IsSecuritySensitive)
                        {
                            _logger.LogWarning($"RPC security violation in command {commandId}: {error}");
                        }
                        
                        return result;
                    }
                }
                catch (RegexMatchTimeoutException)
                {
                    var error = $"Command {commandId} parameter '{rule.ParameterName ?? $"param_{parameterIndex}"}' pattern matching timed out (potential ReDoS attack)";
                    _logger.LogWarning($"RPC security violation in command {commandId}: {error}");
                    return RpcParameterValidationResult.SecurityViolation(error, rule.ParameterName, value);
                }
            }
        }

        return RpcParameterValidationResult.Success();
    }
}