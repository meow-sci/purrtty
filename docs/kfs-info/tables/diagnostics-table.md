---
type: Data Table Reference
title: diagnostics.json — the compiler diagnostic rule registry
description: Schema and complete contents of the embedded diagnostics table (126 numbered rules for kC, kScript and kASM), and how the game uses it.
resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.diagnostics.json
tags: [kfs, tables, diagnostics, compiler, kc, kasm, kscript]
sources:
  - id: diagnostics-json
    resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.diagnostics.json
    title: diagnostics.json (embedded resource)
  - id: diagnostic-records
    resource: ksa-decomp:KSA.Kfs.Vm.Tables/DiagnosticRow.cs
    title: DiagnosticsFile, DiagnosticRow (decompiled)
  - id: diagnostic-manual
    resource: ksa-decomp:KSA.Kfs.Host.Views/DiagnosticManual.cs
    title: DiagnosticManual (decompiled)
  - id: diagnostic-registry
    resource: ksa-decomp:KSA.Kfs.Compiler/DiagnosticRegistry.cs
    title: DiagnosticRegistry (decompiled)
  - id: diagnostic-bag
    resource: ksa-decomp:KSA.Kfs.Compiler/DiagnosticBag.cs
    title: DiagnosticBag, Diagnostic (decompiled)
  - id: workbench
    resource: ksa-decomp:KSA.Kfs.Host.Workbench/Workbench.cs
    title: Workbench (decompiled)
  - id: kfs-editor-window
    resource: ksa-decomp:KSA.Kfs/KfsEditorWindow.cs
    title: KfsEditorWindow (decompiled)
  - id: game-tables
    resource: ksa-decomp:KSA.Kfs.Vm.Tables/GameTables.cs
    title: GameTables (decompiled)
  - id: offline-harness
    resource: /modding/offline-harness.md
    title: Offline harness experiments E1–E12
generated: { by: claude-code/claude-fable-5-2, at: 2026-11-25T21:52:36Z }
status: stable
ksa_build: "2026.11.25.5949"
---

# Purpose

`diagnostics.json` is the registry of compiler **rules**: one row per three-digit diagnostic number,
with the rule text, severity and the languages it applies to. It is reference data — the text shown
by `kfs manual <code>` and the editor's diagnostic tooltip. The compilers do **not** read it to decide
what to report or how severe a finding is; message texts and severities are in the C# code
([/compiler/diagnostics-infrastructure.md](/compiler/diagnostics-infrastructure.md),
[/language/kc-diagnostics.md](/language/kc-diagnostics.md)).

# File header

| key | value |
| --- | --- |
| `version` | `1` (range-checked only) |
| `rules` | 126 rows |

`source`, verbatim (diagnostics.json:3):[^diagnostics-json]

> kFS_compiler_spec.md §11.1 (moved 2026-11-02, D-052). 'severity' is the spec cell as written;
> 'default' the letter reported outside strict mode (null for strict-only rules); 'languages' the
> prefixes. Number 405 names one rule; KC's `main` is 409 (D-060)

# Schema

- `public sealed record DiagnosticsFile(int Version, string Source, List<DiagnosticRow> Rules)` — JSON
  `version`, `source`, `rules`.
- `public sealed record DiagnosticRow(string Number, string Rule, string Range, string Severity, string? Default, List<string> Languages)`.[^diagnostic-records]

| property | JSON key | type | meaning |
| --- | --- | --- | --- |
| `Number` | `number` | `string` | three digits, zero-padded (`"001"` … `"900"`); a string, not an integer |
| `Rule` | `rule` | `string` | the rule, in Markdown (backticks around code) |
| `Range` | `range` | `string` | the hundreds group as the spec labels it: `0xx` … `9xx`, with `6xx (kScript)`, `6xx (kC)`, `6xx (interface)` sub-groups |
| `Severity` | `severity` | `string` | "the spec cell as written": `E`, `W`, or a conditional phrase such as `W, E strict` |
| `Default` | `default` | `string?` | the letter reported outside strict mode: `"E"`, `"W"`, or JSON `null` for strict-only rules. The key is **required** even when null (no constructor default) |
| `Languages` | `languages` | `List<string>` | language prefixes the rule applies to: `KC` (kC), `KS` (kScript), `KA` (kASM) |

