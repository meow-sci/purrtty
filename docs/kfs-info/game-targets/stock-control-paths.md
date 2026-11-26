---
type: Game System Reference
title: Stock control paths for lights and part animations
description: Every way the unmodded game changes light and animation state, traced end to end with file and line and the thread each step runs on, ending with which path a mod should reuse for each operation and whether it may run inside the physics step.
resource: ksa-decomp:KSA/Program.cs
tags: [kfs, game-targets, lights, animation, threading, input-events, ui, hotkeys, staging]
sources:
  - id: program
    resource: ksa-decomp:KSA/Program.cs
    title: Program, the frame loop and key dispatch (decompiled)
  - id: input
    resource: ksa-decomp:KSA/Input.cs
    title: Input default bindings (decompiled)
  - id: input-action
    resource: ksa-decomp:KSA/InputAction.cs
    title: InputAction enum (decompiled)
  - id: input-events
    resource: ksa-decomp:KSA/InputEvents.cs
    title: InputEvents buffers (decompiled)
  - id: vehicle
    resource: ksa-decomp:KSA/Vehicle.cs
    title: Vehicle (decompiled)
  - id: part
    resource: ksa-decomp:KSA/Part.cs
    title: Part (decompiled)
  - id: power-consumer
    resource: ksa-decomp:KSA/PowerConsumer.cs
    title: PowerConsumer (decompiled)
  - id: keyframe-animation-module
    resource: ksa-decomp:KSA/KeyframeAnimationModule.cs
    title: KeyframeAnimationModule (decompiled)
  - id: parachute
    resource: ksa-decomp:KSA/Parachute.cs
    title: Parachute (decompiled)
  - id: parachute-deploy
    resource: ksa-decomp:KSA/ParachuteDeploy.cs
    title: ParachuteDeploy (decompiled)
  - id: vehicle-editor
    resource: ksa-decomp:KSA/VehicleEditor.cs
    title: VehicleEditor (decompiled)
  - id: vehicle-update-state
    resource: ksa-decomp:KSA/VehicleUpdateState.cs
    title: VehicleUpdateState (decompiled)
  - id: physics-bubble
    resource: ksa-decomp:KSA/PhysicsBubble.cs
    title: PhysicsBubble (decompiled)
  - id: universe
    resource: ksa-decomp:KSA/Universe.cs
    title: Universe (decompiled)
  - id: celestial-system
    resource: ksa-decomp:KSA/CelestialSystem.cs
    title: CelestialSystem (decompiled)
  - id: part-tree
    resource: ksa-decomp:KSA/PartTree.cs
    title: PartTree (decompiled)
  - id: sequenced
    resource: ksa-decomp:KSA/ISequenced.cs
    title: ISequenced and its implementers (decompiled)
  - id: kfs-services
    resource: ksa-decomp:KSA.Kfs/KfsServices.cs
    title: KfsServices (decompiled)
  - id: kfs-integration
    resource: ksa-decomp:KSA.Kfs/KfsIntegration.cs
    title: KfsIntegration (decompiled)
  - id: starmap-runtime-mod
    resource: starmap:StarMap.Core/ModRepository/RuntimeMod.cs
    title: StarMap RuntimeMod (GUI hook patch targets)
generated: { by: claude-code/claude-fable-5-2, at: 2026-11-25T21:36:15Z }
status: stable
ksa_build: "2026.11.25.5949"
---

# Purpose

A mod that lets kFS programs drive lights and animations should, where it can, do what the game
already does. This document lists every stock writer of the relevant state, shows the call chain
and the thread, and concludes with a recommendation per operation.

The state in question is small:

| State | Declared | Kind |
| --- | --- | --- |
| `PowerConsumer.LightIsActive` | `public bool` field | light switch command |
| `Vehicle.LightsOn` | `public bool` field | vehicle-wide toggle memory |
| `PowerConsumerState.Active` | state buffer | switch result (powered) |
| `LightModule.Template.*` | shared template object | light appearance |
| `KeyframeAnimationModule.TimeGoal` | `public float` field | animation command |
| `KeyframeAnimationModule.State` | state buffer | animation position |

Background: [LightModule](/game-targets/light-module.md),
[KeyframeAnimationModule](/game-targets/keyframe-animation-module.md),
[part and module model](/game-targets/part-and-module-model.md).

# Frame timeline and thread contexts

Everything below refers to these phases of one main-thread frame (`Program.OnFrame`):[^program][^universe]

