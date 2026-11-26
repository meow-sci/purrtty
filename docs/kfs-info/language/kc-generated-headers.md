---
type: Extension Point
title: kC system and generated headers
description: Exactly which header text kC sees, where each piece comes from (hard-coded strings versus the services, messages and layout tables), when it is generated and cached, and what a mod must change for new functions, messages, enums or I/O fields to appear.
resource: ksa-decomp:KSA.Kfs.KC/VehicleHeader.cs
tags: [kfs, language, kc, headers, vehicle-h, kfs-h, codegen, extension, modding]
sources:
  - id: sysheaders
    resource: ksa-decomp:KSA.Kfs.KC/SystemHeaders.cs
    title: SystemHeaders (decompiled)
  - id: vehicleheader
    resource: ksa-decomp:KSA.Kfs.KC/VehicleHeader.cs
    title: VehicleHeader (decompiled)
  - id: genheaders
    resource: ksa-decomp:KSA.Kfs.KC/GeneratedHeaders.cs
    title: GeneratedHeaders (decompiled)
  - id: frontend
    resource: ksa-decomp:KSA.Kfs.KC/KcFrontEnd.cs
    title: KcFrontEnd (decompiled)
  - id: parser
    resource: ksa-decomp:KSA.Kfs.KC/Parser.cs
    title: Parser (decompiled)
  - id: msglayout
    resource: ksa-decomp:KSA.Kfs.Vm.Tables/MessageLayout.cs
    title: MessageLayout (decompiled)
  - id: builtinincludes
    resource: ksa-decomp:KSA.Kfs.KAsm/BuiltinIncludes.cs
    title: kASM BuiltinIncludes (decompiled)
  - id: catenums
    resource: ksa-decomp:KSA.Kfs.Vm.Tables/CatalogueEnums.cs
    title: CatalogueEnums (decompiled)
  - id: gametables
    resource: ksa-decomp:KSA.Kfs.Vm.Tables/GameTables.cs
    title: GameTables (decompiled)
  - id: workbench
    resource: ksa-decomp:KSA.Kfs.Host.Workbench/Workbench.cs
    title: Workbench (decompiled)
  - id: workspace
    resource: ksa-decomp:KSA.Kfs.Host.Workbench/Workspace.cs
    title: Workspace (decompiled)
  - id: bench-json
    resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.bench_layout.json
    title: bench_layout.json (embedded table)
  - id: messages-json
    resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.messages.json
    title: messages.json (embedded table)
  - id: input
    resource: ksa-decomp:KSA.Kfs.Compiler/CompileInput.cs
    title: CompileInput (decompiled)
  - id: offline-harness
    resource: /modding/offline-harness.md
    title: Offline harness experiments E1–E12
generated: { by: claude-code/claude-fable-5-2, at: 2026-11-25T21:30:48Z }
status: stable
ksa_build: "2026.11.25.5949"
---

# Summary

A kC program sees header text from four places:

| header | produced by | from what | when |
| --- | --- | --- | --- |
| `<stdint.h>`, `<stdbool.h>`, `<stddef.h>`, `<limits.h>`, `<float.h>`, `<assert.h>`, `<math.h>`, `<string.h>`, `<stdlib.h>`, `<stdio.h>`, `<kfs.h>`, `<vecmat.h>` | `SystemHeaders.Files` | **string literals compiled into the game** | looked up on every `#include`[^sysheaders-files] |
| `<vehicle.h>` | `VehicleHeader.Generate(layout, host, tables)` | the vehicle's `IoLayout` and the message catalogue | once per kC compile, cached per layout object[^frontend-vehicle][^vehicleheader-generate] |
| `"X.kasm.h"` | `KcFrontEnd.Assemble` | the `.proto` lines of the project's `X.kasm` | every build of a project that contains kASM[^frontend-kasmh] |
| `vehicle.h`, `vehicle.kinc` files on disk | `GeneratedHeaders.Write` | the same generators plus a banner | `kfs build`, `kfs load`-time builds and `kfs headers`; **for reading only**[^workbench-headers] |

The single most important fact for extending kFS: **`<kfs.h>` is not generated.** The `kfs_*`
prototypes, the `KFS_*` constants, `kfs_sys_t` and the vector typedefs are a fixed string. Only
`<vehicle.h>` follows the game tables.

## What follows the tables and what does not

