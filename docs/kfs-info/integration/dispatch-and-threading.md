---
type: Subsystem Reference
title: kFS dispatch and threading
description: Which threads run the kFS dispatch, the main-thread hook and the UI, what each may read or mutate, and how wakeups, the frame budget, time warp and rails interact with a vehicle computer.
resource: ksa-decomp:KSA/PhysicsBubble.cs
tags: [kfs, integration, threading, dispatch, physics, extension]
sources:
  - id: physics-bubble
    resource: ksa-decomp:KSA/PhysicsBubble.cs
    title: PhysicsBubble (decompiled)
  - id: vehicle-update-task
    resource: ksa-decomp:KSA/VehicleUpdateTask.cs
    title: VehicleUpdateTask (decompiled)
  - id: universe
    resource: ksa-decomp:KSA/Universe.cs
    title: Universe (decompiled)
  - id: program
    resource: ksa-decomp:KSA/Program.cs
    title: Program (decompiled)
  - id: job-systems
    resource: ksa-decomp:KSA/JobSystems.cs
    title: JobSystems (decompiled)
  - id: job-runner
    resource: ksa-decomp:Brutal.Concurrency.Jobs/JobRunner.cs
    title: JobRunner (decompiled)
  - id: dynamic-worker-pool
    resource: ksa-decomp:KSA.Concurrency/DynamicWorkerPool.cs
    title: DynamicWorkerPool (decompiled)
  - id: parallel-batch
    resource: ksa-decomp:KSA.Concurrency/ParallelBatch.cs
    title: ParallelBatch (decompiled)
  - id: bubble-job
    resource: ksa-decomp:KSA/IBubbleJob.cs
    title: IBubbleJob (decompiled)
  - id: vehicle
    resource: ksa-decomp:KSA/Vehicle.cs
    title: Vehicle (decompiled)
  - id: vehicle-update-state
    resource: ksa-decomp:KSA/VehicleUpdateState.cs
    title: VehicleUpdateState (decompiled)
  - id: vehicle-update-data
    resource: ksa-decomp:KSA/VehicleUpdateData.cs
    title: VehicleUpdateData (decompiled)
  - id: flight-computer-output
    resource: ksa-decomp:KSA/FlightComputerOutput.cs
    title: FlightComputerOutput (decompiled)
  - id: input-events
    resource: ksa-decomp:KSA/InputEvents.cs
    title: InputEvents (decompiled)
  - id: game-settings
    resource: ksa-decomp:KSA/GameSettings.cs
    title: GameSettings (decompiled)
  - id: kfs-dispatch
    resource: ksa-decomp:KSA.Kfs/KfsDispatch.cs
    title: KfsDispatch (decompiled)
  - id: kfs-vehicle-host
    resource: ksa-decomp:KSA.Kfs/KfsVehicleHost.cs
    title: KfsVehicleHost (decompiled)
  - id: kfs-integration
    resource: ksa-decomp:KSA.Kfs/KfsIntegration.cs
    title: KfsIntegration (decompiled)
  - id: kfs-services
    resource: ksa-decomp:KSA.Kfs/KfsServices.cs
    title: KfsServices (decompiled)
  - id: vehicle-computer
    resource: ksa-decomp:KSA.Kfs.Host/VehicleComputer.cs
    title: VehicleComputer (decompiled)
  - id: host-computer
    resource: ksa-decomp:KSA.Kfs.Host/HostComputer.cs
    title: HostComputer (decompiled)
  - id: workbench
    resource: ksa-decomp:KSA.Kfs.Host.Workbench/Workbench.cs
    title: Workbench (decompiled)
  - id: console-window
    resource: ksa-decomp:Brutal.ImGuiApi.Abstractions/ConsoleWindow.cs
    title: ConsoleWindow (decompiled)
  - id: kfs-console-commands
    resource: ksa-decomp:KSA.Kfs/KfsConsoleCommands.cs
    title: KfsConsoleCommands (decompiled)
  - id: physics-step-limit
    resource: ksa-decomp:KSA/PhysicsStepLimitEx.cs
    title: PhysicsStepLimit and PhysicsStepLimitEx (decompiled)
  - id: part-tree
    resource: ksa-decomp:KSA/PartTree.cs
    title: PartTree (decompiled)
  - id: keyframe-animation-module
    resource: ksa-decomp:KSA/KeyframeAnimationModule.cs
    title: KeyframeAnimationModule (decompiled)
  - id: power-consumer
    resource: ksa-decomp:KSA/PowerConsumer.cs
    title: PowerConsumer (decompiled)
  - id: part
    resource: ksa-decomp:KSA/Part.cs
    title: Part (decompiled)
  - id: docking-event
    resource: ksa-decomp:KSA/DockingEvent.cs
    title: DockingEvent (decompiled)
  - id: docking-port
    resource: ksa-decomp:KSA/DockingPort.cs
    title: DockingPort (decompiled)
generated: { by: claude-code/claude-fable-5-2, at: 2026-11-26T06:11:10Z }
status: stable
ksa_build: "2026.11.25.5949"
---

# Conclusions first

Line numbers are written inline as `File.cs:N`; the footnote names the source.

