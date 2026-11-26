---
type: Language Reference
title: kC preprocessor
description: The kC preprocessor — directives, macro expansion rules, #if evaluation, predefined macros, #pragma handling, and exactly where an #include name is resolved.
resource: ksa-decomp:KSA.Kfs.KC/Preprocessor.cs
tags: [kfs, language, kc, preprocessor, include, macros, pragma]
sources:
  - id: preprocessor
    resource: ksa-decomp:KSA.Kfs.KC/Preprocessor.cs
    title: Preprocessor (decompiled)
  - id: frontend
    resource: ksa-decomp:KSA.Kfs.KC/KcFrontEnd.cs
    title: KcFrontEnd (decompiled)
  - id: sysheaders
    resource: ksa-decomp:KSA.Kfs.KC/SystemHeaders.cs
    title: SystemHeaders (decompiled)
  - id: parser
    resource: ksa-decomp:KSA.Kfs.KC/Parser.cs
    title: Parser (decompiled)
  - id: input
    resource: ksa-decomp:KSA.Kfs.Compiler/CompileInput.cs
    title: CompileInput (decompiled)
  - id: workspace
    resource: ksa-decomp:KSA.Kfs.Host.Workbench/Workspace.cs
    title: Workspace (decompiled)
  - id: workbench
    resource: ksa-decomp:KSA.Kfs.Host.Workbench/Workbench.cs
    title: Workbench (decompiled)
generated: { by: claude-code/claude-fable-5-2, at: 2026-11-25T21:30:48Z }
status: stable
ksa_build: "2026.11.25.5949"
---

# Overview

Each translation unit gets a fresh `Preprocessor`. It lexes the whole file, walks the token list
line by line, handles directives, expands macros, and returns one flat token list for the parser.
Macros, `#pragma once` state and counters live for one translation unit only — a macro defined in
`a.c` is not visible in `b.c`.[^preprocessor-ctor][^frontend-loop]

A directive is a `#` that is the first token on a line. A line with only `#` is a null directive and
is ignored.[^preprocessor-process]

# Directives

| directive | behaviour |
| --- | --- |
| `#define NAME body` | object-like macro[^preprocessor-define] |
| `#define NAME(a, b) body` | function-like macro; the `(` must touch the name |
| `#define NAME(a, ...) body` | variadic; the extra arguments are `__VA_ARGS__` |
| `#undef NAME` | removes a macro; no error if absent[^preprocessor-directive] |
| `#include "file"` / `#include <file>` | see "Include resolution" |
| `#if expr`, `#ifdef NAME`, `#ifndef NAME`, `#elif expr`, `#else`, `#endif` | conditional inclusion[^preprocessor-conditionals] |
| `#error text` | `KC-E014` with the text[^preprocessor-directive] |
| `#warning text` | `KC-W017` with the text |
| `#pragma once` | include the current file at most once per unit |
| `#pragma kfs …` | passed to the parser; see [kC program structure](/language/kc-program-structure.md) |
| `#pragma` anything else | `KC-W010` "unknown pragma '…'; it has no effect in kC"[^preprocessor-pragma] |
| `#line N` / `#line N "file"` | renumbers following lines; `N` must be 1 … 2147483647 (`KC-E727`)[^preprocessor-directive] |
| any other name | `KC-E012` "unknown directive #name" |

Directives in a skipped conditional group are not examined, except the conditional directives
themselves.[^preprocessor-process] There is no `#include_next`, `#elifdef`, `#embed` or
`__has_include`.

Errors in conditional structure are all `KC-E012`: "#elif without #if", "#elif after #else", "#else
without #if", "#else after #else", "#endif without #if", and "#if without #endif" at end of
file.[^preprocessor-conditionals]

# Macros

## Definition

- A macro named `defined` is rejected.[^preprocessor-define]
- Parameter names must be unique; `...` may appear in the list and contributes the parameter
  `__VA_ARGS__`.
- `##` may not be the first or last token of a body (`KC-E012`).
- Redefining a macro with a **different** body is `KC-E016` "'NAME' is already defined with a
  different body". An identical redefinition is fine. Redefining a *predefined* macro is always
  allowed (no diagnostic).[^preprocessor-redefine]

