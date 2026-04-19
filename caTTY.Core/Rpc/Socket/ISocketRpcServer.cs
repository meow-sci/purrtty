namespace caTTY.Core.Rpc.Socket;

/// <summary>
/// Interface for the TCP RPC server.
/// Manages socket lifecycle and client connections.
/// </summary>
public interface ISocketRpcServer : IDisposable
{
    /// <summary>
    /// Gets the endpoint (host:port) this server is listening on.
    /// </summary>
    string Endpoint { get; }

    /// <summary>
    /// Gets whether the server is currently running.
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// Starts the server and begins accepting connections.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to stop the server</param>
    /// <returns>Task that completes when the server stops</returns>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the server and closes all connections.
    /// </summary>
    Task StopAsync();
}
