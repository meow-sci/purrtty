---
type: Extension Point
title: What kFS exposes for a language server
description: The compiler, workbench, table and host APIs an LSP server for kC and kASM can be built on, feature by feature, with the position conventions, the project and overlay rules, and the threading and caching constraints.
resource: ksa-decomp:KSA.Kfs.Host.Workbench/Workbench.cs
tags: [kfs, modding, lsp, language-server, tooling, diagnostics, completion, threading]
sources:
  - id: diagnostic
    resource: ksa-decomp:KSA.Kfs.Compiler/Diagnostic.cs
    title: Diagnostic, Span, Severity (decompiled; Diagnostic.cs, Span.cs, Severity.cs)
  - id: diagnostic-bag
    resource: ksa-decomp:KSA.Kfs.Compiler/DiagnosticBag.cs
    title: DiagnosticBag (decompiled)
  - id: compiler
    resource: ksa-decomp:KSA.Kfs.Compiler/Compiler.cs
    title: Compiler (decompiled)
  - id: compile-input
    resource: ksa-decomp:KSA.Kfs.Compiler/CompileInput.cs
    title: CompileInput, CompileOptions, CompileResult (decompiled)
  - id: limits
    resource: ksa-decomp:KSA.Kfs.Compiler/TranslationLimits.cs
    title: TranslationLimits (decompiled)
  - id: lexer
    resource: ksa-decomp:KSA.Kfs.KC/Lexer.cs
    title: kC Lexer and Token (decompiled; Lexer.cs, Token.cs, TokKind.cs)
  - id: preprocessor
    resource: ksa-decomp:KSA.Kfs.KC/Preprocessor.cs
    title: kC Preprocessor (decompiled)
  - id: parser
    resource: ksa-decomp:KSA.Kfs.KC/Parser.cs
    title: kC Parser (decompiled)
  - id: sema
    resource: ksa-decomp:KSA.Kfs.KC/Sema.cs
    title: kC Sema (decompiled)
  - id: checks
    resource: ksa-decomp:KSA.Kfs.KC/Checks.cs
    title: kC Checks and Lowering (decompiled; Checks.cs, Lowering.cs)
  - id: kc-front-end
    resource: ksa-decomp:KSA.Kfs.KC/KcFrontEnd.cs
    title: KcFrontEnd (decompiled)
  - id: kc-ast
    resource: ksa-decomp:KSA.Kfs.KC/TranslationUnit.cs
    title: TranslationUnit, Obj, Node, NK, CType, StructField (decompiled)
  - id: system-headers
    resource: ksa-decomp:KSA.Kfs.KC/SystemHeaders.cs
    title: SystemHeaders and Library (decompiled; SystemHeaders.cs, Library.cs)
  - id: vehicle-header
    resource: ksa-decomp:KSA.Kfs.KC/VehicleHeader.cs
    title: VehicleHeader (decompiled)
  - id: generated-headers
    resource: ksa-decomp:KSA.Kfs.KC/GeneratedHeaders.cs
    title: GeneratedHeaders (decompiled)
  - id: assembler
    resource: ksa-decomp:KSA.Kfs.KAsm/Assembler.cs
    title: kASM Assembler, AssembleResult, SourceFile (decompiled)
  - id: builtin-includes
    resource: ksa-decomp:KSA.Kfs.KAsm/BuiltinIncludes.cs
    title: BuiltinIncludes and AsmType (decompiled)
  - id: ir
    resource: ksa-decomp:KSA.Kfs.Compiler.Ir/IrModule.cs
    title: IrModule, IrFunction, IrGlobal, IrBusDecl, IrSlot, IrItem records (decompiled)
  - id: analysis
    resource: ksa-decomp:KSA.Kfs.Compiler/ProgramAnalysis.cs
    title: ProgramAnalysis, FunctionAnalysis, TaskFigures, BusAnalysis, BusAccess (decompiled)
  - id: compile-report
    resource: ksa-decomp:KSA.Kfs.Compiler/CompileReport.cs
    title: CompileReport, LineCost, ReportWcet, ReportMachine (decompiled)
  - id: emitter
    resource: ksa-decomp:KSA.Kfs.Compiler.Backend/Emitter.cs
    title: Emitter and EmitResult (decompiled)
  - id: debug-info
    resource: ksa-decomp:KSA.Kfs.Vm.Debug/DebugInfo.cs
    title: DebugInfo and its records (decompiled)
  - id: image
    resource: ksa-decomp:KSA.Kfs.Vm.Image/ImageReader.cs
    title: ImageReader and KvmImage (decompiled)
  - id: workbench
    resource: ksa-decomp:KSA.Kfs.Host.Workbench/Workbench.cs
    title: Workbench (decompiled)
  - id: workspace
    resource: ksa-decomp:KSA.Kfs.Host.Workbench/Workspace.cs
    title: Workspace and BuildProject (decompiled)
  - id: assemble-outcome
    resource: ksa-decomp:KSA.Kfs.Host.Workbench/AssembleOutcome.cs
    title: AssembleOutcome, ListingLine, Listing (decompiled)
  - id: editor-model
    resource: ksa-decomp:KSA.Kfs.Host.Workbench/EditorModel.cs
    title: EditorModel (decompiled)
  - id: editor-window
    resource: ksa-decomp:KSA.Kfs/KfsEditorWindow.cs
    title: KfsEditorWindow (decompiled)
  - id: kfs-integration
    resource: ksa-decomp:KSA.Kfs/KfsIntegration.cs
    title: KfsIntegration (decompiled)
  - id: definitions
    resource: ksa-decomp:KSA.Kfs.Host.Views/Definitions.cs
    title: Definitions, Definition, SourceLink (decompiled)
  - id: diagnostic-manual
    resource: ksa-decomp:KSA.Kfs.Host.Views/DiagnosticManual.cs
    title: DiagnosticManual and DiagnosticRow (decompiled)
  - id: line-cost-view
    resource: ksa-decomp:KSA.Kfs.Host.Views/LineCostView.cs
    title: LineCostView, LineCostRow, BusMarkerView, ReportView, ReportTaskRow (decompiled)
  - id: game-tables
    resource: ksa-decomp:KSA.Kfs.Vm.Tables/GameTables.cs
    title: GameTables (decompiled)
  - id: table-rows
    resource: ksa-decomp:KSA.Kfs.Vm.Tables/ServiceRow.cs
    title: ServiceRow, ServiceInfo, OpcodeInfo, OpcodeRow, TrapRow, SysBlockField, MessageRow, MessageFieldRow, EventRow, ServiceClassRow, CatalogueEnums (decompiled)
  - id: message-layout
    resource: ksa-decomp:KSA.Kfs.Vm.Tables/MessageLayout.cs
    title: MessageLayout, MessageLeaf, MessageMember (decompiled)
  - id: layout
    resource: ksa-decomp:KSA.Kfs.Host/LayoutGenerator.cs
    title: LayoutGenerator, IoLayout, IoField, LayoutFile (decompiled)
  - id: host-computer
    resource: ksa-decomp:KSA.Kfs.Host/HostComputer.cs
    title: HostComputer and MachineSnapshot records (decompiled)
  - id: host-services
    resource: ksa-decomp:KSA.Kfs.Host.Services/ConsoleLog.cs
    title: ConsoleLog, TraceStore, LogLine, TraceSample (decompiled)
  - id: global-leaves
    resource: ksa-decomp:KSA.Kfs.Host.Views/GlobalLeaves.cs
    title: GlobalLeaves, GlobalLeaf, BusMonitorView (decompiled)
  - id: offline-harness
    resource: offline-harness.md
    title: Running kFS offline — the test harness and what it proved (this bundle)
generated: { by: claude-code/claude-fable-5-2, at: 2026-11-25T22:24:14Z }
status: stable
ksa_build: "2026.11.25.5949"
---

# Purpose and scope

This document answers one question: **what can a Language Server (LSP) for kC (`.c`/`.h`) and kASM
(`.kasm`/`.kinc`) get from the game's own toolchain, without writing a second compiler?** It is
organised by LSP feature. For each feature it names the exact game API, says what is free, what
needs new code, and which threading and caching rules apply.

Two hosts are assumed: an LSP server inside the game process (reusing `KfsIntegration.Tables` and
the game's `Workbench`), and the same code in a test process that loads `KSA.dll` with no game (see
[offline harness](offline-harness.md)). Everything named below is `public` unless marked otherwise,
so neither host needs reflection for the core path.[^offline-harness]

The descriptions of game code were read from the decompiled sources. The recipes labelled
`Inferred:` or `Unverified:` (reconstructing a layout from `vehicle.h`, the three ways to keep the
AST, the concurrency verdict) were reasoned from that code and not executed; only the facts
attributed to the offline harness were run.

Related reading: [compile pipeline](/compiler/compile-pipeline.md),
[diagnostics infrastructure](/compiler/diagnostics-infrastructure.md),
[kC front-end internals](/language/kc-front-end-internals.md),
[workbench](/tooling/workbench.md), [build and load flow](/tooling/build-and-load-flow.md),
[workspace and files](/tooling/workspace-and-files.md), [views](/host/views.md).

# The entry points

| entry point | returns | side effects | thread |
| --- | --- | --- | --- |
| `Compiler.Compile(CompileInput)` | `CompileResult(bool Ok, byte[]? Image, ProgramProperties? Program, IReadOnlyList<Diagnostic> Diagnostics, IrModule? Module, int[]? PeepholeFired, ProgramAnalysis? Analysis = null, CompileReport? Report = null)` | none (pure, in memory) | starts a background thread named `kFS compile` with a 16 MiB stack and joins it for `Options.TimeLimitMs` (default 30000)[^compiler] |
| `Compiler.CompileOnThisThread(CompileInput)` | same | none | the caller's thread; no timeout[^compiler] |
| `new Assembler(tables, part, layout) { ProjectFiles = … }.Assemble(IReadOnlyList<SourceFile>)` | `AssembleResult(bool Ok, byte[]? Image, IReadOnlyList<Diagnostic> Diagnostics, IrModule? Module, CompileReport? Report = null)` | none; an `Assembler` is single-use | the caller's thread[^assembler-core] |
| `Workbench.BuildText(string file, string text, PartDescription part, IoLayout layout)` | `AssembleOutcome` (below) | reads project files and their timestamps from disk; writes nothing | calls `Compiler.Compile` for kC; assembles kASM on the caller's thread[^workbench-build-text] |
| `new KcFrontEnd().Lower(CompileInput, IrModule, DiagnosticBag)` | `bool`, and afterwards `KcFrontEnd.Units` (the typed AST) | none | the caller's thread[^kc-front-end-lower] |

`CompileInput` is a record with `required` `Files` (`IReadOnlyDictionary<string, string>`, name →
text), `Part` (`PartDescription`), `Game` (`GameTables`), `Layout` (`IoLayout`), and optional
`SystemFiles` (per-compile system-header overlay, empty by default), `Options` (`CompileOptions`) and
`ProjectName` (default `"project"`). `CompileOptions` has `BoundsChecks = true`, `Optimize = 1`,
`Strict`, `DoubleBits = 64`, `Defines` (`"NAME"` or `"NAME=value"`), `PeepholeOff`, `EmitDebug = true`
and `TimeLimitMs = 30000`.[^compile-input]

`AssembleOutcome` is what the stock editor consumes:[^assemble-outcome]

