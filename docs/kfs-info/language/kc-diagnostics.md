---
type: Language Reference
title: kC diagnostics
description: Catalogue of the diagnostics a kC programmer can hit — code, severity, message text and cause for everything the kC front end emits, plus the table rules that are listed for kC but never raised in this build.
resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.diagnostics.json
tags: [kfs, language, kc, diagnostics, errors, warnings]
sources:
  - id: diag-json
    resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.diagnostics.json
    title: diagnostics.json (embedded table)
  - id: diagbag
    resource: ksa-decomp:KSA.Kfs.Compiler/DiagnosticBag.cs
    title: DiagnosticBag (decompiled)
  - id: diagnostic
    resource: ksa-decomp:KSA.Kfs.Compiler/Diagnostic.cs
    title: Diagnostic (decompiled)
  - id: compiler
    resource: ksa-decomp:KSA.Kfs.Compiler/Compiler.cs
    title: Compiler (decompiled)
  - id: lexer
    resource: ksa-decomp:KSA.Kfs.KC/Lexer.cs
    title: Lexer (decompiled)
  - id: preprocessor
    resource: ksa-decomp:KSA.Kfs.KC/Preprocessor.cs
    title: Preprocessor (decompiled)
  - id: parser
    resource: ksa-decomp:KSA.Kfs.KC/Parser.cs
    title: Parser (decompiled)
  - id: sema
    resource: ksa-decomp:KSA.Kfs.KC/Sema.cs
    title: Sema (decompiled)
  - id: checks
    resource: ksa-decomp:KSA.Kfs.KC/Checks.cs
    title: Checks (decompiled)
  - id: lowering
    resource: ksa-decomp:KSA.Kfs.KC/Lowering.cs
    title: Lowering (decompiled)
  - id: frontend
    resource: ksa-decomp:KSA.Kfs.KC/KcFrontEnd.cs
    title: KcFrontEnd (decompiled)
  - id: busanalysis
    resource: ksa-decomp:KSA.Kfs.Compiler/BusAnalysis.cs
    title: BusAnalysis (decompiled)
  - id: emitter
    resource: ksa-decomp:KSA.Kfs.Compiler.Backend/Emitter.cs
    title: Emitter (decompiled)
  - id: workbench
    resource: ksa-decomp:KSA.Kfs.Host.Workbench/Workbench.cs
    title: Workbench (decompiled)
generated: { by: claude-code/claude-fable-5-2, at: 2026-11-25T21:30:48Z }
status: stable
ksa_build: "2026.11.25.5949"
---

# How diagnostics are formed

A diagnostic is `Diagnostic(Severity, Code, File, Line, Col, Message, Hint?)`. The code is
`<prefix>-<E|W><number, three digits>`; the kC front end always uses the prefix `KC`, so codes look
like `KC-E012` and `KC-W006`. Printed form:[^diagnostic-tostring][^diagbag-emit]

```text
main.c:14:9: error KC-E003: 'altitude' is not declared
  hint: Did you mean 'altitude_m'?
```

- **Severity is decided at the call site** (`DiagnosticBag.Error` or `.Warning`). The front end does
  not consult `diagnostics.json` when emitting; the table (schema and loader:
  [diagnostics table](/tables/diagnostics-table.md), registry:
  [diagnostics infrastructure](/compiler/diagnostics-infrastructure.md)) documents the rules. Its
  "E strict" and "strict only" severities have no effect, because strict mode is not implemented in
  this build.
- After 100 errors the bag adds "too many errors; stopping" and ignores further errors; parsing
  stops.[^diagbag-emit]
- Any error means no image is produced. Warnings never block a build.
- A file position of line 0, column 0 with the project name as the file marks a whole-program
  diagnostic.

# Catalogue: emitted by the kC front end

"Rule" is the table's wording for the number.[^diag-json-rules] Messages are quoted from the code; `…`
stands for an inserted name or value.

## 0xx — names, syntax, files

