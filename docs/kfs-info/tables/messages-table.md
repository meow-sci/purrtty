---
type: Data Table Reference
title: messages.json — the kFS message catalogue and its layout rules
description: Schema, packing rules and complete contents of the embedded message catalogue (ten bus messages), with every field's offset, size, type and meaning.
resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.messages.json
tags: [kfs, tables, messages, bus, layout, abi, extension]
sources:
  - id: messages-json
    resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.messages.json
    title: messages.json (embedded resource)
  - id: message-records
    resource: ksa-decomp:KSA.Kfs.Vm.Tables/MessageRow.cs
    title: MessagesFile, MessageRow, MessageFieldRow, MessageMember, MessageLeaf, MessageElementType (decompiled)
  - id: message-layout
    resource: ksa-decomp:KSA.Kfs.Vm.Tables/MessageLayout.cs
    title: MessageLayout (decompiled)
  - id: game-tables
    resource: ksa-decomp:KSA.Kfs.Vm.Tables/GameTables.cs
    title: GameTables (decompiled)
  - id: bench-layout-json
    resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.bench_layout.json
    title: bench_layout.json (element counts used for the offsets shown here)
  - id: host-messages
    resource: ksa-decomp:KSA.Kfs.Host/HostMessages.cs
    title: HostMessages (decompiled)
  - id: host-computer
    resource: ksa-decomp:KSA.Kfs.Host/HostComputer.cs
    title: HostComputer (decompiled)
  - id: vehicle-computer
    resource: ksa-decomp:KSA.Kfs.Host/VehicleComputer.cs
    title: VehicleComputer (decompiled)
  - id: ownership
    resource: ksa-decomp:KSA.Kfs.Host/Ownership.cs
    title: Ownership (decompiled)
  - id: layout-generator
    resource: ksa-decomp:KSA.Kfs.Host/LayoutGenerator.cs
    title: LayoutGenerator (decompiled)
  - id: image-reader
    resource: ksa-decomp:KSA.Kfs.Vm.Image/ImageReader.cs
    title: ImageReader (decompiled)
  - id: vehicle-header
    resource: ksa-decomp:KSA.Kfs.KC/VehicleHeader.cs
    title: VehicleHeader (decompiled)
  - id: builtin-includes
    resource: ksa-decomp:KSA.Kfs.KAsm/BuiltinIncludes.cs
    title: BuiltinIncludes (decompiled)
  - id: kc-front-end
    resource: ksa-decomp:KSA.Kfs.KC/KcFrontEnd.cs
    title: KcFrontEnd (decompiled)
  - id: assembler
    resource: ksa-decomp:KSA.Kfs.KAsm/Assembler.cs
    title: Assembler (decompiled)
  - id: bus-monitor-view
    resource: ksa-decomp:KSA.Kfs.Host.Views/BusMonitorView.cs
    title: BusMonitorView (decompiled)
generated: { by: claude-code/claude-fable-5-2, at: 2026-11-25T21:52:36Z }
status: stable
ksa_build: "2026.11.25.5949"
---

# Purpose

`messages.json` is the **message catalogue**: the fixed set of typed records exchanged over the kFS
software bus between a program's tasks, other computers and the host. A row names a message id and
an ordered list of fields; `MessageLayout` turns a row plus a vehicle's I/O layout into a concrete
C-style struct (offsets, sizes, leaf paths). The catalogue is what `#pragma kfs subscribe/publish`
(kC) and `.subscribe/.publish` (kASM) name, what `<vehicle.h>` / `<vehicle.kinc>` declare, and what
`ImageReader` validates an image's `Messages[]` against.

Bus semantics (slots, sequence numbers, latching) are in [/host/message-bus.md](/host/message-bus.md)
and [/vm/kvm-machine.md](/vm/kvm-machine.md); the loader is in
[game-tables-loader](/tables/game-tables-loader.md).

# File header

| key | value |
| --- | --- |
| `version` | `4` — "the message catalogue version recorded in images (kVM §7)" |
| `header_bytes` | `16` |
| `messages` | 10 rows |

`source`, verbatim (messages.json:3):[^messages-json]

> kFS_flight_interface_spec.md §8 (moved 2026-11-02, D-052). 'version' is the message catalogue
> version recorded in images (kVM §7). Each row lists the field names sharing one type, as the spec's
> rows do; 'per_vehicle' arrays are sized by the layout generator (FI §5)

Field meanings mention "catalogue v3" (D-102) and "catalogue v4" (D-116, D-117, D-118) additions, so
`version` has been bumped as fields were added.

# Schema

All are `public sealed record`s in `KSA.Kfs.Vm.Tables`.[^message-records]

## `MessagesFile`

`MessagesFile(int Version, string Source, int HeaderBytes, List<MessageRow> Messages)` — JSON keys
`version`, `source`, `header_bytes`, `messages`; all required. `HeaderBytes` is not read by any code;
the header size comes from `MessageLayout.HEADER_BYTES` (`const int 16`).[^message-layout]

## `MessageRow`

`MessageRow(string Id, string Name, string Section, List<MessageFieldRow> Fields, bool PerVehicle = false)`

| property | JSON key | type | required | meaning |
| --- | --- | --- | --- | --- |
| `Id` | `id` | `string` | yes | `"0x"` + four hex digits; unique; exactly six characters (the only load-time check) |
| `Name` | `name` | `string` | yes | `MSG_…` constant name; `TypeNameOf` assumes the `MSG_` prefix |
| `Section` | `section` | `string` | yes | section of the flight-interface spec, e.g. `8.5`; printed by `kfs msg` as "FI §…" |
| `Fields` | `fields` | `List<MessageFieldRow>` | yes | ordered field rows |
| `PerVehicle` | `per_vehicle` | `bool` | no (default `false`) | the message contains arrays sized by the vehicle layout; only used to word an `ImageReader` error |

`GameTables.CheckMessages` enforces the `id` rule at load and nothing else about this file.[^game-tables]

## `MessageFieldRow`

`MessageFieldRow(List<string> Names, string Type, string Meaning)` — JSON keys `names`, `type`,
`meaning`; all required. One row declares **several consecutive members** of the same type (each
entry of `names`), sharing one `meaning`. A name ending in `[i]` declares a per-vehicle array whose
`type` is an element-struct spec.

## `MessageLayout` and its parts

`public sealed class MessageLayout` — all properties `required … { get; init; }`:[^message-layout]

| member | type | content |
| --- | --- | --- |
| `Id` | `ushort` | `Convert.ToUInt16(row.Id.Substring(2), 16)` |
| `Name` | `string` | row name |
| `TypeName` | `string` | `"kfs_msg_" + name.Substring(4).ToLowerInvariant() + "_t"` |
| `Size` | `int` | slot size in bytes, header included, rounded up to the struct alignment |
| `BodySize` | `int` | `Size - 16` |
| `PerVehicle` | `bool` | from the row |
| `HasDouble` | `bool` | any leaf of type `float64` |
| `Members` | `IReadOnlyList<MessageMember>` | top-level members, `hdr` first |
| `Leaves` | `IReadOnlyList<MessageLeaf>` | every scalar, header leaves first (empty when built with `withLeaves: false`) |
| `ElementTypes` | `IReadOnlyList<MessageElementType>` | element structs of the arrays present |

- `MessageMember(string Name, string TypeName, int Offset, int Size, int Align, int Count)` —
  `Count` is `-1` for a non-array member, else the element count.
- `MessageLeaf(string Path, string Type, int Offset)` with `int Size` = 2 for `uint16`, 8 for `int64`
  and `float64`, otherwise 4. Leaf types are `bool`, `uint32`, `int64`, `float32`, `float64` (plus
  `uint16` in the header).
- `MessageElementType(string TypeName, int Size, int Align, IReadOnlyList<MessageMember> Members)`.

Static API: `Build(MessageRow, IoLayout, bool withLeaves = true)`, `All(GameTables, IoLayout)`,
`Find(GameTables, IoLayout, ushort id)`, `Find(GameTables, IoLayout, string name, bool withLeaves = true)`,
`BodySizeOf(GameTables, IoLayout, ushort id)` (−1 when the id is not in the catalogue),
`TypeNameOf(string)`, `GroupCount(IoLayout, string group)`; instance `Leaf(path)` (throws
`KeyNotFoundException`) and `Offset(path)` (exact leaf, else the lowest offset under `path.` or
`path[`). No layout is cached per `GameTables`: every call rebuilds from `MessagesData.Messages`.

