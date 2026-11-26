---
type: API Reference
title: HostComputer
description: HostComputer is the base host-side computer that owns one KvmMachine, loads images, runs one tick at a time, drains the VM's service requests, keeps the console log and publishes thread-safe snapshots.
resource: ksa-decomp:KSA.Kfs.Host/HostComputer.cs
tags: [kfs, host, host-computer, api, tick, snapshot, syscall]
sources:
  - id: host-computer
    resource: ksa-decomp:KSA.Kfs.Host/HostComputer.cs
    title: HostComputer (decompiled)
  - id: computer-state
    resource: ksa-decomp:KSA.Kfs.Host/ComputerState.cs
    title: ComputerState (decompiled)
  - id: kvm-machine
    resource: ksa-decomp:KSA.Kfs.Vm/KvmMachine.cs
    title: KvmMachine (decompiled)
  - id: syscall-sink
    resource: ksa-decomp:KSA.Kfs.Vm/ISyscallSink.cs
    title: ISyscallSink (decompiled)
  - id: message-layout
    resource: ksa-decomp:KSA.Kfs.Vm.Tables/MessageLayout.cs
    title: MessageLayout (decompiled)
  - id: workbench
    resource: ksa-decomp:KSA.Kfs.Host.Workbench/Workbench.cs
    title: Workbench (decompiled)
  - id: image-reader
    resource: ksa-decomp:KSA.Kfs.Vm.Image/ImageReader.cs
    title: ImageReader (decompiled)
generated: { by: claude-code/claude-fable-5-2, at: 2026-11-25T21:30:51Z }
status: stable
ksa_build: "2026.11.25.5949"
---

# Role

`KSA.Kfs.Host.HostComputer` is the object every other layer talks to when it means "a kFS computer".
It wraps exactly one `KvmMachine` and adds everything the VM deliberately does not know about:
image loading policy, the tick pipeline around `KvmMachine.RunTick()`, service-request dispatch, the
console log, traces, watches, the bus monitor, performance series and an immutable
`MachineSnapshot` that UI threads may read.

```csharp
public class HostComputer : IMachineSnapshotSource, ISyscallSink
```

It is **not sealed** and has two subclasses, both sealed:[^host-computer]

| subclass | adds | doc |
| --- | --- | --- |
| `VehicleComputer` | tick grid against simulation time, host-published messages, pilot/full-control modes, burn handles, the game-facing request queue; also implements `ISyscallHost` | [VehicleComputer](/host/vehicle-computer.md) |
| `BenchMachine` | free-running bench clock with zeroed sensors, no vehicle | [BenchMachine and FakeVehicle](/host/bench-machine-and-fake-vehicle.md) |

`HostComputer` itself implements `ISyscallSink` (requests *after* a tick) but **not** `ISyscallHost`
(results *during* a tick). Only `VehicleComputer` sets `Machine.SyscallHost`.[^host-computer]

# Construction

```csharp
public HostComputer(GameTables tables, PartDescription part, string name)
```

- `Tables` (`protected readonly GameTables`), `Part`, `Name` are stored as given.
- `Machine = new KvmMachine(part, tables, 0) { Profiling = true }` — the computer index is the
  literal `0`; every `HostComputer` in this build is "computer 0", and VM profiling is always on.
- `_snapshot = MachineSnapshot.Empty(name, part.Name)`; `State` starts as its default value,
  `ComputerState.NoSoftware`.[^host-computer]
- The `KvmMachine` constructor runs the floating-point self-test and throws
  `UnsupportedHostException` on a big-endian host or a failing FP vector, so constructing a
  `HostComputer` can throw.[^kvm-machine]

# Public and protected surface

Line numbers are those of `HostComputer.cs`.

## Properties and constants

