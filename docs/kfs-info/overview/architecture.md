---
type: Concept
title: kFS architecture
description: The whole of kFS on one page — its layers, the object graph, how a program gets from source to a running vehicle, what happens in one tick, the channels between a program and the game, and which parts are inactive in this build.
resource: ksa-decomp:KSA.Kfs/KfsIntegration.cs
tags: [kfs, overview, architecture, threading, channels]
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
  - id: kfs-services
    resource: ksa-decomp:KSA.Kfs/KfsServices.cs
    title: KfsServices (decompiled)
  - id: kfs-prototype
    resource: ksa-decomp:KSA.Kfs/KfsPrototype.cs
    title: KfsPrototype (decompiled)
  - id: kfs-controller-set
    resource: ksa-decomp:KSA.Kfs/KfsControllerSet.cs
    title: KfsControllerSet (decompiled)
  - id: host-computer
    resource: ksa-decomp:KSA.Kfs.Host/HostComputer.cs
    title: HostComputer (decompiled)
  - id: vehicle-computer
    resource: ksa-decomp:KSA.Kfs.Host/VehicleComputer.cs
    title: VehicleComputer (decompiled)
  - id: system-headers
    resource: ksa-decomp:KSA.Kfs.KC/SystemHeaders.cs
    title: SystemHeaders (decompiled)
  - id: services-json
    resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.services.json
    title: Embedded services table
  - id: catalogue-json
    resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.catalogue.json
    title: Embedded processor catalogue
generated: { by: claude-code/claude-fable-5-2, at: 2026-11-25T22:02:02Z }
status: stable
ksa_build: "2026.11.25.5949"
---

# What kFS is

kFS is a programmable flight computer built into Kitten Space Agency. A player writes a program in
**kC** (a C subset) or **kASM** (assembly), the game compiles it to a bytecode **image**, and a
virtual machine — the **kVM** — runs that image on a fixed tick, a few times to a hundred times per
simulated second, on board one vehicle. The program reads vehicle state and commands thrusters,
engines and gimbals directly, or asks the stock flight computer to do things on its behalf.

Everything lives in `KSA.dll` under sixteen `KSA.Kfs*` namespaces (about 65,000 decompiled lines)
plus fourteen embedded JSON data tables and three embedded library sources. See the
[source map](/overview/source-map.md).

In this build kFS is a **prototype behind a flag**: `KfsPrototype.Enabled` starts from the
environment variable `KFS_PROTOTYPE=1` and is toggled at run time with the console command
`kfs on` / `kfs off`.[^kfs-prototype] With the flag off, the stock flight computer flies everything
and only `kfs help|on|off` respond. See [the feature flag](/integration/feature-flag.md).

# The layers

```text
 player                kC source (.c/.h)            kASM source (.kasm/.kinc)
   │                        │                              │
 TOOLING   Workbench · KfsEditorWindow · `kfs …` console command · reference/debug windows
   │                        │                              │
 TOOLCHAIN          KC front end ──► IR ──► Peephole ──┐   │
                                                       ├─► Emitter ──► ImageBuilder ──► image bytes
                                    Assembler ─────────┘
   │
 TABLES    GameTables: opcodes, costs, traps, sysblock, services, messages, events, enums, catalogue …
   │       (one instance, loaded lazily from resources embedded in KSA.dll; read by every layer)
   │
 VM        KvmMachine: loads an image, runs init / periodic / program tasks inside a cycle budget,
   │       validates and queues service calls, holds the command block and the message slots
   │
 HOST      HostComputer ► VehicleComputer (per vehicle) / BenchMachine (off-vehicle)
   │       tick grid, host-published messages, service routing, burn handles, console log, traces
   │
 GAME GLUE KfsIntegration · KfsDispatch · KfsVehicleHost · KfsControllerSet · KfsHostSampler · KfsServices
   │
 GAME      PhysicsBubble (flight-computer pass) · FlightComputer · Vehicle / parts / modules · Program
```