| what kC code sees | source | table-driven? |
| --- | --- | --- |
| prototypes `kfs_set_throttle(float)` … | literal text of `<kfs.h>`[^sysheaders-kfsh] | **no** |
| which names are builtins | `SystemHeaders.Builtins` literal set[^sysheaders-builtins] | **no** |
| builtin name → service id | `Parser.Bindings`, from each service row's `kc` column[^parser-bindings] | yes (cached per `GameTables` object) |
| argument / result cell counts of a service call | `GameTables.Services[id]` | yes |
| special handling of `kfs_logf`, `trace`, `kfs_sleep_until_event`, and of the math/memory intrinsics | `switch` on the function name in `Parser`[^parser-switches] | **no** |
| `KFS_ROLL_*`, `KFS_VLF_*`, `KFS_PROFILE_*`, `KFS_BURN_*`, … | literal `#define`s in `<kfs.h>` | **no** — `enums.json` is not read by the kC front end |
| `KFS_EVENT_*` | literal `#define`s; the *validation* of `kfs_sleep_until_event` reads `events.json`[^parser-events] | names no, checks yes |
| `kfs_sys_t`, `sys` | literal text; `sysblock.json` is not consulted | **no** |
| `vec3`, `quat`, `mat3`, `dvec3`, `dquat`, `dmat3` | literal text | **no** |
| `MSG_*` ids and `kfs_msg_*_t` structs | `VehicleHeader.Messages` over `tables.MessagesData.Messages`[^vehicleheader-messages] | **yes** |
| `kfs_sensors_t`, `kfs_commands_t`, `sensors`, `commands` | `VehicleHeader.Block` over the `IoLayout` fields[^vehicleheader-block] | **yes** |
| `KFS_LAYOUT_FINGERPRINT` | `IoLayout.Fingerprint` | yes |
| `THRUSTER_COUNT`, `ENGINE_COUNT`, `GIMBAL_COUNT` | a hard-coded list of three group names; values counted from the layout[^vehicleheader-counts] | names **no**, values yes |

For contrast, kASM's built-in `<kfs.kinc>` **is** generated from the tables: one `SVC_*` constant per
non-reserved service row (from its `symbol`), `sys.*` addresses from `sysblock.json`, `TRAP_*` from
`traps.json`, and `KFS_*` enum constants from `CatalogueEnums.All` — which itself only covers a
hard-coded list of enum groups.[^builtinincludes-kfs][^catenums-groups] See
[kASM assembly language](/compiler/kasm-assembly-language.md).

# The built-in system headers

`SystemHeaders` is a `public static` class with three public static readonly collections:[^sysheaders-files]

| member | type | content |
| --- | --- | --- |
| `Files` | `Dictionary<string, string>` (ordinal) | header name → text, stored with `\r\n` line ends |
| `Builtins` | `HashSet<string>` (ordinal) | function names eligible to be builtins ([kC builtins and intrinsics](/language/kc-builtins-and-intrinsics.md)) |
| `Deferred` | `HashSet<string>` (ordinal) | `{ "matn.h" }` — names rejected as not implemented |

`Get(name)` returns `Files[name]` with `\r\n` replaced by `\n`, or `null`. It is called on every
include; there is no cache.[^sysheaders-get]

## `<kfs.h>` in full

```c
#pragma once
#include <stdint.h>
#include <stdbool.h>
#include <stddef.h>
#include <assert.h>
#ifndef static_assert
#define static_assert _Static_assert
#endif
#define KFS_CORE_K8 0
#define KFS_CORE_K32 1
#define KFS_CORE_K32F 2
#define KFS_CORE_K32D 3
typedef uint32_t kfs_bool32_t;
typedef uint32_t kfs_burn_t;
typedef struct {
    uint32_t cycles;
    uint32_t budget;
    uint32_t tick;
    float t_s;
    float dt_s;
    uint32_t overruns;
    uint32_t task_id;
    float task_dt_s;
    uint32_t wake_reason;
    uint32_t tier;
    uint32_t core;
    uint32_t soi_changes;
    int64_t met_us;
    uint32_t nv_writes;
    uint32_t reset_cause;
    uint32_t rejected_requests;
    uint32_t reserved[47];
} kfs_sys_t;
extern const kfs_sys_t sys __at(0xA0000000);
#define DEG2RAD 0.017453292519943295f
#define RAD2DEG 57.29577951308232f
#define KM 1000.0f
#define G0 9.80665f
#define KFS_EVENT_APOAPSIS 1
#define KFS_EVENT_PERIAPSIS 2
#define KFS_EVENT_SOI_CHANGE 3
#define KFS_EVENT_ATMOSPHERE_ENTRY 4
#define KFS_EVENT_BURN_START 5
#define KFS_EVENT_ALTITUDE_BELOW 6
#define KFS_EVENT_TIME 7
#define KFS_EVENT_MESSAGE 8
#define KFS_ATT_MODE_NONE 0
#define KFS_ATT_MODE_RATE 1
#define KFS_ATT_MODE_ATTITUDE 2
#define KFS_ROLL_FREE 0
#define KFS_ROLL_UP 1
#define KFS_ROLL_DOWN 2
#define KFS_BURN_NONE 0
#define KFS_BURN_PLANNED 1
#define KFS_BURN_ALIGNING 2
#define KFS_BURN_BURNING 3
#define KFS_BURN_DONE 4
#define KFS_BURN_ABORTED 5
#define KFS_BURN_PENDING 6
#define KFS_ENU_FORWARD 2
#define KFS_ENU_BACKWARD 3
#define KFS_ENU_UP 4
#define KFS_ENU_DOWN 5
#define KFS_LVLH_AHEAD 6
#define KFS_LVLH_BEHIND 7
#define KFS_LVLH_RADIAL_OUT 8
#define KFS_LVLH_RADIAL_IN 9
#define KFS_VLF_PROGRADE 10
#define KFS_VLF_RETROGRADE 11
#define KFS_VLF_NORMAL 12
#define KFS_VLF_ANTINORMAL 13
#define KFS_VLF_OUTWARD 14
#define KFS_VLF_INWARD 15
#define KFS_BURN_PLUS_DV 16
#define KFS_BURN_MINUS_DV 17
#define KFS_TARGET_TOWARD 18
#define KFS_TARGET_AWAY 19
#define KFS_TARGET_ANTIVELOCITY 20
#define KFS_TARGET_ALIGN 21
#define KFS_PROFILE_STRICT 0
#define KFS_PROFILE_BALANCED 1
#define KFS_PROFILE_RELAXED 2
#define KFS_AT_APOAPSIS 0
#define KFS_AT_PERIAPSIS 1
#define KFS_BURN_END_NONE 0
#define KFS_BURN_END_COMPLETED 1
#define KFS_BURN_END_PROPELLANT_OUT 2
#define KFS_BURN_END_STAGE_RAN_DRY 3
#define KFS_BURN_END_IGNITION_DENIED 4
#define KFS_BURN_END_PLANNING_FAILED 5
#define KFS_BURN_END_SUPERSEDED 6
#define KFS_BURN_END_CANCELLED 7
#ifndef KFS_VECMAT_TYPES
#define KFS_VECMAT_TYPES
typedef struct { float x, y, z; } vec3;
typedef struct { float x, y, z, w; } quat;
typedef struct { float m[3][3]; } mat3;
typedef struct { double x, y, z; } dvec3;
typedef struct { double x, y, z, w; } dquat;
typedef struct { double m[3][3]; } dmat3;
#endif
void kfs_halt(void);
void kfs_sleep_until(int64_t t_us);
void kfs_sleep_until_event(uint32_t event, uint32_t param);
void kfs_stage(void);
void kfs_hold_direction(uint32_t dir, uint32_t roll);
void kfs_hold_rates(void);
void kfs_release_attitude(void);
void kfs_set_attitude_profile(uint32_t profile);
void kfs_set_throttle(float x);
void kfs_engines_on(void);
void kfs_translate(vec3 dv);
void kfs_hold_heading(float heading_rad, float pitch_rad, uint32_t roll);
void kfs_hold_quat(quat q, uint32_t roll);
void kfs_engines_off(void);
kfs_burn_t kfs_plan_circularize(uint32_t at);
kfs_burn_t kfs_plan_burn(vec3 dv, int64_t t_us);
void kfs_burn_start(kfs_burn_t b);
void kfs_burn_abort(void);
void trace(const char *channel, float v);
void kfs_logf(const char *fmt, ...);
```

