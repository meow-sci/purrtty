---
type: Language Reference
title: kC types and conversions
description: The kC type system as implemented by CType and Sema — sizes and alignment, struct layout, compatibility, implicit conversions, literal typing, constant folding, and how core class and double_bits affect it.
resource: ksa-decomp:KSA.Kfs.KC/CType.cs
tags: [kfs, language, kc, types, conversions, constant-folding]
sources:
  - id: ctype
    resource: ksa-decomp:KSA.Kfs.KC/CType.cs
    title: CType (decompiled)
  - id: tk
    resource: ksa-decomp:KSA.Kfs.KC/TK.cs
    title: TK type-kind enum (decompiled)
  - id: sema
    resource: ksa-decomp:KSA.Kfs.KC/Sema.cs
    title: Sema (decompiled)
  - id: parser
    resource: ksa-decomp:KSA.Kfs.KC/Parser.cs
    title: Parser (decompiled)
  - id: lowering
    resource: ksa-decomp:KSA.Kfs.KC/Lowering.cs
    title: Lowering (decompiled)
  - id: frontend
    resource: ksa-decomp:KSA.Kfs.KC/KcFrontEnd.cs
    title: KcFrontEnd (decompiled)
  - id: checks
    resource: ksa-decomp:KSA.Kfs.KC/Checks.cs
    title: Checks (decompiled)
  - id: sysheaders
    resource: ksa-decomp:KSA.Kfs.KC/SystemHeaders.cs
    title: SystemHeaders (decompiled)
  - id: options
    resource: ksa-decomp:KSA.Kfs.Compiler/CompileOptions.cs
    title: CompileOptions (decompiled)
  - id: enums-json
    resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.enums.json
    title: enums.json (embedded table)
generated: { by: claude-code/claude-fable-5-2, at: 2026-11-25T21:30:48Z }
status: stable
ksa_build: "2026.11.25.5949"
---

# The type model

Every kC type is an immutable `CType` object with a `TK` kind.[^tk-enum] The arithmetic types are
shared static instances; derived types are built by `PointerTo`, `ArrayOf`, `Function`, `EnumType`
and `StructType`.[^ctype-factories]

| `TK` | kC spelling | size | align | unsigned | rank |
| --- | --- | --- | --- | --- | --- |
| `Void` | `void` | 0 | 1 | | 0 |
| `Bool` | `_Bool` (`bool` via `<stdbool.h>`) | 1 | 1 | yes | 1 |
| `Char` | `char` | 1 | 1 | **no** | 2 |
| `SChar` | `signed char` | 1 | 1 | no | 2 |
| `UChar` | `unsigned char` | 1 | 1 | yes | 2 |
| `Short` | `short` | 2 | 2 | no | 3 |
| `UShort` | `unsigned short` | 2 | 2 | yes | 3 |
| `Int` | `int` | 4 | 4 | no | 4 |
| `UInt` | `unsigned int` | 4 | 4 | yes | 4 |
| `Long` | `long` | **4** | 4 | no | 5 |
| `ULong` | `unsigned long` | **4** | 4 | yes | 5 |
| `LongLong` | `long long` | 8 | 8 | no | 6 |
| `ULongLong` | `unsigned long long` | 8 | 8 | yes | 6 |
| `Float` | `float` | 4 | 4 | | 0 |
| `Double` | `double`, `long double` | 8 | 8 | | 0 |
| `Enum` | `enum tag` | 4 | 4 | when all enumerators ≥ 0 | 4 |
| `Pointer` | `T *` | **4** | 4 | yes | 0 |
| `Array` | `T[n]` | `n × sizeof(T)` | of `T` | | |
| `Func` | `R(params)` | 0 | 1 | | |
| `Struct` | `struct tag` | computed | computed | | |

Sizes, alignment: [^ctype-statics]; signedness: [^ctype-unsigned]; rank: [^ctype-rank].

Consequences worth remembering:

- Plain `char` is **signed**: its values normalise through `sbyte` and it loads with the
  sign-extending `LDB`.[^sema-normalize][^lowering-load] Code whose result depends on that draws
  `KC-W204`, whose hint explains "kC's plain `char` is signed, but arm-none-eabi-gcc and PowerPC
  compilers make it unsigned; use `int8_t` or `uint8_t`".[^checks-char]
- `long` is 32 bits; only `long long` (and `int64_t`/`uint64_t` from `<stdint.h>`) is 64 bits.
- Pointers are 32 bits on every part. `size_t` is `unsigned int`, `ptrdiff_t` and `intptr_t` are
  `int`.[^sysheaders-stddef]
