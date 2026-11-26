using System.Buffers.Binary;
using KSA.Kfs.Host;
using KSA.Kfs.Host.Services;
using KSA.Kfs.Host.Views;
using KSA.Kfs.Vm;
using KSA.Kfs.Vm.Image;
using KSA.Kfs.Vm.Tables;
using purrTTY.Kfs.Runtime;

namespace purrTTY.Kfs.Testing;

/// <summary>
/// A simulated vehicle (plan §5.4): the game's own <c>FakeVehicle</c> — a real
/// <see cref="VehicleComputer"/> on a generated layout — with <see cref="SimVehicleParts"/> bound
/// in <see cref="PartsRegistry"/>, scripted telemetry (<see cref="Telemetry"/>,
/// <see cref="TelemetryScript"/>), program globals (<see cref="ReadGlobal{T}"/>), the machine's
/// state and the stock requests it made (<see cref="Requests"/>). Time is simulated: nothing here
/// sleeps or reads a clock.
/// </summary>
/// <remarks>
/// <para>One <see cref="Frame"/> mirrors the game's order: the <b>world steps</b> by the frame's
/// time (<see cref="SimVehicleParts.Step"/>: the physics job that ran since the last dispatch,
/// with the goals and switches as they were before this frame's pump — the game commits its
/// results at the start of the frame); the <b>pump</b> (<see cref="BindingPump.DrainAndApply"/>,
/// the code the game's main-thread pump runs) applies what the last ticks requested to the live
/// fields (a switch, a goal, a light's appearance); then one <b>dispatch</b>
/// (<c>VehicleComputer.Dispatch(timeNs, Func&lt;HostSample&gt;)</c>, as <c>FakeVehicle.Dispatch</c>) runs the ticks that are due, all
/// of them seeing one telemetry sample taken just before the first. Afterwards the stock requests
/// the computer accepted are taken and recorded, and each <c>kfs_stage()</c> is acknowledged
/// (<c>StageApplied</c>) so the program does not stall. While <see cref="Pause">paused</see>, a
/// frame runs the pump only — as the game, which never dispatches while paused — and simulated
/// time, the parts and the program stand still.</para>
/// <para>Burns: <c>kfs_plan_circularize</c> / <c>kfs_plan_burn</c> get their handle synchronously
/// from the game's own <see cref="VehicleComputer"/> (<c>BurnHandles.Issue</c>), and the plan and
/// <c>kfs_burn_start</c>/<c>kfs_burn_abort</c> are recorded in <see cref="Requests"/>. Nothing flies
/// them: no burn status is produced unless the test scripts <see cref="SimTelemetry.BurnStatus"/>,
/// and a plan stays "live" in the host's handle table (the host rejects a ninth live plan) until
/// <see cref="EndBurn"/>.</para>
/// <para>Fields hold class-typed game objects only, so test code may keep a <see cref="SimVehicle"/>
/// anywhere.</para>
/// </remarks>
public sealed class SimVehicle : IDisposable
{
    /// <summary>The default frame length: 1/60 s.</summary>
    public const double DefaultFrameDt = 1.0 / 60.0;

    private const ushort StageServiceId = 32;

    private readonly KfsRuntime _runtime;
    private readonly TelemetrySampler _sampler;
    private readonly List<ServiceRequest> _stockRequests = [];
    private readonly List<StockRequest> _requests = [];
    private IReadOnlyList<GlobalLeaf>? _leavesSource;
    private Dictionary<string, GlobalLeaf> _leaves = new(StringComparer.Ordinal);
    private int _frames;
    private Func<HostSample>? _sample;
    private bool _dispatched;
    private bool _disposed;

    internal SimVehicle(KfsRuntime runtime, SimVehicleSpec spec, Func<SimVehicleParts, IVehicleParts>? bind)
    {
        _runtime = runtime;
        Name = spec.Name;
        int thrusters = Math.Max(1, spec.Thrusters);
        Fake = new FakeVehicle(runtime.Tables, thrusters, spec.Name, engines: spec.Engines, gimbals: spec.Gimbals);
        _sampler = new TelemetrySampler(runtime.Tables, Fake.Layout, thrusters, spec.Engines, spec.Gimbals);
        Parts = new SimVehicleParts(spec);
        BoundParts = bind?.Invoke(Parts) ?? Parts;
        Binding = PartsRegistry.Bind(Fake.Computer, BoundParts);
    }

