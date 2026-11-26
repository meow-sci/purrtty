---
type: Subsystem Reference
title: Interplay between kFS and the stock flight computer
description: When the stock FlightComputer.ComputeControl still runs, how pilot mode and full control divide the work, which services need the stock computer, how the service override, burn modes and attitude modes behave, and what a handback does.
resource: ksa-decomp:KSA/FlightComputer.cs
tags: [kfs, integration, flight-computer, pilot-mode, services, handback]
sources:
  - id: flight-computer
    resource: ksa-decomp:KSA/FlightComputer.cs
    title: FlightComputer (decompiled)
  - id: kfs-vehicle-host
    resource: ksa-decomp:KSA.Kfs/KfsVehicleHost.cs
    title: KfsVehicleHost (decompiled)
  - id: kfs-services
    resource: ksa-decomp:KSA.Kfs/KfsServices.cs
    title: KfsServices (decompiled)
  - id: kfs-write-back
    resource: ksa-decomp:KSA.Kfs/KfsWriteBack.cs
    title: KfsWriteBack (decompiled)
  - id: vehicle-computer
    resource: ksa-decomp:KSA.Kfs.Host/VehicleComputer.cs
    title: VehicleComputer (decompiled)
  - id: ownership
    resource: ksa-decomp:KSA.Kfs.Host/Ownership.cs
    title: Ownership (decompiled)
  - id: service-override
    resource: ksa-decomp:KSA.Kfs.Host.Services/ServiceOverride.cs
    title: ServiceOverride (decompiled)
  - id: services-json
    resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.services.json
    title: services.json (embedded table)
  - id: physics-bubble
    resource: ksa-decomp:KSA/PhysicsBubble.cs
    title: PhysicsBubble (decompiled)
  - id: vehicle
    resource: ksa-decomp:KSA/Vehicle.cs
    title: Vehicle (decompiled)
  - id: program
    resource: ksa-decomp:KSA/Program.cs
    title: Program (decompiled)
  - id: fc-enums
    resource: ksa-decomp:KSA/FlightComputerAttitudeTrackTarget.cs
    title: Flight computer enums (decompiled)
generated: { by: claude-code/claude-fable-5-2, at: 2026-11-25T21:47:37Z }
status: stable
ksa_build: "2026.11.25.5949"
---

# Two computers, one vehicle

Every vehicle has a stock ("C#") `FlightComputer`: attitude hold and tracking, RCS and gimbal allocation, manual throttle, automatic
burn execution. kFS never replaces that object. It either **drives it** (pilot mode) or **bypasses its control law** (full control).
The switch is made per dispatch by the return value of `KfsDispatch.TryComputeControl`: `false` lets
`PhysicsBubble.RunFlightComputer` call the stock `ComputeControl`.[^physics-bubble]

| Situation | Stock `ComputeControl` | Called by | Actuators commanded by |
| --- | --- | --- | --- |
| Flag off, no host, or computer not under kFS control | runs | `PhysicsBubble.RunFlightComputer` (`PhysicsBubble.cs:2083-2084`) | stock |
| Under control but the vehicle is not controllable (fallback) | runs | `PhysicsBubble.RunFlightComputer` | stock |
| Under control, **pilot mode** | runs | **`KfsVehicleHost.DispatchCore`** (`KfsVehicleHost.cs:551`), with the service override applied to its inputs | stock |
| Under control, **full control** | **does not run** | — | the program's command block ([write-back](/integration/write-back.md)) |
| Host fault during dispatch | runs | `PhysicsBubble.RunFlightComputer` | stock |

"Under kFS control" is `VehicleComputer.UnderKfsControl`: no host fault, kill switch not thrown, and state `Boot`, `Running` or `Halted`
(`VehicleComputer.cs:77-90`). `NoSoftware`, `Standby` and **`Idle`** are not under control; `Halted` is — a trapped or halted program
keeps the vehicle, with its commands held.[^vehicle-computer]

# Pilot mode versus full control

`ControlMode { Pilot, FullControl }` is decided by the image: `Ownership.ModeOf(image)` returns `FullControl` exactly when
`image.ClaimsAllocation`.[^ownership]

`Ownership.NeedsStock(image)` makes `VehicleComputer.Load` reject (`LoadResult.Fail("needs-stock", …)`) any image that[^ownership][^vehicle-computer]

