---
type: Extension Point
title: kFS extension-point map
description: Every way a mod can extend or hook kFS, ranked by cost and risk — services, kC headers, kASM symbols, messages, I/O layout, console commands, examples, output capture and UI — with the mechanism, the evidence level and the document that has the detail.
resource: ksa-decomp:KSA.Kfs
tags: [kfs, modding, extension, harmony, map]
sources:
  - id: offline-harness
    resource: /modding/offline-harness.md
    title: Offline harness experiments
  - id: toolchain-extension-points
    resource: /compiler/toolchain-extension-points.md
    title: Toolchain extension points
  - id: host-extension-surface
    resource: /host/host-extension-surface.md
    title: Host extension surface
  - id: patch-targets
    resource: /integration/patch-targets-and-seams.md
    title: Patch targets and seams in the game glue
  - id: tooling-extension-points
    resource: /tooling/tooling-extension-points.md
    title: Tooling extension points
  - id: game-tables-loader
    resource: /tables/game-tables-loader.md
    title: GameTables loader
  - id: system-headers
    resource: ksa-decomp:KSA.Kfs.KC/SystemHeaders.cs
    title: SystemHeaders (decompiled)
  - id: builtin-includes
    resource: ksa-decomp:KSA.Kfs.KAsm/BuiltinIncludes.cs
    title: BuiltinIncludes (decompiled)
  - id: kfs-integration
    resource: ksa-decomp:KSA.Kfs/KfsIntegration.cs
    title: KfsIntegration (decompiled)
  - id: console-commands
    resource: /tooling/console-commands.md
    title: kFS console commands
  - id: dispatch-and-threading
    resource: /integration/dispatch-and-threading.md
    title: kFS dispatch and threading
generated: { by: claude-code/claude-fable-5-2, at: 2026-11-26T06:11:10Z }
status: stable
ksa_build: "2026.11.25.5949"
---

# Reading this map

kFS has **no plugin interface**: no registries, events or overridable types meant for mods. It is
nevertheless unusually extensible, because nearly everything is public and most behaviour is driven
by one data-table object. Each entry below says what a mod would add, how, and how sure we are:

- **Tested offline** — exercised against the real `KSA.dll` in [the offline harness](/modding/offline-harness.md).[^offline-harness]
- **Read** — established from the decompiled code, not executed.
- **Precedent** — done by an existing mod in the running game.
- **Implemented** — used by purrTTY's kFS extension in this repository (`purrTTY.Kfs`,
  `purrTTY.Kfs.Lsp`, `purrTTY.GameMod/Kfs`) and covered by its offline tests, which run the real
  `KSA.dll` outside the game. **None of it has run in the game**; "implemented" is not "verified in
  game". The section "What an implementation has exercised" says which code and which tests.

Area-specific detail lives in four companion documents:
[toolchain](/compiler/toolchain-extension-points.md),[^toolchain-extension-points]
[host](/host/host-extension-surface.md),[^host-extension-surface]
[game glue](/integration/patch-targets-and-seams.md)[^patch-targets] and
[tooling](/tooling/tooling-extension-points.md).[^tooling-extension-points]

# Summary

