using System.Text.Json.Nodes;
using NUnit.Framework;
using purrTTY.Kfs.Lsp;
using purrTTY.Kfs.Lsp.Documents;
using purrTTY.Kfs.Lsp.Features;
using purrTTY.Kfs.Lsp.Live;
using purrTTY.Kfs.Tools.Tests.Lsp;

namespace purrTTY.Kfs.Tools.Tests.Runtime.Lsp;

/// <summary>
/// <c>purrtty/subscribe</c> and the live feeds (<see cref="LiveFeed"/>) over a fake
/// <see cref="ILiveSource"/> (the game-free view the feed reads; the in-game
/// <see cref="HostComputerLive"/> over a real computer is covered in <see cref="InGameHostTests"/>).
/// The feed runs without its timer here: each test calls <see cref="LiveFeed.Poll"/> and moves a fake clock.
/// </summary>
[TestFixture]
public sealed class LiveFeedTests : ToolsRuntimeFixture
{
    /// <summary>A program log and state the test writes; counts every read.</summary>
    private sealed class FakeLive : ILiveSource
    {
        private readonly object _gate = new();
        private readonly List<string> _log = [];
        private long _version;
        private LiveState _state = new("blink", "Running", 0, 0, null);

        public int VersionReads, LogCopies, StateReads;

        public void Log(params string[] lines)
        {
            lock (_gate)
            {
                _log.AddRange(lines);
                _version += lines.Length;
            }
        }

        public LiveState State
        {
            get { lock (_gate) return _state; }
            set { lock (_gate) _state = value; }
        }

        public int Reads => VersionReads + LogCopies + StateReads;

        public long LogVersion
        {
            get
            {
                Interlocked.Increment(ref VersionReads);
                lock (_gate) return _version;
            }
        }

        public long CopyLog(int count, List<string> into)
        {
            Interlocked.Increment(ref LogCopies);
            lock (_gate)
            {
                into.Clear();
                into.AddRange(_log.Skip(Math.Max(0, _log.Count - count)));
                return _version;
            }
        }

        public LiveState ReadState()
        {
            Interlocked.Increment(ref StateReads);
            return State;
        }
    }

    private long _now;

    // Without the subscribe-time tail unless a test asks for it (Log_Tail_On_Subscribe…).
    private LiveFeed NewFeed(FakeLive live, int tail = 0) =>
        new(() => live, new LiveFeedOptions { UseTimer = false, Clock = () => _now, Log = LspLog.None, LogTailLines = tail });

    private static ServerHarness Client(TempWorkspace ws, LiveFeed feed) =>
        ServerHarness.Start(KfsLanguageHost.ForBench(Runtime.Tables, ws.Root), configure: s => HostFeatures.AddTo(s, new HostFeatureOptions { Feed = feed }));

    private static void Subscribe(ServerHarness h, params string[] topics)
    {
        h.Client.Notify(HostFeatures.SubscribeMethod, new JsonObject { ["topics"] = new JsonArray(topics.Select(t => (JsonNode)t).ToArray()) });
        h.Client.Sync();   // processed before the test polls
    }

    private static List<string> LogLines(ServerHarness h) =>
        h.Client.Messages.Where(m => m["method"]?.GetValue<string>() == "purrtty/kfsLog")
            .SelectMany(m => m["params"]!["lines"]!.AsArray().Select(l => l!.GetValue<string>())).ToList();

    private static List<JsonObject> States(ServerHarness h) =>
        h.Client.Messages.Where(m => m["method"]?.GetValue<string>() == "purrtty/kfsState").Select(m => (JsonObject)m["params"]!).ToList();

    /// <summary>Waits until <paramref name="h"/> holds <paramref name="count"/> messages of <paramref name="method"/> (a condition, never a sleep).</summary>
    private static void WaitForCount(ServerHarness h, string method, int count)
    {
        h.Client.Sync();   // the notifications were queued before the sync's answer, on the same writer
        int got = h.Client.Messages.Count(m => m["method"]?.GetValue<string>() == method);
        Assert.That(got, Is.EqualTo(count), $"{method} messages\n{h.Client.Dump()}");
    }