| Layer | Namespace(s) | Bundle section |
| --- | --- | --- |
| Language | `KSA.Kfs.KC` | [/language/](/language/index.md) |
| Compiler, assembler, image | `KSA.Kfs.Compiler*`, `KSA.Kfs.KAsm`, `KSA.Kfs.Vm.Image`, `KSA.Kfs.Vm.Debug` | [/compiler/](/compiler/index.md) |
| Virtual machine | `KSA.Kfs.Vm` | [/vm/](/vm/index.md) |
| Data tables | `KSA.Kfs.Vm.Tables` + embedded JSON | [/tables/](/tables/index.md) |
| Host | `KSA.Kfs.Host`, `.Host.Services`, `.Host.Views` | [/host/](/host/index.md) |
| Game glue | `KSA.Kfs` (non-UI types) | [/integration/](/integration/index.md) |
| Tooling | `KSA.Kfs.Host.Workbench`, `KSA.Kfs.Cli`, UI types in `KSA.Kfs` | [/tooling/](/tooling/index.md) |

# Object graph at run time

```text
KfsIntegration (static)
 ├─ Tables     : GameTables      lazily built on first use by GameTables.LoadEmbedded()
 ├─ Workbench  : Workbench       lazily built; workspace folder, build/load/start, bench machine
 ├─ Output     : TextWriter      where `kfs …` command replies go (default: the game console)
 └─ Editor     : KfsEditorWindow created on first `kfs editor`

Vehicle.KfsHost : KfsVehicleHost?          one per vehicle that the workbench has targeted
 ├─ Computer : VehicleComputer             (HostComputer subclass) owns the KvmMachine
 │    ├─ Machine  : KvmMachine             the VM instance and the loaded image
 │    ├─ Console  : ConsoleLog             program log lines and host events
 │    ├─ Handles  : BurnHandles            burns planned through services
 │    └─ Override : ServiceOverride        throttle / engine switch set through services
 ├─ Frozen   : KfsControllerSet            thrusters/engines/gimbals sorted by InstanceId → I/O layout
 ├─ Current  : KfsControllerSet            the vehicle's hardware now (for change detection)
 └─ Services : KfsServices                 applies drained service requests; main-thread work queue
```

`KfsIntegration.Tables` is `LazyInitializer.EnsureInitialized(ref _tables, GameTables.LoadEmbedded)`,
and nothing at game start-up reads it; the first reader is the `Workbench` getter or
`KfsVehicleHost.For`.[^kfs-integration] One `GameTables` instance is shared by reference by the
compiler, the assembler, every machine and the reference window — see
[the tables loader](/tables/game-tables-loader.md).

A host is created only when the workbench targets "the vehicle" (the player's controlled vehicle),
has no explicit end of life, and is never saved — see
[lifecycle and persistence](/integration/lifecycle-and-persistence.md). Every vehicle computer
emulates the same processor, the catalogue's "Bench" part (a copy of Flight Computer Mk2: core
class K32F, 1 MHz, 131,072 bytes of RAM); nothing maps game parts to catalogue
processors.[^kfs-vehicle-host][^catalogue-json]

# From source to a running program

1. **Write.** Files live in the workspace, `<Documents>/My Games/Kitten Space Agency/kfs` by
   default. A subfolder is a project; a root file is its own project. See
   [workspace and files](/tooling/workspace-and-files.md).
2. **Build.** `kfs build <file>` or the editor. kC goes through the front end
   (preprocess → parse → analyse → lower to IR), the IR validator, the peephole optimiser and the
   emitter; pure kASM goes straight from the assembler to the emitter. See
   [the compile pipeline](/compiler/compile-pipeline.md).
3. **Image.** The result is a byte array: an 80-byte header, a directory, eight sections
   (messages, tasks, links, strings, enums, code, data, debug info), and a CRC. The header records the processor,
   the **layout fingerprint** of the vehicle it was built for, and the **service-table** and
   **message-catalogue versions**. See [the image format](/compiler/kvm-image-format.md).
