---
type: Glossary
title: kFS glossary
description: Definitions of the terms used across the kFS code, its data tables and this bundle, each with a pointer to the document that covers it.
tags: [kfs, overview, glossary]
generated: { by: claude-code/claude-fable-5-2, at: 2026-11-25T22:02:02Z }
status: stable
ksa_build: "2026.11.25.5949"
---

# Languages and artefacts

| Term | Meaning | More |
| --- | --- | --- |
| **kFS** | The programmable flight computer feature as a whole; also the prefix of the game glue types (`KfsIntegration`, `KfsVehicleHost`, …). | [architecture](/overview/architecture.md) |
| **kC** | The C-subset language players write. Files `.c` / `.h`. | [language reference](/language/kc-language-reference.md) |
| **kASM** | The assembly language of the kVM. Files `.kasm` / `.kinc`. | [kASM](/compiler/kasm-assembly-language.md) |
| **kScript** | A third, Python-like language named in the tables. Not implemented in this build. | [kScript status](/language/kscript-status.md) |
| **kVM** | The bytecode virtual machine, class `KvmMachine`. 32-bit cells on every core class. | [kVM machine](/vm/kvm-machine.md) |
| **image** | The compiled program as a byte array: header, directory, sections, CRC. | [image format](/compiler/kvm-image-format.md) |
| **IR** | The compiler's intermediate form: kVM opcodes with symbolic operands. | [IR reference](/compiler/ir-reference.md) |
| **`<kfs.h>`** | The hard-coded system header declaring `kfs_*` services, `KFS_*` constants, `sys`, `vec3`/`quat`/`mat3`. | [generated headers](/language/kc-generated-headers.md) |
| **`<vehicle.h>` / `<vehicle.kinc>`** | Headers generated per layout: `commands`, `THRUSTER_COUNT`, `MSG_*`, `kfs_msg_*_t`. | [generated headers](/language/kc-generated-headers.md) |
| **builtin** | A kC function with no body that the front end resolves itself: its name is in `SystemHeaders.Builtins` and it was declared in a `<…>` header. | [builtins and intrinsics](/language/kc-builtins-and-intrinsics.md) |
| **`#pragma kfs`** | The directive family that declares tick rate, tasks, bus subscriptions/publications and claims. | [program structure](/language/kc-program-structure.md) |

# Machine concepts

| Term | Meaning | More |
| --- | --- | --- |
| **cell** | One 32-bit stack slot. `i64` and `f64` take two; a `vec3` argument takes three. | [kVM machine](/vm/kvm-machine.md) |
| **core class** | `K8`, `K32`, `K32F`, `K32D` — changes costs, soft-arithmetic flags and limits, not word size. | [cost model](/vm/cost-model-and-budgets.md) |
| **part** (catalogue) | A processor description: core class, clock, RAM, code size. Not a game part. | [processor catalogue](/tables/catalogue-processors.md) |
| **Bench part** | The catalogue processor every computer actually uses in this build (a copy of Flight Computer Mk2). | [processor catalogue](/tables/catalogue-processors.md) |
| **tick** | One scheduler step of the VM. Rate `tick_hz` ∈ {5, 10, 20, 25, 50, 100}. | [tasks and scheduling](/vm/tasks-and-scheduling.md) |
| **budget** | Cycles available per tick: `clock_hz / tick_hz`. | [cost model](/vm/cost-model-and-budgets.md) |
| **task** | An entry point: `init` (boot), periodic (`rate_hz`), or the single kASM-only `program` task. | [tasks and scheduling](/vm/tasks-and-scheduling.md) |
| **overrun** | A periodic task exceeding the budget: the tick is abandoned, nothing is latched, queued requests are dropped. | [tasks and scheduling](/vm/tasks-and-scheduling.md) |
| **trap** | A fatal VM error (`BAD_SYSCALL`, stack errors, …); the machine halts. | [traps and faults](/vm/traps-and-faults.md) |
| **soft routine** | Bookkeeping for arithmetic the core has no hardware for: it adds cost and code size; no routine code runs. | [soft routines](/vm/soft-routines-and-fp.md) |
| **sysblock** / `sys` | The read-only system block at `0xA0000000`. | [memory map](/vm/memory-map-and-sysblock.md) |
| **WCET** | Worst-case execution time computed by the compiler's static analysis. | [program analysis](/compiler/program-analysis-and-wcet.md) |

