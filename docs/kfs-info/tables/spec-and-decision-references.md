---
type: Concept
title: Index of internal spec, section and decision references in the kFS tables and code
description: Every RocketWerkz-internal spec document, section number, decision, proposal, open item and milestone cited in the embedded tables and in kFS string literals, with what each is cited for and where.
resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.services.json
tags: [kfs, tables, specs, decisions, provenance, index]
sources:
  - id: services-json
    resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.services.json
    title: services.json
  - id: messages-json
    resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.messages.json
    title: messages.json
  - id: events-json
    resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.events.json
    title: events.json
  - id: enums-json
    resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.enums.json
    title: enums.json
  - id: catalogue-json
    resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.catalogue.json
    title: catalogue.json
  - id: bench-layout-json
    resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.bench_layout.json
    title: bench_layout.json
  - id: diagnostics-json
    resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.diagnostics.json
    title: diagnostics.json
  - id: opcodes-json
    resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.opcodes.json
    title: opcodes.json
  - id: costs-json
    resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.costs.json
    title: costs.json
  - id: traps-json
    resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.traps.json
    title: traps.json
  - id: sysblock-json
    resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.sysblock.json
    title: sysblock.json
  - id: routines-json
    resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.routines.json
    title: routines.json
  - id: fpcheck-json
    resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.fpcheck.json
    title: fpcheck.json
  - id: edgecases-json
    resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.edgecases.json
    title: edgecases.json
  - id: tables-code
    resource: ksa-decomp:KSA.Kfs.Vm.Tables/GameTables.cs
    title: String literals in KSA.Kfs.Vm.Tables (GameTables, CatalogueEnums)
  - id: kfs-code
    resource: ksa-decomp:KSA.Kfs/KfsServices.cs
    title: String literals in the other KSA.Kfs* namespaces (decompiled)
  - id: kc-library
    resource: ksa-decomp:KSA.Kfs.KC.Library.math.c
    title: Embedded kC library sources (math.c, vecmat.c, string.kasm)
generated: { by: claude-code/claude-fable-5-2, at: 2026-11-25T21:52:36Z }
status: stable
ksa_build: "2026.11.25.5949"
---

# Purpose

The embedded tables and many kFS error strings cite design documents that are not shipped with the
game: specification files, numbered sections, and entries of a decision log (`D-…` decisions, `P-…`
proposals, `O-…` open items), plus milestone names. Those documents are not available to us. This
index collects **every such citation** found in the fourteen JSON tables, in the string literals of
all `KSA.Kfs*` namespaces, and in the three embedded kC library sources, so that the citations can be
used as a map of the unseen design: what each reference is *about*, as far as the citing text says.

Sources scanned: the fourteen tables — services,[^services-json] messages,[^messages-json]
events,[^events-json] enums,[^enums-json] catalogue,[^catalogue-json] bench layout,[^bench-layout-json]
diagnostics,[^diagnostics-json] opcodes,[^opcodes-json] costs,[^costs-json] traps,[^traps-json]
sysblock,[^sysblock-json] routines,[^routines-json] fpcheck,[^fpcheck-json] edgecases[^edgecases-json]
— the string literals of the tables namespace[^tables-code] and of the other kFS
namespaces,[^kfs-code] and the embedded kC library sources.[^kc-library] Each footnote lists the
lines that carry citations.

How it was built: a scan of all files under the `KSA.Kfs*` folders and resources for the patterns
`[DPO]-\d{3}`, `G-R\d-\d+`, `MS…` and `§`, then each hit read in context. "What it is cited for"
paraphrases the citing text only; nothing is inferred about the documents beyond that. File names in
the "where" columns are relative to the decompiled tree; `*.json` means the
`KSA.Kfs.Vm.Tables.Data.*.json` resource.

# Spec documents

