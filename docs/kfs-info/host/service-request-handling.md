---
type: Subsystem Reference
title: Service request handling (host side)
description: The complete host-side path of a kFS service call — from the VM's SYSCALL to ServiceRequest, the hard-coded host and vehicle switches, the queue the game drains, synchronous results, rejection, burn handles, the service override, the console log and traces.
resource: ksa-decomp:KSA.Kfs.Host/VehicleComputer.cs
tags: [kfs, host, services, syscall, service-request, burn-handles, console-log, trace, extension]
sources:
  - id: host-computer
    resource: ksa-decomp:KSA.Kfs.Host/HostComputer.cs
    title: HostComputer (decompiled)
  - id: vehicle-computer
    resource: ksa-decomp:KSA.Kfs.Host/VehicleComputer.cs
    title: VehicleComputer (decompiled)
  - id: kvm-machine
    resource: ksa-decomp:KSA.Kfs.Vm/KvmMachine.cs
    title: KvmMachine (decompiled)
  - id: vm-service-types
    resource: ksa-decomp:KSA.Kfs.Vm/ServiceRequest.cs
    title: ServiceRequest, ServiceRejection, ISyscallSink, ISyscallHost, ServiceIds, BurnEndCause (decompiled, one file each)
  - id: arg-domains
    resource: ksa-decomp:KSA.Kfs.Vm/ArgDomains.cs
    title: ArgDomains (decompiled)
  - id: service-info
    resource: ksa-decomp:KSA.Kfs.Vm.Tables/ServiceInfo.cs
    title: ServiceInfo (decompiled)
  - id: services-json
    resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.services.json
    title: services.json (embedded resource)
  - id: game-tables
    resource: ksa-decomp:KSA.Kfs.Vm.Tables/GameTables.cs
    title: GameTables (decompiled)
  - id: image-reader
    resource: ksa-decomp:KSA.Kfs.Vm.Image/ImageReader.cs
    title: ImageReader (decompiled)
  - id: kfs-services
    resource: ksa-decomp:KSA.Kfs/KfsServices.cs
    title: KfsServices (decompiled)
  - id: burn-handles
    resource: ksa-decomp:KSA.Kfs.Host.Services/BurnHandles.cs
    title: BurnHandles, BurnHandle, BurnStatusState (decompiled)
  - id: service-override
    resource: ksa-decomp:KSA.Kfs.Host.Services/ServiceOverride.cs
    title: ServiceOverride (decompiled)
  - id: console-log
    resource: ksa-decomp:KSA.Kfs.Host.Services/ConsoleLog.cs
    title: ConsoleLog and LogLine (decompiled)
  - id: log-formatter
    resource: ksa-decomp:KSA.Kfs.Host.Services/LogFormatter.cs
    title: LogFormatter (decompiled)
  - id: log-format
    resource: ksa-decomp:KSA.Kfs.Vm.Image/LogFormat.cs
    title: LogFormat (decompiled)
  - id: trace-store
    resource: ksa-decomp:KSA.Kfs.Host.Services/TraceStore.cs
    title: TraceStore, TraceSample, TraceBufferPool (decompiled)
  - id: kfs-vehicle-host
    resource: ksa-decomp:KSA.Kfs/KfsVehicleHost.cs
    title: KfsVehicleHost (decompiled)
  - id: kfs-host-sampler
    resource: ksa-decomp:KSA.Kfs/KfsHostSampler.cs
    title: KfsHostSampler (decompiled)
  - id: offline-harness
    resource: /modding/offline-harness.md
    title: Offline harness experiments E1–E12
generated: { by: claude-code/claude-fable-5-2, at: 2026-11-25T21:30:51Z }
status: stable
ksa_build: "2026.11.25.5949"
---

# Overview

A service is a numbered call from a program to its host. There are five stations on its way, and
**every one of them is keyed on the numeric service id**:

```text
 program          SYSCALL id                                                       (during the tick)
    │
    ▼
 1  KvmMachine     table lookup · arg cells · 256/tick cap · argument domains
    │              result-bearing?  ──▶  ISyscallHost.IssueResult(id, args, results)   synchronous
    │              _requests.Add(new ServiceRequest(id, args, bytes, task, results))
    ▼
    tick ends      not latched (overrun or trap)?  ──▶  _requests.Clear()   — the requests are gone
    │
    ▼
 2  HostComputer.RunTick ─▶ machine.DrainRequests(this)                           (after the tick)
    │      ISyscallSink.Request(r):
    │        ApplyHostRequest(r)        private switch: 1, 2, 3, 16, 17
    │        ApplyRequest(r)            protected virtual; VehicleComputer: 32, 48–57, 64–67
    │        else RejectByHost(r, "no vehicle to act on")
    ▼
 3  VehicleComputer._cooked            requests kept for the game
    │
    ▼
 4  KfsServices.AfterDispatch ─▶ foreach (r in C.TakeServiceRequests()) Apply(r, …)   (after Dispatch)
    │      switch (r.Id) — no default case
    ▼
 5  KfsServices.MainThread            queued main-thread work (stage, plan, engine shutdown)
```