- `int` and `long` are distinct kinds although both are 4 bytes, so `int *` and `long *` are not
  compatible pointer types.[^ctype-compatible]

`const` and `volatile` are recorded per type (`Qualified` / `Unqualified`).[^ctype-quals]
`volatile` has no effect on code generation that is visible in the front end.

## The 64-bit predicate and cells

`Is64` is true for `long long`, `unsigned long long` and `double`.[^ctype-is64] The kVM operand
stack is made of 4-byte cells, so a 64-bit value takes two cells and everything else one. Function
results are 0 cells (`void` or aggregate), 1 cell, or 2 cells.[^lowering-resultcells]

## Struct layout

`StructDef.Define` lays members out in declaration order: each member is placed at the next multiple
of its alignment, the struct's alignment is the largest member alignment, and the size is rounded up
to that alignment.[^ctype-define] There is no packing control. Example: `struct { uint8_t a; double
d; uint16_t s; }` places `a` at 0, `d` at 8, `s` at 16 and has size 24, alignment 8.

The same rule is applied by the header generator for the I/O blocks and bus messages, which is why a
generated `kfs_msg_*_t` can be read by plain member access; see
[kC generated headers](/language/kc-generated-headers.md).

## No built-in vector types

kC has no vector, matrix or quaternion types and no operators on aggregates. `vec3`, `quat`, `mat3`,
`dvec3`, `dquat` and `dmat3` are ordinary anonymous structs typedef'd in `<kfs.h>`:[^sysheaders-kfsh]

```c
typedef struct { float x, y, z; } vec3;
typedef struct { float x, y, z, w; } quat;
typedef struct { float m[3][3]; } mat3;
typedef struct { double x, y, z; } dvec3;
typedef struct { double x, y, z, w; } dquat;
typedef struct { double m[3][3]; } dmat3;
```

`a + b` on two `vec3` is `KC-E211`; use the functions of `<vecmat.h>`
([kC standard library](/language/kc-standard-library.md)).

