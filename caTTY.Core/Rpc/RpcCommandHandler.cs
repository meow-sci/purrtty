namespace caTTY.Core.Rpc;

/// <summary>
/// Abstract base class for RPC command handlers.
/// Provides common functionality and enforces the command handler contract.
/// </summary>
public abstract class RpcCommandHandler : IRpcCommandHandler
{
    /// <summary>
    /// Initializes a new instance of the RpcCommandHandler.
    /// </summary>
    /// <param name="description">A description of what this command does</param>
    /// <param name="isFireAndForget">Whether this is a fire-and-forget command</param>
    /// <param name="timeout">The timeout for command execution (applies to query commands)</param>
    protected RpcCommandHandler(string description, bool isFireAndForget, TimeSpan timeout = default)
    {
        Description = description ?? throw new ArgumentNullException(nameof(description));
        IsFireAndForget = isFireAndForget;
        Timeout = isFireAndForget && timeout == default ? TimeSpan.Zero : 
                  (timeout == default ? TimeSpan.FromSeconds(5) : timeout);
    }

    /// <inheritdoc />
    public abstract Task<object?> ExecuteAsync(RpcParameters parameters);

    /// <inheritdoc />
    public bool IsFireAndForget { get; }

    /// <inheritdoc />
    public TimeSpan Timeout { get; }

    /// <inheritdoc />
    public string Description { get; }

    /// <summary>
    /// Validates that the provided parameters meet the command's requirements.
    /// Override this method to implement custom parameter validation.
    /// </summary>
    /// <param name="parameters">The parameters to validate</param>
    /// <returns>True if parameters are valid, false otherwise</returns>
    protected virtual bool ValidateParameters(RpcParameters parameters)
    {
        return parameters != null;
    }

    /// <summary>
    /// Executes parameter validation and throws an exception if validation fails.
    /// </summary>
    /// <param name="parameters">The parameters to validate</param>
    /// <exception cref="ArgumentException">Thrown when parameters are invalid</exception>
    protected void EnsureValidParameters(RpcParameters parameters)
    {
        if (parameters == null)
        {
            throw new ArgumentNullException(nameof(parameters), $"Parameters cannot be null for command: {Description}");
        }

        if (!ValidateParameters(parameters))
        {
            throw new ArgumentException($"Invalid parameters for command: {Description}", nameof(parameters));
        }
    }

    /// <summary>
    /// Validates that a numeric parameter is within the specified range.
    /// </summary>
    /// <param name="value">The value to validate</param>
    /// <param name="min">The minimum allowed value (inclusive)</param>
    /// <param name="max">The maximum allowed value (inclusive)</param>
    /// <param name="parameterName">The name of the parameter for error messages</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when value is outside the valid range</exception>
    protected static void ValidateRange(int value, int min, int max, string parameterName)
    {
        if (value < min || value > max)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, 
                $"Parameter '{parameterName}' must be between {min} and {max}, but was {value}");
        }
    }

    /// <summary>
    /// Validates that a string parameter is not null or empty.
    /// </summary>
    /// <param name="value">The string to validate</param>
    /// <param name="parameterName">The name of the parameter for error messages</param>
    /// <exception cref="ArgumentException">Thrown when string is null or empty</exception>
    protected static void ValidateNotEmpty(string? value, string parameterName)
    {
        if (string.IsNullOrEmpty(value))
        {
            throw new ArgumentException($"Parameter '{parameterName}' cannot be null or empty", parameterName);
        }
    }

    /// <summary>
    /// Validates that the parameters array has the expected number of elements.
    /// </summary>
    /// <param name="parameters">The parameters to validate</param>
    /// <param name="expectedCount">The expected number of parameters</param>
    /// <exception cref="ArgumentException">Thrown when parameter count doesn't match</exception>
    protected static void ValidateParameterCount(RpcParameters parameters, int expectedCount)
    {
        if (parameters.NumericParameters.Length != expectedCount)
        {
            throw new ArgumentException(
                $"Expected {expectedCount} numeric parameters, but got {parameters.NumericParameters.Length}",
                nameof(parameters));
        }
    }
}