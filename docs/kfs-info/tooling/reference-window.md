---
type: Subsystem Reference
title: kFS reference window
description: KfsReferenceWindow, the editor's built-in Opcodes and Services reference — its two pages, the exact text shown, how each column is derived from the game tables, and when a mod-added table row appears in it.
resource: ksa-decomp:KSA.Kfs/KfsReferenceWindow.cs
tags: [kfs, tooling, reference, services, opcodes, imgui, extension]
sources:
  - id: reference-window
    resource: ksa-decomp:KSA.Kfs/KfsReferenceWindow.cs
    title: KfsReferenceWindow (decompiled)
  - id: reference-page
    resource: ksa-decomp:KSA.Kfs/KfsReferencePage.cs
    title: KfsReferencePage (decompiled)
  - id: editor-window
    resource: ksa-decomp:KSA.Kfs/KfsEditorWindow.cs
    title: KfsEditorWindow (decompiled)
  - id: game-tables
    resource: ksa-decomp:KSA.Kfs.Vm.Tables/GameTables.cs
    title: GameTables (decompiled)
  - id: services-json
    resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.services.json
    title: services.json (embedded resource)
  - id: costs-json
    resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.costs.json
    title: costs.json (embedded resource)
  - id: opcodes-json
    resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.opcodes.json
    title: opcodes.json (embedded resource)
  - id: table-records
    resource: ksa-decomp:KSA.Kfs.Vm.Tables/ServiceRow.cs
    title: ServiceRow, ServiceInfo, OpcodeRow, OpcodeInfo, CoreCostTable records (decompiled)
  - id: kfs-integration
    resource: ksa-decomp:KSA.Kfs/KfsIntegration.cs
    title: KfsIntegration (decompiled)
  - id: imgui-window
    resource: ksa-decomp:KSA/ImGuiWindow.cs
    title: KSA ImGuiWindow base class (decompiled)
  - id: offline-harness
    resource: /modding/offline-harness.md
    title: Offline harness experiments E1–E12
generated: { by: claude-code/claude-fable-5-2, at: 2026-11-25T21:56:52Z }
status: stable
ksa_build: "2026.11.25.5949"
---

# What it is

`KSA.Kfs.KfsReferenceWindow` (`public sealed class KfsReferenceWindow : ImGuiWindow, IStaticWindow, IDisposable`)
is the only built-in documentation viewer in kFS. It has exactly **two pages**, selected at
construction by the enum[^reference-page]

```csharp
public enum KfsReferencePage : byte { Opcodes, Services }
```

One window instance shows one page. The editor opens them from its Reference menu
(Reference > Opcodes, Reference > Services) and keeps one instance of each
(`_opcodeReference`, `_serviceReference`).[^editor-window]

There is **no page for messages, events, enums, traps, the system block or diagnostics.** Those are
reachable only through other tools: `kfs msg` and the Bus tab for messages, `kfs manual` and the
Diagnostics tab tooltip for diagnostics, and the generated `vehicle.h` and the hard-coded `kfs.h` for
enums and constants (see [console commands](/tooling/console-commands.md) and
[kC generated headers](/language/kc-generated-headers.md)).

| page | window title | rows come from | hard-coded in the window |
| --- | --- | --- | --- |
| Opcodes | `kFS Opcodes` | `tables.OpcodesData.Rows` (opcodes.json), `tables.OpcodesByMnemonic`, `tables.CostTables`, `tables.CostsData.Cores` (costs.json) | intro text, section titles, the five fixed column headers |
| Services | `kFS Services` | `tables.ServicesData.Services` (services.json), `tables.Services`, `tables.CostTables[core].ServiceClassCycles`, `tables.CostsData.LogCyclesPerValue` | intro text, all eight column headers (including the literal `Cycles K8 / K32 / K32F / K32D`) |

So **both pages are generated from the game tables**; nothing about individual opcodes or services is
written in the window's code.[^reference-window]

# Construction and lifetime

