# kfs-info change log

## 2026-11-26

Revision after purrTTY's kFS extension, command-line tool, language server and Neovim launcher were
implemented and reviewed (offline only; nothing has run in the game). Every game fact below was
re-read in the decompiled sources of build 2026.11.25.5949; no document was added, retitled or
re-described, so the indexes are unchanged.

- **Corrected** `/modding/adding-a-service.md`: the stock `BeforeInputEvents` prunes dead hosts only
  while the kFS switch is on (`KfsIntegration.cs:160-163`) — a main-thread hook must filter by
  `IsAlive` itself. The earlier text said the original always prunes first.
- **Corrected** `/game-targets/light-module.md` and `/game-targets/control-recipes.md`: a light's
  colour was called "linear RGB" without evidence. The C# passes the three floats to the renderer
  unconverted (`ColorRgbReference.cs:35-44`, `Color.cs:8-36`, `Light.cs:38-49`); the colour space is
  now labelled `Unverified:`.
- **Added** to `/integration/dispatch-and-threading.md`: "What a dispatch reads: live fields and
  committed state" — inside the solver job the flight-computer pass runs before the module update
  (`PhysicsBubble.cs:1505/1548/1569`, `:1639-1652/1881-1886`), and committed state is written once
  per frame (`PartTree.cs:1593-1600`), so a field written in the sync window is read at once and
  the state derived from it one frame later; a light switched off reads not lit at once, switched
  on one frame later (`Part.cs:1551-1567`, `PowerConsumer.cs:138-146`). And "Where a vehicle's
  structure changes" — contact docking, destruction and part failure in `ApplyVehicleSolvers`
  (`PhysicsBubble.cs:1113-1165`), requested dock, undock and decouple in `ApplyInputEvents`
  (`InputEvents.cs:1186-1215`). The same timing note in `/game-targets/light-module.md` and
  `/game-targets/keyframe-animation-module.md`.
- **Added** to `/vm/traps-and-faults.md`: the `kfs_logf` lines of the trapping tick never reach the
  log, because `log` is a queued request and a trapped tick's requests are discarded
  (`HostComputer.cs:439`, `:747-770`).
- **Added** to `/tooling/console-commands.md`: how the game console files commands per owner —
  same owner replaces, `UnregisterObject` removes an owner's commands, the same name under two
  owners makes `help` throw (`TerminalInterface.cs:97-124`, `:162-186`); the seven call sites of
  `KfsIntegration.Error`, all in `KfsConsoleCommands`; `build` takes the rest of the line while
  `load` splits at every space (`KfsConsoleCommands.cs:201-203`, `Workbench.cs:659-661`, `:739-762`).
- **Added** to `/tooling/workbench.md`: nothing in `KSA.Kfs.Host.Workbench` checks the prototype
  switch, so `TargetActiveVehicle()` creates a vehicle host and retargets `kfs` even while it is
  off (`Workbench.cs:944-971`, `:1066-1073`; the only check is `KfsConsoleCommands.cs:210`).
- **Added** to `/tooling/workspace-and-files.md`: `Workspace.List()` and `Write()` create the folder
  (`Workspace.cs:43-54`, `:105-111`); the quoted-include scan matches inside comments and misses an
  indented include directly after another include line (`Workspace.cs:169`, `:203-224`).
- **Added** to `/compiler/kasm-assembly-language.md`: kASM has no character literals — the comment
  stripper tracks only double quotes and the expression grammar has no such operand
  (`Assembler.cs:180-195`, `Expr.cs:220-287`); the directive names are string literals in the
  assembler and in no table.
- **Revised** `/modding/`: `extension-points.md` gained the evidence level "Implemented" and the
  table "What an implementation has exercised" (code path, offline coverage, not run in game), the
  kASM note that `SVC_*` symbols of added services are in the generated `<kfs.kinc>`
  (`BuiltinIncludes.cs:43-46`) while a mod's own `.kinc` holds only its constants, and the console
  owner rule; `adding-a-service.md` gained "Taking it out again" (never change `SystemHeaders`
  while a compile runs: `KcFrontEnd.cs:77`, `:143`, `EditorModel.cs:131-133`, `:412-418`), the read
  timing of effects, and the status of the `_tables` assignment (executed offline, unverified in
  game); `offline-harness.md` gained "After the spikes" (the frame order a simulator must follow,
  `Compiler.Compile` versus `CompileOnThisThread`, the test-assembly rules) outside its `verified`
  scope; `risks-and-compatibility.md` gained failure modes for small patch targets, header changes
  under a running compile, uncancellable compiles, a second command owner, a wrapped `Output`, the
  snapshot lease and a local socket, and an update item for tooling members;
  `language-tooling-surface.md` gained "What an implementation found", and its layout
  reconstruction from `vehicle.h` is no longer `Inferred:` (executed by purrTTY's tests).
- Evidence labels were not weakened: everything about the running game remains `Unverified:` or
  `Inferred:`. The four `status: draft` documents stay draft; this pass did not re-read them.

## 2026-11-25

- Bundle created for KSA build 2026.11.25.5949 from the decompiled game assemblies.
- Added `/overview/`: architecture, glossary, source map, bundle conventions.
- Added `/language/`: kC language reference, types and conversions, preprocessor, program
  structure, builtins and intrinsics, generated headers, standard library, front-end internals,
  diagnostics, examples, kScript status.
- Added `/compiler/`: compile pipeline, IR reference, backend emitter, program analysis and WCET,
  bus analysis, compile report, diagnostics infrastructure, kASM language, disassembler, image
  format, debug info, toolchain extension points.
- Added `/vm/`: kVM machine, opcodes, memory map and sysblock, tasks and scheduling, cost model,
  traps, services syscall ABI, soft routines and FP, profiler and exec trace, enums, bus slots,
  machine state format.
- Added `/tables/`: tables loader, services, messages, events, enums, processor catalogue, I/O
  layout tables, diagnostics table, opcode/cost/trap schemas, spec and decision references.
- Added `/host/`: host computer, vehicle computer, tick grid, I/O layout generation, host sampling
  and leaves, message bus, service request handling, ownership and control modes, snapshots and
  perf, bench machine and fake vehicle, views, host extension surface.
- Added `/integration/`: integration overview, dispatch and threading, vehicle host, services
  (game side), host sampler, controller set, write-back, flight-computer interplay, lifecycle and
  persistence, feature flag, patch targets and seams.
- Added `/tooling/`: workbench, workspace and files, build and load flow, editor window, reference
  window, debug window, console commands, CLI, examples, quick start, tooling extension points.
- Added `/game-targets/`: part and module model, light module, keyframe animation module, stock
  control paths, zippo light-control reference, gatOS and other mod precedents, control recipes.
- Added `/modding/`: extension-point map, adding a service, offline harness, risks and
  compatibility, language-tooling surface.
- Recorded offline experiments E1–E12 (real `KSA.dll`, outside the game) in
  `/modding/offline-harness.md`; spike sources are in `plans/investigations/kfs-spikes/`.
- Cross-document review pass against the executed experiments: corrected statements that repeated
  early assumptions (contiguous service ids, a vehicle sensor block, a generated `kfs.h`, a single
  service switch) and upgraded claims the experiments confirmed.
