---
type: Subsystem Reference
title: I/O layout generation
description: How LayoutGenerator turns a vehicle's thruster, engine and gimbal counts into the memory-mapped command block layout (vehicles get no sensor fields; only the bench layout has them), what limits apply, and how the blocks are written and the command block latched each tick.
resource: ksa-decomp:KSA.Kfs.Host/LayoutGenerator.cs
tags: [kfs, host, io-layout, command-block, sensor-block, layout-generator, limits]
sources:
  - id: layout-generator
    resource: ksa-decomp:KSA.Kfs.Host/LayoutGenerator.cs
    title: LayoutGenerator (decompiled)
  - id: layout-limit-exception
    resource: ksa-decomp:KSA.Kfs.Host/LayoutLimitException.cs
    title: LayoutLimitException (decompiled)
  - id: blocks
    resource: ksa-decomp:KSA.Kfs.Host/Blocks.cs
    title: Blocks (decompiled)
  - id: sensor-block
    resource: ksa-decomp:KSA.Kfs.Host/SensorBlock.cs
    title: SensorBlock (decompiled)
  - id: thruster-commands
    resource: ksa-decomp:KSA.Kfs.Host/ThrusterCommands.cs
    title: ThrusterCommands (decompiled)
  - id: io-layout
    resource: ksa-decomp:KSA.Kfs.Vm.Tables/IoLayout.cs
    title: IoLayout and IoField (decompiled)
  - id: message-layout
    resource: ksa-decomp:KSA.Kfs.Vm.Tables/MessageLayout.cs
    title: MessageLayout (decompiled)
  - id: game-tables
    resource: ksa-decomp:KSA.Kfs.Vm.Tables/GameTables.cs
    title: GameTables (decompiled)
  - id: bench-layout
    resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.bench_layout.json
    title: bench_layout.json (embedded resource)
  - id: kvm-machine
    resource: ksa-decomp:KSA.Kfs.Vm/KvmMachine.cs
    title: KvmMachine (decompiled)
  - id: vehicle-computer
    resource: ksa-decomp:KSA.Kfs.Host/VehicleComputer.cs
    title: VehicleComputer (decompiled)
  - id: host-computer
    resource: ksa-decomp:KSA.Kfs.Host/HostComputer.cs
    title: HostComputer (decompiled)
  - id: kfs-controller-set
    resource: ksa-decomp:KSA.Kfs/KfsControllerSet.cs
    title: KfsControllerSet (decompiled)
generated: { by: claude-code/claude-fable-5-2, at: 2026-11-25T21:30:51Z }
status: stable
ksa_build: "2026.11.25.5949"
---

# What a layout is

An `IoLayout` describes the two memory-mapped I/O blocks a program sees:

```csharp
public sealed record IoLayout(string Name, uint Fingerprint, int SensorSize, int CommandSize,
                              IoField[] SensorFields, IoField[] CommandFields)
{
    public IEnumerable<IoField> OneShotFields => CommandFields.Where(f => f.OneShot);
}
public sealed record IoField(string Name, string Type, int Offset, int Size, bool OneShot);
```

[^io-layout]

- The **sensor block** is read-only to the program and mapped at `0x80000000`; the **command
  block** is read-write and mapped at `0x90000000`. Each window is 64 KB wide
  (`0x80000000–0x80010000`, `0x90000000–0x90010000`); access beyond the block's actual size traps
  `BAD_ADDRESS`.[^kvm-machine] See [memory map and system block](/vm/memory-map-and-sysblock.md).
- An image is compiled against one layout and records its `Fingerprint`; loading it against a
  layout with a different fingerprint fails. See [kVM image format](/compiler/kvm-image-format.md).
- The layout also determines the size of per-vehicle message arrays: `MessageLayout.GroupCount`
  counts the distinct indices `n` of command fields named `group[n]…`.[^message-layout]

Two layouts exist in this build: the embedded **bench layout** and the **vehicle layout** generated
per vehicle.

# `LayoutGenerator.Vehicle`

```csharp
public static IoLayout Vehicle(string name, int thrusters, int engines, int gimbals)
```

The game calls it from `KfsControllerSet`'s constructor with the vehicle id and the number of
`ThrusterController`, `EngineController` and `GimbalController` modules, each list sorted by
`InstanceId`.[^kfs-controller-set] `FakeVehicle` calls it for tests.

With `T`, `E`, `G` the three counts and `up8(n) = (n + 7) / 8 * 8`:[^layout-generator]

