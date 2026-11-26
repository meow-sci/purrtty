---
type: Subsystem Reference
title: kFS compile pipeline (Compiler.cs end to end)
description: The stages that turn a kC or kASM project into a kVM image — inputs, options, the front-end seam, validation, peephole, emission, results, how kC and kASM mix, how the standard library is pulled in, and every limit.
resource: ksa-decomp:KSA.Kfs.Compiler/Compiler.cs
tags: [kfs, compiler, pipeline, kc, kasm, limits, extension]
sources:
  - id: compiler
    resource: ksa-decomp:KSA.Kfs.Compiler/Compiler.cs
    title: Compiler (decompiled)
  - id: compile-input
    resource: ksa-decomp:KSA.Kfs.Compiler/CompileInput.cs
    title: CompileInput (decompiled)
  - id: compile-options
    resource: ksa-decomp:KSA.Kfs.Compiler/CompileOptions.cs
    title: CompileOptions (decompiled)
  - id: compile-result
    resource: ksa-decomp:KSA.Kfs.Compiler/CompileResult.cs
    title: CompileResult (decompiled)
  - id: ifrontend
    resource: ksa-decomp:KSA.Kfs.Compiler/IFrontEnd.cs
    title: IFrontEnd (decompiled)
  - id: limits
    resource: ksa-decomp:KSA.Kfs.Compiler/TranslationLimits.cs
    title: TranslationLimits (decompiled)
  - id: program-properties
    resource: ksa-decomp:KSA.Kfs.Compiler/ProgramProperties.cs
    title: ProgramProperties (decompiled)
  - id: kc-front-end
    resource: ksa-decomp:KSA.Kfs.KC/KcFrontEnd.cs
    title: KcFrontEnd (decompiled)
  - id: kc-library
    resource: ksa-decomp:KSA.Kfs.KC/Library.cs
    title: kC Library (decompiled)
  - id: kc-lowering
    resource: ksa-decomp:KSA.Kfs.KC/Lowering.cs
    title: kC Lowering (decompiled)
  - id: assembler
    resource: ksa-decomp:KSA.Kfs.KAsm/Assembler.cs
    title: kASM Assembler (decompiled)
  - id: emitter
    resource: ksa-decomp:KSA.Kfs.Compiler.Backend/Emitter.cs
    title: Emitter (decompiled)
  - id: diagnostic-bag
    resource: ksa-decomp:KSA.Kfs.Compiler/DiagnosticBag.cs
    title: DiagnosticBag (decompiled)
  - id: workbench
    resource: ksa-decomp:KSA.Kfs.Host.Workbench/Workbench.cs
    title: Workbench (decompiled)
  - id: cli
    resource: ksa-decomp:KSA.Kfs.Cli/Cli.cs
    title: kvm-cli (decompiled)
  - id: kc-cli
    resource: ksa-decomp:KSA.Kfs.Cli/KcCli.cs
    title: kC CLI (decompiled)
  - id: fake-vehicle
    resource: ksa-decomp:KSA.Kfs.Host/FakeVehicle.cs
    title: FakeVehicle (decompiled)
  - id: image-format
    resource: ksa-decomp:KSA.Kfs.Vm.Image/ImageFormat.cs
    title: ImageFormat (decompiled)
generated: { by: claude-code/claude-fable-5-2, at: 2026-11-25T21:29:02Z }
status: stable
ksa_build: "2026.11.25.5949"
---

# Two routes to an image

There are two ways to produce a kVM image, and they share only the back end:

```text
kC project (has at least one .c file)
  Compiler.Compile(CompileInput)
    └─ IFrontEnd.Lower  (KcFrontEnd: preprocess, parse, sema, link, lower)
         ├─ project *.kasm  → Assembler{ModuleOnly=true} → IrModule merged in
         └─ library <string.kasm> → Assembler{ModuleOnly=true} when memcpy/… is used
    └─ IrValidator.Check
    └─ Peephole.Run
    └─ IrValidator.Check
    └─ Emitter("KC").Emit          → EmitResult { Image, Analysis, Report, Bus }

pure kASM project (no .c file)
  new Assembler(tables, part, layout).Assemble(files)
    └─ Preprocess → PassA → PassB → CheckProgram
    └─ Emitter("KA"){ KeepOrder=true, AllowRecursion=ImageMode }.Emit
```