    [Test]
    public void Log_Lines_Arrive_Once_And_In_Order_For_Each_Subscriber()
    {
        using var ws = new TempWorkspace();
        var live = new FakeLive();
        live.Log("[k 0 t 0.00] loaded 312 bytes", "[k 1 t 0.10] before anyone subscribed");
        using LiveFeed feed = NewFeed(live);
        ServerHarness a = Client(ws, feed);
        using ServerHarness b = Client(ws, feed);
        try
        {
            LogScenario(live, feed, a, b);
        }
        finally
        {
            a.Dispose();
        }
    }

    private static void LogScenario(FakeLive live, LiveFeed feed, ServerHarness a, ServerHarness b)
    {
        a.Client.Initialize();
        b.Client.Initialize();

        feed.Poll();
        Assert.That((live.Reads, feed.IsPolling, feed.Polls), Is.EqualTo((0, false, 0L)), "nothing is read without a subscriber");

        Subscribe(a, "log");
        Assert.That(feed.IsPolling, Is.True);
        feed.Poll();   // the baseline: no backlog
        Assert.That(live.LogCopies, Is.Zero);
        live.Log("[k 2 t 0.20] one", "[k 3 t 0.30] two");
        feed.Poll();
        live.Log("[k 4 t 0.40] three");
        Subscribe(b, "log", "state");   // joins the running log feed
        feed.Poll();
        feed.Poll();   // nothing new: the tail is not copied again
        Assert.That(live.LogCopies, Is.EqualTo(2), "copied only when the version moved");
        live.Log("[k 5 t 0.50] four", "[k 6 t 0.60] five");
        feed.Poll();

        WaitForCount(a, "purrtty/kfsLog", 3);
        WaitForCount(b, "purrtty/kfsLog", 2);
        Assert.That(LogLines(a), Is.EqualTo(new[] { "[k 2 t 0.20] one", "[k 3 t 0.30] two", "[k 4 t 0.40] three", "[k 5 t 0.50] four", "[k 6 t 0.60] five" }));
        Assert.That(LogLines(b), Is.EqualTo(new[] { "[k 4 t 0.40] three", "[k 5 t 0.50] four", "[k 6 t 0.60] five" }));
        Assert.That(States(a), Is.Empty, "a subscribes to log only");
        Assert.That(States(b), Has.Count.EqualTo(1));

        // Unsubscribing and disconnecting stop the polling.
        Subscribe(b);
        Assert.That(feed.TopicsOf(b.Server), Is.EqualTo(LiveTopics.None));
        Assert.That(feed.IsPolling, Is.True, "a still subscribes");
        a.Client.ShutdownAndExit();
        Assert.That(a.WaitForServerEnd(), Is.True);
        Assert.That((feed.IsPolling, feed.Subscribers), Is.EqualTo((false, 0)), "the closed connection was removed");
        int reads = live.Reads;
        feed.Poll();
        Assert.That(live.Reads, Is.EqualTo(reads), "no subscriber: nothing read");
    }

