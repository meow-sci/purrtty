---
type: API Reference
title: kFS console commands
description: Every verb of the in-game `kfs` console command — exact syntax, arguments, effect and output text — plus how the game console parses it, where the output goes, and how a mod can call, capture or extend it.
resource: ksa-decomp:KSA.Kfs/KfsConsoleCommands.cs
tags: [kfs, tooling, console, commands, terminal, extension]
sources:
  - id: console-commands
    resource: ksa-decomp:KSA.Kfs/KfsConsoleCommands.cs
    title: KfsConsoleCommands (decompiled)
  - id: kfs-integration
    resource: ksa-decomp:KSA.Kfs/KfsIntegration.cs
    title: KfsIntegration (decompiled)
  - id: workbench
    resource: ksa-decomp:KSA.Kfs.Host.Workbench/Workbench.cs
    title: Workbench (decompiled)
  - id: terminal-interface
    resource: ksa-decomp:Brutal.ImGuiApi.Abstractions/TerminalInterface.cs
    title: TerminalInterface (decompiled)
  - id: terminal-parser
    resource: ksa-decomp:Brutal.ImGuiApi.Abstractions/Parser.cs
    title: Terminal Parser (decompiled)
  - id: terminal-command
    resource: ksa-decomp:Brutal.ImGuiApi.Abstractions/Command.cs
    title: Terminal Command (decompiled)
  - id: console-window
    resource: ksa-decomp:Brutal.ImGuiApi.Abstractions/ConsoleWindow.cs
    title: ConsoleWindow (decompiled)
  - id: console-window-ex
    resource: ksa-decomp:KSA/ConsoleWindowEx.cs
    title: ConsoleWindowEx (decompiled)
  - id: program
    resource: ksa-decomp:KSA/Program.cs
    title: KSA Program (decompiled)
  - id: universe
    resource: ksa-decomp:KSA/Universe.cs
    title: KSA Universe (decompiled)
  - id: kfs-prototype
    resource: ksa-decomp:KSA.Kfs/KfsPrototype.cs
    title: KfsPrototype (decompiled)
  - id: console-log
    resource: ksa-decomp:KSA.Kfs.Host.Services/ConsoleLog.cs
    title: ConsoleLog (decompiled)
  - id: trace-store
    resource: ksa-decomp:KSA.Kfs.Host.Services/TraceStore.cs
    title: TraceStore (decompiled)
  - id: host-computer
    resource: ksa-decomp:KSA.Kfs.Host/HostComputer.cs
    title: HostComputer (decompiled)
  - id: kfs-vehicle-host
    resource: ksa-decomp:KSA.Kfs/KfsVehicleHost.cs
    title: KfsVehicleHost (decompiled)
  - id: diagnostic-manual
    resource: ksa-decomp:KSA.Kfs.Host.Views/DiagnosticManual.cs
    title: DiagnosticManual (decompiled)
  - id: purrtty-console-shell
    resource: ../../../purrTTY.CustomShells/GameConsoleShell.cs
    title: purrTTY GameConsoleShell
  - id: purrtty-print-patch
    resource: ../../../purrTTY.GameMod/Patches/ConsoleWindowPrintPatch.cs
    title: purrTTY ConsoleWindowPrintPatch
generated: { by: claude-code/claude-fable-5-2, at: 2026-11-26T06:11:10Z }
status: stable
ksa_build: "2026.11.25.5949"
---

# One command, 36 verbs

kFS registers exactly **one** command with the game console: `kfs`. Everything else is a *verb*
dispatched inside `KfsConsoleCommands.Execute`.[^console-commands]

```csharp
[TerminalAction("kfs", "kFS flight computer: `kfs help` lists the commands, `kfs on|off` the prototype flag", ArgParseMode.Unparsed)]
public static void Execute(string args = "")
```

## Registration

`Program`'s constructor calls `KfsIntegration.RegisterCommands(this)` as the last step of its
"Commands" loading task.[^program] That method (guarded so it runs once):[^kfs-integration]

1. sets `KfsVehicleHost.FrameBudgetMs = 4.0`;
2. calls `KfsConsoleCommands.Register(Program.TerminalInterface, program)`, i.e.
   `terminal.RegisterCommand(<delegate for Execute>, owner)`;
3. registers two static windows: the per-frame hook that calls `Workbench.Frame`, and
   [KfsDebugWindow](/tooling/debug-window.md).

`KfsIntegration.RegisterCommands(TerminalInterface terminal, object owner)` is a second public
overload that only does step 2 on a terminal of the caller's choice.[^kfs-integration]

## How the game console parses a line

The console is `Brutal.ImGuiApi.Abstractions.ConsoleWindow`; its `Terminal` field is the
`TerminalInterface` (`Program.TerminalInterface => ConsoleWindow.Terminal`).[^console-window][^program]