    /// <summary>The vehicle name.</summary>
    public string Name { get; }

    /// <summary>The game's stand-in vehicle.</summary>
    public FakeVehicle Fake { get; }

    /// <summary>The vehicle's computer.</summary>
    public VehicleComputer Computer => Fake.Computer;

    /// <summary>The vehicle's generated layout.</summary>
    public IoLayout Layout => Fake.Layout;

    /// <summary>The simulated parts (what <see cref="Frame"/> steps).</summary>
    public SimVehicleParts Parts { get; }

    /// <summary>The back end bound in the registry: <see cref="Parts"/> or the wrapper given at creation.</summary>
    public IVehicleParts BoundParts { get; }

    /// <summary>The computer's binding (queue, counters).</summary>
    public VehicleBinding Binding { get; }

    /// <summary>Simulated time of the last (or next, before the first frame) dispatch, in nanoseconds.</summary>
    public long TimeNs { get; private set; }

    /// <summary><see cref="TimeNs"/> in seconds.</summary>
    public double TimeS => TimeNs / 1e9;

    /// <summary>Frames run so far (paused frames included).</summary>
    public int FrameCount => _frames;

    /// <summary>The last build of <see cref="LoadKc(string, bool)"/> / <see cref="LoadKasm(string, bool)"/>.</summary>
    public BuildResult? LastBuild { get; private set; }

    /// <summary>Ticks run by the last frame's dispatch (0 for a paused frame).</summary>
    public int LastTicksRun { get; private set; }

    /// <summary>Ticks run by each frame so far, in order (0 for a paused frame).</summary>
    public List<int> TicksPerFrame { get; } = [];

    // ------------------------------------------------------------------ telemetry

    /// <summary>
    /// What the game publishes to the program at each dispatch while <see cref="TelemetryScript"/>
    /// is null: change its values between frames (or in a frame's <c>beforeDispatch</c>). The
    /// default is all zeros with identity attitudes, mass 1 kg and identity inertia (see
    /// <see cref="SimTelemetry"/> for what is and is not scriptable).
    /// </summary>
    public SimTelemetry Telemetry { get; set; } = new();

    /// <summary>
    /// A function of simulated time (seconds, <see cref="TimeS"/> of the dispatch) returning what
    /// the game publishes; called at most once per dispatch, only when a tick is due (as the game's
    /// sampler). Overrides <see cref="Telemetry"/> while set.
    /// </summary>
    public Func<double, SimTelemetry>? TelemetryScript { get; set; }

    /// <summary>The telemetry of the last sample taken, or null before the first.</summary>
    public SimTelemetry? LastTelemetry { get; private set; }

    /// <summary>Samples taken so far (one per dispatch that ran a tick).</summary>
    public int SamplesTaken { get; private set; }

    // ------------------------------------------------------------------ machine state

    /// <summary>The computer's state.</summary>
    public SimComputerState State => (SimComputerState)(int)Computer.State;

    /// <summary>The program's tick rate (0 before a load).</summary>
    public int TickHz => Computer.TickHz;

    /// <summary>Ticks the host ran since the last start (a halted computer still takes ticks; no code runs).</summary>
    public long TicksRun => Computer.TicksRun;

    /// <summary>The number of the last tick run, or null before the first.</summary>
    public ulong? LastTick => Computer.TicksRun > 0 ? Computer.LastK : null;

    /// <summary>Ticks abandoned because a task used the whole cycle budget (<c>sys.overruns</c>).</summary>
    public uint Overruns => Computer.Machine.OverrunCount;

    /// <summary>
    /// Requests rejected so far, by the VM at issue (argument domains) or by a host (<c>sys.rejected_requests</c>,
    /// current value; the program sees a host rejection from its next tick).
    /// </summary>
    public uint RejectedRequests
    {
        get
        {
            if (Computer.Machine.Image is null) return 0;
            // KvmMachine.SaveState: 32-byte header (version 1), then a 64-byte machine block whose
            // offset 44 holds the rejected-request counter.
            byte[] state = Computer.Machine.SaveState();
            ushort version = BinaryPrimitives.ReadUInt16LittleEndian(state.AsSpan(4));
            if (version != 1) throw new NotSupportedException($"KvmMachine state version {version} (expected 1): update SimVehicle.RejectedRequests");
            return BinaryPrimitives.ReadUInt32LittleEndian(state.AsSpan(32 + 44));
        }
    }

