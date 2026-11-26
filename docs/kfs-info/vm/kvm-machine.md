---
type: Subsystem Reference
title: kVM machine (KvmMachine) — execution model and API
description: How the kVM bytecode machine is constructed, loads an image, executes one tick, and reports the result, with the complete public surface of KvmMachine.
resource: ksa-decomp:KSA.Kfs.Vm/KvmMachine.cs
tags: [kfs, vm, kvm, execution-model, api, extensibility]
sources:
  - id: kvm-machine
    resource: ksa-decomp:KSA.Kfs.Vm/KvmMachine.cs
    title: KvmMachine (decompiled)
  - id: image-reader
    resource: ksa-decomp:KSA.Kfs.Vm.Image/ImageReader.cs
    title: ImageReader (decompiled) — image validation and instruction decoding
  - id: instr
    resource: ksa-decomp:KSA.Kfs.Vm.Image/Instr.cs
    title: Instr (decoded instruction record)
  - id: task-entry
    resource: ksa-decomp:KSA.Kfs.Vm.Image/TaskEntry.cs
    title: TaskEntry (image task record)
  - id: kvm-image
    resource: ksa-decomp:KSA.Kfs.Vm.Image/KvmImage.cs
    title: KvmImage (validated image)
  - id: game-tables
    resource: ksa-decomp:KSA.Kfs.Vm.Tables/GameTables.cs
    title: GameTables (decompiled)
  - id: tick-result
    resource: ksa-decomp:KSA.Kfs.Vm/TickResult.cs
    title: TickResult
  - id: run-outcome
    resource: ksa-decomp:KSA.Kfs.Vm/RunOutcome.cs
    title: RunOutcome
  - id: crc32
    resource: ksa-decomp:KSA.Kfs.Vm/Crc32.cs
    title: Crc32
  - id: host-computer
    resource: ksa-decomp:KSA.Kfs.Host/HostComputer.cs
    title: HostComputer (decompiled) — the only in-game owner of a KvmMachine
  - id: catalogue-json
    resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.catalogue.json
    title: catalogue.json (embedded)
generated: { by: claude-code/claude-fable-5-2, at: 2026-11-25T21:46:43Z }
status: stable
ksa_build: "2026.11.25.5949"
---

# Summary

`KSA.Kfs.Vm.KvmMachine` is the whole kVM interpreter: one `public sealed` class of about 2,580 decompiled
lines that owns the RAM image, the per-task registers, the scheduler, the syscall marshalling and the
state save format.[^kvm-machine] It has **no static mutable state** — every machine is self-contained
and takes its behaviour from two constructor arguments: a `PartDescription` (which processor it is) and a
`GameTables` (the embedded JSON tables). It never touches the game: it reads a sensor byte block the host
writes, writes a command byte block the host reads, and queues `ServiceRequest` records for the host to
drain.

The machine is a **stack machine with 32-bit cells**. A *tick* runs the scheduled tasks against a cycle
budget; an instruction's cycle cost is precomputed when the image is loaded.

Related concepts: [opcodes](/vm/opcodes.md), [memory map and system block](/vm/memory-map-and-sysblock.md),
[tasks and scheduling](/vm/tasks-and-scheduling.md), [cost model](/vm/cost-model-and-budgets.md),
[traps](/vm/traps-and-faults.md), [services / syscall ABI](/vm/services-syscall-abi.md),
[bus slots](/vm/bus-slots-vm-side.md), [state save format](/vm/machine-state-format.md). The image the
machine loads is described in [the image format](/compiler/kvm-image-format.md); the host that drives it in
[host computer](/host/host-computer.md).

# Machine model at a glance