# Packing rules

## Header

Every message starts with a 16-byte, 8-aligned `kfs_msg_header_t` (`MessageLayout.Header`,
MessageLayout.cs:17–23):[^message-layout]

| leaf path | type | offset | size |
| --- | --- | --- | --- |
| `hdr.seq` | `uint32` | 0 | 4 |
| `hdr.source` | `uint16` | 4 | 2 |
| `hdr.flags` | `uint16` | 6 | 2 |
| `hdr.t_us` | `int64` | 8 | 8 |

`<vehicle.h>` declares it as
`typedef struct kfs_msg_header { uint32_t seq; uint16_t source; uint16_t flags; int64_t t_us; } kfs_msg_header_t;`.[^vehicle-header]
The header is "set by the bus (FI §6.2)"; host-published messages carry `source` `0xFFFF`
(`HOST_SOURCE`) and `flags` 1 (`HOST_FLAGS`).[^bus-monitor-view][^host-messages]

## Field types

`MessageLayout.TypeShape` (MessageLayout.cs:137–151) — any other `type` throws
`InvalidDataException("messages.json: unknown field type '…'")` when a layout is built:

| table `type` | C type | size | align | leaves (path suffix: leaf type @ offset) |
| --- | --- | --- | --- | --- |
| `bool` | `kfs_bool32_t` | 4 | 4 | `bool` @0 (a 32-bit boolean) |
| `uint32` | `uint32_t` | 4 | 4 | `uint32` @0 |
| `int64` | `int64_t` | 8 | 8 | `int64` @0 |
| `float` | `float` | 4 | 4 | `float32` @0 |
| `vec3` | `vec3` | 12 | 4 | `.x` `.y` `.z`: `float32` @0, 4, 8 |
| `quat` | `quat` | 16 | 4 | `.x` `.y` `.z` `.w`: `float32` @0, 4, 8, 12 |
| `mat3` | `mat3` | 36 | 4 | `.m[i][j]`: `float32` @ `(3i + j) × 4`, row-major |
| `dvec3` | `dvec3` | 24 | 8 | `.x` `.y` `.z`: `float64` @0, 8, 16 |

There is no `double` scalar, no 8/16-bit integer, no signed 32-bit integer and no string type.

## Struct layout (`MessageLayout.Build`, MessageLayout.cs:230–314)

1. Start at offset 16 with struct alignment 8 (the header).
2. For each field row in order, for each name in `names` in order:
   - round the offset up to the type's alignment (`Up(n, a) = (n + a - 1) / a * a`);
   - add a member `(name, C type, offset, size, align, Count = -1)` and its leaves, with leaf path
     `name` for scalars and `name.suffix` otherwise;
   - advance by the size; the struct alignment is the maximum member alignment.
3. `Size = Up(offset, alignment)` — always a multiple of 8.

This is natural C layout with 4- and 8-byte alignment, so padding appears only before `int64` /
`dvec3` members and at the end (for example `MSG_NAV_STATE` has 4 bytes of padding at offset 28 and
`MSG_BURN_PLAN` ends with 4 bytes of padding).

## Per-vehicle arrays (`name[i]`)

For a name ending in `[i]`, with `group` = the name without `[i]`:

- `count = MessageLayout.GroupCount(layout, group)` — the number of **distinct indices `N`** among
  the layout's *command* fields named `group[N]…` (MessageLayout.cs:79–112). The array length is thus
  defined by the vehicle's command block, e.g. `thruster[0].pulse_s` … `thruster[11].pulse_s` → 12.
- If `count == 0` the member is **omitted entirely** (no member, no element type, no leaves).
- The element struct is parsed from the `type` spec `{name: type, name: type[N], …}`
  (`Element`, MessageLayout.cs:169–228): members are laid out with the same alignment rule, a
  `type[N]` member is a fixed-size inner array, and the element size is rounded up to the element's
  alignment. The element type is named `kfs_vehicle_props_{group}_t` regardless of which message
  declares it (`CachedElement`, line 164) and cached process-wide by `(group, spec, withLeaves)`.
- The array member is `(group, element type name, offset, element size × count, element align, count)`;
  leaves are `group[k].member`, `group[k].member.x`, `group[k].member[j]`, `group[k].member[j].x`.
- In `<vehicle.h>` the member is declared `kfs_vehicle_props_thruster_t thruster[THRUSTER_COUNT];`
  where `THRUSTER_COUNT`, `ENGINE_COUNT`, `GIMBAL_COUNT` are `#define`d from the same
  `GroupCount`.[^vehicle-header] Only those three group names get a `_COUNT` macro (hard-coded list in
  `VehicleHeader.Build` and `BuiltinIncludes`).[^builtin-includes]

`LayoutGenerator.PropsBodySize(thrusters, engines, gimbals) = RoundUp8(120 + 84·thrusters + 1120·engines + 76·gimbals) − 16`
hard-codes the element sizes of this table (84, 1120, 76) and the 120-byte fixed part; it is used to
refuse vehicles whose `MSG_VEHICLE_PROPS` body would exceed 65,535 bytes (`body_size` is a
u16).[^layout-generator] With the bench layout (12, 1, 1) it gives 2312, matching the layout below.

# Who publishes what

`messages.json` has no publisher or rate column. The rules live in code:

- `MessageLayout.HostPublished` = `MSG_FLIGHT_DATA`, `MSG_VEHICLE_PROPS`, `MSG_BURN_STATUS`,
  `MSG_PILOT_INPUT`, `MSG_FC_SETTINGS`, `MSG_BURN_PLAN`. `HostComputer.Load` rejects an image that
  *publishes* any of them: `the program publishes {name}, which the host publishes (integration §7.4, FI §6.5)`.[^message-layout][^host-computer]