## Expansion

- A function-like macro name not followed by `(` is left alone.[^preprocessor-expand]
- A macro invocation's arguments may continue on following lines; expansion stops pulling lines at a
  directive line.[^preprocessor-nextline]
- Arguments are fully macro-expanded before substitution, except where they are operands of `#` or
  `##`.[^preprocessor-substitute]
- `#param` stringises the argument's spelling, inserting one space where the tokens were separated by
  white space and escaping `"` and `\` inside string and character literals.[^preprocessor-stringise]
- `a ## b` pastes tokens; the result must lex as exactly one token or it is `KC-E012` "pasting 'a'
  and 'b' doesn't give a valid token". An empty argument pastes as a placemarker.[^preprocessor-paste]
- A variadic macro may be called with its variadic part omitted (it expands as empty).[^preprocessor-expand]
- Recursion is blocked with per-token hide sets: a macro's name is not expanded again inside its own
  expansion.[^preprocessor-expand]
- Calling a macro with the wrong number of arguments is `KC-E012` "macro 'M' takes N argument(s); K
  given".

Built-in dynamic names: `__LINE__` (decimal line number of the token) and `__FILE__` (a string
literal of the file name as the compiler knows it, e.g. `"<kfs.h>"` inside a system
header).[^preprocessor-linefile] `_Pragma("…")` is recognised during expansion and handled as the
equivalent `#pragma`.[^preprocessor-upragma]

## Predefined macros

Defined for every translation unit before it is read:[^frontend-predefine]

| macro | value |
| --- | --- |
| `__KFS__` | `1` |
| `__KC__` | `100` |
| `__KFS_TIER__` | the target part's `PartIndex` (0 is the Sequencer) |
| `__KFS_CORE__` | the target part's core class, 0–3 (compare with `KFS_CORE_K8` … `KFS_CORE_K32D` from `<kfs.h>`) |
| `__STDC__` | `1` |
| `__STDC_VERSION__` | `199901L` |
| `__STDC_HOSTED__` | `0` |
| `__DATE__` | `"??? ?? ????"` |
| `__TIME__` | `"??:??:??"` |
| each entry of `CompileOptions.Defines` | `NAME` → `1`, or `NAME=value` → `value` |

`__DATE__` and `__TIME__` are fixed placeholder strings, so builds are reproducible.
`CompileOptions.Defines` is filled only by the command-line tools' `-D` option (`KSA.Kfs.Cli`,
unreachable in the shipped game — see [CLI](/tooling/cli.md)); the in-game workbench
passes no options.[^workbench-compileinput] `__KFS_HOST__` is **not** defined by this compiler; the
generated headers test it so the same text can serve a native "host build" (see
[kC generated headers](/language/kc-generated-headers.md)).

# `#if` expressions

After `defined NAME` / `defined(NAME)` are replaced by `1`/`0` and macros are expanded, the
expression is evaluated by a small recursive evaluator:[^preprocessor-evalcondition][^preprocessor-condeval]

- Operators, lowest to highest precedence: `?:`, `||`, `&&`, `|`, `^`, `&`, `== !=`,
  `< > <= >=`, `<< >>`, `+ -`, `* / %`, then unary `- + ! ~` and parentheses.
- Arithmetic is 64-bit. The evaluator has one sticky "unsigned" flag that is set as soon as any
  literal with a `u` suffix has been read; from then on comparisons, `>>`, `/` and `%` use unsigned
  semantics for the rest of that expression.
- An identifier that survives expansion evaluates to `0`.
- A character constant evaluates to its signed byte value.
- Both operands of `&&` and `||` are always evaluated, so `#if defined(X) && (1 / X)` reports
  "division by zero in #if" (`KC-E012`) when `X` is `0`, even though the result would not need it.
- A shift count outside 0…63 yields 0.
- Floating literals and anything that is not an integer are `KC-E012` "'…' is not an integer constant
  in #if".
- Nesting deeper than 128 is `KC-E720`.

# Include resolution

