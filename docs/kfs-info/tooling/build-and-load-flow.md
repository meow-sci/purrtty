---
type: Subsystem Reference
title: kFS build and load flow
description: The exact call chain from a source file to a running program — project discovery, compile or assemble, image validation, loading onto the bench or a vehicle, start, stop and reset — with where diagnostics go and how layout and part mismatches are handled.
resource: ksa-decomp:KSA.Kfs.Host.Workbench/Workbench.cs
tags: [kfs, tooling, build, load, diagnostics, layout]
sources:
  - id: workbench
    resource: ksa-decomp:KSA.Kfs.Host.Workbench/Workbench.cs
    title: Workbench (decompiled)
  - id: editor-model
    resource: ksa-decomp:KSA.Kfs.Host.Workbench/EditorModel.cs
    title: EditorModel (decompiled)
  - id: assemble-outcome
    resource: ksa-decomp:KSA.Kfs.Host.Workbench/AssembleOutcome.cs
    title: AssembleOutcome (decompiled)
  - id: listing
    resource: ksa-decomp:KSA.Kfs.Host.Workbench/Listing.cs
    title: Listing and ListingLine (decompiled)
  - id: workspace
    resource: ksa-decomp:KSA.Kfs.Host.Workbench/Workspace.cs
    title: Workspace (decompiled)
  - id: console-commands
    resource: ksa-decomp:KSA.Kfs/KfsConsoleCommands.cs
    title: KfsConsoleCommands (decompiled)
  - id: editor-window
    resource: ksa-decomp:KSA.Kfs/KfsEditorWindow.cs
    title: KfsEditorWindow (decompiled)
  - id: host-computer
    resource: ksa-decomp:KSA.Kfs.Host/HostComputer.cs
    title: HostComputer (decompiled)
  - id: vehicle-computer
    resource: ksa-decomp:KSA.Kfs.Host/VehicleComputer.cs
    title: VehicleComputer (decompiled)
  - id: bench-machine
    resource: ksa-decomp:KSA.Kfs.Host/BenchMachine.cs
    title: BenchMachine (decompiled)
  - id: image-reader
    resource: ksa-decomp:KSA.Kfs.Vm.Image/ImageReader.cs
    title: ImageReader (decompiled)
  - id: kfs-vehicle-host
    resource: ksa-decomp:KSA.Kfs/KfsVehicleHost.cs
    title: KfsVehicleHost (decompiled)
  - id: compile-options
    resource: ksa-decomp:KSA.Kfs.Compiler/CompileOptions.cs
    title: CompileOptions (decompiled)
  - id: diagnostic
    resource: ksa-decomp:KSA.Kfs.Compiler/Diagnostic.cs
    title: Diagnostic (decompiled)
  - id: game-tables
    resource: ksa-decomp:KSA.Kfs.Vm.Tables/GameTables.cs
    title: GameTables (decompiled)
  - id: offline-harness
    resource: /modding/offline-harness.md
    title: Offline harness experiments E1–E12
generated: { by: claude-code/claude-fable-5-2, at: 2026-11-25T21:56:52Z }
status: stable
ksa_build: "2026.11.25.5949"
---

# Overview

```text
source file(s) in the workspace
   │  Workspace.ProjectOf            which files form the program
   ▼
BuildProject ──► Workbench.BuildText ──► Compiler.Compile (kC)  or  Assembler.Assemble (kASM)
                        │                         │
                        │                         ▼
                        │                 image bytes + diagnostics + compile report + IR module
                        ▼
                 ImageReader.Read    validate against part + tables + I/O layout
                        ▼
                 AssembleOutcome     in memory only: image, listing, report, estimates
                        │
        ┌───────────────┴────────────────┐
        ▼                                ▼
 Bench.LoadBench(image)          vehicle.Computer.Enqueue(c => c.Load(image, layout))
 synchronous                     queued; runs when the vehicle's computer is next dispatched
        ▼                                ▼
 Bench.Start() + Tick/Run        TryStart(DispatchTimeNs, cause) on the tick grid
```

Everything is orchestrated by [Workbench](/tooling/workbench.md). The compiler and assembler
internals are in [compile pipeline](/compiler/compile-pipeline.md) and
[kASM assembly language](/compiler/kasm-assembly-language.md); the image format in
[kVM image format](/compiler/kvm-image-format.md).