4. **Load.** `kfs load <file> --vehicle` rebuilds for the target's layout and queues
   `Computer.Load(image, layout)`; the load runs on the vehicle's next dispatch. The loader rejects
   an image whose part, layout fingerprint, table versions, service ids or message ids do not match
   the running tables. See [build and load flow](/tooling/build-and-load-flow.md).
5. **Start.** `kfs start` queues a start; boot ticks run the `init` task, then periodic tasks run
   every tick.
6. **Observe.** `kfs_logf` lines and host events go to the computer's own `ConsoleLog` (not the
   game console); `trace` channels and watches feed the editor's scope. See
   [console commands](/tooling/console-commands.md) and [the editor](/tooling/editor-window.md).

A player-level walkthrough is in the [quick start](/tooling/user-quick-start.md).

# One frame, one dispatch, one tick

```text
MAIN THREAD — Program.PrepareFrame            (no solver job is running in this window)
  wait for the previous vehicle-solver job, apply its results to the live vehicles
  Glfw.PollEvents()
  KfsIntegration.BeforeInputEvents()           ← kFS main-thread hook: release dead hosts, then
                                                 every host's Services.MainThread() (stage, plan burns…)
  InputEvents.ApplyInputEvents()
  start the next vehicle-solver job            ──────────────┐
MAIN THREAD — UI and rendering (editor, console, part windows) runs CONCURRENTLY with ▼

SOLVER / VehicleWorker THREADS — PhysicsBubble flight-computer pass, per vehicle
  KfsDispatch.TryComputeControl(vehicleState, in nav, ref output)
    KfsVehicleHost.Dispatch → DispatchCore
      Services.BeforeDispatch                  pilot-override detection
      Computer.Dispatch(timeNs, sampler)       drain queued actions (load/start/stop…), then for each
        │                                      tick that is due on the tick grid:
        ├─ sample the game → pack host-published messages into the VM's message slots
        ├─ KvmMachine.RunTick                  tasks run inside the cycle budget;
        │     SYSCALL → validate args → IssueResult (synchronous) → queue ServiceRequest
        ├─ latch the command block (full control, controllable vehicle, tick not overrun)
        └─ drain requests → HostComputer.ApplyHostRequest → VehicleComputer.ApplyRequest
      Services.AfterDispatch                   apply drained requests to the worker flight computer
                                               or queue MainWork for the main thread
      write the latched commands into the thruster / engine / gimbal controller states
    returns false → the stock FlightComputer.ComputeControl runs instead
```

The essential rules, established in [dispatch and threading](/integration/dispatch-and-threading.md):

- The dispatch **never runs on the main thread**. Vehicles in different physics bubbles dispatch in
  parallel; one host is never dispatched concurrently with itself, but its thread changes.
- In a dispatch the live `Vehicle` is a **read-only** source. kFS writes only worker-side copies
  (the worker flight computer, per-module controller state buffers) and defers everything else to
  `KfsServices.MainThread()`, drained from `BeforeInputEvents` on the main thread.[^kfs-services]
- **Paused means no dispatch.** Queued loads, starts and anything else waiting on a dispatch do not
  happen until time advances; `BeforeInputEvents` keeps running.
- An exception inside a dispatch is caught by `KfsDispatch`, halts that computer with a sticky host
  fault and hands the vehicle to the stock flight computer.[^kfs-dispatch] An exception on the
  main-thread side is not caught by kFS.
- A running (non-quiescent) computer keeps its vehicle in full physics with one substep per tick.

Inside the VM, tasks run in image order within a budget of `clock_hz / tick_hz` cycles (20,000 at
50 Hz on every catalogue part); a periodic overrun abandons the tick — no latch, requests discarded
— and a trap halts the machine. See [tasks and scheduling](/vm/tasks-and-scheduling.md),
[the cost model](/vm/cost-model-and-budgets.md) and [traps](/vm/traps-and-faults.md).

