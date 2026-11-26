---
type: Concept
title: Ownership and control modes
description: How an image's claims and publications decide whether kFS merely steers the stock flight computer (pilot mode) or drives the actuators itself (full control), which images are refused, and how control is taken, lost and handed back.
resource: ksa-decomp:KSA.Kfs.Host/Ownership.cs
tags: [kfs, host, ownership, control-mode, pilot, full-control, handback, requires-stock]
sources:
  - id: ownership
    resource: ksa-decomp:KSA.Kfs.Host/Ownership.cs
    title: Ownership (decompiled)
  - id: blocks
    resource: ksa-decomp:KSA.Kfs.Host/Blocks.cs
    title: Blocks (decompiled)
  - id: control-mode
    resource: ksa-decomp:KSA.Kfs.Host/ControlMode.cs
    title: ControlMode (decompiled)
  - id: computer-state
    resource: ksa-decomp:KSA.Kfs.Host/ComputerState.cs
    title: ComputerState (decompiled)
  - id: dispatch-result
    resource: ksa-decomp:KSA.Kfs.Host/DispatchResult.cs
    title: DispatchResult (decompiled)
  - id: vehicle-computer
    resource: ksa-decomp:KSA.Kfs.Host/VehicleComputer.cs
    title: VehicleComputer (decompiled)
  - id: host-computer
    resource: ksa-decomp:KSA.Kfs.Host/HostComputer.cs
    title: HostComputer (decompiled)
  - id: kvm-image
    resource: ksa-decomp:KSA.Kfs.Vm.Image/KvmImage.cs
    title: KvmImage (decompiled)
  - id: kfs-vehicle-host
    resource: ksa-decomp:KSA.Kfs/KfsVehicleHost.cs
    title: KfsVehicleHost (decompiled)
  - id: kfs-services
    resource: ksa-decomp:KSA.Kfs/KfsServices.cs
    title: KfsServices (decompiled)
  - id: kfs-write-back
    resource: ksa-decomp:KSA.Kfs/KfsWriteBack.cs
    title: KfsWriteBack (decompiled)
  - id: kc-front-end
    resource: ksa-decomp:KSA.Kfs.KC/KcFrontEnd.cs
    title: KcFrontEnd (decompiled)
  - id: services-json
    resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.services.json
    title: services.json (embedded resource)
generated: { by: claude-code/claude-fable-5-2, at: 2026-11-25T21:30:51Z }
status: stable
ksa_build: "2026.11.25.5949"
---

# The model

kFS thinks of flight software as four blocks in a chain:

```text
NAVIGATION ─▶ GUIDANCE ─▶ ATTITUDE_CONTROL ─▶ ALLOCATION ─▶ actuators
 MSG_NAV_STATE  MSG_ATT_TARGET   MSG_WRENCH_CMD      command block
```

```csharp
[Flags]
public enum Blocks { None = 0, Navigation = 1, Guidance = 2, AttitudeControl = 4, Allocation = 8 }
```

[^blocks]

A program **owns** a block by publishing that block's output message, or — for ALLOCATION — by a
claim recorded in the image. Blocks a program does not own were meant to be supplied by "stock"
implementations. **This build has no stock kFS blocks**; the only alternative to a program is the
game's own C# `FlightComputer`. That reduces the design to two usable modes and makes every other
combination a load error.

# `Ownership`

`public static class Ownership` holds the three decisions, all derived from the image alone.[^ownership]

## `Blocks Of(KvmImage? image)`

| image property | block |
| --- | --- |
| publishes message 257 (`MSG_NAV_STATE`) | `Navigation` |
| publishes message 513 (`MSG_ATT_TARGET`) | `Guidance` |
| publishes message 769 (`MSG_WRENCH_CMD`) | `AttitudeControl` |
| `image.ClaimsAllocation` | `Allocation` |

"Publishes" is the role bit `Role & 2` of the image's message entry; `ClaimsAllocation` is bit 0 of
the image's `Claims` field.[^kvm-image] A `null` image owns nothing.

