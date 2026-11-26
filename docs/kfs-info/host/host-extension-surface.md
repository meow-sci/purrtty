---
type: Extension Point
title: Host extension surface
description: Where a mod can attach to the kFS host layer — the few patch-free hooks, the Harmony patch targets with signatures and visibility, the four ways to deliver variable-length host data to a program, and every size limit that constrains a design.
resource: ksa-decomp:KSA.Kfs.Host/VehicleComputer.cs
tags: [kfs, host, extension, harmony, modding, services, messages, limits]
sources:
  - id: host-computer
    resource: ksa-decomp:KSA.Kfs.Host/HostComputer.cs
    title: HostComputer (decompiled)
  - id: vehicle-computer
    resource: ksa-decomp:KSA.Kfs.Host/VehicleComputer.cs
    title: VehicleComputer (decompiled)
  - id: kvm-machine
    resource: ksa-decomp:KSA.Kfs.Vm/KvmMachine.cs
    title: KvmMachine (decompiled)
  - id: host-messages
    resource: ksa-decomp:KSA.Kfs.Host/HostMessages.cs
    title: HostMessages (decompiled)
  - id: host-leaf-map
    resource: ksa-decomp:KSA.Kfs.Host/HostLeafMap.cs
    title: HostLeafMap (decompiled)
  - id: layout-generator
    resource: ksa-decomp:KSA.Kfs.Host/LayoutGenerator.cs
    title: LayoutGenerator (decompiled)
  - id: message-layout
    resource: ksa-decomp:KSA.Kfs.Vm.Tables/MessageLayout.cs
    title: MessageLayout (decompiled)
  - id: game-tables
    resource: ksa-decomp:KSA.Kfs.Vm.Tables/GameTables.cs
    title: GameTables (decompiled)
  - id: kfs-services
    resource: ksa-decomp:KSA.Kfs/KfsServices.cs
    title: KfsServices (decompiled)
  - id: kfs-vehicle-host
    resource: ksa-decomp:KSA.Kfs/KfsVehicleHost.cs
    title: KfsVehicleHost (decompiled)
  - id: kfs-controller-set
    resource: ksa-decomp:KSA.Kfs/KfsControllerSet.cs
    title: KfsControllerSet (decompiled)
  - id: kfs-host-sampler
    resource: ksa-decomp:KSA.Kfs/KfsHostSampler.cs
    title: KfsHostSampler (decompiled)
  - id: kfs-integration
    resource: ksa-decomp:KSA.Kfs/KfsIntegration.cs
    title: KfsIntegration (decompiled)
  - id: header-generators
    resource: ksa-decomp:KSA.Kfs.KC/VehicleHeader.cs
    title: VehicleHeader (kC) and KSA.Kfs.KAsm/BuiltinIncludes.cs (kASM) (decompiled)
  - id: image-reader
    resource: ksa-decomp:KSA.Kfs.Vm.Image/ImageReader.cs
    title: ImageReader (decompiled)
  - id: offline-harness
    resource: /modding/offline-harness.md
    title: Offline harness experiments E1–E12
generated: { by: claude-code/claude-fable-5-2, at: 2026-11-25T21:30:51Z }
status: draft
ksa_build: "2026.11.25.5949"
---

# Scope and status

This concept collects, from the other `/host/` documents, what bears on **extending** kFS from a
StarMap/Harmony mod. The facts about the game code are read from the decompiled sources and cited.
The recommendations built on them are design reasoning and are marked `Inferred:`; none of it has
been run against the game, which is why this document is `status: draft`. The service route alone
(an augmented table, a postfix on `ApplyRequest`, a postfix on `IssueResult`) was since run against
the real `KSA.dll` outside the game; the tested recipe is
[adding a service](/modding/adding-a-service.md), and the points it settles are cited to
[the offline harness](/modding/offline-harness.md) below.[^offline-harness]

# The short answers

1. **A request with an id the host does not know.** If the id is not in `GameTables.Services`, the
   image is refused at load (rule `syscall`, `unknown service id`); if its row exists but is not
   wired (kind `query`), the program traps `BAD_SYSCALL`. Either way the host sees nothing. If the
   row is wired, the request reaches `HostComputer`'s `ISyscallSink.Request`, falls through both
   switches and is rejected: `Machine.CountRejectedRequest()` plus the rate-limited event
   `rejected <name>: no vehicle to act on`. It never reaches the game's `KfsServices`.[^host-computer][^kvm-machine][^image-reader]