| Aspect | Value | Where |
| --- | --- | --- |
| Cell size | 32 bits, little-endian, for **every** core class | `Cell`/`SetCell2` `KvmMachine.cs:1683-1686, 1801-1804` |
| 64-bit values | two adjacent cells, low half in the deeper cell | `Cell64`/`SetCell` `:1687-1690, 1797-1800` |
| Host requirement | little-endian host, IEEE-754 behaviour verified by a self-test at construction | `:1852-1860` |
| Operand stack | per task, `MaxOperandCells` cells, stored in the protected VM region of RAM | `:664-666` |
| Return stack | per task, `MaxCallDepth` 8-byte entries, stored right after the operand stack | `:665, 1016` |
| RAM (frame) stack | per task, `[StackBase, StackBase + StackSize)` in ordinary RAM | `:668, 1038-1047` |
| Registers (per task) | `pc` (instruction index), `fp`, `sp`, operand depth, return depth, operand `floor` | `:107-117, 1787-1796` |
| Code | not addressable; decoded once into `Instr[]` | `ImageReader.cs:289-364` |
| Cycle budget per tick | `image.BudgetCycles` = `clock_hz / tick_hz` | `:1907`, `ImageReader.cs:769-772` |
| Requests per tick | at most `MAX_REQUESTS_PER_TICK` = 256 | `:157, 1131` |

## Cells, word size and core classes

The interpreter is identical for all four core classes (`K8`, `K32`, `K32F`, `K32D`): cells are 32 bits,
addresses are 32 bits, and all 146 opcodes are executable on every core.[^kvm-machine] The core class
changes only (a) the **cost** of each instruction and whether it counts as a *soft* (library) routine,
(b) the machine limits taken from the part (`MaxOperandCells`, `MaxReturnEntries`, `MaxTasks`, RAM and code
size), and (c) the value reported at `sys.core`. The catalogue defaults give 64 operand cells, 16 return
entries and 16 tasks, and for `K8` 16 operand cells, 8 return entries and 8 tasks.[^catalogue-json][^game-tables]
See [cost model and budgets](/vm/cost-model-and-budgets.md) and
[processor catalogue](/tables/catalogue-processors.md).

`Inferred:` the image flag `FLAG_DOUBLE32` (`KvmImage.Double32`) is a compiler-side convention only — no
code in `KvmMachine` reads it, so the VM itself always implements `D…` opcodes as 64-bit IEEE doubles.

## Per-task state

Each task in the image has its own registers and three stacks. `KvmMachine` keeps the registers in parallel
arrays indexed by task (`_pc`, `_fp`, `_sp`, `_opDepth`, `_retDepth`, `_floor`, `_status`, `_lastRunK`,
`_maxima`); inside `RunTask` they are C# locals and are written back by `SaveRegs` whenever the task
stops.[^kvm-machine] The stack bases and sizes come from the task's `TaskEntry` in the image.[^task-entry]

| Register | Meaning |
| --- | --- |
| `pc` | **Index into `image.Instrs`**, not a byte address. Byte addresses appear only in return entries, traps and saved state. |
| `fp` | RAM address of the current frame (locals and arguments are at `fp + off`). |
| `sp` | RAM address of the first free byte above the current frame. |
| `depth` | Number of cells on the operand stack. |
| `rdepth` | Number of return entries on the return stack. |
| `floor` | Operand depth at the moment the current function was entered; a function may not pop below it. |

**Operand stack.** Cell *i* lives at `ram[task.VmStackBase + 4·i]`. A push at `depth == MaxOperandCells`
(or a 64-bit push at `depth + 2 > MaxOperandCells`) traps `OPERAND_OVERFLOW`; `Need(n)` traps
`OPERAND_UNDERFLOW` when `depth − n < floor` (`KvmMachine.cs:1741-1747, 1758-1782`).

**Return stack.** Entry *j* is the 8 bytes at `ram[task.VmStackBase + 4·MaxOperandCells + 8·j]`. An entry is
packed by `PackEntry` (`:438-441`):

| Bits | Field |
| --- | --- |
| 0–23 | return **byte** PC (24 bits) |
| 24–46 | caller's `fp` (23 bits) |
| 47–62 | caller's operand `floor` (16 bits) |
| 63 | `entered` — set by `ENTER` once the callee's prologue has run |

Entry 0 is written when a task starts (`PackEntry(0, fp, 0, false)`, `:724`); popping it with `RET`
completes the task. Both VM stacks live in the *VM region* `[image.VmRegionBase, MachineRam)`, which program
loads and stores cannot reach (see [memory map](/vm/memory-map-and-sysblock.md)).

