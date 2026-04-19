namespace caTTY.Core.Rpc;

/// <summary>
/// Defines validation rules for RPC command parameters.
/// </summary>
public class RpcParameterValidationRules
{
    /// <summary>
    /// Gets or sets the expected number of numeric parameters.
    /// </summary>
    public int? ExpectedNumericParameterCount { get; set; }

    /// <summary>
    /// Gets or sets the expected number of string parameters.
    /// </summary>
    public int? ExpectedStringParameterCount { get; set; }

    /// <summary>
    /// Gets or sets the minimum number of numeric parameters required.
    /// </summary>
    public int? MinNumericParameterCount { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of numeric parameters allowed.
    /// </summary>
    public int? MaxNumericParameterCount { get; set; }

    /// <summary>
    /// Gets or sets validation rules for individual numeric parameters.
    /// Key is parameter index, value is the validation rule.
    /// </summary>
    public Dictionary<int, NumericParameterRule> NumericParameterRules { get; set; } = new();

    /// <summary>
    /// Gets or sets validation rules for individual string parameters.
    /// Key is parameter index, value is the validation rule.
    /// </summary>
    public Dictionary<int, StringParameterRule> StringParameterRules { get; set; } = new();

    /// <summary>
    /// Gets or sets custom validation functions.
    /// These are executed after basic validation passes.
    /// </summary>
    public List<Func<RpcParameters, RpcParameterValidationResult>> CustomValidators { get; set; } = new();

    /// <summary>
    /// Gets or sets whether this command is considered security-sensitive.
    /// Security-sensitive commands have stricter validation and logging.
    /// </summary>
    public bool IsSecuritySensitive { get; set; }

    /// <summary>
    /// Gets or sets the description of what this command does (for logging).
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Adds a numeric parameter validation rule.
    /// </summary>
    /// <param name="parameterIndex">The index of the parameter</param>
    /// <param name="rule">The validation rule</param>
    /// <returns>This instance for method chaining</returns>
    public RpcParameterValidationRules AddNumericRule(int parameterIndex, NumericParameterRule rule)
    {
        NumericParameterRules[parameterIndex] = rule;
        return this;
    }

    /// <summary>
    /// Adds a string parameter validation rule.
    /// </summary>
    /// <param name="parameterIndex">The index of the parameter</param>
    /// <param name="rule">The validation rule</param>
    /// <returns>This instance for method chaining</returns>
    public RpcParameterValidationRules AddStringRule(int parameterIndex, StringParameterRule rule)
    {
        StringParameterRules[parameterIndex] = rule;
        return this;
    }

    /// <summary>
    /// Adds a custom validation function.
    /// </summary>
    /// <param name="validator">The custom validation function</param>
    /// <returns>This instance for method chaining</returns>
    public RpcParameterValidationRules AddCustomValidator(Func<RpcParameters, RpcParameterValidationResult> validator)
    {
        CustomValidators.Add(validator);
        return this;
    }

    /// <summary>
    /// Sets this command as security-sensitive.
    /// </summary>
    /// <param name="description">Optional description of the security sensitivity</param>
    /// <returns>This instance for method chaining</returns>
    public RpcParameterValidationRules AsSecuritySensitive(string? description = null)
    {
        IsSecuritySensitive = true;
        if (description != null)
        {
            Description = description;
        }
        return this;
    }
}

/// <summary>
/// Validation rule for numeric parameters.
/// </summary>
public class NumericParameterRule
{
    /// <summary>
    /// Gets or sets the minimum allowed value (inclusive).
    /// </summary>
    public int? MinValue { get; set; }

    /// <summary>
    /// Gets or sets the maximum allowed value (inclusive).
    /// </summary>
    public int? MaxValue { get; set; }

    /// <summary>
    /// Gets or sets the set of allowed values (if specified, only these values are valid).
    /// </summary>
    public HashSet<int>? AllowedValues { get; set; }

    /// <summary>
    /// Gets or sets the set of forbidden values.
    /// </summary>
    public HashSet<int>? ForbiddenValues { get; set; }

    /// <summary>
    /// Gets or sets whether this parameter is required.
    /// </summary>
    public bool IsRequired { get; set; } = true;

    /// <summary>
    /// Gets or sets the default value if the parameter is not provided.
    /// </summary>
    public int? DefaultValue { get; set; }

    /// <summary>
    /// Gets or sets the parameter name for error messages.
    /// </summary>
    public string? ParameterName { get; set; }

    /// <summary>
    /// Gets or sets whether values outside the safe range represent security violations.
    /// </summary>
    public bool IsSecuritySensitive { get; set; }

    /// <summary>
    /// Creates a rule for a parameter that must be within a specific range.
    /// </summary>
    /// <param name="minValue">The minimum allowed value</param>
    /// <param name="maxValue">The maximum allowed value</param>
    /// <param name="parameterName">The parameter name for error messages</param>
    /// <param name="isSecuritySensitive">Whether violations are security issues</param>
    /// <returns>A new numeric parameter rule</returns>
    public static NumericParameterRule Range(int minValue, int maxValue, string? parameterName = null, bool isSecuritySensitive = false)
    {
        return new NumericParameterRule
        {
            MinValue = minValue,
            MaxValue = maxValue,
            ParameterName = parameterName,
            IsSecuritySensitive = isSecuritySensitive
        };
    }

