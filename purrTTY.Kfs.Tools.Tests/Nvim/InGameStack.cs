using KSA.Kfs.Host;
using KSA.Kfs.Vm.Tables;
using purrTTY.Kfs.Lsp;
using purrTTY.Kfs.Lsp.Features;
using purrTTY.Kfs.Lsp.InGame;
using purrTTY.Kfs.Lsp.Live;
using purrTTY.Kfs.Runtime;

namespace purrTTY.Kfs.Tools.Tests.Nvim;

/// <summary>One line the fake game ran: the console line, the thread it ran on, what the probe read then.</summary>
/// <param name="Line"><c>kfs build x/a.c</c> or <c>purr parts lights</c>.</param>
/// <param name="Thread">The managed thread id (the simulated main thread is the test thread).</param>
/// <param name="Disk">What <see cref="FakeGame.DiskProbe"/> read when the line ran (the files the test cares about), or null.</param>
internal sealed record GameLine(string Line, int Thread, string? Disk);

/// <summary>
/// The game side of the in-game language host (<see cref="IKfsGameSide"/>), faked: a workspace
/// root, a target the test publishes (<see cref="Next"/>), and a console that records every
/// <c>kfs</c>/<c>purr</c> line and prints <see cref="Print"/>'s text through the output tee, as
/// <c>KfsConsoleCommands.Execute</c> prints through <c>KfsIntegration.Output</c>. <c>kfs target
/// vehicle|bench</c> also switches <see cref="Next"/> to <see cref="Vehicle"/> / <see cref="Bench"/>, as
/// the workbench does. No live program (<see cref="RefreshLive"/> is null): the feed reads a
/// <see cref="FakeLiveLog"/> instead.
/// </summary>
internal sealed class FakeGame : IKfsGameSide
{
    private readonly List<GameLine> _ran = [];

    public string Root { get; set; } = "";

    /// <summary>The target the next frame publishes (null: leave it).</summary>
    public BuildTarget? Next { get; set; }

    /// <summary>What <c>kfs target vehicle</c> switches to.</summary>
    public BuildTarget? Vehicle { get; set; }

    /// <summary>What <c>kfs target bench</c> switches to.</summary>
    public BuildTarget? Bench { get; set; }

    /// <summary>The console writer (the tee).</summary>
    public TextWriter? Console { get; set; }

    /// <summary>What a line prints (default: <c>&lt;line&gt;: done</c>).</summary>
    public Func<string, string> Print { get; set; } = line => line + ": done\n";

    /// <summary>Read when a line runs (to prove what was on disk then).</summary>
    public Func<string?>? DiskProbe { get; set; }

    public IReadOnlyList<GameLine> Ran
    {
        get { lock (_ran) return _ran.ToList(); }
    }

    public string WorkspaceRoot => Root;

    public void RefreshTarget(TargetSnapshot target)
    {
        if (Next is { } t) target.Refresh(t.Part, t.Layout, t.DisplayName, t.IsVehicle);
    }

    public HostComputerLive? RefreshLive() => null;

    public void ExecuteKfs(string line)
    {
        Record("kfs " + line);
        if (line == "target vehicle" && Vehicle is not null) Next = Vehicle;
        if (line == "target bench" && Bench is not null) Next = Bench;
        Console?.Write(Print("kfs " + line));
    }

    public void ExecutePurr(string line)
    {
        Record("purr " + line);
        Console?.Write(Print("purr " + line));
    }

    private void Record(string line)
    {
        string? disk = DiskProbe?.Invoke();
        lock (_ran) _ran.Add(new GameLine(line, Environment.CurrentManagedThreadId, disk));
    }
}

/// <summary>A program log and state the test writes (what <see cref="HostComputerLive"/> reads in game).</summary>
internal sealed class FakeLiveLog : ILiveSource
{
    private readonly object _gate = new();
    private readonly List<string> _log = [];
    private long _version;
    private LiveState _state = new(null, "NoSoftware", 0, 0, null);

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

    public long LogVersion
    {
        get { lock (_gate) return _version; }
    }

    public long CopyLog(int count, List<string> into)
    {
        lock (_gate)
        {
            into.Clear();
            into.AddRange(_log.Skip(Math.Max(0, _log.Count - count)));
            return _version;
        }
    }

    public LiveState ReadState() => State;
}

/// <summary>
/// The language-server stack the mod runs (<c>KfsLspService</c>), without the game: an
/// <see cref="InGameLanguageHost"/> over a <see cref="FakeGame"/>, the <see cref="LiveFeed"/> (with its
/// timer, over a <see cref="FakeLiveLog"/>), the <see cref="OutputTee"/> in a writer slot with the
/// feed as its sink, and an <see cref="LspServiceCore"/> whose connections get
/// <see cref="StandardFeatures.AddTo"/> + <see cref="HostFeatures.AddTo"/> — exactly the mod's
/// configure callback — the per-start token and the server info file
/// <c>&lt;config&gt;/.purrTTY/kfs-lsp.json</c>. <see cref="Restart"/> is <c>purr lsp restart</c> (a new
/// core with new options: port, token, <c>lsp_allow_vehicle_commands</c>). The game's main thread
/// is the test thread calling <see cref="Frame"/> (pass it as <see cref="NvimRun.Pump"/>).
/// </summary>
internal sealed class InGameStack : IDisposable
{
    /// <summary>The writer slot the tee is installed in (<c>KfsIntegration.Output</c> in game).</summary>
    private sealed class WriterSlot
    {
        public TextWriter Writer { get; set; } = new StringWriter();
    }

