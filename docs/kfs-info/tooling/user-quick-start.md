---
type: How-To
title: kFS player quick start
description: Step-by-step for a player — enable the kFS prototype, open the editor, create and build a program, load it onto the controlled vehicle, start it, watch its logs and traces, debug and stop it — with every step traced to the code that implements it.
resource: ksa-decomp:KSA.Kfs/KfsConsoleCommands.cs
tags: [kfs, tooling, how-to, quick-start, editor, console]
sources:
  - id: kfs-prototype
    resource: ksa-decomp:KSA.Kfs/KfsPrototype.cs
    title: KfsPrototype (decompiled)
  - id: console-commands
    resource: ksa-decomp:KSA.Kfs/KfsConsoleCommands.cs
    title: KfsConsoleCommands (decompiled)
  - id: kfs-integration
    resource: ksa-decomp:KSA.Kfs/KfsIntegration.cs
    title: KfsIntegration (decompiled)
  - id: debug-window
    resource: ksa-decomp:KSA.Kfs/KfsDebugWindow.cs
    title: KfsDebugWindow (decompiled)
  - id: editor-window
    resource: ksa-decomp:KSA.Kfs/KfsEditorWindow.cs
    title: KfsEditorWindow (decompiled)
  - id: workbench
    resource: ksa-decomp:KSA.Kfs.Host.Workbench/Workbench.cs
    title: Workbench (decompiled)
  - id: workspace
    resource: ksa-decomp:KSA.Kfs.Host.Workbench/Workspace.cs
    title: Workspace (decompiled)
  - id: editor-model
    resource: ksa-decomp:KSA.Kfs.Host.Workbench/EditorModel.cs
    title: EditorModel (decompiled)
  - id: examples
    resource: ksa-decomp:KSA.Kfs.Host.Workbench/Examples.cs
    title: Examples (decompiled)
  - id: console-window-ex
    resource: ksa-decomp:KSA/ConsoleWindowEx.cs
    title: ConsoleWindowEx (decompiled)
  - id: program
    resource: ksa-decomp:KSA/Program.cs
    title: KSA Program (decompiled)
  - id: kfs-dispatch
    resource: ksa-decomp:KSA.Kfs/KfsDispatch.cs
    title: KfsDispatch (decompiled)
  - id: host-computer
    resource: ksa-decomp:KSA.Kfs.Host/HostComputer.cs
    title: HostComputer (decompiled)
  - id: offline-harness
    resource: /modding/offline-harness.md
    title: Offline harness experiments E1–E12
generated: { by: claude-code/claude-fable-5-2, at: 2026-11-25T21:56:52Z }
status: stable
ksa_build: "2026.11.25.5949"
---

# Before you start

kFS is a prototype in this build and is **off by default**. Every `kfs` verb except `help`, `on` and
`off` answers `kfs is off (kfs on turns the prototype on)` until it is enabled.[^console-commands]
Nothing in this guide was run in the game: each step states the code path it relies on, and
anything concluded rather than read is marked `Inferred:`.

Terms: the **bench** is a simulated computer with no vehicle attached; a **target** is what Load,
Start, Stop and Reset act on (the bench or the vehicle you control). See
[Workbench](/tooling/workbench.md).

# 1. Enable the prototype

Any one of:

| how | what happens |
| --- | --- |
| start the game with the environment variable `KFS_PROTOTYPE=1` | `KfsPrototype._enabled` is initialised from it[^kfs-prototype] |
| open the game console (backslash key by default), type `kfs on`, press Enter | `KfsPrototype.Enabled = true`; prints `kfs is on`[^console-window-ex][^console-commands] |
| main menu bar: View > Debug > Show kFS Debug, then click `kFS ON` | runs `kfs on` and opens the editor[^program][^debug-window] |

`kfs help` shows the current state in its first line and lists all verbs.

# 2. Open the editor

- Console: `kfs editor` (toggle), or `kfs edit` (opens the editor on the workspace and the last file).
  [^console-commands][^workbench]
- Or it is already open if you used the `kFS ON` button.

There is no menu entry or hotkey for the editor itself.[^editor-window]

