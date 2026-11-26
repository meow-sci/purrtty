---
type: Subsystem Reference
title: kFS editor window
description: KfsEditorWindow and its EditorModel — how the in-game kFS editor is opened, its menus, toolbar, hotkeys and eleven tabs, what each user action invokes, and what a mod can and cannot hook.
resource: ksa-decomp:KSA.Kfs/KfsEditorWindow.cs
tags: [kfs, tooling, editor, imgui, ui, extension]
sources:
  - id: editor-window
    resource: ksa-decomp:KSA.Kfs/KfsEditorWindow.cs
    title: KfsEditorWindow (decompiled)
  - id: editor-model
    resource: ksa-decomp:KSA.Kfs.Host.Workbench/EditorModel.cs
    title: EditorModel (decompiled)
  - id: kfs-integration
    resource: ksa-decomp:KSA.Kfs/KfsIntegration.cs
    title: KfsIntegration (decompiled)
  - id: text-box-widget
    resource: ksa-decomp:KSA.Kfs/KfsTextBoxWidget.cs
    title: KfsTextBoxWidget (decompiled)
  - id: text-widget-interface
    resource: ksa-decomp:KSA.Kfs/IKfsTextWidget.cs
    title: IKfsTextWidget (decompiled)
  - id: new-popup
    resource: ksa-decomp:KSA.Kfs/KfsNewPopup.cs
    title: KfsNewPopup (decompiled)
  - id: delete-popup
    resource: ksa-decomp:KSA.Kfs/KfsDeletePopup.cs
    title: KfsDeletePopup (decompiled)
  - id: debug-window
    resource: ksa-decomp:KSA.Kfs/KfsDebugWindow.cs
    title: KfsDebugWindow (decompiled)
  - id: imgui-window
    resource: ksa-decomp:KSA/ImGuiWindow.cs
    title: KSA ImGuiWindow base class (decompiled)
  - id: workbench
    resource: ksa-decomp:KSA.Kfs.Host.Workbench/Workbench.cs
    title: Workbench (decompiled)
  - id: program
    resource: ksa-decomp:KSA/Program.cs
    title: KSA Program (decompiled)
  - id: view-throttle
    resource: ksa-decomp:KSA.Kfs.Host.Views/ViewThrottle.cs
    title: ViewThrottle (decompiled)
  - id: scope-model
    resource: ksa-decomp:KSA.Kfs.Host.Views/ScopeModel.cs
    title: ScopeModel (decompiled)
generated: { by: claude-code/claude-fable-5-2, at: 2026-11-25T21:56:52Z }
status: stable
ksa_build: "2026.11.25.5949"
---

# What it is

`KSA.Kfs.KfsEditorWindow` (`public sealed class KfsEditorWindow : ImGuiWindow, IStaticWindow, IDisposable`)
is the in-game IDE for kFS: a file tree and text editor, plus ten inspection tabs for the build and
for the running target.[^editor-window] It is a pure view over a
[Workbench](/tooling/workbench.md) and that workbench's `EditorModel`; every action in it is a call
on one of those two.

# How it is opened

There is **no menu item and no hotkey** for the editor in this build; the only kFS entry in the
game's menus is View > Debug > "Show kFS Debug".[^program] The editor appears through:

| trigger | path |
| --- | --- |
| `kfs editor` | `KfsIntegration.ToggleEditor()`[^kfs-integration] |
| `kfs edit [file]`, `kfs new <file>`, and anything else that ends in `Workbench.Edit` (`kfs def`, `kfs disasm ... --copy`) | `Workbench.OpenRequested` → `KfsIntegration.ShowEditor()`[^workbench][^kfs-integration] |
| the "kFS ON" button of the kFS Debug window | `KfsConsoleCommands.Execute("on")` then `KfsIntegration.ShowEditor()`[^debug-window] |
| code | `KfsIntegration.ShowEditor()` / `ToggleEditor()` (both `public static`) |

`ShowEditor` and `ToggleEditor` do nothing while `Program.Instance == null`. On first use they create
`new KfsEditorWindow(Workbench)` and keep it in the private static `_editor` (exposed as
`internal static KfsIntegration.Editor`). Whenever the window goes from hidden to shown they call
`Workbench.TargetActiveVehicle()`, which switches the target from the bench to the controlled
vehicle when there is one.[^kfs-integration]

## Construction and lifetime

The constructor:[^editor-window]