Source: [^sysheaders-kfsh]. The 20 prototypes correspond one-to-one to the 20 non-reserved rows of
`services.json`, and the constants to rows of `events.json`, `enums.json` and the "meaning" text of
`messages.json` — but that correspondence was established by whoever wrote the string, not by code.

## The C-library headers in full

`<stdint.h>`:[^sysheaders-std]

```c
#pragma once
typedef signed char int8_t;
typedef unsigned char uint8_t;
typedef short int16_t;
typedef unsigned short uint16_t;
typedef int int32_t;
typedef unsigned int uint32_t;
typedef long long int64_t;
typedef unsigned long long uint64_t;
typedef int32_t intptr_t;
typedef uint32_t uintptr_t;
typedef int64_t intmax_t;
typedef uint64_t uintmax_t;
#define INT8_MIN (-128)
#define INT8_MAX 127
#define UINT8_MAX 255
#define INT16_MIN (-32768)
#define INT16_MAX 32767
#define UINT16_MAX 65535
#define INT32_MIN (-2147483647 - 1)
#define INT32_MAX 2147483647
#define UINT32_MAX 4294967295u
#define INT64_MIN (-9223372036854775807LL - 1)
#define INT64_MAX 9223372036854775807LL
#define UINT64_MAX 18446744073709551615ULL
#define INT8_C(v) (v)
#define INT16_C(v) (v)
#define INT32_C(v) (v)
#define INT64_C(v) (v ## LL)
#define UINT8_C(v) (v)
#define UINT16_C(v) (v)
#define UINT32_C(v) (v ## u)
#define UINT64_C(v) (v ## ULL)
```

`<stdbool.h>`, `<stddef.h>`, `<assert.h>`, `<string.h>`, `<stdlib.h>`, `<stdio.h>`:[^sysheaders-std]

```c
/* stdbool.h */
#pragma once
#define bool _Bool
#define true 1
#define false 0
#define __bool_true_false_are_defined 1

/* stddef.h */
#pragma once
typedef unsigned int size_t;
typedef int ptrdiff_t;
#define NULL ((void *)0)
#define offsetof(t, m) __kfs_offsetof(t, m)

/* assert.h */
#pragma once
void __kfs_assert(int condition);
#ifdef NDEBUG
#define assert(e) ((void)0)
#else
#define assert(e) __kfs_assert(e)
#endif

/* string.h */
#pragma once
void *memcpy(void *d, const void *s, unsigned int n);
void *memmove(void *d, const void *s, unsigned int n);
void *memset(void *d, int c, unsigned int n);
int memcmp(const void *a, const void *b, unsigned int n);

/* stdlib.h — declared only so that calls can be rejected (KC-E302) */
#pragma once
void *malloc(unsigned int n);
void *calloc(unsigned int n, unsigned int m);
void *realloc(void *p, unsigned int n);
void free(void *p);

/* stdio.h — declared only so that calls can be rejected (KC-E645) */
#pragma once
int printf(const char *format, ...);
int puts(const char *s);
```

