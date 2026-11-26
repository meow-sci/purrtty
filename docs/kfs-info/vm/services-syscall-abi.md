---
type: Subsystem Reference
title: kVM services — the SYSCALL ABI, argument domains, results and delivery
description: Exactly what the VM does when a program executes SYSCALL — table lookup, argument marshalling, domain validation, synchronous results, queuing, per-tick limits and delivery to the host — and what that means for adding new services.
resource: ksa-decomp:KSA.Kfs.Vm/KvmMachine.cs
tags: [kfs, vm, kvm, syscall, services, abi, extensibility, arg-domains]
sources:
  - id: kvm-machine
    resource: ksa-decomp:KSA.Kfs.Vm/KvmMachine.cs
    title: KvmMachine (decompiled) — SYSCALL case, DrainRequests, IsWiredService
  - id: arg-domains
    resource: ksa-decomp:KSA.Kfs.Vm/ArgDomains.cs
    title: ArgDomains (decompiled)
  - id: syscall-types
    resource: ksa-decomp:KSA.Kfs.Vm/ServiceRequest.cs
    title: ServiceRequest, ServiceRejection, ISyscallSink, ISyscallHost (decompiled)
  - id: service-ids
    resource: ksa-decomp:KSA.Kfs.Vm/ServiceIds.cs
    title: ServiceIds constants (decompiled)
  - id: service-info
    resource: ksa-decomp:KSA.Kfs.Vm.Tables/ServiceInfo.cs
    title: ServiceInfo and ServiceRow records (decompiled)
  - id: game-tables
    resource: ksa-decomp:KSA.Kfs.Vm.Tables/GameTables.cs
    title: GameTables.BuildServices, CheckDomains, EnumDomain, FiniteRange, CellsOf (decompiled)
  - id: image-reader
    resource: ksa-decomp:KSA.Kfs.Vm.Image/ImageReader.cs
    title: ImageReader (decompiled) — syscall and service-table rules
  - id: cost-model
    resource: ksa-decomp:KSA.Kfs.Vm.Image/CostModel.cs
    title: CostModel (decompiled)
  - id: services-json
    resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.services.json
    title: services.json (embedded)
  - id: host-computer
    resource: ksa-decomp:KSA.Kfs.Host/HostComputer.cs
    title: HostComputer (decompiled) — the ISyscallSink
  - id: vehicle-computer
    resource: ksa-decomp:KSA.Kfs.Host/VehicleComputer.cs
    title: VehicleComputer (decompiled) — the ISyscallHost
  - id: offline-harness
    resource: /modding/offline-harness.md
    title: Offline harness experiments E1–E12
generated: { by: claude-code/claude-fable-5-2, at: 2026-11-25T21:46:43Z }
status: stable
ksa_build: "2026.11.25.5949"
---

# Summary

A program reaches the host through one instruction, `SYSCALL id` (opcode `0x78`, a `u16` immediate). The
VM's handling is **almost entirely data-driven from `GameTables.Services`**: the table row tells it how many
cells to pop, how to validate them, how many result cells to push and what the call costs. The VM does not
know what any service *means*; it turns the call into a `ServiceRequest` record and hands it to the host
after the tick.[^kvm-machine]

The answers to the extension questions, each justified below:

| Question | Answer |
| --- | --- |
| Can a program call an id that is not in the table? | No. The image **fails to load** (rule `syscall`, "unknown service id"). If it somehow ran, `SYSCALL` would trap `BAD_SYSCALL`. |
| A `reserved` row? | Reserved rows are never added to `GameTables.Services`, so they behave exactly like unknown ids. |
| A row of kind `query`? | Accepted by the table builder and the image loader, but `IsWiredService` is false → trap `BAD_SYSCALL` at run time. No stock row uses it. |
| Wrong argument count? | Not detectable at run time. The VM pops `ArgCells` cells; fewer than that above the function's floor traps `OPERAND_UNDERFLOW`. The compilers check stack heights statically. |
| Is per-service behaviour table-driven? | Yes, with **one hard-coded id in the VM**: `id == 1` (`log`) additionally captures a memory buffer. Everything else is driven by `ServiceInfo`. |
| Is adding a table entry enough for the VM? | Yes for the VM: it will load, cost, marshal, validate and queue the request. It is **not** enough end to end — the stock host rejects ids it does not recognise. |
| How does a result reach the program? | **Synchronously, inside the same `SYSCALL` instruction**, through `ISyscallHost.IssueResult`, before the request is delivered or even known to be kept. |
| Limits? | 256 issued requests per tick per machine. Argument and result cells are bounded only by the task's operand stack (≤ 64 cells, 16 on `K8`). |

# The pieces

```csharp
public sealed record ServiceRequest(ushort Id, uint[] Args, byte[]? Bytes, int Task = -1, uint[]? Results = null);
public sealed record ServiceRejection(ushort Id, int Entry, int Argument, uint[] Args, int Task, string Reason);

public interface ISyscallHost {                       // result provider, called during the instruction
    void IssueResult(ushort service, ReadOnlySpan<uint> args, Span<uint> results);
}
public interface ISyscallSink {                       // receiver, called after the tick
    void Request(ServiceRequest request);
    void Rejected(ServiceRejection rejection) { }     // default interface method: no-op
    void Dropped(int count) { }                       // default interface method: no-op
}
```

