---
type: Game System Reference
title: LightModule — part lights, their switch, and how they reach the renderer
description: Complete reference for KSA part lights - LightModule and its shared TemplateData, the PowerConsumer light switch that provides on/off, colour, intensity, range and cone angles with units and limits, the per-frame render path, the XML schema with real examples, the stock parts that carry lights, and persistence.
resource: ksa-decomp:KSA/LightModule.cs
tags: [kfs, game-targets, lights, light-module, power-consumer, rendering, threading, xml]
sources:
  - id: light-module
    resource: ksa-decomp:KSA/LightModule.cs
    title: LightModule (decompiled)
  - id: power-consumer
    resource: ksa-decomp:KSA/PowerConsumer.cs
    title: PowerConsumer (decompiled)
  - id: power-consumer-state
    resource: ksa-decomp:KSA/PowerConsumerState.cs
    title: PowerConsumerState (decompiled)
  - id: part
    resource: ksa-decomp:KSA/Part.cs
    title: Part (decompiled)
  - id: part-tree
    resource: ksa-decomp:KSA/PartTree.cs
    title: PartTree (decompiled)
  - id: part-tree-render-data
    resource: ksa-decomp:KSA/PartTreeRenderData.cs
    title: PartTreeRenderData (decompiled)
  - id: vehicle
    resource: ksa-decomp:KSA/Vehicle.cs
    title: Vehicle (decompiled)
  - id: vehicle-data
    resource: ksa-decomp:KSA/VehicleData.cs
    title: VehicleData save record (decompiled)
  - id: program
    resource: ksa-decomp:KSA/Program.cs
    title: Program, the frame loop (decompiled)
  - id: light
    resource: ksa-decomp:KSA.Rendering.Lighting/Light.cs
    title: Light struct (decompiled)
  - id: light-enums
    resource: ksa-decomp:KSA.Rendering.Lighting/ELightFlags.cs
    title: ELightFlags and ELightType (decompiled)
  - id: clustered-light-system
    resource: ksa-decomp:KSA.Rendering.Lighting/ClusteredLightSystem.cs
    title: ClusteredLightSystem (decompiled)
  - id: light-utils
    resource: ksa-decomp:KSA.Rendering.Lighting/LightUtils.cs
    title: LightUtils (decompiled)
  - id: raytracing-renderer
    resource: ksa-decomp:KSA.Rendering.Raytracing/RaytracingRenderer.cs
    title: RaytracingRenderer (decompiled)
  - id: float-reference
    resource: ksa-decomp:KSA/FloatReference.cs
    title: FloatReference (decompiled)
  - id: color-rgb-reference
    resource: ksa-decomp:KSA/ColorRgbReference.cs
    title: ColorRgbReference (decompiled)
  - id: color-preset
    resource: ksa-decomp:Brutal.Numerics/Color.cs
    title: Brutal.Numerics.Color.Preset (decompiled)
  - id: transform-reference
    resource: ksa-decomp:KSA/TransformReference.cs
    title: TransformReference (decompiled)
  - id: attached-internal
    resource: ksa-decomp:KSA/AttachedInternal.cs
    title: AttachedInternal (decompiled)
  - id: shader-light-prepass
    resource: ksa-content:Core/Shaders/Lighting/LightPrePass.comp
    title: LightPrePass.comp (clustered light shading)
  - id: shader-punctual
    resource: ksa-content:Core/Shaders/Lighting/PunctualLighting.glsl
    title: PunctualLighting.glsl (forward light shading)
  - id: shader-light-data
    resource: ksa-content:Core/Shaders/Lighting/LightData.glsl
    title: LightData.glsl (GPU light struct)
  - id: shader-mesh-indirect
    resource: ksa-content:Core/Shaders/Mesh/MeshIndirect.frag
    title: MeshIndirect.frag (part mesh shading, emissive flags)
  - id: core-electrical-gamedata
    resource: ksa-content:Core/CoreElectricalAGameData.xml
    title: CoreElectricalA game data (light parts)
  - id: core-electrical-assets
    resource: ksa-content:Core/CoreElectricalAAssets.xml
    title: CoreElectricalA assets (light part sub-part layout)
  - id: part-assets
    resource: ksa-content:Core/PartAssets.xml
    title: PartAssets.xml (LightPart)
  - id: core-command-a
    resource: ksa-content:Core/CoreCommandAGameData.xml
    title: Mk1 Command Pod game data
  - id: core-command-b
    resource: ksa-content:Core/CoreCommandBGameData.xml
    title: Mk1 Lander game data
  - id: core-iva-space-a
    resource: ksa-content:Core/CoreIVASpaceAGameData.xml
    title: Medium capsule IVA interior game data
  - id: gemini-vehicle
    resource: ksa-content:Core/defaultvehicles/Gemini7/vehicle.xml
    title: Gemini7 default vehicle save
  - id: gatos-light-analysis
    resource: gatos:docs/LIGHT_PART_ANALYSIS.md
    title: gatOS light part analysis (secondary source)
generated: { by: claude-code/claude-fable-5-2, at: 2026-11-25T21:36:15Z }
status: stable
ksa_build: "2026.11.25.5949"
---

