namespace caTTY.Core.Rpc;

/// <summary>
/// Interface for managing game action command registration and lifecycle.
/// Provides a centralized registry for default vehicle commands and custom command registration.
/// </summary>
public interface IGameActionRegistry
{
    /// <summary>
    /// Registers the default set of vehicle control commands.
    /// This includes engine control, navigation, and system commands.
    /// </summary>
    void RegisterVehicleCommands();

    /// <summary>
    /// Registers default system commands for game state queries and control.
    /// </summary>
    void RegisterSystemCommands();

    /// <summary>
    /// Registers a custom command handler with validation.
    /// </summary>
    /// <param name="commandId">The command ID (must be in valid range)</param>
    /// <param name="handler">The command handler implementation</param>
    /// <returns>True if registration was successful, false if validation failed or ID already exists</returns>
    bool RegisterCustomCommand(int commandId, IRpcCommandHandler handler);

    /// <summary>
    /// Unregisters a command by ID.
    /// </summary>
    /// <param name="commandId">The command ID to unregister</param>
    /// <returns>True if unregistration was successful</returns>
    bool UnregisterCommand(int commandId);

    /// <summary>
    /// Validates that a command ID is in the appropriate range for its type.
    /// </summary>
    /// <param name="commandId">The command ID to validate</param>
    /// <param name="isFireAndForget">Whether this is a fire-and-forget command</param>
    /// <returns>True if the command ID is valid for the specified type</returns>
    bool ValidateCommandId(int commandId, bool isFireAndForget);

    /// <summary>
    /// Gets all registered command IDs.
    /// </summary>
    /// <returns>An enumerable of registered command IDs</returns>
    IEnumerable<int> GetRegisteredCommands();

    /// <summary>
    /// Checks if a command is registered.
    /// </summary>
    /// <param name="commandId">The command ID to check</param>
    /// <returns>True if the command is registered</returns>
    bool IsCommandRegistered(int commandId);

    /// <summary>
    /// Clears all registered commands (useful for testing or reset scenarios).
    /// </summary>
    void ClearAllCommands();

    /// <summary>
    /// Gets the total number of registered commands.
    /// </summary>
    int CommandCount { get; }
}