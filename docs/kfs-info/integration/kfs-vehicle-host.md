---
type: API Reference
title: KfsVehicleHost
description: The per-vehicle kFS host object — its fields, construction, liveness, the Dispatch and DispatchCore control flow branch by branch, sampling, fault handling and static state, with exact signatures and visibility.
resource: ksa-decomp:KSA.Kfs/KfsVehicleHost.cs
tags: [kfs, integration, host, dispatch, api]
sources:
  - id: kfs-vehicle-host
    resource: ksa-decomp:KSA.Kfs/KfsVehicleHost.cs
    title: KfsVehicleHost (decompiled)
  - id: kfs-dispatch
    resource: ksa-decomp:KSA.Kfs/KfsDispatch.cs
    title: KfsDispatch (decompiled)
  - id: kfs-dispatch-profiler
    resource: ksa-decomp:KSA.Kfs/KfsDispatchProfiler.cs
    title: KfsDispatchProfiler (decompiled)
  - id: kfs-integration
    resource: ksa-decomp:KSA.Kfs/KfsIntegration.cs
    title: KfsIntegration (decompiled)
  - id: kfs-console-commands
    resource: ksa-decomp:KSA.Kfs/KfsConsoleCommands.cs
    title: KfsConsoleCommands (decompiled)
  - id: vehicle-computer
    resource: ksa-decomp:KSA.Kfs.Host/VehicleComputer.cs
    title: VehicleComputer (decompiled)
  - id: vehicle-target
    resource: ksa-decomp:KSA.Kfs.Host.Workbench/IVehicleTarget.cs
    title: IVehicleTarget (decompiled)
  - id: dispatch-result
    resource: ksa-decomp:KSA.Kfs.Host/DispatchResult.cs
    title: DispatchResult (decompiled)
  - id: tick-zones
    resource: ksa-decomp:KSA.Kfs.Vm/TickZones.cs
    title: TickZones (decompiled)
  - id: physics-bubble
    resource: ksa-decomp:KSA/PhysicsBubble.cs
    title: PhysicsBubble (decompiled)
generated: { by: claude-code/claude-fable-5-2, at: 2026-11-25T21:37:08Z }
status: stable
ksa_build: "2026.11.25.5949"
---

# Declaration

```csharp
namespace KSA.Kfs;
public sealed class KfsVehicleHost : IVehicleTarget   // KSA.Kfs.Host.Workbench.IVehicleTarget
```

`IVehicleTarget` is what the workbench sees: `string Id`, `VehicleComputer Computer`, `IoLayout Layout`, and `bool IsAlive` (default
interface implementation `true`, overridden here).[^vehicle-target] All line numbers below are in `KfsVehicleHost.cs` unless another file is named.[^kfs-vehicle-host]

# Members

## Instance state