- `MessageLayout.SequencerForbidden` = `MSG_NAV_STATE`, `MSG_ATT_TARGET`, `MSG_WRENCH_CMD`: publishing
  them on part index 0 (the Sequencer) is diagnostic 520 in both front ends ("Owning a GNC block needs
  the Mk1 or above (processors §6)").[^kc-front-end][^assembler]
- `HostMessages.MsG3Order` = ids 257, 258, 1025, 1026, 1027, 1028, 1029 (`MSG_NAV_STATE` plus the
  six host-published messages). On a vehicle computer the host packs each of these that the loaded
  image **subscribes to and does not itself publish** (VehicleComputer.cs:338–342).[^vehicle-computer][^host-messages]
  `HostMessages.PackInto` throws `0x…. is not a host message (integration §7.4)` for any other id.
- `MSG_ATT_TARGET` and `MSG_GUIDANCE_STATUS`: an image that subscribes to either without publishing
  it is refused — "only stock guidance published it, and there is no stock computer in this release
  (kFS D-135)" (Ownership.cs:8, 48–65).[^ownership]

Publication **rate** is not a property of the catalogue; it follows the publishing task's rate or
the host tick — see [/host/message-bus.md](/host/message-bus.md) and
[/host/host-sampling-and-leaves.md](/host/host-sampling-and-leaves.md).

# The catalogue

Offsets and sizes below were computed by re-implementing `MessageLayout.Build` over `messages.json`
and `bench_layout.json` (12 thrusters, 1 engine, 1 gimbal). They are the same for every vehicle
except inside and after the per-vehicle arrays of `MSG_VEHICLE_PROPS`, where they depend on the
element counts. Units are part of the field **names** (`_m`, `_mps`, `_rad`, `_rps`, `_s`, `_us`,
`_N`, `_Nm`, `_Nms`, `_Ns`, `_kg`, `_kgm2`, `_pa`, `_k`, `_m2`, `_kgps`, `_mps2`, `_rps2`,
`_jpkgk`; "the unit suffix (FI §2.1)"); frame names appear as `_cci`, `_body`, `_helm`, `_ctrl`,
`_vlf`, `_gimbal`.[^messages-json][^bench-layout-json]

| `id` | decimal | `name` | `section` | C type | slot size (bytes) | body size | `per_vehicle` | `HasDouble` | publisher | `SequencerForbidden` | line |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| `0x0101` | 257 | `MSG_NAV_STATE` | 8.1 | `kfs_msg_nav_state_t` | 120 | 104 | false | yes | host, or the program | yes | 6 |
| `0x0102` | 258 | `MSG_FLIGHT_DATA` | 8.2 | `kfs_msg_flight_data_t` | 192 | 176 | false | no | host only (`HostPublished`) | no | 16 |
| `0x0201` | 513 | `MSG_ATT_TARGET` | 8.3 | `kfs_msg_att_target_t` | 96 | 80 | false | no | program only | yes | 35 |
| `0x0301` | 769 | `MSG_WRENCH_CMD` | 8.4 | `kfs_msg_wrench_cmd_t` | 112 | 96 | false | no | program only | yes | 46 |
| `0x0401` | 1025 | `MSG_VEHICLE_PROPS` | 8.5 | `kfs_msg_vehicle_props_t` | 120 + arrays (bench: 2328) | 104 + arrays (bench: 2312) | true | no | host only (`HostPublished`) | no | 59 |
| `0x0402` | 1026 | `MSG_BURN_STATUS` | 8.6 | `kfs_msg_burn_status_t` | 56 | 40 | false | no | host only (`HostPublished`) | no | 70 |
| `0x0403` | 1027 | `MSG_PILOT_INPUT` | 8.7 | `kfs_msg_pilot_input_t` | 64 | 48 | false | no | host only (`HostPublished`) | no | 78 |
| `0x0202` | 514 | `MSG_GUIDANCE_STATUS` | 8.8 | `kfs_msg_guidance_status_t` | 224 | 208 | false | no | program only | no | 87 |
| `0x0404` | 1028 | `MSG_FC_SETTINGS` | 8.9 | `kfs_msg_fc_settings_t` | 88 | 72 | false | no | host only (`HostPublished`) | no | 101 |
| `0x0405` | 1029 | `MSG_BURN_PLAN` | 8.10 | `kfs_msg_burn_plan_t` | 160 | 144 | false | yes | host only (`HostPublished`) | no | 115 |

Rows appear in the file in this order (not sorted by id): `0x0101`, `0x0102`, `0x0201`, `0x0301`,
`0x0401`, `0x0402`, `0x0403`, `0x0202`, `0x0404`, `0x0405`. `MessageLayout.All` and the generated
headers keep file order; an image's `Messages[]` must be sorted by ascending id
(ImageReader.cs:680–683).[^image-reader]

## `0x0101` `MSG_NAV_STATE`

- JSON row: messages.json:6; `section` `8.1`; `per_vehicle` false.
- C type `kfs_msg_nav_state_t`; slot size 120 bytes on the bench layout (header 16 + body 104).
- `HasDouble` true; publisher: the host when the program subscribes without publishing it, otherwise the program; in `SequencerForbidden` (not publishable on part index 0).

| offset | size | member | table `type` | C type | `meaning` (verbatim) |
| --- | --- | --- | --- | --- | --- |
| 0 | 16 | `hdr` | — | `kfs_msg_header_t` | Message header: `seq` (uint32 @0), `source` (uint16 @4), `flags` (uint16 @6), `t_us` (int64 @8) |
| 16 | 4 | `valid` | `bool` | `kfs_bool32_t` | The estimate is usable |
| 20 | 4 | `parent_id` | `uint32` | `uint32_t` | Identifier of the current parent body: the CRC-32 (`kVM_spec.md` §7) of its KSA body id in UTF-8 (`kFS_ksa_integration_spec.md` §7.5; proposal P-136) |
| 24 | 4 | `soi_changes` | `uint32` | `uint32_t` | Matches the system block when the estimate is current |
| 32 | 24 | `r_cci_m` | `dvec3` | `dvec3` | Position of the control point in `CCI` |
| 56 | 24 | `v_cci_mps` | `dvec3` | `dvec3` | Velocity of the centre of mass in `CCI` |
| 80 | 16 | `q_body_to_cci` | `quat` | `quat` | Attitude |
| 96 | 12 | `rates_body_rps` | `vec3` | `vec3` | Body angular rates |
| 108 | 12 | `accel_body_mps2` | `vec3` | `vec3` | Specific force (gravity excluded) |

## `0x0102` `MSG_FLIGHT_DATA`

- JSON row: messages.json:16; `section` `8.2`; `per_vehicle` false.
- C type `kfs_msg_flight_data_t`; slot size 192 bytes on the bench layout (header 16 + body 176).
- `HasDouble` false; publisher: the host only (in `HostPublished`: an image that publishes it is rejected at load).

| offset | size | member | table `type` | C type | `meaning` (verbatim) |
| --- | --- | --- | --- | --- | --- |
| 0 | 16 | `hdr` | — | `kfs_msg_header_t` | Message header: `seq` (uint32 @0), `source` (uint16 @4), `flags` (uint16 @6), `t_us` (int64 @8) |
| 16 | 4 | `altitude_m` | `float` | `float` | Above mean radius; above terrain |
| 20 | 4 | `radar_altitude_m` | `float` | `float` | (same JSON row as `altitude_m`; shared meaning) |
| 24 | 4 | `apoapsis_m` | `float` | `float` | Altitudes above mean radius. `apoapsis_m` is +∞ on an unbound orbit (P-136) |
| 28 | 4 | `periapsis_m` | `float` | `float` | (same JSON row as `apoapsis_m`; shared meaning) |
| 32 | 4 | `time_to_apoapsis_s` | `float` | `float` | Time to the next passage; +∞ when there is none: an unbound orbit, or past periapsis on one (P-136) |
| 36 | 4 | `time_to_periapsis_s` | `float` | `float` | (same JSON row as `time_to_apoapsis_s`; shared meaning) |
| 40 | 4 | `speed_mps` | `float` | `float` | (empty) |
| 44 | 4 | `surface_speed_mps` | `float` | `float` | (same JSON row as `speed_mps`; shared meaning) |
| 48 | 4 | `vertical_speed_mps` | `float` | `float` | (same JSON row as `speed_mps`; shared meaning) |
| 52 | 16 | `q_helm_to_body` | `quat` | `quat` | Control point orientation: rotates `HELM` into `BODY` (KSA's `Helm2Body`) |
| 68 | 4 | `target_valid` | `bool` | `kfs_bool32_t` | A target is set |
| 72 | 12 | `target_position_helm_m` | `vec3` | `vec3` | Target relative to the control point, in `HELM`; 0 when no target is set |
| 84 | 12 | `target_velocity_helm_mps` | `vec3` | `vec3` | (same JSON row as `target_position_helm_m`; shared meaning) |
| 96 | 4 | `target_range_m` | `float` | `float` | Magnitudes of the two vectors above; 0 when no target is set |
| 100 | 4 | `target_speed_rel_mps` | `float` | `float` | (same JSON row as `target_range_m`; shared meaning) |
| 104 | 4 | `in_atmosphere` | `bool` | `kfs_bool32_t` | (empty) |
| 108 | 4 | `ambient_pressure_pa` | `float` | `float` | (empty) |
| 112 | 16 | `q_parent_fixed_to_cci` | `quat` | `quat` | Orientation of the parent body's body-fixed frame (`CCF`) in `CCI`; with `parent_spin_cci_rps` it defines the `ENU` and surface frames (D-090, D-091) |
| 128 | 12 | `parent_spin_cci_rps` | `vec3` | `vec3` | The parent body's angular velocity in `CCI` (D-090, D-091) |
| 140 | 4 | `target_dock_valid` | `bool` | `kfs_bool32_t` | The two dock-frame fields are set: true whenever a target is set, as KSA's `NavigationTarget.Dock2Cci` exists for every target (catalogue v3, D-102) |
| 144 | 16 | `target_q_dock_to_cci` | `quat` | `quat` | The target's dock frame in `CCI` (KSA's `NavigationTarget.Dock2Cci`): a docking port's frame, or for any other target its own frame as KSA defines it; for `ALIGN` and the `DOCK` frame; identity when no target is set (D-090, D-091, D-102) |
| 160 | 12 | `target_dock_rates_cci_rps` | `vec3` | `vec3` | The dock frame's angular rate in `CCI` (KSA's `NavigationTarget.DockFrameRatesCci`); 0 when no target is set (D-090, D-091, D-102) |
| 172 | 4 | `parent_mass_kg` | `float` | `float` | The parent body's mass; the `VLF` and `FLP` frame rates use G·M/r² (catalogue v3, D-102) |
| 176 | 16 | `q_parent_cce_to_cci` | `quat` | `quat` | Orientation of the parent body's ecliptic frame (`CCE`) in `CCI`, for the `ECL` frame of a custom attitude target (KSA's `Cce2Cci`) (catalogue v3, D-102) |