2. **Generic hooks.** There is no event, callback list, registry or overridable-by-a-mod method for
   services, messages or ticks. The host layer is closed switches over numeric ids. Five things are
   nevertheless usable without a patch (next section).
3. **Variable-length host → program data** can go four ways — a host-published message, leaves on an
   existing message, sensor-block fields, or a result-bearing service — compared below.
4. **Limits.** Sensor block and command block: 65 536 bytes each, multiples of 8. Message: 65 535
   body bytes plus a 16-byte header. Requests: 256 per tick. Full table at the end.
5. **Visibility.** The data and plumbing types are public; the decision points
   (`ApplyHostRequest`, `ApplyRequest`, `IssueResult`, `PlayerTick`, `_cooked`, `_hostPublished`,
   `_sensors`) are private or protected on sealed classes.

# Hooks that need no patch

| hook | member | what it gives | caveats |
| --- | --- | --- | --- |
| dispatch-thread mailbox | `public void HostComputer.Enqueue(Action<HostComputer> action)` | run code on the computer's dispatch thread at the start of the next `Dispatch`, before any tick | an exception thrown by the action faults the computer; the bench never drains its queue |
| result provider | `public ISyscallHost? KvmMachine.SyscallHost { get; set; }` | synchronous results for result-bearing services; can be wrapped by a decorator | must forward ids 64 and 65 to the original; called mid-tick, before the tick is known to latch |
| image-loaded callback | `public Action? VehicleComputer.ImageLoaded { get; set; }` | notification after a successful `Load` | single slot already used by the game — chain, do not replace; set in `KfsVehicleHost`'s constructor |
| direct VM access | `public` `KvmMachine.WriteMessage`, `ReadMessage`, `ReadSlot`, `MessageRole`, `ReadRam`, `WriteRam`, `ReadCommandBlock`, `WriteCommandBlock`, `ReadSensorBlock`, `ReadSystemBlock`, `CountRejectedRequest`, `IsBooted` | deliver a message, inspect or poke memory | dispatch thread only (use `Enqueue`); `WriteMessage` throws unless the image subscribes to the id |
| mutable tables | `public Dictionary<ushort, ServiceInfo> GameTables.Services`; `GameTables.MessagesData.Messages` (`List<MessageRow>`) | add service and message rows at run time | one `GameTables` instance (`KfsIntegration.Tables`) is shared by compiler, VM and host; see [game tables loader](/tables/game-tables-loader.md) for caches and validation. A `ServiceInfo` added in place gets no kASM symbol and no kC binding (both are built from the `services.json` rows);[^header-generators] the tested route is an augmented instance returned by a postfix on `GameTables.LoadEmbedded` before the first read ([adding a service](/modding/adding-a-service.md)) |

Also public and useful as read-only state: `VehicleComputer.Applied`, `AppliedLayout`, `Grid`,
`Mode`, `LastK`, `TicksLatched`, `Console`, `Event(string)`, `RejectService(request, reason)`,
`Snapshot`; `KfsVehicleHost.For(vehicle)`, `Find(vehicle)`, `Vehicle.KfsHost`, `Frozen`, `Current`.
`KfsVehicleHost.AllHosts` is `internal static`.[^kfs-vehicle-host]

# Harmony patch targets

