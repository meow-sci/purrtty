---
type: Concept
title: kScript status
description: Everything the decompiled build reveals about kScript, the planned third kFS language — table columns, enum values, diagnostics and back-end plumbing — and the finding that no kScript front end exists in this build.
resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.services.json
tags: [kfs, language, kscript, status, not-implemented]
sources:
  - id: services-json
    resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.services.json
    title: services.json (embedded table)
  - id: enums-json
    resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.enums.json
    title: enums.json (embedded table)
  - id: events-json
    resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.events.json
    title: events.json (embedded table)
  - id: diag-json
    resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.diagnostics.json
    title: diagnostics.json (embedded table)
  - id: frontend
    resource: ksa-decomp:KSA.Kfs.KC/KcFrontEnd.cs
    title: KcFrontEnd (decompiled)
  - id: compiler
    resource: ksa-decomp:KSA.Kfs.Compiler/Compiler.cs
    title: Compiler (decompiled)
  - id: debuglang
    resource: ksa-decomp:KSA.Kfs.Vm/DebugLang.cs
    title: DebugLang (decompiled)
  - id: servicerow
    resource: ksa-decomp:KSA.Kfs.Vm.Tables/ServiceRow.cs
    title: ServiceRow (decompiled)
  - id: emitter
    resource: ksa-decomp:KSA.Kfs.Compiler.Backend/Emitter.cs
    title: Emitter (decompiled)
  - id: report
    resource: ksa-decomp:KSA.Kfs.Compiler/CompileReport.cs
    title: CompileReport (decompiled)
  - id: lowering
    resource: ksa-decomp:KSA.Kfs.KC/Lowering.cs
    title: Lowering (decompiled)
  - id: irfunction
    resource: ksa-decomp:KSA.Kfs.Compiler.Ir/IrFunction.cs
    title: IrFunction (decompiled)
  - id: layoutgen
    resource: ksa-decomp:KSA.Kfs.Host/LayoutGenerator.cs
    title: LayoutGenerator (decompiled)
  - id: cli
    resource: ksa-decomp:KSA.Kfs.Cli/Cli.cs
    title: Cli (decompiled)
  - id: workspace
    resource: ksa-decomp:KSA.Kfs.Host.Workbench/Workspace.cs
    title: Workspace (decompiled)
  - id: gametables
    resource: ksa-decomp:KSA.Kfs.Vm.Tables/GameTables.cs
    title: GameTables (decompiled)
generated: { by: claude-code/claude-fable-5-2, at: 2026-11-25T21:30:48Z }
status: stable
ksa_build: "2026.11.25.5949"
---

# Verdict

**Not active in this build:** kScript is named throughout the kFS data tables and has a reserved slot
in several back-end code paths, but **there is no kScript lexer, parser or front end in the decompiled
assemblies.** A kScript source file cannot be compiled. Concretely:

- The compiler has exactly one front-end hook, `Compiler.KcFrontEnd`, and exactly one `IFrontEnd`
  implementation, `KSA.Kfs.KC.KcFrontEnd`.[^compiler-hook]
- No source file or embedded resource in the sixteen `KSA.Kfs*` namespace folders is named for
  kScript, and a text search of those folders for `kscript` (any case) finds only the items listed
  below.
- When a project contains a `.py` file, the kC front end reports `KC-E000` "kScript files beside kC is
  not implemented until MS9" for each one.[^frontend-py]
- Nothing ever sets an IR function's language to `"ks"`: kC lowering sets `"kc"` and the default is
  `"kasm"`.[^lowering-lang][^irfunction-lang]

Everything below is what a future kScript evidently has to fit into.

# What the build does contain

## The `kscript` column of `services.json`

Every non-reserved service row carries a kScript spelling next to its kC one. It is deserialised into
`ServiceRow.Kscript` and then never read: the only references to that property are in the generated
JSON serialiser.[^servicerow-kscript] The in-game reference window shows the `kc` column, not this
one ([reference window](/tooling/reference-window.md)).