- passes `DefaultWindowSize` (1280 x 860, times the interface scale), `lockAspectRatio: false`,
  `show: false` to `ImGuiWindow`;
- sets `workbench.Editor.Background = true` (editor builds run on the thread pool from then on);
- reserves three trace buffers and preallocates the scope plot arrays;
- sets the title `kFS Editor`, `UseConsoleChrome = true` and `ConsoleSignature = "KSA-KFS"`.

`ImGuiWindow`'s constructor adds every instance that implements `IStaticWindow` to a private static
list; `ImGuiWindow.DrawAllStaticWindows` draws those whose `IsShown` is true each frame.[^imgui-window]
The editor does **not** override `IsShown`, so its visibility does not depend on the prototype flag —
but `Workbench.Frame` is only pumped while the flag is on, so with the flag off an open editor shows
stale data and background builds never complete.[^kfs-integration] On its first draw the window
resizes itself to one third of the game window's width and centres itself.[^editor-window]

# Layout

```text
┌ kFS Editor ───────────────────────────────────────────────────────────────┐
│ File   Edit   View   Reference                                  (menu bar) │
│ [target: bench ▾] [part: Bench ▾]  main.c *      [✓] [⇧] [▶] [■] [↻]       │
│ ┌ Code │ Diagnostics │ Report │ Profiler │ Scope │ Bus │ NaN watch │       │
│ │        Disassembly │ Listing │ SRAM │ Console ─────────────────────────┐ │
│ │ workspace  ║ [persistence] [fade]     - Font +        [cycles] [bus]   │ │
│ │  proj/     ║   1  #include <kfs.h>                     12    < MSG_... │ │
│ │   main.c * ║   2  ...                                                  │ │
│ └────────────╨───────────────────────────────────────────────────────────┘ │
│ bench: Running, k 120, 220/20000 cycles  |  compiled proj/main.c           │
└────────────────────────────────────────────────────────────────────────────┘
```

The sketch is schematic (file names and numbers are placeholders). Tab names are the internal static array `TabNames`: `Code`, `Diagnostics`, `Report`, `Profiler`,
`Scope`, `Bus`, `NaN watch`, `Disassembly`, `Listing`, `SRAM`, `Console`.[^editor-window]

# Menus

| menu | item | action |
| --- | --- | --- |
| File | New Project... | `KfsNewPopup.CreateProject(workbench)` |
| File | New File... | `KfsNewPopup.CreateFile(workbench, workbench.CurrentProject)`; tooltip: "A new file in the open file's project, or in a new project named after it when none is open." |
| File | Save (`Ctrl+S`), enabled with a file open | `model.Save()` — writes the file **and builds** |
| File | Revert, enabled with a file open | `model.ReloadFromDisk()` when an external change is pending, else `model.Revert()` |
| File | Examples > `kC` / `kAsm` / `Misc` > *folder* | `workbench.CopyExample(folder)`; tooltip: "Copies the example into the workspace and opens it. Files already there are kept." |
| Edit | Go to definition (`F12`), enabled with a file open | `GoToDefinition(model)` |
| View | Always on Top, Detached (when `Program.AllowMultipleWindows`), Autohide | inherited from `ImGuiWindow.DrawViewMenu` |
| Reference | Opcodes | opens the opcode [reference window](/tooling/reference-window.md); tooltip: "Every kVM instruction: encoding, stack effect, what it does and its cycles on each core." |
| Reference | Services | opens the services reference window; tooltip: "Every syscall: the kC call, the kASM symbol and push order, mode, cost and what it does." |

[^editor-window][^imgui-window] The two reference windows are created on first use and cached in the
editor instance (`_opcodeReference`, `_serviceReference`).

# Hotkeys

Handled in `HandleShortcuts`, only while the window or one of its children has focus **and** a file is
open:[^editor-window]

| key | action |
| --- | --- |
| `LeftCtrl` + `S` | `model.Save()` (save and build) |
| `F12` | go to the definition of the identifier under the cursor |
| `Tab` (in the text box) | inserts four spaces, replacing any selection[^text-box-widget] |

Only the left Control key is tested. There are no shortcuts for build-without-save, upload, start or
stop.

# Toolbar

