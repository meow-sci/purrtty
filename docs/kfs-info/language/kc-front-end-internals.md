---
type: Subsystem Reference
title: kC front end internals
description: How KSA.Kfs.KC turns source into IR — the Lexer, Preprocessor, Parser, Sema, Checks and Lowering phases, their data structures, where names and builtins are resolved, and which IrBuilder calls lowering makes.
resource: ksa-decomp:KSA.Kfs.KC/KcFrontEnd.cs
tags: [kfs, language, kc, compiler, front-end, parser, sema, lowering, internals]
sources:
  - id: frontend
    resource: ksa-decomp:KSA.Kfs.KC/KcFrontEnd.cs
    title: KcFrontEnd (decompiled)
  - id: compiler
    resource: ksa-decomp:KSA.Kfs.Compiler/Compiler.cs
    title: Compiler (decompiled)
  - id: ifrontend
    resource: ksa-decomp:KSA.Kfs.Compiler/IFrontEnd.cs
    title: IFrontEnd (decompiled)
  - id: input
    resource: ksa-decomp:KSA.Kfs.Compiler/CompileInput.cs
    title: CompileInput (decompiled)
  - id: lexer
    resource: ksa-decomp:KSA.Kfs.KC/Lexer.cs
    title: Lexer (decompiled)
  - id: token
    resource: ksa-decomp:KSA.Kfs.KC/Token.cs
    title: Token and TokKind (decompiled)
  - id: preprocessor
    resource: ksa-decomp:KSA.Kfs.KC/Preprocessor.cs
    title: Preprocessor (decompiled)
  - id: parser
    resource: ksa-decomp:KSA.Kfs.KC/Parser.cs
    title: Parser (decompiled)
  - id: sema
    resource: ksa-decomp:KSA.Kfs.KC/Sema.cs
    title: Sema (decompiled)
  - id: node
    resource: ksa-decomp:KSA.Kfs.KC/Node.cs
    title: Node and NK (decompiled)
  - id: obj
    resource: ksa-decomp:KSA.Kfs.KC/Obj.cs
    title: Obj (decompiled)
  - id: unit
    resource: ksa-decomp:KSA.Kfs.KC/TranslationUnit.cs
    title: TranslationUnit (decompiled)
  - id: checks
    resource: ksa-decomp:KSA.Kfs.KC/Checks.cs
    title: Checks (decompiled)
  - id: lowering
    resource: ksa-decomp:KSA.Kfs.KC/Lowering.cs
    title: Lowering (decompiled)
  - id: irbuilder
    resource: ksa-decomp:KSA.Kfs.Compiler.Builder/IrBuilder.cs
    title: IrBuilder (decompiled)
  - id: workbench
    resource: ksa-decomp:KSA.Kfs.Host.Workbench/Workbench.cs
    title: Workbench (decompiled)
  - id: offline-harness
    resource: /modding/offline-harness.md
    title: Offline harness experiments (E-numbered results)
generated: { by: claude-code/claude-fable-5-2, at: 2026-11-25T21:30:48Z }
status: stable
ksa_build: "2026.11.25.5949"
---

# Entry point

The compiler core knows the front end only through an interface and a factory property:[^ifrontend-decl][^compiler-factory]

```csharp
public interface IFrontEnd { bool Lower(CompileInput input, IrModule module, DiagnosticBag diags); }

public static class Compiler {
    public static Func<IFrontEnd>? KcFrontEnd { get; set; }
    public static CompileResult Compile(CompileInput input);            // runs on a worker thread
    public static CompileResult CompileOnThisThread(CompileInput input);
}
```

`KSA.Kfs.KC.KcFrontEnd.Register()` assigns `Compiler.KcFrontEnd = () => new KcFrontEnd()`. It is
marked `[ModuleInitializer]` and is also called from the `Workbench` constructor.[^frontend-register][^workbench-register]
`KcFrontEnd` is the only `IFrontEnd` implementation in the decompiled assemblies. A fresh instance is
created for every compile; if the property is null the compiler throws
`InvalidOperationException("no kC front end is registered (KSA.Kfs.KC.Register())")`.[^compiler-factory]