| # | Phase | Code | Physics workers |
| --- | --- | --- | --- |
| 1 | wait for the previous step | `PrepareFrame`: `JobSystems.VehicleSolver.Wait()` | finishing |
| 2 | apply step results | `Universe.ApplyVehicleSolvers()` → … → `ModuleStateUpdaters.UpdateBulkStates()` | **idle** |
| 3 | OS input | `Glfw.PollEvents()` → `Program.OnKey` callbacks | **idle** |
| 4 | deferred actions | `KfsIntegration.BeforeInputEvents()`, then `InputEvents.ApplyInputEvents()` | **idle** |
| 5 | derived data | `PartTree.FlushDirtyDerived()`, `FlushDirtyResourceManagers()` | **idle** |
| 6 | start next step | `Universe.ExecuteNextVehicleSolvers()` → `JobSystems.VehicleSolver.ExecuteJobs()` | **running from here on** |
| 7 | UI | `OnDrawUiFrame`, `OnDrawUiViewports`, `OnDrawUiThreadSafe` (part windows), console | running |
| 8 | render | `LightSystem.ClearLights()`, `Render` → `UpdateRenderingResources` → `PartTree.UpdateRenderData` → `LightModule.UpdateRenderData`[^part-tree] | running |

Inside the step (worker threads), per vehicle: the flight computer runs
(`PhysicsBubble.RunFlightComputer` → `KfsDispatch.TryComputeControl`, falling back to
`FlightComputer.ComputeControl`), then `VehicleUpdateState.UpdateModules` runs `Parachute`, …,
`PowerConsumer`, `SolarTracker`, `KeyframeAnimationModule` in that order.[^physics-bubble][^vehicle-update-state]
If the simulation time does not advance (pause), `PhysicsBubble.StepFrameTo` returns before any of
it.[^physics-bubble]

"Solver-idle window" below means phases 2–5.

# Lights

## L1 — the lights hotkey (vehicle-wide toggle)

| Step | Code | Thread / phase |
| --- | --- | --- |
| default binding | `Input.cs:268` — `span[66] = Key(InputAction.ToggleLights, GlfwKey.U)`; `InputAction.ToggleLights = 66` | — |
| key callback | `_window.OnKey += OnKey` (`Program.cs:879`); `Program.OnKey` (`:2102`) → `DispatchKeyEvent` (`:2113`) | main, phase 3 |
| match | in the `GlfwKeyAction.Release` branch, inside `if (GetHoveredCamera().Following is Vehicle vehicle)`: `Input.Matches(in keyEvent, InputAction.ToggleLights)` (`:2254`, `:2276`) | main, phase 3 |
| action | `vehicle.ToggleLights()` (`Program.cs:2278`) | main, phase 3 |
| effect | `Vehicle.ToggleLights` (`Vehicle.cs:1677`): `LightsOn = !LightsOn`; for every **full part** in `Parts.Parts` with a `LightSwitch` whose `Parent.Tree == part.Tree`: `lightSwitch.LightIsActive = LightsOn` | main, phase 3 |

[^input][^input-action][^program][^vehicle]

Notes: it fires on key **release**, targets the vehicle the hovered camera follows (not necessarily
the controlled one), and *sets* every switch to the new vehicle-wide value rather than toggling each.
It is a direct field write with no queue, no sound, no alert and no editor-history entry. Because it
runs in phase 3 the next physics step sees the new value from its first sub-step.

## L2 — the part window "Light Switch" checkbox (per part, with symmetry)

| Step | Code | Thread / phase |
| --- | --- | --- |
| UI root | `Program.OnDrawUiThreadSafe` → `Universe.OnDrawUi(MainViewport)` (`Program.cs:3646`) → `CelestialSystem.OnDrawUi` (`:490`) → `Vehicle.OnDrawUi(IGameViewport)` (`Vehicle.cs:5063`) | main, phase 7 |
| per part | `parts[i].OnDrawUi(this, Program.ControlledVehicle, camera, inViewport, in matrix)` (`Vehicle.cs:5082`) → `Part.OnDrawUi` (`Part.cs:3549`) draws the part popup or the pinned part window and calls `ShowContextMenu(vehicle, controlledVehicle)` (`:3013`) | main, phase 7 |
| the widget | at the end of `ShowContextMenu`: for each `PowerConsumer` in `SubtreeModules`, `powerConsumer.DrawStateInfo(in state)` (`Part.cs:3467–3474`) | main, phase 7 |
| the write | `PowerConsumer.DrawStateInfo` (`PowerConsumer.cs:298`): if `LightSwitch`, `ImGuiHelper.DrawCheckbox("Light Switch", ref value)` wrapped in `VehicleEditor.RecordsHistoryFor(Parent.FullPart.Tree, LStrings.EditorSettingLightSwitch, …)`; on change `SetLightIsActive(value)` (`:302–309`) | main, phase 7 |
| fan-out | `SetLightIsActive` (`:274`) → `VehicleEditor.ForEachModuleWithSymmetry(Parent.FullPart, [this], c => c.LightIsActive = active)` — this consumer, plus the same-index `PowerConsumer` on every part in `Parent.FullPart.SymmetryLink` (`VehicleEditor.cs:1724–1765`) | main, phase 7 |

