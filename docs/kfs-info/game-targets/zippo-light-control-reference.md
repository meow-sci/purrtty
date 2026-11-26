---
type: Game System Reference
title: zippo light control — how an existing mod enumerates and mutates KSA lights
description: A code-level account of unscience's zippo light mod - its two mutation paths (shared-template reflection writes and per-instance template replacement), the exact game members it touches, where it runs, how it identifies parts, how it persists and restores changes, and every pitfall its sources and documents record.
resource: unscience:zippo.lib/LightController.cs
tags: [kfs, game-targets, lights, zippo, unscience, mod-precedent, reflection, persistence]
sources:
  - id: zippo-light-controller
    resource: unscience:zippo.lib/LightController.cs
    title: zippo LightController
  - id: zippo-light-controller-persistence
    resource: unscience:zippo.lib/LightController.Persistence.cs
    title: zippo LightController persistence ledger
  - id: zippo-submod
    resource: unscience:zippo.lib/ZippoSubmod.cs
    title: zippo ZippoSubmod (UI and public API)
  - id: zippo-submod-disco
    resource: unscience:zippo.lib/ZippoSubmod.Disco.cs
    title: zippo ZippoSubmod Disco coordination
  - id: zippo-submod-persistence
    resource: unscience:zippo.lib/ZippoSubmod.Persistence.cs
    title: zippo ZippoSubmod save participant
  - id: zippo-disco-light
    resource: unscience:zippo.lib/DiscoLight.cs
    title: zippo DiscoLight (per-instance template ownership)
  - id: zippo-disco-light-persistence
    resource: unscience:zippo.lib/DiscoLight.Persistence.cs
    title: zippo DiscoLight persistence
  - id: zippo-disco-recipe
    resource: unscience:zippo.lib/DiscoRecipe.cs
    title: zippo DiscoRecipe and DiscoTiming
  - id: zippo-light-animation-manager
    resource: unscience:zippo.lib/LightAnimationManager.cs
    title: zippo LightAnimationManager and LightAnimation
  - id: zippo-light-part-info
    resource: unscience:zippo.lib/LightPartInfo.cs
    title: zippo LightPartInfo
  - id: zippo-mod
    resource: unscience:zippo/Mod.cs
    title: zippo standalone mod entry
  - id: zippo-patcher
    resource: unscience:zippo/Patcher.cs
    title: zippo Patcher
  - id: zippo-readme
    resource: unscience:zippo/README.md
    title: zippo README
  - id: zippo-spec
    resource: unscience:zippo/SPEC.md
    title: zippo SPEC
  - id: zippo-lib-readme
    resource: unscience:zippo.lib/README.md
    title: zippo.lib README
  - id: zippo-plan
    resource: unscience:plans/done/ZIPPO_ANIMATIONS_PLAN.md
    title: Zippo light animations implementation plan
  - id: unscience-scope-lights
    resource: unscience:scope/celestial-and-lights.md
    title: unscience scope - celestial welding and lights
  - id: unscience-part-helpers
    resource: unscience:ksa-abstractions.lib/PartHelpers.cs
    title: unscience PartHelpers and VehicleProvider
  - id: unscience-saved-part-reference
    resource: unscience:ksa-abstractions.lib/Persistence/SavedPartReference.cs
    title: unscience SavedPartReference
  - id: starmap-runtime-mod
    resource: starmap:StarMap.Core/ModRepository/RuntimeMod.cs
    title: StarMap RuntimeMod (GUI hook patch targets)
  - id: light-module
    resource: ksa-decomp:KSA/LightModule.cs
    title: LightModule (decompiled)
  - id: color-rgb-reference
    resource: ksa-decomp:KSA/ColorRgbReference.cs
    title: ColorRgbReference (decompiled)
  - id: part-template
    resource: ksa-decomp:KSA/PartTemplate.cs
    title: PartTemplate (decompiled)
generated: { by: claude-code/claude-fable-5-2, at: 2026-11-25T21:36:15Z }
status: stable
ksa_build: "2026.11.25.5949"
---

