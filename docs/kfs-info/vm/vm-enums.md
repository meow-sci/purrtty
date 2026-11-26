---
type: API Reference
title: kVM enums (KSA.Kfs.Vm) with numeric values
description: Every enum declared in the KSA.Kfs.Vm namespace with its underlying type, members and numeric values, where each is used, and how it lines up with enums.json.
resource: ksa-decomp:KSA.Kfs.Vm/TrapCode.cs
tags: [kfs, vm, kvm, enums, api]
sources:
  - id: vm-enum-files
    resource: ksa-decomp:KSA.Kfs.Vm/CoreClass.cs
    title: Enum source files in KSA.Kfs.Vm (CoreClass, ResetCause, TaskKind, TaskStatus, Phase, SchedulerPhase, RunOutcome, StringKind, Direction, Apsis, AttitudeProfile, BurnEndCause, DebugLang, DebugTypeKind, DebugLocalKind, TrapCode, Op)
  - id: enums-json
    resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.enums.json
    title: enums.json (embedded)
  - id: kvm-machine
    resource: ksa-decomp:KSA.Kfs.Vm/KvmMachine.cs
    title: KvmMachine (decompiled)
  - id: image-reader
    resource: ksa-decomp:KSA.Kfs.Vm.Image/ImageReader.cs
    title: ImageReader (decompiled)
  - id: game-tables
    resource: ksa-decomp:KSA.Kfs.Vm.Tables/GameTables.cs
    title: GameTables (decompiled) — EnumDomain, core_class check
  - id: imm-type
    resource: ksa-decomp:KSA.Kfs.Vm.Tables/ImmType.cs
    title: ImmType (decompiled)
generated: { by: claude-code/claude-fable-5-2, at: 2026-11-25T21:46:43Z }
status: stable
ksa_build: "2026.11.25.5949"
---

# Summary

`KSA.Kfs.Vm` declares 17 enums. All use `byte` as the underlying type except `RunOutcome` (`int`); all are
`public` except `Phase` and `RunOutcome` (`internal`). Members are numbered from 0 in declaration order
unless a value is shown explicitly (`Op` only).[^vm-enum-files]

The C# enums are **hand-written parallels** of the lists in `enums.json`; they are not generated from the
JSON at run time. The JSON lists are what the tables code uses (argument-domain ranges, the core-class
consistency check, constants exported to programs); the C# enums are what the VM and host code use. The
header of `enums.json` says its enums are "numbered from 0 in table order".[^enums-json] Its content and
the constants derived from it are documented in [enums table](/tables/enums-table.md).

# Machine and scheduler

## `CoreClass : byte` — `enums.json` `core_class`

| Value | Member |
| --- | --- |
| 0 | `K8` |
| 1 | `K32` |
| 2 | `K32F` |
| 3 | `K32D` |

`PartDescription.CoreClass` is an `int` holding this value (the index of the part's core in the
`core_class` list); it is written to `sys.core` and compared with image header byte 36.
`costs.json` `cores` must equal the `core_class` list (`GameTables.cs:215-218`).[^game-tables]

## `ResetCause : byte` — `enums.json` `reset_cause`

| Value | Member | Used in this build |
| --- | --- | --- |
| 0 | `PowerOn` | Default of `HostComputer.Start`. |
| 1 | `Player` | `HostComputer.Reset()`. |
| 2 | `Watchdog` | `Not active in this build:` no caller passes it. |
| 3 | `Load` | `Not active in this build:` no caller passes it. |

Argument of `KvmMachine.Reset`; exposed at `sys.reset_cause`; stored in saved state (a value above 3 is
rejected).

## `TaskKind : byte` — `enums.json` `task_kind` (`init`, `periodic`, `program`)

| Value | Member |
| --- | --- |
| 0 | `Init` |
| 1 | `Periodic` |
| 2 | `Program` |

Byte 7 of each 40-byte task record in an image; a value above 2 fails loading (`ImageReader.cs:401-405`).[^image-reader]

## `TaskStatus : byte` — `enums.json` `task_status`

| Value | Member |
| --- | --- |
| 0 | `Ready` |
| 1 | `Suspended` |
| 2 | `Completed` |

`TaskState.Status` is this enum's `ToString()`.

## `Phase : byte` (internal) and `SchedulerPhase : byte` (public) — `enums.json` `scheduler_phase`