(The `/* name */` and explanatory comments are added here; each header's text starts at its
`#pragma once`.)

`<limits.h>` and `<float.h>`:[^sysheaders-std]

```c
/* limits.h */
#pragma once
#define CHAR_BIT 8
#define SCHAR_MIN (-128)
#define SCHAR_MAX 127
#define UCHAR_MAX 255
#define CHAR_MIN (-128)
#define CHAR_MAX 127
#define SHRT_MIN (-32768)
#define SHRT_MAX 32767
#define USHRT_MAX 65535
#define INT_MIN (-2147483647 - 1)
#define INT_MAX 2147483647
#define UINT_MAX 4294967295u
#define LONG_MIN (-2147483647L - 1)
#define LONG_MAX 2147483647L
#define ULONG_MAX 4294967295uL
#define LLONG_MIN (-9223372036854775807LL - 1)
#define LLONG_MAX 9223372036854775807LL
#define ULLONG_MAX 18446744073709551615ULL

/* float.h */
#pragma once
#define FLT_RADIX 2
#define FLT_MANT_DIG 24
#define FLT_DIG 6
#define FLT_EPSILON 1.19209290e-07F
#define FLT_MIN 1.17549435e-38F
#define FLT_TRUE_MIN 1.40129846e-45F
#define FLT_MAX 3.40282347e+38F
#define FLT_MIN_EXP (-125)
#define FLT_MAX_EXP 128
#define DBL_MANT_DIG 53
#define DBL_DIG 15
#define DBL_EPSILON 2.2204460492503131e-16
#define DBL_MIN 2.2250738585072014e-308
#define DBL_TRUE_MIN 4.9406564584124654e-324
#define DBL_MAX 1.7976931348623157e+308
#define DBL_MIN_EXP (-1021)
#define DBL_MAX_EXP 1024
#define FLT_EVAL_METHOD 0
```

`<math.h>` and `<vecmat.h>` are listed function by function in
[kC standard library](/language/kc-standard-library.md).

# `<vehicle.h>`

## When and how it is produced

`KcFrontEnd.Lower` calls `VehicleHeader.Generate(input.Layout, host: false, input.Game)` near the
start of **every** kC compile, whether or not the program includes the header, and hands the text to
the include resolver under the name `vehicle.h`.[^frontend-vehicle]

`Generate` caches: a `ConditionalWeakTable<IoLayout, GeneratedText>` holds, per layout **object**, the
last text and the `GameTables` reference it was built with. The cached text is reused when the same
`tables` reference is passed again; a different `tables` object triggers a rebuild. The `host`
parameter is not used — the text is the same either way and distinguishes host builds with
`#ifdef __KFS_HOST__`.[^vehicleheader-generate]

Consequences:

- For the bench target (`GameTables.BenchLayout`, one object for the life of the tables) the header is
  built once per session.
- For a vehicle, a new header is built whenever the host produces a new `IoLayout` object; see
  [I/O layout generation](/host/io-layout-generation.md) and
  [controller set and layout changes](/integration/controller-set-and-layout-changes.md).
- **Mutating a `GameTables` object in place after the header for a layout has been generated does not
  refresh that layout's header.**

If the layout cannot be expressed as a C struct, `Build` throws `InvalidOperationException` (see
"Layout rules"); that surfaces as a failed compile, not as a diagnostic.

## Template

`VehicleHeader.Build` emits, in this order:[^vehicleheader-build]

1. A fixed two-line comment, then `#pragma once` and `#include <kfs.h>`.
2. `#define KFS_LAYOUT_FINGERPRINT 0x%08Xu` from `layout.Fingerprint`.
3. For each of the three group names `thruster`, `engine`, `gimbal` (a literal array):
   `#define <GROUP>_COUNT <n>`, where `n` is the number of distinct indices `group[i]` among the
   layout's **command** fields.[^vehicleheader-counts][^msglayout-groupcount]
4. If the layout has sensor fields: the sensor block, as type `kfs_sensors_t`, with group element
   types named `kfs_<group>_t`.
5. If the layout has command fields: the command block, as type `kfs_commands_t`, with group element
   types named `kfs_<group>_cmd_t`.
6. If `tables` is not null: the message section.
7. If there are any I/O fields: the object declarations

   ```c
   #ifdef __KFS_HOST__
   extern kfs_sensors_t sensors;
   extern kfs_commands_t commands;
   #else
   extern const kfs_sensors_t sensors __at(0x80000000);
   extern kfs_commands_t commands __at(0x90000000);
   #endif
   ```

   (each line only when that block has fields).

### I/O blocks