`Compiler` is never used for a pure-kASM build: the assembler constructs its own `Emitter` and skips
`IrValidator` and `Peephole` entirely.[^assembler-emit] The workbench picks the route with
`project.IsKc`.[^workbench-build]

Both routes take the same three context objects: a `GameTables` (the embedded JSON tables), a
`PartDescription` (the processor part the program is built for) and an `IoLayout` (the target's
I/O layout: a command block, plus a sensor block on the bench only). All three end up fingerprinted
in the image header — see [kVM image format](/compiler/kvm-image-format.md).

# Callers

| caller | call | notes |
| --- | --- | --- |
| `Workbench` build | `Compiler.Compile(new CompileInput { Files, Part, Game = Tables, Layout, ProjectName })` | default `CompileOptions`[^workbench-build] |
| `Workbench` build (kASM) | `new Assembler(Tables, part, layout) { ProjectFiles = … }.Assemble(…)` | only the project's `.kasm` files[^workbench-build] |
| `kvm-cli compile` | `Compiler.Compile` with `BoundsChecks`, `Optimize`, `DoubleBits`, `Defines`, `PeepholeOff`, `EmitDebug` from flags | can print the IR with `IrPrinter.Print`[^cli-compile] |
| `kvm-cli assemble` | `new Assembler(...) { EmitDebug, ImageMode = o.Image, ProjectFiles }` | `--image` reassembles a disassembly[^cli-assemble] |
| `KcCli` | `Compiler.Compile` with `EmitDebug = false`, bench layout | diagnostics only[^kc-cli] |
| `FakeVehicle` | `new Assembler(Tables, Computer.Part, Layout).Assemble(one file)` | bench/test helper[^fake-vehicle] |

`Not active in this build:` the `kvm-cli` and `KcCli` rows are public entry points that nothing in
the shipped game calls — see [CLI](/tooling/cli.md).

After a successful build the workbench re-reads the image with `ImageReader.Read` against the same
part, tables and layout; if that fails it reports `KC-E900` / `KA-E900` "the built image fails
validation: …".[^workbench-validate]

# `CompileInput`

`public sealed record CompileInput`.[^compile-input]

| member | type | default | meaning |
| --- | --- | --- | --- |
| `Files` (required) | `IReadOnlyDictionary<string,string>` | — | project file name → text. `.c` files are translation units, `.kasm` files are assembled, `.h` and other names are reachable through `#include "…"` / `.include "…"`, `.py` triggers a "not yet" diagnostic |
| `SystemFiles` | `IReadOnlyDictionary<string,string>` | empty | overrides for `<…>` system headers; consulted before `vehicle.h` and `SystemHeaders.Get`[^kc-front-end-headers] |
| `Options` | `CompileOptions` | `new()` | see below |
| `Part` (required) | `PartDescription` | — | target processor part (limits, cost table, core class) |
| `Game` (required) | `GameTables` | — | the tables every stage reads |
| `Layout` (required) | `IoLayout` | — | vehicle I/O layout; drives `vehicle.h`, message sizes and the image fingerprint |
| `ProjectName` | `string` | `"project"` | used as the `File` of diagnostics that have no source position |

# `CompileOptions`

`public sealed record CompileOptions`.[^compile-options]

| member | default | consumed by |
| --- | --- | --- |
| `BoundsChecks` | `true` | ANDed into each kC function's `BoundsChecks` before lowering[^kc-front-end-lower] |
| `Optimize` | `1` | `0` forces every kC function's `Optimize` to 0; a function with `Optimize == 1` gets `IrFunction.Peephole = true`[^kc-front-end-lower][^kc-lowering-fn] |
| `Strict` | `false` | **Not active in this build:** no code in the decompiled kFS namespaces reads `CompileOptions.Strict`. The strict-only diagnostics in the table (702, 704–706, 708, 709) are never emitted |
| `DoubleBits` | `64` | copied to `IrModule.DoubleBits`, passed to the kC parser, checked against messages with `double` fields, copied to `Emitter.DoubleBits` and the image's `FLAG_DOUBLE32`[^compiler-stages] |
| `Defines` | empty | `NAME` or `NAME=value` predefines for the kC preprocessor[^kc-front-end-predefine] |
| `PeepholeOff` | empty set | peephole rule numbers (1–5) to skip; echoed into the report as `peephole_off` |
| `EmitDebug` | `true` | whether the image carries a Debug section |
| `TimeLimitMs` | `30000` | wall-clock limit for the whole compile |

