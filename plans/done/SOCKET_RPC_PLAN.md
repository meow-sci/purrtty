# Implementation Plan

## Solution Overview

Implement a bidirectional RPC mechanism using **Unix Domain Sockets** (UDS) for GAME ↔ USERLAND communication. This enables:

- Pipeline-compatible data flow (`ksa-list-crafts | grep rocket | xargs ksa-ignite-engine`)
- Request-reply semantics with JSON payloads
- Cross-platform support (Windows 10+, Linux, macOS via `AF_UNIX`)
- Single-threaded serialized request handling (no concurrency concerns)

**Key Design Decisions:**

1. **Leave existing OSC RPC untouched** - The new UDS RPC is additive, not a replacement
2. **Singleton socket server** - One server for entire game lifecycle, not per-PTY session
3. **Game-level lifecycle** - Starts with mod load, stops with mod unload
4. **Environment variable inheritance** - `KSA_RPC_SOCKET` set at process level, all PTYs inherit it
5. **Synchronous request handling** - Each connection fully handled before accepting next
6. **JSON protocol** - Request and response are newline-delimited JSON objects
7. **Cross-platform UDS** - .NET's `UnixDomainSocketEndPoint` works on Windows 10+ and Unix

**Architecture Diagram:**

```
┌──────────────────────────────────────────────────────────────────────────┐
│                           GAME PROCESS                                   │
│  ┌────────────────────────────────────────────────────────────────────┐  │
│  │  TerminalMod (Singleton)                                           │  │
│  │  ┌──────────────────────────────┐  ┌──────────────────────────┐   │  │
│  │  │   SocketRpcServer            │  │  KSA_RPC_SOCKET env var  │   │  │
│  │  │   - Single server for game   │  │  - Set at game startup   │   │  │
│  │  │   - Listens on UDS           │  │  - All PTYs inherit it   │   │  │
│  │  │   - Routes to handler        │  │                          │   │  │
│  │  └──────────────────────────────┘  └──────────────────────────┘   │  │
│  └────────────────────────────────────────────────────────────────────┘  │
│                                  │                                        │
│  ┌───────────────────────────────┼──────────────────────────┐            │
│  │   TerminalEmulator(s)         │                          │            │
│  │   - ProcessManager (PTY)      │  (no direct connection)  │            │
│  │   - stdin/stdout/stderr       │                          │            │
│  └───────────────────────────────┴──────────────────────────┘            │
│           │                              │                               │
│           │ PTY stdin/stdout             │ Unix Domain Socket            │
│           │ (display + OSC RPC)          │ (bidirectional RPC)           │
└───────────┼──────────────────────────────┼───────────────────────────────┘
            │                              │
            ▼                              ▼
┌──────────────────────────────────────────────────────────────────────────┐
│                         USERLAND PROCESS                                 │
│  ┌───────────────────────────────────────────────────────────────────┐  │
│  │  ksa-list-crafts (TypeScript/bun)                                 │  │
│  │                                                                    │  │
│  │  const client = new KsaRpcClient(process.env.KSA_RPC_SOCKET);     │  │
│  │  const crafts = await client.call("list-crafts", {});             │  │
│  │  crafts.forEach(c => console.log(c.name));  // → stdout/pipe      │  │
│  └───────────────────────────────────────────────────────────────────┘  │
└──────────────────────────────────────────────────────────────────────────┘
```

**Wire Protocol:**

```
REQUEST (client → server):
{"action":"list-crafts","params":{"filter":"rocket"}}\n

RESPONSE (server → client):
{"success":true,"data":["Rocket-1","Rocket-2"]}\n

ERROR RESPONSE:
{"success":false,"error":"No active game session"}\n
```

---

## Task 1: Create Socket RPC Infrastructure in caTTY.Core

**Goal:** Add core socket RPC interfaces and base classes to `caTTY.Core` that are game-agnostic.

**Project:** `caTTY.Core`

**Files to create:**

1. `caTTY.Core/Rpc/Socket/ISocketRpcHandler.cs`
2. `caTTY.Core/Rpc/Socket/ISocketRpcServer.cs`
3. `caTTY.Core/Rpc/Socket/SocketRpcRequest.cs`
4. `caTTY.Core/Rpc/Socket/SocketRpcResponse.cs`
5. `caTTY.Core/Rpc/Socket/SocketRpcServer.cs`

**Detailed Instructions:**

### 1.1 Create `ISocketRpcHandler.cs`

Location: `caTTY.Core/Rpc/Socket/ISocketRpcHandler.cs`

```csharp
namespace caTTY.Core.Rpc.Socket;

/// <summary>
/// Interface for handling socket-based RPC requests.
/// Implementations dispatch actions to game code and return responses.
/// </summary>
public interface ISocketRpcHandler
{
    /// <summary>
    /// Handles an RPC request and returns a response.
    /// Called synchronously on the socket server thread.
    /// </summary>
    /// <param name="request">The deserialized RPC request</param>
    /// <returns>Response to send back to the client</returns>
    SocketRpcResponse HandleRequest(SocketRpcRequest request);
}
```

### 1.2 Create `ISocketRpcServer.cs`

Location: `caTTY.Core/Rpc/Socket/ISocketRpcServer.cs`

```csharp
namespace caTTY.Core.Rpc.Socket;

/// <summary>
/// Interface for the Unix Domain Socket RPC server.
/// Manages socket lifecycle and client connections.
/// </summary>
public interface ISocketRpcServer : IDisposable
{
    /// <summary>
    /// Gets the socket path this server is listening on.
    /// </summary>
    string SocketPath { get; }

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
```

### 1.3 Create `SocketRpcRequest.cs`

Location: `caTTY.Core/Rpc/Socket/SocketRpcRequest.cs`

```csharp
using System.Text.Json.Serialization;

namespace caTTY.Core.Rpc.Socket;

/// <summary>
/// Represents an RPC request received over the socket.
/// </summary>
public sealed class SocketRpcRequest
{
    /// <summary>
    /// The action name to invoke (e.g., "list-crafts", "ignite-engine").
    /// </summary>
    [JsonPropertyName("action")]
    public string Action { get; set; } = string.Empty;

    /// <summary>
    /// Optional parameters for the action as a JSON element.
    /// </summary>
    [JsonPropertyName("params")]
    public System.Text.Json.JsonElement? Params { get; set; }
}
```

### 1.4 Create `SocketRpcResponse.cs`

Location: `caTTY.Core/Rpc/Socket/SocketRpcResponse.cs`

```csharp
using System.Text.Json.Serialization;

namespace caTTY.Core.Rpc.Socket;

/// <summary>
/// Represents an RPC response to send back over the socket.
/// </summary>
public sealed class SocketRpcResponse
{
    /// <summary>
    /// Whether the request was successful.
    /// </summary>
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    /// <summary>
    /// The response data (only present if Success is true).
    /// Can be any JSON-serializable object.
    /// </summary>
    [JsonPropertyName("data")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Data { get; set; }

    /// <summary>
    /// Error message (only present if Success is false).
    /// </summary>
    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Error { get; set; }

    /// <summary>
    /// Creates a successful response with data.
    /// </summary>
    public static SocketRpcResponse Ok(object? data = null) => new() { Success = true, Data = data };

    /// <summary>
    /// Creates a failed response with an error message.
    /// </summary>
    public static SocketRpcResponse Fail(string error) => new() { Success = false, Error = error };
}
```

### 1.5 Create `SocketRpcServer.cs`

Location: `caTTY.Core/Rpc/Socket/SocketRpcServer.cs`