| id | service | `kc` | `kscript` |
| --- | --- | --- | --- |
| 1 | `log` | `kfs_logf(fmt, …)` | `log(f"…")` |
| 2 | `trace` | `trace(channel, v)` | `trace(channel, v)` |
| 3 | `halt` | `kfs_halt()` | `halt()` |
| 16 | `sleep_until` | `kfs_sleep_until(t_us)` | `sleep_until(t)` |
| 17 | `sleep_until_event` | `kfs_sleep_until_event(e, p)` | `sleep_until_event(e, <keyword>=p)` (§10) |
| 32 | `stage` | `kfs_stage()` | `stage()` |
| 48 | `hold_direction` | `kfs_hold_direction(dir, roll)` | `hold_attitude(dir, roll=…)` |
| 49 | `hold_rates` | `kfs_hold_rates()` | `hold_rates()` |
| 50 | `release_attitude` | `kfs_release_attitude()` | `release_attitude()` |
| 51 | `set_attitude_profile` | `kfs_set_attitude_profile(p)` | `set_attitude_profile(p)` |
| 52 | `set_throttle` | `kfs_set_throttle(x)` | `set_throttle(x)` |
| 53 | `engines_on` | `kfs_engines_on()` | `engines_on()` |
| 54 | `translate` | `kfs_translate(dv)` | `translate(dv)` |
| 55 | `hold_heading` | `kfs_hold_heading(heading_rad, pitch_rad, roll)` | `hold_attitude(heading(compass, pitch), roll=…)` |
| 56 | `hold_quat` | `kfs_hold_quat(q, roll)` | `hold_attitude(q, roll=…)`, `q: Quat[HELM, CCI]` |
| 57 | `engines_off` | `kfs_engines_off()` | `engines_off()` |
| 64 | `plan_circularize` | `kfs_plan_circularize(at)` | `plan_circularize(at=…)` |
| 65 | `plan_burn` | `kfs_plan_burn(dv, t_us)` | `plan_burn(dv, at)` |
| 66 | `burn_start` | `kfs_burn_start(b)` | `execute_burn(b)` (requests, then suspends until done) |
| 67 | `burn_abort` | `kfs_burn_abort()` | `abort_burn()` |

Source: [^services-json-rows]. Three kC services (`hold_direction`, `hold_heading`, `hold_quat`) share one
kScript name, `hold_attitude`, and `execute_burn` is described as suspending — so the planned language
overloads on argument type and has suspending calls.

## `events.json` keywords

Each event row has a `keyword` column — `lead=` for `APOAPSIS` and `PERIAPSIS`, `burn=` for
`BURN_START`, `altitude=` for `ALTITUDE_BELOW`, `msg=` for `MESSAGE` — matching the
`sleep_until_event(e, <keyword>=p)` spelling above. No code reads `EventRow.Keyword` outside the
serialiser.[^events-json-keywords]

## Enumerations

`enums.json` defines `debug_lang` as `["kASM", "kC", "kScript", "generated"]`, and the VM-side enum
mirrors it:[^enums-json-debuglang][^debuglang-enum]

```csharp
public enum DebugLang : byte { KASM, KC, KScript, Generated }
```

The same table's `debug_type_kind` includes `vector`, `matrix` and `record`, and `task_kind` includes
`program`; none of those three type kinds is produced by kC, and the `program` task kind is produced
only by kASM. `Inferred:` they are provisions for kScript's types and its `@program` tasks (see the
diagnostics below).

## Diagnostics reserved for kScript

`diagnostics.json` tags 60 of its 126 rules with the language prefix `KS`; 27 of them are `KS`-only.
The kScript-only rules describe the language better than anything else in the build:[^diag-json-ks]

| number | rule |
| --- | --- |
| 011 | Tab in indentation |
| 102 | Float literal loses precision |
| 201 | Incompatible assignments to one variable |
| 205 | Too many specialisations of one function |
| 210 | Vector times vector |
| 220 | Mixed frames |
| 230 | Subtracting two single-precision absolute positions |
| 240 | Missing unit on a literal |
| 241 | Mixed dimensions |
| 242 | Dimensionless value passed to a trigonometric function |
| 306 | Tuple return of more than 8 cells |
| 320 | Unknown module |
| 402 | `@program` loop with no suspension point |
| 403 | Task or `init` calls a function that can suspend |
| 601 | Appending to an array |
| 602 | Dict literal |
| 603 | Class that isn't a record or `Enum` |
| 604 | Exceptions |
| 605 | `lambda` or nested function |
| 606 | Comprehension |
| 607 | `print` |
| 608 | Slicing |
| 609 | Non-`bool` condition |
| 610 | `x or default` |
| 611 | String stored in a variable |
| 612 | `match` statement |
| 613 | f-string outside the pre-3.12 rules |