| code | rule | raised for |
| --- | --- | --- |
| `KC-E000` | *(not in the table)* | A feature reserved for a later milestone: "… is not implemented until MSn", hint "This is a milestone limit of the current kFS build, not a kC rule." Raised for `.py` files in the project (MS9), `#pragma kfs strict` (MS13), a builtin with no service binding (MS6), and `<matn.h>` (MS-G2).[^sema-notyet][^frontend-notyet] |
| `KC-E003` | Unknown symbol or undeclared identifier | "'x' is not declared" (with "Did you mean 'y'?"), "'struct s' has no member 'm'", "'MSG_X' is not a message", a bus pragma whose buffer object does not exist, an initialiser that "names 'x', which has no address".[^parser-undeclared][^frontend-bus] |
| `KC-W006` | Unused variable, label, local or global | "'x' is never used" for a `static` function or global, or a local; "declaration declares nothing"; "'x' is `extern` and initialised".[^frontend-unused][^checks-unused] |
| `KC-W010` | Unknown pragma | "unknown pragma '…'; it has no effect in kC", "empty #pragma has no effect in kC", "unknown pragma 'kfs …'".[^preprocessor-pragma] |
| `KC-E012` | Syntax error: the source doesn't parse | Every parse error: "expected ';' after the expression; found '}'", "expected a type; found '…'", "unexpected '…' in an expression"; every preprocessor structure error ("#else without #if", "unknown directive #…", macro argument errors, bad `##`); lexical errors ("unterminated comment", "stray '@' in the program"); malformed `#pragma kfs` lines; "`__at` is for the system headers"; "the project has no .c file".[^parser-expect][^preprocessor-errors][^lexer-errors] |
| `KC-E013` | `break`/`continue`/`case`/`default` misplaced | "`break` is not inside a loop or switch", "`continue` is not inside a loop", "`case` is not inside a `switch`".[^parser-misplaced] |
| `KC-E014` | `#error` directive reached | the directive's text.[^preprocessor-directive] |
| `KC-E015` | Include file not found | "can't find '…'", hint "\"\" searches the code project, <> the library and generated headers."[^preprocessor-include] |
| `KC-E016` | Conflicting or duplicate declaration or definition | "'x' is already declared", "'x' is defined twice", "'struct s' is already defined", "duplicate member 'm'", "'M' is already defined with a different body" (macro), "'x' is defined in 'b.c' and in 'a.c'", "'x' is declared with type '…' here and '…' in '…'", "'f' is defined in kC and in kASM", a buffer bound by two different bus pragmas.[^parser-declare][^frontend-link] |
| `KC-W017` | `#warning` directive reached | the directive's text.[^preprocessor-directive] |

## 1xx — constants

| code | rule | raised for |
| --- | --- | --- |
| `KC-E101` | Literal or immediate out of range or of the wrong kind | invalid or oversized numeric literals, "multi-character constant", bad escapes, wide literals, enumerator out of `int` range, "an array size must be positive", "too many initialisers for …", a string longer than its array.[^parser-literals][^lexer-errors] |
| `KC-W104` | Implicit `float` to `double` promotion without a double FPU | "implicit `float` to `double` promotion: a double multiply costs about N cycles on K32F instead of M", hint "Write the constant with an `f` suffix, or cast the operand to `float`." Only when the part's core class is not `K32D`.[^sema-w104] |
| `KC-E105` | Constant expression required but not given | "an array size needs an integer constant expression", "a `case` label needs …", "an enumerator value needs …", "the initialiser of a global or `static` object must be a constant expression", "a compound literal at file scope is not a constant expression". Hints explain that a `const` variable is not a constant and that division by zero is not one.[^parser-constexpr] |
| `KC-E106` | Duplicate `case` or `default` label | "duplicate `case N`", "duplicate `default` label".[^parser-switch] |
| `KC-E107` | Static assertion failed | "static assertion failed: \"…\"".[^parser-staticassert] |

## 2xx — types