| Value | Member (both enums) |
| --- | --- |
| 0 | `Booting` |
| 1 | `Running` |
| 2 | `Idle` |
| 3 | `Halted` |

`KvmMachine._phase` is a `Phase`; it is saved as a byte in machine state. `SchedulerPhase` has identical
members and is not referenced by any decompiled code — the machine exposes its phase only through the
booleans `InBoot`, `IsIdle`, `IsHalted`.[^kvm-machine]

## `RunOutcome` (internal, `int`)

| Value | Member |
| --- | --- |
| 0 | `Completed` |
| 1 | `Suspended` |
| 2 | `Overrun` |
| 3 | `Trapped` |

Return value of the private `KvmMachine.RunTask`.

## `TrapCode : byte` — `traps.json`

| Value | Member | | Value | Member |
| --- | --- | --- | --- | --- |
| 0 | `None` | | 9 | `READ_ONLY` |
| 1 | `ILLEGAL_OPCODE` | | 10 | `MISALIGNED` |
| 2 | `OPERAND_OVERFLOW` | | 11 | `DIV_ZERO` |
| 3 | `OPERAND_UNDERFLOW` | | 12 | `BOUNDS` |
| 4 | `RETURN_OVERFLOW` | | 13 | `USER` |
| 5 | `INIT_OVERRUN` | | 14 | `BAD_JUMP` |
| 6 | `STACK_OVERFLOW` | | 15 | `BAD_SYSCALL` |
| 7 | `NULL_ACCESS` | | 16 | `BAD_YIELD` |
| 8 | `BAD_ADDRESS` | | 17 | `MEMORY_ERROR` |

See [traps and faults](/vm/traps-and-faults.md).

## `Op : byte`

146 members with explicit values from `0x00` (`NOP`) to `0xBF` (`F2L`); the complete list is in
[opcodes](/vm/opcodes.md).

# Image and debug-section enums

## `StringKind : byte` — `enums.json` `string_kind`

| Value | Member | Meaning |
| --- | --- | --- |
| 0 | `Name` | Task, enum-member and other names. |
| 1 | `Format` | `log` format strings; parsed into `KvmImage.Formats` at load. |
| 2 | `Channel` | `trace` channel names. |

The enum type itself is not referenced in the decompiled code: `StringEntry.Kind` is a `byte` and the code
compares it with the literals 0, 1 and 2 (`ImageReader.cs:197, 537, 847`; `ArgDomains.cs:145`).

## `DebugLang : byte` — `enums.json` `debug_lang`

| Value | Member |
| --- | --- |
| 0 | `KASM` |
| 1 | `KC` |
| 2 | `KScript` |
| 3 | `Generated` |

## `DebugTypeKind : byte` — `enums.json` `debug_type_kind`

| Value | Member | | Value | Member |
| --- | --- | --- | --- | --- |
| 0 | `Void` | | 6 | `Struct` |
| 1 | `Bool` | | 7 | `Enum` |
| 2 | `Int` | | 8 | `Vector` |
| 3 | `Float` | | 9 | `Matrix` |
| 4 | `Pointer` | | 10 | `Record` |
| 5 | `Array` | | 11 | `Function` |

## `DebugLocalKind : byte` — `enums.json` `debug_local_kind`

| Value | Member |
| --- | --- |
| 0 | `Local` |
| 1 | `Argument` |
| 2 | `Temporary` |

The three `Debug*` enums belong to the image's debug section; see [debug info](/compiler/debug-info.md).

# Service-argument and message enums

These exist for the flight services' arguments and messages; the VM proper uses only their **ranges**,
through `enum:` argument domains resolved against `enums.json` (see
[syscall ABI](/vm/services-syscall-abi.md)).

## `Direction : byte` — `enums.json` `direction`

`enums.json` notes that `direction` "is numbered as KSA's FlightComputerAttitudeTrackTarget (0 none and 1
custom are not directions; each direction's name is its frame, then its name)". The stock `hold_direction`
service accepts `enum:direction[2..21]`.

