---
type: Extension Point
title: Extension points in the kFS tooling
description: Where a StarMap/Harmony mod can hook the kFS user tooling — shipping examples and headers, driving build/load/start from code, capturing kFS and program output into its own UI, and making mod-added services visible in the reference window — with the visibility of every member involved.
resource: ksa-decomp:KSA.Kfs/KfsIntegration.cs
tags: [kfs, tooling, extension, harmony, modding, purrtty]
sources:
  - id: kfs-integration
    resource: ksa-decomp:KSA.Kfs/KfsIntegration.cs
    title: KfsIntegration (decompiled)
  - id: console-commands
    resource: ksa-decomp:KSA.Kfs/KfsConsoleCommands.cs
    title: KfsConsoleCommands (decompiled)
  - id: workbench
    resource: ksa-decomp:KSA.Kfs.Host.Workbench/Workbench.cs
    title: Workbench (decompiled)
  - id: workspace
    resource: ksa-decomp:KSA.Kfs.Host.Workbench/Workspace.cs
    title: Workspace (decompiled)
  - id: examples
    resource: ksa-decomp:KSA.Kfs.Host.Workbench/Examples.cs
    title: Examples (decompiled)
  - id: editor-window
    resource: ksa-decomp:KSA.Kfs/KfsEditorWindow.cs
    title: KfsEditorWindow (decompiled)
  - id: reference-window
    resource: ksa-decomp:KSA.Kfs/KfsReferenceWindow.cs
    title: KfsReferenceWindow (decompiled)
  - id: text-box-widget
    resource: ksa-decomp:KSA.Kfs/KfsTextBoxWidget.cs
    title: KfsTextBoxWidget (decompiled)
  - id: system-headers
    resource: ksa-decomp:KSA.Kfs.KC/SystemHeaders.cs
    title: SystemHeaders (decompiled)
  - id: kc-front-end
    resource: ksa-decomp:KSA.Kfs.KC/KcFrontEnd.cs
    title: KcFrontEnd (decompiled)
  - id: console-log
    resource: ksa-decomp:KSA.Kfs.Host.Services/ConsoleLog.cs
    title: ConsoleLog (decompiled)
  - id: host-computer
    resource: ksa-decomp:KSA.Kfs.Host/HostComputer.cs
    title: HostComputer (decompiled)
  - id: kfs-vehicle-host
    resource: ksa-decomp:KSA.Kfs/KfsVehicleHost.cs
    title: KfsVehicleHost (decompiled)
  - id: game-tables
    resource: ksa-decomp:KSA.Kfs.Vm.Tables/GameTables.cs
    title: GameTables (decompiled)
  - id: terminal-interface
    resource: ksa-decomp:Brutal.ImGuiApi.Abstractions/TerminalInterface.cs
    title: TerminalInterface (decompiled)
  - id: cli
    resource: ksa-decomp:KSA.Kfs.Cli/Cli.cs
    title: Cli (decompiled)
  - id: offline-harness
    resource: /modding/offline-harness.md
    title: Offline harness experiments E1–E12
generated: { by: claude-code/claude-fable-5-2, at: 2026-11-25T21:56:52Z }
status: draft
ksa_build: "2026.11.25.5949"
---

# Scope

This document collects, in one place, the answers to four questions about extending the kFS
**tooling** (not the VM or the service layer) from a mod such as purrtty. The facts are read from the
decompiled code and cited; the *recommendations* are design conclusions and are marked `Inferred:`.
Nothing here was exercised in the running game, hence `status: draft`. A subset was run offline
against the real `KSA.dll` with a `FakeVehicle` as the vehicle target: `Workspace.Write`,
`Workbench.Build`, `SelectTarget("vehicle")`, `Load("<file> --vehicle")`, `Start()`, `Frame`, a
header added to `SystemHeaders.Files` with its names in `SystemHeaders.Builtins`, and reading
program output from `Computer.Console.Snapshot()` — see
[the offline harness](/modding/offline-harness.md).[^offline-harness] The Harmony targets and the
`Output` tee below were not part of that run.

