---
type: API Reference
title: kC standard library
description: Every function of the bundled kC library — math.c, vecmat.c and string.kasm — with signatures and behaviour, and how library source is pulled into a program and pruned.
resource: ksa-decomp:KSA.Kfs.KC/Library.cs
tags: [kfs, language, kc, library, math, vecmat, string, quaternion]
sources:
  - id: library
    resource: ksa-decomp:KSA.Kfs.KC/Library.cs
    title: Library (decompiled)
  - id: frontend
    resource: ksa-decomp:KSA.Kfs.KC/KcFrontEnd.cs
    title: KcFrontEnd (decompiled)
  - id: sysheaders
    resource: ksa-decomp:KSA.Kfs.KC/SystemHeaders.cs
    title: SystemHeaders (decompiled)
  - id: math-c
    resource: ksa-decomp:KSA.Kfs.KC.Library.math.c
    title: math.c (embedded kC library source)
  - id: vecmat-c
    resource: ksa-decomp:KSA.Kfs.KC.Library.vecmat.c
    title: vecmat.c (embedded kC library source)
  - id: string-kasm
    resource: ksa-decomp:KSA.Kfs.KC.Library.string.kasm
    title: string.kasm (embedded kASM library source)
  - id: sema
    resource: ksa-decomp:KSA.Kfs.KC/Sema.cs
    title: Sema (decompiled)
  - id: checks
    resource: ksa-decomp:KSA.Kfs.KC/Checks.cs
    title: Checks (decompiled)
  - id: offline-harness
    resource: /modding/offline-harness.md
    title: Offline harness experiments (E-numbered results)
generated: { by: claude-code/claude-fable-5-2, at: 2026-11-25T21:30:48Z }
status: stable
ksa_build: "2026.11.25.5949"
---

# What the library is

The kC library is **source code embedded in the game assembly**, compiled into each program that uses
it. There are three files, stored as manifest resources whose names end in `Library.<file>`:[^library-names]

| file | language | declared by | contents |
| --- | --- | --- | --- |
| `math.c` | kC | `<math.h>` | 16 `float` functions and their 16 `double` twins |
| `vecmat.c` | kC | `<vecmat.h>` | vectors, quaternions, 3×3 matrices in `float` and `double` |
| `string.kasm` | kASM | `<string.h>` | `memcpy`, `memmove`, `memset`, `memcmp` |

Because it is compiled with the program, library code runs on the kVM, costs cycles like player code
and appears in the profiler under its own function names. The header of `math.c` says so directly:
"compiled with the program (only the functions a task can reach are emitted, P-084, so they show up
in the profiler)".[^math-c-header]

# How it is linked in

1. **By header.** After each translation unit is preprocessed, the front end looks at every system
   file that unit read. `Library.SourceFor` maps `math.h` → `math.c` and `vecmat.h` → `vecmat.c`
   (everything else → none). A mapped source is queued as an extra translation unit named
   `<math.c>` / `<vecmat.c>`.[^library-sourcefor][^frontend-pending]
   `vecmat.c` includes `<vecmat.h>`, which includes `<math.h>`, so using `<vecmat.h>` brings in both.
2. **Library units are compiled like any other unit**, with two differences: the translation unit is
   flagged `IsLibrary`, and these lint warnings are not reported for it: `KC-W104`, `KC-W202`,
   `KC-W250`, `KC-W711`, `KC-W204`, `KC-W206` and the unused-name warnings
   (`KC-W006`).[^frontend-islibrary][^sema-inlibrary][^checks-library]
3. **`string.kasm` on demand.** The kASM file is assembled only if some unit ends up calling
   `memcpy`, `memmove`, `memset` or `memcmp` as a real function — that is, a call the compiler could
   not turn into an inline `COPY`/`ZERO` (see [kC builtins and intrinsics](/language/kc-builtins-and-intrinsics.md)).[^frontend-kasmlib][^library-kasmfns]
