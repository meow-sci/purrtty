---
type: Concept
title: kFS source map
description: Where every piece of kFS lives in the decompiled game — each namespace with its size, role and key files, the embedded resources, the non-kFS game files that call into kFS, and the bundle document that covers each.
resource: ksa-decomp:KSA.Kfs
tags: [kfs, overview, source-map, navigation]
sources:
  - id: decomp-tree
    resource: ksa-decomp:KSA.Kfs
    title: Decompiled KSA.Kfs* namespace folders and embedded resources
  - id: program
    resource: ksa-decomp:KSA/Program.cs
    title: Program (decompiled)
  - id: physics-bubble
    resource: ksa-decomp:KSA/PhysicsBubble.cs
    title: PhysicsBubble (decompiled)
  - id: vehicle
    resource: ksa-decomp:KSA/Vehicle.cs
    title: Vehicle (decompiled)
generated: { by: claude-code/claude-fable-5-2, at: 2026-11-25T22:02:02Z }
status: stable
ksa_build: "2026.11.25.5949"
---

# How the decompiled tree is laid out

`ksa-decomp:` is the ILSpy output of the game DLLs: one folder per namespace, one file per type.
Everything kFS is in `KSA.dll`. Embedded resources decompile to flat files beside the folders. File
and line counts below were measured on the snapshot for `ksa_build`.[^decomp-tree]

# Namespaces

| Namespace (folder) | Files | Lines | Role | Largest files | Bundle |
| --- | ---: | ---: | --- | --- | --- |
| `KSA.Kfs` | 19 | 7,784 | Game glue and all ImGui UI | `KfsEditorWindow.cs` (3,099), `KfsHostSampler.cs` (711), `KfsServices.cs` (682), `KfsVehicleHost.cs` (644), `KfsReferenceWindow.cs` (617) | [/integration/](/integration/index.md), [/tooling/](/tooling/index.md) |
| `KSA.Kfs.KC` | 24 | 12,723 | The kC front end: lexer, preprocessor, parser, semantic analysis, lowering, headers | `Parser.cs` (3,739), `Lowering.cs` (1,809), `Sema.cs` (1,616) | [/language/](/language/index.md) |
| `KSA.Kfs.Compiler` | 43 | 7,100 | Compile driver, analyses (WCET, stack, bus), report types, diagnostics | `ReportJsonContext.cs` (4,520, generated JSON glue), `ProgramAnalysis.cs` (1,006), `BusAnalysis.cs` (540) | [/compiler/](/compiler/index.md) |
| `KSA.Kfs.Compiler.Ir` | 18 | 857 | IR model, validator, printer | `IrPrinter.cs` (477), `IrValidator.cs` (155) | [IR reference](/compiler/ir-reference.md) |
| `KSA.Kfs.Compiler.Builder` | 2 | 457 | `IrBuilder`, the API front ends emit through | `IrBuilder.cs` (438) | [IR reference](/compiler/ir-reference.md) |
| `KSA.Kfs.Compiler.Backend` | 2 | 879 | `Emitter`: IR → kVM code and image sections | `Emitter.cs` (874) | [backend emitter](/compiler/backend-emitter.md) |
| `KSA.Kfs.KAsm` | 9 | 2,567 | Assembler, expression evaluator, computed includes, disassembler | `Assembler.cs` (1,718), `Expr.cs` (409), `Disassembler.cs` (242) | [kASM](/compiler/kasm-assembly-language.md) |
| `KSA.Kfs.Vm.Image` | 18 | 1,776 | Image format, builder, reader/validator, log-format parsing, cost model | `ImageReader.cs` (890), `LogFormat.cs` (297), `ImageBuilder.cs` (253) | [image format](/compiler/kvm-image-format.md) |
| `KSA.Kfs.Vm.Debug` | 8 | 627 | Debug-section model | `DebugInfo.cs` (606) | [debug info](/compiler/debug-info.md) |
| `KSA.Kfs.Vm` | 42 | 3,971 | The interpreter, syscall interfaces, argument domains, profiler, enums | `KvmMachine.cs` (2,579), `ArgDomains.cs` (249), `Profiler.cs` (200) | [/vm/](/vm/index.md) |
| `KSA.Kfs.Vm.Tables` | 53 | 12,138 | Table row types, loader and validation, layouts, message layout | `DataJsonContext.cs` (10,384, generated JSON glue), `GameTables.cs` (875), `MessageLayout.cs` (350) | [/tables/](/tables/index.md) |
| `KSA.Kfs.Host` | 51 | 4,403 | Host and vehicle computers, tick grid, layout generator, host messages, bench, fake vehicle, snapshots | `HostComputer.cs` (947), `VehicleComputer.cs` (607), `HostMessages.cs` (552) | [/host/](/host/index.md) |
| `KSA.Kfs.Host.Services` | 10 | 906 | Burn handles, service override, console log, log formatter, trace store | `LogFormatter.cs` (252), `ConsoleLog.cs` (204), `TraceStore.cs` (134) | [service request handling](/host/service-request-handling.md) |
| `KSA.Kfs.Host.Views` | 34 | 2,726 | UI-independent view models (bus monitor, profiler, report, scope, disassembly…) | `BusMonitorView.cs` (363), `ProfilerView.cs` (278), `ReportView.cs` (265) | [views](/host/views.md) |
| `KSA.Kfs.Host.Workbench` | 12 | 3,622 | Workbench, workspace, build projects, editor model, embedded examples | `Workbench.cs` (2,295), `EditorModel.cs` (580), `Workspace.cs` (236) | [/tooling/](/tooling/index.md) |
| `KSA.Kfs.Cli` | 8 | 2,531 | Command-line front ends and their JSON output; unreachable in the shipped game | `CliJson.cs` (1,026, generated JSON glue), `Cli.cs` (583), `KcCliJson.cs` (424) | [CLI](/tooling/cli.md) |

