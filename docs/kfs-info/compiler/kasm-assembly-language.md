---
type: Language Reference
title: kASM assembly language
description: The complete kASM language as implemented by the assembler — line syntax, macros and includes, every directive, instruction operands, the expression grammar, types, every symbol predefined from the game tables and the vehicle layout, limits, diagnostics and verified example programs.
resource: ksa-decomp:KSA.Kfs.KAsm/Assembler.cs
tags: [kfs, kasm, assembler, language, directives, symbols, syscall, bus, extension]
sources:
  - id: assembler
    resource: ksa-decomp:KSA.Kfs.KAsm/Assembler.cs
    title: kASM Assembler (decompiled)
  - id: expr
    resource: ksa-decomp:KSA.Kfs.KAsm/Expr.cs
    title: kASM Expr, ExprValue, ExprException (decompiled)
  - id: builtin-includes
    resource: ksa-decomp:KSA.Kfs.KAsm/BuiltinIncludes.cs
    title: kASM BuiltinIncludes (decompiled)
  - id: asm-type
    resource: ksa-decomp:KSA.Kfs.KAsm/AsmType.cs
    title: AsmType (decompiled)
  - id: assemble-result
    resource: ksa-decomp:KSA.Kfs.KAsm/AssembleResult.cs
    title: AssembleResult and SourceFile (decompiled)
  - id: catalogue-enums
    resource: ksa-decomp:KSA.Kfs.Vm.Tables/CatalogueEnums.cs
    title: CatalogueEnums (decompiled)
  - id: message-layout
    resource: ksa-decomp:KSA.Kfs.Vm.Tables/MessageLayout.cs
    title: MessageLayout (decompiled)
  - id: log-format
    resource: ksa-decomp:KSA.Kfs.Vm.Image/LogFormat.cs
    title: LogFormat (decompiled)
  - id: services-json
    resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.services.json
    title: services.json (embedded table)
  - id: sysblock-json
    resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.sysblock.json
    title: sysblock.json (embedded table)
  - id: traps-json
    resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.traps.json
    title: traps.json (embedded table)
  - id: enums-json
    resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.enums.json
    title: enums.json (embedded table)
  - id: messages-json
    resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.messages.json
    title: messages.json (embedded table)
  - id: bench-layout-json
    resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.bench_layout.json
    title: bench_layout.json (embedded table)
  - id: examples
    resource: ksa-decomp:KSA.Kfs.Host.Workbench/Examples.cs
    title: Workbench examples (decompiled)
  - id: string-kasm
    resource: ksa-decomp:KSA.Kfs.KC.Library.string.kasm
    title: string.kasm (embedded library source)
  - id: kc-front-end
    resource: ksa-decomp:KSA.Kfs.KC/KcFrontEnd.cs
    title: KcFrontEnd (decompiled)
  - id: bus-analysis
    resource: ksa-decomp:KSA.Kfs.Compiler/BusAnalysis.cs
    title: BusAnalysis (decompiled)
  - id: offline-harness
    resource: /modding/offline-harness.md
    title: Offline harness experiments E1–E12
generated: { by: claude-code/claude-fable-5-2, at: 2026-11-26T06:11:10Z }
status: stable
ksa_build: "2026.11.25.5949"
---

# Overview

kASM is a line-oriented assembler for the kVM stack machine. One source instruction becomes one
[IR](/compiler/ir-reference.md) instruction, which the [emitter](/compiler/backend-emitter.md) encodes
unchanged — no optimisation is applied to hand-written kASM. Mnemonics, stack effects and immediate
types come from the opcode table ([kVM opcodes](/vm/opcodes.md)); everything else — functions, frames,
globals, tasks, bus buffers, constants — is expressed with directives.

```csharp
public sealed class Assembler(GameTables tables, PartDescription part, IoLayout layout) {
    public Func<string, string?> ProjectFiles { get; init; }   // resolves .include "name"; default: nothing found
    public bool EmitDebug { get; init; } = true;
    public bool ModuleOnly { get; init; }                      // stop after building the IrModule (kC projects)
    public bool ImageMode { get; init; }                       // relaxed mode for reassembling a disassembly
    public DiagnosticBag Diagnostics { get; init; }
    public AssembleResult Assemble(IReadOnlyList<SourceFile> files);   // single use per instance
    public static List<string> SplitOperands(string s);
}
public sealed record SourceFile(string Path, string Text);
```

An `Assembler` is single-use ("an Assembler is single-use: create one per Assemble call"). All files
passed to one `Assemble` call share one namespace of functions, globals, macros, constants and
types.[^assembler-api]

Passes: **Preprocess** (comments, labels, macros, includes) → **PassA** (structure: functions, frames,
globals, strings, entries, constants) → **PassB** (entry options, instructions, data items) →
**CheckProgram** (skipped in `ImageMode`) → `Emitter` (skipped with `ModuleOnly`).

# Line syntax

Each physical line (CRLF is normalised to LF) is processed as:[^assembler-split]

