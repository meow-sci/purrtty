---
type: Extension Point
title: Patch targets and seams in the kFS game glue
description: The Harmony patch-target candidates in the KSA-side kFS glue with exact signatures, visibility, thread and patchability concerns, and the recommended seams for observing service requests, running per-frame main-thread code and keeping per-host state.
resource: ksa-decomp:KSA.Kfs/KfsServices.cs
tags: [kfs, integration, extension, harmony, patching, threading]
sources:
  - id: kfs-integration
    resource: ksa-decomp:KSA.Kfs/KfsIntegration.cs
    title: KfsIntegration (decompiled)
  - id: kfs-prototype
    resource: ksa-decomp:KSA.Kfs/KfsPrototype.cs
    title: KfsPrototype (decompiled)
  - id: kfs-dispatch
    resource: ksa-decomp:KSA.Kfs/KfsDispatch.cs
    title: KfsDispatch (decompiled)
  - id: kfs-vehicle-host
    resource: ksa-decomp:KSA.Kfs/KfsVehicleHost.cs
    title: KfsVehicleHost (decompiled)
  - id: kfs-services
    resource: ksa-decomp:KSA.Kfs/KfsServices.cs
    title: KfsServices (decompiled)
  - id: kfs-console-commands
    resource: ksa-decomp:KSA.Kfs/KfsConsoleCommands.cs
    title: KfsConsoleCommands (decompiled)
  - id: vehicle-computer
    resource: ksa-decomp:KSA.Kfs.Host/VehicleComputer.cs
    title: VehicleComputer (decompiled)
  - id: host-computer
    resource: ksa-decomp:KSA.Kfs.Host/HostComputer.cs
    title: HostComputer (decompiled)
  - id: physics-bubble
    resource: ksa-decomp:KSA/PhysicsBubble.cs
    title: PhysicsBubble (decompiled)
  - id: program
    resource: ksa-decomp:KSA/Program.cs
    title: Program (decompiled)
  - id: input-events
    resource: ksa-decomp:KSA/InputEvents.cs
    title: InputEvents (decompiled)
  - id: imgui-window
    resource: ksa-decomp:KSA/ImGuiWindow.cs
    title: ImGuiWindow (decompiled)
  - id: offline-harness
    resource: /modding/offline-harness.md
    title: Offline harness experiments E1–E12
generated: { by: claude-code/claude-fable-5-2, at: 2026-11-25T21:47:37Z }
status: draft
ksa_build: "2026.11.25.5949"
---

# Scope and evidence

