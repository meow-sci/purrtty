---
type: API Reference
title: Workbench
description: The Workbench class is the facade behind most kfs console verbs (30 of 36) and the editor window, owning the workspace, the bench machine, the editor model and the choice of target (bench or vehicle).
resource: ksa-decomp:KSA.Kfs.Host.Workbench/Workbench.cs
tags: [kfs, tooling, workbench, api, extension]
sources:
  - id: workbench
    resource: ksa-decomp:KSA.Kfs.Host.Workbench/Workbench.cs
    title: Workbench (decompiled)
  - id: kfs-integration
    resource: ksa-decomp:KSA.Kfs/KfsIntegration.cs
    title: KfsIntegration (decompiled)
  - id: ivehicle-target
    resource: ksa-decomp:KSA.Kfs.Host.Workbench/IVehicleTarget.cs
    title: IVehicleTarget (decompiled)
  - id: bench-machine
    resource: ksa-decomp:KSA.Kfs.Host/BenchMachine.cs
    title: BenchMachine (decompiled)
  - id: host-computer
    resource: ksa-decomp:KSA.Kfs.Host/HostComputer.cs
    title: HostComputer (decompiled)
  - id: kfs-vehicle-host
    resource: ksa-decomp:KSA.Kfs/KfsVehicleHost.cs
    title: KfsVehicleHost (decompiled)
  - id: editor-model
    resource: ksa-decomp:KSA.Kfs.Host.Workbench/EditorModel.cs
    title: EditorModel (decompiled)
  - id: kc-front-end
    resource: ksa-decomp:KSA.Kfs.KC/KcFrontEnd.cs
    title: KcFrontEnd (decompiled)
generated: { by: claude-code/claude-fable-5-2, at: 2026-11-25T21:56:52Z }
status: stable
ksa_build: "2026.11.25.5949"
---

# Purpose

`KSA.Kfs.Host.Workbench.Workbench` (`public sealed class`) is the object every piece of kFS user
tooling talks to. Thirty of the 36 `kfs` console verbs are one call on it (the exceptions are `help`,
`on`, `off`, `editor`, `kill` and `budget`; see [console commands](/tooling/console-commands.md)), the
editor window draws from it, and the debug window reads its target.[^workbench] It owns:

- the on-disk [workspace](/tooling/workspace-and-files.md) (`Workspace`),
- the off-vehicle [bench machine](/host/bench-machine-and-fake-vehicle.md) (`Bench`),
- the editor's text and build state (`Editor`, an `EditorModel`),
- the choice of **target**: the bench, or the computer of a vehicle (`IVehicleTarget`),
- the last build and the image it loaded.

It contains no ImGui code and no reference to game types: the game is reached only through the
`ActiveVehicle` delegate and the `IVehicleTarget` interface.[^workbench][^ivehicle-target]

# Construction

```csharp
public Workbench(GameTables tables, string workspaceFolder, TextWriter output)
```

The constructor:[^workbench]

1. calls `KcFrontEnd.Register()`, which stores the kC front-end factory in
   `Compiler.KcFrontEnd` (the method is also a `[ModuleInitializer]`);[^kc-front-end]
2. stores `Tables`, creates `Workspace = new Workspace(workspaceFolder)` and
   `Bench = new BenchMachine(tables)` (the bench part), and sets `Out = output`;
3. creates `Editor = new EditorModel(Workspace, prepare)` where `prepare(file, text)` captures the
   target's part and layout **on the calling thread** (`Target.Part`,
   `_vehicle?.Layout ?? tables.BenchLayout`) and returns a closure that calls
   `BuildText(file, text, part, layout)`; the closure may then run on a thread-pool thread (see
   [build and load flow](/tooling/build-and-load-flow.md));
4. wires `Editor.OpenAt = (file, line) => { Edit(file); Editor.GoToLine = line; }` and
   `Editor.BuildLogged = line => Target.Console.Host(Target.LastK, Target.TickHz, line)`, so every
   editor build status line is appended to the target's console log.

Nothing touches the disk here: `Workspace`'s constructor only resolves the full path.

## The game's instance

The game never calls the constructor directly; `KfsIntegration.Workbench` builds one lazily:[^kfs-integration]

