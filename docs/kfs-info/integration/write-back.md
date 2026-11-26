---
type: Subsystem Reference
title: Write-back — how VM commands reach thrusters, engines and gimbals
description: The full-control write-back path from a program's latched command block to the game's per-module controller state, including indexing, clamping and NaN handling, plus the KfsWriteBack helpers that reset the stock flight computer.
resource: ksa-decomp:KSA.Kfs/KfsWriteBack.cs
tags: [kfs, integration, actuators, write-back, flight-computer]
sources:
  - id: kfs-vehicle-host
    resource: ksa-decomp:KSA.Kfs/KfsVehicleHost.cs
    title: KfsVehicleHost (decompiled)
  - id: kfs-write-back
    resource: ksa-decomp:KSA.Kfs/KfsWriteBack.cs
    title: KfsWriteBack (decompiled)
  - id: kfs-controller-set
    resource: ksa-decomp:KSA.Kfs/KfsControllerSet.cs
    title: KfsControllerSet (decompiled)
  - id: vehicle-computer
    resource: ksa-decomp:KSA.Kfs.Host/VehicleComputer.cs
    title: VehicleComputer (decompiled)
  - id: thruster-commands
    resource: ksa-decomp:KSA.Kfs.Host/ThrusterCommands.cs
    title: ThrusterCommands (decompiled)
  - id: module-stateful
    resource: ksa-decomp:KSA/ModuleStateful.cs
    title: ModuleStateful (decompiled)
  - id: thruster-controller
    resource: ksa-decomp:KSA/ThrusterController.cs
    title: ThrusterController and ThrusterControllerState (decompiled)
  - id: engine-controller
    resource: ksa-decomp:KSA/EngineControllerState.cs
    title: EngineControllerState (decompiled)
  - id: gimbal-controller
    resource: ksa-decomp:KSA/GimbalControllerState.cs
    title: GimbalControllerState and Gimbal (decompiled)
  - id: rocket
    resource: ksa-decomp:KSA/Rocket.cs
    title: Rocket (decompiled)
  - id: flight-computer
    resource: ksa-decomp:KSA/FlightComputer.cs
    title: FlightComputer (decompiled)
  - id: vehicle
    resource: ksa-decomp:KSA/Vehicle.cs
    title: Vehicle (decompiled)
generated: { by: claude-code/claude-fable-5-2, at: 2026-11-25T21:47:37Z }
status: stable
ksa_build: "2026.11.25.5949"
---

# Where write-back happens

Write-back is the last block of `KfsVehicleHost.DispatchCore` (`KfsVehicleHost.cs:575-614`), on the dispatch thread.[^kfs-vehicle-host] It runs
only when **all** of these hold:

| Condition | Source |
| --- | --- |
| The computer is under kFS control | `dispatchResult.UnderControl` |
| The program is in full-control mode (claims ALLOCATION) | `!dispatchResult.Pilot` — pilot-mode programs never write actuators; the stock computer does |
| The vehicle is controllable | `!dispatchResult.Fallback` |
| The computer is not quiescent | `Computer.IsQuiescent` returned `false` |
| At least one tick in this dispatch latched its command block | `dispatchResult.Latched` — set in `VehicleComputer.Dispatch` only when `hostTickResult.Latched & fullControl & vehicleControllable` (`VehicleComputer.cs:505-508`)[^vehicle-computer] |

Before it, in the same branch, `output.Clear()` has reset the output's wakeup, `AnyActuatorCommanded`, `EngineBurnDuration` and
`EngineThrottle`; then `output.RequestWakeup(NextWakeSeconds, PhysicsStepLimit.KfsTick)` and `output.AnyActuatorCommanded = true`.
The stock `FlightComputer.ComputeControl` is not called at all in this branch.

On dispatches where nothing latched, **no controller state is rewritten**: the previously written `Command…` values remain in the
state buffers and keep their meaning (a thruster pulse runs out, an engine lease counts down — see below).

# The target: per-module controller state

`FlightComputerOutput` carries three `ModuleStateful<TModule, TState, …>.StateUpdater` objects (`Thrusters`, `Engines`, `Gimbals`).
`GetModulesAndNewStates(ReadOnlySpan<TModule> modules)` returns an enumerator of `(Module, ref State)` pairs, where the state reference
is `GetNewStates()[module.StatesIdx]` — the worker's writable copy of that module's state (`ModuleStateful.cs:597-645`, `:689-701`,
`:775-778`).[^module-stateful] The buffers are published to the live module state list when the frame's results are applied
(`Vehicle.cs:2801`).[^vehicle]