```csharp
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace caTTY.Core.Rpc.Socket;

/// <summary>
/// Unix Domain Socket RPC server implementation.
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

    /// <inheritdoc />
    public string SocketPath { get; }

    /// <inheritdoc />
    public bool IsRunning => _listenSocket != null && _acceptTask != null && !_acceptTask.IsCompleted;

    /// <summary>
    /// Creates a new SocketRpcServer.
    /// </summary>
    /// <param name="socketPath">Path for the Unix domain socket file</param>
    /// <param name="handler">Handler to dispatch requests to</param>
    /// <param name="logger">Logger for diagnostics</param>
    public SocketRpcServer(string socketPath, ISocketRpcHandler handler, ILogger logger)
    {
        SocketPath = socketPath ?? throw new ArgumentNullException(nameof(socketPath));
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

        // Delete existing socket file if present
        if (File.Exists(SocketPath))
        {
            File.Delete(SocketPath);
        }

        // Ensure directory exists
        var dir = Path.GetDirectoryName(SocketPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        _listenSocket = new System.Net.Sockets.Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        _listenSocket.Bind(new UnixDomainSocketEndPoint(SocketPath));
        _listenSocket.Listen(1); // Only 1 connection at a time

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _acceptTask = AcceptLoopAsync(_cts.Token);

        _logger.LogInformation("Socket RPC server started on {SocketPath}", SocketPath);
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

        CleanupSocketFile();
        _logger.LogInformation("Socket RPC server stopped");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cts?.Cancel();
        _cts?.Dispose();

        try { _listenSocket?.Dispose(); } catch { /* ignore */ }

        CleanupSocketFile();
    }

    private void CleanupSocketFile()
    {
        try
        {
            if (File.Exists(SocketPath))
            {
                File.Delete(SocketPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to delete socket file {SocketPath}", SocketPath);
        }
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var client = await _listenSocket!.AcceptAsync(ct).ConfigureAwait(false);
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
                return;
            }

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

            _logger.LogDebug("Socket RPC response: {Response}", responseJson);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error handling client");
        }
    }
}
```

**Verification:**

- Run `dotnet build caTTY.Core` - must compile without errors
- No existing code is modified

**Commit message:**
```
feat(core): add Socket RPC infrastructure for bidirectional communication

Add core interfaces and server implementation for Unix Domain Socket
based RPC communication:

- ISocketRpcHandler: interface for dispatching RPC requests
- ISocketRpcServer: interface for socket server lifecycle
- SocketRpcRequest/Response: JSON-serializable message types
- SocketRpcServer: single-threaded UDS server with accept loop

This enables GAME→USERLAND data flow for pipeline-compatible CLI tools.
The server uses synchronous request handling to avoid game thread safety
concerns.
```

---

## Task 2: Add Unit Tests for SocketRpcServer

**Goal:** Create comprehensive unit tests for the socket RPC server.

**Project:** `caTTY.Core.Tests`

**Files to create:**

1. `caTTY.Core.Tests/Unit/Rpc/Socket/SocketRpcServerTests.cs`
2. `caTTY.Core.Tests/Unit/Rpc/Socket/SocketRpcResponseTests.cs`

**Detailed Instructions:**

### 2.1 Create `SocketRpcServerTests.cs`

Location: `caTTY.Core.Tests/Unit/Rpc/Socket/SocketRpcServerTests.cs`

```csharp
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using caTTY.Core.Rpc.Socket;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace caTTY.Core.Tests.Unit.Rpc.Socket;

public class SocketRpcServerTests : IDisposable
{
    private readonly string _socketPath;
    private readonly ILogger _logger = NullLogger.Instance;

    public SocketRpcServerTests()
    {
        _socketPath = Path.Combine(Path.GetTempPath(), $"catty-test-{Guid.NewGuid()}.sock");
    }

    public void Dispose()
    {
        if (File.Exists(_socketPath))
        {
            try { File.Delete(_socketPath); } catch { }
        }
    }

    [Fact]
    public async Task StartAsync_CreatesSocketFile()
    {
        // Arrange
        var handler = new TestHandler(req => SocketRpcResponse.Ok());
        using var server = new SocketRpcServer(_socketPath, handler, _logger);

        // Act
        await server.StartAsync();

        // Assert
        Assert.True(server.IsRunning);
        Assert.True(File.Exists(_socketPath));

        await server.StopAsync();
    }

    [Fact]
    public async Task StopAsync_RemovesSocketFile()
    {
        // Arrange
        var handler = new TestHandler(req => SocketRpcResponse.Ok());
        using var server = new SocketRpcServer(_socketPath, handler, _logger);
        await server.StartAsync();

        // Act
        await server.StopAsync();

        // Assert
        Assert.False(server.IsRunning);
        Assert.False(File.Exists(_socketPath));
    }

    [Fact]
    public async Task HandleRequest_ReturnsHandlerResponse()
    {
        // Arrange
        var expectedData = new { crafts = new[] { "Rocket-1", "Rocket-2" } };
        var handler = new TestHandler(req =>
        {
            Assert.Equal("list-crafts", req.Action);
            return SocketRpcResponse.Ok(expectedData);
        });

        using var server = new SocketRpcServer(_socketPath, handler, _logger);
        await server.StartAsync();

        // Act
        var response = await SendRequestAsync(_socketPath, new SocketRpcRequest { Action = "list-crafts" });

        // Assert
        Assert.True(response.Success);
        Assert.NotNull(response.Data);

        await server.StopAsync();
    }

    [Fact]
    public async Task HandleRequest_InvalidJson_ReturnsError()
    {
        // Arrange
        var handler = new TestHandler(req => SocketRpcResponse.Ok());
        using var server = new SocketRpcServer(_socketPath, handler, _logger);
        await server.StartAsync();

        // Act
        var response = await SendRawRequestAsync(_socketPath, "not valid json\n");

        // Assert
        Assert.False(response.Success);
        Assert.Contains("Invalid JSON", response.Error);

        await server.StopAsync();
    }

    [Fact]
    public async Task HandleRequest_MissingAction_ReturnsError()
    {
        // Arrange
        var handler = new TestHandler(req => SocketRpcResponse.Ok());
        using var server = new SocketRpcServer(_socketPath, handler, _logger);
        await server.StartAsync();

        // Act
        var response = await SendRawRequestAsync(_socketPath, "{}\n");

        // Assert
        Assert.False(response.Success);
        Assert.Contains("missing action", response.Error);

        await server.StopAsync();
    }

    [Fact]
    public async Task HandleRequest_HandlerThrows_ReturnsError()
    {
        // Arrange
        var handler = new TestHandler(req => throw new InvalidOperationException("Game not loaded"));
        using var server = new SocketRpcServer(_socketPath, handler, _logger);
        await server.StartAsync();

        // Act
        var response = await SendRequestAsync(_socketPath, new SocketRpcRequest { Action = "test" });

        // Assert
        Assert.False(response.Success);
        Assert.Contains("Game not loaded", response.Error);

        await server.StopAsync();
    }

    [Fact]
    public async Task HandleRequest_WithParams_PassesParamsToHandler()
    {
        // Arrange
        JsonElement? receivedParams = null;
        var handler = new TestHandler(req =>
        {
            receivedParams = req.Params;
            return SocketRpcResponse.Ok();
        });

        using var server = new SocketRpcServer(_socketPath, handler, _logger);
        await server.StartAsync();

        // Act
        var request = "{\"action\":\"test\",\"params\":{\"craftId\":42}}\n";
        await SendRawRequestAsync(_socketPath, request);

        // Assert
        Assert.NotNull(receivedParams);
        Assert.Equal(42, receivedParams.Value.GetProperty("craftId").GetInt32());

        await server.StopAsync();
    }

    private static async Task<SocketRpcResponse> SendRequestAsync(string socketPath, SocketRpcRequest request)
    {
        var json = JsonSerializer.Serialize(request) + "\n";
        return await SendRawRequestAsync(socketPath, json);
    }

    private static async Task<SocketRpcResponse> SendRawRequestAsync(string socketPath, string rawRequest)
    {
        using var socket = new System.Net.Sockets.Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath));

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
```

### 2.2 Create `SocketRpcResponseTests.cs`

Location: `caTTY.Core.Tests/Unit/Rpc/Socket/SocketRpcResponseTests.cs`

