---
type: Subsystem Reference
title: KfsServices — game-side service effects
description: How KfsServices turns drained service requests into game effects, detects pilot overrides, tracks burn handles across a dispatch, and marshals work to the main thread through its MainWork queue.
resource: ksa-decomp:KSA.Kfs/KfsServices.cs
tags: [kfs, integration, services, threading, extension]
sources:
  - id: kfs-services
    resource: ksa-decomp:KSA.Kfs/KfsServices.cs
    title: KfsServices (decompiled)
  - id: kfs-vehicle-host
    resource: ksa-decomp:KSA.Kfs/KfsVehicleHost.cs
    title: KfsVehicleHost (decompiled)
  - id: kfs-write-back
    resource: ksa-decomp:KSA.Kfs/KfsWriteBack.cs
    title: KfsWriteBack (decompiled)
  - id: kfs-integration
    resource: ksa-decomp:KSA.Kfs/KfsIntegration.cs
    title: KfsIntegration (decompiled)
  - id: vehicle-computer
    resource: ksa-decomp:KSA.Kfs.Host/VehicleComputer.cs
    title: VehicleComputer (decompiled)
  - id: host-computer
    resource: ksa-decomp:KSA.Kfs.Host/HostComputer.cs
    title: HostComputer (decompiled)
  - id: service-override
    resource: ksa-decomp:KSA.Kfs.Host.Services/ServiceOverride.cs
    title: ServiceOverride (decompiled)
  - id: burn-handles
    resource: ksa-decomp:KSA.Kfs.Host.Services/BurnHandles.cs
    title: BurnHandles and BurnHandle (decompiled)
  - id: service-request
    resource: ksa-decomp:KSA.Kfs.Vm/ServiceRequest.cs
    title: ServiceRequest (decompiled)
  - id: services-json
    resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.services.json
    title: services.json (embedded table)
  - id: flight-computer
    resource: ksa-decomp:KSA/FlightComputer.cs
    title: FlightComputer (decompiled)
  - id: vehicle
    resource: ksa-decomp:KSA/Vehicle.cs
    title: Vehicle (decompiled)
  - id: input-events
    resource: ksa-decomp:KSA/InputEvents.cs
    title: InputEvents (decompiled)
  - id: sequence-list
    resource: ksa-decomp:KSA/SequenceList.cs
    title: SequenceList (decompiled)
generated: { by: claude-code/claude-fable-5-2, at: 2026-11-25T21:37:08Z }
status: stable
ksa_build: "2026.11.25.5949"
---

# Where `KfsServices` sits

A service call in a program travels through three layers before it changes the game:

```text
kVM tick ── SVC ──► KvmMachine request buffer
   HostComputer.RunTick: machine.DrainRequests(this)                      HostComputer.cs:439       dispatch thread
     HostComputer.ISyscallSink.Request(request)                           :670-676
       ApplyHostRequest   ids 1 log, 2 trace, 3 halt, 16/17 sleep         :747-811   (handled inside the host layer)
       VehicleComputer.ApplyRequest                                       VehicleComputer.cs:185-239
           54 translate → rejected; FullControl + attitude/throttle/burn ids → rejected
           otherwise its listed ids (32, 48-53, 55-57, 64-67) accepted → _cooked.Add(request); an id it does not list → false
       neither → RejectByHost(request, "no vehicle to act on")
KfsVehicleHost.DispatchCore → Services.AfterDispatch                      KfsVehicleHost.cs:530/537/566
   foreach (ServiceRequest r in C.TakeServiceRequests()) Apply(r, in nav, fc, fallback)      KfsServices.cs:186-189   dispatch thread
       immediate effects on the worker FlightComputer copy / ServiceOverride / BurnHandles
       or Queue(MainWork)                                                 :543-549
KfsIntegration.BeforeInputEvents → Services.MainThread()                  :551-624                  main thread, next frame
```

`KfsServices` is the last two stages. The first stages are documented in [services syscall ABI](/vm/services-syscall-abi.md) and
[service request handling](/host/service-request-handling.md); the table itself in [services table](/tables/services-table.md).

