---
type: API Reference
title: Snapshots and performance data
description: The immutable MachineSnapshot a HostComputer publishes for other threads and everything inside it — task, function, trap, NaN and last-run records, performance series and reports — plus the tick recorder and the RAM view helpers.
resource: ksa-decomp:KSA.Kfs.Host/MachineSnapshot.cs
tags: [kfs, host, snapshot, perf, profiler, recorder, sram]
sources:
  - id: machine-snapshot
    resource: ksa-decomp:KSA.Kfs.Host/MachineSnapshot.cs
    title: MachineSnapshot (decompiled)
  - id: snapshot-records
    resource: ksa-decomp:KSA.Kfs.Host/TaskSnapshot.cs
    title: TaskSnapshot, FunctionSnapshot, TrapSnapshot, LastRunSnapshot, NanSnapshot, NanGlobal, ProfileEdge, OverrunEvent, IMachineSnapshotSource (decompiled, one file each)
  - id: host-computer
    resource: ksa-decomp:KSA.Kfs.Host/HostComputer.cs
    title: HostComputer (decompiled)
  - id: vehicle-computer
    resource: ksa-decomp:KSA.Kfs.Host/VehicleComputer.cs
    title: VehicleComputer (decompiled)
  - id: perf-series
    resource: ksa-decomp:KSA.Kfs.Host/PerfSeries.cs
    title: PerfSeries (decompiled)
  - id: perf-snapshot
    resource: ksa-decomp:KSA.Kfs.Host/PerfSnapshot.cs
    title: PerfSnapshot and Timing (decompiled)
  - id: perf-report
    resource: ksa-decomp:KSA.Kfs.Host/PerfReport.cs
    title: PerfReport (decompiled)
  - id: profile-last-run
    resource: ksa-decomp:KSA.Kfs.Host/ProfileLastRun.cs
    title: ProfileLastRun (decompiled)
  - id: host-recorder
    resource: ksa-decomp:KSA.Kfs.Host/HostRecorder.cs
    title: HostRecorder, HostTickRecord, HostTickResult (decompiled)
  - id: sram-view
    resource: ksa-decomp:KSA.Kfs.Host/SramView.cs
    title: SramView (decompiled)
generated: { by: claude-code/claude-fable-5-2, at: 2026-11-25T21:30:51Z }
status: stable
ksa_build: "2026.11.25.5949"
---

# Why snapshots exist

A computer ticks on one thread; windows, console commands and the workbench read it from others.
Instead of locking the computer, `HostComputer.Publish` builds an **immutable `MachineSnapshot`**
and swaps the reference with `Volatile.Write`; `HostComputer.Snapshot` reads it with
`Volatile.Read`. A reader therefore always sees one consistent tick.[^host-computer]

```csharp
public interface IMachineSnapshotSource { MachineSnapshot Snapshot { get; } }
```

`HostComputer` is the only implementer. When and how often it publishes (the 1000 ms leases,
`PublishEveryTick`, `SnapshotIsStale`) is described in [HostComputer](/host/host-computer.md).

# `MachineSnapshot`

```csharp
public sealed record MachineSnapshot(string Name, ComputerState State, string Part, int TickHz,
    ulong K, double TimeS, uint CyclesUsed, uint Budget, long Bytecodes, uint Overruns,
    long TicksRun, long TicksLatched, bool LatchedThisTick, bool BootTick, uint CommandStreamHash,
    byte[] LatchedCommands, IReadOnlyList<TaskSnapshot> Tasks, TrapSnapshot? Trap,
    IReadOnlyList<FunctionSnapshot> Profile, long ProgramStarvedTicks, uint LayoutFingerprint,
    long PulsesSent, long LogLinesSuppressed)
{
    public byte[]? Ram { get; init; }
    public PerfSnapshot Perf { get; init; } = PerfSnapshot.Empty;
    public IReadOnlyList<FunctionSnapshot> ProfileTick { get; init; }
    public IReadOnlyList<ProfileEdge> ProfileEdges { get; init; }
    public IReadOnlyList<LastRunSnapshot> ProfileLast { get; init; }
    public IReadOnlyList<OverrunEvent> OverrunHistory { get; init; }
    public NanSnapshot? Nan { get; init; }
    public IReadOnlyList<NanGlobal> NanGlobals { get; init; }
    public IReadOnlyList<BusRow> Bus { get; init; }
    public static MachineSnapshot Empty(string name, string part);     // State = NoSoftware
}
```

[^machine-snapshot]