    /// <summary>
    /// Creates a rule for a parameter that must be one of specific allowed values.
    /// </summary>
    /// <param name="allowedValues">The allowed values</param>
    /// <param name="parameterName">The parameter name for error messages</param>
    /// <param name="isSecuritySensitive">Whether violations are security issues</param>
    /// <returns>A new numeric parameter rule</returns>
    public static NumericParameterRule WithAllowedValues(IEnumerable<int> allowedValues, string? parameterName = null, bool isSecuritySensitive = false)
    {
        return new NumericParameterRule
        {
            AllowedValues = new HashSet<int>(allowedValues),
            ParameterName = parameterName,
            IsSecuritySensitive = isSecuritySensitive
        };
    }

    /// <summary>
    /// Creates a rule for an optional parameter with a default value.
    /// </summary>
    /// <param name="defaultValue">The default value</param>
    /// <param name="minValue">The minimum allowed value (optional)</param>
    /// <param name="maxValue">The maximum allowed value (optional)</param>
    /// <param name="parameterName">The parameter name for error messages</param>
    /// <returns>A new numeric parameter rule</returns>
    public static NumericParameterRule Optional(int defaultValue, int? minValue = null, int? maxValue = null, string? parameterName = null)
    {
        return new NumericParameterRule
        {
            IsRequired = false,
            DefaultValue = defaultValue,
            MinValue = minValue,
            MaxValue = maxValue,
            ParameterName = parameterName
        };
    }
}

/// <summary>
/// Validation rule for string parameters.
/// </summary>
public class StringParameterRule
{
    /// <summary>
    /// Gets or sets the minimum allowed length.
    /// </summary>
    public int? MinLength { get; set; }

    /// <summary>
    /// Gets or sets the maximum allowed length.
    /// </summary>
    public int? MaxLength { get; set; }

    /// <summary>
    /// Gets or sets the regular expression pattern the string must match.
    /// </summary>
    public string? Pattern { get; set; }

    /// <summary>
    /// Gets or sets the set of allowed values (if specified, only these values are valid).
    /// </summary>
    public HashSet<string>? AllowedValues { get; set; }

    /// <summary>
    /// Gets or sets whether this parameter is required.
    /// </summary>
    public bool IsRequired { get; set; } = true;

    /// <summary>
    /// Gets or sets the default value if the parameter is not provided.
    /// </summary>
    public string? DefaultValue { get; set; }

    /// <summary>
    /// Gets or sets the parameter name for error messages.
    /// </summary>
    public string? ParameterName { get; set; }

    /// <summary>
    /// Gets or sets whether invalid values represent security violations.
    /// </summary>
    public bool IsSecuritySensitive { get; set; }

    /// <summary>
    /// Creates a rule for a string parameter with length constraints.
    /// </summary>
    /// <param name="minLength">The minimum allowed length</param>
    /// <param name="maxLength">The maximum allowed length</param>
    /// <param name="parameterName">The parameter name for error messages</param>
    /// <param name="isSecuritySensitive">Whether violations are security issues</param>
    /// <returns>A new string parameter rule</returns>
    public static StringParameterRule Length(int minLength, int maxLength, string? parameterName = null, bool isSecuritySensitive = false)
    {
        return new StringParameterRule
        {
            MinLength = minLength,
            MaxLength = maxLength,
            ParameterName = parameterName,
            IsSecuritySensitive = isSecuritySensitive
        };
    }

    /// <summary>
    /// Creates a rule for a string parameter that must match a pattern.
    /// </summary>
    /// <param name="pattern">The regular expression pattern</param>
    /// <param name="parameterName">The parameter name for error messages</param>
    /// <param name="isSecuritySensitive">Whether violations are security issues</param>
    /// <returns>A new string parameter rule</returns>
    public static StringParameterRule WithPattern(string pattern, string? parameterName = null, bool isSecuritySensitive = false)
    {
        return new StringParameterRule
        {
            Pattern = pattern,
            ParameterName = parameterName,
            IsSecuritySensitive = isSecuritySensitive
        };
    }

    /// <summary>
    /// Creates a rule for a string parameter that must be one of specific allowed values.
    /// </summary>
    /// <param name="allowedValues">The allowed values</param>
    /// <param name="parameterName">The parameter name for error messages</param>
    /// <param name="isSecuritySensitive">Whether violations are security issues</param>
    /// <returns>A new string parameter rule</returns>
    public static StringParameterRule WithAllowedValues(IEnumerable<string> allowedValues, string? parameterName = null, bool isSecuritySensitive = false)
    {
        return new StringParameterRule
        {
            AllowedValues = new HashSet<string>(allowedValues),
            ParameterName = parameterName,
            IsSecuritySensitive = isSecuritySensitive
        };
    }
}