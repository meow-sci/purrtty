---
type: Subsystem Reference
title: KfsControllerSet and layout changes
description: How a vehicle's thrusters, engines and gimbals get stable program-visible indices — ordering by InstanceId, the generated I/O layout, the frozen and current sets, and how elements go inert after staging or docking.
resource: ksa-decomp:KSA.Kfs/KfsControllerSet.cs
tags: [kfs, integration, layout, indexing, staging, docking]
sources:
  - id: kfs-controller-set
    resource: ksa-decomp:KSA.Kfs/KfsControllerSet.cs
    title: KfsControllerSet (decompiled)
  - id: kfs-vehicle-host
    resource: ksa-decomp:KSA.Kfs/KfsVehicleHost.cs
    title: KfsVehicleHost (decompiled)
  - id: kfs-host-sampler
    resource: ksa-decomp:KSA.Kfs/KfsHostSampler.cs
    title: KfsHostSampler (decompiled)
  - id: layout-generator
    resource: ksa-decomp:KSA.Kfs.Host/LayoutGenerator.cs
    title: LayoutGenerator (decompiled)
  - id: module-stateful
    resource: ksa-decomp:KSA/ModuleStateful.cs
    title: ModuleStateful (decompiled)
  - id: module-base
    resource: ksa-decomp:KSA/ModuleBase.cs
    title: ModuleBase (decompiled)
  - id: vehicle
    resource: ksa-decomp:KSA/Vehicle.cs
    title: Vehicle (decompiled)
  - id: workbench
    resource: ksa-decomp:KSA.Kfs.Host.Workbench/Workbench.cs
    title: Workbench (decompiled)
generated: { by: claude-code/claude-fable-5-2, at: 2026-11-25T21:37:08Z }
status: stable
ksa_build: "2026.11.25.5949"
---

# The problem it solves

A program addresses hardware by index: `commands.thruster[3].pulse_s`, `engine[0].throttle`, `gimbal[1].y`. Those indices are baked into
the image through the I/O layout the program was compiled against. The game's own module lists, however, are reordered and shrink
whenever the part tree changes (staging, docking, undocking, part failure). `KfsControllerSet` pins one ordering and tracks which of its
elements are still on the vehicle.[^kfs-controller-set]

# Class

```csharp
public sealed class KfsControllerSet
{
    public KfsControllerSet(Vehicle vehicle);

    public IoLayout Layout { get; }
    public ThrusterController[] Thrusters { get; }          // ordered, never changes
    public EngineController[]   Engines   { get; }
    public GimbalController[]   Gimbals   { get; }
    public bool[] ThrusterIn { get; }                       // still on the vehicle?
    public bool[] EngineIn   { get; }
    public bool[] GimbalIn   { get; }
    public ThrusterController[] PresentThrusters { get; private set; }   // the In == true subset, in order
    public int[]                ThrusterIndex    { get; private set; }   // PresentThrusters[k] == Thrusters[ThrusterIndex[k]]
    public EngineController[]   PresentEngines   { get; private set; }
    public int[]                EngineIndex      { get; private set; }
    public GimbalController[]   PresentGimbals   { get; private set; }
    public int[]                GimbalIndex      { get; private set; }
    public int InertCount { get; }                          // number of false entries across the three In arrays

    public int ThrusterAt(int i);   // byte offset of thruster[i].pulse_s in the command block
    public int EngineAt(int i);     // byte offset of engine[i].throttle   (lease_s is at +4)
    public int GimbalAt(int i);     // byte offset of gimbal[i].y          (z is at +4)

    public bool Matches(Vehicle vehicle);
    public void MarkDeparted(Vehicle vehicle);

    internal HostLeafMap?    LeafMap;            // lazily built by KfsVehicleHost.SamplerFor
    internal KfsHostSampler? Sampler;
    internal readonly double[] InertiaBuffer = new double[9];
    internal ThrusterProps[] ThrusterPropsBuffer;   // reused VehiclePropsValues storage, one entry per element
    internal EngineProps[]   EnginePropsBuffer;
    internal GimbalProps[]   GimbalPropsBuffer;
    internal HostSample?     SampleBuffer;
}
```