```csharp
using caTTY.Core.Rpc.Socket;
using Xunit;

namespace caTTY.Core.Tests.Unit.Rpc.Socket;

public class SocketRpcResponseTests
{
    [Fact]
    public void Ok_WithoutData_CreatesSuccessResponse()
    {
        var response = SocketRpcResponse.Ok();

        Assert.True(response.Success);
        Assert.Null(response.Data);
        Assert.Null(response.Error);
    }

    [Fact]
    public void Ok_WithData_IncludesData()
    {
        var data = new { name = "Rocket-1" };
        var response = SocketRpcResponse.Ok(data);

        Assert.True(response.Success);
        Assert.Equal(data, response.Data);
        Assert.Null(response.Error);
    }

    [Fact]
    public void Fail_CreatesErrorResponse()
    {
        var response = SocketRpcResponse.Fail("Something went wrong");

        Assert.False(response.Success);
        Assert.Null(response.Data);
        Assert.Equal("Something went wrong", response.Error);
    }
}
```

**Verification:**

- Run `.\scripts\dotnet-test.ps1 -Filter "FullyQualifiedName~SocketRpc"` - all tests pass
- Run `dotnet build` - solution compiles

**Commit message:**
```
test(core): add unit tests for Socket RPC server

Add comprehensive test coverage for SocketRpcServer:
- Server lifecycle (start/stop, socket file management)
- Request handling (valid requests, params passing)
- Error handling (invalid JSON, missing action, handler exceptions)
- SocketRpcResponse factory methods
```

---

## Task 3: Create KSA Socket RPC Handler in caTTY.TermSequenceRpc

**Goal:** Create the KSA-specific implementation of `ISocketRpcHandler` that dispatches actions to game code.

**Project:** `caTTY.TermSequenceRpc`

**Files to create:**

1. `caTTY.TermSequenceRpc/SocketRpc/KsaSocketRpcHandler.cs`
2. `caTTY.TermSequenceRpc/SocketRpc/Actions/ISocketRpcAction.cs`
3. `caTTY.TermSequenceRpc/SocketRpc/Actions/ListCraftsAction.cs`
4. `caTTY.TermSequenceRpc/SocketRpc/Actions/GetCurrentCraftAction.cs`

**Detailed Instructions:**

### 3.1 Create `ISocketRpcAction.cs`

Location: `caTTY.TermSequenceRpc/SocketRpc/Actions/ISocketRpcAction.cs`

```csharp
using System.Text.Json;
using caTTY.Core.Rpc.Socket;

namespace caTTY.TermSequenceRpc.SocketRpc.Actions;

/// <summary>
/// Interface for individual socket RPC actions.
/// Each action handles a specific command (e.g., "list-crafts").
/// </summary>
public interface ISocketRpcAction
{
    /// <summary>
    /// The action name this handler responds to.
    /// </summary>
    string ActionName { get; }

    /// <summary>
    /// Executes the action and returns a response.
    /// </summary>
    /// <param name="params">Optional parameters from the request</param>
    /// <returns>The response to send back</returns>
    SocketRpcResponse Execute(JsonElement? @params);
}
```

### 3.2 Create `ListCraftsAction.cs`

Location: `caTTY.TermSequenceRpc/SocketRpc/Actions/ListCraftsAction.cs`

```csharp
using System.Text.Json;
using caTTY.Core.Rpc.Socket;
using KSA;
using Microsoft.Extensions.Logging;

namespace caTTY.TermSequenceRpc.SocketRpc.Actions;

/// <summary>
/// Lists all crafts/vehicles in the current game.
/// Returns array of craft info objects.
/// </summary>
public class ListCraftsAction : ISocketRpcAction
{
    private readonly ILogger _logger;

    public string ActionName => "list-crafts";

    public ListCraftsAction(ILogger logger)
    {
        _logger = logger;
    }

    public SocketRpcResponse Execute(JsonElement? @params)
    {
        try
        {
            var crafts = new List<object>();

            // Universe.Vehicles contains all vehicles in the current game
            if (Universe.Vehicles != null)
            {
                foreach (var vehicle in Universe.Vehicles)
                {
                    crafts.Add(new
                    {
                        id = vehicle.VehicleId,
                        name = vehicle.Name,
                        isControlled = vehicle == Program.ControlledVehicle
                    });
                }
            }

            _logger.LogDebug("list-crafts returning {Count} vehicles", crafts.Count);
            return SocketRpcResponse.Ok(crafts);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list crafts");
            return SocketRpcResponse.Fail($"Failed to list crafts: {ex.Message}");
        }
    }
}
```

### 3.3 Create `GetCurrentCraftAction.cs`

Location: `caTTY.TermSequenceRpc/SocketRpc/Actions/GetCurrentCraftAction.cs`

```csharp
using System.Text.Json;
using caTTY.Core.Rpc.Socket;
using KSA;
using Microsoft.Extensions.Logging;

namespace caTTY.TermSequenceRpc.SocketRpc.Actions;

/// <summary>
/// Gets the currently controlled craft/vehicle.
/// Returns craft info or null if no craft is controlled.
/// </summary>
public class GetCurrentCraftAction : ISocketRpcAction
{
    private readonly ILogger _logger;

    public string ActionName => "get-current-craft";

    public GetCurrentCraftAction(ILogger logger)
    {
        _logger = logger;
    }

    public SocketRpcResponse Execute(JsonElement? @params)
    {
        try
        {
            var vehicle = Program.ControlledVehicle;
            if (vehicle == null)
            {
                return SocketRpcResponse.Ok(null);
            }

            var craftInfo = new
            {
                id = vehicle.VehicleId,
                name = vehicle.Name
            };

            _logger.LogDebug("get-current-craft returning {Name}", vehicle.Name);
            return SocketRpcResponse.Ok(craftInfo);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get current craft");
            return SocketRpcResponse.Fail($"Failed to get current craft: {ex.Message}");
        }
    }
}
```

### 3.4 Create `KsaSocketRpcHandler.cs`

Location: `caTTY.TermSequenceRpc/SocketRpc/KsaSocketRpcHandler.cs`

```csharp
using caTTY.Core.Rpc.Socket;
using caTTY.TermSequenceRpc.SocketRpc.Actions;
using Microsoft.Extensions.Logging;

namespace caTTY.TermSequenceRpc.SocketRpc;

/// <summary>
/// KSA-specific socket RPC handler.
/// Routes incoming requests to registered action handlers.
/// </summary>
public class KsaSocketRpcHandler : ISocketRpcHandler
{
    private readonly ILogger _logger;
    private readonly Dictionary<string, ISocketRpcAction> _actions = new(StringComparer.OrdinalIgnoreCase);

    public KsaSocketRpcHandler(ILogger logger)
    {
        _logger = logger;
        RegisterDefaultActions();
    }

    /// <summary>
    /// Registers a custom action handler.
    /// </summary>
    public void RegisterAction(ISocketRpcAction action)
    {
        _actions[action.ActionName] = action;
        _logger.LogDebug("Registered socket RPC action: {ActionName}", action.ActionName);
    }

    /// <inheritdoc />
    public SocketRpcResponse HandleRequest(SocketRpcRequest request)
    {
        if (string.IsNullOrEmpty(request.Action))
        {
            return SocketRpcResponse.Fail("Missing action");
        }

        if (!_actions.TryGetValue(request.Action, out var action))
        {
            _logger.LogWarning("Unknown socket RPC action: {Action}", request.Action);
            return SocketRpcResponse.Fail($"Unknown action: {request.Action}");
        }

        _logger.LogDebug("Executing socket RPC action: {Action}", request.Action);
        return action.Execute(request.Params);
    }

    private void RegisterDefaultActions()
    {
        RegisterAction(new ListCraftsAction(_logger));
        RegisterAction(new GetCurrentCraftAction(_logger));
    }
}
```

**Verification:**

- Run `dotnet build caTTY.TermSequenceRpc` - must compile without errors
- Verify project references `caTTY.Core` (should already be present)

