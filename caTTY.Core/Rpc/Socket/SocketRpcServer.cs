using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace caTTY.Core.Rpc.Socket;

/// <summary>
/// TCP RPC server implementation.
/// Accepts connections, reads JSON requests, dispatches to handler, sends JSON responses.
/// Uses single-threaded accept loop with synchronous request handling.
/// </summary>
public sealed class SocketRpcServer : ISocketRpcServer
{
    private readonly ISocketRpcHandler _handler;
    private readonly ILogger _logger;
    private readonly JsonSerializerOptions _jsonOptions;
    private System.Net.Sockets.Socket? _listenSocket;
    private CancellationTokenSource? _cts;
    private Task? _acceptTask;
    private bool _disposed;
    private readonly int _port;
    private readonly string _host;

    /// <inheritdoc />
    public string Endpoint => $"{_host}:{_port}";

    /// <inheritdoc />
    public bool IsRunning => _listenSocket != null && _acceptTask != null && !_acceptTask.IsCompleted;

    /// <summary>
    /// Creates a new SocketRpcServer.
    /// </summary>
    /// <param name="host">Host to bind to (e.g., "0.0.0.0" or "localhost")</param>
    /// <param name="port">Port to listen on</param>
    /// <param name="handler">Handler to dispatch requests to</param>
    /// <param name="logger">Logger for diagnostics</param>
    public SocketRpcServer(string host, int port, ISocketRpcHandler handler, ILogger logger)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _port = port;
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false
        };
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SocketRpcServer));
        if (IsRunning) throw new InvalidOperationException("Server is already running");

        // Create TCP socket
        _listenSocket = new System.Net.Sockets.Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        
        // Allow reuse of address to avoid "address already in use" errors on quick restart
        _listenSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        
        // Bind to specified host and port
        var ipAddress = _host == "0.0.0.0" ? IPAddress.Any : IPAddress.Parse(_host);
        var endPoint = new IPEndPoint(ipAddress, _port);
        _listenSocket.Bind(endPoint);
        _listenSocket.Listen(10); // Allow multiple connections in backlog

        // Log successful binding
        var actualEndpoint = (IPEndPoint)_listenSocket.LocalEndPoint!;
        Console.WriteLine($"[caTTY] TCP RPC server successfully bound to {actualEndpoint.Address}:{actualEndpoint.Port}");
        Console.WriteLine($"[caTTY] Client endpoint: {Endpoint}");

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _acceptTask = AcceptLoopAsync(_cts.Token);

        _logger.LogInformation("TCP RPC server started on {Endpoint}", Endpoint);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync()
    {
        if (_disposed) return;

        _cts?.Cancel();

        try
        {
            _listenSocket?.Close();
        }
        catch { /* ignore */ }

        if (_acceptTask != null)
        {
            try
            {
                await _acceptTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) { /* expected */ }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Accept loop ended with exception");
            }
        }

        _logger.LogInformation("TCP RPC server stopped");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cts?.Cancel();
        _cts?.Dispose();

        try { _listenSocket?.Dispose(); } catch { /* ignore */ }
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        Console.WriteLine("[caTTY] TCP RPC server accept loop started, waiting for connections...");
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var client = await _listenSocket!.AcceptAsync(ct).ConfigureAwait(false);
                var remoteEndPoint = client.RemoteEndPoint;
                Console.WriteLine($"[caTTY] Client connected from {remoteEndPoint}");
                await HandleClientAsync(client, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.OperationAborted)
            {
                break; // Socket was closed
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error accepting client connection");
            }
        }
    }

    private async Task HandleClientAsync(System.Net.Sockets.Socket client, CancellationToken ct)
    {
        try
        {
            using var stream = new NetworkStream(client, ownsSocket: false);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            await using var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };

            // Read single line JSON request
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (string.IsNullOrEmpty(line))
            {
                Console.WriteLine("[caTTY] Client sent empty request, closing connection");
                return;
            }

            Console.WriteLine($"[caTTY] Request received: {line}");
            _logger.LogDebug("Socket RPC received: {Request}", line);

            SocketRpcResponse response;
            try
            {
                var request = JsonSerializer.Deserialize<SocketRpcRequest>(line, _jsonOptions);
                if (request == null || string.IsNullOrEmpty(request.Action))
                {
                    response = SocketRpcResponse.Fail("Invalid request: missing action");
                }
                else
                {
                    response = _handler.HandleRequest(request);
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse RPC request");
                response = SocketRpcResponse.Fail($"Invalid JSON: {ex.Message}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling RPC request");
                response = SocketRpcResponse.Fail($"Internal error: {ex.Message}");
            }

            var responseJson = JsonSerializer.Serialize(response, _jsonOptions);
            await writer.WriteLineAsync(responseJson).ConfigureAwait(false);
            writer.Flush();
            Console.WriteLine($"Socket RPC response: {responseJson}");
            _logger.LogDebug("Socket RPC response: {Response}", responseJson);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error handling client");
        }
    }
}