| member | lines | meaning |
| --- | --- | --- |
| `public const int OVERRUN_HISTORY_LENGTH = 64` | 75 | size of the overrun history ring |
| `public const long SNAPSHOT_WANTED_MS = 1000L` | 99 | documented lease length; the code uses the literal `1000` |
| `public long DispatchNsTotal` | 115 | `DispatchSeries.Total` |
| `public string Name { get; }` | 117 | vehicle id, `"bench"`, or the fake vehicle's name |
| `public PartDescription Part { get; }` | 119 | the processor part the VM emulates |
| `public KvmMachine Machine { get; }` | 121 | the wrapped VM — fully public |
| `public CpuProfiler? Zones { get; set; }` | 123–133 | forwards to `Machine.Zones` (host-CPU zone profiler) |
| `public ConsoleLog Console { get; }` | 135 | the program/host log, see [service request handling](/host/service-request-handling.md) |
| `public TraceStore Traces { get; }` | 137 | channels fed by the `trace` service |
| `public TraceStore Watches { get; }` | 139 | channels fed by watched globals |
| `public IReadOnlyList<GlobalLeaf> Leaves` | 141 | scalar leaves of the image's debug globals |
| `public BusTracker? Bus` | 143 | bus monitor, created at `Load` |
| `public ComputerState State { get; private set; }` | 145 | see "States" |
| `public KvmImage? Image` | 147 | `Machine.Image` |
| `public IoLayout? Layout` | 149 | `Machine.Layout` |
| `public int TickHz` | 151 | `Image?.TickHz ?? 0` |
| `public long TicksRun { get; private set; }` | 153 | ticks handed to the VM since `Load`/`Start` |
| `public long TicksLatched { get; private set; }` | 155 | ticks whose command block latched |
| `public ulong LastK { get; private set; }` | 157 | tick index of the last `RunTick` |
| `public HostTickResult? LastTick { get; private set; }` | 159 | result of the last `RunTick` |
| `public ReadOnlySpan<byte> Latched` | 161 | the host's copy of the latched command block |
| `public string? SourceName { get; set; }` | 163 | free-form label; not read inside `HostComputer` |
| `public HostRecorder? Recorder { get; set; }` | 165 | optional tick-input recorder |
| `public MachineSnapshot Snapshot` | 167–174 | renews the snapshot lease, then `Volatile.Read` |
| `public bool SnapshotIsStale` | 176 | a tick ran without publishing |
| `public bool PublishEveryTick { get; set; } = true` | 178 | `VehicleComputer` sets it `false` |
| `public ExecTrace? ExecTrace` | 194 | `Machine.ExecTrace` |
| `public byte[]? LoadedBytes` | 196 | the image bytes last loaded successfully |
| `protected virtual IReadOnlyCollection<ushort> HostPublishedIds` | 202 | empty here; overridden by `VehicleComputer` |
| `public IReadOnlyList<string> Watched` | 204–213 | copy of the watch list (locks `_watchGate`) |
| `public (uint Event, long Value)? SleepRequest { get; protected set; }` | 215 | last `sleep_until*` request, recorded only |
| `protected readonly PerfSeries VmSeries, HostSeries, PublishSeries, DispatchSeries, AllocSeries, CyclesSeries, BytecodeSeries, LatenessSeries` | 83–97 | see [snapshots and perf](/host/snapshots-and-perf.md) |

## Methods

