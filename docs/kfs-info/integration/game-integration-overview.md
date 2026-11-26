---
type: Subsystem Reference
title: kFS game integration overview
description: Every touch point between the KSA game code and kFS, the startup registration order, the object graph from KfsIntegration down to a vehicle's computer, and a per-frame and per-step walkthrough.
resource: ksa-decomp:KSA.Kfs/KfsIntegration.cs
tags: [kfs, integration, architecture, lifecycle]
sources:
  - id: kfs-integration
    resource: ksa-decomp:KSA.Kfs/KfsIntegration.cs
    title: KfsIntegration (decompiled)
  - id: kfs-dispatch
    resource: ksa-decomp:KSA.Kfs/KfsDispatch.cs
    title: KfsDispatch (decompiled)
  - id: kfs-vehicle-host
    resource: ksa-decomp:KSA.Kfs/KfsVehicleHost.cs
    title: KfsVehicleHost (decompiled)
  - id: kfs-console-commands
    resource: ksa-decomp:KSA.Kfs/KfsConsoleCommands.cs
    title: KfsConsoleCommands (decompiled)
  - id: program
    resource: ksa-decomp:KSA/Program.cs
    title: Program (decompiled)
  - id: physics-bubble
    resource: ksa-decomp:KSA/PhysicsBubble.cs
    title: PhysicsBubble (decompiled)
  - id: vehicle
    resource: ksa-decomp:KSA/Vehicle.cs
    title: Vehicle (decompiled)
  - id: physics-step-limit
    resource: ksa-decomp:KSA/PhysicsStepLimit.cs
    title: PhysicsStepLimit (decompiled)
  - id: imgui-window
    resource: ksa-decomp:KSA/ImGuiWindow.cs
    title: ImGuiWindow (decompiled)
  - id: constants
    resource: ksa-decomp:KSA/Constants.cs
    title: Constants (decompiled)
  - id: offline-harness
    resource: /modding/offline-harness.md
    title: Offline harness experiments E1–E12
generated: { by: claude-code/claude-fable-5-2, at: 2026-11-25T21:37:08Z }
status: stable
ksa_build: "2026.11.25.5949"
---

# Summary

kFS lives in its own namespaces (`KSA.Kfs`, `KSA.Kfs.*`). The rest of the game refers to it in **five files and ten places**. Everything
else is kFS calling *into* the game. The glue classes in `KSA.Kfs` are:

| Class | Visibility | Role | Document |
| --- | --- | --- | --- |
| `KfsIntegration` | `public static` | Lazy statics (`Tables`, `Workbench`), registration, the per-frame main-thread hook, console output | this document |
| `KfsPrototype` | `public static` | The feature flag | [feature flag](/integration/feature-flag.md) |
| `KfsDispatch` | `internal static` | The one call the physics step makes into kFS | [dispatch and threading](/integration/dispatch-and-threading.md) |
| `KfsVehicleHost` | `public sealed` | One per `Vehicle`: owns the computer, the controller sets, the services | [KfsVehicleHost](/integration/kfs-vehicle-host.md) |
| `KfsServices` | `public sealed` | Applies service requests to the game; main-thread work queue | [KfsServices (game side)](/integration/kfs-services-game-side.md) |
| `KfsHostSampler` | `public sealed` | Fills host-published message leaves from game state | [host sampler](/integration/host-sampler.md) |
| `KfsControllerSet` | `public sealed` | Ordered thruster/engine/gimbal arrays and their I/O layout | [controller set](/integration/controller-set-and-layout-changes.md) |
| `KfsWriteBack` | `public static` | Stock flight-computer resets and engine performance helper | [write-back](/integration/write-back.md) |
| `EngineModel` | `public sealed` | Static engine-core model exported in `MSG_VEHICLE_PROPS` | [host sampler](/integration/host-sampler.md) |
| `KfsDispatchProfiler` | `public sealed` | Per-host CPU zone profiler for the debug window | [KfsVehicleHost](/integration/kfs-vehicle-host.md) |
| `KfsConsoleCommands`, `KfsEditorWindow`, `KfsDebugWindow`, `KfsReferenceWindow`, popups, text widgets | — | Tooling | [/tooling/](/tooling/console-commands.md) |

# Game → kFS touch points (complete)

A search for `Kfs` over every decompiled namespace other than `KSA.Kfs*` finds only these.

