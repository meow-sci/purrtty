---
type: Subsystem Reference
title: Host lifecycle and persistence
description: When a KfsVehicleHost is created and when it dies, what happens to it on staging, docking, vehicle destruction, save loading and star-system changes, and what kFS state does and does not survive a save.
resource: ksa-decomp:KSA.Kfs/KfsVehicleHost.cs
tags: [kfs, integration, lifecycle, persistence, staging, docking, save]
sources:
  - id: kfs-vehicle-host
    resource: ksa-decomp:KSA.Kfs/KfsVehicleHost.cs
    title: KfsVehicleHost (decompiled)
  - id: kfs-integration
    resource: ksa-decomp:KSA.Kfs/KfsIntegration.cs
    title: KfsIntegration (decompiled)
  - id: kfs-console-commands
    resource: ksa-decomp:KSA.Kfs/KfsConsoleCommands.cs
    title: KfsConsoleCommands (decompiled)
  - id: workbench
    resource: ksa-decomp:KSA.Kfs.Host.Workbench/Workbench.cs
    title: Workbench (decompiled)
  - id: vehicle-computer
    resource: ksa-decomp:KSA.Kfs.Host/VehicleComputer.cs
    title: VehicleComputer (decompiled)
  - id: vehicle
    resource: ksa-decomp:KSA/Vehicle.cs
    title: Vehicle (decompiled)
  - id: docking
    resource: ksa-decomp:KSA/DockingPort.cs
    title: DockingPort and DockingEvent (decompiled)
  - id: universe
    resource: ksa-decomp:KSA/Universe.cs
    title: Universe (decompiled)
  - id: celestial-system
    resource: ksa-decomp:KSA/CelestialSystem.cs
    title: CelestialSystem (decompiled)
  - id: program
    resource: ksa-decomp:KSA/Program.cs
    title: Program (decompiled)
  - id: vehicle-data
    resource: ksa-decomp:KSA/VehicleData.cs
    title: VehicleData (decompiled)
  - id: flight-computer
    resource: ksa-decomp:KSA/FlightComputer.cs
    title: FlightComputer (decompiled)
  - id: constants
    resource: ksa-decomp:KSA/Constants.cs
    title: Constants (decompiled)
generated: { by: claude-code/claude-fable-5-2, at: 2026-11-25T21:47:37Z }
status: stable
ksa_build: "2026.11.25.5949"
---

# Summary

- A host is created **on demand, for the player's controlled vehicle only**, the first time the workbench targets "the vehicle".
- A host has no explicit end of life. It is *discovered dead* by `KfsVehicleHost.ReleaseDead()` once its vehicle is disposed, the star
  system changes, or `Vehicle.KfsHost` stops pointing at it.
- **Nothing of kFS is written into a save game.** Programs live as files in the workspace folder; a loaded image, the VM's memory, the
  tick grid, burn handles and the service override exist only in memory and are gone after a load, a system change or a restart.

# Creation

The only constructor call is in `KfsVehicleHost.For(Vehicle)` and the only caller of `For` is the `Workbench.ActiveVehicle` delegate set up
by `KfsIntegration`:[^kfs-vehicle-host][^kfs-integration]

```csharp
ActiveVehicle = () =>
{
    Vehicle controlledVehicle = Program.ControlledVehicle;
    return (controlledVehicle == null) ? null : KfsVehicleHost.For(controlledVehicle);
},
ActiveVehicleIdSource = () => Program.ControlledVehicle?.Id        // lets the workbench ask "is there one?" without creating a host
```

The workbench invokes `ActiveVehicle()` in these places:[^workbench]

| Trigger | Path |
| --- | --- |
| `kfs target vehicle` | `Workbench.SelectTarget("vehicle")` (`Workbench.cs:956-962`) |
| Editor shown while the target is the bench and a vehicle is controlled | `KfsIntegration.ShowEditor()` / `ToggleEditor()` → `Workbench.TargetActiveVehicle()` → `SelectTarget("vehicle")` (`KfsIntegration.cs:183-214`, `Workbench.cs:1066-1073`) |
| `kfs load <file> --vehicle` | `Workbench.Load` (`:782-796`) |
| Build-and-load from the editor while targeting a vehicle | `Workbench.LoadTarget` (`:837-854`) |