[^syscall-types]

```csharp
public sealed record ServiceInfo(ushort Id, string Name, string Kind, string[] ArgTypes, int ArgCells,
    int ResultCells, string CostClass, bool PerValue, string? RequiresStock, string[]? Domains = null,
    int[]? ArgumentOf = null, string[]? ArgumentNames = null, string Summary = "")
{
    public int Argument(int entry);   // ArgumentOf[entry], or entry itself when there is no map
}
```

[^service-info]

`GameTables.Services` is a `Dictionary<ushort, ServiceInfo>` with a **public getter and a private setter**
(`GameTables.cs:67`); it is filled by `BuildServices` from `services.json`.[^game-tables] In the game,
`HostComputer` implements `ISyscallSink` and `VehicleComputer` implements `ISyscallHost` and installs itself
with `Machine.SyscallHost = this` (`VehicleComputer.cs:146-151`).[^host-computer][^vehicle-computer]

# Before the program runs: table and image checks

## Building `ServiceInfo` from a row (`GameTables.cs:538-582`)

- `id` must be 0–65535.
- A row with `"reserved": true` may not carry `kind`, `arg_types` or `cost_class`, and is **skipped** — it
  never enters `Services`.
- Otherwise `kind` must be `query`, `request` or `request+result`; `arg_types`, `result_types`,
  `cost_class` (one of the `costs.json` service classes), `symbol` and a non-blank `summary` are required.
