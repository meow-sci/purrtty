using System.Text.Json.Nodes;
using KSA.Kfs.Host;
using NUnit.Framework;
using purrTTY.Kfs.Lsp;
using purrTTY.Kfs.Lsp.Documents;
using purrTTY.Kfs.Lsp.Features;
using purrTTY.Kfs.Lsp.InGame;
using purrTTY.Kfs.Lsp.Live;
using purrTTY.Kfs.Testing;
using purrTTY.Kfs.Tools.Tests.Lsp;

namespace purrTTY.Kfs.Tools.Tests.Runtime.Lsp;

/// <summary>
/// The in-game host (<see cref="InGameLanguageHost"/>) with a fake game side: the target snapshot,
/// the verb queue through the output tee, the workspace root, the per-frame cost; and
/// <see cref="HostComputerLive"/> over a real bench computer of the game.
/// </summary>
[TestFixture]
public sealed class InGameHostTests : ToolsRuntimeFixture
{
    /// <summary>The game side a test drives: a target to publish, a workspace root, a console that prints through the tee.</summary>
    internal sealed class FakeGameSide : IKfsGameSide
    {
        public string Root { get; set; } = "";
        public BuildTarget? Next { get; set; }
        public HostComputerLive? LiveProgram { get; set; }
        public TextWriter? Console { get; set; }
        public List<(string Line, int Thread)> Ran { get; } = [];

        public string WorkspaceRoot => Root;

        public void RefreshTarget(TargetSnapshot target)
        {
            if (Next is { } t) target.Refresh(t.Part, t.Layout, t.DisplayName, t.IsVehicle);
        }

        public HostComputerLive? RefreshLive() => LiveProgram;

        public void ExecuteKfs(string line)
        {
            lock (Ran) Ran.Add(("kfs " + line, Environment.CurrentManagedThreadId));
            if (line == "explode")
            {
                Console?.WriteLine("about to fail");
                throw new InvalidOperationException("boom");
            }
            Console?.WriteLine("kfs " + line + ": done");
        }

        public void ExecutePurr(string line)
        {
            lock (Ran) Ran.Add(("purr " + line, Environment.CurrentManagedThreadId));
            Console?.WriteLine("purr " + line + ": done");
        }
    }

    private static BuildTarget Target(int thrusters) =>
        new(Runtime.Tables.BenchPart, LayoutGenerator.Vehicle("t", thrusters, 0, 0), $"vehicle t{thrusters}") { IsVehicle = true };

    private static InGameLanguageHost NewHost(FakeGameSide game, TimeSpan? timeout = null) =>
        new(Runtime.Tables, game, new InGameHostOptions { VerbTimeout = timeout ?? LspTestClient.Timeout, Log = LspLog.None });

    private static (FakeGameSide Game, InGameLanguageHost Host, OutputTeeInstallation Tee, StringWriter Console) WithConsole(TempWorkspace ws, TimeSpan? timeout = null)
    {
        TextWriter slot = new StringWriter();
        var console = (StringWriter)slot;
        OutputTeeInstallation tee = OutputTeeInstallation.Install(() => slot, w => slot = w, null);
        var game = new FakeGameSide { Root = ws.Root, Console = tee.Tee };
        InGameLanguageHost host = NewHost(game, timeout);
        host.Tee = tee.Tee;
        return (game, host, tee, console);
    }