| member | filled from (in `Publish`) |
| --- | --- |
| `Name`, `State`, `Part`, `TickHz` | the computer's `Name`, `State`, `Part.Name`, `TickHz` |
| `K`, `TimeS` | `LastK`; `LastK / TickHz` (0 without an image) |
| `CyclesUsed`, `Bytecodes`, `LatchedThisTick`, `BootTick` | the last tick's `TickResult` (0/false when publishing without a tick) |
| `Budget` | `image.BudgetCycles` |
| `Overruns` | `machine.OverrunCount` |
| `TicksRun`, `TicksLatched` | host counters |
| `CommandStreamHash` | `machine.CommandStreamHash` — CRC-32 over `(k, latched command block, published slots)` of every latched tick since reset |
| `LatchedCommands` | a clone of the host's latched command block |
| `Tasks` | one `TaskSnapshot` per image task |
| `Trap` | `TrapSnapshot` when the machine has trapped |
| `Profile` | cumulative per-function profile, ordered by self cycles descending |
| `ProfileTick` | this tick's per-function profile, ordered by total cycles descending |
| `ProfileEdges` | this tick's caller → callee cycles |
| `ProfileLast` | per function, its most recent run (`ProfileLastRun`) |
| `ProgramStarvedTicks` | ticks in which the program task got no bytecode |
| `LayoutFingerprint` | `Layout.Fingerprint` |
| `PulsesSent` | `0` from the base class; `VehicleComputer.Publishing` sets it |
| `LogLinesSuppressed` | `Console.SuppressedTotal` |
| `Perf` | `PerfSnapshot` (below) |
| `OverrunHistory`, `Nan`, `NanGlobals` | copies of the host's bookkeeping |
| `Bus` | `BusTracker.Rows(…)`; slot bytes only while a bus lease is active |
| `Ram` | a full copy of machine RAM, only while a RAM lease is active (`WantRam`) |

After building, the snapshot passes through the protected virtual
`Publishing(MachineSnapshot) → MachineSnapshot`; `VehicleComputer` uses it to add `PulsesSent`,
`Perf.LateTicks` and `Perf.MaxLatenessNs`.[^vehicle-computer]

## The records inside

```csharp
public sealed record TaskSnapshot(string Name, string Kind, int RateDivisor, long CyclesThisTick,
    long CyclesTotal, uint MaxRamStack, int MaxReturnDepth, int MaxOperandDepth, string Status)
{
    public long   MeasuredMax   { get; init; }   // largest CyclesThisTick seen since start
    public long   CyclesLastRun { get; init; }   // cycles of the last tick in which the task ran
    public ulong? LastRunK      { get; init; }   // that tick's index
}
public sealed record FunctionSnapshot(string Name, long Self, long Total, long Calls, bool Pseudo);
public sealed record TrapSnapshot(TrapCode Code, uint Pc, int Task, byte UserCode, string? File, int Line, string Function);
public sealed record LastRunSnapshot(string Name, bool Pseudo, long Self, long Total, long Calls,
    ulong K, long RunIndex, IReadOnlyList<ProfileEdge> Children);
public sealed record NanSnapshot(uint Pc, int Task, ulong K, string Function, string? File, int Line);
public sealed record NanGlobal(string Path, ulong FirstK, string File);
public readonly record struct ProfileEdge(string Caller, string Callee, long Cycles);
public sealed record OverrunEvent(ulong K, int Task, string TaskName);
```

[^snapshot-records]

- `TaskSnapshot.Kind` is the task kind in lower case (`init`, `periodic`, `program`); `Status` is
  the VM's task status text, empty before boot; the three maxima are the VM's high-water marks
  (`machine.Maxima`). Per-task cycles are taken from the profile entry of the task's entry function.
- `FunctionSnapshot.Pseudo` marks rows that are not program functions: soft routines, services and
  the `bus` copy cost ([profiler and exec trace](/vm/profiler-and-exec-trace.md)).
- `TrapSnapshot` and `NanSnapshot` carry the source location resolved with
  `HostComputer.Locate(pc)`: function name from the debug function table (or `f_<start>`), file and
  line from the nearest debug line at or below the pc; `File` is `null` without debug info.
- `NanSnapshot` is the first NaN the VM produced since reset; `NanGlobal` records the first tick
  after which a float global was seen to hold NaN (checked once per tick for every float leaf).

## `ProfileLastRun`

