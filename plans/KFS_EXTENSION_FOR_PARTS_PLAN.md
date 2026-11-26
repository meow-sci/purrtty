# kFS extension plan — parts control, external test runtime, language server, Neovim

| | |
| --- | --- |
| Status | Tasks F1, A1–A5, B1, B2, C1–C4 and D1–D3 are implemented, integrated into `purrTTY.GameMod`, independently reviewed and corrected. **Nothing has been run in the game.** Details: the "Status" section below; what must be checked in the game: §10. |
| Branch | `feature/explore-kfs` |
| KSA build researched | 2026.11.25.5949 |
| Reference | [`docs/kfs-info/`](../docs/kfs-info/index.md) (OKF bundle), spikes in [`plans/investigations/kfs-spikes/`](investigations/kfs-spikes/README.md) |
| Written | 2026-11-25 |
| Reviewed | Checked independently against the decompiled game, the purrTTY sources and the spike code; its 37 findings (12 errors, 6 unsupported claims, 11 inconsistencies, 8 gaps) are incorporated |

## Status (2026-11-26)

**Nothing described here has been run in the game.** Everything below was built and tested
**offline**: the tests load the player's own `KSA.dll` in an ordinary .NET process and run the
game's real kFS compiler, assembler, virtual machine and host layer against simulated parts. The
code that only exists inside the game (the list at the end of this section) was compiled against
the game assemblies and audited member by member against the decompiled sources of build
2026.11.25.5949, and no test executes it. §10 is the checklist that closes this gap; until its
first items pass, treat every in-game behaviour in this document as designed, not observed.

| Task | State | What exists |
| --- | --- | --- |
| F1, A1, A2, A3, B1 (milestone M1) | done, offline | scaffolding and the assembly resolver; catalogue, emitter and table augmentation; runtime core and the two host hooks; installer; the test runtime (`KfsRuntime`, `SimVehicle`). Notes: §8.6 |
| A4 — game back end and wiring | code complete, **not run in the game** | `purrTTY.Kfs/Game/`, the `BeforeInputEvents` pump patch, install from `purrTTY.GameMod/Patcher.cs`, `kfs.toml` |
| A5 — `purr` console command, examples, menu | code complete, **not run in the game** | `purrTTY.Kfs/Console/`, `purrTTY.Kfs/Examples/`, `purrTTY.GameMod/Kfs/KfsMenu.cs` |
| B2 — project resolver and `purr-kfs` | done, offline | `ProjectResolver`, `ProjectCompiler`, `purrTTY.Kfs.Cli` (`check`, `run`, `lsp`, `header`, `kinc`, `services`) |
| C1, C2 — language-server core, builds, diagnostics, standalone host | done, offline | `purrTTY.Kfs.Lsp` (`Protocol/`, `Documents/`, `Build/`), `purr-kfs lsp` |
| C3 — hover and completion | done, offline | `purrTTY.Kfs.Lsp/Index/`, `Features/` |
| C4 — the in-game server | code complete, **not run in the game** | `purrTTY.Kfs.Lsp/InGame/`, `Live/`; `purrTTY.GameMod/Kfs/KfsLspService.cs`, `KsaKfsGameSide.cs`, `KfsErrorEchoPatch.cs` |
| D1 — terminal exit handling (T1, T2 of §7.3) | done, offline | `purrTTY.Terminal/Pty/`, `Sessions/`, `purrTTY.Display/Ghostty/SessionExitPolicy.cs` |
| D2 — Neovim runtime files | done; run against a headless Neovim 0.12.6 only | `purrTTY.GameMod/nvim/` |
| D3 — launcher, menu, `purr nvim` | code complete, **not run in the game** | `purrTTY.Kfs/Runtime/NeovimLaunchPlan.cs`, `purrTTY.GameMod/Kfs/KfsNeovimLauncher.cs` |

Where the code differs from the text of the sections: §8.6 (milestone M1) and §8.7 (the later tasks).
The sections themselves were corrected wherever they contradicted the code.

**Deliberately not built** (outside the agreed scope; their sections remain as design):

| Not built | Section |
| --- | --- |
| B3 — scenario files and `purr-kfs test` | §5.6, §5.8 |
| C5, C6 — document symbols, cost lenses, inlay hints, navigation | §6.6 |
| D4–D8 — terminal polish; in particular terminal row **T3** (honour `PATHEXT`, run `.cmd`/`.bat` shims through `cmd.exe /c`) | §7.3 |

**Tests.** The last full run of the suite (after both fix passes and the final cleanup, Debug and
Release, on macOS arm64) reported
**1,008 passed, 6 skipped (platform-specific tests), 0 failed** in six test projects:
`purrTTY.Kfs.Tests` 393, `purrTTY.Kfs.Tools.Tests` 207, `purrTTY.Terminal.Tests` 141 (+ 6 skipped),
`purrTTY.CustomShellContract.Tests` 197, `purrTTY.CustomShells.Tests` 38, `purrTTY.Display.Tests` 32.

**Compiled but not executed by any test** (game-only code; the first things §10 exercises): the
game-facing members of `KsaVehicleParts`, `GameHostSource` and `KsaConsoleHost`
(`purrTTY.Kfs/Game/`), and everything in `purrTTY.GameMod/Kfs/` — `KsaKfsGameSide`,
`KfsLspService`, `KfsErrorEchoPatch`, `KfsNeovimLauncher`, `KfsMenu`, `KfsConfig` (`purrTTY.GameMod`
has no test project). The rules they apply (`KsaPartRules`, `LightOverrides`, `MainThreadPump`,
`NeovimLaunchPlan`, `InGameLanguageHost`, `LspServiceCore`) are separate types that the offline
tests do execute.

## 0. How to read this plan

- **§1–§3** say what will be built, what is already proven, and the architecture shared by every
  workstream.
- **§4–§7** are the four workstreams, each with design, file-by-file contents, and tests:
  - **A** — extend kFS with services for lights and part animations (the original goal);
  - **B** — the same extended kFS running **outside the game** as a unit-test runtime;
  - **C** — a **language server** for kC and kASM built on the game's real compiler;
  - **D** — **Neovim, with that language server attached, opened from in-game through purrTTY**.
- **§8** is the task breakdown: every coding task is sized and briefed for one delegated coding
  agent, in dependency order, with its acceptance test and its state; §8.6 and §8.7 list where the
  code differs from the text.
- **§9–§11** are risks, the in-game validation checklist (the master list of what a player must
  still check), and the decisions taken and still open.

Vocabulary follows the bundle's [glossary](../docs/kfs-info/overview/glossary.md). Claims about
the game cite the bundle document that establishes them rather than repeating the evidence.

## 1. Goals and non-goals

### 1.1 Goals

1. **A — Parts services.** A kC or kASM program running on a vehicle can:
   - enumerate the vehicle's lights; switch each on or off; set its colour, inner and outer cone
     angle, brightness and range; read all of that back; restore the stock appearance;
   - enumerate the parts with a deploy animation; deploy, retract or toggle each; read state and
     progress;
   - enumerate the parts with an actuation animation; set each target from 0.0 to 1.0; read target
     and position.
2. **B — External runtime.** The same extended kFS (real compiler, real VM, real host, the mod's
   services) runs in an ordinary .NET process with **simulated** lights and animations, so the
   extension itself and players' programs can be unit tested without launching the game.
3. **C — Language server.** An LSP server for kC and kASM whose diagnostics come from the game's
   own compiler and which knows the mod's services, usable from the game process and standalone.
4. **D — Neovim through purrTTY.** From inside the game, open a kFS source file in the player's
   host `nvim` inside a purrTTY terminal with the language server attached, and build / load /
   start the program from the editor.

### 1.2 Non-goals (this plan)

- Changing a vehicle's memory-mapped I/O layout or adding host-published messages. Both are
  possible but costly ([extension-point map](../docs/kfs-info/modding/extension-points.md) §5–§6);
  §4.12 records when they would become worth it.
- Persisting light overrides or program state in save games (kFS itself saves nothing).
- Per-emitter on/off where the game has one switch per part; controlling other vehicles; symmetry
  fan-out.
- kScript (not implemented in the game).
- Shipping a .NET runtime or a Neovim binary with the mod.

## 2. What is already established

### 2.1 How kFS works (read from the decompiled game)

Full detail is in the bundle; the facts this plan leans on:

| Fact | Where |
| --- | --- |
| A program talks to the game through a command block, numbered **services** and a message bus. Only services are data-driven end to end; vehicles have no sensor block. | [architecture](../docs/kfs-info/overview/architecture.md) |
| One `GameTables` instance, built lazily on first use from JSON embedded in `KSA.dll`, drives the compiler, assembler, VM, cost model, image loader and reference window. The game does not read it at start-up. | [tables loader](../docs/kfs-info/tables/game-tables-loader.md) |
| A service call is validated by the VM against declarative argument domains, may get **one result cell synchronously**, is queued, and after a completed tick is routed by three hard-coded id switches; an unknown id is rejected "no vehicle to act on". | [syscall ABI](../docs/kfs-info/vm/services-syscall-abi.md), [service request handling](../docs/kfs-info/host/service-request-handling.md) |
| `<kfs.h>` is a hard-coded string in a public mutable dictionary; a kC call binds to a service through the table's `kc` column when the function is declared in a `<…>` header and named in `SystemHeaders.Builtins`. kASM gets `SVC_*` symbols from the rows automatically. | [generated headers](../docs/kfs-info/language/kc-generated-headers.md) |
| The dispatch (VM ticks, service routing, results) runs on solver/worker threads, **never the main thread**, in parallel across physics bubbles, while the main thread draws UI. `KfsIntegration.BeforeInputEvents()` is the one main-thread point per frame where no solver job is running; stock kFS applies all game mutations there. Nothing dispatches while paused. | [dispatch and threading](../docs/kfs-info/integration/dispatch-and-threading.md) |
| An exception inside a dispatch halts that vehicle's computer with a host fault; one inside the table loader makes kFS unusable. | [risks](../docs/kfs-info/modding/risks-and-compatibility.md) |
| Hardware is addressed by rank of `InstanceId` in a list frozen per host; departed hardware goes inert, docked-on hardware gets no index. | [controller set](../docs/kfs-info/integration/controller-set-and-layout-changes.md) |
| A light's on/off is `Part.LightSwitch` (`PowerConsumer.LightIsActive`), one switch per full part. Colour, cone, intensity and range live in `LightModule.Template`, **shared by every light of that part type** until cloned per instance. | [light module](../docs/kfs-info/game-targets/light-module.md) |
| Deploy and actuation animations are one class, `KeyframeAnimationModule`; the command is `TimeGoal` (seconds, 0…`Shared.Duration`); `ShowDeployRetract` only picks the stock widget. Position slews at one animation-second per simulated second. | [keyframe animation module](../docs/kfs-info/game-targets/keyframe-animation-module.md) |
| gatOS and zippo already perform exactly these light and animation writes in the running game, from the main thread. | [control recipes](../docs/kfs-info/game-targets/control-recipes.md), [mod precedents](../docs/kfs-info/game-targets/gatos-and-other-mod-precedents.md) |
| Nothing of kFS is saved; hosts exist only for vehicles the workbench targeted; every vehicle computer emulates the "Bench" processor. | [lifecycle](../docs/kfs-info/integration/lifecycle-and-persistence.md) |

### 2.2 What was proven by running code

All against the unmodified `KSA.dll` of build 5949, outside the game
([offline harness](../docs/kfs-info/modding/offline-harness.md); sources in
[`plans/investigations/kfs-spikes/`](investigations/kfs-spikes/README.md)):

| # | Proven | Spike |
| --- | --- | --- |
| P1 | The kFS tables, compiler, VM, host and `Workbench` run in a plain console process. `KSA.dll` loads on arm64 with a two-byte in-memory patch of its PE machine field; nothing is copied or modified on disk. | 01, 04 |
| P2 | The table loader accepts mod service rows (typed arguments, domains, results) through the public `GameTables.Load(open, origin)`. | 01, 02 |
| P3 | A Harmony postfix on `GameTables.LoadEmbedded` makes the game's own `KfsIntegration.Tables` the augmented instance. | 03, 04 |
| P4 | kC needs a prototype in a `<…>` header **and** the name in `SystemHeaders.Builtins`; then the call compiles to the right `SYSCALL`. kASM needs nothing extra. | 01, 02 |
| P5 | Argument domains are enforced for mod rows; `vec3` arguments flatten to three cells; a one-cell `u32` or `f32` result works. **Multi-cell results crash the kC lowering.** | 02 |
| P6 | Harmony postfixes on `VehicleComputer.ApplyRequest` and its explicit `ISyscallHost.IssueResult` (with `Span` parameters) deliver requests to mod code and return mod results to the program. | 01, 02, 04 |
| P7 | The game's `Workbench` builds a kC file from a workspace folder, loads and starts it on a `FakeVehicle`, and the program's mod-service calls arrive — the whole in-game tooling path short of a real vehicle. | 04 |
| P8 | The complete API of §4.1 — 22 services, three enumerations with disjoint integer/float sub-ranges, a generated header — loads, compiles from kC and kASM, runs on the game's VM against simulated lights and animations, and rejects every documented misuse. 76 self-checks pass. | 06 |
| P9 | A language server driven by the game's compiler serves diagnostics, hover and completion to a headless Neovim 0.12.6 over TCP and stdio; positions map correctly in three encodings; warm compiles take 0.2–19 ms; concurrent compiles do not corrupt. | 05 |

### 2.3 What is not proven

- Anything in the running game: Harmony patch timing under StarMap, JIT inlining of patched
  methods, real light and animation effects, threading under load.
- Performance of the per-frame pump and of read-back from the dispatch thread on large vehicles.
- Behaviour across docking, staging and save/load with real vehicles (read from code only).

§10 is the checklist that closes these.

## 3. Architecture shared by all workstreams

### 3.1 One definition list, generated everywhere

A single C# catalogue lists every mod service and enumeration. From it the code generates the
JSON rows merged into `services.json`, the enum entries merged into `enums.json`, the text of
`<purrtty.h>`, the builtin-name set, the id→handler table, the language server's hover and
completion data, and the reference documentation. Rows and prototypes therefore cannot disagree —
which matters because a disagreement throws inside the game's compiler instead of producing a
diagnostic (P5).

### 3.2 One parts abstraction, two back ends

```text
                      kC / kASM program
                            │  SYSCALL 0x50xx
                 ┌──────────▼───────────┐
                 │  KvmMachine (game)   │  validates argument domains
                 └───┬──────────────┬───┘
   IssueResult (sync)│              │ApplyRequest (after the tick)
              ┌──────▼──────────────▼──────┐
              │  ServiceRouter (mod)       │  decode · range-check · answer · enqueue
              └──────┬──────────────┬──────┘
              reads  │              │ commands (queued)
              ┌──────▼──────────────▼──────┐
              │        IVehicleParts       │
              └──────┬──────────────┬──────┘
        in the game  │              │  outside the game
        ┌────────────▼───┐   ┌──────▼─────────────┐
        │ KsaVehicleParts│   │ SimVehicleParts    │
        │ live modules;  │   │ in-memory lights   │
        │ writes applied │   │ and animations;    │
        │ on the main    │   │ writes applied by  │
        │ thread         │   │ the test's pump    │
        └────────────────┘   └────────────────────┘
```

The router, the catalogue, the table augmentation, the two host hooks and the drain-and-apply step
are **identical** in the game and in tests. Only the object behind `IVehicleParts` differs, and the
few lines that discover the game's hosts. Workstream B is therefore not a
second implementation but the first one with a different back end, and every service gets an
automated end-to-end test: real compiler → real VM → real host hooks → router → simulated part.

### 3.3 One language host, two front doors

The language server is a transport-agnostic library over an `IKfsLanguageHost` (tables, layout,
part, workspace root, optional live vehicle). In the game it is hosted by the mod and listens on a
local socket; outside the game the same library is hosted by a small CLI that loads `KSA.dll` the
way the tests do. Neovim connects to whichever is available.

### 3.4 Projects

| Project | Kind | References | Ships in the mod? | Workstream |
| --- | --- | --- | --- | --- |
| `purrTTY.Kfs` | library | `KSA.dll`, `Brutal.Logging` and `Brutal.ImGui.Abstractions` (compile-only; the last for the console's `[TerminalAction]` attribute), `Lib.Harmony`, `purrTTY.Logging` | yes | A |
| `purrTTY.Kfs.Testing` | library | `purrTTY.Kfs`, `KSA.dll` (compile-only) | no | B |
| `purrTTY.Kfs.Tests` | NUnit | `purrTTY.Kfs`, `purrTTY.Kfs.Testing` | no | A, B |
| `purrTTY.Kfs.Lsp` | library | `purrTTY.Kfs`, `purrTTY.Logging`, `KSA.dll` and `Brutal.Logging` (compile-only); no NuGet package | yes | C |
| `purrTTY.Kfs.Tools.Tests` | NUnit | `purrTTY.Kfs`, `purrTTY.Kfs.Testing`, `purrTTY.Kfs.Lsp`, `purrTTY.Kfs.Cli` | no | B, C, D |
| `purrTTY.Kfs.Cli` (assembly and command `purr-kfs`) | console app | `purrTTY.Kfs`, `purrTTY.Kfs.Testing`, `purrTTY.Kfs.Lsp` | optional download | B, C |
| `purrTTY.GameMod` (existing) | mod entry | + `purrTTY.Kfs`, `purrTTY.Kfs.Lsp` | yes | A, C, D |
| `nvim/` runtime files (Lua) | not .NET | — | yes | D |

The tests of the command-line tool, the language server and the Neovim runtime files share one
project, `purrTTY.Kfs.Tools.Tests`; there is no `purrTTY.Kfs.Lsp.Tests`.

`StandaloneLanguageHost` lives in the CLI project — the only one that references both the
language-server library and the test runtime. `InGameLanguageHost` lives in the language-server
library (`purrTTY.Kfs.Lsp/InGame/`), with its few game calls behind an interface the mod
implements (§6.1), so that it is tested offline. `ProjectResolver` and `NeovimLaunchPlan` live in
`purrTTY.Kfs` so that the CLI, the server and the mod share one implementation of each.

Conventions for the new projects follow the existing ones: `net10.0`, nullable, warnings as
errors, `RequiresKSAAssemblies=true`, game references with `<Private>false</Private>` resolved from
`$(KSAFolder)`, NUnit tests that are silent on pass and never sleep for a fixed time
([`docs/build-and-test.md`](../docs/build-and-test.md)).

`purrTTY.Kfs` deliberately has **no dependency on the terminal projects** (`purrTTY.Terminal`,
`purrTTY.Display`): the kFS extension must stay loadable in a test process and switchable off
without touching the terminal.

### 3.5 Rules every workstream obeys

| Rule | Why |
| --- | --- |
| Never change a stock table row, table version, layout or the `kfs.h` entry. | Existing images and programs must keep working. |
| Install all-or-nothing: if any patch target, the catalogue self-check or the table dry run fails, install nothing and log one line. | A half-installed extension produces confusing compile errors. |
| Every patch body is wrapped in `try`/`catch`; dispatch-side code never throws, blocks or allocates per call after warm-up. | An escaping exception halts the player's computer mid-flight. |
| No write to a live game object outside the main-thread pump. | The dispatch runs concurrently with the main thread. |
| Every float from a program is checked finite by the VM (argument domain) and clamped by the handler. | The game does not validate; a NaN animation goal never settles. |
| Results are one cell; enumeration is "count + indexed getter". | Multi-cell results crash the kC compiler. |
| The kFS extension has its own enable switch and its own status line in the log. | A game update that breaks kFS must not take the terminal with it. |

## 4. Workstream A — parts services

### 4.1 The program-facing API

Programs include `<purrtty.h>`; functions carry the prefix `x_` and constants `X_` (decision D1).
Every function is a kFS service in the id block
`0x5000–0x50FF` ("P" is ASCII `0x50`); the table `name` is the kC function name and the kASM symbol
is `SVC_` + the upper-cased name. All services have `requires_stock: null`, so they work in pilot
mode, full control and fallback alike.

**Bookkeeping**

| Id | Prototype | Kind | Purpose |
| --- | --- | --- | --- |
| `0x5000` | `uint32_t x_parts_info(uint32_t what)` | request+result | `X_PARTS_GENERATION`, `X_PARTS_DIRTY`, `X_PARTS_LIGHTS`, `X_PARTS_DEPLOYABLES`, `X_PARTS_ACTUATORS` |
| `0x5001` | `void x_parts_rescan(void)` | request (Medium) | Rebuild the part lists now; indices may change; the generation increments |
| `0x5002` | `void x_parts_describe(uint32_t kind)` | request (Medium) | Write the index table to the program log: `X_KIND_ALL`, `X_KIND_LIGHT`, `X_KIND_DEPLOYABLE`, `X_KIND_ACTUATOR` |

**Lights**

| Id | Prototype | Kind | Purpose |
| --- | --- | --- | --- |
| `0x5010` | `uint32_t x_light_count(void)` | request+result | Number of lights |
| `0x5011` | `uint32_t x_light_geti(uint32_t light, uint32_t prop)` | request+result | Integer property (table below) |
| `0x5012` | `float x_light_getf(uint32_t light, uint32_t prop)` | request+result | Float property (table below) |
| `0x5013` | `void x_light_set_on(uint32_t light, uint32_t on)` | request | Switch the light's part on or off |
| `0x5014` | `void x_light_set_color(uint32_t light, vec3 rgb)` | request | Emission colour |
| `0x5015` | `void x_light_set_cone(uint32_t light, float inner_rad, float outer_rad)` | request | Spot cone half-angles |
| `0x5016` | `void x_light_set_intensity(uint32_t light, float intensity)` | request | Brightness |
| `0x5017` | `void x_light_set_range(uint32_t light, float range_m)` | request | Range |
| `0x5018` | `void x_light_reset(uint32_t light)` | request | Restore the stock appearance (not the on/off state) |
| `0x5019` | `void x_lights_set_all(uint32_t on)` | request | Every switchable light, like the game's lights key but as a set |

**Deployables** — parts whose animation the stock UI shows as Deploy / Retract

| Id | Prototype | Kind | Purpose |
| --- | --- | --- | --- |
| `0x5020` | `uint32_t x_deploy_count(void)` | request+result | Number of deployables |
| `0x5021` | `uint32_t x_deploy_geti(uint32_t index, uint32_t prop)` | request+result | Integer property |
| `0x5022` | `float x_deploy_getf(uint32_t index, uint32_t prop)` | request+result | Float property |
| `0x5023` | `void x_deploy_set(uint32_t index, uint32_t deployed)` | request | Deploy (1) or retract (0) |
| `0x5024` | `void x_deploy_toggle(uint32_t index)` | request | Flip the commanded state; valid mid-travel |

**Actuators** — parts whose animation the stock UI shows as a 0–1 slider

| Id | Prototype | Kind | Purpose |
| --- | --- | --- | --- |
| `0x5030` | `uint32_t x_actuator_count(void)` | request+result | Number of actuators |
| `0x5031` | `uint32_t x_actuator_geti(uint32_t index, uint32_t prop)` | request+result | Integer property |
| `0x5032` | `float x_actuator_getf(uint32_t index, uint32_t prop)` | request+result | Float property |
| `0x5033` | `void x_actuator_set(uint32_t index, float value)` | request | Target position, 0.0–1.0 |

Property selectors. Each part kind has **one** enumeration; integer properties are values 0–15 and
float properties 16 and up, and each getter's argument domain is the matching sub-range
(`enum:x_light_prop[0..7]` for `geti`, `[16..23]` for `getf`), so the VM rejects a mix-up
instead of silently reading the wrong property — verified in spike 06, which used the earlier
`purr_` spelling: `rejected purr_light_geti argument 1 = 16: not in purr_light_prop[0..7]`. The gaps are filled with
`reservedN` entries in `enums.json` that get no `#define`. **The lists are positional and part of
the image format**: a new integer property replaces the next `reserved` entry and widens the
sub-range; nothing is ever inserted or reordered.

| Constant | Value | Getter | Meaning |
| --- | ---: | --- | --- |
| `X_LIGHT_PRESENT` | 0 | `geti` | 1 while the light is still on this vehicle |
| `X_LIGHT_HAS_SWITCH` | 1 | `geti` | 1 when the light has a usable switch: its part's switch is on this vehicle and the light is not an interior fill light |
| `X_LIGHT_ON` | 2 | `geti` | commanded state of the switch (1 for switchless lights) |
| `X_LIGHT_LIT` | 3 | `geti` | actually emitting: switched on **and** powered (always 1 for interior fill lights, which ignore both) |
| `X_LIGHT_TYPE` | 4 | `geti` | `X_LIGHT_TYPE_SPOT` 0, `X_LIGHT_TYPE_POINT` 1 |
| `X_LIGHT_PART_ID` | 5 | `geti` | the part's instance id (a session-only descriptor) |
| `X_LIGHT_SWITCH_GROUP` | 6 | `geti` | lowest light index sharing this light's switch |
| `X_LIGHT_OVERRIDDEN` | 7 | `geti` | 1 once a program changed its appearance |
| `X_LIGHT_INTENSITY` | 16 | `getf` | current intensity |
| `X_LIGHT_RANGE_M` | 17 | `getf` | current range |
| `X_LIGHT_COLOR_R` / `_G` / `_B` | 18 / 19 / 20 | `getf` | current colour components |
| `X_LIGHT_INNER_RAD` / `X_LIGHT_OUTER_RAD` | 21 / 22 | `getf` | current cone half-angles |
| `X_LIGHT_STOCK_INTENSITY` | 23 | `getf` | the part's authored intensity, as a reference for scaling |
| `X_DEPLOY_PRESENT` | 0 | `geti` | still on this vehicle |
| `X_DEPLOY_STATE` | 1 | `geti` | `X_STATE_NONE` 0 (no part: out of range, departed, bench), `X_STATE_DEPLOYED` 1, `_RETRACTED` 2, `_DEPLOYING` 3, `_RETRACTING` 4, `_BROKEN` 5 (never produced in this build). The game's own `DeploymentState` is 0–4 (Deployed first); `PartMath.FromGameDeploymentState` converts (decision D18) |
| `X_DEPLOY_COMMANDED` | 2 | `geti` | 1 when the goal is above zero |
| `X_DEPLOY_MOVING` | 3 | `geti` | 1 while position differs from the goal |
| `X_DEPLOY_PART_ID` | 4 | `geti` | part instance id |
| `X_DEPLOY_LINKS` | 5 | `geti` | bit 1: drives a solar panel; bit 2: also driven by a parachute; bit 4: aims a light |
| `X_DEPLOY_POSITION` / `_TARGET` | 16 / 17 | `getf` | 0–1 fraction of travel |
| `X_DEPLOY_DURATION_S` | 18 | `getf` | the animation's authored length in simulated seconds (a tracking solar array additionally rotates back to zero before it folds) |
| `X_ACTUATOR_PRESENT` / `_MOVING` / `_PART_ID` / `_LINKS` | 0 / 1 / 2 / 3 | `geti` | as above |
| `X_ACTUATOR_POSITION` / `_TARGET` / `_DURATION_S` | 16 / 17 / 18 | `getf` | as above |

Argument domains: indices are `any` (range-checked by the handler, where the vehicle is known);
selectors are `enum:` sub-ranges; every float is `finite` or `finite:>=0`, so the VM rejects NaN,
infinities and negative magnitudes before the mod sees them. Getters never reject on the index: an
out-of-range or departed slot reads 0, so `…_PRESENT` can be used to probe. **Programs must check
`…_PRESENT` before trusting another property of a part that may have left**: 0 is also a real value
of some properties (`X_LIGHT_TYPE` 0 is spot, `X_LIGHT_SWITCH_GROUP` 0 is light 0's group, a
`…_POSITION` of 0 is retracted). `X_DEPLOY_STATE` is the exception that needs no check: it reads
`X_STATE_NONE`, which no part has. Setters reject with an event in the program log.

The whole surface — every row, prototype and domain, called from kC and kASM, executed by the
game's VM against simulated parts — was exercised by spike 06
(`plans/investigations/kfs-spikes/06-service-catalog-and-sim/`).

Units and ranges:

| Quantity | Unit / range | Handler behaviour |
| --- | --- | --- |
| colour | RGB, each component nominally 0–1, passed to the game's light colour unconverted. The colour space is the game's own and is **unverified** (R22, checklist V29); the header says exactly that | clamped to [0, 1] |
| cone angles | **half-angles in radians**; outer in [1e-5, 1.5697963] | outer clamped to that band; inner clamped to [0, outer]; ignored for point lights |
| intensity | unitless, ≥ 0 (stock parts use 0.05–50) | clamped to [0, 1e6] (`PartMath.MaxIntensity`, decision D19) |
| range | metres, ≥ 0 | clamped to [0, 1e6 m] (`PartMath.MaxRangeM`, decision D19) |
| actuator value | 0–1 fraction of the animation's travel | clamped to [0, 1], multiplied by the duration |
| animation position / target | 0–1 fraction | — |
| durations | seconds of simulated time | — |

A complete program:

```c
/* Blink every light once a second and keep the first deployable open. */
#include <kfs.h>
#include <purrtty.h>
#pragma kfs tick_hz 10
#pragma kfs task run rate_hz=10

uint32_t ticks;

void run(void)
{
    uint32_t lights = x_light_count();
    uint32_t on = (ticks / 5u) % 2u;
    for (uint32_t i = 0u; i < lights; i++)
        if (x_light_geti(i, X_LIGHT_HAS_SWITCH))
            x_light_set_on(i, on);

    if (x_deploy_count() > 0u && x_deploy_geti(0u, X_DEPLOY_STATE) == X_STATE_RETRACTED)
        x_deploy_set(0u, 1u);

    ticks++;
}
```

### 4.2 Semantics a program author must know

| Topic | Behaviour |
| --- | --- |
| **Indices** | An index is the rank of the module by `InstanceId` in a list frozen for the program: lights are every `LightModule` on the vehicle (emitters, including interior ones); deployables are `KeyframeAnimationModule`s with `ShowDeployRetract` and a duration > 0; actuators are those without it. |
| **When lists are built** | When a run **starts** — load and start, `kfs reset`, `kfs stop` then `kfs start`, a start while running — so every run starts from a fresh, gap-free list, and when the program calls `x_parts_rescan()` (however many rescans one tick issues, one rebuild). Never spontaneously, and not while a computer is stopped. The rebuild happens at the run's first service call or the pump of the next frame, whichever is first — in the game both are before any part can change. `x_parts_info(X_PARTS_GENERATION)` changes each time. |
| **Parts that leave** | After staging or undocking the slot stays, `…_geti(i, PRESENT)` reads 0, other reads return 0, and writes are rejected with an event. |
| **Parts that arrive** | Docked-on parts have no index until the program rescans. `x_parts_info(X_PARTS_DIRTY)` is 1 when the vehicle's module counts differ from the frozen lists. |
| **One switch, several lights** | On/off is per part: emitters sharing a part (and an interior that inherits its switch) switch together. `X_LIGHT_SWITCH_GROUP` returns the lowest index sharing the switch. A light with no switch reads `HAS_SWITCH` 0, `ON` 1, and `x_light_set_on` on it is rejected. |
| **Appearance is per light** | The first colour/cone/intensity/range write gives that light a private copy of its template; other lights of the same part type are unaffected. `x_light_reset` restores the stock template. Overrides last until reset, reload or the part's destruction — they are not saved, and they are **not** reset when the program stops or a new run starts (decision D5); the console command `purr lights reset [all]` and the menu entry kFS › Reset light overrides restore the stock look by hand. (Switch states and animation goals, by contrast, are part of the game's own save.) |
| **Read-back timing** | A write is never visible in the tick that issued it. The game's frame order is: commit the last physics job → the pump applies queued commands → the next job starts, and inside it the program's ticks run **before** the parts are stepped (§4.12). So two kinds of value come back at different times. *Live fields* — a light's switch (`X_LIGHT_ON`), an animation's goal (`…_TARGET`, `X_DEPLOY_COMMANDED`), a light's colour, cone, intensity and range — read the new value from the first tick of the next frame: tick *k+1* at ordinary frame rates, later when several ticks run per frame. *Committed state* — `X_LIGHT_LIT`, `…_POSITION`, `X_DEPLOY_STATE` — is what the last finished physics job produced, so it follows **one frame later still**, and every tick of one frame reads the same committed values (at time warp a program sees a position move in steps of one frame). `…_MOVING` compares the committed position with the live goal, so it turns 1 together with the goal. `LIT` additionally needs electrical power, and is asymmetric in the game: switching **on** shows `LIT` 1 one frame later (it waits for the committed power state), switching **off** shows `LIT` 0 at once, because the game's test (`Part.IsLightSwitchedOff`) answers "off" from the switch field alone. A light without a switch always reads `LIT` 1. All of this, the asymmetry included, is reproduced in the simulator and pinned by `purrTTY.Kfs.Tests/Runtime/FrameOrderTests.cs`. In the game it is checklist item V7. |
| **Animations move at their own speed** | A target is a goal; position slews toward it at the animation's authored rate. Poll `POSITION` or `STATE`. A program that changes the target every tick keeps the part moving and the vehicle off rails. |
| **Budget** | Measured on the Bench processor: 22–25 cycles for a call with up to two scalar arguments, 55 for `x_light_set_color`, 40 for the two Medium calls. The binding limit is the VM's **256 service calls per tick**, `kfs_logf` included; calls beyond it are dropped with an event. |
| **Bench** | There is no vehicle: results are 0 and every call — getters too, since the VM queues a request for them as well — is rejected "no vehicle to act on" (optional patch 4 of §4.6 softens this). |
| **Paused** | Nothing ticks, so nothing is issued; commands already issued are still applied. |
| **Without the mod** | An image that uses these services does not load ("unknown service id"). |