1. The console is toggled with the backslash key (the default of the static field
   `ConsoleWindowEx.ToggleKey`, `GlfwKey.Backslash`; `GameSettings.ApplyTo(ConsoleWindow)` overwrites
   it from the player's console-toggle binding) and a line is submitted with Enter; Up / Down walk
   the history.[^console-window-ex]
2. `TerminalInterface.Execute(string)` splits the line into commands at every `;` that is **not inside
   double quotes**, trimming each piece.[^terminal-interface][^terminal-parser]
3. For each piece, the first whitespace-delimited token, lower-cased, is the command name; the rest is
   the argument string.[^terminal-parser]
4. `kfs` is registered with `ArgParseMode.Unparsed`, so the whole remainder is passed as the single
   `string args` (empty string when there is none).[^terminal-parser][^terminal-command]
5. An unknown command name logs `unknown command <name>, use 'help' for a list of commands`.
   [^terminal-interface]

Consequences: `kfs load a.c --vehicle; kfs start` runs two commands; a `;` in a path must be inside
double quotes; `KFS HELP` works (the command name is lower-cased by the terminal and verbs are
compared case-insensitively by kFS).

## Verb dispatch

`Execute` trims `args`; an empty string runs `help`. The verb is the text up to the first space, the
verb's argument is the rest with leading whitespace removed.[^console-commands]

- The verb name is matched with `StringComparison.OrdinalIgnoreCase` against the private table
  `_verbs` (`Verb(string Name, string Help, Action<string> Run, bool NeedsFlag = true)`).
- When the verb has `NeedsFlag` and `KfsPrototype.Enabled` is false, nothing runs and the error
  `kfs is off (kfs on turns the prototype on)` is reported. Only `help`, `on` and `off` have
  `NeedsFlag: false`.
- An `IOException` from the verb is reported as `kfs <verb>: <message>`; any other exception as
  `kfs <verb> failed, which is a bug in kFS and not in your program: <exception>`.
- An unknown verb is reported as `unknown kfs command '<x>', available: <all verb names, comma-separated>`.

`KfsConsoleCommands.Verbs` (public) enumerates the 36 names in table order.[^console-commands]

# The prototype flag from the console

| command | effect | output |
| --- | --- | --- |
| `kfs on` | `KfsPrototype.Enabled = true` | `kfs is on` |
| `kfs off` (when on) | `KfsPrototype.Enabled = false`, then `KfsVehicleHost.ReleaseDead()` and `KfsVehicleHost.HandBackAtFlagOff()` | `kfs is off: every vehicle is back on the stock flight computer; after kfs on, kfs start boots a loaded image again` |
| `kfs off` (when already off) | sets the flag | `kfs is off` |
| `kfs help` | — | first line `kfs is on\|off; commands (kfs <command> [arguments]):` |

[^console-commands] The flag starts as `Environment.GetEnvironmentVariable("KFS_PROTOTYPE") == "1"`.
[^kfs-prototype] See [feature flag](/integration/feature-flag.md).

Running `kfs on` automatically at start-up:

- **Mod `[console] onBoot` lines** are executed by `TerminalInterface.Execute(ModLibrary.OnBoot)` at
  Program.cs:1058, after the kFS command was registered at :978, so `kfs on` works there.[^program]
- **Mod and settings `onLoad` lines** run after a universe is loaded (Universe.cs:2696-2697); `kfs on`
  works there too.[^universe]
- **The game settings' `Console.OnBoot` lines** run at Program.cs:867, *before* the commands are
  registered; `kfs on` there is an unknown command.[^program]

# Verb table

`flag` = requires the prototype flag. `snap` = wrapped in `Workbench.WithSnapshot`, so on a vehicle
target the output may arrive on a later frame (see [Workbench](/tooling/workbench.md)). Help texts are
verbatim. Line numbers are in KfsConsoleCommands.cs.[^console-commands]

| # | verb | arguments | flag | snap | help text | calls | lines |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | `help` | — | no | | this table | `Help()` | 17-20 |
| 2 | `on` | — | no | | turn the kFS prototype on (every other verb needs it; KFS_PROTOTYPE=1 turns it on at start) | `SetFlag(true)` | 21-24 |
| 3 | `off` | — | no | | turn the kFS prototype off: every computer stops and the game runs as if kFS were not there | `SetFlag(false)` | 25-28 |
| 4 | `ws` | `[path]` | yes | | show or set the workspace folder | `Workbench.Ws(a)` | 29-32 |
| 5 | `ls` | — | yes | | list the workspace's .c, .h, .kasm and .kinc files (subfolders are projects) | `Workbench.Ls()` | 33-36 |
| 6 | `edit` | `[file]` | yes | | open a file in the editor window; with no file, open the editor on the workspace and the last file | `Workbench.Edit(a)` | 37-40 |
| 7 | `new` | `<file>` | yes | | create a file in the workspace and open it | `Workbench.New(a)` | 41-44 |
| 8 | `editor` | — | yes | | show or hide the editor window | `KfsIntegration.ToggleEditor()` | 45-48 |
| 9 | `build` | `[file]` | yes | | build a file (kC compiled, kASM assembled, by extension): diagnostics, sizes, stack depths and the compile report | `Workbench.Build(a)` | 49-52 |
| 10 | `report` | `[file]` | yes | yes | the compile report of the last build [file], with measured maxima once it runs | `Workbench.Report(a)` | 53-59 |
| 11 | `lines` | `[file]` | yes | | per-line cycle estimates of a file (/it per iteration, [n] kASM stack depth) | `Workbench.Lines(a)` | 60-63 |
| 12 | `preview` | `[part name \| off]` | yes | | recompile the open file against another part [part name \| off]; nothing is loaded | `Workbench.Preview(a)` | 64-67 |
| 13 | `def` | `<name>` | yes | | go to the definition of a function or global of the last build | `Workbench.Def(a)` | 68-71 |
| 14 | `manual` | `<code>` | yes | | a diagnostic code's manual page | `Workbench.Manual(a)` | 72-75 |
| 15 | `target` | `[bench \| vehicle]` | yes | | show or set what load, start, stop and reset act on [bench \| vehicle] | `Workbench.SelectTarget(a)` | 76-79 |
| 16 | `load` | `[file] [--part name] [--vehicle]` | yes | | build and load onto the bench machine [--part name] or the active vehicle [--vehicle] | `Workbench.Load(a)` | 80-83 |
| 17 | `start` | — | yes | | boot the loaded image | `Workbench.Start()` | 84-87 |
| 18 | `stop` | — | yes | | stop a bench run, or detach a vehicle computer | `Workbench.Stop()` | 88-91 |
| 19 | `reset` | — | yes | | reset the computer (reboots the active image) | `Workbench.ResetTarget()` | 92-95 |
| 20 | `tick` | `[n]` | yes | | run n ticks now on the bench machine | `Workbench.Tick(a)` | 96-99 |
| 21 | `run` | `[seconds]` | yes | | run the bench machine continuously in game time [seconds] | `Workbench.Run(a)` | 100-103 |
| 22 | `state` | — | yes | yes | system block, task cycles and budgets, overrun and trap counters | `Workbench.State()` | 104-110 |
| 23 | `peek` | `<symbol\|address> [count]` | yes | | read RAM or the command block by symbol or address [count] | `Workbench.Peek(a)` | 111-114 |
| 24 | `poke` | `<symbol\|address> <value>` | yes | | write RAM or the command block by symbol or address | `Workbench.Poke(a)` | 115-118 |
| 25 | `disasm` | `[function] [--source] [--copy file.kasm]` | yes | | the loaded image as kASM; [function] or --source shows source lines; [function --copy file.kasm] starts a kASM file | `Workbench.DisasmView(a)` | 119-122 |
| 26 | `prof` | — | yes | yes | per-function cycles from the profiler | `Workbench.Prof()` | 123-129 |
| 27 | `perf` | — | yes | yes | what the target costs the host: VM and host time per tick, dispatch, allocations, footprint | `Workbench.Perf()` | 130-136 |
| 28 | `trace` | `[channel]` | yes | | list trace channels, or print a channel's last samples | `Workbench.Trace(a)` | 137-140 |
| 29 | `watch` | `[name \| -name]` | yes | | list globals, watch one on the oscilloscope [name], or stop [-name] | `Workbench.WatchCommand(a)` | 141-144 |
| 30 | `nan` | — | yes | yes | the NaN watch: first NaN (line, tick) and the globals that went NaN | `Workbench.NanCommand()` | 145-151 |
| 31 | `bus` | `[MSG_NAME \| plot F \| unplot F \| plots]` | yes | yes | the bus monitor (publisher, seq, age, rate per message); [MSG_NAME] its field values; [plot\|unplot MSG_NAME.field] on the oscilloscope; [plots] | `Workbench.BusCommand(a)` | 152-158 |
| 32 | `msg` | `<MSG_NAME \| NAME \| 0x0101>` | yes | | a message's definition (catalogue entry, offsets in the target's layout) | `Workbench.Msg(a)` | 159-162 |
| 33 | `headers` | `[file]` | yes | | write vehicle.h and vehicle.kinc for the current target into the project folder of [file] (build does too) | `Workbench.Headers(a)` | 163-166 |
| 34 | `examples` | `[--force] [--all]` | yes | | write the example programs into the workspace [--force] [--all with the archived ones] | `Workbench.ExamplesCommand(a)` | 167-170 |
| 35 | `kill` | — | yes | | the kill switch: halt the active vehicle's computer and hand the vehicle to the C# flight computer | `Kill(args)` | 171, 271-290 |
| 36 | `budget` | `[ms \| off]` | yes | | show or set the host time a vehicle's computer may use per frame before its ticks are skipped [ms \| off] | `Budget(args)` | 172, 292-327 |