- publishes `MSG_NAV_STATE` (257), `MSG_ATT_TARGET` (513) or `MSG_WRENCH_CMD` (769) — i.e. owns the NAVIGATION, GUIDANCE or
  ATTITUDE_CONTROL block — **without** claiming ALLOCATION ("…which needs the stock computer this release doesn't have (kFS D-135)"), or
- subscribes to `MSG_ATT_TARGET` (513) or `MSG_GUIDANCE_STATUS` (514) without publishing it ("only stock guidance published it, and
  there is no stock computer in this release").

*Not active in this build:* a "stock computer" written in kC that would consume intermediate blocks. What remains are exactly two shapes
of program:

| | Pilot mode | Full control |
| --- | --- | --- |
| Image | claims nothing | claims ALLOCATION |
| Reads | host messages (nav state, flight data, vehicle props, pilot input, FC settings, burn plan/status) | the same |
| Acts through | **services** that set the stock computer's modes and targets, the throttle/engine override, burn planning, staging | the **command block** (thruster pulses, engine throttle + lease, gimbal deflection), plus `stage` and burn *planning* |
| Stock control law | runs every dispatch | bypassed |
| Steering services (48-53, 55-57, 66, 67) | applied | **rejected**: "{name} steers the C# flight computer, which flies only in pilot mode; this program claims ALLOCATION (kFS D-135)" (`VehicleComputer.cs:218-235`) |

See also [ownership and control modes](/host/ownership-and-control-modes.md).

# Services and `requires_stock`

The services table carries a `requires_stock` column. Its non-null rows are exactly the ids the host refuses in full control (plus
`translate`, refused always):[^services-json][^vehicle-computer]

| Id | Name | `requires_stock` | Pilot mode | Full control |
| --- | --- | --- | --- | --- |
| 1, 2, 3 | `log`, `trace`, `halt` | — | host-internal | host-internal |
| 16, 17 | `sleep_until`, `sleep_until_event` | — | logged, no effect | logged, no effect |
| 32 | `stage` | — | applied (main thread) | applied (main thread) |
| 48 | `hold_direction` | GUIDANCE | applied | rejected |
| 49 | `hold_rates` | GUIDANCE | applied | rejected |
| 50 | `release_attitude` | GUIDANCE | applied | rejected |
| 51 | `set_attitude_profile` | ATTITUDE_CONTROL | applied | rejected |
| 52 | `set_throttle` | GUIDANCE | applied | rejected |
| 53 | `engines_on` | GUIDANCE | applied | rejected |
| 54 | `translate` | GUIDANCE | rejected (always) | rejected (always) |
| 55 | `hold_heading` | GUIDANCE | applied | rejected |
| 56 | `hold_quat` | GUIDANCE | applied | rejected |
| 57 | `engines_off` | GUIDANCE | applied | rejected |
| 64, 65 | `plan_circularize`, `plan_burn` | — | applied (main thread) | applied (main thread) |
| 66 | `burn_start` | GUIDANCE | applied | rejected |
| 67 | `burn_abort` | GUIDANCE | applied | rejected |

What "applied" does, line by line: [KfsServices (game side)](/integration/kfs-services-game-side.md).

# Pilot mode, one dispatch

`KfsVehicleHost.DispatchCore`, pilot branch (`KfsVehicleHost.cs:534-560`):[^kfs-vehicle-host]

```csharp
Services.AfterDispatch(in nav, flightComputer, wasAuto, fallback: false);     // apply this tick's services to fc
ManualControlInputs inputs = PhysicsBubble.FlightComputerInputsFor(vehicleState);
ServiceOverride o = Computer.Override;
if (o.ThrottleValid) inputs.EngineThrottle = o.Throttle;
if (o.EngineValid)   inputs.EngineOn       = o.EngineOn;
KfsServices.BurnFlags before = KfsServices.BeforeComputeControl(flightComputer);
flightComputer.ComputeControl(in nav, in inputs, ref output);                 // the stock control law
Services.AfterComputeControl(flightComputer, before);                         // detect an auto burn ending
if (Computer.State != ComputerState.Halted)
{
    output.RequestWakeup(dispatchResult.NextWakeSeconds, PhysicsStepLimit.KfsTick);
    output.AnyActuatorCommanded = true;
}
return true;
```

So a service issued in tick *k* takes effect in the stock control law call of the **same dispatch**.

## The service override

`ServiceOverride` (one per computer) holds `Throttle`/`ThrottleValid` and `EngineOn`/`EngineValid`.[^service-override]

| Aspect | Behaviour |
| --- | --- |
| Set by | `set_throttle(x)` → `SetThrottle(x)`; `engines_on` / `engines_off` → `SetEngine(true/false)` |
| Applied | Only in the pilot branch above, to the **copy** of the pilot inputs handed to `ComputeControl`. The vehicle's own `_manualControlInputs` (what the throttle gauge shows and `MSG_PILOT_INPUT` reports) is not modified. |
| Effect in Manual burn mode | Stock: `outputs.EngineBurnDuration = inputs.EngineOn ? +∞ : 0`, `outputs.EngineThrottle = min(inputs.EngineThrottle, g-load cap)` (`FlightComputer.cs:645-652`) — the engines fire only with the switch on and throttle above zero. |
| Effect in Auto burn mode | The stock law ignores the inputs' throttle and switch (`FlightComputer.cs:589-644`); but `set_throttle(0)` and `engines_off` **cancel** an auto burn (`KfsServices.CancelAutoBurn`). |
| Released by the pilot | `ServiceOverride.PilotFrame`, called from `KfsServices.BeforeDispatch` with the pilot's throttle, switch and "burn mode is Manual": when both this and the previous call were in Manual and the pilot's throttle (or switch) changed, `ThrottleValid` (or `EngineValid`) is cleared and an event is logged. |
| Cleared | `Override.Clear()` on `VehicleComputer.LeaveControl()` and `Fault()` (`VehicleComputer.cs:398`, `:417`). |
| Visible to the program | `MSG_FC_SETTINGS.service_throttle(_valid)`, `service_engine_on(_valid)`. |
| When the vehicle is not controllable | Recorded, but not applied (the stock call is then made by `PhysicsBubble` with plain inputs); the events say so ("… (kFS D-124)"). |

## Attitude

The attitude services write the stock computer's public settings on the worker copy:[^kfs-services][^flight-computer][^fc-enums]

| Setting | Type | Values |
| --- | --- | --- |
| `AttitudeMode` | `FlightComputerAttitudeMode : byte` | `Manual`, `Auto` |
| `AttitudeFrame` | `VehicleReferenceFrame` | `EclBody` 0, `EnuBody` 1, `Lvlh` 2, `VlfBody` 3, `BurnBody` 4, `Dock` 5 |
| `AttitudeTrackTarget` | `FlightComputerAttitudeTrackTarget : byte` | `None` 0 (rate hold), `Custom` 1, `Forward` 2, `Backward`, `Up`, `Down`, `Ahead`, `Behind`, `RadialOut`, `RadialIn`, `Prograde` 10, `Retrograde`, `Normal`, `AntiNormal`, `Outward`, `Inward`, `PositiveDv` 16, `NegativeDv`, `Toward`, `Away`, `Antivel`, `Align` 21 |
| `CustomAttitudeTarget` | `double3` | Euler angles in `AttitudeFrame`; in rate hold a rate offset |
| `RollMode` | `FlightComputerRollMode : byte` | `Decoupled`, `Up`, `Down` |
| `AngleDeadband`, `RateLimit` | `float` | Set together by `SetAttitudeProfile`: `Strict` 0.0034906585 rad / π/6 rad/s; `Balanced` π/180 rad / 0.08726646 rad/s; `Relaxed` π/24 rad / π/180 rad/s (`FlightComputerAttitudeProfileEx`) |

`hold_direction` accepts track targets 2-21 (`arg_domains: enum:direction[2..21]`). The pilot can still change any of these through the
stock UI; the change is applied in `InputEvents.ApplyInputEvents`, reaches the worker copy at the next frame, and is noticed by
`KfsServices.BeforeDispatch` as a "pilot set" value (events only — a later service call simply overwrites it).

## Burns

`FlightComputerBurnMode : byte { Manual, Auto }`. In Auto the stock law runs the loaded burn (`fc.Burn`, a `BurnTarget` derived from the
first executable `Burn` of `fc.BurnPlan`): it points along `PositiveDv` once `Burn.AlignmentStartTime` has passed, waits for
`Burn.IgnitionTime` and alignment, then throttles and requests steering wakeups (`FlightComputer.cs:589-644`).[^flight-computer] It leaves Auto by itself when

- the burn completes (`AutoBurnCompleted = true`, `FlightComputer.cs:1478`),
- the active engines run out of propellant (`BurnMode = Manual`, `AutoBurnStoppedOutOfPropellant = true`, `AutoBurnStageRanDry = HasLaterStage()`, `:665-670`),
- ignition is denied on two consecutive passes (`:657-661`).

kFS's part: `burn_start` sets `fc.BurnMode = Auto`; `burn_abort`, `set_throttle(0)` and `engines_off` set it back to `Manual`;
`KfsServices.AfterComputeControl` compares `BurnFlags` taken before and after the stock call to classify how an auto burn ended and ends
the burn's handle with the matching `BurnEndCause`. The stock main-thread follow-up (`FlightComputer.RaisePendingAlerts`, called for every
vehicle in frame at `Program.cs:2561-2566`: alerts, removing the finished burn, stopping warp) still runs.[^program]

