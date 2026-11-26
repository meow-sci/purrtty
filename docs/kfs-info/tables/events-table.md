---
type: Data Table Reference
title: events.json — sleep events and wake reasons
description: Schema and complete contents of the embedded events table, and how its ids tie to the sleep_until_event service and the system block's wake_reason.
resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.events.json
tags: [kfs, tables, events, sleep, wake-reason]
sources:
  - id: events-json
    resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.events.json
    title: events.json (embedded resource)
  - id: event-records
    resource: ksa-decomp:KSA.Kfs.Vm.Tables/EventRow.cs
    title: EventsFile, EventRow (decompiled)
  - id: game-tables
    resource: ksa-decomp:KSA.Kfs.Vm.Tables/GameTables.cs
    title: GameTables (decompiled)
  - id: arg-domains
    resource: ksa-decomp:KSA.Kfs.Vm/ArgDomains.cs
    title: ArgDomains (decompiled)
  - id: kvm-machine
    resource: ksa-decomp:KSA.Kfs.Vm/KvmMachine.cs
    title: KvmMachine (decompiled)
  - id: host-computer
    resource: ksa-decomp:KSA.Kfs.Host/HostComputer.cs
    title: HostComputer (decompiled)
  - id: kc-parser
    resource: ksa-decomp:KSA.Kfs.KC/Parser.cs
    title: kC Parser (decompiled)
  - id: system-headers
    resource: ksa-decomp:KSA.Kfs.KC/SystemHeaders.cs
    title: SystemHeaders — hard-coded kfs.h (decompiled)
  - id: builtin-includes
    resource: ksa-decomp:KSA.Kfs.KAsm/BuiltinIncludes.cs
    title: BuiltinIncludes (decompiled)
  - id: services-json
    resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.services.json
    title: services.json (services 16 and 17)
  - id: sysblock-json
    resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.sysblock.json
    title: sysblock.json (wake_reason)
generated: { by: claude-code/claude-fable-5-2, at: 2026-11-25T21:52:36Z }
status: stable
ksa_build: "2026.11.25.5949"
---

# Purpose

`events.json` lists the events a program can name in `sleep_until_event` (service 17). The same ids
are the values of the system block's `wake_reason` word. In this build the table is consulted for
validation and messages only: **sleeping and waking are not implemented**, so no event ever fires.

# File header

| key | value |
| --- | --- |
| `version` | `1` (range-checked only; not recorded anywhere) |
| `events` | 8 rows |

`source`, verbatim (events.json:3):[^events-json]

> kFS_flight_interface_spec.md §10 (moved 2026-11-02, D-052). Ids are also the wake_reason values (P-018)

# Schema

- `public sealed record EventsFile(int Version, string Source, List<EventRow> Events)` — JSON
  `version`, `source`, `events`.
- `public sealed record EventRow(int Id, string Name, string Parameter, string ParamType, string Keyword)` —
  JSON `id`, `name`, `parameter`, `param_type`, `keyword`; **all five required** (no defaults).[^event-records]

