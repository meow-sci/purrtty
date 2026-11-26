---
type: How-To
title: Control recipes for lights and part animations
description: Concrete C# for enumerating a vehicle's lights and animation modules, reading their state, switching lights, changing colour, cone angles and brightness, toggling deployment and setting an actuation value, each labelled with its provenance, required thread and stock side effects.
resource: ksa-decomp:KSA/LightModule.cs
tags: [kfs, game-targets, how-to, lights, animation, recipes, threading]
sources:
  - id: light-module
    resource: ksa-decomp:KSA/LightModule.cs
    title: LightModule (decompiled)
  - id: light
    resource: ksa-decomp:KSA.Rendering.Lighting/Light.cs
    title: Light struct (decompiled)
  - id: clustered-light-system
    resource: ksa-decomp:KSA.Rendering.Lighting/ClusteredLightSystem.cs
    title: ClusteredLightSystem (decompiled)
  - id: color-rgb-reference
    resource: ksa-decomp:KSA/ColorRgbReference.cs
    title: ColorRgbReference (decompiled)
  - id: float-reference
    resource: ksa-decomp:KSA/FloatReference.cs
    title: FloatReference (decompiled)
  - id: module-base
    resource: ksa-decomp:KSA/ModuleBase.cs
    title: ModuleBase and KeyedTemplate construction (decompiled)
  - id: content-resolve-scope
    resource: ksa-decomp:KSA/ContentResolveScope.cs
    title: ContentResolveScope (decompiled)
  - id: power-consumer
    resource: ksa-decomp:KSA/PowerConsumer.cs
    title: PowerConsumer (decompiled)
  - id: part
    resource: ksa-decomp:KSA/Part.cs
    title: Part (decompiled)
  - id: vehicle
    resource: ksa-decomp:KSA/Vehicle.cs
    title: Vehicle (decompiled)
  - id: keyframe-animation-module
    resource: ksa-decomp:KSA/KeyframeAnimationModule.cs
    title: KeyframeAnimationModule (decompiled)
  - id: module-list
    resource: ksa-decomp:KSA/ModuleList.cs
    title: ModuleList (decompiled)
  - id: module-stateful
    resource: ksa-decomp:KSA/ModuleStateful.cs
    title: ModuleStateful (decompiled)
  - id: module-state-updaters
    resource: ksa-decomp:KSA/ModuleStateUpdaters.cs
    title: ModuleStateUpdaters (decompiled)
  - id: vehicle-update-data
    resource: ksa-decomp:KSA/VehicleUpdateData.cs
    title: VehicleUpdateData (decompiled)
  - id: solar-panel
    resource: ksa-decomp:KSA/SolarPanel.cs
    title: SolarPanel (decompiled)
  - id: parachute
    resource: ksa-decomp:KSA/Parachute.cs
    title: Parachute (decompiled)
  - id: kfs-controller-set
    resource: ksa-decomp:KSA.Kfs/KfsControllerSet.cs
    title: KfsControllerSet (decompiled)
  - id: kfs-vehicle-host
    resource: ksa-decomp:KSA.Kfs/KfsVehicleHost.cs
    title: KfsVehicleHost (decompiled)
  - id: kfs-services
    resource: ksa-decomp:KSA.Kfs/KfsServices.cs
    title: KfsServices (decompiled)
  - id: zippo-disco-light
    resource: unscience:zippo.lib/DiscoLight.cs
    title: zippo DiscoLight
  - id: zippo-submod
    resource: unscience:zippo.lib/ZippoSubmod.cs
    title: zippo ZippoSubmod
  - id: zippo-submod-disco
    resource: unscience:zippo.lib/ZippoSubmod.Disco.cs
    title: zippo ZippoSubmod Disco coordination
  - id: gatos-light-actuator
    resource: gatos:gatOS.GameMod/Game/Ksa/Actuators/LightActuator.cs
    title: gatOS LightActuator
  - id: gatos-animation-actuator
    resource: gatos:gatOS.GameMod/Game/Ksa/Actuators/AnimationActuator.cs
    title: gatOS AnimationActuator
  - id: gatos-vessel-reader
    resource: gatos:gatOS.GameMod/Game/Ksa/Readers/VesselReader.cs
    title: gatOS VesselReader
  - id: gatos-animation-links
    resource: gatos:gatOS.GameMod/Game/Ksa/Readers/AnimationLinks.cs
    title: gatOS AnimationLinks
generated: { by: claude-code/claude-fable-5-2, at: 2026-11-26T06:11:10Z }
status: draft
ksa_build: "2026.11.25.5949"
---

# How to read this document

Each recipe states:

- **Provenance** — one of *from zippo*, *from gatOS*, *from the stock UI path*, *from stock kFS*, or
  *constructed from the decompiled API — untested*. "From" means the same game members are used the
  same way by that code; the snippet itself is still written for this bundle and **none of these
  snippets has been compiled or run**, hence `status: draft`. Every game member, type and signature
  the snippets use was re-read against the decompiled sources of this build in review.
