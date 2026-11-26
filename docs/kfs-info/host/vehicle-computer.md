---
type: API Reference
title: VehicleComputer
description: VehicleComputer is the HostComputer subclass a vehicle carries; it schedules ticks against simulation time, publishes the host messages, latches thruster pulses, owns burn handles and the service override, and queues service requests for the game.
resource: ksa-decomp:KSA.Kfs.Host/VehicleComputer.cs
tags: [kfs, host, vehicle-computer, api, dispatch, tick, command-block]
sources:
  - id: vehicle-computer
    resource: ksa-decomp:KSA.Kfs.Host/VehicleComputer.cs
    title: VehicleComputer (decompiled)
  - id: host-computer
    resource: ksa-decomp:KSA.Kfs.Host/HostComputer.cs
    title: HostComputer (decompiled)
  - id: dispatch-result
    resource: ksa-decomp:KSA.Kfs.Host/DispatchResult.cs
    title: DispatchResult (decompiled)
  - id: thruster-commands
    resource: ksa-decomp:KSA.Kfs.Host/ThrusterCommands.cs
    title: ThrusterCommands (decompiled)
  - id: kfs-vehicle-host
    resource: ksa-decomp:KSA.Kfs/KfsVehicleHost.cs
    title: KfsVehicleHost (decompiled)
  - id: kfs-services
    resource: ksa-decomp:KSA.Kfs/KfsServices.cs
    title: KfsServices (decompiled)
  - id: kvm-machine
    resource: ksa-decomp:KSA.Kfs.Vm/KvmMachine.cs
    title: KvmMachine (decompiled)
generated: { by: claude-code/claude-fable-5-2, at: 2026-11-25T21:30:51Z }
status: stable
ksa_build: "2026.11.25.5949"
---

# Role

```csharp
public sealed class VehicleComputer : HostComputer, ISyscallHost
```

One `VehicleComputer` exists per vehicle that has a kFS host. The game constructs it as
`new VehicleComputer(tables, tables.BenchPart, vehicle.Id)` — every vehicle computer in this build
emulates the **bench part**, named after the vehicle id.[^kfs-vehicle-host] `FakeVehicle` builds
one too, for tests.

What it adds over [`HostComputer`](/host/host-computer.md):

- a [`TickGrid`](/host/tick-grid-and-timing.md) that maps simulation time to tick indices, with
  catch-up, skipping and a wall-clock deadline;
- the per-tick packing of host-published messages ([message bus](/host/message-bus.md));
- the control-mode decision (pilot vs full control) and the leave/handback flags
  ([ownership and control modes](/host/ownership-and-control-modes.md));
- thruster pulse extraction from the latched command block
  ([I/O layout generation](/host/io-layout-generation.md));
- `ISyscallHost` (burn-handle results), the vehicle service switch and the request queue the game
  drains ([service request handling](/host/service-request-handling.md)).

# Construction

```csharp
public VehicleComputer(GameTables tables, PartDescription part, string name)
    : base(tables, part, name)
{
    Machine.SyscallHost = this;
    PublishEveryTick = false;
}
```

[^vehicle-computer]

# Members

Line numbers are those of `VehicleComputer.cs`.

