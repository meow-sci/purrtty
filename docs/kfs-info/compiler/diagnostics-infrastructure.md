---
type: Subsystem Reference
title: Diagnostics infrastructure (Diagnostic, DiagnosticBag, DiagnosticRegistry, Severity, Span)
description: How compiler and assembler diagnostics are created, numbered, prefixed, capped and rendered, how they relate to the embedded diagnostics table, and every diagnostic number the middle end, back end and assembler emit.
resource: ksa-decomp:KSA.Kfs.Compiler/DiagnosticBag.cs
tags: [kfs, compiler, diagnostics, errors, warnings]
sources:
  - id: diagnostic
    resource: ksa-decomp:KSA.Kfs.Compiler/Diagnostic.cs
    title: Diagnostic (decompiled)
  - id: diagnostic-bag
    resource: ksa-decomp:KSA.Kfs.Compiler/DiagnosticBag.cs
    title: DiagnosticBag (decompiled)
  - id: diagnostic-registry
    resource: ksa-decomp:KSA.Kfs.Compiler/DiagnosticRegistry.cs
    title: DiagnosticRegistry (decompiled)
  - id: severity-span
    resource: ksa-decomp:KSA.Kfs.Compiler/Severity.cs
    title: Severity and Span (decompiled; Severity.cs, Span.cs)
  - id: limit-exception
    resource: ksa-decomp:KSA.Kfs.Compiler/LimitException.cs
    title: LimitException (decompiled)
  - id: diagnostics-json
    resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.diagnostics.json
    title: diagnostics.json (embedded table)
  - id: diagnostic-row
    resource: ksa-decomp:KSA.Kfs.Vm.Tables/DiagnosticRow.cs
    title: DiagnosticRow and DiagnosticsFile (decompiled)
  - id: diagnostic-manual
    resource: ksa-decomp:KSA.Kfs.Host.Views/DiagnosticManual.cs
    title: DiagnosticManual (decompiled)
  - id: compiler
    resource: ksa-decomp:KSA.Kfs.Compiler/Compiler.cs
    title: Compiler (decompiled)
  - id: assembler
    resource: ksa-decomp:KSA.Kfs.KAsm/Assembler.cs
    title: kASM Assembler (decompiled)
  - id: expr
    resource: ksa-decomp:KSA.Kfs.KAsm/Expr.cs
    title: kASM Expr (decompiled)
  - id: emitter
    resource: ksa-decomp:KSA.Kfs.Compiler.Backend/Emitter.cs
    title: Emitter (decompiled)
  - id: analysis
    resource: ksa-decomp:KSA.Kfs.Compiler/ProgramAnalysis.cs
    title: ProgramAnalysis (decompiled)
  - id: bus-analysis
    resource: ksa-decomp:KSA.Kfs.Compiler/BusAnalysis.cs
    title: BusAnalysis (decompiled)
  - id: workbench
    resource: ksa-decomp:KSA.Kfs.Host.Workbench/Workbench.cs
    title: Workbench (decompiled)
  - id: cli
    resource: ksa-decomp:KSA.Kfs.Cli/Cli.cs
    title: kvm-cli (decompiled)
  - id: offline-harness
    resource: /modding/offline-harness.md
    title: Offline harness experiments E1–E12
generated: { by: claude-code/claude-fable-5-2, at: 2026-11-25T21:29:02Z }
status: stable
ksa_build: "2026.11.25.5949"
---

# Types

```csharp
public enum Severity { Error, Warning }
public readonly record struct Span(string File, int Line, int Col);
public sealed record Diagnostic(Severity Severity, string Code, string File, int Line, int Col,
                                string Message, string? Hint = null);
```

- `Span` is a point, not a range: file name, 1-based line, column. A `Span` with `Line == 0` means "no
  position" (used with the project name as `File`); the back end tests `Line > 0` before emitting debug
  lines or cost estimates.[^severity-span]
- `Diagnostic.ToString()` is the canonical one-line rendering, with an optional second line:[^diagnostic]

```text
<File>:<Line>:<Col>: error|warning <Code>: <Message>
  hint: <Hint>
```

There are only two severities. Nothing in the pipeline promotes a warning to an error.

# `DiagnosticBag`

`public sealed class DiagnosticBag` collects diagnostics in emission order.[^diagnostic-bag]

| member | behaviour |
| --- | --- |
| `const int MAX_ERRORS = 100` | error cap |
| `IReadOnlyList<Diagnostic> All` | the live list (errors and warnings interleaved) |
| `int ErrorCount`, `bool HasErrors`, `bool Full` | `Full` = `ErrorCount >= 100` |
| `void Error(string prefix, int number, Span at, string message, string? hint = null)` | ignored when `Full`; otherwise appends code `"{prefix}-E{number:000}"`; when that makes the bag full, also appends one more error with the same code and position and the message **"too many errors; stopping"** |
| `void Warning(string prefix, int number, Span at, string message, string? hint = null)` | appends code `"{prefix}-W{number:000}"`; never capped |