| State struct | Fields kFS writes | Other fields |
| --- | --- | --- |
| `ThrusterControllerState` | `double CommandPulseTime`, `UniverseTime CommandTime` | `ControlMap`, `IntendedTorque(Inv)`, `IntendedForce(Inv)`, `TorqueFraction`, `ForceFraction`, `IsPropellantAvailable`, `WasActive` |
| `EngineControllerState` | `float CommandThrottle`, `double CommandBurnTime`, `UniverseTime CommandTime` | `IsPropellantAvailable`, `WasActive` |
| `GimbalControllerState` | `float CommandY`, `float CommandZ` | `TotalThrust`, `NaturalFrequencyLimit`, `Angles`, `AngleRates` |

`GetRocketCommand()` on the thruster and engine states turns them into `RocketCommand { Throttle, BurnTime, LastCommandTime }` (throttle
is fixed at `1f` for thrusters), consumed by `Rocket.UpdateRockets` in the module update that follows the flight-computer pass.[^thruster-controller][^engine-controller][^rocket]

# Thrusters

```csharp
KfsControllerSet frozen = Frozen;
ReadOnlySpan<double> lastPulses = Computer.LastPulses;
int n = 0;
foreach (var current in output.Thrusters.GetModulesAndNewStates(frozen.PresentThrusters.AsSpan()))
{
    int index = frozen.ThrusterIndex[n++];                                     // frozen (program) index of this present thruster
    double pulse = (index < lastPulses.Length) ? lastPulses[index] : 0.0;
    if (pulse > 0.0)
    {
        ThrusterController.UpdateCommand(pulse, current);
        current.State.CommandTime = nav.Time;
    }
}
```