| property | JSON key | type | meaning | read by |
| --- | --- | --- | --- | --- |
| `Id` | `id` | `int` | event id; also the `wake_reason` value | `ArgDomains`, `KvmMachine` (max id), `Parser`, `HostComputer` |
| `Name` | `name` | `string` | upper-case name; `TIME` is special-cased by name | `ArgDomains` (parameter rules by name), `Parser`, `HostComputer` |
| `Parameter` | `parameter` | `string` | prose description of the parameter | kC diagnostic text only |
| `ParamType` | `param_type` | `string` | `` `f32` ``, `` `u32` `` (backticks included in the JSON string), `0` (no parameter) or `—` | `Parser` (after `Trim('`')`) |
| `Keyword` | `keyword` | `string` | kScript keyword argument, e.g. `` `lead=` `` | `Not active in this build:` nothing reads it |

`GameTables.Build` validates nothing in this file beyond the version range: no uniqueness, ordering
or id-range check. There is no derived dictionary; every consumer scans `EventsData.Events`
linearly.[^game-tables]

# Contents

| `id` | `name` | `parameter` | `param_type` | `keyword` | kC constant (`kfs.h`) | line |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | `APOAPSIS` | Lead time, s | `f32` | `lead=` | `KFS_EVENT_APOAPSIS` | 5 |
| 2 | `PERIAPSIS` | Lead time, s | `f32` | `lead=` | `KFS_EVENT_PERIAPSIS` | 6 |
| 3 | `SOI_CHANGE` | — | 0 | — | `KFS_EVENT_SOI_CHANGE` | 7 |
| 4 | `ATMOSPHERE_ENTRY` | — | 0 | — | `KFS_EVENT_ATMOSPHERE_ENTRY` | 8 |
| 5 | `BURN_START` | Burn handle; wakes at the planned alignment start | `u32` | `burn=` | `KFS_EVENT_BURN_START` | 9 |
| 6 | `ALTITUDE_BELOW` | Altitude, m | `f32` | `altitude=` | `KFS_EVENT_ALTITUDE_BELOW` | 10 |
| 7 | `TIME` | `wake_reason` only: reported when a `sleep_until` (0x0010) expires. Not a valid `sleep_until_event` parameter | — | — | `KFS_EVENT_TIME` | 11 |
| 8 | `MESSAGE` | Message id: wakes when that message is next published by another computer or the host | `u32` | `msg=` | `KFS_EVENT_MESSAGE` | 12 |

The `kC constant` column is from the hard-coded `<kfs.h>` text (`#define KFS_EVENT_APOAPSIS 1` …
`#define KFS_EVENT_MESSAGE 8`), which is a literal in `SystemHeaders`, not generated from this
table.[^system-headers] `<kfs.kinc>` (kASM) defines **no** event constants — `BuiltinIncludes` adds
service symbols, `sys.*`, `TRAP_*` and the `CatalogueEnums` names only — so kASM code writes the
numeric id.[^builtin-includes]

# How events tie to `sleep_until_event`

Service 17 is `sleep_until_event` (`SVC_SLEEP_UNTIL_EVENT`, kC `kfs_sleep_until_event(e, p)`), with
`arg_types` `["u32", "cell"]` and `arg_domains` `["event", "event_param"]`; service 16 is
`sleep_until` (`i64 t_us`, domain `future`).[^services-json]

## Compile time (kC)

`Parser.ServiceCall` handles `kfs_sleep_until_event` specially (Parser.cs:3138–3192):[^kc-parser]

- the first argument must be an integer **constant** equal to some `EventRow.Id`, and not the row
  named `TIME`; otherwise diagnostic `KC 551` ("the event isn't a constant KFS_EVENT_* value" /
  "KFS_EVENT_TIME is not a sleep event");
- the second argument is typed by `param_type` with backticks trimmed: `f32` → any arithmetic value,
  cast to `float`; `u32` → an integer, cast to `uint32`; anything else → must be the integer constant
  `0` ("KFS_EVENT_{name} has no parameter; pass 0").

## Issue time (VM)

`ArgDomains.Check` (ArgDomains.cs:123–131, 194–248):[^arg-domains]

- domain `event`: the id must be in the table and its name must not be `TIME`, else the request is
  rejected with `not an event sleep_until_event may name`;
- domain `event_param`: the rule is chosen by the event's **name**, hard-coded:

| event name | parameter cell is | rejected when |
| --- | --- | --- |
| `APOAPSIS`, `PERIAPSIS` | `f32` lead time | `lead time not finite`; `lead time below 0` |
| `ALTITUDE_BELOW` | `f32` altitude | `altitude not finite` |
| `BURN_START` | `u32` burn handle | `handle 0` |
| `MESSAGE` | `u32` message id | `not a catalogue message` (id not in `messages.json`); `a message this computer publishes` |
| any other name (`SOI_CHANGE`, `ATMOSPHERE_ENTRY`, any added row) | must be 0 | `not 0 for an event without a parameter` |
| `TIME`, or an id not in the table | — | no parameter check (the `event` cell already failed) |

services.json adds a host check for `BURN_START`: "A `BURN_START` handle the host never issued
(§9.1 burn handles)".

## Host

`HostComputer.ApplyHostRequest` (HostComputer.cs:786–807):[^host-computer]

- service 16: `SleepRequest = (0, t_us)` and the console event
  `sleep until met_us {t} requested (sleeping and waking are MS7; the computer keeps running)`;
- service 17: `SleepRequest = (event, param)` and
  `sleep until event {name} requested (sleeping and waking are MS7; the computer keeps running)`,
  where `{name}` is looked up in this table (the number is printed when the id is unknown).

`Not active in this build:` nothing consumes `SleepRequest` to suspend the machine. The service
summaries say the same: "Not active yet: the request is logged and the computer keeps running."

# Wake reasons

`sysblock.json` defines `wake_reason` (uint32 at offset 32): "Event that last woke the machine, 0
after reset; values are the flight interface spec's event ids (`events.json`, FI §10; P-018)".[^sysblock-json]

- `KvmMachine` keeps `_wakeReason`, writes it at offset 32 whenever it refreshes the system block
  (KvmMachine.cs:479) and clears it on reset (line 1971).[^kvm-machine]
- `public void Wake(uint reason)` sets `_wakeReason` and clears `_sleeping` (lines 2212–2216);
  `Not active in this build:` it has no caller anywhere in the decompiled game.
- Restoring a saved machine state rejects a wake reason greater than
  `EventsData.Events.Max(e => e.Id)` (`phase, trap code, reset cause or wake reason unknown`, line
  2408) and rejects any sleeping state with `sleep is not implemented until MS7` (line 2434).
- `TIME` (id 7) is "`wake_reason` only: reported when a `sleep_until` (0x0010) expires".

So `wake_reason` always reads 0 in this build.

# Modding notes

- Events are the least useful extension channel in this build: nothing delivers them. A mod that
  wants "wake my program when X happens" would have to implement sleeping itself; a host-published
  message each tick is the stock mechanism that already works (vehicles have no sensor block), and
  for a mod a polled one-cell `request+result` service is the tested route — see
  [adding a service](/modding/adding-a-service.md).
- Adding a row is unvalidated and immediately visible (no cache), but: kC needs a
  `#define KFS_EVENT_X n` (not generated); a parameterised event is rejected at issue unless
  `ArgDomains.EventParam` (private, name-switched) is patched, because unknown names require the
  parameter cell to be 0; and raising the maximum id widens the accepted `wake_reason` range in
  state restore.
- `EventRow` is a public record with a public five-argument constructor; `EventsData.Events` is a
  mutable `List<EventRow>`.

[^events-json]: events.json:2–3 (header), 5–12 (rows).
[^event-records]: EventsFile.cs:5; EventRow.cs:3.
[^game-tables]: GameTables.cs:43, 101, 184 (the only references to `EventsData` in the loader).
[^arg-domains]: ArgDomains.cs:123–131 (`event`, `event_param` cases), 194–197 (`IsSleepEvent`), 199–248 (`EventParam`).
[^kvm-machine]: KvmMachine.cs:99, 479, 1971–1973, 2212–2216, 2408–2411, 2432–2435.
[^host-computer]: HostComputer.cs:215 (`SleepRequest`), 786–807.
[^kc-parser]: Parser.cs:3138–3192.
[^system-headers]: SystemHeaders.cs:20.
[^builtin-includes]: BuiltinIncludes.cs:35–63.
[^services-json]: services.json:9–10.
[^sysblock-json]: sysblock.json:14.