```csharp
public sealed class KfsServices           // one per KfsVehicleHost, created in the host constructor
{
    public KfsServices(KfsVehicleHost host);
    private VehicleComputer C => _host.Computer;
}
```

`ServiceRequest` is `public sealed record ServiceRequest(ushort Id, uint[] Args, byte[]? Bytes, int Task = -1, uint[]? Results = null)`;
arguments are raw 32-bit cells, floats are decoded with `BitConverter.UInt32BitsToSingle` (private helper `F`).[^service-request][^kfs-services]

# Members

| Member | Declaration | Thread | Notes |
| --- | --- | --- | --- |
| `StagePresses` | `public uint StagePresses { get; private set; }` | dispatch | Pilot staging presses since the computer's reset; sampled into `MSG_PILOT_INPUT.stage_presses`. |
| `BurnFactory` | `public static Func<OrbitPointCce, double, double3, PatchedConic, Vehicle, Burn> BurnFactory { get; set; } = Burn.Create;` | main | Replaceable burn constructor (`:58`). |
| `Loaded` | `public static Burn? Loaded(FlightComputer fc)` | any | `fc.Burn != null ? fc.BurnPlan.FindFirstExecutableBurn() : null` (`:67-74`). |
| `BeforeDispatch` | `public void BeforeDispatch(VehicleUpdateState vs, FlightComputer fc)` | dispatch | `:76-150` |
| `AfterDispatch` | `public void AfterDispatch(in FlightComputerNavigation nav, FlightComputer fc, bool wasAuto, bool fallback)` | dispatch | `:152-192`; `wasAuto` is unused. |
| `BeforeComputeControl` | `public static BurnFlags BeforeComputeControl(FlightComputer fc)` | dispatch | `:194-197` |
| `AfterComputeControl` | `public void AfterComputeControl(FlightComputer fc, BurnFlags before)` | dispatch | `:233-252` |
| `EndOfCall` | `internal static (BurnStatusState State, BurnEndCause Cause)? EndOfCall(BurnFlags before, BurnFlags after, bool ignitionDenied)` | dispatch | `:204-231` |
| `HandBack` | `public void HandBack(FlightComputer fc, bool pilotDone)` | dispatch | `:254-277` |
| `HandBackPilot` | `public void HandBackPilot(Vehicle vehicle)` | main | `:279-284` |
| `MainThread` | `public void MainThread()` | main | `:551-624` |
| `Apply` | `private void Apply(ServiceRequest r, in FlightComputerNavigation nav, FlightComputer fc, bool fallback)` | dispatch | `:310-449` |
| `Queue` | `private void Queue(MainWork w)` | dispatch | `:543-549` |
| `BurnStart`, `Start`, `CancelAutoBurn`, `EndHandle`, `Wrote`, `PlanFailed`, `Plan` (static) | private | | |
| `BurnFlags` | `public readonly record struct BurnFlags(bool Completed, bool OutOfPropellant, bool StageRanDry, bool Auto)` | | `:30` |
| `Settings` | `private readonly record struct Settings(FlightComputerAttitudeMode Mode, VehicleReferenceFrame Frame, FlightComputerAttitudeTrackTarget Track, double3 Custom, FlightComputerRollMode Roll, float Deadband, float RateLimit)` with `static Settings Of(FlightComputer fc)` | | `:22-28` |
| `MainWork` | `private abstract record MainWork;` `private sealed record StageWork : MainWork;` `private sealed record EngineOffWork : MainWork;` `private sealed record PlanWork(BurnHandle Handle, ushort Service, uint At, double3 DvVlf, UniverseTime? T, List<Burn> Ended) : MainWork;` | | `:14-20` |

Private state: `_gate` (lock object), `_main` (`List<MainWork>`), `_known` (`Settings?`), `_pilotSet` (`HashSet<string>`), `_lastAuto`,
`_lastSequence` (`int?`), `_resetNs`, `_serviceStages`, `_handles` (scratch list), `_profileSet` (`volatile bool`), `_lastLoaded`
(`:32-54`).[^kfs-services]

# Service effects: `Apply`

