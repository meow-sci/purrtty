---
type: API Reference
title: Compile report (CompileReport and the Report* records)
description: Every field of the build report the emitter produces — program memory, RAM, stacks, VM region, bus, calls, WCET and per-line estimates — with its JSON key, how it is computed, and the fixed text rendering.
resource: ksa-decomp:KSA.Kfs.Compiler/CompileReport.cs
tags: [kfs, compiler, report, json, wcet, memory]
sources:
  - id: compile-report
    resource: ksa-decomp:KSA.Kfs.Compiler/CompileReport.cs
    title: CompileReport (decompiled)
  - id: report-records
    resource: ksa-decomp:KSA.Kfs.Compiler/ReportProgram.cs
    title: Report* records (decompiled; ReportBus, ReportBusPublication, ReportBusTask, ReportByLanguage, ReportCalls, ReportCode, ReportDataInit, ReportFamily, ReportLibraryCode, ReportLibraryData, ReportMachine, ReportProgram, ReportRam, ReportRuntimeLib, ReportTaskStack, ReportTaskStacks, ReportVmRegion, ReportVmTask, ReportWcet)
  - id: report-json
    resource: ksa-decomp:KSA.Kfs.Compiler/ReportJsonContext.cs
    title: ReportJsonContext (source-generated serializer, decompiled)
  - id: line-cost
    resource: ksa-decomp:KSA.Kfs.Compiler/LineCost.cs
    title: LineCost (decompiled)
  - id: emitter
    resource: ksa-decomp:KSA.Kfs.Compiler.Backend/Emitter.cs
    title: Emitter (decompiled)
  - id: cli
    resource: ksa-decomp:KSA.Kfs.Cli/Cli.cs
    title: kvm-cli (decompiled)
  - id: report-view
    resource: ksa-decomp:KSA.Kfs.Host.Views/ReportView.cs
    title: ReportView (decompiled)
generated: { by: claude-code/claude-fable-5-2, at: 2026-11-25T21:29:02Z }
status: stable
ksa_build: "2026.11.25.5949"
---

# When a report exists

`Emitter.BuildReport` runs at the very end of a successful emission, and only when program analysis
ran — so a report accompanies every successful kC compile and every successful normal kASM assembly,
and is `null` for failed builds and for `kvm-cli assemble --image`.[^emitter-report-when] It is
surfaced as `CompileResult.Report` / `AssembleResult.Report`.

Consumers:

- `kvm-cli` writes it next to the image as `<image>.report.json` (`report.ToJson()`) and prints
  `report.Text` when `--report` is given;[^cli-report]
- the workbench's `ReportView` turns it into rows for the editor's report panel and joins `wcet` with
  measured figures.[^report-view]

# Serialisation

```csharp
public string ToJson() => JsonSerializer.Serialize(this, ReportJsonContext.Default.CompileReport) + "\n";
```

`ReportJsonContext` is a `System.Text.Json` source-generated context declared with
`[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, WriteIndented
= true)]` and `[JsonSerializable(typeof(CompileReport))]`.[^report-json] So: **snake_case keys, indented
output, properties in declaration order**, one trailing newline. `BusAccesses` is `[JsonIgnore]`;
`LineCost.StackDepth` is omitted when null (`JsonIgnoreCondition.WhenWritingNull`).

# Shape

Key order is exactly as written. Types in angle brackets; this is a schema sketch, not captured output.

