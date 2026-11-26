using System.Net;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using NUnit.Framework;
using purrTTY.Kfs.Lsp;
using purrTTY.Kfs.Lsp.Features;
using purrTTY.Kfs.Lsp.InGame;
using purrTTY.Kfs.Lsp.Protocol;
using purrTTY.Kfs.Tools.Tests.Lsp;

namespace purrTTY.Kfs.Tools.Tests.Runtime.Lsp;

/// <summary>
/// <see cref="LspServiceCore"/> over real loopback sockets with temporary folders (never the
/// player's config folder): the token, the status line, the server info file, a port in use,
/// restart, thread accounting; and the in-game configuration end to end (token refusal, commands
/// through the main-thread queue, two clients).
/// </summary>
[TestFixture]
public sealed class ServiceCoreTests : ToolsRuntimeFixture
{
    private sealed class Log
    {
        private readonly List<string> _lines = [];
        public LspLogSink Sink => (level, message) => { lock (_lines) _lines.Add($"{level}: {message}"); };
        public List<string> Lines { get { lock (_lines) return _lines.ToList(); } }
    }

    /// <summary>An LSP client on a TCP connection.</summary>
    private sealed class TcpLsp : IDisposable
    {
        private readonly TcpClient _tcp;

        public TcpLsp(int port)
        {
            _tcp = new TcpClient();
            _tcp.Connect(IPAddress.Loopback, port);
            NetworkStream stream = _tcp.GetStream();
            Client = new LspTestClient(stream, stream);
        }

        public LspTestClient Client { get; }

        public void Dispose()
        {
            Client.Dispose();
            _tcp.Dispose();
        }
    }

    /// <summary>The game's main thread, simulated: calls <see cref="InGameLanguageHost.Frame"/> until disposed.</summary>
    private sealed class FramePump : IDisposable
    {
        private readonly Thread _thread;
        private volatile bool _stop;

        public FramePump(InGameLanguageHost host)
        {
            _thread = new Thread(() =>
            {
                while (!_stop)
                {
                    host.Frame();
                    Thread.Sleep(1);   // frame pacing of the simulated main thread, not a wait for a result
                }
            }) { IsBackground = true, Name = "simulated main thread" };
            _thread.Start();
        }

        public void Dispose()
        {
            _stop = true;
            _thread.Join(LspTestClient.Timeout);
        }
    }

    private static string InfoPath(TempWorkspace config) => Path.Combine(config.Root, ".purrTTY", LspServerInfoFile.FileName);

    private static LspServiceCore NewCore(IKfsLanguageHost host, TempWorkspace config, Log log, int port = 0, Action<KfsLanguageServer>? configure = null) =>
        new(host, new LspServiceOptions
        {
            Port = port,
            InfoFilePath = InfoPath(config),
            KsaBuild = "v2026.11.25.5949-test",
            Log = log.Sink,
            Configure = configure,
            ServerOptions = name => new KfsLanguageServerOptions { Name = name, Log = LspLog.None },
        });

    [Test]
    public void Token_Status_And_Info_File_Lifecycle()
    {
        using var ws = new TempWorkspace();
        using var config = new TempWorkspace();
        var log = new Log();
        string path = InfoPath(config);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, """{ "address": "127.0.0.1:1", "token": "stale", "pid": 999999, "ksa_build": "old", "workspace_root": "/gone", "extra": 1 }""");

        var game = new InGameHostTests.FakeGameSide { Root = ws.Root };
        var host = new InGameLanguageHost(Runtime.Tables, game, new InGameHostOptions { Log = LspLog.None });
        using LspServiceCore core = NewCore(host, config, log);
        host.WorkspaceChanged += _ => core.WorkspaceChanged();

        Assert.That(core.Start(), Is.True, core.Status);
        string first = core.Token!;
        Assert.That(first, Does.Match("^[A-Za-z0-9_-]{43}$"), "256 bits, base64url");
        Assert.That(core.Address, Does.Match(@"^127\.0\.0\.1:\d+$"));
        Assert.That(core.Endpoint, Is.EqualTo(new purrTTY.Kfs.Runtime.KfsLspEndpoint(core.Address!, first)));