| signature | lines | notes |
| --- | --- | --- |
| `public void WantSnapshot()` | 229–232 | lease: publish for the next 1000 ms |
| `public void WantRam()` | 234–245 | lease RAM copies in snapshots; enqueues a republish if the current snapshot has none |
| `public void WantBus()` / `public void WantBus(ushort messageId)` | 247–255 | lease bus slot bytes for all messages (`-1`) or one id |
| `public void WantExecTrace()` | 283–286 | lease VM execution tracing |
| `public void Enqueue(Action<HostComputer> action)` | 288–291 | thread-safe mailbox |
| `public void DrainQueue()` | 293–300 | run queued actions on the calling thread |
| `public virtual LoadResult Load(byte[] image, IoLayout layout)` | 302–344 | see "Loading an image" |
| `public virtual void Start(ResetCause cause = ResetCause.PowerOn)` | 346–379 | reset and boot |
| `protected void HaltByHost(string reason)` | 381–386 | `State = Halted`, log, publish |
| `public void Reset()` | 388–391 | `Start(ResetCause.Player)` |
| `public virtual void Stop()` | 393–402 | to `Standby` if above it |
| `public HostTickResult RunTick(ulong k, ReadOnlySpan<byte> sensors)` | 404–407 | no host messages |
| `public HostTickResult RunTick(ulong k, ReadOnlySpan<byte> sensors, IReadOnlyList<HostMessage> messages)` | 409–515 | the tick pipeline |
| `public bool Watch(string path)` / `public bool Unwatch(string path)` / `public void CopyWatched(List<string> into)` | 595–632 | watched globals |
| `void ISyscallSink.Request(ServiceRequest request)` | 670–676 | explicit interface implementation |
| `void ISyscallSink.Rejected(ServiceRejection r)` | 678–685 | logs an argument rejection |
| `void ISyscallSink.Dropped(int count)` | 687–693 | logs the per-tick request cap |
| `public static string ArgumentText(ServiceInfo svc, IReadOnlyList<uint> cells, int argument)` | 695–735 | formats one logical argument from its cells |
| `protected void RejectByHost(ServiceRequest request, string reason)` | 737–745 | count + log a host-side rejection |
| `private bool ApplyHostRequest(ServiceRequest request)` | 747–811 | hard-coded switch: services 1, 2, 3, 16, 17 |
| `protected virtual bool ApplyRequest(ServiceRequest request)` | 813–816 | returns `false`; the subclass hook |
| `public (string Function, string? File, int Line) TrapLocation(TrapInfo trap)` | 818–821 | |
| `public (string Function, string? File, int Line) Locate(uint pc)` | 823–826 | |
| `public static (string Function, string? File, int Line) Locate(KvmImage img, uint pc)` | 828–851 | pc → function and debug line |
| `public static string FunctionName(KvmImage img, uint start)` | 853–861 | debug name or `f_{start:x6}` |
| `protected void Publish(HostTickResult? tick)` | 863–936 | builds and swaps the snapshot |
| `protected virtual MachineSnapshot Publishing(MachineSnapshot snapshot)` | 938–941 | subclass decoration hook |
| `public void Republish()` | 943–946 | `Publish(LastTick)` |

# States

```csharp
public enum ComputerState { NoSoftware, Standby, Boot, Running, Idle, Halted }   // 0..5
```

[^computer-state]

| transition | condition | lines |
| --- | --- | --- |
| → `NoSoftware` | initial value; nothing sets it again | — |
| → `Standby` | `Load` succeeded | 335 |
| → `Boot` / `Idle` / `Running` | `Start`: `Machine.InBoot` → `Boot`; else `Machine.IsIdle` → `Idle`; else `Running` | 366–376 |
| → `Halted` | a tick reported a trap, or `machine.IsHalted` | 445–460 |
| → `Idle` | `TickResult.NothingLeftToRun` | 461–464 |
| → `Boot` | `TickResult.BootTick && !InitCompleted` | 465–468 |
| → `Running` | `TickResult.Ran || BootTick` | 469–472 |
| → `Halted` | `HaltByHost(reason)` (service `halt`, kill switch, host fault) | 381–386 |
| → `Standby` | `Stop()` when `State > Standby` | 393–402 |

Several call sites test `(uint)State > 1u`, i.e. "has been started" (`Boot`, `Running`, `Idle`,
`Halted`).[^host-computer]

# Loading an image

`Load(byte[] image, IoLayout layout)`:[^host-computer]