# Entry points

| user action | call | builds how | loads? |
| --- | --- | --- | --- |
| `kfs build [file]` | `Workbench.Build(file)` | synchronous on the main thread | no |
| `kfs load [file] [--part name] [--vehicle]` | `Workbench.Load(args)` → `Asm` → `Build` | synchronous | yes |
| editor Save (`Ctrl+S`, File > Save) | `EditorModel.Save()` → `Assemble()` | background `Task.Run` | no |
| editor Build button | `EditorModel.Assemble()` (does not save) | background | no |
| editor Upload button | `Workbench.Upload()` | reuses the editor build, or background, or synchronous | yes |
| `kfs preview <part>` / editor part combo | `Workbench.Preview(part)` | synchronous, for another part | never |
| `kfs report <file>`, `kfs lines [file]` | `Workbench.BuildQuiet(file)` | synchronous, prints no diagnostics | no |

[^console-commands][^workbench][^editor-model][^editor-window]

# Step 1 — which files: `Workspace.ProjectOf`

`BuildText(file, text, part, layout)` starts with
`Workspace.ProjectOf(file, file, text, out why)`.[^workbench][^workspace] The rules (subfolder = project;
root-level file = itself plus its quoted includes; headers cannot be built) are in
[workspace and files](/tooling/workspace-and-files.md). `text` replaces the disk content of `file`, so
unsaved editor text is what gets built.

When no project results (a header was named, or reading failed with an `IOException`) the outcome is
a failed `AssembleOutcome` carrying one synthetic diagnostic:
`Diagnostic(Severity.Error, "kfs", file, 0, 0, why ?? "nothing to build")`.[^workbench]

# Step 2 — compile or assemble

`BuildProject.IsKc` (true when the project has a `.c` file) picks the tool.[^workbench]

## kC

```csharp
CompileResult r = Compiler.Compile(new CompileInput {
    Files = project.Files, Part = part, Game = Tables, Layout = layout, ProjectName = project.Name
});
```

`Options` and `SystemFiles` are left at their defaults, so every in-game build uses
`BoundsChecks = true`, `Optimize = 1`, `DoubleBits = 64`, no `-D` defines, `EmitDebug = true`,
`TimeLimitMs = 30000`.[^workbench][^compile-options] There is no way to change compile options from
the console or the editor. `.kasm` files in the project are assembled by the kC front end and linked
in.

## kASM

```csharp
AssembleResult r = new Assembler(Tables, part, layout) {
    ProjectFiles = name => (folder.Length > 0 ? Workspace.ProjectFile(folder + "/" + name) : null)
                           ?? Workspace.ProjectFile(name)
}.Assemble(project.Files where the name ends with ".kasm", as SourceFile(name, text));
```

All `.kasm` files of the project are assembled together; quoted `.include` files are looked up in the
project folder and then at the workspace root.[^workbench]

Both tools return image bytes (used only when `Ok`), diagnostics, an optional `CompileReport` and an
`IrModule`. A failed build becomes
`new AssembleOutcome(Ok: false, diagnostics, null, 0, 0, [], elapsed) { Project, Language, Part, Module }`.
[^workbench]

# Step 3 — validate the image

The fresh bytes are immediately read back with
`ImageReader.Read(image, part, Tables, layout, out KvmImage image)`.[^workbench] If that fails the
outcome is failed and one more diagnostic is appended:

| field | value |
| --- | --- |
| code | `KC-E900` (kC) or `KA-E900` (kASM) |
| message | `the built image fails validation: <rule>: <message>` |
| hint | `Report this with the source.` |

`Inferred:` this is an internal-consistency check (compiler output rejected by the loader), not a
user-facing error class; it would also fire if tables were mutated between emit and read.

# Step 4 — the outcome

```csharp
public sealed record AssembleOutcome(bool Ok, IReadOnlyList<Diagnostic> Diagnostics, byte[]? Image,
    uint CodeSize, uint RamRequired, IReadOnlyList<ListingLine> Listing, TimeSpan Elapsed)
```