| document (as cited) | short form used in citations | governs (from the citations) |
| --- | --- | --- |
| `kVM_spec.md` | `kVM §n` | the virtual machine: parts and core classes, system block, tasks and traps, the instruction set, service call format, arithmetic edge cases, the image format, host API, profiler |
| `kFS_flight_interface_spec.md` | `FI §n` | the program↔vehicle interface: units, I/O layout rules, the bus, ownership, the message catalogue, services, events, the log ABI |
| `kFS_ksa_integration_spec.md` | `integration §n` | how kFS binds to KSA: layout changes, reset causes, allocation, host messages, stock guidance, service handling, pilot override, the kill switch |
| `kFS_processors_spec.md` | `processors §n` | cost tables, soft routines, the processor catalogue, tick rates, the bench, per-part feature availability, calibration |
| `kFS_compiler_spec.md` | `compiler §n` | translation limits, constant expressions, the library, loop bounds, the diagnostics table |
| (kC language spec — file name never given) | `kC §n` | the kC language: vector types, task pragmas, bindings, the library, host builds |
| (kASM spec — file name never given) | `kASM §n` | the assembler: `.func`/`ENTER`, `.proto`, disassembly round trip |
| (tooling spec — file name never given) | `tooling §n` | workbench tools: plotting, "copy as kASM", generated headers, CLI dumps, Debug-section enums |
| "decision log" | `D-…`, `P-…`, `O-…` | named once: "Optional (decision log O-005)" (catalogue.json:18) |
| `reuse.md`; "flightvm's src/lib/math.c" | — | provenance of the kC math library (KSA.Kfs.KC.Library.math.c:1) |

Several `source` strings say "(moved 2026-11-02, D-052)": the tables' content was moved into these
documents on that date under decision D-052. Bare `§n` references inside a table refer to that
table's own source document.

# Section index

## `kVM_spec.md`

| section | cited for | where |
| --- | --- | --- |
| §2.2 | "enums numbered from 0 in table order"; the part's `part_index`; core-class numbering; the legal ranges of a part's RAM, code size and clock | enums.json:3; sysblock.json:15–16; GameTables.cs:443; KSA.Kfs.KC.Library.string.kasm:2 |
| §2.5 | the system block | sysblock.json:3 |
| §2.6, §3.8 | (with §7) the one-tick budget that an instruction or a task's start/completion step with its bus copies may not exceed (rule 718) | diagnostics.json:120 |
| §3.1 | "the implicit call that starts a task", priced as `call` | costs.json:75 |
| §3.3 | `init` and `init_budget_cycles` across boot ticks | traps.json:9 |
| §3.4 | the `program` task and `YIELD` | opcodes.json:51; traps.json:20 |
| §3.6 | the trap table | traps.json:3 |
| §3.7 | the floating-point self-test | fpcheck.json:3 |
| §4.2 | the cost category list | costs.json:3 |
| §4.3–§4.7 | the opcode tables (row `section` values `4.3` stack and constants, `4.4` memory, `4.5` control flow, `4.6` 32-bit arithmetic, `4.7` 64-bit and conversions) | opcodes.json:3 and every row |
| §4.4 | bounds checks being optional | traps.json:16 |
| §4.5 | call discipline — rule 4 (an `ENTER` must be reached by a call), rule 5 (`ENTER` frame and per-word cost), rules 7 and 9 (operand floor, `RET r`); `ENTER`'s u8 `ncells` | traps.json:6, 7, 18; costs.json:75; opcodes.json:48–49; diagnostics.json:121 |
| §4.7 | saturating 64-bit conversions | fpcheck.json:31 |
| §4.8 | the service call format and the **argument-domain vocabulary**; per-service cost classes; format-string arguments; counting rejected requests | services.json:3; costs.json:83; opcodes.json:52; sysblock.json:21; traps.json:19; GameTables.cs:687 |
| §4.9 | arithmetic edge cases (prose bullets; division and shift semantics) | edgecases.json:3; opcodes.json:56, 74 |
| §7 | the image: CRC-32, `Tasks[]`, task kind, the message catalogue version recorded in images, format strings | bench_layout.json:3; messages.json:3, 8; enums.json:3; sysblock.json:12; traps.json:19; diagnostics.json:120 |
| §8.2 | the host's `WriteMessage` / `ReadMessage` contract | KSA.Kfs.Vm/KvmMachine.cs:372, 377, 390, 395 |
| §8.4 | the profiler | KSA.Kfs.Host.Workbench/Workbench.cs:1376 |
| §9.2 | optional memory error correction (`MEMORY_ERROR`) | traps.json:21 |