# What zippo is

`zippo` is a light-control submod of the `meow-sci/unscience` repository: a thin StarMap entry
project (`zippo/`) over a library (`zippo.lib/`) that is also hosted inside the combined `unscience`
mod. It lets a player pick a vehicle and one of its light parts, switch it, change intensity and
colour, queue timed colour/intensity transitions, and run "Disco" recipes that cycle colour,
spotlight cone angles and the light's aiming actuator.[^zippo-lib-readme]

It is the closest existing precedent for the kFS light extension, and it contains **two different
mutation strategies** that must not be confused:

| | "Ordinary" path | "Disco" path |
| --- | --- | --- |
| Class | `LightController` (static) | `DiscoLight` (one object per active light part) |
| Writes | intensity, colour | colour, inner/outer cone angle, actuator `TimeGoal`, light switch |
| Target object | the **shared** `LightModule.TemplateData` inside `PartTemplate.Components` | a **module-local copy** installed in `LightModule.Template` |
| Scope of effect | every light created from that part template, on every vehicle | that one `LightModule` |
| Access style | string reflection | direct typed member access |
| Restore | ledger of originals, replayed on scene reset | swap the original template reference back |

The game-side facts these rely on are in [LightModule](/game-targets/light-module.md); the decompiled
code confirms them for this build (the shared template reference, the public `Template` field, the
`OnDataLoad` requirement for colour).[^light-module][^color-rgb-reference]

# Enumerating light parts

```csharp
// LightController.cs:103-104
public static List<Part> GetLightParts(Vehicle vehicle) =>
    PartHelpers.GetPartsWhere(vehicle, p => p.Template != null && HasLights(p.Template));
```

- `PartHelpers.GetAllParts(vehicle)` iterates `vehicle.Parts.Parts` and recurses through
  `part.SubParts` to any depth; `GetPartsWhere` filters that list.[^unscience-part-helpers]
- `HasLights(template)` is `GetLightComponents(template).Count > 0`, where `GetLightComponents`
  reads the template's `Components` list **by reflection** (`ReflectionHelpers.GetFieldValue(t,
  "Components") as IList`) and keeps entries whose `GetType().FullName ==
  "KSA.LightModule+TemplateData"`.[^zippo-light-controller]
- Vehicles come from `VehicleProvider.GetAllVehicles()` = `Universe.CurrentSystem?.All.UnsafeAsList()
  .OfType<Vehicle>()`, excluding `IsDebris` unless asked.[^unscience-part-helpers]

Consequences:

- A zippo "light part" is **whichever `Part` object's own template declares `<Light>`** — a sub-part
  for the dedicated light parts (`CoreElectricalA_Subpart_SpotlightA1`, …), the full part for
  capsules. It is a `Part`, not a `LightModule`: a part with two `<Light>` components is one entry.
- `PartTemplate.Components` is a public field in this build; the reflection is not required, only
  historical.[^part-template]
- `GetLightComponents` has a side effect: it records every component it returns in the static
  `ComponentOwners` dictionary (component → owning template) used by the persistence ledger.[^zippo-light-controller][^zippo-light-controller-persistence]
- The UI label is `part.DisplayName ?? part.Id`.[^zippo-submod]

Disco, by contrast, takes the modules directly: `part.Modules.Get<LightModule>()`.[^zippo-disco-light]

# On / off

Identical in every zippo code path:[^zippo-submod][^zippo-disco-light]

```csharp
// ZippoSubmod.cs:157, 166-169
var ls = selectedPart.LightSwitch ?? selectedPart.FullPart.LightSwitch;
if (ls != null)
    ls.LightIsActive = _lightEnabled;
else
    LightController.ApplyIntensity(selectedPart, _lightEnabled ? _savedIntensity : 0f);
