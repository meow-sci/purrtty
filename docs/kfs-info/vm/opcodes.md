---
type: Data Table Reference
title: kVM opcode table (opcodes.json) with implementation semantics
description: Every one of the 146 kVM opcodes — value, mnemonic, immediates, stack effect, cost category and per-core cycles — merged with what KvmMachine actually does for each.
resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.opcodes.json
tags: [kfs, vm, kvm, opcodes, bytecode, data-table]
sources:
  - id: opcodes-json
    resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.opcodes.json
    title: opcodes.json (embedded)
  - id: costs-json
    resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.costs.json
    title: costs.json (embedded)
  - id: op-enum
    resource: ksa-decomp:KSA.Kfs.Vm/Op.cs
    title: Op enum (decompiled)
  - id: kvm-machine
    resource: ksa-decomp:KSA.Kfs.Vm/KvmMachine.cs
    title: KvmMachine (decompiled) — the opcode switch
  - id: image-reader
    resource: ksa-decomp:KSA.Kfs.Vm.Image/ImageReader.cs
    title: ImageReader (decompiled) — decode and static checks
  - id: game-tables
    resource: ksa-decomp:KSA.Kfs.Vm.Tables/GameTables.cs
    title: GameTables.BuildOpcodes (decompiled)
  - id: conv
    resource: ksa-decomp:KSA.Kfs.Vm/Conv.cs
    title: Conv (decompiled) — integer/float conversions
generated: { by: claude-code/claude-fable-5-2, at: 2026-11-25T21:46:43Z }
status: stable
ksa_build: "2026.11.25.5949"
---

# Summary

`opcodes.json` (version 1) holds 86 rows describing **146 opcodes**; a row can list several opcodes that
share a stack effect and cost category.[^opcodes-json] `GameTables.BuildOpcodes` expands it into
`OpcodeInfo?[256] Opcodes` (indexed by opcode value) and `Dictionary<string, OpcodeInfo> OpcodesByMnemonic`;
it rejects an unknown category, a duplicate value or mnemonic, and a row without one summary per
opcode.[^game-tables] The `Op` enum in `KSA.Kfs.Vm` repeats the same 146 values as C# constants, and the
interpreter switches on that enum.[^op-enum][^kvm-machine]

The tables below reproduce every row of the JSON and add, in the last column, what the interpreter does.
The JSON's own header describes itself as:

> One record per spec table row. 'ops' lists [mnemonic, hex code]. 'op_style' is how the Op column joins
> several ops (space: one code span; slash: separate spans joined by ' / '). 'hex_style' is how the Hex
> column is written (list, slash, range). 'imm' is the immediate list in encoding order. 'pops'/'pushes'
> are operand cells, null where the instruction decides at run time (calls, RET, ENTER, SYSCALL).
> 'summary' is one plain-text line per op, in 'ops' order, for the in-game reference and the quick start
> guide.

`op_style` and `hex_style` only steer how the in-game reference renders a row; they are omitted below.
Row schema details are in [opcode, cost and trap table schemas](/tables/opcode-cost-trap-tables-schema.md).

# Reading the tables

- **Immediates** follow the opcode byte, little-endian, in the order shown. `rel16` is a signed offset
  from the end of the instruction. Instruction size is `1 + Σ immediate sizes`.
- **Stack effect** is the table's notation: `[a b] → [c]` with the top of stack on the right; `:64` marks a
  64-bit value occupying two cells (low half deeper).
- **Pops→Pushes** counts operand cells; *run time* means the JSON has `null` (the instruction decides).
- **Cycles** are the four core classes' costs from `costs.json` for the opcode's category, in the order
  K8 / K32 / K32F / K32D. `N+Mw` is a base plus `M` per 4-byte word (see
  [cost model](/vm/cost-model-and-budgets.md)); `*` marks a per-opcode override; `+svc` means the service's
  cost class is added. **Soft on** lists the cores where the instruction is charged as a library
  routine.[^costs-json]
- **Core-class availability.** There is no per-core availability column in the table and no check in the
  loader or the interpreter: **every opcode is legal on every core class**. A core without the hardware
  (for example floating point on `K8` and `K32`) pays the *soft* cost instead and the image is charged the
  routine family's code bytes (see [soft routines](/vm/soft-routines-and-fp.md)).
- In the last column, bare `:NNN` line numbers refer to `KvmMachine.cs`.

Every pop goes through `Need(n)`, which traps `OPERAND_UNDERFLOW` if it would go below the current
function's floor, and every push traps `OPERAND_OVERFLOW` at the task's `MaxOperandCells`; these two are
not repeated per row. Every memory access goes through `Resolve` and can trap `NULL_ACCESS`,
`BAD_ADDRESS`, `READ_ONLY` or `MISALIGNED` (see [memory map](/vm/memory-map-and-sysblock.md)); 32-bit
accesses require 4-byte alignment, 16-bit 2-byte, 64-bit 8-byte.

# Opcode value map

Assigned ranges (everything else is unassigned; the loader rejects an unassigned byte with rule `code`,
`unknown opcode 0x.. at 0x..`, so the interpreter's `default:` → `ILLEGAL_OPCODE` branch at `:1651-1652` is
unreachable for a validated image):[^image-reader]

| Range | Count | Mnemonics |
| --- | --- | --- |
| `0x00`–`0x02` | 3 | `NOP` `BRK` `TRAP` |
| `0x08`–`0x0A` | 3 | `PUSH8` `PUSH16` `PUSH32` |
| `0x10`–`0x13` | 4 | `DUP` `DROP` `SWAP` `OVER` |
| `0x18`–`0x1C` | 5 | `LDL` `STL` `ADDRL` `LDG` `STG` |
| `0x20`–`0x2B` | 12 | `LDW` `STW` `LDH` `LDHU` `STH` `LDB` `LDBU` `STB` `COPY` `ZERO` `INDEX` `BOUNDS` |
| `0x30`–`0x3E` | 15 | `ADD` `SUB` `MUL` `DIV` `DIVU` `REM` `REMU` `NEG` `AND` `OR` `XOR` `NOT` `SHL` `SHR` `SAR` |
| `0x40`–`0x49` | 10 | `EQ` `NE` `LT` `LTU` `LE` `LEU` `GT` `GTU` `GE` `GEU` |
| `0x50`–`0x56` | 7 | `FADD` `FSUB` `FMUL` `FDIV` `FSQRT` `FNEG` `FABS` |
| `0x58`–`0x5D` | 6 | `FEQ` `FNE` `FLT` `FLE` `FGT` `FGE` |
| `0x60`–`0x67` | 8 | `I2F` `U2F` `F2I` `F2U` `SEXT8` `SEXT16` `ZEXT8` `ZEXT16` |
| `0x70`–`0x78` | 9 | `JMP` `JZ` `JNZ` `CALL` `RET` `ENTER` `CALLF` `YIELD` `SYSCALL` |
| `0x80`–`0xBF` | 64 | `PUSH64` `DUP2` `DROP2` `SWAP2` `OVER2` `LDL64` `STL64` `LDG64` `STG64` `LD64` `ST64` `ADD64` `SUB64` `MUL64` `DIV64` `DIVU64` `REM64` `REMU64` `NEG64` `AND64` `OR64` `XOR64` `NOT64` `SHL64` `SHR64` `SAR64` `EQ64` `NE64` `LT64` `LTU64` `LE64` `LEU64` `GT64` `GTU64` `GE64` `GEU64` `DADD` `DSUB` `DMUL` `DDIV` `DSQRT` `DNEG` `DABS` `DEQ` `DNE` `DLT` `DLE` `DGT` `DGE` `I2L` `U2L` `L2I` `L2D` `LU2D` `D2L` `D2LU` `I2D` `U2D` `D2I` `D2U` `F2D` `D2F` `L2F` `F2L` |