```csharp
public KfsReferenceWindow(GameTables tables, KfsReferencePage page)
public void Open()                       // SetShown(true) and request focus
public override bool IsShown => _show && KfsPrototype.Enabled;
```

- The constructor copies the core names (`_cores = tables.CostsData.Cores.ToArray()`), sets the title,
  `UseConsoleChrome = true`, `ConsoleSignature = "KSA-KFS"`, and calls `BuildOpcodes(tables)` or
  `BuildServices(tables)`.[^reference-window]
- **The page content is built once, in the constructor**, into private lists (`_sections`,
  `_services`). The window never looks at the tables again.
- Default size 1100 x 760 (times the interface scale); on its first draw it resizes to 60 % of the
  game window's width and centres itself.
- Unlike the editor, the window hides itself while the prototype flag is off.
- The editor passes `_workbench.Tables`, which in the game is `KfsIntegration.Tables` — a single
  lazily loaded `GameTables.LoadEmbedded()` instance.[^editor-window][^kfs-integration]

# Common controls

Both pages draw, top to bottom:[^reference-window]

1. a dimmed, wrapped intro sentence (page-specific, quoted below);
2. `Filter` and a 64-character text field — a case-insensitive substring match against three fields
   of each row;
3. a `Copy as CSV` button, right-aligned; tooltip: "Copies the whole table, ignoring the filter, to
   the clipboard as comma-separated values.";
4. the table, in a scrolling child.

CSV rows use `,` separators and CRLF line ends; a field containing `,`, `"`, CR or LF is quoted with
doubled quotes (`CsvRow`).

# Opcodes page

Intro text:

> Stack: before -> after, top on the right; :64 is a two-cell value. Cycles per core; s = soft
> operation, +Nw = per 4-byte word. The Bench and Mk2 are K32F.

## How it is built

`BuildOpcodes` walks `tables.OpcodesData.Rows` in file order.[^reference-window][^table-records]

- **Sections.** A new collapsible section starts whenever the mapped title of `row.Section` differs
  from the previous row's. The mapping is hard-coded in `SectionTitle`:

  | `section` in opcodes.json | title shown |
  | --- | --- |
  | `4.3` | Constants and stack |
  | `4.4` | Memory |
  | `4.5` | Control flow and calls |
  | `4.6` | 32-bit arithmetic, comparison and conversion |
  | `4.7` | 64-bit arithmetic, comparison and conversion |
  | anything else | the raw `section` string |

- **One entry per mnemonic.** Each `row.Ops` element is `[mnemonic, hex]`; a row with several ops
  yields several entries sharing the row's immediates and stack text.
- **Columns.**

  | column | value |
  | --- | --- |
  | `Op` | `tables.OpcodesByMnemonic[mnemonic].Mnemonic` |
  | `Hex` | the row's hex string for that op |
  | `Immediate` | `row.Imm` entries joined with `, `, each `type` or `type name` |
  | `Stack` | `row.Stack` with backticks removed and spec references such as ` (§4.5)` or `; rule 12)` stripped (`WithoutSpecReferences`) |
  | `What it does` | `OpcodeInfo.Summary` |
  | one column per core, headed by the core name from `costs.json` (`K8`, `K32`, `K32F`, `K32D`) | `CycleText(tables.CostTables[core], info)` |

- **`CycleText`.** If the core has a per-opcode override (`costs.OpcodeOverride[info.Code]`), the
  text is the override's cycles plus `s` when it is a soft operation. Otherwise it is the category
  cost `costs.Cycles[info.CategoryIndex]`, plus `+<n>w` when `costs.PerWord[...] > 0`, plus `s` when
  `costs.Soft[...]`, plus `+svc` when the opcode's category is `syscall`.
- **Filter** matches the mnemonic, the summary or the stack text; a section with no matching entry is
  hidden. Sections are open by default.
- **CSV** header: `Group,Op,Hex,Immediate,Stack,Description,<core names...>`.

## Content in this build

opcodes.json has 86 rows producing 146 mnemonics in five sections:[^opcodes-json]