```csharp
public sealed record AssembleOutcome(bool Ok, IReadOnlyList<Diagnostic> Diagnostics, byte[]? Image,
    uint CodeSize, uint RamRequired, IReadOnlyList<ListingLine> Listing, TimeSpan Elapsed)
{
    CompileReport? Report; IReadOnlyList<LineCost> Estimates; IrModule? Module;
    BuildProject? Project; string Language /* "kC" | "kASM" */; string Part;
    IReadOnlyList<BusAccess> BusAccesses; ReportView? ReportView;
    IReadOnlyList<DisassemblyFunction>? Disassembly;
    IReadOnlyDictionary<string, List<string>?> BusDefinitions; IoLayout? Layout;
    IReadOnlyList<(string Path, DateTime WriteUtc)> SourceStamps;
}
```

## What `Workbench.BuildText` does, exactly

`BuildText` (and its alias `AssembleText`) reads no `Workbench` state except `Tables` and
`Workspace`, assigns no field, prints nothing to `Out`, and does not write `vehicle.h` or
`vehicle.kinc`.[^workbench-build-text] Its steps:

1. `Workspace.ProjectOf(file, file, text, out why)` — the project, with `text` overlaid on `file`
   (next section). An `IOException` is caught; a `null` project returns a failed outcome holding one
   diagnostic `new Diagnostic(Severity.Error, "kfs", file, 0, 0, why ?? "nothing to build")`.
2. kC: `Compiler.Compile(new CompileInput { Files = project.Files, Part, Game = Tables, Layout,
   ProjectName = project.Name })`. kASM: a new `Assembler` over every `.kasm` in the project, with
   `ProjectFiles` reading `.include "…"` files **from disk** (`folder/name`, then root `name`).
3. No image → failed outcome carrying `Diagnostics`, `Project`, `Language`, `Part` and the partial
   `Module`.
4. `ImageReader.Read(image, part, Tables, layout, out KvmImage image)`; a rejection appends a
   `KC-E900`/`KA-E900` diagnostic at `(file, 0, 0)`.
5. Success: `Listing.Build`, `ReportView.From`, `DisassemblyView.Build`, bus definitions,
   `SourceStamps` (last-write times of the other project files).

The methods that *do* have side effects are the console-facing ones: `Workbench.Build(file)` sets
`_lastFile` and `LastBuild`, writes the generated headers through `ReportHeaders` and prints a report;
`BuildQuiet` (used by `Report(file)` and `Lines(file)`) assigns `LastBuild`; `LoadBuilt` (the upload
path) also writes the headers.[^workbench-build-effects] An LSP should call `BuildText`, or the compiler
directly, and never `Build`.

Exceptions are not diagnostics. `Compiler.Compile` rethrows whatever the compile thread threw, and
`BuildText` catches only the `IOException` of step 1. The offline harness observed an
`InvalidOperationException` out of `Lowering` for one unsupported program shape.[^compiler][^offline-harness]
Wrap every build in a `try`/`catch` and report the exception as a file-level diagnostic.

# Conventions an LSP must know

## Positions

```csharp
public readonly record struct Span(string File, int Line, int Col);
public sealed record Diagnostic(Severity Severity, string Code, string File, int Line, int Col,
                                string Message, string? Hint = null);
public enum Severity { Error, Warning }
public sealed record Token(TokKind Kind, string Text, Span At, bool SpaceBefore, bool LineStart);
public enum TokKind { Ident, Number, Char, String, Punct, Eof, Placemarker }
```

| property | kC | kASM |
| --- | --- | --- |
| line base | 1 (`_line = 1`)[^lexer-pos] | 1 (`new Span(path, i + 1, 1)`)[^assembler-pos] |
| column base | 1 (`_col = 1`) | always 1: the assembler records lines, not columns |
| column unit | one per C# `char`, so UTF-16 code units; a tab counts 1[^lexer-pos] | n/a |
| line terminators | `\r\n` and a lone `\r` are rewritten to `\n` before lexing | only `\r\n` is rewritten; lines are `Split('\n')` |
| end position or length | none on `Span`/`Diagnostic`. A `Token` has `Text`, so a single-line token ends at `At.Col + Text.Length` | none |
| "no position" | `Line == 0 && Col == 0`, with `File` set to the project name (`new Span(input.ProjectName, 0, 0)`)[^compiler] | `default(Span)` on a limit error: `File == null`, line 0[^assembler-core] |

LSP's default position encoding is UTF-16 with 0-based lines and characters, so the mapping for kC is
`line = Line - 1`, `character = Col - 1`, with no re-encoding, provided the server holds the buffer as
a .NET `string`.

Positions are those of the token the parser was looking at. Three preprocessor rules change what
that means:

- **Macro expansions report at the invocation.** Every token produced by expanding an object-like or
  function-like macro, including the substituted arguments, is rewritten `with { At = t.At }` where
  `t` is the macro-name token. A diagnostic "inside" a macro body or its arguments therefore points
  at the macro name in the file that used it.[^preprocessor-expand]
- **`#pragma kfs` reports at the `#`.** The directive is turned into `__kfs_pragma__ … @` tokens all
  carrying the position of the line's `#` token, so every pragma diagnostic has the column of `#`.[^preprocessor-pragma]
- **`#line` rewrites both parts.** `#line N "name"` sets a line offset and replaces the file name for
  the rest of that file (`Reloc`), so `Span.File` can be any string a source file chose.[^preprocessor-line]

## File names in spans

`Diagnostic.File` (and every `Span.File`) is a name, never a path. The values that occur:

| `File` value | meaning | text available from |
| --- | --- | --- |
| a key of `CompileInput.Files` | project file. For a folder project the bare file name; for a root-level file the workspace-relative name[^workspace-project] | the editor buffer or disk: `Workspace.Resolve(project.WorkspacePath(name))` |
| the name written in `#include "name"` / `.include "name"` | an included project file (the include text is used verbatim as the name)[^preprocessor-include][^assembler-include] | same |
| `<kfs.h>`, `<math.h>`, … | a system header | `SystemHeaders.Get("kfs.h")` (LF-normalised) or `SystemHeaders.Files["kfs.h"]`[^system-headers] |
| `<vehicle.h>` | the generated layout header | `VehicleHeader.Generate(layout, host: false, tables)`[^vehicle-header] |
| `<math.c>`, `<vecmat.c>`, `<string.kasm>` | embedded library source, compiled on demand[^kc-front-end-lower] | `Library.Get("math.c")` (name without the brackets)[^system-headers] |
| `X.kasm.h` | header generated from `X.kasm`'s `.proto` lines; lives only in a local dictionary during the compile[^kc-front-end-kasm-h] | rebuild it (see "Include resolution") |
| `<predefined>` | a predefined macro (`Preprocessor.Define`)[^preprocessor-define] | none |
| the project name, line 0 | a whole-program diagnostic (`KC-E012` no `.c` file, `KC-E401` no task, `KC-E900` timeout or validator)[^compiler][^kc-front-end-lower] | none |
| the `file` argument, line 0 | `BuildText`'s own `kfs` and `K?-E900` diagnostics[^workbench-build-text] | none |
| `null` | a kASM limit error (`LimitException` caught in `Assemble`)[^assembler-core] | none |

`IrModule.Files` lists every name the build read, and `IrModule.FileLanguages` classifies them as
`DebugLang.KC`, `DebugLang.KASM` or `DebugLang.Generated` (system headers).[^kc-front-end-lower][^ir]

**The on-disk `vehicle.h` is three lines longer than `<vehicle.h>`.** `GeneratedHeaders.VehicleH`
prepends a three-line `/* … */` banner to `VehicleHeader.Generate`'s text before
`GeneratedHeaders.Write` saves it. Spans in `<vehicle.h>` count lines of the un-bannered text;
`GeneratedHeaders.HeaderBody(written)` strips the banner again.[^generated-headers]

## Projects and the unsaved-buffer overlay

```csharp
public sealed record BuildProject(string Folder, string Name,
                                  IReadOnlyDictionary<string, string> Files, bool IsKc);
public BuildProject? Workspace.ProjectOf(string file, string? openFile, string? openText, out string? why);
```

`ProjectOf` builds the file set from disk; `file` is workspace-relative, `\` is normalised to `/`.[^workspace-project]

| case | `Files` | `Folder` / `Name` / `IsKc` |
| --- | --- | --- |
| `file` is inside a folder (`proj/main.c`, any depth) | every file directly in that folder with extension `.kasm`, `.kinc`, `.c` or `.h` (case-insensitive), in ordinal order, **except** `vehicle.h` and `vehicle.kinc`; keyed by bare file name | the folder path / its last segment / true when any key ends `.c` |
| root-level `.c` | the file, plus a breadth-first walk of `#include "x"` lines (regex `^\s*#\s*include\s*"([^"]+)"\s*`): `"X.kasm.h"` adds `X.kasm` when it exists; any other name without `/` that exists is added and scanned in turn | `""` / file name without extension / true |
| root-level `.kasm` | the file only | `""` / file name without extension / false |
| root-level `.h` or `.kinc` | none: returns `null` with `why` = `"<file> is a header: build a .c or .kasm file that includes it (a subfolder builds as one project)"` | — |

The overlay is one file: the local function `Text(wsPath)` returns `openText` when
`openFile.Replace('\\', '/')` equals `wsPath` under `StringComparison.Ordinal`, and reads the disk
otherwise. Consequences for an LSP:

- Only **one** unsaved buffer can be passed. With several dirty buffers in one project, build the
  dictionary yourself (start from `ProjectOf(file, null, null, out _)` and replace entries) and call
  `Compiler.Compile` directly; `BuildProject` has a public constructor if an `AssembleOutcome`-shaped
  result is not needed.
- The comparison is case-sensitive and the candidate path is built from the on-disk file name, so
  `openFile` must match the disk casing exactly or the overlay is silently skipped.
- In a folder project a buffer that has never been saved is not enumerated, so it is not built. At
  the root, `Text(file)` returns the overlay without touching the disk.
- For a root-level `.c`, `#include "vehicle.h"` is added from disk when a root `vehicle.h` exists
  (the walk does not exclude it), so that build compiles the on-disk copy; `#include <vehicle.h>`
  always uses the generated text.
- Pure-kASM builds resolve `.include "x.kinc"` through `Workspace.ProjectFile` at assemble time, so
  an unsaved `.kinc` buffer is ignored on that path. To overlay it, construct the `Assembler`
  yourself with a `ProjectFiles` callback that consults the open buffers first.[^workbench-build-text]
- `Workspace.Read` throws `IOException` for a source over `MAX_SOURCE_BYTES` (4194304).[^workspace-project]

## Include resolution

| form | lookup order |
| --- | --- |
| kC `#include "name"` | `CompileInput.Files[name]`, then the generated `name` (`X.kasm.h`), then the system lookup below[^preprocessor-include][^kc-front-end-lower] |
| kC `#include <name>` | `CompileInput.SystemFiles[name]`, then `vehicle.h` → `VehicleHeader.Generate(input.Layout, false, input.Game)`, then `SystemHeaders.Get(name)`[^kc-front-end-lower] |
| kASM `.include <name>` | only `kfs.kinc` and `vehicle.kinc` (`BuiltinIncludes.Names`). They are not text: `new BuiltinIncludes(tables, layout, name)` yields `Types` and `Constants`[^assembler-include][^builtin-includes] |
| kASM `.include "name"` | the `Assembler.ProjectFiles` callback (`CompileInput.Files` inside a kC project; disk in `BuildText`)[^assembler-include][^kc-front-end-kasm-h] |