```

- Game members: `Part.LightSwitch` (field), `Part.FullPart`, `PowerConsumer.LightIsActive` (field).
  A direct field write — not `Vehicle.ToggleLights()`, not `PowerConsumer.SetLightIsActive` (so no
  symmetry fan-out).
- **Fallback for a light with no switch**: write intensity 0 into the (shared) template.
- Read-back for the public API (`LightPartInfo.IsEnabled`) is `ls == null || ls.LightIsActive` — the
  *commanded* state; it does not account for power.[^zippo-submod][^zippo-light-part-info]

# Intensity and colour — the "ordinary" path (shared template, reflection)

```csharp
// LightController.cs:67-94 (abridged)
public static void WriteIntensity(List<object> lights, float intensity)
{
    foreach (var light in lights)
    {
        RememberOriginal(light);
        var intensityRef = ReflectionHelpers.GetFieldValue(light, "Intensity");
        ReflectionHelpers.SetFieldValue(intensityRef, "Value", intensity);
    }
}

public static void WriteColor(List<object> lights, float3 color)
{
    foreach (var light in lights)
    {
        RememberOriginal(light);
        var colorRef = ReflectionHelpers.GetFieldValue(light, "ColorRgb");
        if (colorRef == null) continue;
        ReflectionHelpers.SetFieldValue(colorRef, "R", color.X);
        ReflectionHelpers.SetFieldValue(colorRef, "G", color.Y);
        ReflectionHelpers.SetFieldValue(colorRef, "B", color.Z);
        ReflectionHelpers.SetFieldValue(colorRef, "IndexedColor", IndexedColor.Invalid);
        try { colorRef.GetType().GetMethod("OnDataLoad", All)?.Invoke(colorRef, new object?[] { null }); }
        catch (Exception ex) { … }
    }
}

