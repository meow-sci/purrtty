---
okf_version: "0.2"
---

# kfs-info — the kFS knowledge bundle

A reference to **kFS**, the programmable flight computer built into Kitten Space Agency: the kC
language, the kASM assembler, the kVM bytecode machine, the host layer and the game glue — written
for people extending it from a mod. Everything was derived from the decompiled game assemblies of
build **2026.11.25.5949**; a few documents were additionally verified by running the real
toolchain outside the game. Nothing has been checked in the running game.

## Start here

- New to kFS: [architecture](/overview/architecture.md), then the [glossary](/overview/glossary.md).
- Want to use it as a player: [quick start](/tooling/user-quick-start.md), [console commands](/tooling/console-commands.md), [examples](/tooling/examples.md).
- Writing kC: [language reference](/language/kc-language-reference.md), [program structure](/language/kc-program-structure.md), [builtins](/language/kc-builtins-and-intrinsics.md).
- Extending it from a mod: [extension-point map](/modding/extension-points.md), [adding a service](/modding/adding-a-service.md), [offline harness](/modding/offline-harness.md), [risks](/modding/risks-and-compatibility.md).
- Threading questions: [dispatch and threading](/integration/dispatch-and-threading.md).
- Finding code: [source map](/overview/source-map.md).
- How this bundle is written: [conventions](/overview/bundle-conventions.md). Changes: [log](/log.md).

## Overview

Orientation: the mental model, vocabulary and code map. Read first. Directory index: [/overview/](/overview/index.md).

- [kFS architecture](/overview/architecture.md) — The whole of kFS on one page — its layers, the object graph, how a program gets from source to a running vehicle, what happens in one tick, the channels between a program and the game, and which parts are inactive in this build.
- [kFS glossary](/overview/glossary.md) — Definitions of the terms used across the kFS code, its data tables and this bundle, each with a pointer to the document that covers it.
- [kFS source map](/overview/source-map.md) — Where every piece of kFS lives in the decompiled game — each namespace with its size, role and key files, the embedded resources, the non-kFS game files that call into kFS, and the bundle document that covers each.
- [kfs-info bundle conventions](/overview/bundle-conventions.md) — Authoring rules for this OKF bundle — frontmatter template, source URI schemes, citation style, evidence labels, and the directory layout.

## Language — kC

The C-subset language players write: syntax, types, preprocessor, program structure, builtins, headers, library, diagnostics. Directory index: [/language/](/language/index.md).

- [kC builtins and intrinsics](/language/kc-builtins-and-intrinsics.md) — Every compiler-recognised function in kC — intrinsics, the kfs_* service bindings and how a call lowers to SYSCALL, kfs_logf and trace, and how kC code reads sensors, writes commands and uses bus messages.
- [kC diagnostics](/language/kc-diagnostics.md) — Catalogue of the diagnostics a kC programmer can hit — code, severity, message text and cause for everything the kC front end emits, plus the table rules that are listed for kC but never raised in this build.
- [kC front end internals](/language/kc-front-end-internals.md) — How KSA.Kfs.KC turns source into IR — the Lexer, Preprocessor, Parser, Sema, Checks and Lowering phases, their data structures, where names and builtins are resolved, and which IrBuilder calls lowering makes.
- [kC language reference](/language/kc-language-reference.md) — The kC source language as the in-game compiler accepts it — lexical rules, declarations, statements, expressions, what C99 it drops, and the hard translation limits.
- [kC preprocessor](/language/kc-preprocessor.md) — The kC preprocessor — directives, macro expansion rules,
- [kC program structure](/language/kc-program-structure.md) — How a kC program is assembled from files and declares its tick rate, init and periodic tasks, bus buffers and ownership through
- [kC standard library](/language/kc-standard-library.md) — Every function of the bundled kC library — math.c, vecmat.c and string.kasm — with signatures and behaviour, and how library source is pulled into a program and pruned.
- [kC system and generated headers](/language/kc-generated-headers.md) — Exactly which header text kC sees, where each piece comes from (hard-coded strings versus the services, messages and layout tables), when it is generated and cached, and what a mod must change for new functions, messages, enums or I/O fields to appear.
- [kC types and conversions](/language/kc-types-and-conversions.md) — The kC type system as implemented by CType and Sema — sizes and alignment, struct layout, compatibility, implicit conversions, literal typing, constant folding, and how core class and double_bits affect it.
- [kC worked examples](/language/kc-examples.md) — The idioms of real kC programs, drawn from the examples embedded in the game, plus five small complete programs constructed from constructs the front end is known to accept.
- [kScript status](/language/kscript-status.md) — Everything the decompiled build reveals about kScript, the planned third kFS language — table columns, enum values, diagnostics and back-end plumbing — and the finding that no kScript front end exists in this build.