# Construction: ordering and layout (`:60-85`)

```csharp
Thrusters = (from t in vehicle.Parts.Modules.Get<ThrusterController>().ToArray() orderby t.InstanceId select t).ToArray();
Engines   = (from t in vehicle.Parts.Modules.Get<EngineController>().ToArray()   orderby t.InstanceId select t).ToArray();
Gimbals   = (from t in vehicle.Parts.Modules.Get<GimbalController>().ToArray()   orderby t.InstanceId select t).ToArray();
Layout    = LayoutGenerator.Vehicle(vehicle.Id, Thrusters.Length, Engines.Length, Gimbals.Length);
```

- The enumeration source is the vehicle-wide module list `vehicle.Parts.Modules.Get<T>()`.
- **The index of an element is its rank by `InstanceId`** among modules of the same type on the vehicle at construction time.
  `InstanceId` is `public readonly ulong ModuleBase.InstanceId`, assigned in the module's constructor from
  `Universe.GetNextRunningId()` — a process-wide `Interlocked.Increment` counter that `Universe.DeserializeSave` restarts at 1
  (`ModuleBase.cs:61`, `:79`, `:85`; `Universe.cs:254-262`, `:2604`).[^module-base] The order is therefore **module creation order
  since the game started or a save was last loaded**, independent of the order
  of the game's lists, and unaffected by staging or docking (modules keep their ids when parts move between vehicles).
  *Inferred:* the ids are not saved (the save format carries a separate per-file `LocalInstanceId`), so after loading a save the modules
  are new objects with new ids and the ranking follows the order in which the loader recreates them; whether that reproduces the
  pre-save order was not traced. See [part and module model](/game-targets/part-and-module-model.md).
- `LayoutGenerator.Vehicle(name, thrusters, engines, gimbals)` builds the command block from the three counts only:[^layout-generator]

| Field | Type | Offset rule | One-shot |
| --- | --- | --- | --- |
| `thruster[i].pulse_s` | `float32` | `4 * i` | yes |
| `engine[j].throttle`, `engine[j].lease_s` | `float32` each | after the thrusters, rounded up to 8; 8 bytes per engine | no |
| `gimbal[k].y`, `gimbal[k].z` | `float32` each | after the engines, rounded up to 8; 8 bytes per gimbal | no |

  The command block may not exceed 65 536 bytes and the `MSG_VEHICLE_PROPS` body 65 535 bytes (`LayoutLimitException`). Full detail:
  [I/O layout generation](/host/io-layout-generation.md).

- The byte offsets used by write-back are resolved once by field name with the invariant culture
  (`Offset(Inv($"engine[{i}].throttle"))` → `Array.Find(Layout.CommandFields, f => f.Name == name).Offset`).
- All `…In` flags start `true`; `Refresh()` builds the `Present…`/`…Index` arrays.

Because the layout depends only on the vehicle id and the three counts, two vehicles with the same counts get layouts that differ only in
name; the fingerprint the image is checked against is described in [I/O layout tables](/tables/io-layout-tables.md).

# Frozen versus current

`KfsVehicleHost` holds two sets:[^kfs-vehicle-host]

| Set | Created | Used for |
| --- | --- | --- |
| `Frozen` | Once, in the host constructor (`Frozen = (Current = new KfsControllerSet(vehicle))`). **Never replaced.** | `host.Layout` (what programs are compiled and loaded against, `Workbench.cs:790`, `:815`), the sampler, the props buffers, the write-back. |
| `Current` | Replaced by `new KfsControllerSet(Vehicle)` every time a change is detected. | Only as the baseline for detecting the *next* change. Nothing in this build samples or writes through `Current`. |

So for the lifetime of a host, **program indices are fixed at the moment the host was created** — typically the first time the player
targeted the vehicle with the workbench — not at image load and not at program start.

# Change detection: `Matches` and `MarkDeparted`