| # | Conclusion | Evidence |
| --- | --- | --- |
| 1 | `KfsDispatch.TryComputeControl` **never runs on the main thread.** It runs inside the vehicle solver job, on either the solver's .NET thread-pool thread or one of the dedicated `VehicleWorker{i}` threads. | Only caller is `PhysicsBubble.RunFlightComputer` (`PhysicsBubble.cs:2077-2086`), reached only from `PhysicsBubble.StepFrameTo`, which is called only from `VehicleUpdateTask` island jobs (`VehicleUpdateTask.cs:60-83`, `BubbleStepSegmentJob.cs:9-12`). `VehicleUpdateTask` is an `IJob` queued on `JobSystems.VehicleSolver` (`Universe.cs:2283-2288`), whose runner executes on `ThreadPool.UnsafeQueueUserWorkItem` (`JobRunner.cs:115`).[^physics-bubble][^vehicle-update-task][^universe][^job-runner] |
| 2 | Dispatches of **different vehicles can run in parallel** when the vehicles are in different physics bubbles; vehicles in the **same bubble are dispatched sequentially** on one thread. | Islands are executed with `ParallelBatch.Execute(this, _islandJobs…)` (`VehicleUpdateTask.cs:300-303`); inside a bubble the flight-computer pass is a plain `for` loop over `_vehicleStates` (`PhysicsBubble.cs:1642-1652`, `2036-2047`).[^vehicle-update-task][^physics-bubble] |
| 3 | One host is **never dispatched concurrently with itself**, but the thread that dispatches it can change from call to call. | A vehicle's `VehicleUpdateState` belongs to exactly one bubble (`VehicleUpdateState.cs:132`, `PhysicsBubble.cs:310-317`); a bubble is stepped by one job at a time (`VehicleUpdateTask.cs:716-795`). *Inferred* from the job structure, not from an explicit assertion in the code.[^vehicle-update-state][^vehicle-update-task] |
| 4 | While dispatches run, the **main thread is concurrently drawing UI and rendering** — including the kFS editor, debug window, the `Workbench.Frame` hook and every `kfs …` console command. | `Universe.ExecuteNextVehicleSolvers` only *starts* the job (`Universe.cs:2277-2290`, `Program.cs:2592`); the UI is drawn afterwards in the same `OnFrame` (`Program.cs:2633-2647`); the job is only awaited at the top of the next frame (`Program.cs:2528`).[^universe][^program] |
| 5 | `KfsIntegration.BeforeInputEvents` runs **on the main thread, in the per-frame window where no vehicle solver job is running**: after the previous job has been awaited and its results applied, after `Glfw.PollEvents()`, immediately before `InputEvents.ApplyInputEvents()`, and before the next job is started. | `Program.PrepareFrame`: `VehicleSolver.Wait()` `:2528` → `Universe.ApplyVehicleSolvers()` `:2537` → `Glfw.PollEvents()` `:2549` → `KfsIntegration.BeforeInputEvents()` `:2551` → `InputEvents.ApplyInputEvents()` `:2552` → `Universe.ExecuteNextVehicleSolvers` `:2592`.[^program] |
| 6 | In a dispatch the vehicle is a **read-only snapshot source**; only the worker-side copies (`VehicleUpdateData`, the `StateUpdater`s inside `FlightComputerOutput`, the worker's own `FlightComputer` copy) may be written. Results reach the live `Vehicle` at the next frame's `ApplyVehicleSolvers`. | Field is literally named `ReadOnlyVehicle` (`VehicleUpdateState.cs:14`); `VehicleUpdateState.FlightComputer => UpdateData.NewFlightComputer` (`:159`), copied from the live one in `VehicleUpdateData.Prepare` (`VehicleUpdateData.cs:84-103`) and copied back in `Vehicle.UpdateFromTaskResultsUnsynchronized` (`Vehicle.cs:2799-2801`).[^vehicle-update-state][^vehicle-update-data][^vehicle] |
| 7 | Anything that must mutate live game objects (part tree, staging, burn plan, `InputEvents` buffers, `Vehicle` fields) has to be **marshalled to the main thread** — kFS does this with the `KfsServices` `MainWork` queue drained from `BeforeInputEvents`. | `KfsServices.Queue` / `MainThread` (`KfsServices.cs:543-624`), called from `KfsIntegration.BeforeInputEvents` (`KfsIntegration.cs:158-176`).[^kfs-services][^kfs-integration] |

# The call chain

```text
main thread, Program.OnFrame → Program.PrepareFrame            Program.cs:2610, 2511
  JobSystems.VehicleSolver.Wait()                              :2528   (busy-wait until the previous job is idle)
  Universe.ApplyVehicleSolvers()                               :2537   → VehicleUpdateTask.ApplyResultsToMainThread()
  Glfw.PollEvents()                                            :2549
  KfsIntegration.BeforeInputEvents()                           :2551   ← kFS main-thread hook
  InputEvents.ApplyInputEvents()                               :2552
  Universe.ExecuteNextVehicleSolvers(dtPlayer, jobSimStep)     :2592
    Universe.PrepareVehicleWorkers(simStep)                    Universe.cs:2292   Vehicle.PrepareWorker for every vehicle
    _vehicleUpdateTask.PrepareFrame(orphans, simStep)          :2283
    JobSystems.VehicleSolver.Queue(_vehicleUpdateTask, 1)      :2284
    JobSystems.VehicleSolver.ExecuteJobs()                     :2288   returns immediately

thread-pool thread (JobRunner.DoWork, priority Highest)        JobRunner.cs:201-217
  VehicleUpdateTask.Run()                                      VehicleUpdateTask.cs:274
    AssignOrphanVehicles, MergeBubbles, bubble.Prepare(…)      :286-297
    BuildContactIslands()                                      :298
    ParallelBatch.Execute(this, _islandJobs[0.._islandCount])  :300-303   ← fan-out to VehicleWorker threads

VehicleWorker{i} thread or the awaiting solver thread
  ContactIsland.Execute                                        :60-83
    single bubble, no contacts:  bubble.RunStep()              :68      → BeginStepFrame, StepFrameTo, EndStepFrame
    otherwise:                   task.RunIsland(ref island)    :73      → BubbleStepSegmentJob → bubble.StepFrameTo
      PhysicsBubble.StepFrameTo(targetTime)                    PhysicsBubble.cs:774
        1 vehicle  → ApplySingleVehicleMotion                  :1494    RunFlightComputer at :1505
        2+ vehicles→ StepFullPhysicsTo → FullPhysicsPreStep    :2033    RunFlightComputer at :2044
                                       → FullPhysicsStepUntil  :1621    RunFlightComputer at :1646
          PhysicsBubble.RunFlightComputer(vehicleState)        :2077
            KfsDispatch.TryComputeControl(vehicleState, in nav, ref output)      :2081
              KfsVehicleHost.Dispatch → DispatchCore → VehicleComputer.Dispatch<StepSampler>
            if it returned false: vehicleState.FlightComputer.ComputeControl(…)   :2084   (stock path)
```

`RunFlightComputer` is `private static` and is the only caller of `KfsDispatch.TryComputeControl`.[^physics-bubble]
`FlightComputer.ComputeControl` has exactly two callers in the whole game: `PhysicsBubble.cs:2084` (stock path) and
`KfsVehicleHost.cs:551` (kFS pilot mode).[^kfs-vehicle-host]

# Thread inventory

| Thread | How it is created | What kFS code runs on it |
| --- | --- | --- |
| **Main thread** | The application loop that calls `Program.OnFrame`. *Inferred* to be the process main thread from `Profiler.MainThread.*` tags, `Glfw.PollEvents()` and ImGui calls inside `PrepareFrame`/`OnFrame` (`Program.cs:2511-2710`).[^program] | `KfsIntegration.RegisterCommands`, `KfsIntegration.BeforeInputEvents` (→ `KfsVehicleHost.BeginFrame`, `ReleaseDead`, every `KfsServices.MainThread()`), all UI (`FrameHook`, `KfsDebugWindow`, `KfsEditorWindow`, `KfsReferenceWindow`), all console commands, `KfsVehicleHost.For` (host creation), `KfsVehicleHost.HandBackAtFlagOff`, `KfsServices.HandBackPilot`. |
| **VehicleSolver runner** | `JobSystems.VehicleSolver = new JobScheduler(1u, "VehicleSolver")` (`JobSystems.cs:40`) — one `JobRunner`; each frame `ExecuteJobs` posts the runner to the .NET thread pool (`JobRunner.cs:115`) and `DoWork` sets `Thread.CurrentThread.Priority` to the scheduler's priority (`JobRunner.cs:206`), which is `Highest` (`JobSystems.cs:52`).[^job-systems][^job-runner] | `VehicleUpdateTask.Run()`. It also executes island / bubble jobs itself while it waits for a batch (see below), so a dispatch can run here. |
| **VehicleWorker0..N-1** | `JobSystems.VehicleWorkerPool = new DynamicWorkerPool((int)(num - 1), "VehicleWorker")` with `num = max(round(GameSettings.GetOrbitSolverCount() * 0.75), 1)` (`JobSystems.cs:36`, `:42`). Each worker is a dedicated `new Thread(...)`, `IsBackground = true`, `Priority = ThreadPriority.Highest`, named `VehicleWorker{i}` (`DynamicWorkerPool.cs:130-140`).[^job-systems][^dynamic-worker-pool] | Island jobs, per-bubble step jobs, hence `KfsDispatch.TryComputeControl` and everything below it. |

How a `ParallelBatch` distributes work:[^parallel-batch][^dynamic-worker-pool]

- `ParallelBatch.Execute` posts a job to the pool's ring buffer (`ParallelBatch.cs:31`, `DynamicWorkerPool.cs:163-186`). If the ring is full the job is executed **inline on the posting thread** (`DynamicWorkerPool.cs:173-176`).
- `ParallelBatch.Dispose()` calls `AwaitBatch`, in which the **awaiting thread claims and executes jobs of its own batch** while it waits (`DynamicWorkerPool.cs:189-204`).
- With `OrbitSolvers` set so that the pool has zero workers (`num - 1 == 0`), everything runs on the solver runner thread.

So the dispatch thread is "whichever of {solver runner, `VehicleWorker{i}`} executed the island or bubble-segment job". Do not use
thread identity or `[ThreadStatic]` state that must survive between two dispatches of the same host.

# Parallelism granularity

| Level | Parallel? | Evidence |
| --- | --- | --- |
| Contact islands (groups of bubbles that might touch this frame) | Yes — one job per island | `VehicleUpdateTask.cs:300-303`[^vehicle-update-task] |
| Bubbles inside a multi-bubble island | Yes — `BubbleBeginStepJob`, `BubbleStepSegmentJob`, `BubbleEndStepJob`, `BubblePostWorkJob` are posted per bubble | `VehicleUpdateTask.cs:716-795`[^vehicle-update-task] |
| Flight computers (and so kFS dispatches) of the vehicles **inside one bubble** | **No** — sequential `for` loops on the bubble's thread | `PhysicsBubble.cs:1642-1652`, `:2036-2047`, `:1505`[^physics-bubble] |
| Per-vehicle module updates and unconstrained motion inside one bubble | Yes when the bubble has 2+ vehicles (`ForEachVehicleParallel`, threshold 2) — but the flight computer is **not** part of these jobs | `PhysicsBubble.cs:1582-1598`, `:1881-1886`, `:2544-2551`[^physics-bubble] |

A bubble can be stepped in several segments per frame (`RunIsland` loops until every bubble reaches the frame's target time,
`VehicleUpdateTask.cs:730-770`), each segment possibly on a different thread; a mid-step bubble merge forces an immediate
flight-computer pass by setting `_nextFlightComputerTime = _intraStepTime` (`PhysicsBubble.cs:812-818`).[^vehicle-update-task][^physics-bubble]

# What `VehicleUpdateState` tells you about reading and writing

`KfsDispatch.TryComputeControl(VehicleUpdateState vehicleState, ref readonly FlightComputerNavigation nav, ref FlightComputerOutput output)`
receives the worker-side view of one vehicle.[^kfs-dispatch]

| Object reachable in a dispatch | Status | Notes |
| --- | --- | --- |
| `vehicleState.ReadOnlyVehicle` (the live `Vehicle`) | **Read only.** | The main thread may be reading it for UI/render at the same time, and no lock protects it. kFS reads `KfsHost`, `IsControllable`, `Id`, `Parts.Modules.Get<…>()`, `Parts.SequenceList.ActiveSequence`, `KinematicMeasurements`, `Helm2Body` from it on the worker thread.[^kfs-dispatch][^kfs-vehicle-host][^kfs-services] The part tree is only restructured on the main thread in the sync window, so these reads are stable for the duration of a job. *Inferred* from where `Split`/`MergeFrom`/`UpdateVehicleConfiguration` are called, not asserted by the code. |
| `vehicleState.GetReadOnlyProps()` / `GetReadOnlyEnvironment()` / `GetReadOnlyStates()` | Read. | Return the worker's new value if one was written this step, else the live vehicle's (`VehicleUpdateState.cs:201-250`).[^vehicle-update-state] |
| `vehicleState.GetNewProps()` / `GetNewEnvironment()` / `GetNewStates()` / `UpdateData.*` | Worker-writable copies. | Published to the vehicle by `Vehicle.UpdateFromTaskResultsUnsynchronized` (`Vehicle.cs:2711-2814`).[^vehicle] kFS itself does not write these. |
| `vehicleState.FlightComputer` | Worker-writable **copy** (`UpdateData.NewFlightComputer`). | Refreshed from `Vehicle.FlightComputer` at the start of each frame's job (`VehicleUpdateData.cs:100`) and copied back with `FlightComputer.CopyFrom` afterwards (`Vehicle.cs:2799`). `KfsServices.Apply` writes attitude and burn settings here.[^vehicle-update-data][^vehicle] `Burn`, `BurnPlan` and `VehicleConfig` are copied **by reference** (`FlightComputer.cs:321-325`), so those objects are shared between the two copies. |
| `output.Thrusters` / `output.Engines` / `output.Gimbals` (`ModuleStateful<…>.StateUpdater`) | Worker-writable per-module state buffers. | `GetNewStates()` lazily copies the live state list into a buffer (`ModuleStateful.cs:689-701`); written back in bulk by `Parts.UpdateFromTaskResults` (`Vehicle.cs:2801`). This is how kFS commands thrusters, engines and gimbals — see [write-back](/integration/write-back.md). |
| `vehicleState.ManualControlInputs` | Read (a struct snapshot). | Copied from `Vehicle._manualControlInputs` in `Vehicle.PrepareWorker` (`Vehicle.cs:2698`).[^vehicle] |
| `InputEvents.*Buffer`, `Program.*`, ImGui, `TimedAlert`, `SequenceList.ActivateNextSequence`, `Vehicle.SetEnum`, `Vehicle.Split` … | **Do not touch from a dispatch.** | `InputEvents.TypedBuffer<T>` is a plain array + count with no synchronisation (`InputEvents.cs:1010-1061`).[^input-events] kFS routes every such action through the main-thread queue. |

`Universe.GetElapsedTime()` is the *published* time (`_lastSimStep.NextTime`, `Universe.cs:2540-2543`), not the dispatch instant; in a
dispatch use `nav.Time` (kFS passes `(long)nav.Time.Nanoseconds` to the computer, `KfsVehicleHost.cs:446`, `:512`).[^universe][^kfs-vehicle-host]

# The main-thread sync window and `BeforeInputEvents`

`Program.PrepareFrame` (`Program.cs:2511-2608`), in order:[^program]

| Line | Call | Meaning for kFS |
| --- | --- | --- |
| 2522-2531 | `NearestOrbitAndPerformanceWorker.Wait()`, `OrbitSolvers.Wait()`, **`VehicleSolver.Wait()`**, `ClothSolvers.Wait()` | After `:2528` no dispatch is running. |
| 2537 | `Universe.ApplyVehicleSolvers()` | Worker results (including the worker `FlightComputer` copy and module states) are copied into the live vehicles; docking, destruction and part-failure events are applied (`VehicleUpdateTask.cs:234-267`, `PhysicsBubble.cs:1113-1165`). Vehicles may be disposed here.[^vehicle-update-task][^physics-bubble] |
| 2549 | `Glfw.PollEvents()` | Raw input callbacks fire. |
| **2551** | **`KfsIntegration.BeforeInputEvents()`** | Frame counter, dead-host release, every host's `Services.MainThread()`. |
| 2552 | `InputEvents.ApplyInputEvents()` | Applies the buffered game input events — including the `BurnUpdateBuffer` entries and `IActivate` staging events that `Services.MainThread()` just produced (`InputEvents.cs:1186-1215`).[^input-events] Requested docking, undocking and decoupling happen here too — see "Where a vehicle's structure changes". |
| 2565 | `vehicle.FlightComputer.RaisePendingAlerts(vehicle)` | Stock auto-burn completion alerts. |
| 2587 | `Universe.GetJobSimStep(dtPlayer)` | Chooses the next simulation step. |
| 2592 | `Universe.ExecuteNextVehicleSolvers(dtPlayer, jobSimStep)` | Next job starts; from here on dispatches may be running again. |

`KfsIntegration.BeforeInputEvents()` itself (`KfsIntegration.cs:158-176`):[^kfs-integration]

```csharp
public static void BeforeInputEvents()
{
    if (KfsPrototype.Enabled)
    {
        KfsVehicleHost.BeginFrame();          // Interlocked.Increment(ref _frame)
        KfsVehicleHost.ReleaseDead();         // prune AllHosts under lock
        KfsVehicleHost[] array;
        lock (KfsVehicleHost.AllHosts) { array = KfsVehicleHost.AllHosts.ToArray(); }
        Array.Sort(array, (a, b) => string.CompareOrdinal(a.Id, b.Id));
        foreach (var host in array) host.Services.MainThread();
    }
}
```

- It does nothing when the feature flag is off.
- Hosts are processed in ordinal order of `Vehicle.Id`, so main-thread effects are deterministic across hosts.
- There is **no `try`/`catch`**: an exception thrown by `MainThread()` propagates into `Program.PrepareFrame`. (A dispatch-side exception,
  by contrast, is caught — see "Exceptions".)
- It runs every frame, **including while the simulation is paused** (it is not conditional on simulation speed).

Writes to `Vehicle.FlightComputer` only stick when made in this window: any write made later in the frame (UI phase) is overwritten at the
next `ApplyVehicleSolvers` by `FlightComputer.CopyFrom(updateData.NewFlightComputer)` (`Vehicle.cs:2799`). This is why the stock UI buffers
flight-computer clicks into `InputEvents.FlightComputerInputBuffer` (`GaugeButtonFlightComputer.cs:141`, `Vehicle.cs:3774-3787`) instead of
writing the flight computer directly. *Inferred:* `KfsServices.HandBackPilot`, which writes `vehicle.FlightComputer` directly and is reached
from the `kfs off` console command during the UI phase (`KfsConsoleCommands.cs:244-258`), is subject to this overwrite for the fields
`CopyFrom` covers; its `vehicle.SetEnum(VehicleEngine.MainShutdown)` writes a `Vehicle` field and is not affected.[^vehicle][^kfs-services]

# What a dispatch reads: live fields and committed state

The order above has a consequence for anything that reads part state inside a dispatch — stock
kFS sampling, or a mod's service that answers a query.

Inside the solver job the flight-computer pass comes **first** and the module update **second**.
In a single-vehicle bubble `ApplySingleVehicleMotion` calls `RunFlightComputer` and only then
`vehicleState.UpdateModules(window)`; in full physics a substep starts with the flight-computer
loop (when one is due) and queues the `UpdateModulesJob`s later in the same substep.[^physics-bubble-order] The module
update writes its results to worker-side buffers (`ModuleUpdateContext.Updaters`), not to the
committed state lists of the part tree (`PartTree.States`); those are written once per frame, when the main thread's
`Universe.ApplyVehicleSolvers()` runs `Vehicle.UpdateFromTaskResultsUnsynchronized` →
`PartTree.UpdateFromTaskResults` → `Updaters.UpdateBulkStates()`.[^part-tree-commit]

So within one frame:

```text
main     ApplyVehicleSolvers()        committed state := what the LAST job computed
main     BeforeInputEvents()          main-thread hooks may write live fields (a switch, a goal, a template)
main     ApplyInputEvents()
main     ExecuteNextVehicleSolvers()  the next job starts
worker     dispatch (program ticks)   reads: live fields as just written; committed state from the last job
worker     UpdateModules              computes new state from the live fields — committed at the NEXT frame
```

This concerns code that reaches a module through the **live part tree** — which is all a patch on
a `VehicleComputer` method has: the computer, and from it the vehicle. (Code that holds the
worker-side `VehicleUpdateState` can read that job's own newer copies, see the table under "What
`VehicleUpdateState` tells you about reading and writing".) Two kinds of value therefore come back
at different times:

| Kind | Examples | When a write made in the sync window is visible to a dispatch |
| --- | --- | --- |
| A field of a live object | `PowerConsumer.LightIsActive`, `KeyframeAnimationModule.TimeGoal`, the fields of a `LightModule.Template` | from the first dispatch of the job started in the same frame |
| Committed module state | `KeyframeAnimationModule.State` (`TimeCurrent`, `DeploymentState`), `PowerConsumerState.Active` | one frame later: the job that first sees the new field computes the new state, and the following frame's `ApplyVehicleSolvers` commits it |

- `KeyframeAnimationModule.UpdateModules` reads `module.TimeGoal` — the live field — and advances
  `State.TimeCurrent` in the worker-side buffer.[^keyframe-animation-module-update]
- A light's lit test is **asymmetric**. `LightModule.IsActive` is
  `!Parent.FullPart.IsLightSwitchedOff()`, and `Part.IsLightSwitchedOff()` returns "off" straight
  from the field when `LightIsActive` is false, but when it is true asks
  `PowerConsumer.IsSwitchedOn()`, which reads the committed `PowerConsumerState.Active`. A light
  switched **off** in the sync window reads not active at once; a light switched **on** reads
  active only after the next job has run `PowerConsumer.UpdateModules` and its result was
  committed.[^part-light-off][^power-consumer-state]
- Several dispatches of one host in one frame (a tick rate above the frame rate, or time warp:
  one per physics substep) all read the **same** committed state, because nothing commits between
  them. A program polling an animation's position at time warp sees it move in steps of one frame.

`Inferred:` the "one frame later" figures follow from the order of the calls read above; they were
not observed in the running game. purrTTY's offline simulator steps its simulated parts in the
same order (step, then main-thread hook, then dispatch), and its tests show these offsets for an
animation; see `purrTTY.Kfs.Tests/Runtime/FrameOrderTests.cs` in this repository.

# Where a vehicle's structure changes

Parts leave and join a vehicle only on the main thread, in the window where no solver job runs —
but at two different points of it, one before `BeforeInputEvents` and one after:

| What | Where it is applied | Relative to `BeforeInputEvents` |
| --- | --- | --- |
| Docking detected by the physics step (contact), vehicle destruction, part failure, ground impact | `Universe.ApplyVehicleSolvers()` → `VehicleUpdateTask.ApplyResultsToMainThread()` → `PhysicsBubble.ApplyRenderEventsToVehicles()`; `DockingEvent.Apply` calls `DockingPort.Dock`[^physics-bubble-events][^docking-event] | **before** |
| Docking and undocking requested from a docking port's context menu | queued in `InputEvents.VehicleDockingInputBuffer`; applied by `InputEvents.ApplyInputEvents()` (`VehicleDockingInputData.Apply` → `DockingPort.Dock` / `Undock`)[^docking-port][^input-events-structure] | **after** |
| Decoupling (staging, including a program's `stage` request, which stock kFS turns into `IActivate` events in `Services.MainThread()`) | `InputEvents.ApplyInputEvents()` → `ApplyIActivateBatched()` → `Decoupler.Decouple(Vehicle)`[^input-events-structure] | **after** |

A `BeforeInputEvents` hook therefore sees the result of a contact docking of this frame, and does
not yet see a decoupling or a menu docking of this frame; in both cases the next dispatch sees the
changed part tree, and no dispatch ever runs while it changes.

# The UI phase runs concurrently with dispatches

After `PrepareFrame`, `Program.OnFrame` draws ImGui (`Program.cs:2633-2647`). `OnDrawUiFrame` calls
`ImGuiWindow.DrawAllStaticWindows(MainViewport)` (`Program.cs:3552`), which draws kFS's `FrameHook` (→ `Workbench.Frame`), `KfsDebugWindow`,
the editor and the reference window; `OnDrawUiConsole` (`:2647`) draws the console, which executes a typed command immediately
(`ConsoleWindow.cs:546`).[^program][^console-window]

Because the solver job is typically still running, kFS UI code never calls into a `VehicleComputer` directly. It uses:

- `HostComputer.Enqueue(Action<HostComputer>)` — a `ConcurrentQueue` drained by `DrainQueue()` at the top of the next
  `VehicleComputer.Dispatch` on the dispatch thread (`HostComputer.cs:23`, `:288-300`; `VehicleComputer.cs:455`). Load, start, stop,
  reset, kill, peek and poke all go through it (`Workbench.cs:816-819`, `:994-1001`, `:1009-1012`; `KfsConsoleCommands.cs:285-288`).[^host-computer][^vehicle-computer][^workbench]
- `HostComputer.Snapshot` — an immutable `MachineSnapshot` published with `Volatile.Write` from the dispatch thread
  (`HostComputer.cs:167-174`, `:933`).[^host-computer]

Consequence: **queued computer actions only take effect when the vehicle is next dispatched.** While the simulation is paused no dispatch
happens (see "Pause and time warp"), which is why the workbench prints
"(the vehicle's computer was not dispatched, as when the simulation is paused: this is its last snapshot)" (`Workbench.cs:1195`).[^workbench]

# When the flight-computer pass (and so the dispatch) happens inside a bubble step

Every `Vehicle` in the current system is prepared each frame (`Universe.PrepareVehicleWorkers`, `Universe.cs:2292-2306`) and belongs to
a bubble (orphans are assigned, `VehicleUpdateTask.cs:325-362`). The flight computer therefore runs for **every vehicle, controlled by the
player or not**; kFS looks only at `Vehicle.KfsHost`.[^universe][^vehicle-update-task]

| Situation | Where the pass runs | Frequency |
| --- | --- | --- |
| Bubble with one vehicle | `ApplySingleVehicleMotion` calls `RunFlightComputer` **once per step window, before the rails decision** (`PhysicsBubble.cs:1500-1505`). Navigation comes from analytic (orbit) states. | At least once per frame while simulation time advances — even for a vehicle on rails. |
| Same vehicle, taken off rails this window | `StepFullPhysicsTo` → `FullPhysicsBeginFrame` → `FullPhysicsPreStep`: runs the pass only for vehicles whose `FlightComputerOutput.NextWakeupDeltaTime <= 0.0` (`:2041-2045`) — which the single-vehicle path has already cleared — then `FullPhysicsStepUntil`. | Then at every requested wake time. |
| Bubble with 2+ vehicles | `BeginStepFrame` zeroes every `NextWakeupDeltaTime` (`:768-771`), so `FullPhysicsPreStep` runs every flight computer at the start of the frame; `FullPhysicsStepUntil` re-runs **all** of them whenever `IntraStepTime >= _nextFlightComputerTime` (`:1639-1664`). | Start of frame, then at the earliest wake time requested by any member. |

In full physics the next pass time is computed like this (`PhysicsBubble.cs:1653-1663`, `WakeRequest` at `:72-127`):[^physics-bubble]

```text
lattice   = floor((t + minDt) / minDt) * minDt        // minDt = 1 / GameSettings.Current.Simulation.MaxFlightComputerRate
stockTime = max(lattice, t + min stock-requested wake)
next      = (t + min kFS-requested wake < stockTime) ? t + min kFS wake  (limit = PhysicsStepLimit.KfsTick)
                                                     : stockTime         (limit = the stock source)
```

`MaxFlightComputerRate` defaults to `10f` (`GameSettings.cs:1164`), so stock wakeups are quantised to a 0.1 s lattice; a wakeup whose
`NextWakeupSource` is `PhysicsStepLimit.KfsTick` is tracked separately (`KfsDeltaTime`, `KfsVehicleIdx`) and **bypasses that rate limit**.[^game-settings][^physics-bubble]
The physics substep is then cut to end at `next` (`:1665-1671`), so a running kFS computer produces one physics substep per tick period.

# Wakeups: `RequestWakeup`, `PhysicsStepLimit.KfsTick`, rails

`FlightComputerOutput` (`FlightComputerOutput.cs`):[^flight-computer-output]

| Member | Meaning |
| --- | --- |
| `double NextWakeupDeltaTime` (`:7`), `PhysicsStepLimit NextWakeupSource` (`:9`) | Earliest requested next flight-computer pass, relative to now. |
| `void RequestWakeup(double deltaTime, PhysicsStepLimit source)` (`:33-40`) | Keeps the minimum; the source follows the minimum. |
| `bool AnyActuatorCommanded` (`:11`) | Forces the vehicle off rails this step. |
| `void Clear()` (`:43-50`) | Clears wakeup, `AnyActuatorCommanded`, `ManualTranslationFired`, `EngineBurnDuration`, `EngineThrottle`. |

What kFS sets (`KfsVehicleHost.DispatchCore`, `KfsVehicleHost.cs:488-616`):[^kfs-vehicle-host]

| Dispatch outcome | Output |
| --- | --- |
| Not under kFS control, or `Fallback` (vehicle not controllable) | Returns `false`; the stock `ComputeControl` then owns the output. No kFS wakeup is requested. |
| Pilot mode | Stock `ComputeControl` fills the output; then, unless the computer is `Halted`, `output.RequestWakeup(dispatchResult.NextWakeSeconds, PhysicsStepLimit.KfsTick)` and `output.AnyActuatorCommanded = true` (`:554-558`). |
| Full control, quiescent (`Computer.IsQuiescent`) | `output.Clear()` and return `true`: no wakeup, no actuator flag — the vehicle may go on rails (`:568-572`). |
| Full control, otherwise | `output.Clear()`, `RequestWakeup(NextWakeSeconds, KfsTick)`, `AnyActuatorCommanded = true` (`:568-574`). |

`NextWakeSeconds` is the time to the next tick on the computer's grid, or one tick period while a `stage` request is pending
(`VehicleComputer.cs:516-517`).[^vehicle-computer]

Rails consequences (`PhysicsBubble.cs:1507-1534`, `:1672-1716`):[^physics-bubble]

- A single vehicle is taken off rails when `NextWakeupDeltaTime < window.DeltaTime` or `AnyActuatorCommanded` — so **a non-quiescent kFS
  computer keeps its vehicle in full physics every frame**, even with no thrust.
- `IsQuiescent` is `State == Halted`, more than 1 s since the last latched command, and no non-one-shot command field non-zero
  (`VehicleComputer.cs:102-112`, `:269-286`).[^vehicle-computer]
- *Inferred:* in pilot mode, if the stock computer requests an earlier wake than kFS, the output's source is the stock one and the pass is
  quantised to the stock lattice, so a tick can be up to `1 / MaxFlightComputerRate` late (it is then counted in `LateTicks`).
- `KeyframeAnimationModule.AnyAnimating(vehicleState.ReadOnlyVehicle.Parts.States)` also forces a vehicle off rails (`:1531-1534`,
  `:1680`) — relevant to any extension that triggers part animations.

# The frame budget

| Piece | Behaviour | Where |
| --- | --- | --- |
| `KfsVehicleHost.FrameBudgetMs` (`public static double { get; set; }`) | Host time one vehicle computer may use per frame. `0`/negative/NaN = no limit. Default value of the property is `0`; `KfsIntegration.RegisterCommands(Program)` sets it to `4.0` (`DEFAULT_FRAME_BUDGET_MS`). `kfs budget [ms \| off]` changes it (0 < ms ≤ 1000). | `KfsVehicleHost.cs:53`, `:99`; `KfsIntegration.cs:151`; `KfsConsoleCommands.cs:292-327`[^kfs-vehicle-host][^kfs-integration][^kfs-console-commands] |
| `KfsVehicleHost.BeginFrame()` | `Interlocked.Increment(ref _frame)` — once per main-thread frame, from `BeforeInputEvents`, only while the flag is on. | `KfsVehicleHost.cs:466-469` |
| `TickDeadlineAt(long now)` (private) | When `_frame` differs from the host's `_budgetFrame`, resets `_frameHostTicks` to 0. Returns `now + max(0, budgetTicks − _frameHostTicks)` where `budgetTicks = FrameBudgetMs * Stopwatch.Frequency / 1000`; `long.MaxValue` when there is no budget. | `:471-486` |
| `Dispatch` | Sets `Computer.TickDeadline = TickDeadlineAt(start)` before `DispatchCore`, and adds the whole dispatch's elapsed stopwatch ticks to `_frameHostTicks` afterwards. | `:442-464` |
| `VehicleComputer.TickDue(long tNs)` | False once `Stopwatch.GetTimestamp() >= TickDeadline`. | `VehicleComputer.cs:430-441`[^vehicle-computer] |
| `VehicleComputer.Dispatch` tick loop | Before each tick: past the deadline → `SkipPastDeadline(tNs)` (grid jumps past now, `SkippedTicks`/`DeadlineSkippedTicks` grow, event "skipped N tick(s): the computer had used its host time for this frame; lower time warp or make the program cheaper") and the loop ends. | `VehicleComputer.cs:477-483`, `:543-552` |

Properties worth knowing:

- The budget is **per host, per main-thread frame**, cumulative over all of that host's dispatches in the frame (there can be many: one
  per physics substep).
- What is charged is the **whole dispatch** — sampling, service handling and (in pilot mode) the stock `ComputeControl` — not only VM time.
  Code a mod adds inside the dispatch path is charged to the program's budget too.
- The budget only gates *starting* a tick; a tick in progress is not interrupted by it (the VM has its own cycle budget, see
  [cost model](/vm/cost-model-and-budgets.md)).
- Independently, ticks more than `MAX_CATCH_UP_NS = 1 000 000 000` ns late are skipped (`SkipOverdueTicks`, `VehicleComputer.cs:59`,
  `:520-541`).

# Pause and time warp

| Condition | Effect | Evidence |
| --- | --- | --- |
| Simulation speed 0 (pause; also forced while the vehicle editor is open, `Program.cs:2576-2581`) | `GetJobSimStep` yields `DeltaTime = 0`; `PhysicsBubble.StepFrameTo` returns immediately when `targetTime <= StepTime`, so **no flight-computer pass and no kFS dispatch** happen. `BeforeInputEvents` still runs. `HostComputer.Enqueue`d actions wait. | `Universe.cs:2766-2789`; `PhysicsBubble.cs:774-779`[^universe][^physics-bubble] |
| Time warp | The frame's simulation step is `dtPlayer × achievedSpeedFraction × SimulationSpeed`, capped by `_maxStepSimSeconds` (derived from the last solver time so the solver fits in `0.9 / MinTargetFrameRate`). More ticks fall due per frame; each needs a flight-computer pass and a physics substep. Ticks beyond the frame budget are skipped. | `Universe.cs:2061-2101`, `:2766-2789`; `VehicleComputer.cs:477-483` |
| | *Inferred:* a running program therefore lowers the achievable warp for its bubble; the frame-statistics overlay attributes the limit as "kFS Tick" (`PhysicsStepLimitEx.ToPlayerString`, `PhysicsStepLimitEx.cs:24`; `PhysicsBubble.cs:832-841`, `:861-883`).[^physics-step-limit] | |
| Simulation speed > 30 | `Vehicle.PrepareWorker` clears held player input for the controlled vehicle (`Vehicle.cs:2670-2673`); kFS itself has no warp check. The current speed is exposed to programs as `MSG_FC_SETTINGS.sim_speed` (see [host sampler](/integration/host-sampler.md)). | [^vehicle] |

# Vehicles without control, without a host, on rails

| Case | What happens | Evidence |
| --- | --- | --- |
| Flag off | `TryComputeControl` returns `false` immediately; stock computer. | `KfsDispatch.cs:9-12`[^kfs-dispatch] |
| Vehicle has no host (`Vehicle.KfsHost == null`) | Returns `false`; stock computer. Hosts exist only for vehicles the workbench has targeted (see [lifecycle](/integration/lifecycle-and-persistence.md)). | `KfsDispatch.cs:13-18` |
| Host exists, computer not under kFS control (no image, `Standby`, `Idle`, host fault, kill switch) | `VehicleComputer.Dispatch` still **drains its action queue**, then returns `DispatchResult.NotControlled`; stock computer. | `VehicleComputer.cs:451-461`, `:77-90` |
| Vehicle not the player's controlled vehicle | No difference: kFS never checks `Program.ControlledVehicle` in the dispatch path. | `KfsVehicleHost.cs:488-616` |
| `Vehicle.IsControllable == false` (no control module, probe core off/unpowered, crew incapacitated…) | `controllable: false` → the VM **still ticks**, but nothing is latched to actuators (`Fallback`); queued services are applied with `fallback: true`; `DispatchCore` returns `false` so the stock computer runs. | `KfsDispatch.cs:21`; `VehicleComputer.cs:463-472`, `:505-508`, `:517`; `KfsVehicleHost.cs:527-533`; `Vehicle.cs:704-718` |
| Vehicle on rails | The single-vehicle path still dispatches once per frame; a non-quiescent computer pulls it off rails (above). | `PhysicsBubble.cs:1500-1534` |

# Exceptions

- Anything thrown inside `KfsVehicleHost.Dispatch` is caught by `KfsDispatch.TryComputeControl` (`KfsDispatch.cs:19-27`), which calls
  `kfsHost.Fault(e, vehicleState.FlightComputer)` and returns `false`: the computer is halted with a host fault and the stock computer flies
  the vehicle. See [KfsVehicleHost](/integration/kfs-vehicle-host.md).[^kfs-dispatch]
- An exception that escapes a bubble job is caught by `IBubbleJob.Execute` / `ContactIsland.Execute` and marks the **whole bubble failed**
  for the frame (`IBubbleJob.cs:12-19`, `VehicleUpdateTask.cs:76-82`, `PhysicsBubble.cs:1303-1325`) — the kFS catch exists to avoid that.[^bubble-job][^vehicle-update-task][^physics-bubble]
- Exceptions on the main-thread side (`BeforeInputEvents`, `Services.MainThread()`) are not caught by kFS.

# Modding notes

- **Patches on anything under `KfsDispatch.TryComputeControl` run on solver/worker threads, possibly for several vehicles at once.**
  Static mod state touched there must be thread-safe; per-host state is safe without locks only if it is touched solely from that host's
  dispatch.
- **Never mutate live game objects from the dispatch path.** Reading module state of the live vehicle there is what kFS itself does; for
  any write (lights, animations, staging, part state) queue it and apply it in the main-thread sync window — the natural hook is a Harmony
  postfix on `KfsIntegration.BeforeInputEvents` (public static, parameterless, once per frame, before `InputEvents.ApplyInputEvents`). See
  [patch targets and seams](/integration/patch-targets-and-seams.md) and the queue template in
  [KfsServices (game side)](/integration/kfs-services-game-side.md).
- **The sync window is also the only place where a write to `Vehicle.FlightComputer` survives**, and the safe place to add to
  `InputEvents.*Buffer`s so the game applies them the same frame.
- UI-phase code (ImGui windows, console commands, StarMap menu callbacks drawn with the UI) runs **concurrently with dispatches**. Talk to a
  `VehicleComputer` only through `Computer.Enqueue(...)` and read it only through `Computer.Snapshot`.
- A mod exception inside the dispatch path does not crash the frame, but it **faults the player's computer** ("kFS host fault …") — catch
  your own exceptions. A mod exception inside a `BeforeInputEvents` postfix is not caught by anything in kFS.
- Extra work in the dispatch path consumes the 4 ms frame budget and can make the program skip ticks.
- A running computer forces full physics and a substep per tick for its bubble; an extension that keeps a program "alive" purely to poll
  game state has a real simulation cost. Animating parts have the same off-rails effect by themselves.
- Nothing dispatches while paused: an action that should work in pause (for example switching a light) must not depend on a dispatch
  draining a queue — only `BeforeInputEvents` keeps running.

[^physics-bubble]: PhysicsBubble.cs — line numbers given inline; key anchors: `WakeRequest` 72-127, `BeginStepFrame` 750-772, `StepFrameTo` 774-799, `ApplySingleVehicleMotion` 1494-1580, `ForEachVehicleParallel` 1582-1598, `FullPhysicsStepUntil` 1621-1905, `FullPhysicsPreStep` 2033-2048, `RunFlightComputer` 2077-2086.
[^vehicle-update-task]: VehicleUpdateTask.cs — `ContactIsland.Execute` 60-83, `ApplyResultsToMainThread` 234-267, `Run` 274-323, `RunIsland` 716-795.
[^universe]: Universe.cs — `ApplyVehicleSolvers` 2061-2101, `ExecuteNextVehicleSolvers` 2277-2290, `PrepareVehicleWorkers` 2292-2306, `GetElapsedTime` 2540-2543, `GetJobSimStep` 2766-2789.
[^program]: Program.cs — `PrepareFrame` 2511-2608, `OnFrame` 2610-2710, `OnDrawUiFrame` 3542-3575 (`DrawAllStaticWindows` 3552), `OnDrawUiConsole` 3530-3540.
[^job-systems]: JobSystems.cs:32-59.
[^job-runner]: JobRunner.cs:115 (`ThreadPool.UnsafeQueueUserWorkItem`), 149-154 (`Wait` busy-waits), 201-217 (`DoWork`).
[^dynamic-worker-pool]: DynamicWorkerPool.cs:122-141 (worker threads), 163-186 (`ExecuteBatched`), 189-204 (`AwaitBatch`), 241-290 (`WorkerLoop`).
[^parallel-batch]: ParallelBatch.cs:29-56.
[^bubble-job]: IBubbleJob.cs:10-20; BubbleStepSegmentJob.cs:9-12.
[^vehicle]: Vehicle.cs — `KfsHost` 567, `IsControllable` 704-718, `PrepareWorker` 2668-2699, `UpdateFromTaskResultsUnsynchronized` 2711-2814 (`FlightComputer.CopyFrom` 2799, `Parts.UpdateFromTaskResults` 2801), `ReleaseFlightComputer` 3770-3788.
[^vehicle-update-state]: VehicleUpdateState.cs:14, 18, 30-32, 132, 159, 201-250, 302-312.
[^vehicle-update-data]: VehicleUpdateData.cs:46, 72, 84-103.
[^flight-computer-output]: FlightComputerOutput.cs:5-50.
[^input-events]: InputEvents.cs:1010-1061 (`TypedBuffer<T>`), 1067 (`BurnUpdateBuffer`), 1186-1215 (`ApplyInputEvents`).
[^game-settings]: GameSettings.cs:1161-1164.
[^kfs-dispatch]: KfsDispatch.cs:5-28.
[^kfs-vehicle-host]: KfsVehicleHost.cs — `Dispatch` 442-464, `BeginFrame` 466-469, `TickDeadlineAt` 471-486, `DispatchCore` 488-616.
[^kfs-integration]: KfsIntegration.cs:146-176.
[^kfs-services]: KfsServices.cs — `HandBackPilot` 279-284, `Queue` 543-549, `MainThread` 551-624.
[^vehicle-computer]: VehicleComputer.cs — `UnderKfsControl` 77-90, `IsQuiescent` 102-112, `TickDue` 430-441, `Dispatch<TSampler>` 449-518, `SkipOverdueTicks` 520-541, `SkipPastDeadline` 543-552.
[^host-computer]: HostComputer.cs:23, 167-174, 288-300, 933.
[^workbench]: Workbench.cs:809-822, 991-1020, 1168-1221.
[^console-window]: ConsoleWindow.cs:546.
[^kfs-console-commands]: KfsConsoleCommands.cs:244-258 (`SetFlag`), 271-290 (`Kill`), 292-327 (`Budget`).
[^physics-step-limit]: PhysicsStepLimit.cs:3-19; PhysicsStepLimitEx.cs:10-27.
[^physics-bubble-order]: PhysicsBubble.cs — single vehicle: `RunFlightComputer` :1505, then `vehicleState.UpdateModules(window)` :1548 (surface) / :1569 (freefall); full physics: the flight-computer loop :1639-1652 (`RunFlightComputer` :1646) at the head of a substep, `UpdateModulesJob`s queued :1881-1886 (job body :30-45); `FullPhysicsBeginFrame` → `FullPhysicsPreStep` :1610-1616, :2033-2048 (`RunFlightComputer` :2044).
[^part-tree-commit]: PartTree.cs:1593-1600 (`UpdateFromTaskResults` → `updateData.Updaters.UpdateBulkStates()`), called from Vehicle.cs:2801.
[^keyframe-animation-module-update]: KeyframeAnimationModule.cs:162-214 (`UpdateModules`: `module.TimeGoal` compared with and applied to `current.State.TimeCurrent`).
[^part-light-off]: Part.cs:1551-1567 (`IsLightSwitchedOff`); LightModule.cs:60-70 (`IsActive`).
[^power-consumer-state]: PowerConsumer.cs:138-146 (`IsSwitchedOn` reads `Parent.Tree.PowerConsumers.States[StatesIdx].Active`), :235-272 (`UpdateModules` sets `State.Active` from `LightIsActive` and the power available).
[^physics-bubble-events]: PhysicsBubble.cs:1113-1165 (`ApplyRenderEventsToVehicles`: `DockingEvent`, `GroundImpactEvent`, `WaterSplashEvent`, `PartFailureEvent`, then destruction); VehicleUpdateTask.cs:234-267.
[^docking-event]: DockingEvent.cs:11-25.
[^docking-port]: DockingPort.cs:253-262 (Undock menu item), :377-386 (Dock menu item): both add to `InputEvents.VehicleDockingInputBuffer`.
[^input-events-structure]: InputEvents.cs — `ActivateOp.Decouple` :453, `IActivateInputData.Apply` → `Decoupler.Decouple(Vehicle)` :483-492; `VehicleDockingInputData` :524-570 (`Undock` :538-543, `Dock` :565); `ApplyInputEvents` :1186-1215 (`VehicleDockingInputBuffer.ApplyAll()` :1189, `ApplyIActivateBatched()` :1190).