    [Test]
    public void InGame_Target_Change_Rediagnoses()
    {
        using var ws = new TempWorkspace();
        var game = new FakeGameSide { Root = ws.Root };
        InGameLanguageHost host = NewHost(game);
        int events = 0;
        host.TargetChanged += () => events++;
        Assert.That(host.CurrentTarget.DisplayName, Is.EqualTo("bench"), "the bench until the first frame says otherwise");

        using var h = ServerHarness.Start(host);
        h.Client.Initialize();
        string uri = WorkspaceUris.FileUri(ws.Full("needs4.c"), OperatingSystem.IsWindows());
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
        game.Next = Target(1);
        host.Frame();
        Assert.That((events, host.CurrentTarget.DisplayName, host.CurrentTarget.IsVehicle), Is.EqualTo((1, "vehicle t1", true)));
        h.Client.Open(uri, text);
        h.Flush();
        Assert.That(Codes(h.Client.WaitForDiagnostics(uri)), Is.EqualTo(new[] { "KC-E014" }));

        // The same target, frame after frame — also as a new snapshot of the same objects: no event, no build.
        BuildTarget same = game.Next;
        game.Next = new BuildTarget(same.Part, same.Layout, same.DisplayName) { IsVehicle = true };
        int mark = h.Client.Mark;
        for (int i = 0; i < 3; i++) host.Frame();
        Assert.That((events, host.Target.Changes), Is.EqualTo((1, 1)));
        h.Builds.Flush();
        Assert.That(h.Builds.WaitForIdle(LspTestClient.Timeout), Is.True);
        h.Client.Sync();
        Assert.That(h.Client.Publishes(uri, mark), Is.Empty, "nothing was re-diagnosed");

        mark = h.Client.Mark;
        game.Next = Target(4);
        host.Frame();
        h.Builds.Flush();
        Assert.That(h.Client.WaitForDiagnostics(uri, null, mark)["diagnostics"]!.AsArray(), Is.Empty, "clean on four thrusters");
        Assert.That(h.Builds.ResultFor("needs4.c")!.Target.Layout.Fingerprint, Is.EqualTo(host.CurrentTarget.Layout.Fingerprint));

        mark = h.Client.Mark;
        game.Next = Target(2);
        host.Frame();
        h.Builds.Flush();
        Assert.That(Codes(h.Client.WaitForDiagnostics(uri, null, mark)), Is.EqualTo(new[] { "KC-E014" }), "an error again on two");
        Assert.That(events, Is.EqualTo(3));
    }

    [Test]
    public void A_Verb_That_Changes_The_Target_Publishes_It_Before_It_Answers()
    {
        // kfs.load decides --vehicle from CurrentTarget when the request arrives (any thread). An
        // editor sending kfs.target then kfs.load must get the new target without waiting for a
        // frame to refresh it: the verb's own run refreshes the snapshot before it answers.
        using var ws = new TempWorkspace();
        var game = new purrTTY.Kfs.Tools.Tests.Nvim.FakeGame { Root = ws.Root, Vehicle = Target(2) };
        var host = new InGameLanguageHost(Runtime.Tables, game, new InGameHostOptions { VerbTimeout = LspTestClient.Timeout, Log = LspLog.None });
        int events = 0;
        host.TargetChanged += () => events++;
        host.Frame();
        Assert.That(host.CurrentTarget.IsVehicle, Is.False);

        Task<CommandResult> target = host.RunVerb("target vehicle", CancellationToken.None);
        host.Frame();
        Assert.That(target.Wait(LspTestClient.Timeout), Is.True);
        Assert.That((host.CurrentTarget.IsVehicle, host.CurrentTarget.DisplayName, events), Is.EqualTo((true, "vehicle t2", 1)),
            "published by the verb's run, not a frame later");
        host.Frame();
        Assert.That(events, Is.EqualTo(1), "and not again");
    }