| control | shows | action |
| --- | --- | --- |
| target combo | `target: <vehicle id or bench>` | `bench` → `SelectTarget("bench")`; the controlled vehicle's id → `SelectTarget("vehicle")`; with no controlled vehicle a dimmed `vehicle (none controlled)`. Tooltip: "Where Upload puts the file, and what play, stop and reset act on: the bench or the vehicle you control. Switching doesn't upload or stop anything." |
| part combo | `part: <target part>` or `preview: <part>` | `<part> (the target's part)` → `Preview("off")`; `preview on <part> (<core>)` for each catalogue part → `Preview(part.Name)`. Tooltip: "Preview recompiles the open file for another part: the report and the cost column change; nothing is loaded." |
| file label | file name, ` *` when there are unsaved edits, `(no file)` | tooltip: the workspace path |
| Build (check mark, green) | | `model.Assemble()` — builds the current text without saving |
| Upload (arrow), enabled with a file open | | `workbench.Upload()`; tooltip: "Upload: save, build and put the file on the target" |
| Start (play) — shown while the target is not running | | `workbench.Play()` |
| Pause — shown while the target runs | | bench: `workbench.Pause()`; vehicle: disabled, tooltip "Pause (a vehicle's computer can't pause: stop returns the vehicle to its flight computer)" |
| Stop (square, red) | | `workbench.StopTarget()` |
| Reset (circular arrow) | | `workbench.ResetTarget()` |

[^editor-window] What these calls do is traced in
[build and load flow](/tooling/build-and-load-flow.md).

## Status line

The window footer shows
`<target>: <state>, k <k>, <cycles>/<budget> cycles[, trap <code>]` (in the warning style when there
is a trap), then `  |  ` and `building …` or the model's brief status (`compiled <file>`,
`assembled <file>`, `<n> error(s)`, `opened <file>`, ...), then optionally `  |  ` and the result of
the last go-to-definition or plot attempt.[^editor-window]

# Tabs

## Code

A resizable file tree (default 170 px, drag the 6 px divider) and the editor pane.[^editor-window]

**File tree.** Header `workspace`. Folders are tree nodes; files are selectable rows.

| interaction | action |
| --- | --- |
| click a file | `workbench.Edit(file)` |
| right-click a folder | New File... → `KfsNewPopup.CreateFile(workbench, folder)` |
| right-click a file | New File... (when the file is in a folder), Delete → `KfsDeletePopup.Create(workbench, file)` |

The open file is highlighted, its folder chain is expanded and scrolled into view once, and files
with unsaved edits carry ` *`. The tree is rebuilt when the workspace watcher fires (see
[workspace and files](/tooling/workspace-and-files.md)).

**Editor pane.** With no file open it shows
`No file open: kfs edit <file>, kfs new <file> (.c or .kasm), or pick one on the left.` Otherwise a
row of controls and the text:

| control | effect |
| --- | --- |
| `persistence` toggle (on by default) and its slider (1 to 60 s, logarithmic; fully right = `inf`) | lights the lines the target executed recently, fading over the chosen time |
| `-` / `Font` / `+` | code font scale, 0.6 to 2.5 in steps of 0.1, default 0.9; double-click `Font` to reset |
| `cycles` toggle | the cost column: estimated cycles per line (`/it` per loop iteration, `[n]` kASM operand stack depth); hovering a cell shows `line <n>: <soft routines>` |
| `bus` toggle | the bus column: `< NAME` for messages a line reads, `> NAME` for writes; clicking one opens the Bus tab on that message |

The text itself is a line-number gutter plus an `IKfsTextWidget`. The only implementation,
`KfsTextBoxWidget`, is a plain `ImGui.InputTextMultiline` over a 262,144-byte buffer with flags
`AllowTabInput | CallbackAlways | CallbackCharFilter`.[^text-box-widget] Consequently:

- **no syntax highlighting, no autocomplete, no keyword or builtin list** exists anywhere in the
  editor;
- language awareness is limited to data from the last build: the cost and bus columns, live lines,
  go-to-definition and diagnostics.

Live lines are drawn only when the editor's successful build of the *current* text is byte-identical
to the image loaded on the target (`LiveSourceIsLoaded`); the tooltip otherwise says "The computer
isn't running this text. Build it, then load and start it, to see its lines run." The data comes
from the target's execution trace (`target.WantExecTrace()`, `LiveLines`).[^editor-window] See
[profiler and exec trace](/vm/profiler-and-exec-trace.md).

