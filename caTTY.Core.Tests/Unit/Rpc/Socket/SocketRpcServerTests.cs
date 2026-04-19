using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using caTTY.Core.Rpc.Socket;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace caTTY.Core.Tests.Unit.Rpc.Socket;

[TestFixture]
[Category("Unit")]
public class SocketRpcServerTests : IDisposable
{
    private readonly ILogger _logger = NullLogger.Instance;
    private const string TestHost = "127.0.0.1";
    
    // Use dynamic port allocation to avoid conflicts
    private static int GetAvailablePort()
    {
        using var socket = new System.Net.Sockets.Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var port = ((IPEndPoint)socket.LocalEndPoint!).Port;
        socket.Close();
        return port;
    }

    [SetUp]
    public void SetUp()
    {
        // No setup needed for TCP
    }

    [TearDown]
    public void Dispose()
    {
        // No cleanup needed for TCP
    }

    [Test]
    public async Task StartAsync_StartsServer()
    {
        // Arrange
        var port = GetAvailablePort();
        var handler = new TestHandler(req => SocketRpcResponse.Ok());
        using var server = new SocketRpcServer(TestHost, port, handler, _logger);

        // Act
        await server.StartAsync();

        // Assert
        Assert.That(server.IsRunning, Is.True);
        Assert.That(server.Endpoint, Is.EqualTo($"{TestHost}:{port}"));

        await server.StopAsync();
    }

    [Test]
    public async Task StopAsync_StopsServer()
    {
        // Arrange
        var port = GetAvailablePort();
        var handler = new TestHandler(req => SocketRpcResponse.Ok());
        using var server = new SocketRpcServer(TestHost, port, handler, _logger);
        await server.StartAsync();

        // Act
        await server.StopAsync();

        // Assert
        Assert.That(server.IsRunning, Is.False);
    }

    [Test]
    public async Task HandleRequest_ReturnsHandlerResponse()
    {
        // Arrange
        var port = GetAvailablePort();
        var expectedData = new { crafts = new[] { "Rocket-1", "Rocket-2" } };
        var handler = new TestHandler(req =>
        {
            Assert.That(req.Action, Is.EqualTo("list-crafts"));
            return SocketRpcResponse.Ok(expectedData);
        });

        using var server = new SocketRpcServer(TestHost, port, handler, _logger);
        await server.StartAsync();

        // Act
        var response = await SendRequestAsync(TestHost, port, new SocketRpcRequest { Action = "list-crafts" });

        // Assert
        Assert.That(response.Success, Is.True);
        Assert.That(response.Data, Is.Not.Null);

        await server.StopAsync();
    }

    [Test]
    public async Task HandleRequest_InvalidJson_ReturnsError()
    {
        // Arrange
        var port = GetAvailablePort();
        var handler = new TestHandler(req => SocketRpcResponse.Ok());
        using var server = new SocketRpcServer(TestHost, port, handler, _logger);
        await server.StartAsync();

        // Act
        var response = await SendRawRequestAsync(TestHost, port, "not valid json\n");

        // Assert
        Assert.That(response.Success, Is.False);
        Assert.That(response.Error, Does.Contain("Invalid JSON"));

        await server.StopAsync();
    }

    [Test]
    public async Task HandleRequest_MissingAction_ReturnsError()
    {
        // Arrange
        var port = GetAvailablePort();
        var handler = new TestHandler(req => SocketRpcResponse.Ok());
        using var server = new SocketRpcServer(TestHost, port, handler, _logger);
        await server.StartAsync();

        // Act
        var response = await SendRawRequestAsync(TestHost, port, "{}\n");

        // Assert
        Assert.That(response.Success, Is.False);
        Assert.That(response.Error, Does.Contain("missing action"));

        await server.StopAsync();
    }

    [Test]
    public async Task HandleRequest_HandlerThrows_ReturnsError()
    {
        // Arrange
        var port = GetAvailablePort();
        var handler = new TestHandler(req => throw new InvalidOperationException("Game not loaded"));
        using var server = new SocketRpcServer(TestHost, port, handler, _logger);
        await server.StartAsync();

        // Act
        var response = await SendRequestAsync(TestHost, port, new SocketRpcRequest { Action = "test" });

        // Assert
        Assert.That(response.Success, Is.False);
        Assert.That(response.Error, Does.Contain("Game not loaded"));

        await server.StopAsync();
    }

    [Test]
    public async Task HandleRequest_WithParams_PassesParamsToHandler()
    {
        // Arrange
        var port = GetAvailablePort();
        JsonElement? receivedParams = null;
        var handler = new TestHandler(req =>
        {
            receivedParams = req.Params;
            return SocketRpcResponse.Ok();
        });

        using var server = new SocketRpcServer(TestHost, port, handler, _logger);
        await server.StartAsync();

        // Act
        var request = "{\"action\":\"test\",\"params\":{\"craftId\":42}}\n";
        await SendRawRequestAsync(TestHost, port, request);

        // Assert
        Assert.That(receivedParams, Is.Not.Null);
        Assert.That(receivedParams!.Value.GetProperty("craftId").GetInt32(), Is.EqualTo(42));

        await server.StopAsync();
    }

    [Test]
    public void StartAsync_AlreadyRunning_ThrowsInvalidOperationException()
    {
        // Arrange
        var port = GetAvailablePort();
        var handler = new TestHandler(req => SocketRpcResponse.Ok());
        using var server = new SocketRpcServer(TestHost, port, handler, _logger);
        server.StartAsync().Wait();

        // Act & Assert
        Assert.ThrowsAsync<InvalidOperationException>(async () => await server.StartAsync());

        server.StopAsync().Wait();
    }

    [Test]
    public async Task Dispose_StopsServer()
    {
        // Arrange
        var port = GetAvailablePort();
        var handler = new TestHandler(req => SocketRpcResponse.Ok());
        var server = new SocketRpcServer(TestHost, port, handler, _logger);
        await server.StartAsync();

        // Act
        server.Dispose();
        
        // Give the server a moment to clean up the accept task
        await Task.Delay(100);

        // Assert - server should eventually stop
        Assert.That(server.IsRunning, Is.False);
    }

    private static async Task<SocketRpcResponse> SendRequestAsync(string host, int port, SocketRpcRequest request)
    {
        var json = JsonSerializer.Serialize(request) + "\n";
        return await SendRawRequestAsync(host, port, json);
    }

    private static async Task<SocketRpcResponse> SendRawRequestAsync(string host, int port, string rawRequest)
    {
        using var socket = new System.Net.Sockets.Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        await socket.ConnectAsync(new IPEndPoint(IPAddress.Parse(host), port));

        using var stream = new NetworkStream(socket);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        await using var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };

        await writer.WriteAsync(rawRequest);
        var responseLine = await reader.ReadLineAsync();

        return JsonSerializer.Deserialize<SocketRpcResponse>(responseLine!)
               ?? throw new InvalidOperationException("Failed to deserialize response");
    }

    private class TestHandler : ISocketRpcHandler
    {
        private readonly Func<SocketRpcRequest, SocketRpcResponse> _handleFunc;

        public TestHandler(Func<SocketRpcRequest, SocketRpcResponse> handleFunc)
        {
            _handleFunc = handleFunc;
        }

        public SocketRpcResponse HandleRequest(SocketRpcRequest request) => _handleFunc(request);
    }
}