When the editor window appears while you control a vehicle, the target switches to that vehicle and
the console prints `target: vehicle <id> (Bench); Load, Start, Stop and Reset act on it`.
[^kfs-integration][^workbench] The toolbar's first combo shows the target; you can switch between
`bench` and the vehicle there at any time (or with `kfs target bench` / `kfs target vehicle`).

`Inferred:` the part name in that line is always `Bench` for a vehicle, because every vehicle computer
is created with the bench part (see [build and load flow](/tooling/build-and-load-flow.md)). Run
offline, a vehicle computer on that part did report itself as `Bench` (its load line reads
`loaded 852 bytes on Bench: …`).[^offline-harness]

# 3. Create a program

Pick one:

- **New project (recommended).** Editor menu File > New Project..., type a name (letters, digits,
  `_`, `-`), choose `kC  (main.c)`, press `CREATE`. This writes `<name>/main.c` from the kC template
  and opens it.[^editor-window][^workbench]
- **Console.** `kfs new hello.c` creates a root-level file from the same template (without an
  extension you get a `.kasm` file).[^workbench]
- **Start from an example.** File > Examples > kC > `kC_ascent_simple` copies the example into the
  workspace and opens it; `kfs examples` writes all seven menu examples.[^editor-window][^examples]

Files live under `<Documents>/My Games/Kitten Space Agency/kfs`; a subfolder is one program. See
[workspace and files](/tooling/workspace-and-files.md). All embedded examples are reproduced in
[examples](/tooling/examples.md).

The kC template is a complete program: a 10 Hz task `step` that increments `count` and computes
`wave = sinf(0.1f * (float)count)`.[^workspace] It commands nothing, so it is safe to run on a
vehicle.

# 4. Build

- Press `Ctrl+S` (left Ctrl) or File > Save: the file is written and built in the background.
  The toolbar's check-mark button builds the current text without saving.[^editor-window][^editor-model]
- Console equivalent: `kfs build <name>/main.c`.[^console-commands]

Result in the editor: the status line at the bottom reads
`compiled <name>/main.c: <n> bytes of code, <n> bytes of RAM, <ms> ms`, or `<n> error(s)`. On errors
the Diagnostics tab turns red; click a row to jump to the line, hover it for the diagnostic's manual
page.[^editor-model][^editor-window]

Useful while writing:

- the `cycles` toggle above the text shows each line's estimated cost; `bus` shows the messages a
  line reads or writes;
- the Report tab shows code and RAM usage, per-task stack and WCET against the budget;
- Reference > Services and Reference > Opcodes list what the program can call
  ([reference window](/tooling/reference-window.md));
- `kfs lines` prints the per-line costs in the console.

# 5. (Optional) Try it on the bench first

1. Set the target to `bench` (toolbar combo or `kfs target bench`).
2. Click Upload (arrow button): saves, builds for the bench and loads; console:
   `loaded onto the bench machine (Bench): Standby; kfs start boots it`.[^workbench]
3. Click Start (play button): the bench boots and runs continuously
   (`bench: running from k <k>`).[^workbench]
4. Console alternative: `kfs load <file>`, `kfs start`, then `kfs tick 10` or `kfs run`.

On the bench the sensors are all zero and nothing flies; it is for checking logic, costs and traps.
See [bench machine and fake vehicle](/host/bench-machine-and-fake-vehicle.md).

# 6. Load onto the vehicle you control

1. Take control of a vehicle in the game.
2. Make the vehicle the target: toolbar combo → the vehicle's id, or `kfs target vehicle`.
   (Opening the editor already did this if you were controlling one.)
3. Click Upload, or type `kfs load <name>/main.c --vehicle`.[^workbench][^console-commands]

Console output: the build summary, then
`queued load onto vehicle <id> (<n> thrusters, layout 0x<fingerprint>); kfs start boots it`.

What happens underneath: the program is rebuilt for that vehicle's I/O layout and the load is queued
on the vehicle's computer; it takes effect the next time the simulation dispatches that vehicle. A
rejected load is reported only in the computer's own log (editor Console tab), as
`load rejected: <rule>: <message>`.[^workbench][^host-computer]