## `0x0201` `MSG_ATT_TARGET`

- JSON row: messages.json:35; `section` `8.3`; `per_vehicle` false.
- C type `kfs_msg_att_target_t`; slot size 96 bytes on the bench layout (header 16 + body 80).
- `HasDouble` false; publisher: a program only (the host never packs it); in `SequencerForbidden` (not publishable on part index 0).

| offset | size | member | table `type` | C type | `meaning` (verbatim) |
| --- | --- | --- | --- | --- | --- |
| 0 | 16 | `hdr` | — | `kfs_msg_header_t` | Message header: `seq` (uint32 @0), `source` (uint16 @4), `flags` (uint16 @6), `t_us` (int64 @8) |
| 16 | 4 | `mode` | `uint32` | `uint32_t` | `0` `NONE` (free: KSA's attitude mode Manual), `1` `RATE` (rate hold: Auto with no track target), `2` `ATTITUDE` (attitude target: Auto with a track target). Stock guidance publishes the mode and target its errors are formed from, before the alignment retarget of the same tick (`kFS_ksa_integration_spec.md` §7.6, P-186) |
| 20 | 16 | `q_target_helm_to_cci` | `quat` | `quat` | Target attitude of `HELM`, the control point (mode 2). Under `TNM` stock guidance converts KSA's control-frame target T_c: this field is `compose(inverse(q_ctrl_to_helm), T_c)` in KSA's order (`kFS_ksa_integration_spec.md` §7.6) |
| 36 | 12 | `rates_ff_body_rps` | `vec3` | `vec3` | Feed-forward rates in `BODY`, for example a rotating frame's rate: KSA's target rates in `CCI` rotated into `BODY` by this tick's `MSG_NAV_STATE.q_body_to_cci` |
| 48 | 4 | `roll_mode` | `uint32` | `uint32_t` | `0` `FREE`, `1` `UP`, `2` `DOWN` |
| 52 | 4 | `throttle` | `float` | `float` | Main-engine throttle request in [0, 1] |
| 56 | 4 | `engines_on` | `bool` | `kfs_bool32_t` | Main engines should be firing |
| 60 | 12 | `translate_helm_mps` | `vec3` | `vec3` | RCS translation request: the velocity change, in m/s along each `HELM` axis, to deliver over the next second (proposal P-146, confirmed by D-078) |
| 72 | 16 | `q_ctrl_to_helm` | `quat` | `quat` | The control frame, in which attitude control forms its per-axis errors and laws: identity for `HELM`, or the thrust-normal frame (`TNM`, `HELM` turned so +X lies along the trimmed thrust) while engines fire or a burn is armed. The target above is still `HELM`'s. The host writes it back as the vehicle's control frame (`kFS_ksa_integration_spec.md` §7.8) (D-090, D-091) |
| 88 | 4 | `engine_burn_s` | `float` | `float` | Engine burn time from this tick to cutoff, which attitude control passes on in `MSG_WRENCH_CMD.engine_burn_s`; +∞ for no planned cutoff (G-R1-224) (D-090, D-091) |

## `0x0301` `MSG_WRENCH_CMD`

- JSON row: messages.json:46; `section` `8.4`; `per_vehicle` false.
- C type `kfs_msg_wrench_cmd_t`; slot size 112 bytes on the bench layout (header 16 + body 96).
- `HasDouble` false; publisher: a program only (the host never packs it); in `SequencerForbidden` (not publishable on part index 0).

| offset | size | member | table `type` | C type | `meaning` (verbatim) |
| --- | --- | --- | --- | --- | --- |
| 0 | 16 | `hdr` | — | `kfs_msg_header_t` | Message header: `seq` (uint32 @0), `source` (uint16 @4), `flags` (uint16 @6), `t_us` (int64 @8) |
| 16 | 12 | `torque_body_Nm` | `vec3` | `vec3` | Requested torque |
| 28 | 12 | `force_body_N` | `vec3` | `vec3` | Requested translational force from RCS |
| 40 | 4 | `throttle` | `float` | `float` | Passed through from guidance, possibly limited |
| 44 | 4 | `engines_on` | `bool` | `kfs_bool32_t` | Passed through from guidance |
| 48 | 4 | `detail_valid` | `bool` | `kfs_bool32_t` | The fields below are filled; when 0, allocation derives them from the torque and force above by the averaging formulas of `kFS_ksa_integration_spec.md` §7.1. Stock allocation chooses its merge by the publisher, not by this flag (integration §9.2's pilot-override table, D-096) (D-090, D-091) |
| 52 | 16 | `q_ctrl_to_body` | `quat` | `quat` | The control frame whose axes the per-axis fields use (D-090, D-091). All zeros (not a valid quaternion) means guidance's control frame (D-111 (1)) |
| 68 | 4 | `axis_system` | `uint32` | `uint32_t` | Actuator per control axis, two bits each (x bits 0–1, y 2–3, z 4–5): 0 none, 1 RCS, 2 TVC (D-090, D-091) |
| 72 | 12 | `rcs_impulse_ctrl_Nms` | `vec3` | `vec3` | Angular impulse to deliver with RCS on the RCS axes, in the control frame; its on-time may exceed a tick (D-090, D-091) |
| 84 | 12 | `tvc_cmd_ctrl` | `vec3` | `vec3` | TVC rotation command per control axis in [−1, 1], a fraction of the TVC torque range in that direction. Stock allocation applies all three components, whatever each axis's actuator. Stock attitude control sets, in Auto, the law's command on TVC axes and 0 elsewhere; in Manual, while any axis is on TVC, the pilot's ±1 on every keyed axis, RCS and unassigned axes included, and 0 elsewhere (`FlightComputer.cs` 745–757, 767–770; D-097 (1)) (D-090, D-091) |
| 96 | 12 | `linear_impulse_ctrl_Ns` | `vec3` | `vec3` | Linear impulse for RCS translation, in the control frame. Stock attitude control sends 0: allocation computes the pilot's Pulse and Direct paths itself (`kFS_ksa_integration_spec.md` §7.1) (D-090, D-091) |
| 108 | 4 | `engine_burn_s` | `float` | `float` | Engine burn time from this tick to cutoff, sent as the engines' lease; +∞ for no planned cutoff, 0 for off (D-090, D-091) |

## `0x0401` `MSG_VEHICLE_PROPS`

- JSON row: messages.json:59; `section` `8.5`; `per_vehicle` true.
- C type `kfs_msg_vehicle_props_t`; slot size 2328 bytes on the bench layout (header 16 + body 2312); 120 bytes with no per-vehicle elements.
- `HasDouble` false; publisher: the host only (in `HostPublished`: an image that publishes it is rejected at load).

| offset | size | member | table `type` | C type | `meaning` (verbatim) |
| --- | --- | --- | --- | --- | --- |
| 0 | 16 | `hdr` | — | `kfs_msg_header_t` | Message header: `seq` (uint32 @0), `source` (uint16 @4), `flags` (uint16 @6), `t_us` (int64 @8) |
| 16 | 4 | `mass_kg` | `float` | `float` | (empty) |
| 20 | 36 | `I_body_kgm2` | `mat3` | `mat3` | Inertia about the centre of mass |
| 56 | 12 | `com_body_m` | `vec3` | `vec3` | Centre of mass in the assembly frame (`BODY` axes; the `BODY` origin is the centre of mass) |
| 68 | 4 | `max_g_load` | `float` | `float` | The structural g-load limit (KSA's `EffectiveMaxGLoad` of the bounding radius), in g (D-090, D-091) |
| 72 | 12 | `ang_accel_body_rps2` | `vec3` | `vec3` | Angular acceleration in `BODY`, as the C# computer's `ReadMeasurements` last took it from a physics step (D-090, D-091) |
| 84 | 12 | `rcs_torque_ctrl_Nm` | `vec3` | `vec3` | The thruster set's conservative per-axis torque authority, minimum rotational impulse and the translation thrust that rotation firings cause, in the control frame of `MSG_ATT_TARGET` (KSA's thruster cache) (D-090, D-091) |
| 96 | 12 | `rcs_min_impulse_ctrl_Nms` | `vec3` | `vec3` | (same JSON row as `rcs_torque_ctrl_Nm`; shared meaning) |
| 108 | 12 | `rcs_thrust_from_rotation_N` | `vec3` | `vec3` | (same JSON row as `rcs_torque_ctrl_Nm`; shared meaning) |
| 120 | 1008 = 12 × 84 | `thruster[THRUSTER_COUNT]` | struct (below) | `kfs_vehicle_props_thruster_t` | Per-thruster geometry at full thrust, about the current centre of mass. Element i describes the thruster `commands.thruster[i]` drives, in the same order and count (§5.1). `min_pulse_s` is the thruster controller's minimum pulse time, the largest across its cores; it may be 0 (`kFS_ksa_integration_spec.md` §8.1). `control_map` is the thruster's twelve control flags: bit k is KSA's `ThrusterMapFlags` bit k + 1, so bits 0–5 rotate +x, −x, +y, −y, +z, −z (roll right, roll left, pitch up, pitch down, yaw right, yaw left) and bits 6–11 translate +x, −x, +y, −y, +z, −z (forward, backward, right, left, down, up), axes of the control frame; `torque_ctrl_Nm`, `force_ctrl_N`, `torque_fraction` and `force_fraction` are KSA's per-thruster intended action and share of the set's authority, in the control frame (D-090, D-091) |
| 1128 | 1120 = 1 × 1120 | `engine[ENGINE_COUNT]` | struct (below) | `kfs_vehicle_props_engine_t` | Element i describes `commands.engine[i]`. `mass_flow_kgps` at full throttle; `min_thrust_N` and `min_mass_flow_kgps` at `min_throttle`, all at the current ambient pressure (D-090, D-091; informational since P-176 was withdrawn, D-102). The rest (catalogue v3, D-102) are the static parameters of KSA's engine-core performance model (`RocketControllerData.ComputeFromCores`), with which stock guidance evaluates the engine below full throttle at `MSG_FLIGHT_DATA.ambient_pressure_pa` (`kFS_ksa_integration_spec.md` §7.6 row 1). `cores` (at most 2) and `nozzles` (at most 8) count the controller's cores and nozzles. Core c: `core_kind` 1 a combustor (a one-dimensional gas table over ln pressure), 2 a thermal core (two rows of a two-dimensional table at its fixed core temperature), 3 a core whose conditions depend on its state (solid motor, antimatter ram core) or a nozzle other than a de Laval nozzle, for which stock guidance takes the engine's full-throttle values at every throttle; `core_min_throttle`, `core_p_max_pa` (the chamber pressure at full throttle), `core_efficiency` (thermal efficiency); for kind 2 `core_temperature_k` (the core temperature clamped to the table) and `core_row_interp` (its interpolation between the two rows); `core_points` the table's points (at most 16). The table of core c is entries 16·c to 16·c + `core_points` − 1 of the `lut_` arrays: ln of the pressure in Pa, temperature (kind 1), ratio of specific heats and specific gas constant (the lower row for kind 2), and the upper row's two (kind 2). Nozzle j, in the order of the controller's cores and then each core's nozzles: `nozzle_core` its core, its throat and exit areas, flow and expansion efficiencies (a de Laval nozzle), and `nozzle_dir_body` its thrust direction in the assembly frame (`BODY` axes) as KSA computes it from the exhaust direction and the nozzle part's orientation. Unused entries are 0 |
| 2248 | 76 = 1 × 76 | `gimbal[GIMBAL_COUNT]` | struct (below) | `kfs_vehicle_props_gimbal_t` | Element i describes `commands.gimbal[i]`. `active`: the gimbal moves an active engine; `thrust_N`: the thrust of the nozzles it moves now, `max_thrust_N` at full throttle; `pivot_body_m`: the pivot in the assembly frame (as `com_body_m`); `dir0_gimbal` and `offset_gimbal_m`: the undeflected thrust direction and the thrust point's offset from the pivot, in the gimbal frame; `q_gimbal_to_body`: the gimbal frame in `BODY`; `circular`: the deflection is limited to a circle (KSA's `TvcAllocation` inputs) (D-090, D-091) |

### Element type `kfs_vehicle_props_thruster_t` (size 84, align 4)

| offset | size | member | spec type | C type | count |
| --- | --- | --- | --- | --- | --- |
| 0 | 12 | `force_body_N` | `vec3` | `vec3` | — |
| 12 | 12 | `torque_body_Nm` | `vec3` | `vec3` | — |
| 24 | 4 | `min_pulse_s` | `float` | `float` | — |
| 28 | 4 | `propellant_ok` | `bool` | `kfs_bool32_t` | — |
| 32 | 4 | `control_map` | `uint32` | `uint32_t` | — |
| 36 | 12 | `torque_ctrl_Nm` | `vec3` | `vec3` | — |
| 48 | 12 | `force_ctrl_N` | `vec3` | `vec3` | — |
| 60 | 12 | `torque_fraction` | `vec3` | `vec3` | — |
| 72 | 12 | `force_fraction` | `vec3` | `vec3` | — |

### Element type `kfs_vehicle_props_engine_t` (size 1120, align 4)

| offset | size | member | spec type | C type | count |
| --- | --- | --- | --- | --- | --- |
| 0 | 4 | `max_thrust_N` | `float` | `float` | — |
| 4 | 4 | `min_throttle` | `float` | `float` | — |
| 8 | 4 | `isp_s` | `float` | `float` | — |
| 12 | 4 | `active` | `bool` | `kfs_bool32_t` | — |
| 16 | 4 | `propellant_ok` | `bool` | `kfs_bool32_t` | — |
| 20 | 4 | `mass_flow_kgps` | `float` | `float` | — |
| 24 | 4 | `min_thrust_N` | `float` | `float` | — |
| 28 | 4 | `min_mass_flow_kgps` | `float` | `float` | — |
| 32 | 4 | `cores` | `uint32` | `uint32_t` | — |
| 36 | 4 | `nozzles` | `uint32` | `uint32_t` | — |
| 40 | 8 | `core_kind` | `uint32[2]` | `uint32_t` | 2 |
| 48 | 8 | `core_min_throttle` | `float[2]` | `float` | 2 |
| 56 | 8 | `core_p_max_pa` | `float[2]` | `float` | 2 |
| 64 | 8 | `core_efficiency` | `float[2]` | `float` | 2 |
| 72 | 8 | `core_temperature_k` | `float[2]` | `float` | 2 |
| 80 | 8 | `core_row_interp` | `float[2]` | `float` | 2 |
| 88 | 8 | `core_points` | `uint32[2]` | `uint32_t` | 2 |
| 96 | 128 | `lut_ln_p` | `float[32]` | `float` | 32 |
| 224 | 128 | `lut_temperature_k` | `float[32]` | `float` | 32 |
| 352 | 128 | `lut_gamma` | `float[32]` | `float` | 32 |
| 480 | 128 | `lut_r_jpkgk` | `float[32]` | `float` | 32 |
| 608 | 128 | `lut_gamma_hi` | `float[32]` | `float` | 32 |
| 736 | 128 | `lut_r_hi_jpkgk` | `float[32]` | `float` | 32 |
| 864 | 32 | `nozzle_core` | `uint32[8]` | `uint32_t` | 8 |
| 896 | 32 | `nozzle_throat_m2` | `float[8]` | `float` | 8 |
| 928 | 32 | `nozzle_exit_m2` | `float[8]` | `float` | 8 |
| 960 | 32 | `nozzle_flow_eff` | `float[8]` | `float` | 8 |
| 992 | 32 | `nozzle_expansion_eff` | `float[8]` | `float` | 8 |
| 1024 | 96 | `nozzle_dir_body` | `vec3[8]` | `vec3` | 8 |

### Element type `kfs_vehicle_props_gimbal_t` (size 76, align 4)

| offset | size | member | spec type | C type | count |
| --- | --- | --- | --- | --- | --- |
| 0 | 4 | `range_y_rad` | `float` | `float` | — |
| 4 | 4 | `range_z_rad` | `float` | `float` | — |
| 8 | 4 | `active` | `bool` | `kfs_bool32_t` | — |
| 12 | 4 | `thrust_N` | `float` | `float` | — |
| 16 | 4 | `max_thrust_N` | `float` | `float` | — |
| 20 | 12 | `pivot_body_m` | `vec3` | `vec3` | — |
| 32 | 12 | `dir0_gimbal` | `vec3` | `vec3` | — |
| 44 | 12 | `offset_gimbal_m` | `vec3` | `vec3` | — |
| 56 | 16 | `q_gimbal_to_body` | `quat` | `quat` | — |
| 72 | 4 | `circular` | `bool` | `kfs_bool32_t` | — |

## `0x0402` `MSG_BURN_STATUS`

- JSON row: messages.json:70; `section` `8.6`; `per_vehicle` false.
- C type `kfs_msg_burn_status_t`; slot size 56 bytes on the bench layout (header 16 + body 40).
- `HasDouble` false; publisher: the host only (in `HostPublished`: an image that publishes it is rejected at load).

| offset | size | member | table `type` | C type | `meaning` (verbatim) |
| --- | --- | --- | --- | --- | --- |
| 0 | 16 | `hdr` | — | `kfs_msg_header_t` | Message header: `seq` (uint32 @0), `source` (uint16 @4), `flags` (uint16 @6), `t_us` (int64 @8) |
| 16 | 4 | `burn` | `uint32` | `uint32_t` | Handle of the current burn: the one the latest plan or `burn_start` request named, else the loaded burn's (§8.6); 0 if none. Handles are the vehicle's, issued by the host (§9.1, D-116 (3)) |
| 20 | 4 | `state` | `uint32` | `uint32_t` | `0` `NONE`, `1` `PLANNED`, `2` `ALIGNING`, `3` `BURNING`, `4` `DONE`, `5` `ABORTED`, `6` `PENDING` (the plan is requested and not built yet; catalogue v4, D-117 (1)) |
| 24 | 8 | `t_ignition_us` | `int64` | `int64_t` | Planned ignition |
| 32 | 4 | `dv_remaining_mps` | `float` | `float` | Magnitude of velocity-to-go |
| 36 | 12 | `dv_vlf_mps` | `vec3` | `vec3` | Planned velocity change in `VLF` at the burn point |
| 48 | 4 | `end_cause` | `uint32` | `uint32_t` | Why the current handle ended, numbered by `enums.json` `burn_end_cause` (§8.6): none while it hasn't ended, completed, propellant out, stage ran dry, ignition denied, planning failed, superseded, cancelled. Latched with `DONE` and `ABORTED` (catalogue v4, D-117 (1)) |

## `0x0403` `MSG_PILOT_INPUT`

- JSON row: messages.json:78; `section` `8.7`; `per_vehicle` false.
- C type `kfs_msg_pilot_input_t`; slot size 64 bytes on the bench layout (header 16 + body 48).
- `HasDouble` false; publisher: the host only (in `HostPublished`: an image that publishes it is rejected at load).

| offset | size | member | table `type` | C type | `meaning` (verbatim) |
| --- | --- | --- | --- | --- | --- |
| 0 | 16 | `hdr` | — | `kfs_msg_header_t` | Message header: `seq` (uint32 @0), `source` (uint16 @4), `flags` (uint16 @6), `t_us` (int64 @8) |
| 16 | 12 | `rotate` | `vec3` | `vec3` | Pilot rotation input per control-frame axis in [−1, 1]: x roll (+ right), y pitch (+ up), z yaw (+ right). KSA's keys are digital, so each component is −1, 0 or 1 (`kFS_ksa_integration_spec.md` §7.5) |
| 28 | 12 | `translate` | `vec3` | `vec3` | Pilot translation input per control-frame axis in [−1, 1]: x forward, y right, z down positive; −1, 0 or 1 from KSA's keys |
| 40 | 4 | `throttle` | `float` | `float` | Pilot throttle setting |
| 44 | 4 | `throttle_active` | `bool` | `kfs_bool32_t` | The pilot changed the throttle this tick |
| 48 | 4 | `engine_on` | `bool` | `kfs_bool32_t` | The pilot's engine switch (KSA's manual `EngineOn`) (D-090, D-091) |
| 52 | 4 | `stage_presses` | `uint32` | `uint32_t` | Count of the pilot's staging-key presses applied since the computer's reset; a `stage()` service request doesn't count (D-116, P-254) |
| 56 | 4 | `kill_switch` | `bool` | `kfs_bool32_t` | Never seen by running code: the kill switch halts every computer (`kFS_ksa_integration_spec.md` §9.3). Reserved |

