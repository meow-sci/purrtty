---
type: Subsystem Reference
title: Host sampler — game sources of every host-published value
description: Every value KfsVehicleHost and KfsHostSampler put into the host-published messages, the exact game expression each one comes from, and the conditions under which each group is filled.
resource: ksa-decomp:KSA.Kfs/KfsHostSampler.cs
tags: [kfs, integration, sampling, messages, telemetry]
sources:
  - id: kfs-host-sampler
    resource: ksa-decomp:KSA.Kfs/KfsHostSampler.cs
    title: KfsHostSampler (decompiled)
  - id: kfs-vehicle-host
    resource: ksa-decomp:KSA.Kfs/KfsVehicleHost.cs
    title: KfsVehicleHost (decompiled)
  - id: engine-model
    resource: ksa-decomp:KSA.Kfs/EngineModel.cs
    title: EngineModel (decompiled)
  - id: host-messages
    resource: ksa-decomp:KSA.Kfs.Host/HostMessages.cs
    title: HostMessages (decompiled)
  - id: host-leaf-values
    resource: ksa-decomp:KSA.Kfs.Host/HostLeafValues.cs
    title: HostLeafValues and HostLeafMap (decompiled)
  - id: host-sample
    resource: ksa-decomp:KSA.Kfs.Host/HostSample.cs
    title: HostSample (decompiled)
  - id: vehicle-computer
    resource: ksa-decomp:KSA.Kfs.Host/VehicleComputer.cs
    title: VehicleComputer (decompiled)
  - id: messages-json
    resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.messages.json
    title: messages.json (embedded table)
  - id: flight-computer-navigation
    resource: ksa-decomp:KSA/FlightComputerNavigation.cs
    title: FlightComputerNavigation (decompiled)
  - id: physics-bubble
    resource: ksa-decomp:KSA/PhysicsBubble.cs
    title: PhysicsBubble (decompiled)
generated: { by: claude-code/claude-fable-5-2, at: 2026-11-25T21:37:08Z }
status: stable
ksa_build: "2026.11.25.5949"
---

# Overview

The host publishes seven messages to a program (`HostMessages.MsG3Order`).[^host-messages] Field names below are those of the embedded
message catalogue; every leaf path the sampler asks for exists in it.[^messages-json] Their values come from two code paths:

| Path | Class | Feeds | Runs when |
| --- | --- | --- | --- |
| **Typed sample** | `KfsVehicleHost.Sample` / `FlightData` / `SampleFor` → `NavTruth`, `FlightDataValues`, `VehiclePropsValues` → `HostMessages.PackNav/PackFlight/PackProps` | `MSG_NAV_STATE` (0x0101 = 257), the base fields of `MSG_FLIGHT_DATA` (0x0102 = 258) and of `MSG_VEHICLE_PROPS` (0x0401 = 1025) | Every dispatch in which a tick runs. `FlightDataValues` is computed only if `Computer.FlightDataWanted` (the image subscribes to 258), else it is `FlightDataValues.Zero`.[^kfs-vehicle-host][^vehicle-computer] |
| **Leaf sample** | `KfsHostSampler.Sample` → `HostLeafValues` (per-leaf byte patches applied over the packed slot) | The extra fields of 258 and 1025, and all of `MSG_BURN_STATUS` (0x0402 = 1026), `MSG_PILOT_INPUT` (0x0403 = 1027), `MSG_FC_SETTINGS` (0x0404 = 1028), `MSG_BURN_PLAN` (0x0405 = 1029) | Only if `Computer.WantsHostLeaves` — the image subscribes to at least one of 1026-1029 (`HostMessages.IsMsG3`).[^vehicle-computer][^host-messages] |

Consequence, read directly from the code: **a program that subscribes only to `MSG_FLIGHT_DATA` and/or `MSG_VEHICLE_PROPS` gets zeros in every
field the leaf sampler owns** (for example `q_parent_fixed_to_cci`, `parent_mass_kg`, `max_g_load`, the per-thruster `control_map`, the engine
core model), because the leaf sampler is not run for it.