- **Thread** — using the phases of [stock control paths](/game-targets/stock-control-paths.md):
  - **STEP** — inside the physics step, on the worker running the vehicle's flight computer (where
    `KfsDispatch.TryComputeControl` runs);
  - **IDLE** — main thread, solver-idle window (where `KfsIntegration.BeforeInputEvents` runs);
  - **UI** — main thread, UI phase, physics step possibly running (where the stock part window,
    zippo and gatOS's frame lane run).
- **Side effects** — everything the stock game does as a consequence.

Common preamble for the snippets:

```csharp
using System;
using System.Linq;
using Brutal.Numerics;
using KSA;
using Anim       = KSA.KeyframeAnimationModule;
using AnimStates = KSA.ModuleStateful<KSA.KeyframeAnimationModule, KSA.KeyframeAnimationModule.State, KSA.EmptyStruct, KSA.EmptyStruct>;
```

Background: [part and module model](/game-targets/part-and-module-model.md),
[LightModule](/game-targets/light-module.md),
[KeyframeAnimationModule](/game-targets/keyframe-animation-module.md).

# Part 1 — lights

## L-1 Enumerate the lights of a vehicle

```csharp
/// Frozen, InstanceId-ordered light list; index in this array is the program's "light #n".
static LightModule[] SnapshotLights(Vehicle vehicle) =>
    vehicle.Parts.Modules.Get<LightModule>().ToArray()
        .OrderBy(m => m.InstanceId)
        .ToArray();
```

- **Provenance:** *from stock kFS* — the same expression `KfsControllerSet` uses for
  `ThrusterController`, `EngineController` and `GimbalController`.[^kfs-controller-set] gatOS uses the
  unsorted span index instead; zippo walks parts and tests their templates.
- **Thread:** any; it allocates (two arrays and a LINQ sort), so do it when the list is (re)built,
  not per tick. Stock kFS builds its sets in the host constructor and on a detected layout change
  inside dispatch.[^kfs-vehicle-host]
- **Side effects:** none.
- `Get<LightModule>()` returns a live `Span<LightModule>` over the tree's list; `ToArray()` is what
  makes it safe to keep.[^module-list]
- Includes lights on IVA interiors and `DisableInIva` fill lights. To list only switchable lights,
  filter with L-2's `HasSwitch`.

Useful per-element descriptors to publish once, when the list is built:

```csharp
static (string partName, string templateId, string lightKey, uint partInstanceId, bool isSpot, bool hasSwitch)
    Describe(LightModule m)
{
    Part full = m.Parent.FullPart;
    return (full.DisplayName, full.Template.Id, m.TemplateId, full.InstanceId,
            m.Template.Type == LightModule.TemplateData.LightType.Spot,
            full.LightSwitch != null);
}
```

- **Provenance:** *constructed from the decompiled API — untested*.

The switch is per **full part**, so several entries of the list can share one. Group them when the
program-facing model wants "light parts":

```csharp
static PowerConsumer?[] SwitchOf(LightModule[] lights) =>
    lights.Select(m => m.Parent.FullPart.LightSwitch).ToArray();   // equal references = same switch
```

## L-2 Read a light's state

```csharp
readonly record struct LightReadout(
    bool HasSwitch, bool CommandedOn, bool Lit, bool IsSpot,
    float Intensity, float Range, float R, float G, float B, float InnerRad, float OuterRad);

static LightReadout Read(LightModule m)
{
    PowerConsumer? sw = m.Parent.FullPart.LightSwitch;
    LightModule.TemplateData t = m.Template;
    return new LightReadout(
        HasSwitch:   sw != null,
        CommandedOn: sw?.LightIsActive ?? true,       // policy choice for switchless lights; gatOS reports false
        Lit:         m.IsActive,                      // commanded AND powered (always true for switchless / DisableInIva)
        IsSpot:      t.Type == LightModule.TemplateData.LightType.Spot,
        Intensity:   t.Intensity.Value,
        Range:       t.Range.Value,
        R: t.ColorRgb.R, G: t.ColorRgb.G, B: t.ColorRgb.B,
        InnerRad:    t.InnerAngle.Value,
        OuterRad:    t.OuterAngle.Value);
}
```

- **Provenance:** *from gatOS* (`VesselReader.SampleLights`) for the template fields and the
  commanded switch;[^gatos-vessel-reader] `m.IsActive` is the stock predicate the renderer uses.[^light-module]
- **Thread:** STEP, IDLE or UI. Every item is a field read except `IsActive`, which also reads one
  element of the committed `PowerConsumerState` buffer.[^part][^power-consumer]
- **Side effects:** none. No allocation.
- `Lit` is one physics step behind `CommandedOn` after a switch-on, and stays `false` on a dead
  battery.

## L-3 Switch one light part on or off

```csharp
/// Returns false when the light has no switch (cannot be turned off by the stock mechanism).
static bool SetLightOn(LightModule m, bool on)
{
    PowerConsumer? sw = m.Parent.FullPart.LightSwitch;
    if (sw == null) return false;
    sw.LightIsActive = on;
    return true;
}
```

- **Provenance:** *from zippo* and *from gatOS* — both write this exact field
  (`ZippoSubmod.cs:157–169`, `LightActuator.cs:45–53`); `Vehicle.ToggleLights` writes the same
  field.[^zippo-submod][^gatos-light-actuator][^vehicle]
- **Thread:** **STEP is acceptable** — one `bool` that `PowerConsumer.UpdateModules` reads later in
  the same step; also IDLE or UI (the stock checkbox writes it in UI).
- **Side effects:** from the next `PowerConsumer.UpdateModules`: `PowerConsumerState.Active` follows
  and the part draws `Consumed` watts from its circuit while on; the part's mesh emissive (lens glow)
  follows via render flag 64; **every `LightModule` on that full part (and on an attached IVA interior
  inheriting the switch) switches together**. No sound, alert or history entry. The part window's
  checkbox shows the new value on its next draw. `Vehicle.LightsOn` is *not* updated.
- Always read `LightSwitch` live; `Part.ResolveLightSwitch` can reassign the field.[^part]

Variant with the stock symmetry fan-out:

```csharp
sw.SetLightIsActive(on);     // this consumer + same-index PowerConsumer on each symmetry counterpart
```

- **Provenance:** *from the stock UI path* (`PowerConsumer.DrawStateInfo` → `SetLightIsActive`).[^power-consumer]
- **Thread:** IDLE or UI. It allocates a delegate and walks `SymmetryLink` lists; keep it off STEP.

Variant for all lights of the vehicle, as a *set* rather than a toggle:

```csharp
static void SetAllLights(Vehicle vehicle, bool on)
{
    vehicle.LightsOn = on;                                      // keeps the U hotkey coherent
    foreach (PowerConsumer c in vehicle.Parts.Modules.Get<PowerConsumer>())
        if (c.LightSwitch) c.LightIsActive = on;
}
```

- **Provenance:** *from gatOS* (`LightActuator.SetMaster`, "Replicates ToggleLights() as a set").[^gatos-light-actuator]
  The stock method is `vehicle.ToggleLights()`, which flips `LightsOn` and iterates full parts'
  `LightSwitch` instead.[^vehicle]
- **Thread:** IDLE or UI.

## L-4 Give a light its own template (prerequisite for L-5 … L-7)

The template object is shared by every light of the same part type; isolate before the first write.

```csharp
/// Call once per module before any appearance write. Returns the original so it can be restored.
static LightModule.TemplateData MakePrivate(LightModule m)
{
    LightModule.TemplateData o = m.Template;
    var p = new LightModule.TemplateData
    {
        Key          = o.Key,
        Type         = o.Type,
        Transform    = o.Transform,                              // never written: may stay shared
        Range        = new FloatReference(o.Range.Value),
        Intensity    = new FloatReference(o.Intensity.Value),
        InnerAngle   = new FloatReference(o.InnerAngle.Value),
        OuterAngle   = new FloatReference(o.OuterAngle.Value),
        ColorRgb     = new ColorRgbReference(new float3(o.ColorRgb.R, o.ColorRgb.G, o.ColorRgb.B)),
        RayTracing   = o.RayTracing,
        DisableInIva = o.DisableInIva,
    };
    p.ColorRgb.OnDataLoad(null!);        // the float3 constructor does not set the rendered Value
    m.Template = p;                      // single reference write; the object is complete before this
    return o;
}

static void RestoreShared(LightModule m, LightModule.TemplateData original, LightModule.TemplateData mine)
{
    if (ReferenceEquals(m.Template, mine)) m.Template = original;   // only if still ours
}
```

- **Provenance:** *from zippo* (`DiscoLight` constructor and `Dispose`), extended to give **every**
  written reference field a private object (zippo privatises only the channels it animates).[^zippo-disco-light]
  gatOS does the same with a reflective field-by-field clone, which keeps working if the game adds a
  field to `TemplateData`.[^gatos-light-actuator]
- **Thread:** IDLE or UI recommended (it allocates twelve objects: the six it keeps, plus the six
  defaults that `TemplateData`'s field initialisers create before the object initialiser replaces
  them). The construction itself has no
  hidden side effects on any thread: the `KeyedTemplate` constructor calls
  `ContentResolveScope.Track(this)`, which is a no-op when the thread-static `Current` scope is null
  (always, outside content loading), and `TemplateDataBase` only looks up its default key in a
  concurrent dictionary.[^module-base][^content-resolve-scope]
- **Side effects:** none visible — the copy renders identically.
- Track ownership per module (a `ConditionalWeakTable<LightModule, …>` as gatOS does, or a side array
  parallel to the frozen light list) so the clone happens once.
- The override lasts as long as the module object: through staging and docking, not through
  save/load.
- If the game adds a field to `LightModule.TemplateData`, this hand-written copy must be updated.

## L-5 Set colour

```csharp
static void SetColor(LightModule.TemplateData mine, float r, float g, float b)
{
    ColorRgbReference c = mine.ColorRgb;
    c.R = r; c.G = g; c.B = b;
    c.IndexedColor = IndexedColor.Invalid;   // otherwise OnDataLoad re-derives R/G/B from the named colour
    c.OnDataLoad(null!);                     // recomputes the Value the renderer actually reads
}
```

- **Provenance:** *from zippo* (`DiscoLight.Update`, `LightController.WriteColor`) and *from gatOS*
  (`LightActuator.SetColor`, which omits the `IndexedColor` reset).[^zippo-disco-light][^gatos-light-actuator][^color-rgb-reference]
- **Thread:** **IDLE** (or UI). The renderer reads `Value`, a 28-byte struct written non-atomically
  by `OnDataLoad`; from STEP a frame could render a half-updated colour.

Atomic variant, safe to publish from any thread at the cost of one allocation:

```csharp
static void SetColorAtomic(LightModule.TemplateData mine, float r, float g, float b)
{
    var c = new ColorRgbReference(new float3(r, g, b));
    c.OnDataLoad(null!);
    mine.ColorRgb = c;                       // single reference write
}
```

- **Provenance:** *constructed from the decompiled API — untested*.
- **Side effects (both):** none beyond the light's colour. The lens emissive does **not** change
  colour. Units: RGB floats, nominal 0–1 per component (`Unverified:` whether the renderer treats
  them as linear or sRGB values — see [light module](/game-targets/light-module.md)); sanitise NaN/∞ and decide whether to clamp
  (gatOS clamps to 0–1 and zippo's Disco recipe validates 0–1; zippo's ordinary `WriteColor` path
  passes the value through unclamped).

## L-6 Set inner / outer cone angle

```csharp
const float MinOuterRad = 1e-5f, MaxOuterRad = 1.5697963f;       // Light.CreateSpotLight's clamp band

/// Half-angles in radians. No effect on point lights.
static void SetCone(LightModule.TemplateData mine, float innerRad, float outerRad)
{
    if (mine.Type != LightModule.TemplateData.LightType.Spot) return;
    float outer = Math.Clamp(outerRad, MinOuterRad, MaxOuterRad);
    float inner = Math.Clamp(innerRad, 0f, outer);               // keep inner <= outer: the renderer SWAPS them otherwise
    mine.InnerAngle.Value = inner;
    mine.OuterAngle.Value = outer;
}

static void SetOuter(LightModule.TemplateData mine, float outerRad)
{
    float outer = Math.Clamp(outerRad, MinOuterRad, MaxOuterRad);
    mine.OuterAngle.Value = outer;
    if (mine.InnerAngle.Value > outer) mine.InnerAngle.Value = outer;
}

static void SetInner(LightModule.TemplateData mine, float innerRad) =>
    mine.InnerAngle.Value = Math.Clamp(innerRad, 0f, mine.OuterAngle.Value);
```

- **Provenance:** *from gatOS* (`SetOuterAngle` / `SetInnerAngle`, including the pull-down of the
  inner angle) and *from zippo* (`DiscoLight.Update` writes both `FloatReference.Value`s).[^gatos-light-actuator][^zippo-disco-light]
  The clamp constants are `Light.MIN_OUTER_ANGLE` / `MAX_OUTER_ANGLE`, which are `private const`
  — hence the redeclared values above.[^light]
- **Thread:** IDLE or UI recommended. From STEP each write is a single `float`; the worst case is one
  frame rendered with a mismatched pair, which `CreateSpotLight` normalises (swap/clamp) rather than
  faulting.
- **Side effects:** none. `Math.Clamp` propagates NaN — reject non-finite inputs first. Below roughly
  1° half-angle the beam dims instead of narrowing (shader denominator floor).

## L-7 Set brightness

```csharp
static void SetIntensity(LightModule.TemplateData mine, float intensity) =>
    mine.Intensity.Value = float.IsFinite(intensity) ? MathF.Max(0f, intensity) : 0f;
```

- **Provenance:** *from gatOS* (`SetBrightness`: `(float)Math.Max(0, value)`) and *from zippo*
  (`LightController.WriteIntensity`, on the shared template).[^gatos-light-actuator] `Value` is the
  public field of `FloatReference`.[^float-reference]
- **Thread:** IDLE or UI recommended; from STEP it is a single `float` write.
- **Side effects:** none — brightness does not change the power draw, and the lens glow is
  unaffected. Unitless, inverse-square in metres; stock values 0.05–50. Present the stock value as
  the program's reference (`original.Intensity.Value`) rather than assuming a 0–1 scale.

Range, if exposed, is the same pattern on `mine.Range.Value` (metres). A range that
`IsNearlyZero()` makes `ClusteredLightSystem.CreateLightInstance` skip the light, which is a way to
blank **one emitter** without touching the part's switch (not on the ray-traced path, which adds
the light to a static list without calling `CreateLightInstance`).[^clustered-light-system][^light-module]
**Provenance:** *constructed from the decompiled API — untested*.

