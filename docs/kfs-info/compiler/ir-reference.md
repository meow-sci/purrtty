---
type: API Reference
title: kFS IR reference (IrModule, instructions, validator, printer, IrBuilder)
description: The compiler's intermediate representation — a symbolic form of kVM stack code — with every model type, the instruction set and operand kinds, the validator's rules, the printer's text form and the IrBuilder API.
resource: ksa-decomp:KSA.Kfs.Compiler.Ir/IrModule.cs
tags: [kfs, compiler, ir, api, kvm, opcodes]
sources:
  - id: ir-module
    resource: ksa-decomp:KSA.Kfs.Compiler.Ir/IrModule.cs
    title: IrModule (decompiled)
  - id: ir-function
    resource: ksa-decomp:KSA.Kfs.Compiler.Ir/IrFunction.cs
    title: IrFunction (decompiled)
  - id: ir-global
    resource: ksa-decomp:KSA.Kfs.Compiler.Ir/IrGlobal.cs
    title: IrGlobal (decompiled)
  - id: ir-items
    resource: ksa-decomp:KSA.Kfs.Compiler.Ir/IrInstr.cs
    title: IrInstr and the other IrItem records (decompiled; IrItem.cs, IrLabel.cs, IrLoc.cs, IrLoop.cs, IrScope.cs)
  - id: ir-operand
    resource: ksa-decomp:KSA.Kfs.Compiler.Ir/IrOperand.cs
    title: IrOperand and OperandKind (decompiled)
  - id: ir-type
    resource: ksa-decomp:KSA.Kfs.Compiler.Ir/IrType.cs
    title: IrType (decompiled)
  - id: ir-validator
    resource: ksa-decomp:KSA.Kfs.Compiler.Ir/IrValidator.cs
    title: IrValidator (decompiled)
  - id: ir-printer
    resource: ksa-decomp:KSA.Kfs.Compiler.Ir/IrPrinter.cs
    title: IrPrinter (decompiled)
  - id: ir-builder
    resource: ksa-decomp:KSA.Kfs.Compiler.Builder/IrBuilder.cs
    title: IrBuilder and IrLabelRef (decompiled)
  - id: op
    resource: ksa-decomp:KSA.Kfs.Vm/Op.cs
    title: Op enum (decompiled)
  - id: opcodes-json
    resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.opcodes.json
    title: opcodes.json (embedded table)
  - id: emitter
    resource: ksa-decomp:KSA.Kfs.Compiler.Backend/Emitter.cs
    title: Emitter (decompiled)
  - id: assembler
    resource: ksa-decomp:KSA.Kfs.KAsm/Assembler.cs
    title: kASM Assembler (decompiled)
  - id: analysis
    resource: ksa-decomp:KSA.Kfs.Compiler/ProgramAnalysis.cs
    title: ProgramAnalysis (decompiled)
  - id: kc-lowering
    resource: ksa-decomp:KSA.Kfs.KC/Lowering.cs
    title: kC Lowering (decompiled)
generated: { by: claude-code/claude-fable-5-2, at: 2026-11-25T21:29:02Z }
status: stable
ksa_build: "2026.11.25.5949"
---

# What the IR is

The IR is **kVM stack code with symbolic operands**. An `IrInstr` carries a `KSA.Kfs.Vm.Op` — the very
opcode byte the VM executes — and an `IrOperand` that may still be a label name, a function name or a
global symbol plus offset.[^ir-items] There are no virtual registers, no SSA values and no basic-block
objects: a function body is a flat `List<IrItem>` of instructions interleaved with labels and three
kinds of annotation. Lowering from kC already decides stack order; the back end only

- resolves symbols to addresses, picks `CALL` vs `CALLF`, and encodes bytes
  ([backend emitter](/compiler/backend-emitter.md)), and
- analyses the code (stack heights, call graph, WCET, bus use —
  [program analysis](/compiler/program-analysis-and-wcet.md), [bus analysis](/compiler/bus-analysis.md)).

