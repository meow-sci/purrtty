---
type: Data Table Reference
title: catalogue.json — processor parts, tick rates and the bench part
description: Schema and full contents of the embedded processor catalogue, how each PartDescription is derived, what the bench part is, and how a part is chosen for the bench and for vehicles.
resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.catalogue.json
tags: [kfs, tables, catalogue, processors, parts, tick-rate, bench]
sources:
  - id: catalogue-json
    resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.catalogue.json
    title: catalogue.json (embedded resource)
  - id: catalogue-records
    resource: ksa-decomp:KSA.Kfs.Vm.Tables/CatalogueFile.cs
    title: CatalogueFile, CatalogueDefaults, PartRow, BenchPartRow, PartDescription (decompiled)
  - id: game-tables
    resource: ksa-decomp:KSA.Kfs.Vm.Tables/GameTables.cs
    title: GameTables (decompiled)
  - id: enums-json
    resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.enums.json
    title: enums.json (core_class)
  - id: kfs-vehicle-host
    resource: ksa-decomp:KSA.Kfs/KfsVehicleHost.cs
    title: KfsVehicleHost (decompiled)
  - id: workbench
    resource: ksa-decomp:KSA.Kfs.Host.Workbench/Workbench.cs
    title: Workbench (decompiled)
  - id: bench-machine
    resource: ksa-decomp:KSA.Kfs.Host/BenchMachine.cs
    title: BenchMachine (decompiled)
  - id: image-reader
    resource: ksa-decomp:KSA.Kfs.Vm.Image/ImageReader.cs
    title: ImageReader (decompiled)
  - id: image-builder
    resource: ksa-decomp:KSA.Kfs.Vm.Image/ImageBuilder.cs
    title: ImageBuilder (decompiled)
  - id: kc-front-end
    resource: ksa-decomp:KSA.Kfs.KC/KcFrontEnd.cs
    title: KcFrontEnd (decompiled)
  - id: assembler
    resource: ksa-decomp:KSA.Kfs.KAsm/Assembler.cs
    title: Assembler (decompiled)
  - id: kvm-machine
    resource: ksa-decomp:KSA.Kfs.Vm/KvmMachine.cs
    title: KvmMachine (decompiled)
  - id: cli
    resource: ksa-decomp:KSA.Kfs.Cli/Cli.cs
    title: kvm-cli (decompiled)
generated: { by: claude-code/claude-fable-5-2, at: 2026-11-25T21:52:36Z }
status: stable
ksa_build: "2026.11.25.5949"
---

# Purpose

