---
type: Subsystem Reference
title: Host sampling and leaves
description: The data a sampler hands the host each dispatch (HostSample with NavTruth, FlightDataValues, VehiclePropsValues and HostLeafValues), every field of the seven host messages with its type and offset, and when the host asks for each part.
resource: ksa-decomp:KSA.Kfs.Host/HostSample.cs
tags: [kfs, host, sampling, leaves, host-sample, messages, vehicle-props]
sources:
  - id: host-sample
    resource: ksa-decomp:KSA.Kfs.Host/HostSample.cs
    title: HostSample (decompiled)
  - id: host-sampler-interface
    resource: ksa-decomp:KSA.Kfs.Host/IHostSampler.cs
    title: IHostSampler (decompiled)
  - id: value-records
    resource: ksa-decomp:KSA.Kfs.Host/NavTruth.cs
    title: NavTruth, TruthSample, FlightDataValues, VehiclePropsValues, ThrusterProps, EngineProps, GimbalProps, Vec3d, Quatd (decompiled, one file each)
  - id: host-leaf-map
    resource: ksa-decomp:KSA.Kfs.Host/HostLeafMap.cs
    title: HostLeafMap (decompiled)
  - id: host-leaf-values
    resource: ksa-decomp:KSA.Kfs.Host/HostLeafValues.cs
    title: HostLeafValues (decompiled)
  - id: host-messages
    resource: ksa-decomp:KSA.Kfs.Host/HostMessages.cs
    title: HostMessages (decompiled)
  - id: message-packer
    resource: ksa-decomp:KSA.Kfs.Host/MessagePacker.cs
    title: MessagePacker (decompiled)
  - id: message-layout
    resource: ksa-decomp:KSA.Kfs.Vm.Tables/MessageLayout.cs
    title: MessageLayout (decompiled)
  - id: messages-json
    resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.messages.json
    title: messages.json (embedded resource)
  - id: vehicle-computer
    resource: ksa-decomp:KSA.Kfs.Host/VehicleComputer.cs
    title: VehicleComputer (decompiled)
  - id: kfs-vehicle-host
    resource: ksa-decomp:KSA.Kfs/KfsVehicleHost.cs
    title: KfsVehicleHost (decompiled)
  - id: kfs-host-sampler
    resource: ksa-decomp:KSA.Kfs/KfsHostSampler.cs
    title: KfsHostSampler (decompiled)
  - id: engine-model
    resource: ksa-decomp:KSA.Kfs/EngineModel.cs
    title: EngineModel (decompiled)
generated: { by: claude-code/claude-fable-5-2, at: 2026-11-25T21:30:51Z }
status: stable
ksa_build: "2026.11.25.5949"
---

# The sampling contract

```csharp
public interface IHostSampler { HostSample Sample(); }
```

[^host-sampler-interface]

`VehicleComputer.Dispatch<TSampler>(long tNs, ref TSampler sampler, bool vehicleControllable)`
calls `sampler.Sample()` **at most once per dispatch, lazily, immediately before the first tick it
runs**. If no tick is due, the sampler is not called. Every tick of that dispatch is packed from
the same `HostSample`.[^vehicle-computer]

Two implementations exist:

| implementation | where | notes |
| --- | --- | --- |
| `KfsVehicleHost.StepSampler` | game glue, private `ref struct` | holds the physics step's state by reference and calls `KfsVehicleHost.Sample(...)`; see [host sampler](/integration/host-sampler.md) |
| `VehicleComputer.FuncSampler` | host, private struct | wraps a `Func<HostSample>` for the `Dispatch(long, Func<HostSample>, bool)` overload used by `FakeVehicle` |

[^kfs-vehicle-host][^vehicle-computer]

# `HostSample`

```csharp
public sealed record HostSample
{
    public IReadOnlyDictionary<ushort, IReadOnlyDictionary<string, double>>? Values { get; init; }
    public NavTruth           Nav    { get; set; }
    public FlightDataValues   Flight { get; set; }
    public VehiclePropsValues Props  { get; set; }
    public HostLeafValues?    Leaves { get; init; }

    public HostSample(NavTruth Nav, FlightDataValues Flight, VehiclePropsValues Props,
                      IReadOnlyDictionary<ushort, IReadOnlyDictionary<string, double>>? Values = null);

    public bool Carries(ushort id);
    public static HostSample FromTruth(in TruthSample s, VehiclePropsValues? props = null);
}
```

[^host-sample]

A sample has **three layers**, applied in this order when a message is packed
(`HostMessages.PackInto`), each overwriting the previous one where they overlap:[^host-messages]