## `0x0202` `MSG_GUIDANCE_STATUS`

- JSON row: messages.json:87; `section` `8.8`; `per_vehicle` false.
- C type `kfs_msg_guidance_status_t`; slot size 224 bytes on the bench layout (header 16 + body 208).
- `HasDouble` false; publisher: a program only (the host never packs it).

| offset | size | member | table `type` | C type | `meaning` (verbatim) |
| --- | --- | --- | --- | --- | --- |
| 0 | 16 | `hdr` | — | `kfs_msg_header_t` | Message header: `seq` (uint32 @0), `source` (uint16 @4), `flags` (uint16 @6), `t_us` (int64 @8) |
| 16 | 4 | `burn_mode` | `uint32` | `uint32_t` | The flight computer's burn and attitude modes after this tick: `0` manual, `1` auto. Guidance drops a burn to manual and turns attitude to auto for the alignment (D-090, D-091) |
| 20 | 4 | `attitude_mode` | `uint32` | `uint32_t` | (same JSON row as `burn_mode`; shared meaning) |
| 24 | 4 | `frame` | `uint32` | `uint32_t` | The attitude reference frame and track target after this tick, as `MSG_FC_SETTINGS` numbers them (guidance retargets to +Δv at alignment and falls back to rate hold) (D-090, D-091) |
| 28 | 4 | `track_target` | `uint32` | `uint32_t` | (same JSON row as `frame`; shared meaning) |
| 32 | 4 | `angle_deadband_rad` | `float` | `float` | The profile's angle deadband and rate limit after the floors guidance applies (D-090, D-091) |
| 36 | 4 | `rate_limit_rps` | `float` | `float` | (same JSON row as `angle_deadband_rad`; shared meaning) |
| 40 | 4 | `burn_completed` | `bool` | `kfs_bool32_t` | Burn-end events and the g-load throttle limit, set in the tick they happen (D-090, D-091) |
| 44 | 4 | `burn_out_of_propellant` | `bool` | `kfs_bool32_t` | (same JSON row as `burn_completed`; shared meaning) |
| 48 | 4 | `burn_stage_ran_dry` | `bool` | `kfs_bool32_t` | (same JSON row as `burn_completed`; shared meaning) |
| 52 | 4 | `throttle_g_limited` | `bool` | `kfs_bool32_t` | (same JSON row as `burn_completed`; shared meaning) |
| 56 | 4 | `burn_started` | `bool` | `kfs_bool32_t` | The loaded burn has ignited (D-090, D-091) |
| 64 | 8 | `t_started_us` | `int64` | `int64_t` | Ignition time, planned ignition and alignment start of the loaded burn (D-090, D-091) |
| 72 | 8 | `t_ignition_us` | `int64` | `int64_t` | (same JSON row as `t_started_us`; shared meaning) |
| 80 | 8 | `t_alignment_us` | `int64` | `int64_t` | (same JSON row as `t_started_us`; shared meaning) |
| 88 | 4 | `burn_throttle_valid` | `bool` | `kfs_bool32_t` | The throttle frozen at ignition is set; the last ignition request was denied (D-090, D-091) |
| 92 | 4 | `last_ignition_denied` | `bool` | `kfs_bool32_t` | (same JSON row as `burn_throttle_valid`; shared meaning) |
| 96 | 4 | `burn_throttle` | `float` | `float` | Burn progress and the performance the planner predicts the burn with (D-090, D-091) |
| 100 | 4 | `burn_duration_s` | `float` | `float` | (same JSON row as `burn_throttle`; shared meaning) |
| 104 | 4 | `planned_throttle` | `float` | `float` | (same JSON row as `burn_throttle`; shared meaning) |
| 108 | 4 | `planned_thrust_N` | `float` | `float` | (same JSON row as `burn_throttle`; shared meaning) |
| 112 | 4 | `planned_mass_flow_kgps` | `float` | `float` | (same JSON row as `burn_throttle`; shared meaning) |
| 116 | 16 | `q_burn_body_to_cci` | `quat` | `quat` | The burn attitude (D-090, D-091) |
| 132 | 12 | `error_angles_rad` | `vec3` | `vec3` | Per control axis, the values the phase-plane display shows (D-090, D-091) |
| 144 | 12 | `error_rates_rps` | `vec3` | `vec3` | (same JSON row as `error_angles_rad`; shared meaning) |
| 156 | 12 | `error_integrals` | `vec3` | `vec3` | (same JSON row as `error_angles_rad`; shared meaning) |
| 168 | 12 | `angle_turnaround_rad` | `vec3` | `vec3` | (same JSON row as `error_angles_rad`; shared meaning) |
| 180 | 12 | `rate_bit_rps` | `vec3` | `vec3` | (same JSON row as `error_angles_rad`; shared meaning) |
| 192 | 12 | `rate_deadband_rps` | `vec3` | `vec3` | (same JSON row as `error_angles_rad`; shared meaning) |
| 204 | 12 | `rcs_authority_rps2` | `vec3` | `vec3` | (same JSON row as `error_angles_rad`; shared meaning) |
| 216 | 4 | `latch_states` | `uint32` | `uint32_t` | The phase-plane latch per axis, two bits each: 0 inside, 1 above, 2 below (D-090, D-091) |
| 220 | 4 | `rcs_accel_bound_mps2` | `float` | `float` | The translation acceleration attitude firings can cause, for the host's step bound (D-090, D-091) |

