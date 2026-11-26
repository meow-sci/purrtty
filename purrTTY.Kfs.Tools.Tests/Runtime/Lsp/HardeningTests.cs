using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using NUnit.Framework;
using purrTTY.Kfs.Lsp;
using purrTTY.Kfs.Lsp.Build;
using purrTTY.Kfs.Lsp.Documents;
using purrTTY.Kfs.Lsp.Features;
using purrTTY.Kfs.Lsp.InGame;
using purrTTY.Kfs.Lsp.Protocol;
using purrTTY.Kfs.Runtime;
using purrTTY.Kfs.Tools.Tests.Lsp;

namespace purrTTY.Kfs.Tools.Tests.Runtime.Lsp;

/// <summary>
/// The review's findings against the language server as the game runs it, over real loopback
/// sockets: peers that reset (A1), idle and oversized unauthenticated peers (A2), peers that never
/// read (A3), an HTTP request (B5), a persistent accept error (C2), the process-wide retired-thread
/// cap (C1), late diagnostics after didClose (B1), a stop with a compile in flight (B2), the
/// main thread's per-frame share (B3), publishing under the client's URIs (D2), the info file (D4)
/// and a configure callback that throws (D6). Every test that starts threads ends with
/// <see cref="LspThreads"/> exactly back at its baseline.
/// </summary>
[TestFixture]
public sealed class HardeningTests : ToolsRuntimeFixture
{
    private const string Token = "hardening-token";

    private static KfsLanguageHost Host(TempWorkspace ws, string? token = Token) => KfsLanguageHost.ForBench(Runtime.Tables, ws.Root, token);

    private static KfsLspListener Listen(IKfsLanguageHost host, KfsLspListenerLimits? limits = null, Action<KfsLanguageServer>? configure = null,
        LspLogSink? log = null, Func<string, KfsLanguageServerOptions>? options = null)
    {
        var listener = new KfsLspListener(host, IPAddress.Loopback, 0,
            options ?? (name => new KfsLanguageServerOptions { Name = name, Log = LspLog.None }), configure, log ?? LspLog.None, limits);
        listener.Start();
        return listener;
    }

    private static TcpClient Raw(int port)
    {
        var tcp = new TcpClient();
        tcp.Connect(IPAddress.Loopback, port);
        tcp.ReceiveTimeout = (int)LspTestClient.Timeout.TotalMilliseconds;
        return tcp;
    }

    /// <summary>True when the server closed <paramref name="tcp"/> (end of stream or reset) without sending anything first.</summary>
    private static bool ClosedSilently(TcpClient tcp, out int received)
    {
        received = 0;
        var buffer = new byte[4096];
        try
        {
            while (true)
            {
                int n = tcp.GetStream().Read(buffer, 0, buffer.Length);
                if (n <= 0) return received == 0;
                received += n;
            }
        }
        catch (IOException e) when (e.InnerException is SocketException { SocketErrorCode: not SocketError.TimedOut })
        {
            return received == 0;   // a reset is a close too
        }
    }

    private static bool Wait(Func<bool> condition) => SpinWait.SpinUntil(condition, LspTestClient.Timeout);

    private static string Uri(TempWorkspace ws, string path) => new WorkspaceUris(ws.Root).ToUri(path);

    // ------------------------------------------------------------------ A1: resets never kill the process

    [Test]
    public void Connections_Reset_At_Once_Never_Stop_The_Listener()
    {
        using var ws = new TempWorkspace().Write("bad.c", Programs.Undeclared);
        int baseline = LspThreads.Alive;
        KfsLspListener listener = Listen(Host(ws));
        try
        {
            using LspTestClient good = ServerProtocolTests.Connect(listener.Port);
            good.Initialize(Token);
            string uri = Uri(ws, "bad.c");
            for (int i = 0; i < 300; i++)
            {
                // What killed purr-kfs lsp --listen: connect, then close with SO_LINGER 0 (a RST), so the
                // accept thread's setsockopt meets a socket the peer already reset.
                var s = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                s.Connect(IPAddress.Loopback, listener.Port);
                s.LingerState = new LingerOption(true, 0);
                s.Close();
                if (i % 100 == 50)
                {
                    int mark = good.Mark;
                    good.Change(uri, i, Programs.Undeclared + $"/* {i} */\n");
                    if (i == 50) good.Open(uri, Programs.Undeclared);
                    good.Sync();
                    Assert.That(good.Mark, Is.GreaterThan(mark), "the well-behaved client is served throughout");
                }
            }
            JsonObject published = good.WaitForDiagnostics(uri, p => p["diagnostics"]!.AsArray().Count > 0);
            Assert.That(published["diagnostics"]![0]!["code"]!.GetValue<string>(), Is.EqualTo("KC-E003"));
            using LspTestClient late = ServerProtocolTests.Connect(listener.Port);
            Assert.That(late.Initialize(Token)["capabilities"], Is.Not.Null, "and accepts new clients afterwards");
        }
        finally
        {
            Assert.That(listener.Stop(), Is.True);
        }
        Assert.That(LspThreads.Alive, Is.EqualTo(baseline), "every thread joined by Stop: exactly the baseline");
    }