| Value | Member | | Value | Member |
| --- | --- | --- | --- | --- |
| 0 | `None` | | 11 | `VlfRetrograde` |
| 1 | `Custom` | | 12 | `VlfNormal` |
| 2 | `EnuForward` | | 13 | `VlfAntinormal` |
| 3 | `EnuBackward` | | 14 | `VlfOutward` |
| 4 | `EnuUp` | | 15 | `VlfInward` |
| 5 | `EnuDown` | | 16 | `BurnPlusDv` |
| 6 | `LvlhAhead` | | 17 | `BurnMinusDv` |
| 7 | `LvlhBehind` | | 18 | `TargetToward` |
| 8 | `LvlhRadialOut` | | 19 | `TargetAway` |
| 9 | `LvlhRadialIn` | | 20 | `TargetAntivelocity` |
| 10 | `VlfPrograde` | | 21 | `TargetAlign` |

## `AttitudeProfile : byte` — `enums.json` `attitude_profile`

| Value | Member |
| --- | --- |
| 0 | `Strict` |
| 1 | `Balanced` |
| 2 | `Relaxed` |

"Numbered as KSA's FlightComputerAttitudeProfile" per the table header.

## `Apsis : byte` — `enums.json` `apsis`

| Value | Member |
| --- | --- |
| 0 | `Apoapsis` |
| 1 | `Periapsis` |

## `BurnEndCause : byte` — `enums.json` `burn_end_cause`

| Value | Member |
| --- | --- |
| 0 | `None` |
| 1 | `Completed` |
| 2 | `PropellantOut` |
| 3 | `StageRanDry` |
| 4 | `IgnitionDenied` |
| 5 | `PlanningFailed` |
| 6 | `Superseded` |
| 7 | `Cancelled` |

The table header calls it "MSG_BURN_STATUS.end_cause". The host's burn-handle bookkeeping uses it
(for example `BurnEndCause.PlanningFailed` in `VehicleComputer.cs:204, 247`).

# Related lists without a C# enum in this namespace

| `enums.json` key / type | Values | Where it lives in code |
| --- | --- | --- |
| `message_role_bits` | bit 0 `subscribed` (1), bit 1 `published` (2) | `MessageEntry.Role` byte; tested as `Role & 1`, `Role & 2` (`KvmMachine.cs:280-281`) |
| `roll_mode` (not in `enums.json`) | resolved by `GameTables.EnumDomain` from catalogue constants named `KFS_ROLL_*` | `GameTables.cs:708-711` |
| `ImmType : byte` (`KSA.Kfs.Vm.Tables`) | `I8` 0, `U8` 1, `I16` 2, `U16` 3, `U24` 4, `U32` 5, `U64` 6, `Rel16` 7 | opcode immediate kinds[^imm-type] |

# Modding notes

- These values are persisted (images, saved state, the system block) and exported to programs as constants;
  treat them as ABI.
- To give a new service an enumerated argument that the VM validates, the name after `enum:` must be a key
  of `GameTables.EnumsData.Enums` (a `Dictionary<string, List<string>>`); the C# enums here play no part
  in that check. Otherwise declare the argument `any` and validate in the handler.
- `Phase` and `RunOutcome` are internal: from a mod, use `KvmMachine.InBoot`/`IsIdle`/`IsHalted` and
  `TickResult`.

[^vm-enum-files]: CoreClass.cs:3-9; ResetCause.cs:3-9; TaskKind.cs:3-8; TaskStatus.cs:3-8; Phase.cs:3-9; SchedulerPhase.cs:3-9; RunOutcome.cs:3-9; StringKind.cs:3-8; Direction.cs:3-27; Apsis.cs:3-7; AttitudeProfile.cs:3-8; BurnEndCause.cs:3-13; DebugLang.cs:3-9; DebugTypeKind.cs:3-17; DebugLocalKind.cs:3-8; TrapCode.cs:3-23; Op.cs:3-151.
[^enums-json]: KSA.Kfs.Vm.Tables.Data.enums.json:1-20.
[^kvm-machine]: KvmMachine.cs:89-91 (`_resetCause`, `_phase`), 183-217 (`InBoot`, `IsIdle`, `IsHalted`), 2293 and 2408 (phase byte in saved state).
[^image-reader]: ImageReader.cs:195-199 (string kind), 401-405 (task kind).
[^game-tables]: GameTables.cs:215-218 (cores vs `core_class`), 433-440 (part core index), 702-730 (`EnumDomain`).
[^imm-type]: ImmType.cs:3-13.