Packing order for one message slot (`HostMessages.PackInto`): clear the slot, write the header, write the typed fields, write any
`HostSample.Values` dictionary entries (unused by the game glue), then `sample.Leaves?.Apply(id, slot, epochNs)` — so a leaf **overrides**
a typed field at the same offset.[^host-messages]

Everything here runs on the dispatch thread, at most once per `VehicleComputer.Dispatch` (the sample is shared by all ticks run in that
dispatch).[^vehicle-computer] `nav` is the `FlightComputerNavigation` the physics step built for this pass: from physics states in full
physics, from analytic orbit states for a single on-rails vehicle; `nav.PositionCci` is the **control point** (helm origin), not the
centre of mass (`FlightComputerNavigation.ApplyHelmOrigin`).[^flight-computer-navigation] `fc` is the worker copy of the flight computer,
`vs` the `VehicleUpdateState`, `output` the `FlightComputerOutput`.

# How the leaf sampler is built

`KfsHostSampler(KfsVehicleHost host, KfsControllerSet set, HostLeafMap map)` resolves every leaf it will write to an integer handle once,
with `_map.TryHandle(messageId, path)`; a path missing from the layout yields `-1` and every later `Set` on that handle is a no-op
(`HostLeafValues.Set` checks `handle >= 0`).[^kfs-host-sampler][^host-leaf-values] Vector leaves use `.x/.y/.z`, quaternions `.x/.y/.z/.w`, the inertia
matrix `I_body_kgm2.m[r][c]`, per-element fields `thruster[i].…`, `engine[i].…`, `gimbal[i].…` (invariant-culture indices).

`Sample(VehicleUpdateState vs, in FlightComputerNavigation nav, in ManualControlInputs inputs, in FlightComputerOutput output, FlightComputer fc, double simulationSpeed, float maxFlightComputerRate)`
allocates a fresh `HostLeafValues` and first marks 1026-1029 as carried, so those four messages are published every tick even if nothing
was set.[^kfs-host-sampler] `inputs` is `PhysicsBubble.FlightComputerInputsFor(vs)` — the pilot inputs with thruster flags cleared for a kitten
that is not in MMU mode.[^physics-bubble]

# MSG_NAV_STATE (257) — typed path only

| Field | Source |
| --- | --- |
| `valid` | `true` (`NavTruth.Valid` default) |
| `parent_id` | `Crc32.Compute(Encoding.UTF8.GetBytes(nav.Parent.Id))` (`KfsVehicleHost.ParentId`) |
| `soi_changes` | `0` — `VehicleComputer.PackKept` passes `soiChanges: 0u` |
| `r_cci_m` | `nav.PositionCci` |
| `v_cci_mps` | `nav.VelocityCci` |
| `q_body_to_cci` | `nav.Body2Cci` |
| `rates_body_rps` | `nav.BodyRates` |
| `accel_body_mps2` | `Vehicle.KinematicMeasurements.AccelerationBody` — the live vehicle's last *published* measurement, i.e. the previous frame's average |

Source: `KfsVehicleHost.Sample` (`KfsVehicleHost.cs:281-295`), `HostMessages.PackNav`.[^kfs-vehicle-host][^host-messages]

# MSG_FLIGHT_DATA (258)

## Typed path (`KfsVehicleHost.FlightData`, `KfsVehicleHost.cs:297-360`)

With `r = nav.PositionCci`, `v = nav.VelocityCci`, `parent = nav.Parent`, `R = parent.MeanRadius`:

| Field | Source |
| --- | --- |
| `altitude_m` | `r.Length() - R` |
| `radar_altitude_m` | If `parent is Celestial` and `|r| > 0`: `|r| - (R + celestial.GetTerrainHeightFromDirCcf((r/|r|).Transform(celestial.GetCci2Ccf(nav.Time))))`; otherwise equal to `altitude_m` |
| `apoapsis_m` | `+∞` unless the state is a valid orbit and `orbit.IsBound()`: then `orbit.Apoapsis - R` |
| `periapsis_m` | `NaN` unless the state is a valid orbit: then `orbit.Periapsis - R` |
| `time_to_apoapsis_s` | `+∞` unless bound and `orbit.GetNextApoapsisTime(nav.Time)` has a value: `(t - nav.Time).Seconds()` |
| `time_to_periapsis_s` | `+∞` unless `orbit.GetNextPeriapsisTime(nav.Time)` has a value and it is not in the past |
| `speed_mps` | `v.Length()` |
| `surface_speed_mps` | `(v - double3.Cross(parent.GetAngularVelocityCci(), r)).Length()` |
| `vertical_speed_mps` | `|r| > 0 ? double3.Dot(v, r) / |r| : 0` |
| `q_helm_to_body` | `nav.Helm2Body` |
| `target_valid` | `nav.Target.HasValue` |
| `target_position_helm_m`, `target_velocity_helm_mps` | `(target.PositionCci - r)` and `(target.VelocityCci - v)` transformed by `Concatenate(nav.Helm2Body, nav.Body2Cci).Inverse()`; zero without a target |
| `target_range_m`, `target_speed_rel_mps` | Lengths of the two vectors; `0` without a target |
| `in_atmosphere` | `parent.GetPhysicalAtmosphere() != null && altitude < atmosphere.Height` |
| `ambient_pressure_pa` | `in_atmosphere ? atmosphere.ComputePressureAtAltitude(Math.Max(0, altitude)) : 0` |

"Valid orbit" is `Orbit.GetOrbitType(v.LengthSquared() / 2.0 - parent.Mu / |r|, parent.Mu / |r|) != Orbit.OrbitType.Invalid`; the orbit is
`Orbit.CreateFromStateCci(parent, nav.Time, r, v, default)`.

## Leaf path (`KfsHostSampler.cs:422-448`)

| Leaf | Source |
| --- | --- |
| `q_helm_to_body` | `nav.Helm2Body` |
| `ambient_pressure_pa` | `fc.AmbientPressure` (overrides the typed value) |
| `q_parent_fixed_to_cci` | `parent.GetCcf2Cci(nav.Time)` |
| `parent_spin_cci_rps` | `parent.GetAngularVelocityCci()` |
| `parent_mass_kg` | `parent.Mass` |
| `q_parent_cce_to_cci` | `parent.GetCce2Cci()` |
| `target_valid`, `target_position_helm_m`, `target_velocity_helm_mps`, `target_range_m`, `target_speed_rel_mps` | As in the typed path, set only when `nav.Target` has a value |
| `target_dock_valid` | `1` when a target is set |
| `target_q_dock_to_cci` | `target.Dock2Cci`; `(0, 0, 0, 1)` without a target |
| `target_dock_rates_cci_rps` | `target.DockFrameRatesCci` |

# MSG_VEHICLE_PROPS (1025)

## Typed path (`KfsVehicleHost.SampleFor`, `KfsVehicleHost.cs:362-408`)

`props = vs.GetReadOnlyProps()`, `com = props.CenterOfMassAsmb`, `ambient = vs.GetReadOnlyEnvironment().AtmosphericPressure`. Element `i` is
element `i` of the **frozen** controller set; an inert element (see [controller set](/integration/controller-set-and-layout-changes.md))
reports zeros and `false`.