`GameTables.Build` checks only the version range for this file.[^game-tables]

## How a code maps to a row

`DiagnosticBag` formats codes as `{prefix}-E{number:000}` for errors and `{prefix}-W{number:000}` for
warnings — e.g. `KC-E501`, `KA-W250` — and which of `Error(…)` / `Warning(…)` the compiler calls
decides the letter.[^diagnostic-bag] `DiagnosticManual.Page(tables, code, hint)` takes the text after
the first `-` minus one character (the letter) as the number — or the whole string when there is no
`-` — and finds the row with that `Number` (DiagnosticManual.cs:13–15). Output:[^diagnostic-manual]

```text
{code}: {rule}
severity {severity} (default {default}); range {range}; languages {KC, KS, KA}
hint: {hint}            (only when a hint was passed)
```

or `{code}: not in the diagnostics registry` when no row matches. Callers: `Workbench.Manual`
(`kfs manual`, Workbench.cs:2123–2132) and the editor tooltip (KfsEditorWindow.cs:2149).[^workbench][^kfs-editor-window]

`DiagnosticRegistry` (`KSA.Kfs.Compiler`) would index the rows by integer number, reject duplicates
with `diagnostics.json: number {n} is listed twice (one number is one rule, D-060)` and answer
`Has(prefix, number)`, `DefaultLetter(number)` and `NumbersFor(prefix)`; `Not active in this build:`
nothing constructs it.[^diagnostic-registry]

# Summary of the contents

| range | rules | theme (from the rule texts) |
| --- | --- | --- |
| `0xx` | 14 | lexical/syntactic: unknown names, includes, `#error`/`#warning`, unused names |
| `1xx` | 6 | literals and constant expressions |
| `2xx` | 20 | types, conversions, units and frames |
| `3xx` | 13 | functions, calls, recursion, kASM stack discipline |
| `4xx` | 10 | program structure: entry points, tasks, tick rates |
| `5xx` | 12 | flight interface: services, ownership, bus buffers, logging |
| `6xx (kScript)` | 13 | Python constructs kScript rejects |
| `6xx (kC)` | 11 | C constructs kC rejects |
| `6xx (interface)` | 3 | kC → kScript export annotations |
| `7xx` | 23 | resource limits, worst-case analysis, Power of Ten rules |
| `9xx` | 1 | internal compiler error |

Severity cells: `E` 95, `W` 19, `W, strict only` 5, `W, E strict` 4, and one each of
`W in KC, E in KS`, ``E (W with `units = relaxed`)``, `E, strict only`. `default`: `E` 96, `W` 24,
`null` 6. Language sets: `KC` only 41, `KS` only 27, all three 25, `KA` only 14, `KC`+`KA` 11,
`KC`+`KS` 7, `KA`+`KS` 1. Rules whose only language is `KS` describe kScript, which is not compiled
in this build ([/language/kscript-status.md](/language/kscript-status.md)).

# The table

`line` = line in diagnostics.json.[^diagnostics-json]

