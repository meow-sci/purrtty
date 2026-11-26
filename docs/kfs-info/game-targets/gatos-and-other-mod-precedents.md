---
type: Game System Reference
title: gatOS and other unscience submods — part, module, light and animation precedents
description: How gatOS and the non-zippo unscience submods enumerate vehicle parts and modules, address them over time, schedule reads and writes relative to the physics workers, and control lights and keyframe animations, with the pitfalls their own documents record.
resource: gatos:gatOS.GameMod/Game/Ksa/Actuators/LightActuator.cs
tags: [kfs, game-targets, gatos, unscience, mod-precedent, lights, animation, threading, identity, command-queue]
sources:
  - id: gatos-light-actuator
    resource: gatos:gatOS.GameMod/Game/Ksa/Actuators/LightActuator.cs
    title: gatOS LightActuator
  - id: gatos-animation-actuator
    resource: gatos:gatOS.GameMod/Game/Ksa/Actuators/AnimationActuator.cs
    title: gatOS AnimationActuator
  - id: gatos-animation-links
    resource: gatos:gatOS.GameMod/Game/Ksa/Readers/AnimationLinks.cs
    title: gatOS AnimationLinks
  - id: gatos-vessel-reader
    resource: gatos:gatOS.GameMod/Game/Ksa/Readers/VesselReader.cs
    title: gatOS VesselReader
  - id: gatos-parts-reader
    resource: gatos:gatOS.GameMod/Game/Ksa/Readers/PartsReader.cs
    title: gatOS PartsReader
  - id: gatos-command-queue
    resource: gatos:gatOS.SimFs/Commands/CommandQueue.cs
    title: gatOS CommandQueue
  - id: gatos-sim-command
    resource: gatos:gatOS.SimFs/Commands/SimCommand.cs
    title: gatOS SimCommand (phase assignment)
  - id: gatos-mod
    resource: gatos:gatOS.GameMod/Mod.cs
    title: gatOS Mod (StarMap hooks)
  - id: gatos-mod-game
    resource: gatos:gatOS.GameMod/Game/Mod.Game.cs
    title: gatOS Mod.Game (command drains, solver hook)
  - id: gatos-agents
    resource: gatos:AGENTS.md
    title: gatOS AGENTS.md (threading rules)
  - id: gatos-spec-9p
    resource: gatos:SPEC_9P_FILESYSTEM.md
    title: gatOS 9p filesystem specification
  - id: gatos-integration-plan
    resource: gatos:KSA_GAME_INTEGRATION_PLAN.md
    title: gatOS KSA game integration plan
  - id: gatos-runtime-coupling
    resource: gatos:scope/ksa-runtime-coupling.md
    title: gatOS scope - KSA runtime coupling
  - id: gatos-light-analysis
    resource: gatos:docs/LIGHT_PART_ANALYSIS.md
    title: gatOS light part analysis
  - id: gatos-survey
    resource: gatos:gatOS.GameMod/Game/Ksa/
    title: gatOS game-integration sources (survey pass, relayed)
  - id: unscience-physics-frame-hook
    resource: unscience:ksa-abstractions.lib/PhysicsFrameHook.cs
    title: unscience PhysicsFrameHook
  - id: unscience-hidden-ui-hook
    resource: unscience:ksa-abstractions.lib/HiddenUiFrameHook.cs
    title: unscience HiddenUiFrameHook
  - id: unscience-part-helpers
    resource: unscience:ksa-abstractions.lib/PartHelpers.cs
    title: unscience PartHelpers and VehicleProvider
  - id: unscience-saved-part-reference
    resource: unscience:ksa-abstractions.lib/Persistence/SavedPartReference.cs
    title: unscience SavedPartReference and PartIdentitySnapshot
  - id: unscience-eternal-flame
    resource: unscience:eternal-flame.lib/EternalFlameLib.cs
    title: unscience eternal-flame (refill timing)
  - id: unscience-patcher
    resource: unscience:unscience/Patcher.cs
    title: unscience host Patcher (solver prefix)
  - id: unscience-mod
    resource: unscience:unscience/Mod.cs
    title: unscience host Mod (submod update loop)
  - id: unscience-shiny
    resource: unscience:its-so-shiny.lib/ShinyPixelCell.cs
    title: unscience its-so-shiny light cells and grid manager
  - id: unscience-graffiti
    resource: unscience:graffiti.lib/GraffitiSubmod.cs
    title: unscience graffiti (module identity fallback)
  - id: unscience-beverage-cooler
    resource: unscience:beverage-cooler/BeverageCooler.xml
    title: unscience beverage cooler part XML
  - id: unscience-scope-vehicle-physics
    resource: unscience:scope/vehicle-physics.md
    title: unscience scope - vehicle physics
  - id: unscience-saves-lifecycle
    resource: unscience:plans/saves-game-lifecycle.md
    title: unscience saves game-lifecycle plan
  - id: unscience-survey
    resource: unscience:REPOSITORY_INDEX.md
    title: unscience submod sources and plans (survey pass, relayed)
  - id: module-stateful
    resource: ksa-decomp:KSA/ModuleStateful.cs
    title: ModuleStateful (decompiled)
