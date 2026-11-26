# Integration — the KSA-side kFS glue

How the game calls kFS and how kFS reaches back into the game: registration, the per-frame hook, the dispatch inside the physics step,
sampling, service effects, actuator write-back, and the lifetime of a vehicle's computer. Derived from the decompiled `KSA.Kfs`
namespace and its call sites in `KSA` for build 2026.11.25.5949.

## Start here

- [kFS game integration overview](game-integration-overview.md) — Every touch point between the KSA game code and kFS, the startup registration order, the object graph from KfsIntegration down to a vehicle's computer, and a per-frame and per-step walkthrough.
- [kFS dispatch and threading](dispatch-and-threading.md) — Which threads run the kFS dispatch, the main-thread hook and the UI, what each may read or mutate, and how wakeups, the frame budget, time warp and rails interact with a vehicle computer.

## The per-vehicle host

- [KfsVehicleHost](kfs-vehicle-host.md) — The per-vehicle kFS host object — its fields, construction, liveness, the Dispatch and DispatchCore control flow branch by branch, sampling, fault handling and static state, with exact signatures and visibility.
- [KfsControllerSet and layout changes](controller-set-and-layout-changes.md) — How a vehicle's thrusters, engines and gimbals get stable program-visible indices — ordering by InstanceId, the generated I/O layout, the frozen and current sets, and how elements go inert after staging or docking.
- [Host lifecycle and persistence](lifecycle-and-persistence.md) — When a KfsVehicleHost is created and when it dies, what happens to it on staging, docking, vehicle destruction, save loading and star-system changes, and what kFS state does and does not survive a save.

## Game state in, commands out

- [Host sampler — game sources of every host-published value](host-sampler.md) — Every value KfsVehicleHost and KfsHostSampler put into the host-published messages, the exact game expression each one comes from, and the conditions under which each group is filled.
- [KfsServices — game-side service effects](kfs-services-game-side.md) — How KfsServices turns drained service requests into game effects, detects pilot overrides, tracks burn handles across a dispatch, and marshals work to the main thread through its MainWork queue.
- [Write-back — how VM commands reach thrusters, engines and gimbals](write-back.md) — The full-control write-back path from a program's latched command block to the game's per-module controller state, including indexing, clamping and NaN handling, plus the KfsWriteBack helpers that reset the stock flight computer.

## Coexistence with the stock game

- [Interplay between kFS and the stock flight computer](flight-computer-interplay.md) — When the stock FlightComputer.ComputeControl still runs, how pilot mode and full control divide the work, which services need the stock computer, how the service override, burn modes and attitude modes behave, and what a handback does.
- [The KfsPrototype feature flag](feature-flag.md) — The single process-wide switch that gates kFS — how it is initialised and changed, every place that reads it, and exactly what turning it off and back on does to running vehicle computers.

## Extending

- [Patch targets and seams in the kFS game glue](patch-targets-and-seams.md) — The Harmony patch-target candidates in the KSA-side kFS glue with exact signatures, visibility, thread and patchability concerns, and the recommended seams for observing service requests, running per-frame main-thread code and keeping per-host state.
