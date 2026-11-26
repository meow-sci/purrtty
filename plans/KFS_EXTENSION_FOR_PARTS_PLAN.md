# kFS extension plan — parts control, external test runtime, language server, Neovim

| | |
| --- | --- |
| Status | Milestone M1 (tasks F1, A1, A2, A3, B1) implemented and tested **offline**, and corrected after its code review: on 2026-11-25 the solution suite passed 643 tests with 2 platform skips (260 of them in `purrTTY.Kfs.Tests`), Debug and Release, macOS arm64. Nothing has run in the game; the extension is not wired into `purrTTY.GameMod` (A4/A5 are next). Where the code differs from this text: §8.6. |
| Branch | `feature/explore-kfs` |
| KSA build researched | 2026.11.25.5949 |
| Reference | [`docs/kfs-info/`](../docs/kfs-info/index.md) (OKF bundle), spikes in [`plans/investigations/kfs-spikes/`](investigations/kfs-spikes/README.md) |
| Written | 2026-11-25 |
| Reviewed | Checked independently against the decompiled game, the purrTTY sources and the spike code; its 37 findings (12 errors, 6 unsupported claims, 11 inconsistencies, 8 gaps) are incorporated |

## 0. How to read this plan

- **§1–§3** say what will be built, what is already proven, and the architecture shared by every
  workstream.
- **§4–§7** are the four workstreams, each with design, file-by-file contents, and tests:
  - **A** — extend kFS with services for lights and part animations (the original goal);
  - **B** — the same extended kFS running **outside the game** as a unit-test runtime;
  - **C** — a **language server** for kC and kASM built on the game's real compiler;
  - **D** — **Neovim, with that language server attached, opened from in-game through purrTTY**.
- **§8** is the task breakdown: every coding task is sized and briefed for one delegated coding
  agent, in dependency order, with its acceptance test.
- **§9–§11** are risks, the in-game validation checklist, and the decisions still open.

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
| `purrTTY.Kfs` | library | `KSA.dll` and the Brutal assemblies it needs (compile-only), `Lib.Harmony`, `purrTTY.Logging` | yes | A |
| `purrTTY.Kfs.Testing` | library | `purrTTY.Kfs`, `KSA.dll` (compile-only) | no | B |
| `purrTTY.Kfs.Tests` | NUnit | `purrTTY.Kfs`, `purrTTY.Kfs.Testing` | no | A, B |
| `purrTTY.Kfs.Lsp` | library | `purrTTY.Kfs`, `KSA.dll` (compile-only) | yes | C |
| `purrTTY.Kfs.Lsp.Tests` | NUnit | `purrTTY.Kfs.Lsp`, `purrTTY.Kfs.Testing` | no | C |
| `purrTTY.Kfs.Cli` (`purr-kfs`) | console app | `purrTTY.Kfs.Testing`, `purrTTY.Kfs.Lsp` | optional download | B, C |
| `purrTTY.GameMod` (existing) | mod entry | + `purrTTY.Kfs`, `purrTTY.Kfs.Lsp` | yes | A, C, D |
| `nvim/` runtime files (Lua) | not .NET | — | yes | D |

`StandaloneLanguageHost` lives in the CLI project — the only one that references both the
language-server library and the test runtime. `ProjectResolver` and `NeovimLaunchPlan` live in
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
| colour | linear RGB, each component nominally 0–1 | clamped to [0, 1] |
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
| **Appearance is per light** | The first colour/cone/intensity/range write gives that light a private copy of its template; other lights of the same part type are unaffected. `x_light_reset` restores the stock template. Overrides last until reset, reload or the part's destruction — they are not saved. (Switch states and animation goals, by contrast, are part of the game's own save.) |
| **Read-back timing** | Reads are live. Writes are applied at the next frame boundary: a value written in tick *k* is read back from the first tick of the next dispatch — tick *k+1* at ordinary frame rates, later when several ticks run per frame (measured in spike 06) — never in the same tick. `LIT` additionally needs one physics step and electrical power. |
| **Animations move at their own speed** | A target is a goal; position slews toward it at the animation's authored rate. Poll `POSITION` or `STATE`. A program that changes the target every tick keeps the part moving and the vehicle off rails. |
| **Budget** | Measured on the Bench processor: 22–25 cycles for a call with up to two scalar arguments, 55 for `x_light_set_color`, 40 for the two Medium calls. The binding limit is the VM's **256 service calls per tick**, `kfs_logf` included; calls beyond it are dropped with an event. |
| **Bench** | There is no vehicle: results are 0 and every call — getters too, since the VM queues a request for them as well — is rejected "no vehicle to act on" (optional patch 4 of §4.6 softens this). |
| **Paused** | Nothing ticks, so nothing is issued; commands already issued are still applied. |
| **Without the mod** | An image that uses these services does not load ("unknown service id"). |

### 4.3 Files

```text
purrTTY.Kfs/
  purrTTY.Kfs.csproj
  KfsExtension.cs              public entry: Install(Harmony), Uninstall(), Status
  KfsExtensionStatus.cs        what applied, what did not, why
  Catalog/
    ServiceDef.cs              record: Id, Name, Kind, Params[], Result, CostClass, Summary, HostChecks, Doc
    ParamDef.cs                record: Name, CType, CellTypes[], Domain
    EnumDef.cs                 record: Key, Prefix, Values[] (with reserved fillers)
    PurrCatalog.cs             THE list of services and enums (the only place ids appear)
    CatalogEmitter.cs          → services JSON rows, enums JSON entries, purrtty.h, purrtty.kinc, builtin names, markdown
    CatalogSelfCheck.cs        cell types and counts, grouping, sub-ranges, id block, duplicate names; parses the emitted header back
  Tables/
    TableAugmenter.cs          GameTables Build(Func<string, Stream?> stockOpen) — pure, no statics
    TablesPublisher.cs         assigns KfsIntegration._tables, or installs the LoadEmbedded fallback
  Patches/
    ApplyRequestPatch.cs       postfix  VehicleComputer.ApplyRequest
    IssueResultPatch.cs        postfix  VehicleComputer : ISyscallHost.IssueResult
    BeforeInputEventsPatch.cs  postfix  KfsIntegration.BeforeInputEvents
    BenchApplyRequestPatch.cs  postfix  HostComputer.ApplyRequest          (optional)
    LoadEmbeddedFallbackPatch.cs  postfix  GameTables.LoadEmbedded         (only if the field is not found)
  Runtime/
    IVehicleParts.cs           the abstraction of §3.2
    PartSets.cs                immutable frozen lists (slots) + descriptors + generation
    PartCommand.cs             readonly struct: Service, Slot (object reference), U, A, B, C
    VehicleBinding.cs          per computer: parts, command queue, event throttle
    PartsRegistry.cs           VehicleComputer → VehicleBinding; creation under one lock
    BindingPump.cs             drain-and-apply for one binding — shared by the game pump and the test runtime
    ServiceRouter.cs           Answer(...) and Accept(...)
    PartMath.cs                pure clamps and conversions (unit-tested)
    ProjectResolver.cs         which files make up a project, with unsaved-buffer overlays (used by B2 and C2)
    NeovimLaunchPlan.cs        executable, argv, env, cwd as plain strings (used by D3; no terminal types)
  Game/
    KsaVehicleParts.cs         IVehicleParts over a live Vehicle
    LightOps.cs                effective switch, private template, colour, cone, intensity, range, reset
    AnimationOps.cs            goal writes, state reads
    LightOverrides.cs          LightModule → (original template, private template)
    HostDirectory.cs           reads KfsVehicleHost.AllHosts under its lock
    MainThreadPump.cs          once per frame on the main thread: discover hosts, call BindingPump, prune
  Console/
    PurrConsole.cs             the `purr` console command and its verbs
    PartsListing.cs            the index-table formatter
  Examples/                    embedded .c / .kasm resources, one folder per example
purrTTY.GameMod/
  Patcher.cs                   + KfsExtension.Install / Uninstall
  Kfs/KfsConfig.cs             <config>/.purrTTY/kfs.toml (own file; see §7.5)
  TerminalMenus.cs             + "kFS" submenu (status; Workstream D entries)
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

For kASM, whose `<kfs.kinc>` gains the `SVC_X_*` symbols automatically but not the selector
constants, the emitter also produces `purrtty.kinc` (`.equ X_LIGHT_ON, 2` lines).
`purr examples` writes it next to the kASM example, and a pure-kASM project picks it up with
`.include "purrtty.kinc"` (verified through the game's `Workbench.Build` in spike 06). The
alternative — a postfix on the `BuiltinIncludes(GameTables, IoLayout, string)` constructor that
`TryAdd`s the constants so `<kfs.kinc>` carries them — also works but costs another patch target on
a constructor that runs for every assembly; it is decision D14.

### 4.5 Install sequence

`KfsExtension.Install(Harmony harmony)` — called from `Patcher.patch()` after the terminal's own
patches, inside its own `try`/`catch`:

1. Return immediately if the extension is switched off (`kfs.toml`, default on) or already
   installed.
2. Run `CatalogSelfCheck`. Failure → status `Disabled: catalogue`, stop.
3. Resolve every **required** patch target (§4.6) with `AccessTools`. Any `null` → status
   `Disabled: target <name> not found`, stop.
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
9. Register the `purr` console command (§4.13).
10. Log one line: `purrTTY kFS extension: active — 22 services 0x5000–0x5033, 3 hooks (+1 optional)`,
    or the disabled reason.

`Uninstall()` (from `Patcher.unload()`): unpatch, remove the header and builtin names, restore
every private light template still alive, clear the registry. The augmented tables stay published
for the life of the process — with the hooks gone their rows simply reject, and a later `Install`
in the same process recognises them (step 5, second row).

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

Bodies, in full:

```csharp
// 1
static void Postfix(VehicleComputer __instance, ServiceRequest request, ref bool __result)
{
    if (__result || !PurrCatalog.Owns(request.Id)) return;
    try { ServiceRouter.Accept(__instance, request); }
    catch (Exception e) { KfsLog.Once("accept", e); }
    __result = true;                                   // ours either way: never "no vehicle to act on"
}