    [Test]
    public void A_Thread_That_Cannot_Start_Costs_That_Connection_Only()
    {
        using var ws = new TempWorkspace();
        int baseline = LspThreads.Alive;
        var lines = new List<string>();
        LspLogSink previous = LspThreads.Log;
        LspThreads.Log = (level, message) => { lock (lines) lines.Add(message); };
        KfsLspListener listener = Listen(Host(ws));
        try
        {
            LspThreads.FailNextStarts(1);   // the next connection's client thread
            using (TcpClient refused = Raw(listener.Port))
                Assert.That(ClosedSilently(refused, out _), Is.True, "that connection is closed");
            LspThreads.FailNextStarts(0);
            using LspTestClient ok = ServerProtocolTests.Connect(listener.Port);
            Assert.That(ok.Initialize(Token)["capabilities"], Is.Not.Null, "the listener carries on");
            lock (lines) Assert.That(lines, Has.Some.Contains("could not start the thread 'purrTTY kFS LSP tcp#"));
        }
        finally
        {
            LspThreads.FailNextStarts(0);
            LspThreads.Log = previous;
            Assert.That(listener.Stop(), Is.True);
        }
        Assert.That(LspThreads.Alive, Is.EqualTo(baseline));
    }

    [Test]
    public void The_Compile_Thread_And_The_Timers_Survive_What_They_Did_Not_Expect()
    {
        using var ws = new TempWorkspace();
        var lines = new List<string>();
        LspLogSink log = (level, message) => { lock (lines) lines.Add($"{level}: {message}"); };
        int faults = 1;
        ProjectCompileOutcome Compile(ProjectCompileRequest request) =>
            request.Project.FileMap.Values.Any(t => t.Contains("OOM", StringComparison.Ordinal)) && Interlocked.Exchange(ref faults, 0) == 1
                ? throw new OutOfMemoryException("injected: escapes the compile guard")
                : BuildScheduler.DefaultCompile(request);
        bool timerFault = true;
        using var h = ServerHarness.Start(Host(ws, token: null), new BuildSchedulerOptions
        {
            Debounce = TimeSpan.FromMilliseconds(1),
            Compile = Compile,
            TimerFault = what => { if (what == "debounce" && Volatile.Read(ref timerFault)) { Volatile.Write(ref timerFault, false); throw new InvalidOperationException("injected timer fault"); } },
        }, log: log);
        h.Client.Initialize();
        string uri = Uri(ws, "t.c");

        // The first debounce throws (guarded, logged); the build waits for the next edit.
        h.Client.Open(uri, "/* OOM */\n" + Programs.Undeclared);
        h.Client.Sync();
        Assert.That(Wait(() => { lock (lines) return lines.Any(l => l.Contains("the debounce timer failed: InvalidOperationException: injected timer fault")); }), Is.True);

        // The next one builds, the compile throws past its guard: the thread logs it and carries on.
        h.Client.Change(uri, 2, "/* OOM */\n" + Programs.Undeclared);
        Assert.That(Wait(() => { lock (lines) return lines.Any(l => l.Contains("the compile thread hit OutOfMemoryException")); }), Is.True);
        int mark = h.Client.Mark;
        h.Client.Change(uri, 3, Programs.Undeclared);
        JsonObject published = h.Client.WaitForDiagnostics(uri, p => p["version"]?.GetValue<int>() == 3, mark);
        Assert.That(published["diagnostics"]![0]!["code"]!.GetValue<string>(), Is.EqualTo("KC-E003"), "the same compile thread builds the next edit");
        Assert.That(h.Builds.ThreadsStarted, Is.EqualTo(1));
    }

    // ------------------------------------------------------------------ A2: limits before authentication