| # | Extension | Cost | Evidence | Verdict |
| --- | --- | --- | --- | --- |
| 1 | New **services** (program → game, one result cell back) | 3 small Harmony postfixes + data | Tested offline, including a 22-service family; implemented | **Use this** |
| 2 | New **kC headers / builtins / constants** | none (public statics) | Tested offline; implemented | Use with 1 |
| 3 | New **kASM symbols** for services | none (automatic from rows) | Tested offline; implemented | Free with 1 |
| 4 | New **enumerations** as argument domains | data | Tested offline (sub-range domains, E25); implemented | Use with 1 |
| 5 | New **host-published messages** (game → program, structured) | catalogue row + packer patch + delivery | Read | Possible, heavier |
| 6 | New **command / sensor fields** (memory-mapped I/O) | layout, header, write-back patches; breaks all images | Read | Avoid |
| 7 | New **opcodes** | interpreter, loader, both compilers | Read | Not practical |
| 8 | New **console verbs** | prefix on `KfsConsoleCommands.Execute`, or a separate command | Read; a separate command is implemented (its game registration is compiled, not executed) | Easy |
| 9 | Shipping **example programs** | write files, or patch the examples menu | `Workspace.Write` tested offline (E11); implemented | Easy |
| 10 | Capturing **program output and telemetry** | polling public objects | Read; log, snapshot and trace polling implemented and tested offline against real `HostComputer`s; the output tee's game side is compiled, not executed | Easy, no patch (one optional patch to avoid doubled errors) |
| 11 | Driving the **workbench** programmatically | public API | Tested offline (build/load/start); running verb lines through `KfsConsoleCommands.Execute` is implemented (compiled, not executed) | Easy, no patch |
| 12 | Extending the **editor / reference UI** | mostly automatic; postfix `DrawMenuBar` for more | Read | Optional |
| 13 | Replacing the **kC front end** | `Compiler.KcFrontEnd` is settable | Read | Exotic |
| 14 | A **language server** on the game's compiler | public compiler, tables and AST types | Tested offline (diagnostics, hover, completion with Neovim); implemented, standalone and as an in-game host | Feasible — see [language-tooling surface](/modding/language-tooling-surface.md) |

# 1 — Services

The only program↔game channel that is data-driven end to end apart from its routing switches.
A mod adds rows to the services table as it loads,[^game-tables-loader] declares the kC prototypes, and claims its ids
in `VehicleComputer.ApplyRequest`; result-bearing services are answered in
`ISyscallHost.IssueResult`. Full recipe, validation rules and constraints:
[adding a service](/modding/adding-a-service.md).

What services can and cannot carry:

| Can | Cannot |
| --- | --- |
| Scalar and flattened-struct arguments (`u32`, `f32`, `vec3`, …), validated by declarative domains | Strings (no program-side string values; only `log` format indices) |
| One synchronous result cell (`u32` or `f32`) | Multi-cell or struct results from kC |
| Up to 256 calls per tick | Blocking, callbacks or deferred results |
| Work in pilot mode, full control and fallback alike (when not marked `requires_stock`) | Run on the bench (no vehicle; requests are rejected, results are zero) |

Patterns that fit this shape: **count + indexed getters** for enumeration
(`n = x_count(); v = x_get(i, PROP)`), **setters with clamping** for commands, and
**request-then-poll** for anything that needs the main thread to answer.

# 2 — kC headers, builtins and constants

`SystemHeaders.Files` (`Dictionary<string,string>`) and `SystemHeaders.Builtins`
(`HashSet<string>`) are public, static and read live on every compile.[^system-headers] Adding a
key creates a new `<name.h>`; replacing the `"kfs.h"` entry changes the stock header (avoid).
Header text may contain anything kC accepts — prototypes, `#define` constants, typedefs, enums,
even function bodies — but bodies are a trap: an unused `static` helper warns (`KC-W006`) in every
including program and a non-static one breaks multi-file projects (`KC-E016`). Only headers
included with `<…>` may declare builtins.

`CompileInput.SystemFiles` would override per compile, but no in-game caller sets it. The
generated `<vehicle.h>` can be changed by patching the public static `VehicleHeader.Generate`, but
its output is cached per layout object. See
[generated headers](/language/kc-generated-headers.md).

There is no per-vehicle variant of a system header: constants that depend on the vehicle (a count
of lights, named indices) must come from a service call at run time or from a file the mod writes
into the workspace.

# 3 — kASM symbols

`<kfs.kinc>` and `<vehicle.kinc>` are computed on every assembly from the tables and the layout:
`SVC_*` from each service row's `symbol`, `MSG_*` and struct types from the message catalogue,
`sensors.*` / `commands.*` from the layout. A mod-added row is therefore usable as
`SYSCALL SVC_…` with no further work.[^builtin-includes] The `KFS_*` enumeration constants come from
`CatalogueEnums.All`, which is hard-coded to seven groups; a postfix on that public static method
could add more. See [kASM](/compiler/kasm-assembly-language.md).

