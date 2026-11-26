---
type: Subsystem Reference
title: GameTables loader — loading, validation, derived data, consumers
description: How KSA.Kfs.Vm.Tables.GameTables reads the 14 embedded JSON tables, what it validates and derives, who consumes the result, and how a mod could inject extra rows.
resource: ksa-decomp:KSA.Kfs.Vm.Tables/GameTables.cs
tags: [kfs, tables, loader, validation, extension, harmony]
sources:
  - id: game-tables
    resource: ksa-decomp:KSA.Kfs.Vm.Tables/GameTables.cs
    title: GameTables (decompiled)
  - id: data-json-context
    resource: ksa-decomp:KSA.Kfs.Vm.Tables/DataJsonContext.cs
    title: DataJsonContext — System.Text.Json source-generated context (decompiled)
  - id: catalogue-enums
    resource: ksa-decomp:KSA.Kfs.Vm.Tables/CatalogueEnums.cs
    title: CatalogueEnums (decompiled)
  - id: message-layout
    resource: ksa-decomp:KSA.Kfs.Vm.Tables/MessageLayout.cs
    title: MessageLayout (decompiled)
  - id: table-records
    resource: ksa-decomp:KSA.Kfs.Vm.Tables/ServiceInfo.cs
    title: Row and info records of the tables namespace (ServiceInfo, ServiceRow, CoreCostTable, PartDescription, IoLayout, …)
  - id: kfs-integration
    resource: ksa-decomp:KSA.Kfs/KfsIntegration.cs
    title: KfsIntegration (decompiled)
  - id: kfs-vehicle-host
    resource: ksa-decomp:KSA.Kfs/KfsVehicleHost.cs
    title: KfsVehicleHost (decompiled)
  - id: kfs-services
    resource: ksa-decomp:KSA.Kfs/KfsServices.cs
    title: KfsServices (decompiled)
  - id: kfs-reference-window
    resource: ksa-decomp:KSA.Kfs/KfsReferenceWindow.cs
    title: KfsReferenceWindow (decompiled)
  - id: kfs-editor-window
    resource: ksa-decomp:KSA.Kfs/KfsEditorWindow.cs
    title: KfsEditorWindow (decompiled)
  - id: kfs-console-commands
    resource: ksa-decomp:KSA.Kfs/KfsConsoleCommands.cs
    title: KfsConsoleCommands (decompiled)
  - id: kfs-debug-window
    resource: ksa-decomp:KSA.Kfs/KfsDebugWindow.cs
    title: KfsDebugWindow (decompiled)
  - id: program
    resource: ksa-decomp:KSA/Program.cs
    title: KSA.Program (decompiled)
  - id: workbench
    resource: ksa-decomp:KSA.Kfs.Host.Workbench/Workbench.cs
    title: Workbench (decompiled)
  - id: cli
    resource: ksa-decomp:KSA.Kfs.Cli/Cli.cs
    title: kvm-cli entry point (decompiled)
  - id: kc-cli
    resource: ksa-decomp:KSA.Kfs.Cli/KcCli.cs
    title: KcCli (decompiled)
  - id: image-reader
    resource: ksa-decomp:KSA.Kfs.Vm.Image/ImageReader.cs
    title: ImageReader (decompiled)
  - id: image-builder
    resource: ksa-decomp:KSA.Kfs.Vm.Image/ImageBuilder.cs
    title: ImageBuilder (decompiled)
  - id: cost-model
    resource: ksa-decomp:KSA.Kfs.Vm.Image/CostModel.cs
    title: CostModel and BusSteps (decompiled)
  - id: kvm-machine
    resource: ksa-decomp:KSA.Kfs.Vm/KvmMachine.cs
    title: KvmMachine (decompiled)
  - id: arg-domains
    resource: ksa-decomp:KSA.Kfs.Vm/ArgDomains.cs
    title: ArgDomains (decompiled)
  - id: vm-mirrors
    resource: ksa-decomp:KSA.Kfs.Vm/ServiceIds.cs
    title: Hard-coded mirrors in KSA.Kfs.Vm (ServiceIds, Op, TrapCode, SysBlock, Direction, …)
  - id: soft-routines
    resource: ksa-decomp:KSA.Kfs.Vm/SoftRoutines.cs
    title: SoftRoutines and FpSelfTest (decompiled)
  - id: host-computer
    resource: ksa-decomp:KSA.Kfs.Host/HostComputer.cs
    title: HostComputer (decompiled)
  - id: vehicle-computer
    resource: ksa-decomp:KSA.Kfs.Host/VehicleComputer.cs
    title: VehicleComputer (decompiled)
  - id: host-messages
    resource: ksa-decomp:KSA.Kfs.Host/HostMessages.cs
    title: HostMessages, HostLeafMap, BusTracker, BenchMachine, FakeVehicle (decompiled)
  - id: host-views
    resource: ksa-decomp:KSA.Kfs.Host.Views/BusMonitorView.cs
    title: Host views — BusMonitorView, DiagnosticManual, DisassemblyView, Listing (decompiled)
  - id: kc-parser
    resource: ksa-decomp:KSA.Kfs.KC/Parser.cs
    title: kC Parser (decompiled)
  - id: kc-front-end
    resource: ksa-decomp:KSA.Kfs.KC/KcFrontEnd.cs
    title: KcFrontEnd, Lowering, Sema (decompiled)
  - id: kc-headers
    resource: ksa-decomp:KSA.Kfs.KC/GeneratedHeaders.cs
    title: GeneratedHeaders, VehicleHeader, SystemHeaders (decompiled)
  - id: kasm
    resource: ksa-decomp:KSA.Kfs.KAsm/BuiltinIncludes.cs
    title: kASM — BuiltinIncludes, Assembler, Disassembler (decompiled)
  - id: compiler
    resource: ksa-decomp:KSA.Kfs.Compiler/ProgramAnalysis.cs
    title: Compiler — ProgramAnalysis, BusAnalysis, IrValidator, IrBuilder, Emitter, DiagnosticRegistry (decompiled)
  - id: offline-harness
    resource: /modding/offline-harness.md
    title: Offline harness experiments E1–E12
generated: { by: claude-code/claude-fable-5-2, at: 2026-11-25T21:52:36Z }
status: stable
ksa_build: "2026.11.25.5949"
---

# Purpose