`Compile` starts a background thread named "kFS compile" with a 16 MiB stack, waits up to
`CompileOptions.TimeLimitMs`, and rethrows any exception from that thread on the caller. A
`LimitException` raised anywhere in the front end is converted into one diagnostic with that
exception's number.[^compiler-thread]

What happens after `Lower` returns (IR validation, peephole, emission) is in
[compile pipeline](/compiler/compile-pipeline.md).

# Pipeline

```text
KcFrontEnd.Lower(input, module, diags)
 ├─ new Sema(diags) { Tables, CoreClass, CoreName, CategoryCycles }        one instance for all units
 ├─ Assembler(ModuleOnly) over the project's *.kasm  → module functions/globals, X.kasm.h texts
 ├─ VehicleHeader.Generate(layout, false, tables)                         text of <vehicle.h>
 ├─ for each unit: *.c in ordinal order, then queued library units (<math.c>, <vecmat.c>)
 │    ├─ new Preprocessor(...) ; Predefine ; Run(file, text)  → List<Token>
 │    └─ new Parser(tokens, diags, sema, unit, partIndex, systemFiles, doubleBits).ParseUnit()
 │         (the parser calls Sema to build typed, constant-folded Node trees as it goes)
 ├─ Assembler over the library's string.kasm, if a real mem* call exists
 ├─ Link            resolve names across units (Obj.Resolved, Obj.IsKasm)
 ├─ Entries         tick_hz, init/task pragmas → Obj.Entry, RateDivisor, Priority
 ├─ Bus             publish/subscribe pragmas → module.Bus ; claim → module.Claims
 ├─ ServiceOwnership  KC-E501
 ├─ Checks.Recursion ; Checks.Function for every function with a body
 ├─ unused-name warnings
 └─ LowerModule     globals → IrGlobal ; Lowering.Function per function → IrFunction ; PruneLibrary
```

Source: [^frontend-lower]. Parsing stops early when the diagnostic bag is full (100 errors); lowering
is skipped when any error has been reported.

# Lexer

`Lexer(file, text).Lex()` returns a `List<Token>` ending in an `Eof` token.[^lexer-lex]

```csharp
public sealed record Token(TokKind Kind, string Text, Span At, bool SpaceBefore, bool LineStart) {
    public HashSet<string>? NoExpand { get; init; }   // macro hide set
}
public enum TokKind { Ident, Number, Char, String, Punct, Eof, Placemarker }
```

Source: [^token-decl]. Keywords are not distinguished by the lexer: they are `Ident` tokens that the
parser recognises by text. `Span` is `(File, Line, Col)`. `LineStart` and `SpaceBefore` exist for the
preprocessor (directive detection, function-like macro detection, stringising). `Placemarker` is used
only inside `##` processing. Static helpers `ParseInt`, `ParseFloat` and `Unescape` turn token text
into values and are shared with the preprocessor's `#if` evaluator.[^lexer-helpers] Lexical errors are
thrown as `LexException(number, message, span)`.

# Preprocessor

One instance per translation unit; see [kC preprocessor](/language/kc-preprocessor.md) for behaviour.
Outputs used by the rest of the front end:[^preprocessor-api]

| member | use |
| --- | --- |
| `List<Token> Run(path, text)` | the unit's token stream |
| `FilesRead` — `(Path, System)` pairs | registers source files and their debug language in the module; drives library queuing; the `System` flag builds the set of system files handed to the parser |
| `KfsPragmas` | every `#pragma kfs` with its span (the parser re-reads them from the token stream instead) |
| `Unavailable` callback | lets the front end veto an include name with a specific diagnostic (`<matn.h>`) |

`#pragma kfs a b c` is passed down as the tokens `__kfs_pragma__ a b c @`.

