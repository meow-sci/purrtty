---
type: Data Table Reference
title: services.json — the kFS service (syscall) table
description: Schema, vocabulary and complete contents of the embedded services table that defines every SYSCALL id, its argument cells, domains, cost class and language bindings.
resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.services.json
tags: [kfs, tables, services, syscall, abi, extension]
sources:
  - id: services-json
    resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.services.json
    title: services.json (embedded resource)
  - id: costs-json
    resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.costs.json
    title: costs.json (service_classes, log_cycles_per_value)
  - id: service-records
    resource: ksa-decomp:KSA.Kfs.Vm.Tables/ServiceRow.cs
    title: ServicesFile, ServiceRow, ServiceInfo, ServiceClassRow (decompiled)
  - id: game-tables
    resource: ksa-decomp:KSA.Kfs.Vm.Tables/GameTables.cs
    title: GameTables (decompiled)
  - id: data-json-context
    resource: ksa-decomp:KSA.Kfs.Vm.Tables/DataJsonContext.cs
    title: DataJsonContext (decompiled)
  - id: arg-domains
    resource: ksa-decomp:KSA.Kfs.Vm/ArgDomains.cs
    title: ArgDomains (decompiled)
  - id: kvm-machine
    resource: ksa-decomp:KSA.Kfs.Vm/KvmMachine.cs
    title: KvmMachine (decompiled)
  - id: cost-model
    resource: ksa-decomp:KSA.Kfs.Vm.Image/CostModel.cs
    title: CostModel (decompiled)
  - id: host-computer
    resource: ksa-decomp:KSA.Kfs.Host/HostComputer.cs
    title: HostComputer (decompiled)
  - id: vehicle-computer
    resource: ksa-decomp:KSA.Kfs.Host/VehicleComputer.cs
    title: VehicleComputer (decompiled)
  - id: kfs-services
    resource: ksa-decomp:KSA.Kfs/KfsServices.cs
    title: KfsServices (decompiled)
  - id: kc-parser
    resource: ksa-decomp:KSA.Kfs.KC/Parser.cs
    title: kC Parser (decompiled)
  - id: kc-front-end
    resource: ksa-decomp:KSA.Kfs.KC/KcFrontEnd.cs
    title: KcFrontEnd (decompiled)
  - id: system-headers
    resource: ksa-decomp:KSA.Kfs.KC/SystemHeaders.cs
    title: SystemHeaders (decompiled)
  - id: builtin-includes
    resource: ksa-decomp:KSA.Kfs.KAsm/BuiltinIncludes.cs
    title: BuiltinIncludes (decompiled)
  - id: kfs-reference-window
    resource: ksa-decomp:KSA.Kfs/KfsReferenceWindow.cs
    title: KfsReferenceWindow (decompiled)
  - id: service-ids
    resource: ksa-decomp:KSA.Kfs.Vm/ServiceIds.cs
    title: ServiceIds (decompiled)
  - id: offline-harness
    resource: /modding/offline-harness.md
    title: Offline harness experiments E1–E12
generated: { by: claude-code/claude-fable-5-2, at: 2026-11-25T21:52:36Z }
status: stable
ksa_build: "2026.11.25.5949"
---

# Purpose

`services.json` is the table of **services** — the numbered requests a kVM program issues with the
`SYSCALL id` instruction. Each row gives the id, the machine-level argument and result cells, the
valid-value domain of every argument cell, a cost class, the stock block the service needs, the kC
and kScript spellings, and prose for the in-game reference. The loader turns the non-reserved rows
into `GameTables.Services` (`Dictionary<ushort, ServiceInfo>`).[^game-tables]

The mechanics of issuing and applying a request are in
[/vm/services-syscall-abi.md](/vm/services-syscall-abi.md),
[/host/service-request-handling.md](/host/service-request-handling.md) and
[/integration/kfs-services-game-side.md](/integration/kfs-services-game-side.md); the loader is in
[game-tables-loader](/tables/game-tables-loader.md).

# File header

| key | value |
| --- | --- |
| `version` | `1` — "the service_table_version recorded in images" |
| `services` | 21 rows: 20 services and 1 reserved id |

`source`, verbatim (services.json:3):[^services-json]

> kFS_flight_interface_spec.md §9.1 (moved 2026-11-02, D-052); format kVM_spec.md §4.8. 'version' is
> the service_table_version recorded in images. 'arg_types'/'result_types' are the machine form (cell
> by cell, deepest first; 'cell' is one cell whose type depends on another argument, D-116);
> 'args'/'results' the spec's prose, which the renderer prints. 'arg_domains' gives each argument
> cell's valid values in kVM §4.8's domain vocabulary, checked by the VM at issue (D-116 (5), (11));
> 'host_checks' the values only the host can reject, after the latch (FI §9.3). 'summary' is one
> plain-text line, for the in-game reference and the quick start guide. A reserved row has no machine
> fields

# Schema

## `ServicesFile`

`public sealed record ServicesFile(int Version, string Source, List<ServiceRow> Services)` — JSON keys
`version`, `source`, `services`; all required.[^service-records]

