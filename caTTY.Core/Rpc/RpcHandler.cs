using Microsoft.Extensions.Logging;

namespace caTTY.Core.Rpc;

/// <summary>
/// Default implementation of IRpcHandler that processes RPC messages from private use area sequences.
/// Provides clean separation between core terminal emulation and RPC functionality.
/// </summary>
public class RpcHandler : IRpcHandler
{
    private readonly IRpcCommandRouter _commandRouter;
    private readonly IRpcResponseGenerator? _responseGenerator;
    private readonly ILogger _logger;
    private readonly Action<byte[]>? _outputWriter;

    /// <summary>
    /// Initializes a new instance of the RpcHandler class.
    /// </summary>
    /// <param name="commandRouter">The command router for executing RPC commands</param>
    /// <param name="responseGenerator">Optional response generator for query commands</param>
    /// <param name="outputWriter">Optional output writer for sending responses back to the terminal</param>
    /// <param name="logger">Logger for debugging and error reporting</param>
    public RpcHandler(
        IRpcCommandRouter commandRouter,
        IRpcResponseGenerator? responseGenerator,
        Action<byte[]>? outputWriter,
        ILogger logger)
    {
        _commandRouter = commandRouter ?? throw new ArgumentNullException(nameof(commandRouter));
        _responseGenerator = responseGenerator;
        _outputWriter = outputWriter;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public bool IsEnabled { get; set; } = true;

    /// <inheritdoc />
    public void HandleRpcMessage(RpcMessage message)
    {
        if (!IsEnabled)
        {
            // Console.WriteLine($"RPC handling is disabled, ignoring message: {message.Raw}");
            return;
        }

        // Console.WriteLine($"Processing RPC message: CommandId={message.CommandId}, Type={message.CommandType}, Raw={message.Raw}");

        // Validate command ID range for the command type
        if (!message.IsValidCommandIdRange())
        {
            _logger.LogWarning("Invalid command ID {CommandId} for command type {CommandType}", 
                            message.CommandId, message.CommandType);
            _ = Task.Run(async () => await HandleInvalidCommand(message));
            return;
        }

        // Route the command for execution
        _ = Task.Run(async () =>
        {
            try
            {
                RpcResult result = await _commandRouter.RouteCommandAsync(message);
                await HandleCommandResult(message, result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception while processing RPC command {CommandId}", message.CommandId);
              //  Console.WriteLine($"Unhandled exception while processing RPC command {message.CommandId}");

                await HandleCommandException(message, ex);
            }
        });
    }

    /// <inheritdoc />
    public void HandleMalformedRpcSequence(ReadOnlySpan<byte> rawSequence, RpcSequenceType sequenceType)
    {
        if (!IsEnabled)
        {
            return;
        }

        string sequenceString = System.Text.Encoding.ASCII.GetString(rawSequence);

        _logger.LogWarning("Malformed RPC sequence detected: Type={SequenceType}, Raw={Raw}",
            sequenceType, sequenceString);

        // Optionally trace malformed sequences for debugging
        switch (sequenceType)
        {
            case RpcSequenceType.InvalidCommandId:
                _logger.LogDebug("RPC sequence has invalid command ID range");
                break;
            case RpcSequenceType.InvalidFinalCharacter:
                _logger.LogDebug("RPC sequence has invalid final character");
                break;
            case RpcSequenceType.Malformed:
                _logger.LogDebug("RPC sequence format is malformed");
                break;
        }
    }

    /// <summary>
    /// Handles the result of a successfully executed RPC command.
    /// </summary>
    /// <param name="message">The original RPC message</param>
    /// <param name="result">The execution result</param>
    private async Task HandleCommandResult(RpcMessage message, RpcResult result)
    {
        if (result.Success)
        {
            _logger.LogDebug("RPC command {CommandId} executed successfully in {ExecutionTime}ms",
                message.CommandId, result.ExecutionTime.TotalMilliseconds);

            // For query commands, send response back to terminal
            if (message.IsQuery && _responseGenerator != null && _outputWriter != null)
            {
                try
                {
                    byte[] responseBytes = _responseGenerator.GenerateResponse(message.CommandId, result.Data);
                    _outputWriter(responseBytes);
                    _logger.LogDebug("Sent response for query command {CommandId}", message.CommandId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to generate or send response for command {CommandId}", message.CommandId);
                    // Try to send an error response instead
                    await SendErrorResponse(message.CommandId, "Failed to generate response", ex);
                }
            }
        }
        else
        {
            // Handle different types of failures
            if (result.IsTimeout)
            {
                _logger.LogWarning("RPC command {CommandId} timed out after {ExecutionTime}ms: {ErrorMessage}",
                    message.CommandId, result.ExecutionTime.TotalMilliseconds, result.ErrorMessage);

                // Send timeout error response for query commands
                if (message.IsQuery && _responseGenerator != null && _outputWriter != null)
                {
                    try
                    {
                        byte[] timeoutBytes = _responseGenerator.GenerateTimeout(message.CommandId);
                        _outputWriter(timeoutBytes);
                        _logger.LogDebug("Sent timeout response for query command {CommandId}", message.CommandId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to generate or send timeout response for command {CommandId}", message.CommandId);
                    }
                }
            }
            else
            {
                _logger.LogWarning("RPC command {CommandId} failed in {ExecutionTime}ms: {ErrorMessage}",
                    message.CommandId, result.ExecutionTime.TotalMilliseconds, result.ErrorMessage);

                // Send error response for query commands
                if (message.IsQuery && _responseGenerator != null && _outputWriter != null)
                {
                    await SendErrorResponse(message.CommandId, result.ErrorMessage ?? "Command execution failed");
                }
            }
        }
    }

    /// <summary>
    /// Handles exceptions that occur during command execution.
    /// </summary>
    /// <param name="message">The original RPC message</param>
    /// <param name="exception">The exception that occurred</param>
    private async Task HandleCommandException(RpcMessage message, Exception exception)
    {
        string errorMessage = $"Command execution failed: {exception.Message}";
        _logger.LogError(exception, "Unhandled exception in RPC command {CommandId}: {ErrorMessage}",
            message.CommandId, exception.Message);

        // Send error response for query commands
        if (message.IsQuery)
        {
            await SendErrorResponse(message.CommandId, errorMessage, exception);
        }
    }

    /// <summary>
    /// Sends an error response for a query command.
    /// </summary>
    /// <param name="commandId">The command ID that failed</param>
    /// <param name="errorMessage">The error message</param>
    /// <param name="exception">Optional exception for additional logging</param>
    private async Task SendErrorResponse(int commandId, string errorMessage, Exception? exception = null)
    {
        if (_responseGenerator != null && _outputWriter != null)
        {
            try
            {
                byte[] errorBytes = _responseGenerator.GenerateError(commandId, errorMessage);
                _outputWriter(errorBytes);
                _logger.LogDebug("Sent error response for command {CommandId}: {ErrorMessage}", commandId, errorMessage);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate or send error response for command {CommandId}. Original error: {OriginalError}",
                    commandId, errorMessage);

                if (exception != null)
                {
                    _logger.LogError(exception, "Original exception details for command {CommandId}", commandId);
                }
            }
        }
    }

    /// <summary>
    /// Handles invalid commands (wrong command ID range for command type).
    /// </summary>
    /// <param name="message">The invalid RPC message</param>
    private async Task HandleInvalidCommand(RpcMessage message)
    {
        string errorMessage = $"Invalid command ID {message.CommandId} for command type {message.CommandType}";
        _logger.LogWarning("Invalid RPC command: {ErrorMessage}. Expected ranges: 1000-1999 (fire-and-forget), 2000-2999 (queries). Raw: {Raw}",
            errorMessage, message.Raw);

        // Send error response for query commands with invalid command IDs
        if (message.IsQuery)
        {
            await SendErrorResponse(9999, errorMessage); // Use system error ID for invalid commands
        }
    }
}