// 2
static void Postfix(VehicleComputer __instance, ushort service, ReadOnlySpan<uint> args, Span<uint> results)
{
    if (results.Length == 0 || !PurrCatalog.Owns(service)) return;
    try { results[0] = ServiceRouter.Answer(__instance, service, args); }
    catch (Exception e) { KfsLog.Once("answer", e); results[0] = 0u; }
}

// 3
static void Postfix()
{
    try { MainThreadPump.Run(); }                      // the stock body is a no-op while the flag is off; ours still prunes
    catch (Exception e) { KfsLog.Once("pump", e); }
}
```

`PurrCatalog.Owns(id)` is `(uint)(id - 0x5000) <= 0xFF`. `KfsLog.Once(key, e)` logs the first
occurrence per key and counts the rest.

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
  phase) and `Find` (dispatch) — and must be created, and its `ImageLoaded` chain installed,
  exactly once.
- In the game, `MainThreadPump` binds every host in `HostDirectory.Snapshot()` it has not seen to
  a `KsaVehicleParts(host.Vehicle)`. (`HostDirectory` copies `KfsVehicleHost.AllHosts` under that
  list's own lock; the list is `internal static`, reached once by reflection.)
- A host can be created and have a program loaded and ticked within one frame, before the pump has
  seen it. `Find` therefore falls back, on the dispatch thread, to `HostDirectory` to locate the
  host whose `Computer` is this computer and binds it there. Stock kFS itself builds its controller
  sets on the dispatch thread when the layout changes.
- When a binding is created it chains `computer.ImageLoaded` (a settable `Action?` the host already
  uses): the previous delegate runs first, then `binding.Parts.Rebuild()`. `ImageLoaded` fires on
  the dispatch thread at the end of a successful load, before the first tick, so every program
  starts from fresh lists.
- Removal: the game's own `ReleaseDead()` has already dropped dead hosts from `AllHosts` by the
  time the postfix runs, so the pump **prunes bindings whose host is absent from the snapshot**
  (their queued commands are discarded, as stock discards main-thread work of a dead host). The
  weak table needs no explicit removal.

`VehicleBinding`:

```csharp
sealed class VehicleBinding
{
    public IVehicleParts Parts { get; }
    readonly object _gate = new();
    List<PartCommand> _pending = new(), _spare = new();
    public void Enqueue(in PartCommand c);        // dispatch: lock, add; drop-oldest above 4096 with one event
    public List<PartCommand> Take();              // main: lock, swap lists
    public void Recycle(List<PartCommand> l);     // main: clear and keep as spare
    public bool ShouldReport(int key, long tick); // rate limit for mod-originated events
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
    return sw != null && ReferenceEquals(sw.Parent.Tree, vehicle.Parts) ? sw : null;   // confirm the member path in A4
}
```

`HAS_SWITCH` = effective switch is not null; `ON` = its `LightIsActive` (1 when there is none);
`LIT` = `m.IsActive`; `x_light_set_on` is rejected when there is none. A switch on another
vehicle is never written.

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
hosts = HostDirectory.Snapshot()                 // copy of AllHosts under its lock; already pruned by the game
for each host in hosts, ordered by Vehicle.Id:   // same order stock uses
    binding = PartsRegistry.Find(host.Computer) ?? bind new KsaVehicleParts(host.Vehicle)
    mark binding seen
    BindingPump.DrainAndApply(binding)
drop every live binding not seen this frame      // its host is gone; queued commands are discarded
```

It runs every frame, including while paused and while the kFS flag is off (so pruning still
happens); with no hosts it is one lock and an empty loop. With a host and no purr calls: one lock,
one small array copy and one lookup.

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
   vehicle,

   ```text
   vehicle "Explorer 1" — parts as a program loaded now would see them (generation 2)
   lights
     0  Spotlight Mk1            spot   switch group 0   on   lit
     1  Spotlight Mk1            spot   switch group 0   on   lit
     2  Cabin (interior)         point  no switch
   deployables
     0  Solar Array XL           retracted   8.0 s   drives a solar panel
   actuators
     0  Cargo Bay 2m             0.00 → 0.00   6.0 s
   ```

   When the vehicle has a host, the listing is the host's frozen sets (what the running program
   sees); otherwise it is a preview. Other verbs: `purr parts lights|deploy|actuators`,
   `purr status` (the install status line and counters), `purr examples` (§4.14),
   `purr header` (prints `purrtty.h`), and in Workstreams C/D `purr lsp` and `purr nvim`.
   Output goes through `KfsIntegration.Print`, so it lands wherever `kfs` output lands, including
   purrTTY's game-console shell.
2. **From a program:** `x_parts_describe(X_KIND_ALL)` writes the same table to the program
   log.
3. **Reference window:** the Services page lists the mod rows with their summaries automatically,
   because the tables were augmented before the window is first built.

### 4.14 Examples

`purr examples` writes the examples into the workspace through the public `Workspace.Write` (never
automatically, like the stock examples). **Each example is its own folder**, one level deep: a
workspace folder is one project, so two programs in one folder would be compiled together and
collide.

| Folder / file | Shows |
| --- | --- |
| `x_blink/blink.c` | counts, has-switch check, on/off |
| `x_light_show/light_show.c` | colour wheel, cone breathing, intensity ramp, reset on a flag |
| `x_deploy/deploy_when_safe.c` | subscribe `MSG_FLIGHT_DATA`; deploy solar arrays above the atmosphere, retract below |
| `x_actuator/actuator_sweep.c` | triangle wave on an actuator, read-back of position |
| `x_parts/parts_list.c` | `x_parts_describe`, generation and dirty handling, rescan |
| `x_blink_kasm/blink.kasm` + `x_blink_kasm/purrtty.kinc` | the kASM spelling, with the constants include |

Each is also a test input (§4.16).

### 4.15 Status and logging

`KfsExtensionStatus` records: active or the disabled reason; hooks applied; services and id range;
counters (requests accepted, rejected, answered; commands applied; apply failures; pump
exceptions; queue high-water mark). `purr status` and the purrTTY menu show it. Logging goes
through `purrTTY.Logging` at most once per distinct failure.

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