# The channels between a program and the game

| Channel | Direction | What it carries | Defined by | Extensible by a mod? |
| --- | --- | --- | --- | --- |
| **Command block** — memory-mapped at `0x90000000` | program → game | `thruster[i].pulse_s`, `engine[i].throttle`, `engine[i].lease_s`, `gimbal[i].y/z`, generated per vehicle | `LayoutGenerator.Vehicle` and the layout fingerprint | Costly: changes the fingerprint of every image |
| **System block** — read-only at `0xA0000000` | VM → program | tick counters, time, budget, reset cause (`sys.*`) | hard-coded offsets in `KvmMachine` | No |
| **Services** — `SYSCALL <id>` | program → game, optionally one result cell back | log, trace, halt, stage, attitude holds, throttle, burn planning | `services.json` plus three hard-coded switches | **Yes — the practical route** |
| **Message bus** — fixed-layout messages in VM slots | game → program (host-published) and program → program/game | `MSG_NAV_STATE`, `MSG_FLIGHT_DATA`, `MSG_VEHICLE_PROPS`, `MSG_BURN_STATUS`, …; programs publish `MSG_WRENCH_CMD` and friends | `messages.json` plus a hard-coded packer for seven ids | Partly: catalogue rows are data, the packer is not |

Things the first reading of the code suggests but that are **not true in this build**:

- Vehicles have **no sensor block**. `LayoutGenerator.Vehicle` produces a command block only; the
  IMU sensor fields exist only in the bench layout (where the game writes zeros to them). All
  program inputs on a vehicle are messages.
  See [I/O layout generation](/host/io-layout-generation.md).
- Service ids are **sparse**, not 1–67: 1, 2, 3, (4 reserved), 16, 17, 32, 48–57, 64–67 — twenty
  active rows.[^services-json] See [the services table](/tables/services-table.md).
- `<kfs.h>` is a **hard-coded string**, not generated from the tables; only `<vehicle.h>` (layout
  structs and message types) is generated.[^system-headers] See
  [generated headers](/language/kc-generated-headers.md).
- A service call passes **three** hard-coded id switches, not one:
  `HostComputer.ApplyHostRequest` (1, 2, 3, 16, 17), `VehicleComputer.ApplyRequest`
  (32, 48–57, 64–67) and `KfsServices.Apply`. An id none of them knows is rejected with
  "no vehicle to act on".[^host-computer][^vehicle-computer] See
  [service request handling](/host/service-request-handling.md).

# Two ways a program can fly

A loaded image is one of two shapes, decided at load from what it claims and publishes — see
[ownership and control modes](/host/ownership-and-control-modes.md) and
[flight-computer interplay](/integration/flight-computer-interplay.md):

| Mode | The program… | Stock flight computer | Typical code |
| --- | --- | --- | --- |
| **Pilot** | calls services (`kfs_hold_heading`, `kfs_set_throttle`, `kfs_stage`, `kfs_plan_burn`…) | still runs every pass, steered by the program | [`kC_ascent_simple`](/tooling/examples.md) |
| **Full control** | claims `ALLOCATION`, subscribes to nav/props messages, writes `commands.*` each tick | does not run; steering services are rejected | [`kC_attitude_control`](/tooling/examples.md) |

Services that do not steer — `log`, `trace`, `halt`, `stage`, burn planning — work in both modes.
That is the category a mod-added "vehicle systems" service belongs to.

# Stable hardware indices

A program addresses hardware by index: `commands.thruster[3]`, `props.engine[0]`. The index is the
rank of the controller module by `ModuleBase.InstanceId` within the vehicle, captured in
`KfsControllerSet` when the host is created ("frozen"). When hardware leaves (staging, undocking)
its slot stays and becomes inert; hardware that arrives by docking gets no index.[^kfs-controller-set]
See [controller set and layout changes](/integration/controller-set-and-layout-changes.md). This is
the model to copy for any other indexed vehicle hardware.