## `kFS_flight_interface_spec.md` (FI)

| section | cited for | where |
| --- | --- | --- |
| §2.1 | the unit-suffix naming rule (and its one exception) | messages.json:105 |
| §5 | the layout generator sizing `per_vehicle` arrays | messages.json:3 |
| §5.1 | I/O layout rules — rule 4 (one-shot fields), rule 6 (64 KB command window; u16 `body_size`), natural layout of groups; element order matching `commands.thruster[i]` | bench_layout.json:3; messages.json:66; KSA.Kfs.Host/LayoutGenerator.cs:75, 80; KSA.Kfs.KC/VehicleHeader.cs:63, 235, 373 |
| §6.2 | the message header being set by the bus | KSA.Kfs.Host.Views/BusMonitorView.cs:310 |
| §6.5 | messages the host publishes | KSA.Kfs.Host/HostComputer.cs:311 |
| §7.2 | block ownership by publishing; only `ALLOCATION` is claimed explicitly | KSA.Kfs.KAsm/Assembler.cs:566; KSA.Kfs.KC/Parser.cs:546 |
| §8 (8.1–8.10) | the message catalogue; each message's `section` (`8.1` NAV_STATE … `8.10` BURN_PLAN); "a message is a catalogue `MSG_*` id"; enumerated fields | messages.json:3 and every row; KSA.Kfs.KAsm/Assembler.cs:905; KSA.Kfs.KC/KcFrontEnd.cs:266; CatalogueEnums.cs:43; BusMonitorView.cs:294 |
| §8.3 | `MSG_ATT_TARGET`: `roll_mode`, `q_target_helm_to_cci` | services.json:12, 19, 20 |
| §8.6 | `MSG_BURN_STATUS`: current burn, `end_cause` | messages.json:71, 76; enums.json:3 |
| §9.1 | the services table; burn handles; `set_throttle` | services.json:3, 10; messages.json:71, 110 |
| §9.2 | the service argument enumerations (directions) | enums.json:3; services.json:12; messages.json:104; CatalogueEnums.cs:55 |
| §9.3 | host checks "after the latch" | services.json:3 |
| §10 | events and `wake_reason` | events.json:3; services.json:10; sysblock.json:14 |
| §11, §11.1 | the `log` buffer ABI and format rules | services.json:5, 8; KSA.Kfs.KC/Parser.cs:3340; KSA.Kfs.Vm.Image/LogFormat.cs:232 |

## `kFS_ksa_integration_spec.md` (integration)