# Parser

`Parser` is a one-pass recursive-descent parser that performs name lookup and type checking **while
parsing**: every expression production calls a `Sema` method that returns a typed `Node`. There is no
separate untyped AST.[^parser-fields]

State:

| field | content |
| --- | --- |
| `_fileScope`, `_scopes` | ordinary identifiers → `Obj`, file scope plus a stack of block scopes |
| `_tags`, `_tagScopes` | `"struct name"` / `"enum name"` → `CType` |
| `_fn`, `_loopDepth`, `_switchDepth`, `_switches` | the function being parsed and its control context |
| `_depth`, `_ops` | nesting and operator counters for the translation limits |
| `_strings` | string-literal pool of the unit (by content) |
| `_enumerators` | enum type → enumerators, for the `switch` coverage warning |
| `_systemFiles` | names of files included with `<…>`; `InSystemHeader` is "the current token's file is in this set" |
| `PendingEntries` | `(Name, Kind, Divisor, RateHz, Priority, At)` for each `init`/`task` pragma, resolved later by `KcFrontEnd.Entries` |
| static `_bindings`, `_bindingsFor` | service-name dictionary and the `GameTables` it was built from |

**Error recovery.** A syntax error reports `KC-E012` and throws the internal `ParseError`; the
catcher skips to the next `;` or matching `}` (`Resync`) and continues. `SkipDeclaration` abandons one
external declaration silently (used after a K&R definition). `LimitException` is never caught
here.[^parser-recovery]

**Name resolution** happens at the moment an identifier is parsed (`Lookup` walks block scopes
outward, then file scope), which is why declaration before use is required. A typedef name is
recognised as starting a declaration when it is followed by anything other than `=`.[^parser-lookup]

**Declarations** create `Obj` records and merge redeclarations in `Declare`: compatible
redeclarations are folded into the first `Obj` and the newer one's `Resolved` points at it.[^parser-declare]

**Builtins and services** are intercepted in the postfix-call production: if the callee `Obj` has
`IsBuiltin`, `BuiltinCall` runs first and may return a special node (`BuiltinSqrtf`, `Syscall`, …);
otherwise `Sema.Call` builds an ordinary `Call` node and the callee is added to the current function's
`Callees` list (the call graph used for the recursion check).[^parser-postfix]

# Sema

`Sema` is a node factory with the language's typing rules; it has no symbol tables of its own.
Public methods, each returning a `Node`:[^sema-api]

| method | builds |
| --- | --- |
| `Num`, `FNum`, `Var` | leaves; `Var` on an enumerator returns a `Num`; `Var` also records use counts and bus reads |
| `Rvalue` | array-to-pointer decay; rejects a function used as a value |
| `Cast`, `ConvertAssign`, `ToBool`, `Condition` | conversions (with constant folding and the conversion warnings) |
| `Unary`, `Binary`, `Conditional`, `Comma` | operators, folding constants |
| `Assign`, `AssignOp`, `IncDec` | mutation, via `CheckModifiable` |
| `Index`, `Member`, `Deref`, `AddrOf` | access paths |
| `Call`, `Builtin`, `Classify`, `MemBuiltin` | calls and intrinsics |
| static `Normalize`, `AddressConstant`, `RootObject`, `OpText` | helpers |

Context properties the parser and front end set: `InSystemHeader`, `InLibrary`, `CurrentFunction`,
`CurrentFunctionObj`, `ReturnType`, `NoEval` (inside `sizeof`), `Tables`, `CoreClass`, `CoreName`,
`CategoryCycles`. `NotYet(span, what, milestone)` reports the milestone error `KC-E000`.[^sema-context]

**Bus tracking.** While a function is being parsed, `Var` adds every referenced file-scope object to
`CurrentFunctionObj.BusReads`, and `CheckModifiable` / pointer casts add written ones to `BusWrites`
and `BusMarks`. These later become `IrFunction.BusReads`, `BusWrites`, `BusMarks` and
`CommandWrites`, the inputs of [bus analysis](/compiler/bus-analysis.md).[^sema-bus][^frontend-busir]