4. **Pruning.** After lowering, library functions not reachable (through `CALL`/`CALLF`) from an entry
   point or from any player function are deleted, and so are library globals that no remaining
   function or kept initialiser references — for example the `powf` lookup tables disappear unless
   `powf` is used.[^frontend-prune]

`Inferred:` a player cannot simply redefine a library function while including its header: the
program would then contain two definitions of the name and fail with `KC-E016` "'sinf' is defined in
'<math.c>' and in '…'". Not including the header (and declaring a prototype by hand) avoids pulling
the library unit in at all.[^frontend-linkdup]

`Library` itself is a `public static` class: `Files` (name, text pairs, read lazily once and cached),
`Get(name)`, `HostSources()` (the `.c` files), `SourceFor(header)` and the public set
`KasmFunctions`.[^library-names]

# `<math.h>`

Intrinsics (compiled to single instructions or inline tests, not library code):
`sqrtf`, `sqrt`, `fabsf`, `fabs`, `isnan`, `isinf`.[^sysheaders-math]

Library functions. Each `float` function uses only `float` arithmetic and each `double` function only
`double`; "special values follow C99 Annex F".[^math-c-header]

| `float` | `double` | behaviour |
| --- | --- | --- |
| `float sinf(float x)` | `double sin(double x)` | sine; argument reduced modulo π/2 in several exact parts. Returns `x` for tiny `x`; NaN for ±∞[^math-c-trig] |
| `float cosf(float x)` | `double cos(double x)` | cosine, same reduction |
| `float tanf(float x)` | `double tan(double x)` | tangent as sin/cos of the reduced argument |
| `float asinf(float x)` | `double asin(double x)` | arcsine via `atan(x / sqrt((1-x)(1+x)))`; NaN outside [−1, 1][^math-c-atan] |
| `float acosf(float x)` | `double acos(double x)` | arccosine via `2·atan(sqrt((1-x)/(1+x)))`; NaN outside [−1, 1] |
| `float atanf(float x)` | `double atan(double x)` | arctangent; ±π/2 at ±∞ |
| `float atan2f(float y, float x)` | `double atan2(double y, double x)` | four-quadrant arctangent with the Annex F special cases for zeros and infinities |
| `float expf(float x)` | `double exp(double x)` | eˣ; overflows to +∞ above 88.72 (`float`) / 709.78 (`double`), 0 below −103.97 / −745.13[^math-c-explog] |
| `float logf(float x)` | `double log(double x)` | natural log; −∞ at ±0, NaN for negative arguments |
| `float powf(float x, float y)` | `double pow(double x, double y)` | xʸ computed as 2^(y·log2 x) in double-length arithmetic, with the full Annex F case table (`pow(x, ±0) = 1`, `pow(1, y) = 1`, negative base with integer exponent, …)[^math-c-pow] |
| `float floorf(float x)` | `double floor(double x)` | round toward −∞, by bit manipulation[^math-c-round] |
| `float ceilf(float x)` | `double ceil(double x)` | round toward +∞ |
| `float fmodf(float x, float y)` | `double fmod(double x, double y)` | exact remainder with the sign of `x`, by long division on the significands; NaN for `y = 0` or infinite `x` |
| `float fminf(float x, float y)` | `double fmin(double x, double y)` | minimum; a NaN operand is ignored; prefers −0 over +0 |
| `float fmaxf(float x, float y)` | `double fmax(double x, double y)` | maximum; a NaN operand is ignored; prefers +0 over −0 |
| `float copysignf(float x, float y)` | `double copysign(double x, double y)` | magnitude of `x`, sign of `y` |

Accuracy note from the source: trigonometric reduction is accurate for |x| below about 10⁵ (`float`)
and 5·10⁷ (`double`); larger arguments are first reduced with `fmod` by a rounded 2π, which "stays
deterministic and in range, but is no longer close to the true value".[^math-c-trig]

Macros defined by `<math.h>`:[^sysheaders-math]