# Program ↔ game interface

| Term | Meaning | More |
| --- | --- | --- |
| **service** | A numbered request from a program to its host, issued with `SYSCALL <id>`; kinds `request` and `request+result`. | [syscall ABI](/vm/services-syscall-abi.md), [services table](/tables/services-table.md) |
| **`ServiceRequest`** | The record a service call becomes: id, argument cells, optional bytes, task, result cells. | [syscall ABI](/vm/services-syscall-abi.md) |
| **argument domain** | A per-cell validity rule from the services table (`any`, `finite`, `finite:[a,b]`, `enum:x`, `handle`, …) checked by the VM at issue time. | [syscall ABI](/vm/services-syscall-abi.md) |
| **issue-time rejection** | A request refused by the VM's domain check. | [syscall ABI](/vm/services-syscall-abi.md) |
| **host rejection** | A request refused later by the host or game (`RejectByHost`), logged as "rejected <name>: <reason>". | [service request handling](/host/service-request-handling.md) |
| **cost class** | `Light`, `Medium` or `Heavy`: the cycle cost of a service call. | [cost model](/vm/cost-model-and-budgets.md) |
| **I/O layout** | The named, typed, offset fields of a vehicle's sensor and command blocks, with a CRC-32 fingerprint. | [I/O layout generation](/host/io-layout-generation.md) |
| **command block** | Program-writable memory at `0x90000000` holding actuator commands. | [memory map](/vm/memory-map-and-sysblock.md) |
| **sensor block** | Read-only memory at `0x80000000`. Empty on vehicles; only the bench layout has fields. | [I/O layout generation](/host/io-layout-generation.md) |
| **latch** | Copying the command block out to the host at the end of a successful tick. | [vehicle computer](/host/vehicle-computer.md) |
| **one-shot field** | A command field cleared after each latch (`thruster[i].pulse_s`). | [I/O layout tables](/tables/io-layout-tables.md) |
| **layout fingerprint** | CRC-32 of the canonical layout text; baked into images and checked at load. | [I/O layout tables](/tables/io-layout-tables.md) |
| **message** / **bus** | Fixed-layout records (`MSG_*`) held in VM slots; tasks subscribe and publish through bound globals. | [message bus](/host/message-bus.md), [messages table](/tables/messages-table.md) |
| **host-published message** | One of the seven message ids the host packs from game state each tick. | [host sampling](/host/host-sampling-and-leaves.md) |
| **leaf** | A single scalar value in a host-published message, filled by `KfsHostSampler`. | [host sampler](/integration/host-sampler.md) |
| **claim** / **ownership** | A program's declaration that it provides a function of the stock computer (`ALLOCATION`, `ATTITUDE_CONTROL`, `GUIDANCE`). | [ownership and control modes](/host/ownership-and-control-modes.md) |
| **pilot mode** | No claim: the stock flight computer still runs and the program steers it through services. | [flight-computer interplay](/integration/flight-computer-interplay.md) |
| **full control** | The program claims `ALLOCATION` and writes the command block; the stock computer does not run. | [flight-computer interplay](/integration/flight-computer-interplay.md) |
| **fallback** | The vehicle is not controllable: the VM still ticks but nothing is latched and the stock computer runs. | [dispatch and threading](/integration/dispatch-and-threading.md) |
| **handback** | Returning the vehicle to the pilot/stock computer when a program stops flying it. | [flight-computer interplay](/integration/flight-computer-interplay.md) |
| **burn handle** | A `u32` returned by `kfs_plan_*`, tracking a planned burn through its life. | [service request handling](/host/service-request-handling.md) |
| **service override** | Throttle and engine-switch values a pilot-mode program sets through services. | [service request handling](/host/service-request-handling.md) |

# Host and game glue

