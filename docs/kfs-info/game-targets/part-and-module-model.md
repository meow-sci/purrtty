---
type: Game System Reference
title: Vehicle, part tree, parts, sub-parts and modules
description: How a KSA vehicle is composed of a PartTree of Part objects carrying typed modules, how to enumerate and identify them, how ModuleStateful state buffers work, and what staging, docking, save/load and scene changes do to those objects.
resource: ksa-decomp:KSA/PartTree.cs
tags: [kfs, game-targets, parts, modules, part-tree, instance-id, module-stateful, threading]
sources:
  - id: part
    resource: ksa-decomp:KSA/Part.cs
    title: Part (decompiled)
  - id: part-tree
    resource: ksa-decomp:KSA/PartTree.cs
    title: PartTree (decompiled)
  - id: part-template
    resource: ksa-decomp:KSA/PartTemplate.cs
    title: PartTemplate (decompiled)
  - id: sub-part-template
    resource: ksa-decomp:KSA/SubPartTemplate.cs
    title: SubPartTemplate (decompiled)
  - id: part-instance
    resource: ksa-decomp:KSA/PartInstance.cs
    title: PartInstance, the save-file form of a part (decompiled)
  - id: module-base
    resource: ksa-decomp:KSA/ModuleBase.cs
    title: ModuleBase (decompiled)
  - id: module
    resource: ksa-decomp:KSA/Module.cs
    title: Module<TModule> and its List (decompiled)
  - id: module-list
    resource: ksa-decomp:KSA/ModuleList.cs
    title: ModuleList (decompiled)
  - id: module-stateful
    resource: ksa-decomp:KSA/ModuleStateful.cs
    title: ModuleStateful, StateList and StateUpdater (decompiled)
  - id: module-state-list
    resource: ksa-decomp:KSA/ModuleStateList.cs
    title: ModuleStateList (decompiled)
  - id: module-state-updaters
    resource: ksa-decomp:KSA/ModuleStateUpdaters.cs
    title: ModuleStateUpdaters (decompiled)
  - id: vehicle-update-data
    resource: ksa-decomp:KSA/VehicleUpdateData.cs
    title: VehicleUpdateData (decompiled)
  - id: vehicle-update-state
    resource: ksa-decomp:KSA/VehicleUpdateState.cs
    title: VehicleUpdateState (decompiled)
  - id: vehicle
    resource: ksa-decomp:KSA/Vehicle.cs
    title: Vehicle (decompiled)
  - id: universe
    resource: ksa-decomp:KSA/Universe.cs
    title: Universe (decompiled)
  - id: program
    resource: ksa-decomp:KSA/Program.cs
    title: Program, the frame loop (decompiled)
  - id: physics-bubble
    resource: ksa-decomp:KSA/PhysicsBubble.cs
    title: PhysicsBubble (decompiled)
  - id: input-events
    resource: ksa-decomp:KSA/InputEvents.cs
    title: InputEvents (decompiled)
  - id: decoupler
    resource: ksa-decomp:KSA/Decoupler.cs
    title: Decoupler (decompiled)
  - id: kfs-controller-set
    resource: ksa-decomp:KSA.Kfs/KfsControllerSet.cs
    title: KfsControllerSet (decompiled)
  - id: core-electrical-assets
    resource: ksa-content:Core/CoreElectricalAAssets.xml
    title: CoreElectricalA part and sub-part layout
  - id: unscience-saved-part-reference
    resource: unscience:ksa-abstractions.lib/Persistence/SavedPartReference.cs
    title: unscience SavedPartReference (save-stable part address)
generated: { by: claude-code/claude-fable-5-2, at: 2026-11-25T21:36:15Z }
status: stable
ksa_build: "2026.11.25.5949"
---

# Purpose

Everything a kFS extension does to a light or an animation starts with finding the right module
object on the right vehicle and being able to find it again next tick. This document describes the
object model that makes that possible: `Vehicle` → `PartTree` → `Part` (and sub-`Part`s) → modules,
the two id spaces, the state-buffer pattern (`ModuleStateful`), and what happens to these objects
when the vehicle stages, docks, is saved or the scene changes.

The specific modules are documented in [LightModule](/game-targets/light-module.md) and
[KeyframeAnimationModule](/game-targets/keyframe-animation-module.md).

# Object model at a glance

```text
Vehicle                         KSA.Vehicle             one per craft / debris / EVA kitten
 └─ Parts : PartTree            KSA.PartTree            one tree per vehicle (OwningVehicle back-ref)
     ├─ Root : Part
     ├─ Parts : ReadOnlySpan<Part>      every FULL part of the tree (flat list, not sub-parts)
     ├─ Modules : ModuleList            every module of every part AND sub-part in the tree
     ├─ States  : ModuleStateList       per-type state buffers for the stateful modules
     └─ PowerConsumers, Batteries, SolarPanels, ... : hot-path StateList shortcuts
Part (full part)                KSA.Part                Template : PartTemplate
 ├─ TreeParent / TreeChildren                           structural tree links between full parts
 ├─ SubParts : ReadOnlySpan<Part>                       sub-parts are ALSO Part objects
 ├─ Modules : ModuleList                                modules declared on this part's own template
 ├─ SubtreeModules : ModuleList                         own modules + all sub-parts' modules
 └─ LightSwitch : PowerConsumer?                        resolved light switch (see light-module.md)
Part (sub-part)                 KSA.Part                Template : SubPartTemplate, PartParent != null
 └─ Modules : ModuleList                                modules declared on the sub-part template
ModuleBase → Module<T> → ModuleStateful<T, TState, TGlobalState, TFxState>
 ├─ InstanceId : ulong (readonly)    TemplateId : string    Parent : Part
 └─ (stateful only) StatesIdx : int  index into the tree's StateList for that module type
```

