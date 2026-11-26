---
type: File Format Reference
title: kVM machine state snapshot format (KVS1) — SaveState / LoadState
description: The binary layout KvmMachine.SaveState produces and every validation LoadState applies, for the save/restore API that exists in this build but has no caller.
resource: ksa-decomp:KSA.Kfs.Vm/KvmMachine.cs
tags: [kfs, vm, kvm, state, save, persistence, file-format]
sources:
  - id: kvm-machine
    resource: ksa-decomp:KSA.Kfs.Vm/KvmMachine.cs
    title: KvmMachine (decompiled) — SaveState, LoadState
  - id: crc32
    resource: ksa-decomp:KSA.Kfs.Vm/Crc32.cs
    title: Crc32 (decompiled)
  - id: load-result
    resource: ksa-decomp:KSA.Kfs.Vm.Image/LoadResult.cs
    title: LoadResult (decompiled)
generated: { by: claude-code/claude-fable-5-2, at: 2026-11-25T21:46:43Z }
status: stable
ksa_build: "2026.11.25.5949"
---

# Summary

`KvmMachine` can serialise its complete run state to a byte array and restore it:

```csharp
public byte[] SaveState()
public LoadResult LoadState(ReadOnlySpan<byte> state)
```

`Not active in this build:` neither method is called anywhere in the decompiled assemblies (the only
occurrences of `SaveState`, `LoadState` and the magic `KVS1` are in `KvmMachine.cs`). kFS computers
therefore do not survive a game save/load through this path; what the game actually persists is covered in
[lifecycle and persistence](/integration/lifecycle-and-persistence.md). The format is documented because it
is the authoritative list of what constitutes machine state, and because a mod could call it.[^kvm-machine]

A state is only meaningful for the **same image** (CRC-32 checksum) on the **same I/O layout**
(fingerprint); `LoadState` requires the image to be loaded first.

# Layout

All integers little-endian. `Pad8(n)` rounds `n` up to a multiple of 8.

```text
+0                     header            32 bytes
+32                    scheduler         64 bytes
+96                    task records      Pad8(taskCount × 32)
                       command block     Pad8(commandSize)      (live)
                       latched commands  Pad8(commandSize)
                       latched slots     Pad8(Σ published slot sizes), in ascending message order
                       RAM               Pad8(machineRam)
                       CRC-32            4 bytes, over everything before it
```

The checksum is `Crc32.Compute` (reflected CRC-32, polynomial `0xEDB88320`).[^crc32]

`state_size` (header +24) is the number of bytes between the header and the CRC:
`64 + Pad8(tasks × 32) + 2 × Pad8(cmd) + Pad8(published slots) + Pad8(ram)`; the file is
`32 + state_size + 4` bytes (`KvmMachine.cs:2260-2268, 2276-2278`).

## Header (32 bytes)

| Offset | Size | Field |
| --- | --- | --- |
| 0 | 4 | magic `"KVS1"` |
| 4 | 2 | `state_version` = 1 |
| 6 | 2 | flags: bit 0 = booted; other bits 0 |
| 8 | 4 | image checksum |
| 12 | 4 | I/O layout fingerprint |
| 16 | 4 | RAM size in bytes |
| 20 | 2 | task count |
| 22 | 2 | reserved, 0 |
| 24 | 4 | `state_size` |
| 28 | 4 | reserved, 0 |

## Scheduler block (64 bytes)

| Offset | Size | Field |
| --- | --- | --- |
| 0 | 8 | tick index `k` of the last executed tick (`_lastK`) |
| 8 | 4 | reset cause |
| 12 | 4 | SOI-change count |
| 16 | 1 | phase (0 booting, 1 running, 2 idle, 3 halted) |
| 17 | 1 | trap code (0 = none) |
| 18 | 1 | sleeping flag |
| 19 | 1 | reserved, 0 |
| 20 | 4 | trap byte PC |
| 24 | 1 | trap task |
| 25 | 3 | reserved, 0 |
| 28 | 4 | `init` cycles used |
| 32 | 8 | sleep-until tick |
| 40 | 4 | wake reason |
| 44 | 4 | rejected-request count |
| 48 | 4 | overrun count |
| 52 | 1 | trap user code |
| 53 | 11 | reserved, 0 |