# Part 2 — animations

## A-1 Enumerate animation modules, split by kind

```csharp
static Anim[] SnapshotAnimations(Vehicle vehicle) =>
    vehicle.Parts.Modules.Get<Anim>().ToArray().OrderBy(m => m.InstanceId).ToArray();

static void Split(Anim[] all, out Anim[] deployables, out Anim[] actuators)
{
    deployables = all.Where(m =>  m.ShowDeployRetract && m.Shared.Duration > 0f).ToArray();   // Deploy / Retract in the stock UI
    actuators   = all.Where(m => !m.ShowDeployRetract && m.Shared.Duration > 0f).ToArray();   // Actuate slider in the stock UI
}
```

- **Provenance:** enumeration *from stock kFS* (same pattern as L-1) and *from gatOS*
  (`vehicle.Parts.Modules.Get<KeyframeAnimationModule>()`); the split by `ShowDeployRetract` is
  *constructed from the decompiled API — untested* — it is the flag the stock part window branches
  on.[^keyframe-animation-module][^gatos-animation-actuator]
- **Thread:** any; allocates.
- **Side effects:** none.
- The `Duration > 0` filter (zippo applies the same test) drops an asset that failed to load or has
  no keyframes, which would otherwise divide by zero in fraction maths.[^zippo-submod-disco]