There is **no runtime `SubPart` class**. A sub-part is a `Part` whose `PartParent` is set and whose
`Template` is a `SubPartTemplate` (a `PartTemplate` subclass that only sets `IsSubPart = true`).[^sub-part-template]
`Part.IsSubPart => PartParent != null` and `Part.FullPart => PartParent ?? this`.[^part]

# Vehicle and PartTree

`Vehicle.Parts` is a property over `_parts`; its setter stamps `value.OwningVehicle = this`.[^vehicle]
So from any part or module you can get back to the vehicle with
`module.Parent.FullPart.Tree?.OwningVehicle`.

`PartTree` members that matter here:[^part-tree]

| Member | Type | Notes |
| --- | --- | --- |
| `Root` | `Part` | root full part |
| `Parts` | `ReadOnlySpan<Part>` | span over the private `List<Part> _parts`; **full parts only** — sub-parts are reached through `part.SubParts` |
| `Count` | `int` | `Parts.Length` |
| `Contains(Part)` | `bool` | linear `_parts.Contains` — full parts only |
| `Find(ulong id)` | `Part?` | linear scan for `part.InstanceId == id` over full parts |
| `OwningVehicle` | `Vehicle?` (public field) | set by `Vehicle.Parts` setter |
| `Modules` | `ModuleList` (public field) | tree-wide module index, includes sub-part modules |
| `States` | `ModuleStateList` (public field) | committed state buffers; **replaced by a new object** in `ClearAllDerivedValues` |
| `PowerConsumers`, `Batteries`, `SolarPanels`, `Gimbals`, `RocketCores`, `RocketNozzles`, `Moles`, `Generators`, `SolarTrackers` | `ModuleStateful<…>.StateList` | "hot path" state lists pre-created for these types |
| `Tanks`, `Segments`, `DockingPorts`, `Decouplers`, `EVADoors`, `GrabRails`, `Controls` | `Module<T>.List` | hot-path module lists |
| `SequenceList`, `ResourceGroupList`, `FuelLinks`, `PerformanceSequences` | | staging / sequences / resources |
| `RenderData` | `PartTreeRenderData` | cached per-tree render batches |
| `IsDisposed` | `bool` | set by `Dispose()` |

`KeyframeAnimationModule` and `LightModule` have **no** hot-path shortcut field; reach them with
`tree.Modules.Get<T>()` and (for animation state) `tree.States.TryGetTypeList(...)`.

## Structure lock — not active in this build