`KSA.Kfs.Vm.Tables.GameTables` is the single in-memory form of kFS's "game tables": fourteen JSON
files embedded as manifest resources in `KSA.dll`. Every other kFS layer — the kC compiler, the kASM
assembler, the kVM, the host and the in-game tooling — receives a `GameTables` instance and reads
opcodes, costs, services, messages, events, enums, the processor catalogue and the bench I/O layout
from it. This document describes the loader exhaustively and then evaluates it as an **extension
point** (see [Modding notes](#modding-notes)).

Content of the individual tables is documented separately:
[services](/tables/services-table.md), [messages](/tables/messages-table.md),
[events](/tables/events-table.md), [enums](/tables/enums-table.md),
[catalogue](/tables/catalogue-processors.md), [I/O layout](/tables/io-layout-tables.md),
[diagnostics](/tables/diagnostics-table.md), and the
[row schemas of the VM tables](/tables/opcode-cost-trap-tables-schema.md).

# The type at a glance

`public sealed class GameTables` has no explicit constructor, so the compiler-generated public
parameterless constructor exists; but all fifteen raw-data properties are `required … { get; init; }`,
so an object initializer must supply every one of them, and the derived data is only filled by the
**private** `Build()` method.[^game-tables]

| member | declaration | notes |
| --- | --- | --- |
| `FileNames` | `public static readonly string[]` (14 entries) | load order; not referenced anywhere else in the decompiled game |
| `RESOURCE_PREFIX` | `public const string = "KSA.Kfs.Vm.Tables.Data."` | inlined into `LoadEmbedded` |
| `Origin` | `public required string { get; init; }` | `folder` path or `"embedded:" + assembly name` |
| `OpcodesData` | `public required OpcodesFile { get; init; }` | raw opcodes.json |
| `TrapsData` | `public required TrapsFile { get; init; }` | raw traps.json |
| `SysBlockData` | `public required SysBlockFile { get; init; }` | raw sysblock.json |
| `EnumsData` | `public required EnumsFile { get; init; }` | raw enums.json |
| `CostsData` | `public required CostsFile { get; init; }` | raw costs.json |
| `RoutinesData` | `public required RoutinesFile { get; init; }` | raw routines.json |
| `CatalogueData` | `public required CatalogueFile { get; init; }` | raw catalogue.json |
| `MessagesData` | `public required MessagesFile { get; init; }` | raw messages.json |
| `ServicesData` | `public required ServicesFile { get; init; }` | raw services.json |
| `EventsData` | `public required EventsFile { get; init; }` | raw events.json |
| `DiagnosticsData` | `public required DiagnosticsFile { get; init; }` | raw diagnostics.json |
| `FpCheckData` | `public required FpCheckFile { get; init; }` | raw fpcheck.json |
| `EdgeCasesData` | `public required EdgeCasesFile { get; init; }` | raw edgecases.json |
| `BenchLayoutData` | `public required LayoutFile { get; init; }` | raw bench_layout.json |
| `Opcodes` | `public OpcodeInfo?[] { get; private set; }` | 256 slots indexed by opcode byte |
| `OpcodesByMnemonic` | `public Dictionary<string, OpcodeInfo> { get; private set; }` | 146 entries |
| `Categories` | `public List<string> { get; private set; }` | 42 cost categories, costs.json order |
| `CostTables` | `public Dictionary<string, CoreCostTable> { get; private set; }` | one per core class |
| `Parts` | `public List<PartDescription> { get; private set; }` | 5 catalogue parts |
| `BenchPart` | `public PartDescription { get; private set; }` | copy of the `based_on` part, renamed |
| `BenchLayout` | `public IoLayout { get; private set; }` | validated bench_layout.json |
| `Services` | `public Dictionary<ushort, ServiceInfo> { get; private set; }` | 20 entries; reserved rows excluded |
| `ServiceTableVersion` | `public ushort => (ushort)ServicesData.Version` | computed on every read |
| `MessageCatalogueVersion` | `public ushort => (ushort)MessagesData.Version` | computed on every read |
| `AllowedTickHz` | `public IReadOnlyList<int> => CatalogueData.AllowedTickHz` | the same `List<int>` instance as the raw data |

Nothing in the class is frozen or wrapped: the derived collections are ordinary mutable
`Dictionary`/`List`/array objects behind public getters, and the raw `…Data` records are C# positional
`sealed record`s whose properties are init-only but whose `List<T>` / `Dictionary<,>` / array values
are mutable in place.[^game-tables][^table-records] No `FrozenDictionary`, `ReadOnlyCollection` or
`ImmutableArray` is used anywhere in the namespace.

# Entry points

Three public static factories exist (GameTables.cs:75–126):[^game-tables]

```csharp
public static GameTables Load(string folder);                          // files from a directory
public static GameTables LoadEmbedded();                               // manifest resources of KSA.dll
public static GameTables Load(Func<string, Stream?> open, string origin);  // the real loader
```

- `Load(string folder)` opens `Path.Combine(folder, file)` for each file name, yielding `null` when
  the file does not exist, with `origin = folder`.
- `LoadEmbedded()` calls `typeof(GameTables).Assembly.GetManifestResourceStream("KSA.Kfs.Vm.Tables.Data." + file)`
  with `origin = "embedded:" + assembly.GetName().Name`. The types and the fourteen resources are in
  `KSA.dll` (the resource name `KSA.Kfs.Vm.Tables.Data.services.json` and the type name occur in that
  file and in no other shipped DLL), so the origin string is `embedded:KSA`.
- `Load(Func<string, Stream?> open, string origin)` builds the instance with one object initializer
  whose member order fixes the read order, then calls `Build()` and returns.

The local function `Read<T>(string file, JsonTypeInfo<T> info)` wraps each file:

| condition | exception |
| --- | --- |
| `open(file)` returns `null` | `GameTablesException`: `{file}: missing from '{origin}'` |
| the JSON deserialises to `null` | `GameTablesException`: `{file}: empty` |
| `JsonSerializer.Deserialize` throws `JsonException` | `GameTablesException`: `{file}: {ex.Message}` |

The stream is disposed after each read (`using Stream utf8Json = …`). `GameTablesException` is a
`public sealed class` deriving from `Exception` with a single `(string message)` constructor.

## Files and read order

`FileNames` and the initializer agree on this order (GameTables.cs:15–19, 92–105):[^game-tables]

| # | file | property | record type | JSON `version` in this build |
| --- | --- | --- | --- | --- |
| 1 | `opcodes.json` | `OpcodesData` | `OpcodesFile` | 1 |
| 2 | `traps.json` | `TrapsData` | `TrapsFile` | 1 |
| 3 | `sysblock.json` | `SysBlockData` | `SysBlockFile` | 1 |
| 4 | `enums.json` | `EnumsData` | `EnumsFile` | 1 |
| 5 | `costs.json` | `CostsData` | `CostsFile` | 1 |
| 6 | `routines.json` | `RoutinesData` | `RoutinesFile` | 1 |
| 7 | `catalogue.json` | `CatalogueData` | `CatalogueFile` | 1 |
| 8 | `messages.json` | `MessagesData` | `MessagesFile` | 4 |
| 9 | `services.json` | `ServicesData` | `ServicesFile` | 1 |
| 10 | `events.json` | `EventsData` | `EventsFile` | 1 |
| 11 | `diagnostics.json` | `DiagnosticsData` | `DiagnosticsFile` | 1 |
| 12 | `fpcheck.json` | `FpCheckData` | `FpCheckFile` | 1 |
| 13 | `edgecases.json` | `EdgeCasesData` | `EdgeCasesFile` | 1 |
| 14 | `bench_layout.json` | `BenchLayoutData` | `LayoutFile` | 1 |

All fourteen files are read before any cross-check runs. A missing file aborts the whole load.

## JSON binding rules (`DataJsonContext`)

`DataJsonContext` is System.Text.Json **source-generated** glue: a `[GeneratedCode("System.Text.Json.SourceGeneration", "10.0.14.32716")]`
`JsonSerializerContext` with `[JsonSerializable]` attributes for the fourteen file records, and
10,384 lines of generated `Create_*`, `*PropInit`, `*CtorParamInit` and `*SerializeHandler` methods.
It contains no hand-written logic.[^data-json-context] Its options decide what an (augmented) JSON
stream may contain (DataJsonContext.cs:11, 163–171):

| option | value | consequence |
| --- | --- | --- |
| `PropertyNamingPolicy` | `SnakeCaseLower` | C# `ArgTypes` ↔ JSON `arg_types`, `K8MaxTasks` ↔ `k8_max_tasks` |
| `UnmappedMemberHandling` | `Disallow` | an unknown JSON property is a `JsonException` → `GameTablesException` |
| `RespectRequiredConstructorParameters` | `true` | a record parameter without a default value must be present |
| `RespectNullableAnnotations` | `true` | `null` for a non-nullable parameter is an error |
| `ReadCommentHandling` | `Disallow` | no `//` or `/* */` comments |
| `AllowTrailingCommas` | `false` | no trailing commas |

The only explicit `[JsonPropertyName]` overrides are `K8`, `K32`, `K32F`, `K32D` on `CostRow` and
`ServiceClassRow` (upper case, exempt from the snake-case policy). `DataJsonContext.Default` is a
public static singleton; `GameTables.LayoutJson` uses the same context to *serialise* a `LayoutFile`.

# `Build()` — validation and derivation, in order

`Build()` is private and runs once per instance (GameTables.cs:171–206).[^game-tables] Every failure
below is a `GameTablesException` unless marked otherwise.

## Step 0 — version range

For each of the fourteen files: `version < 1 || version > 65535` →
`{name}.json: version {n} out of range` (`name` without extension, e.g. `services`).

## Step 1 — `BuildCategories` (lines 208–232)

| check | message |
| --- | --- |
| `Categories = CostsData.Costs.Select(r => r.Category)`; duplicates | `costs.json: a category is listed twice` |
| `CostsData.Cores` must be sequence-equal to `EnumsData.Enums["core_class"]` | `costs.json: 'cores' differs from enums.json core_class` |
| `costs.table_rows` must list every category exactly once | `costs.json table_rows: must list every category once (missing: …; unknown: …)` |
| `costs.category_rows` likewise | `costs.json category_rows: must list every category once (missing: …; unknown: …)` |

`EnumsData.Enums["core_class"]` is a plain dictionary indexer: a missing `core_class` key throws
`KeyNotFoundException`, not `GameTablesException`.

## Step 2 — `BuildOpcodes` (lines 234–270)

`Opcodes = new OpcodeInfo[256]`; then for each `OpcodeRow` and each `[mnemonic, hex]` pair in `ops`:

| check | message |
| --- | --- |
| `row.Category` not in `Categories` | `opcodes.json: unknown category '{c}'` |
| an `imm` type not one of `i8 u8 i16 u16 u24 u32 u64 rel16` | `opcodes.json: unknown immediate type '{s}'` |
| `summary` null, wrong length, or a blank entry | `opcodes.json: the row for {mnemonics} needs one summary per op` |
| op entry not 2 strings or the code not a hex byte | `opcodes.json: bad op entry [{…}]` |
| opcode byte already assigned | `opcodes.json: opcode 0x{XX} assigned twice` |
| mnemonic already present | `opcodes.json: mnemonic '{m}' listed twice` |

Each op becomes `new OpcodeInfo(code, mnemonic, ImmType[] imm, string[] immNames, size, categoryIndex, category, pops, pushes, section, summary)`
with `size = 1 + Σ ImmSize(imm)` and is stored in both `Opcodes[code]` and
`OpcodesByMnemonic[mnemonic]`.

## Step 3 — `BuildCostTables` (lines 272–390)

Local dictionaries: families by `id`, and routine category → family. `ToDictionary` throws
`ArgumentException` (not `GameTablesException`) on a duplicate family id, duplicate routine category
or duplicate service class.

| check | message |
| --- | --- |
| routine category not in `Categories` | `routines.json: unknown category '{c}'` |
| routine family not declared | `routines.json: unknown family '{f}'` |
| `routines.table_rows` must list every routine category once | `routines.json table_rows: must list every category once (…)` |
| per core × category: `block`/`enter` must have `base`, others must not | `costs.json: {cat} on {core}: variable categories (block, enter) take base + per_word, others cycles` |
| variable category without `per_word` or with `cycles` | `costs.json: {cat} on {core}: needs base and per_word only` |
| fixed category without `cycles` or with `per_word` | `costs.json: {cat} on {core}: needs cycles only` |
| negative base or per-word | `costs.json: {cat} on {core}: negative cost` |
| base cost `< 1` | `costs.json: {cat} on {core}: costs no cycles; every instruction costs at least 1` |
| `soft` category without a routine row | `costs.json: {cat} is soft on {core} but routines.json names no routine for it` |
| `soft` category whose family has `bytes[core] == null` | `costs.json: {cat} is soft on {core} but family '{f}' has no footprint there` |
| override opcode not a known mnemonic | `costs.json: override names unknown opcode '{op}'` |
| soft override whose opcode category has no family | `costs.json: soft override of '{op}' has no family` |
| override `cycles < 1` | `costs.json: override of '{op}' on {core} costs {n} cycles; every instruction costs at least 1` |
| override `core` not in `cores` | `costs.json: override for unknown core '{core}'` |

`CostRow.For(core)` and `ServiceClassRow.For(core)` are `switch` expressions over the four literal
names `K8`, `K32`, `K32F`, `K32D`; any other core name throws `ArgumentException("unknown core class '…'")`.
The set of core classes is therefore fixed by the record *types*, not only by the data.[^table-records]

For each core a `CoreCostTable` is created (all members `required … { get; init; }`):

| member | type | content |
| --- | --- | --- |
| `Core` / `CoreIndex` | `string` / `int` | core name and its index in `cores` |
| `Cycles` | `int[]` | per category: `cycles`, or `base` for `block`/`enter` |
| `PerWord` | `int[]` | per category: `per_word` for `block`/`enter`, else 0 |
| `Soft` | `bool[]` | per category |
| `Family` | `string?[]` | per category: soft-routine family id when soft |
| `OpcodeOverride` | `(int Cycles, bool Soft)?[]` (256) | per opcode byte, from `overrides` |
| `FamilyBytes` | `Dictionary<string, int>` | families with a footprint on this core |
| `ServiceClassCycles` | `Dictionary<string, int>` | `Light`/`Medium`/`Heavy` → cycles on this core |
| `LogCyclesPerValue` | `int` | `costs.log_cycles_per_value` |
| `Hash` | `uint` | `Crc32.Compute(UTF-8(CanonicalCostText(core)))` |

## Step 4 — `BuildParts` (lines 430–468)

| check | message |
| --- | --- |
| part `core` not in `enums.core_class` | `catalogue.json: part '{name}' has unknown core '{core}'` |
| `ram_bytes ∉ [2048, 1048576]`, `code_bytes > 16777216`, `clock_hz ≤ 0` or `> int.MaxValue` | `catalogue.json: part '{name}' is outside kVM §2.2's ranges` |
| duplicate part `name` | `catalogue.json: duplicate part name` |
| `bench_part.based_on` not a part name | `catalogue.json: bench part based on unknown part '{name}'` |
| a tick rate `≤ 0` or not dividing 100 | `catalogue.json: tick rate {n} doesn't divide 100 Hz (processors §5.1)` |
| `default_tick_hz` not in `allowed_tick_hz` | `catalogue.json: default tick rate not allowed` |

See [catalogue-processors](/tables/catalogue-processors.md) for how each `PartDescription` is derived.

## Step 5 — `BuildServices` (lines 538–582)

For each `ServiceRow`, in file order:

| check | message |
| --- | --- |
| `id < 0 || id > 65535` | `services.json: id {id} out of range` |
| `reserved` row with `kind`, `arg_types` or `cost_class` set | `services.json: reserved id {id} has machine fields` |
| (reserved rows are then skipped — they are **not** added to `Services`) | — |
| `kind` not `query` / `request` / `request+result`; or `arg_types`, `result_types`, `cost_class`, `symbol` null; or `cost_class` not a `costs.service_classes` class; or `summary` blank | `services.json: service '{name}' is incomplete` |
| `arg_domains` or `host_checks` null, or `arg_domains.Count != arg_types.Count` | `services.json: service '{name}' needs one arg_domains entry per arg_types entry, and host_checks` |
| a domain that does not fit its cell type (table in [services-table](/tables/services-table.md#arg_domains-vocabulary)) | `services.json: service '{name}' argument {i}: domain '{d}' doesn't fit type '{t}' (kVM §4.8)` |
| the `kc` column's argument names cannot be grouped onto `arg_types` | `services.json: service '{name}': the kC column's {n} arguments don't group its {m} arg_types entries (D-118 (4))` |
| an `arg_types`/`result_types` entry not `i32 u32 f32 ptr cell i64 f64` | `services.json: unknown cell type '{t}'` |
| id already in `Services` | `services.json: id {id} listed twice` |

Validating an `enum:roll_mode` domain calls `CatalogueEnums.All(this)`, which can itself throw
`messages.json: no enumerated field '{field}' (FI §8)` or `enums.json: no enumeration '{name}' (FI §9.2)`
(CatalogueEnums.cs:43, 55).[^catalogue-enums] This is why `messages.json` and `enums.json` are
implicitly required to keep the `mode`, `roll_mode` and `state` fields and the `direction`,
`attitude_profile`, `apsis` and `burn_end_cause` enums.

Each accepted row becomes a `ServiceInfo` (see [services-table](/tables/services-table.md#serviceinfo)).

Not checked: uniqueness of `name`, `symbol` or the `kc` function name; that `kind` matches whether
`result_types` is empty; that an id is outside any "reserved" range.

## Step 6 — `BuildLayout` (line 734) → `LayoutFrom(BenchLayoutData, "bench_layout.json")`

See [io-layout-tables](/tables/io-layout-tables.md) for the six checks and the fingerprint algorithm.

## Step 7 — `CheckTraps` (lines 853–862)

`TrapsData.Traps[i].Code` must equal `i + 1`:
`traps.json: codes must run 1, 2, … in table order (P-023); '{name}' is {code}`.

## Step 8 — `CheckMessages` (lines 864–874)

Each message `id` must be unique, start with `0x` and be exactly six characters:
`messages.json: bad or duplicate id '{id}'`. Nothing else about `messages.json` is checked at load.

## What `Build()` does not check

- `events.json`, `sysblock.json`, `diagnostics.json`, `edgecases.json`: only the version range.
- `fpcheck.json`: only the version range; its vectors run when a `KvmMachine` is constructed
  (`FpSelfTest.Run`, KvmMachine.cs:1856), where an unknown `op` throws
  `GameTablesException("fpcheck.json: unknown op '…'")` and a mismatch throws
  `UnsupportedHostException`.[^kvm-machine][^soft-routines]
- Message field `type` strings: validated lazily by `MessageLayout.TypeShape`, which throws
  `InvalidDataException("messages.json: unknown field type '…'")` the first time a layout is
  built.[^message-layout] The four hex digits of a message id are not parsed at load either
  (`Convert.ToUInt16(…, 16)` runs later).
- `messages.header_bytes` is never compared with `MessageLayout.HEADER_BYTES` (a `const int 16`).
- `diagnostics.json` duplicate numbers are only rejected by `DiagnosticRegistry`, which nothing in
  this build constructs.[^compiler]

# Versions, hashes and fingerprints

| value | computed from | recorded / compared where |
| --- | --- | --- |
| `ServiceTableVersion` (u16) | `services.json` `version` (1) | written at image offset 32 (`ImageBuilder`); `ImageReader` fails with `service-table`: `service_table_version differs from the game's` when it differs[^image-builder][^image-reader] |
| `MessageCatalogueVersion` (u16) | `messages.json` `version` (4) | written at image offset 34; `ImageReader` fails with `message-catalogue`: `message_catalogue_version differs from the game's`; also printed in the `vehicle.h` / `vehicle.kinc` banner[^kc-headers] |
| `CoreCostTable.Hash` (u32) | CRC-32 of `CanonicalCostText(core)` | written at image offset 24; `ImageReader` fails with `part`: `the image was built for another part or cost table; rebuild from source` |
| `IoLayout.Fingerprint` (u32) | CRC-32 of the canonical layout text | written at image offset 28; see [io-layout-tables](/tables/io-layout-tables.md) |
| every other file's `version` | — | only range-checked; not recorded anywhere |

`CanonicalCostText(core)` (public, lines 392–428) emits one line per cost row —
`{category}={cycles}` or `{category}={base}+{per_word}`, with `,S` appended when soft — then one line
per override opcode of that core, `opcode:{mnemonic}={cycles}` plus `,S` when soft; every line ends in
`\n`. `Crc32.Compute` is the reflected CRC-32 with polynomial `0xEDB88320`, initial value and final
XOR `0xFFFFFFFF` (identical to zlib's `crc32`).

| core | `CoreIndex` | `Hash` (CRC-32 of `CanonicalCostText`) | canonical text length (bytes) |
| --- | --- | --- | --- |
| `K8` | 0 | `0xDE3FB35D` | 483 |
| `K32` | 1 | `0x6D0B4259` | 524 |
| `K32F` | 2 | `0x53AE2D6D` | 442 |
| `K32D` | 3 | `0x54E8568C` | 431 |

The four hashes above were recomputed from `costs.json` with an independent CRC-32 implementation
while writing this document; they are not read from the game. For illustration, the canonical text of
`K32` (the only core with an override):

```text
stack=1
const32=2
stack64=2
mem8=2
mem16=2
mem32=2
mem64=4
addr=2
bounds=2
block=4+2
branch=3
call=3
callf=4
ret=3
enter=6+1
i32.alu=1
i32.shift=1
i32.mul=1
i32.div=60,S
i64.alu=2
i64.cmp=2
i64.shift=4
i64.mul=20,S
i64.div=300,S
f32.add=50,S
f32.mul=50,S
f32.div=150,S
f32.sqrt=300,S
f32.cmp=20,S
f32.sign=1
f64.add=80,S
f64.mul=120,S
f64.div=400,S
f64.sqrt=600,S
f64.cmp=30,S
f64.sign=2
cvt.i32f32=30,S
cvt.f32f64=30,S
cvt.i32i64=2
cvt.i64f=60,S
yield=4
syscall=10
opcode:DIV=10
opcode:DIVU=10
opcode:REM=10
opcode:REMU=10
```

**No hash covers `services.json`, `messages.json`, `events.json` or `enums.json`.** The only runtime
comparison involving those tables is the two 16-bit `version` numbers above, and both sides of that
comparison come from the same `GameTables` instance unless an image was built with a different one.
`KSA.Kfs.Vm.ServiceIds.TABLE_VERSION` is a hard-coded `const ushort 1` that nothing reads.[^vm-mirrors]

# Public helpers on `GameTables`

| member | behaviour |
| --- | --- |
| `static int ImmSize(ImmType)` | 1 for `I8`/`U8`, 2 for `I16`/`U16`/`Rel16`, 3 for `U24`, 4 for `U32`, 8 for `U64` |
| `string CanonicalCostText(string core)` | see above |
| `PartDescription Part(string name)` | bench part by its name, else a part by `name` or `short_name`; else `GameTablesException("unknown part '…'")` |
| `static int CellsOf(string type)` | 1 for `i32 u32 f32 ptr cell`, 2 for `i64 f64`; else throws |
| `static (float Lo, float Hi)? FiniteRange(string d)` | parses `finite:[lo,hi]`; `null` unless two invariant-culture floats with `lo <= hi` and a closing `]` |
| `(string Name, int First, int Last)? EnumDomain(string d)` | parses `enum:name` or `enum:name[a..b]` (instance method: needs `EnumsData`) |
| `static int FieldSize(string type, string what)` | I/O field sizes; see [io-layout-tables](/tables/io-layout-tables.md) |
| `static IoLayout LayoutFrom(LayoutFile, string what)` | validate + convert a layout file |
| `static IoLayout LoadLayout(string path)` | read a layout JSON file from disk, then `LayoutFrom` |
| `static LayoutFile ToLayoutFile(IoLayout, string source)` | inverse, recomputing the fingerprint |
| `static string LayoutJson(LayoutFile)` | serialise with `DataJsonContext` + `"\n"` |
| `static string CanonicalLayoutText(LayoutFile)` / `static uint ComputeLayoutFingerprint(LayoutFile)` | fingerprint inputs |

# Who creates a `GameTables`

| site | call | reachable in the game? |
| --- | --- | --- |
| `KfsIntegration.Tables` (KfsIntegration.cs:85–89) | `LazyInitializer.EnsureInitialized(ref _tables, GameTables.LoadEmbedded)` over `private static GameTables? _tables` | yes — the only instance the game makes[^kfs-integration] |
| `Cli.Run` (Cli.cs:274) | `options.Game == null ? GameTables.LoadEmbedded() : GameTables.Load(options.Game)` — `--game DIR` loads the 14 files from a folder | no caller of `Cli.Run` exists in the decompiled game[^cli] |
| `KcCli.Run` (KcCli.cs:127) | same pattern | no caller in the decompiled game[^kc-cli] |

So in a running game there is exactly **one** `GameTables` instance, created lazily; the `Workbench`,
every `KfsVehicleHost`, every `HostComputer`/`KvmMachine` and every compile share it by reference.
The CLI classes prove the design supports several instances side by side (and a folder of loose JSON
files), and several caches are keyed by instance identity for that reason (below).

## When `Tables` is first touched

`KfsIntegration.RegisterCommands(Program)` — called from `Program` at Program.cs:978 — does **not**
touch `Tables`: it sets `KfsVehicleHost.FrameBudgetMs`, registers the `kfs` console command and
registers two static ImGui windows (KfsIntegration.cs:146–156).[^kfs-integration][^program] The first
access happens through one of:

- the `KfsIntegration.Workbench` getter (`new Workbench(Tables, …)`, line 99), reached by every `kfs`
  console verb except `help`, `on`, `off`, `kill` and `budget`; by `ShowEditor`/`ToggleEditor`; and by
  the kFS Debug window once the prototype flag is on (`DrawPerf` uses the `Workbench`
  property);[^kfs-console-commands][^kfs-debug-window]
- `KfsVehicleHost.For(vehicle)` (KfsVehicleHost.cs:141), whose only caller is the Workbench's
  `ActiveVehicle` delegate (KfsIntegration.cs:104).[^kfs-vehicle-host]

Nothing in startup, save loading or the physics loop reads `Tables` before a player (or a mod) uses
kFS: `KfsDispatch.TryComputeControl` only looks at `vehicle.KfsHost`, which is `null` until
`KfsVehicleHost.For` runs. All of kFS is additionally gated by `KfsPrototype.Enabled`
(`KFS_PROTOTYPE=1` or `kfs on`); see [/integration/feature-flag.md](/integration/feature-flag.md).

# Consumers of `GameTables`

Every reference to the type outside `KSA.Kfs.Vm.Tables`, grouped by layer. "Cached" means the consumer
copies table-derived data into its own state, so a later in-place mutation of the tables would not
reach it.

## Game glue (`KSA.Kfs`)

| consumer | what it reads | cached? |
| --- | --- | --- |
| `KfsIntegration.Workbench` (KfsIntegration.cs:99) | passes `Tables` to `new Workbench(…)` | reference held in `Workbench.Tables` |
| `KfsVehicleHost` ctor (KfsVehicleHost.cs:105–110) | stores `_tables`; `new VehicleComputer(tables, tables.BenchPart, vehicle.Id)` | reference in `_tables`; the vehicle's part is always `BenchPart` |
| `KfsVehicleHost.SamplerFor` (line 133) | `new HostLeafMap(_tables, set.Layout)` | leaf map cached per controller set |
| `KfsServices` (KfsServices.cs:305, 594) | `KfsIntegration.Tables.Services[id].Name` for event text | reads the static each time[^kfs-services] |
| `KfsReferenceWindow` ctor (KfsReferenceWindow.cs:115–130, 241–270, 373–430) | `CostsData.Cores`, `OpcodesData.Rows`, `OpcodesByMnemonic`, `CostTables`, `ServicesData.Services`, `Services`, `CostsData.LogCyclesPerValue` | **yes** — entries built once in the constructor; the two windows are kept in `_opcodeReference` / `_serviceReference` (KfsEditorWindow.cs:1010)[^kfs-reference-window] |
| `KfsEditorWindow` (KfsEditorWindow.cs:1854, 2085, 2094, 2149) | `_workbench.Tables` → `BusMonitorView.Definition`, `MessageLayout.Find`, `DiagnosticManual.Page` | small per-message caches (`_busDefinitions`, `_busLayoutCache`, `_diagnosticTip`)[^kfs-editor-window] |

## Workbench and views (`KSA.Kfs.Host.Workbench`, `KSA.Kfs.Host.Views`)

| consumer | what it reads | cached? |
| --- | --- | --- |
| `Workbench` ctor (Workbench.cs:364–388) | `Tables = tables`; `new BenchMachine(tables)`; `tables.BenchLayout` | reference |
| `Workbench` (lines 66, 203, 349–355, 507–526, 644, 675–676, 769–801, 839–840, 870, 934, 1487–1491, 1823–1826, 1856, 1921–1943) | `BenchLayout`, `BenchPart`, `Parts`, `Part(name)`, `MessagesData.Messages`, `new CompileInput { Game = Tables }`, `new Assembler(Tables, …)`, `new BuiltinIncludes(Tables, …)`, `ImageReader.Read(…, Tables, …)` | no — read on demand[^workbench] |
| `BusMonitorView` (BusMonitorView.cs:16, 118–360) | `MessageLayout.Find`, `MessagesData.Messages` (`Meaning`, `Section`, `Names`) | **yes** — static `_enumNames` dictionary keyed `(GameTables, field, value)`[^host-views] |
| `DiagnosticManual.Page` (DiagnosticManual.cs:11–50) | `DiagnosticsData.Rules` | no |
| `DisassemblyView.Build` / `CopyAsKasm`, `Listing.Build` / `Text` | `Opcodes` | no |

## Host (`KSA.Kfs.Host`)

| consumer | what it reads | cached? |
| --- | --- | --- |
| `HostComputer` ctor (HostComputer.cs:217–225) | `Tables = tables` (protected readonly); `new KvmMachine(part, tables, 0)` | reference[^host-computer] |
| `HostComputer.Load` (lines 302–314) | `ImageReader.Read`, `MessageLayout.Find`, `MessageLayout.HostPublished` | no |
| `HostComputer` syscall sink (lines 669–745, 804) | `Tables.Services[id]`, `GameTables.CellsOf`, `Tables.EventsData.Events` | no |
| `HostComputer` (line 333) | `new BusTracker(Tables, img, layout)` | **yes** — per image load: one `MessageLayout` per image message |
| `VehicleComputer` (VehicleComputer.cs:146, 231, 251, 334) | base ctor; `Tables.Services[id].Name`; `new HostMessages(Tables, layout)` | **yes** — `HostMessages._layouts` / `_leaves` built at load[^vehicle-computer][^host-messages] |
| `HostLeafMap` ctor (HostLeafMap.cs:25–44) | `MessageLayout.All(tables, layout)` filtered to `HostMessages.MsG3Order` | **yes** |
| `BenchMachine` (BenchMachine.cs:31–43) | `tables.BenchPart`, `Tables.BenchLayout` | reference |
| `FakeVehicle` (FakeVehicle.cs:13–48) | `tables.BenchPart`, `new Assembler(Tables, …)` | reference; no constructor call found in this build |

## VM and image (`KSA.Kfs.Vm`, `KSA.Kfs.Vm.Image`)

| consumer | what it reads | cached? |
| --- | --- | --- |
| `KvmMachine` ctors (KvmMachine.cs:1845–1863) | `game.FpCheckData.Vectors` → `FpSelfTest.Run`; `_tables = game` | reference[^kvm-machine] |
| `KvmMachine` SYSCALL (lines 1122–1190) | `IsWiredService(id)`, `_tables.Services[id]` (`ArgCells`, `ResultCells`, `Name`) | no |
| `KvmMachine.IsWiredService` (lines 2248–2258) | `_tables.Services` + `Kind` ∈ {`request`, `request+result`} | no |
| `KvmMachine` (lines 299, 696, 1883, 1927, 2408) | `BusSteps.Of`, `OpcodesByMnemonic["CALL"]`, `ImageReader.Read`, `SoftRoutines.Table`, `EventsData.Events.Max(Id)` | no |
| `ArgDomains` (ArgDomains.cs:22–51, 194–248) | `MessagesData.Messages` ids, `Services.Values` domains, `EnumDomain`, `FiniteRange`, `EventsData.Events` | **yes** — `_catalogueIds`, `_enums`, `_ranges` snapshotted when first needed (KvmMachine.cs:1160–1163); events are re-read each call[^arg-domains] |
| `SoftRoutines.Table` / `Name` (SoftRoutines.cs:7–52) | `Categories`, `RoutinesData.Routines`, `Opcodes` | caller caches the array[^soft-routines] |
| `ImageBuilder` (ImageBuilder.cs:49–70, 190–191) | `Opcodes`, `ServiceTableVersion`, `MessageCatalogueVersion` | no[^image-builder] |
| `ImageReader.Read` (ImageReader.cs:41–52, 298, 350, 426, 556–559, 642–687, 765) | `Opcodes`, `GameTables.ImmSize`, `Services.ContainsKey`, both versions, `MessagesData.Messages` ids, `MessageLayout.BodySizeOf`, `AllowedTickHz` | no[^image-reader] |
| `CostModel.Cost` (CostModel.cs:8–41), `BusSteps` (BusSteps.cs:9–25) | `Services` (`CostClass`, `PerValue`), `Categories`, `OpcodesByMnemonic` | no[^cost-model] |

## Languages and compiler (`KSA.Kfs.KC`, `KSA.Kfs.KAsm`, `KSA.Kfs.Compiler*`)

| consumer | what it reads | cached? |
| --- | --- | --- |
| `CompileInput.Game` (CompileInput.cs:16) | the tables for one compile | reference |
| `Compiler.Compile` (Compiler.cs:121, 127) | `new Emitter(input.Game, …)`, `CatalogueData.DefaultTickHz` | no |
| `KcFrontEnd` (KcFrontEnd.cs:36–41, 77, 217, 263–278, 330, 541–552, 608, 768) | `Sema.Tables`, `Categories`, `Services[id].RequiresStock`, `MessageLayout.Find`, `MessageLayout.SequencerForbidden`, `AllowedTickHz`, `CatalogueData.DefaultTickHz`, `VehicleHeader.Generate(…, input.Game)` | no[^kc-front-end] |
| `Parser.Bindings` (Parser.cs:141–143, 3085–3103) | `ServicesData.Services` (`Kc`, `Reserved`), `Services[id]` → function-name → `ServiceInfo` map | **yes** — static `_bindings`, rebuilt only when the `GameTables` *reference* changes[^kc-parser] |
| `Parser.ServiceCall` (lines 3105–3206) | `EventsData.Events` (`Id`, `Name`, `ParamType`, `Parameter`) | no |
| `Lowering.Syscall` (Lowering.cs:1347–1410) | `Services[id]` (`ArgCells`, `Name`) | no |
| `GeneratedHeaders` (GeneratedHeaders.cs:23–61, 116–173) | `MessagesData.Version`, `VehicleHeader.Generate`, `new BuiltinIncludes(…, "vehicle.kinc")` | **yes** — `_texts`: per `IoLayout`, keyed `(GameTables, target, part, kinc)`[^kc-headers] |
| `VehicleHeader.Generate` (VehicleHeader.cs:39–58, 142–190) | `MessageLayout.All(tables, layout)` | **yes** — `_generated`: per `IoLayout`, invalidated when the tables reference differs |
| `BuiltinIncludes` ctor (BuiltinIncludes.cs:33–108) | `kfs.kinc`: `ServicesData.Services` (`Symbol`, `Id`), `SysBlockData.Fields`, `TrapsData.Traps`, `CatalogueEnums.All`; `vehicle.kinc`: `MessageLayout.All` | no — a new object per `.include` (Assembler.cs:329)[^kasm] |
| `Assembler` (Assembler.cs:111–147, 329, 600, 902–910, 1349–1398) | `AllowedTickHz`, `CatalogueData.DefaultTickHz`, `MessageLayout.Find`, `OpcodesByMnemonic` | no |
| `Disassembler` (Disassembler.cs:26–58, 180) | `OpcodesByMnemonic`, `Opcodes` | no |
| `Emitter` (Emitter.cs:37–55, 124, 151, 291, 430, 633–696) | `Opcodes`, `GameTables.ImmSize`, `CatalogueData.DefaultTickHz`; creates `ProgramAnalysis`, `BusAnalysis`, `ImageBuilder` | no |
| `ProgramAnalysis` (ProgramAnalysis.cs:31, 106, 145–147, 557–968) | `Opcodes`, `Services.TryGetValue` (error "SYSCALL … is not a service in the table") | no[^compiler] |
| `BusAnalysis` (BusAnalysis.cs:35, 189, 207–208, 314, 434) | `MessageLayout.All`, `OpcodesByMnemonic`, `Opcodes`, `Services` | per-compile dictionary |
| `IrValidator.Check` (IrValidator.cs:11, 66, 95) | `Opcodes`, `Services` ("unknown service …") | no |
| `IrBuilder` (IrBuilder.cs:81, 200) | `Opcodes` | no |
| `DiagnosticRegistry` ctor (DiagnosticRegistry.cs:14–24) | `DiagnosticsData.Rules` | **yes** — but never constructed in this build |

## One cache that ignores the instance

`MessageLayout._elements` is a `private static readonly ConcurrentDictionary<(string Group, string Spec, bool WithLeaves), …>`:
the parsed shape of a per-vehicle element struct (`thruster[i]`, `engine[i]`, `gimbal[i]`) is cached
for the process lifetime, keyed by the group name and the literal `type` spec string, **not** by
`GameTables` instance (MessageLayout.cs:25, 158–167).[^message-layout] Changing an element spec
string changes the key, so this is safe for added or edited specs; it matters only if a spec were
mutated to an identical string with different meaning, which cannot happen.

# Hard-coded mirrors of table content

The tables are not the only copy of this information. The C# code carries compile-time mirrors, so
adding a row to a table does **not** by itself create behaviour:

| mirror | where | mirrors |
| --- | --- | --- |
| `ServiceIds` constants (`SVC_LOG = 1` … `SVC_BURN_ABORT = 67`, `TABLE_VERSION = 1`) | KSA.Kfs.Vm/ServiceIds.cs | services.json ids; inlined as literals in `switch (request.Id)` / `switch (r.Id)` in `HostComputer`, `VehicleComputer`, `KfsServices`[^vm-mirrors] |
| `Op` enum; the interpreter `switch` | KSA.Kfs.Vm/Op.cs, KvmMachine.cs | opcodes.json |
| `TrapCode` enum | KSA.Kfs.Vm/TrapCode.cs | traps.json |
| `SysBlock` offsets | KSA.Kfs.Vm/SysBlock.cs | sysblock.json |
| `CoreClass`, `ResetCause`, `TaskKind`, `StringKind`, `DebugLang`, `DebugTypeKind`, `DebugLocalKind`, `SchedulerPhase`, `TaskStatus`, `Direction`, `AttitudeProfile`, `Apsis`, `BurnEndCause` enums | KSA.Kfs.Vm/*.cs | enums.json (same order) |
| `BurnStatusState` enum | KSA.Kfs.Host.Services/BurnStatusState.cs | the `` `0` `NONE` … `6` `PENDING` `` text of `MSG_BURN_STATUS.state` |
| `SystemHeaders.Files["kfs.h"]` — the whole kC `<kfs.h>` text, including every `KFS_*` constant and every service prototype; and `SystemHeaders.Builtins` | KSA.Kfs.KC/SystemHeaders.cs:20–30 | services.json `kc` column, events.json, `CatalogueEnums`[^kc-headers] |
| `MessageLayout.HostPublished`, `SequencerForbidden`; `HostMessages` id constants and packers | MessageLayout.cs:27–29; HostMessages.cs | messages.json |
| `CostRow.For` / `ServiceClassRow.For` core-name switches | CostRow.cs:8–18, ServiceClassRow.cs:8–18 | enums.json `core_class` |

# Modding notes

## Could a mod inject extra rows so the whole toolchain sees them?

Yes for **services, messages, events and enums as data**, with one instance shared by every layer —
but the tables only describe; the game-side *behaviour* for a new service id or a new host-published
message has to be supplied by further patches (see the consumer and mirror tables above and
[/integration/kfs-services-game-side.md](/integration/kfs-services-game-side.md),
[/host/service-request-handling.md](/host/service-request-handling.md)).

### (a) Harmony-patch the loader so it reads augmented JSON

Targets: `GameTables.LoadEmbedded()` (public static, no parameters) or
`GameTables.Load(Func<string, Stream?>, string)` (public static).

- **Prefix on `LoadEmbedded`** returning `false` with `__result = GameTables.Load(myOpen, "embedded:KSA+mod")`,
  where `myOpen(file)` returns the stock manifest stream for untouched files and a mod-built stream
  (stock JSON parsed, rows appended, re-serialised) for `services.json` / `messages.json` /
  `enums.json` / `events.json`. Everything then flows through the normal `Read<T>` + `Build()` path,
  so **all derived data is consistent and nothing is stale**: `Services`, `ServiceInfo.ArgCells`,
  `ArgumentOf`, the lazily built `ArgDomains`, `Parser._bindings`, `kfs.kinc` constants, the
  reference window, `CostModel` — all are computed from the augmented rows.
- **Prefix on `Load(Func, string)`** that wraps the `open` delegate (`open = file => Augment(file, original(file))`)
  achieves the same and also covers the `--game DIR` / CLI path.
- Constraints on the augmented JSON: every rule in [JSON binding rules](#json-binding-rules-datajsoncontext)
  (no unknown properties, no comments, no trailing commas, all required properties) and every check
  in [`Build()`](#build--validation-and-derivation-in-order). A violation throws
  `GameTablesException` out of the `KfsIntegration.Tables` getter; because `LazyInitializer` leaves
  `_tables` null on an exception, **every later access re-runs the loader and throws again**, i.e.
  kFS is unusable until the mod's JSON is fixed. A defensive patch should validate by calling
  `GameTables.Load` itself inside a `try` and fall back to the stock tables.
- Timing: the patch must be applied before the first `KfsIntegration.Tables` access
  ([above](#when-tables-is-first-touched)) — in practice any time during mod load, since nothing
  reads the tables at startup. If the tables were already loaded (for example another mod opened the
  workbench), a loader patch has no effect; combine with (b).
- `LoadEmbedded` is a small static method and could in principle be inlined into the
  `LazyInitializer` delegate call site; it is passed as a method-group delegate
  (`GameTables.LoadEmbedded`), so the call goes through the delegate and a Harmony prefix on the
  method body is effective. `Unverified:` not tested against the running game. `Verified:` outside
  the game, against the real `KSA.dll`, a Harmony **postfix** on `LoadEmbedded` that replaces
  `__result` with `GameTables.Load(augmentedOpen, …)` made `KfsIntegration.Tables` return the
  augmented instance — the same object on every read, the postfix running once — and the loader
  accepted the extra service rows; that postfix is the tested variant, see
  [adding a service](/modding/adding-a-service.md).[^offline-harness]

### (b) Build a second instance and swap `KfsIntegration._tables`

`GameTables.Load(Func<string, Stream?>, string)` is public, so a mod can build an augmented instance
without any patch and store it with reflection into `private static GameTables? KfsIntegration._tables`
(a static field of a public static class).

- If done **before first use**, the effect is identical to (a): `LazyInitializer.EnsureInitialized`
  returns the existing non-null value and never calls `LoadEmbedded`.
- If done **after first use**, holders of the old reference keep it: `Workbench.Tables`,
  `Workbench.Bench` (a `BenchMachine` with its `KvmMachine._tables`), every existing
  `KfsVehicleHost._tables` / `VehicleComputer.Tables` / `KvmMachine._tables`, cached
  `KfsReferenceWindow`s and the already-created `KfsEditorWindow`. Only `KfsServices`' two
  `KfsIntegration.Tables.Services[…]` lookups and newly created hosts would see the new instance, and
  an image built by the (old-tables) workbench could be loaded on a (new-tables) vehicle computer or
  vice versa. Instance-keyed caches (`Parser._bindings`, `GeneratedHeaders._texts`,
  `VehicleHeader._generated`, `BusMonitorView._enumNames`) rebuild correctly for the new reference.
  A late swap therefore also needs `KfsIntegration._workbench` / `_editor` reset (both private
  static) and existing vehicle hosts torn down — not recommended.
- Validation requirements are the same as (a), but the failure mode is friendlier: the mod calls
  `Load` itself, can catch `GameTablesException`, and simply not swap.

### (c) Mutate the loaded instance in place

Possible, because everything is an ordinary mutable collection behind a public getter:
`tables.ServicesData.Services.Add(new ServiceRow(…))`, `tables.Services[id] = new ServiceInfo(…)`,
`tables.MessagesData.Messages.Add(new MessageRow(…))`, `tables.EnumsData.Enums["x"] = …`,
`tables.EventsData.Events.Add(new EventRow(…))`. All row records have public constructors. No
validation runs, so the mod must reproduce what `Build()` derives:

| added thing | must also be done by hand | stale if added late |
| --- | --- | --- |
| service | add both the `ServiceRow` (for `Parser.Bindings`, `kfs.kinc` `SVC_*`, reference window) **and** the `ServiceInfo` with correct `ArgCells`/`ResultCells`/`Domains`/`ArgumentOf`/`ArgumentNames` | `Parser._bindings` (static, same reference → never rebuilt); any live `KvmMachine._domains` (`ArgDomains` snapshot — a new `enum:` or `finite:[…]` domain string missing from the snapshot is silently **not checked**); an open `KfsReferenceWindow`; cached `vehicle.kinc` text is unaffected (services are in `kfs.kinc`, built fresh) |
| message | `MessageRow` only (no derived structure) | `ArgDomains._catalogueIds` of live machines; `GeneratedHeaders._texts` and `VehicleHeader._generated` entries already generated for a layout (same tables reference → stale header text without the new struct); `HostMessages` / `HostLeafMap` / `BusTracker` of already-loaded images |
| enum / enum value | dictionary entry | `ArgDomains._enums` ranges of live machines; `BusMonitorView._enumNames` |
| event | list entry | nothing cached (`EventsData.Events` is scanned on every use), but see the hard-coded parameter rules in [events-table](/tables/events-table.md) |

In-place mutation is only safe **before** the first compile, the first machine SYSCALL and the first
reference-window open — at which point (a) or (b) is strictly better because `Build()` does the
derivation and validation.

## Validations an added row must pass

- **Service row**: `id` 0–65535 and not already used (20 ids are taken; see the id map in
  [services-table](/tables/services-table.md#id-space)); `name`, `args`, `symbol`, `summary` present;
  `kind` one of `query`/`request`/`request+result` (use `request` or `request+result` —
  `KvmMachine.IsWiredService` traps `BAD_SYSCALL` for `query`); `arg_types` and `result_types` lists
  of `i32 u32 f32 ptr cell i64 f64`; `cost_class` one of `Light`/`Medium`/`Heavy`; `arg_domains` the
  same length as `arg_types` with a domain that fits each type; `host_checks` non-null (any string,
  conventionally `"—"`); if `kc` is given, its parenthesised argument list must either have exactly
  one name per `arg_types` entry or group adjacent equal-domain entries.
- **Message row**: `id` a unique six-character `0x…` string (four hex digits); every field `type` one
  of `bool uint32 int64 float vec3 quat mat3 dvec3` or a `{name: type, …}` element spec on a name
  ending in `[i]`. The fields named `mode`, `roll_mode` and `state` are looked up by *first match in
  file order* by `CatalogueEnums` and `BusMonitorView`, so a new message placed **before**
  `MSG_ATT_TARGET` / `MSG_BURN_STATUS` must not reuse those field names.
- **Enum**: any new key is accepted. Existing lists must keep their order (the C# enums mirror
  them), and `core_class` must stay equal to `costs.cores`.
- **Event row**: `id`, `name`, `parameter`, `param_type`, `keyword` all required; nothing validated.

## Is it safe to add rows without bumping `version`?

- Adding rows and leaving `services.version` = 1 / `messages.version` = 4: images built before the
  change still load (the versions match and their SYSCALL ids / message ids still exist); images
  built after the change that use a new id load only where the augmented tables are present —
  without the mod, `ImageReader` rejects them with `SYSCALL at 0x…: unknown service id 0x….` or
  `message 0x…. not in the catalogue` rather than a version error.
- Bumping a version makes every previously built image fail the version check
  (`service_table_version differs from the game's`). Whether images persist across sessions (and so
  whether that matters) is covered in
  [/integration/lifecycle-and-persistence.md](/integration/lifecycle-and-persistence.md);
  `Inferred:` since the workbench rebuilds from source on `kfs build`/`kfs load`, leaving the versions
  untouched is the lower-risk choice. `Verified:` with two service rows added and both versions
  left alone, the game's own `Workbench` built, loaded and started a kC image on a `FakeVehicle`
  outside the game.[^offline-harness]
- Changing an existing message's field list changes its body size, and `ImageReader` compares each
  image message's `body_size` with `MessageLayout.BodySizeOf` — so existing messages must not be
  edited, only new ones added.
- Cost-table hashes and the layout fingerprint do not depend on services, messages, events or enums.

## Access summary

| thing | visibility | static? |
| --- | --- | --- |
| `GameTables` type, its three `Load*` methods, all helpers | public | static methods |
| `GameTables.Build`, `Fail`, `ParseImm`, `BuildX`, `CheckX`, `Arguments`, `CheckDomains` | private | instance (`Arguments`, `Fail`, `ParseImm`, `CheckRowsCover` static) |
| derived-collection setters | private | — |
| `KfsIntegration._tables`, `_workbench`, `_editor`, `_registered` | private static | yes |
| `KfsIntegration.Tables`, `Workbench`, `UseWorkbench(Workbench)` | public static | yes |
| `DataJsonContext.Default` | public static | yes |
| row/info records (`ServiceRow`, `ServiceInfo`, `MessageRow`, `EventRow`, …) | public sealed records with public constructors | — |

Threading: `LazyInitializer.EnsureInitialized(ref T, Func<T>)` may run the factory on more than one
thread under a race and keeps the first result; the tables are otherwise read without locks from the
main thread (workbench, UI) and from whatever thread runs vehicle dispatch (see
[/integration/dispatch-and-threading.md](/integration/dispatch-and-threading.md)). In-place mutation
(c) from the main thread while a dispatch thread enumerates `Services.Values` or
`MessagesData.Messages` would be a data race.

[^game-tables]: GameTables.cs — members 15–73; `Load`/`LoadEmbedded`/`Load(Func)` 75–126; `ImmSize`/`ParseImm` 133–169; `Build` 171–206; `BuildCategories`/`CheckRowsCover` 208–232; `BuildOpcodes` 234–270; `BuildCostTables` 272–390; `CanonicalCostText` 392–428; `BuildParts` 430–468; `Part` 470–473; `CellsOf` 475–536; `BuildServices` 538–582; `Arguments` 584–639; `CheckDomains` 641–690; `FiniteRange` 692–700; `EnumDomain` 702–730; `BuildLayout`…`ComputeLayoutFingerprint` 732–851; `CheckTraps` 853–862; `CheckMessages` 864–874.
[^data-json-context]: DataJsonContext.cs:11–27 (attributes), 163–171 (`s_defaultOptions`), 231–237 (`K8`…`K32D` property names), 557–559 (`Default`), 8009–8166 (`ServiceRowCtorParamInit`: which `ServiceRow` parameters have defaults).
[^catalogue-enums]: CatalogueEnums.cs:34–64.
[^message-layout]: MessageLayout.cs:25 (`_elements`), 27–29 (`HostPublished`, `SequencerForbidden`), 137–151 (`TypeShape`), 158–167 (`CachedElement`), 230–349 (`Build`, `All`, `Find`, `BodySizeOf`).
[^table-records]: ServiceInfo.cs:3–14; ServiceRow.cs:5; CoreCostTable.cs:5–28; CostRow.cs:6–19; ServiceClassRow.cs:6–19; PartDescription.cs:3; IoLayout.cs:6–9; GameTablesException.cs:5–11.
[^kfs-integration]: KfsIntegration.cs:77–89 (statics, `Tables`), 93–121 (`Workbench` getter), 137–156 (`UseWorkbench`, `RegisterCommands`), 183–214 (`ShowEditor`, `ToggleEditor`).
[^kfs-vehicle-host]: KfsVehicleHost.cs:37, 105–116 (ctor), 127–136 (`SamplerFor`), 139–142 (`For`).
[^kfs-services]: KfsServices.cs:305, 594.
[^kfs-reference-window]: KfsReferenceWindow.cs:115–130, 241–270, 373–430.
[^kfs-editor-window]: KfsEditorWindow.cs:1010, 1854, 2085, 2094, 2149.
[^kfs-console-commands]: KfsConsoleCommands.cs:15–173 (verb table: which verbs call `KfsIntegration.Workbench`), 193–232 (`Execute`).
[^kfs-debug-window]: KfsDebugWindow.cs:23 (`Show`), 78 (`Workbench` property), 88–104 (`OnDrawUi`), 110–119 (`DrawEnable`).
[^program]: Program.cs:978 (`KfsIntegration.RegisterCommands(this)`), 2551 (`KfsIntegration.BeforeInputEvents()`), 4423 ("Show kFS Debug" menu item).
[^workbench]: Workbench.cs:66, 68, 203, 349–355, 364–388, 507–526, 644, 675–676, 769–801, 839–840, 870, 934, 1487–1491, 1823–1826, 1856, 1921–1943.
[^cli]: Cli.cs:262–290 (`Run`), 300, 313, 362, 403, 419, 435, 573.
[^kc-cli]: KcCli.cs:15, 127–137.
[^image-reader]: ImageReader.cs:41–52 (`Read`), 298, 350, 426, 556–559 (unknown service id), 642–655 (version checks, catalogue membership), 684–687 (body size), 765–775 (tick rate, budget, part/cost-hash check).
[^image-builder]: ImageBuilder.cs:49, 62–68, 180–200 (header writes: cost hash @24, layout fingerprint @28, versions @32/@34).
[^cost-model]: CostModel.cs:8–41; BusSteps.cs:9–25.
[^kvm-machine]: KvmMachine.cs:65, 155, 299, 696, 1122–1190 (SYSCALL), 1845–1863 (ctors), 1881–1883, 1927, 2248–2258 (`IsWiredService`), 2408.
[^arg-domains]: ArgDomains.cs:14–51 (snapshot), 53–187 (`Check`), 194–248 (event checks).
[^vm-mirrors]: KSA.Kfs.Vm/ServiceIds.cs:3; Op.cs; TrapCode.cs; SysBlock.cs; CoreClass.cs, ResetCause.cs, TaskKind.cs, StringKind.cs, DebugLang.cs, DebugTypeKind.cs, DebugLocalKind.cs, SchedulerPhase.cs, TaskStatus.cs, Direction.cs, AttitudeProfile.cs, Apsis.cs, BurnEndCause.cs; KSA.Kfs.Host.Services/BurnStatusState.cs.
[^soft-routines]: SoftRoutines.cs:7–52; FpSelfTest.cs:10–85.
[^host-computer]: HostComputer.cs:21, 217–225, 302–314, 333, 669–745, 747–811.
[^vehicle-computer]: VehicleComputer.cs:146–150, 190, 212–236, 251, 334.
[^host-messages]: HostMessages.cs:387–391; HostLeafMap.cs:25–44; BusTracker.cs:74–90; BenchMachine.cs:31–43; FakeVehicle.cs:13, 31–48.
[^host-views]: BusMonitorView.cs:16, 118–144, 200–248, 250–360; DiagnosticManual.cs:11–50; DisassemblyView.cs:20, 115; Listing.cs:16, 39–41.
[^kc-parser]: Parser.cs:141–143, 3085–3103 (`Bindings`), 3105–3206 (`ServiceCall`), 3414–3423 (binding lookup and the "MS6" fallback).
[^kc-front-end]: KcFrontEnd.cs:36–41, 74–82, 204–239 (`ServiceOwnership`), 263–302, 330, 541–552, 608, 768; Lowering.cs:17, 39, 92, 1347–1410; Sema.cs:24.
[^kc-headers]: GeneratedHeaders.cs:23–61, 116–173, 228–251; VehicleHeader.cs:32–58, 60–190; SystemHeaders.cs:8–41.
[^kasm]: BuiltinIncludes.cs:10, 33–108; Assembler.cs:29, 111–147, 320–345, 600, 902–910, 1349–1398; Disassembler.cs:26–58, 180.
[^offline-harness]: Experiments E2, E10 and E11 in [the offline harness](/modding/offline-harness.md).
[^compiler]: ProgramAnalysis.cs:15, 31, 106, 143–156, 557, 596, 731, 749, 917, 968; BusAnalysis.cs:13, 35, 189, 207–208, 314, 432–443; IrValidator.cs:11, 32, 66, 93–103; IrBuilder.cs:12, 81, 200; Emitter.cs:17, 37–55, 124, 151, 291, 430, 633–696; CompileInput.cs:16; Compiler.cs:121, 127; DiagnosticRegistry.cs:8–52.