| order | layer | type | covers | used by the game |
| --- | --- | --- | --- | --- |
| 1 | typed values | `Nav`, `Flight`, `Props` | part of 0x0101, 0x0102, 0x0401 | yes, always |
| 2 | `Values` | message id → leaf path → `double` | any field of any message being packed | `Not active in this build:` the game never sets it |
| 3 | `Leaves` | `HostLeafValues` (pre-resolved handles, byte masks) | any field of the seven host messages | yes, when `WantsHostLeaves` |

`Nav`, `Flight` and `Props` have setters because the game reuses one `HostSample` instance per
controller set when there are no leaves; a sample with leaves is allocated fresh each time.[^kfs-vehicle-host]

`Carries(id)` decides whether a host message is delivered this tick:

- ids 0x0101, 0x0102, 0x0401 (everything that is not "MS-G3"): always `true`;
- ids 0x0402–0x0405 (`HostMessages.IsMsG3`): `true` only if `Values` has an entry for the id or
  `Leaves.Has(id)`.

`FromTruth` builds a test sample: `NavTruth.Zero` with `Rates` and `Q` from the `TruthSample`,
`FlightDataValues.Zero`, and the given props or `VehiclePropsValues.Empty(0)`.

# The typed value records

All are `public readonly record struct`s in `KSA.Kfs.Host`.[^value-records]

```csharp
Vec3d(double X, double Y, double Z)              // Length, Zero, operator -, operator +, Dot, Cross
Quatd(double X, double Y, double Z, double W)    // Identity = (0, 0, 0, 1)

NavTruth(Vec3d R, Vec3d V, Quatd Q, Vec3d Rates, Vec3d Accel, uint ParentId, bool Valid = true)
    // Zero = (Zero, Zero, Identity, Zero, Zero, 0)

TruthSample(float RateX, float RateY, float RateZ, float QX, float QY, float QZ, float QW)
    // Zero = (0, 0, 0, 0, 0, 0, 1)

FlightDataValues(double AltitudeM, double RadarAltitudeM, double ApoapsisM, double PeriapsisM,
    double TimeToApoapsisS, double TimeToPeriapsisS, double SpeedMps, double SurfaceSpeedMps,
    double VerticalSpeedMps, Quatd QHelmToBody, bool TargetValid, Vec3d TargetPositionHelmM,
    Vec3d TargetVelocityHelmMps, double TargetRangeM, double TargetSpeedRelMps,
    bool InAtmosphere, double AmbientPressurePa)
    // Zero = all zero, QHelmToBody = Identity

VehiclePropsValues(double MassKg, double[] InertiaRowMajor, Vec3d ComBodyM,
    ThrusterProps[] Thrusters, EngineProps[] Engines, GimbalProps[] Gimbals)
    // static Empty(int thrusters, int engines = 0, int gimbals = 0)

ThrusterProps(Vec3d ForceBodyN, Vec3d TorqueBodyNm, double MinPulseS, bool PropellantOk)
EngineProps(double MaxThrustN, double MinThrottle, double IspS, bool Active, bool PropellantOk)
GimbalProps(double RangeYRad, double RangeZRad)
```

How each maps onto message bytes (`HostMessages.PackNav` / `PackFlight` / `PackProps`):[^host-messages]

| record member | message field | conversion |
| --- | --- | --- |
| `NavTruth.Valid` | `MSG_NAV_STATE.valid` | bool → u32 0/1 |
| `NavTruth.ParentId` | `parent_id` | u32; the game passes the CRC-32 of the parent body's id |
| *(argument `soiChanges`)* | `soi_changes` | always `0`: `VehicleComputer.PackKept` passes `0u` |
| `NavTruth.R`, `.V` | `r_cci_m`, `v_cci_mps` | `float64` × 3 |
| `NavTruth.Q` | `q_body_to_cci` | `float32` × 4 (x, y, z, w) |
| `NavTruth.Rates`, `.Accel` | `rates_body_rps`, `accel_body_mps2` | `float32` × 3 |
| `FlightDataValues.*` (17 members) | the first 17 fields of `MSG_FLIGHT_DATA`, `altitude_m` … `ambient_pressure_pa` | doubles narrowed to `float32`; bools → u32 |
| `VehiclePropsValues.MassKg` | `mass_kg` | `float32` |
| `InertiaRowMajor[3·r + c]` | `I_body_kgm2.m[r][c]` | `float32` |
| `ComBodyM` | `com_body_m` | `float32` × 3 |
| `Thrusters[i]` | `thruster[i].force_body_N`, `.torque_body_Nm`, `.min_pulse_s`, `.propellant_ok` | |
| `Engines[i]` | `engine[i].max_thrust_N`, `.min_throttle`, `.isp_s`, `.active`, `.propellant_ok` | |
| `Gimbals[i]` | `gimbal[i].range_y_rad`, `.range_z_rad` | |