A layout field is `IoField(Name, Type, Offset, Size, OneShot)`; its name is a dotted path with
optional indices, such as `imu.accel_body_mps2.x` or `thruster[3].pulse_s`. `Tree` turns the field
list of one block into a tree: the first path segment is a top-level **group**, `[i]` makes a node an
array whose length is the largest index plus one, and the last segment is a scalar leaf.[^vehicleheader-tree]

Layout field types map to C types as follows; any other type name throws
`InvalidOperationException("unknown layout field type '…'")`:[^vehicleheader-ctype]

| layout type | C type | size |
| --- | --- | --- |
| `float32` | `float` | 4 |
| `float64` | `double` | 8 |
| `int32` / `uint32` | `int32_t` / `uint32_t` | 4 |
| `int16` / `uint16` | `int16_t` / `uint16_t` | 2 |
| `int8` / `uint8` | `int8_t` / `uint8_t` | 1 |
| `int64` / `uint64` | `int64_t` / `uint64_t` | 8 |
| `bool` | `kfs_bool32_t` | 4 |

`Block` then writes:[^vehicleheader-block]

- for every top-level group that is a struct (or an array of structs):
  `typedef struct { … } <element name>;` with nested anonymous structs for inner path segments,
  indented four spaces per level;
- `typedef struct {` … `} kfs_sensors_t;` (or `kfs_commands_t`) with one member per top-level group:
  `<element name or scalar type> <group>[dims];`
- `uint8_t kfs_padN[n];` members wherever the layout leaves a gap between groups, and at the end up
  to the block size.

### Layout rules

The generator recomputes natural C layout (each scalar aligned to its size, structs aligned to their
widest member and padded to that alignment) and **verifies every field's offset against the
layout**. A mismatch throws, for example "the sensor block's field 'x' is at offset 12 in the layout
but 16 in natural layout (FI §5.1, P-082)".[^vehicleheader-tree][^vehicleheader-layout] A layout that
is to be visible from kC must therefore already be laid out by C rules within each group; only gaps
*between* top-level groups are tolerated and padded.

### Messages

`Messages` writes, for the whole catalogue (`MessageLayout.All(tables, layout)`):[^vehicleheader-messages]

```c
typedef struct kfs_msg_header { uint32_t seq; uint16_t source; uint16_t flags; int64_t t_us; } kfs_msg_header_t;
#define <NAME> 0x<ID as 4 hex digits>          /* one per message, in table order */
/* then, per message, in table order: */
typedef struct kfs_vehicle_props_<group> { <type> <member>[<n>]; … } kfs_vehicle_props_<group>_t;   /* per element type */
typedef struct kfs_msg_<lower name> { kfs_msg_header_t hdr; <type> <member>; … <elem type> <group>[<GROUP>_COUNT]; } kfs_msg_<lower name>_t;
```

Each typedef is a single line. How a `MessageRow` becomes members is decided by `MessageLayout.Build`:[^msglayout-build]

- The struct type name is `"kfs_msg_" + name.Substring(4).ToLowerInvariant() + "_t"` — the row's name
  is assumed to begin with `MSG_`.
- The numeric id is the row's `id` string parsed as hex after its `0x`.
- Every name in a field row's `names` list becomes one member of that row's type.
- Field types are restricted to this list; anything else throws
  `InvalidDataException("messages.json: unknown field type '…'")`:[^msglayout-typeshape]

  | table type | C type | size / align |
  | --- | --- | --- |
  | `bool` | `kfs_bool32_t` | 4 / 4 |
  | `uint32` | `uint32_t` | 4 / 4 |
  | `int64` | `int64_t` | 8 / 8 |
  | `float` | `float` | 4 / 4 |
  | `vec3` | `vec3` | 12 / 4 |
  | `quat` | `quat` | 16 / 4 |
  | `mat3` | `mat3` | 36 / 4 |
  | `dvec3` | `dvec3` | 24 / 8 |

- A name ending in `[i]` is a **per-vehicle array**. Its row type is a brace spec
  `{member: type, member: type[n], …}` describing one element; the element type is named
  `kfs_vehicle_props_<group>_t` (the `kfs_vehicle_props_` prefix is a literal, whatever message the
  field is in) and cached in a static dictionary by `(group, spec)`. The array length is the
  group's count among the layout's **command** fields; a group with count 0 is left out of the struct
  entirely. In the header the length is written as the macro `<GROUP>_COUNT`.
- Members are laid out with natural alignment after the 16-byte header; the struct size is rounded up
  to its alignment (at least 8).

## Representative output

The text below is **derived by hand** from the generator and the embedded `bench_layout.json` /
`messages.json`; it was not captured from a running game.[^bench-json-fields][^messages-json-rows] Long message
lines are shown wrapped.