The same IR is produced by the kASM assembler (one `IrInstr` per source instruction) and by the kC
front end (through `IrBuilder`), which is what lets the two languages link in one program.

All IR types are `public` and live in `KSA.Kfs.Compiler.Ir`; `IrBuilder`/`IrLabelRef` are in
`KSA.Kfs.Compiler.Builder`.

# Model

## `IrModule`

`public sealed class IrModule`.[^ir-module]

| member | type | meaning |
| --- | --- | --- |
| `Functions` | `List<IrFunction>` | every function, kC and kASM, project and library |
| `Globals` | `List<IrGlobal>` | RAM objects; order determines addresses (after the compiler's per-file re-sort) |
| `Strings` | `List<IrString>` | format and channel strings; the list index is the image string-table index |
| `Files` | `List<string>` | source file names; the index is the debug file index and the global/function ordering key |
| `FileLanguages` | `Dictionary<string, DebugLang>` | per-file language for the Debug section (`KASM`, `KC`, `KScript`, `Generated`); files not listed default to `KASM` |
| `TickHz` / `TickHzAt` | `int?` / `Span` | declared tick rate and where; null means the catalogue default |
| `DoubleBits` | `int` (64) | 64 or 32; 32 sets the image's `FLAG_DOUBLE32` |
| `Claims` | `List<string>` | claim names; only `"ALLOCATION"` exists |
| `Bus` | `List<IrBusDecl>` | publish/subscribe declarations |

## `IrFunction`

`public sealed class IrFunction`.[^ir-function]

| member | set by | read by | meaning |
| --- | --- | --- | --- |
| `Name` (required) | front ends | everyone | link name; kC statics that collide get `name.N` |
| `At` (required) | front ends | diagnostics, debug, library test (`At.File.StartsWith('<')`) | definition span |
| `Slots` | `IrBuilder` / assembler | debug info, printer | frame layout (`IrSlot`) |
| `ArgCells` | `IrBuilder.CloseArgs` / assembler `FinishFrame` | validator, analysis, `ENTER` | argument area in 4-byte cells |
| `Frame` | `IrBuilder.Finish` / `FinishFrame` | `ENTER`, stack analysis | frame bytes, rounded up to 8 |
| `ProtoResultCells` | kC lowering (always); assembler when `.proto` present | `IrValidator.ResultCells` | declared result cells |
| `ProtoArgCells`, `Proto` | assembler | header generation, printer | kASM `.proto` text and its parameter cell count |
| `Body` | front ends, peephole | everyone | `List<IrItem>` |
| `Entry`, `EntryAt` | front ends | emitter, analysis | `EntryKind` and where the entry was declared |
| `RateDivisor` (1) | front ends | emitter | periodic task: run every N ticks |
| `Priority` | front ends | `Emitter.TaskOrder` | higher runs earlier among equal divisors |
| `Share` | assembler (`.program share=`) | emitter | program task's percent of the tick; 0 = unset |
| `Suspends` | `ProgramAnalysis.Yields` | debug flags | reaches a `YIELD` |
| `Language` (`"kasm"`) | front ends | prefix selection, placement, report | `"kc"`, `"ks"` or anything else = kASM |
| `MaxHeight` | `IrBuilder`, `ProgramAnalysis.Analyse` | — | peak operand-stack height inside the function |
| `Peephole` | kC lowering (`Optimize == 1`) | `Peephole.Run` | opt-in to peephole rewriting |
| `DeclIndex`, `Static` | kC lowering | `Emitter.Place` | source declaration order; static linkage |
| `LoopBounds` | assembler `.loop_bound` | WCET | head-label → iteration bound |
| `BusReads`, `BusWrites`, `CommandWrites`, `BusMarks` | kC front end | `BusAnalysis` | precomputed bus attribution; null means "derive it from the instructions" (kASM) |

## `IrGlobal`

`public sealed class IrGlobal`.[^ir-global]

| member | meaning |
| --- | --- |
| `Name` (required), `At` (required) | symbol and definition span |
| `Size`, `Align` (default 4) | bytes; the emitter aligns the running address up to `Align` |
| `Data` | `null` = zero-initialised RAM (no image bytes); otherwise the initial bytes, emitted as one data record |
| `Relocs` | `(int Offset, string Symbol, long Addend)` — a little-endian `u32` at `Offset` is patched with `address(Symbol) + Addend` |
| `Const` | read-only intent; the assembler rejects `STG`/`STG64` to it (`KA-E209`) |
| `Type` | optional `IrType` for the Debug section |
| `DebugName` | name shown in debug info when it differs from the link name |
| `Generated` | compiler-generated (string literals); recorded with no source file in debug info |

## Small records

| type | shape | notes |
| --- | --- | --- |
| `IrString` | `(string Name, byte Kind, string Text, Span At)` | `Kind` 1 = format, 2 = channel (`StringKind.Format` / `Channel`); kind 0 (`Name`) strings are added by the emitter for task names, never by front ends |
| `IrBusDecl` | `(bool Publish, ushort MessageId, string Message, string Buffer, Span At, string Prefix)` | `Buffer` is a global's name; `Prefix` is `"KC"` or `"KA"` for diagnostics |
| `IrSlot` | `(string Name, int Offset, int Size, int Align, bool Argument, string? TypeName, IrType? Type = null, bool Temporary = false)` | an empty `Name` marks an alignment-padding argument slot |
| `IrType` | `(DebugTypeKind Kind, bool Signed, int Size, IrType? Elem = null, int Count = 0, string? Name = null, IReadOnlyList<(string Name, IrType Type, int Offset)>? Fields = null, bool Const = false)` | factories: `Bool`, `Int(size, signed, name)`, `Float(size)`, `Pointer(elem)` (4 bytes), `Array(elem, count)`, `Enum(signed, name)` (4 bytes)[^ir-type] |
| `EntryKind` | `None`, `Init`, `Periodic`, `Program` | maps to `TaskKind.Init/Periodic/Program` in the image |

## Body items

All derive from `public abstract record IrItem(Span At)`.[^ir-items]

| item | shape | consumed by |
| --- | --- | --- |
| `IrInstr` | `(Span At, Op Op, IrOperand Operand)` | everything; `At` becomes the Debug line |
| `IrLabel` | `(Span At, string Name)` | branch resolution; labels are **function-local** |
| `IrLoc` | `(Span At, string File, int Line, int Col)` | `IrPrinter` and the line-cost estimator's fallback position only — the emitter takes Debug lines from `IrInstr.At`, not from `IrLoc` |
| `IrScope` | `(Span At, string[]? Names)` | `IrPrinter` only; `Names == null` is a scope end. **Not active in this build:** nothing in the back end reads scopes — every local's debug scope is its whole function — and the kC lowering never calls `BeginScope` |
| `IrLoop` | `(Span At, int Id, long? Bound, string Source, bool End)` | WCET analysis; a begin marker (`End == false`) carries the bound, the end marker carries only the id |

## `IrOperand`

```csharp
public readonly record struct IrOperand(OperandKind Kind, long Value = 0, long Value2 = 0, string? Name = null);
public enum OperandKind { None, Imm, Imm2, Label, Symbol, Function }
```

| kind | factory | fields | how the emitter encodes it |
| --- | --- | --- | --- |
| `None` | `IrOperand.None` | — | no immediate |
| `Imm` | `Imm(v)` | `Value` | first immediate = `Value` |
| `Imm2` | `Imm2(a, b)` | `Value`, `Value2` | two immediates (only `ENTER`, which front ends never emit) |
| `Label` | `LabelRef(name)` | `Name` | `rel16` = label address − address of the next instruction; out of ±32 KB is error 703 |
| `Symbol` | `Symbol(name, offset)` | `Name`, `Value` = offset | global address + offset; unknown symbol is error 3 |
| `Function` | `FunctionRef(name)` | `Name` | absolute code address of the callee's `ENTER`; `u16` for `CALL`, `u24` for `CALLF` |

# Instruction set

`IrInstr.Op` ranges over the whole `Op` enum; the stack effect and immediate layout of each opcode come
from `opcodes.json` through `GameTables.Opcodes[(byte)op]` (`OpcodeInfo.Pops`, `.Pushes`, `.Imm`,
`.Size`, `.Category`). Semantics are documented in [kVM opcodes](/vm/opcodes.md); the table below is the
compiler's view (cells popped → pushed, immediates, cost category).[^op][^opcodes-json]

| mnemonic(s) | code(s) hex | immediates | pops → pushes | category |
| --- | --- | --- | --- | --- |
| `NOP` `BRK` | 00 01 | — | 0 → 0 | stack |
| `TRAP` | 02 | `u8 code` | 0 → 0 | stack |
| `PUSH8` | 08 | `i8` | 0 → 1 | stack |
| `PUSH16` | 09 | `i16` | 0 → 1 | stack |
| `PUSH32` | 0A | `u32` | 0 → 1 | const32 |
| `DUP` | 10 | — | 1 → 2 | stack |
| `DROP` | 11 | — | 1 → 0 | stack |
| `SWAP` | 12 | — | 2 → 2 | stack |
| `OVER` | 13 | — | 2 → 3 | stack |
| `LDL` / `STL` | 18 / 19 | `u16 off` | 0 → 1 / 1 → 0 | mem32 |
| `ADDRL` | 1A | `u16 off` | 0 → 1 | addr |
| `LDG` / `STG` | 1B / 1C | `u32 addr` | 0 → 1 / 1 → 0 | mem32 |
| `LDW` / `STW` | 20 / 21 | — | 1 → 1 / 2 → 0 | mem32 |
| `LDH` `LDHU` / `STH` | 22 23 / 24 | — | 1 → 1 / 2 → 0 | mem16 |
| `LDB` `LDBU` / `STB` | 25 26 / 27 | — | 1 → 1 / 2 → 0 | mem8 |
| `COPY` | 28 | `u16 n` | 2 → 0 | block |
| `ZERO` | 29 | `u16 n` | 1 → 0 | block |
| `INDEX` | 2A | `u16 size` | 2 → 1 | addr |
| `BOUNDS` | 2B | `u32 count` | 1 → 1 | bounds |
| `ADD` `SUB` | 30 31 | — | 2 → 1 | i32.alu |
| `MUL` | 32 | — | 2 → 1 | i32.mul |
| `DIV` `DIVU` `REM` `REMU` | 33–36 | — | 2 → 1 | i32.div |
| `NEG` `NOT` | 37 3B | — | 1 → 1 | i32.alu |
| `AND` `OR` `XOR` | 38–3A | — | 2 → 1 | i32.alu |
| `SHL` `SHR` `SAR` | 3C–3E | — | 2 → 1 | i32.shift |
| `EQ` `NE` `LT` `LTU` `LE` `LEU` `GT` `GTU` `GE` `GEU` | 40–49 | — | 2 → 1 | i32.alu |
| `FADD` `FSUB` | 50 51 | — | 2 → 1 | f32.add |
| `FMUL` / `FDIV` | 52 / 53 | — | 2 → 1 | f32.mul / f32.div |
| `FSQRT` | 54 | — | 1 → 1 | f32.sqrt |
| `FNEG` `FABS` | 55 56 | — | 1 → 1 | f32.sign |
| `FEQ` `FNE` `FLT` `FLE` `FGT` `FGE` | 58–5D | — | 2 → 1 | f32.cmp |
| `I2F` `U2F` `F2I` `F2U` | 60–63 | — | 1 → 1 | cvt.i32f32 |
| `SEXT8` `SEXT16` `ZEXT8` `ZEXT16` | 64–67 | — | 1 → 1 | i32.alu |
| `JMP` | 70 | `rel16` | 0 → 0 | branch |
| `JZ` `JNZ` | 71 72 | `rel16` | 1 → 0 | branch |
| `CALL` | 73 | `u16 addr` | callee's arg cells → result cells | call |
| `RET` | 74 | `u8 r` | — | ret |
| `ENTER` | 75 | `u8 ncells`, `u16 frame` | — | enter |
| `CALLF` | 76 | `u24 addr` | as `CALL` | callf |
| `YIELD` | 77 | — | 0 → 0 | yield |
| `SYSCALL` | 78 | `u16 id` | service's arg cells → result cells | syscall |
| `PUSH64` | 80 | `u64` | 0 → 2 | stack64 |
| `DUP2` / `DROP2` | 81 / 82 | — | 2 → 4 / 2 → 0 | stack64 |
| `SWAP2` / `OVER2` | 83 / 84 | — | 4 → 4 / 4 → 6 | stack64 |
| `LDL64` / `STL64` | 85 / 86 | `u16 off` | 0 → 2 / 2 → 0 | mem64 |
| `LDG64` / `STG64` | 87 / 88 | `u32 addr` | 0 → 2 / 2 → 0 | mem64 |
| `LD64` / `ST64` | 89 / 8A | — | 1 → 2 / 3 → 0 | mem64 |
| `ADD64` `SUB64` | 8B 8C | — | 4 → 2 | i64.alu |
| `MUL64` | 8D | — | 4 → 2 | i64.mul |
| `DIV64` `DIVU64` `REM64` `REMU64` | 8E–91 | — | 4 → 2 | i64.div |
| `NEG64` `NOT64` | 92 96 | — | 2 → 2 | i64.alu |
| `AND64` `OR64` `XOR64` | 93–95 | — | 4 → 2 | i64.alu |
| `SHL64` `SHR64` `SAR64` | 97–99 | — | 3 → 2 | i64.shift |
| `EQ64` `NE64` `LT64` `LTU64` `LE64` `LEU64` `GT64` `GTU64` `GE64` `GEU64` | 9A–A3 | — | 4 → 1 | i64.cmp |
| `DADD` `DSUB` | A4 A5 | — | 4 → 2 | f64.add |
| `DMUL` / `DDIV` | A6 / A7 | — | 4 → 2 | f64.mul / f64.div |
| `DSQRT` | A8 | — | 2 → 2 | f64.sqrt |
| `DNEG` `DABS` | A9 AA | — | 2 → 2 | f64.sign |
| `DEQ` `DNE` `DLT` `DLE` `DGT` `DGE` | AB–B0 | — | 4 → 1 | f64.cmp |
| `I2L` `U2L` / `L2I` | B1 B2 / B3 | — | 1 → 2 / 2 → 1 | cvt.i32i64 |
| `L2D` `LU2D` `D2L` `D2LU` | B4–B7 | — | 2 → 2 | cvt.i64f |
| `I2D` `U2D` / `D2I` `D2U` | B8 B9 / BA BB | — | 1 → 2 / 2 → 1 | cvt.i64f |
| `F2D` / `D2F` | BC / BD | — | 1 → 2 / 2 → 1 | cvt.f32f64 |
| `L2F` / `F2L` | BE / BF | — | 2 → 1 / 1 → 2 | cvt.i64f |

Five opcodes — `CALL`, `CALLF`, `RET`, `SYSCALL`, `ENTER` — have `null` pops/pushes in the table ("the
instruction decides at run time") and are special-cased by every IR consumer; `TRAP` (0 → 0 in the
table) is listed with them because it also ends a path:

| op | IR operand | stack effect used by the compiler |
| --- | --- | --- |
| `CALL`, `CALLF` | `Function` | pops callee `ArgCells`, pushes `IrValidator.ResultCells(callee)` |
| `RET` | `Imm(n)` | must find exactly `n` cells above the function's entry level; terminates the path |
| `SYSCALL` | `Imm(id)` | pops `ServiceInfo.ArgCells`, pushes `ServiceInfo.ResultCells`, looked up in `GameTables.Services` |
| `ENTER` | never present in a body | generated by the emitter from `ArgCells` and `Frame` |
| `TRAP` | `Imm(code)` | terminates the path |

A function **never contains `ENTER`** in the IR: the emitter prepends `ENTER ArgCells, Frame` when it
encodes each function.[^emitter-enter] `CALL` in kC-generated IR is a request for "a call"; the emitter
widens it to `CALLF` when the callee lands above 64 KB. kASM `CALL`/`CALLF` are taken literally.

# `IrValidator`

`public static string? Check(IrModule m, GameTables tables)` returns `null` or
`"<function>: <reason>"` for the first failing function. It is run only on the kC route (before and
after the peephole); a failure there is an internal compiler error (`KC-E900`), never a user
diagnostic.[^ir-validator]

`ResultCells(f)` = `f.ProtoResultCells`, else the operand of the first `RET` in the body, else 0.

Per function, a worklist walk over the instruction list (labels and annotations skipped) tracks the
operand-stack height, starting at 0 on entry:

| check | failure text |
| --- | --- |
| each label name is unique in the function | `label X defined twice` |
| control never runs past the last instruction | `control reaches the end of the function without RET or JMP` |
| `CALL`/`CALLF` names a function in the module | `call to unknown function 'X'` |
| `RET n` has `n` = the function's result cells | `RET n differs from the function's N result cells` |
| height at `RET` = result cells | `RET N with height H` |
| `SYSCALL id` is a key of `tables.Services` | `unknown service N` |
| no `ENTER` in a body | `ENTER inside a body` |
| pops never go below 0 | `instruction I (OP) pops below the floor (height H)` |
| `JMP`/`JZ`/`JNZ` name a label in the function | `jump to unknown label 'X'` |
| every instruction is reached with one height on all paths | `inconsistent height at instruction I: A and B` |

`TRAP` and a correct `RET` end a path; `JMP` has only the label successor; `JZ`/`JNZ` have both.

The kASM route does not call `IrValidator`. The same properties are checked there, with user-facing
diagnostics, by `ProgramAnalysis.Analyse` (see
[program analysis](/compiler/program-analysis-and-wcet.md)); for kC functions those same checks report
`KC-E900`.

# `IrPrinter` text form

`public static string Print(IrModule m)` renders a module as assembler-like text (invariant culture,
`\n` line ends). `kvm-cli compile --ir` is its only stock caller.[^ir-printer]

Order and syntax:

```text
.tick_hz <n>                                   ; only when TickHz is set
.format <name> "<text>"   |  .string <name> "<text>"     ; one per IrString (Kind 1 → .format)
.global <name>, <size>, <align>                ; IrGlobal with Data == null
.data <name>  | .const <name>                  ; IrGlobal with Data
    .word <symbol>[ + <addend>]                ;   4 bytes covered by a relocation
    .byte 0x<HH>                               ;   every other byte, one per line
.enddata      | .endconst
.init <fn>                                     ; entry directives, for functions with Entry != None
.task <fn> divisor=<n>[ priority=<p>]          ;   (priority printed only when non-zero)
.program <fn>[ share=<s>]                      ;   (share printed only when > 0)
.proto "<prototype>"                           ; only when IrFunction.Proto is set
.func <name>
    .lang <language>
    .args <name>[ : <type>]                    ; 4-byte argument slot
    .args64 <name>[ : <type>]                  ; 8-byte, 8-aligned argument slot
    .local <name>[ : <type>]                   ; 4-byte, 4-aligned local
    .local64 <name>                            ; 8-byte, 8-aligned local with no TypeName
    .local <name>, <size>, <align>             ; any other slot
<label>:
    .loc <file>:<line>:<col>
    .scope <a>, <b>   | .endscope
    .loop <id>[ bound=<n>] src=<source>   | .endloop <id>
    <OP>[ <operand>]
.endfunc
```

- Slots with an empty name (argument padding) are not printed.
- Operands: `Imm` → decimal; `Imm2` → `a, b`; `Label`/`Function` → the name; `Symbol` → `name` or
  `name + offset`.
- Strings are quoted with `\"`, `\\`, `\n`; bytes outside printable ASCII 0x20–0x7E are `\xHH` (per
  UTF-8 byte).