# The front-end seam: `IFrontEnd` and `Compiler.KcFrontEnd`

```csharp
public interface IFrontEnd { bool Lower(CompileInput input, IrModule module, DiagnosticBag diags); }
public static Func<IFrontEnd>? KcFrontEnd { get; set; }   // on static class Compiler
```

`Compiler.KcFrontEnd` is a **public static settable factory**.[^compiler-factory] The only stock
assignment is `KcFrontEnd.Register()`, which sets it to `() => new KcFrontEnd()`; that method is a
`[ModuleInitializer]` and is also called explicitly by the workbench.[^kc-front-end-register]
`CompileOnThisThread` throws `InvalidOperationException("no kC front end is registered
(KSA.Kfs.KC.Register())")` if it is null.[^compiler-stages] A new front end instance is created per
compile.

# `Compiler.Compile` — threading and the time limit

`Compile(input)` runs `CompileOnThisThread` on a dedicated background thread named `"kFS compile"`
with a 16 MiB stack (`COMPILE_THREAD_STACK_BYTES = 16777216`) and joins it with
`input.Options.TimeLimitMs` (`COMPILE_TIME_LIMIT_MS = 30000` by default).[^compiler-thread]

- On timeout the thread is abandoned (it is a background thread, it is not aborted) and the result is
  `Ok: false` with a single `KC-E900` "internal compiler error: the compile did not finish in N s and
  was abandoned".
- An exception on the compile thread is captured and rethrown on the caller with
  `ExceptionDispatchInfo`.
- `CompileOnThisThread(input)` is public and runs the same pipeline synchronously with no time limit.

# Stages of `CompileOnThisThread`, in order

1. **Set up.** A fresh `DiagnosticBag`; a fresh `IrModule { DoubleBits = input.Options.DoubleBits }`.
2. **Front end.** `frontEnd.Lower(input, module, diags)`. A `false` return or any error ends the
   compile with `Ok: false` and the partially built module attached. A `LimitException` thrown
   anywhere in the front end becomes one `KC-E<number>` error with a hint chosen by number: 722/723
   "A header that includes itself, or a cycle; add an include guard.", 725 "A macro that repeats its
   argument at each level of nesting grows exponentially.", 724 "No program this size fits any part's
   program memory."[^compiler-stages]
3. **Order globals.** `module.Globals` is stably re-sorted by the index of each global's source file in
   `module.Files` (unknown files last), so RAM addresses depend on file order, not on the order the
   front end happened to add them.[^compiler-order]
4. **Validate.** `IrValidator.Check(module, input.Game)`; a non-null result is `KC-E900` "internal
   compiler error: the IR validator rejected the lowering (…)". See
   [IR reference](/compiler/ir-reference.md).
5. **Peephole.** `Peephole.Run(module, input.Options.PeepholeOff, out _)` — the "did not settle"
   output is discarded here. See [backend emitter](/compiler/backend-emitter.md).
6. **Validate again.** Failure is `KC-E900` "… rejected the peephole output (…)".
7. **Emit.** `new Emitter(input.Game, input.Part, input.Layout, diags, "KC") { EmitDebug, DoubleBits =
   module.DoubleBits, PeepholeOff = sorted list or null }.Emit(module)`. This runs
   [program analysis](/compiler/program-analysis-and-wcet.md), [bus analysis](/compiler/bus-analysis.md),
   placement, encoding, RAM planning and `ImageBuilder.Build`.
8. **Result.** `ProgramProperties(module.TickHz ?? Game.CatalogueData.DefaultTickHz, module.DoubleBits,
   module.Claims)` and a `CompileResult` whose `Ok` is `emitResult.Image != null && !diags.HasErrors`.[^compiler-result]