| File:line | Code | Purpose |
| --- | --- | --- |
| `KSA/Program.cs:31` | `using KSA.Kfs;` | |
| `KSA/Program.cs:978` | `KfsIntegration.RegisterCommands(this);` | Startup registration, inside the `Loading.Task("Commands")` block of the `Program` constructor.[^program] |
| `KSA/Program.cs:2551` | `KfsIntegration.BeforeInputEvents();` | Per-frame main-thread hook in `Program.PrepareFrame`, between `Glfw.PollEvents()` and `InputEvents.ApplyInputEvents()`.[^program] |
| `KSA/Program.cs:4423` | `ImGuiHelper.DrawMenuItem("Show kFS Debug"u8, ref KfsDebugWindow.Show);` | Debug-menu toggle for the kFS debug window, next to "Show Flight Computer Debug".[^program] |
| `KSA/PhysicsBubble.cs:14` | `using KSA.Kfs;` | |
| `KSA/PhysicsBubble.cs:80`, `:82`, `:91-92`, `:98-104`, `:116-121` | `WakeRequest.KfsDeltaTime`, `KfsVehicleIdx`, and the `PhysicsStepLimit.KfsTick` branches of `MinimumOf` / `Resolve` | kFS tick wakeups are tracked apart from stock wakeups so they bypass the stock flight-computer rate limit.[^physics-bubble] |
| `KSA/PhysicsBubble.cs:2081` | `if (!KfsDispatch.TryComputeControl(vehicleState, in flightComputerNavigation, ref flightComputerOutput))` | The dispatch. `false` → the stock `FlightComputer.ComputeControl` runs (`:2083-2084`).[^physics-bubble] |
| `KSA/Vehicle.cs:22`, `:567` | `using KSA.Kfs;` / `public KfsVehicleHost? KfsHost { get; set; }` | The per-vehicle back-pointer. Nothing else in `Vehicle` reads or clears it.[^vehicle] |
| `KSA/PhysicsStepLimit.cs:17` | `KfsTick,` (value 12, last before `EnumCount`) | Substep-limit attribution.[^physics-step-limit] |
| `KSA/PhysicsStepLimitEx.cs:24` | `PhysicsStepLimit.KfsTick => "kFS Tick",` | Player-facing name in the slow-bubble overlay. |

One game member exists only for kFS's benefit: `internal static ManualControlInputs PhysicsBubble.FlightComputerInputsFor(VehicleUpdateState)`
(`PhysicsBubble.cs:2067-2075`), shared by the stock path and `KfsVehicleHost`.[^physics-bubble]

# kFS → game dependencies (what the glue calls)

| Game API | Used by | For |
| --- | --- | --- |
| `Program.ControlledVehicle`, `Program.Instance`, `Program.ConsoleWindow`, `Program.TerminalInterface` | `KfsIntegration`, `KfsConsoleCommands` | Active vehicle, console output, command registration |
| `ImGuiWindow.RegisterStaticWindow(IStaticWindow)` | `KfsIntegration.RegisterCommands` | Per-frame UI hook and debug window |
| `Constants.DocumentsFolderPath` | `KfsIntegration.Workbench` | Workspace root `<Documents>/My Games/Kitten Space Agency/kfs`[^constants] |
| `Vehicle.KfsHost`, `IsDisposed`, `System`, `IsControllable`, `Parts`, `FlightComputer`, `FlightPlan`, `Orbit`, `KinematicMeasurements`, `SetEnum` | `KfsVehicleHost`, `KfsServices`, `KfsControllerSet` | Host lifetime, sampling, main-thread effects |
| `Universe.CurrentSystem`, `Universe.GetElapsedTime()`, `Universe.GetSimulationSpeed()` | `KfsVehicleHost`, `KfsServices` | Liveness, planning time, `sim_speed` |
| `GameSettings.Current.Simulation.MaxFlightComputerRate` | `KfsVehicleHost.SampleFor` | `min_fc_dt_s` |
| `VehicleUpdateState`, `FlightComputerNavigation`, `FlightComputerOutput`, `ModuleStateful<…>.StateUpdater` | dispatch path | Inputs and actuator write-back |
| `FlightComputer` (fields, `ComputeControl`, `TrackTarget`, `RateHold`, `SetAttitudeProfile`, `BurnPlan`, `Burn`, `GetFullThrottlePhases`, `HasLaterStage`) | `KfsServices`, `KfsWriteBack`, `KfsHostSampler` | Services and pilot mode |
| `InputEvents.BurnUpdateBuffer`, `SequenceList.ActivateNextSequence`, `Burn.Create`, `FiniteBurnRefiner.RefineToCircular`, `OrbitalTransfers.DvVlfToCircularize` | `KfsServices.MainThread` / `Plan` | Burn planning and staging |
| `ThrusterController`, `EngineController`, `GimbalController`, `Rocket`, `RocketNozzle`, `RocketControllerData`, `Gimbal` | `KfsControllerSet`, `KfsVehicleHost`, `KfsHostSampler`, `EngineModel` | Hardware enumeration and properties |