[^program][^celestial-system][^vehicle][^part][^power-consumer][^vehicle-editor]

This write happens **while the physics step is running** (phase 7). The game accepts the race: it is
one `bool` per consumer.

`VehicleEditor.ForEachModuleWithSymmetry`, `RecordsHistoryFor` and `TrackSliderHistoryFor` are
`internal static`; `PowerConsumer.SetLightIsActive(bool)` is the public entry that includes the
symmetry fan-out.

## L3 — the physics step consumes the switch

`PowerConsumer.UpdateModules` (worker, every step):[^power-consumer]

```csharp
if (current.Module.LightSwitch)      current.State.Active = current.Module.LightIsActive;
else if (current.Module.PowerSwitch) current.State.Active = current.Module.IsOn;
if (current.State.Active)
{
    if (PowerManager.ResourceAvailable(batteryStates)) { /* consume Consumed * dt, record actual draw */ }
    else { current.State.Active = false; current.State.Consumed = Watts.Zero; }
}
else current.State.Consumed = Watts.Zero;
```

This is the **only** "state updater write inside the physics step" for lights. The command is a
module *field*; the *state* is derived from it every step and is not a place to write commands.

## L4 — load

`PowerConsumer.ApplySaveData` (main, load): `state.Active = saveData.Active`, and for a light switch
`LightIsActive = saveData.Active`.[^power-consumer] `Vehicle.DeserializeSave` restores
`LightsOn`.[^vehicle]

## What stock never does to lights

- Nothing in the decompiled assemblies writes any `LightModule.TemplateData` field, a
  `FloatReference.Value` or a `ColorRgbReference` belonging to a light after load. Colour, intensity,
  range and cone angles are content-only.
- There is no `InputEvents` buffer for lights, no `Vehicle.SetEnum` case for them (`SetEnum` handles
  only flight-computer modes, `VehicleEngine` and `KittenEvaAction`), no gauge button and no
  sequence/staging hook.[^vehicle][^input-events]

# Animations

## A1 — the part window Deploy / Retract button and Actuate slider

Same UI root as L2 down to `Part.ShowContextMenu`; then:[^part][^keyframe-animation-module]

| Step | Code | Thread / phase |
| --- | --- | --- |
| loop | `Part.cs:3099–3106`: for each `KeyframeAnimationModule` in `SubtreeModules`, `ShowContextMenu()` | main, phase 7 |
| deploy | `ShowDeployRetract` and committed `DeploymentState == Retracted`: button `EditorSettingDeploy` → `TimeGoal = Shared.Duration; ApplyToMirroredParts(TimeGoal, DeploymentState.Retracted)` (`KeyframeAnimationModule.cs:293–299`) | main, phase 7 |
| retract | `ShowDeployRetract` and `DeploymentState == Deployed`: button `EditorSettingRetract` → `TimeGoal = 0f; ApplyToMirroredParts(TimeGoal, DeploymentState.Deployed)` (`:301–305`) | main, phase 7 |
| status | `"Current State: " + DeploymentState`, then `Extension?.DrawMenuInfo()` (`:306–310`) | main, phase 7 |
| actuate | not `ShowDeployRetract`: `float v = TimeGoal / Shared.Duration; ImGui.SliderFloat(EditorSettingActuate, ref v, 0f, 1f)` → `TimeGoal = v * Shared.Duration` (`:312–319`) | main, phase 7 |

Observations:

- The button is offered **only at rest** (`Retracted` or `Deployed`); while `Deploying` /
  `Retracting` the stock UI shows just the state text. The module itself has no such restriction.