## `0x0404` `MSG_FC_SETTINGS`

- JSON row: messages.json:101; `section` `8.9`; `per_vehicle` false.
- C type `kfs_msg_fc_settings_t`; slot size 88 bytes on the bench layout (header 16 + body 72).
- `HasDouble` false; publisher: the host only (in `HostPublished`: an image that publishes it is rejected at load).

| offset | size | member | table `type` | C type | `meaning` (verbatim) |
| --- | --- | --- | --- | --- | --- |
| 0 | 16 | `hdr` | — | `kfs_msg_header_t` | Message header: `seq` (uint32 @0), `source` (uint16 @4), `flags` (uint16 @6), `t_us` (int64 @8) |
| 16 | 4 | `burn_mode` | `uint32` | `uint32_t` | `0` manual, `1` auto (D-090, D-091) |
| 20 | 4 | `attitude_mode` | `uint32` | `uint32_t` | (same JSON row as `burn_mode`; shared meaning) |
| 24 | 4 | `frame` | `uint32` | `uint32_t` | Attitude reference frame, numbered as KSA's `VehicleReferenceFrame`: `0` ECL, `1` ENU, `2` LVLH, `3` VLF, `4` BURN, `5` DOCK (D-090, D-091) |
| 28 | 4 | `track_target` | `uint32` | `uint32_t` | Numbered as KSA's `FlightComputerAttitudeTrackTarget`: `0` none (rate hold), `1` custom, `2`–`5` FORWARD, BACKWARD, UP, DOWN, `6`–`9` AHEAD, BEHIND, RADIAL_OUT, RADIAL_IN, `10`–`15` PROGRADE, RETROGRADE, NORMAL, ANTINORMAL, OUTWARD, INWARD, `16`–`17` PLUS_DV, MINUS_DV, `18`–`21` TOWARD, AWAY, ANTIVELOCITY, ALIGN (§9.2) (D-090, D-091) |
| 32 | 12 | `custom_target_rad` | `vec3` | `vec3` | Custom attitude as Euler angles in `frame` (rad); in rate hold, a rate offset added to the target rates (rad/s), the one exception to the unit suffix (FI §2.1) (D-090, D-091) |
| 44 | 4 | `manual_thrust_mode` | `uint32` | `uint32_t` | Manual thrust: `0` direct, `1` pulse. Roll: `0` decoupled, `1` up, `2` down (D-090, D-091) |
| 48 | 4 | `roll` | `uint32` | `uint32_t` | (same JSON row as `manual_thrust_mode`; shared meaning) |
| 52 | 4 | `rcs_enabled` | `bool` | `kfs_bool32_t` | RCS enabled (D-090, D-091) |
| 56 | 4 | `angle_deadband_rad` | `float` | `float` | The attitude profile (D-090, D-091) |
| 60 | 4 | `rate_limit_rps` | `float` | `float` | (same JSON row as `angle_deadband_rad`; shared meaning) |
| 64 | 4 | `sim_speed` | `float` | `float` | Time warp, and 1/`MaxFlightComputerRate` for the deadband floor (D-090 (3a)) (D-090, D-091) |
| 68 | 4 | `min_fc_dt_s` | `float` | `float` | (same JSON row as `sim_speed`; shared meaning) |
| 72 | 4 | `service_throttle` | `float` | `float` | The service override's throttle (`set_throttle`, §9.1), read while `service_throttle_valid`: 0 cuts the engines, a positive value below the engines' minimum throttle is raised to it as the pilot's is. Stock guidance applies it in place of `MSG_PILOT_INPUT.throttle` in Manual burn mode; in Auto it is ignored, except that 0 cancels an active Auto burn (`kFS_ksa_integration_spec.md` §8.4, §9.2) (catalogue v4, D-116 (1), D-118 (2)) |
| 76 | 4 | `service_throttle_valid` | `bool` | `kfs_bool32_t` | `service_throttle` is set: from a `set_throttle` request until the pilot changes the throttle while the burn mode is Manual (`kFS_ksa_integration_spec.md` §8.4) or the vehicle leaves kFS control (catalogue v4, D-116 (1), D-118 (4)) |
| 80 | 4 | `service_engine_on` | `bool` | `kfs_bool32_t` | The service override's engine switch (`engines_on`, `engines_off`), read while `service_engine_valid`, in place of `MSG_PILOT_INPUT.engine_on` (catalogue v4, D-116 (1)) |
| 84 | 4 | `service_engine_valid` | `bool` | `kfs_bool32_t` | `service_engine_on` is set: from an `engines_on` or `engines_off` request until the pilot changes the engine switch while the burn mode is Manual (`kFS_ksa_integration_spec.md` §8.4) or the vehicle leaves kFS control (catalogue v4, D-116 (1), D-118 (4)) |

