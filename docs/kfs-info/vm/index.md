# /vm/ — the kVM machine

The bytecode virtual machine at the centre of kFS: `KSA.Kfs.Vm.KvmMachine` and the types and embedded
tables it executes against. Everything here was read from the decompiled `KSA.Kfs.Vm` namespace of KSA
build 2026.11.25.5949. Conventions: [bundle conventions](/overview/bundle-conventions.md).

## Start here

- [kVM machine — execution model and API](/vm/kvm-machine.md) — How the kVM bytecode machine is constructed, loads an image, executes one tick, and reports the result, with the complete public surface of KvmMachine.
- [Services — the SYSCALL ABI](/vm/services-syscall-abi.md) — Exactly what the VM does when a program executes SYSCALL — table lookup, argument marshalling, domain validation, synchronous results, queuing, per-tick limits and delivery to the host — and what that means for adding new services.

## Instruction set and memory

- [Opcode table with implementation semantics](/vm/opcodes.md) — Every one of the 146 kVM opcodes — value, mnemonic, immediates, stack effect, cost category and per-core cycles — merged with what KvmMachine actually does for each.
- [Memory map, sensor/command windows and the system block](/vm/memory-map-and-sysblock.md) — The kVM address space — RAM regions, the read-only sensor window, the command window, the 256-byte system block (sysblock.json in full) — and the bounds, alignment and write checks applied to every access.
- [Message-bus slots — the VM side](/vm/bus-slots-vm-side.md) — How KvmMachine stores, copies, stamps, latches and exposes message-bus slots — the mechanics underneath the host's message bus.

## Scheduling, time and failure

- [Tasks and scheduling](/vm/tasks-and-scheduling.md) — The three task kinds (init, periodic, program), task status and scheduler phases, how a tick schedules, starts, suspends, completes and abandons tasks, and the inactive sleep/wake machinery.
- [Cost model and cycle budgets](/vm/cost-model-and-budgets.md) — The complete cost table per core class, how an instruction's cycle cost is computed and charged, the per-tick and init budgets, overruns, and how service calls and soft routines are priced.
- [Traps and faults](/vm/traps-and-faults.md) — Every kVM trap code with its table text, the exact places KvmMachine raises it, what a trap does to the machine, and how the host is told.

## Arithmetic and determinism

- [Soft routines, floating-point self-test and arithmetic edge cases](/vm/soft-routines-and-fp.md) — What "soft" instructions are and how routines.json names and sizes them, the FpSelfTest that guards every machine construction with fpcheck.json, the Conv conversion helpers, and the edgecases.json conformance vectors, all reproduced in full.

## Observation and persistence

- [Profiler, execution trace and diagnostics records](/vm/profiler-and-exec-trace.md) — The cycle profiler (Profiler, Profile, FunctionStats), the per-instruction execution trace (ExecTrace), the first-NaN record, the TickZones wall-clock tags and the OpCount constant.
- [Machine state snapshot format (KVS1)](/vm/machine-state-format.md) — The binary layout KvmMachine.SaveState produces and every validation LoadState applies, for the save/restore API that exists in this build but has no caller.

## Reference

- [VM enums with numeric values](/vm/vm-enums.md) — Every enum declared in the KSA.Kfs.Vm namespace with its underlying type, members and numeric values, where each is used, and how it lines up with enums.json.

## Embedded tables whose content is reproduced in this directory

| Table | Reproduced in |
| --- | --- |
| `opcodes.json` | [opcodes](/vm/opcodes.md) |
| `costs.json` | [cost model and budgets](/vm/cost-model-and-budgets.md) |
| `traps.json` | [traps and faults](/vm/traps-and-faults.md) |
| `sysblock.json` | [memory map and system block](/vm/memory-map-and-sysblock.md) |
| `routines.json`, `fpcheck.json`, `edgecases.json` | [soft routines and floating point](/vm/soft-routines-and-fp.md) |

Loader behaviour and row schemas for all tables are under [/tables/](/tables/index.md).