## `ControlMode ModeOf(KvmImage? image)`

```csharp
public enum ControlMode : byte { Pilot, FullControl }
```

`FullControl` when the image claims ALLOCATION, otherwise `Pilot`.[^control-mode]

## `string? NeedsStock(KvmImage image)` — the load gate

Returns `null` for an acceptable image, otherwise the reason. `VehicleComputer.Load` turns a reason
into `LoadResult.Fail("needs-stock", reason)` and logs `load rejected: needs-stock: …`.

1. **Owning a block without ALLOCATION.** If the image owns any of NAVIGATION, GUIDANCE or
   ATTITUDE_CONTROL and does not claim ALLOCATION:

   > the program owns *X and Y* without claiming ALLOCATION, which needs the stock computer this
   > release doesn't have (kFS D-135): claim ALLOCATION and command the actuators, or publish none
   > of MSG_NAV_STATE, MSG_ATT_TARGET and MSG_WRENCH_CMD and steer with the services

2. **Subscribing to a stock-only message.** `StockOnlyMessages = { 513, 514 }`. If the image
   subscribes to `MSG_ATT_TARGET` or `MSG_GUIDANCE_STATUS` without publishing it:

   > the program subscribes to *MSG_…* without publishing it; only stock guidance published it, and
   > there is no stock computer in this release (kFS D-135)

Together with `HostComputer.Load`'s `host-message` rule (a program may not publish a host-only
message) these are the host's three image-policy checks. They apply to `VehicleComputer` only;
`BenchMachine` applies just the `host-message` rule.[^vehicle-computer][^host-computer]

# The two modes

| | pilot mode | full control |
| --- | --- | --- |
| image | no ALLOCATION claim; publishes none of 0x0101, 0x0201, 0x0301 | claims ALLOCATION; may publish any of them |
| `Mode` | `ControlMode.Pilot` | `ControlMode.FullControl` |
| `Owned` | `Blocks.None` | `Allocation` plus whatever it publishes |
| who computes actuator commands | the game's `FlightComputer.ComputeControl` | the program, through the command block |
| program's levers | services: attitude holds, throttle, engines, stage, burn planning | `thruster[i].pulse_s`, `engine[j].throttle`/`lease_s`, `gimbal[k].y`/`z`; plus `stage`, `plan_*`, `log`, `trace`, `halt` |
| steering services 48–53, 55–57, 66, 67 | queued for the game | rejected: `<name> steers the C# flight computer, which flies only in pilot mode; this program claims ALLOCATION (kFS D-135)` |
| `DispatchResult.Pilot` | `true` | `false` |
| `DispatchResult.Latched` | always `false` | `true` when a tick latched on a controllable vehicle |

[^vehicle-computer][^dispatch-result]

## `requires_stock`

Each service row carries `requires_stock`: `null`, `"GUIDANCE"` or `"ATTITUDE_CONTROL"`. It names
the stock block that would service the request.[^services-json]

- **The host layer never reads `ServiceInfo.RequiresStock`.** Its equivalent is the hard-coded id
  list in `VehicleComputer.ApplyRequest`, which matches the rows with a non-null `requires_stock`
  (48–57, 66, 67), with 54 (`translate`) rejected unconditionally.
- The **kC front end** does read it: a call to such a service is error `KC` 501 when the program
  claims ALLOCATION (`'<name>' steers the game's flight computer, which flies only in pilot mode;
  this program claims ALLOCATION (kFS D-135)`) or when the program itself publishes that block's
  message.[^kc-front-end] So the run-time rejection is normally reachable only from kASM or a
  hand-built image.
- The in-game reference window shows it as mode `any` or `pilot`
  ([reference window](/tooling/reference-window.md)).

# Being "under kFS control"

```csharp
public bool UnderKfsControl =>
    HostFault == null && !_killed &&
    (State == ComputerState.Boot || State == ComputerState.Running || State == ComputerState.Halted);
```