# Summary

A part light in KSA is three cooperating things, none of which is a "light object with state":

| Concern | Lives in | Mutable at runtime by stock code? |
| --- | --- | --- |
| The emitter definition (type, position, range, colour, intensity, cone) | `LightModule.Template`, a `LightModule.TemplateData` object **shared by every light created from the same XML `<Light>` element** | No — stock code only reads it |
| On/off | `PowerConsumer.LightIsActive` on the **full part's** light-switch `PowerConsumer` (`Part.LightSwitch`), plus that consumer's simulated `PowerConsumerState.Active` (has power) | Yes — hotkey and part window |
| The glowing lens on the mesh | per-part render state bit 64 ("no emissive") derived from the switch each frame | derived |

`LightModule` itself has exactly one instance field (`Template`) and one computed property
(`IsActive`). Every frame the renderer rebuilds every visible light from the template. That is why
on/off is a field write on a *different module*, and why changing colour, brightness or cone angle
means changing (or first replacing) the template object.

See [part and module model](/game-targets/part-and-module-model.md) for the surrounding object model,
[stock control paths](/game-targets/stock-control-paths.md) for who writes the switch, and
[control recipes](/game-targets/control-recipes.md) for code.

# `LightModule` — every member

`public class LightModule : Module<LightModule>, IDisposable`[^light-module]

| Member | Declaration | Notes |
| --- | --- | --- |
| `TemplateData` | `public class TemplateData : TemplateDataBase`, `[XmlType(TypeName = "Light")]` | the XML `<Light>` element; see next table |
| `PointLightsRayTrace` | `public static List<Light>` | filled each frame with ray-traced point lights; read and cleared by `RaytracingRenderer` |
| `SpotLightsRayTrace` | `public static List<Light>` | **Not active in this build:** appended to by `UpdateRenderData` but never read or cleared anywhere in the decompiled assemblies |
| `Template` | `public TemplateData Template;` | **public, reassignable field**; initially the shared template object |
| `IsActive` | `public bool IsActive { get; }` | `Template.DisableInIva ? true : !Parent.FullPart.IsLightSwitchedOff()` |
| constructor | `public LightModule(string templateId, TemplateData template)` | `TemplateId` = the component `Key` |
| `CreateComponents` | `public static void CreateComponents(Part part, PartTemplate template, TransformDataReference<SubPartTemplate>? instance)` | one module per `TemplateData` in `template.Components`, `Parent = part`, added to `part.Modules` |
| `UpdateRenderData` | `public void UpdateRenderData(in double4x4 matrixVehicleAsmb2Ego, IViewport viewport, bool isEditedVehicle, int frameIndex)` | builds and submits one `Light` for this viewport |
| inherited | `InstanceId` (`ulong`), `TemplateId`, `Parent` (`Part`), `Dispose()` (no-op), `GetSaveData` (returns `null`) | no save data, no state buffer, no UI |

`LightModule` does not override `GetSaveData`, `ApplySaveData`, `OnDrawUi` or `DrawsUi`, and is not
`ISequenced`/`IActivate`: it cannot be staged, has no context-menu entry of its own and saves nothing.

## `LightModule.TemplateData`

