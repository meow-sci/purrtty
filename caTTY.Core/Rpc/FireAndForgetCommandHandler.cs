namespace caTTY.Core.Rpc;

/// <summary>
/// Base class for fire-and-forget RPC command handlers.
/// These commands execute actions without returning responses to the client.
/// </summary>
public abstract class FireAndForgetCommandHandler : RpcCommandHandler
{
    /// <summary>
    /// Initializes a new instance of the FireAndForgetCommandHandler.
    /// </summary>
    /// <param name="description">A description of what this command does</param>
    protected FireAndForgetCommandHandler(string description) 
        : base(description, isFireAndForget: true, timeout: TimeSpan.Zero)
    {
    }

    /// <inheritdoc />
    public sealed override async Task<object?> ExecuteAsync(RpcParameters parameters)
    {
        EnsureValidParameters(parameters);
        await ExecuteActionAsync(parameters);
        return null; // Fire-and-forget commands never return data
    }

    /// <summary>
    /// Executes the fire-and-forget action with the provided parameters.
    /// Override this method to implement the specific command logic.
    /// </summary>
    /// <param name="parameters">The command parameters</param>
    /// <returns>A task representing the asynchronous operation</returns>
    protected virtual async Task ExecuteActionAsync(RpcParameters parameters)
    {
        // Default implementation calls synchronous method
        await Task.Run(() => ExecuteAction(parameters));
    }

    /// <summary>
    /// Executes a synchronous fire-and-forget action with the provided parameters.
    /// This is a convenience method for commands that don't need async execution.
    /// Override either this method or ExecuteActionAsync for custom behavior.
    /// </summary>
    /// <param name="parameters">The command parameters</param>
    protected virtual void ExecuteAction(RpcParameters parameters)
    {
        // Default implementation does nothing - override either this or ExecuteActionAsync
    }
}