`fc` is the **worker copy** of the vehicle's flight computer; its fields are copied to the live flight computer when the frame's results
are applied. `Apply` is a `switch (r.Id)` with **no default case** — an id it does not know is silently ignored.[^kfs-services]
The `FlightComputer` helpers it calls are one-liners over public fields.[^flight-computer]

| Id | Name (`services.json`) | Code in `Apply` | Game API touched | Lines |
| --- | --- | --- | --- | --- |
| 32 | `stage` | `Queue(new StageWork());` | later, on the main thread: `vehicle.Parts.SequenceList.ActivateNextSequence(vehicle)` | 389-391 |
| 48 | `hold_direction(dir, roll)` | `Wrote(r, "attitude_mode", "track_target", "custom_target", "roll"); fc.CustomAttitudeTarget = double3.Zero; fc.TrackTarget((FlightComputerAttitudeTrackTarget)args[0]); fc.RollMode = (FlightComputerRollMode)args[1];` | `FlightComputer.TrackTarget` (sets `AttitudeMode = Auto`, `AttitudeTrackTarget`) | 315-320 |
| 49 | `hold_rates()` | `Wrote(r, "attitude_mode", "frame", "track_target", "custom_target"); fc.CustomAttitudeTarget = double3.Zero; fc.RateHold(VehicleReferenceFrame.EclBody);` | `FlightComputer.RateHold` (`AttitudeMode = Auto`, `SetNullRot(frame)`) | 345-349 |
| 50 | `release_attitude()` | `Wrote(r, "attitude_mode", "custom_target"); fc.CustomAttitudeTarget = double3.Zero; fc.AttitudeMode = FlightComputerAttitudeMode.Manual;` | field writes | 350-354 |
| 51 | `set_attitude_profile(p)` | `Wrote(r, "attitude_profile"); fc.SetAttitudeProfile((FlightComputerAttitudeProfile)args[0]); _profileSet = true;` | `FlightComputer.SetAttitudeProfile` (sets `AngleDeadband`, `RateLimit`) | 355-359 |
| 52 | `set_throttle(x)` | `float x = F(args[0]);` if `fallback` → event; `if (x == 0f) CancelAutoBurn(fc); C.Override.SetThrottle(x);` | `ServiceOverride.SetThrottle`; possibly `fc.BurnMode = Manual` | 360-373 |
| 53 | `engines_on()` | if `fallback` → event; `C.Override.SetEngine(on: true);` | `ServiceOverride.SetEngine` | 374-380 |
| 55 | `hold_heading(heading, pitch, roll)` | `Wrote(r, "attitude_mode", "frame", "track_target", "custom_target", "roll"); double z = Math.IEEERemainder((double)F(args[0]) - Math.PI / 2.0, Math.PI * 2.0); fc.AttitudeMode = Auto; fc.AttitudeFrame = VehicleReferenceFrame.EnuBody; fc.AttitudeTrackTarget = Custom; fc.CustomAttitudeTarget = new double3(0.0, F(args[1]), z); fc.RollMode = (FlightComputerRollMode)args[2];` | field writes | 321-331 |
| 56 | `hold_quat(q, roll)` | `Wrote(…same five…); b = new doubleQuat(F(args[0]), F(args[1]), F(args[2]), F(args[3])).Normalized(); a = Concatenate(Concatenate(fc.Ctrl2Asmb, Inverse(fc.Helm2Asmb)), b); frame2Cci = VehicleReferenceFrame.EclBody.GetFrame2Cci(in nav, fc.Burn) ?? doubleQuat.Identity; fc.AttitudeMode = Auto; fc.AttitudeFrame = EclBody; fc.AttitudeTrackTarget = Custom; fc.CustomAttitudeTarget = VehicleReferenceFrame.EclBody.QuaternionToEulerAngles(Concatenate(a, Inverse(frame2Cci))); fc.RollMode = (FlightComputerRollMode)args[4];` | `VehicleReferenceFrameEx.GetFrame2Cci`, `QuaternionToEulerAngles` | 332-344 |
| 57 | `engines_off()` | if `fallback` → event; `CancelAutoBurn(fc); C.Override.SetEngine(on: false);` | `ServiceOverride.SetEngine`; possibly `fc.BurnMode = Manual` | 381-388 |
| 64 | `plan_circularize(at)` → handle | see "Burn planning" | main thread: `InputEvents.BurnUpdateBuffer`, `Burn.Create` | 392-419 |
| 65 | `plan_burn(dv, t_us)` → handle | see "Burn planning" | same | 392-424 |
| 66 | `burn_start(b)` | `BurnStart(r, fc, Loaded(fc));` | `fc.BurnMode = Auto` when accepted | 425-427, 469-541 |
| 67 | `burn_abort()` | if a burn is loaded and `fc.BurnMode == Auto`: `fc.BurnMode = Manual` and end its handle `Aborted`/`Cancelled`; then end every `Armed` un-ended handle `Aborted`/`Cancelled` | field write, `BurnHandles.End` | 428-447 |