| member | lines | meaning |
| --- | --- | --- |
| `public const long MAX_CATCH_UP_NS = 1000000000L` | 59 | ticks more than 1 s overdue are skipped |
| `public BurnHandles Handles { get; }` | 67 | the vehicle's burn-handle table |
| `public ServiceOverride Override { get; }` | 69 | `set_throttle` / `engines_on` / `engines_off` override state |
| `public bool AwaitingStage` | 71 | `_stagesPending > 0` (volatile read) |
| `public TickGrid? Grid { get; private set; }` | 73 | `null` until started; `null` again after `Stop`/`Fault`/`Load` |
| `public long DispatchTimeNs { get; private set; }` | 75 | the `tNs` of the last `Dispatch` |
| `public bool UnderKfsControl` | 77–90 | `HostFault == null && !_killed && State ∈ { Boot, Running, Halted }` |
| `public ControlMode Mode { get; private set; }` | 92 | `Pilot` or `FullControl`, from the image |
| `public Blocks Owned { get; private set; }` | 94 | flight-software blocks the image owns |
| `public long SkippedTicks { get; private set; }` | 96 | ticks skipped (catch-up limit plus deadline) |
| `public long TickDeadline { get; set; } = long.MaxValue` | 98 | `Stopwatch` timestamp after which no more ticks run this dispatch |
| `public long DeadlineSkippedTicks { get; private set; }` | 100 | the deadline share of `SkippedTicks` |
| `public bool IsQuiescent` | 102–112 | halted, no latch for > 1 s, and no held command |
| `public bool FlightDataWanted` | 114 | image subscribes to `MSG_FLIGHT_DATA` (0x0102) without publishing it |
| `public bool WantsHostLeaves { get; private set; }` | 116 | image subscribes to any of 0x0402–0x0405 |
| `public string? HostFault { get; private set; }` | 118 | reason of the last host fault |
| `public long LateTicks`, `public long MaxLatenessNs` | 120–122 | lateness statistics |
| `public long PulsesSent { get; private set; }` | 124 | positive thruster pulses applied since start |
| `public ReadOnlySpan<double> LastPulses` | 126 | per-thruster pulse seconds of the last applied tick |
| `public ReadOnlySpan<byte> Applied => Latched` | 128 | the latched command block |
| `public IoLayout? AppliedLayout => Layout` | 130 | layout of `Applied` |
| `public PerfSeries HostPublishSeries { get; }` | 132 | time spent packing host messages |
| `public bool Killed` | 134 | kill switch state |
| `public Action? ImageLoaded { get; set; }` | 136 | invoked at the end of a successful `Load` |
| `public HostMessages? Messages` | 138 | the packer for this layout |
| `public IReadOnlyList<ushort> HostPublished` | 140 | ids the host delivers to this image |
| `protected override IReadOnlyCollection<ushort> HostPublishedIds` | 142 | same array, for the bus monitor |
| `public IReadOnlyList<HostMessage> LastHostMessages` | 144 | messages delivered on the last tick |

| method | lines |
| --- | --- |
| `public List<ServiceRequest> TakeServiceRequests()` | 153–162 |
| `public void RejectService(ServiceRequest request, string reason)` | 164–167 |
| `public void Event(string text)` | 169–172 |
| `void ISyscallHost.IssueResult(ushort service, ReadOnlySpan<uint> args, Span<uint> results)` | 174–183 |
| `protected override bool ApplyRequest(ServiceRequest request)` | 185–239 |
| `public void StageApplied()` | 259–267 |
| `public bool TakeCSharpReset()` | 288–293 |
| `public bool TakeHandback()` | 295–300 |
| `public void KillSwitch()` | 302–311 |
| `public override LoadResult Load(byte[] image, IoLayout layout)` | 313–350 |
| `public override void Start(ResetCause cause = ResetCause.PowerOn)` — **throws** | 352–355 |
| `public void Start(long nowNs, ResetCause cause = ResetCause.PowerOn)` | 357–360 |
| `public bool TryStart(long nowNs, ResetCause cause = ResetCause.PowerOn)` | 362–385 |
| `public void Fault(string reason)` | 387–406 |
| `public override void Stop()` | 408–413 |
| `public bool TickDue(long tNs)` | 430–441 |
| `public DispatchResult Dispatch(long tNs, Func<HostSample> sampler, bool vehicleControllable = true)` | 443–447 |
| `public DispatchResult Dispatch<TSampler>(long tNs, ref TSampler sampler, bool vehicleControllable = true) where TSampler : IHostSampler, allows ref struct` | 449–518 |
| `protected override MachineSnapshot Publishing(MachineSnapshot snapshot)` | 595–606 |

# `Load`

1. `ImageReader.Read`; if it succeeds, `Ownership.NeedsStock(image)` is consulted and a non-null
   answer fails the load with rule `"needs-stock"` (see
   [ownership and control modes](/host/ownership-and-control-modes.md)).
