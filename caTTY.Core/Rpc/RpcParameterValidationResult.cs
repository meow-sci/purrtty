namespace caTTY.Core.Rpc;

/// <summary>
/// Represents the result of RPC parameter validation.
/// </summary>
public record RpcParameterValidationResult
{
    /// <summary>
    /// Gets whether the validation was successful.
    /// </summary>
    public bool IsValid { get; init; }

    /// <summary>
    /// Gets the error message if validation failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Gets the validation error type.
    /// </summary>
    public RpcParameterValidationErrorType ErrorType { get; init; }

    /// <summary>
    /// Gets the parameter name that failed validation (if applicable).
    /// </summary>
    public string? ParameterName { get; init; }

    /// <summary>
    /// Gets the invalid parameter value (if applicable).
    /// </summary>
    public object? InvalidValue { get; init; }

    /// <summary>
    /// Gets whether this validation failure represents a security concern.
    /// </summary>
    public bool IsSecurityViolation { get; init; }

    /// <summary>
    /// Creates a successful validation result.
    /// </summary>
    /// <returns>A successful validation result</returns>
    public static RpcParameterValidationResult Success()
    {
        return new RpcParameterValidationResult { IsValid = true };
    }

    /// <summary>
    /// Creates a failed validation result.
    /// </summary>
    /// <param name="errorMessage">The error message</param>
    /// <param name="errorType">The type of validation error</param>
    /// <param name="parameterName">The name of the parameter that failed (optional)</param>
    /// <param name="invalidValue">The invalid value (optional)</param>
    /// <param name="isSecurityViolation">Whether this is a security violation</param>
    /// <returns>A failed validation result</returns>
    public static RpcParameterValidationResult Failure(
        string errorMessage,
        RpcParameterValidationErrorType errorType = RpcParameterValidationErrorType.InvalidValue,
        string? parameterName = null,
        object? invalidValue = null,
        bool isSecurityViolation = false)
    {
        return new RpcParameterValidationResult
        {
            IsValid = false,
            ErrorMessage = errorMessage,
            ErrorType = errorType,
            ParameterName = parameterName,
            InvalidValue = invalidValue,
            IsSecurityViolation = isSecurityViolation
        };
    }

    /// <summary>
    /// Creates a security violation validation result.
    /// </summary>
    /// <param name="errorMessage">The security error message</param>
    /// <param name="parameterName">The name of the parameter that caused the violation (optional)</param>
    /// <param name="invalidValue">The invalid value (optional)</param>
    /// <returns>A security violation validation result</returns>
    public static RpcParameterValidationResult SecurityViolation(
        string errorMessage,
        string? parameterName = null,
        object? invalidValue = null)
    {
        return new RpcParameterValidationResult
        {
            IsValid = false,
            ErrorMessage = errorMessage,
            ErrorType = RpcParameterValidationErrorType.SecurityViolation,
            ParameterName = parameterName,
            InvalidValue = invalidValue,
            IsSecurityViolation = true
        };
    }
}

/// <summary>
/// Represents the type of parameter validation error.
/// </summary>
public enum RpcParameterValidationErrorType
{
    /// <summary>
    /// Invalid parameter value (out of range, wrong type, etc.).
    /// </summary>
    InvalidValue,

    /// <summary>
    /// Missing required parameter.
    /// </summary>
    MissingParameter,

    /// <summary>
    /// Too many parameters provided.
    /// </summary>
    TooManyParameters,

    /// <summary>
    /// Parameter represents a security violation.
    /// </summary>
    SecurityViolation,

    /// <summary>
    /// Parameter would cause unsafe game state changes.
    /// </summary>
    UnsafeOperation,

    /// <summary>
    /// Parameter format is invalid.
    /// </summary>
    InvalidFormat
}