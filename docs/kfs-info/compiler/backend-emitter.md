---
type: Subsystem Reference
title: Backend emitter and peephole (IR to kVM image)
description: How Emitter turns an IrModule into image bytes — task ordering, function placement and far calls, the calling convention and frames, RAM planning, how service calls, memory-mapped I/O and bus buffers appear in code, every emitter diagnostic, and the five peephole rewrites.
resource: ksa-decomp:KSA.Kfs.Compiler.Backend/Emitter.cs
tags: [kfs, compiler, backend, emitter, peephole, calling-convention, syscall, io]
sources:
  - id: emitter
    resource: ksa-decomp:KSA.Kfs.Compiler.Backend/Emitter.cs
    title: Emitter (decompiled)
  - id: emit-result
    resource: ksa-decomp:KSA.Kfs.Compiler.Backend/EmitResult.cs
    title: EmitResult (decompiled)
  - id: peephole
    resource: ksa-decomp:KSA.Kfs.Compiler/Peephole.cs
    title: Peephole (decompiled)
  - id: image-builder
    resource: ksa-decomp:KSA.Kfs.Vm.Image/ImageBuilder.cs
    title: ImageBuilder (decompiled)
  - id: image-format
    resource: ksa-decomp:KSA.Kfs.Vm.Image/ImageFormat.cs
    title: ImageFormat (decompiled)
  - id: image-reader
    resource: ksa-decomp:KSA.Kfs.Vm.Image/ImageReader.cs
    title: ImageReader (decompiled)
  - id: ir-builder
    resource: ksa-decomp:KSA.Kfs.Compiler.Builder/IrBuilder.cs
    title: IrBuilder (decompiled)
  - id: assembler
    resource: ksa-decomp:KSA.Kfs.KAsm/Assembler.cs
    title: kASM Assembler (decompiled)
  - id: kc-lowering
    resource: ksa-decomp:KSA.Kfs.KC/Lowering.cs
    title: kC Lowering (decompiled)
  - id: kc-front-end
    resource: ksa-decomp:KSA.Kfs.KC/KcFrontEnd.cs
    title: KcFrontEnd (decompiled)
  - id: kvm-machine
    resource: ksa-decomp:KSA.Kfs.Vm/KvmMachine.cs
    title: KvmMachine (decompiled)
  - id: builtin-includes
    resource: ksa-decomp:KSA.Kfs.KAsm/BuiltinIncludes.cs
    title: kASM BuiltinIncludes (decompiled)
  - id: examples
    resource: ksa-decomp:KSA.Kfs.Host.Workbench/Examples.cs
    title: Workbench examples (decompiled)
  - id: offline-harness
    resource: /modding/offline-harness.md
    title: Offline harness experiments E1–E12
generated: { by: claude-code/claude-fable-5-2, at: 2026-11-25T21:29:02Z }
status: stable
ksa_build: "2026.11.25.5949"
---

# Role

`public sealed class Emitter` is the single back end for both languages. It takes an
[`IrModule`](/compiler/ir-reference.md) and produces an `EmitResult`:

```csharp
public Emitter(GameTables tables, PartDescription part, IoLayout layout, DiagnosticBag diags, string prefix = "KA");
public EmitResult Emit(IrModule m);

public sealed record EmitResult(byte[]? Image,
    IReadOnlyDictionary<string, uint> FunctionAddresses,
    IReadOnlyDictionary<string, uint> GlobalAddresses,
    ProgramAnalysis? Analysis = null, CompileReport? Report = null, BusAnalysis? Bus = null);
```

Because the IR already is kVM stack code, "emission" is mostly bookkeeping: order tasks, analyse,
place functions, assign addresses, encode, plan RAM, fill the image tables.

| init-only property | default | set by | effect |
| --- | --- | --- | --- |
| `EmitDebug` | `true` | compiler, assembler | build the Debug section |
| `AllowRecursion` | `false` | assembler `ImageMode` (`kvm-cli assemble --image`) | skip `ProgramAnalysis`, use the part's maximum stack figures, disable bus checks, produce no report |
| `KeepOrder` | `false` | assembler (always `true`) | keep module function order; never widen calls |
| `DoubleBits` | `64` | compiler | recorded in the report only (the image flag comes from `m.DoubleBits`) |
| `PeepholeOff` | `null` | compiler | recorded in the report only |

The constructor's `prefix` (`"KC"` from the compiler, default `"KA"`) is used for diagnostics that
belong to no function; diagnostics tied to a function use that function's language (`"kc"` → `KC`,
`"ks"` → `KS`, anything else → `KA`).[^emitter-ctor]

