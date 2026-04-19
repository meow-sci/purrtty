namespace caTTY.Core.Rpc;

/// <summary>
/// Base class for query RPC command handlers.
/// These commands execute queries and return responses to the client.
/// </summary>
public abstract class QueryCommandHandler : RpcCommandHandler
{
    /// <summary>
    /// Initializes a new instance of the QueryCommandHandler.
    /// </summary>
    /// <param name="description">A description of what this command does</param>
    /// <param name="timeout">The timeout for query execution (default: 5 seconds)</param>
    protected QueryCommandHandler(string description, TimeSpan timeout = default) 
        : base(description, isFireAndForget: false, timeout == default ? TimeSpan.FromSeconds(5) : timeout)
    {
    }

    /// <inheritdoc />
    public sealed override async Task<object?> ExecuteAsync(RpcParameters parameters)
    {
        EnsureValidParameters(parameters);
        return await ExecuteQueryAsync(parameters);
    }

    /// <summary>
    /// Executes the query with the provided parameters and returns the result.
    /// Override this method to implement the specific query logic.
    /// </summary>
    /// <param name="parameters">The query parameters</param>
    /// <returns>The query result data</returns>
    protected virtual async Task<object?> ExecuteQueryAsync(RpcParameters parameters)
    {
        // Default implementation calls synchronous method
        return await Task.FromResult(ExecuteQuery(parameters));
    }

    /// <summary>
    /// Executes a synchronous query with the provided parameters.
    /// This is a convenience method for queries that don't need async execution.
    /// Override either this method or ExecuteQueryAsync for custom behavior.
    /// </summary>
    /// <param name="parameters">The query parameters</param>
    /// <returns>The query result data</returns>
    protected virtual object? ExecuteQuery(RpcParameters parameters)
    {
        // Default implementation returns null - override either this or ExecuteQueryAsync
        return null;
    }

    /// <summary>
    /// Creates a structured response object for common query patterns.
    /// </summary>
    /// <param name="status">The status or state being queried</param>
    /// <param name="value">The current value</param>
    /// <param name="additionalData">Optional additional data</param>
    /// <returns>A structured response object</returns>
    protected static object CreateResponse(string status, object? value, object? additionalData = null)
    {
        var response = new Dictionary<string, object?>
        {
            ["status"] = status,
            ["value"] = value
        };

        if (additionalData != null)
        {
            response["data"] = additionalData;
        }

        return response;
    }

    /// <summary>
    /// Creates a simple value response for queries that return a single value.
    /// </summary>
    /// <param name="value">The value to return</param>
    /// <returns>The value wrapped in a response structure</returns>
    protected static object CreateValueResponse(object? value)
    {
        return new { value };
    }

    /// <summary>
    /// Creates an error response for queries that encounter errors.
    /// </summary>
    /// <param name="errorMessage">The error message</param>
    /// <param name="errorCode">Optional error code</param>
    /// <returns>An error response object</returns>
    protected static object CreateErrorResponse(string errorMessage, int? errorCode = null)
    {
        var response = new Dictionary<string, object?>
        {
            ["error"] = errorMessage
        };

        if (errorCode.HasValue)
        {
            response["errorCode"] = errorCode.Value;
        }

        return response;
    }
}