[^vehicle-computer][^computer-state]

| state | under control? | why |
| --- | --- | --- |
| `NoSoftware`, `Standby` | no | nothing started |
| `Boot`, `Running` | yes | the program is flying or steering |
| `Idle` | **no** | every task finished; nothing left to run, so the vehicle is released |
| `Halted` (trap or `halt()`) | **yes** | "commands held": the last latched command block stays in force |
| `Halted` by kill switch or host fault | no | `_killed` / `HostFault` override the state |

A halted full-control computer keeps holding its last level commands until `IsQuiescent`: halted,
more than one second since the last applied latch, and every non-one-shot command field zero. Then
the game stops asking for wake-ups.[^kfs-vehicle-host]

# What the game does with a `DispatchResult`

```csharp
public readonly record struct DispatchResult(bool UnderControl, bool Latched, int TicksRun,
    double NextWakeSeconds, bool Fallback = false, bool Pilot = false);
```

`KfsVehicleHost.DispatchCore` returns `true` when kFS handled the step and `false` when the stock
flight computer should run as if kFS were absent.[^kfs-vehicle-host]

| condition (first match wins) | game behaviour | returns |
| --- | --- | --- |
| `!UnderControl` | nothing further | `false` |
| `Fallback` (vehicle not controllable) | `Services.AfterDispatch(…, fallback: true)` — requests are still applied | `false` |
| `Pilot` | `Services.AfterDispatch`; apply the service override to the pilot's inputs; `FlightComputer.ComputeControl`; `AfterComputeControl`; request a wake-up unless halted | `true` |
| otherwise (full control) | refresh engine performance if ticks ran; `Services.AfterDispatch`; `output.Clear()`; unless quiescent: request a wake-up and, if `Latched`, write pulses, throttles and gimbals to the controllers | `true` |

`Fallback` is `!vehicleControllable`, where the game passes `vehicle.IsControllable`. In fallback
the program keeps ticking and its services are applied, but neither its command block nor the
service override reaches the vehicle (the game logs
`set_throttle: the service override doesn't apply while the vehicle isn't controllable (kFS D-124)`
and the like).[^kfs-services]

# Taking and losing control

Inside `VehicleComputer` three private booleans track the relationship:[^vehicle-computer]

| field | meaning | set | cleared |
| --- | --- | --- | --- |
| `_controlled` | the last dispatch ran under kFS control | every controlled `Dispatch` | `LeaveControl`, `Fault` |
| `_flying` | full control is actually driving a controllable vehicle | `Dispatch` when `FullControl && vehicleControllable` | `LeaveControl`, `Fault`, or `FullControl && !vehicleControllable` |
| `_killed` | kill switch pressed | `KillSwitch` | `TryStart` |

Leaving raises two one-shot flags the game collects after each dispatch:

| flag | raised when | game reaction |
| --- | --- | --- |
| `TakeCSharpReset()` | control ends while `_flying`; a full-control vehicle stops being controllable; a host fault | `KfsWriteBack.CSharpReset(fc)` — clears the stock flight computer's controller state (last control time, steering, TVC and RCS authorities, error integrals, deadband latches) so it restarts cleanly |
| `TakeHandback()` | control ends while `_controlled`; a host fault while controlled | `KfsServices.HandBack(fc, pilotDone)` |

`LeaveControl()` runs on: a dispatch that finds the computer not under control (stopped, idle,
killed, faulted, no grid), `Stop()`, and `KillSwitch()`. `Fault()` does the same work inline.

`KfsServices.HandBack` ends every armed, started or pending burn handle as `Aborted`/`Cancelled`,
forgets what it knew about the pilot's settings, and — unless the pilot defaults were already
applied when kFS was switched off — calls `KfsWriteBack.PilotDefaults` (burn mode manual, custom
target zero, roll decoupled, rate hold in the ecliptic frame, attitude profile back to balanced if
a service changed it), queues a main-engine shutdown for the main thread and logs
`handback: burn mode manual, rate hold at zero rates, engines off`.[^kfs-services][^kfs-write-back]