    private readonly List<string> _log;
    private readonly OutputTeeInstallation _tee;
    private readonly string _configDirectory;
    private LspServiceCore? _core;

    private InGameStack(List<string> log, FakeGame game, FakeLiveLog live, LiveFeed feed, InGameLanguageHost host, OutputTeeInstallation tee, string configDirectory)
    {
        _log = log;
        Game = game;
        Live = live;
        Feed = feed;
        Host = host;
        _tee = tee;
        _configDirectory = configDirectory;
    }

    public FakeGame Game { get; }
    public FakeLiveLog Live { get; }
    public LiveFeed Feed { get; }
    public InGameLanguageHost Host { get; }

    /// <summary>The running core (null before <see cref="StartService"/> and after a failed start).</summary>
    public LspServiceCore? Core => _core;

    /// <summary>The endpoint the mod hands the Neovim launcher (null while not running).</summary>
    public KfsLspEndpoint? Endpoint => _core?.Endpoint;

    /// <summary>The folder holding <c>.purrTTY/</c> (what the launcher passes as <c>PURRTTY_CONFIG_DIR</c>).</summary>
    public string ConfigDirectory => _configDirectory;

    public string InfoFile => Path.Combine(_configDirectory, ".purrTTY", LspServerInfoFile.FileName);

    /// <summary>Every log line of the service, the listener, the connections, the host and the feed.</summary>
    public string Log
    {
        get { lock (_log) return string.Join("\n", _log); }
    }

    public IReadOnlyList<string> LogLines
    {
        get { lock (_log) return _log.ToList(); }
    }

    /// <summary>Builds the stack (the service not started: <see cref="StartService"/>).</summary>
    public static InGameStack Create(GameTables tables, string workspaceRoot, string configDirectory, TimeSpan? verbTimeout = null)
    {
        var lines = new List<string>();
        LspLogSink sink = (level, message) => { lock (lines) lines.Add($"{level}: {message}"); };
        var game = new FakeGame { Root = workspaceRoot };
        var live = new FakeLiveLog();
        var feed = new LiveFeed(() => live, new LiveFeedOptions
        {
            Period = TimeSpan.FromMilliseconds(20), StateInterval = TimeSpan.FromMilliseconds(50), Log = sink,
        });
        var slot = new WriterSlot();
        OutputTeeInstallation tee = OutputTeeInstallation.Install(() => slot.Writer, w => slot.Writer = w, feed.WriteOutput);
        game.Console = tee.Tee;
        var host = new InGameLanguageHost(tables, game, new InGameHostOptions { VerbTimeout = verbTimeout ?? TimeSpan.FromSeconds(20), Log = sink })
        {
            Tee = tee.Tee,
            Feed = feed,
        };
        return new InGameStack(lines, game, live, feed, host, tee, configDirectory) { Sink = sink };
    }

    private LspLogSink Sink { get; init; } = LspLog.None;

    /// <summary>Starts the service as <c>KfsLspService.StartServer</c> does; true when it listens.</summary>
    public bool StartService(bool allowVerb = false, int port = 0)
    {
        if (_core is not null) throw new InvalidOperationException("already started");
        LspLogSink sink = Sink;
        var features = new HostFeatureOptions { AllowVerb = allowVerb, RunPurr = Host.RunPurr, Feed = Feed };
        _core = new LspServiceCore(Host, new LspServiceOptions
        {
            Port = port,
            InfoFilePath = InfoFile,
            KsaBuild = "v-test",
            Log = sink,
            Configure = server =>
            {
                StandardFeatures.AddTo(server);
                HostFeatures.AddTo(server, features);
            },
        });
        return _core.Start();
    }

    /// <summary><c>purr lsp restart</c>: stops the core (connections closed, info file removed) and starts a new one.</summary>
    public bool Restart(bool allowVerb = false, int port = 0)
    {
        _core?.Stop();
        _core = null;
        return StartService(allowVerb, port);
    }

    /// <summary>One frame of the game's UI phase (call on the test thread).</summary>
    public void Frame() => Host.Frame();

    /// <summary>A vehicle target with <paramref name="thrusters"/> thrusters (the build target's layout).</summary>
    public static BuildTarget VehicleTarget(GameTables tables, int thrusters) =>
        new(tables.BenchPart, LayoutGenerator.Vehicle("t", thrusters, 0, 0), $"vehicle t{thrusters}") { IsVehicle = true };

    /// <summary>The bench target.</summary>
    public static BuildTarget BenchTarget(GameTables tables) => new(tables.BenchPart, tables.BenchLayout, "bench");

    public void Dispose()
    {
        Host.Shutdown("the test ended");
        _core?.Stop();
        _core = null;
        Feed.Dispose();
        _tee.Restore();
    }
}