| section | cited for | where |
| --- | --- | --- |
| §2.4 | layout change: elements of a program's frozen layout becoming inert | KSA.Kfs/KfsVehicleHost.cs:438 |
| §2.6 | reset causes (0 power-on, 1 player reset, 2 watchdog, 3 load) | enums.json:3; sysblock.json:20 |
| §7.1 | allocation's averaging formulas; the pilot's Pulse and Direct paths | messages.json:51, 56 |
| §7.4 | host messages | KSA.Kfs.Host/HostComputer.cs:311; KSA.Kfs.Host/HostMessages.cs:461 |
| §7.5 | the parent-body id (CRC-32 of the KSA body id); digital pilot keys | messages.json:8, 79 |
| §7.6 | stock guidance: what it publishes and when, the `TNM` target conversion, engine evaluation ("row 1") | messages.json:36, 37, 67 |
| §7.8 | writing the control frame back to the vehicle | messages.json:43 |
| §8.1 | thruster controller minimum pulse time | messages.json:66 |
| §8.4 | applying services: `translate` rejection, `burn_start` checks, the service throttle/engine overrides and when they end | services.json:18, 24; messages.json:110, 111, 113; KSA.Kfs.Host/VehicleComputer.cs:190 |
| §9.2 | the pilot-override table (which publisher's command wins) | messages.json:51, 110 |
| §9.3 | the kill switch | messages.json:85; KSA.Kfs.Host/VehicleComputer.cs:308 |

## `kFS_processors_spec.md` (processors)

| section | cited for | where |
| --- | --- | --- |
| §3.1–§3.3 | the per-core cost tables | costs.json:3 |
| §4.1, §4.2 | soft routines and families | routines.json:3 |
| §5.1 | the catalogue; tick rates dividing 100 Hz | catalogue.json:3; GameTables.cs:461 |
| §5.2 | (catalogue content) | catalogue.json:3 |
| §5.4 | the bench part and bench layout | catalogue.json:3; bench_layout.json:3 |
| §6 | which features need "the Mk1 or above" (claiming `ALLOCATION`, owning a GNC block, fixed-size matrix types) | KSA.Kfs.KAsm/Assembler.cs:570, 910; KSA.Kfs.KC/KcFrontEnd.cs:81, 280; KSA.Kfs.KC/Parser.cs:550 |
| §8 | calibration (cost values are "placeholder values until calibration") | costs.json:3 |

## `kFS_compiler_spec.md` (compiler)

| section | cited for | where |
| --- | --- | --- |
| §2 | task ordering between files; translation limits (nesting depth) | diagnostics.json:65, 122 |
| §4 | constant expressions (a `const` object is not one; division by zero) | diagnostics.json:22; KSA.Kfs.KC/Parser.cs:615 |
| §4.1 ("G") | `memcpy` compiling to `COPY 4` / `COPY 8`; the memory routines of `<string.h>` | KSA.Kfs.KC.Library.string.kasm:1; KSA.Kfs.KC.Library.math.c:9 |
| §7 | every loop having a constant bound | KSA.Kfs.KC.Library.math.c:10, 584 |
| §11.1 | the diagnostics table | diagnostics.json:3 |

## kC, kASM and tooling specs

| reference | cited for | where |
| --- | --- | --- |
| kC §5.4 | `<vecmat.h>` types | KSA.Kfs.KC.Library.vecmat.c:1 |
| kC §9.1 | `#pragma kfs task name rate_hz=N` | KSA.Kfs.KC/KcFrontEnd.cs:633 |
| kC §9.5, §10.2, §10.3 | "the service bindings" | KSA.Kfs.KC/Parser.cs:3421 |
| kC §10, §10.1 | the standard library; `<math.h>` | KSA.Kfs.KC.Library.vecmat.c:7; KSA.Kfs.KC.Library.math.c:1, 6, 222, 695 |
| kC §10.2, §10.2a | the generated vehicle header and binding cell counts; `<vecmat.h>` | KSA.Kfs.KC/VehicleHeader.cs:63; KSA.Kfs.KC/Lowering.cs:1406; KSA.Kfs.KC.Library.vecmat.c:1, 4 |
| kC §14 | host builds (`__KFS_HOST__`) computing the same results | KSA.Kfs.KC/GeneratedHeaders.cs:42; KSA.Kfs.KC/VehicleHeader.cs:63; KSA.Kfs.KC.Library.math.c:10 |
| kASM §5.2 | `ENTER` is written by `.func` | KSA.Kfs.KAsm/Assembler.cs:1405 |
| kASM §5.3 | `.proto` before `.func` for kC callers | KSA.Kfs.Host.Views/DisassemblyView.cs:139 |
| kASM §6.3 | disassembly that reassembles byte-identically | KSA.Kfs.KAsm/Disassembler.cs:30 |
| kASM §8.1 | headers generated from `.proto` lines | KSA.Kfs.KC/KcFrontEnd.cs:381 |
| tooling §5.1 | at most 16 plotted message fields per computer | KSA.Kfs.Host/BusTracker.cs:243 |
| tooling §6 | "copy as kASM" | KSA.Kfs.Host.Views/DisassemblyView.cs:137 |
| tooling §7.3, §7.4 | the built-in `<vehicle.h>` and `<vehicle.kinc>` | KSA.Kfs.KC/GeneratedHeaders.cs:42 |
| tooling §8, §8.1 | `--dump-globals` needing a Debug section; Debug-section enums | KSA.Kfs.Cli/Cli.cs:448, 452; enums.json:3 |

# Decisions (`D-…`)

| id | cited for | where |
| --- | --- | --- |
| D-040 | the processor catalogue (with D-052) | catalogue.json:3 |
| D-047 | the floating-point self-test; the kC `<math.h>` library | fpcheck.json:3; KSA.Kfs.KC.Library.math.c:1 |
| D-052 | "moved 2026-11-02": the relocation of table content into its current spec documents | catalogue.json:3; costs.json:3; diagnostics.json:3; events.json:3; fpcheck.json:3; messages.json:3; opcodes.json:3; routines.json:3; services.json:3; sysblock.json:3; traps.json:3 |
| D-060 | "one number is one rule": 405 names one rule and kC's `main` is 409 | diagnostics.json:3; KSA.Kfs.Compiler/DiagnosticRegistry.cs:21 |
| D-076 | messages with `double` fields need 64-bit doubles (`double_bits = 32` cannot use them) | KSA.Kfs.KC/KcFrontEnd.cs:302 |
| D-078 | confirms P-146 (`translate_helm_mps`: velocity change to deliver over the next second) | messages.json:42 |
| D-080 | the compiler's built-in `<vehicle.h>` | KSA.Kfs.KC/GeneratedHeaders.cs:42 |
| D-090, D-091 | (always cited together) the large extension of the catalogue to carry what KSA's own flight computer uses: parent-frame and dock-frame fields of `MSG_FLIGHT_DATA`; the control frame and `engine_burn_s` of `MSG_ATT_TARGET`; the detail fields of `MSG_WRENCH_CMD`; `max_g_load`, angular acceleration, RCS authority and the per-thruster/engine/gimbal element fields of `MSG_VEHICLE_PROPS`; `MSG_PILOT_INPUT.engine_on`; and all of `MSG_GUIDANCE_STATUS`, `MSG_FC_SETTINGS` (except the service overrides) and `MSG_BURN_PLAN`. "D-090 (3a)" is the deadband floor from `1/MaxFlightComputerRate` | messages.json:27, 28, 30, 31, 43, 44, 51–57, 63–68, 83, 88–99, 102–109, 116–126 |
| D-092 | the quaternion error convention `q_current⁻¹ ⊗ q_target` (body-frame rotation current → target) | KSA.Kfs.KC.Library.vecmat.c:74, 205 |
| D-096 | stock allocation choosing its merge by the publisher, not by `detail_valid` | messages.json:51 |
| D-097 (1) | `tvc_cmd_ctrl` semantics in Manual | messages.json:55 |
| D-102 | "catalogue v3": `target_dock_valid`, the dock-frame fields, `parent_mass_kg`, `q_parent_cce_to_cci`, and the engine-core performance-model parameters; P-176 withdrawn | messages.json:29–33, 67 |
| D-109 | a "kernel service" whose bad gimbal index or count above 1,024 traps `BAD_SYSCALL` (`Not active in this build:` no such service row exists) | traps.json:19 |
| D-111 (1) | an all-zero `q_ctrl_to_body` meaning guidance's control frame | messages.json:52 |
| D-116 | services rework: the `cell` argument type; (1) the service throttle/engine overrides in `MSG_FC_SETTINGS`; (3) burn handles issued by the host; (4) the service argument enumerations; (5), (11) argument domains checked by the VM at issue and counted in `rejected_requests`; `stage_presses` (with P-254) | services.json:3; enums.json:3; messages.json:71, 84, 110–113; sysblock.json:21 |
| D-117 | (1) `MSG_BURN_STATUS` state `PENDING` and `end_cause` ("catalogue v4"); (2) `translate` rejected until stock allocation has automatic translation | messages.json:72, 76; enums.json:3; services.json:18; KSA.Kfs.Host/VehicleComputer.cs:190 |
| D-118 | (2) how `service_throttle` is applied; (4) when the service overrides stop being valid — and, in the loader, "(D-118 (4))" on the error about the kC column's arguments not grouping `arg_types` | messages.json:110, 111, 113; GameTables.cs:636 |
| D-124 | service overrides (`set_throttle`, `engines_on`, `engines_off`) not applying while the vehicle isn't controllable | KSA.Kfs/KfsServices.cs:365, 377, 384 |
| D-135 | "there is no stock computer in this release": owning a GNC block needs `ALLOCATION`; services steer the C# flight computer, which flies only in pilot mode | KSA.Kfs.Host/Ownership.cs:53, 62; KSA.Kfs.Host/VehicleComputer.cs:231; KSA.Kfs.KC/KcFrontEnd.cs:224 |

"catalogue v3" and "catalogue v4" in `messages.json` are earlier values of the message catalogue
`version` (now 4): v3 arrived with D-102, v4 with D-116/D-117/D-118 (messages.json:29, 32, 33, 67 and
72, 76, 110–113).

# Proposals (`P-…`)

| id | cited for | where |
| --- | --- | --- |
| P-018 | event ids doubling as `wake_reason` values | events.json:3; enums.json:3; sysblock.json:14 |
| P-019 | `cost_table_hash` | costs.json:3 |
| P-020 | the cost-table schema; sizes in bytes and clocks in Hz | costs.json:3; catalogue.json:3 |
| P-021 | the category-to-family map, units and K32D routine names | routines.json:3; catalogue.json:3 |
| P-023 | numeric trap codes 1–17 in table order, 0 none | traps.json:3; GameTables.cs:859 |
| P-024 | the §4.9 edge-case vectors | edgecases.json:3 |
| P-026 | the bench layout being laid out by the FI §5.1 rules | bench_layout.json:3 |
| P-032 | kASM having no type grammar (hence `; .type` / `; .field` comments) | KSA.Kfs.KC/GeneratedHeaders.cs:42 |
| P-040 | a function ending in `RET` or `JMP`; no jump to its own `ENTER` | KSA.Kfs.Vm.Image/ImageReader.cs:390, 445 |
| P-041 | byte-identical reassembly of disassembled code | KSA.Kfs.KAsm/Disassembler.cs:30 |
| P-048 | the layout fingerprint over the canonical text | GameTables.cs:784 |
| P-082, P-083, P-132, P-133 | rules for generating the vehicle header from the layout (natural layout of groups) | KSA.Kfs.KC/VehicleHeader.cs:63, 235, 373 |
| P-084 | library functions compiled with the program, only reachable ones emitted | KSA.Kfs.KC.Library.math.c:3 |
| P-088 | `<vecmat.h>` | KSA.Kfs.KC.Library.vecmat.c:1 |
| P-090 | host builds computing the same thing bit for bit | KSA.Kfs.KC.Library.math.c:10 |
| P-091 | `<string.kasm>` memory routines | KSA.Kfs.KC.Library.string.kasm:1 |
| P-101 | the sampled accuracy tool (`MathAccuracy`) | KSA.Kfs.KC.Library.math.c:7 |
| P-104 | headers generated from a `.kasm` file's `.proto` lines | KSA.Kfs.KC/KcFrontEnd.cs:381 |
| P-130 | `MSG_VEHICLE_PROPS` element counts having to match the layout | KSA.Kfs.Host/HostMessages.cs:549 |
| P-136 | `parent_id` as CRC-32 of the body id; `+∞` for an unbound orbit or no next passage | messages.json:8, 18, 19 |
| P-146 | `translate_helm_mps` semantics (confirmed by D-078) | messages.json:42 |
| P-160 | one `<vehicle.h>` text for kC and host builds | KSA.Kfs.KC/GeneratedHeaders.cs:42; KSA.Kfs.KC/VehicleHeader.cs:63 |
| P-161 | `<vehicle.kinc>` | KSA.Kfs.KC/GeneratedHeaders.cs:42 |
| P-176 | a withdrawn proposal: since its withdrawal the `engine[i]` fields `mass_flow_kgps`, `min_thrust_N` and `min_mass_flow_kgps` are "informational" | messages.json:67 |
| P-186 | stock guidance publishing mode and target before the same tick's alignment retarget | messages.json:36 |
| P-254 | `stage_presses` counting only pilot key presses | messages.json:84 |

# Open items and other ids

| id | cited for | where |
| --- | --- | --- |
| O-002 | the reserved service 4, a "cFS-style `event(id, severity, fmt, …)`" | services.json:8 |
| O-005 | `Flight Computer Mk1` being optional | catalogue.json:18 |
| G-R1-224 | `engine_burn_s`: `+∞` for no planned cutoff | messages.json:44 |
| G-R1-087 | "a program with no entry point never runs" | KSA.Kfs.KC/KcFrontEnd.cs:633 |
| MS4-21 ("gap MS4-21, R1 MS4 2026-11-04") | the 64-bit conversion vectors guarding against double rounding | fpcheck.json:3, 23, 27 |
| B17 | "recorded streams arrive with B17, MS7" | KSA.Kfs.Cli/Cli.cs:439 |

`Inferred:` `G-R1-…` and "R1 MS4" look like findings of a numbered review round ("R1"); the sources
do not explain the prefix.

# Milestones

Milestone names show what the authors consider unfinished in this build:

| milestone | what the text says arrives then | where |
| --- | --- | --- |
| MS4 | a reachability check that removes `BAD_YIELD` from hand-written kASM | traps.json:20 |
| MS6 | "the service bindings" of kC; kC `log`/`trace` bindings (template text still says so, although the bindings exist in this build) | KSA.Kfs.KC/Parser.cs:3421; KSA.Kfs.Host.Workbench/Workbench.cs:2168, 2290; KSA.Kfs.Host.Workbench/Workspace.cs:23 |
| MS7 | sleeping and waking; recorded sensor streams | KSA.Kfs.Host/HostComputer.cs:793, 805; KSA.Kfs.Vm/KvmMachine.cs:2434; KSA.Kfs.Cli/Cli.cs:439 |
| MS9 | kScript files beside kC | KSA.Kfs.KC/KcFrontEnd.cs:52 |
| MS13 | `--strict` / `#pragma kfs strict` (Power of Ten mode) | KSA.Kfs.Cli/KcCli.cs:108; KSA.Kfs.KC/Parser.cs:558 |
| MS-G2 | `<matn.h>` | KSA.Kfs.KC/KcFrontEnd.cs:81 |
| "Ms5", "MsG3" (identifiers) | `HostMessages.Ms5Order` (3 host messages) and `MsG3Order` / `IsMsG3` (7) name the milestones at which those host messages were added | KSA.Kfs.Host/HostMessages.cs:362, 364, 378 |

# KSA identifiers named in the tables

`messages.json` and `enums.json` tie fields to named members of the game's own code, which is useful
when tracing a field to its source in the decompiled `KSA` namespace:

| KSA identifier | field | where |
| --- | --- | --- |
| `Helm2Body` | `MSG_FLIGHT_DATA.q_helm_to_body` | messages.json:21 |
| `NavigationTarget.Dock2Cci` | `target_dock_valid`, `target_q_dock_to_cci` | messages.json:29, 30 |
| `NavigationTarget.DockFrameRatesCci` | `target_dock_rates_cci_rps` | messages.json:31 |
| `Cce2Cci` | `q_parent_cce_to_cci` | messages.json:33 |
| `FlightComputer.cs` 745–757, 767–770 | `tvc_cmd_ctrl` (line numbers of RocketWerkz's own source file) | messages.json:55 |
| `EffectiveMaxGLoad` | `max_g_load` | messages.json:63 |
| `ReadMeasurements` | `ang_accel_body_rps2` | messages.json:64 |
| "KSA's thruster cache" | `rcs_torque_ctrl_Nm` and its two siblings | messages.json:65 |
| `ThrusterMapFlags` | `thruster[i].control_map` (bit k = flag bit k + 1) | messages.json:66 |
| `RocketControllerData.ComputeFromCores` | the `engine[i]` core/nozzle model fields | messages.json:67 |
| `TvcAllocation` | the `gimbal[i]` fields | messages.json:68 |
| `EngineOn` | `MSG_PILOT_INPUT.engine_on` | messages.json:83 |
| `VehicleReferenceFrame` | `MSG_FC_SETTINGS.frame` numbering | messages.json:103 |
| `FlightComputerAttitudeTrackTarget` | `track_target` numbering; `direction` enum | messages.json:104; enums.json:3 |
| `FlightComputerAttitudeProfile` | `attitude_profile` enum | enums.json:3 |
| `MaxFlightComputerRate` | `min_fc_dt_s` | messages.json:109 |
| `Conv.cs` | the VM conversion code checked by the 64-bit `fpcheck` vectors (`KSA.Kfs.Vm.Conv`) | fpcheck.json:3 |

# Using this index

- The citations confirm the design intent behind extension-relevant behaviour: services are requests
  latched at tick end and checked by the host afterwards (FI §9.3); argument domains are a closed
  vocabulary defined in kVM §4.8; "there is no stock computer in this release" (D-135), so the
  steering services drive the game's C# flight computer in pilot mode only (`log`, `trace`, `halt`,
  `stage` and burn planning are accepted in both modes).
- Nothing cited mentions lights, animations, parts or modules: the specs as visible through these
  citations cover guidance, navigation and control only. `Inferred:` an extension in that direction
  does not collide with any planned-but-unshipped table content we can see, except the reserved
  service id 4 (O-002) and the unnamed "kernel service" of D-109.
- When a newer build changes a table, comparing its `source` strings and decision ids against this
  index is a quick way to see what changed and why.

[^services-json]: services.json:3, 5, 8, 10, 12, 18–20, 24.
[^messages-json]: messages.json:3, 8, 18–19, 21, 27–33, 36–37, 42–44, 51–57, 63–68, 71–72, 76, 79, 83–85, 88–99, 102–113, 116–126.
[^events-json]: events.json:3.
[^enums-json]: enums.json:3.
[^catalogue-json]: catalogue.json:3, 18.
[^bench-layout-json]: bench_layout.json:3.
[^diagnostics-json]: diagnostics.json:3, 22, 65, 120–122.
[^opcodes-json]: opcodes.json:3, 48–49, 51–52, 56, 74.
[^costs-json]: costs.json:3, 75, 83.
[^traps-json]: traps.json:3, 6–7, 9, 16, 18–21.
[^sysblock-json]: sysblock.json:3, 12, 14–16, 20–21.
[^routines-json]: routines.json:3.
[^fpcheck-json]: fpcheck.json:3, 23, 27, 31.
[^edgecases-json]: edgecases.json:3.
[^tables-code]: GameTables.cs:443, 461, 636, 687, 784, 859; CatalogueEnums.cs:43, 55.
[^kfs-code]: KSA.Kfs/KfsServices.cs:365, 377, 384; KSA.Kfs/KfsVehicleHost.cs:438; KSA.Kfs.Host/Ownership.cs:53, 62; VehicleComputer.cs:190, 231, 308; HostComputer.cs:311, 793, 805; HostMessages.cs:461, 549; LayoutGenerator.cs:75, 80; BusTracker.cs:243; KSA.Kfs.Host.Views/BusMonitorView.cs:294, 310; DisassemblyView.cs:137, 139; KSA.Kfs.Host.Workbench/Workbench.cs:1376, 2168, 2290; Workspace.cs:23; KSA.Kfs.KAsm/Assembler.cs:566, 570, 905, 910, 1405; Disassembler.cs:30; KSA.Kfs.KC/KcFrontEnd.cs:52, 81, 224, 266, 280, 302, 381, 633; Parser.cs:546, 550, 558, 615, 3340, 3421; Lowering.cs:1406; GeneratedHeaders.cs:42; VehicleHeader.cs:63, 235, 373; KSA.Kfs.Vm/KvmMachine.cs:372–395, 2434; KSA.Kfs.Vm.Image/ImageReader.cs:390, 445; LogFormat.cs:232; KSA.Kfs.Cli/Cli.cs:439, 448, 452; KcCli.cs:108; KSA.Kfs.Compiler/DiagnosticRegistry.cs:21.
[^kc-library]: KSA.Kfs.KC.Library.math.c:1, 3, 6–7, 9–10, 222, 584, 695; KSA.Kfs.KC.Library.vecmat.c:1, 4, 7, 74, 205; KSA.Kfs.KC.Library.string.kasm:1–2.