## Task record (32 bytes each)

| Offset | Size | Field |
| --- | --- | --- |
| 0 | 4 | byte PC: the saved instruction's PC for a non-`Ready` task; the task's entry for a `Ready` task; `code_size` for a task parked past the end |
| 4 | 4 | `fp` |
| 8 | 4 | `sp` |
| 12 | 2 | operand depth |
| 14 | 1 | return depth |
| 15 | 1 | status (0 ready, 1 suspended, 2 completed) |
| 16 | 8 | tick index of the task's last run |
| 24 | 2 | operand floor |
| 26 | 6 | reserved, 0 |

The operand and return stacks themselves are inside the RAM image (the VM region).

# What is and is not saved

Saved: everything above — registers, scheduler counters, the live and latched command blocks, latched
published slots, all of RAM.

**Not** saved, and so not restored by `LoadState`: the sensor block, the system block (rebuilt at the next
task start), the command stream hash, the first-NaN record, per-task maxima, the profiler and exec trace,
and queued service requests. `LoadState` sets `_k` and `_lastK` to the saved tick index and forces
`_sleeping = false`.

# `LoadState` validation

Each failure returns `LoadResult.Fail(rule, message)`; on success `LoadResult.Success`
(`KvmMachine.cs:2345-2578`).[^load-result]

| Rule | Condition for failure |
| --- | --- |
| `image` | No image loaded; or the state's image checksum differs ("the state belongs to another image"). |
| `size` | Shorter than 36 bytes; or RAM size, task count, `state_size` or the total length disagree with the loaded image and layout. |
| `magic` | Not `KVS1`. |
| `version` | `state_version` ≠ 1. |
| `reserved` | Any flag bit other than bit 0, or a reserved header field, non-zero; reserved scheduler bytes non-zero; reserved task-record bytes non-zero. |
| `checksum` | CRC-32 mismatch. |
| `layout` | Layout fingerprint differs. |
| `scheduler` | Phase > 3, trap code > 17, reset cause > 3, wake reason above the largest `events.json` id, or sleeping flag > 1; halted phase and trap code disagree (a halted state must carry a trap and vice versa); trap PC beyond the code or trap task out of range; a user code without a `USER` trap; `init` cycles above the init budget; sleeping without a target; **sleeping at all** ("sleep is not implemented until MS7"); booting phase (with the booted flag) without an `init` task. |
| `task` | Status > 2, or a periodic task not `Ready`; PC not on an instruction boundary; a `Ready` task whose PC is outside its entry function; `fp`/`sp` outside the task's RAM stack, out of order or not 8-aligned; depths or floor over the task's limits; `Suspended` with no return entry; a return entry (in the saved RAM) whose PC is not an instruction boundary, whose `fp` is outside the task's RAM stack, or whose floor exceeds the operand stack. |
| `slots` | A latched slot header the VM could not have written (non-zero reserved word, a zero sequence with a non-zero publisher or timestamp, or a negative timestamp). |

One consequence of the `scheduler` rules: a machine halted with `Halt()` (halted phase, no trap) produces a
state that `LoadState` rejects ("halted phase and trap code disagree").

Only after all checks pass does `LoadState` copy the blocks and assign the registers; a rejected state
leaves the machine untouched.

# Modding notes

- `SaveState`/`LoadState` are public and self-validating, so a mod can snapshot and roll back a machine
  (for instance around an experiment) — but in-flight service requests and host-side state (burn handles,
  console, bus tracker) are not part of it.
- The format embeds `TrapCode`, `TaskStatus`, phase and reset-cause numbers; see [VM enums](/vm/vm-enums.md).
- `StateMagic`, `STATE_HEADER` (32), `SCHEDULER_SIZE` (64) and `TASK_RECORD_SIZE` (32) are private members
  of `KvmMachine`.

[^kvm-machine]: KvmMachine.cs:163-167 and 263 (sizes and magic), 2260-2268 (`Pad8`, `StateSize`), 2270-2343 (`SaveState`), 2345-2578 (`LoadState`).
[^crc32]: Crc32.cs:24-39.
[^load-result]: LoadResult.cs:3-20.