**Commit message:**
```
feat(rpc): add KSA socket RPC handler with craft listing actions

Implement KSA-specific socket RPC handling:

- KsaSocketRpcHandler: routes requests to action handlers
- ISocketRpcAction: interface for individual actions
- ListCraftsAction: returns all vehicles in current game
- GetCurrentCraftAction: returns currently controlled vehicle

Actions are registered at handler construction and can be extended
with custom implementations.
```

---

## Task 4: Add Unit Tests for KsaSocketRpcHandler

**Goal:** Create unit tests for the KSA socket RPC handler (mocking KSA game types).

**Project:** `caTTY.TermSequenceRpc.Tests`

**Files to create:**

1. `caTTY.TermSequenceRpc.Tests/Unit/SocketRpc/KsaSocketRpcHandlerTests.cs`

**Detailed Instructions:**

### 4.1 Create `KsaSocketRpcHandlerTests.cs`

Location: `caTTY.TermSequenceRpc.Tests/Unit/SocketRpc/KsaSocketRpcHandlerTests.cs`

```csharp
using System.Text.Json;
using caTTY.Core.Rpc.Socket;
using caTTY.TermSequenceRpc.SocketRpc;
using caTTY.TermSequenceRpc.SocketRpc.Actions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace caTTY.TermSequenceRpc.Tests.Unit.SocketRpc;

public class KsaSocketRpcHandlerTests
{
    private readonly ILogger _logger = NullLogger.Instance;

    [Fact]
    public void HandleRequest_UnknownAction_ReturnsError()
    {
        // Arrange
        var handler = new KsaSocketRpcHandler(_logger);
        var request = new SocketRpcRequest { Action = "unknown-action" };

        // Act
        var response = handler.HandleRequest(request);

        // Assert
        Assert.False(response.Success);
        Assert.Contains("Unknown action", response.Error);
    }

    [Fact]
    public void HandleRequest_MissingAction_ReturnsError()
    {
        // Arrange
        var handler = new KsaSocketRpcHandler(_logger);
        var request = new SocketRpcRequest { Action = "" };

        // Act
        var response = handler.HandleRequest(request);

        // Assert
        Assert.False(response.Success);
        Assert.Contains("Missing action", response.Error);
    }

    [Fact]
    public void RegisterAction_CustomAction_CanBeInvoked()
    {
        // Arrange
        var handler = new KsaSocketRpcHandler(_logger);
        var customAction = new TestAction("custom-test", SocketRpcResponse.Ok("custom-result"));
        handler.RegisterAction(customAction);

        var request = new SocketRpcRequest { Action = "custom-test" };

        // Act
        var response = handler.HandleRequest(request);

        // Assert
        Assert.True(response.Success);
        Assert.Equal("custom-result", response.Data);
    }

    [Fact]
    public void RegisterAction_OverwritesExistingAction()
    {
        // Arrange
        var handler = new KsaSocketRpcHandler(_logger);
        var action1 = new TestAction("test", SocketRpcResponse.Ok("first"));
        var action2 = new TestAction("test", SocketRpcResponse.Ok("second"));

        handler.RegisterAction(action1);
        handler.RegisterAction(action2);

        var request = new SocketRpcRequest { Action = "test" };

        // Act
        var response = handler.HandleRequest(request);

        // Assert
        Assert.True(response.Success);
        Assert.Equal("second", response.Data);
    }

    [Fact]
    public void HandleRequest_ActionNameIsCaseInsensitive()
    {
        // Arrange
        var handler = new KsaSocketRpcHandler(_logger);
        var customAction = new TestAction("My-Action", SocketRpcResponse.Ok("found"));
        handler.RegisterAction(customAction);

        var request = new SocketRpcRequest { Action = "my-action" }; // lowercase

        // Act
        var response = handler.HandleRequest(request);

        // Assert
        Assert.True(response.Success);
        Assert.Equal("found", response.Data);
    }

    [Fact]
    public void HandleRequest_PassesParamsToAction()
    {
        // Arrange
        var handler = new KsaSocketRpcHandler(_logger);
        JsonElement? receivedParams = null;
        var customAction = new TestAction("test-params", req =>
        {
            receivedParams = req;
            return SocketRpcResponse.Ok();
        });
        handler.RegisterAction(customAction);

        var paramsJson = JsonDocument.Parse("{\"key\":\"value\"}").RootElement;
        var request = new SocketRpcRequest { Action = "test-params", Params = paramsJson };

        // Act
        handler.HandleRequest(request);

        // Assert
        Assert.NotNull(receivedParams);
        Assert.Equal("value", receivedParams.Value.GetProperty("key").GetString());
    }

    private class TestAction : ISocketRpcAction
    {
        private readonly SocketRpcResponse _response;
        private readonly Func<JsonElement?, SocketRpcResponse>? _executeFunc;

        public string ActionName { get; }

        public TestAction(string actionName, SocketRpcResponse response)
        {
            ActionName = actionName;
            _response = response;
        }

        public TestAction(string actionName, Func<JsonElement?, SocketRpcResponse> executeFunc)
        {
            ActionName = actionName;
            _executeFunc = executeFunc;
            _response = SocketRpcResponse.Ok(); // fallback
        }

        public SocketRpcResponse Execute(JsonElement? @params)
        {
            return _executeFunc?.Invoke(@params) ?? _response;
        }
    }
}
```

**Verification:**

- Run `.\scripts\dotnet-test.ps1 -Filter "FullyQualifiedName~KsaSocketRpcHandler"` - all tests pass
- Run `dotnet build` - solution compiles

**Commit message:**
```
test(rpc): add unit tests for KsaSocketRpcHandler

Test coverage for KSA socket RPC handler:
- Unknown/missing action handling
- Custom action registration
- Action overwriting behavior
- Case-insensitive action names
- Parameter passing to actions
```

---

## Task 5: Integrate Socket RPC Server into ProcessManager Lifecycle

**Goal:** Start/stop the socket RPC server when a PTY process starts/stops, and set the `KSA_RPC_SOCKET` environment variable.

**Project:** `caTTY.Core`

**Files to modify:**

1. `caTTY.Core/Terminal/ProcessManager.cs` - Add socket server integration

**Files to create:**

1. `caTTY.Core/Rpc/Socket/SocketRpcServerFactory.cs`

**Detailed Instructions:**

### 5.1 Create `SocketRpcServerFactory.cs`

Location: `caTTY.Core/Rpc/Socket/SocketRpcServerFactory.cs`

```csharp
using Microsoft.Extensions.Logging;

namespace caTTY.Core.Rpc.Socket;

/// <summary>
/// Factory for creating socket RPC servers with unique socket paths.
/// </summary>
public static class SocketRpcServerFactory
{
    /// <summary>
    /// Environment variable name for the RPC socket path.
    /// </summary>
    public const string SocketPathEnvVar = "KSA_RPC_SOCKET";

    /// <summary>
    /// Generates a unique socket path for a terminal session.
    /// </summary>
    /// <param name="sessionId">Optional session identifier for uniqueness</param>
    /// <returns>Full path to the socket file</returns>
    public static string GenerateSocketPath(string? sessionId = null)
    {
        var id = sessionId ?? Guid.NewGuid().ToString("N")[..8];
        var fileName = $"ksa-rpc-{id}.sock";

        // Use temp directory for cross-platform compatibility
        return Path.Combine(Path.GetTempPath(), fileName);
    }

    /// <summary>
    /// Creates a new socket RPC server instance.
    /// </summary>
    /// <param name="handler">Handler to dispatch requests to</param>
    /// <param name="logger">Logger for diagnostics</param>
    /// <param name="sessionId">Optional session identifier</param>
    /// <returns>Configured but not started server instance</returns>
    public static ISocketRpcServer Create(ISocketRpcHandler handler, ILogger logger, string? sessionId = null)
    {
        var socketPath = GenerateSocketPath(sessionId);
        return new SocketRpcServer(socketPath, handler, logger);
    }
}
```

### 5.2 Create `IProcessManager.cs` update (add optional socket RPC support)