# Stack and constants (spec §4.3)

| Hex | Dec | Mnemonic | Immediates | Stack effect (table text) | Pops→Pushes | Category | Cycles K8 / K32 / K32F / K32D | Soft on | Summary (table text) | VM implementation (`KvmMachine.cs`) |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `0x00` | 0 | `NOP` | — | — | 0→0 | `stack` | 2 / 1 / 1 / 1 | — | Does nothing. | No effect; only the cycle cost and the bytecode counter advance (`KvmMachine.cs:1653`). |
| `0x01` | 1 | `BRK` | — | — (stops in the debugger, otherwise a NOP) | 0→0 | `stack` | 2 / 1 / 1 / 1 | — | Breakpoint: stops in the debugger, otherwise a NOP. | Executed exactly like `NOP`; `RunTask` has no breakpoint hook (`KvmMachine.cs:1654`). |
| `0x02` | 2 | `TRAP` | `u8` code | — (raises `USER` with the code) | 0→0 | `stack` | 2 / 1 / 1 / 1 | — | Raises the USER trap with code code (what a failed assert() compiles to). | Throws the `USER` trap with `UserCode = (byte)code` (`:826-827`). |
| `0x08` | 8 | `PUSH8` | `i8` | → [sign-extended value] | 0→1 | `stack` | 2 / 1 / 1 / 1 | — | Pushes a sign-extended 8-bit constant. | Pushes `(uint)A`; the loader already sign-extended the `i8` (`:828-832`, `ImageReader.cs:315`). |
| `0x09` | 9 | `PUSH16` | `i16` | → [sign-extended value] | 0→1 | `stack` | 2 / 1 / 1 / 1 | — | Pushes a sign-extended 16-bit constant. | Pushes `(uint)A`; the loader already sign-extended the `i16` (`:828-832`, `ImageReader.cs:321`). |
| `0x0A` | 10 | `PUSH32` | `u32` | → [value] (int or float bits) | 0→1 | `const32` | 4 / 2 / 2 / 1 | — | Pushes a 32-bit constant: an integer or a float32 bit pattern. | Pushes the raw 32-bit pattern (`:828-832`). |
| `0x10` | 16 | `DUP` | — | [a] → [a a] | 1→2 | `stack` | 2 / 1 / 1 / 1 | — | Duplicates the top cell. | `Need(1)` then push (`:833-836`). |
| `0x11` | 17 | `DROP` | — | [a] → [] | 1→0 | `stack` | 2 / 1 / 1 / 1 | — | Discards the top cell. | `Need(1)`, depth−1 (`:837-840`). |
| `0x12` | 18 | `SWAP` | — | [a b] → [b a] | 2→2 | `stack` | 2 / 1 / 1 / 1 | — | Swaps the top two cells. | `Need(2)` (`:841-848`). |
| `0x13` | 19 | `OVER` | — | [a b] → [a b a] | 2→3 | `stack` | 2 / 1 / 1 / 1 | — | Copies the second cell to the top. | `Need(2)` (`:849-852`). |
| `0x80` | 128 | `PUSH64` | `u64` | → [v:64] | 0→2 | `stack64` | 4 / 2 / 2 / 2 | — | Pushes a 64-bit constant (int64 or float64) as two cells. | Pushes two cells: low 32 bits in the deeper cell, high 32 bits on top (`:853-855`, `Cell64` `:1687`). |
| `0x81` | 129 | `DUP2` | — | [a:64] → [a:64 a:64] | 2→4 | `stack64` | 4 / 2 / 2 / 2 | — | Duplicates the top 64-bit value. | `Need(2)`; copies the top two cells (`:856-859`). |
| `0x82` | 130 | `DROP2` | — | [a:64] → [] | 2→0 | `stack64` | 4 / 2 / 2 / 2 | — | Discards the top 64-bit value. | `Need(2)`, depth−2 (`:860-863`). |
| `0x83` | 131 | `SWAP2` | — | [a:64 b:64] → [b:64 a:64] | 4→4 | `stack64` | 4 / 2 / 2 / 2 | — | Swaps the top two 64-bit values. | `Need(4)` (`:864-871`). |
| `0x84` | 132 | `OVER2` | — | [a:64 b:64] → [a:64 b:64 a:64] | 4→6 | `stack64` | 4 / 2 / 2 / 2 | — | Copies the second 64-bit value to the top. | `Need(4)` (`:872-875`). |

# Memory (spec §4.4)