```c
/* Generated by kFS from the vehicle layout (kC §10.2; FI §5.1 rules, P-082, P-083, P-132, P-133). One text for kC and
 * for host builds, which define __KFS_HOST__ (kC §14, P-160). */
#pragma once
#include <kfs.h>
#define KFS_LAYOUT_FINGERPRINT 0x070A6447u
#define THRUSTER_COUNT 12
#define ENGINE_COUNT 1
#define GIMBAL_COUNT 1
typedef struct {
    struct {
        float x;
        float y;
        float z;
    } accel_body_mps2;
    struct {
        float x;
        float y;
        float z;
    } rates_body_rps;
    kfs_bool32_t valid;
} kfs_imu_t;
typedef struct {
    kfs_imu_t imu;
    uint8_t kfs_pad0[4];
} kfs_sensors_t;
typedef struct {
    float pulse_s;
} kfs_thruster_cmd_t;
typedef struct {
    float throttle;
    float lease_s;
} kfs_engine_cmd_t;
typedef struct {
    float y;
    float z;
} kfs_gimbal_cmd_t;
typedef struct {
    kfs_thruster_cmd_t thruster[12];
    kfs_engine_cmd_t engine[1];
    kfs_gimbal_cmd_t gimbal[1];
} kfs_commands_t;
typedef struct kfs_msg_header { uint32_t seq; uint16_t source; uint16_t flags; int64_t t_us; } kfs_msg_header_t;
#define MSG_NAV_STATE 0x0101
#define MSG_FLIGHT_DATA 0x0102
#define MSG_ATT_TARGET 0x0201
#define MSG_WRENCH_CMD 0x0301
#define MSG_VEHICLE_PROPS 0x0401
#define MSG_BURN_STATUS 0x0402
#define MSG_PILOT_INPUT 0x0403
#define MSG_GUIDANCE_STATUS 0x0202
#define MSG_FC_SETTINGS 0x0404
#define MSG_BURN_PLAN 0x0405
typedef struct kfs_msg_nav_state { kfs_msg_header_t hdr; kfs_bool32_t valid; uint32_t parent_id;
    uint32_t soi_changes; dvec3 r_cci_m; dvec3 v_cci_mps; quat q_body_to_cci; vec3 rates_body_rps;
    vec3 accel_body_mps2; } kfs_msg_nav_state_t;
/* … kfs_msg_flight_data_t, kfs_msg_att_target_t … */
typedef struct kfs_msg_wrench_cmd { kfs_msg_header_t hdr; vec3 torque_body_Nm; vec3 force_body_N;
    float throttle; kfs_bool32_t engines_on; kfs_bool32_t detail_valid; quat q_ctrl_to_body;
    uint32_t axis_system; vec3 rcs_impulse_ctrl_Nms; vec3 tvc_cmd_ctrl; vec3 linear_impulse_ctrl_Ns;
    float engine_burn_s; } kfs_msg_wrench_cmd_t;
typedef struct kfs_vehicle_props_thruster { vec3 force_body_N; vec3 torque_body_Nm; float min_pulse_s;
    kfs_bool32_t propellant_ok; uint32_t control_map; vec3 torque_ctrl_Nm; vec3 force_ctrl_N;
    vec3 torque_fraction; vec3 force_fraction; } kfs_vehicle_props_thruster_t;
/* … kfs_vehicle_props_engine_t (29 members, arrays such as float lut_ln_p[32]; vec3 nozzle_dir_body[8];) … */
typedef struct kfs_vehicle_props_gimbal { float range_y_rad; float range_z_rad; kfs_bool32_t active;
    float thrust_N; float max_thrust_N; vec3 pivot_body_m; vec3 dir0_gimbal; vec3 offset_gimbal_m;
    quat q_gimbal_to_body; kfs_bool32_t circular; } kfs_vehicle_props_gimbal_t;
typedef struct kfs_msg_vehicle_props { kfs_msg_header_t hdr; float mass_kg; mat3 I_body_kgm2;
    vec3 com_body_m; float max_g_load; vec3 ang_accel_body_rps2; vec3 rcs_torque_ctrl_Nm;
    vec3 rcs_min_impulse_ctrl_Nms; vec3 rcs_thrust_from_rotation_N;
    kfs_vehicle_props_thruster_t thruster[THRUSTER_COUNT]; kfs_vehicle_props_engine_t engine[ENGINE_COUNT];
    kfs_vehicle_props_gimbal_t gimbal[GIMBAL_COUNT]; } kfs_msg_vehicle_props_t;
/* … kfs_msg_burn_status_t, kfs_msg_pilot_input_t, kfs_msg_guidance_status_t, kfs_msg_fc_settings_t,
   kfs_msg_burn_plan_t … */
#ifdef __KFS_HOST__
extern kfs_sensors_t sensors;
extern kfs_commands_t commands;
#else
extern const kfs_sensors_t sensors __at(0x80000000);
extern kfs_commands_t commands __at(0x90000000);
#endif
```

Worked layout example (derived): `kfs_msg_nav_state_t` puts `hdr` at 0, `valid` 16, `parent_id` 20,
`soi_changes` 24, `r_cci_m` 32 (8-aligned), `v_cci_mps` 56, `q_body_to_cci` 80, `rates_body_rps` 96,
`accel_body_mps2` 108, and has size 120.

Note that vector-valued *sensor* fields come out as anonymous `struct { float x; float y; float z; }`
(the layout flattens them to scalars), whereas *message* fields use the `vec3` typedef. Only the bench
layout has sensor fields: for a vehicle (`LayoutGenerator.Vehicle` produces command fields only) the
`kfs_imu_t` and `kfs_sensors_t` typedefs and both `sensors` declarations are absent — see
[I/O layout generation](/host/io-layout-generation.md).