1. **Host-message pre-check.** `ImageReader.Read(image, Part, Tables, layout, out image2)` is run
   once; if it succeeds and any message entry has the publish role bit (`Role & 2`) for a message
   whose name is in `MessageLayout.HostPublished`, the load fails with rule `"host-message"` and
   the text `the program publishes <NAME>, which the host publishes (integration §7.4, FI §6.5)`.
   `MessageLayout.HostPublished` is `MSG_FLIGHT_DATA`, `MSG_VEHICLE_PROPS`, `MSG_BURN_STATUS`,
   `MSG_PILOT_INPUT`, `MSG_FC_SETTINGS`, `MSG_BURN_PLAN` — `MSG_NAV_STATE` is deliberately absent,
   a program may publish it.[^message-layout]
2. `Machine.LoadImage(image, layout)` validates again and installs the image. A failure is logged
   as `load rejected: <rule>: <message>` and returned; the previous image stays.
3. On success the host rebuilds its per-image state: `_latched = new byte[layout.CommandSize]`,
   `LoadedBytes`, RAM usage (`SramView.UsedBytes`), task entry names, `Leaves`
   (`GlobalLeaves.Leaves(img)`), `_bus = new BusTracker(Tables, img, layout)`, view data.
4. `State = Standby`, counters zeroed, and one `Console.Host` line:
   `loaded N bytes on <part>: tick_hz H, budget B cycles, T task(s), layout 0xFFFFFFFF`.
5. `Publish(null)`.

`Load` never starts the computer. It returns a `LoadResult(bool Ok, string Rule, string Message)`.
The image validator behind `Machine.LoadImage` is described in
[kVM image format](/compiler/kvm-image-format.md); among much else it rejects an image whose
`SYSCALL`s name a service id missing from `GameTables.Services` (rule `syscall`), whose messages
are missing from the catalogue or have a different `body_size` (rule `messages`), or whose layout
fingerprint differs from the layout it is loaded against.[^image-reader]

# Start, Reset, Stop, Halt

- `Start(cause)` throws `InvalidOperationException("no image loaded")` without an image. Otherwise
  `Machine.Reset(cause)`, clears `_latched`, resets all eight perf series and the view data, zeroes
  counters, derives `State` from the machine and logs `start: <State>` (or `reset: <State>` when
  `cause == ResetCause.Player`), then publishes.
- `Reset()` is `Start(ResetCause.Player)`.
- `Stop()` only acts when `State > Standby`: `State = Standby`, logs `stopped: Standby`, publishes.
  It does **not** reset the VM.
- `HaltByHost(reason)` sets `State = Halted`, logs `halted: <reason>` and publishes. It does not
  itself halt the VM; the `halt` service path calls `Machine.Halt()` first.[^host-computer]

`VehicleComputer` overrides `Load`, `Start` and `Stop`; its parameterless `Start` throws, see
[VehicleComputer](/host/vehicle-computer.md).

# The tick pipeline: `RunTick`

`RunTick(k, sensors, messages)` is the only place the VM executes. The host-CPU profiler zones
(`TickZones.*`) bracket each stage.[^host-computer]

| # | zone | work |
| --- | --- | --- |
| 1 | `WriteInputs` | `machine.TracingExecution = ExecTraceWanted`; `machine.SetSystem(k, 0u)`; `machine.WriteSensors(sensors)`; for each host message `machine.WriteMessage(id, slot)`; `Recorder?.Add(k, sensors, messages)` |
| 2 | `VmRunTick` | `TickResult r = machine.RunTick()` |
| 3 | `Requests` | `LastK = k`; `TicksRun++`; if `r.Latched`: `TicksLatched++` and `machine.ReadCommands(_latched)`; then `machine.DrainRequests(this)` |
| 4 | `BusMonitor` | `_bus?.AfterTick(machine, k, messages)` |
| 5 | `Views` | state transition; trap report (once); per-task cycle bookkeeping; `AfterTick` (overruns, NaN, watches); `Console.Advance(k, TickHz)` |
| 6 | `Publish` | `Publish(result)` if a snapshot is wanted or the state changed, else `_snapshotStale = true` |

