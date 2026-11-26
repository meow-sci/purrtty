---
type: Data Table Reference
title: kVM cost model and cycle budgets (costs.json)
description: The complete cost table per core class, how an instruction's cycle cost is computed and charged, the per-tick and init budgets, overruns, and how service calls and soft routines are priced.
resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.costs.json
tags: [kfs, vm, kvm, costs, cycles, budget, overrun, data-table]
sources:
  - id: costs-json
    resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.costs.json
    title: costs.json (embedded)
  - id: cost-model
    resource: ksa-decomp:KSA.Kfs.Vm.Image/CostModel.cs
    title: CostModel (decompiled)
  - id: bus-steps
    resource: ksa-decomp:KSA.Kfs.Vm.Image/BusSteps.cs
    title: BusSteps (decompiled)
  - id: game-tables
    resource: ksa-decomp:KSA.Kfs.Vm.Tables/GameTables.cs
    title: GameTables.BuildCostTables / BuildParts (decompiled)
  - id: core-cost-table
    resource: ksa-decomp:KSA.Kfs.Vm.Tables/CoreCostTable.cs
    title: CoreCostTable (decompiled)
  - id: image-reader
    resource: ksa-decomp:KSA.Kfs.Vm.Image/ImageReader.cs
    title: ImageReader (decompiled) — cost baking and budget rules
  - id: kvm-machine
    resource: ksa-decomp:KSA.Kfs.Vm/KvmMachine.cs
    title: KvmMachine (decompiled) — cycle accounting
  - id: catalogue-json
    resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.catalogue.json
    title: catalogue.json (embedded)
  - id: log-format
    resource: ksa-decomp:KSA.Kfs.Vm.Image/LogFormat.cs
    title: LogFormat (decompiled)
generated: { by: claude-code/claude-fable-5-2, at: 2026-11-25T21:46:43Z }
status: stable
ksa_build: "2026.11.25.5949"
---

# Summary

kVM time is counted in **cycles**. Every instruction has a fixed cycle cost that depends on the processor's
core class; a tick may spend `clock_hz / tick_hz` cycles. Costs are data (`costs.json`), are turned into one
`CoreCostTable` per core class when the tables load, and are **baked into each decoded instruction when an
image is loaded** — the interpreter itself only adds integers.[^cost-model][^image-reader]

The table's own `source` string calls its numbers "Placeholder values until calibration".[^costs-json]

# Clock, tick rate and budgets

| Quantity | Value | Source |
| --- | --- | --- |
| Clock | `clock_hz` of the part; **1,000,000 Hz for every part** in `catalogue.json` | `catalogue.json:17-21` |
| Allowed tick rates | 5, 10, 20, 25, 50, 100 Hz (each must divide 100); default 50 | `catalogue.json:4-5`, `GameTables.cs:457-467` |
| Tick budget | `budget_cycles = clock_hz / tick_hz`, stored in the image and re-checked at load (rule `budget`) | `ImageReader.cs:765-772` |
| Init budget | `init_budget_seconds × clock_hz` = 2 × 1,000,000 = **2,000,000 cycles** in total across all boot ticks | `catalogue.json:7`, `GameTables.cs:446` |

With the 1 MHz clock the per-tick budget is 200,000 cycles at 5 Hz, 100,000 at 10 Hz, 50,000 at 20 Hz,
40,000 at 25 Hz, 20,000 at 50 Hz and 10,000 at 100 Hz.[^catalogue-json] The image header must also match
the part's clock, RAM, code size, NV size, stack limits, init budget and cost-table hash, otherwise loading
fails with rule `part` ("the image was built for another part or cost table; rebuild from source",
`ImageReader.cs:773-776`).

# costs.json in full

`"cores": ["K8", "K32", "K32F", "K32D"]` — must equal `enums.json` `core_class`.[^game-tables]

## `costs` — cycles per category and core

A plain number is `cycles`; `B + P/word` is `base` plus `per_word`; **soft** marks `"soft": true` (the
instruction is implemented by a library routine on that core; see
[soft routines](/vm/soft-routines-and-fp.md)). The `#` column is the category index used throughout the
code (`OpcodeInfo.CategoryIndex`, `Instr.Soft`).