| init-only property | content | set by `BuildText` on success from |
| --- | --- | --- |
| `Report` | `CompileReport?` | the tool's report |
| `Estimates` | `IReadOnlyList<LineCost>` (per-line cycle estimates) | `report.Estimates` |
| `Module` | `IrModule?` | the tool's IR module (used for go-to-definition) |
| `Project` | `BuildProject?` | step 1 |
| `Language` | `"kC"` or `"kASM"` (default `"kASM"`) | `project.IsKc` |
| `Part` | part **name** the build targeted | `part.Name` |
| `BusAccesses` | computed: `Report?.BusAccesses` | — |
| `ReportView` | prebuilt view for the Report tab | `ReportView.From(report, image, null, Definitions.TasksReachingKasm(module), MessageName)` |
| `Disassembly` | prebuilt view for the Disassembly tab | `DisassemblyView.Build(listing, source lookup)` |
| `BusDefinitions` | message name → definition lines, for each message the program touches | `BusMonitorView.Definition(Tables, layout, message)` |
| `Layout` | the `IoLayout` **object** the build targeted | `layout` |
| `SourceStamps` | `(path, last write UTC)` of the other project files | `SourceStampsOf` |

`CodeSize` and `RamRequired` come from the validated image (`image.CodeSize`, `image.RamRequired`).
[^assemble-outcome][^workbench] The view types are documented in [views](/host/views.md).

Nothing is written to disk: the image lives in `AssembleOutcome.Image`.

## `Listing`

`Listing.Build(image, tables)` produces one `ListingLine(uint Pc, int Cost, string Function, string Text, string? File, int Line)`
per instruction, attaching the source file and line from the image's debug line table.[^listing]
`Listing.Text` formats an instruction:

| instruction | text |
| --- | --- |
| `JMP`, `JZ`, `JNZ` | `<mnemonic> 0x<target pc, hex>` |
| `CALL`, `CALLF` | `<mnemonic> <function name>` (or `0x<pc>` when no function starts there) |
| `ENTER` | `ENTER <A>, <B>` |
| no immediate | the mnemonic |
| first immediate is `U32` or `U64` | `<mnemonic> 0x<A, HEX>` |
| otherwise | `<mnemonic> <A, decimal>` |

`Listing.Format(lines)` renders `name:` headers and rows `  <pc:x,6>  <cost,5>  <text,-32>; <file>:<line>`;
it is what `Workbench.Disasm(function)` prints, and the editor's Listing tab draws the same rows.
[^listing][^workbench] The disassembler proper is in [kASM disassembler](/compiler/kasm-disassembler.md).

# Where diagnostics go

A diagnostic is `Diagnostic(Severity Severity, string Code, string File, int Line, int Col, string Message, string? Hint)`.
[^diagnostic]

| path | destination |
| --- | --- |
| `kfs build`, `kfs load`, editor Upload | printed through `Workbench.Out` by the private `Report(file, outcome, part, layout)`: one line `<file>:<line>:<col>: error\|warning <code>: <message>` per diagnostic, plus `  hint: <hint>`[^workbench] |
| editor Save / Build | `EditorModel.Diagnostics` (Diagnostics tab, which turns red when non-empty); `EditorModel.Status`; and `BuildLogged(Status)`, which appends the status line to the **target's console log**[^editor-model][^workbench] |
| background build threw | `EditorModel.Status = "build failed: <message>"`[^editor-model] |
| `kfs preview` failure | first five errors as `  <file>:<line>: <code>: <message>`[^workbench] |
| `kfs report <file>`, `kfs lines` | not printed; only `the last build failed; no report` / `<file>: the build failed (kfs build <file> shows why)`[^workbench] |
| explanation of a code | `kfs manual <code>` or hovering a row in the Diagnostics tab: `DiagnosticManual.Page(Tables, code, hint)`, generated from `diagnostics.json`[^workbench][^editor-window] |

After a successful console build `Report` also prints:[^workbench]

```text
<file> (<kC|kASM>[: f1, f2, ...]): <code> bytes of code, <ram> bytes of RAM, image <n> bytes, tick_hz <hz>, budget <cycles> cycles, <ms> ms on <part>
  <task kind> <task name>: RAM stack <n> B, return depth <n>, operand cells <n>          (per task)
  func <name>: frame <n> B, <n> instructions, <n> cycles straight through               (per function not flagged FN_LIBRARY)
  | <compile report text line>                                                          (per report line)
```

and after a failed one `<file>: <n> error(s), <ms> ms`. Editor status texts are
`compiled <file>: <code> bytes of code, <ram> bytes of RAM, <ms> ms` (kC),
`assembled <file>: ...` (kASM) or `<n> error(s)`.[^editor-model]