| macro | definition |
| --- | --- |
| `INFINITY` | `(3.40282347e+38F * 2.0F)` |
| `NAN` | `(0.0F / 0.0F)` |
| `HUGE_VALF` | `INFINITY` |
| `HUGE_VAL` | `(1.7976931348623157e+308 * 2.0)` |
| `M_PI` | `3.14159265358979323846` |
| `M_PI_2` | `1.57079632679489661923` |
| `M_SQRT2` | `1.41421356237309504880` |
| `M_E` | `2.71828182845904523536` |

`M_PI` and friends are `double` constants; in `float` code write `(float)M_PI` or multiply by an
`f`-suffixed literal to avoid `KC-W104`. There is no `log10`, `log2`, `exp2`, `round`, `trunc`,
`hypot`, or hyperbolic function in this build.

Implementation details that explain what shows up in a profile: `math.c` begins with
`#pragma kfs bounds off`; it reads and writes float bit patterns with `memcpy` of 4 or 8 bytes (which
the compiler inlines as `COPY`); every loop has a constant bound; the `double` polynomials are written
as statement sequences because "the nested form of degree 14 needs more cells than a task has".
Internal helpers are `static`: `fc_scale`, `fc_atan01`, `fc_int_kind`, the tables `fc_log2_hi`,
`fc_log2_lo`, and the `fd_*` equivalents (`fd_sin_poly`, `fd_cos_poly`, `fd_exp_poly`, `fd_scale`,
`fd_log_poly`, `fd_atan_poly`, `fd_atan01`, `fd_log2_tail`, `fd_exp2_poly`, `fd_int_kind`,
`fd_log2_hi`, `fd_log2_lo`).[^math-c-impl]

# `<vecmat.h>`

`<vecmat.h>` includes `<kfs.h>` (for the types) and `<math.h>`.[^sysheaders-vecmat] Conventions, from
the source's header comment:[^vecmat-c-header]

- quaternions are `(x, y, z, w)`;
- `quat_compose(q_ab, q_bc)` is the Hamilton product `q_bc ⊗ q_ab` — "first A to B, then B to C";
- `mat3` is row-major, `m[row][col]`;
- sums are evaluated in a fixed written order so every build gives the same bits;
- each function has a **value form** (structs passed and returned by value) and a **pointer form**
  with suffix `_p` whose first parameter is `out`; `out` may alias an argument.

## `float` API

| value form | pointer form | behaviour |
| --- | --- | --- |
| `vec3 vec3_zero(void)` | | (0, 0, 0) |
| `vec3 vec3_make(float x, float y, float z)` | | builds a vector |
| `vec3 vec3_add(vec3 a, vec3 b)` | `void vec3_add_p(vec3 *out, const vec3 *a, const vec3 *b)` | a + b |
| `vec3 vec3_sub(vec3 a, vec3 b)` | `void vec3_sub_p(vec3 *out, const vec3 *a, const vec3 *b)` | a − b |
| `vec3 vec3_neg(vec3 a)` | `void vec3_neg_p(vec3 *out, const vec3 *a)` | −a |
| `vec3 vec3_scale(vec3 a, float k)` | `void vec3_scale_p(vec3 *out, const vec3 *a, float k)` | k·a |
| `float vec3_dot(vec3 a, vec3 b)` | | dot product |
| `vec3 vec3_cross(vec3 a, vec3 b)` | `void vec3_cross_p(vec3 *out, const vec3 *a, const vec3 *b)` | a × b |
| `float vec3_length(vec3 a)` | | Euclidean length |
| `vec3 vec3_normalize(vec3 a)` | `void vec3_normalize_p(vec3 *out, const vec3 *a)` | unit vector; the zero vector is returned unchanged |
| `quat quat_identity(void)` | | (0, 0, 0, 1) |
| `quat quat_compose(quat q_ab, quat q_bc)` | `void quat_compose_p(quat *out, const quat *q_ab, const quat *q_bc)` | rotation A→C |
| `quat quat_inverse(quat q)` | `void quat_inverse_p(quat *out, const quat *q)` | conjugate (−x, −y, −z, w) |
| `quat quat_normalize(quat q)` | `void quat_normalize_p(quat *out, const quat *q)` | unit quaternion; the zero quaternion becomes identity |
| `vec3 quat_rotate(quat q_ab, vec3 v_a)` | `void quat_rotate_p(vec3 *out, const quat *q_ab, const vec3 *v_a)` | rotates a vector from frame A to frame B |
| `quat quat_from_axis_angle(vec3 axis, float angle_rad)` | `void quat_from_axis_angle_p(quat *out, const vec3 *axis, float angle_rad)` | axis is used as given (not normalised) |
| `vec3 quat_attitude_error(quat q_target, quat q_current)` | `void quat_attitude_error_p(vec3 *out, const quat *q_target, const quat *q_current)` | body-frame rotation vector taking current to target: twice the vector part of `q_current⁻¹ ⊗ q_target`, with the sign chosen so `w ≥ 0` |
| `mat3 mat3_identity(void)` | | identity |
| `mat3 mat3_transpose(mat3 m)` | `void mat3_transpose_p(mat3 *out, const mat3 *m)` | transpose |
| `mat3 mat3_scale(mat3 m, float k)` | `void mat3_scale_p(mat3 *out, const mat3 *m, float k)` | k·m |
| `vec3 mat3_mul_vec3(mat3 m, vec3 v)` | `void mat3_mul_vec3_p(vec3 *out, const mat3 *m, const vec3 *v)` | m·v |
| `mat3 mat3_mul(mat3 a, mat3 b)` | `void mat3_mul_p(mat3 *out, const mat3 *a, const mat3 *b)` | a·b |
| `mat3 mat3_inverse(mat3 m)` | `void mat3_inverse_p(mat3 *out, const mat3 *m)` | adjugate over determinant; a singular matrix (determinant exactly 0) gives the zero matrix |