First, check if there's an existing `IProcessManager` interface. If it exists, we need to add new members. If not, we'll add to ProcessManager directly.

Create a new interface for socket RPC integration that ProcessManager can optionally implement:

Location: `caTTY.Core/Rpc/Socket/ISocketRpcIntegration.cs`

```csharp
namespace caTTY.Core.Rpc.Socket;

/// <summary>
/// Interface for components that can host a socket RPC server.
/// Implemented by ProcessManager to provide RPC alongside PTY.
/// </summary>
public interface ISocketRpcIntegration
{
    /// <summary>
    /// Gets the socket RPC server, if configured.
    /// </summary>
    ISocketRpcServer? SocketRpcServer { get; }

    /// <summary>
    /// Gets the socket path for client connections.
    /// Returns null if no RPC server is configured.
    /// </summary>
    string? SocketRpcPath { get; }

    /// <summary>
    /// Configures the socket RPC handler to use when starting processes.
    /// Must be called before StartAsync.
    /// </summary>
    /// <param name="handler">The handler to dispatch requests to</param>
    void ConfigureSocketRpc(ISocketRpcHandler handler);
}
```

### 5.3 Modify `ProcessManager.cs`

Add the following changes to `ProcessManager.cs`:

**Add to using statements at top:**
```csharp
using caTTY.Core.Rpc.Socket;
```

**Add interface implementation:**
Change class declaration from:
```csharp
public class ProcessManager : IProcessManager
```
To:
```csharp
public class ProcessManager : IProcessManager, ISocketRpcIntegration
```

**Add new fields after existing fields (around line 20):**
```csharp
    // Socket RPC integration
    private ISocketRpcHandler? _socketRpcHandler;
    private ISocketRpcServer? _socketRpcServer;
    private readonly ILogger _logger;
```

**Add constructor that accepts logger:**
```csharp
    /// <summary>
    /// Creates a new ProcessManager with optional logging.
    /// </summary>
    /// <param name="logger">Logger for diagnostics (optional)</param>
    public ProcessManager(ILogger? logger = null)
    {
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
    }
```

**Add ISocketRpcIntegration implementation after existing properties:**
```csharp
    /// <inheritdoc />
    public ISocketRpcServer? SocketRpcServer => _socketRpcServer;

    /// <inheritdoc />
    public string? SocketRpcPath => _socketRpcServer?.SocketPath;

    /// <inheritdoc />
    public void ConfigureSocketRpc(ISocketRpcHandler handler)
    {
        if (_process != null)
        {
            throw new InvalidOperationException("Cannot configure socket RPC while a process is running");
        }
        _socketRpcHandler = handler;
    }
```

**Modify StartAsync method - add socket server startup after process validation succeeds (near end of try block):**

Find this section in StartAsync (around line 100):
```csharp
            // Validate that the process started successfully
            try
            {
                await ProcessLifecycleManager.ValidateProcessStartAsync(process, shellPath, cancellationToken);
            }
            catch
            {
                CleanupProcess();
                throw;
            }
```

Add after it (before the closing of the try block):
```csharp
            // Start socket RPC server if handler is configured
            if (_socketRpcHandler != null)
            {
                try
                {
                    _socketRpcServer = SocketRpcServerFactory.Create(_socketRpcHandler, _logger);
                    await _socketRpcServer.StartAsync(cancellationToken);
                    _logger.LogInformation("Socket RPC server started at {SocketPath}", _socketRpcServer.SocketPath);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to start socket RPC server, continuing without RPC");
                    _socketRpcServer?.Dispose();
                    _socketRpcServer = null;
                }
            }
```

**Modify CleanupProcess method - add socket server cleanup:**

Find the CleanupProcess method and add socket cleanup at the beginning (after the lock statement):
```csharp
    private void CleanupProcess()
    {
        lock (_processLock)
        {
            // Stop socket RPC server
            if (_socketRpcServer != null)
            {
                try
                {
                    _socketRpcServer.StopAsync().Wait(1000);
                }
                catch { /* ignore */ }
                _socketRpcServer.Dispose();
                _socketRpcServer = null;
            }

            _readCancellationSource?.Cancel();
            // ... rest of existing cleanup
```

**Update ProcessLaunchOptions to include environment variables:**

Check if `ProcessLaunchOptions` has an `EnvironmentVariables` property. If not, it needs to be added. The socket path should be passed to the child process.

### 5.4 Update ProcessLaunchOptions

Location: Check `caTTY.Core/Terminal/ProcessLaunchOptions.cs` and add if not present:

```csharp
    /// <summary>
    /// Additional environment variables to set for the process.
    /// </summary>
    public Dictionary<string, string>? EnvironmentVariables { get; set; }
```

### 5.5 Update ProcessLifecycleManager to pass environment variables

In `ProcessLifecycleManager.CreateProcess`, ensure environment variables from options are passed to the child process.

**Verification:**

- Run `dotnet build caTTY.Core` - must compile without errors
- Existing ProcessManager tests still pass
- No changes to existing public API behavior

**Commit message:**
```
feat(core): integrate socket RPC server with ProcessManager

Add ISocketRpcIntegration interface for optional RPC alongside PTY:
- ConfigureSocketRpc() to set handler before process start
- SocketRpcServer property for access to running server
- SocketRpcPath property for client connection path

ProcessManager now:
- Starts socket RPC server after PTY process validation
- Stops and cleans up server on process termination
- Logs RPC server status but continues on failure

Add SocketRpcServerFactory for generating unique socket paths.
```

---

## Task 6: Update RpcBootstrapper to Wire Socket RPC

**Goal:** Extend `RpcBootstrapper` to create and configure socket RPC handler alongside existing OSC RPC.

**Project:** `caTTY.TermSequenceRpc`

**Files to modify:**

1. `caTTY.TermSequenceRpc/RpcBootstrapper.cs`

**Detailed Instructions:**

### 6.1 Modify `RpcBootstrapper.cs`

Add a new method that creates all RPC handlers including the socket RPC handler:

```csharp
using caTTY.Core.Rpc;
using caTTY.Core.Rpc.Socket;
using caTTY.TermSequenceRpc.SocketRpc;
using Microsoft.Extensions.Logging;

namespace caTTY.TermSequenceRpc;

/// <summary>
/// Factory for creating and wiring KSA-specific RPC handlers.
/// Provides a single entry point for initializing all RPC components needed for KSA game integration.
/// </summary>
public static class RpcBootstrapper
{
    /// <summary>
    /// Creates fully configured RPC handlers for KSA game integration.
    /// Wires up CSI RPC command router and OSC RPC handler with KSA-specific implementations.
    /// </summary>
    /// <param name="logger">Logger instance for debugging and error reporting</param>
    /// <param name="outputCallback">Optional callback for RPC response output (e.g., sending bytes to terminal).
    /// If null, responses will be discarded.</param>
    /// <returns>Tuple of (IRpcHandler for CSI RPC, IOscRpcHandler for OSC RPC)</returns>
    public static (IRpcHandler rpcHandler, IOscRpcHandler oscRpcHandler)
        CreateKsaRpcHandlers(ILogger logger, Action<byte[]>? outputCallback = null)
    {
        // Create RPC infrastructure
        var router = new RpcCommandRouter(logger);
        var responseGenerator = new RpcResponseGenerator();

        var rpcHandler = new RpcHandler(
            router,
            responseGenerator,
            outputCallback ?? (_ => { }), // No-op if no callback provided
            logger);

        // Create KSA-specific handlers
        var oscRpcHandler = new KsaOscRpcHandler(logger);
        var registry = new KsaGameActionRegistry(router, logger, null);

        // Register vehicle commands
        registry.RegisterVehicleCommands();

        return (rpcHandler, oscRpcHandler);
    }

    /// <summary>
    /// Creates all RPC handlers for KSA game integration, including socket RPC for bidirectional communication.
    /// </summary>
    /// <param name="logger">Logger instance for debugging and error reporting</param>
    /// <param name="outputCallback">Optional callback for RPC response output (e.g., sending bytes to terminal).
    /// If null, responses will be discarded.</param>
    /// <returns>Tuple of (IRpcHandler for CSI RPC, IOscRpcHandler for OSC RPC, ISocketRpcHandler for socket RPC)</returns>
    public static (IRpcHandler rpcHandler, IOscRpcHandler oscRpcHandler, ISocketRpcHandler socketRpcHandler)
        CreateAllKsaRpcHandlers(ILogger logger, Action<byte[]>? outputCallback = null)
    {
        // Create existing handlers
        var (rpcHandler, oscRpcHandler) = CreateKsaRpcHandlers(logger, outputCallback);

        // Create socket RPC handler for bidirectional communication
        var socketRpcHandler = new KsaSocketRpcHandler(logger);

        return (rpcHandler, oscRpcHandler, socketRpcHandler);
    }
}
```