A new host has a `VehicleComputer` in state `NoSoftware`; until an image is loaded and started every dispatch merely drains the
computer's action queue and returns "not controlled". Loading and starting are themselves queued actions
(`Computer.Enqueue(c => c.Load(image, layout))`, `…TryStart(DispatchTimeNs, cause)`) executed on the dispatch thread at the vehicle's next
dispatch (`Workbench.cs:809-822`, `:991-1002`).[^workbench]

What is fixed at creation: the frozen controller set and therefore the I/O layout
([controller set](/integration/controller-set-and-layout-changes.md)), the computer's part description (`tables.BenchPart`), the
computer's name (`vehicle.Id`).

# Death

```csharp
public bool IsAlive => !Vehicle.IsDisposed && Vehicle.System == Universe.CurrentSystem && Vehicle.KfsHost == this;
```

`KfsVehicleHost.ReleaseDead()` removes every host that fails this test from `AllHosts` and clears `Vehicle.KfsHost` if it still points
at the host (`KfsVehicleHost.cs:83-93`, `:149-166`). It is called from:[^kfs-vehicle-host][^kfs-integration][^kfs-console-commands]

- `KfsIntegration.BeforeInputEvents()` — every frame while the flag is on, **before** the `Services.MainThread()` loop, so main-thread
  work queued by a host that died is discarded, never applied;
- `KfsConsoleCommands.SetFlag(false)` — before `HandBackAtFlagOff`.

Nothing else happens at death: there is no `Dispose`, no callback, no handback (the vehicle no longer exists or is in another system).
Actions still sitting in the computer's `Enqueue` queue are never run. The workbench notices on its next access
(`Workbench.VehicleTarget`: "vehicle {id} is gone: the target is the bench", `Workbench.cs:82-94`).[^workbench]

`Vehicle.Dispose(bool)` itself does not mention `KfsHost` (`Vehicle.cs:4984-5014`); between disposal and the next `ReleaseDead` the host is
still in `AllHosts` but its vehicle is out of every bubble, so it is no longer dispatched.[^vehicle]

While the flag is **off**, `ReleaseDead` is not called per frame; dead hosts linger in `AllHosts` until the flag is turned on again
(`SetFlag` sweeps only on an on→off transition, so a second `kfs off` does nothing). They are harmless (not dispatched, not drained).

# What happens to a host when the vehicle changes

| Event | Game mechanics | Host consequence |
| --- | --- | --- |
| **Staging / decoupling / undocking** | `Vehicle.Split` detaches the subtree on the *tree-child* side of the connection and builds a **new `Vehicle`** for it with `CreateVehicle(…)`; it copies `_manualControlInputs`, the nav-ball frame, launch time, mission, `HasLaunched`, `IsDebris` and the orbit view — **not `KfsHost`** (`Vehicle.cs:1808-1953`).[^vehicle] | The vehicle object that keeps the part-tree root keeps the host and its running program; the separated vehicle has no host. The host sees a layout change and marks the departed controllers inert. *Inferred:* if the player continues with the separated half (for example a craft whose root part is in a lower stage), the program does **not** follow — a new host must be created and the program reloaded. |
| **Sub-part debris** | `Vehicle.SpawnSubPartDebris` creates a new debris vehicle (`Vehicle.cs:1971-2010`). | Debris has no host. |
| **Docking** | `DockingEvent.Apply(vehicle)` → `DockingPort.Dock(thisVehicle, otherVehicle, …)` → `otherVehicle.MergeFrom(…, thisVehicle, …)`: the *other* vehicle absorbs the parts, calls `UpdateVehicleConfiguration()`, and **`thisVehicle` is disposed** (`DockingEvent.cs:11-25`, `DockingPort.cs:468-488`, `Vehicle.cs:2018-2078`).[^docking][^vehicle] | The surviving vehicle keeps its host (if it had one): counts grow, a layout change is logged with no new inert elements, and the docked-on hardware is not addressable. The consumed vehicle's host dies at the next `ReleaseDead`; its program is lost. *Unverified:* which of the two docking vehicles is the consumed one in a given encounter was not traced. |
| **Destruction, recovery, deletion, crash** | `Universe.DestroyVehicle` → `vehicle.Dispose(…)` (`Universe.cs:2167-2202`); structural-failure and impact events are applied on the main thread in `ApplyVehicleSolvers`.[^universe] | Host dead; dropped in the same frame's `BeforeInputEvents` when the disposal happened in `ApplyVehicleSolvers`, otherwise the next frame's. |
| **Kitten boarding a vehicle** | The EVA vehicle is disposed on boarding (`Universe.DestroyVehicle` with `CrewDisposition.Boarded`). | As destruction, for a host on the EVA vehicle. |
| **Loading a save** | `Universe.DeserializeSave`: `SimulationSpeed = 0`, `JobSystems.WaitAll()`, `CurrentSystem.DestroyAllVehicles()` (every vehicle `Dispose`d), bubbles destroyed, vehicles recreated from save data with `Vehicle.CreateVehicleFromSaveGameData` (`Universe.cs:2570-2584`, `CelestialSystem.cs:154-165`, `Vehicle.cs:1692-1700`).[^universe][^celestial-system] | **Every host dies.** The recreated vehicles are new objects with `KfsHost == null`, even when they have the same `Id`. |
| **Star-system change** | `Program.SwapSystem`: `JobSystems.WaitAll()`, `currentSystem.DestroyAllVehicles()`, new `CelestialSystem` assigned to `Universe.CurrentSystem` (`Program.cs:1920-1927`, `Universe.cs:313-315`).[^program][^universe] | Every host dies (both the `IsDisposed` and the `System == CurrentSystem` tests fail). |
| **Switching the controlled vehicle** | `Program.ControlledVehicle` changes. | Existing hosts are unaffected and **keep running** — dispatch never looks at who is controlled. The workbench keeps targeting the old vehicle until told otherwise; a new host appears only when the workbench targets the new vehicle. |
| **Vehicle editor open** | Simulation speed forced to 0 (`Program.cs:2576-2581`). | No dispatch while it is open; hosts idle. |
| **Flag off, then on** | See [feature flag](/integration/feature-flag.md). | Hosts survive; computers are stopped (image kept) at their first dispatch after the flag returns. |
| **Process restart** (the game restarts itself for some system switches) | New process. | Everything in memory is gone. *Unverified* here; see the repository's KSA compatibility notes. |