`PackProps` throws `ArgumentException("MSG_VEHICLE_PROPS: N <group> element(s) given; the layout has M (P-130)")`
when an array length differs from the layout's element count.

# Leaves

A **leaf** is one scalar inside a message slot, addressed by a dotted path such as
`target_position_helm_m.x`, `thruster[3].control_map` or `engine[0].lut_gamma[17]`. Leaf paths come
from `MessageLayout.Build`: vectors expand to `.x .y .z` (and `.w`), matrices to `.m[r][c]`,
per-vehicle arrays to `group[i].member`, fixed arrays inside an element to `member[j]` (or
`member[j].x`).[^message-layout]

Leaf types and their wire size:

| leaf type | bytes | written from a `double` as |
| --- | --- | --- |
| `float32` | 4 | `(float)v` |
| `float64` | 8 | `v` |
| `uint32` | 4 | `(uint)v` |
| `int32` | 4 | `(int)v` |
| `uint16` | 2 | `(ushort)v` |
| `int64` | 8 | `(long)v` |
| `bool` | 4 | `v != 0 ? 1u : 0u` |

Only `bool`, `uint32`, `int64`, `float32` and `float64` occur in message bodies in this catalogue;
`uint16` occurs only in the header. Integer conversions run in a `checked` context, so a value
outside the target range (or `NaN`) throws `OverflowException`.[^host-leaf-values][^message-packer]

## `HostLeafMap`

```csharp
public sealed class HostLeafMap
{
    public IoLayout   Layout { get; }
    public GameTables Tables { get; }
    public HostLeafMap(GameTables tables, IoLayout layout);
    public int Handle(ushort id, string path);      // throws KeyNotFoundException if absent
    public int TryHandle(ushort id, string path);   // -1 if absent
}
```

The constructor builds the layout of every message in `HostMessages.MsG3Order`
(`0x0101, 0x0102, 0x0401, 0x0402, 0x0403, 0x0404, 0x0405`) for the given I/O layout and indexes
their leaves by path; **no other message can have leaves**. `TryHandle` returns a small integer
handle (allocated on first use and cached per `(id, path)`), `-1` when the message has no such
field, and throws `ArgumentException("0x<ID> is not a host message")` for any other message id. The
resolved table (`Leaf(int Message, int Offset, string Type)`, the `Leaves` and `Messages` lists) is
`internal`.[^host-leaf-map]

One map is built per controller set (per I/O layout) and shared by all samples.

## `HostLeafValues`

```csharp
public sealed class HostLeafValues
{
    public HostLeafMap Map { get; }
    public HostLeafValues(HostLeafMap map);
    public void Carry(ushort id);                     // deliver the message even with no field set
    public bool Has(ushort id);
    public void Set(int handle, double v);            // handle < 0 is ignored
    public void Set(int handle, bool v);
    public void SetTimeNs(int handle, long absNs);    // int64 leaves only
    public void Apply(ushort id, byte[] slot, long epochNs);
    public static long Us(long absNs, long epochNs);
}
```

[^host-leaf-values]

- One instance holds, per host message, a data buffer and a byte mask of the slot's size. `Set`
  writes the value at the leaf's offset, marks its bytes in the mask and marks the message as
  carried. A negative handle is a silent no-op, which is how fields absent from a layout (for
  example `engine[0].…` on a vehicle without engines) are skipped.
- `SetTimeNs` records an absolute simulation time for an `int64` leaf (any other type throws
  `InvalidOperationException("a time leaf is int64")`); the conversion to microseconds since the
  computer's reset happens in `Apply`, when the epoch (`Grid.ResetNs`) is known.
- `Apply` copies only masked bytes, and only from offset 16 on — **a leaf can never change the
  message header** — then writes the time leaves with `Us(absNs, epochNs)`.

# When the host asks for what

| part | computed when | decided by |
| --- | --- | --- |
| `Nav`, `Props` | every sample | always |
| `Flight` | `VehicleComputer.FlightDataWanted` — the image subscribes to 0x0102 and does not publish it; otherwise `FlightDataValues.Zero` | `VehicleComputer.Load` |
| `Leaves` | `VehicleComputer.WantsHostLeaves` — the image subscribes to at least one of 0x0402–0x0405 | `VehicleComputer.Load` |

[^vehicle-computer][^kfs-vehicle-host]

The game prepares the leaf sampler from `VehicleComputer.ImageLoaded` (`PrepareSampler`), and builds
it lazily otherwise.