generated: { by: claude-code/claude-fable-5-2, at: 2026-11-25T21:36:15Z }
status: stable
ksa_build: "2026.11.25.5949"
---

# Scope and evidence

This document surveys two mod code bases for the patterns the kFS extension needs: **gatOS** (an
in-game Linux VM that exposes game state as a filesystem and accepts control writes) and the
**unscience** monorepo's submods other than zippo (zippo has its own
[reference](/game-targets/zippo-light-control-reference.md)).

Evidence levels used here: unlabelled statements were read in the cited file for this document.
Statements that a survey pass over the repositories had only relayed were re-opened in a review pass
and are unlabelled where the cited file confirmed them (the two "survey" footnotes list those
files); nothing here is now marked **Unverified:**. Neither
mod has been validated against build 5949 in the running game by this bundle, and gatOS's own anchor
notes for lights date from mid-2026.

# gatOS

## Lights: `LightActuator`

gatOS exposes `lights/<n>/{on,brightness,color,inner_angle,outer_angle}` per vessel and a vessel-wide
master switch. All of it is one static class of direct field writes:[^gatos-light-actuator]

| Operation | Code | Game member |
| --- | --- | --- |
| Find light *n* | `vehicle.Parts.Modules.Get<LightModule>()[ordinal]` (bounds-checked, `NotFound` otherwise) | `PartTree.Modules`, `ModuleList.Get<T>()` |
| Master on/off | `vehicle.LightsOn = on; foreach (PowerConsumer c in vehicle.Parts.Modules.Get<PowerConsumer>()) if (c.LightSwitch) c.LightIsActive = on;` | "Replicates ToggleLights() as a set" |
| One light on/off | `light.Parent.FullPart.LightSwitch.LightIsActive = on`; `Unsupported` if the part has no switch | `Part.LightSwitch` |
| Brightness | `EnsureUnshared(light); light.Template.Intensity.Value = (float)Math.Max(0, value);` | `FloatReference.Value` — no upper bound |
| Colour | `EnsureUnshared(light);` set `ColorRgb.R/G/B` clamped to 0–1; `color.OnDataLoad(null!)` | `ColorRgbReference` |
| Outer angle | degrees in; `outer = clamp(deg·π/180, 1e-5, 1.5697963)`; `OuterAngle.Value = outer`; **if `InnerAngle.Value > outer` set it to `outer`** | `Light.CreateSpotLight` clamp and swap |
| Inner angle | degrees in; `InnerAngle.Value = clamp(deg·π/180, 0, OuterAngle.Value)` | |

Per-instance isolation is done on first write:[^gatos-light-actuator]

```csharp
private static readonly ConditionalWeakTable<LightModule, object> Unshared = new();

private static void EnsureUnshared(LightModule light)
{
    if (Unshared.TryGetValue(light, out _)) return;
    var clone = ShallowClone(light.Template);
    clone.Intensity  = ShallowClone(light.Template.Intensity);
    clone.ColorRgb   = ShallowClone(light.Template.ColorRgb);
    clone.OuterAngle = ShallowClone(light.Template.OuterAngle);
    clone.InnerAngle = ShallowClone(light.Template.InnerAngle);
    light.Template = clone;
    Unshared.Add(light, Marker);
}
```

`ShallowClone<T>` uses `RuntimeHelpers.GetUninitializedObject` and copies every instance field up the
type hierarchy by reflection — constructor-agnostic, so it keeps working if `TemplateData` gains
fields (zippo's hand-written copy does not). `Range` and `Transform` stay shared (gatOS never writes
them). There is **no restore path and no persistence**: a clone lives until the module object is
collected.