See [diagnostics table](/tables/diagnostics-table.md) and
[diagnostics infrastructure](/compiler/diagnostics-infrastructure.md).

# Console build: `Workbench.Build`

`kfs build [file]` → `KfsConsoleCommands.Execute` → verb `build` → `Workbench.Build(a)`.[^console-commands]
In order:[^workbench]

1. `file` defaults to `_lastFile`; none → `usage: kfs build <file> (or kfs edit a file first)`.
2. Missing file → `<file>: not found in <workspace>`.
3. `_lastFile = file`.
4. `ReportHeaders(file, always: false)` refreshes `vehicle.h` / `vehicle.kinc` on disk.
5. Text = `Editor.Text` when that exact file is open in the editor, else the disk text.
6. `LastBuild = BuildText(file, text, part ?? Target.Part, layout ?? VehicleTarget?.Layout ?? Tables.BenchLayout)`.
7. `Report(...)` prints diagnostics and the summary.

The build targets whatever the **current target** is: the bench part and layout, or the target
vehicle's layout.

# Editor build: `EditorModel.Assemble`

The editor window sets `Editor.Background = true` in its constructor.[^editor-window] `Assemble()`
then:[^editor-model]

1. calls `_prepare(File, Text)` on the main thread, which captures the target's part and layout
   (see [Workbench](/tooling/workbench.md), "Construction");
2. remembers the file and `TextVersion`, starts `Task.Run(build)`, sets status `building <file> …`
   and returns `null`;
3. on a later frame `Workbench.Frame` → `Editor.Poll()` sees the completed task and calls `Apply`:
   `Outcome`, `Diagnostics`, `ListingLines`, `LastAssembleMs`, `LineCosts`, `Definitions` (on success),
   `Status`, `BuildLogged`; then `OutcomeTextVersion = <captured version>`. A result for a file that is
   no longer open is discarded.

With `Background == false` (the default without the editor window) the same steps run synchronously.
`EditorModel.Wait()` blocks on a pending build.

Because polling happens in `Workbench.Frame`, a background build is never applied while the
prototype flag is off.

# Load

## `Workbench.Load(args)` (console)

Arguments are split on spaces: `--part <name>` and `--vehicle` are flags, any other token is the
file (the last one wins); file names with spaces cannot be passed.[^workbench]

1. No file and no `_lastFile` → `usage: kfs load <file> [--part <name>] [--vehicle]`.
2. `--part` resolves through `Tables.Part(name)`, which accepts the bench part's name, a part's full
   name or its short name; unknown →
   `unknown part '<x>'; parts: <names>`.[^workbench][^game-tables] Default part: `Tables.BenchPart`.
3. **With `--vehicle`:** `ActiveVehicle()` (null → `no active vehicle`); build with
   `Asm(file, part, vehicle.Layout)`; on success `LoadOntoVehicle(vehicle, outcome)`.
4. **Without:** if the part differs from the bench's, `Bench = new BenchMachine(Tables, part)`; build
   with `Asm(file, Bench.Part, Tables.BenchLayout)`; on success `LoadOntoBench(outcome)`.

So a load **always rebuilds from source** for its destination. The target switches to the
destination (bench or that vehicle).

## `LoadOntoBench`

`_vehicle = null`; `Bench.LoadBench(image)` runs synchronously (`Load(image, Tables.BenchLayout)`); on
success `LoadedImage` / `Loaded` are set. Printed:
`loaded onto the bench machine (<part>): Standby; kfs start boots it` or
`bench load rejected: <rule>: <message>`.[^workbench][^bench-machine]

## `LoadOntoVehicle`

`_vehicle = target`, `LoadedImage` / `Loaded` are set **immediately**, and the real load is queued:
`target.Computer.Enqueue(c => c.Load(image, layout))` with `layout = target.Layout`. Printed:
`queued load onto vehicle <id> (<n> thrusters, layout 0x<fingerprint>); kfs start boots it`. The
method returns `true` without knowing the result.[^workbench]