Stations 1–3 are the host layer and are documented here; 4–5 belong to the game glue
([kFS services, game side](/integration/kfs-services-game-side.md)). The table itself is in
[services table](/tables/services-table.md) and the VM's calling convention in
[services syscall ABI](/vm/services-syscall-abi.md).

# The types that cross the VM boundary

```csharp
public sealed record ServiceRequest(ushort Id, uint[] Args, byte[]? Bytes, int Task = -1, uint[]? Results = null);
public sealed record ServiceRejection(ushort Id, int Entry, int Argument, uint[] Args, int Task, string Reason);

public interface ISyscallSink
{
    void Request(ServiceRequest request);
    void Rejected(ServiceRejection rejection) { }     // default: nothing
    void Dropped(int count) { }                       // default: nothing
}

public interface ISyscallHost
{
    void IssueResult(ushort service, ReadOnlySpan<uint> args, Span<uint> results);
}
```

[^vm-service-types]

- `Args` are the raw 32-bit operand-stack cells, deepest first, exactly `ServiceInfo.ArgCells` of
  them. An `f32` is one cell (bit pattern), an `i64` two cells (low, then high).
- `Bytes` is non-null only for service 1 (`log`): the VM copies the format's argument buffer out of
  program memory at issue time.
- `Task` is the index of the issuing task.
- `Results` is the array the VM pushed back on the operand stack; non-null only when
  `ServiceInfo.ResultCells > 0`.

The table row both the VM and the host consult is
`ServiceInfo(ushort Id, string Name, string Kind, string[] ArgTypes, int ArgCells, int ResultCells,
string CostClass, bool PerValue, string? RequiresStock, string[]? Domains, int[]? ArgumentOf,
string[]? ArgumentNames, string Summary)`, held in the public dictionary
`GameTables.Services` (`Dictionary<ushort, ServiceInfo>`).[^service-info][^services-json] The host
layer never reads `RequiresStock`; its pilot-mode-only list is hard-coded (below) and happens to
match the rows whose `requires_stock` is set.

# Station 1: what the VM does before the host sees anything

`Op.SYSCALL` in `KvmMachine.RunTask`:[^kvm-machine]

0. **Before any tick:** the image validator has already refused to load an image containing a
   `SYSCALL` whose id is not a key of `GameTables.Services` (rule `syscall`,
   `SYSCALL at 0x…: unknown service id 0x…`), or whose `service_table_version` differs from the
   game's (rule `service-table`).[^image-reader]
1. `IsWiredService(id)` — the id must be a key of `GameTables.Services` **and** its `Kind` must be
   `"request"` or `"request+result"`. Otherwise the task traps `BAD_SYSCALL`. Reserved rows (id 4,
   `event`) are never added to `GameTables.Services`, and a row of kind `"query"` — which the table
   loader accepts — is not wired.[^game-tables]
2. The operand stack must hold `ArgCells` cells.
3. **Per-tick cap.** `MAX_REQUESTS_PER_TICK = 256`. Beyond it the arguments are popped, the
   rejected-request counter is incremented, zeros are pushed for any result cells and nothing else
   happens; the host later hears `Dropped(count)`.
4. Arguments are copied to `Args`. For id 1 only, the format index in `Args[0]` must be in the
   image's format table (else `BAD_SYSCALL`) and the argument buffer is copied to `Bytes`.
5. **Argument domains.** `ArgDomains.Check(serviceInfo, args, context)` evaluates the row's
   `arg_domains`. On a violation the VM increments the rejected counter, records a
   `ServiceRejection`, pushes zeros for any result cells and does **not** create a request.
   Domains this build understands: `finite`, `finite:>=0`, `finite:[lo,hi]`, `quat`, `vec_nonzero`,
   `event`, `event_param`, `future`, `handle`, `channel`, `enum:<name>` (optionally with a range),
   and the unchecked `any`, `ptr`, `format`, `count`. A row with no domains is not checked.[^arg-domains]
6. **Results.** If `ResultCells > 0`: `results = new uint[ResultCells]`,
   `SyscallHost?.IssueResult(id, args, results)`, then each cell is pushed. With no `SyscallHost`
   (a `BenchMachine`) the results are zeros.