| code | rule | raised for |
| --- | --- | --- |
| `KC-W202` | Implicit narrowing | "implicit narrowing from 'double' to 'float' loses precision or range", also 64-bit → narrower integers and compound assignments that compute wider than their target.[^sema-w202] |
| `KC-W203` | `double` is single precision | once per file when built with `double_bits = 32`.[^parser-w203] |
| `KC-W204` | Result depends on plain `char` signedness | "the result depends on the signedness of plain `char`".[^checks-w204] |
| `KC-W206` | Possibly uninitialised use | "'x' may be used before it is set", hint "Initialise it where it is declared."[^checks-w206] |
| `KC-W207` | Ignored non-`void` return value | "the result of 'f' is ignored", hint "Check it, or cast the call to `(void)`."[^parser-w207] |
| `KC-W208` | `switch` on an enum misses a case and has no `default` | "`switch` doesn't handle 'NAME'".[^parser-switch] |
| `KC-E209` | Store to constant data | "'x' is `const`".[^sema-const] |
| `KC-E211` | Incompatible types | "can't convert 'T' to 'U'", "can't assign 'T' to 'U'", "'+' doesn't accept 'T' and 'U'", "`switch` needs an integer", "the condition of 'if' must be a scalar", "can't dereference 'T'", "'T' has no members" (hint "Use `->` through a pointer."), "`return` with a value in the `void` function", "can't cast to 'T'".[^sema-e211] |
| `KC-E212` | Not a modifiable lvalue | "can't assign to '…': not an lvalue", "can't assign to an array", "'&' needs an lvalue".[^sema-const] |
| `KC-E213` | `void` or incomplete type | "'x' has an incomplete type 'struct s'", "member 'm' has an incomplete type" (hint "Flexible array members are not in kC."), "a parameter can't have type `void`", "sizeof an incomplete type", "pointer arithmetic on 'T *' (incomplete pointee)".[^parser-incomplete] |
| `KC-W250` | `==` on floats | "floating-point values are rarely exactly equal", hint "Compare the difference against a tolerance."[^sema-w250] |

## 3xx — functions

| code | rule | raised for |
| --- | --- | --- |
| `KC-E301` | Recursion | "'f' calls itself through 'g' (f → g → f)", hint "Flight code can't use recursion, because the stack size has to be known before launch. Turn the recursion into a loop."[^checks-e301] |
| `KC-E302` | Dynamic allocation | "'malloc': flight code can't allocate memory at run time", hint "Use a fixed-size static array."[^parser-builtins] |
| `KC-E303` | Function pointer | "function pointer", "a function used as a value is a function pointer", "call through something that isn't a function name", "a function member (function pointer)".[^parser-fnptr] |
| `KC-E304` | Called function has no definition | "'f' is declared but never defined", "'x' is declared `extern` but defined nowhere in the project", "'f' is declared `static` but never defined in '…'".[^frontend-link] |
| `KC-E305` | Wrong number of arguments | "'f' takes 2 arguments; 3 given" with the prototype in the hint; also for `trace`, `kfs_logf`, `assert` and the math intrinsics.[^sema-call] |
| `KC-W307` | Control can reach the end of a non-`void` function | "'f' can reach its end without returning a value".[^lowering-w307] |

## 4xx — program structure

| code | rule | raised for |
| --- | --- | --- |
| `KC-E401` | Entry function missing or wrong signature | "task 'step' must be defined as `void step(void)`", "`#pragma kfs task` needs a function name", "the program declares no task or init".[^frontend-entries] |
| `KC-E406` | Bad or duplicate `tick_hz` | "`#pragma kfs tick_hz N` needs one number", "`tick_hz` declared more than once", "N Hz is not an allowed tick rate" (hint lists the allowed rates).[^frontend-entries] |
| `KC-E407` | Task rate doesn't divide `tick_hz` | "N Hz doesn't divide the T Hz tick" (hint: nearest valid rates), "`#pragma kfs task f` needs rate_hz=N or divisor=N", "divisor must be at least 1".[^frontend-entries] |
| `KC-E409` | `main` defined | "kC programs have no `main`", hint "Declare tasks with `#pragma kfs task`."[^parser-main] |
| `KC-E410` | More than one `init`, or a function named by more than one entry pragma | "more than one `init` task in the program", "'f' is already an entry point".[^frontend-entries] |

## 5xx — services, bus, processor

| code | rule | raised for |
| --- | --- | --- |
| `KC-E501` | Service requires a stock block the program owns | "'kfs_set_throttle' is serviced by stock 'GUIDANCE', which this program owns"; "'…' steers the game's flight computer, which flies only in pilot mode; this program claims ALLOCATION (kFS D-135)".[^frontend-e501] |
| `KC-E520` | Feature or block unavailable on this processor | "claiming ALLOCATION is not available on the Sequencer", "publishing MSG_… is not available on the Sequencer", "<matn.h> is not available on the Sequencer".[^frontend-bus] |
| `KC-E541` | Bus buffer type doesn't match its message | "'buf' must have type 'kfs_msg_x_t'", "buffer type can't match under `double_bits = 32`".[^frontend-bus] |
| `KC-E550` | `log` over 8 values or 64 bytes, or invalid format | "invalid conversion in the kfs_logf format at '%s'", "kfs_logf format rejected: more than 8 values".[^parser-logf] |
| `KC-E551` | Bad sleep-until-event arguments | "the event isn't a constant KFS_EVENT_* value", "KFS_EVENT_TIME is not a sleep event", "KFS_EVENT_SOI_CHANGE has no parameter; pass 0".[^parser-sleep] |
| `KC-E552` | `kfs_logf` argument doesn't match its format | "'%d' needs a 32-bit integer; this is a `float`", "'%f' has no argument", "kfs_logf has 3 values for 2 conversions".[^parser-logf] |
| `KC-E553` | Format or channel name not a string literal | "the kfs_logf format isn't a string literal", "the trace channel isn't a string literal".[^parser-logf] |

