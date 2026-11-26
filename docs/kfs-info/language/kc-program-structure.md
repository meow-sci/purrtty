---
type: Language Reference
title: kC program structure
description: How a kC program is assembled from files and declares its tick rate, init and periodic tasks, bus buffers and ownership through #pragma kfs, plus globals, linkage, symbol naming and per-program limits.
resource: ksa-decomp:KSA.Kfs.KC/KcFrontEnd.cs
tags: [kfs, language, kc, tasks, pragma, entry-points, linkage]
sources:
  - id: frontend
    resource: ksa-decomp:KSA.Kfs.KC/KcFrontEnd.cs
    title: KcFrontEnd (decompiled)
  - id: parser
    resource: ksa-decomp:KSA.Kfs.KC/Parser.cs
    title: Parser (decompiled)
  - id: unit
    resource: ksa-decomp:KSA.Kfs.KC/TranslationUnit.cs
    title: TranslationUnit (decompiled)
  - id: lowering
    resource: ksa-decomp:KSA.Kfs.KC/Lowering.cs
    title: Lowering (decompiled)
  - id: compiler
    resource: ksa-decomp:KSA.Kfs.Compiler/Compiler.cs
    title: Compiler (decompiled)
  - id: options
    resource: ksa-decomp:KSA.Kfs.Compiler/CompileOptions.cs
    title: CompileOptions (decompiled)
  - id: entrykind
    resource: ksa-decomp:KSA.Kfs.Compiler.Ir/EntryKind.cs
    title: EntryKind (decompiled)
  - id: catalogue
    resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.catalogue.json
    title: catalogue.json (embedded table)
  - id: msglayout
    resource: ksa-decomp:KSA.Kfs.Vm.Tables/MessageLayout.cs
    title: MessageLayout (decompiled)
  - id: examples
    resource: ksa-decomp:KSA.Kfs.Host.Workbench/Examples.cs
    title: Examples (decompiled)
  - id: assembler
    resource: ksa-decomp:KSA.Kfs.KAsm/Assembler.cs
    title: kASM Assembler (decompiled)
generated: { by: claude-code/claude-fable-5-2, at: 2026-11-25T21:30:48Z }
status: stable
ksa_build: "2026.11.25.5949"
---

# A program is a project

The compiler is handed a `CompileInput`: a dictionary of file name → text, the target part, the
game tables and the vehicle's I/O layout.[^compiler-input] The kC front end treats those files like
this:[^frontend-files]

| files | role |
| --- | --- |
| `*.c` | one translation unit each, processed in ordinal name order. A project with none is `KC-E012` "the project has no .c file". |
| `*.h` | not compiled on their own; reached through `#include "…"` |
| `*.kasm` | assembled first, in "module only" mode; their functions and globals join the same program, and each `X.kasm` yields a generated header `X.kasm.h` built from its `.proto` lines |
| `*.py` | `KC-E000` "kScript files beside kC is not implemented until MS9" — see [kScript status](/language/kscript-status.md) |
| `<math.c>`, `<vecmat.c>` | library units appended automatically when `<math.h>` / `<vecmat.h>` is included — see [kC standard library](/language/kc-standard-library.md) |

Which files form a project (a workspace subfolder, or one root-level `.c` plus what it includes) is
decided by the workbench: see [workspace and files](/tooling/workspace-and-files.md). The whole
pipeline after the front end is in [compile pipeline](/compiler/compile-pipeline.md).

There is **no `main`** (`KC-E409`). A program is a set of *tasks* that the kVM scheduler calls; each
returns before the next runs. See [tasks and scheduling](/vm/tasks-and-scheduling.md).

# Minimal program

```c
#include <kfs.h>
#pragma kfs tick_hz 10
#pragma kfs task idle rate_hz=10
void idle(void) { }
```

The shipped example `ex_stock/all_stock.c` is this program without the `#include` line, which a
program that uses nothing from the headers does not need.[^examples-allstock]

# `#pragma kfs` directives