The queued action runs when the vehicle's computer drains its queue during dispatch
(see [dispatch and threading](/integration/dispatch-and-threading.md)). `VehicleComputer.Load` first
applies `Ownership.NeedsStock(image)` (rule `needs-stock`: the program owns a control block without
claiming `ALLOCATION`, or subscribes to `MSG_ATT_TARGET` / `MSG_GUIDANCE_STATUS` without publishing
it); then `HostComputer.Load` refuses a program that publishes a host-published message
(`host-message`) and whatever `KvmMachine.LoadImage` rejects. Every refusal is written only to that
computer's console log as `load rejected: <rule>: <message>`; a success logs
`loaded <n> bytes on <part>: tick_hz <hz>, budget <cycles> cycles, <n> task(s), layout 0x<fingerprint>`
and leaves the computer in `Standby`.[^vehicle-computer][^host-computer]

## Editor Upload: `Workbench.Upload`

1. No open file → `false`. `LoadTarget` picks the destination: bench part and layout, or — when the
   target is a vehicle — `ActiveVehicle()` and its layout (`no active vehicle` when null).
2. `Editor.Save(build: false)` writes the file.
3. `Editor.BuildOf(textVersion, part, layout)` returns the editor's existing build if it succeeded
   **for this text version, this part name and this exact layout object**, the other project files'
   time stamps are unchanged and the folder still holds the same sources → `LoadBuilt(file, outcome)`.
4. Otherwise, if the destination's part or layout differs from what an editor build would capture,
   `LoadIntoTarget(file)` does a synchronous console-style `Load`.
5. Otherwise `_upload = (file, textVersion)` and `Editor.Assemble()` starts a background build;
   `FinishUpload()` (called now and from every `Frame`) waits until `Editor.Building` is false, then
   loads the build through `LoadBuilt`, prints
   `<file>: the build failed, so nothing was loaded (see Diagnostics)`, or falls back to
   `LoadIntoTarget(file)`.

`LoadBuilt` sets `_lastFile` and `LastBuild`, refreshes the generated headers, prints the same build
report as a console build, then calls `LoadOntoVehicle` or (replacing the bench if the part differs)
`LoadOntoBench`.[^workbench][^editor-model]

# Start, stop, reset

| call | bench | vehicle |
| --- | --- | --- |
| `Start()` (`kfs start`) | `nothing loaded (kfs load first)` when `NoSoftware`; else `Bench.Start()` and `bench: <state>`. Starting boots the machine but runs **no ticks**: follow with `kfs tick` or `kfs run` | queues `((VehicleComputer)c).TryStart(c.DispatchTimeNs, ResetCause.PowerOn)`; prints `queued start on vehicle <id>`; a failed start later prints `vehicle <id>: nothing loaded (kfs load <file> --vehicle first)` |
| `Play()` (editor play button) | starts if `Standby` or `Halted`, then `Bench.Run(0.0)`; `bench: running from k <k>` | same as `Start()` |
| `Tick(n)` (`kfs tick`) | `Bench.Tick(n, TickHostMs)`; prints ticks run, `k`, state, last tick's cycles and any trap | refused: `a vehicle computer ticks on its own grid once started; use the bench for kfs tick` |
| `Run(seconds)` (`kfs run`) | `Bench.Run(seconds)`; advanced by `Frame` | refused: `a vehicle computer runs in simulation time once started` |
| `Pause()` (editor) | `Bench.Stop()`; `bench: paused at k <k>` | refused with an explanation |
| `Stop()` (`kfs stop`) | `Bench.Stop()` ends a continuous run, machine state kept; `bench: run stopped at k <k>` | queues `c.Stop()`; `queued stop on vehicle <id>: the stock flight computer resumes` |
| `StopTarget()` (editor stop button) | `Bench.PowerDown()` → `Standby`; `bench: <state>` | same as `Stop()` |
| `ResetTarget()` (`kfs reset`, editor reset) | `Bench.Reset()`; `bench: reset, <state>` | queues `TryStart(..., ResetCause.Player)`; `queued reset on vehicle <id>` |

[^workbench][^bench-machine]

On a vehicle, `TryStart` resets the VM, creates the tick grid aligned to the dispatch time, clears
counters and recomputes the control mode and owned blocks; `Stop` returns the computer to `Standby`,
drops the grid and hands control back to the stock flight computer.[^vehicle-computer] Details:
[vehicle computer](/host/vehicle-computer.md), [tick grid and timing](/host/tick-grid-and-timing.md),
[ownership and control modes](/host/ownership-and-control-modes.md),
[flight computer interplay](/integration/flight-computer-interplay.md).