Ids that never reach `Apply`:[^vehicle-computer][^host-computer][^services-json]

| Id | Name | Fate |
| --- | --- | --- |
| 1, 2, 3 | `log`, `trace`, `halt` | Handled in `HostComputer.ApplyHostRequest` (console line, trace store, machine halt). |
| 4 | `event` | Reserved in the table (`"reserved": true`). |
| 16, 17 | `sleep_until`, `sleep_until_event` | *Not active in this build:* recorded in `SleepRequest` and logged "…(sleeping and waking are MS7; the computer keeps running)". |
| 54 | `translate` | *Not active in this build:* always rejected — "translate is not serviced (no automatic translation; integration §8.4, D-117 (2))". |
| 48-53, 55-57, 66, 67 in **full-control** mode | | Rejected by `VehicleComputer.ApplyRequest`: "{name} steers the C# flight computer, which flies only in pilot mode; this program claims ALLOCATION (kFS D-135)". These services only work for pilot-mode programs. |
| 64, 65 when 8 plans are live | | Rejected (`BurnHandles.MAX_LIVE_PLANS = 8`); the freshly issued handle ends `Aborted`/`PlanningFailed`. |

The fallback events (ids 52, 53, 57) read "set_throttle: the service override doesn't apply while the vehicle isn't controllable (kFS D-124)"
(and likewise `engines_on`, `engines_off`); the override is still recorded.

`CancelAutoBurn(fc)` (`:451-467`): only when `fc.BurnMode == Auto` and a burn is loaded — sets `fc.BurnMode = Manual` and ends the loaded
burn's handle `Aborted`/`Cancelled`.

## `Wrote` and the pilot set

`Wrote(ServiceRequest r, params string[] settings)` removes each name from `_pilotSet`; for every name that was present it logs
`service {name} changed {setting} set by the pilot` (`:299-308`). The pilot set is informational only — services are never blocked by it.

# Pilot-override detection: `BeforeDispatch`

Runs at the start of a dispatch, only while the computer is under kFS control (`KfsVehicleHost.cs:504-510`).[^kfs-vehicle-host]

| Step | Lines | Behaviour |
| --- | --- | --- |
| Attitude settings | 78-106 | If `_known` (the settings recorded at the end of the previous dispatch) is set, compare with `Settings.Of(fc)`; each difference adds a name to `_pilotSet`: `Mode` → `"attitude_mode"`, `Frame` → `"frame"`, `Track` → `"track_target"`, `Custom` → `"custom_target"`, `Roll` → `"roll"`, `Deadband` or `RateLimit` → `"attitude_profile"`. A difference means something other than kFS changed the flight computer between dispatches — the pilot's UI input (applied in `InputEvents.ApplyInputEvents`) or the stock computer in the fallback path. |
| Auto burn cancelled | 107-121 | If `_lastAuto && fc.BurnMode == Manual`: every handle that is `Started` and not ended ends `Aborted`/`Cancelled`. |
| Reset detection | 123-129 | `long num = C.Grid?.ResetNs ?? 0;` when it differs from `_resetNs` (the computer was started/reset) `StagePresses = 0`. |
| Stage presses | 123, 130-139 | `activeSequence = _host.Vehicle.Parts.SequenceList.ActiveSequence` (read from the live vehicle). If it changed since the last dispatch **and** `Interlocked.Exchange(ref _serviceStages, 0) == 0` (the change was not caused by the `stage` service) → `StagePresses++`. |
| Throttle / engine switch | 140-149 | `var (throttleTaken, enginesTaken) = C.Override.PilotFrame(vs.ManualControlInputs.EngineThrottle, vs.ManualControlInputs.EngineOn, fc.BurnMode == Manual);` → events "pilot took throttle back from the service" / "pilot took engines back from the service". |