## `0x0405` `MSG_BURN_PLAN`

- JSON row: messages.json:115; `section` `8.10`; `per_vehicle` false.
- C type `kfs_msg_burn_plan_t`; slot size 160 bytes on the bench layout (header 16 + body 144).
- `HasDouble` true; publisher: the host only (in `HostPublished`: an image that publishes it is rejected at load).

| offset | size | member | table `type` | C type | `meaning` (verbatim) |
| --- | --- | --- | --- | --- | --- |
| 0 | 16 | `hdr` | — | `kfs_msg_header_t` | Message header: `seq` (uint32 @0), `source` (uint16 @4), `flags` (uint16 @6), `t_us` (int64 @8) |
| 16 | 4 | `valid` | `bool` | `kfs_bool32_t` | A burn is loaded (D-090, D-091) |
| 20 | 4 | `burn` | `uint32` | `uint32_t` | Its handle, as in `MSG_BURN_STATUS` (D-090, D-091) |
| 24 | 8 | `t_impulse_us` | `int64` | `int64_t` | The impulsive instant, and the planner's ignition time (D-090, D-091) |
| 32 | 8 | `t_planned_ignition_us` | `int64` | `int64_t` | (same JSON row as `t_impulse_us`; shared meaning) |
| 40 | 4 | `planned_ignition_valid` | `bool` | `kfs_bool32_t` | Which optional values are set; `later_stage`: the staged burn lights a later stage (D-090, D-091) |
| 44 | 4 | `planned_throttle_valid` | `bool` | `kfs_bool32_t` | (same JSON row as `planned_ignition_valid`; shared meaning) |
| 48 | 4 | `fixed_duration_valid` | `bool` | `kfs_bool32_t` | (same JSON row as `planned_ignition_valid`; shared meaning) |
| 52 | 4 | `later_stage` | `bool` | `kfs_bool32_t` | (same JSON row as `planned_ignition_valid`; shared meaning) |
| 56 | 24 | `r_burn_cci_m` | `dvec3` | `dvec3` | Position at the burn point (D-090, D-091) |
| 80 | 12 | `dv_target_cci_mps` | `vec3` | `vec3` | The velocity change to make, and the specific-force Δv accumulated since loading, summed over every physics step (D-090, D-091) |
| 92 | 12 | `dv_accum_cci_mps` | `vec3` | `vec3` | (same JSON row as `dv_target_cci_mps`; shared meaning) |
| 104 | 16 | `q_burn_body_to_cci` | `quat` | `quat` | The burn attitude as last computed (D-090, D-091) |
| 120 | 4 | `planned_throttle` | `float` | `float` | The planned throttle; an interstellar burn's fixed duration; the speed above a slow-down burn's stop speed, +∞ for other burns (D-090, D-091) |
| 124 | 4 | `fixed_duration_s` | `float` | `float` | (same JSON row as `planned_throttle`; shared meaning) |
| 128 | 4 | `speed_above_stop_mps` | `float` | `float` | (same JSON row as `planned_throttle`; shared meaning) |
| 132 | 4 | `burn_started` | `bool` | `kfs_bool32_t` | Burn progress, as guidance last wrote it back (D-090, D-091) |
| 136 | 4 | `burn_throttle_valid` | `bool` | `kfs_bool32_t` | (same JSON row as `burn_started`; shared meaning) |
| 140 | 4 | `last_ignition_denied` | `bool` | `kfs_bool32_t` | (same JSON row as `burn_started`; shared meaning) |
| 144 | 8 | `t_started_us` | `int64` | `int64_t` | Ignition time, when started (D-090, D-091) |
| 152 | 4 | `burn_throttle` | `float` | `float` | Throttle frozen at ignition (D-090, D-091) |