Two further ways to stop a vehicle program exist outside `Workbench`: `kfs kill` (the kill switch)
and `kfs off` (the prototype flag); see [console commands](/tooling/console-commands.md).

# Layout and part mismatches

An image records the fingerprint of the I/O layout and the properties of the part it was built for.
`ImageReader.Read` rejects a mismatch:[^image-reader]

| rule | message |
| --- | --- |
| `part` | `the image was built for another part or cost table; rebuild from source` |
| `layout` | `the image was built for another I/O layout; rebuild from source` |

How the tooling avoids and handles this:

- **It never reuses an image across destinations.** `Load` rebuilds for the destination every time;
  `Upload` reuses a build only when `outcome.Part == part.Name` and `outcome.Layout` is the same
  object as the destination's layout.[^workbench][^editor-model]
- **A vehicle's layout is frozen when its host is created.** `KfsVehicleHost.Layout` returns
  `Frozen.Layout`, and `Frozen` is assigned once in the constructor.[^kfs-vehicle-host] The layout
  the workbench builds against and the layout it passes to `c.Load(image, layout)` are therefore the
  same object, so a stock load cannot hit the `layout` rule.
- **When the vehicle changes afterwards** (staging, docking, lost parts) the host keeps the frozen
  layout for the running program, replaces its `Current` controller set, and logs
  `layout change: <n> element(s) of the program's frozen layout are inert (integration §2.4)` to the
  computer's console.[^kfs-vehicle-host] The image keeps running; elements that no longer exist are
  inert. See [controller set and layout changes](/integration/controller-set-and-layout-changes.md).
- **`--part` together with `--vehicle`.** Every vehicle computer is constructed with
  `tables.BenchPart`.[^kfs-vehicle-host] `Inferred:` an image built with `--part` for a part whose
  core, clock, memory sizes or limits differ from the bench part is rejected by the queued load with
  rule `part`, visible only in the vehicle's console log, while the workbench has already printed
  `queued load onto vehicle ...` and set `Loaded`. (Derived from `Load`, `LoadOntoVehicle` and
  `ImageReader.Read`; not run.)
- **Different vehicles have different layouts.** `vehicle.h` on disk reflects the target at the time
  of the last header write; the build itself always uses the layout it was handed, so a stale
  `vehicle.h` file does not affect the result (see
  [workspace and files](/tooling/workspace-and-files.md)).

# Preview builds

`Workbench.Preview(partName)` builds the open (or last) file with
`BuildText(file, text, otherPart, VehicleTarget?.Layout ?? Tables.BenchLayout)` and stores the
outcome in `Editor.Preview`; the editor then shows that outcome's cost column and report in amber.
The name is matched case-insensitively and with spaces ignored against `Tables.Parts`. `off` clears
it; opening or closing a file clears it too. Nothing is loaded.[^workbench][^editor-model]

# Modding notes

- **A mod can run the whole chain with public calls** on the main thread:
  `var o = KfsIntegration.Workbench.Build("proj/main.c");` (check `o?.Ok`, read `o.Diagnostics`),
  `KfsIntegration.Workbench.Load("proj/main.c --vehicle")`, `KfsIntegration.Workbench.Start()`.
  `BuildText` gives a build with no printing and no side effects on `LastBuild`.[^workbench] This
  chain (`Build`, `SelectTarget("vehicle")`, `Load("… --vehicle")`, `Start()`, then a dispatch) was
  run outside the game against a `FakeVehicle` and printed the `queued load onto vehicle …` and
  `queued start on vehicle …` lines quoted above.[^offline-harness]
- **Building for a vehicle without loading:** `BuildText(file, text, Tables.BenchPart, host.Layout)`
  with `host = KfsVehicleHost.For(vehicle)`.
- **Load results on a vehicle are asynchronous and silent.** Poll
  `Workbench.Target.Snapshot.State` or read `Workbench.Target.Console` after the next dispatch;
  alternatively set `VehicleComputer.ImageLoaded` — but note that property is a single
  `Action?` already used by `KfsVehicleHost` (`Computer.ImageLoaded = PrepareSampler`), so chain
  rather than replace it.[^vehicle-computer][^kfs-vehicle-host]