So a mod's symbols end up in two places. The **service ids** (`SVC_<SYMBOL>`) are in the
generated `<kfs.kinc>`, because the game builds that include from the augmented service rows on
every assembly. The mod's own **constants** — selector values, enumeration names — are not, and a
mod supplies them as a `.kinc` file of `.equ` lines that the program includes with
`.include "name.kinc"`. A kASM program that uses a mod service needs both includes; the mod's
file must not define the `SVC_*` names again.

# 4 — Enumerations

`enums.json` is a dictionary of name → value list. A key added during table augmentation is
immediately valid in a service row as `enum:<key>` (or `enum:<key>[a..b]`), so the VM rejects
out-of-range selector arguments before the host sees them. kC does not read `enums.json`; define
the matching constants in the mod's header. Never reorder a stock list. See
[enums table](/tables/enums-table.md).

# 5 — Host-published messages

The message catalogue is data: a row appended to `MessagesData.Messages` gets an id, a generated
`kfs_msg_<name>_t` in `<vehicle.h>`, the `#pragma kfs subscribe` machinery and load-time checks
automatically. What is **not** data:

- The host packs exactly seven ids (`HostMessages.MsG3Order`) and `PackInto` throws for any other;
  a mod must write its own bytes and deliver them with the public `KvmMachine.WriteMessage(id, slot)`
  on the dispatch thread each tick (for example from a postfix on the private
  `VehicleComputer.PlayerTick`), and add its id to the private `_hostPublished` list at load.
- Field types are a closed set: `bool`, `uint32`, `int64`, `float`, `vec3`, `quat`, `mat3`,
  `dvec3`. Top-level fields cannot be arrays.
- A per-vehicle array (`thing[i]`) is sized only by a **command-block group** of the same name,
  and its `<GROUP>_COUNT` macro is emitted only for thruster, engine and gimbal — so a variable
  list forces a layout change (entry 6).

A fixed-size message (say 16 slots plus a count) avoids the layout dependency and is a reasonable
later step for bulk read-back. See [message bus](/host/message-bus.md),
[messages table](/tables/messages-table.md) and
[host extension surface](/host/host-extension-surface.md).

# 6 — Command and sensor fields

Adding `light[i].on`-style fields to the generated layout would be the most native interface, and
the header generators would emit the structs unprompted. It is also the most expensive route:

- the layout CRC changes, so **every existing image for every vehicle stops loading**;
- `KfsControllerSet` (layout source, change detection, inert tracking) knows only thrusters,
  engines and gimbals;
- command writes require the program to claim `ALLOCATION` (full control), which is wrong for
  switching a light from a pilot-mode program;
- vehicles have no sensor block at all today, so read-back needs new plumbing;
- write-back is hard-coded per controller type.

See [I/O layout generation](/host/io-layout-generation.md) and
[controller set](/integration/controller-set-and-layout-changes.md).

# 7 — Opcodes

All 146 opcodes are an enum, a `switch` in the interpreter, a loader table and code in both
compilers. Not a mod-sized change.

# 8 — Console verbs

kFS registers one console command, `kfs`, parsed by a private static verb table. Options:
register a separate command with the game's console (the route purrTTY's `GameConsoleShell`
already rides), or prefix the public `KfsConsoleCommands.Execute(string)` to intercept new verbs.
See [console commands](/tooling/console-commands.md).

A separate command needs care about its **owner object**: the console replaces a command
registered again under the same owner, removes commands only per owner, and its `help` throws when
two owners hold the same name — which a reloaded mod produces if it registers under a new owner
each time.[^console-commands]

# 9 — Example programs

`Workspace.Write(path, text)` and `Workbench.Edit(path)` are public, so a mod can drop files into
the workspace on demand. The File › Examples menu reads a get-only static list and a dictionary
behind a read-only interface; extending the menu needs a postfix. See
[tooling extension points](/tooling/tooling-extension-points.md) and
[workspace and files](/tooling/workspace-and-files.md).

# 10 — Program output and telemetry

| What | Where | How to read |
| --- | --- | --- |
| `kfs …` command replies | `KfsIntegration.Output` (public settable `TextWriter`) | wrap it, then read `KfsIntegration.Workbench` once: the workbench copies `Output` only when that getter runs. `KfsIntegration.Error` writes its line to a non-default writer **in addition** to logging it, and the console shows log errors itself, so a wrapper that forwards everything prints each `kfs` error twice[^kfs-integration] |
| Program log (`kfs_logf`) and host events | `computer.Console` (`ConsoleLog`) | poll `Version`, then `CopyTail`; no event exists |
| Traces, watches, bus samples | `computer.Traces`, `Watches`, `Bus` | public read models |
| Machine state | `computer.Snapshot` | immutable, safe from the UI thread |

