using System.Diagnostics;
using System.Text.Json.Nodes;
using KSA.Kfs.Host;
using KSA.Kfs.Host.Workbench;
using NUnit.Framework;
using purrTTY.Kfs.Lsp;
using purrTTY.Kfs.Lsp.Build;
using purrTTY.Kfs.Lsp.Documents;
using purrTTY.Kfs.Runtime;
using purrTTY.Kfs.Tools.Tests.Lsp;

namespace purrTTY.Kfs.Tools.Tests.Runtime.Lsp;

/// <summary>
/// The build scheduler (plan §6.3) through the server: coalescing and latest-wins, the exception
/// guard, the watchdog, target changes, and a latency smoke test.
/// </summary>
[TestFixture]
public sealed class SchedulerTests : ToolsRuntimeFixture
{
    private static string Uri(TempWorkspace ws, string path) => new WorkspaceUris(ws.Root).ToUri(path);

    private static int Count(JsonObject published) => published["diagnostics"]!.AsArray().Count;

    private static List<string> Codes(JsonObject published) =>
        published["diagnostics"]!.AsArray().Select(d => d!["code"]!.GetValue<string>()).ToList();

    private static string Message(JsonObject published) => published["diagnostics"]![0]!["message"]!.GetValue<string>();

    [Test]
    public void Latest_Wins_Under_Rapid_Edits()
    {
        using var ws = new TempWorkspace();
        var gate = new BlockingCompile();
        using var h = ServerHarness.Start(KfsLanguageHost.ForBench(Runtime.Tables, ws.Root), new BuildSchedulerOptions
        {
            Debounce = Timeout.InfiniteTimeSpan,
            Compile = gate.Compile,
        });
        h.Client.Initialize();
        string uri = Uri(ws, "rapid.c");
        h.Client.Open(uri, Programs.Undeclared, 1);
        h.Flush();
        Assert.That(Codes(h.Client.WaitForDiagnostics(uri, p => p["version"]?.GetValue<int>() == 1)), Is.EqualTo(new[] { "KC-E003" }));

        // A burst of changes, every one but the last broken: one build, for the last version, published once.
        int mark = h.Client.Mark;
        for (int v = 2; v <= 30; v++) h.Client.Change(uri, v, v == 30 ? Programs.Clean : Programs.Undeclared.Replace("missing", "missing" + v));
        int before = gate.Calls;
        h.Flush();
        JsonObject last = h.Client.WaitForDiagnostics(uri, p => p["version"]?.GetValue<int>() == 30, mark);
        Assert.That(Count(last), Is.EqualTo(0), last.ToJsonString());
        Assert.That(h.Builds.WaitForIdle(LspTestClient.Timeout), Is.True);
        Assert.That(gate.Calls - before, Is.EqualTo(1), "the burst compiled once");
        Assert.That(h.Client.Publishes(uri, mark).Select(p => p["version"]?.GetValue<int>()), Is.EqualTo(new int?[] { 30 }));

        // A change while a compile runs: the running compile's result is stale and dropped, the newer one is published.
        mark = h.Client.Mark;
        gate.BlockNext();
        h.Client.Change(uri, 31, Programs.Undeclared);
        h.Flush();
        Assert.That(gate.Entered.Wait(LspTestClient.Timeout), Is.True, "the compile of version 31 started");
        h.Client.Change(uri, 32, Programs.Clean.Replace("n + 1u", "n + 2u"));
        h.Client.Sync();
        gate.Release();
        h.Builds.Flush();
        JsonObject newest = h.Client.WaitForDiagnostics(uri, p => p["version"]?.GetValue<int>() == 32, mark);
        Assert.That(Count(newest), Is.EqualTo(0));
        Assert.That(h.Builds.WaitForIdle(LspTestClient.Timeout), Is.True);
        Assert.That(h.Client.Publishes(uri, mark).Select(p => p["version"]?.GetValue<int>()), Is.EqualTo(new int?[] { 32 }), "version 31 was never published");
    }

    [Test]
    public void Latest_Wins_With_The_Real_Debounce()
    {
        // The default 120 ms debounce, no Flush: a burst sent in one write builds once, for the last version.
        using var ws = new TempWorkspace();
        var counting = new BlockingCompile();
        var received = new List<TimeSpan>();
        var clock = Stopwatch.StartNew();
        LspLogSink log = (level, message) =>
        {
            if (level == LspLogLevel.Debug && (message.EndsWith("<- textDocument/didOpen", StringComparison.Ordinal) || message.EndsWith("<- textDocument/didChange", StringComparison.Ordinal)))
                lock (received) received.Add(clock.Elapsed);
        };
        using var h = ServerHarness.Start(KfsLanguageHost.ForBench(Runtime.Tables, ws.Root), new BuildSchedulerOptions { Compile = counting.Compile }, log: log);
        h.Client.Initialize();
        string uri = Uri(ws, "burst.c");
        var burst = new List<byte>();
        burst.AddRange(LspTestClient.Frame(Notification("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject { ["uri"] = uri, ["languageId"] = "kc", ["version"] = 1, ["text"] = Programs.Undeclared },
        })));
        for (int v = 2; v <= 20; v++)
        {
            burst.AddRange(LspTestClient.Frame(Notification("textDocument/didChange", new JsonObject
            {
                ["textDocument"] = new JsonObject { ["uri"] = uri, ["version"] = v },
                ["contentChanges"] = new JsonArray(new JsonObject { ["text"] = v == 20 ? Programs.Clean : Programs.Undeclared }),
            })));
        }
        int mark = h.Client.Mark;
        h.Client.SendRaw(burst.ToArray());