# `Emit`, phase by phase

Every phase that reports an error returns an `EmitResult` with `Image = null`.

| # | phase | diagnostics |
| --- | --- | --- |
| 1 | `tickHz = m.TickHz ?? CatalogueData.DefaultTickHz`; `maxLogValues` = the largest value count of any format string (`LogFormat.Parse(text, 0, …)?.Values`) | — |
| 2 | every `CALL`/`CALLF` operand must name a function in the module | 304 "'f' is declared but never defined" |
| 3 | `TaskOrder`; periodic tasks with equal divisor and priority declared in different files; task count vs `part.MaxTasks` | 408; 701 "N tasks; the part allows M" |
| 4 | `ProgramAnalysis.Run` (skipped when `AllowRecursion`), then per task: call depth vs `part.MaxReturnEntries`, operand cells vs `part.MaxOperandCells` | analysis diagnostics; 701 ×2 |
| 5 | `BusAnalysis.Run(tasks, part.ClockHz / tickHz)`; each task's WCET gets its subscription and publication copy costs added | bus diagnostics |
| 6 | `Place` — function order and the set of calls to widen | — |
| 7 | `Layout` — function and label addresses | — |
| 8 | global addresses from 16 upward | 701 "globals need N bytes; the part has M bytes of RAM" |
| 9 | encode `ENTER` + body per function | 5, 703, 713, 3 |
| 10 | RAM plan: task stacks, VM region, message slots | 701 "RAM: globals … task stacks … VM region … exceed …" |
| 11 | strings, task entries, links, message entries, data records (with relocations) | 3 "unknown symbol" in a data relocation |
| 12 | per-instruction cost vs one tick's cycles | 718 |
| 13 | program memory: code + data init + runtime-library charge vs `part.CodeBytes` | 701 "program memory: …" |
| 14 | Debug section (if `EmitDebug`), `ImageBuilder.Build()`, compile report (if analysis ran) | — |

Source for the table: the body of `Emit`.[^emitter-emit]

## Task order

`TaskOrder(m)` returns entry functions as: all `Init` entries, then `Periodic` entries sorted by
`RateDivisor` ascending, then `Priority` descending, then declaration order, then all `Program`
entries.[^emitter-taskorder] This is the order of `Tasks[]` in the image, which `ImageReader` requires
to have periodic divisors ascending.

## Function placement and far calls

`Place(m, out far)`:[^emitter-place]

- `KeepOrder` (kASM route): module order, no widening. A near `CALL` to an address ≥ 0x10000 is then
  error 713 "near CALL to 'f' at 0x…, beyond the first 64 KB — Use CALLF, or move the function
  earlier."
- Otherwise functions are sorted by (library last, source-file index, `DeclIndex`, module index). If
  the total encoded size is ≤ 65,536 bytes that order is final and nothing is widened.
- Above 64 KB: functions that kASM code calls with a near `CALL` are moved to the front (they must stay
  reachable by `u16`), the rest are ordered by call-site count descending. Then, up to 8 times,
  addresses are recomputed and every `CALL` **in a kC/kScript function** whose callee is at or above
  0x10000 is added to `far`; iteration stops when a pass adds nothing.

While encoding, a `CALL`/`CALLF` instruction in `far` is emitted as `CALLF` (`u24` target). The image
flag `FLAG_FAR_CALLS` is set when at least one `CALLF` was encoded.

`Layout` then walks the placed functions: each starts with a 4-byte `ENTER` (opcode + `u8` + `u16`),
labels take the current address, instructions advance it by `OpcodeInfo.Size`.[^emitter-layout]

## Encoding

`Encode` writes the opcode byte followed by each immediate little-endian at the width
`GameTables.ImmSize` gives (1, 2, 3, 4 or 8 bytes).[^emitter-encode] Operand resolution:

| operand kind | value written | error |
| --- | --- | --- |
| `Imm` / `Imm2` | the value(s), truncated to the immediate width | — |
| `Label` | `labelAddress − (instructionAddress + size)` as `rel16` | 5 "'L' is not a label in 'f'"; 703 "jump to 'L' is N bytes away; rel16 reaches ±32 KB" |
| `Function` | the callee's `ENTER` address | 713 (near `CALL` beyond 64 KB) |
| `Symbol` | `globalAddress + offset` | 3 "unknown symbol 'x'" |