| Field | Source |
| --- | --- |
| `mass_kg` | `props.TotalMass` |
| `I_body_kgm2` (row-major) | `props.TotalMassPropsAsmb.Props.Inertia` expanded from the symmetric `XX, YX, ZX / YX, YY, ZY / ZX, ZY, ZZ` |
| `com_body_m` | `props.CenterOfMassAsmb` |
| `thruster[i].force_body_N`, `.torque_body_Nm` | `FullThrust(controller, com, ambient)`: for every nozzle of every core of every rocket of the controller, at throttle 1 — `thrust = nozzle.ComputePerformance(in conditions[core], ambient).GetTotalThrust()`, direction `(-nozzle.ExhaustDirectionAsmb).Transform(floatQuat.Pack(nozzle.Parent.Asmb2VehicleAsmb))`, position `nozzle.LocationAsmb.Transform(float4x4.Pack(nozzle.Parent.MatrixAsmb2VehicleAsmb))`; force summed, torque `Σ Cross(position - com, force)` |
| `thruster[i].min_pulse_s` | `controller.MinimumPulseTime` |
| `thruster[i].propellant_ok` | `output.Thrusters.GetReadOnlyState(controller).IsPropellantAvailable` |
| `engine[i].max_thrust_N` | `RocketControllerData.ComputeFromRockets(engine.Rockets, ambient, 1f).ThrustMax.Length()` |
| `engine[i].min_throttle` | `engine.MinimumThrottle` |
| `engine[i].isp_s` | `MassFlowRateMax > 0 ? thrust / MassFlowRateMax / 9.80665 : 0` |
| `engine[i].active` | `engine.IsActive` |
| `engine[i].propellant_ok` | `output.Engines.GetReadOnlyState(engine).IsPropellantAvailable` |
| `gimbal[i].range_y_rad`, `.range_z_rad` | `gimbal.Gimbal.AxisY.MaxAngle`, `gimbal.Gimbal.AxisZ.MaxAngle` |

Note that the engine values in this path do not depend on whether the engine is active: an inactive engine still reports its full thrust.

## Leaf path — vehicle level (`KfsHostSampler.cs:449-462`)

| Leaf | Source |
| --- | --- |
| `mass_kg` | `fc.TotalMassPropsBody.Mass` (overrides the typed value; the flight computer reads it in `ReadMeasurements`) |
| `I_body_kgm2.m[r][c]` | `fc.TotalMassPropsBody.Inertia` |
| `com_body_m` | `fc.CenterOfMassAsmb` |
| `max_g_load` | `fc.MaxGLoad` |
| `ang_accel_body_rps2` | `fc.AngularAccelerationBody` |
| `rcs_torque_ctrl_Nm` | `output.Thrusters.GlobalState.Authority.TotalTorque` |
| `rcs_min_impulse_ctrl_Nms` | `output.Thrusters.GlobalState.Authority.MinRotationalImpulse` |
| `rcs_thrust_from_rotation_N` | `output.Thrusters.GlobalState.MaxThrustFromRotation` |

## Leaf path — per thruster (present elements only, `:463-477`)

`state = output.Thrusters.GetReadOnlyState(thrusters[k])`:

| Leaf | Source |
| --- | --- |
| `thruster[k].torque_ctrl_Nm` | `state.IntendedTorque` |
| `thruster[k].force_ctrl_N` | `state.IntendedForce` |
| `thruster[k].torque_fraction` | `state.TorqueFraction` |
| `thruster[k].force_fraction` | `state.ForceFraction` |
| `thruster[k].min_pulse_s` | `thrusters[k].MinimumPulseTime` |
| `thruster[k].propellant_ok` | `state.IsPropellantAvailable` |
| `thruster[k].control_map` | `(long)state.ControlMap >> 1` — KSA's `ThrusterMapFlags` shifted down one bit |

## Leaf path — per engine (present elements only, `:478-501`)

`state = output.Engines.GetReadOnlyState(engine)`; `full = engine.ComputeActivePerformance(in state, fc.CenterOfMassAsmb, fc.AmbientPressure, 1f)`;
`min = engine.ComputeActivePerformance(in state, fc.CenterOfMassAsmb, fc.AmbientPressure, full.MinimumThrottle)`.
`ComputeActivePerformance` returns zero when the engine is inactive or out of propellant (`EngineController.cs:88-99`).