| Hex | Dec | Mnemonic | Immediates | Stack effect (table text) | Pops→Pushes | Category | Cycles K8 / K32 / K32F / K32D | Soft on | Summary (table text) | VM implementation (`KvmMachine.cs`) |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `0x18` | 24 | `LDL` | `u16` off | → [word at FP+off] | 0→1 | `mem32` | 8 / 2 / 2 / 1 | — | Loads the 32-bit local or argument at FP+off. | `Load32(FP + off)`; no pop (`:876-878`). |
| `0x19` | 25 | `STL` | `u16` off | [v] → [] | 1→0 | `mem32` | 8 / 2 / 2 / 1 | — | Stores to the 32-bit local or argument at FP+off. | `Store32(FP + off, top)` (`:879-883`). |
| `0x1A` | 26 | `ADDRL` | `u16` off | → [FP+off] | 0→1 | `addr` | 6 / 2 / 2 / 1 | — | Pushes the address FP+off (the address of a local). | Pushes `FP + off`; no memory access, so no trap here (`:884-886`). |
| `0x1B` | 27 | `LDG` | `u32` addr | → [word at addr] | 0→1 | `mem32` | 8 / 2 / 2 / 1 | — | Loads the 32-bit word at a fixed address (a global, a bus buffer field, sys.*). | `Load32(addr)`; reaches RAM, sensor, command and system blocks through `Resolve` (`:887-889`). |
| `0x1C` | 28 | `STG` | `u32` addr | [v] → [] | 1→0 | `mem32` | 8 / 2 / 2 / 1 | — | Stores a 32-bit word to a fixed address. | `Store32(addr, top)` (`:890-894`). |
| `0x20` | 32 | `LDW` | — | [addr] → [word] | 1→1 | `mem32` | 8 / 2 / 2 / 1 | — | Loads a 32-bit word through a pointer. | Replaces the address on top with the word; 4-byte aligned (`:895-898`). |
| `0x21` | 33 | `STW` | — | [addr v] → [] | 2→0 | `mem32` | 8 / 2 / 2 / 1 | — | Stores a 32-bit word through a pointer. | Address is the second cell, value is on top (`:899-903`). |
| `0x22` | 34 | `LDH` | — | [addr] → [sign- / zero-extended half] | 1→1 | `mem16` | 4 / 2 / 2 / 1 | — | Loads a 16-bit half, sign-extended. | 2-byte aligned; sign-extends (`:904-907`). |
| `0x23` | 35 | `LDHU` | — | [addr] → [sign- / zero-extended half] | 1→1 | `mem16` | 4 / 2 / 2 / 1 | — | Loads a 16-bit half, zero-extended. | 2-byte aligned; zero-extends (`:908-911`). |
| `0x24` | 36 | `STH` | — | [addr v] → [] (low 16 bits) | 2→0 | `mem16` | 4 / 2 / 2 / 1 | — | Stores the low 16 bits. | Writes the low 16 bits; 2-byte aligned (`:912-916`). |
| `0x25` | 37 | `LDB` | — | [addr] → [sign- / zero-extended byte] | 1→1 | `mem8` | 2 / 2 / 2 / 1 | — | Loads a byte, sign-extended. | Sign-extends (`:917-920`). |
| `0x26` | 38 | `LDBU` | — | [addr] → [sign- / zero-extended byte] | 1→1 | `mem8` | 2 / 2 / 2 / 1 | — | Loads a byte, zero-extended. | Zero-extends (`:921-924`). |
| `0x27` | 39 | `STB` | — | [addr v] → [] (low 8 bits) | 2→0 | `mem8` | 2 / 2 / 2 / 1 | — | Stores the low 8 bits. | Writes the low 8 bits (`:925-929`). |
| `0x28` | 40 | `COPY` | `u16` n | [dst src] → [] (copies n bytes) | 2→0 | `block` | 4+8w / 4+2w / 4+2w / 4+1w | — | Copies n bytes (memcpy; struct assignment). | `dst` is the second cell, `src` on top. Resolves `src` (read) then `dst` (write) with alignment 1 and uses `Span.CopyTo`, which is overlap-safe. With `n = 0` `Resolve` returns an empty span without any address check (`:930-939`, `:490-493`). |
| `0x29` | 41 | `ZERO` | `u16` n | [dst] → [] (zeroes n bytes) | 1→0 | `block` | 4+8w / 4+2w / 4+2w / 4+1w | — | Zeroes n bytes. | Resolves for write, alignment 1, clears `n` bytes (`:940-944`). |
| `0x2A` | 42 | `INDEX` | `u16` size | [base i] → [base + i·size] | 2→1 | `addr` | 6 / 2 / 2 / 1 | — | Array addressing: base + i * size. | `base + (int)i * (int)size`, 32-bit wrapping; no bounds check (`:945-952`). |
| `0x2B` | 43 | `BOUNDS` | `u32` count | [i] → [i], traps `BOUNDS` unless 0 ≤ i < count (unsigned) | 1→1 | `bounds` | 6 / 2 / 2 / 2 | — | Traps BOUNDS unless 0 ≤ i < count (unsigned). kC inserts it on array accesses. | Unsigned `i >= count` throws `BOUNDS`; `i` stays on the stack (`:953-959`). |
| `0x85` | 133 | `LDL64` | `u16` off | → [v:64 at FP+off] | 0→2 | `mem64` | 16 / 4 / 4 / 2 | — | Loads the 64-bit local at FP+off. | `Load64(FP + off)`; 8-byte aligned (`:960-962`). |
| `0x86` | 134 | `STL64` | `u16` off | [v:64] → [] | 2→0 | `mem64` | 16 / 4 / 4 / 2 | — | Stores to the 64-bit local at FP+off. | `Need(2)`; 8-byte aligned (`:963-967`). |
| `0x87` | 135 | `LDG64` | `u32` addr | → [v:64 at addr] | 0→2 | `mem64` | 16 / 4 / 4 / 2 | — | Loads the 64-bit value at a fixed address. | 8-byte aligned (`:968-970`). |
| `0x88` | 136 | `STG64` | `u32` addr | [v:64] → [] | 2→0 | `mem64` | 16 / 4 / 4 / 2 | — | Stores a 64-bit value to a fixed address. | `Need(2)`; 8-byte aligned (`:971-975`). |
| `0x89` | 137 | `LD64` | — | [addr] → [v:64] | 1→2 | `mem64` | 16 / 4 / 4 / 2 | — | Loads a 64-bit value through a pointer (8-byte aligned). | Pops the address, pushes two cells (`:976-983`). |
| `0x8A` | 138 | `ST64` | — | [addr v:64] → [] | 3→0 | `mem64` | 16 / 4 / 4 / 2 | — | Stores a 64-bit value through a pointer (8-byte aligned). | `Need(3)`: address is the third cell, the value the top two (`:984-988`). |

# Control flow and services (spec §4.5)