**A consequence worth knowing.** Fields of `MSG_FLIGHT_DATA` and `MSG_VEHICLE_PROPS` that are marked
"leaf" below are filled *only* by the leaf layer. A program that subscribes to those two messages
but to none of `MSG_BURN_STATUS`, `MSG_PILOT_INPUT`, `MSG_FC_SETTINGS`, `MSG_BURN_PLAN` gets no
leaves, and those fields are zero (the slot is cleared before packing). Nothing adds such a
subscription implicitly: subscriptions come only from a program's own `#pragma kfs subscribe` (kC)
or `.subscribe` (kASM) lines, and outside the example programs and the data tables the four message
names occur in the decompiled code only in `MessageLayout.HostPublished`.[^message-layout] The stock
`kC_attitude_control/prograde_hold.c` example is such a program (of the host messages it subscribes
only to `MSG_NAV_STATE` and `MSG_VEHICLE_PROPS`) and reads typed fields only. See
[kC generated headers](/language/kc-generated-headers.md).

# Every field of the seven host messages

Offsets are byte offsets in the message slot (the 16-byte header first). They are computed with the
rules of `MessageLayout.Build` from `messages.json` and reproduce `LayoutGenerator.PropsBodySize`.
The "written by" column says which layer fills the field in the game:
**typed** = `PackNav`/`PackFlight`/`PackProps`; **leaf** = a handle set by `KfsHostSampler`
(the values themselves are documented in [host sampler](/integration/host-sampler.md)).[^messages-json][^host-messages][^kfs-host-sampler][^engine-model]

Header, common to every message:

| leaf | type | offset | host value |
| --- | --- | --- | --- |
| `hdr.seq` | `uint32` | 0 | `k + 1` (never 0) |
| `hdr.source` | `uint16` | 4 | `0xFFFF` |
| `hdr.flags` | `uint16` | 6 | `1` |
| `hdr.t_us` | `int64` | 8 | `k · 1 000 000 / tick_hz` |

## `MSG_NAV_STATE` — 0x0101 (257), 120 bytes

| field | catalogue type | offset | leaf paths | leaf type | written by |
| --- | --- | --- | --- | --- | --- |
| `valid` | `bool` | 16 | `valid` | `bool` | typed |
| `parent_id` | `uint32` | 20 | `parent_id` | `uint32` | typed |
| `soi_changes` | `uint32` | 24 | `soi_changes` | `uint32` | typed |
| `r_cci_m` | `dvec3` | 32 | `r_cci_m` + `.x .y .z` | `float64` | typed |
| `v_cci_mps` | `dvec3` | 56 | `v_cci_mps` + `.x .y .z` | `float64` | typed |
| `q_body_to_cci` | `quat` | 80 | `q_body_to_cci` + `.x .y .z .w` | `float32` | typed |
| `rates_body_rps` | `vec3` | 96 | `rates_body_rps` + `.x .y .z` | `float32` | typed |
| `accel_body_mps2` | `vec3` | 108 | `accel_body_mps2` + `.x .y .z` | `float32` | typed |

## `MSG_FLIGHT_DATA` — 0x0102 (258), 192 bytes

| field | catalogue type | offset | leaf paths | leaf type | written by |
| --- | --- | --- | --- | --- | --- |
| `altitude_m` | `float` | 16 | `altitude_m` | `float32` | typed |
| `radar_altitude_m` | `float` | 20 | `radar_altitude_m` | `float32` | typed |
| `apoapsis_m` | `float` | 24 | `apoapsis_m` | `float32` | typed |
| `periapsis_m` | `float` | 28 | `periapsis_m` | `float32` | typed |
| `time_to_apoapsis_s` | `float` | 32 | `time_to_apoapsis_s` | `float32` | typed |
| `time_to_periapsis_s` | `float` | 36 | `time_to_periapsis_s` | `float32` | typed |
| `speed_mps` | `float` | 40 | `speed_mps` | `float32` | typed |
| `surface_speed_mps` | `float` | 44 | `surface_speed_mps` | `float32` | typed |
| `vertical_speed_mps` | `float` | 48 | `vertical_speed_mps` | `float32` | typed |
| `q_helm_to_body` | `quat` | 52 | `q_helm_to_body` + `.x .y .z .w` | `float32` | typed, then leaf |
| `target_valid` | `bool` | 68 | `target_valid` | `bool` | typed, then leaf |
| `target_position_helm_m` | `vec3` | 72 | `target_position_helm_m` + `.x .y .z` | `float32` | typed, then leaf |
| `target_velocity_helm_mps` | `vec3` | 84 | `target_velocity_helm_mps` + `.x .y .z` | `float32` | typed, then leaf |
| `target_range_m` | `float` | 96 | `target_range_m` | `float32` | typed, then leaf |
| `target_speed_rel_mps` | `float` | 100 | `target_speed_rel_mps` | `float32` | typed, then leaf |
| `in_atmosphere` | `bool` | 104 | `in_atmosphere` | `bool` | typed |
| `ambient_pressure_pa` | `float` | 108 | `ambient_pressure_pa` | `float32` | typed, then leaf |
| `q_parent_fixed_to_cci` | `quat` | 112 | `q_parent_fixed_to_cci` + `.x .y .z .w` | `float32` | leaf |
| `parent_spin_cci_rps` | `vec3` | 128 | `parent_spin_cci_rps` + `.x .y .z` | `float32` | leaf |
| `target_dock_valid` | `bool` | 140 | `target_dock_valid` | `bool` | leaf |
| `target_q_dock_to_cci` | `quat` | 144 | `target_q_dock_to_cci` + `.x .y .z .w` | `float32` | leaf |
| `target_dock_rates_cci_rps` | `vec3` | 160 | `target_dock_rates_cci_rps` + `.x .y .z` | `float32` | leaf |
| `parent_mass_kg` | `float` | 172 | `parent_mass_kg` | `float32` | leaf |
| `q_parent_cce_to_cci` | `quat` | 176 | `q_parent_cce_to_cci` + `.x .y .z .w` | `float32` | leaf |