| field name | type | size | offset | one-shot | present when |
| --- | --- | --- | --- | --- | --- |
| `thruster[i].pulse_s` | `float32` | 4 | `4·i` | **yes** | `0 ≤ i < T` |
| `engine[j].throttle` | `float32` | 4 | `E0 + 8·j` | no | `0 ≤ j < E` |
| `engine[j].lease_s` | `float32` | 4 | `E0 + 8·j + 4` | no | `0 ≤ j < E` |
| `gimbal[k].y` | `float32` | 4 | `G0 + 8·k` | no | `0 ≤ k < G` |
| `gimbal[k].z` | `float32` | 4 | `G0 + 8·k + 4` | no | `0 ≤ k < G` |

- `E0 = up8(4·T)` (only computed when `E > 0`)
- `G0 = up8(end of the previous group)` — `up8(E0 + 8·E)` when there are engines, else `up8(4·T)`
- `CommandSize = up8(end of the last field)`
- `SensorSize = 0` and `SensorFields = []` — **a vehicle layout has no sensor block at all**
- `Name = name`; `Fingerprint` as below
- field names are formatted with the invariant culture; the index is a plain decimal

Worked examples:

| `T, E, G` | fields (offset) | `CommandSize` |
| --- | --- | --- |
| `12, 1, 1` | `thruster[0..11].pulse_s` (0…44), `engine[0].throttle` (48), `engine[0].lease_s` (52), `gimbal[0].y` (56), `gimbal[0].z` (60) | 64 |
| `3, 0, 2` | `thruster[0..2].pulse_s` (0, 4, 8), `gimbal[0].y` (16), `.z` (20), `gimbal[1].y` (24), `.z` (28) | 32 |
| `0, 0, 0` | none | 0 |

The `12, 1, 1` command block is field-for-field the bench layout's command block.

## What the fields mean to the game

The host layer decodes only the thruster pulses (`ThrusterCommands`, below). Engines and gimbals
are read by the game from `VehicleComputer.Applied` — throttle clamped to `[0, 1]`, `lease_s`
clamped to `[0, float.MaxValue]` and used as `CommandBurnTime`, gimbal `y`/`z` clamped to
`[-1, 1]`, with `NaN` treated as `0`. See [write-back](/integration/write-back.md).

# Limits and `LayoutLimitException`

```csharp
public sealed class LayoutLimitException : Exception   // one constructor: (string message)
```

`Vehicle` throws it in exactly two cases:[^layout-generator][^layout-limit-exception]

| check | message |
| --- | --- |
| `CommandSize > 65536` | `the command block is N bytes; its window is 64 KB (FI §5.1 rule 6)` |
| `PropsBodySize(T, E, G) > 65535` | `MSG_VEHICLE_PROPS's body would be N bytes; body_size is a u16 (FI §5.1 rule 6)` |

```csharp
public static int PropsBodySize(int thrusters, int engines, int gimbals)
    => RoundUp8(120 + 84 * thrusters + 1120 * engines + 76 * gimbals) - 16;
```

The constants are the sizes the message catalogue gives `MSG_VEHICLE_PROPS`: 120 bytes for the
header and fixed fields, 84 per `thruster[i]` element, 1120 per `engine[i]` element, 76 per
`gimbal[i]` element (see [host sampling and leaves](/host/host-sampling-and-leaves.md)). They are
hard-coded here, not derived from `messages.json`.

The message-size check is always the binding one. Computed from the formula:

| vehicle with only… | largest count accepted | body size at that count |
| --- | --- | --- |
| thrusters | 778 | 65 456 |
| engines | 58 | 65 064 |
| gimbals | 860 | 65 464 |

(The command block alone would allow 16 384 thrusters.)

Nothing in the host layer catches `LayoutLimitException`. `Inferred:` when it is thrown from
`KfsVehicleHost.DetectLayoutChange` during a dispatch it propagates to `KfsDispatch`'s catch-all
and becomes a host fault; when thrown while a host is first created for a vehicle it propagates to
that caller.

Other limits on layouts in general come from `GameTables.LayoutFrom` (used for the bench layout and
layout files): every field inside its block, both block sizes multiples of 8, both at most 65 536
bytes, fields aligned to their size, no one-shot sensor fields, and the stored fingerprint equal to
the computed one.[^game-tables]

# Fingerprint and canonical text

```csharp
public static string CanonicalText(IoLayout layout)
public static uint   Fingerprint(IoLayout layout)      // Crc32.Compute(UTF8(CanonicalText))
```

