---
type: Data Table Reference
title: kVM soft routines, floating-point self-test and arithmetic edge cases
description: What "soft" instructions are and how routines.json names and sizes them, the FpSelfTest that guards every machine construction with fpcheck.json, the Conv conversion helpers, and the edgecases.json conformance vectors, all reproduced in full.
resource: ksa-decomp:KSA.Kfs.Vm/SoftRoutines.cs
tags: [kfs, vm, kvm, soft-routines, floating-point, determinism, data-table]
sources:
  - id: soft-routines
    resource: ksa-decomp:KSA.Kfs.Vm/SoftRoutines.cs
    title: SoftRoutines (decompiled)
  - id: routines-json
    resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.routines.json
    title: routines.json (embedded)
  - id: fp-self-test
    resource: ksa-decomp:KSA.Kfs.Vm/FpSelfTest.cs
    title: FpSelfTest (decompiled)
  - id: fpcheck-json
    resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.fpcheck.json
    title: fpcheck.json (embedded)
  - id: edgecases-json
    resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.edgecases.json
    title: edgecases.json (embedded)
  - id: conv
    resource: ksa-decomp:KSA.Kfs.Vm/Conv.cs
    title: Conv (decompiled)
  - id: kvm-machine
    resource: ksa-decomp:KSA.Kfs.Vm/KvmMachine.cs
    title: KvmMachine (decompiled)
  - id: game-tables
    resource: ksa-decomp:KSA.Kfs.Vm.Tables/GameTables.cs
    title: GameTables.BuildCostTables (decompiled)
  - id: image-reader
    resource: ksa-decomp:KSA.Kfs.Vm.Image/ImageReader.cs
    title: ImageReader (decompiled) — runtime-lib rule
  - id: costs-json
    resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.costs.json
    title: costs.json (embedded)
generated: { by: claude-code/claude-fable-5-2, at: 2026-11-25T21:46:43Z }
status: stable
ksa_build: "2026.11.25.5949"
---

# Summary

Three loosely related pieces keep kVM arithmetic *priced realistically* and *bit-for-bit deterministic*:

- **Soft routines** model the fact that a small processor has no hardware for some operations (floating
  point on `K8`/`K32`, doubles on `K32F`, 64-bit divide everywhere). The VM still executes those opcodes
  natively on the host; "soft" only changes their **cycle cost**, charges the image a **code-size
  footprint** for the library family, and gives the profiler a **routine name** to attribute the cycles to.
  No routine code exists or runs.
- **`FpSelfTest`** proves, each time a machine is constructed, that the host's floating point behaves as
  strict IEEE-754 (no flush-to-zero, no fused multiply-add, correct rounding), using the vectors in
  `fpcheck.json`.
- **`Conv`** implements the integer ↔ float conversions whose host behaviour is not trustworthy, and
  **`edgecases.json`** records the expected result of every arithmetic corner case.

# Soft routines

## Where "soft" comes from

A cost cell in `costs.json` may carry `"soft": true` (see [cost model](/vm/cost-model-and-budgets.md)).
For each core, `GameTables.BuildCostTables` records `Soft[category]` and, for soft categories, the routine
*family* named for that category in `routines.json`; it throws if a soft category has no routine row or its
family has no byte footprint on that core.[^game-tables] When an image is loaded, each instruction whose
cost comes back soft gets `Instr.Soft = category index`, and its family is added to the image's set of
used families; the image header's `runtime_lib_size` must equal the sum of those families' bytes (rule
`runtime-lib`), and code + initialised data + runtime library must fit the part's code memory (rule
`program-memory`).[^image-reader]

At run time the only effect is in the profiler: for an instruction with `Soft >= 0` the interpreter calls
`prof.Soft(SoftRoutineName(op, soft), cycles, cost)` (`KvmMachine.cs:819-822`), which books the cycles
under a pseudo-function with the routine's name (see [profiler](/vm/profiler-and-exec-trace.md)).[^kvm-machine]

## routines.json in full

Version 1.[^routines-json]

### `routines` — category → routine names and family