# Computer state transitions that matter to the game

These are host-layer facts (see [vehicle computer](/host/vehicle-computer.md)); listed because they decide who flies the vehicle:[^vehicle-computer]

| State / flag | Under kFS control? | Left by |
| --- | --- | --- |
| `NoSoftware`, `Standby` | no | `Load` → `Standby`; `TryStart` → `Boot`/`Running`/`Idle` |
| `Boot`, `Running` | yes | trap or `halt()` → `Halted`; nothing left to run → `Idle`; `Stop` → `Standby` |
| `Idle` | **no** — the next dispatch calls `LeaveControl()` and hands back | `kfs start` / `kfs reset` |
| `Halted` | **yes**, commands held | `kfs start`, `kfs reset`, `kfs stop`, `kfs kill` |
| host fault (`HostFault != null`) | no | `Load`, `TryStart` |
| kill switch (`_killed`) | no | `TryStart` |

# Persistence

## What the game saves

A search of every non-kFS namespace finds no reference to kFS in any save-data type; `Vehicle.KfsHost` is a plain property that no
serializer touches, and `VehicleData` (the per-vehicle save record) has no kFS field.[^vehicle-data][^vehicle] So a save contains **no program, no
image, no computer state, no handle table, no layout**.

What a save *does* contain is the stock state a program had been driving, because that belongs to the game:

| Saved stock state | Where | Consequence after loading |
| --- | --- | --- |
| Flight-computer settings: `BurnMode`, `AttitudeMode`, `AttitudeFrame`, `AttitudeTrackTarget`, `CustomAttitudeTarget`, `ManualThrustMode`, `AngleDeadband`, `RateLimit`, `RollMode`, `RCSMode`, attitude target, deadband states, the loaded burn | `FlightComputer.SerializeSave` / `DeserializeSave` (`FlightComputer.cs:362-448`)[^flight-computer] | A vehicle saved while a pilot-mode program was holding an attitude or running an auto burn reloads with the **stock** computer continuing that mode on its own — no handback was performed, and no program is supervising. |
| The burn plan, including burns a program planned | `BurnPlan.SerializeSave` | Planned burns survive; their kFS handles do not. |
| `ActiveSequence`, engine/thruster `ActiveInStage` | `VehicleSaveData.ActiveSequence`, controller `SaveData` | Staging done by a program is permanent. |

*Unverified:* whether per-module command state written by a full-control program (`CommandThrottle`, `CommandBurnTime`, gimbal commands)
is part of the saved module state was not traced.

## What lives on disk outside saves