`help` prints each verb as two spaces, the name left-aligned in 9 columns, a space and the help text.
[^console-commands]

Operations that exist on `Workbench` but have **no verb**: `Upload`, `Play`, `Pause`, `StopTarget`,
`NewFile`, `NewProject`, `DeleteFile`, `CopyExample`, `Disasm(function)`, `TargetActiveVehicle`. They
are reachable only from the [editor window](/tooling/editor-window.md) or from code.[^workbench]

# Verb details

`<target>` below is `bench` or `vehicle <id>`. File arguments are workspace-relative paths with `/`.
Several verbs default their file to "the last file" (`_lastFile`: the last one named to `edit`,
`build` or a load) or to the file open in the editor. All texts are from Workbench.cs unless noted.
[^workbench]

## Workspace verbs

### `kfs ws [path]`

With a path (surrounding double quotes are trimmed): switch the workspace to it and create it. Always
prints `workspace: <absolute folder>`.

### `kfs ls`

One line per file: `<path>  (<n> bytes)`. Root files first, then `folder/file` for each direct
subfolder. Empty workspace:
`no .kasm, .kinc, .c or .h files in <folder> (kfs examples writes the examples)`.

### `kfs edit [file]`

| case | output | side effect |
| --- | --- | --- |
| file given, exists, opens | `editing <file>` | editor model opens it; `OpenRequested(file)` shows the editor window |
| file given, missing | `<file>: not found in <folder> (kfs new creates it)` | — |
| file given, too large | the size message | — |
| no file, a previous file still exists | as "opens" for that file | |
| no file, nothing to reopen | `workspace: <folder> (<n> files; pick one in the editor's file list, or kfs new)` | `OpenRequested("")` shows the editor window |

### `kfs new <file>`

No extension → `.kasm` is appended. Existing file: `<file> exists; opening it`. Otherwise the file is
created from the template for its extension, `created <file>` is printed and it is opened. No
argument: `usage: kfs new <file>`. Templates:
[workspace and files](/tooling/workspace-and-files.md).

### `kfs editor`

Toggles the editor window (creating it on first use). When it becomes visible and the target is the
bench while a vehicle is controlled, the target switches to that vehicle and the `target:` line is
printed.[^kfs-integration]

### `kfs headers [file]`