`ServiceOverride.PilotFrame` invalidates `ThrottleValid` / `EngineValid` when the pilot's value differs from the one recorded at the
previous call and both that call and this one were in Manual burn mode; `SetThrottle` / `SetEngine` clear the record so the service's own
change is not mistaken for the pilot's.[^service-override] How the override is applied to the stock computer:
[flight computer interplay](/integration/flight-computer-interplay.md).

# Burn-handle lifecycle across a dispatch

`BurnHandles` (on the computer) maps handle ids to `BurnHandle { Id, Service, Delivered, Burn (object), Armed, Started, Ended, Cause, IgnitionNs, DvRemaining, DvVlf }`;
`Pending` means delivered, not yet bound to a game `Burn`, not ended.[^burn-handles]

| Phase | Method | What it does to handles |
| --- | --- | --- |
| Tick | `VehicleComputer.IssueResult` | For ids 64/65 issues a handle and returns its id to the program as the service result, in the same tick (`VehicleComputer.cs:174-183`). |
| Tick | `VehicleComputer.ApplyRequest` | Marks the handle `Delivered` (or ends it if 8 plans are live); handles issued in a tick that overran are ended `PlanningFailed` in `AfterPlayerTick` (`:192-213`, `:241-257`). |
| Before tick | `BeforeDispatch` | Ends `Started` handles if the burn mode went Auto → Manual behind kFS's back. |
| After tick | `AfterDispatch` | (1) Ends handles whose bound `Burn` is no longer in `fc.BurnPlan` (`TryGetBurn` false) → `Aborted`/`Cancelled`. (2) If a burn is loaded and has no handle, **issues one with `Service = null`** and binds it — pilot-made burns get handles too. (3) Starts every `Armed` handle whose burn is now the loaded one (`Start(fc, h, null)`). (4) Applies this dispatch's service requests. (5) Records `_known` and `_lastAuto`. |
| Pilot mode | `BeforeComputeControl` / `AfterComputeControl` | Snapshot `BurnFlags` before the stock `ComputeControl`, compare after: `EndOfCall` returns a result only when the burn mode went Auto → Manual during the call — `Completed` newly set → `Done`/`Completed`; `OutOfPropellant` newly set → `Aborted`/`StageRanDry` or `PropellantOut`; ignition denied → `Aborted`/`IgnitionDenied`; else by the flags' current values; else `Aborted`/`Cancelled`. The handle of the loaded (or last loaded) burn ends accordingly. |
| Leaving control | `HandBack` | Ends every un-ended handle that is `Armed`, `Started` or `Pending` → `Aborted`/`Cancelled`; clears `_known`, `_lastAuto`. |
| Main thread | `MainThread` → `C.Enqueue` | Binds a planned `Burn` to its handle, or ends it `PlanningFailed` — executed on the dispatch thread at the next dispatch. |

`burn_start` (`BurnStart`, `:469-520`):

| Handle state | Result |
| --- | --- |
| Unknown id, or issued by a plan service but never delivered | Rejected: "burn {n} was never issued" |
| Ended `Aborted` with cause `StageRanDry`, and its burn is still the loaded one | `Rearm`, make current, `Start` — a burn can be resumed after staging |
| Ended otherwise | Rejected: "burn {n} has ended ({cause})" |
| `Pending` (plan not built yet) | `Armed = true`, made current; started by `AfterDispatch` once bound and loaded |
| Bound to the loaded burn | Made current, `Start` |
| Bound to another burn | Rejected: "another burn is loaded ahead of burn {n}" (+ " (burn {id})") |