**Not active in this build:** `enums.json` lists `vector`, `matrix` and `record` among
`debug_type_kind`, but kC's debug-type mapping only ever produces bool, int, float, enum, pointer,
array and struct.[^enums-json-debugkind][^lowering-irtype] The header `<matn.h>` ("fixed-size matrix
types") is recognised by name and rejected as not yet implemented.[^frontend-deferred]

# Type compatibility

`CType.Compatible(a, b, ignoreQuals)`:[^ctype-compatible]

| kind | compatible when |
| --- | --- |
| arithmetic, `void` | same `TK` kind |
| pointer | pointees compatible **including qualifiers** |
| array | elements compatible and, when both lengths are known, equal |
| enum | same tag |
| function | return types compatible; if both have prototypes, same parameter count, same variadic flag, parameters pairwise compatible (qualifiers ignored) |
| struct | the same definition, **or** the same tag (two untagged structs count as the same tag), both complete, and field-by-field identical names, offsets and compatible types |

The struct rule is structural. `Inferred:` because `vec3` is an untagged `struct { float x, y, z; }`
and the generated sensor block declares vector fields as untagged structs with members `x`, `y`, `z`
of type `float`, a sensor vector can be assigned to a `vec3` variable without a cast. This follows
from `StructsCompatible` and the generator's output; it was not confirmed by compiling.[^ctype-structs]
(It concerns the bench layout only: a vehicle layout has no sensor block, see
[kC generated headers](/language/kc-generated-headers.md).)

# Conversions

## Integer promotion and the usual arithmetic conversions

`Promoted()`: integer types of rank below `int` (`_Bool`, the `char` types, the `short` types)
become `int`; an enum becomes `int` or `unsigned int` by its signedness; everything else is
unchanged apart from dropping qualifiers.[^ctype-promoted]

`UsualArith(a, b)`:[^ctype-usual]

1. either operand `double` → `double`;
2. else either `float` → `float`;
3. else promote both; if the ranks are equal, the unsigned one wins;
4. else take the higher-rank type, unless it is signed and the lower-rank one is unsigned and at
   least as wide — then the result is `unsigned int` (or `unsigned long long` when the higher-rank
   type is `long long`).

So `long + unsigned int` is `unsigned int`, and `long long + unsigned int` is `long long`.

## Operators

| operator | operand rule | result type |
| --- | --- | --- |
| unary `+`, `-` | arithmetic | promoted operand[^sema-unary] |
| `~` | integer | promoted operand |
| `!` | scalar | `int` (0 or 1) |
| `*`, `/`, `+`, `-` | arithmetic | usual arithmetic type[^sema-binary] |
| `%`, `&`, `\|`, `^` | integers only | usual arithmetic type |
| `<<`, `>>` | integers only | promoted **left** operand; a 64-bit shift count is converted to `int`[^sema-shift] |
| `<`, `<=`, `>`, `>=`, `==`, `!=` | arithmetic (compared in the usual arithmetic type), or two pointers, or a pointer and null for `==`/`!=` | `int` |
| `&&`, `\|\|` | scalars | `int` |
| `c ? a : b` | both arithmetic → usual arithmetic type; both `void`; two pointers; pointer and null; two compatible structs | as listed[^sema-conditional] |
| `p + i`, `i + p`, `p - i` | pointer to complete type and integer | the pointer type[^sema-ptradd] |
| `p - q` | pointers to compatible types | `int` (element count) |
| `a[i]` | array or pointer, integer index (a 64-bit index is converted to `int`) | element type[^sema-index] |
| `op=` | computed in the usual arithmetic type of both sides, then converted back to the left type | left type[^sema-assignop] |

`>>` on a signed left operand is an arithmetic shift; on unsigned, logical.[^lowering-operator]

## Assignment, argument passing, return, initialisation

All four go through `ConvertAssign`:[^sema-convertassign]

- struct ← struct only when compatible (no conversion between different struct types);
- arrays cannot be assigned (`KC-E212`);
- arithmetic ← arithmetic converts implicitly;
- pointer ← pointer when the pointee is compatible, or either side is `void *`; dropping `const` or
  `volatile` from the pointee is `KC-E211` "The conversion discards `const`";[^sema-ptrassign]
- pointer ← integer only for the null constant `0`; integer ← pointer never (outside system
  headers);
- `_Bool` ← anything scalar is a test against zero.[^sema-tobool]

Implicit arithmetic conversions are silent except for these warnings, which fire only in player code
(not in system headers or the library):[^sema-cast-warn]

| warning | when |
| --- | --- |
| `KC-W104` | an implicit `float` → `double` conversion while the target part's core class is not 3 (`K32D`); the message quotes the part's cycle cost of a double multiply against a float multiply |
| `KC-W202` | implicit `double` → `float`, or a 64-bit integer → a narrower integer; also `x op= y` where the operation runs in `double` or 64 bits and `x` is `float` or narrower than 64 bits[^sema-assignop] |

Other narrowing (`int` → `int8_t`, `float` → `int`, `int` → `float`) is silent.

## Explicit casts

`(T)x` requires a scalar or `void` target.[^parser-cast] Pointer-to-pointer casts between any object
types are allowed when written explicitly. **Integer ↔ pointer casts are `KC-E644`** in player code
("Addresses are managed by the system headers"); they are allowed in system headers, where a constant
integer becomes a constant pointer.[^sema-intptr]

## Calls without a prototype

For a callee declared `R f()` (no prototype), integer arguments are promoted and `float` arguments
become `double`.[^sema-call] `kfs_logf` is not handled this way; it has its own argument rules, see
[kC builtins and intrinsics](/language/kc-builtins-and-intrinsics.md).

# Literal types

Integer literals (`NumberLiteral`):[^parser-number]

| suffix | decimal | hex / octal |
| --- | --- | --- |
| none | `int` if ≤ 2³¹−1, else `long long`; above 2⁶³−1 is `KC-E101` ("add a `u` suffix") | `int`, `unsigned int`, `long long`, `unsigned long long` — the first that holds the value |
| `l` | `long`, else `long long` | `long`, `unsigned long`, `long long`, `unsigned long long` |
| `u` | `unsigned int` if ≤ 2³²−1, else `unsigned long long` | same |
| `ul` | `unsigned long` if ≤ 2³²−1, else `unsigned long long` | same |
| `ll` | `long long`; above 2⁶³−1 is `KC-E101` | `long long`, else `unsigned long long` |
| `ull` | `unsigned long long` | same |

A value above 2⁶⁴−1 is `KC-E101`. Floating literals are `double`, or `float` with an `f` suffix; a
value outside `double` range is `KC-E101`. Character constants and enumerators are `int`.
`sizeof`, `_Alignof` and `offsetof` produce `unsigned int`.[^parser-sizeof]

# Constant folding

Folding happens in `Sema` while the tree is built; a folded node is a `Num` or `FNum` with
`IsConst` set, and that is what "integer constant expression" means to the parser.[^parser-constexpr]

- **Normalisation.** Every integer constant is wrapped to its type: `_Bool` to 0/1, the 8/16/32-bit
  types by truncation with the type's signedness, pointers as unsigned 32-bit.[^sema-normalize]
- **Unary** `+ - ~ !` fold.[^sema-unary]
- **Integer binary** `+ - * / % & | ^` fold with wraparound; comparisons fold to `int`. `x / 0` and
  `x % 0` are **not** folded (the expression stays non-constant, and where a constant is required the
  hint reads "A division by zero is not a constant expression"). `INT_MIN / -1` folds to `INT_MIN`
  and `INT_MIN % -1` to 0, in both 32 and 64 bits.[^sema-fold]
- **Shifts** fold with the count masked to 5 bits (32-bit) or 6 bits (64-bit).[^sema-shift]
- **Floating binary** `+ - * /` fold in the operation's own precision: `float` operations are
  computed in `float`, not `double`. A NaN result is replaced by the canonical quiet NaN
  (`0x7FC00000` / `0x7FF8000000000000`). Float comparisons fold too.[^sema-fold]
- **`&&` / `||`** fold only when both operands are constants.[^sema-logical]
- **`?:`** folds when the condition and both arms are constants.[^sema-conditional]
- **Casts** of constants fold. Float → integer uses the VM's own conversion helpers
  (`KSA.Kfs.Vm.Conv`), so compile-time and run-time results agree; see
  [soft routines and FP](/vm/soft-routines-and-fp.md).[^sema-foldcast]
- **Builtins** `sqrtf`/`sqrt`/`fabsf`/`fabs`/`isnan`/`isinf` fold on constant arguments.[^sema-builtin]

`Inferred:` in the decompiled `FoldCast`, the branch that converts a 32-bit integer constant to a
floating type converts through `float` for both `float` and `double` targets. If that reflects the
shipped IL faithfully, `double d = 16777217;` folds to `16777216.0`. Write such constants with a
decimal point (`16777217.0`) to avoid depending on it.[^sema-foldcast]

Static-storage initialisers additionally accept **address constants**: `&object`, array names,
`&object.member`, `&array[const]`, pointer casts of those, plus or minus a constant
offset.[^sema-addrconst]

# Core class and `double_bits`

Type sizes do **not** depend on the target part. Two things do vary:

| input | effect in the front end |
| --- | --- |
| `PartDescription.CoreClass` (0 `K8`, 1 `K32`, 2 `K32F`, 3 `K32D`) | `KC-W104` is issued for implicit `float` → `double` promotions unless the class is 3. Nothing else in `KSA.Kfs.KC` reads it, apart from the predefined macro `__KFS_CORE__`.[^sema-coreclass][^frontend-predefine] |
| `CompileOptions.DoubleBits` (64 default, or 32) | with 32, the keyword `double` and unsuffixed floating literals become `float`; `KC-W203` "`double` is single precision in this project" is reported once per translation unit; a bus message that contains a `double` field cannot be bound (`KC-E541`).[^parser-doubletype][^frontend-double32] |

How `float` and `double` arithmetic is executed on cores without the matching FPU (soft-float
routines and their cycle costs) is a back-end and VM matter; see
[cost model and budgets](/vm/cost-model-and-budgets.md) and
[soft routines and FP](/vm/soft-routines-and-fp.md).

The in-game workbench builds with default `CompileOptions`, so `double_bits` is 64 there; only the
command-line front ends (unreachable in the shipped game) expose `--double-bits`. See [build and load flow](/tooling/build-and-load-flow.md)
and [CLI](/tooling/cli.md).[^options-defaults]

# How types reach the VM

| operation | instruction chosen |
| --- | --- |
| load `_Bool`, `unsigned char` | `LDBU` |
| load `char`, `signed char` | `LDB` |
| load `short` / `unsigned short` | `LDH` / `LDHU` |
| load any 8-byte type | `LD64` |
| load anything else (4 bytes) | `LDW` |
| store, by size 1 / 2 / 4 / 8 | `STB` / `STH` / `STW` / `ST64` |
| narrow to `char`/`signed char`, `unsigned char`, `short`, `unsigned short` | `SEXT8`, `ZEXT8`, `SEXT16`, `ZEXT16` |

Loads and stores: [^lowering-load]; narrowing: [^lowering-narrow].

Arithmetic conversions map to dedicated opcodes (`I2F`, `U2F`, `I2D`, `U2D`, `F2D`, `D2F`, `F2I`,
`F2U`, `D2I`, `D2U`, `I2L`, `U2L`, `L2I`, `L2F`, `L2D`, `LU2D`, `F2L`, `D2L`, `D2LU`); `unsigned long
long` → `float` is open-coded with a halving trick, and `float` → `unsigned long long` goes through
`F2D` then `D2LU`.[^lowering-convert] Opcode semantics are in [opcodes](/vm/opcodes.md).

Aggregates never travel on the operand stack. A struct argument is copied into a caller temporary
and its address passed; a struct result is written through a hidden first argument named
`.result`.[^lowering-call]

For debug information each `CType` is mapped to an `IrType`; see [debug info](/compiler/debug-info.md).[^lowering-irtype]

# Modding notes

- `CType`, `TK`, `StructField` and `CType.StructDef` are public. The primitive types are
  `public static readonly` singletons compared by kind, so they must not be replaced.
- The widths above are constants in the type objects; nothing reads them from the game tables. A mod
  cannot change `sizeof(long)` or pointer width without patching `CType`.
- `StructDef` ids come from a process-wide counter (`Interlocked.Increment`), so struct identity is
  per compile but ids keep growing across compiles.[^ctype-define]
- A mod that adds header types should stick to the scalar and struct shapes above: the syscall
  lowering flattens a struct argument one level deep into cells
  ([kC builtins and intrinsics](/language/kc-builtins-and-intrinsics.md)). A struct *return* type on
  a service binding is not supported — the lowering throws instead of reporting a diagnostic — so keep
  service results to one scalar cell; see [adding a service](/modding/adding-a-service.md).

[^tk-enum]: TK.cs:3-25
[^ctype-factories]: CType.cs:464-532
[^ctype-statics]: CType.cs:66-164
[^ctype-unsigned]: CType.cs:351-381
[^ctype-rank]: CType.cs:383-412
[^ctype-compatible]: CType.cs:687-742
[^ctype-structs]: CType.cs:744-780
[^ctype-quals]: CType.cs:539-563
[^ctype-is64]: CType.cs:327-338
[^ctype-define]: CType.cs:27-56
[^ctype-promoted]: CType.cs:621-640
[^ctype-usual]: CType.cs:642-685
[^sema-normalize]: Sema.cs:136-167
[^sema-unary]: Sema.cs:502-578
[^sema-binary]: Sema.cs:602-798
[^sema-shift]: Sema.cs:638-683
[^sema-logical]: Sema.cs:607-635
[^sema-conditional]: Sema.cs:1176-1233
[^sema-ptradd]: Sema.cs:690-740, 800-820
[^sema-index]: Sema.cs:1243-1280
[^sema-assignop]: Sema.cs:1065-1148
[^sema-convertassign]: Sema.cs:420-462
[^sema-ptrassign]: Sema.cs:285-289, 332-360
[^sema-tobool]: Sema.cs:464-485
[^sema-cast-warn]: Sema.cs:310-322
[^sema-intptr]: Sema.cs:263-276
[^sema-call]: Sema.cs:1432-1456
[^sema-fold]: Sema.cs:822-937
[^sema-foldcast]: Sema.cs:362-418
[^sema-builtin]: Sema.cs:1464-1511
[^sema-addrconst]: Sema.cs:1549-1615
[^sema-coreclass]: Sema.cs:36-40, 235-238
[^parser-cast]: Parser.cs:2663-2698
[^parser-number]: Parser.cs:3653-3738
[^parser-sizeof]: Parser.cs:2889-2996
[^parser-constexpr]: Parser.cs:591-619
[^parser-doubletype]: Parser.cs:866-878
[^lowering-load]: Lowering.cs:846-883
[^lowering-narrow]: Lowering.cs:1733-1756
[^lowering-convert]: Lowering.cs:1586-1731
[^lowering-operator]: Lowering.cs:1437-1560
[^lowering-resultcells]: Lowering.cs:165-176
[^lowering-call]: Lowering.cs:101-116, 1293-1331
[^lowering-irtype]: Lowering.cs:46-71
[^frontend-deferred]: KcFrontEnd.cs:81
[^frontend-predefine]: KcFrontEnd.cs:392-415
[^frontend-double32]: KcFrontEnd.cs:300-304
[^checks-char]: Checks.cs:142-159
[^sysheaders-stddef]: SystemHeaders.cs:10, 12
[^sysheaders-kfsh]: SystemHeaders.cs:20
[^options-defaults]: CompileOptions.cs:6-23
[^enums-json-debugkind]: KSA.Kfs.Vm.Tables.Data.enums.json:11