Classification helpers for a descriptor table:

```csharp
static bool DrivesSolarPanel(Anim m)   => m.Parent.SubtreeModules.HasAny<SolarPanel>();
static bool DrivenByParachute(Anim m)  => m.Parent.SubtreeModules.HasAny<Parachute>();     // chute state machine also writes TimeGoal
static bool AimsALight(Anim m)         => m.Parent.SubtreeModules.HasAny<LightModule>();
static bool HasSolarTracking(Anim m)   => m.Extension is SolarTrackingExtension;
static (string part, string templateId, string key, uint partInstanceId) Describe(Anim m) =>
    (m.Parent.DisplayName, m.Parent.Template.Id, m.TemplateId, m.Parent.InstanceId);
```

- **Provenance:** *from gatOS* (`AnimationLinks.Build`) for the solar and light links; the rest
  *constructed — untested*.[^gatos-animation-links] `m.Parent` is the full part for every stock animation
  module.

## A-2 Read deployed state and progress

```csharp
readonly record struct AnimReadout(float Goal01, float Position01, Anim.DeploymentState State, bool Moving, float DurationS);

static AnimReadout Read(Anim m)
{
    float d = m.Shared.Duration;
    float goal = m.TimeGoal;
    float goal01 = d > 0f ? Math.Clamp(goal / d, 0f, 1f) : 0f;

    PartTree? tree = m.Parent.Tree;                        // the module's OWN tree: StatesIdx indexes that tree's list
    if (tree != null && AnimStates.TryGetFrom(tree.States, out AnimStates.StateList list)
        && (uint)m.StatesIdx < (uint)list.States.Length)
    {
        ref readonly Anim.State s = ref list.States[m.StatesIdx];
        float pos01 = d > 0f ? Math.Clamp(s.TimeCurrent / d, 0f, 1f) : 0f;
        return new AnimReadout(goal01, pos01, s.DeploymentState, goal != s.TimeCurrent, d);
    }
    return new AnimReadout(goal01, goal01, Anim.DeriveDeploymentState(goal, goal), false, d);
}
```