    /// <summary>The trap that halted the program, or null.</summary>
    public SimTrap? Trap
    {
        get
        {
            if (Computer.Machine.Trap is not { } trap) return null;
            (string function, string? file, int line) = Computer.TrapLocation(trap);
            return new SimTrap(trap.Code.ToString(), trap.Pc, trap.Task, function, file, line, trap.UserCode);
        }
    }

    /// <summary>A host fault (the computer was faulted by the host), or null.</summary>
    public string? HostFault => Computer.HostFault;

    /// <summary>True while a <c>kfs_stage()</c> waits for the game; the harness acknowledges each after its frame.</summary>
    public bool AwaitingStage => Computer.AwaitingStage;

    /// <summary>The program log: <c>kfs_logf</c> lines and host events.</summary>
    public List<LogLine> Log => Computer.Console.Snapshot();

    /// <summary>The program log's texts.</summary>
    public IReadOnlyList<string> LogText => Log.Select(l => l.Text).ToList();

    // ------------------------------------------------------------------ stock requests

    /// <summary>Requests the stock host accepted (stage, throttle, holds, burns …), as issued.</summary>
    public IReadOnlyList<ServiceRequest> StockRequests => _stockRequests;

    /// <summary><see cref="StockRequests"/> decoded with the services table, in the same order.</summary>
    public IReadOnlyList<StockRequest> Requests => _requests;

    /// <summary><see cref="Requests"/> as text (<c>stage</c>, <c>set_throttle(1.0)</c> …).</summary>
    public IReadOnlyList<string> RequestText => _requests.Select(r => r.ToString()).ToList();

    /// <summary>Plans delivered to the host and not ended (the host accepts at most 8).</summary>
    public int LiveBurnPlans => Computer.Handles.LivePlans;

    /// <summary>
    /// Ends burn <paramref name="handle"/> in the host's handle table, as the game does when a burn
    /// finishes (<paramref name="done"/>: DONE, completed) or is abandoned (ABORTED, cancelled).
    /// This frees a live-plan slot only; the program learns of it from <see cref="SimTelemetry.BurnStatus"/>.
    /// </summary>
    /// <returns>False when the handle is unknown or already ended.</returns>
    public bool EndBurn(uint handle, bool done = true)
    {
        ThrowIfDisposed();
        BurnHandle? h = Computer.Handles.Get(handle);
        return h is not null && Computer.Handles.End(h, done ? BurnStatusState.Done : BurnStatusState.Aborted,
            done ? BurnEndCause.Completed : BurnEndCause.Cancelled);
    }

    // ------------------------------------------------------------------ globals

    /// <summary>
    /// The leaf paths of the program's globals, as the game's own tooling names them
    /// (<c>GlobalLeaves</c>): <c>n</c>, <c>err.x</c>, <c>va[2]</c>, <c>props.engine[0].active</c>.
    /// Scalars only (float/double, bool, integers and enums of 1, 2, 4 or 8 bytes, pointers as
    /// 32-bit unsigned), struct members and array elements to any depth; at most 4096 leaves per
    /// program; only globals with debug information (every kC build here has it).
    /// </summary>
    public IReadOnlyCollection<string> GlobalPaths => Globals().Keys;

    /// <summary>
    /// Reads a program global by its leaf path (see <see cref="GlobalPaths"/>), straight from the
    /// VM's RAM: what the program has written so far (the value at the end of the last tick).
    /// A float leaf reads as <c>float</c> or <c>double</c>; an integer, enum, pointer or bool leaf as
    /// any integer type (checked) or <c>bool</c>, or as <c>float</c>/<c>double</c>.
    /// </summary>
    /// <exception cref="KeyNotFoundException">No such leaf (or no program loaded).</exception>
    /// <exception cref="InvalidCastException">The leaf cannot be read as <typeparamref name="T"/>.</exception>
    public T ReadGlobal<T>(string path) where T : unmanaged
    {
        if (TryReadGlobal(path, out T value)) return value;
        var known = Globals().Keys.Take(20).ToList();
        throw new KeyNotFoundException($"no global '{path}' in the loaded program" +
            (known.Count == 0 ? " (no program loaded, or no globals)" : $"; known: {string.Join(", ", known)}{(Globals().Count > known.Count ? ", …" : "")}"));
    }