`catalogue.json` describes the **processors** a kFS program can target — five catalogue "parts" in
four core classes — together with the allowed tick rates and per-part limits. `GameTables.BuildParts`
turns each row into a `PartDescription`, which is the "hardware" argument of the compiler, the
assembler, `ImageReader` and `KvmMachine`. The name "part" is kFS's own; **nothing in this build
links a catalogue part to a KSA vehicle part** (see [How a part is chosen](#how-a-part-is-chosen)).

# File header and scalars

| key | value | notes |
| --- | --- | --- |
| `version` | `1` | range-checked only |
| `allowed_tick_hz` | `[5, 10, 20, 25, 50, 100]` | each must be `> 0` and divide 100 |
| `default_tick_hz` | `50` | must be in `allowed_tick_hz`; used when a program sets no tick rate |
| `defaults` | see below | `CatalogueDefaults` |
| `parts` | 5 rows | `PartRow` |
| `bench_part` | `{ "name": "Bench", "based_on": "Flight Computer Mk2" }` | `BenchPartRow` |

`source`, verbatim (catalogue.json:3):[^catalogue-json]

> kFS_processors_spec.md §5.1, §5.2, §5.4 (moved 2026-11-02, D-052; D-040). Sizes in bytes (1 KB =
> 1,024), clocks in Hz: P-020, P-021

## `defaults`

| JSON key | value | used for |
| --- | --- | --- |
| `init_budget_seconds` | 2 | `InitBudgetCycles = init_budget_seconds × clock_hz` |
| `max_operand_cells` | 64 | `MaxOperandCells` of non-K8 parts |
| `max_return_entries` | 16 | `MaxReturnEntries` of non-K8 parts |
| `max_tasks` | 16 | `MaxTasks` of non-K8 parts |
| `k8_max_operand_cells` | 16 | `MaxOperandCells` of K8 parts |
| `k8_max_return_entries` | 8 | `MaxReturnEntries` of K8 parts |
| `k8_max_tasks` | 8 | `MaxTasks` of K8 parts |
| `double_bits` | 64 | `Not active in this build:` no code reads `CatalogueDefaults.DoubleBits` |

# Schema

All `public sealed record`s; every constructor parameter is required in the JSON (none has a
default).[^catalogue-records]

| record | parameters (JSON key = snake_case of the name) |
| --- | --- |
| `CatalogueFile` | `int Version`, `string Source`, `List<int> AllowedTickHz`, `int DefaultTickHz`, `CatalogueDefaults Defaults`, `List<PartRow> Parts`, `BenchPartRow BenchPart` |
| `CatalogueDefaults` | `int InitBudgetSeconds`, `int MaxOperandCells`, `int MaxReturnEntries`, `int MaxTasks`, `int K8MaxOperandCells`, `int K8MaxReturnEntries`, `int K8MaxTasks`, `int DoubleBits` |
| `PartRow` | `string Name`, `string ShortName`, `string Core`, `long ClockHz`, `string Analogue`, `long AnalogueClockHz`, `long RamBytes`, `long CodeBytes`, `long NvBytes`, `string Radiation`, `string Status` |
| `BenchPartRow` | `string Name`, `string BasedOn` |
| `PartDescription` (derived) | `string Name`, `int PartIndex`, `int CoreClass`, `CoreCostTable Costs`, `uint ClockHz`, `uint RamBytes`, `uint CodeBytes`, `uint NvBytes`, `uint InitBudgetCycles`, `ushort MaxOperandCells`, `byte MaxReturnEntries`, `byte MaxTasks`, `uint Features` |

`PartRow.Analogue`, `AnalogueClockHz`, `Radiation` and `Status` are descriptive only: no code reads
them (`Not active in this build:` in particular "Upset-prone"/"Hardened" has no effect, and the
`MEMORY_ERROR` trap is "optional").

# Parts

| index | `name` | `short_name` | `core` | `clock_hz` | `analogue` | `analogue_clock_hz` | `ram_bytes` | `code_bytes` | `nv_bytes` | `radiation` | `status` | line |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 0 | `Sequencer` | `Sequencer` | `K8` | 1000000 | ATmega328P | 16000000 | 2048 | 32768 | 1024 | Upset-prone | v1 | 17 |
| 1 | `Flight Computer Mk1` | `Mk1` | `K32` | 1000000 | RP2040 | 133000000 | 65536 | 262144 | 0 | Upset-prone | Optional (decision log O-005) | 18 |
| 2 | `Flight Computer Mk2` | `Mk2` | `K32F` | 1000000 | STM32F4 | 168000000 | 131072 | 524288 | 0 | Upset-prone | v1 | 19 |
| 3 | `Flight Computer Mk2-RH` | `Mk2-RH` | `K32F` | 1000000 | VA41620 | 100000000 | 65536 | 262144 | 0 | Hardened | v1 | 20 |
| 4 | `Flight Computer Mk3` | `Mk3` | `K32D` | 1000000 | RAD750 | 200000000 | 262144 | 1048576 | 0 | Hardened | v1 | 21 |

All five have the same `clock_hz` (1,000,000): the cores differ by **cost table**, not clock rate —
see [/vm/cost-model-and-budgets.md](/vm/cost-model-and-budgets.md).

## Derived `PartDescription`s

`BuildParts` (GameTables.cs:430–468):[^game-tables]

- `PartIndex` = the row's position in `parts` (0-based);
- `CoreClass` = index of `core` in `enums.json` `core_class` (`K8` 0, `K32` 1, `K32F` 2, `K32D` 3);[^enums-json]
- `Costs` = `CostTables[core]` (a shared reference to that core's `CoreCostTable`);
- `ClockHz`, `RamBytes`, `CodeBytes`, `NvBytes` = the row values cast to `uint`;
- `InitBudgetCycles` = `defaults.init_budget_seconds × clock_hz`;
- `MaxOperandCells`, `MaxReturnEntries`, `MaxTasks` = the `k8_…` defaults when `core == "K8"`, else
  the plain defaults;
- `Features` = `0u` for every part.

| `Name` | `PartIndex` | `CoreClass` | `ClockHz` | `RamBytes` | `CodeBytes` | `NvBytes` | `InitBudgetCycles` | `MaxOperandCells` | `MaxReturnEntries` | `MaxTasks` | `Features` |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `Sequencer` | 0 | 0 (`K8`) | 1000000 | 2048 | 32768 | 1024 | 2000000 | 16 | 8 | 8 | 0 |
| `Flight Computer Mk1` | 1 | 1 (`K32`) | 1000000 | 65536 | 262144 | 0 | 2000000 | 64 | 16 | 16 | 0 |
| `Flight Computer Mk2` | 2 | 2 (`K32F`) | 1000000 | 131072 | 524288 | 0 | 2000000 | 64 | 16 | 16 | 0 |
| `Flight Computer Mk2-RH` | 3 | 2 (`K32F`) | 1000000 | 65536 | 262144 | 0 | 2000000 | 64 | 16 | 16 | 0 |
| `Flight Computer Mk3` | 4 | 3 (`K32D`) | 1000000 | 262144 | 1048576 | 0 | 2000000 | 64 | 16 | 16 | 0 |
| `Bench` (`BenchPart`) | 2 | 2 (`K32F`) | 1000000 | 131072 | 524288 | 0 | 2000000 | 64 | 16 | 16 | 0 |

Validation (each a `GameTablesException`; full list in
[game-tables-loader](/tables/game-tables-loader.md#step-4--buildparts-lines-430468)): unknown `core`;
`ram_bytes` outside 2,048–1,048,576, `code_bytes` above 16,777,216, or `clock_hz` not in
1–2,147,483,647 ("outside kVM §2.2's ranges"); duplicate `name`; unknown `bench_part.based_on`.
`short_name` uniqueness is not checked.

`GameTables.Part(string name)` resolves the bench part's name first, then a part by `name` or by
`short_name` (exact, case-sensitive); otherwise `GameTablesException("unknown part '…'")`.

## What `PartIndex` means elsewhere

- It is written to the system block as `sys.tier` (KvmMachine.cs:480) and defined for kC as
  `__KFS_TIER__` (KcFrontEnd.cs:396).[^kvm-machine][^kc-front-end]
- `PartIndex == 0` **is** "the Sequencer" for feature gating: it cannot `claim ALLOCATION`, publish
  the `SequencerForbidden` messages, or include `<matn.h>` (diagnostic 520, "needs the Mk1 or above
  (processors §6)").[^kc-front-end][^assembler] The test is on the index, not on the name or core.

# Tick rates

- A program's `tick_hz` must be in `allowed_tick_hz`: kC diagnostic `KC 406`
  (`{n} Hz is not an allowed tick rate`, KcFrontEnd.cs:541–543), kASM `.tick_hz` (Assembler.cs:600),
  and `ImageReader` (`tick_hz {n} not allowed`, ImageReader.cs:765–767).[^kc-front-end][^assembler][^image-reader]
- Without a declaration the tick rate is `default_tick_hz` (50).
- The per-tick cycle budget stored in an image is `clock_hz / tick_hz`
  (ImageBuilder.cs:200; `ImageReader`: `budget_cycles is not clock_hz / tick_hz`).[^image-builder]
  With every part at 1,000,000 Hz:

| `tick_hz` | 5 | 10 | 20 | 25 | 50 | 100 |
| --- | --- | --- | --- | --- | --- | --- |
| budget (cycles per tick) | 200,000 | 100,000 | 50,000 | 40,000 | 20,000 | 10,000 |

The loader's message for a bad rate — `tick rate {n} doesn't divide 100 Hz (processors §5.1)` —
shows the tick grid is defined against a 100 Hz base; see
[/host/tick-grid-and-timing.md](/host/tick-grid-and-timing.md).

# The bench part

`BenchPart = basedOnPart with { Name = bench_part.name }` (GameTables.cs:452–456): a record copy of
`Flight Computer Mk2` whose only difference is the name `Bench`. It keeps `PartIndex` 2, core class
`K32F`, the same `CoreCostTable` object (so the same cost hash), 131,072 bytes of RAM and 524,288
bytes of code space. It is **not** in `Parts`.

"Bench" is the workbench's test stand: a `BenchMachine` is a `HostComputer` constructed with
`tables.BenchPart` and loaded against `tables.BenchLayout` (the fixed
[bench layout](/tables/io-layout-tables.md)), with no vehicle behind it.[^bench-machine] See
[/host/bench-machine-and-fake-vehicle.md](/host/bench-machine-and-fake-vehicle.md).

# How a part is chosen

| target | part used | where |
| --- | --- | --- |
| a vehicle's computer | **always `BenchPart`** | `KfsVehicleHost` constructor: `new VehicleComputer(tables, tables.BenchPart, vehicle.Id)` (KfsVehicleHost.cs:110)[^kfs-vehicle-host] |
| building for the vehicle target | `BenchPart` | `Workbench.LoadTarget` sets `part = Tables.BenchPart` and only replaces the layout with the vehicle's (Workbench.cs:837–853)[^workbench] |
| the bench machine, default | `BenchPart` | `new BenchMachine(tables)` (Workbench.cs:370) |
| the bench machine, `kfs load <file> --part <name>` | `Tables.Part(name)` | a new `BenchMachine(Tables, part)` is created when the part differs (Workbench.cs:769–801, 934) |
| `kfs load <file> --part <name> --vehicle` | source built for the named part, loaded on the vehicle's `BenchPart` computer | Workbench.cs:783–795 — see below |
| `kfs preview <part>` | any `Parts` entry (name compared case-insensitively, spaces ignored) | compile only, nothing loaded (Workbench.cs:1915–1950) |
| `kvm-cli … --part NAME` | `Part(NAME)`, default the bench part | Cli.cs:275; no caller in the game[^cli] |

There is no code that inspects a vehicle's parts or modules to pick a processor: every vehicle gets
one `VehicleComputer` whose part is `Bench` (= Mk2, `K32F`, 1 MHz), created on demand by
`KfsVehicleHost.For(vehicle)`. `Not active in this build:` the Sequencer / Mk1 / Mk2-RH / Mk3 rows
can only be exercised on the bench machine and in `kfs preview`.

`ImageReader` refuses an image whose recorded part properties differ from the machine's part — core
class, cost-table hash, clock, RAM, code and NV sizes, return entries, operand cells, init budget
and features — with `the image was built for another part or cost table; rebuild from source`
(ImageReader.cs:773–775). The part **name** and `PartIndex` are not recorded, and `Bench` equals
`Flight Computer Mk2` in every compared property, so an image built for `Mk2` loads on a vehicle;
one built for any other part does not.[^image-reader]

# Modding notes

- The catalogue is validated and consumed like the other tables (see
  [game-tables-loader](/tables/game-tables-loader.md)); adding a part row with an existing core class
  is data-only and immediately selectable with `kfs load --part` / `kfs preview`. A new **core
  class** is not data-only (`CostRow`/`ServiceClassRow` have fixed `K8`/`K32`/`K32F`/`K32D`
  properties).
- For a purrtty-style extension the relevant fact is the budget a vehicle program has: on every
  vehicle, `K32F` costs at 1,000,000 cycles per second, 131,072 bytes of RAM, at most 16 tasks, and
  each service call costs `syscall` 10 + class cycles (`Light` 10 / `Medium` 30 / `Heavy` 100) on
  that core ([services-table](/tables/services-table.md)).
- `GameTables.BenchPart` has a private setter; replacing which part vehicles use would mean either
  changing `bench_part.based_on` in an augmented catalogue (affects the bench too) or patching the
  `KfsVehicleHost` constructor.
- `Features` is a `uint` that is always 0 and is compared by `ImageReader`; `Unverified:` it looks
  like a reserved feature bit-set for future parts.

[^catalogue-json]: catalogue.json:2–15 (header, tick rates, defaults), 17–21 (parts), 23 (bench part).
[^catalogue-records]: CatalogueFile.cs:5; CatalogueDefaults.cs:3; PartRow.cs:3; BenchPartRow.cs:3; PartDescription.cs:3.
[^game-tables]: GameTables.cs:61–65, 73, 430–468 (`BuildParts`), 470–473 (`Part`).
[^enums-json]: enums.json:5.
[^kfs-vehicle-host]: KfsVehicleHost.cs:105–116, 139–142.
[^workbench]: Workbench.cs:370, 739–806 (`Load`), 837–853 (`LoadTarget`), 934, 1915–1950 (`Preview`).
[^bench-machine]: BenchMachine.cs:31–43.
[^image-reader]: ImageReader.cs:765–775.
[^image-builder]: ImageBuilder.cs:188–200.
[^kc-front-end]: KcFrontEnd.cs:70, 81, 278–280, 396, 541–552.
[^assembler]: Assembler.cs:566–570, 600, 908–910, 1349.
[^kvm-machine]: KvmMachine.cs:480–481.
[^cli]: Cli.cs:274–276.
