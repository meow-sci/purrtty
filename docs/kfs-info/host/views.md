---
type: API Reference
title: Host view models (KSA.Kfs.Host.Views)
description: Every renderer-neutral view model in KSA.Kfs.Host.Views — bus monitor, disassembly, profiler, oscilloscope, compile report, NaN view, definitions, diagnostic manual, budget bars, line costs and layout helpers — with the data each draws and where that data comes from.
resource: ksa-decomp:KSA.Kfs.Host.Views/BusMonitorView.cs
tags: [kfs, host, views, view-model, profiler, bus-monitor, scope, disassembly, report]
sources:
  - id: bus-monitor-view
    resource: ksa-decomp:KSA.Kfs.Host.Views/BusMonitorView.cs
    title: BusMonitorView and BusField (decompiled)
  - id: bus-marker-view
    resource: ksa-decomp:KSA.Kfs.Host.Views/BusMarkerView.cs
    title: BusMarkerView and BusMarker (decompiled)
  - id: line-cost-view
    resource: ksa-decomp:KSA.Kfs.Host.Views/LineCostView.cs
    title: LineCostView and LineCostRow (decompiled)
  - id: definitions
    resource: ksa-decomp:KSA.Kfs.Host.Views/Definitions.cs
    title: Definitions and Definition (decompiled)
  - id: diagnostic-manual
    resource: ksa-decomp:KSA.Kfs.Host.Views/DiagnosticManual.cs
    title: DiagnosticManual (decompiled)
  - id: disassembly-view
    resource: ksa-decomp:KSA.Kfs.Host.Views/DisassemblyView.cs
    title: DisassemblyView, DisassemblyFunction, DisassemblyLine (decompiled)
  - id: global-leaves
    resource: ksa-decomp:KSA.Kfs.Host.Views/GlobalLeaves.cs
    title: GlobalLeaves, GlobalLeaf, LeafKind (decompiled)
  - id: live-lines
    resource: ksa-decomp:KSA.Kfs.Host.Views/LiveLines.cs
    title: LiveLines (decompiled)
  - id: nan-view
    resource: ksa-decomp:KSA.Kfs.Host.Views/NanView.cs
    title: NanView (decompiled)
  - id: profiler-view
    resource: ksa-decomp:KSA.Kfs.Host.Views/ProfilerView.cs
    title: ProfilerView, ProfilerRow, BudgetBar (decompiled)
  - id: profiler-table
    resource: ksa-decomp:KSA.Kfs.Host.Views/ProfilerTable.cs
    title: ProfilerTable, ProfilerLine, BudgetLine (decompiled)
  - id: report-view
    resource: ksa-decomp:KSA.Kfs.Host.Views/ReportView.cs
    title: ReportView, ReportBar, ReportTaskRow, ReportLibraryRow, SourceLink (decompiled)
  - id: scope-model
    resource: ksa-decomp:KSA.Kfs.Host.Views/ScopeModel.cs
    title: ScopeModel and ScopeSeries (decompiled)
  - id: layout-helpers
    resource: ksa-decomp:KSA.Kfs.Host.Views/SplitLayout.cs
    title: SplitLayout, AnnotationSplit, ViewThrottle, InvariantWriter (decompiled, one file each)
  - id: host-computer
    resource: ksa-decomp:KSA.Kfs.Host/HostComputer.cs
    title: HostComputer (decompiled)
generated: { by: claude-code/claude-fable-5-2, at: 2026-11-25T21:30:51Z }
status: stable
ksa_build: "2026.11.25.5949"
---

# What this namespace is

`KSA.Kfs.Host.Views` holds 34 small types that turn host and compiler data into rows, cells and
text lines. None of them references ImGui: the same view models feed the in-game editor and debug
windows (`KSA.Kfs.KfsEditorWindow`, `KfsDebugWindow`), the console commands (through
`KSA.Kfs.Host.Workbench.Workbench`, which prints their `Lines()` output) and the editor model.
Most have two forms: an allocating one returning strings (console) and a `Write…(…, Span<char>)`
one for per-frame drawing.

