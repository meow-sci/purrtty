---
type: API Reference
title: kVM profiler, execution trace and diagnostics records
description: The cycle profiler (Profiler, Profile, FunctionStats), the per-instruction execution trace (ExecTrace), the first-NaN record, the TickZones wall-clock tags and the OpCount constant.
resource: ksa-decomp:KSA.Kfs.Vm/Profiler.cs
tags: [kfs, vm, kvm, profiler, exec-trace, diagnostics, api]
sources:
  - id: profiler
    resource: ksa-decomp:KSA.Kfs.Vm/Profiler.cs
    title: Profiler (decompiled, internal)
  - id: profile
    resource: ksa-decomp:KSA.Kfs.Vm/Profile.cs
    title: Profile and FunctionStats (decompiled)
  - id: exec-trace
    resource: ksa-decomp:KSA.Kfs.Vm/ExecTrace.cs
    title: ExecTrace (decompiled)
  - id: tick-zones
    resource: ksa-decomp:KSA.Kfs.Vm/TickZones.cs
    title: TickZones (decompiled)
  - id: small-records
    resource: ksa-decomp:KSA.Kfs.Vm/NanRecord.cs
    title: NanRecord and OpCount (decompiled)
  - id: kvm-machine
    resource: ksa-decomp:KSA.Kfs.Vm/KvmMachine.cs
    title: KvmMachine (decompiled) — profiler and trace call sites
  - id: host-computer
    resource: ksa-decomp:KSA.Kfs.Host/HostComputer.cs
    title: HostComputer (decompiled) — consumers
generated: { by: claude-code/claude-fable-5-2, at: 2026-11-25T21:46:43Z }
status: stable
ksa_build: "2026.11.25.5949"
---

# Summary

The VM carries two optional observers and a few small diagnostic records:

- a **cycle profiler** that attributes every charged cycle to a function, a soft routine, the bus or a
  service, per tick and cumulatively;
- an **execution trace** that stamps each instruction with the serial number of the last tick in which it
  executed (for editor "what ran" highlighting);
- the **first-NaN record**, the **`TickZones`** tags for the game's wall-clock CPU profiler, and the
  **`OpCount`** constant.

None of them changes program behaviour or cycle accounting.

# Cycle profiler

## Turning it on

`KvmMachine.Profiling { get; set; }` creates (`new Profiler(FunctionName)`) or drops the profiler;
`GetProfile()` returns its live `Profile` object or null (`KvmMachine.cs:225-235, 1871-1874`).[^kvm-machine]
`HostComputer` constructs its machine with `Profiling = true`, so **profiling is always on in the game**
(`HostComputer.cs:222-225`).[^host-computer] `Profiler` itself is `internal sealed`; only its data is
public.

## Data model

```csharp
public sealed class Profile {
    public long ProgramStarvedTicks;
    public Dictionary<string, FunctionStats> Tick { get; }        // StringComparer.Ordinal
    public Dictionary<string, FunctionStats> Cumulative { get; }  // StringComparer.Ordinal
    public Dictionary<(string Caller, string Callee), long> TickEdges { get; }
}
public sealed class FunctionStats {
    public long Self;     // cycles charged while this was the innermost frame
    public long Total;    // cycles between entry and return, callees included
    public long Calls;
    public bool Pseudo;   // true for soft routines, "bus" and services
}
```

[^profile]

| Member | Scope |
| --- | --- |
| `Tick` | Reset by `BeginTick` at the start of every executed tick. |
| `TickEdges` | Caller → callee cycles for the current tick. |
| `Cumulative` | Since the last `Reset` or `LoadImage` (both clear it, `KvmMachine.cs:1917, 2002`). |
| `ProgramStarvedTicks` | Ticks in which the program task was scheduled but executed no instruction; zeroed by `Reset` (`:2163-2166, 2003-2006`). |

Keys are **names**, so entries of different kinds share one namespace:

| Kind | Name | `Pseudo` |
| --- | --- | --- |
| Program function | The debug-info name of the function starting at that `ENTER`, or `f_{start:x6}` when the image has no debug entry for it (`BuildFunctionName`, `:1941-1950`) | false |
| Soft routine | The routine name from `routines.json` (for example `__addsf3`), or `soft` | true |
| Bus copies | `"bus"` (`KvmMachine.BUS_PSEUDO`) | true |
| Service call | The service's `name` from `services.json` (for example `log`, `set_throttle`) | true |

A program function that happens to be called `bus`, `soft` or like a service or routine would share an
entry with the pseudo-function of that name.

## How cycles are attributed (`Profiler.cs`)

The profiler keeps a frame stack of `(function index, start cycles)`, the current function and a cycle
*mark* up to which self time has been booked.[^profiler] The interpreter calls it at these points:

| Call | From | Effect |
| --- | --- | --- |
| `BeginTick()` | `RunTick` (`:2099`) | Recycle and clear `Tick` and `TickEdges`; mark = 0. |
| `TaskStart(entryFn, cycles)` | fresh task start (`:700`) | Frame stack = `[entryFn]`; count one call of the entry function. |
| `EnterTask(frames, cycles)` | task resume without subscriptions (`:777`) and the early-exit paths | Rebuild the frame stack from `CallChain` (the saved return stack). No call is counted. |
| `EnterTaskWithBus(frames, before, cost, "bus")` | task resume with subscription copies (`:772`) | As above, booking `cost` to `bus` as a callee of the outermost frame. |
| `Enter(callee, cycles, enterCost, taskEntry)` | `ENTER` (`:1060`) | Unless it is the task's entry prologue: book self time to the caller up to just before the `ENTER`, push the callee (so the `ENTER`'s own cost counts as the callee's), count a call. |
| `Return(cycles)` | `RET` (`:1089`) | Book self time, pop the frame, add its total and the caller → callee edge. |
| `Soft(name, cycles, cost)` | soft instruction (`:821`), bus copies (`:722, 1087, 1117`), `SYSCALL` (`:1141, 1192`) | Book preceding self time to the current function; add `cost` to the pseudo-function's `Self` and `Total`, `Calls++`, and to the edge current → name. |
| `LeaveTask(cycles)` | every exit from `RunTask` (`:1736-1740, 1751`) | Book self time; add totals for every frame still on the stack (a suspended task's open frames get a partial total for this tick). |

Consequences worth knowing when reading the numbers:

- A `SYSCALL`'s whole cycle cost (base + class) is charged to the service's pseudo-function, not to the
  calling function's `Self`.
- A function still open when the task suspends gets `Total` credited each tick it is on the stack, but
  `Calls` only on the tick it was entered.
- `Total` for a task's entry function in `Tick` is what `HostComputer` uses as the task's cycles for the
  tick (`HostComputer.cs:473-491`).

`GetProfile()` hands out the live dictionaries; they are mutated during `RunTick` with no locking.

# Execution trace

```csharp
public sealed class ExecTrace {
    public readonly KvmImage Image;
    public readonly uint[] Stamps;      // one per decoded instruction
    public uint Serial { get; }         // Volatile.Read of the last published serial
    internal uint NextSerial { get; }   // _serial + 1
    public ExecTrace(KvmImage image);
    internal void Publish(uint serial); // Volatile.Write
}
```

[^exec-trace]

- `KvmMachine.TracingExecution { get; set; }`: on a change, creates a new `ExecTrace(image)` (only if an
  image is loaded) or drops it, with `Volatile.Write`; `LoadImage` creates a fresh one when tracing is
  wanted (`:237-253, 1918-1921`). `KvmMachine.ExecTrace` reads it with `Volatile.Read`.
- At the start of an executed tick the machine takes `serial = trace.NextSerial`; for every instruction it
  executes it writes `Stamps[instructionIndex] = serial`; at the end of the tick it calls
  `trace.Publish(serial)` (`:2100-2104, 815-818, 2208`).
- A reader on another thread therefore sees: `Stamps[i] == Serial` ⇔ instruction *i* executed in the most
  recently completed tick; a smaller non-zero stamp is the serial of the last tick it ran in; 0 means never
  since the trace was created. Stamp writes are unsynchronised (benign races by design).
- Ticks that return early (not booted, halted, idle) do not advance the serial.

The host enables tracing on demand: `HostComputer.WantExecTrace()` keeps it wanted for one second, and
every tick sets `machine.TracingExecution = ExecTraceWanted` (`HostComputer.cs:198, 283-286, 417`), so the
array exists only while a UI is asking for it. See [editor window](/tooling/editor-window.md) and
[debug window](/tooling/debug-window.md).

# First-NaN record

```csharp
public sealed record NanRecord(uint Pc, int Task);
```

Set once per reset by the first arithmetic instruction that produces a NaN from inputs that were not NaN
(`CF`/`CD`, `KvmMachine.cs:596-621`); `Pc` is the instruction's byte PC. Cleared by `Reset` and `LoadImage`;
read with `GetFirstNan()`. The host turns it into a console line `first NaN at 0x… in {function}…` and a
snapshot field (`HostComputer.cs:558-570`).[^small-records]

# `TickZones`

`TickZones` is a static class of `public static readonly ProfilerTag` fields — tags for the game's own
wall-clock `KSA.CpuProfiler`, not for cycle accounting.[^tick-zones] `KvmMachine.Zones` (a
`CpuProfiler?`) is bracketed with `zones?.Begin(tag)` / `zones?.End()`.

| Field | Tag text | Used at |
| --- | --- | --- |
| `Dispatch` | `Kfs.Dispatch` | `KSA.Kfs/KfsDispatchProfiler.cs:12` |
| `BeforeDispatch` | `Kfs.BeforeDispatch` | `KSA.Kfs/KfsVehicleHost.cs:506` |
| `AfterDispatch` | `Kfs.AfterDispatch` | `KSA.Kfs/KfsVehicleHost.cs:529, 536, 565` |
| `FlightComputer` | `Kfs.FlightComputer` | `KSA.Kfs/KfsVehicleHost.cs:539` |
| `WriteBack` | `Kfs.WriteBack` | `KSA.Kfs/KfsVehicleHost.cs:577` |
| `DrainQueue` | `Kfs.DrainQueue` | `KSA.Kfs.Host/VehicleComputer.cs:454` |
| `Sample` | `Kfs.Sample` | `KSA.Kfs.Host/VehicleComputer.cs:498` |
| `Tick` | `Kfs.Tick` | `KSA.Kfs.Host/VehicleComputer.cs:502` |
| `PackMessages` | `Kfs.Tick.PackMessages` | `KSA.Kfs.Host/VehicleComputer.cs:558` |
| `WriteInputs` | `Kfs.Tick.WriteInputs` | `KSA.Kfs.Host/HostComputer.cs:416` |
| `Requests` | `Kfs.Tick.Requests` | `KSA.Kfs.Host/HostComputer.cs:431` |
| `BusMonitor` | `Kfs.Tick.BusMonitor` | `KSA.Kfs.Host/HostComputer.cs:441` |
| `Views` | `Kfs.Tick.Views` | `KSA.Kfs.Host/HostComputer.cs:444` |
| `Publish` | `Kfs.Tick.Publish` | `KSA.Kfs.Host/HostComputer.cs:505` |
| `VmRunTick` | `Kfs.Vm.RunTick` | `KSA.Kfs.Host/HostComputer.cs:427` |
| `VmInitTask` | `Kfs.Vm.InitTask` | `KSA.Kfs.Vm/KvmMachine.cs:2120` |
| `VmPeriodicTask` | `Kfs.Vm.PeriodicTask` | `KSA.Kfs.Vm/KvmMachine.cs:2143` |
| `VmProgramTask` | `Kfs.Vm.ProgramTask` | `KSA.Kfs.Vm/KvmMachine.cs:2160` |

The zone names double as a map of the per-tick pipeline: sample → tick (pack messages, write inputs, VM run,
requests, bus monitor, views, publish) → write-back. The owning concepts are
[snapshots and perf](/host/snapshots-and-perf.md) and
[dispatch and threading](/integration/dispatch-and-threading.md).

# `OpCount`

`public static class OpCount { public const int ASSIGNED = 146; }` — the number of assigned opcodes, equal
to the number of entries in `opcodes.json` and in the `Op` enum.[^small-records] No decompiled code
references it (a `const` leaves no reference after compilation).

# Modding notes

- The cycle profile is the cheapest way for a mod to see **which services a program called in the last
  tick and how often** without patching anything: `machine.GetProfile().Tick["<service name>"].Calls`
  (attempted calls — rejected and dropped ones are counted too). Read
  it on the VM's tick thread (for example from a postfix on `HostComputer.RunTick`), not from the UI thread.
- A mod-defined service shows up automatically under its `ServiceInfo.Name`; choose a name that cannot
  collide with a program function or a routine name.
- `Profiler` is `internal`; to add attribution of your own, call nothing — use `ServiceInfo.Name`.
- `TickZones` fields are `static readonly`; adding zones of your own means creating your own
  `ProfilerTag`s, not extending this class.

[^profiler]: Profiler.cs:6-200.
[^profile]: Profile.cs:6-15; FunctionStats.cs:3-12.
[^exec-trace]: ExecTrace.cs:6-28.
[^tick-zones]: TickZones.cs:3-40.
[^small-records]: NanRecord.cs:3; OpCount.cs:3-6.
[^kvm-machine]: KvmMachine.cs:139-147 (observer fields), 225-253 (`Profiling`, `TracingExecution`, `ExecTrace`), 596-621 (first NaN), 679-681 and 815-822 (per-instruction hooks), 1824-1843 (`CallChain`), 1871-1879 (`GetProfile`, `GetFirstNan`), 1935-1950 (function names), 2099-2104 and 2208 (tick begin/publish).
[^host-computer]: HostComputer.cs:198 and 283-286 (exec trace wanted window), 222-225 (profiling on), 417 (per-tick toggle), 473-491 (task cycles from the profile), 558-570 (first NaN report).