### 4.3 Files

As built:

```text
purrTTY.Kfs/
  purrTTY.Kfs.csproj
  KfsExtension.cs              public entry: Install(Harmony, options, gameServices), Uninstall(), Status; the patch registry
  KfsExtensionOptions.cs       Enabled, DisabledOptionalPatches
  KfsExtensionStatus.cs        what applied, what did not, why; counters; the one log line; Describe() for `purr status`
  IKfsGameServices.cs          the seam between the installer and game-only code: Name, Prepare, Attach, Detach, Console, SystemHeadersInUse
  KfsBuildInfo.cs, KfsLog.cs   the id block and Owns(id); log-once
  Catalog/
    ServiceDef.cs              record: Id, Name, Kind, Params[], Result, CostClass, Summary, HostChecks, Doc
    ParamDef.cs                record: Name, CType, CellTypes[], Domain
    EnumDef.cs                 record: Key, Prefix, Values[] (with reserved fillers)
    ServiceCatalog.cs          a catalogue value (services + enumerations)
    XCatalog.cs                THE list of services and enums (ids in `Svc`; the only place ids appear)
    Selectors.cs               the selector and state enumerations shared with C#
    CatalogEmitter.cs          → services JSON rows, enums JSON entries, purrtty.h, purrtty.kinc, builtin names, markdown
    CatalogSelfCheck.cs        cell types and counts, grouping, sub-ranges, id block, duplicate names; parses the emitted header back
  Tables/
    TableAugmenter.cs          builds the augmented GameTables from the stock streams — pure, no statics
    TablesPublisher.cs         assigns KfsIntegration._tables, or installs the LoadEmbedded fallback
    SystemHeaderRegistration.cs  the one mechanism that adds and removes <purrtty.h> and the builtin names
    StockEditorProbe.cs        "is the stock editor building now?" without creating the workbench (§4.5, uninstall)
  Patches/
    KfsPatch.cs                one registry entry: name, target, required?, resolve, apply, remove
    ApplyRequestPatch.cs       postfix  VehicleComputer.ApplyRequest
    IssueResultPatch.cs        postfix  VehicleComputer : ISyscallHost.IssueResult
    BeforeInputEventsPatch.cs  postfix  KfsIntegration.BeforeInputEvents
    BenchApplyRequestPatch.cs  postfix  HostComputer.ApplyRequest          (optional)
    LoadEmbeddedFallbackPatch.cs  postfix  GameTables.LoadEmbedded         (only if the field is not found)
  Runtime/
    IVehicleParts.cs           the abstraction of §3.2
    VehiclePartsBase.cs        everything a back end shares: list publication, checks, derived properties, clamps, Apply
    PartSets.cs                immutable frozen lists (slots) + descriptors + generation
    PartCommand.cs             readonly struct: Service, Slot (object reference), U, A, B, C
    VehicleBinding.cs          per computer: parts, command queue (a ring), the run, event throttle, counters
    PartsRegistry.cs           VehicleComputer → VehicleBinding; creation under one lock; Open/Close
    BindingPump.cs             drain-and-apply for one binding — shared by the game pump and the test runtime
    ServiceRouter.cs           Answer(...) and Accept(...)
    PartMath.cs, PartsText.cs  pure clamps and conversions (unit-tested); the texts of events and rejections
    ProjectResolver.cs         which files make up a project, with unsaved-buffer overlays (B2, C2)
    ProjectCompiler.cs         compiles or assembles a resolved project as the workbench would (B2, C2)
    NeovimLaunchPlan.cs        executable, argv, environment, cwd as plain strings (D3; no terminal types)
  Game/
    KsaGameServices.cs         IKfsGameServices for the game: host directory, pump, dispatch fallback, light restore
    KsaVehicleParts.cs         IVehicleParts over a live Vehicle: collection, live checks, raw reads and writes
    KsaPartRules.cs            the decisions KsaVehicleParts makes, as pure functions (effective switch, states, keys)
    LightOverrides.cs          LightModule → (original template, private template); RestoreAll
    HostDirectory.cs           reads KfsVehicleHost.AllHosts under its lock
    GameHostSource.cs          the pump's view of the game: the live hosts (IsAlive), parts factory, ownership
    MainThreadPump.cs          once per frame on the main thread: discover hosts, call BindingPump, prune
    KsaConsoleHost.cs          the game console, controlled vehicle and workspace for the `purr` command
  Console/                     (namespace purrTTY.Kfs.Commands)
    PurrConsole.cs             the `purr` console command: registration, dispatch, ASCII output
    PurrVerbRegistry.cs        the verb table; Add(name, help, handler, needsExtension) → IDisposable
    PurrBuiltinVerbs.cs        help, status, parts, lights, examples, header, kinc, services
    PartsListing.cs            the index-table formatter
    ConsoleText.cs             ASCII stand-ins for typographic characters
    IKfsConsoleHost.cs         what the command needs from the game (console, vehicle, workspace)
  Examples/                    (namespace purrTTY.Kfs.ExamplePrograms)
    ExampleCatalog.cs          the list; writes the folders through the console host
    x_*/…                      embedded .c / .kasm resources, one folder per example
purrTTY.GameMod/
  Patcher.cs                   InstallKfs (KfsExtension.Install with KsaGameServices.Create()), the standing `purr`
                               registration, KfsExtension.Uninstall() before UnpatchAll
  TerminalMod.cs               starts and pumps the language server and the Neovim launcher (§6, §7)
  TerminalMenus.cs             + the "kFS" submenu hook (DrawKfsMenu)
  Kfs/KfsConfig.cs             <config>/.purrTTY/kfs.toml (own file; see §7.5)
  Kfs/KfsMenu.cs               the entries of the kFS submenu
```

### 4.4 The catalogue and its emitter

```csharp
public sealed record ParamDef(string Name, string CType, string[] CellTypes, string Domain);
public sealed record ServiceDef(
    ushort Id, string Name, string Kind, ParamDef[] Params,
    string? ResultCType, string? ResultCell,          // null for a request
    string CostClass, string Summary, string HostChecks, string Doc);
public sealed record EnumDef(string Key, string CPrefix, string[] Values);
```

`CatalogEmitter` produces, for one `ServiceDef`:

- the JSON row — `id`, `name`, `kc` (`` `name(p1, p2)` ``), `kscript` (`"—"`), `symbol`, `kind`,
  `args` and `results` prose, `arg_types` (the concatenated `CellTypes`), `result_types`,
  `cost_class`, `requires_stock: null`, `arg_domains` (the parameter's domain repeated once per
  cell), `host_checks`, `summary`;
- the prototype line and its doc comment for the header;
- the builtin name.

`CatalogSelfCheck` asserts, before anything is installed and again in tests:

1. ids are unique, inside `0x5000–0x50FF`, and absent from the stock table;
2. each parameter's `CellTypes` matches what its `CType` flattens to (`uint32_t` → `u32`,
   `float` → `f32`, `vec3` → `f32,f32,f32`) — **types, not just counts**: a `float` prototype
   against a `u32` cell compiles silently and sends float bits;
3. results are zero or one cell, and a `void` prototype has no result cell and vice versa;
4. the `kc` names group onto cells under the loader's rule — consecutive cells with the **same
   domain string** merge left to right — so adjacent scalar parameters of the same type must not be
   mistaken for one aggregate. Where two adjacent parameters share a domain and the count of names
   is less than the count of cells, the check fails and the definition must change;
5. every `enum:<key>` domain names an `EnumDef`;
6. cost classes are `Light`, `Medium` or `Heavy`.

The header is emitted with `\r\n` line ends like the stock entries, starts with `#pragma once` and
`#include <kfs.h>`, then the `#define`s for every enum value, then one commented prototype per
service. It contains **no function bodies**: an unused `static` helper would raise `KC-W006` in
every program that includes the header, and a non-static body breaks a two-file project
(`KC-E016`). Helpers, if any, are macros. Including the header adds no code to a program.

Every definition mistake below surfaces as an `InvalidOperationException` thrown out of the game's
compiler when a program calls the function — never as a diagnostic — which is why the self-check
exists and runs in CI (all reproduced in spike 06):

| Mistake | Exception text |
| --- | --- |
| prototype cell count ≠ row cell count | `<name>: 1 cells for 2 (kC §10.2 binding)` |
| multi-cell (struct) result | `RET 0 with 2 cells on the stack …` or `label … reached by fall-through with height 2 …` (builder invariant) |
| prototype returns a value, row has no result | `operand stack below the floor…` |
| prototype is `void`, row has a result | `RET 0 with 1 cells…` |

JSON details the emitter must respect: ids are written as plain integers (a `ushort`-typed JSON
value made the game's reader fail with `UInt16 cannot be converted to Int32` in spike 06, which would disable
the whole augmentation); no unknown keys (the reader rejects them); enum filler entries
(`reserved8`…) get no `#define`.

For kASM, whose `<kfs.kinc>` gains the `SVC_X_*` symbols automatically (the game generates that
include from the service rows on every assembly) but not the selector constants, the emitter also
produces `purrtty.kinc` (`.equ X_LIGHT_ON, 2` lines). **`purrtty.kinc` holds only the `X_*`
constants; the `SVC_X_*` symbols are never in it** — a kASM program needs `.include <kfs.kinc>`
for the service ids and `.include "purrtty.kinc"` for the selectors.
`purr examples` writes it next to the kASM example, and a pure-kASM project picks it up with
`.include "purrtty.kinc"` (verified through the game's `Workbench.Build` in spike 06). The
alternative — a postfix on the `BuiltinIncludes(GameTables, IoLayout, string)` constructor that
`TryAdd`s the constants so `<kfs.kinc>` carries them — also works but costs another patch target on
a constructor that runs for every assembly; it is decision D14.

### 4.5 Install sequence

`KfsExtension.Install(Harmony harmony, KfsExtensionOptions? options, IKfsGameServices? gameServices)`
— called from `Patcher.patch()` (`InstallKfs`) after the terminal's own patches, inside its own
`try`/`catch`, with the options read from `kfs.toml` and `KsaGameServices.Create()`. The game
services are everything that exists only in the running game (host discovery, the per-frame pump,
the dispatch-thread fallback, the console); the offline test runtime passes none and binds its
simulated vehicles itself. The extension shares the terminal's Harmony id (`purrTTY`); a failed
install is logged and leaves the terminal working.

1. Return immediately if the extension is switched off (`kfs.toml`, default on) or already
   installed.
2. Run `CatalogSelfCheck`. Failure → status `Disabled: catalogue`, stop.
3. Resolve every **required** patch target (§4.6) with `AccessTools`. Any `null` → status
   `Disabled: target <name> not found`, stop. Then ask the game services to prepare
   (`IKfsGameServices.Prepare`: in the game, find `KfsVehicleHost.AllHosts`); a reason refuses the
   install the same way, before anything has changed.
4. **Dry run the tables**: `TableAugmenter.Build(stock streams)` through the public
   `GameTables.Load(open, origin)`. This runs the game's own validation over the mod rows now, at
   install, instead of at the player's first `kfs` command. An exception → status
   `Disabled: tables rejected (<message>)`, stop. Keep the instance; its `Origin` is
   `"embedded:KSA+purrtty"`.
5. **Publish the tables.** `KfsIntegration.Tables` is
   `LazyInitializer.EnsureInitialized(ref _tables, GameTables.LoadEmbedded)`: it returns an existing
   value untouched. Read the private static `_tables` by reflection (a field read does not run the
   initialiser; nothing in the game reads the tables before mods are loaded):

   | `_tables` is… | Do |
   | --- | --- |
   | `null` | assign the instance from step 4 — the game's loader then never runs |
   | already ours (its `Origin` is our marker: a StarMap reload without an assembly unload keeps statics, as does a second install in one test process) | reuse it and continue |
   | someone else's (built before us) | status `Disabled: tables already built`, stop — the header must not be installed either, or programs would compile calls that then fail to load |
   | not found (renamed by a game update) | fall back to the mechanism proven in spike 03: a Harmony postfix on the public static `GameTables.LoadEmbedded` returning the instance from step 4; log that "already built" can no longer be detected |

6. Apply the required patches one by one; on the first failure unpatch the ones already applied
   and stop. (Tables published in step 5 stay: rows without hooks only reject.)
7. Install the header: `SystemHeaders.Files["purrtty.h"] = text` and add every builtin name.
8. Apply the optional bench patch; a failure is logged and ignored.
9. Register the `purr` console command (§4.13) when the game services bring a console. A failure
   to register is reported in the status and never undoes the install — the purrTTY menu still
   runs the verbs.
10. Attach the game services (`IKfsGameServices.Attach`: the pump becomes the one the
    `BeforeInputEvents` postfix runs, and its fallback binds from dispatch threads); an exception
    here undoes the whole install. Then, as the very last step, open the parts registry so that
    bindings may be created, and log one line:
    `purrTTY kFS extension: active — 22 services 0x5000–0x5033, 3 hooks (+1 optional), tables published, game back end, purr command`
    (the tables word is `published`, `reused` or `published through the LoadEmbedded fallback`;
    the line ends `, no purr command (<reason>)` when step 9 failed), or
    `purrTTY kFS extension: disabled — <reason>`.

`Uninstall()` (from `Patcher.unload()`, before `UnpatchAll`): remove the console command the
install registered, detach the game services (the pump stops; every private light template still
alive is restored), remove the patches (only its own), remove the header and builtin names, close
and clear the registry. The augmented tables stay published for the life of the process — with
the hooks gone their rows simply reject, and a later `Install` in the same process recognises them
(step 5, second row).

**The header is not removed while a compile may be running.** `SystemHeaders.Files` and
`SystemHeaders.Builtins` are a plain `Dictionary` and `HashSet` that the game's compiler reads on
whatever thread compiles — the stock kFS editor builds on a thread-pool thread — so changing them
under a running compile could corrupt them. At uninstall the header is therefore **left
registered** (one status and log line, `<purrtty.h> left registered: <why>`) when one of the
language server's compiles is in flight or the stock editor is building
(`IKfsGameServices.SystemHeadersInUse()`; the default probe, `Tables/StockEditorProbe.cs`, reads
the private `KfsIntegration._workbench` without creating it and checks `Editor.Building`). The
next install takes the leftover registration over. The mod's unload order makes the first case
rare: the language server stops, and joins its compile thread, before `Patcher.unload()` runs.

Consequences for tests: install tests reset `_tables` to `null` by reflection in their own set-up;
a "stock kFS" for comparison is built with `GameTables.Load` over the stock streams and passed
explicitly (`FakeVehicle`, `CompileInput` and `Workbench` all take the tables as an argument) —
never read through `KfsIntegration.Tables`.

### 4.6 The patches

| # | Target | Declaration | Patch | Thread | Purpose |
| --- | --- | --- | --- | --- | --- |
| 1 | `KSA.Kfs.Host.VehicleComputer.ApplyRequest` | `protected override bool ApplyRequest(ServiceRequest request)` | postfix, `VehicleComputer __instance, ServiceRequest request, ref bool __result` | dispatch | claim ids in the mod block that fell through |
| 2 | `KSA.Kfs.Host.VehicleComputer` method `KSA.Kfs.Vm.ISyscallHost.IssueResult` | `void ISyscallHost.IssueResult(ushort service, ReadOnlySpan<uint> args, Span<uint> results)` | postfix, same parameters + `__instance` | dispatch, inside `SYSCALL` | fill the result cell |
| 3 | `KSA.Kfs.KfsIntegration.BeforeInputEvents` | `public static void BeforeInputEvents()` | postfix | main, solver idle | run the pump |
| 4 *(optional)* | `KSA.Kfs.Host.HostComputer.ApplyRequest` | `protected virtual bool ApplyRequest(ServiceRequest request)` — the base that `VehicleComputer` overrides without calling | postfix | bench: main | a readable bench: getters silent, setters rejected "x_ services need a vehicle (the bench has no parts)" |
| fallback | `KSA.Kfs.Vm.Tables.GameTables.LoadEmbedded` | `public static GameTables LoadEmbedded()` | postfix, `ref GameTables __result` | — | only when `_tables` cannot be found (§4.5 step 5) |

Three required patches, all on methods with simple signatures; 1 and 2 are the ones exercised by
the spikes (P6). Patch 4 was exercised in spike 06 (it fired only for `BenchMachine`, never for a
`VehicleComputer`); it is **not** part of the all-or-nothing set — its target is a one-line virtual
the JIT may devirtualise and inline, and losing it only costs the nicer message. Console commands
need no patch (§4.13).

One more kFS method is patched, outside this table and outside `purrTTY.Kfs`: the language
server's optional `KfsErrorEchoPatch` (a prefix and a finalizer on `KSA.Kfs.KfsIntegration.Error`,
in `purrTTY.GameMod/Kfs/`, applied with the terminal's own optional patches; §6.7, R17). That
makes six kFS patch targets in all for an upgrade review: the five above and `Error`.

Bodies, as built (`Patches/ApplyRequestPatch.cs`, `IssueResultPatch.cs`, `BeforeInputEventsPatch.cs`):

```csharp
// 1
static void Postfix(VehicleComputer __instance, ServiceRequest request, ref bool __result)
{
    bool claimed = false;
    try
    {
        if (__result || request is null || !KfsBuildInfo.Owns(request.Id)) return;
        __result = claimed = true;                     // ours either way: never "no vehicle to act on"
        ServiceRouter.Accept(__instance, request);
    }
    catch (Exception e)
    {
        KfsLog.Once("accept hook", e);
        if (claimed) ServiceRouter.RejectAfterFault(__instance, null, request);   // rejected "internal error", never dropped silently
    }
}

// 2
static void Postfix(VehicleComputer __instance, ushort service, ReadOnlySpan<uint> args, Span<uint> results)
{
    try
    {
        if (results.Length == 0 || !KfsBuildInfo.Owns(service)) return;
        results[0] = ServiceRouter.Answer(__instance, service, args);
    }
    catch (Exception e) { KfsLog.Once("answer hook", e); if (results.Length != 0) results[0] = 0u; }
}

// 3
static void Postfix()
{
    try { Interlocked.Increment(ref _calls); MainThreadPump.Run(); }   // the stock body is a no-op while the flag is off; ours still prunes
    catch (Exception e) { KfsLog.Once("pump hook", e); }
}
```

`KfsBuildInfo.Owns(id)` tests the id block `0x5000–0x50FF`. `KfsLog.Once(key, e)` logs the first
occurrence per key and counts the rest. The patches are listed in one registry,
`KfsExtension.Patches` (name, target, required or optional, resolve, apply, remove), which is what
the installer walks.

### 4.7 Registry and binding

```csharp
static class PartsRegistry
{
    public static VehicleBinding Bind(VehicleComputer c, IVehicleParts parts);   // tests; the game pump
    public static VehicleBinding? Find(VehicleComputer c);                       // dispatch: lookup, then HostDirectory
    public static IReadOnlyList<VehicleBinding> Live { get; }                    // the pump's list
}
```

- A `ConditionalWeakTable<VehicleComputer, VehicleBinding>` for lookup, plus a strong list of live
  bindings the pump maintains. **All creation goes through one lock**, because a binding can be
  requested from three places at once — the pump (main, solver idle), `purr parts` (main, UI
  phase) and `Find` (dispatch) — and must be created exactly once.
- In the game, `MainThreadPump` binds every **live** host it has not seen to a
  `KsaVehicleParts(host.Vehicle)`. It sees the game through `GameHostSource`: `HostDirectory`
  copies `KfsVehicleHost.AllHosts` under that list's own lock (the list is `internal static`,
  reached once by reflection), and the source keeps only the hosts whose `IsAlive` is true.
- A host can be created and have a program loaded and ticked within one frame, before the pump has
  seen it. `Find` therefore falls back, on the dispatch thread, to `HostDirectory` to locate the
  live host whose `Computer` is this computer and binds it there (`MainThreadPump.BindFromDispatch`,
  installed as `PartsRegistry.Fallback`). Stock kFS itself builds its controller sets on the
  dispatch thread when the layout changes.
- Lists belong to a **run**, not to an image: `VehicleBinding.SyncRun` notices a new run by the
  computer's `TickGrid` and rebuilds the lists then (§8.6, "Runs, not images"). Binding touches
  nothing of the computer; `computer.ImageLoaded` is not chained.
- Removal: **the game's own `ReleaseDead()` does not always run before the postfix.** It is inside
  `if (KfsPrototype.Enabled)` in `KfsIntegration.BeforeInputEvents`, so while the kFS switch is off
  a dead host stays in `AllHosts`. The pump therefore does not rely on it: `GameHostSource` filters
  by `IsAlive` (vehicle not disposed, in the current star system, still pointing at this host), and
  the pump **prunes every binding it owns whose host was not among the live ones this frame**
  (their queued commands are discarded, as stock discards main-thread work of a dead host). Only
  bindings made for game hosts are pruned (`IPumpHostSource.Owns`). The weak table needs no
  explicit removal.

`VehicleBinding`:

```csharp
sealed class VehicleBinding
{
    public const int QueueLimit = 4096;
    public IVehicleParts Parts { get; }
    PartCommand[] _ring;                          // a ring buffer under a lock, grown by doubling up to QueueLimit
    public void Enqueue(in PartCommand c);        // dispatch: O(1); when QueueLimit commands wait, the oldest is overwritten (one event per drain)
    public List<PartCommand> Take();              // main: copy the ring in order into the spare list
    public void Recycle(List<PartCommand> l);     // main: clear and keep as spare
    public bool ShouldReport(BindingEvent kind, ulong k, int tickHz);   // rate limit for mod-originated events, per run second
}
```

A `PartCommand` carries the **slot object** resolved when the request was accepted (the
`LightSlot` / `AnimSlot` holding the module reference), not an index. Lists can be rebuilt between
accept and apply — by a new image, or by a rescan earlier in the same batch — and a stored index
would then address a different part.

### 4.8 Part sets

```csharp
sealed class PartSets            // immutable once published
{
    public int Generation;
    public LightSlot[] Lights;   // { LightModule Module; int SwitchGroup; uint PartId; string PartName; string Key; }
    public AnimSlot[] Deployables, Actuators;   // { KeyframeAnimationModule Module; uint PartId; string PartName; string Key; uint Links; }
}
```

Build rule (`KsaVehicleParts.Rebuild`, callable from either thread, guarded by a lock; publishes
with `Volatile.Write`):

1. `vehicle.Parts.Modules.Get<LightModule>().ToArray()`, order by `InstanceId`.
2. For each light compute `SwitchGroup` = lowest index whose effective switch (§4.10) is the same
   object (itself when it has none), and capture `PartId = Parent.FullPart.InstanceId`,
   `PartName = Parent.FullPart.DisplayName`, `Key = TemplateId` for the console listing.
3. `vehicle.Parts.Modules.Get<KeyframeAnimationModule>().ToArray()`, order by `InstanceId`, drop
   `Shared.Duration <= 0`, split by `ShowDeployRetract`.
4. `Links` bit flags from the part's subtree: 1 = drives a `SolarPanel`, 2 = shared with a
   `Parachute` (its state machine also writes the goal), 4 = carries a `LightModule` (an aimable
   light).
5. `Generation = previous + 1`.

"Present" is evaluated live, not stored: `ReferenceEquals(module.Parent.Tree, vehicle.Parts)`.

### 4.9 The router

`Answer` (dispatch thread, inside `SYSCALL`) — one `switch` on the id; every branch is a bounds
check plus reads through `binding.Parts`:

```csharp
public static uint Answer(VehicleComputer c, ushort id, ReadOnlySpan<uint> a)
{
    VehicleBinding? b = PartsRegistry.Find(c);
    if (b == null) return 0u;
    IVehicleParts p = b.Parts;
    switch (id)
    {
        case Svc.LightCount:  return (uint)p.Sets.Lights.Length;
        case Svc.LightGetI:   return p.LightGetI((int)a[0], (LightProp)a[1]);
        case Svc.LightGetF:   return Bits(p.LightGetF((int)a[0], (LightProp)a[1]));
        // … one line per result-bearing service
        default: return 0u;
    }
}
```

Getters never reject on the index: an out-of-range or departed slot reads 0.

`Accept` (dispatch thread, after the tick) — validates what can be validated without touching the
game, then queues. The VM queues a request for **result-bearing calls too**, so getters arrive here
after having been answered; they are claimed and ignored (unclaimed, they would be rejected
"no vehicle to act on").

| Check | On failure |
| --- | --- |
| no binding (should not happen) | `c.RejectService(r, "no vehicle parts are bound")` |
| index ≥ list length | `RejectService(r, "light 9 out of range (this vehicle has 4)")` |
| slot not present | `RejectService(r, "light 2 is no longer on this vehicle")` |
| `x_light_set_on` on a light without an effective switch (§4.10) | `RejectService(r, "light 5 has no switch")` |
| `x_parts_describe` | not queued: one event line per slot from the captured descriptors. `VehicleComputer.Event` is not rate-limited by the host, so the router allows one describe per second of the run per computer |
| the back end (or the router) throws while checking | `RejectService(r, "internal error")`, counted in `sys.rejected_requests`; the fault is counted and logged once — a request is never dropped silently |