- **The printout is not a complete round trip.** `IrModule.Bus`, `IrModule.Claims`,
  `IrFunction.LoopBounds`, `DoubleBits` and the bus-attribution lists are not printed, and `.lang`,
  `.loop` and `.endloop` are rejected by the assembler as "IR-only" (`KA-E002`).[^assembler-ironly]

Example. `Inferred:` this text was produced by applying the printer's rules by hand to the IR a small
kC task would lower to (`count++; kfs_logf("count %d\n", count);`); it is not captured tool output, and
the exact instruction sequence kC emits for the increment may differ. The log call follows
`Lowering.Syscall`: store each value into an 8-aligned temporary buffer, push the format's string
index, push the buffer address, `SYSCALL 1`.

```text
.tick_hz 50
.format .fmt0 "count %d\n"
.global count, 4, 4
.task step divisor=1
.func step
    .lang kc
    .local64 tmp.0
    .loc main.c:7:5
    LDG count
    PUSH8 1
    ADD
    STG count
    .loc main.c:8:5
    ADDRL 0
    LDG count
    STW
    PUSH8 0
    ADDRL 0
    SYSCALL 1
    RET 0
.endfunc
```

# `IrBuilder`

`public sealed class IrBuilder(GameTables tables, IrFunction fn)` appends to one function while
tracking the operand-stack height, so a lowering bug shows up as an `InvalidOperationException` at the
faulty call rather than as a corrupt image.[^ir-builder]

