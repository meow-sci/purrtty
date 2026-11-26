using System.Text;
using System.Text.Json.Nodes;
using NUnit.Framework;
using purrTTY.Kfs.Lsp.Protocol;

namespace purrTTY.Kfs.Tools.Tests.Lsp;

/// <summary>
/// <see cref="JsonRpcConnection"/> on its own (no game): Content-Length framing split and merged in
/// every way, large bodies, malformed input. The connection survives what it can answer and closes
/// cleanly on what it cannot; nothing ever escapes its read loop.
/// </summary>
[TestFixture]
public sealed class JsonRpcFramingTests
{
    /// <summary>An echo peer: <c>echo</c> answers its params, <c>fail</c> throws, <c>wait</c> waits for cancellation; notifications are recorded.</summary>
    private sealed class Echo : IDisposable
    {
        private readonly Thread _thread;
        private Exception? _escaped;

        public Echo(JsonRpcOptions? options = null)
        {
            var (serverIn, serverOut, clientIn, clientOut, toServer, _) = MemoryPipe.Duplex();
            ToServer = toServer;
            Connection = new JsonRpcConnection(serverIn, serverOut, options);
            Connection.RequestHandler = (request, token) => request.Method switch
            {
                "echo" => Task.FromResult(request.Params?.DeepClone()),
                "fail" => throw new JsonRpcException(JsonRpcErrorCodes.InvalidParams, "bad"),
                "wait" => Task.Delay(Timeout.Infinite, token).ContinueWith<JsonNode?>(_ => null, TaskScheduler.Default),
                _ => throw new JsonRpcException(JsonRpcErrorCodes.MethodNotFound, "no " + request.Method),
            };
            Connection.NotificationHandler = n =>
            {
                lock (Notifications) Notifications.Add(n.Method);
            };
            Client = new LspTestClient(clientIn, clientOut);
            _thread = new Thread(() =>
            {
                try { Connection.Run(); }
                catch (Exception e) { _escaped = e; }
            }) { IsBackground = true };
            _thread.Start();
        }

        public JsonRpcConnection Connection { get; }
        public LspTestClient Client { get; }
        public MemoryPipe ToServer { get; }
        public List<string> Notifications { get; } = [];

        /// <summary>True when the connection's read loop returned (cleanly: nothing escaped).</summary>
        public bool Ended(TimeSpan? timeout = null) => _thread.Join(timeout ?? LspTestClient.Timeout) && _escaped is null;

        public void Dispose()
        {
            Client.Dispose();
            Connection.Close();
            Assert.That(Ended(), Is.True, "the read loop returned without an exception");
        }
    }

    private static string Body(int id, string method, string paramsJson = "{}") =>
        $"{{\"jsonrpc\":\"2.0\",\"id\":{id},\"method\":\"{method}\",\"params\":{paramsJson}}}";

    private static string Framed(string body) => $"Content-Length: {Encoding.UTF8.GetByteCount(body)}\r\n\r\n{body}";

    [Test]
    public void Framing_Headers_Split_Across_Reads()
    {
        using var echo = new Echo();
        string body = Body(1, "echo", "{\"x\":\"é\"}");
        int length = Encoding.UTF8.GetByteCount(body);
        // Every piece is one read on the server side (MemoryPipe), cutting the header mid-name, mid-number and mid-CRLF.
        foreach (string piece in new[] { "Content-Le", "ngth: ", length.ToString()[..1], length.ToString()[1..] + "\r", "\n", "Content-Type: application/vscode-jsonrpc; charset=utf-8\r\n\r" })
        {
            echo.Client.SendRaw(piece);
        }
        byte[] bytes = Encoding.UTF8.GetBytes("\n" + body);
        echo.Client.SendRaw(bytes[..7]);
        echo.Client.SendRaw(bytes[7..]);

        JsonObject response = echo.Client.WaitForResponse(1);
        Assert.That(response["result"]?["x"]?.GetValue<string>(), Is.EqualTo("é"));
    }

    [Test]
    public void Framing_Several_Messages_In_One_Read()
    {
        using var echo = new Echo();
        string all = Framed(Body(1, "echo", "[1]")) + Framed("{\"jsonrpc\":\"2.0\",\"method\":\"note\"}") + "\r\n" + Framed(Body(2, "echo", "[2]")) + Framed(Body(3, "fail"));
        echo.Client.SendRaw(all);

        Assert.That(echo.Client.WaitForResponse(1)["result"]![0]!.GetValue<int>(), Is.EqualTo(1));
        Assert.That(echo.Client.WaitForResponse(2)["result"]![0]!.GetValue<int>(), Is.EqualTo(2));
        Assert.That(echo.Client.WaitForResponse(3)["error"]?["code"]?.GetValue<int>(), Is.EqualTo(JsonRpcErrorCodes.InvalidParams));
        lock (echo.Notifications) Assert.That(echo.Notifications, Is.EqualTo(new[] { "note" }));
    }

    [Test]
    public void Framing_Large_Body()
    {
        using var echo = new Echo();
        string big = new string('k', 3 * 1024 * 1024) + "🐱";
        var request = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = 7, ["method"] = "echo", ["params"] = new JsonObject { ["text"] = big } };
        byte[] frame = LspTestClient.Frame(request);
        // Delivered in 64 KiB reads.
        for (int i = 0; i < frame.Length; i += 65536) echo.Client.SendRaw(frame[i..Math.Min(frame.Length, i + 65536)]);