Lookups are exact, ordinal dictionary hits on the text between the quotes: no directory search, no
case folding, no `..` handling. Limits: nesting 32, 1000 includes per unit.[^limits]

Mapping a name back to a URI: `Workspace.Resolve(project.WorkspacePath(name))` for project files.
The virtual files need a custom URI scheme served by the LSP (`workspace/textDocumentContent` or a
client-side buffer). The `X.kasm.h` text is not retained, but it is fully determined by the module:
`"/* Generated from <path>'s .proto lines (kASM §8.1, P-104). */\n#pragma once\n#include <stdint.h>\n#include <kfs.h>\n"`
followed by `Proto.TrimEnd().TrimEnd(';') + ";\n"` for each `IrFunction` of the module with
`At.File == path` and `Proto != null`.[^kc-front-end-kasm-h]

## Vehicle dependence: `PartDescription` and `IoLayout`

Every build needs a part (costs, limits, core class) and a layout (`<vehicle.h>`, message sizes, the
command block). The same source can be clean for one vehicle and wrong for another.

```csharp
public sealed record IoLayout(string Name, uint Fingerprint, int SensorSize, int CommandSize,
                              IoField[] SensorFields, IoField[] CommandFields);
public sealed record IoField(string Name, string Type, int Offset, int Size, bool OneShot);
public static IoLayout LayoutGenerator.Vehicle(string name, int thrusters, int engines, int gimbals);
public static IoLayout GameTables.LoadLayout(string path);              // layout JSON → IoLayout
public static IoLayout GameTables.LayoutFrom(LayoutFile l, string what); // validates + fingerprint check
public static LayoutFile GameTables.ToLayoutFile(IoLayout layout, string source);
public static string GameTables.LayoutJson(LayoutFile file);
```

- In the game, the stock editor captures `workbench.Target.Part` and
  `workbench._vehicle?.Layout ?? tables.BenchLayout` on the main thread.[^workbench-ctor] The public
  equivalents are `Workbench.Target.Part` and `Workbench.TargetLayout`. Both getters go through the
  private `VehicleTarget` property, which clears a dead vehicle and prints, so read them on the main
  thread.[^workbench-target]
- A vehicle layout is a pure function of three counts. `LayoutGenerator.Vehicle` emits
  `thruster[i].pulse_s` (float32, one-shot), `engine[j].throttle`/`.lease_s`, `gimbal[k].y`/`.z`, no
  sensor fields, and a fingerprint that is the CRC-32 of the canonical text. The game calls it with
  the vehicle id and the controller counts (`KfsControllerSet.cs:71`).[^layout]
- The bench layout is different in kind: it comes from the embedded `bench_layout.json`, has sensor
  fields, and is `tables.BenchLayout` (fingerprint `0x070A6447` in this build); the default part is
  `tables.BenchPart`, the catalogue parts are `tables.Parts`.[^game-tables]
- `IoLayout.Name` is not part of the fingerprint or the header text.

**Reconstructing the last-built vehicle out of game.** The workbench writes `vehicle.h` and
`vehicle.kinc` into the project folder (or the workspace root for a root-level file) on
`Workbench.Build`, `Workbench.Headers` and upload — not on the editor's Save or Build button, which
go through `BuildText`.[^workbench-build-effects] The file starts with a banner and three defines:[^generated-headers][^vehicle-header]

```text
/* vehicle.h: generated by kFS for <target> (<part>), layout 0x<Fingerprint:X8>, messages.json version <n>.
 * Generated and read-only: …
 * It is the compiler's built-in <vehicle.h> for this layout … */
…
#define KFS_LAYOUT_FINGERPRINT 0x<Fingerprint:X8>u
#define THRUSTER_COUNT <n>
#define ENGINE_COUNT <n>
#define GIMBAL_COUNT <n>
```

`<target>` is `bench` or `vehicle <id>`; `<part>` is `PartDescription.Name`. `Inferred:` a tool can
therefore recover the build context with no game: parse the part name from the banner and look it up
in `tables.Parts`; if the fingerprint equals `tables.BenchLayout.Fingerprint` use the bench layout,
otherwise call `LayoutGenerator.Vehicle(name, THRUSTER_COUNT, ENGINE_COUNT, GIMBAL_COUNT)` and check
that its `Fingerprint` equals the define. This rests on `Vehicle` being a pure function of the counts
and on the count defines coming from `MessageLayout.GroupCount`; it was not executed. A layout JSON
written with `GameTables.LayoutJson(GameTables.ToLayoutFile(layout, source))` and read back with
`LoadLayout` is the lossless alternative (`LayoutFrom` rejects a fingerprint that does not match the
canonical text).[^game-tables]

# Threading and shared state

## Static mutable members reachable from a compile

| member | kind | protection | concurrent compiles |
| --- | --- | --- | --- |
| `Parser._bindings` / `_bindingsFor` (private static) | `Dictionary<string, ServiceInfo>?` plus the `GameTables` it was built from; rebuilt when the tables *reference* differs | **none**: two fields, checked and assigned without a lock, `_bindingsFor` assigned before `_bindings`[^parser-bindings] | see verdict |
| `Library._files` (private static) | lazily built list of the three embedded library sources | none; `_files ?? (_files = …)` | benign (worst case built twice)[^system-headers] |
| `VehicleHeader._generated` (private static) | `ConditionalWeakTable<IoLayout, GeneratedText>` | `lock` on the entry around read and write | safe[^vehicle-header] |
| `GeneratedHeaders._texts` (private static) | `ConditionalWeakTable<IoLayout, ConcurrentDictionary<…>>` | concurrent collection | safe; not used by the compiler, only by header writing[^generated-headers] |
| `MessageLayout._elements` (private static) | `ConcurrentDictionary<(Group, Spec, WithLeaves), …>` | concurrent collection | safe[^message-layout] |
| `CType._comparing` (private static) | recursion guard of `CType.Compatible` | `[ThreadStatic]` | safe[^kc-ast-ctype] |
| `CType.StructDef._next` (private static) | struct id counter | `Interlocked.Increment` | safe[^kc-ast-ctype] |
| `BusMonitorView._enumNames` (private static) | enum-name cache used by views | `lock` | safe[^global-leaves] |
| `Compiler.KcFrontEnd` (public static property) | `Func<IFrontEnd>?`, the front-end factory | none; assigned by the `[ModuleInitializer]` `KcFrontEnd.Register()` and again by every `new Workbench(…)` | read once per compile[^compiler][^workbench-ctor] |
| `SystemHeaders.Files`, `.Builtins`, `.Deferred`; `Library.KasmFunctions`; `BuiltinIncludes.Scalars`, `.Names`; `MessageLayout.HostPublished`, `.SequencerForbidden` (public static readonly) | plain `Dictionary` / `HashSet` / arrays | none | safe **only while nobody writes**[^system-headers][^builtin-includes] |
| the `GameTables` instance | lists and dictionaries with public getters | none | same: read-only during a compile |

Everything else a compile touches is per-compile instance state: `DiagnosticBag`, `IrModule`,
`KcFrontEnd`, `Sema`, `Preprocessor`, `Parser`, `Assembler`, `Emitter`, `ProgramAnalysis`,
`BusAnalysis`. No static fields were found in `KSA.Kfs.Compiler.Backend`, `KSA.Kfs.Compiler.Builder`,
`KSA.Kfs.Vm.Image` or `KSA.Kfs.Vm.Debug` beyond constant tables.

## Verdict

- **Two compiles against the same `GameTables` instance may run concurrently.** The game already does
  it: the editor builds on a thread-pool thread while the console can build on the main thread.
  `Inferred:` the one unprotected cache, `Parser._bindings`, is harmless in that case because both
  writers publish equal, fully built dictionaries.
- **Compiles against two different `GameTables` instances must be serialised.** A thread can observe
  `_bindingsFor` already switched to its own tables while `_bindings` still holds the other tables'
  dictionary, and bind service calls to the wrong rows. This matters for a test process that mixes
  stock and augmented tables; run those fixtures sequentially.
- **Mutating the shared statics is a stop-the-world operation.** Adding a header to
  `SystemHeaders.Files`, a name to `SystemHeaders.Builtins` or a row to the tables (see
  [adding a service](adding-a-service.md)) must happen before any compile can be running, including
  the stock editor's background build; a `Dictionary` is not safe for a concurrent reader and writer.
  `Parser._bindings` also stays stale for a tables instance that is edited after its first compile.
- **`Workbench` itself is main-thread only**, apart from `BuildText`. `Target`, `TargetLayout`,
  `TargetName`, `Editor`, `RunningSnapshotOf`, `Def`, `Lines`, `Report`, `Manual`, `SourceLines` and
  `OpenSource` read or write unsynchronised fields and print. The `KfsIntegration.Workbench` getter
  lazily creates the instance without a lock and assigns `Out` on every read.[^kfs-integration]
- **Stack depth is part of the language.** The parser calls `TranslationLimits.EnsureStack`, which
  throws `LimitException(720, "nesting too deep for the compiler's stack")` when
  `RuntimeHelpers.TryEnsureSufficientExecutionStack()` fails. A front end run on an ordinary thread
  can report `KC-E720` where the game's 16 MiB thread would not. Use
  `Compiler.COMPILE_THREAD_STACK_BYTES` for any thread that runs the front end.[^limits][^compiler]
- **There is no cancellation.** No API takes a `CancellationToken`. On timeout `Compiler.Compile`
  returns a `KC-E900` diagnostic and abandons the thread, which keeps running to completion.

## The stock editor's background-build model

This is the model to copy:[^editor-model][^editor-window][^kfs-integration]

1. **Trigger.** The editor does not build on keystrokes. `EditorModel.Assemble()` runs on Save
   (Ctrl+S, File → Save: `model.Save()` writes the file, then assembles), on the toolbar Build button
   (current text, saved or not) and inside `Workbench.Upload`. There is no debounce timer.
2. **Capture on the main thread.** `Assemble()` calls `_prepare(file, Text)`, which reads the part
   and layout immediately and returns a closure over `BuildText(file, text, part, layout)`.
3. **Run on the pool.** `_pending = Task.Run(function)`; `_pendingFile` and `_pendingTextVersion`
   remember what was built. `KfsEditorWindow` sets `Editor.Background = true`; without it the build
   is synchronous.
4. **No cancellation.** A second `Assemble()` overwrites `_pending`; the older task finishes and its
   result is never looked at.
5. **Apply on the main thread.** `Workbench.Frame` (called from an ImGui static window each frame)
   calls `Editor.Poll()`: a completed task whose file is still the open file is applied
   (`Outcome`, `Diagnostics`, `ListingLines`, `LineCosts`, `Definitions` on success, `Status`), and
   `OutcomeTextVersion = _pendingTextVersion`. A faulted task becomes the status text
   `"build failed: <message>"`.
6. **Staleness check.** Consumers compare `OutcomeTextVersion` with `TextVersion`;
   `EditorModel.BuildOf(textVersion, part, layout)` additionally requires `Ok`, the same part name,
   the same `IoLayout` reference, unchanged `SourceStamps` and `Workspace.HoldsSourcesOf(project)`.

