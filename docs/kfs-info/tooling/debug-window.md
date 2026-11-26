---
type: Subsystem Reference
title: kFS debug window and dispatch profiler
description: KfsDebugWindow (View > Debug > Show kFS Debug) and KfsDispatchProfiler — the on/off switch for the prototype, the per-tick timing tables, the dispatch flame graph, and where every number comes from.
resource: ksa-decomp:KSA.Kfs/KfsDebugWindow.cs
tags: [kfs, tooling, debug, performance, profiler, imgui]
sources:
  - id: debug-window
    resource: ksa-decomp:KSA.Kfs/KfsDebugWindow.cs
    title: KfsDebugWindow (decompiled)
  - id: dispatch-profiler
    resource: ksa-decomp:KSA.Kfs/KfsDispatchProfiler.cs
    title: KfsDispatchProfiler (decompiled)
  - id: kfs-integration
    resource: ksa-decomp:KSA.Kfs/KfsIntegration.cs
    title: KfsIntegration (decompiled)
  - id: program
    resource: ksa-decomp:KSA/Program.cs
    title: KSA Program (decompiled)
  - id: kfs-vehicle-host
    resource: ksa-decomp:KSA.Kfs/KfsVehicleHost.cs
    title: KfsVehicleHost (decompiled)
  - id: host-computer
    resource: ksa-decomp:KSA.Kfs.Host/HostComputer.cs
    title: HostComputer (decompiled)
  - id: perf-snapshot
    resource: ksa-decomp:KSA.Kfs.Host/PerfSnapshot.cs
    title: PerfSnapshot and Timing (decompiled)
  - id: tick-zones
    resource: ksa-decomp:KSA.Kfs.Vm/TickZones.cs
    title: TickZones (decompiled)
  - id: view-throttle
    resource: ksa-decomp:KSA.Kfs.Host.Views/ViewThrottle.cs
    title: ViewThrottle (decompiled)
  - id: editor-window
    resource: ksa-decomp:KSA.Kfs/KfsEditorWindow.cs
    title: KfsEditorWindow (decompiled)
generated: { by: claude-code/claude-fable-5-2, at: 2026-11-25T21:56:52Z }
status: stable
ksa_build: "2026.11.25.5949"
---

# What it is

`KSA.Kfs.KfsDebugWindow` (`public sealed class KfsDebugWindow : IStaticWindow, IDisposable`) is a plain
ImGui window titled `kFS Debug`. It has two jobs:[^debug-window]

1. it is the only **GUI switch for the prototype flag** (a `kFS ON` / `kFS OFF` button);
2. while the flag is on, it shows what the current target costs the host: per-tick timings,
   interpreter speed, memory, editor cost, and a flame graph of the vehicle dispatch.

It is the only kFS window reachable from the game's menus.

# How it is shown

- One instance is registered at boot:
  `ImGuiWindow.RegisterStaticWindow(new KfsDebugWindow())` in `KfsIntegration.RegisterCommands`.
  [^kfs-integration]
- Visibility is the public static field `KfsDebugWindow.Show` (`IsShown => Show`).
- The game's main menu bar toggles it: **View > Debug > Show kFS Debug**
  (`ImGuiHelper.DrawMenuItem("Show kFS Debug"u8, ref KfsDebugWindow.Show)`).[^program]
- The window's own close button clears `Show` (`ImGui.Begin("kFS Debug"u8, ref Show)`); first-use
  size is 720 x 860 times the interface scale.[^debug-window]

It is drawn regardless of the flag; only its content changes.

# Flag off: the enable button

`DrawEnable` centres one 160 x 40 button, `kFS ON`. Clicking it runs[^debug-window]

```csharp
KfsConsoleCommands.Execute("on");
KfsIntegration.ShowEditor();
```

so the flag is set and the [editor window](/tooling/editor-window.md) opens. This is the complete
mouse-only path to kFS: View > Debug > Show kFS Debug > kFS ON.

# Flag on: the performance view

`DrawPerf` draws, top to bottom:[^debug-window]

- a `kFS OFF` button → `KfsConsoleCommands.Execute("off")` (see
  [console commands](/tooling/console-commands.md) for what that does to running programs);