Everything else — including `x_parts_rescan`, so that it takes effect in order with the
commands around it — becomes a `PartCommand` with the slot resolved and the floats decoded
(`BitConverter.UInt32BitsToSingle`). A rescan applied by the pump rebuilds the lists on the main
thread — once per pump however many rescans the batch holds, since commands hold their slots and
the rebuild's position in the batch cannot be observed — and reports through the computer's queue:
`parts rescanned: 4 lights, 2 deployables, 1 actuator (generation 3)`; the program sees the new
lists from its next dispatch.

`RejectService` is the stock path: it increments `sys.rejected_requests` (visible to the program
from the next tick; issue-time domain rejections count in the same tick) and writes
`rejected <name>: <reason>` to the program log. The log admits 20 host events and 10 program lines
per second, so a program must rely on the counter, not the text.

### 4.10 Game back end — lights

All writes run in the pump, on the main thread. From
[control recipes](../docs/kfs-info/game-targets/control-recipes.md):

| Command | Game write |
| --- | --- |
| set on | `switch.LightIsActive = on`, where `switch` is the light's *effective switch* (below), read live each time |
| set all | `vehicle.LightsOn = on`; every `PowerConsumer` on this vehicle with `LightSwitch` gets `LightIsActive = on` |
| colour | private template, then `ColorRgb.R/G/B`, `IndexedColor = Invalid`, `OnDataLoad(null)` |
| cone | private template, then `OuterAngle.Value`, `InnerAngle.Value` with the clamps of §4.1 |
| intensity / range | private template, then `Intensity.Value` / `Range.Value` |
| reset | if the module's template is still ours, put the original back; forget the override |

**Effective switch.** `LightModule.IsActive` is unconditionally true for a light whose template has
`DisableInIva` (interior fill lights ignore switch and power), and the game ignores a switch whose
part sits on a different part tree. So:

```csharp
static PowerConsumer? EffectiveSwitch(Vehicle vehicle, LightModule m)
{
    if (m.Template.DisableInIva) return null;
    PowerConsumer? sw = m.Parent.FullPart.LightSwitch;
    return sw != null && ReferenceEquals(sw.Parent.Tree, vehicle.Parts) ? sw : null;
}
```

As built the rule is the pure function `KsaPartRules.EffectiveSwitch`, called by
`KsaVehicleParts` with the member path above (confirmed against the decompiled `Part`,
`PowerConsumer` and `LightModule`); there is no `LightOps.cs` or `AnimationOps.cs` — the raw reads
and writes are in `KsaVehicleParts`, the decisions in `KsaPartRules`, and everything shared with
the simulated back end in `Runtime/VehiclePartsBase.cs`.

`HAS_SWITCH` = effective switch is not null; `ON` = its `LightIsActive` (1 when there is none);
`LIT` = `m.IsActive`; `x_light_set_on` is rejected when there is none. A switch on another
vehicle is never written. `LIT` says whether the light emits, not whether the camera shows it: an
interior fill light (`DisableInIva`) reads 1 even in the IVA view, where the game does not draw it.

`LightOverrides.EnsurePrivate(LightModule m)`:

- keeps `ConditionalWeakTable<LightModule, Override { Original, Mine }>`;
- if there is no entry, or `m.Template` is no longer `Mine` (another mod replaced it), clones the
  **current** template: a field-by-field shallow clone of `LightModule.TemplateData` (reflection
  over the type hierarchy, so a field the game adds later is carried along — the approach gatOS
  uses), then fresh `FloatReference` objects for `Range`, `Intensity`, `InnerAngle`, `OuterAngle`
  and a fresh `ColorRgbReference` with `OnDataLoad(null)` called, then one assignment
  `m.Template = clone`;
- reads on the dispatch thread see either the old or the new template object, both complete.

Reads (`LightGetI` / `LightGetF`) are single field reads through `m.Template` and the switch, plus
`m.IsActive` for `LIT`; no allocation.

What the game saves and what it does not: the switch state is part of the stock save (as the
simulated "active" flag, so a commanded-on but unpowered light reloads off); template values are
never saved, so appearance overrides revert on reload.

When overrides end (decision D5): a program's `x_light_reset`; the player's `purr lights reset`
(the controlled vehicle) or `purr lights reset all` (every vehicle), also in the menu as
kFS › Reset light overrides; and `LightOverrides.RestoreAll()` when the extension is uninstalled
(mod unload). **Nothing resets them automatically** when a program stops, traps, is reloaded or a
new run starts: a light keeps the look it was given, like any other hardware. `purr parts` marks
such lights `overridden` and says how to restore them.

### 4.11 Game back end — animations

| Command | Game write |
| --- | --- |
| deploy set | `m.TimeGoal = deployed ? m.Shared.Duration : 0f` |
| deploy toggle | `m.TimeGoal = m.TimeGoal > 0f ? 0f : m.Shared.Duration` |
| actuator set | `m.TimeGoal = clamp01(v) * m.Shared.Duration` (the VM has already rejected non-finite `v`) |

Reads: `TARGET` = `TimeGoal / Duration`; `POSITION` and `STATE` from the committed
`KeyframeAnimationModule.State` of the module's own tree (bounds-checked, with the goal-derived
fallback gatOS uses); `MOVING` = goal ≠ position; `COMMANDED` = goal > 0.

Never written: `KeyframeAnimationModule.State` and `PowerConsumerState` — they are outputs of the
physics step.

### 4.12 The pump

Two pieces, so that the part every test exercises is the part the game runs:

- `Runtime/BindingPump.DrainAndApply(VehicleBinding b)` — start a new run if the computer began
  one (fresh lists and rate limits), take the queued commands, apply each through
  `b.Parts.Apply(command)` inside its own `try`/`catch` (the batch's rescans coalesced into one
  rebuild), recycle the list. The test runtime's `SimVehicle.Frame` calls exactly this.
- `Game/MainThreadPump.Run()`, once per frame from patch 3 — discovery and pruning around it:

```text
hosts = GameHostSource.Snapshot()                // copy of AllHosts under its lock, then only hosts with IsAlive (§4.7)
for each host in hosts, ordered by Vehicle.Id:   // ordinal, the order stock uses
    binding = PartsRegistry.TryGet(host.Computer) ?? bind new KsaVehicleParts(host.Vehicle)
    mark binding seen
    BindingPump.DrainAndApply(binding)
drop every binding of a game host not seen this frame   // its host is gone; queued commands are discarded
```

`MainThreadPump` is an instance over an `IPumpHostSource` (the game's is `GameHostSource`; tests
drive the same pump with `SimHostSource`), and the static `MainThreadPump.Run()` runs the active
one. A host whose parts cannot be built, or whose drain throws, is skipped for that frame and
logged once; `RunFrame` itself never throws.

It runs every frame, including while paused and while the kFS flag is off (so pruning still
happens); with no hosts it is one lock and an empty loop. With a host and no purr calls: one lock,
one small array copy and one lookup.

**Where the pump sits in the game's frame** (read from the decompiled `Program.PrepareFrame`,
`Universe` and `PhysicsBubble`; see
[dispatch and threading](../docs/kfs-info/integration/dispatch-and-threading.md)):

```text
main thread   VehicleSolver.Wait()                       the previous physics job has finished
              Universe.ApplyVehicleSolvers()             its results are committed to the live vehicles
              KfsIntegration.BeforeInputEvents()         stock kFS main-thread work, then THE PUMP
              InputEvents.ApplyInputEvents()             requested decouple, dock and undock happen here
              Universe.ExecuteNextVehicleSolvers(…)      the next job starts
solver thread   per vehicle: the kFS dispatch (the program's ticks), THEN the module update that steps the parts
```

So a command a program issues in one frame is applied by the next frame's pump, and the ticks that
follow see the new switch, goal or appearance at once (they are fields of live objects) but still
the old committed state (`LIT`, `POSITION`, `STATE`), which the job they run in has not produced
yet (§4.2, "Read-back timing"). Structure changes — staging, docking, undocking, a part destroyed
— all happen on the main thread in this same solver-idle window, either in `ApplyVehicleSolvers`
before the pump (contact docking, destruction, part failure) or in `ApplyInputEvents` after it
(decouple, dock and undock requested by the player or a program), never while a dispatch runs.
The simulator runs its frame in the same order (§5.4).

Failures found at apply time (a part that left between accept and apply) are reported to the
program with `host.Computer.Enqueue(c => ((VehicleComputer)c).Event(text))`, which runs on the
dispatch thread at the next dispatch; throttled by `ShouldReport`.

### 4.13 Finding out which index is which

Indices mean nothing to a player without a listing. Three ways, all from one formatter:

1. **Console:** the mod registers its own console command, `purr`, through the game's command
   registration — the same call kFS makes for `kfs`
   (`terminal.RegisterCommand(delegate, owner)` on a method carrying
   `[TerminalAction("purr", "<help>", ArgParseMode.Unparsed)]`). No patch, its own `purr help`,
   and it works whether or not the kFS flag is on. `purr parts` prints, for the controlled
   vehicle (an illustration in the format of `PartsListing`: column widths follow the longest
   name, and the console shows the text in ASCII, see below),

   ```text
   vehicle "Explorer 1" — parts as its running program sees them (generation 2)
   lights (3): type, switch, on/off, lit/dark
       0  Spotlight Mk1     spot   switch group 0   on   lit
       1  Spotlight Mk1     spot   switch group 0   off  dark  overridden
       2  Cabin (interior)  point  no switch             lit
     overridden: a program's colour, cone, intensity or range, kept after it stops; purr lights reset restores the stock look
   deployables (1): state, position, travel time
       0  Solar Array XL  retracted      0%    8.0 s  drives a solar panel
   actuators (1): position → target, travel time
       0  Cargo Bay 2m  0.25 → 0.75    6.0 s
   ```

   The heading says which lists are shown: those of the running program, those the vehicle's kFS
   computer holds (`…; a program started now gets fresh lists`), or — for a vehicle without a
   computer — a preview (`parts as a program started now would see them`). A slot whose part has
   left reads `not present`, and a line under the heading says when the vehicle has changed since
   the lists were built.

   The verbs, as built (`purr` alone is `purr help`):

   | Verb | Does | Needs the extension active? |
   | --- | --- | --- |
   | `purr help` | lists the verbs, and says whether the extension is active | no |
   | `purr status` | the status line, hooks, tables, back end, console, and the counters (requests accepted / rejected / answered, commands applied / failed / discarded / dropped, bench claimed / rejected, pump frames, logged failures) | no |
   | `purr parts [lights \| deploy \| actuators]` | the index tables above | yes |
   | `purr lights reset [all]` | restores the stock look of every overridden light of the controlled vehicle, or with `all` of every vehicle (§4.10) | yes |
   | `purr examples [--force]` | writes the example programs into the workspace (§4.14) | yes |
   | `purr header` | prints `<purrtty.h>` | no |
   | `purr kinc` | prints `purrtty.kinc` | no |
   | `purr services` | one line per `x_` service: id, prototype, summary | no |
   | `purr lsp [token \| restart]` | the language server: address, clients, workspace, target; its token; restart it (§6.7, §7.5). Added by the mod's `KfsLspService` | no |
   | `purr nvim [file]` | opens a workspace file, or the workspace, in Neovim (§7.2). Added by the mod's `KfsNeovimLauncher` | no |

   A verb that needs the extension answers
   `purr <verb>: the purrTTY kFS extension is not active (<status>); purr status says more` when
   it is not. Other code adds verbs through
   `PurrConsole.Verbs.Add(name, help, handler, needsExtension)`, which returns an `IDisposable`
   to remove the verb again; a handler receives a `PurrContext` (`Verb`, `Args`, `Words`, `Print`,
   `Host`, `ExtensionActive`).

   **The command exists for as long as the mod is loaded**, whether or not the extension
   installed: `Patcher` registers it itself after the install attempt (`PurrConsole.Register`, the
   "standing" registration) and removes it at unload, so with `enabled = false` in `kfs.toml`
   `purr status`, `purr lsp` and `purr nvim` still work. The command is filed under one
   process-wide owner object (kept in `AppDomain` data, `KsaConsoleHost.SharedOwner`), because
   the game's console replaces a command registered again under the same owner but its `help`
   throws when two owners hold the same name.

   Output goes through `KfsIntegration.Print`, so it lands wherever `kfs` output lands, including
   purrTTY's game-console shell. **It is ASCII by default** (`PurrConsole.AsciiOutput`,
   `ConsoleText.ToAscii`: `—` becomes `-`, `→` becomes `->`, `…` becomes `...`, and so on),
   because whether the console's font has those characters cannot be checked from the mod; part
   names, which are the game's own text, are passed through unchanged.
2. **From a program:** `x_parts_describe(X_KIND_ALL)` writes the same table to the program
   log.
3. **Reference window:** the Services page lists the mod rows with their summaries automatically,
   because the tables were augmented before the window is first built.

### 4.14 Examples

`purr examples` writes the examples into the workspace through the public `Workspace.Write` (never
automatically, like the stock examples). **Each example is its own folder**, one level deep: a
workspace folder is one project, so two programs in one folder would be compiled together and
collide. A file that already exists is kept and reported (`kept <path> (exists; purr examples
--force overwrites it)`); `purr examples --force` overwrites. Line ends are `\r\n`, like the
game's own examples. The command ends with how to run one:
`kfs on; kfs load x_blink/blink.c --vehicle; kfs start`.

| Folder / file | Shows |
| --- | --- |
| `x_blink/blink.c` | counts, `PRESENT` and has-switch checks, switch groups, on/off once a second |
| `x_light_show/light_show.c` | colour wheel, breathing cone and intensity ramp on the spot lights; `kfs poke stop 1` ends the show and resets every light once (`kfs poke stop 0` starts it again) |
| `x_deploy/deploy_when_safe.c` | subscribes `MSG_FLIGHT_DATA` (its `in_atmosphere` field; needs `#include <vehicle.h>`); deploys the solar arrays above the atmosphere, retracts them below; `LINKS` |
| `x_actuator/actuator_sweep.c` | triangle wave on actuator 0, read-back of position at each turn |
| `x_parts/parts_list.c` | `x_parts_describe`, generation and dirty handling, rescan |
| `x_blink_kasm/blink.kasm` + `x_blink_kasm/purrtty.kinc` | `x_blink` in kASM; `purrtty.kinc` is generated from the catalogue when the examples are written, never stored |

The sources are embedded resources of `purrTTY.Kfs` (`Examples/<folder>/…`, listed by
`ExampleCatalog`). Each is also a test input (§4.16, `ExampleProgramTests`).

### 4.15 Status and logging

`KfsExtensionStatus` records: active or the disabled reason; hooks applied (required, optional,
and optional ones skipped with the reason); how the tables were published; services and id range;
the game back end; whether the `purr` command is registered; and counters — vehicles bound,
requests accepted, rejected and answered, commands applied, failed, discarded and dropped, router
faults, bench requests claimed and rejected, pump frames, logged failures. `purr status` prints
all of it (`KfsExtensionStatus.Describe()`); the purrTTY menu shows the one-line summary. A
non-zero `pump frames` is the evidence that the `BeforeInputEvents` hook fires, and non-zero
`accepted` and `answered` that the two host hooks do (§10, V2). Logging goes through
`purrTTY.Logging` at most once per distinct failure.

### 4.16 Tests (all offline, in `purrTTY.Kfs.Tests`)

| Test | Asserts |
| --- | --- |
| `Catalog_SelfCheck_Passes` | §4.4 rules on the real catalogue |
| `Tables_Augmented_LoadAndValidate` | the game's loader accepts every row and enum; stock rows unchanged; both table versions unchanged |
| `Stock_Examples_Compile_Identically` | every stock example (`Examples.Files`) compiles to **byte-identical** images with stock and with augmented tables |
| `Header_Compiles_Alone` | a program that only includes `<purrtty.h>` builds; code size equals the same program without it |
| `Every_Service_Is_Callable` (one case per service) | compile a kC call, run on a `FakeVehicle` bound to `SimVehicleParts`, assert the decoded command or the returned value |
| `Kasm_Symbols_Resolve` | `SYSCALL SVC_X_…` assembles and arrives |
| `Domains_Reject` | NaN, negative intensity, invalid selector → issue-time rejection text; out-of-range index → host rejection text |
| `Getter_Selector_Ranges_Are_Disjoint` | `x_light_geti(i, X_LIGHT_INTENSITY)` and `x_light_getf(i, X_LIGHT_ON)` are rejected at issue time |
| `Two_Vehicles_Are_Independent` | one program on two sim vehicles with different part counts sees each vehicle's own lists |
| `Examples_Run` | each shipped example runs N simulated seconds against a sim vehicle and reaches its documented end state |
| `Bench_Is_Readable`, `Bench_Without_Optional_Patch` | on `BenchMachine` with patch 4: getters silent and 0, setters rejected with the bench text; without it: the stock "no vehicle to act on" for every call |
| `Patches_Apply_To_Distinct_Targets` | every registered target resolves and patches (the probe the compatibility reviews already run for the terminal patches) |
| `Install_Is_All_Or_Nothing` | with a deliberately broken catalogue, a missing target, or foreign tables already published, nothing is installed and the status says why |
| `Install_Reuses_Its_Own_Tables`, `Install_Is_Idempotent`, `Fallback_Postfix_Publishes_Tables` | a second install in one process works; the `LoadEmbedded` fallback publishes the same tables when the field lookup is forced to fail |
| `Rescan_Applies_In_Order` | commands accepted before a rescan still reach the part they named |
| `Router_Does_Not_Allocate`, fault injection | no per-call allocation inside `Answer`/`Accept` after warm-up; a throwing back end never faults the computer |
| `Uninstall_Restores` | header, builtins and patches are gone |
| `PartMath_*` | clamps, cone ordering, fraction conversions |