Compared with zippo: gatOS clamps colour to 0–1, leaves brightness unbounded above, does **not**
reset `IndexedColor` before `OnDataLoad` (a light whose XML used a named colour would have its RGB
overwritten by the name again — no stock light uses a named colour), and guards the inner/outer swap,
which zippo's Disco handles by validating its recipe.

The swap guard exists because of a bug gatOS shipped and analysed: writing only the outer angle below
the untouched inner angle made `CreateSpotLight` exchange them, so "the beam refuses to get narrower
than ~22.5° half-angle".[^gatos-light-analysis] That analysis also establishes that the emissive lens is
unrelated to the light colour (see [LightModule](/game-targets/light-module.md)).

## Animations: `AnimationActuator`

```csharp
internal static CommandResult SetGoal(Vehicle vehicle, int ordinal, double fraction)
{
    var animations = vehicle.Parts.Modules.Get<KeyframeAnimationModule>();
    if (ordinal < 0 || ordinal >= animations.Length)
        return new CommandResult(CommandOutcome.NotFound, $"animation {ordinal} does not exist");
    var module = animations[ordinal];
    module.TimeGoal = (float)(Math.Clamp(fraction, 0, 1) * module.Shared.Duration);
    return CommandResult.Ok;
}
```
[^gatos-animation-actuator]

- One control for every `KeyframeAnimationModule`, regardless of `ShowDeployRetract`: "fraction 0 =
  retract, 1 = deploy". gatOS does not read `ShowDeployRetract` at all.
- The doc comment gives the design reason: "the sim then integrates the animation itself, so this
  behaves correctly at any time-warp".
- Its anchor note records the competing writer: `Parachute.Arm/Disarm/Deploy` call `SetBayDoorsOpen`,
  so "an animations/<n>/goal write on such a bay fights it (the next chute transition re-drives the
  doors)".
- The same animation ordinal is published under three views — `animations/<n>/goal`,
  `solar/<n>/goal` and `lights/<n>/goal`.[^gatos-spec-9p]

## Read-back

Lights, per sampler tick, uncached "because the light controls write them live":[^gatos-vessel-reader]

```csharp
var on = light.Parent.FullPart.LightSwitch?.LightIsActive ?? false;
double intensity = light.Template.Intensity.Value;
var rgb = light.Template.ColorRgb;                 // .R .G .B
// OuterAngleDeg = light.Template.OuterAngle.Value * RadToDeg, InnerAngleDeg likewise
```

Note the choice: `on` is the *commanded* switch and reads `false` for a switchless light (zippo reads
`true` there).

Animations:[^gatos-vessel-reader]

```csharp
ModuleStateful<KeyframeAnimationModule, KeyframeAnimationModule.State, EmptyStruct, EmptyStruct>
    .TryGetFrom(vehicle.Parts.States, out var states);
ref readonly var state = ref states.GetState(module);
goalFraction    = duration > 0 ? Math.Clamp(module.TimeGoal / duration, 0, 1) : 0;
currentFraction = duration > 0 ? Math.Clamp(state.TimeCurrent / duration, 0, 1) : 0;
deploymentState = state.DeploymentState;            // falls back to DeriveDeploymentState(goal, goal) with no state list
```

Structural links are cached per vehicle in `AnimationLinks` (a `ConditionalWeakTable<Vehicle, Entry>`,
rebuilt when any of the three module counts changes or every 10 simulated seconds):[^gatos-animation-links]