- a pause toggle (tooltip "Pause the numbers (the program keeps running)" / "Resume the numbers");
- five sections inside a scrolling child, described below.

The snapshot behind the numbers is fetched through a `ViewThrottle<MachineSnapshot>`: at most once
every 0.25 s, or immediately when the target changes, never while paused.[^view-throttle] The fetch
(`BuildPerfFigures`) reads `Workbench.Target.Snapshot` and also captures the target name, the
editor's file and last build time, whether the editor is shown and its draw time,
`GC.GetTotalMemory(false)` and `GC.CollectionCount(0)`.[^debug-window] The workbench is
`KfsIntegration.Workbench`, so opening this window with the flag on creates the workbench if it did
not exist yet.

## 1. Dispatch flame graph

Header: `dispatch on the vehicle worker, wall time by zone`.

- Source: `(Workbench.TargetVehicle as KfsVehicleHost)?.DispatchProfiler`. With a bench target the
  section shows `the bench is not dispatched: target a vehicle`.
- Each frame the window calls `Want()` on the profiler (see below) and mirrors its own pause state
  into `Paused`. Every 0.25 s (`FLAME_REFRESH_S`) it calls `Publish()` and takes
  `LastCompleteFrameIndex` as the frame to draw.
- A `smooth` slider (0 to 2 s, logarithmic; below 0.001 it reads `off (one dispatch)`) averages the
  dispatches over that time (`ProfilerAverage.Build`). Tooltip: "Show the mean of the dispatches over
  this long. Off shows one dispatch as it ran."
- Summary text: `dispatch <ms> ms, <pct> % of the <period> ms tick period[ (mean of <n>)]`, where the
  duration is the root zone's ticks and the period is `1000 / TickHz` of the target.
- The graph itself is the game's generic `ProfilerPlots.DrawFlameGraph`, 200 px high (scaled).
- Before any tick ran: `no dispatch has run a tick yet`.

The zones are the profiler tags declared in `TickZones`: `Kfs.Dispatch` (root), `Kfs.BeforeDispatch`,
`Kfs.AfterDispatch`, `Kfs.FlightComputer`, `Kfs.WriteBack`, `Kfs.DrainQueue`, `Kfs.Sample`,
`Kfs.Tick`, `Kfs.Tick.PackMessages`, `Kfs.Tick.WriteInputs`, `Kfs.Tick.Requests`,
`Kfs.Tick.BusMonitor`, `Kfs.Tick.Views`, `Kfs.Tick.Publish`, `Kfs.Vm.RunTick`, `Kfs.Vm.InitTask`,
`Kfs.Vm.PeriodicTask`, `Kfs.Vm.ProgramTask`.[^tick-zones] What
each phase does is in [dispatch and threading](/integration/dispatch-and-threading.md).

## 2. Timings

Header: `<target>: timings over the last second, peak since start`. With no samples:
`no image loaded` (state `NoSoftware`) or `no ticks yet`. Otherwise a table with columns
`per tick`, `mean`, `max`, `peak`, `samples`:[^debug-window]

| row | source in `MachineSnapshot.Perf` | unit |
| --- | --- | --- |
| `VM (the program)` | `Vm` | time |
| `  cycles` | `Cycles` | count |
| `  budget used` | `Cycles.Avg` and `Cycles.Max` as a percentage of `snapshot.Budget` | % |
| `host (around the tick)` | `Host` | time |
| `publish (the snapshot)` | `Publish` | time |
| `allocated` | `Alloc` | bytes |
| `dispatch (per physics call)` — only when it has samples | `Dispatch` | time |
| `lateness` — only when `LateTicks > 0` or `MaxLatenessNs > 0` | `Lateness`, peak = `MaxLatenessNs` | time |

Each `Timing` is `(long Last, long Avg, long Max, long Peak, int Samples)`.[^perf-snapshot]

## 3. The machine

Header `the machine`; a two-column table:[^debug-window]