public static void ApplyIntensity(Part part, float intensity) => WriteIntensity(GetLightComponents(part.Template), intensity);
public static void ApplyColor(Part part, float3 color)        => WriteColor(GetLightComponents(part.Template), color);
```
[^zippo-light-controller]

| zippo call | Decompiled target | Notes |
| --- | --- | --- |
| `GetFieldValue(t, "Components")` | `PartTemplate.Components : List<ModuleBase.TemplateDataBase>` (public field) | |
| type name `"KSA.LightModule+TemplateData"` | `LightModule.TemplateData` (nested, `[XmlType("Light")]`) | hard-coded string |
| `"Intensity"` → `"Value"` | `TemplateData.Intensity : FloatReference` → `FloatReference.Value : float` | |
| `"ColorRgb"` → `"R"`, `"G"`, `"B"` | `TemplateData.ColorRgb : ColorRgbReference` → public float fields | |
| `"IndexedColor"` = `IndexedColor.Invalid` | `ColorRgbReference.IndexedColor` | the one compile-checked game reference in this path |
| `"OnDataLoad"` invoked with `null` | `ColorRgbReference.OnDataLoad(Mod)` | recomputes the rendered `Value` |

Behaviour to be aware of:

- **All light components of the part's template are written together**, and reads
  (`ReadIntensity`, `ReadColor`) return only the first component's values.
- **The template is shared**: `ApplyColor(part, red)` turns every spotlight of that type red, on
  every craft, including ones built later in the session. zippo's library README calls this out as
  intentional compatibility ("Ordinary appearance changes … remain intentionally compatible with the
  existing Zippo behavior").[^zippo-lib-readme] its-so-shiny, another unscience submod, relies on it to
  colour whole grids of identical light parts.
- **The intensity UI is a 0–1 `DragFloat`** and reads are clamped to 0–1 (`ZippoSubmod.cs:178`,
  `:601`), a convention that comes from the original spec ("a drag float slider … which goes from 0
  to 1")[^zippo-spec] and not from the engine: the stock spotlight's intensity is 10. Selecting a
  stock spotlight and touching the slider therefore makes it at most a tenth as bright.

# Colour and cone angles — the Disco path (per-instance template)

`DiscoLight`'s constructor gives each `LightModule` of the target part its own template:[^zippo-disco-light]

```csharp
// DiscoLight.cs:36-67
foreach (var module in part.Modules.Get<LightModule>())
{
    var original = module.Template;
    var owned = new LightModule.TemplateData
    {
        Key = original.Key, Type = original.Type, Transform = original.Transform,
        Range = original.Range, Intensity = original.Intensity, ColorRgb = original.ColorRgb,
        InnerAngle = original.InnerAngle, OuterAngle = original.OuterAngle,
        RayTracing = original.RayTracing, DisableInIva = original.DisableInIva,
    };
    if (Recipe.Color)
    {
        owned.ColorRgb = new ColorRgbReference((float3)original.ColorRgb);
        owned.ColorRgb.OnDataLoad(null!);
    }
    if (Recipe.Spread && owned.Type == LightModule.TemplateData.LightType.Spot)
    {
        owned.InnerAngle = new FloatReference(original.InnerAngle.Value);
        owned.OuterAngle = new FloatReference(original.OuterAngle.Value);
    }
    _lights.Add((module, original, owned));
    module.Template = owned;
}
```

Points to copy:

- The new `TemplateData` initially **shares** every reference-typed field with the original
  (`Transform`, `Range`, `Intensity`, `ColorRgb`, `InnerAngle`, `OuterAngle`); only the fields a
  channel will write get private objects. `Intensity` and `Range` are never privately cloned because
  Disco never writes them.
- `new ColorRgbReference(float3)` must be followed by `OnDataLoad(null!)` — the constructor does not
  set the rendered `Value`.
- All ten fields are copied by name. The scope document records that the key field was `Id` before
  KSA 5801 and is `Key` since, and that "Field additions to the game require review so the copy
  remains complete".[^unscience-scope-lights]

Per-frame writes:[^zippo-disco-light]

```csharp
// DiscoLight.cs:113-131
foreach (var (module, _, owned) in _lights)
{
    if (!ReferenceEquals(module.Template, owned)) continue;      // someone else replaced it: hands off
    if (Recipe.Color)
    {
        owned.ColorRgb.R = color.X; owned.ColorRgb.G = color.Y; owned.ColorRgb.B = color.Z;
        owned.ColorRgb.IndexedColor = IndexedColor.Invalid;
        owned.ColorRgb.OnDataLoad(null!);
    }
    if (Recipe.Spread && owned.Type == LightModule.TemplateData.LightType.Spot)
    {
        owned.InnerAngle.Value = Lerp(Recipe.InnerMin, Recipe.InnerMax, spreadMix) * MathF.PI / 180f;
        owned.OuterAngle.Value = Lerp(Recipe.OuterMin, Recipe.OuterMax, spreadMix) * MathF.PI / 180f;
    }
}
```

- Angles are authored in **degrees of half-angle**, validated to `0.1 … 89` with inner ≤ outer at
  both ends of the sweep, and stored in radians.[^zippo-disco-recipe]
- Point lights skip the spread channel.
- Colours are validated to the unit range per component.[^zippo-disco-recipe]

# The aiming actuator (zippo drives `KeyframeAnimationModule`)

Disco's "actuation" channel moves the light part's animation:[^zippo-submod-disco][^zippo-disco-light]

```csharp
// ZippoSubmod.Disco.cs:218-228 — choose the actuator
foreach (var module in part.FullPart.Modules.Get<KeyframeAnimationModule>())
{
    if (module.Shared.Duration <= 0f || !module.Shared.PartLookup.ContainsKey(part.Id) || !claimed.Add(module))
        continue;
    foreach (var other in _discoLights.Values) other.ReleaseActuator(module);
    live.AddActuator(module);
}