## `MSG_VEHICLE_PROPS` — 0x0401 (1025), 120 bytes (fixed part)

| field | catalogue type | offset | leaf paths | leaf type | written by |
| --- | --- | --- | --- | --- | --- |
| `mass_kg` | `float` | 16 | `mass_kg` | `float32` | typed, then leaf |
| `I_body_kgm2` | `mat3` | 20 | `I_body_kgm2.m[r][c]`, r, c ∈ 0..2 | `float32` | typed, then leaf |
| `com_body_m` | `vec3` | 56 | `com_body_m` + `.x .y .z` | `float32` | typed, then leaf |
| `max_g_load` | `float` | 68 | `max_g_load` | `float32` | leaf |
| `ang_accel_body_rps2` | `vec3` | 72 | `ang_accel_body_rps2` + `.x .y .z` | `float32` | leaf |
| `rcs_torque_ctrl_Nm` | `vec3` | 84 | `rcs_torque_ctrl_Nm` + `.x .y .z` | `float32` | leaf |
| `rcs_min_impulse_ctrl_Nms` | `vec3` | 96 | `rcs_min_impulse_ctrl_Nms` + `.x .y .z` | `float32` | leaf |
| `rcs_thrust_from_rotation_N` | `vec3` | 108 | `rcs_thrust_from_rotation_N` + `.x .y .z` | `float32` | leaf |

### `thruster[i]` element — 84 bytes

| member | type | offset in element | leaf type | written by |
| --- | --- | --- | --- | --- |
| `force_body_N` | `vec3` | 0 | `float32` | typed |
| `torque_body_Nm` | `vec3` | 12 | `float32` | typed |
| `min_pulse_s` | `float` | 24 | `float32` | typed, then leaf |
| `propellant_ok` | `bool` | 28 | `bool` | typed, then leaf |
| `control_map` | `uint32` | 32 | `uint32` | leaf |
| `torque_ctrl_Nm` | `vec3` | 36 | `float32` | leaf |
| `force_ctrl_N` | `vec3` | 48 | `float32` | leaf |
| `torque_fraction` | `vec3` | 60 | `float32` | leaf |
| `force_fraction` | `vec3` | 72 | `float32` | leaf |

### `engine[i]` element — 1120 bytes