- **Compile options are fixed** in the workbench path; a mod that needs `-D` style defines, extra
  system headers or other options must call `Compiler.Compile` itself (`CompileInput.SystemFiles` and
  `CompileInput.Options` are public init-only properties) and then load the bytes with
  `vehicle.Computer.Enqueue(c => c.Load(bytes, layout))`.[^compile-options][^workbench]
- **Tables and layout are inputs to every step** (`Compiler.Compile`, `Assembler`, `ImageReader.Read`,
  `HostComputer.Load`). A mod that extends the service or message tables must have done so before the
  build *and* keep them stable through the load, otherwise the `KC-E900` / `part` / `layout` style
  rejections above apply. See [game tables loader](/tables/game-tables-loader.md).
- **Threads:** `BuildText` may run on a thread-pool thread (editor builds). Harmony patches inside
  the compiler, assembler or `GameTables` must be thread-safe.

[^workbench]: Workbench.cs — `BuildText` :483-587 (project discovery :486-500, kC compile :505-522, kASM assemble :523-539, failed outcome :541-550, image validation and `E900` :551-570, successful outcome :571-586); `SourceStampsOf` :589-610; `Build` :659-678; `Report` (printing; library functions skipped by `DebugInfo.FN_LIBRARY` = 4, DebugInfo.cs:19) :680-737; `Load` :739-807; `LoadOntoVehicle` :809-822; `LoadOntoBench` :824-835; `LoadTarget` :837-854; `Upload` :856-878; `FinishUpload` :880-916; `LoadBuilt` :918-937; `LoadIntoTarget` :939-942; `Start` :973-989; `QueueStart` :991-1002; `Stop` :1004-1020; `Play` :1022-1041; `Pause` :1043-1053; `StopTarget` :1055-1064; `ResetTarget` :1075-1091; `Tick` :1093-1132; `Run` :1143-1166; `Disasm` :1383-1408; `Report(file)` :1833-1846; `BuildQuiet` :1848-1857; `Lines` :1859-1913; `Preview` :1915-1958; `Manual` :2123-2133.
[^editor-model]: EditorModel.cs — `Open` clears `Preview` :233; `Save` :344-365; `BuildOf` :367-394; `Assemble` :406-427; `Poll` :429-449; `Wait` :451-456; `Apply` (status texts, `BuildLogged`) :458-478.
[^assemble-outcome]: AssembleOutcome.cs:10-35.
[^listing]: Listing.cs — `Build` :16-37; `Text` :39-127; `Format` :129-178. ListingLine.cs:3.
[^workspace]: Workspace.cs — `ProjectOf` :176-235.
[^console-commands]: KfsConsoleCommands.cs — `build` :49-52; `report` :53-59; `lines` :60-63; `preview` :64-67; `load` :80-83; `Execute` :192-232.
[^editor-window]: KfsEditorWindow.cs — constructor sets `Editor.Background = true` :700-711; Save :961-964 and :1073-1076; Build button :1277-1280; Upload button :1282-1285; diagnostics tooltip :2145-2152.
[^host-computer]: HostComputer.cs — `Load` (host-message rule, `LoadImage`, log lines) :302-344; `Start` :346-379; `Stop` :393-402.
[^vehicle-computer]: VehicleComputer.cs — `ImageLoaded` :136; `Load` (needs-stock rule :315-324; `Ownership.NeedsStock` is Ownership.cs:48-66) :313-350; `TryStart` :362-385; `Stop` / `LeaveControl` :408-429.
[^bench-machine]: BenchMachine.cs — `LoadBench` :41-44; `Load` :46-57; `Start` :59-65; `Stop` :67-71; `PowerDown` :73-77; `Tick` :84-110; `Run` :127-136.
[^image-reader]: ImageReader.cs — part rule :773-776; layout rule :777-780.
[^kfs-vehicle-host]: KfsVehicleHost.cs — `Layout => Frozen.Layout` :67; constructor (`Frozen`, `VehicleComputer(tables, tables.BenchPart, vehicle.Id)`, `ImageLoaded = PrepareSampler`) :105-118; layout-change event :430-438.
[^compile-options]: CompileOptions.cs:6-23.
[^diagnostic]: Diagnostic.cs:3-9.
[^game-tables]: GameTables.cs — `Part(name)` :470-473.
[^offline-harness]: Experiment E11 and the output of the complete spike program in the offline harness document.