2. `base.Load(image, layout)` (host-message check, VM load, `State = Standby`).
3. Per-image vehicle state is rebuilt:[^vehicle-computer]
   - `HostFault = null`
   - `_sensors = new SensorBlock(layout)`, `_thrusters = new ThrusterCommands(layout)`,
     `_pulses = new double[_thrusters.Count]`
   - `_hostMessages = new HostMessages(Tables, layout)`, `_playerSlots.Clear()`
   - `_playerReadsFlightData` = `Machine.MessageRole(258)` is *subscribes and not publishes*
   - `_hostPublished` = the ids of `HostMessages.MsG3Order`
     (`257, 258, 1025, 1026, 1027, 1028, 1029`) that the image subscribes to and does not publish
   - `WantsHostLeaves = _hostPublished.Any(HostMessages.IsMsG3)` (ids 1026–1029)
   - `Mode = Ownership.ModeOf(image)`, `Owned = Ownership.Of(image)`
   - `Grid = null`, `_stagesPending = 0`
4. `ImageLoaded?.Invoke()`. The game sets this to `KfsVehicleHost.PrepareSampler`, which builds the
   leaf sampler when `WantsHostLeaves` is true.[^kfs-vehicle-host]

Because `Grid` becomes `null`, a freshly loaded vehicle computer is not under control until started.

# `Start`, `TryStart`, `Stop`, `Fault`, `KillSwitch`

- The inherited `Start(ResetCause)` is overridden to throw
  `InvalidOperationException("a vehicle computer starts at a simulation time: Start(nowNs)")`.
  `HostComputer.Reset()` calls that overload, so **`Reset()` on a `VehicleComputer` throws**; the
  workbench uses `TryStart(c.DispatchTimeNs, cause)` instead.
- `TryStart(nowNs, cause)`: with no image it logs `start ignored: no image loaded` (or
  `reset ignored: …`) and returns `false`. Otherwise it clears `HostFault`, calls
  `base.Start(cause)`, creates `Grid = new TickGrid(TickHz, TickGrid.AlignUp(nowNs))`, zeroes
  `_stagesPending`, the skip/lateness/pulse counters and `_pulses`, clears `_killed`, recomputes
  `Mode`/`Owned` and publishes.
- `Stop()`: `base.Stop()`, `Grid = null`, `LeaveControl()`.
- `Fault(reason)`: sets `HostFault`, `Grid = null`, raises the C# reset flag, clears `_flying`,
  raises the handback flag if the computer was controlling, clears `Override`, and calls
  `HaltByHost("host fault: <reason>; the C# flight computer flies the vehicle until Load, Start or Reset")`
  inside a `try/catch` that swallows any exception.
- `KillSwitch()`: when `State > Standby`, sets `_killed`, halts with
  `kill switch: the C# flight computer flies the vehicle (integration §9.3)` and leaves control.
  `_killed` is cleared only by `TryStart`.[^vehicle-computer]

`LeaveControl()` (private, 415–428) is the common exit: clear `Override`; if `_flying`, raise
`_csharpReset`; if `_controlled`, raise `_handback`; clear both.

# `TickDue`

```csharp
public bool TickDue(long tNs)
{
    if (!UnderKfsControl || Grid == null || AwaitingStage || Grid.NextTickNs > tNs + 1000)
        return false;
    if (TickDeadline != long.MaxValue)
        return Stopwatch.GetTimestamp() < TickDeadline;
    return true;
}
```

The game uses it only to label the dispatch for its profiler (`DispatchProfiler.BeginDispatch`);
`Dispatch` repeats the same tests itself.[^kfs-vehicle-host]

# `Dispatch<TSampler>`

`Dispatch` is called once per flight-computer step of the vehicle with the step's simulation time
in nanoseconds, a sampler, and whether the vehicle is controllable. The sampler is a generic
`IHostSampler` that may be a `ref struct`, so the game can pass by-reference physics state without
allocating.[^vehicle-computer]

1. `DispatchTimeNs = tNs`.
2. `DrainQueue()` (zone `DrainQueue`) — **always**, so queued `Load`/`TryStart`/`Stop` take
   effect here.
3. If `!UnderKfsControl || Grid == null || _sensors == null || _thrusters == null`:
   `LeaveControl()` and return `DispatchResult.NotControlled`.
4. `_controlled = true`. With `full = (Mode == ControlMode.FullControl)`:
   - `full && !vehicleControllable && _flying` → `_csharpReset = true; _flying = false`
   - `full && vehicleControllable` → `_flying = true`
5. `SkipOverdueTicks(tNs)`: if the oldest due tick is more than `MAX_CATCH_UP_NS` late,
   `Grid.SkipTo(tNs)` and log `skipped N tick(s): the oldest due tick was X s late, more than 1 s`.