## 6xx — things kC leaves out of C

| code | rule | raised for |
| --- | --- | --- |
| `KC-E640` | `goto`, labels, `setjmp`/`longjmp` | "`goto`", "label 'x:' (goto)", hint "Flight code uses simple control flow (Power of Ten rule 1); restructure with loops and early `return`."[^parser-6xx] |
| `KC-E641` | `union` or bitfield | "`union` is not in kC v1", "bitfields are not in kC v1". |
| `KC-E642` | Variable-length array | "variable-length array", "an array sized by a `const` variable is a variable-length array". |
| `KC-E643` | User-defined variadic function | hint "Only `kfs_logf` is variadic in kC; pass a fixed-size array and a count." |
| `KC-E644` | Integer/pointer cast | "integer/pointer cast", hint "Addresses are managed by the system headers."[^sema-e644] |
| `KC-E645` | `printf` family | "'printf': kC has no standard output", hint "Use `kfs_logf` or `trace`." |
| `KC-E646` | C11 or C23 construct | "'_Generic' is a C11/C23 construct", hint "kC follows C99." |
| `KC-E647` | GNU extension | "`__attribute__` is a GNU extension", "inline assembly is a GNU extension" (hint "Write a kASM function instead."). |
| `KC-W648` | Assignment in a condition | "assignment in a condition; did you mean `==`?"[^sema-w648] |
| `KC-E649` | Comma operator outside a `for` header | raised for a parenthesised comma expression. |
| `KC-E650` | K&R function definition | "old-style (K&R) function definition". |

## 7xx — sizes and limits

| code | rule | raised for |
| --- | --- | --- |
| `KC-W711` | Loop without a provable or declared bound | "can't prove this loop ends", hint "Compare a counter against a constant, with one constant-step update to the counter and no other writes to it (Power of Ten rule 2)."[^checks-w711] |
| `KC-E716` | Aggregate copy or array element over 65,535 bytes | "a struct of N bytes copied by value; the largest is 65,535", "an element of N bytes is larger than INDEX's 65,535".[^sema-e716] |
| `KC-E717` | Function frame over 65,528 bytes | "'x' is N bytes; a frame holds at most 65,528", "'f' has a N-byte frame".[^parser-e717] |
| `KC-E719` | Argument area over 255 cells | "'f' takes N argument cells; the limit is 255", hint "Pass a pointer to a struct."[^parser-e717] |
| `KC-E720` | Nesting deeper than 128 | "nesting deeper than 128 (blocks, parentheses, …)", "a type nested deeper than 128", "macro expansion nested deeper than 128", "#if expression nested deeper than 128", "nesting too deep for the compiler's stack".[^parser-limits] |
| `KC-E721` | More than 1,000 binary operators in one full expression | |
| `KC-E722` | Include depth over 32 | hint (added by the compiler driver) "A header that includes itself, or a cycle; add an include guard."[^compiler-limit] |
| `KC-E723` | More than 1,000 includes in one translation unit | same hint. |
| `KC-E724` | More than 150,000 tokens after preprocessing | hint "No program this size fits any part's program memory." |
| `KC-E725` | Macro expansion produced more than 200,000 tokens | hint "A macro that repeats its argument at each level of nesting grows exponentially." |
| `KC-E726` | Object or type larger than the largest RAM | "an array larger than the largest RAM (1 MB)", "'x' is larger than the largest RAM (1 MB)".[^frontend-e726] |
| `KC-E727` | `#line` outside 1 to 2147483647 | "#line needs a line number from 1 to 2147483647". |

Codes 720–725 are raised as `LimitException`: the compile stops at once with that single error.