For an LSP the same shape gives: one worker with a 16 MiB stack, latest-request-wins coalescing per
project, a debounce of its own choosing, `(uri, version)` captured with the request, and results
dropped when the document version moved on.

# Diagnostics

**Free.** `CompileResult.Diagnostics`, `AssembleResult.Diagnostics` and `AssembleOutcome.Diagnostics`
are the complete list for the whole project, in emission order, errors and warnings interleaved.

- Two severities only; no notes or informational entries. Warnings are produced, for example
  `KC-W006` (never used), `KC-W010` (unknown pragma), `KC-W017` (`#warning`), `KC-W203`…`KC-W208`,
  `KC-W307`, `KC-W711`, `KA-W260`, `KA-W546`, `KA-W711`.[^checks-warnings]
- Code format `"{prefix}-E{number:000}"` / `"{prefix}-W{number:000}"`; `BuildText` adds the bare code
  `"kfs"`.[^diagnostic-bag][^workbench-build-text]
- `DiagnosticBag.MAX_ERRORS = 100`. The 100th error is followed by one more error with the same code
  and position and the message `"too many errors; stopping"`; later errors are dropped, warnings are
  never capped.[^diagnostic-bag]
- The parser recovers: a `ParseError` is caught per external declaration and per statement
  (`Resync`), so one build reports many errors.[^parser-recovery]
- `Hint` is a separate string. `Diagnostic.ToString()` prints it as a second line `"  hint: …"`.[^diagnostic]

**Mapping recipe.**

| LSP field | value |
| --- | --- |
| `uri` | resolve `File` with the table in "File names in spans"; group diagnostics per file and publish an empty list for every project file that has none |
| `range.start` | `(Line - 1, Col - 1)`; for `Line == 0` or `File == null` use `(0, 0)` of the file that was built |
| `range.end` | not provided. kC: lex the file with `new Lexer(name, text).Lex()` and take the token whose `At` equals the position: end = `Col - 1 + Text.Length`; if that token is the `#` of a directive, extend to the end of the line. Fallback: `Definitions.WordAt(text, offset)` or one character. kASM (`Col` is 1): the trimmed line[^lexer-api][^definitions] |
| `severity` | `Severity.Error` → 1, `Severity.Warning` → 2 |
| `code` | `Diagnostic.Code` (`KC-E003`) |
| `source` | a constant such as `kfs`; the language is the code prefix (`KC`, `KA`) |
| `message` | `Message`, with `Hint` appended on its own line |
| `relatedInformation` | not provided as data. Several hints end in a location written as `"… at {File}:{Line}."` (first definition, other declaration, other pragma); `SourceLink.Parse` splits `file:line` text[^definitions] |
| `tags` | `Unnecessary` for `K?-W006` is a reasonable choice |
| `data` | keep `Hint`: hints of the form `"Did you mean 'x'?"` come from `Parser.SimilarName` / `Sema.SimilarField` and can drive a quick-fix[^parser-similar] |

`Lexer.Lex()` throws `LexException` (public; `Number`, `At`) at the first unterminated comment or
literal, stray character or wide literal; catch it and fall back.[^lexer-api]

**Constraints.** A diagnostic in an included project header carries the header's name, so one build
of `main.c` yields diagnostics for several URIs. Diagnostics positioned in `<…>` files have no
workspace URI. The result depends on the part and layout used.

# Diagnostic manual

`DiagnosticManual.Page(GameTables tables, string code, string? hint = null)` returns the only
long-form text there is:[^diagnostic-manual]

```text
KC-E003: Unknown symbol or undeclared identifier
severity E (default E); range 0xx; languages KC, KS, KA
hint: <hint, when one was passed>
```

It takes the three digits after `"-E"`/`"-W"` and looks them up in `tables.DiagnosticsData.Rules`, a
list of `DiagnosticRow(string Number, string Rule, string Range, string Severity, string? Default,
List<string> Languages)` loaded from the embedded `diagnostics.json`. An unknown number yields
`"<code>: not in the diagnostics registry"`. There is no per-code explanation, example or URL beyond
the one-line `Rule`; see the [diagnostics table](/tables/diagnostics-table.md). `Workbench.Manual(code)`
prints the same page to `Out`, looking the hint up in the editor's and the last build's diagnostics.[^workbench-views]

For LSP: use the page as hover text on a diagnostic, or serve it from a custom scheme and point
`codeDescription.href` at it. Pure table lookup, any thread.

# Completion and hover: table lookups

All of the following are plain reads of a `GameTables` instance (`KfsIntegration.Tables` in game,
`GameTables.LoadEmbedded()` outside) or of public statics. None needs a build.

| what | read | fields |
| --- | --- | --- |
| kC service functions | `tables.ServicesData.Services` (`List<ServiceRow>`); the kC name is the `Kc` column with its surrounding backticks trimmed, up to the first `(`, for rows with `!Reserved && Kc != null`[^parser-bindings] | `ServiceRow(int Id, string Name, string Args, bool Reserved, string? Kc, string? Kscript, string? Symbol, string? Kind, List<string>? ArgTypes, string? Results, List<string>? ResultTypes, string? CostClass, bool PerValue, string? RequiresStock, List<string>? ArgDomains, string? HostChecks, string? Summary)`[^table-rows] |
| service details by id | `tables.Services[(ushort)id]` | `ServiceInfo(ushort Id, string Name, string Kind, string[] ArgTypes, int ArgCells, int ResultCells, string CostClass, bool PerValue, string? RequiresStock, string[]? Domains, int[]? ArgumentOf, string[]? ArgumentNames, string Summary)` |
| service cost in cycles | `tables.CostsData.ServiceClasses` matched on `Class == CostClass` | `ServiceClassRow(Class, K8, K32, K32F, K32D)`; pick the column for `part.Costs.Core` |
| C prototypes of services and library functions | the text of `SystemHeaders.Files` (`kfs.h`, `math.h`, `vecmat.h`, `string.h`, `stdlib.h`, `stdio.h`, `stdint.h`, `stdbool.h`, `stddef.h`, `limits.h`, `float.h`, `assert.h`)[^system-headers] | one prototype per line, with parameter names |
| which prototypes are compiler builtins | `SystemHeaders.Builtins` | names |
| `KFS_*`, `DEG2RAD`, `M_PI`, … macros | `#define` lines of the same header texts | name and replacement text |
| catalogue enum constants with values | `CatalogueEnums.All(tables)` | `(string Name, int Value)` — `KFS_ATT_MODE_*`, `KFS_ROLL_*`, `KFS_BURN_*`, direction, profile, apsis and burn-end names |
| events | `tables.EventsData.Events` | `EventRow(int Id, string Name, string Parameter, string ParamType, string Keyword)` |
| `sys.*` | `tables.SysBlockData.Fields` | `SysBlockField(int Offset, string Name, string Type, string Meaning)`; the kC struct is `kfs_sys_t` in `kfs.h` |
| message names, ids, docs | `tables.MessagesData.Messages` | `MessageRow(string Id, string Name, string Section, List<MessageFieldRow> Fields, bool PerVehicle)`, `MessageFieldRow(List<string> Names, string Type, string Meaning)` (units and enum values are prose inside `Meaning`) |
| message struct layout for a vehicle | `MessageLayout.All(tables, layout)`, `MessageLayout.Find(tables, layout, name)` | `Id`, `Name`, `TypeName` (`kfs_msg_*_t`), `Size`, `PerVehicle`, `HasDouble`, `Members`, `Leaves` (`MessageLeaf(Path, Type, Offset)`), `ElementTypes`[^message-layout] |
| a ready-made message description | `BusMonitorView.Definition(tables, layout, nameOrId)` | `List<string>?` of display lines, as the editor's bus markers show[^global-leaves] |
| layout fields | `layout.CommandFields`, `layout.SensorFields` | `IoField`; counts via `MessageLayout.GroupCount(layout, "thruster" \| "engine" \| "gimbal")` |
| the generated C text | `VehicleHeader.Generate(layout, false, tables)` | `kfs_commands_t`, `kfs_sensors_t`, every `kfs_msg_*_t`, `*_COUNT`, `KFS_LAYOUT_FINGERPRINT` |
| kASM mnemonics | `tables.OpcodesByMnemonic` (`Dictionary<string, OpcodeInfo>`), `tables.Opcodes[code]` | `OpcodeInfo(byte Code, string Mnemonic, ImmType[] Imm, string[] ImmNames, int Size, int CategoryIndex, string Category, int? Pops, int? Pushes, string Section, string Summary)` |
| stack-effect prose | `tables.OpcodesData.Rows` | `OpcodeRow(Section, Ops, Stack, Pops, Pushes, Category, Imm, OpStyle, HexStyle, Summary)` |
| per-part opcode cost | `part.Costs.Cycles[tables.Categories.IndexOf(info.Category)]`[^kc-front-end-lower] | cycles for the category |
| kASM symbols from `<kfs.kinc>` / `<vehicle.kinc>` | `new BuiltinIncludes(tables, layout, "kfs.kinc")` | `.Constants` (`SVC_*` from `ServiceRow.Symbol`, `sys`, `sys.<field>`, `TRAP_<name>`, catalogue enums; for `vehicle.kinc`: `sensors`, `commands`, `sensors.<f>`, `commands.<f>`, `*_COUNT`, `MSG_*`) and `.Types` (`AsmType(Name, Size, Align, Fields)`)[^builtin-includes] |
| kASM scalar type names | `BuiltinIncludes.Scalars` | 13 names with sizes |
| traps | `tables.TrapsData.Traps` | `TrapRow(int Code, string Name, string Cause)` |
| tick rates | `tables.AllowedTickHz`, `tables.CatalogueData.DefaultTickHz` | integers |
| parts | `tables.Parts`, `tables.BenchPart` | `PartDescription(Name, PartIndex, CoreClass, Costs, ClockHz, RamBytes, CodeBytes, NvBytes, InitBudgetCycles, MaxOperandCells, MaxReturnEntries, MaxTasks, Features)` |

The stock reference window builds its Opcodes and Services pages from exactly `OpcodesData.Rows`,
`OpcodeInfo.Summary` and `ServicesData.Services`.

## `#pragma kfs` grammar

Parsed in `Parser.Pragma()` from the tokens after `kfs`; numbers go through `Lexer.ParseInt`.[^parser-pragma]

| directive | arguments | notes |
| --- | --- | --- |
| `tick_hz` | `N` | once per program; must be in `tables.AllowedTickHz` (`KC-E406`) |
| `task` | `NAME` then `key=number` options: `rate_hz=N` or `divisor=N` (one is required, `KC-E407`), `priority=N` (0–255) | `NAME` must be declared in the same unit and defined in the program as `void NAME(void)` (`KC-E401`); `rate_hz` must divide the tick rate (`KC-E407`) |
| `init` | `NAME`, same option syntax | at most one in the program (`KC-E410`) |
| `optimize` | `0` or `1` | per translation unit |
| `bounds` | `on` or `off` | per translation unit |
| `publish` | `MSG_ID buffer` | `buffer` must be a file-scope object of the message's `kfs_msg_*_t` type |
| `subscribe` | `MSG_ID buffer` | same |
| `claim` | `ALLOCATION` | the only claim; rejected on the Sequencer (`KC-E520`) |
| `strict` | — | `Not active in this build:` reported as "not implemented until MS13" (`KC-E000`) |
| anything else | — | `KC-W010` unknown pragma |

