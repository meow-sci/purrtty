---
type: Data Table Reference
title: kVM traps and faults (traps.json, TrapCode, TrapInfo)
description: Every kVM trap code with its table text, the exact places KvmMachine raises it, what a trap does to the machine, and how the host is told.
resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.traps.json
tags: [kfs, vm, kvm, traps, faults, halt, data-table]
sources:
  - id: traps-json
    resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.traps.json
    title: traps.json (embedded)
  - id: trap-code
    resource: ksa-decomp:KSA.Kfs.Vm/TrapCode.cs
    title: TrapCode enum (decompiled)
  - id: trap-info
    resource: ksa-decomp:KSA.Kfs.Vm/TrapInfo.cs
    title: TrapInfo record struct (decompiled)
  - id: kvm-machine
    resource: ksa-decomp:KSA.Kfs.Vm/KvmMachine.cs
    title: KvmMachine (decompiled)
  - id: game-tables
    resource: ksa-decomp:KSA.Kfs.Vm.Tables/GameTables.cs
    title: GameTables.CheckTraps (decompiled)
  - id: host-computer
    resource: ksa-decomp:KSA.Kfs.Host/HostComputer.cs
    title: HostComputer (decompiled) — trap reporting
  - id: builtin-includes
    resource: ksa-decomp:KSA.Kfs.KAsm/BuiltinIncludes.cs
    title: BuiltinIncludes (decompiled) — TRAP_* constants
generated: { by: claude-code/claude-fable-5-2, at: 2026-11-26T06:11:10Z }
status: stable
ksa_build: "2026.11.25.5949"
---

# Summary

A **trap** is the VM's only fault mechanism. It is fatal for the machine: the tick is discarded, the machine
enters the `Halted` phase and stays there until it is reset or a new image is loaded. There are no
recoverable traps, no handlers inside the program, and no exceptions other than this one.[^kvm-machine]

Do not confuse a trap with an **overrun** (a periodic task exhausting the tick budget — the tick is
abandoned but the machine keeps running; see [tasks and scheduling](/vm/tasks-and-scheduling.md)) or with a
**rejected service request** (counted in `sys.rejected_requests`; see
[syscall ABI](/vm/services-syscall-abi.md)).

# traps.json in full

Version 1. `GameTables.CheckTraps` requires the codes to run 1, 2, … in table order; 0 means "no
trap".[^traps-json][^game-tables]

| Code | Name | Cause (table text) |
| --- | --- | --- |
| 1 | `ILLEGAL_OPCODE` | Unknown opcode or truncated instruction |
| 2 | `OPERAND_OVERFLOW` | A push past the task's operand-stack size, or `RET r` with more than `floor + r` cells (§4.5 rules 7, 9) |
| 3 | `OPERAND_UNDERFLOW` | A pop below the current function's operand floor, including by `ENTER` and `SYSCALL`, or `RET r` with fewer than `floor + r` cells (§4.5 rules 7, 9) |
| 4 | `RETURN_OVERFLOW` | Call depth over the limit |
| 5 | `INIT_OVERRUN` | `init` would use more than `init_budget_cycles` in total across its boot ticks (§3.3) |
| 6 | `STACK_OVERFLOW` | The task's RAM stack is out of space |
| 7 | `NULL_ACCESS` | Access below `0x10` |
| 8 | `BAD_ADDRESS` | Access outside a mapped region, past the generated I/O layout, or into the protected VM stack region |
| 9 | `READ_ONLY` | Write to the sensor or system block |
| 10 | `MISALIGNED` | Misaligned access, including 64-bit accesses not on 8-byte boundaries |
| 11 | `DIV_ZERO` | Integer divide or remainder by zero, 32- or 64-bit |
| 12 | `BOUNDS` | Array index out of range (only when bounds checks are on, §4.4) |
| 13 | `USER` | `TRAP` instruction (`assert()` failure) |
| 14 | `BAD_JUMP` | Jump or call target outside the code, or not on an instruction boundary; an `ENTER` not reached by a call (§4.5 rule 4); execution reaching PC ≥ `code_size` (falling off the end of the code) |
| 15 | `BAD_SYSCALL` | Unknown service id; a format argument that isn't a format string in the image (§4.8, §7); for a kernel service (D-109), a gimbal index outside the vehicle's layout or a count n above 1,024. Argument counts are checked statically; the floor rule is the runtime backstop |
| 16 | `BAD_YIELD` | `YIELD` outside a `program` task (§3.4). Never from compiled code; from hand-written kASM only until MS4's reachability check, or from a corrupted image |
| 17 | `MEMORY_ERROR` | Uncorrectable memory error, when error correction is enabled (§9.2, optional) |