| target | signature | visibility | runs on | use |
| --- | --- | --- | --- | --- |
| `KSA.Kfs.Host.VehicleComputer.ApplyRequest` | `protected override bool ApplyRequest(ServiceRequest request)` | protected | dispatch thread, after a latched tick | **claim new request ids on vehicles**: postfix that turns a `false` `__result` into `true` (tested offline, the recommended recipe); alternative: prefix, `__result = true`, skip original |
| `KSA.Kfs.Host.HostComputer.ApplyHostRequest` | `private bool ApplyHostRequest(ServiceRequest request)` | private | same | claim ids for every computer, including the bench |
| `KSA.Kfs.Host.VehicleComputer` (explicit `ISyscallHost.IssueResult`) | `void IssueResult(ushort service, ReadOnlySpan<uint> args, Span<uint> results)` | private (explicit interface implementation) | dispatch thread, mid-tick | results: postfix that writes `results[0]`, target resolved as `AccessTools.Method(typeof(VehicleComputer), "KSA.Kfs.Vm.ISyscallHost.IssueResult")` (tested offline, the recommended recipe); alternative: wrap `Machine.SyscallHost` — no patch |
| `KSA.Kfs.Host.VehicleComputer.PlayerTick` | `private HostTickResult PlayerTick(ulong k, HostSample host)` | private | dispatch thread, once per tick | per-tick hook with the tick index: prefix to deliver extra messages or write sensors, postfix to read `Applied` |
| `KSA.Kfs.Host.VehicleComputer.Load` | `public override LoadResult Load(byte[] image, IoLayout layout)` | public | dispatch thread (queued) | postfix: per-image setup (which mod messages the image subscribes to) |
| `KSA.Kfs.Host.HostMessages.PackInto` | `public void PackInto(byte[] slot, ushort id, HostSample sample, ulong k, int tickHz, uint soiChanges, long epochNs = 0L)` | public | dispatch thread | prefix to pack a new id (the original throws for unknown ids) |
| `KSA.Kfs.Host.LayoutGenerator.Vehicle` | `public static IoLayout Vehicle(string name, int thrusters, int engines, int gimbals)` | public static | wherever a `KfsControllerSet` is built | postfix to append command or sensor fields; `name` is the vehicle id |
| `KSA.Kfs.KfsControllerSet..ctor` | `public KfsControllerSet(Vehicle vehicle)` | public | same | alternative to the above when the patch needs the `Vehicle` itself |
| `KSA.Kfs.KfsControllerSet.Matches` | `public bool Matches(Vehicle vehicle)` | public | dispatch thread | postfix so a change in the mod's part set triggers a new layout |
| `KSA.Kfs.KfsHostSampler.Sample` | `public HostLeafValues Sample(VehicleUpdateState vs, in FlightComputerNavigation nav, in ManualControlInputs inputs, in FlightComputerOutput output, FlightComputer fc, double simulationSpeed, float maxFlightComputerRate)` | public | dispatch thread | postfix to set extra leaves on `__result` |
| `KSA.Kfs.KfsServices.Apply` | `private void Apply(ServiceRequest r, in FlightComputerNavigation nav, FlightComputer fc, bool fallback)` | private | dispatch thread | game-side handling of ids a patch added to `_cooked` — an alternative to claiming the id in `ApplyRequest`, needed only when the handler wants the worker flight computer or the `fallback` flag |
| `KSA.Kfs.KfsServices.MainThread` | `public void MainThread()` | public | **main thread**, once per frame per host | postfix: apply queued mod work to game objects |
| `KSA.Kfs.KfsServices.HandBack` | `public void HandBack(FlightComputer fc, bool pilotDone)` | public | dispatch thread | postfix: restore mod-controlled parts when kFS releases the vehicle |

[^vehicle-computer][^host-computer][^host-messages][^layout-generator][^kfs-controller-set][^kfs-host-sampler][^kfs-services]

Notes:

- `VehicleComputer` and `BenchMachine` are sealed, so `ApplyRequest`, `Publishing` and
  `HostPublishedIds` cannot be overridden by subclassing; Harmony is the only way in.
- Verified offline: Harmony 2.4.2 on .NET 10 patches the explicit interface implementation
  `KSA.Kfs.Vm.ISyscallHost.IssueResult` with a postfix that declares its `ReadOnlySpan<uint>` and
  `Span<uint>` parameters, and the program receives the cell written there synchronously; a
  postfix on `ApplyRequest` that sets `__result = true` claims the request without touching
  `_cooked` (experiment E5 of [the offline harness](/modding/offline-harness.md)).[^offline-harness]
  `Unverified:` a patch on `HostComputer.RunTick` (also span-typed), which was not part of those runs.
- `KfsServices.MainThread` is called from `KfsIntegration.BeforeInputEvents` for every live host,
  sorted by vehicle id, only while `KfsPrototype.Enabled`.[^kfs-integration]

# Adding a service: what each layer requires