// DiscoLight.cs:136-141 — drive it
float goal = Lerp(Recipe.ActuationMin, Recipe.ActuationMax, actuationMix) * actuator.Shared.Duration;
actuator.TimeGoal = goal;
```

- The actuator is found on the **full part** (`part.FullPart.Modules`) and accepted only if its
  animation actually moves this light's sub-part (`Shared.PartLookup.ContainsKey(part.Id)`) and has a
  positive duration.
- One module moves the whole assembly (the twin spotlight has two lights and one animation), so
  exactly one Disco light may own it; a later start takes ownership.
- The range is a normalised 0–1 fraction multiplied by `Shared.Duration` — the same convention as the
  stock Actuate slider.
- zippo's README records the rate limit: "KSA moves toward each goal at the mechanism's own rate, so
  very short cycles can outpace it", and that pausing "freezes the recipe clock, although a mechanism
  may finish moving toward its most recent goal".[^zippo-readme]

# Harmony patches

**None for lights or animations.** `Patcher.Patch()` only runs `PatchAll` over an assembly with no
light patches and installs the shared `HotkeyGuard` (blocks game hotkeys while typing in ImGui).[^zippo-patcher]
The scope document states it for Disco explicitly: "No Harmony target, shader, render-pass, byte
layout, or game asset is added."[^unscience-scope-lights] Everything zippo does is field writes on
public members plus the string reflection of the ordinary path.

# Thread and timing

- Standalone: `[StarMapBeforeGui] OnBeforeUi(dt)` → `ZippoSubmod.Update(dt)` (ticks the transition
  queue and every `DiscoLight`); `[StarMapAfterGui] OnAfterUi(dt)` → the ImGui window (F11).[^zippo-mod]
  StarMap implements these as a prefix on `Program.OnDrawUiFrame` and a postfix on
  `Program.OnDrawUiViewports`.[^starmap-runtime-mod]
- So **every zippo mutation happens on the main thread during the UI phase**, i.e. after the frame's
  physics step has been started and while it may still be running — the same timing as the stock
  part window (see [stock control paths](/game-targets/stock-control-paths.md)). zippo takes no lock
  and waits for no job.
- The renderer reads the template later in the same frame, so appearance changes show immediately;
  the switch and the actuator goal are picked up by the next physics step.
- Hosted in `unscience`, the same `Update` is called from the host's submod loop, and a
  `HiddenUiFrameHook` keeps it running while F2 hides the HUD; standalone playback "follows StarMap
  and pauses while that game UI hook is skipped".[^unscience-scope-lights]
- The HTTP RPC surface planned for zippo marshals to the game thread: "All game state access goes
  through `GameThread.Scheduler.Schedule()`; animation ticking happens on game thread via
  `Update(dt)`".[^zippo-plan]

# Identity: how zippo addresses a light over time

| Use | Key | Source |
| --- | --- | --- |
| Transition queues | the string `$"part-instance:{part.InstanceId}"`; resolved every frame by scanning all vehicles (including debris) for a light part with that key; unresolvable → queue cancelled | `ZippoSubmod.cs:525-537`, `LightAnimationManager.cs:16`, `:62-74` |
| Active Disco effects | the live `Part` object (`Dictionary<Part, DiscoLight>` with `ReferenceEqualityComparer`); each update builds the set of all live parts across all vehicles including debris and disposes effects whose part is gone | `ZippoSubmod.Disco.cs:16`, `:244-260` |
| Public API (`SetLightState`, `QueueAnimation`) | `vehicle.Id` + `part.Id`, first match | `ZippoSubmod.cs:468-485`, `:539-544` |
| Saves | `SavedPartReference` (vehicle id + tree path + sub-part path + template id + topology hash) | `ZippoSubmod.Persistence.cs:20-30`, `SavedPartReference.cs` |

[^zippo-submod][^zippo-light-animation-manager][^zippo-submod-disco][^zippo-submod-persistence][^unscience-saved-part-reference]

The comment at the queue dictionary gives the reason for instance ids: "Key = runtime-unique
Part.InstanceId token. Part.Id is only a template/subpart name and can collide."[^zippo-light-animation-manager]
The original plan had used `Part.Id` strings resolved fresh each frame.[^zippo-plan] The public API still
resolves by `part.Id`, which is ambiguous when a vehicle carries two of the same light part.

Because a part that stages away keeps its `InstanceId` and merely changes vehicle, zippo's
all-vehicles scan keeps following it; nothing is retargeted to a different part.

# Persistence of overrides

zippo adds persistence only when hosted by `unscience`, through that mod's save-sidecar framework
(`ISaveParticipantSource`, record id `"zippo"`).[^zippo-submod-persistence]

**Shared-template ledger** (`LightController.Persistence.cs`):[^zippo-light-controller-persistence]

- `ComponentOwners : Dictionary<object, PartTemplate>` and `OriginalSettings : Dictionary<object,
  (float Intensity, float3 Color)>`, both keyed by component reference.
- `RememberOriginal(component)` runs before the first write to a known component and stores its
  pre-mutation intensity and colour.
- `CaptureTemplateChanges()` emits one `SavedLightTemplate { TemplateId, ComponentIndex,
  OriginalIntensity, OriginalColor, Intensity, Color }` per mutated component, where `ComponentIndex`
  is the ordinal among that template's light components.
- `RestoreTemplateChange(saved)` resolves `ModLibrary.Get<PartTemplate>(saved.TemplateId)`, checks the
  ordinal, re-seeds the original and re-applies the saved values through the normal writers.
- `ResetTemplateChanges()` writes every original back and clears both dictionaries; it is called from
  the scene-reset callback **before another world loads**, because the shared templates outlive the
  scene.

**Disco** (`DiscoLight.Persistence.cs`): `SavedDiscoLight` stores the `SavedPartReference`, the
recipe, paused/enabled flags, the original and owned switch state, elapsed time, random seed and
phase offsets, and for each owned actuator `{ ModuleIndex, OriginalGoal }` where `ModuleIndex` is the
index in `Part.FullPart.Modules.Get<KeyframeAnimationModule>()`. On restore the private templates are
**recreated**, never deserialized; a changed actuator layout is rejected.[^zippo-disco-light-persistence][^zippo-submod-persistence]

**What is deliberately left to the game**: "Native switches and module positions remain native save
data."[^zippo-lib-readme] (`PowerConsumer` saves the switch; `KeyframeAnimationModule` saves goal and
position.)

Standalone zippo persists nothing: template edits last until the process exits.

# Restoring originals

| What | How | When |
| --- | --- | --- |
| Per-instance template (Disco) | `if (ReferenceEquals(module.Template, owned)) module.Template = original;` | stop, target part gone, mod unload (`DiscoLight.Dispose`) |
| Actuator goal (Disco) | `if (module.TimeGoal == goal.Written) module.TimeGoal = goal.Original;` | same, and when another Disco light claims the actuator |
| Light switch (Disco) | restored only if zippo wrote it and it still holds zippo's last value | same |
| Shared template (ordinary) | `ResetTemplateChanges()` from the ledger | scene reset in the `unscience` host only |
| "Default" colour in the UI | `_originalColors[part]`, captured the first time the part is selected | user picks *Default* |

[^zippo-disco-light][^zippo-light-controller-persistence][^zippo-submod]

The recurring rule is **restore only what you still own**: every restore first checks that the
current value is the one zippo last wrote, so a competing feature's replacement template or goal is
left alone. `ZippoSubmod.Dispose()` stops Disco and clears queues but does **not** revert ordinary
shared-template writes.[^zippo-submod]

# Recorded pitfalls

Collected from zippo's sources, README, SPEC, library README, plan and the scope document.

1. **Wrong field name was a silent no-op.** zippo originally reflected a field called `"Color"` — the
   XML element name — but the C# field is `ColorRgb`; `GetField` returned null and colour changes did
   nothing for two game versions while intensity and on/off worked. String reflection fails
   silently.[^unscience-scope-lights]
2. **Clear `IndexedColor` before `OnDataLoad`.** "`ColorRgbReference.OnDataLoad` re-derives R/G/B from
   `IndexedColor` whenever it isn't Invalid, which would discard the RGB we just wrote."[^zippo-light-controller]
3. **Call `OnDataLoad(null)` after every colour write** or the renderer keeps using the old
   `Value`.[^zippo-light-controller][^zippo-disco-light]
4. **The template is shared across part instances.** Ordinary writes are global; isolation needs the
   module-local copy.[^zippo-lib-readme]
5. **A hard-coded nested type name** (`"KSA.LightModule+TemplateData"`) — "Rename/move of nested type
   silently yields zero light parts."[^unscience-scope-lights]
6. **`Part.Id` is not unique**; key by `InstanceId` or object identity.[^zippo-light-animation-manager]
7. **Lights without a switch exist**; zippo's only way to turn those off is intensity 0, which on the
   ordinary path is a shared-template write.[^zippo-submod]
8. **On/off depends on power since KSA 5402.** The consumer side became `Part.IsLightSwitchedOff()`
   (`!LightIsActive || !IsSwitchedOn()`); writing `LightIsActive` still works, but an unpowered light
   stays dark.[^unscience-scope-lights]
9. **The clone must be complete and follow game changes** (`Id` → `Key` at 5801).[^unscience-scope-lights]
10. **Mechanisms have their own speed**; fast cycles outrun them, and pausing the recipe does not stop
    a mechanism already under way.[^zippo-readme]
11. **One animation module may serve several lights**; ownership must be exclusive.[^zippo-submod-disco]
12. **Symmetry.** "Native lights now propagate switch changes across symmetry; manually owned Disco
    switches need an in-game symmetry check", and the stock mirrored fan-out of animation goals
    "still needs a live check".[^unscience-scope-lights]
13. **GUI hooks stop with the HUD hidden**; frame-driven effects pause unless another hook is
    added.[^unscience-scope-lights]
14. **Never serialize module references or private templates**; recreate them from a structural part
    address after load, and reset shared templates before the next world loads.[^zippo-submod-persistence][^zippo-light-controller-persistence]
15. **The README is not a reliable description of the code.** It shows a reflection snippet reading a
    `_lightData` field and states intensity is "0.0 - 1.0"; the code reads `PartTemplate.Components`,
    and the 0–1 range is a UI choice.[^zippo-readme][^zippo-light-controller]
16. **Not validated in the running game.** The scope document lists "Native color isolation, actuator
    selection/ownership, spotlight cone rendering, pause, external-template replacement, craft
    destruction/debris handoff, and unload restoration" as requiring "an in-game smoke pass".[^unscience-scope-lights]

# What to take from zippo for the kFS extension

- Use the **Disco strategy** (typed access, module-local template) for every appearance change; the
  ordinary strategy's shared-template writes are unsuitable for a program that addresses individual
  lights.
- Clone lazily on first write and remember the original reference so a program stop can restore it.
- On/off is `module.Parent.FullPart.LightSwitch.LightIsActive`; decide explicitly what "off" means
  for a switchless light.
- Normalised 0–1 actuator values times `Shared.Duration` is already the established convention in
  both the stock UI and zippo.
- zippo gives **no** precedent for writing any of this off the main thread.

# Modding notes

- zippo needs no Harmony patch, publicizer or private-member access for lights; its reflection is
  avoidable because every member it reaches is public in this build.
- It runs entirely on the main thread in the UI phase; it never touches `ModuleStateful` state.
- Its identity scheme is `Part.InstanceId` in-session and a structural tree path across saves.
- Its Disco restore logic ("only if still owned") is the pattern to reuse when two features — or a
  kFS program and the player's part window — can write the same field.

[^zippo-light-controller]: zippo.lib/LightController.cs — `GetLightComponents` :30–42; `HasLights` :44; `ReadIntensity` :46–53; `ReadColor` :55–65; `WriteIntensity` :67–75; `WriteColor` :77–94 (comment on `IndexedColor` :87–88); `ApplyIntensity`/`ApplyColor` :96–100; `GetLightParts` :103–104.
[^zippo-light-controller-persistence]: zippo.lib/LightController.Persistence.cs — `SavedLightTemplate` :10–18; dictionaries :22–24; `RememberOriginal` :36–40; `CaptureTemplateChanges` :42–52; `RestoreTemplateChange` :54–65; `ResetTemplateChanges` :67–80.
[^zippo-submod]: zippo.lib/ZippoSubmod.cs — `Update` :54–58; on/off :157–170; intensity drag :178–183; default colour :195–204; `GetLightPartInfos` :444–465; `SetLightState` :468–485; `ResolvePartByKey` :525–535; `PartKey` :537; `ResolvePartInVehicle` :539–544; `Dispose` :546–551; `RebuildLightParts` :587–597; `OnPartSelected` :599–621.
[^zippo-submod-disco]: zippo.lib/ZippoSubmod.Disco.cs — `_discoLights` :16; `StartDisco` :205–232; `StopDisco` :234–237; `UpdateDisco` :244–260.
[^zippo-submod-persistence]: zippo.lib/ZippoSubmod.Persistence.cs — save state types :11–30; participant :34–38; `CaptureLights` :40–61; `ResetSavedLights` :63–68; `RestoreLights` :104–146.
[^zippo-disco-light]: zippo.lib/DiscoLight.cs — fields :14–22; constructor :24–68; `SetEnabled` :80–85; `AddActuator`/`ReleaseActuator` :87–98; `Update` :100–142; `Dispose` :166–187.
[^zippo-disco-light-persistence]: zippo.lib/DiscoLight.Persistence.cs — `SavedDiscoLight` :9–23; `SavedDiscoActuator` :25–29; `CaptureSaved` :33–47; restoring constructor :49–68.
[^zippo-disco-recipe]: zippo.lib/DiscoRecipe.cs — defaults :10–25; `Validate` :46–66. zippo.lib/DiscoTiming.cs:6–45.
[^zippo-light-animation-manager]: zippo.lib/LightAnimationManager.cs — key comment :16; `Enqueue` :35–54; `Update` :62–88; `PromoteNext` :104–127. zippo.lib/LightAnimation.cs:11–68.
[^zippo-light-part-info]: zippo.lib/LightPartInfo.cs:6–13.
[^zippo-mod]: zippo/Mod.cs — `OnFullyLoaded` :22–36; `OnBeforeUi` :38–50; `OnAfterUi` :52–65; `Unload` :67–80.
[^zippo-patcher]: zippo/Patcher.cs:14–39.
[^zippo-readme]: zippo/README.md — reflection snippet :58–70; Disco behaviour :84–94; property table :103–109.
[^zippo-spec]: zippo/SPEC.md:1–32 (intensity slider :31, on/off fallback :30).
[^zippo-lib-readme]: zippo.lib/README.md — Disco capability :11–23; scene saves :35–43.
[^zippo-plan]: plans/done/ZIPPO_ANIMATIONS_PLAN.md — public API note :463; risk table :874–883.
[^unscience-scope-lights]: scope/celestial-and-lights.md — 5801 note :3–9; zippo section :275–354 (integration table :296–312, Disco table :314–324, timing :326–332, Harmony :334–336, update-risk findings :340–354); persistence :366–382.
[^unscience-part-helpers]: ksa-abstractions.lib/PartHelpers.cs:11–34; ksa-abstractions.lib/VehicleProvider.cs:22–26.
[^unscience-saved-part-reference]: ksa-abstractions.lib/Persistence/SavedPartReference.cs:7–63.
[^starmap-runtime-mod]: StarMap.Core/ModRepository/RuntimeMod.cs:192–210.
[^light-module]: LightModule.cs — `Template` :58; `CreateComponents` stores the template reference :78–91; `UpdateRenderData` :93–132.
[^color-rgb-reference]: ColorRgbReference.cs — `Value` :21–22; constructor :28–33; `OnDataLoad` :35–44.
[^part-template]: PartTemplate.cs:130–131.