```json
{
  "text": "<string: the rendered text report, see below>",
  "machine": { "name": "<string>", "core": "<string>", "clock_hz": <int>, "cycles_per_tick": <int> },
  "program": {
    "total": <int>, "limit": <int>,
    "code": {
      "total": <int>,
      "by_language": { "kc": <int>, "kscript": <int>, "kasm": <int> },
      "library": [ { "name": "<string>", "language": "<string>", "bytes": <int>, "first_call": "<string>" } ]
    },
    "data_init": { "total": <int>, "library": [ { "name": "<string>", "bytes": <int> } ] },
    "runtime_lib": { "total": <int>, "families": [ { "name": "<string>", "first_use": "<string>" } ] }
  },
  "ram": {
    "total": <int>, "limit": <int>, "globals": <int>, "bus_buffers": <int>,
    "task_stacks": {
      "total": <int>,
      "tasks": [ { "name": "<string>", "bytes": <int>, "deepest": ["<string>"], "overlaid": <bool> } ]
    },
    "vm_region": {
      "total": <int>,
      "tasks": [ { "name": "<string>", "cells": <int>, "entries": <int> } ],
      "bus_slots": <int>
    }
  },
  "calls": { "near": <int>, "far": <int> },
  "wcet": [ { "task": "<string>", "cycles": <int>, "budget": <int> } ],
  "tick_hz": <int>,
  "double_bits": <int>,
  "image_bytes": <int>,
  "peephole_off": <null | [<int>]>,
  "estimates": [ { "file": "<string>", "line": <int>, "cycles": <int>, "per_iteration": <bool>,
                   "soft_routines": ["<string>"], "stack_depth": <int, kASM lines only> } ],
  "bus": {
    "text": "<string>",
    "subscribes": ["<string>"],
    "publishes": [ { "message": "<string>", "task": "<string>" } ],
    "tasks": [ { "task": "<string>", "subscribes": ["<string>"], "publishes": ["<string>"],
                 "start_step": <int>, "completion_step": <int> } ]
  }
}
```

# Field meanings

Computed in `Emitter.BuildReport` unless noted.[^emitter-report]

## `CompileReport`

| C# member | JSON key | meaning |
| --- | --- | --- |
| `Text` | `text` | `CompileReport.Render(...)` output |
| `Machine` | `machine` | the part the image was built for |
| `Program` | `program` | program-memory accounting |
| `Ram` | `ram` | RAM accounting |
| `Calls` | `calls` | encoded near `CALL`s and far `CALLF`s |
| `Wcet` | `wcet` | one entry per task whose WCET is known |
| `TickHz` | `tick_hz` | the tick rate used |
| `DoubleBits` | `double_bits` | `Emitter.DoubleBits` (64 or 32; always 64 on the kASM route) |
| `ImageBytes` | `image_bytes` | length of the finished image |
| `PeepholeOff` | `peephole_off` | disabled peephole rules, sorted, or `null` when none |
| `Estimates` | `estimates` | `ProgramAnalysis.LineEstimates` — see [program analysis](/compiler/program-analysis-and-wcet.md#per-line-cost-estimates) |
| `Bus` | `bus` | bus summary; defaults to `"none"` with empty lists |
| `BusAccesses` | — (ignored) | `BusAnalysis.Accesses()` for editor markers |

## `ReportMachine`

| member | key | value |
| --- | --- | --- |
| `Name` | `name` | `part.Name` |
| `Core` | `core` | `part.Costs.Core` (`K8`, `K32`, `K32F`, `K32D`) |
| `ClockHz` | `clock_hz` | `part.ClockHz` |
| `CyclesPerTick` | `cycles_per_tick` | `part.ClockHz / tickHz` |

## `ReportProgram` and children

| record.member | key | value |
| --- | --- | --- |
| `ReportProgram.Total` | `total` | code + data init + runtime library bytes — the quantity limited by the part |
| `ReportProgram.Limit` | `limit` | `part.CodeBytes` |
| `ReportCode.Total` | `code.total` | encoded code bytes |
| `ReportByLanguage.Kc` / `.Kscript` / `.Kasm` | `by_language.kc` / `.kscript` / `.kasm` | sum of function sizes (each including its 4-byte `ENTER`) by `IrFunction.Language` |
| `ReportLibraryCode` | `code.library[]` | one per function from a library file (file name starts with `<`): `name`, `language`, `bytes`, `first_call` = `file:line` of the first call to it in placement order, or `"(entry)"` |
| `ReportDataInit.Total` | `data_init.total` | bytes of initialised-global data records |
| `ReportLibraryData` | `data_init.library[]` | library globals with initial data: `name`, `bytes` |
| `ReportRuntimeLib.Total` | `runtime_lib.total` | `ImageBuilder.RuntimeLibCharge` — Σ `FamilyBytes` of the soft-routine families the code uses on this core |
| `ReportFamily` | `runtime_lib.families[]` | `name` = family id (`div32`, `int64`, `float32`, `float64`, `sqrt`), `first_use` = `file:line` of the first instruction needing it |

## `ReportRam` and children

| record.member | key | value |
| --- | --- | --- |
| `ReportRam.Total` | `total` | the header's `ram_required`: end of the RAM-stack area + VM region size |
| `ReportRam.Limit` | `limit` | `part.RamBytes` |
| `ReportRam.Globals` | `globals` | address where stacks start = end of globals rounded up to 8 (includes the 16-byte null guard) |
| `ReportRam.BusBuffers` | `bus_buffers` | Σ sizes of the distinct buffers named by bus declarations (already counted inside `globals`) |
| `ReportTaskStacks.Total` | `task_stacks.total` | bytes of the RAM-stack area |
| `ReportTaskStack` | `task_stacks.tasks[]` | `name`; `bytes` = the task's RAM stack size; `deepest` = its deepest call path (function names); `overlaid` = true for an init task (its stack shares the other tasks' space), except when there are other tasks and the init stack is larger than all of theirs combined |
| `ReportVmRegion.Total` | `vm_region.total` | VM-stack bytes + message slots, each rounded to 8 |
| `ReportVmTask` | `vm_region.tasks[]` | `name`, `cells` = operand cells reserved, `entries` = return entries reserved |
| `ReportVmRegion.BusSlots` | `vm_region.bus_slots` | Σ message slot bytes |

