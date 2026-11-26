---
type: Subsystem Reference
title: BenchMachine and FakeVehicle
description: The two off-vehicle hosts — BenchMachine, the workbench's free-running computer with zeroed sensors and no vehicle, and FakeVehicle, a test harness that runs a real VehicleComputer against a generated layout and scripted samples.
resource: ksa-decomp:KSA.Kfs.Host/BenchMachine.cs
tags: [kfs, host, bench, fake-vehicle, testing, workbench]
sources:
  - id: bench-machine
    resource: ksa-decomp:KSA.Kfs.Host/BenchMachine.cs
    title: BenchMachine (decompiled)
  - id: fake-vehicle
    resource: ksa-decomp:KSA.Kfs.Host/FakeVehicle.cs
    title: FakeVehicle (decompiled)
  - id: vehicle-target
    resource: ksa-decomp:KSA.Kfs.Host.Workbench/IVehicleTarget.cs
    title: IVehicleTarget (decompiled)
  - id: host-computer
    resource: ksa-decomp:KSA.Kfs.Host/HostComputer.cs
    title: HostComputer (decompiled)
  - id: workbench
    resource: ksa-decomp:KSA.Kfs.Host.Workbench/Workbench.cs
    title: Workbench (decompiled)
  - id: kfs-integration
    resource: ksa-decomp:KSA.Kfs/KfsIntegration.cs
    title: KfsIntegration (decompiled)
  - id: bench-layout
    resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.bench_layout.json
    title: bench_layout.json (embedded resource)
  - id: catalogue
    resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.catalogue.json
    title: catalogue.json (embedded resource)
  - id: offline-harness
    resource: /modding/offline-harness.md
    title: Offline harness experiments E1–E12
generated: { by: claude-code/claude-fable-5-2, at: 2026-11-25T21:30:51Z }
status: stable
ksa_build: "2026.11.25.5949"
---

# Two ways to run a program without a vehicle

| | `BenchMachine` | `FakeVehicle` |
| --- | --- | --- |
| base | `HostComputer` | owns a `VehicleComputer` |
| used by | the in-game workbench (`kfs` console commands, editor) | nothing in the shipped game code |
| layout | the embedded bench layout | `LayoutGenerator.Vehicle(name, thrusters, engines, gimbals)` |
| clock | its own tick counter | a `TickGrid` driven by caller-supplied times |
| inputs | zeroed sensor block, **no messages** | `HostSample`s from a callback |
| services | `log`, `trace`, `halt`, `sleep_*` only; everything else rejected | everything a real vehicle computer accepts, queued in `_cooked` |
| results of result-bearing services | zeros (no `ISyscallHost`) | burn handles |

# `BenchMachine`

```csharp
public sealed class BenchMachine : HostComputer
{
    public const long DEFAULT_MAX_CYCLES_PER_FRAME = 2000000L;
    public long MaxCyclesPerFrame { get; set; } = 2000000L;
    public bool Running { get; private set; }
    public ulong NextK { get; }
    public string? HostFault { get; private set; }
    public Exception? LastFault { get; private set; }

    public BenchMachine(GameTables tables);                        // part = tables.BenchPart
    public BenchMachine(GameTables tables, PartDescription part);
    public LoadResult LoadBench(byte[] image);                     // Load(image, Tables.BenchLayout)
    public override LoadResult Load(byte[] image, IoLayout layout);
    public override void Start(ResetCause cause = ResetCause.PowerOn);
    public override void Stop();
    public void PowerDown();
    public int Tick(int n);
    public int Tick(int n, long maxHostMs);
    public void Run(double seconds);
    public int Advance(double dtSeconds, double maxHostMs = double.PositiveInfinity);
}
```

[^bench-machine]

Both constructors name the computer `"bench"`.

## Behaviour

- **`Load`** calls `HostComputer.Load`; on success it allocates a zero-filled sensor array of
  `layout.SensorSize` bytes, resets `_nextK`, clears `Running` and `HostFault`. The bench layout has
  a 32-byte sensor block (an IMU) that therefore always reads as zeros, including `imu.valid`.[^bench-layout]
- **`Start`** clears `HostFault`, calls `HostComputer.Start` (VM reset, state `Boot`/`Running`/`Idle`)
  and zeroes `_nextK` and the time carry. It does not set `Running`: the machine is started but
  paused.
- **`Tick(n, maxHostMs)`** — single-stepping. Does nothing unless the computer has been started
  (`State > Standby`). Runs up to `n` ticks with `RunTick(_nextK++, _zeroSensors)`, stopping early
  when `maxHostMs` of wall-clock time has elapsed. Returns the number of ticks run.