| Term | Meaning | More |
| --- | --- | --- |
| **host** | (1) The layer between VM and game (`HostComputer` and friends). (2) A `KfsVehicleHost`, the per-vehicle glue object. | [host computer](/host/host-computer.md), [KfsVehicleHost](/integration/kfs-vehicle-host.md) |
| **bench** | The off-vehicle machine (`BenchMachine`) the workbench targets by default. | [bench machine](/host/bench-machine-and-fake-vehicle.md) |
| **`FakeVehicle`** | A public test double for a vehicle computer, with a generated layout. | [bench machine](/host/bench-machine-and-fake-vehicle.md) |
| **dispatch** | One call of `KfsDispatch.TryComputeControl` for one vehicle in a flight-computer pass; runs zero or more ticks. | [dispatch and threading](/integration/dispatch-and-threading.md) |
| **tick grid** | The lattice of due tick times against simulation time, with catch-up and skip rules. | [tick grid and timing](/host/tick-grid-and-timing.md) |
| **frame budget** | Host wall-clock time one vehicle computer may use per frame (4 ms by default). | [dispatch and threading](/integration/dispatch-and-threading.md) |
| **sync window** | The part of a frame on the main thread where no vehicle-solver job is running; where `BeforeInputEvents` runs. | [dispatch and threading](/integration/dispatch-and-threading.md) |
| **`MainWork`** | `KfsServices`' private queue of actions that must run on the main thread. | [KfsServices](/integration/kfs-services-game-side.md) |
| **frozen / current set** | The hardware list captured at host creation versus the vehicle's hardware now. | [controller set](/integration/controller-set-and-layout-changes.md) |
| **inert** | A frozen-set element whose hardware has left the vehicle. | [controller set](/integration/controller-set-and-layout-changes.md) |
| **quiescent** | A halted computer holding no commands; lets the vehicle return to rails. | [vehicle computer](/host/vehicle-computer.md) |
| **host fault** | An exception in the dispatch path; halts that computer until reloaded or restarted. | [KfsVehicleHost](/integration/kfs-vehicle-host.md) |
| **workbench** | The headless tooling object behind most `kfs` verbs and the editor. | [workbench](/tooling/workbench.md) |
| **workspace** | The folder of program sources on disk. | [workspace and files](/tooling/workspace-and-files.md) |
| **target** | What the workbench loads onto: the bench or the controlled vehicle. | [workbench](/tooling/workbench.md) |

# Reference frames and units in names

Identifiers carry their frame and unit as suffixes: `v_cci_mps`, `rates_body_rps`,
`torque_body_Nm`, `q_body_to_cci`. Frames: `CCI` celestial-centred inertial, `CCF`
celestial-centred fixed, `BODY` vehicle body (X forward, Y right, Z down), `HELM` the control
point, `ENU` east-north-up, `LVLH` local-vertical local-horizontal, `VLF` velocity frame
(prograde/normal/radial). See [host sampling and leaves](/host/host-sampling-and-leaves.md).

# Game-side terms used by the parts extension

| Term | Meaning | More |
| --- | --- | --- |
| **module** | A component on a part (`LightModule`, `KeyframeAnimationModule`, `PowerConsumer`, `EngineController`…), enumerated with `vehicle.Parts.Modules.Get<T>()`. | [part and module model](/game-targets/part-and-module-model.md) |
| **`InstanceId`** | A session-wide running id on every module and part; stable across staging and docking, not saved. | [part and module model](/game-targets/part-and-module-model.md) |
| **light switch** | `Part.LightSwitch`, a `PowerConsumer` whose `LightIsActive` turns every light on that full part on or off. | [light module](/game-targets/light-module.md) |
| **light template** | `LightModule.Template`, the object holding colour, cone angles, intensity and range — shared by every light of the same part type until cloned. | [light module](/game-targets/light-module.md) |
| **`TimeGoal`** | The commanded position of a `KeyframeAnimationModule`, in seconds from 0 to `Shared.Duration`. | [keyframe animation module](/game-targets/keyframe-animation-module.md) |
| **deploy vs actuator animation** | The same class; `ShowDeployRetract` only selects a Deploy/Retract button or a 0–1 slider in the stock UI. | [keyframe animation module](/game-targets/keyframe-animation-module.md) |