Everything is `public`. Data sources are, without exception, one of:

| source | provided by |
| --- | --- |
| `MachineSnapshot` | `HostComputer.Snapshot` — see [snapshots and perf](/host/snapshots-and-perf.md) |
| live stores on the computer | `Traces`, `Watches`, `Bus.Samples`, `ExecTrace` |
| the loaded `KvmImage` and its debug info | `HostComputer.Image` |
| compiler outputs | `CompileReport`, `LineCost`, `BusAccess`, `IrModule` — see [compile report](/compiler/compile-report.md) |
| `GameTables` | message, diagnostic and opcode tables |

# Catalogue

| type | kind | purpose |
| --- | --- | --- |
| `BusMonitorView`, `BusField` | static + record | bus monitor rows, field values, message definitions |
| `BusMarkerView`, `BusMarker` | static + record | per-source-line "reads/writes message" gutter markers |
| `LineCostView`, `LineCostRow` | static + record | per-source-line cycle estimates |
| `Definitions`, `Definition` | static + record | go-to-definition index, identifier under the cursor, tasks that reach kASM |
| `DiagnosticManual` | static | the manual page of a diagnostic code |
| `DisassemblyView`, `DisassemblyFunction`, `DisassemblyLine` | static + records | annotated disassembly, "copy as kASM" |
| `GlobalLeaves`, `GlobalLeaf`, `LeafKind` | static + record + enum | scalar leaves of the program's globals (watches, NaN scan) |
| `LiveLines` | class | which source lines executed recently |
| `NanView` | static | NaN report |
| `ProfilerView`, `ProfilerRow`, `BudgetBar` | class + records | profiler, immutable form |
| `ProfilerTable`, `ProfilerLine`, `BudgetLine` | class + structs | profiler, reusable per-frame form |
| `ReportView`, `ReportBar`, `ReportTaskRow`, `ReportLibraryRow`, `SourceLink` | class + records | compile report: memory bars, per-task stack/WCET, library use |
| `ScopeModel`, `ScopeSeries` | classes | oscilloscope |
| `SplitLayout`, `AnnotationSplit` | static + record struct | splitter geometry for the editor |
| `ViewThrottle<T>` | class | rebuild a view at most every N seconds |
| `InvariantWriter` | ref struct | allocation-free invariant-culture formatting |

# Bus monitor

`BusMonitorView` renders `MachineSnapshot.Bus` (`BusRow`s from the
[bus tracker](/host/message-bus.md)).[^bus-monitor-view]

| member | output |
| --- | --- |
| `Age(BusRow)` / `WriteAge(BusRow, Span<char>)` | `12.5 ms (1 tick)`, or `—` when never received |
| `Rate(BusRow)` / `WriteRate` | `50/s` |
| `Lines(MachineSnapshot)` | `bus of <name> after tick K (H Hz):` then per message `MSG_NAME 0xIIII pub\|sub\|pub+sub from <publisher>[, task T]; seq N, age …, rate …`; or `no messages on the bus (load a program that subscribes to or publishes one)` |
| `Fields(GameTables, IoLayout, BusRow)` | one `BusField(Path, Type, Text, Plottable)` per leaf, decoded from `row.Slot`; empty when the row carries no slot bytes (no bus lease) or the size mismatches; header leaves are not plottable |
| `WriteText(tables, leaf, slot, dest)` | one leaf's value as text |
| `EnumName(tables, field, value)` | symbolic name for an enum-valued field |
| `PlottableNames(tables, layout, id)` | `MSG_NAME.path` for every body leaf |
| `Definition(tables, layout, nameOrId)` | a text description of a message in this layout |

Two details matter to anyone adding messages:

- **Enum names are scraped from prose.** `WriteText` appends a symbolic name only for `uint32`
  leaves whose last path segment is `state`, `roll_mode` or `mode`. `EnumName` finds the *first*
  field row in the whole catalogue whose `names` contains that word and parses pairs of the form
  `` `1` `NAME` `` out of the row's free-text `meaning` with the regular expression
  `` `(\d+)`\s+`([A-Z_]+)` ``. Results are cached in a static dictionary keyed by
  `(tables, field, value)`. A `uint32` leaf named `parent_id` is shown as `0x%08X`.
- **The publisher rule is a hard-coded switch on the message name** (`PublisherRule`):
  `MSG_NAV_STATE` → "the NAVIGATION owner (the host's stock navigation unless a program publishes
  it)", `MSG_ATT_TARGET` and `MSG_GUIDANCE_STATUS` → "the GUIDANCE owner", `MSG_WRENCH_CMD` → "the
  ATTITUDE_CONTROL owner", **anything else → "the host, always"**.

`Definition` prints `NAME (0xID), FI §<section>: published by <rule>`, the type name and size
(`header 16, body_size N`), the fixed header line, and one line per field with its C type, offset
and `meaning`; a per-vehicle array with no elements is shown as `(absent on this vehicle: no <group>
elements)`.

# Editor gutters: bus markers and line costs

`BusMarkerView.Rows(IReadOnlyList<BusAccess> accesses, string file, int lineCount)` returns, per
source line, the distinct `(message, read|write)` accesses the compiler's
[bus analysis](/compiler/bus-analysis.md) attributed to it, reads first. A `BusMarker(Message,
MessageId, Buffer, Write)` renders as `< NAV_STATE` or `> WRENCH_CMD` (`Cell`), and a row as either
the joined cells or a compact `<`, `>`, `<>` with a count when several messages are involved
(`CompactCell`).[^bus-marker-view]