What these cannot cover — `KsaVehicleParts`, `LightOps`, `AnimationOps`, `HostDirectory`, the pump
against real hosts — is deliberately thin, mirrors code two existing mods run in game, and is
covered by the in-game checklist (§10).

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
    public void Run(double seconds, double frameDt = 1.0 / 60.0);   // per frame: pump, parts.Step, dispatch
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

One simulated frame, mirroring the game's order (§2.1): **pump** (`BindingPump.DrainAndApply`, the
same code the game's pump calls, applying queued commands to `SimVehicleParts`) → **step the parts** by `frameDt` (animations slew
toward their goals; lit follows on and powered) → **dispatch** (`Fake.Dispatch(timeNs, sample)`),
which runs the ticks that are due. Requests the stock switch accepts are taken with
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

| Command | Does |
| --- | --- |
| `purr-kfs check <file> [--vehicle <spec>] [--json]` | Build a kC or kASM project exactly as the workbench would (folder = project; root file = own project); print diagnostics; exit code 1 on errors. |
| `purr-kfs run <file> --vehicle <spec> [--for 10s] [--frame 60] [--log] [--state]` | Build, load onto a simulated vehicle, run, print the program log and the final state of lights and animations. |
| `purr-kfs test <scenario.toml>…` | Run scenario files: a vehicle spec, a program, a duration, optional scripted telemetry, and expectations on parts, logged lines and stock requests. Output in TAP; non-zero exit on failure. |
| `purr-kfs header` | Print `purrtty.h`. |
| `purr-kfs services [--markdown]` | Print the catalogue as a reference table. |
| `purr-kfs lsp --stdio \| --listen <addr>` | The language server, standalone (Workstream C). |

All commands take `--ksa <dir>` or `KSA_DLL_DIR`. A vehicle spec is a small TOML file (the
repository already uses Tomlyn) or shorthand flags (`--lights 4 --deployables 1 --actuators 1`).

Scenario files are the player-facing form of unit tests:

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
  SimTelemetry.cs               helpers to build HostSample / flight data for scripted flights
  Scenario/ScenarioFile.cs, ScenarioRunner.cs