Details worth knowing:

- `SetSystem(k, 0u)` — the SOI-change counter handed to the VM is the literal `0`.
- `WriteSensors` throws `ArgumentException` unless `sensors.Length == layout.SensorSize`;
  `WriteMessage` throws unless the machine is booted, the image subscribes to (and does not
  publish) the id, and the slot length is `16 + body_size`.[^kvm-machine]
- The command block is copied out **only on a latched tick**. A tick that overran or trapped
  leaves `Latched` at its previous contents.
- The VM clears its request list when a tick does not latch, so `DrainRequests` delivers requests
  only for latched ticks.[^kvm-machine]
- On the first trap the host logs
  `trap <Code> at 0x<Pc> in task <n> (<function>, <file>:<line>): commands held`; later ticks of
  the halted machine do not repeat it (`_trapReported`).
- An exception thrown by the VM or by a request handler propagates out of `RunTick`; the callers
  (`BenchMachine.Tick/Advance`, `KfsDispatch.TryComputeControl`) convert it to a host fault.

`AfterTick` (537–584) keeps: the overrun history (max 64 `OverrunEvent`s, with a log line on the
first overrun and at each power of ten), the first NaN (`NanSnapshot`, logged once), NaN-valued
float globals (`NanGlobal`), and one float sample per watched global in `Watches`.

`RunTick` also feeds the perf series: VM time, host time (everything around the VM), cycles,
bytecodes and bytes allocated on the calling thread.

# The mailbox: `Enqueue` and `DrainQueue`

`_queue` is a `ConcurrentQueue<Action<HostComputer>>`. `Enqueue` may be called from any thread;
`DrainQueue` dequeues and invokes each action with `this` on the calling thread.[^host-computer]

- `VehicleComputer.Dispatch` calls `DrainQueue()` first thing on every dispatch, even when the
  computer is not under kFS control. This is how UI-thread verbs reach a vehicle computer: the
  workbench enqueues `c.Load(image, layout)`, `TryStart(...)`, `c.Stop()`, memory writes and
  `Republish()`; the console's `kfs kill` enqueues `KillSwitch()`; `KfsServices` enqueues burn
  handle updates from the main thread.[^workbench]
- `HostComputer` itself enqueues a `Republish()` from `WantRam` and `WantBus` when the current
  snapshot lacks the wanted data.
- Nothing in `KSA.Kfs.Host` drains the queue of a `BenchMachine`. `Inferred:` the bench does not
  need it — the workbench ticks the bench from its per-frame hook (`Workbench.Frame` →
  `Bench.Advance`) and calls `Bench.Republish()` directly on that same thread.

# Snapshots and leases

`Publish` builds a fresh immutable `MachineSnapshot` and swaps it in with `Volatile.Write`; readers
take it with the `Snapshot` property. Publishing is not free, so it is demand-driven:[^host-computer]

- `PublishEveryTick` (`true` by default, `false` on a `VehicleComputer`) forces a publish per tick.
- Otherwise a publish happens when `Environment.TickCount64 <= _snapshotWantedUntil` or when the
  tick changed `State`. Reading `Snapshot` (or calling `WantSnapshot`) pushes the lease 1000 ms
  into the future.
- `WantRam`, `WantBus`, `WantExecTrace` are separate 1000 ms leases for the expensive parts: a
  full RAM copy, per-message slot bytes, and VM execution tracing.
- `SnapshotIsStale` tells a reader that ticks ran since the last publish; the workbench then
  enqueues `WantSnapshot(); Republish()` and waits.[^workbench]

The snapshot's contents are described in [snapshots and perf](/host/snapshots-and-perf.md).

# `ISyscallSink` in brief

```csharp
void ISyscallSink.Request(ServiceRequest request)
{
    if (!ApplyHostRequest(request) && !ApplyRequest(request))
        RejectByHost(request, "no vehicle to act on");
}
```