    [Test]
    public void Unauthenticated_Connections_Are_Capped_Timed_Out_And_Cost_No_Compile_Thread()
    {
        using var ws = new TempWorkspace();
        int baseline = LspThreads.Alive;
        var limits = new KfsLspListenerLimits { InitializeTimeout = TimeSpan.FromMilliseconds(400) };
        KfsLspListener listener = Listen(Host(ws), limits);
        try
        {
            var idle = new List<TcpClient> { Raw(listener.Port), Raw(listener.Port) };
            Assert.That(Wait(() => listener.Clients.Count == 2), Is.True);
            // Accept + per idle connection a reader and a writer: no compile thread before a build.
            Assert.That(LspThreads.Alive - baseline, Is.LessThanOrEqualTo(1 + 2 * 2));
            using (TcpClient third = Raw(listener.Port))
                Assert.That(ClosedSilently(third, out _), Is.True, "a third unauthenticated connection is closed at accept");
            Assert.That(listener.Refused, Is.EqualTo(1));

            var clock = Stopwatch.StartNew();
            foreach (TcpClient c in idle) Assert.That(ClosedSilently(c, out _), Is.True, "no initialize in time: closed");
            Assert.That(clock.Elapsed, Is.LessThan(TimeSpan.FromSeconds(10)), "by the initialize deadline, not by a client timeout");
            foreach (TcpClient c in idle) c.Dispose();

            // An authenticated client is no longer counted against the unauthenticated limit.
            using LspTestClient a = ServerProtocolTests.Connect(listener.Port);
            a.Initialize(Token);
            using LspTestClient b = ServerProtocolTests.Connect(listener.Port);
            b.Initialize(Token);
            using LspTestClient c3 = ServerProtocolTests.Connect(listener.Port);
            c3.Initialize(Token);
            a.Sync();
            Assert.That(listener.Clients.Count(s => s.IsAuthenticated), Is.EqualTo(3));
        }
        finally
        {
            Assert.That(listener.Stop(), Is.True);
        }
        Assert.That(LspThreads.Alive, Is.EqualTo(baseline));
    }

    [Test]
    public void At_Most_Eight_Connections()
    {
        using var ws = new TempWorkspace();
        int baseline = LspThreads.Alive;
        KfsLspListener listener = Listen(Host(ws));
        var clients = new List<LspTestClient>();
        try
        {
            for (int i = 0; i < 8; i++)
            {
                LspTestClient c = ServerProtocolTests.Connect(listener.Port);
                clients.Add(c);
                c.Initialize(Token);
            }
            using (TcpClient ninth = Raw(listener.Port))
                Assert.That(ClosedSilently(ninth, out _), Is.True, "the ninth is closed at accept");
            clients[0].ShutdownAndExit();
            Assert.That(clients[0].WaitForEnd(), Is.True);
            Assert.That(Wait(() => listener.Clients.Count == 7), Is.True);
            using LspTestClient again = ServerProtocolTests.Connect(listener.Port);
            Assert.That(again.Initialize(Token)["capabilities"], Is.Not.Null, "room again once one left");
        }
        finally
        {
            Assert.That(listener.Stop(), Is.True);
            foreach (LspTestClient c in clients) c.Dispose();
        }
        Assert.That(LspThreads.Alive, Is.EqualTo(baseline));
    }