`PartTree` declares a static `ReaderWriterLockSlim _structureLock` with `ReadStructure()`,
`TryEnterWriteStructure()`, `ExitWriteStructure()`, `StampMainThread()` and a private
`AssertStructureWritable()` that throws when a tree is restructured off the main thread without the
write lock.[^part-tree] **Not active in this build:** nothing in the decompiled assemblies calls
`ReadStructure`, `TryEnterWriteStructure` or `StampMainThread`, so `_mainThreadId` stays `-1` and
`AssertStructureWritable()` always returns immediately. Do not rely on the lock for safety, and do not
be surprised if a later build starts enforcing it — structural changes belong on the main thread (see
[Threading](#threading-who-touches-what-and-when)).

# Part

`Part` is one class for both full parts and sub-parts.[^part] Identity and naming members:

| Member | Type / visibility | Meaning |
| --- | --- | --- |
| `InstanceId` | `public uint` field | assigned once in the constructor from `Universe.GetNextRunningId()` |
| `Template` | `public PartTemplate` field | shared, immutable-by-convention definition (see below) |
| `Id` | `public string { get; init; }` | `ResolveRuntimeId(inName, Template)`: the instance name if one was passed, else `Template.Id`. For sub-parts created from a template it is the placement **`Key`** (e.g. `CoreElectricalA_Subpart_SpotlightA1`). **Not unique** — every instance of a part template loaded from a save has `Id == Template.Id` |
| `DisplayName` | `public string { get; init; }` | `Template.DisplayName` when that differs from `Template.Id`, otherwise `Id` |
| `PartParent` | `public Part?` field | non-null for sub-parts |
| `FullPart` | `Part` property | `PartParent ?? this` |
| `StructuralPart` | `Part` property | walks up through `IsAttachedInternal` parents (IVA interiors) |
| `Tree` | `public PartTree?` field | owning tree; kept in sync for sub-parts too |
| `TreeParent`, `TreeChildren` | `Part?`, `List<Part>` | links between full parts |
| `SubParts` | `ReadOnlySpan<Part>` | empty span when the part has none |
| `Modules` | `public ModuleList` field | this part's own modules |
| `SubtreeModules` | `ModuleList` property | `_subtreeModules ?? Modules`: own modules plus every sub-part's |
| `SymmetryLink` | `SymmetryLinkData?` | mirrored-part groups (restored when a tree is deserialized, so present in flight) |
| `LightSwitch` | `public PowerConsumer?` field | see [LightModule](/game-targets/light-module.md) |
| `Stage`, `SequenceGroup`, `SequenceOrder`, `IsSequenceable`, `PrimarySequenceModule` | | staging metadata |
| `PositionParentAsmb`, `Asmb2ParentAsmb`, `Scale` | properties | local pose; setters call `ResetCachedPosMatrixValues()` |
| `PositionParentAsmbSafe`, `Asmb2ParentAsmbSafe` | properties | rest pose captured at construction (used by animations) |

Human-readable names available for a part, in decreasing usefulness:

1. `part.DisplayName` — e.g. `Mk1 Command Pod`, `Mk1 Cargo Bay (Double)`, `Parachute Bay`. Many stock
   parts set no `DisplayName`, in which case it equals the template id
   (`PartTemplate.OnDataLoad` copies `Id` into an empty `DisplayName`).[^part-template]
2. `part.Template.Id` — e.g. `CoreElectricalA_Prefab_LightSmallA`.
3. `part.Template.EditorTags` (`List<EditorTag>`, each with a `Tag` string such as `Lights`,
   `Electrical`, `Landing`, `Cargo`) — resolved from the XML `<EditorTags>` list.[^part-template]
4. `module.TemplateId` — the component `Key` (e.g. `LandingLegAnimation`, `SolarPanelAnimation`,
   `RightWindow`); defaults to the template class's XML type name (`Light`, `PowerConsumer`,
   `KeyframeAnimationModule`) when the XML gives no `Key`.[^module-base]

## Construction order (matters for `InstanceId` ordering)

The private `Part` constructor runs, in order: allocate `Modules`; `InstanceId =
Universe.GetNextRunningId()`; set `Id`/`DisplayName`; **construct every template sub-part** (each is a
full `Part` construction, including its own id and its own modules); build connectors; then
`ModuleList.CreateModules(this, template, placement, parent)` for the part's own components.[^part]
Consequences:

- a part's `InstanceId` is lower than its sub-parts', and
- sub-part modules get **lower** `InstanceId`s than the parent part's own modules.

`ModuleList.CreateModules` calls each module type's static `CreateComponents` in a fixed order;
`LightModule` and `KeyframeAnimationModule` are created after `PowerConsumer`.[^module-list]

A tree loaded from a save is built by `Part.RegenerateFromPartInstance`, depth-first, parent before
its tree children, in saved child order.[^part] Module and part ids therefore follow a deterministic
pre-order of the saved tree.

# Part templates versus instances

`PartTemplate : SerializedId` is the XML-loaded definition shared by every instance:[^part-template]

| Field | Meaning |
| --- | --- |
| `Id` (from `SerializedId`) | library id, e.g. `CoreLandingA_Prefab_MediumLandingLegA` |
| `DisplayName` | `[XmlAttribute]`, optional |
| `SubPartInstances` | `List<TransformDataReference<SubPartTemplate>>` — the `<SubParts><SubPart Key=… Ref=…>` placements |
| `Components` | **public** `List<ModuleBase.TemplateDataBase>` — the `<Components>` list; every module type scans it in its `CreateComponents` |
| `Connectors`, `EditorTagRefs`/`EditorTags`, `InertMasses`, `Rockets`, … | other definition data |
| `IsSubPart` | `[XmlIgnore] bool`, set by the `SubPartTemplate` constructor |

Each module's `CreateComponents(part, template, instance)` walks `template.Components`, and for every
entry of its own `TemplateData` type creates one module instance with `Parent = part`.[^module-list]
Whether the module then **copies** values out of the template or **keeps a reference** to the shared
template object differs per module and is the single most important fact for light control:

- `PowerConsumer.CreateComponents` copies `LightSwitch`, `LightIsActive`, … into instance fields.
- `KeyframeAnimationModule.CreateComponents` copies `ShowDeployRetract` and takes the shared
  `KeyframeAnimationData` (read-only animation curves).
- `LightModule.CreateComponents` stores **the template object itself** in `LightModule.Template`; all
  lights of the same part type share one `TemplateData` until someone replaces the reference. See
  [LightModule](/game-targets/light-module.md).

Real layout example — the small spotlight part is a full part with four sub-parts; the `<Light>`
component lives on the sub-part template `CoreElectricalA_Subpart_SpotlightA`, while the power switch
and the tilt animation live on the full part:[^core-electrical-assets]

```xml
<Part Id="CoreElectricalA_Prefab_LightSmallA">
  <SubParts>
    <SubPart Key="CoreElectricalA_Subpart_SpotlightAHinge1" Ref="CoreElectricalA_Subpart_SpotlightAHinge"> … </SubPart>
    <SubPart Key="CoreElectricalA_Subpart_SpotlightA1" Ref="CoreElectricalA_Subpart_SpotlightA" />
    <SubPart Key="CoreElectricalA_Subpart_LightMountA1" Ref="CoreElectricalA_Subpart_LightMountA"> … </SubPart>
    <SubPart Key="CoreElectricalA_Subpart_SpotlightAHinge2" Ref="CoreElectricalA_Subpart_SpotlightAHinge"> … </SubPart>
  </SubParts>
  …
</Part>
```

# Modules

## Class hierarchy

- `ModuleBase : IPartParent` — `public readonly ulong InstanceId`, `public string TemplateId`,
  `public required Part Parent { get; set; }`, `virtual bool DrawsUi`, `virtual GetSaveData(ref uint
  localRunningId, ModuleStateList states)`, `virtual ApplySaveData(SaveDataBase, ModuleStateList)`,
  `virtual OnDrawUi(...)`, `virtual OnFullPartCreated()`. Nested abstract `TemplateDataBase :
  KeyedTemplate` (XML `Key` attribute, default key = XML type name) and `SaveDataBase` (`InstanceOf`,
  `LocalInstanceId`).[^module-base]
- `Module<TModule> : ModuleBase` — adds the nested `List` type, `AddNewTo(Part)` / `RemoveFrom(Part)`
  (which keep `part.Modules`, `part.FullPart.OwnSubtreeModules` and `part.Tree.Modules` in sync) and
  `virtual Dispose()`.[^module]
- `ModuleStateful<TModule, TState, TGlobalState, TFxState> : Module<TModule>` — adds `public int
  StatesIdx` and the `StateList` / `StateUpdater` machinery below.[^module-stateful]

`LightModule` is a plain `Module<LightModule>` (no state buffer). `KeyframeAnimationModule` and
`PowerConsumer` are `ModuleStateful`.

## Enumerating: `ModuleList.Get<T>()`

`ModuleList` exists at three levels with the same API: `part.Modules`, `part.SubtreeModules`,
`tree.Modules`.[^module-list]

| Call | Returns | Notes |
| --- | --- | --- |
| `Get<TModule>()` | `Span<TModule>` | exact-type lookup (`typeof(TModule)` key); a **live span over the list's backing array** — empty span when there are none. Do not hold it across a structural change |
| `GetUsing<TInterface>()` | `ModuleEnumerator<TInterface>` (ref struct) | enumerates every module assignable to an interface/base (e.g. `ISequenced`, `ModuleBase`) |
| `HasAny<TModule>()` | `bool` | |
| `TryGetById<TModule>(ulong instanceId, out TModule)` | `bool` | by module `InstanceId` |
| `GetByTemplateId<TModule>(string templateId)` | `TModule?` | only on lists created with `keepModuleIdIndex: true` (per-part lists); logs an error and returns null if two different module types share the key |
| `TryGetTypeList<TModule>(out Module<TModule>.List)` | `bool` | the underlying per-type list (`Modules`, `Count`, `this[int]`, `TryGetById`, `TryGetIdxById`) |

So the three useful enumeration idioms are:

```csharp
Span<LightModule> all      = vehicle.Parts.Modules.Get<LightModule>();           // whole vehicle
Span<LightModule> onPart   = part.Modules.Get<LightModule>();                    // this (sub-)part only
Span<KeyframeAnimationModule> forAssembly = part.FullPart.SubtreeModules.Get<KeyframeAnimationModule>();
```

Small per-part lists are stored in a "compact" form (an array of up to 8 `ModuleBase`); this is
transparent to `Get<T>()` but means stateful modules force the full dictionary form.[^module-list]

### Order of `tree.Modules.Get<T>()`

The tree-level list is filled part by part: each full part's own modules, then each of its
sub-parts' modules, in `tree.Parts` order (`TransferPart`, `AddModulesAndStaticParts`).[^part-tree]
Within one `Module<T>.List`, modules are grouped into contiguous segments per concrete class;
`LightModule` and `KeyframeAnimationModule` have a single concrete class each, so their order is
insertion order.[^module] Removal behaves differently per path:

- `RemoveFrom(otherList)` / `RemoveTransferredOut(tree)` (used when a subtree leaves the tree) compact
  the array **preserving relative order**, so every later index shifts down.
- `Remove(module)` (single module) moves the **last** element of the segment into the hole.

**An index into `tree.Modules.Get<T>()` is therefore not a stable address** across staging or
undocking. gatOS addresses lights and animations by exactly this ordinal and accepts that the numbers
shift (see [gatOS and other precedents](/game-targets/gatos-and-other-mod-precedents.md)); stock kFS
avoids it by freezing an `InstanceId`-sorted array (below).

# Identity: `InstanceId`

There are two different `InstanceId`s, both drawn from one process-wide counter:[^universe]

```csharp
private static uint _runningId = 1u;
public static uint GetNextRunningId() => Interlocked.Increment(ref _runningId);
private static void ResetRunningId() => Interlocked.Exchange(ref _runningId, 1u);
```

| | `Part.InstanceId` | `ModuleBase.InstanceId` |
| --- | --- | --- |
| Type | `uint`, public mutable field | `ulong`, public `readonly` field (value always fits in 32 bits because the counter is a `uint`) |
| Assigned | once, in the `Part` constructor | once, in the `ModuleBase` constructor |
| Unique | across all parts **and** modules of the session (shared counter) | same |
| Stable across frames | yes — never reassigned | yes |
| Stable across staging / undocking / docking | **yes** — the same `Part` and module objects are moved between trees (see below) | **yes** |
| Stable across save → load | **no** — not serialized; `PartInstance.GlobalInstanceId` is `[XmlIgnore]`, saves carry only `InstanceOf` (template id / module key) and a per-file `LocalInstanceId` | **no** |
| Stable across system load | **no** — `Universe.DeserializeSave` calls `ResetRunningId()` and all vehicles are rebuilt | **no** |

Stock precedents for using them:

- `KfsControllerSet` builds `vehicle.Parts.Modules.Get<ThrusterController>().ToArray()` ordered by
  `InstanceId`, keeps the arrays for the life of the program, and `MarkDeparted(vehicle)` flags
  elements that are no longer in the vehicle's current `Get<T>()` set as not "in".[^kfs-controller-set]
- `StateList.Add(module, oldStates)` uses the module `InstanceId` to carry a module's state from the
  old tree's buffers into the new tree's buffers when parts move.[^module-stateful]
- `Part.ImGuiContext` and the part window id embed `InstanceId` to keep ImGui ids unique.[^part]

**Inferred:** because ids are allocated in construction order and a saved tree is rebuilt depth-first,
an `InstanceId`-sorted list of one module type has the same *relative* order after a save/load of an
unchanged vehicle. That stops being true for a vehicle assembled by docking: in-session the docked
craft's modules sort by when each craft was created, but after a reload the merged tree is rebuilt in
tree order. This rests on the constructor order above; it was not checked in the running game.

A cheap membership test for "is this module still on my vehicle" is `module.Parent.Tree ==
vehicle.Parts` — the game itself uses that comparison (`Module<T>.List.RemoveTransferredOut`,
`Vehicle.ToggleLights`, `Part.IsLightSwitchedOff`).[^module][^vehicle][^part]

## A save-stable address (mod precedent)

unscience does not persist ids at all; its `SavedPartReference` stores `VehicleId`, the list of
`TreeChildren` indices from the root (`TreePath`), the list of `SubParts` indices (`SubPartPath`),
the part's `Template.Id`, and a SHA-256 of the whole tree shape, and refuses to resolve if the
topology hash differs. Its doc comment states the reason: "Runtime InstanceId and ordinary Part.Id
are not persisted by KSA."[^unscience-saved-part-reference]

# The `ModuleStateful` state / global-state / updater pattern

A stateful module splits its data three ways:

| Where | What | Who writes it |
| --- | --- | --- |
| Fields on the module object | configuration and **commands** (e.g. `KeyframeAnimationModule.TimeGoal`, `PowerConsumer.LightIsActive`, `PowerConsumer.Mode`) | UI on the main thread, occasionally module code on the worker |
| `TState` (unmanaged struct, one per module) in `tree.States` | simulation state produced by the physics step (e.g. `KeyframeAnimationModule.State { TimeCurrent, DeploymentState }`, `PowerConsumerState { Active, Consumed }`) | the worker, through a `StateUpdater` |
| `TGlobalState` (one per module type per tree) and `TFxState` (per module, FX only) | aggregates and effect state | worker / render |

## `StateList` — the committed buffer

`ModuleStateful<…>.StateList` holds a `Module<T>.List`, a `List<TState>`, a `List<TFxState>` and one
`TGlobalState`, kept index-aligned; each module's slot is `module.StatesIdx`, which the list updates
whenever it moves a module (`StateSync.OnMove`).[^module-stateful] Read API:

| Member | Returns |
| --- | --- |
| `Modules` | `Span<TModule>` |
| `States` | `ReadOnlySpan<TState>` |
| `GlobalState` | `ref readonly TGlobalState` |
| `GetState(module)` / `GetStateByIdx(i)` | `ref readonly TState` |
| `GetModuleAndState(module)` | `ModuleAndStateRef { Module, ref readonly State }` |
| `ModulesAndStates` | enumerator of the above |
| `TryGetModuleAndStateById(ulong, out …)`, `TryGetById`, `TryGetIdxById` | lookup by module `InstanceId` |
| `GetModuleAndAllMutableStatesForInitialization(module)` | `ref TState` + `ref TFxState` — **direct write access to the committed buffer**; the name says when the game uses it (load, editor) |
| `GetMutableGlobalStateForInitialization()` | `ref TGlobalState` |

Get a type's list from a tree with either form:[^module-state-list]

```csharp
tree.States.TryGetTypeList<KeyframeAnimationModule, KeyframeAnimationModule.State, EmptyStruct, EmptyStruct>(out var list);
ModuleStateful<KeyframeAnimationModule, KeyframeAnimationModule.State, EmptyStruct, EmptyStruct>.TryGetFrom(tree.States, out var list);
```

`TryGetTypeList` returns `false` when the tree has no module of that type (the list is created on
first add, except for the hot-path types).

## `StateUpdater` — the worker's scratch buffer

During a physics step the worker never writes the committed buffer. `VehicleUpdateData` owns one
`ModuleStateUpdaters` built over `vehicle.Parts.States`;[^vehicle-update-data] a module type's
`UpdateModules(ref readonly ModuleUpdateContext)` asks it for a `StateUpdater`
(`context.Updaters.TryGetOrCreateTypeStateUpdater(out updater)`), and:[^module-stateful][^module-state-updaters]

- `updater.GetModulesAndNewStates()` yields `{ Module, ref TState State }` over a pooled **copy** of
  the committed states (copied lazily on first `GetNewStates()`),
- `updater.GetReadOnlyStates()` returns the new buffer if this step already wrote one, otherwise the
  committed states,
- `updater.GetModuleAndReadOnlyState(module)` always reads the **committed** buffer,
- `updater.GlobalState` is a `ref` to the new global state.

On the main thread, after the step, `Vehicle.UpdateFromTaskResultsUnsynchronized` →
`PartTree.UpdateFromTaskResults` → `ModuleStateUpdaters.UpdateBulkStates()` copies every written
buffer back into the committed `StateList` (and logs an error instead if the module count changed
underneath it).[^part-tree][^vehicle][^module-stateful]

This is the same mechanism stock kFS uses for actuators: `FlightComputerOutput` carries the gimbal,
thruster and engine `StateUpdater`s and kFS writes `GetModulesAndNewStates(...)` on them.[^vehicle-update-data]
`VehicleUpdateState.UpdateData.Updaters` is public, so code running inside the physics step can obtain
an updater for any module type the same way.

# Threading: who touches what and when

One game frame on the main thread (`Program.OnFrame` → `PrepareFrame`):[^program]

1. `JobSystems.VehicleSolver.Wait()` — the previous frame's physics step is finished.
2. `Universe.ApplyVehicleSolvers()` — results are applied (`UpdateBulkStates` etc.).
3. `Glfw.PollEvents()` — key callbacks run (`Program.OnKey`).
4. `KfsIntegration.BeforeInputEvents()` then `InputEvents.ApplyInputEvents()` — queued actions run.
   **This is the solver-idle window**: no worker is touching vehicles.
5. `PartTree.FlushDirtyDerived()`, `FlushDirtyResourceManagers()`.
6. `Universe.ExecuteNextVehicleSolvers(...)` — the next physics step is queued and **starts running
   on worker threads**.
7. ImGui UI (`OnDrawUiFrame`, viewports, part windows), then `LightSystem.ClearLights()` and
   rendering (`UpdateRenderData`) — all on the main thread, **concurrently with the physics step**.

Inside the step, `VehicleUpdateState.UpdateModules(SimStep)` runs the per-type `UpdateModules` in a
fixed order — `Gimbal`, `Tank.UpdateTransfers`, `MagneticScoop`, `LifeSupport`, `Parachute`,
`Rocket.UpdateRockets`, `FxTemperature`, `FxWetness`, `FxDeformation`, `Generator`, `SolarPanel`,
`PowerConsumer`, `SolarTracker`, `KeyframeAnimationModule`, `Mole`.[^vehicle-update-state] It is called
from `PhysicsBubble` either directly (single-vehicle bubbles) or from `UpdateModulesJob` on the
vehicle worker pool; the flight computer (and therefore `KfsDispatch.TryComputeControl`) runs earlier
in the same step from `RunFlightComputer`.[^physics-bubble] `PhysicsBubble.StepFrameTo` returns
immediately when the target time has not advanced, so **nothing here runs while the simulation is
paused**.[^physics-bubble]

What that means for a mod:

- **Module fields** such as `TimeGoal` and `LightIsActive` are plain fields with no synchronization.
  The stock UI writes them from step 7 while the worker reads them; the worker also writes
  `TimeGoal` itself in one place (see
  [stock control paths](/game-targets/stock-control-paths.md)). Single-word writes of this kind are
  what the game already does from both threads.
- **Committed state buffers** are stable for reading while the step runs (the worker writes copies);
  they change only in step 2. Reading them from the worker or the main thread is what stock code does.
- **Structural changes** (anything that reaches `PartTree.Split` / `Merge` / `TransferPart`) are done
  by the game only in step 4: a decoupler's `SetIsActive` does not split, it queues an
  `IActivateInputData` that `ApplyInputEvents` runs, and that calls `Decoupler.Decouple` →
  `Vehicle.Split`.[^decoupler][^input-events]

# What staging, docking, splitting and scene changes do

## Staging / undocking / part failure → `Vehicle.Split`

`Vehicle.Split(connection, endpoint, impulse, out …)` calls `Parts.Split(part,
recomputeDerivedData: false)`, then creates a new `Vehicle` whose root is the separated part.[^vehicle]
`PartTree.Split` → `TransferPartSubtreeTo(newTree, part)`:[^part-tree]

1. `newTree.TransferPartSubtree(part, States, appendModules: true)` — for every part of the subtree:
   add to `newTree._parts`, set `part.Tree = newTree` (and each sub-part's `Tree`), call
   `part.Modules.AddStatesInto(newTree.States, oldStates)` (state is **copied across by module
   `InstanceId`**), append the modules to `newTree.Modules`.
2. The old tree drops them: `Modules.RemoveTransferredOut(this)` / `States.RemoveTransferredOut(this)`
   (or `RemoveFrom(newTree.Modules)`), then `RemoveTransferredParts()`.
3. Both trees get `RecomputeAllDerivedData()` (deferred through `MarkDerivedDirty`).

So after staging: **the same `Part` and module objects exist, with the same `InstanceId`s, the same
field values (`TimeGoal`, `LightIsActive`, a replaced `LightModule.Template` …) and the same state
values, but `part.Tree`, `tree.OwningVehicle`, every `StatesIdx` and every index into
`tree.Modules.Get<T>()` may have changed.** `Vehicle.Split` also calls
`Part.PartitionSymmetryLinksByTree`, so mirrored groups no longer span the two vehicles.[^vehicle]

## Docking → `Vehicle.MergeFrom`

`MergeFrom(thisConnector, otherVehicle, otherConnector, out …)` re-stages the other vehicle's parts,
calls `thisConnector.ConnectAndMerge(...)` (→ `PartTree.Merge` → the same `TransferPartSubtreeTo`,
now into this tree), and finally `otherVehicle.Dispose()`.[^vehicle] The other craft's parts and
modules **join this vehicle as the same objects**; the other `Vehicle` object is dead
(`IsDisposed == true`). Their modules are appended at the end of this tree's type lists.

## Vehicle destruction

`Vehicle.Dispose` sets `IsDisposed` and calls `Parts.Dispose()`, which sets `PartTree.IsDisposed` and
disposes every part still owned by that tree (`part.Dispose()` → `SubtreeModules.Dispose()`).[^vehicle][^part-tree][^part]
`LightModule.Dispose` and `KeyframeAnimationModule.Dispose` are the empty base implementation.

## Derived-data rebuilds

`PartTree.ReinitializeDerivedValues(oldStates)` → `ClearAllDerivedValues()` creates a **new
`ModuleStateList`** and new hot-path lists, then re-adds every module and copies states from the old
list.[^part-tree] Anything cached from `tree.States` (a `StateList` reference) is stale after that; module
and part objects are unaffected.

## Save / load and system change

Saving serializes the tree to `PartInstance` records (`InstanceOf`, `LocalInstanceId`, transform,
stage, per-module `SaveDataBase` components).[^part-instance] Loading constructs **new** `Part` and
module objects (new `InstanceId`s) and applies module save data by matching module type +
`TemplateId == InstanceOf`, falling back to "the only module of that type on the part".[^module-list][^part-tree]
`ResetRunningId()` is called from `Universe.DeserializeSave`, so ids restart from the same base each
time a universe is loaded.[^universe] Nothing a mod attaches to a module object (a replaced light template, a
dictionary keyed by `InstanceId`) survives this unless the mod re-applies it.

# Modding notes

- **Public surface.** Everything needed to enumerate and address parts and modules is public:
  `Vehicle.Parts`, `PartTree.Parts/Modules/States/OwningVehicle`, `Part.SubParts/Modules/SubtreeModules/
  InstanceId/Id/DisplayName/Template/FullPart/Tree`, `ModuleBase.InstanceId/TemplateId/Parent`,
  `ModuleList.Get<T>()`, `ModuleStateList.TryGetTypeList`, `StateList.GetState`, and
  `VehicleUpdateState.UpdateData.Updaters`. No reflection or publicizer is required for reads.
- **Internal / private.** `Part.OwnSubtreeModules`, `Part.ShowPartWindow`, `ModuleList.IsCompact`,
  `PartTree.AppendModulesOfNewPart`, `BeginTransferOutBatch`, `Universe.ResetRunningId`,
  `ModuleTypeListOps`. None are needed for light or animation control.
- **Best address for "light #3" / "deployable #2" inside a running program:** mirror
  `KfsControllerSet` — snapshot `vehicle.Parts.Modules.Get<T>().ToArray()` ordered by `InstanceId`
  when the program starts, never reorder it, and mark an element inert when
  `module.Parent.Tree != vehicle.Parts`. This is stable across frames, staging and docking; indices
  never shift; and it matches how the program already addresses engines and thrusters. Parts that
  join later (docking) are simply absent until the list is rebuilt.
- **Do not persist ids.** Across save/load use something structural (tree path + template id, as
  unscience does) or re-derive the list.
- **Do not cache spans or `StateList` references** across frames; re-fetch them. Caching module and
  part object references is fine as long as you test `Parent.Tree`.
- **`Part.Id` is not unique** and `DisplayName` often equals the template id; show the user
  `DisplayName` plus an ordinal.
- **Threads.** Reads of module fields and committed state are safe from either thread in the sense
  that stock code already does them from both. Structural operations must happen in the solver-idle
  window on the main thread — the same place `KfsIntegration.BeforeInputEvents` drains kFS's queued
  work (see [dispatch and threading](/integration/dispatch-and-threading.md)).

[^part]: Part.cs — `InstanceId` :672; `Template` :674; `PartParent`/`Tree`/`TreeParent`/`TreeChildren` :704–712; `Modules` :726; `LightSwitch` :734; `Id`/`DisplayName` :750–752; `ImGuiContext` :754; pose setters :828–891; `SubParts` :1172–1182; `IsSubPart` :1184; `FullPart` :1225; `SubtreeModules` :1235; `IsLightSwitchedOff` :1551–1567; `ResolveRuntimeId` :1595–1602; private constructor :1619–1714 (`InstanceId` :1630, sub-part loop :1655–1666, `CreateModules` :1683); `LinkSubPart` :2063–2069; `RegenerateFromPartInstance` :2714–2740; `Dispose` :3863–3866.
[^part-tree]: PartTree.cs — structure lock :44–48, :250–278; fields :60–110; `Parts`/`Root` :168–170; `Contains` :245–248; `Deserialize` :384–437; `ReinitializeDerivedValues`/`ClearAllDerivedValues`/`AddModulesAndStaticParts` :586–635; `UpdateFromTaskResults` :1593–1600; `Find` :1860–1872; `Split` :1890–1904; `Merge` :1906–1916; `TransferPartSubtreeTo` :1933–1966; `TransferPart` :2039–2076; `RemoveTransferredParts` :2078–2100; `Dispose` :2131–2146.
[^part-template]: PartTemplate.cs — `DisplayName` :14–15; `SubPartInstances` :23–25; `EditorTagRefs` :39–41; `Components` :130–131; `IsSubPart` :133–134; `EditorTags` :139–140; default `DisplayName = Id` :440–443.
[^sub-part-template]: SubPartTemplate.cs:3–9.
[^part-instance]: PartInstance.cs — `GlobalInstanceId` `[XmlIgnore]` :9–10; `InstanceOf` :15–16; `LocalInstanceId` :18–19; `Components` :48. Part.cs `GetReferenceWithChildren` :2442–2480.
[^module-base]: ModuleBase.cs — `TemplateDataBase` and `DefaultKey` :11–48; `SaveDataBase` :50–59; `InstanceId` :61; `TemplateId` :63; `Parent` :73; constructors :77–87; virtuals :89–105.
[^module]: Module.cs — `List` :14–607 (segments :16–23, `Modules` :68, `RemoveFrom` :262–307, `RemoveTransferredOut` :314–359 with the `Parent.Tree != tree` test :327, `Remove` :455–493, `TryGetById` :556–565); `AddNewTo`/`RemoveFrom(Part)` :632–646; `Dispose` :648–650.
[^module-list]: ModuleList.cs — compact form :154, :269–316; `CreateModules` order :318–357; `ApplySaveData`/`FindExactModule`/`FindSoleModule` :359–455; `Get<T>` :506–517; `GetUsing<T>` :520–528; `HasAny<T>` :531–543; `TryGetById<T>` :546–569; `GetByTemplateId<T>` :572–643; `TryGetTypeList<T>` :872–881.
[^module-stateful]: ModuleStateful.cs — `StateSync` :16–37; ref structs :39–95; `StateList` fields and accessors :258–282; `Add(module, oldStates)` :301–316; `UpdateBulkStates` :388–417; lookups :419–579; `GetModuleAndAllMutableStatesForInitialization` :482–497; `StateUpdater` :582–808 (`GetReadOnlyStates` :678–686, `GetNewStates` :688–701, `GetModuleAndReadOnlyState` :745–749, `GetModulesAndNewStates` :768–778, `Prepare` :780–790); `StatesIdx` :810; statics :837–865.
[^module-state-list]: ModuleStateList.cs — `TryGetTypeList` :66–76; `AddFrom` :17–29; `RemoveTransferredOut` :49–55.
[^module-state-updaters]: ModuleStateUpdaters.cs — `UpdateBulkStates` :18–25; `TryGetOrCreateTypeStateUpdater` :59–72.
[^vehicle-update-data]: VehicleUpdateData.cs — `Updaters` :52; hot-path updaters :54–64; construction over `vehicle.Parts.States` :73–80; `Prepare` :103. FlightComputerOutput.cs:5–23.
[^vehicle-update-state]: VehicleUpdateState.cs — `UpdateModules` :387–419; `UpdateData` field :66.
[^vehicle]: Vehicle.cs — `Parts` :786–797; `IsDisposed` :799; `ToggleLights` :1677–1690; `Split` :1807–1930 (`Parts.Split` :1843, `PartitionSymmetryLinksByTree` :1848); `MergeFrom` :2018–2079; `UpdateFromTaskResultsUnsynchronized` :2711, `Parts.UpdateFromTaskResults` :2801; `Dispose` :4984–5011.
[^universe]: Universe.cs — `_runningId` :72; `GetNextRunningId` :254–257; `ResetRunningId` :259–262, called from `DeserializeSave(UniverseData)` (:2571) at :2604.
[^program]: Program.cs — `PrepareFrame` :2511–2608 (waits :2521–2532, apply :2533–2541, `Glfw.PollEvents` :2549, `KfsIntegration.BeforeInputEvents` :2551, `InputEvents.ApplyInputEvents` :2552, flush :2590–2591, `ExecuteNextVehicleSolvers` :2592); `OnFrame` :2610–2700 (UI :2636–2648, `LightSystem.ClearLights` :2693).
[^physics-bubble]: PhysicsBubble.cs — `UpdateModulesJob` :30–45; `StepFrameTo` early return :774–799; single-vehicle path `ApplySingleVehicleMotion` :1494–1580; `RunFlightComputer` → `KfsDispatch.TryComputeControl` :2077–2086; job creation :1884–1886.
[^input-events]: InputEvents.cs — `IActivateInputData.Apply` :482–523; `ApplyInputEvents` :1186–1215.
[^decoupler]: Decoupler.cs — `SetIsActive` queues :108–120; `Decouple` → `vehicle.Split` :122–125.
[^kfs-controller-set]: KfsControllerSet.cs — constructor :60–85; `Matches` :112–119; `MarkDeparted` :121–139.
[^core-electrical-assets]: Core/CoreElectricalAAssets.xml:663–692 (`CoreElectricalA_Prefab_LightSmallA`), sub-part templates :136–155.
[^unscience-saved-part-reference]: ksa-abstractions.lib/Persistence/SavedPartReference.cs:7–14, :37–63; PartIdentitySnapshot.cs:16–49.