`ApplyHostRequest` is a private switch over services 1 (`log`), 2 (`trace`), 3 (`halt`),
16 (`sleep_until`) and 17 (`sleep_until_event`); `ApplyRequest` is the protected virtual a
subclass overrides. Everything else is rejected with the text above. The full path, including
`Rejected` and `Dropped`, is in [service request handling](/host/service-request-handling.md).[^host-computer][^syscall-sink]

# Watches and global leaves

`Leaves` is the flattened list of scalar globals from the image's debug info (see
[views](/host/views.md)). `Watch(path)` accepts only an existing leaf path and adds it to the watch
list; the tick thread resolves the list when its version changes and then appends
`(float)leaf.Read(machine)` per tick to `Watches`. `Unwatch` and `CopyWatched` complete the set.
The list is guarded by `_watchGate`; the resolved leaves are touched only by the tick thread.[^host-computer]

# Modding notes

- **Public:** `Machine`, `Console`, `Traces`, `Watches`, `Bus`, `Latched`, `Snapshot`, `Enqueue`,
  `DrainQueue`, `Load`, `Start`, `Stop`, `RunTick`, the `Want*` leases, the static helpers.
  `Enqueue` is the supported way to run code on the computer's tick thread without a patch.
- **Protected:** `Tables`, `RejectByHost`, `ApplyRequest` (virtual), `HaltByHost`, `Publish`,
  `Publishing` (virtual), `HostPublishedIds` (virtual), the perf series. Both subclasses are
  sealed, so the virtuals are reachable only through Harmony, not by subclassing a
  `VehicleComputer`.
- **Private:** `ApplyHostRequest`, `_latched`, `_queue`, `_bus`, all view bookkeeping.
- **No events or delegates** are raised by `HostComputer`; nothing notifies an observer of a tick,
  a request, a state change or a publish. `VehicleComputer.ImageLoaded` is the only delegate in the
  host layer and has a single slot the game already uses.
- **Threading:** `RunTick`, `Load`, `Start`, `Stop` and everything they touch run on whichever
  thread calls them and are not internally synchronised. For a vehicle computer that is the
  thread running `KfsVehicleHost.Dispatch` (see
  [dispatch and threading](/integration/dispatch-and-threading.md)). Cross-thread data is limited
  to the `Volatile` snapshot, the `Volatile` lease stamps, `ConsoleLog`/`TraceStore` (locked) and
  the concurrent queue.
- **Fixed assumptions:** computer index `0`; SOI change count `0`; `Profiling = true`.

[^host-computer]: HostComputer.cs — class declaration :17; constructor :217–227; `WantSnapshot`/`WantRam`/`WantBus`/`WantExecTrace` :229–286; `Enqueue`/`DrainQueue` :288–300; `Load` :302–344; `Start` :346–379; `HaltByHost` :381–386; `Reset` :388–391; `Stop` :393–402; `RunTick` :404–515; `AfterTick` :537–584; watches :595–668; `ISyscallSink` :670–693; `RejectByHost` :737–745; `ApplyHostRequest` :747–811; `ApplyRequest` :813–816; `Publish` :863–936; `Publishing` :938–941.
[^computer-state]: ComputerState.cs:3–11.
[^kvm-machine]: KvmMachine.cs — constructor and FP self-test :1845–1864; `WriteMessage` :368–384; `WriteSensors` :2028–2035; `ReadCommands` :2037–2040; `RunTick` clears requests when the tick did not latch :2199–2202; `DrainRequests` :2229–2246.
[^syscall-sink]: ISyscallSink.cs:3–14.
[^message-layout]: MessageLayout.cs:27.
[^workbench]: Workbench.cs — queued load :816–819; queued start :994–1001; queued stop :1009–1012; stale-snapshot wait :1202–1220.
[^image-reader]: ImageReader.cs — unknown service id :557–560; message not in the catalogue :655; `body_size` mismatch :684–687; layout fingerprint :777.