# What the kC front end does before the IR exists

The front end belongs to [kC front-end internals](/language/kc-front-end-internals.md); only what
matters to the middle and back end is summarised here.[^kc-front-end-lower-top]

1. Fail with `KC-E012` "the project has no .c file" if there is none. `.py` files produce a "kScript
   files beside kC … MS9" not-yet diagnostic.
2. Add every project file name to `module.Files` in ordinal order.
3. **Assemble the project's `.kasm` files first** (all of them in one `Assembler` call, ordinal file
   order) with `ModuleOnly = true`, sharing the compile's `DiagnosticBag` and resolving
   `.include "x"` against `input.Files`.[^kc-front-end-assemble]
4. Generate `vehicle.h` from the layout, then preprocess and parse each `.c` file (ordinal order)
   followed by any pending library sources.
5. Assemble the library's kASM if needed (below).
6. `Link`, `Entries`, `Bus`, `ServiceOwnership`, semantic `Checks`, then `LowerModule`, which builds
   `IrGlobal`s and one `IrFunction` per kC function body via
   [`IrBuilder`](/compiler/ir-reference.md), and finally `PruneLibrary`.

## How kC and kASM mix in one program

- `Assembler.Assemble` with `ModuleOnly = true` stops after `PassA`/`PassB`/`CheckProgram` and returns
  an `IrModule` instead of an image.[^assembler-emit] Its functions, globals, strings, claims, bus
  declarations and files are appended to the compile's module; files are tagged `DebugLang.KASM`.
- A kASM function whose name is already taken is `KC-E016` "'f' is defined twice".
- For every project `.kasm` file the front end synthesises a header named `<file>.kasm.h` containing
  `#pragma once`, `#include <stdint.h>`, `#include <kfs.h>` and one prototype per `.proto` line of that
  file. kC code includes it with `#include "dot3.kasm.h"`; it is served from an in-memory `generated`
  map, not from the project folder.[^kc-front-end-assemble]
- In `Link`, a kC declaration with no kC definition whose name matches a kASM function or global is
  marked `IsKasm` and resolved to it; a name defined in both is `KC-E016` "defined in kC and in kASM".
  A kASM `CALL` whose target is defined in neither language is `KA-E304`.[^kc-front-end-link]
- kASM functions keep `Language = "kasm"` and `Peephole = false`; kC functions get `Language = "kc"`.
  Diagnostics from later stages choose their prefix (`KC`/`KA`/`KS`) from the function's language.
- **String indices.** kASM `.string`/`.format` symbols evaluate to an index into `module.Strings` at
  assembly time. Because project kASM is assembled before any kC lowering appends strings, the indices
  stay valid in the merged module. `Inferred:` this ordering is load-bearing — nothing renumbers
  strings when modules are merged.
- `.claim ALLOCATION`, `.publish`/`.subscribe` and the entry directives (`.init`, `.task`, `.program`)
  in kASM carry into the merged program: claims and bus declarations are copied and the entry kind,
  divisor and priority live on the `IrFunction`s.
- **`.tick_hz` in a `.kasm` file does not carry over.** `KcFrontEnd.Assemble` copies functions,
  globals, strings, claims, bus declarations and files, but not the kASM module's `TickHz`; the merged
  program's tick rate comes only from `#pragma kfs tick_hz` in kC (or the catalogue default), while a
  kASM `.task f rate_hz=N` was already converted to a divisor against the kASM module's own tick rate.
  `Inferred:` from the absence of any `TickHz` copy in `Assemble`; in a mixed project declare the tick
  rate in kC and give kASM tasks `divisor=`.

## How the standard library is pulled in

The library is three embedded sources, `math.c`, `string.kasm` and `vecmat.c`, read lazily from
manifest resources ending in `Library.<name>` and cached in a static field.[^kc-library]

| trigger | effect |
| --- | --- |
| a translation unit `#include`s `<math.h>` | `<math.c>` is queued as an extra translation unit (`Library.SourceFor`) |
| a translation unit `#include`s `<vecmat.h>` | `<vecmat.c>` is queued likewise |
| any kC use (`Uses > 0`) of `memcpy`, `memmove`, `memset` or `memcmp` resolved to kASM | every library file ending `.kasm` is assembled as `<string.kasm>` with `library: true` (no generated header) |