- The mirrored fan-out copies the goal only to counterparts currently in the same state, and only
  for the button, not the slider.
- The slider divides by `Shared.Duration` without a zero check.
- The same `ShowContextMenu()` is used by the vehicle editor's part menu (`VehicleEditor.cs:7823–7830`).[^vehicle-editor]
- Again a direct field write during phase 7, concurrent with the step.

## A2 — parachutes drive their bay doors

`Parachute` binds the first `KeyframeAnimationModule` of its full part and writes its `TimeGoal`
through the private `SetBayDoorsOpen(bool open)` (`TimeGoal = open ? Shared.Duration : 0f`).[^parachute]
Three callers:

| Caller | Reached from | Thread / phase |
| --- | --- | --- |
| `Parachute.Arm()` (`:764`) | staging: a `ParachuteDeploy` sequence module queues `InputEvents.IActivateInputData { Operation = ActivateOp.Parachute }` (`ParachuteDeploy.cs:83`); `ApplyInputEvents` → `ParachuteDeploy.ArmAll()` (`:92`) → `Arm()`. Also the chute context-menu arm button (`Parachute.cs:855`) | main, phase 4 (staging) or phase 7 (menu) |
| `Parachute.Disarm()` (`:773`) | chute context menu (`:872`); closes the doors only when every chute on the part is stowed | main, phase 7 |
| `Parachute.Deploy()` (`:725`) | **`Parachute.UpdateModules` (`:1096–1119`) when `ParachutePhysics.ShouldAutoDeploy(...)`** — and the context-menu deploy button (`:892`) | **worker, inside the physics step**; or main, phase 7 |

[^parachute][^parachute-deploy][^input-events]

The auto-deploy path is the important precedent: **stock code writes
`KeyframeAnimationModule.TimeGoal` from a worker thread during `UpdateModules`**, a few module types
before `KeyframeAnimationModule.UpdateModules` reads it in the same pass.

## A3 — the physics step moves the animation

`KeyframeAnimationModule.UpdateModules` (worker, every step) is the only writer of
`State.TimeCurrent` / `State.DeploymentState` in flight, through
`typeUpdater.GetModulesAndNewStates()`, and the only caller of `ApplyAnimationTransforms` in
flight.[^keyframe-animation-module] `SolarPanel.UpdateModules` reads the committed `DeploymentState` in the
same step.

## A4 — editor and load

`KeyframeAnimationModule.EditorUpdateModules(tree, dt)` (main, vehicle editor frame) writes the
committed state directly via `GetModuleAndAllMutableStatesForInitialization`.[^vehicle-editor]
`ApplySaveData` (main, load) restores goal, position and pose.[^keyframe-animation-module]

## What stock never does to animations

- No `InputEvents` buffer, no hotkey, no `Vehicle.SetEnum` case.
- **Animations cannot be staged.** `ISequenced` is implemented only by `Decoupler`,
  `EngineController`, `ParachuteCut` and `ParachuteDeploy`; `KeyframeAnimationModule` (and
  `LightModule`, `PowerConsumer`) are not sequence modules, so landing legs, solar panels and cargo
  bays cannot be put in a sequence.[^sequenced] The only staging-driven animation is the parachute bay
  door, indirectly through A2.

# How stock kFS defers work to the main thread

For comparison, the existing kFS services never touch non-actuator game objects from the physics
step. `KfsServices` keeps a private `List<MainWork> _main` guarded by `_gate`; `Queue(MainWork)`
appends from the dispatch thread; `MainThread()` swaps the list out under the lock and executes each
item — `StageWork` → `vehicle.Parts.SequenceList.ActivateNextSequence(vehicle)`, `EngineOffWork` →
`vehicle.SetEnum(VehicleEngine.MainShutdown)`, `PlanWork` → `InputEvents.BurnUpdateBuffer.Add(...)`.[^kfs-services]
`KfsIntegration.BeforeInputEvents()` calls `Services.MainThread()` for every host, sorted by id, in
phase 4 — immediately before `InputEvents.ApplyInputEvents()`.[^kfs-integration][^program]

`MainWork`, its subclasses, `Queue` and `_main` are all `private` to `KfsServices`. See
[kFS services, game side](/integration/kfs-services-game-side.md) and
[dispatch and threading](/integration/dispatch-and-threading.md).

# Conclusions: which path to reuse