Total: 353 files, about 65,000 lines, of which roughly 16,000 are System.Text.Json source-generated
glue (`DataJsonContext`, `ReportJsonContext`, `CliJson`, `KcCliJson`).

# The `KSA.Kfs` glue namespace, file by file

| File | Lines | What it is | Covered in |
| --- | ---: | --- | --- |
| `KfsIntegration.cs` | 215 | Static entry point: tables, workbench, output writer, registration, per-frame hook | [integration overview](/integration/game-integration-overview.md) |
| `KfsPrototype.cs` | 29 | The feature flag | [feature flag](/integration/feature-flag.md) |
| `KfsDispatch.cs` | 29 | The call the physics step makes; the dispatch try/catch | [dispatch and threading](/integration/dispatch-and-threading.md) |
| `KfsVehicleHost.cs` | 644 | Per-vehicle host: dispatch, sampling, write-back, faults | [KfsVehicleHost](/integration/kfs-vehicle-host.md) |
| `KfsControllerSet.cs` | 156 | Hardware lists and the generated layout | [controller set](/integration/controller-set-and-layout-changes.md) |
| `KfsHostSampler.cs` | 711 | Fills message leaves from game state | [host sampler](/integration/host-sampler.md) |
| `EngineModel.cs` | 239 | Engine performance model used by the sampler | [host sampler](/integration/host-sampler.md) |
| `KfsServices.cs` | 682 | Game effect of each service; main-thread work queue | [KfsServices](/integration/kfs-services-game-side.md) |
| `KfsWriteBack.cs` | 74 | Pilot defaults, flight-computer reset, engine performance | [write-back](/integration/write-back.md) |
| `KfsDispatchProfiler.cs` | 100 | Per-dispatch timing zones | [debug window](/tooling/debug-window.md) |
| `KfsConsoleCommands.cs` | 328 | The `kfs` console command and its verbs | [console commands](/tooling/console-commands.md) |
| `KfsEditorWindow.cs` | 3,099 | The editor window | [editor window](/tooling/editor-window.md) |
| `KfsReferenceWindow.cs`, `KfsReferencePage.cs` | 624 | Opcodes and Services reference pages | [reference window](/tooling/reference-window.md) |
| `KfsDebugWindow.cs` | 498 | "Show kFS Debug" window | [debug window](/tooling/debug-window.md) |
| `KfsNewPopup.cs`, `KfsDeletePopup.cs`, `KfsTextBoxWidget.cs`, `IKfsTextWidget.cs` | 356 | Editor popups and the text widget seam | [editor window](/tooling/editor-window.md) |

# Embedded resources

| Resource (flat file under `ksa-decomp:`) | Bytes | Content | Covered in |
| --- | ---: | --- | --- |
| `KSA.Kfs.Vm.Tables.Data.opcodes.json` | 20,984 | Opcode table | [opcodes](/vm/opcodes.md) |
| `KSA.Kfs.Vm.Tables.Data.costs.json` | 10,667 | Per-core cycle costs | [cost model](/vm/cost-model-and-budgets.md) |
| `KSA.Kfs.Vm.Tables.Data.traps.json` | 2,623 | Trap codes | [traps and faults](/vm/traps-and-faults.md) |
| `KSA.Kfs.Vm.Tables.Data.sysblock.json` | 2,335 | System block fields | [memory map](/vm/memory-map-and-sysblock.md) |
| `KSA.Kfs.Vm.Tables.Data.routines.json` | 2,874 | Soft-arithmetic routine families | [soft routines](/vm/soft-routines-and-fp.md) |
| `KSA.Kfs.Vm.Tables.Data.fpcheck.json` | 6,872 | Floating-point self-test vectors | [soft routines](/vm/soft-routines-and-fp.md) |
| `KSA.Kfs.Vm.Tables.Data.edgecases.json` | 9,357 | Edge-case vectors (loaded, unused) | [soft routines](/vm/soft-routines-and-fp.md) |
| `KSA.Kfs.Vm.Tables.Data.services.json` | 11,323 | Services | [services table](/tables/services-table.md) |
| `KSA.Kfs.Vm.Tables.Data.messages.json` | 24,379 | Message catalogue | [messages table](/tables/messages-table.md) |
| `KSA.Kfs.Vm.Tables.Data.events.json` | 1,242 | Events (inert in this build) | [events table](/tables/events-table.md) |
| `KSA.Kfs.Vm.Tables.Data.enums.json` | 1,899 | Enumerations | [enums table](/tables/enums-table.md) |
| `KSA.Kfs.Vm.Tables.Data.catalogue.json` | 1,883 | Processor catalogue | [processor catalogue](/tables/catalogue-processors.md) |
| `KSA.Kfs.Vm.Tables.Data.bench_layout.json` | 2,910 | The bench I/O layout | [I/O layout tables](/tables/io-layout-tables.md) |
| `KSA.Kfs.Vm.Tables.Data.diagnostics.json` | 19,890 | Diagnostic manual entries | [diagnostics table](/tables/diagnostics-table.md) |
| `KSA.Kfs.KC.Library.math.c` | 46,065 | kC maths library source | [standard library](/language/kc-standard-library.md) |
| `KSA.Kfs.KC.Library.vecmat.c` | 14,122 | kC vector/matrix/quaternion library source | [standard library](/language/kc-standard-library.md) |
| `KSA.Kfs.KC.Library.string.kasm` | 2,580 | `memcpy` / `memmove` / `memset` / `memcmp` in kASM | [standard library](/language/kc-standard-library.md) |