- **Provenance:** *from gatOS* (`VesselReader.SampleAnimations`), with an explicit bounds check
  added; the fallback branch is gatOS's.[^gatos-vessel-reader][^module-stateful]
- **Thread:** STEP, IDLE or UI. Reads the **committed** state, which is one step old during STEP.
- **Side effects:** none; no allocation.
- `State` values: `Deployed = 0`, `Retracted = 1`, `Deploying = 2`, `Retracting = 3`, `Broken = 4`
  (never produced in this build). A resting actuator anywhere above 0 reads `Deployed`.
- "Deployed?" for a toggle-style API: `State == Deployed` (at rest, open) or, for the *commanded*
  sense, `m.TimeGoal > 0f`.

Freshest value inside the step (`SolarPanel.UpdateModules` obtains the same updater for its own
gating, but reads through `GetModuleAndReadOnlyState`, which always returns the committed state):

```csharp
// vehicleState is the VehicleUpdateState passed to KfsDispatch.TryComputeControl
if (vehicleState.UpdateData.Updaters.TryGetOrCreateTypeStateUpdater(out AnimStates.StateUpdater updater))
{
    ref readonly Anim.State s = ref updater.GetReadOnlyState(module);   // this step's buffer if already written, else committed
}
```

- **Provenance:** *constructed from the decompiled API — untested*; `TryGetOrCreateTypeStateUpdater`
  is the call used by `KeyframeAnimationModule.UpdateModules` and `SolarPanel.UpdateModules`;
  `GetReadOnlyState` is a public `StateUpdater` accessor that no stock `UpdateModules` cited here
  calls.[^module-state-updaters][^solar-panel][^vehicle-update-data][^module-stateful]
- **Thread:** STEP only, and only for the vehicle being dispatched. The flight computer runs before
  `UpdateModules` in a step, so at that point this returns the same values as the committed read; it
  is listed for completeness.

## A-3 Toggle deployed / retracted