    [Test]
    public void An_Oversized_Or_Flooding_Unauthenticated_Peer_Costs_No_Memory()
    {
        using var ws = new TempWorkspace();
        int baseline = LspThreads.Alive;
        KfsLspListener listener = Listen(Host(ws));
        try
        {
            GC.Collect();
            long before = GC.GetTotalMemory(forceFullCollection: true);
            for (int i = 0; i < 20; i++)
            {
                // R3: 29 bytes declaring 64 MiB used to allocate it, per connection.
                using TcpClient tcp = Raw(listener.Port);
                tcp.GetStream().Write(Encoding.ASCII.GetBytes("Content-Length: 67108864\r\n\r\n{"));
                Assert.That(ClosedSilently(tcp, out _), Is.True, "a message over 64 KiB before initialize closes the connection unread");
            }
            long grown = GC.GetTotalMemory(forceFullCollection: true) - before;
            Assert.That(grown, Is.LessThan(32L * 1024 * 1024), $"managed heap grew by {grown} bytes");

            // R2: a flood of requests, never initialize, nothing read: one answer, then closed.
            using (TcpClient flood = Raw(listener.Port))
            {
                byte[] frame = LspTestClient.Frame(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = 1, ["method"] = "x" });
                byte[] chunk = Enumerable.Range(0, 2000).SelectMany(_ => frame).ToArray();
                try
                {
                    for (int i = 0; i < 50; i++) flood.GetStream().Write(chunk);
                }
                catch (IOException)
                {
                    // the server closed while we were still writing: what we want
                }
                Assert.That(Wait(() => listener.Clients.Count == 0), Is.True, "the flooding connection is gone");
            }
        }
        finally
        {
            Assert.That(listener.Stop(), Is.True);
        }
        Assert.That(LspThreads.Alive, Is.EqualTo(baseline));
    }

    [Test]
    public void An_Http_Request_Is_Closed_Without_An_Answer()
    {
        using var ws = new TempWorkspace();
        KfsLspListener listener = Listen(Host(ws));
        try
        {
            // What a web page in the player's browser can send to 127.0.0.1 (B5): it used to reach initialize.
            string body = LspTestClient.InitializeParams(null).ToJsonString();
            using TcpClient tcp = Raw(listener.Port);
            tcp.GetStream().Write(Encoding.ASCII.GetBytes($"POST /a:b HTTP/1.1\r\nHost: 127.0.0.1\r\nContent-Type: text/plain\r\nContent-Length: {body.Length}\r\n\r\n{body}"));
            Assert.That(ClosedSilently(tcp, out int received), Is.True, $"closed without a byte of answer (got {received})");
        }
        finally
        {
            Assert.That(listener.Stop(), Is.True);
        }
    }

    // ------------------------------------------------------------------ A3: a peer that never reads

    /// <summary>A client that initializes and subscribes to output, then never reads again (the editor is suspended).</summary>
    private static TcpClient SubscribedNonReader(int port)
    {
        var tcp = new TcpClient { ReceiveBufferSize = 4096 };   // before connect: a small window
        tcp.Connect(IPAddress.Loopback, port);
        NetworkStream stream = tcp.GetStream();
        stream.Write(LspTestClient.Frame(new JsonObject
        {
            ["jsonrpc"] = "2.0", ["id"] = 1, ["method"] = "initialize", ["params"] = LspTestClient.InitializeParams(Token),
        }));
        stream.Write(LspTestClient.Frame(new JsonObject
        {
            ["jsonrpc"] = "2.0", ["method"] = HostFeatures.SubscribeMethod, ["params"] = new JsonObject { ["topics"] = new JsonArray("output") },
        }));
        return tcp;
    }

    [Test]
    public void A_Peer_That_Stops_Reading_Is_Cut_Off_At_The_Queue_Bound_And_Unsubscribed()
    {
        using var ws = new TempWorkspace();
        int baseline = LspThreads.Alive;
        using var feed = new LiveFeed(() => null, new LiveFeedOptions { UseTimer = false, Log = LspLog.None });
        KfsLspListener listener = Listen(Host(ws), configure: s => HostFeatures.AddTo(s, new HostFeatureOptions { Feed = feed }),
            options: name => new KfsLanguageServerOptions { Name = name, Log = LspLog.None, MaxQueuedBytes = 1024 * 1024 });
        try
        {
            using TcpClient tcp = SubscribedNonReader(listener.Port);
            Assert.That(Wait(() => feed.Subscribers == 1), Is.True);
            KfsLanguageServer server = listener.Clients.Single();
            string line = new string('x', 200) + "\n";
            long peak = 0;
            var deadline = DateTime.UtcNow + LspTestClient.Timeout;
            int frames = 0;
            while (listener.Clients.Count > 0 && DateTime.UtcNow < deadline)
            {
                for (int i = 0; i < 50; i++) feed.WriteOutput(line);
                feed.FlushOutput();   // one purrtty/kfsOutput per frame, as InGameLanguageHost.Frame does
                peak = Math.Max(peak, server.QueuedBytes);
                frames++;
            }
            Assert.That(listener.Clients, Is.Empty, $"cut off after {frames} frames");
            Assert.That(server.CloseReason, Does.Contain("stopped reading"));
            Assert.That(peak, Is.LessThanOrEqualTo(1024 * 1024 + 16 * 1024), "the queue never grew past its bound");
            Assert.That(feed.Subscribers, Is.Zero, "its subscription went with it");
        }
        finally
        {
            Assert.That(listener.Stop(), Is.True);
        }
        Assert.That(LspThreads.Alive, Is.EqualTo(baseline));
    }

    [Test]
    public void A_Write_Blocked_Longer_Than_The_Send_Timeout_Closes_The_Connection()
    {
        using var ws = new TempWorkspace();
        int baseline = LspThreads.Alive;
        using var feed = new LiveFeed(() => null, new LiveFeedOptions { UseTimer = false, Log = LspLog.None });
        var limits = new KfsLspListenerLimits { SendTimeout = TimeSpan.FromMilliseconds(500) };
        KfsLspListener listener = Listen(Host(ws), limits, s => HostFeatures.AddTo(s, new HostFeatureOptions { Feed = feed }));
        try
        {
            using TcpClient tcp = SubscribedNonReader(listener.Port);
            Assert.That(Wait(() => feed.Subscribers == 1), Is.True);
            KfsLanguageServer server = listener.Clients.Single();
            // Enough to fill the socket buffers (far below the 16 MiB queue bound), then nothing more:
            // only the send timeout can end this connection.
            string text = new('y', 64 * 1024);
            for (int i = 0; i < 160; i++)   // 10 MiB
            {
                feed.WriteOutput(text);
                feed.FlushOutput();
            }
            var clock = Stopwatch.StartNew();
            Assert.That(Wait(() => listener.Clients.Count == 0), Is.True, "the blocked write timed out and closed the connection");
            Assert.That(server.CloseReason, Is.EqualTo("write failed"));
            Assert.That(clock.Elapsed, Is.LessThan(TimeSpan.FromSeconds(15)));
            Assert.That(feed.Subscribers, Is.Zero);
        }
        finally
        {
            Assert.That(listener.Stop(), Is.True);
        }
        Assert.That(LspThreads.Alive, Is.EqualTo(baseline));
    }

    // ------------------------------------------------------------------ C2: a persistent accept error

    [Test]
    public void A_Persistent_Accept_Error_Backs_Off_And_Is_Logged_Once()
    {
        using var ws = new TempWorkspace();
        var lines = new List<string>();
        LspLogSink log = (level, message) => { lock (lines) lines.Add($"{level}: {message}"); };
        int calls = 0;
        var limits = new KfsLspListenerLimits { AcceptBackoff = TimeSpan.FromMilliseconds(20), MaxAcceptBackoff = TimeSpan.FromMilliseconds(80) };
        var listener = new KfsLspListener(Host(ws), IPAddress.Loopback, 0, log: log, limits: limits)
        {
            AcceptOverride = _ =>
            {
                Interlocked.Increment(ref calls);
                throw new SocketException((int)SocketError.TooManyOpenSockets);   // EMFILE
            },
        };
        var clock = Stopwatch.StartNew();
        listener.Start();
        Assert.That(Wait(() => Volatile.Read(ref calls) >= 6), Is.True);
        TimeSpan elapsed = clock.Elapsed;
        // 20 + 40 + 80 + 80 + 80 ms between the first six attempts: a spin would make thousands.
        Assert.That(elapsed, Is.GreaterThanOrEqualTo(TimeSpan.FromMilliseconds(290)), $"{calls} accepts in {elapsed.TotalMilliseconds} ms");
        var stop = Stopwatch.StartNew();
        Assert.That(listener.Stop(), Is.True);
        Assert.That(stop.Elapsed, Is.LessThan(TimeSpan.FromSeconds(1)), "Stop interrupts the back-off");
        lock (lines)
        {
            Assert.That(lines.Count(l => l.StartsWith("Warning: accept failed (TooManyOpenSockets)", StringComparison.Ordinal)), Is.EqualTo(1), "logged once per error code");
        }
    }

    // ------------------------------------------------------------------ C1: the retired-thread cap is process-wide

    [Test]
    public void The_Retired_Thread_Cap_Holds_Across_Connections_While_The_Compiles_Never_Return()
    {
        using var ws = new TempWorkspace();
        var hang = new BlockingCompile(hangOn: "HANG");
        int baseline = LspThreads.Alive;
        Assert.That(LspThreads.Retired, Is.Zero);
        var options = new BuildSchedulerOptions
        {
            Debounce = Timeout.InfiniteTimeSpan,
            CompileTimeLimit = TimeSpan.FromMilliseconds(200),
            MaxRetiredThreads = 1,
            Compile = hang.Compile,
        };
        var first = ServerHarness.Start(Host(ws, null), options);
        var second = ServerHarness.Start(Host(ws, null), options);
        try
        {
            first.Client.Initialize();
            second.Client.Initialize();
            string uri = Uri(ws, "stuck.c");
            first.Client.Open(uri, "/* HANG */\n" + Programs.Clean);
            first.Flush();
            first.Client.WaitForDiagnostics(uri, p => p["diagnostics"]!.AsArray().Any(d => d!["message"]!.GetValue<string>().Contains("was abandoned", StringComparison.Ordinal)));
            Assert.That(LspThreads.Retired, Is.EqualTo(1));

            // The other connection's stuck compile finds the process at its cap: not replaced, refused.
            second.Client.Open(uri, "/* HANG */\n" + Programs.Clean);
            second.Flush();
            JsonObject refused = second.Client.WaitForDiagnostics(uri, p => p["diagnostics"]!.AsArray().Count > 0);
            Assert.That(refused["diagnostics"]![0]!["message"]!.GetValue<string>(), Does.Contain("no further compile starts until one of them ends"));
            Assert.That(second.Builds.Refusing, Is.True);
            Assert.That(second.Builds.ThreadsStarted, Is.EqualTo(1), "no fresh thread for the second connection");
            Assert.That(LspThreads.Retired, Is.EqualTo(1), "the cap held process-wide; neither compile has returned");
            Assert.That(purrTTY.Kfs.Tables.SystemHeaderReaders.Compiles, Is.EqualTo(2), "both compiles are counted as header readers");

            // A connection that goes away does not take its retired thread with it.
            first.Dispose();
            first = null!;
            Assert.That(LspThreads.Retired, Is.EqualTo(1));
        }
        finally
        {
            hang.Release();
            first?.Dispose();
            second.Dispose();
        }
        Assert.That(LspThreads.WaitUntilAtMost(baseline, LspTestClient.Timeout), Is.True, "every compile thread ended once released");
        Assert.That(LspThreads.Retired, Is.Zero);
        Assert.That(purrTTY.Kfs.Tables.SystemHeaderReaders.Compiles, Is.Zero);
    }

    // ------------------------------------------------------------------ B1: nothing published after didClose

    [Test]
    public void A_Build_That_Finishes_After_DidClose_Publishes_Nothing_And_A_Reopen_Builds()
    {
        using var ws = new TempWorkspace();
        var atPublish = new ManualResetEventSlim(false);
        var go = new ManualResetEventSlim(false);
        LspLogSink log = (level, message) =>
        {
            // WorkLoop logs "<name>: <result>" after it decided to publish, before it raises Built.
            if (level == LspLogLevel.Debug && message.Contains("folder:proj #", StringComparison.Ordinal) && !message.Contains("dropped", StringComparison.Ordinal)
                && !atPublish.IsSet)
            {
                atPublish.Set();
                go.Wait(LspTestClient.Timeout);
            }
        };
        using var h = ServerHarness.Start(Host(ws, null), log: log);
        h.Client.Initialize();
        string uri = Uri(ws, "proj/main.c");
        h.Client.Open(uri, Programs.Undeclared);
        h.Flush();
        Assert.That(atPublish.Wait(LspTestClient.Timeout), Is.True, "the compile reached its publish decision");
        int mark = h.Client.Mark;
        h.Client.Close(uri);
        h.Client.Sync();   // didClose processed: the project removed, Removed raised
        Assert.That(h.Builds.OpenProjects, Is.Empty);
        go.Set();

        // The compile thread is one: once a later build of another project is published, the late one was handled.
        string other = Uri(ws, "other.c");
        h.Client.Open(other, Programs.Clean);
        h.Flush();
        h.Client.WaitForDiagnostics(other, null, mark);
        Assert.That(h.Client.Publishes(uri, mark).Where(p => p["diagnostics"]!.AsArray().Count > 0), Is.Empty,
            "no diagnostics for the closed file: " + h.Client.Dump());

        mark = h.Client.Mark;
        h.Client.Open(uri, Programs.Undeclared, version: 7);
        h.Flush();
        JsonObject again = h.Client.WaitForDiagnostics(uri, p => p["version"]?.GetValue<int>() == 7, mark);
        Assert.That(again["diagnostics"]!.AsArray(), Is.Not.Empty, "after a reopen the project publishes again");
    }

    // ------------------------------------------------------------------ B2: Stop never waits for a compile

    [Test]
    public void Stop_Returns_At_Once_With_A_Compile_In_Flight_And_Says_So()
    {
        using var ws = new TempWorkspace();
        using var config = new TempWorkspace();
        var hang = new BlockingCompile(hangOn: "HANG");
        int baseline = LspThreads.Alive;
        var game = new InGameHostTests.FakeGameSide { Root = ws.Root };
        var host = new InGameLanguageHost(Runtime.Tables, game, new InGameHostOptions { Log = LspLog.None });
        var core = new LspServiceCore(host, new LspServiceOptions
        {
            InfoFilePath = Path.Combine(config.Root, ".purrTTY", LspServerInfoFile.FileName),
            Log = LspLog.None,
            ServerOptions = name => new KfsLanguageServerOptions
            {
                Name = name,
                Log = LspLog.None,
                Builds = new BuildSchedulerOptions { Debounce = TimeSpan.FromMilliseconds(1), Compile = hang.Compile },
            },
        });
        try
        {
            Assert.That(core.Start(), Is.True, core.Status);
            int port = int.Parse(core.Address!.Split(':')[1]);
            using LspTestClient client = ServerProtocolTests.Connect(port);
            client.Initialize(core.Token);
            client.Open(Uri(ws, "stuck.c"), "/* HANG */\n" + Programs.Clean);
            Assert.That(hang.Entered.Wait(LspTestClient.Timeout), Is.True, "the compile started");

            var clock = Stopwatch.StartNew();
            bool joined = core.Stop();   // what KfsLspService.Dispose and `purr lsp restart` call on the main thread
            clock.Stop();
            Assert.That(clock.Elapsed, Is.LessThan(TimeSpan.FromSeconds(2)), "the main thread is not held for the compile (it was 10 s)");
            Assert.That(joined, Is.True, "every thread but the busy compile ended; that one is retired, not waited for");
            Assert.That(core.Status, Is.EqualTo("stopped; 1 compile is still running and will end by itself"));
            Assert.That(purrTTY.Kfs.Tables.SystemHeaderReaders.InUse(), Is.EqualTo("1 language-server compile is still running"),
                "what the extension's uninstall sees: it leaves the header registered");
            Assert.That(LspThreads.Active, Is.EqualTo(baseline), "only the retired compile thread is left");
            Assert.That(client.WaitForEnd(), Is.True);
        }
        finally
        {
            hang.Release();
            core.Stop();
        }
        Assert.That(LspThreads.WaitUntilAtMost(baseline, LspTestClient.Timeout), Is.True);
        Assert.That(purrTTY.Kfs.Tables.SystemHeaderReaders.InUse(), Is.Null);
    }

    // ------------------------------------------------------------------ B3: the main thread's share of a frame

    private sealed class CountingGame : IKfsGameSide
    {
        public string Root = "";
        public int Ran;
        public string WorkspaceRoot => Root;
        public void RefreshTarget(TargetSnapshot target) { }
        public purrTTY.Kfs.Lsp.Live.HostComputerLive? RefreshLive() => null;
        public void ExecuteKfs(string line) => Ran++;
        public void ExecutePurr(string line) => Ran++;
    }

    [Test]
    public void One_Frame_Runs_One_Queued_Command_And_A_Burst_Is_Refused_As_Busy()
    {
        using var ws = new TempWorkspace();
        var game = new CountingGame { Root = ws.Root };
        // Budget zero: exactly one command per frame (the default budget of 4 ms behaves so for any
        // command slower than that: kfs build).
        var host = new InGameLanguageHost(Runtime.Tables, game, new InGameHostOptions { Log = LspLog.None, VerbBudgetPerFrame = TimeSpan.Zero });
        var tasks = new List<Task<CommandResult>>();
        for (int i = 0; i < 80; i++) tasks.Add(host.RunVerb($"load p{i}/main.c", CancellationToken.None));
        int refused = tasks.Count(t => t.IsCompleted && !t.Result.Ok);
        Assert.That(refused, Is.EqualTo(72), "at most eight wait");
        Assert.That(tasks.First(t => t.IsCompleted).Result.Error, Does.Contain("the game is busy: 8 commands are already waiting"));
        host.Frame();
        Assert.That(game.Ran, Is.EqualTo(1), "one frame ran one command (it ran 64 back to back)");
        for (int i = 0; i < 7; i++) host.Frame();
        Assert.That(game.Ran, Is.EqualTo(8));
        Assert.That(Task.WaitAll(tasks.ToArray(), LspTestClient.Timeout), Is.True);

        // Identical builds coalesce: one run, every caller answered with it.
        game.Ran = 0;
        var builds = Enumerable.Range(0, 20).Select(_ => host.RunVerb("build proj/main.c", CancellationToken.None)).ToList();
        Assert.That(builds.All(t => !t.IsCompleted), Is.True, "none refused: they share one slot");
        host.Frame();
        Assert.That(Task.WaitAll(builds.ToArray(), LspTestClient.Timeout), Is.True);
        Assert.That(game.Ran, Is.EqualTo(1));
        Assert.That(builds.Select(t => t.Result.Ok).Distinct(), Is.EqualTo(new[] { true }));
        host.Shutdown("done");
    }

    // ------------------------------------------------------------------ D2: the client's URIs

    [Test]
    public void Diagnostics_Use_The_Uri_Form_The_Client_Used()
    {
        if (OperatingSystem.IsWindows()) Assert.Ignore("symbolic links need privileges on Windows");
        using var real = new TempWorkspace();
        real.Write("proj/util.h", "int broken = missing;\n").Write("proj/main.c", "#include \"util.h\"\n" + Programs.Clean);
        string alias = Path.Combine(Path.GetTempPath(), "purr-kfs-alias-" + Guid.NewGuid().ToString("N"));
        Directory.CreateSymbolicLink(alias, real.Root);
        try
        {
            // The server's root is the alias; the editor opened the workspace by its real path.
            string realRoot = RealPath(real.Root);
            using var h = ServerHarness.Start(KfsLanguageHost.ForBench(Runtime.Tables, alias));
            h.Client.Initialize();
            string clientRoot = WorkspaceUris.FileUri(realRoot, windows: false);
            string main = clientRoot + "/proj/main.c";
            h.Client.Open(main, File.ReadAllText(real.Full("proj/main.c")));
            h.Flush();
            string util = clientRoot + "/proj/util.h";
            JsonObject published = h.Client.WaitForDiagnostics(util, p => p["diagnostics"]!.AsArray().Count > 0);
            Assert.That(published["uri"]!.GetValue<string>(), Is.EqualTo(util), "a file that is not open: under the client's root");
            Assert.That(h.Client.Messages.Where(m => m["method"]?.GetValue<string>() == "textDocument/publishDiagnostics")
                    .Select(m => m["params"]!["uri"]!.GetValue<string>()).All(u => u.StartsWith(clientRoot + "/", StringComparison.Ordinal)), Is.True,
                "nothing under the server's own spelling: " + h.Client.Dump());

            // The clear once the project closes goes to the client's form as well (it used to go to the server's).
            int mark = h.Client.Mark;
            h.Client.Close(main);
            h.Client.Sync();
            List<JsonObject> clears = h.Client.Messages.Skip(mark).Where(m => m["method"]?.GetValue<string>() == "textDocument/publishDiagnostics")
                .Select(m => (JsonObject)m["params"]!).ToList();
            Assert.That(clears.Select(p => (p["uri"]!.GetValue<string>(), p["diagnostics"]!.AsArray().Count)), Is.EqualTo(new[] { (util, 0) }), h.Client.Dump());
        }
        finally
        {
            Directory.Delete(alias);
        }
    }

    private static string RealPath(string full)
    {
        string root = Path.GetPathRoot(full)!;
        string current = root;
        foreach (string segment in full[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            string next = Path.Combine(current, segment);
            FileSystemInfo? target = new DirectoryInfo(next).ResolveLinkTarget(returnFinalTarget: true);
            current = target is null ? next : RealPath(Path.TrimEndingDirectorySeparator(target.FullName));
        }
        return current;
    }

    // ------------------------------------------------------------------ D4: the info file

    [Test]
    public void Only_A_Provably_Own_Info_File_Is_Deleted()
    {
        using var ws = new TempWorkspace();
        using var config = new TempWorkspace();
        string path = Path.Combine(config.Root, ".purrTTY", LspServerInfoFile.FileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ not json, someone's notes");
        using var core = new LspServiceCore(Host(ws, null), new LspServiceOptions
        {
            InfoFilePath = path,
            Log = LspLog.None,
            ServerOptions = name => new KfsLanguageServerOptions { Name = name, Log = LspLog.None },
        });
        Assert.That(core.Start(), Is.True);
        Assert.That(LspServerInfoFile.Read(path)!.Token, Is.EqualTo(core.Token), "an unreadable file is replaced at start (atomically)");

        // Something else rewrote it with content we cannot parse: stop leaves it.
        File.WriteAllText(path, "{ \"half\": ");
        Assert.That(core.Stop(), Is.True);
        Assert.That(File.ReadAllText(path), Is.EqualTo("{ \"half\": "), "a file that is not provably ours is never deleted (it used to be)");

        Assert.That(core.Start(), Is.True);
        string token = core.Token!;
        LspServerInfoFile.Write(path, new LspServerInfo(core.Address!, token, Environment.ProcessId + 1, "x", ws.Root + "-other"));
        core.Stop();
        Assert.That(File.Exists(path), Is.True, "our token but another process: not ours");
        Assert.That(LspServerInfoFile.DeleteIfOurs(path, token, Environment.ProcessId + 1), Is.True);
        Assert.That(File.Exists(path), Is.False);
    }

    // ------------------------------------------------------------------ D6: a configure callback that throws

    [Test]
    public void A_Configure_Callback_That_Throws_Leaks_Nothing()
    {
        using var ws = new TempWorkspace();
        int baseline = LspThreads.Alive;
        int closed = 0, attempts = 0;
        KfsLspListener listener = Listen(Host(ws), configure: s =>
        {
            s.Closed += _ => Interlocked.Increment(ref closed);
            if (Interlocked.Increment(ref attempts) == 1) throw new InvalidOperationException("injected configure failure");
        });
        try
        {
            using (TcpClient first = Raw(listener.Port))
                Assert.That(ClosedSilently(first, out _), Is.True);
            Assert.That(Wait(() => Volatile.Read(ref closed) == 1), Is.True, "the half-made server was disposed: its Closed released what configure registered");
            Assert.That(listener.Clients, Is.Empty);
            using LspTestClient ok = ServerProtocolTests.Connect(listener.Port);
            Assert.That(ok.Initialize(Token)["capabilities"], Is.Not.Null);
        }
        finally
        {
            Assert.That(listener.Stop(), Is.True);
        }
        Assert.That(LspThreads.Alive, Is.EqualTo(baseline));
        Assert.That(Volatile.Read(ref closed), Is.EqualTo(2));
    }
}