# Ways control ends

| cause | entry point | state after | handback |
| --- | --- | --- | --- |
| program finished (all tasks done) | tick result → `Idle` | `Idle` | at the next dispatch |
| `kfs stop` | `Stop()` (queued) | `Standby`, no grid | at that dispatch |
| kill switch (`kfs kill`) | `KillSwitch()` (queued) | `Halted`, `Killed` | at that dispatch |
| host exception | `KfsVehicleHost.Fault` → `Fault(reason)` | `Halted`, `HostFault` set, no grid | in the fault handler |
| kFS feature switched off | game: `Computer.Stop()` + event | `Standby` | at that dispatch |
| a new image is loaded | `Load` (queued) | `Standby`, no grid | at that dispatch (the load runs in `DrainQueue`, then the dispatch finds no grid) |
| trap or `halt()` | tick result → `Halted` | `Halted`, **still under control** | none; commands held |

Restarting (`TryStart`) clears `HostFault` and `_killed` and takes control again at the next due tick.

# Modding notes

- `Ownership` is a public static class with three pure functions; `StockOnlyMessages` is a
  `static readonly ushort[]` (its elements are writable, its length is not).
- All three checks are hard-coded against message ids 257, 513, 514 and 769 and the single
  ALLOCATION claim bit. A mod's own messages and services are invisible to them: a new host-published
  message does not affect ownership, and a new service is not subject to the pilot-mode list unless
  the mod adds its own test.
- **Mode matters for an extension's design.** In full control the steering services are rejected
  and `Latched` gates the stock write-back; in pilot mode `DispatchResult.Latched` is always
  `false` even though `VehicleComputer.Applied` is still updated on every latched VM tick. A mod
  that reads extra command-block fields should look at `Applied` (and `TicksLatched`) rather than
  at `DispatchResult.Latched`, and should decide what "not controllable" (`Fallback`) and "halted,
  commands held" mean for its actuators.
- A light or an animation is not an attitude actuator, so a service that controls one arguably
  belongs with `stage` (accepted in both modes) rather than with the pilot-mode-only list.
- Handback is the natural place to decide whether lights or deployables should be returned to a
  default state; there is no hook for it — `KfsServices.HandBack` is a public instance method a
  postfix can observe, and `VehicleComputer.TakeHandback` is consumed by the game.

[^ownership]: Ownership.cs — `StockOnlyMessages` :8; `Of` :10–37; `ModeOf` :39–46; `NeedsStock` :48–66; `Names` :68–76.
[^blocks]: Blocks.cs:5–13.
[^control-mode]: ControlMode.cs:3–7.
[^computer-state]: ComputerState.cs:3–11.
[^dispatch-result]: DispatchResult.cs:3–6.
[^vehicle-computer]: VehicleComputer.cs — `UnderKfsControl` :77–90; `IsQuiescent` :102–112; pilot-mode-only services :218–235; `KillSwitch` :302–311; `needs-stock` at `Load` :315–324; `Mode`/`Owned` :344–345, :381–382; `Fault` :387–406; `Stop` :408–413; `LeaveControl` :415–428; control flags in `Dispatch` :457–472; result :517.
[^host-computer]: HostComputer.cs — `host-message` rule :305–315; state transitions in `RunTick` :445–472.
[^kvm-image]: KvmImage.cs:57.
[^kfs-vehicle-host]: KfsVehicleHost.cs — `Fault` :168–210; feature-off stop :493–502; `DispatchCore` branches :512–616.
[^kfs-services]: KfsServices.cs — `HandBack` :254–277; `HandBackPilot` :279–284; fallback notes :363–388.
[^kfs-write-back]: KfsWriteBack.cs — `PilotDefaults` :33–43; `CSharpReset` :45–73.
[^kc-front-end]: KcFrontEnd.cs — `ServiceOwnership` :204–244.
[^services-json]: services.json — `requires_stock` per row.