    [Test]
    public void RunVerb_Runs_In_The_Frame_Through_The_Tee()
    {
        using var ws = new TempWorkspace();
        (FakeGameSide game, InGameLanguageHost host, OutputTeeInstallation tee, StringWriter console) = WithConsole(ws);

        Task<CommandResult> build = host.RunVerb("build x_blink/blink.c", CancellationToken.None);
        Task<CommandResult> parts = host.RunPurr("parts lights", CancellationToken.None);
        Assert.That(build.IsCompleted || parts.IsCompleted, Is.False, "nothing runs before the frame");
        Assert.That(game.Ran, Is.Empty);

        host.Frame();
        Assert.That(build.Wait(LspTestClient.Timeout) && parts.Wait(LspTestClient.Timeout), Is.True);
        Assert.That(build.Result, Is.EqualTo(new CommandResult(true, "kfs build x_blink/blink.c: done\n")));
        Assert.That(parts.Result, Is.EqualTo(new CommandResult(true, "purr parts lights: done\n")));
        Assert.That(game.Ran.Select(r => r.Thread).Distinct(), Is.EqualTo(new[] { Environment.CurrentManagedThreadId }), "on the frame's thread");
        Assert.That(console.ToString().ReplaceLineEndings("\n"), Is.EqualTo("kfs build x_blink/blink.c: done\npurr parts lights: done\n"), "the console still prints it");

        // A verb that throws: the exception is the result, the frame goes on.
        Task<CommandResult> broken = host.RunVerb("explode", CancellationToken.None);
        Assert.DoesNotThrow(host.Frame);
        Assert.That(broken.Result, Is.EqualTo(new CommandResult(false, "about to fail\n", "kfs explode failed: InvalidOperationException: boom")));

        Assert.That(tee.Restore(), Is.EqualTo(OutputTeeRestore.Restored));
    }

    [Test]
    public void RunVerb_Fails_Cleanly_When_No_Frame_Comes_Or_The_Host_Shuts_Down()
    {
        using var ws = new TempWorkspace();
        (FakeGameSide game, InGameLanguageHost host, _, _) = WithConsole(ws, TimeSpan.FromMilliseconds(30));
        CommandResult late = host.RunVerb("start", CancellationToken.None).Result;
        Assert.That(late.Ok, Is.False);
        Assert.That(late.Error, Does.StartWith("kfs start: the game's main thread did not run the command within"));
        host.Frame();
        Assert.That(game.Ran, Is.Empty, "a timed-out command never runs");

        (game, host, _, _) = WithConsole(ws);
        Task<CommandResult> pending = host.RunVerb("stop", CancellationToken.None);
        host.Shutdown("purrTTY is unloading");
        Assert.That(pending.Result, Is.EqualTo(CommandResult.Refused("kfs stop: purrTTY is unloading")));
        Assert.That(host.RunVerb("reset", CancellationToken.None).Result.Error, Is.EqualTo("kfs reset: purrTTY is unloading"));
        host.Frame();
        Assert.That(game.Ran, Is.Empty);

        (game, host, _, _) = WithConsole(ws);
        using var cts = new CancellationTokenSource();
        Task<CommandResult> cancelled = host.RunVerb("start", cts.Token);
        cts.Cancel();
        Assert.Throws<AggregateException>(() => cancelled.Wait(LspTestClient.Timeout), "a cancelled request is cancelled (the connection answers RequestCancelled)");
        host.Frame();
        Assert.That(game.Ran, Is.Empty);
    }

    [Test]
    public void Workspace_Root_Changes_Are_Seen_Once()
    {
        using var ws = new TempWorkspace();
        using var other = new TempWorkspace();
        var game = new FakeGameSide { Root = ws.Root + Path.DirectorySeparatorChar };
        InGameLanguageHost host = NewHost(game);
        var roots = new List<string>();
        host.WorkspaceChanged += roots.Add;
        Assert.That(host.WorkspaceRoot, Is.EqualTo(Path.GetFullPath(ws.Root).TrimEnd(Path.DirectorySeparatorChar)));

        host.Frame();
        game.Root = ws.Root;   // another string, the same folder
        host.Frame();
        Assert.That(roots, Is.Empty);
        game.Root = other.Root;
        host.Frame();
        host.Frame();
        Assert.That(roots, Is.EqualTo(new[] { Path.GetFullPath(other.Root) }));
        Assert.That(host.WorkspaceRoot, Is.EqualTo(Path.GetFullPath(other.Root)));
    }