**RAM stack / frames.** `ENTER ncells, frame` sets `fp = sp`, `sp += frame`, moves `ncells` argument cells from
the operand stack to `fp + 4·i`, and zeroes the rest of the frame; `RET` sets `sp = fp` and restores the
caller's `fp` (`:1025-1062, 1063-1106`). Frames are multiples of 8 bytes (enforced by the loader).

# Instruction encoding and the decoded form

An instruction is one opcode byte followed by its immediates in table order, all little-endian. Immediate
sizes come from `GameTables.ImmSize`: `i8`/`u8` 1 byte, `i16`/`u16`/`rel16` 2, `u24` 3, `u32` 4, `u64`
8.[^game-tables] The loader decodes the entire code section once into `Instr[]`:[^instr][^image-reader]

```csharp
public struct Instr {
    public byte Op;     // opcode value
    public uint Pc;     // byte offset of the instruction in the code section
    public long A;      // first immediate (sign-extended for i8, i16, rel16)
    public int  B;      // second immediate (only ENTER has one: the frame size)
    public int  Cost;   // cycles on this part, computed at load
    public short Soft;  // cost-category index if the instruction is a soft routine on this core, else -1
}
```

Load-time rewrites: for `JMP`/`JZ`/`JNZ` the loader computes `Pc + size + rel16` and stores the **target
instruction index** in `A`; for `CALL`/`CALLF` it replaces the byte address with the index of the target
`ENTER` (`ImageReader.cs:429-464`). `image.IndexOfPc[bytePc]` maps a byte PC to an instruction index (−1 if
not a boundary; `IndexOfPc[CodeSize]` is `Instrs.Length`).[^kvm-image] Functions are the ranges between consecutive
`ENTER`s (`image.Functions`), and the code must begin with an `ENTER` and every function must end in `RET`
or `JMP` (`ImageReader.cs:365-392`).

The full opcode list with per-opcode semantics is in [opcodes](/vm/opcodes.md).

# Construction

```csharp
public KvmMachine(PartDescription part, GameTables game, ushort computerIndex = 0)
public KvmMachine(PartDescription part, GameTables game, IReadOnlyList<FpVector> fpVectors, ushort computerIndex = 0)
```

The first overload passes `game.FpCheckData.Vectors`. The constructor (`KvmMachine.cs:1850-1864`):

1. throws `UnsupportedHostException("endianness", "kVM needs a little-endian host")` on a big-endian host;
2. runs `FpSelfTest.Run(fpVectors, out failingVector)` and throws
   `UnsupportedHostException(failingVector, message)` if any vector fails (see
   [soft routines and floating point](/vm/soft-routines-and-fp.md));
3. stores `part`, `game` and `computerIndex`. Nothing else is allocated until an image is loaded.

In the game the only constructor call is in `HostComputer`:
`Machine = new KvmMachine(part, tables, 0) { Profiling = true }` (`HostComputer.cs:222-225`), so **every
in-game machine has `ComputerIndex` 0 and profiling on**.[^host-computer] The CLI builds one the same way
(`KSA.Kfs.Cli/Cli.cs:419`).

# Loading an image — `LoadImage`

```csharp
public LoadResult LoadImage(byte[] image, IoLayout layout)
```

`LoadImage` (`KvmMachine.cs:1881-1923`) calls `ImageReader.Read(image, _part, _tables, layout, out img)`.
If validation fails it returns the failing `LoadResult` **and leaves the previously loaded image in place**.
On success it:

- stores the image and layout and sets `_booted = false` (the machine does nothing until `Reset`);
- allocates `_ram` (`image.MachineRam` bytes), `_cmd` and `_latched` (`layout.CommandSize`), `_sensors`
  (`layout.SensorSize`), and the per-task register arrays;
- records the offsets of the layout's one-shot command fields, `_budgetCycles = image.BudgetCycles`, the
  index of the `Init` and `Program` tasks (−1 if absent) and whether any `Periodic` task exists;
- builds the soft-routine name table and the bus tables (`LoadBus`);
- clears the first-NaN record and any trap, sets the phase to `Booting`, clears the profiler's cumulative
  statistics, and creates a fresh `ExecTrace` if tracing is wanted.

