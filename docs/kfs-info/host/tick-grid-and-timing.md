---
type: Subsystem Reference
title: Tick grid and timing
description: How a vehicle computer maps simulation time onto tick indices with TickGrid, catches up or skips overdue ticks, honours a wall-clock deadline, reports overruns and tells the game when to wake it next.
resource: ksa-decomp:KSA.Kfs.Host/TickGrid.cs
tags: [kfs, host, tick, timing, scheduling, overrun, deadline]
sources:
  - id: tick-grid
    resource: ksa-decomp:KSA.Kfs.Host/TickGrid.cs
    title: TickGrid (decompiled)
  - id: timing
    resource: ksa-decomp:KSA.Kfs.Host/Timing.cs
    title: Timing (decompiled)
  - id: vehicle-computer
    resource: ksa-decomp:KSA.Kfs.Host/VehicleComputer.cs
    title: VehicleComputer (decompiled)
  - id: host-computer
    resource: ksa-decomp:KSA.Kfs.Host/HostComputer.cs
    title: HostComputer (decompiled)
  - id: host-messages
    resource: ksa-decomp:KSA.Kfs.Host/HostMessages.cs
    title: HostMessages (decompiled)
  - id: host-leaf-values
    resource: ksa-decomp:KSA.Kfs.Host/HostLeafValues.cs
    title: HostLeafValues (decompiled)
  - id: overrun-event
    resource: ksa-decomp:KSA.Kfs.Host/OverrunEvent.cs
    title: OverrunEvent (decompiled)
  - id: kfs-vehicle-host
    resource: ksa-decomp:KSA.Kfs/KfsVehicleHost.cs
    title: KfsVehicleHost (decompiled)
  - id: kvm-machine
    resource: ksa-decomp:KSA.Kfs.Vm/KvmMachine.cs
    title: KvmMachine (decompiled)
  - id: catalogue
    resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.catalogue.json
    title: catalogue.json (embedded resource)
  - id: bench-machine
    resource: ksa-decomp:KSA.Kfs.Host/BenchMachine.cs
    title: BenchMachine (decompiled)
  - id: kfs-services
    resource: ksa-decomp:KSA.Kfs/KfsServices.cs
    title: KfsServices (decompiled)
generated: { by: claude-code/claude-fable-5-2, at: 2026-11-25T21:30:51Z }
status: stable
ksa_build: "2026.11.25.5949"
---

# Three clocks

| clock | unit | owner | used for |
| --- | --- | --- | --- |
| simulation time | `long` nanoseconds (`tNs`) | the game (`nav.Time.Nanoseconds`) | deciding which ticks are due |
| tick index `k` | `ulong`, 0 at reset | `TickGrid.NextK` | the VM's notion of time; `sys.k`, `sys.met_us` |
| wall clock | `Stopwatch` ticks | the host process | the per-frame deadline and all performance figures |

A program never sees simulation time directly. Its time is `k`, and derived from it the mission
elapsed time `met_us = k * 1_000_000 / tick_hz` microseconds since the computer's reset.[^host-messages][^kvm-machine]

# `TickGrid`

```csharp
public sealed class TickGrid
{
    public const long LATTICE_NS   = 10000000L;   // 10 ms: the 100 Hz lattice
    public const long TOLERANCE_NS = 1000L;       // 1 µs

    public int   TickHz   { get; }
    public long  PeriodNs { get; }                // 1_000_000_000 / TickHz
    public long  ResetNs  { get; }                // simulation time of tick 0
    public ulong NextK    { get; private set; }
    public long  NextTickNs => TimeOf(NextK);

    public TickGrid(int tickHz, long resetNs);
    public static long AlignUp(long tNs);
    public long   TimeOf(ulong k);                // ResetNs + k * PeriodNs
    public bool   IsDue(long tNs);                // NextTickNs <= tNs + 1000
    public ulong  Take();                         // return NextK++
    public long   SkipTo(long tNs);
    public long   SkipPast(long tNs);
    public double SecondsToNext(long tNs);        // max(0, NextTickNs - tNs) / 1e9
}
```

[^tick-grid]

- **Constructor checks.** `tickHz` must divide one second exactly and its period must be a multiple
  of 10 ms, otherwise `ArgumentException("tick_hz N doesn't divide the 100 Hz lattice")`;
  `resetNs` must be a multiple of 10 ms, otherwise
  `ArgumentException("t_reset must lie on the lattice")`. The rates that pass are the divisors of
  100: 1, 2, 4, 5, 10, 20, 25, 50, 100 Hz. The processor catalogue allows
  `[5, 10, 20, 25, 50, 100]` with a default of 50.[^catalogue]