- `CheckDomains`: `arg_domains` and `host_checks` must be present, there must be exactly one domain per
  `arg_types` entry, and each domain must fit its type (table in [Argument domains](#argument-domains)).
- `ArgCells` and `ResultCells` are the sums of `CellsOf(type)`:

| Type | Cells |
| --- | --- |
| `i32`, `u32`, `f32`, `ptr`, `cell` | 1 |
| `i64`, `f64` | 2 |
| anything else | `GameTablesException` "unknown cell type" |

- `ArgumentOf`/`ArgumentNames` map table entries to source-level arguments by parsing the `kc` column's
  parameter list (so `kfs_translate(dv)` has one argument covering three `f32` entries); they are used only
  for diagnostics text.
- A duplicate id throws.

Row schema and the full table content are documented in [services table](/tables/services-table.md).

## Loading an image

| Check | Location | Failure |
| --- | --- | --- |
| Every `SYSCALL` immediate is a key of `tables.Services` | `ImageReader.cs:557-560` | rule `syscall`: `SYSCALL at 0x…: unknown service id 0x….` |
| Header `service_table_version` (u16 at offset 32) equals `tables.ServiceTableVersion` = `services.json` `version` | `ImageReader.cs:642-645`, `GameTables.cs:69` | rule `service-table`: `service_table_version differs from the game's` |
| The instruction's cost (`syscall` base + cost class + per-value part) fits the tick budget[^cost-model] | `ImageReader.cs:561-569`, `CostModel.cs:26-37` | rule `cost` |

Note that the loader tests only `ContainsKey`; it does **not** test the kind. That is why a `query` row gets
past the loader and fails at run time.[^image-reader]

# The `SYSCALL` instruction, step by step

`KvmMachine.cs:1122-1194`. `id` is the instruction's immediate; `depth` is the operand stack depth; cycles for
the instruction were charged before this code runs.

| # | Step | Code |
| --- | --- | --- |
| 1 | `if (!IsWiredService(id)) throw BAD_SYSCALL`. `IsWiredService` = the id is in `_tables.Services` **and** its `Kind` is `"request"` or `"request+result"`. | `:1125-1128`, `:2248-2258` |
| 2 | `svc = _tables.Services[id]`; `Need(svc.ArgCells)` → `OPERAND_UNDERFLOW` if the cells are not there above the floor. | `:1129-1130` |
| 3 | **Per-tick cap.** If `_issuedThisTick >= 256`: pop the arguments, `_rejected++`, `_droppedThisTick++`, update `sys.rejected_requests`, push `ResultCells` zero cells, and stop. No request, no rejection record. | `:1131-1143` |
| 4 | `_issuedThisTick++`; copy the arguments into `uint[] args`, `args[i] = Cell(depth − ArgCells + i)` — **`args[0]` is the deepest cell, i.e. the first argument pushed**. | `:1144-1149` |
| 5 | **Only for `id == 1` (`log`)**: look up `image.Formats[args[0]]` (a format-string index) — missing → `BAD_SYSCALL`; then read `BufferBytes` bytes at address `args[1]` through `Resolve` (read, 8-byte aligned; may trap `NULL_ACCESS`/`BAD_ADDRESS`/`MISALIGNED`) into `byte[] bytes`. | `:1150-1158` |
| 6 | Pop the arguments (`depth -= ArgCells`). | `:1159` |
| 7 | Lazily create the machine's `ArgDomains` and run `Check(svc, args, ctx)` with `ctx = (met_us now, image.Strings, "does this computer publish message m")`. | `:1160-1165` |
| 8a | **Domain failure** → `_rejected++`, update `sys.rejected_requests`, queue `new ServiceRejection(id, entry, svc.Argument(entry), args, task, reason)`, push `ResultCells` zero cells. No request is queued and `IssueResult` is not called. | `:1167-1177` |
| 8b | **Accepted** → if `svc.ResultCells > 0`: allocate `uint[] results`, call `SyscallHost?.IssueResult(id, args, results)` and push every result cell in order. Then queue `new ServiceRequest(id, args, bytes, task, results)` (`results` is null for a service without result cells). | `:1178-1191` |
| 9 | Attribute the instruction's cost to a profiler pseudo-function named `svc.Name`. | `:1192` |

Observations that follow directly from the code:

- **The kind string does not control results.** Whether `IssueResult` is called and cells are pushed
  depends on `ResultCells > 0`, not on `Kind == "request+result"`. `Kind` matters to the VM only in
  `IsWiredService`.
- **Results are produced before delivery.** `IssueResult` runs inside the instruction, on the thread that is
  executing `RunTick`. The program can use the value in its very next instruction. The corresponding
  `ServiceRequest` reaches the host only after the whole tick, and only if the tick latched.
- **A null `SyscallHost` yields zeros.** `SyscallHost?.` leaves `results` zero-filled; that is the case for
  any machine not owned by a `VehicleComputer`: the CLI's bare `KvmMachine` and the bench machine
  (`BenchMachine : HostComputer`) — `VehicleComputer`'s constructor is the only place in the decompiled
  code that assigns the property.
- **Rejected or dropped calls still balance the stack**: arguments are popped and zero results pushed, so
  the program continues with a result of 0 and can only notice through `sys.rejected_requests`.
- **A trap inside steps 1–5 leaves the arguments on the stack** and, for step 5, has already counted the
  call against the per-tick cap.

## Stack ABI

- Arguments are pushed left to right; the last argument is on top. `Args` is in push order, cell by cell.
- A 64-bit argument occupies two cells, **low word first (deeper)**: `ArgDomains` and the host both rebuild
  it as `args[n] | ((ulong)args[n+1] << 32)` (`ArgDomains.cs:133`, `HostComputer.cs:789`).
- Floats are raw IEEE-754 bit patterns in a cell.
- Result cells are pushed in array order: `results[0]` deepest, the last result on top.
- `Task` is the index of the calling task in `image.Tasks`.
- `Bytes` is non-null only for `log`.

# Argument domains

`ArgDomains` is a `public sealed class` constructed from a `GameTables`; one instance is created per machine
on its first `SYSCALL` and kept in the private field `_domains` for the machine's lifetime
(`KvmMachine.cs:155, 1160-1163`).[^arg-domains]

## Construction-time caches (`ArgDomains.cs:22-51`)

- `_catalogueIds`: every `messages.json` message id parsed as hex — for the `MESSAGE` event parameter.
- `_enums`: for every service's domain string starting with `enum:`, the inclusive value range from
  `tables.EnumDomain(text)`.
- `_ranges`: for every domain string starting with `finite:[`, the `(lo, hi)` pair from
  `GameTables.FiniteRange(text)`.

These are filled by iterating `tables.Services.Values` **once**. A service added to the table after a
machine has built its `ArgDomains` is still validated for the fixed domain keywords, but an `enum:` or
`finite:[…]` domain string that was not already cached is simply **not checked** on that machine (the
`default:` branch finds no cache entry and does nothing, `ArgDomains.cs:150-173`).

## `Check` (`ArgDomains.cs:53-187`)

```csharp
public (int Entry, string Reason)? Check(ServiceInfo svc, ReadOnlySpan<uint> cells, in Context ctx)
public readonly record struct Context(long MetUs, StringEntry[] Strings, Func<ushort, bool> Publishes);
```

If `svc.Domains` is null there is nothing to check. Otherwise it walks the domain list entry by entry,
advancing a cell cursor by `CellsOf(svc.ArgTypes[i])`, and returns the **first** failing entry with a reason
string, or null.

| Domain | Type it must annotate (`CheckDomains`) | Run-time check | Reason strings |
| --- | --- | --- | --- |
| `any` | any | none | — |
| `ptr` | `ptr` | none | — |
| `format` | `u32` | none here (the `log` special case validates the format index and traps instead) | — |
| `count` | `u32` | none | — |
| `finite` | `f32` | `float.IsFinite` | `not finite` |
| `finite:>=0` | `f32` | finite and `>= 0` | `not finite`, `below 0` |
| `finite:[lo,hi]` | `f32` (and the range must parse with `lo <= hi`) | finite and `lo <= v <= hi` | `not finite`, `outside [lo, hi]` |
| `quat` | `f32` | this cell finite; and on the **first** entry of a run of consecutive `quat` entries, the four cells starting here (if all finite) must have a Euclidean norm within `0.001` of 1 | `not finite`, `norm not within 1e-3 of 1` |
| `vec_nonzero` | `f32` | this cell finite; and on the first entry of a run, the three cells starting here must not all equal 0 | `not finite`, `all zero` |
| `event` | `u32` | the value is an `events.json` id whose name is not `TIME` | `not an event sleep_until_event may name` |
| `event_param` | `cell` | depends on the **preceding** cell (the event id); see below | see below |
| `future` | `i64` | the signed 64-bit value must be `>` the current `met_us` (`k · 10⁶ / tick_hz`) | `not in the future` |
| `handle` | `u32` | non-zero | `handle 0` |
| `channel` | `u32` | an index into the image's string table whose entry has kind 2 (channel) | `not a declared channel` |
| `enum:NAME` | `u32` (and `NAME` must resolve) | `0 <= v <= count − 1` | `not in NAME` |
| `enum:NAME[a..b]` | `u32` | `a <= v <= b` (unsigned) | `not in NAME[a..b]` |
| anything else | rejected by `CheckDomains` when loaded from JSON | **no check** | — |

`event_param` (`EventParam`, `ArgDomains.cs:199-248`), by the name of the event in the previous cell:

| Event | Check on the parameter cell | Reason |
| --- | --- | --- |
| unknown id, or `TIME` | none (the `event` domain on the previous entry already rejected it) | — |
| `APOAPSIS`, `PERIAPSIS` | `f32`, finite and not `< 0` | `lead time not finite`, `lead time below 0` |
| `ALTITUDE_BELOW` | `f32`, finite | `altitude not finite` |
| `BURN_START` | non-zero | `handle 0` |
| `MESSAGE` | a catalogue message id, ≤ 65535, that this computer does **not** publish | `not a catalogue message`, `a message this computer publishes` |
| any other (`SOI_CHANGE`, `ATMOSPHERE_ENTRY`) | must be 0 | `not 0 for an event without a parameter` |

`enum:` resolution (`GameTables.EnumDomain`, `GameTables.cs:702-730`): `NAME` is looked up in
`enums.json` `enums` (the member count gives the range), except `roll_mode`, whose count is the number of
catalogue enum constants whose name starts with `KFS_ROLL_`. An optional `[a..b]` suffix narrows the range
and must satisfy `0 <= a <= b < count`.

Pitfalls for hand-built `ServiceInfo` objects (they bypass `CheckDomains`):

- `Domains.Length` must not exceed `ArgTypes.Length` (`svc.ArgTypes[i]` is indexed per domain).
- `quat` reads four cells from the cursor, `vec_nonzero` three, `future` two, and `event_param` reads the
  cell before the cursor; a domain placed where those cells do not exist throws
  `IndexOutOfRangeException` out of `RunTick` (it is not a trap).
- Only the first entry of a *run* of `quat`/`vec_nonzero` is group-checked, so two vectors declared back to
  back with the same domain are checked as one run (the second group gets only the per-cell finite check).

# Rejections: issue-time versus host

| | Issue-time (VM) | Host |
| --- | --- | --- |
| When | Inside the `SYSCALL` instruction | After the tick, while or after applying the request |
| Why | A domain check failed, or the 256-per-tick cap was hit | Anything the host decides: unsupported, wrong mode, bad handle, no vehicle… |
| Counter | `_rejected++` immediately, mirrored into `sys.rejected_requests` (`:1134-1136, 1170-1171`) | `KvmMachine.CountRejectedRequest()` (`:2072-2075`), visible to the program from the next task start |
| Record | `ServiceRejection` (not for cap drops) → `ISyscallSink.Rejected`; cap drops → `ISyscallSink.Dropped(count)` | none in the VM; the host writes a console event |
| Request delivered? | No | It was delivered; the host chose not to act |
| Result cells | Zeros | Whatever `IssueResult` already returned |

`ServiceRejection.Entry` is the index into `arg_types`/`arg_domains`; `Argument` is the source-level
argument index (`ServiceInfo.Argument(entry)`). The stock sink prints
`rejected {name} argument {Argument} = {value}: {Reason}` and
`dropped {count} service request(s): a tick may issue 256` (`HostComputer.cs:678-693`).[^host-computer]

`sys.rejected_requests` counts both kinds since reset; it is saved and restored with machine state.

# Queuing, the per-tick limit and delivery

- `_requests` and `_rejections` are per-machine `List<>`s. `RunTick` clears both, and zeroes
  `_issuedThisTick`/`_droppedThisTick`, **at the start of every executed tick** (`:2108-2111`) — anything
  the host did not drain is lost.
- `MAX_REQUESTS_PER_TICK = 256` (`:157`) is compared with `_issuedThisTick`, which counts every call that
  got past step 3, **including ones then rejected by a domain check**. It is per machine and per tick, across
  all tasks.
- At the end of a tick that did **not** latch (a periodic task overran, or a trap occurred),
  `_requests.Clear()` discards every request of that tick (`:2199-2202`). Rejections and the dropped count
  survive.
- `DrainRequests(ISyscallSink sink)` (`:2229-2246`) calls `sink.Request(r)` for each queued request in issue
  order, clears the list, then `sink.Rejected(r)` for each rejection, clears, then `sink.Dropped(n)` if any
  were dropped. `HostComputer.RunTick` calls it right after `machine.RunTick()`
  (`HostComputer.cs:428-439`).
- `PendingRequests` / `PendingRejections` expose the queue lengths.

Because results are issued before the latch decision, a result-returning call can be "issued but never
delivered" when its tick later overruns or traps. The stock host handles that for burn handles: every
handle issued in a tick is tracked and, if its request was not delivered, ended as `PlanningFailed` with
`CountRejectedRequest()` and an event (`VehicleComputer.cs:241-257`).[^vehicle-computer]

# What the stock host does with a request (pointers)

The VM's responsibility ends at `sink.Request`. For orientation — details in
[service request handling](/host/service-request-handling.md) and
[game-side services](/integration/kfs-services-game-side.md):

- `HostComputer.ISyscallSink.Request` → `ApplyHostRequest(request)` (a `switch (request.Id)` over 1, 2, 3,
  16, 17) → else the virtual `ApplyRequest(request)` → else
  `RejectByHost(request, "no vehicle to act on")` (`HostComputer.cs:670-676, 747-816`).
- `VehicleComputer.ApplyRequest` is a `switch (request.Id)` over 32, 48–57, 64–67 (54, `translate`, is
  always rejected there); its `default` returns
  false (`VehicleComputer.cs:185-239`). **An id unknown to these switches is therefore rejected by the
  host and counted**, even though the VM marshalled it correctly.
- The requests `VehicleComputer` accepts are only queued; a **third** hard-coded switch applies them
  game-side later in the same dispatch: `KfsServices.Apply`, a `switch (r.Id)` over 32, 48–53, 55–57 and
  64–67 with no `default` (`KSA.Kfs/KfsServices.cs:310-449`).
- `VehicleComputer.ISyscallHost.IssueResult` fills `results[0]` with a new burn-handle id for services 64
  and 65 only (`VehicleComputer.cs:174-183`).

# `cost_class`, `per_value`, `requires_stock`

| Field | Used by the VM? |
| --- | --- |
| `cost_class` → `ServiceInfo.CostClass` | Yes, at image load only: `CostModel.Cost` adds `ServiceClassCycles[CostClass]` to the `syscall` base (`CostModel.cs:26-37`). An unknown class name throws `KeyNotFoundException` during load. See [cost model](/vm/cost-model-and-budgets.md). |
| `per_value` → `ServiceInfo.PerValue` | Yes, at image load only: adds `log_cycles_per_value` × (largest value count of any format string in the image). Only `log` sets it. |
| `requires_stock` → `ServiceInfo.RequiresStock` | **No.** Nothing in `KSA.Kfs.Vm` reads it. It is read by the kC front end (`KcFrontEnd.cs:217`, diagnostics about steering the stock flight computer) and the in-game reference (`KfsReferenceWindow.cs:424`). See [ownership and control modes](/host/ownership-and-control-modes.md). |

# `ServiceIds` and the table version

`ServiceIds` is a static class of `public const ushort` values.[^service-ids] Being constants, they are
inlined by the C# compiler: no decompiled code references `ServiceIds`, and the id switches in the host
show bare numbers.

| Constant | Value | | Constant | Value |
| --- | --- | --- | --- | --- |
| `TABLE_VERSION` | 1 | | `SVC_SET_ATTITUDE_PROFILE` | 51 |
| `SVC_LOG` | 1 | | `SVC_SET_THROTTLE` | 52 |
| `SVC_TRACE` | 2 | | `SVC_ENGINES_ON` | 53 |
| `SVC_HALT` | 3 | | `SVC_TRANSLATE` | 54 |
| `SVC_SLEEP_UNTIL` | 16 | | `SVC_HOLD_HEADING` | 55 |
| `SVC_SLEEP_UNTIL_EVENT` | 17 | | `SVC_HOLD_QUAT` | 56 |
| `SVC_STAGE` | 32 | | `SVC_ENGINES_OFF` | 57 |
| `SVC_HOLD_DIRECTION` | 48 | | `SVC_PLAN_CIRCULARIZE` | 64 |
| `SVC_HOLD_RATES` | 49 | | `SVC_PLAN_BURN` | 65 |
| `SVC_RELEASE_ATTITUDE` | 50 | | `SVC_BURN_START` | 66 |
| | | | `SVC_BURN_ABORT` | 67 |

There is no constant for the reserved id 4 (`event`). The version that images are actually checked against
is `GameTables.ServiceTableVersion` (`(ushort)ServicesData.Version`), not the `TABLE_VERSION` constant.

## The stock table as the VM sees it

Derived columns (`Arg cells`, `Result cells`) are what `BuildServices` computes.[^services-json]

| Id | Hex | Symbol | Name | Kind | `arg_types` | Arg cells | `result_types` | Result cells | `cost_class` | `per_value` | `requires_stock` | `arg_domains` |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | `0x0001` | `SVC_LOG` | `log` | `request` | `u32`, `ptr` | 2 | — | 0 | `Medium` | true | null | `format`, `ptr` |
| 2 | `0x0002` | `SVC_TRACE` | `trace` | `request` | `u32`, `f32` | 2 | — | 0 | `Light` | — | null | `channel`, `any` |
| 3 | `0x0003` | `SVC_HALT` | `halt` | `request` | — | 0 | — | 0 | `Light` | — | null | — |
| 4 | `0x0004` | — | `event` | *reserved* | — | — | — | — | — | — | — | — |
| 16 | `0x0010` | `SVC_SLEEP_UNTIL` | `sleep_until` | `request` | `i64` | 2 | — | 0 | `Medium` | — | null | `future` |
| 17 | `0x0011` | `SVC_SLEEP_UNTIL_EVENT` | `sleep_until_event` | `request` | `u32`, `cell` | 2 | — | 0 | `Medium` | — | null | `event`, `event_param` |
| 32 | `0x0020` | `SVC_STAGE` | `stage` | `request` | — | 0 | — | 0 | `Light` | — | null | — |
| 48 | `0x0030` | `SVC_HOLD_DIRECTION` | `hold_direction` | `request` | `u32`, `u32` | 2 | — | 0 | `Light` | — | `GUIDANCE` | `enum:direction[2..21]`, `enum:roll_mode` |
| 49 | `0x0031` | `SVC_HOLD_RATES` | `hold_rates` | `request` | — | 0 | — | 0 | `Light` | — | `GUIDANCE` | — |
| 50 | `0x0032` | `SVC_RELEASE_ATTITUDE` | `release_attitude` | `request` | — | 0 | — | 0 | `Light` | — | `GUIDANCE` | — |
| 51 | `0x0033` | `SVC_SET_ATTITUDE_PROFILE` | `set_attitude_profile` | `request` | `u32` | 1 | — | 0 | `Light` | — | `ATTITUDE_CONTROL` | `enum:attitude_profile` |
| 52 | `0x0034` | `SVC_SET_THROTTLE` | `set_throttle` | `request` | `f32` | 1 | — | 0 | `Light` | — | `GUIDANCE` | `finite:[0,1]` |
| 53 | `0x0035` | `SVC_ENGINES_ON` | `engines_on` | `request` | — | 0 | — | 0 | `Light` | — | `GUIDANCE` | — |
| 54 | `0x0036` | `SVC_TRANSLATE` | `translate` | `request` | `f32`, `f32`, `f32` | 3 | — | 0 | `Light` | — | `GUIDANCE` | `finite`, `finite`, `finite` |
| 55 | `0x0037` | `SVC_HOLD_HEADING` | `hold_heading` | `request` | `f32`, `f32`, `u32` | 3 | — | 0 | `Light` | — | `GUIDANCE` | `finite`, `finite:[-1.5707964,1.5707964]`, `enum:roll_mode` |
| 56 | `0x0038` | `SVC_HOLD_QUAT` | `hold_quat` | `request` | `f32`, `f32`, `f32`, `f32`, `u32` | 5 | — | 0 | `Light` | — | `GUIDANCE` | `quat`, `quat`, `quat`, `quat`, `enum:roll_mode` |
| 57 | `0x0039` | `SVC_ENGINES_OFF` | `engines_off` | `request` | — | 0 | — | 0 | `Light` | — | `GUIDANCE` | — |
| 64 | `0x0040` | `SVC_PLAN_CIRCULARIZE` | `plan_circularize` | `request+result` | `u32` | 1 | `u32` | 1 | `Heavy` | — | null | `enum:apsis` |
| 65 | `0x0041` | `SVC_PLAN_BURN` | `plan_burn` | `request+result` | `f32`, `f32`, `f32`, `i64` | 5 | `u32` | 1 | `Heavy` | — | null | `vec_nonzero`, `vec_nonzero`, `vec_nonzero`, `future` |
| 66 | `0x0042` | `SVC_BURN_START` | `burn_start` | `request` | `u32` | 1 | — | 0 | `Light` | — | `GUIDANCE` | `handle` |
| 67 | `0x0043` | `SVC_BURN_ABORT` | `burn_abort` | `request` | — | 0 | — | 0 | `Light` | — | `GUIDANCE` | — |

Used ids: 1–4, 16–17, 32, 48–57, 64–67. The `SYSCALL` immediate and `ServiceInfo.Id` are `ushort`, so the
id space is 0–65535.

# Every place `KSA.Kfs.Vm*` reads the services table or tests a service id

| Location | What it does |
| --- | --- |
| `KSA.Kfs.Vm/KvmMachine.cs:1125` | `IsWiredService(id)` gate → `BAD_SYSCALL` |
| `KSA.Kfs.Vm/KvmMachine.cs:1129` | `_tables.Services[id]` — the row driving the call |
| `KSA.Kfs.Vm/KvmMachine.cs:1151` | **`if (id == 1)`** — the only hard-coded service id in the VM (`log` buffer capture) |
| `KSA.Kfs.Vm/KvmMachine.cs:2248-2258` | `IsWiredService`: `TryGetValue` + kind is `request` or `request+result` |
| `KSA.Kfs.Vm/ArgDomains.cs:26-50` | Iterates `tables.Services.Values` once to cache `enum:` and `finite:[…]` domains |
| `KSA.Kfs.Vm/ArgDomains.cs:53-187` | `Check` — driven by the `ServiceInfo` passed in; no id tests |
| `KSA.Kfs.Vm.Image/ImageReader.cs:557-560` | `Services.ContainsKey(id)` for every `SYSCALL` in an image |
| `KSA.Kfs.Vm.Image/ImageReader.cs:642-645` | `service_table_version` comparison |
| `KSA.Kfs.Vm.Image/CostModel.cs:26-37` | `Services.TryGetValue(id)` → cost class and per-value cycles |
| `KSA.Kfs.Vm.Image/ImageBuilder.cs:190` | Writes `ServiceTableVersion` into a new image header |
| `KSA.Kfs.Vm.Tables/GameTables.cs:67, 69, 538-582` | The dictionary, the version property and `BuildServices` |

Outside these namespaces the table is read by the compilers and tools — `IrValidator.cs:95` (stack effect
from `ArgCells`/`ResultCells`; "unknown service"), `ProgramAnalysis.cs:145`, `BusAnalysis.cs:434`,
`KC/Lowering.cs:1350`, `KC/Parser.cs:3092-3101` (kC function bindings built from the `kc` column of
`ServicesData.Services` and cached per `GameTables` instance), `KAsm/BuiltinIncludes.cs:43-46` (`SVC_*`
constants from `ServicesData.Services`), `KC/KcFrontEnd.cs:217`, `KfsReferenceWindow.cs:375-424` — and by
the host for names in messages (`HostComputer.cs:682, 742`, `VehicleComputer.cs:231, 251`,
`KfsServices.cs:305, 594`). Those are covered in the language, compiler, host and integration areas.

# Modding notes — adding a service

The complete recipe that was exercised end to end — rows added by re-loading `services.json` through
`GameTables.Load` from a postfix on `GameTables.LoadEmbedded`, rather than by mutating the loaded
instance as sketched below — is in [adding a service](/modding/adding-a-service.md); prefer it where the
two differ.

What the **VM** needs, and nothing more:

1. **A `ServiceInfo` in `GameTables.Services`** under an unused `ushort` id. The dictionary object is
   mutable through its public getter (`tables.Services[id] = new ServiceInfo(...)`); `ServiceInfo` is a
   public record with a public constructor that validates nothing, so the caller must supply consistent
   values:
   - `Kind` = `"request"` or `"request+result"` (anything else → `BAD_SYSCALL`);
   - `ArgTypes` from `i32 u32 f32 ptr cell i64 f64`, `ArgCells`/`ResultCells` = the cell sums;
   - `CostClass` = `"Light"`, `"Medium"` or `"Heavy"`;
   - `Domains` = `null` (no VM validation) or one entry per `ArgTypes` entry, using the vocabulary above.
2. **Do it before any image that uses the id is loaded** (the loader checks `ContainsKey` and bakes the
   cost), and before machines execute their first `SYSCALL` if the service relies on `enum:` or
   `finite:[…]` domains (per-machine `ArgDomains` cache). Using only `any`/`finite`/`handle`-style fixed
   keywords, or `null`, avoids the cache issue.
3. **No version bump is needed or wanted.** The image check compares against `ServicesData.Version`, which
   an in-memory addition does not change. An image built with the extra service and loaded in a game
   without it fails cleanly with rule `syscall`.

With that, a program's `SYSCALL id` pops the declared cells, is validated, costs its class, and produces a
`ServiceRequest(id, args, null, task, results)` at the sink. What the VM does **not** give you:

- **A handler.** The stock sink rejects unknown ids with "no vehicle to act on". A mod must intercept before
  that — for example a Harmony prefix on `VehicleComputer.ApplyRequest(ServiceRequest)` (protected override,
  returns `bool` handled) — and then apply the request game-side on the right thread. See
  [service request handling](/host/service-request-handling.md).
- **A result.** For `ResultCells > 0` the value must be written into the `results` span during
  `ISyscallHost.IssueResult`. Two routes: wrap the host — `machine.SyscallHost` has a public setter, so a
  mod can install its own `ISyscallHost` that handles its ids and forwards the rest to the original
  `VehicleComputer` — or patch `VehicleComputer`'s explicit interface implementation.
  `Verified:` a Harmony postfix on that method (`KSA.Kfs.Vm.ISyscallHost.IssueResult`) can take the
  `ushort`, `ReadOnlySpan<uint>` and `Span<uint>` parameters directly — it wrote `results[0]` and the
  program read the value; a postfix on `VehicleComputer.ApplyRequest` that turns `false` into `true`
  likewise claimed the request ([offline harness](/modding/offline-harness.md), E5).[^offline-harness]
- **Deferred or asynchronous results.** There is no mechanism for a result that arrives later: no blocking,
  no completion callback, no sleeping. A "query" has to be answerable synchronously from data that is safe
  to read on the VM's tick thread, or be modelled as *request now, read the answer later* through a bus
  message or a second result-returning call (not a sensor field: vehicle layouts have none in this build).
- **A memory payload.** `Bytes` is captured only for id 1. A service that takes a `ptr` must read VM memory
  itself with `machine.ReadRam` — at `IssueResult` time (only called when `ResultCells > 0`) the RAM is
  exactly as the program left it; at drain time it reflects the end of the tick.
- **Rollback.** `IssueResult` runs before the tick is known to latch. If it has side effects and the tick
  then overruns or traps, the request is silently dropped; mirror the stock handle bookkeeping if that
  matters.
- **Exceptions.** Anything thrown from `IssueResult` or from `ArgDomains.Check` escapes `RunTick`; it is not
  converted to a trap.

Limits to design within: at most 256 calls per tick per machine; `ArgCells` plus the caller's own stack
usage must fit the task's operand stack (`MaxOperandCells` ≤ 64, or ≤ 16 on `K8`), and pushing results past
that limit traps `OPERAND_OVERFLOW`; a kC function can return at most 8 cells (`RET` rule), which bounds how a
wrapper can pass results on. `Verified:` for a service called from kC keep `ResultCells` to **one**: a
one-cell `u32` or `f32` result is delivered synchronously, but a prototype returning `vec3` against
`result_types ["f32","f32","f32"]` made the kC lowering throw `InvalidOperationException` (`label Lend0
reached by fall-through with height 2 and by a jump with 0 … builder invariant`) — an exception, not a
diagnostic (offline harness E6, E8).[^offline-harness]

To make the service *nameable* from kC (`kfs_…()`), kASM (`SVC_…`) and the in-game reference, the raw row
list `GameTables.ServicesData.Services` (a `List<ServiceRow>`) is what those consumers iterate — see
[kC builtins and intrinsics](/language/kc-builtins-and-intrinsics.md) and
[kASM assembly language](/compiler/kasm-assembly-language.md). A raw numeric `SYSCALL` needs only the
`Services` dictionary entry. `Verified:` the row alone is enough for kASM (`SYSCALL SVC_<SYMBOL>` assembles
from the row's `symbol`) but **not** for kC: without a prototype the call is `KC-E003 '<name>' is not
declared`, and with a prototype in a `<…>` header but the name missing from `SystemHeaders.Builtins` it is
`KC-E304 '<name>' is declared but never defined`; with both, the call binds through the row's `kc` column
(offline harness E3, E9).[^offline-harness]

[^kvm-machine]: KvmMachine.cs:151-161 (queues, `_domains`, `MAX_REQUESTS_PER_TICK`, counters), 257-261 (`SyscallHost`, `PendingRequests`, `PendingRejections`), 1122-1194 (SYSCALL), 2072-2075 (`CountRejectedRequest`), 2108-2111 and 2199-2202 (per-tick clearing), 2229-2258 (`DrainRequests`, `IsWiredService`).
[^arg-domains]: ArgDomains.cs:10-51 (context, caches), 53-187 (`Check`), 194-248 (`IsSleepEvent`, `EventParam`).
[^syscall-types]: ServiceRequest.cs:3; ServiceRejection.cs:3; ISyscallSink.cs:3-14; ISyscallHost.cs:5-8.
[^service-ids]: ServiceIds.cs:3-46.
[^service-info]: ServiceInfo.cs:3-14; ServiceRow.cs:5.
[^game-tables]: GameTables.cs:67-69 (`Services`, `ServiceTableVersion`), 475-536 (`CellsOf`), 538-582 (`BuildServices`), 584-639 (`Arguments`), 641-690 (`CheckDomains`), 692-730 (`FiniteRange`, `EnumDomain`).
[^image-reader]: ImageReader.cs:553-569 (syscall rule and cost), 642-645 (service-table rule).
[^cost-model]: CostModel.cs:26-37.
[^services-json]: KSA.Kfs.Vm.Tables.Data.services.json:1-27.
[^host-computer]: HostComputer.cs:428-439 (drain after the tick), 670-693 (sink methods), 737-745 (`RejectByHost`), 747-816 (`ApplyHostRequest`, `ApplyRequest`).
[^vehicle-computer]: VehicleComputer.cs:146-151 (installs itself as `SyscallHost`), 174-183 (`IssueResult`), 185-239 (`ApplyRequest`), 241-257 (undelivered handles).
[^offline-harness]: Experiments E3, E5, E6, E8 and E9 in the offline harness document.