## State

| member | meaning |
| --- | --- |
| `Function` | the function being built |
| `Height` | current operand-stack height in cells |
| `MaxHeight` | `fn.MaxHeight` (peak so far) |
| `At` (get/set) | span stamped onto every item emitted next |
| `Unreachable` | true after `RET`, `TRAP` or `JMP` until a live label is marked |
| `NeedsTerminator` | whether the body's last instruction/label still needs a `RET` appended |

## Frame layout calls

Slots must be declared in this order: arguments, scalar locals, aggregates, temporaries. Violations
throw ("arguments come before locals", "scalar locals are declared before aggregates and temporaries",
"aggregates are declared before temporaries").

| method | effect |
| --- | --- |
| `IrSlot Argument(name, size, align, type)` | next argument offset; an 8-byte argument at a non-multiple-of-8 offset is preceded by an unnamed 4-byte padding argument slot |
| `IrSlot Local(name, size, align, type)` | closes the argument area (`fn.ArgCells = argBytes / 4`), aligns, appends |
| `IrSlot Aggregate(name, size, align, type)` | as `Local`, in the aggregate region |
| `IrSlot Temp(size, align, type = null)` | slot named `tmp.N`, `Temporary = true` |
| `void Finish()` | `fn.Frame` = highest slot end rounded up to 8; throws if a referenced label was never placed |