| Hex | Dec | Mnemonic | Immediates | Stack effect (table text) | Pops→Pushes | Category | Cycles K8 / K32 / K32F / K32D | Soft on | Summary (table text) | VM implementation (`KvmMachine.cs`) |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `0x70` | 112 | `JMP` | `rel16` | — | 0→0 | `branch` | 2 / 3 / 3 / 2 | — | Jumps to a label. | `A` was rewritten at load to the target instruction index; a jump must stay inside its function and may not target the function's `ENTER` (`:989-991`, `ImageReader.cs:429-449`). |
| `0x71` | 113 | `JZ` | `rel16` | [c] → [] (jumps if c == 0) | 1→0 | `branch` | 2 / 3 / 3 / 2 | — | Jumps to a label if c == 0. | Pops, jumps when the cell is 0 (`:992-999`). |
| `0x72` | 114 | `JNZ` | `rel16` | [c] → [] (jumps if c ≠ 0) | 1→0 | `branch` | 2 / 3 / 3 / 2 | — | Jumps to a label if c != 0. | Pops, jumps when the cell is not 0 (`:1000-1007`). |
| `0x73` | 115 | `CALL` | `u16` addr | — (near call: pushes the return entry and jumps to an `ENTER` below 64 KB) | run time | `call` | 6 / 3 / 3 / 2 | — | Near call to a function in the first 64 KB of code. | `RETURN_OVERFLOW` if the return stack is at `MaxCallDepth`. Pushes a return entry `(next pc, FP, floor, entered=false)` and jumps to the pre-resolved `ENTER`. The loader rejects a `CALL` target at or above 64 KB (`:1008-1024`, `ImageReader.cs:450-464`). |
| `0x74` | 116 | `RET` | `u8` r | [v1 … vr] → [v1 … vr] (checks the depth, frees the frame, restores FP, floor and PC; rule 9) | run time | `ret` | 6 / 3 / 3 / 2 | — | Returns r result cells; the operand stack must hold exactly those r cells above the function's floor. | `depth < floor + r` → `OPERAND_UNDERFLOW`; `depth > floor + r` → `OPERAND_OVERFLOW`. Frees the frame (`SP = FP`), restores FP/floor/PC from the return entry. Popping the last entry completes the task (and first copies its publications). Loader: `r ≤ 8` (`:1063-1106`, `ImageReader.cs:471-476`). |
| `0x75` | 117 | `ENTER` | `u8` ncells, `u16` frame | [a1 … an] → [] (rule 5) | run time | `enter` | 12+8w / 6+1w / 6+1w / 4+1w | — | Function prologue: moves ncells argument cells into a fresh, zeroed frame of frame bytes. .func generates it. | `BAD_JUMP` unless reached by a call (return stack empty or top entry already entered); `STACK_OVERFLOW` if `SP + frame` passes the task's RAM stack; `OPERAND_UNDERFLOW` if fewer than `ncells` cells above the floor. Sets `FP = SP`, `SP += frame`, moves the argument cells to `FP + 4·i` (first argument at offset 0), zeroes the rest of the frame, sets the floor to the new depth. Loader: `frame % 8 == 0`, `4·ncells ≤ frame` (`:1025-1062`, `ImageReader.cs:465-470`). |
| `0x76` | 118 | `CALLF` | `u24` addr | — (far call: an `ENTER` at any code address) | run time | `callf` | 8 / 4 / 4 / 2 | — | Far call to a function anywhere in code. | Same code path as `CALL`; no 64 KB limit on the target (`:1008-1024`). |
| `0x77` | 119 | `YIELD` | — | — (suspends the `program` task until the next grid tick, §3.4) | 0→0 | `yield` | 10 / 4 / 4 / 4 | — | Suspends the .program task until the next tick (traps BAD_YIELD anywhere else). | `BAD_YIELD` unless the running task's kind is `Program`. Charges and performs the task's publication copies, saves the registers with PC at the next instruction and suspends until the next tick (`:1107-1121`). |
| `0x78` | 120 | `SYSCALL` | `u16` id | Service-defined (§4.8) | run time | `syscall` | 20+svc / 10+svc / 10+svc / 10+svc | — | Calls service id (see *Syscalls*); pops its argument cells, pushes its result cells. | See [services and the syscall ABI](/vm/services-syscall-abi.md) (`:1122-1194`). |

Static rules the loader enforces for this group (`ImageReader.cs:365-478`):[^image-reader]

| Rule name | Check |
| --- | --- |
| `code` | The code starts with an `ENTER`; every function (the span between two `ENTER`s) ends in `RET` or `JMP`. |
| `jump-target` | A `JMP`/`JZ`/`JNZ` target is an instruction boundary inside the code, inside the same function, and is not that function's `ENTER`. |
| `call-target` | A `CALL`/`CALLF` target is an `ENTER`; a `CALL` target is below 64 KB (65,536). |
| `enter` | `frame % 8 == 0` and `4·ncells ≤ frame`; a task entry's `ENTER` has `ncells == 0`. |
| `ret` | `RET r` has `r ≤ 8`. |
| `syscall` | The `SYSCALL` id is a key of `GameTables.Services` (`ImageReader.cs:557-560`). |

# 32-bit arithmetic, comparison and conversion (spec §4.6)