| Field | Type | XML | Default | Meaning / unit |
| --- | --- | --- | --- | --- |
| `Type` | `LightType` enum `{ Spot = 0, Point = 1 }` | `<Type>` | `Spot` | note the template enum order differs from the renderer's `ELightType { Point = 0, Spot = 1 }` |
| `Transform` | `TransformReference` | `<Transform><Position X Y Z/><Rotation X Y Z/></Transform>` | identity | position in the parent (sub-)part's frame, metres; rotation as XYZ Euler **radians**; a spot shines along the rotated local **+X** |
| `Range` | `FloatReference` | `<Range Value=…/>` | `1` | metres; hard cut-off radius of the falloff window; a range that `IsNearlyZero()` suppresses the light entirely |
| `Intensity` | `FloatReference` | `<Intensity Value=…/>` | `1` | unitless multiplier, see [photometry](#photometry-what-intensity-range-and-the-angles-mean) |
| `ColorRgb` | `ColorRgbReference` | `<Color R G B/>` or `<Color Name=…/>` | `Color.Gray` | linear RGB floats, nominal 0–1 |
| `InnerAngle` | `FloatReference` | `<InnerAngle Value=…/>` | `π/8` (0.3927 rad, 22.5°) | spot only; **half-angle in radians** of the full-brightness core |
| `OuterAngle` | `FloatReference` | `<OuterAngle Value=…/>` | `π/4` (0.7854 rad, 45°) | spot only; **half-angle in radians** where the cone reaches zero |
| `RayTracing` | `bool` | `<RayTracing>` | `false` | route to the ray-traced IVA light list when ray tracing is active |
| `DisableInIva` | `bool` | `<DisableInIva>` | `false` | hide in IVA camera mode **and** ignore the light switch (always on, draws no power) |
| `Key` (inherited) | `string` | `Key=` attribute | `"Light"` | becomes `LightModule.TemplateId` |
| `OnDataLoad(Mod)` | override | | | calls `Transform.OnDataLoad(mod)` and `ColorRgb.OnDataLoad(mod)` |

The value holder types matter for mutation:

- `FloatReference` is a class with a public `float Value` field and an implicit conversion to
  `float`.[^float-reference] Writing `template.Intensity.Value = x` is all it takes — but the
  `FloatReference` object is shared along with the template.
- `ColorRgbReference` has public `float R, G, B`, a public `IndexedColor IndexedColor` (default
  `IndexedColor.Invalid`) and `public Color.Preset Value { get; protected set; }`. The renderer uses
  the implicit `float3` conversion, which returns **`Value`**, not `R/G/B`. `Value` is recomputed only
  by `public virtual void OnDataLoad(Mod mod)`: if `IndexedColor != Invalid` it first overwrites
  `R/G/B` from the named colour, then sets `Value = new float3(R, G, B)`. The `mod` argument is not
  used. The `ColorRgbReference(float3)` constructor sets `R/G/B` but **not** `Value`.[^color-rgb-reference]
  So a colour change is: set `R`, `G`, `B`; set `IndexedColor = IndexedColor.Invalid`; call
  `OnDataLoad(null)`.
- `Color.Preset` is a 28-byte explicit-layout struct (a `float4`, a `ushort4` and a `byte4`
  pre-packed from the same colour).[^color-preset] Assigning it is not atomic.
- `TransformReference.PositionValue` / `RotationValue` are computed properties over nullable
  `Vector3Reference` fields; the getters allocate nothing but re-evaluate the Euler→quaternion
  conversion on every read.[^transform-reference]

# On/off: the light switch

## Resolution: `Part.LightSwitch`

`Part.LightSwitch` is a `public PowerConsumer?` field filled by `Part.ResolveLightSwitch()`:[^part]

1. the first `PowerConsumer` in **this part's own** `Modules` whose `LightSwitch` field is `true`;
2. otherwise, if this part is an *attached internal* (an IVA interior attached to its tree parent
   through an `AttachedInternal` module), the tree parent's full part's `LightSwitch`;
3. otherwise `null`.

`ResolveLightSwitch` runs from `ResetModuleProperties()` (construction, sub-part add/remove) and from
`AttachedInternal.ResyncTransformsAllParts`.[^part][^attached-internal] For a **sub-part** it always
yields `null` (sub-parts own no `PowerConsumer` in stock content and have no `TreeParent`), which is
why every consumer goes through `Parent.FullPart`.

## The predicate: `Part.IsLightSwitchedOff()`

```csharp
public bool IsLightSwitchedOff()
{
    PowerConsumer lightSwitch = LightSwitch;
    if (lightSwitch == null) return false;                  // no switch  -> never "off"
    if (lightSwitch.Parent.Tree != Tree) return false;      // stale switch after a split -> "on"
    if (lightSwitch.LightIsActive) return !lightSwitch.IsSwitchedOn();   // commanded on -> lit iff powered
    return true;                                            // commanded off
}
```
[^part]

`PowerConsumer.IsSwitchedOn()` reads `Parent.Tree.PowerConsumers.States[StatesIdx].Active` (bounds
checked, `false` if out of range).[^power-consumer] So a light is lit when:

```text
lit  =  Template.DisableInIva                       (always on, switch ignored)
     or FullPart.LightSwitch == null                (no switch: always on, cannot be turned off)
     or FullPart.LightSwitch.Parent.Tree != Tree    (inherited switch now on another vehicle)
     or (LightSwitch.LightIsActive  and  PowerConsumerState.Active)
```

## `PowerConsumer` fields that implement it

`public class PowerConsumer : ModuleStateful<PowerConsumer, PowerConsumerState, EmptyStruct, EmptyStruct>`[^power-consumer]

| Member | Declaration | Role |
| --- | --- | --- |
| `LightSwitch` | `public bool` | this consumer is a light switch (from XML attribute `LightSwitch`) |
| `LightIsActive` | `public bool` | **the commanded on/off**; initial value from XML attribute `LightIsActive` (default `false`) |
| `Consumed` | `public required Watts` | draw while active (XML `<Consumed W=…/>`) |
| `PowerSwitch`, `Mode`, `StandbyInStorms`, `Hold`, `HasStatusLight` | | probe-core power switch features, unrelated to lights |
| `IsSwitchedOn()` | `public bool` | committed `PowerConsumerState.Active` |
| `IsWanted(states)` | `public bool` | for a light switch returns `LightIsActive` |
| `SetLightIsActive(bool active)` | `public void` | sets `LightIsActive` on this consumer **and its symmetry counterparts** (`VehicleEditor.ForEachModuleWithSymmetry`) |
| `UpdateModules(ref readonly ModuleUpdateContext)` | `public static` | physics step, see below |
| `DrawStateInfo(ref readonly PowerConsumerState)` | `public void` | part window: the "Light Switch" checkbox |

`PowerConsumerState` is `{ bool Active; Watts Consumed; }`.[^power-consumer-state]

Each physics step, `PowerConsumer.UpdateModules` does, for a light switch: `state.Active =
LightIsActive`; if active and the circuit has charge it consumes `Consumed × dt` joules from the
batteries and records the actual draw; **if there is no charge it sets `state.Active = false`**.[^power-consumer]
Consequences:

- Turning a light on is not visible until the next physics step has run and its result has been
  applied on the main thread (normally the next frame). Turning it off is also a one-step delay: the
  predicate reads `LightIsActive` first, so the off is immediate for rendering, but the power draw
  stops at the next step.
- A light with a flat battery is dark even though `LightIsActive` is `true`.
- While the simulation is paused no physics step runs, so `state.Active` does not change: switching a
  light **on** while paused shows nothing until time advances; switching it **off** shows immediately.
  **Inferred** from `PhysicsBubble.StepFrameTo` returning early when the target time has not advanced;
  not observed in the running game.

## Vehicle-level flag

`Vehicle.LightsOn` (`public bool`, default `true`) exists only for `Vehicle.ToggleLights()`, which
flips it and copies the new value into `LightIsActive` of every full part's `LightSwitch` that is on
the same tree.[^vehicle] Nothing else reads `LightsOn`; it can disagree with individual switches.

# Emissive coupling (the glowing lens)

The lens glow is a material emissive texture on the part mesh, not part of `LightModule`. Each frame
`PartTreeRenderData.EnsureBuilt` → `RefreshFullPartDynamicState` → `ComputeOwnerDynamicState`
recomputes per-full-part render flags:[^part-tree-render-data]

- bit `64` is set when `inFullPart.IsLightSwitchedOff()` — the mesh shader treats bit 6 as "No
  emissive";
- bit `128` plus a packed `0xRRGGBB` emissive colour is set only for battery status lights and
  `PowerConsumer.HasStatusLight` — the shader treats bit 7 as "Add emissive color".[^shader-mesh-indirect]

So switching a light off also darkens its lens, automatically and on the same frame the predicate
flips. There is **no coupling between `ColorRgb`/`Intensity` and the emissive lens**: a recoloured
spotlight still has a white lens, and an intensity of 0 leaves the lens glowing. gatOS's analysis
reaches the same conclusion and sketches the Harmony patch that would be needed to tint the
lens.[^gatos-light-analysis]

# Per-frame render path

`LightModule.UpdateRenderData` is called for every `LightModule` of a tree from
`PartTree.UpdateRenderData`, inside the `Profiler.Tag.PartsLights` section — **main thread, once per
visible viewport per frame**:[^part-tree][^light-module]

```text
Program.OnFrame                                         (main thread)
  LightSystem.ClearLights()                             Program.cs:2693   last frame's lights dropped
  LightSystem.OnFrame(...)
  OnPreRender / Render
    UpdateRenderingResources(frameIndex)                Program.cs:2840 -> :4769
      for each visible viewport, for each vehicle in frame (not KittenEva):
        vehicle.UpdateRenderData(viewport, frameIndex)  Vehicle.cs:4929  (skipped if < 1 pixel on screen)
          Parts.UpdateRenderData(...)                   PartTree.cs:1548
            RenderData.EnsureBuilt(...)                 emissive bit 64 refreshed here
            foreach LightModule in tree.Modules.Get<LightModule>():
              module.UpdateRenderData(...)              PartTree.cs:1565-1569
      LightSystem.UpdateRenderResources(...)            Program.cs:4872
```
[^program][^vehicle]

Inside `UpdateRenderData`:[^light-module]

1. Return if `!IsActive`, or if `Template.DisableInIva` and the viewport is in `CameraMode.IVA`.
2. `matrix = Parent.MatrixAsmb2Ego(in matrixVehicleAsmb2Ego)` — the parent (sub-)part's current pose,
   so a light on an animated sub-part follows the animation.
3. Point: `Light.CreatePointLight(Template.Transform.PositionValue.Transform(matrix), Template.Range,
   Template.ColorRgb, Template.Intensity, ELightFlags.CastsShadows | ELightFlags.SoftShadows)`.
4. Spot: direction = `double3.UnitX` rotated by `Template.Transform.RotationValue`, then by the
   upper 3×3 of `matrix`, normalized; `Light.CreateSpotLight(position, direction, Template.Range,
   Template.OuterAngle, Template.InnerAngle, Template.ColorRgb, Template.Intensity, CastsShadows |
   SoftShadows)`.
5. If ray tracing is active (`Program.Instance.IsRaytracingActive`,
   `GameSettings.Current.Graphics.IVARayTracing`, `Template.RayTracing`, viewport has
   `UseRaytracing`) the light goes to the static ray-trace list; otherwise
   `Program.LightSystem.CreateLightInstance(light, viewport)`.

**Every template field is re-read on every call.** A mutation made at any point before step 3/4 of a
frame is visible in that frame; there is no dirty flag, cache or GPU resource owned by the module.

## `Light` — the renderer's struct

`public struct Light` (`KSA.Rendering.Lighting`):[^light]

| Field | Type | Notes |
| --- | --- | --- |
| `Type` | `ELightType : byte { Point = 0, Spot = 1 }`[^light-enums] | |
| `Flags` | `ELightFlags : uint { None = 0, CastsShadows = 1, SoftShadows = 2 }` | requested; the GPU flags are only set if a shadow slot was assigned |
| `NearPlane` | `float` | `Range * 0.01` |
| `ShadowIndex`, `ShadowTileSize` | `uint` | `INVALID_SHADOW_INDEX = uint.MaxValue` until assigned |
| `Position`, `Direction` | `double3` | egocentric (camera-relative) |
| `Range`, `Intensity`, `InnerAngle`, `OuterAngle` | `float` | angles in radians here, converted to cosines for the GPU in `CreateLightData` (the shader's `LightData.innerAngle` / `outerAngle` hold cosines)[^shader-light-data] |
| `Color` | `float3` | |

`CreateSpotLight` normalises the cone before storing it:

```csharp
if (inInnerAngle > inOuterAngle) { swap(inInnerAngle, inOuterAngle); }
inOuterAngle = float.Clamp(inOuterAngle, 1E-05f, 1.5697963f);   // MIN_OUTER_ANGLE, MAX_OUTER_ANGLE
inInnerAngle = float.Clamp(inInnerAngle, 0f, inOuterAngle);
```

The swap is the trap: lowering only `OuterAngle` below an untouched `InnerAngle` makes the renderer
exchange them, so the visible cone does not narrow. Always keep `InnerAngle <= OuterAngle` in the
template.

`ClusteredLightSystem.CreateLightInstance(Light, IViewport)` drops a light whose
`Range.IsNearlyZero()` or whose viewport has `ViewportLightMode.None`; adds it to that viewport's
forward list (max 256); and, for the main viewport when not in `Forward` mode, to the clustered list
(`MaxLights => 1024`). `MaxShadowCasters` is `min(GameSettings.GetLightSystemShadowSlots(), 64)`, or 0
with shadows disabled.[^clustered-light-system] Lights beyond those limits are silently dropped.

## Photometry: what intensity, range and the angles mean

Both shading paths (clustered pre-pass and forward) compute, per light and shaded point at distance
`d`:[^shader-light-prepass][^shader-punctual]

```text
x2        = (d / range)^2
falloff   = clamp(1 - x2*x2, 0, 1)                      // window: 1 at the light, 0 at d = range
rangeAtt  = (1 / d^2) * falloff                         // inverse-square inside the window
spot      = clamp((cosAng - cos(outer)) / max(cos(inner) - cos(outer), 1e-4), 0, 1)
spotAtt   = spot * spot                                 // spots only
radiance ~= color * intensity * rangeAtt * spotAtt * N·L
```

- **Intensity** is a unitless scalar on an inverse-square law in metres: `color × intensity` is the
  irradiance factor at 1 m on-axis. Stock values run from `0.05` (cabin fill light) through `2`
  (window lights) and `10` (spot/flood parts) to `50` (the hidden `LightPart` spot). There is no upper
  clamp in `LightModule`, `Light` or the shaders. Zero or negative intensity produces no light but the
  light is still submitted (`CreateLightInstance` only tests `Range`).
- **Range** (metres) is a hard cut-off, not a brightness control: it only shapes the last part of the
  falloff and bounds the clustered light volume and shadow near plane.
- **Angles** are half-angles from the beam axis in radians. Valid stored range: `OuterAngle` in
  `[1e-5, 1.5697963]` (just under π/2, i.e. ≤ 89.94°), `InnerAngle` in `[0, OuterAngle]`. Between
  inner and outer the falloff is the squared linear ramp in *cosine* space. `LightUtils.
  RadiusFromConeAngle(angle, distance) = distance * tan(angle)` gives the spot radius at a
  distance.[^light-utils]
- **Narrow-cone floor.** Because the denominator is clamped to `1e-4`, a cone whose
  `cos(inner) − cos(outer)` is below that dims instead of tightening. gatOS's analysis puts the
  threshold near 0.81° half-angle.[^gatos-light-analysis] **Unverified** here beyond reading the
  shader line.
- **Colour** is used as given; the float path has no clamp. **Inferred:** components above 1 act as
  extra intensity; both existing mods restrict themselves to 0–1.
- **No volumetric beam**: lights only illuminate surfaces.

## Shadows and ray tracing

Every part light requests `CastsShadows | SoftShadows`; whether it actually gets a shadow slot is
decided inside `ClusteredLightSystem` (slot count from graphics settings, max 64) and is not
controllable per light from `LightModule`. With `RayTracing = true` and ray tracing active, **point**
lights are consumed by `RaytracingRenderer` (at most 16 lights in total there).[^raytracing-renderer]

# XML schema and real examples

`<Light>` appears inside `<Components>` of a `<Part>` or `<SubPart>`. Complete real example — the
spotlight sub-part used by the two small spotlight parts:[^core-electrical-gamedata]

```xml
<SubPart Id="CoreElectricalA_Subpart_SpotlightA" Modify="Patch">
  <Components Modify="Extend">
    <Light>
      <Type>Spot</Type>
      <Transform>
        <Position X="0.38" Y="0.21" Z="0" />
      </Transform>
      <Range Value="5" />
      <Intensity Value="10" />
      <Color R="1" G="1" B="1" />
      <InnerAngle Value="0.392599" />
      <OuterAngle Value="0.785398" />
    </Light>
  </Components>
</SubPart>
```

The owning full part supplies the switch (and, for these parts, an aiming animation — see
[KeyframeAnimationModule](/game-targets/keyframe-animation-module.md)):[^core-electrical-gamedata]

```xml
<KeyframeAnimation Id="CoreElectricalA_Prefab_LightSmallA_Anim" Path="Animations/CoreElectricalA_Prefab_LightSmallA_Anim.glb" />
<Part Id="CoreElectricalA_Prefab_LightSmallA" Modify="Patch">
  …
  <EditorTags Modify="Extend">
    <EditorTag Ref="Lights" />
  </EditorTags>
  <Components Modify="Extend">
    <KeyframeAnimationModule Key="CoreElectricalA_Prefab_LightSmallA_Anim">
      <KeyframeAnimation Ref="CoreElectricalA_Prefab_LightSmallA_Anim" />
    </KeyframeAnimationModule>
    <Collider Key="Collider1"> … </Collider>
    <PowerConsumer LightSwitch="true">
      <Consumed W="60" />
    </PowerConsumer>
  </Components>
</Part>
```

A point light with ray tracing and two always-on fill lights, from the capsule interior:[^core-iva-space-a]

```xml
<Light>
  <Type>Point</Type>
  <Transform><Position X="-0.275" Z="-0.80" /></Transform>
  <Range Value="1.5" />
  <Intensity Value="0.05" />
  <Color R="1.0" G="0.9" B="0.7" />
  <RayTracing>true</RayTracing>
</Light>
<Light Key="Right">
  <Type>Point</Type>
  <Transform><Position X="-0.10" Y="0.42" Z="-0.60" /></Transform>
  <Range Value="1.0" />
  <Intensity Value="0.5" />
  <Color R="1.0" G="0.95" B="0.85" />
  <RayTracing>false</RayTracing>
  <DisableInIva>true</DisableInIva>
</Light>
```

The XML comment beside those fill lights states the intended semantics: "DisableInIva also makes the
light always-on/EC-free, ignoring the part's inherited LightSwitch/PowerConsumer" and "Intensity is
inverse-square against Range, the same convention LightPrePass.comp uses".[^core-iva-space-a]

# Stock parts that carry lights

Every `<Light>` in `Content/Core` for this build:

| Part (template id) | Display name | Where the `<Light>` is | Lights (key: type, range m, intensity, colour, inner/outer rad) | Switch |
| --- | --- | --- | --- | --- |
| `CoreElectricalA_Prefab_LightSmallA` | (none) | sub-part `CoreElectricalA_Subpart_SpotlightA` ×1 | `Light`: Spot, 5, 10, white, 0.392599 / 0.785398 | `PowerConsumer LightSwitch="true"`, 60 W, starts **off** |
| `CoreElectricalA_Prefab_LightSmallB` | (none) | sub-part `CoreElectricalA_Subpart_SpotlightA` ×2 | same spotlight twice | `LightSwitch`, 120 W, starts off |
| `CoreElectricalA_Prefab_LightSmallC` | (none) | sub-part `CoreElectricalA_Subpart_FloodlightA` ×1 | `Light`: Spot, 3, 10, white, 0.23 / 1.57 | `LightSwitch`, 100 W, starts off |
| `LightPart` (editor tag `Hidden`) | (none) | sub-part `LightSubPart` | `PointLight`: Point, 2, 2, white; `SpotLight`: Spot, 5, 50, white, 0.392599 / 0.785398, rotated Z 1.5708 | `LightSwitch`, 40 W, starts off |
| `CoreCommandA_Prefab_MediumCapsuleVariantA` | Mk1 Command Pod | the part itself | `RightWindow`, `LeftWindow`: Spot, 2.5, 2.0, white, default inner (π/8) / 1.57 | `LightSwitch="true" LightIsActive="true"`, 10 W, starts **on** |
| `CoreCommandB_Prefab_SmallLanderA` | Mk1 Lander | the part itself | `WindowA`, `WindowB`, `WindowC`: Point, 1.5, 2.0, white | `LightSwitch`, `LightIsActive="true"`, 10 W, starts on |
| `CoreIVASpaceA_Prefab_MediumCapsuleA` (IVA interior, `Hidden`) | (none) | the part itself | `Light`: Point, 1.5, 0.05, (1, 0.9, 0.7), ray traced; `Right`, `Left`: Point, 1.0, 0.5, (1, 0.95, 0.85), `DisableInIva` | inherits the pod's switch through `AttachedInternal`; the two fill lights ignore it |

[^core-electrical-gamedata][^core-electrical-assets][^part-assets][^core-command-a][^core-command-b][^core-iva-space-a]

Things this table implies for an enumeration API:

- One full part can carry **several** `LightModule`s (LightSmallB: 2, LightPart: 2, the pod: 2 plus
  3 more on its attached interior) but has **one** switch. On/off is per full part; colour, intensity
  and angles are per `LightModule`.
- Light modules sit on sub-parts for the dedicated light parts and on the full part for cabins.
  `module.Parent` may be either; always use `module.Parent.FullPart` for the switch.
- The interior's `DisableInIva` lights report `IsActive == true` regardless of any switch.
- All three dedicated light parts also carry a `KeyframeAnimationModule` (the aim/tilt actuator).

# Persistence

| What | Saved? | How |
| --- | --- | --- |
| Template values (colour, intensity, range, angles) | **No** | they come from content XML at load; `LightModule` has no save data. A runtime change is lost on reload and, if made on the shared template, also leaks to every other light of that type until the game restarts |
| A replaced `LightModule.Template` reference | **No** | new module objects are constructed on load with the shared template |
| Light switch | Yes, with a quirk | `PowerConsumer.GetSaveData` writes `<PowerConsumerData InstanceOf=… LocalInstanceId=… Active=…/>` where `Active` is the **simulated `PowerConsumerState.Active`**, not `LightIsActive`; `ApplySaveData` restores both the state and, for a light switch, `LightIsActive = saveData.Active` |
| `Vehicle.LightsOn` | Yes | `VehicleData.LightsOn` (`<LightsOn>` element, default `true`) |

[^power-consumer][^vehicle][^vehicle-data]

The quirk means a light that was commanded on but unpowered (flat battery) at save time loads as
commanded **off**. Real example from a stock vehicle file:
`<PowerConsumerData InstanceOf="PowerConsumer" LocalInstanceId="5" Active="false" />`.[^gemini-vehicle]

# Reading light state cheaply

All of these are field reads or one bounds-checked array read; none allocate.

| Value | Expression | Type → suggested cell | Notes |
| --- | --- | --- | --- |
| has a switch | `m.Parent.FullPart.LightSwitch != null` | bool → u32 | |
| commanded on | `m.Parent.FullPart.LightSwitch?.LightIsActive` | bool → u32 | what a program set |
| actually lit | `m.IsActive` | bool → u32 | commanded **and** powered; `true` for switchless and `DisableInIva` lights |
| powered | `m.Parent.FullPart.LightSwitch?.IsSwitchedOn()` | bool → u32 | committed state, one step old |
| type | `m.Template.Type` | enum → u32 (`Spot = 0`, `Point = 1`) | |
| intensity | `m.Template.Intensity.Value` | f32 | ≥ 0, unbounded |
| range | `m.Template.Range.Value` | f32 metres | |
| colour | `m.Template.ColorRgb.R/.G/.B` | 3 × f32 | reads the authored components; the rendered value is `ColorRgb.Value.AsFloat3` |
| inner / outer half-angle | `m.Template.InnerAngle.Value`, `.OuterAngle.Value` | f32 radians | meaningful for spots only |
| draw | `LightSwitch.Consumed` | `Watts` → f32 | nominal; actual draw is `PowerConsumerState.Consumed` |

# Modding notes

- **Nothing here is private.** `LightModule.Template`, every `TemplateData` field,
  `FloatReference.Value`, `ColorRgbReference.R/G/B/IndexedColor/OnDataLoad`, `Part.LightSwitch`,
  `Part.IsLightSwitchedOff()`, `PowerConsumer.LightSwitch/LightIsActive/SetLightIsActive/IsSwitchedOn`
  and `Vehicle.LightsOn/ToggleLights` are public. No reflection, publicizer or Harmony patch is needed
  to enumerate, read or change lights. The only non-public item on the path is
  `ColorRgbReference.Value`'s `protected set`, which `OnDataLoad` covers. (zippo's older code path
  uses string reflection anyway; see
  [zippo light control reference](/game-targets/zippo-light-control-reference.md).)
- **The template is shared.** Writing `module.Template.Intensity.Value` on an untouched module changes
  every light created from that `<Light>` element, on every vehicle, for the rest of the session. For
  per-light control first give the module its own `TemplateData` (and its own `FloatReference` /
  `ColorRgbReference` objects) and assign it to the public `Template` field. Both gatOS and zippo's
  Disco mode do exactly this. `TemplateData` has a public parameterless constructor; copy all ten
  fields including `Key`.
- **Thread: where to write.** The only reader of the template is `UpdateRenderData` on the main
  thread, late in the frame, while the next physics step may already be running on workers. Writing a
  single `float` from the physics step is the same kind of unsynchronised single-word write the game
  itself performs on `LightIsActive`; writing a colour is three floats plus a 28-byte struct and can be
  observed half-written for one frame; replacing `Template` or `ColorRgb` with a fully built new
  object is a single reference write. The simple, race-free option is to queue appearance changes and
  apply them on the main thread in the solver-idle window — they are purely visual, the renderer will
  not look before then anyway, and that is where kFS already runs its deferred work (see
  [stock control paths](/game-targets/stock-control-paths.md)).
- **`LightIsActive` can be written from the physics step.** It is a `bool` field that the step itself
  reads a moment later in `PowerConsumer.UpdateModules`; writing it from the flight-computer phase of
  the same step makes power draw start in that very step.
- **Per-emitter off without the switch.** Setting a private template's `Range` to 0 makes
  `CreateLightInstance` skip that one emitter (not the ray-traced path), leaving the part's switch,
  power draw and lens glow untouched. Constructed from the decompiled code; untested.
- **Keep `InnerAngle <= OuterAngle`** in the template and clamp `OuterAngle` to
  `[1e-5, 1.5697963]`; otherwise the renderer swaps or clamps and read-back no longer matches what is
  drawn.
- **Do not set `RayTracing = true` on a spot light**: it would be appended every frame to
  `SpotLightsRayTrace`, which nothing reads or clears in this build.
- **Limits.** 1024 clustered lights, 256 forward lights per viewport, at most 64 shadow casters; a
  program that turns on hundreds of lights will silently lose some.
- **Lifetime of overrides.** A replaced template lives exactly as long as the module object: it
  survives staging and docking (same objects) and is gone after save/load or a system change.

[^light-module]: LightModule.cs — `TemplateData` :11–52 (fields :20–45, `OnDataLoad` :47–51); static ray-trace lists :54–56; `Template` :58; `IsActive` :60–70; constructor :72–76; `CreateComponents` :78–91; `UpdateRenderData` :93–132 (early return :95, point :102–114, spot :115–130).
[^power-consumer]: PowerConsumer.cs — `Template` :13–35; `SaveData` :37–70; fields :72–88; `IsSwitchedOn` :138–146; `IsWanted` :187–202; `CreateComponents` :204–223; `UpdateModules` :235–272; `SetLightIsActive` :274–280; `DrawStateInfo` light checkbox :302–309; `GetSaveData` :387–403; `ApplySaveData` :405–421.
[^power-consumer-state]: PowerConsumerState.cs:3–8.
[^part]: Part.cs — `LightSwitch` field :734; `IsLightSwitchedOff` :1551–1567; `ResolveLightSwitch` :2082–2108; `ResetModuleProperties` :2110–2136; `IsAttachedInternal` :1204–1223.
[^part-tree]: PartTree.cs — `UpdateRenderData` :1548–1582 (lights :1564–1570).
[^part-tree-render-data]: PartTreeRenderData.cs — `EnsureBuilt` :673–686; `RefreshFullPartDynamicState` :880–890; `ComputeOwnerDynamicState` :1118–1158 (bit 64 :1123–1126, bit 128 :1128–1151).
[^vehicle]: Vehicle.cs — `LightsOn` :297; load :1316; save :1414; `ToggleLights` :1677–1690; `UpdateRenderData` :4929–4940.
[^vehicle-data]: VehicleData.cs:50–51.
[^program]: Program.cs — `LightSystem` static :152; `ClearLights`/`OnFrame` :2693–2694; `UpdateRenderingResources` call :2840, body :4769–4872 (vehicles :4808–4820, `LightSystem.UpdateRenderResources` :4872).
[^light]: KSA.Rendering.Lighting/Light.cs — fields :8–36; `CreatePointLight` :38–52; `CreateSpotLight` :54–79; `CreateLightData` :81–106.
[^light-enums]: ELightFlags.cs:5–11; ELightType.cs:3–7.
[^clustered-light-system]: ClusteredLightSystem.cs — `MaxLights` :289; `MaxShadowCasters` :291, :355; `CreateLightInstance` :911–935; `ClearLights` :937–948.
[^light-utils]: LightUtils.cs:104–107.
[^raytracing-renderer]: RaytracingRenderer.cs:1175–1181, :1239.
[^float-reference]: FloatReference.cs:5–24.
[^color-rgb-reference]: ColorRgbReference.cs — fields :9–22; constructors :24–33; `OnDataLoad` :35–44; implicit conversions :148–156.
[^color-preset]: Brutal.Numerics/Color.cs:7–36.
[^transform-reference]: TransformReference.cs:17–45.
[^attached-internal]: AttachedInternal.cs — `ResyncTransformsAllParts` :66–88.
[^shader-light-prepass]: Core/Shaders/Lighting/LightPrePass.comp:317–335.
[^shader-punctual]: Core/Shaders/Lighting/PunctualLighting.glsl:49–79.
[^shader-light-data]: Core/Shaders/Lighting/LightData.glsl:19–30.
[^shader-mesh-indirect]: Core/Shaders/Mesh/MeshIndirect.frag:320–328.
[^core-electrical-gamedata]: Core/CoreElectricalAGameData.xml — LightSmallA :61–99; LightSmallB :101–139; LightSmallC :141–179; `CoreElectricalA_Subpart_SpotlightA` :181–195; `CoreElectricalA_Subpart_FloodlightA` :197–211; `LightPart` switch :359–365.
[^core-electrical-assets]: Core/CoreElectricalAAssets.xml — sub-part placements of LightSmallA/B/C :663–760.
[^part-assets]: Core/PartAssets.xml:32–79.
[^core-command-a]: Core/CoreCommandAGameData.xml — `AttachedInternal` :18–20; lights :31–51; switch :82–84.
[^core-command-b]: Core/CoreCommandBGameData.xml — lights :57–83; switch :87–89.
[^core-iva-space-a]: Core/CoreIVASpaceAGameData.xml — lights :10–68; comment :33–43.
[^gemini-vehicle]: Core/defaultvehicles/Gemini7/vehicle.xml:2440.
[^gatos-light-analysis]: gatos:docs/LIGHT_PART_ANALYSIS.md — §0 TL;DR :11–55; §10 emissive lens :472–594.