| section | count | mnemonics |
| --- | --- | --- |
| Constants and stack | 15 | `NOP BRK TRAP PUSH8 PUSH16 PUSH32 DUP DROP SWAP OVER PUSH64 DUP2 DROP2 SWAP2 OVER2` |
| Memory | 23 | `LDL STL ADDRL LDG STG LDW STW LDH LDHU STH LDB LDBU STB COPY ZERO INDEX BOUNDS LDL64 STL64 LDG64 STG64 LD64 ST64` |
| Control flow and calls | 9 | `JMP JZ JNZ CALL RET ENTER CALLF YIELD SYSCALL` |
| 32-bit arithmetic, comparison and conversion | 46 | `ADD SUB MUL DIV DIVU REM REMU NEG AND OR XOR NOT SHL SHR SAR EQ NE LT LTU LE LEU GT GTU GE GEU FADD FSUB FMUL FDIV FSQRT FNEG FABS FEQ FNE FLT FLE FGT FGE I2F U2F F2I F2U SEXT8 SEXT16 ZEXT8 ZEXT16` |
| 64-bit arithmetic, comparison and conversion | 53 | `ADD64 SUB64 MUL64 DIV64 DIVU64 REM64 REMU64 NEG64 AND64 OR64 XOR64 NOT64 SHL64 SHR64 SAR64 EQ64 NE64 LT64 LTU64 LE64 LEU64 GT64 GTU64 GE64 GEU64 DADD DSUB DMUL DDIV DSQRT DNEG DABS DEQ DNE DLT DLE DGT DGE I2L U2L L2I L2D LU2D D2L D2LU I2D U2D D2I D2U F2D D2F L2F F2L` |

Example of a rendered row (from the JSON row for `PUSH32`): Op `PUSH32`, Hex `0A`, Immediate `u32`,
Stack `→ [value] (int or float bits)`, What it does "Pushes a 32-bit constant: an integer or a
float32 bit pattern.", followed by the four per-core cycle cells. The full per-opcode table with
encodings and costs is in [opcodes](/vm/opcodes.md) and
[cost model and budgets](/vm/cost-model-and-budgets.md).

# Services page

Intro text:

> kASM: push the arguments in order (first deepest), then SYSCALL. Pilot-only services steer the
> game's flight computer and are refused in a program that claims ALLOCATION.

## How it is built

`BuildServices` walks `tables.ServicesData.Services` (the rows of services.json) in file order.
[^reference-window][^table-records]

- **Reserved rows.** When `row.Reserved` is true **or** `tables.Services` has no `ServiceInfo` for
  the id, the entry shows only `Id`, the symbol `(reserved)` (dimmed) and `row.Summary`.
- **Active rows.** For every other row, with `info = tables.Services[id]`:

  | column | value |
  | --- | --- |
  | `Id` | `0x` + id as four hex digits |
  | `kC` | `row.Kc` with backticks removed |
  | `kASM` | `row.Symbol` |
  | `kASM push` | `PushText(info)`: `none` for no arguments, else one `type name` per stack entry in push order; an argument spanning several entries gets `.x`, `.y`, `.z`, `.w` suffixes; names come from `info.ArgumentNames` (parsed by the loader from the parentheses of the `kc` column) and `info.Argument(i)` |
  | `Result` | `row.Results` with backticks removed, but only when `row.ResultTypes` is non-empty |
  | `Mode` | `any` when `info.RequiresStock == null`, else `pilot` |
  | `Cycles K8 / K32 / K32F / K32D` | `<CostClass>: <a> / <b> / <c> / <d>` with one value per core from `tables.CostTables[core].ServiceClassCycles[info.CostClass]`, plus `, +<n> per value` when `info.PerValue` (`n` = `tables.CostsData.LogCyclesPerValue`) |
  | `What it does` | `info.Summary` |

- **Filter** matches the kASM symbol, the kC text or the summary.
- **CSV** header: `Id,kC,kASM,kASM push,Result,Mode,Cost class,<core names...>,Cycles per value,Description`.

## Content in this build