| # | Category | K8 | K32 | K32F | K32D |
| --- | --- | --- | --- | --- | --- |
| 0 | `stack` | 2 | 1 | 1 | 1 |
| 1 | `const32` | 4 | 2 | 2 | 1 |
| 2 | `stack64` | 4 | 2 | 2 | 2 |
| 3 | `mem8` | 2 | 2 | 2 | 1 |
| 4 | `mem16` | 4 | 2 | 2 | 1 |
| 5 | `mem32` | 8 | 2 | 2 | 1 |
| 6 | `mem64` | 16 | 4 | 4 | 2 |
| 7 | `addr` | 6 | 2 | 2 | 1 |
| 8 | `bounds` | 6 | 2 | 2 | 2 |
| 9 | `block` | 4 + 8/word | 4 + 2/word | 4 + 2/word | 4 + 1/word |
| 10 | `branch` | 2 | 3 | 3 | 2 |
| 11 | `call` | 6 | 3 | 3 | 2 |
| 12 | `callf` | 8 | 4 | 4 | 2 |
| 13 | `ret` | 6 | 3 | 3 | 2 |
| 14 | `enter` | 12 + 8/word | 6 + 1/word | 6 + 1/word | 4 + 1/word |
| 15 | `i32.alu` | 4 | 1 | 1 | 1 |
| 16 | `i32.shift` | 30 | 1 | 1 | 1 |
| 17 | `i32.mul` | 40 | 1 | 1 | 4 |
| 18 | `i32.div` | 600 **soft** | 60 **soft** | 8 | 19 |
| 19 | `i64.alu` | 8 | 2 | 2 | 2 |
| 20 | `i64.cmp` | 8 | 2 | 2 | 2 |
| 21 | `i64.shift` | 60 | 4 | 4 | 4 |
| 22 | `i64.mul` | 200 **soft** | 20 **soft** | 4 | 10 |
| 23 | `i64.div` | 3000 **soft** | 300 **soft** | 100 **soft** | 200 **soft** |
| 24 | `f32.add` | 110 **soft** | 50 **soft** | 1 | 1 |
| 25 | `f32.mul` | 150 **soft** | 50 **soft** | 1 | 1 |
| 26 | `f32.div` | 480 **soft** | 150 **soft** | 14 | 17 |
| 27 | `f32.sqrt` | 480 **soft** | 300 **soft** | 14 | 100 **soft** |
| 28 | `f32.cmp` | 60 **soft** | 20 **soft** | 2 | 2 |
| 29 | `f32.sign` | 4 | 1 | 1 | 1 |
| 30 | `f64.add` | 400 **soft** | 80 **soft** | 80 **soft** | 1 |
| 31 | `f64.mul` | 1000 **soft** | 120 **soft** | 120 **soft** | 2 |
| 32 | `f64.div` | 2500 **soft** | 400 **soft** | 400 **soft** | 31 |
| 33 | `f64.sqrt` | 3000 **soft** | 600 **soft** | 600 **soft** | 150 **soft** |
| 34 | `f64.cmp` | 150 **soft** | 30 **soft** | 30 **soft** | 2 |
| 35 | `f64.sign` | 8 | 2 | 2 | 1 |
| 36 | `cvt.i32f32` | 60 **soft** | 30 **soft** | 1 | 4 |
| 37 | `cvt.f32f64` | 80 **soft** | 30 **soft** | 30 **soft** | 1 |
| 38 | `cvt.i32i64` | 8 | 2 | 2 | 2 |
| 39 | `cvt.i64f` | 200 **soft** | 60 **soft** | 60 **soft** | 20 **soft** |
| 40 | `yield` | 10 | 4 | 4 | 4 |
| 41 | `syscall` | 20 | 10 | 10 | 10 |

## `overrides` — per-opcode exceptions

| Core | Opcodes | Cycles | Soft | Why (table text) |
| --- | --- | --- | --- | --- |
| K32 | `DIV`, `DIVU`, `REM`, `REMU` | 10 | false | The RP2040's hardware divider peripheral, which an analogue of the Pico should reproduce |

An override replaces both the cycles and the soft flag of the listed opcodes on that core. The effect of the
single stock override is that on `K32` the whole `i32.div` category is never charged its table value (60,
soft): all four of its opcodes cost 10 cycles and are not soft.

## `service_classes` — extra cycles per service call

| Class | K8 | K32 | K32F | K32D |
| --- | --- | --- | --- | --- |
| `Light` | 20 | 10 | 10 | 10 |
| `Medium` | 60 | 30 | 30 | 30 |
| `Heavy` | 200 | 100 | 100 | 100 |

`"log_cycles_per_value": 10`.

## `category_rows` — which opcodes belong to which category (prose, for the in-game reference)