## Compiler, assembler and image

The pipeline from source to image: IR, emitter, analyses, kASM, the binary image format. Directory index: [/compiler/](/compiler/index.md).

- [Backend emitter and peephole (IR to kVM image)](/compiler/backend-emitter.md) — How Emitter turns an IrModule into image bytes — task ordering, function placement and far calls, the calling convention and frames, RAM planning, how service calls, memory-mapped I/O and bus buffers appear in code, every emitter diagnostic, and the five peephole rewrites.
- [Bus analysis (static publish/subscribe attribution)](/compiler/bus-analysis.md) — How BusAnalysis decides which task reads and writes which message buffer, the rules it enforces (read-only subscriptions, single publisher, ALLOCATION for the command block), the per-task start and completion costs, and how the result becomes the image's Messages and Links tables.
- [Compile report (CompileReport and the Report* records)](/compiler/compile-report.md) — Every field of the build report the emitter produces — program memory, RAM, stacks, VM region, bus, calls, WCET and per-line estimates — with its JSON key, how it is computed, and the fixed text rendering.
- [Debug info (the image's Debug section and the DebugInfo model)](/compiler/debug-info.md) — The optional Debug section of a kVM image — its record model, exact byte layout, how the emitter fills it, the consistency checks applied at load, and which tools consume it.
- [Diagnostics infrastructure (Diagnostic, DiagnosticBag, DiagnosticRegistry, Severity, Span)](/compiler/diagnostics-infrastructure.md) — How compiler and assembler diagnostics are created, numbered, prefixed, capped and rendered, how they relate to the embedded diagnostics table, and every diagnostic number the middle end, back end and assembler emit.
- [kASM assembly language](/compiler/kasm-assembly-language.md) — The complete kASM language as implemented by the assembler — line syntax, macros and includes, every directive, instruction operands, the expression grammar, types, every symbol predefined from the game tables and the vehicle layout, limits, diagnostics and verified example programs.
- [kASM disassembler](/compiler/kasm-disassembler.md) — The text Disassembler.Disassemble produces from a loaded kVM image, what it preserves (the code section, byte for byte) and what it drops, and the tools built on it.
- [kFS compile pipeline (Compiler.cs end to end)](/compiler/compile-pipeline.md) — The stages that turn a kC or kASM project into a kVM image — inputs, options, the front-end seam, validation, peephole, emission, results, how kC and kASM mix, how the standard library is pulled in, and every limit.
- [kFS IR reference (IrModule, instructions, validator, printer, IrBuilder)](/compiler/ir-reference.md) — The compiler's intermediate representation — a symbolic form of kVM stack code — with every model type, the instruction set and operand kinds, the validator's rules, the printer's text form and the IrBuilder API.
- [kVM image format (KVM1) and load-time validation](/compiler/kvm-image-format.md) — The binary kVM image byte by byte — header, section directory, messages, tasks, links, strings, enums, code, data and debug sections, the trailing CRC — plus every version and fingerprint baked into an image and every check ImageReader performs against the running tables, part and layout, with each LoadResult rule.
- [Program analysis, stack figures and WCET](/compiler/program-analysis-and-wcet.md) — What ProgramAnalysis proves about a program before an image is built — operand-stack balance, no recursion, per-task stack and call-depth figures, YIELD placement, worst-case execution time — what is rejected at compile time versus only reported, and how the figures relate to the processor catalogue.
- [Toolchain extension points (what a mod must touch so programs can use new services, messages, enums or I/O fields)](/compiler/toolchain-extension-points.md) — A consolidated view, from the compiler, assembler and image loader's side, of which symbol tables and checks are derived from GameTables and which are hard-coded, where ids and fingerprints are verified, and the hooks and risks for a mod that adds game features to kFS.

## Virtual machine — kVM

The interpreter: opcodes, memory map, tasks, costs, traps and the service-call ABI. Directory index: [/vm/](/vm/index.md).

- [kVM cost model and cycle budgets (costs.json)](/vm/cost-model-and-budgets.md) — The complete cost table per core class, how an instruction's cycle cost is computed and charged, the per-tick and init budgets, overruns, and how service calls and soft routines are priced.
- [kVM enums (KSA.Kfs.Vm) with numeric values](/vm/vm-enums.md) — Every enum declared in the KSA.Kfs.Vm namespace with its underlying type, members and numeric values, where each is used, and how it lines up with enums.json.
- [kVM machine (KvmMachine) — execution model and API](/vm/kvm-machine.md) — How the kVM bytecode machine is constructed, loads an image, executes one tick, and reports the result, with the complete public surface of KvmMachine.
- [kVM machine state snapshot format (KVS1) — SaveState / LoadState](/vm/machine-state-format.md) — The binary layout KvmMachine.SaveState produces and every validation LoadState applies, for the save/restore API that exists in this build but has no caller.
- [kVM memory map, sensor/command windows and the system block](/vm/memory-map-and-sysblock.md) — The kVM address space — RAM regions, the read-only sensor window, the command window, the 256-byte system block (sysblock.json in full) — and the bounds, alignment and write checks applied to every access.
- [kVM message-bus slots — the VM side](/vm/bus-slots-vm-side.md) — How KvmMachine stores, copies, stamps, latches and exposes message-bus slots — the mechanics underneath the host's message bus.
- [kVM opcode table (opcodes.json) with implementation semantics](/vm/opcodes.md) — Every one of the 146 kVM opcodes — value, mnemonic, immediates, stack effect, cost category and per-core cycles — merged with what KvmMachine actually does for each.
- [kVM profiler, execution trace and diagnostics records](/vm/profiler-and-exec-trace.md) — The cycle profiler (Profiler, Profile, FunctionStats), the per-instruction execution trace (ExecTrace), the first-NaN record, the TickZones wall-clock tags and the OpCount constant.
- [kVM services — the SYSCALL ABI, argument domains, results and delivery](/vm/services-syscall-abi.md) — Exactly what the VM does when a program executes SYSCALL — table lookup, argument marshalling, domain validation, synchronous results, queuing, per-tick limits and delivery to the host — and what that means for adding new services.
- [kVM soft routines, floating-point self-test and arithmetic edge cases](/vm/soft-routines-and-fp.md) — What "soft" instructions are and how routines.json names and sizes them, the FpSelfTest that guards every machine construction with fpcheck.json, the Conv conversion helpers, and the edgecases.json conformance vectors, all reproduced in full.
- [kVM tasks and scheduling](/vm/tasks-and-scheduling.md) — The three task kinds (init, periodic, program), task status and scheduler phases, how a tick schedules, starts, suspends, completes and abandons tasks, and the inactive sleep/wake machinery.
- [kVM traps and faults (traps.json, TrapCode, TrapInfo)](/vm/traps-and-faults.md) — Every kVM trap code with its table text, the exact places KvmMachine raises it, what a trap does to the machine, and how the host is told.

## Embedded data tables

The JSON tables inside KSA.dll and the loader that validates them. Directory index: [/tables/](/tables/index.md).

- [catalogue.json — processor parts, tick rates and the bench part](/tables/catalogue-processors.md) — Schema and full contents of the embedded processor catalogue, how each PartDescription is derived, what the bench part is, and how a part is chosen for the bench and for vehicles.
- [diagnostics.json — the compiler diagnostic rule registry](/tables/diagnostics-table.md) — Schema and complete contents of the embedded diagnostics table (126 numbered rules for kC, kScript and kASM), and how the game uses it.
- [enums.json and CatalogueEnums — enumerations and the generated KFS_* constants](/tables/enums-table.md) — Schema and full contents of the embedded enums table, and the exact CatalogueEnums rules that turn table text into the KFS_* constants of kfs.kinc, with every generated name and value.
- [events.json — sleep events and wake reasons](/tables/events-table.md) — Schema and complete contents of the embedded events table, and how its ids tie to the sleep_until_event service and the system block's wake_reason.
- [GameTables loader — loading, validation, derived data, consumers](/tables/game-tables-loader.md) — How KSA.Kfs.Vm.Tables.GameTables reads the 14 embedded JSON tables, what it validates and derives, who consumes the result, and how a mod could inject extra rows.
- [I/O layout tables — IoLayout, LayoutFile, the bench layout and the fingerprint](/tables/io-layout-tables.md) — Schema of the memory-mapped I/O layout records, the validation and exact CRC-32 fingerprint algorithm, the complete bench_layout.json, and the SensorFile test-vector format.
- [Index of internal spec, section and decision references in the kFS tables and code](/tables/spec-and-decision-references.md) — Every RocketWerkz-internal spec document, section number, decision, proposal, open item and milestone cited in the embedded tables and in kFS string literals, with what each is cited for and where.
- [messages.json — the kFS message catalogue and its layout rules](/tables/messages-table.md) — Schema, packing rules and complete contents of the embedded message catalogue (ten bus messages), with every field's offset, size, type and meaning.
- [Row schemas of the VM tables — opcodes, costs, traps, sysblock, routines, fpcheck, edgecases](/tables/opcode-cost-trap-tables-schema.md) — The record types, JSON keys, optionality and load-time constraints of the seven VM-facing embedded tables, with pointers to the /vm/ documents that reproduce their contents.
- [services.json — the kFS service (syscall) table](/tables/services-table.md) — Schema, vocabulary and complete contents of the embedded services table that defines every SYSCALL id, its argument cells, domains, cost class and language bindings.

## Host layer

Between VM and game: computers, tick grid, layouts, messages, service routing. Directory index: [/host/](/host/index.md).

- [BenchMachine and FakeVehicle](/host/bench-machine-and-fake-vehicle.md) — The two off-vehicle hosts — BenchMachine, the workbench's free-running computer with zeroed sensors and no vehicle, and FakeVehicle, a test harness that runs a real VehicleComputer against a generated layout and scripted samples.
- [Host extension surface](/host/host-extension-surface.md) *(draft)* — Where a mod can attach to the kFS host layer — the few patch-free hooks, the Harmony patch targets with signatures and visibility, the four ways to deliver variable-length host data to a program, and every size limit that constrains a design.
- [Host sampling and leaves](/host/host-sampling-and-leaves.md) — The data a sampler hands the host each dispatch (HostSample with NavTruth, FlightDataValues, VehiclePropsValues and HostLeafValues), every field of the seven host messages with its type and offset, and when the host asks for each part.
- [Host view models (KSA.Kfs.Host.Views)](/host/views.md) — Every renderer-neutral view model in KSA.Kfs.Host.Views — bus monitor, disassembly, profiler, oscilloscope, compile report, NaN view, definitions, diagnostic manual, budget bars, line costs and layout helpers — with the data each draws and where that data comes from.
- [HostComputer](/host/host-computer.md) — HostComputer is the base host-side computer that owns one KvmMachine, loads images, runs one tick at a time, drains the VM's service requests, keeps the console log and publishes thread-safe snapshots.
- [I/O layout generation](/host/io-layout-generation.md) — How LayoutGenerator turns a vehicle's thruster, engine and gimbal counts into the memory-mapped command block layout (vehicles get no sensor fields; only the bench layout has them), what limits apply, and how the blocks are written and the command block latched each tick.
- [Message bus (host side)](/host/message-bus.md) — How the host packs its seven published messages into slot bytes and delivers them to a program each tick, how program publications come back, what BusTracker monitors, and which bus features are not active in this build.
- [Ownership and control modes](/host/ownership-and-control-modes.md) — How an image's claims and publications decide whether kFS merely steers the stock flight computer (pilot mode) or drives the actuators itself (full control), which images are refused, and how control is taken, lost and handed back.
- [Service request handling (host side)](/host/service-request-handling.md) — The complete host-side path of a kFS service call — from the VM's SYSCALL to ServiceRequest, the hard-coded host and vehicle switches, the queue the game drains, synchronous results, rejection, burn handles, the service override, the console log and traces.
- [Snapshots and performance data](/host/snapshots-and-perf.md) — The immutable MachineSnapshot a HostComputer publishes for other threads and everything inside it — task, function, trap, NaN and last-run records, performance series and reports — plus the tick recorder and the RAM view helpers.
- [Tick grid and timing](/host/tick-grid-and-timing.md) — How a vehicle computer maps simulation time onto tick indices with TickGrid, catches up or skips overdue ticks, honours a wall-clock deadline, reports overruns and tells the game when to wake it next.
- [VehicleComputer](/host/vehicle-computer.md) — VehicleComputer is the HostComputer subclass a vehicle carries; it schedules ticks against simulation time, publishes the host messages, latches thruster pulses, owns burn handles and the service override, and queues service requests for the game.

## Game integration

The KSA-side glue: dispatch and threading, services, sampling, write-back, lifecycle. Directory index: [/integration/](/integration/index.md).

- [Host lifecycle and persistence](/integration/lifecycle-and-persistence.md) — When a KfsVehicleHost is created and when it dies, what happens to it on staging, docking, vehicle destruction, save loading and star-system changes, and what kFS state does and does not survive a save.
- [Host sampler — game sources of every host-published value](/integration/host-sampler.md) — Every value KfsVehicleHost and KfsHostSampler put into the host-published messages, the exact game expression each one comes from, and the conditions under which each group is filled.
- [Interplay between kFS and the stock flight computer](/integration/flight-computer-interplay.md) — When the stock FlightComputer.ComputeControl still runs, how pilot mode and full control divide the work, which services need the stock computer, how the service override, burn modes and attitude modes behave, and what a handback does.
- [kFS dispatch and threading](/integration/dispatch-and-threading.md) — Which threads run the kFS dispatch, the main-thread hook and the UI, what each may read or mutate, and how wakeups, the frame budget, time warp and rails interact with a vehicle computer.
- [kFS game integration overview](/integration/game-integration-overview.md) — Every touch point between the KSA game code and kFS, the startup registration order, the object graph from KfsIntegration down to a vehicle's computer, and a per-frame and per-step walkthrough.
- [KfsControllerSet and layout changes](/integration/controller-set-and-layout-changes.md) — How a vehicle's thrusters, engines and gimbals get stable program-visible indices — ordering by InstanceId, the generated I/O layout, the frozen and current sets, and how elements go inert after staging or docking.
- [KfsServices — game-side service effects](/integration/kfs-services-game-side.md) — How KfsServices turns drained service requests into game effects, detects pilot overrides, tracks burn handles across a dispatch, and marshals work to the main thread through its MainWork queue.
- [KfsVehicleHost](/integration/kfs-vehicle-host.md) — The per-vehicle kFS host object — its fields, construction, liveness, the Dispatch and DispatchCore control flow branch by branch, sampling, fault handling and static state, with exact signatures and visibility.
- [Patch targets and seams in the kFS game glue](/integration/patch-targets-and-seams.md) *(draft)* — The Harmony patch-target candidates in the KSA-side kFS glue with exact signatures, visibility, thread and patchability concerns, and the recommended seams for observing service requests, running per-frame main-thread code and keeping per-host state.
- [The KfsPrototype feature flag](/integration/feature-flag.md) — The single process-wide switch that gates kFS — how it is initialised and changed, every place that reads it, and exactly what turning it off and back on does to running vehicle computers.
- [Write-back — how VM commands reach thrusters, engines and gimbals](/integration/write-back.md) — The full-control write-back path from a program's latched command block to the game's per-module controller state, including indexing, clamping and NaN handling, plus the KfsWriteBack helpers that reset the stock flight computer.

## Tooling

Workbench, editor, console commands, reference and debug windows, examples. Directory index: [/tooling/](/tooling/index.md).

- [Extension points in the kFS tooling](/tooling/tooling-extension-points.md) *(draft)* — Where a StarMap/Harmony mod can hook the kFS user tooling — shipping examples and headers, driving build/load/start from code, capturing kFS and program output into its own UI, and making mod-added services visible in the reference window — with the visibility of every member involved.
- [kFS build and load flow](/tooling/build-and-load-flow.md) — The exact call chain from a source file to a running program — project discovery, compile or assemble, image validation, loading onto the bench or a vehicle, start, stop and reset — with where diagnostics go and how layout and part mismatches are handled.
- [kFS command-line tools (kvm-cli, kc-cli)](/tooling/cli.md) — The KSA.Kfs.Cli namespace — the kvm-cli and kc-cli entry points compiled into the game assembly, their arguments, files, exit codes and JSON outputs, and the finding that nothing in the shipped game calls them.
- [kFS console commands](/tooling/console-commands.md) — Every verb of the in-game `kfs` console command — exact syntax, arguments, effect and output text — plus how the game console parses it, where the output goes, and how a mod can call, capture or extend it.
- [kFS debug window and dispatch profiler](/tooling/debug-window.md) — KfsDebugWindow (View > Debug > Show kFS Debug) and KfsDispatchProfiler — the on/off switch for the prototype, the per-tick timing tables, the dispatch flame graph, and where every number comes from.
- [kFS editor window](/tooling/editor-window.md) — KfsEditorWindow and its EditorModel — how the in-game kFS editor is opened, its menus, toolbar, hotkeys and eleven tabs, what each user action invokes, and what a mod can and cannot hook.
- [kFS embedded example programs](/tooling/examples.md) — Every kC and kASM example program embedded in the game (Examples.cs), reproduced verbatim with its name, menu placement and purpose.
- [kFS player quick start](/tooling/user-quick-start.md) — Step-by-step for a player — enable the kFS prototype, open the editor, create and build a program, load it onto the controlled vehicle, start it, watch its logs and traces, debug and stop it — with every step traced to the code that implements it.
- [kFS reference window](/tooling/reference-window.md) — KfsReferenceWindow, the editor's built-in Opcodes and Services reference — its two pages, the exact text shown, how each column is derived from the game tables, and when a mod-added table row appears in it.
- [kFS workspace and files](/tooling/workspace-and-files.md) — The on-disk kFS workspace under Documents/My Games/Kitten Space Agency/kfs — its folder structure, file extensions, generated headers, templates, naming rules, and exactly which files a build reads.
- [Workbench](/tooling/workbench.md) — The Workbench class is the facade behind most kfs console verbs (30 of 36) and the editor window, owning the workspace, the bench machine, the editor model and the choice of target (bench or vehicle).

## Game systems the parts extension drives

Lights, part animations and the part/module model, with mod precedents. Directory index: [/game-targets/](/game-targets/index.md).

- [Control recipes for lights and part animations](/game-targets/control-recipes.md) *(draft)* — Concrete C# for enumerating a vehicle's lights and animation modules, reading their state, switching lights, changing colour, cone angles and brightness, toggling deployment and setting an actuation value, each labelled with its provenance, required thread and stock side effects.
- [gatOS and other unscience submods — part, module, light and animation precedents](/game-targets/gatos-and-other-mod-precedents.md) — How gatOS and the non-zippo unscience submods enumerate vehicle parts and modules, address them over time, schedule reads and writes relative to the physics workers, and control lights and keyframe animations, with the pitfalls their own documents record.
- [KeyframeAnimationModule — deploy and actuation animations](/game-targets/keyframe-animation-module.md) — Complete reference for KSA part animations - one module class whose TimeGoal field is slewed toward by the physics step, how the ShowDeployRetract flag separates deploy/retract parts from 0-to-1 actuated parts, the animation data, the solar-tracking extension, side effects on colliders, solar power and on-rails state, the XML schema with real examples, stock parts, and persistence.
- [LightModule — part lights, their switch, and how they reach the renderer](/game-targets/light-module.md) — Complete reference for KSA part lights - LightModule and its shared TemplateData, the PowerConsumer light switch that provides on/off, colour, intensity, range and cone angles with units and limits, the per-frame render path, the XML schema with real examples, the stock parts that carry lights, and persistence.
- [Stock control paths for lights and part animations](/game-targets/stock-control-paths.md) — Every way the unmodded game changes light and animation state, traced end to end with file and line and the thread each step runs on, ending with which path a mod should reuse for each operation and whether it may run inside the physics step.
- [Vehicle, part tree, parts, sub-parts and modules](/game-targets/part-and-module-model.md) — How a KSA vehicle is composed of a PartTree of Part objects carrying typed modules, how to enumerate and identify them, how ModuleStateful state buffers work, and what staging, docking, save/load and scene changes do to those objects.
- [zippo light control — how an existing mod enumerates and mutates KSA lights](/game-targets/zippo-light-control-reference.md) — A code-level account of unscience's zippo light mod - its two mutation paths (shared-template reflection writes and per-instance template replacement), the exact game members it touches, where it runs, how it identifies parts, how it persists and restores changes, and every pitfall its sources and documents record.

## Modding

Extension points, the tested service recipe, the offline harness, risks. Directory index: [/modding/](/modding/index.md).

- [Adding a service to kFS from a mod](/modding/adding-a-service.md) — The complete, tested recipe for a mod-defined kFS service — the table row and its validation rules, the kC header and builtin registration, the two host hooks for requests and results, the threading rules for acting on the game, and the constraints that will otherwise bite.
- [kFS extension-point map](/modding/extension-points.md) — Every way a mod can extend or hook kFS, ranked by cost and risk — services, kC headers, kASM symbols, messages, I/O layout, console commands, examples, output capture and UI — with the mechanism, the evidence level and the document that has the detail.
- [Risks and compatibility rules for kFS mods](/modding/risks-and-compatibility.md) — The invariants a kFS-extending mod must keep, the ways it can break the player's game or programs, how each failure shows up, and what to re-check when the game updates.
- [Running kFS offline — the test harness and what it proved](/modding/offline-harness.md) — How to load KSA.dll outside the game and drive the real kFS tables, compiler, VM, host and Workbench from a console or test project, with the complete spike program and the results of each experiment run against build 5949.
- [What kFS exposes for a language server](/modding/language-tooling-surface.md) — The compiler, workbench, table and host APIs an LSP server for kC and kASM can be built on, feature by feature, with the position conventions, the project and overlay rules, and the threading and caching constraints.

