---
type: Data Table Reference
title: enums.json and CatalogueEnums — enumerations and the generated KFS_* constants
description: Schema and full contents of the embedded enums table, and the exact CatalogueEnums rules that turn table text into the KFS_* constants of kfs.kinc, with every generated name and value.
resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.enums.json
tags: [kfs, tables, enums, constants, catalogue-enums, extension]
sources:
  - id: enums-json
    resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.enums.json
    title: enums.json (embedded resource)
  - id: enums-file
    resource: ksa-decomp:KSA.Kfs.Vm.Tables/EnumsFile.cs
    title: EnumsFile (decompiled)
  - id: catalogue-enums
    resource: ksa-decomp:KSA.Kfs.Vm.Tables/CatalogueEnums.cs
    title: CatalogueEnums (decompiled)
  - id: game-tables
    resource: ksa-decomp:KSA.Kfs.Vm.Tables/GameTables.cs
    title: GameTables (decompiled)
  - id: messages-json
    resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.messages.json
    title: messages.json (meaning text of mode, roll_mode, state)
  - id: builtin-includes
    resource: ksa-decomp:KSA.Kfs.KAsm/BuiltinIncludes.cs
    title: BuiltinIncludes (decompiled)
  - id: system-headers
    resource: ksa-decomp:KSA.Kfs.KC/SystemHeaders.cs
    title: SystemHeaders — hard-coded kfs.h (decompiled)
  - id: vm-enums
    resource: ksa-decomp:KSA.Kfs.Vm/Direction.cs
    title: C# enums mirroring enums.json in KSA.Kfs.Vm (decompiled)
  - id: kfs-services
    resource: ksa-decomp:KSA.Kfs/KfsServices.cs
    title: KfsServices (decompiled)
  - id: arg-domains
    resource: ksa-decomp:KSA.Kfs.Vm/ArgDomains.cs
    title: ArgDomains (decompiled)
generated: { by: claude-code/claude-fable-5-2, at: 2026-11-25T21:52:36Z }
status: stable
ksa_build: "2026.11.25.5949"
---

# Purpose

`enums.json` holds named enumerations as ordered string lists; a value's number is its index.
`CatalogueEnums` derives the language-visible `KFS_*` constants from four of those lists plus the
`` `N` `NAME` `` pairs embedded in three message fields' prose.

# File header

| key | value |
| --- | --- |
| `version` | `1` (range-checked only) |
| `enums` | 14 keys |

`source`, verbatim (enums.json:3):[^enums-json]

> kVM_spec.md §2.2 (enums numbered from 0 in table order), §7 (task kind); integration §2.6 (reset
> causes); tooling §8.1 (Debug-section enums). wake_reason uses events.json ids (P-018). The service
> argument enumerations (FI §9.2; D-116 (4)): 'direction' is numbered as KSA's
> FlightComputerAttitudeTrackTarget (0 none and 1 custom are not directions; each direction's name is
> its frame, then its name), 'attitude_profile' as KSA's FlightComputerAttitudeProfile, 'apsis' (no
> KSA enum) in the catalogue's listing order. 'burn_end_cause' is MSG_BURN_STATUS.end_cause (FI §8.6;
> D-117 (1))

# Schema

`public sealed record EnumsFile(int Version, string Source, Dictionary<string, List<string>> Enums)`
— JSON keys `version`, `source`, `enums`; `enums` is an object mapping an enum key to an array of
value names.[^enums-file] The dictionary uses the default (ordinal, case-sensitive) comparer and
preserves nothing but key → list; a value's numeric code is its list index, starting at 0.

# Contents