Canonical text is one line per field, sensors first then commands, each in array order, then the
two sizes, with `\n` line ends:[^layout-generator]

```text
sensor <name> <type> <offset>[ oneshot]
command <name> <type> <offset>[ oneshot]
sensor_size <N>
command_size <N>
```

For a vehicle with one thruster and one engine the text is:

```text
command thruster[0].pulse_s float32 0 oneshot
command engine[0].throttle float32 8
command engine[0].lease_s float32 12
sensor_size 0
command_size 16
```

The fingerprint depends on field names, types, offsets, the one-shot flag and both sizes — but not
on the layout's `Name`. Two vehicles with the same controller counts have the same fingerprint, so
an image built for one loads on the other.

# The bench layout

The embedded `bench_layout.json` (name `bench`, fingerprint `0x070A6447`, `sensor_size` 32,
`command_size` 64) is the only layout in this build with sensor fields:[^bench-layout]

| block | name | type | offset | one-shot |
| --- | --- | --- | --- | --- |
| sensor | `imu.accel_body_mps2.x` / `.y` / `.z` | `float32` | 0 / 4 / 8 | |
| sensor | `imu.rates_body_rps.x` / `.y` / `.z` | `float32` | 12 / 16 / 20 | |
| sensor | `imu.valid` | `bool` | 24 | |
| command | `thruster[0]` … `thruster[11]` `.pulse_s` | `float32` | 0 … 44 | yes |
| command | `engine[0].throttle` | `float32` | 48 | |
| command | `engine[0].lease_s` | `float32` | 52 | |
| command | `gimbal[0].y` | `float32` | 56 | |
| command | `gimbal[0].z` | `float32` | 60 | |

See [I/O layout tables](/tables/io-layout-tables.md) for the file schema. On the bench the sensor
block is always all zeros (`BenchMachine` passes a zero-filled array of `SensorSize` bytes).

# `SensorBlock`

```csharp
public sealed class SensorBlock
{
    public byte[] Bytes { get; }
    public SensorBlock(IoLayout layout) { Bytes = new byte[layout.SensorSize]; }
}
```

`VehicleComputer.Load` creates one per image and passes `_sensors.Bytes` to every `RunTick`.
**Nothing in the decompiled code ever writes into `Bytes`**, and for a vehicle layout the array has
length 0.[^sensor-block][^vehicle-computer] In this build a flying program receives all of its
inputs through host-published messages ([message bus](/host/message-bus.md)), not through the
sensor block.

# How the blocks move each tick

| step | where | what |
| --- | --- | --- |
| sensors in | `HostComputer.RunTick` → `machine.WriteSensors(sensors)` | copies the host's array into the VM's sensor buffer; throws `ArgumentException` if the length differs from `layout.SensorSize` |
| program runs | `KvmMachine.RunTick` | tasks read the sensor window and write the command window (a working buffer) |
| latch | end of `KvmMachine.RunTick` | if no task overran and no trap occurred, the working command block is copied to the VM's latched block and folded into `CommandStreamHash` |
| one-shot clear | end of `KvmMachine.RunTick` | every one-shot field's 4 bytes in the **working** block are zeroed, latched or not |
| commands out | `HostComputer.RunTick` → `machine.ReadCommands(_latched)` | only when the tick latched; this is `HostComputer.Latched` / `VehicleComputer.Applied` |
| pulses | `VehicleComputer.ApplyPlayer` → `ThrusterCommands.Read` | only in full control on a controllable vehicle |

[^kvm-machine][^host-computer][^vehicle-computer]

Consequences: a level command (throttle, gimbal) persists in the working block until the program
changes it; a pulse must be rewritten every tick it is wanted; a tick that overruns or traps
leaves the previously latched block in force.

# `ThrusterCommands`

```csharp
public sealed class ThrusterCommands
{
    public const double WATCHDOG_CAP_SECONDS = 1.0;
    public int Count { get; }
    public ThrusterCommands(IoLayout layout);
    public int Read(ReadOnlySpan<byte> latched, Span<double> pulses);
}
```

The constructor takes `LayoutGenerator.ThrusterCount(layout)` (the `thruster` group count) and looks
up the offset of each `thruster[i].pulse_s` command field by name. `Read` decodes each as a
little-endian `float`: `NaN` or `<= 0` gives `0.0`; anything else gives `min(value, 1.0)` and is
counted. The return value is the number of positive pulses.[^thruster-commands]

# `Blocks`

```csharp
[Flags]
public enum Blocks { None = 0, Navigation = 1, Guidance = 2, AttitudeControl = 4, Allocation = 8 }
```