| Categories | Opcodes (table text) |
| --- | --- |
| `stack` | `NOP`, `BRK`, `TRAP`, `PUSH8`, `PUSH16`, `DUP`, `DROP`, `SWAP`, `OVER` |
| `const32` | `PUSH32` |
| `stack64` | `PUSH64`, `DUP2`, `DROP2`, `SWAP2`, `OVER2` |
| `mem8`, `mem16`, `mem32` | Byte, half-word and word loads and stores (`LDL`, `STL`, `LDG` and `STG` are word forms; there are no 8- or 16-bit local or global forms) |
| `mem64` | 64-bit loads and stores |
| `addr` | `ADDRL`, `INDEX` |
| `bounds` | `BOUNDS` |
| `block` | `COPY`, `ZERO` (base + per word × ⌈n/4⌉) |
| `branch` | `JMP`, `JZ`, `JNZ` |
| `call`, `callf`, `ret`, `enter` | `CALL`, `CALLF`, `RET`, `ENTER` (base + per word × (frame − 4·ncells)/4, §4.5 rule 5). `call` also prices the implicit call that starts a task (§3.1) |
| `i32.alu` | `ADD`, `SUB`, `NEG`, `AND`, `OR`, `XOR`, `NOT`, 32-bit comparisons, sign and zero extension |
| `i32.shift`, `i32.mul`, `i32.div` | Shifts; `MUL`; `DIV`, `DIVU`, `REM`, `REMU` |
| `i64.alu`, `i64.shift`, `i64.mul`, `i64.div`, `i64.cmp` | The 64-bit integer equivalents |
| `f32.add`, `f32.mul`, `f32.div`, `f32.sqrt`, `f32.cmp`, `f32.sign` | `FADD`/`FSUB`; `FMUL`; `FDIV`; `FSQRT`; float comparisons; `FNEG`/`FABS` |
| `f64.add`, `f64.mul`, `f64.div`, `f64.sqrt`, `f64.cmp`, `f64.sign` | The `D…` equivalents |
| `cvt.i32f32`, `cvt.f32f64`, `cvt.i32i64`, `cvt.i64f` | 32-bit int/float conversions; `F2D`/`D2F`; 32/64-bit integer conversions; conversions between 64-bit integers or `double` and other types |
| `yield` | `YIELD` |
| `syscall` | `SYSCALL` base cost. Each service adds the cycles of its cost class (§4.8) |

The authoritative opcode → category mapping is the `category` key of each `opcodes.json` row; see
[opcodes](/vm/opcodes.md).

## `table_rows` — rendering groups

Used only to lay out the cost table in the in-game reference; the loader checks that the rows together list
every category exactly once.

| # | Categories | `label_suffix` | `list_values` |
| --- | --- | --- | --- |
| 0 | `stack` | — | — |
| 1 | `const32` | — | — |
| 2 | `stack64` | — | — |
| 3 | `mem8` | — | — |
| 4 | `mem16` | — | — |
| 5 | `mem32` | — | — |
| 6 | `mem64` | — | — |
| 7 | `addr` | — | — |
| 8 | `bounds` | — | — |
| 9 | `block` | ` (base + per 4-byte word)` | — |
| 10 | `branch` | — | — |
| 11 | `call`, `callf`, `ret` | — | true |
| 12 | `enter` | ` (base + per 4-byte word zeroed)` | — |
| 13 | `i32.alu` | — | — |
| 14 | `i32.shift` | — | — |
| 15 | `i32.mul` | — | — |
| 16 | `i32.div` | — | — |
| 17 | `i64.alu`, `i64.cmp` | — | — |
| 18 | `i64.shift` | — | — |
| 19 | `i64.mul` | — | — |
| 20 | `i64.div` | — | — |
| 21 | `f32.add` | — | — |
| 22 | `f32.mul` | — | — |
| 23 | `f32.div` | — | — |
| 24 | `f32.sqrt` | — | — |
| 25 | `f32.cmp` | — | — |
| 26 | `f32.sign` | — | — |
| 27 | `f64.add` | — | — |
| 28 | `f64.mul` | — | — |
| 29 | `f64.div` | — | — |
| 30 | `f64.sqrt` | — | — |
| 31 | `f64.cmp` | — | — |
| 32 | `f64.sign` | — | — |
| 33 | `cvt.i32f32` | — | — |
| 34 | `cvt.f32f64` | — | — |
| 35 | `cvt.i32i64` | — | — |
| 36 | `cvt.i64f` | — | — |
| 37 | `yield` | — | — |
| 38 | `syscall` | ` (base)` | — |