`ImageReader.Read` is where the image is checked against the part, the cost table hash, the I/O layout
fingerprint, the service table version and the service ids it calls; the rules are listed in
[the image format](/compiler/kvm-image-format.md). The checks that matter to the VM's behaviour are cited in
the relevant concept documents of this directory.

# Reset

```csharp
public void Reset(ResetCause cause)
```

`Reset` (`:1952-2007`) throws `InvalidOperationException("no image loaded")` without an image. Otherwise it
zeroes RAM and copies every `DataRecord` to its address, clears the command block, the latched copy and the
latched bus slots, stores `cause`, zeroes `soi_changes`, the overrun and rejected-request counters, the wake
reason, the sleep state, the trap, `init` cycles used, the tick index `k`, the command stream hash and the
first-NaN record, empties the request and rejection queues, and puts every task back to `Ready` with
`fp = sp = StackBase`. The phase becomes `Booting` if the image has an `init` task, else `Running`, or
`Idle` if there is nothing to run. Finally `_booted = true`.

The sensor block is **not** cleared by `Reset`; the host rewrites it every tick. (On a vehicle it is
zero-length in this build — only the bench layout has sensor fields; see
[memory map](/vm/memory-map-and-sysblock.md).)

# Running a tick — `RunTick`

```csharp
public TickResult RunTick()
```

The host calls, in order, `SetSystem(k, soiChanges)`, `WriteSensors(block)`, `WriteMessage(...)` for each
host-published message, then `RunTick()`, then `ReadCommands` (if the tick latched) and
`DrainRequests(sink)` (`HostComputer.cs:416-439`).[^host-computer] `RunTick` itself
(`KvmMachine.cs:2085-2210`):

1. **Early exits** (no state change): no image or not booted → an all-default result with
   `OverrunTask = −1`; phase `Halted` → the same but carrying `Trap`; phase `Idle` → the same with
   `NothingLeftToRun = true`.
2. **Tick start**: profiler `BeginTick`, pick the next exec-trace serial, `_cycles = 0`, `_bytecodes = 0`,
   `_lastK = _k`, clear the request and rejection queues and the issued/dropped counters.
3. **Boot tick** (phase `Booting`): run the `init` task — fresh if its status is `Ready`, otherwise
   resumed. If it completes, the phase becomes `Running` (or `Idle`). No other task runs in a boot tick.
4. **Normal tick**: for each task in image order, stopping at the first trap, run every `Periodic` task
   whose `k % RateDivisor == 0` **fresh**; an `Overrun` outcome records the task index and stops the loop.
   If nothing overran or trapped and a `Program` task exists that is not `Completed`, run it (fresh if
   `Ready`, else resumed). Then, without a trap, `NothingToRun()` moves the phase to `Idle`.
5. **Overrun accounting**: on an overrun `_overruns++` and `_cycles = _budgetCycles`.
6. **Latch**: only when the tick neither overran nor trapped, copy the command block to the latched copy,
   extend the command stream hash with `k` (8 bytes LE), the latched command block and every published bus
   slot, and latch the published slots.
7. **One-shot fields**: write a 32-bit zero at every one-shot command field offset in the live command
   block (every executed tick, latched or not).
8. **Unlatched tick**: `_requests.Clear()` — service requests issued in a tick that overran or trapped are
   discarded. Rejections and the dropped count are kept.
9. A trap sets the phase to `Halted`. The exec trace serial is published.
10. Return `TickResult`.

Details of step 3–4 — how a task is started, resumed, suspended and abandoned — are in
[tasks and scheduling](/vm/tasks-and-scheduling.md).

## The instruction loop (`RunTask`)

`private RunOutcome RunTask(int task, bool fresh)` (`:658-1822`) is the interpreter. Per instruction
(`:783-823`):

1. `pc >= Instrs.Length` → trap `BAD_JUMP` (execution fell off the end of the code).
2. `charge = instr.Cost`, plus the task's publication-copy cost when the instruction is the `RET` that
   completes the task (`rdepth == 1`) or a `YIELD` in the program task.
3. `init` task: if `initCyclesUsed + charge > image.InitBudgetCycles` → trap `INIT_OVERRUN`.
4. If `charge > budget − cyclesUsed`: save the registers; an `init` or `program` task **suspends**
   (resumes next tick), a periodic task **overruns**.