| enum key | count | values (index `name`) | line |
| --- | --- | --- | --- |
| `core_class` | 4 | 0 `K8`, 1 `K32`, 2 `K32F`, 3 `K32D` | 5 |
| `reset_cause` | 4 | 0 `PowerOn`, 1 `Player`, 2 `Watchdog`, 3 `Load` | 6 |
| `task_kind` | 3 | 0 `init`, 1 `periodic`, 2 `program` | 7 |
| `message_role_bits` | 2 | 0 `subscribed`, 1 `published` | 8 |
| `string_kind` | 3 | 0 `name`, 1 `format`, 2 `channel` | 9 |
| `debug_lang` | 4 | 0 `kASM`, 1 `kC`, 2 `kScript`, 3 `generated` | 10 |
| `debug_type_kind` | 12 | 0 `void`, 1 `bool`, 2 `int`, 3 `float`, 4 `pointer`, 5 `array`, 6 `struct`, 7 `enum`, 8 `vector`, 9 `matrix`, 10 `record`, 11 `function` | 11 |
| `debug_local_kind` | 3 | 0 `local`, 1 `argument`, 2 `temporary` | 12 |
| `scheduler_phase` | 4 | 0 `booting`, 1 `running`, 2 `idle`, 3 `halted` | 13 |
| `task_status` | 3 | 0 `ready`, 1 `suspended`, 2 `completed` | 14 |
| `direction` | 22 | 0 `none`, 1 `custom`, 2 `enu_forward`, 3 `enu_backward`, 4 `enu_up`, 5 `enu_down`, 6 `lvlh_ahead`, 7 `lvlh_behind`, 8 `lvlh_radial_out`, 9 `lvlh_radial_in`, 10 `vlf_prograde`, 11 `vlf_retrograde`, 12 `vlf_normal`, 13 `vlf_antinormal`, 14 `vlf_outward`, 15 `vlf_inward`, 16 `burn_plus_dv`, 17 `burn_minus_dv`, 18 `target_toward`, 19 `target_away`, 20 `target_antivelocity`, 21 `target_align` | 15 |
| `attitude_profile` | 3 | 0 `strict`, 1 `balanced`, 2 `relaxed` | 16 |
| `apsis` | 2 | 0 `apoapsis`, 1 `periapsis` | 17 |
| `burn_end_cause` | 8 | 0 `none`, 1 `completed`, 2 `propellant_out`, 3 `stage_ran_dry`, 4 `ignition_denied`, 5 `planning_failed`, 6 `superseded`, 7 `cancelled` | 18 |

## What reads each enum

`EnumsData.Enums` is referenced only inside the tables namespace:[^game-tables][^catalogue-enums]

| key | read by code | C# mirror (same order) |
| --- | --- | --- |
| `core_class` | `BuildCategories`: must equal `costs.json` `cores`; `BuildParts`: a part's `CoreClass` is the index of its `core` | `KSA.Kfs.Vm.CoreClass` |
| `direction` | `CatalogueEnums.ServiceGroups` (from index 2); `EnumDomain` for `enum:direction[2..21]` | `KSA.Kfs.Vm.Direction` |
| `attitude_profile` | `CatalogueEnums.ServiceGroups`; `EnumDomain` for `enum:attitude_profile` | `KSA.Kfs.Vm.AttitudeProfile` |
| `apsis` | `CatalogueEnums.ServiceGroups`; `EnumDomain` for `enum:apsis` | `KSA.Kfs.Vm.Apsis` |
| `burn_end_cause` | `CatalogueEnums.ServiceGroups` | `KSA.Kfs.Vm.BurnEndCause` |
| `reset_cause` | — | `KSA.Kfs.Vm.ResetCause` |
| `task_kind` | — | `KSA.Kfs.Vm.TaskKind` |
| `message_role_bits` | — | none; the image's message `Role` byte uses bit 0 (`& 1`) = subscribed, bit 1 (`& 2`) = published |
| `string_kind` | — | `KSA.Kfs.Vm.StringKind` |
| `debug_lang` | — | `KSA.Kfs.Vm.DebugLang` |
| `debug_type_kind` | — | `KSA.Kfs.Vm.DebugTypeKind` |
| `debug_local_kind` | — | `KSA.Kfs.Vm.DebugLocalKind` |
| `scheduler_phase` | — | `KSA.Kfs.Vm.SchedulerPhase` |
| `task_status` | — | `KSA.Kfs.Vm.TaskStatus` |