Library translation units have file names in angle brackets; `IsLibrary` tests throughout the back end
are simply `At.File.StartsWith('<')`. After lowering, `PruneLibrary` removes every function not
reachable (through `CALL`/`CALLF` operands) from an entry point or a non-library function, then every
library global no remaining instruction or kept global's relocation names.[^kc-front-end-prune]
The emitter places library functions after all project functions (see
[backend emitter](/compiler/backend-emitter.md)).

# `CompileResult` and `ProgramProperties`

```csharp
public sealed record CompileResult(bool Ok, byte[]? Image, ProgramProperties? Program,
    IReadOnlyList<Diagnostic> Diagnostics, IrModule? Module, int[]? PeepholeFired,
    ProgramAnalysis? Analysis = null, CompileReport? Report = null);
public sealed record ProgramProperties(int TickHz, int DoubleBits, IReadOnlyList<string> Claims);
```

| member | meaning |
| --- | --- |
| `Ok` | image produced and no error in the bag |
| `Image` | the complete image bytes including the trailing CRC, or null |
| `Program` | tick rate actually used, double width, claim names (`"ALLOCATION"` is the only one) |
| `Diagnostics` | everything in the bag, errors and warnings, in emission order |
| `Module` | the IR after peephole (present even on most failures) |
| `PeepholeFired` | `int[6]`; index *n* counts applications of peephole rule *n* (index 0 unused); null if the compile failed before the peephole |
| `Analysis` | the `ProgramAnalysis` (stack, WCET figures) |
| `Report` | the [compile report](/compiler/compile-report.md) |
| `Errors` | computed: diagnostics with `Severity.Error` |
| `Estimates` | computed: `Report?.Estimates` or empty (per-line cycle costs) |
| `BusAccesses` | computed: `Report?.BusAccesses` or empty (editor bus markers) |

`AssembleResult(bool Ok, byte[]? Image, IReadOnlyList<Diagnostic> Diagnostics, IrModule? Module,
CompileReport? Report = null)` is the kASM route's equivalent, with the same three computed members.

# Limits

| limit | value | where | diagnostic |
| --- | --- | --- | --- |
| compile thread stack | 16,777,216 bytes | `Compiler.COMPILE_THREAD_STACK_BYTES` | — |
| compile wall clock | 30,000 ms | `Compiler.COMPILE_TIME_LIMIT_MS`, `CompileOptions.TimeLimitMs` | `KC-E900` |
| nesting depth | 128 | `TranslationLimits.NESTING`; kASM macro and expression nesting | 720 |
| binary operators per full expression | 1,000 | `TranslationLimits.BINARY_OPERATORS` (kC parser) | 721 |
| include depth | 32 | `TranslationLimits.INCLUDE_DEPTH`, `Assembler.MAX_INCLUDE_DEPTH` | 722 |
| includes per unit / program | 1,000 | `INCLUDES_PER_UNIT`, `Assembler.MAX_INCLUDES` | 723 |
| tokens after preprocessing / expansion | 150,000 | `TOKENS`, `Assembler.MAX_TOKENS` | 724 |
| macro-expansion tokens | 200,000 | `MACRO_TOKENS`, `Assembler.MAX_MACRO_TOKENS` | 725 |
| object size | 1,048,576 bytes | `OBJECT_BYTES`, `Assembler.MAX_OBJECT` | 726 |
| `#line` maximum | 2,147,483,647 | `TranslationLimits.LINE_MAX` | 727 |
| compiler's own stack | `RuntimeHelpers.TryEnsureSufficientExecutionStack()` | `TranslationLimits.EnsureStack` | 720 "nesting too deep for the compiler's stack" |
| errors reported | 100 | `DiagnosticBag.MAX_ERRORS` | a final "too many errors; stopping" |
| image size | 16,777,216 bytes | `ImageFormat.MAX_IMAGE_BYTES` | load rule `size` |
| frame size | 65,528 bytes | assembler / kC lowering | 717 |
| argument cells | 255 | assembler / kC | 719 |
| result cells | 8 | assembler, `ImageReader` | 331 / load rule `ret` |
| branch reach | ±32 KB (`rel16`) | emitter | 703 |
| near `CALL` target | first 64 KB | emitter | 713 (kASM); kC calls are widened to `CALLF` automatically |