    [Test]
    public void A_Frame_With_Nothing_To_Do_Allocates_Nothing()
    {
        using var ws = new TempWorkspace();
        var game = new FakeGameSide { Root = ws.Root, Next = Target(3) };
        InGameLanguageHost host = NewHost(game);
        using var feed = new LiveFeed(() => null, new LiveFeedOptions { UseTimer = false, Log = LspLog.None });
        host.Feed = feed;
        for (int i = 0; i < 10; i++) host.Frame();   // warm: the first frame publishes the target

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) host.Frame();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(allocated, Is.Zero, "1000 idle frames");
    }

    [Test]
    public void Live_Program_Of_A_Real_Computer()
    {
        const string source = """
            #include <kfs.h>
            #pragma kfs tick_hz 10
            #pragma kfs task run rate_hz=10
            uint32_t n;
            uint32_t zero;
            uint32_t q;
            void run(void) {
                n = n + 1u;
                kfs_logf("tick %u", n);
                if (n == 4u) {
                    q = 10u / zero;
                }
            }
            """;
        int trapLine = source.Split('\n').ToList().FindIndex(l => l.Contains("10u / zero", StringComparison.Ordinal)) + 1;
        var (lines, before, after, wrongProgram) = RunBench(source);

        Assert.That(lines, Has.Some.Match(@"^\[k 0 t 0\.00\] loaded \d+ bytes on "), string.Join("\n", lines));
        Assert.That(lines, Has.Member("[k 0 t 0.00] tick 1"), string.Join("\n", lines));
        Assert.That(lines, Has.Member("[k 2 t 0.20] tick 3"), "formatted as the stock editor's console pane shows them");
        Assert.That(lines[^1], Does.StartWith("[k 3 t 0.30] trap DIV_ZERO at ").And.EndWith($"(run, main.c:{trapLine}): commands held"),
            "the trapping tick's own log line is dropped with its requests");
        Assert.That(before, Is.EqualTo(new LiveState("prog", "Standby", 0, 0, null)));
        Assert.That(after.State, Is.EqualTo("Halted"));
        Assert.That(after.Trap, Is.EqualTo(new LiveTrap("trap DIV_ZERO in task 0, function run", "main.c", "prog/main.c", trapLine)));
        Assert.That(wrongProgram.Program, Is.Null, "a program record of another image names nothing");
        Assert.That(wrongProgram.Trap!.Path, Is.Null, "and places nothing");
    }

    /// <summary>Builds and runs <paramref name="source"/> on a bench computer; returns the formatted log and three states.</summary>
    private static (List<string> Lines, LiveState Before, LiveState After, LiveState WrongProgram) RunBench(string source)
    {
        var bench = Runtime.CreateBench();
        BuildResult built = Runtime.BuildKc(source, Runtime.Tables.BenchLayout);
        Assert.That(built.Ok, Is.True, built.Text);
        Assert.That(bench.LoadBench(built.Image!).Ok, Is.True);
        var live = new HostComputerLive(bench) { Program = new LoadedProgram("prog", "prog", ["main.c"], built.Image) };
        ILiveProgram program = live;
        Assert.That((program.Name, program.LoadedImage), Is.EqualTo((bench.Name, built.Image)));
        LiveState before = live.ReadState();
        bench.Start();
        bench.Tick(6);
        var lines = new List<string>();
        long version = live.CopyLog(64, lines);
        Assert.That(version, Is.EqualTo(live.LogVersion));
        LiveState after = live.ReadState();
        live.Program = new LoadedProgram("other", "other", ["main.c"], [1, 2, 3]);
        LiveState wrong = live.ReadState();
        return (lines, before, after, wrong);
    }

    private static string[] Codes(JsonObject publish) =>
        publish["diagnostics"]!.AsArray().Select(d => d!["code"]!.GetValue<string>()).ToArray();
}