## Emission calls

| method | emits | stack rule |
| --- | --- | --- |
| `Emit(Op)`, `Emit(Op, long imm)`, `Emit(Op, IrOperand)` | any opcode whose table row has non-null pops and pushes | throws for `CALL`/`CALLF`/`RET`/`ENTER`/`SYSCALL` ("needs an explicit stack effect; use Call, Ret or Syscall") and for `JMP`/`JZ`/`JNZ` ("use Jump for branches") |
| `PushInt(long)` | `PUSH8`, `PUSH16` or `PUSH32` — the smallest that holds the value truncated to 32 bits | +1 |
| `PushFloat(float)`, `PushFloatBits(uint)` | `PUSH32 bits` | +1 |
| `PushInt64(long)`, `PushDouble(double)` | `PUSH64 bits` | +2 |
| `PushAddress(long)` | `PUSH32 Imm(address)` | +1 |
| `LoadAt(long address, size = 4)` / `StoreAt(...)` | `LDG`/`LDG64` / `STG`/`STG64` with an **absolute** `Imm` address | used for memory-mapped I/O objects |
| `LoadGlobal(symbol, size = 4)` / `StoreGlobal(...)` | the same opcodes with a `Symbol` operand | |
| `AddressOfGlobal(symbol)` | `PUSH32 Symbol(symbol, 0)` | +1 |
| `LoadLocal(slot)` / `StoreLocal(slot)` | `LDL`/`LDL64` / `STL`/`STL64` with `slot.Offset` | |
| `AddressOfLocal(slot)` | `ADDRL slot.Offset` | +1 |
| `Call(function, argCells, resultCells)` | `CALL FunctionRef(function)` | −argCells +resultCells (caller supplies both) |
| `Ret(cells)` | `RET cells` | throws unless `Height == cells`; then height 0, unreachable |
| `Trap(code)` | `TRAP code` | unreachable |
| `Syscall(ushort id, argCells, resultCells)` | `SYSCALL Imm(id)` | −argCells +resultCells; **the builder does not check the id or the cell counts against the table** — the validator and analysis do |
| `IrLabelRef Label(hint = "L")` | nothing; creates a unique name `hintN` | |
| `Jump(Op op, IrLabelRef)` | `JMP`/`JZ`/`JNZ` | conditional jumps pop 1; records the height the label must have |
| `Mark(IrLabelRef)` | an `IrLabel` | throws on double placement or a height mismatch between fall-through and jumps |
| `int BeginLoop(long? bound, string source)` / `EndLoop()` | `IrLoop` markers | kC passes `source` `"proven"` when it derived a bound and `"none"` otherwise[^kc-lowering-loops] |
| `BeginScope(params string[] names)` / `EndScope()` | `IrScope` markers | unused by stock lowering |
| `Loc(Span)` | an `IrLoc`, and sets `At` | |