`Start(fc, h, r)` (`:522-541`): clears `Armed`; if `!_host.Vehicle.IsControllable` rejects with "auto burn refused: no control module (ControlsLockout)"
(or, when there is no request, ends the handle `Aborted`/`Cancelled`); otherwise `fc.BurnMode = FlightComputerBurnMode.Auto; h.Started = true;`.

Each end is logged through `C.Event(BurnHandles.EndText(h))` — "burn {id} DONE|ABORTED {cause}".

## Burn planning (ids 64, 65)

Dispatch thread, in `Apply` (`:392-424`): take the handle from `r.Results[0]`; end every *other* handle that is `Armed`, not started and not
ended as `Aborted`/`Superseded` (event "burn {old} superseded by burn {new}"); `C.Handles.Current = h.Id`; collect the game `Burn` objects
of all ended handles into `ended`; then queue:

- 64: `new PlanWork(h, 64, At: args[0], DvVlf: double3.Zero, T: null, ended)`
- 65: `t_us = args[3] | (args[4] << 32)`; `T = UniverseTime.FromNanoseconds((C.Grid?.ResetNs ?? 0) + (Int128)t_us * 1000)`;
  `new PlanWork(h, 65, At: 0, DvVlf: new double3(F(args[0]), F(args[1]), F(args[2])), T, ended)`

Main thread, in `MainThread` → `Plan` (`:572-611`, `:633-681`):

1. For each burn in `ended` still in `vehicle.FlightComputer.BurnPlan`: `InputEvents.BurnUpdateBuffer.Add(new BurnUpdateData { FlightComputer = fc, Burn = b, DeleteBurn = true })`.
2. `Plan(vehicle, fc, work, Universe.GetElapsedTime())`:
   - 64: `orbit = vehicle.Orbit`; `At != 0` → `orbit.GetNextPeriapsisTime(now)`, else `orbit.IsBound() ? orbit.GetNextApoapsisTime(now) : null`;
     missing or not in the future → failure "no apoapsis ahead" / "no periapsis ahead".
   - 65: `T <= now` → failure "the burn time is no longer in the future".
   - Find the patch in `vehicle.FlightPlan.Patches` with `StartTime <= t <= EndTime`; none → "no flight-plan patch holds the burn time".
   - Δv: for 64 `FiniteBurnRefiner.RefineToCircular(patch.Orbit, t, OrbitalTransfers.DvVlfToCircularize(patch.Orbit, t), fc.GetFullThrottlePhases())`; for 65 the given vector.
   - `BurnFactory(patch.Orbit.GetPointAt(t), t.Seconds(), dv, patch, vehicle)`.
3. Failure → `C.Enqueue(_ => PlanFailed(h, serviceName, reason))` (counts a rejected request, event "rejected {service}: planning failed: {reason}", handle ends `Aborted`/`PlanningFailed`).
   Success → `InputEvents.BurnUpdateBuffer.Add(new BurnUpdateData { FlightComputer = fc, Burn = burn, AddBurn = true })` and `C.Enqueue(_ => C.Handles.Bind(h, burn))`.

The burn buffer is applied by `InputEvents.ApplyInputEvents()` immediately after the hook returns (deletes sort before adds:
`BurnUpdateData.CompareTo`), so the new burn is in the plan before the next solver job starts.[^input-events]

# `HandBack` and `HandBackPilot`

`HandBack(FlightComputer fc, bool pilotDone)` (dispatch thread; called when the computer reports a handback after stop, kill, idle, fault or
flag cycle): ends live handles (above), clears `_known`/`_lastAuto`, and unless `pilotDone`:

```csharp
KfsWriteBack.PilotDefaults(fc, _profileSet);   // burn mode Manual, zero custom target, roll Decoupled, RateHold(EclBody), profile Balanced if a service changed it
_profileSet = false;
Queue(new EngineOffWork());                    // main thread: vehicle.SetEnum(VehicleEngine.MainShutdown)
C.Event("handback: burn mode manual, rate hold at zero rates, engines off");
```

`PilotDefaults` is described in [write-back](/integration/write-back.md).[^kfs-write-back]