`Inferred:` taken together with the `.py` extension, `log(f"…")` and keyword arguments, kScript is
designed as a statically typed **Python subset** with indentation syntax, type inference with function
specialisation, vectors and records, units of measure, reference-frame checking and a `@program`
decorator for a suspending main task. None of this is implemented.

Three further rules are listed for **kC** and concern a kC-to-kScript export interface: 670 "Malformed
`@ks` annotation", 671 "Unsigned value out of kScript's range on export", 672 "Declaration not
exportable to kScript". No code emits them, and the kC front end does not look for `@ks` annotations.

## Back-end plumbing

The stages after the front end already distinguish a third language tag `"ks"` on IR functions:

| place | what it does with `"ks"` |
| --- | --- |
| `Emitter` | maps it to the diagnostic prefix `KS` and to `DebugLang.KScript` in debug info; treats it like `"kc"` in several compiled-code checks; counts its code bytes separately[^emitter-ks] |
| `BusAnalysis`, `FunctionAnalysis`, `ProgramAnalysis` | same prefix mapping and "compiled language" treatment |
| `CompileReport` / `ReportByLanguage(Kc, Kscript, Kasm)` | a "kScript N" line in the size report when the byte count is non-zero[^report-ks] |
| `ReportView`, `Definitions` (host views) | display the same split |

Since no function ever carries that tag, these paths are dormant.

## Other traces

- `LayoutGenerator.TagName(group, tag, bool kscript)` turns a part's tag into an identifier: lower-case
  for kScript, or `THR_` / `ENG_` / `GIM_` plus upper-case for C. It has no callers in the decompiled
  namespaces.[^layoutgen-tagname]
- The command-line compiler gathers `*.py` files along with `.c`, `.h` and `.kasm` when it builds a
  project folder — which is the only way to reach the "not implemented until MS9" message; the
  in-game workspace recognises only `.kasm`, `.kinc`, `.c` and `.h`.[^cli-py][^workspace-ext] As the
  `KSA.Kfs.Cli` entry points are unreachable in the shipped game ([CLI](/tooling/cli.md)), a player
  cannot produce that message at all.

# Modding notes

- There is nothing to hook for kScript. A mod that adds service rows can leave `Kscript` null:
  `GameTables.BuildServices` requires `Kind`, `ArgTypes`, `ResultTypes`, `CostClass`, `Symbol` and
  `Summary` (and, through `CheckDomains`, `ArgDomains` and `HostChecks`), and neither language
  column.[^gametables-required] The full row recipe is in
  [adding a service](/modding/adding-a-service.md).
- If a later build ships kScript, expect it to arrive as a second `IFrontEnd` (or as `.py` handling
  inside `KcFrontEnd.Lower`, where the placeholder is today) and to bind services through the
  `kscript` column the way kC binds through `kc`. A mod that fills both columns for its own services
  is positioned for that, but this is speculation about unreleased code.
- For now, the only languages a mod's features need to be reachable from are kC
  ([kC builtins and intrinsics](/language/kc-builtins-and-intrinsics.md)) and kASM
  ([kASM assembly language](/compiler/kasm-assembly-language.md)).

[^compiler-hook]: Compiler.cs:24, 68
[^frontend-py]: KcFrontEnd.cs:50-53
[^lowering-lang]: Lowering.cs:77-85
[^irfunction-lang]: IrFunction.cs:36
[^servicerow-kscript]: ServiceRow.cs:5
[^services-json-rows]: KSA.Kfs.Vm.Tables.Data.services.json:5-25
[^events-json-keywords]: KSA.Kfs.Vm.Tables.Data.events.json:5-12
[^enums-json-debuglang]: KSA.Kfs.Vm.Tables.Data.enums.json:7-11
[^debuglang-enum]: DebugLang.cs:3-9
[^diag-json-ks]: KSA.Kfs.Vm.Tables.Data.diagnostics.json:5-130
[^emitter-ks]: Emitter.cs:505-507, 552, 584, 666-669, 792
[^report-ks]: CompileReport.cs:58-64, 202-204
[^layoutgen-tagname]: LayoutGenerator.cs:140-174
[^cli-py]: Cli.cs:315-317
[^workspace-ext]: Workspace.cs:15
[^gametables-required]: GameTables.cs:556-572, 641-646