# Id space

| id range | used | theme (from the names) |
| --- | --- | --- |
| `0x0101`–`0x0102` | `MSG_NAV_STATE`, `MSG_FLIGHT_DATA` | navigation / flight data |
| `0x0201`–`0x0202` | `MSG_ATT_TARGET`, `MSG_GUIDANCE_STATUS` | guidance outputs |
| `0x0301` | `MSG_WRENCH_CMD` | attitude-control output |
| `0x0401`–`0x0405` | `MSG_VEHICLE_PROPS`, `MSG_BURN_STATUS`, `MSG_PILOT_INPUT`, `MSG_FC_SETTINGS`, `MSG_BURN_PLAN` | host-provided vehicle and flight-computer state |

`Inferred:` the high byte groups messages by producing block (`01` navigation, `02` guidance, `03`
attitude control, `04` host/vehicle); nothing in the code depends on that structure. Ids are 16-bit
(`ushort`); every other value is free. A mod-defined message should use a high byte far from these
(for example `0x7Fxx`).

# Enumerated fields

Three field names are parsed for `` `N` `NAME` `` pairs in their `meaning` text, both by
`CatalogueEnums` (to make `KFS_*` constants, see [enums-table](/tables/enums-table.md)) and by
`BusMonitorView.EnumName` (to show a name next to the number in the bus monitor): `mode`,
`roll_mode`, `state`. Both look the field up as the **first field row in file order whose `names`
contains that name**, across all messages (CatalogueEnums.cs:43; BusMonitorView.cs:200–248).[^bus-monitor-view]

# Modding notes

- **Adding a message row is table-driven for the toolchain**: `MessageLayout.All` then emits its
  `#define MSG_X 0x….` and `typedef struct kfs_msg_x { … } kfs_msg_x_t;` in `<vehicle.h>`, the
  `MSG_X` constant and `; .type`/`; .field` lines in `<vehicle.kinc>`, `kfs msg` documents it,
  `ImageReader` accepts images that subscribe to or publish it and checks their `body_size`, and
  `ArgDomains` accepts its id as a `KFS_EVENT_MESSAGE` parameter. A program can then **publish** it.
  `Not active in this build:` other computers subscribing to it — every vehicle has exactly one
  computer (computer index 0).[^host-computer]
- **Host → program data needs code**: the host only packs the seven `MsG3Order` ids, through
  hard-coded packers in `HostMessages` and `KfsHostSampler`. A new host-published message (for
  example "light states") needs a patch that writes its slot into the machine
  (`KvmMachine.WriteMessage(id, slot)` is the low-level entry the host uses, HostComputer.cs:422) on
  the right thread; see [/host/message-bus.md](/host/message-bus.md) and
  [/integration/host-sampler.md](/integration/host-sampler.md). `MessageLayout.HostPublished` and
  `HostMessages.MsG3Order` are `public static readonly` arrays (elements replaceable, length fixed).
- **Per-vehicle arrays are tied to the command block**: an array member exists only if the vehicle's
  command layout has fields named `group[N]…`, and only `thruster`, `engine`, `gimbal` get `_COUNT`
  macros. A message with a `light[i]` array would need a matching `light[N].…` command group
  generated by `LayoutGenerator` (which changes the layout fingerprint) *and* a `LIGHT_COUNT` macro —
  the generated C text references `LIGHT_COUNT` unconditionally for array members
  (VehicleHeader.cs:184–187), so without the macro `<vehicle.h>` would not compile. A fixed-size
  message, or fixed-size inner arrays inside one element spec (`type[N]`), avoids this — but note the
  `type[N]` form is only parsed inside an element spec, i.e. inside a `[i]` array.
- **Do not edit existing rows**: any size change breaks the `body_size` check for existing images
  and the hard-coded packers/offset tables in `HostMessages`.
- **Name collisions**: do not add fields named `mode`, `roll_mode` or `state` in a message placed
  before `MSG_ATT_TARGET` / `MSG_BURN_STATUS` in the file (first-match lookup above). Appending new
  messages at the end is safe.
- **Size limits**: `body_size` is a u16 in the image; slots live in the VM region of RAM, so large
  messages cost program RAM on every subscriber
  ([/vm/memory-map-and-sysblock.md](/vm/memory-map-and-sysblock.md)).
- `double_bits = 32` programs cannot use messages with `HasDouble` (diagnostic text "Build with
  64-bit doubles to use this message (D-076)", KcFrontEnd.cs:302).

[^messages-json]: messages.json:2–4 (header), 6–127 (rows; per-row line numbers are in the tables above).
[^message-records]: MessagesFile.cs:5; MessageRow.cs:5; MessageFieldRow.cs:5; MessageMember.cs:3; MessageLeaf.cs:3–21; MessageElementType.cs:5.
[^message-layout]: MessageLayout.cs:13 (`Shape`), 15–23 (header), 25 (`_elements`), 27–29 (`HostPublished`, `SequencerForbidden`), 31–49 (properties), 51–77 (`Leaf`, `Offset`, `TypeNameOf`), 79–112 (`GroupCount`), 114–156 (shapes), 158–228 (`CachedElement`, `Element`), 230–349 (`Build`, `All`, `Find`, `BodySizeOf`).
[^game-tables]: GameTables.cs:864–874 (`CheckMessages`).
[^bench-layout-json]: bench_layout.json:16–31 (the `thruster[0…11]`, `engine[0]`, `gimbal[0]` command fields).
[^host-messages]: HostMessages.cs:338–364 (id constants, `HOST_SOURCE`, `HOST_FLAGS`, `Ms5Order`, `MsG3Order`), 378–385 (`IsMsG3`), 387–391, 430–461 (`PackInto`).
[^host-computer]: HostComputer.cs:222 (`new KvmMachine(part, tables, 0)`: computer index 0), 302–314 (`Load`), 422 (`machine.WriteMessage`).
[^vehicle-computer]: VehicleComputer.cs:334–343.
[^ownership]: Ownership.cs:8 (`StockOnlyMessages`), 48–65 (`NeedsStock`).
[^layout-generator]: LayoutGenerator.cs:77–90.
[^image-reader]: ImageReader.cs:608–626, 646–655, 676–691.
[^vehicle-header]: VehicleHeader.cs:76–86 (`_COUNT` defines), 142–190 (`Messages`).
[^builtin-includes]: BuiltinIncludes.cs:78–107.
[^kc-front-end]: KcFrontEnd.cs:263–302.
[^assembler]: Assembler.cs:902–910.
[^bus-monitor-view]: BusMonitorView.cs:200–248, 274–360.