```csharp
static void SetDeployed(Anim m, bool deployed) =>
    m.TimeGoal = deployed ? m.Shared.Duration : 0f;

static void ToggleDeployed(Anim m) =>
    m.TimeGoal = m.TimeGoal > 0f ? 0f : m.Shared.Duration;          // toggles the COMMAND, valid mid-travel
```

- **Provenance:** *from the stock UI path* — the Deploy and Retract buttons assign exactly these two
  values.[^keyframe-animation-module] Stock offers the button only at rest; the module's update loop accepts
  a change at any time.
- **Thread:** **STEP is acceptable** — a single `float` the same step reads in
  `KeyframeAnimationModule.UpdateModules`; stock writes it from the worker in
  `Parachute.UpdateModules` → `Deploy()` → `SetBayDoorsOpen`.[^parachute] Also IDLE or UI.
- **Side effects:** the part's sub-parts move over `Shared.Duration` seconds of simulation time;
  colliders are flagged for update each step it moves; the vehicle is held **off rails** while moving;
  a linked `SolarPanel` produces power only while the state is `Deployed`; a tracking panel first
  rotates back to zero before it starts to fold. No sound, alert, history entry or power cost. The
  part window shows the new state on its next draw. Goal and position are saved by the stock game.
- **Not included:** the stock button's fan-out to mirrored parts (`ApplyToMirroredParts` is private).

Mirrored variant, reimplemented:

```csharp
static void SetDeployedMirrored(Anim m, bool deployed)
{
    float goal = deployed ? m.Shared.Duration : 0f;
    m.TimeGoal = goal;
    if (m.Parent.SymmetryLink is not { } link) return;
    foreach (Part.SymmetryData sym in link.Symmetries)
        foreach (Part other in sym.Parts)
        {
            if (ReferenceEquals(other, m.Parent)) continue;
            foreach (Anim twin in other.SubtreeModules.Get<Anim>())
                if (twin.TemplateId == m.TemplateId) { twin.TimeGoal = goal; break; }
        }
}
```

- **Provenance:** *constructed from the decompiled API — untested*, following
  `ApplyToMirroredParts` but without its "only counterparts currently in the same state" condition.
- **Thread:** IDLE or UI (walks symmetry lists).

## A-4 Read and set an actuation value 0.0 – 1.0

```csharp
static float GetActuationTarget(Anim m) =>
    m.Shared.Duration > 0f ? Math.Clamp(m.TimeGoal / m.Shared.Duration, 0f, 1f) : 0f;

static void SetActuation(Anim m, float value01)
{
    if (!float.IsFinite(value01)) return;                        // a NaN goal never settles (see below)
    m.TimeGoal = Math.Clamp(value01, 0f, 1f) * m.Shared.Duration;
}
```

- **Provenance:** *from the stock UI path* (the Actuate slider: `TimeGoal = v * Shared.Duration`),
  *from gatOS* (`AnimationActuator.SetGoal`, with the clamp) and *from zippo* (Disco actuation).[^keyframe-animation-module][^gatos-animation-actuator][^zippo-disco-light]
- **Thread:** **STEP is acceptable**, same reasoning as A-3; also IDLE or UI.
- **Side effects:** as A-3. The value is a *target*: position slews toward it at one animation-second
  per simulation-second, so a full sweep takes `Shared.Duration` seconds and the actual position must
  be read back with A-2. Works for either kind of module — the split in A-1 is presentation only.
- **Guard non-finite input.** With `TimeGoal = NaN` every comparison in the update loop is false: the
  loop falls into the "deploying" branch, adds `DeltaTime` to `TimeCurrent` forever and
  `AnyAnimating` stays true, pinning the vehicle off rails (the reported state is then `Deployed`,
  not `Deploying`, because `DeriveDeploymentState` also compares against NaN). Read from
  `UpdateModules` :176–213, `DeriveDeploymentState` :122–137 and `AnyAnimating` :149; not observed
  in the running game.
- A program that writes a different value every tick keeps the module permanently in motion; consider
  quantising or rate-limiting if on-rails time warp matters.

# Part 3 — shared plumbing

## P-1 "Is this element still on my vehicle?"

```csharp
static bool StillAboard(Vehicle vehicle, ModuleBase m) =>
    ReferenceEquals(m.Parent.Tree, vehicle.Parts) && !vehicle.IsDisposed;
```

- **Provenance:** *constructed from the decompiled API — untested*; the game uses the same `Tree`
  comparison (`Module<T>.List.RemoveTransferredOut`, `Vehicle.ToggleLights`,
  `Part.IsLightSwitchedOff`). Stock kFS instead rebuilds a `HashSet` of the vehicle's current modules
  in `KfsControllerSet.MarkDeparted` when the element counts stop matching.[^kfs-controller-set][^kfs-vehicle-host]
- **Thread:** any. Run it before every write and treat a departed element as inert, as stock kFS does
  for engines, thrusters and gimbals (see
  [controller set and layout changes](/integration/controller-set-and-layout-changes.md)).
- A light whose part staged away keeps its private template; restore the shared template (L-4) when
  marking it inert if the program should not leave a mark on the departed stage.

