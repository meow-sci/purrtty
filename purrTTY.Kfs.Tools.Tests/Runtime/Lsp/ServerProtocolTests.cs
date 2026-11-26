using System.Net;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using NUnit.Framework;
using purrTTY.Kfs.Lsp;
using purrTTY.Kfs.Lsp.Protocol;
using purrTTY.Kfs.Tools.Tests.Lsp;

namespace purrTTY.Kfs.Tools.Tests.Runtime.Lsp;

/// <summary>The LSP lifecycle of <see cref="KfsLanguageServer"/>, its capabilities, and <see cref="KfsLspListener"/>.</summary>
[TestFixture]
public sealed class ServerProtocolTests : ToolsRuntimeFixture
{
    private static KfsLanguageHost Host(TempWorkspace ws, string? token = null) => KfsLanguageHost.ForBench(Runtime.Tables, ws.Root, token);

    [Test]
    public void Initialize_Negotiates_Utf16_And_Requires_Token()
    {
        using var ws = new TempWorkspace();
        KfsLanguageHost host = Host(ws, token: "s3cret");

        // With a token: a request before initialize is answered once, then the connection is closed (A2).
        using (var early = ServerHarness.Start(host))
        {
            Assert.That(early.Client.Request("textDocument/hover", new JsonObject())["error"]?["code"]?.GetValue<int>(),
                Is.EqualTo(JsonRpcErrorCodes.ServerNotInitialized), "a request before initialize");
            Assert.That(early.Client.WaitForEnd(), Is.True, "and the unauthenticated connection is closed");
            Assert.That(early.WaitForServerEnd(), Is.True);
        }
        // Without a token (stdio): the LSP rule, ServerNotInitialized and the connection carries on.
        using (var open = ServerHarness.Start(Host(ws)))
        {
            Assert.That(open.Client.Request("textDocument/hover", new JsonObject())["error"]?["code"]?.GetValue<int>(),
                Is.EqualTo(JsonRpcErrorCodes.ServerNotInitialized));
            Assert.That(open.Client.Initialize()["capabilities"], Is.Not.Null);
        }

        using (var ok = ServerHarness.Start(host))
        {
            JsonObject result = ok.Client.Initialize("s3cret", "utf-8", "utf-16", "utf-32");
            JsonObject caps = result["capabilities"]!.AsObject();
            Assert.That(caps["positionEncoding"]!.GetValue<string>(), Is.EqualTo("utf-16"));
            Assert.That(caps["textDocumentSync"]!["change"]!.GetValue<int>(), Is.EqualTo(1), "full sync");
            Assert.That(caps["textDocumentSync"]!["openClose"]!.GetValue<bool>(), Is.True);
            Assert.That(caps.Select(p => p.Key), Is.EquivalentTo(new[] { "positionEncoding", "textDocumentSync" }), "no provider registered: nothing else advertised");
            Assert.That(result["serverInfo"]!["name"]!.GetValue<string>(), Is.EqualTo("purrTTY kFS"));
            Assert.That(ok.Server.Encoding, Is.EqualTo(PositionEncoding.Utf16));
            Assert.That(ok.Client.Request("initialize", LspTestClient.InitializeParams("s3cret"))["error"]?["code"]?.GetValue<int>(),
                Is.EqualTo(JsonRpcErrorCodes.InvalidRequest), "a second initialize");
        }

        foreach (string? token in new[] { null, "wrong", "" })
        {
            using var refused = ServerHarness.Start(host);
            JsonObject response = refused.Client.Request("initialize", LspTestClient.InitializeParams(token, "utf-16"));
            Assert.That(response["error"]?["code"]?.GetValue<int>(), Is.EqualTo(JsonRpcErrorCodes.RequestFailed), $"token {token ?? "(none)"}");
            Assert.That(response["error"]?["message"]?.GetValue<string>(), Does.Contain("initializationOptions.token"));
            Assert.That(refused.Client.WaitForEnd(), Is.True, "the server closes the connection after refusing");
            Assert.That(refused.WaitForServerEnd(), Is.True);
        }

        // A client offering only utf-8 or utf-32 gets it; one offering nothing (or nothing known) gets utf-16.
        foreach ((string[] offered, string expected) in new[]
        {
            (new[] { "utf-8" }, "utf-8"), (new[] { "utf-32" }, "utf-32"), (Array.Empty<string>(), "utf-16"), (new[] { "utf-7" }, "utf-16"),
        })
        {
            using var h = ServerHarness.Start(Host(ws));
            JsonNode? r = h.Client.Call("initialize", LspTestClient.InitializeParams(null, offered));
            Assert.That(r!["capabilities"]!["positionEncoding"]!.GetValue<string>(), Is.EqualTo(expected), string.Join(",", offered));
        }
    }

    private sealed class FakeHover : IHoverProvider
    {
        public int Calls;

        public Task<JsonNode?> Hover(LspRequestContext context, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            return Task.FromResult<JsonNode?>(new JsonObject { ["contents"] = "hover for " + LspJson.DocumentUri(context.Params) });
        }
    }