# Data structures

## `Node` and `NK`

`Node` is one mutable class used for both expressions and statements; which fields are meaningful
depends on `Kind`.[^node-fields]

| `NK` | meaning | fields used |
| --- | --- | --- |
| `Num`, `FNum` | integer / floating constant | `IVal` / `FVal`, `IsConst` |
| `Var` | reference to a declared object or string literal | `Var`, `IsLvalue`, `Str` (literal text) |
| `Index` | `L[R]` on an array | `L`, `R`, `NoBounds` |
| `Member` | `L.name` | `L`, `Str`, `IVal` = offset |
| `Deref`, `AddrOf` | `*L`, `&L` | `L` |
| `PtrAdd` | pointer ± integer | `L`, `R`, `IVal` = element size, `Op` = `Add`/`Sub` |
| `PtrDiff` | pointer − pointer | `L`, `R`, `IVal` = element size |
| `CompoundLit` | `(T){…}` | `Var` = hidden local |
| `Cast` | conversion to `Type` | `L`, `CharSignednessDependent` |
| `Neg`, `BitNot`, `LogNot`, `Plus` | unary operators | `L` |
| `Add` `Sub` `Mul` `Div` `Mod` `BitAnd` `BitOr` `BitXor` `Shl` `Shr` | binary arithmetic | `L`, `R`, `OpType` |
| `Eq` `Ne` `Lt` `Le` `Gt` `Ge` | comparisons (type `int`) | `L`, `R`, `OpType` = operand type |
| `LogAnd`, `LogOr` | short-circuit | `L`, `R` |
| `Cond` | `?:` | `Cond`, `Then`, `Else` |
| `Assign`, `AssignOp` | `=`, `op=` | `L`, `R`, `Op`, `OpType`, `IVal` |
| `PreInc` `PreDec` `PostInc` `PostDec` | | `L`, `IVal` = element size for pointers |
| `Comma` | | `L`, `R` |
| `Call` | direct call | `Var` = callee, `Args` |
| `BuiltinSqrtf`, `BuiltinFabsf`, `BuiltinIsNan`, `BuiltinIsInf` | math intrinsics (the type says float or double) | `L` |
| `BuiltinAssert` | `assert` | `L` = condition |
| `BuiltinMem` | inline `memcpy`/`memmove`/`memset` | `Var`, `Args`, `IVal` = length |
| `Syscall` | service request | `IVal` = service id, `Args`, `Str` = format or channel, `CaseValue` = 0 plain / 1 log / 2 trace |
| `Block` | `{ … }` | `Stmts` |
| `ExprStmt`, `Decl`, `Null` | statements | `L`; `Decls` |
| `If`, `While`, `Do`, `For` | | `Cond`, `Then`, `Else`, `Init`, `Inc`, `Body`, `Bound` |
| `Switch`, `Case`, `Default` | | `Cond`, `Body`, `Cases`, `Default`, `OpType`; `CaseValue` |
| `Break`, `Continue`, `Return` | | `L` for `return` |

Source: [^node-nk]. Every node also has `Type`, `At`, a per-compile `Id`, `Parens` (was parenthesised)
and `IsLvalue`.

## `Obj`

One `Obj` per declared name (variable, function, typedef, enumerator, parameter, string literal,
compound-literal temporary).[^obj-fields]