Source: [^vecmat-c-float].

## `double` API

The same 40 functions exist with a `d` prefix on every type and function name and `double` scalars:
`dvec3_zero`, `dvec3_make`, `dvec3_add`, `dvec3_sub`, `dvec3_neg`, `dvec3_scale`, `dvec3_dot`,
`dvec3_cross`, `dvec3_length`, `dvec3_normalize`, `dvec3_add_p`, `dvec3_sub_p`, `dvec3_neg_p`,
`dvec3_scale_p`, `dvec3_cross_p`, `dvec3_normalize_p`, `dquat_identity`, `dquat_compose`,
`dquat_inverse`, `dquat_normalize`, `dquat_rotate`, `dquat_from_axis_angle`, `dquat_attitude_error`,
`dquat_compose_p`, `dquat_inverse_p`, `dquat_normalize_p`, `dquat_rotate_p`,
`dquat_from_axis_angle_p`, `dquat_attitude_error_p`, `dmat3_identity`, `dmat3_transpose`,
`dmat3_scale`, `dmat3_mul_vec3`, `dmat3_mul`, `dmat3_inverse`, `dmat3_transpose_p`, `dmat3_scale_p`,
`dmat3_mul_vec3_p`, `dmat3_mul_p`, `dmat3_inverse_p`. For example
`dvec3 dquat_rotate(dquat q_ab, dvec3 v_a)`.[^vecmat-c-double]

## Conversions

| function | behaviour |
| --- | --- |
| `vec3 vec3_from_d(dvec3 v)` / `dvec3 dvec3_from_f(vec3 v)` | componentwise narrow / widen |
| `quat quat_from_d(dquat q)` / `dquat dquat_from_f(quat q)` | componentwise |
| `mat3 mat3_from_d(dmat3 m)` / `dmat3 dmat3_from_f(mat3 m)` | componentwise |

Source: [^vecmat-c-conv]. The shipped example `prograde_hold.c` uses `vec3_from_d(nav.v_cci_mps)` to
bring a `dvec3` message field into `float` arithmetic.

# `<string.h>`

The four routines are hand-written kASM. Their `.proto` lines:[^string-kasm]

```c
void *memcpy(void *d, const void *s, uint32_t n);
void *memmove(void *d, const void *s, uint32_t n);
void *memset(void *d, int32_t c, uint32_t n);
int32_t memcmp(const void *a, const void *b, uint32_t n);
```