The tooling was not designed for extension: there is no plugin registry, one event, and no virtual
members. What makes extension practical anyway is that almost every type is `public` and most
state is reachable from the two static classes `KfsIntegration` and `KfsConsoleCommands`.

# Visibility map

| thing | declaration | reachable from a mod |
| --- | --- | --- |
| `KfsIntegration` | `public static class` | yes |
| `KfsIntegration.Tables` | `public static GameTables` (lazy, one instance) | yes |
| `KfsIntegration.Workbench` | `public static Workbench` (lazy) | yes |
| `KfsIntegration.Output` | `public static TextWriter { get; set; }` | yes, settable |
| `KfsIntegration.Print`, `Error`, `ShowEditor`, `ToggleEditor`, `UseWorkbench`, `RegisterCommands` (two overloads), `BeforeInputEvents` | `public static` | yes |
| `KfsIntegration.Editor` | `internal static KfsEditorWindow?` | reflection |
| `KfsIntegration._workbench`, `_editor`, `_registered`, `_tables` | `private static` | reflection |
| `KfsIntegration.ConsoleWindowWriter`, `FrameHook` | `private sealed` nested classes | reflection |
| `KfsConsoleCommands.Execute(string)`, `Register`, `Verbs` | `public static` | yes |
| `KfsConsoleCommands._verbs`, `Verb`, `Help`, `On`, `SetFlag`, `Kill`, `Budget` | `private` | Harmony / reflection |
| `KfsPrototype.Enabled` | `public static bool { get; set; }` | yes |
| `Workbench`, `Workspace`, `EditorModel`, `BuildProject`, `AssembleOutcome`, `Examples`, `Listing`, `ListingLine`, `IVehicleTarget`, `WorkspaceTree`, `WorkspaceTreeState`, `WorkspaceFolder` | `public` | yes |
| `Workbench.Out`, `ActiveVehicle`, `ActiveVehicleIdSource`, `TickHostMs`, `RunHostMsPerFrame` | public settable properties | yes |
| `Workbench.OpenRequested` | `public event Action<string>` | yes |
| `Workbench._pending`, `_vehicle`, `_lastFile`, `Print`, `Report(file, outcome, part, layout)`, `LoadOntoVehicle`, ... | `private` | Harmony / reflection |
| `KfsEditorWindow`, `KfsReferenceWindow`, `KfsDebugWindow`, `KfsNewPopup`, `KfsDeletePopup`, `KfsTextBoxWidget`, `IKfsTextWidget`, `KfsReferencePage` | `public` types | yes (constructors of the two popups are private; use their static factories) |
| `KfsEditorWindow.DrawContent`, `DrawMenuBar` | `public override` | Harmony |
| every other `KfsEditorWindow` / `KfsReferenceWindow` drawing or building method, and their fields | `private` | Harmony / reflection |
| `KfsDebugWindow.Show` | `public static bool` field | yes |
| `HostComputer.Console`, `Traces`, `Watches`, `Bus`, `Snapshot`, `Enqueue`, `Watch`, `Unwatch` | `public` | yes |
| `ConsoleLog`, `TraceStore`, `LogLine`, `TraceSample` | `public` | yes |
| `KfsVehicleHost.For`, `Find` | `public static` | yes |
| `KfsVehicleHost.AllHosts` | `internal static` | reflection |
| `SystemHeaders.Files`, `SystemHeaders.Builtins` | `public static readonly` mutable collections | yes |
| `KSA.Kfs.Cli.Cli.Run`, `KcCli.Run` | `public static` | yes |

[^kfs-integration][^console-commands][^workbench][^editor-window][^reference-window][^host-computer][^kfs-vehicle-host][^system-headers][^cli]

# (a) Shipping example programs and header files

## Source files and examples