7. `_requests.Add(new ServiceRequest(id, args, bytes, task, results))`.

The rejected counter is visible to the program in the system block (offset 64). At the end of the
tick, **if the tick did not latch, `_requests.Clear()`** — a tick that overran or trapped delivers
no requests at all, although `IssueResult` has already run for them.

`DrainRequests(ISyscallSink sink)` then calls `sink.Request` for each request in issue order,
`sink.Rejected` for each rejection, and `sink.Dropped(n)` once if the cap was hit.

# Station 2: `HostComputer` as `ISyscallSink`

```csharp
void ISyscallSink.Request(ServiceRequest request)
{
    if (!ApplyHostRequest(request) && !ApplyRequest(request))
    {
        RejectByHost(request, "no vehicle to act on");
    }
}
```

[^host-computer]

## `ApplyHostRequest` — services every computer handles itself

`private bool ApplyHostRequest(ServiceRequest request)` is a `switch (request.Id)`:[^host-computer]

| id | service | host action |
| --- | --- | --- |
| 1 | `log` | if `Console.LogAllowed(k, hz)`: format `img.Strings[Args[0]].Text` against `Bytes` with `LogFormatter.Format` (enum names resolved from the image's enum tables), `LogFormatter.Cut` to 255 characters, `Console.Log` |
| 2 | `trace` | `Traces.Add(img.Strings[Args[0]].Text, LastK, BitConverter.UInt32BitsToSingle(Args[1]))`; if that returns `false`: `RejectByHost(request, "more than 64 trace channels")` |
| 3 | `halt` | if the machine is not already halted: `Machine.Halt()`, `HaltByHost("halt() requested by the program")` |
| 16 | `sleep_until` | `SleepRequest = (0, t_us)`; event `sleep until met_us N requested (sleeping and waking are MS7; the computer keeps running)` |
| 17 | `sleep_until_event` | `SleepRequest = (event, param)`; event `sleep until event NAME requested (sleeping and waking are MS7; the computer keeps running)` |

All five return `true`. `Not active in this build:` sleeping — the request is recorded in the
public `SleepRequest` property and logged, nothing reads the property, `TickResult.SleepRequested`
is always `false`, and `KvmMachine.Wake` has no caller.

## `ApplyRequest` — the subclass hook

```csharp
protected virtual bool ApplyRequest(ServiceRequest request) => false;      // HostComputer
```

`BenchMachine` does not override it, so on the bench every service other than 1, 2, 3, 16, 17 is
rejected with `no vehicle to act on`.

`VehicleComputer` overrides it with a second `switch (request.Id)`:[^vehicle-computer]

| ids | services | action |
| --- | --- | --- |
| 54 | `translate` | always `RejectByHost(request, "translate is not serviced (no automatic translation; integration §8.4, D-117 (2))")` |
| 64, 65 | `plan_circularize`, `plan_burn` | if the handle in `Results[0]` exists: when `Handles.LivePlans >= 8`, end it `Aborted`/`PlanningFailed` and reject with `8 planned burns are live already; one must finish, be aborted or be deleted first`; otherwise mark it `Delivered`. Then `_cooked.Add(request)` |
| 32 | `stage` | `_cooked.Add(request)`; `Interlocked.Increment(ref _stagesPending)` |
| 48, 49, 50, 51, 52, 53, 55, 56, 57, 66, 67 | `hold_direction`, `hold_rates`, `release_attitude`, `set_attitude_profile`, `set_throttle`, `engines_on`, `hold_heading`, `hold_quat`, `engines_off`, `burn_start`, `burn_abort` | if `Mode == ControlMode.FullControl`: reject with `<name> steers the C# flight computer, which flies only in pilot mode; this program claims ALLOCATION (kFS D-135)`; otherwise `_cooked.Add(request)` |
| anything else | | `return false` |

## Rejections

```csharp
protected void RejectByHost(ServiceRequest request, string reason)
{
    Machine.CountRejectedRequest();
    if (Console.EventAllowed(LastK, TickHz))
    {
        ServiceInfo? svc = Tables.Services.GetValueOrDefault(request.Id);
        Console.Event(LastK, TickHz, "rejected " + (svc?.Name ?? $"0x{request.Id:X4}") + ": " + reason);
    }
}
```

`Machine.CountRejectedRequest()` increments the VM counter the program can read. Three kinds of
rejection reach the console, all through the rate-limited event channel:[^host-computer]

| origin | text |
| --- | --- |
| VM argument domain (`ISyscallSink.Rejected`) | `rejected <name> argument <n> = <value>: <reason>` |
| VM per-tick cap (`ISyscallSink.Dropped`) | `dropped N service request(s): a tick may issue 256` |
| host or game (`RejectByHost`, `VehicleComputer.RejectService`) | `rejected <name>: <reason>` |

`ISyscallSink.Rejected` indexes `Tables.Services[r.Id]` directly; that is safe because the VM only
produces rejections for wired services.

A rejection is never reported back to the program as an error value. The program sees only the
rejected-request counter, a zero result for result-bearing services, and for burns the handle's
state in `MSG_BURN_STATUS`.

## What happens to an id the host has no case for

Take a service id `X` that is not one of the ids above.

1. If `X` is **not in `GameTables.Services`**: the image does not load (rule `syscall`). If its row
   exists but is not of kind `request` / `request+result`: the program traps `BAD_SYSCALL` at the
   `SYSCALL` instruction. The host never sees a request in either case. (And a compiler or
   assembler that takes its service names from the same table would not have emitted the call —
   see [services table](/tables/services-table.md).)
2. If `X` **is** a wired row in the table: the VM checks its `arg_domains`, calls
   `IssueResult(X, …)` when it has result cells — `VehicleComputer.IssueResult` ignores every id but
   64 and 65, so the results stay `0` — and queues the request.
3. After the tick, `ApplyHostRequest` returns `false` (default case), `VehicleComputer.ApplyRequest`
   returns `false` (default case), and the request is **rejected**:
   `Machine.CountRejectedRequest()` plus the event line `rejected <name>: no vehicle to act on`.
4. It is not added to `_cooked`, so the game's `KfsServices.Apply` never sees it. (If it were, the
   game's switch has no `default` and would silently ignore it.)

So an unknown-but-tabled service is harmless and useless: counted, logged, dropped.

# Station 3: the queue for the game

```csharp
public List<ServiceRequest> TakeServiceRequests()
{
    if (_cooked.Count == 0) return _noRequests;          // one shared, empty static list
    List<ServiceRequest> result = new List<ServiceRequest>(_cooked);
    _cooked.Clear();
    return result;
}
public void RejectService(ServiceRequest request, string reason) => RejectByHost(request, reason);
public void Event(string text) => Console.Host(LastK, TickHz, text);
```

[^vehicle-computer]

- `_cooked` accumulates across all ticks of one dispatch (and across dispatches until taken). The
  game takes it once per dispatch in `KfsServices.AfterDispatch`, after `Computer.Dispatch`
  returned, on the same thread.[^kfs-services][^kfs-vehicle-host]
- `RejectService` is how the game rejects after the fact (a `burn_start` on an unknown or ended
  handle, an auto burn refused for lack of a control module).
- `Event` writes an unthrottled host line; the game uses it for hand-back, layout-change,
  supersession and "pilot took … back" notes.
- Main-thread results come back through `Enqueue`: the game's planner queues
  `C.Enqueue(_ => C.Handles.Bind(h, burn))` or a `PlanFailed` action that calls
  `C.Machine.CountRejectedRequest()`.

What the game does with each queued id (for orientation; details in the integration docs):

| id | game action (`KfsServices.Apply`) |
| --- | --- |
| 48, 55, 56, 49, 50, 51 | write attitude settings on the stock `FlightComputer` |
| 52, 53, 57 | `Override.SetThrottle` / `Override.SetEngine`; cancel an auto burn where applicable |
| 32 | queue `StageWork` for the main thread, which activates the next sequence and calls `StageApplied()` |
| 64, 65 | supersede armed handles, set `Handles.Current`, queue `PlanWork` for the main thread |
| 66 | `BurnStart` — arm, start or reject by handle state |
| 67 | abort the loaded auto burn and all armed handles |

# Synchronous results: `ISyscallHost`

```csharp
void ISyscallHost.IssueResult(ushort service, ReadOnlySpan<uint> args, Span<uint> results)
{
    bool flag = (uint)(service - 64) <= 1u;          // service is 64 or 65
    if (flag && results.Length != 0)
    {
        BurnHandle burnHandle = Handles.Issue(service);
        _issuedThisTick.Add(burnHandle);
        results[0] = burnHandle.Id;
    }
}
```

[^vehicle-computer]

This is the only result-bearing path in the build. It runs **inside the VM's instruction loop**, on
the dispatch thread, at the moment the program executes the `SYSCALL`, before argument-independent
outcomes (overrun, latch) are known. That ordering is why a handle has a `Delivered` flag:

- `IssueResult` creates the handle and returns its id immediately;
- if the tick latches, `ApplyRequest` (64/65) later marks the handle `Delivered`;
- `AfterPlayerTick` then ends every handle issued this tick that was not delivered as
  `Aborted`/`PlanningFailed`, counts a rejected request and logs
  `rejected <name>: its tick overran, so the request was discarded (handle N)` plus the handle's end line.

`KvmMachine.SyscallHost` is a public settable property (`ISyscallHost? SyscallHost { get; set; }`);
`VehicleComputer`'s constructor assigns `this`.[^kvm-machine]

# Every hard-coded service-id test

| # | file:line | construct | ids |
| --- | --- | --- | --- |
| 0 | `ImageReader.cs:557` | `tables.Services.ContainsKey(id)` at image load — table driven | any id in the table |
| 1 | `KvmMachine.cs:1125` → `:2248–2258` | `IsWiredService(id)` — table driven | any id with kind `request` / `request+result` |
| 2 | `KvmMachine.cs:1151` | `if (num27 == 1)` — copy the log buffer | 1 |
| 3 | `HostComputer.cs:750–810` | `switch (request.Id)` in `ApplyHostRequest` | 1, 2, 3, 16, 17 |
| 4 | `VehicleComputer.cs:176` | `(uint)(service - 64) <= 1u` in `IssueResult` | 64, 65 |
| 5 | `VehicleComputer.cs:187–238` | `switch (request.Id)` in `ApplyRequest` | 54; 64, 65; 32; 48–53, 55–57, 66, 67 |
| 6 | `KfsServices.cs:313–448` | `switch (r.Id)` in `Apply` (game) | 48, 55, 56, 49, 50, 51, 52, 53, 57, 32, 64, 65, 66, 67 |
| 7 | `KfsServices.cs:415`, `:636`, `:679` | `r.Id == 64`, `p.Service == 64` (game) | 64 vs 65 |

Table lookups by id that are not switches: `HostComputer.cs:682` (`Tables.Services[r.Id]`),
`HostComputer.cs:742` (`GetValueOrDefault`), `VehicleComputer.cs:231` and `:251`
(`Tables.Services[id].Name`), `KfsServices.cs:305` and `:594`. `ServiceIds` (in `KSA.Kfs.Vm`)
declares named constants for all twenty ids, but the switches above use literals.

**There is no registry, event, delegate or interface through which code outside these methods can
claim a service id.** The extension options are collected in
[host extension surface](/host/host-extension-surface.md).

# Burn handles

```csharp
public enum BurnStatusState : uint { None, Planned, Aligning, Burning, Done, Aborted, Pending }   // 0..6
public enum BurnEndCause : byte { None, Completed, PropellantOut, StageRanDry, IgnitionDenied,
                                  PlanningFailed, Superseded, Cancelled }                          // 0..7

public sealed class BurnHandle
{
    public uint Id { get; }
    public ushort? Service { get; internal set; }     // 64 or 65; null for a handle bound to a pilot's burn
    public bool Delivered { get; internal set; }
    public bool Pending => Delivered && Burn == null && !Ended.HasValue;
    public object? Burn { get; internal set; }        // the game's Burn object, opaque here
    public bool Armed { get; set; }
    public bool Started { get; set; }
    public BurnStatusState? Ended { get; internal set; }
    public BurnEndCause Cause { get; internal set; }
    public long IgnitionNs { get; set; }
    public double DvRemaining { get; set; }
    public (double X, double Y, double Z) DvVlf { get; set; }
    public BurnHandle(uint id);
}

public sealed class BurnHandles
{
    public const int MAX_LIVE_PLANS = 8;
    public const int MAX_ENDED_UNBOUND = 64;
    public int Count { get; }
    public int LivePlans { get; }                     // Service != null && Delivered && !Ended
    public uint Current { get; set; }
    public Dictionary<uint, BurnHandle>.ValueCollection All { get; }
    public BurnHandle Issue(ushort? service);         // ids start at 1 and never repeat
    public BurnHandle? Get(uint id);
    public BurnHandle? ByBurn(object burn);
    public void Bind(BurnHandle h, object burn);
    public bool End(BurnHandle h, BurnStatusState state, BurnEndCause cause);   // false if already ended
    public void Rearm(BurnHandle h);
    public static string EndText(BurnHandle h);       // "burn N DONE|ABORTED <cause>"
    public static string CauseName(BurnEndCause c);
}
```

[^burn-handles]

Lifecycle of a planned burn:

| step | where | thread | effect |
| --- | --- | --- | --- |
| issue | `VehicleComputer.IssueResult` | dispatch, during the tick | `Handles.Issue(service)`; the id is the program's return value |
| deliver | `VehicleComputer.ApplyRequest` | dispatch, after the tick | `Delivered = true` (or abort when 8 plans are live); request queued |
| discard | `VehicleComputer.AfterPlayerTick` | dispatch | not delivered → `End(Aborted, PlanningFailed)` |
| plan | `KfsServices.Apply` → `MainThread` | main | builds the game `Burn` |
| bind / fail | `Enqueue`d action | dispatch (next `DrainQueue`) | `Bind(h, burn)` or `End(Aborted, PlanningFailed)` |
| arm / start | `KfsServices.BurnStart`, `Start` | dispatch | `Armed`, then `Started` with the flight computer in auto burn mode |
| end | `KfsServices.EndHandle` | dispatch | `End(state, cause)` and the host line `burn N DONE completed` etc. |

Between "deliver" and "bind" the handle is `Pending` (state 6 in `MSG_BURN_STATUS`). `End` clears
`Armed` and `Started`; a handle that ends without a bound burn enters a queue of at most 64 and the
oldest such handles are forgotten. `Rearm` clears the end state (used for a burn that stopped
because a stage ran dry). `Handles.Current` selects the handle `MSG_BURN_STATUS` reports.
`Load` and `TryStart` do **not** clear the table: handles are "the vehicle's", and the `BurnHandles`
instance lives as long as the `VehicleComputer`.

`CauseName` strings: `none`, `completed`, `propellant_out`, `stage_ran_dry`, `ignition_denied`,
`planning_failed`, `superseded`, `cancelled`.

# `ServiceOverride`

The state behind `set_throttle` (52), `engines_on` (53) and `engines_off` (57):[^service-override]

```csharp
public sealed class ServiceOverride
{
    public float Throttle      { get; private set; }
    public bool  ThrottleValid { get; private set; }
    public bool  EngineOn      { get; private set; }
    public bool  EngineValid   { get; private set; }
    public void SetThrottle(float x);     // Throttle = x, ThrottleValid = true
    public void SetEngine(bool on);       // EngineOn = on, EngineValid = true
    public void Clear();
    public (bool Throttle, bool Engines) PilotFrame(float pilotThrottle, bool pilotEngineOn, bool burnManual);
}
```

- The game applies a valid override to the pilot's inputs right before the stock
  `FlightComputer.ComputeControl` in pilot mode, and publishes the four values in
  `MSG_FC_SETTINGS.service_*`.[^kfs-vehicle-host][^kfs-host-sampler]
- `PilotFrame` is called once per dispatch (`KfsServices.BeforeDispatch`) with the pilot's current
  throttle and engine switch. It remembers them; when the previous call was also in manual burn
  mode and the pilot's value **changed**, the corresponding override is invalidated and the
  returned flag is `true` (the game then logs `pilot took throttle back from the service` /
  `pilot took engines back from the service`). `SetThrottle`, `SetEngine` and `Clear` forget the
  remembered pilot values, so the first frame after a service call never counts as a pilot change.
- `Clear()` is called by `VehicleComputer.LeaveControl` and `Fault`.

# The console: `ConsoleLog`, `LogLine`, `LogFormatter`

```csharp
public readonly record struct LogLine(ulong K, double TimeS, string Text);

public sealed class ConsoleLog
{
    public const int LINES_PER_SECOND  = 10;
    public const int CAPACITY          = 4096;
    public const int EVENTS_PER_SECOND = 20;
    public long SuppressedTotal { get; }
    public long EventsSuppressedTotal { get; }
    public long Version { get; }
    public bool LogAllowed(ulong k, int tickHz);
    public void Log(ulong k, int tickHz, string text);
    public bool EventAllowed(ulong k, int tickHz);
    public void Event(ulong k, int tickHz, string text);
    public void Host(ulong k, int tickHz, string text);
    public void Advance(ulong k, int tickHz);
    public List<LogLine> Snapshot();
    public long CopyTail(int count, List<LogLine> into);
    public void Clear();
}
```

[^console-log]

Three write channels share one list:

| channel | used for | limit |
| --- | --- | --- |
| `Log` | program output (`log` service) | 10 lines per second of **machine time** (`k / tickHz`) |
| `Event` | rejections, drops, skipped ticks, sleep notices | 20 per machine second; unlimited while `tickHz <= 0` |
| `Host` | load/start/stop/trap/overrun/NaN lines, game notes | none |

When the machine second rolls over, a summary line is appended for anything suppressed:
`(N log lines suppressed: more than 10 per second)` and
`(N host events suppressed: more than 20 per second)`. The list holds at most 4096 lines; when full
the oldest 1024 are dropped. `Version` increments on every append and on `Clear`, so a UI can poll
cheaply. All members lock a private gate, so the log can be read from the UI thread while the tick
thread writes. `LogAllowed` and `EventAllowed` let callers skip the formatting work for a line that
would be suppressed (they count the suppression themselves).

`LogFormatter` turns a program's format string and argument buffer into text:[^log-formatter][^log-format]

```csharp
public const int MAX_LINE_LENGTH = 255;
public static string Format(string fmt, ReadOnlySpan<byte> buffer, Func<int, uint, string?>? enumName = null);
public static string Cut(string text);                 // > 255 chars → first 254 + '…'
public static string Float(LogFormat.Spec s, double v);
public static string Float(char conv, double v, int precision);
```

| conversion | buffer bytes | rendering |
| --- | --- | --- |
| `%d`, `%u`, `%x` | 4 | signed / unsigned / hex integer |
| `%lld`, `%llu`, `%llx` | 8 | 64-bit forms |
| `%f`, `%e`, `%g` | 8 (`double`) | C-style fixed / exponent / shortest; `nan`, `inf` |
| `%hf`, `%he`, `%hg` | 4 (`float`) | same, from a `float` |
| `%{B}` | 4 | `true` / `false` |
| `%{E:n}` | 4 | name from the image's enum table `n`, or the number |
| `%%` | 0 | a literal `%` |

Flags `-`, `+`, space, `0`, `#`, a width and a precision (each at most 99) follow C `printf` rules;
`%{…}` accepts only `-` and a width. Each value is aligned to its own size in the buffer. Formats
are validated when the program is built (`LogFormat.MAX_VALUES = 8`, `MAX_BYTES = 64`); see
[kC builtins and intrinsics](/language/kc-builtins-and-intrinsics.md).

# Traces: `TraceStore`, `TraceSample`, `TraceBufferPool`

```csharp
public readonly record struct TraceSample(ulong K, float Value);

public sealed class TraceStore
{
    public const int SAMPLES_PER_CHANNEL = 2048;
    public const int MAX_CHANNELS = 64;
    public IReadOnlyList<string> Channels { get; }
    public TraceStore(TraceBufferPool? pool = null);          // default: TraceBufferPool.Shared
    public bool Add(string channel, ulong k, float value);    // false: a 65th channel
    public List<TraceSample> Samples(string channel);         // copy
    public void CopyChannels(List<string> into);
    public void CopyLast(string channel, int count, List<TraceSample> into);
    public void KeepOnly(IReadOnlyList<string> keep);
    public void Clear();
}

public sealed class TraceBufferPool
{
    public const int MAX_FREE = 16;
    public static TraceBufferPool Shared { get; }
    public int Free { get; }
    public void Reserve(int count);
    internal List<TraceSample> Rent();
    internal void Return(List<TraceSample> buffer);
}
```

[^trace-store]

A channel is a named list of `(k, float)` samples capped at 2048; when full, the oldest 512 are
removed. Channels are created on first `Add`, in order, up to 64. All members lock. Buffers come
from a shared pool so that creating channels does not allocate in steady state.

Three stores exist per computer, all rendered by the oscilloscope ([views](/host/views.md)):

| store | fed by | channel name |
| --- | --- | --- |
| `HostComputer.Traces` | the `trace` service | the program's channel string |
| `HostComputer.Watches` | watched globals, once per tick | the global's leaf path |
| `BusTracker.Samples` | plotted message fields, once per tick | `MSG_NAME.field` |

`Traces` is not cleared by `Start` or `Load` in the host layer (only `Watches` and the bus samples
are reset in `ResetViewData`).

# Modding notes

- `ApplyHostRequest` is **private**, `ApplyRequest` is **protected virtual** on a class whose only
  subclasses are sealed; neither can be reached without Harmony. Both return `bool` ("handled"),
  which makes a prefix that sets `__result = true` and returns `false` a clean way to claim an id.
  The variant actually tested (offline, against the real `KSA.dll`) is a **postfix** on
  `VehicleComputer.ApplyRequest` that turns a `false` result into `true`; it is the recommended
  recipe — see [adding a service](/modding/adding-a-service.md) and
  [the offline harness](/modding/offline-harness.md).[^offline-harness]
- `RejectByHost` is protected; `VehicleComputer.RejectService` is its public wrapper.
  `VehicleComputer.Event` is the public way to write a host line; `Console` is public too.
- `Machine.SyscallHost` can be replaced without a patch, giving a mod the synchronous result path
  for its own ids — provided it forwards ids 64 and 65 to the original host. The tested variant is
  instead a Harmony postfix on the explicit `KSA.Kfs.Vm.ISyscallHost.IssueResult` implementation,
  whose span parameters Harmony 2.4.2 handled.[^offline-harness]
- Anything done in `IssueResult` happens before the tick's fate is known. Follow the burn-handle
  pattern: allocate or look up in `IssueResult`, act only when the request arrives through
  `ISyscallSink.Request` after a latched tick.
- Requests are handled on the dispatch thread. The stock game never touches main-thread-only game
  state from there; it queues `MainWork` and applies it in `KfsServices.MainThread`, called from
  `KfsIntegration.BeforeInputEvents`. A mod controlling parts should do the same.
- A new service also needs a row in `GameTables.Services` with a wired `Kind` (or the VM traps),
  sensible `Domains` (or none), and compiler-side knowledge of its name and signature: for kC a
  prototype in a `<…>` header plus the name in `SystemHeaders.Builtins`; kASM takes `SVC_<SYMBOL>`
  from the loaded `services.json` row with nothing further.[^offline-harness]
- Limits to design against: 256 requests per tick, one synchronous `u32`-cell result array per
  call, 20 host events per machine second (so do not log every request), 64 trace channels.

[^host-computer]: HostComputer.cs — `SleepRequest` :215; `ISyscallSink.Request` :670–676; `Rejected` :678–685; `Dropped` :687–693; `ArgumentText` :695–735; `RejectByHost` :737–745; `ApplyHostRequest` :747–811; `ApplyRequest` :813–816; `ResetViewData` :523–535.
[^vehicle-computer]: VehicleComputer.cs — `_cooked`, `_issuedThisTick`, `_noRequests`, `TOO_MANY_PLANS` :49–55; constructor sets `Machine.SyscallHost` :149; `TakeServiceRequests` :153–162; `RejectService` :164–167; `Event` :169–172; `IssueResult` :174–183; `ApplyRequest` :185–239; `AfterPlayerTick` :241–257; `LeaveControl` clears the override :415–428.
[^kvm-machine]: KvmMachine.cs — `MAX_REQUESTS_PER_TICK` :157; `SyscallHost` :257; `Op.SYSCALL` :1122–1194; rejected counter in the system block :485; requests cleared when the tick did not latch :2199–2202; `CountRejectedRequest` :2072–2075; `DrainRequests` :2229–2246; `IsWiredService` :2248–2258.
[^vm-service-types]: ServiceRequest.cs:3; ServiceRejection.cs:3; ISyscallSink.cs:3–14; ISyscallHost.cs:5–8; ServiceIds.cs:3–46; BurnEndCause.cs:3–13.
[^arg-domains]: ArgDomains.cs — `Check` :53–190 (domain names :69–177); event parameters :200–240.
[^service-info]: ServiceInfo.cs:3–14.
[^game-tables]: GameTables.cs — `Services` dictionary :67; `BuildServices` (reserved rows skipped, kinds accepted) :538–580.
[^image-reader]: ImageReader.cs — unknown service id :557–560; `service_table_version` :642–644.
[^services-json]: services.json (whole file, table version 1).
[^kfs-services]: KfsServices.cs — `AfterDispatch` takes the requests :186–189; `Apply` :310–449; `BurnStart` :469–520; `Start` :522–541; `MainThread` :551–624; `PlanFailed` :626–631.
[^burn-handles]: BurnHandles.cs:6–127; BurnHandle.cs:5–45; BurnStatusState.cs:3–12.
[^service-override]: ServiceOverride.cs:3–67.
[^console-log]: ConsoleLog.cs — limits :9–13; `LogAllowed`/`Log` :46–78; `EventAllowed`/`Event` :80–116; `Host` :118–128; `RollSecond` :138–156; `Append` :158–166; LogLine.cs:3.
[^log-formatter]: LogFormatter.cs — `Format` :13–68; `Cut` :70–77; `Integer` :79–127; `Float` :160–219.
[^log-format]: LogFormat.cs — `Spec` :12; limits :14–18; `TryParseSpec` :59–200.
[^trace-store]: TraceStore.cs:7–134; TraceSample.cs:3; TraceBufferPool.cs:6–59.
[^kfs-vehicle-host]: KfsVehicleHost.cs — override applied to pilot inputs :540–551; requests taken after `Dispatch` :512–566.
[^kfs-host-sampler]: KfsHostSampler.cs — `service_*` leaves :554–558.
[^offline-harness]: Offline harness — experiments E3 (kC prototype and `Builtins`), E5 (the two postfixes), E9 (kASM symbol from the row).
