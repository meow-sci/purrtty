---
type: Subsystem Reference
title: kVM tasks and scheduling
description: The three task kinds (init, periodic, program), task status and scheduler phases, how a tick schedules, starts, suspends, completes and abandons tasks, and the inactive sleep/wake machinery.
resource: ksa-decomp:KSA.Kfs.Vm/KvmMachine.cs
tags: [kfs, vm, kvm, tasks, scheduler, tick, sleep, wake]
sources:
  - id: kvm-machine
    resource: ksa-decomp:KSA.Kfs.Vm/KvmMachine.cs
    title: KvmMachine (decompiled) — RunTick, RunTask
  - id: task-entry
    resource: ksa-decomp:KSA.Kfs.Vm.Image/TaskEntry.cs
    title: TaskEntry (decompiled)
  - id: image-reader
    resource: ksa-decomp:KSA.Kfs.Vm.Image/ImageReader.cs
    title: ImageReader (decompiled) — task rules
  - id: task-enums
    resource: ksa-decomp:KSA.Kfs.Vm/TaskKind.cs
    title: TaskKind, TaskStatus, Phase, SchedulerPhase (decompiled)
  - id: task-records
    resource: ksa-decomp:KSA.Kfs.Vm/TaskState.cs
    title: TaskState and TaskMaxima records (decompiled)
  - id: events-json
    resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.events.json
    title: events.json (embedded) — wake reasons
  - id: host-computer
    resource: ksa-decomp:KSA.Kfs.Host/HostComputer.cs
    title: HostComputer (decompiled) — sleep requests are only logged
  - id: catalogue-json
    resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.catalogue.json
    title: catalogue.json (embedded)
generated: { by: claude-code/claude-fable-5-2, at: 2026-11-25T21:46:43Z }
status: stable
ksa_build: "2026.11.25.5949"
---

# Summary

A kVM image declares a fixed list of tasks. The scheduler is cooperative-by-budget and entirely
deterministic: each tick it runs the due **periodic** tasks to completion in image order, then gives the
single **program** task whatever cycles are left; before any of that, the single **init** task runs alone
over as many "boot" ticks as it needs. There is no preemption between tasks, no priorities beyond table
order, and — in this build — no sleeping.[^kvm-machine]

# Task records

Each task is a `TaskEntry` read from the image's Tasks section (40 bytes per entry):[^task-entry]

```csharp
public sealed record TaskEntry(
    uint NameIndex, uint Entry, TaskKind Kind, ushort RateDivisor, ushort MaxOperandCells,
    uint StackBase, uint StackSize, uint VmStackBase, byte MaxCallDepth, byte ProgramShare,
    uint SubFirst, ushort SubCount, ushort PubCount, uint PubFirst)
{
    public uint VmStackSize => (uint)(4 * MaxOperandCells + 8 * MaxCallDepth);
}
```

| Field | Meaning |
| --- | --- |
| `NameIndex` | String-table index of the task's name (must be a `name` string). |
| `Entry` | Byte PC of the entry function's `ENTER` (24-bit in the file). |
| `Kind` | `Init` (0), `Periodic` (1), `Program` (2). |
| `RateDivisor` | Periodic tasks run on ticks where `k % RateDivisor == 0`. Must be ≥ 1 for every task. |
| `MaxOperandCells` | Size of this task's operand stack in cells. |
| `StackBase`, `StackSize` | The task's RAM (frame) stack. |
| `VmStackBase` | Start of the task's operand stack in the VM region; the return stack follows it. |
| `MaxCallDepth` | Return-stack entries, including the implicit entry call (must be ≥ 1). |
| `ProgramShare` | 0–100; only on the program task: percentage of the tick budget it may use per tick (0 = no cap). |
| `SubFirst`/`SubCount`, `PubFirst`/`PubCount` | Ranges in the image's Links section: the bus messages this task subscribes to / publishes. |

## Rules the loader enforces (`ImageReader.cs:393-422, 490-533`)

