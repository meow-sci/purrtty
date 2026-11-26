# kfs-info change log

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