| Category | `k8` | `arm` | Family |
| --- | --- | --- | --- |
| `i32.div` | `__divmodsi4` | `__aeabi_idiv` | `div32` |
| `i64.mul` | `__muldi3` | `__muldi3` | `int64` |
| `i64.div` | `__divdi3` | `__aeabi_ldivmod` | `int64` |
| `f32.add` | `__addsf3` | `__addsf3` | `float32` |
| `f32.mul` | `__mulsf3` | `__mulsf3` | `float32` |
| `f32.div` | `__divsf3` | `__divsf3` | `float32` |
| `f32.cmp` | `__cmpsf2` | `__cmpsf2` | `float32` |
| `f32.sqrt` | `sqrtf` | `sqrtf` | `sqrt` |
| `f64.sqrt` | `sqrt` | `sqrt` | `sqrt` |
| `f64.add` | `__adddf3` | `__adddf3` | `float64` |
| `f64.mul` | `__muldf3` | `__muldf3` | `float64` |
| `f64.div` | `__divdf3` | `__divdf3` | `float64` |
| `f64.cmp` | `__cmpdf2` | `__cmpdf2` | `float64` |
| `cvt.i32f32` | `__floatsisf`, `__fixsfsi` | `__floatsisf`, `__fixsfsi` | `float32` |
| `cvt.f32f64` | `__extendsfdf2`, `__truncdfsf2` | `__extendsfdf2`, `__truncdfsf2` | `float64` |
| `cvt.i64f` | `__floatdidf`, `__fixdfdi` | `__floatdidf`, `__fixdfdi` | `int64` |

Where two names are listed, the first is the "to floating point / widening" direction and the second the
"to integer / narrowing" direction (see `SoftRoutines.Name` below).

### `families` — code bytes charged per core

| Family | Contents | K8 bytes | K32 bytes | K32F bytes | K32D bytes |
| --- | --- | --- | --- | --- | --- |
| `div32` | Soft 32-bit integer divide | 200 | 100 | 100 | null |
| `int64` | Soft 64-bit multiply and divide | 1000 | 500 | 500 | 300 |
| `float32` | Soft single precision | 1500 | 1000 | 1000 | null |
| `float64` | Soft double precision | 4000 | 2000 | 2000 | null |
| `sqrt` | Soft square root | 400 | 300 | 300 | 300 |

`null` means the family has no footprint on that core, which is only legal if no category of that family is
soft there.

### `table_rows` — rendering groups for the in-game reference

| # | Categories |
| --- | --- |
| 0 | `i32.div` |
| 1 | `i64.mul` |
| 2 | `i64.div` |
| 3 | `f32.add`, `f32.mul`, `f32.div`, `f32.cmp` |
| 4 | `f32.sqrt`, `f64.sqrt` |
| 5 | `f64.add`, `f64.mul`, `f64.div`, `f64.cmp` |
| 6 | `cvt.i32f32` |
| 7 | `cvt.f32f64` |
| 8 | `cvt.i64f` |

## Which categories are soft where

Derived from `costs.json` and `routines.json`; the stock `K32` override makes `DIV`/`DIVU`/`REM`/`REMU`
*not* soft on `K32` even though the `i32.div` category is.[^costs-json]

| Category | Soft on | Routine name(s) reported |
| --- | --- | --- |
| `i32.div` | K8, K32 | K8: `__divmodsi4`; K32: `__aeabi_idiv` |
| `i64.mul` | K8, K32 | K8: `__muldi3`; K32: `__muldi3` |
| `i64.div` | K8, K32, K32F, K32D | K8: `__divdi3`; K32: `__aeabi_ldivmod`; K32F: `__aeabi_ldivmod`; K32D: `__aeabi_ldivmod` |
| `f32.add` | K8, K32 | K8: `__addsf3`; K32: `__addsf3` |
| `f32.mul` | K8, K32 | K8: `__mulsf3`; K32: `__mulsf3` |
| `f32.div` | K8, K32 | K8: `__divsf3`; K32: `__divsf3` |
| `f32.sqrt` | K8, K32, K32D | K8: `sqrtf`; K32: `sqrtf`; K32D: `sqrtf` |
| `f32.cmp` | K8, K32 | K8: `__cmpsf2`; K32: `__cmpsf2` |
| `f64.add` | K8, K32, K32F | K8: `__adddf3`; K32: `__adddf3`; K32F: `__adddf3` |
| `f64.mul` | K8, K32, K32F | K8: `__muldf3`; K32: `__muldf3`; K32F: `__muldf3` |
| `f64.div` | K8, K32, K32F | K8: `__divdf3`; K32: `__divdf3`; K32F: `__divdf3` |
| `f64.sqrt` | K8, K32, K32F, K32D | K8: `sqrt`; K32: `sqrt`; K32F: `sqrt`; K32D: `sqrt` |
| `f64.cmp` | K8, K32, K32F | K8: `__cmpdf2`; K32: `__cmpdf2`; K32F: `__cmpdf2` |
| `cvt.i32f32` | K8, K32 | K8: `__floatsisf` / `__fixsfsi`; K32: `__floatsisf` / `__fixsfsi` |
| `cvt.f32f64` | K8, K32, K32F | K8: `__extendsfdf2` / `__truncdfsf2`; K32: `__extendsfdf2` / `__truncdfsf2`; K32F: `__extendsfdf2` / `__truncdfsf2` |
| `cvt.i64f` | K8, K32, K32F, K32D | K8: `__floatdidf` / `__fixdfdi`; K32: `__floatdidf` / `__fixdfdi`; K32F: `__floatdidf` / `__fixdfdi`; K32D: `__floatdidf` / `__fixdfdi` |