1. **Comment.** A `;` outside a double-quoted string starts a comment that runs to the end of the
   line. Only `"` opens and closes a string for this purpose (a `"` preceded by `\` does not); a
   single quote means nothing to the comment stripper. Together with the expression grammar below,
   which has no character literal, this means **kASM has no character literals**: `PUSH8 ';'` is
   cut at the `;` and what is left, `PUSH8 '`, is an expression error (`KA-E012`); write the
   number.[^assembler-strip-comment]
2. **Label.** An optional `name:` at the start, matching `^\s*([A-Za-z_][A-Za-z0-9_]*)\s*:(?!\S)` — an
   identifier, a colon, then whitespace or end of line.
3. **Word and rest.** The first whitespace-delimited token is the *word* (a directive if it starts
   with `.`, a macro name if one is defined, otherwise a mnemonic); the remainder, trimmed, is the
   operand text.

A line may carry only a label. Directives and mnemonics are **case-insensitive** (directives are
compared lower-cased, mnemonics upper-cased); labels, symbols, macro names and option names are
case-sensitive.

Operands are separated by commas at nesting depth 0 — commas inside `( )`, `[ ]` or double quotes do
not split (`SplitOperands`).

Names: labels are `[A-Za-z_][A-Za-z0-9_]*`; function, global, slot and loop-bound names additionally
allow `.` after the first character (`^[A-Za-z_][A-Za-z0-9_.]*$`). Macro and `.equ` names are not
validated.

# Macros and includes

Handled before anything else, per file.[^assembler-preprocess]

```text
.macro NAME [p1[, p2 …]]      ; parameters separated by spaces or commas
    body lines, with \p1 \p2 … substituted textually
.endm
NAME a, b                     ; invocation: operand count must equal the parameter count
```

- Body lines are stored raw and re-parsed at each invocation, so a body may contain labels,
  directives, instructions and other macro invocations (nesting limit 128, `KA-E720`).
- A label on the invoking line is kept as a label on its own line.
- Every expanded line reports diagnostics at the **invoking** line's position.
- Macro names are matched against the word exactly as written; a macro defined later in the same file
  does not affect earlier lines. A missing `.endm` is `KA-E012`.

```text
.include <kfs.kinc>           ; built-in: constants and types from the game tables
.include <vehicle.kinc>       ; built-in: constants and types from this vehicle's I/O layout
.include "other.kasm"         ; project file, spliced in textually
```

- `<…>` accepts only the two built-in names (`BuiltinIncludes.Names`); anything else is `KA-E015`
  "can't find 'x'" with the hint "`<>` searches generated and library includes, `\"\"` the project."
- Built-in includes add **no source text**: they add entries to the assembler's constant and type
  dictionaries. Because preprocessing of all files finishes before `PassA`, a built-in include takes
  effect for the whole assembly regardless of where, or in which file, it appears.
- `"…"` resolves through `ProjectFiles`. There is no include guard; including a file twice
  re-assembles its text (and reports duplicate definitions).
- Limits: depth 32 (`KA-E722`), 1,000 includes per program (`KA-E723`), 150,000 tokens after
  expansion (`KA-E724`), 200,000 tokens produced by macro expansion (`KA-E725`).

# Program-level directives

| directive | syntax | meaning and checks |
| --- | --- | --- |
| `.tick_hz` | `.tick_hz N` | scheduler tick rate; `N` must be in `catalogue.json`'s `allowed_tick_hz` (5, 10, 20, 25, 50, 100); at most once (`KA-E406`). Default: `default_tick_hz` (50) |
| `.claim` | `.claim ALLOCATION` | the program takes actuator allocation and may write the command block. `ALLOCATION` is the only claim (`KA-E012` otherwise); not available on part index 0, the Sequencer (`KA-E520`), except in `ImageMode` |
| `.subscribe` | `.subscribe MSG_NAME buffer` | defines the global `buffer` (message struct size, align 8) and subscribes it; unknown message is `KA-E003` |
| `.publish` | `.publish MSG_NAME buffer` | likewise for publication; on the Sequencer publishing `MSG_NAV_STATE`, `MSG_ATT_TARGET` or `MSG_WRENCH_CMD` is `KA-E520` |
| `.init` | `.init func` | `func` is the init task (at most one) |
| `.task` | `.task func rate_hz=N` or `.task func divisor=N` `[priority=P]` | `func` is a periodic task. One of `rate_hz`/`divisor` is required. `rate_hz` must divide the tick rate (`KA-E407`, with the nearest valid rates in the hint); `divisor` 1–65535; `priority` 0–255, higher runs first among equal divisors |
| `.program` | `.program func [share=S]` | `func` is the program task — the only one allowed to `YIELD` (at most one, `KA-E405`); `share` 1–100 percent |
| `.equ` | `.equ name, value` | a constant: an integer expression, or a float literal **with** suffix (`1.5f` float32, `1.5d` float64). Evaluated when the line is reached |
| `.string` | `.string name "text"` | a channel string (string kind 2); `name` evaluates to its string-table index |
| `.format` | `.format name "fmt"` | a log format (kind 1); `name` = string index. Also defines `name.size` (argument-buffer bytes) and `name.arg0`, `name.arg1`, … (offset of each value in the buffer). An invalid format is `KA-E550` |
| `.global` | `.global name, size[, align]` | zero-initialised RAM. `size` 0–1,048,576 (`KA-E726`); `align` 1, 2, 4 or 8 (`KA-E004`), default 8 when `size ≥ 8`, else 4 |
| `.data` / `.const` | `.data name` … `.enddata`, `.const name` … `.endconst` | initialised RAM; `.const` marks it read-only for `STG`/`STG64` (`KA-E209`). Either terminator closes either block |
| `.extern` | `.extern f[, g …]` | declares functions defined outside this assembly (kC functions in a mixed project) so `CALL` accepts them; kASM files assembled together already see each other's functions. A name defined nowhere ends as error 304 |
| `.proto` | `.proto "C prototype"` | attaches a C prototype to the **next** `.func` |
| `.func` / `.endfunc` | `.func name` … `.endfunc` | a function; no nesting |
| `.strict`, `.bounds` | anything | accepted and **ignored** (`Not active in this build`) |
| `.lang`, `.loop`, `.endloop` | — | rejected: `KA-E002` "unknown directive (IR-only)" |

Entry-directive tokens are split on single spaces and options must be `key=value` with a whole-number
value. An entry function must take no arguments and return nothing (`KA-E401`), and must be
defined (`KA-E401` "entry function 'f' is not defined").[^assembler-passb]

Bus buffers, strings and globals share one symbol namespace; defining a name twice is `KA-E012`.

## Data items (inside `.data` / `.const`)

| item | bytes each | operand |
| --- | --- | --- |
| `.byte` | 1 | integer expression, −128…255 |
| `.half` | 2 | −32768…65535 |
| `.word` | 4 | −2³¹…2³²−1, **or a global's address** (`sym`, `sym + n`, `sym − n`) — emitted as a relocation |
| `.long` | 8 | any 64-bit integer |
| `.float` | 4 | float literal with suffix `f`, or a float32 `.equ` name |
| `.double` | 8 | float literal with suffix `d`, or a float64 `.equ` name |
| `.zero` | n | `.zero n` appends `n` zero bytes (0…1,048,576) |

Each item takes a comma-separated list. Before each value the block is padded with zeros to the item's
size, and the global's alignment is raised to that size. Data items are evaluated in `PassB`, so they
may refer to globals defined later. Anything else inside a block is `KA-E012`.[^assembler-data]

# Functions and frames

```text
.proto "float dot3(const float *a, const float *b)"
.func dot3
    .args a, b              ; 4-byte argument cells, in call order (first = deepest on the stack)
    .args64 t               ; 8-byte argument (two cells), 8-aligned; a hidden padding cell is inserted if needed
    .local i                ; 4-byte local, 4-aligned
    .local64 acc            ; 8-byte local, 8-aligned
    .local buf, 24, 8       ; untyped block: name, size (0–65,528), alignment (1, 2, 4 or 8)
    .local p : vec3*        ; optional type annotation after ':' (debug information only)
top:                        ; labels are local to the function
    …instructions…
    RET 1
.endfunc
```

- `.args`/`.args64` must precede every `.local` and every instruction (`KA-E012`).
- A type annotation must be a known type name or end in `*` (`KA-E003` otherwise).
- At `.endfunc` the assembler computes `ArgCells` = argument bytes / 4 and the frame size (highest slot
  end rounded up to 8). More than 255 argument cells is `KA-E719`; a frame over 65,528 bytes is
  `KA-E717`.
- **`ENTER` is never written**; it is generated from the frame declarations (`KA-E001` if written).
- A slot name used in an instruction operand evaluates to its **frame offset**: `LDL i`, `STL64 acc`,
  `ADDRL buf + 8`.
- `.proto` is parsed for cell counts: the parameter cells must equal `.args` (`KA-E310`) and the
  result cells become the function's declared result (`RET n` must match, `KA-E331`). Result cells:
  `void` 0; a pointer 1; `double`, `int64_t`, `uint64_t`, `long long`, `unsigned long long` 2;
  `vec3`/`quat`/`mat3`/`dvec3`/`dquat`/`dmat3` or `struct …` 0 results plus one hidden pointer
  parameter; anything else 1. In a kC project each `.proto` is also exported through the generated
  header `<file>.kasm.h`.[^assembler-proto]

Annotations inside a function:

| directive | syntax | effect |
| --- | --- | --- |
| `.loop_bound` | `.loop_bound label, N` (N ≥ 1) | declares that the loop headed by `label` runs at most `N` times, so WCET can be computed; without it the loop draws `KA-W711` and the task has no WCET |
| `.loc` | `.loc file:line:col` | records an `IrLoc` item; `Inferred:` no observable effect for kASM (debug lines come from the kASM line itself) |
| `.scope` / `.endscope` | `.scope a, b` | records an `IrScope` item; no effect in this build |

`CheckProgram` (not in `ImageMode`) additionally requires that the last instruction of every function
is `RET` or `JMP` (`KA-E333`), that all `RET`s of a function return the same number of cells
(`KA-E331`), at most one `.program` (`KA-E405`) and at most one `.init` (`KA-E012`).[^assembler-check]

# Instructions

`MNEMONIC [operand]`. The mnemonic is looked up in `GameTables.OpcodesByMnemonic`; an unknown one is
`KA-E001` with a "Did you mean X?" hint (nearest by edit distance). The operand count must equal the
opcode's immediate count.[^assembler-instr]

| operand of | accepted | notes |
| --- | --- | --- |
| `JMP`, `JZ`, `JNZ` | a label of the same function | forward references allowed; `KA-E005` otherwise |
| `CALL`, `CALLF` | a function defined in this assembly or named by `.extern` | `KA-E003` otherwise. There is no indirect call |
| `u32` immediates (`PUSH32`, `LDG`, `STG`, `LDG64`, `STG64`, `BOUNDS`) | integer expression −2³¹…2³²−1; **a global's address ± constant**; a float32 literal or constant (meaningful for `PUSH32`) | `STG`/`STG64` to a `.const` is `KA-E209` |
| `u64` immediate (`PUSH64`) | any 64-bit integer expression; a float64 literal or constant | |
| `i8`/`u8` immediates (`PUSH8`, `TRAP`, `RET`) | −128…255 | `RET n` with `n > 8` is `KA-E331` |
| `i16`/`u16` immediates (`PUSH16`, `LDL`, `STL`, `ADDRL`, `LDL64`, `STL64`, `COPY`, `ZERO`, `INDEX`, `SYSCALL`) | −32768…65535 | |

Float operands: a literal matching
`-?(digits.digits | .digits | digits before e/E)(e[+-]digits)?[f|d]?` — it must contain a `.` or an
exponent. Without a suffix it takes the width of the instruction (`PUSH32 1.0` is float32, `PUSH64
2.0` is float64); a suffix that disagrees is `KA-E101` ("1.0d is float64; PUSH32 needs float32").
`_` digit separators are allowed.

Out-of-range immediates are `KA-E101` "N doesn't fit MNEMONIC's u16 immediate (lo to hi)".

# Expressions

Evaluated by `Expr` over 64-bit signed integers (wrapping).[^expr]

```text
expr    := unary { binop unary }
binop   := "|"                precedence 1 (lowest)
         | "&"                precedence 3
         | "<<" | ">>"        precedence 4      (shift count masked to 6 bits; >> is arithmetic)
         | "+" | "-"          precedence 5
         | "*" | "/" | "%"    precedence 6 (highest)
unary   := ("+" | "-" | "~") unary
         | "(" expr ")"
         | number
         | sizeof "(" type ")" | alignof "(" type ")" | offsetof "(" type "," field ")"
         | identifier
number  := decimal | "0x" hex | "0b" binary        ; "_" separators allowed; must fit 64 bits unsigned
identifier := [A-Za-z_][A-Za-z0-9_.]*
```

- All binary operators are left-associative. There is no `^`, no comparison and no logical operator.
- Division or remainder by zero is `KA-E101`; `x / -1` is `-x` and `x % -1` is `0`.
- Nesting deeper than 128 is `KA-E720`. A float literal in an integer expression is `KA-E101`.
- **Address arithmetic.** A global's name evaluates to a *symbol value*. Only `sym + const`,
  `const + sym` and `sym − const` are allowed; anything else is `KA-E101` "a global's address can only
  have a constant added to or subtracted from it".
- `offsetof`'s field may be a dotted/indexed path (`offsetof(kfs_msg_wrench_cmd_t,
  torque_body_Nm.x)`, `offsetof(kfs_commands_t, thruster[0].pulse_s)`). Naming an aggregate member
  yields the lowest offset among its leaves.

Identifier lookup order:[^assembler-lookup]

1. a slot of the current function → its frame offset (instruction operands only);
2. a `.equ` constant (a float constant here is `KA-E101`);
3. a `.string`/`.format` name → its string index;
4. a constant from a built-in include;
5. a global (`.global`, `.data`, `.const`, bus buffer) → symbol value.

Unknown: `KA-E003` "unknown symbol 'x'" — hint "`.include` the file that defines it, or `.extern` it."
Labels and function names are not expression symbols. `Inferred:` because step 5 consults only the
assembler's own globals, kASM cannot name a global defined in kC, whereas kC can declare and use a
kASM global with `extern`.

# Types

Types exist only for `sizeof`/`alignof`/`offsetof` and slot annotations:
`AsmType(Name, Size, Align, Fields: path → offset)`.[^asm-type]

Always available (`BuiltinIncludes.Scalars`; size = alignment):

| type | size | type | size |
| --- | --- | --- | --- |
| `float` | 4 | `int32_t` | 4 |
| `double` | 8 | `uint32_t` | 4 |
| `int8_t` | 1 | `int64_t` | 8 |
| `uint8_t` | 1 | `uint64_t` | 8 |
| `int16_t` | 2 | `int` | 4 |
| `uint16_t` | 2 | `char` | 1 |
| `bool` | 4 | | |

There is no directive for defining new struct types in kASM.

# `<kfs.kinc>` — symbols derived from the game tables

`new BuiltinIncludes(tables, layout, "kfs.kinc")` computes the following. Nothing is stored as text;
"reproducing the include" means reproducing these rules and their stock results.[^builtin-includes-kfs]

## Types

| type | fields → offsets | size | align |
| --- | --- | --- | --- |
| `vec3` | `x` 0, `y` 4, `z` 8 | 12 | 4 |
| `quat` | `x` 0, `y` 4, `z` 8, `w` 12 | 16 | 4 |
| `dvec3` | `x` 0, `y` 8, `z` 16 | 24 | 8 |
| `dquat` | `x` 0, `y` 8, `z` 16, `w` 24 | 32 | 8 |
| `mat3` | `m[i][j]` at `(3i + j) × 4`, i, j ∈ 0..2 | 36 | 4 |
| `dmat3` | `m[i][j]` at `(3i + j) × 8` | 72 | 8 |

## Service ids — `ServiceRow.Symbol → Id` for every non-reserved row of `services.json`

| symbol | id | symbol | id |
| --- | --- | --- | --- |
| `SVC_LOG` | 1 | `SVC_SET_THROTTLE` | 52 |
| `SVC_TRACE` | 2 | `SVC_ENGINES_ON` | 53 |
| `SVC_HALT` | 3 | `SVC_TRANSLATE` | 54 |
| `SVC_SLEEP_UNTIL` | 16 | `SVC_HOLD_HEADING` | 55 |
| `SVC_SLEEP_UNTIL_EVENT` | 17 | `SVC_HOLD_QUAT` | 56 |
| `SVC_STAGE` | 32 | `SVC_ENGINES_OFF` | 57 |
| `SVC_HOLD_DIRECTION` | 48 | `SVC_PLAN_CIRCULARIZE` | 64 |
| `SVC_HOLD_RATES` | 49 | `SVC_PLAN_BURN` | 65 |
| `SVC_RELEASE_ATTITUDE` | 50 | `SVC_BURN_START` | 66 |
| `SVC_SET_ATTITUDE_PROFILE` | 51 | `SVC_BURN_ABORT` | 67 |

(Id 4 is a reserved row with no symbol.) Argument order and types are in
[services table](/tables/services-table.md).

## System block — `sys` = `0xA0000000`, and `sys.<name>` = `0xA0000000 + offset` for every `sysblock.json` field

| symbol | address | symbol | address |
| --- | --- | --- | --- |
| `sys.cycles` | `0xA0000000` | `sys.wake_reason` | `0xA0000020` |
| `sys.budget` | `0xA0000004` | `sys.tier` | `0xA0000024` |
| `sys.tick` | `0xA0000008` | `sys.core` | `0xA0000028` |
| `sys.t_s` | `0xA000000C` | `sys.soi_changes` | `0xA000002C` |
| `sys.dt_s` | `0xA0000010` | `sys.met_us` | `0xA0000030` |
| `sys.overruns` | `0xA0000014` | `sys.nv_writes` | `0xA0000038` |
| `sys.task_id` | `0xA0000018` | `sys.reset_cause` | `0xA000003C` |
| `sys.task_dt_s` | `0xA000001C` | `sys.rejected_requests` | `0xA0000040` |

## Trap codes — `TRAP_<name>` = code for every `traps.json` row

`TRAP_ILLEGAL_OPCODE` 1, `TRAP_OPERAND_OVERFLOW` 2, `TRAP_OPERAND_UNDERFLOW` 3, `TRAP_RETURN_OVERFLOW`
4, `TRAP_INIT_OVERRUN` 5, `TRAP_STACK_OVERFLOW` 6, `TRAP_NULL_ACCESS` 7, `TRAP_BAD_ADDRESS` 8,
`TRAP_READ_ONLY` 9, `TRAP_MISALIGNED` 10, `TRAP_DIV_ZERO` 11, `TRAP_BOUNDS` 12, `TRAP_USER` 13,
`TRAP_BAD_JUMP` 14, `TRAP_BAD_SYSCALL` 15, `TRAP_BAD_YIELD` 16, `TRAP_MEMORY_ERROR` 17.

## Catalogue enums — `CatalogueEnums.All(tables)`

Two hard-coded lists drive this:[^catalogue-enums]

- **Message-field groups** `("mode", "ATT_MODE")`, `("roll_mode", "ROLL")`, `("state", "BURN")`: the
  first message field with that name is found in `messages.json`, and every `` `N` `NAME` `` pair in
  its free-text `meaning` becomes `KFS_<GROUP>_<NAME> = N`.
- **Service enum groups** `("direction", "KFS_", from 2)`, `("attitude_profile", "KFS_PROFILE_", 0)`,
  `("apsis", "KFS_AT_", 0)`, `("burn_end_cause", "KFS_BURN_END_", 0)`: each `enums.json` list yields
  `<prefix><UPPERCASE NAME> = index`, starting at the given index.

Stock results:

| constants | values |
| --- | --- |
| `KFS_ATT_MODE_NONE`, `_RATE`, `_ATTITUDE` | 0, 1, 2 |
| `KFS_ROLL_FREE`, `_UP`, `_DOWN` | 0, 1, 2 |
| `KFS_BURN_NONE`, `_PLANNED`, `_ALIGNING`, `_BURNING`, `_DONE`, `_ABORTED`, `_PENDING` | 0–6 |
| `KFS_ENU_FORWARD`, `KFS_ENU_BACKWARD`, `KFS_ENU_UP`, `KFS_ENU_DOWN` | 2, 3, 4, 5 |
| `KFS_LVLH_AHEAD`, `KFS_LVLH_BEHIND`, `KFS_LVLH_RADIAL_OUT`, `KFS_LVLH_RADIAL_IN` | 6, 7, 8, 9 |
| `KFS_VLF_PROGRADE`, `_RETROGRADE`, `_NORMAL`, `_ANTINORMAL`, `_OUTWARD`, `_INWARD` | 10–15 |
| `KFS_BURN_PLUS_DV`, `KFS_BURN_MINUS_DV` | 16, 17 |
| `KFS_TARGET_TOWARD`, `_AWAY`, `_ANTIVELOCITY`, `_ALIGN` | 18, 19, 20, 21 |
| `KFS_PROFILE_STRICT`, `_BALANCED`, `_RELAXED` | 0, 1, 2 |
| `KFS_AT_APOAPSIS`, `KFS_AT_PERIAPSIS` | 0, 1 |
| `KFS_BURN_END_NONE`, `_COMPLETED`, `_PROPELLANT_OUT`, `_STAGE_RAN_DRY`, `_IGNITION_DENIED`, `_PLANNING_FAILED`, `_SUPERSEDED`, `_CANCELLED` | 0–7 |

(`direction` entries 0 `none` and 1 `custom` are deliberately skipped.)

# `<vehicle.kinc>` — symbols derived from the I/O layout and the message catalogue

`new BuiltinIncludes(tables, layout, "vehicle.kinc")`:[^builtin-includes-vehicle]

| symbol / type | rule |
| --- | --- |
| `sensors` = `0x80000000`, type `kfs_sensors_t` | only if the layout has sensor fields; the type maps every sensor field name to its offset (size `SensorSize`, align 8) |
| `commands` = `0x90000000`, type `kfs_commands_t` | only if the layout has command fields; likewise |
| `THRUSTER_COUNT`, `ENGINE_COUNT`, `GIMBAL_COUNT` | number of distinct `thruster[i]` / `engine[i]` / `gimbal[i]` indices among the command fields (the three group names are a hard-coded array) |
| `sensors.<field>` = `0x80000000 + offset` | for every sensor field whose name contains no `[` |
| `commands.<field>` = `0x90000000 + offset` | for every command field whose name contains no `[` |
| type `kfs_msg_header_t` (16 bytes, align 8) | `seq` 0, `source` 4, `flags` 6, `t_us` 8 |
| `MSG_<NAME>` = id, type `kfs_msg_<name>_t` | for every row of `messages.json`: the type maps every leaf path (`hdr.seq`, `torque_body_Nm.x`, …) to its offset; size = `MessageLayout.Size`, align 8 |
| type `kfs_vehicle_props_<group>_t` | per-vehicle element struct of an array message member, with leaf paths relative to one element |

Array elements have no direct constant; address them with `offsetof`:
`STG commands + offsetof(kfs_commands_t, thruster[3].pulse_s)`.

For the embedded bench layout (`bench_layout.json`, fingerprint `0x070A6447`, sensor size 32, command
size 64) this gives: `sensors.imu.accel_body_mps2.x/.y/.z` = `0x80000000`/`04`/`08`,
`sensors.imu.rates_body_rps.x/.y/.z` = `0x8000000C`/`10`/`14`, `sensors.imu.valid` = `0x80000018`;
`THRUSTER_COUNT` 12, `ENGINE_COUNT` 1, `GIMBAL_COUNT` 1; no `commands.<field>` constants (every bench
command field is indexed: `thruster[0..11].pulse_s` at 0–44, `engine[0].throttle` 48,
`engine[0].lease_s` 52, `gimbal[0].y` 56, `gimbal[0].z` 60). Real vehicles differ: a generated
vehicle layout has command fields only, so `sensors`, `sensors.*` and `kfs_sensors_t` are not defined
there — see [I/O layout generation](/host/io-layout-generation.md).

Stock message constants: `MSG_NAV_STATE` `0x0101`, `MSG_FLIGHT_DATA` `0x0102`, `MSG_ATT_TARGET`
`0x0201`, `MSG_GUIDANCE_STATUS` `0x0202`, `MSG_WRENCH_CMD` `0x0301`, `MSG_VEHICLE_PROPS` `0x0401`,
`MSG_BURN_STATUS` `0x0402`, `MSG_PILOT_INPUT` `0x0403`, `MSG_FC_SETTINGS` `0x0404`, `MSG_BURN_PLAN`
`0x0405`.

Note that `.subscribe`/`.publish` resolve the message **by name** through `MessageLayout.Find` and do
not need `<vehicle.kinc>`; the include is needed for the `MSG_*` constants and the struct types.

# Strings and log formats

String literals are double-quoted with escapes `\"`, `\\`, `\n`, `\t`, `\xHH`; the result must be valid
UTF-8 (`KA-E012`).

A `.format` string is validated by `LogFormat.Parse` and laid out by `LogFormat.Layout`; the grammar
and the buffer rules are in
[kVM image format](/compiler/kvm-image-format.md#format-strings). To log from kASM, store each value at
`buffer + name.argN`, then push the format index and the buffer address and `SYSCALL SVC_LOG`.

# Examples

## Stock: pilot-mode service calls (`kAsm_engines_on/engines_on.kasm`, verbatim)

```text
; engines_on.kasm: Pilot mode. The stock flight computer flies the vehicle;
; this program asks it, once, for full throttle and the engine switch on. A
; service's arguments go on the stack first, the first one deepest, and SYSCALL
; pops them.
.include <kfs.kinc>
.tick_hz 10
.global done, 4                     ; 1 once the requests are made

.task seq rate_hz=10
.func seq
    LDG   done                      ; done already: nothing more to ask
    JNZ   out
    PUSH32 1.0f                     ; f32 throttle: full
    SYSCALL SVC_SET_THROTTLE        ; pops 1, pushes 0
    SYSCALL SVC_ENGINES_ON          ; pops 0, pushes 0
    PUSH8 1
    STG   done
out:
    RET   0
.endfunc
```

The companion `kAsm_hold_att/hold_att.kasm` shows a three-argument service and an enum constant:
`PUSH32 1.5707964f` / `PUSH32 0.7853982f` / `PUSH8 KFS_ROLL_UP` / `SYSCALL SVC_HOLD_HEADING`.[^examples]

## Stock: bus publish and subscribe (`ex_busmon/slow_pub.kasm`, header comment omitted)

```text
.include <kfs.kinc>
.include <vehicle.kinc>
.tick_hz 50
.publish   MSG_WRENCH_CMD out
.subscribe MSG_WRENCH_CMD seen
.global last_seq, 4

.task fast rate_hz=50
.func fast
    LDG   seen + offsetof(kfs_msg_wrench_cmd_t, hdr.seq)
    STG   last_seq
    RET   0
.endfunc

.task slow rate_hz=10
.func slow
    LDG   sys.tick
    I2F
    STG   out + offsetof(kfs_msg_wrench_cmd_t, torque_body_Nm.x)
    RET   0
.endfunc
```

`slow` is the single writer of the publication buffer `out`; `fast` reads the subscription buffer
`seen`. The image's `Messages[]` holds `MSG_WRENCH_CMD` once with role 3.

## Stock: a function called from kC (`e2e04_kasm/dot3.kasm`, verbatim)

```text
; dot3 by hand (for float[3]): 46 cycles on the Mk2. The stack column shows the
; operand depth after each line; it peaks at 4 and returns with 1 (the float
; result).
.include <kfs.kinc>

.proto "float dot3(const float *a, const float *b)"
.func dot3
    .args a, b
    LDL   a
    LDW
    LDL   b
    LDW
    FMUL
    LDL   a
    PUSH8 4
    ADD
    LDW
    LDL   b
    PUSH8 4
    ADD
    LDW
    FMUL
    FADD
    LDL   a
    PUSH8 8
    ADD
    LDW
    LDL   b
    PUSH8 8
    ADD
    LDW
    FMUL
    FADD
    RET   1
.endfunc
```

The kC side includes the generated header: `#include "dot3.kasm.h"`.

## Stock library: a bounded loop (`<string.kasm>` `memset`, verbatim)

```text
.proto "void *memset(void *d, int32_t c, uint32_t n)"
.func memset
    .args d
    .args c
    .args n
    .local i
    .loop_bound Ltop, 1048576
Ltop:
    LDL   i
    LDL   n
    GEU
    JNZ   Lend
    LDL   d
    LDL   i
    INDEX 1
    LDL   c
    STB
    LDL   i
    PUSH8 1
    ADD
    STL   i
    JMP   Ltop
Lend:
    LDL   d
    RET   1
.endfunc
```

## Constructed: logging

`Inferred:` assembled by hand from the rules above (the `SVC_LOG` row says "in kASM, fmt is a .format
index and argp the buffer's address"); not a stock example and not run.

```text
.include <kfs.kinc>
.tick_hz 10
.format msg "tick %u\n"             ; defines msg (string index), msg.size (4) and msg.arg0 (0)
.global buf, 8, 8                   ; argument buffer, 8-aligned

.task t rate_hz=10
.func t
    LDG   sys.tick                  ; value 0
    STG   buf + msg.arg0            ; into the buffer at its offset
    PUSH8 msg                       ; u32 fmt  (deepest)
    PUSH32 buf                      ; ptr argp (on top)
    SYSCALL SVC_LOG                 ; pops 2, pushes 0
    RET   0
.endfunc
```

A global buffer is used rather than a frame slot because a store through an address the bus analysis
cannot tie to a named global (for example `ADDRL slot` … `STW`) draws the warning `KA-W546` in user
kASM.

# `ImageMode`

`kvm-cli assemble --image` sets `ImageMode`, intended for reassembling
[disassembler](/compiler/kasm-disassembler.md) output:[^assembler-imagemode]

- `.claim ALLOCATION` is allowed on the Sequencer;
- `.proto` cell checks and all of `CheckProgram` are skipped;
- the emitter runs with `AllowRecursion` — no program analysis (so no recursion, stack-balance, WCET
  or `SYSCALL`-id checks at assembly time), maximum stack reservations, bus checks off, no report.

An image built this way is still fully validated when it is loaded.

# Diagnostics

Every assembler diagnostic has prefix `KA`. Numbers used: 1, 2, 3, 4, 5, 12, 15, 101, 209, 310, 331,
333, 401, 405, 406, 407, 520, 550, 717, 719, 720, 722–726; later stages add 260, 301, 304, 330, 332,
404, 408, 540, 542, 544, 546, 701, 703, 711, 713, 718. See
[diagnostics infrastructure](/compiler/diagnostics-infrastructure.md) for the rule text of each.

# Modding notes

Which symbols a mod gets "for free" by adding table rows, and which are hard-coded:

| symbol family | source | free with a new row? |
| --- | --- | --- |
| mnemonics | `tables.OpcodesByMnemonic` | table-driven, but a new opcode would also need a VM implementation — not practical |
| `SVC_*` | `tables.ServicesData.Services` rows with `Reserved == false`, via `Symbol` and `Id` | **yes** — append a `ServiceRow` with a `Symbol`. Note `BuiltinIncludes` reads `ServicesData` (the row list), while validation reads `tables.Services` (the `ServiceInfo` dictionary): a mod must add to **both** |
| `MSG_*` and `kfs_msg_*_t` | `tables.MessagesData.Messages` via `MessageLayout.All` | **yes** — append a `MessageRow` (field types limited to the built-in set) |
| `sys.*` | `tables.SysBlockData.Fields` | yes for the symbol; the VM must actually write the field |
| `TRAP_*` | `tables.TrapsData.Traps` | yes for the symbol |
| `KFS_*` enums | `CatalogueEnums.All` — **hard-coded** group lists over three named message fields and four named `enums.json` lists | no: a new enumeration gets no constants unless it extends one of those seven lists (appending a value to, say, `enums.json`'s `direction` does produce a new `KFS_…` constant) |
| `sensors.*`, `commands.*`, `kfs_sensors_t`, `kfs_commands_t` | the `IoLayout` passed to the assembler | yes, automatically, for whatever fields the layout generator produces — but new fields change the layout fingerprint |
| `THRUSTER_COUNT`, `ENGINE_COUNT`, `GIMBAL_COUNT` | hard-coded group names `thruster`, `engine`, `gimbal` | no `<GROUP>_COUNT` for a new command group |
| scalar and vector types | hard-coded in `BuiltinIncludes` | no |

Other points:

- `BuiltinIncludes` is recomputed on every `.include <…>`; there is **no cache**, so table changes are
  picked up by the next assembly.
- **The directive names are in no table.** Mnemonics come from the opcode table, but `.func`,
  `.task`, `.equ` and the rest exist only as string literals compared in `Assembler`'s code
  (`text2 == ".macro"`, `case ".long":` …); nothing in `GameTables` or the embedded JSON lists
  them. A tool that needs the list — a syntax file, a completion source — must transcribe it (it is
  written out under "Program-level directives" above and in
  [language-tooling surface](/modding/language-tooling-surface.md#kasm-directives)) or read the
  string literals out of the assembler's compiled methods, and re-check after a game update.[^assembler-directive-literals]
- An unknown `SVC_NEW` symbol is `KA-E003` at assembly; a numeric `SYSCALL 200` with no table row is
  `KA-E101` from program analysis ("SYSCALL 200 is not a service in the table").
- `Assembler` and `BuiltinIncludes` are public; `BuiltinIncludes.Names` and `.Scalars` are `public
  static readonly` — the array and dictionary contents are mutable, so a mod could register a third
  built-in include name, but `BuiltinIncludes`' constructor only knows the two stock names and would
  add nothing for it without a patch.
- kASM is the lowest-friction way to exercise a mod-added service before kC bindings exist: push
  the argument cells with `PUSH8`/`PUSH16`/`PUSH32` (there is no bare `PUSH` mnemonic), then
  `SYSCALL <id or symbol>`. `Verified:` with a row added through `GameTables.Load`,
  `SYSCALL SVC_<SYMBOL>` assembled with no other declaration and the request reached the
  host.[^offline-harness] Supplying the row as JSON fills both structures named in the table above;
  the tested recipe is [adding a service](/modding/adding-a-service.md).

[^assembler-api]: Assembler.cs:19-178; SourceFile.cs:3; AssembleResult.cs:8-15
[^assembler-split]: Assembler.cs:180-236, 437-474, 935-938
[^assembler-preprocess]: Assembler.cs:238-435
[^assembler-passb]: Assembler.cs:476-891, 1277-1369
[^assembler-data]: Assembler.cs:508-540, 1559-1659
[^assembler-proto]: Assembler.cs:940-1123; KcFrontEnd.cs:376-388
[^assembler-check]: Assembler.cs:1661-1717
[^assembler-instr]: Assembler.cs:1125-1153, 1388-1557
[^assembler-lookup]: Assembler.cs:1219-1275
[^assembler-imagemode]: Assembler.cs:135-152, 570, 1046
[^expr]: Expr.cs:8-409; ExprValue.cs:3-6; ExprException.cs:5-17
[^asm-type]: AsmType.cs:6-25; BuiltinIncludes.cs:12-27
[^builtin-includes-kfs]: BuiltinIncludes.cs:33-63, 110-126
[^builtin-includes-vehicle]: BuiltinIncludes.cs:64-108, 128-131
[^catalogue-enums]: CatalogueEnums.cs:12-64
[^examples]: Examples.cs:48-56; KSA.Kfs.KC.Library.string.kasm:89-114
[^offline-harness]: Experiment E9 in the offline harness document.
[^assembler-strip-comment]: Assembler.cs:180-195 (`StripComment`: toggles on `"` unless the previous character is `\`, returns the text before the first `;` seen outside such a string), :213 (its one caller, `Split`); Expr.cs:220-287 (`Unary`: a sign or `~`, `(`, an ASCII digit, a letter or `_`; anything else is `unexpected '<c>' in expression`, number 12).
[^assembler-directive-literals]: Assembler.cs:272, :282, :294 (`.endm`, `.macro`, `.include` compared as literals during preprocessing), :518-524 (data-item `case` labels), :559-878 (the directive `switch`). A search of the decompiled `KSA.Kfs.Vm.Tables` folder and of the embedded `KSA.Kfs.Vm.Tables.Data.*.json` files finds no directive name.