| layer | requirement | consequence if missing |
| --- | --- | --- |
| tables | a `ServiceInfo` in `GameTables.Services` with `Kind` `request` or `request+result`, `ArgTypes`/`ArgCells`, `ResultCells`, a known `CostClass`, optional `Domains` | image refused at load (`syscall`: unknown service id), or `BAD_SYSCALL` for a row that is not wired; no cost; compiler does not know the name |
| compiler / assembler | kC: a prototype in a `<…>` header and the name in `SystemHeaders.Builtins`; kASM: nothing beyond the loaded `services.json` row (`SVC_<SYMBOL>`) | the kC call does not compile (`KC-E003`, `KC-E304`) — see [adding a service](/modding/adding-a-service.md), [kC builtins and intrinsics](/language/kc-builtins-and-intrinsics.md), [services table](/tables/services-table.md) |
| VM | nothing further; argument domains are checked from the row | — |
| host, results | an `ISyscallHost` that fills `results` for the id | the program receives zeros |
| host, requests | a handler that returns "handled" for the id | `rejected <name>: no vehicle to act on` on every call |
| game | the actual effect, on the right thread | — |

The tested, recommended recipe is the postfix pair in
[adding a service](/modding/adding-a-service.md): a postfix on `VehicleComputer.ApplyRequest` that
claims the mod's ids by setting `__result = true`, and, for a **result-bearing** service, a postfix
on the explicit `ISyscallHost.IssueResult` implementation that fills the result cell.[^offline-harness]
`Inferred:` the alternatives this document first proposed remain possible — a Harmony prefix on
`VehicleComputer.ApplyRequest` that recognises the mod's id range, copies what it needs from
`ServiceRequest.Args` into a mod-owned queue and returns handled (or one that appends to `_cooked`
and leaves the handling to a patch on `KfsServices.Apply`), and an `ISyscallHost` decorator around
`Machine.SyscallHost`, installed when the `VehicleComputer` is first seen, that answers from a
snapshot of game state prepared on the main thread. Either way the mod drains its queue on the
main thread — a postfix on `KfsIntegration.BeforeInputEvents` or on `KfsServices.MainThread`, not a
UI or per-frame draw hook, which runs while dispatches are in flight — and touches parts there.

Things the stock services teach about doing this correctly:

- A request exists only if its tick latched. Never act in `IssueResult`; allocate or look up there,
  act when the request arrives.
- Requests of one dispatch can come from several catch-up ticks; they arrive in issue order.
- In full-control mode the stock steering services are rejected, in fallback
  (`!vehicle.IsControllable`) the stock override does not apply. Decide both cases explicitly for a
  new service; `stage` (accepted in both modes, executed on the main thread, with the
  `AwaitingStage` gate) is the closest stock precedent for "actuate a part".
- Use `VehicleComputer.RejectService(request, reason)` for a refusal: it increments the counter the
  program can read and writes a rate-limited event.

# Delivering a variable-length list to a program

The example is "the state of N lights", N differing per vehicle.

## Option A — a new host-published message with a per-vehicle array

How the stock `MSG_VEHICLE_PROPS` does it for thrusters, engines and gimbals.

- **Catalogue.** A row with a field named `light[i]` and an element type string such as
  `{on: bool, brightness: float, color: vec3, …}`. `MessageLayout.Build` sizes the array by
  `GroupCount(layout, "light")`, i.e. the number of distinct `n` in command fields named
  `light[n].…` in the I/O layout. So **the element count comes from the layout**, and the layout
  must carry at least one `light[n].*` command field per light.[^message-layout]
- **Element type.** Members may be `bool`, `uint32`, `int64`, `float`, `vec3`, `quat`, `mat3`,
  `dvec3`, optionally as fixed arrays `type[n]`. The generated C type name is fixed as
  `kfs_vehicle_props_<group>_t`.
- **Delivery.** Either extend the stock path — add the id to the private
  `VehicleComputer._hostPublished` after `Load` and pack it in a `HostMessages.PackInto` prefix — or
  bypass it: build the slot (`MessagePacker.Pack(layout, values, HostMessages.Seq(k),
  HostMessages.MetUs(k, tickHz))` or hand-written bytes) and call `Machine.WriteMessage(id, slot)`
  from a `PlayerTick` prefix or an `Enqueue`d action.[^host-messages][^kvm-machine]
- **Program side.** The image must subscribe to the id. Each subscribing task gets its own copy of
  the slot at task start and pays the copy in cycles.