## The `SoftRoutines` class

```csharp
public static class SoftRoutines {
    public static string?[] Table(GameTables tables, string core);
    public static string Name(string?[] table, byte op, int category);
    public static string Name(GameTables tables, string core, byte op);
}
```

[^soft-routines]

- `Table` returns an array indexed by category index; each entry is the routine names for that category
  joined with `|`, taken from the row's `k8` list when `core == "K8"` and from `arm` otherwise (so `K32`,
  `K32F` and `K32D` all use the ARM names). Categories without a routine row stay null. `KvmMachine` builds
  this once per image load with `SoftRoutines.Table(_tables, _part.Costs.Core)`.
- `Name(table, op, category)` splits the entry: no entry → the literal `"soft"`; one name → that name; two
  names → the first for `I2F`, `U2F`, `L2D`, `LU2D`, `I2D`, `U2D`, `F2D`, `L2F`, the second for every other
  opcode of the category.
- `Name(tables, core, op)` is a convenience that rebuilds the table; the compiler's program analysis uses
  it for per-line cost reports (`KSA.Kfs.Compiler/ProgramAnalysis.cs:973`).

# Floating-point self-test

## When it runs

`KvmMachine`'s constructor calls `FpSelfTest.Run(fpVectors, out failingVector)` after the endianness check
and throws `UnsupportedHostException(failingVector, message)` on failure (`KvmMachine.cs:1856-1860`). The
vectors default to `game.FpCheckData.Vectors`. So the test runs for **every machine constructed** — every
vehicle computer, bench machine and CLI run — not once per process.

```csharp
public static string? Run(IReadOnlyList<FpVector> vectors, out string? failingVector)
public sealed record FpVector(string Name, string Op, int Width, string A, string Expect, string Why,
                              string? B = null, string? C = null);
```

## What it checks (`FpSelfTest.cs:10-85`)

| `op` | Computation | Notes |
| --- | --- | --- |
| `mul`, `add`, `div` | `a op b` in `float` (width 32) or `double` (width 64) | Operands are parsed from hex bit patterns. |
| `sqrt` | `MathF.Sqrt(a)` / `Math.Sqrt(a)` | |
| `muladd` | `Keep(Keep(a × b) + c)` | Detects a fused multiply-add. |
| `lu2d`, `l2d` | `Conv.ULongToDouble`, `Conv.LongToDouble` | `a` is an integer bit pattern. |
| `l2f` | `Conv.LongToFloat` | |
| `d2lu` | `Conv.DoubleToULong` | `a` is a double bit pattern. |
| `f2l` | `Conv.FloatToLong` | `a` is a float bit pattern. |
| anything else | throws `GameTablesException("fpcheck.json: unknown op '…'")` | |

Every arithmetic intermediate goes through `Keep`, a `[MethodImpl(MethodImplOptions.NoInlining)]` identity
function, so the JIT cannot fold the expression or keep it in extended precision. The result's bits are
compared with `expect`; the special value `"nan"` accepts any NaN (only for the arithmetic ops). The first
mismatch returns

`floating-point self-test failed: {name} gave 0x{bits:X}, expected {expect}; this host can't run kFS computers deterministically`

and sets `failingVector` to the vector's name.[^fp-self-test]

## fpcheck.json in full

Version 1, 31 vectors.[^fpcheck-json]