6. Loop `while (!AwaitingStage && Grid.NextTickNs <= tNs + 1000)`:
   - if a deadline is set and has passed: `SkipPastDeadline(tNs)` (logs
     `skipped N tick(s): the computer had used its host time for this frame; lower time warp or make the program cheaper`)
     and stop;
   - record lateness (`tNs − NextTickNs`) in `MaxLatenessNs` and `LatenessSeries`; every tick after
     the first of a dispatch counts as a `LateTicks`;
   - `k = Grid.Take()`;
   - on the first tick only, `hostSample = sampler.Sample()` (zone `Sample`) — **all ticks of one
     dispatch share one sample**;
   - `PlayerTick(k, hostSample)` (zone `Tick`);
   - if that tick latched **and** `full` **and** `vehicleControllable`: `ApplyPlayer()`.
7. If any tick was applied, `_lastLatchNs = tNs`.
8. Return

   ```csharp
   new DispatchResult(UnderControl: true, Latched: applied, TicksRun: n,
       NextWakeSeconds: (AwaitingStage ? Grid.PeriodNs : Math.Max(0, Grid.NextTickNs - tNs)) / 1e9,
       Fallback: !vehicleControllable, Pilot: !full);
   ```

`PlayerTick` (554–573) packs the host messages into `_tickMessages`, calls
`RunTick(k, _sensors.Bytes, _tickMessages)` and then `AfterPlayerTick()`, which aborts burn handles
issued this tick whose request was not delivered.

`ApplyPlayer` (585–593) reads the thruster pulses from the latched command block:
`PulsesSent += _thrusters.Read(Latched, _pulses)`.

# `DispatchResult`

```csharp
public readonly record struct DispatchResult(bool UnderControl, bool Latched, int TicksRun,
    double NextWakeSeconds, bool Fallback = false, bool Pilot = false)
{
    public static readonly DispatchResult NotControlled =
        new DispatchResult(UnderControl: false, Latched: false, 0, double.PositiveInfinity);
}
```

[^dispatch-result]

| field | set to | consumer behaviour (`KfsVehicleHost.DispatchCore`) |
| --- | --- | --- |
| `UnderControl` | `false` only via `NotControlled` | `false` → the stock flight computer runs |
| `Latched` | a tick latched while in full control on a controllable vehicle | gates the write-back of pulses, throttles and gimbals |
| `TicksRun` | ticks executed this dispatch | refreshes `ActiveEnginePerformanceMax` when `> 0` |
| `NextWakeSeconds` | time to the next grid tick (or one period while awaiting a stage) | `output.RequestWakeup(…, PhysicsStepLimit.KfsTick)` |
| `Fallback` | `!vehicleControllable` | services still applied, then the stock computer runs |
| `Pilot` | `Mode != FullControl` | services applied, then `flightComputer.ComputeControl` with the override |

Note that `Latched` is never `true` in pilot mode, although the command block is still copied to
`Applied` on every latched VM tick (the base class does that).[^host-computer]

# The command block as seen from outside

- `Applied` / `Latched` is the host's copy of the VM's latched command block. It changes only
  when a tick latches — an overrun or trapped tick keeps the previous values ("commands held").
- One-shot fields (thruster pulses) are zeroed in the VM's working block after every tick, so a
  pulse appears in `Applied` for exactly the tick that wrote it.[^kvm-machine]
- `LastPulses` holds, per thruster, the pulse length in seconds read by `ThrusterCommands.Read`:
  `NaN` and values `<= 0` become `0`, everything else is clamped to
  `ThrusterCommands.WATCHDOG_CAP_SECONDS = 1.0`.[^thruster-commands]
- `Inferred:` when several ticks run in one dispatch, `_pulses` is overwritten per applied tick, so
  only the last applied tick's pulses reach the game (the game reads `LastPulses` once, after
  `Dispatch` returns).[^kfs-vehicle-host]
- Engine and gimbal commands are not decoded by the host layer; the game reads them from `Applied`
  at the offsets of its controller set (see [write-back](/integration/write-back.md)).

`IsQuiescent` uses `NoHeldCommand()` (269–286): every command field that is not one-shot must be
all zero in `Applied`.

# Staging gate