# From table to `CoreCostTable`

`GameTables.BuildCostTables` builds one `CoreCostTable` per core (`GameTables.cs:272-390`):[^game-tables][^core-cost-table]

| Member | Content |
| --- | --- |
| `Core`, `CoreIndex` | Core name and its index in `cores`. |
| `int[] Cycles` | Per category: `cycles`, or `base` for `block` and `enter`. |
| `int[] PerWord` | Per category: `per_word` (0 except `block`, `enter`). |
| `bool[] Soft` | Per category soft flag. |
| `string?[] Family` | Per category: the routine family charged when soft (from `routines.json`). |
| `(int Cycles, bool Soft)?[256] OpcodeOverride` | Per opcode value. |
| `Dictionary<string,int> FamilyBytes` | Routine family → code bytes on this core. |
| `Dictionary<string,int> ServiceClassCycles` | `Light`/`Medium`/`Heavy` → cycles. |
| `int LogCyclesPerValue` | 10. |
| `uint Hash` | CRC-32 of `CanonicalCostText(core)`; stored in images as `cost_table_hash`. |

Validation performed while building (each failure throws `GameTablesException`): only `block` and `enter` may
(and must) use `base` + `per_word`; no negative cost; **every category and every override must cost at least
1 cycle**; a soft category needs a routine in `routines.json` whose family has a byte footprint on that core;
an override must name a known opcode and core.

`CanonicalCostText(core)` is one line per category — `name=cycles` or `name=base+per_word`, with `,S`
appended when soft — followed by one `opcode:MNEMONIC=cycles[,S]` line per override for that core
(`GameTables.cs:392-428`). Service classes and `log_cycles_per_value` are **not** part of the hash.

# The cost of one instruction — `CostModel.Cost`

```csharp
public static (int Cycles, bool Soft) Cost(GameTables tables, CoreCostTable t, OpcodeInfo info,
                                           long a, int b, int maxLogValues)
```

(`CostModel.cs:8-40`)[^cost-model]

1. If `t.OpcodeOverride[info.Code]` is set, return it.
2. Otherwise start from `t.Cycles[category]` and add, by category:

| Category | Addition | Operands |
| --- | --- | --- |
| `block` (`COPY`, `ZERO`) | `PerWord × ⌈a / 4⌉` | `a` = byte count `n` |
| `enter` (`ENTER`) | `PerWord × max(0, b/4 − a)` | `a` = `ncells`, `b` = frame bytes: the words that have to be zeroed |
| `syscall` (`SYSCALL`) | `ServiceClassCycles[service.CostClass]`, plus `LogCyclesPerValue × maxLogValues` if the service has `per_value` | `a` = service id; nothing is added if the id is not in `GameTables.Services` |

3. Clamp to `int.MaxValue`; the soft flag is `t.Soft[category]`.

`maxLogValues` is the **largest value count of any format string in the image** (0 if the image has no
format strings), not the count of the format actually passed (`ImageReader.cs:547`); a format holds at most
`LogFormat.MAX_VALUES` = 8 values.[^log-format] So every `log` call in an image costs the same.

Resulting service-call prices (syscall base + class):

| Cost class | K8 | K32 / K32F / K32D |
| --- | --- | --- |
| `Light` | 20 + 20 = 40 | 10 + 10 = 20 |
| `Medium` | 20 + 60 = 80 | 10 + 30 = 40 |
| `Heavy` | 20 + 200 = 220 | 10 + 100 = 110 |
| `log` (`Medium`, `per_value`) | 80 + 10 × maxLogValues | 40 + 10 × maxLogValues |

`ServiceClassCycles[...]` is a dictionary indexer: a `ServiceInfo` whose `CostClass` is not one of the three
classes makes `CostModel.Cost` throw `KeyNotFoundException` during image load (that exception type is not
among those `ImageReader.Read` converts to a `LoadResult`, `ImageReader.cs:48-51`).

# Baking costs at load

For every decoded instruction the loader calls `CostModel.Cost` and stores the result
(`ImageReader.cs:553-589`):[^image-reader]

- rule `cost`: an instruction may not cost more than the tick budget, nor less than 1;
- `Instr.Cost = cycles`; if soft, `Instr.Soft = category index` (else −1) and the category's routine family
  is added to a set;
- rule `runtime-lib`: the image's `runtime_lib_size` must equal the sum of `FamilyBytes` over the families
  used;
- rule `program-memory`: `code_size + data_init_size + runtime_lib_size <= machine_code`.

