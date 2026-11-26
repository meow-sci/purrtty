---
type: Game System Reference
title: KeyframeAnimationModule — deploy and actuation animations
description: Complete reference for KSA part animations - one module class whose TimeGoal field is slewed toward by the physics step, how the ShowDeployRetract flag separates deploy/retract parts from 0-to-1 actuated parts, the animation data, the solar-tracking extension, side effects on colliders, solar power and on-rails state, the XML schema with real examples, stock parts, and persistence.
resource: ksa-decomp:KSA/KeyframeAnimationModule.cs
tags: [kfs, game-targets, animation, keyframe-animation, deploy, actuation, solar, landing-legs, threading, xml]
sources:
  - id: keyframe-animation-module
    resource: ksa-decomp:KSA/KeyframeAnimationModule.cs
    title: KeyframeAnimationModule (decompiled)
  - id: keyframe-animation-data
    resource: ksa-decomp:KSA/KeyframeAnimationData.cs
    title: KeyframeAnimationData (decompiled)
  - id: keyframe-animation-extension
    resource: ksa-decomp:KSA/IKeyframeAnimationExtension.cs
    title: IKeyframeAnimationExtension (decompiled)
  - id: solar-tracking-extension
    resource: ksa-decomp:KSA/SolarTrackingExtension.cs
    title: SolarTrackingExtension (decompiled)
  - id: solar-panel
    resource: ksa-decomp:KSA/SolarPanel.cs
    title: SolarPanel (decompiled)
  - id: solar-tracker
    resource: ksa-decomp:KSA/SolarTracker.cs
    title: SolarTracker (decompiled)
  - id: parachute
    resource: ksa-decomp:KSA/Parachute.cs
    title: Parachute (decompiled)
  - id: part
    resource: ksa-decomp:KSA/Part.cs
    title: Part (decompiled)
  - id: vehicle
    resource: ksa-decomp:KSA/Vehicle.cs
    title: Vehicle (decompiled)
  - id: vehicle-editor
    resource: ksa-decomp:KSA/VehicleEditor.cs
    title: VehicleEditor (decompiled)
  - id: vehicle-update-state
    resource: ksa-decomp:KSA/VehicleUpdateState.cs
    title: VehicleUpdateState (decompiled)
  - id: physics-bubble
    resource: ksa-decomp:KSA/PhysicsBubble.cs
    title: PhysicsBubble (decompiled)
  - id: constraint-sim
    resource: ksa-decomp:KSA/ConstraintSim.cs
    title: ConstraintSim (decompiled)
  - id: module-list
    resource: ksa-decomp:KSA/ModuleList.cs
    title: ModuleList (decompiled)
  - id: game-content-tags
    resource: ksa-decomp:KSA/GameContentTags.cs
    title: GameContentTags (decompiled)
  - id: animation-clip-system
    resource: ksa-decomp:KSA/AnimationClipSystem.cs
    title: AnimationClipSystem (decompiled)
  - id: transient-animation
    resource: ksa-decomp:KSA/TransientAnimationData.cs
    title: TransientAnimationData and TransientAnimationLut (decompiled)
  - id: core-landing-gamedata
    resource: ksa-content:Core/CoreLandingAGameData.xml
    title: Landing leg game data
  - id: core-electrical-gamedata
    resource: ksa-content:Core/CoreElectricalAGameData.xml
    title: CoreElectricalA game data (solar tracker, light parts)
  - id: core-utility-gamedata
    resource: ksa-content:Core/CoreUtilityAGameData.xml
    title: CoreUtilityA game data (parachute bay)
  - id: core-service-module-gamedata
    resource: ksa-content:Core/CoreServiceModuleAGameData.xml
    title: Cargo bay game data
  - id: gemini-vehicle
    resource: ksa-content:Core/defaultvehicles/Gemini7/vehicle.xml
    title: Gemini7 default vehicle save
generated: { by: claude-code/claude-fable-5-2, at: 2026-11-26T06:11:10Z }
status: stable
ksa_build: "2026.11.25.5949"
---

# Summary

KSA has **one** part-animation module, `KeyframeAnimationModule`. There is no separate "deploy
animation" class and "actuation animation" class. What the player sees as two kinds of control is a
single boolean, `ShowDeployRetract`, that only changes which widget the stock part window draws:

| `ShowDeployRetract` | Stock UI | Values the stock UI writes to `TimeGoal` | Stock parts |
| --- | --- | --- | --- |
| `true` | **Deploy** / **Retract** button + "Current State" text | `Shared.Duration` or `0` | landing leg, parachute bay doors, tracking solar panel |
| `false` (default) | **Actuate** slider 0.0 – 1.0 | `v * Shared.Duration` | cargo bays, the three aimable light parts |

In both cases the mechanism is identical:

- `TimeGoal` (a public `float` field on the module, in **seconds of animation time**, `0` …
  `Shared.Duration`) is the command.