The nine keys marked "—" are documentation data in this build: the running code uses the C# enums,
whose members are in the same order (verified by reading each enum file).[^vm-enums]

`direction` and `attitude_profile` values are passed straight to the game: `KfsServices` casts the
service argument cell — `(FlightComputerAttitudeTrackTarget)args[0]` for `hold_direction`,
`(FlightComputerAttitudeProfile)args[0]` for `set_attitude_profile` (KfsServices.cs:318, 357) — so
their numbering must match those KSA enums.[^kfs-services]

# `CatalogueEnums`

`public static class CatalogueEnums` has one public member,
`static IReadOnlyList<(string Name, int Value)> All(GameTables tables)`, which builds a **new** list
on every call from two private static tables (CatalogueEnums.cs:12–64).[^catalogue-enums]

## Rule 1 — message-field groups (`Groups`)

```csharp
private static readonly (string Field, string Group)[] Groups =
{ ("mode", "ATT_MODE"), ("roll_mode", "ROLL"), ("state", "BURN") };
```

For each `(Field, Group)`, in this order:

1. Find the **first** `MessageFieldRow`, scanning `MessagesData.Messages` in file order and each
   message's `fields` in order, whose `names` list contains `Field`. If none:
   `GameTablesException("messages.json: no enumerated field '{Field}' (FI §8)")`.
2. Apply the generated regex `` `(\d+)`\s+`([A-Z_]+)` `` to that row's `meaning` text.
3. For every match emit `("KFS_" + Group + "_" + $2, int.Parse($1))`.

In this build the three rows are `MSG_ATT_TARGET.mode` (messages.json:36), `MSG_ATT_TARGET.roll_mode`
(messages.json:39) and `MSG_BURN_STATUS.state` (messages.json:72).[^messages-json] The pattern needs a
backticked number followed by whitespace and a backticked upper-case name; text such as
`` `0` ECL `` (no backticks around the name, used by `MSG_FC_SETTINGS.frame`) does not match.

## Rule 2 — table enums (`ServiceGroups`)

```csharp
private static readonly (string Enum, string Prefix, int From)[] ServiceGroups =
{ ("direction", "KFS_", 2), ("attitude_profile", "KFS_PROFILE_", 0),
  ("apsis", "KFS_AT_", 0), ("burn_end_cause", "KFS_BURN_END_", 0) };
```

For each `(Enum, Prefix, From)`: look `Enum` up in `EnumsData.Enums` — if absent,
`GameTablesException("enums.json: no enumeration '{Enum}' (FI §9.2)")` — then for every index
`i` from `From` to the end emit `(Prefix + value.ToUpperInvariant(), i)`. `direction` starts at 2
because "0 none and 1 custom are not directions"; the constant's value is still the list index, so
`KFS_ENU_FORWARD` is 2.

Results are appended in rule order (all `Groups`, then all `ServiceGroups`); duplicates are not
detected here.

## Every generated constant