| Leaf | Source |
| --- | --- |
| `engine[l].max_thrust_N` | `full.ThrustMax.Length()` (overrides the typed value — and unlike it, is 0 for an inactive engine) |
| `engine[l].mass_flow_kgps` | `full.MassFlowRateMax` |
| `engine[l].min_throttle` | `full.MinimumThrottle` |
| `engine[l].min_thrust_N` | `min.ThrustMax.Length()` |
| `engine[l].min_mass_flow_kgps` | `min.MassFlowRateMax` |
| `engine[l].active` | `engine.IsActive` |
| `engine[l].propellant_ok` | `state.IsPropellantAvailable` |
| engine-model leaves | Constants computed once at sampler construction — see "EngineModel" |

## Leaf path — per gimbal (present elements only, `:502-521`)

`state = output.Gimbals.GetReadOnlyState(gimbal)`, `data = gimbal.Data` (`GimbalControllerData`):

| Leaf | Source |
| --- | --- |
| `gimbal[m].range_y_rad`, `.range_z_rad` | `gimbal.Gimbal.AxisY.MaxAngle`, `.AxisZ.MaxAngle` |
| `gimbal[m].active` | `gimbal.HasActiveEngine()` |
| `gimbal[m].thrust_N` | `state.TotalThrust` |
| `gimbal[m].max_thrust_N` | `data.MaximumThrust` |
| `gimbal[m].pivot_body_m` | `data.PivotPosVehicleAsmb` |
| `gimbal[m].dir0_gimbal` | `data.InitialDirGimbal` |
| `gimbal[m].offset_gimbal_m` | `data.ThrustOffsetGimbal` |
| `gimbal[m].q_gimbal_to_body` | `data.Gimbal2VehicleAsmb` |
| `gimbal[m].circular` | `gimbal.Gimbal.ConstrainToCircle` |

## `EngineModel`

`public sealed class EngineModel` — a fixed-capacity, flattened copy of an engine controller's core/nozzle performance model, built by
`EngineModel.From(EngineController controller)` and exported through `IEnumerable<(string Path, double Value)> Leaves(int i)`.[^engine-model]
The sampler evaluates it **once per engine when the sampler is constructed** and replays the cached `(handle, value)` pairs every sample
(`KfsHostSampler.cs:311-312`, `:494-499`).

Capacities: `CORES = 2`, `NOZZLES = 8`, `POINTS = 16` (table points per core), `LUT = 32`. Core kinds: `KIND_NONE = 0`, `KIND_COMBUSTOR = 1`,
`KIND_THERMAL = 2`, `KIND_PUBLISHED = 3`.