All program-level structure is declared with `#pragma kfs …` at file scope. Macros are not expanded
inside the pragma line ([kC preprocessor](/language/kc-preprocessor.md)).

| directive | syntax | checks |
| --- | --- | --- |
| tick rate | `#pragma kfs tick_hz N` | one per program; `N` must be in the catalogue's `allowed_tick_hz` — 5, 10, 20, 25, 50 or 100 (`KC-E406`)[^parser-tickhz][^frontend-tick] |
| init task | `#pragma kfs init name` | at most one in the program (`KC-E410`)[^frontend-entries] |
| periodic task | `#pragma kfs task name rate_hz=N` or `… divisor=N`, optionally `priority=P` | one of `rate_hz` / `divisor` is required (`KC-E407`); see below[^parser-task] |
| optimiser | `#pragma kfs optimize 0` or `1` | affects functions defined after it in the same file[^parser-optimize] |
| bounds checks | `#pragma kfs bounds on` or `off` | affects functions defined after it in the same file |
| bus publication | `#pragma kfs publish MSG_NAME buffer` | see "Bus buffers"[^parser-bus] |
| bus subscription | `#pragma kfs subscribe MSG_NAME buffer` | see "Bus buffers" |
| actuator ownership | `#pragma kfs claim ALLOCATION` | the only claim that exists; not on the Sequencer (`KC-E520`)[^parser-claim] |
| strict mode | `#pragma kfs strict` | **Not active in this build:** `KC-E000` "… is not implemented until MS13"[^parser-strict] |
| anything else | | `KC-W010` "unknown pragma 'kfs …'; it has no effect in kC"[^parser-strict] |

A `#pragma kfs` inside a function body is `KC-E012` "`#pragma kfs` must be at file scope".[^parser-filescope]

## Tick rate

`tick_hz` is the rate of the computer's base tick. If no unit declares it, the catalogue's
`default_tick_hz` (50) is used.[^frontend-tick][^catalogue-tick]

## Tasks

An entry function must be **defined in the same `.c` file as its pragma**, with a body, and have
exactly the type `void name(void)` (a prototype with no parameters). Otherwise `KC-E401`, e.g. "task
'step' must be defined as `void step(void)`".[^frontend-entries] The pragma may appear before or
after the function; entries are resolved after the whole file is parsed. A function can be named by
only one entry pragma (`KC-E410` "'f' is already an entry point").

Task options are `key=number` pairs separated by spaces:[^parser-task]

| option | meaning |
| --- | --- |
| `rate_hz=N` | run `N` times per second. `N` must be positive and divide the tick rate exactly; otherwise `KC-E407` "N Hz doesn't divide the T Hz tick", with the nearest valid rates in the hint. Stored as divisor `tick_hz / N`. |
| `divisor=N` | run every `N`-th tick; `N` ≥ 1. Used only when `rate_hz` is absent. |
| `priority=P` | 0 … 255, default 0. Passed through to the IR (`IrFunction.Priority`); its scheduling meaning is in [tasks and scheduling](/vm/tasks-and-scheduling.md). |

An unknown option is `KC-E012` "unknown task option '…'". `init` accepts the same option syntax but
only `priority` is kept; a rate on `init` is ignored.[^frontend-entries]

The program must declare at least one entry: otherwise `KC-E401` "the program declares no task or
init" (hint: "a program with no entry point never runs").[^frontend-noentry]

**Task kinds.** The IR knows `Init`, `Periodic` and `Program`.[^entrykind-enum] kC's pragmas can
produce only the first two. **Not active in this build (for kC):** the `Program` kind — a single
long-running task that may suspend — is created only by kASM's `.program` directive.[^assembler-program]
A kC task therefore always runs to completion within its tick budget, and kC has no `yield`.