Program output never reaches the game console, so capture of console prints (as purrTTY's
`GameConsoleShell` does) sees command replies but not `kfs_logf` lines.

# 11 — Driving the workbench

`KfsIntegration.Workbench` exposes `Build`, `BuildText`, `Load`, `Start`, `Stop`, `ResetTarget`,
`SelectTarget`, `Upload`; `KfsConsoleCommands.Execute("<verb> …")` runs any verb;
`KfsIntegration.ShowEditor()` opens the editor. All main thread. Vehicle load/start/stop are
queued and only take effect at the vehicle's next dispatch — not while paused. See
[workbench](/tooling/workbench.md).

# 12 — Editor and reference UI

The editor is a plain multiline text box with no highlighting or completion, so it needs no
change for new services. The reference window's Services page is built once from the tables when
first opened; a mod row appears there if the tables were augmented first. `KfsEditorWindow.DrawMenuBar`
is a public override a postfix can extend. See [reference window](/tooling/reference-window.md).

# 13 — The front end

`Compiler.KcFrontEnd` is a public settable `Func<IFrontEnd>`; `KcFrontEnd.Register()` (a module
initialiser, also called by the `Workbench` constructor) resets it, so a replacement must be
re-installed afterwards. Useful only for source-to-source experiments.

# Where code may run

| Hook | Thread | Runs when | Use for |
| --- | --- | --- | --- |
| Postfix on `GameTables.LoadEmbedded` | whichever first reads the tables (main) | once | table augmentation |
| Postfix on `VehicleComputer.ApplyRequest` | solver / worker | per unclaimed request, after a completed tick | decoding and queueing commands |
| Postfix on `VehicleComputer` `ISyscallHost.IssueResult` | solver / worker | inside each result-bearing `SYSCALL` | synchronous read-back |
| `computer.Enqueue(Action<HostComputer>)` | solver / worker | start of that computer's next dispatch | touching the computer from the main thread |
| Postfix on `KfsIntegration.BeforeInputEvents` | **main, sync window** | every frame, even paused or flag off | all game mutation |
| StarMap `[StarMapAfterGui]` / ImGui windows | main, **UI phase (dispatches in flight)** | every drawn frame | UI only |

What the sync-window hook writes is not all visible at the same time. A **field** of a live object
(a switch flag, an animation goal, a light template) is read by the dispatches of the job started
in the same frame; **committed module state** computed from it (an animation's position, whether a
consumer is active) is produced by that job and committed at the start of the following frame, so
a program reads it one frame later still. And although `BeforeInputEvents` itself runs every
frame, the stock body — including the pruning of dead hosts — runs only while the kFS switch is
on.[^dispatch-and-threading][^kfs-integration]

# What an implementation has exercised

purrTTY's kFS extension uses entries 1–4, 8–11 and 14. This table says, for each mechanism, where
the code is and how far it has been exercised. "Offline" means the repository's test suite, which
loads the unmodified `KSA.dll` and runs the game's own compiler, VM and host classes in a test
process (see [the offline harness](/modding/offline-harness.md)). The last column is the same in
every row on purpose.