| # | Name | Op | Width | `a` | `b` | `c` | Expect | Why (table text) |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 0 | `f32.min_normal_times_half` | `mul` | 32 | `0x00800000` | `0x3F000000` | — | `0x00400000` | 2^-126 × 0.5 = 2^-127, a subnormal, not zero (FTZ gives 0) |
| 1 | `f32.subnormal_times_one` | `mul` | 32 | `0x00000001` | `0x3F800000` | — | `0x00000001` | 2^-149 × 1 unchanged (DAZ gives 0) |
| 2 | `f32.add_tie_to_even_down` | `add` | 32 | `0x3F800000` | `0x33800000` | — | `0x3F800000` | 1 + 2^-24 is exactly half an ulp above 1; ties to even keep 1 |
| 3 | `f32.add_tie_to_even_up` | `add` | 32 | `0x3F800000` | `0x34400000` | — | `0x3F800002` | 1 + 1.5 × 2^-23 lies halfway between 1 + 2^-23 (odd) and 1 + 2^-22 (even); ties to even |
| 4 | `f32.div_rounds_down` | `div` | 32 | `0x40A00000` | `0x40400000` | — | `0x3FD55555` | 5/3 = 1.1010…₂; bit 24 is 0, remainder ulp/3, rounds down |
| 5 | `f32.div_rounds_up` | `div` | 32 | `0x3F800000` | `0x40400000` | — | `0x3EAAAAAB` | 1/3 = 1.0101…₂ × 2^-2; bit 24 is 1, remainder 2ulp/3, rounds up |
| 6 | `f32.zero_div_zero` | `div` | 32 | `0x00000000` | `0x00000000` | — | `nan` | 0/0 is a NaN |
| 7 | `f32.sqrt2` | `sqrt` | 32 | `0x40000000` | — | — | `0x3FB504F3` | Correctly rounded √2 in binary32 |
| 8 | `f32.mul_then_add_unfused` | `muladd` | 32 | `0x3F800001` | `0x3F7FFFFE` | `0xBF800000` | `0x00000000` | (1 + 2^-23)(1 − 2^-23) = 1 − 2^-46 rounds to 1, then + (−1) = +0; a fused multiply-add gives −2^-46 = 0xA8800000 |
| 9 | `f64.min_normal_times_half` | `mul` | 64 | `0x0010000000000000` | `0x3FE0000000000000` | — | `0x0008000000000000` | 2^-1022 × 0.5 = 2^-1023, a subnormal |
| 10 | `f64.subnormal_times_one` | `mul` | 64 | `0x0000000000000001` | `0x3FF0000000000000` | — | `0x0000000000000001` | 2^-1074 × 1 unchanged |
| 11 | `f64.add_tie_to_even_down` | `add` | 64 | `0x3FF0000000000000` | `0x3CA0000000000000` | — | `0x3FF0000000000000` | 1 + 2^-53, half an ulp; ties to even keep 1 |
| 12 | `f64.add_tie_to_even_up` | `add` | 64 | `0x3FF0000000000000` | `0x3CB8000000000000` | — | `0x3FF0000000000002` | 1 + 1.5 × 2^-52, halfway between odd and even neighbours; ties to even |
| 13 | `f64.div_rounds_down` | `div` | 64 | `0x3FF0000000000000` | `0x4008000000000000` | — | `0x3FD5555555555555` | 1/3: bit 53 is 0, remainder ulp/3, rounds down |
| 14 | `f64.div_rounds_up` | `div` | 64 | `0x4014000000000000` | `0x4008000000000000` | — | `0x3FFAAAAAAAAAAAAB` | 5/3: bit 53 is 1, remainder 2ulp/3, rounds up |
| 15 | `f64.zero_div_zero` | `div` | 64 | `0x0000000000000000` | `0x0000000000000000` | — | `nan` | 0/0 is a NaN |
| 16 | `f64.sqrt2` | `sqrt` | 64 | `0x4000000000000000` | — | — | `0x3FF6A09E667F3BCD` | Correctly rounded √2 in binary64 |
| 17 | `f64.mul_then_add_unfused` | `muladd` | 64 | `0x3FF0000000000001` | `0x3FEFFFFFFFFFFFFE` | `0xBFF0000000000000` | `0x0000000000000000` | (1 + 2^-52)(1 − 2^-52) = 1 − 2^-104 rounds to 1, then + (−1) = +0; fused gives −2^-104 = 0xB970000000000000 |
| 18 | `lu2d.top_bit_sticky` | `lu2d` | 64 | `0x8000000000000401` | — | — | `0x43E0000000000001` | MS4-21: 2^63 + 1025 lies 1025 above 2^63, where doubles are 2048 apart, so one rounding gives 2^63 + 2048; halving first (2^62 + 512.5 → a tie at spacing 1024, to even 2^62) then doubling gives 2^63, the double-rounding error |
| 19 | `lu2d.max` | `lu2d` | 64 | `0xFFFFFFFFFFFFFFFF` | — | — | `0x43F0000000000000` | 2^64 − 1 rounds to 2^64 (all 64 bits set: the dropped 11 bits are above half) |
| 20 | `l2d.tie_to_even` | `l2d` | 64 | `0x0020000000000001` | — | — | `0x4340000000000000` | 2^53 + 1 is a tie between 2^53 (even) and 2^53 + 2; ties to even |
| 21 | `l2d.min` | `l2d` | 64 | `0x8000000000000000` | — | — | `0xC3E0000000000000` | INT64_MIN is −2^63 exactly |
| 22 | `l2f.double_rounding` | `l2f` | 32 | `0x4000004000000001` | — | — | `0x5E800001` | MS4-21: 2^62 + 2^38 + 1 is just above the float midpoint 2^62 + 2^38 (floats are 2^39 apart at 2^62), so one rounding gives 2^62 + 2^39 = 0x5E800001; via double the +1 is lost (doubles are 1024 apart there), leaving the exact midpoint, which ties to even 2^62 = 0x5E800000 |
| 23 | `l2f.negative` | `l2f` | 32 | `0xFFFFFFFFFF000000` | — | — | `0xCB800000` | −2^24 exactly |
| 24 | `d2lu.above_2_63` | `d2lu` | 64 | `0x43E0000000000000` | — | — | `0x8000000000000000` | 2^63 as an unsigned integer; a conversion through a signed 64-bit type would saturate or wrap |
| 25 | `d2lu.near_max` | `d2lu` | 64 | `0x43EFFFFFFFFFFFFF` | — | — | `0xFFFFFFFFFFFFF800` | (2^53 − 1) × 2^11 = 2^64 − 2048, the largest double below 2^64 |
| 26 | `d2lu.saturate` | `d2lu` | 64 | `0x43F0000000000000` | — | — | `0xFFFFFFFFFFFFFFFF` | 2^64 saturates to UINT64_MAX (kVM §4.7) |
| 27 | `d2lu.negative_fraction` | `d2lu` | 64 | `0xBFE0000000000000` | — | — | `0x0000000000000000` | −0.5 truncates toward zero to 0 |
| 28 | `f2l.exact` | `f2l` | 64 | `0x5E800000` | — | — | `0x4000000000000000` | 2^62 as a float is exact |
| 29 | `f2l.saturate` | `f2l` | 64 | `0x5F000000` | — | — | `0x7FFFFFFFFFFFFFFF` | 2^63 saturates to INT64_MAX; −2^63 (0xDF000000) is INT64_MIN exactly |
| 30 | `f2l.nan` | `f2l` | 64 | `0x7FC00000` | — | — | `0x0000000000000000` | NaN gives 0 |