| member | type | offset in element | leaf type | written by |
| --- | --- | --- | --- | --- |
| `max_thrust_N` | `float` | 0 | `float32` | typed, then leaf |
| `min_throttle` | `float` | 4 | `float32` | typed, then leaf |
| `isp_s` | `float` | 8 | `float32` | typed |
| `active` | `bool` | 12 | `bool` | typed, then leaf |
| `propellant_ok` | `bool` | 16 | `bool` | typed, then leaf |
| `mass_flow_kgps` | `float` | 20 | `float32` | leaf |
| `min_thrust_N` | `float` | 24 | `float32` | leaf |
| `min_mass_flow_kgps` | `float` | 28 | `float32` | leaf |
| `cores` | `uint32` | 32 | `uint32` | leaf |
| `nozzles` | `uint32` | 36 | `uint32` | leaf |
| `core_kind` | `uint32[2]` | 40 | `uint32` | leaf |
| `core_min_throttle` | `float[2]` | 48 | `float32` | leaf |
| `core_p_max_pa` | `float[2]` | 56 | `float32` | leaf |
| `core_efficiency` | `float[2]` | 64 | `float32` | leaf |
| `core_temperature_k` | `float[2]` | 72 | `float32` | leaf |
| `core_row_interp` | `float[2]` | 80 | `float32` | leaf |
| `core_points` | `uint32[2]` | 88 | `uint32` | leaf |
| `lut_ln_p` | `float[32]` | 96 | `float32` | leaf |
| `lut_temperature_k` | `float[32]` | 224 | `float32` | leaf |
| `lut_gamma` | `float[32]` | 352 | `float32` | leaf |
| `lut_r_jpkgk` | `float[32]` | 480 | `float32` | leaf |
| `lut_gamma_hi` | `float[32]` | 608 | `float32` | leaf |
| `lut_r_hi_jpkgk` | `float[32]` | 736 | `float32` | leaf |
| `nozzle_core` | `uint32[8]` | 864 | `uint32` | leaf |
| `nozzle_throat_m2` | `float[8]` | 896 | `float32` | leaf |
| `nozzle_exit_m2` | `float[8]` | 928 | `float32` | leaf |
| `nozzle_flow_eff` | `float[8]` | 960 | `float32` | leaf |
| `nozzle_expansion_eff` | `float[8]` | 992 | `float32` | leaf |
| `nozzle_dir_body` | `vec3[8]` | 1024 | `float32` | leaf |

### `gimbal[i]` element — 76 bytes

| member | type | offset in element | leaf type | written by |
| --- | --- | --- | --- | --- |
| `range_y_rad` | `float` | 0 | `float32` | typed, then leaf |
| `range_z_rad` | `float` | 4 | `float32` | typed, then leaf |
| `active` | `bool` | 8 | `bool` | leaf |
| `thrust_N` | `float` | 12 | `float32` | leaf |
| `max_thrust_N` | `float` | 16 | `float32` | leaf |
| `pivot_body_m` | `vec3` | 20 | `float32` | leaf |
| `dir0_gimbal` | `vec3` | 32 | `float32` | leaf |
| `offset_gimbal_m` | `vec3` | 44 | `float32` | leaf |
| `q_gimbal_to_body` | `quat` | 56 | `float32` | leaf |
| `circular` | `bool` | 72 | `bool` | leaf |

## `MSG_BURN_STATUS` — 0x0402 (1026), 56 bytes

| field | catalogue type | offset | leaf paths | leaf type | written by |
| --- | --- | --- | --- | --- | --- |
| `burn` | `uint32` | 16 | `burn` | `uint32` | leaf |
| `state` | `uint32` | 20 | `state` | `uint32` | leaf |
| `t_ignition_us` | `int64` | 24 | `t_ignition_us` | `int64` | leaf |
| `dv_remaining_mps` | `float` | 32 | `dv_remaining_mps` | `float32` | leaf |
| `dv_vlf_mps` | `vec3` | 36 | `dv_vlf_mps` + `.x .y .z` | `float32` | leaf |
| `end_cause` | `uint32` | 48 | `end_cause` | `uint32` | leaf |

## `MSG_PILOT_INPUT` — 0x0403 (1027), 64 bytes

| field | catalogue type | offset | leaf paths | leaf type | written by |
| --- | --- | --- | --- | --- | --- |
| `rotate` | `vec3` | 16 | `rotate` + `.x .y .z` | `float32` | leaf |
| `translate` | `vec3` | 28 | `translate` + `.x .y .z` | `float32` | leaf |
| `throttle` | `float` | 40 | `throttle` | `float32` | leaf |
| `throttle_active` | `bool` | 44 | `throttle_active` | `bool` | leaf |
| `engine_on` | `bool` | 48 | `engine_on` | `bool` | leaf |
| `stage_presses` | `uint32` | 52 | `stage_presses` | `uint32` | leaf |
| `kill_switch` | `bool` | 56 | `kill_switch` | `bool` | — (stays 0) |

## `MSG_FC_SETTINGS` — 0x0404 (1028), 88 bytes

