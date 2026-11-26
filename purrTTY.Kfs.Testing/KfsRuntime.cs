using HarmonyLib;
using KSA.Kfs;
using KSA.Kfs.Compiler;
using KSA.Kfs.Host;
using KSA.Kfs.KAsm;
using KSA.Kfs.KC;
using KSA.Kfs.Vm.Tables;
using purrTTY.Kfs.Runtime;
using purrTTY.Kfs.Tables;

namespace purrTTY.Kfs.Testing;

/// <summary>
/// The game's real kFS (tables, kC compiler, kASM assembler, VM, host) with purrTTY's extension
/// installed, in an ordinary .NET process (plan §5.3). <see cref="Start"/> goes through the real
/// install path: a Harmony instance with a unique id, <see cref="KfsExtension.Install"/> (tables
/// published to <c>KfsIntegration.Tables</c>, host hooks, the optional bench hook,
/// <c>&lt;purrtty.h&gt;</c> and the builtin names), and <c>KcFrontEnd.Register()</c>; it fails
/// loudly when the extension does not come up active. <see cref="Dispose"/> unbinds every vehicle,
/// calls <see cref="KfsExtension.Uninstall"/> and resets <c>KfsIntegration._tables</c> to null
/// (testing only), so the next runtime in the process starts clean. Harmony patches and the header
/// statics are process-wide, so a process hosts one runtime at a time: a second <see cref="Start"/>
/// while one is alive throws, and every member that builds or creates something throws
/// <see cref="ObjectDisposedException"/> once the runtime is disposed.
/// </summary>
/// <remarks>Callers must have installed <see cref="KsaAssemblyResolver"/> first.</remarks>
public sealed class KfsRuntime : IDisposable
{
    private static readonly object Gate = new();
    private static KfsRuntime? _current;

    private readonly Harmony _harmony;
    private readonly List<SimVehicle> _vehicles = [];
    private GameTables? _stock;
    private bool _disposed;

    private KfsRuntime(KfsRuntimeOptions options)
    {
        if (KfsExtension.IsActive) throw new InvalidOperationException("the kFS extension is already installed in this process; uninstall it first");

        KcFrontEnd.Register();
        _harmony = new Harmony("purrtty.kfs.testing." + Guid.NewGuid().ToString("N"));
        KfsExtensionStatus status = KfsExtension.Install(_harmony, options.Extension);
        if (!status.IsActive) throw new InvalidOperationException("the kFS extension did not install:\n" + status.Describe());

        try
        {
            GameTables tables = KfsIntegration.Tables;
            if (tables.Origin != TableAugmenter.OriginFor(Catalog.XCatalog.Catalog))
                throw new InvalidOperationException($"KfsIntegration.Tables is not the augmented instance (origin {tables.Origin})");
            Tables = tables;
        }
        catch
        {
            KfsExtension.Uninstall();
            KfsIntegrationTables.Reset();
            throw;
        }
    }

    /// <summary>The runtime of this process, or null.</summary>
    public static KfsRuntime? Current
    {
        get { lock (Gate) return _current; }
    }

    /// <summary>Installs the extension into this process through <see cref="KfsExtension.Install"/>.</summary>
    /// <exception cref="InvalidOperationException">A runtime is already active, the extension is already installed, or the install was refused (the message holds the status).</exception>
    public static KfsRuntime Start(KfsRuntimeOptions? options = null)
    {
        lock (Gate)
        {
            if (_current is not null)
                throw new InvalidOperationException("a KfsRuntime is already active in this process (one per process: Harmony patches and the header statics are process-wide); use KfsRuntime.Current or dispose it first");
            _current = new KfsRuntime(options ?? new KfsRuntimeOptions());
            return _current;
        }
    }

    /// <summary>The extension's status now (live counters).</summary>
    public KfsExtensionStatus Extension => KfsExtension.Status;

    /// <summary>The tables with purrTTY's catalogue merged in: the instance <c>KfsIntegration.Tables</c> returns.</summary>
    public GameTables Tables { get; }

