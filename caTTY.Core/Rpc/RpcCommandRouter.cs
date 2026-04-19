using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace caTTY.Core.Rpc;

/// <summary>
/// Routes RPC commands to their appropriate handlers.
/// Manages command registration and execution with support for fire-and-forget and query-response patterns.
/// </summary>
public class RpcCommandRouter : IRpcCommandRouter
{
    private readonly ConcurrentDictionary<int, IRpcCommandHandler> _handlers = new();
    private readonly ILogger _logger;
    private readonly IRpcParameterValidator? _parameterValidator;

    /// <summary>
    /// Initializes a new instance of the RpcCommandRouter.
    /// </summary>
    /// <param name="logger">Logger for debugging and error reporting</param>
    /// <param name="parameterValidator">Optional parameter validator for security validation</param>
    public RpcCommandRouter(ILogger logger, IRpcParameterValidator? parameterValidator = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _parameterValidator = parameterValidator;
    }

    /// <inheritdoc />
    public async Task<RpcResult> RouteCommandAsync(RpcMessage message)
    {
        if (message == null)
        {
            const string error = "RPC message cannot be null";
            Console.WriteLine(error);
            return RpcResult.CreateFailure(error);
        }

        // Validate command ID range for command type
        if (!message.IsValidCommandIdRange())
        {
            var error = $"Invalid command ID {message.CommandId} for command type {message.CommandType}";
            // Console.WriteLine($"RPC routing failed: {error}. Raw sequence: {message.Raw}");
            return RpcResult.CreateFailure(error);
        }

        // Check if handler is registered
        if (!_handlers.TryGetValue(message.CommandId, out var handler))
        {
            var error = $"No handler registered for command ID {message.CommandId}";
            // Console.WriteLine($"RPC routing failed: {error}. Raw sequence: {message.Raw}");
            return RpcResult.CreateFailure(error);
        }

        // Validate command type matches handler expectations
        if (message.IsFireAndForget && !handler.IsFireAndForget)
        {
            var error = $"Command {message.CommandId} is fire-and-forget but handler expects response";
            // Console.WriteLine($"RPC routing failed: {error}. Raw sequence: {message.Raw}");
            return RpcResult.CreateFailure(error);
        }

        if (message.IsQuery && handler.IsFireAndForget)
        {
            var error = $"Command {message.CommandId} is query but handler is fire-and-forget";
            // Console.WriteLine($"RPC routing failed: {error}. Raw sequence: {message.Raw}");
            return RpcResult.CreateFailure(error);
        }

        // Validate parameters if validator is available
        if (_parameterValidator != null)
        {
            var validationResult = _parameterValidator.ValidateParameters(message.CommandId, message.Parameters);
            if (!validationResult.IsValid)
            {
                var error = $"Parameter validation failed for command {message.CommandId}: {validationResult.ErrorMessage}";

                if (validationResult.IsSecurityViolation)
                {
                    _logger.LogWarning($"RPC security violation: {error}. Raw sequence: {message.Raw}");
                }
                else
                {
                    _logger.LogWarning($"RPC parameter validation failed: {error}. Raw sequence: {message.Raw}");
                }

                return RpcResult.CreateFailure(error);
            }
        }

        // Execute the command with comprehensive error handling
        var stopwatch = Stopwatch.StartNew();
        try
        {
            // Console.WriteLine($"Executing RPC command {message.CommandId} ({handler.Description})");

            object? result;
            if (handler.IsFireAndForget)
            {
                // Fire-and-forget commands don't need timeout handling but still need exception handling
                try
                {
                    result = await handler.ExecuteAsync(message.Parameters);
                }
                catch (ArgumentException argEx)
                {
                    // Parameter validation errors
                    stopwatch.Stop();
                    var error = $"Invalid parameters for command {message.CommandId}: {argEx.Message}";
                    // Console.WriteLine($"RPC parameter validation failed: {error}. Raw sequence: {message.Raw}");
                    return RpcResult.CreateFailure(error, stopwatch.Elapsed);
                }
                catch (InvalidOperationException opEx)
                {
                    // Operation state errors
                    stopwatch.Stop();
                    var error = $"Invalid operation for command {message.CommandId}: {opEx.Message}";
                    // Console.WriteLine($"RPC operation failed: {error}. Raw sequence: {message.Raw}");
                    return RpcResult.CreateFailure(error, stopwatch.Elapsed);
                }
            }
            else
            {
                // Query commands need comprehensive timeout and exception handling
                using var cts = new CancellationTokenSource(handler.Timeout);

                try
                {
                    var task = handler.ExecuteAsync(message.Parameters);
                    result = await task.WaitAsync(cts.Token);
                }
                catch (OperationCanceledException) when (cts.Token.IsCancellationRequested)
                {
                    // Timeout occurred
                    stopwatch.Stop();
                    var error = $"Query command {message.CommandId} timed out after {handler.Timeout.TotalMilliseconds}ms";
                    _logger.LogWarning($"RPC query timeout: {error}. Handler: {handler.Description}. Raw sequence: {message.Raw}");
                    return RpcResult.CreateTimeout(message.CommandId, error, stopwatch.Elapsed);
                }
                catch (TimeoutException)
                {
                    // Legacy timeout handling
                    stopwatch.Stop();
                    var error = $"Query command {message.CommandId} timed out after {handler.Timeout.TotalMilliseconds}ms";
                    _logger.LogWarning($"RPC query timeout: {error}. Handler: {handler.Description}. Raw sequence: {message.Raw}");
                    return RpcResult.CreateTimeout(message.CommandId, error, stopwatch.Elapsed);
                }
                catch (ArgumentException argEx)
                {
                    // Parameter validation errors for queries
                    stopwatch.Stop();
                    var error = $"Invalid parameters for query {message.CommandId}: {argEx.Message}";
                    // Console.WriteLine($"RPC query parameter validation failed: {error}. Raw sequence: {message.Raw}");
                    return RpcResult.CreateFailure(error, stopwatch.Elapsed);
                }
                catch (InvalidOperationException opEx)
                {
                    // Operation state errors for queries
                    stopwatch.Stop();
                    var error = $"Invalid operation for query {message.CommandId}: {opEx.Message}";
                    // Console.WriteLine($"RPC query operation failed: {error}. Raw sequence: {message.Raw}");
                    return RpcResult.CreateFailure(error, stopwatch.Elapsed);
                }
            }

            stopwatch.Stop();
            // Console.WriteLine($"RPC command {message.CommandId} completed successfully in {stopwatch.ElapsedMilliseconds}ms");
            return RpcResult.CreateSuccess(result, stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            // Catch-all for any unhandled exceptions
            stopwatch.Stop();
            var error = $"Unexpected error executing command {message.CommandId}: {ex.Message}";
            _logger.LogError(ex, $"RPC command execution failed unexpectedly: {error}. Handler: {handler.Description}. Raw sequence: {message.Raw}");
            return RpcResult.CreateFailure(error, stopwatch.Elapsed);
        }
    }

    /// <inheritdoc />
    public bool RegisterCommand(int commandId, IRpcCommandHandler handler)
    {
        if (handler == null)
        {
            _logger.LogError($"Cannot register null handler for command {commandId}");
            return false;
        }

        // Validate command ID range
        if (!IsValidCommandId(commandId))
        {
            _logger.LogError($"Invalid command ID {commandId}. Must be in range 1000-2999");
            return false;
        }

        // Validate command ID matches handler type
        var isFireAndForgetRange = commandId >= 1000 && commandId <= 1999;
        var isQueryRange = commandId >= 2000 && commandId <= 2999;

        if (handler.IsFireAndForget && !isFireAndForgetRange)
        {
            _logger.LogError($"Fire-and-forget handler for command {commandId} must use range 1000-1999");
            return false;
        }

        if (!handler.IsFireAndForget && !isQueryRange)
        {
            _logger.LogError($"Query handler for command {commandId} must use range 2000-2999");
            return false;
        }

        var success = _handlers.TryAdd(commandId, handler);
        if (success)
        {
            _logger.LogDebug($"Registered RPC command {commandId}: {handler.Description}");
        }
        else
        {
            _logger.LogWarning($"Command {commandId} is already registered");
        }

        return success;
    }

    /// <inheritdoc />
    public bool UnregisterCommand(int commandId)
    {
        var success = _handlers.TryRemove(commandId, out var handler);
        if (success)
        {
            _logger.LogDebug($"Unregistered RPC command {commandId}: {handler?.Description ?? "unknown"}");
        }
        else
        {
            _logger.LogWarning($"Cannot unregister command {commandId}: not found");
        }

        return success;
    }

    /// <inheritdoc />
    public bool IsCommandRegistered(int commandId)
    {
        return _handlers.ContainsKey(commandId);
    }

    /// <inheritdoc />
    public IEnumerable<int> GetRegisteredCommands()
    {
        return _handlers.Keys.ToArray(); // Return a snapshot to avoid concurrent modification issues
    }

    /// <inheritdoc />
    public void ClearAllCommands()
    {
        var count = _handlers.Count;
        _handlers.Clear();
        _logger.LogDebug($"Cleared all {count} registered RPC commands");
    }

    /// <summary>
    /// Validates that a command ID is in the valid range for registration.
    /// </summary>
    /// <param name="commandId">The command ID to validate</param>
    /// <returns>True if the command ID is valid</returns>
    private static bool IsValidCommandId(int commandId)
    {
        // Valid ranges: 1000-1999 (fire-and-forget), 2000-2999 (queries)
        return (commandId >= 1000 && commandId <= 1999) || (commandId >= 2000 && commandId <= 2999);
    }
}