`#include` takes exactly `"file"` or `<file>` (a macro that expands to one of those is **not**
expanded; the directive's tokens are used as written).[^preprocessor-include]

The front end hands each `Preprocessor` two resolver callbacks:[^frontend-resolvers]

| form | lookup order |
| --- | --- |
| `#include "name"` | 1. the project's files (`CompileInput.Files[name]`); 2. headers the build generated from kASM sources (`name.kasm.h`); 3. everything `<name>` would find |
| `#include <name>` | 1. `CompileInput.SystemFiles[name]`; 2. if `name` is `vehicle.h`, the vehicle header generated for this build's I/O layout; 3. the built-in table `SystemHeaders.Files` |

Details that matter:

- **Names are exact dictionary keys.** There is no directory search and no path normalisation. A
  folder project is keyed by bare file names, so `#include "util.h"` works and
  `#include "sub/util.h"` does not.[^workspace-projectof]
- **`CompileInput.SystemFiles` is empty in every caller in this build** (the workbench and both
  command-line tools construct `CompileInput` without it), so in practice `<name>` means "generated
  `vehicle.h`, else a built-in header".[^input-systemfiles][^workbench-compileinput]
- The built-in system headers are `stdint.h`, `stdbool.h`, `stddef.h`, `limits.h`, `float.h`,
  `assert.h`, `math.h`, `string.h`, `stdlib.h`, `stdio.h`, `kfs.h`, `vecmat.h`.[^sysheaders-files]
  Their text is reproduced in [kC generated headers](/language/kc-generated-headers.md).
- `<matn.h>` is on a "deferred" list: including it is an error before any lookup — `KC-E520` "is not
  available on the Sequencer" when the target part index is 0, otherwise `KC-E000` "<matn.h> is not
  implemented until MS-G2".[^frontend-deferred][^sysheaders-deferred]
- A name found nowhere is `KC-E015` "can't find 'name'" with the hint "\"\" searches the code
  project, <> the library and generated headers".[^preprocessor-include]
- **Only the `<…>` form marks a file as a system file.** Being a system file is what permits `__at`,
  integer/pointer casts and builtin function declarations inside it, and it suppresses lint warnings.
  A quoted include that falls through to a built-in header is *not* treated as a system file, so
  `#include "kfs.h"` fails on the `__at` line. Always write `#include <kfs.h>`.[^preprocessor-filesread][^parser-insystem]
- A system file's name in diagnostics and `__FILE__` is `<name>`.[^preprocessor-include]
- Including `<math.h>` or `<vecmat.h>` anywhere in the program also schedules the matching library
  source for compilation; see [kC standard library](/language/kc-standard-library.md).[^frontend-libpending]

The workbench writes `vehicle.h` and `vehicle.kinc` into the project folder for the player to read,
but it leaves those two file names out of the project's file set when it builds, so a folder project
always gets the in-memory header even for `#include "vehicle.h"`.[^workspace-projectof] The
command-line compiler includes every `.h` in the folder in the project, so there a quoted include
would read the on-disk copy.

## Include guards and limits

`#pragma once` is honoured (keyed by the include form and name). Conventional `#ifndef` guards work
too. Without either, a self-including header hits a limit:[^preprocessor-include][^preprocessor-limits]

| limit | value | diagnostic |
| --- | --- | --- |
| include nesting depth | 32 | `KC-E722` "#include nested deeper than 32" |
| includes per translation unit | 1,000 | `KC-E723` |
| tokens emitted after preprocessing, per unit | 150,000 | `KC-E724` |
| tokens produced by macro substitution, per unit | 200,000 | `KC-E725` |
| macro expansion nesting | 128 | `KC-E720` |

These are raised as `LimitException` and abort the compile with one error.

# `#pragma kfs`

The preprocessor does not interpret `#pragma kfs …`. It records it and emits the token sequence
`__kfs_pragma__ <tokens> @` into the output stream, which the parser consumes at file scope
(`KC-E012` "`#pragma kfs` must be at file scope" inside a function).[^preprocessor-pragma][^parser-pragma]
Because the pragma's tokens are taken from the directive line unexpanded, **macros are not expanded
inside `#pragma kfs`** — `#pragma kfs task step rate_hz=RATE` does not work.

The directives (`tick_hz`, `init`, `task`, `optimize`, `bounds`, `publish`, `subscribe`, `claim`,
`strict`) are specified in [kC program structure](/language/kc-program-structure.md).

# Lexical recovery

If a file fails to lex (for example an unterminated comment), the error is reported and the file is
re-lexed leniently line by line, dropping lines that still fail, so that later diagnostics are still
produced.[^preprocessor-lenient]

# Modding notes

- `Preprocessor` is `public sealed`; its two resolvers are constructor arguments supplied by
  `KcFrontEnd.Lower` as closures. They are not replaceable from outside without patching `Lower`.
- The cheapest supported-looking injection point for extra headers is `CompileInput.SystemFiles`
  (an `IReadOnlyDictionary<string, string>` init property, checked **first** for `<name>` includes,
  so it can also shadow `kfs.h` or `vehicle.h`). No game code sets it, so a mod must intercept the
  `CompileInput` (for example a Harmony prefix on `KSA.Kfs.Compiler.Compiler.Compile(CompileInput)`
  that returns a `with`-copy carrying the extra dictionary).[^input-systemfiles][^frontend-resolvers]
- Alternatively `SystemHeaders.Files` is a `public static readonly Dictionary<string, string>` whose
  *contents* can be changed at run time; `SystemHeaders.Get` reads it on every include, with no
  cache.[^sysheaders-get] Header text is stored with `\r\n` and normalised to `\n` on read. This
  patch-free route is the one that has been run against the real `KSA.dll` (a `<purrtty.h>` entry,
  included with `<…>`); prefer it over intercepting `CompileInput` — see
  [adding a service](/modding/adding-a-service.md).
- Predefined macros come from `KcFrontEnd.Predefine` (private static). `CompileOptions.Defines` is the
  intended way to add more.[^frontend-predefine]

[^preprocessor-ctor]: Preprocessor.cs:318-366
[^preprocessor-process]: Preprocessor.cs:368-527
[^preprocessor-conditionals]: Preprocessor.cs:405-484, 515-518
[^preprocessor-directive]: Preprocessor.cs:586-643
[^preprocessor-define]: Preprocessor.cs:659-752
[^preprocessor-redefine]: Preprocessor.cs:742-746, 754-761
[^preprocessor-include]: Preprocessor.cs:763-822
[^preprocessor-filesread]: Preprocessor.cs:814-820
[^preprocessor-pragma]: Preprocessor.cs:824-858
[^preprocessor-evalcondition]: Preprocessor.cs:860-893
[^preprocessor-condeval]: Preprocessor.cs:28-308
[^preprocessor-limits]: Preprocessor.cs:785-792, 895-911, 913-918
[^preprocessor-expand]: Preprocessor.cs:913-1060
[^preprocessor-nextline]: Preprocessor.cs:529-546
[^preprocessor-substitute]: Preprocessor.cs:1073-1163
[^preprocessor-paste]: Preprocessor.cs:1165-1227
[^preprocessor-stringise]: Preprocessor.cs:1229-1261
[^preprocessor-linefile]: Preprocessor.cs:1036-1053
[^preprocessor-upragma]: Preprocessor.cs:1028-1035
[^preprocessor-lenient]: Preprocessor.cs:370-379, 557-584
[^frontend-loop]: KcFrontEnd.cs:72-153
[^frontend-resolvers]: KcFrontEnd.cs:61, 75-79
[^frontend-deferred]: KcFrontEnd.cs:81
[^frontend-predefine]: KcFrontEnd.cs:392-415
[^frontend-libpending]: KcFrontEnd.cs:103-115
[^sysheaders-files]: SystemHeaders.cs:8-22
[^sysheaders-deferred]: SystemHeaders.cs:32
[^sysheaders-get]: SystemHeaders.cs:34-41
[^parser-insystem]: Parser.cs:149, 1358-1362
[^parser-pragma]: Parser.cs:395-409, 2119-2124
[^input-systemfiles]: CompileInput.cs:10
[^workspace-projectof]: Workspace.cs:176-235
[^workbench-compileinput]: Workbench.cs:507-514