# `"X.kasm.h"`

When a kC project contains `X.kasm`, the kASM sources are assembled first and, for each one, a header
is synthesised from the functions that carry a `.proto` line:[^frontend-kasmh]

```c
/* Generated from X.kasm's .proto lines (kASM §8.1, P-104). */
#pragma once
#include <stdint.h>
#include <kfs.h>
<prototype text of each .proto, with a single trailing ';'>
```

It is stored under the name `X.kasm.h` in a per-compile dictionary that the *quoted* include resolver
consults after the project files, so kC writes `#include "X.kasm.h"`. It is not a system file: its
functions are ordinary externals that the linker step matches to the assembled kASM functions. No
such header is produced for the kASM library (`string.kasm`). Example in the shipped
`kAsm_dot3` project: `.proto "float dot3_fast(const float *a, const float *b)"`.

# The on-disk copies

`GeneratedHeaders.Write(folder, tables, layout, target, part)` writes `vehicle.h` and `vehicle.kinc`
(UTF-8 without BOM) into a folder, skipping a file whose bytes are already identical, and returns the
paths it wrote.[^genheaders-write] The workbench calls it for the project folder on build, on load and
for the console command `kfs headers [file]`.[^workbench-headers]

The file content is a three-line banner followed by the generated text:[^genheaders-banner]

```text
/* vehicle.h: generated by kFS for <target> (<part>), layout 0x<FINGERPRINT>, messages.json version <n>.
 * Generated and read-only: the in-game workbench rewrites it at kfs build and kfs headers, and edits are lost.
 * It is the compiler's built-in <vehicle.h> for this layout (tooling §7.3, D-080, P-160). Host builds define __KFS_HOST__ and use the host headers of kC §14. */
```

The compiler never reads these files in a workbench build: the workbench omits `vehicle.h` and
`vehicle.kinc` from the project's file set, and `<vehicle.h>` always resolves to the in-memory
text.[^workspace-skip] Editing the file on disk changes nothing. `GeneratedHeaders.HeaderBody(text)`
strips the banner again; `GeneratedHeaders.KincTypes(text)` parses the `; .type` / `; .field` comment
lines of a written `vehicle.kinc` back into type descriptions.[^genheaders-helpers]

`vehicle.kinc` is the kASM counterpart: `.equ` lines for every constant of the built-in
`vehicle.kinc` include (message ids as `0x%04X`, `sensors`/`commands` addresses as `0x%08X`, the rest
decimal) and comment lines describing each type.[^genheaders-kinc]

`GeneratedHeaders` keeps its own cache of the bannered texts, again per layout object, keyed by
`(tables, target, part, kinc)`.[^genheaders-cache]

`Unverified:` the "host builds" the comments mention (compiling the same kC source natively with
`__KFS_HOST__` defined) have no implementation in the decompiled game assemblies; `Library.HostSources()`
exists but nothing calls it.

# Direct answers

**If a mod adds a row to the services table, does a kC prototype or wrapper appear automatically?**
No. The name → id binding and the cell counts are taken from the table, and kASM gets its `SVC_*`
constant automatically, but the kC prototype must be added to a system header by hand **and** the
name must be added to `SystemHeaders.Builtins`. The complete list of conditions is in
[kC builtins and intrinsics](/language/kc-builtins-and-intrinsics.md) (Modding notes).

**If a mod adds a message?** Yes: `MSG_NAME` and `kfs_msg_name_t` appear in `<vehicle.h>` and
`#pragma kfs publish/subscribe` accept the name, with these conditions:

- the row is present in `tables.MessagesData.Messages` *before* the header for the layout in use is
  first generated (or the cache is defeated, see below);
- its name starts with `MSG_`, its id is a unique `0x` + 4 hex digit string (checked only by
  `GameTables.CheckMessages` at load time, which a late insertion bypasses);[^gametables-checkmessages]
- its field types are among the eight in the table above;
- a per-vehicle array (`name[i]`) can only be sized by an existing command group, its element type is
  named `kfs_vehicle_props_<group>_t`, and the header references `<GROUP>_COUNT`, which the generator
  defines only for `thruster`, `engine` and `gimbal`.

Whether the bus actually carries the new message is a host matter: [message bus](/host/message-bus.md).

**If a mod adds an enum?** Nothing appears. The kC front end never reads `enums.json`; the `KFS_*`
names are literal `#define`s. Define the constants in the mod's own header.

**If a mod adds sensor or command fields to a vehicle's layout?** They appear in `kfs_sensors_t` /
`kfs_commands_t` automatically, as members and (for a new top-level group) a new
`kfs_<group>_t` / `kfs_<group>_cmd_t` typedef — provided the field types are in the layout type table
and the offsets follow natural C layout. No `<GROUP>_COUNT` macro is emitted for a new group. (A stock
vehicle layout has no sensor fields at all, and any added field changes the layout fingerprint that
every image is checked against at load.)

**Are builtin names recognised by hard-coded string compares?** Yes, in three places: the
`SystemHeaders.Builtins` set; `Parser.BuiltinCall`'s `switch` (math and memory intrinsics, the
allocation and `printf` rejections); and `Parser.ServiceCall`'s `switch` for `kfs_logf`, `trace` and
`kfs_sleep_until_event`. All other services go through the table-built dictionary.[^parser-switches]