- **Limits.** 65 535 body bytes; in practice the part's RAM and cycle budget.
- **Cost of the approach.** A layout change (new fingerprint), a catalogue change, and a load-time
  dependency: the image validator rejects an image that names a message id missing from the tables
  or whose recorded `body_size` differs from what the current catalogue and layout give (rule
  `messages`).[^image-reader]

## Option B — leaves on an existing host message

`HostLeafMap` and `HostLeafValues` can address any field of the seven host messages by path, and
`KfsHostSampler.Sample` returns the `HostLeafValues` a postfix can extend.[^host-leaf-map][^kfs-host-sampler]
That only helps if the fields exist, so it means adding fields (for example a `light[i]` array) to
a stock row such as `MSG_VEHICLE_PROPS`.

- Works with almost no host patching (table edit + sampler postfix + a layout that names
  `light[n]`).
- **But** it changes a stock message's size: every image's recorded `body_size` for that message
  must match, so images built without the mod stop loading on a modded game and vice versa, and
  `LayoutGenerator.PropsBodySize`'s hard-coded constants no longer describe the message.
- Leaves are computed only when the image subscribes to one of 0x0402–0x0405
  (`WantsHostLeaves`).

`Inferred:` Option B is the cheapest to build and the most fragile to ship.

## Option C — sensor-block fields

The memory-mapped sensor block is exactly "host data a program reads without a call", but **the
vehicle layout has no sensor fields and nothing writes `SensorBlock.Bytes`** in this build.[^layout-generator][^vehicle-computer]

- Add `light[n].on`, `light[n].brightness`, … as *sensor* fields in a `LayoutGenerator.Vehicle`
  postfix (types `bool`, `int32`, `uint32`, `float32`, …, aligned to their size; block size a
  multiple of 8, at most 65 536 bytes), recompute the fingerprint.