    [Test]
    public void Log_Tail_On_Subscribe_Then_Every_Later_Line_Once()
    {
        using var ws = new TempWorkspace();
        var live = new FakeLive();
        live.Log(Enumerable.Range(1, 5).Select(i => $"old {i}").ToArray());
        using LiveFeed feed = NewFeed(live, tail: 3);
        using ServerHarness a = Client(ws, feed), b = Client(ws, feed);
        a.Client.Initialize();
        b.Client.Initialize();

        Subscribe(a, "log", "output");
        feed.Poll();                                    // a: the tail
        live.Log("new 1");
        feed.Poll();                                    // a: new 1
        Subscribe(b, "state", "log");                   // b joins: its own tail, up to the same version
        live.Log("new 2");
        feed.Poll();                                    // a: new 2; b: the tail (ends with new 2)
        live.Log("new 3");
        feed.Poll();                                    // both: new 3
        Subscribe(a, "output");                         // log off …
        Subscribe(a, "log", "output");                  // … and on again: a tail again (the editor reopened its log)
        feed.Poll();

        WaitForCount(a, "purrtty/kfsLog", 5);
        Assert.That(LogLines(a), Is.EqualTo(new[] { "old 3", "old 4", "old 5", "new 1", "new 2", "new 3", "new 1", "new 2", "new 3" }));
        Assert.That(LogLines(b), Is.EqualTo(new[] { "old 5", "new 1", "new 2", "new 3" }), "no line twice, none missing");
        List<bool> tails = a.Client.Messages.Where(m => m["method"]?.GetValue<string>() == "purrtty/kfsLog")
            .Select(m => m["params"]!["tail"]?.GetValue<bool>() ?? false).ToList();
        Assert.That(tails, Is.EqualTo(new[] { true, false, false, false, true }), "the tail is marked");

        // An empty log: no tail message at all.
        var empty = new FakeLive();
        using LiveFeed quiet = NewFeed(empty, tail: 3);
        using ServerHarness c = Client(ws, quiet);
        c.Client.Initialize();
        Subscribe(c, "log");
        quiet.Poll();
        WaitForCount(c, "purrtty/kfsLog", 0);
    }

    [Test]
    public void State_Is_Sent_On_Change_And_Read_At_Most_Five_Times_A_Second()
    {
        using var ws = new TempWorkspace();
        var live = new FakeLive();
        using LiveFeed feed = NewFeed(live);
        using ServerHarness h = Client(ws, feed);
        h.Client.Initialize();
        Subscribe(h, "state");

        feed.Poll();                                   // t = 0: first read, sent
        live.State = new LiveState("blink", "Running", 5, 0, null);
        _now += 100; feed.Poll();                      // t = 100 ms: too soon to read again
        Assert.That(live.StateReads, Is.EqualTo(1));
        _now += 100; feed.Poll();                      // t = 200 ms: read, changed, sent
        _now += 200; feed.Poll();                      // t = 400 ms: read, unchanged, not sent
        Assert.That(live.StateReads, Is.EqualTo(3));
        Assert.That(live.LogCopies + live.VersionReads, Is.Zero, "state only: the log is never read");

        WaitForCount(h, "purrtty/kfsState", 2);
        List<JsonObject> states = States(h);
        Assert.That(states[0].ToJsonString(), Is.EqualTo("""{"program":"blink","state":"Running","tick":0,"overruns":0,"trap":null}"""));
        Assert.That(states[1]["tick"]!.GetValue<ulong>(), Is.EqualTo(5UL));
    }

    [Test]
    public void A_Trap_Becomes_A_Diagnostic_Until_The_Program_Restarts()
    {
        using var ws = new TempWorkspace();
        ws.Write("prog/main.c", "void run(void) {\n    n = n + 1u;\n        q = 10u / zero;\n}\n");
        var live = new FakeLive();
        using LiveFeed feed = NewFeed(live);
        using ServerHarness h = Client(ws, feed);
        h.Client.Initialize();
        Subscribe(h, "state");
        string uri = WorkspaceUris.FileUri(ws.Full("prog/main.c"), OperatingSystem.IsWindows());

        live.State = new LiveState("prog", "Halted", 4, 0, new LiveTrap("trap DIV_ZERO in task 0, function run", "main.c", "prog/main.c", 3));
        feed.Poll();
        JsonObject published = h.Client.WaitForDiagnostics(uri, p => p["diagnostics"]!.AsArray().Count == 1);
        JsonObject d = (JsonObject)published["diagnostics"]![0]!;
        Assert.That(d["code"]!.GetValue<string>(), Is.EqualTo(LiveFeed.TrapCode));
        Assert.That(d["severity"]!.GetValue<int>(), Is.EqualTo(1));
        Assert.That(d["range"]!.ToJsonString(), Is.EqualTo("""{"start":{"line":2,"character":8},"end":{"line":2,"character":23}}"""), "the trimmed source line");
        Assert.That(d["message"]!.GetValue<string>(), Does.StartWith("trap DIV_ZERO in task 0, function run: the program halted here"));

        WaitForCount(h, "purrtty/kfsState", 1);
        JsonObject state = States(h)[0];
        Assert.That(state["trap"]!["uri"]!.GetValue<string>(), Is.EqualTo(uri));
        Assert.That(state["trap"]!["line"]!.GetValue<int>(), Is.EqualTo(2), "0-based");
        Assert.That(state["trap"]!["message"]!.GetValue<string>(), Is.EqualTo("trap DIV_ZERO in task 0, function run"));

        // The same trap on the next read: nothing published again. A reset clears it.
        int mark = h.Client.Mark;
        _now += 200; feed.Poll();
        live.State = new LiveState("prog", "Running", 1, 0, null);
        _now += 200; feed.Poll();
        h.Client.WaitForDiagnostics(uri, p => p["diagnostics"]!.AsArray().Count == 0, mark);
        Assert.That(h.Client.Publishes(uri, mark), Has.Count.EqualTo(1), "published once when it cleared");

        // Unsubscribing clears a trap it was shown.
        live.State = new LiveState("prog", "Halted", 4, 0, new LiveTrap("trap DIV_ZERO in task 0, function run", "main.c", "prog/main.c", 3));
        _now += 200; feed.Poll();
        h.Client.WaitForDiagnostics(uri, p => p["diagnostics"]!.AsArray().Count == 1, mark);
        mark = h.Client.Mark;
        Subscribe(h);
        h.Client.WaitForDiagnostics(uri, p => p["diagnostics"]!.AsArray().Count == 0, mark);
    }