The preprocessor itself handles `#pragma once`; any other non-`kfs` pragma is `KC-W010`.[^preprocessor-pragma]
`_Pragma("…")` is accepted as the operator form. Predefined macros: `__KFS__`, `__KC__`,
`__KFS_TIER__`, `__KFS_CORE__`, `__STDC__`, `__STDC_VERSION__`, `__STDC_HOSTED__`, `__DATE__`,
`__TIME__`, plus `CompileOptions.Defines`.[^kc-front-end-lower]

## kASM directives

The names the assembler switches on: `.include`, `.macro`, `.endm`, `.equ`, `.string`, `.format`,
`.global`, `.data`, `.const`, `.enddata`, `.endconst`, `.byte`, `.half`, `.word`, `.long`, `.float`,
`.double`, `.zero`, `.proto`, `.extern`, `.func`, `.endfunc`, `.args`, `.args64`, `.local`,
`.local64`, `.loc`, `.scope`, `.endscope`, `.loop_bound`, `.tick_hz`, `.program`, `.init`, `.task`,
`.publish`, `.subscribe`, `.claim`, `.strict`, `.bounds`. `.lang`, `.loop` and `.endloop` are
rejected in player source as IR-only (`KA-E002`).[^assembler-directives] Argument syntax is in
[kASM assembly language](/compiler/kasm-assembly-language.md).

**Needs new code.** Context detection (am I after `#pragma kfs`, inside `commands.`, after a
mnemonic?) and the kC keyword list: keywords are lexed as `Ident`, and the set is the private static
`Parser.Keywords`.[^parser-keywords]

# Symbols: definition, references, hover types, signature help, document symbols

## What a successful or failed build leaves behind

| source | position-bearing content | limits |
| --- | --- | --- |
| `IrModule.Functions` (`IrFunction`) | `Name`, `At` (file, line, col of the name), `EntryAt` (the pragma), `Entry`, `RateDivisor`, `Priority`, `Language`, `Static`, `Proto` (kASM), `Slots` (`IrSlot(Name, Offset, Size, Align, Argument, TypeName, Type, Temporary)` — parameters and locals, **no positions**), `Body` items each with `At` (`IrInstr`, `IrLabel`, `IrLoc(At, File, Line, Col)`, `IrScope(At, Names)`, `IrLoop`), `BusWrites`, `BusMarks`, `CommandWrites`[^ir] | only functions with bodies; unreachable library functions are pruned; static functions that collide are renamed `name.N`[^kc-front-end-lower] |
| `IrModule.Globals` (`IrGlobal`) | `Name`, `At`, `Size`, `Type` (`IrType`), `DebugName`, `Const`, `Generated` | file-scope objects with storage only; string literals appear as generated `.strN` |
| `IrModule.Bus`, `.TickHzAt`, `.Strings` | `IrBusDecl(Publish, MessageId, Message, Buffer, At, Prefix)`; `IrString(Name, Kind, Text, At)` | — |
| `ProgramAnalysis` (`CompileResult.Analysis`) | `Functions[name].CallSites` (`(string Callee, int HeightBefore, Span At, bool Far)`), `Tasks` (`TaskFigures`) | present only when the emitter got that far[^emitter] |
| `CompileReport.BusAccesses` | `BusAccess(File, Line, Col, EndCol, MessageId, Message, Buffer, Write)` — the one record with an end column (`Col + buffer name length`)[^analysis] | successful emits only |
| `KvmImage.Debug` (`DebugInfo`) | `Functions` (`File`, `Line`, pc range, `FirstLocal`/`LocalCount`, `Type`), `Lines` (`DebugLine(Pc, File, Line, Col)`), `Globals` (`Name`, `Type`, `Address`, `File` — **no line**), `Locals` (`Name`, `Type`, `FrameOffset`, `ScopeStart`, `ScopeEnd` as pcs, `Kind`), `Types`, `Fields`[^debug-info] | needs an image: `ImageReader.Read(bytes, part, tables, layout, out KvmImage? image)`[^image] |
| `AssembleOutcome.Listing` | `ListingLine(uint Pc, int Cost, string Function, string Text, string? File, int Line)` per instruction | successful builds only |
| `KcFrontEnd.Units` (`List<TranslationUnit>`) | the typed AST, below | not returned by `Compiler.Compile` |

`CompileResult.Module` is returned on failure too, holding whatever was lowered before the error:
kASM functions and file lists always, kC functions only when the front end had no errors.[^compiler]

## The typed AST: `KcFrontEnd.Units`

`KcFrontEnd` is a public sealed class with an implicit public constructor, a public
`bool Lower(CompileInput input, IrModule module, DiagnosticBag diags)` and
`public List<TranslationUnit> Units { get; }`.[^kc-front-end-lower] After `Lower`, each unit holds:[^kc-ast]

```csharp
public sealed class TranslationUnit {
    string File; List<Obj> Globals; List<Obj> Functions; List<Obj> Strings; bool IsLibrary;
    int? TickHz; Span TickHzAt; int Optimize; bool BoundsChecks;
    List<(bool Publish, string Message, string Buffer, Span At)> BusPragmas;
    List<Span> Claims; List<(ushort Id, string Name, Span At)> ServiceCalls;
}
public sealed class Obj {            // a declared entity
    string Name; string LinkName; CType Type; Span At; bool IsLocal, IsParam, IsFunction, IsStatic,
    IsExtern, IsDefinition, HasInit, IsTypedef, IsEnumConstant, IsBuiltin, IsInline, IsKasm;
    long EnumValue; int Uses; Span FirstUse; List<Obj> Params; List<Obj> Locals; Node? Body;
    List<Obj> Callees; Obj? Resolved; EntryKind Entry; Span EntryAt; ushort RateDivisor; int Priority; …
}
public sealed class Node {           // expression or statement
    NK Kind; CType Type; Span At; Node? L, R, Cond, Then, Else, Init, Inc, Body;
    List<Node> Stmts, Args, Cases; Obj? Var; string? Str; long IVal; double FVal; List<Obj> Decls; …
}
```

What that supports, read from the parser and semantic pass:

- **Declarations.** `Obj.At` is the declarator's identifier token, so its range is
  `Name.Length` wide. When a later definition follows a declaration, the recorded `At` moves to the
  definition.[^parser-declare] Every file-scope function and object of the unit is in `Functions` /
  `Globals`, including the prototypes pulled in from `<kfs.h>` and friends (their `At.File` is the
  `<…>` name). Parameters and block locals are in `Obj.Params` and `Obj.Locals`.
- **References.** An identifier in an expression becomes `Sema.Var(obj, at)`: an `NK.Var` node with
  `Var` = the `Obj` and `At` = the identifier token (an enum constant becomes `NK.Num` but keeps
  `Var`). A call is `NK.Call` with `Var` = callee and `Args`. `Obj.Uses` counts references and
  `Obj.FirstUse` is the first one.[^sema-var] Walking `Obj.Body` therefore yields every surviving
  reference with its target.
- **Cross-file resolution.** `KcFrontEnd.Link` sets `Obj.Resolved` on each declaration to the
  defining `Obj` in another unit; `Resolved ?? obj` is the definition.[^kc-front-end-lower]
- **Types.** Every `Node` has a `CType`; `CType.ToString()` renders canonical C (`unsigned int`,
  `struct tag`, `float *`, `int(float, float)`), not the typedef name that was written. Function
  types carry `Ret`, `Params`, `ParamNames`, `Variadic`, `HasProto`; structs carry
  `Fields` (`StructField(Name, Type, Offset)`).[^kc-ast-ctype]
- **Members.** `NK.Member` has `Str` = member name, `IVal` = offset, `L` = the struct expression, but
  its `At` is the start of the postfix expression, not the member token.[^sema-member]
- **Service calls.** `TranslationUnit.ServiceCalls` lists `(Id, Name, At)` for every bound call.

What it does **not** hold:

- Typedef names, enum constants and struct tags are kept only in the parser's private scope
  dictionaries (`Declare` adds an `Obj` to the unit only if it is neither a typedef nor an enum
  constant), and the `Parser` instances are local to `Lower`. Struct fields have no position.[^parser-declare]
- Macros are a private record in a private dictionary of `Preprocessor`; the only public queries are
  `IsDefined(name)`, `FilesRead` and `KfsPragmas`.[^preprocessor-define]
- No end positions, no comments, no preprocessor-skipped regions.
- `Inferred:` constant folding and unevaluated operands (`sizeof`) drop sub-expressions, so a
  reference search over `Body` can miss identifiers that appear only in folded or unevaluated
  contexts. Identifiers inside a macro invocation all report the macro name's position.[^preprocessor-expand]

## Getting at `Units`

`Compiler.CompileOnThisThread` creates the front end through the static factory and drops it, and
the two-argument `Compile(input, compile)` overload is `internal`.[^compiler] Three ways to keep the
instance, in order of preference:

1. `Inferred:` **Run the front end yourself for symbols.** On a 16 MiB thread:
   `var fe = new KcFrontEnd(); var bag = new DiagnosticBag(); var module = new IrModule { DoubleBits = input.Options.DoubleBits }; fe.Lower(input, module, bag);`
   catching `LimitException`. This yields `fe.Units` and the front-end diagnostics, but not the
   back-end ones (sizes, stacks, WCET, bus checks); run `Compiler.Compile` as well, or replicate
   `CompileOnThisThread`, whose remaining steps use only public members: reorder `module.Globals` by
   file order, `IrValidator.Check(module, tables)`, `Peephole.Run(module, off, out _)`, then
   `new Emitter(tables, part, layout, bag, "KC") { EmitDebug, DoubleBits, PeepholeOff }.Emit(module)`.
2. `Unverified:` **Wrap the factory.** Assign `Compiler.KcFrontEnd` a factory that creates a
   `KcFrontEnd`, stores it in a `[ThreadStatic]` slot and returns it, and call
   `CompileOnThisThread` on your own thread. This is a process-wide change, and every
   `new Workbench(…)` calls `KcFrontEnd.Register()`, which puts the stock factory back.[^workbench-ctor]
3. `Unverified:` a Harmony postfix on `KcFrontEnd.Lower` capturing `__instance`.

Partial results survive errors: a unit is appended to `Units` after `ParseUnit()` whatever it
reported, and `Lower` returns early only when the bag is full.[^kc-front-end-lower]

## `Definition`, `Definitions` and `Workbench.Def`

```csharp
public sealed record Definition(string Name, string Kind, string File, int Line);
public static List<Definition> Definitions.Of(IrModule? module);
public static Definition? Definitions.Find(IReadOnlyList<Definition> defs, string name);
public static string? Definitions.WordAt(string text, int offset);
public Definition? Workbench.Def(string? name);
public Definition? EditorModel.GoToDefinition(string name);
```

A "definition" here is one entry per `IrModule.Functions` (`Kind = "function"`) and per
`IrModule.Globals` (`Kind = "global"`), with the file and **line** of its `At`; no column.
`Find` matches the name exactly and prefers an entry whose file does not start with `<`.[^definitions]
It does not resolve macros, typedefs, struct fields, enum constants, locals, parameters, message
names or `sys`/`commands` fields, and `sensors`/`commands`/`sys` themselves are absent because
objects placed with `__at` are not lowered to globals.[^kc-front-end-lower]