| field | catalogue type | offset | leaf paths | leaf type | written by |
| --- | --- | --- | --- | --- | --- |
| `burn_mode` | `uint32` | 16 | `burn_mode` | `uint32` | leaf |
| `attitude_mode` | `uint32` | 20 | `attitude_mode` | `uint32` | leaf |
| `frame` | `uint32` | 24 | `frame` | `uint32` | leaf |
| `track_target` | `uint32` | 28 | `track_target` | `uint32` | leaf |
| `custom_target_rad` | `vec3` | 32 | `custom_target_rad` + `.x .y .z` | `float32` | leaf |
| `manual_thrust_mode` | `uint32` | 44 | `manual_thrust_mode` | `uint32` | leaf |
| `roll` | `uint32` | 48 | `roll` | `uint32` | leaf |
| `rcs_enabled` | `bool` | 52 | `rcs_enabled` | `bool` | leaf |
| `angle_deadband_rad` | `float` | 56 | `angle_deadband_rad` | `float32` | leaf |
| `rate_limit_rps` | `float` | 60 | `rate_limit_rps` | `float32` | leaf |
| `sim_speed` | `float` | 64 | `sim_speed` | `float32` | leaf |
| `min_fc_dt_s` | `float` | 68 | `min_fc_dt_s` | `float32` | leaf |
| `service_throttle` | `float` | 72 | `service_throttle` | `float32` | leaf |
| `service_throttle_valid` | `bool` | 76 | `service_throttle_valid` | `bool` | leaf |
| `service_engine_on` | `bool` | 80 | `service_engine_on` | `bool` | leaf |
| `service_engine_valid` | `bool` | 84 | `service_engine_valid` | `bool` | leaf |

## `MSG_BURN_PLAN` — 0x0405 (1029), 160 bytes

| field | catalogue type | offset | leaf paths | leaf type | written by |
| --- | --- | --- | --- | --- | --- |
| `valid` | `bool` | 16 | `valid` | `bool` | leaf |
| `burn` | `uint32` | 20 | `burn` | `uint32` | — (stays 0) |
| `t_impulse_us` | `int64` | 24 | `t_impulse_us` | `int64` | leaf |
| `t_planned_ignition_us` | `int64` | 32 | `t_planned_ignition_us` | `int64` | leaf |
| `planned_ignition_valid` | `bool` | 40 | `planned_ignition_valid` | `bool` | leaf |
| `planned_throttle_valid` | `bool` | 44 | `planned_throttle_valid` | `bool` | leaf |
| `fixed_duration_valid` | `bool` | 48 | `fixed_duration_valid` | `bool` | leaf |
| `later_stage` | `bool` | 52 | `later_stage` | `bool` | leaf |
| `r_burn_cci_m` | `dvec3` | 56 | `r_burn_cci_m` + `.x .y .z` | `float64` | leaf |
| `dv_target_cci_mps` | `vec3` | 80 | `dv_target_cci_mps` + `.x .y .z` | `float32` | leaf |
| `dv_accum_cci_mps` | `vec3` | 92 | `dv_accum_cci_mps` + `.x .y .z` | `float32` | leaf |
| `q_burn_body_to_cci` | `quat` | 104 | `q_burn_body_to_cci` + `.x .y .z .w` | `float32` | leaf |
| `planned_throttle` | `float` | 120 | `planned_throttle` | `float32` | leaf |
| `fixed_duration_s` | `float` | 124 | `fixed_duration_s` | `float32` | leaf |
| `speed_above_stop_mps` | `float` | 128 | `speed_above_stop_mps` | `float32` | leaf |
| `burn_started` | `bool` | 132 | `burn_started` | `bool` | leaf |
| `burn_throttle_valid` | `bool` | 136 | `burn_throttle_valid` | `bool` | leaf |
| `last_ignition_denied` | `bool` | 140 | `last_ignition_denied` | `bool` | leaf |
| `t_started_us` | `int64` | 144 | `t_started_us` | `int64` | leaf |
| `burn_throttle` | `float` | 152 | `burn_throttle` | `float32` | leaf |

Element placement inside `MSG_VEHICLE_PROPS`, for `T` thrusters, `E` engines, `G` gimbals: after
the 120 fixed bytes come `thruster[i]` at `120 + 84·i`, then `engine[j]` at
`120 + 84·T + 1120·j`, then `gimbal[k]` at `120 + 84·T + 1120·E + 76·k`; the slot size is that
total rounded up to 8. A group with a count of zero is omitted from the message entirely. Arrays
inside an element expand to leaves `member[c]` (`engine[i].core_kind[0]`,
`engine[i].nozzle_dir_body[7].z`). Slot sizes for the bench-shaped vehicle (12 thrusters, 1 engine,
1 gimbal): 2 328 bytes for `MSG_VEHICLE_PROPS`; the other six are fixed.

Notes on the table:

- `MSG_NAV_STATE.soi_changes` is always 0 in this build.
- `MSG_PILOT_INPUT.kill_switch` and `MSG_BURN_PLAN.burn` have no writer and are always 0.
- `engine[i].isp_s` is written only by the typed layer.
- Fields marked "typed, then leaf" are written twice when leaves are present; the leaf value wins.
  The two layers do not always use the same game source (for example `mass_kg` comes from
  `VehicleProperties.TotalMass` in the typed layer and from the flight computer's
  `TotalMassPropsBody.Mass` in the leaf layer).[^kfs-vehicle-host][^kfs-host-sampler]