| group | fields |
| --- | --- |
| identity | `Name`, `LinkName`, `Type`, `At`, `File`, `DeclIndex` |
| kind flags | `IsLocal`, `IsParam`, `IsFunction`, `IsTypedef`, `IsEnumConstant`, `IsStringLiteral`, `IsCompoundTemp` |
| linkage | `IsStatic`, `IsExtern`, `IsDefinition`, `IsInline`, `Resolved` (the defining `Obj` after merging/linking), `IsKasm` (defined by kASM), `IsBuiltin` |
| data | `HasInit`, `InitData`, `InitRelocs`, `LocalInit`, `LocalInitZero`, `EnumValue`, `AtAddress` |
| function | `Params`, `Locals`, `Body`, `Callees`, `Optimize`, `BoundsChecks`, `Slot` |
| entry | `Entry`, `RateDivisor`, `Priority`, `EntryAt` |
| analysis | `Uses`, `FirstUse`, `AddressTaken`, `BusReads`, `BusWrites`, `BusMarks` |

## `TranslationUnit`

`File`, `Globals`, `Functions`, `Strings`, `TickHz`/`TickHzAt`, `Optimize`, `BoundsChecks`,
`Double32Warned`, `IsLibrary`, `BusPragmas` `(Publish, Message, Buffer, At)`, `Claims`, and
`ServiceCalls` `(Id, Name, At)`.[^unit-fields]

# Link, Entries, Bus

These `KcFrontEnd` steps are specified from the language side in
[kC program structure](/language/kc-program-structure.md). Internally: `Link` groups every non-static,
non-builtin, non-`__at` object and function by name across units, picks the defining `Obj`, sets
`Resolved` on the others, and marks declarations satisfied by kASM with `IsKasm`.[^frontend-link]
It also checks that every `CALL`/`CALLF` target named by project kASM code exists (`KA-E304`).

# Checks

Run after linking, on every function that has a body:[^checks-all]

| check | algorithm | diagnostic |
| --- | --- | --- |
| `Recursion` | iterative Tarjan strongly-connected components over `Obj.Callees`; a component with more than one function, or a self-call, is a cycle | `KC-E301` with the chain `a → b → a` |
| `Uninitialised` | flow-sensitive walk tracking the set of definitely-assigned scalar locals through `if`/loops/`switch`/`&&`/`?:` | `KC-W206`, once per variable |
| `LoopBounds` | for each loop, `Bound(loop, previous statement)` tries to derive an iteration count and stores it in `Node.Bound` | `KC-W711` when it cannot |
| unused locals | `Uses == 0` on non-parameter locals | `KC-W006` |
| `CharSignedness` | reports nodes flagged `CharSignednessDependent`, once per source line | `KC-W204` |

For library functions only `LoopBounds` runs, silently, because the bounds are still needed
downstream.

# Lowering

`Lowering` generates stack-machine IR directly from the `Node` tree; there is no intermediate
three-address form. One `Lowering` object serves the whole module and is handed the global symbol map
built by `LowerModule`.[^lowering-function]

**Frame slots**, in the order `IrBuilder` requires (arguments, scalar locals, aggregates,
temporaries):

- a hidden pointer argument `.result` when the function returns an aggregate;
- each parameter: 4 bytes, or 8 for a 64-bit type; an aggregate parameter is a 4-byte pointer;
- each scalar local: a 4- or 8-byte slot (a `char` local still occupies 4 bytes);
- each aggregate local and compound literal: its exact size and alignment;
- temporaries on demand (`Temp`). Slot names are made unique with `.2`, `.3` suffixes.

**Access.** A scalar of at least 4 bytes held in a named slot or global is read and written directly
(`LDL`/`STL`, `LDG`/`STG`, their `64` forms, or an absolute `LDG`/`STG` for an `__at` object). Every
other access computes an address — `ADDRL`, a symbol or absolute `PUSH32`, `+ offset`, `BOUNDS n` and
`INDEX size` — and then uses the typed load/store.[^lowering-address]

**Statements.** `if`/loops lower to labels and `JZ`/`JNZ`/`JMP`; conditions are compiled by `CondJump`
so that `&&`, `||` and `!` branch without materialising 0/1. Each loop is bracketed by
`BeginLoop(bound, "proven" | "none")` and `EndLoop()` markers. `switch` stores the controlling value
in a temporary and emits one compare-and-`JNZ` per `case` in source order, then a jump to `default`
or the end.[^lowering-stmt]

