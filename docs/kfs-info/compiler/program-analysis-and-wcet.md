---
type: Subsystem Reference
title: Program analysis, stack figures and WCET
description: What ProgramAnalysis proves about a program before an image is built — operand-stack balance, no recursion, per-task stack and call-depth figures, YIELD placement, worst-case execution time — what is rejected at compile time versus only reported, and how the figures relate to the processor catalogue.
resource: ksa-decomp:KSA.Kfs.Compiler/ProgramAnalysis.cs
tags: [kfs, compiler, analysis, wcet, stack, budget, limits, catalogue]
sources:
  - id: analysis
    resource: ksa-decomp:KSA.Kfs.Compiler/ProgramAnalysis.cs
    title: ProgramAnalysis (decompiled)
  - id: function-analysis
    resource: ksa-decomp:KSA.Kfs.Compiler/FunctionAnalysis.cs
    title: FunctionAnalysis (decompiled)
  - id: task-figures
    resource: ksa-decomp:KSA.Kfs.Compiler/TaskFigures.cs
    title: TaskFigures (decompiled)
  - id: line-cost
    resource: ksa-decomp:KSA.Kfs.Compiler/LineCost.cs
    title: LineCost (decompiled)
  - id: limits
    resource: ksa-decomp:KSA.Kfs.Compiler/TranslationLimits.cs
    title: TranslationLimits (decompiled)
  - id: limit-exception
    resource: ksa-decomp:KSA.Kfs.Compiler/LimitException.cs
    title: LimitException (decompiled)
  - id: cost-model
    resource: ksa-decomp:KSA.Kfs.Vm.Image/CostModel.cs
    title: CostModel (decompiled)
  - id: bus-steps
    resource: ksa-decomp:KSA.Kfs.Vm.Image/BusSteps.cs
    title: BusSteps (decompiled)
  - id: emitter
    resource: ksa-decomp:KSA.Kfs.Compiler.Backend/Emitter.cs
    title: Emitter (decompiled)
  - id: assembler
    resource: ksa-decomp:KSA.Kfs.KAsm/Assembler.cs
    title: kASM Assembler (decompiled)
  - id: game-tables
    resource: ksa-decomp:KSA.Kfs.Vm.Tables/GameTables.cs
    title: GameTables (decompiled)
  - id: catalogue-json
    resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.catalogue.json
    title: catalogue.json (embedded table)
  - id: costs-json
    resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.costs.json
    title: costs.json (embedded table)
  - id: diagnostics-json
    resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.diagnostics.json
    title: diagnostics.json (embedded table)
  - id: kc-checks
    resource: ksa-decomp:KSA.Kfs.KC/Checks.cs
    title: kC Checks (decompiled)
generated: { by: claude-code/claude-fable-5-2, at: 2026-11-25T21:29:02Z }
status: stable
ksa_build: "2026.11.25.5949"
---

# Where it runs