`Blocks` is unrelated to the I/O blocks: it names the four flight-software blocks a program can own.
See [ownership and control modes](/host/ownership-and-control-modes.md).[^blocks]

# Other helpers

- `LayoutGenerator.ThrusterCount(IoLayout layout)` → `MessageLayout.GroupCount(layout, "thruster")`.
- `LayoutGenerator.TagName(string group, string tag, bool kscript)` sanitises a tag into an
  identifier (ASCII letters and digits kept, other runs collapsed to one `_`, trimmed; `null` when
  empty); for kScript it lower-cases, otherwise it upper-cases and prefixes `THR_`, `ENG_` or
  `GIM_` (any other group throws `ArgumentException`). `Not active in this build:` it has no caller.

# Modding notes

- `LayoutGenerator` is a public static class; `Vehicle` is the single place the vehicle layout is
  defined, so a Harmony postfix on it changes the layout for the compiler headers, the image
  validator, the VM and the host at once. `IoLayout` and `IoField` have public constructors, and
  `LayoutGenerator.Fingerprint(IoLayout)` is public, so a postfix can return
  `layout with { CommandFields = …, CommandSize = …, Fingerprint = … }`. `Unverified:` this patch
  was not among those exercised offline; the single-definition claim rests on `Vehicle` having
  only two callers (`KfsControllerSet`, `FakeVehicle`).
- A layout is **frozen per `KfsControllerSet`**: it is generated when the vehicle host is created,
  and the host's `Layout` is that frozen set's layout for the host's whole life. When the number of
  thruster, engine or gimbal controllers changes (`KfsControllerSet.Matches`) a second set
  (`KfsVehicleHost.Current`) is rebuilt with its own generated layout, but it is used only for
  change detection: a program — including one loaded afterwards — keeps the frozen layout.
- Adding command fields named `light[n].<x>` would make `MessageLayout.GroupCount(layout, "light")`
  return the number of lights, which is what sizes a `light[i]` array in a message — the two
  mechanisms are linked by the field-name convention, not by a registry.
- Extra command fields change `CommandSize` and therefore must respect the 64 KB window and the
  multiple-of-8 rule. Non-one-shot fields also take part in `VehicleComputer.IsQuiescent` (a held
  non-zero level keeps a halted full-control computer from going quiescent).
- Adding **sensor** fields additionally requires writing them: `SensorBlock.Bytes` is a public
  array, but the `SensorBlock` instance is the private field `VehicleComputer._sensors`.
- The `PropsBodySize` constants are a second, hand-maintained copy of the `MSG_VEHICLE_PROPS`
  element sizes. Changing that message's row without changing them only desynchronises the limit
  check, not the packing.

[^layout-generator]: LayoutGenerator.cs — `Vehicle` :13–85; limits :72–81; `PropsBodySize` :87–90; `RoundUp8` :92–95; `CanonicalText` :97–128; `Fingerprint` :130–138; `TagName` :140–174; `ThrusterCount` :176–179.
[^layout-limit-exception]: LayoutLimitException.cs:5–11.
[^blocks]: Blocks.cs:5–13.
[^sensor-block]: SensorBlock.cs:5–13.
[^thruster-commands]: ThrusterCommands.cs:8–43.
[^io-layout]: IoLayout.cs:6–9; IoField.cs:3.
[^message-layout]: MessageLayout.cs — `GroupCount` :79–112; per-vehicle arrays sized by it :245–285.
[^game-tables]: GameTables.cs — `LayoutFrom` :761–796.
[^bench-layout]: bench_layout.json (whole file).
[^kvm-machine]: KvmMachine.cs — address windows in `Resolve` :504–536; buffers sized at load :1893–1895; `WriteSensors` :2028–2035; latch :2181–2193; one-shot clear :2194–2198.
[^vehicle-computer]: VehicleComputer.cs — `_sensors`/`_thrusters` created at `Load` :331–333; `RunTick(k, _sensors.Bytes, …)` :570; `ApplyPlayer` :585–593.
[^host-computer]: HostComputer.cs — `WriteSensors` call :419; `ReadCommands` on a latched tick :434–438.
[^kfs-controller-set]: KfsControllerSet.cs — controller lists sorted by `InstanceId` and layout generation :60–85; `Matches` :112–119. KfsVehicleHost.cs — `Layout => Frozen.Layout` :67; get-only `Frozen` :75, set once :109; `Current` rebuilt in `DetectLayoutChange` :428–440.