```csharp
_workbench = new Workbench(Tables, Path.Combine(Constants.DocumentsFolderPath, "kfs"), Output)
{
    ActiveVehicle = () => {
        Vehicle controlledVehicle = Program.ControlledVehicle;
        return (controlledVehicle == null) ? null : KfsVehicleHost.For(controlledVehicle);
    },
    ActiveVehicleIdSource = () => Program.ControlledVehicle?.Id
};
if (_registered) { _workbench.TickHostMs = 250L; _workbench.RunHostMsPerFrame = 4.0; }
_workbench.OpenRequested += (string _) => { ShowEditor(); };
```

Every read of `KfsIntegration.Workbench` also executes `_workbench.Out = Output`, so the workbench
follows a changed `KfsIntegration.Output` at the next property access.[^kfs-integration]
`KfsIntegration.UseWorkbench(Workbench)` (public) replaces the instance and subscribes the same
`OpenRequested` handler.[^kfs-integration] See
[game integration overview](/integration/game-integration-overview.md).

# Public properties

| member | type | meaning | Workbench.cs |
| --- | --- | --- | --- |
| `Tables` | `GameTables` (get) | the tables given to the constructor | 68 |
| `Workspace` | `Workspace` (get) | the workspace object | 70 |
| `Bench` | `BenchMachine` (get; private set) | the bench machine; **replaced by a new instance** when a load asks for another part | 72, 799, 934 |
| `Editor` | `EditorModel` (get) | editor text, diagnostics and the editor's build | 74 |
| `Out` | `TextWriter` (get; set) | where every printed line goes | 76 |
| `ActiveVehicle` | `Func<IVehicleTarget?>` (get; set) | returns the vehicle the player controls; default returns `null` | 78 |
| `ActiveVehicleIdSource` | `Func<string?>?` (get; set) | cheap id lookup used by `ActiveVehicleId` | 80 |
| `ActiveVehicleId` | `string?` | `ActiveVehicleIdSource()` when set, else `ActiveVehicle()?.Id` | 139-150 |
| `TargetVehicle` | `IVehicleTarget?` | the vehicle target, or `null` when the target is the bench | 96 |
| `Target` | `HostComputer` | `TargetVehicle.Computer` or `Bench` | 98-109 |
| `TargetsVehicle` | `bool` | whether the target is a vehicle | 111 |
| `TargetName` | `string` | `"bench"` or `"vehicle <Id>"` | 113-129 |
| `TargetVehicleId` | `string?` | the target vehicle's id | 131 |
| `TargetLayout` | `IoLayout` | the target vehicle's layout or `Tables.BenchLayout` | 66 |
| `TargetRunning` | `bool` | bench: `Bench.Running`; vehicle: snapshot state is `Boot`, `Running` or `Idle` | 152-163 |
| `TargetCanPause` | `bool` | true only for the bench | 165 |
| `TickHostMs` | `long` (get; set), default `long.MaxValue` | wall-clock limit for one `kfs tick n` | 167 |
| `RunHostMsPerFrame` | `double` (get; set), default `double.PositiveInfinity` | wall-clock limit per frame for a continuous bench run | 169 |
| `LastBuild` | `AssembleOutcome?` (private set) | result of the last console build or load | 133 |
| `CurrentBuild` | `AssembleOutcome?` | `Editor.Outcome ?? LastBuild` | 171 |
| `LoadedImage` | `byte[]?` (private set) | bytes of the image last handed to a target | 135 |
| `Loaded` | `AssembleOutcome?` (private set) | the build that produced `LoadedImage` | 137 |
| `LoadedLibraryFunctions` | `IReadOnlySet<string>` | names of library functions linked into `Loaded` (from its compile report); cached per `Loaded` | 173-184 |
| `CurrentProject` | `string?` | folder of the file open in the editor, or `null` for a root file or no file | 186-201 |
| `Parts` | `IReadOnlyList<PartDescription>` | `Tables.Parts` | 203 |

## Event

```csharp
public event Action<string>? OpenRequested;
```

Raised by `Edit`: with `""` when there is no file to reopen, with the workspace-relative file name
after a file was opened.[^workbench] The game's only subscriber shows the editor window.[^kfs-integration]
It is the only event on the class; build, load and start results are not observable through events.

# The target

The target is the private field `_vehicle` (`IVehicleTarget?`); `null` means the bench.[^workbench]