# 7. Start it

Click the play button, or type `kfs start`. Output: `queued start on vehicle <id>`.[^workbench]

The status line then shows `vehicle <id>: Boot` / `Running` / `Idle`, the tick counter `k` and the
cycles used against the budget.[^editor-window]

`Inferred:` the simulation must be running for anything to happen. A vehicle computer only advances
inside the flight-computer update of the physics step, and only while the prototype flag is on and
the vehicle has a kFS host;[^kfs-dispatch] with the game paused, snapshot-based verbs print
`(the vehicle's computer was not dispatched, as when the simulation is paused: this is its last snapshot)`.
[^workbench] That a queued load and start take effect at the computer's next dispatch was verified
offline with the game's own `Workbench`;[^offline-harness] that nothing dispatches while the game is
paused is established in [dispatch and threading](/integration/dispatch-and-threading.md).

Whether the program actually flies the vehicle or only advises the stock flight computer depends on
what it claims and publishes; see [ownership and control modes](/host/ownership-and-control-modes.md).

# 8. Watch it

| what | in the editor | in the console |
| --- | --- | --- |
| state, tick, cycles, traps | status line | `kfs state` |
| program log (`kfs_logf`) and host notices (loaded, start, trap, overrun, rejected requests) | Console tab (last 60 lines) | last 5 lines at the end of `kfs state` |
| a global variable over time | Scope tab: pick the global, click `Watch` | `kfs watch <name>`, then `kfs trace <name>` |
| `trace(channel, v)` channels | Scope tab | `kfs trace`, `kfs trace <channel>` |
| which lines are executing | Code tab with `persistence` on (green bars) | — |
| per-function cycles | Profiler tab | `kfs prof` |
| bus messages and their fields | Bus tab; `plot` puts a field on the Scope | `kfs bus`, `kfs bus <MSG>`, `kfs msg <MSG>` |
| RAM | SRAM tab | `kfs peek <symbol> [count]` |
| host-side cost | View > Debug > Show kFS Debug | `kfs perf` |

[^editor-window][^workbench]

With the template program: open the Scope tab, choose `wave`, click `Watch` — a sine wave appears.
`kfs peek count` prints the counter.

Program log lines are limited to 10 per second of machine time; the rest are counted and summarised.
They do **not** appear in the game console by themselves (see
[console commands](/tooling/console-commands.md), "Output channels").

# 9. Debug

| symptom | where to look |
| --- | --- |
| build errors | Diagnostics tab; `kfs manual <code>` for the rule behind a code |
| the status line shows `trap <code>` | `kfs state` gives the function and `file:line`; the Console tab has the same line as `trap ... : commands held` |
| a value went NaN | NaN watch tab (turns red) or `kfs nan`; click the first-NaN line to jump to the source |
| the program is too slow (`overrun` lines in the Console tab) | Profiler tab, Report tab (WCET against budget), the `cycles` column; the toolbar's part combo (or `kfs preview <part>`) shows what the same source costs on another part |
| a service call was refused | Console tab: `rejected <service> ...` with the reason |
| what did the compiler generate | Disassembly tab (loaded image, with source lines), Listing tab (last build), `kfs disasm` |
| inspect or change memory | `kfs peek`, `kfs poke <symbol> <value>` (RAM and the command block only) |
| the editor shows stale text | the file changed on disk while you had edits: File > Revert |

[^editor-window][^workbench][^host-computer]

To change the program: edit, `Ctrl+S`, Upload, Start. Upload always rebuilds for the current
target, so nothing else is needed after switching vehicles.

# 10. Stop

| action | effect |
| --- | --- |
| stop button, or `kfs stop` | queues `Stop` on the vehicle's computer: it returns to `Standby` and `the stock flight computer resumes`[^workbench] |
| reset button, or `kfs reset` | reboots the loaded image |
| `kfs kill` | kill switch on the **controlled** vehicle: the computer halts and the game's flight computer flies; `kfs reset` restores[^console-commands] |
| `kfs off`, or `kFS OFF` in the debug window | turns the whole prototype off and hands every vehicle back; after `kfs on` each loaded image needs `kfs start` again[^console-commands] |