**Order.** `Inferred:` the shipped examples state that two tasks declared in one file run in
declaration order ("Declared in this order in one file, attitude_control runs first, so allocate sees
this tick's wrench").[^examples-order] The front end only records each function's declaration index,
rate divisor and priority; the ordering rule itself, and the error for an ambiguous order across
files (`KC-E408`), live in the back end — see [backend emitter](/compiler/backend-emitter.md).

## `optimize` and `bounds`

Both are per-file switches read at the moment each function body is parsed, so they can be toggled
between functions.[^parser-fnflags] The effective setting is combined with the build
options:[^frontend-lowerflags]

- peephole optimisation for a function = `CompileOptions.Optimize != 0` **and** the file's
  `optimize` is 1;
- run-time array bounds checks for a function = `CompileOptions.BoundsChecks` **and** the file's
  `bounds` is `on`.

The bundled `<math.c>` starts with `#pragma kfs bounds off`.

## Bus buffers

`#pragma kfs subscribe MSG_X buf` and `#pragma kfs publish MSG_X buf` bind a file-scope object to a
message of the software bus. The checks, in order:[^frontend-bus]

1. `MSG_X` must be a row of the message catalogue (`KC-E003` "'MSG_X' is not a message").
2. One buffer serves one declaration (`KC-E016`), though repeating the identical pragma is accepted.
3. On the Sequencer (part index 0), publishing `MSG_NAV_STATE`, `MSG_ATT_TARGET` or `MSG_WRENCH_CMD`
   is `KC-E520`.[^msglayout-forbidden]
4. The program must define `buf` at file scope with external linkage — not `static`, not merely
   `extern` (`KC-E003`, with the hint "Define `kfs_msg_x_t buf;` at file scope").
5. Under `double_bits = 32` a message with `double` fields cannot be bound (`KC-E541`).
6. `buf` must have exactly the message's generated struct type, unqualified (`KC-E541` "'buf' must
   have type 'kfs_msg_x_t'").

The type comes from `<vehicle.h>`; reading and writing the buffer is ordinary member access. See
[kC builtins and intrinsics](/language/kc-builtins-and-intrinsics.md) and [message bus](/host/message-bus.md).

## Ownership

Publishing certain messages means the program replaces a stock flight-computer block, and
`claim ALLOCATION` means it drives the actuators itself. The front end encodes this mapping
when it checks service calls:[^frontend-owner]

| stock block | owned by publishing |
| --- | --- |
| `NAVIGATION` | `MSG_NAV_STATE` |
| `GUIDANCE` | `MSG_ATT_TARGET` |
| `ATTITUDE_CONTROL` | `MSG_WRENCH_CMD` |
| allocation | `#pragma kfs claim ALLOCATION` |

A service whose table row has `requires_stock` cannot be called when the program owns that block, and
no `requires_stock` service at all can be called from a program that claims `ALLOCATION`
(`KC-E501`). A program that owns nothing "runs in pilot mode and the game's own flight computer flies
the vehicle".[^examples-allstock] See [ownership and control modes](/host/ownership-and-control-modes.md).

# Globals, statics and initialisation

- Globals and `static` locals have static storage. Their initialisers are evaluated at compile time
  into a byte image plus relocations for address constants; there are no run-time constructors.[^parser-staticinit]
- A global without an initialiser is emitted with no data. `Inferred:` such objects start as zero —
  the examples rely on it (`uint32_t ticks;` then `ticks++`), and the image-level rule is in
  [kVM image format](/compiler/kvm-image-format.md).[^frontend-globals]
- A global is flagged `Const` in the IR when its type is `const` (or it is an array of `const`
  elements); what the image does with constant data is in
  [kVM image format](/compiler/kvm-image-format.md).[^frontend-globals]
- State that must survive between calls of a task has to be global or `static`; a task's locals are
  gone when it returns.
- An `init` task is the place for run-time initialisation.

**Non-volatile storage.** `Not active in this build:` kC has no storage class, pragma or section for
non-volatile data. The catalogue gives the Sequencer `nv_bytes` 1024 (0 for the other parts) and the
system block has an `nv_writes` counter, but nothing in `KSA.Kfs.KC` or `KSA.Kfs.Compiler` refers to
non-volatile memory.[^catalogue-parts] See [memory map and sysblock](/vm/memory-map-and-sysblock.md).

# Linkage across files

`KcFrontEnd.Link` resolves names across translation units after all are parsed:[^frontend-link]

- Non-`static` functions and objects share one name space across the program's `.c` files.
- Exactly one unit may *define* a function or give an object an initialiser; a second is `KC-E016`
  "'x' is defined in 'b.c' and in 'a.c'". Several uninitialised tentative definitions of the same
  object merge.
- All declarations of a name must have compatible types (`KC-E016`).
- A function that is declared and called but defined nowhere is `KC-E304` "'f' is declared but never
  defined" — unless a kASM function of that name exists, which then satisfies it. Defining the same
  name in both kC and kASM is `KC-E016`.
- A `static` function that is used but not defined in its file is `KC-E304`.
- `static` functions and globals that are never used draw `KC-W006` (player files only).[^frontend-unused]

All functions defined in player files are emitted, used or not; library functions are emitted only
when reachable from them.[^frontend-prune]

## Symbol names in the image

| entity | symbol |
| --- | --- |
| non-`static` global or function | its own name |
| `static` global or function | its name, or `name.N` (`N` = 1-based index of the unit) when the name is already taken[^frontend-symbols] |
| `static` local `x` in function `f` | `f.x`[^parser-staticlocal] |
| string literal | `.strN`, shared by identical contents across the whole program[^frontend-strings] |
| `kfs_logf` format / `trace` channel | entries `.fmtN` / `.chanN` in the module's string table, not globals[^lowering-stringindex] |

These are the names the debugger and `kfs watch` show; see [debug info](/compiler/debug-info.md).

# Build options

`CompileOptions`:[^options-all]

| option | default | effect in the front end |
| --- | --- | --- |
| `BoundsChecks` | `true` | master switch for array bounds checks |
| `Optimize` | `1` | `0` disables the peephole pass for every function |
| `DoubleBits` | `64` | `32` makes `double` single precision ([kC types and conversions](/language/kc-types-and-conversions.md)) |
| `Defines` | empty | extra predefined macros |
| `Strict` | `false` | **Not active in this build:** no code in `KSA.Kfs.KC` or `KSA.Kfs.Compiler` reads it |
| `PeepholeOff`, `EmitDebug` | empty, `true` | back end only |
| `TimeLimitMs` | `30000` | the compile is abandoned with `KC-E900` after this long[^compiler-timeout] |

The in-game workbench builds with all defaults.

# Per-program limits

Front-end limits (nesting, expression size, object size, frame size, argument cells) are in
[kC language reference](/language/kc-language-reference.md). The part-dependent limits are checked by
the back end against the catalogue; the catalogue values in this build are:[^catalogue-parts]

| part | core | RAM bytes | code bytes | NV bytes |
| --- | --- | --- | --- | --- |
| Sequencer | K8 | 2,048 | 32,768 | 1,024 |
| Flight Computer Mk1 | K32 | 65,536 | 262,144 | 0 |
| Flight Computer Mk2 | K32F | 131,072 | 524,288 | 0 |
| Flight Computer Mk2-RH | K32F | 65,536 | 262,144 | 0 |
| Flight Computer Mk3 | K32D | 262,144 | 1,048,576 | 0 |

Every vehicle computer in this build is the catalogue's `bench_part` — "Bench", a copy of Flight
Computer Mk2 (K32F, 131,072 bytes of RAM) — so on a vehicle only that row applies; nothing maps game
parts to the other rows.[^catalogue-parts]

Catalogue defaults: `max_tasks` 16 (8 on K8), `max_operand_cells` 64 (16 on K8),
`max_return_entries` 16 (8 on K8), `init_budget_seconds` 2. Exceeding them is reported with a `KC`
prefix by the back end (`KC-E701`, `KC-E718`); see [catalogue and processors](/tables/catalogue-processors.md)
and [cost model and budgets](/vm/cost-model-and-budgets.md).

Restrictions the front end applies to the Sequencer (part index 0): no `claim ALLOCATION`, no
publishing of the three GNC messages, and `<matn.h>` reported as unavailable on that part.[^parser-claim][^frontend-bus][^frontend-deferred]

# Modding notes

- The set of `#pragma kfs` directives is a `switch` on the first token in `Parser.Pragma`
  (private). A mod cannot add a directive without patching it; an unknown directive is only a
  warning, so a patch could also post-process `Preprocessor.KfsPragmas`, which records every
  `#pragma kfs` line with its span.[^parser-strict]
- Bus messages are looked up by name in the live `GameTables`
  (`MessageLayout.Find(input.Game, input.Layout, name)`), so a message row added to the tables is
  immediately bindable — subject to the type restrictions and caches described in
  [kC generated headers](/language/kc-generated-headers.md).[^frontend-bus]
- The block-ownership mapping (`GUIDANCE` → `MSG_ATT_TARGET`, …) and the Sequencer's forbidden list
  are hard-coded: a local function in `KcFrontEnd.ServiceOwnership` and the public static array
  `MessageLayout.SequencerForbidden`. A new service with a `requires_stock` value other than those
  three names is still blocked for programs that claim `ALLOCATION`; leave `requires_stock` null for
  services that do not steer the stock flight computer.[^frontend-owner]
- `TranslationUnit.ServiceCalls` records every service call site (id, name, span) per unit, which is
  a convenient place for a mod to inspect what a compiled program uses.[^unit-fields]

[^compiler-input]: Compiler.cs:61-74
[^compiler-timeout]: Compiler.cs:48-53
[^frontend-files]: KcFrontEnd.cs:44-66
[^frontend-tick]: KcFrontEnd.cs:531-552
[^frontend-entries]: KcFrontEnd.cs:553-629
[^frontend-noentry]: KcFrontEnd.cs:630-634
[^frontend-bus]: KcFrontEnd.cs:246-317
[^frontend-owner]: KcFrontEnd.cs:204-244
[^frontend-link]: KcFrontEnd.cs:417-506
[^frontend-unused]: KcFrontEnd.cs:181-195
[^frontend-prune]: KcFrontEnd.cs:804-869
[^frontend-globals]: KcFrontEnd.cs:713-747
[^frontend-symbols]: KcFrontEnd.cs:700-710, 748-767
[^frontend-strings]: KcFrontEnd.cs:683-699
[^frontend-lowerflags]: KcFrontEnd.cs:776-777
[^frontend-deferred]: KcFrontEnd.cs:81
[^parser-tickhz]: Parser.cs:413-434
[^parser-task]: Parser.cs:435-493
[^parser-optimize]: Parser.cs:494-531
[^parser-bus]: Parser.cs:532-542
[^parser-claim]: Parser.cs:543-556
[^parser-strict]: Parser.cs:557-562
[^parser-filescope]: Parser.cs:2119-2124
[^parser-fnflags]: Parser.cs:2006-2007
[^parser-staticinit]: Parser.cs:1668-1733
[^parser-staticlocal]: Parser.cs:2513-2534
[^lowering-stringindex]: Lowering.cs:1333-1345
[^unit-fields]: TranslationUnit.cs:6-33
[^options-all]: CompileOptions.cs:6-23
[^entrykind-enum]: EntryKind.cs:3-9
[^assembler-program]: Assembler.cs:611-637
[^catalogue-tick]: KSA.Kfs.Vm.Tables.Data.catalogue.json:4-5
[^catalogue-parts]: KSA.Kfs.Vm.Tables.Data.catalogue.json:6-23 (`defaults`, `parts`, `bench_part`)
[^msglayout-forbidden]: MessageLayout.cs:29
[^examples-allstock]: Examples.cs:57
[^examples-order]: Examples.cs:53, 55