| Hex | Dec | Mnemonic | Immediates | Stack effect (table text) | Pops→Pushes | Category | Cycles K8 / K32 / K32F / K32D | Soft on | Summary (table text) | VM implementation (`KvmMachine.cs`) |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `0x30` | 48 | `ADD` | — | [a b] → [a op b] | 2→1 | `i32.alu` | 4 / 1 / 1 / 1 | — | Integer add (wraps). | Wrapping `uint` add (`:1195`). |
| `0x31` | 49 | `SUB` | — | [a b] → [a op b] | 2→1 | `i32.alu` | 4 / 1 / 1 / 1 | — | Integer subtract (wraps). | Wrapping `uint` subtract (`:1198`). |
| `0x32` | 50 | `MUL` | — | [a b] → [a·b] | 2→1 | `i32.mul` | 40 / 1 / 1 / 4 | — | Integer multiply (low 32 bits). | Low 32 bits of the product (`:1201`). |
| `0x33` | 51 | `DIV` | — | [a b] → [a op b]. `DIV` truncates toward zero; `REM` has the sign of `a`; `DIVU`, `REMU` unsigned (§4.9) | 2→1 | `i32.div` | 600 / 10* / 8 / 19 | K8 | Signed divide, truncating toward zero. Traps DIV_ZERO. | Divisor 0 → `DIV_ZERO`. Divisor −1 is special-cased to `−a` (so `INT_MIN / −1 = INT_MIN`, no host exception); otherwise C# truncating division (`:1204-1216`). |
| `0x34` | 52 | `DIVU` | — | [a b] → [a op b]. `DIV` truncates toward zero; `REM` has the sign of `a`; `DIVU`, `REMU` unsigned (§4.9) | 2→1 | `i32.div` | 600 / 10* / 8 / 19 | K8 | Unsigned divide. Traps DIV_ZERO. | Divisor 0 → `DIV_ZERO` (`:1230-1241`). |
| `0x35` | 53 | `REM` | — | [a b] → [a op b]. `DIV` truncates toward zero; `REM` has the sign of `a`; `DIVU`, `REMU` unsigned (§4.9) | 2→1 | `i32.div` | 600 / 10* / 8 / 19 | K8 | Signed remainder (sign of a). Traps DIV_ZERO. | Divisor 0 → `DIV_ZERO`. Divisor −1 gives 0; otherwise C# `%` (sign of the dividend) (`:1217-1229`). |
| `0x36` | 54 | `REMU` | — | [a b] → [a op b]. `DIV` truncates toward zero; `REM` has the sign of `a`; `DIVU`, `REMU` unsigned (§4.9) | 2→1 | `i32.div` | 600 / 10* / 8 / 19 | K8 | Unsigned remainder. Traps DIV_ZERO. | Divisor 0 → `DIV_ZERO` (`:1242-1253`). |
| `0x37` | 55 | `NEG` | — | [a] → [−a] | 1→1 | `i32.alu` | 4 / 1 / 1 / 1 | — | Integer negate. | `0 − a` (`:1254-1257`). |
| `0x38` | 56 | `AND` | — | [a b] → [a op b] | 2→1 | `i32.alu` | 4 / 1 / 1 / 1 | — | Bitwise and. | (`:1258`) |
| `0x39` | 57 | `OR` | — | [a b] → [a op b] | 2→1 | `i32.alu` | 4 / 1 / 1 / 1 | — | Bitwise or. | (`:1261`) |
| `0x3A` | 58 | `XOR` | — | [a b] → [a op b] | 2→1 | `i32.alu` | 4 / 1 / 1 / 1 | — | Bitwise exclusive or. | (`:1264`) |
| `0x3B` | 59 | `NOT` | — | [a] → [~a] | 1→1 | `i32.alu` | 4 / 1 / 1 / 1 | — | Bitwise not. | (`:1267-1270`) |
| `0x3C` | 60 | `SHL` | — | [a b] → [a op (b & 31)]. `SHL` fills with zeros; `SHR` logical (zero-fill); `SAR` arithmetic (sign-fill) | 2→1 | `i32.shift` | 30 / 1 / 1 / 1 | — | Shift left; count is b & 31. | Count masked with `& 0x1F` (`:1271`). |
| `0x3D` | 61 | `SHR` | — | [a b] → [a op (b & 31)]. `SHL` fills with zeros; `SHR` logical (zero-fill); `SAR` arithmetic (sign-fill) | 2→1 | `i32.shift` | 30 / 1 / 1 / 1 | — | Logical shift right (zero fill); count is b & 31. | Logical; count masked with `& 0x1F` (`:1274`). |
| `0x3E` | 62 | `SAR` | — | [a b] → [a op (b & 31)]. `SHL` fills with zeros; `SHR` logical (zero-fill); `SAR` arithmetic (sign-fill) | 2→1 | `i32.shift` | 30 / 1 / 1 / 1 | — | Arithmetic shift right (sign fill); count is b & 31. | Arithmetic; count masked with `& 0x1F` (`:1277`). |
| `0x40` | 64 | `EQ` | — | [a b] → [0 or 1] | 2→1 | `i32.alu` | 4 / 1 / 1 / 1 | — | a == b | (`:1280`) |
| `0x41` | 65 | `NE` | — | [a b] → [0 or 1] | 2→1 | `i32.alu` | 4 / 1 / 1 / 1 | — | a != b | (`:1283`) |
| `0x42` | 66 | `LT` | — | [a b] → [0 or 1] | 2→1 | `i32.alu` | 4 / 1 / 1 / 1 | — | a < b, signed | Signed (`:1286`). |
| `0x43` | 67 | `LTU` | — | [a b] → [0 or 1] | 2→1 | `i32.alu` | 4 / 1 / 1 / 1 | — | a < b, unsigned | Unsigned (`:1289`). |
| `0x44` | 68 | `LE` | — | [a b] → [0 or 1] | 2→1 | `i32.alu` | 4 / 1 / 1 / 1 | — | a <= b, signed | Signed (`:1292`). |
| `0x45` | 69 | `LEU` | — | [a b] → [0 or 1] | 2→1 | `i32.alu` | 4 / 1 / 1 / 1 | — | a <= b, unsigned | Unsigned (`:1295`). |
| `0x46` | 70 | `GT` | — | [a b] → [0 or 1] | 2→1 | `i32.alu` | 4 / 1 / 1 / 1 | — | a > b, signed | Signed (`:1298`). |
| `0x47` | 71 | `GTU` | — | [a b] → [0 or 1] | 2→1 | `i32.alu` | 4 / 1 / 1 / 1 | — | a > b, unsigned | Unsigned (`:1301`). |
| `0x48` | 72 | `GE` | — | [a b] → [0 or 1] | 2→1 | `i32.alu` | 4 / 1 / 1 / 1 | — | a >= b, signed | Signed (`:1304`). |
| `0x49` | 73 | `GEU` | — | [a b] → [0 or 1] | 2→1 | `i32.alu` | 4 / 1 / 1 / 1 | — | a >= b, unsigned | Unsigned (`:1307`). |
| `0x50` | 80 | `FADD` | — | [a b] → [a op b] | 2→1 | `f32.add` | 110 / 50 / 1 / 1 | K8, K32 | float32 add. | Host `float` add; a NaN result is replaced by the canonical NaN `0x7FC00000` and, when no input was NaN, recorded as the machine's first NaN (`:1310`, `FBin` `:1715-1722`, `CF` `:596-607`). |
| `0x51` | 81 | `FSUB` | — | [a b] → [a op b] | 2→1 | `f32.add` | 110 / 50 / 1 / 1 | K8, K32 | float32 subtract. | As `FADD` (`:1313`). |
| `0x52` | 82 | `FMUL` | — | [a b] → [a·b] | 2→1 | `f32.mul` | 150 / 50 / 1 / 1 | K8, K32 | float32 multiply. | As `FADD` (`:1316`). |
| `0x53` | 83 | `FDIV` | — | [a b] → [a/b] | 2→1 | `f32.div` | 480 / 150 / 14 / 17 | K8, K32 | float32 divide (÷0 gives ±∞ or NaN, never traps). | As `FADD`; ÷0 yields ±∞ or the canonical NaN, never a trap (`:1319`). |
| `0x54` | 84 | `FSQRT` | — | [a] → [√a] | 1→1 | `f32.sqrt` | 480 / 300 / 14 / 100 | K8, K32, K32D | float32 square root. | `MathF.Sqrt`; canonical NaN and first-NaN record as `FADD` (`:1322-1328`). |
| `0x55` | 85 | `FNEG` | — | [a] → [op a] (bit operations, NaN keeps its payload) | 1→1 | `f32.sign` | 4 / 1 / 1 / 1 | — | float32 negate (sign bit flip). | `cell ^ 0x80000000`; NaN payload is kept, nothing is canonicalised (`:1329-1332`). |
| `0x56` | 86 | `FABS` | — | [a] → [op a] (bit operations, NaN keeps its payload) | 1→1 | `f32.sign` | 4 / 1 / 1 / 1 | — | float32 absolute value (sign bit clear). | `cell & 0x7FFFFFFF`; NaN payload is kept (`:1333-1336`). |
| `0x58` | 88 | `FEQ` | — | [a b] → [0 or 1] | 2→1 | `f32.cmp` | 60 / 20 / 2 / 2 | K8, K32 | float a == b | Host comparison; false if either operand is NaN (`:1337`, `FCmp` `:1723-1730`). |
| `0x59` | 89 | `FNE` | — | [a b] → [0 or 1] | 2→1 | `f32.cmp` | 60 / 20 / 2 / 2 | K8, K32 | float a != b | True if either operand is NaN (`:1340`). |
| `0x5A` | 90 | `FLT` | — | [a b] → [0 or 1] | 2→1 | `f32.cmp` | 60 / 20 / 2 / 2 | K8, K32 | float a < b | (`:1343`) |
| `0x5B` | 91 | `FLE` | — | [a b] → [0 or 1] | 2→1 | `f32.cmp` | 60 / 20 / 2 / 2 | K8, K32 | float a <= b | (`:1346`) |
| `0x5C` | 92 | `FGT` | — | [a b] → [0 or 1] | 2→1 | `f32.cmp` | 60 / 20 / 2 / 2 | K8, K32 | float a > b | (`:1349`) |
| `0x5D` | 93 | `FGE` | — | [a b] → [0 or 1] | 2→1 | `f32.cmp` | 60 / 20 / 2 / 2 | K8, K32 | float a >= b | (`:1352`) |
| `0x60` | 96 | `I2F` | — | [i] → [f] (round to nearest even) | 1→1 | `cvt.i32f32` | 60 / 30 / 1 / 4 | K8, K32 | int32 to float32 (round to nearest even). | `(float)(int)cell` (`:1355-1358`). |
| `0x61` | 97 | `U2F` | — | [i] → [f] (round to nearest even) | 1→1 | `cvt.i32f32` | 60 / 30 / 1 / 4 | K8, K32 | uint32 to float32 (round to nearest even). | `(float)(uint)cell` (`:1359-1362`). |
| `0x62` | 98 | `F2I` | — | [f] → [i] (truncate, saturate, NaN → 0) | 1→1 | `cvt.i32f32` | 60 / 30 / 1 / 4 | K8, K32 | float32 to int32 (truncate, saturate, NaN → 0). | `Conv.FloatToInt`: NaN → 0, saturates at `INT_MAX`/`INT_MIN`, else truncates (`:1363-1366`, `Conv.cs:103-118`). |
| `0x63` | 99 | `F2U` | — | [f] → [i] (truncate, saturate, NaN → 0) | 1→1 | `cvt.i32f32` | 60 / 30 / 1 / 4 | K8, K32 | float32 to uint32 (truncate, saturate, NaN → 0). | `Conv.FloatToUInt`: NaN or ≤ 0 → 0, ≥ 2³² → `UINT_MAX` (`:1367-1370`, `Conv.cs:120-131`). |
| `0x64` | 100 | `SEXT8` | — | [a] → [extended a] | 1→1 | `i32.alu` | 4 / 1 / 1 / 1 | — | Sign-extend the low 8 bits. | (`:1371`) |
| `0x65` | 101 | `SEXT16` | — | [a] → [extended a] | 1→1 | `i32.alu` | 4 / 1 / 1 / 1 | — | Sign-extend the low 16 bits. | (`:1375`) |
| `0x66` | 102 | `ZEXT8` | — | [a] → [extended a] | 1→1 | `i32.alu` | 4 / 1 / 1 / 1 | — | Zero-extend the low 8 bits. | (`:1379`) |
| `0x67` | 103 | `ZEXT16` | — | [a] → [extended a] | 1→1 | `i32.alu` | 4 / 1 / 1 / 1 | — | Zero-extend the low 16 bits. | (`:1383`) |