Burn *planning* (ids 64, 65) only edits the burn plan (through `InputEvents.BurnUpdateBuffer`) and works in both modes; but only a
pilot-mode program can *execute* a planned burn, because `burn_start` is a steering service.

# Full control: what of the stock computer still runs

`ComputeControl` is skipped, but the object stays in the loop:[^physics-bubble][^kfs-vehicle-host][^kfs-write-back]

| Still happens | Where |
| --- | --- |
| `FlightComputer.ReadMeasurements` each physics substep (mass properties, centre of mass, ambient pressure, max g-load, accumulated burn Δv, accelerations) | `PhysicsBubble.IngestStepKinematicMeasurements` (`PhysicsBubble.cs:2682-2688`) |
| Staged-burn / interstellar-supply bookkeeping | `PhysicsBubble.UpdateStagedBurn` (`:958-991`) |
| Copy to and from the live vehicle each frame; pilot UI edits of modes and targets | `VehicleUpdateData.Prepare`, `Vehicle.UpdateFromTaskResultsUnsynchronized` |
| `fc.ActiveEnginePerformanceMax` refreshed by kFS on dispatches that ran a tick | `KfsWriteBack.ActiveEnginePerformanceMax` (`KfsVehicleHost.cs:561-564`) |
| Queued services 32, 64, 65 | `Services.AfterDispatch` (`:565-567`) |

