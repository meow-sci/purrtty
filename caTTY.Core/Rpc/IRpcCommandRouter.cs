namespace caTTY.Core.Rpc;

/// <summary>
/// Interface for routing RPC commands to their appropriate handlers.
/// Manages command registration and execution.
/// </summary>
public interface IRpcCommandRouter
{
    /// <summary>
    /// Routes a parsed RPC message to the appropriate command handler.
    /// </summary>
    /// <param name="message">The RPC message to route</param>
    /// <returns>The result of command execution</returns>
    Task<RpcResult> RouteCommandAsync(RpcMessage message);

    /// <summary>
    /// Registers a command handler for a specific command ID.
    /// </summary>
    /// <param name="commandId">The command ID to register</param>
    /// <param name="handler">The handler for the command</param>
    /// <returns>True if registration was successful</returns>
    bool RegisterCommand(int commandId, IRpcCommandHandler handler);

    /// <summary>
    /// Unregisters a command handler for a specific command ID.
    /// </summary>
    /// <param name="commandId">The command ID to unregister</param>
    /// <returns>True if unregistration was successful</returns>
    bool UnregisterCommand(int commandId);

    /// <summary>
    /// Checks if a command handler is registered for the given command ID.
    /// </summary>
    /// <param name="commandId">The command ID to check</param>
    /// <returns>True if a handler is registered</returns>
    bool IsCommandRegistered(int commandId);

    /// <summary>
    /// Gets all registered command IDs.
    /// </summary>
    /// <returns>An enumerable of registered command IDs</returns>
    IEnumerable<int> GetRegisteredCommands();

    /// <summary>
    /// Clears all registered command handlers.
    /// </summary>
    void ClearAllCommands();
}

/// <summary>
/// Interface for individual RPC command handlers.
/// </summary>
public interface IRpcCommandHandler
{
    /// <summary>
    /// Executes the RPC command with the given parameters.
    /// </summary>
    /// <param name="parameters">The command parameters</param>
    /// <returns>The result of command execution</returns>
    Task<object?> ExecuteAsync(RpcParameters parameters);

    /// <summary>
    /// Indicates whether this is a fire-and-forget command (no response expected).
    /// </summary>
    bool IsFireAndForget { get; }

    /// <summary>
    /// The timeout for command execution (applies to query commands).
    /// </summary>
    TimeSpan Timeout { get; }

    /// <summary>
    /// A description of what this command does (for debugging/logging).
    /// </summary>
    string Description { get; }
}