| Operation | Stock precedent | Reuse | From the physics step? |
| --- | --- | --- | --- |
| Toggle **all** lights of a vehicle | L1 `Vehicle.ToggleLights()` | Call it only if a toggle is wanted; for an explicit on/off replicate its loop as a *set* (also write `Vehicle.LightsOn` so the hotkey stays coherent) | It loops over `Parts.Parts` and writes plain `bool`s. Stock runs it in phase 3. Callable from the step in the same sense as the single write below, but there is no reason to — queue it. |
| Switch **one light part** on/off | L2 `PowerConsumer.SetLightIsActive(bool)` (with symmetry) or the bare field `LightIsActive` (L1 writes the bare field) | For program control of an individually addressed light use the bare field on `module.Parent.FullPart.LightSwitch`. Use `SetLightIsActive` only if mirrored parts should follow | **Yes** for the bare field: one `bool`, read later in the same step by `PowerConsumer.UpdateModules`; stock writes it from the main thread while the step runs. `SetLightIsActive` allocates a delegate and walks symmetry lists — keep it on the main thread. |
| Change colour / intensity / range / cone | **none** | No stock path exists. Use the mod precedent: give the module a private `TemplateData` and edit that (see [zippo](/game-targets/zippo-light-control-reference.md) and [gatOS](/game-targets/gatos-and-other-mod-precedents.md)) | **Defer to the main thread** (phase 4). The only reader is the renderer in phase 8; a deferred write lands in the same frame it would have anyway, and avoids multi-field tearing and allocation inside the step. |
| Deploy / retract | A1 button: `TimeGoal = Shared.Duration` / `0` (+ private mirroring) | Write `TimeGoal`. Mirroring is not reachable without reimplementing `ApplyToMirroredParts` | **Yes.** Stock does it from the worker (A2 auto-deploy). Written before `UpdateModules` of the same step, it starts moving in that step. |
| Set actuation 0…1 | A1 slider: `TimeGoal = v * Shared.Duration` | Same write, with your own clamp | **Yes**, same reasoning. |
| Read light / animation state | UI reads fields and committed state in phase 7; `SolarPanel` reads committed animation state on the worker | Read fields and `tree.States` directly | **Yes.** Committed buffers do not change while the step runs. |

Additional rules that fall out of the traces:

- **No sounds, alerts or UI refresh are attached to any of these writes.** The part window re-reads
  the fields every frame, so it reflects a program's changes automatically. `RecordsHistoryFor` only
  matters to the vehicle editor's undo stack.
- **Power**: an active light switch draws `Consumed` watts per step; an unpowered one goes dark by
  itself.
- **Off-rails**: any animation in motion forces full physics for its vehicle
  (`KeyframeAnimationModule.AnyAnimating`).[^physics-bubble]
- **Controllability**: none of these stock paths checks `Vehicle.IsControllable`; the part window
  lets a player switch lights and deploy parts on an uncontrollable craft (contrast the decoupler and
  engine entries in the same menu, which are disabled). Whether a kFS program should be allowed to do
  the same is a design choice for the extension.
- **If in doubt, queue.** Phase 4 is where kFS already performs its non-actuator effects, it is the
  one place no worker is running, and for visual-only state it costs no latency.

# Modding notes

- Every member named in the recommendations is public: `Vehicle.ToggleLights`, `Vehicle.LightsOn`,
  `Part.LightSwitch`, `PowerConsumer.LightIsActive`, `PowerConsumer.SetLightIsActive`,
  `KeyframeAnimationModule.TimeGoal`, `KeyframeAnimationModule.Shared.Duration`.
- Not reachable without reflection or Harmony: `KeyframeAnimationModule.ApplyToMirroredParts`
  (private), `Parachute.SetBayDoorsOpen` (private), `VehicleEditor.ForEachModuleWithSymmetry` /
  `RecordsHistoryFor` (internal), and kFS's own `KfsServices.Queue` / `MainWork` (private). None is
  required.
- A mod-owned main-thread queue should be drained in phase 4. The stock anchor points there are
  `KfsIntegration.BeforeInputEvents()` (public static) and `InputEvents.ApplyInputEvents()` (public
  static); both run between "results applied" and "next step started". Patch strategy belongs to
  [patch targets and seams](/integration/patch-targets-and-seams.md) and the `/modding/` documents.