# 64-bit integers and doubles (spec §4.7)

| Hex | Dec | Mnemonic | Immediates | Stack effect (table text) | Pops→Pushes | Category | Cycles K8 / K32 / K32F / K32D | Soft on | Summary (table text) | VM implementation (`KvmMachine.cs`) |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `0x8B` | 139 | `ADD64` | — | [a:64 b:64] → [a op b:64] | 4→2 | `i64.alu` | 8 / 2 / 2 / 2 | — | int64 add. | (`:1387`, `Bin64` `:1676-1682`) |
| `0x8C` | 140 | `SUB64` | — | [a:64 b:64] → [a op b:64] | 4→2 | `i64.alu` | 8 / 2 / 2 / 2 | — | int64 subtract. | (`:1390`) |
| `0x8D` | 141 | `MUL64` | — | [a:64 b:64] → [a·b:64] | 4→2 | `i64.mul` | 200 / 20 / 4 / 10 | K8, K32 | int64 multiply. | Low 64 bits (`:1393`). |
| `0x8E` | 142 | `DIV64` | — | [a:64 b:64] → [a op b:64]. `DIV64` truncates toward zero; `REM64` has the sign of `a`; `DIVU64`, `REMU64` unsigned (§4.9) | 4→2 | `i64.div` | 3000 / 300 / 100 / 200 | K8, K32, K32F, K32D | int64 signed divide. Traps DIV_ZERO. | Divisor 0 → `DIV_ZERO`; divisor −1 → `−a` (`:1396-1408`). |
| `0x8F` | 143 | `DIVU64` | — | [a:64 b:64] → [a op b:64]. `DIV64` truncates toward zero; `REM64` has the sign of `a`; `DIVU64`, `REMU64` unsigned (§4.9) | 4→2 | `i64.div` | 3000 / 300 / 100 / 200 | K8, K32, K32F, K32D | uint64 divide. Traps DIV_ZERO. | Divisor 0 → `DIV_ZERO` (`:1422-1433`). |
| `0x90` | 144 | `REM64` | — | [a:64 b:64] → [a op b:64]. `DIV64` truncates toward zero; `REM64` has the sign of `a`; `DIVU64`, `REMU64` unsigned (§4.9) | 4→2 | `i64.div` | 3000 / 300 / 100 / 200 | K8, K32, K32F, K32D | int64 signed remainder. Traps DIV_ZERO. | Divisor 0 → `DIV_ZERO`; divisor −1 → 0 (`:1409-1421`). |
| `0x91` | 145 | `REMU64` | — | [a:64 b:64] → [a op b:64]. `DIV64` truncates toward zero; `REM64` has the sign of `a`; `DIVU64`, `REMU64` unsigned (§4.9) | 4→2 | `i64.div` | 3000 / 300 / 100 / 200 | K8, K32, K32F, K32D | uint64 remainder. Traps DIV_ZERO. | Divisor 0 → `DIV_ZERO` (`:1434-1445`). |
| `0x92` | 146 | `NEG64` | — | [a:64] → [−a:64] | 2→2 | `i64.alu` | 8 / 2 / 2 / 2 | — | int64 negate. | (`:1446-1449`) |
| `0x93` | 147 | `AND64` | — | [a:64 b:64] → [a op b:64] | 4→2 | `i64.alu` | 8 / 2 / 2 / 2 | — | 64-bit and. | (`:1450`) |
| `0x94` | 148 | `OR64` | — | [a:64 b:64] → [a op b:64] | 4→2 | `i64.alu` | 8 / 2 / 2 / 2 | — | 64-bit or. | (`:1453`) |
| `0x95` | 149 | `XOR64` | — | [a:64 b:64] → [a op b:64] | 4→2 | `i64.alu` | 8 / 2 / 2 / 2 | — | 64-bit exclusive or. | (`:1456`) |
| `0x96` | 150 | `NOT64` | — | [a:64] → [~a:64] | 2→2 | `i64.alu` | 8 / 2 / 2 / 2 | — | 64-bit not. | (`:1459-1462`) |
| `0x97` | 151 | `SHL64` | — | [a:64 b] → [a op (b & 63):64] (shift count is one 32-bit cell). `SHL64` fills with zeros; `SHR64` logical; `SAR64` arithmetic | 3→2 | `i64.shift` | 60 / 4 / 4 / 4 | — | 64-bit shift left; the count is one 32-bit cell, b & 63. | `Need(3)`; the count is the single top cell, masked with `& 0x3F` (`:1463`, `Shift` `:1805-1811`). |
| `0x98` | 152 | `SHR64` | — | [a:64 b] → [a op (b & 63):64] (shift count is one 32-bit cell). `SHL64` fills with zeros; `SHR64` logical; `SAR64` arithmetic | 3→2 | `i64.shift` | 60 / 4 / 4 / 4 | — | 64-bit logical shift right. | Logical (`:1466`). |
| `0x99` | 153 | `SAR64` | — | [a:64 b] → [a op (b & 63):64] (shift count is one 32-bit cell). `SHL64` fills with zeros; `SHR64` logical; `SAR64` arithmetic | 3→2 | `i64.shift` | 60 / 4 / 4 / 4 | — | 64-bit arithmetic shift right. | Arithmetic (`:1469`). |
| `0x9A` | 154 | `EQ64` | — | [a:64 b:64] → [0 or 1] | 4→1 | `i64.cmp` | 8 / 2 / 2 / 2 | — | int64 == | Pops four cells, pushes one (`:1472`, `Cmp` `:1691-1698`). |
| `0x9B` | 155 | `NE64` | — | [a:64 b:64] → [0 or 1] | 4→1 | `i64.cmp` | 8 / 2 / 2 / 2 | — | int64 != | (`:1475`) |
| `0x9C` | 156 | `LT64` | — | [a:64 b:64] → [0 or 1] | 4→1 | `i64.cmp` | 8 / 2 / 2 / 2 | — | int64 <, signed | Signed (`:1478`). |
| `0x9D` | 157 | `LTU64` | — | [a:64 b:64] → [0 or 1] | 4→1 | `i64.cmp` | 8 / 2 / 2 / 2 | — | int64 <, unsigned | Unsigned (`:1481`). |
| `0x9E` | 158 | `LE64` | — | [a:64 b:64] → [0 or 1] | 4→1 | `i64.cmp` | 8 / 2 / 2 / 2 | — | int64 <=, signed | Signed (`:1484`). |
| `0x9F` | 159 | `LEU64` | — | [a:64 b:64] → [0 or 1] | 4→1 | `i64.cmp` | 8 / 2 / 2 / 2 | — | int64 <=, unsigned | Unsigned (`:1487`). |
| `0xA0` | 160 | `GT64` | — | [a:64 b:64] → [0 or 1] | 4→1 | `i64.cmp` | 8 / 2 / 2 / 2 | — | int64 >, signed | Signed (`:1490`). |
| `0xA1` | 161 | `GTU64` | — | [a:64 b:64] → [0 or 1] | 4→1 | `i64.cmp` | 8 / 2 / 2 / 2 | — | int64 >, unsigned | Unsigned (`:1493`). |
| `0xA2` | 162 | `GE64` | — | [a:64 b:64] → [0 or 1] | 4→1 | `i64.cmp` | 8 / 2 / 2 / 2 | — | int64 >=, signed | Signed (`:1496`). |
| `0xA3` | 163 | `GEU64` | — | [a:64 b:64] → [0 or 1] | 4→1 | `i64.cmp` | 8 / 2 / 2 / 2 | — | int64 >=, unsigned | Unsigned (`:1499`). |
| `0xA4` | 164 | `DADD` | — | [a:64 b:64] → [a op b:64] | 4→2 | `f64.add` | 400 / 80 / 80 / 1 | K8, K32, K32F | float64 add. | Host `double` add; NaN result → canonical `0x7FF8000000000000`, first-NaN record as `FADD` (`:1502`, `DBin` `:1699-1706`, `CD` `:610-621`). |
| `0xA5` | 165 | `DSUB` | — | [a:64 b:64] → [a op b:64] | 4→2 | `f64.add` | 400 / 80 / 80 / 1 | K8, K32, K32F | float64 subtract. | As `DADD` (`:1505`). |
| `0xA6` | 166 | `DMUL` | — | [a:64 b:64] → [a·b:64] | 4→2 | `f64.mul` | 1000 / 120 / 120 / 2 | K8, K32, K32F | float64 multiply. | As `DADD` (`:1508`). |
| `0xA7` | 167 | `DDIV` | — | [a:64 b:64] → [a/b:64] | 4→2 | `f64.div` | 2500 / 400 / 400 / 31 | K8, K32, K32F | float64 divide. | As `DADD` (`:1511`). |
| `0xA8` | 168 | `DSQRT` | — | [a:64] → [√a:64] | 2→2 | `f64.sqrt` | 3000 / 600 / 600 / 150 | K8, K32, K32F, K32D | float64 square root. | `Math.Sqrt`; canonical NaN (`:1514-1520`). |
| `0xA9` | 169 | `DNEG` | — | [a:64] → [op a:64] (bit operations) | 2→2 | `f64.sign` | 8 / 2 / 2 / 1 | — | float64 negate. | Sign-bit flip; payload kept (`:1521-1524`). |
| `0xAA` | 170 | `DABS` | — | [a:64] → [op a:64] (bit operations) | 2→2 | `f64.sign` | 8 / 2 / 2 / 1 | — | float64 absolute value. | Sign-bit clear; payload kept (`:1525-1528`). |
| `0xAB` | 171 | `DEQ` | — | [a:64 b:64] → [0 or 1] | 4→1 | `f64.cmp` | 150 / 30 / 30 / 2 | K8, K32, K32F | double == | (`:1529`, `DCmp` `:1707-1714`) |
| `0xAC` | 172 | `DNE` | — | [a:64 b:64] → [0 or 1] | 4→1 | `f64.cmp` | 150 / 30 / 30 / 2 | K8, K32, K32F | double != | (`:1532`) |
| `0xAD` | 173 | `DLT` | — | [a:64 b:64] → [0 or 1] | 4→1 | `f64.cmp` | 150 / 30 / 30 / 2 | K8, K32, K32F | double < | (`:1535`) |
| `0xAE` | 174 | `DLE` | — | [a:64 b:64] → [0 or 1] | 4→1 | `f64.cmp` | 150 / 30 / 30 / 2 | K8, K32, K32F | double <= | (`:1538`) |
| `0xAF` | 175 | `DGT` | — | [a:64 b:64] → [0 or 1] | 4→1 | `f64.cmp` | 150 / 30 / 30 / 2 | K8, K32, K32F | double > | (`:1541`) |
| `0xB0` | 176 | `DGE` | — | [a:64 b:64] → [0 or 1] | 4→1 | `f64.cmp` | 150 / 30 / 30 / 2 | K8, K32, K32F | double >= | (`:1544`) |
| `0xB1` | 177 | `I2L` | — | [i] → [l:64] (sign- / zero-extend) | 1→2 | `cvt.i32i64` | 8 / 2 / 2 / 2 | — | int32 to int64 (sign-extend). | Sign-extends (`:1547-1554`). |
| `0xB2` | 178 | `U2L` | — | [i] → [l:64] (sign- / zero-extend) | 1→2 | `cvt.i32i64` | 8 / 2 / 2 / 2 | — | uint32 to int64 (zero-extend). | Zero-extends (`:1555-1562`). |
| `0xB3` | 179 | `L2I` | — | [l:64] → [i] (low 32 bits) | 2→1 | `cvt.i32i64` | 8 / 2 / 2 / 2 | — | int64 to int32 (low 32 bits). | Keeps the low cell (`:1563-1570`). |
| `0xB4` | 180 | `L2D` | — | [l:64] → [d:64] (round to nearest even) | 2→2 | `cvt.i64f` | 200 / 60 / 60 / 20 | K8, K32, K32F, K32D | int64 to double. | `Conv.LongToDouble`, the VM's own single-rounding conversion (`:1571-1574`, `Conv.cs:14-21`). |
| `0xB5` | 181 | `LU2D` | — | [l:64] → [d:64] (round to nearest even) | 2→2 | `cvt.i64f` | 200 / 60 / 60 / 20 | K8, K32, K32F, K32D | uint64 to double. | `Conv.ULongToDouble` (`:1575-1578`, `Conv.cs:8-12`). |
| `0xB6` | 182 | `D2L` | — | [d:64] → [l:64] (truncate, saturate, NaN → 0) | 2→2 | `cvt.i64f` | 200 / 60 / 60 / 20 | K8, K32, K32F, K32D | double to int64 (truncate, saturate, NaN → 0). | `Conv.DoubleToLong`: NaN → 0, saturates (`:1579-1582`, `Conv.cs:81-96`). |
| `0xB7` | 183 | `D2LU` | — | [d:64] → [l:64] (truncate, saturate, NaN → 0) | 2→2 | `cvt.i64f` | 200 / 60 / 60 / 20 | K8, K32, K32F, K32D | double to uint64 (truncate, saturate, NaN → 0). | `Conv.DoubleToULong`: NaN or ≤ −1 → 0, ≥ 2⁶⁴ → `UINT64_MAX` (`:1583-1586`, `Conv.cs:60-79`). |
| `0xB8` | 184 | `I2D` | — | [i] → [d:64] (exact) | 1→2 | `cvt.i64f` | 200 / 60 / 60 / 20 | K8, K32, K32F, K32D | int32 to double (exact). | Exact (`:1587-1594`). |
| `0xB9` | 185 | `U2D` | — | [i] → [d:64] (exact) | 1→2 | `cvt.i64f` | 200 / 60 / 60 / 20 | K8, K32, K32F, K32D | uint32 to double (exact). | Exact (`:1595-1602`). |
| `0xBA` | 186 | `D2I` | — | [d:64] → [i] (truncate, saturate, NaN → 0) | 2→1 | `cvt.i64f` | 200 / 60 / 60 / 20 | K8, K32, K32F, K32D | double to int32 (truncate, saturate, NaN → 0). | `Conv.DoubleToInt` (`:1603-1610`, `Conv.cs:133-148`). |
| `0xBB` | 187 | `D2U` | — | [d:64] → [i] (truncate, saturate, NaN → 0) | 2→1 | `cvt.i64f` | 200 / 60 / 60 / 20 | K8, K32, K32F, K32D | double to uint32 (truncate, saturate, NaN → 0). | `Conv.DoubleToUInt` (`:1611-1618`, `Conv.cs:150-161`). |
| `0xBC` | 188 | `F2D` | — | [f] → [d:64] (exact) | 1→2 | `cvt.f32f64` | 80 / 30 / 30 / 1 | K8, K32, K32F | float to double (exact). | A NaN input becomes the canonical 64-bit NaN; no first-NaN record (`:1619-1626`). |
| `0xBD` | 189 | `D2F` | — | [d:64] → [f] (round to nearest even) | 2→1 | `cvt.f32f64` | 80 / 30 / 30 / 1 | K8, K32, K32F | double to float (round to nearest even). | Host `(float)`; a NaN becomes the canonical 32-bit NaN (`:1627-1634`). |
| `0xBE` | 190 | `L2F` | — | [l:64] → [f] (round to nearest even) | 2→1 | `cvt.i64f` | 200 / 60 / 60 / 20 | K8, K32, K32F, K32D | int64 to float (round to nearest even). | `Conv.LongToFloat`, single rounding (`:1635-1642`, `Conv.cs:29-36`). |
| `0xBF` | 191 | `F2L` | — | [f] → [l:64] (truncate, saturate, NaN → 0) | 1→2 | `cvt.i64f` | 200 / 60 / 60 / 20 | K8, K32, K32F, K32D | float to int64 (truncate, saturate, NaN → 0). | `Conv.FloatToLong` (via `DoubleToLong`) (`:1643-1650`, `Conv.cs:98-101`). |