## `ReportCalls`, `ReportWcet`

| record | keys | value |
| --- | --- | --- |
| `ReportCalls(Near, Far)` | `near`, `far` | counts of `Function`-operand instructions encoded as `CALL` / `CALLF` |
| `ReportWcet(Task, Cycles, Budget)` | `task`, `cycles`, `budget` | entry name; worst-case cycles including the entry call and bus copies; `InitBudgetCycles` for init, cycles per tick otherwise. Program tasks and tasks with an unbounded loop have no entry |

## `ReportBus` and children

See [bus analysis](/compiler/bus-analysis.md#report-shapes). `subscribes` and
`publishes[].message` are message names without the `MSG_` prefix; `tasks[].subscribes` /
`.publishes` are **buffer** names; `start_step` / `completion_step` are cycles.

# Text rendering

`CompileReport.Render(machine, program, ram, calls, wcet, busText)` builds a fixed layout with
invariant-culture `N0` numbers (comma thousands separators). Bracketed parts appear only when
non-empty.[^compile-report-render]

```text
Machine     <name> (<core>, <clock MHz, 0.###> MHz game clock, <cycles per tick> cycles per tick)
Program     <total> / <limit> bytes
  code       <total, width 9>   (kC <n>, kScript <n>, kASM <n>[; library <n> in <kC|kScript|kASM>: <fn> ← <first call>, …])
  data init  <total, width 9>[   (library <n>: <global>, …)]
  runtime lib<total, width 9>   none | soft <family> ← first use <file:line>, …
RAM         <total> / <limit> bytes
  globals    <globals, width 9>   (bus buffers <n>)
  task stacks<total, width 9>   (<task> <bytes>[, deepest: a → b → c][ overlaid]; …)
  VM region  <total, width 9>   (operand cells: <task> <n>, …; return entries: <task> <n>, …; bus slots <n>)
Bus         <bus text>
Calls       <near> near, <far> far
WCET        <task> <cycles> / <budget> cycles          ← one line per ReportWcet
```

Only languages with a non-zero byte count are listed on the `code` line; library functions are grouped
by language.

# Modding notes

- The report is the cheapest way for a mod to learn a built program's figures (addresses aside): it is
  a plain public record graph, and `ToJson()` is public.
- `text` is a rendering of the other fields; do not parse it.
- Adding a field to the JSON would mean patching a source-generated serializer; attach extra data
  beside the report instead.
- `wcet` entries can be missing for a task — treat absence as "unknown", not zero.

[^emitter-report-when]: Emitter.cs:465-467
[^emitter-report]: Emitter.cs:664-736
[^compile-report-render]: CompileReport.cs:23-209
[^report-json]: ReportJsonContext.cs:11-14, 90-94, 628-657, 945-956
[^cli-report]: Cli.cs:390-401
[^report-view]: ReportView.cs:75-78
