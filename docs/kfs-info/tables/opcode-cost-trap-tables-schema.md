---
type: Data Table Reference
title: Row schemas of the VM tables — opcodes, costs, traps, sysblock, routines, fpcheck, edgecases
description: The record types, JSON keys, optionality and load-time constraints of the seven VM-facing embedded tables, with pointers to the /vm/ documents that reproduce their contents.
resource: ksa-decomp:KSA.Kfs.Vm.Tables/OpcodeRow.cs
tags: [kfs, tables, schema, opcodes, costs, traps, sysblock, routines, fpcheck, edgecases]
sources:
  - id: opcode-records
    resource: ksa-decomp:KSA.Kfs.Vm.Tables/OpcodeRow.cs
    title: OpcodesFile, OpcodeRow, OpcodeInfo, ImmType (decompiled)
  - id: cost-records
    resource: ksa-decomp:KSA.Kfs.Vm.Tables/CostsFile.cs
    title: CostsFile, CostRow, CostCell, CostOverride, CategoryTableRow, CategoryOpcodesRow, ServiceClassRow, CoreCostTable (decompiled)
  - id: trap-records
    resource: ksa-decomp:KSA.Kfs.Vm.Tables/TrapRow.cs
    title: TrapsFile, TrapRow (decompiled)
  - id: sysblock-records
    resource: ksa-decomp:KSA.Kfs.Vm.Tables/SysBlockFile.cs
    title: SysBlockFile, SysBlockField, SysBlockReserved (decompiled)
  - id: routine-records
    resource: ksa-decomp:KSA.Kfs.Vm.Tables/RoutinesFile.cs
    title: RoutinesFile, RoutineRow, FamilyRow (decompiled)
  - id: fpcheck-records
    resource: ksa-decomp:KSA.Kfs.Vm.Tables/FpVector.cs
    title: FpCheckFile, FpVector (decompiled)
  - id: edgecase-records
    resource: ksa-decomp:KSA.Kfs.Vm.Tables/EdgeCase.cs
    title: EdgeCasesFile, EdgeCase (decompiled)
  - id: game-tables
    resource: ksa-decomp:KSA.Kfs.Vm.Tables/GameTables.cs
    title: GameTables (decompiled)
  - id: opcodes-json
    resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.opcodes.json
    title: opcodes.json
  - id: costs-json
    resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.costs.json
    title: costs.json
  - id: traps-json
    resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.traps.json
    title: traps.json
  - id: sysblock-json
    resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.sysblock.json
    title: sysblock.json
  - id: routines-json
    resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.routines.json
    title: routines.json
  - id: fpcheck-json
    resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.fpcheck.json
    title: fpcheck.json
  - id: edgecases-json
    resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.edgecases.json
    title: edgecases.json
  - id: fp-self-test
    resource: ksa-decomp:KSA.Kfs.Vm/FpSelfTest.cs
    title: FpSelfTest (decompiled)
  - id: soft-routines
    resource: ksa-decomp:KSA.Kfs.Vm/SoftRoutines.cs
    title: SoftRoutines (decompiled)
  - id: image-builder
    resource: ksa-decomp:KSA.Kfs.Vm.Image/ImageBuilder.cs
    title: ImageBuilder.RuntimeLibCharge (decompiled)
  - id: builtin-includes
    resource: ksa-decomp:KSA.Kfs.KAsm/BuiltinIncludes.cs
    title: BuiltinIncludes (decompiled)
  - id: kfs-reference-window
    resource: ksa-decomp:KSA.Kfs/KfsReferenceWindow.cs
    title: KfsReferenceWindow (decompiled)
generated: { by: claude-code/claude-fable-5-2, at: 2026-11-25T21:52:36Z }
status: stable
ksa_build: "2026.11.25.5949"
---

# Purpose

Seven of the fourteen embedded tables describe the virtual machine itself. Their **contents** are
reproduced in the `/vm/` documents; this document records only their **shape**: record types, JSON
keys, which keys are optional, what the loader demands of them, and what reads them.