## `ServiceRow`

`public sealed record ServiceRow(int Id, string Name, string Args, bool Reserved = false, string? Kc = null, string? Kscript = null, string? Symbol = null, string? Kind = null, List<string>? ArgTypes = null, string? Results = null, List<string>? ResultTypes = null, string? CostClass = null, bool PerValue = false, string? RequiresStock = null, List<string>? ArgDomains = null, string? HostChecks = null, string? Summary = null)`

"JSON-required" below is what the deserialiser enforces (constructor parameters without a default,
DataJsonContext.cs:8009–8166); "loader-required" is what `BuildServices` additionally demands of a
non-reserved row.[^data-json-context][^game-tables]

| property | JSON key | type | JSON-required | loader-required (non-reserved) | meaning |
| --- | --- | --- | --- | --- | --- |
| `Id` | `id` | `int` | yes | 0–65535, unique among non-reserved rows | service id = the `SYSCALL` immediate (u16) |
| `Name` | `name` | `string` | yes | — (used in messages) | spec name, e.g. `hold_direction`; shown in console events and the profiler |
| `Args` | `args` | `string` | yes | — | the spec's prose for the arguments (Markdown); not consumed by code in this build |
| `Reserved` | `reserved` | `bool` | no (default `false`) | — | `true`: the id is reserved; the row must have no `kind`, `arg_types` or `cost_class` and is not added to `Services` |
| `Kc` | `kc` | `string?` | no | no | kC spelling in backticks, e.g. `` `kfs_hold_direction(dir, roll)` ``. Drives the kC binding and the derived argument names |
| `Kscript` | `kscript` | `string?` | no | no | kScript spelling. `Not active in this build:` no code reads it |
| `Symbol` | `symbol` | `string?` | no | **yes** | kASM constant name, e.g. `SVC_HOLD_DIRECTION` (defined in `<kfs.kinc>`) |
| `Kind` | `kind` | `string?` | no | **yes**: `query`, `request` or `request+result` | see [`kind`](#kind) |
| `ArgTypes` | `arg_types` | `List<string>?` | no | **yes** (may be `[]`) | argument cells, "cell by cell, deepest first" |
| `Results` | `results` | `string?` | no | no | spec prose for the results; shown in the reference window when `result_types` is non-empty |
| `ResultTypes` | `result_types` | `List<string>?` | no | **yes** (may be `[]`) | result cells pushed after the call |
| `CostClass` | `cost_class` | `string?` | no | **yes**, a `costs.json` `service_classes` class | `Light`, `Medium` or `Heavy` |
| `PerValue` | `per_value` | `bool` | no (default `false`) | — | adds `log_cycles_per_value` × (most values in any format string) to the cost |
| `RequiresStock` | `requires_stock` | `string?` | no | no | stock block that services the request: `GUIDANCE`, `ATTITUDE_CONTROL` (the code also recognises `NAVIGATION`), or `null` |
| `ArgDomains` | `arg_domains` | `List<string>?` | no | **yes**, same length as `arg_types` | per-cell domain; see [vocabulary](#arg_domains-vocabulary) |
| `HostChecks` | `host_checks` | `string?` | no | **yes** (non-null; `"—"` when none) | prose: what only the host can reject. Not consumed by code beyond the null check |
| `Summary` | `summary` | `string?` | no | **yes**, non-blank | one line for the in-game reference |

Because `UnmappedMemberHandling` is `Disallow`, a row may not carry any other key.

## `ServiceInfo`

The derived, per-service record stored in `GameTables.Services`:[^service-records]

`public sealed record ServiceInfo(ushort Id, string Name, string Kind, string[] ArgTypes, int ArgCells, int ResultCells, string CostClass, bool PerValue, string? RequiresStock, string[]? Domains = null, int[]? ArgumentOf = null, string[]? ArgumentNames = null, string Summary = "")`

| member | derived from | notes |
| --- | --- | --- |
| `Id` | `(ushort)row.Id` | dictionary key |
| `Name`, `Kind`, `CostClass`, `PerValue`, `RequiresStock`, `Summary` | the row | copied |
| `ArgTypes` | `row.ArgTypes.ToArray()` | |
| `ArgCells` | `Σ CellsOf(arg_types[i])` | cells the VM pops |
| `ResultCells` | `Σ CellsOf(result_types[i])` | cells the VM pushes |
| `Domains` | `row.ArgDomains.ToArray()` | one per `arg_types` entry |
| `ArgumentOf` | `Arguments(row).ArgOf` | for each `arg_types` entry, the index of the *language-level* argument it belongs to |
| `ArgumentNames` | `Arguments(row).Names` | names from the `kc` column's parentheses, or `arg0`, `arg1`, … |
| `int Argument(int entry)` | method | `ArgumentOf[entry]`, or `entry` when out of range / null |

The row's `Symbol`, `Kc`, `Kscript`, `Args`, `Results`, `ResultTypes` and `HostChecks` are **not**
copied into `ServiceInfo`; consumers that need them read `ServicesData.Services` again.

### Argument grouping (`Arguments`, GameTables.cs:584–639)

The names are the comma-separated tokens between the first `(` and the following `)` of `kc`, each
trimmed and with leading `&` removed. If there are as many names as `arg_types` entries, or no names
at all, the mapping is the identity. Otherwise adjacent entries are folded into one argument while
their `arg_domains` strings are **equal** and surplus entries remain; the result must come out to
exactly the number of names, or the load fails with
`services.json: service '{name}': the kC column's {n} arguments don't group its {m} arg_types entries (D-118 (4))`.
This is how `kfs_translate(dv)` maps one `vec3` name onto three `finite` cells and
`kfs_hold_quat(q, roll)` maps `q` onto four `quat` cells.[^game-tables] The grouping is used to
report a rejected argument by its source-level position (`ServiceRejection`, console text
`rejected {name} argument {n} = …`).[^kvm-machine][^host-computer]

## `ServiceClassRow` (in `costs.json`)

`public sealed record ServiceClassRow(string Class, int K8, int K32, int K32F, int K32D)` with
`[JsonPropertyName("K8")]` etc., and `int For(string core)`. `cost_class` must name one of these
rows.[^service-records][^costs-json]

| `class` | K8 | K32 | K32F | K32D |
| --- | --- | --- | --- | --- |
| `Light` | 20 | 10 | 10 | 10 |
| `Medium` | 60 | 30 | 30 | 30 |
| `Heavy` | 200 | 100 | 100 | 100 |

`costs.json` also has `"log_cycles_per_value": 10`.

# Vocabulary

## `kind`

| value | loader | VM | used in this build |
| --- | --- | --- | --- |
| `request` | accepted | wired: arguments popped, request queued for the host | 18 services |
| `request+result` | accepted | wired: additionally `SyscallHost.IssueResult(id, args, results)` fills `ResultCells` cells that are pushed immediately | `plan_circularize`, `plan_burn` |
| `query` | accepted | `Not active in this build:` `KvmMachine.IsWiredService` returns `false` for any kind other than `request` / `request+result`, so a `SYSCALL` to a `query` service traps `BAD_SYSCALL` | none |

`IsWiredService` is the gate at the top of the interpreter's `SYSCALL` case
(KvmMachine.cs:1122–1129, 2248–2258).[^kvm-machine]

## Cell types (`arg_types`, `result_types`)

`GameTables.CellsOf` (GameTables.cs:475–536): `i32`, `u32`, `f32`, `ptr`, `cell` are one 32-bit cell;
`i64`, `f64` are two cells (low cell first — "`i64 t_us` (low, high)"). Anything else throws
`services.json: unknown cell type '{t}'`. `cell` is "one cell whose type depends on another argument"
(the event parameter of `sleep_until_event`). Types present in the data: `u32` ×10, `f32` ×14,
`i64` ×2, `ptr` ×1, `cell` ×1; results: `u32` ×2.[^services-json]

## `arg_domains` vocabulary

`CheckDomains` (GameTables.cs:641–690) accepts exactly these domain strings, each only on the cell
type shown; `ArgDomains.Check` (ArgDomains.cs:53–187) enforces them when the VM executes the
`SYSCALL`.[^game-tables][^arg-domains]

| domain | required cell type | runtime check (rejection reason) | in data |
| --- | --- | --- | --- |
| `any` | any | none | `trace` value |
| `finite` | `f32` | `not finite` | `translate`, `hold_heading` heading |
| `finite:>=0` | `f32` | `not finite`, `below 0` | — (unused) |
| `finite:[lo,hi]` | `f32`; `lo`, `hi` invariant floats with `lo <= hi` | `not finite`, `outside [lo, hi]` | `finite:[0,1]`, `finite:[-1.5707964,1.5707964]` |
| `vec_nonzero` | `f32` | `not finite`; on the first cell of a run of three, `all zero` when all three are 0 | `plan_burn` dv |
| `quat` | `f32` | `not finite`; on the first cell of a run of four, `norm not within 1e-3 of 1` | `hold_quat` q |
| `enum:name` | `u32`; `name` an `enums.json` key, or the special `roll_mode` | value outside `0 … count-1` → `not in name` | `enum:roll_mode`, `enum:attitude_profile`, `enum:apsis` |
| `enum:name[a..b]` | `u32`; `0 <= a <= b < count` | value outside `a … b` → `not in name[a..b]` | `enum:direction[2..21]` |
| `format` | `u32` | none in `ArgDomains` (the interpreter traps `BAD_SYSCALL` when service 1's format index is not in the image) | `log` fmt |
| `channel` | `u32` | `not a declared channel` (string index out of range or not kind 2) | `trace` channel |
| `handle` | `u32` | `handle 0` | `burn_start` |
| `event` | `u32` | `not an event sleep_until_event may name` (id not in `events.json`, or `TIME`) | `sleep_until_event` |
| `event_param` | `cell` | per event, using the **previous** cell as the event id; see [events-table](/tables/events-table.md) | `sleep_until_event` |
| `future` | `i64` | `not in the future` (value `<= met_us`) | `sleep_until`, `plan_burn` t_us |
| `ptr` | `ptr` | none | `log` argp |
| `count` | `u32` | none | — (unused) |

`enum:roll_mode` is special-cased in `EnumDomain` (GameTables.cs:702–730): there is no `roll_mode`
key in `enums.json`; its size is the number of `KFS_ROLL_*` constants produced by
`CatalogueEnums.All`, i.e. 3 (`FREE`, `UP`, `DOWN`). See [enums-table](/tables/enums-table.md).

`ArgDomains` caches the parsed `enum:` and `finite:[…]` domains of all services when it is first
constructed for a machine; a domain string that is not in those caches falls through its `default`
branch and is **not checked**. A rejected request is not queued: `sys.rejected_requests` is
incremented, zeroes are pushed for the result cells, and the host logs
`rejected {name} argument {n} = {value}: {reason}`.[^arg-domains][^kvm-machine][^host-computer]

## `cost_class` and `per_value`

`CostModel.Cost` adds, for category `syscall`, the `syscall` base cycles plus
`ServiceClassCycles[CostClass]`, plus `LogCyclesPerValue × maxLogValues` when `PerValue`
(CostModel.cs:26–37).[^cost-model] Only `log` has `per_value: true`.

## `requires_stock`

`KcFrontEnd.ServiceOwnership` (KcFrontEnd.cs:204–239) raises diagnostic `KC 501` for a call to a
service with a non-null `requires_stock` when the program claims `ALLOCATION`
("steers the game's flight computer, which flies only in pilot mode … (kFS D-135)"), or when the
program itself publishes the block's message — the mapping is hard-coded as `GUIDANCE` →
`MSG_ATT_TARGET`, `ATTITUDE_CONTROL` → `MSG_WRENCH_CMD`, `NAVIGATION` → `MSG_NAV_STATE`.[^kc-front-end]
The reference window shows the column as mode `pilot` (non-null) or `any` (null)
(KfsReferenceWindow.cs:424).[^kfs-reference-window] At run time `VehicleComputer.ApplyRequest`
rejects ids 48–53, 55–57, 66 and 67 by a hard-coded `case` list (not by reading `RequiresStock`) when
the computer is in `ControlMode.FullControl`.[^vehicle-computer]
No other code reads the column, so kASM programs get no compile-time ownership check — only the
run-time rejection.

## `reserved`

A reserved row (`"reserved": true`) documents an id that must not be reused. It is listed in the
reference window as `(reserved)` and excluded from `Services`, `<kfs.kinc>` and the kC
bindings.[^kfs-reference-window][^builtin-includes][^kc-parser]

# The table

Machine columns, all 21 rows (`line` = line in services.json):[^services-json]

| id | hex | `name` | `symbol` | `kind` | `arg_types` | `arg_domains` | `result_types` | `cost_class` | `per_value` | `requires_stock` | ArgCells / ResultCells | line |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | 0x0001 | `log` | `SVC_LOG` | `request` | `u32`, `ptr` | `format`, `ptr` | (none) | `Medium` | true | null | 2 / 0 | 5 |
| 2 | 0x0002 | `trace` | `SVC_TRACE` | `request` | `u32`, `f32` | `channel`, `any` | (none) | `Light` | false | null | 2 / 0 | 6 |
| 3 | 0x0003 | `halt` | `SVC_HALT` | `request` | (none) | (none) | (none) | `Light` | false | null | 0 / 0 | 7 |
| 4 | 0x0004 | `event` | (reserved) | — | — | — | — | — | — | — | — | 8 |
| 16 | 0x0010 | `sleep_until` | `SVC_SLEEP_UNTIL` | `request` | `i64` | `future` | (none) | `Medium` | false | null | 2 / 0 | 9 |
| 17 | 0x0011 | `sleep_until_event` | `SVC_SLEEP_UNTIL_EVENT` | `request` | `u32`, `cell` | `event`, `event_param` | (none) | `Medium` | false | null | 2 / 0 | 10 |
| 32 | 0x0020 | `stage` | `SVC_STAGE` | `request` | (none) | (none) | (none) | `Light` | false | null | 0 / 0 | 11 |
| 48 | 0x0030 | `hold_direction` | `SVC_HOLD_DIRECTION` | `request` | `u32`, `u32` | `enum:direction[2..21]`, `enum:roll_mode` | (none) | `Light` | false | `GUIDANCE` | 2 / 0 | 12 |
| 49 | 0x0031 | `hold_rates` | `SVC_HOLD_RATES` | `request` | (none) | (none) | (none) | `Light` | false | `GUIDANCE` | 0 / 0 | 13 |
| 50 | 0x0032 | `release_attitude` | `SVC_RELEASE_ATTITUDE` | `request` | (none) | (none) | (none) | `Light` | false | `GUIDANCE` | 0 / 0 | 14 |
| 51 | 0x0033 | `set_attitude_profile` | `SVC_SET_ATTITUDE_PROFILE` | `request` | `u32` | `enum:attitude_profile` | (none) | `Light` | false | `ATTITUDE_CONTROL` | 1 / 0 | 15 |
| 52 | 0x0034 | `set_throttle` | `SVC_SET_THROTTLE` | `request` | `f32` | `finite:[0,1]` | (none) | `Light` | false | `GUIDANCE` | 1 / 0 | 16 |
| 53 | 0x0035 | `engines_on` | `SVC_ENGINES_ON` | `request` | (none) | (none) | (none) | `Light` | false | `GUIDANCE` | 0 / 0 | 17 |
| 54 | 0x0036 | `translate` | `SVC_TRANSLATE` | `request` | `f32`, `f32`, `f32` | `finite`, `finite`, `finite` | (none) | `Light` | false | `GUIDANCE` | 3 / 0 | 18 |
| 55 | 0x0037 | `hold_heading` | `SVC_HOLD_HEADING` | `request` | `f32`, `f32`, `u32` | `finite`, `finite:[-1.5707964,1.5707964]`, `enum:roll_mode` | (none) | `Light` | false | `GUIDANCE` | 3 / 0 | 19 |
| 56 | 0x0038 | `hold_quat` | `SVC_HOLD_QUAT` | `request` | `f32`, `f32`, `f32`, `f32`, `u32` | `quat`, `quat`, `quat`, `quat`, `enum:roll_mode` | (none) | `Light` | false | `GUIDANCE` | 5 / 0 | 20 |
| 57 | 0x0039 | `engines_off` | `SVC_ENGINES_OFF` | `request` | (none) | (none) | (none) | `Light` | false | `GUIDANCE` | 0 / 0 | 21 |
| 64 | 0x0040 | `plan_circularize` | `SVC_PLAN_CIRCULARIZE` | `request+result` | `u32` | `enum:apsis` | `u32` | `Heavy` | false | null | 1 / 1 | 22 |
| 65 | 0x0041 | `plan_burn` | `SVC_PLAN_BURN` | `request+result` | `f32`, `f32`, `f32`, `i64` | `vec_nonzero`, `vec_nonzero`, `vec_nonzero`, `future` | `u32` | `Heavy` | false | null | 5 / 1 | 23 |
| 66 | 0x0042 | `burn_start` | `SVC_BURN_START` | `request` | `u32` | `handle` | (none) | `Light` | false | `GUIDANCE` | 1 / 0 | 24 |
| 67 | 0x0043 | `burn_abort` | `SVC_BURN_ABORT` | `request` | (none) | (none) | (none) | `Light` | false | `GUIDANCE` | 0 / 0 | 25 |

Prose and language columns:

| id | `name` | `args` (spec prose) | `results` | `kc` | `kscript` | `summary` |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | `log` | `u32 fmt`, `u32 argp` (§11) | — | `kfs_logf(fmt, …)` | `log(f"…")` | Prints a formatted line to the console. kC's kfs_logf builds the argument buffer; in kASM, fmt is a .format index and argp the buffer's address. |
| 2 | `trace` | `u32 chan` (string-table index of the channel name), `f32 v` | — | `trace(channel, v)` | `trace(channel, v)` | Records a float on a named channel, for kfs trace and the oscilloscope. In kASM the channel is a .string index. |
| 3 | `halt` | — | — | `kfs_halt()` | `halt()` | Halts the computer, as a trap does but without an error. kfs start or kfs reset restarts it. |
| 4 | `event` | Reserved for a cFS-style `event(id, severity, fmt, …)` using the `log` buffer ABI (§11; O-002) | — | — | — | Reserved for an event log service. |
| 16 | `sleep_until` | `i64 t_us` (low, high) | — | `kfs_sleep_until(t_us)` | `sleep_until(t)` | Sleeps until sys.met_us reaches t_us. Not active yet: the request is logged and the computer keeps running. |
| 17 | `sleep_until_event` | `u32 event`, `cell param` (type per event, §10) | — | `kfs_sleep_until_event(e, p)` | `sleep_until_event(e, <keyword>=p)` (§10) | Sleeps until an event (apoapsis, periapsis, SOI change, ...). Not active yet: the request is logged and the computer keeps running. |
| 32 | `stage` | — | — | `kfs_stage()` | `stage()` | Activates the next stage, as the staging key does. |
| 48 | `hold_direction` | `u32 dir` (§9.2), `u32 roll_mode` (§8.3) | — | `kfs_hold_direction(dir, roll)` | `hold_attitude(dir, roll=…)` | Points the vehicle along a named direction (prograde, radial out, toward the target, ...) and holds it there. |
| 49 | `hold_rates` | — | — | `kfs_hold_rates()` | `hold_rates()` | Rate hold: stops the vehicle rotating. |
| 50 | `release_attitude` | — | — | `kfs_release_attitude()` | `release_attitude()` | Releases attitude control back to manual. |
| 51 | `set_attitude_profile` | `u32 profile` (`STRICT`, `BALANCED`, `RELAXED`) | — | `kfs_set_attitude_profile(p)` | `set_attitude_profile(p)` | Sets how tightly attitude is held against how much RCS it uses: strict, balanced or relaxed. |
| 52 | `set_throttle` | `f32 throttle` | — | `kfs_set_throttle(x)` | `set_throttle(x)` | Sets the main-engine throttle, 0 to 1, until the pilot moves the throttle. |
| 53 | `engines_on` | — | — | `kfs_engines_on()` | `engines_on()` | Turns the engine switch on. The engines fire only with the switch on and a throttle above zero. |
| 54 | `translate` | `f32 dv.x`, `f32 dv.y`, `f32 dv.z` (`vec3` in `HELM`) | — | `kfs_translate(dv)` | `translate(dv)` | RCS translation by dv m/s along the control axes. Always rejected for now: the game has no automatic translation yet. |
| 55 | `hold_heading` | `f32 heading_rad`, `f32 pitch_rad` (both in `ENU`), `u32 roll_mode` (§8.3) | — | `kfs_hold_heading(heading_rad, pitch_rad, roll)` | `hold_attitude(heading(compass, pitch), roll=…)` | Holds a compass heading and a pitch above the horizon, in the local east-north-up frame. Pitch within +-90 degrees. |
| 56 | `hold_quat` | `quat q` (4 × `f32`, in `quat` field order; target attitude of the control point, as `q_target_helm_to_cci` in §8.3), `u32 roll_mode` (§8.3) | — | `kfs_hold_quat(q, roll)` | `hold_attitude(q, roll=…)`, `q: Quat[HELM, CCI]` | Holds an exact attitude: the control point's rotation into the inertial frame (CCI). |
| 57 | `engines_off` | — | — | `kfs_engines_off()` | `engines_off()` | Turns the engine switch off. |
| 64 | `plan_circularize` | `u32 at` (`APOAPSIS` or `PERIAPSIS`) | `u32 burn` | `kfs_plan_circularize(at)` | `plan_circularize(at=…)` | Plans a circularisation burn at apoapsis or periapsis and returns its handle. The plan fails (state ABORTED) if that apsis isn't ahead. |
| 65 | `plan_burn` | `f32 dv_vlf.x`, `f32 dv_vlf.y`, `f32 dv_vlf.z` (`vec3` in `VLF`), `i64 t_us` (low, high) | `u32 burn` | `kfs_plan_burn(dv, t_us)` | `plan_burn(dv, at)` | Plans a burn of dv m/s in the VLF frame (prograde, normal, radial) at mission time t_us and returns its handle. |
| 66 | `burn_start` | `u32 burn` | — | `kfs_burn_start(b)` | `execute_burn(b)` (requests, then suspends until done) | Executes a planned burn: the flight computer aligns, then ignites at the planned time. A handle whose plan isn't ready yet is held until it is. |
| 67 | `burn_abort` | — | — | `kfs_burn_abort()` | `abort_burn()` | Aborts the current burn. |

Host checks and derived argument grouping:

| id | `name` | `host_checks` | derived `ArgumentNames` ← `arg_types` entries (`ArgumentOf`) |
| --- | --- | --- | --- |
| 1 | `log` | — | fmt ← entries 0; … ← entries 1 |
| 2 | `trace` | — | channel ← entries 0; v ← entries 1 |
| 3 | `halt` | — | (no arguments) |
| 16 | `sleep_until` | — | t_us ← entries 0 |
| 17 | `sleep_until_event` | A `BURN_START` handle the host never issued (§9.1 burn handles) | e ← entries 0; p ← entries 1 |
| 32 | `stage` | — | (no arguments) |
| 48 | `hold_direction` | — | dir ← entries 0; roll ← entries 1 |
| 49 | `hold_rates` | — | (no arguments) |
| 50 | `release_attitude` | — | (no arguments) |
| 51 | `set_attitude_profile` | — | p ← entries 0 |
| 52 | `set_throttle` | — | x ← entries 0 |
| 53 | `engines_on` | — | (no arguments) |
| 54 | `translate` | Every request: `translate` is rejected with an event and counted until stock allocation has automatic translation (integration §8.4; D-117 (2)) | dv ← entries 0–2 |
| 55 | `hold_heading` | — | heading_rad ← entries 0; pitch_rad ← entries 1; roll ← entries 2 |
| 56 | `hold_quat` | — | q ← entries 0–3; roll ← entries 4 |
| 57 | `engines_off` | — | (no arguments) |
| 64 | `plan_circularize` | Planning fails (no such apsis ahead on the current orbit): the handle ends `ABORTED` with an event | at ← entries 0 |
| 65 | `plan_burn` | Planning fails (the time has passed when the plan is built, or no orbit patch holds it): the handle ends `ABORTED` with an event | dv ← entries 0–2; t_us ← entries 3 |
| 66 | `burn_start` | A handle never issued; one whose plan failed or whose burn has ended; one whose burn is not the vehicle's next executable burn (integration §8.4) | b ← entries 0 |
| 67 | `burn_abort` | — | (no arguments) |

# Id space

| ids (decimal) | hex | theme | used |
| --- | --- | --- | --- |
| 0 | `0x0000` | — | unused |
| 1–4 | `0x0001`–`0x0004` | diagnostics and control of the computer itself | 1 `log`, 2 `trace`, 3 `halt`, 4 reserved (`event`) |
| 5–15 | `0x0005`–`0x000F` | — | gap |
| 16–17 | `0x0010`–`0x0011` | sleeping | 16 `sleep_until`, 17 `sleep_until_event` |
| 18–31 | `0x0012`–`0x001F` | — | gap |
| 32 | `0x0020` | vehicle actions | 32 `stage` |
| 33–47 | `0x0021`–`0x002F` | — | gap |
| 48–57 | `0x0030`–`0x0039` | attitude, throttle, engines, translation (stock guidance) | all ten used |
| 58–63 | `0x003A`–`0x003F` | — | gap |
| 64–67 | `0x0040`–`0x0043` | burn planning and execution | all four used |
| 68–65535 | `0x0044`–`0xFFFF` | — | unused |

`Inferred:` the ids are allocated in blocks of sixteen by theme (`0x00`, `0x10`, `0x20`, `0x30`,
`0x40`); the table itself states no allocation rule, and the loader only requires 0–65535 and
uniqueness. `traps.json` mentions "a kernel service (D-109)" with "a gimbal index outside the
vehicle's layout or a count n above 1,024" — no such service exists in this table
(`Not active in this build:`), which suggests further ids are planned by the game's authors; a mod
should therefore pick ids far from the stock blocks (for example a high block such as `0x7Fxx`) to
avoid colliding with future stock services.

`KSA.Kfs.Vm.ServiceIds` repeats every non-reserved id as a `public const ushort SVC_*` plus
`TABLE_VERSION = 1`.[^service-ids]

# Where each column goes

| consumer | columns read | effect |
| --- | --- | --- |
| `KvmMachine` `SYSCALL` (KvmMachine.cs:1122–1194) | `Kind`, `ArgCells`, `ResultCells`, `Name`, `Domains`, `ArgTypes` | pops/pushes cells, validates domains, queues a `ServiceRequest(id, args, bytes, task, results)`; at most 256 requests per tick[^kvm-machine] |
| `CostModel` | `CostClass`, `PerValue` | cycle cost of each `SYSCALL`[^cost-model] |
| `ImageReader` (ImageReader.cs:556–559) | key set of `Services` | `SYSCALL at 0x…: unknown service id 0x….` load failure |
| `ProgramAnalysis`, `IrValidator`, `BusAnalysis` | `ArgCells`, `ResultCells` | stack-height analysis; "SYSCALL … is not a service in the table" |
| `Parser.Bindings` (Parser.cs:3085–3103) | `Kc`, `Reserved`, `Id` | maps the function name before `(` in `kc` to the `ServiceInfo`; a call to a *builtin* function with that name becomes an `NK.Syscall` node[^kc-parser] |
| `SystemHeaders.Files["kfs.h"]`, `SystemHeaders.Builtins` (SystemHeaders.cs:20–30) | **nothing** — hard-coded | the C prototypes (`void kfs_stage(void);` …) and the set of builtin names are literals, not generated from this table[^system-headers] |
| `BuiltinIncludes` `kfs.kinc` (BuiltinIncludes.cs:43–46) | `Symbol`, `Id`, `Reserved` | `.equ SVC_* = id` constants for kASM[^builtin-includes] |
| `KfsReferenceWindow.BuildServices` | `Id`, `Kc`, `Symbol`, `Results`, `ResultTypes`, `Summary`, `Reserved`, `CostClass`, `PerValue`, `RequiresStock`, `ArgTypes` | the in-game "kFS Services" page[^kfs-reference-window] |
| `HostComputer.ApplyHostRequest` (HostComputer.cs:747–811) | hard-coded ids 1, 2, 3, 16, 17 | log, trace, halt, and the two sleep requests (logged only: "sleeping and waking are MS7; the computer keeps running")[^host-computer] |
| `VehicleComputer.ApplyRequest` (VehicleComputer.cs:185–236) | hard-coded ids 54; 64, 65; 32; 48–53, 55–57, 66, 67 | rejects `translate`, tracks burn handles, counts stages, forwards the rest; any other id returns `false`[^vehicle-computer] |
| `KfsServices.Apply` (KfsServices.cs:310–448) | hard-coded `switch (r.Id)`: 48, 55, 56, 49, 50, 51, 52, 53, 57, 32, 64, 65, 66, 67 — no `default` | drives the game's `FlightComputer`[^kfs-services] |

A request whose id neither `ApplyHostRequest` nor `ApplyRequest` handles is rejected with
`rejected {name}: no vehicle to act on` (HostComputer.cs:670–676).

# Modding notes

- **A new row is enough for the toolchain, not for behaviour.** With an added non-reserved row
  (kind `request`), the assembler accepts `SYSCALL SVC_X` (through `<kfs.kinc>`), `ImageReader`
  accepts the image, the VM pops the cells, checks domains, charges cycles and queues the request,
  the profiler and console name it, and the reference window lists it — all table-driven. The host
  then rejects it as `no vehicle to act on` until a patch handles the id: the hook points are
  `HostComputer.ApplyHostRequest` (private, for services that need no vehicle),
  `VehicleComputer.ApplyRequest` (protected override, per-tick routing) and `KfsServices`, whose
  private `Apply` runs on the dispatch (solver/worker) thread and defers game writes to
  `MainThread()` on the main thread (see
  [/integration/kfs-services-game-side.md](/integration/kfs-services-game-side.md)). The tested
  recipe — a postfix on `VehicleComputer.ApplyRequest` — is in
  [adding a service](/modding/adding-a-service.md).
- **kC needs two more things** besides the `kc` column: a prototype in a *system* header and the
  function name in `SystemHeaders.Builtins` — the parser only marks a function builtin when it is
  declared in a system header and its name is in that set (Parser.cs:1585–1588, KcFrontEnd.cs:143),
  and only builtins reach the binding lookup. Both `SystemHeaders.Files` (`Dictionary<string, string>`)
  and `SystemHeaders.Builtins` (`HashSet<string>`) are `public static readonly` **mutable**
  collections, so a mod can add a header such as `<purrtty.h>` and its function names without
  patching. `Parser._bindings` is a static cache keyed by the `GameTables` reference
  (see [game-tables-loader](/tables/game-tables-loader.md#consumers-of-gametables)).
- **Results**: a `request+result` service gets its result cells from `KvmMachine.SyscallHost`
  (`ISyscallHost.IssueResult`), which `VehicleComputer` implements only for ids 64 and 65; for any
  other id the cells stay 0 unless that method is patched. The result is produced *synchronously
  at issue*, before the host applies the request, so it can only be a handle or a value already
  known to the host — not the outcome of the action. `Verified:` a one-cell `u32` or `f32` result
  on an added row reaches a kC program, but a multi-cell (struct) result makes the kC lowering
  throw `InvalidOperationException` instead of reporting a diagnostic — keep `result_types` to one
  cell.[^offline-harness]
- **Reading game state back** (e.g. "is light 3 on?") does not fit the `request` kind, and `query`
  is not wired. Stock state flows to programs through host-published messages
  ([messages-table](/tables/messages-table.md)); vehicles have no sensor block — only the bench
  layout has sensor fields ([io-layout-tables](/tables/io-layout-tables.md)). A one-cell
  `request+result` service answered by a postfix on `IssueResult` does return such a value
  synchronously — see [adding a service](/modding/adding-a-service.md).
- **Domains are the only table-driven argument validation.** Use `enum:<key>[a..b]` with a new
  `enums.json` key for enumerated arguments, `finite:[lo,hi]` for ranges, `any` for raw cells. There
  is no integer-range domain: a `u32` index can only be `any`, `handle` (non-zero), `count`
  (unchecked) or an enum, so index bounds must be checked by the handler ("host checks").
- **Limits**: at most 256 service requests per tick per computer (extra ones are dropped and
  counted); every `SYSCALL` costs `syscall` + class cycles against the tick budget
  ([/vm/cost-model-and-budgets.md](/vm/cost-model-and-budgets.md)).
- `ServiceInfo` and `ServiceRow` are public records with public constructors; `GameTables.Services`
  is a public mutable `Dictionary<ushort, ServiceInfo>`.

[^services-json]: services.json:2–3 (header), 5–25 (rows; per-row line numbers are in the table).
[^costs-json]: costs.json:88–93 (`service_classes`, `log_cycles_per_value`).
[^service-records]: ServicesFile.cs:5; ServiceRow.cs:5; ServiceInfo.cs:3–14; ServiceClassRow.cs:6–19.
[^game-tables]: GameTables.cs:67, 475–536 (`CellsOf`), 538–582 (`BuildServices`), 584–639 (`Arguments`), 641–690 (`CheckDomains`), 692–730 (`FiniteRange`, `EnumDomain`).
[^data-json-context]: DataJsonContext.cs:8009–8166 (`ServiceRowCtorParamInit`).
[^arg-domains]: ArgDomains.cs:22–51 (caches), 53–187 (`Check`), 194–248 (`IsSleepEvent`, `EventParam`).
[^kvm-machine]: KvmMachine.cs:1122–1194 (`SYSCALL`), 2248–2258 (`IsWiredService`).
[^cost-model]: CostModel.cs:8–41.
[^host-computer]: HostComputer.cs:670–691 (`ISyscallSink`), 693–735 (`ArgumentText`), 737–745 (`RejectByHost`), 747–816 (`ApplyHostRequest`, `ApplyRequest`).
[^vehicle-computer]: VehicleComputer.cs:174–183 (`IssueResult`), 185–236 (`ApplyRequest`).
[^kfs-services]: KfsServices.cs:305, 310–448, 594.
[^kc-parser]: Parser.cs:141–143, 1585–1588, 3085–3103, 3105–3206, 3414–3423.
[^kc-front-end]: KcFrontEnd.cs:143–146, 204–239.
[^system-headers]: SystemHeaders.cs:8–41.
[^builtin-includes]: BuiltinIncludes.cs:33–63.
[^kfs-reference-window]: KfsReferenceWindow.cs:373–430.
[^service-ids]: KSA.Kfs.Vm/ServiceIds.cs:3.
[^offline-harness]: Experiments E5, E6 and E8 in [the offline harness](/modding/offline-harness.md).
