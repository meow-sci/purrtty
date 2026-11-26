---
type: Data Table Reference
title: I/O layout tables — IoLayout, LayoutFile, the bench layout and the fingerprint
description: Schema of the memory-mapped I/O layout records, the validation and exact CRC-32 fingerprint algorithm, the complete bench_layout.json, and the SensorFile test-vector format.
resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.bench_layout.json
tags: [kfs, tables, io-layout, fingerprint, sensors, commands, bench]
sources:
  - id: bench-layout-json
    resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.bench_layout.json
    title: bench_layout.json (embedded resource)
  - id: layout-records
    resource: ksa-decomp:KSA.Kfs.Vm.Tables/IoLayout.cs
    title: IoLayout, IoField, LayoutFile, LayoutField (decompiled)
  - id: game-tables
    resource: ksa-decomp:KSA.Kfs.Vm.Tables/GameTables.cs
    title: GameTables — FieldSize, LayoutFrom, LoadLayout, ToLayoutFile, fingerprint (decompiled)
  - id: sensor-file
    resource: ksa-decomp:KSA.Kfs.Vm.Tables/SensorFile.cs
    title: SensorFile (decompiled)
  - id: crc32
    resource: ksa-decomp:KSA.Kfs.Vm/Crc32.cs
    title: Crc32 (decompiled)
  - id: layout-generator
    resource: ksa-decomp:KSA.Kfs.Host/LayoutGenerator.cs
    title: LayoutGenerator (decompiled)
  - id: message-layout
    resource: ksa-decomp:KSA.Kfs.Vm.Tables/MessageLayout.cs
    title: MessageLayout.GroupCount (decompiled)
  - id: image-reader
    resource: ksa-decomp:KSA.Kfs.Vm.Image/ImageReader.cs
    title: ImageReader (decompiled)
  - id: image-builder
    resource: ksa-decomp:KSA.Kfs.Vm.Image/ImageBuilder.cs
    title: ImageBuilder (decompiled)
  - id: kvm-machine
    resource: ksa-decomp:KSA.Kfs.Vm/KvmMachine.cs
    title: KvmMachine (decompiled)
  - id: builtin-includes
    resource: ksa-decomp:KSA.Kfs.KAsm/BuiltinIncludes.cs
    title: BuiltinIncludes (decompiled)
  - id: vehicle-header
    resource: ksa-decomp:KSA.Kfs.KC/VehicleHeader.cs
    title: VehicleHeader (decompiled)
  - id: cli
    resource: ksa-decomp:KSA.Kfs.Cli/Cli.cs
    title: kvm-cli (decompiled)
generated: { by: claude-code/claude-fable-5-2, at: 2026-11-25T21:52:36Z }
status: stable
ksa_build: "2026.11.25.5949"
---

# Purpose

An **I/O layout** describes the two memory-mapped blocks through which a program touches hardware:
the read-only **sensor block** and the writable **command block**. A layout is a flat list of scalar
fields (name, type, offset) per block, the two block sizes, and a CRC-32 **fingerprint** that binds
an image to the layout it was built for. The tables namespace defines the records, the file form
(`LayoutFile`), validation and the fingerprint; one layout — the **bench layout** — is embedded as
`bench_layout.json`. Vehicle layouts are generated at run time by `LayoutGenerator`
([/host/io-layout-generation.md](/host/io-layout-generation.md)).

# Records

All `public sealed record`s.[^layout-records]

| record | parameters |
| --- | --- |
| `LayoutFile` (file form) | `int Version`, `string Source`, `string Name`, `string Fingerprint`, `int SensorSize`, `int CommandSize`, `List<LayoutField> Fields` — JSON `version`, `source`, `name`, `fingerprint`, `sensor_size`, `command_size`, `fields`; all required |
| `LayoutField` | `string Block`, `string Name`, `string Type`, `int Offset`, `bool OneShot = false` — JSON `block`, `name`, `type`, `offset`, `one_shot` (optional) |
| `IoLayout` (in-memory form) | `string Name`, `uint Fingerprint`, `int SensorSize`, `int CommandSize`, `IoField[] SensorFields`, `IoField[] CommandFields`; plus `IEnumerable<IoField> OneShotFields => CommandFields.Where(f => f.OneShot)` |
| `IoField` | `string Name`, `string Type`, `int Offset`, `int Size`, `bool OneShot` |