        JsonObject published = h.Client.WaitForDiagnostics(uri, p => p["version"]?.GetValue<int>() == 20, mark);
        Assert.That(Count(published), Is.EqualTo(0));
        Assert.That(h.Builds.WaitForIdle(LspTestClient.Timeout), Is.True);
        Assert.That(h.Client.Publishes(uri, mark).Select(p => p["version"]?.GetValue<int>()).Last(), Is.EqualTo(20));
        // Exact, not a loose bound: a build can start before the burst ends only where two consecutive
        // changes were received a whole debounce apart (a stalled read thread). Without such a gap
        // the burst built exactly once.
        TimeSpan[] times;
        lock (received) times = received.ToArray();
        Assert.That(times, Has.Length.EqualTo(20));
        int gaps = times.Zip(times.Skip(1), (a, b) => b - a).Count(g => g >= BuildSchedulerOptions.DefaultDebounce - TimeSpan.FromMilliseconds(5));
        if (gaps == 0) Assert.That(counting.Calls, Is.EqualTo(1), "no gap of a debounce in the burst: one build");
        else Assert.That(counting.Calls, Is.InRange(1, 1 + gaps), $"{gaps} gap(s) of a debounce while the burst was read");
    }

    private static JsonObject Notification(string method, JsonObject @params) => new() { ["jsonrpc"] = "2.0", ["method"] = method, ["params"] = @params };

    [Test]
    public void Compiler_Exception_Becomes_Diagnostic()
    {
        using var ws = new TempWorkspace();
        using var h = ServerHarness.Start(KfsLanguageHost.ForBench(Runtime.Tables, ws.Root), new BuildSchedulerOptions
        {
            Debounce = Timeout.InfiniteTimeSpan,
            Compile = ThrowOnBoom,
        });
        h.Client.Initialize();
        string uri = Uri(ws, "boom.c");
        string text = "/* BOOM */\n" + Programs.Clean;
        h.Client.Open(uri, text);
        h.Flush();

        JsonObject published = h.Client.WaitForDiagnostics(uri);
        Assert.That(Codes(published), Is.EqualTo(new[] { BuildScheduler.ErrorCode }));
        Assert.That(Message(published), Does.StartWith("the game's kC toolchain threw InvalidOperationException: RET 0 with 2 cells on the stack in 't' (builder invariant)"));
        JsonNode range = published["diagnostics"]![0]!["range"]!;
        Assert.That((range["start"]!["line"]!.GetValue<int>(), range["end"]!["character"]!.GetValue<int>()), Is.EqualTo((0, 10)), "the first line of the file");
        Assert.That(h.Builds.ResultFor("boom.c")!.Kind, Is.EqualTo(ProjectBuildKind.CompilerThrew));

        // The server carries on: the next version builds normally.
        int mark = h.Client.Mark;
        h.Client.Change(uri, 2, Programs.Undeclared);
        h.Flush();
        Assert.That(Codes(h.Client.WaitForDiagnostics(uri, p => p["version"]?.GetValue<int>() == 2, mark)), Is.EqualTo(new[] { "KC-E003" }));
    }

    /// <summary>Throws what the spike's throws.c made the game's compiler throw, for any project containing BOOM.</summary>
    private static ProjectCompileOutcome ThrowOnBoom(ProjectCompileRequest request) =>
        request.Project.FileMap.Values.Any(t => t.Contains("BOOM", StringComparison.Ordinal))
            ? throw new InvalidOperationException("RET 0 with 2 cells on the stack in 't' (builder invariant)")
            : BuildScheduler.DefaultCompile(request);

    [Test]
    public void Watchdog_Retires_A_Stuck_Compile()
    {
        using var ws = new TempWorkspace();
        var hang = new BlockingCompile(hangOn: "HANG");
        int baseline = LspThreads.Alive;
        var h = ServerHarness.Start(KfsLanguageHost.ForBench(Runtime.Tables, ws.Root), new BuildSchedulerOptions
        {
            Debounce = Timeout.InfiniteTimeSpan,
            CompileTimeLimit = TimeSpan.FromMilliseconds(300),
            MaxRetiredThreads = 1,
            Compile = hang.Compile,
        });
        try
        {
            h.Client.Initialize();
            string uri = Uri(ws, "stuck.c");
            Assert.That(h.Builds.CompileThreadId, Is.Null, "no compile thread before the first build");

            // 1. A compile that never returns: abandoned, its thread retired, a fresh one started.
            h.Client.Open(uri, "/* HANG 1 */\n" + Programs.Clean);
            h.Flush();
            Assert.That(hang.Entered.Wait(LspTestClient.Timeout), Is.True);
            int firstThread = hang.HungThreads.Single();
            JsonObject abandoned = h.Client.WaitForDiagnostics(uri, p => Codes(p).Contains(BuildScheduler.ErrorCode));
            Assert.That(Message(abandoned), Does.StartWith("the compile did not finish in 0.3 s and was abandoned"));
            Assert.That(h.Builds.ThreadsStarted, Is.EqualTo(2));
            Assert.That(h.Builds.RetiredThreadsAlive, Is.EqualTo(1));
            Assert.That(h.Builds.CompileThreadId, Is.Not.EqualTo(firstThread), "a fresh compile thread");
            Assert.That(hang.HungThreads, Is.EqualTo(new[] { firstThread }));

            // 2. Later edits build on the fresh thread.
            int mark = h.Client.Mark;
            h.Client.Change(uri, 2, Programs.Undeclared);
            h.Flush();
            Assert.That(Codes(h.Client.WaitForDiagnostics(uri, p => p["version"]?.GetValue<int>() == 2, mark)), Is.EqualTo(new[] { "KC-E003" }));
            Assert.That(hang.LastThread, Is.EqualTo(h.Builds.CompileThreadId));

            // 3. Another stuck compile with the one allowed retired thread still running: not replaced;
            //    compiles are refused, and said so, until a retired thread ends.
            mark = h.Client.Mark;
            h.Client.Change(uri, 3, "/* HANG 2 */\n" + Programs.Clean);
            h.Flush();
            JsonObject second = h.Client.WaitForDiagnostics(uri, p => Codes(p).Contains(BuildScheduler.ErrorCode), mark);
            Assert.That(Message(second), Does.Contain("no further compile starts until one of them ends"));
            Assert.That(h.Builds.Refusing, Is.True);
            Assert.That(h.Builds.ThreadsStarted, Is.EqualTo(2), "no third thread");

            mark = h.Client.Mark;
            h.Client.Change(uri, 4, Programs.Clean);
            h.Flush();
            JsonObject refused = h.Client.WaitForDiagnostics(uri, p => p["version"]?.GetValue<int>() == 4, mark);
            Assert.That(Message(refused), Does.StartWith("compiles are suspended"));

            // 4. The stuck compiles return: compiling resumes and the newest text is built.
            mark = h.Client.Mark;
            hang.Release();
            JsonObject resumed = h.Client.WaitForDiagnostics(uri, p => p["version"]?.GetValue<int>() == 4 && Count(p) == 0, mark);
            Assert.That(Count(resumed), Is.EqualTo(0));
            Assert.That(h.Builds.WaitForRetiredThreads(LspTestClient.Timeout), Is.True, "every retired thread ended");
            Assert.That(h.Builds.Refusing, Is.False);
        }
        finally
        {
            hang.Release();
            h.Dispose();
        }
        Assert.That(LspThreads.WaitUntilAtMost(baseline, LspTestClient.Timeout), Is.True, "no compile thread is left");
    }

    [Test]
    public void Target_Change_Rediagnoses()
    {
        using var ws = new TempWorkspace();
        var host = new KfsLanguageHost(Runtime.Tables, ws.Root, Target(1));
        using var h = ServerHarness.Start(host);
        h.Client.Initialize();
        string uri = Uri(ws, "needs4.c");
        const string text = """
            #include <kfs.h>
            #include <vehicle.h>
            #pragma kfs tick_hz 10
            #pragma kfs claim ALLOCATION
            #pragma kfs task run rate_hz=10
            #if THRUSTER_COUNT < 4
            #error "this program needs four thrusters"
            #endif
            void run(void) { commands.thruster[THRUSTER_COUNT - 1].pulse_s = 0.1f; }

            """;
        h.Client.Open(uri, text);
        h.Flush();
        Assert.That(Codes(h.Client.WaitForDiagnostics(uri)), Is.EqualTo(new[] { "KC-E014" }));

        int mark = h.Client.Mark;
        host.SetTarget(Target(4));
        h.Builds.Flush();
        JsonObject four = h.Client.WaitForDiagnostics(uri, null, mark);
        Assert.That(Count(four), Is.EqualTo(0), $"clean on four thrusters: {four.ToJsonString()}");
        Assert.That(h.Builds.ResultFor("needs4.c")!.Target.Layout.Fingerprint, Is.EqualTo(host.CurrentTarget.Layout.Fingerprint));

        mark = h.Client.Mark;
        host.SetTarget(Target(2));
        h.Builds.Flush();
        Assert.That(Codes(h.Client.WaitForDiagnostics(uri, null, mark)), Is.EqualTo(new[] { "KC-E014" }), "an error again on two");
    }

    private static BuildTarget Target(int thrusters) =>
        new(Runtime.Tables.BenchPart, LayoutGenerator.Vehicle("t", thrusters, 0, 0), $"t ({thrusters} thrusters)");

    [Test]
    public void Performance_Smoke_Warm_Build_Latency()
    {
        using var ws = new TempWorkspace();
        Examples.Write(new Workspace(ws.Root), overwrite: true, archived: true);
        // The largest stock kC example, and the bus example the spike measured slowest (~19 ms).
        string largest = Examples.Files.Where(f => f.Key.EndsWith(".c", StringComparison.Ordinal)).OrderByDescending(f => f.Value.Length).First().Key;
        ws.Write("small.c", CliCheckTests.Blink);

        // The examples are written for a vehicle (bus messages, thrusters): build for one.
        using var h = ServerHarness.Start(new KfsLanguageHost(Runtime.Tables, ws.Root,
            new BuildTarget(Runtime.Tables.BenchPart, LayoutGenerator.Vehicle("t", 4, 1, 1), "t")));
        h.Client.Initialize();
        var results = new List<string>();
        foreach (string file in new[] { "small.c", largest, Examples.KC_ATTITUDE })
        {
            string uri = Uri(ws, file);
            string text = File.ReadAllText(ws.Full(file));
            h.Client.Open(uri, text, 1);
            h.Flush();
            JsonObject first = h.Client.WaitForDiagnostics(uri, p => p["version"]?.GetValue<int>() == 1);
            Assert.That(Codes(first).Where(c => c.Contains("-E", StringComparison.Ordinal)), Is.Empty, $"{file}: {first.ToJsonString()}");

            var times = new List<double>();
            for (int v = 2; v <= 11; v++)
            {
                int mark = h.Client.Mark;
                h.Client.Change(uri, v, text + $"/* edit {v} */\n");
                h.Client.Sync();
                var clock = Stopwatch.StartNew();
                h.Builds.Flush();
                h.Client.WaitForDiagnostics(uri, p => p["version"]?.GetValue<int>() == v, mark);
                times.Add(clock.Elapsed.TotalMilliseconds);
            }
            times.Sort();
            double median = times[times.Count / 2];
            results.Add($"{file}: median {median:0.0} ms, max {times[^1]:0.0} ms");
            // A smoke bound, not a benchmark: warm builds take ~20 ms here (the spike measured ~19 ms
            // for the slowest example); 500 ms catches a regression by an order of magnitude while a
            // loaded CI runner stays well inside it.
            Assert.That(median, Is.LessThan(500), $"warm build latency through the scheduler: {string.Join("; ", results)}");
        }
    }
}