| Item | Location |
| --- | --- |
| The workspace: `.c`, `.h`, `.kasm`, `.kinc` sources, generated `vehicle.h` / `vehicle.kinc`, examples | `Path.Combine(Constants.DocumentsFolderPath, "kfs")` = `<Personal>/My Games/Kitten Space Agency/kfs` unless changed with `kfs ws` (`KfsIntegration.cs:99`, `Constants.cs:289`)[^kfs-integration][^constants] |

See [workspace and files](/tooling/workspace-and-files.md). Whether the workbench persists any of its own settings (workspace path, last
file) is covered there.

## What lives only in memory

`KfsPrototype.Enabled` (re-read from the `KFS_PROTOTYPE` environment variable at each process start), `KfsVehicleHost.FrameBudgetMs`,
every host and computer, the bench machine, the workbench's last build and loaded image (`Workbench.LoadedImage`, `Loaded`), console and
trace buffers.

Practical consequence for players, read from the above: after **any** load, a program has to be loaded and started again
(`kfs load <file> --vehicle`, `kfs start`), and it starts from its initial state.

# Modding notes

- **There is no creation or destruction hook.** A mod that keeps per-vehicle state next to a host must either key it weakly on the host
  (`ConditionalWeakTable<KfsVehicleHost, T>` — entries disappear when the host is collected) or sweep it itself: `IsAlive` is public, and
  a postfix on `KfsVehicleHost.ReleaseDead()` runs exactly when kFS does its own sweep. See
  [patch targets and seams](/integration/patch-targets-and-seams.md).
- **A host is not the vehicle.** After staging the program stays with the root side of the split; after docking it survives only on the
  absorbing vehicle; after a load it is gone. Mod state keyed on `Vehicle` or on `Vehicle.Id` will not line up with host lifetime — key it
  on the host.
- **Anything a mod wants to survive a save must be saved by the mod** (kFS offers no storage), or be re-derivable from the vehicle.
  Conversely, effects a mod applies to stock part state (a light switched on, a part deployed) *are* saved by the game if that state is
  part of the module's save data — exactly like kFS's flight-computer settings — and will be there after a load with no program running.
  Whether light and animation state is saved: [light module](/game-targets/light-module.md),
  [keyframe animation module](/game-targets/keyframe-animation-module.md).
- Hosts for non-controlled vehicles are possible (`KfsVehicleHost.For(anyVehicle)` is public and the dispatch does not care who is
  controlled) but the stock UI never creates them; the workbench can only *target* the controlled vehicle.
- `Vehicle.KfsHost` has a public setter. Setting it to `null` orphans the host (it fails `IsAlive` and is swept); the game does this
  nowhere except in `ReleaseDead`.
- Main-thread work queued for a host that dies before the next sync window is silently dropped. A mod queue should behave the same way
  (check `host.IsAlive` when draining) rather than act on a disposed vehicle.

[^kfs-vehicle-host]: KfsVehicleHost.cs:83-93 (`IsAlive`), 105-118 (constructor), 139-147 (`For`, `Find`), 149-166 (`ReleaseDead`).
[^kfs-integration]: KfsIntegration.cs:93-121 (`Workbench`), 158-176 (`BeforeInputEvents`), 183-214 (`ShowEditor`, `ToggleEditor`).
[^kfs-console-commands]: KfsConsoleCommands.cs:244-258.
[^workbench]: Workbench.cs:78-94, 782-854, 944-1002, 1066-1073.
[^vehicle-computer]: VehicleComputer.cs:77-90, 302-311, 313-350, 362-428; HostComputer.cs:335, 366-377, 393-402, 445-472.
[^vehicle]: Vehicle.cs — `KfsHost` 567, `CreateVehicleFromSaveGameData` 1692-1700, `Split` 1808-1953, `SpawnSubPartDebris` 1971-2010, `MergeFrom` 2018-2078, `Dispose` 4984-5014.
[^docking]: DockingEvent.cs:11-25; DockingPort.cs:468-488.
[^universe]: Universe.cs — `CreateSystem` 313-315, `DestroyVehicle` 2167-2202, `DeserializeSave` 2570-2584.
[^celestial-system]: CelestialSystem.cs:154-165.
[^program]: Program.cs:1920-1927 (`SwapSystem`), 2576-2581.
[^vehicle-data]: VehicleData.cs:6-36.
[^flight-computer]: FlightComputer.cs:362-448.
[^constants]: Constants.cs:289.