Codes therefore look like `KC-E301`, `KA-W711`, `KA-E005`. Passes that loop over many items check
`_d.Full` to stop early (the assembler's `PassA`/`PassB`, the kC unit loop).

One bag is shared by a whole build: `Compiler.CompileOnThisThread` creates it and hands it to the front
end and the emitter; the front end passes the same bag to the embedded assembler through
`Assembler.Diagnostics` (an `init` property), so kASM errors inside a kC project land in the same list.
A standalone `Assembler` owns a private bag by default.[^assembler-diags]

# Prefixes

| prefix | language | chosen by |
| --- | --- | --- |
| `KC` | kC | front end; any back-end diagnostic about a function with `Language == "kc"`; the emitter's default prefix on the kC route |
| `KA` | kASM | the assembler (constant `P = "KA"`); any back-end diagnostic about a function whose language is neither `"kc"` nor `"ks"`; the emitter's default prefix on the kASM route |
| `KS` | kScript | functions with `Language == "ks"` — `Not active in this build:` no front end produces them |

The mapping function appears three times with identical bodies: `Emitter.Prefix`,
`BusAnalysis.Prefix` and `FunctionAnalysis.Prefix`.

# Numbers and the diagnostics table

Numbers are **integer literals at each call site**. Emitting code never consults the table: the
severity is decided by whether the site calls `Error` or `Warning`, and the message and hint are
string literals or interpolations at the site.

The embedded `diagnostics.json` (`version` 1, 126 rules) is the catalogue of what each number means.
Its row type:[^diagnostic-row]

```csharp
public sealed record DiagnosticRow(string Number, string Rule, string Range, string Severity,
                                   string? Default, List<string> Languages);
public sealed record DiagnosticsFile(int Version, string Source, List<DiagnosticRow> Rules);
```

| field | meaning |
| --- | --- |
| `Number` | three-digit string, e.g. `"301"` |
| `Rule` | one-line description |
| `Range` | the hundred-block, e.g. `"3xx"` |
| `Severity` | the specification's severity cell as written (`E`, `W`, `W, E strict`, `E, strict only`, …) |
| `Default` | the letter reported outside strict mode, `null` for strict-only rules |
| `Languages` | prefixes the rule applies to (`KC`, `KS`, `KA`) |

The table is documented in full in [diagnostics table](/tables/diagnostics-table.md) and
[kC diagnostics](/language/kc-diagnostics.md).

Two things read the table:

- `DiagnosticManual.Page(tables, code, hint)` — strips everything up to and including the letter after
  the dash, finds the row with that `Number`, and renders "`<code>: <rule>` / `severity … (default …);
  range …; languages …` / `hint: …`", or "`<code>: not in the diagnostics registry`". The editor's
  diagnostic tooltip and a workbench command use it.[^diagnostic-manual]
- `DiagnosticRegistry` (below).

## `DiagnosticRegistry`

```csharp
public sealed class DiagnosticRegistry {
    public DiagnosticRegistry(GameTables tables);          // throws GameTablesException on a duplicate number
    public IReadOnlyDictionary<int, DiagnosticRow> Rows { get; }
    public bool Has(string prefix, int number);            // number listed and prefix among its Languages
    public string? DefaultLetter(int number);              // the row's Default, or null
    public IEnumerable<int> NumbersFor(string prefix);     // ascending
}
```

It indexes `tables.DiagnosticsData.Rules` by `int.Parse(Number)`; a repeated number throws
"diagnostics.json: number N is listed twice (one number is one rule, D-060)".[^diagnostic-registry]

`Not active in this build:` no code in the decompiled assemblies constructs a `DiagnosticRegistry`.
`Inferred:` it exists for the toolchain's own tests (checking that every emitted number is registered
for its prefix); the shipping pipeline does not validate codes against the table.

# kASM faults that become internal errors in kC

`ProgramAnalysis` checks structural properties that a kASM author can get wrong but the kC lowering
must never get wrong. For a kASM function the user-facing number is used; for any other language the
same condition is reported as **900** with "internal compiler error: …" and the hint "This is a bug in
kFS, not in your program. Report it with the source."[^analysis-violation]

Other sources of 900:

| site | message |
| --- | --- |
| `Compiler.Compile` timeout | "internal compiler error: the compile did not finish in N s and was abandoned" |
| `IrValidator` failure after lowering | "internal compiler error: the IR validator rejected the lowering (…)" |
| `IrValidator` failure after peephole | "internal compiler error: the IR validator rejected the peephole output (…)" |
| workbench post-build check | code `KC-E900` / `KA-E900`, "the built image fails validation: `<rule>: <message>`" — constructed directly, not through a bag[^workbench-diag] |

The workbench also constructs one diagnostic with the non-standard code `"kfs"` when there is nothing
to build.[^workbench-diag]

# `LimitException`

Deeply recursive code cannot return an error gracefully, so translation limits throw
`LimitException(number, message, at)`. `Compiler.CompileOnThisThread` and `Assembler.Assemble` catch it
and turn it into a single error with that number (position = `ex.At`, or the project name / an empty
span) and a hint chosen by number.[^limit-exception-catch]

| number | thrown for | hint (compiler / assembler) |
| --- | --- | --- |
| 720 | nesting deeper than 128; compiler stack exhausted | — |
| 721 | more than 1,000 binary operators in one expression (kC) | — |
| 722 | include depth over 32 | "A header that includes itself, or a cycle; add an include guard." / "A file that includes itself?" |
| 723 | more than 1,000 includes | same as 722 |
| 724 | more than 150,000 tokens | "No program this size fits any part's program memory." / — |
| 725 | macro expansion over 200,000 tokens | "A macro that repeats its argument at each level (of nesting) grows exponentially." |

`ExprException(number, message, hint)` is the assembler's expression-evaluator equivalent; `TryEval`
catches it and reports it at the directive or instruction being assembled.

# Numbers emitted by the middle end, back end and assembler

Rule text is from `diagnostics.json`; "sites" lists where this slice emits the number. The kC front end
emits many more — see [kC diagnostics](/language/kc-diagnostics.md).

| number | rule (table) | table severity | sites |
| --- | --- | --- | --- |
| 001 | Unknown mnemonic | E | assembler (`CALL.auto`, unknown mnemonic with a nearest-match hint, `ENTER`) |
| 002 | Unknown directive | E | assembler (unknown; IR-only `.lang` `.loop` `.endloop`) |
| 003 | Unknown symbol or undeclared identifier | E | assembler (message, type, function, symbol); `Expr`; emitter (symbol operand, data relocation) |
| 004 | Invalid alignment on an untyped `.local` | E | assembler (`.global`, `.local`) |
| 005 | Jump to a label not defined in this function | E | assembler; `ProgramAnalysis`; emitter |
| 012 | Syntax error: the source doesn't parse | E | assembler (most structural errors); `Expr`; `ProgramAnalysis` (`ENTER` in a body) |
| 015 | Include file not found | E | assembler `.include` |
| 101 | Literal or immediate out of range or of the wrong kind | E | assembler (immediates, floats, `share=`, data items); `Expr`; `ProgramAnalysis` (**unknown `SYSCALL` id**) |
| 209 | Store to constant data | E | assembler (`STG`/`STG64` to a `.const`) |
| 260 | Array index without a preceding `BOUNDS` (lint) | W | `ProgramAnalysis.Lints` |
| 301 | Recursion | E | `ProgramAnalysis.Recursion` |
| 304 | Called function has no definition in the project or library | E | emitter; kC `Link` |
| 310 | Prototype doesn't match the function's arguments | E | assembler `.proto` vs `.args` |
| 330 | Inconsistent operand-stack height | E | `ProgramAnalysis` |
| 331 | `RET n` inconsistent with the result cells or stack height, or n > 8 | E | assembler; `ProgramAnalysis` |
| 332 | Operand stack below the function's entry level | E | `ProgramAnalysis` |
| 333 | Function falls off its end without `RET` | E | assembler `CheckProgram`; `ProgramAnalysis` |
| 401 | Entry function missing or with the wrong signature | E | assembler |
| 404 | kASM `YIELD` reachable from `init`, a periodic task or a non-suspending caller | E | `ProgramAnalysis.Yields` |
| 405 | More than one `program` task in the program, across languages | E | assembler `CheckProgram` |
| 406 | Bad or duplicate `tick_hz` | E | assembler |
| 407 | Task rate doesn't divide `tick_hz` | E | assembler |
| 408 | Task order between files is ambiguous (same rate and priority) | E | emitter |
| 520 | Feature or block unavailable on this processor | E | assembler (`.claim ALLOCATION`, GNC publish on the Sequencer) |
| 540 | Write to a subscription buffer | E | `BusAnalysis` |
| 542 | Publication buffer written by no task or by more than one task, or a message published through more than one buffer | E | `BusAnalysis` |
| 544 | Command block written without claiming `ALLOCATION` | E | `BusAnalysis` |
| 546 | Store not attributable to a buffer | W, E strict | `BusAnalysis` (always a warning) |
| 550 | `log` over 8 values or 64 bytes, or invalid format | E | assembler `.format` |
| 701 | Stack, RAM or program size exceeded, or more than `max_tasks` tasks | E | emitter |
| 703 | Branch beyond `rel16` range | E | emitter |
| 711 | Loop without a provable or declared bound (rule 2) | W, E strict | `ProgramAnalysis` (kASM; always a warning) |
| 713 | Near `CALL` target can't be placed in the first 64 KB | E | emitter |
| 717 | Function frame over 65,528 bytes | E | assembler |
| 718 | Instruction, or a task's start or completion step with its bus copies, costs more than one tick's budget | E | emitter; `BusAnalysis` |
| 719 | Argument area over 255 cells | E | assembler |
| 720, 722–725 | translation limits | E | `LimitException` (assembler, `Expr`) |
| 726 | Object or type larger than the largest RAM | E | assembler (`.global`, `.zero`, `.data`) |
| 900 | Internal compiler error | E | `Compiler`; `ProgramAnalysis` for non-kASM functions |

Table rules with strict-only behaviour — 702 (WCET over budget), 704–706, 708, 709 — are never emitted, and the
"E strict" halves of 546 and 711 are never applied: `CompileOptions.Strict` is not read anywhere, and
the assembler's `.strict` directive is a no-op. `Not active in this build.`

The one table number that appears only as display text is 712 ("Measured cycles exceed the computed
worst case"): the report view prints "KA-W712: measured …" beside a task row; it is not a
`Diagnostic`.

# Consumers of a diagnostic list

| consumer | use |
| --- | --- |
| `CompileResult.Diagnostics` / `.Errors`, `AssembleResult.Diagnostics` / `.Errors` | programmatic access |
| `kvm-cli` | prints `Diagnostic.ToString()` per item to stderr, or with `--json` a `DiagJson(Severity "error"/"warning", Code, File, Line, Col, Message, Hint)` object per line[^cli-diag] |
| workbench `AssembleOutcome` | carries the list to the editor (gutter marks, tooltips via `DiagnosticManual.Page`) |

# Modding notes

- `DiagnosticBag`, `Diagnostic`, `Span` and `Severity` are public; a mod that extends the toolchain
  (for example a wrapping `IFrontEnd`) can add its own diagnostics to the build's bag with any prefix
  and number. Codes are free-form strings — nothing validates them — but
  `DiagnosticManual.Page` will show "not in the diagnostics registry" unless a `DiagnosticRow` with
  that three-digit number exists in `tables.DiagnosticsData.Rules` (a mutable `List`).
- The 100-error cap is a constant; after it, `Error` silently drops everything.
- A mod-added service that is missing from the tables surfaces to kASM users as `KA-E003` (an
  unknown `SVC_*` symbol) or `KA-E101` (a numeric id), and to kC users before any IR exists.
  `Verified:` without a prototype the call is `KC-E003` "'<name>' is not declared"; with a prototype
  in a `<…>` header but the name absent from `SystemHeaders.Builtins` it is `KC-E304` "'<name>' is
  declared but never defined".[^offline-harness] `Inferred:` `KC-E900` "internal compiler error"
  is left for a `SYSCALL` with an unlisted id that reaches the IR some other way — a numeric id in a
  `.kasm` file of a kC project (`IrValidator` checks the merged module before program analysis
  runs) or a wrapping front end. Make sure table rows are installed before any build runs.

[^severity-span]: Severity.cs:3-7; Span.cs:3
[^diagnostic]: Diagnostic.cs:3-9
[^diagnostic-bag]: DiagnosticBag.cs:5-36
[^diagnostic-row]: DiagnosticRow.cs:5; DiagnosticsFile.cs:5; KSA.Kfs.Vm.Tables.Data.diagnostics.json
[^diagnostic-manual]: DiagnosticManual.cs:9-51; Workbench.cs:2132; KfsEditorWindow.cs:2149
[^diagnostic-registry]: DiagnosticRegistry.cs:8-52
[^assembler-diags]: Assembler.cs:47, 99-109; KcFrontEnd.cs:330-336
[^analysis-violation]: ProgramAnalysis.cs:100, 147, 159, 189, 224-234
[^workbench-diag]: Workbench.cs:499, 551-556
[^limit-exception-catch]: Compiler.cs:76-101; Assembler.cs:155-177
[^cli-diag]: Cli.cs:339-342, 375-378; DiagJson.cs
[^offline-harness]: Experiment E3 in the offline harness document.