    [Test]
    public void Output_Goes_Only_To_Output_Subscribers()
    {
        using var ws = new TempWorkspace();
        using LiveFeed feed = NewFeed(new FakeLive());
        using ServerHarness h = Client(ws, feed), other = Client(ws, feed);
        h.Client.Initialize();
        other.Client.Initialize();

        feed.WriteOutput("before anyone listens\n");
        Assert.That(feed.HasOutput, Is.False, "not buffered without a subscriber");
        Subscribe(h, "output");
        Subscribe(other, "log");
        Assert.That(feed.IsPolling, Is.True, "log polls; output alone would not");
        feed.WriteOutput("target: vehicle 12 (bench); Load, Start, Stop and Reset act on it\n");
        feed.WriteOutput("queued start on vehicle 12\n");
        feed.FlushOutput();
        feed.FlushOutput();   // nothing buffered: nothing sent

        WaitForCount(h, "purrtty/kfsOutput", 1);
        WaitForCount(other, "purrtty/kfsOutput", 0);
        JsonObject output = h.Client.Messages.Single(m => m["method"]?.GetValue<string>() == "purrtty/kfsOutput");
        Assert.That(output["params"]!.ToJsonString(),
            Is.EqualTo("""{"text":"target: vehicle 12 (bench); Load, Start, Stop and Reset act on it\nqueued start on vehicle 12\n"}"""));
    }

    [Test]
    public void The_Timer_Polls_Only_While_Someone_Subscribes()
    {
        using var ws = new TempWorkspace();
        var live = new FakeLive();
        using var feed = new LiveFeed(() => live, new LiveFeedOptions { Period = TimeSpan.FromMilliseconds(10), Log = LspLog.None });
        using ServerHarness h = Client(ws, feed);
        h.Client.Initialize();
        Assert.That(feed.IsPolling, Is.False);

        Subscribe(h, "log");
        Assert.That(SpinUntil(() => live.VersionReads > 2), Is.True, "the timer polls the log version");
        live.Log("[k 1 t 0.10] hello from the timer");
        h.Client.WaitFor(m => m["method"]?.GetValue<string>() == "purrtty/kfsLog", "a log notification from the timer");

        Subscribe(h);
        Assert.That(feed.IsPolling, Is.False, "the timer is disarmed with the last subscriber");
        int reads = live.Reads;
        feed.Poll();
        Assert.That(live.Reads, Is.EqualTo(reads), "and a poll reads nothing");
    }

    private static bool SpinUntil(Func<bool> condition) => SpinWait.SpinUntil(condition, LspTestClient.Timeout);
}