`Workbench.Def(name)` runs `Find` over `Definitions.Of(CurrentBuild?.Module)` (`CurrentBuild` is
`Editor.Outcome ?? LastBuild`), prints `"{Kind} {Name}: {File}:{Line}"`, and opens the file in the
in-game editor at that line when it is a workspace file — a UI action, main thread only. The
editor's F12 uses `WordAt` at the cursor and `EditorModel.GoToDefinition`, whose list is refreshed
only by successful builds.[^workbench-views][^editor-model][^editor-window]

## kASM symbols

`AssembleResult.Module` is populated even when assembly fails (it is `null` only after a limit
error). It gives `.func` functions with `At` and `Proto`, `.global` objects, `.string`/`.format`
entries (`IrString`), bus and entry declarations, and every label as `IrLabel(At, Name)` in its
function's `Body` — all with line positions and column 1.[^assembler-core][^assembler-pos] `.equ`
constants, macros and `.local` names are private assembler state with no position.

## Feature by feature

| feature | free from the game | new code |
| --- | --- | --- |
| document symbols | functions and globals from `TranslationUnit`/`IrModule` with name ranges; tasks from `Obj.Entry`/`IrFunction.Entry`; kASM functions and labels | symbol end ranges (brace matching over `Lexer` tokens); typedefs, structs, enums, macros, `.equ` |
| go to definition | identifier → `Obj` → `(Resolved ?? obj).At` through the AST, including into `<…>` headers; line-only fallback `Definitions.Find` | position → node index; macros, typedefs, tags, fields; virtual-document provider |
| find references | walk `Obj.Body` for `NK.Var`/`NK.Call` nodes whose `Var` is the target | a light token index for everything the AST drops |
| hover (type of an identifier) | `Node.Type.ToString()`, `Obj.Type`, `CType.SizeNow`, struct `Fields` with offsets | member-token lookup; typedef names |
| signature help | `Obj.Params` (names, types), `CType.ParamNames`, `Variadic`; header prototype text; `ServiceRow.Args`/`Summary` | call-site detection in incomplete code (the AST is not there while the line does not parse) |
| semantic tokens | `Lexer` token kinds and positions; `Obj` flags to classify identifiers | keywords, comments, directives, inactive `#if` regions |
| call hierarchy | `Obj.Callees`; `FunctionAnalysis.CallSites` with spans | — |

# Code lens and inlay hints: costs, budgets, bus markers

```csharp
public sealed record LineCost(string File, int Line, long Cycles, bool PerIteration,
                              List<string> SoftRoutines, int? StackDepth);
public sealed record ReportWcet(string Task, long Cycles, long Budget);
public sealed record ReportMachine(string Name, string Core, long ClockHz, long CyclesPerTick);
```

- `CompileReport.Estimates` (also `CompileResult.Estimates`, `AssembleOutcome.Estimates`) is built by
  `ProgramAnalysis.LineEstimates`: for every function, the entry cost is added on the function's own
  line, then each instruction's cost (from `CostModel.Cost` with the part's cost table) is added to
  the source line it came from. `PerIteration` is set when the line lies between a backward jump and
  its target; `SoftRoutines` names the software routines the line pulls in; `StackDepth` (the operand
  stack height after the line) is filled for kASM functions only.[^analysis]
- **The editor's cycles column** is `LineCostView.Rows(outcome.Estimates, projectRelativeFile,
  LineCostView.CountLines(text))`: an array with one `LineCostRow` per line, matched on the file name
  (exact, or equal `Path.GetFileName`). `LineCostRow.Cell` is the gutter text (`"42"`, `"42/it"`,
  `"7 [2]"`), `LineCostRow.Hover` the tooltip (`"≈42 cycles per iteration, <routines>; operand stack
  2 after this line"`).[^line-cost-view][^editor-model] Both are directly usable as inlay-hint label and
  tooltip.
- **Budgets.** `CompileReport.Machine.CyclesPerTick` is `part.ClockHz / tickHz`.
  `CompileReport.Wcet` has one `ReportWcet` per task whose worst case could be computed;
  `Budget` is `part.InitBudgetCycles` for an `init` task and `ClockHz / tickHz` for a periodic one.
  `ProgramAnalysis.Tasks` (`TaskFigures`: `Entry`, `RamStack`, `Entries`, `Cells`, `DeepestPath`,
  `Wcet`, `Budget`) has the same figures with the `IrFunction`, whose `At`/`EntryAt` place a code
  lens on the task function or its pragma.[^analysis][^emitter]
- **Memory.** `CompileReport.Program` (`Total`, `Limit`), `CompileReport.Ram` (`Total`, `Limit`,
  `Globals`, `BusBuffers`, `TaskStacks`, `VmRegion`); `ReportView.From(report, image, snapshot, …)`
  flattens them into `ReportBar`s and `ReportTaskRow`s, and `ReportView.Lines()` renders the text
  report.[^compile-report][^line-cost-view]
- **Bus markers.** `CompileReport.BusAccesses` gives a column range per read or write of a bus
  buffer; `BusMarkerView.Rows(accesses, file, lineCount)` groups them per line and `BusMarker.Cell`
  renders `"< NAV_STATE"` / `"> WRENCH_CMD"`.[^line-cost-view]

All of this exists **only when the emitter finished**: `EmitResult.Report` is set on the single
success return, and `BuildText` drops the report unless the build is `Ok`.[^emitter][^workbench-build-text] Keep
the last good figures and mark them stale while the buffer has errors. The numbers are per part.

# Commands

Things an LSP can offer through `workspace/executeCommand`. All are main-thread calls on
`KfsIntegration.Workbench` unless noted.[^workbench-views][^workbench-build-effects]