| Leaf (`engine[i].…`) | Source |
| --- | --- |
| `cores`, `nozzles` | Counts summed over `controller.Rockets` (`rocket.Cores.Length`, `rocket.AllNozzles.Length`). If there are more than 2 cores or 8 nozzles: `cores = min(n, 2)`, every `core_kind = 3`, `nozzles = 0`, note "{n} cores, {m} nozzles: beyond the capacity". |
| `core_kind[c]` | `1` if the core is a `Combustor`, all its nozzles are `DeLavalNozzle` and `Config.Lut.NumPoints <= 16`; `2` if it is a `ThermalCore`, all nozzles de Laval and `Config.Lut.Cols <= 16`; otherwise `3` |
| `core_min_throttle[c]` | `rocket.MinimumThrottle` |
| `core_p_max_pa[c]` | `combustor.Config.CombustionPressureMax` / `thermalCore.Config.ChamberPressureMax` |
| `core_efficiency[c]` | `Config.ThermalEfficiency` |
| `core_temperature_k[c]` (kind 2) | `Config.CoreTemperature` clamped to the first and last entries of `lut.Temperature` |
| `core_row_interp[c]` (kind 2) | Interpolation factor from `FixedReactionTable.FindSegment(lut.Temperature, Config.CoreTemperature)` |
| `core_points[c]` | `lut.NumPoints` / `lut.Cols` |
| `lut_ln_p[16c + k]` | `lut.LnPressure[k]` |
| `lut_temperature_k[16c + k]` (kind 1) | `lut.Temperature[k]` |
| `lut_gamma[…]`, `lut_r_jpkgk[…]` | `Properties[k].Gamma`, `.SpecificGasConstant` (kind 2: the lower temperature row) |
| `lut_gamma_hi[…]`, `lut_r_hi_jpkgk[…]` (kind 2) | The upper temperature row |
| `nozzle_core[j]` | Index of the nozzle's core |
| `nozzle_throat_m2[j]`, `nozzle_exit_m2[j]`, `nozzle_flow_eff[j]`, `nozzle_expansion_eff[j]` | `DeLavalNozzle.Config.ThroatArea`, `.ExitArea`, `.FlowEfficiency`, `.ExpansionEfficiency` (0 for other nozzle types) |
| `nozzle_dir_body[j].x/.y/.z` | `(-nozzle.ExhaustDirectionAsmb).Transform(floatQuat.Pack(nozzle.Parent.Asmb2VehicleAsmb))` |

`Notes` (`List<string>`) records why a core was classified as kind 3; nothing in the glue reads it.

# MSG_PILOT_INPUT (1027) (`KfsHostSampler.cs:522-541`)

`f = inputs.ThrusterCommandFlags.WithCanceledOpposingCommands()`; `Sg(plus, minus) = f.IsSet(plus) ? 1 : (f.IsSet(minus) ? -1 : 0)`.

| Leaf | Source |
| --- | --- |
| `rotate.x`, `.y`, `.z` | `Sg(RollRight, RollLeft)`, `Sg(PitchUp, PitchDown)`, `Sg(YawRight, YawLeft)` |
| `translate.x`, `.y`, `.z` | `Sg(TranslateForward, TranslateBackward)`, `Sg(TranslateRight, TranslateLeft)`, `Sg(TranslateDown, TranslateUp)` |
| `throttle` | `inputs.EngineThrottle` |
| `engine_on` | `inputs.EngineOn` |
| `throttle_active` | `true` when `inputs.EngineThrottle` differs from the value at the previous *sample* (false on the first sample) |
| `stage_presses` | `_host.Services.StagePresses` |
| `kill_switch` | Never written (always 0). The catalogue marks it reserved. |

# MSG_FC_SETTINGS (1028) (`:542-558`)

| Leaf | Source |
| --- | --- |
| `burn_mode` | `(int)fc.BurnMode` (0 Manual, 1 Auto) |
| `attitude_mode` | `(int)fc.AttitudeMode` |
| `frame` | `fc.AttitudeFrame` (`VehicleReferenceFrame`: 0 EclBody, 1 EnuBody, 2 Lvlh, 3 VlfBody, 4 BurnBody, 5 Dock) |
| `track_target` | `(int)fc.AttitudeTrackTarget` |
| `custom_target_rad` | `fc.CustomAttitudeTarget` |
| `manual_thrust_mode` | `(int)fc.ManualThrustMode` |
| `roll` | `(int)fc.RollMode` |
| `rcs_enabled` | `fc.RCSMode == FlightComputerRCSMode.Enabled` |
| `angle_deadband_rad`, `rate_limit_rps` | `fc.AngleDeadband`, `fc.RateLimit` |
| `sim_speed` | `Universe.GetSimulationSpeed()` (passed in by `SampleFor`) |
| `min_fc_dt_s` | `1.0 / GameSettings.Current.Simulation.MaxFlightComputerRate` |
| `service_throttle`, `service_throttle_valid`, `service_engine_on`, `service_engine_valid` | `_host.Computer.Override` (`ServiceOverride.Throttle`, `ThrottleValid`, `EngineOn`, `EngineValid`) |

