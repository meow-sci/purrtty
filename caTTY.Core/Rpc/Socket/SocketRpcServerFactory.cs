using Microsoft.Extensions.Logging;

namespace caTTY.Core.Rpc.Socket;

/// <summary>
/// Factory for creating TCP RPC servers.
/// </summary>
public static class SocketRpcServerFactory
{
    private static string? _activeEndpoint;
    private static readonly object _endpointLock = new object();

    /// <summary>
    /// Environment variable name for the RPC target endpoint.
    /// </summary>
    public const string EndpointEnvVar = "KSA_RPC_TARGET";

    /// <summary>
    /// Default host to bind to (0.0.0.0 for all interfaces, WSL2 compatible).
    /// </summary>
    public const string DefaultHost = "0.0.0.0";

    /// <summary>
    /// Default port for RPC server.
    /// </summary>
    public const int DefaultPort = 4242;

    /// <summary>
    /// Generates the endpoint string for client connections.
    /// </summary>
    /// <param name="host">Host (default: "localhost")</param>
    /// <param name="port">Port (default: 4242)</param>
    /// <returns>Endpoint in host:port format</returns>
    public static string GenerateEndpoint(string? host = null, int? port = null)
    {
        return $"{host ?? "localhost"}:{port ?? DefaultPort}";
    }

    /// <summary>
    /// Creates a new TCP RPC server instance.
    /// </summary>
    /// <param name="handler">Handler to dispatch requests to</param>
    /// <param name="logger">Logger for diagnostics</param>
    /// <param name="host">Host to bind to (default: 0.0.0.0 for all interfaces)</param>
    /// <param name="port">Port to listen on (default: 4242)</param>
    /// <returns>Configured but not started server instance</returns>
    public static ISocketRpcServer Create(
        ISocketRpcHandler handler,
        ILogger logger,
        string? host = null,
        int? port = null)
    {
        var bindHost = host ?? DefaultHost;
        var bindPort = port ?? DefaultPort;
        Console.WriteLine($"[caTTY] Creating TCP RPC server: bind={bindHost}:{bindPort}, client endpoint=localhost:{bindPort}");

        return new SocketRpcServer(
            bindHost,
            bindPort,
            handler,
            logger);
    }

    /// <summary>
    /// Registers an active socket RPC server endpoint.
    /// Called by server implementations when they start.
    /// </summary>
    /// <param name="endpoint">The endpoint in host:port format</param>
    public static void RegisterEndpoint(string endpoint)
    {
        lock (_endpointLock)
        {
            _activeEndpoint = endpoint;
        }
    }

    /// <summary>
    /// Clears the active socket RPC server endpoint.
    /// Called by server implementations when they stop.
    /// </summary>
    public static void ClearEndpoint()
    {
        lock (_endpointLock)
        {
            _activeEndpoint = null;
        }
    }

    /// <summary>
    /// Gets the currently active socket RPC server endpoint.
    /// Returns null if no server is running.
    /// </summary>
    /// <returns>The active endpoint in host:port format, or null if no server is running</returns>
    public static string? GetActiveEndpoint()
    {
        lock (_endpointLock)
        {
            return _activeEndpoint;
        }
    }
}