- **`AlignUp(tNs)`** rounds a time up to the next 10 ms lattice point (handles negative times).
  `VehicleComputer.TryStart` uses it: `Grid = new TickGrid(TickHz, TickGrid.AlignUp(nowNs))`, so
  tick 0 falls on the first lattice point at or after the start time.[^vehicle-computer]
- **`SkipTo(tNs)`** sets `NextK` to `(tNs + 1000 − ResetNs) / PeriodNs` when that is ahead of
  `NextK` — the newest tick that is already due stays runnable — and returns how many were skipped.
- **`SkipPast(tNs)`** sets `NextK` to that index **plus one** — nothing remains due — and returns
  the count. Both return `0` when `tNs + 1000 < ResetNs`.

`Grid.ResetNs` is also the **epoch for message timestamps**: absolute simulation times that the
host puts in `int64 …_us` fields are converted with `HostLeafValues.Us(absNs, epochNs)`, which
returns microseconds since the reset (rounded toward minus infinity) and passes `long.MinValue`
and `long.MaxValue` through unchanged.[^host-leaf-values] The game also uses `Grid.ResetNs` to turn
a program's `t_us` argument back into simulation time, and as a "the computer was reset" marker.[^kfs-services]

# Scheduling inside `Dispatch`

`VehicleComputer.Dispatch(tNs, …)` is the only code that advances the grid (`TickDue` and the game
only read it).[^vehicle-computer]

```text
DrainQueue
if not under control / no grid            -> NotControlled
SkipOverdueTicks(tNs)                     -> catch-up limit
while !AwaitingStage and Grid.NextTickNs <= tNs + 1000:
    if deadline passed: SkipPastDeadline(tNs); break
    lateness = tNs - Grid.NextTickNs
    k = Grid.Take()
    sample once per dispatch
    PlayerTick(k, sample)
NextWakeSeconds = AwaitingStage ? PeriodNs : max(0, Grid.NextTickNs - tNs)
```

## Catch-up

If the game steps less often than the tick period, several ticks are due at once and the loop runs
them back to back with the **same host sample**. Each tick after the first of a dispatch increments
`LateTicks`; the lateness of each tick goes to `MaxLatenessNs` and the `LatenessSeries`.

## The one-second limit

`MAX_CATCH_UP_NS = 1_000_000_000`. When the oldest due tick is more than one second behind `tNs`,
`SkipOverdueTicks` calls `Grid.SkipTo(tNs)`, adds the count to `SkippedTicks` and writes an
unthrottled host line:

```text
skipped N tick(s): the oldest due tick was X.XXX s late, more than 1 s
```

One tick (the newest due one) still runs in that dispatch. Tick indices are never reused: skipped
indices simply never execute, so `k` and `met_us` keep tracking simulation time.

## The wall-clock deadline

`TickDeadline` is a `Stopwatch.GetTimestamp()` value, `long.MaxValue` meaning "none". Before each
tick of the loop the host compares the current timestamp with it; once it has passed,
`SkipPastDeadline` calls `Grid.SkipPast(tNs)`, adds the count to both `SkippedTicks` and
`DeadlineSkippedTicks`, and writes a rate-limited event:

```text
skipped N tick(s): the computer had used its host time for this frame; lower time warp or make the program cheaper
```

The game sets the deadline before every dispatch from a per-frame budget
(`KfsVehicleHost.FrameBudgetMs`, set to `4.0` when the kFS console commands are registered;
`DEFAULT_FRAME_BUDGET_MS = 4.0`): the deadline is "now plus what is left of this frame's budget for
this vehicle". A budget of `0` or less means no deadline.[^kfs-vehicle-host]

## Awaiting a stage

While `AwaitingStage` is true the loop does not run at all and `NextWakeSeconds` is one period.
The grid does not advance, so the paused ticks become overdue and are later caught up or skipped
by the rules above.

## `NextWakeSeconds`

The value is a request, not a timer: the game passes it to
`output.RequestWakeup(seconds, PhysicsStepLimit.KfsTick)` so the next flight-computer step lands on
the next tick.[^kfs-vehicle-host] `DispatchResult.NotControlled` carries
`double.PositiveInfinity`.

# Overruns are a VM matter

A tick "overruns" when a periodic task exhausts the tick's cycle budget; that is decided inside
`KvmMachine.RunTick` and reported as `TickResult.OverrunTask >= 0`. The host only records it
(`HostComputer.AfterTick`):[^host-computer]

```csharp
public sealed record OverrunEvent(ulong K, int Task, string TaskName);
```