```csharp
public interface IVehicleTarget
{
    string Id { get; }
    VehicleComputer Computer { get; }
    IoLayout Layout { get; }
    bool IsAlive => true;
}
```

The game's implementation is `KfsVehicleHost` (see
[KfsVehicleHost](/integration/kfs-vehicle-host.md)).[^ivehicle-target][^kfs-vehicle-host]

| what changes the target | effect |
| --- | --- |
| `SelectTarget("vehicle")` | `_vehicle = ActiveVehicle()`; fails with `no active vehicle: take control of one first` when that is `null` |
| `SelectTarget("bench")` | `_vehicle = null` |
| `Load("<file> --vehicle")` → `LoadOntoVehicle` | `_vehicle =` the active vehicle |
| `Load("<file>")` → `LoadOntoBench` | `_vehicle = null` |
| `TargetActiveVehicle()` | `SelectTarget("vehicle")` when the target is the bench and `ActiveVehicleId != null`; the game calls it whenever the editor window becomes visible[^kfs-integration] |
| reading the target while `!_vehicle.IsAlive` | `_vehicle = null` and the line `vehicle <Id> is gone: the target is the bench` |

`Start`, `Stop`, `ResetTarget`, `Peek`, `Poke`, `State`, `Trace`, `WatchCommand` and the editor tabs all
act on `Target`.[^workbench] Two details matter to callers:

- `ActiveVehicle()` in the game calls `KfsVehicleHost.For(vehicle)`, which **creates** the host (and
  freezes its I/O layout) when the vehicle has none.[^kfs-integration][^kfs-vehicle-host] Selecting
  the vehicle target is therefore enough to attach a kFS host to the controlled vehicle.
  `ActiveVehicleIdSource` exists so the per-frame toolbar can show the id without doing that.
- `Upload` and `Load --vehicle` take the destination from `ActiveVehicle()` at that moment, not from
  `_vehicle`; if the player has switched vehicles, the load goes to the newly controlled one and
  `_vehicle` follows it.[^workbench]

# `Frame(double dtSeconds)`

Per-frame pump, called by the game from a static-window hook with `ImGui.GetIO().DeltaTime`, and only
while `KfsPrototype.Enabled` is true and a workbench exists.[^kfs-integration] In order:[^workbench]

1. `Bench.Advance(dtSeconds, RunHostMsPerFrame)` — runs the bench ticks that are due when a
   continuous run is active. `BenchMachine.Advance` stops early at `RunHostMsPerFrame` of wall time,
   at `MaxCyclesPerFrame` (2,000,000 cycles) or at the end of a timed run.[^bench-machine]
2. `ReportBenchFault()` — prints a new bench host fault once.
3. `Editor.Poll()` — applies a finished background build to the editor model.[^editor-model]
4. `FinishUpload()` — completes an `Upload` that was waiting for that build.
5. Every 0.5 s of accumulated `dtSeconds`: `Editor.CheckExternalChange()`.
6. Drains the `_pending` queue (a `ConcurrentQueue<string>` filled from vehicle worker threads) and
   prints each line.
7. Ages the snapshot waits created by `WithSnapshot`; a wait completes when its request ran on the
   vehicle computer or after 0.5 s (`SNAPSHOT_WAIT_SECONDS`), in which case the line
   `(the vehicle's computer was not dispatched, as when the simulation is paused: this is its last snapshot)`
   is printed first.

With the prototype flag off, `Frame` is not called: background editor builds are not applied, queued
vehicle replies are not printed, and a running bench does not advance.

## `TickHostMs` and `RunHostMsPerFrame`

Both default to "no limit"; the game sets 250 ms and 4.0 ms on its instance when the console
commands were registered (`BENCH_TICK_HOST_MS = 250`).[^kfs-integration]

| property | used by | effect |
| --- | --- | --- |
| `TickHostMs` | `Tick(arg)` → `Bench.Tick(n, TickHostMs)` | the loop stops after the first tick that ends past the limit; the workbench then prints `bench: stopped after <ran> of <n> ticks, which took <TickHostMs> ms; kfs run runs the bench in game time`[^workbench][^bench-machine] |
| `RunHostMsPerFrame` | `Frame` → `Bench.Advance(dt, RunHostMsPerFrame)` | at most that much wall time of bench ticks per frame (the first due tick always runs)[^bench-machine] |