`IoLayout` is a record whose array members are compared by reference, and several caches key on the
`IoLayout` object identity (`ConditionalWeakTable<IoLayout, …>` in `GeneratedHeaders` and
`VehicleHeader`).

## Field types (`GameTables.FieldSize`, GameTables.cs:737–759)

| `type` | size (bytes) |
| --- | --- |
| `int8`, `uint8` | 1 |
| `int16`, `uint16` | 2 |
| `bool`, `int32`, `uint32`, `float32` | 4 |
| `int64`, `uint64`, `float64` | 8 |
| anything else | `GameTablesException`: `{what}: unknown field type '{type}'` |

Fields are scalars only: "Fields are flattened to scalars; vec3 is three floats at +0, +4, +8"
(bench_layout.json:3). Structure is carried by the dotted/indexed **name** (`imu.accel_body_mps2.x`,
`thruster[3].pulse_s`), from which `VehicleHeader` rebuilds nested C structs and
`MessageLayout.GroupCount` counts array elements.[^vehicle-header][^message-layout]

# Validation — `GameTables.LayoutFrom(LayoutFile l, string what)`

Public static; used for the embedded bench layout (`what = "bench_layout.json"`) and by
`LoadLayout(path)` (`what` = the file name). Checks run in this order (GameTables.cs:761–796):[^game-tables]

| # | check | message |
| --- | --- | --- |
| 1 | every `sensor` field, then every `command` field: `type` known | `{what}: unknown field type '{type}'` |
| 2 | same pass: `offset % size == 0` | `{what}: field '{name}' is misaligned` |
| 3 | every `block` is `sensor` or `command`; no `one_shot` field outside `command` | `{what}: bad block name or a one-shot sensor field` |
| 4 | every field ends within its block; both sizes are multiples of 8 and `<= 65536` | `{what}: a field lies outside its block, or a block size isn't a multiple of 8 within 64 KB` |
| 5 | `fingerprint` starts with `0x` and the rest parses as hex `uint` | `{what}: fingerprint must be 0x-prefixed hex` |
| 6 | the parsed value equals `ComputeLayoutFingerprint(l)` | `{what}: fingerprint 0x{file:X8} differs from the canonical text's 0x{computed:X8} (P-048)` |