purrTTY.Kfs.Cli/
  purrTTY.Kfs.Cli.csproj        <ToolCommandName>purr-kfs</ToolCommandName>
  Program.cs                    resolver first, then command dispatch
  Commands/Check.cs, Run.cs, Test.cs, Header.cs, Services.cs, Lsp.cs
  StandaloneLanguageHost.cs, LayoutFromVehicleHeader.cs   (the language server's host outside the game)
purrTTY.Kfs.Tests/
  purrTTY.Kfs.Tests.csproj      stamps KSAFolder as assembly metadata
  ModuleInit.cs                 [ModuleInitializer]: the resolver
  Setup.cs                      [SetUpFixture]: KfsRuntime
  …                             the tests of §4.16 and §5.8
```

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
| `Scenario_Files_Run` | the sample scenarios pass and a deliberately failing one fails with a useful message |
| `Cli_Check_Exit_Codes` | 0 on a clean build, 1 with diagnostics in `file:line:col: severity code: message` form |

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
      ILiveProgram? Live { get; }                      // snapshot, log tail, traces; null when there is no vehicle
  }
  ```

- Two hosts implement it:

  | Host | Where | Tables | Target layout | Vehicle commands | Live data |
  | --- | --- | --- | --- | --- | --- |
  | `InGameLanguageHost` | `purrTTY.GameMod`, inside the game | `KfsIntegration.Tables` | the workbench target: the controlled vehicle's layout, or the bench | yes | yes |
  | `StandaloneLanguageHost` | `purr-kfs lsp` | built by `KfsRuntime` from the player's `KSA.dll` | reconstructed from the workspace's `vehicle.h`, else a layout file, else the bench | no (or onto a simulated vehicle) | simulated |

- Transports: `KfsLspListener`, a small loopback TCP accept loop in the library, is used by both
  hosts; the CLI adds stdio. In the game, `KfsLspService` (in `purrTTY.GameMod`) owns the
  listener's lifetime, the configuration and the server info file.

### 6.2 Protocol behaviour

| Area | Decision | Basis |
| --- | --- | --- |
| Position encoding | Offer and prefer `utf-16`; convert when the client forces `utf-8` or `utf-32`. | kFS columns are 1-based UTF-16 code units (tab = 1); spike 05 passed all three encodings. |
| Text sync | Full-document sync at first; incremental later if profiling asks for it. | Sources are small (the stock editor caps a buffer at 262,144 bytes). |
| Clients | Several at once; `exit` closes one connection and never the process. | The server lives in the game. |
| Authentication | `initialize` must carry `initializationOptions.token`; otherwise the server answers with an error and closes. | R12. |
| Lifetime | Starts when the extension installs, stops at unload; writes and removes the server info file (§7.6). | — |
| Logging | Method names and timings at debug level through `purrTTY.Logging`; never to stdout in stdio mode. | Spike 05. |

### 6.3 Builds

One **build scheduler** owns all compiling:

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
3. kC is compiled with the public `Compiler.CompileOnThisThread(new CompileInput { Files, Part,
   Game, Layout, ProjectName })` **on the scheduler's own thread** — one long-lived, below-normal
   priority thread with a 16 MiB stack (the front end's nesting limit assumes that stack).
   `Compiler.Compile` is deliberately not used: it starts a new 16 MiB normal-priority thread per
   call and, on timeout, abandons it still running.
   Pure kASM is assembled as the workbench does it:
   `new Assembler(tables, part, layout) { ProjectFiles = name => overlay-or-disk text }.Assemble(the .kasm sources)`;
   without `ProjectFiles` a quoted `.include "purrtty.kinc"` cannot be resolved.
4. One compile runs at a time; a newer request for the same project replaces the queued one; a
   finished result is published only if it is for the latest version of every buffer it used.
5. Every compile is wrapped in `try`/`catch`: the game's compiler can throw on unusual input
   (spikes 05 and 06 reproduced several cases); an exception becomes a single diagnostic with code
   `KFS-LSP` on the first line and the server carries on.
6. **Watchdog.** A compile cannot be cancelled. If one runs longer than 10 s the scheduler
   publishes a `KFS-LSP` "compile did not finish" diagnostic, retires that thread (it is left to
   finish or not) and starts a fresh one — at most twice per session; after that the scheduler
   refuses further compiles until a retired thread ends, and says so. This bounds what stock leaves
   unbounded.

Measured cost (spike 05): 0.2–2 ms for small files, about 19 ms for the largest stock example,
80–95 ms for the first compile in a process.

Thread-safety (read from the code, and stress-tested in spike 05 — 400 compiles of 12 files on 8
threads, five times, matching serial results): concurrent compiles against **one** tables instance
are safe — the stock editor already compiles on a pool thread while `kfs build` compiles on the
main thread — and all mutation of `SystemHeaders` and the tables happens once at install, before
any compile. The scheduler still compiles serially to bound its cost inside the game.

### 6.4 Diagnostics

`Diagnostic(Severity, Code, File, Line, Col, Message, Hint)` → LSP:

| kFS | LSP |
| --- | --- |
| `Severity.Error` / `Warning` | 1 / 2 |
| `Code` (`KC-E003`, `KA-W711`) | `code`; `codeDescription` is not available — the manual page (rule sentence, range, languages) is served through hover on the code and a `kfs.manual` command |
| `Message` + `Hint` | `message` (hint on a second line); a hint ending "at file:line" becomes `relatedInformation` |
| `File` | a **name**, not a path. Mapped to a URI through the project's file map. `<kfs.h>`, `<vehicle.h>`, `<purrtty.h>`, `<math.c>`, `X.kasm.h`, `<predefined>` are virtual: the diagnostic is anchored on the `#include` line that pulled the header in, with the original location in the message and, once virtual documents exist (§6.6), as related information |
| `Line`, `Col` (1-based) | `line-1`, `character` = `Col-1` converted to the negotiated encoding |
| no end position | kC: lex the line with the game's public `Lexer` (it throws `LexException` on malformed text — catch it) and take the width of the token at that column; fall back to the identifier or one character. kASM: the column is always 1 — use the trimmed line |
| `(project, 0, 0)` "no position" (no task declared, limits) | first line of the project's main file |
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

### 6.6 Navigation, symbols and cost lenses

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

`workspace/executeCommand`:

| Command | Runs | Notes |
| --- | --- | --- |
| `kfs.build` | `kfs build <file>` | also writes `vehicle.h` / `vehicle.kinc`, as the stock command does |
| `kfs.load` | `Workbench.LoadIntoTarget(file)`, after the client saved its buffers | the game builds from disk (or from the stock editor's buffer when the file is open there), never from Neovim's unsaved text; the load is queued and takes effect at the next dispatch — not while paused |
| `kfs.start`, `kfs.stop`, `kfs.reset` | the same verbs | |
| `kfs.target` | `kfs target vehicle \| bench` | triggers `TargetChanged` |
| `kfs.parts` | `purr parts` | the index tables of §4.13 |
| `kfs.manual` | `kfs manual <code>` | |
| `kfs.verb` | any `kfs …` line | behind `lsp_allow_vehicle_commands` |

`RunVerb` marshals to the **main thread in the UI phase** (a queue drained from the mod's
`[StarMapAfterGui]` hook — where the stock console itself runs commands, and the only place the
workbench may be touched), calls the public `KfsConsoleCommands.Execute(line)`, and returns what
was printed during the call. Output is captured by a **standing tee**: when the service starts it
replaces the public `KfsIntegration.Output` with a writer that forwards to the original and to
subscribed clients, then reads `KfsIntegration.Workbench` once — the workbench copies `Output`
only when that getter runs, and prints deferred replies from its per-frame hook, so a temporary
swap would miss them. One side effect to check in game: with a non-default writer installed,
`KfsIntegration.Error` writes its line to `Output` as well as to the log.

Notifications to clients that asked for them:

| Notification | Content | Source |
| --- | --- | --- |
| `purrtty/kfsLog` | new program-log lines (`kfs_logf` output and host events) | `computer.Console`: poll `Version`, then `CopyTail` — thread-safe |
| `purrtty/kfsOutput` | `kfs` command output | the `KfsIntegration.Output` tee |
| `purrtty/kfsState` | computer state, tick, overruns, trap with file and line | `computer.Snapshot` (immutable, published by the dispatch thread) — polled at 5 Hz at most and only while a client subscribes, because reading the property asks the computer to publish a snapshot on every tick |

A trap with a source location is also published as an error diagnostic on that line until the
program is restarted.

`InGameLanguageHost.CurrentTarget` is refreshed once per frame on the main thread (part, layout
object and name of the workbench's target, exactly what the stock editor captures before a
background build) and read by the scheduler as an immutable snapshot.

### 6.8 Files

```text
purrTTY.Kfs.Lsp/
  purrTTY.Kfs.Lsp.csproj
  Protocol/JsonRpcConnection.cs      Content-Length framing, request/response correlation, cancellation
  Protocol/LspTypes.cs               the subset of LSP structures used
  KfsLanguageServer.cs               one per connection: lifecycle, capabilities, dispatch table
  KfsLspListener.cs                  loopback TCP accept loop; token check
  IKfsLanguageHost.cs, BuildTarget.cs, ILiveProgram.cs
  Documents/DocumentStore.cs         URI ↔ workspace path, text, version, line index
  Documents/PositionMapper.cs        kFS (line, col) ↔ LSP position in three encodings
  Build/BuildScheduler.cs            debounce, own compile thread, latest-wins, exception guard, watchdog
                                     (file sets come from purrTTY.Kfs's ProjectResolver)
  Build/DiagnosticMapper.cs          §6.4
  Features/HoverProvider.cs, CompletionProvider.cs, SymbolProvider.cs,
           CostLensProvider.cs, InlayHintProvider.cs, CommandProvider.cs
  Index/TableIndex.cs                services, opcodes, sysblock, messages, pragmas, directives
  Index/HeaderIndex.cs               #define and prototype scanner over header texts
purrTTY.GameMod/Kfs/
  InGameLanguageHost.cs              target snapshot, main-thread verb queue, output tee, log poller
  KfsLspService.cs                   start/stop with the extension; config; status; server info file
```

### 6.9 Tests (`purrTTY.Kfs.Lsp.Tests`, offline)

Driven through in-memory streams against a real `KfsRuntime` and a `StandaloneLanguageHost` on a
temporary workspace:

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
| `Cost_Lenses_Match_Compile_Report` | per-task WCET and budget |
| `Target_Change_Rediagnoses` | `THRUSTER_COUNT`-dependent code flips between clean and erroring |
| `Commands_Reach_The_Host` | a fake host records `RunVerb("load x --vehicle")` |
| `Exit_Closes_Connection_Only` | a second client keeps working |
| `Neovim_Attaches_Headless` (silently skipped without `nvim`) | the spike-05 scenario, with the shipped runtime files |

## 7. Workstream D — Neovim through purrTTY

### 7.1 What the player gets

1. In the game: purrTTY menu › **kFS** › **Edit in Neovim…** (a list of the workspace's source
   files, plus "New file…"), or the console command `purr nvim <file>`.
2. A purrTTY terminal opens — a 2D window by default, an in-world terminal on request — whose
   process is the player's own host `nvim`, started in the kFS workspace with the file open.
3. The buffer is attached to the language server running inside the game: diagnostics as you type
   against the workbench target's layout (the launcher targets the controlled vehicle), completion and hover for stock and `x_*`
   services, per-task cost lenses.
4. `:KfsBuild`, `:KfsLoad`, `:KfsStart`, `:KfsStop`, `:KfsReset` act on the vehicle; `:KfsLog`
   shows the program's log as it runs; `:KfsParts` shows the index tables.
5. Quitting Neovim closes the terminal.

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
NeovimLaunchPlan plan = NeovimLaunchPlan.For(file, workspaceRoot, nvimRuntimeDir, server, config);
var o = ProcessLaunchOptions.CreateCustom(plan.Executable, plan.Arguments);
//   arguments:  --cmd  "lua dofile(vim.env.PURRTTY_KFS_NVIM .. '/purrtty_kfs.lua')"  <file>
o.WorkingDirectory = plan.WorkingDirectory;                  // the workspace root
foreach (var (k, v) in plan.Environment) o.EnvironmentVariables[k] = v;
//   PURRTTY_KFS_NVIM  = <mod folder>/nvim          (a path, so spaces and commas are harmless)
//   PURRTTY_KFS_ROOT  = <workspace root>
//   PURRTTY_KFS_LSP   = 127.0.0.1:<port>
//   PURRTTY_KFS_TOKEN = <session token>
o.CloseOnExit = true;                                        // T1
KfsIntegration.Workbench.TargetActiveVehicle();              // diagnostics and :KfsLoad then agree on the layout
TerminalWindow w = controller.OpenWindow(o, Path.GetFileName(file));
w.TryRename("kFS: " + Path.GetFileName(file));
controller.IsVisible = true;                                 // after OpenWindow: set first, it would spawn a default shell
```

Why the runtime directory travels in an environment variable and is loaded with `dofile`: the
mod's default folder is under `…/My Games/Kitten Space Agency/mods/`, which contains spaces, and
`:set runtimepath^=<path>` splits on unescaped spaces and commas. Loading one entry script by
absolute path also survives plugin managers that reset `runtimepath` during start-up.

Resolving `nvim`, in order:

1. the configured path (`nvim_path` in `kfs.toml`);
2. the game process's `PATH` (what `CreateCustom` does with a bare name; on Windows it tries
   `nvim.exe`);
3. on Unix, the user's login shell: `CreateCustom($SHELL or "/bin/sh", "-lc", "exec \"$@\"", "sh",
   "nvim", …)` — a game started from Finder or Steam usually lacks the Homebrew and user `bin`
   directories. **Unverified:** which start-up files a given shell reads with `-lc`; task D3
   confirms this on macOS (zsh) and Linux (bash) or adds `-i`;
4. otherwise a message in the menu and the log: "Neovim was not found; set `nvim_path`".

If the language server is not running (extension disabled, or the port could not be opened) Neovim
is still launched, without the `PURRTTY_KFS_LSP` variables; the runtime files then try the
fallbacks of §7.6.

`TargetActiveVehicle()` matters: the workbench's target stays the bench until something selects
the vehicle, so without it diagnostics would be computed against the bench layout while
`:KfsLoad` built for the vehicle's.

### 7.3 Terminal changes this needs

| # | Change | Why | Where |
| --- | --- | --- | --- |
| T1 | **Close on exit**: a launch option `CloseOnExit`; when set, the window (or tab, or in-world instance) is closed once its process has exited. | Today nothing reacts to an exit except " (exit N)" in the tab label, and with one tab the tab bar is hidden. | `ProcessLaunchOptions`, `TerminalWindow`, `GhosttyTerminalController` |
| T2 | **Guard resize after exit**: skip `ProcessManager.Resize` once the process has exited. | Both back ends throw `InvalidOperationException` then; resizing a dead window throws between `ImGui.Begin` and `End`. A latent bug independent of kFS. | `TerminalWindow.cs` (resize call), `InWorldTerminalRenderer.cs` |
| T3 | **Windows `PATH` lookup honours `PATHEXT`**, and a `.cmd`/`.bat` hit is launched through `cmd.exe /c`. | `nvim` installed through a `.cmd` shim (scoop, some package managers) is missed; and the terminal starts processes with a raw `CreateProcessW`, which cannot run a script directly. | `ShellCommandResolver.FindExecutableInPath`, the Windows process launcher |
| T4 | **Layouts keep environment variables** (or the kFS launcher is excluded from layout capture). | A captured layout drops `EnvironmentVariables`, so a saved "kFS editor" terminal would relaunch without its server address. Simplest: do not capture kFS-launched terminals; re-create them through the launcher. | `ShellSpec` / `LayoutManager` |

Useful but not blocking (each is its own small task in §8):

| # | Change | Why |
| --- | --- | --- |
| T5 | Focus-in/out reporting when the program enables mode 1004. | Neovim's `FocusGained` drives `autoread` and many plugins. |
| T6 | Encode Ctrl/Alt with the remaining punctuation (`/ - ; ' , . =`), and Alt-composed characters on non-US layouts. | Common Neovim mappings (`<C-/>`, `<M-.>`) and typing `{ [ |` with Option on some Mac layouts. |
| T7 | Let the toggle hotkey (F12 by default) pass to a 2D terminal when a modifier is held, or make it configurable per window. | F12 never reaches a 2D terminal's program today. |
| T8 | OSC 52 read support (clipboard query) in 2D windows; OSC 52 at all in-world. | `"+p` over OSC 52. |

What already works for a full-screen editor: truecolor (`COLORTERM=truecolor`, `TERM=xterm-256color`),
alternate screen, scroll regions, cursor shapes, synchronized output, the kitty keyboard protocol,
mouse tracking with motion, bracketed paste, resize propagation, and game-hotkey suppression while
a terminal has focus.

### 7.4 The Neovim runtime files

Shipped in the mod folder as `nvim/`. Plain Lua, no plugin manager, no dependency beyond Neovim's
built-in LSP client.

```text
nvim/
  purrtty_kfs.lua             the single entry script: adds ./lua to package.path, then require('purrtty_kfs').setup()
  lua/purrtty_kfs/init.lua    setup(): filetypes, attach autocommand, connection discovery
  lua/purrtty_kfs/commands.lua
  lua/purrtty_kfs/log.lua     the :KfsLog buffer
  lua/purrtty_kfs/buffer.lua  per-buffer options for kc and kasm (set from the FileType callback)
  syntax/kasm.vim             mnemonics and directives (generated from the opcode table at build time; sourced by path)
  plugin/purrtty_kfs.lua      two lines, for people who install the folder as an ordinary plugin
  doc/purrtty-kfs.txt
```

Design points:

- **Independent of `runtimepath`.** The launcher loads `purrtty_kfs.lua` by absolute path
  (`--cmd "lua dofile(…)"`), and that script extends `package.path` itself; buffer options are set
  from Lua and the kASM syntax file is sourced by path, so nothing depends on `ftplugin/` or
  `syntax/` discovery. The same folder also works as a normal plugin for manual installation.
- **Own filetypes.** kC files are `*.c`/`*.h`; a normal C language server must not attach to them.
  `vim.filetype.add` maps `*.c`/`*.h` **under the workspace root** to filetype `kc` and
  `*.kasm`/`*.kinc` to `kasm`; the buffer set-up uses `syntax=c` for `kc` and registers the C
  tree-sitter parser for it when present. Files elsewhere are untouched.
- **Attach.** A `FileType kc,kasm` autocommand calls `vim.lsp.start` with a `cmd` chosen by
  discovery (§7.6) and `root_dir` = the workspace root, so every buffer in the workspace shares
  one client. Because `--cmd` runs before the file is read, the rules are in place for the first
  buffer.
- **Authentication.** The token goes in `init_options`; the server refuses `initialize` without it.
- **Commands.** `:Kfs*` commands write modified workspace buffers first (`:wall` restricted to the
  workspace) — the game's `kfs build` and `kfs load` read the disk, never Neovim's unsaved text —
  then send `workspace/executeCommand`; results come back as the command's response and as
  `window/showMessage`.
- **Log.** The server sends the program log as a custom notification (`purrtty/kfsLog`); the
  handler appends to a scratch buffer opened by `:KfsLog`.
- The player's own configuration loads normally; nothing is written to it.

The attach logic. The `vim.lsp.start` / `vim.lsp.rpc.connect` / `vim.filetype.add` calls are the
ones spike 05 ran against Neovim 0.12.6 on macOS (TCP and stdio; the path rule won over the `.c`
extension, so C language servers never matched). **Not yet run:** this file layout and the `dofile`
entry, Windows path separators in the patterns, and start-up under a plugin manager — task D2's
headless test covers them.

```lua
local root = vim.env.PURRTTY_KFS_ROOT            -- or the discovered workspace root (§7.6)
local pat  = vim.pesc((root:gsub('\\', '/')))    -- patterns are matched against forward-slash paths
vim.filetype.add({ pattern = {
  [pat .. '/.*%.[ch]']   = 'kc',
  [pat .. '/.*%.kasm']   = 'kasm',
  [pat .. '/.*%.kinc']   = 'kasm',
}})

vim.api.nvim_create_autocmd('FileType', { pattern = { 'kc', 'kasm' }, callback = function(ev)
  require('purrtty_kfs.buffer').setup(ev.buf)
  local cmd = require('purrtty_kfs').resolve_cmd()   -- rpc.connect(host, port) | { 'purr-kfs', 'lsp', '--stdio' } | nil
  if not cmd then return end
  vim.lsp.start({
    name = 'kfs',
    cmd = cmd,
    root_dir = root,                                  -- one client per (name, root_dir)
    init_options = { token = vim.env.PURRTTY_KFS_TOKEN },
  }, { bufnr = ev.buf })
end })
```

Connection behaviour the runtime files must handle (all observed in the spike):

| Situation | What Neovim does | What `purrtty_kfs` does |
| --- | --- | --- |
| Nothing is listening on the port | one warning; the client object exists but never initialises and never retries | probe the port with a short `vim.uv` TCP connect **before** `vim.lsp.start`; if it fails, fall through to the next discovery step |
| The server goes away (game closed, extension reloaded) | `on_exit`, `LspDetach` for every buffer, diagnostics cleared, no reconnect | an `on_exit` handler starts a low-rate timer that re-runs discovery and re-attaches open `kc`/`kasm` buffers when a server appears |
| Several buffers in the workspace | one client, one connection | nothing |
| Several Neovim processes | one connection each | the server accepts several clients |

### 7.5 Menu, console and config

- `TerminalMenus.DrawMenuContent`: a `kFS` submenu after "Layouts…", shown when the launcher hook
  is set: **Edit in Neovim…** (workspace file list from `Workbench.Workspace.List()`),
  **Open workspace in Neovim**, **Status** (extension and server lines), **Copy server address**.
- Console: `purr nvim [file]` and `purr lsp` (address, connected clients) — verbs of the mod's own
  `purr` command (§4.13), so no kFS code is patched for them.
- Optional, later: a button in the stock kFS editor's menu bar through a postfix on the public
  `KfsEditorWindow.DrawMenuBar`.
- **Config lives in its own file**, `<config>/.purrTTY/kfs.toml`, owned by `purrTTY.GameMod/Kfs/KfsConfig.cs`
  (Tomlyn, written through `AtomicFile`). It cannot be a `[kfs]` table in `purrtty.toml`: that file
  is a typed model re-serialised on save, which would drop a table it does not know, and
  `purrTTY.Kfs` must not reference the display project that owns it. Keys: `enabled`,
  `lsp_enabled`, `lsp_port` (0 = pick a free one), `lsp_allow_vehicle_commands`, `nvim_path`,
  `nvim_open_in` (`window` | `inworld`).
- Wiring follows the layouts precedent: `TerminalMod` constructs the launcher with
  `(_controller, _inWorld)`, exposes a nullable static hook to `TerminalMenus`, and pumps it from
  `OnAfterUi` (which is also where an in-world terminal must be created).
- Deployment: `nvim/**` is copied next to the mod DLLs by a new item and `Copy` pair in
  `purrTTY.GameMod.csproj`'s `CopyCustomContent`, like fonts and themes; the launcher finds it
  beside its own assembly.

### 7.6 Using Neovim outside the game

The runtime files choose a connection in this order:

1. `PURRTTY_KFS_LSP` / `PURRTTY_KFS_TOKEN` in the environment (the in-game launch);
2. a **server info file**, `<config>/.purrTTY/kfs-lsp.json` — `{ "address", "token", "pid",
   "ksa_build", "workspace_root" }` — written by the mod when its server starts, rewritten when
   the workspace is moved (`kfs ws`), deleted when it stops; a stale file (dead pid, refused
   connection) is ignored. It is **not** in the workspace: anything written there appears in
   `kfs ls` and the editor's file tree. With the default locations the config folder is the
   workspace's sibling (`…/Kitten Space Agency/.purrTTY/` beside `…/Kitten Space Agency/kfs/`), which
   is how the runtime files find it; otherwise `PURRTTY_CONFIG_DIR` names it;
3. `purr-kfs lsp --stdio` if `purr-kfs` is on `PATH` — the standalone server of Workstreams B/C,
   which needs a .NET runtime and `KSA_DLL_DIR`, and uses the layout recorded in the workspace's
   `vehicle.h`;
4. nothing: the files open as plain `kc`/`kasm` buffers with syntax only.

So a player can keep a Neovim open on a second monitor attached to the running game, and a
developer can edit and test with no game at all.

The token keeps other users and other machines out; it does not protect against another process
of the same user, which can read the info file.

### 7.7 Tests

| Test | Where | Asserts |
| --- | --- | --- |
| `NeovimLaunchPlan_*` | `purrTTY.Kfs.Tests` (the plan is plain strings; `purrTTY.GameMod` has no test project) | argv, env, cwd for each resolution path |
| `CloseOnExit_Closes_Window` | `purrTTY.Terminal.Tests` (real `/bin/sh -c 'exit 0'`; the Windows twin with `cmd /c exit`) | the session is closed after exit; no resize throw |
| `Resize_After_Exit_Is_Ignored` | `purrTTY.Terminal.Tests` | no exception |
| `PathExt_Resolution` | `PtyInputContractTests` (pure logic, every OS) | `.cmd` / `.exe` candidates |
| `Neovim_Attaches_Headless` | `purrTTY.Kfs.Lsp.Tests`, skipped silently when `nvim` is absent | a headless Neovim with the bundled runtime files attaches to a server on a loopback port, receives a diagnostic for a broken file, and a `:KfsBuild` round trip returns |
| manual | §10 V16–V23 | in game |

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
| M2 — the extension works in the game | A4, A5 | lights and animations driven from a kC program on a real vehicle | yes: V1–V15 |
| M3 — command-line runtime | B2, B3 | `purr-kfs check / run / test` | no |
| M4 — language server, standalone | C1, C2, C3 | diagnostics, hover, completion in any LSP client via `purr-kfs lsp` | no |
| M5 — Neovim from in-game | C4, D1, D2, D3 | the full editing loop inside the game | yes: V16–V23 |
| M6 — depth | C5, C6, D4–D8 | symbols, cost lenses, navigation, terminal polish | partly |

### 8.4 Tasks

#### F1 — Scaffolding and the assembly resolver

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

- **Create** `Catalog/*`, `Tables/TableAugmenter.cs` (§4.4, §4.5 step 4).
- **Port from** spike 06 (`ServiceCatalog.cs`, `KfsInstall.cs`), which already implements the
  single-enumeration-per-kind selector scheme of §4.1.
- **Tests:** `Catalog_SelfCheck_Passes`, `Tables_Augmented_LoadAndValidate`,
  `Stock_Examples_Compile_Identically`, `Header_Compiles_Alone`,
  `Getter_Selector_Ranges_Are_Disjoint`, negative cases for each self-check rule, and snapshot
  tests of the emitted header, rows, enums and `purrtty.kinc` (byte-stable across runs).
- **Done when:** the game's loader accepts the emitted tables and the snapshots are committed.

#### A2 — Runtime core and the two host hooks

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

- **Create** `Game/*`, `Patches/BeforeInputEventsPatch.cs`, `purrTTY.GameMod/Kfs/KfsConfig.cs`;
  call `KfsExtension.Install/Uninstall` from `purrTTY.GameMod/Patcher.cs`.
- **Spec:** §4.7, §4.8, §4.10–§4.12, §7.5 (config file); game facts in
  [control recipes](../docs/kfs-info/game-targets/control-recipes.md),
  [light module](../docs/kfs-info/game-targets/light-module.md),
  [keyframe animation module](../docs/kfs-info/game-targets/keyframe-animation-module.md),
  [dispatch and threading](../docs/kfs-info/integration/dispatch-and-threading.md).
- **Tests:** only what is pure (clamps, switch-group computation over a fake list, config
  round-trip). A reviewer checks every game member used against the decompiled sources.
- **Done when:** it builds, the offline suite is still green, and V1–V15 are handed over.

#### A5 — Console command, examples, documentation

- **Create** `Console/*`, `Examples/*`; register `purr` at install; the purrTTY menu's kFS status
  entry.
- **Spec:** §4.13, §4.14.
- **Tests:** the formatter against sim part sets; `Examples_Run` for every shipped example, each
  built as its own folder project; the verb parser.
- **Docs:** `docs/how-to.md` (write a kFS program that uses parts), `docs/gotchas.md` (threading,
  indices, template sharing), `docs/code-navigation.md`, `AGENTS.md`, the `upgrade-ksa` skill's
  coupling list, and a bundle document describing the shipped extension.

#### B1 — The full test runtime

- **Extend** `KfsRuntime` / `SimVehicle` to §5.3–§5.5: scripted telemetry, stock-request recording
  with `StageApplied`, `ReadGlobal`, detach/attach, pause.
- **Tests:** §5.8 except the scenario and CLI rows.

#### B2 — `purr-kfs` command line

- **Create** `Runtime/ProjectResolver.cs` in `purrTTY.Kfs` (§6.3 step 2 — the one implementation
  the CLI and the language server share) and `purrTTY.Kfs.Cli` with `check`, `run`, `header`,
  `services`.
- **Tests:** `ProjectResolver_*` (folder project; root file with an include closure; overlays,
  including an unsaved new `#include`; pure kASM with `.include`), `Cli_Check_Exit_Codes`, output
  format, a `run` golden output.
- **Packaging:** a framework-dependent tool package as a release asset (D12); not in the mod dist.

#### B3 — Scenario files

- **Create** `Scenario/*`, `purr-kfs test`. Tomlyn is already used by the repository; add it to
  this project.
- **Tests:** `Scenario_Files_Run`; documentation with two worked scenarios.

#### C1 — LSP protocol core

- **Create** `purrTTY.Kfs.Lsp` protocol and document layers (§6.1–§6.2, §6.8) and
  `purrTTY.Kfs.Lsp.Tests`.
- **Port from** spike 05 (`Session.cs`, position mapping).
- **Tests:** `Initialize_Negotiates_Utf16_And_Requires_Token`, framing fuzz (split headers, large
  bodies), `Exit_Closes_Connection_Only`, position round-trips in three encodings.

#### C2 — Builds and diagnostics, standalone host

- **Create** `Build/*` in `purrTTY.Kfs.Lsp`; `StandaloneLanguageHost` and the `lsp` command in
  `purrTTY.Kfs.Cli` (§6.3, §6.4).
- **Tests:** `Diagnostics_Map_Positions`, `Diagnostics_Cover_Whole_Project`,
  `Unsaved_Buffers_Overlay_Disk`, `Header_Diagnostics_Anchor_On_Include`,
  `Latest_Wins_Under_Rapid_Edits`, `Compiler_Exception_Becomes_Diagnostic`,
  `Watchdog_Retires_A_Stuck_Compile` (with an injected compile function), `Kasm_Diagnostics…`,
  layout reconstruction from a `vehicle.h` (the fingerprint must equal the one in its banner).

#### C3 — Hover and completion

- **Create** `Index/*`, `Features/HoverProvider.cs`, `CompletionProvider.cs` (§6.5).
- **Tests:** `Hover_And_Completion_Know_Mod_Services`, pragma/include/member contexts, opcode hover.

#### C4 — The in-game server

- **Create** `KfsLspListener` use in `purrTTY.GameMod/Kfs/KfsLspService.cs`,
  `InGameLanguageHost.cs`; commands and notifications (§6.7); config keys; the server info file
  (§7.6); `purr lsp`.
- **Tests:** against a fake host (`Commands_Reach_The_Host`, `Target_Change_Rediagnoses`, token
  refusal, info-file lifecycle including a workspace move). In game: V16, V19, V20, V22.

#### C5 — Symbols, cost lenses, inlay hints

- §6.6 phase 2. **Tests:** `Cost_Lenses_Match_Compile_Report`, symbol ranges.

#### C6 — Navigation

- §6.6 phase 3: own `KcFrontEnd.Lower` run, position index, definition/references/hover types/
  signature help, virtual header copies. **First step is a spike** (delegated like the others):
  confirm that running `Lower` directly yields the same diagnostics as the compiler's own run and
  that the AST positions are sufficient; the surface document marks this path as read, not
  executed.

#### D1 — Terminal: exit handling

- T1 and T2 of §7.3 in `purrTTY.Terminal` / `purrTTY.Display`.
- **Tests:** `CloseOnExit_Closes_Window`, `Resize_After_Exit_Is_Ignored` (Unix and Windows twins,
  condition-waits only).

#### D2 — Neovim runtime files

- **Create** `purrTTY.GameMod/nvim/**` (§7.4) and the `CopyCustomContent` entries.
- **Port from** spike 05 (`nvim/*.lua`).
- **Tests:** `Neovim_Attaches_Headless` (silently skipped without `nvim`): launched exactly as the
  launcher would (`--cmd "lua dofile(…)"`, the environment variables, a runtime folder whose path
  contains a space), including the no-server and server-restart paths, and once with a
  `runtimepath` reset in a test `init.lua`.

#### D3 — Launcher, menu, console

- **Create** `Runtime/NeovimLaunchPlan.cs` in `purrTTY.Kfs`; `purrTTY.GameMod/Kfs/KfsNeovimLauncher.cs`;
  the `kFS` submenu; `purr nvim`; config keys (§7.2, §7.5).
- **Tests:** `NeovimLaunchPlan_*` in `purrTTY.Kfs.Tests` (argv, environment and working directory
  for each resolution path, on Unix and Windows conventions). Confirm the login-shell fallback on
  macOS and Linux. In game: V17, V18, V21, V23.

#### D4 — Path and layout details

- T3 and T4 of §7.3. **Tests:** `PathExt_Resolution`, and a launch through a `.cmd` shim on the
  Windows leg.

#### D5, D6, D7, D8 — Terminal polish (optional, independent)

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
    **`ImageLoaded` is no longer chained** (§4.7's last-but-one bullet is superseded): binding touches
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

## 9. Risks

| # | Risk | Likelihood | Impact | Mitigation |
| --- | --- | --- | --- | --- |
| R1 | A game update reshapes a patch target, the service-row schema or the header mechanism. kFS is a prototype and will change. | High over time | Extension stops working | All-or-nothing install with a logged reason (§4.5); offline tests run against each new `KSA.dll` and fail first; `upgrade-ksa` checklist gains the kFS items in [risks and compatibility](../docs/kfs-info/modding/risks-and-compatibility.md). |
| R2 | A stock update claims an id in `0x5000–0x50FF`. | Low | Tables rejected at the dry run → extension disabled, kFS intact | The dry run (§4.5 step 4) turns it into a log line; moving the block is a one-constant change, and images are rebuilt from source on every load anyway. |
| R3 | Stock kFS gains lights/animation control itself. | Plausible | Duplicate features | Keep the mod's names prefixed (`x_`), retire them when stock covers the feature. |
| R4 | A patched method is inlined at its call site under the game's runtime, so a hook silently never fires. | Low for the chosen targets (large bodies, virtual/interface dispatch) | A service is rejected or returns 0 | `purr status` reports per-hook call counters; in-game checklist item V2. |
| R5 | Patches are applied after another mod (or the game) has already built the tables. | Low | Extension disabled for the session | Detected (§4.5 step 5) and reported. Nothing in the game reads the tables before mods load, so this takes another mod; the remedy would be to publish the tables from a `[StarMapBeforeMain]` hook (**unverified** — not tried). |
| R6 | A mod exception inside a dispatch faults the player's flight computer. | Would be severe | Loss of control mid-flight | Every patch body is `try`/`catch`; tests inject faults into the router and assert the computer keeps running. |
| R7 | Reading live modules from the dispatch thread races a main-thread writer (another mod, the part window). | Low | A stale or torn read of one value for one tick | Reads are single fields; nothing is cached; if in-game testing shows trouble, switch `KsaVehicleParts` reads to a per-frame snapshot built in the pump (the interface does not change). |
| R8 | Light template cloning conflicts with another mod doing the same (zippo, gatOS). | Medium when both are active on one light | Last writer wins; a reset may restore the other mod's template | `EnsurePrivate` re-clones from whatever template is current and only restores when the template is still ours. |
| R9 | An animation driven by its own state machine (parachute bay doors) fights the program. | Certain for those parts | Confusing behaviour | `LINKS` bit 2 marks them; the console listing says so. |
| R10 | Programs polling services keep the vehicle off rails and limit time warp. | Inherent to any running program | Performance at high warp | Documented; examples use low tick rates. |
| R11 | The in-game language server's compiles cost frame time, or a stuck compile leaves a 16 MiB thread running in the game. | Low | Stutter; a leaked thread | One serial low-priority compile thread, a debounce, and the watchdog of §6.3. Concurrent compiles against one tables instance are safe (read from the code and stress-tested in spike 05), so the stock editor is unaffected. |
| R12 | A local process other than the player's editor connects to the in-game server. | Low | Could load and start programs on the vehicle | Loopback only, random port, per-session token required at `initialize`; commands that act on the vehicle are a separate, switchable capability. The token stops other users and machines, not another process of the same user (it is in the info file). |
| R13 | Neovim is not installed or not on the game's `PATH`. | Common on macOS/Steam launches | "Open in Neovim" fails | Configurable absolute path, login-shell fallback, and a clear message; the feature degrades to nothing else. |
| R14 | Terminal key-handling gaps make Neovim awkward (toggle hotkey swallowed, some Ctrl/Alt chords, no focus events). | Known | Usability | Tasks D5–D7; none blocks the feature. |
| R15 | Redistribution: `KSA.dll` must never be copied into the repository, test output or tool packages. | — | Legal | Compile-only references, in-memory resolver, CI check that `KSA.dll` appears in no test output and no game assembly in the mod dist or a tool package. |
| R16 | The property enumerations are positional and baked into images. | Certain if mishandled | Programs built earlier read the wrong property | Fillers reserve room; new properties replace a filler and widen the sub-range; the self-check and a snapshot test pin the lists. |
| R17 | Installing the output tee changes where `KfsIntegration.Error` lines appear. | Likely | Cosmetic | Check V16; if unwanted, filter in the tee. |

## 10. In-game validation checklist

Run once on each supported OS before calling a workstream done. Each item names what it settles.

**Workstream A**

- V1 — Start the game with the mod; the log shows `purrTTY kFS extension: active …`. *(patch timing, R5)*
- V2 — `kfs on`; `purr status` shows the three hooks (and the optional one) applied; after running any example the
  accept/answer/pump counters are non-zero. *(inlining, R4)*
- V3 — Open the kFS reference window: the Services page lists the `x_*` rows. *(tables reached the UI)*
- V4 — `purr parts` on a vehicle with lights, a solar array and a cargo bay lists them with sensible names.
- V5 — `purr examples`, then load `x_blink/blink.c` onto the vehicle and start it: the lights blink; the part
  window's checkbox follows; other vehicles of the same type are unaffected.
- V6 — `light_show.c`: colour, cone and intensity change on **one** light only; a second light of
  the same part type keeps its stock look; `x_light_reset` restores it. *(template cloning, R8)*
- V7 — `deploy_when_safe.c` / a manual deploy: the array deploys at stock speed; `STATE` and
  `POSITION` read back correctly; solar output starts only when deployed.
- V8 — `actuator_sweep.c` on a cargo bay: follows the target; the stock slider shows the same value.
- V9 — Stage away a part that has a light a running program addresses: the program keeps running,
  the slot reads not present, writes are rejected with the documented event. Dock two vehicles:
  `DIRTY` becomes 1; `x_parts_rescan()` renumbers.
- V10 — Pause, switch a light from a still-queued command, unpause: no exception; behaviour as documented.
- V11 — Time warp at 10× and 100× with a 50 Hz program polling services: no host fault; note the
  frame time and skipped ticks. *(budget, R7, R10)*
- V12 — Save and reload with overrides active: the game loads; light **appearance** is back to
  stock, while switch states and animation goals are as saved (the game saves those itself); the
  program is gone (as stock kFS).
- V13 — `kfs off` / `kfs on`, star-system change, mod unload: no exception, no leaked overrides.
- V14 — A stock example (`kC_ascent_simple`) still builds, loads and flies with the mod present. *(no regression)*
- V15 — Remove the mod and load a program built with it: clean "unknown service id" rejection.

**Workstream C / D**

- V16 — The language-server status line appears in the log with its port; `purr lsp` prints it; `kfs` errors still appear where expected with the output tee installed.
- V17 — purrTTY menu › kFS › Edit in Neovim opens a terminal running `nvim` on the file; the
  buffer shows diagnostics for a deliberate error within a second of typing; fixing it clears them.
- V18 — A program using `<purrtty.h>` shows no false errors; hovering a `x_*` call shows its documentation.
- V19 — `THRUSTER_COUNT` and `commands.*` resolve against the **controlled vehicle's** layout;
  switching vehicles and re-targeting updates diagnostics.
- V20 — `:KfsLoad` and `:KfsStart` from Neovim load and start the program; `kfs_logf` lines appear in Neovim.
- V21 — Quit Neovim: the terminal window closes; resizing a window whose process has exited does not throw.
- V22 — Frame time with the server idle and while typing quickly in a 500-line file. *(compile cost on the game process)*
- V23 — With a second Neovim started outside the game in the same workspace, it attaches through
  the server info file; with the game closed it falls back to `purr-kfs lsp --stdio` when installed.

## 11. Open decisions

| # | Decision | Recommendation | Alternatives |
| --- | --- | --- | --- |
| D1 | Function prefix and header name | **Decided (2026-11-25): `x_` prefix** — functions `x_light_set_on`…, constants `X_LIGHT_ON`…, kASM symbols `SVC_X_…`, enum keys `x_light_prop`…. The header stays `<purrtty.h>` and the console command `purr` (assumed; not part of the decision). The spikes ran with the earlier spelling `purr_` / `PURR_`; only the names differ. | `purr_`; `kfs_` |
| D2 | Light index unit | Every `LightModule` (emitter), with `SWITCH_GROUP` exposing shared switches | One index per switchable part |
| D3 | Switchless lights | Listed, `HAS_SWITCH` 0, on/off rejected | Hide them; or fake off with range 0 (untested in game) |
| D4 | When lists are rebuilt | At program load and on explicit rescan | Also automatically when the vehicle changes (indices would move under a running program) |
| D5 | Overrides when a program stops | Kept until reset/reload (a light stays as commanded, like any hardware) | Auto-reset on stop or halt |
| D6 | Symmetry | Not mirrored | Mirror like the stock buttons (opt-in argument) |
| D7 | Should parts services honour `Vehicle.IsControllable`? | No — the stock part window does not either | Reject in fallback mode |
| D8 | Colour range | Clamp to 0–1 like gatOS and zippo | Allow > 1 (HDR) once confirmed in game |
| D9 | Id block | `0x5000–0x50FF` | Any other unused block |
| D10 | Where the language server runs by default | Inside the game, on a loopback socket (no .NET needed on the player's machine, real vehicle layout, can act on the vehicle) | Standalone only |
| D11 | Server-side vehicle commands (`load`, `start`, `stop`) | Enabled, token-guarded, with a config switch | Diagnostics-only server |
| D12 | Distribution of `purr-kfs` | Separate release asset (framework-dependent), not in the mod folder | Bundle in the mod; self-contained per-OS builds |
| D13 | Neovim runtime files | Shipped in the mod and loaded by absolute path (`--cmd "lua dofile(…)"`, folder passed in an environment variable) for the in-game launch; documented for manual install | Publish as a standalone Neovim plugin repository |
| D14 | kASM selector constants | A generated `purrtty.kinc` the program includes | Patch `BuiltinIncludes` so `<kfs.kinc>` carries them |
| D15 | Bench behaviour for mod services | Optional patch 4: getters silent, setters rejected with a clear message | Leave the stock "no vehicle to act on" for every call |
| D16 | Console command | The mod's own `purr` command, registered like `kfs` (no patch, works with the kFS flag off) | Patch `KfsConsoleCommands.Execute` to add `kfs parts` verbs |
| D17 | How the tables are published | Assign `KfsIntegration._tables` while it is null; Harmony postfix on `LoadEmbedded` only as a fallback | Postfix only (the variant the spikes ran) |
| D18 | Values of `X_DEPLOY_STATE` | **Decided (2026-11-25, following the M1 code review): `X_STATE_NONE` 0, `X_STATE_DEPLOYED` 1, `_RETRACTED` 2, `_DEPLOYING` 3, `_RETRACTING` 4, `_BROKEN` 5**, so the 0 every getter reads where there is no part is never a state (`STATE == X_STATE_DEPLOYED` was true for an absent part). Converted from the game's 0–4 at the boundary (`PartMath`). Other properties keep 0 as their "nothing"; programs check `PRESENT` first. | Keep the game's 0–4 (0 = deployed) |
| D19 | Ceilings for intensity and range | **Decided (2026-11-25, following the M1 code review): at most 1e6 (intensity) and 1e6 m (range)**, `PartMath.MaxIntensity` / `MaxRangeM`; the VM's `finite:>=0` domain admits up to `FLT_MAX` | No ceiling; a tighter one near the stock values (≤ 50) |

## Appendix A — evidence index

| Evidence | What it is | Where |
| --- | --- | --- |
| Bundle | 95 documents on kFS and its extension points, with file:line citations into the decompiled game | [`docs/kfs-info/index.md`](../docs/kfs-info/index.md) |
| Experiments E1–E24 | Results of running the real toolchain offline | [`docs/kfs-info/modding/offline-harness.md`](../docs/kfs-info/modding/offline-harness.md) |
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