| # | constant | value | produced by |
| --- | --- | --- | --- |
| 1 | `KFS_ATT_MODE_NONE` | 0 | `Groups` (`mode`, `ATT_MODE`): `MSG_ATT_TARGET.mode` meaning text |
| 2 | `KFS_ATT_MODE_RATE` | 1 | `Groups` (`mode`, `ATT_MODE`): `MSG_ATT_TARGET.mode` meaning text |
| 3 | `KFS_ATT_MODE_ATTITUDE` | 2 | `Groups` (`mode`, `ATT_MODE`): `MSG_ATT_TARGET.mode` meaning text |
| 4 | `KFS_ROLL_FREE` | 0 | `Groups` (`roll_mode`, `ROLL`): `MSG_ATT_TARGET.roll_mode` meaning text |
| 5 | `KFS_ROLL_UP` | 1 | `Groups` (`roll_mode`, `ROLL`): `MSG_ATT_TARGET.roll_mode` meaning text |
| 6 | `KFS_ROLL_DOWN` | 2 | `Groups` (`roll_mode`, `ROLL`): `MSG_ATT_TARGET.roll_mode` meaning text |
| 7 | `KFS_BURN_NONE` | 0 | `Groups` (`state`, `BURN`): `MSG_BURN_STATUS.state` meaning text |
| 8 | `KFS_BURN_PLANNED` | 1 | `Groups` (`state`, `BURN`): `MSG_BURN_STATUS.state` meaning text |
| 9 | `KFS_BURN_ALIGNING` | 2 | `Groups` (`state`, `BURN`): `MSG_BURN_STATUS.state` meaning text |
| 10 | `KFS_BURN_BURNING` | 3 | `Groups` (`state`, `BURN`): `MSG_BURN_STATUS.state` meaning text |
| 11 | `KFS_BURN_DONE` | 4 | `Groups` (`state`, `BURN`): `MSG_BURN_STATUS.state` meaning text |
| 12 | `KFS_BURN_ABORTED` | 5 | `Groups` (`state`, `BURN`): `MSG_BURN_STATUS.state` meaning text |
| 13 | `KFS_BURN_PENDING` | 6 | `Groups` (`state`, `BURN`): `MSG_BURN_STATUS.state` meaning text |
| 14 | `KFS_ENU_FORWARD` | 2 | `ServiceGroups` (`direction`, `KFS_`, from 2): enums.json `direction[2]` = `enu_forward` |
| 15 | `KFS_ENU_BACKWARD` | 3 | `ServiceGroups` (`direction`, `KFS_`, from 2): enums.json `direction[3]` = `enu_backward` |
| 16 | `KFS_ENU_UP` | 4 | `ServiceGroups` (`direction`, `KFS_`, from 2): enums.json `direction[4]` = `enu_up` |
| 17 | `KFS_ENU_DOWN` | 5 | `ServiceGroups` (`direction`, `KFS_`, from 2): enums.json `direction[5]` = `enu_down` |
| 18 | `KFS_LVLH_AHEAD` | 6 | `ServiceGroups` (`direction`, `KFS_`, from 2): enums.json `direction[6]` = `lvlh_ahead` |
| 19 | `KFS_LVLH_BEHIND` | 7 | `ServiceGroups` (`direction`, `KFS_`, from 2): enums.json `direction[7]` = `lvlh_behind` |
| 20 | `KFS_LVLH_RADIAL_OUT` | 8 | `ServiceGroups` (`direction`, `KFS_`, from 2): enums.json `direction[8]` = `lvlh_radial_out` |
| 21 | `KFS_LVLH_RADIAL_IN` | 9 | `ServiceGroups` (`direction`, `KFS_`, from 2): enums.json `direction[9]` = `lvlh_radial_in` |
| 22 | `KFS_VLF_PROGRADE` | 10 | `ServiceGroups` (`direction`, `KFS_`, from 2): enums.json `direction[10]` = `vlf_prograde` |
| 23 | `KFS_VLF_RETROGRADE` | 11 | `ServiceGroups` (`direction`, `KFS_`, from 2): enums.json `direction[11]` = `vlf_retrograde` |
| 24 | `KFS_VLF_NORMAL` | 12 | `ServiceGroups` (`direction`, `KFS_`, from 2): enums.json `direction[12]` = `vlf_normal` |
| 25 | `KFS_VLF_ANTINORMAL` | 13 | `ServiceGroups` (`direction`, `KFS_`, from 2): enums.json `direction[13]` = `vlf_antinormal` |
| 26 | `KFS_VLF_OUTWARD` | 14 | `ServiceGroups` (`direction`, `KFS_`, from 2): enums.json `direction[14]` = `vlf_outward` |
| 27 | `KFS_VLF_INWARD` | 15 | `ServiceGroups` (`direction`, `KFS_`, from 2): enums.json `direction[15]` = `vlf_inward` |
| 28 | `KFS_BURN_PLUS_DV` | 16 | `ServiceGroups` (`direction`, `KFS_`, from 2): enums.json `direction[16]` = `burn_plus_dv` |
| 29 | `KFS_BURN_MINUS_DV` | 17 | `ServiceGroups` (`direction`, `KFS_`, from 2): enums.json `direction[17]` = `burn_minus_dv` |
| 30 | `KFS_TARGET_TOWARD` | 18 | `ServiceGroups` (`direction`, `KFS_`, from 2): enums.json `direction[18]` = `target_toward` |
| 31 | `KFS_TARGET_AWAY` | 19 | `ServiceGroups` (`direction`, `KFS_`, from 2): enums.json `direction[19]` = `target_away` |
| 32 | `KFS_TARGET_ANTIVELOCITY` | 20 | `ServiceGroups` (`direction`, `KFS_`, from 2): enums.json `direction[20]` = `target_antivelocity` |
| 33 | `KFS_TARGET_ALIGN` | 21 | `ServiceGroups` (`direction`, `KFS_`, from 2): enums.json `direction[21]` = `target_align` |
| 34 | `KFS_PROFILE_STRICT` | 0 | `ServiceGroups` (`attitude_profile`, `KFS_PROFILE_`, from 0): enums.json `attitude_profile[0]` = `strict` |
| 35 | `KFS_PROFILE_BALANCED` | 1 | `ServiceGroups` (`attitude_profile`, `KFS_PROFILE_`, from 0): enums.json `attitude_profile[1]` = `balanced` |
| 36 | `KFS_PROFILE_RELAXED` | 2 | `ServiceGroups` (`attitude_profile`, `KFS_PROFILE_`, from 0): enums.json `attitude_profile[2]` = `relaxed` |
| 37 | `KFS_AT_APOAPSIS` | 0 | `ServiceGroups` (`apsis`, `KFS_AT_`, from 0): enums.json `apsis[0]` = `apoapsis` |
| 38 | `KFS_AT_PERIAPSIS` | 1 | `ServiceGroups` (`apsis`, `KFS_AT_`, from 0): enums.json `apsis[1]` = `periapsis` |
| 39 | `KFS_BURN_END_NONE` | 0 | `ServiceGroups` (`burn_end_cause`, `KFS_BURN_END_`, from 0): enums.json `burn_end_cause[0]` = `none` |
| 40 | `KFS_BURN_END_COMPLETED` | 1 | `ServiceGroups` (`burn_end_cause`, `KFS_BURN_END_`, from 0): enums.json `burn_end_cause[1]` = `completed` |
| 41 | `KFS_BURN_END_PROPELLANT_OUT` | 2 | `ServiceGroups` (`burn_end_cause`, `KFS_BURN_END_`, from 0): enums.json `burn_end_cause[2]` = `propellant_out` |
| 42 | `KFS_BURN_END_STAGE_RAN_DRY` | 3 | `ServiceGroups` (`burn_end_cause`, `KFS_BURN_END_`, from 0): enums.json `burn_end_cause[3]` = `stage_ran_dry` |
| 43 | `KFS_BURN_END_IGNITION_DENIED` | 4 | `ServiceGroups` (`burn_end_cause`, `KFS_BURN_END_`, from 0): enums.json `burn_end_cause[4]` = `ignition_denied` |
| 44 | `KFS_BURN_END_PLANNING_FAILED` | 5 | `ServiceGroups` (`burn_end_cause`, `KFS_BURN_END_`, from 0): enums.json `burn_end_cause[5]` = `planning_failed` |
| 45 | `KFS_BURN_END_SUPERSEDED` | 6 | `ServiceGroups` (`burn_end_cause`, `KFS_BURN_END_`, from 0): enums.json `burn_end_cause[6]` = `superseded` |
| 46 | `KFS_BURN_END_CANCELLED` | 7 | `ServiceGroups` (`burn_end_cause`, `KFS_BURN_END_`, from 0): enums.json `burn_end_cause[7]` = `cancelled` |