Writes `vehicle.h` and `vehicle.kinc` for the current target into the project folder of `file`
(default: last file, else the open file; a root-level or absent file means the workspace root).

- `wrote vehicle.h and vehicle.kinc for <target> (layout 0x<fingerprint>) in <folder|the workspace>`
  (only the files that changed are named), or
- `vehicle.h and vehicle.kinc in <...> are current for <target> (layout 0x<fingerprint>)`, or
- `couldn't write the generated headers: <message>`.

### `kfs examples [--force] [--all]`

Writes the embedded examples. Without flags, the seven menu examples, skipping files that exist;
`--force` overwrites; `--all` adds the seven archived ones. The flags are detected by substring, so
their order and position do not matter. Output: `wrote <path>, <path>, ...` or
`the examples are already in the workspace (kfs examples --force rewrites them)`. See
[examples](/tooling/examples.md).

## Build verbs

### `kfs build [file]`

Builds for the current target (see [build and load flow](/tooling/build-and-load-flow.md)). Prints,
in order: an optional header-refresh line, one line per diagnostic
(`<file>:<line>:<col>: error|warning <code>: <message>`, then `  hint: <hint>`), and either
`<file>: <n> error(s), <ms> ms` or the size line, one line per task, one per function and the compile
report prefixed with `  | `. Errors: `usage: kfs build <file> (or kfs edit a file first)`,
`<file>: not found in <folder>`.

### `kfs report [file]`