## 9xx

| code | rule | raised for |
| --- | --- | --- |
| `KC-E900` | Internal compiler error | "internal compiler error: the compile did not finish in 30 s and was abandoned"; "… the IR validator rejected the lowering (…)"; "… rejected the peephole output (…)"; and, from the workbench, "the built image fails validation: …". Hint "This is a bug in kFS, not in your program. Report it with the source."[^compiler-e900][^workbench-e900] |

# `KC`-prefixed diagnostics from the back end

Later compiler stages report with the language prefix of the code involved, so a kC programmer also
sees these. They are specified in the compiler documents.

| code | rule | stage |
| --- | --- | --- |
| `KC-E540` | Write to a subscription buffer — "'x' is a subscription buffer; subscription buffers are read-only" | [bus analysis](/compiler/bus-analysis.md)[^busanalysis-codes] |
| `KC-E542` | Publication buffer written by no task or by more than one, or a message published through two buffers | bus analysis |
| `KC-E544` | Command block written without claiming `ALLOCATION` — "only the allocation owner may write the command block" | bus analysis |
| `KC-E718` | A task's start or completion step with its bus copies costs more than one tick's budget | bus analysis / [backend emitter](/compiler/backend-emitter.md) |
| `KC-E408` | Task order between files is ambiguous | backend emitter[^emitter-codes] |
| `KC-E701` | Stack, RAM or program size exceeded, or too many tasks | backend emitter |
| `KC-E703` | Branch beyond `rel16` range | backend emitter |
| `KC-E003`, `KC-E304`, `KC-E301` | unresolved symbol, undefined callee, recursion found on the IR call graph | backend emitter, [program analysis and WCET](/compiler/program-analysis-and-wcet.md) |

# Listed for kC but never raised in this build

`Not active in this build:` these rules name `KC` among their languages in `diagnostics.json`, but a
search of `KSA.Kfs.KC`, `KSA.Kfs.Compiler`, `KSA.Kfs.Compiler.Backend` and `KSA.Kfs.KAsm` for their
numbers finds no call site that emits them.[^diag-json-rules]

| number | rule | table severity |
| --- | --- | --- |
| 510 | Sensor not fitted on this vehicle | E |
| 670 | Malformed `@ks` annotation | W |
| 671 | Unsigned value out of kScript's range on export | W |
| 672 | Declaration not exportable to kScript | W |
| 702 | Worst-case cycles exceed the task budget | E, strict only |
| 704 | Function over 60 lines or instructions (Power of Ten rule 4) | W, strict only |
| 705 | Assertion density below 2 (rule 5) | W, strict only |
| 706 | Global used by one function (rule 6) | W, strict only |
| 708 | Restricted preprocessor or macro use (rule 8) | W, strict only |
| 709 | Pointer to pointer or multi-level dereference (rule 9) | W, strict only |

Reading a sensor the layout lacks therefore fails earlier and differently. A vehicle layout has no
sensor block at all, so `<vehicle.h>` does not declare `sensors` and the access is `KC-E003`
"'sensors' is not declared"; on the bench layout, a field that is not a member of `kfs_sensors_t` is
`KC-E003` "'…' has no member '…'".

# Modding notes

- Diagnostic numbers are integer literals at each call site; a mod that adds its own checks can call
  `DiagnosticBag.Error(prefix, number, span, message, hint)` with any prefix and number. Nothing
  validates them against `diagnostics.json` at emission time.
- `DiagnosticBag.MAX_ERRORS` (100) is a constant. Because `LimitException` and ordinary exceptions
  bypass the bag, a mod-induced failure inside header generation or lowering shows up as a thrown
  exception from `Compiler.Compile`, not as an entry in `CompileResult.Diagnostics`.
- User-facing display of diagnostics (editor squiggles, console) is covered in
  [editor window](/tooling/editor-window.md) and [build and load flow](/tooling/build-and-load-flow.md).