The resource names all start with `KSA.Kfs.Vm.Tables.Data.` (`GameTables.RESOURCE_PREFIX`) or
`KSA.Kfs.KC.Library.`.

# Where the rest of the game touches kFS

These are the only references to kFS from outside the `KSA.Kfs*` namespaces:

| File:line | What |
| --- | --- |
| `KSA/Program.cs:978` | `KfsIntegration.RegisterCommands(this)` at start-up[^program] |
| `KSA/Program.cs:2551` | `KfsIntegration.BeforeInputEvents()` once per frame on the main thread[^program] |
| `KSA/Program.cs:4423` | View › Debug › "Show kFS Debug" menu item[^program] |
| `KSA/PhysicsBubble.cs:2081` | `KfsDispatch.TryComputeControl(…)` in the flight-computer pass[^physics-bubble] |
| `KSA/PhysicsBubble.cs:80–120` | `KfsDeltaTime` / `KfsVehicleIdx` wake-up bookkeeping[^physics-bubble] |
| `KSA/Vehicle.cs:567` | `public KfsVehicleHost? KfsHost { get; set; }`[^vehicle] |
| `KSA/PhysicsStepLimit.cs:17`, `KSA/PhysicsStepLimitEx.cs:24` | The `KfsTick` step-limit value and its label "kFS Tick" |

# Game files the parts extension depends on

| File | Why it matters | Covered in |
| --- | --- | --- |
| `KSA/LightModule.cs` | Light module and its shared `TemplateData` | [light module](/game-targets/light-module.md) |
| `KSA/PowerConsumer.cs`, `KSA/Part.cs` | The light switch (`LightIsActive`, `Part.LightSwitch`) | [light module](/game-targets/light-module.md) |
| `KSA/ColorRgbReference.cs`, `KSA/FloatReference.cs` | Colour and scalar holders inside the light template | [light module](/game-targets/light-module.md) |
| `KSA.Rendering.Lighting/Light.cs` | Spot-cone clamping at render time | [light module](/game-targets/light-module.md) |
| `KSA/KeyframeAnimationModule.cs`, `KSA/KeyframeAnimationData.cs` | Deploy/actuator animations (`TimeGoal`, `State`, `Shared.Duration`) | [keyframe animation module](/game-targets/keyframe-animation-module.md) |
| `KSA/ModuleBase.cs`, `KSA/ModuleList.cs`, `KSA/ModuleStateful.cs`, `KSA/PartTree.cs` | Module enumeration, identity and state buffers | [part and module model](/game-targets/part-and-module-model.md) |

# Sources outside the game

| Scheme | Used for |
| --- | --- |
| `unscience:` | The zippo submod's light control — [zippo reference](/game-targets/zippo-light-control-reference.md) |
| `gatos:` | gatOS's light and animation actuators — [mod precedents](/game-targets/gatos-and-other-mod-precedents.md) |
| `starmap:` | The mod loader's lifecycle — referenced from [/modding/](/modding/index.md) |

# Modding notes

- A game update replaces the whole decompiled tree; line numbers in this bundle then drift. Search
  by member name, and re-check the eight touch points above first — they are the complete coupling
  between kFS and the rest of the game.
- The generated JSON glue files are large but carry no logic; skip them when reading.

[^decomp-tree]: Folder listing and `wc -l` over each `KSA.Kfs*` folder; `wc -c` over the flat resource files.
[^program]: Program.cs:978, :2551, :4423.
[^physics-bubble]: PhysicsBubble.cs:80–120, :2077–2086.
[^vehicle]: Vehicle.cs:567.