- **Plain files.** `KfsIntegration.Workbench.Workspace.Write("purrtty_lights/main.c", text)` creates
  the project folder and the file; `Workbench.Edit(path)` opens it and shows the editor. The
  workspace has no index or manifest; the editor's tree updates through its file watcher.
  [^workspace][^workbench] A mod should not overwrite existing files (the stock example writer never
  does unless forced).[^examples]
- **Into the File > Examples menu.** The menu is drawn from `Examples.Menu` each frame and each item
  calls `Workbench.CopyExample(folder)`, which uses `Examples.MainFile` and `Examples.Write`, both
  reading `Examples.Files`.[^editor-window][^workbench][^examples] `Files` is typed
  `IReadOnlyDictionary<string,string>` but is a `Dictionary<string,string>` at run time; `Menu`,
  `Names` and `AllNames` are get-only static auto-properties computed once.
  `Inferred:` add the files by casting `Examples.Files` to `Dictionary<string,string>`, and extend the
  menu with a Harmony postfix on the `Examples.Menu` getter (and on `Names` if `kfs examples` should
  write them too). Replacing the backing fields by reflection is not a safe alternative for static
  `readonly` fields on current .NET runtimes.
- **Naming.** Example folders in the game are `kC_*`, `kAsm_*`, `ex_*`, `e2e04_*`; nothing reserves a
  prefix, so a mod should choose a distinctive one to avoid colliding with a future stock example.

## Header files

| goal | mechanism | notes |
| --- | --- | --- |
| a header for one project | write `name.h` into that project folder | every `.h` in a project folder is part of the build input automatically[^workspace] |
| a header every kC project can include | add `["purrtty.h"] = text` to `SystemHeaders.Files` | consulted for `#include <purrtty.h>` and as the fallback for `#include "purrtty.h"`; `SystemHeaders.Get` returns the text with CRLF normalised to LF[^system-headers][^kc-front-end] |
| extra declarations in `<kfs.h>` itself | replace the `"kfs.h"` entry of `SystemHeaders.Files` with an extended text | `kfs.h` is a hard-coded string in that dictionary, including every stock service prototype[^system-headers] |
| per-build system headers | `CompileInput.SystemFiles` | checked before everything else, but the workbench never sets it; only useful when the mod calls `Compiler.Compile` itself[^kc-front-end][^workbench] |
| a kASM include for all pure-kASM projects | write `name.kinc` at the workspace root | `.include "name.kinc"` falls back from the project folder to the root; inside a kC project only the project's own files are searched[^workbench][^kc-front-end] |

Declaring a function in a header does not by itself make it callable: the front end treats a
body-less function as built in only when its name is in `SystemHeaders.Builtins` (a public, mutable
set) **and** it was declared in a header included with `<…>`, and it binds the call to the services
row whose `kc` column starts with that name. With the row, the `<…>` prototype and the `Builtins`
entry all present, a mod-defined call compiled and ran in the offline harness; without the
prototype it fails with `KC-E003`, without the `Builtins` entry with `KC-E304`.[^offline-harness]
See [adding a service](/modding/adding-a-service.md),
[kC builtins and intrinsics](/language/kc-builtins-and-intrinsics.md),
[kC generated headers](/language/kc-generated-headers.md) and
[services table](/tables/services-table.md).

# (b) Opening the editor and driving build / load / start from code

All of these are public and must run on the main thread.