| Member | Declaration | Line | Notes |
| --- | --- | --- | --- |
| `Vehicle` | `public Vehicle Vehicle { get; }` | 61 | The live vehicle. |
| `Id` | `public string Id => Vehicle.Id;` | 63 | |
| `Computer` | `public VehicleComputer Computer { get; }` | 65 | `new VehicleComputer(tables, tables.BenchPart, vehicle.Id)`. |
| `Layout` | `public IoLayout Layout => Frozen.Layout;` | 67 | Never changes for the host's lifetime. |
| `Thrusters` / `Engines` / `Gimbals` | `public ThrusterController[] Thrusters => Frozen.Thrusters;` (and likewise) | 69-73 | The frozen arrays. |
| `Frozen` | `public KfsControllerSet Frozen { get; }` | 75 | Hardware at host construction. |
| `Current` | `public KfsControllerSet Current { get; private set; }` | 77 | Starts equal to `Frozen`; replaced on a detected hardware change. Only `DetectLayoutChange` reads it. |
| `Services` | `public KfsServices Services { get; }` | 79 | |
| `DispatchProfiler` | `public KfsDispatchProfiler DispatchProfiler { get; } = new KfsDispatchProfiler();` | 81 | |
| `IsAlive` | `public bool IsAlive { get; }` | 83-93 | `!Vehicle.IsDisposed && Vehicle.System == Universe.CurrentSystem && Vehicle.KfsHost == this`. |
| `DispatchAllocatedBytes` | `public long DispatchAllocatedBytes { get; private set; }` | 101 | Sum of `GC.GetAllocatedBytesForCurrentThread()` deltas over successful dispatches. |
| `Dispatches` | `public long Dispatches { get; private set; }` | 103 | Count of dispatches that did not throw. |
| `_tables` | `private readonly GameTables _tables;` | 37 | |
| `_offCount` | `private int _offCount = KfsPrototype.OffCount;` | 43 | Last seen flag-off counter. |
| `_handedBackAtOff` | `private int _handedBackAtOff;` | 45 | Set to 1 by `HandBackAtFlagOff` (main thread), consumed with `Interlocked.Exchange` in `DispatchCore`. |
| `_parentIdOf`, `_parentId` | `private string?`, `private uint` | 47-49 | One-entry cache of the parent body's CRC. |
| `_ticksThisDispatch` | `private int` | 51 | |
| `_budgetFrame`, `_frameHostTicks` | `private int _budgetFrame = -1;`, `private long` | 57-59 | Frame-budget accounting. |

## Static state

| Member | Declaration | Line | Notes |
| --- | --- | --- | --- |
| `AllHosts` | `internal static readonly List<KfsVehicleHost> AllHosts = new List<KfsVehicleHost>();` | 39 | Always accessed under `lock (AllHosts)`. |
| `FaultCount` | `public static int FaultCount => Volatile.Read(in _faultCount);` | 95 | |
| `LastFault` | `public static Exception? LastFault { get; private set; }` | 97 | |
| `FrameBudgetMs` | `public static double FrameBudgetMs { get; set; }` | 99 | 0 until `KfsIntegration.RegisterCommands(Program)` sets `4.0`.[^kfs-integration] |
| `DEFAULT_FRAME_BUDGET_MS` | `public const double DEFAULT_FRAME_BUDGET_MS = 4.0;` | 53 | |
| `_frame` | `private static int _frame;` | 55 | Main-thread frame counter. |

## Methods

| Method | Signature | Line | Thread |
| --- | --- | --- | --- |
| constructor | `private KfsVehicleHost(Vehicle vehicle, GameTables tables)` | 105-118 | main |
| `For` | `public static KfsVehicleHost For(Vehicle vehicle)` | 139-142 | main |
| `Find` | `public static KfsVehicleHost? Find(Vehicle vehicle)` | 144-147 | any |
| `ReleaseDead` | `internal static void ReleaseDead()` | 149-166 | main |
| `Fault` | `internal void Fault(Exception e, FlightComputer fc)` | 168-210 | dispatch |
| `HandBackAtFlagOff` | `public static void HandBackAtFlagOff()` | 212-228 | main |
| `ParentId` | `public static uint ParentId(string id)` | 276-279 | any |
| `Sample` | `public HostSample Sample(VehicleUpdateState vehicleState, ref readonly FlightComputerNavigation nav, ref FlightComputerOutput output)` | 281-295 | dispatch |
| `Dispatch` | `public bool Dispatch(VehicleUpdateState vehicleState, ref readonly FlightComputerNavigation nav, ref FlightComputerOutput output, bool controllable)` | 442-464 | dispatch |
| `BeginFrame` | `internal static void BeginFrame()` | 466-469 | main |
| `ThrottleCommand` / `BurnTimeCommand` / `GimbalCommand` | `internal static float ThrottleCommand(float latched)`, `internal static double BurnTimeCommand(float latched)`, `internal static float GimbalCommand(float latched)` | 618-643 | dispatch |
| `PrepareSampler` | `private void PrepareSampler()` | 120-126 | dispatch (it is `Computer.ImageLoaded`) |
| `SamplerFor` | `private KfsHostSampler SamplerFor(KfsControllerSet set)` | 128-137 | dispatch |
| `FullThrust` | `private static (Vec3d Force, Vec3d Torque) FullThrust(ThrusterController controller, float3 com, float ambientPa)` | 230-264 | dispatch |
| `FlightData` | `private static FlightDataValues FlightData(ref readonly FlightComputerNavigation nav)` | 297-360 | dispatch |
| `SampleFor` | `private HostSample SampleFor(KfsControllerSet set, VehicleUpdateState vehicleState, ref readonly FlightComputerNavigation nav, ref FlightComputerOutput output, NavTruth navTruth, FlightDataValues flight)` | 362-426 | dispatch |
| `DetectLayoutChange` | `private void DetectLayoutChange()` | 428-440 | dispatch |
| `TickDeadlineAt` | `private long TickDeadlineAt(long now)` | 471-486 | dispatch |
| `DispatchCore` | `private bool DispatchCore(VehicleUpdateState vehicleState, ref readonly FlightComputerNavigation nav, ref FlightComputerOutput output, bool controllable)` | 488-616 | dispatch |