/// <summary>
/// A compile function for the scheduler tests: counts calls and can block — the next call
/// (<see cref="BlockNext"/>) or every call whose project contains a marker — until <see cref="Release"/>.
/// </summary>
internal sealed class BlockingCompile(string? hangOn = null)
{
    private readonly ManualResetEventSlim _release = new(false);
    private readonly object _gate = new();
    private readonly List<int> _hung = [];
    private int _calls;
    private bool _blockNext;

    public ManualResetEventSlim Entered { get; } = new(false);

    public int Calls => Volatile.Read(ref _calls);

    public int? LastThread { get; private set; }

    public IReadOnlyList<int> HungThreads
    {
        get { lock (_gate) return _hung.ToList(); }
    }

    public void BlockNext()
    {
        lock (_gate)
        {
            _blockNext = true;
            Entered.Reset();
            _release.Reset();
        }
    }

    public void Release() => _release.Set();

    public ProjectCompileOutcome Compile(ProjectCompileRequest request)
    {
        Interlocked.Increment(ref _calls);
        bool block;
        lock (_gate)
        {
            block = _blockNext || (hangOn is not null && request.Project.FileMap.Values.Any(t => t.Contains(hangOn, StringComparison.Ordinal)));
            _blockNext = false;
            if (block && hangOn is not null) _hung.Add(Environment.CurrentManagedThreadId);
        }
        if (block)
        {
            Entered.Set();
            _release.Wait(TimeSpan.FromSeconds(60)); // the test releases it; the bound only stops a broken test from leaking the thread
        }
        LastThread = Environment.CurrentManagedThreadId;
        return BuildScheduler.DefaultCompile(request);
    }
}