The pilot's attitude and burn-mode buttons therefore have **no effect on the actuators** in full control; the program can read them
(`MSG_FC_SETTINGS`, `MSG_PILOT_INPUT`) and decide what to do. Stock internal control state goes stale while bypassed, which is why a
`CSharpReset` is applied when the program stops flying.

# Leaving kFS control: reset and handback

The computer raises two one-shot flags that `DispatchCore` (and `KfsVehicleHost.Fault`) consume right after `Computer.Dispatch`:[^vehicle-computer][^kfs-vehicle-host]

| Flag | Raised when | Consumed as |
| --- | --- | --- |
| `TakeCSharpReset()` | `LeaveControl()` while a full-control program was actually flying (`_flying`); a full-control program finds the vehicle no longer controllable; any `Fault` | `KfsWriteBack.CSharpReset(fc)` — clears the stock law's internal state ([write-back](/integration/write-back.md)) |
| `TakeHandback()` | `LeaveControl()` or `Fault` while the computer had been in control (`_controlled`) | `Services.HandBack(fc, pilotDone)` |

`LeaveControl()` is called by `Stop()`, `KillSwitch()` and at the top of `Dispatch` whenever the computer is not under kFS control
(covers a program that went `Idle`, and the dispatch after a stop). `Services.HandBack` (unless `pilotDone`):[^kfs-services]

1. ends every live burn handle `Aborted`/`Cancelled`;
2. `KfsWriteBack.PilotDefaults(fc, resetProfile)` — burn mode `Manual`, custom target zero, roll `Decoupled`, `RateHold(EclBody)`
   (attitude `Auto`, track target `None`), and the `Balanced` profile if a service had changed the profile;