# Startup registration order

`Program`'s constructor, in order:[^program]

| Line | Step |
| --- | --- |
| 962-963 | `TerminalInterface.OnOutput += TerminalOutput`, console settings |
| 966-977 | `RegisterCommands()` and the other subsystems' `RegisterCommands(this)` |
| **978** | **`KfsIntegration.RegisterCommands(this)`** — last in the block |
| 991-1002 | New-mod confirmation, manifest save |
| 1007, 1011, 1026 | `ModLibrary.PrepareAll`, `PreloadAssetBundles`, `LoadAll` (content mods) |
| 1058 | `TerminalInterface.Execute(ModLibrary.OnBoot…)` |
| 1060 | `JobSystems.Initialize()` (creates the solver scheduler and worker pools) |

`KfsIntegration.RegisterCommands(Program program)` does four things, once (`_registered` guard):[^kfs-integration]

1. `KfsVehicleHost.FrameBudgetMs = 4.0;`
2. `KfsConsoleCommands.Register(Program.TerminalInterface, program);` — registers the single `kfs` terminal action
   (`KfsConsoleCommands.Execute(string args = "")`, `[TerminalAction("kfs", …, ArgParseMode.Unparsed)]`).[^kfs-console-commands]
3. `ImGuiWindow.RegisterStaticWindow(new FrameHook());` — a window-less `IStaticWindow` whose `OnDrawUi` calls
   `_workbench?.Frame(ImGui.GetIO().DeltaTime)`; `IsShown` is `KfsPrototype.Enabled && _workbench != null`.
4. `ImGuiWindow.RegisterStaticWindow(new KfsDebugWindow());`

Nothing here touches `Tables` or `Workbench`: both stay unbuilt until first use. The registration runs regardless of the feature flag.

*Unverified:* where a StarMap-loaded code mod's patches are applied relative to line 978 depends on the loader, not on the game; see
[/modding/](/modding/index.md).

# Object graph

```text
KfsIntegration (static)
├── Tables      : GameTables         lazy, LazyInitializer.EnsureInitialized(ref _tables, GameTables.LoadEmbedded)   KfsIntegration.cs:89
├── Workbench   : Workbench          lazy, new Workbench(Tables, <Documents>/…/kfs, Output)                          :93-121
│     ├── ActiveVehicle        = () => Program.ControlledVehicle == null ? null : KfsVehicleHost.For(controlledVehicle)
│     ├── ActiveVehicleIdSource= () => Program.ControlledVehicle?.Id          (does not create a host)
│     └── Bench : BenchMachine       the off-vehicle test computer
├── Editor      : KfsEditorWindow?   created by ShowEditor()/ToggleEditor()                                          :183-214
└── Output      : TextWriter         default ConsoleWindowWriter → Program.ConsoleWindow.Print                       :91, :39-75

Vehicle.KfsHost ──► KfsVehicleHost : IVehicleTarget          (also listed in the static KfsVehicleHost.AllHosts)
                    ├── Vehicle   : Vehicle                  back-reference
                    ├── Computer  : VehicleComputer          new VehicleComputer(tables, tables.BenchPart, vehicle.Id)
                    │     ├── Machine : KvmMachine           the VM
                    │     ├── Handles : BurnHandles          burn-handle table
                    │     ├── Override: ServiceOverride      throttle / engine-switch override
                    │     └── Grid    : TickGrid?            tick schedule (null when not started)
                    ├── Frozen    : KfsControllerSet         hardware as of host creation; defines Layout
                    ├── Current   : KfsControllerSet         hardware as of the last detected change
                    ├── Services  : KfsServices              game-side service effects + main-thread queue
                    └── DispatchProfiler : KfsDispatchProfiler
```

Every vehicle computer is built on `tables.BenchPart` — the host does not look at which avionics parts the vehicle actually carries.[^kfs-vehicle-host]

# Who creates a host