# What is not active in this build

| Feature | Status | Where |
| --- | --- | --- |
| Sleeping (`kfs_sleep_until`, `kfs_sleep_until_event`) and all events | Requests are only logged; nothing ever sleeps or wakes | [events table](/tables/events-table.md), [service request handling](/host/service-request-handling.md) |
| kScript | No implementation: the compiler answers a `.py` source with "not implemented until MS9", and the workspace does not accept `.py` files at all | [kScript status](/language/kscript-status.md) |
| `translate` service (54) | Always rejected | [KfsServices](/integration/kfs-services-game-side.md) |
| Non-volatile memory | Unmapped; only constants exist | [memory map](/vm/memory-map-and-sysblock.md) |
| Multiple computers per vehicle, message wake | Computer index is always 0 | [message bus](/host/message-bus.md) |
| Per-part processors (Sequencer, Mk1, Mk3…) | Every vehicle uses the Bench part | [processor catalogue](/tables/catalogue-processors.md) |
| Strict mode, WCET-versus-budget enforcement | Never applied | [program analysis](/compiler/program-analysis-and-wcet.md) |
| Saving kFS state | Nothing is saved; `SaveState`/`LoadState` have no callers | [lifecycle](/integration/lifecycle-and-persistence.md), [machine state format](/vm/machine-state-format.md) |
| The `KSA.Kfs.Cli` entry points | Public but unreachable in the shipped game | [CLI](/tooling/cli.md) |

The data tables cite internal specification documents and numbered decisions (for example "kFS
D-124", "integration §2.4") that are not available; the only map of them is
[spec and decision references](/tables/spec-and-decision-references.md).

# Modding notes

- The whole toolchain, VM and host are **public types** and run without the game: the tables load
  from embedded resources, `FakeVehicle` stands in for a vehicle, and the game's own `Workbench`
  can be driven headlessly. This has been exercised — see
  [the offline harness](/modding/offline-harness.md).
- The services channel is data-driven end to end **except** for the three id switches and the
  kC header. A mod can add services with one augmented table, one header string and two Harmony
  postfixes — see [adding a service](/modding/adding-a-service.md) and the
  [extension-point map](/modding/extension-points.md).
- Never bump the table versions and never change a stock layout: both invalidate every existing
  image. See [risks and compatibility](/modding/risks-and-compatibility.md).

[^kfs-integration]: KfsIntegration.cs:85–121 (lazy `Tables`, `Workbench`), :146–156 (`RegisterCommands`), :158–176 (`BeforeInputEvents`).
[^kfs-dispatch]: KfsDispatch.cs:7–27.
[^kfs-vehicle-host]: KfsVehicleHost.cs:105–118 (constructor, `tables.BenchPart`), :139–147 (`For` / `Find`), :488–616 (`DispatchCore`).
[^kfs-services]: KfsServices.cs:310–449 (`Apply`), :543–624 (`Queue`, `MainThread`).
[^kfs-prototype]: KfsPrototype.cs:8–28.
[^kfs-controller-set]: KfsControllerSet.cs:60–85 (ordering and layout), :112–139 (`Matches`, `MarkDeparted`).
[^host-computer]: HostComputer.cs:670–676 (`ISyscallSink.Request`), :747–811 (`ApplyHostRequest`), :813–816 (`ApplyRequest` default).
[^vehicle-computer]: VehicleComputer.cs:174–183 (`IssueResult`), :185–239 (`ApplyRequest`).
[^system-headers]: SystemHeaders.cs:8–41.
[^services-json]: KSA.Kfs.Vm.Tables.Data.services.json — the `services` array.
[^catalogue-json]: KSA.Kfs.Vm.Tables.Data.catalogue.json — `parts` and `bench_part`.