`KfsVehicleHost.DetectLayoutChange()` runs at the start of every dispatch while the computer is under kFS control, on the dispatch
thread (`KfsVehicleHost.cs:428-440`, `:504-510`):

```csharp
if (!Current.Matches(Vehicle))
{
    Frozen.MarkDeparted(Vehicle);
    if (Current != Frozen) Current.MarkDeparted(Vehicle);
    Current = new KfsControllerSet(Vehicle);
    Computer.Event($"layout change: {Frozen.InertCount} element(s) of the program's frozen layout are inert (integration §2.4)");
}
```

`Matches(Vehicle vehicle)` (`:112-119`) compares **counts only**: the vehicle's current number of `ThrusterController`,
`EngineController` and `GimbalController` modules against the lengths of `PresentThrusters`, `PresentEngines`, `PresentGimbals`.

`MarkDeparted(Vehicle vehicle)` (`:121-139`) builds a `HashSet` of each module type currently on the vehicle and does
`ThrusterIn[i] &= set.Contains(Thrusters[i])` (and likewise for engines and gimbals), then `Refresh()`. The `&=` makes departure
**permanent**: an element that left and later returns (undock, then dock again) stays inert in that set.

The part-tree operations that change these counts are `Vehicle.Split` (staging, decoupling, undocking: the departing subtree becomes a
new `Vehicle`) and `Vehicle.MergeFrom` (docking: the other vehicle's parts join this one and the other vehicle is disposed); both end
with `UpdateVehicleConfiguration()`.[^vehicle] Programs are loaded against `host.Layout`, i.e. the frozen layout.[^workbench]

| Event on the vehicle | Effect on `Frozen` | Program-visible result |
| --- | --- | --- |
| Staging / decoupling / part destruction removes controllers | Their `…In` flags become `false`; `Present…` shrinks; `InertCount` grows | Indices of the remaining elements are unchanged. Commands to inert indices are ignored; their `MSG_VEHICLE_PROPS` entries read zero / `false`. One "layout change" event is logged. |
| Docking adds controllers | Counts differ → change detected; nothing in `Frozen` departs (`InertCount` unchanged) | The new hardware has **no index** — the frozen layout has no slot for it. It cannot be commanded or sampled by the program for the life of this host. |
| Undocking the vehicle that carried the host away | The remaining vehicle keeps the host (see [lifecycle](/integration/lifecycle-and-persistence.md)); controllers on the departed part are marked inert | As staging. |
| A change that keeps all three counts equal (for example one engine lost and one gained in the same frame) | **Not detected** — `Matches` returns `true` | *Inferred:* the frozen set would still list a module that is no longer on the vehicle; see the hazard below. |

# Why inert elements must be filtered: `StatesIdx`

Write-back and sampling address module state through `ModuleStateful<…>.StateUpdater`, whose enumerators index the state buffer with the
module's own `StatesIdx` field (`ModuleStateful.cs:605-617`, `:722-725`).[^module-stateful] `StatesIdx` is the module's slot in *its current
vehicle's* state list. A controller that has moved to another vehicle carries an index that is meaningless — or simply out of range — for
this vehicle's updater. That is why:

- write-back iterates `frozen.PresentThrusters` / `PresentEngines` / `PresentGimbals`, never the full arrays, and maps back to frozen
  indices through `ThrusterIndex` / `EngineIndex` / `GimbalIndex` (`KfsVehicleHost.cs:575-614`);
- the samplers test `set.ThrusterIn[i]` / `EngineIn[i]` / `GimbalIn[i]` before calling `GetReadOnlyState(module)`
  (`KfsVehicleHost.cs:381-406`, `KfsHostSampler.cs:466`, `:481`, `:505`).[^kfs-vehicle-host][^kfs-host-sampler]

`DetectLayoutChange` runs before the tick and the write-back in the same dispatch, and the part tree is only restructured on the main
thread between solver jobs, so within one dispatch the `Present…` arrays are consistent with the updaters. *Inferred* from the call
order and the frame structure in [dispatch and threading](/integration/dispatch-and-threading.md).

# Using the set from outside

- `host.Frozen.Thrusters[i]` is the game module behind program index `i`; `host.Frozen.ThrusterIn[i]` says whether it is still aboard.
- `host.Thrusters`, `host.Engines`, `host.Gimbals` are shortcuts to the frozen arrays; `host.Layout` to `Frozen.Layout`.
- `Frozen.InertCount` is the number the "layout change" event reports.

# Modding notes — a model for indexing lights and animated parts

The design choices here are directly reusable for any new per-vehicle hardware list (lights, deployable or animated parts):

1. **Order by an identity, not by list position.** kFS uses `InstanceId`, which is stable for as long as the module object lives
   (through staging and docking) but is a session-local running number. A mod that wants indices to mean the same thing after a
   save/load, or the same thing as a name printed on the part, needs a key that is persisted (for example the part's id plus the module's
   position within the part) — at the price of not matching kFS's own ordering rule.
2. **Freeze the ordering at a well-defined moment** and keep it for the life of the per-vehicle object; never renumber under a running
   program.
3. **Track presence separately** (`…In` flags), make departure sticky, and treat commands to absent elements as no-ops and their
   telemetry as zero. Expose a count or a per-element "present" bit so programs can tell.
4. **Detect change cheaply every dispatch** — but note the two weaknesses of kFS's own detector: it is count-based (equal-count swaps go
   unnoticed) and it only runs while the computer is under kFS control. A mod list can do better by comparing identities.