# `Conv` — the VM's own conversions

`Conv` is a public static class used by the conversion opcodes and by the self-test.[^conv]

| Method | Behaviour |
| --- | --- |
| `double ULongToDouble(ulong u)` | Rounds `u` to 53 significant bits (to nearest, ties to even) in integer arithmetic, then scales with `Math.ScaleB` — a single rounding. |
| `double LongToDouble(long v)` | Sign-magnitude wrapper around `ULongToDouble`. |
| `float ULongToFloat(ulong u)` / `float LongToFloat(long v)` | The same with 24 significant bits and `MathF.ScaleB` — avoids the double rounding a `long → double → float` cast would cause. |
| `ulong DoubleToULong(double d)` | NaN or `d <= −1` → 0; `d >= 2⁶⁴` → `ulong.MaxValue`; `d >= 2⁶³` handled by subtracting 2⁶³ first; `d <= 0` → 0; else truncation. |
| `long DoubleToLong(double d)` | NaN → 0; `d >= 2⁶³` → `long.MaxValue`; `d < −2⁶³` → `long.MinValue`; else truncation. |
| `long FloatToLong(float f)` | `DoubleToLong(f)`. |
| `int FloatToInt(float f)` | NaN → 0; `f >= 2.1474836E+09` → `int.MaxValue`; `f <= −2.1474836E+09` → `int.MinValue`; else truncation. |
| `uint FloatToUInt(float f)` | NaN or `f <= 0` → 0; `f >= 4.2949673E+09` → `uint.MaxValue`; else truncation. |
| `int DoubleToInt(double d)` | NaN → 0; `d >= 2147483648` → `int.MaxValue`; `d <= −2147483649` → `int.MinValue`; else truncation. |
| `uint DoubleToUInt(double d)` | NaN or `d < 1` → 0; `d >= 4294967296` → `uint.MaxValue`; else truncation. |