# `WithSnapshot(Action verb)`

A vehicle computer publishes its `MachineSnapshot` only while someone wants it. `WithSnapshot` runs
`verb` immediately when the target is the bench or the vehicle's snapshot is not stale; otherwise it
enqueues `c.WantSnapshot(); c.Republish();` on the vehicle computer and runs `verb` from a later
`Frame` once that ran (or after the 0.5 s timeout).[^workbench][^host-computer] The console verbs
`report`, `state`, `prof`, `perf`, `nan` and `bus` are wrapped this way (see
[console commands](/tooling/console-commands.md)).

# Public methods

Unless stated, methods print their result through `Out` and return nothing useful. The exact strings
are listed per verb in [console commands](/tooling/console-commands.md); the build and load sequence is
traced in [build and load flow](/tooling/build-and-load-flow.md).

## Workspace and files

| signature | behaviour | Workbench.cs |
| --- | --- | --- |
| `void Ws(string? path)` | optional `Workspace.Set(path)` (quotes trimmed), `Workspace.Ensure()`, prints `workspace: <folder>` | 396-404 |
| `void Ls()` | prints each listed file with its size | 406-418 |
| `void Edit(string? file)` | opens a file in the editor model and raises `OpenRequested`; with no argument reopens `Editor.File ?? _lastFile` or raises `OpenRequested("")` | 420-451 |
| `void New(string? file)` | creates a root-level file from a template (default extension `.kasm`) and opens it | 453-476 |
| `string? NewFile(string? project, string? name)` | creates `project/name` (default extension `.c`); with no project, a new project named after the file; returns an error text or `null` | 2181-2233 |
| `string? NewProject(string? name, bool kc)` | creates `name/main.c` or `name/main.kasm`; returns an error text or `null` | 2263-2294 |
| `string? DeleteFile(string file)` | deletes the file, closes it if open, removes its folder when left empty; returns an error text or `null` | 2235-2261 |
| `void ExamplesCommand(string? args)` | writes the embedded examples (`--force`, `--all`) | 2135-2140 |
| `bool CopyExample(string name)` | writes one example folder and opens its main file | 2142-2154 |
| `(string Folder, List<string> Written) WriteHeaders(string? file)` | writes `vehicle.h` and `vehicle.kinc` for the current target into the file's project folder (or the workspace root) | 207-214 |
| `void Headers(string? file)` | `WriteHeaders` plus a report line; file defaults to `_lastFile ?? Editor.File` | 264-268 |

## Building