`IrLabelRef` exposes only `Name`; its bookkeeping fields and constructor are `internal`.

# Modding notes

- **Adding instructions to an existing kC build** is possible by wrapping the front end
  (`Compiler.KcFrontEnd`, see [compile pipeline](/compiler/compile-pipeline.md)) and editing
  `IrFunction.Body` — but the body must still satisfy the validator's height discipline, and any
  `SYSCALL` id must be a key of `GameTables.Services`.
- **A new service needs no IR change.** `SYSCALL` is generic: operand = `u16` id; argument and result
  cell counts come from the `ServiceInfo` in the tables at validation, analysis and emission time. The
  opcode set, by contrast, is closed: `Op` is a compiled enum and the VM's interpreter is a `switch`
  over it.
- **Memory-mapped I/O has no IR concept.** A sensor or command field is just an absolute address
  operand (`LDG 0x8000_0018`); nothing in the IR records which layout field was meant. Only the
  image-header layout fingerprint ties a compiled image to the layout it was built against.
- `IrValidator.Check` is public and cheap; a mod that generates IR should call it before emitting.
- `IrBuilder` is the only safe way to build bodies by hand: it keeps `MaxHeight`, `ArgCells` and
  `Frame` consistent. Building `IrInstr`s directly (as the assembler does) leaves those to the caller.

[^ir-module]: IrModule.cs:7-28
[^ir-function]: IrFunction.cs:6-57
[^ir-global]: IrGlobal.cs:5-26
[^ir-items]: IrInstr.cs:5; IrItem.cs:3; IrLabel.cs:3; IrLoc.cs:3; IrLoop.cs:3; IrScope.cs:3
[^ir-type]: IrType.cs:6-34
[^ir-validator]: IrValidator.cs:9-155
[^ir-printer]: IrPrinter.cs:10-477; Cli.cs:344-347
[^ir-builder]: IrBuilder.cs:10-438; IrLabelRef.cs:3-19
[^op]: Op.cs:3-151
[^opcodes-json]: KSA.Kfs.Vm.Tables.Data.opcodes.json (86 rows; `pops`/`pushes` null for CALL, RET, ENTER, CALLF, SYSCALL)
[^emitter-enter]: Emitter.cs:206-211
[^assembler-ironly]: Assembler.cs:83, 554-558
[^kc-lowering-loops]: Lowering.cs:245, 267, 298