| goal | call | result to check |
| --- | --- | --- |
| turn kFS on | `KfsConsoleCommands.Execute("on")` or `KfsPrototype.Enabled = true` | `KfsPrototype.Enabled` |
| turn kFS off (hands vehicles back) | `KfsConsoleCommands.Execute("off")` | |
| show the editor | `KfsIntegration.ShowEditor()` | |
| open a file in the editor | `KfsIntegration.Workbench.Edit(path)`; then `Workbench.Editor.GoToLine = n` | `Workbench.Editor.File` |
| build without side effects | `Workbench.BuildText(file, text, part, layout)` | `AssembleOutcome.Ok`, `.Diagnostics`, `.Image` |
| build as the console does | `Workbench.Build(file)` | returned `AssembleOutcome?`; text on `Out` |
| build and load on the controlled vehicle | `Workbench.Load(file + " --vehicle")` | `bool` (true = *queued*) |
| build and load on the bench | `Workbench.Load(file)` | `bool` (true = loaded) |
| editor-style upload of the open file | `Workbench.Upload()` | may finish on a later frame |
| start / stop / reset the target | `Workbench.Start()`, `Stop()`, `ResetTarget()` | `Workbench.Target.Snapshot.State` on a later frame |
| any verb, exactly as typed | `KfsConsoleCommands.Execute("<verb> <args>")` | text only |
| load raw image bytes on a specific vehicle | `KfsVehicleHost.For(vehicle).Computer.Enqueue(c => c.Load(bytes, host.Layout))` | the computer's console log |

[^workbench][^console-commands][^kfs-integration][^kfs-vehicle-host][^host-computer]

Caveats, all read from the code:

- Direct `Workbench` calls skip the flag check that `Execute` applies; with the flag off nothing
  dispatches vehicle computers and `Workbench.Frame` is not pumped.[^kfs-integration][^console-commands]
- Vehicle operations are queued and complete during the next simulation dispatch; they are silent on
  failure except for the computer's console log.[^workbench]
- `Workbench.Load` splits its argument on spaces, so paths with spaces cannot be passed.[^workbench]
- `Workbench.ActiveVehicle()` (and therefore `Load --vehicle`, `SelectTarget("vehicle")`,
  `TargetActiveVehicle()`) attaches a `KfsVehicleHost` to the controlled vehicle and freezes its I/O
  layout as a side effect.[^kfs-integration][^kfs-vehicle-host]
- The only notification is `Workbench.OpenRequested`. To learn that a build, load or start finished,
  poll (`Editor.Building`, `Target.Snapshot`, `Target.Console.Version`) or Harmony-patch the private
  steps (`Workbench.LoadOntoVehicle`, `Workbench.FinishUpload`, `EditorModel.Apply`).

# (c) Capturing kFS output in a mod's own UI

There are three independent streams.

## 1. What the tooling prints

Everything a `kfs` verb or the workbench prints goes through `KfsIntegration.Output`
(`Print` → `Output.WriteLine`; `Workbench.Out` is re-assigned from `Output` on every
`KfsIntegration.Workbench` read).[^kfs-integration] The default is a private writer that forwards
each line to `Program.ConsoleWindow.Print`.

- `Output` is a public settable property: assign a `TextWriter` that forwards to the previous value
  and to the mod's sink. It must handle `WriteLine(string)` **and** `Write(string)` (multi-line text
  ending in `\n`, used by `State`, `Disasm`, `DisasmView` and `Manual`).[^workbench]
- `KfsIntegration.Error(line)` always logs at Error level, and additionally writes to `Output` when
  `Output` is not the default writer type.[^kfs-integration] `Inferred:` a tee that forwards
  everything to the original writer will therefore duplicate error lines in the game console.
- Some lines are printed later, from `Workbench.Frame` (queued vehicle replies, deferred snapshot
  verbs, upload completion, bench faults).[^workbench] A capture that is only active while a command
  executes — which is how purrtty's current Game Console shell captures `ConsoleWindow.Print` —
  misses those; the `Output` tee does not.
- All of this is written on the main thread.

## 2. What the program says

`kfs_logf` / `SVC_LOG` lines, and the host's notices about that computer, are appended to
`HostComputer.Console`, a `ConsoleLog`.[^host-computer][^console-log] They are **not** forwarded to
`Output`.