Each encoded instruction also appends a Debug line `(pc, file, line, col)` from `IrInstr.At` when its
line is > 0 and differs from the previous entry's file and line.

# Calling convention and frames

Established from `IrBuilder`, the assembler's frame directives, the emitter, `ImageReader`'s checks and
the VM's `ENTER`/`RET` implementation.[^kvm-machine-enter]

- **Arguments** are pushed on the operand stack left to right, the first argument deepest.
- **`CALL addr`** (`u16`, first 64 KB) or **`CALLF addr`** (`u24`) transfers to the callee's `ENTER`.
  A call target that is not an `ENTER` is rejected when the image is loaded.
- **`ENTER ncells, frame`** opens a `frame`-byte frame on the task's RAM stack, moves the top `ncells`
  operand cells into the first `4·ncells` bytes (first argument at frame offset 0), zeroes the rest of
  the frame, and makes the current operand height the function's floor. `frame` is a multiple of 8,
  at most 65,528; `ncells` ≤ 255; `4·ncells ≤ frame`.
- **Locals and arguments** are addressed by frame offset: `LDL`/`STL` (4 bytes), `LDL64`/`STL64`
  (8 bytes), `ADDRL` (address). 64-bit values occupy two cells and are 8-aligned in the frame; an
  8-byte argument at an odd cell is preceded by an unnamed padding argument cell.
- **`RET n`** requires exactly `n` cells above the floor (0–8), pops the frame and returns them to the
  caller. Scalars return 1 cell, 64-bit values 2.
- **Aggregates** (kC): an aggregate argument is passed as a pointer; an aggregate result is written
  through a hidden first pointer argument named `.result`, and the function returns 0 cells.[^kc-lowering-agg]
- **Entry functions** (tasks) take no arguments and return nothing: `ENTER 0, frame` … `RET 0`.
- The operand stack and the return stack live in the task's *VM stack* inside the VM region, not in
  the RAM stack: each task reserves `4·MaxOperandCells + 8·MaxCallDepth` bytes there.

# RAM plan

All addresses are RAM offsets; 0–15 is the null guard (`ImageFormat.NULL_GUARD = 16`).[^emitter-ram]

```text
0x00000000  null guard (16 bytes)
0x00000010  globals, in module order, each aligned to its Align
            … round up to 8 = stacksStart
            RAM stacks: one per non-init task, consecutive, each (RamStack + 7) & ~7 bytes
            (the init task's RAM stack starts at stacksStart too — it overlays the others)
            … free …
VmRegionBase = (RamBytes − vmTotal) & ~7
            VM stacks: one per non-init task, consecutive, 4·cells + 8·depth bytes each
            (the init task's VM stack starts at VmRegionBase — overlaid)
            message slots: one per bus message, (Size + 7) & ~7 bytes each, 8-aligned
RamBytes
```