## P-2 Deferring appearance writes to the main thread

Shape only — the hook point is a `/modding/` concern:

```csharp
// STEP (worker): record intent, no game writes, no allocation after warm-up
lock (_gate) { _pending.Add(new LightEdit(lightIndex, kind, a, b, c)); }

// IDLE (main thread, between "results applied" and "next step started")
List<LightEdit> batch;
lock (_gate) { if (_pending.Count == 0) return; batch = _pending; _pending = _spare; _spare = batch; }
foreach (LightEdit e in batch) Apply(e);      // L-4 on first touch, then L-5 / L-6 / L-7
batch.Clear();
```

- **Provenance:** *from stock kFS* — `KfsServices.Queue` / `MainThread` use this
  lock-swap-drain shape for staging, engine-off and burn planning, drained from
  `KfsIntegration.BeforeInputEvents()` (stock swaps in a newly allocated list on every non-empty
  drain; the recycled spare above is a refinement).[^kfs-services] gatOS's `CommandQueue` is the same idea with
  concurrent queues.
- Coalesce per light and field before applying if a program writes every tick; the renderer only
  ever sees the last value of a frame.

# Summary table

| Operation | Recipe | Game write | Provenance | STEP? | Stock side effects |
| --- | --- | --- | --- | --- | --- |
| enumerate lights | L-1 | — | stock kFS pattern | any (allocates) | none |
| read light | L-2 | — | gatOS + stock predicate | yes | none |
| light on/off | L-3 | `FullPart.LightSwitch.LightIsActive` | zippo, gatOS, stock | **yes** | power draw, lens glow, all lights on the part |
| private template | L-4 | `LightModule.Template = copy` | zippo, gatOS | prefer IDLE | none |
| colour | L-5 | `ColorRgb.R/G/B`, `IndexedColor`, `OnDataLoad(null)` | zippo, gatOS | prefer IDLE (not atomic) | none |
| cone angles | L-6 | `InnerAngle.Value`, `OuterAngle.Value` (radians) | gatOS, zippo | prefer IDLE | none |
| brightness | L-7 | `Intensity.Value` | gatOS, zippo | prefer IDLE | none |
| enumerate animations | A-1 | — | stock kFS pattern, gatOS | any (allocates) | none |
| read animation | A-2 | — | gatOS | yes | none |
| deploy / retract | A-3 | `TimeGoal = Duration` / `0` | stock UI | **yes** | motion, colliders, off-rails, solar gating |
| actuation 0–1 | A-4 | `TimeGoal = v × Duration` | stock UI, gatOS, zippo | **yes** | same |

# Extension design answers in one place

**Stable addressing.** Use the index into an `InstanceId`-sorted array captured once (L-1, A-1),
exactly as stock kFS addresses engines, thrusters and gimbals. It is stable across frames; across
staging and undocking (departed elements keep their slot and become inert, P-1); and across docking
(new parts are simply not in the array until it is rebuilt). It is **not** meaningful across
save/load or a system change — module and part `InstanceId`s are reassigned and the array must be
rebuilt, with relative order preserved only for a vehicle whose tree did not change shape
(**Inferred**, see [part and module model](/game-targets/part-and-module-model.md)). The unsorted
span index gatOS uses shifts whenever an earlier module leaves. `Part.InstanceId` is a good
*descriptor* to publish next to each slot, and `Part.Id` is not unique.

**Read-back cost.** Everything a program would poll is a field read or one bounds-checked read of a
committed state buffer, with no allocation (L-2, A-2): commanded on, lit, type, intensity, range,
colour, inner/outer angle, animation goal, animation position, deployment state, is-moving, duration.
State-buffer values are one physics step old.

**Numeric representation.**

| Quantity | Game type | Unit / range | Suggested cell |
| --- | --- | --- | --- |
| light commanded on / lit / has switch | `bool` | — | `u32` 0/1 |
| light type | `LightModule.TemplateData.LightType` | `Spot = 0`, `Point = 1` | `u32` |
| intensity | `float` (`FloatReference.Value`) | unitless, ≥ 0, no upper bound; stock 0.05–50 | `f32` |
| range | `float` | metres, > 0 (≈ 0 disables the emitter) | `f32` |
| colour R, G, B | `float` × 3 | nominal 0–1; colour space `Unverified:` (linear or sRGB) | 3 × `f32` (or one packed `u32` if 8-bit is enough) |
| inner half-angle | `float` | radians, `0 … outer` | `f32` |
| outer half-angle | `float` | radians, `1e-5 … 1.5697963` | `f32` |
| animation kind | `bool ShowDeployRetract` | 1 = deploy/retract, 0 = actuator | `u32` |
| animation duration | `float Shared.Duration` | seconds of simulation time, asset-defined | `f32` |
| animation goal | `float TimeGoal` | seconds `0 … Duration`; as a fraction `0 … 1` | `f32` |
| animation position | `float State.TimeCurrent` | seconds; fraction `0 … 1` | `f32` |
| deployment state | `KeyframeAnimationModule.DeploymentState` | 0 Deployed, 1 Retracted, 2 Deploying, 3 Retracting, 4 Broken (unused) | `u32` |
| module / part instance id | `ulong` / `uint` | values come from one `uint` counter | `u32` |