- **`Run(seconds)`** — free-running. Sets `Running = true`; with `seconds > 0` it sets a stop tick
  `_nextK + round(seconds * TickHz)`, otherwise it runs until stopped.
- **`Advance(dtSeconds, maxHostMs)`** — called once per frame. Accumulates frame time, converts it
  to whole ticks (`floor(carry * TickHz + 1e-9)`), and runs them until one of: the stop tick is
  reached, the frame's `MaxCyclesPerFrame` (2 000 000 VM cycles) is spent, or — after at least one
  tick — `maxHostMs` has elapsed. Reaching the stop tick calls `Stop()`. The bench therefore runs
  in real time while it can and silently falls behind when the program is expensive.
- **`Stop()`** is overridden to *pause*: `Running = false` and the stop tick is cleared; it does
  **not** call `HostComputer.Stop`, so the state stays `Running`/`Idle`/`Halted`.
  **`PowerDown()`** pauses and then calls `HostComputer.Stop()` (state `Standby`).
- **Faults.** `Tick` and `Advance` catch every exception from `RunTick`, stop the run, store
  `LastFault` and `HostFault = "<ExceptionType>: <message>"` and halt the computer with
  `host fault: …; kfs load or kfs start runs the bench again`. The workbench reports it once as
  "a bug in kFS and not in your program".[^workbench]

## How the game drives it

The workbench owns one `BenchMachine` (`Workbench.Bench`), recreated when the selected part
changes. The per-frame hook `KfsIntegration.FrameHook.OnDrawUi` calls `Workbench.Frame(dt)`, which
calls `Bench.Advance(dt, RunHostMsPerFrame)`; when running inside the game `RunHostMsPerFrame` is
`4.0` and the single-step limit `TickHostMs` is `250`.[^kfs-integration][^workbench] The bench
therefore ticks on the UI thread. See [workbench](/tooling/workbench.md) and
[build and load flow](/tooling/build-and-load-flow.md).

## What a bench program can and cannot do