# Modding notes

Ways to put text in front of the kC compiler, from least to most invasive:

| approach | how | caveats |
| --- | --- | --- |
| add a new system header | `SystemHeaders.Files["purrtty.h"] = text;` | public static dictionary, read live; players must write `#include <purrtty.h>`; text may use `__at`, and its prototypes can be builtins |
| extend `<kfs.h>` | replace `SystemHeaders.Files["kfs.h"]` with the original plus additions | every existing program picks it up; a game update that changes the string must be re-merged |
| per-compile system files | make `CompileInput.SystemFiles` non-empty (checked before everything else for `<name>`) | no game code sets it, so `Compiler.Compile` or its callers must be patched[^input-systemfiles] |
| extend `<vehicle.h>` | Harmony postfix on `VehicleHeader.Generate` appending text | runs for every kC compile and for the on-disk copy; append after the generated text so `kfs_bool32_t`, `vec3` etc. are in scope |
| make table edits visible | edit the `GameTables` instance before any compile, or pass a fresh instance | caches: `VehicleHeader._generated` and `GeneratedHeaders._texts` (per `IoLayout` object, compared by `tables` reference), `MessageLayout._elements` (static, by spec text), `Parser._bindings` (static, by `tables` reference) |

`Verified:` the first row together with `SystemHeaders.Builtins` entries, and a fresh augmented
`GameTables` supplied through a postfix on `GameTables.LoadEmbedded` (rather than editing the loaded
instance in place), is the combination that was run against the real `KSA.dll` outside the
game[^offline-harness] — the tested recipe is [adding a service](/modding/adding-a-service.md).

Other facts:

- All three classes (`SystemHeaders`, `VehicleHeader`, `GeneratedHeaders`) are `public static`. Their
  generators' helpers (`Build`, `Messages`, `Block`, `Tree`, `Emit`, `MakeVehicleH`, …) are private
  static, patchable only by name through Harmony.
- Header generation runs on the "kFS compile" thread (via `KcFrontEnd.Lower`) and on whichever thread
  calls the workbench's `WriteHeaders`. `VehicleHeader.Generate` locks its cache entry; the
  dictionaries in `SystemHeaders` are plain collections with no locking, so mutate them once at mod
  start-up, before any build.
- `SystemHeaders.Deferred` only produces errors; removing `matn.h` from it would merely turn the
  message into "can't find 'matn.h'" unless a header of that name is also supplied.

[^sysheaders-files]: SystemHeaders.cs:6-32
[^sysheaders-kfsh]: SystemHeaders.cs:20
[^sysheaders-std]: SystemHeaders.cs:10-19
[^sysheaders-builtins]: SystemHeaders.cs:24-30
[^sysheaders-get]: SystemHeaders.cs:34-41
[^frontend-vehicle]: KcFrontEnd.cs:67, 75-79
[^frontend-kasmh]: KcFrontEnd.cs:61-66, 376-388
[^vehicleheader-generate]: VehicleHeader.cs:32-58
[^vehicleheader-build]: VehicleHeader.cs:60-140
[^vehicleheader-counts]: VehicleHeader.cs:74-88
[^vehicleheader-messages]: VehicleHeader.cs:142-192
[^vehicleheader-block]: VehicleHeader.cs:194-265, 433-467
[^vehicleheader-ctype]: VehicleHeader.cs:267-305
[^vehicleheader-tree]: VehicleHeader.cs:307-378
[^vehicleheader-layout]: VehicleHeader.cs:216-235, 380-431
[^msglayout-groupcount]: MessageLayout.cs:79-112
[^msglayout-build]: MessageLayout.cs:74-77, 158-314
[^msglayout-typeshape]: MessageLayout.cs:137-151
[^parser-bindings]: Parser.cs:3085-3103
[^parser-switches]: Parser.cs:2401-2411, 3109-3205, 3353-3424
[^parser-events]: Parser.cs:3148-3153
[^builtinincludes-kfs]: BuiltinIncludes.cs:35-63
[^catenums-groups]: CatalogueEnums.cs:12-64
[^genheaders-write]: GeneratedHeaders.cs:228-251
[^genheaders-banner]: GeneratedHeaders.cs:25-44, 63-70
[^genheaders-helpers]: GeneratedHeaders.cs:72-80, 189-226
[^genheaders-kinc]: GeneratedHeaders.cs:82-173
[^genheaders-cache]: GeneratedHeaders.cs:23, 51-61
[^workbench-headers]: Workbench.cs:207-268, 673, 926
[^workspace-skip]: Workspace.cs:187-190
[^gametables-checkmessages]: GameTables.cs:864-874
[^bench-json-fields]: KSA.Kfs.Vm.Tables.Data.bench_layout.json:4-32
[^messages-json-rows]: KSA.Kfs.Vm.Tables.Data.messages.json:5-127
[^input-systemfiles]: CompileInput.cs:10
[^offline-harness]: Experiments E3, E10 and E11 in [the offline harness](/modding/offline-harness.md).