[^diagnostic-tostring]: Diagnostic.cs:3-9
[^diagbag-emit]: DiagnosticBag.cs:7-35
[^diag-json-rules]: KSA.Kfs.Vm.Tables.Data.diagnostics.json:5-130
[^sema-notyet]: Sema.cs:131-134
[^frontend-notyet]: KcFrontEnd.cs:50-53, 81
[^parser-undeclared]: Parser.cs:1856-1860, 2966-2970, 3516-3547
[^frontend-bus]: KcFrontEnd.cs:263-310
[^frontend-unused]: KcFrontEnd.cs:181-195
[^checks-unused]: Checks.cs:135-138
[^preprocessor-pragma]: Preprocessor.cs:824-858
[^parser-expect]: Parser.cs:207-226, 405-409, 853-857, 1358-1362, 3512-3557
[^preprocessor-errors]: Preprocessor.cs:421-484, 515-518, 639-641, 659-752, 971-1009, 1204
[^lexer-errors]: Lexer.cs:95-98, 167-196, 219
[^parser-misplaced]: Parser.cs:2274-2277, 2305-2308, 2327-2338
[^preprocessor-directive]: Preprocessor.cs:606-611, 636
[^preprocessor-include]: Preprocessor.cs:809-813
[^parser-declare]: Parser.cs:910-913, 968-971, 1057-1060, 1079-1105, 1990-1994
[^frontend-link]: KcFrontEnd.cs:269-275, 343-346, 441-506
[^parser-literals]: Parser.cs:1030-1033, 1267-1271, 1794-1797, 1828-1832, 1869-1873, 3439-3449, 3566-3570, 3660-3737
[^sema-w104]: Sema.cs:312-317, 1123-1126
[^parser-constexpr]: Parser.cs:591-619, 1254-1300, 1690, 1729, 2702-2707
[^parser-switch]: Parser.cs:2260-2268, 2287-2290, 2312-2315
[^parser-staticassert]: Parser.cs:581-588
[^sema-w202]: Sema.cs:318-321, 1127-1130
[^parser-w203]: Parser.cs:866-878
[^checks-w204]: Checks.cs:142-159
[^checks-w206]: Checks.cs:392-399
[^parser-w207]: Parser.cs:2388-2421
[^sema-const]: Sema.cs:972-1024, 1343-1361
[^sema-e211]: Sema.cs:256, 287, 425-455, 492, 511-557, 642-743, 1206, 1253-1267, 1286, 1328-1334; Parser.cs:2247-2251, 2350-2359, 2690-2694
[^parser-incomplete]: Parser.cs:963-967, 1312-1316, 1496-1500, 1608-1636, 2521-2547, 2913-2935
[^sema-w250]: Sema.cs:775-784
[^checks-e301]: Checks.cs:109-119
[^parser-builtins]: Parser.cs:3401-3413
[^parser-fnptr]: Parser.cs:958-962, 1327-1349, 1491-1495, 3041-3045; Sema.cs:227-231, 1345-1349
[^sema-call]: Sema.cs:1410-1424; Parser.cs:3115-3118, 3236-3239, 3359-3380
[^lowering-w307]: Lowering.cs:130-135
[^frontend-entries]: KcFrontEnd.cs:529-636; Parser.cs:413-493
[^parser-main]: Parser.cs:1589-1592
[^frontend-e501]: KcFrontEnd.cs:204-244
[^parser-logf]: Parser.cs:3120-3125, 3233-3349
[^parser-sleep]: Parser.cs:3140-3186
[^parser-6xx]: Parser.cs:761-785, 953-957, 1245-1252, 1375-1378, 1462, 1593-1597, 2130-2134, 2364-2375, 3407-3413, 3463-3466, 3474-3511
[^sema-e644]: Sema.cs:263-271
[^sema-w648]: Sema.cs:495-498
[^checks-w711]: Checks.cs:506-515
[^sema-e716]: Sema.cs:808-812, 1055-1058, 1442-1445, 1457-1460; Lowering.cs:533-541, 777-780; KcFrontEnd.cs:742-745
[^parser-e717]: Parser.cs:2032-2035, 2548-2552, 2737-2741; Lowering.cs:125-128, 148-151
[^parser-limits]: Parser.cs:228-244, 2583-2589; Preprocessor.cs:236-239, 785-792, 897-911, 915-918
[^compiler-limit]: Compiler.cs:76-101
[^frontend-e726]: KcFrontEnd.cs:738-741; Parser.cs:1267-1271, 1318-1322
[^compiler-e900]: Compiler.cs:48-53, 108-120
[^workbench-e900]: Workbench.cs:551-555
[^busanalysis-codes]: BusAnalysis.cs:108-114, 174-186, 217-221
[^emitter-codes]: Emitter.cs:76, 93, 99-143, 199, 247, 273, 345, 415, 434