- Write the values into the private `VehicleComputer._sensors.Bytes` before each tick (a
  `PlayerTick` prefix using Harmony's private-field injection for `_sensors`); `RunTick` copies the
  array into the VM.
- The program reads `sensors.light[n].on` directly — no subscription, no copy cost, no per-task
  buffer. The kC and kASM header generators already emit a `kfs_sensors_t` when a layout has sensor
  fields ([kC generated headers](/language/kc-generated-headers.md)).[^header-generators]
- Fixed at layout time, like Option A. No header, sequence number or timestamp.

## Option D — a result-bearing service

`light_count() → u32`, `light_get(u32 index, u32 field) → f32` and similar.

- No layout or catalogue change: one image runs on any vehicle and discovers N at run time. This is
  the only option that is genuinely variable-length **per run**.
- Each call is synchronous, costs cycles by its cost class, and counts against the 256 requests per
  tick; results are raw 32-bit cells.
- Answers must come from a snapshot prepared on the main thread: `IssueResult` runs on the dispatch
  thread in the middle of VM execution.
- The request also flows through `ISyscallSink.Request` afterwards and must be claimed there, or it
  is logged as rejected.

## Comparison

| | A: new message | B: leaves on a stock message | C: sensor fields | D: result service |
| --- | --- | --- | --- | --- |
| layout (fingerprint) change | yes | yes | yes | no |
| catalogue change | new row | **edits a stock row** | no | no (service row instead) |
| count fixed when | layout is generated | layout is generated | layout is generated | each call |
| program cost | subscription copy per task | same message, larger | none beyond loads | per call |
| consistency | whole record per tick, with `seq`/`t_us` | same | whole block per tick | per call, may straddle host updates |
| host patches | `PlayerTick` prefix (or `Load` + `PackInto`) | sampler postfix | `PlayerTick` prefix | `ApplyRequest` postfix + `IssueResult` postfix (tested), or `ApplyRequest` prefix + `SyscallHost` decorator |
| stock images unaffected | yes, if the layout is unchanged for vehicles without lights | **no** | yes, same condition | yes |

The same choice exists in the other direction. Program → host control can be **request services**
(`light_set(index, field, value)`: discrete, logged, rejectable, 256 per tick, no layout change) or
**command-block fields** (`light[n].on`, `.brightness`, …: level state latched atomically with the
rest of the commands, read by the mod from `VehicleComputer.Applied` at the field offsets; requires
the layout change and takes part in `IsQuiescent`). Note that a layout which names `light[n]`
command fields is also what gives Options A and B their element count.

`Inferred:` for lights and deploy/actuation animations — low-rate, discrete actions on a
vehicle-specific number of parts — Option D for discovery and state plus request services for
control keeps stock programs, stock layouts and the stock catalogue untouched, at the price of
per-call cycles. Command and sensor fields give the nicest program-side ergonomics (plain struct
access) but bind every image to a vehicle's light count through the fingerprint and require
handling layout changes when lights are added or lost (`KfsControllerSet.Matches` counts only
thruster, engine and gimbal controllers).

# Threading rules to design by

- `Dispatch`, `RunTick`, `IssueResult`, `ApplyRequest`, `PlayerTick`, sampling and
  `KfsServices.Apply` all run on the thread that steps the vehicle's flight computer
  (`KfsDispatch.TryComputeControl`, called from `PhysicsBubble`); see
  [dispatch and threading](/integration/dispatch-and-threading.md).
- The stock code moves anything that changes main-thread game state to `KfsServices.MainThread`
  (staging, burn creation, engine shutdown) and returns results with `Computer.Enqueue`.
- An unhandled exception anywhere under `Dispatch` is caught by `KfsDispatch`, logged, and turns
  into `VehicleComputer.Fault` — the program is halted and the stock flight computer takes over
  until the next load or start. A buggy patch therefore disables kFS on that vehicle rather than
  crashing the game, but silently from the program's point of view.[^kfs-vehicle-host]
- `ConsoleLog` and `TraceStore` are locked and safe from any thread; `MachineSnapshot` is immutable.

# Limits

| what | limit | where enforced |
| --- | --- | --- |
| command block | 65 536 bytes, multiple of 8 | `LayoutGenerator.Vehicle`; address window in the VM; `GameTables.LayoutFrom` |
| sensor block | 65 536 bytes, multiple of 8 | address window in the VM; `GameTables.LayoutFrom` |
| message body | 65 535 bytes (`body_size` is `ushort`); slot = 16 + body | image format; `LayoutGenerator.Vehicle` for `MSG_VEHICLE_PROPS` |
| `MSG_VEHICLE_PROPS` elements | 84 bytes per thruster, 1120 per engine, 76 per gimbal, 120 fixed | `LayoutGenerator.PropsBodySize` |
| service requests | 256 per tick; excess dropped and counted | `KvmMachine.MAX_REQUESTS_PER_TICK` |
| service results | `ResultCells` 32-bit cells, synchronous | `ISyscallHost.IssueResult` |
| live burn plans | 8 | `BurnHandles.MAX_LIVE_PLANS` |
| program log | 10 lines per machine second, 255 characters per line | `ConsoleLog`, `LogFormatter` |
| host events | 20 per machine second | `ConsoleLog` |
| console history | 4096 lines | `ConsoleLog.CAPACITY` |
| trace channels | 64 per store, 2048 samples each | `TraceStore` |
| plotted message fields | 16 per computer | `BusTracker.MAX_PLOTTED_FIELDS` |
| tick rates | divisors of 100 Hz; catalogue allows 5, 10, 20, 25, 50, 100 | `TickGrid`, `catalogue.json` |
| catch-up | ticks more than 1 s overdue are skipped | `VehicleComputer.MAX_CATCH_UP_NS` |
| host time | 4 ms per frame per vehicle by default | `KfsVehicleHost.FrameBudgetMs` |

[^game-tables][^layout-generator][^kvm-machine]

# Related

- [Service request handling](/host/service-request-handling.md) — the full request path.
- [Message bus](/host/message-bus.md) — packing and delivery.
- [I/O layout generation](/host/io-layout-generation.md) — the layout and its limits.
- [Host sampling and leaves](/host/host-sampling-and-leaves.md) — every host message field.
- [Ownership and control modes](/host/ownership-and-control-modes.md) — pilot vs full control.

# Modding notes

- **Static and shared:** `KfsIntegration.Tables` (one `GameTables` for compiler, VM and host),
  `HostMessages.MsG3Order`, `MessageLayout.HostPublished`, `Ownership.StockOnlyMessages`,
  `TraceBufferPool.Shared`, the element-shape cache inside `MessageLayout` (keyed by group name and
  type string). Table edits must happen before the first image is compiled or loaded.
- **Validated at load, per image:** layout fingerprint, each message's `body_size` against the
  current catalogue and layout, service table and catalogue versions, host-only publications,
  `needs-stock`. Anything a mod changes in tables or layouts changes which images load.
- **Cached per image or per controller set:** `VehicleComputer._hostPublished`, `Mode`, `Owned`,
  `HostMessages` offsets, `HostLeafMap`, the game's `KfsHostSampler` handles. A mod that changes
  tables after a program is loaded will not see the change until the next `Load` (or a new
  controller set).
- **Per-vehicle lifetime:** one `KfsVehicleHost`, one `VehicleComputer` and one `BurnHandles`
  table per vehicle, created on first use (`KfsVehicleHost.For`, whose only caller is the
  workbench's `ActiveVehicle` delegate, for the controlled vehicle) and released when the vehicle is
  disposed or leaves the current system.
- kFS is a prototype in this build, off unless `KFS_PROTOTYPE=1` or `KfsPrototype.Enabled` is set
  ([feature flag](/integration/feature-flag.md)); every member named here can change between
  builds, so patches should fail soft when a target is missing.

[^host-computer]: HostComputer.cs — `Enqueue` :288–291; `ISyscallSink.Request` :670–676; `RejectByHost` :737–745; `ApplyHostRequest` :747–811; `ApplyRequest` :813–816.
[^vehicle-computer]: VehicleComputer.cs — private fields `_sensors`, `_hostPublished`, `_cooked` :27–49; `ImageLoaded` :136; constructor :146–151; `IssueResult` :174–183; `ApplyRequest` :185–239; `Load` :313–350; `PlayerTick` :554–573.
[^kvm-machine]: KvmMachine.cs — `SyscallHost` :257; `WriteMessage` :368–384; sensor/command windows :511–524; `IsWiredService` :2248–2258; memory accessors :2042–2075.
[^host-messages]: HostMessages.cs — `MsG3Order` :364; `IsMsG3` :378–385; `Seq`/`MetUs` :403–416; `PackInto` (throws for other ids) :430–471.
[^host-leaf-map]: HostLeafMap.cs — only `MsG3Order` messages :27–32; `TryHandle` :56–75.
[^layout-generator]: LayoutGenerator.cs — `Vehicle` (no sensor fields, limits) :13–85; `PropsBodySize` :87–90; `Fingerprint` :130–138.
[^message-layout]: MessageLayout.cs — `GroupCount` :79–112; `TypeShape` :137–151; element type name prefix :164; `Element` :169–228; per-vehicle arrays :245–285.
[^game-tables]: GameTables.cs — `Services` :67; `BuildServices` :538–580; `LayoutFrom` limits :761–796.
[^kfs-services]: KfsServices.cs — `HandBack` :254–277; `Apply` :310–449; `MainThread` :551–624.
[^kfs-vehicle-host]: KfsVehicleHost.cs — `AllHosts` :39; `For`/`Find` :139–147; `Fault` :168–210; KfsDispatch.cs:7–28.
[^kfs-controller-set]: KfsControllerSet.cs — constructor :60–85; `Matches` :112–119.
[^kfs-host-sampler]: KfsHostSampler.cs — `Sample` :411–613.
[^kfs-integration]: KfsIntegration.cs — `Tables` :89; `ActiveVehicle` delegate (the only `KfsVehicleHost.For` call) :101–105; `BeforeInputEvents` :158–176.
[^offline-harness]: Offline harness — experiments E3 (kC needs a prototype and a `Builtins` entry), E4 (stock rejection), E5 (the two postfixes), E9 (kASM symbol from the row), E10 (`LoadEmbedded` postfix).
[^header-generators]: VehicleHeader.cs:89–113 (kC `kfs_sensors_t`); BuiltinIncludes.cs:68–71 (kASM `kfs_sensors_t`), :43–46 (kASM `SVC_*` constants from `ServicesData.Services`); KSA.Kfs.KC/Parser.cs:3085–3103 (kC bindings from the rows' `kc` column).
[^image-reader]: ImageReader.cs — unknown service id :557–560; table versions :642–648; message not in the catalogue :655; `body_size` mismatch :684–687; layout fingerprint :777.