Nested type: `private readonly ref struct StepSampler(KfsVehicleHost host, VehicleUpdateState vehicleState, ref readonly FlightComputerNavigation nav, ref FlightComputerOutput output) : IHostSampler`
(21-35). It holds `ref` fields to `nav` and `output` and its `Sample()` forwards to `_host.Sample(_vehicleState, in _nav, ref _output)`. It
exists so the computer can pull a sample lazily, only when a tick is actually due, without allocating a closure.

# Construction and lookup

```csharp
private KfsVehicleHost(Vehicle vehicle, GameTables tables)
{
    _tables = tables;
    Vehicle = vehicle;
    Frozen = (Current = new KfsControllerSet(vehicle));
    Computer = new VehicleComputer(tables, tables.BenchPart, vehicle.Id);
    Services = new KfsServices(this);
    Computer.ImageLoaded = PrepareSampler;
    Computer.Zones = DispatchProfiler.Cpu;
    lock (AllHosts) { AllHosts.Add(this); }
}

public static KfsVehicleHost For(Vehicle vehicle)
    => vehicle.KfsHost ?? (vehicle.KfsHost = new KfsVehicleHost(vehicle, KfsIntegration.Tables));

public static KfsVehicleHost? Find(Vehicle vehicle) => vehicle.KfsHost;
```