Not checked: overlapping fields, duplicate names, gaps, ordering, or `version` (for a layout loaded
with `LoadLayout`; the embedded bench layout's version is range-checked by `Build`). The result is
`new IoLayout(l.Name, fingerprint, l.SensorSize, l.CommandSize, sensorFields, commandFields)`, each
array in file order.

# The fingerprint, exactly

`GameTables.CanonicalLayoutText(LayoutFile l)` (GameTables.cs:830–846) builds a string:

1. for each entry of `l.Fields` **in file order**: `{block} {name} {type} {offset}` — single spaces,
   `offset` in invariant-culture decimal — followed by ` oneshot` when `one_shot` is true, then `\n`;
2. `sensor_size {SensorSize}\n`;
3. `command_size {CommandSize}\n`.

`ComputeLayoutFingerprint(l) = Crc32.Compute(Encoding.UTF8.GetBytes(text))`, and the file stores it as
`0x` + eight upper-case hex digits (`$"0x{…:X8}"` in `ToLayoutFile`).[^game-tables]

`Crc32` (KSA.Kfs.Vm/Crc32.cs) is the standard reflected CRC-32: table generated from polynomial
`0xEDB88320`, register initialised to `~0`, final complement — the same function as zlib `crc32`,
PNG and Ethernet.[^crc32]

`LayoutGenerator` has its own copy of the algorithm for in-memory layouts
(`CanonicalText(IoLayout)` / `Fingerprint(IoLayout)`, LayoutGenerator.cs:97–138): all **sensor**
fields first, then all **command** fields, each line `sensor …` / `command …` with the same format,
then the two size lines.[^layout-generator] The two implementations give the same value exactly when
the file lists every sensor field before every command field, which `ToLayoutFile` guarantees and
`bench_layout.json` satisfies.

## Where the fingerprint is checked

| place | effect |
| --- | --- |
| `ImageBuilder` (ImageBuilder.cs:189) | written at image header offset 28 |
| `ImageReader` (ImageReader.cs:777–780) | `layout`: `the image was built for another I/O layout; rebuild from source` |
| `KvmMachine` state save/restore (KvmMachine.cs:2284, 2377–2380) | `layout`: `the state belongs to another I/O layout` |
| `<vehicle.h>` (VehicleHeader.cs:66–72) | `#define KFS_LAYOUT_FINGERPRINT 0x…u`; also in the generated-file banner |
| console/workbench text | `layout 0x…` in load and status lines |

So a program image is bound to one exact field list: adding, removing, renaming, retyping or moving
any field — or changing a block size — changes the fingerprint and invalidates existing
images.[^image-builder][^image-reader][^kvm-machine]

# Size and alignment rules

For a `LayoutFile` (validated, not computed): each field's offset is a multiple of its own size;
each block size is a multiple of 8 and at most 65,536 bytes ("Block sizes round up to 8").

For generated vehicle layouts (`LayoutGenerator.Vehicle(name, thrusters, engines, gimbals)`,
LayoutGenerator.cs:13–85) the rules are applied constructively:[^layout-generator]

- `thruster[i].pulse_s` — `float32`, one-shot, 4 bytes each from offset 0;
- if there are engines: round the offset up to 8, then per engine `engine[j].throttle` and
  `engine[j].lease_s` (`float32`, not one-shot), 8 bytes per engine;
- if there are gimbals: round up to 8, then per gimbal `gimbal[k].y` and `gimbal[k].z`
  (`float32`), 8 bytes per gimbal;
- `command_size = RoundUp8(end)`; above 65,536 →
  `LayoutLimitException`: `the command block is {n} bytes; its window is 64 KB (FI §5.1 rule 6)`;
- the generated layout has **no sensor fields** and `sensor_size` 0.

The blocks are mapped at fixed addresses, which `<kfs.kinc>` / `<vehicle.kinc>` expose as constants:
`sensors` = `0x80000000`, `commands` = `0x90000000`, `sys` = `0xA0000000`
(BuiltinIncludes.cs:49–51, 70, 75).[^builtin-includes] See
[/vm/memory-map-and-sysblock.md](/vm/memory-map-and-sysblock.md).

## One-shot fields

`one_shot` is legal only in the command block ("' oneshot' appended for a one-shot field (FI §5.1
rule 4)"). `KvmMachine.LoadImage` records `layout.OneShotFields` offsets (KvmMachine.cs:1906); the
latch/clear semantics are described in
[/vm/memory-map-and-sysblock.md](/vm/memory-map-and-sysblock.md) and
[/integration/write-back.md](/integration/write-back.md). In the bench and generated layouts the
thruster pulses are one-shot; engine and gimbal commands are not.

# `bench_layout.json` in full

| key | value |
| --- | --- |
| `version` | `1` |
| `name` | `bench` |
| `fingerprint` | `0x070A6447` |
| `sensor_size` | `32` |
| `command_size` | `64` |
| `fields` | 23 (7 sensor, 16 command) |

`source`, verbatim (bench_layout.json:3):[^bench-layout-json]

> kFS_processors_spec.md §5.4 bench layout, laid out by kFS_flight_interface_spec.md §5.1 rules
> (P-026). Fields are flattened to scalars; vec3 is three floats at +0, +4, +8. Block sizes round up
> to 8. 'fingerprint' is CRC-32 (kVM §7) of the canonical text: one 'block name type offset' line per
> field in order, with ' oneshot' appended for a one-shot field (FI §5.1 rule 4), then 'sensor_size N'
> and 'command_size N', UTF-8 with LF

| # | `block` | `name` | `type` | `offset` | size | `one_shot` | line |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 0 | `sensor` | `imu.accel_body_mps2.x` | `float32` | 0 | 4 | false | 9 |
| 1 | `sensor` | `imu.accel_body_mps2.y` | `float32` | 4 | 4 | false | 10 |
| 2 | `sensor` | `imu.accel_body_mps2.z` | `float32` | 8 | 4 | false | 11 |
| 3 | `sensor` | `imu.rates_body_rps.x` | `float32` | 12 | 4 | false | 12 |
| 4 | `sensor` | `imu.rates_body_rps.y` | `float32` | 16 | 4 | false | 13 |
| 5 | `sensor` | `imu.rates_body_rps.z` | `float32` | 20 | 4 | false | 14 |
| 6 | `sensor` | `imu.valid` | `bool` | 24 | 4 | false | 15 |
| 7 | `command` | `thruster[0].pulse_s` | `float32` | 0 | 4 | true | 16 |
| 8 | `command` | `thruster[1].pulse_s` | `float32` | 4 | 4 | true | 17 |
| 9 | `command` | `thruster[2].pulse_s` | `float32` | 8 | 4 | true | 18 |
| 10 | `command` | `thruster[3].pulse_s` | `float32` | 12 | 4 | true | 19 |
| 11 | `command` | `thruster[4].pulse_s` | `float32` | 16 | 4 | true | 20 |
| 12 | `command` | `thruster[5].pulse_s` | `float32` | 20 | 4 | true | 21 |
| 13 | `command` | `thruster[6].pulse_s` | `float32` | 24 | 4 | true | 22 |
| 14 | `command` | `thruster[7].pulse_s` | `float32` | 28 | 4 | true | 23 |
| 15 | `command` | `thruster[8].pulse_s` | `float32` | 32 | 4 | true | 24 |
| 16 | `command` | `thruster[9].pulse_s` | `float32` | 36 | 4 | true | 25 |
| 17 | `command` | `thruster[10].pulse_s` | `float32` | 40 | 4 | true | 26 |
| 18 | `command` | `thruster[11].pulse_s` | `float32` | 44 | 4 | true | 27 |
| 19 | `command` | `engine[0].throttle` | `float32` | 48 | 4 | false | 28 |
| 20 | `command` | `engine[0].lease_s` | `float32` | 52 | 4 | false | 29 |
| 21 | `command` | `gimbal[0].y` | `float32` | 56 | 4 | false | 30 |
| 22 | `command` | `gimbal[0].z` | `float32` | 60 | 4 | false | 31 |

The sensor block's used bytes end at 28 (`imu.valid` at 24, 4 bytes) and the block is padded to 32;
the command block is exactly full at 64.

Canonical text (990 bytes of UTF-8); its CRC-32, recomputed independently while writing this document, is
`0x070A6447`, equal to the stored fingerprint:

```text
sensor imu.accel_body_mps2.x float32 0
sensor imu.accel_body_mps2.y float32 4
sensor imu.accel_body_mps2.z float32 8
sensor imu.rates_body_rps.x float32 12
sensor imu.rates_body_rps.y float32 16
sensor imu.rates_body_rps.z float32 20
sensor imu.valid bool 24
command thruster[0].pulse_s float32 0 oneshot
command thruster[1].pulse_s float32 4 oneshot
command thruster[2].pulse_s float32 8 oneshot
command thruster[3].pulse_s float32 12 oneshot
command thruster[4].pulse_s float32 16 oneshot
command thruster[5].pulse_s float32 20 oneshot
command thruster[6].pulse_s float32 24 oneshot
command thruster[7].pulse_s float32 28 oneshot
command thruster[8].pulse_s float32 32 oneshot
command thruster[9].pulse_s float32 36 oneshot
command thruster[10].pulse_s float32 40 oneshot
command thruster[11].pulse_s float32 44 oneshot
command engine[0].throttle float32 48
command engine[0].lease_s float32 52
command gimbal[0].y float32 56
command gimbal[0].z float32 60
sensor_size 32
command_size 64
```

The bench layout therefore has 12 thrusters, 1 engine and 1 gimbal for `MessageLayout.GroupCount`
(`THRUSTER_COUNT` 12, `ENGINE_COUNT` 1, `GIMBAL_COUNT` 1), and an IMU in the sensor block that
generated vehicle layouts do not have.

# File helpers

| method | behaviour |
| --- | --- |
| `GameTables.LoadLayout(string path)` | `layout file '{path}' not found` if absent; deserialise with `DataJsonContext.Default.LayoutFile` (`{path}: empty` / `{path}: {json error}`); then `LayoutFrom(file, Path.GetFileName(path))` |
| `GameTables.ToLayoutFile(IoLayout layout, string source)` | sensor fields then command fields as `LayoutField`s, `version` 1, the given `source`, and a freshly computed fingerprint |
| `GameTables.LayoutJson(LayoutFile file)` | `JsonSerializer.Serialize(file, DataJsonContext.Default.LayoutFile) + "\n"` |

In the decompiled game the only callers of `LoadLayout` are in `kvm-cli` (`--layout FILE`, or a
`layout.json` beside the project; Cli.cs:276, 311–314), and `ToLayoutFile` / `LayoutJson` have no
caller; `Inferred:` they exist so a vehicle's generated layout can be exported for the offline
tools.[^cli]

# `SensorFile` — recorded sensor input for the CLI

`public sealed class SensorFile` (SensorFile.cs) is a test-vector format for `kvm-cli run --sensors FILE`
(Cli.cs:454–459); nothing in the game uses it.[^sensor-file][^cli]

```json
{ "fields": ["imu.rates_body_rps.x", "imu.valid"],
  "ticks": [ ["3f800000", 1], ["0", 1] ] }
```

- `Parse(json)` / `Load(path)`: `fields` is an array of sensor-field names; `ticks` is an array of
  rows with at least one value per field. A **string** value is parsed as hexadecimal into a `ulong`
  bit pattern (no `0x` prefix); a **number** is taken as `Int64` (or `UInt64`). Errors are
  `FormatException`s (`sensors: tick {k} has {n} values; the file names {m} fields`, …).
- `int[] Bind(IoLayout layout)`: maps each name to its index in `layout.SensorFields`, or throws
  `sensors: field '{name}' is not in the layout '{layout.Name}'`.
- `Write(layout, map, k, block)`: copies row `min(k, lastRow)` into the sensor block, little-endian,
  truncating each value to the field's size.
- `static string Format(fields, ticks)`: the inverse — `float32`/`float64` fields as lower-case hex
  strings, `int*` fields as signed decimals, others as unsigned decimals masked to the field size.

# Modding notes

- **The I/O layout is the tightest-coupled channel.** Anything a mod adds to the command or sensor
  block changes the fingerprint for every vehicle whose layout gains fields, must be produced by
  `LayoutGenerator.Vehicle` (a `public static` method taking only a name and three counts, which
  emits command fields only — a vehicle has no sensor block at all), consumed by the
  write-back code on the game side, and — for arrays — reflected in `_COUNT` macros that are
  hard-coded to `thruster`, `engine`, `gimbal`. See
  [/host/io-layout-generation.md](/host/io-layout-generation.md) and
  [/integration/write-back.md](/integration/write-back.md) before considering it; a service or a
  message is far cheaper for lights and animations.
- `IoLayout` and `IoField` are public records with public constructors, and `LayoutGenerator.Fingerprint(IoLayout)`
  is public, so a mod *can* construct a valid extended layout (compute the fingerprint from the
  final field arrays, sensors first).
- The fingerprint depends only on fields and sizes — not on services, messages, enums or events — so
  table extensions of those kinds never invalidate a layout.
- `bench_layout.json` is validated at table load: an augmented copy must keep its stored
  `fingerprint` consistent with its fields or the whole `GameTables` load fails.

[^bench-layout-json]: bench_layout.json:2–7 (header), 9–31 (fields).
[^layout-records]: LayoutFile.cs:5; LayoutField.cs:3; IoLayout.cs:6–9; IoField.cs:3.
[^game-tables]: GameTables.cs:732–735 (`BuildLayout`), 737–759 (`FieldSize`), 761–796 (`LayoutFrom`), 798–813 (`LoadLayout`), 815–823 (`ToLayoutFile`), 825–828 (`LayoutJson`), 830–851 (`CanonicalLayoutText`, `ComputeLayoutFingerprint`).
[^sensor-file]: SensorFile.cs:12–158.
[^crc32]: Crc32.cs:7–39.
[^layout-generator]: LayoutGenerator.cs:13–85 (`Vehicle`), 87–95 (`PropsBodySize`, `RoundUp8`), 97–138 (`CanonicalText`, `Fingerprint`).
[^message-layout]: MessageLayout.cs:79–112.
[^image-reader]: ImageReader.cs:777–780.
[^image-builder]: ImageBuilder.cs:189.
[^kvm-machine]: KvmMachine.cs:1906, 2284, 2377–2380.
[^builtin-includes]: BuiltinIncludes.cs:47–51, 68–93.
[^vehicle-header]: VehicleHeader.cs:60–141.
[^cli]: Cli.cs:274–276, 311–314, 454–459.