Keeps, per function name, the figures of the last tick in which the function appeared in the
profile, with that tick's outgoing edges sorted by cycles (then callee name). `Add(profile, k,
runIndex)` updates the functions that ran this tick; `Snapshot()` returns a name-ordered list and
caches both the list and each entry's record until something changes. `Reset()` forgets
everything; `Count` is the number of functions seen.[^profile-last-run]

# Performance series

```csharp
public sealed class PerfSeries
{
    public const long WINDOW_NS = 1000000000L;
    public long Total { get; }                     // sum of every value since Reset
    public long Added { get; }                     // number of values since Reset
    public PerfSeries(int capacity = 256);
    public void Add(long value, long atNs = long.MinValue);
    public void Reset();
    public Timing Snapshot();
    public static long Ns(long from, long to);     // Stopwatch ticks → nanoseconds
}
public readonly record struct Timing(long Last, long Avg, long Max, long Peak, int Samples);
```

[^perf-series][^perf-snapshot]

A series is a ring of the last 256 values, each optionally stamped with a time. `Snapshot()` walks
back from the newest value and stops at the first one that is unstamped or at least one second
older than the newest (when the newest itself is unstamped, the whole ring counts). It returns the
last value, the mean and maximum of that window, the peak since reset and the window's sample
count.

| series | owner | value | stamp |
| --- | --- | --- | --- |
| `VmSeries` | `HostComputer` | ns inside `KvmMachine.RunTick` | machine time of the tick |
| `HostSeries` | `HostComputer` | ns of `RunTick` outside the VM | machine time |
| `PublishSeries` | `HostComputer` | ns in `Publish` | machine time |
| `AllocSeries` | `HostComputer` | bytes allocated on the calling thread during `RunTick` | machine time |
| `CyclesSeries` | `HostComputer` | cycles used by the tick | machine time |
| `BytecodeSeries` | `HostComputer` | bytecodes executed by the tick | machine time |
| `DispatchSeries` | `HostComputer` field, fed by `VehicleComputer.Dispatch` | ns per `Dispatch` call that ran under control | simulation time |
| `LatenessSeries` | same | ns a tick ran after its grid time | simulation time |
| `HostPublishSeries` | `VehicleComputer` (public) | ns packing host messages per tick | none |

"Machine time" is `k / tick_hz` converted to nanoseconds, so "the last second" of a tick series
means one second of program time, not of wall-clock time. All eight `HostComputer` series are reset
by `Start`.[^host-computer][^vehicle-computer]

```csharp
public sealed record PerfSnapshot(Timing Vm, Timing Host, Timing Publish, Timing Dispatch, Timing Alloc,
    long LoadNs, int ImageBytes, int RamBytes, int RamUsedBytes, uint ClockHz,
    long LateTicks, long MaxLatenessNs)
{
    public Timing Cycles { get; init; }
    public double CyclesPerUs { get; init; }       // cycles emulated per microsecond of VM time, since start
    public double NsPerBytecode { get; init; }     // VM nanoseconds per bytecode, since start
    public Timing Lateness { get; init; }
    public static readonly PerfSnapshot Empty;
}
```

`LoadNs` is the duration of `Machine.LoadImage`; `ImageBytes` the image size; `RamBytes` the part's
machine RAM; `RamUsedBytes` is `SramView.UsedBytes(image)`; `ClockHz` the part's clock.[^perf-snapshot]

## `PerfReport`

`PerfReport.Lines(MachineSnapshot s)` renders the figures as text; `WriteLine(s, index, dest)` writes
line `index` into a caller-supplied span and returns its length, or `-1` past the end (an
allocation-free form whose only caller in this build is `Lines`, which `Workbench.Perf` prints; the
debug window formats its own table with the `Append…` helpers below).[^perf-report]

| line | shown when | content |
| --- | --- | --- |
| no ticks | no VM samples | `no image loaded` or `no ticks yet` |
| VM | ticks ran | `VM tick, last 1 s (N ticks): mean …, max …; peak … since start` |
| cycles | ticks ran | mean and max cycles, emulated cycles/µs against the part's MHz, ns/bytecode |
| budget | ticks ran | budget use (mean, max) and the tick's host cost as a share of its period |
| host | ticks ran | host time per tick and publish time |
| dispatch | dispatch samples exist | `dispatch, last 1 s (N calls): mean …, max … per physics call; peak … since start` |
| lateness | any late tick | late-tick count, maximum and windowed lateness in ms |
| memory | an image was loaded | RAM, RAM used, image size, load time, allocation per tick |

Formatting helpers are public: `AppendPercent`, `Time`/`AppendTime` (ns, µs, ms),
`Bytes`/`AppendBytes` (B, KB, MB). They write through `InvariantWriter` (see [views](/host/views.md)).

# Tick results and the recorder

```csharp
public readonly record struct HostTickResult(ulong K, TickResult Vm, bool Latched);
public sealed record HostTickRecord(ulong K, byte[] Sensors, HostMessage[] Messages);