**Verification:**

- Run `dotnet build caTTY.TermSequenceRpc` - must compile without errors
- Existing `CreateKsaRpcHandlers` method unchanged and still works

**Commit message:**
```
feat(rpc): add CreateAllKsaRpcHandlers for socket RPC integration

Extend RpcBootstrapper with new factory method that creates all three
RPC handlers:
- IRpcHandler (CSI-based, existing)
- IOscRpcHandler (OSC-based, existing)
- ISocketRpcHandler (socket-based, new)

The existing CreateKsaRpcHandlers method is unchanged for backward
compatibility.
```

---

## Task 7: Initialize Singleton Socket RPC Server in TerminalMod

**Goal:** Start singleton socket RPC server when game mod loads, independent of terminal sessions.

**Project:** `caTTY.GameMod`

**Files to modify:**

1. `caTTY.GameMod/TerminalMod.cs`

**Detailed Instructions:**

### 7.1 Modify `TerminalMod.cs`

**Add field for socket RPC server (class level):**
```csharp
private ISocketRpcServer? _socketRpcServer;
private ISocketRpcHandler? _socketRpcHandler;
```

**Initialize socket RPC in `[StarMapAllModsLoaded]` method (before terminal creation):**

```csharp
[StarMapAllModsLoaded]
public void OnAllModsLoaded()
{
    try
    {
        // Create socket RPC handler (game-level singleton)
        _socketRpcHandler = new KsaSocketRpcHandler(NullLogger.Instance);
        
        // Create and start socket RPC server
        var socketPath = Path.Combine(Path.GetTempPath(), $"ksa-rpc-{Guid.NewGuid():N}.sock");
        _socketRpcServer = new SocketRpcServer(socketPath, _socketRpcHandler, NullLogger.Instance);
        
        // Start server (non-blocking)
        _ = _socketRpcServer.StartAsync();
        
        // Set environment variable for all child processes
        Environment.SetEnvironmentVariable(
            "KSA_RPC_SOCKET",
            _socketRpcServer.SocketPath,
            EnvironmentVariableTarget.Process
        );
        
        Console.WriteLine($"caTTY: Socket RPC server started at {_socketRpcServer.SocketPath}");
        
        // Continue with terminal initialization...
        // (existing terminal creation code remains unchanged)
    }
    catch (Exception ex)
    {
        Console.WriteLine($"caTTY: Failed to start socket RPC server: {ex.Message}");
        // Continue without socket RPC - terminal still works
    }
}
```

**Add cleanup in `[StarMapUnload]` method:**

```csharp
[StarMapUnload]
public void OnUnload()
{
    // Stop socket RPC server
    if (_socketRpcServer != null)
    {
        try
        {
            _socketRpcServer.StopAsync().Wait(1000);
            _socketRpcServer.Dispose();
            Console.WriteLine("caTTY: Socket RPC server stopped");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"caTTY: Error stopping socket RPC: {ex.Message}");
        }
    }
    
    // Existing cleanup code...
}
```

**Update RPC handler creation (if using RpcBootstrapper):**

If currently using `RpcBootstrapper.CreateKsaRpcHandlers()`, update to:
```csharp
var (rpcHandler, oscRpcHandler, socketRpcHandler) = RpcBootstrapper.CreateAllKsaRpcHandlers(
    NullLogger.Instance,
    bytes => _outputBuffer.Add(bytes)
);

_socketRpcHandler = socketRpcHandler;
```

**Key architectural points:**
- Socket server lives at mod level, not terminal level
- One server for entire game instance
- All PTY sessions inherit `KSA_RPC_SOCKET` environment variable
- Terminal initialization code requires **no changes** - completely decoupled
- Server continues running even if all terminals are closed

**Verification:**

- Run `dotnet build caTTY.GameMod` - must compile without errors
- Game mod loads successfully
- Console shows socket RPC startup message
- Socket file created in temp directory
- Multiple terminals can be opened/closed without affecting RPC server
- Environment variable visible in any spawned PTY: `echo $env:KSA_RPC_SOCKET`

**Commit message:**
```
feat(gamemod): add singleton socket RPC server

Initialize socket RPC server at mod lifecycle, not terminal lifecycle:
- Start server in [StarMapAllModsLoaded]
- Stop server in [StarMapUnload]
- Set KSA_RPC_SOCKET environment variable for all child processes
- One server per game instance, independent of terminal sessions

Terminal and ProcessManager code requires no changes - RPC is
completely decoupled from PTY lifecycle.
```

---

## Task 8: Create TypeScript/Bun Client SDK

**Goal:** Create a TypeScript package for userland programs to call game RPC.

**Project:** New directory `userland/ksa-rpc-client`

**Files to create:**

1. `userland/ksa-rpc-client/package.json`
2. `userland/ksa-rpc-client/tsconfig.json`
3. `userland/ksa-rpc-client/src/index.ts`
4. `userland/ksa-rpc-client/src/client.ts`
5. `userland/ksa-rpc-client/src/types.ts`
6. `userland/ksa-rpc-client/README.md`

**Detailed Instructions:**

### 8.1 Create `package.json`

Location: `userland/ksa-rpc-client/package.json`

```json
{
  "name": "@ksa/rpc-client",
  "version": "0.1.0",
  "description": "KSA game RPC client for userland programs",
  "type": "module",
  "main": "./dist/index.js",
  "types": "./dist/index.d.ts",
  "exports": {
    ".": {
      "import": "./dist/index.js",
      "types": "./dist/index.d.ts"
    }
  },
  "scripts": {
    "build": "bun build ./src/index.ts --outdir ./dist --target node",
    "test": "bun test"
  },
  "devDependencies": {
    "@types/bun": "latest",
    "typescript": "^5.0.0"
  },
  "peerDependencies": {
    "bun": ">=1.0.0"
  }
}
```

### 8.2 Create `tsconfig.json`

Location: `userland/ksa-rpc-client/tsconfig.json`

```json
{
  "compilerOptions": {
    "target": "ESNext",
    "module": "ESNext",
    "moduleResolution": "bundler",
    "strict": true,
    "esModuleInterop": true,
    "skipLibCheck": true,
    "outDir": "./dist",
    "declaration": true,
    "declarationDir": "./dist"
  },
  "include": ["src/**/*"]
}
```

### 8.3 Create `src/types.ts`

Location: `userland/ksa-rpc-client/src/types.ts`

```typescript
/**
 * RPC request sent to the game server.
 */
export interface RpcRequest {
  action: string;
  params?: Record<string, unknown>;
}

/**
 * RPC response from the game server.
 */
export interface RpcResponse<T = unknown> {
  success: boolean;
  data?: T;
  error?: string;
}

/**
 * Craft/vehicle information.
 */
export interface CraftInfo {
  id: number;
  name: string;
  isControlled?: boolean;
}

/**
 * Client configuration options.
 */
export interface ClientOptions {
  /** Connection timeout in milliseconds (default: 5000) */
  timeout?: number;
}
```

### 8.4 Create `src/client.ts`

Location: `userland/ksa-rpc-client/src/client.ts`