| signature | behaviour | Workbench.cs |
| --- | --- | --- |
| `AssembleOutcome BuildText(string file, string text, PartDescription part, IoLayout layout)` | the build itself: project discovery, compile or assemble, image validation, listing; prints nothing; thread-safe enough to be run from `Task.Run` by the editor | 483-587 |
| `AssembleOutcome AssembleText(...)` | alias of `BuildText` | 478-481 |
| `AssembleOutcome? Build(string? file, PartDescription? part = null, IoLayout? layout = null)` | console build: refreshes the generated headers, builds (using the editor's unsaved text when that file is open), stores `LastBuild`, prints diagnostics and the size summary | 659-678 |
| `AssembleOutcome? Asm(...)` | alias of `Build` | 654-657 |
| `AssembleOutcome? Preview(string? partName)` | builds the open file for another part and stores it in `Editor.Preview`; `off` clears it; nothing is loaded | 1915-1958 |
| `void Report(string? file)` | prints the compile report view of `CurrentBuild`, or of a quiet rebuild of `file` | 1833-1846 |
| `void Lines(string? file)` | quiet rebuild, then per-line cycle estimates and bus markers | 1859-1913 |
| `Definition? Def(string? name)` | finds a function or global of `CurrentBuild`, prints it, opens the file at the line | 1960-1993 |
| `void Manual(string? code)` | prints `DiagnosticManual.Page(Tables, code, hint)` | 2123-2133 |

## Loading and running

| signature | behaviour | Workbench.cs |
| --- | --- | --- |
| `bool Load(string? args)` | parses `<file> [--part <name>] [--vehicle]`, builds for the destination, loads; switches the target to the destination | 739-807 |
| `bool Upload()` | editor path: save, reuse or redo the editor's build, load onto the current target | 856-878 |
| `bool LoadIntoTarget(string file)` | `Load(TargetsVehicle ? file + " --vehicle" : file)` | 939-942 |
| `bool SelectTarget(string? which)` | `bench`, `vehicle`, or empty to just print the target | 944-971 |
| `bool TargetActiveVehicle()` | see "The target" | 1066-1073 |
| `void Start()` | vehicle: queues `TryStart(DispatchTimeNs, ResetCause.PowerOn)`; bench: `Bench.Start()` | 973-989 |
| `void Stop()` | vehicle: queues `c.Stop()`; bench: `Bench.Stop()` (ends a continuous run, state unchanged) | 1004-1020 |
| `void StopTarget()` | vehicle: `Stop()`; bench: `Bench.PowerDown()` (back to `Standby`) | 1055-1064 |
| `void Play()` | vehicle: `Start()`; bench: start if `Standby` or `Halted`, then `Bench.Run(0.0)` (run until stopped) | 1022-1041 |
| `void Pause()` | bench only: `Bench.Stop()`; a vehicle computer cannot pause | 1043-1053 |
| `void ResetTarget()` | vehicle: queues `TryStart(..., ResetCause.Player)`; bench: `Bench.Reset()` | 1075-1091 |
| `void Tick(string? arg)` | bench only: run `n` ticks now (default 1) | 1093-1132 |
| `void Run(string? arg)` | bench only: continuous run, optionally for a number of machine seconds | 1143-1166 |
| `void Frame(double dtSeconds)` | see above | 1168-1200 |

## Inspecting the target

| signature | behaviour | Workbench.cs |
| --- | --- | --- |
| `void WithSnapshot(Action verb)` | see above | 1202-1221 |
| `void State()` | multi-line state dump of `Target.Snapshot` plus the last 5 console log lines | 1223-1351 |
| `void Perf()` | `PerfReport.Lines(Target.Snapshot)` plus the last editor build time | 1353-1366 |
| `void Prof()` | `ProfilerView.From(snapshot).Lines()` | 1368-1381 |
| `void Disasm(string? function)` | whole-image kASM, or one function's listing; **not bound to a console verb** (the `disasm` verb calls `DisasmView`) | 1383-1408 |
| `void DisasmView(string? args)` | `[function] [--source] [--copy file.kasm]` | 2027-2087 |
| `void Trace(string? channel)` | lists channels, watched globals and plotted fields, or prints a channel's last 10 samples | 1410-1454 |
| `void WatchCommand(string? arg)` | list, `name` to watch, `-name` to stop | 1995-2017 |
| `void NanCommand()` | `NanView.Lines(Target.Snapshot)` | 2019-2025 |
| `void BusCommand(string? args)` | bus monitor, one message's fields, `plot` / `unplot` / `plots` | 270-342 |
| `void Msg(string? name)` | a message's definition in the target layout | 344-362 |
| `bool TryResolve(string text, out uint address, out string? why)` | `number`, `0xHEX`, `symbol`, or any of those `+ offset`; symbols are debug globals of the loaded image, constants of the built-in kASM includes (`kfs.kinc`, `vehicle.kinc`), then command-field and sensor-field names of the layout (only the bench layout has sensor fields) | 1456-1511 |
| `uint[]? Read(uint address, int count, out string? why)` | reads `count` little-endian 32-bit words from the **target** machine | 1522-1525 |
| `static uint[]? Read(KvmMachine vm, uint address, int count, out string? why)` | region by address: RAM below `0x80000000`, sensor block `0x80000000`, command block `0x90000000`, system block `0xA0000000`; `0xB0000000` and above is `no such region` | 1527-1566 |
| `void Peek(string? args)` | `<symbol\|address> [count 1..64]`; on a vehicle the read is queued and the answer arrives through `_pending` | 1568-1606 |
| `static byte[]? ParseValue(string s)` | `42`, `0x2A`, `1.5f` (float32), `1.5d` (float64), or a bare number containing `.` (float32) | 1638-1667 |
| `void Poke(string? args)` | `<symbol\|address> <value>`; only RAM and the command block are writable | 1669-1732 |

## Editor support

These exist for the [editor window](/tooling/editor-window.md) and print nothing.

| signature | behaviour | Workbench.cs |
| --- | --- | --- |
| `List<LogLine> ConsoleLines()` | `Target.Console.Snapshot()` (a copy) | 1734-1737 |
| `void ConsoleTail(int count, List<LogLine> into, ref (object? Console, long Version) version)` | refills `into` with the last `count` lines only when the log object or its `Version` changed | 1739-1746 |
| `void WantRam()` | asks the target to include RAM in snapshots (1 s lease); republishes the bench at once | 1748-1756 |
| `MachineSnapshot? RunningSnapshotOf(AssembleOutcome? outcome)` | the target snapshot when `outcome.Image` equals `LoadedImage` byte for byte, else `null` | 1758-1771 |
| `ReportView? EditorReportOf(AssembleOutcome? outcome)` | the outcome's prebuilt `ReportView`, else `ReportViewOf` | 1773-1780 |
| `ReportView? ReportViewOf(AssembleOutcome? outcome)` | builds a report view, with measured maxima when the outcome is the loaded image | 1813-1831 |
| `IReadOnlyList<DisassemblyFunction> DisassemblyOf(KvmImage image)` | disassembly view of an image, reusing `Loaded.Disassembly` when it is the same image; cached | 1782-1811 |
| `bool OpenSource(string file, int line)` | maps a build-relative file to the workspace, opens it and sets `Editor.GoToLine` | 2089-2101 |
| `string[]? SourceLines(string file)` | source lines for the disassembly view; reports an unreadable file once | 2103-2121 |

# Private members worth knowing

| member | role | Workbench.cs |
| --- | --- | --- |
| `Print(string line)` | `Out.WriteLine(line)`; the only printing primitive besides direct `Out.Write(string)` in `State`, `Disasm`, `DisasmView` and `Manual` | 391-394, 1350, 1393, 1406, 2073, 2086, 2132 |
| `_pending` (`ConcurrentQueue<string>`) | lines produced on a vehicle worker thread, printed by `Frame` | 36, 1181-1184 |
| `_lastFile` | last file named to `Edit`, `Build` or a load; the default for most verbs | 40 |
| `_upload` | `(File, Version)` of an `Upload` waiting for the background build | 50, 874 |
| `ReportHeaders(file, always)` | header refresh with its report line | 216-262 |
| `Report(file, outcome, part, layout)` | prints diagnostics and the size / task / function / report summary | 680-737 |
| `LoadOntoVehicle`, `LoadOntoBench`, `LoadTarget`, `LoadBuilt`, `FinishUpload`, `QueueStart` | the load and start plumbing | 809-937, 991-1002 |
| `BuildQuiet(file)` | build without printing, used by `Report` and `Lines` | 1848-1857 |
| `TemplateFor(file)` | new-file text by extension | 2156-2179 |

# Threading

- All public methods are written for the main thread; the console and the ImGui windows call them
  there. `Frame` is the only place that prints lines produced elsewhere.[^workbench]
- Bench operations are synchronous on the caller.
- Vehicle operations that change or read the machine (load, start, stop, reset, peek, poke, snapshot
  republish) never touch the vehicle computer directly. They call
  `vehicle.Computer.Enqueue(Action<HostComputer>)`; the queue is a `ConcurrentQueue` drained by
  `HostComputer.DrainQueue()` on the thread that dispatches that computer.[^host-computer] That is
  why vehicle verbs answer `queued ...` and why `Load` on a vehicle returns `true` before the load
  has actually been attempted (see [dispatch and threading](/integration/dispatch-and-threading.md)).
  The other vehicle verbs (`watch`, `bus plot`, `trace`, the snapshot verbs) do call the computer
  from the main thread while a dispatch may be in flight, through members that take their own lock
  (`Watch`, `Unwatch`, `BusTracker.Plot`, `Traces`, `Console`) or through the published `Snapshot`.
  [^host-computer]
- `BuildText` is the one method the editor runs off the main thread (`Task.Run`), with part and
  layout captured beforehand.[^editor-model]

# Modding notes

- **Reachability.** `KfsIntegration.Workbench` is `public static`; `Workbench`, `Workspace`,
  `EditorModel`, `BenchMachine`, `IVehicleTarget`, `AssembleOutcome` and `BuildProject` are all
  `public`. A mod in another assembly can call every method listed under "Public methods" with no
  reflection.[^workbench][^kfs-integration]
- **Driving kFS programmatically.** Build, load and start are plain calls on the main thread:
  `Workbench.Load("proj/main.c --vehicle")` then `Workbench.Start()`; `Workbench.Edit(file)` opens the
  editor through `OpenRequested`. The calls bypass the prototype-flag check that the console applies
  (`KfsConsoleCommands.On()`), but nothing dispatches a vehicle computer while
  `KfsPrototype.Enabled` is false, so a mod should set the flag first (see
  [feature flag](/integration/feature-flag.md)).
- **Results are text.** Success and failure are only printed; `Load` returns `bool`, `Build` returns
  the `AssembleOutcome` (inspect `Ok` and `Diagnostics`), `NewFile` / `NewProject` / `DeleteFile`
  return an error string. For a vehicle, `Load` returning `true` means "queued": confirm through
  `Workbench.Target.Snapshot.State` or the target's console log.
- **Capturing output.** Replace `KfsIntegration.Output` with a `TextWriter` that tees to the original;
  it must implement both `WriteLine(string)` and `Write(string)` (multi-line strings ending in
  `\n`), because `State`, `Disasm`, `DisasmView` and `Manual` use `Write`.[^workbench] The workbench
  picks the new writer up at the next `KfsIntegration.Workbench` read. Program output (`kfs_logf`,
  host events) does **not** go through `Out`; it is in `Target.Console` (see
  [console commands](/tooling/console-commands.md), "Output channels").
- **No extension hooks.** There is no verb registry on `Workbench`, no virtual member, and one
  event. Adding behaviour means calling it from outside or Harmony-patching specific methods.
  `ActiveVehicle`, `ActiveVehicleIdSource`, `Out`, `TickHostMs` and `RunHostMsPerFrame` are the
  settable seams.
- **`Bench` is replaced, not mutated,** when a load names another part; do not cache the reference.
- **`ActiveVehicle()` has a side effect in the game:** it attaches a `KfsVehicleHost` to the controlled
  vehicle and freezes its layout.

[^workbench]: Workbench.cs — fields :36-64; properties :66-203; `OpenRequested` :205; constructor :364-389; `Print` :391-394; `VehicleTarget` (drops a dead vehicle) :82-94; `Edit` :420-451; `Load` :739-807; `LoadOntoVehicle` :809-822; `LoadOntoBench` :824-835; `LoadTarget` :837-854; `Upload` :856-878; `SelectTarget` :944-971; `Tick` :1093-1132; `Frame` :1168-1200; `WithSnapshot` :1202-1221; `State` (uses `Out.Write`) :1223-1351; `TryResolve` :1456-1511; `Read` :1522-1566; `Peek` :1568-1606; `Poke` :1669-1732.
[^kfs-integration]: KfsIntegration.cs — `FrameHook` (calls `Frame`, shown only while the flag is on) :15-37; `BENCH_TICK_HOST_MS` :83; `Output` :91; `Workbench` getter :93-121; `UseWorkbench` :137-144; `RegisterCommands` :146-156; `ShowEditor` / `ToggleEditor` (call `TargetActiveVehicle`) :183-214.
[^ivehicle-target]: IVehicleTarget.cs:5-14.
[^bench-machine]: BenchMachine.cs — `DEFAULT_MAX_CYCLES_PER_FRAME` :11; `Tick(n, maxHostMs)` :84-110; `Run` :127-136; `Advance` :138-183; `Stop` / `PowerDown` :67-77.
[^host-computer]: HostComputer.cs — `Console` :135; `WantSnapshot` :229-232; `Enqueue` :288-291; `DrainQueue` :293-300; `SnapshotIsStale` :176; `Watch` / `Unwatch` (under `_watchGate`) :595-623. BusTracker.cs — `Plot` / `Unplot` (under `_gate`) :228-262.
[^kfs-vehicle-host]: KfsVehicleHost.cs — class declaration (`: IVehicleTarget`) :19; `Layout => Frozen.Layout` :67; constructor (frozen controller set :109, bench part :110) :105-118; `For` :139-142.
[^editor-model]: EditorModel.cs — `Assemble` (background `Task.Run`) :406-427; `Poll` :429-449.
[^kc-front-end]: KcFrontEnd.cs — `[ModuleInitializer] Register()` :25-29.