5. Program task only: except for the first instruction it executes this tick, if this task's cycles this
   tick plus `charge` would exceed `budget × ProgramShare / 100` (when `ProgramShare > 0`) the task
   suspends.
6. Add the cost to `_cycles`, count one bytecode, stamp the exec trace, attribute soft-routine cost to the
   profiler, and execute the opcode through one large `switch`.

A `TrapException` thrown anywhere in the loop is caught at `:1662-1668`: the registers are saved,
`_trap = new TrapInfo(code, bytePc, task, userCode)` is recorded and the outcome is `Trapped`.

## `TickResult` and `RunOutcome`

```csharp
public readonly record struct TickResult(
    uint CyclesUsed, long Bytecodes, int OverrunTask, TrapInfo? Trap, bool SleepRequested,
    bool BootTick, bool InitCompleted, bool NothingLeftToRun, bool Latched, bool Ran);
```

| Field | Meaning |
| --- | --- |
| `CyclesUsed` | Cycles charged this tick; equals the budget after an overrun. |
| `Bytecodes` | Instructions executed this tick. |
| `OverrunTask` | Index of the periodic task that overran, else −1. |
| `Trap` | The machine's trap, if any (also returned on later ticks while halted). |
| `SleepRequested` | **Always `false`.** `Not active in this build:` every `new TickResult(...)` passes `SleepRequested: false` (`:2089, 2093, 2097, 2209`). |
| `BootTick` | The tick started in phase `Booting`. |
| `InitCompleted` | The `init` task returned during this tick. |
| `NothingLeftToRun` | Phase is `Idle` at the end of the tick. |
| `Latched` | The command block was latched (no overrun, no trap). |
| `Ran` | At least one task was started or resumed. |

`RunOutcome` is `internal` and is what `RunTask` returns: `Completed` (0), `Suspended` (1), `Overrun` (2),
`Trapped` (3).[^run-outcome] It never leaves the VM assembly; the host sees only `TickResult`.[^tick-result]

# Command stream hash and CRC-32

`CommandStreamHash` is a running CRC-32 over, for every latched tick, the 8-byte tick index, the latched
command block and each published bus slot in message order (`:2186-2191, 344-353`). `RestartStreamHash()`
zeroes it, as does `Reset`. It exists for determinism checks (the CLI prints it; snapshots carry it).
`Crc32` is the reflected CRC-32 with polynomial `0xEDB88320`, table-driven, with `Compute(data)` and
`Append(crc, data)`;[^crc32] it is also used for the image checksum, the cost-table hash, the layout
fingerprint and the state checksum.

# Public API of `KvmMachine`

All members are instance members. Nothing here is thread-safe: the class takes no locks, and only
`ExecTrace` is published with `Volatile` semantics for a reader on another thread.

## Constants

| Member | Value |
| --- | --- |
| `public const string BUS_PSEUDO` | `"bus"` — the profiler pseudo-function that bus copy cycles are attributed to |
| `public const int MAX_REQUESTS_PER_TICK` | `256` |

## Properties

| Signature | Notes |
| --- | --- |
| `ushort ComputerIndex { get; }` | Constructor argument; written into published bus slot headers. |
| `CpuProfiler? Zones { get; set; }` | Optional `KSA.CpuProfiler`; when set, `RunTick` brackets task runs with `TickZones.VmInitTask` / `VmPeriodicTask` / `VmProgramTask`. |
| `PartDescription Part { get; }` | |
| `KvmImage? Image { get; }` | Null until a successful `LoadImage`. |
| `IoLayout? Layout { get; }` | Layout given to the last successful `LoadImage`. |
| `bool IsBooted { get; }` | True after `Reset` (or a state load with the booted flag). |
| `bool IsSleeping { get; }` | `Not active in this build:` never becomes true. |
| `bool InBoot { get; }` | Booted and phase `Booting`. |
| `bool IsIdle { get; }` | Booted and phase `Idle`. |
| `bool IsHalted { get; }` | Booted and phase `Halted`. |
| `uint OverrunCount { get; }` | Overruns since reset. |
| `TrapInfo? Trap { get; }` | |
| `uint CommandStreamHash { get; }` | |
| `bool Profiling { get; set; }` | Setting true creates the internal `Profiler` (kept if already present); false drops it. |
| `bool TracingExecution { get; set; }` | Creates or drops the `ExecTrace` (needs a loaded image). |
| `ExecTrace? ExecTrace { get; }` | `Volatile.Read` of the current trace. |
| `IReadOnlyList<TaskMaxima> Maxima { get; }` | Per-task high-water marks. |
| `ISyscallHost? SyscallHost { get; set; }` | Called synchronously for result-returning services. **Public setter.** |
| `int PendingRequests { get; }` | Requests queued and not yet drained. |
| `int PendingRejections { get; }` | Issue-time rejections queued and not yet drained. |