`TranslationLimits` constants are from the decompiled class;[^limits] per-part limits (tasks, RAM,
program memory, operand cells, return entries, tick budget) are covered in
[program analysis and WCET](/compiler/program-analysis-and-wcet.md).

# Modding notes

- **Everything here is public.** `Compiler` (static), `CompileInput`, `CompileOptions`, `CompileResult`,
  `IFrontEnd`, `Emitter`, `Assembler`, `IrBuilder`, `IrModule` and friends are all `public`, so a mod
  can call the toolchain directly with its own `GameTables` / `IoLayout` without any reflection.
- **`Compiler.KcFrontEnd` is a global, settable hook.** A mod can wrap the stock factory
  (`var stock = Compiler.KcFrontEnd; Compiler.KcFrontEnd = () => new Wrapper(stock())`) to inspect or
  post-process the `IrModule` after lowering — for example to append `IrGlobal`s or rewrite `SYSCALL`
  operands — without a Harmony patch. The `Workbench` constructor calls `KcFrontEnd.Register()`
  unconditionally, so a wrapper installed before a workbench is constructed is overwritten; install
  after construction or patch `Register`. The game builds its workbench lazily, on the first read of
  `KfsIntegration.Workbench` (see [architecture](/overview/architecture.md#object-graph-at-run-time));
  `Unverified:` when that first read happens relative to mod load — see
  [workbench](/tooling/workbench.md).
- **The tables object is passed in, not global.** The compiler reads whatever `GameTables` instance the
  caller supplies (`input.Game`); the workbench passes its own `Tables`. A mod that adds rows to that
  instance before a build changes what the compiler accepts (services, messages, opcode costs). There
  is no second copy inside the compiler.
- **Threading.** Compiles run on their own thread and touch only the inputs; the calling thread — the
  main thread for a console `kfs build`, a `Task.Run` worker for the editor window's background
  builds (see [build and load flow](/tooling/build-and-load-flow.md)), never a vehicle's dispatch
  thread — blocks in `Thread.Join` for up to `TimeLimitMs`. Mutating `GameTables` from another
  thread during a compile is a race.
- **`Options.Strict` does nothing** in this build; do not rely on strict-only rules.
- **Unknown service ids cannot be compiled.** See the dedicated table in
  [kVM image format](/compiler/kvm-image-format.md#where-an-unknown-service-id-is-rejected).

[^compiler-thread]: Compiler.cs:20-59
[^compiler-factory]: Compiler.cs:24
[^compiler-stages]: Compiler.cs:61-101
[^compiler-order]: Compiler.cs:102-107
[^compiler-result]: Compiler.cs:108-129
[^compile-input]: CompileInput.cs:6-21
[^compile-options]: CompileOptions.cs:6-23
[^limits]: TranslationLimits.cs:5-30
[^kc-front-end-register]: KcFrontEnd.cs:25-29; Workbench.cs:367
[^kc-front-end-lower-top]: KcFrontEnd.cs:31-202
[^kc-front-end-headers]: KcFrontEnd.cs:75-79
[^kc-front-end-assemble]: KcFrontEnd.cs:62-66, 328-390
[^kc-front-end-predefine]: KcFrontEnd.cs:392-415
[^kc-front-end-link]: KcFrontEnd.cs:417-527
[^kc-front-end-lower]: KcFrontEnd.cs:768-792
[^kc-front-end-prune]: KcFrontEnd.cs:804-869
[^kc-lowering-fn]: Lowering.cs:73-91
[^kc-library]: Library.cs:9-49; KcFrontEnd.cs:103-115, 154-163
[^assembler-emit]: Assembler.cs:118-178
[^workbench-build]: Workbench.cs:503-535
[^workbench-validate]: Workbench.cs:551-570
[^cli-compile]: Cli.cs:300-361
[^cli-assemble]: Cli.cs:363-388
[^kc-cli]: KcCli.cs:128-155
[^fake-vehicle]: FakeVehicle.cs:48