- `For` creates lazily and stores the host on `Vehicle.KfsHost`; it is not synchronised (it is only called from the main thread, via the
  workbench's `ActiveVehicle` delegate).[^kfs-integration]
- `Computer.ImageLoaded = PrepareSampler` builds the `HostLeafMap` and `KfsHostSampler` for the frozen set when an image that wants host
  leaves is loaded (`Computer.WantsHostLeaves`), so the first tick does not pay for it.
- `Computer.Zones = DispatchProfiler.Cpu` makes the VM and host record their `TickZones` into the host's profiler.

# Liveness and release

`IsAlive` (83-93) is false when the vehicle was disposed, when the current star system changed, or when the vehicle's `KfsHost` no
longer points at this host.

```csharp
internal static void ReleaseDead()
{
    lock (AllHosts)
        for (int i = AllHosts.Count - 1; i >= 0; i--)
        {
            KfsVehicleHost h = AllHosts[i];
            if (!h.IsAlive)
            {
                if (h.Vehicle.KfsHost == h) h.Vehicle.KfsHost = null;
                AllHosts.RemoveAt(i);
            }
        }
}
```

Callers: `KfsIntegration.BeforeInputEvents` (every frame while the flag is on) and `KfsConsoleCommands.SetFlag` when turning the flag off.[^kfs-integration][^kfs-console-commands]
There is no event, callback or `Dispose`: a dead host is simply dropped from the list and from `Vehicle.KfsHost`. See
[lifecycle](/integration/lifecycle-and-persistence.md).

# `Dispatch` (442-464)

```csharp
public bool Dispatch(VehicleUpdateState vehicleState, ref readonly FlightComputerNavigation nav,
                     ref FlightComputerOutput output, bool controllable)
{
    long timestamp = Stopwatch.GetTimestamp();
    Computer.TickDeadline = TickDeadlineAt(timestamp);
    DispatchProfiler.BeginDispatch(Computer.TickDue((long)nav.Time.Nanoseconds));
    _ticksThisDispatch = 0;
    long allocated = GC.GetAllocatedBytesForCurrentThread();
    bool result;
    try { result = DispatchCore(vehicleState, in nav, ref output, controllable); }
    catch { DispatchProfiler.CancelDispatch(); throw; }
    _frameHostTicks += Stopwatch.GetTimestamp() - timestamp;
    DispatchAllocatedBytes += GC.GetAllocatedBytesForCurrentThread() - allocated;
    Dispatches++;
    DispatchProfiler.EndDispatch(_ticksThisDispatch > 0);
    return result;
}
```

The return value is the answer to "did kFS compute control?": `true` suppresses the stock `FlightComputer.ComputeControl` call in
`PhysicsBubble.RunFlightComputer`.[^physics-bubble] The only caller is `KfsDispatch.TryComputeControl`, which passes
`readOnlyVehicle.IsControllable` as `controllable` and converts any exception into `Fault` + `false`.[^kfs-dispatch]
Frame-budget details (`TickDeadlineAt`, `BeginFrame`): [dispatch and threading](/integration/dispatch-and-threading.md).

# `DispatchCore` step by step (488-616)

`fc` below is `vehicleState.FlightComputer` — the **worker copy** of the flight computer.

| Step | Lines | What happens |
| --- | --- | --- |
| 1. Flag cycle | 491-502 | If `KfsPrototype.OffCount != _offCount` (the flag was turned off at least once since the last dispatch): remember the new count, `pilotDone = Interlocked.Exchange(ref _handedBackAtOff, 0) != 0`, and if `Computer.UnderKfsControl` call `Computer.Stop()` and log the event "stopped: kfs was turned off; kfs start boots the image again". |
| 2. Pre-dispatch | 504-510 | Only if `Computer.UnderKfsControl`: zone `Kfs.BeforeDispatch`; `DetectLayoutChange()`; `Services.BeforeDispatch(vehicleState, fc)`. |
| 3. Computer dispatch | 511-513 | `StepSampler sampler = new StepSampler(this, vehicleState, in nav, ref output); DispatchResult r = Computer.Dispatch<StepSampler>((long)nav.Time.Nanoseconds, ref sampler, controllable); _ticksThisDispatch = r.TicksRun;` — drains queued actions, runs due ticks, see [vehicle computer](/host/vehicle-computer.md). |
| 4. Requested resets | 514-521 | `if (Computer.TakeCSharpReset()) KfsWriteBack.CSharpReset(fc);` then `if (Computer.TakeHandback()) Services.HandBack(fc, pilotDone);` |
| 5. Not under control | 522-525 | `if (!r.UnderControl) return false;` |
| 6. Fallback | 526-533 | `wasAuto = fc.BurnMode == FlightComputerBurnMode.Auto`. If `r.Fallback` (the vehicle is not controllable): zone `Kfs.AfterDispatch`, `Services.AfterDispatch(in nav, fc, wasAuto, fallback: true)`, **`return false`** — the stock computer runs. |
| 7. Pilot mode | 534-560 | If `r.Pilot`: `Services.AfterDispatch(in nav, fc, wasAuto, fallback: false)`; zone `Kfs.FlightComputer`: `inputs = PhysicsBubble.FlightComputerInputsFor(vehicleState)`; apply `Computer.Override` (`ThrottleValid` → `inputs.EngineThrottle = Throttle`, `EngineValid` → `inputs.EngineOn = EngineOn`); `before = KfsServices.BeforeComputeControl(fc)`; **`fc.ComputeControl(in nav, in inputs, ref output)`**; `Services.AfterComputeControl(fc, before)`. Then, if `Computer.State != ComputerState.Halted`: `output.RequestWakeup(r.NextWakeSeconds, PhysicsStepLimit.KfsTick); output.AnyActuatorCommanded = true;`. **`return true`**. |
| 8. Full control: engine performance | 561-564 | `if (r.TicksRun > 0) fc.ActiveEnginePerformanceMax = KfsWriteBack.ActiveEnginePerformanceMax(fc, in output);` |
| 9. Full control: services | 565-567 | `Services.AfterDispatch(in nav, fc, wasAuto, fallback: false)` |
| 10. Full control: output | 568-574 | `output.Clear();` `if (Computer.IsQuiescent) return true;` `output.RequestWakeup(r.NextWakeSeconds, PhysicsStepLimit.KfsTick); output.AnyActuatorCommanded = true;` |
| 11. Full control: write-back | 575-614 | Only `if (r.Latched)`: zone `Kfs.WriteBack`; write thruster pulses, engine throttle + burn time, gimbal commands through `output.Thrusters/Engines/Gimbals.GetModulesAndNewStates(frozen.Present….AsSpan())`. See [write-back](/integration/write-back.md). |
| 12. | 615 | `return true` |

`DispatchResult` is `readonly record struct DispatchResult(bool UnderControl, bool Latched, int TicksRun, double NextWakeSeconds, bool Fallback = false, bool Pilot = false)`;
`VehicleComputer.Dispatch` sets `Fallback = !vehicleControllable` and `Pilot = Mode != ControlMode.FullControl`.[^dispatch-result][^vehicle-computer]

Branch summary:

| Condition | Stock `ComputeControl` runs? | Who commands actuators | Returns |
| --- | --- | --- | --- |
| Flag off / no host | yes (in `RunFlightComputer`) | stock | `false` (from `KfsDispatch`) |
| Computer not under kFS control | yes | stock | `false` |
| Under control, vehicle not controllable (`Fallback`) | yes | stock | `false` |
| Under control, pilot mode | yes, **called by kFS** with the service override applied | stock | `true` |
| Under control, full control | **no** | the program's latched command block | `true` |
| Exception anywhere | yes | stock | `false`, after `Fault` |

Order subtleties:

- `Services.BeforeDispatch` runs *before* queued actions are drained, so on the dispatch where a queued start takes effect it has not run.
- In the fallback branch services are still applied (attitude services still change the stock computer's settings), but the kFS wakeup is
  not requested: ticks then run at whatever cadence the stock computer's own wakeups produce.
- In pilot mode the stock `ComputeControl` begins with `outputs.Clear()`; kFS adds its wakeup afterwards, so the earlier of the stock
  and kFS wakeups wins.
- `wasAuto` is computed and passed to `Services.AfterDispatch` but the callee does not use it in this build.

# `DetectLayoutChange` (428-440)

```csharp
if (!Current.Matches(Vehicle))
{
    Frozen.MarkDeparted(Vehicle);
    if (Current != Frozen) Current.MarkDeparted(Vehicle);
    Current = new KfsControllerSet(Vehicle);
    Computer.Event($"layout change: {Frozen.InertCount} element(s) of the program's frozen layout are inert (integration §2.4)");
}
```

See [controller set and layout changes](/integration/controller-set-and-layout-changes.md).

# Sampling: `Sample`, `FlightData`, `SampleFor`

`Sample` (281-295) is public and is what `StepSampler` calls — at most once per `Computer.Dispatch`, and only if at least one tick runs
(`VehicleComputer.cs:496-501`).[^vehicle-computer]

1. Reads `nav.Parent`, `nav.PositionCci`, `nav.VelocityCci`, and `Vehicle.KinematicMeasurements.AccelerationBody` (the live vehicle's
   last published measurement).
2. Parent id: `ParentId(parent.Id)` = `Crc32.Compute(Encoding.UTF8.GetBytes(id))`, cached while the parent's id string instance is unchanged.
3. `NavTruth navTruth = new NavTruth(R: PositionCci, V: VelocityCci, Q: nav.Body2Cci, Rates: nav.BodyRates, Accel: accelerationBody, ParentId)`.
4. `FlightDataValues flight = Computer.FlightDataWanted ? FlightData(in nav) : FlightDataValues.Zero;`
5. `return SampleFor(Frozen, vehicleState, in nav, ref output, navTruth, flight);`

`SampleFor` (362-426) fills `VehiclePropsValues` from `vehicleState.GetReadOnlyProps()` and the frozen controller set (inert elements
report zeros), then:

- if `!Computer.WantsHostLeaves`: reuses one `HostSample` per set (`set.SampleBuffer`), overwriting `Nav`, `Flight`, `Props`;
- otherwise: `SamplerFor(set).Sample(vehicleState, in nav, PhysicsBubble.FlightComputerInputsFor(vehicleState), in output, vehicleState.FlightComputer, Universe.GetSimulationSpeed(), GameSettings.Current.Simulation.MaxFlightComputerRate)`
  and a new `HostSample(navTruth, flight, props) { Leaves = leaves }`.

Every value and its game source is tabulated in [host sampler](/integration/host-sampler.md).

# `Fault` (168-210)

Called by `KfsDispatch.TryComputeControl` on the dispatch thread when the dispatch threw.[^kfs-dispatch]

1. `Interlocked.Increment(ref _faultCount); LastFault = e;`
2. Logs at error level: "kFS host fault on vehicle '{Id}', so its computer is halted and the stock flight computer flies it: {e}".
3. `Computer.Fault(e.GetType().Name + ": " + e.Message)` — sets `HostFault`, clears the tick grid and the service override, requests a
   C# reset and (if the computer was in control) a handback, and halts the machine (`VehicleComputer.cs:387-406`).[^vehicle-computer]
4. `if (Computer.TakeCSharpReset()) KfsWriteBack.CSharpReset(fc);`
5. `if (Computer.TakeHandback())` → `Services.HandBack(fc, pilotDone: false)` inside its own `try`/`catch`, logging
   "kFS handback after a host fault on vehicle '{Id}' failed: …" if that throws.

A host fault is sticky until `Load`, `Start` or `Reset` (`UnderKfsControl` requires `HostFault == null`).

# `HandBackAtFlagOff` (212-228)

Main thread, from `kfs off`: snapshots `AllHosts` under the lock; for every host whose `Computer.UnderKfsControl` is true calls
`host.Services.HandBackPilot(host.Vehicle)` and `Volatile.Write(ref host._handedBackAtOff, 1)`. The flag makes the next `DispatchCore`
(after the flag is turned on again) pass `pilotDone = true` to `Services.HandBack`, so the pilot defaults are not applied twice. See
[feature flag](/integration/feature-flag.md).

# `KfsDispatchProfiler`

`public sealed class KfsDispatchProfiler : IProfilerSource` — a thin gate around a `CpuProfiler` named after `TickZones.Dispatch`
("Kfs.Dispatch").[^kfs-dispatch-profiler][^tick-zones]

| Member | Behaviour |
| --- | --- |
| `public readonly CpuProfiler Cpu` | The recorder; also assigned to `Computer.Zones`. |
| `public void Want()` | UI thread: "someone is looking" for the next `WANTED_MS = 1000` ms. |
| `public bool IsActive { get; set; }` | `Environment.TickCount64 <= _wantedUntil`; the setter is empty. |
| `public void Publish()` | UI thread: `_publishedFrameIndex = _completedFrameIndex`. |
| `public void BeginDispatch(bool tickDue)` | Dispatch thread: records a frame only when a tick is due, somebody wants it, and the next ring slot is not the published one. |
| `public void EndDispatch(bool ranTick)` | Ends the frame; marks it completed only if a tick ran, otherwise clears it. |
| `public void CancelDispatch()` | On exception: drop the frame. |
| `Frames`, `CurrentFrameIndex`, `LastCompleteFrameIndex`, `Paused`, `Name`, `ImDisplayName` | `IProfilerSource` plumbing for the debug window's flame graph. |

Zones recorded by the glue: `Kfs.BeforeDispatch`, `Kfs.AfterDispatch`, `Kfs.FlightComputer`, `Kfs.WriteBack`; by the host/VM:
`Kfs.DrainQueue`, `Kfs.Sample`, `Kfs.Tick` and its children.[^tick-zones]

# Modding notes

- **Public and directly callable:** `KfsVehicleHost.For`, `Find`, `HandBackAtFlagOff`, `FrameBudgetMs`, `FaultCount`, `LastFault`,
  `ParentId`, and on an instance `Vehicle`, `Computer`, `Frozen`, `Current`, `Services`, `Layout`, `IsAlive`, `Sample`, `Dispatch`.
  `For(vehicle)` is how a mod gives a vehicle a host without going through the workbench.
- **Internal:** `AllHosts`, `ReleaseDead`, `BeginFrame`, `Fault`, the three `*Command` clamps. **Private:** the constructor,
  `DispatchCore`, `SampleFor`, `FlightData`, `DetectLayoutChange`, `TickDeadlineAt`, `StepSampler`.
- **Enumerating hosts:** `AllHosts` must be read under `lock (KfsVehicleHost.AllHosts)` — copy to an array as kFS does.
- **Patch-friendly members:** `Sample` (public instance, returns the reference type `HostSample`, by-ref struct parameters) and `Dispatch`
  (public instance, returns `bool`). A Harmony patch declares the by-ref parameters as `ref FlightComputerNavigation nav` /
  `ref FlightComputerOutput output`. *Unverified:* neither patch was exercised against this build.
- **Patch-hostile members:** `DispatchCore` (private, but otherwise the same shape as `Dispatch`), and above all the generic
  `VehicleComputer.Dispatch<TSampler>` instantiated over the private `ref struct StepSampler` — a ref struct cannot be boxed into a
  Harmony `object` argument and the instantiation is specific to a private nested type. Patch `Sample` or `Dispatch` instead.
- **`For`, `Find`, `BeginFrame` and the `*Command` clamps are tiny** and are candidates for JIT inlining; do not rely on patching them.
  *Unverified:* actual inlining in this build was not observed.
- **No lifecycle callbacks exist.** To attach per-host state use a `ConditionalWeakTable<KfsVehicleHost, T>` (entries vanish with the
  host) and/or a postfix on `ReleaseDead`; see [patch targets and seams](/integration/patch-targets-and-seams.md).
- **Thread ownership:** everything a dispatch touches on the host (`_frameHostTicks`, `Current`, sampler caches, `Services` dispatch-side
  fields) is owned by the dispatch thread. From the main thread use `Computer.Enqueue` and `Computer.Snapshot` only.
- An exception from mod code inside `Dispatch` becomes a host fault for that vehicle (computer halted, sticky until reload/start/reset).

[^kfs-vehicle-host]: KfsVehicleHost.cs — line numbers given inline and in the tables.
[^vehicle-target]: IVehicleTarget.cs:5-14.
[^kfs-integration]: KfsIntegration.cs:101-105 (`ActiveVehicle` delegate), 151 (`FrameBudgetMs = 4.0`), 158-176 (`BeforeInputEvents`).
[^kfs-console-commands]: KfsConsoleCommands.cs:244-258 (`SetFlag`).
[^kfs-dispatch]: KfsDispatch.cs:7-28.
[^physics-bubble]: PhysicsBubble.cs:2067-2086.
[^dispatch-result]: DispatchResult.cs:3-6.
[^vehicle-computer]: VehicleComputer.cs — `Fault` 387-406, `Dispatch<TSampler>` 449-518 (lazy sample 496-501, result 516-517).
[^kfs-dispatch-profiler]: KfsDispatchProfiler.cs:8-100.
[^tick-zones]: TickZones.cs:3-40.