```typescript
import { connect, type Socket } from "bun";
import type { RpcRequest, RpcResponse, ClientOptions } from "./types";

/**
 * KSA RPC client for communicating with the game via Unix domain socket.
 */
export class KsaRpcClient {
  private readonly socketPath: string;
  private readonly timeout: number;

  /**
   * Creates a new KSA RPC client.
   * @param socketPath Path to the Unix domain socket (defaults to KSA_RPC_SOCKET env var)
   * @param options Client configuration options
   */
  constructor(socketPath?: string, options: ClientOptions = {}) {
    this.socketPath = socketPath ?? process.env.KSA_RPC_SOCKET ?? "";
    this.timeout = options.timeout ?? 5000;

    if (!this.socketPath) {
      throw new Error(
        "Socket path not provided. Set KSA_RPC_SOCKET environment variable or pass socketPath to constructor."
      );
    }
  }

  /**
   * Sends an RPC request to the game and returns the response.
   * @param action The action name to invoke
   * @param params Optional parameters for the action
   * @returns The response data
   * @throws Error if the request fails or times out
   */
  async call<T = unknown>(
    action: string,
    params?: Record<string, unknown>
  ): Promise<T> {
    const request: RpcRequest = { action, params };
    const requestJson = JSON.stringify(request) + "\n";

    return new Promise<T>((resolve, reject) => {
      const timeoutId = setTimeout(() => {
        reject(new Error(`RPC call timed out after ${this.timeout}ms`));
      }, this.timeout);

      let responseBuffer = "";

      const socket = connect({
        unix: this.socketPath,
        socket: {
          data(socket, data) {
            responseBuffer += data.toString();
            const newlineIndex = responseBuffer.indexOf("\n");
            if (newlineIndex !== -1) {
              clearTimeout(timeoutId);
              const responseLine = responseBuffer.slice(0, newlineIndex);
              socket.end();

              try {
                const response: RpcResponse<T> = JSON.parse(responseLine);
                if (response.success) {
                  resolve(response.data as T);
                } else {
                  reject(new Error(response.error ?? "Unknown error"));
                }
              } catch (e) {
                reject(new Error(`Failed to parse response: ${e}`));
              }
            }
          },
          error(socket, error) {
            clearTimeout(timeoutId);
            reject(new Error(`Socket error: ${error.message}`));
          },
          close() {
            clearTimeout(timeoutId);
          },
          open(socket) {
            socket.write(requestJson);
          },
        },
      });
    });
  }
}
```

### 8.5 Create `src/index.ts`

Location: `userland/ksa-rpc-client/src/index.ts`

```typescript
export { KsaRpcClient } from "./client";
export type {
  RpcRequest,
  RpcResponse,
  CraftInfo,
  ClientOptions,
} from "./types";
```

### 8.6 Create `README.md`

Location: `userland/ksa-rpc-client/README.md`

```markdown
# @ksa/rpc-client

TypeScript/Bun client for KSA game RPC communication.

## Installation

```bash
bun add @ksa/rpc-client
```

## Usage

```typescript
import { KsaRpcClient, CraftInfo } from "@ksa/rpc-client";

// Client reads KSA_RPC_SOCKET from environment by default
const client = new KsaRpcClient();

// List all crafts
const crafts = await client.call<CraftInfo[]>("list-crafts");
crafts.forEach((craft) => console.log(craft.name));

// Get current craft
const current = await client.call<CraftInfo | null>("get-current-craft");
if (current) {
  console.log(`Controlling: ${current.name}`);
}
```

## Environment Variables

- `KSA_RPC_SOCKET` - Path to the Unix domain socket (set automatically by terminal)

## Available Actions

| Action              | Description                      | Returns          |
| ------------------- | -------------------------------- | ---------------- |
| `list-crafts`       | List all vehicles in game        | `CraftInfo[]`    |
| `get-current-craft` | Get currently controlled vehicle | `CraftInfo|null` |
```

**Verification:**

- Run `cd userland/ksa-rpc-client && bun install` - dependencies install
- Run `bun build ./src/index.ts --outdir ./dist` - builds without errors
- TypeScript types are correct

**Commit message:**
```
feat(userland): add TypeScript/Bun RPC client SDK

Create @ksa/rpc-client package for userland programs:
- KsaRpcClient: connects to game via Unix domain socket
- Automatic KSA_RPC_SOCKET environment variable handling
- Timeout support for requests
- TypeScript types for craft info and responses

Enables shell commands like:
  ksa-list-crafts | grep rocket | xargs ksa-ignite-engine
```

---

## Task 9: Create Example Userland CLI Tools

**Goal:** Create example TypeScript CLI tools that demonstrate the RPC client.

**Project:** New directory `userland/ksa-tools`

**Files to create:**

1. `userland/ksa-tools/package.json`
2. `userland/ksa-tools/bin/ksa-list-crafts.ts`
3. `userland/ksa-tools/bin/ksa-current-craft.ts`
4. `userland/ksa-tools/README.md`

**Detailed Instructions:**

### 9.1 Create `package.json`

Location: `userland/ksa-tools/package.json`

```json
{
  "name": "@ksa/tools",
  "version": "0.1.0",
  "description": "KSA command-line tools for game interaction",
  "type": "module",
  "bin": {
    "ksa-list-crafts": "./bin/ksa-list-crafts.ts",
    "ksa-current-craft": "./bin/ksa-current-craft.ts"
  },
  "dependencies": {
    "@ksa/rpc-client": "file:../ksa-rpc-client"
  },
  "devDependencies": {
    "@types/bun": "latest"
  }
}
```

### 9.2 Create `bin/ksa-list-crafts.ts`

Location: `userland/ksa-tools/bin/ksa-list-crafts.ts`

```typescript
#!/usr/bin/env bun
/**
 * Lists all crafts/vehicles in the current KSA game.
 * Output: one craft name per line (suitable for piping).
 *
 * Usage:
 *   ksa-list-crafts              # List all craft names
 *   ksa-list-crafts --json       # Output as JSON
 *   ksa-list-crafts --current    # Only show current craft name
 *   ksa-list-crafts | grep rocket | xargs -n1 ksa-ignite-engine
 */

import { KsaRpcClient, type CraftInfo } from "@ksa/rpc-client";

const args = process.argv.slice(2);
const jsonOutput = args.includes("--json");
const currentOnly = args.includes("--current");

try {
  const client = new KsaRpcClient();

  if (currentOnly) {
    const craft = await client.call<CraftInfo | null>("get-current-craft");
    if (craft) {
      if (jsonOutput) {
        console.log(JSON.stringify(craft));
      } else {
        console.log(craft.name);
      }
    }
  } else {
    const crafts = await client.call<CraftInfo[]>("list-crafts");

    if (jsonOutput) {
      console.log(JSON.stringify(crafts, null, 2));
    } else {
      for (const craft of crafts) {
        console.log(craft.name);
      }
    }
  }
} catch (error) {
  console.error(`Error: ${error instanceof Error ? error.message : error}`);
  process.exit(1);
}
```

### 9.3 Create `bin/ksa-current-craft.ts`

Location: `userland/ksa-tools/bin/ksa-current-craft.ts`

```typescript
#!/usr/bin/env bun
/**
 * Gets the currently controlled craft/vehicle.
 * Output: craft name or empty if none controlled.
 *
 * Usage:
 *   ksa-current-craft              # Print current craft name
 *   ksa-current-craft --json       # Output as JSON
 *   ksa-warp --craft $(ksa-current-craft) --to jupiter
 */

import { KsaRpcClient, type CraftInfo } from "@ksa/rpc-client";

const args = process.argv.slice(2);
const jsonOutput = args.includes("--json");

try {
  const client = new KsaRpcClient();
  const craft = await client.call<CraftInfo | null>("get-current-craft");

  if (craft) {
    if (jsonOutput) {
      console.log(JSON.stringify(craft));
    } else {
      console.log(craft.name);
    }
  } else if (jsonOutput) {
    console.log("null");
  }
  // Silent output if no craft and not JSON mode
} catch (error) {
  console.error(`Error: ${error instanceof Error ? error.message : error}`);
  process.exit(1);
}
```