Which opcode uses which helper is listed in [opcodes](/vm/opcodes.md). The 32-bit integer → float opcodes
(`I2F`, `U2F`) and the exact widenings (`I2D`, `U2D`, `F2D`) use plain C# casts.

## NaN handling in the interpreter

Arithmetic results pass through `CF` (32-bit) or `CD` (64-bit): a NaN is replaced by the canonical quiet
NaN `0x7FC00000` / `0x7FF8000000000000`, and if none of the inputs was a NaN and no record exists yet, a
`NanRecord(bytePc, task)` is stored as the machine's *first NaN* (`KvmMachine.cs:596-621`). The sign-bit
opcodes (`FNEG`, `FABS`, `DNEG`, `DABS`) and the comparisons do not canonicalise. The host reports the first
NaN once on its console and exposes it in snapshots (`HostComputer.cs:558-570`).

# edgecases.json — arithmetic conformance vectors

Version 1, 80 cases.[^edgecases-json] The file's header describes the encoding: values are typed strings —
`i32:`/`u32:`/`i64:` followed by a decimal or `0x` hex number, `f32:`/`f64:` followed by a bit pattern;
`args` are pushed in order (the first is deepest); `expect` is the resulting stack with the top last, or
`trap` names the trap raised.

`Not active in this build:` the table is loaded and version-checked (`GameTables.EdgeCasesData`) but no code
in the decompiled assemblies reads `Cases`; it is a specification artefact. It is nevertheless the most
compact statement of the semantics the interpreter implements. A sample from every `bullet` group
(`INT_MIN / -1`, division signs, shift-count masking, ÷0 results, NaN canonicalisation and payload
preservation, NaN comparisons, saturating conversions, round-to-nearest-even) was traced through
`KvmMachine` and `Conv` while writing [opcodes](/vm/opcodes.md); no disagreement was found.

```csharp
public sealed record EdgeCase(string Bullet, string Op, List<string> Args,
                              List<string>? Expect = null, string? Trap = null);
```

