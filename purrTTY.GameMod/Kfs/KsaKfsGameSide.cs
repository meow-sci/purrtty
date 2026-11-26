using KSA.Kfs;
using KSA.Kfs.Host;
using KSA.Kfs.Host.Workbench;
using KSA.Kfs.Vm.Tables;
using purrTTY.Kfs.Commands;
using purrTTY.Kfs.Lsp.InGame;
using purrTTY.Kfs.Lsp.Live;

namespace purrTTY.GameMod.Kfs;

/// <summary>
/// The game side of the in-game language host (<see cref="IKfsGameSide"/>, task C4): the only code of
/// the language server that touches the game. Every member runs on the main thread in the UI phase
/// (<c>TerminalMod.OnAfterUi</c> → <see cref="InGameLanguageHost.Frame"/>), where the stock console
/// runs commands and the only place the workbench may be touched. Member by member, against the
/// decompiled game (build 2026.11.25.5949):
/// <list type="bullet">
/// <item><c>KfsIntegration.Workbench</c> (KfsIntegration.cs:93-121) — creates the workbench on first
/// read (no file is touched: <c>new Workspace</c> only takes the full path, Workspace.cs:27-30; the
/// constructor also calls <c>KcFrontEnd.Register()</c>, Workbench.cs:364-389) and assigns
/// <c>Out = Output</c> on every read (the tee, once installed). Unsynchronised: main thread only.</item>
/// <item><c>Workbench.Workspace.Folder</c> (Workspace.cs:25) — a string property; read per frame, compared by reference.</item>
/// <item><c>Workbench.TargetVehicle</c> (Workbench.cs:82-96) — the vehicle target after its
/// <c>IsAlive</c> check; when the vehicle is gone it falls back to the bench and prints once
/// "vehicle … is gone: the target is the bench" (the stock editor's every-frame reads do the same).</item>
/// <item><c>IVehicleTarget.Computer</c> / <c>.Layout</c> (IVehicleTarget.cs; KfsVehicleHost.cs:63-67,
/// <c>Layout =&gt; Frozen.Layout</c>), <c>Workbench.Bench</c> (:72), <c>Workbench.Tables</c> (:68),
/// <c>HostComputer.Part</c> (HostComputer.cs:119), <c>Workbench.TargetName</c> (:113-129, a cached
/// string), <c>Workbench.Loaded</c> (:137) and <c>AssembleOutcome.Image</c> / <c>.Project</c> — plain
/// getters: the part, layout object and name are exactly what the stock editor captures before a
/// background build (<c>workbench.Target.Part</c>, <c>_vehicle?.Layout ?? tables.BenchLayout</c>,
/// Workbench.cs:372-377).</item>
/// <item><c>KfsConsoleCommands.Execute(string)</c> (KfsConsoleCommands.cs:192-232) — the console's own
/// entry, public; it catches the verbs' exceptions itself and prints through
/// <c>KfsIntegration.Print/Error</c>, i.e. <c>Output</c> (the tee).</item>
/// <item><c>PurrConsole.Execute(string)</c> — the mod's own <c>purr</c> command (never throws).</item>
/// </list>
/// Per frame with nothing changed: one workbench read, one <c>IsAlive</c> check per property read
/// of the vehicle target (two), reference compares; nothing is allocated.
/// </summary>
internal sealed class KsaKfsGameSide : IKfsGameSide
{
    private HostComputer? _computer;
    private HostComputerLive? _live;
    private AssembleOutcome? _loaded;
    private bool _loadedRead;

    /// <inheritdoc/>
    public string WorkspaceRoot => KfsIntegration.Workbench.Workspace.Folder;

    /// <inheritdoc/>
    public void RefreshTarget(TargetSnapshot target)
    {
        Workbench workbench = KfsIntegration.Workbench;
        IVehicleTarget? vehicle = workbench.TargetVehicle;
        HostComputer computer = vehicle is null ? workbench.Bench : vehicle.Computer;
        IoLayout layout = vehicle?.Layout ?? workbench.Tables.BenchLayout;
        target.Refresh(computer.Part, layout, workbench.TargetName, vehicle is not null);
    }

    /// <inheritdoc/>
    public HostComputerLive? RefreshLive()
    {
        Workbench workbench = KfsIntegration.Workbench;
        IVehicleTarget? vehicle = workbench.TargetVehicle;
        HostComputer computer = vehicle is null ? workbench.Bench : vehicle.Computer;
        if (!ReferenceEquals(computer, _computer))
        {
            _computer = computer;
            _live = new HostComputerLive(computer);
            _loadedRead = false;
        }
        AssembleOutcome? loaded = workbench.Loaded;
        if (!_loadedRead || !ReferenceEquals(loaded, _loaded))
        {
            _loaded = loaded;
            _loadedRead = true;
            _live!.Program = ProgramOf(loaded);
        }
        return _live;
    }

    /// <inheritdoc/>
    public void ExecuteKfs(string line) => KfsConsoleCommands.Execute(line);

    /// <inheritdoc/>
    public void ExecutePurr(string line) => PurrConsole.Execute(line);

    private static LoadedProgram? ProgramOf(AssembleOutcome? outcome)
    {
        if (outcome?.Project is not { } project) return null;
        return new LoadedProgram(project.Name, project.Folder, project.Files.Keys.ToArray(), outcome.Image);
    }
}