### 9.4 Create `README.md`

Location: `userland/ksa-tools/README.md`

```markdown
# @ksa/tools

Command-line tools for interacting with KSA game from the terminal.

## Installation

```bash
cd userland/ksa-tools
bun install
bun link
```

## Commands

### ksa-list-crafts

Lists all crafts in the current game.

```bash
# List all craft names (one per line)
ksa-list-crafts

# Output as JSON
ksa-list-crafts --json

# Only show currently controlled craft
ksa-list-crafts --current

# Pipeline example
ksa-list-crafts | grep -i rocket
```

### ksa-current-craft

Gets the currently controlled craft.

```bash
# Print current craft name
ksa-current-craft

# Use in command substitution
echo "Controlling: $(ksa-current-craft)"
```

## Environment

These tools require the `KSA_RPC_SOCKET` environment variable to be set.
This is automatically configured when running in the KSA terminal.
```

**Verification:**

- Run `cd userland/ksa-tools && bun install` - dependencies install
- Scripts are syntactically valid TypeScript
- Documentation is complete

**Commit message:**
```
feat(userland): add example KSA CLI tools

Create @ksa/tools package with example commands:
- ksa-list-crafts: lists all vehicles, supports --json and --current
- ksa-current-craft: gets controlled vehicle name

Demonstrates pipeline-compatible CLI design:
  ksa-list-crafts | grep rocket | xargs -n1 ksa-do-something
```

---

## Task 10: Integration Testing and Documentation

**Goal:** Add integration tests and update project documentation.

**Project:** Multiple

**Files to create/modify:**

1. `caTTY.Core.Tests/Integration/SocketRpcIntegrationTests.cs`
2. Update main `README.md` with RPC documentation
3. Update `FEATURE_TRACKING.md` if it tracks RPC features

**Detailed Instructions:**

### 10.1 Create `SocketRpcIntegrationTests.cs`

Location: `caTTY.Core.Tests/Integration/SocketRpcIntegrationTests.cs`

```csharp
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using caTTY.Core.Rpc.Socket;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace caTTY.Core.Tests.Integration;

/// <summary>
/// Integration tests for socket RPC end-to-end communication.
/// </summary>
public class SocketRpcIntegrationTests : IDisposable
{
    private readonly string _socketPath;
    private readonly ILogger _logger = NullLogger.Instance;

    public SocketRpcIntegrationTests()
    {
        _socketPath = Path.Combine(Path.GetTempPath(), $"catty-integration-{Guid.NewGuid()}.sock");
    }

    public void Dispose()
    {
        if (File.Exists(_socketPath))
        {
            try { File.Delete(_socketPath); } catch { }
        }
    }

    [Fact]
    public async Task EndToEnd_MultipleSequentialRequests_AllSucceed()
    {
        // Arrange
        var callCount = 0;
        var handler = new CountingHandler(() =>
        {
            callCount++;
            return SocketRpcResponse.Ok(new { count = callCount });
        });

        using var server = new SocketRpcServer(_socketPath, handler, _logger);
        await server.StartAsync();

        // Act - send 5 sequential requests
        for (int i = 1; i <= 5; i++)
        {
            var response = await SendRequestAsync(new SocketRpcRequest { Action = "count" });
            Assert.True(response.Success);
        }

        // Assert
        Assert.Equal(5, callCount);

        await server.StopAsync();
    }

    [Fact]
    public async Task EndToEnd_LargePayload_HandledCorrectly()
    {
        // Arrange
        var largeData = new string('x', 100_000); // 100KB
        var handler = new EchoHandler();

        using var server = new SocketRpcServer(_socketPath, handler, _logger);
        await server.StartAsync();

        // Act
        var request = new SocketRpcRequest
        {
            Action = "echo",
            Params = JsonDocument.Parse($"{{\"data\":\"{largeData}\"}}").RootElement
        };
        var response = await SendRequestAsync(request);

        // Assert
        Assert.True(response.Success);

        await server.StopAsync();
    }

    [Fact]
    public async Task EndToEnd_ServerStopsDuringRequest_ClientGetsError()
    {
        // Arrange
        var handler = new DelayHandler(TimeSpan.FromSeconds(5));
        using var server = new SocketRpcServer(_socketPath, handler, _logger);
        await server.StartAsync();

        // Act - start request then stop server
        var requestTask = SendRequestAsync(new SocketRpcRequest { Action = "delay" });
        await Task.Delay(100); // Let connection establish
        await server.StopAsync();

        // Assert - request should fail
        await Assert.ThrowsAnyAsync<Exception>(() => requestTask);
    }

    private async Task<SocketRpcResponse> SendRequestAsync(SocketRpcRequest request)
    {
        using var socket = new System.Net.Sockets.Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        await socket.ConnectAsync(new UnixDomainSocketEndPoint(_socketPath));

        using var stream = new NetworkStream(socket);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        await using var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };

        var json = JsonSerializer.Serialize(request) + "\n";
        await writer.WriteAsync(json);

        var responseLine = await reader.ReadLineAsync();
        return JsonSerializer.Deserialize<SocketRpcResponse>(responseLine!)!;
    }

    private class CountingHandler : ISocketRpcHandler
    {
        private readonly Func<SocketRpcResponse> _factory;
        public CountingHandler(Func<SocketRpcResponse> factory) => _factory = factory;
        public SocketRpcResponse HandleRequest(SocketRpcRequest request) => _factory();
    }

    private class EchoHandler : ISocketRpcHandler
    {
        public SocketRpcResponse HandleRequest(SocketRpcRequest request)
            => SocketRpcResponse.Ok(request.Params);
    }

    private class DelayHandler : ISocketRpcHandler
    {
        private readonly TimeSpan _delay;
        public DelayHandler(TimeSpan delay) => _delay = delay;
        public SocketRpcResponse HandleRequest(SocketRpcRequest request)
        {
            Thread.Sleep(_delay);
            return SocketRpcResponse.Ok();
        }
    }
}
```

### 10.2 Update documentation

Add a new section to the main README.md or create `docs/SOCKET_RPC.md` documenting:

- Architecture overview
- How to enable socket RPC
- Wire protocol specification
- Available actions
- Creating custom actions
- TypeScript client usage

### 10.3 Final verification

Run full test suite:
```powershell
.\scripts\dotnet-test.ps1
```

Verify build:
```powershell
dotnet build
```

**Commit message:**
```
test(integration): add socket RPC end-to-end tests

Add integration tests verifying:
- Multiple sequential requests handled correctly
- Large payload handling (100KB+)
- Server shutdown behavior during requests

Update documentation with socket RPC architecture and usage.
```

---

## Summary

| Task | Project | Description |
|------|---------|-------------|
| 1 | caTTY.Core | Socket RPC interfaces and server implementation (game-agnostic) |
| 2 | caTTY.Core.Tests | Unit tests for SocketRpcServer |
| 3 | caTTY.TermSequenceRpc | KSA socket handler and action implementations |
| 4 | caTTY.TermSequenceRpc.Tests | Unit tests for KsaSocketRpcHandler |
| 5 | caTTY.GameMod | Set KSA_RPC_SOCKET environment variable |
| 6 | caTTY.TermSequenceRpc | RpcBootstrapper socket handler factory |
| 7 | caTTY.GameMod | Initialize singleton socket RPC server at mod lifecycle |
| 8 | userland/ksa-rpc-client | TypeScript client SDK |
| 9 | userland/ksa-tools | Example CLI tools |
| 10 | Multiple | Integration tests and documentation |

**Simplified Architecture Benefits:**
- No modifications needed to ProcessManager, SessionManager, or terminal infrastructure
- Single socket server per game instance (singleton pattern)
- Clean separation: RPC is game-level concern, not terminal-level
- All PTY sessions automatically inherit `KSA_RPC_SOCKET` environment variable
- Easier to test and maintain

Each task builds on the previous and can be implemented independently by a focused coding agent with the detailed instructions provided.