| # | Bullet | Op | Args (pushed in order) | Expected stack / trap |
| --- | --- | --- | --- | --- |
| 0 | wraps | `ADD` | `i32:2147483647`, `i32:1` | `i32:-2147483648` |
| 1 | wraps | `MUL` | `i32:65536`, `i32:65536` | `i32:0` |
| 2 | INT_MIN / -1 | `DIV` | `i32:-2147483648`, `i32:-1` | `i32:-2147483648` |
| 3 | INT_MIN / -1 | `REM` | `i32:-2147483648`, `i32:-1` | `i32:0` |
| 4 | INT_MIN / -1 | `DIV64` | `i64:-9223372036854775808`, `i64:-1` | `i64:-9223372036854775808` |
| 5 | INT_MIN / -1 | `REM64` | `i64:-9223372036854775808`, `i64:-1` | `i64:0` |
| 6 | division | `DIV` | `i32:-7`, `i32:2` | `i32:-3` |
| 7 | division | `REM` | `i32:-7`, `i32:2` | `i32:-1` |
| 8 | division | `DIV` | `i32:7`, `i32:-2` | `i32:-3` |
| 9 | division | `REM` | `i32:7`, `i32:-2` | `i32:1` |
| 10 | division | `DIV` | `i32:-7`, `i32:-2` | `i32:3` |
| 11 | division | `REM` | `i32:-7`, `i32:-2` | `i32:-1` |
| 12 | division | `DIV64` | `i64:-7`, `i64:2` | `i64:-3` |
| 13 | division | `REM64` | `i64:-7`, `i64:2` | `i64:-1` |
| 14 | division | `DIV64` | `i64:7`, `i64:-2` | `i64:-3` |
| 15 | division | `REM64` | `i64:7`, `i64:-2` | `i64:1` |
| 16 | division | `DIVU` | `u32:0x80000000`, `u32:2` | `u32:0x40000000` |
| 17 | division | `REMU` | `u32:0xFFFFFFFF`, `u32:10` | `u32:5` |
| 18 | division | `DIVU64` | `i64:0x8000000000000000`, `i64:2` | `i64:0x4000000000000000` |
| 19 | division | `REMU64` | `i64:0xFFFFFFFFFFFFFFFF`, `i64:10` | `i64:5` |
| 20 | division | `DIV` | `i32:1`, `i32:0` | trap `DIV_ZERO` |
| 21 | division | `REM` | `i32:1`, `i32:0` | trap `DIV_ZERO` |
| 22 | division | `DIVU` | `i32:1`, `i32:0` | trap `DIV_ZERO` |
| 23 | division | `REMU` | `i32:1`, `i32:0` | trap `DIV_ZERO` |
| 24 | division | `DIV64` | `i64:1`, `i64:0` | trap `DIV_ZERO` |
| 25 | division | `REM64` | `i64:1`, `i64:0` | trap `DIV_ZERO` |
| 26 | division | `DIVU64` | `i64:1`, `i64:0` | trap `DIV_ZERO` |
| 27 | division | `REMU64` | `i64:1`, `i64:0` | trap `DIV_ZERO` |
| 28 | shifts | `SHL` | `i32:1`, `i32:33` | `i32:2` |
| 29 | shifts | `SHR` | `u32:0x80000000`, `i32:31` | `i32:1` |
| 30 | shifts | `SAR` | `u32:0x80000000`, `i32:31` | `i32:-1` |
| 31 | shifts | `SHR` | `i32:-8`, `i32:1` | `u32:0x7FFFFFFC` |
| 32 | shifts | `SAR` | `i32:-8`, `i32:1` | `i32:-4` |
| 33 | shifts | `SAR` | `i32:-8`, `i32:33` | `i32:-4` |
| 34 | shifts | `SHL64` | `i64:1`, `i32:65` | `i64:2` |
| 35 | shifts | `SHR64` | `i64:-9223372036854775808`, `i32:63` | `i64:1` |
| 36 | shifts | `SAR64` | `i64:-9223372036854775808`, `i32:63` | `i64:-1` |
| 37 | shifts | `SHR64` | `i64:-8`, `i32:1` | `i64:0x7FFFFFFFFFFFFFFC` |
| 38 | float | `FDIV` | `f32:0x3F800000`, `f32:0x00000000` | `f32:0x7F800000` |
| 39 | float | `FDIV` | `f32:0xBF800000`, `f32:0x00000000` | `f32:0xFF800000` |
| 40 | float | `FDIV` | `f32:0x3F800000`, `f32:0x80000000` | `f32:0xFF800000` |
| 41 | float | `DDIV` | `f64:0x3FF0000000000000`, `f64:0x0000000000000000` | `f64:0x7FF0000000000000` |
| 42 | canonical NaN | `FDIV` | `f32:0x00000000`, `f32:0x00000000` | `f32:0x7FC00000` |
| 43 | canonical NaN | `FSUB` | `f32:0x7F800000`, `f32:0x7F800000` | `f32:0x7FC00000` |
| 44 | canonical NaN | `FADD` | `f32:0x7F800000`, `f32:0xFF800000` | `f32:0x7FC00000` |
| 45 | canonical NaN | `FMUL` | `f32:0x7F800000`, `f32:0x00000000` | `f32:0x7FC00000` |
| 46 | canonical NaN | `FSQRT` | `f32:0xBF800000` | `f32:0x7FC00000` |
| 47 | canonical NaN | `FADD` | `f32:0xFFC00001`, `f32:0x3F800000` | `f32:0x7FC00000` |
| 48 | canonical NaN | `FMUL` | `f32:0x3F800000`, `f32:0x7F800001` | `f32:0x7FC00000` |
| 49 | canonical NaN | `FDIV` | `f32:0x7FFFFFFF`, `f32:0x3F800000` | `f32:0x7FC00000` |
| 50 | canonical NaN | `DDIV` | `f64:0x0000000000000000`, `f64:0x0000000000000000` | `f64:0x7FF8000000000000` |
| 51 | canonical NaN | `DSUB` | `f64:0x7FF0000000000000`, `f64:0x7FF0000000000000` | `f64:0x7FF8000000000000` |
| 52 | canonical NaN | `DADD` | `f64:0x7FF0000000000000`, `f64:0xFFF0000000000000` | `f64:0x7FF8000000000000` |
| 53 | canonical NaN | `DSQRT` | `f64:0xBFF0000000000000` | `f64:0x7FF8000000000000` |
| 54 | canonical NaN | `DMUL` | `f64:0xFFF8000000000001`, `f64:0x3FF0000000000000` | `f64:0x7FF8000000000000` |
| 55 | canonical NaN | `D2F` | `f64:0xFFF8000000000001` | `f32:0x7FC00000` |
| 56 | canonical NaN | `F2D` | `f32:0xFFC00001` | `f64:0x7FF8000000000000` |
| 57 | canonical NaN | `FNEG` | `f32:0x7FC00001` | `f32:0xFFC00001` |
| 58 | canonical NaN | `FABS` | `f32:0xFFC00001` | `f32:0x7FC00001` |
| 59 | canonical NaN | `DNEG` | `f64:0x7FF8000000000001` | `f64:0xFFF8000000000001` |
| 60 | canonical NaN | `DABS` | `f64:0xFFF8000000000001` | `f64:0x7FF8000000000001` |
| 61 | NaN comparisons | `FNE` | `f32:0x7FC00000`, `f32:0x7FC00000` | `i32:1` |
| 62 | NaN comparisons | `FEQ` | `f32:0x7FC00000`, `f32:0x7FC00000` | `i32:0` |
| 63 | NaN comparisons | `FLT` | `f32:0x7FC00000`, `f32:0x3F800000` | `i32:0` |
| 64 | NaN comparisons | `FGE` | `f32:0x3F800000`, `f32:0x7FC00000` | `i32:0` |
| 65 | NaN comparisons | `DNE` | `f64:0x7FF8000000000000`, `f64:0x3FF0000000000000` | `i32:1` |
| 66 | NaN comparisons | `DLE` | `f64:0x7FF8000000000000`, `f64:0x3FF0000000000000` | `i32:0` |
| 67 | saturating conversions | `F2I` | `f32:0x4F32D05E` | `i32:2147483647` |
| 68 | saturating conversions | `F2I` | `f32:0xCF32D05E` | `i32:-2147483648` |
| 69 | saturating conversions | `F2I` | `f32:0x7FC00000` | `i32:0` |
| 70 | saturating conversions | `F2I` | `f32:0xBFC00000` | `i32:-1` |
| 71 | saturating conversions | `F2U` | `f32:0xBF800000` | `u32:0` |
| 72 | saturating conversions | `F2U` | `f32:0x4F9502F9` | `u32:0xFFFFFFFF` |
| 73 | saturating conversions | `D2I` | `f64:0x7FF0000000000000` | `i32:2147483647` |
| 74 | saturating conversions | `D2L` | `f64:0xFFF0000000000000` | `i64:-9223372036854775808` |
| 75 | saturating conversions | `D2LU` | `f64:0x7FF8000000000000` | `i64:0` |
| 76 | saturating conversions | `F2L` | `f32:0x7F800000` | `i64:9223372036854775807` |
| 77 | round to nearest even | `I2F` | `i32:16777217` | `f32:0x4B800000` |
| 78 | round to nearest even | `I2F` | `i32:16777219` | `f32:0x4B800002` |
| 79 | round to nearest even | `U2F` | `u32:0xFFFFFFFF` | `f32:0x4F800000` |