The table below is what the page shows for the 21 rows of this build's services.json.
`Inferred:` it was produced by re-implementing `BuildServices`, `PushText` and the loader's
argument-name rule (`GameTables.Arguments`) in a script over services.json and costs.json, not
captured from the running game.[^services-json][^costs-json][^game-tables]

| Id | kC | kASM | kASM push | Result | Mode | Cycles K8 / K32 / K32F / K32D | What it does |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 0x0001 | `kfs_logf(fmt, …)` | `SVC_LOG` | u32 fmt, ptr … |  | any | Medium: 60 / 30 / 30 / 30, +10 per value | Prints a formatted line to the console. kC's kfs_logf builds the argument buffer; in kASM, fmt is a .format index and argp the buffer's address. |
| 0x0002 | `trace(channel, v)` | `SVC_TRACE` | u32 channel, f32 v |  | any | Light: 20 / 10 / 10 / 10 | Records a float on a named channel, for kfs trace and the oscilloscope. In kASM the channel is a .string index. |
| 0x0003 | `kfs_halt()` | `SVC_HALT` | none |  | any | Light: 20 / 10 / 10 / 10 | Halts the computer, as a trap does but without an error. kfs start or kfs reset restarts it. |
| 0x0004 |  | (reserved) |  |  |  |  | Reserved for an event log service. |
| 0x0010 | `kfs_sleep_until(t_us)` | `SVC_SLEEP_UNTIL` | i64 t_us |  | any | Medium: 60 / 30 / 30 / 30 | Sleeps until sys.met_us reaches t_us. Not active yet: the request is logged and the computer keeps running. |
| 0x0011 | `kfs_sleep_until_event(e, p)` | `SVC_SLEEP_UNTIL_EVENT` | u32 e, cell p |  | any | Medium: 60 / 30 / 30 / 30 | Sleeps until an event (apoapsis, periapsis, SOI change, ...). Not active yet: the request is logged and the computer keeps running. |
| 0x0020 | `kfs_stage()` | `SVC_STAGE` | none |  | any | Light: 20 / 10 / 10 / 10 | Activates the next stage, as the staging key does. |
| 0x0030 | `kfs_hold_direction(dir, roll)` | `SVC_HOLD_DIRECTION` | u32 dir, u32 roll |  | pilot | Light: 20 / 10 / 10 / 10 | Points the vehicle along a named direction (prograde, radial out, toward the target, ...) and holds it there. |
| 0x0031 | `kfs_hold_rates()` | `SVC_HOLD_RATES` | none |  | pilot | Light: 20 / 10 / 10 / 10 | Rate hold: stops the vehicle rotating. |
| 0x0032 | `kfs_release_attitude()` | `SVC_RELEASE_ATTITUDE` | none |  | pilot | Light: 20 / 10 / 10 / 10 | Releases attitude control back to manual. |
| 0x0033 | `kfs_set_attitude_profile(p)` | `SVC_SET_ATTITUDE_PROFILE` | u32 p |  | pilot | Light: 20 / 10 / 10 / 10 | Sets how tightly attitude is held against how much RCS it uses: strict, balanced or relaxed. |
| 0x0034 | `kfs_set_throttle(x)` | `SVC_SET_THROTTLE` | f32 x |  | pilot | Light: 20 / 10 / 10 / 10 | Sets the main-engine throttle, 0 to 1, until the pilot moves the throttle. |
| 0x0035 | `kfs_engines_on()` | `SVC_ENGINES_ON` | none |  | pilot | Light: 20 / 10 / 10 / 10 | Turns the engine switch on. The engines fire only with the switch on and a throttle above zero. |
| 0x0036 | `kfs_translate(dv)` | `SVC_TRANSLATE` | f32 dv.x, f32 dv.y, f32 dv.z |  | pilot | Light: 20 / 10 / 10 / 10 | RCS translation by dv m/s along the control axes. Always rejected for now: the game has no automatic translation yet. |
| 0x0037 | `kfs_hold_heading(heading_rad, pitch_rad, roll)` | `SVC_HOLD_HEADING` | f32 heading_rad, f32 pitch_rad, u32 roll |  | pilot | Light: 20 / 10 / 10 / 10 | Holds a compass heading and a pitch above the horizon, in the local east-north-up frame. Pitch within +-90 degrees. |
| 0x0038 | `kfs_hold_quat(q, roll)` | `SVC_HOLD_QUAT` | f32 q.x, f32 q.y, f32 q.z, f32 q.w, u32 roll |  | pilot | Light: 20 / 10 / 10 / 10 | Holds an exact attitude: the control point's rotation into the inertial frame (CCI). |
| 0x0039 | `kfs_engines_off()` | `SVC_ENGINES_OFF` | none |  | pilot | Light: 20 / 10 / 10 / 10 | Turns the engine switch off. |
| 0x0040 | `kfs_plan_circularize(at)` | `SVC_PLAN_CIRCULARIZE` | u32 at | u32 burn | any | Heavy: 200 / 100 / 100 / 100 | Plans a circularisation burn at apoapsis or periapsis and returns its handle. The plan fails (state ABORTED) if that apsis isn't ahead. |
| 0x0041 | `kfs_plan_burn(dv, t_us)` | `SVC_PLAN_BURN` | f32 dv.x, f32 dv.y, f32 dv.z, i64 t_us | u32 burn | any | Heavy: 200 / 100 / 100 / 100 | Plans a burn of dv m/s in the VLF frame (prograde, normal, radial) at mission time t_us and returns its handle. |
| 0x0042 | `kfs_burn_start(b)` | `SVC_BURN_START` | u32 b |  | pilot | Light: 20 / 10 / 10 / 10 | Executes a planned burn: the flight computer aligns, then ignites at the planned time. A handle whose plan isn't ready yet is held until it is. |
| 0x0043 | `kfs_burn_abort()` | `SVC_BURN_ABORT` | none |  | pilot | Light: 20 / 10 / 10 / 10 | Aborts the current burn. |