`LineCostView.Rows(IReadOnlyList<LineCost> estimates, string file, int lineCount)` places the
compiler's per-line estimates into an array indexed by line; a `LineCostRow(Line, Cycles,
PerIteration, StackDepth, SoftRoutines)` renders as `123/it [4]` (`Cell`) and
`≈1,234 cycles per iteration, <soft routines>; operand stack N after this line` (`Hover`).
`CountLines(text)` counts lines.[^line-cost-view] Both match an entry's file either exactly or by
file name.

# Definitions

`Definitions.Of(IrModule?)` lists every IR function and global with its source position
(`Definition(Name, Kind, File, Line)`, kind `function` or `global`); `Find(defs, name)` prefers a
definition in a real file over one in a `<…>` pseudo-file; `WordAt(text, offset)` returns the
identifier under a cursor. `TasksReachingKasm(IrModule?)` walks the call graph from every entry
function and returns the tasks that reach a function whose language is neither `kc` nor `ks` and
whose file is not a `<…>` pseudo-file — used to word the WCET warning below.[^definitions]

# Diagnostic manual

`DiagnosticManual.Page(GameTables tables, string code, string? hint = null)` looks the code's number
up in `tables.DiagnosticsData.Rules` (the number is what follows the dash and one severity letter,
e.g. `501` in `KC-E501`) and returns `CODE: rule`, a line with severity, default, range and
languages, and the optional hint; or `CODE: not in the diagnostics registry`.[^diagnostic-manual]
See [diagnostics table](/tables/diagnostics-table.md).

# Disassembly

`DisassemblyView.Build(KvmImage, GameTables, Func<string, string[]?> source)` groups the workbench
listing (`Listing.Build`) by function into `DisassemblyFunction(Name, Lines)`; each
`DisassemblyLine(Pc, Cost, Instruction, File, Line, Source, FirstOfLine)` carries the instruction's
cycle cost and, on the first instruction of each source line, that line's text (fetched through the
`source` callback and cached per file). `Format` prints it as text. `CopyAsKasm(image, tables,
function)` cuts one `.func … .endfunc` block out of the disassembler's output, prefixes explanatory
comments and `.include <kfs.kinc>`, and rewrites `LDG`/`STG`/`LDG64`/`STG64` absolute addresses to
`symbol` or `symbol + offset` using the debug globals.[^disassembly-view] See
[kASM disassembler](/compiler/kasm-disassembler.md).

# Global leaves

`GlobalLeaves.Leaves(KvmImage)` flattens the image's debug globals into scalars
(`GlobalLeaf(Path, Global, Address, Kind, Size, File)`), walking arrays (`name[i]`) and structs
(`name.field`) to a depth of 64 and a total of `MAX_LEAVES = 4096`:[^global-leaves]

| debug type | sizes accepted | `LeafKind` |
| --- | --- | --- |
| float | 4, 8 | `Float` |
| bool | 1, 4 | `Bool` |
| int, enum | 1, 2, 4, 8 | `Int` when the type's signed flag is set, else `UInt` |
| pointer | 4 | `UInt` |

Globals with no source file (compiler-internal) are skipped. `GlobalLeaf.Read(KvmMachine)` and
`Read(ReadOnlySpan<byte> ram)` decode the value as a `double`; `IsNaN(KvmMachine)` tests a float
leaf. `GlobalLeaves.Globals(image)` is the unflattened list `(Name, Address, Size, File)`, largest
first. `HostComputer` computes the leaves at `Load` and uses them for watches and the per-tick NaN
scan.[^host-computer]

# Live lines

`LiveLines` drives the editor's "this line just ran" highlight. `Bind(ExecTrace? trace, string? file)`
builds an instruction → source-line map for one file from the image's debug lines;
`Scan(double now)` marks every line with an instruction whose trace stamp is newer than the last
scan; `Alpha(line, now, fadeS)` returns an exponential fade from 1 to 0 over `fadeS` seconds.[^live-lines]
The trace exists only while someone keeps calling `HostComputer.WantExecTrace()`.

# NaN view

`NanView.Lines(MachineSnapshot)` prints `no NaN since reset`, or
`first NaN at tick K: 0x<PC> in <function>, <file>:<line>, task T` followed by one
`  <path>: NaN first seen after tick K (<file>)` per affected global. `WriteFirst` and
`WriteGlobal` are the span-writing forms.[^nan-view]

# Profiler

Both forms combine three parts of the snapshot: `Profile` (cumulative per function), `ProfileLast`
(each function's most recent run and its callee edges) and `Tasks` (per-task budget use).

- **`ProfilerView.From(MachineSnapshot)`** returns an immutable view: `Rows` of
  `ProfilerRow(Name, Pseudo, SelfLast, TotalLast, CallsLast, AgeTicks, AgeSeconds, SelfAverage,
  TotalAverage, Calls, SelfCumulative, TotalCumulative, Children)` ordered by average total, and
  `Budgets` of `BudgetBar(Task, Kind, Cycles, Budget, MeasuredMax, AgeTicks)` with a `Fraction`.
  Averages are cumulative cycles divided by `TicksRun`. `OwnRows(libraryFunctions)` gives the "own
  code" view: library functions and pseudo rows (soft routines, services, bus copies) are removed
  and their share is taken out of their callers' totals. `Lines()` is the console rendering;
  `AgeText`/`WriteAgeText` print `now` or `N t, S.SS s`.[^profiler-view]
- **`ProfilerTable.Fill(MachineSnapshot, bool ownCode, IReadOnlySet<string> libraryFunctions)`**
  computes the same thing into reusable lists of `ProfilerLine` and `BudgetLine` structs, for
  drawing every frame without allocating; `ShowsChild(callee)` applies the own-code filter to an
  edge.[^profiler-table]

See [profiler and exec trace](/vm/profiler-and-exec-trace.md) for where the numbers originate.

# Compile report

`ReportView.From(CompileReport report, KvmImage? image, MachineSnapshot? snapshot,
IReadOnlySet<string>? kasmTasks = null, Func<ushort, string>? messageName = null)` merges the
compiler's report with the loaded image and, when the program has run, measured values:[^report-view]

| member | content |
| --- | --- |
| `Machine` | `Name (core, X MHz, N cycles per tick at H Hz)` |
| `Program` | `ReportBar("Program", used, limit, segments)` — kC, kScript, kASM, data init, runtime lib |
| `Ram` | `ReportBar("RAM", …)` — globals, task stacks, VM region |
| `Globals` | from `GlobalLeaves.Globals(image)` |
| `Tasks` | `ReportTaskRow(Task, StackBytes, DeepestPath, Overlaid, OperandCells, ReturnEntries, Wcet, Budget, MeasuredMax, Flag)` with `Subscribes`, `Publishes` (message names from the image's links) and `ReachesKasm` |
| `Library` | `ReportLibraryRow(Name, Kind, Bytes, FirstUse, Link)` for runtime families and library code |
| `Calls` | `N near, M far` |
| `Text` | the compiler's own text report |

A task whose measured maximum exceeds its computed worst case gets a `Flag`:
`KA-W712: measured N cycles exceeds the computed worst case W; check the .loop_bound declarations`
when the task reaches hand-written kASM, otherwise
`internal error: measured N cycles exceeds the computed worst case W of compiled code; report it with the source`.
`SourceLink.Parse("file:line")` turns a first-use string into a clickable location. `Lines()` is the
console rendering (it lists at most 12 globals).

# Oscilloscope

`ScopeModel` shows three kinds of series side by side: program trace channels
(`computer.Traces`), watched globals (`computer.Watches`) and plotted message fields
(`computer.Bus.Samples`).[^scope-model]

- `Visible(HostComputer)` copies the last 2048 samples of each series into pooled buffers
  (`TraceStore.CopyLast`) and returns `ScopeSeries` objects showing the trailing `Window` samples;
  buffers of series that disappeared are recycled. Three buffers are preallocated
  (`PREALLOCATED_SERIES`).
- `SetPaused(true, computer)` freezes the currently visible data; while paused `Visible` only
  re-applies the window.
- `Window` defaults to 256 samples; `Zoom(factor)` clamps it to 8…2048. `Cursor` and
  `Readout(series)` give `k K: value` for the sample under the cursor.
- `ScopeModel.Live(HostComputer)` is the allocating equivalent used by the console.
- `ScopeSeries` has `Name`, `Watched`, `MessageField`, `Samples`, `Min`, `Max`.

# Layout and plumbing helpers

| type | what it does |
| --- | --- |
| `SplitLayout` | `Clamp`, `Drag`, `Right` for a two-pane splitter with `MIN_LEFT = 100`, `MIN_RIGHT = 400`, `DIVIDER_WIDTH = 6` |
| `AnnotationSplit(float Width, bool Collapsed, float? Dragging)` | state of the editor's annotation column: `Shown`, `Text`, `Drag`, `EndDrag`, `Toggle`; `MIN_TEXT = 200`, `COLLAPSE_BELOW = 24`, `DEFAULT_WIDTH = 200` |
| `ViewThrottle<T>` | `Get(nowSeconds, key, build)` returns the cached view unless the key changed or `IntervalSeconds` (default 0.25) elapsed; `Paused`, `Invalidate()`, `Builds` |
| `InvariantWriter` | `ref struct` over a `Span<char>`: `Append(ReadOnlySpan<char>)`, `Append<T>(T value, format)` for any `ISpanFormattable`, always invariant culture; when the span is full further appends are dropped silently |

[^layout-helpers]

# Modding notes

- The views are a convenient, stable-looking read API for a mod that wants to show kFS state in its
  own UI (purrtty could render the bus monitor or the console in a terminal): they need only a
  `MachineSnapshot`, the tables and the layout.
- **A new message appears in the bus monitor automatically** once its row is in
  `GameTables.MessagesData.Messages` and a loaded image subscribes to or publishes it (`BusTracker`
  builds one row per message of the image): `Fields`, `Definition` and `PlottableNames` are entirely
  table-driven. Its publisher is described as "the host, always" unless it is one of the four named
  messages, which is the right text for a mod-published message.
- To get symbolic enum names in the monitor, a new field must be called `state`, `mode` or
  `roll_mode` **and** be the first such field in the catalogue — in practice those three names are
  already taken by `MSG_BURN_STATUS.state` and `MSG_ATT_TARGET.mode`/`.roll_mode`, so a mod's enum
  fields show as plain numbers (or wrongly borrow those names' values if they reuse the field name).
- `BusMonitorView._enumNames` is a process-wide static cache keyed by the `GameTables` instance; if
  a mod edits a `meaning` string after the monitor has been used, the old names stay cached.
- The views themselves never touch game or VM state, so they are safe on any thread; what they are
  given (`ScopeModel.Visible(computer)`, `LiveLines.Bind(trace, …)`) reads locked or volatile data.
  The one exception is `GlobalLeaf.Read(KvmMachine)` / `IsNaN(KvmMachine)`, which read machine RAM
  directly and belong on the tick thread (their only callers are in `HostComputer.AfterTick`); from
  another thread use `Read(ReadOnlySpan<byte> ram)` on `MachineSnapshot.Ram`.

[^bus-monitor-view]: BusMonitorView.cs — `Age`/`WriteAge` :25–51; `Rate`/`WriteRate` :53–65; `Lines` :67–116; `Fields` :118–136; `WriteText` :144–198; `EnumName`/`LookUpEnumName` :200–248; `PlottableNames` :250–260; `PublisherRule` :262–272; `Definition` :274–362; BusField.cs:3.
[^bus-marker-view]: BusMarkerView.cs:10–70; BusMarker.cs:5–8.
[^line-cost-view]: LineCostView.cs:8–37; LineCostRow.cs:8–70.
[^definitions]: Definitions.cs — `Of` :11–27; `Find` :29–35; `WordAt` :37–66; `TasksReachingKasm` :68–110; Definition.cs:3.
[^diagnostic-manual]: DiagnosticManual.cs:9–51.
[^disassembly-view]: DisassemblyView.cs — `Build` :20–62; `Format` :64–106; `CopyAsKasm` :115–174; DisassemblyFunction.cs:5; DisassemblyLine.cs:3.
[^global-leaves]: GlobalLeaves.cs — `MAX_LEAVES` :14; `Globals` :16–28; `Leaves` :39–60; `Walk` :62–141; GlobalLeaf.cs:7–56; LeafKind.cs:3–9.
[^live-lines]: LiveLines.cs — `Bind` :32–90; `Scan` :92–120; `Alpha` :122–138.
[^nan-view]: NanView.cs:6–63.
[^profiler-view]: ProfilerView.cs — `From` :29–63; `OwnRows` :65–135; `Age` :137–144; `AgeText`/`WriteAgeText` :155–174; `Lines` :176–277; ProfilerRow.cs:5; BudgetBar.cs:5–18.
[^profiler-table]: ProfilerTable.cs — `Fill` :58–124; own-code computation :126–189; ProfilerLine.cs:5–26; BudgetLine.cs:5–30.
[^report-view]: ReportView.cs — `From` :29–141; `Lines` :143–264; ReportTaskRow.cs:6–60; ReportBar.cs:6–19; ReportLibraryRow.cs:3; SourceLink.cs:6–21.
[^scope-model]: ScopeModel.cs — `Zoom` :78–81; `SetPaused` :101–108; `Live` :110–133; `Visible` :135–174; `Readout` :215–234; ScopeSeries.cs:7–40.
[^layout-helpers]: SplitLayout.cs:5–36; AnnotationSplit.cs:5–81; ViewThrottle.cs:5–44; InvariantWriter.cs:6–52.
[^host-computer]: HostComputer.cs — leaves computed at `Load` :330–331; NaN scan and watches in `AfterTick` :571–583.