The later tasks added, in the same project: `KsaPartRulesTests` and `LightOverridesTests` (the
game back end's decisions and the template cloning, without a vehicle), `Runtime/MainThreadPumpTests`
(the game's pump over simulated hosts: discovery, order, pruning, the dispatch fallback),
`Runtime/FrameOrderTests` (§4.2 read-back timing), `PartsListingTests`, `PurrConsoleTests` and
`Runtime/PurrCommandTests` (the command, its registration states and every verb),
`Runtime/ExampleProgramTests` (every shipped example, built as its own folder project and run to
its documented end state), and `Installer/HeaderInUseTests` (the header is left registered while
a compile runs, §4.5).

What these cannot cover — the game-facing members of `KsaVehicleParts`, `GameHostSource`,
`HostDirectory` against the real host list, `KsaConsoleHost`, the pump against real hosts — is
deliberately thin, mirrors code two existing mods run in game, and is covered by the in-game
checklist (§10).

### 4.17 If services turn out not to be enough

- **Many values every tick** (say, 30 lights' state at 50 Hz): add one fixed-size host-published
  message (`MSG_X_LIGHTS`: count + 16 slots) packed by the mod and delivered with
  `KvmMachine.WriteMessage` from a postfix on `VehicleComputer.PlayerTick`. Needs a catalogue row,
  `_hostPublished` membership at load, and care with the leaf sampler. See
  [extension-point map](../docs/kfs-info/modding/extension-points.md) §5.
- **Native `commands.light[i]` syntax**: only with a layout change, which invalidates every image
  and requires full control. Not recommended.

## 5. Workstream B — the extended kFS as an external test runtime

### 5.1 What it is

A library, `purrTTY.Kfs.Testing`, and a command-line tool, `purr-kfs`, that run **the game's real
kFS** — tables, kC compiler, kASM assembler, kVM, host layer — together with **the mod's real
extension** — catalogue, table augmentation, header, Harmony hooks, router — in an ordinary .NET
process, with a simulated vehicle. It serves three audiences:

| Audience | Use |
| --- | --- |
| This repository's CI | End-to-end tests of every mod service on every platform leg, with no game process (§4.16). |
| Mod and tool developers | A fixture for testing anything built on kFS (the language server uses it). |
| Players who write kFS programs | `purr-kfs check` and `purr-kfs run` to compile and exercise a program against a described vehicle before flying it. |

It does not simulate flight dynamics. It simulates what the mod adds (lights, animations) and lets
a test **script** what the game would publish (navigation and flight-data messages) and **inspect**
what a program requests of the stock game (staging, throttle, attitude, burns).

`KSA.dll` is never redistributed: every entry point takes the directory that holds the player's or
developer's own copy (`KSA_DLL_DIR`, the same variable the build already uses).

### 5.2 Loading the game assemblies

`KsaAssemblyResolver.Install(string ksaDllDir)` registers the `AssemblyLoadContext.Default.Resolving`
handler proven in spike 04: resolve `<name>.dll` from that directory, and when the file's PE machine
field is x64 and the process is arm64, patch the two bytes **in memory** before
`LoadFromStream`. Nothing is copied and nothing on disk is modified.

Rules that keep this working:

- The new projects reference the game assemblies with `<Private>false</Private>`, so `KSA.dll` is
  absent from their output folders and resolution always reaches the handler.
- `KsaAssemblyResolver` lives in its own file with **no game types in its signatures or fields**,
  and must run before any method that mentions a game type is JIT-compiled. In test assemblies it
  is installed from a `[ModuleInitializer]` (`purrTTY.Kfs.Tests/ModuleInit.cs`). Task F1
  established the limits under `dotnet test`: the initializer runs before any test body but **not**
  before NUnit reflects over the assembly, so **test code may mention game types only inside
  method bodies and helper-method signatures** — never as field or property types, base types or
  interfaces (fakes implementing game interfaces live in `purrTTY.Kfs.Testing`), test-method
  parameters, attribute arguments, `yield` element types, or game structs captured by lambdas or
  held across `await`. Breaking the rule makes the whole test assembly fail to load, which
  `dotnet test -v quiet` reports as *success with no tests*; the project's runsettings therefore
  set `TreatNoTestsAsError`. Fixtures that need the game derive from `GameFixture`, which skips
  silently when no game directory is configured and fails when one is configured but broken. In
  the CLI the resolver runs in `Main` before dispatching to a `[MethodImpl(NoInlining)]` method.
- The directory comes from, in order: an explicit argument, `KSA_DLL_DIR`, an assembly-metadata
  value the test project stamps from `$(KSAFolder)` at build time.
- A missing directory or `KSA.dll` fails with one clear message; tests then `Assert.Ignore()`
  silently only when the variable is absent on a machine that never had the game DLLs (never in CI,
  which checks them out).

### 5.3 The runtime object

```csharp
public sealed class KfsRuntime : IDisposable
{
    public static KfsRuntime Start(KfsRuntimeOptions? options = null);   // installs the extension into this process
    public GameTables Tables { get; }                                   // the augmented tables
    public KfsExtensionStatus Extension { get; }
    public BuildResult BuildKc(IReadOnlyDictionary<string, string> files, IoLayout layout);
    public BuildResult BuildKasm(string file, string text, IoLayout layout);
    public SimVehicle CreateVehicle(SimVehicleSpec spec);
    public BenchMachine CreateBench();
    public void Dispose();                                              // uninstalls hooks, header, builtins
}
```

`Start` creates a Harmony instance with a unique id, calls `KfsExtension.Install`, and reads
`KfsIntegration.Tables` so the augmented instance exists. A stock kFS for comparison tests is
`KfsRuntime.StockTables` — built with `GameTables.Load` over the stock streams and passed
explicitly to a `FakeVehicle` or a build; `KfsIntegration.Tables` stays augmented for the life of
the process. Because Harmony patches and `SystemHeaders` are
process-wide, a process hosts **one** `KfsRuntime` at a time; the test assembly is marked
non-parallelizable and creates it once in the `[SetUpFixture]`.

### 5.4 The simulated vehicle

```csharp
public sealed class SimVehicle
{
    public string Id { get; }
    public FakeVehicle Fake { get; }              // the game's own stand-in: VehicleComputer + generated layout
    public SimVehicleParts Parts { get; }         // the IVehicleParts back end, bound in PartsRegistry
    public long TimeNs { get; }

    public LoadResult Load(byte[] image);
    public void Start();
    public void Run(double seconds, double frameDt = 1.0 / 60.0);   // per frame: parts.Step, pump, dispatch
    public void Frame(double frameDt);                               // one frame, for step-wise tests

    public IReadOnlyList<LogLine> Log { get; }                       // kfs_logf lines and host events
    public IReadOnlyList<ServiceRequest> StockRequests { get; }      // stage, throttle, holds, burns… as issued
    public T ReadGlobal<T>(string name) where T : unmanaged;         // program variables by name
    public Func<long, HostSample>? Telemetry { get; set; }           // scripted navigation / flight data
    public void Pause(); public void Resume();
}

public sealed record SimVehicleSpec
{
    public int Thrusters, Engines, Gimbals;
    public List<SimLightSpec> Lights;             // name, type, has switch, switch group, powered, stock values
    public List<SimAnimationSpec> Deployables, Actuators;   // name, duration, initial position, links
}
```

One simulated frame, mirroring the game's order (§4.12): **step the parts** by `frameDt`
(`SimVehicleParts.Step`: the physics job that ran since the last dispatch, with the goals and
switches as they were before this frame's pump — animations slew toward their goals; lit follows
on and powered; the game commits these results at the start of the frame) → **pump**
(`BindingPump.DrainAndApply`, the same code the game's pump calls, applying queued commands to
the live fields of `SimVehicleParts`) → **dispatch** (`VehicleComputer.Dispatch(timeNs, sample)`),
which runs the ticks that are due, all of them seeing the same part state. The first dispatching
frame after a start dispatches at the start time without stepping. This is **step → pump →
dispatch**; the first version of the simulator ran pump → step → dispatch, which made a program
see committed state (`LIT`, `POSITION`, `STATE`) one frame earlier than the game shows it, and
was corrected after the frame order was traced in the decompiled sources
(`purrTTY.Kfs.Tests/Runtime/FrameOrderTests.cs`). `LIT` follows the game's own expression — the
live switch and the committed power state — so a light switched **off** reads not lit at once
and one switched **on** reads lit a frame later (§4.2). Requests the stock switch accepts are taken with
`TakeServiceRequests()` and recorded; `stage` is acknowledged with `StageApplied()` so programs
that stage do not stall (spike observation E12).

`SimVehicleParts` implements `IVehicleParts` with plain fields and the same clamps as the game
back end (both call `PartMath`). It can also script the awkward cases: remove a part
(`Parts.Detach(light)` → not present), add parts (dirty), cut power, share a switch.

### 5.5 Test-facing helpers

```csharp
[Test]
public void Blink_toggles_every_five_ticks()
{
    var v = Kfs.Vehicle(lights: 2);
    v.LoadKc("""
        #include <kfs.h>
        #include <purrtty.h>
        #pragma kfs tick_hz 10
        #pragma kfs task run rate_hz=10
        uint32_t n;
        void run(void) { x_light_set_on(0u, (n / 5u) % 2u); n++; }
        """);
    v.Run(seconds: 1.0);
    Assert.That(v.Parts.Lights[0].OnHistory, Is.EqualTo(new[] { false, true }));
}
```

`LoadKc` builds against the vehicle's own layout, fails the test with the compiler's diagnostics
when the build fails, loads and starts. Time is simulated, so tests never sleep.

### 5.6 The command-line tool

`purr-kfs` (`purrTTY.Kfs.Cli`), a framework-dependent .NET tool — for machines that have a .NET 10
runtime; the game itself is self-contained and does not provide one.

| Command | Does | State |
| --- | --- | --- |
| `purr-kfs check <file> [--workspace <dir>] [--json]` + vehicle flags | Build a kC or kASM project exactly as the workbench would (folder = project; root file = own project); print each diagnostic as `file:line:col: severity code: message` with its hint on a second line, then a one-line summary. `--json` prints one JSON document instead (diagnostics, sizes, per-task WCET). Writes nothing to the workspace. | built (B2) |
| `purr-kfs run <file> [--workspace <dir>] [--for 10s] [--frame 60] [--log] [--requests] [--state]` + vehicle flags | Build, load onto a simulated vehicle, run for a simulated duration, print the program log, the decoded stock requests and the final state of the machine, lights and animations (all three when none is asked for). | built (B2) |
| `purr-kfs lsp (--stdio \| --listen <host>:<port>)` | The language server, standalone (Workstream C; options in §7.6). | built (C2) |
| `purr-kfs header`, `purr-kfs kinc` | Print `<purrtty.h>` / `purrtty.kinc`. | built (B2) |
| `purr-kfs services [--markdown]` | Print the catalogue as a text table, or as the full markdown reference. | built (B2) |
| `purr-kfs test <scenario.toml>…` | Run scenario files: a vehicle spec, a program, a duration, optional scripted telemetry, and expectations on parts, logged lines and stock requests. Output in TAP; non-zero exit on failure. | **not built** (B3) |

`check`, `run` and `lsp` need the game's assemblies: `--ksa <dir>` or `KSA_DLL_DIR`. Exit codes:
0 success, 1 build errors or a trapped program, 2 a usage or environment problem. The simulated
vehicle is described by flags — `--lights <n>`, `--deployables <n>` (2 s each), `--actuators <n>`
(1 s each), `--thrusters <n>` (default 1), `--engines <n>`, `--gimbals <n>`; a TOML vehicle
description belongs to the scenario files and was not built.

Scenario files (task B3, **not built**) would be the player-facing form of unit tests:

```toml
program = "blink.c"
duration = "2s"

[vehicle]
lights = [ { name = "Spot A" }, { name = "Spot B", switch_group = 0 } ]
deployables = [ { name = "Solar", duration = 5.0 } ]

[[expect]]
at = "end"
light = 0
on = true

[[expect]]
log_contains = "lights: 2"
```

### 5.7 Files

```text
purrTTY.Kfs.Testing/
  purrTTY.Kfs.Testing.csproj
  KsaAssemblyResolver.cs        no game types; the in-memory PE patch
  KfsRuntime.cs, KfsRuntimeOptions.cs
  BuildResult.cs                Ok, Image, Diagnostics (file, line, column, code, severity, message, hint)
  SimVehicle.cs, SimVehicleSpec.cs
  SimVehicleParts.cs            IVehicleParts in memory
  SimTelemetry.cs, TelemetrySampler.cs   scripted navigation and flight data
  SimHostSource.cs              simulated hosts for the game's MainThreadPump (A4 tests)
  SimConsoleHost.cs             an in-memory console, vehicle and workspace for the `purr` command (A5 tests)
  ProjectBuilder.cs             ProjectResolver + ProjectCompiler over a runtime: what `purr-kfs check` and the tests build with (B2)
  FaultyVehicleParts.cs, DecoyTablesField.cs, …   fault injection and installer seams
purrTTY.Kfs.Cli/                (assembly name purr-kfs)
  purrTTY.Kfs.Cli.csproj        <PackAsTool>, <ToolCommandName>purr-kfs</ToolCommandName>
  Program.cs                    resolver first, then command dispatch (no game types)
  CliApp.cs, CliOptions.cs, HelpText.cs
  Commands/CheckCommand.cs, RunCommand.cs, LspCommand.cs, CatalogCommands.cs (header, kinc, services), BuildOutput.cs
  Commands/StandaloneLanguageHost.cs   the language server's host outside the game (layout flags, vehicle.h, default)
purrTTY.Kfs.Tests/
  purrTTY.Kfs.Tests.csproj      stamps KSAFolder as assembly metadata
  ModuleInit.cs                 [ModuleInitializer]: the resolver
  Runtime/RuntimeSetUp.cs       [SetUpFixture]: the one KfsRuntime of namespace …Tests.Runtime
  …                             the tests of §4.16 and §5.8
purrTTY.Kfs.Tools.Tests/        the CLI, the language server and the Neovim runtime files (§6.9, §7.7)
```

Not built: `Scenario/ScenarioFile.cs`, `ScenarioRunner.cs` and `Commands/Test.cs` (task B3).

### 5.8 Tests of the runtime itself

| Test | Asserts |
| --- | --- |
| `Resolver_Loads_Unmodified_Assemblies` | `KSA.dll` is loaded from the configured directory and the file on disk is byte-identical afterwards |
| `Frame_Order_Matches_Game` | a write in tick k is readable from the first tick of the next frame, not the same tick |
| `Animation_Slews_At_One_Second_Per_Second` | deploy takes `duration` simulated seconds; state sequence retracted → deploying → deployed |
| `Detach_Makes_Slot_Inert` | not present, reads 0, writes rejected with the documented text |
| `Rescan_Renumbers_And_Bumps_Generation` | after adding parts and `x_parts_rescan()` |
| `Stock_Requests_Are_Recorded` | a stock example's `kfs_stage`/`kfs_set_throttle` calls appear in `StockRequests` and the program does not stall |
| `Scripted_Telemetry_Reaches_Program` | a program subscribed to `MSG_FLIGHT_DATA` sees the scripted altitude |
| `Scenario_Files_Run` | the sample scenarios pass and a deliberately failing one fails with a useful message — **not built** (B3) |
| `Cli_Check_Exit_Codes` | 0 on a clean build, 1 with diagnostics in `file:line:col: severity code: message` form |

The test names actually used for the M1 rows are in §8.6. The read-back row is now
`Runtime/FrameOrderTests.cs` in `purrTTY.Kfs.Tests`; the command-line rows (`check`, `run`, usage
and exit codes) are in `purrTTY.Kfs.Tools.Tests` (`Runtime/CliCheckTests.cs`, `Runtime/CliRunTests.cs`,
`Usage/CliUsageTests.cs`), which runs the tool in process through `CliApp.Run`.

## 6. Workstream C — a language server for kC and kASM

What the game exposes for this is catalogued in
[language-tooling surface](../docs/kfs-info/modding/language-tooling-surface.md); what follows is
the design built on it. Spike 05 (`plans/investigations/kfs-spikes/05-lsp-neovim/`) already ran a
minimal version end to end against a headless Neovim.

### 6.1 Shape

- `purrTTY.Kfs.Lsp` is a **library**: JSON-RPC framing, the LSP method handlers, a document store,
  a build scheduler and the feature providers. Its core, `KfsLanguageServer`, runs over any pair of
  streams and has no knowledge of where it runs. JSON-RPC is hand-rolled over `System.Text.Json` (as in the spike) so the mod gains no
  third-party dependency.
- It talks to its surroundings through one interface:

  ```csharp
  public interface IKfsLanguageHost
  {
      GameTables Tables { get; }                       // the augmented tables
      string WorkspaceRoot { get; }
      BuildTarget CurrentTarget { get; }               // part + layout + display name; a snapshot, safe from any thread
      event Action TargetChanged;                      // re-diagnose open documents
      bool CanControlVehicle { get; }
      Task<CommandResult> RunVerb(string verb, CancellationToken ct);   // `kfs <verb>` on the right thread, output captured
      ILiveProgram? Live { get; }                      // snapshot, log tail, traces; null when there is none (always, outside the game)
      string? Token { get; }                           // what `initialize` must carry; null = no check (stdio)
  }
  ```

- Two hosts implement it:

  | Host | Where | Tables | Target layout | Vehicle commands | Live data |
  | --- | --- | --- | --- | --- | --- |
  | `InGameLanguageHost` | the library, `purrTTY.Kfs.Lsp/InGame/` — **not** `purrTTY.GameMod` — so that it is tested offline. Its only game calls go through the five-member interface `IKfsGameSide` (`WorkspaceRoot`, `RefreshTarget`, `RefreshLive`, `ExecuteKfs`, `ExecutePurr`), implemented by `purrTTY.GameMod/Kfs/KsaKfsGameSide.cs` | `KfsIntegration.Tables` (the augmented tables when the extension is active, the stock ones when it is not) | the workbench target: the targeted vehicle's layout, or the bench | yes | yes: the workbench target's computer (the vehicle when one is targeted, else the bench) |
  | `StandaloneLanguageHost` | `purrTTY.Kfs.Cli/Commands/`, behind `purr-kfs lsp` | built by `KfsRuntime` from the player's `KSA.dll` | in this order: the layout flags (`--thrusters`, `--engines`, `--gimbals`); the layout recorded in the workspace-root `vehicle.h` (banner and fingerprint verified; the file is watched); the default of `purr-kfs check` (one thruster, bench processor) | **no**: every `kfs.*` command is refused except `kfs.manual`, which is answered from the tables | none (`Live` is null) |

- Transports: `KfsLspListener`, a small loopback TCP accept loop in the library, is used by both
  hosts; the CLI adds stdio. In the game the lifetime is split in two: `LspServiceCore` (library,
  `InGame/`, tested offline) owns the listener, the per-start token, the server info file and a
  one-line status; `KfsLspService` (in `purrTTY.GameMod`) owns the configuration, the output tee,
  the live feed, the `purr lsp` verb and the per-frame call.
- **One connection, one of everything.** Each connection gets its own `KfsLanguageServer`,
  document store and build scheduler (with its own compile thread, started lazily at the first
  build, so a connection that never authenticates costs none). Nothing is shared between clients
  except the host, the indexes of §6.5 and the process-wide count of retired compile threads
  (§6.3).

### 6.2 Protocol behaviour

| Area | Decision | Basis |
| --- | --- | --- |
| Position encoding | Offer and prefer `utf-16`; convert when the client forces `utf-8` or `utf-32`. | kFS columns are 1-based UTF-16 code units (tab = 1); spike 05 passed all three encodings. |
| Text sync | Full-document sync at first; incremental later if profiling asks for it. | Sources are small (the stock editor caps a buffer at 262,144 bytes). |
| Clients | Several at once; `exit` closes one connection and never the process. | The server lives in the game. |
| Authentication | `initialize` must carry `initializationOptions.token`; a wrong or missing token is answered with error `-32803` and the connection is closed. On a host with a token, any other request before a successful `initialize` gets `-32002` and the connection is closed as well; after `shutdown`, requests get `-32600`. The token is 32 bytes from the cryptographic random generator as base64url, new at every start, never logged. Over stdio there is no token (the peer started the process). | R12. |
| Lifetime | **Independent of the extension.** The server starts when the mod has loaded (`TerminalMod.OnFullyLoaded`, right after `Patcher.patch()`), whether or not the kFS extension installed and whether or not the game's kFS prototype switch is on — diagnostics are then computed against whatever tables the game has (stock ones without the extension, where `<purrtty.h>` is unknown, exactly as it is to the game's own compiler). Only `lsp_enabled = false` in `kfs.toml` keeps it off. Starting creates no folder and no file in the workspace. It stops at unload, **before** Harmony unpatching and the extension's uninstall; it writes and removes the server info file (§7.6). A port that cannot be opened leaves the mod working, with the reason in `purr lsp` and the menu. | A player who switched the extension off still gets an editor. |
| Limits | The server listens on a loopback socket inside the game process, so every resource a peer can consume is bounded — table below. | Independent review: an unauthenticated peer could exhaust threads or kill the process. |
| Logging | One status line at start (`purrTTY kFS LSP: listening on 127.0.0.1:<port>, workspace <root> …`); method names and timings at debug level through `purrTTY.Logging`; never to stdout in stdio mode. | Spike 05. |

Limits and hardening, as built (each is an option with this default):

| Limit | Value | Where |
| --- | --- | --- |
| Connections at once | 8 | `KfsLspListenerLimits.MaxConnections` |
| Connections not yet authenticated | 2 (further ones are refused) | `KfsLspListenerLimits.MaxUnauthenticated` |
| Time allowed for a successful `initialize` | 5 s | `KfsLspListenerLimits.InitializeTimeout` |
| Send timeout | 10 s | `KfsLspListenerLimits.SendTimeout` |
| Back-off after a failed accept | 100 ms, doubling to 1 s | `KfsLspListenerLimits.AcceptBackoff`, `MaxAcceptBackoff` |
| Message size before authentication | 64 KiB (a larger one closes the connection) | `KfsLanguageServerOptions.MaxPreAuthMessageBytes` |
| Message size after authentication | 16 MiB (a larger one is skipped) | `KfsLanguageServerOptions.MaxMessageBytes` |
| Bytes queued for a peer that does not read | 16 MiB (then the connection is cut off) | `KfsLanguageServerOptions.MaxQueuedBytes` |
| Header block | 2 KiB, 4 lines, 1 KiB per line; only `Content-Length` and `Content-Type` are accepted, so an HTTP request is closed at once | `JsonRpcOptions` |
| `kfs` commands waiting for the game's main thread | 8 (further ones are refused, "the game is busy") | `InGameHostOptions.MaxPendingVerbs` |
| `kfs` commands run per frame | at least one; more only while the frame's 4 ms budget lasts | `InGameHostOptions.VerbBudgetPerFrame` |
| Wait for a frame to run a command | 10 s; a command that has not started by then fails and never runs later | `InGameHostOptions.VerbTimeout` |
| Compile time | 10 s, then the compile thread is retired (§6.3) | `BuildSchedulerOptions.CompileTimeLimit` |
| Retired compile threads still running | 2, process-wide (then builds are refused until one ends) | `BuildSchedulerOptions.MaxRetiredThreads` |
| Wait for the server's threads at stop | 500 ms | `LspServiceOptions.StopTimeout` |
| Overlay text (an unsaved buffer) | 4 MiB, the game's own source limit, refused with the game's message | `ProjectResolver.MaxSourceBytes` |

No exception can leave a thread or timer the library starts (`LspThreads.Start` catches
everything; a thread that cannot be started costs that connection only). Left open, deliberately:
the number of open documents per authenticated connection is not capped, and the text an output
capture holds is bounded only by what a command prints.

### 6.3 Builds

One **build scheduler** per connection owns all compiling for that client (`Build/BuildScheduler.cs`;
the compile itself is `ProjectCompiler` in `purrTTY.Kfs`, shared with `purr-kfs check`):

1. A change marks the document's *project* dirty and (re)starts a 120 ms debounce.
2. When it fires, the scheduler assembles the input **itself** (`ProjectResolver`), following the
   workbench's rules but over the editor's text rather than the disk:
   - a file in a folder → every `.c/.h/.kasm/.kinc` directly in that folder except `vehicle.h` /
     `vehicle.kinc`, keyed by bare name — the union of the files on disk and the open buffers;
   - a root `.c` → the file plus the transitive closure of its quoted includes, **computed from the
     overlaid text** (the workbench's own `Workspace.ProjectOf` reads the closure from disk, so an
     unsaved `#include "x.h"` would otherwise produce a false "file not found"); `"X.kasm.h"` pulls
     in `X.kasm`;
   - a root `.kasm` → itself; a root header alone is not a project (no diagnostics, by design);
   - **every open buffer** in the set overrides the file on disk (the workbench's overlay handles
     exactly one file).

   The resolver reproduces the game's rules exactly, quirks included, because a tool that finds
   more files than the game would build a different program: the game scans quoted includes with
   a multiline regular expression that also matches inside block comments and inactive `#if`
   blocks and misses an **indented** include directly after another include line; the resolver's
   hand-written scans (linear in the text, where the regular expressions were quadratic on runs
   of blank lines) are tested to find exactly the same names
   (`purrTTY.Kfs.Tools.Tests/Projects/IncludeScanTests.cs`). Reads are bounded: a file or buffer
   over the game's 4 MiB source limit is refused with the game's message, a FIFO or device reads
   as empty, and a path that leaves the workspace is refused.
3. kC is compiled with the public `Compiler.CompileOnThisThread(new CompileInput { Files, Part,
   Game, Layout, ProjectName })` **on the scheduler's own thread** — one long-lived, below-normal
   priority thread with a 16 MiB stack (the front end's nesting limit assumes that stack), started
   lazily at the first build.
   `Compiler.Compile` is deliberately not used: it starts a new 16 MiB normal-priority thread per
   call and, on timeout, abandons it still running.
   Pure kASM is assembled as the workbench does it:
   `new Assembler(tables, part, layout) { ProjectFiles = name => overlay-or-disk text }.Assemble(the .kasm sources)`;
   without `ProjectFiles` a quoted `.include "purrtty.kinc"` cannot be resolved.
4. One compile runs at a time; a newer request for the same project replaces the queued one; a
   finished result is published only if it is for the latest version of every buffer it used, and
   never for a document that was closed while its build ran.
5. Every compile is wrapped in `try`/`catch`: the game's compiler can throw on unusual input
   (spikes 05 and 06 reproduced several cases); an exception becomes a single diagnostic with code
   `KFS-LSP` on the first line and the server carries on.
6. **Watchdog.** A compile cannot be cancelled. If one runs longer than 10 s the scheduler
   publishes a `KFS-LSP` "compile did not finish" diagnostic, retires that thread (it is left to
   finish or not) and starts a fresh one. At most two retired threads may still be running **in
   the whole process** (`BuildSchedulerOptions.MaxRetiredThreads`, counted by `LspThreads.Retired`
   across every connection); beyond that the scheduler refuses further compiles until one ends,
   and says so. This bounds what stock leaves unbounded.

Measured cost (spike 05): 0.2–2 ms for small files, about 19 ms for the largest stock example,
80–95 ms for the first compile in a process.

Thread-safety (read from the code, and stress-tested in spike 05 — 400 compiles of 12 files on 8
threads, five times, matching serial results): concurrent compiles against **one** tables instance
are safe — the stock editor already compiles on a pool thread while `kfs build` compiles on the
main thread. What is **not** safe is changing `SystemHeaders.Files`, `SystemHeaders.Builtins` or
the tables while any compile runs: they are plain collections read without a lock on the compiling
thread. The install changes them once, before the server exists; the only later change is the
uninstall's removal of `<purrtty.h>`, which is skipped while a compile is counted as running
(`SystemHeaderReaders`, §4.5). Each scheduler compiles serially to bound its cost inside the game.

### 6.4 Diagnostics

`Diagnostic(Severity, Code, File, Line, Col, Message, Hint)` → LSP:

| kFS | LSP |
| --- | --- |
| `Severity.Error` / `Warning` | 1 / 2 |
| `Code` (`KC-E003`, `KA-W711`) | `code`; `codeDescription` is not available — the manual page (rule sentence, range, languages) is served through hover on the code and a `kfs.manual` command |
| `Message` + `Hint` | `message`, with the hint on a second line as `hint: …`; a hint ending "at file:line" also becomes `relatedInformation` (workspace files only). `source` is `kfs`; `K?-W006` (never used) carries the `Unnecessary` tag |
| `File` | a **name**, not a path. Mapped to a URI through the project's file map. `<kfs.h>`, `<vehicle.h>`, `<purrtty.h>`, `<math.c>`, `X.kasm.h`, `<predefined>` are virtual: the diagnostic is anchored on the `#include` line that pulled the header in, with the original location in the message (virtual documents, §6.6, were not built) |
| `Line`, `Col` (1-based) | `line-1`, `character` = `Col-1` converted to the negotiated encoding |
| no end position | kC: lex the line with the game's public `Lexer` (it throws `LexException` on malformed text — catch it) and take the width of the token at that column; fall back to the identifier or one character. kASM: the column is always 1 — use the trimmed line |
| `(project, 0, 0)` "no position" (no task declared, limits) | first line of the project's main file: a root project's own file; in a folder `main.c` / `main.kasm` when there is one, else the first `.c` (kC) or `.kasm` (kASM) file, else the first file |
| a failure of the server itself (the compiler threw, the watchdog fired, builds are suspended) | one diagnostic with code `KFS-LSP` on the main file |
| a trap of the running program (in-game host, §6.7) | an error diagnostic with code `KFS-TRAP` on the trapping line |
| macro expansion | reported at the macro-name token (as the compiler does) |

After each build the server publishes diagnostics for **every file of the project**, including
empty lists for files that became clean. The compiler reports up to 100 errors plus a "too many
errors" marker; warnings are not capped.

### 6.5 Hover and completion (table-driven, no parser needed)

| What | Source |
| --- | --- |
| Services, stock and `x_*`: signature, summary, arguments, results, cost class, host checks, stock requirement, id | `tables.ServicesData.Services` rows and `tables.Services[id]`; for mod services the richer `Doc` text of the catalogue |
| Constants (`KFS_*`, `X_*`, `DEG2RAD`…) and library prototypes (`<math.h>`, `<vecmat.h>`, `<string.h>`) | the header texts in `SystemHeaders.Files`, indexed once by a small scanner of `#define` and prototype lines |
| Vehicle-specific names: `commands.*`, `THRUSTER_COUNT`…, `MSG_*`, `kfs_msg_*_t` and their fields with units | `VehicleHeader.Generate(layout, false, tables)` text, `MessageLayout`, `layout.CommandFields`, `tables.MessagesData` |
| `sys.*` fields | `tables.SysBlockData.Fields` |
| `#pragma kfs …` directives and their arguments | a static table transcribed from the parser (listed in the surface document) |
| kASM mnemonics: immediates, stack effect, summary, cost | `tables.OpcodesByMnemonic`, `OpcodesData.Rows` |
| kASM directives, `SVC_*`, `TRAP_*`, layout and struct symbols | static directive table; `new BuiltinIncludes(tables, layout, "kfs.kinc" \| "vehicle.kinc")` |
| Diagnostic codes | `DiagnosticManual.Page(tables, code, hint)` |

Completion context is decided by the server's own light scanning of the current line (after `#`,
after `#pragma kfs`, after `#include <`, after `commands.`/a message variable and `.`, at a kASM
mnemonic position); everything else offers identifiers from the indexes above plus the project's
own functions and globals from the last successful build.

As built (task C3; `Index/` and `Features/`, registered by `StandardFeatures.AddTo`):

- **Hover** answers for: services, stock and `x_*` (the catalogue's documentation for `x_*`);
  library prototypes, constants and macros of every system header, `<purrtty.h>` included; the
  names of `<vehicle.h>` for the current target; members of `sys`, `commands`, `sensors` and of a
  variable the document declares with a known struct type; `#pragma kfs` directives, preprocessor
  directives and `#include <…>` names; the project's own functions and globals from the last
  successful build. In kASM: mnemonics (stack effect, and the cost on the target's part),
  directives, the symbols of `<kfs.kinc>`, `<vehicle.kinc>` and `purrtty.kinc`, and the file's own
  `.equ`s, data, slots, labels and macros. In both languages a diagnostic code — and any position
  a current diagnostic covers — shows the game's manual page for that code. Everything else
  (locals, parameters, keywords, numbers, user macros and typedefs, a member of an expression whose
  type is not known cheaply) answers nothing rather than a guess.
- **Completion** is triggered by `.`, `>` (only as the end of `->`), `#`, `<` and `"`; a trigger
  character that opens none of the contexts gets an empty list. The server filters, answers
  `isIncomplete: false` and offers no `completionItem/resolve`.
- **Units are derived, and say so.** The game's tables hold a message field's unit only inside the
  prose of its `Meaning`; hover therefore derives a unit from the field name's suffix
  (`altitude_m`, `pulse_s`) and labels it `Unit: … (from the name's suffix)`. No table describes
  what a command field *means* either — a layout field has a type, an offset and a one-shot flag
  only.
- **The diagnostic manual is one line per code.** `DiagnosticManual.Page` gives the rule sentence,
  severity, range and languages; there is no longer explanation to show.
- **The static tables are transcribed, not read.** `#pragma kfs` directives, preprocessor
  directives, kC keywords and kASM directives are not in any game table — the kASM directive names
  exist only as string literals in the assembler's code — so `Index/StaticTables.cs` lists them by
  hand, and `syntax/kasm.vim` (§7.4) is checked against the assembler by a test.
- **The index is cached**, not rebuilt per request: a small most-recently-used cache
  (`KfsIndexCache`) keyed by the tables, part and layout references, the target's name and a stamp
  of the header texts in `SystemHeaders.Files`.

### 6.6 Navigation, symbols and cost lenses

**Not built** (tasks C5 and C6 were outside the agreed scope). The server advertises none of
these capabilities; the table remains as the design for whoever continues.

| Feature | How | Phase |
| --- | --- | --- |
| Document symbols (functions, globals, tasks, kASM labels) | `IrModule` of the last **successful** build (a failed kC build returns a module with no functions in it, a timed-out one none at all), with ranges from brace matching | 2 |
| Per-task cost lens: "WCET 161 / 100,000 cycles · stack 8 B" above each task function | `CompileReport` (`ReportWcet`, task stacks); budget = clock / tick rate | 2 |
| Per-line cycles as inlay hints, with the soft routines a line pulls in | `CompileReport.Estimates` (`LineCost`), formatted like the stock editor's cycles column | 2 |
| Go to definition, references, hover with types, signature help for functions, globals, locals and parameters | the server runs the public `KcFrontEnd.Lower` itself to keep the typed AST (`TranslationUnit`, `Obj`, `Node` all carry positions) and builds a position index | 3 |
| Definition into `<kfs.h>`, `<vehicle.h>`, `<purrtty.h>` | read-only copies written under `<config>/.purrTTY/kfs-headers/` (not the workspace, where they would show up as a project) and returned as ordinary file URIs | 3 |
| Macros, typedefs, struct tags and fields, enum constants, inactive `#if` regions, semantic tokens | not retained by the game's front end: needs a small scanner of our own | later |
| Formatting, rename | not offered | — |

Cost data exists only for successful builds; the lenses keep the last good values, greyed when the
buffer no longer builds.

### 6.7 Commands and live data (in-game host)

This is the wire contract between the server and an editor, as implemented
(`Features/CommandProvider.cs`, `HostFeatures.cs`, `LiveFeed.cs`, `InGame/`). The Neovim runtime
files (§7.4) are its one client so far.

**Commands** (`workspace/executeCommand`). Each is translated into exactly one line of the game's
own console command and run there; the translation is fixed, so an editor cannot inject a second
verb (the verb word is the server's, and `KfsConsoleCommands.Execute` splits at the first space
only).

| Command and arguments | The line that runs | Notes |
| --- | --- | --- |
| `kfs.build [fileUri]` | `kfs build <path>` | also writes `vehicle.h` / `vehicle.kinc`, as the stock command does. `Workbench.Build` takes the whole rest of the line as the file, so a path with spaces works |
| `kfs.load [fileUri]` | `kfs load <path>`, plus ` --vehicle` when the build target is a vehicle | the game builds from disk (or from the stock editor's buffer when the file is open there), never from the editor's unsaved text, so the client writes its buffers first; the load is queued and takes effect at the vehicle's next dispatch — not while paused. **A path that contains whitespace is refused**: the game's `Workbench.Load` splits its argument string at every space (`--part <name>`, `--vehicle`, else the file), so such a file cannot be loaded by `kfs load` at all, from any front end. Without ` --vehicle` a load onto a vehicle target would retarget the bench, which is why the server adds it |
| `kfs.start`, `kfs.stop`, `kfs.reset` (no arguments) | `kfs start`, `kfs stop`, `kfs reset` | |
| `kfs.target ["vehicle" \| "bench"]` | `kfs target vehicle` / `kfs target bench` | the changed target is published to every client before the command's answer is sent, so a `kfs.load` sent next already adds ` --vehicle` |
| `kfs.manual [code]` | `kfs manual <code>` | one code, such as `KC-E003`. The only command the standalone server answers (from the tables) |
| `kfs.parts [] \| ["lights" \| "deploy" \| "actuators"]` | `purr parts [kind]` | the index tables of §4.13 |
| `kfs.verb [line]` | the line (a leading `kfs ` is stripped); one non-empty line | refused unless `lsp_allow_vehicle_commands = true` in `kfs.toml` |

A file argument must lie inside the workspace; malformed arguments are refused and nothing runs.

**The answer** is always `{ "ok": bool, "output": string, "error": string | null }`, never a
JSON-RPC error. **`ok: true` means "the game ran the line"**, not "the game was happy": the
game's own failures — a build error, `kfs is off …`, an unknown verb — come back as text in
`output` with `ok: true`, because the game's wording cannot be parsed reliably (build errors also
arrive as diagnostics). `ok: false` means the line did not run, and `error` says why: a refusal
above, the game too busy, no frame within the time limit, the server stopping, or — on the
standalone server — `kfs.build needs the game: this language server runs outside it (diagnostics,
hover, completion and kfs.manual only); in KSA, open the file with purr nvim`.

**Where and when a line runs.** `RunVerb` marshals to the **main thread in the UI phase**: a queue
(`InGame/MainThreadQueue.cs`) drained by `InGameLanguageHost.Frame()` from the top of the mod's
`[StarMapAfterGui]` hook — where the stock console itself runs commands, and the only place the
workbench may be touched — which calls the public `KfsConsoleCommands.Execute(line)` and returns
what was printed during the call. A frame runs **at least one line, and more only while the
frame has spent less than 4 ms on them**, so a burst of slow commands (`kfs build`) costs one of
them per frame, never all of them in one. At most **8** lines wait; a ninth is refused at once
("the game is busy"). A line that has not *started* within 10 s (a loading screen) fails and
never runs later. An identical `kfs build <file>` or `kfs manual <code>` still waiting is joined
rather than queued twice; nothing else is coalesced. Stopping the server fails everything that
waits.

**Output capture.** Output is captured by a **standing tee** (`InGame/OutputTee.cs`): when the
service starts it replaces the public `KfsIntegration.Output` with a writer that forwards to the
original and to subscribed clients, then reads `KfsIntegration.Workbench` once — the workbench
copies `Output` only when that getter runs, and prints deferred replies from its per-frame hook,
so a temporary swap would miss them. At stop the tee is taken out only if `Output` still holds
it; otherwise it stays as a pass-through. With a non-default writer installed the game's
`KfsIntegration.Error` writes each error to `Output` **as well as** to the log, and the console
window shows log errors by itself, so every `kfs` error would appear twice in the console. The
optional patch `KfsErrorEchoPatch` (a prefix and finalizer on `KfsIntegration.Error`) marks the
call so the tee passes that line to the editors but not to the console writer; it applies only
when the writer the tee replaced is the game's own console writer. `purr lsp token` prints inside
the opposite scope — console only — so the token never reaches an editor's output feed.

**Capabilities.** `initialize` returns `capabilities.experimental.purrtty`:
`{ "subscribe": bool, "vehicleCommands": bool, "verb": bool }`. **`subscribe` means "a live feed
exists here"** — true in the game, false on the standalone server; a `purrtty/subscribe`
notification is accepted either way and simply never answered where there is no feed.
`vehicleCommands` false means every command but `kfs.manual` will be refused; `verb` says whether
`kfs.verb` is allowed.

**Live data.** A client asks with the notification `purrtty/subscribe { "topics": [...] }`
(`"log"`, `"output"`, `"state"`; the list replaces the previous one, an empty list unsubscribes).
A timer polls only while some client subscribes to `log` or `state`.

| Notification | Content | Source and rule |
| --- | --- | --- |
| `purrtty/kfsLog { lines }` | new program-log lines (`kfs_logf` output and host events), each formatted by the server as the stock editor's console pane and `kfs state` show them — `[k <tick> t <seconds, 0.00>] <text>` — for example `[k 3 t 0.30] trap DIV_ZERO at 0x34 in task 0 (run, main.c:11): commands held` | `computer.Console`: poll `Version`, then `CopyTail` — thread-safe. **On subscribing to `log`, the server first sends the log's last 100 lines once, as `{ lines, tail: true }`**, then every line added after them |
| `purrtty/kfsOutput { text }` | what `kfs` and `purr` commands printed | the `KfsIntegration.Output` tee; batched per command and per frame |
| `purrtty/kfsState` | computer state, tick, overruns, trap with file and line | `computer.Snapshot` (immutable, published by the dispatch thread) — read at most 5 times a second and **only while a client subscribes to `state`**, because reading the property asks the computer to keep publishing a snapshot on every tick. For the same reason a client must not subscribe to `state` merely because it attached: the Neovim files do so after the first `:KfsLoad` or `:KfsStart` that ran, while the log buffer exists, or once the status-line function is used |

The live data comes from the workbench target's computer: the vehicle's when a vehicle is
targeted, else the bench.

**Trap diagnostics.** A trap with a source location is also published, to the clients subscribed
to `state`, as an error diagnostic with code `KFS-TRAP` on that line, merged with the build's
diagnostics; it is cleared when the trap is gone (the program was reset, restarted, reloaded or
stopped) or the client unsubscribes. The log line of a trap ends `commands held`, and the
`kfs_logf` lines the program issued in the trapping tick itself never appear: the game discards
a trapped tick's service requests, the log request among them.

`InGameLanguageHost.CurrentTarget` is refreshed once per frame on the main thread (part, layout
object and name of the workbench's target, exactly what the stock editor captures before a
background build) and read by the scheduler as an immutable snapshot. A frame in which nothing
changed reads the workbench once and compares references; it allocates nothing.

### 6.8 Files

As built:

```text
purrTTY.Kfs.Lsp/
  purrTTY.Kfs.Lsp.csproj
  Protocol/JsonRpcConnection.cs      Content-Length framing with the header and size limits of §6.2,
                                     request/response correlation, cancellation, the bounded write queue
  Protocol/LspTypes.cs               the subset of LSP structures used
  KfsLanguageServer.cs               one per connection: lifecycle, token check, capabilities, dispatch table
  KfsLspListener.cs                  loopback TCP accept loop; connection limits and deadlines
  LspThreads.cs, LspLog.cs           every thread the library starts (catch-all, counters); log sinks
  LspFeatures.cs                     the feature registry (hover, completion, commands, notifications)
  IKfsLanguageHost.cs                IKfsLanguageHost, BuildTarget, CommandResult, ILiveProgram
  KfsLanguageHost.cs                 a plain host: fixed tables and workspace, a replaceable target, no vehicle
                                     (StandaloneLanguageHost builds on it; tests use it directly)
  Documents/DocumentStore.cs         open documents: text, version, line index
  Documents/WorkspaceUris.cs         URI ↔ workspace path (the URI form the client used; real paths through links)
  Documents/PositionMapper.cs        kFS (line, col) ↔ LSP position in three encodings
  Build/BuildScheduler.cs            debounce, own compile thread, latest-wins, exception guard, watchdog
                                     (file sets come from purrTTY.Kfs's ProjectResolver, the compile from ProjectCompiler)
  Build/ProjectBuildResult.cs        what one build produced; the main file
  Build/DiagnosticMapper.cs          §6.4
  Index/KfsIndex.cs                  the index and its cache (KfsIndexCache)
  Index/TableIndex.cs                services, opcodes, sysblock, messages
  Index/HeaderIndex.cs               #define and prototype scanner over header texts
  Index/TargetIndex.cs               the names that depend on the build target (<vehicle.h>, layout, messages)
  Index/StaticTables.cs              pragmas, directives and keywords transcribed from the game's parsers
  Index/IndexEntry.cs, MemberShape.cs
  Features/StandardFeatures.cs       registers hover and completion
  Features/HoverProvider.cs, CompletionProvider.cs, CompletionList.cs
  Features/SourceScan.cs, Words.cs, PositionRequest.cs, ProjectSymbols.cs   the light line scanning of §6.5
  Features/HostFeatures.cs           registers the commands, purrtty/subscribe and the capability entry
  Features/CommandProvider.cs        the commands → verb lines (§6.7)
  Features/LiveFeed.cs               log, output and state feeds; trap diagnostics
  Live/LiveSource.cs, HostComputerLive.cs   the running program as plain values / over a HostComputer
  InGame/InGameLanguageHost.cs       IKfsLanguageHost in the game; the IKfsGameSide interface
  InGame/MainThreadQueue.cs          calls marshalled to the game's main thread (budget, cap, timeout, coalescing)
  InGame/OutputTee.cs                the standing tee on KfsIntegration.Output
  InGame/TargetSnapshot.cs           the build target, replaced as a whole
  InGame/LspServerInfoFile.cs        kfs-lsp.json: write (owner-only), read, delete if still ours
  InGame/LspServiceCore.cs           listener + token + info file + status, without the game
  InGame/LspServiceText.cs           the text of `purr lsp` and of the menu line
purrTTY.GameMod/Kfs/
  KfsLspService.cs                   start/stop with the mod; kfs.toml; the tee; `purr lsp`; the per-frame call
  KsaKfsGameSide.cs                  IKfsGameSide: the only language-server code that touches the game
  KfsErrorEchoPatch.cs               optional patch on KfsIntegration.Error (§6.7, R17)
purrTTY.Kfs.Cli/Commands/
  LspCommand.cs, StandaloneLanguageHost.cs   `purr-kfs lsp`
```

Not built: `Features/SymbolProvider.cs`, `CostLensProvider.cs`, `InlayHintProvider.cs` (C5, C6).

### 6.9 Tests (`purrTTY.Kfs.Tools.Tests`, offline)

The tests are in `purrTTY.Kfs.Tools.Tests` — there is no `purrTTY.Kfs.Lsp.Tests` project — under
`Lsp/` (framing, documents, position mapping, the main-thread queue, the output tee: no game
needed) and `Runtime/Lsp/` (everything that compiles: protocol, scheduler, diagnostics, hover,
completion, host commands, the live feed, the in-game host and service core over a fake game
side, hardening). They are driven through in-memory streams (`MemoryPipe`, `LspTestClient`)
against a real `KfsRuntime`; `ServerHarness` gives the scheduler an infinite debounce and a
`Flush`, so no test waits for a timer. One rule was added to those of §5.2: a type in a test
assembly cannot implement an interface that has game-typed members (NUnit's reflection over the
assembly fails), so tests fake the game-free `ILiveSource`, not `ILiveProgram`. The names below
are the planned ones; the fixtures group them differently.

| Test | Asserts |
| --- | --- |
| `Initialize_Negotiates_Utf16_And_Requires_Token` | capabilities; refusal without the token |
| `Diagnostics_Map_Positions` (ASCII, tab, non-BMP character; each encoding) | exact ranges |
| `Diagnostics_Cover_Whole_Project` | an error in `b.h` of a folder project is published for `b.h` while editing `a.c`; fixing it clears it |
| `Unsaved_Buffers_Overlay_Disk` (two dirty buffers) | the build uses both |
| `Header_Diagnostics_Anchor_On_Include` | virtual-file diagnostics |
| `Latest_Wins_Under_Rapid_Edits` | only the final version is published |
| `Compiler_Exception_Becomes_Diagnostic` | the server survives and reports `KFS-LSP` |
| `Hover_And_Completion_Know_Mod_Services` | `x_light_set_on` documentation; pragma and include contexts |
| `Kasm_Diagnostics_And_Opcode_Hover` | whole-line ranges; stack effect text |
| `Cost_Lenses_Match_Compile_Report` | per-task WCET and budget — **not built** (C5) |
| `Target_Change_Rediagnoses` | `THRUSTER_COUNT`-dependent code flips between clean and erroring |
| `Commands_Reach_The_Host` | a fake host records `RunVerb("load x --vehicle")` |
| `Exit_Closes_Connection_Only` | a second client keeps working |
| `Neovim_Attaches_Headless` (silently skipped without `nvim`) | the spike-05 scenario, with the shipped runtime files — `Runtime/Nvim/NeovimTests.cs` and `InGameNeovimTests.cs` (§7.7) |

Added by the hardening pass after the independent review (`Lsp/HardeningUnitTests.cs`,
`Runtime/Lsp/HardeningTests.cs`, `Runtime/Lsp/CliLspHardeningTests.cs`): the limits of §6.2 hold —
a tokenless connection that is reset does not take the process down, idle tokenless connections
do not accumulate threads, and `purr-kfs lsp --stdio` exits with code 1 on a signal. The fix pass
reported that the reviewer's three reproduction scripts failed against the earlier code and pass
now, and disclosed that the new tests themselves were not each run against the old code first
(most use interfaces the old code did not have).

## 7. Workstream D — Neovim through purrTTY

### 7.1 What the player gets

1. In the game: purrTTY menu › **kFS** › **Edit in Neovim...** (a list of the workspace's source
   files) or **Open workspace in Neovim**, or the console command `purr nvim [file]`. A new file
   is started with `purr nvim <name>`; there is no "New file…" menu entry.
2. A purrTTY terminal opens — a 2D window by default, an in-world terminal on request — whose
   process is the player's own host `nvim`, started in the kFS workspace with the file open.
3. The buffer is attached to the language server running inside the game: diagnostics as you type
   against the workbench target's layout (the launcher targets the controlled vehicle when the
   game's kFS prototype is on), completion and hover for stock and `x_*` services. (Per-task cost
   lenses were planned and not built, §6.6.)
4. `:KfsBuild`, `:KfsLoad`, `:KfsStart`, `:KfsStop`, `:KfsReset`, `:KfsTarget` act on the
   workbench target; `:KfsLog` shows the program's log as it runs; `:KfsParts` shows the index
   tables; `:KfsManual` a diagnostic code's manual page; `:KfsInfo` the connection status.
5. Quitting Neovim normally closes the terminal; a Neovim that exits with an error keeps its
   window, so the error can be read.

The kFS workspace is an ordinary folder, so the same Neovim setup also works from a terminal
outside the game (§7.6).

### 7.2 Launching Neovim

purrTTY can already run an arbitrary executable as a terminal's process, with arguments,
environment and working directory, on Unix (discrete argv) and Windows (ConPTY, each argument
quoted): `ProcessLaunchOptions.CreateCustom(path, args…)`,
`GhosttyTerminalController.OpenWindow(options, title)`, and for in-world terminals
`InWorldTerminalManager.Instance.Create(record)` from inside an ImGui frame. No shell and no
`StartupCommand` typing is involved.

The launch is planned in `purrTTY.Kfs` as plain strings (`NeovimLaunchPlan`: executable, argv,
environment, working directory — unit-testable, no terminal types) and carried out by
`KfsNeovimLauncher` in `purrTTY.GameMod/Kfs/`, on the main thread:

```csharp
KfsConfig config = KfsConfig.Load();                          // read again at every launch: no restart needed
NeovimLaunchPlan plan = NeovimLaunchPlan.For(NeovimLaunchRequest.ForThisProcess(
    workspaceRoot, file, nvimRuntimeDir, server, config.NvimPath, KfsConfig.ConfigRoot()));
var o = ProcessLaunchOptions.CreateCustom(plan.Executable, [.. plan.Arguments]);
//   Neovim's arguments, as discrete argv elements (no shell quoting anywhere):
//     --cmd  "lua dofile(vim.env.PURRTTY_KFS_NVIM .. '/purrtty_kfs.lua')"  --  <file, or the workspace folder>
o.WorkingDirectory = plan.WorkingDirectory;                  // the workspace root
foreach (var (k, v) in plan.Environment) o.EnvironmentVariables[k] = v;
foreach (string v in plan.RemovedEnvironment) { o.EnvironmentVariables.Remove(v); o.RemovedEnvironmentVariables.Add(v); }
o.CloseOnSuccessfulExit = true;                              // T1: close only on exit code 0
TerminalWindow w = controller.OpenWindow(o, shownName);
w.TryRename("kFS: " + shownName);                            // or the registry's unique variant when taken
controller.IsVisible = true;                                 // after OpenWindow: set first, it would spawn a default shell
w.RequestFocus();
```

The argument list always has `--` before the file, so a file name that starts with `-` cannot be
read as an option. The `--cmd` pair is present only when `<runtime dir>/purrtty_kfs.lua` exists;
without it Neovim still opens the file, with no kFS support.

Environment of the editor process:

| Variable | Value | Note |
| --- | --- | --- |
| `PURRTTY_KFS_NVIM` | `<mod folder>/nvim` | a path in a variable, so spaces and commas are harmless |
| `PURRTTY_KFS_ROOT` | the workspace root | present in every launch; it is also how a kFS editor terminal is recognised (§7.3, T4) |
| `PURRTTY_KFS_LSP` | `127.0.0.1:<port>` | only while the mod's language server runs |
| `PURRTTY_KFS_TOKEN` | the server's token | the runtime files read it at start-up and remove it from Neovim's own environment, so `:terminal`, `:!` and plugins do not inherit it |
| `PURRTTY_CONFIG_DIR` | the folder that **contains** `.purrTTY/` | so the runtime files find the server info file again after `purr lsp restart` (a new port and token) without guessing (§7.6) |
| `SHELL` | the player's own shell, as the game process saw it (Unix, when it is an existing absolute path) | the terminal otherwise points `SHELL` at the process it starts, which for the login-shell route below is `/bin/sh`; Neovim takes `'shell'` and `:terminal` from it |
| `FORCE_COLOR`, `CLICOLOR_FORCE` | **removed** | other purrTTY terminals set them; in an editor they would make programs run through pipes (`:!`, `:make`, linters) write colour codes into the quickfix list. Removal is a new launch option, `ProcessLaunchOptions.RemovedEnvironmentVariables`, honoured by the Unix spawner, the Windows environment block and the custom-shell bridge |

Why the runtime directory travels in an environment variable and is loaded with `dofile`: the
mod's default folder is under `…/My Games/Kitten Space Agency/mods/`, which contains spaces, and
`:set runtimepath^=<path>` splits on unescaped spaces and commas. Loading one entry script by
absolute path also survives plugin managers that reset `runtimepath` during start-up.

Resolving `nvim`, in order (`NeovimLaunchPlan.Resolution`: `ConfiguredPath`, `SearchPath`,
`LoginShell`, or `None` with an error text):

1. the configured path (`nvim_path` in `kfs.toml`): exactly that file. It must be absolute; if it
   does not exist, or on Windows names a `.cmd`/`.bat` script, the launch fails with that reason —
   there is **no** fallback to the steps below;
2. the game process's `PATH`, absolute directories only: `nvim` on Unix, `nvim.exe` on Windows.
   The terminal does not honour `PATHEXT` (T3 is open), so a `nvim.cmd` or `nvim.bat` shim (Scoop,
   some package managers) is reported as found but not launchable, with the advice to set
   `nvim_path` to the real `nvim.exe`;
3. on Unix, the player's login shell — a game started from Finder, the Dock or Steam usually lacks
   the Homebrew and user `bin` directories. The shell is `$SHELL` when that is an existing `sh`,
   `bash`, `zsh` or `dash`, else `/bin/sh` (the script below is POSIX `sh`; fish, nu or tcsh would
   not run it). It is started as `<shell> -lc <script> sh nvim <Neovim's arguments>`, where the
   fixed script is the only shell text in the whole launch:

   ```sh
   purrtty_nvim=$(command -v "$1" 2>/dev/null)
   case "$purrtty_nvim" in
   /*) shift; exec "$purrtty_nvim" "$@" ;;
   esac
   printf '%s\n' '<Neovim was not found …>' "(searched by your login (non-interactive) shell: PATH=$PATH)"
   printf '%s' 'Press Enter to close this terminal. '
   read -r purrtty_reply
   exit 127
   ```

   Neovim found as a file is `exec`ed with its arguments untouched. Not found (or only an alias or
   a function): the script prints why and the `PATH` it searched, **waits for Enter**, and exits
   127 — so the window does not vanish silently. A login shell that is not interactive reads the
   profile files, not the rc files: zsh `~/.zprofile`, bash `~/.bash_profile` or `~/.profile`,
   never `~/.zshrc` or anything behind an interactivity check; a `PATH` set only there is not
   seen. A profile that `exec`s another program (tmux, another shell) replaces the shell before
   the script runs: `nvim_path` is the remedy. `-i` was considered and not used. **Unverified in
   the game:** the route itself was exercised by tests with real shells, not from a game started
   by Finder or Steam (§10, V52 and V53);
4. otherwise an error. The menu checks this when it opens (`NeovimLaunchPlan.Problem`): the two
   Neovim entries are greyed out with the reason under them, and a failed launch leaves
   `Last launch failed: …` there.

**The window closes only on a successful exit.** `CloseOnSuccessfulExit` closes the terminal when
Neovim exits with code 0. A Neovim that fails — it crashed at start, a wrapper's target is
missing, the not-found script's 127 — keeps its window, its final screen and ` (exit N)` in the
tab label (visible when the window has more than one tab), so the player can read what happened.
(`CloseOnExit`, which closes on any exit code, exists as well and is what T1 originally
specified.)

**Which file.** A path is taken relative to the workspace. An existing file or folder opens as it
is. A name that does not exist must end in `.c`, `.h`, `.kasm` or `.kinc`; it is **not** written
by the mod — Neovim's first `:w` creates it, and the runtime files' `BufWritePre` creates its
folder (for kC and kASM files below a workspace root only). An absolute path, a `..`, another
extension for a new file, or anything else outside the workspace is refused with a message.
Listing the workspace's files for the menu reads the disk without creating the workspace folder
(`Workspace.List()` would create it); only a launch creates it.

**In-world.** With `nvim_open_in = "inworld"` the launch is queued and the terminal is created
from the launcher's pump in `OnAfterUi` (an in-world terminal needs an ImGui frame): a 120×40
camera billboard with click-to-focus, focused at once. Without the in-world subsystem the launch
fails with the advice to set `nvim_open_in = "window"`.

If the language server is not running (`lsp_enabled = false`, or the port could not be opened)
Neovim is still launched, without `PURRTTY_KFS_LSP` and `PURRTTY_KFS_TOKEN`; the runtime files
then try the fallbacks of §7.6.

**Targeting the vehicle.** The workbench's target stays the bench until something selects the
vehicle, so without a retarget diagnostics would be computed against the bench layout while
`:KfsLoad` built for the vehicle's. The launcher therefore calls
`KfsIntegration.Workbench.TargetActiveVehicle()` — but **only when the game's kFS prototype
switch is on and a vehicle is controlled**. The call is not harmless: it creates the vehicle's
`KfsVehicleHost` and switches what `kfs load`, `start`, `stop` and `reset` act on, and the game's
method does not check the prototype switch itself. With the switch off that choice is left to the
player. The launch message says which case applied (`; kfs targets <vehicle>`, `; no controlled
vehicle: diagnostics use the bench layout`, `; the kFS prototype is off: …`).

### 7.3 Terminal changes this needs

| # | Change | Why | Where | State |
| --- | --- | --- | --- | --- |
| T1 | **Close on exit**: a launch option `CloseOnExit`; when set, the window (or tab, or in-world instance) is closed once its process has exited. | Before, nothing reacted to an exit except " (exit N)" in the tab label, and with one tab the tab bar is hidden. | `ProcessLaunchOptions`, `TerminalSession`, `SessionManager`, `TerminalWindow`, `SessionExitPolicy`, `GhosttyTerminalController`, `InWorldTerminalRenderer` / `InWorldTerminalManager` | **done** (D1), plus the variant `CloseOnSuccessfulExit` (exit code 0 only) the editor uses |
| T2 | **Guard resize after exit**: a resize when no process is running does nothing. | Both back ends threw `InvalidOperationException` then; resizing a dead window threw between `ImGui.Begin` and `End`. A latent bug independent of kFS. | `IProcessManager.Resize` in each back end, under its state lock | **done** (D1) |
| T3 | **Windows `PATH` lookup honours `PATHEXT`**, and a `.cmd`/`.bat` hit is launched through `cmd.exe /c`. | `nvim` installed through a `.cmd` shim (scoop, some package managers) is missed; and the terminal starts processes with a raw `CreateProcessW`, which cannot run a script directly. | `ShellCommandResolver.FindExecutableInPath`, the Windows process launcher | **open** (D4). The launcher detects a shim and says it cannot be started (§7.2) |
| T4 | **Layouts keep environment variables** (or the kFS launcher is excluded from layout capture). | A captured layout drops `EnvironmentVariables`, so a saved "kFS editor" terminal would relaunch without its server address. | `LayoutManager`, `WindowLayoutRecord` | **done by exclusion** (D3): a terminal whose launch environment has `PURRTTY_KFS_ROOT` is left out of a saved layout (`NeovimLaunchPlan.IsKfsEditorEnvironment`); it is re-created through the launcher. A window that is still starting is recognised too (`TerminalWindow.StartLaunchOptions`, `WindowLayoutRecord.LaunchOf`) |

How T1 and T2 work, as built — details that are easy to get wrong when changing them:

- The options are `ProcessLaunchOptions.CloseOnExit` and `CloseOnSuccessfulExit` (both default
  false, both cloned with the options); `TerminalSession.ShouldCloseAfterExit` decides. In a
  layout file they are the keys `close_on_exit` and `close_on_successful_exit` of
  `[terminal.shell]`, written only when true, so older layout files load unchanged.
- **The exit flag is set only after the process's last output has been drained**
  (`ProcessExited` is raised after the output pump finished). `IsRunning` turns false earlier and
  is the wrong signal: closing on it would lose the final screen.
- `SessionManager.CloseExitedSessions()` runs on the main thread **before `ImGui.Begin`**. It
  builds one more frame first, so the final output, title and clipboard request are applied, then
  closes the session through the ordinary `CloseSessionAsync`.
- When the last tab closes, `SessionExitPolicy` decides and the window calls the same `Close()`
  as its "x" button; the controller prunes it on the next frame, saves the window geometry if it
  was the last window, and hides the terminal. A hidden terminal's 4 Hz drain closes exited tabs
  too, and showing it again prunes windows that closed while hidden.
- In-world: the renderer closes the session and sets `SessionEnded`; the manager's
  `PruneRetired` calls `Remove`, which is the normal two-frame deferred GPU teardown.
- A session that fails to start is never added to the manager; its error text stays on screen.
- `IProcessManager.Resize` is a no-op before the start, after the exit and after a stop; a failure
  of the operating system on a live terminal goes to `ProcessError`; after `Dispose` it still
  throws `ObjectDisposedException`. The Windows back end checks the pseudoconsole handle, not
  `Process.HasExited`. `TerminalSession.Resize(cols, rows, cellW, cellH)` always resizes the
  surface and then the PTY, and both front ends call it.

Useful but not blocking (each is its own small task in §8; **none was built**):

| # | Change | Why |
| --- | --- | --- |
| T5 | Focus-in/out reporting when the program enables mode 1004. | Neovim's `FocusGained` drives `autoread` and many plugins. |
| T6 | Encode Ctrl/Alt with the remaining punctuation (`/ - ; ' , . =`), and Alt-composed characters on non-US layouts. | Common Neovim mappings (`<C-/>`, `<M-.>`) and typing `{ [ |` with Option on some Mac layouts. |
| T7 | Let the toggle hotkey (F12 by default) pass to a 2D terminal when a modifier is held, or make it configurable per window. | F12 never reaches a 2D terminal's program today. |
| T8 | OSC 52 read support (clipboard query) in 2D windows; OSC 52 at all in-world. | `"+p` over OSC 52. |

Until those are built, these do not reach a program in a purrTTY terminal: F12 (the toggle
hotkey), Ctrl or Alt with `/ - ; ' , . =`, Option-composed characters on non-US layouts, focus
events, and clipboard reads over OSC 52.

What already works for a full-screen editor: truecolor (`COLORTERM=truecolor`, `TERM=xterm-256color`),
alternate screen, scroll regions, cursor shapes, synchronized output, the kitty keyboard protocol,
mouse tracking with motion, bracketed paste, resize propagation, and game-hotkey suppression while
a terminal has focus.

### 7.4 The Neovim runtime files

Shipped in the mod folder as `nvim/`. Plain Lua, no plugin manager, no dependency beyond Neovim's
built-in LSP client.

As built (`purrTTY.GameMod/nvim/`, deployed to `<mod folder>/nvim/`; the reference for a player
is the shipped `doc/purrtty-kfs.txt`):

```text
nvim/
  purrtty_kfs.lua             the single entry script: finds its Lua modules beside itself, then require('purrtty_kfs').setup()
  lua/purrtty_kfs/init.lua    setup(): options, workspace roots, filetypes, connection discovery, attach, reconnect, :KfsInfo, status()
  lua/purrtty_kfs/commands.lua   the :Kfs* commands (workspace/executeCommand) and the kfs://output buffer
  lua/purrtty_kfs/log.lua     the :KfsLog buffer (kfs://log) and the subscriptions
  lua/purrtty_kfs/buffer.lua  per-buffer options for kc and kasm (set from the FileType callback)
  lua/purrtty_kfs/text.lua    makes every string from the server safe to show
  syntax/kasm.vim             mnemonics and directives; a checked-in snapshot (see below); sourced by path
  plugin/purrtty_kfs.lua      two lines, for people who install the folder as an ordinary plugin
  doc/purrtty-kfs.txt         the help file
```

Design points:

- **Independent of `runtimepath`.** The launcher loads `purrtty_kfs.lua` by absolute path
  (`--cmd "lua dofile(…)"`), and that script finds its Lua modules relative to its own location;
  buffer options are set from Lua and the kASM syntax file is sourced by path, so nothing depends
  on `ftplugin/` or `syntax/` discovery, and a plugin manager that resets `runtimepath` during
  start-up does not matter. The same folder also works as a normal plugin for manual
  installation; both ways may be active at once (`setup()` runs once).
- **Own filetypes, decided by a function.** kC files are `*.c`/`*.h`; a normal C language server
  must not attach to them, and ordinary C files must keep filetype `c`. The rule is therefore not
  a path pattern but a **function**: `vim.filetype.add` registers the pattern `.*%.[cChH]` with
  priority 1000 (it beats the `.c`/`.h` extension rule) and a callback that answers `kc` only
  when the file lies below a known workspace root (`root_of(path)`: Windows paths compared
  case-insensitively with `\` and `/` alike, symbolic links resolved). `*.kasm` and `*.kinc` are
  `kasm` **everywhere**, in any letter case, because no other filetype owns them; only kASM files
  below a workspace root are attached to a server. The buffer set-up uses Neovim's C syntax for
  `kc` and registers the C tree-sitter parser for it.
- **Workspace roots**, in this order: `$PURRTTY_KFS_ROOT`; the `root` option; the
  `workspace_root` of every server info file found; the game's default workspace,
  `<Documents>/My Games/Kitten Space Agency/kfs`, when that folder exists. `~`, environment
  variables and relative paths in the first two are expanded.
- **Attach.** A `FileType kc,kasm` autocommand starts `vim.lsp.start` with a `cmd` chosen by
  discovery (§7.6) and `root_dir` = the workspace root, so every buffer in the workspace shares
  one client. Because `--cmd` runs before the file is read, the rules are in place for the first
  buffer. Start-up is never blocked: the probe and the server start are asynchronous.
- **Authentication.** The token goes in `init_options`; the server refuses `initialize` without it.
  The launcher's `PURRTTY_KFS_TOKEN` is moved into Lua state and removed from Neovim's
  environment at start-up.
- **Commands.** `:KfsBuild [file]`, `:KfsLoad [file]`, `:KfsStart`, `:KfsStop`, `:KfsReset`,
  `:KfsTarget vehicle|bench`, `:KfsParts [kind]`, `:KfsManual [code]`, `:KfsVerb {line}`,
  `:KfsLog`, `:KfsInfo`, `:KfsRestart`. `:KfsBuild`, `:KfsLoad` and `:KfsVerb` first write every
  modified kC/kASM buffer of the workspace — the game's `kfs build` and `kfs load` read the disk,
  never Neovim's unsaved text — then send `workspace/executeCommand`. The answer
  (`{ ok, output, error }`, §6.7) is shown as it is: a short output as a message, a long one in
  the scratch buffer `kfs://output`; `ok: false` as a warning, never as a Lua error. When a
  command finishes the `User` event `KfsCommandDone` fires with `data = { command, ok, output,
  error }`.
- **Log and state.** `:KfsLog` opens the scratch buffer `kfs://log` and subscribes to the feeds
  (§6.7): it starts with the program log's last lines, appends new ones, prefixes `kfs` command
  output with `> ` and marks a trap as `!! trap …`. Deleting the buffer unsubscribes.
  `require('purrtty_kfs').status()` gives a short string for a status line
  (`kFS: Running main.c tick 1200`, or the trap). The `state` topic is subscribed only after the
  first `:KfsLoad` or `:KfsStart` that ran, while the log buffer exists, or once `status()` is
  used — never merely at attach (§6.7 says why).
- **Server text is untrusted for display.** `text.lua` puts every string from the server on
  single lines, without escape sequences or control characters, and cuts very long lines.
- The player's own configuration loads normally; nothing is written to it and no global option is
  changed. Everything is namespaced (the augroup `purrtty_kfs`, the client `kfs`, the `Kfs*`
  commands, the buffers `kfs://log` and `kfs://output`, the filetypes `kc` and `kasm`).

Options, in `vim.g.purrtty_kfs` or `require('purrtty_kfs').setup({ … })`:

| Option | Default | Meaning |
| --- | --- | --- |
| `root` | none | extra workspace folder or folders |
| `retry_interval_ms` | 3000 | how often to look for a server again |
| `probe_timeout_ms` | 500 | limit of the TCP probe |
| `stdio` | true | fall back to `purr-kfs lsp --stdio` |
| `purr_kfs` | `purr-kfs` | that executable (a name on `PATH`, or a path) |
| `log_max_lines` | 10000 | lines kept in `kfs://log` |
| `treesitter` | false | start tree-sitter highlighting in `kc` buffers |
| `buffer_options` | true | false leaves the `kc`/`kasm` buffer options to the player |
| `initialize_timeout_ms` | 5000 | how long a new connection may take to answer `initialize` |
| `max_attempts` | 3 | early ends in a row before an address is dropped |

**`syntax/kasm.vim` is a checked-in snapshot, not a build product.** It is generated from the
game itself — the mnemonics of its opcode table and the directive names its assembler recognises
— by `purrTTY.Kfs.Tools.Tests` (`Nvim/KasmSyntax.cs`), and a test (`Runtime/Nvim/KasmSyntaxTests.cs`)
regenerates it and compares it with the file in the repository; `PURRTTY_UPDATE_SNAPSHOTS=1`
rewrites the file after a game update. The directive names are in no game table: the assembler
matches them with `switch` statements and comparisons on string literals, so the generator reads
them out of the assembler's compiled code. Mnemonics are highlighted only at statement position
(after an optional label), so a label, an operand or a directive argument spelled like one is not
mistaken for one. kASM has no character literals, so the syntax defines none.

**Which Neovim.** The files check for Neovim 0.11, the oldest release with every interface they
use, and give one message and nothing else on anything older. **Only 0.12.6 was actually run**
(headless, on macOS); 0.11 itself was not.

Connection behaviour, as built:

| Situation | What Neovim does by itself | What `purrtty_kfs` does |
| --- | --- | --- |
| Nothing is listening on the port | one warning; the client object exists but never initialises and never retries | probes the address with a short `vim.uv` TCP connect **before** `vim.lsp.start`; if it fails, falls through to the next discovery step |
| No server was found, or the server goes away (game closed, mod reloaded) | `on_exit`, `LspDetach` for every buffer, diagnostics cleared, no reconnect | looks again every `retry_interval_ms` (3 s) for as long as a `kc`/`kasm` buffer of that workspace is loaded, and attaches all of them when a server appears — for example through a new server info file with a new port and token. Only a connection that had worked is announced as lost |
| A peer accepts the connection and never answers, or closes before answering | — | given up after `initialize_timeout_ms` without a message; the retry interval doubles each time (up to 16 times), and after `max_attempts` in a row that address is not tried again until its address or token changes or `:KfsRestart` |
| The server refuses the token | — | reported once; not tried again until `:KfsRestart` (a new info file is picked up by itself) |
| `purr-kfs` exits with an error | — | not restarted |
| The game starts while a buffer is attached to the standalone server | — | the buffer stays with it; `:KfsRestart` switches |
| Several buffers in the workspace | one client, one connection | nothing |
| Several Neovim processes | one connection each | the server accepts several clients (up to the limits of §6.2) |

### 7.5 Menu, console and config

- **Menu.** `TerminalMenus.DrawMenuContent` draws a `kFS` submenu after "Layouts…" through the
  hook `TerminalMenus.DrawKfsMenu` (set to `KfsMenu.Draw`). It is shown whether or not the
  extension installed, so a disabled install shows its reason. Its entries, top to bottom:

  | Entry | Does |
  | --- | --- |
  | *(a status line)* | the extension's one-line status |
  | **List parts in console** | `purr parts` (greyed while the extension is not active) |
  | **Reset light overrides** | `purr lights reset` (greyed likewise) |
  | **Write example programs** | `purr examples` (greyed likewise) |
  | **Extension status in console** | `purr status` |
  | *(the server line)* | `Language server: 127.0.0.1:<port>, <n> client(s)`, or why it is not running |
  | **Copy server address** | puts the address on the clipboard (greyed without a server) |
  | **Language server in console** | `purr lsp` |
  | **Edit in Neovim...** | a submenu of the workspace's `.c`, `.h`, `.kasm` and `.kinc` files, each running `purr nvim <file>`; the list is re-read when the submenu opens |
  | **Open workspace in Neovim** | `purr nvim` |
  | *(problem lines)* | when Neovim cannot be launched, the two entries above are greyed and the reason is shown; after a failed launch, `Last launch failed: …` |

  Every entry calls `PurrConsole.Execute` directly, so the menu works even if registering the
  console command failed. Every `BeginMenu`/`EndMenu` pair ends in a `finally`, and
  `DrawMenuContent` catches and logs once: an exception in one entry cannot leave ImGui's menu
  stack unbalanced.
- **Console.** `purr nvim [file]` and `purr lsp [token | restart]` are verbs of the mod's own
  `purr` command (§4.13), so no kFS code is patched for them. `purr lsp` prints the address, the
  connected clients, the workspace, the build target, which commands an editor may run, the info
  file and how to get the token; `purr lsp token` prints the token (to the console only);
  `purr lsp restart` stops and starts the server, reading `kfs.toml` again, with a new port (when
  `lsp_port` is 0) and a new token.
- Optional, later (not built): a button in the stock kFS editor's menu bar through a postfix on
  the public `KfsEditorWindow.DrawMenuBar`.
- **Config lives in its own file**, `<config>/.purrTTY/kfs.toml`, owned by
  `purrTTY.GameMod/Kfs/KfsConfig.cs` (Tomlyn; written crash-safely through a temporary file and a
  rename — the same pattern as the display project's `AtomicFile`, repeated because that class is
  internal to `purrTTY.Display`). It cannot be a `[kfs]` table in `purrtty.toml`: that file is a
  typed model re-serialised on save, which would drop a table it does not know, and `purrTTY.Kfs`
  must not reference the display project that owns it. The file is created with comments on first
  load; an existing file is never rewritten, so a file from an earlier build does not gain the
  comments of keys added later.

  | Key | Default | Meaning | Read |
  | --- | --- | --- | --- |
  | `enabled` | `true` | install the kFS extension (`x_` services) | at mod load |
  | `nvim_path` | `""` | absolute path of the `nvim` executable; empty = search (§7.2). On Windows write it as a single-quoted TOML string, so backslashes are literal | at every launch |
  | `nvim_open_in` | `"window"` | `"window"` or `"inworld"` | at every launch |
  | `lsp_enabled` | `true` | run the language server | at start and at `purr lsp restart` |
  | `lsp_port` | `0` | loopback port; 0 picks a free one | at start and at `purr lsp restart` |
  | `lsp_allow_vehicle_commands` | `false` | allow `kfs.verb`, i.e. any `kfs` line from an editor (§6.7) | at start and at `purr lsp restart` |

- Wiring follows the layouts precedent: `TerminalMod` constructs the launcher with
  `(_controller, _inWorld)`, hands it to `KfsMenu`, and pumps it from `OnAfterUi` (which is also
  where an in-world terminal must be created). The language server's per-frame call
  (`KfsLspService.Frame`) is the first thing `OnAfterUi` does, before the check that the terminal
  initialised: a terminal that failed to come up does not take the server with it.
- Deployment: `nvim/**` (without `doc/tags`) is copied to `<mod folder>/nvim/` by an item and
  `Copy` pair in `purrTTY.GameMod.csproj`'s `CopyCustomContent`, like fonts and themes; the
  launcher finds it beside its own assembly. The mod folder also gains `purrTTY.Kfs.dll` and
  `purrTTY.Kfs.Lsp.dll`; no game or test assembly is ever copied there.

### 7.6 Using Neovim outside the game

When the first `kc` or `kasm` buffer of a workspace opens, the runtime files look for a server in
this order and use the first that answers; every buffer of that workspace then shares one client:

1. `PURRTTY_KFS_LSP` with `PURRTTY_KFS_TOKEN` in the environment (the in-game launch), when
   `PURRTTY_KFS_ROOT` is unset or is this workspace. One exception: when the live server's info
   file names the **same address with another token** — the server was restarted on a fixed
   `lsp_port` — the environment is stale and is skipped (`:KfsInfo` says "superseded") instead of
   being tried and refused;
2. a **server info file** whose `workspace_root` is this workspace:
   `<config>/.purrTTY/kfs-lsp.json` — `{ "address", "token", "pid", "ksa_build",
   "workspace_root" }`, unknown keys ignored — written by the mod when its server starts,
   rewritten when the workspace is moved (`kfs ws`), deleted when it stops. A file whose `pid` is
   not running is stale and ignored. It is **not** in the workspace: anything written there
   appears in `kfs ls` and the editor's file tree. Where it is looked for: only
   `$PURRTTY_CONFIG_DIR/.purrTTY/kfs-lsp.json` when `PURRTTY_CONFIG_DIR` is set (the variable
   names the folder that **contains** `.purrTTY`; the mod sets it for every Neovim it starts);
   otherwise beside every known workspace root (`<root>/../.purrTTY/kfs-lsp.json` — with the
   default locations `…/Kitten Space Agency/.purrTTY/` is next to `…/Kitten Space Agency/kfs/`)
   and in `<Documents>/My Games/Kitten Space Agency/.purrTTY/`;
3. `purr-kfs lsp --stdio --workspace <root>` if `purr-kfs` is on `PATH` (and the `stdio` option
   is on) — the standalone server of Workstreams B/C, which needs a .NET runtime and
   `KSA_DLL_DIR`, and uses the layout recorded in the workspace's `vehicle.h`. It offers
   diagnostics, hover, completion and `kfs.manual` only (§6.7);
4. nothing: the files open as plain `kc`/`kasm` buffers with syntax only, and one message says so.

A TCP address is probed with a short connect before Neovim's client is started (§7.4).

So a player can keep a Neovim open on a second monitor attached to the running game, and a
developer can edit and test with no game at all. Outside the game the runtime files are loaded
with `nvim --cmd "lua dofile('<mod folder>/nvim/purrtty_kfs.lua')"` (or a `dofile` line in
`init.lua`), or by copying the folder to `…/site/pack/purrtty/start/purrtty_kfs`.

**The server info file** is written owner-only on Unix (mode 0600, set when the temporary file is
created, so the token is never in a file others could read), is refused when its path would lie
inside the workspace, and at stop is deleted only when it still parses and both its token and its
`pid` are this server's — another server's file is left alone.

**The standalone server's token** (`purr-kfs lsp`). Over `--stdio` there is none: the peer started
the process. A `--listen <host>:<port>` server is **always** token-protected: the token comes from
`--token <t>` (visible in the process list), `--token-file <file>` (the file's first line) or the
environment variable `PURR_KFS_LSP_TOKEN`; without any of them one is generated and printed as a
second line. Standard output then carries exactly `LISTENING <port>` and, when the token was
generated, `TOKEN <t>`; logs go to standard error. With `--stdio` nothing but the protocol is
written to standard output, and a termination signal ends the process with exit code 1 even while
standard input stays open.

The token keeps other users and other machines out; it does not protect against another process
of the same user, which can read the info file (risk R12).

### 7.7 Tests

| Test | Where | Asserts |
| --- | --- | --- |
| `NeovimLaunchPlan_*` | `purrTTY.Kfs.Tests/NeovimLaunchPlanTests.cs` (the plan is plain strings; `purrTTY.GameMod` has no test project) | argv, environment and working directory for each resolution path, in Unix and Windows conventions; the login-shell script run with the real `sh`, `bash`, `zsh` and `dash` (arguments passed exactly; the not-found branch) |
| `CloseOnExit_*`, `CloseOnSuccessfulExit_*` | `purrTTY.Terminal.Tests/SessionExitTests.cs` | the session is closed after exit (any code / code 0 only), only the exited one, also when hidden and never rendered; a failed process keeps its final screen; a session that failed to start is untouched |
| the last-tab decision | `purrTTY.Display.Tests/SessionExitPolicyTests.cs` | when the window itself closes |
| `Resize_After_Exit_Is_Ignored`, `Resize_Before_Start_…`, `Resize_Racing_Exit_Never_Throws` | `purrTTY.Terminal.Tests/UnixProcessManagerTests.cs` and `ConPtyProcessManagerTests.cs` (each set runs on its own platform only and is among the suite's platform skips elsewhere; the totals above come from a macOS run, so the ConPTY set was not part of them) | no exception |
| layout keys | `purrTTY.Display.Tests/LayoutTomlTests.cs` | `close_on_exit` / `close_on_successful_exit` round-trip; old files load |
| `PathExt_Resolution` | — | **not built** (T3, task D4) |
| `Neovim_Attaches_Headless` and the rest of the Neovim tests | `purrTTY.Kfs.Tools.Tests/Runtime/Nvim/NeovimTests.cs` (real language servers on a loopback port, and a scripted one, `Nvim/FakeKfsServer.cs`, for the command and feed contract) and `InGameNeovimTests.cs` (against the real in-game host and service core over a fake game side, `Nvim/InGameStack.cs`); skipped silently when `nvim` is not on `PATH` | a headless Neovim, started exactly as the launcher starts it and with temporary configuration folders, attaches, receives diagnostics, runs the commands, follows the log, reconnects |
| `Kasm_Syntax_Matches_The_Game` | `purrTTY.Kfs.Tools.Tests/Runtime/Nvim/KasmSyntaxTests.cs` (runs without `nvim`) | the checked-in `syntax/kasm.vim` equals what the game's tables and assembler give |
| manual | §10 | in game |

## 8. Task breakdown

### 8.1 How the work is executed

- **Each task below is one unit for one coding agent** — an Opus 5.5 subagent at high effort —
  briefed with: the task's section here, the plan sections and bundle documents it names, the
  spike folder it ports from, the files it may touch, and its acceptance command. Research or
  review that a task needs first goes to a Fable subagent.
- The lead reviews each diff, runs the whole suite, and has a separate reviewer check
  game-facing claims against the bundle before the next dependent task starts.
- Every task ends green on
  `KSA_DLL_DIR=<dir> dotnet build purrtty.slnx` and
  `KSA_DLL_DIR=<dir> dotnet test purrtty.slnx --nologo -v quiet`, in Debug and Release, with zero
  warnings (warnings are errors) and zero test output on pass.
- Standing constraints for every task: tests are silent and never sleep for a fixed time (drive
  simulated time, or wait on a condition); `KSA.dll` is never copied into an output folder, the
  mod dist or a package (the new projects resolve it at run time; a few existing test projects
  already copy some `Brutal.*` assemblies and are left as they are); no machine-specific path in
  any file; no new NuGet dependency unless the task lists it; `try`/`catch` around every patch
  body; nothing under `KfsDispatch` may block or throw.
- A test named in a task and not in a section's table is defined by that task.
- Every task that changes structure, commands or behaviour updates `AGENTS.md` and the affected
  `docs/` files in the same change (the repository's instruction-maintenance rule), and the bundle's
  `log.md` when a bundle document changes.
- Tasks that can only be proven in the game end with the relevant §10 items handed to the player;
  they are "code complete" until those are ticked.

### 8.2 Dependency order

```text
F1 ──► A1 ──► A2 ──► A3 ──► A4 ──► A5                              (parts services)
               └──► B1 ──► B2 ──► B3                                (external runtime, CLI, scenarios)
                     └───► C1 ──► C2 ──► C3 ──► C4 ──► C5 ──► C6     (language server; C2 also needs B2)
D1 (independent) ──────────────────────► D2 (needs C2) ──► D3 (needs C4, A5) ──► D4
D5, D6, D7, D8 (independent, optional)
```

Parallel lanes once A2 is in: {A3 → A4 → A5}, {B1 → B2 → B3}, {D1}; after B1 {C1 → …}.

### 8.3 Milestones

| Milestone | Tasks | Demonstrates | Needs the game? |
| --- | --- | --- | --- |
| M1 — the extension works offline — **done** (offline; see §8.6) | F1, A1, A2, A3, B1 | every service compiled by the real compiler, run by the real VM, against simulated parts, in CI | no |
| M2 — the extension works in the game — **code complete, not run in the game** (§8.7) | A4, A5 | lights and animations driven from a kC program on a real vehicle | yes: §10.1–§10.4 and §10.8 (V1–V15, V24–V34, V51) |
| M3 — command-line runtime — **`check` and `run` done; `test` not built** | B2, B3 | `purr-kfs check / run / test` | no |
| M4 — language server, standalone — **done** (offline) | C1, C2, C3 | diagnostics, hover, completion in any LSP client via `purr-kfs lsp` | no |
| M5 — Neovim from in-game — **code complete, not run in the game** | C4, D1, D2, D3 | the full editing loop inside the game | yes: §10.5–§10.9 (V16–V23, V35–V50, V52–V54) |
| M6 — depth — **not built** | C5, C6, D4–D8 | symbols, cost lenses, navigation, terminal polish | partly |

### 8.4 Tasks

#### F1 — Scaffolding and the assembly resolver

**State: done** (M1; notes in §8.6).

- **Create** `purrTTY.Kfs`, `purrTTY.Kfs.Testing`, `purrTTY.Kfs.Tests` per §3.4 and add them to
  `purrtty.slnx`; game references `Private=false` from `$(KSAFolder)`; `Lib.Harmony` 2.4.2 with
  `PrivateAssets=all` in `purrTTY.Kfs`, as a normal reference in the test project.
- **First, a spike inside the task:** the resolver has only ever run in console programs. Confirm
  it under `dotnet test` (NUnit reflects over fixtures before any set-up runs): install it from a
  `[ModuleInitializer]` in the test assembly, and keep game types out of fixture signatures and
  fields if that proves necessary. Report what was needed.
- **Implement** `KsaAssemblyResolver` (§5.2); stamp `$(KSAFolder)` into the test assembly as
  metadata. **Port from** spike 04.
- **Tests:** `Resolver_Loads_Unmodified_Assemblies`; a smoke test that tables built with
  `GameTables.Load` over the stock streams hold 20 wired services.
- **Also:** a test (or an addition to `scripts/check-release-policy.py`, which today checks only
  release metadata) asserting that `KSA.dll` is in no test output and that no game assembly is in
  the mod dist.
- **Done when:** the suite is green on macOS arm64, Linux x64 and Windows x64 CI legs.

#### A1 — Catalogue, emitter, self-check, table augmentation

**State: done** (M1; notes in §8.6).

- **Create** `Catalog/*`, `Tables/TableAugmenter.cs` (§4.4, §4.5 step 4).
- **Port from** spike 06 (`ServiceCatalog.cs`, `KfsInstall.cs`), which already implements the
  single-enumeration-per-kind selector scheme of §4.1.
- **Tests:** `Catalog_SelfCheck_Passes`, `Tables_Augmented_LoadAndValidate`,
  `Stock_Examples_Compile_Identically`, `Header_Compiles_Alone`,
  `Getter_Selector_Ranges_Are_Disjoint`, negative cases for each self-check rule, and snapshot
  tests of the emitted header, rows, enums and `purrtty.kinc` (byte-stable across runs).
- **Done when:** the game's loader accepts the emitted tables and the snapshots are committed.

#### A2 — Runtime core and the two host hooks

**State: done** (M1; notes in §8.6).

- **Create** `Runtime/*` except `ProjectResolver` and `NeovimLaunchPlan`,
  `Patches/ApplyRequestPatch.cs`, `Patches/IssueResultPatch.cs`; in `purrTTY.Kfs.Testing`:
  `SimVehicleParts`, a first `KfsRuntime` and `SimVehicle` sufficient to load, run and assert,
  whose frame calls `BindingPump.DrainAndApply`.
- **Spec:** §3.2, §4.6–§4.9, §4.12, §5.3–§5.4. **Port from** spike 06 (`PurrRouter.cs`,
  `VehicleParts.cs`, `KfsSim.cs`).
- **Tests:** `Every_Service_Is_Callable` (one case per service), `Kasm_Symbols_Resolve`,
  `Domains_Reject`, `Two_Vehicles_Are_Independent`, `Rescan_Applies_In_Order` (commands accepted
  before a rescan still reach the part they named), `PartMath_*`, fault-injection (a parts back
  end that throws must not fault the computer: the program keeps ticking and the failure is
  counted), and `Router_Does_Not_Allocate` (after warm-up, `ServiceRouter.Answer` and `Accept`
  allocate nothing per call — measured around those calls only; the VM itself allocates per
  `SYSCALL`).
- **Done when:** all of the above pass with the patches applied directly by the test fixture.

#### A3 — Install, uninstall, status, bench

**State: done** (M1; notes in §8.6).

- **Create** `KfsExtension`, `KfsExtensionStatus`, `Tables/TablesPublisher.cs`,
  `Patches/LoadEmbeddedFallbackPatch.cs`, `Patches/BenchApplyRequestPatch.cs`; make
  `KfsRuntime.Start` use `KfsExtension.Install`. The install set is "every patch registered so
  far" — A4 adds the pump patch to it.
- **Spec:** §4.5, §4.6, §4.15.
- **Tests:** `Patches_Apply_To_Distinct_Targets` (every registered patch), `Install_Is_All_Or_Nothing`
  (broken catalogue; foreign tables already published; a missing target simulated by an injectable
  target resolver), `Install_Reuses_Its_Own_Tables`, `Fallback_Postfix_Publishes_Tables` (field
  lookup forced to fail), `Uninstall_Restores`, `Install_Is_Idempotent`,
  `Bench_Is_Readable` (with patch 4: getters silent and 0, setters rejected with the bench text)
  and `Bench_Without_Optional_Patch` (stock "no vehicle to act on").
- **Done when:** M1's whole suite runs through the real install path.

#### A4 — Game back end and wiring *(not offline-testable)*

**State: code complete, reviewed against the decompiled game, not run in the game** (notes in §8.7).
In game: §10.1–§10.3, §10.8.

- **Create** `Game/*`, `Patches/BeforeInputEventsPatch.cs`, `purrTTY.GameMod/Kfs/KfsConfig.cs`;
  call `KfsExtension.Install/Uninstall` from `purrTTY.GameMod/Patcher.cs`.
- **Spec:** §4.7, §4.8, §4.10–§4.12, §7.5 (config file); game facts in
  [control recipes](../docs/kfs-info/game-targets/control-recipes.md),
  [light module](../docs/kfs-info/game-targets/light-module.md),
  [keyframe animation module](../docs/kfs-info/game-targets/keyframe-animation-module.md),
  [dispatch and threading](../docs/kfs-info/integration/dispatch-and-threading.md).
- **Tests:** only what is pure (clamps, switch-group computation over a fake list, config
  round-trip). A reviewer checks every game member used against the decompiled sources.
- **Done when:** it builds, the offline suite is still green, and the items of §10.1–§10.3 and §10.8 are handed over.

#### A5 — Console command, examples, documentation

**State: code complete, not run in the game** (notes in §8.7). In game: V4, V25–V28, V30, V33, V34.

- **Create** `Console/*`, `Examples/*`; register `purr` at install; the purrTTY menu's kFS status
  entry.
- **Spec:** §4.13, §4.14.
- **Tests:** the formatter against sim part sets; `Examples_Run` for every shipped example, each
  built as its own folder project; the verb parser.
- **Docs:** `docs/how-to.md` (write a kFS program that uses parts), `docs/gotchas.md` (threading,
  indices, template sharing), `docs/code-navigation.md`, `AGENTS.md`, the `upgrade-ksa` skill's
  coupling list, and a bundle document describing the shipped extension.

#### B1 — The full test runtime

**State: done** (M1; notes in §8.6). Its frame order was corrected later (§5.4, §8.7).

- **Extend** `KfsRuntime` / `SimVehicle` to §5.3–§5.5: scripted telemetry, stock-request recording
  with `StageApplied`, `ReadGlobal`, detach/attach, pause.
- **Tests:** §5.8 except the scenario and CLI rows.

#### B2 — `purr-kfs` command line

**State: done, offline** (notes in §8.7). The tool package as a release asset (decision D12) is not set up.

- **Create** `Runtime/ProjectResolver.cs` in `purrTTY.Kfs` (§6.3 step 2 — the one implementation
  the CLI and the language server share) and `purrTTY.Kfs.Cli` with `check`, `run`, `header`,
  `services`.
- **Tests:** `ProjectResolver_*` (folder project; root file with an include closure; overlays,
  including an unsaved new `#include`; pure kASM with `.include`), `Cli_Check_Exit_Codes`, output
  format, a `run` golden output.
- **Packaging:** a framework-dependent tool package as a release asset (D12); not in the mod dist.

#### B3 — Scenario files

**State: not built** (outside the agreed scope).

- **Create** `Scenario/*`, `purr-kfs test`. Tomlyn is already used by the repository; add it to
  this project.
- **Tests:** `Scenario_Files_Run`; documentation with two worked scenarios.

#### C1 — LSP protocol core

**State: done, offline** (notes in §8.7).

- **Create** `purrTTY.Kfs.Lsp` protocol and document layers (§6.1–§6.2, §6.8) and its tests (as
  built: in `purrTTY.Kfs.Tools.Tests`, §6.9).
- **Port from** spike 05 (`Session.cs`, position mapping).
- **Tests:** `Initialize_Negotiates_Utf16_And_Requires_Token`, framing fuzz (split headers, large
  bodies), `Exit_Closes_Connection_Only`, position round-trips in three encodings.

#### C2 — Builds and diagnostics, standalone host

**State: done, offline** (notes in §8.7).

- **Create** `Build/*` in `purrTTY.Kfs.Lsp`; `StandaloneLanguageHost` and the `lsp` command in
  `purrTTY.Kfs.Cli` (§6.3, §6.4).
- **Tests:** `Diagnostics_Map_Positions`, `Diagnostics_Cover_Whole_Project`,
  `Unsaved_Buffers_Overlay_Disk`, `Header_Diagnostics_Anchor_On_Include`,
  `Latest_Wins_Under_Rapid_Edits`, `Compiler_Exception_Becomes_Diagnostic`,
  `Watchdog_Retires_A_Stuck_Compile` (with an injected compile function), `Kasm_Diagnostics…`,
  layout reconstruction from a `vehicle.h` (the fingerprint must equal the one in its banner).

#### C3 — Hover and completion

**State: done, offline** (notes in §8.7).

- **Create** `Index/*`, `Features/HoverProvider.cs`, `CompletionProvider.cs` (§6.5).
- **Tests:** `Hover_And_Completion_Know_Mod_Services`, pragma/include/member contexts, opcode hover.

#### C4 — The in-game server

**State: code complete, hardened after an independent review, not run in the game** (notes in §8.7).

- **Create** `KfsLspListener` use in `purrTTY.GameMod/Kfs/KfsLspService.cs`,
  `InGameLanguageHost.cs`; commands and notifications (§6.7); config keys; the server info file
  (§7.6); `purr lsp`.
- **Tests:** against a fake host (`Commands_Reach_The_Host`, `Target_Change_Rediagnoses`, token
  refusal, info-file lifecycle including a workspace move). In game: §10.6 (V16, V19, V22, V38–V42) and V20.

#### C5 — Symbols, cost lenses, inlay hints

**State: not built** (outside the agreed scope).

- §6.6 phase 2. **Tests:** `Cost_Lenses_Match_Compile_Report`, symbol ranges.

#### C6 — Navigation

**State: not built** (outside the agreed scope).

- §6.6 phase 3: own `KcFrontEnd.Lower` run, position index, definition/references/hover types/
  signature help, virtual header copies. **First step is a spike** (delegated like the others):
  confirm that running `Lower` directly yields the same diagnostics as the compiler's own run and
  that the AST positions are sufficient; the surface document marks this path as read, not
  executed.

#### D1 — Terminal: exit handling

**State: done, offline** (notes in §8.7). In game: §10.5.

- T1 and T2 of §7.3 in `purrTTY.Terminal` / `purrTTY.Display`.
- **Tests:** `CloseOnExit_Closes_Window`, `Resize_After_Exit_Is_Ignored` (Unix and Windows twins,
  condition-waits only).

#### D2 — Neovim runtime files

**State: done; run only against a headless Neovim 0.12.6** (notes in §8.7).

- **Create** `purrTTY.GameMod/nvim/**` (§7.4) and the `CopyCustomContent` entries.
- **Port from** spike 05 (`nvim/*.lua`).
- **Tests:** `Neovim_Attaches_Headless` (silently skipped without `nvim`): launched exactly as the
  launcher would (`--cmd "lua dofile(…)"`, the environment variables, a runtime folder whose path
  contains a space), including the no-server and server-restart paths, and once with a
  `runtimepath` reset in a test `init.lua`.

#### D3 — Launcher, menu, console

**State: code complete, not run in the game** (notes in §8.7).

- **Create** `Runtime/NeovimLaunchPlan.cs` in `purrTTY.Kfs`; `purrTTY.GameMod/Kfs/KfsNeovimLauncher.cs`;
  the `kFS` submenu; `purr nvim`; config keys (§7.2, §7.5).
- **Tests:** `NeovimLaunchPlan_*` in `purrTTY.Kfs.Tests` (argv, environment and working directory
  for each resolution path, on Unix and Windows conventions). Confirm the login-shell fallback on
  macOS and Linux. In game: §10.7 (V17, V18, V20, V23, V43–V50), V21, V36, V37 and V52–V54.

#### D4 — Path and layout details

**State: T3 not built; T4 done within D3** by leaving kFS editor terminals out of saved layouts (§7.3).

- T3 and T4 of §7.3. **Tests:** `PathExt_Resolution`, and a launch through a `.cmd` shim on the
  Windows leg.

#### D5, D6, D7, D8 — Terminal polish (optional, independent)

**State: not built.**

- D5: T5 focus reporting. D6: T6 remaining Ctrl/Alt chords and Alt-composed characters. D7: T7
  toggle-hotkey pass-through. D8: T8 OSC 52 read. Each with encoder-level tests in
  `purrTTY.Terminal.Tests`.

### 8.5 What each coding brief contains

1. Goal in two sentences and the milestone it serves.
2. Read first: this plan's sections; the bundle documents; the spike folder to port from.
3. Files to create or change — and an explicit list of what not to touch.
4. Behaviour, by reference to the plan's tables (not restated).
5. Tests to write, by name, and the acceptance command.
6. Constraints from §3.5 and §8.1.
7. What to report: files changed, test totals, anything that contradicted the plan or the bundle.

### 8.6 Implementation notes from M1

Where the M1 code (F1, A1, A2, A3, B1) differs from the text above; the code and its XML docs are
authoritative. Read this before A4.

- **Names and files.** The catalogue is `Catalog/XCatalog.cs` (ids in `Svc`), not `PurrCatalog`;
  the id-block test is `KfsBuildInfo.Owns(id)`. `Runtime/VehiclePartsBase.cs` (not in §4.3) holds
  everything an `IVehicleParts` shares — list publication, index/presence checks, derived
  properties, `PartMath` clamps, command dispatch — so `KsaVehicleParts` (A4) supplies only
  collection, live checks and raw reads/writes, like `SimVehicleParts`. `Catalog/Selectors.cs`
  holds the selector/state enums shared with C#. `Tables/SystemHeaderRegistration.cs` is the one
  header mechanism (installer, runtime, tests).
- **Install (§4.5).** `KfsExtension.Install(Harmony, KfsExtensionOptions?)`; the install set is
  two required hooks (`ApplyRequest`, `IssueResult`) plus the optional `BenchApplyRequest` —
  A4 adds the pump patch. `TablesPublisher` has a fifth outcome, `NotVisible`: `_tables` is
  assigned, but if `KfsIntegration.Tables` does not then return the instance the assignment is
  undone and the install refused. Reused purrTTY tables of another catalogue (any difference, not only
  a missing row: see the review corrections below) are refused as "already built". The log line is
  `purrTTY kFS extension: active — 22 services 0x5000–0x5033, 2 hooks (+1 optional), tables published`
  (the last word is `published`, `reused` or `published through the LoadEmbedded fallback`). Patch 1 claims the request (`__result = true`) before
  routing; the `KfsLog.Once` keys are `accept hook`, `answer hook` etc.
- **Router (§4.9).** An `x_parts_describe` over its one-per-second limit is **rejected**
  (`rejected x_parts_describe: at most one table per second`, counted in `sys.rejected_requests`),
  not silently dropped. Rate limits count the seconds of the current run (`VehicleBinding.ShouldReport`).
- **Test layout (§5.7).** No `Setup.cs`: the `[SetUpFixture]` is `Runtime/RuntimeSetUp.cs` and
  covers only namespace `purrTTY.Kfs.Tests.Runtime`; installer tests are in
  `purrTTY.Kfs.Tests.Installer`; root-namespace tests run with no runtime. The F1 rule (game types
  only in method bodies and helper signatures) shapes every test-facing API: `SimVehicle` exposes
  plain values (`SimComputerState`, `SimTrap`, `StockRequest`, `SimTelemetry`).
- **`SimVehicle` (§5.4–§5.5).** `Name`, not `Id`; `LoadImage(byte[], start)` plus `LoadKc`/`LoadKasm`
  (failed builds throw `KfsBuildException` with the diagnostics). Telemetry is not a
  `Func<long, HostSample>`: `Telemetry` (a mutable `SimTelemetry`) or `TelemetryScript`
  (`Func<double seconds, SimTelemetry>`), converted to the game's `HostSample`. Through the
  sample's `Values` channel (which the game's sampler never uses but its packer honours) a test can
  also set **leaf-only** fields and the four MS-G3 messages, so more is scriptable than §5.1 says;
  the message header and `sys.soi_changes` are not. Added: `ReadGlobal<T>`/`TryReadGlobal<T>`
  over the game's `GlobalLeaves` paths (`err.x`, `va[2]`; scalars only), `State`, `TicksRun`,
  `Overruns`, `RejectedRequests`, `Trap`, decoded `Requests`/`RequestText`, `RunTicks`,
  `RunUntil` (simulated time), `EndBurn`/`LiveBurnPlans`. A paused frame runs the pump only and
  freezes simulated time and the parts. Burns are recorded, never flown: handles come from the
  game's `VehicleComputer`, burn status only from the test, and a plan stays live (the host caps
  live plans at 8) until `EndBurn`. The §5.5 sugar is `KfsRuntime.Vehicle(lights: 2, …)` — a
  static class `Kfs` would be shadowed by the namespace `purrTTY.Kfs` — and there is no
  `OnHistory`; use `Parts.Applied`, `Parts.DeployHistory` or a frame callback.
- **One runtime per process (§5.3).** A second `KfsRuntime.Start` throws; a disposed runtime or
  vehicle throws `ObjectDisposedException`.
- **§5.8 names.** `Frame_Order_Matches_Game` is A2's read-back test (renamed);
  `Animation_Slews_At_One_Second_Per_Second` is `Deploy_Takes_Its_Duration`;
  `Rescan_Renumbers_And_Bumps_Generation` is `Dirty_And_Rescan_Renumber`; the stock-example row is
  `Stock_Example_Ascent_Runs_Against_Scripted_Telemetry` (the game's `kC_ascent_simple` runs to
  its `DONE` phase). `Scenario_Files_Run` and `Cli_Check_Exit_Codes` remain for B2/B3.
- **Corrections after the M1 code review (2026-11-25).** Read these before A4; they change §4.5,
  §4.7, §4.9 and §4.12 where those sections still say otherwise.
  - *Runs, not images.* Lists and the event rate limits belong to a **run**.
    `VehicleBinding.SyncRun` detects a new one by the computer's `TickGrid`: `VehicleComputer.TryStart`
    (behind start, `kfs reset` and stop → start) assigns a new instance, `Load`/`Stop`/`Fault` set it
    to null, and ticks — so every service call — happen only while it is set. `ServiceRouter.Answer`
    and `Accept` and `BindingPump.DrainAndApply` call it first; without a new run it is one reference
    compare. A new run rebuilds the lists and clears the limiter, which counts the run's own seconds
    (`k / tick_hz`, `k` restarting at 0), so a run can never be mistaken for an earlier one.
    **`ImageLoaded` is no longer chained** (§4.7 now says so): binding touches
    nothing of the computer, so re-binding cannot grow a delegate chain and the game's host keeps its
    slot. In `SimVehicle` the run's lists are frozen by the first frame's pump after a start: a test
    that wants a part to leave *during* a run detaches it after the first `Frame`.
  - *Registry gate.* `PartsRegistry.Bind` throws unless the extension is installed: `Open` is the
    install's last step, `Close` (with the unbinding) happens at uninstall under the same lock, so a
    dispatch thread holding an old `Fallback` cannot bind after an uninstall.
  - *Pump.* One rebuild per drain however many `x_parts_rescan` the batch holds (one `parts
    rescanned` event); every rescan counts as applied.
  - *Router.* A back-end exception while a request is checked rejects it (`internal error`, counted in
    `sys.rejected_requests`); so does the `ApplyRequest` hook's own last-resort catch. An id of the
    block the catalogue does not define cannot reach the router from an image (the assembler and
    `ImageReader` refuse a `SYSCALL` whose id is not in the tables); if called directly it answers 0
    and rejects `not a purrTTY service in this version`. `ServiceRouter.EntryFault` is an internal
    test seam.
  - *Back-end contract (for `KsaVehicleParts`).* Raw writes, and the raw reads `Apply` makes
    (`ReadLightType`, `ReadDurationS`, `ReadGoalS`), get the slot resolved at accept time, which may
    be of an **older generation**; its `Index` may name another part now. Reach the part through the
    slot object, never through `slot.Index`. `VehiclePartsBase.Apply` re-checks `IsPresent` itself.
    A back end that reads the game's `KeyframeAnimationModule.DeploymentState` overrides
    `ReadGameDeploymentState`; the base class converts it (D18).
  - *Installer.* Every step after the publication is undone on any exception (seams
    `InstallSeams.Step` and `RegisterHeader`); null option collections count as empty. Reused tables
    must carry this build's catalogue hash: the origin is `embedded:KSA+purrtty;catalog=<16 hex>`
    (`TableAugmenter.OriginFor`, a SHA-256 of the emitted rows and enumerations); any other purrTTY
    origin is refused as "another catalogue". The `LoadEmbedded` fallback is verified through
    `KfsIntegration.Tables`; when the game hands out other tables the postfix is removed and the
    install refused as "tables already built".
  - *`PartMath`.* Every entry point is total over all floats (NaN, ±∞, `FLT_MAX`); intensity and range
    have ceilings (D19); `X_STATE_*` renumbered (D18).
  - *Test runtime.* `SimVehicle.Reset()`/`Stop()`, `LastDispatchAllocatedBytes`,
    `SimVehicleParts.Builds`, `FaultyVehicleParts` throws from every member independently,
    `DecoyTablesField` (the publisher's `NotVisible` outcome). Measured per call through the VM and
    both hooks (`Tick_Allocation_Through_The_Hooks`): a setter allocates the same as a stock request
    (120 bytes, the VM's request), a getter 24–32 bytes more (the VM's result cell); the router and
    the hooks add nothing.

### 8.7 Implementation notes from the later tasks (A4, A5, B2, C1–C4, D1–D3)

Where the code of these tasks differs from the plan as it was written, and why. The sections
above were corrected where they said something else; this list is the record of what changed, for
someone comparing with an older copy or with the spikes. The code and its XML documentation are
authoritative.

- **A4 — game back end.**
  - *A seam the plan did not have.* `IKfsGameServices` (`Name`, `Prepare`, `Attach`, `Detach`,
    optional `Console`, `SystemHeadersInUse`) separates the installer from everything that exists
    only in the running game; `Game/KsaGameServices.cs` implements it. `new KsaGameServices()` has
    no console — so the offline tests never touch the game's `Program` — and
    `KsaGameServices.Create()` adds `KsaConsoleHost`. *Why:* the installer and its all-or-nothing
    rule stay testable offline, and the test runtime installs with no game services at all.
  - *The pump patch is required in every install*, the offline one included (its target exists
    in `KSA.dll`, and with no active pump the postfix does nothing): three required hooks plus one
    optional, not "two until A4".
  - *`ReleaseDead` is not relied on* (§4.7): it runs only while the kFS switch is on, so
    `GameHostSource` filters by `KfsVehicleHost.IsAlive` and the pump prunes what it did not see.
    `MainThreadPump` became an instance over `IPumpHostSource`, so the tests drive the real pump
    with `SimHostSource`.
  - *No `LightOps.cs` or `AnimationOps.cs`.* The decisions are pure functions in `KsaPartRules`
    (tested), the shared behaviour is `Runtime/VehiclePartsBase.cs`, and `KsaVehicleParts` holds
    only the raw game reads and writes.
  - *Config.* `KfsConfig` writes atomically with its own temporary-file-and-rename, because the
    display project's `AtomicFile` is internal. It has no test (`purrTTY.GameMod` has no test
    project); the plan's "config round-trip" test was not written.
  - *Checked against the decompiled game by the reviewer:* the patch targets, the pump's place in
    `Program.PrepareFrame`, the light and animation members. The same facts, with their lines, are
    in the bundle ([dispatch and threading](../docs/kfs-info/integration/dispatch-and-threading.md),
    [light module](../docs/kfs-info/game-targets/light-module.md),
    [keyframe animation module](../docs/kfs-info/game-targets/keyframe-animation-module.md)).
- **A5 — `purr`, examples, menu.**
  - *Namespaces.* The folder `Console/` is namespace `purrTTY.Kfs.Commands` (a namespace
    `purrTTY.Kfs.Console` would shadow `System.Console` in the whole project) and `Examples/` is
    `purrTTY.Kfs.ExamplePrograms` (`…Examples` would shadow the game's `Examples` type).
  - *More verbs than planned:* `lights reset [all]`, `kinc`, `services`; the registry
    (`PurrVerbRegistry`) lets the mod add `lsp` and `nvim` without `purrTTY.Kfs` knowing them.
  - *The command outlives the extension.* The mod registers `purr` itself ("standing"
    registration) so it exists with `enabled = false`; the installer's step 9 and that registration
    recognise each other's, so there is never a second `purr`. A failed registration never fails
    the install.
  - *One owner per process*, kept in `AppDomain` data, because the game's `help` throws on the
    same name under two owners (§4.13). *ASCII output by default* (decision D26).
  - *Light overrides are not reset automatically* (decision D5 confirmed); `purr lights reset` and
    a menu entry were added instead.
  - *The menu is its own file,* `purrTTY.GameMod/Kfs/KfsMenu.cs`, behind the hook
    `TerminalMenus.DrawKfsMenu`.
  - *Test helpers added:* `SimConsoleHost`, `SimVehicle.WriteGlobal<T>`, and `LoadKasm` over a
    dictionary of files (a folder project). Offline `KfsIntegration.Print` goes to the game
    log, so tests pass their own writer.
  - *Not written:* the "bundle document describing the shipped extension". The bundle documents
    the game and how a mod can extend it; this plan describes the extension.
- **B2 — project resolver and `purr-kfs`.**
  - `Runtime/ProjectResolver.cs` decides the file set and `Runtime/ProjectCompiler.cs` compiles
    it as the workbench would; `purrTTY.Kfs.Testing/ProjectBuilder.cs` joins them to a runtime.
    `purrTTY.Kfs.Tools.Tests/Runtime/ProjectParityTests.cs` checks both against the game's own
    `Workspace.ProjectOf` and `Workbench.BuildText` on a real folder.
  - The resolver refuses what the game would read but a tool should not: a path that leaves the
    workspace, a file over the 4 MiB limit, a FIFO or device. It keeps the game's include-scan
    quirks on purpose (§6.3).
  - Commands as built: `check`, `run`, `lsp`, `header`, `kinc`, `services [--markdown]`. The
    simulated vehicle is given by flags; there is no vehicle TOML file (it belongs to B3). The
    project packs as a .NET tool; publishing it as a release asset (decision D12) is not wired into CI.
- **C1, C2 — language-server core.**
  - *Tests live in `purrTTY.Kfs.Tools.Tests`*, not a `purrTTY.Kfs.Lsp.Tests` project (§6.9).
  - *Error codes:* a refused token `-32803` and close; a request before `initialize` `-32002`;
    after `shutdown` `-32600`. Diagnostics of the server itself carry code `KFS-LSP`. A hint is
    appended to the message as `\nhint: …`.
  - *One scheduler per connection* (decision D24), debounce 120 ms, compile limit 10 s, a 16 MiB
    below-normal thread started lazily, the public `Compiler.CompileOnThisThread`.
  - *Diagnostics for a file that belongs to several open projects are the union* of every
    project's diagnostics for it, and are published under the URI form the client used
    (`WorkspaceUris` resolves links such as `/var` on macOS).
  - *Standalone target:* layout flags, else the workspace-root `vehicle.h` (banner and
    fingerprint verified, watched), else one thruster on the bench processor — no "layout file".
  - *What a file needs to compile at all* (learned from the tests' fixtures): `#include <kfs.h>`
    — `uint32_t` is a typedef of `<stdint.h>`, which `<kfs.h>` includes, not a keyword — and
    `#pragma kfs claim ALLOCATION` before any write to the command block (`KC-E544`).
- **C3 — hover and completion.** As §6.5. Deviations from its table: no per-argument hover data
  beyond the row's prose; units are derived from name suffixes and labelled as derived; the static
  vocabularies are transcribed in `Index/StaticTables.cs`; the index is cached
  (`KfsIndexCache`). The kASM `SVC_X_*` symbols are offered from `<kfs.kinc>`, where the game puts
  them — `purrtty.kinc` holds only the `X_*` constants (§4.4).
- **C4 — in-game server.**
  - *`InGameLanguageHost` is in the library* (`purrTTY.Kfs.Lsp/InGame/`), not in
    `purrTTY.GameMod` as §6.8 first said, with the game behind `IKfsGameSide`
    (`KsaKfsGameSide.cs`); likewise `LspServiceCore`. *Why:* the queue, the tee, the info file and
    the limits are all testable without the game.
  - *The server starts regardless of the extension* and of the prototype switch (§6.2), not
    "when the extension installs".
  - *The wire contract* is §6.7 as rewritten: fixed verb lines; `kfs.load` refuses a path with
    whitespace; `ok` means "ran"; `subscribe` means "a live feed exists"; the log tail on
    subscribing; `KFS-TRAP`; at least one verb per frame within 4 ms; at most 8 waiting.
  - *R17 was real and is patched:* `KfsErrorEchoPatch` (§6.7).
  - *Hardening after the independent review* — every limit in §6.2, the header and framing
    rules, `LspThreads` — because the first version could be brought down, or made to leak
    threads, by a local peer that never authenticated.
  - *A new rule for test code* (§6.9): fake `ILiveSource`, never an interface with game-typed
    members.
- **D1 — terminal exit handling.**
  - *Two options, not one:* `CloseOnExit` and, added in the fix pass, `CloseOnSuccessfulExit`
    (decision D23) — the editor must not vanish when it fails to start.
  - *The resize guard is in the back ends* (`IProcessManager.Resize` is a no-op with no process),
    not at the two call sites the plan named, so every caller is covered; `TerminalSession.Resize`
    is the one entry both front ends use.
  - *The exit signal is the drained exit*, not `IsRunning` (§7.3). `SessionExitPolicy` (new,
    `purrTTY.Display/Ghostty/`) holds the last-tab decision so it is testable without ImGui.
  - Also fixed on the way: on Windows the `Exited` handler is attached before
    `EnableRaisingEvents` is set (a process that exited in between was never reported).
- **D2 — Neovim runtime files.**
  - *The filetype rule is a function*, not the path patterns of the first draft: patterns cannot
    compare Windows paths case-insensitively or follow links, and the set of roots is not known
    when the rule is registered. `*.kasm`/`*.kinc` are `kasm` everywhere.
  - *`syntax/kasm.vim` is a checked-in snapshot* refreshed by a test, not generated at build time:
    the mod's build must not need the game's assembler, and the directive list exists only in the
    assembler's code.
  - *More than the plan listed:* `text.lua` (server text made safe to display), `:KfsTarget`,
    `:KfsManual`, `:KfsVerb`, `:KfsInfo`, `:KfsRestart`, `status()`, the options of §7.4, the
    reconnect rules of §7.4, the token removed from Neovim's environment.
  - *Only Neovim 0.12.6 was run*; the files gate at 0.11.
- **D3 — launcher.**
  - `NeovimLaunchPlan.For(NeovimLaunchRequest)` (with `ForThisProcess`) instead of a five-argument
    call; the result carries `Resolution`, `Executable`, `Arguments`, `NeovimArguments`,
    `Environment`, `RemovedEnvironment`, `WorkingDirectory`, `UsesRuntimeFiles`, `Description`,
    `Error`.
  - *Changed from the plan's sketch:* `--` before the file; `PURRTTY_CONFIG_DIR` and `SHELL`
    passed, `FORCE_COLOR` and `CLICOLOR_FORCE` removed; a configured `nvim_path` that is wrong is
    an error, not a fallback; the login-shell script waits for Enter when Neovim is not found
    instead of `exec "$@"` failing in a window that closes; `$SHELL` is used only when it is an
    `sh`-compatible shell; `-i` is not used.
  - *`TargetActiveVehicle()` is conditional* (decision D27): only with the prototype switch on
    and a controlled vehicle.
  - *A new file is not written by the mod* and listing the workspace does not create its folder;
    only a launch creates the workspace folder.
  - *No "New file…" menu entry;* the menu checks whether Neovim can be launched when it opens and
    shows the reason.
  - *T4 by exclusion:* kFS editor terminals are left out of saved layouts.
- **Integration and fix passes — changes to M1 code.**
  - *The simulator's frame order* is step → pump → dispatch (§5.4); it was pump → step → dispatch.
  - *`X_PARTS_GENERATION` continues* per computer when a vehicle is bound again
    (`VehiclePartsBase.ContinueAfter`), so a program never sees the generation go backwards.
  - *The pending-command queue is a ring buffer* (§4.7): `Enqueue` runs on the solver thread,
    many times a frame at time warp, and must be O(1).
  - *`ServiceRouter` asks the host's event limiter first* (`ConsoleLog.EventAllowed`) and formats
    a rejection text only when the line would be written; the rejection is counted either way.
  - *Catalogue wording:* lists are ranked by the **module's** instance id; `X_LIGHT_LIT` reads 1
    for an interior light whatever the camera shows. The snapshots and the shipped header text
    were regenerated.
  - *Menus:* every `BeginMenu`/`EndMenu` pair of `TerminalMenus`, `KfsMenu`, `ThemeDialog`,
    `LayoutManagerUI` and `InWorldManagerUI` ends in a `finally`. The pairs in `TerminalWindow.cs`
    and `InWorldTerminalRenderer.cs` were left as they were.

## 9. Risks

State after the implementation: no risk below has been observed or ruled out in the game, because
nothing has run there. "Built" in the last column means the mitigation exists and is tested
offline; R18–R23 were added by the implementation and its reviews.

| # | Risk | Likelihood | Impact | Mitigation |
| --- | --- | --- | --- | --- |
| R1 | A game update reshapes a patch target, the service-row schema or the header mechanism. kFS is a prototype and will change. | High over time | Extension stops working | All-or-nothing install with a logged reason (§4.5); offline tests run against each new `KSA.dll` and fail first; `upgrade-ksa` checklist gains the kFS items in [risks and compatibility](../docs/kfs-info/modding/risks-and-compatibility.md). |
| R2 | A stock update claims an id in `0x5000–0x50FF`. | Low | Tables rejected at the dry run → extension disabled, kFS intact | The dry run (§4.5 step 4) turns it into a log line; moving the block is a one-constant change, and images are rebuilt from source on every load anyway. |
| R3 | Stock kFS gains lights/animation control itself. | Plausible | Duplicate features | Keep the mod's names prefixed (`x_`), retire them when stock covers the feature. |
| R4 | A patched method is inlined at its call site under the game's runtime, so a hook silently never fires. | Low for the three required targets (large bodies, virtual/interface dispatch); see R18 for the small ones | A service is rejected or returns 0 | Built: `purr status` shows `pump frames`, `accepted` and `answered`; in-game checklist item V2 is the test. |
| R5 | Patches are applied after another mod (or the game) has already built the tables. | Low | Extension disabled for the session | Detected (§4.5 step 5) and reported. Nothing in the game reads the tables before mods load, so this takes another mod; the remedy would be to publish the tables from a `[StarMapBeforeMain]` hook (**unverified** — not tried). Checklist V1. |
| R6 | A mod exception inside a dispatch faults the player's flight computer. | Would be severe | Loss of control mid-flight | Every patch body is `try`/`catch`; tests inject faults into the router and assert the computer keeps running. |
| R7 | Reading live modules from the dispatch thread races a main-thread writer (another mod, the part window); and the part lists are collected on the dispatch thread at a run's first service call. | Low | A stale or torn read of one value for one tick; at worst an exception that rejects one request | Reads are single fields and bounds-checked span reads; nothing is cached; a back-end exception rejects the request instead of faulting the computer. The lists rest on the reading that the part tree is restructured only in the solver-idle window (checklist V31, V32). If in-game testing shows trouble, switch `KsaVehicleParts` reads to a per-frame snapshot built in the pump (the interface does not change). |
| R8 | Light template cloning conflicts with another mod doing the same (zippo, gatOS). | Medium when both are active on one light | Last writer wins; a reset may restore the other mod's template | `EnsurePrivate` re-clones from whatever template is current and only restores when the template is still ours. |
| R9 | An animation driven by its own state machine (parachute bay doors) fights the program. | Certain for those parts | Confusing behaviour | `LINKS` bit 2 marks them; the console listing says so. |
| R10 | Programs polling services keep the vehicle off rails and limit time warp. | Inherent to any running program | Performance at high warp | Documented; examples use low tick rates. |
| R11 | The in-game language server's compiles cost frame time, or a stuck compile leaves a 16 MiB thread running in the game. | Low | Stutter; a leaked thread | Built: one below-normal compile thread per connection, a debounce, a 10 s watchdog, at most two retired threads in the whole process (§6.3). Concurrent compiles against one tables instance are safe (read from the code and stress-tested in spike 05), so the stock editor is unaffected. Frame cost is checklist V22. See R20 for what the threads share with the game. |
| R12 | A local process other than the player's editor connects to the in-game server. | Low | Could build, load, start, stop and reset programs on the vehicle | Built: loopback only, random port by default, a 256-bit token new at every start and required at `initialize`, the info file owner-only on Unix, and the limits of §6.2. Arbitrary `kfs` lines (`kfs.verb`) are off unless `lsp_allow_vehicle_commands = true`; the fixed commands are always available to a client that has the token. The token stops other users and machines, not another process of the same user (it is in the info file). See R19. |
| R13 | Neovim is not installed or not on the game's `PATH`. | Common on macOS/Steam launches | "Open in Neovim" fails | Built: configurable absolute path, login-shell fallback with a message that waits for Enter, the reason shown in the menu; the feature degrades to nothing else. On Windows a `.cmd` shim is detected and reported, not started (T3 open). Checklist V43, V52–V54. |
| R14 | Terminal key-handling gaps make Neovim awkward (toggle hotkey swallowed, some Ctrl/Alt chords, no focus events). | Known | Usability | Tasks D5–D7, **not built**; none blocks the feature. Checklist V46 records what does not arrive. |
| R15 | Redistribution: `KSA.dll` must never be copied into the repository, test output or tool packages. | — | Legal | Compile-only references, in-memory resolver, CI check that `KSA.dll` appears in no test output and no game assembly in the mod dist or a tool package. |
| R16 | The property enumerations are positional and baked into images. | Certain if mishandled | Programs built earlier read the wrong property | Fillers reserve room; new properties replace a filler and widen the sub-range; the self-check and a snapshot test pin the lists. |
| R17 | Installing the output tee changes where `KfsIntegration.Error` lines appear. | **Confirmed by reading the code**: with a writer other than its own console writer in `Output`, `KfsIntegration.Error` writes the line to `Output` as well as to the log, and the console window shows log errors by itself — every `kfs` error would appear twice | Cosmetic | Built: the optional patch `KfsErrorEchoPatch` marks the call so the tee passes the line to editors but not to the console (§6.7); it acts only when the writer the tee replaced is the game's own. If `Error` was inlined (R18) errors appear twice. Checklist V38. |
| R18 | **Small patched methods are inlined.** Two of the mod's targets are tiny: `KfsIntegration.Error` (a few lines; target of the error-echo patch) and the base `HostComputer.ApplyRequest` (one line, virtual; target of the optional bench patch). If the JIT inlined them into callers compiled before the patch was applied, the patch never runs. The pump's own target, `KfsIntegration.BeforeInputEvents`, is larger (a lock and a loop) but equally unproven in the game. | Unknown — cannot be decided offline | For the two optional patches: cosmetic (errors twice in the console; the stock bench message). For `BeforeInputEvents`: nothing would ever be applied | The two are optional and outside the all-or-nothing set; each has a visible test — V38 and V27 — and the pump has V2 (`pump frames`). No offline test can show inlining. |
| R19 | **The language server listens by default.** With the default `lsp_enabled = true` the game opens a loopback TCP port and writes a token file at every start, whether or not the player ever uses an editor; any local process that can read the file can build, load, start, stop and reset programs on the targeted vehicle. | Certain (it is the default) | The exposure of R12, for every player of the mod | Stated plainly in §11 (decision D22) and in the comments of the generated `kfs.toml`; `lsp_enabled = false` removes it entirely (V42); the limits of §6.2 bound what an unauthenticated peer can cost. Whether the default should be off until first use is an open product question. |
| R20 | **Compile threads inside the game process share unsynchronised statics with the game.** The game's compiler reads `SystemHeaders.Files`, `SystemHeaders.Builtins` and the tables without a lock; a compile cannot be cancelled; a hung one keeps its 16 MiB thread. A change to those collections while any compile runs — the mod's own at unload, another mod's at any time — could corrupt them. | Low | A corrupted dictionary inside the game's compiler; a leaked thread | Built: the server stops and joins its threads before the extension uninstalls; the uninstall leaves `<purrtty.h>` registered while a compile is counted as running or the stock editor is building (§4.5); retired threads are capped (§6.3). Not covered: another mod changing the same statics. |
| R21 | **Game-only code has never executed.** `KsaVehicleParts`, `GameHostSource`, `KsaConsoleHost`, `KsaKfsGameSide`, `KfsLspService`, `KfsErrorEchoPatch`, `KfsNeovimLauncher`, `KfsMenu` and `KfsConfig` are compiled against the game and audited against the decompiled sources, but no test runs them. | Certain | Unknown until the first in-game run | Kept thin, with the decisions in tested types; §10.1 exercises them first. |
| R22 | **The colour space is unknown.** The shipped header text claims only that each component is clamped to 0–1 and given to the game unconverted (an earlier text said "linear RGB"; that claim had no support and was removed); the game's C# passes the three floats to the renderer unconverted, and what the lighting shader does with them was not read. | Unknown | A documented colour looks different from the same numbers elsewhere in the game | Checklist V29 decides; only documentation changes either way. |
| R23 | **Overrides outlive programs by design** (decision D5): a program that traps or is replaced leaves its colours and cones on the lights. | Certain | A player sees odd lights and no running program | `purr parts` marks them, `purr lights reset [all]` and the menu restore them, a reload or the mod's unload does too (V30). |

## 10. In-game validation checklist

**Nothing below has been done yet.** This is the master list of what must be checked in the
running game; it is written so that a player can walk it in one sitting, top to bottom, on one
operating system, and then repeat the last group on the others. Ids are stable: V1–V23 are the
original items (their text now describes what was built), V24 onwards were added from the
implementation and review reports. Each item says what to do, what should be seen, and — in
italics — what a failure would mean, that is, which assumption the item tests.

Preparation: a save with a vehicle that has at least two lights of the same part type, an
interior light, a solar array, a cargo bay or other slider-animated part, and a decoupler below a
part that carries a light; `nvim` 0.11 or newer installed. Keep the game's log open
(`purr status` and `purr lsp` repeat what the log says). `<config>` is the folder that holds
purrTTY's `.purrTTY` folder; by default it is `<Documents>/My Games/Kitten Space Agency`.

### 10.1 First: does anything work at all?

These five decide whether the rest is worth doing. If one fails, stop and report it with the log.

- **V1 — the extension installed.** Start the game with the mod. The log has one line
  `purrTTY kFS extension: active — 22 services 0x5000–0x5033, 3 hooks (+1 optional), tables published, game back end, purr command`.
  *A line `… disabled — <reason>` names the broken assumption: `tables already built` — something
  read the game's kFS tables before the mod's patches ran (patch timing under StarMap, or another
  mod; R5); `target … not found` or `game back end unavailable` — this game build renamed a
  member the extension needs (R1); `tables rejected` — the game's loader refused the mod's rows
  (R2). `(+0 optional)` only means the bench message of V27 is the stock one.*
- **V24 — the configuration file exists.** `<config>/.purrTTY/kfs.toml` was created, with
  comments and the six keys of §7.5. *Missing: the mod resolved another configuration folder than
  the one documented; the switches below cannot be found by a player.*
- **V3 — the tables reached the game.** `kfs on`, open the kFS reference window: the Services
  page lists the `x_*` rows with their summaries. *Missing rows although V1 said `published`: the
  game is not using the tables instance the mod published.*
- **V2 — the three hooks fire.** Take control of the vehicle. `purr examples`, then
  `kfs load x_blink/blink.c --vehicle` and `kfs start`. After a few seconds `purr status` shows
  `pump frames`, `requests … accepted` and `… answered` all non-zero and growing between two
  calls. *This is the only evidence that the patched methods are really called (R4, R18).
  `pump frames 0`: the `KfsIntegration.BeforeInputEvents` postfix never runs — no command is ever
  applied. `accepted 0` with `rejected x_…: no vehicle to act on` in the program log (`kfs state`):
  the `VehicleComputer.ApplyRequest` postfix does not run. `answered 0` and every count reads 0:
  the `IssueResult` postfix does not run.*
- **V5 — a light switches.** With `x_blink` running, the vehicle's switchable lights blink once a
  second; the part window's light checkbox follows; another vehicle of the same type is
  unaffected. *No blinking although V2 passed: the write to `PowerConsumer.LightIsActive` does not
  have the effect read from the code, or the pump applies it at a point where the game overwrites
  it.*

### 10.2 Install and status

- **V25 — one `purr` command.** `help` in the game console lists `purr` exactly once and does not
  fail; `purr` alone lists the verbs. *`help` throwing: the command is registered under two
  owners (§4.13).*
- **V26 — the switch.** Set `enabled = false` in `kfs.toml` and restart. The log says
  `purrTTY kFS extension: disabled — switched off`; `purr status` works; `purr parts` answers
  `… the purrTTY kFS extension is not active (…)`; the kFS menu shows the reason and greys the
  three extension entries; the terminal, `purr lsp` and `purr nvim` work. A kC file that includes
  `<purrtty.h>` now fails to build, in the game and in the editor alike. Set it back to `true`.
  *Anything of the terminal failing here would mean the extension is not as independent of it as
  §3.5 requires.*
- **V14 — no regression.** A stock example (`kC_ascent_simple`) still builds, loads and flies
  with the mod present.
- **V27 — the bench.** `kfs target bench`, `kfs load x_blink/blink.c`, `kfs start`, `kfs run 2`
  (the bench runs only when told to), `kfs state`: the program runs, the getters read 0 silently, and the setters are rejected with
  `x_ services need a vehicle (the bench has no parts)`; `purr status` shows `bench: … claimed`.
  `kfs target vehicle` afterwards. *The stock text `no vehicle to act on` instead: the optional
  hook on `HostComputer.ApplyRequest` — a one-line virtual, the likeliest of all to be inlined —
  is not called. Cosmetic only.*

### 10.3 Services and examples

- **V28 — the examples.** `purr examples` created `x_blink`, `x_light_show`, `x_deploy`,
  `x_actuator`, `x_parts` and `x_blink_kasm` under `<Documents>/My Games/Kitten Space Agency/kfs/`,
  and the stock editor's file tree shows them. A second `purr examples` reports every file as
  kept; after editing one, `purr examples --force` overwrites it. `kfs build x_blink_kasm/blink.kasm`
  builds (it finds `purrtty.kinc` beside it and `SVC_X_…` in `<kfs.kinc>`).
- **V4 — the listing.** `purr parts` lists the vehicle's lights, deployables and actuators with
  sensible names; with `x_blink` running the heading says `as its running program sees them`. The
  interior light shows `no switch` and `lit` — also while the camera is inside the vehicle.
- **V6 — appearance is per light.** `kfs load x_light_show/light_show.c --vehicle`, `kfs start`:
  colour, cone and intensity change on the spot lights; a second light of the same part type on
  **another vehicle** keeps its stock look. `kfs poke stop 1` restores them.
  *A stock light elsewhere changing too: the template was not cloned per light (R8).*
- **V29 — colour.** In `light_show`, or with a small program, set a light to (1, 0, 0), then
  (0.5, 0.5, 0.5). Compare with a part whose stock colour is known. *The header says only
  that the components reach the game unconverted; nothing in the game's C# converts them, and
  whether the lighting shader treats them as linear is not known. This item records the answer
  for the documentation.*
- **V30 — overrides outlive the program.** While `light_show` shows a colour, `kfs stop`: the
  lights keep that look. `purr parts lights` marks them `overridden`. `purr lights reset` restores
  the stock look (and says how many); `purr lights reset all` does it for every vehicle.
- **V7 — deploy, and the read-back timing.** `kfs load x_deploy/deploy_when_safe.c --vehicle`,
  `kfs start` above the atmosphere (or deploy by hand): the array deploys at stock speed, solar
  output starts only when deployed, and `STATE` and `POSITION` read back correctly. With a
  program that logs the tick in which it commands a deploy and the ticks in which `TARGET`,
  `MOVING`, `POSITION` and `STATE` first change: `TARGET` and `MOVING` change one tick after the
  command, `POSITION` and `STATE` one tick later still (at a tick rate at or below the frame
  rate). *Different offsets: the frame order of §4.12 is not what the game does, and the
  simulator (§5.4) must be corrected again.*
- **V8 — an actuator.** `x_actuator/actuator_sweep.c` on a cargo bay: the part follows the
  target; the stock slider shows the same value.
- **V9 — parts that leave and arrive.** Stage away a part that has a light a running program
  addresses: the program keeps running, the slot reads not present, writes are rejected with
  `light N is no longer on this vehicle`. Dock two vehicles: `X_PARTS_DIRTY` becomes 1;
  `x_parts_rescan()` renumbers and the generation goes up.
- **V10 — pause.** Pause while a program is switching lights, unpause: no exception; commands
  issued before the pause are applied while paused, nothing new is issued.
- **V11 — time warp.** At 10× and at 100× or more with a 50 Hz program that polls services: no
  host fault. Note the frame time and whether the program log shows `skipped N tick(s) …` or
  `x_ commands: queue full, dropped the oldest …`. *(budget, R7, R10)*
- **V31 — the listing under load.** At time warp with a program running, `purr parts` several
  times: no exception in the log, a complete table each time. *An exception: the console reads
  the frozen lists while a dispatch thread replaces them.*
- **V32 — lists built on a solver thread.** `kfs reset` and `kfs start` a program repeatedly
  while the part window of a light is open and its checkbox is being clicked, while a kitten
  boards or leaves, and right after docking. No exception, no host fault (`kfs state`), and the
  lists are right afterwards (`purr parts`). *A fault here: the part lists are collected on the
  dispatch thread at a run's first service call, on the assumption that the part tree is only
  restructured in the solver-idle window (R7).*
- **V12 — save and load.** Save and reload with overrides active: the game loads; light
  **appearance** is back to stock, while switch states and animation goals are as saved (the game
  saves those itself); the program is gone (as stock kFS).
- **V15 — without the mod.** Remove the mod and load a program built with it: a clean
  "unknown service id" rejection.

### 10.4 Console and menu

- **V33 — console text.** The output of `purr status`, `purr parts` and `purr examples` is plain
  ASCII (`-`, `->`, `...`) with no missing-glyph boxes; part names appear as the game shows them
  elsewhere.
- **V34 — the kFS menu.** purrTTY menu › kFS: the status line; **List parts in console**,
  **Reset light overrides**, **Write example programs** and **Extension status in console** do
  what the verbs do; the server line shows the address and client count; **Copy server address**
  puts `127.0.0.1:<port>` on the clipboard. Opening and closing the menu repeatedly, also with no
  vehicle controlled, leaves the game's other menus intact. *Other menus disappearing or an ImGui
  assertion: a `BeginMenu`/`EndMenu` pair is unbalanced.*

### 10.5 Terminal exit handling

- **V21 — the editor's window closes.** Open a file in Neovim (V17) and quit with `:q`: the
  terminal window closes by itself. In an ordinary terminal window type `exit`: the window stays
  with its last screen (its tab label reads ` (exit 0)` when the tab bar is shown, that is, with
  two or more tabs) and can still be dragged and resized without an exception in the log.
- **V35 — a single-tab window that closes.** After V21's first half the terminal is hidden if
  that was the last window; the game's own keys work at once; toggling the terminal again shows a
  fresh window at the position and size the closed one had. With a layout whose terminal has
  `close_on_exit = true` and two tabs: `exit` in one tab closes only that tab.
- **V36 — a failed editor keeps its window.** Set `nvim_path` to an existing program that exits
  with an error at once (on macOS or Linux `/usr/bin/false`), then kFS › Open workspace in Neovim:
  the window opens and stays (` (exit 1)` in its tab label when the tab bar is shown). Clear
  `nvim_path` again. *The window vanishing: a
  failed launch would be invisible to a player.*
- **V37 — in-world.** Set `nvim_open_in = "inworld"`, `purr nvim x_blink/blink.c`: a 120×40
  billboard terminal appears in front of the camera with focus, and the text is readable; `:q`
  removes it with no error in the log on that or the next frames; an in-world terminal that was
  created from a layout with `close_on_exit` behaves the same. Set the key back to `"window"`.

### 10.6 Language server

- **V16 — the server runs.** The log has `purrTTY kFS LSP: listening on 127.0.0.1:<port>,
  workspace …`; `purr lsp` prints the same address, the workspace, the target and the info file;
  `purr lsp token` prints a token; `<config>/.purrTTY/kfs-lsp.json` exists and names that address.
- **V38 — errors appear once.** `kfs nope` in the game console: the line
  `unknown kfs command 'nope' …` appears **once**. *Twice: the game's `KfsIntegration.Error` — a
  method of a few lines — was inlined into its callers before the mod patched it, so the optional
  error-echo patch has no effect (R17, R18). Cosmetic.*
- **V19 — the target.** `THRUSTER_COUNT` and `commands.*` resolve against the **targeted
  vehicle's** layout; switching vehicles and `:KfsTarget vehicle` (or `kfs target vehicle`)
  updates the diagnostics of open buffers without an edit.
- **V22 — cost on the game.** Frame time with the server idle; while typing quickly in a
  500-line file; and while `:KfsLog` is open with a program running (a client subscribed to
  `state` keeps the vehicle's computer publishing a snapshot every tick). *(compile threads and
  the snapshot lease in the game process, R11)*
- **V39 — a trap.** Load and start a program that divides by zero in its task: `kfs state` and
  `:KfsLog` show `trap DIV_ZERO … : commands held`; the editor shows an error with code
  `KFS-TRAP` on that line; a `kfs_logf` in the trapping tick does not appear; `kfs reset` clears
  the diagnostic.
- **V40 — the port is taken.** Set `lsp_port` to a port another program is listening on and
  restart: the terminal and the extension work; `purr lsp` and the kFS menu say why the server is
  not running. Set it back to 0.
- **V41 — restart with the editor open.** With Neovim attached, `purr lsp restart`: Neovim reports
  the connection lost and attaches again within a few seconds (a new port and token, found
  through `PURRTTY_CONFIG_DIR`); diagnostics return.
- **V42 — switched off.** `lsp_enabled = false`, restart: the log says the server is off; nothing
  listens; there is no `kfs-lsp.json`; `purr nvim` still opens the editor, which then falls back
  to `purr-kfs` or to syntax only. Set it back.

### 10.7 Neovim

- **V17 — open and diagnose.** purrTTY menu › kFS › Edit in Neovim... lists the workspace's
  files; choosing one opens a terminal window titled `kFS: <file>` running `nvim` on it, with
  keyboard focus — also when the terminal was hidden before. The console says which Neovim was
  started and what `kfs` targets. The buffer shows diagnostics for a deliberate error within a
  second of typing; fixing it clears them.
- **V18 — the mod's services.** A program using `<purrtty.h>` shows no false errors; `K` on an
  `x_*` call shows its documentation; completion after `x_light_` offers the services, and after
  `commands.` the layout's fields.
- **V20 — run from the editor.** `:KfsLoad` and `:KfsStart` load and start the program on the
  vehicle; `:KfsLog` shows the log's last lines and then new `kfs_logf` lines as they come;
  `:KfsParts` shows the tables; `:KfsBuild` with an error reports it and the diagnostics agree.
- **V43 — Neovim cannot be found.** With `nvim_path` set to a file that does not exist, the two
  Neovim menu entries are greyed and the reason is under them; `purr nvim` prints the same
  reason. Where Neovim is found only through the login shell (V52), temporarily hide it there:
  the terminal shows the not-found text and `Press Enter to close this terminal.` and waits.
- **V44 — the editor's environment.** In the launched Neovim: `:echo $SHELL` is your own shell;
  `:echo $FORCE_COLOR $CLICOLOR_FORCE` is empty; `:echo $PURRTTY_KFS_TOKEN` is empty although
  the client is attached (`:KfsInfo`); `:!ls` output has no colour codes.
- **V45 — a new file.** `purr nvim newdir/new.c`: the console says `(new file: :w creates it)`,
  nothing exists on disk yet; `:w` creates the folder and the file, and the buffer gets
  diagnostics. `purr nvim ../outside.c` and `purr nvim notes.txt` are refused with a reason.
- **V46 — keys.** Esc, Ctrl-W chords, Alt chords and the function keys other than the toggle
  hotkey reach Neovim; note every key that does not (§7.3 lists the known ones).
- **V47 — closing with unsaved text.** With a modified buffer, close the window with its "x",
  and separately quit the game: nothing hangs, and Neovim's swap file allows recovery on the next
  open.
- **V48 — layouts.** With a kFS editor window open next to an ordinary terminal, save a layout:
  the saved file contains the ordinary terminal only.
- **V49 — targeting follows the switch.** With `kfs off`, `purr nvim x_blink/blink.c` says
  `the kFS prototype is off: diagnostics use the bench layout`, and afterwards `kfs on`,
  `kfs target` still reports the bench. With `kfs on` and a controlled vehicle it says
  `kfs targets <vehicle>`.
- **V50 — filetypes.** `:set ft?` is `kc` for a `.c` file in the workspace, `c` for a `.c` file
  elsewhere, `kasm` for `x_blink_kasm/blink.kasm`, whose mnemonics and directives are highlighted.
- **V23 — outside the game.** A second Neovim started outside the game in the same workspace,
  with the runtime files loaded (§7.6), attaches through the server info file; with the game
  closed it falls back to `purr-kfs lsp --stdio` when that is installed, and `:KfsBuild` then
  answers that it needs the game.

### 10.8 Unload and reload

- **V13 — switches and scene changes.** `kfs off` / `kfs on`, a star-system change, with programs
  that had been running and lights overridden: no exception, no leaked overrides after the mod
  unloads.
- **V51 — unload, then load again.** Unload the mod through StarMap: `purr` is gone from `help`;
  the server's port no longer accepts connections; `kfs-lsp.json` is gone; overridden lights are
  back to stock; an attached Neovim reports the connection lost. Load it again in the same game
  session: the log line of V1 now says `tables reused` instead of `tables published`, `help` lists
  `purr` once, and V2 passes again. *`purr` listed twice or `help` throwing: the process-wide
  owner object was not reused. A log line `<purrtty.h> left registered: …` at unload is expected
  only when the stock editor was building at that moment.*

### 10.9 Operating-system specifics

Repeat V1, V2, V5, V16, V17, V21 on each system, then:

- **V52 — macOS, started from Finder or Steam.** With `nvim` installed only by Homebrew and
  `nvim_path` empty: kFS › Edit in Neovim... starts it through the login shell (the console says
  `nvim via login shell (…)`). *The not-found text instead: the login shell's `PATH` comes from a
  file a non-interactive login shell does not read (§7.2); the remedy is `nvim_path`.*
- **V53 — Linux.** The same with `nvim` in `~/.local/bin` or an AppImage on a `PATH` that only
  the login shell has; and with `nvim` on the game's own `PATH` (the console says
  `nvim <path> (found on PATH)`).
- **V54 — Windows.** `nvim.exe` on `PATH` or in `nvim_path` (a single-quoted TOML string): it
  starts under ConPTY, the environment of V44 is as described, resizing works while it runs and
  after it has exited, and `:q` closes the window. With Neovim installed only as a Scoop or
  winget shim (`nvim.cmd`): the menu says a script shim cannot be started and asks for
  `nvim_path` (terminal row T3 is open). The ConPTY tests of resize after exit run only on
  Windows and were skipped in the run that produced this document's totals, so treat this item as
  the first evidence for that code path.

## 11. Open decisions

Where the table stands after the implementation: D1, D18 and D19 were decided earlier; for D2–D11
and D13–D17 the **recommendation is what was built** and is now the behaviour the checklist of §10
tests (they can still be revisited after the first in-game run); D12 is open — the tool packs but
is not published; D20–D29 were taken during the implementation and its reviews.

| # | Decision | Recommendation | Alternatives |
| --- | --- | --- | --- |
| D1 | Function prefix and header name | **Decided (2026-11-25): `x_` prefix** — functions `x_light_set_on`…, constants `X_LIGHT_ON`…, kASM symbols `SVC_X_…`, enum keys `x_light_prop`…. The header stays `<purrtty.h>` and the console command `purr` (assumed; not part of the decision). The spikes ran with the earlier spelling `purr_` / `PURR_`; only the names differ. | `purr_`; `kfs_` |
| D2 | Light index unit | Every `LightModule` (emitter), with `SWITCH_GROUP` exposing shared switches | One index per switchable part |
| D3 | Switchless lights | Listed, `HAS_SWITCH` 0, on/off rejected | Hide them; or fake off with range 0 (untested in game) |
| D4 | When lists are rebuilt | At program load and on explicit rescan | Also automatically when the vehicle changes (indices would move under a running program) |
| D5 | Overrides when a program stops | **Decided (2026-11-26, built): kept** until reset, reload or mod unload — a light stays as commanded, like any hardware. Nothing resets them automatically, not a stop, a trap, a reload of the program or a new run. The player's way back is `purr lights reset [all]` and the menu entry (§4.10); `purr parts` marks overridden lights | Auto-reset on stop or halt |
| D6 | Symmetry | Not mirrored | Mirror like the stock buttons (opt-in argument) |
| D7 | Should parts services honour `Vehicle.IsControllable`? | No — the stock part window does not either | Reject in fallback mode |
| D8 | Colour range | Clamp to 0–1 like gatOS and zippo | Allow > 1 (HDR) once confirmed in game |
| D9 | Id block | `0x5000–0x50FF` | Any other unused block |
| D10 | Where the language server runs by default | Inside the game, on a loopback socket (no .NET needed on the player's machine, real vehicle layout, can act on the vehicle). Built so; see D22 for what that means for every player | Standalone only |
| D11 | Server-side vehicle commands (`load`, `start`, `stop`) | Built: the fixed commands (`kfs.build`, `load`, `start`, `stop`, `reset`, `target`, `manual`, `parts`) are always available to a client that has the token; only `kfs.verb` — any `kfs` line — is behind the switch `lsp_allow_vehicle_commands`, default off | Diagnostics-only server; a switch for every command |
| D12 | Distribution of `purr-kfs` | Separate release asset (framework-dependent), not in the mod folder | Bundle in the mod; self-contained per-OS builds |
| D13 | Neovim runtime files | Shipped in the mod and loaded by absolute path (`--cmd "lua dofile(…)"`, folder passed in an environment variable) for the in-game launch; documented for manual install | Publish as a standalone Neovim plugin repository |
| D14 | kASM selector constants | A generated `purrtty.kinc` the program includes | Patch `BuiltinIncludes` so `<kfs.kinc>` carries them |
| D15 | Bench behaviour for mod services | Optional patch 4: getters silent, setters rejected with a clear message | Leave the stock "no vehicle to act on" for every call |
| D16 | Console command | The mod's own `purr` command, registered like `kfs` (no patch, works with the kFS flag off) | Patch `KfsConsoleCommands.Execute` to add `kfs parts` verbs |
| D17 | How the tables are published | Assign `KfsIntegration._tables` while it is null; Harmony postfix on `LoadEmbedded` only as a fallback | Postfix only (the variant the spikes ran) |
| D18 | Values of `X_DEPLOY_STATE` | **Decided (2026-11-25, following the M1 code review): `X_STATE_NONE` 0, `X_STATE_DEPLOYED` 1, `_RETRACTED` 2, `_DEPLOYING` 3, `_RETRACTING` 4, `_BROKEN` 5**, so the 0 every getter reads where there is no part is never a state (`STATE == X_STATE_DEPLOYED` was true for an absent part). Converted from the game's 0–4 at the boundary (`PartMath`). Other properties keep 0 as their "nothing"; programs check `PRESENT` first. | Keep the game's 0–4 (0 = deployed) |
| D19 | Ceilings for intensity and range | **Decided (2026-11-25, following the M1 code review): at most 1e6 (intensity) and 1e6 m (range)**, `PartMath.MaxIntensity` / `MaxRangeM`; the VM's `finite:>=0` domain admits up to `FLT_MAX` | No ceiling; a tighter one near the stock values (≤ 50) |
| D20 | Order of one simulated frame | **Decided (2026-11-26, following the fix pass): step the parts → pump → dispatch**, the order read from the decompiled game (§4.12, §5.4), so that committed state (`POSITION`, `STATE`, and `LIT` when switching on) lags the live fields by one frame in tests as it will in the game | pump → step → dispatch (the first version; one frame too optimistic) |
| D21 | Lifetime of the `purr` console command | **Decided (built): for as long as the mod is loaded**, registered by the mod itself, independent of the extension; verbs that need the extension refuse with the reason | Register only when the extension installs (then `purr status` could not explain a disabled install) |
| D22 | Language server: lifetime and default | **Decided (built), and to be stated plainly to players: on by default.** With `lsp_enabled = true` the game listens on a loopback port and writes its address and token to `<config>/.purrTTY/kfs-lsp.json` at every start, whether or not an editor is ever used, and independently of the extension and of the kFS prototype switch. Any local process of the same user that reads the file can build, load, start, stop and reset programs on the targeted vehicle (R12, R19). `lsp_enabled = false` switches all of it off | Off until the first `purr nvim` or `purr lsp start`; tied to the extension |
| D23 | When the editor's terminal closes | **Decided (built): only on a successful exit** (`CloseOnSuccessfulExit`, exit code 0); a Neovim that fails keeps its window and final screen | Close on any exit (`CloseOnExit`, as T1 first specified) |
| D24 | Build schedulers | **Decided (built): one per connection**, each with its own lazily started compile thread; the cap on retired threads is process-wide | One scheduler for all clients (fewer threads; one client's stuck compile would block the others) |
| D25 | Server limits | **Decided (built): the table of §6.2** — 8 connections, 2 unauthenticated, 5 s to `initialize`, 64 KiB before and 16 MiB after authentication, 8 commands waiting, one command per frame plus what fits in 4 ms, 10 s per compile, 2 retired threads | No limits (the first version; an independent review showed it could be brought down by a local peer) |
| D26 | Characters in console output | **Decided (built): ASCII by default** (`PurrConsole.AsciiOutput`), because the console font's coverage cannot be checked from the mod | Typographic characters (`—`, `→`, `…`), to be enabled if V33 shows the font has them |
| D27 | Retargeting the workbench when the editor opens | **Decided (built): only with the kFS prototype switch on and a vehicle controlled.** `Workbench.TargetActiveVehicle()` creates a vehicle host and switches what `kfs` acts on without checking the switch; with the switch off that is left to the player and the launch message says so | Always retarget (diagnostics would always match the vehicle, at the price of creating kFS state the player switched off) |
| D28 | What a command's answer means | **Decided (built): `ok` means "the game ran the line"**; the game's own failures come back as its text in `output`. A `kfs.load` of a path with whitespace is refused before it reaches the game, because the game's `load` cannot take one | Parse the game's wording into success and failure (fragile); quote the path (the game's parser has no quoting) |
| D29 | `<purrtty.h>` at uninstall | **Decided (built): left registered while a compile may be reading the game's header table**, taken over by the next install (§4.5) | Always remove (risks corrupting a dictionary the game's compiler is reading) |

## Appendix A — evidence index

| Evidence | What it is | Where |
| --- | --- | --- |
| Bundle | 95 documents on kFS and its extension points, with file:line citations into the decompiled game | [`docs/kfs-info/index.md`](../docs/kfs-info/index.md) |
| Experiments E1–E27 | Results of running the real toolchain offline | [`docs/kfs-info/modding/offline-harness.md`](../docs/kfs-info/modding/offline-harness.md) |
| Spike 01–03 | Tables, service rows, kC/kASM binding, host hooks, the loader patch | [`plans/investigations/kfs-spikes/`](investigations/kfs-spikes/README.md) |
| Spike 04 | In-memory assembly resolver; the game's `Workbench` end to end on a `FakeVehicle` | `…/04-workbench-end-to-end/` |
| Spike 05 | Minimal language server on the real compiler; headless Neovim over TCP and stdio | `…/05-lsp-neovim/` |
| Spike 06 | The full 22-service catalogue, generated header, router, simulated parts, 76 self-checks | `…/06-service-catalog-and-sim/` |

## Appendix B — where to look in the bundle

| Question | Document |
| --- | --- |
| How does a service call travel, exactly? | [services syscall ABI](../docs/kfs-info/vm/services-syscall-abi.md), [service request handling](../docs/kfs-info/host/service-request-handling.md), [KfsServices](../docs/kfs-info/integration/kfs-services-game-side.md) |
| What must a table row satisfy? | [adding a service](../docs/kfs-info/modding/adding-a-service.md), [services table](../docs/kfs-info/tables/services-table.md), [tables loader](../docs/kfs-info/tables/game-tables-loader.md) |
| Which thread am I on, and what may I touch? | [dispatch and threading](../docs/kfs-info/integration/dispatch-and-threading.md), [patch targets and seams](../docs/kfs-info/integration/patch-targets-and-seams.md) |
| How do lights and animations work in the game? | [light module](../docs/kfs-info/game-targets/light-module.md), [keyframe animation module](../docs/kfs-info/game-targets/keyframe-animation-module.md), [control recipes](../docs/kfs-info/game-targets/control-recipes.md) |
| How do hosts live and die? | [lifecycle and persistence](../docs/kfs-info/integration/lifecycle-and-persistence.md), [controller set](../docs/kfs-info/integration/controller-set-and-layout-changes.md) |
| What can the compiler tell an editor? | [language-tooling surface](../docs/kfs-info/modding/language-tooling-surface.md), [diagnostics infrastructure](../docs/kfs-info/compiler/diagnostics-infrastructure.md), [compile report](../docs/kfs-info/compiler/compile-report.md) |
| How does the stock tooling build, load and start? | [workbench](../docs/kfs-info/tooling/workbench.md), [build and load flow](../docs/kfs-info/tooling/build-and-load-flow.md), [console commands](../docs/kfs-info/tooling/console-commands.md) |
| What does kC accept? | [language reference](../docs/kfs-info/language/kc-language-reference.md), [program structure](../docs/kfs-info/language/kc-program-structure.md), [examples](../docs/kfs-info/tooling/examples.md) |
| What breaks when the game updates? | [risks and compatibility](../docs/kfs-info/modding/risks-and-compatibility.md), [source map](../docs/kfs-info/overview/source-map.md) |