        LspServerInfo info = LspServerInfoFile.Read(path)!;
        Assert.That(info, Is.EqualTo(new LspServerInfo(core.Address!, first, Environment.ProcessId, "v2026.11.25.5949-test", host.WorkspaceRoot)),
            "the stale file of a dead process was replaced");
        Assert.That(JsonNode.Parse(File.ReadAllText(path))!.AsObject().Select(p => p.Key),
            Is.EqualTo(new[] { "address", "token", "pid", "ksa_build", "workspace_root" }));
        if (!OperatingSystem.IsWindows())
            Assert.That(File.GetUnixFileMode(path), Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite), "owner-only");
        Assert.That(Directory.EnumerateFileSystemEntries(Path.GetDirectoryName(path)!).Select(Path.GetFileName), Is.EqualTo(new[] { LspServerInfoFile.FileName }),
            "no temporary file left");

        Assert.That(core.Status, Does.StartWith($"listening on {core.Address}, workspace {host.WorkspaceRoot}, server info {path}"));
        IReadOnlyList<string> purrLsp = LspServiceText.Describe(core, host, allowVerb: false, feed: null, offReason: null);
        Assert.That(purrLsp, Is.EqualTo(new[]
        {
            $"purr lsp: listening on {core.Address}, 0 clients connected",
            $"  workspace:  {host.WorkspaceRoot}",
            "  target:     bench (Bench, layout 0x" + host.CurrentTarget.Layout.Fingerprint.ToString("X8") + ")",
            "  commands:   kfs.build kfs.load kfs.start kfs.stop kfs.reset kfs.target kfs.manual kfs.parts; kfs.verb off (lsp_allow_vehicle_commands = false in kfs.toml)",
            $"  info file:  {path}",
            "  token:      purr lsp token prints it (it changes at every start)",
        }), "purr lsp never prints the token");
        Assert.That(LspServiceText.MenuLine(core, null), Is.EqualTo($"Language server: {core.Address}, 0 clients"));
        Assert.That(LspServiceText.MenuLine(null, "off (lsp_enabled = false in kfs.toml)"), Is.EqualTo("Language server: off (lsp_enabled = false in kfs.toml)"));
        Assert.That(log.Lines.Count(l => l.Contains("listening on", StringComparison.Ordinal)), Is.EqualTo(1), string.Join("\n", log.Lines));

        // The workspace moves (kfs ws): the file is rewritten.
        using var moved = new TempWorkspace();
        game.Root = moved.Root;
        host.Frame();
        Assert.That(LspServerInfoFile.Read(path)!.WorkspaceRoot, Is.EqualTo(Path.GetFullPath(moved.Root)));

        Assert.That(core.Stop(), Is.True);
        Assert.That(File.Exists(path), Is.False, "deleted at stop");
        Assert.That((core.IsRunning, core.Token, core.Address, core.Endpoint), Is.EqualTo((false, (string?)null, (string?)null, (purrTTY.Kfs.Runtime.KfsLspEndpoint?)null)));

        Assert.That(core.Restart(), Is.True);
        Assert.That(core.Token, Is.Not.EqualTo(first), "a new token at every start");
        string second = core.Token!;

        // Another server took the file over: our stop leaves it.
        LspServerInfoFile.Write(path, new LspServerInfo("127.0.0.1:2", "theirs", 1, "x", moved.Root + "-other"));
        core.Stop();
        Assert.That(LspServerInfoFile.Read(path)!.Token, Is.EqualTo("theirs"));

        foreach (string token in new[] { first, second })
            Assert.That(log.Lines.Where(l => l.Contains(token, StringComparison.Ordinal)), Is.Empty, "the token is never logged");
    }

    [Test]
    public void The_Info_File_Is_Never_Written_Inside_The_Workspace()
    {
        using var ws = new TempWorkspace();
        var log = new Log();
        var host = KfsLanguageHost.ForBench(Runtime.Tables, ws.Root);
        using var core = new LspServiceCore(host, new LspServiceOptions
        {
            InfoFilePath = Path.Combine(ws.Root, ".purrTTY", LspServerInfoFile.FileName),
            Log = log.Sink,
            ServerOptions = name => new KfsLanguageServerOptions { Name = name, Log = LspLog.None },
        });
        Assert.That(core.Start(), Is.True, "the server still runs");
        Assert.That(core.InfoFile.Problem, Does.Contain("inside the kFS workspace"));
        Assert.That(core.Status, Does.Contain("no server info file"));
        Assert.That(ws.Snapshot(), Is.Empty, "nothing written in the workspace");
        core.Stop();
    }

    [Test]
    public void A_Port_In_Use_Fails_Cleanly()
    {
        using var ws = new TempWorkspace();
        using var config = new TempWorkspace();
        var taken = new TcpListener(IPAddress.Loopback, 0);
        taken.Start();
        try
        {
            int port = ((IPEndPoint)taken.LocalEndpoint).Port;
            int baseline = LspThreads.Alive;
            var log = new Log();
            using LspServiceCore core = NewCore(KfsLanguageHost.ForBench(Runtime.Tables, ws.Root), config, log, port);
            Assert.That(core.Start(), Is.False);
            Assert.That(core.Status, Is.EqualTo($"not running: cannot listen on 127.0.0.1:{port} (port {port} is in use); set lsp_port = 0 in kfs.toml to pick a free port"));
            Assert.That((core.IsRunning, core.Token), Is.EqualTo((false, (string?)null)));
            Assert.That(File.Exists(InfoPath(config)), Is.False);
            Assert.That(LspThreads.Alive, Is.EqualTo(baseline), "no thread was left");
            Assert.That(core.Stop(), Is.True, "stopping a service that never ran is fine");
        }
        finally
        {
            taken.Stop();
        }
    }

    [Test]
    [Repeat(5)]
    public void Stop_Joins_Every_Thread_It_Started()
    {
        using var ws = new TempWorkspace();
        using var config = new TempWorkspace();
        ws.Write("a.c", "void f(void) { }\n");
        int baseline = LspThreads.Alive;
        var log = new Log();
        using LspServiceCore core = NewCore(KfsLanguageHost.ForBench(Runtime.Tables, ws.Root), config, log, configure: s => HostFeatures.AddTo(s));
        Assert.That(core.Start(), Is.True, core.Status);
        int port = int.Parse(core.Address!.Split(':')[1]);
        using (var a = new TcpLsp(port))
        using (var b = new TcpLsp(port))
        {
            a.Client.Initialize(core.Token);
            b.Client.Initialize(core.Token);
            a.Client.Open(new purrTTY.Kfs.Lsp.Documents.WorkspaceUris(ws.Root).ToUri("a.c"), "void f(void) { }\n");
            a.Client.Sync();
            Assert.That(SpinWait.SpinUntil(() => core.Clients == 2, LspTestClient.Timeout), Is.True);
            Assert.That(LspThreads.Alive, Is.GreaterThan(baseline));
            var clock = System.Diagnostics.Stopwatch.StartNew();
            Assert.That(core.Stop(), Is.True, "every thread ended in time");
            Assert.That(clock.Elapsed, Is.LessThan(TimeSpan.FromSeconds(2)), "Stop runs on the game's main thread: it must not wait out a timeout");
            Assert.That(LspThreads.Alive, Is.EqualTo(baseline), "Stop returned after the last of its threads (accept, client, writer, compile)");
            Assert.That(a.Client.WaitForEnd() && b.Client.WaitForEnd(), Is.True, "the connections were closed");
        }
    }

    [Test]
    public void InGame_Configuration_Refuses_A_Wrong_Token_And_Serves_Two_Clients()
    {
        using var ws = new TempWorkspace();
        using var config = new TempWorkspace();
        int baseline = LspThreads.Alive;
        var game = new InGameHostTests.FakeGameSide { Root = ws.Root };
        var host = new InGameLanguageHost(Runtime.Tables, game, new InGameHostOptions { Log = LspLog.None });
        TextWriter slot = new StringWriter();
        using var feed = new LiveFeed(() => host.LiveSource, new LiveFeedOptions { Log = LspLog.None });
        OutputTeeInstallation tee = OutputTeeInstallation.Install(() => slot, w => slot = w, feed.WriteOutput);
        host.Tee = tee.Tee;
        host.Feed = feed;
        game.Console = tee.Tee;
        var features = new HostFeatureOptions { RunPurr = host.RunPurr, Feed = feed };
        var log = new Log();
        using LspServiceCore core = NewCore(host, config, log, configure: s =>
        {
            purrTTY.Kfs.Lsp.Features.StandardFeatures.AddTo(s);
            HostFeatures.AddTo(s, features);
        });
        Assert.That(core.Start(), Is.True, core.Status);
        int port = int.Parse(core.Address!.Split(':')[1]);
        using var pump = new FramePump(host);

        using (var intruder = new TcpLsp(port))
        {
            JsonObject refused = intruder.Client.Request("initialize", LspTestClient.InitializeParams("not-the-token", "utf-16"));
            Assert.That(refused["error"]?["code"]?.GetValue<int>(), Is.EqualTo(JsonRpcErrorCodes.RequestFailed));
            Assert.That(intruder.Client.WaitForEnd(), Is.True, "closed after the refusal");
        }
        using (var noToken = new TcpLsp(port))
        {
            Assert.That(noToken.Client.Request("initialize", LspTestClient.InitializeParams(null, "utf-16"))["error"], Is.Not.Null);
            Assert.That(noToken.Client.WaitForEnd(), Is.True);
        }

        var first = new TcpLsp(port);
        using var second = new TcpLsp(port);
        JsonObject caps = first.Client.Initialize(core.Token)["capabilities"]!.AsObject();
        Assert.That(caps["experimental"]!["purrtty"]!.ToJsonString(), Is.EqualTo("""{"subscribe":true,"vehicleCommands":true,"verb":false}"""));
        second.Client.Initialize(core.Token);
        second.Client.Notify(HostFeatures.SubscribeMethod, new JsonObject { ["topics"] = new JsonArray("output") });
        second.Client.Sync();

        JsonNode? start = first.Client.Call("workspace/executeCommand", new JsonObject { ["command"] = KfsCommands.Start });
        Assert.That(start!.ToJsonString(), Is.EqualTo("""{"ok":true,"output":"kfs start: done\n","error":null}"""));
        first.Client.ShutdownAndExit();
        Assert.That(first.Client.WaitForEnd(), Is.True, "exit closes that connection");
        first.Dispose();

        JsonNode? parts = second.Client.Call("workspace/executeCommand", new JsonObject { ["command"] = KfsCommands.Parts, ["arguments"] = new JsonArray("lights") });
        Assert.That(parts!["ok"]!.GetValue<bool>(), Is.True, "the second client keeps working");
        Assert.That(parts["output"]!.GetValue<string>(), Is.EqualTo("purr parts lights: done\n"));
        JsonObject output = second.Client.WaitFor(m => m["method"]?.GetValue<string>() == "purrtty/kfsOutput"
            && m["params"]!["text"]!.GetValue<string>().Contains("purr parts lights", StringComparison.Ordinal), "the output feed");
        Assert.That(output["params"]!["text"]!.GetValue<string>(), Does.Contain("purr parts lights: done\n"));
        lock (game.Ran) Assert.That(game.Ran.Select(r => r.Line), Is.EqualTo(new[] { "kfs start", "purr parts lights" }));

        host.Shutdown("test over");
        Assert.That(core.Stop(), Is.True);
        Assert.That(tee.Restore(), Is.EqualTo(OutputTeeRestore.Restored));
        pump.Dispose();
        Assert.That(LspThreads.WaitUntilAtMost(baseline, LspTestClient.Timeout), Is.True);
    }
}