public sealed class HostRecorder
{
    public IReadOnlyList<HostTickRecord> Ticks { get; }
    public void Add(ulong k, ReadOnlySpan<byte> sensors, IReadOnlyList<HostMessage> messages);
    public void Clear();
    public static void Play(HostComputer computer, IReadOnlyList<HostTickRecord> stream);
}
```

[^host-recorder]

`HostTickResult` is what `RunTick` returns: the tick index, the VM's `TickResult`, and whether the
command block latched. Setting `HostComputer.Recorder` makes `RunTick` call `Recorder.Add` for
every tick with deep copies of the sensor block and every host message slot — the complete input
of the tick. `HostRecorder.Play` feeds a recorded stream to another computer with
`computer.RunTick(k, sensors, messages)`. Because a VM tick is a deterministic function of its
image, layout and these inputs, a recording replays exactly; `CommandStreamHash` is the quantity to
compare. Nothing in the decompiled assemblies sets `Recorder` or calls `Play`. `Inferred:` it is a
test facility whose callers are not shipped.

# `SramView`: RAM as a bitmap

Static helpers for the workbench's RAM display.[^sram-view]

| member | meaning |
| --- | --- |
| `BITS_PER_ROW = 32`, `MIN_WORDS_PER_ROW = 1`, `MAX_WORDS_PER_ROW = 32`, `DEFAULT_WORDS_PER_ROW = 16` | layout constants |
| `Rows(int bytes)` / `Rows(int bytes, int wordsPerRow)` | 32-bit words needed; rows of words needed |
| `Word(ReadOnlySpan<byte> ram, int row)` | little-endian word `row`, zero-padded at the end |
| `Runs(uint word, Span<(int Start, int Length)> runs)` | runs of set bits, most significant bit first |
| `Regions(KvmImage image)` | the RAM areas in use: `[0, end of data and task stacks)` and `[VmRegionBase, MachineRam)`; one region covering all RAM when they meet |
| `UsedBytes(KvmImage image)` | total length of those regions |
| `Symbol(KvmImage image, uint address)` | nearest debug global at or below the address, as `name` or `name+offset`; `null` without debug info |
| `Region(int Start, int Length)` | record struct with `End`, `Rows`, `RowsAt(wordsPerRow)`, `WordAt(row, column, wordsPerRow)` |

The RAM bytes themselves come from `MachineSnapshot.Ram`, which is populated only while a RAM lease
is active.

# Modding notes

- Everything here is public and read-only by construction; a mod can read any computer's
  `Snapshot` from any thread. Remember that reading `Snapshot` renews the publish lease, which
  costs the tick thread a snapshot build per tick while the lease is alive.
- `MachineSnapshot.LatchedCommands` and `Bus[i].Slot` are the thread-safe way to inspect the
  command block and message contents from the UI thread; `HostComputer.Latched` and
  `KvmMachine.ReadSlot` are for the tick thread.
- The only customisation hook is the protected virtual `Publishing`. Adding a field to the
  snapshot for a mod's own state is not possible without subclassing the (positional) record; keep
  mod state outside it.
- `HostComputer.Recorder` is a public settable property: an inexpensive way for a mod to capture
  exactly which host messages a program was given.

[^machine-snapshot]: MachineSnapshot.cs:6–30.
[^snapshot-records]: TaskSnapshot.cs:3–10; FunctionSnapshot.cs:3; TrapSnapshot.cs:5; LastRunSnapshot.cs:5; NanSnapshot.cs:3; NanGlobal.cs:3; ProfileEdge.cs:3; OverrunEvent.cs:3; IMachineSnapshotSource.cs:3–6.
[^host-computer]: HostComputer.cs — series fields :83–97; `Snapshot` :167–174; series reset in `Start` :354–358; per-task cycles :473–491; series fed in `RunTick` :497–513; `TickNs` :517–521; `AfterTick` (overruns, NaN) :537–584; `Locate` :828–851; `Publish` :863–936.
[^vehicle-computer]: VehicleComputer.cs — `HostPublishSeries` :132; `LatenessSeries`/`DispatchSeries` fed :494, :515; host publish time :569; `Publishing` :595–606.
[^perf-series]: PerfSeries.cs:6–95.
[^perf-snapshot]: PerfSnapshot.cs:3–14; Timing.cs:3.
[^perf-report]: PerfReport.cs — `Lines` :22–38; `LineAt` :40–76; `WriteLine` :78–175; helpers :177–248. Callers: Workbench.cs:1356 (`Perf` → `Lines`); KfsDebugWindow.cs:175–289 (`AppendPercent`, `AppendTime`, `AppendBytes` only).
[^profile-last-run]: ProfileLastRun.cs:8–100.
[^host-recorder]: HostRecorder.cs:7–30; HostTickRecord.cs:3; HostTickResult.cs:5.
[^sram-view]: SramView.cs:9–147.