# Modding notes

- Soft routines are bookkeeping, not code: a mod cannot "provide" a faster routine. Costs and footprints
  can only be changed through the tables, and `runtime_lib_size` is recomputed and compared on every load.
- If a mod constructs its own `KvmMachine` (for tests or tooling) on an unusual runtime, catch
  `UnsupportedHostException`; its `Vector` property names the failing check.
- Host-side code that feeds floats to a program (message fields, service results; sensor fields exist
  only on the bench layout) is outside these
  guarantees: the VM canonicalises only the NaNs it produces itself. A NaN written by the host into a
  message slot or a result cell is read by the program bit for bit.
- The `Conv` helpers are public and are the right way to mirror kVM conversion semantics in mod code that
  must agree with a program's arithmetic.

[^soft-routines]: SoftRoutines.cs:5-53.
[^routines-json]: KSA.Kfs.Vm.Tables.Data.routines.json:1-40.
[^fp-self-test]: FpSelfTest.cs:8-103.
[^fpcheck-json]: KSA.Kfs.Vm.Tables.Data.fpcheck.json:1-37.
[^edgecases-json]: KSA.Kfs.Vm.Tables.Data.edgecases.json:1-86.
[^conv]: Conv.cs:6-162.
[^kvm-machine]: KvmMachine.cs:596-621 (`CF`, `CD`), 819-822 (profiler attribution), 1856-1860 (self-test in the constructor), 1925-1933 (`BuildSoftRoutineNames`, `SoftRoutineName`).
[^game-tables]: GameTables.cs:272-345 (routine/family validation and `Family[]`), 373 (`FamilyBytes`).
[^image-reader]: ImageReader.cs:570-589 (`Instr.Soft`, `runtime-lib` and `program-memory` rules).
[^costs-json]: KSA.Kfs.Vm.Tables.Data.costs.json:24-45 (soft cells), 85-87 (override).