**Calls.** Arguments are pushed left to right; a 64-bit argument is preceded by a zero pad cell when
the cell count so far is odd; an aggregate argument is copied to a temporary whose address is pushed.
`IrBuilder.Call(name, argCells, resultCells)` emits `CALL` to the function by link name.
`return` of a scalar is `RET 1` or `RET 2`; of an aggregate, a `COPY` into `.result` and
`RET 0`.[^lowering-call]

**IR builder calls made by kC lowering** (the complete list): `Argument`, `Local`, `Aggregate`,
`Temp`, `Loc`, `Emit(op)` / `Emit(op, imm)`, `PushInt`, `PushInt64`, `PushFloat`, `PushDouble`,
`PushAddress`, `LoadAt`, `StoreAt`, `LoadLocal`, `StoreLocal`, `AddressOfLocal`, `LoadGlobal`,
`StoreGlobal`, `AddressOfGlobal`, `Label`, `Mark`, `Jump`, `BeginLoop`, `EndLoop`, `Call`, `Syscall`,
`Trap`, `Ret`, `Finish`, plus the properties `At`, `Unreachable`, `NeedsTerminator`. `BeginScope`,
`EndScope` and `PushFloatBits` exist on the builder but are not used by kC.[^irbuilder-api]

**Opcodes emitted through `Emit`**: the arithmetic, comparison, conversion, load/store and stack
families (`ADD`…`XOR64`, `FADD`…`DSQRT`, `I2F`…`LU2D`, `LDB`…`ST64`, `SEXT8`…`ZEXT16`, `DUP`, `DUP2`,
`DROP`, `DROP2`) and the structured ones `BOUNDS`, `INDEX`, `COPY`, `ZERO`. Branches go through
`Jump` (`JMP`, `JZ`, `JNZ`). The IR node types and the builder are documented in
[IR reference](/compiler/ir-reference.md); opcodes in [opcodes](/vm/opcodes.md).

`LowerModule` creates the `IrGlobal`s (name, size, alignment, `Const`, initial bytes, relocations,
debug type), rejects objects over 1 MiB (`KC-E726`), assigns link names to `static` functions, lowers
each function, attaches the bus sets, and finally prunes unreachable library code.[^frontend-lowermodule]

# Failure modes that are not diagnostics