**Go to definition** takes the identifier at the cursor (`Definitions.WordAt`), looks it up in the
definitions of the last successful build (`model.GoToDefinition`) and jumps there, opening another
workspace file if needed. Status texts: `no identifier at the cursor`,
`'<word>' isn't defined in the last build`, `<kind> <name>: <file>:<line>`. Definitions whose file
name starts with `<` (built-in sources) are reported but not opened.[^editor-window][^editor-model]

## Diagnostics

The tab is tinted red while `model.Diagnostics` is non-empty. Content:[^editor-window]

- `changed on disk; Revert loads the new text` when an external change is pending;
- `no build yet: Save or Build` or `no diagnostics`;
- one selectable row per diagnostic, `<file>:<line>:<col> error|warning <code>: <message>`; clicking
  jumps to the location (`model.JumpTo`) and switches to the Code tab; hovering shows the
  diagnostic's manual page (`DiagnosticManual.Page`).

## Report

Shows the compile report of `model.Preview ?? model.Outcome ?? workbench.CurrentBuild` through
`workbench.EditorReportOf` (a `ReportView`), with measured values when that build is the one running
(`workbench.RunningSnapshotOf`).[^editor-window]

- `no build yet` / `the last build failed: see Diagnostics`; in preview mode
  `PREVIEW on <part>: nothing is loaded`.
- The machine line, then two usage bars (`report.Program`, `report.Ram`) with coloured segments and a
  legend.
- A table per task: `task`, `RAM stack` (`(overlaid)` when applicable), `cells / entries`, `WCET`
  (`<wcet> / <budget>` or `none`), `measured max` (red with ` !` and a tooltip when it exceeds the
  estimate; `(not running)` when the build is not loaded), `deepest path`,
  `bus (subscribes / publishes)`.
- `Calls <...>`, then one selectable row per linked library item
  (`<kind> <name> [<n> B] ← <first use>`); clicking jumps to the first use.
- `globals by size` (the first 24: name, size, address), then the report's text.

See [compile report](/compiler/compile-report.md) and
[program analysis and WCET](/compiler/program-analysis-and-wcet.md).

## Profiler

A table built from the target snapshot, refreshed at most every 0.25 s (`ViewThrottle`) and
pausable.[^editor-window][^view-throttle]

- `Exclude Services` toggle: lit = every row, totals include callees; off = only the program's own
  functions (bus copies, services, soft operations and linked library functions left out).
- Header: `<target>: <n> ticks since reset; last tick <cycles> / <budget> cycles; per function its last run`.
- Per task: `task <name> (<kind>): <n> cycles in its last run (<pct> %, <age|not run>), measured max <n>`
  with a bar that turns red above 90 % of the budget.
- `program task starved on <n> tick(s)` when applicable.
- Table columns: `function`, `self (last run)`, `total (last run)`, `age`, `avg self`, `avg total`,
  `calls`; callee rows are indented with `└`.
- `overruns (<n>, last 64):` and the last ten as `k <k>: task <name>`.
- Empty: `<target>: no profile yet (load, start and run a program)`.

## Scope

The oscilloscope (`ScopeModel`).[^editor-window][^scope-model]

- `zoom in` / `zoom out` (window of 8 to 2048 samples, default 256) and `window <n> samples`.
- A combo of the loaded image's global leaves (first 400) and a `Watch` button →
  `target.Watch(path)`.
- A pause toggle ("the program keeps running").
- One plot per series, titled `trace <name>`, `global <name>` or `message <name>` with min, max and
  the last value; `unwatch` / `unplot` buttons on watched globals and message fields. Hovering a plot
  shows `k <k>: <value>`.
- Empty: `no trace channels, watched globals or message fields: pick a global and Watch, plot a field on the Bus tab, or call trace from kASM`.

## Bus

The bus monitor for the target (see [message bus](/host/message-bus.md)).[^editor-window]

- Optional panel `definition of <MSG> (from a bus marker)` with a `close` button.
- `bus of <target> after tick <k> (<hz> Hz)` and a table: `message`, `publisher`
  (`<publisher>, task <task>`), `seq`, `age`, `rate`, `here` (`pub+sub`, `pub` or `sub`). Hovering a
  message shows its definition; clicking selects it.
- `plotted on the Scope tab (<n>/16):` with one `<field> x` button per plotted field.
- For the selected message: one row per field (`<path> <value>`) with a `plot` / `unplot` button
  (not for `hdr.*` fields). `pick a message to see its fields` / `waiting for the next tick's slot`.
- Empty: `<target>: no messages on the bus (load a program that subscribes to or publishes one)`.