**Access level.** Nothing on these paths is private or internal; see the per-document modding notes
for the few non-public helpers that exist nearby and are not needed.

# Modding notes

- Every recipe uses public members only. No reflection, publicizer or Harmony patch is involved in
  the game-side operations themselves; a patch is needed only to *get called* at the right time.
- The two writes that affect simulation (`LightIsActive`, `TimeGoal`) are single-word field writes
  that stock code already performs concurrently with or inside the physics step. The appearance
  writes are render-only and are best queued to the solver-idle window.
- Never write `KeyframeAnimationModule.State` or `PowerConsumerState`; they are outputs.
- Validate every program-supplied number for finiteness before it reaches a game field — the game
  does not.
- Nothing here has been exercised in the running game for this bundle.

[^light-module]: LightModule.cs — `TemplateData` :11–52; `Template` :58; `IsActive` :60–70; `UpdateRenderData` :93–132.
[^light]: KSA.Rendering.Lighting/Light.cs — clamp constants :10–12; `CreateSpotLight` :54–79.
[^clustered-light-system]: ClusteredLightSystem.cs — `CreateLightInstance` :911–935.
[^color-rgb-reference]: ColorRgbReference.cs — fields :9–22; `OnDataLoad` :35–44.
[^float-reference]: FloatReference.cs:5–24.
[^module-base]: ModuleBase.cs — `TemplateDataBase` constructor and `DefaultKey` :30–38; KeyedTemplate.cs:5–14.
[^content-resolve-scope]: ContentResolveScope.cs — thread-static `Current` :7–8; `Track(IKeyed)` :34–37.
[^power-consumer]: PowerConsumer.cs — `IsSwitchedOn` :138–146; `UpdateModules` :235–272; `SetLightIsActive` :274–280; `DrawStateInfo` :302–309.
[^part]: Part.cs — `LightSwitch` :734; `IsLightSwitchedOff` :1551–1567; `ResolveLightSwitch` :2082–2108.
[^vehicle]: Vehicle.cs — `LightsOn` :297; `ToggleLights` :1677–1690.
[^keyframe-animation-module]: KeyframeAnimationModule.cs — `State` :77–82; `TimeGoal` :86; `ShowDeployRetract` :92; `DeriveDeploymentState` :122–137; `UpdateModules` :162–214; `ApplyToMirroredParts` :259–285; `ShowContextMenu` :287–321.
[^module-list]: ModuleList.cs — `Get<T>` :506–517; `HasAny<T>` :531–543.
[^module-stateful]: ModuleStateful.cs — `StateList.States` :268; `GetState` :437–441; `StateUpdater.GetReadOnlyStates` :678–686; `StateUpdater.GetReadOnlyState` :721–731; `StateUpdater.GetModuleAndReadOnlyState` :745–749; `StatesIdx` :810; `TryGetFrom` :843–847.
[^module-state-updaters]: ModuleStateUpdaters.cs — `TryGetOrCreateTypeStateUpdater` :59–72.
[^vehicle-update-data]: VehicleUpdateData.cs — `Updaters` :52, constructed :73.
[^solar-panel]: SolarPanel.cs:120–134.
[^parachute]: Parachute.cs — `Deploy` :725–747; `SetBayDoorsOpen` :794–800; `UpdateModules` :1096–1119.
[^kfs-controller-set]: KfsControllerSet.cs — constructor :60–85; `Matches` :112–119; `MarkDeparted` :121–139.
[^kfs-vehicle-host]: KfsVehicleHost.cs — sets built in the constructor :105–118; `DetectLayoutChange` :428–440.
[^kfs-services]: KfsServices.cs — `Queue` :543–549; `MainThread` :551–624.
[^zippo-disco-light]: zippo.lib/DiscoLight.cs — constructor :36–67; colour and angle writes :113–131; actuator write :136–141; `Dispose` :166–187.
[^zippo-submod]: zippo.lib/ZippoSubmod.cs — on/off :157–170, :476–483.
[^zippo-submod-disco]: zippo.lib/ZippoSubmod.Disco.cs — actuator selection :218–228.
[^gatos-light-actuator]: gatOS.GameMod/Game/Ksa/Actuators/LightActuator.cs — `SetMaster` :34–41; `SetOn` :45–53; `SetBrightness` :58–65; `SetColor` :70–83; `SetOuterAngle` :92–104; `SetInnerAngle` :111–119; `EnsureUnshared` :127–138.
[^gatos-animation-actuator]: gatOS.GameMod/Game/Ksa/Actuators/AnimationActuator.cs:20–28.
[^gatos-vessel-reader]: gatOS.GameMod/Game/Ksa/Readers/VesselReader.cs — `SampleLights` :826–847; `SampleAnimations` :945–978.
[^gatos-animation-links]: gatOS.GameMod/Game/Ksa/Readers/AnimationLinks.cs:75–101.