    /// <summary>Tables from the untouched stock streams, for comparisons (built on first use).</summary>
    public GameTables StockTables
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _stock ??= GameTables.Load(TableAugmenter.OpenStock, "test:stock");
        }
    }

    /// <summary>True once <see cref="Dispose"/> ran.</summary>
    public bool IsDisposed => _disposed;

    /// <summary>The id of the runtime's Harmony instance.</summary>
    public string HarmonyId => _harmony.Id;

    /// <summary>Compiles a kC project (file name → text) for the bench processor against <paramref name="layout"/>.</summary>
    public BuildResult BuildKc(IReadOnlyDictionary<string, string> files, IoLayout layout, GameTables? tables = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(layout);
        tables ??= Tables;
        CompileResult r = Compiler.Compile(new CompileInput
        {
            Files = files,
            Part = tables.BenchPart,
            Game = tables,
            Layout = layout,
            Options = new CompileOptions { EmitDebug = true },
        });
        return new BuildResult(r.Ok && r.Image is not null, r.Image, Convert(r.Diagnostics));
    }

    /// <summary>Compiles one kC file.</summary>
    public BuildResult BuildKc(string source, IoLayout layout, string file = "main.c") =>
        BuildKc(new Dictionary<string, string>(StringComparer.Ordinal) { [file] = source }, layout);

    /// <summary>
    /// Assembles a kASM project (file name → text) as the game's Workbench does: every
    /// <c>.kasm</c> file in name order, other files reachable through <c>.include "…"</c>.
    /// </summary>
    public BuildResult BuildKasm(IReadOnlyDictionary<string, string> files, IoLayout layout, GameTables? tables = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(layout);
        tables ??= Tables;
        var sources = files.Where(f => f.Key.EndsWith(".kasm", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f.Key, StringComparer.Ordinal)
            .Select(f => new SourceFile(f.Key, f.Value))
            .ToList();
        AssembleResult r = new Assembler(tables, tables.BenchPart, layout)
        {
            ProjectFiles = name => files.TryGetValue(name, out var text) ? text : null,
        }.Assemble(sources);
        return new BuildResult(r.Ok && r.Image is not null, r.Image, Convert(r.Diagnostics));
    }

    /// <summary>Assembles one kASM file.</summary>
    public BuildResult BuildKasm(string file, string text, IoLayout layout) =>
        BuildKasm(new Dictionary<string, string>(StringComparer.Ordinal) { [file] = text }, layout);

    /// <summary>
    /// A simulated vehicle: a game <c>FakeVehicle</c> on the augmented tables with
    /// <see cref="SimVehicleParts"/> bound in <see cref="PartsRegistry"/>. <paramref name="bind"/>
    /// may wrap the parts in another back end (fault injection); the wrapper is what gets bound.
    /// </summary>
    public SimVehicle CreateVehicle(SimVehicleSpec? spec = null, Func<SimVehicleParts, IVehicleParts>? bind = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var vehicle = new SimVehicle(this, spec ?? new SimVehicleSpec(), bind);
        lock (_vehicles) _vehicles.Add(vehicle);
        return vehicle;
    }

    /// <summary>
    /// Test sugar (plan §5.5): a simulated vehicle with that many default lights, deployables (2 s),
    /// actuators (1 s), engines, gimbals and thrusters. Chain <see cref="SimVehicle.LoadKc(string, bool)"/>
    /// to build, load and start a program in the same line; a failed build throws
    /// <see cref="KfsBuildException"/> with the compiler's diagnostics. Dispose the vehicle (<c>using</c>).
    /// </summary>
    public SimVehicle Vehicle(int lights = 0, int deployables = 0, int actuators = 0, int engines = 0,
        int gimbals = 0, int thrusters = 1, string name = "sim") =>
        CreateVehicle(SimVehicleSpec.With(lights, deployables, actuators, name, engines, gimbals, thrusters));

    /// <summary>The game's workbench computer on the augmented tables (no vehicle, no parts).</summary>
    public BenchMachine CreateBench()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new BenchMachine(Tables);
    }

    /// <summary>
    /// Unbinds every vehicle, uninstalls the extension (hooks, header, builtins, bindings), resets
    /// <c>KfsIntegration._tables</c> to null and frees the process for another runtime.
    /// </summary>
    public void Dispose()
    {
        lock (Gate)
        {
            if (_disposed) return;
            _disposed = true;
            lock (_vehicles)
            {
                foreach (SimVehicle vehicle in _vehicles) vehicle.Dispose();
                _vehicles.Clear();
            }
            KfsExtension.Uninstall();
            KfsIntegrationTables.Reset();
            if (ReferenceEquals(_current, this)) _current = null;
        }
    }

    private static List<BuildDiagnostic> Convert(IEnumerable<Diagnostic> diagnostics) =>
        diagnostics.Select(d => new BuildDiagnostic(d.File, d.Line, d.Col, d.Code,
            d.Severity == Severity.Error ? BuildSeverity.Error : BuildSeverity.Warning, d.Message, d.Hint)).ToList();
}