| command | call | effect |
| --- | --- | --- |
| build and report | `Workbench.Build(file)` | writes headers, prints the report, sets `LastBuild`; returns `AssembleOutcome?` |
| preview for another part | `Workbench.BuildText(file, text, tables.Part(name), layout)` (any thread) | none; `Workbench.Preview(partName)` is the UI version and sets `Editor.Preview` |
| upload to the target | `Workbench.Upload()` (acts on the in-game editor's open file), or `Workbench.Load(args)` | `Upload` saves the editor buffer, builds and loads; `Load` builds the named file and loads it |
| start / stop | `Workbench.Start()`, `Workbench.Stop()` | queued for a vehicle |
| write `vehicle.h` / `vehicle.kinc` | `Workbench.WriteHeaders(file)` → `(string Folder, List<string> Written)`; out of game `GeneratedHeaders.Write(folder, tables, layout, target, part)` | writes only files whose bytes changed |
| open in the in-game editor | `Workbench.Edit(file)` then `Workbench.Editor.GoToLine = line`; `Workbench.OpenSource(file, line)` for a build-relative name | raises `OpenRequested`, which shows the editor window |
| cost listing | `Workbench.Lines(file)` | builds quietly and prints one line per costed source line |
| report | `Workbench.Report(file)` | prints `ReportView.Lines()` |
| source of a built file | `Workbench.SourceLines(file)` | `string[]?`, resolved through the loaded or current project |
| manual page | `DiagnosticManual.Page(tables, code, hint)` (any thread) | text |
| disassembly | `Disassembler.Disassemble(image, tables)`, `DisassemblyView.Build(listing, source)`, `Listing.Format(lines)` (any thread) | text / view models |

The in-game editor follows an external editor by itself: `Workbench.Frame` calls
`Editor.CheckExternalChange()` every 0.5 s, which reloads the open file when its timestamp and
content changed on disk and the in-game buffer is not dirty.[^editor-model][^workbench-frame]

# Live data from a running computer

Relevant only to the in-game host. `Workbench.Target` is the `HostComputer` being targeted (the
bench, or the active vehicle's computer).[^workbench-target]

| data | API | safe off the dispatch thread? |
| --- | --- | --- |
| machine state | `HostComputer.Snapshot` → `MachineSnapshot(Name, State, Part, TickHz, K, TimeS, CyclesUsed, Budget, Bytecodes, Overruns, TicksRun, TicksLatched, …, Tasks, Trap, Profile, …, LayoutFingerprint, …)` with `Ram`, `Perf`, `ProfileTick`, `OverrunHistory`, `Nan`, `NanGlobals`, `Bus` | yes: the getter calls `WantSnapshot()` (keeps publication alive for 1000 ms) and does a `Volatile.Read` of the last published record; treat its arrays as read-only[^host-computer] |
| per-task cycles | `MachineSnapshot.Tasks` → `TaskSnapshot(Name, Kind, RateDivisor, CyclesThisTick, CyclesTotal, MaxRamStack, MaxReturnDepth, MaxOperandDepth, Status)` | yes |
| a trap with its source line | `MachineSnapshot.Trap` → `TrapSnapshot(TrapCode Code, uint Pc, int Task, byte UserCode, string? File, int Line, string Function)` | yes; a natural runtime diagnostic |
| profile | `MachineSnapshot.Profile` → `FunctionSnapshot(Name, Self, Total, Calls, Pseudo)` | yes |
| program log | `HostComputer.Console` (`ConsoleLog`): `Snapshot()` → `List<LogLine>`, `CopyTail(count, into)` → version, `Version`; `LogLine(ulong K, double TimeS, string Text)` | yes: every member takes the log's lock[^host-services] |
| `trace()` channels | `HostComputer.Traces` (`TraceStore`): `Channels`, `Samples(channel)`, `CopyLast(channel, count, into)`; `TraceSample(ulong K, float Value)` | yes: locked[^host-services] |
| watched globals | `HostComputer.Watch(path)` / `Unwatch(path)` / `Watched` (locked); the tick path stores `(float)leaf.Read(machine)` in `HostComputer.Watches` under the leaf path | yes[^host-computer] |
| a global's current value | `GlobalLeaves.Leaves(image)` → `List<GlobalLeaf>`; `GlobalLeaf(Path, Global, Address, Kind, Size, File).Read(ReadOnlySpan<byte> ram)` over `MachineSnapshot.Ram` | yes, with a snapshot. `Ram` is `null` unless requested: `HostComputer.WantRam()` (1000 ms window, queues a republish)[^global-leaves][^host-computer] |
| the loaded image | `HostComputer.LoadedBytes` (`Volatile.Read`), `HostComputer.Image` | `LoadedBytes` yes; `Image` and `Leaves` are plain fields swapped at load |
| direct machine reads | `GlobalLeaf.Read(KvmMachine)`, `Workbench.Read(address, count, out why)`, `Workbench.TryResolve(text, out address, out why)` | **no** for a vehicle: they read the live `KvmMachine`; run them inside `HostComputer.Enqueue(Action<HostComputer>)`, which the dispatching thread drains |
| is the editor's build the one running? | `Workbench.RunningSnapshotOf(outcome)` → the target's snapshot when `outcome.Image` equals `Workbench.LoadedImage` byte for byte, else `null` | main thread (caches in a field)[^workbench-views] |

`Workbench.ConsoleLines()` and `Workbench.ConsoleTail(count, into, ref version)` wrap the log calls
but read `Target`, so they are main-thread calls; hold the `HostComputer` reference instead. See
[snapshots and perf](/host/snapshots-and-perf.md) and
[dispatch and threading](/integration/dispatch-and-threading.md).

LSP uses: inlay hints with live values of globals (`Read` over `Ram`), measured task cycles next to
the computed WCET, a diagnostic at `TrapSnapshot.File`/`Line`, a log stream as `window/logMessage`.
Line-level live data must be shown only when the document matches the loaded image; compare the
build's `Image` with `LoadedBytes`.

# LSP feature → data source → effort

| LSP feature | data source | effort |
| --- | --- | --- |
| `publishDiagnostics` (kC, kASM) | `Compiler.Compile` / `Assembler.Assemble` / `Workbench.BuildText` | low: file mapping, `-1` offsets, a token lookup for the range end |
| diagnostic hover / `codeDescription` | `DiagnosticManual.Page` | low; the text is one line per code |
| quick fix "did you mean" | `Diagnostic.Hint` text | low, by pattern-matching the hint |
| completion: services, constants, messages, layout fields, `sys.*`, opcodes, traps, kinc symbols | `GameTables`, `SystemHeaders.Files`, `MessageLayout`, `BuiltinIncludes`, `VehicleHeader.Generate` | low for the data; medium for context detection |
| completion: `#pragma kfs`, kASM directives | fixed lists in this document | low (hard-coded) |
| completion: identifiers in scope | `TranslationUnit` globals/functions, `Obj.Params`/`Locals` from the last good parse | medium |
| hover: services, messages, opcodes, fields | table rows above; `BusMonitorView.Definition` | low |
| hover: type of an expression | `KcFrontEnd.Units` → `Node.Type` | medium: own front-end run plus a position index |
| signature help | `Obj.Params`, header prototypes, `ServiceRow` | medium: needs own call-site scan in unparsable text |
| go to definition (functions, globals, locals, parameters) | AST `Var` → `Obj.At`; `Definitions.Find` as line-only fallback | medium |
| go to definition into virtual headers | `SystemHeaders.Get`, `VehicleHeader.Generate`, `Library.Get`, rebuilt `X.kasm.h` | medium: a virtual-document provider |
| go to definition (macros, typedefs, struct tags, fields, enum constants, `.equ`, kASM macros) | not retained by the game | needs an own lightweight parser |
| find references | AST walk | medium; incomplete for macros and folded expressions |
| document symbols | `TranslationUnit` / `IrModule` / `IrLabel` | low for names; ranges need brace matching |
| semantic tokens | `Lexer` tokens + `Obj` classification | medium: keywords and comments are own code |
| inactive `#if` regions, folding | not exposed | own preprocessor pass |
| code lens / inlay: line cycles | `CompileReport.Estimates`, `LineCostView.Rows` | low |
| code lens: task WCET vs budget, RAM, program size | `CompileReport.Wcet`, `.Ram`, `.Program`, `ProgramAnalysis.Tasks` | low |
| bus markers | `CompileReport.BusAccesses` (has column ranges) | low |
| formatting, rename | nothing | own code; rename is unsafe without full reference data |
| live values, traps, log | `MachineSnapshot`, `ConsoleLog`, `TraceStore`, `GlobalLeaves` | low to medium, in-game only |
| build / upload / open-in-game commands | `Workbench` | low, marshalled to the main thread |

# Modding notes

- **Public surface.** `Compiler`, `CompileInput`, `CompileResult`, `Diagnostic`, `DiagnosticBag`,
  `Span`, `Lexer`, `Token`, `Preprocessor`, `Parser`, `Sema`, `KcFrontEnd`, `TranslationUnit`, `Obj`,
  `Node`, `CType`, `Assembler`, `BuiltinIncludes`, `IrModule` and its records, `Emitter`,
  `ProgramAnalysis`, `CompileReport`, `DebugInfo`, `Workbench`, `Workspace`, `BuildProject`,
  `EditorModel`, `AssembleOutcome` and every type in `KSA.Kfs.Host.Views` named here are `public`.
  The private pieces an LSP would have liked are `Parser.Keywords`, the parser's scope dictionaries,
  `Preprocessor._macros`, the assembler's `_equ`/`_macros`, and the internal
  `Compiler.Compile(input, compile)` overload.
- **Prefer `Compiler.Compile` plus your own project assembly over `Workbench.BuildText` when you
  hold several unsaved buffers**, and prefer `BuildText` when you want exactly what the in-game
  editor would show (it also validates the image and builds the listing).
- **Capture part and layout on the main thread, build elsewhere, apply by version.** Never read
  `Workbench.Target*` from the server thread. Outside the game pass `tables.BenchPart` and
  `tables.BenchLayout`, or the reconstructed vehicle.
- **One `GameTables` instance per process if at all possible**, and finish every mutation of
  `SystemHeaders.Files`, `SystemHeaders.Builtins` and the tables before the first build. In tests,
  do not run fixtures with different tables in parallel (`Parser._bindings`).
- **Run front-end code on a 16 MiB stack** so nesting limits match the game, and treat any exception
  as a build failure to report, not to propagate.
- **Nothing is incremental and nothing is cancellable.** Each request recompiles the whole project,
  including the embedded library sources a unit pulls in; the only reuse is the per-layout
  `<vehicle.h>` text. Coalesce requests.
- **Positions are points.** Budget for a token lookup on every diagnostic, and expect macro and
  pragma diagnostics at the invocation or `#` rather than at the offending token.
- **`<vehicle.h>` line numbers are not the on-disk `vehicle.h` line numbers** (three-line banner).
- **`Compiler.KcFrontEnd` is reset by `new Workbench(…)`.** A mod that wraps the factory has to
  re-check it after the game creates its workbench.
- **Everything here is for build 2026.11.25.5949.** kFS is a prototype; the AST classes in
  particular are compiler internals that happen to be public and can change without notice. A test
  that compiles a fixture and asserts a known diagnostic position, a known `Obj.At` and a known
  `LineCost` will catch a convention change at the next game update.

[^offline-harness]: offline-harness.md — "Step 1" (loading `KSA.dll` outside the game), experiment E8 (`InvalidOperationException` from `Lowering`), E11 (`Workbench.Build` path).
[^compiler]: Compiler.cs — constants :20-22; `KcFrontEnd` factory :24; `Compile(input)` :26-29; internal overload, thread, name, stack, join, timeout diagnostic, rethrow :31-59; `CompileOnThisThread` :61-129 (front-end creation :68; early return with `irModule` :71-74; `LimitException` → diagnostic :76-101; global reorder :102-107; validator, peephole, emitter :108-128).
[^compile-input]: CompileInput.cs:6-21; CompileOptions.cs:6-23; CompileResult.cs:7-14.
[^limits]: TranslationLimits.cs:7-29 (`NESTING` 128, `INCLUDE_DEPTH` 32, `INCLUDES_PER_UNIT` 1000, `TOKENS` 150000, `MACRO_TOKENS` 200000; `EnsureStack` :23-29); Parser.cs:228-235 (`Nest` calls it).
[^assembler-core]: Assembler.cs — single-use guard :118-124; `Assemble` :118-178 (failure returns `_m` :139-142; `ModuleOnly` :143-146; `LimitException` with `Span at = default` and `Module = null` :155-177); `ProjectFiles`, `EmitDebug`, `ModuleOnly`, `ImageMode`, `Diagnostics` :91-109; AssembleResult.cs:8-15; SourceFile.cs:3.
[^assembler-pos]: Assembler.cs — `Preprocess` (`Replace("\r\n", "\n").Split('\n')`, `new Span(path, i + 1, 1)`) :257-307; macro bodies re-split at the invoking line's span :402-435; labels :489-502.
[^assembler-include]: Assembler.cs:309-367 (`<…>` limited to `BuiltinIncludes.Names`; `"…"` through `ProjectFiles`, the included file's span name is the quoted text).
[^assembler-directives]: Assembler.cs — preprocess-level `.macro`/`.endm`/`.include` :269-297; data items :518-524; `IrOnly` :83 and its error :554-557; directive switch :559-878.
[^workbench-build-text]: Workbench.cs — `AssembleText` :478-481; `BuildText` :483-587 (`ProjectOf` and `IOException` :486-496; `"kfs"` diagnostic :497-500; kC branch :505-522; kASM branch and disk `ProjectFiles` :523-539; no-image outcome :541-550; `ImageReader.Read` and `E900` :551-570; success outcome :571-586); `SourceStampsOf` :589-610; `BuiltSourceLines` :629-640.
[^workbench-build-effects]: Workbench.cs — `WriteHeaders` :207-214; `ReportHeaders` :216-262; `Headers` :264-268; `Build` :659-678; `Report(file, outcome, …)` printing :680-737; `LoadBuilt` :918-937; `BuildQuiet` :1848-1857.
[^assemble-outcome]: AssembleOutcome.cs:10-35; ListingLine.cs:3; Listing.cs:16.
[^lexer-pos]: Lexer.cs — `_line = 1`, `_col = 1` :21-23; constructor normalising `\r\n` and `\r` :36-40; `Here()` :244-247; `Advance()` (newline resets the column, everything else adds one) :249-261.
[^lexer-api]: Lexer.cs — `Lex()` :42-220 (comments skipped :77-103; identifiers, numbers, literals, punctuators :113-214; `LexException` throws :97, :171, :180, :194, :219); Token.cs:6-14; TokKind.cs:3-12; LexException.cs:6-18.
[^preprocessor-expand]: Preprocessor.cs — object-like expansion `At = t.At` :928-941; function-like expansion `At = t.At` :1015-1025; `Substitute` :1073-1163.
[^preprocessor-pragma]: Preprocessor.cs — directive position `Reloc(token.At, …)` of the `#` token :387-402; `Pragma` (`once`, `kfs` → `__kfs_pragma__` tokens at `at`, others `KC-W010`) :824-858; `_Pragma` :1028-1035.
[^preprocessor-line]: Preprocessor.cs — `Reloc` :548-555; `#line` :615-638.
[^preprocessor-include]: Preprocessor.cs — `Include` :763-822 (quoted: `_projectFiles(text) ?? _systemFiles(text)` :808; file name `<name>` or `name` :814; `FilesRead` :815-818; not found `KC-E015` :809-813).
[^preprocessor-define]: Preprocessor.cs — private `Macro` record :13; `_macros` :318; `KfsPragmas`, `FilesRead`, `Unavailable` :334-338; constructor :340-346; `Define` (`<predefined>`) :348-353; `IsDefined` :355-358; `Run` :360-366.
[^parser-bindings]: Parser.cs — `_bindings`, `_bindingsFor` :141-143; `Bindings(tables)` :3085-3103.
[^parser-pragma]: Parser.cs:395-564 (`tick_hz` :413-434; `init`/`task` :435-493; `optimize` :494-512; `bounds` :513-531; `publish`/`subscribe` :532-542; `claim` :543-556; `strict` :557-559; default :560-562); KcFrontEnd.cs:529-636 (`Entries`: allowed rates, task signature, single `init`).
[^parser-keywords]: Parser.cs:81-88.
[^parser-recovery]: Parser.cs — `ParseUnit` :320-357; `Resync` :359-393; statement-level catch :2067-2069.
[^parser-declare]: Parser.cs — `Declare` merging a later definition (`value.At = …`) :1106-1110, `o.Resolved = value` :1151, adding to `_unit.Functions`/`_unit.Globals` only for non-typedef, non-enum-constant file-scope names :1155-1167; `Declarator` taking the identifier token's position :1170-1205; private scopes :55-61.
[^parser-similar]: Parser.cs:3542-3546, :3612; Sema.cs:1297-1299, :1318-1321.
[^sema-var]: Sema.cs — `New` :106-119; `Var` :185-209; `Call` (`node.Var = f`) :1397-1431; `RootSpan` :94-104.
[^sema-member]: Sema.cs:1282-1316; Parser.cs:3071-3080.
[^kc-front-end-lower]: KcFrontEnd.cs — `Units` :23; `Register` :25-29; `Lower` :31-202 (`CategoryCycles` :38-42; no `.c` file `KC-E012` at the project name :44-49; module files :54-60; embedded kASM :61-66; `VehicleHeader.Generate` :67; library units named `<…>` and `Library.Get` :72-96; preprocessor lookups :75-79; `FilesRead` → `module.Files`/`FileLanguages` :116-127; `Units.Add` :136-148; unused-symbol warnings :181-195); `Predefine` :392-415; `Link` (`Resolved`) :417-527; no-task `KC-E401` :630-634; `LowerModule` (skips `AtAddress` objects :650-654; static renames :748-767); `PruneLibrary` :804-869.
[^kc-front-end-kasm-h]: KcFrontEnd.cs — `Assemble` with `ProjectFiles = n => input.Files.GetValueOrDefault(n)` :328-336; generated `X.kasm.h` text :376-388; lookup through `generated` :61, :75.
[^kc-ast]: TranslationUnit.cs:6-33; Obj.cs:7-101; Node.cs:6-67; NK.cs:3-68.
[^kc-ast-ctype]: CType.cs — `StructDef` and `Interlocked.Increment(ref _next)` :11-33; `[ThreadStatic] _comparing` :166-167; function and struct members :169-239; `ToString` :782-809; StructField.cs:3.
[^system-headers]: SystemHeaders.cs — `Files` :8-22; `Builtins` :24-30; `Deferred` :32; `Get` :34-41; Library.cs — `_files`, `Files` :13-17; `KasmFunctions` :15; `Get` :28-31; `SourceFor` :38-49.
[^vehicle-header]: VehicleHeader.cs — `_generated` :39; `Generate` with `lock` :41-58; `Build` (`KFS_LAYOUT_FINGERPRINT` :70-72; `*_COUNT` :74-88; blocks, messages, `sensors`/`commands` externs :89-139).
[^generated-headers]: GeneratedHeaders.cs — file names :19-21; `_texts` :23; `BannerLines` :25-44; `VehicleH` :46-49; `Cached` :51-61; `MakeVehicleH` (banner + `VehicleHeader.Generate`) :63-70; `HeaderBody` :72-80; `VehicleKinc` :111-114; `KincTypes` :189-226; `Write` :228-251.
[^builtin-includes]: BuiltinIncludes.cs — `Names` :10; `Scalars` :12-27; `Types`, `Constants` :29-31; `kfs.kinc` contents :35-62; `vehicle.kinc` contents :64-107; AsmType.cs:6-25.
[^ir]: IrModule.cs:7-28; IrFunction.cs:6-57; IrGlobal.cs:5-26; IrBusDecl.cs:3; IrSlot.cs:3; IrItem.cs:3; IrInstr.cs:5; IrLabel.cs:3; IrLoc.cs:3; IrScope.cs:3; IrString.cs:3; IrType.cs:6-34.
[^analysis]: ProgramAnalysis.cs — `Functions`, `Tasks` :23-25; budget and task figures :596-632; `LineEstimates` :913-1005; FunctionAnalysis.cs:7-57; TaskFigures.cs:6-21; BusAnalysis.cs — `Accesses` (`EndCol`) :227-262; BusAccess.cs:3.
[^compile-report]: CompileReport.cs:11-21; LineCost.cs:6; ReportWcet.cs:3; ReportMachine.cs:3; ReportProgram.cs:3; ReportRam.cs:3.
[^emitter]: Emitter.cs — public constructor and options :27-49; `Emit` :51; failure returns without a report :81-457 (one with `Analysis` :158); success return :467; `ReportWcet` and `ReportMachine` construction :714-723; EmitResult.cs:5.
[^debug-info]: DebugInfo.cs:25-39 (lists), :41-60 (`String`, `Name`); DebugFunction.cs:3; DebugLine.cs:3; DebugGlobal.cs:3; DebugLocal.cs:3; DebugType.cs:3; DebugField.cs:3; DebugFile.cs:3.
[^image]: ImageReader.cs:41; KvmImage.cs:39-47 (`TickHz`, `BudgetCycles`, `InitBudgetCycles`), :85 (`Debug`).
[^workspace-project]: Workspace.cs — `Extensions` :15; `MAX_SOURCE_BYTES` :19; `Resolve` :66-73; `Read`/`ReadSource`/`TooLarge` :80-103; `ProjectFile` :113-121; `FolderOf` :123-132; `HoldsSourcesOf` :134-158; include regex :169; `ProjectOf` :176-235 (folder case :182-193; header refusal :194-199; root `.c` walk :200-225; overlay `Text` :227-234); BuildProject.cs:5-15.
[^workbench-ctor]: Workbench.cs:364-389 (`KcFrontEnd.Register()` :367; the `_prepare` closure capturing `Target.Part` and the layout :372-377).
[^workbench-target]: Workbench.cs — `TargetLayout` :66; `Tables`, `Workspace`, `Bench`, `Editor`, `Out` :68-76; private `VehicleTarget` (clears and prints) :82-94; `Target` :98-109; `TargetName` :113-129; `CurrentBuild` :171.
[^workbench-views]: Workbench.cs — `ConsoleLines`/`ConsoleTail` :1734-1746; `WantRam` :1748-1756; `RunningSnapshotOf` :1758-1771; `ReportViewOf` :1813-1831; `Report(file)` :1833-1846; `Lines` :1859-1913; `Preview` :1915-1958; `Def` :1960-1993; `OpenSource` :2089-2101; `SourceLines` :2103-2121; `Manual` :2123-2133; `Edit` :420-451; `Upload` :856-878; `TryResolve` :1456-1511; `Read` :1522-1566.
[^workbench-frame]: Workbench.cs:1168-1200 (`Editor.Poll()` :1172; `CheckExternalChange` every 0.5 s :1174-1179); KfsIntegration.cs:29-32.
[^editor-model]: EditorModel.cs — fields `_pending`, `_pendingFile`, `_pendingTextVersion` :36-52; `TextVersion`, `Outcome`, `LineCosts`, `Definitions`, `Background`, `Building`, `OutcomeTextVersion` :90-137; `Save` :344-365; `BuildOf` :367-394; `Assemble` :406-427; `Poll` :429-449; `Wait` :451-456; `Apply` :458-478; `CheckExternalChange` :490-534; `GoToDefinition` :571-579.
[^editor-window]: KfsEditorWindow.cs — constructor setting `Background = true` :700-711; Save menu and Ctrl+S :961-963, :1073-1076; F12 and `GoToDefinition` :1049-1066, :1077-1080; Build button :1277-1280; cost column `UpdateCosts` :175-186; manual tooltip :2149.
[^kfs-integration]: KfsIntegration.cs — `FrameHook.OnDrawUi` → `Frame` :29-32; `Tables` :89; `Workbench` getter :93-121; `UseWorkbench` :137-144.
[^definitions]: Definition.cs:3; Definitions.cs — `Of` :11-27; `Find` :29-35; `WordAt` :37-66; SourceLink.cs:6-21.
[^diagnostic]: Diagnostic.cs:3-9; Span.cs:3; Severity.cs:3-7.
[^diagnostic-bag]: DiagnosticBag.cs:7-35.
[^diagnostic-manual]: DiagnosticManual.cs:11-50; DiagnosticRow.cs:5; DiagnosticsFile.cs:5; KSA.Kfs.Vm.Tables.Data.diagnostics.json (row `003`).
[^checks-warnings]: Preprocessor.cs:610 (`W017`), :828, :856 (`W010`); Parser.cs:561 (`W010`), :875 (`W203`), :947, :1549, :1656 (`W006`), :2266 (`W208`), :2418 (`W207`); KcFrontEnd.cs:189, :193 (`W006`); Checks.cs:137 (`W006`), :152 (`W204`), :397 (`W206`), :513 (`W711`); Lowering.cs:134 (`W307`); ProgramAnalysis.cs:552 (`KA-W260`), :802 (`KA-W711`); BusAnalysis.cs:123 (`KA-W546`).
[^line-cost-view]: LineCostView.cs:10-36; LineCostRow.cs:8-69; BusMarkerView.cs:12, :41; BusMarker.cs:5-8; ReportView.cs:13-29, :143; ReportTaskRow.cs:6-60.
[^game-tables]: GameTables.cs — table properties :25-73 (`Opcodes`, `OpcodesByMnemonic`, `Categories`, `Parts`, `BenchPart`, `BenchLayout`, `Services`, `AllowedTickHz`); `LoadEmbedded` :80; `Part(name)` :470; `LayoutFrom` :761-796; `LoadLayout` :798-813; `ToLayoutFile` :815-823; `LayoutJson` :825-828; `CanonicalLayoutText` :830-846; `ComputeLayoutFingerprint` :848-851; KSA.Kfs.Vm.Tables.Data.bench_layout.json (name `bench`, fingerprint `0x070A6447`).
[^table-rows]: ServiceRow.cs:5; ServiceInfo.cs:3; ServiceClassRow.cs:6; CostsFile.cs:5; OpcodeInfo.cs:3; OpcodeRow.cs:5; TrapRow.cs:3; SysBlockField.cs:3; MessageRow.cs:5; MessageFieldRow.cs:5; EventRow.cs:3; CatalogueEnums.cs:34-60; PartDescription.cs:3.
[^message-layout]: MessageLayout.cs — `_elements` :25; properties :31-49; `GroupCount` :79; `CachedElement` :158-167; `All` :316-319; `Find` :321-339; MessageLeaf.cs:3; MessageMember.cs:3.
[^layout]: LayoutGenerator.cs — `Vehicle` :13-85; `CanonicalText` :97-128; `Fingerprint` :130-138; IoLayout.cs:6-9; IoField.cs:3; LayoutFile.cs:5; LayoutField.cs:3; KfsControllerSet.cs:71.
[^host-computer]: HostComputer.cs — `Console`, `Traces`, `Watches`, `Leaves` :135-141; `Snapshot` :167-174; `LoadedBytes` :196; `Watched` :204-213; `WantSnapshot` :229-232; `WantRam` :234-245; `Enqueue`/`DrainQueue` :288-300; watch sampling :580-582; `Watch`/`Unwatch` :595-623; MachineSnapshot.cs (record and init properties); TaskSnapshot.cs; TrapSnapshot.cs; FunctionSnapshot.cs.
[^host-services]: ConsoleLog.cs — `CAPACITY` :11; `Version` :35-44; `Snapshot` :168-174; `CopyTail` :176-187; TraceStore.cs — `Channels` :21-30; `Samples` :60-67; `CopyLast` :78-91; LogLine.cs; TraceSample.cs:3.
[^global-leaves]: GlobalLeaves.cs — `Globals` :16-28; `Leaves` :39-60; GlobalLeaf.cs:7-44; BusMonitorView.cs — `_enumNames` :16, `lock` :218-230; `Definition` :274.