| Link | How it is found |
| --- | --- |
| "this animation deploys a solar panel" | `animations[i].Parent.SubtreeModules.HasAny<SolarPanel>()` |
| solar panel → its animation | `panels[i].KeyframeAnimationModule` (the game's own binding) |
| light → its aiming animation | first of `lights[i].Parent.FullPart.SubtreeModules.Get<KeyframeAnimationModule>()` |

These three expressions are a ready-made way to classify animation modules for a program-facing
descriptor table.

## Enumeration and identity

- **Parts**: `PartsReader` walks `vehicle.Parts.Parts`, with each part's sub-parts nested one level,
  recording `InstanceId`, `Id`, `DisplayName`, `Template.Id`, `PartParent is null`,
  `SubParts.Length` and position. The list is cached per vehicle and rebuilt on `Parts.Count` change
  or every 10 s because "KSA exposes no part-tree version/dirty flag".[^gatos-parts-reader]
- **Modules**: always tree-wide `vehicle.Parts.Modules.Get<T>()`; **the span index is the public
  address** (`SimCommand.Ordinal`). Every actuator re-fetches the span and bounds-checks.
- **Parts as anchors** (welds, stickers, camera targets) use `Part.InstanceId`.

gatOS's documents are explicit that these are two different grades of address:

> `<n>`/`<m>` are 0-based indexes (friendly to enumerate) but **not** stable across vehicle edits —
> `instance_id` is the stable handle a weld uses.[^gatos-spec-9p]

> module collections keep ordinal indices `<n>`, ordered by PartTree enumeration … renumbering only
> happens if the vehicle is structurally edited — documented caveat[^gatos-integration-plan]

and the plan still lists as an open question "**Module ordinal stability** (G3): freeze
ordinal-vs-InstanceId addressing rules after observing renumbering behavior across
docking/decoupling", next to "**Light brightness scale** (G3): confirm `Intensity.Value` range before
freezing `0..1`".[^gatos-integration-plan]

`PartsReader`'s anchor note adds two facts worth carrying over: since KSA 5402 `DisplayName` is the
authored template name where one exists, so it "is therefore no longer unique per instance; Id and
InstanceId are unchanged and remain the addressing keys"; and the part-failure system "can move a
Part — keeping its InstanceId — into a NEW fragment/debris Vehicle ('<id>_N') between two samples
with no command".[^gatos-parts-reader]

gatOS never uses `ModuleBase.InstanceId` (every `InstanceId` in its C# sources is a part's or
sub-part's); light and animation snapshots carry only the
ordinal; anchors that leave the vehicle degrade (weld falls back to the body frame, stickers go
dormant) rather than being re-resolved across vehicles, and that cross-vehicle re-resolution is
recorded as "a follow-up option"; the light and animation actuators have no save path, and gatOS's
sticker and paint documents call those overrides session-only ("Session-only by default").[^gatos-survey]

## Threading: snapshot out, command queue in, two drain phases

The binding rule: "**Game state is read *and mutated* only on the game thread**".[^gatos-agents]

- **Reads** — a sampler on the game thread builds an immutable snapshot and publishes it with one
  reference swap; transport threads only ever see snapshots.[^gatos-agents]
- **Writes** — `CommandQueue` is "the one-way write pipe between transport threads and the game
  thread": two `ConcurrentQueue`s (`_frame`, `_solver`), one `TaskCompletionSource` per awaited
  command, and `Drain(phase, executor, maxCommands)` which is "Game-thread only".[^gatos-command-queue]
- **Frame phase** drains from `[StarMapBeforeGui] OnBeforeUi` (a prefix on `Program.OnDrawUiFrame`,
  which "does not fire while the player has the UI hidden (F2)"), with a `[StarMapAfterOnFrame]`
  postfix on `Program.OnFrame` covering the hidden-UI frames.[^gatos-mod]
- **Solver phase** drains from a Harmony `Priority.First` prefix on
  `Universe.ExecuteNextVehicleSolvers`.[^gatos-mod-game]

Which phase a command runs in is a function of its action name. The solver set is small — refills,
teleport/impulse and the flight-computer setpoints — and the reasons are recorded:[^gatos-sim-command]

> the flight-computer setpoints … write fields that KSA's async vehicle solver *snapshots and
> restores* every frame (`FlightComputer.CopyFrom` at prepare and apply). A frame-phase write lands
> outside that capture and is overwritten by the in-flight solve — the value flashes on, then
> reverts to manual. Draining in the solver prefix (just before the prepare-capture) makes it stick.

**Lights and `animation.goal` are Frame-phase commands** (the specification's action table lists
`light.brightness`, `light.color`, `light.outer_angle`, `light.inner_angle` and `animation.goal` all
as `Frame`).[^gatos-spec-9p] That is: gatOS writes `LightIsActive`, the light template and `TimeGoal`
from the main thread **while the physics step is running**, exactly like the stock part window and
zippo, and they stick because they are plain module/template fields that the solver neither
snapshots nor commits back.

gatOS's scope notes spell out the timing that makes the solver prefix "the one provably safe window":
the vehicle job is "in flight from the tail of `PrepareFrame` until the next frame's `Wait()` — i.e.
across the whole GUI phase, which is exactly where the Frame lane drains", and they record one known
unfixed race of their own (staging calls `UpdateAfterPartTreeModification()` from the Frame
lane).[^gatos-runtime-coupling] **No race analysis exists in gatOS for the light or `TimeGoal` writes.**

## Patches and private access

Light on/off, template writes and `TimeGoal` use public members only. The single reflective piece on
this path is `ShallowClone` (needed only to be constructor-agnostic). gatOS uses no
publicizer (nothing in the repository references one); its Harmony instances are the solver prefix
(`gatos.solver`), a `GameViewport.OnFrame` prefix/postfix (`gatos.camera`), display and menu hooks,
render-side hooks (`gatos.always_render`, `gatos.iva`, `gatos.thug_life`, `gatos.stickers`) and the
inline guard — none concerns lights or animations; private `Vehicle._manualControlInputs` is reached
by reflection for throttle and RCS; and because "KSA now ships JIT-optimised, and Harmony cannot reach
callers that already inlined a target before it was patched", every lazily patched target carries a
permanent empty prefix installed at load.[^gatos-survey]

# unscience (other than zippo)

## What touches lights and animations

- **its-so-shiny** is the only non-zippo submod that controls lights. On/off is the same field write
  (`(LightPart.LightSwitch ?? LightPart.FullPart.LightSwitch).LightIsActive = enabled`, falling back
  to zippo's `ApplyIntensity(part, enabled ? onIntensity : 0f)` when there is no switch); colour and
  intensity go through zippo's `LightController.WriteColor` / `WriteIntensity` on the **shared part
  template**, deliberately, to colour a whole grid of identical light parts at once.[^unscience-shiny]
- **No non-zippo C# drives `KeyframeAnimationModule`.** The only other use is content: the beverage
  cooler part declares `<KeyframeAnimationModule Key="LidAnimation" ShowDeployRetract="false">` with
  the comment "KSA names this slider Actuate. The first GLB clip is Actuation, 0..1.2
  seconds."[^unscience-beverage-cooler] That is an independent confirmation that a mod part gets the 0–1
  actuator UI simply by omitting `ShowDeployRetract`.
- `kitten-animations` is kitten character animation, not part animation: its patch is a prefix on
  `AnimatedRenderable.UpdateAnimation`.[^unscience-survey]

## Enumeration helpers

`PartHelpers.GetAllParts(vehicle)` (all full parts and, recursively, all sub-parts) and
`VehicleProvider.GetAllVehicles(includeDebris)` / `FindVehicle(id)` (null when zero **or more than
one** vehicle has that id) are the shared entry points.[^unscience-part-helpers] Submods
otherwise use the same three module accessors as the game — tree-wide `vehicle.Parts.Modules.Get<T>()`,
per-part `part.SubtreeModules.Get<T>()` and `part.Modules.Get<T>()` — and none calls
`GetUsing<T>()` (it does not occur in the repository's C# sources).[^unscience-survey]

## Identity schemes

| Scheme | Used for | Notes |
| --- | --- | --- |
| `Part.InstanceId` (+ vehicle id), re-resolved each frame | in-session anchors (decals, cameras) | "Live references are only a per-frame convenience" (hot-pursuit)[^unscience-survey] |
| module `InstanceId`, with a structural fallback | graffiti's parachute decals: first `parachute.InstanceId == instanceId`, else `parachute.Parent.InstanceId == partInstanceId && parachute.CanopyIndex == canopyIndex` | the only place either mod keys on `ModuleBase.InstanceId`[^unscience-graffiti] |
| (saved part, index within that part's typed module span) | saving a module reference | e.g. zippo's actuator `ModuleIndex`, kitchen-sink's `SeatIndex`, humble-arteest's `ModuleIndex`[^unscience-survey] |
| `SavedPartReference` | everything persisted | vehicle id + `TreeChildren` path + `SubParts` path + template id + whole-tree topology hash; resolves to null on any mismatch[^unscience-saved-part-reference] |

The saves plan states the rules the repository adopted, each of which was checked against the
decompiled code for this bundle (see [part and module model](/game-targets/part-and-module-model.md)):[^unscience-saves-lifecycle]

> `Part.InstanceId` is runtime-only: the constructor calls `Universe.GetNextRunningId`; loading calls
> `ResetRunningId` and constructs parts anew. Numeric collisions with unrelated old parts are likely.

> Module addresses need module type/template plus occurrence index under the resolved part, with
> strict validation.

> `Part.Id` alone is unsafe: native template instances can share IDs.

A consequence of the topology hash: any staging, docking or added part invalidates every saved
reference into that vehicle. That is acceptable for save files; it is the wrong tool for a running
program.

## Threading: three seams

unscience runs everything on the main thread and distinguishes three moments:

1. **UI tick** — `[StarMapBeforeGui]` → every submod's `Update(dt)`; `[StarMapAfterGui]` → UI.[^unscience-mod]
   Because both StarMap hooks sit inside `if (Program.DrawUI)`, a `HiddenUiFrameHook` prefixes the
   private `Program.OnDrawUiConsole(double)` (called unconditionally in the same phase) and replays
   the callbacks only while the HUD is hidden.[^unscience-hidden-ui-hook] This is where zippo and
   its-so-shiny write light fields.
2. **Physics handoff** — `PhysicsFrameHook`: "Runs queued vessel edits and registered callbacks after
   result application and before any next-step physics snapshots." It is a transpiler on the private
   `Program.PrepareFrame(double, double)` that replaces the `Universe.GetJobSimStep` call with its
   own `GetStepAndDispatch`, which drains a `Queue<Action>` (only the actions present at entry;
   later ones wait a frame; each wrapped in try/catch) and then raises a `BeforePhysics` event. API:
   `Enqueue(Action)`, `BeforePhysics`, `JoinOrbitReaders()`.[^unscience-physics-frame-hook] The queue has
   no lock — it is main-thread only by convention.
3. **Solver prefix** — a `Priority.First` Harmony prefix on `Universe.ExecuteNextVehicleSolvers`
   (the same seam gatOS uses), used by eternal-flame.[^unscience-patcher]

Seams 2 and 3 are both inside the solver-idle window described in
[stock control paths](/game-targets/stock-control-paths.md), a few statements apart.

Why the distinction matters is recorded where it bit:[^unscience-eternal-flame][^unscience-scope-vehicle-physics]

> Fuel and battery charge are module state that the vehicle worker snapshots when it starts and
> commits back after its step. A refill made later in the frame (for example from the UI update) is
> overwritten by that commit while engines burn, so both refills run here: after the previous results
> are applied and before the next snapshot.

That matches the decompiled mechanism: a `StateUpdater` copies the committed states on first write
and `UpdateBulkStates` copies the whole buffer back over the committed list.[^module-stateful] **The rule
for the kFS extension: commands that are module *fields* (`LightIsActive`, `TimeGoal`, a replaced
`LightModule.Template`) may be written at any seam and stick; anything that is `ModuleStateful`
*state* (`KeyframeAnimationModule.State`, `PowerConsumerState`) must not be written from the UI tick
— and for lights and animations never needs to be written at all.**

Worker-thread Harmony patches in unscience follow "Do not mutate the module: this runs
on physics workers" and read an immutable published snapshot instead (iron-man); iron-man also joins
the solver explicitly (`JobSystems.VehicleSolver.Wait()`) before touching modules outside the handoff;
blinky and its-so-shiny rebuild a vehicle's part tree from inside an ImGui button handler, and
blinky's builder records the worker crash that an incomplete follow-up call caused.[^unscience-survey]

## Departed parts

The submods that keep per-part state treat a part that has left the vehicle as not
theirs any more — "A staged/destroyed part belongs to a different tree; never dereference its
modules" (godzilla), "Detached/destroyed parts may now belong to another vehicle and must not be
mutated by this weld" (garrys-torch). The scope notes add that `Part.ResetModuleProperties()` "now
nulls `LightSwitch`" and that its-so-shiny reads the switch "live every frame, so no invalidation is
needed" — always read it live, never cache.[^unscience-survey] In this build
`Part.ResetModuleProperties` calls `Part.ResolveLightSwitch` (`Part.cs:2125`), which reassigns the
field (`Part.cs:2082–2108`).

# Side-by-side

| Question | gatOS | unscience (zippo, its-so-shiny, shared libs) | Stock kFS (for comparison) |
| --- | --- | --- | --- |
| Enumerate lights | `vehicle.Parts.Modules.Get<LightModule>()` | parts whose own template has `<Light>` (recursive part walk); Disco: `part.Modules.Get<LightModule>()` | n/a |
| Address a light | ordinal in that span | live `Part` / `Part.InstanceId`; saves: tree path | n/a |
| Address an animation | ordinal in `Get<KeyframeAnimationModule>()` | the module object; saves: index within the full part | n/a |
| Address engines etc. | ordinal | live references | array sorted by module `InstanceId`, frozen at program start, departed elements flagged |
| On/off | `FullPart.LightSwitch.LightIsActive`; switchless → unsupported | same field; switchless → intensity 0 | n/a |
| Appearance | per-instance clone (reflection `ShallowClone`), never restored | shared-template writes (ordinary) or hand-built per-instance copy restored on stop (Disco) | n/a |
| Angle units at the API | degrees | degrees | — |
| Actuator units at the API | fraction 0–1 × `Shared.Duration` | fraction 0–1 × `Shared.Duration` | — |
| Thread for light/animation writes | main, UI phase (step running) | main, UI phase (step running) | physics step for actuator state; main thread, solver-idle, for everything else |
| Solver-idle seam | prefix on `Universe.ExecuteNextVehicleSolvers` | same prefix; transpiled `Program.PrepareFrame` | `KfsIntegration.BeforeInputEvents()` |
| Persistence of overrides | none | sidecar save (unscience host) | — |

# What to take from these precedents

1. **The mechanisms are settled.** Two independent mods converged on the same three writes —
   `LightIsActive`, a per-instance `LightModule.Template`, and `TimeGoal = fraction × Duration` —
   using only public members. Nobody patches the game to control lights or animations.
2. **Ordinals are convenient and unstable.** gatOS's own documents call the ordinal a caveat and the
   instance id the stable handle. The `KfsControllerSet` approach (sort by `InstanceId` once, freeze,
   flag departures) gives programs gatOS's convenience without the renumbering.
3. **Classify with the game's own links** (`SolarPanel.KeyframeAnimationModule`,
   `SubtreeModules.HasAny<…>()`, `PartLookup.ContainsKey(subPart.Id)`) rather than by name.
4. **Guard the inner/outer swap** and clamp the outer angle to the renderer's band; keep the API in
   whatever unit is chosen but store radians.
5. **Decide the switchless-light policy** explicitly: gatOS says unsupported, zippo fakes it with
   intensity.
6. **Know which seam a write needs.** Field commands stick from anywhere; state does not. Neither mod
   provides a precedent for writing light or animation fields from a worker thread — the precedent
   for that is the stock game itself (see [stock control paths](/game-targets/stock-control-paths.md)).
7. **Per-instance clones need an owner.** gatOS never restores; zippo restores only what it still
   owns. A kFS program that stops, faults or loses the part should have a defined outcome.

# Modding notes

- Both mods demonstrate that no publicizer, reflection or Harmony patch is required for the four
  light operations and the two animation operations.
- Both run their light and animation writes on the main thread during the UI phase; both added a
  second, solver-idle hook for writes that the solver would otherwise overwrite.
- GUI-phase StarMap hooks do not fire with the HUD hidden; both mods carry a workaround. A queue
  drained from the solver-idle window (as stock kFS does in `KfsIntegration.BeforeInputEvents`) does
  not have that problem.
- If Harmony is used on game methods that may already have been JIT-inlined into their callers,
  gatOS's experience says to install patches at load.

[^gatos-light-actuator]: gatOS.GameMod/Game/Ksa/Actuators/LightActuator.cs — clamp constants :22–24; `Unshared` :27–28; `SetMaster` :34–41; `SetOn` :45–53; `SetBrightness` :58–65; `SetColor` :70–83; `SetOuterAngle` :92–104; `SetInnerAngle` :111–119; `GetLight` :121–125; `EnsureUnshared` :127–138; `ShallowClone` :141–150.
[^gatos-animation-actuator]: gatOS.GameMod/Game/Ksa/Actuators/AnimationActuator.cs — summary :6–12; anchor note :15–19; `SetGoal` :20–28.
[^gatos-animation-links]: gatOS.GameMod/Game/Ksa/Readers/AnimationLinks.cs — cache :24–44; `Get` :58–73; `Build` :75–101.
[^gatos-vessel-reader]: gatOS.GameMod/Game/Ksa/Readers/VesselReader.cs — `SampleLights` :818–847; `SampleAnimations` :940–978; `TryStates` :982–988.
[^gatos-parts-reader]: gatOS.GameMod/Game/Ksa/Readers/PartsReader.cs — remarks :16–22; cache :26–28; anchor note :36–38; `Sample` :39–47; `Build` :52–66.
[^gatos-command-queue]: gatOS.SimFs/Commands/CommandQueue.cs — summary :20–27; queues :30–32; `Drain` :130–138.
[^gatos-sim-command]: gatOS.SimFs/Commands/SimCommand.cs — solver-phase rationale :60–71; `SolverActions` :74–84; `PhaseFor` :87–88.
[^gatos-mod]: gatOS.GameMod/Mod.cs — `OnBeforeUi` and its remarks :346–357; `OnAfterUi` :389–395; `OnAfterFrame` summary :413–420.
[^gatos-mod-game]: gatOS.GameMod/Game/Mod.Game.cs — `DrainSolverCommands` :855–862; `InstallSolverHook` :865–879.
[^gatos-agents]: AGENTS.md:564–568.
[^gatos-spec-9p]: SPEC_9P_FILESYSTEM.md — part indexes vs `instance_id` :520–523; action table rows for lights and `animation.goal` :1478–1482.
[^gatos-integration-plan]: KSA_GAME_INTEGRATION_PLAN.md — ordinal caveat :329–331; open questions :623–625.
[^gatos-runtime-coupling]: scope/ksa-runtime-coupling.md:220–238.
[^gatos-light-analysis]: docs/LIGHT_PART_ANALYSIS.md — TL;DR :11–55; emissive lens :472–594.
[^gatos-survey]: Survey pass, re-opened in review (paths under gatOS.GameMod/Game/ unless rooted): Ksa/Welds/WeldManager.cs:125–128, :193; plans/STICKERS_PLAN.md:57, :427; plans/PAINT_ASBUILT.md:34; scope/ksa-read-surface.md:516–520; Ksa/Readers/VesselReader.cs:838–844, :974–975 (snapshot fields); Ksa/Actuators/ThrottleActuator.cs:17–18, TranslateActuator.cs:22, RotateActuator.cs:25; `new Harmony(…)` sites Mod.Game.cs:604, :797, :869, :912, Ksa/InlineGuard.cs:72, Ksa/Render/VesselForceRender.cs:142, Ksa/Render/IvaForceRender.cs:83, Ksa/ThugLife/ThugLifeManager.cs:301, Ksa/Paint/Stickers/StickerManager.cs:552; Ksa/Camera/CameraViewportPatch.cs:38; AGENTS.md:654–663; Ksa/InlineGuard.cs:115–134.
[^unscience-physics-frame-hook]: ksa-abstractions.lib/PhysicsFrameHook.cs — summary :10; queue and API :13–46; `PrepareFrameMethod` :112–114; operand swap :163; `GetStepAndDispatch` :167–201.
[^unscience-hidden-ui-hook]: ksa-abstractions.lib/HiddenUiFrameHook.cs:10–28.
[^unscience-part-helpers]: ksa-abstractions.lib/PartHelpers.cs:11–34; ksa-abstractions.lib/VehicleProvider.cs:22–39.
[^unscience-saved-part-reference]: ksa-abstractions.lib/Persistence/SavedPartReference.cs:7–63; ksa-abstractions.lib/Persistence/PartIdentitySnapshot.cs:16–49.
[^unscience-eternal-flame]: eternal-flame.lib/EternalFlameLib.cs:48–56.
[^unscience-patcher]: unscience/Patcher.cs:189–202.
[^unscience-mod]: unscience/Mod.cs:155–172.
[^unscience-shiny]: its-so-shiny.lib/ShinyPixelCell.cs:22–32; its-so-shiny.lib/ShinyGridManager.cs:219–227.
[^unscience-graffiti]: graffiti.lib/GraffitiSubmod.cs:262–272.
[^unscience-beverage-cooler]: beverage-cooler/BeverageCooler.xml:88–89.
[^unscience-scope-vehicle-physics]: scope/vehicle-physics.md:149–154.
[^unscience-saves-lifecycle]: plans/saves-game-lifecycle.md:122–142.
[^unscience-survey]: Survey pass, re-opened in review: kitten-animations.lib/KittenAnimationPatches.cs:30–40; hot-pursuit.lib/HotPursuitCamera.cs:15–19; kitchen-sink.lib/IvaCameraUnlock.Persistence.cs:31–32; humble-arteest.lib/HumbleArteestSubmod.Persistence.cs:54–63; godzilla.lib/VesselScaleSnapshot.cs:120–121; garrys-torch.lib/WeldScaleSnapshot.cs:66–67; scope/pixel-grids-and-render.md:493; scope/part-editor-and-robotics.md:485; iron-man.lib/IronManRcsOrientationPatches.cs:66–68; iron-man.lib/IronManSubmod.cs:24–27 (published snapshot), :144–146; blinky.lib/BlinkySubmod.cs:292–293, blinky.lib/LcdGridBuilder.cs:150–164; its-so-shiny.lib/ItsSoShinySubmod.cs:172, :358.
[^module-stateful]: ModuleStateful.cs — `UpdateBulkStates` :388–417; `GetNewStates` :688–701; `Prepare` :780–790.