| file | rows in this build | content documented in |
| --- | --- | --- |
| `opcodes.json` | 86 rows defining 146 opcodes (`0x00`–`0xBF`) | [/vm/opcodes.md](/vm/opcodes.md) |
| `costs.json` | 42 categories × 4 cores, 1 override, 3 service classes | [/vm/cost-model-and-budgets.md](/vm/cost-model-and-budgets.md) |
| `traps.json` | 17 traps | [/vm/traps-and-faults.md](/vm/traps-and-faults.md) |
| `sysblock.json` | 16 fields, 256 bytes | [/vm/memory-map-and-sysblock.md](/vm/memory-map-and-sysblock.md) |
| `routines.json` | 16 routines, 5 families | [/vm/soft-routines-and-fp.md](/vm/soft-routines-and-fp.md) |
| `fpcheck.json` | 31 vectors | [/vm/soft-routines-and-fp.md](/vm/soft-routines-and-fp.md) |
| `edgecases.json` | 80 cases | [/vm/soft-routines-and-fp.md](/vm/soft-routines-and-fp.md) |

General JSON rules (snake_case keys, no unknown keys, required constructor parameters, no comments or
trailing commas) and the load order are in
[game-tables-loader](/tables/game-tables-loader.md#json-binding-rules-datajsoncontext). In the
tables below "required" means the JSON key must be present; a `?` type may then still be `null`.
Every file record starts with `int Version` (1–65535) and `string Source`.

# opcodes.json

`source`: "kVM_spec.md §4.3-§4.7 (moved 2026-11-02, D-052)". `version` 1.[^opcodes-json]

`OpcodesFile(int Version, string Source, string Notes, List<OpcodeRow> Rows)` — `notes` is a required
free-text key (it documents the row format; no code reads it).[^opcode-records]

`OpcodeRow(string Section, List<string[]> Ops, string Stack, int? Pops, int? Pushes, string Category, List<string[]>? Imm = null, string? OpStyle = null, string? HexStyle = null, List<string>? Summary = null)`

| property | JSON key | type | required | meaning |
| --- | --- | --- | --- | --- |
| `Section` | `section` | `string` | yes | spec section, `4.3`–`4.7`; groups the reference window |
| `Ops` | `ops` | `List<string[]>` | yes | `[mnemonic, hex byte]` pairs, e.g. `["ADD", "30"]`; one row may define several opcodes |
| `Stack` | `stack` | `string` | yes | stack-effect prose, shown in the reference window |
| `Pops` | `pops` | `int?` | yes (nullable) | operand cells popped; `null` "where the instruction decides at run time (calls, RET, ENTER, SYSCALL)" |
| `Pushes` | `pushes` | `int?` | yes (nullable) | operand cells pushed; `null` likewise |
| `Category` | `category` | `string` | yes | cost category; must exist in `costs.json` |
| `Imm` | `imm` | `List<string[]>?` | no | immediates in encoding order: `[type]` or `[type, name]`; `type` ∈ `i8 u8 i16 u16 u24 u32 u64 rel16` |
| `OpStyle` | `op_style` | `string?` | no | spec-rendering hint (`slash`); not read by code |
| `HexStyle` | `hex_style` | `string?` | no | spec-rendering hint (`slash`, `range`); not read by code |
| `Summary` | `summary` | `List<string>?` | no in JSON, **required by the loader** | one non-blank plain-text line per op, in `ops` order |

Derived: `OpcodeInfo(byte Code, string Mnemonic, ImmType[] Imm, string[] ImmNames, int Size, int CategoryIndex, string Category, int? Pops, int? Pushes, string Section, string Summary = "")`
— one per opcode, in `GameTables.Opcodes[code]` and `OpcodesByMnemonic[mnemonic]`;
`Size = 1 + Σ ImmSize`. `public enum ImmType : byte { I8, U8, I16, U16, U24, U32, U64, Rel16 }` with
`GameTables.ImmSize` = 1, 1, 2, 2, 3, 4, 8, 2 respectively.[^game-tables]

Loader constraints: known category; known immediate types; a summary per op; each op entry exactly
two strings with a parseable hex byte; no opcode byte or mnemonic assigned twice
(GameTables.cs:234–270).

# costs.json

`source`: "kFS_processors_spec.md §3.1-§3.3 and kVM_spec.md §4.2 category list (moved 2026-11-02,
D-052). Placeholder values until calibration (processors §8). Schema P-020; cost_table_hash P-019".
`version` 1.[^costs-json]