## NaN watch

Tinted red when the snapshot reports a NaN. `no NaN since reset`, or the first-NaN line (clickable
when it has a source location: `workbench.OpenSource(file, line)`) followed by the globals that went
NaN.[^editor-window]

## Disassembly

For the **loaded image** of the target: a function combo, a `Copy as kASM` button
(`workbench.DisasmView("<f> --copy <f>_copy.kasm")`; tooltip "Writes this function as a new .kasm
file and opens it: a starting point for a hand-optimised version."), and rows
`  <pc hex>  <cost>  <instruction>` under dimmed source comments `; <file>:<line>  <source>`.
`<target>: nothing loaded` otherwise.[^editor-window]

## Listing

For the **editor's last build** (`model.ListingLines`, not the loaded image): header
`pc      cycles  instruction`, a `name:` line per function and rows
`  <pc hex>  <cost>  <text>   ; <line>`.[^editor-window] See
[build and load flow](/tooling/build-and-load-flow.md), "Listing".

## SRAM

A bit map of the target's RAM. Calls `workbench.WantRam()` each frame so snapshots carry RAM.
Controls: `SRAM <used> / <total> KB`, an `all` checkbox (whole RAM instead of the used regions), a
`px` slider (1 to 6) and a `words/row` slider (1 to 32). Regions are labelled
`globals and task RAM stacks`, `VM stack region`, or
`globals, task RAM stacks and VM stack region` with their address range; hovering a word shows
`0x<address> [<symbol>]: 0x<word>`. `nothing loaded` / `waiting for a snapshot` otherwise.
[^editor-window] See [memory map and system block](/vm/memory-map-and-sysblock.md).

## Console

The last 60 lines of the target's console log (`workbench.ConsoleTail(60, ...)`), each as
`[k <k> t <seconds>] <text>`, auto-scrolling while the view is at the bottom.[^editor-window] This is
where a program's `kfs_logf` output and the host's load / start / trap / rejection notices appear;
see [console commands](/tooling/console-commands.md), "Output channels".

# Popups

| popup | title | content | buttons |
| --- | --- | --- | --- |
| `KfsNewPopup.CreateProject(workbench)` | `NEW PROJECT` | intro "A project is a folder in the workspace; the files in it build together."; `NAME` field; language selector `kC  (main.c)` / `kASM  (main.kasm)` | `CREATE` → `workbench.NewProject(name, kc)`; `CANCEL` |
| `KfsNewPopup.CreateFile(workbench, project)` | `NEW FILE` | intro "A new file in `<project>`. Without an extension it is a .c file; any extension works, but only .c and .kasm files build." or, with no project, "No project is selected, so a project named after the file is created to hold it. Without an extension it is a .c file."; `NAME` field | `CREATE` → `workbench.NewFile(project, name)`; `CANCEL` |
| `KfsDeletePopup.Create(workbench, file)` | `DELETE` | "Delete `<file>`? This can't be undone." | `DELETE` → `workbench.DeleteFile(file)`; `CANCEL` |

Enter in the name field also creates; an error text returned by the workbench is shown in red and the
popup stays open.[^new-popup][^delete-popup]

# `EditorModel`

`KSA.Kfs.Host.Workbench.EditorModel` (`public sealed`) holds the open file's text, the saved copy, the
editor's build and navigation requests. It has no UI dependency.[^editor-model]

| member | meaning |
| --- | --- |
| `string? File` | workspace-relative path of the open file |
| `string Text { get; set; }` | the current text; setting it bumps `TextVersion` and recomputes `Dirty` |
| `void TextEdited(ReadOnlySpan<byte> utf8, Func<string> read)` | cheap "the widget's buffer changed" notification; the string is materialised lazily |
| `int TextVersion`, `int LineCount`, `bool Dirty`, `bool TextIsSaved`, `ReadOnlySpan<byte> SavedUtf8` | change tracking |
| `bool Open(string file)` | loads a file (stashing unsaved edits of the previous one); false when the file is too large |
| `void Close()`, `void Forget(string file)`, `bool HasUnsavedEdits(string file)` | |
| `AssembleOutcome? Save()` / `Save(bool build)` | write the file, optionally `Assemble()` |
| `void Revert()`, `void ReloadFromDisk()` | discard edits |
| `AssembleOutcome? Assemble()` | build now (`Background == false`) or start a background build and return `null` |
| `bool Poll()`, `AssembleOutcome? Wait()` | apply / wait for a background build |
| `bool Background`, `bool Building` | build mode and in-progress flag |
| `AssembleOutcome? Outcome`, `int OutcomeTextVersion` | the editor's build and the text version it was built from |
| `AssembleOutcome? BuildOf(int textVersion, PartDescription part, IoLayout layout)` | the build, if still valid for that text, part and layout |
| `IReadOnlyList<Diagnostic> Diagnostics`, `IReadOnlyList<ListingLine> ListingLines`, `LineCostRow[] LineCosts`, `IReadOnlyList<Definition> Definitions`, `double? LastAssembleMs` | results of the last build |
| `AssembleOutcome? Preview { get; set; }` | a build for another part |
| `string Status`, `string BriefStatus` | status texts |
| `int? GoToLine { get; set; }` | one-shot request consumed by the window (move cursor, scroll, highlight) |
| `bool ExternalChangePending`, `bool CheckExternalChange()` | on-disk change tracking |
| `void JumpTo(Diagnostic d)`, `void JumpTo(string file, int line)`, `Definition? GoToDefinition(string name)` | navigation; opens another file through `OpenAt` |
| `Action<string,int>? OpenAt`, `Action<string>? BuildLogged` | callbacks set by the workbench |

# Internals useful to know

- **Performance metering.** The window measures its own draw time (`DrawNs`) and per-tab, per-frame
  managed allocations (`AllocMeter`; `FrameAllocation`, `WindowAllocation`, `FooterAllocation`,
  `TabAllocations` are `internal`). The [debug window](/tooling/debug-window.md) displays them.
  [^editor-window]
- **Views are throttled and cached** (`ViewThrottle`, per-outcome caches) so a tab that is not
  visible costs nothing; only visible tabs request data (`WantRam`, `WantBus`, `WantExecTrace`), each
  with a one-second lease on the target.[^editor-window][^view-throttle]
- **Text widget seam.** `private readonly IKfsTextWidget _text = new KfsTextBoxWidget();` The
  interface is public:[^text-widget-interface]

  ```csharp
  public interface IKfsTextWidget
  {
      void Draw(EditorModel model, float2 size);
      void MoveToLine(string text, int line);
      int CursorOffset(string text);
  }
  ```

# Modding notes

- **Opening the editor from a mod:** `KfsIntegration.ShowEditor()`; opening a specific file:
  `KfsIntegration.Workbench.Edit("proj/main.c")` (which raises `OpenRequested` and so also shows the
  window). Jump to a line with `KfsIntegration.Workbench.Editor.GoToLine = n`.
  [^kfs-integration][^workbench]
- **Getting the instance.** `KfsIntegration.Editor` is `internal` and `_editor` is `private static`;
  a mod needs reflection (or Harmony `AccessTools`) to reach the live window. The class itself and its
  constructor are public, so a mod may also create its own `new KfsEditorWindow(workbench)` — note
  that every `ImGuiWindow` that is an `IStaticWindow` registers itself in a private static list and
  is never removed from it.[^imgui-window]
- **No syntax tables to extend.** There is no keyword, type, builtin or service-name list in the
  editor, so mod-added services need nothing here to be "recognised": they are plain identifiers
  in an `InputTextMultiline`. What makes them usable is the compiler side
  ([kC builtins and intrinsics](/language/kc-builtins-and-intrinsics.md)) and what makes them
  discoverable is the [reference window](/tooling/reference-window.md).
- **Adding UI.** All drawing methods except `DrawContent` and `DrawMenuBar` (public overrides) are
  private. `DrawMenuBar` runs inside the window's menu bar, so `Inferred:` a Harmony postfix on
  `KfsEditorWindow.DrawMenuBar` can append a menu (for example a Reference entry for mod-added
  services). The tab bar is opened and closed inside `DrawContent`; adding a tab needs a transpiler
  or a prefix that redraws the whole content, so a separate window is the safer design.
- **Replacing the text widget.** `_text` is a private readonly instance field typed as the public
  interface `IKfsTextWidget`; `Inferred:` a mod could swap it by reflection for a widget with
  highlighting. The contract is small: draw the model's text and call `model.TextEdited(...)` on
  change, move the cursor on `MoveToLine`, and report the cursor's character offset.
- **Flag coupling.** The window stays visible when the flag is turned off but nothing pumps the
  workbench; a mod that shows the editor should make sure `KfsPrototype.Enabled` is true.
- **Threading.** Everything here runs on the main thread inside the ImGui frame, except builds
  (thread pool) whose results are applied by `Workbench.Frame`.

[^editor-window]: KfsEditorWindow.cs — `Tab` enum :26-40; `AllocMeter` :42-80; `ScopePlot` :251-486; default size :492; `_text` :506; `TabNames` :584-588; constructor :700-711; `DrawContent` (tab bar) :713-829; `SetInitialWidth` :836-844; `DrawMenuBar` :943-1006; `OpenReference` :1008-1011; `DrawExampleItems` :1013-1035; `Revert` :1037-1047; `GoToDefinition` :1049-1067; `HandleShortcuts` :1069-1082; `DrawToolbar` :1182-1315; `DrawStatusLine` :1324-1370; `DrawCode` / `DrawFileTree` / `DrawFolder` :1372-1552; `DrawEditor` :1554-1635; `UpdateLive` / `LiveSourceIsLoaded` / `LiveTip` :1637-1680; `DrawEditorRows` :1714-1844; `DrawBus` :1846-2051; `ShowInBusTab` :2053-2067; `DrawDiagnostics` :2114-2154; `DrawReport` :2156-2342; `DrawProfiler` :2396-2568; `DrawScope` :2570-2709; `DrawNan` :2711-2748; `DrawDisassembly` :2750-2828; `DrawListing` :2830-2862; `DrawConsole` / `DrawConsoleLines` :2864-2895; `DrawSram` :2915-3098.
[^editor-model]: EditorModel.cs — properties :54-137; `TextEdited` :148-155; constructor :205-209; `Open` :211-241; `HasUnsavedEdits` :243-254; `Forget` :310-313; `Close` :330-342; `Save` :344-365; `BuildOf` :367-394; `Revert` :396-404; `Assemble` :406-427; `Poll` :429-449; `Wait` :451-456; `Apply` :458-478; `CheckExternalChange` :490-534; `ReloadFromDisk` :536-543; `JumpTo` :545-569; `GoToDefinition` :571-579.
[^kfs-integration]: KfsIntegration.cs — `FrameHook.IsShown` (flag-gated pump) :17-27; `_editor` :79; `Editor` (internal) :87; `OpenRequested` subscription :113-116; `ShowEditor` :183-198; `ToggleEditor` :200-214.
[^text-box-widget]: KfsTextBoxWidget.cs — buffer :11-13; `Draw` (`InputTextMultiline` flags) :41-72; `MoveToLine` :74-79; `CursorOffset` :81-93; tab-to-spaces callback :95-128.
[^text-widget-interface]: IKfsTextWidget.cs:6-13.
[^new-popup]: KfsNewPopup.cs — language labels :10; buttons :12-22; intro texts :47; factories :50-58; `OnDrawUi` :60-106; `TryCreate` :108-120.
[^delete-popup]: KfsDeletePopup.cs — buttons :7-17; question :29; `Delete` :50-54.
[^debug-window]: KfsDebugWindow.cs — `DrawEnable` ("kFS ON") :112-121.
[^imgui-window]: ImGuiWindow.cs — static window list :11; `IsShown` :75; `Toggle` / `SetShown` :79-87; constructor (self-registration at :116-119) :99-120; `RegisterStaticWindow` :122-125; `DrawAllStaticWindows` :127-137; console-chrome toolbar menu bar calling `DrawMenuBar` :397-400; `DrawConsoleFooter` :407-409; `DrawViewMenu` :411-442; `DrawMenuBar` :444-451.
[^workbench]: Workbench.cs — `OpenRequested` :205; `Edit` :420-451; `Upload` :856-878; `Play` / `Pause` / `StopTarget` / `ResetTarget` :1022-1091; `ConsoleTail` :1739-1746; `WantRam` :1748-1756; `RunningSnapshotOf` / `EditorReportOf` :1758-1780; `OpenSource` :2089-2101; `CopyExample` :2142-2154.
[^program]: Program.cs — View menu :4096; Debug submenu :4305; "Show kFS Debug" item :4423. No other reference to a kFS window exists in Program.cs.
[^view-throttle]: ViewThrottle.cs:5-44 (default interval 0.25 s).
[^scope-model]: ScopeModel.cs — `Window` default and `Zoom` clamp :74-81; `Live` :110-133; `Visible` :135 onward.