    /// <summary>As <see cref="ReadGlobal{T}"/>, but false (and default) when there is no such leaf.</summary>
    /// <exception cref="InvalidCastException">The leaf exists but cannot be read as <typeparamref name="T"/>.</exception>
    public bool TryReadGlobal<T>(string path, out T value) where T : unmanaged
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(path);
        if (!Globals().TryGetValue(path, out GlobalLeaf? leaf))
        {
            value = default;
            return false;
        }
        Span<byte> raw = stackalloc byte[8];
        Computer.Machine.ReadRam((int)leaf.Address, raw[..leaf.Size]);
        value = GlobalValue.Convert<T>(path, leaf.Kind, leaf.Size, raw[..leaf.Size]);
        return true;
    }

    /// <summary>
    /// Writes a 4-byte program global (<c>uint32_t</c>, <c>int32_t</c>, <c>float</c>) by its leaf
    /// path straight into the VM's RAM, as the game's <c>kfs poke</c> does; the program sees it from
    /// its next tick.
    /// </summary>
    /// <exception cref="KeyNotFoundException">No such leaf.</exception>
    /// <exception cref="InvalidCastException">The leaf is not 4 bytes, or <typeparamref name="T"/> is not a 4-byte value.</exception>
    public void WriteGlobal<T>(string path, T value) where T : unmanaged
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(path);
        if (!Globals().TryGetValue(path, out GlobalLeaf? leaf)) throw new KeyNotFoundException($"no global '{path}' in the loaded program");
        if (leaf.Size != 4 || System.Runtime.CompilerServices.Unsafe.SizeOf<T>() != 4)
            throw new InvalidCastException($"global '{path}' is {leaf.Size} bytes; WriteGlobal writes 4-byte values only");
        Span<byte> raw = stackalloc byte[4];
        System.Runtime.InteropServices.MemoryMarshal.Write(raw, in value);
        Computer.Machine.WriteRam((int)leaf.Address, raw);
    }

    // ------------------------------------------------------------------ loading

    /// <summary>Builds one kC file against this vehicle's layout and loads it.</summary>
    /// <exception cref="KfsBuildException">The build failed (the message holds the compiler's diagnostics).</exception>
    public SimVehicle LoadKc(string source, bool start = true) =>
        LoadKc(new Dictionary<string, string>(StringComparer.Ordinal) { ["main.c"] = source }, start);

    /// <summary>Builds a kC project against this vehicle's layout and loads it.</summary>
    /// <exception cref="KfsBuildException">The build failed (the message holds the compiler's diagnostics).</exception>
    public SimVehicle LoadKc(IReadOnlyDictionary<string, string> files, bool start = true)
    {
        ThrowIfDisposed();
        BuildResult build = LastBuild = _runtime.BuildKc(files, Layout);
        if (!build.Ok) throw new KfsBuildException("kC", build);
        return LoadImage(build.Image!, start);
    }

    /// <summary>Assembles one kASM file (with <c>purrtty.kinc</c> available to <c>.include</c>) and loads it.</summary>
    /// <exception cref="KfsBuildException">The assembly failed.</exception>
    public SimVehicle LoadKasm(string source, bool start = true)
    {
        ThrowIfDisposed();
        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["program.kasm"] = source,
            [Catalog.XCatalog.KincName] = Catalog.CatalogEmitter.Kinc(Catalog.XCatalog.Catalog),
        };
        BuildResult build = LastBuild = _runtime.BuildKasm(files, Layout);
        if (!build.Ok) throw new KfsBuildException("kASM", build);
        return LoadImage(build.Image!, start);
    }

    /// <summary>
    /// Assembles a pure kASM project (file name inside the folder → text: every <c>.kasm</c> in
    /// name order, the rest reachable through <c>.include "…"</c>, as the game's Workbench builds a
    /// workspace folder) and loads it. Nothing is added to <paramref name="files"/>.
    /// </summary>
    /// <exception cref="KfsBuildException">The assembly failed.</exception>
    public SimVehicle LoadKasm(IReadOnlyDictionary<string, string> files, bool start = true)
    {
        ThrowIfDisposed();
        BuildResult build = LastBuild = _runtime.BuildKasm(files, Layout);
        if (!build.Ok) throw new KfsBuildException("kASM", build);
        return LoadImage(build.Image!, start);
    }

    /// <summary>Loads an image (and starts it at the current simulated time unless told not to).</summary>
    public SimVehicle LoadImage(byte[] image, bool start = true)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(image);
        LoadResult load = Computer.Load(image, Layout);
        if (!load.Ok) throw new InvalidOperationException("image rejected: " + load);
        if (start) Start();
        return this;
    }

    /// <summary>
    /// Starts the loaded program at the current simulated time — a new run (also when it is
    /// already running: as the game's <c>kfs start</c>, the machine is reset).
    /// </summary>
    public SimVehicle Start()
    {
        ThrowIfDisposed();
        Computer.Start(TimeNs);
        return this;
    }

    /// <summary>
    /// Restarts the loaded program without reloading it, as the game's <c>kfs reset</c> does
    /// (<c>VehicleComputer.TryStart(DispatchTimeNs, ResetCause.Player)</c>): a new run.
    /// </summary>
    /// <exception cref="InvalidOperationException">No image is loaded.</exception>
    public SimVehicle Reset()
    {
        ThrowIfDisposed();
        if (!Computer.TryStart(TimeNs, ResetCause.Player)) throw new InvalidOperationException("reset ignored: no image loaded");
        return this;
    }

    /// <summary>Stops the program, as the game's <c>kfs stop</c> (<c>VehicleComputer.Stop()</c>); <see cref="Start"/> begins a new run.</summary>
    public SimVehicle Stop()
    {
        ThrowIfDisposed();
        Computer.Stop();
        return this;
    }

    /// <summary>
    /// Bytes this thread allocated inside the last frame's dispatch (the game's
    /// <c>VehicleComputer.Dispatch</c>: the VM, the host, the Harmony hooks, the router and the
    /// telemetry sample), excluding the pump and the harness's own request decoding; 0 for a
    /// paused frame.
    /// </summary>
    public long LastDispatchAllocatedBytes { get; private set; }

    // ------------------------------------------------------------------ time

    /// <summary>True between <see cref="Pause"/> and <see cref="Resume"/>.</summary>
    public bool IsPaused { get; private set; }

    /// <summary>Pauses the simulation: frames run the pump only (commands already issued are applied), nothing ticks, time stands still.</summary>
    public SimVehicle Pause()
    {
        ThrowIfDisposed();
        IsPaused = true;
        return this;
    }

    /// <summary>Resumes the simulation.</summary>
    public SimVehicle Resume()
    {
        ThrowIfDisposed();
        IsPaused = false;
        return this;
    }

    /// <summary>
    /// One frame: step the parts by <paramref name="frameDt"/> (the time since the previous frame;
    /// the first dispatching frame dispatches at the start time without stepping), pump, dispatch.
    /// A command issued in frame N is applied by the pump of frame N+1 and moves the parts in the
    /// step of frame N+2, as in the game, where a dispatch reads the part states committed by the
    /// physics job before it, which ran with the goals the pump had set before that.
    /// Returns the ticks the dispatch ran. <paramref name="beforeDispatch"/> sees the world as the
    /// frame's ticks will. A paused frame runs the pump only and returns 0.
    /// </summary>
    public int Frame(double frameDt = DefaultFrameDt, Action<SimVehicle>? beforeDispatch = null)
    {
        ThrowIfDisposed();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frameDt);
        return FrameCore((long)Math.Round(frameDt * 1e9), frameDt, beforeDispatch);
    }

    /// <summary>Runs whole frames for <paramref name="seconds"/> of simulated time.</summary>
    public SimVehicle Run(double seconds, double frameDt = DefaultFrameDt, Action<SimVehicle>? beforeDispatch = null)
    {
        ThrowIfDisposed();
        int frames = (int)Math.Round(seconds / frameDt);
        for (int i = 0; i < frames; i++) Frame(frameDt, beforeDispatch);
        return this;
    }

    /// <summary>
    /// Runs frames of <paramref name="frameDt"/> until <paramref name="condition"/> holds (checked
    /// before the first frame and after each), for at most <paramref name="timeoutSeconds"/> of
    /// frame time. Returns the simulated time that passed (<see cref="TimeS"/> after minus before;
    /// the first frame of a vehicle dispatches at its start time and adds none). No wall-clock waiting.
    /// </summary>
    /// <exception cref="TimeoutException">The condition did not hold in time; the message ends with the program log.</exception>
    public double RunUntil(Func<bool> condition, double timeoutSeconds, double frameDt = DefaultFrameDt)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentOutOfRangeException.ThrowIfNegative(timeoutSeconds);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frameDt);
        int maxFrames = (int)Math.Ceiling(timeoutSeconds / frameDt - 1e-9);
        double startS = TimeS;
        for (int frame = 0; ; frame++)
        {
            if (condition()) return TimeS - startS;
            if (frame >= maxFrames)
                throw new TimeoutException($"condition not met within {timeoutSeconds} s of simulated time ({maxFrames} frames of {frameDt} s; state {State}, {TicksRun} ticks)\n{Dump()}");
            Frame(frameDt);
        }
    }

    /// <summary>
    /// Runs exactly <paramref name="ticks"/> ticks, one dispatch per tick at the tick's own time
    /// (the world steps to it). Ticks already overdue when it is called run together in its first
    /// dispatch and count toward <paramref name="ticks"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">Paused, or the computer stopped taking ticks (not loaded or started, faulted).</exception>
    public SimVehicle RunTicks(int ticks)
    {
        ThrowIfDisposed();
        ArgumentOutOfRangeException.ThrowIfNegative(ticks);
        if (IsPaused) throw new InvalidOperationException("the vehicle is paused: nothing ticks until Resume()");
        int ran = 0;
        while (ran < ticks)
        {
            TickGrid? grid = Computer.Grid;
            if (grid is null || !Computer.UnderKfsControl)
                throw new InvalidOperationException($"the computer takes no ticks (state {State}{(HostFault is null ? "" : ", host fault: " + HostFault)}) after {ran} of {ticks}\n{Dump()}");
            long dtNs = _dispatched ? Math.Max(0, grid.NextTickNs - TimeNs) : 0;
            ran += FrameCore(dtNs, dtNs / 1e9, null);
        }
        return this;
    }

    /// <summary>The program log as <c>k=… text</c> lines, for failure messages.</summary>
    public string Dump() => string.Join("\n", Log.Select(l => $"  k={l.K} {l.Text}"));

    /// <summary>Unbinds the computer from the registry; the vehicle is unusable afterwards.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        PartsRegistry.Unbind(Computer);
    }

    // ------------------------------------------------------------------ internals

    private int FrameCore(long dtNs, double dtS, Action<SimVehicle>? beforeDispatch)
    {
        if (IsPaused)
        {
            BindingPump.DrainAndApply(Binding);
            LastTicksRun = 0;
            LastDispatchAllocatedBytes = 0;
            TicksPerFrame.Add(0);
            _frames++;
            return 0;
        }
        // The physics job that ran since the last dispatch stepped the parts with the goals and
        // switches as they were then; the game commits its results before this frame's pump
        // applies what the last ticks requested (ApplyVehicleSolvers, then BeforeInputEvents).
        if (_dispatched)
        {
            TimeNs += dtNs;
            Parts.Step(dtS);
        }
        BindingPump.DrainAndApply(Binding);
        _dispatched = true;
        beforeDispatch?.Invoke(this);
        Func<HostSample> sample = _sample ??= Sample;
        long before = GC.GetAllocatedBytesForCurrentThread();
        // Computer.Dispatch is what FakeVehicle.Dispatch calls; calling it directly keeps
        // FakeVehicle's growing record of dispatches out of the allocation measurement.
        LastTicksRun = Computer.Dispatch(TimeNs, sample).TicksRun;
        LastDispatchAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        TicksPerFrame.Add(LastTicksRun);
        TakeStockRequests();
        _frames++;
        return LastTicksRun;
    }

    private HostSample Sample()
    {
        SimTelemetry t = TelemetryScript?.Invoke(TimeS) ?? Telemetry;
        LastTelemetry = t;
        SamplesTaken++;
        return _sampler.ToHostSample(t, Computer.HostPublished);
    }

    private void TakeStockRequests()
    {
        foreach (ServiceRequest request in Computer.TakeServiceRequests())
        {
            _stockRequests.Add(request);
            _requests.Add(StockRequest.Decode(_runtime.Tables, request, TimeS));
            // The game applies a stage on the main thread and then lets the computer tick again.
            if (request.Id == StageServiceId) Computer.StageApplied();
        }
    }

    private Dictionary<string, GlobalLeaf> Globals()
    {
        IReadOnlyList<GlobalLeaf> source = Computer.Leaves;
        if (!ReferenceEquals(source, _leavesSource))
        {
            var map = new Dictionary<string, GlobalLeaf>(StringComparer.Ordinal);
            foreach (GlobalLeaf leaf in source) map.TryAdd(leaf.Path, leaf);
            (_leaves, _leavesSource) = (map, source);
        }
        return _leaves;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}