| Mechanism | purrTTY code (repository-relative) | Exercised offline | In the game |
| --- | --- | --- | --- |
| Table augmentation, published by assigning `KfsIntegration._tables` (with the `LoadEmbedded` postfix as fallback) | `purrTTY.Kfs/Tables/TableAugmenter.cs`, `TablesPublisher.cs` | yes: the game's loader validates the rows; both publication paths | not run |
| kC header and builtin registration; removal at unload | `purrTTY.Kfs/Tables/SystemHeaderRegistration.cs` | yes | not run |
| `VehicleComputer.ApplyRequest` and `ISyscallHost.IssueResult` postfixes | `purrTTY.Kfs/Patches/` | yes: every service through the real VM on a `FakeVehicle` computer | not run |
| `HostComputer.ApplyRequest` postfix for the bench | `purrTTY.Kfs/Patches/BenchApplyRequestPatch.cs` | yes, on a `BenchMachine` | not run; the target is a one-line virtual that may be inlined |
| `KfsIntegration.BeforeInputEvents` postfix | `purrTTY.Kfs/Patches/BeforeInputEventsPatch.cs`, `Game/MainThreadPump.cs` | the pump logic yes, over simulated hosts; the postfix is applied but nothing calls the patched method offline | not run |
| Host discovery through `KfsVehicleHost.AllHosts`; reads and writes of `LightModule`, `PowerConsumer`, `KeyframeAnimationModule` | `purrTTY.Kfs/Game/HostDirectory.cs`, `GameHostSource.cs`, `KsaVehicleParts.cs` | no (needs live vehicles); only the pure rules in `KsaPartRules.cs` and the template cloning in `LightOverrides.cs` | not run |
| A separate console command (`TerminalInterface.RegisterCommand`, `[TerminalAction]`) | `purrTTY.Kfs/Console/`, `Game/KsaConsoleHost.cs` | the command and its verbs yes, against a fake console; the game registration no | not run |
| Example programs through `Workspace.Write` | `purrTTY.Kfs/Examples/` | yes: every example built as the workbench builds a folder and run on a simulated vehicle | not run |
| Output tee on `KfsIntegration.Output`; verb lines through `KfsConsoleCommands.Execute`; prefix and finalizer on `KfsIntegration.Error` | `purrTTY.Kfs.Lsp/InGame/OutputTee.cs`, `purrTTY.GameMod/Kfs/KsaKfsGameSide.cs`, `KfsErrorEchoPatch.cs` | the tee and the queue yes, over a fake game side; the game calls no | not run; `Error` is small enough to be inlined |
| Program log, snapshot and loaded image read from another thread (`ConsoleLog.Version` / `CopyTail`, `HostComputer.Snapshot`, `LoadedBytes`) | `purrTTY.Kfs.Lsp/Live/HostComputerLive.cs`, `Features/LiveFeed.cs` | yes, against real `HostComputer` objects | not run |
| A language server on `Compiler.CompileOnThisThread`, `Assembler`, `Lexer`, the tables and `BuiltinIncludes` | `purrTTY.Kfs.Lsp/`, `purrTTY.Kfs/Runtime/ProjectResolver.cs`, `ProjectCompiler.cs` | yes, including a headless Neovim | the standalone server needs no game; the in-game host is not run |


# Modding notes

- Start from entry 1; it covers commands and scalar read-back with three patches on stable
  targets. Reach for entry 5 only when a program needs many values every tick.
- Whatever is added, keep the invariants in [risks and compatibility](/modding/risks-and-compatibility.md):
  never change a stock table row, version or layout; never throw into a dispatch; never write the
  game from a worker thread.

[^offline-harness]: Experiments E1–E12.
[^toolchain-extension-points]: Compiler, assembler and image side.
[^host-extension-surface]: Host computer, messages and layout side.
[^patch-targets]: Game-glue patch targets, seams (a)–(c).
[^tooling-extension-points]: Workbench, console, editor and reference window.
[^game-tables-loader]: Loader, validation and consumers of the tables.
[^system-headers]: SystemHeaders.cs:8–41.
[^builtin-includes]: BuiltinIncludes.cs:43-46 (`kfs.kinc`: `Constants[item.Symbol] = item.Id` for every non-reserved row of `tables.ServicesData.Services`), :33 (constructor, run for every `.include <…>`).
[^kfs-integration]: KfsIntegration.cs:91 (`Output`), :118 (the `Workbench` getter assigns `_workbench.Out = Output` on every read), :128-135 (`Error`: logs, then writes to `Output` unless it is the private `ConsoleWindowWriter`), :158-176 (`BeforeInputEvents`: the whole body is inside `if (KfsPrototype.Enabled)`, `ReleaseDead()` at :163).
[^console-commands]: "Registering a separate command: the owner matters", with the lines of `TerminalInterface`.
[^dispatch-and-threading]: "What a dispatch reads: live fields and committed state".