- `vmTotal` = Σ VM-stack bytes of non-init tasks (or the init task's, if larger), rounded up to 8, plus
  the message slots, rounded up to 8.
- A message slot's `SlotOffset` is relative to `VmRegionBase` and holds the 16-byte header plus the
  body (`MessageLayout.Size`).
- `RamRequired` (header field) = end of the RAM stack area + `vmTotal`.
- If the stack area would pass `VmRegionBase`: 701 "RAM: globals G bytes, task stacks S bytes and the
  VM region V bytes exceed the part's N bytes (largest stack: f B, deepest path: a → b → c)".
- The init task's stacks overlay the other tasks' because init runs to completion before any other
  task starts; `ImageReader`'s overlap check ignores init ranges for the same reason.

With `AllowRecursion` there is no analysis, so every entry gets `min(4096, RamBytes / 8)` bytes of RAM
stack, `part.MaxReturnEntries` return entries and `part.MaxOperandCells` operand cells.[^emitter-allowrec]

# How the three program↔game channels look in code

## Service calls

A service call is one instruction: **`SYSCALL id`**, `id` a `u16` immediate
(`IrOperand.Imm(id)`).[^ir-builder-syscall] Arguments are pushed first-deepest like a function call;
the number of cells popped and pushed is the service's `ArgCells`/`ResultCells` from
`GameTables.Services`, never encoded in the instruction.

- kC: the parser binds a function name to a `ServiceInfo` from the table's `kc` column and produces a
  `Syscall` node; `Lowering.Syscall` pushes arguments (aggregates are flattened field by field),
  checks the cell count against `ServiceInfo.ArgCells` (throwing `InvalidOperationException` on
  mismatch) and calls `IrBuilder.Syscall`. `kfs_logf` builds an 8-aligned argument buffer in a frame
  temporary and passes `(formatStringIndex, bufferAddress)`; `trace` passes
  `(channelStringIndex, float)`.[^kc-lowering-syscall] `Verified:` a one-cell `u32` or `f32`
  result works for an added row, but a multi-cell (struct) result does not — a `vec3`-returning
  prototype over a three-cell row made the lowering throw `InvalidOperationException` ("label …
  reached by fall-through with height 2 and by a jump with 0 (… builder invariant)"), an exception,
  not a diagnostic.[^offline-harness]
- kASM: `SYSCALL SVC_SET_THROTTLE`, the symbol coming from `<kfs.kinc>`.
- The emitter does nothing service-specific; it encodes the id. The id is checked by
  `IrValidator` (kC), `ProgramAnalysis` (both), `ImageReader` (load) and `KvmMachine.IsWiredService`
  (run time) — see [kVM image format](/compiler/kvm-image-format.md#where-an-unknown-service-id-is-rejected).
- Cost: the `syscall` category's cycles + the service's cost-class cycles (+ `LogCyclesPerValue ×
  maxLogValues` for per-value services) — [program analysis](/compiler/program-analysis-and-wcet.md#cost-model).

## Memory-mapped I/O fields

There is **no I/O-specific instruction or IR node**. Sensor, command and system-block fields are
absolute addresses in fixed windows:[^image-format-windows]

| window | base | constant |
| --- | --- | --- |
| sensors | `0x80000000` | `ImageFormat.SENSOR_BASE` |
| commands | `0x90000000` | `ImageFormat.COMMAND_BASE` |
| system block | `0xA0000000` | `ImageFormat.SYSTEM_BASE` (`SYSTEM_SIZE = 256`) |
| non-volatile | `0xB0000000` | `ImageFormat.NV_BASE` |

`WINDOW_SIZE = 0x10000` for the sensor and command windows.

- kC objects declared at a fixed address — the generated `vehicle.h` declares
  `extern const kfs_sensors_t sensors __at(0x80000000);` (only for a layout with sensor fields: the
  bench layout; a vehicle layout has none) and
  `extern kfs_commands_t commands __at(0x90000000);` — lower to `LDG`/`STG`/`LDG64`/`STG64` with an
  absolute `Imm` operand (`IrBuilder.LoadAt` / `StoreAt`) or to `PUSH32 address` followed by indirect
  loads/stores for computed elements.[^kc-lowering-at] See
  [kC generated headers](/language/kc-generated-headers.md).
- kASM uses `LDG sensors.<field>` / `STG commands.<field>` or `commands + offsetof(kfs_commands_t,
  thruster[0].pulse_s)`; the symbols are computed from the `IoLayout` by `<vehicle.kinc>`.
- Field offsets are therefore **baked into the code as immediates**. The only record of which layout
  was used is the header's `layout_fingerprint`; a different layout means a rebuild.
- The compile-time gate on command writes is in [bus analysis](/compiler/bus-analysis.md) (544:
  writing the command block without claiming `ALLOCATION`), not in the emitter.

## Bus messages

A subscription or publication buffer is an **ordinary global** of the message's size, 8-aligned.
Program code reads and writes it with normal loads and stores. The emitter turns the declarations
into three image tables:[^emitter-bus]

- `Messages[]` — one entry per message id the program touches, ascending by id, with `Role` bit 0 =
  subscribed, bit 1 = published, and the slot offset in the VM region;
- `Links[]` — `(messageIndex, bufferAddress)` pairs; per task, first its subscriptions then its
  publications, each sorted by message index then buffer address;
- per-task `SubFirst`/`SubCount`/`PubFirst`/`PubCount` ranges into `Links[]`.

The VM copies slot → buffer for each subscription when a task starts and buffer → slot for each
publication when it completes; those copies are costed as the task's "start step" and "completion
step".

# Peephole

`public static class Peephole` rewrites function bodies in place. It only touches functions with
`IrFunction.Peephole == true`, which the kC lowering sets when the function's `Optimize` is 1; kASM
functions are never rewritten.[^peephole-run]

```csharp
public const int RULE_COUNT = 5;  public const int MAX_PASSES = 1000;
public static int[] Run(IrModule m, IReadOnlySet<int>? off = null);                      // throws if a function does not settle
public static int[] Run(IrModule m, IReadOnlySet<int>? off, out IrFunction? unsettled);  // reports it instead
```

Per function the rules are applied in order 1–5, repeatedly, until a full pass changes nothing or
1,000 passes have run. The returned `int[6]` counts applications per rule number (index 0 is unused).
Rules listed in `off` are skipped (`CompileOptions.PeepholeOff`, `kvm-cli --peephole-off`).

"Next instruction" below means the next `IrInstr` with only *transparent* items in between:
annotations, and labels that no jump in the function targets.

| rule | pattern | rewrite |
| --- | --- | --- |
| 1 | `STL x` ; `LDL x` (same offset) — or `STL64 x` ; `LDL64 x` | `DUP` ; `STL x` — or `DUP2` ; `STL64 x` |
| 2 | `PUSH8/PUSH16/PUSH32 0` (plain immediate) ; `ADD` — or `PUSH64 0` ; `ADD64` | both removed |
| 3 | a `JMP`/`JZ`/`JNZ` to label `L` whose first following instruction is `JMP M`, `M ≠ L` | retarget the jump to `M` |
| 4 | `JMP L` where `L` is placed before the next instruction | remove the `JMP` |
| 5 | instructions after `TRAP`, `JMP` or `RET` up to the next jump-target label | removed |

Source for the rules: `Peephole.Apply`.[^peephole-apply]

# Diagnostics emitted by the emitter

| number | condition |
| --- | --- |
| 3 | a `Symbol` operand or data relocation names no global |
| 5 | a `Label` operand names no label of its function |
| 304 | a `CALL`/`CALLF` names no function in the module |
| 408 | two periodic tasks share divisor and priority across files |
| 701 | too many tasks; call depth or operand cells over the part's; globals over RAM; RAM plan over RAM; program memory over `CodeBytes` |
| 703 | branch out of `rel16` range |
| 713 | near `CALL` to a function beyond 64 KB |
| 718 | one instruction (including a function's `ENTER`) costs more than a tick's cycles |

# Modding notes

- **Nothing in the emitter is keyed to specific service, message or field ids.** It is table-driven
  (`GameTables.Opcodes`, `.Services`, cost tables) plus the `IoLayout` and `PartDescription` handed
  in. A service added to the tables compiles through the emitter unchanged.
- **I/O field offsets are immediates.** If a mod changes the generated `IoLayout` (new sensor or
  command fields), `layout.Fingerprint` changes, every existing image fails `ImageReader`'s `layout`
  rule and must be rebuilt, and programs see the new fields only through regenerated headers. A new
  service or message avoids that coupling.
- **The command window test is hard-coded** as `0x90000000 ≤ address < 0x90010000` in the bus analysis
  and the disassembler.
- `Emitter` is `public sealed`; its helpers (`Place`, `Layout`, `Encode`, `BuildReport`, `BuildDebug`)
  are `private`. To change emission a mod would patch `Emit` or post-process the image bytes with
  `ImageBuilder.Patch` (which recomputes the checksum).
- `EmitResult.FunctionAddresses` / `.GlobalAddresses` give a mod the final addresses of every symbol
  without parsing the Debug section — but `Compiler` does not surface the `EmitResult`; use the Debug
  section of the image, or call `Emitter` directly.
- The peephole is the only optimisation pass in the pipeline. It is safe to disable per rule and is
  off for all hand-written kASM.

[^emitter-ctor]: Emitter.cs:27-44, 500-512
[^emitter-emit]: Emitter.cs:51-467
[^emitter-taskorder]: Emitter.cs:514-524
[^emitter-place]: Emitter.cs:526-603
[^emitter-layout]: Emitter.cs:605-629
[^emitter-encode]: Emitter.cs:206-286, 631-662
[^emitter-ram]: Emitter.cs:178-201, 297-349, 363-372
[^emitter-allowrec]: Emitter.cs:107-121
[^emitter-bus]: Emitter.cs:373-406
[^kvm-machine-enter]: KvmMachine.cs:1025-1105; ImageReader.cs:450-476; Assembler.cs:1030-1045
[^kc-lowering-agg]: Lowering.cs:101-119
[^kc-lowering-syscall]: Lowering.cs:1347-1409
[^kc-lowering-at]: Lowering.cs:887-920; IrBuilder.cs:265-278; VehicleHeader.cs:120-132
[^ir-builder-syscall]: IrBuilder.cs:335-340
[^image-format-windows]: ImageFormat.cs:53-65
[^peephole-run]: Peephole.cs:11-60; Lowering.cs:82
[^peephole-apply]: Peephole.cs:62-270
[^offline-harness]: Experiments E6 and E8 in the offline harness document.