46 constants, all names distinct (reproduced by applying the two rules to the JSON; the list is not
read from a running game).

## Consumers

| consumer | use |
| --- | --- |
| `BuiltinIncludes` for `<kfs.kinc>` (BuiltinIncludes.cs:57–60) | `Constants[name] = value` for each pair — a later duplicate name would silently overwrite an earlier one. This is what makes the constants available to kASM[^builtin-includes] |
| `GameTables.EnumDomain` (GameTables.cs:708–711) | for the domain `enum:roll_mode`, the enum size is the count of generated names starting with `KFS_ROLL_` (3)[^game-tables] |

kC does **not** get its constants from `CatalogueEnums`: `<kfs.h>` is a string literal in
`SystemHeaders.Files["kfs.h"]` that contains the same 46 `#define`s, plus `KFS_CORE_K8` … `KFS_CORE_K32D`
(0–3) and `KFS_EVENT_APOAPSIS` … `KFS_EVENT_MESSAGE` (1–8), which `CatalogueEnums` does not
produce.[^system-headers] Consequently `<kfs.kinc>` has no `KFS_CORE_*` or `KFS_EVENT_*` names, and
the kC and kASM constant sets are kept in step by hand (by the game's authors), not by code.

The numbers in `<kfs.h>` and the generated list agree for all 46 names in this build.

# Enum domains for service arguments

`GameTables.EnumDomain("enum:<key>")` / `("enum:<key>[a..b]")` resolves `<key>` against
`EnumsData.Enums` (or the `roll_mode` special case) and returns `(key, first, last)`; the loader
rejects a service row whose enum domain does not resolve, and `ArgDomains` rejects out-of-range
values at issue with `not in <key>` / `not in <key>[a..b]`.[^game-tables][^arg-domains] See
[services-table](/tables/services-table.md#arg_domains-vocabulary).

# Modding notes

- **A new enum key is the cheap way to get a validated enumerated service argument.** Add
  `"light_mode": ["off", "on", "toggle"]` to the `enums` object and use `enum:light_mode` as an
  `arg_domains` entry: the loader validates it and the VM range-checks it with no further patch
  (provided the key exists before `Build()` runs — see
  [game-tables-loader](/tables/game-tables-loader.md#modding-notes)).
- **Constants are not generated for new keys.** `ServiceGroups` and `Groups` are private static
  readonly arrays with fixed length; a Harmony postfix on the public static `CatalogueEnums.All` that
  returns the original list plus extra `(name, value)` pairs makes them appear in `<kfs.kinc>`. Do not
  add names starting with `KFS_ROLL_` (they would change the `enum:roll_mode` range). For kC the
  `#define`s must be put in a header text (`SystemHeaders.Files` is a public mutable dictionary).
- **Never reorder or insert into existing lists**: values are positional and mirrored by C# enums and
  by KSA's own enums.
- `core_class` cannot be extended by data alone: `costs.cores` must match it and `CostRow` /
  `ServiceClassRow` only have `K8`, `K32`, `K32F`, `K32D` properties.

[^enums-json]: enums.json:2–3 (header), 5–18 (the fourteen lists).
[^enums-file]: EnumsFile.cs:5.
[^catalogue-enums]: CatalogueEnums.cs:12–17 (`Groups`), 19–25 (`ServiceGroups`), 27–32 (regex), 34–64 (`All`).
[^game-tables]: GameTables.cs:215 (`core_class` vs `cores`), 433–436 (part core index), 702–730 (`EnumDomain`).
[^messages-json]: messages.json:36 (`mode`), 39 (`roll_mode`), 72 (`state`).
[^builtin-includes]: BuiltinIncludes.cs:35–63.
[^system-headers]: SystemHeaders.cs:20.
[^vm-enums]: KSA.Kfs.Vm/CoreClass.cs, ResetCause.cs, TaskKind.cs, StringKind.cs, DebugLang.cs, DebugTypeKind.cs, DebugLocalKind.cs, SchedulerPhase.cs, TaskStatus.cs, Direction.cs, AttitudeProfile.cs, Apsis.cs, BurnEndCause.cs (each: line 3 onward).
[^kfs-services]: KfsServices.cs:318, 357.
[^arg-domains]: ArgDomains.cs:26–39 (enum range cache), 151–158 (range check).