        Assert.That(echo.Client.WaitForResponse(7)["result"]?["text"]?.GetValue<string>(), Is.EqualTo(big));
    }

    [Test]
    public void Framing_Oversized_Body_Is_Skipped()
    {
        using var echo = new Echo(new JsonRpcOptions { MaxMessageBytes = 1000 });
        echo.Client.SendRaw(Framed(Body(1, "echo", "\"" + new string('x', 5000) + "\"")) + Framed(Body(2, "echo", "[2]")));

        Assert.That(echo.Client.WaitForResponse(2)["result"]![0]!.GetValue<int>(), Is.EqualTo(2), "the next message is read");
        Assert.That(echo.Client.Messages.Any(m => m["id"]?.ToJsonString() == "1"), Is.False, "the oversized one is dropped");
    }

    [Test]
    public void Framing_Invalid_Json_Is_Answered_And_The_Connection_Survives()
    {
        using var echo = new Echo();
        echo.Client.SendRaw(Framed("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":") + Framed("[1,2]") + Framed("42") + Framed("{}") + Framed("") +
            Framed("{\"jsonrpc\":\"2.0\",\"id\":{\"x\":1},\"method\":\"echo\"}") + Framed(Body(9, "echo", "[9]")));

        Assert.That(echo.Client.WaitForResponse(9)["result"]![0]!.GetValue<int>(), Is.EqualTo(9));
        var errors = echo.Client.Messages.Where(m => m["error"] is not null).Select(m => m["error"]!["code"]!.GetValue<int>()).ToList();
        Assert.That(errors, Is.EqualTo(new[]
        {
            JsonRpcErrorCodes.ParseError, JsonRpcErrorCodes.InvalidRequest, JsonRpcErrorCodes.InvalidRequest,
            JsonRpcErrorCodes.InvalidRequest, JsonRpcErrorCodes.InvalidRequest, JsonRpcErrorCodes.InvalidRequest,
        }), echo.Client.Dump());
        Assert.That(echo.Client.Messages.Where(m => m["error"] is not null).All(m => m.ContainsKey("id") && m["id"] is null), Is.True, "errors without a usable id answer with id null");
    }

    [TestCase("Content-Type: application/vscode-jsonrpc\r\n\r\n{}")]
    [TestCase("Content-Length: lots\r\n\r\n{}")]
    [TestCase("Content-Length: -5\r\n\r\n")]
    [TestCase("no colon here\r\n\r\n")]
    [TestCase("Content-Length: 100\r\n\r\n{\"jsonrpc\":")]
    public void Framing_Unrecoverable_Input_Closes_Cleanly(string input)
    {
        using var echo = new Echo();
        echo.Client.SendRaw(input);
        if (input.Contains("100")) echo.ToServer.Close(); // end of stream inside the body

        Assert.That(echo.Ended(), Is.True, "the read loop returned without an exception");
        Assert.That(echo.Connection.CloseReason, Is.Not.Null);
        Assert.That(echo.Client.WaitForEnd(), Is.True, "the peer sees the end of the stream");
    }

    [Test]
    public void Framing_Header_Line_Too_Long_Closes_Cleanly()
    {
        using var echo = new Echo(new JsonRpcOptions { MaxHeaderLineBytes = 64 });
        echo.Client.SendRaw("X-Junk: " + new string('j', 500));

        Assert.That(echo.Ended(), Is.True);
        Assert.That(echo.Connection.CloseReason, Does.Contain("longer than 64 bytes"));
    }

    [Test]
    public void Cancel_Request_Answers_RequestCancelled()
    {
        using var echo = new Echo();
        int id = echo.Client.SendRequest("wait");
        echo.Client.Notify("$/cancelRequest", new JsonObject { ["id"] = id });

        Assert.That(echo.Client.WaitForResponse(id)["error"]?["code"]?.GetValue<int>(), Is.EqualTo(JsonRpcErrorCodes.RequestCancelled));
        lock (echo.Notifications) Assert.That(echo.Notifications, Is.Empty, "$/cancelRequest is the connection's own");
    }

    [Test]
    public void Server_Requests_Get_Their_Response()
    {
        using var echo = new Echo();
        Task<JsonNode?> pending = echo.Connection.SendRequest("workspace/configuration", new JsonObject { ["items"] = new JsonArray() });
        JsonObject request = echo.Client.WaitFor(m => m["method"]?.GetValue<string>() == "workspace/configuration", "the server's request");
        echo.Client.Send(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = request["id"]!.DeepClone(), ["result"] = new JsonArray(1) });

        Assert.That(pending.Wait(LspTestClient.Timeout), Is.True);
        Assert.That(pending.Result![0]!.GetValue<int>(), Is.EqualTo(1));

        Task<JsonNode?> refused = echo.Connection.SendRequest("window/showMessageRequest", null);
        JsonObject second = echo.Client.WaitFor(m => m["method"]?.GetValue<string>() == "window/showMessageRequest", "the second request");
        echo.Client.Send(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = second["id"]!.DeepClone(), ["error"] = new JsonObject { ["code"] = -32000, ["message"] = "no" } });
        var e = Assert.Throws<AggregateException>(() => refused.Wait(LspTestClient.Timeout));
        Assert.That(e!.InnerException, Is.TypeOf<JsonRpcException>().With.Property(nameof(JsonRpcException.Code)).EqualTo(-32000));
    }
}