| row | text | source |
| --- | --- | --- |
| `VM tick cost` | `<pct> of the <ms> ms tick period` | `Perf.Vm.Avg` against `1e9 / TickHz` ns |
| `emulation rate` | `<n> cycles/µs since start (the part runs at <n> MHz)` | `Perf.CyclesPerUs`, `Perf.ClockHz` |
| `interpreter` | `<n> ns per bytecode since start` | `Perf.NsPerBytecode` |
| `late ticks` | `<n> since start` | `Perf.LateTicks` |
| `RAM` | `<size>, <size> used` | `Perf.RamBytes`, `Perf.RamUsedBytes` |
| `image` | `<size>, loaded in <time>` | `Perf.ImageBytes`, `Perf.LoadNs` |

The first four rows need at least one VM sample.

## 4. The editor and the process

Header `the editor and the process`:[^debug-window]

| row | text |
| --- | --- |
| `last build` | `<ms> ms (<file>)` — `EditorModel.LastAssembleMs` |
| `editor draw` | the editor window's last draw time (`KfsEditorWindow.DrawNs`), or `the editor is closed` |
| `process heap` | `GC.GetTotalMemory(false)` |
| `gen0 collections` | `GC.CollectionCount(0)` |

## 5. Editor allocations

Header `the editor's allocations per frame on the main thread, over the last second`. If the editor
was never created: `the editor has not been opened`. Otherwise a `part` / `mean` / `peak` table with
rows `whole frame (the game's)`, `editor window`, `status line` and one row per editor tab name;
values are bytes from the editor's `AllocMeter`s (`GC.GetAllocatedBytesForCurrentThread` deltas), or
`not drawn yet`.[^debug-window][^editor-window]

# Where `MachineSnapshot.Perf` comes from

`HostComputer.Publish` fills a `PerfSnapshot` from eight rolling `PerfSeries` kept by every computer
(`VmSeries`, `HostSeries`, `PublishSeries`, `DispatchSeries`, `AllocSeries`, `CyclesSeries`,
`BytecodeSeries`, `LatenessSeries`), plus the load time, image size, machine RAM and used RAM.
`CyclesPerUs` is total cycles over total VM time; `NsPerBytecode` is total VM time over total
bytecodes.[^host-computer][^perf-snapshot] The series are reset by `Start`. The same data printed as
text is `kfs perf` (`PerfReport.Lines`). Details:
[snapshots and perf](/host/snapshots-and-perf.md).

# `KfsDispatchProfiler`

```csharp
public sealed class KfsDispatchProfiler : IProfilerSource
{
    public const long WANTED_MS = 1000L;
    public readonly CpuProfiler Cpu = new CpuProfiler(TickZones.Dispatch);
    public void Want();                    // keep recording for the next WANTED_MS
    public void Publish();                 // expose the last completed frame to readers
    public void BeginDispatch(bool tickDue);
    public void EndDispatch(bool ranTick);
    public void CancelDispatch();
    public bool IsActive { get; set; }     // getter: "wanted recently"; setter does nothing
    public bool Paused { get; set; }       // forwards to Cpu.Paused
    public int CurrentFrameIndex { get; }  // both return the published frame index
    public int LastCompleteFrameIndex { get; }
    // Name, ImDisplayName, Frames forward to Cpu
}
```

It wraps one of the game's `CpuProfiler`s so that a vehicle's dispatch can be recorded **only while
somebody is looking**, and read from the UI thread without tearing.[^dispatch-profiler]

- Every `KfsVehicleHost` owns one (`public KfsDispatchProfiler DispatchProfiler { get; }`) and hands
  its `Cpu` to the computer (`Computer.Zones = DispatchProfiler.Cpu`), so the host and VM zones are
  recorded into it.[^kfs-vehicle-host]
- `KfsVehicleHost.Dispatch` brackets each dispatch with
  `BeginDispatch(Computer.TickDue(...))` and `EndDispatch(ticksThisDispatch > 0)`, or
  `CancelDispatch()` when `DispatchCore` throws (the exception is rethrown).[^kfs-vehicle-host]
- `BeginDispatch` enables recording only when a tick is due, the profiler was wanted within the last
  second, and the frame slot about to be written is not the published one (so the frame the window is
  drawing is never overwritten).
- `EndDispatch` ends the frame; it becomes the "completed" frame only when the dispatch actually ran
  a tick, otherwise the frame is cleared.
- `Want()` and `Publish()` are called from the main thread by the debug window; the indices are
  exchanged with `Volatile` reads and writes.