- `State.TimeCurrent` (in the tree's state buffer) is where the animation actually is.
- Every physics step moves `TimeCurrent` toward `TimeGoal` by the step's `DeltaTime` — one second of
  animation per second of **simulation** time — and re-poses the part's sub-parts.

So "toggle deployed" is `TimeGoal = deployed ? 0 : Shared.Duration`, and "set actuation to *x*" is
`TimeGoal = x * Shared.Duration`. Neither is instantaneous; both are slewed at a fixed rate that
the animation asset determines.

The two values also become visible to a reader at different times. `TimeGoal` is a field: once
written it is what every later reader sees. `State.TimeCurrent` and `State.DeploymentState` are
committed state: the physics job computes them into its own buffer and the main thread commits
them once per frame. Code that runs inside the job before the module update — a flight-computer
pass, and so a kFS program — therefore reads a goal set in the frame's sync window at once, and
the position that goal produces one frame later; several passes in one frame all read the same
position. See [dispatch and threading](/integration/dispatch-and-threading.md#what-a-dispatch-reads-live-fields-and-committed-state).[^physics-bubble-order]

Related: [part and module model](/game-targets/part-and-module-model.md),
[stock control paths](/game-targets/stock-control-paths.md),
[control recipes](/game-targets/control-recipes.md).

# `KeyframeAnimationModule` — every member

`public class KeyframeAnimationModule : ModuleStateful<KeyframeAnimationModule,
KeyframeAnimationModule.State, EmptyStruct, EmptyStruct>, IDisposable`[^keyframe-animation-module]

## Nested types

| Type | Declaration | Contents |
| --- | --- | --- |
| `Template` | `public class Template : TemplateDataBase`, `[XmlType("KeyframeAnimationModule")]` | `DataReference<KeyframeAnimationData.Template>? KeyframeAnimationTemplate` (`<KeyframeAnimation Ref=…/>`), `bool ShowDeployRetract` (`ShowDeployRetract` attribute), `SolarTrackingTemplate? SolarTracking` (`<SolarTracking>`), inherited `Key` |
| `SolarTrackingTemplate` | `public class` | `float DegreesPerSecond` (attribute), `KeyReference<…>? Tracked` (`<Tracked KeyRef=…/>`), `List<KeyReference<…>> ExcludeSubParts`, `HashSet<string>? CreateExcludedKeys()` |
| `DeploymentState` | `public enum` | `Deployed = 0`, `Retracted = 1`, `Deploying = 2`, `Retracting = 3`, `Broken = 4` |
| `SaveData` | `public class SaveData : SaveDataBase`, `[XmlType(TypeName = "KeyframeAnimationData")]` | attributes `TimeGoal` (float), `TimeCurrent` (float), `SolarCurrentAngle` (double) |
| `State` | `public struct` | `float TimeCurrent; DeploymentState DeploymentState;` |

**Not active in this build:** `DeploymentState.Broken` is never assigned or tested anywhere in the
decompiled assemblies.

## Instance fields

| Field | Declaration | Meaning |
| --- | --- | --- |
| `Shared` | `public required KeyframeAnimationData` | the animation curves, **shared** by every module using the same `<KeyframeAnimation>` asset; read-only by convention |
| `TimeGoal` | `public float` | commanded animation time in seconds; `0` at construction |
| `ExcludeSubPartIds` | `public HashSet<string>?` | sub-part ids exempt from the extension's supplemental rotation |
| `_transformsDirty` | `private bool` (initially `true`) | forces one pose application after movement stops |
| `ShowDeployRetract` | `public bool` | copied from the template |
| `Extension` | `public IKeyframeAnimationExtension?` | `SolarTrackingExtension` when the template has `<SolarTracking>`, else `null` |
| inherited | `InstanceId`, `TemplateId` (the component `Key`), `Parent` (the **full part**), `StatesIdx` | |

## Methods

| Method | Signature | Thread it runs on in stock | What it does |
| --- | --- | --- | --- |
| `CreateComponents` | `public static void CreateComponents(Part part, PartTemplate template, TransformDataReference<SubPartTemplate>? instance)` | load / editor | one module per `Template` in `template.Components`; `Shared = template.KeyframeAnimationTemplate.Value.GetInstance()`; builds the solar extension; then `ApplyAnimationTransforms(TimeGoal)` |
| `DeriveDeploymentState` | `public static DeploymentState DeriveDeploymentState(float timeGoal, float timeCurrent)` | any | `goal < current` → `Retracting`; `goal > current` → `Deploying`; equal: `current <= 0` → `Retracted`, else `Deployed` |
| `AnyAnimating` | `public static bool AnyAnimating(ModuleStateList states)` | worker | true if any module has `TimeGoal != State.TimeCurrent` or an extension with `IsAnimating` |
| `UpdateModules` | `public static void UpdateModules(ref readonly ModuleUpdateContext context)` | **worker**, every physics step | the slew loop, below |
| `EditorUpdateModules` | `public static void EditorUpdateModules(PartTree tree, double deltaTime)` | main (vehicle editor)[^vehicle-editor] | same slew, writing the committed state directly, no extension |
| `ApplyToMirroredParts` | `private void ApplyToMirroredParts(float value, DeploymentState deploymentState)` | main (UI) | copies `TimeGoal` to the module with the same `TemplateId` on every symmetry counterpart **whose current state equals `deploymentState`** |
| `ShowContextMenu` | `public bool ShowContextMenu()` | main (UI) | the Deploy/Retract button or the Actuate slider; always returns `false` |
| `ApplyAnimationTransforms` | `public void ApplyAnimationTransforms(float time, doubleQuat? supplementalRotation = null)` | worker (flight), main (editor, load) | poses the sub-parts for `time` |
| constructor | `public KeyframeAnimationModule(string templateId)` | | |
| `CreateStates` | `public override void CreateStates(out State state, out EmptyStruct fxState)` | | `TimeCurrent = 0`, `DeploymentState = Retracted` |
| `GetSaveData` / `ApplySaveData` | overrides | main | persistence, below |

# The slew loop (`UpdateModules`)

Called from `VehicleUpdateState.UpdateModules`, after `PowerConsumer` and `SolarTracker`, before
`Mole`.[^vehicle-update-state] For each module (`current.State` is the step's *new* state copy):[^keyframe-animation-module]

```text
module.Extension?.Update(module, State.TimeCurrent, closestParent, states, Step.DeltaTime)
rot = module.Extension?.Rotation                       // null without an extension

if TimeGoal == TimeCurrent:                            // at rest
    DeploymentState = Derive(goal, current)            // Retracted or Deployed
    apply = rot.HasValue && DeploymentState == Deployed
    if (apply && Extension.ConsumeRotationChange()) || _transformsDirty:
        ApplyAnimationTransforms(TimeCurrent, apply ? rot : null); _transformsDirty = false
    continue

if TimeGoal < TimeCurrent:                             // retracting
    if rot.HasValue:                                   // extension still turned away from zero
        DeploymentState = Retracting
        ApplyAnimationTransforms(TimeCurrent, rot); _transformsDirty = true
        continue                                       // TimeCurrent does NOT move yet
    TimeCurrent -= (float)DeltaTime;  if TimeCurrent < TimeGoal: TimeCurrent = TimeGoal
else:                                                  // deploying
    TimeCurrent += (float)DeltaTime;  if TimeCurrent > TimeGoal: TimeCurrent = TimeGoal

DeploymentState = Derive(goal, current)
ApplyAnimationTransforms(TimeCurrent, rot); _transformsDirty = true
```

Properties that follow directly from this code:

- **Rate.** `TimeCurrent` advances exactly `DeltaTime` per step: 1 animation-second per simulation
  second. A full travel takes `Shared.Duration` seconds of sim time; at time warp it completes
  proportionally faster. There is no speed field, no easing and no acceleration.
- **Any goal, any time.** The loop compares `TimeGoal` with `TimeCurrent` every step, so the goal can
  be changed mid-travel (the animation simply reverses or continues) and can be any value. Nothing
  clamps `TimeGoal` to `[0, Duration]`; `KeyframeAnimationData.EvaluateWorldMatrix` returns the
  cached start pose for `time <= 0` and the cached end pose for `time >= Duration`, so an
  out-of-range goal only makes `TimeCurrent` overshoot numerically while the pose stays at the end
  stop. Clamp it yourself.
- **`DeploymentState` is derived, never stored independently.** At rest it is `Retracted` only when
  `TimeCurrent <= 0`; **any other resting position, including a part-way actuation, is `Deployed`.**
- **"Is moving"** is `TimeGoal != State.TimeCurrent`, or equivalently
  `DeploymentState is Deploying or Retracting`.
- **Progress 0…1** is `State.TimeCurrent / Shared.Duration`; target 0…1 is
  `TimeGoal / Shared.Duration` (guard `Duration <= 0`).
- **Float equality.** The rest test is exact float equality between a field and the state; because
  the loop snaps `TimeCurrent = TimeGoal` on arrival this terminates, but a goal that changes every
  step (a program dithering the value) keeps the module permanently "animating".
- **A non-finite goal never settles.** Read from the comparisons above (not observed in the running
  game): with `TimeGoal = NaN`
  neither `==` nor `<` is true, so the loop takes the "deploying" branch every step, `TimeCurrent`
  grows without bound and `AnyAnimating` stays true. Nothing in the game validates the field.

# How a pose is applied (`ApplyAnimationTransforms`)

For each `Part part` in `Parent.SubParts`:[^keyframe-animation-module]

- If `Shared.PartLookup` has an entry for `part.Id`: evaluate the sub-part's matrix at `time`,
  decompose it and set `part.PositionParentAsmb`, `part.Asmb2ParentAsmb` and `part.Scale` (with the
  supplemental rotation composed in when present and the sub-part is not excluded).
- Otherwise reset the sub-part to its rest pose (`PositionParentAsmbSafe`, `Asmb2ParentAsmbSafe`),
  rotated by the supplemental rotation when one is supplied and the sub-part is not excluded.

Then `Parent.UpdateBounds()` and `NeedsColliderUpdate = true` on every `ColliderModule` in
`Parent.FullPart.SubtreeModules`.

Each pose setter calls `Part.ResetCachedPosMatrixValues()`, which clears the cached matrices and calls
`Tree?.RenderData.InvalidateTransforms(this)`; the `MarkDerivedDirty(DerivedData.AeroMesh)` branch in
that method runs only for full parts, and animations move only sub-parts.[^part]

The method is public and could be called by a mod to force a pose, but the physics step overwrites
the pose on its next pass and nothing else (state, `TimeCurrent`, save data) would agree with it. Use
`TimeGoal`.

# `KeyframeAnimationData` — the shared curves

`public class KeyframeAnimationData`:[^keyframe-animation-data]

| Member | Type | Notes |
| --- | --- | --- |
| `Duration` | `public float` | the largest keyframe time found in the asset's first animation, seconds |
| `PartLookup` | `public Dictionary<string, AnimatedPart>` | keyed by **glTF node name**, which must equal the sub-part's runtime `Part.Id` (the `<SubPart Key=…>` of the part template) |
| `InstanceLookup` | `public static readonly ConcurrentDictionary<Template, KeyframeAnimationData>` | one instance per asset |
| `EvaluateWorldMatrix(AnimatedPart, double time, bool noCache = false)` | `float4x4` | clamps to cached start/end matrices outside `(0, Duration)` |
| `Template` | nested `public class Template : FileReference` | the `<KeyframeAnimation Id=… Path=…/>` asset; `DoLoad()` parses the GLB; `GetInstance()` |
| `AnimatedPart`, `Animation`, `SampleType { Linear, Step, CubicSpline }` | nested | per-node TRS tracks and cached start/end matrices |

`Template.DoLoad()` reads **only `Animations[0]`** of the glTF file, builds translation / rotation /
scale tracks per animated node, and registers in `PartLookup` every *named, un-animated* node that
has an animated ancestor. Interpolation is `Lerp` / `Slerp` between neighbouring keys (or `Step`);
`CubicSpline` is parsed as a sample type but evaluated with the same linear code.

The asset element is bound as content tag `KeyframeAnimation`.[^game-content-tags] The GLB payloads are
not part of the reference content snapshot used for this bundle, so **the actual durations of the
stock animations are Unverified** — read `module.Shared.Duration` at runtime.

# `IKeyframeAnimationExtension` and `SolarTrackingExtension`

```csharp
public interface IKeyframeAnimationExtension
{
    doubleQuat? Rotation { get; }
    bool IsAnimating { get; }
    bool ConsumeRotationChange();
    void Update(KeyframeAnimationModule module, float timeCurrent, IParentBody parentBody, ReadOnlyPhysicsStates states, double deltaTime);
    void OnSave(KeyframeAnimationModule.SaveData data);
    void OnLoad(KeyframeAnimationModule.SaveData data, KeyframeAnimationModule module);
    void DrawMenuInfo();
}
```
[^keyframe-animation-extension]

The only implementation is `SolarTrackingExtension`:[^solar-tracking-extension]

| Member | Notes |
| --- | --- |
| `RotationPerSecond` (`Degrees`, readonly), `TrackingSubPartId` (`string?`, readonly) | from `<SolarTracking DegreesPerSecond=…><Tracked KeyRef=…/>` |
| `CurrentAngle` (`double`, public field, radians), `IsOccluded` (`bool`), `SunAoA` (`double`) | live tracking state, written by the worker |
| `Rotation` | `doubleQuat?` — axis-angle about local X by `CurrentAngle`; `null` when the angle is ~0 while retracted |
| `IsAnimating` | **always `false`** — tracking alone never keeps a vehicle off rails |
| `Update` | `Deployed`: if not occluded, turn toward the brightest star by at most `RotationPerSecond × dt`. `Retracted` / `Retracting`: turn back toward 0 at the same rate; when `|angle| < 1e-6` set `Rotation = null`. `Deploying`: nothing |
| `ConsumeRotationChange` | true when the angle moved ≥ 0.001 rad since last applied |
| `OnSave` / `OnLoad` | `SaveData.SolarCurrentAngle` |
| `DrawMenuInfo` | "SolarAngle: …° Occluded: …" text |

Effect on the deploy toggle: **a tracking panel does not start retracting until it has rotated back
to zero.** While `Rotation` has a value the loop reports `Retracting` and holds `TimeCurrent`. With
the stock 5°/s that adds up to 36 s before the fold begins.

A separate module class `SolarTracker` (its own `ModuleStateful`, XML `<SolarTracker>`) also exists
and is updated just before `KeyframeAnimationModule`; no part in `Content/Core` declares
one.[^solar-tracker]

# Side effects of animation state

| Effect | Mechanism | Evidence |
| --- | --- | --- |
| Sub-part poses, render transforms | `ApplyAnimationTransforms` → pose setters → `RenderData.InvalidateTransforms` | [^keyframe-animation-module][^part] |
| Collision shape | `ColliderModule.NeedsColliderUpdate` → `ConstraintSim.UpdateShape` → `Vehicle.UpdateColliderCompound` rewrites the compound's child poses | [^constraint-sim][^vehicle] |
| Lights on animated sub-parts | `LightModule.UpdateRenderData` uses the sub-part's current matrix, so the beam follows the actuator | see [LightModule](/game-targets/light-module.md) |
| Solar power | `SolarPanel.OnFullPartCreated` binds the **first** `KeyframeAnimationModule` in the full part's subtree; each step `State.Active = (that module's committed DeploymentState == Deployed)`; an inactive panel produces nothing | [^solar-panel] |
| Parachute bay doors | `Parachute.OnFullPartCreated` binds the first `KeyframeAnimationModule` in the full part's subtree; `Arm()` and `Deploy()` set `TimeGoal = Shared.Duration`, `Disarm()` sets `0` once all chutes on the part are stowed | [^parachute] |
| On-rails / physics cost | `KeyframeAnimationModule.AnyAnimating(...)` forces the vehicle **off rails** (full physics) for as long as anything is moving | [^physics-bubble] |
| Aerodynamics, mass, centre of mass | **Inferred: none.** The aero-mesh dirty flag is raised only for full-part pose changes and no static-mass refresh is triggered; animations move sub-parts only | [^part] |

Two consequences worth designing around:

- Because `Deployed` means "at rest and `TimeCurrent > 0`", a tracking solar panel parked at, say,
  30 % by a program **counts as deployed and generates power** once it stops moving.
- A program that keeps changing `TimeGoal` keeps the vehicle in full physics, which matters under
  time warp.

# XML schema and real examples

Two elements are involved: the asset (`<KeyframeAnimation Id Path>`, a file reference at the top
level of an `<Assets>` file) and the component (`<KeyframeAnimationModule>` inside `<Components>` of
a **`<Part>`**, not a sub-part).

**Deploy / retract** — landing leg, complete component:[^core-landing-gamedata]

```xml
<KeyframeAnimation Id="CoreLandingA_Prefab_MediumLandingLegA_Anim" Path="Animations/CoreLandingA_Prefab_MediumLandingLegA_Anim.glb"/>
<Part Id="CoreLandingA_Prefab_MediumLandingLegA" Modify="Patch">
  …
  <EditorTags Modify="Extend">
    <EditorTag Ref="Landing" />
  </EditorTags>
  <Components Modify="Extend">
    <KeyframeAnimationModule Key="LandingLegAnimation" ShowDeployRetract="true">
      <KeyframeAnimation Ref="CoreLandingA_Prefab_MediumLandingLegA_Anim" />
    </KeyframeAnimationModule>
  </Components>
  …
</Part>
```

**Deploy / retract with solar tracking** — the tracking solar panel:[^core-electrical-gamedata]

```xml
<KeyframeAnimation Id="CoreElectricalA_Prefab_SolarPanelB_Anim" Path="Animations/CoreElectricalA_Prefab_SolarPanelB_Anim.glb" />
<Part Id="CoreElectricalA_Prefab_SolarPanelTrackerMediumV1A" Modify="Patch">
  …
  <Components Modify="Extend">
    <KeyframeAnimationModule Key="SolarPanelAnimation" ShowDeployRetract="true">
      <KeyframeAnimation Ref="CoreElectricalA_Prefab_SolarPanelB_Anim" />
      <SolarTracking DegreesPerSecond="5">
        <Tracked KeyRef="CoreStructuralA_Subpart_DriveRotorB1" />
        <ExcludeSubParts>
          <ExcludeSubPart KeyRef="CoreStructuralA_Subpart_DriveHousingB1" />
        </ExcludeSubParts>
      </SolarTracking>
    </KeyframeAnimationModule>
  </Components>
  …
</Part>
<SubPart Id="CoreElectricalA_Subpart_SolarPanelB_CellA" Modify="Patch">
  <Components Modify="Extend">
    <SolarPanel>
      <Produced W="100" />
      <Transform><Rotation Y="1.5708" /></Transform>
    </SolarPanel>
  </Components>
</SubPart>
```

**Actuation (slider)** — no `ShowDeployRetract` attribute. Cargo bay:[^core-service-module-gamedata]

```xml
<KeyframeAnimation Id="CoreServiceModuleA_Prefab_SetA1SingleA_Anim" Path="Animations/CoreServiceModuleA_Prefab_SetA1SingleA_Anim.glb" />
<Part Id="CoreServiceModuleA_Prefab_SetA1SingleA" Modify="Patch" DisplayName="Mk1 Cargo Bay (Single)">
  …
  <Components Modify="Extend">
    <Decoupler Force="1000"> <Connector KeyRef="_connector1" /> </Decoupler>
    <Decoupler Key="HeatshieldMount" Force="1000"> <Connector KeyRef="_connector131" /> </Decoupler>
    <Habitat Key="ServiceBay" />
    <KeyframeAnimationModule Key="CoreServiceModuleA_Prefab_SetA1SingleA_Anim">
      <KeyframeAnimation Ref="CoreServiceModuleA_Prefab_SetA1SingleA_Anim" />
    </KeyframeAnimationModule>
  </Components>
  …
</Part>
```

and an aimable light (the actuator tilts the lamp head; full listing in
[LightModule](/game-targets/light-module.md)):[^core-electrical-gamedata]

```xml
<KeyframeAnimationModule Key="CoreElectricalA_Prefab_LightSmallA_Anim">
  <KeyframeAnimation Ref="CoreElectricalA_Prefab_LightSmallA_Anim" />
</KeyframeAnimationModule>
```

# Stock parts using each kind

Every `<KeyframeAnimationModule>` in `Content/Core` for this build:

| Part (template id) | Display name | Module `Key` (= `TemplateId`) | `ShowDeployRetract` | Extension / coupled module |
| --- | --- | --- | --- | --- |
| `CoreLandingA_Prefab_MediumLandingLegA` | (none) | `LandingLegAnimation` | **true** | — (colliders move with the leg) |
| `CoreUtilityA_Prefab_ParachuteBayA` | Parachute Bay | `ParachuteBayDoorAnimation` | **true** | driven by `Parachute` arm/deploy/disarm |
| `CoreElectricalA_Prefab_SolarPanelTrackerMediumV1A` | (none) | `SolarPanelAnimation` | **true** | `SolarTrackingExtension` (5°/s); gates `SolarPanel` output |
| `CoreServiceModuleA_Prefab_SetA1SingleA` | Mk1 Cargo Bay (Single) | `CoreServiceModuleA_Prefab_SetA1SingleA_Anim` | false | — |
| `CoreServiceModuleA_Prefab_SetA2DoubleA` | Mk1 Cargo Bay (Double) | `CoreServiceModuleA_Prefab_SetA2DoubleA_Anim` | false | — |
| `CoreServiceModuleA_Prefab_SetA3TripleA` | Mk1 Cargo Bay (Triple) | `CoreServiceModuleA_Prefab_SetA3TripleA_Anim` | false | — |
| `CoreServiceModuleA_Prefab_SetA4QuadA` | Mk1 Cargo Bay (Quad) | `CoreServiceModuleA_Prefab_SetA4QuadA_Anim` | false | — |
| `CoreElectricalA_Prefab_LightSmallA` | (none) | `CoreElectricalA_Prefab_LightSmallA_Anim` | false | aims the spotlight sub-part |
| `CoreElectricalA_Prefab_LightSmallB` | (none) | `CoreElectricalA_Prefab_LightSmallB_Anim` | false | aims two spotlights |
| `CoreElectricalA_Prefab_LightSmallC` | (none) | `CoreElectricalA_Prefab_LightSmallC_Anim` | false | aims the floodlight |

[^core-landing-gamedata][^core-utility-gamedata][^core-electrical-gamedata][^core-service-module-gamedata]

There is no antenna, airbrake or control-surface animation in this build's `Content/Core`. The fixed
solar panel (`CoreElectricalA_Prefab_SolarPanelFixedSmallV1A`) has no animation module; its
`SolarPanel.KeyframeAnimationModule` is `null` and it is always active.

# Persistence

`GetSaveData` writes, per module, a component record on the owning `PartRef`:[^keyframe-animation-module]

```xml
<KeyframeAnimationData InstanceOf="ParachuteBayDoorAnimation" LocalInstanceId="994" TimeGoal="0" TimeCurrent="0" SolarCurrentAngle="0" />
```
[^gemini-vehicle]

`InstanceOf` is the module `TemplateId`; `TimeGoal` is the field, `TimeCurrent` the committed state;
`Extension?.OnSave` adds `SolarCurrentAngle`. `GetSaveData` returns `null` (nothing saved) if the tree
has no state list for the type.

`ApplySaveData` restores `TimeGoal`, writes `TimeCurrent` and the derived `DeploymentState` into the
committed state (`GetModuleAndAllMutableStatesForInitialization`), lets the extension reload, and calls
`ApplyAnimationTransforms(saveData.TimeCurrent, …)` so the part is posed immediately. A mid-travel
animation resumes after load. Matching is by module type plus `TemplateId == InstanceOf`, with the
"only module of this type on the part" fallback.[^module-list]

So **everything a program sets through `TimeGoal` is saved by the stock game**, including part-way
actuation values. Nothing extra needs persisting.

# Reading animation state cheaply

```csharp
PartTree? tree = module.Parent.Tree;      // the module's own tree: StatesIdx indexes THAT tree's list
if (tree != null && tree.States.TryGetTypeList<KeyframeAnimationModule, KeyframeAnimationModule.State, EmptyStruct, EmptyStruct>(out var list))
{
    ref readonly KeyframeAnimationModule.State s = ref list.GetState(module);   // committed state
    float duration = module.Shared.Duration;
    float progress = duration > 0f ? s.TimeCurrent / duration : 0f;             // 0..1
    float target   = duration > 0f ? module.TimeGoal / duration : 0f;           // 0..1
    bool  moving   = module.TimeGoal != s.TimeCurrent;
    var   state    = s.DeploymentState;                                          // 0..4
}
```

| Value | Source | Type → suggested cell |
| --- | --- | --- |
| kind | `module.ShowDeployRetract` | bool → u32 (1 = deploy/retract, 0 = actuator) |
| duration | `module.Shared.Duration` | f32 seconds |
| target (seconds / fraction) | `module.TimeGoal`, `/ Duration` | f32 |
| position (seconds / fraction) | `State.TimeCurrent`, `/ Duration` | f32 |
| deployment state | `State.DeploymentState` | enum → u32: 0 Deployed, 1 Retracted, 2 Deploying, 3 Retracting, 4 Broken (unused) |
| is moving | `TimeGoal != TimeCurrent` | bool → u32 |
| solar angle / occluded | `(module.Extension as SolarTrackingExtension)?.CurrentAngle`, `.IsOccluded` | f64 radians → f32, bool |

Use the module's own `Parent.Tree`, not a cached vehicle: after staging the module's `StatesIdx`
refers to its new tree's list. The committed state is one physics step old when read during a step. Inside a step the freshest value
is available through the step's updater
(`vehicleState.UpdateData.Updaters.TryGetOrCreateTypeStateUpdater(out updater)` then
`updater.GetReadOnlyState(module)`). `SolarPanel.UpdateModules` obtains the same updater for the
deployment state but reads it with `GetModuleAndReadOnlyState`, which always returns the committed
value.[^solar-panel]

# Other "animation" types in the same namespace (not part animations)

The file names suggest more than exists for parts: `IAnimation`, `AnimationClipSystem`,
`AnimSkeletonSystem`, `AnimatedRenderable`, `BoneAnimRuntime` and the `Cat*Anim` types are the
**skeletal animation system for kitten characters** (`AnimationClipSystem.CreateAsset` requires a
skeleton and throws without one), and `TransientAnimationData` / `TransientAnimationLut` bake
start-up / shut-down curves for **volumetric engine exhaust** into a lookup texture.[^animation-clip-system][^transient-animation]
None of them move vehicle parts.

# Modding notes

- **Public surface.** `TimeGoal`, `Shared` (`Duration`, `PartLookup`), `ShowDeployRetract`,
  `Extension`, `ExcludeSubPartIds`, `DeriveDeploymentState`, `AnyAnimating`,
  `ApplyAnimationTransforms`, the `State` struct and the state list are all public. Private:
  `_transformsDirty` and `ApplyToMirroredParts`. Driving an animation needs no reflection, publicizer
  or Harmony patch.
- **The command is a plain field write.** `module.TimeGoal = x` is what every stock path does. It is
  safe to do **inside the physics step**: the step reads the field later in the same pass, and stock
  code itself writes it from the worker (`Parachute.UpdateModules` → `Deploy()` →
  `SetBayDoorsOpen`). Written from the main thread (as the stock UI does) it takes effect in the next
  step.
- **Do not write `State` directly.** `TimeCurrent` and `DeploymentState` belong to the slew loop;
  the only stock writers outside it are load and the editor. Snapping an animation by writing the
  committed state would also need `ApplyAnimationTransforms` and would race the worker.
- **Mirroring is a UI feature.** The Deploy/Retract button fans out to symmetry counterparts through
  the private `ApplyToMirroredParts`; the Actuate slider and every direct `TimeGoal` write affect one
  module only. A program addressing modules individually gets no mirroring unless the extension adds
  it.
- **Competing writers.** A parachute bay's door module is re-driven by the chute state machine on
  arm, disarm and deploy; a program's value lasts only until the next chute transition.
- **Distinguishing the two kinds** for the kFS API is `module.ShowDeployRetract`. Classifying by
  what the part *is* (solar, leg, bay, light aim) needs the part: `Parent.SubtreeModules.HasAny<SolarPanel>()`,
  `HasAny<Parachute>()`, `HasAny<LightModule>()`, the template's `EditorTags`, or the module `Key`.
- **Units for the wire.** Fractions `0.0–1.0` are the natural program-facing unit (it is what the
  stock slider shows); convert with `Shared.Duration`. Clamp the fraction — the game does not.
- **Lifetime.** The module object and its `TimeGoal` survive staging and docking; state is carried
  across by module `InstanceId`. Save/load round-trips goal and position.

[^keyframe-animation-module]: KeyframeAnimationModule.cs — `Template` :11–22; `SolarTrackingTemplate` :24–49; `DeploymentState` :51–58; `SaveData` :60–75; `State` :77–82; fields :84–94; `CreateComponents` :96–120; `DeriveDeploymentState` :122–137; `AnyAnimating` :139–160; `UpdateModules` :162–214; `EditorUpdateModules` :216–257; `ApplyToMirroredParts` :259–285; `ShowContextMenu` :287–321 (deploy :293–299, retract :301–305, slider :312–319); `ApplyAnimationTransforms` :323–369; `CreateStates` :376–381; `GetSaveData` :383–399; `ApplySaveData` :401–429.
[^keyframe-animation-data]: KeyframeAnimationData.cs — `AnimatedPart` :12–25; `Animation` :27–54; `SampleType` :56–61; `Template.DoLoad` :70–219 (first animation only :105, duration :169, `PartLookup` fill :181–213); `GetInstance` :221–224; `InstanceLookup` :227; `PartLookup` :229; `Duration` :231; `EvaluateWorldMatrix` :233–252; interpolation :291–333.
[^keyframe-animation-extension]: IKeyframeAnimationExtension.cs:5–20.
[^solar-tracking-extension]: SolarTrackingExtension.cs — fields :9–25; `Update` :33–81; `ConsumeRotationChange` :83–91; `OnSave`/`OnLoad` :93–110; `DrawMenuInfo` :112–120.
[^solar-panel]: SolarPanel.cs — `KeyframeAnimationModule` field :36; `OnFullPartCreated` :88–95; `UpdateModules` gating :120–134, production :139–154.
[^solar-tracker]: SolarTracker.cs:9–60; VehicleUpdateState.cs:416.
[^parachute]: Parachute.cs — `_bayDoorAnim` :213; `Deploy` :725–747; `Arm` :764–771; `Disarm` :773–783; `OnFullPartCreated` :785–792; `SetBayDoorsOpen` :794–800; `UpdateModules` auto-deploy :1096–1119.
[^part]: Part.cs — pose setters :828–891; `ResetCachedPosMatrixValues` :1311–1322; `UpdateBounds` :1324–1329; `ShowContextMenu` animation loop :3099–3106.
[^vehicle]: Vehicle.cs — `UpdateColliderCompound` :2300–2330.
[^vehicle-editor]: VehicleEditor.cs — `EditorUpdateModules` calls :3071–3078; part menu :7823–7830.
[^vehicle-update-state]: VehicleUpdateState.cs:387–419.
[^physics-bubble]: PhysicsBubble.cs — single-vehicle path :1531–1534; full-physics path :1680–1694.
[^constraint-sim]: ConstraintSim.cs — `UpdateShape` :446–460.
[^module-list]: ModuleList.cs — `ApplySaveData` :359–419; `FindExactModule` :424–436.
[^game-content-tags]: GameContentTags.cs:38; AssetBundle.cs:37.
[^animation-clip-system]: AnimationClipSystem.cs:11–58; IAnimation.cs:6–18.
[^transient-animation]: TransientAnimationData.cs:6–36; TransientAnimationLut.cs:12–106.
[^core-landing-gamedata]: Core/CoreLandingAGameData.xml:3–54.
[^core-electrical-gamedata]: Core/CoreElectricalAGameData.xml — light parts :61–179; solar tracker :278–329; solar cell sub-part :331–340; fixed panel :251–276.
[^core-utility-gamedata]: Core/CoreUtilityAGameData.xml:145–173.
[^core-service-module-gamedata]: Core/CoreServiceModuleAGameData.xml — Single :3–49; Double :98–162; Triple :221–303; Quad :372–472.
[^gemini-vehicle]: Core/defaultvehicles/Gemini7/vehicle.xml:2271, :2385.
[^physics-bubble-order]: PhysicsBubble.cs — `RunFlightComputer` before `UpdateModules`: :1505 then :1548 / :1569 (single vehicle); the flight-computer loop :1639-1652 then the `UpdateModulesJob`s :1881-1886 (full physics). KeyframeAnimationModule.cs:162-214 (`UpdateModules` reads `module.TimeGoal`, writes `current.State`).