# Arithmetic rules shared by many opcodes

- **Integer arithmetic wraps**; nothing but division by zero traps.
- **Shifts** mask the count (`& 31` for 32-bit, `& 63` for 64-bit).
- **Float results are canonicalised**: any arithmetic opcode whose host result is a NaN stores the
  canonical quiet NaN (`0x7FC00000` / `0x7FF8000000000000`); `FNEG`, `FABS`, `DNEG`, `DABS` are pure bit
  operations and keep a NaN's payload (`CF`/`CD`, `:596-621`).
- **First-NaN tracking**: the first time an arithmetic opcode turns non-NaN inputs into a NaN, the machine
  stores `NanRecord(bytePc, task)`; the host reports it once (`GetFirstNan()`).
- **Float → integer conversions** truncate toward zero, saturate at the destination range, and map NaN
  to 0.[^conv]
- **64-bit integer ↔ float conversions** use the VM's own code in `Conv` (single rounding, to nearest
  even) rather than the host's casts.[^conv]

`edgecases.json` holds 80 worked examples of these rules; it is reproduced in
[soft routines and floating point](/vm/soft-routines-and-fp.md).

# Modding notes

- The opcode space is closed: `Op` is a compiled enum, `RunTask`'s switch is compiled code, and the loader
  rejects any byte without an `OpcodeInfo`. Adding an opcode means patching the table **and** the
  interpreter **and** both compilers. Extend through `SYSCALL` instead.