/// <summary>Converts a global's raw bytes to the type a test asks for.</summary>
internal static class GlobalValue
{
    internal static T Convert<T>(string path, LeafKind kind, int size, ReadOnlySpan<byte> raw) where T : unmanaged
    {
        Type t = typeof(T);
        if (kind == LeafKind.Float)
        {
            double d = size == 8 ? BinaryPrimitives.ReadDoubleLittleEndian(raw) : BinaryPrimitives.ReadSingleLittleEndian(raw);
            if (t == typeof(double)) return (T)(object)d;
            if (t == typeof(float)) return (T)(object)(float)d;
            throw new InvalidCastException($"global '{path}' is a {size * 8}-bit float; read it as float or double, not {t.Name}");
        }

        bool signed = kind == LeafKind.Int;
        long s = 0;
        ulong u = 0;
        if (kind == LeafKind.Bool)
        {
            u = raw.IndexOfAnyExcept((byte)0) >= 0 ? 1u : 0u;
        }
        else if (signed)
        {
            s = size switch
            {
                1 => (sbyte)raw[0],
                2 => BinaryPrimitives.ReadInt16LittleEndian(raw),
                4 => BinaryPrimitives.ReadInt32LittleEndian(raw),
                _ => BinaryPrimitives.ReadInt64LittleEndian(raw),
            };
        }
        else
        {
            u = size switch
            {
                1 => raw[0],
                2 => BinaryPrimitives.ReadUInt16LittleEndian(raw),
                4 => BinaryPrimitives.ReadUInt32LittleEndian(raw),
                _ => BinaryPrimitives.ReadUInt64LittleEndian(raw),
            };
        }

        try
        {
            checked
            {
                if (t == typeof(bool)) return (T)(object)(signed ? s != 0 : u != 0);
                if (t == typeof(double)) return (T)(object)(signed ? s : (double)u);
                if (t == typeof(float)) return (T)(object)(signed ? s : (float)u);
                if (t == typeof(long)) return (T)(object)(signed ? s : (long)u);
                if (t == typeof(ulong)) return (T)(object)(signed ? (ulong)s : u);
                if (t == typeof(int)) return (T)(object)(signed ? (int)s : (int)u);
                if (t == typeof(uint)) return (T)(object)(signed ? (uint)s : (uint)u);
                if (t == typeof(short)) return (T)(object)(signed ? (short)s : (short)u);
                if (t == typeof(ushort)) return (T)(object)(signed ? (ushort)s : (ushort)u);
                if (t == typeof(sbyte)) return (T)(object)(signed ? (sbyte)s : (sbyte)u);
                if (t == typeof(byte)) return (T)(object)(signed ? (byte)s : (byte)u);
            }
        }
        catch (OverflowException)
        {
            throw new InvalidCastException($"global '{path}' = {(signed ? s.ToString(System.Globalization.CultureInfo.InvariantCulture) : u.ToString(System.Globalization.CultureInfo.InvariantCulture))} does not fit in {t.Name}");
        }
        throw new InvalidCastException($"global '{path}' ({kind}, {size} bytes) cannot be read as {t.Name}");
    }
}