    private sealed class FakeCompletion : ICompletionProvider
    {
        public IReadOnlyList<string> TriggerCharacters { get; } = [".", "#"];

        public Task<JsonNode?> Complete(LspRequestContext context, CancellationToken cancellationToken) =>
            Task.FromResult<JsonNode?>(new JsonObject { ["isIncomplete"] = false, ["items"] = new JsonArray() });
    }

    private sealed class FakeCommands : IExecuteCommandHandler
    {
        public readonly List<string> Ran = [];

        public IReadOnlyList<string> Commands { get; } = ["kfs.test", "kfs.other"];

        public Task<JsonNode?> Execute(LspRequestContext context, string command, JsonArray? arguments, CancellationToken cancellationToken)
        {
            lock (Ran) Ran.Add(command + " " + (arguments?.ToJsonString() ?? "[]"));
            return Task.FromResult<JsonNode?>(JsonValue.Create("ran " + command));
        }
    }

    [Test]
    public void Capabilities_Reflect_Registered_Providers()
    {
        using var ws = new TempWorkspace();
        var hover = new FakeHover();
        var commands = new FakeCommands();
        using var h = ServerHarness.Start(Host(ws), configure: s => s.Features.Add(hover).Add(new FakeCompletion()).Add(commands));

        JsonObject caps = h.Client.Initialize()["capabilities"]!.AsObject();
        Assert.That(caps["hoverProvider"]?.GetValue<bool>(), Is.True);
        Assert.That(caps["completionProvider"]!["triggerCharacters"]!.AsArray().Select(n => n!.GetValue<string>()), Is.EqualTo(new[] { ".", "#" }));
        Assert.That(caps["executeCommandProvider"]!["commands"]!.AsArray().Select(n => n!.GetValue<string>()), Is.EqualTo(new[] { "kfs.other", "kfs.test" }));
        Assert.That(caps.ContainsKey("documentSymbolProvider") || caps.ContainsKey("codeLensProvider") || caps.ContainsKey("inlayHintProvider"), Is.False);

        Assert.That(h.Client.Call("textDocument/hover", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = "file:///x.c" } })!["contents"]!.GetValue<string>(),
            Is.EqualTo("hover for file:///x.c"));
        Assert.That(hover.Calls, Is.EqualTo(1));
        Assert.That(h.Client.Call("workspace/executeCommand", new JsonObject { ["command"] = "kfs.test", ["arguments"] = new JsonArray(1, "a") })!.GetValue<string>(),
            Is.EqualTo("ran kfs.test"));
        lock (commands.Ran) Assert.That(commands.Ran, Is.EqualTo(new[] { "kfs.test [1,\"a\"]" }));
        Assert.That(h.Client.Request("workspace/executeCommand", new JsonObject { ["command"] = "kfs.nope" })["error"]!["code"]!.GetValue<int>(), Is.EqualTo(JsonRpcErrorCodes.InvalidParams));
        Assert.That(h.Client.Request("textDocument/definition", new JsonObject())["error"]!["code"]!.GetValue<int>(), Is.EqualTo(JsonRpcErrorCodes.MethodNotFound));

        // Unknown notifications are ignored; the server still answers.
        h.Client.Notify("$/setTrace", new JsonObject { ["value"] = "off" });
        h.Client.Notify("purrtty/unknown");
        h.Client.Sync();

        Assert.Throws<InvalidOperationException>(() => h.Server.Features.Add(new FakeHover()), "the set is frozen at initialize");
        Assert.Throws<ArgumentException>(() => new LspFeatures().AddRequest("initialize", (_, _) => Task.FromResult<JsonNode?>(null)));
    }

    [Test]
    public void Shutdown_Then_Requests_Are_Refused_And_Exit_Closes()
    {
        using var ws = new TempWorkspace();
        using var h = ServerHarness.Start(Host(ws));
        h.Client.Initialize();
        Assert.That(h.Client.Call("shutdown"), Is.Null);
        Assert.That(h.Client.Request("textDocument/hover", new JsonObject())["error"]!["code"]!.GetValue<int>(), Is.EqualTo(JsonRpcErrorCodes.InvalidRequest));
        h.Client.Notify("exit");

        Assert.That(h.Client.WaitForEnd(), Is.True);
        Assert.That(h.WaitForServerEnd(), Is.True);
        Assert.That(h.Server.ExitCode, Is.EqualTo(0));
        Assert.That(h.Server.State, Is.EqualTo(LspServerState.Closed));
    }

    [Test]
    public void Server_Survives_Invalid_Json()
    {
        using var ws = new TempWorkspace();
        using var h = ServerHarness.Start(Host(ws));
        h.Client.SendRaw("Content-Length: 9\r\n\r\n{\"id\":1,]");
        JsonObject parseError = h.Client.WaitFor(m => m["error"] is not null, "the parse error");
        Assert.That(parseError["error"]!["code"]!.GetValue<int>(), Is.EqualTo(JsonRpcErrorCodes.ParseError));
        Assert.That(h.Client.Initialize()["capabilities"], Is.Not.Null, "the connection carries on");
    }

    [Test]
    public void Exit_Closes_Connection_Only()
    {
        using var ws = new TempWorkspace().Write("ok.c", Programs.Clean);
        int baseline = LspThreads.Alive;
        var listener = new KfsLspListener(Host(ws, token: "t"), IPAddress.Loopback, 0, name => new KfsLanguageServerOptions { Name = name, Log = LspLog.None }, log: LspLog.None);
        listener.Start();
        try
        {
            using LspTestClient a = Connect(listener.Port);
            using LspTestClient b = Connect(listener.Port);
            a.Initialize("t");
            b.Initialize("t");
            Assert.That(WaitForClients(listener, 2), Is.True);

            a.ShutdownAndExit();
            Assert.That(a.WaitForEnd(), Is.True, "exit closes the connection");
            Assert.That(WaitForClients(listener, 1), Is.True, "and only that one");

            // The other client still works: it opens a file and gets its diagnostics.
            string uri = new purrTTY.Kfs.Lsp.Documents.WorkspaceUris(ws.Root).ToUri("ok.c");
            b.Open(uri, Programs.Clean);
            JsonObject published = b.WaitForDiagnostics(uri);
            Assert.That(published["diagnostics"]!.AsArray(), Is.Empty);

            // A third client can still connect.
            using LspTestClient c = Connect(listener.Port);
            c.Initialize("t");
        }
        finally
        {
            Assert.That(listener.Stop(), Is.True);
        }
        Assert.That(LspThreads.WaitUntilAtMost(baseline, LspTestClient.Timeout), Is.True, $"threads left: {LspThreads.Alive - baseline}");
    }

    [Test]
    public void Listener_Stop_Leaves_No_Thread()
    {
        using var ws = new TempWorkspace().Write("bad.c", Programs.Undeclared);
        int baseline = LspThreads.Alive;
        var listener = new KfsLspListener(Host(ws), IPAddress.Loopback, 0, name => new KfsLanguageServerOptions { Name = name, Log = LspLog.None }, log: LspLog.None);
        listener.Start();
        int port = listener.Port;
        Assert.That(port, Is.GreaterThan(0));

        using LspTestClient a = Connect(port);
        using LspTestClient b = Connect(port);
        string uri = new purrTTY.Kfs.Lsp.Documents.WorkspaceUris(ws.Root).ToUri("bad.c");
        foreach (LspTestClient client in new[] { a, b })
        {
            client.Initialize();
            client.Open(uri, Programs.Undeclared);
            Assert.That(client.WaitForDiagnostics(uri, p => p["diagnostics"]!.AsArray().Count > 0)["diagnostics"]![0]!["code"]!.GetValue<string>(), Is.EqualTo("KC-E003"));
        }
        Assert.That(LspThreads.Alive, Is.EqualTo(baseline + 1 + 2 * 3), "accept + per client: reader, writer, compile (nothing else)");

        var clock = System.Diagnostics.Stopwatch.StartNew();
        Assert.That(listener.Stop(), Is.True, "every thread joined");
        Assert.That(clock.Elapsed, Is.LessThan(TimeSpan.FromSeconds(2)), "cooperative clients: no wait near the 10 s limit");
        Assert.That(LspThreads.Alive, Is.EqualTo(baseline), "exactly back at the baseline when Stop returns, not eventually");
        Assert.That(a.WaitForEnd(), Is.True, "clients are closed");
        Assert.That(b.WaitForEnd(), Is.True);
        Assert.That(listener.Clients, Is.Empty);
        Assert.Throws<SocketException>(() => { using var late = new TcpClient(); late.Connect(IPAddress.Loopback, port); });
        Assert.Throws<ArgumentException>(() => _ = new KfsLspListener(Host(ws), IPAddress.Parse("192.0.2.1")), "loopback only");
    }

    internal static LspTestClient Connect(int port)
    {
        var tcp = new TcpClient { NoDelay = true };
        tcp.Connect(IPAddress.Loopback, port);
        NetworkStream stream = tcp.GetStream();
        return new LspTestClient(stream, stream);
    }

    private static bool WaitForClients(KfsLspListener listener, int count)
    {
        var deadline = DateTime.UtcNow + LspTestClient.Timeout;
        while (listener.Clients.Count != count)
        {
            if (DateTime.UtcNow > deadline) return false;
            Thread.Sleep(10); // polling interval inside a deadline-bounded wait (no event for "client thread ended")
        }
        return true;
    }
}

/// <summary>Small programs the LSP tests share.</summary>
internal static class Programs
{
    public const string Clean = """
        #include <kfs.h>
        #pragma kfs tick_hz 10
        #pragma kfs task run rate_hz=10
        uint32_t n;
        void run(void) { n = n + 1u; }

        """;

    /// <summary>KC-E003 at line 5, column 22 ('missing').</summary>
    public const string Undeclared = """
        #include <kfs.h>
        #pragma kfs tick_hz 10
        #pragma kfs task run rate_hz=10
        uint32_t n;
        void run(void) { n = missing + 1u; }

        """;
}