- the last `OVERRUN_HISTORY_LENGTH = 64` events are kept and published in
  `MachineSnapshot.OverrunHistory`;[^overrun-event]
- the first overrun logs
  `overrun: task T used the tick's whole budget of B cycles and was abandoned; this tick's commands and requests are dropped`;
- overrun counts that are powers of ten log
  `overrun: N ticks abandoned since the start, the last in task T`.

An overrun tick does not latch: `Applied` keeps its previous contents and the VM discards that
tick's service requests. See [cost model and budgets](/vm/cost-model-and-budgets.md) and
[tasks and scheduling](/vm/tasks-and-scheduling.md).

Three different things are therefore called "late" or "skipped", and they are counted separately:

| what | counter | cause |
| --- | --- | --- |
| overrun | `Machine.OverrunCount`, `MachineSnapshot.Overruns` | the program used the whole cycle budget |
| late tick | `LateTicks`, `MaxLatenessNs`, `Perf.Lateness` | the game stepped later than the tick's grid time |
| skipped tick | `SkippedTicks`, `DeadlineSkippedTicks` | more than 1 s behind, or the frame's host budget ran out |

# `Timing`

```csharp
public readonly record struct Timing(long Last, long Avg, long Max, long Peak, int Samples);
```

`Timing` is the snapshot of one `PerfSeries`: the last value, the mean and maximum over the last
second of samples, the peak since reset, and how many samples the window held.[^timing] It is the
element type of `PerfSnapshot`; see [snapshots and perf](/host/snapshots-and-perf.md). It has
nothing to do with tick scheduling despite the name.

# The bench clock

`BenchMachine` does not use a `TickGrid`. It keeps its own `_nextK` and either steps a fixed number
of ticks (`Tick(n)`) or converts frame time to ticks with a carry (`Advance(dtSeconds)`), bounded
by `MaxCyclesPerFrame` and a host-millisecond limit.[^bench-machine] See
[BenchMachine and FakeVehicle](/host/bench-machine-and-fake-vehicle.md).

# Modding notes

- `TickGrid` is public and sealed; `VehicleComputer.Grid` is publicly readable but privately set.
  A mod can read `Grid.ResetNs`, `Grid.PeriodNs`, `Grid.NextK` to convert between simulation time
  and program time; it cannot retime the computer without patching.
- `TickDeadline` has a public setter, but the game overwrites it at the start of every
  `KfsVehicleHost.Dispatch`.
- `KfsVehicleHost.FrameBudgetMs` is a public static property; it is the supported knob for how much
  wall-clock time kFS may use per frame per vehicle.
- All catch-up ticks of one dispatch see one sample. Data a mod injects per dispatch (for example
  with `Enqueue`) is likewise seen by every tick of that dispatch.
- Program time stops while the vehicle's physics does not step (the dispatch is not called), and
  jumps when ticks are skipped. A mod that timestamps data for a program should use
  `HostMessages.MetUs(k, tickHz)` for the tick it targets, not wall-clock time.

[^tick-grid]: TickGrid.cs — constants :7–9; constructor :21–34; `AlignUp` :36–48; `TimeOf` :50–53; `IsDue` :55–58; `Take` :60–63; `SkipTo` :65–79; `SkipPast` :81–95; `SecondsToNext` :97–100.
[^timing]: Timing.cs:3.
[^vehicle-computer]: VehicleComputer.cs — `MAX_CATCH_UP_NS` :59; `TryStart` creates the grid :371; `TickDue` :430–441; dispatch loop :473–517; `SkipOverdueTicks` :520–541; `SkipPastDeadline` :543–552.
[^host-computer]: HostComputer.cs — `OVERRUN_HISTORY_LENGTH` :75; overrun bookkeeping in `AfterTick` :540–557.
[^host-messages]: HostMessages.cs — `MetUs` :413–416.
[^host-leaf-values]: HostLeafValues.cs — `Us` :142–154.
[^overrun-event]: OverrunEvent.cs:3.
[^kfs-vehicle-host]: KfsVehicleHost.cs — `DEFAULT_FRAME_BUDGET_MS` :53; deadline set per dispatch :445; `TickDeadlineAt` :471–486; `RequestWakeup` :556, :573.
[^kvm-machine]: KvmMachine.cs — system block `k` and `met_us` :470–484; overrun handling in `RunTick` :2146–2180.
[^catalogue]: catalogue.json — `allowed_tick_hz`, `default_tick_hz`.
[^bench-machine]: BenchMachine.cs:79–183.
[^kfs-services]: KfsServices.cs — reset marker :124–129; `plan_burn` time conversion :420–421.