Service 32 (`stage`) increments `_stagesPending` when it is queued for the game; the game's main
thread calls `StageApplied()` after activating the next sequence, which decrements it (never below
zero). While `AwaitingStage` is true no tick runs and `NextWakeSeconds` is one period. `Load` and
`TryStart` reset the counter.[^vehicle-computer][^kfs-services]

# One-shot flags for the game

| flag | raised by | read with | game reaction |
| --- | --- | --- | --- |
| `_csharpReset` | `LeaveControl` while `_flying`; `Fault`; full control on a vehicle that stopped being controllable | `TakeCSharpReset()` | `KfsWriteBack.CSharpReset(fc)` |
| `_handback` | `LeaveControl` or `Fault` while `_controlled` | `TakeHandback()` | `KfsServices.HandBack(fc, pilotDone)` |

Both `Take*` methods return the flag and clear it; they are plain fields, read and written on the
dispatch thread.[^vehicle-computer][^kfs-vehicle-host]

# `Zones`

`Zones` is inherited (`HostComputer.Zones` → `Machine.Zones`, a `KSA.CpuProfiler`). The game assigns
`Computer.Zones = DispatchProfiler.Cpu` so the host's `TickZones` appear in its dispatch profiler;
when `null`, all `zones?.Begin/End` calls are skipped.[^kfs-vehicle-host]

# Snapshot decoration

`Publishing` adds `PulsesSent`, `Perf.LateTicks` and `Perf.MaxLatenessNs` to each snapshot.

# Modding notes

- **Public and usable without patching:** `Handles`, `Override`, `Applied`, `AppliedLayout`,
  `LastPulses`, `Grid`, `Mode`, `Owned`, `HostPublished`, `LastHostMessages`, `Messages`,
  `TakeServiceRequests`, `RejectService`, `Event`, `Enqueue` (inherited), `Machine` (inherited).
- **`ImageLoaded`** is a single `Action?` property, not an event. The game assigns it in the
  `KfsVehicleHost` constructor; a mod that wants a load notification must chain
  (`var old = c.ImageLoaded; c.ImageLoaded = () => { old?.Invoke(); mine(); }`) rather than replace.
- **`Machine.SyscallHost`** is a public settable property assigned in the constructor; it can be
  wrapped by a decorator that forwards to the computer (see
  [host extension surface](/host/host-extension-surface.md)).
- **Private state a mod might want:** `_cooked` (queued requests), `_hostPublished`,
  `_tickMessages`, `_sensors`, `_playerSlots`, `_stagesPending`.
- `TakeServiceRequests()` *moves* the list out; a second caller in the same dispatch gets the
  shared empty list. Do not call it from a mod — the game's `KfsServices.AfterDispatch` relies on it.
- Everything here runs on the dispatch thread except `StageApplied` (main thread, hence the
  interlocked counter) and `Enqueue`.

[^vehicle-computer]: VehicleComputer.cs — class :15; fields :27–65; constructor :146–151; `TakeServiceRequests` :153–162; `IssueResult` :174–183; `ApplyRequest` :185–239; `AfterPlayerTick` :241–257; `StageApplied` :259–267; `NoHeldCommand` :269–286; `TakeCSharpReset`/`TakeHandback` :288–300; `KillSwitch` :302–311; `Load` :313–350; `Start` overloads and `TryStart` :352–385; `Fault` :387–406; `Stop` :408–413; `LeaveControl` :415–428; `TickDue` :430–441; `Dispatch` :443–518; `SkipOverdueTicks` :520–541; `SkipPastDeadline` :543–552; `PlayerTick` :554–573; `ApplyPlayer` :585–593; `Publishing` :595–606.
[^host-computer]: HostComputer.cs — command block copied on a latched tick :434–438; `Reset()` calls `Start(ResetCause.Player)` :388–391.
[^dispatch-result]: DispatchResult.cs:3–6.
[^thruster-commands]: ThrusterCommands.cs:10, :27–42.
[^kfs-vehicle-host]: KfsVehicleHost.cs — constructor :105–118; `PrepareSampler` :120–126; `Dispatch` profiler label :446; `DispatchCore` :488–616.
[^kfs-services]: KfsServices.cs — stage work on the main thread :612–617.
[^kvm-machine]: KvmMachine.cs — one-shot fields cleared after each tick :2194–2198; latch on a tick without overrun or trap :2181–2193.
