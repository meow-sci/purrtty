using System.Net;
using System.Net.Sockets;
using NUnit.Framework;
using purrTTY.Kfs.Lsp;
using purrTTY.Kfs.Lsp.Features;
using purrTTY.Kfs.Tools.Tests.Lsp;

namespace purrTTY.Kfs.Tools.Tests.Runtime.Lsp;

/// <summary>
/// The ends of a connection's life that the in-game service meets every day: a server disposed
/// before it ran (a listener stopping between accept and run, a configure callback that throws)
/// releases everything registered on the long-lived host and the shared live feed; the editor's
/// port probe (connect, close, no <c>initialize</c>) is logged at Debug only.
/// </summary>
[TestFixture]
public sealed class LifecycleTests : ToolsRuntimeFixture
{
    private sealed class Log
    {
        private readonly List<(LspLogLevel Level, string Message)> _lines = [];
        public LspLogSink Sink => (level, message) => { lock (_lines) _lines.Add((level, message)); };
        public List<(LspLogLevel Level, string Message)> Lines { get { lock (_lines) return _lines.ToList(); } }
        public string Dump() => string.Join("\n", Lines.Select(l => $"{l.Level}: {l.Message}"));
    }

    [Test]
    public void A_Server_Disposed_Before_It_Ran_Raises_Closed_And_Releases_Its_Handlers()
    {
        using var ws = new TempWorkspace();
        var host = KfsLanguageHost.ForBench(Runtime.Tables, ws.Root);
        using var feed = new LiveFeed(() => null, new LiveFeedOptions { UseTimer = false, Log = LspLog.None });
        int before = host.TargetChangedHandlers;

        var server = new KfsLanguageServer(host, new MemoryStream(), new MemoryStream(), new KfsLanguageServerOptions { Log = LspLog.None });
        StandardFeatures.AddTo(server);
        HostFeatures.AddTo(server, new HostFeatureOptions { Feed = feed });
        feed.Subscribe(server, LiveTopics.State | LiveTopics.Output);
        int closed = 0;
        server.Closed += _ => closed++;
        Assert.That(host.TargetChangedHandlers, Is.EqualTo(before + 2), "the server's own re-diagnose and the index warm-up");
        Assert.That((feed.Subscribers, feed.IsPolling), Is.EqualTo((1, true)));

        server.Dispose();
        Assert.That(closed, Is.EqualTo(1), "Closed is raised for a server that never ran");
        Assert.That(server.State, Is.EqualTo(LspServerState.Closed));
        Assert.That(host.TargetChangedHandlers, Is.EqualTo(before), "nothing left on the host");
        Assert.That((feed.Subscribers, feed.IsPolling), Is.EqualTo((0, false)), "nothing left in the feed (no timer for nobody)");

        server.Dispose();
        server.Run();   // a disposed server returns at once
        Assert.That(closed, Is.EqualTo(1), "once");
    }

    [Test]
    public void A_Configure_Callback_That_Throws_Leaves_Nothing_On_The_Host()
    {
        using var ws = new TempWorkspace();
        var host = KfsLanguageHost.ForBench(Runtime.Tables, ws.Root, "t");
        int before = host.TargetChangedHandlers;
        var log = new Log();
        using var listener = new KfsLspListener(host, IPAddress.Loopback, 0, n => new KfsLanguageServerOptions { Name = n, Log = LspLog.None },
            configure: server =>
            {
                StandardFeatures.AddTo(server);
                throw new InvalidOperationException("configure broke");
            }, log: log.Sink);
        listener.Start();

        using (var tcp = new TcpClient())
        {
            tcp.Connect(IPAddress.Loopback, listener.Port);
            Assert.That(Eventually(() => log.Lines.Any(l => l.Message.Contains("could not start a server", StringComparison.Ordinal))), Is.True, log.Dump);
            // The listener closed the socket: the read ends.
            tcp.ReceiveTimeout = (int)LspTestClient.Timeout.TotalMilliseconds;
            Assert.That(tcp.GetStream().Read(new byte[1], 0, 1), Is.Zero, "the connection was closed");
        }
        Assert.That(host.TargetChangedHandlers, Is.EqualTo(before), "the half-made server was disposed");
    }

    [Test]
    public void A_Port_Probe_Is_Logged_At_Debug_A_Client_At_Info()
    {
        using var ws = new TempWorkspace();
        var host = KfsLanguageHost.ForBench(Runtime.Tables, ws.Root, "tok");
        var log = new Log();
        using var listener = new KfsLspListener(host, IPAddress.Loopback, 0, n => new KfsLanguageServerOptions { Name = n, Log = log.Sink },
            configure: StandardFeatures.AddTo, log: log.Sink);
        listener.Start();

        // The Neovim client's probe: connect and close at once.
        using (var probe = new TcpClient())
        {
            probe.Connect(IPAddress.Loopback, listener.Port);
        }
        Assert.That(Eventually(() => log.Lines.Any(l => l.Message.StartsWith("tcp#1 ", StringComparison.Ordinal) && l.Message.Contains(": closed (", StringComparison.Ordinal))),
            Is.True, log.Dump);
        Assert.That(log.Lines.Where(l => l.Message.StartsWith("tcp#1 ", StringComparison.Ordinal)).Select(l => l.Level), Is.All.EqualTo(LspLogLevel.Debug), log.Dump);

        // A real client: "initialized" and "closed" at Info.
        using (var tcp = new TcpClient())
        {
            tcp.Connect(IPAddress.Loopback, listener.Port);
            NetworkStream stream = tcp.GetStream();
            using var client = new LspTestClient(stream, stream);
            client.Initialize("tok");
            client.ShutdownAndExit();
            Assert.That(client.WaitForEnd(), Is.True);
        }
        Assert.That(Eventually(() => log.Lines.Any(l => l.Message.StartsWith("tcp#2 ", StringComparison.Ordinal) && l.Message.Contains(": closed (", StringComparison.Ordinal))),
            Is.True, log.Dump);
        var info = log.Lines.Where(l => l.Level == LspLogLevel.Info && l.Message.StartsWith("tcp#2 ", StringComparison.Ordinal)).Select(l => l.Message).ToList();
        Assert.That(info.Any(m => m.Contains(": initialized (", StringComparison.Ordinal)), Is.True, log.Dump);
        Assert.That(info.Any(m => m.Contains(": closed (exit)", StringComparison.Ordinal)), Is.True, log.Dump);
        Assert.That(info.Any(m => m.EndsWith(": connected", StringComparison.Ordinal)), Is.False, "connected is Debug for everyone");
    }

    // Deadline-bounded polling (no event exists for a log line written on the listener's threads).
    private static bool Eventually(Func<bool> condition) => purrTTY.Kfs.Tools.Tests.Nvim.FakeKfsServer.WaitUntil(condition, LspTestClient.Timeout);
}