`HandBackPilot(Vehicle vehicle)` is the main-thread variant used when the flag is turned off: `KfsWriteBack.PilotDefaults(vehicle.FlightComputer, _profileSet); _profileSet = false; vehicle.SetEnum(VehicleEngine.MainShutdown);`
— it writes the **live** flight computer directly. See the caveat in [dispatch and threading](/integration/dispatch-and-threading.md).

`Vehicle.SetEnum(VehicleEngine.MainShutdown)` → `SetAction` → `_manualControlInputs.EngineOn = false` (`Vehicle.cs:7384-7394`, `:7452-7462`).[^vehicle]

# The `MainWork` queue — the template for main-thread actions

This is the pattern kFS uses for every effect that must not happen on a worker thread. It has four parts.

**1. A closed set of work records** (`:14-20`) — immutable, carrying everything the main thread needs, captured on the dispatch thread.

**2. Enqueue under a lock, from the dispatch thread** (`:543-549`):

```csharp
private void Queue(MainWork w)
{
    lock (_gate) { _main.Add(w); }
}
```

**3. Drain once per frame on the main thread** (`:551-624`), called from `KfsIntegration.BeforeInputEvents` for every host in `Id` order:[^kfs-integration]

```csharp
public void MainThread()
{
    List<MainWork> main;
    lock (_gate)
    {
        if (_main.Count == 0) return;
        main = _main;
        _main = new List<MainWork>();       // swap, so work runs outside the lock
    }
    Vehicle vehicle = _host.Vehicle;
    FlightComputer flightComputer = vehicle.FlightComputer;      // the LIVE flight computer
    UniverseTime now = Universe.GetElapsedTime();
    foreach (MainWork item in main)
    {
        // EngineOffWork → vehicle.SetEnum(VehicleEngine.MainShutdown);
        // StageWork     → Interlocked.Increment(ref _serviceStages);
        //                 vehicle.Parts.SequenceList.ActivateNextSequence(vehicle);
        //                 C.StageApplied();
        // PlanWork      → BurnUpdateBuffer deletes/adds + C.Enqueue(…)
    }
}
```

**4. Marshal results back to the dispatch thread with `C.Enqueue`** — `HostComputer.Enqueue(Action<HostComputer>)` is a
`ConcurrentQueue` drained by `DrainQueue()` at the very start of the computer's next dispatch, before any tick runs
(`HostComputer.cs:288-300`, `VehicleComputer.cs:455`).[^host-computer][^vehicle-computer] kFS uses it for `Handles.Bind` and `PlanFailed`, so the handle table
is only ever mutated on the dispatch thread.

Two ways a program can observe completion, both visible in this code:

- **Non-blocking, result later** (burn planning): the service returns a handle immediately; the outcome appears in a host message
  (`MSG_BURN_STATUS`) once the main thread has done the work and the result has been enqueued back.
- **Blocking the program** (staging): `VehicleComputer.ApplyRequest` increments `_stagesPending` when it accepts a `stage` request
  (`VehicleComputer.cs:214-217`); while `AwaitingStage` is true `TickDue` is false and **no tick runs** (`:71`, `:432`, `:477`);
  `MainThread` calls `C.StageApplied()` after `ActivateNextSequence`, decrementing the counter with a CAS loop (`:259-267`). The program
  resumes on the first dispatch after the stage has really happened, so it sees the post-staging vehicle.[^vehicle-computer]

Latency: work queued during frame *N*'s solver job is applied in frame *N+1*'s sync window; a result enqueued back is seen by the
program in the first dispatch of frame *N+1*'s job. `MainThread()` also runs while the simulation is paused; the enqueued result then
waits for the next dispatch.

`SequenceList.ActivateNextSequence(Vehicle)` (`SequenceList.cs:186-219`) activates the next non-empty sequence by calling
`ISequenced.Activate(vehicle)` on its modules; engines, thrusters and decouplers respond by adding `IActivateInputData` to
`InputEvents.IActivateInputBuffer`, which `ApplyInputEvents` applies right after the hook (`InputEvents.cs:460-522`, `:1186-1215`).[^sequence-list][^input-events]