What survives a save, a scene change or a restart is described in
[lifecycle and persistence](/integration/lifecycle-and-persistence.md).

# Command cheat sheet

```text
kfs on                         enable the prototype
kfs editor                     show / hide the editor
kfs examples                   write the example programs into the workspace
kfs ls                         list workspace files
kfs edit proj/main.c           open a file
kfs build proj/main.c          build for the current target
kfs target vehicle             act on the vehicle you control
kfs load proj/main.c --vehicle build and load onto it
kfs start                      boot it
kfs state                      state, tasks, trap, last log lines
kfs watch wave ; kfs trace wave
kfs stop                       back to the stock flight computer
kfs kill                       emergency stop on the controlled vehicle
kfs off                        disable the prototype
```

Several commands can share one console line when separated by `;`.

# Modding notes

- A mod can perform every step above without the UI:
  `KfsConsoleCommands.Execute("on")`, `KfsIntegration.Workbench.Load("proj/main.c --vehicle")`,
  `KfsIntegration.Workbench.Start()`, then read `KfsIntegration.Workbench.Target.Console` for the
  program's log. Details and caveats (queued operations, main thread, flag) are in
  [Workbench](/tooling/workbench.md) and [console commands](/tooling/console-commands.md).
- A mod's `mod.toml` can carry `[console] onBoot` / `onLoad` command lines; `kfs on` works in both
  (see [console commands](/tooling/console-commands.md), "The prototype flag from the console").
- For a mod that adds services (for example part lights), the player-visible workflow stays the same:
  the new calls must compile (headers and service tables), appear under Reference > Services, and
  report refusals through the same Console tab.

[^kfs-prototype]: KfsPrototype.cs:8.
[^console-commands]: KfsConsoleCommands.cs — verb table :15-173; flag check :234-242; `SetFlag` :244-258; `Help` :260-269; `Kill` :271-290.
[^kfs-integration]: KfsIntegration.cs — `ShowEditor` / `ToggleEditor` call `TargetActiveVehicle` :183-214.
[^debug-window]: KfsDebugWindow.cs — `DrawEnable` :112-121; `kFS OFF` :125-129.
[^editor-window]: KfsEditorWindow.cs — menus :943-1006; shortcuts :1069-1082; toolbar :1182-1315; status line :1324-1370; editor toggles :1570-1622; Diagnostics :2114-2154; Report :2156-2342; Profiler :2396-2568; Scope :2570-2709; NaN watch :2711-2748; Disassembly :2750-2828; Console :2864-2895.
[^workbench]: Workbench.cs — `Edit` :420-451; `New` :453-476; `Load` :739-807; `LoadOntoVehicle` :809-822; `LoadOntoBench` :824-835; `Upload` :856-878; `SelectTarget` :944-971; `Start` :973-989; `Stop` :1004-1020; `Play` :1022-1041; `TargetActiveVehicle` :1066-1073; snapshot-wait message :1193-1196; `State` :1223-1351; `NewProject` :2263-2294.
[^workspace]: Workspace.cs — `NEW_KC_TEMPLATE` :23.
[^editor-model]: EditorModel.cs — `Save` :344-365; `Assemble` :406-427; status texts in `Apply` :471-476.
[^examples]: Examples.cs — `Menu` :61-79; `Write` :97-123.
[^console-window-ex]: ConsoleWindowEx.cs:8-12.
[^program]: Program.cs — View > Debug > "Show kFS Debug" :4096, :4305, :4423.
[^kfs-dispatch]: KfsDispatch.cs:7-28 (returns false unless `KfsPrototype.Enabled` and `vehicle.KfsHost != null`); called from PhysicsBubble.cs:2081.
[^offline-harness]: Experiment E11 and the output of the complete spike program in the offline harness document (a `FakeVehicle` stood in for the vehicle; nothing was run in the game).
[^host-computer]: HostComputer.cs — load log lines :312-341; trap line :454; overrun lines :551-555; rejected-request lines :683, :743.