`KfsVehicleHost.For(Vehicle)` is the only creator, and its only caller is the `Workbench.ActiveVehicle` delegate.[^kfs-integration][^kfs-vehicle-host]
The workbench invokes that delegate when the user targets or loads onto "the vehicle" (`kfs target vehicle`, `kfs load … --vehicle`,
the editor's upload, and `ShowEditor()` → `Workbench.TargetActiveVehicle()`), always for `Program.ControlledVehicle`. Vehicles nobody
targeted have `KfsHost == null` and are untouched by kFS. Details and death conditions: [lifecycle](/integration/lifecycle-and-persistence.md).

# Per-frame walkthrough

| # | Thread | Step | Where |
| --- | --- | --- | --- |
| 1 | main | Wait for the previous frame's solver jobs; apply their results to vehicles (worker `FlightComputer` copy, module states, docking / destruction events). | `Program.cs:2522-2540` |
| 2 | main | `Glfw.PollEvents()` | `:2549` |
| 3 | main | **`KfsIntegration.BeforeInputEvents()`** — if the flag is on: bump the frame counter (`KfsVehicleHost.BeginFrame`), drop dead hosts (`ReleaseDead`), then `host.Services.MainThread()` for every host in `Id` order: applies queued stage / engine-off / burn-plan work. | `:2551`; `KfsIntegration.cs:158-176` |
| 4 | main | `InputEvents.ApplyInputEvents()` — applies the staging activations and burn add/delete events step 3 queued, plus the player's. | `:2552` |
| 5 | main | Choose the simulation step; `Universe.ExecuteNextVehicleSolvers` snapshots every vehicle (`Vehicle.PrepareWorker`) and starts the solver job. | `:2587-2592` |
| 6 | solver / workers | For every bubble, the physics step runs flight-computer passes; each pass calls `KfsDispatch.TryComputeControl` per vehicle (next section). Runs concurrently with steps 7-8. | `PhysicsBubble.cs:2077-2086` |
| 7 | main | UI: static windows (`FrameHook` → `Workbench.Frame`, debug window, editor, reference), then the console (where `kfs …` commands execute). | `Program.cs:2639-2647`, `:3552` |
| 8 | main | Render, present. | `:2698-2707` |

# Per-step walkthrough (one dispatch)

`KfsDispatch.TryComputeControl(vehicleState, in nav, ref output)`:[^kfs-dispatch]

1. Flag off → `false`. No `vehicleState.ReadOnlyVehicle.KfsHost` → `false`.
2. `kfsHost.Dispatch(vehicleState, in nav, ref output, readOnlyVehicle.IsControllable)`:
   1. Compute the tick deadline from the frame budget; open a profiler frame.
   2. `DispatchCore`: react to a flag off/on cycle; if the computer is under kFS control, detect hardware changes and run
      `Services.BeforeDispatch` (pilot-override detection, stage-press counting).
   3. `Computer.Dispatch<StepSampler>(nav.Time ns, ref sampler, controllable)`: drain the queued actions (load/start/stop/…); if under
      control, run every due tick — sampling game state once, lazily, via `KfsVehicleHost.Sample`.
   4. Apply the resets the computer asked for (`KfsWriteBack.CSharpReset`, `Services.HandBack`).
   5. Not under control → return `false`. Fallback (vehicle not controllable) → apply services, return `false`.
   6. Pilot mode → apply services, then run the stock `ComputeControl` with the service override, request the next tick wakeup, return `true`.
   7. Full control → apply services, clear the output, request the next tick wakeup, and when a tick latched commands write thruster
      pulses, engine throttle/burn time and gimbal commands into the state updaters; return `true`.
3. Any exception → `kfsHost.Fault(e, vehicleState.FlightComputer)` and `false`.

Step-by-step detail with line numbers: [KfsVehicleHost](/integration/kfs-vehicle-host.md).

# Public surface of `KfsIntegration`

| Member | Signature | Notes |
| --- | --- | --- |
| `Tables` | `public static GameTables Tables { get; }` | Lazy; `LazyInitializer.EnsureInitialized(ref _tables, GameTables.LoadEmbedded)` (`:89`). |
| `Output` | `public static TextWriter Output { get; set; }` | Default: private `ConsoleWindowWriter` (prints to `Program.ConsoleWindow`, or `DefaultCategory.Log.Info` before the console exists). Re-applied to `Workbench.Out` on every `Workbench` access (`:118`). |
| `Workbench` | `public static Workbench Workbench { get; }` | Lazy. When `_registered`, sets `TickHostMs = 250` and `RunHostMsPerFrame = 4.0`; subscribes `OpenRequested` → `ShowEditor()`. |
| `Editor` | `internal static KfsEditorWindow? Editor { get; }` | |
| `Print` / `Error` | `public static void Print(string line)` / `Error(string line)` | `Error` logs at error level and writes to `Output` only when it is not the console writer. |
| `UseWorkbench` | `public static void UseWorkbench(Workbench workbench)` | Replaces the workbench instance (and wires `OpenRequested`). |
| `RegisterCommands` | `public static void RegisterCommands(Program program)`; `public static void RegisterCommands(TerminalInterface terminal, object owner)` | The second overload only registers the `kfs` command on another terminal. |
| `BeforeInputEvents` | `public static void BeforeInputEvents()` | The per-frame hook. |
| `ShowEditor` / `ToggleEditor` | `public static void ShowEditor()` / `ToggleEditor()` | No-ops while `Program.Instance == null`; create the editor on first use and call `Workbench.TargetActiveVehicle()` when it becomes visible — which creates a host for the controlled vehicle. |

# Modding notes

- The whole game-side contract is three calls (`RegisterCommands`, `BeforeInputEvents`, `TryComputeControl`) plus one property
  (`Vehicle.KfsHost`). A mod that wants to live *next to* kFS can hook the same three places; see
  [patch targets and seams](/integration/patch-targets-and-seams.md).
- `KfsIntegration`, `KfsPrototype`, `KfsVehicleHost`, `KfsServices`, `KfsControllerSet`, `KfsWriteBack` are public; `KfsDispatch` is
  `internal`, `KfsVehicleHost.AllHosts`/`ReleaseDead`/`BeginFrame`/`Fault` are `internal`. A compile-time reference to the game assembly
  can use the public ones directly; the internal ones need reflection, a publicized reference assembly or Harmony `AccessTools`.
- `Tables` is loaded lazily on first kFS use, so a mod has time to patch `GameTables.LoadEmbedded` before the tables exist — provided it
  does so before anything touches `KfsIntegration.Tables` or `KfsIntegration.Workbench` (first kFS console verb that goes through the
  workbench — every verb except `help`, `on`, `off`, `kill` and `budget` — first debug-window draw with the flag on, or first editor
  show). *Inferred* from the lazy getters; not checked
  against a running game. The patch route itself is verified outside the game: with a postfix on `GameTables.LoadEmbedded` that
  replaces `__result`, `KfsIntegration.Tables` returned the augmented instance, and the private `_tables` was null before the first
  read ([offline harness](/modding/offline-harness.md)).[^offline-harness]
- `KfsIntegration.Output` and `KfsIntegration.UseWorkbench` are public seams: a mod can redirect kFS console output (for example into an
  in-game terminal) by replacing `Output`; the workbench picks the new writer up on its next access.
- `ImGuiWindow.RegisterStaticWindow(IStaticWindow)` is public static (`ImGuiWindow.cs:122-125`): the same mechanism kFS uses for its
  `FrameHook` gives a mod a per-frame main-thread callback — but in the UI phase, concurrent with dispatches, not in the sync window.[^imgui-window]

[^program]: Program.cs — constructor registration 962-979, `ModLibrary` loading 1007-1026, `JobSystems.Initialize` 1060, `PrepareFrame` 2511-2608, `OnFrame` 2610-2710, menu item 4423.
[^physics-bubble]: PhysicsBubble.cs:72-127 (`WakeRequest`), 2067-2075 (`FlightComputerInputsFor`), 2077-2086 (`RunFlightComputer`).
[^vehicle]: Vehicle.cs:22, 567.
[^physics-step-limit]: PhysicsStepLimit.cs:3-19; PhysicsStepLimitEx.cs:10-27.
[^constants]: Constants.cs:289.
[^kfs-integration]: KfsIntegration.cs — `FrameHook` 15-37, `ConsoleWindowWriter` 39-75, `Tables` 89, `Output` 91, `Workbench` 93-121, `RegisterCommands` 146-156, `BeforeInputEvents` 158-176, `ShowEditor`/`ToggleEditor` 183-214.
[^kfs-console-commands]: KfsConsoleCommands.cs:187-232.
[^kfs-vehicle-host]: KfsVehicleHost.cs:105-118 (constructor), 139-147 (`For`, `Find`).
[^kfs-dispatch]: KfsDispatch.cs:5-28.
[^imgui-window]: ImGuiWindow.cs:11, 122-137.
[^offline-harness]: Experiment E10 in the offline harness document.