The raw table with every JSON field is in [services table](/tables/services-table.md); what each
service does on the game side is in [KfsServices, game side](/integration/kfs-services-game-side.md).

# When does a mod-added row show up?

The window is a pure function of the `GameTables` object **at the moment it is constructed**.

| a mod adds ... | result in the Services page |
| --- | --- |
| a `ServiceRow` to `tables.ServicesData.Services` **and** a matching `ServiceInfo` to `tables.Services`, before the window exists | a normal row, automatically |
| only the `ServiceRow` | a `(reserved)` row showing just the id and summary |
| only the `ServiceInfo` | nothing: the page iterates `ServicesData.Services` |
| a `ServiceInfo` whose `CostClass` is not a key of every core's `ServiceClassCycles` | the constructor throws `KeyNotFoundException`, so the window never opens |
| anything, after the player already opened Reference > Services | nothing until a new window object is created: the editor caches the instance and the instance caches its rows |

The mutable pieces a mod would touch are all public: `GameTables.ServicesData`
(`ServicesFile.Services` is a `List<ServiceRow>`), `GameTables.Services`
(`Dictionary<ushort, ServiceInfo>`, public getter), `GameTables.CostTables` and
`CoreCostTable.ServiceClassCycles` (`Dictionary<string, int>`).[^game-tables][^table-records] Whether
changing them is *safe* for images and the cost-table hash is a separate question; see
[game tables loader](/tables/game-tables-loader.md) and
[services table](/tables/services-table.md).

Opcode rows work the same way through `tables.OpcodesData.Rows`, `tables.OpcodesByMnemonic` and
`tables.CostTables`; a new `section` value appears under its raw string as the heading.

# Modding notes

- **Visibility.** The class, its constructor, `Open()` and the enum are public. The row models
  (`OpcodeEntry`, `OpcodeSection`, `ServiceEntry`), the lists and every `Build*` / `Draw*` method are
  private. `CsvRow` is `internal static`.[^reference-window]