`CostsFile(int Version, string Source, List<string> Cores, List<CostRow> Costs, List<CategoryTableRow> TableRows, List<CategoryOpcodesRow> CategoryRows, List<CostOverride> Overrides, List<ServiceClassRow> ServiceClasses, int LogCyclesPerValue)`
— JSON keys `version`, `source`, `cores`, `costs`, `table_rows`, `category_rows`, `overrides`,
`service_classes`, `log_cycles_per_value`; all required.[^cost-records]

| record | parameters | notes |
| --- | --- | --- |
| `CostRow` | `string Category`, `CostCell K8`, `CostCell K32`, `CostCell K32F`, `CostCell K32D` | the four cell keys are literally `K8`, `K32`, `K32F`, `K32D` (`[JsonPropertyName]`); all required. `CostCell For(string core)` throws `ArgumentException` for any other core name |
| `CostCell` | `int? Cycles = null`, `int? Base = null`, `int? PerWord = null`, `bool Soft = false` | JSON `cycles`, `base`, `per_word`, `soft`; all optional in JSON. Loader: `block` and `enter` need `base` + `per_word` and no `cycles`; every other category needs `cycles` only; base cost ≥ 1 |
| `CostOverride` | `string Core`, `List<string> Opcodes`, `int Cycles`, `bool Soft`, `string Why` | per-opcode override on one core; all required; `why` is prose (not read) |
| `CategoryTableRow` | `List<string> Categories`, `string? LabelSuffix = null`, `bool ListValues = false` | which categories share a row of the *spec's* cost table; `label_suffix` / `list_values` are rendering hints, not read by code |
| `CategoryOpcodesRow` | `List<string> Categories`, `string Opcodes` | prose mapping categories to opcodes; not read by code beyond the coverage check |
| `ServiceClassRow` | `string Class`, `int K8`, `int K32`, `int K32F`, `int K32D` | see [services-table](/tables/services-table.md#serviceclassrow-in-costsjson) |

Loader constraints (GameTables.cs:208–232, 272–390): category names unique; `cores` equal to
`enums.json` `core_class`; `table_rows` and `category_rows` each cover every category exactly once;
the per-cell shape rules above; a `soft` cell needs a routine row for its category and a family
footprint on that core; overrides name known mnemonics and cores, cost ≥ 1, and a soft override needs
a family. The category **order** defines `CategoryIndex`, the index into `CoreCostTable.Cycles`,
`PerWord`, `Soft`, `Family`.

Derived per core: `CoreCostTable` — members and the `Hash` (CRC-32 of the canonical cost text,
recorded in images) are described in
[game-tables-loader](/tables/game-tables-loader.md#step-3--buildcosttables-lines-272390).

# traps.json

`source`: "kVM_spec.md §3.6 (moved 2026-11-02, D-052). Numeric codes: P-023 (1-17 in table order, 0
none)". `version` 1.[^traps-json]

- `TrapsFile(int Version, string Source, List<TrapRow> Traps)`
- `TrapRow(int Code, string Name, string Cause)` — JSON `code`, `name`, `cause`; all required.[^trap-records]

Loader constraint: `Traps[i].Code == i + 1` (`CheckTraps`). Read by `BuiltinIncludes` to define
`TRAP_{NAME} = code` in `<kfs.kinc>` (BuiltinIncludes.cs:52–55).[^builtin-includes] The VM itself uses
the hard-coded `KSA.Kfs.Vm.TrapCode` enum; machine-state restore accepts trap codes up to the literal
17.

# sysblock.json

`source`: "kVM_spec.md §2.5 (moved 2026-11-02, D-052)". `version` 1, `size` 256.[^sysblock-json]

- `SysBlockFile(int Version, string Source, int Size, List<SysBlockField> Fields, SysBlockReserved Reserved)`
- `SysBlockField(int Offset, string Name, string Type, string Meaning)` — `type` strings in the data
  are `uint32`, `float`, `int64`
- `SysBlockReserved(int From, int To, string Meaning)` — in the data `{ from: 68, to: 255, meaning: "Reads return 0" }`[^sysblock-records]

No loader constraint beyond the version. Read by `BuiltinIncludes` only: `sys.{name}` =
`0xA0000000 + offset` and `sys` = `0xA0000000` in `<kfs.kinc>` (BuiltinIncludes.cs:47–51). `Size`,
`Type`, `Meaning` and `Reserved` are not read by code; the VM writes the block using the hard-coded
offsets of `KSA.Kfs.Vm.SysBlock`, and kC's `kfs_sys_t` struct is part of the hard-coded `<kfs.h>`.

# routines.json

`source`: "kFS_processors_spec.md §4.1, §4.2 (moved 2026-11-02, D-052). Category-to-family map, units
and K32D names: P-021". `version` 1.[^routines-json]

- `RoutinesFile(int Version, string Source, List<RoutineRow> Routines, List<CategoryTableRow> TableRows, List<FamilyRow> Families)`
- `RoutineRow(string Category, List<string> K8, List<string> Arm, string Family)` — JSON `category`,
  `k8`, `arm`, `family`: the soft-routine names used on the K8 core and on the ARM-like cores
  (`K32`, `K32F`, `K32D`); one or two names per row
- `FamilyRow(string Id, string Contents, Dictionary<string, int?> Bytes)` — `bytes` maps a core name
  to the family's code footprint in bytes, or `null` when the core never needs it[^routine-records]

Loader constraints (GameTables.cs:274–287): each routine's category and family exist;
`table_rows` covers the routine categories exactly once; duplicate family ids or routine categories
throw `ArgumentException`.

Readers: `SoftRoutines.Table/Name` (profiler labels; when a row lists two names the first is used for
the int/float→wider conversions `I2F U2F L2D LU2D I2D U2D F2D L2F`, the second otherwise);
`CoreCostTable.Family` / `FamilyBytes` feed `ImageBuilder.RuntimeLibCharge` and the matching check in
`ImageReader` — the image header records the summed footprint of every soft family the code
uses.[^soft-routines][^image-builder]

# fpcheck.json

`source`: "kVM_spec.md §3.7 floating-point self-test (D-047, D-052). Expected bits derived by hand
under IEEE 754 round-to-nearest-even with gradual underflow (derivation in each 'why'); 'nan' accepts
any NaN, since the host's own NaN is checked before canonicalisation. The 64-bit conversion vectors
(ops lu2d, l2d, l2f, d2lu, f2l; gap MS4-21, R1 MS4 2026-11-04) take 'a' as an integer or float bit
pattern and check the VM's own conversion code (Conv.cs)". `version` 1.[^fpcheck-json]

- `FpCheckFile(int Version, string Source, List<FpVector> Vectors)`
- `FpVector(string Name, string Op, int Width, string A, string Expect, string Why, string? B = null, string? C = null)`[^fpcheck-records]

| property | JSON key | meaning |
| --- | --- | --- |
| `Name` | `name` | vector id, reported on failure |
| `Op` | `op` | `mul`, `add`, `div`, `sqrt`, `muladd` (host float arithmetic at `width`), or `lu2d`, `l2d`, `l2f`, `d2lu`, `f2l` (the VM's `Conv` routines) |
| `Width` | `width` | 32 or 64 — operand width for the arithmetic ops |
| `A`, `B`, `C` | `a`, `b`, `c` | operand bit patterns as hex strings (`0x` prefix optional); `b`, `c` optional |
| `Expect` | `expect` | expected result bits as hex, or the literal `nan` |
| `Why` | `why` | derivation prose |

Not validated at table load. `FpSelfTest.Run(vectors, out failingVector)` runs in every `KvmMachine`
constructor; an unknown `op` throws `GameTablesException("fpcheck.json: unknown op '…'")`, and a
mismatch makes the constructor throw `UnsupportedHostException` with
`floating-point self-test failed: {name} gave 0x…, expected …; this host can't run kFS computers deterministically`
(FpSelfTest.cs:10–85).[^fp-self-test] `KvmMachine` has a second public constructor taking an explicit
`IReadOnlyList<FpVector>`.

# edgecases.json

`source`: "kVM_spec.md §4.9 (P-024). Vectors derived by hand from each bullet; not rendered (§4.9 is
prose). Values are typed: i32:/u32:/i64: decimal or 0x hex, f32:/f64: bit patterns. 'args' are pushed
in order (first deepest); 'expect' is the resulting stack, top last, or a trap". `version` 1.[^edgecases-json]

- `EdgeCasesFile(int Version, string Source, List<EdgeCase> Cases)`
- `EdgeCase(string Bullet, string Op, List<string> Args, List<string>? Expect = null, string? Trap = null)`
  — `bullet` names the §4.9 bullet (`wraps`, `INT_MIN / -1`, `division`, `shifts`, `float`,
  `canonical NaN`, `NaN comparisons`, `saturating conversions`, `round to nearest even`); `op` is an
  opcode mnemonic; `args`/`expect` are typed literals; `trap` is a trap name (`DIV_ZERO`)[^edgecase-records]

`Not active in this build:` nothing reads `EdgeCasesData` except the version-range check — the cases
are conformance vectors for a test suite that is not in the shipped assembly. They remain a precise,
machine-readable statement of the arithmetic edge behaviour.

# What reads the prose columns

| column | reader |
| --- | --- |
| `OpcodeRow.Section`, `Stack`, `Imm` names, `Summary` | `KfsReferenceWindow.BuildOpcodes` — the in-game "kFS Opcodes" page; it strips spec references such as `(§4.5)` / `(rule 9)` from `stack` with two regexes[^kfs-reference-window] |
| `OpcodeInfo.Summary`, `ImmNames` | reference window, disassembly views |
| `TrapRow.Cause`, `SysBlockField.Meaning`, `FamilyRow.Contents`, `CostOverride.Why`, `CategoryOpcodesRow.Opcodes`, `OpcodesFile.Notes`, `FpVector.Why` | none in this build |

# Modding notes

- These seven tables are **not** extension points for game features: each has a hard-coded C#
  counterpart (the `Op` enum and interpreter `switch`, `TrapCode`, `SysBlock` offsets, the fixed
  four-core record shapes), and the cost table of each core is hashed into every image.
- Editing a cost value, adding a category or an override changes `CoreCostTable.Hash` and invalidates
  existing images for that core (`the image was built for another part or cost table`).
- An augmented-tables mod ([game-tables-loader](/tables/game-tables-loader.md#modding-notes)) should
  pass these seven files through unchanged. The one cost-related knob a service extension touches is
  `cost_class` on its own rows, which references `service_classes` here; adding a new class is
  possible (`service_classes` is not part of the canonical cost text, so the hash is unaffected) but
  every row needs all four `K8`/`K32`/`K32F`/`K32D` values.

[^opcode-records]: OpcodesFile.cs:5; OpcodeRow.cs:5; OpcodeInfo.cs:3; ImmType.cs:3–13.
[^cost-records]: CostsFile.cs:5; CostRow.cs:6–19; CostCell.cs:3; CostOverride.cs:5; CategoryTableRow.cs:5; CategoryOpcodesRow.cs:5; ServiceClassRow.cs:6–19; CoreCostTable.cs:5–28.
[^trap-records]: TrapsFile.cs:5; TrapRow.cs:3.
[^sysblock-records]: SysBlockFile.cs:5; SysBlockField.cs:3; SysBlockReserved.cs:3.
[^routine-records]: RoutinesFile.cs:5; RoutineRow.cs:5; FamilyRow.cs:5.
[^fpcheck-records]: FpCheckFile.cs:5; FpVector.cs:3.
[^edgecase-records]: EdgeCasesFile.cs:5; EdgeCase.cs:5.
[^game-tables]: GameTables.cs:133–169 (`ImmSize`, `ParseImm`), 208–232, 234–270, 272–390, 392–428, 853–862.
[^opcodes-json]: opcodes.json:2–4 (header, notes), 6–95 (rows).
[^costs-json]: costs.json:2–4, 5–48 (`costs`), 49–64 (`table_rows`), 65–84 (`category_rows`), 85–87 (`overrides`), 88–93.
[^traps-json]: traps.json:2–3, 5–21.
[^sysblock-json]: sysblock.json:2–4, 6–21, 23.
[^routines-json]: routines.json:2–3, 5–20 (`routines`), 22–32 (`table_rows`), 33–39 (`families`).
[^fpcheck-json]: fpcheck.json:2–3, 5–35.
[^edgecases-json]: edgecases.json:2–3, 5–84.
[^fp-self-test]: FpSelfTest.cs:10–85; KvmMachine.cs:1845–1863.
[^soft-routines]: SoftRoutines.cs:7–52.
[^image-builder]: ImageBuilder.cs:62–83; ImageReader.cs:573–581.
[^builtin-includes]: BuiltinIncludes.cs:47–55.
[^kfs-reference-window]: KfsReferenceWindow.cs:91–93 (`SpecParenthesis`, `SpecClause`), 241–275.