3. queues `EngineOffWork` → main thread `vehicle.SetEnum(VehicleEngine.MainShutdown)` (the pilot's engine switch goes off);
4. logs "handback: burn mode manual, rate hold at zero rates, engines off".

So after any exit — stop, kill switch, program finished, host fault — the vehicle is left **holding attitude with engines off**, under
the stock computer. Turning the feature flag off does the equivalent from the main thread (`KfsServices.HandBackPilot`); see
[feature flag](/integration/feature-flag.md).

A `Halted` program does **not** hand back: in full control its last latched commands stay (engine leases run out, gimbals stay
deflected), and after one second with no held non-one-shot command the computer is quiescent and stops requesting wakeups; in pilot mode
the stock law keeps flying with the last settings and override. `kfs kill` (kill switch) or `kfs stop` is the way to hand back.

# Controllability

`Vehicle.IsControllable` (`Vehicle.cs:704-718`) requires a control module and `_control == VehicleControl.Controllable` (probe core
powered and on, crew capable…).[^vehicle] It is read on the dispatch thread and passed down as `controllable`:

- not controllable → fallback: the VM keeps ticking, nothing is latched to actuators, the stock computer runs — and the stock side has
  already queued `AttitudeMode = Manual` / `BurnMode = Manual` on losing control (`Vehicle.ReleaseFlightComputer`, `Vehicle.cs:3770-3788`);
- `burn_start` on a non-controllable vehicle is rejected: "auto burn refused: no control module (ControlsLockout)" (`KfsServices.cs:525-530`).

# Modding notes

- A mod adding **vehicle-system services** (lights, animations) is orthogonal to both modes: like `stage`, such a service steers neither
  the stock law nor the command block, so it should be accepted in pilot *and* full control (do not copy the `Mode == FullControl`
  rejection) and — again like `stage` — applied on the main thread.
- Decide what a mod service does **in fallback** (vehicle not controllable). kFS applies stock-settings services anyway and only logs for
  override services; `stage` and burn planning are applied regardless. A light switch on an uncontrollable vehicle is a design choice;
  `KfsVehicleHost.Vehicle.IsControllable` and the `fallback` argument of `KfsServices.Apply` are the two places the information exists.
- Decide what happens **on handback**. kFS restores a safe state for the things it changed (modes, engines). A mod that changed lights or
  deployed parts should choose explicitly whether to leave them as they are; if it wants to react, the observable points are
  `KfsServices.HandBack(FlightComputer, bool)` (public, dispatch thread) and `KfsServices.HandBackPilot(Vehicle)` (public, main thread).
- `FlightComputer`'s settings are public fields and the helper methods (`TrackTarget`, `RateHold`, `SetAttitudeProfile`) are tiny
  `AggressiveInlining` methods — patching them is unreliable; patch the kFS side instead.
- The only two callers of `FlightComputer.ComputeControl` are `PhysicsBubble.RunFlightComputer` and `KfsVehicleHost.DispatchCore`; a
  prefix on `ComputeControl` therefore sees both the stock path and kFS pilot mode, and never runs under full control.

[^physics-bubble]: PhysicsBubble.cs:958-991, 2067-2086, 2682-2688.
[^kfs-vehicle-host]: KfsVehicleHost.cs:168-210, 488-616.
[^kfs-services]: KfsServices.cs — `BeforeDispatch` 76-150, `AfterComputeControl` 233-252, `HandBack` 254-277, `Apply` 310-449, `CancelAutoBurn` 451-467, `Start` 522-541.
[^kfs-write-back]: KfsWriteBack.cs:10-73.
[^vehicle-computer]: VehicleComputer.cs — `UnderKfsControl` 77-90, `ApplyRequest` 185-239, `TakeCSharpReset`/`TakeHandback` 288-300, `KillSwitch` 302-311, `Load` 313-350, `Fault` 387-406, `Stop`/`LeaveControl` 408-428, `Dispatch<TSampler>` 449-518.
[^ownership]: Ownership.cs:6-77.
[^service-override]: ServiceOverride.cs:3-67.
[^services-json]: KSA.Kfs.Vm.Tables.Data.services.json — `requires_stock` of every row.
[^flight-computer]: FlightComputer.cs — public settings 84-102, `ComputeControl` 550-679, `RaisePendingAlerts` 713-751, `AutoBurnCompleted = true` 1478, `RateHold` 2309-2313, `TrackTarget` 2324-2328, `SetAttitudeProfile` 2351-2355.
[^fc-enums]: FlightComputerAttitudeTrackTarget.cs, FlightComputerAttitudeMode.cs, FlightComputerBurnMode.cs, FlightComputerRollMode.cs, FlightComputerAttitudeProfile.cs, FlightComputerAttitudeProfileEx.cs, VehicleReferenceFrame.cs.
[^vehicle]: Vehicle.cs:692-718, 3742-3788.
[^program]: Program.cs:2560-2566.