Because costs are baked, changing a `CoreCostTable` in memory affects only images loaded afterwards.

# Charging at run time

For each instruction the interpreter computes `charge = Instr.Cost` (+ publication copies on the completing
`RET` or a program `YIELD`), checks it against the remaining budgets, then adds it to `_cycles` **before**
executing the instruction (`KvmMachine.cs:791-813`).[^kvm-machine] Other charges:

| Charge | Amount | When |
| --- | --- | --- |
| Task start | `Cycles[CALL's category]` (K8 6, K32/K32F 3, K32D 2) | Every fresh start of a task (`:696-718`). |
| Subscription copies | Σ over the task's subscribed messages of `block.base + block.per_word × ⌈(16 + body)/4⌉` | Every fresh start **and every resume** (`:697, 743-769`). |
| Publication copies | Same formula over the task's published messages | With the `RET` that completes the task, or each `YIELD` of the program task (`:792, 1078-1088, 1112-1118`). |

The copy formula is `BusSteps.CopyCost`; `BusSteps.Of` also returns the totals the loader compares with the
tick budget (`Start = Call + SubCopies`, `Completion = max(RET, YIELD for a program) + PubCopies`).[^bus-steps]

`sys.cycles` (system block offset 0) exposes the running total and `sys.budget` the limit.

## What happens when cycles run out

| Situation | Result |
| --- | --- |
| Periodic task cannot afford its next instruction (or its start step) | **Overrun**: `_cycles = budget`, task abandoned, tick not latched, requests dropped, `overruns++`. |
| `init` or `program` task cannot afford its next instruction | Suspends; continues next tick. |
| Program task reaches `budget × ProgramShare / 100` cycles this tick (`ProgramShare > 0`) | Suspends (never before executing at least one instruction). |
| `init` task's cumulative cycles would exceed `InitBudgetCycles` | Trap `INIT_OVERRUN`. |

`TickResult.CyclesUsed` reports `_cycles`; the host records it as a performance series and counts overruns
(see [snapshots and perf](/host/snapshots-and-perf.md)).

# Modding notes

- **A new service needs no cost-table change** as long as its `CostClass` is `Light`, `Medium` or `Heavy`.
  Any other string crashes image loading (see above).
- `GameTables.CostTables` (public getter) holds mutable `CoreCostTable` objects with `init`-only properties
  but mutable arrays and dictionaries. `Inferred:` (from `CostModel.Cost`; not exercised) adding a key to
  `ServiceClassCycles` at run time would make a new
  cost class usable without touching the hash; it must be done for all four cores (and
  `PartDescription.Costs` references the same objects). A row loaded through `GameTables.Load` must still
  name a class listed in `costs.json` `service_classes`, or `BuildServices` rejects it as incomplete
  (`GameTables.cs:540, 568`).
- The image's `cost_table_hash` covers only category costs and overrides. Changing those in `costs.json`
  (if a mod supplied its own tables) invalidates every existing image for that core.
- Costs are the only "time" the VM knows. A mod service that does heavy host work costs the program just its
  class cycles; the real work is done by the host outside the cycle budget.
- Every instruction must cost ≥ 1, so a tick always terminates: the budget is finite and nothing is free.

[^costs-json]: KSA.Kfs.Vm.Tables.Data.costs.json:1-94 (`source` at line 3, `costs` 5-48, `table_rows` 49-64, `category_rows` 65-84, `overrides` 85-87, `service_classes` 88-92, `log_cycles_per_value` 93).
[^cost-model]: CostModel.cs:8-40.
[^bus-steps]: BusSteps.cs:9-44.
[^game-tables]: GameTables.cs:208-221 (`BuildCategories`), 272-390 (`BuildCostTables`), 392-428 (`CanonicalCostText`), 430-468 (`BuildParts`).
[^core-cost-table]: CoreCostTable.cs:5-28.
[^image-reader]: ImageReader.cs:547 (`maxLogValues`), 553-589 (cost baking, runtime-lib and program-memory rules), 755-776 (step costs, tick rate, budget and part rules).
[^kvm-machine]: KvmMachine.cs:671 (program share limit), 696-733 (start step), 743-769 (resume), 791-813 (per-instruction checks and charge), 1748-1757 (`Overrun`), 2176-2180 (overrun accounting).
[^catalogue-json]: KSA.Kfs.Vm.Tables.Data.catalogue.json:4-21.
[^log-format]: LogFormat.cs:14-18 (`MAX_VALUES = 8`, `MAX_BYTES = 64`, `MAX_WIDTH = 99`).