- StarMap's `[StarMapBeforeGui]` (a prefix on the private `Program.OnDrawUiFrame`) and
  `[StarMapAfterGui]` (a postfix on the private `Program.OnDrawUiViewports`) run in phase 7, i.e.
  **while the step is running**, and both are skipped when the HUD is hidden because `OnFrame` calls
  those methods only `if (DrawUI)`.[^starmap-runtime-mod][^program] That is where zippo applies its
  changes and where gatOS drains its "Frame" command lane; it is the same timing as the stock part
  window, not the solver-idle window. Both mods add a separate hook for the solver-idle window when
  they need one (see [gatOS and other precedents](/game-targets/gatos-and-other-mod-precedents.md)).

[^program]: Program.cs — `OnKey` registration :879; `OnKey` :2102–2111; `DispatchKeyEvent` :2113–2135; `ToggleLights` match :2254–2280; `PrepareFrame` :2511–2608 (waits :2521–2532, apply :2533–2541, `Glfw.PollEvents` :2549, `KfsIntegration.BeforeInputEvents` :2551, `InputEvents.ApplyInputEvents` :2552, flush :2590–2591, `ExecuteNextVehicleSolvers` :2592); `OnFrame` UI :2636–2648, `LightSystem.ClearLights` :2693; `OnDrawUiThreadSafe` :3593–3655 (`Universe.OnDrawUi` :3646).
[^input]: Input.cs:268.
[^input-action]: InputAction.cs:65.
[^input-events]: InputEvents.cs — buffer list :1067–1117 (no light or animation buffer); `IActivateInputData.Apply` :482–523 (`ActivateOp.Parachute` → `ParachuteDeploy.ArmAll()`); `ApplyInputEvents` :1186–1215.
[^vehicle]: Vehicle.cs — `LightsOn` :297, load :1316; `ToggleLights` :1677–1690; `OnDrawUi` :5063–5083; `SetEnum` :7364–7449.
[^part]: Part.cs — `ShowContextMenu` :3013–3499 (animation loop :3099–3106, power consumers :3467–3474); `OnDrawUi` :3549–3655.
[^power-consumer]: PowerConsumer.cs — `UpdateModules` :235–272; `SetLightIsActive` :274–280; `DrawStateInfo` :298–348 (light checkbox :302–309); `ApplySaveData` :405–421.
[^keyframe-animation-module]: KeyframeAnimationModule.cs — `UpdateModules` :162–214; `ApplyToMirroredParts` :259–285; `ShowContextMenu` :287–321; `ApplySaveData` :401–429.
[^parachute]: Parachute.cs — `_bayDoorAnim` :213; `Deploy` :725–747; `Arm` :764–771; `Disarm` :773–783; `OnFullPartCreated` :785–792; `SetBayDoorsOpen` :794–800; context menu arm/disarm/deploy :855, :872, :892; `UpdateModules` :1096–1119.
[^parachute-deploy]: ParachuteDeploy.cs — queueing :83; `ArmAll` :92–98.
[^vehicle-editor]: VehicleEditor.cs — `ForEachModuleWithSymmetry` :1724–1765; `EditorUpdateModules` calls :3071–3078; part-menu animation controls :7823–7830; `TrackSliderHistoryFor` :8123–8134; `RecordsHistoryFor` :8157–8164.
[^vehicle-update-state]: VehicleUpdateState.cs:387–419.
[^physics-bubble]: PhysicsBubble.cs — `StepFrameTo` :774–799; `AnyAnimating` off-rails :1531–1534, :1680–1694; flight computer in the full-physics loop :1640–1647; `RunFlightComputer` :2077–2086; `UpdateModulesJob` :30–45.
[^universe]: Universe.cs — `ApplyVehicleSolvers` :2061; `ExecuteNextVehicleSolvers` :2277–2290; `OnDrawUi` :1943–1946.
[^celestial-system]: CelestialSystem.cs:490.
[^part-tree]: PartTree.cs — `UpdateRenderData` :1548–1582.
[^sequenced]: ISequenced.cs:5; implementers: Decoupler.cs:9, EngineController.cs:7, ParachuteCut.cs:6, ParachuteDeploy.cs:6.
[^kfs-services]: KfsServices.cs — `MainWork` records :14–20; `_main` :36; `Queue` :543–549; `MainThread` :551–624 (stage :613–616, engine off :621).
[^kfs-integration]: KfsIntegration.cs — `BeforeInputEvents` :158–176.
[^starmap-runtime-mod]: StarMap.Core/ModRepository/RuntimeMod.cs:192–210.