This document turns the facts in the other `/integration/` documents into a list of hook points. Signatures, visibility, callers and
threads are read from the decompiled code. Statements about **how Harmony or the JIT will treat a method** are general .NET/Harmony
knowledge, not something the decompiled code can show; they are marked *Unverified* and should be confirmed with a test patch against the
running build. That is why this document is `status: draft`. Three patches have since been applied and exercised against the real
`KSA.dll` outside the game (Harmony 2.4.2 on .NET 10) and are marked as verified where they appear: a postfix on
`GameTables.LoadEmbedded`, a postfix on `VehicleComputer.ApplyRequest` (#14) and a postfix on `VehicleComputer`'s explicit
`ISyscallHost.IssueResult` implementation with its `Span` parameters — see [the offline harness](/modding/offline-harness.md).[^offline-harness]
Nothing here has been run inside the game.

Threads are named as in [dispatch and threading](/integration/dispatch-and-threading.md): **main** (sync window or UI phase) and
**dispatch** (solver / `VehicleWorker` threads; concurrent across vehicles in different bubbles).

# No-patch seams (public API a mod can simply call)

| Seam | Declaration | Use |
| --- | --- | --- |
| Feature flag | `KfsPrototype.Enabled { get; set; }`, `KfsPrototype.OffCount`; `KfsConsoleCommands.Execute(string args = "")` | Gate mod work; turn kFS on/off properly with `Execute("on"/"off")`.[^kfs-prototype][^kfs-console-commands] |
| Find or create a host | `KfsVehicleHost.Find(Vehicle)`, `KfsVehicleHost.For(Vehicle)`, `Vehicle.KfsHost { get; set; }` | `For` is main-thread only (unsynchronised lazy create).[^kfs-vehicle-host] |
| Host liveness | `host.IsAlive` | Sweep mod state without patching. |
| Hardware indices | `host.Frozen` / `host.Current` (`KfsControllerSet`), `new KfsControllerSet(vehicle)` | Read the program's index ↔ module mapping. |
| Run code on the dispatch thread of one computer | `host.Computer.Enqueue(Action<HostComputer>)` | Executed at the start of that computer's next dispatch, before ticks; never while paused.[^host-computer] |
| Program-visible events and rejections | `host.Computer.Event(string)`, `host.Computer.RejectService(ServiceRequest, string)` | Write to the program's console log / count a rejected request. Call them on the dispatch thread (they are used there by kFS; their thread-safety was not analysed).[^vehicle-computer] |
| Image-loaded notification | `host.Computer.ImageLoaded { get; set; }` (`Action?`) | The host sets it to its private `PrepareSampler`; a mod can chain: keep the old delegate and assign one that calls both. Invoked on the dispatch thread at the end of a successful `Load`.[^vehicle-computer] |
| Read computer state from the UI | `host.Computer.Snapshot` (`MachineSnapshot`) | Immutable, published with `Volatile.Write`.[^host-computer] |
| Console output | `KfsIntegration.Output { get; set; }`, `KfsIntegration.Print(string)`, `KfsIntegration.Error(string)` | Redirect or write kFS console text.[^kfs-integration] |
| Burn construction | `KfsServices.BurnFactory { get; set; }` | Replaceable delegate, default `Burn.Create`.[^kfs-services] |
| Frame budget | `KfsVehicleHost.FrameBudgetMs { get; set; }` | Mod code inside the dispatch path is charged against it. |
| Per-frame UI-phase callback | `ImGuiWindow.RegisterStaticWindow(IStaticWindow)` | What kFS's own `FrameHook` uses. Runs in the UI phase (concurrent with dispatches), only while `IsShown` and the game UI is drawn.[^imgui-window] |
| Game input-event buffers | `InputEvents.IEnableInputBuffer`, `IActivateInputBuffer`, `BurnUpdateBuffer`, … (`public static TypedBuffer<T>`) | Adding from the sync window makes the game apply the event the same frame. Main thread only.[^input-events] |

# Patch-target candidates

| # | Method | Declaration | Thread | Called | Notes |
| --- | --- | --- | --- | --- | --- |
| 1 | `KfsIntegration.BeforeInputEvents` | `public static void BeforeInputEvents()` | main, sync window | once per frame from `Program.PrepareFrame` (`Program.cs:2551`) | Best per-frame main-thread hook. Body is skipped when the flag is off; a postfix still runs.[^kfs-integration][^program] |
| 2 | `KfsServices.MainThread` | `public void MainThread()` | main, sync window | once per live host per frame (flag on), hosts in `Id` order | Per-host main-thread hook. Returns early when its queue is empty; a postfix still runs. Host via the private field `_host`.[^kfs-services] |
| 3 | `KfsVehicleHost.ReleaseDead` | `internal static void ReleaseDead()` | main | every frame (flag on) and at `kfs off` | Sweep point for per-host mod state.[^kfs-vehicle-host] |
| 4 | `KfsVehicleHost..ctor` | `private KfsVehicleHost(Vehicle vehicle, GameTables tables)` | main | once per host, from `For` | Creation hook (postfix: host is fully built and already in `AllHosts`). |
| 5 | `KfsVehicleHost.HandBackAtFlagOff` | `public static void HandBackAtFlagOff()` | main, UI phase | at `kfs off` | "kFS was just switched off" notification. |
| 6 | `KfsVehicleHost.Dispatch` | `public bool Dispatch(VehicleUpdateState vehicleState, ref readonly FlightComputerNavigation nav, ref FlightComputerOutput output, bool controllable)` | dispatch | once per flight-computer pass for a vehicle with a host (flag on) | Wraps a whole dispatch; `__result` is "kFS computed control". By-ref struct parameters.[^kfs-vehicle-host] |
| 7 | `KfsVehicleHost.DispatchCore` | `private bool DispatchCore(…same parameters…)` | dispatch | from `Dispatch` | Same shape, private; no advantage over #6. |
| 8 | `KfsDispatch.TryComputeControl` | `internal static class KfsDispatch` / `public static bool TryComputeControl(VehicleUpdateState vehicleState, ref readonly FlightComputerNavigation nav, ref FlightComputerOutput output)` | dispatch | once per flight-computer pass for **every** vehicle (`PhysicsBubble.cs:2081`) | Sees vehicles without a host. Internal type; small method called from a small private static method — inlining risk.[^kfs-dispatch][^physics-bubble] |
| 9 | `KfsVehicleHost.Sample` | `public HostSample Sample(VehicleUpdateState vehicleState, ref readonly FlightComputerNavigation nav, ref FlightComputerOutput output)` | dispatch | at most once per dispatch, only when a tick runs | Postfix can read or replace the `HostSample` handed to the VM. |
| 10 | `KfsServices.BeforeDispatch` | `public void BeforeDispatch(VehicleUpdateState vs, FlightComputer fc)` | dispatch | once per dispatch while under kFS control, before ticks | Pre-tick hook with the vehicle state and worker flight computer; no by-ref parameters. |
| 11 | `KfsServices.AfterDispatch` | `public void AfterDispatch(in FlightComputerNavigation nav, FlightComputer fc, bool wasAuto, bool fallback)` | dispatch | once per dispatch while under kFS control, after ticks | Post-tick hook; this is the method that drains and applies service requests. `in` parameter. |
| 12 | `KfsServices.Apply` | `private void Apply(ServiceRequest r, in FlightComputerNavigation nav, FlightComputer fc, bool fallback)` | dispatch | once per accepted request | Per-request hook with full context. Private; `in` parameter.[^kfs-services] |
| 13 | `VehicleComputer.TakeServiceRequests` | `public List<ServiceRequest> TakeServiceRequests()` | dispatch | once per `AfterDispatch` | The drain itself. Returns a shared static empty list when nothing is queued — **do not mutate `__result` in that case.**[^vehicle-computer] |
| 14 | `VehicleComputer.ApplyRequest` | `protected override bool ApplyRequest(ServiceRequest request)` | dispatch, inside the VM tick's request drain | once per request not handled by `HostComputer.ApplyHostRequest` | The accept/reject gate: returning `true` claims the request; `false` makes the host reject it with "no vehicle to act on". Single reference-type parameter. Verified offline: a postfix that sets `__result = true` claims a request of an id the stock switch does not list.[^vehicle-computer][^host-computer][^offline-harness] |
| 15 | `HostComputer.ISyscallSink.Request` | explicit interface implementation `void ISyscallSink.Request(ServiceRequest request)` (IL name `KSA.Kfs.Vm.ISyscallSink.Request`, private) | dispatch for vehicles; main for the bench machine | once per request of any id | Sees **every** request including `log`/`trace`/`halt`, for vehicle computers *and* the bench machine. This method itself was not patched in the offline runs; looking up and patching an explicit interface implementation by its IL name is verified for `VehicleComputer`'s `KSA.Kfs.Vm.ISyscallHost.IssueResult`.[^host-computer][^offline-harness] |
| 16 | `KfsServices.HandBack` / `HandBackPilot` | `public void HandBack(FlightComputer fc, bool pilotDone)` / `public void HandBackPilot(Vehicle vehicle)` | dispatch / main | when a computer leaves control / at `kfs off` | "Program stopped flying this vehicle" notification. |
| 17 | `KfsControllerSet.MarkDeparted` | `public void MarkDeparted(Vehicle vehicle)` | dispatch | on a detected hardware change | "Layout changed" notification (called for the frozen set, and for the current set if different). |
| 18 | `FlightComputer.ComputeControl` | `public void ComputeControl(ref readonly FlightComputerNavigation nav, ref readonly ManualControlInputs inputs, ref FlightComputerOutput outputs)` | dispatch | stock path and kFS pilot mode; never under full control | Not kFS code, listed because its two callers are the kFS decision point. |

# Patchability concerns

## By-ref struct parameters (`in`, `ref readonly`, `ref`)

Candidates 6-9, 11, 12 and 18 take `FlightComputerNavigation` and/or `FlightComputerOutput` by reference. In metadata these are ordinary
managed by-ref parameters; a Harmony patch method declares them as `ref FlightComputerNavigation nav` (or `in`) and
`ref FlightComputerOutput output`, and when a method must be looked up by parameter types the type is
`typeof(FlightComputerNavigation).MakeByRefType()`. None of these methods is overloaded, so lookup by name alone
(`AccessTools.Method(typeof(KfsServices), "Apply")`) is unambiguous. *Unverified:* not exercised against this build.

A patch that receives `nav` by `ref` can technically write through a `ref readonly` reference; do not — the same struct is the bubble's
`vehicleState.FlightComputerNavigation` and is read again after the dispatch.

## The ref struct sampler and the generic dispatch

`VehicleComputer.Dispatch<TSampler>(long tNs, ref TSampler sampler, bool vehicleControllable = true) where TSampler : IHostSampler, allows ref struct`
is instantiated by the glue over the private nested `ref struct KfsVehicleHost.StepSampler`. Avoid it as a patch target: the instantiation
is specific to a private value type, a ref struct cannot be boxed into `object` (so `__args` or an `object`-typed patch parameter cannot
carry it), and its fields are `ref` fields. Everything it does is reachable through `KfsVehicleHost.Sample` (#9) and `Dispatch` (#6).
*Unverified:* whether Harmony can patch that instantiation at all was not tested.

## Inlining

A Harmony patch replaces a method's compiled entry point; a call site into which the JIT has already **inlined** the original never
reaches the patch. *Unverified for this build:* which of these methods the runtime inlines was not observed, and newer .NET runtimes
inline more aggressively (including some methods containing `try`/`finally`) than older ones. Risk assessment from method shape:

| Likely safe (large bodies, loops, locks, lambdas, many locals) | Small enough to be at risk |
| --- | --- |
| `BeforeInputEvents`, `KfsServices.MainThread`, `ReleaseDead`, the host constructor, `Dispatch`, `DispatchCore`, `Sample`, `BeforeDispatch`, `AfterDispatch`, `Apply`, `ApplyRequest`, `MarkDeparted`, `ComputeControl` | `KfsVehicleHost.For` / `Find` / `BeginFrame`, `ThrottleCommand` / `BurnTimeCommand` / `GimbalCommand`, `KfsServices.BeforeComputeControl`, `KfsControllerSet.EngineAt` / `GimbalAt` / `ThrusterAt`, `VehicleComputer.TakeServiceRequests` (short), `KfsDispatch.TryComputeControl` (short, although it contains a `try`/`catch`), `PhysicsBubble.RunFlightComputer`, the `FlightComputer` one-liners (`TrackTarget`, `RateHold`, `SetAttitudeProfile` — marked `AggressiveInlining`), `ThrusterController.UpdateCommand` (`AggressiveInlining`) |

Mitigations: apply patches as early as the loader allows (before the first frame compiles the callers); prefer the "likely safe" column;
verify each patch fires with a log line during development.

## Lazy statics and type initialisers

| Static | Initialisation | Consequence |
| --- | --- | --- |
| `KfsIntegration.Tables` | `LazyInitializer.EnsureInitialized(ref _tables, GameTables.LoadEmbedded)` on first access | To change tables, patch `GameTables.LoadEmbedded` (or its internals) before the first access; afterwards the instance is cached in the private static `_tables` and already captured by every `Workbench`, `VehicleComputer` and `KfsVehicleHost` created so far. Verified offline: with a postfix on `LoadEmbedded` that replaces `__result`, `Tables` returned the augmented instance (the same object on every read), and `_tables` was null before the first read.[^offline-harness] |
| `KfsIntegration.Workbench` | Created on first access; replaceable with `UseWorkbench` | Captures `Tables` and `Output` at creation. |
| `KfsPrototype._enabled` | Static field initialiser reading the environment variable | Use the property. |
| `KfsConsoleCommands._verbs` | `private static readonly Verb[]` built in the type initialiser | The verb table cannot be extended without replacing the array by reflection; register a separate terminal command instead, or prefix `KfsConsoleCommands.Execute(string)`. |
| `KfsVehicleHost.AllHosts` | `internal static readonly List<KfsVehicleHost>` | Lock it for every access. |
| `KfsServices.BurnFactory` | Static property initialiser `= Burn.Create` | Plain settable delegate. |

## Exceptions thrown by patches

| Where the patch runs | What catches it |
| --- | --- |
| Inside `KfsVehicleHost.Dispatch` and below (#6, #7, #9-#14) | `KfsDispatch.TryComputeControl` → `KfsVehicleHost.Fault`: the vehicle's computer is halted with a sticky host fault and the stock computer flies.[^kfs-dispatch] |
| On `KfsDispatch.TryComputeControl` itself (#8) or in `PhysicsBubble` | The bubble job wrapper: the whole physics bubble is marked failed for the frame. |
| In `BeforeInputEvents`, `MainThread`, `ReleaseDead` (#1-#3) | Nothing in kFS — the exception propagates into `Program.PrepareFrame`. |

Mod code in any of these should catch its own exceptions.

## Visibility

`KfsDispatch` is an `internal static class`; `KfsVehicleHost.AllHosts`, `ReleaseDead`, `BeginFrame`, `Fault` are `internal`;
`KfsServices.Apply`, `Queue`, `_host` and the `MainWork` records are private; `VehicleComputer.ApplyRequest` is `protected`,
`_cooked` private; `KfsControllerSet.LeafMap`/`Sampler` are `internal`. Reach them with `AccessTools` (or a publicized reference
assembly at compile time).

# Seam (a): observe every `ServiceRequest` the game drains

Three levels, from narrowest to widest:

| Goal | Hook | What you get |
| --- | --- | --- |
| See each request **as the game applies it**, with the worker flight computer and navigation | Postfix (or prefix) on `KfsServices.Apply` (#12) | `__instance` (→ `_host` → `Vehicle`), `ServiceRequest r`, `nav`, `fc`, `fallback`. Only requests the host layer accepted (ids 32, 48-53, 55-57, 64-67, minus mode-based rejections). |
| See **exactly what is drained**, as a list, once per dispatch | Postfix on `VehicleComputer.TakeServiceRequests` (#13) | `__instance` is the `VehicleComputer`, `__result` the list. No host/`fc`/`nav`; pair it with a prefix on `KfsServices.AfterDispatch` (#11) that stores the context for the duration of the call if they are needed. |
| See **every request of every id**, before acceptance, including host-internal ones and the bench machine | Prefix on `VehicleComputer.ApplyRequest` (#14) for vehicle requests, or on `HostComputer`'s `ISyscallSink.Request` (#15) for everything | `ServiceRequest` only; runs inside the VM tick. |

**Recommended for a mod that adds service ids — the tested recipe:** a **postfix** on `VehicleComputer.ApplyRequest` (#14) that, for
an id in the mod's range the stock switch left unclaimed (`__result == false`), queues the mod's own work item and sets
`__result = true`; no append to `_cooked` is needed to stop the "no vehicle to act on" rejection. A result-bearing service adds a
postfix on `VehicleComputer`'s explicit `ISyscallHost.IssueResult` (below). The work is then applied on the main thread — seam (b).
Both postfixes were applied and exercised outside the game; the complete recipe is
[adding a service](/modding/adding-a-service.md).[^offline-harness]

**Alternative (predates those experiments; not run):** let the request flow through the stock drain instead, for a handler that needs
`fc`, `nav` or the `fallback` flag. It reaches into the private `_cooked` list and adds a patch on the private `KfsServices.Apply`:

1. Prefix `VehicleComputer.ApplyRequest`: for an id in the mod's range, append the request to the computer's private `_cooked` list, set
   `__result = true` and skip the original. (This is what the stock cases do; it makes the request flow through
   `TakeServiceRequests` → `Apply` like a stock one, and stops the "no vehicle to act on" rejection.) Reject malformed requests with
   `__instance.RejectService(request, reason)` and `__result = true`.
2. Prefix `KfsServices.Apply`: for the mod's ids, translate the request into a mod work item for the host (available as
   `__instance._host`) and skip the original — whose `switch` would ignore the id anyway.
3. Apply the work on the main thread — seam (b).

With either recipe the request can only arrive at `ApplyRequest` if the VM accepted the service call in the first place: the id, its
argument cell types and domains come from the services table, and the compiler needs to know the service to emit the call. That part is outside the game glue: see
[services table](/tables/services-table.md), [services syscall ABI](/vm/services-syscall-abi.md) and
[kC builtins and intrinsics](/language/kc-builtins-and-intrinsics.md).

Results back to the program: a "request" kind service returns nothing. Synchronous results are produced in
`ISyscallHost.IssueResult(ushort service, ReadOnlySpan<uint> args, Span<uint> results)` (implemented explicitly by `VehicleComputer`, only
for ids 64/65), during the tick, on the dispatch thread — so only values that can be computed there without touching live game objects.
Anything else has to come back asynchronously (a later tick reads it from a message or memory). Verified offline: a Harmony postfix on
that explicit implementation — found with `AccessTools.Method(typeof(VehicleComputer), "KSA.Kfs.Vm.ISyscallHost.IssueResult")`, its
`ReadOnlySpan<uint>` and `Span<uint>` parameters declared as such in the patch method — applied under Harmony 2.4.2 on .NET 10, and
the cell it wrote to `results[0]` reached the program synchronously.[^offline-harness]

# Seam (b): run code on the main thread once per frame, alongside `Services.MainThread()`

**Recommended:** Harmony **postfix on `KfsIntegration.BeforeInputEvents`**.

- Main thread, exactly once per frame, inside the sync window: the previous solver job has been awaited and applied, the next has not
  started, so live vehicles, parts and modules may be mutated.
- It runs immediately before `InputEvents.ApplyInputEvents()`, so events the mod adds to `InputEvents.*Buffer`s are applied in the same
  frame, in the game's own order.
- It runs while paused.
- As a postfix it runs after kFS's own `ReleaseDead()` and `MainThread()` loop (when the flag is on), so `AllHosts` is already pruned.
  It also runs when the flag is off — check `KfsPrototype.Enabled` if the work should stop with kFS.
- kFS processes hosts in ordinal `Id` order; copy that if cross-vehicle determinism matters.

Sketch (identifiers other than the game's are illustrative):

```csharp
[HarmonyPatch(typeof(KfsIntegration), nameof(KfsIntegration.BeforeInputEvents))]
static class BeforeInputEventsPatch
{
    static void Postfix()
    {
        if (!KfsPrototype.Enabled) return;
        try { ModMainThread.DrainAll(); }           // per-host queues filled on dispatch threads under a lock
        catch (Exception e) { /* log; never let it escape into Program.PrepareFrame */ }
    }
}
```

Alternatives: a postfix on `KfsServices.MainThread` (#2) for strictly per-host work (same window; runs only while the flag is on and
only for live hosts); a prefix on `InputEvents.ApplyInputEvents` (same window, independent of kFS). A registered `IStaticWindow` or any
UI-drawn callback is **not** equivalent: it runs in the UI phase while dispatches are in flight.

To return a result to the program's side, call `host.Computer.Enqueue(c => …)` from the main thread; the action runs on the dispatch
thread at the start of the computer's next dispatch.

# Seam (c): per-vehicle state next to a `KfsVehicleHost`, cleaned up when the host dies

**Recommended:** key the state on the host object, create it lazily, sweep it where kFS sweeps.

```csharp
static readonly ConditionalWeakTable<KfsVehicleHost, ModHostState> States = new();

static ModHostState StateOf(KfsVehicleHost host) => States.GetValue(host, h => new ModHostState(h));   // thread-safe, lazy
```

- `ConditionalWeakTable` needs no patch and no explicit removal for memory safety: when kFS drops a host from `AllHosts` and from
  `Vehicle.KfsHost`, the entry becomes collectable with it. `GetValue` is thread-safe, so the state can be created from a dispatch thread
  or the main thread, whichever touches the host first.
- For **deterministic** clean-up (undoing game-side effects, releasing resources) add a postfix on `KfsVehicleHost.ReleaseDead()` (#3)
  that walks the mod's own strong list of states and disposes those whose `host.IsAlive` is false; or do the same walk in the seam (b)
  postfix with no extra patch — `IsAlive` is public. The vehicle may already be disposed at that point: clean up mod state, do not touch
  the vehicle.
- For **eager creation** (state must exist before the first dispatch), postfix the private constructor (#4,
  `AccessTools.Constructor(typeof(KfsVehicleHost), new[] { typeof(Vehicle), typeof(GameTables) })`). `KfsVehicleHost.For` is too small to
  rely on.
- Do not key on `Vehicle` or `Vehicle.Id`: a host does not follow a split, dies on a merge, and a reloaded vehicle with the same id is a
  different object ([lifecycle](/integration/lifecycle-and-persistence.md)).
- Fields of the state that are written on dispatch threads and read on the main thread need their own synchronisation, exactly as
  `KfsServices` uses `_gate` for its queue and `Interlocked`/`volatile` for its counters.

# Putting the three together for a "vehicle systems" extension

```text
program:  SVC <mod id>(args)                                   dispatch thread, in the VM tick
  └─ VehicleComputer.ApplyRequest postfix       (a)    claim (__result = true); build work item; StateOf(host).Queue(work)   (c)
       (alternative: prefix → _cooked, then a KfsServices.Apply prefix builds the work item)
main:     KfsIntegration.BeforeInputEvents postfix (b)  for each live host, in Id order: drain queue,
                                                        resolve index → part module, mutate game state or add InputEvents
          └─ optional: host.Computer.Enqueue(c => …)   result/ack back to the dispatch thread
dispatch: next dispatch drains the computer queue before ticks; the program observes the result
```

In the `ApplyRequest` postfix `__instance` is the `VehicleComputer`, not the host: keep a `VehicleComputer` → host map (filled from
the constructor postfix #4 or from `AllHosts`), as [adding a service](/modding/adding-a-service.md) describes.

Latency is one frame for the effect and one more dispatch for an acknowledgement — the same as kFS's own burn planning.

# Modding notes

- Prefer **postfixes on the public, coarse methods** (#1, #2, #6, #9-#11, #13) over anything fine-grained; they have the simplest
  signatures and the lowest inlining risk. The one fine-grained exception is the `ApplyRequest` postfix (#14), which is the tested
  route for claiming a mod service id.
- Every dispatch-side patch must be **re-entrant across vehicles** and must not block: it runs on the solver's highest-priority threads,
  inside the frame's physics job, and its time counts against the program's frame budget.
- Nothing in this glue is versioned or marked as API: the class is literally called `KfsPrototype`. Resolve members defensively
  (`AccessTools` returns `null` when a member is missing) and fail closed.
- Seams that need changes *outside* the game glue — new service ids, new messages, new command fields — are listed in
  [/modding/](/modding/index.md).

[^kfs-integration]: KfsIntegration.cs:89-121, 137-176.
[^kfs-prototype]: KfsPrototype.cs:6-29.
[^kfs-dispatch]: KfsDispatch.cs:5-28.
[^kfs-vehicle-host]: KfsVehicleHost.cs:21-35, 39, 83-118, 139-166, 212-228, 281-295, 442-616.
[^kfs-services]: KfsServices.cs:14-20, 32-65, 76-192, 254-284, 310-449, 543-624.
[^kfs-console-commands]: KfsConsoleCommands.cs:15-28, 187-258.
[^vehicle-computer]: VehicleComputer.cs:49-53, 136, 153-183, 185-239, 313-350, 449.
[^host-computer]: HostComputer.cs:167-174, 288-300, 439, 670-676, 737-745, 813-816.
[^physics-bubble]: PhysicsBubble.cs:2077-2086.
[^program]: Program.cs:2511-2608.
[^input-events]: InputEvents.cs:1010-1117, 1186-1215.
[^imgui-window]: ImGuiWindow.cs:122-137.
[^offline-harness]: Experiments E5 (`ApplyRequest` and `IssueResult` postfixes) and E10 (`LoadEmbedded` postfix) in the offline harness document.