| need | API |
| --- | --- |
| current target's log | `KfsIntegration.Workbench.Target.Console` |
| a specific vehicle's log | `KfsVehicleHost.Find(vehicle)?.Computer.Console` (`Find` never creates a host) |
| detect new lines | `ConsoleLog.Version` (monotonic) |
| read | `ConsoleLog.CopyTail(count, list)` (returns the version) or `Snapshot()` (copies up to 4096 lines) |
| line shape | `LogLine(ulong K, double TimeS, string Text)` |

The log is thread-safe (one lock), rate-limited (10 program lines and 20 host events per machine
second) and bounded (4096 lines).[^console-log] There is no append event. `Inferred:` for a terminal
that tails a program, polling `Version` once per frame is sufficient and keeps mod code out of the
dispatch; a Harmony postfix on `ConsoleLog.Log` / `Event` / `Host` would give push delivery but
runs on the appending thread — for a vehicle's program output a solver / vehicle-worker thread
inside the dispatch (see [dispatch and threading](/integration/dispatch-and-threading.md)).

Because `Workbench.Bench` and vehicle computers are different `HostComputer` objects, and `Bench` is
replaced when a load names another part, a tailer should re-resolve the `ConsoleLog` each frame (the
editor does exactly this through `Workbench.ConsoleTail`, comparing the log object as well as its
version).[^workbench]

## 3. Numeric telemetry

| data | API |
| --- | --- |
| `trace(channel, v)` samples | `HostComputer.Traces` (`Channels`, `Samples(name)`, `CopyLast`) |
| watched globals | `HostComputer.Watch(path)` / `Unwatch(path)`, samples in `HostComputer.Watches` |
| plotted bus fields | `HostComputer.Bus.Plot(name)`, samples in `Bus.Samples` |
| state, task cycles, traps, profile, perf, bus rows | `HostComputer.Snapshot` (`MachineSnapshot`); call `WantSnapshot()` (one-second lease) to keep a vehicle's snapshot fresh |

See [views](/host/views.md) and [snapshots and perf](/host/snapshots-and-perf.md).

## Exposing `kfs` in a terminal shell

`KfsConsoleCommands.Execute(args)` is the whole command surface as one public static method taking
the text after `kfs`. A terminal shell can call it directly, or call
`Program.TerminalInterface.Execute("kfs " + args)` to go through the game's parser.
[^console-commands][^terminal-interface] `Inferred:` for purrtty the minimal complete integration is
(1) keep executing typed lines through the terminal interface, (2) install the `Output` tee so
deferred lines reach the terminal, and (3) add a per-frame tail of the target's `ConsoleLog` so
program output is visible without the editor's Console tab.

To add **new verbs** (for example `kfs lights ...`) there is no registry: either prefix-patch
`KfsConsoleCommands.Execute(string)` and handle the extra verbs before the original runs, or register
a separate terminal command through `TerminalInterface.RegisterCommand(delegate, owner)` with a
method marked `[TerminalAction("name", "help", ArgParseMode.Unparsed)]`.[^terminal-interface]

# (d) Reference window, editor highlighting and autocomplete

- **The editor has no syntax highlighting, autocomplete, keyword list or builtin list.** The text
  control is `ImGui.InputTextMultiline` wrapped by `KfsTextBoxWidget`; the only "smart" features
  (cost column, bus column, live lines, go-to-definition, diagnostics) are driven by build output.
  Mod-added services therefore need no editor change to be typed, built and navigated.
  [^text-box-widget][^editor-window]
- **Both reference pages are generated from `GameTables`** — Services from
  `tables.ServicesData.Services` + `tables.Services` + cost tables, Opcodes from the opcode and cost
  tables. A mod-added service row appears automatically **if** both the `ServiceRow` and the
  `ServiceInfo` exist, its `CostClass` is a known class, and the tables were patched before the
  window object was constructed (the editor caches one window per page, and each window builds its
  rows once).[^reference-window][^editor-window][^game-tables] Both objects exist when the row was
  added by augmenting `services.json` in a postfix on `GameTables.LoadEmbedded`, the tested route of
  [adding a service](/modding/adding-a-service.md).