# Modding notes

- **Visibility.** `KfsDebugWindow` and `KfsDispatchProfiler` are public. `KfsDebugWindow.Show` is a
  public static field: a mod can open the window with `KfsDebugWindow.Show = true`. The drawing
  methods are private; the registered instance is not stored anywhere reachable (it lives only in
  `ImGuiWindow`'s private static list).[^debug-window][^kfs-integration]
- **Turning kFS on the way the game does:** `KfsConsoleCommands.Execute("on")`, optionally followed
  by `KfsIntegration.ShowEditor()`. Setting `KfsPrototype.Enabled = true` directly is equivalent for
  the "on" direction; the "off" direction should go through `Execute("off")` because that also hands
  vehicles back (`KfsVehicleHost.HandBackAtFlagOff`).
- **Measuring a mod's own service cost.** Code that a mod adds inside the dispatch (for example a
  service handler in a postfix on `VehicleComputer.ApplyRequest`, the tested recipe of
  [adding a service](/modding/adding-a-service.md), or one patched into `KfsServices`) is attributed
  to whichever `TickZones` zone encloses it.
  `Inferred:` a mod can open its own zone on `host.DispatchProfiler.Cpu` (the same `CpuProfiler` the
  host uses) to appear as a separate bar in the flame graph; the `CpuProfiler` API itself was not
  read for this document.
- **The numbers are per target.** With the bench as target there is no dispatch section, and the
  timing rows describe the bench machine driven from `Workbench.Frame`.
- **Reading performance data from code:** `KfsIntegration.Workbench.Target.Snapshot.Perf`, or
  `KfsVehicleHost.Find(vehicle)?.Computer.Snapshot.Perf` for a specific vehicle. A vehicle computer
  refreshes its snapshot only while something keeps asking (`WantSnapshot`, a one-second lease); see
  [Workbench](/tooling/workbench.md), "WithSnapshot".
- **Threads.** The window runs on the main thread; `BeginDispatch` / `EndDispatch` run on whichever
  solver / vehicle-worker thread dispatches the vehicle, never the main thread (see
  [dispatch and threading](/integration/dispatch-and-threading.md)).

[^debug-window]: KfsDebugWindow.cs — `Show` :23; default size :27; throttle and flame constants :29-35; `IsShown` :80; `OnDrawUi` :89-106; `DrawEnable` :112-121; `DrawPerf` :123-147; `DrawPerfTimings` :149-193; `TimingRow` :195-228; `DrawPerfMachine` :243-293; `DrawDispatchFlame` :295-382; `BuildPerfFigures` :384-391; `DrawPerfEditor` :393-431; `DrawAllocations` / `AllocationRow` :433-497.
[^dispatch-profiler]: KfsDispatchProfiler.cs — `WANTED_MS` :10; `Cpu` :12; `IsActive` :32-41; `Paused` :43-53; `Want` :55-58; `Publish` :60-63; `BeginDispatch` :65-72; `EndDispatch` :74-89; `CancelDispatch` :91-99.
[^kfs-integration]: KfsIntegration.cs — `RegisterCommands` (registers the window) :146-156; `ShowEditor` :183-198.
[^program]: Program.cs — main menu bar :3907; View menu :4096; Debug submenu :4305; "Show kFS Debug" :4423.
[^kfs-vehicle-host]: KfsVehicleHost.cs — `DispatchProfiler` :81; `Computer.Zones = DispatchProfiler.Cpu` :113; `BeginDispatch` :446; `CancelDispatch` :456; `EndDispatch` :462.
[^host-computer]: HostComputer.cs — `PerfSeries` fields :83-97; series reset in `Start` :353-357; `Publish` (builds `PerfSnapshot`) :863-936.
[^perf-snapshot]: PerfSnapshot.cs:3-14; Timing.cs:3.
[^tick-zones]: TickZones.cs:3-40.
[^view-throttle]: ViewThrottle.cs — default interval 0.25 s :21-24; `Get` :26-37.
[^editor-window]: KfsEditorWindow.cs — `AllocMeter` :42-80; `DrawNs` :666; allocation accessors :670-678; `TabNames` :584-588.