A violation fails `LoadImage` with the named `LoadResult.Rule`.[^image-reader]

| Rule name | Check |
| --- | --- |
| `tasks` | Kind byte ≤ 2; at most `part.MaxTasks` tasks; at most one `init` and one `program` task; `RateDivisor ≥ 1`; `ProgramShare ≤ 100` and 0 on non-program tasks; `MaxOperandCells ≤ part.MaxOperandCells`; `MaxCallDepth ≤ part.MaxReturnEntries` and ≥ 1; link ranges inside Links. |
| `entry` / `call-target` / `enter` | The entry is an instruction boundary, is an `ENTER`, and that `ENTER` has `ncells == 0`. |
| `names` | `NameIndex` refers to a `name` string. |
| `task-order` | Periodic tasks appear in ascending `RateDivisor` order. |
| `cost` | The task's start step (`CALL` cost + subscription copies) and completion step (`RET`, or `YIELD` if larger for a program task, + publication copies) each fit in one tick budget (`ImageReader.cs:755-764`). |

The part limits come from `catalogue.json` `defaults`: `max_tasks` 16, `max_operand_cells` 64,
`max_return_entries` 16; for a `K8` core 8, 16 and 8.[^catalogue-json] See
[processor catalogue](/tables/catalogue-processors.md).

# Status and phase

| Enum | Values | Visibility |
| --- | --- | --- |
| `TaskStatus : byte` | `Ready` 0, `Suspended` 1, `Completed` 2 | public |
| `Phase : byte` | `Booting` 0, `Running` 1, `Idle` 2, `Halted` 3 | **internal** — the machine's actual scheduler phase |
| `SchedulerPhase : byte` | `Booting` 0, `Running` 1, `Idle` 2, `Halted` 3 | public mirror of `Phase`; no code in the decompiled assemblies references it |
| `RunOutcome` | `Completed` 0, `Suspended` 1, `Overrun` 2, `Trapped` 3 | internal — result of one `RunTask` |

Status meanings:[^task-enums]

- `Ready` — the next run starts the task from its entry (a *fresh* start).
- `Suspended` — the task stopped mid-function and resumes from its saved registers.
- `Completed` — the entry function of an `init` or `program` task returned; it never runs again until the
  next `Reset`. A periodic task goes back to `Ready` when it returns.

A periodic task is never observed `Suspended` or `Completed` between ticks: `LoadState` rejects a saved
state where one is (`KvmMachine.cs:2455-2458`).

Phase transitions:

| From | To | When |
| --- | --- | --- |
| (reset) | `Booting` | The image has an `init` task (`:1996`). |
| (reset) | `Running` / `Idle` | No `init` task; `Idle` if `NothingToRun()`. |
| `Booting` | `Running` / `Idle` | The `init` task completes (`:2124-2128`). |
| `Running` | `Idle` | After a tick without a trap, `NothingToRun()` is true: no periodic task exists and the program task is absent or `Completed` (`:2009-2020, 2169-2173`). |
| any | `Halted` | A trap in this tick (`:2203-2207`), or `Halt()` (`:2077-2083`). |

`Idle` and `Halted` are terminal until `Reset`: `RunTick` returns immediately in both.

# Tick structure