- The other three catalogue messages — `MSG_ATT_TARGET` (0x0201, 96 bytes), `MSG_GUIDANCE_STATUS`
  (0x0202, 224 bytes) and `MSG_WRENCH_CMD` (0x0301, 112 bytes) — are never published by the host;
  see [messages table](/tables/messages-table.md) for their fields.

# Modding notes

- Everything a mod needs to *fill existing fields* is public: `HostLeafMap.TryHandle`,
  `HostLeafValues.Set`/`SetTimeNs`/`Carry`, `HostSample.Leaves`/`Values` (init-only), the value
  records. `KfsHostSampler.Sample(...)` is a public instance method returning the `HostLeafValues`,
  so a Harmony postfix on it can set additional leaves on `__result` (`Unverified:` this patch has
  not been exercised).
- The set of messages that can carry leaves is fixed by the static array
  `HostMessages.MsG3Order` and the range test `HostMessages.IsMsG3` (1026–1029). A new message id
  is rejected by `HostLeafMap.TryHandle` and by `HostMessages.PackInto`.
- `HostSample.Values` is an unused, fully generic second channel (id → path → double) that is
  honoured by `PackInto` for any id it is asked to pack; it still only applies to ids the computer
  decides to pack (`VehicleComputer.HostPublished`).
- Adding a field to an existing host message row (`GameTables.MessagesData.Messages`) makes it
  addressable as a leaf with no host change, but it changes the message's size and therefore the
  `body_size` recorded in every image that subscribes to or publishes that message; such images
  compiled before the change fail to load.
- A new per-vehicle array needs an element count, and that comes only from command-field names in
  the I/O layout (see [I/O layout generation](/host/io-layout-generation.md)).
- Samples are taken on the dispatch thread from physics-step state. Game objects a mod reads there
  must be safe to read off the main thread; see
  [dispatch and threading](/integration/dispatch-and-threading.md).

[^host-sample]: HostSample.cs — properties and constructor :6–25; `Carries` :27–38; `FromTruth` :40–47.
[^host-sampler-interface]: IHostSampler.cs:3–6.
[^value-records]: NavTruth.cs:3–6; TruthSample.cs:3–6; FlightDataValues.cs:3–6; VehiclePropsValues.cs:5–14; ThrusterProps.cs:3; EngineProps.cs:3; GimbalProps.cs:3; Vec3d.cs:5–30; Quatd.cs:3–6.
[^host-leaf-map]: HostLeafMap.cs — internal `Leaf` and lists :9–19; constructor :25–44; `Handle` :46–54; `TryHandle` :56–75.
[^host-leaf-values]: HostLeafValues.cs — constructor :24–30; `Carry`/`Has` :32–49; `Set` :51–100; `SetTimeNs` :102–115; `Apply` :117–140; `Us` :142–154.
[^host-messages]: HostMessages.cs — field offsets resolved by path (`NavAt`, `FlightAt`, `PropsAt`) :111–279; `MsG3Order` :364; `IsMsG3` :378–385; `Seq`/`MetUs` :403–416; `PackInto` layer order :430–471; `PackNav` :473–483; `PackFlight` :485–504; `PackProps` and `Check` :506–551.
[^message-packer]: MessagePacker.cs — `WriteLeaves` :22–60.
[^message-layout]: MessageLayout.cs — `HostPublished` :27; `TypeShape` :137–151; `Element` :169–228; `Build` :230–314. The kC pragma is parsed at KSA.Kfs.KC/Parser.cs:532–540.
[^messages-json]: messages.json (whole file, catalogue version 4).
[^vehicle-computer]: VehicleComputer.cs — `FuncSampler` :17–25; `FlightDataWanted`/`WantsHostLeaves` set at `Load` :336–343; sample taken once per dispatch :496–501; `PackKept` passes `soiChanges` 0 :575–583.
[^kfs-vehicle-host]: KfsVehicleHost.cs — `StepSampler` :21–35; `PrepareSampler`/`SamplerFor` :120–137; `Sample` :281–295; `SampleFor` (reused sample without leaves, fresh sample with leaves) :362–426.
[^kfs-host-sampler]: KfsHostSampler.cs — leaf handles registered in the constructor :209–313; `Sample` :411–613; `BurnStatus` :615–672.
[^engine-model]: EngineModel.cs — `Leaves(int i)` :181–214.