`TrapCode : byte` has the same names and values plus `None = 0`.[^trap-code] kASM's `kfs.kinc` exposes each
as a constant `TRAP_<NAME>` = code (`BuiltinIncludes.cs:52-55`).[^builtin-includes]

# Where each trap is raised

Line numbers are in `KvmMachine.cs`.

| Code | Name | Raised by | Lines |
| --- | --- | --- | --- |
| 1 | `ILLEGAL_OPCODE` | `default:` branch of the opcode switch. Unreachable for a validated image, because the loader rejects unassigned opcode bytes and truncated instructions. | 1651-1652 |
| 2 | `OPERAND_OVERFLOW` | `Push2` at `depth >= MaxOperandCells`; `Push` (64-bit) at `depth + 2 > MaxOperandCells`; `RET r` with `depth > floor + r`. Includes result cells pushed by `SYSCALL`. | 1758-1782, 1070-1073 |
| 3 | `OPERAND_UNDERFLOW` | `Need(n)` when `depth − n < floor` (every popping opcode, including `SYSCALL`'s argument cells); `ENTER` when `depth − ncells < floor`; `RET r` with `depth < floor + r`. | 1741-1747, 1042-1045, 1066-1069 |
| 4 | `RETURN_OVERFLOW` | `CALL`/`CALLF` when `rdepth >= MaxCallDepth`. | 1011-1014 |
| 5 | `INIT_OVERRUN` | In the `init` task: the start step, a resume's subscription copies, or any instruction would take cumulative `init` cycles above `image.InitBudgetCycles`. | 701-704, 746-753, 794-797 |
| 6 | `STACK_OVERFLOW` | `ENTER` when `sp + frame > StackBase + StackSize`. | 1038-1041 |
| 7 | `NULL_ACCESS` | `Resolve` with `addr < 16` and `size > 0`. | 496-499 |
| 8 | `BAD_ADDRESS` | `Resolve`: address range in no mapped region (including the VM region and the NV window), or past the sensor/command layout size. | 525-530, 541-544 |
| 9 | `READ_ONLY` | `Resolve`: write to the sensor window or the system block. | 537-540 |
| 10 | `MISALIGNED` | `Resolve`: `addr % align != 0` for 2-, 4- or 8-byte accesses (and the 8-aligned `log` buffer). | 545-548 |
| 11 | `DIV_ZERO` | `DIV`, `REM`, `DIVU`, `REMU`, `DIV64`, `REM64`, `DIVU64`, `REMU64` with a zero divisor. | 1209-1212, 1222-1225, 1234-1237, 1246-1249, 1401-1404, 1414-1417, 1426-1429, 1438-1441 |
| 12 | `BOUNDS` | `BOUNDS count` when the top cell, unsigned, is `>= count`. | 953-959 |
| 13 | `USER` | `TRAP code`; `TrapInfo.UserCode` carries the 8-bit code. | 826-827 |
| 14 | `BAD_JUMP` | `pc` at or past the end of the code; `ENTER` with an empty return stack or whose return entry is already marked entered (an `ENTER` not reached by a call); `RET` to a byte PC that is not an instruction boundary. | 785-789, 1029-1037, 1100-1104 |
| 15 | `BAD_SYSCALL` | `SYSCALL id` where `IsWiredService(id)` is false; the `log` service (id 1) with a first argument that is not a format string index of the image. | 1125-1128, 1151-1156 |
| 16 | `BAD_YIELD` | `YIELD` in a task whose kind is not `Program`. | 1108-1111 |
| 17 | `MEMORY_ERROR` | `Not active in this build:` never raised anywhere in the decompiled code. | — |

Notes on the table text versus the code:

- The `BAD_SYSCALL` cause also mentions "for a kernel service (D-109), a gimbal index outside the vehicle's
  layout or a count n above 1,024". `Not active in this build:` there is no such check in `KvmMachine`; the
  two cases above are the only sources of `BAD_SYSCALL`.
- `BAD_ADDRESS`, `NULL_ACCESS` and `MISALIGNED` can also come from the `log` service reading its argument
  buffer (`:1157`).
- With a validated image `BAD_JUMP` via `pc >= Instrs.Length` cannot be reached by a jump (targets are
  checked at load) but could be reached by falling off the end; the loader's rule that every function ends
  in `RET` or `JMP` closes that too. The `ENTER` checks guard hand-written kASM that jumps or falls into a
  function prologue.

# What a trap does

Inside `RunTask`, opcode handlers throw a private `TrapException(TrapCode code, byte user = 0)`; the single
`catch` at `:1662-1668` does:

```csharp
uint pc3 = (pc < instrs.Length) ? instrs[pc].Pc : image.CodeSize;   // byte PC of the faulting instruction
SaveRegs(pc, fp, sp, depth, rdepth, floor);
_trap = new TrapInfo(ex.Code, pc3, task, ex.User);
return Leave(RunOutcome.Trapped);
```

`INIT_OVERRUN` at task start is recorded without an exception through `Finish` (`:1731-1735`).

```csharp
public readonly record struct TrapInfo(TrapCode Code, uint Pc, int Task, byte UserCode);
```

Effects, in order:[^trap-info][^kvm-machine]

1. **The faulting instruction's cost is already charged** (cycles are added before execution), except for
   `INIT_OVERRUN`, which is detected before charging. Its side effects up to the throw remain: RAM and the
   live command block are **not** rolled back, and a `SYSCALL` that traps on underflow or a bad format has
   not popped its arguments.
2. The task's registers are saved pointing **at** the faulting instruction; `GetTaskState` and the debugger
   views show that state.
3. `RunTick` stops scheduling: remaining periodic tasks and the program task do not run (`:2134-2137, 2156`).
4. The tick is **not latched** — the host keeps the previously latched commands ("commands held") and the
   command stream hash does not advance (`:2181-2193`).
5. All service requests queued in this tick, by any task, are discarded (`_requests.Clear()`, `:2199-2202`).
   Issue-time rejections and the dropped count stay queued and are still delivered by `DrainRequests`.
   That includes the `log` service: a `kfs_logf` line is a queued request (id 1) that the host formats
   and appends to the program log only when the tick's requests are drained, so **whatever the
   trapping tick itself logged before the fault never appears**. The log shows the lines of earlier
   ticks and then the host's trap line (`trap <Code> at 0x<Pc> in task <n> (…): commands held`).[^host-computer-log]
6. One-shot command fields are still zeroed in the live block (`:2194-2198`).
7. The phase becomes `Halted` (`:2203-2207`) and the `TickResult` carries `Trap`.
8. Every later `RunTick` returns immediately with the same `Trap`, `Ran = false`, `Latched = false`
   (`:2091-2094`).

The trap is cleared only by `Reset(cause)` (`:1974`) or a successful `LoadImage` (`:1915`). `SaveState`
stores the trap code, PC, task and user code; `LoadState` requires the halted phase and a non-zero trap code
to agree, the trap code to be ≤ 17, and a user code only with `USER` (`:2408-2423`).

`Halt()` puts the machine in the same `Halted` phase **without** a trap (`Trap` stays null); that is how the
stock `halt` service stops a program.

# How the host learns of a trap

- `TickResult.Trap` (non-null on the trapping tick and on every later tick), `KvmMachine.Trap` and
  `KvmMachine.IsHalted`.
- `HostComputer.RunTick` sets `State = ComputerState.Halted` and, once per trap, writes a console line
  `trap {Code} at 0x{Pc:X} in task {Task} ({function}[, {file}:{line}]): commands held`
  (`HostComputer.cs:445-456`).[^host-computer] The source location comes from `HostComputer.Locate`, which
  uses the image's debug section when present.
- The trap is part of the published `MachineSnapshot` (see [snapshots and perf](/host/snapshots-and-perf.md)).

# Modding notes

- A mod's service handler cannot raise a trap after the fact: handlers run after the tick, when the
  machine has already latched. The supported ways to push back are a host rejection
  (`CountRejectedRequest()` + a console event) or `Halt()`.
- `TrapException` is a private nested class of `KvmMachine`; code called synchronously from inside the
  interpreter (an `ISyscallHost.IssueResult` implementation) must **not** throw — any other exception type
  propagates out of `RunTick` uncaught and leaves the machine mid-tick with unsaved registers.
- `TrapCode` values are persisted in saved state and exposed to programs as `TRAP_*` constants; do not
  renumber.

[^traps-json]: KSA.Kfs.Vm.Tables.Data.traps.json:1-23.
[^trap-code]: TrapCode.cs:3-23.
[^trap-info]: TrapInfo.cs:3.
[^kvm-machine]: KvmMachine.cs:16-27 (`TrapException`), 433-436 (`T`), 1662-1668 (catch), 1731-1740 (`Finish`, `Leave`), 2091-2094 and 2132-2209 (`RunTick` handling), 2077-2083 (`Halt`).
[^game-tables]: GameTables.cs:853-862 (`CheckTraps`).
[^host-computer]: HostComputer.cs:445-460 (trap → `Halted`, console line), 818-826 (`TrapLocation`, `Locate`).
[^builtin-includes]: BuiltinIncludes.cs:52-55.
[^host-computer-log]: HostComputer.cs:439 (`machine.DrainRequests(this)` after the tick), :670-676 (`ISyscallSink.Request`), :747-770 (`ApplyHostRequest` case 1: `LogFormatter.Format` and `Console.Log`), :445-455 (the trap line, written with `Console.Host`, once).