| function | behaviour |
| --- | --- |
| `memcpy` | byte loop ascending; returns `d` |
| `memmove` | copies descending when `d` lies above `s`, ascending otherwise; returns `d` |
| `memset` | stores the low byte of `c`; returns `d` |
| `memcmp` | returns −1, 0 or 1 at the first differing byte compared as `unsigned char` |

Each loop carries a declared bound of 1,048,576 iterations ("the largest RAM"), which is what the
worst-case analysis assumes for a variable-length call. Prefer constant lengths: `memcpy(&a, &b,
sizeof a)` compiles to one `COPY` instruction and never reaches this code.

There are no string functions (`strlen`, `strcpy`, …) in this build; `<string.h>` declares only the
four memory routines.[^sysheaders-string]

# Modding notes

- `Library.Names` (private static), `Library.SourceFor` (a two-case `if`) and
  `Library.KasmFunctions` are hard-coded. A mod cannot register a third library source through data;
  to ship reusable kC code it can either put `static` functions and macros entirely in a header it
  injects (see [kC generated headers](/language/kc-generated-headers.md)), or patch `SourceFor` and
  `Library.Get`/`Files` together so that including its header queues its own `<name.c>` unit.
  `Verified:` the first route compiles, but an unused `static` helper draws `KC-W006` in every program
  that includes the header (the warning exempts only library units, not system
  headers[^frontend-islibrary]) and a non-`static` body included from two `.c` files is `KC-E016`; a
  prototype-and-`#define` header is the safe shape.[^offline-harness] `Unverified:` the second route
  has not been tried.
- Library units are recognised everywhere by their file name starting with `<`: pruning
  (`IsLibrary(Span)`), lint suppression in `Checks.Function`, and the unused warnings all test that
  prefix.[^frontend-prune][^checks-library]
- Library text is cached in a static field after the first read; resource text is normalised to `\n`
  line ends.[^library-names]

[^library-names]: Library.cs:9-36
[^library-sourcefor]: Library.cs:38-49
[^library-kasmfns]: Library.cs:15
[^frontend-pending]: KcFrontEnd.cs:71-72, 103-115, 319-326
[^frontend-islibrary]: KcFrontEnd.cs:74, 136-140, 183-186
[^frontend-kasmlib]: KcFrontEnd.cs:154-163
[^frontend-prune]: KcFrontEnd.cs:804-869
[^frontend-linkdup]: KcFrontEnd.cs:441-451
[^sema-inlibrary]: Sema.cs:310, 781, 1121
[^checks-library]: Checks.cs:126-132
[^sysheaders-math]: SystemHeaders.cs:16
[^sysheaders-vecmat]: SystemHeaders.cs:21
[^sysheaders-string]: SystemHeaders.cs:17
[^math-c-header]: KSA.Kfs.KC.Library.math.c:1-21
[^math-c-trig]: KSA.Kfs.KC.Library.math.c:215-289, 691-781
[^math-c-explog]: KSA.Kfs.KC.Library.math.c:291-354, 783-877
[^math-c-atan]: KSA.Kfs.KC.Library.math.c:356-423, 879-962
[^math-c-pow]: KSA.Kfs.KC.Library.math.c:425-544, 964-1105
[^math-c-round]: KSA.Kfs.KC.Library.math.c:546-685, 1107-1244
[^math-c-impl]: KSA.Kfs.KC.Library.math.c:9-21, 294, 359, 453, 709-731, 785-846, 881-907, 988-1024
[^vecmat-c-header]: KSA.Kfs.KC.Library.vecmat.c:1-9
[^vecmat-c-float]: KSA.Kfs.KC.Library.vecmat.c:13-142
[^vecmat-c-double]: KSA.Kfs.KC.Library.vecmat.c:146-272
[^vecmat-c-conv]: KSA.Kfs.KC.Library.vecmat.c:276-291
[^string-kasm]: KSA.Kfs.KC.Library.string.kasm:1-161
[^offline-harness]: Experiment E14 in [the offline harness](/modding/offline-harness.md).