No argument: the report of the current build (the editor's, else the last console build). With a
file: rebuilds it quietly first. Output: the lines of `ReportView.Lines()`
(see [compile report](/compiler/compile-report.md) and [views](/host/views.md)), with measured
maxima when that build is the loaded image. Otherwise `nothing built yet (kfs build <file>)` or
`the last build failed; no report`.

### `kfs lines [file]`

Quietly rebuilds the file (default: the open file, else the last file) and prints a header

```text
<file> on <part>: cycles per line (/it per iteration, [n] operand stack after a kASM line); bus markers (< reads, > writes; kfs msg NAME shows a message)
```

followed by one row for every source line that has a cost or a bus marker: line number (width 5),
cost cell (width 14), bus-marker cell (width 24), the source text, and `   ; <soft routines>` when
the line calls soft routines. Errors: `usage: kfs lines <file>`,
`<file>: the build failed (kfs build <file> shows why)`.

### `kfs preview [part name | off]`

| argument | output |
| --- | --- |
| none | `preview: <part or off>; parts: <all part names>` |
| `off` | `preview off: the editor shows the target's part again` |
| a part (case-insensitive, spaces optional) | `preview on <part> (<core>): <n> bytes of code, <n> bytes of RAM; nothing loaded (kfs preview off returns to <target part>)`, or `preview on <part>: the program doesn't build for it` plus up to five errors |
| unknown part | `unknown part '<x>'; parts: <names>` |
| no file open | `open a file first (kfs edit)` |

The part names in this build's catalogue are `Sequencer`, `Flight Computer Mk1`,
`Flight Computer Mk2`, `Flight Computer Mk2-RH`, `Flight Computer Mk3` (see
[catalogue processors](/tables/catalogue-processors.md)). `preview` matches the **full name** only
(the whole rest of the line is the argument, so `kfs preview Flight Computer Mk3` and
`kfs preview flightcomputermk3` both work); the short names that `kfs load --part` accepts
(`Mk3`) are not recognised here.

### `kfs def <function or global>`

Looks the name up in the IR module of the current build. Found:
`<kind> <name>: <file>:<line>`, and the file is opened at that line unless the file name starts with
`<` (a built-in or library source). Otherwise `nothing built yet (kfs build <file>)` or
`'<name>' is not defined in the last build`; no argument: `usage: kfs def <function or global>`.

### `kfs manual <code>`

Prints the manual page of a diagnostic code such as `KA-E550`.[^diagnostic-manual]

```text
<code>: <rule text>
severity <severity>[ (default <default>)]; range <range>; languages <a, b>
hint: <hint of the matching diagnostic in the editor or last build, if any>
```

The page is looked up by the digits after the dash and the severity letter (`KA-E550` → `550`) in
`diagnostics.json`; an unknown code prints `<code>: not in the diagnostics registry`. No argument:
`usage: kfs manual <code>   (for example KA-E550)`.

## Target, load and run verbs

### `kfs target [bench | vehicle]`

Sets the target and prints `target: <target> (<part name>); Load, Start, Stop and Reset act on it`.
With no argument only prints. Errors: `usage: kfs target [bench | vehicle]`,
`no active vehicle: take control of one first`.

### `kfs load [file] [--part <name>] [--vehicle]`

Builds (printing as `kfs build`) and loads.

- Without `--vehicle`: onto the bench, replacing the bench machine when `--part` names another part.
  `loaded onto the bench machine (<part>): Standby; kfs start boots it` or
  `bench load rejected: <rule>: <message>`.
- With `--vehicle`: onto the controlled vehicle's computer.
  `queued load onto vehicle <id> (<n> thrusters, layout 0x<fingerprint>); kfs start boots it`. The
  load itself happens when that computer is next dispatched; a rejection appears only in the
  computer's console log.
- Errors: `usage: kfs load <file> [--part <name>] [--vehicle]`,
  `unknown part '<x>'; parts: <names>`, `no active vehicle`, plus those of `build`.

`--part` accepts a full name, a short name (`Mk1`, `Mk2`, `Mk2-RH`, `Mk3`, `Sequencer`) or `Bench`,
but **only as a single token**: the argument string is split on spaces, so `--part Flight Computer Mk2`
does not work — use `--part Mk2`. File names containing spaces cannot be passed either.

### `kfs start`

Bench: `nothing loaded (kfs load first)` or `bench: <state>` after booting; no ticks run until
`kfs tick` / `kfs run`. Vehicle: `queued start on vehicle <id>`; if nothing is loaded the line
`vehicle <id>: nothing loaded (kfs load <file> --vehicle first)` follows on a later frame.

### `kfs stop`

Bench: ends a continuous run, `bench: run stopped at k <k>`. Vehicle:
`queued stop on vehicle <id>: the stock flight computer resumes`.

### `kfs reset`

Bench: `nothing loaded (kfs load first)` or `bench: reset, <state>`. Vehicle:
`queued reset on vehicle <id>`.

### `kfs tick [n]`

Bench only. `n` is a positive decimal integer (default 1).

```text
bench: ran <n> tick(s), k <k>, <state>, last tick <cycles> cycles[, trap <code>]
bench: stopped after <ran> of <n> ticks, which took <ms> ms; kfs run runs the bench in game time
bench: host fault, which is a bug in kFS and not in your program; the bench is halted: <exception>
```

The second line appears when the wall-clock limit (`TickHostMs`, 250 ms in the game) cut the run
short. Errors: `usage: kfs tick [n]`, `bench: <state>; kfs start first`,
`a vehicle computer ticks on its own grid once started; use the bench for kfs tick`.

### `kfs run [seconds]`

Bench only. `bench: running <s> s of machine time in game time` or
`bench: running until kfs stop`. Errors: `usage: kfs run [seconds]`,
`bench: <state>; kfs start first`, `a vehicle computer runs in simulation time once started`.

### `kfs kill`

Acts on the vehicle the player **controls**, not on the workbench target.[^console-commands]
Enqueues `((VehicleComputer)c).KillSwitch()` and prints
`kfs kill: the computer halted, the C# flight computer flies the vehicle (applied at the next tick; kfs reset restores)`.
Errors: `kfs kill: no active vehicle`,
`kfs kill: the active vehicle has no computer (kfs load <file> --vehicle puts one on it)`.

### `kfs budget [ms | off]`

Shows or sets the static `KfsVehicleHost.FrameBudgetMs`, which applies to **every** vehicle
computer.[^console-commands][^kfs-vehicle-host] `off` sets 0; a number must be above 0 and at most
1000 (invariant culture). Default 4.0.

```text
kfs budget: <ms> ms of host time per vehicle computer per frame; ticks past it are skipped
kfs budget: off, every due tick runs however long it takes
usage: kfs budget [ms | off], with ms above 0 and at most 1000
```

## Inspection verbs

### `kfs state`

```text
<target>: <state>, part <part>, tick_hz <hz>, k <k>, t <seconds, 0.000> s, layout 0x<fingerprint>
  last tick: <cycles> / <budget> cycles, <n> bytecodes[, boot tick](, latched|, no latch)
  ticks <run> (latched <n>), overruns <n>, pulses sent <n>, stream 0x<hash>, log lines suppressed <n>
  <kind> <task>[ /<divisor>]: <n> cycles this tick, <n> total; RAM stack <n> B, return depth <n>, operands <n>; <status>
  trap <code> at 0x<pc> in task <n>, function <name>[, <file>:<line>][, code <user code>]
  latched: <hex of the latched command block>
  [k <k> t <seconds, 0.00>] <one of the last 5 console log lines>
```

One task line per task; the trap and latched lines only when present. `<state>` is one of
`NoSoftware`, `Standby`, `Boot`, `Running`, `Idle`, `Halted`.

### `kfs peek <symbol|address> [count]`

Reads `count` (1 to 64, default 1) 32-bit words. An address is decimal or `0x` hex; a symbol is a
global of the loaded image, a constant of the built-in kASM includes, or a command or sensor field
name of the layout (a vehicle layout has no sensor fields); `+offset` may follow. One line per word:

```text
<name>+<byte offset> (0x<address>): 0x<hex>  i32 <signed>  f32 <float>
```

On a vehicle the read is queued: `queued peek on vehicle <id>`, the words follow on a later frame.
Errors: `usage: kfs peek <symbol|address> [count]`, `count must be 1 to 64`,
`unknown symbol '<x>'`, `bad offset in '<x>'`, `no such region`, `0x<address>: outside the region`.

Address regions: RAM below `0x80000000`; sensor block from `0x80000000` (only the bench layout has
sensor fields, see [I/O layout generation](/host/io-layout-generation.md); a vehicle layout's sensor
size is 0, so on a vehicle target a read there is `outside the region`);
command block from `0x90000000`; system block from `0xA0000000` (see
[memory map and system block](/vm/memory-map-and-sysblock.md)).

### `kfs poke <symbol|address> <value>`

Value forms: `42` or `0x2A` (4 bytes), `1.5f` (float32), `1.5d` (float64, 8 bytes), or a bare number
containing `.` (float32). Only RAM and the command block are writable.

```text
wrote <n> bytes at <symbol> (0x<address>)
0x<address>: only RAM and the command block can be written
0x<address>: outside the region
```

Vehicle: `queued poke on vehicle <id>` first. Errors:
`usage: kfs poke <symbol|address> <value>   (value: 42, 0x2A, 1.5f, 1.5d)`, `bad value '<v>'`,
`<target>: nothing loaded`.

### `kfs disasm [function] [--source] [--copy file.kasm]`

| form | output |
| --- | --- |
| `kfs disasm` | the whole loaded image as kASM (`Disassembler.Disassemble`) |
| `kfs disasm --source` | every function with interleaved source lines (`DisassemblyView.Format`) |
| `kfs disasm <function>` | that function with source lines |
| `kfs disasm <function> --copy <file>` | writes the function as a new kASM file (default extension `.kasm`), prints `wrote <file>: '<function>' as kASM; add its .proto before .func (the file's header says how)` and opens it |

Errors: `<target>: nothing loaded`, `no function '<f>'; functions: <names>`, `no function '<f>'`,
`usage: kfs disasm <function> --copy <file.kasm>`.

### `kfs prof`

`<target>: no profile yet`, or `<target>: profiler (kVM §8.4)` followed by the profiler table lines
(see [profiler and exec trace](/vm/profiler-and-exec-trace.md)).

### `kfs perf`

`<target>: <state>`, then the indented lines of `PerfReport.Lines(snapshot)` and, when the editor
has built something, `  assemble <file>: <ms> ms`. See [snapshots and perf](/host/snapshots-and-perf.md).

### `kfs trace [channel]`

No argument: `<target>: channels a, b, ...` (or `<target>: no trace channels yet`), plus
`<target>: watched globals ...` and `<target>: plotted message fields ...` when there are any. With a
name (a trace channel, a watched global or a plotted message field):

```text
<channel>: <n> samples, last <min(n, 10)>:
  k <k>: <value>
```

or `no samples on '<channel>'`.

### `kfs watch [name | -name]`

| argument | output |
| --- | --- |
| none | `<target>: watched none\|<names>` and `  globals: <first 40 global leaf paths>[ … (<count>)]` |
| `name` | `watching <name> on <target>: sampled after every tick (kfs trace <name> prints the last samples)` or `no numeric global '<name>' in the loaded image (kfs watch lists them)` |
| `-name` | `stopped watching <name>` or `<name> wasn't watched` |

Nothing loaded: `<target>: nothing loaded`.

### `kfs nan`

The lines of `NanView.Lines(snapshot)`, each prefixed `<target>: `.

### `kfs bus [...]`

| form | output |
| --- | --- |
| `kfs bus` | the bus monitor table (`BusMonitorView.Lines(snapshot)`) |
| `kfs bus MSG_NAME` (the `MSG_` prefix is optional, case-insensitive) | `<MSG> after tick <k>: seq <n>, age <age>, rate <rate>` and one line per field: `  <path, width 32> <value>`; or `<MSG> is not on the bus of <target>; on it: <names>`; or `<MSG>: the values arrive with the target's next tick; run kfs bus <MSG> again` |
| `kfs bus plot MSG_NAME.field` | `plotting <f> on the oscilloscope: one sample per tick (<n>/16)` or `can't plot <f>: <reason>` |
| `kfs bus unplot MSG_NAME.field` | `stopped plotting <f>` or `<f> isn't plotted` |
| `kfs bus plots` | `plotted: <fields>` or `no message fields plotted (kfs bus plot MSG_NAME.field)` |

Any sub-form with nothing loaded: `nothing loaded on the target, so nothing is on its bus`. See
[message bus](/host/message-bus.md).

### `kfs msg <MSG_NAME | NAME | 0x0101>`

Prints the message's definition lines from the catalogue with offsets in the target's layout
(`BusMonitorView.Definition`). No argument or unknown name: a usage or
`no message '<x>' in the catalogue` line that also lists every message name. See
[messages table](/tables/messages-table.md).

# Output channels

| channel | what goes there | how to read it |
| --- | --- | --- |
| `KfsIntegration.Output` (`public static TextWriter { get; set; }`) | every line a `kfs` verb or the workbench prints, via `KfsIntegration.Print` and `Workbench.Out` | default: a private `ConsoleWindowWriter` that calls `Program.ConsoleWindow.Print(line)` (or logs at Info level when there is no console window)[^kfs-integration] |
| `KfsIntegration.Error(line)` | verb errors. Its only callers are in `KfsConsoleCommands`: a verb's `IOException`, any other exception of a verb, an unknown verb, `kfs is off …`, and the argument errors of `kill` and `budget`[^console-commands-errors] | always logged with `DefaultCategory.Log.Error` (which the console window displays as a log provider[^console-window][^program]); **also** written to `Output` when `Output` is not the default writer[^kfs-integration] |
| `HostComputer.Console` (`ConsoleLog`) | what the *program* and the host say: `kfs_logf` / `SVC_LOG` lines, load / start / stop / trap / overrun notices, rejected service requests, editor build status lines | `Snapshot()`, `CopyTail(count, into)`, `Version`; shown in the editor's Console tab (last 60 lines) and by `kfs state` (last 5)[^console-log][^host-computer] |
| `HostComputer.Traces` (`TraceStore`) | `trace(channel, v)` / `SVC_TRACE` samples | `Channels`, `Samples(name)`, `CopyLast`; `kfs trace`, Scope tab[^trace-store] |
| `HostComputer.Watches`, `HostComputer.Bus.Samples` | watched globals, plotted message fields | same API |
| `TerminalInterface.OnOutput` (event) | terminal-level parse errors and the end of the terminal's own `help` | not used by kFS[^terminal-interface] |

Limits of `ConsoleLog`: program log lines are limited to 10 per machine second and host events to
20; the excess is counted and summarised as
`(<n> log lines suppressed: more than 10 per second)`; the buffer holds 4096 lines and drops the
oldest 1024 when full; each line is `LogLine(ulong K, double TimeS, string Text)`. All members lock
an internal gate, so reading from another thread is safe. There is no "line appended" event: poll
`Version`.[^console-log]

**Program output never reaches `KfsIntegration.Output` or the game console on its own.** A `kfs_logf`
line is visible only in the editor's Console tab, in `kfs state`, or to code that reads the
`ConsoleLog`.

Output timing: most verbs print synchronously inside `Execute`. These print **later, from
`Workbench.Frame`**: results of queued vehicle operations (`peek`, `poke`, a failed `start`), the
`snap` verbs when the vehicle snapshot was stale, bench host faults, and the completion of an editor
Upload.[^workbench]

# Modding notes

- **Calling verbs from code.** `KfsConsoleCommands.Execute("load proj/main.c --vehicle")` is
  `public static` and is how the debug window toggles the flag; it applies the flag check and the
  exception handling. `Program.TerminalInterface.Execute("kfs ...")` goes through the terminal
  parser (so `;` splits). Calling `KfsIntegration.Workbench.<Method>` directly skips both.
  [^console-commands][^kfs-integration]
- **Composing a verb line from a file name.** `Execute` cuts the verb at the **first** space and
  hands the rest, trimmed at the start, to the verb as one string; what happens to spaces after
  that is the verb's business. `build` passes the whole rest to `Workbench.Build`, which only trims
  it, so `kfs build my folder/main.c` builds that file — and quotes would become part of the name.
  `load` passes it to `Workbench.Load`, which splits it at **every** space into `--part <name>`,
  `--vehicle` and "the file" (the last other token wins), so a path that contains a space cannot be
  loaded with `kfs load` at all. A tool that forwards file names should refuse such a path for
  `load` rather than send a line that loads something else or nothing.[^console-commands-split][^workbench-args]
- **purrtty's Game Console shell already runs `kfs`.** It executes the typed line with
  `Program.TerminalInterface.Execute(commandLine)` and captures
  `ConsoleWindow.Print(ReadOnlySpan<char>, ImColor8, int)` through a Harmony postfix, but only while
  that `Execute` call is on the stack.[^purrtty-console-shell][^purrtty-print-patch] `Inferred:` the
  synchronous output of every verb therefore already appears in a purrtty terminal, while the
  deferred lines listed under "Output timing" and all program output (`kfs_logf`) do not.
- **Capturing everything kFS prints.** Assign `KfsIntegration.Output` a `TextWriter` that forwards to
  the previous writer and to the mod's sink; implement `WriteLine(string)` and `Write(string)`. The
  workbench adopts it at its next `KfsIntegration.Workbench` read. `Inferred:` because `Error` writes
  to a non-default `Output` in addition to logging, a naive tee shows each error twice in the game
  console (once from the log provider, once from the tee); the tee should either not forward error
  lines or the mod should patch `KfsIntegration.Error`. The default writer type is a `private sealed`
  nested class, so it cannot be subclassed, only wrapped.[^kfs-integration]
- **Capturing program output.** Poll `KfsIntegration.Workbench.Target.Console.Version` each frame and
  call `CopyTail`; for a specific vehicle use `KfsVehicleHost.Find(vehicle)?.Computer.Console`
  (`Find` does not create a host, `For` does).[^kfs-vehicle-host][^console-log] A Harmony postfix on
  `ConsoleLog.Log` / `Event` / `Host` (all public instance methods) gives push delivery, but for a
  vehicle those run on the thread that dispatches its computer (a solver or worker thread, never the
  main thread); `Host` is in addition called on the main thread for editor build status lines
  (`EditorModel.BuildLogged`), and every call on the bench is on the main thread.[^workbench]
- **Adding verbs.** `_verbs` is a `private static readonly` array of a `private` record struct, and
  `Execute` reports anything else as unknown. There is no registration API. Options, none of them
  provided by the game: a Harmony prefix on `KfsConsoleCommands.Execute(string)` that handles extra
  verbs and skips the original (with postfixes on the private `Help()` and the `Verbs` getter to
  advertise them), or a separate terminal command registered with
  `TerminalInterface.RegisterCommand(delegate, owner)` whose method carries `[TerminalAction]`.
  [^console-commands][^terminal-interface]
- **Registering a separate command: the owner matters.** `RegisterCommand(delegate, owner)` files
  the command in a dictionary of its own for each `owner` object (`CommandLibrary[owner][name]`);
  with no owner it goes into one static dictionary. Three consequences, all read from
  `TerminalInterface`:[^terminal-interface-registration]
  - Registering the same name again under the **same owner** replaces the entry — safe to repeat.
  - `UnregisterObject(owner)` removes **every** command of that owner and nothing else. There is
    no call that removes one command by name.
  - The same name under **two owners** is accepted by `RegisterCommand`, and a typed command runs
    the first match (the static dictionary, then each owner's in enumeration order) — but the
    terminal's own `help` copies every command into one new dictionary with `Add`, so it throws an
    `ArgumentException` on the duplicate and lists nothing.

  A mod that can be loaded twice in one process (a reload without an assembly unload, or a second
  load context) should therefore file its command under one owner object that survives the reload
  — for example one kept in `AppDomain` data — and unregister exactly that owner when it unloads.
  purrTTY does this for its `purr` command (`purrTTY.Kfs/Game/KsaConsoleHost.cs` in this
  repository; not yet run in the game).
- **Terminal command signatures.** A terminal callback may only take primitives, `string` or
  `DateTime` parameters; with `ArgParseMode.Unparsed` it must take exactly one `string`. In default
  mode arguments are split by `System.CommandLine`'s `CommandLineParser.SplitCommandLine` and
  converted with `Convert.ChangeType`.[^terminal-command][^terminal-parser]
- **Threading.** Console input is processed on the main thread; verbs assume it. A mod invoking verbs
  from a terminal I/O thread must marshal to the main thread first.
- **`kfs off` is destructive for running programs:** it hands every vehicle back to the stock flight
  computer; after `kfs on` each needs `kfs start` again.

[^console-commands]: KfsConsoleCommands.cs — `Verb` record :13; `_verbs` table :15-173; `Verbs` :175-185; `Register` :187-190; `Execute` (attribute, parsing, flag check, exception handling) :192-232; `On` :234-242; `SetFlag` :244-258; `Help` :260-269; `Kill` :271-290; `Budget` :292-327.
[^kfs-integration]: KfsIntegration.cs — `ConsoleWindowWriter` :39-75; `Output` :91; `Workbench` getter (`Out = Output`) :93-121; `Print` :123-126; `Error` :128-135; `RegisterCommands(Program)` :146-156; `RegisterCommands(TerminalInterface, object)` :178-181; `ShowEditor` :183-198; `ToggleEditor` :200-214.
[^workbench]: Workbench.cs — constructor (`BuildLogged` → `Target.Console.Host`) :364-389; `Headers` / `ReportHeaders` :216-268; `BusCommand` :270-342; `Msg` :344-362; `Ws` :396-404; `Ls` :406-418; `Edit` :420-451; `New` :453-476; `Report` (build printing) :680-737; `Load` :739-807; `SelectTarget` :944-971; `Start` :973-989; `Stop` :1004-1020; `ResetTarget` :1075-1091; `Tick` :1093-1132; `Run` :1143-1166; `Frame` (deferred printing) :1168-1200; `State` :1223-1351; `Perf` :1353-1366; `Prof` :1368-1381; `Trace` :1410-1454; `TryResolve` :1456-1511; `Peek` :1568-1606; `ParseValue` :1638-1667; `Poke` :1669-1732; `Report(file)` :1833-1846; `Lines` :1859-1913; `Preview` :1915-1958; `Def` :1960-1993; `WatchCommand` :1995-2017; `NanCommand` :2019-2025; `DisasmView` :2027-2087; `Manual` :2123-2133; `ExamplesCommand` :2135-2140.
[^terminal-interface]: TerminalInterface.cs — `OnOutput` :22; `Execute(string)` :41-56; `RegisterCommand` :105-124; `ProcessCommand` (lookup, unknown-command log) :126-154; built-in `clear` and `help` :156-238.
[^terminal-parser]: Parser.cs — `SplitToCommands` (`;` outside quotes) :12-42; `SplitCommand` (lower-cased name) :44-53; `SplitToArgs` (`Unparsed` passes one string) :55-66.
[^terminal-command]: Command.cs — `Issue` :28-32; argument count and conversion :34-76; `Unparsed` signature rule :78-91; supported parameter types :109-124.
[^console-window]: ConsoleWindow.cs — class is an `ILoggerProvider` :14; `Terminal` field :240; `OnLog` :386-409; `Submit` :533-550; `Print` overloads :568-601.
[^console-window-ex]: ConsoleWindowEx.cs — toggle and submit keys :8-12; `OnKey` :14-38. The binding is applied in GameSettings.cs:2695-2701.
[^program]: Program.cs — `TerminalInterface` property :585; console window created :780 and added as a logging provider :807; settings `Console.OnBoot` executed :867; command registration block ending with `KfsIntegration.RegisterCommands(this)` :964-979; `ModLibrary.OnBoot` executed :1058.
[^universe]: Universe.cs:2696-2697.
[^kfs-prototype]: KfsPrototype.cs:8-26.
[^console-log]: ConsoleLog.cs — limits :9-13; `Version` :35-44; `Log` :61-78; `Event` :99-116; `Host` :118-128; suppression summary :138-156; capacity trimming :158-166; `Snapshot` :168-174; `CopyTail` :176-187. LogLine.cs:3.
[^trace-store]: TraceStore.cs — capacities :9, :19; `Channels` :21-30; `Add` :37-58; `Samples` :60-67; `CopyLast` :78-91.
[^host-computer]: HostComputer.cs — `Console`, `Traces`, `Watches` :135-139; program log line appended :754-770; trace sample added :774.
[^kfs-vehicle-host]: KfsVehicleHost.cs — `DEFAULT_FRAME_BUDGET_MS` :53; `FrameBudgetMs` :99; `For` :139-142; `Find` :144-147; `HandBackAtFlagOff` :212.
[^diagnostic-manual]: DiagnosticManual.cs:11-50.
[^purrtty-console-shell]: purrTTY.CustomShells/GameConsoleShell.cs — capture gate `IsCapturing` and `_isExecutingCommand`; `Program.TerminalInterface.Execute(commandLine)` inside the capture window; remarks on asynchronous output being lost.
[^purrtty-print-patch]: purrTTY.GameMod/Patches/ConsoleWindowPrintPatch.cs — postfix on `ConsoleWindow.Print(ReadOnlySpan<char>, ImColor8, int)`.
[^console-commands-errors]: KfsConsoleCommands.cs:221, :226, :231, :240, :276, :282, :305 — the seven call sites of `KfsIntegration.Error`; there is none outside this file.
[^console-commands-split]: KfsConsoleCommands.cs:201-203 (`IndexOf(' ')`, `Substring(num + 1).TrimStart()`), :49-52 (`build` → `Workbench.Build(a)`), :80-83 (`load` → `Workbench.Load(a)`).
[^workbench-args]: Workbench.cs:659-661 (`Build`: `file.Trim()`), :739-762 (`Load`: `args.Split(' ', StringSplitOptions.RemoveEmptyEntries)`, `--part`, `--vehicle`, else the file).
[^terminal-interface-registration]: TerminalInterface.cs:97-103 (`UnregisterObject`), :105-124 (`RegisterCommand`: per-owner dictionary :114-118, static dictionary :122), :126-143 (`ProcessCommand` lookup order), :162-186 (`help`: `dictionary.Add` at :175 and :184).