- **Hard-coded in the reference window:** the two intro sentences, the opcode section titles, the
  column headers. There is no page for messages, events or enums.
- **Hard-coded lists that do matter for a new service live in the compiler, not the editor:**
  `SystemHeaders.Files["kfs.h"]` (prototypes and `KFS_*` constants) and `SystemHeaders.Builtins`
  (names treated as built in).[^system-headers] They are public mutable collections; their exact role
  is documented in [kC builtins and intrinsics](/language/kc-builtins-and-intrinsics.md).
- **Adding a mod reference page or menu entry:** `KfsReferencePage` has two values and the page
  builders are private, so a mod page is a separate window. `KfsEditorWindow.DrawMenuBar` is a public
  override that executes inside the window's menu bar; `Inferred:` a postfix there can add a menu.

Full detail: [reference window](/tooling/reference-window.md), [editor window](/tooling/editor-window.md).

# Candidate Harmony targets in the tooling

`Inferred:` suggested patch points, derived from the call structure; none was tried.
`Unverified:` whether a patch on the two `Examples` getters takes effect at all — they are trivial
auto-property getters, which the JIT may inline into their callers.

| target | kind | purpose |
| --- | --- | --- |
| `KSA.Kfs.KfsConsoleCommands.Execute(string)` (public static) | prefix | handle additional `kfs` verbs |
| `KSA.Kfs.KfsConsoleCommands.Help()` (private static) | postfix | list the mod's verbs under `kfs help` |
| `KSA.Kfs.Host.Workbench.Examples.get_Menu` / `get_Names` (public static getters) | postfix | add example categories and names |
| `KSA.Kfs.KfsEditorWindow.DrawMenuBar()` (public override) | postfix | add a menu to the editor |
| `KSA.Kfs.KfsIntegration.Error(string)` (public static) | prefix or postfix | capture errors without the duplicate-line issue of a tee |
| `KSA.Kfs.Host.Services.ConsoleLog.Log` / `Event` / `Host` (public instance) | postfix | push delivery of program output (worker thread) |
| `KSA.Kfs.Host.Workbench.Workbench.LoadOntoVehicle` (private) | postfix | observe loads |

Alternatives that need **no patch**: the `Output` tee, per-frame polling of `ConsoleLog.Version`,
calling `Workspace.Write` / `Workbench.*` / `KfsConsoleCommands.Execute`, mutating
`SystemHeaders.Files`, and registering a separate terminal command.

# Risks specific to the tooling

- **Prototype status.** The whole feature is behind `KfsPrototype`; every signature here can change
  between builds. Prefer the public, no-patch routes and probe for members before use.
- **Lazy singletons.** `KfsIntegration.Tables` and `KfsIntegration.Workbench` are created on first
  access. Table patches must happen before the first compile, header generation or reference window;
  the earliest safe moment is before anything reads `KfsIntegration.Tables` (the workbench getter
  reads it).[^kfs-integration] Verified offline: a Harmony postfix on `GameTables.LoadEmbedded`
  applied before that first read made `KfsIntegration.Tables` return the augmented instance, the
  same object on every read.[^offline-harness]
- **The CLI ignores live tables** (`GameTables.LoadEmbedded()` per run), so it will not understand
  services a mod added by mutating `KfsIntegration.Tables` in place without `--game DIR`.[^cli]
  `Inferred:` rows added in a `GameTables.LoadEmbedded` postfix reach it too, because each run calls
  the patched method. The CLI is unreachable in the shipped game either way — see
  [CLI](/tooling/cli.md).
- **Cached UI.** Reference windows and the generated header text are cached; the editor instance is
  cached in a private static.
- **Thread affinity.** Tooling is main-thread code. Vehicle computers are dispatched on solver /
  vehicle-worker threads, concurrently with the UI; cross-thread hand-off is through
  `HostComputer.Enqueue` and the workbench's private `_pending` queue only.