# Modding notes

- `KfsServices` is public and reachable as `host.Services`; `MainThread`, `BeforeDispatch`, `AfterDispatch`, `AfterComputeControl`,
  `HandBack`, `HandBackPilot`, `StagePresses`, `Loaded` and `BurnFactory` are public. `Apply`, `Queue`, the `MainWork` records and
  `_host` are private.
- **`Apply` ignores unknown ids** and `VehicleComputer.ApplyRequest` returns `false` for them, which makes the host reject the request
  with "no vehicle to act on". A mod-added service id therefore needs a hook at `VehicleComputer.ApplyRequest` (to accept it) — and the id
  must first exist in the services table so the VM and compiler accept the call at all ([services table](/tables/services-table.md)).
- **Patching `Apply`:** it is private and has an `in FlightComputerNavigation` parameter. Harmony resolves it with
  `AccessTools.Method(typeof(KfsServices), "Apply")`; the patch method declares the parameter as `in`/`ref FlightComputerNavigation nav`.
  *Unverified:* this patch was not exercised; the tested recipe in [adding a service](/modding/adding-a-service.md) does not need it.
  It only ever sees requests the host layer accepted. A postfix on the public `VehicleComputer.TakeServiceRequests()` sees the same
  list without the awkward signature (but without `fc`/`nav`).
- **Do not reuse kFS's `MainWork` queue** — the record hierarchy is private and `MainThread` ignores unknown subclasses anyway. Copy the
  pattern: your own lock + list per host, filled from the dispatch thread, drained in a postfix on `KfsIntegration.BeforeInputEvents`,
  results returned with `host.Computer.Enqueue(...)`.
- **Pick the completion style deliberately.** Light and animation commands are naturally fire-and-forget (non-blocking). If a program
  must observe the effect before continuing, either publish state in a host message or imitate the stage counter — but note the stage
  counter stalls *all* ticks of the computer, and it is private.
- `MainThread()` runs with no exception handler. kFS's own work items call game code that can throw; a mod's main-thread work should
  catch and report rather than break `Program.PrepareFrame`.
- `KfsServices.BurnFactory` is a public static delegate: a mod can observe or replace burn creation without patching.
- The pilot-set mechanism is a ready-made model for "the player changed something the program had set": snapshot at the end of a
  dispatch, compare at the start of the next.

[^kfs-services]: KfsServices.cs — line numbers given inline and in the tables.
[^service-request]: ServiceRequest.cs:3.
[^kfs-vehicle-host]: KfsVehicleHost.cs:504-510, 514-521, 526-567.
[^vehicle-computer]: VehicleComputer.cs — `AwaitingStage` 71, `TakeServiceRequests` 153-162, `IssueResult` 174-183, `ApplyRequest` 185-239, `AfterPlayerTick` 241-257, `StageApplied` 259-267, `TickDue` 430-441, `Dispatch<TSampler>` 449-518.
[^host-computer]: HostComputer.cs — `Enqueue`/`DrainQueue` 288-300, `RunTick` 409-515 (`DrainRequests` 439), `ISyscallSink.Request` 670-676, `RejectByHost` 737-745, `ApplyHostRequest` 747-811, `ApplyRequest` 813-816.
[^service-override]: ServiceOverride.cs:3-67.
[^burn-handles]: BurnHandles.cs:6-127; BurnHandle.cs:5-45.
[^services-json]: KSA.Kfs.Vm.Tables.Data.services.json — rows with ids 1-4, 16, 17, 32, 48-57, 64-67.
[^kfs-integration]: KfsIntegration.cs:158-176.
[^input-events]: InputEvents.cs:460-522 (`IActivateInputData`), 944-976 (`BurnUpdateData`), 1067, 1186-1215.
[^vehicle]: Vehicle.cs:7364-7462.
[^sequence-list]: SequenceList.cs:136, 186-219.
[^kfs-write-back]: KfsWriteBack.cs:33-43.
[^flight-computer]: FlightComputer.cs:2309-2313 (`RateHold`), 2324-2328 (`TrackTarget`), 2351-2355 (`SetAttitudeProfile`).