- **Making mod services appear.** Patch the tables before the first `new KfsReferenceWindow(...)`
  — in practice before the player first opens Reference > Services, and safest before
  `KfsIntegration.Tables` is first read by anything, because compiled images and headers also cache
  table-derived data. The tested way to do that is a Harmony postfix on `GameTables.LoadEmbedded`
  that returns tables loaded from an augmented `services.json`: the loader then builds the
  `ServiceRow` and its `ServiceInfo` together, and in an offline run against the real `KSA.dll`
  `KfsIntegration.Tables` returned that augmented instance on every read (the window itself was not
  opened there).[^offline-harness] Prefer it to mutating the loaded instance in place — see
  [adding a service](/modding/adding-a-service.md). If the patch can only land later, either clear
  the editor's cached `_serviceReference` field by reflection or open a fresh window from the mod:
  `new KfsReferenceWindow(KfsIntegration.Tables, KfsReferencePage.Services).Open()`. Each window
  constructed this way registers itself permanently in `ImGuiWindow`'s static window list, so create
  it once and keep it.[^imgui-window]
- **A mod-specific reference page** cannot be added to this window (two-value enum, private builders).
  A separate ImGui window, optionally reached from a postfix on `KfsEditorWindow.DrawMenuBar`, is the
  practical route (see [editor window](/tooling/editor-window.md)).
- **Messages and enums added by a mod** are never shown here. They surface through `kfs msg <name>`
  (which reads `Tables.MessagesData.Messages` at call time), the Bus tab, and the generated headers.
- **The window obeys the flag:** it is hidden whenever `KfsPrototype.Enabled` is false.
- **Main thread only:** construction walks the tables synchronously; with 146 opcode entries and 21
  services this is cheap.

[^reference-window]: KfsReferenceWindow.cs — row models :18-65; default size :69; `IsShown` :103-113; constructor :115-131; `Open` :133-137; `DrawContent` (intro texts :155 and :159, filter :162-169, Copy as CSV :170-189) :139-203; `SetInitialSize` :205-213; `Matches` :215-226; `SectionTitle` :228-239; `BuildOpcodes` :241-281; `CycleText` :283-310; `DrawOpcodes` (column headers :333-342) :312-371; `BuildServices` :373-433; `PushText` :435-467; `DrawServices` (column headers :476-483) :469-516; `OpcodesCsv` :518-546; `ServicesCsv` :548-583; `CsvRow` :585-604; `Plain` :606-609; `WithoutSpecReferences` (regexes :91-93) :611-616.
[^reference-page]: KfsReferencePage.cs:3-7.
[^editor-window]: KfsEditorWindow.cs — cached windows :602-604; Reference menu :986-1005; `OpenReference` :1008-1011.
[^game-tables]: GameTables.cs — data properties (`OpcodesData`, `CostsData`, `ServicesData`, ...) :25-51; `OpcodesByMnemonic`, `CostTables`, `Services` :55-67; `LoadEmbedded` :80-84; `BuildServices` (validation, `ServiceInfo` creation) :538-582; `Arguments` (argument names from the `kc` column) :584-639.
[^services-json]: KSA.Kfs.Vm.Tables.Data.services.json — 21 entries, one of them (`id` 4) reserved.
[^costs-json]: KSA.Kfs.Vm.Tables.Data.costs.json — `cores` = K8, K32, K32F, K32D; `service_classes` Light 20/10/10/10, Medium 60/30/30/30, Heavy 200/100/100/100; `log_cycles_per_value` = 10.
[^opcodes-json]: KSA.Kfs.Vm.Tables.Data.opcodes.json — 86 `rows`, sections 4.3 to 4.7.
[^table-records]: ServiceRow.cs:5; ServicesFile.cs:5; ServiceInfo.cs (record with `Argument(int)`); OpcodeRow.cs; OpcodesFile.cs; OpcodeInfo.cs:3; CoreCostTable.cs:5-27; CostsFile.cs:5.
[^kfs-integration]: KfsIntegration.cs — `Tables` :89.
[^imgui-window]: ImGuiWindow.cs — constructor self-registration :116-119; no removal API.
[^offline-harness]: Experiments E2 and E10 in the offline harness document.