- `Computer.LastPulses` is filled by `ThrusterCommands.Read(latched, pulses)` each time a tick latches: for every
  `thruster[i].pulse_s` (little-endian `float32` at the field's offset) — **NaN or `<= 0` → `0`; otherwise `min(value, 1.0)`**
  (`WATCHDOG_CAP_SECONDS = 1.0`). The count of non-zero pulses is added to `PulsesSent`.[^thruster-commands][^vehicle-computer]
- `ThrusterController.UpdateCommand(double pulseTime, StateUpdater.ModuleAndNewStateRef controller)` is public static: if
  `!controller.Module.IsActive` the pulse becomes `0`; it sets `controller.State.CommandPulseTime = pulseTime` and returns
  `pulseTime > 0.0`.[^thruster-controller]
- A zero pulse leaves the thruster's state untouched: an earlier pulse still in progress is **not cancelled** by commanding 0.
- `pulse_s` is a one-shot command field (see [I/O layout generation](/host/io-layout-generation.md)); when several ticks latch within
  one dispatch, only the last latched tick's pulses are in `LastPulses` when write-back runs.

# Engines

```csharp
ReadOnlySpan<byte> applied = Computer.Applied;        // the latched command block
int n = 0;
foreach (var current in output.Engines.GetModulesAndNewStates(frozen.PresentEngines.AsSpan()))
{
    int at = frozen.EngineAt(frozen.EngineIndex[n++]);                        // offset of engine[i].throttle
    current.State.CommandThrottle = ThrottleCommand(BinaryPrimitives.ReadSingleLittleEndian(applied.Slice(at)));
    current.State.CommandBurnTime = BurnTimeCommand(BinaryPrimitives.ReadSingleLittleEndian(applied.Slice(at + 4)));   // engine[i].lease_s
    current.State.CommandTime = nav.Time;
}
```

| Helper (`internal static`, `KfsVehicleHost.cs:618-634`) | Rule |
| --- | --- |
| `float ThrottleCommand(float latched)` | NaN → `0f`; else `Math.Clamp(latched, 0f, 1f)` |
| `double BurnTimeCommand(float latched)` | NaN → `0.0`; else `Math.Clamp(latched, 0f, float.MaxValue)` (so `+∞` becomes `float.MaxValue`, negatives become 0) |

- Every present engine is written on every latching dispatch — including a throttle of 0, which shuts it down.
- `engine[i].lease_s` becomes `CommandBurnTime`: the engine burns for that long measured from `CommandTime`. A program must keep
  renewing the lease; if it stops latching (halt, trap, overrun, budget skip) the engine stops when the lease expires. *Inferred* from the
  field name and `RocketCommand.BurnTime`/`LastCommandTime`; the rocket integration itself was not traced.
- kFS does not check `IsActive` or `IsPropellantAvailable` here. The stock path does (`FlightComputer.CommandEngineThrottles`,
  `FlightComputer.cs:814-834`), but the effect is the same: `Rocket.UpdateRockets` replaces the state of an inactive controller with
  `EngineControllerState.Zero(…)` / `ThrusterControllerState.Zero(…)` before building rocket commands (`Rocket.cs:278-316`).[^flight-computer][^rocket]
- The minimum throttle is not enforced by write-back; `MSG_VEHICLE_PROPS.engine[i].min_throttle` tells the program what it is.

# Gimbals

```csharp
int n = 0;
foreach (var current in output.Gimbals.GetModulesAndNewStates(frozen.PresentGimbals.AsSpan()))
{
    int at = frozen.GimbalAt(frozen.GimbalIndex[n++]);                        // offset of gimbal[i].y
    current.State.CommandY = GimbalCommand(BinaryPrimitives.ReadSingleLittleEndian(applied.Slice(at)));
    current.State.CommandZ = GimbalCommand(BinaryPrimitives.ReadSingleLittleEndian(applied.Slice(at + 4)));         // gimbal[i].z
}
```

`internal static float GimbalCommand(float latched)`: NaN → `0f`; else `Math.Clamp(latched, -1f, 1f)` (`:636-643`). The commands are
**normalised deflections**, not angles: `Gimbal.GetCommand` multiplies them by `AxisY.MaxAngle` / `AxisZ.MaxAngle` after clamping again
and, when `ConstrainToCircle`, scaling the pair back onto the unit circle (`Gimbal.cs:144-158`).[^gimbal-controller] There is no command time for gimbals.

# Indexing and inert elements

Write-back enumerates `frozen.PresentThrusters`, `PresentEngines`, `PresentGimbals` — the frozen-set elements still on the vehicle —
and maps each back to its program index through `ThrusterIndex` / `EngineIndex` / `GimbalIndex`. Command fields of inert elements are
never read. Why the filter is required (the updaters index by `StatesIdx`):
[controller set and layout changes](/integration/controller-set-and-layout-changes.md).[^kfs-controller-set]

# `KfsWriteBack`

`public static class KfsWriteBack` holds the three stock-flight-computer helpers used around write-back.[^kfs-write-back]

## `ActiveEnginePerformanceMax`

```csharp
public static ActiveEnginePerformance ActiveEnginePerformanceMax(FlightComputer fc, ref readonly FlightComputerOutput outputs)
```

For every engine in `outputs.Engines.ModulesAndReadOnlyStates`: `data = module.ComputeActivePerformance(in state, fc.CenterOfMassAsmb, fc.AmbientPressure, 1f)`;
engines with `data.MassFlowRateMax <= 0` are skipped; otherwise `Thrust += data.ThrustMax.Length()`, `MassFlowRate += data.MassFlowRateMax`,
and the largest `data.MinimumThrottle` is kept. `MinThrottle` is set only if the total mass flow is positive.

In full control `DispatchCore` assigns the result to `fc.ActiveEnginePerformanceMax` on every dispatch that ran a tick
(`KfsVehicleHost.cs:561-564`). The stock `ComputeControl` normally maintains this field (`UpdateActiveEnginePerformance`,
`FlightComputer.cs:577`, `:1381-1384`); because it does not run in full control, kFS keeps it fresh for the consumers that still read it —
burn-duration and staged-burn planning (`PhysicsBubble.UpdateStagedBurn`, `FlightComputer.GetFullThrottlePhases`).[^flight-computer]

## `PilotDefaults`

```csharp
public static void PilotDefaults(FlightComputer fc, bool resetProfile)
{
    fc.BurnMode = FlightComputerBurnMode.Manual;
    fc.CustomAttitudeTarget = double3.Zero;
    fc.RollMode = FlightComputerRollMode.Decoupled;
    fc.RateHold(VehicleReferenceFrame.EclBody);      // AttitudeMode = Auto, track target None, frame EclBody, zero target
    if (resetProfile) fc.SetAttitudeProfile(FlightComputerAttitudeProfile.Balanced);
}
```

The state a vehicle is handed back in: manual burn mode, attitude hold at zero rates in the ecliptic body frame, roll decoupled, and —
only if a `set_attitude_profile` service had changed it — the Balanced profile. Callers: `KfsServices.HandBack` (dispatch thread, worker
flight computer) and `KfsServices.HandBackPilot` (main thread, live flight computer). See
[flight computer interplay](/integration/flight-computer-interplay.md).

## `CSharpReset`

```csharp
public static void CSharpReset(FlightComputer fc)
```

Resets the stock controller's *internal* state so that it restarts cleanly after a period in which kFS, not it, was flying:

| Field | Value |
| --- | --- |
| `LastControlTime` | `null` (the next `ComputeControl` treats itself as the first call: `ControlDeltaTime = 0`) |
| `SteeringInterval` | `0.1` (`KfsWriteBack.TVC_CONTROL_INTERVAL` is the same constant, declared `public const double TVC_CONTROL_INTERVAL = 0.1`) |
| `SteeringAuthority` | `0.0` |
| `ActiveControlSystem` | `new PerAxisAttitudeControlSystem(AttitudeControlSystem.None)` |
| `PlannedBurnThrottle` | `1f` |
| `IsThrottleGLoadLimited` | `false` |
| `ManualGLoadThrottleCap` | `1f` |
| `TvcTorqueAuthority`, `TvcTorqueAuthorityMax`, `TvcDeflection`, `TvcDeflectionRate`, `TvcNaturalFrequency`, `RcsTorqueAuthority`, `AngleTurnaround`, `RateBit`, `RateDeadband`, `ErrorAngles`, `ErrorRates`, `ManualTargetRates` | `float3.Zero` |
| `ConservativeFlipTime`, `DetumbleRateLimit` | `0f` |
| `ErrorAngleIntegrals` | `double3.Zero` |
| `RateDeadbandStates[0..2]` | `DeadbandState.Inside` |

It mirrors the reset block of `FlightComputer.DeserializeSave` (`FlightComputer.cs:397-419`).[^flight-computer] It is applied when the computer raises
its "C# reset" flag (`VehicleComputer.TakeCSharpReset()`): when a full-control program stops flying — stop, kill switch, idle, host
fault — or loses controllability while flying (`VehicleComputer.cs:288-293`, `:387-428`, `:463-468`).[^vehicle-computer] It does not touch the pilot-visible
settings (modes, targets, profile); those are `PilotDefaults`' job.

# Modding notes

- The pattern for commanding hardware from a dispatch is: **take the module's `StateUpdater`, get the new-state reference, write plain
  fields, stamp a command time.** It works because those three module types are `ModuleStateful` with unmanaged state structs that the
  frame's result application copies back. Whether lights and animation modules have equivalent worker-side state updaters decides
  whether they can be driven the same way or must go through the main-thread queue; see
  [light module](/game-targets/light-module.md) and [keyframe animation module](/game-targets/keyframe-animation-module.md).
- The command block is a fixed layout of thruster, engine and gimbal fields generated from three counts. New actuator classes cannot be
  added to it without changing the layout generator and the image fingerprint; a **service request** (see
  [KfsServices](/integration/kfs-services-game-side.md)) is the lower-friction route for on/off, colour, angle or "play animation"
  commands.
- `ThrottleCommand`, `BurnTimeCommand`, `GimbalCommand` are `internal static` one-liners (likely inlined); `ThrusterController.UpdateCommand`
  is `[MethodImpl(AggressiveInlining)]`. None of them is a dependable patch target. To observe or alter actuator commands, postfix
  `KfsVehicleHost.Dispatch` and read or rewrite `output.Thrusters/Engines/Gimbals` new states.
- Sanitisation rules worth copying for any new command channel: NaN means "no command" (0), ranges are clamped host-side, pulses are
  capped at one second, and persistent commands are leases that expire unless renewed.
- `KfsWriteBack` is public static with plain signatures; `PilotDefaults` and `CSharpReset` can be called by a mod that needs to hand a
  vehicle back to the stock computer — on the correct flight-computer copy for the calling thread
  ([dispatch and threading](/integration/dispatch-and-threading.md)).

[^kfs-vehicle-host]: KfsVehicleHost.cs — full-control branch 561-616, clamps 618-643.
[^kfs-write-back]: KfsWriteBack.cs:6-74.
[^kfs-controller-set]: KfsControllerSet.cs:46-56, 97-110, 141-155.
[^vehicle-computer]: VehicleComputer.cs — `LastPulses`/`Applied` 126-128, `TakeCSharpReset` 288-293, `Fault`/`Stop`/`LeaveControl` 387-428, `Dispatch<TSampler>` 449-518, `ApplyPlayer` 585-593.
[^thruster-commands]: ThrusterCommands.cs:8-42.
[^module-stateful]: ModuleStateful.cs:582-808.
[^thruster-controller]: ThrusterController.cs:44-53; ThrusterControllerState.cs:6-64.
[^engine-controller]: EngineControllerState.cs:5-48.
[^gimbal-controller]: GimbalControllerState.cs:6-38; Gimbal.cs:144-158.
[^rocket]: Rocket.cs:273-330.
[^flight-computer]: FlightComputer.cs — `GetFullThrottlePhases` 281-289, `DeserializeSave` 362-421, `ComputeControl` 550-679, `CommandEngineThrottles` 814-834.
[^vehicle]: Vehicle.cs:2801.