`Emitter.Emit` constructs `new ProgramAnalysis(tables, part, diags, module)` and calls
`Run(orderedTasks, tickHz, maxLogValues)` right after ordering the tasks, unless the emitter was created
with `AllowRecursion` (the assembler's `ImageMode`).[^emitter-analysis] It works on the
[IR](/compiler/ir-reference.md) of every function in the module, both languages alike.

```csharp
public sealed class ProgramAnalysis {
    public Dictionary<string, FunctionAnalysis> Functions { get; }
    public List<TaskFigures> Tasks { get; }
    public bool Ok { get; }
    public IrFunction? Program { get; }          // the program-task entry, if any
    public bool Run(IReadOnlyList<IrFunction> orderedTasks, int tickHz, int maxLogValues);
    public List<LineCost> LineEstimates(int maxLogValues);
}
```

`Run` executes six passes in this order: `Analyse` (per function) → `Recursion` → *(stop here if any
error)* → `Figures` → `Yields` → `Lints` → `Wcet`.[^analysis-run]

A design point that recurs below: the same structural fault is a **user error in kASM** and an
**internal compiler error in kC**. `FunctionAnalysis.Prefix` is `KC` for `Language == "kc"`, `KS` for
`"ks"`, else `KA`; for non-`KA` functions violations are re-reported as number 900 "internal compiler
error: the lowering is unbalanced (…)".[^analysis-violation]

# Pass 1 — `Analyse`: operand-stack balance

A worklist walk over the instruction list, tracking the operand-stack height from 0 at entry, exactly
like `IrValidator` but with diagnostics.[^analysis-analyse]

| condition | kASM diagnostic | kC diagnostic |
| --- | --- | --- |
| the function has no instructions | 333 "'f' has no instructions; it can't return" | (same call; does not arise) |
| a path runs off the end | 333 "'f' can fall off its end without RET" | 900 |
| `RET n` with `n` ≠ the function's result cells | 331 | 900 |
| height at `RET` ≠ result cells | 331 "… with N cells on the operand stack; it returns M" | 900 |
| `SYSCALL id` with `id` not a key of `tables.Services` | **101 "SYSCALL N is not a service in the table"**, hint "Use an `SVC_*` symbol from <kfs.kinc>." | 900 |
| an `ENTER` in a body | 12 | 900 |
| pops below the entry level | 332 | 900 |
| `JMP`/`JZ`/`JNZ` to an unknown label | 5 | 900 |
| two paths reach an instruction with different heights | 330 | 900 |

Side products per function: `HeightAfter[i]` (height after each instruction), `MaxHeight`, the list of
`CallSites` `(Callee, HeightBefore, At, Far)`, and `YieldsDirectly`. A `CALL` to a function that is not
in the module is treated as popping and pushing nothing here; the emitter has already reported it
(304).

# Pass 2 — `Recursion`

An iterative Tarjan strongly-connected-components search over the call graph. Any component with more
than one function, or a function that calls itself, is **error 301**:

> `'a' calls itself through 'b' (a → b → a)` — hint: *Flight code can't use recursion, because the
> stack size has to be known before launch. Turn the recursion into a loop.*

The diagnostic is attached to the component's first function by file then line.[^analysis-recursion]
(The kC front end runs its own `Checks.Recursion` on the AST first; this pass is what catches kASM
and mixed-language cycles.) If any error has been reported so far, `Run` returns `false` and the
remaining passes are skipped.

# Pass 3 — `Figures`: static stack sizes

Computed bottom-up over the (now acyclic) call graph:[^analysis-figures]

| figure | definition |
| --- | --- |
| `RamStack(f)` | `Frame(f)` + max over callees of `RamStack(callee)` — bytes of RAM stack |
| `Entries(f)` | 1 + max over callees of `Entries(callee)` — return-stack entries |
| `Cells(f)` | max( `MaxHeight(f)`, max over call sites of `HeightBefore − ArgCells(callee) + Cells(callee)` ) — operand cells |
| `DeepestPath(f)` | `f` followed by the deepest path of the callee with the largest `RamStack` |

A task's figures are its entry function's. The emitter then enforces
`Entries ≤ part.MaxReturnEntries` and `Cells ≤ part.MaxOperandCells` (701, with the deepest path in the
message) and sizes the task's RAM stack and VM stack from them — see
[backend emitter](/compiler/backend-emitter.md#ram-plan).

# Pass 4 — `Yields`

`Suspends` is the transitive closure of "contains a `YIELD`" through callers; it is copied to
`IrFunction.Suspends`. `Program` is the first `Program` entry. For every `Init` or `Periodic` entry
that suspends: **error 404** at the offending `YIELD`:

> `YIELD in 'g' is reachable from the periodic entry 'f' (f → g)` — hint: *Only the program task can
> suspend; move the loop into the `.program` function or stop calling it from the other tasks.*[^analysis-yields]

# Pass 5 — `Lints` (kASM only)

For non-library kASM functions: after `PUSH32 <global>`, an `INDEX size` reached with a net stack
change of exactly +1 and no `BOUNDS` in between is **warning `KA-W260`** "indexing 'g' (N elements of S
bytes) without a BOUNDS check", with the hint "Add `BOUNDS N` before the INDEX, or `.bounds off` to
silence the lint."[^analysis-lints]

`Not active in this build:` the hint's `.bounds off` does not silence anything — the assembler accepts
`.bounds` and `.strict` and ignores them.[^assembler-noop]

# Pass 6 — `Wcet`: worst-case cycles

## Per function

`FunctionWcet` returns `long?` — a cycle count, or `null` for "unknown".[^analysis-wcet]

1. **Loop regions.** Each `IrLoop` begin/end pair (kC) is a region `(firstInstr, endInstr, Bound,
   Exit: true)`. Each kASM `.loop_bound label, N` is a region from the label to just past the *last*
   jump to that label, `(labelIndex, lastJump + 1, N, Exit: false)`.
2. **Instruction costs.** `CostModel.Cost(tables, part.Costs, opcode, operand, operand2,
   maxLogValues).Cycles`; a `CALL`/`CALLF` adds the callee's WCET. An unknown callee WCET makes this
   function's unknown.
3. **Back edges.** A branch to an earlier or the same instruction is a back edge. Every back edge must
   lie inside at least one region, and every region containing it must have a bound; otherwise the
   WCET is unknown.
4. **Longest path.** `ENTER`'s cost plus the longest path through the forward-edge DAG, where each
   outermost region collapses to one node costing `Bound × max(1, longest path inside)` plus, for kC
   regions (`Exit`), the longest path to an instruction that leaves the region. Nested regions recurse.

An unbounded loop is **not an error**:

- kASM: **warning `KA-W711`** "the loop back to this jump's target in 'f' has no declared bound" — hint
  "Add `.loop_bound label, N` for the loop's head label." It is suppressed for a function that
  suspends and is reachable only from a suspending program task (a program's main loop needs no
  bound).
- kC: the front end's `Checks` reports `KC-W711` "can't prove this loop ends" from the AST;[^kc-checks]
  the kC lowering emits the `IrLoop` marker with `Bound = null` and `Source = "none"`.

In both cases the function's — and therefore the task's — WCET is simply absent.

## Per task

For each task in order `Wcet` appends a `TaskFigures`:[^analysis-tasks]

| field | value |
| --- | --- |
| `Entry` | the entry `IrFunction` |
| `RamStack`, `Entries`, `Cells`, `DeepestPath` | the entry function's figures |
| `Wcet` | `null` for a `Program` task or an unknown function WCET; else function WCET + the cost of one `CALL` |
| `Budget` | `part.InitBudgetCycles` for `Init`; otherwise `part.ClockHz / tickHz` |

After [bus analysis](/compiler/bus-analysis.md) the emitter adds the task's subscription and
publication copy costs to `Wcet`.[^emitter-wcet]

## What nothing does: compare WCET with the budget

No code compares `TaskFigures.Wcet` with `TaskFigures.Budget`. Both are carried into the
[compile report](/compiler/compile-report.md) (`wcet[].cycles` / `.budget`) and displayed. The
diagnostics table has rule 702 "Worst-case cycles exceed the task budget" as `E, strict only` with no
default letter, and no code emits it. `Not active in this build:` a program whose WCET exceeds its tick
budget compiles and loads; overruns are a run-time matter
([cost model and budgets](/vm/cost-model-and-budgets.md)).

# Compile-time rejection summary

| property | outcome | number | where |
| --- | --- | --- | --- |
| recursion (direct or mutual, any language mix) | **error** | 301 | `ProgramAnalysis.Recursion` (and kC `Checks.Recursion`) |
| unbounded loop | warning; WCET becomes unknown | 711 | `FunctionWcet` (kASM), kC `Checks` |
| WCET over the tick/init budget | **not checked** | (702 unused) | — |
| one instruction costing more than a tick | **error** | 718 | `Emitter` |
| a task's start or completion step (call/return + bus copies) costing more than a tick | **error** | 718 | `BusAnalysis` |
| `YIELD` reachable from init or a periodic task | **error** | 404 | `ProgramAnalysis.Yields` |
| operand-stack imbalance, falling off the end, bad `RET` | **error** | 330–333 (kASM) / 900 (kC) | `ProgramAnalysis.Analyse` |
| unknown service id | **error** | 101 (kASM) / 900 (kC) | `ProgramAnalysis.Analyse`; `IrValidator` first on the kC route |
| call depth over `MaxReturnEntries`, operand cells over `MaxOperandCells` | **error** | 701 | `Emitter` |
| more tasks than `MaxTasks` | **error** | 701 | `Emitter` |
| globals, stacks and VM region over `RamBytes` | **error** | 701 | `Emitter` |
| code + data init + runtime library over `CodeBytes` | **error** | 701 | `Emitter` |
| frame over 65,528 bytes / more than 255 argument cells | **error** | 717 / 719 | assembler, kC lowering |
| array index without `BOUNDS` (kASM) | warning | 260 | `ProgramAnalysis.Lints` |

In `ImageMode` (`kvm-cli assemble --image`) none of `ProgramAnalysis` runs, `CheckProgram` is skipped
and bus checks are off; such an image is only held to `ImageReader`'s rules.

# Cost model

`CostModel.Cost(tables, CoreCostTable t, OpcodeInfo info, long a, int b, int maxLogValues)` returns
`(int Cycles, bool Soft)`:[^cost-model]

1. If the core has a per-opcode override (`t.OpcodeOverride[code]`), that is the answer.
2. Otherwise start from `t.Cycles[category]` and add by category:
   - `block` (`COPY n`, `ZERO n`): `PerWord × ⌈a / 4⌉`
   - `enter`: `PerWord × max(0, b/4 − a)` — the frame words beyond the arguments (the ones zeroed)
   - `syscall`: if `a` is a known service id, `ServiceClassCycles[service.CostClass]`, plus
     `LogCyclesPerValue × maxLogValues` when the service is `PerValue`
3. Clamp to `int.MaxValue`; `Soft` = the category is a soft routine on this core.

`maxLogValues` is the largest value count of any format string in the program, so every per-value
service call is costed at the program's widest format.

`BusSteps` prices the scheduler's implicit work:[^bus-steps]

| function | value |
| --- | --- |
| `CopyCost(tables, t, slotBytes)` | `block` base + `PerWord × ⌈slotBytes / 4⌉` |
| `CallCost(tables, t)` | the `call` category's cycles |
| `Of(tables, t, task, messages, links)` | `BusStepCosts(Start = Call + SubCopies, Completion = Complete + PubCopies, SubCopies, PubCopies, Call, Complete)`, where `Complete` = `RET` cost, or max(`RET`, `YIELD`) for a program task; slot bytes = 16 + `BodySize` |

Service cost classes in `costs.json` (cycles added on top of the `syscall` category: 20 on K8, 10 on
the others); `log_cycles_per_value` is 10:[^costs-json]

| class | K8 | K32 | K32F | K32D |
| --- | --- | --- | --- | --- |
| `Light` | 20 | 10 | 10 | 10 |
| `Medium` | 60 | 30 | 30 | 30 |
| `Heavy` | 200 | 100 | 100 | 100 |

The full per-category table is in [cost model and budgets](/vm/cost-model-and-budgets.md).

# Budgets and the processor catalogue

`PartDescription(Name, PartIndex, CoreClass, Costs, ClockHz, RamBytes, CodeBytes, NvBytes,
InitBudgetCycles, MaxOperandCells, MaxReturnEntries, MaxTasks, Features)` is built from `catalogue.json`
by `GameTables.BuildParts`:[^game-tables-parts]

- `InitBudgetCycles = defaults.init_budget_seconds × clock_hz`
- `MaxOperandCells`, `MaxReturnEntries`, `MaxTasks` = the `k8_*` defaults for a `K8` core, else the
  plain defaults
- `Features = 0` for every part
- `Costs` = the `CoreCostTable` of the part's core

Stock catalogue (`version` 1; `allowed_tick_hz` = 5, 10, 20, 25, 50, 100; `default_tick_hz` = 50;
`defaults`: `init_budget_seconds` 2, `max_operand_cells` 64, `max_return_entries` 16, `max_tasks` 16,
`k8_max_operand_cells` 16, `k8_max_return_entries` 8, `k8_max_tasks` 8, `double_bits` 64):[^catalogue-json]

| index | part | core | clock Hz | RAM bytes | program bytes | NV bytes | operand cells | return entries | tasks |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 0 | Sequencer | K8 | 1,000,000 | 2,048 | 32,768 | 1,024 | 16 | 8 | 8 |
| 1 | Flight Computer Mk1 | K32 | 1,000,000 | 65,536 | 262,144 | 0 | 64 | 16 | 16 |
| 2 | Flight Computer Mk2 | K32F | 1,000,000 | 131,072 | 524,288 | 0 | 64 | 16 | 16 |
| 3 | Flight Computer Mk2-RH | K32F | 1,000,000 | 65,536 | 262,144 | 0 | 64 | 16 | 16 |
| 4 | Flight Computer Mk3 | K32D | 1,000,000 | 262,144 | 1,048,576 | 0 | 64 | 16 | 16 |
| — | Bench (`bench_part`, based on Mk2) | K32F | as Mk2 | as Mk2 | as Mk2 | as Mk2 | 64 | 16 | 16 |

With a 1 MHz clock the per-tick budget `ClockHz / tick_hz` is 200,000 / 100,000 / 50,000 / 40,000 /
20,000 / 10,000 cycles at 5 / 10 / 20 / 25 / 50 / 100 Hz, and the init budget is 2,000,000 cycles.
`PartIndex == 0` (the Sequencer) is additionally barred from `.claim ALLOCATION` and from publishing
the GNC messages (520).

# Per-line cost estimates

`LineEstimates(maxLogValues)` produces the editor's cost column: one `LineCost` per source line that
has code, in first-seen order.[^analysis-lines]

```csharp
public sealed record LineCost(string File, int Line, long Cycles, bool PerIteration,
                              List<string> SoftRoutines, int? StackDepth);   // StackDepth omitted from JSON when null
```

- `Cycles` — sum of the costs of the instructions attributed to the line (a function's `ENTER` is
  charged to the function's definition line). Call costs here are the call instruction only, not the
  callee.
- `PerIteration` — some instruction on the line lies between a backward branch and its target.
- `SoftRoutines` — names of the soft routines the line's instructions use on this core
  (`SoftRoutines.Name`).
- `StackDepth` — kASM lines only: the operand height after the line's last instruction.

# Supporting types

`FunctionAnalysis` (one per function):[^function-analysis]

| member | meaning |
| --- | --- |
| `Function`, `Name`, `Prefix`, `IsLibrary` | the IR function; `IsLibrary` = file name starts with `<` |
| `HeightAfter`, `MaxHeight` | operand heights |
| `CallSites` | `(Callee, HeightBefore, At, Far)` |
| `Violations` | declared, never filled in this build |
| `RamStack`, `Entries`, `Cells`, `DeepestPath` | stack figures |
| `YieldsDirectly`, `Suspends` | `YIELD` reachability |
| `Wcet`, `WcetComputed` | worst-case cycles (nullable) |
| `CodeBytes` | declared, never assigned in this build |

`TaskFigures`: `Entry`, `RamStack`, `Entries`, `Cells`, `DeepestPath`, `Wcet`, `Budget`.[^task-figures]

`TranslationLimits` holds the translation-limit constants (listed in
[compile pipeline](/compiler/compile-pipeline.md#limits)) and `EnsureStack(Span? at = null)`, which
throws `LimitException(720, "nesting too deep for the compiler's stack", at)` when
`RuntimeHelpers.TryEnsureSufficientExecutionStack()` fails. `LimitException(int number, string message,
Span? at = null)` carries a diagnostic number out of deep recursion; `Compiler` and `Assembler` catch
it and turn it into one error.[^limits]

# Modding notes

- **A new service is costed from its `CostClass`.** `CostModel.Cost` indexes
  `ServiceClassCycles[service.CostClass]` with no fallback, so a `ServiceInfo` added by a mod must use
  one of the classes in `costs.json` (`Light`, `Medium`, `Heavy`) or analysis, emission and image
  loading throw `KeyNotFoundException`. A row supplied as JSON through `GameTables.Load` — the
  tested route, see [adding a service](/modding/adding-a-service.md) — is instead refused when the
  tables load ("service '<name>' is incomplete").
- **`ArgCells`/`ResultCells` of the `ServiceInfo` drive stack analysis.** They must match what the
  game-side handler pops and pushes; the VM uses the same numbers at run time.
- **Budgets are per part, from the catalogue.** A mod that makes a service expensive (`Heavy`,
  per-value) eats the caller's tick budget; nothing at compile time stops a task from exceeding it.
- **No recursion, ever** — including in code a mod generates. In `ImageMode` the check is skipped but
  stacks are then sized at the part's maximum.
- `ProgramAnalysis`, `FunctionAnalysis`, `TaskFigures`, `CostModel` and `BusSteps` are public; all
  analysis state is per-instance, nothing is cached statically.

[^emitter-analysis]: Emitter.cs:105-150
[^emitter-wcet]: Emitter.cs:160-174
[^analysis-run]: ProgramAnalysis.cs:45-61
[^analysis-violation]: ProgramAnalysis.cs:39-43, 224-234; FunctionAnalysis.cs:13-28
[^analysis-analyse]: ProgramAnalysis.cs:63-222
[^analysis-recursion]: ProgramAnalysis.cs:236-334
[^analysis-figures]: ProgramAnalysis.cs:336-408
[^analysis-yields]: ProgramAnalysis.cs:410-463
[^analysis-lints]: ProgramAnalysis.cs:500-587
[^analysis-wcet]: ProgramAnalysis.cs:635-896
[^analysis-tasks]: ProgramAnalysis.cs:589-633
[^analysis-lines]: ProgramAnalysis.cs:913-1005; LineCost.cs:6
[^assembler-noop]: Assembler.cs:877-879
[^kc-checks]: Checks.cs:513
[^cost-model]: CostModel.cs:8-40
[^bus-steps]: BusSteps.cs:9-44
[^costs-json]: KSA.Kfs.Vm.Tables.Data.costs.json (`service_classes`, `log_cycles_per_value`, `costs[category=syscall]`)
[^game-tables-parts]: GameTables.cs:430-468; PartDescription.cs:3
[^catalogue-json]: KSA.Kfs.Vm.Tables.Data.catalogue.json
[^function-analysis]: FunctionAnalysis.cs:7-57
[^task-figures]: TaskFigures.cs:6-21
[^limits]: TranslationLimits.cs:5-30; LimitException.cs:5-17