5. **Hardware gained by docking gets no index** in kFS. If new hardware should become addressable, the per-vehicle object has to be
   rebuilt — which in kFS terms means a new host and a recompiled program, because the layout fingerprint changes.

API facts for patching:

- `KfsControllerSet` is public; its constructor is public and has no side effects beyond reading the vehicle, so a mod can build its own
  instance to learn "what would the indices be now" without touching the host.
- `PresentThrusters`, `ThrusterIndex` and their siblings have private setters (assigned in the private `Refresh`); `LeafMap`, `Sampler`
  and the buffers are `internal`.
- `KfsVehicleHost.Frozen` has no setter at all; `Current` has a private setter. There is no supported way to re-freeze a host — clearing
  `Vehicle.KfsHost` (a public settable property) makes the old host fail `IsAlive`, so it is dropped at the next `ReleaseDead` and a new
  one can be created with `KfsVehicleHost.For`. *Unverified:* doing so under a running program was not analysed beyond the code paths
  described in [lifecycle](/integration/lifecycle-and-persistence.md).
- The command block layout is generated from three integers; adding command fields for new hardware means changing
  `LayoutGenerator.Vehicle` and everything that reads `IoLayout` (see [I/O layout generation](/host/io-layout-generation.md)) — a far
  larger change than adding a service.

[^kfs-controller-set]: KfsControllerSet.cs — fields and properties 12-58, constructor 60-85, `Offset` 92-95, `EngineAt`/`GimbalAt`/`ThrusterAt` 97-110, `Matches` 112-119, `MarkDeparted` 121-139, `Refresh` 141-155.
[^kfs-vehicle-host]: KfsVehicleHost.cs:67-77, 105-118, 128-137, 362-426, 428-440, 575-614.
[^kfs-host-sampler]: KfsHostSampler.cs:463-521.
[^layout-generator]: LayoutGenerator.cs:12-80.
[^module-stateful]: ModuleStateful.cs — `StateList.Add` 302-316 (lookup by `InstanceId`), `StateUpdater.ModuleAndNewStateEnumerator` 597-645, `GetReadOnlyState` 722-725, `GetModulesAndNewStates(ReadOnlySpan<TModule>)` 775-778, `StatesIdx` 810.
[^module-base]: ModuleBase.cs:52-86; Universe.cs:254-262 (`GetNextRunningId`, `ResetRunningId`), 2604 (reset in `DeserializeSave`).
[^vehicle]: Vehicle.cs:1808-1953 (`Split`), 2018-2078 (`MergeFrom`), 2151-2166 (`UpdateVehicleConfiguration`).
[^workbench]: Workbench.cs:784-822.