## Methods

| Signature | Behaviour |
| --- | --- |
| `LoadResult LoadImage(byte[] image, IoLayout layout)` | See above. |
| `void Reset(ResetCause cause)` | See above. |
| `void SetSystem(ulong k, uint soiChanges)` | Sets the tick index and the SOI-change count used for the system block. The host always passes `soiChanges = 0` (`HostComputer.cs:418`). |
| `void WriteSensors(ReadOnlySpan<byte> block)` | Copies the whole sensor block; throws `ArgumentException` unless the length equals `layout.SensorSize`. |
| `TickResult RunTick()` | See above. |
| `void ReadCommands(Span<byte> block)` | Copies the **latched** command block. |
| `void DrainRequests(ISyscallSink sink)` | Delivers queued requests, then rejections, then the dropped count; see [syscall ABI](/vm/services-syscall-abi.md). |
| `bool IsWiredService(ushort id)` | True if the id is in `GameTables.Services` with kind `request` or `request+result`. |
| `void CountRejectedRequest()` | `_rejected++` — the host's way to count a request it rejected after the latch. |
| `void Halt()` | If booted, sets the phase to `Halted` (no trap recorded). |
| `void Wake(uint reason)` | Stores the wake reason and clears the sleeping flag. `Not active in this build:` no caller exists. |
| `TaskState GetTaskState(int task)` | Snapshot of a task's saved registers and operand cells. |
| `Profile? GetProfile()` | The profiler's data, or null when profiling is off. |
| `NanRecord? GetFirstNan()` | First instruction that produced a NaN from non-NaN inputs since reset. |
| `void RestartStreamHash()` | Zeroes `CommandStreamHash`. |
| `void ReadRam(int address, Span<byte> destination)` | Raw copy out of the RAM array (no memory-map checks; can read the VM region). |
| `void WriteRam(int address, ReadOnlySpan<byte> source)` | Raw copy into the RAM array. |
| `void ReadCommandBlock(int offset, Span<byte> destination)` | Raw read of the **live** (unlatched) command block. |
| `void WriteCommandBlock(int offset, ReadOnlySpan<byte> source)` | Raw write into the live command block. |
| `void ReadSensorBlock(int offset, Span<byte> destination)` | Raw read of the sensor block. |
| `void ReadSystemBlock(int offset, Span<byte> destination)` | Raw read of the 256-byte system block as last refreshed. |
| `void WriteMessage(ushort id, ReadOnlySpan<byte> msg)` | Host publishes a message into a subscribed slot; see [bus slots](/vm/bus-slots-vm-side.md). |
| `void ReadMessage(ushort id, Span<byte> msg)` | Host reads the latched copy of a message the program publishes. |
| `(bool Subscribes, bool Publishes) MessageRole(ushort id)` | |
| `void ReadSlot(ushort id, Span<byte> msg)` | Raw read of a live slot. |
| `(uint SubCopies, uint PubCopies) BusCopyCycles(int task)` | Cycle cost of the task's subscription and publication copies. |
| `byte[] SaveState()` / `LoadResult LoadState(ReadOnlySpan<byte> state)` | See [state save format](/vm/machine-state-format.md). |

`internal string SoftRoutineName(byte op, int category)` and `internal string FunctionName(int fn)` exist for
the profiler. The raw `Read*`/`Write*` accessors index the backing arrays directly: an out-of-range span
throws a .NET `ArgumentOutOfRangeException`, not a VM trap.