| `number` | `rule` | `range` | `severity` | `default` | `languages` | line |
| --- | --- | --- | --- | --- | --- | --- |
| `001` | Unknown mnemonic | 0xx | E | `E` | KA | 5 |
| `002` | Unknown directive | 0xx | E | `E` | KA | 6 |
| `003` | Unknown symbol or undeclared identifier | 0xx | E | `E` | KC, KS, KA | 7 |
| `004` | Invalid alignment on an untyped `.local` | 0xx | E | `E` | KA | 8 |
| `005` | Jump to a label not defined in this function | 0xx | E | `E` | KA | 9 |
| `006` | Unused variable, label, local or global | 0xx | W | `W` | KC, KA | 10 |
| `010` | Unknown pragma | 0xx | W | `W` | KC | 11 |
| `011` | Tab in indentation | 0xx | E | `E` | KS | 12 |
| `012` | Syntax error: the source doesn't parse | 0xx | E | `E` | KC, KS, KA | 13 |
| `013` | `break` or `continue` outside a loop or `switch`; `case` or `default` outside a `switch` | 0xx | E | `E` | KC, KS | 14 |
| `014` | `#error` directive reached | 0xx | E | `E` | KC | 15 |
| `015` | Include file not found | 0xx | E | `E` | KC, KA | 16 |
| `016` | Conflicting or duplicate declaration or definition of a name | 0xx | E | `E` | KC | 17 |
| `017` | `#warning` directive reached | 0xx | W | `W` | KC | 18 |
| `101` | Literal or immediate out of range or of the wrong kind | 1xx | E | `E` | KC, KS, KA | 19 |
| `102` | Float literal loses precision | 1xx | W | `W` | KS | 20 |
| `104` | Implicit `float` to `double` promotion without a double FPU | 1xx | W | `W` | KC | 21 |
| `105` | Constant expression required (array size, `case` label, static-storage initialiser) but not given (for example a `const` object, §4) | 1xx | E | `E` | KC | 22 |
| `106` | Duplicate `case` or `default` label in one `switch` | 1xx | E | `E` | KC | 23 |
| `107` | Static assertion failed (`_Static_assert`) | 1xx | E | `E` | KC | 24 |
| `201` | Incompatible assignments to one variable | 2xx | E | `E` | KS | 25 |
| `202` | Implicit narrowing (64-bit to 32-bit, double to single) | 2xx | W in KC, E in KS | `W` | KC, KS | 26 |
| `203` | `double` is single precision (`double_bits = 32`) | 2xx | W | `W` | KC | 27 |
| `204` | Result depends on plain `char` signedness | 2xx | W, E strict | `W` | KC | 28 |
| `205` | Too many specialisations of one function | 2xx | E | `E` | KS | 29 |
| `206` | Possibly uninitialised use | 2xx | W | `W` | KC | 30 |
| `207` | Ignored non-`void` return value (Power of Ten rule 7) | 2xx | W, E strict | `W` | KC | 31 |
| `208` | `switch` on an enum misses a case and has no `default` | 2xx | W | `W` | KC | 32 |
| `209` | Store to constant data | 2xx | E | `E` | KC, KA | 33 |
| `210` | Vector times vector | 2xx | E | `E` | KS | 34 |
| `211` | Incompatible types in assignment, initialisation, argument, return or an operator's operands | 2xx | E | `E` | KC, KS | 35 |
| `212` | Assignment, `++`, `--` or `&` applied to something that isn't a modifiable lvalue | 2xx | E | `E` | KC | 36 |
| `213` | Object declared with `void` or an incomplete type | 2xx | E | `E` | KC | 37 |
| `220` | Mixed frames | 2xx | E | `E` | KS | 38 |
| `230` | Subtracting two single-precision absolute positions | 2xx | W | `W` | KS | 39 |
| `240` | Missing unit on a literal | 2xx | E (W with `units = relaxed`) | `E` | KS | 40 |
| `241` | Mixed dimensions | 2xx | E | `E` | KS | 41 |
| `242` | Dimensionless value passed to a trigonometric function | 2xx | W | `W` | KS | 42 |
| `250` | `==` on floats | 2xx | W | `W` | KC, KS, KA | 43 |
| `260` | Array index without a preceding `BOUNDS` (lint) | 2xx | W | `W` | KA | 44 |
| `301` | Recursion | 3xx | E | `E` | KC, KS, KA | 45 |
| `302` | Dynamic allocation | 3xx | E | `E` | KC | 46 |
| `303` | Function pointer | 3xx | E | `E` | KC | 47 |
| `304` | Called function has no definition in the project or library | 3xx | E | `E` | KC, KA | 48 |
| `305` | Wrong number of arguments in a call | 3xx | E | `E` | KC | 49 |
| `306` | Tuple return of more than 8 cells | 3xx | E | `E` | KS | 50 |
| `307` | Control can reach the end of a non-`void` function | 3xx | W | `W` | KC | 51 |
| `310` | Prototype doesn't match the function's arguments | 3xx | E | `E` | KA | 52 |
| `320` | Unknown module | 3xx | E | `E` | KS | 53 |
| `330` | Inconsistent operand-stack height | 3xx | E | `E` | KA | 54 |
| `331` | `RET n` inconsistent with the result cells or stack height, or n > 8 | 3xx | E | `E` | KA | 55 |
| `332` | Operand stack below the function's entry level | 3xx | E | `E` | KA | 56 |
| `333` | Function falls off its end without `RET` | 3xx | E | `E` | KA | 57 |
| `401` | Entry function missing or with the wrong signature (it takes no arguments and returns nothing) | 4xx | E | `E` | KC, KA | 58 |
| `402` | `@program` loop with no suspension point | 4xx | W | `W` | KS | 59 |
| `403` | Task or `init` calls a function that can suspend | 4xx | E | `E` | KS | 60 |
| `404` | kASM `YIELD` reachable from `init`, a periodic task or a non-suspending caller | 4xx | E | `E` | KA | 61 |
| `405` | More than one `program` task in the program, across languages | 4xx | E | `E` | KA, KS | 62 |
| `406` | Bad or duplicate `tick_hz` | 4xx | E | `E` | KC, KS, KA | 63 |
| `407` | Task rate doesn't divide `tick_hz` | 4xx | E | `E` | KC, KS, KA | 64 |
| `408` | Task order between files is ambiguous (same rate and priority, §2) | 4xx | E | `E` | KC, KS, KA | 65 |
| `409` | `main` defined | 4xx | E | `E` | KC | 66 |
| `410` | More than one `init` task, or a function named by more than one entry pragma | 4xx | E | `E` | KC | 67 |
| `501` | Service requires a stock block the program owns, or steers the game's flight computer from a program that claims `ALLOCATION` | 5xx | E | `E` | KC, KS, KA | 68 |
| `510` | Sensor not fitted on this vehicle | 5xx | E | `E` | KC, KS, KA | 69 |
| `520` | Feature or block unavailable on this processor | 5xx | E | `E` | KC, KS, KA | 70 |
| `540` | Write to a subscription buffer | 5xx | E | `E` | KC, KS, KA | 71 |
| `541` | Bus buffer type doesn't match its message | 5xx | E | `E` | KC, KS | 72 |
| `542` | Publication buffer written by no task or by more than one task, or a message published through more than one buffer | 5xx | E | `E` | KC, KS, KA | 73 |
| `544` | Command block written without claiming `ALLOCATION` | 5xx | E | `E` | KC, KS, KA | 74 |
| `546` | Store not attributable to a buffer | 5xx | W, E strict | `W` | KA | 75 |
| `550` | `log` over 8 values or 64 bytes, or invalid format | 5xx | E | `E` | KC, KS, KA | 76 |
| `551` | Bad sleep-until-event arguments | 5xx | E | `E` | KC, KS | 77 |
| `552` | `kfs_logf` argument doesn't match its format | 5xx | E | `E` | KC | 78 |
| `553` | Format or channel name not a string literal | 5xx | E | `E` | KC | 79 |
| `601` | Appending to an array | 6xx (kScript) | E | `E` | KS | 80 |
| `602` | Dict literal | 6xx (kScript) | E | `E` | KS | 81 |
| `603` | Class that isn't a record or `Enum` | 6xx (kScript) | E | `E` | KS | 82 |
| `604` | Exceptions | 6xx (kScript) | E | `E` | KS | 83 |
| `605` | `lambda` or nested function | 6xx (kScript) | E | `E` | KS | 84 |
| `606` | Comprehension | 6xx (kScript) | E | `E` | KS | 85 |
| `607` | `print` | 6xx (kScript) | E | `E` | KS | 86 |
| `608` | Slicing | 6xx (kScript) | E | `E` | KS | 87 |
| `609` | Non-`bool` condition | 6xx (kScript) | E | `E` | KS | 88 |
| `610` | `x or default` | 6xx (kScript) | E | `E` | KS | 89 |
| `611` | String stored in a variable | 6xx (kScript) | E | `E` | KS | 90 |
| `612` | `match` statement | 6xx (kScript) | E | `E` | KS | 91 |
| `613` | f-string outside the pre-3.12 rules | 6xx (kScript) | E | `E` | KS | 92 |
| `640` | `goto`, labels, `setjmp`/`longjmp` | 6xx (kC) | E | `E` | KC | 93 |
| `641` | `union` or bitfield | 6xx (kC) | E | `E` | KC | 94 |
| `642` | Variable-length array | 6xx (kC) | E | `E` | KC | 95 |
| `643` | User-defined variadic function | 6xx (kC) | E | `E` | KC | 96 |
| `644` | Integer/pointer cast | 6xx (kC) | E | `E` | KC | 97 |
| `645` | `printf` family | 6xx (kC) | E | `E` | KC | 98 |
| `646` | C11 or C23 construct | 6xx (kC) | E | `E` | KC | 99 |
| `647` | GNU extension | 6xx (kC) | E | `E` | KC | 100 |
| `648` | Assignment in a condition | 6xx (kC) | W | `W` | KC | 101 |
| `649` | Comma operator outside a `for` header | 6xx (kC) | E | `E` | KC | 102 |
| `650` | K&R (old-style) function definition | 6xx (kC) | E | `E` | KC | 103 |
| `670` | Malformed `@ks` annotation | 6xx (interface) | W | `W` | KC | 104 |
| `671` | Unsigned value out of kScript's range on export | 6xx (interface) | W | `W` | KC | 105 |
| `672` | Declaration not exportable to kScript | 6xx (interface) | W | `W` | KC | 106 |
| `701` | Stack, RAM or program size exceeded, or more than `max_tasks` tasks | 7xx | E | `E` | KC, KS, KA | 107 |
| `702` | Worst-case cycles exceed the task budget | 7xx | E, strict only | null | KC, KA | 108 |
| `703` | Branch beyond `rel16` range | 7xx | E | `E` | KC, KS, KA | 109 |
| `704` | Function over 60 lines or instructions (Power of Ten rule 4) | 7xx | W, strict only | null | KC, KA | 110 |
| `705` | Assertion density below 2 (rule 5) | 7xx | W, strict only | null | KC | 111 |
| `706` | Global used by one function (rule 6) | 7xx | W, strict only | null | KC | 112 |
| `708` | Restricted preprocessor or macro use (rule 8) | 7xx | W, strict only | null | KC, KA | 113 |
| `709` | Pointer to pointer or multi-level dereference (rule 9) | 7xx | W, strict only | null | KC | 114 |
| `711` | Loop without a provable or declared bound (rule 2) | 7xx | W, E strict | `W` | KC, KS, KA | 115 |
| `712` | Measured cycles exceed the computed worst case | 7xx | W | `W` | KA | 116 |
| `713` | Near `CALL` target can't be placed in the first 64 KB | 7xx | E | `E` | KA | 117 |
| `716` | Aggregate copy or array element over 65,535 bytes | 7xx | E | `E` | KC, KS | 118 |
| `717` | Function frame over 65,528 bytes | 7xx | E | `E` | KC, KS, KA | 119 |
| `718` | Instruction, or a task's start or completion step with its bus copies, costs more than one tick's budget (`kVM_spec.md` §2.6, §3.8, §7) | 7xx | E | `E` | KC, KS, KA | 120 |
| `719` | Argument area over 255 cells (`ENTER`'s u8 `ncells`, `kVM_spec.md` §4.5) | 7xx | E | `E` | KC, KS, KA | 121 |
| `720` | Nesting deeper than 128 (§2 translation limits) | 7xx | E | `E` | KC, KS, KA | 122 |
| `721` | More than 1,000 binary operators in one full expression | 7xx | E | `E` | KC, KS | 123 |
| `722` | Include depth over 32 | 7xx | E | `E` | KC, KA | 124 |
| `723` | More than 1,000 includes in one translation unit | 7xx | E | `E` | KC, KA | 125 |
| `724` | More than 150,000 tokens after preprocessing | 7xx | E | `E` | KC, KS, KA | 126 |
| `725` | Macro expansion produced more than 200,000 tokens | 7xx | E | `E` | KC, KA | 127 |
| `726` | Object or type larger than the largest RAM | 7xx | E | `E` | KC, KS, KA | 128 |
| `727` | `#line` outside 1 to 2147483647 | 7xx | E | `E` | KC | 129 |
| `900` | Internal compiler error (a compiler bug, to be reported) | 9xx | E | `E` | KC, KS, KA | 130 |

# Rules that concern the flight interface

For an extension that adds services or messages, these are the rules whose conditions involve the
tables documented in this directory:

| number | condition | table input |
| --- | --- | --- |
| 501 | a service with `requires_stock` called from a program that owns that block or claims `ALLOCATION` | [services](/tables/services-table.md) `requires_stock` |
| 510 | sensor not fitted on this vehicle — `Not active in this build:` no code emits 510, and vehicle layouts have no sensor fields | [I/O layout](/tables/io-layout-tables.md) |
| 520 | feature or block unavailable on this processor | [catalogue](/tables/catalogue-processors.md) `PartIndex` |
| 540–542, 546 | bus buffer misuse | [messages](/tables/messages-table.md) |
| 544 | command block written without claiming `ALLOCATION` | I/O layout |
| 550, 552, 553 | `log` / `kfs_logf` / channel-name rules | services 1 and 2 |
| 551 | bad sleep-until-event arguments | [events](/tables/events-table.md) |
| 406, 407 | tick-rate rules | catalogue `allowed_tick_hz` |
| 718 | an instruction or bus step costs more than one tick's budget | [cost tables](/tables/opcode-cost-trap-tables-schema.md) |

# Modding notes

- Adding a rule row only adds a `kfs manual` page. A mod's own compile-time checks would be emitted
  through `DiagnosticBag.Error` / `Warning` (public), with any number; an unknown number merely
  yields "not in the diagnostics registry" in the manual and tooltip.
- A new **service** never needs a diagnostics row: unknown service ids, wrong argument counts and
  domain violations are reported by existing rules and by run-time rejection. `Verified:` a mod
  service without a prototype is `KC-E003` and one whose name is missing from
  `SystemHeaders.Builtins` is `KC-E304`, but a multi-cell (struct) result throws inside the kC
  lowering instead of producing a diagnostic — see
  [adding a service](/modding/adding-a-service.md).[^offline-harness]
- `DiagnosticsData.Rules` is a mutable `List<DiagnosticRow>` scanned on every lookup (no cache).

[^diagnostics-json]: diagnostics.json:2–3 (header), 5–130 (rules).
[^diagnostic-records]: DiagnosticsFile.cs:5; DiagnosticRow.cs:5.
[^diagnostic-manual]: DiagnosticManual.cs:11–50.
[^diagnostic-registry]: DiagnosticRegistry.cs:8–52.
[^diagnostic-bag]: DiagnosticBag.cs:19–35; Diagnostic.cs:3–9.
[^workbench]: Workbench.cs:2123–2132.
[^kfs-editor-window]: KfsEditorWindow.cs:2149.
[^game-tables]: GameTables.cs:45, 102, 185.
[^offline-harness]: Experiments E3 and E8 in [the offline harness](/modding/offline-harness.md).