`RunTick` is described step by step in [kVM machine](/vm/kvm-machine.md#running-a-tick--runtick). The
scheduling part is:

**Boot tick** (`Booting`). Only the `init` task runs: `RunTask(_initTask, fresh: status == Ready)`. It may
span many ticks; its total cycles across all boot ticks are capped by `image.InitBudgetCycles` (trap
`INIT_OVERRUN`). Commands are still latched at the end of a boot tick that did not trap.

**Normal tick** (`Running`), `KvmMachine.cs:2132-2168`:

```text
for i in 0 .. Tasks.Length-1:
    if a trap is pending: break
    if Tasks[i].Kind == Periodic and k % Tasks[i].RateDivisor == 0:
        outcome = RunTask(i, fresh: true)          # periodic tasks always start fresh
        if outcome == Overrun: overrunTask = i; break
if no overrun and no trap and a program task exists and it is not Completed:
    RunTask(programTask, fresh: status == Ready)
    if it executed no instruction and profiling is on: ProgramStarvedTicks++
```

Consequences:

- Periodic tasks run in **image order**, which the loader forces to be ascending rate divisor — the fastest
  tasks first.
- A periodic task must finish within the tick it starts in. If the budget runs out first it is
  **abandoned** (`Overrun`): the tick's commands are not latched, its service requests are discarded,
  the remaining periodic tasks and the program task do not run, and `OverrunCount` increments. The task
  simply starts fresh the next time it is due.
- The program task is the only one that spans ticks. It gets the cycles left after the periodic tasks, and
  cannot cause an overrun — running out of budget just suspends it.

# Starting, resuming and stopping a task (`RunTask`)

## Fresh start (`KvmMachine.cs:688-734`)

1. `fp = sp = StackBase`, operand depth 0, floor 0, `pc` = index of the entry `ENTER`.
2. The **start step** costs the `CALL` category's cycles (the implicit call that starts a task) plus the
   task's subscription-copy cycles.
3. `init` only: if `initCyclesUsed + cost > InitBudgetCycles` → trap `INIT_OVERRUN` at the entry PC.
4. If `cost` exceeds the cycles left in the tick: a periodic task **overruns** immediately; an `init` or
   `program` task stays `Ready` and reports `Suspended` (it will try again next tick).
5. Otherwise the cost is charged, the subscribed bus slots are copied into the task's buffers, return entry
   0 is written and the return depth becomes 1.
6. A non-periodic task's status is set to `Suspended` so that any later stop resumes rather than restarts.

## Resume (`:735-779`)

The saved registers are restored. If the task has subscriptions, their copy cost is charged **again on
every resume** and the buffers are refreshed (with the same `INIT_OVERRUN` / out-of-budget checks, the
latter leaving the task suspended).

## Ways a task stops

| Outcome | Trigger | Effect |
| --- | --- | --- |
| `Completed` | `RET` pops return entry 0 (`:1090-1097`) | Publications are copied to their slots first. Status → `Ready` (periodic) or `Completed` (init, program). |
| `Suspended` — yield | `YIELD` in the program task (`:1107-1121`) | Publications copied; resumes at the next instruction on the next tick. |
| `Suspended` — budget | next instruction's cost exceeds the remaining tick budget, in an `init` or `program` task (`:798-802`) | Resumes at that instruction next tick. |
| `Suspended` — share | program task would exceed `budget × ProgramShare / 100` this tick (`:803-806, 1659-1660`) | Same. The first instruction of each run is exempt, so the program always makes progress. |
| `Overrun` | same budget condition in a periodic task (`:801, 1748-1757`) | `_cycles = budget`, status `Ready`, depths reset; the tick is abandoned. |
| `Trapped` | any `TrapException` (`:1662-1668`) | `TrapInfo` recorded; the machine halts at the end of the tick. |

`_lastRunK[task]` records the tick index of each task's most recent run (`:780`); it is only used by the
state save format.

# Task state inspection

```csharp
public sealed record TaskState(uint Pc, uint Fp, uint Sp, int OperandDepth, int ReturnDepth,
                               string Status, uint[] Operands);
public readonly record struct TaskMaxima(uint RamStackBytes, int ReturnDepth, int OperandDepth);
```

`GetTaskState(task)` returns the saved registers (with `Pc` converted to a byte PC, or `CodeSize` if the
task is past the end), the status name and a copy of the operand cells (`:2218-2227`). `Maxima[task]` holds
high-water marks since `Reset`: bytes of RAM stack used (`sp − StackBase` after each `ENTER`), deepest
return stack and deepest operand stack; they are written back whenever the task's registers are saved or
it overruns (`:1056-1059, 1755, 1795`).[^task-records] The host shows them in its task views
(`HostComputer.cs:876-878`).

# Sleeping and waking

`Not active in this build:` the scheduler has sleep fields but nothing drives them.

- `_sleeping` is only ever assigned `false` (`Reset`, `Wake`, `LoadState`); `IsSleeping` is therefore
  always false, and `RunTick` never consults it.
- `_sleepUntilK` is only zeroed or copied from a saved state.
- `Wake(uint reason)` (`:2212-2216`) stores `reason` for `sys.wake_reason` and clears the flag, but has no
  caller anywhere in the decompiled assemblies.
- `TickResult.SleepRequested` is always `false`.
- `LoadState` refuses a state with the sleeping flag: `"sleep is not implemented until MS7"` (`:2432-2435`).
- The services `sleep_until` (16) and `sleep_until_event` (17) pass through the VM like any other request;
  `HostComputer.ApplyHostRequest` records them in `SleepRequest` and logs
  `"… requested (sleeping and waking are MS7; the computer keeps running)"`
  (`HostComputer.cs:787-807`).[^host-computer]

The intended wake reasons are the event ids of `events.json`, which the VM does use to validate the
arguments of `sleep_until_event` (see [syscall ABI](/vm/services-syscall-abi.md)) and the `wake_reason` of a
loaded state:[^events-json]

| Id | Name | Parameter |
| --- | --- | --- |
| 1 | `APOAPSIS` | lead time, s (`f32`) |
| 2 | `PERIAPSIS` | lead time, s (`f32`) |
| 3 | `SOI_CHANGE` | — |
| 4 | `ATMOSPHERE_ENTRY` | — |
| 5 | `BURN_START` | burn handle (`u32`) |
| 6 | `ALTITUDE_BELOW` | altitude, m (`f32`) |
| 7 | `TIME` | `wake_reason` only; not a valid `sleep_until_event` argument |
| 8 | `MESSAGE` | message id (`u32`) |

The full table is documented in [events table](/tables/events-table.md).

# Modding notes

- A mod cannot add a task to a running image; the task list, stacks and bus links are fixed by the image
  and validated at load.
- Work a mod does on behalf of a program is bounded by the VM's tick: requests issued in a tick are
  delivered only after the whole tick (all tasks) has run and only if it latched. A program that wants a
  "wait for X" has no blocking primitive — it must poll (a message or a result-returning service; vehicle
  layouts have no sensor fields in this build) and `YIELD`.
- `ProgramShare`, `RateDivisor` and the tick rate are image properties; the host does not override them.
- `Halt()` is the supported way for a host or mod to stop a machine without a trap; it is what the stock
  `halt` service uses.

[^kvm-machine]: KvmMachine.cs:658-822 (task start/resume and the per-instruction budget checks), 1063-1121 (`RET`, `YIELD`), 1731-1821 (`Finish`, `Leave`, `Overrun`, `Save`, `Suspend`, `SuspendBeforeStart`), 1984-2007 (reset of task state and phase), 2009-2020 (`NothingToRun`), 2085-2210 (`RunTick`), 2212-2227 (`Wake`, `GetTaskState`).
[^task-entry]: TaskEntry.cs:3-6; ImageReader.cs:393-407 (field offsets in the 40-byte record).
[^image-reader]: ImageReader.cs:393-422 (entry rules), 490-533 (task rules), 755-764 (start/completion step cost).
[^task-enums]: TaskKind.cs:3-8; TaskStatus.cs:3-8; Phase.cs:3-9; SchedulerPhase.cs:3-9; RunOutcome.cs:3-9.
[^task-records]: TaskState.cs:3; TaskMaxima.cs:3.
[^events-json]: KSA.Kfs.Vm.Tables.Data.events.json:4-13.
[^host-computer]: HostComputer.cs:787-807 (services 16 and 17), 876-878 (task view).
[^catalogue-json]: KSA.Kfs.Vm.Tables.Data.catalogue.json:6-15.