# MSG_BURN_PLAN (1029) (`:559-606`)

Filled only when `fc.Burn` (`BurnTarget`) is not null; otherwise every field is 0. Time leaves are written with `SetTimeNs` as absolute
nanoseconds (`KfsHostSampler.Ns` saturates `UniverseTime.Nanoseconds`, an `Int128`, to `long`) and converted to microseconds relative to the
computer's reset epoch at pack time (`HostLeafValues.Us(absNs, epochNs)`).[^host-leaf-values]

| Leaf | Source |
| --- | --- |
| `valid` | `1` |
| `t_impulse_us` | `burn.ImpulsiveInstant` |
| `planned_ignition_valid`, `t_planned_ignition_us` | `burn.PlannedIgnition` when it has a value |
| `planned_throttle_valid`, `planned_throttle` | `burn.PlannedThrottle` when it has a value |
| `fixed_duration_valid`, `fixed_duration_s` | `burn.FixedDurationSeconds` when it has a value |
| `later_stage` | `fc.HasLaterStage()` |
| `r_burn_cci_m` | `burn.BurnPositionCci` |
| `dv_target_cci_mps`, `dv_accum_cci_mps` | `burn.DeltaVTargetCci`, `burn.DeltaVAccumCci` |
| `q_burn_body_to_cci` | `burn.BurnBody2Cci` |
| `speed_above_stop_mps` | `KfsHostSampler.SpeedAboveStop(burn, in nav)`: `+∞` when `burn.StopRelativeTo == null`; else the vehicle's speed relative to that body (via `Orbit.CciToCci`) minus `burn.StopSpeed` |
| `burn_started`, `t_started_us` | `burn.StartedAt` when it has a value |
| `burn_throttle_valid`, `burn_throttle` | `burn.Throttle` when it has a value |
| `last_ignition_denied` | `burn.LastIgnitionDenied` |
| `burn` (handle) | **Never written by the sampler** — always 0 in this build, although the catalogue describes it. |

# MSG_BURN_STATUS (1026) (`BurnStatus`, `:615-672`; `WriteLoaded`, `:674-710`)

`handles = _host.Computer.Handles`; `loaded = KfsServices.Loaded(fc)`; the reported handle is `handles.Get(handles.Current)` when a current
handle is set, else the handle bound to the loaded burn (if any).

| Case | Fields written |
| --- | --- |
| No handle, but `fc.Burn != null` and no executable burn is loaded | `WriteLoaded` from the `BurnTarget` with no plan: `state`, `t_ignition_us`, `dv_remaining_mps`, `dv_vlf_mps = 0`; `burn = 0` |
| No handle otherwise | Nothing (all zero: `state = NONE`) |
| Handle ended | `burn = id`, `state = (double)Ended` (4 Done, 5 Aborted), `end_cause = (double)Cause`, and the values latched in the handle: `t_ignition_us = IgnitionNs`, `dv_remaining_mps = DvRemaining`, `dv_vlf_mps = DvVlf` |
| Handle pending (plan requested, not built) | `burn = id`, `state = 6` |
| Handle bound to the loaded burn and `fc.Burn != null` | `burn = id` + `WriteLoaded` (below), which also stores the values in the handle |
| Handle bound to a burn that is not the loaded one | `burn = id`, `state = 1` (Planned), `t_ignition_us = Ns(burn.Time)`, `dv_vlf_mps = burn.DeltaVVlf` (also stored in the handle) |

`WriteLoaded(v, BurnTarget burn, Burn? plan, in nav, BurnHandle? h)`:

| Leaf | Source |
| --- | --- |
| `t_ignition_us` | `burn.IgnitionTime` unless it is `UniverseTime.EndOfTime`; then `burn.PlannedIgnition` if set; else `burn.ImpulsiveInstant` minus half of `burn.BurnDuration` |
| `dv_remaining_mps` | `burn.DeltaVToGoCci.Length()` |
| `state` | `burn.StartedAt.HasValue` → 3 Burning; else `burn.AlignmentStartTime <= nav.Time` → 2 Aligning; else 1 Planned |
| `dv_vlf_mps` | `plan?.DeltaVVlf ?? double3.Zero` |

State numbering is `BurnStatusState { None, Planned, Aligning, Burning, Done, Aborted, Pending }`; end causes are
`BurnEndCause { None, Completed, PropellantOut, StageRanDry, IgnitionDenied, PlanningFailed, Superseded, Cancelled }`.

# Modding notes

- The sampler is the model for **exposing game state to a program**: resolve leaf handles once per layout, then write doubles into a
  per-sample `HostLeafValues`. But the set of host messages is closed: `HostMessages.MsG3Order` is a static readonly array,
  `HostLeafMap` only maps messages in it, and `HostMessages.PackInto` throws for any other id. Publishing a *new* host message needs
  changes in the tables and host layer, not just here — see [message bus](/host/message-bus.md) and [messages table](/tables/messages-table.md).
- `KfsHostSampler` is public and its constructor is public, but its handles are private readonly fields and `Sample` builds the result
  from scratch; the practical extension seam is a postfix on `KfsVehicleHost.Sample` (public), which can inspect or replace the returned
  `HostSample` (its `Leaves` and `Values` are `init` properties; `Nav`, `Flight`, `Props` are settable).
- `HostSample.Values` (`IReadOnlyDictionary<ushort, IReadOnlyDictionary<string, double>>`) is an unused-by-the-game path that
  `PackInto` already honours for host messages: named leaf values written by `MessagePacker.WriteLeaves`. It is the least invasive way to
  override leaves of the existing seven messages.[^host-sample]
- All of this runs on the dispatch thread and reads live module objects (`ThrusterController`, `EngineController`, `GimbalController`,
  nozzles, gimbals) plus the worker's state updaters. A mod sampling other part modules (lights, animation state) from here must treat
  them as read-only and tolerate values that are one frame old.
- The leaf path only runs for programs that subscribe to one of the four "G3" messages; do not hang always-needed data on it.
- Sampler construction costs an `EngineModel.From` per engine and many dictionary lookups; kFS pre-builds it at image load
  (`Computer.ImageLoaded`) rather than in the first tick.
- Layout elements are indexed by the **frozen** set; a value for an element that has left the vehicle is simply not written (leaf
  path) or written as zero (typed path).

[^kfs-host-sampler]: KfsHostSampler.cs — constructor 209-313, `Ns` 386-398, `SpeedAboveStop` 400-409, `Sample` 411-613, `BurnStatus` 615-672, `WriteLoaded` 674-710.
[^kfs-vehicle-host]: KfsVehicleHost.cs — `FullThrust` 230-264, `ParentId` 276-279, `Sample` 281-295, `FlightData` 297-360, `SampleFor` 362-426.
[^engine-model]: EngineModel.cs:10-239.
[^host-messages]: HostMessages.cs — ids 338-364, `IsMsG3` 378-385, `PackInto` 430-470, `PackNav`/`PackFlight`/`PackProps` 472-543.
[^host-leaf-values]: HostLeafValues.cs:8-155; HostLeafMap.cs:7-76.
[^host-sample]: HostSample.cs:6-57.
[^vehicle-computer]: VehicleComputer.cs — `FlightDataWanted` 114, `WantsHostLeaves` 116, `Load` 336-343, lazy sample 496-501, `PlayerTick`/`PackKept` 554-583.
[^messages-json]: KSA.Kfs.Vm.Tables.Data.messages.json — messages 0x0101, 0x0102, 0x0401-0x0405.
[^flight-computer-navigation]: FlightComputerNavigation.cs:5-66.
[^physics-bubble]: PhysicsBubble.cs:2067-2075.