- `GameTables.Opcodes` and `OpcodesByMnemonic` have public getters and private setters; the array and
  dictionary objects themselves are mutable. Changing `Category`/`CategoryIndex` on a replaced `OpcodeInfo`
  would change costs for images loaded afterwards, but would also change nothing in
  `CoreCostTable.Hash`, which is computed from `costs.json` content only — so images would still load.
- `SYSCALL` is the only opcode whose stack effect is data (the service row); see
  [services and the syscall ABI](/vm/services-syscall-abi.md).

[^opcodes-json]: KSA.Kfs.Vm.Tables.Data.opcodes.json:1-97 (86 rows; `notes` at line 4).
[^costs-json]: KSA.Kfs.Vm.Tables.Data.costs.json:5-48 (`costs`), 85-87 (`overrides`).
[^op-enum]: Op.cs:3-151.
[^kvm-machine]: KvmMachine.cs:824-1656 (opcode switch), 1669-1811 (helper local functions `Bin`, `Bin64`, `Cmp`, `DBin`, `DCmp`, `FBin`, `FCmp`, `Need`, `Push`, `Push2`, `Shift`).
[^image-reader]: ImageReader.cs:296-306 (unknown opcode / truncated instruction), 365-392, 423-478, 557-560.
[^game-tables]: GameTables.cs:234-270 (`BuildOpcodes`).
[^conv]: Conv.cs:8-162.
