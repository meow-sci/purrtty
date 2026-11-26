using System.Globalization;
using KSA.Kfs.Host;
using KSA.Kfs.Host.Services;
using KSA.Kfs.Vm;

namespace purrTTY.Kfs.Lsp.Live;

/// <summary>
/// The live program of one kFS computer (a vehicle's <c>VehicleComputer</c> or the bench machine):
/// <see cref="ILiveProgram"/> for the language host and <see cref="ILiveSource"/> for the feeds.
/// Uses only the computer's thread-safe members, so any thread may call it:
/// <list type="bullet">
/// <item><c>HostComputer.Console</c> — <c>ConsoleLog.Version</c> and <c>CopyTail</c> take the log's lock
/// (ConsoleLog.cs: <c>Version</c> :35-44, <c>CopyTail</c> :176-187);</item>
/// <item><c>HostComputer.Snapshot</c> — <c>WantSnapshot()</c> (a <c>Volatile.Write</c> of a deadline)
/// and a <c>Volatile.Read</c> of the immutable record the dispatching thread publishes
/// (HostComputer.cs:167-174, :229-232, :933-934). A vehicle computer publishes only while one is wanted
/// (VehicleComputer.cs:150, HostComputer.cs:180-190, :503): reading it keeps it publishing a
/// snapshot every tick for the next second, which is why the feeds read it only for a subscriber;</item>
/// <item><c>HostComputer.LoadedBytes</c> — a <c>Volatile.Read</c> (HostComputer.cs:196);</item>
/// <item><c>HostComputer.Traces</c> — <c>TraceStore.Channels</c> (:21) and <c>CopyLast</c> (:78) lock their store.</item>
/// </list>
/// <see cref="Program"/> is set by the main thread (the in-game host reads the workbench's last load).
/// </summary>
public sealed class HostComputerLive : ILiveProgram, ILiveSource
{
    private readonly HostComputer _computer;
    private readonly List<LogLine> _scratch = new(64);
    private LoadedProgram? _program;

    /// <summary>The live program of <paramref name="computer"/>.</summary>
    public HostComputerLive(HostComputer computer)
    {
        _computer = computer ?? throw new ArgumentNullException(nameof(computer));
    }

    /// <summary>The computer.</summary>
    public HostComputer Computer => _computer;

    /// <summary>The program the workbench loaded last (null: unknown). Applies only while the computer runs its image.</summary>
    public LoadedProgram? Program
    {
        get => Volatile.Read(ref _program);
        set => Volatile.Write(ref _program, value);
    }

    // ------------------------------------------------------------------ ILiveProgram

    /// <inheritdoc/>
    public string Name => _computer.Name;

    /// <inheritdoc/>
    public MachineSnapshot Snapshot => _computer.Snapshot;

    /// <inheritdoc/>
    public byte[]? LoadedImage => _computer.LoadedBytes;

    /// <inheritdoc cref="ILiveProgram.LogVersion"/>
    public long LogVersion => _computer.Console.Version;

    /// <inheritdoc/>
    public long CopyLogTail(int count, List<LogLine> into) => _computer.Console.CopyTail(count, into);

    /// <inheritdoc/>
    public IReadOnlyList<string> TraceChannels => _computer.Traces.Channels;

    /// <inheritdoc/>
    public void CopyTrace(string channel, int count, List<TraceSample> into) => _computer.Traces.CopyLast(channel, count, into);

    // ------------------------------------------------------------------ ILiveSource

    /// <inheritdoc/>
    public long CopyLog(int count, List<string> into)
    {
        ArgumentNullException.ThrowIfNull(into);
        into.Clear();
        lock (_scratch)
        {
            long version = _computer.Console.CopyTail(count, _scratch);
            foreach (LogLine line in _scratch) into.Add(LiveFormat.Line(line));
            _scratch.Clear();
            return version;
        }
    }

    /// <inheritdoc/>
    public LiveState ReadState()
    {
        MachineSnapshot snapshot = _computer.Snapshot;
        LoadedProgram? program = Program;
        if (program is not null && !ReferenceEquals(program.Image, _computer.LoadedBytes)) program = null;
        return LiveFormat.State(snapshot, program);
    }
}

/// <summary>The game's text for log lines and traps, and the snapshot → <see cref="LiveState"/> conversion.</summary>
public static class LiveFormat
{
    /// <summary>A log line as the stock editor's console pane and <c>kfs state</c> show it: <c>[k 12 t 1.20] text</c> (KfsEditorWindow.cs:2885-2893).</summary>
    public static string Line(LogLine line) =>
        string.Create(CultureInfo.InvariantCulture, $"[k {line.K} t {line.TimeS:0.00}] {line.Text}");

    /// <summary>
    /// A trap as <c>kfs state</c> describes it (Workbench.cs:1309-1327), without the address:
    /// <c>trap DIV_ZERO in task 0, function step</c>, plus <c>, code 7</c> for a user trap.
    /// </summary>
    public static string Trap(TrapSnapshot trap)
    {
        ArgumentNullException.ThrowIfNull(trap);
        string user = trap.Code == TrapCode.USER ? $", code {trap.UserCode}" : "";
        return string.Create(CultureInfo.InvariantCulture, $"trap {trap.Code} in task {trap.Task}, function {trap.Function}{user}");
    }

    /// <summary>The state of <paramref name="snapshot"/>; <paramref name="program"/> (when it describes the loaded image) names the program and places the trap.</summary>
    public static LiveState State(MachineSnapshot snapshot, LoadedProgram? program)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        LiveTrap? trap = null;
        if (snapshot.Trap is { } t)
        {
            int line = t.File is null ? 0 : Math.Max(0, t.Line);
            trap = new LiveTrap(Trap(t), t.File, line > 0 ? program?.PathOf(t.File) : null, line);
        }
        string? name = snapshot.State == ComputerState.NoSoftware ? null : program?.Name;
        return new LiveState(name, snapshot.State.ToString(), snapshot.K, snapshot.Overruns, trap);
    }
}