## Small supporting types

| Type | Shape |
| --- | --- |
| `TaskState` | `sealed record (uint Pc, uint Fp, uint Sp, int OperandDepth, int ReturnDepth, string Status, uint[] Operands)` — `Pc` is a byte PC (or `CodeSize` past the end); `Status` is `TaskStatus.ToString()`. |
| `TaskMaxima` | `readonly record struct (uint RamStackBytes, int ReturnDepth, int OperandDepth)` |
| `TrapInfo` | `readonly record struct (TrapCode Code, uint Pc, int Task, byte UserCode)` |
| `NanRecord` | `sealed record (uint Pc, int Task)` |
| `UnsupportedHostException` | `sealed : Exception`, `string Vector { get; }` |
| `KvmImageStateExtensions` | `internal static int Ram(this KvmImage image)` → `(int)image.MachineRam` |
| `OpCount` | `public const int ASSIGNED = 146` — the number of assigned opcodes; no reference to it survives in the decompiled code (constants are inlined). |

# Modding notes

- **Visibility.** `KvmMachine` is `public sealed`; its state is entirely `private` instance fields (reachable
  only by reflection or Harmony `___field` injection). `HostComputer.Machine` is a public getter, so a mod
  holding a `VehicleComputer` can call every method above directly.
- **No statics, no caches across machines.** The only lazily built cache is the per-machine `ArgDomains`
  (`_domains`, `:1160-1163`), created the first time that machine executes a `SYSCALL` and never rebuilt.
- **Table-driven.** Opcode metadata, costs and services come from the `GameTables` instance passed to the
  constructor; in the game that is the process-wide `KfsIntegration.Tables` (see
  [game tables loader](/tables/game-tables-loader.md)). Mutating that instance affects every machine and
  every later image load, but costs are baked into `Instr.Cost` at load, so an already loaded image keeps
  its old numbers until reloaded.
- **`SyscallHost` has a public setter**, which makes wrapping the host's result provider possible without
  Harmony; see [syscall ABI](/vm/services-syscall-abi.md).
- **Do not transpile `RunTask` casually.** It is one method with ~20 local functions sharing a closure;
  ILSpy renders it as a `switch`, but the JIT'd shape depends on a compiler-generated display struct. Adding
  a new *opcode* would require patching it, the `Op` enum users, `opcodes.json`, `costs.json`, the image
  loader's opcode table and both compilers; adding a new *service* requires none of that.
- **Threading.** All calls must come from the thread that ticks the machine; the host arranges that (see
  [dispatch and threading](/integration/dispatch-and-threading.md)). `ReadRam`/`WriteRam` from another
  thread race with `RunTick`.
- **Construction cost.** Each `new KvmMachine` runs the 31-vector floating-point self-test.

[^kvm-machine]: KvmMachine.cs:14 (class), 29-167 (fields), 169-265 (properties), 438-461 (return entry packing), 658-1822 (`RunTask`), 1845-1864 (constructors), 1881-1923 (`LoadImage`), 1952-2007 (`Reset`), 2022-2083 (host accessors), 2085-2210 (`RunTick`), 2212-2258 (`Wake`, `GetTaskState`, `DrainRequests`, `IsWiredService`).
[^image-reader]: ImageReader.cs:289-364 (decode), 365-392 (function ranges), 423-478 (jump/call/ENTER/RET rules), 553-580 (per-instruction cost), 765-780 (part, cost-table and layout checks).
[^instr]: Instr.cs:3-16.
[^task-entry]: TaskEntry.cs:3-6.
[^kvm-image]: KvmImage.cs:7-114.
[^game-tables]: GameTables.cs:133-153 (`ImmSize`), 430-447 (`BuildParts`: per-part limits from the catalogue defaults).
[^tick-result]: TickResult.cs:3.
[^run-outcome]: RunOutcome.cs:3-9.
[^crc32]: Crc32.cs:5-40.
[^host-computer]: HostComputer.cs:222-225 (construction), 409-440 (tick sequence).
[^catalogue-json]: KSA.Kfs.Vm.Tables.Data.catalogue.json:6-15 (`defaults`).