- **Flag off.** `kfs off` hands every vehicle back and stops the frame pump; a mod UI should reflect
  that state rather than keep polling stale snapshots.

[^kfs-integration]: KfsIntegration.cs — nested `FrameHook` :15-37 and `ConsoleWindowWriter` :39-75 (private); private statics :77-85; `Editor` (internal) :87; `Tables` :89; `Output` :91; `Workbench` :93-121; `Print` :123-126; `Error` :128-135; `UseWorkbench` :137-144; `RegisterCommands` :146-156 and :178-181; `BeforeInputEvents` :158-176; `ShowEditor` :183-198; `ToggleEditor` :200-214.
[^console-commands]: KfsConsoleCommands.cs — private `Verb` and `_verbs` :13-173; `Verbs` :175-185; `Register` :187-190; `Execute` :192-232; private helpers :234-327.
[^workbench]: Workbench.cs — settable properties :76-80, :167-169; `OpenRequested` :205; `Print` and direct `Out.Write` uses :391-394, :1350, :1393, :1406, :2073, :2086, :2132; `Edit` :420-451; `BuildText` :483-587; kASM include fallback :526-529; `Build` :659-678; `Load` (space-split arguments) :739-807; `LoadOntoVehicle` :809-822; `Upload` / `FinishUpload` :856-916; `Start` / `Stop` / `ResetTarget` :973-1091; `Frame` (deferred printing) :1168-1200; `ConsoleTail` :1739-1746; `CopyExample` :2142-2154.
[^workspace]: Workspace.cs — `Write` :105-111; `ProjectOf` (folder project takes every source file) :182-193.
[^examples]: Examples.cs — `Files` :41-59; `Menu` :61-79; `Names` :81; `AllNames` :83-84; `MainFile` :91-95; `Write` overloads :97-123.
[^editor-window]: KfsEditorWindow.cs — class declaration :24; `_text` :506; cached reference windows :602-604; `DrawContent` :713; `DrawMenuBar` :943-1006; `OpenReference` :1008-1011; `DrawExampleItems` :1013-1035.
[^reference-window]: KfsReferenceWindow.cs — constructor (builds once) :115-131; `BuildOpcodes` :241-281; `BuildServices` :373-433.
[^text-box-widget]: KfsTextBoxWidget.cs — `InputTextMultiline` :66.
[^system-headers]: SystemHeaders.cs — `Files` :8-22 (`kfs.h` :20); `Builtins` :24-30; `Get` :34-41.
[^kc-front-end]: KcFrontEnd.cs — include resolvers (project files, generated `.kasm.h`, `SystemFiles`, `vehicle.h`, `SystemHeaders.Get`) :75-79; nested assembler `ProjectFiles` :334.
[^console-log]: ConsoleLog.cs — limits :9-13; `Version` :35-44; `Log` :61-78; `Event` :99-116; `Host` :118-128; `Snapshot` :168-174; `CopyTail` :176-187.
[^host-computer]: HostComputer.cs — `Console`, `Traces`, `Watches`, `Leaves`, `Bus` :135-143; `Snapshot` :167; `WantSnapshot` :229-232; `Enqueue` :288-291; `Load` :302-344; `Watch` / `Unwatch` :595-623; program log append :754-770.
[^kfs-vehicle-host]: KfsVehicleHost.cs — `AllHosts` (internal) :39; constructor :105-118; `For` :139-142; `Find` :144-147.
[^game-tables]: GameTables.cs — `ServicesData` :41 (among the data properties :25-51); `Services` :67; `BuildServices` :538-582.
[^terminal-interface]: TerminalInterface.cs — `Execute(string)` :41-56; `RegisterObject` :68-88; `RegisterCommand` :105-124.
[^cli]: Cli.cs — `Run` (fresh `GameTables.LoadEmbedded()`) :264-291. KcCli.cs — `Run` :15-171.
[^offline-harness]: Experiments E3, E10 and E11 in the offline harness document, and its spike program.