- It can compute, write the command block (visible in the snapshot's `LatchedCommands`), log,
  trace and halt.
- It receives **no messages**: `RunTick(k, sensors)` passes an empty list, so every subscribed slot
  stays zero with `hdr.seq == 0`. An image that publishes a host-only message is still rejected.
- Vehicle services are rejected with `rejected <name>: no vehicle to act on`; `plan_circularize` /
  `plan_burn` return handle 0.[^host-computer]
- The default bench part is `Bench`, based on "Flight Computer Mk2".[^catalogue]

# `FakeVehicle`

```csharp
public sealed class FakeVehicle : IVehicleTarget
{
    public GameTables Tables { get; }
    public string Id { get; }
    public IoLayout Layout { get; }
    public VehicleComputer Computer { get; }
    public float[][] Torques { get; set; }
    public List<(ulong K, long TNs, int Thruster, double Seconds)> Pulses { get; }
    public List<(long TNs, DispatchResult Result)> Dispatches { get; }
    public int Engines { get; }
    public int Gimbals { get; }

    public FakeVehicle(GameTables tables, int thrusters, string name = "fake",
                       PartDescription? part = null, int engines = 0, int gimbals = 0);
    public AssembleResult Assemble(string source, string file = "program.kasm");
    public void Boot(string source, long startNs = 0L);
    public void RunFrames(int frames, double dt, Func<long, TruthSample> sampleAt, long startNs = 0L);
    public void RunFramesWithCuts(int frames, double dt, Func<long, TruthSample> sampleAt, long startNs = 0L);
    public VehiclePropsValues Props();
    public DispatchResult Dispatch(long tNs, in TruthSample sample);
    public DispatchResult Dispatch(long tNs, Func<HostSample> sample);
}
```

[^fake-vehicle]

It is the smallest complete stand-in for `KfsVehicleHost`:

- The constructor generates a vehicle layout, creates
  `new VehicleComputer(tables, part ?? tables.BenchPart, name) { PublishEveryTick = true }` and gives
  every thruster the unit torque `(1, 0, 0)`.
- `Assemble` runs the kASM assembler against the computer's part and the layout; `Boot` assembles,
  loads and calls `Computer.Start(startNs)`, throwing `InvalidOperationException` on an assembly or
  load failure.
- `Dispatch(tNs, sample)` calls `Computer.Dispatch(tNs, sample)`, records the result in
  `Dispatches` and, when it latched, appends every positive entry of `Computer.LastPulses` to
  `Pulses`.
- The `TruthSample` overload builds the sample with `HostSample.FromTruth(sample, Props())`: only
  angular rates and attitude are meaningful; flight data is zero and there are no leaves, so
  `MSG_BURN_STATUS`, `MSG_PILOT_INPUT`, `MSG_FC_SETTINGS` and `MSG_BURN_PLAN` are never delivered.
- `Props()` reports mass 1, an identity inertia, the configured torques with zero force and
  `PropellantOk = true`, and zeroed engines and gimbals.
- `RunFrames` dispatches once per frame at `startNs + i * dt`. `RunFramesWithCuts` additionally
  honours `NextWakeSeconds`, re-dispatching inside a frame at each requested wake time — the way
  the game cuts physics steps at tick boundaries.

`FakeVehicle` never calls `TakeServiceRequests`, `TakeHandback` or `TakeCSharpReset`; a test reads
those from `Computer` itself.

## `IVehicleTarget`

```csharp
public interface IVehicleTarget
{
    string Id { get; }
    VehicleComputer Computer { get; }
    IoLayout Layout { get; }
    bool IsAlive => true;
}
```

The workbench talks to a vehicle only through this interface; `KfsVehicleHost` (the real one) and
`FakeVehicle` implement it.[^vehicle-target] `Workbench.ActiveVehicle` is a
`Func<IVehicleTarget?>` the game sets to "the controlled vehicle's host".[^kfs-integration]

`Inferred:` `FakeVehicle` is the developers' test harness, compiled into the game assembly; nothing
in the decompiled game constructs one. It does work as one: run outside the game against the
unmodified `KSA.dll`, a `FakeVehicle` served as the `ActiveVehicle` of the game's own `Workbench`
(build, `SelectTarget("vehicle")`, load, start, dispatch), and the program's log lines appeared in
`Computer.Console`. The same experiments showed that after a `kfs_stage()` request the computer
runs no further ticks until `Computer.StageApplied()` is called, which `FakeVehicle` never does —
see [the offline harness](/modding/offline-harness.md).[^offline-harness]

# Modding notes

- Both classes are public and sealed with public constructors, and both work without any game
  object: `GameTables.LoadEmbedded()` (or `KfsIntegration.Tables`) is the only dependency. That
  makes `FakeVehicle` a ready-made rig for testing a kFS extension headlessly — assemble a program,
  dispatch frames with hand-built `HostSample`s, inspect `Computer.Applied`,
  `Computer.TakeServiceRequests()` and the console.
- A mod that patches `VehicleComputer` or `HostComputer` methods affects `FakeVehicle` and the
  bench as well. A patch on `HostComputer.ApplyHostRequest` runs for bench programs; a patch on
  `VehicleComputer.ApplyRequest` does not.
- An extension that needs a vehicle (lights, animations) has nothing to act on at the bench. Decide
  deliberately what its services do there: with no patch they are rejected as
  `no vehicle to act on`, which is the honest answer.
- `FakeVehicle.Assemble` accepts kASM only; kC programs go through the compiler first (see
  [compile pipeline](/compiler/compile-pipeline.md)).

[^bench-machine]: BenchMachine.cs — constants and properties :11–29; constructors :31–39; `LoadBench`/`Load` :41–57; `Start` :59–65; `Stop`/`PowerDown` :67–77; `Tick` :79–110; `Fault` :112–125; `Run` :127–136; `Advance` :138–183.
[^fake-vehicle]: FakeVehicle.cs — properties :13–29; constructor :31–44; `Assemble` :46–49; `Boot` :51–64; `RunFrames` :66–73; `RunFramesWithCuts` :75–93; `Props` :95–100; `Dispatch` :102–124.
[^vehicle-target]: IVehicleTarget.cs:5–14.
[^host-computer]: HostComputer.cs — `RunTick(k, sensors)` passes no messages :404–407; rejection text :670–676.
[^workbench]: Workbench.cs — `Bench` :72, created :370, recreated per part :797–800; bench fault report :1136–1139; `Frame` → `Bench.Advance` :1168–1170.
[^kfs-integration]: KfsIntegration.cs — frame hook :15–37; workbench limits :108–112; `ActiveVehicle` :101–106.
[^bench-layout]: bench_layout.json — sensor fields.
[^catalogue]: catalogue.json — `bench_part`.
[^offline-harness]: Experiments E11 (Workbench with a `FakeVehicle` target, program log) and E12 (pending stage) in the offline harness document.