- `InvalidOperationException` from `Lowering` (for example a service whose prototype does not match
  the table's cell count, or "not an lvalue"/"not an expression" on an unexpected node) and from
  `VehicleHeader.Build` propagate out of `Compile` to the caller.
- The IR validator and the builder's own invariants turn front-end bugs into `KC-E900` "internal
  compiler error" or exceptions ([compile pipeline](/compiler/compile-pipeline.md)).
- `Verified:` a service prototype returning a struct (`vec3` against a three-cell `result_types` row)
  ends in an `InvalidOperationException` thrown out of the compile, not a
  diagnostic, whose text depends on the code around the call ("label Lend0 reached by fall-through
  with height 2 and by a jump with 0 ('…', builder invariant)" or "RET 0 with 2 cells on the stack in
  '…' (builder invariant)"). So do a prototype that returns a value for a row without a result and a
  `void` prototype for a row with one.[^offline-harness][^irbuilder-api]

# Modding notes

**Visibility.** Public: `KcFrontEnd`, `Lexer`, `Preprocessor`, `Parser`, `Sema`, `Checks`, `Lowering`,
`CType`, `Node`, `Obj`, `Token`, `TranslationUnit`, `StructField`, `LexException` (all `sealed`), the
static classes `SystemHeaders`, `GeneratedHeaders`, `VehicleHeader`, `Library`, and the enums `NK`,
`TK`, `TokKind`. Internal: `ParseError`, `SkipDeclaration`, `TypeExtensions`. Almost all behaviour
lives in **private instance methods**, so Harmony patches must target them by name.

**The front-end factory is a public settable static.** A mod can install its own `IFrontEnd` — for
instance a thin wrapper that adjusts the `CompileInput` (adds `SystemFiles`, `Options.Defines`) and
delegates to a new `KcFrontEnd` — without patching any method:

```csharp
Compiler.KcFrontEnd = () => new MyWrapper();   // MyWrapper : IFrontEnd
```

Two cautions: `KcFrontEnd.Register()` overwrites the property, and it runs from the `Workbench`
constructor as well as at module initialisation, so the assignment must be made (or re-made) after the
workbench exists; and `CompileInput` is a record with init-only properties, so the wrapper passes a
`with`-copy.[^workbench-register][^input-record] Adding callable services does not need this
wrapper: the tested recipe is a header in `SystemHeaders.Files` plus names in
`SystemHeaders.Builtins` — see [adding a service](/modding/adding-a-service.md).

**Statics shared across compiles:** `Parser._bindings`/`_bindingsFor` (service names),
`CType.StructDef._next` (struct ids), and the header caches described in
[kC generated headers](/language/kc-generated-headers.md). `CType._comparing` is `[ThreadStatic]`.
Compiles run one at a time per `Compile` call, each on its own thread. Of these statics only
`Parser._bindings`/`_bindingsFor` is read and written without synchronisation: `StructDef._next` is
advanced with `Interlocked.Increment` and `VehicleHeader.Generate` locks its cache entry.

**Useful observation points:** `KcFrontEnd.Units` (public, all translation units of the last `Lower`
on that instance), `TranslationUnit.ServiceCalls`, `TranslationUnit.BusPragmas`, and the `IrModule`
returned in `CompileResult.Module`.

[^ifrontend-decl]: IFrontEnd.cs:5-8
[^compiler-factory]: Compiler.cs:24, 61-68
[^compiler-thread]: Compiler.cs:20-59, 76-101
[^frontend-register]: KcFrontEnd.cs:25-29
[^frontend-lower]: KcFrontEnd.cs:31-202
[^frontend-link]: KcFrontEnd.cs:417-527
[^frontend-busir]: KcFrontEnd.cs:779-789
[^frontend-lowermodule]: KcFrontEnd.cs:638-802
[^workbench-register]: Workbench.cs:364-367
[^input-record]: CompileInput.cs:6-21
[^lexer-lex]: Lexer.cs:42-220
[^lexer-helpers]: Lexer.cs:268-501
[^token-decl]: Token.cs:6-14
[^preprocessor-api]: Preprocessor.cs:334-366, 824-858
[^parser-fields]: Parser.cs:37-162
[^parser-recovery]: Parser.cs:320-393, 2063-2074
[^parser-lookup]: Parser.cs:268-278, 621-679, 3516-3553
[^parser-declare]: Parser.cs:1066-1168
[^parser-postfix]: Parser.cs:3026-3061
[^sema-api]: Sema.cs:169-233, 240, 420, 464, 487, 502, 602, 1044-1547
[^sema-context]: Sema.cs:22-45, 131-134
[^sema-bus]: Sema.cs:47-104, 198-202, 1018
[^node-fields]: Node.cs:6-67
[^node-nk]: NK.cs:3-68
[^obj-fields]: Obj.cs:7-101
[^unit-fields]: TranslationUnit.cs:6-33
[^checks-all]: Checks.cs:20-140, 220-232, 497-531
[^lowering-function]: Lowering.cs:39-163
[^lowering-address]: Lowering.cs:512-519, 728-926
[^lowering-stmt]: Lowering.cs:194-458, 1758-1808
[^lowering-call]: Lowering.cs:350-371, 1293-1331
[^irbuilder-api]: IrBuilder.cs:87-160, 203-438
[^offline-harness]: Experiments E8 and E19 in [the offline harness](/modding/offline-harness.md).
