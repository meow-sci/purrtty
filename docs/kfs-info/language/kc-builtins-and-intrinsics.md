---
type: API Reference
title: kC builtins and intrinsics
description: Every compiler-recognised function in kC — intrinsics, the kfs_* service bindings and how a call lowers to SYSCALL, kfs_logf and trace, and how kC code reads sensors, writes commands and uses bus messages.
resource: ksa-decomp:KSA.Kfs.KC/Parser.cs
tags: [kfs, language, kc, builtins, services, syscall, sensors, commands, bus, logging]
sources:
  - id: parser
    resource: ksa-decomp:KSA.Kfs.KC/Parser.cs
    title: Parser (decompiled)
  - id: sema
    resource: ksa-decomp:KSA.Kfs.KC/Sema.cs
    title: Sema (decompiled)
  - id: lowering
    resource: ksa-decomp:KSA.Kfs.KC/Lowering.cs
    title: Lowering (decompiled)
  - id: frontend
    resource: ksa-decomp:KSA.Kfs.KC/KcFrontEnd.cs
    title: KcFrontEnd (decompiled)
  - id: sysheaders
    resource: ksa-decomp:KSA.Kfs.KC/SystemHeaders.cs
    title: SystemHeaders (decompiled)
  - id: vehicleheader
    resource: ksa-decomp:KSA.Kfs.KC/VehicleHeader.cs
    title: VehicleHeader (decompiled)
  - id: services-json
    resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.services.json
    title: services.json (embedded table)
  - id: events-json
    resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.events.json
    title: events.json (embedded table)
  - id: sysblock-json
    resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.sysblock.json
    title: sysblock.json (embedded table)
  - id: traps-json
    resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.traps.json
    title: traps.json (embedded table)
  - id: gametables
    resource: ksa-decomp:KSA.Kfs.Vm.Tables/GameTables.cs
    title: GameTables (decompiled)
  - id: logformat
    resource: ksa-decomp:KSA.Kfs.Vm.Image/LogFormat.cs
    title: LogFormat (decompiled)
  - id: irbuilder
    resource: ksa-decomp:KSA.Kfs.Compiler.Builder/IrBuilder.cs
    title: IrBuilder (decompiled)
  - id: busanalysis
    resource: ksa-decomp:KSA.Kfs.Compiler/BusAnalysis.cs
    title: BusAnalysis (decompiled)
  - id: examples
    resource: ksa-decomp:KSA.Kfs.Host.Workbench/Examples.cs
    title: Examples (decompiled)
  - id: offline-harness
    resource: /modding/offline-harness.md
    title: Offline harness experiments (E-numbered results)
generated: { by: claude-code/claude-fable-5-2, at: 2026-11-25T21:30:48Z }
status: stable
ksa_build: "2026.11.25.5949"
---

# What "builtin" means

A function is a builtin when **both** hold:[^parser-builtinflag][^frontend-builtinflag]

1. its declaration was read from a system file (a header included with `<…>`), and
2. its name is in the hard-coded set `SystemHeaders.Builtins`.

The 37 names in that set:[^sysheaders-builtins]

```text
sqrtf fabsf sqrt fabs isnan isinf __kfs_assert memcpy memmove memset memcmp
malloc calloc realloc free printf puts
kfs_halt kfs_sleep_until kfs_sleep_until_event kfs_stage kfs_hold_direction kfs_hold_rates
kfs_release_attitude kfs_set_attitude_profile kfs_set_throttle kfs_engines_on kfs_translate
kfs_hold_heading kfs_hold_quat kfs_engines_off kfs_plan_circularize kfs_plan_burn kfs_burn_start
kfs_burn_abort trace kfs_logf
```

A call to a builtin is routed to `Parser.BuiltinCall`, a `switch` on the function name.[^parser-builtincall]
A function with one of these names that is declared only in player files (the system header that
declares the builtin is not included) is an ordinary function. Once the system header's declaration
is in scope, later declarations of the same name merge into it and the name stays a builtin.
Builtins are never linked or lowered as functions.[^frontend-link]

# Compiler intrinsics

| name(s) | header | what the compiler does |
| --- | --- | --- |
| `sqrtf`, `sqrt` | `<math.h>` | a single instruction, `FSQRT` / `DSQRT`; constant arguments fold[^parser-builtincall][^lowering-builtins] |
| `fabsf`, `fabs` | `<math.h>` | `FABS` / `DABS`; constant arguments fold |
| `isnan(x)` | `<math.h>` | inline `x != x` (`DUP`, `FNE`, or `DUP2`, `DNE`); an integer argument is converted to `float` first[^sema-classify] |
| `isinf(x)` | `<math.h>` | inline `fabs(x) == +inf` |
| `assert(e)` | `<assert.h>` | a macro for `__kfs_assert(e)` (or `((void)0)` under `NDEBUG`); lowers to "if the condition is false, execute `TRAP 1`"[^sysheaders-assert][^lowering-assert] |
| `memcpy`, `memmove` | `<string.h>` | when the length is an integer constant 0…65,535: one inline `COPY n`. Otherwise an ordinary call to the kASM library routine[^sema-membuiltin][^lowering-mem] |
| `memset` | `<string.h>` | when the length is such a constant **and** the fill value is the constant 0: one inline `ZERO n`. Otherwise the library routine |
| `memcmp` | `<string.h>` | always the kASM library routine |
| `malloc`, `calloc`, `realloc`, `free` | `<stdlib.h>` | `KC-E302` "flight code can't allocate memory at run time" |
| `printf`, `puts` | `<stdio.h>` | `KC-E645` "kC has no standard output" |

`traps.json` describes trap 13 `USER` as "`TRAP` instruction (`assert()` failure)".[^traps-json-user] What
a trap does to the computer is in [traps and faults](/vm/traps-and-faults.md).

Every other `<math.h>` function (`sinf`, `atan2`, `pow`, …) is **not** an intrinsic: it is kC source
compiled into the program ([kC standard library](/language/kc-standard-library.md)).

# Service bindings

The remaining 20 builtins are *services*: requests the program makes of the host through the VM's
`SYSCALL` instruction. The service ABI is in [services syscall ABI](/vm/services-syscall-abi.md), the
full table in [services table](/tables/services-table.md), and what the game does with each request
in [service request handling](/host/service-request-handling.md) and
[kFS services game side](/integration/kfs-services-game-side.md).

## The bindings

Prototypes are from the built-in `<kfs.h>`; ids, cell types, `requires_stock` and cost class from
`services.json`.[^sysheaders-kfsh][^services-json-rows]

| id | symbol | kC prototype | argument cells | result | requires stock | cost class |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | `SVC_LOG` | `void kfs_logf(const char *fmt, ...);` | `u32` format index, `ptr` buffer | — | — | Medium (per value) |
| 2 | `SVC_TRACE` | `void trace(const char *channel, float v);` | `u32` channel index, `f32` | — | — | Light |
| 3 | `SVC_HALT` | `void kfs_halt(void);` | — | — | — | Light |
| 16 | `SVC_SLEEP_UNTIL` | `void kfs_sleep_until(int64_t t_us);` | `i64` (2 cells) | — | — | Medium |
| 17 | `SVC_SLEEP_UNTIL_EVENT` | `void kfs_sleep_until_event(uint32_t event, uint32_t param);` | `u32`, `cell` | — | — | Medium |
| 32 | `SVC_STAGE` | `void kfs_stage(void);` | — | — | — | Light |
| 48 | `SVC_HOLD_DIRECTION` | `void kfs_hold_direction(uint32_t dir, uint32_t roll);` | `u32`, `u32` | — | `GUIDANCE` | Light |
| 49 | `SVC_HOLD_RATES` | `void kfs_hold_rates(void);` | — | — | `GUIDANCE` | Light |
| 50 | `SVC_RELEASE_ATTITUDE` | `void kfs_release_attitude(void);` | — | — | `GUIDANCE` | Light |
| 51 | `SVC_SET_ATTITUDE_PROFILE` | `void kfs_set_attitude_profile(uint32_t profile);` | `u32` | — | `ATTITUDE_CONTROL` | Light |
| 52 | `SVC_SET_THROTTLE` | `void kfs_set_throttle(float x);` | `f32` | — | `GUIDANCE` | Light |
| 53 | `SVC_ENGINES_ON` | `void kfs_engines_on(void);` | — | — | `GUIDANCE` | Light |
| 54 | `SVC_TRANSLATE` | `void kfs_translate(vec3 dv);` | `f32` ×3 | — | `GUIDANCE` | Light |
| 55 | `SVC_HOLD_HEADING` | `void kfs_hold_heading(float heading_rad, float pitch_rad, uint32_t roll);` | `f32`, `f32`, `u32` | — | `GUIDANCE` | Light |
| 56 | `SVC_HOLD_QUAT` | `void kfs_hold_quat(quat q, uint32_t roll);` | `f32` ×4, `u32` | — | `GUIDANCE` | Light |
| 57 | `SVC_ENGINES_OFF` | `void kfs_engines_off(void);` | — | — | `GUIDANCE` | Light |
| 64 | `SVC_PLAN_CIRCULARIZE` | `kfs_burn_t kfs_plan_circularize(uint32_t at);` | `u32` | `u32` | — | Heavy |
| 65 | `SVC_PLAN_BURN` | `kfs_burn_t kfs_plan_burn(vec3 dv, int64_t t_us);` | `f32` ×3, `i64` | `u32` | — | Heavy |
| 66 | `SVC_BURN_START` | `void kfs_burn_start(kfs_burn_t b);` | `u32` | — | `GUIDANCE` | Light |
| 67 | `SVC_BURN_ABORT` | `void kfs_burn_abort(void);` | — | — | `GUIDANCE` | Light |

Id 4 (`event`) is a reserved row with no binding.[^services-json-rows]

Statements made by the table's own `summary` text about run-time behaviour, repeated here because
they affect how a program should be written: `sleep_until` and `sleep_until_event` are "Not active
yet: the request is logged and the computer keeps running"; `translate` is "Always rejected for now:
the game has no automatic translation yet".[^services-json-rows]

## How a call becomes a `SYSCALL`

1. **Name → service.** `Parser.Bindings(tables)` builds a dictionary from the services table: for
   every row that is not reserved and has a `kc` column, the text of that column up to the first `(`
   (backticks trimmed) is the key and `tables.Services[id]` the value. The dictionary is cached in a
   private static field, valid while the same `GameTables` object is in use.[^parser-bindings]
2. **Dispatch.** `BuiltinCall`'s `default` branch looks the called name up in that dictionary. A hit
   goes to `ServiceCall`; a miss is `KC-E000` "'name' (the service bindings of kC §10.2, §9.5,
   §10.3) is not implemented until MS6".[^parser-builtincall]
3. **Type check.** For every service except `kfs_logf`, `trace` and `kfs_sleep_until_event`, the call
   is checked like any function call against the prototype from the header — argument count
   (`KC-E305`) and assignment conversion of each argument — and the node's kind is then switched to
   `Syscall` carrying the service id.[^parser-servicecall] The call site is recorded in
   `TranslationUnit.ServiceCalls` for the ownership check (`KC-E501`,
   [kC program structure](/language/kc-program-structure.md)).
4. **Lowering.** `Lowering.Syscall` evaluates the arguments left to right, so the first argument is
   deepest on the operand stack:[^lowering-syscall]
   - a scalar pushes one cell, or two for a 64-bit type (**no** even-cell alignment padding, unlike
     an ordinary call);
   - a struct argument is copied to a temporary and then **each field is loaded in declaration order
     and pushed** — this is how `vec3` becomes three `f32` cells and `quat` four. The flattening is
     one level deep and loads each field as a scalar.
   - The total number of cells must equal the service's `ArgCells` (the sum over the table's
     `arg_types`, with `i64`/`f64` counting two). A mismatch throws `InvalidOperationException`
     ("name: N cells for M (kC §10.2 binding)") — an internal error, not a diagnostic.
   - Finally `IrBuilder.Syscall(id, argCells, resultCells)` emits `SYSCALL id`; the result cells (0
     or, for the two planning services, 1) are left on the stack as the value of the call.[^irbuilder-syscall]

The shipped kASM example `hold_att.kasm` shows the same stack shape written by hand — two `PUSH32`
floats, `PUSH8 KFS_ROLL_UP`, `SYSCALL SVC_HOLD_HEADING` — and its neighbour `engines_on.kasm` carries
the comment "A service's arguments go on the stack first, the first one deepest, and SYSCALL pops
them".[^examples-kasm]

Argument **values** are not checked by the compiler (except for the cases below). The table's
`arg_domains` are enforced by the VM when the request is issued and `host_checks` by the host; a
rejected request increments `sys.rejected_requests`.[^sysblock-json-fields]

## `kfs_logf`

`kfs_logf(fmt, …)` is checked and lowered specially.[^parser-logf]

- `fmt` must be a string literal (`KC-E553` "the kfs_logf format isn't a string literal").
- Conversions: `%` flags width `.`precision length conversion, where flags are any of `- + space 0
  #`, width and precision are decimal digits, length is empty or `ll`, and conversion is one of
  `d u x f e g`. `%%` is a literal percent. `ll` is only valid with `d`, `u`, `x`. Anything else —
  including `%s`, `%c`, `%p`, `%i`, `%X`, `*` widths — is `KC-E550` "invalid conversion in the
  kfs_logf format" (hint: "Use %d, %u, %x, %lld, %llu, %llx, %f, %e or %g.").
- Arguments, one per conversion (`KC-E552` for a mismatch or a wrong count):

  | conversion | argument must be | passed as |
  | --- | --- | --- |
  | `%d` | integer of at most 32 bits | `int` |
  | `%u`, `%x` | integer of at most 32 bits | `unsigned int` |
  | `%lld` | 64-bit integer | `long long` |
  | `%llu`, `%llx` | 64-bit integer | `unsigned long long` |
  | `%f`, `%e`, `%g` | `float` or `double` | unchanged — a `float` is **not** promoted |

- The format stored in the image is rewritten: a conversion fed by a 4-byte `float` gets an `h`
  length modifier (`%.1f` with a `float` argument is stored as `%.1hf`).
- The rewritten format is validated by `LogFormat.Parse`: at most **8 values**, an argument buffer of
  at most **64 bytes**, width and precision at most 99, and no `#` flag on `%d`/`%u` (`KC-E550`
  "kfs_logf format rejected: …").[^logformat-parse]

Lowering:[^lowering-syscall][^logformat-layout]

1. A temporary buffer is reserved in the frame (size rounded up to 8, at least 8, 8-aligned).
2. Each argument is stored at its naturally aligned offset in the buffer (4- or 8-byte slots in
   order).
3. The format text is added to the module's string table as a *format* string (kind 1, named
   `.fmtN`, shared when identical) and its index is pushed, then the buffer's address.
4. `SYSCALL 1` with two argument cells.

So formatting happens on the host, not in the VM; see [service request handling](/host/service-request-handling.md).
Example from the shipped ascent program:[^examples-ascent]

```c
kfs_logf("MECO: altitude %.1f km, apoapsis %.1f km",
         flight.altitude_m / KM, flight.apoapsis_m / KM);
```

## `trace`

`trace("channel", value)`: the channel must be a string literal (`KC-E553`), the value any arithmetic
expression, converted to `float`. The channel name becomes a *channel* string (kind 2, `.chanN`) in
the module's string table; its index and the value are pushed and `SYSCALL 2` issued.[^parser-trace][^lowering-syscall]
Traces feed the oscilloscope and `kfs trace`; see [profiler and exec trace](/vm/profiler-and-exec-trace.md)
and [debug window](/tooling/debug-window.md).

## `kfs_sleep_until_event`

The first argument must be an integer **constant** equal to an id in `events.json`, and not `TIME`;
the second is checked against that event's parameter type (`KC-E551`).[^parser-sleepevent][^events-json-rows]

| id | `<kfs.h>` constant | parameter | second argument |
| --- | --- | --- | --- |
| 1 | `KFS_EVENT_APOAPSIS` | lead time, s (`f32`) | any arithmetic value, converted to `float` |
| 2 | `KFS_EVENT_PERIAPSIS` | lead time, s (`f32`) | as above |
| 3 | `KFS_EVENT_SOI_CHANGE` | none | must be the constant `0` |
| 4 | `KFS_EVENT_ATMOSPHERE_ENTRY` | none | must be the constant `0` |
| 5 | `KFS_EVENT_BURN_START` | burn handle (`u32`) | an integer |
| 6 | `KFS_EVENT_ALTITUDE_BELOW` | altitude, m (`f32`) | any arithmetic value, converted to `float` |
| 7 | `KFS_EVENT_TIME` | — | rejected: "KFS_EVENT_TIME is not a sleep event" |
| 8 | `KFS_EVENT_MESSAGE` | message id (`u32`) | an integer |

The header prototype declares the parameter as `uint32_t`, but this special case bypasses it, so a
float lead time is passed as a float bit pattern in the one `cell`.

## Named argument values

`<kfs.h>` defines these constants for service arguments and message fields:[^sysheaders-kfsh]

| group | constants (value) |
| --- | --- |
| direction for `kfs_hold_direction` | `KFS_ENU_FORWARD` 2, `KFS_ENU_BACKWARD` 3, `KFS_ENU_UP` 4, `KFS_ENU_DOWN` 5, `KFS_LVLH_AHEAD` 6, `KFS_LVLH_BEHIND` 7, `KFS_LVLH_RADIAL_OUT` 8, `KFS_LVLH_RADIAL_IN` 9, `KFS_VLF_PROGRADE` 10, `KFS_VLF_RETROGRADE` 11, `KFS_VLF_NORMAL` 12, `KFS_VLF_ANTINORMAL` 13, `KFS_VLF_OUTWARD` 14, `KFS_VLF_INWARD` 15, `KFS_BURN_PLUS_DV` 16, `KFS_BURN_MINUS_DV` 17, `KFS_TARGET_TOWARD` 18, `KFS_TARGET_AWAY` 19, `KFS_TARGET_ANTIVELOCITY` 20, `KFS_TARGET_ALIGN` 21 |
| roll mode | `KFS_ROLL_FREE` 0, `KFS_ROLL_UP` 1, `KFS_ROLL_DOWN` 2 |
| attitude profile | `KFS_PROFILE_STRICT` 0, `KFS_PROFILE_BALANCED` 1, `KFS_PROFILE_RELAXED` 2 |
| apsis for `kfs_plan_circularize` | `KFS_AT_APOAPSIS` 0, `KFS_AT_PERIAPSIS` 1 |
| `MSG_ATT_TARGET.mode` | `KFS_ATT_MODE_NONE` 0, `KFS_ATT_MODE_RATE` 1, `KFS_ATT_MODE_ATTITUDE` 2 |
| `MSG_BURN_STATUS.state` | `KFS_BURN_NONE` 0, `KFS_BURN_PLANNED` 1, `KFS_BURN_ALIGNING` 2, `KFS_BURN_BURNING` 3, `KFS_BURN_DONE` 4, `KFS_BURN_ABORTED` 5, `KFS_BURN_PENDING` 6 |
| `MSG_BURN_STATUS.end_cause` | `KFS_BURN_END_NONE` 0, `…_COMPLETED` 1, `…_PROPELLANT_OUT` 2, `…_STAGE_RAN_DRY` 3, `…_IGNITION_DENIED` 4, `…_PLANNING_FAILED` 5, `…_SUPERSEDED` 6, `…_CANCELLED` 7 |
| core class | `KFS_CORE_K8` 0, `KFS_CORE_K32` 1, `KFS_CORE_K32F` 2, `KFS_CORE_K32D` 3 |
| units | `DEG2RAD` 0.017453292519943295f, `RAD2DEG` 57.29577951308232f, `KM` 1000.0f, `G0` 9.80665f |

`kfs_burn_t` is `uint32_t` (a burn handle) and `kfs_bool32_t` is `uint32_t` (a 4-byte boolean used in
all I/O and message structs; compare it with `!= 0u`).

# Memory-mapped objects

Three objects are declared with `__at`, i.e. they have no storage in the program and every access is
a load or store at a fixed address.[^sysheaders-kfsh][^vehicleheader-objects]

| object | declared in | type | address | access |
| --- | --- | --- | --- | --- |
| `sys` | `<kfs.h>` | `const kfs_sys_t` | `0xA0000000` | read-only |
| `sensors` | `<vehicle.h>`, only for a layout with sensor fields (the bench layout; no vehicle layout has any) | `const kfs_sensors_t` | `0x80000000` | read-only |
| `commands` | `<vehicle.h>` | `kfs_commands_t` | `0x90000000` | read/write |

Lowering: the object's address is pushed as a constant, each `.member` adds its offset, each `[i]`
applies a bounds check and scaled index, and the leaf is loaded or stored with the instruction for
its type.[^lowering-address] Writing through a `const` object is rejected at compile time
(`KC-E209`); the VM additionally traps `READ_ONLY` on a write to the sensor or system block.[^traps-json-user]
The address-space layout is in [memory map and sysblock](/vm/memory-map-and-sysblock.md).

## `sys`

`kfs_sys_t` mirrors `sysblock.json` (256 bytes):[^sysblock-json-fields]

| offset | field | type | meaning (from the table) |
| --- | --- | --- | --- |
| 0 | `cycles` | `uint32_t` | cycles used so far in the current tick |
| 4 | `budget` | `uint32_t` | cycle budget per tick |
| 8 | `tick` | `uint32_t` | low 32 bits of the tick index |
| 12 | `t_s` | `float` | seconds since reset |
| 16 | `dt_s` | `float` | base tick period |
| 20 | `overruns` | `uint32_t` | overrun count since reset |
| 24 | `task_id` | `uint32_t` | index of the running task |
| 28 | `task_dt_s` | `float` | period of the running task |
| 32 | `wake_reason` | `uint32_t` | event that last woke the machine (an `events.json` id) |
| 36 | `tier` | `uint32_t` | the part's index |
| 40 | `core` | `uint32_t` | core class |
| 44 | `soi_changes` | `uint32_t` | parent-body changes since reset |
| 48 | `met_us` | `int64_t` | microseconds since reset |
| 56 | `nv_writes` | `uint32_t` | non-volatile writes since reset, if present |
| 60 | `reset_cause` | `uint32_t` | 0 power-on, 1 player reset, 2 watchdog, 3 load |
| 64 | `rejected_requests` | `uint32_t` | service requests rejected for bad argument values |
| 68 | `reserved[47]` | `uint32_t` | reads return 0 |

## Sensors

`sensors` is a struct generated from the sensor fields of the I/O layout; with the bench layout it is
`sensors.imu.accel_body_mps2.{x,y,z}`, `sensors.imu.rates_body_rps.{x,y,z}` and `sensors.imu.valid`.
**Not active in this build (on vehicles):** `LayoutGenerator.Vehicle` produces a command block only
(`SensorSize` 0, no sensor fields), so `<vehicle.h>` for a vehicle declares neither `kfs_sensors_t` nor
`sensors`, and a flying program gets all of its inputs from bus messages (below).[^vehicleheader-sensors]
See [kC generated headers](/language/kc-generated-headers.md),
[I/O layout generation](/host/io-layout-generation.md) and [host sampling and leaves](/host/host-sampling-and-leaves.md).

```c
#include <kfs.h>
#include <vehicle.h>
float ax;
void step(void) { if (sensors.imu.valid != 0u) ax = sensors.imu.accel_body_mps2.x; }
```

(Fragment constructed from the generator's output for the bench layout; not compiled. Built for a
vehicle, `sensors` would not be declared.)

## Commands

`commands` holds the actuator command block: `commands.thruster[i].pulse_s`,
`commands.engine[i].throttle`, `commands.engine[i].lease_s`, `commands.gimbal[i].y`, `.z` in the bench
layout, with the counts available as `THRUSTER_COUNT`, `ENGINE_COUNT`, `GIMBAL_COUNT`.

The front end marks every function that writes into the object at `0x90000000`; the back end then
requires the program to claim allocation — `KC-E544` "only the allocation owner may write the command
block" (hint: "Add `#pragma kfs claim ALLOCATION`.").[^frontend-commandwrites][^busanalysis-544] The
shipped allocator example:[^examples-ascent]

```c
#pragma kfs claim ALLOCATION
/* … */
commands.thruster[i].pulse_s = (on_s >= props.thruster[i].min_pulse_s) ? on_s : 0.0f;
```

What happens to the values afterwards is in [write-back](/integration/write-back.md).

# Bus messages

There are no send/receive calls. A message is a global struct bound with a pragma
([kC program structure](/language/kc-program-structure.md)):

```c
#include <kfs.h>
#include <vehicle.h>
#pragma kfs subscribe MSG_NAV_STATE nav
#pragma kfs publish   MSG_WRENCH_CMD wrench
kfs_msg_nav_state_t  nav;      /* filled before the task runs */
kfs_msg_wrench_cmd_t wrench;   /* copied out when the task completes */
```

- **Reading.** Read the subscription buffer's fields. Every message starts with
  `kfs_msg_header_t hdr` (`uint32_t seq; uint16_t source; uint16_t flags; int64_t t_us;`); the
  shipped examples test `nav.hdr.seq == 0u` for "nothing received yet".[^vehicleheader-msghdr][^examples-ascent]
- **Publishing.** Assign the publication buffer's fields in exactly one task. The back end rejects a
  publication buffer written by no task or by more than one (`KC-E542`) and any write to a
  subscription buffer (`KC-E540` "subscription buffers are read-only", hint "Copy into a local.").[^busanalysis-544]
- To support those checks the front end records, per function, every file-scope object it reads and
  writes (`Obj.BusReads`, `BusWrites`, `BusMarks`); taking a non-`const` pointer to such an object
  counts as a write.[^sema-buswrite]

Message contents are in [messages table](/tables/messages-table.md); delivery timing in
[message bus](/host/message-bus.md) and [bus analysis](/compiler/bus-analysis.md).

# Modding notes

To make a new host service callable from kC as `kfs_xyz(…)`, **all** of the following must be true at
compile time; none of them follows automatically from the others:

1. **A service table entry.** `tables.Services[id]` must hold a `ServiceInfo` (its `ArgCells` /
   `ResultCells` drive the lowering) and `tables.ServicesData.Services` must hold a non-reserved
   `ServiceRow` whose `Kc` column starts with `kfs_xyz(`. `GameTables.Services` is a public
   `Dictionary<ushort, ServiceInfo>` with a private setter (the dictionary object itself is mutable);
   `ServicesData.Services` is a `List<ServiceRow>`. `BuildServices`, which normally derives one from
   the other and validates domains and cost classes, is private and runs once at load.[^gametables-services]
2. **A fresh binding cache.** `Parser._bindings` / `_bindingsFor` are private statics filled on the
   first service call compiled against a given `GameTables` instance. Rows added afterwards are
   invisible until the cache is cleared (reflection) or a different `GameTables` instance is used.[^parser-bindings]
3. **A prototype in a system header.** The built-in `<kfs.h>` text is a hard-coded string; add the
   prototype there or in a new system header ([kC generated headers](/language/kc-generated-headers.md)).
   It must be reached through a `<…>` include.
4. **The name in `SystemHeaders.Builtins`** (a public static `HashSet<string>`; contents mutable).
   Without it the declaration is an ordinary external function and the build ends with `KC-E304`
   "declared but never defined".[^sysheaders-builtins]
5. **Matching cells.** The prototype's parameters, flattened as described above, must total
   `ArgCells`, and the return type's cell count (0, 1, or 2 for a 64-bit type) must equal
   `ResultCells`. A mismatch in arguments throws inside `Lowering.Syscall`; a mismatch in results
   breaks the IR builder's stack-height bookkeeping. Neither is reported as a diagnostic.[^lowering-syscall]

`Verified:` these conditions were exercised against the real `KSA.dll` outside the game. A row alone
gives `KC-E003` "'name' is not declared"; a `<…>` prototype without the `Builtins` entry gives
`KC-E304`; with both, the call binds through the row's `kc` column and runs. A `vec3` argument arrives
as three `f32` cells and a one-cell `uint32_t` or `float` result is returned, but a struct
(multi-cell) result makes the lowering throw `InvalidOperationException` ("… builder invariant")
even when the cell counts agree — keep results to one cell. Cell *types* are not compared: a `float`
parameter against a `u32` cell compiles and passes float bits.[^offline-harness] The tested recipe does
not mutate the loaded tables (step 1) or reset the cache (step 2): it builds one augmented
`GameTables` with the public `GameTables.Load(open, origin)` behind a postfix on
`GameTables.LoadEmbedded` — see [adding a service](/modding/adding-a-service.md).

Further constraints:

- Supported parameter shapes are scalars and one-level structs of scalars. A pointer parameter would
  push a VM address in one cell (the `ptr` cell type exists in the table for `log`), but nothing in
  kC marshals strings: only `kfs_logf` and `trace` turn a string literal into a string-table index,
  and they are matched **by name** in `ServiceCall`.[^parser-servicecall]
- `requires_stock` participates in the `KC-E501` ownership check; leave it null for services
  unrelated to the stock flight computer.
- kASM gets `SVC_*` constants for every non-reserved row automatically (from the row's `symbol`), so
  a new service is reachable from kASM with only step 1; see [kASM assembly language](/compiler/kasm-assembly-language.md).
- The id must also be accepted by the VM (`BAD_SYSCALL` trap otherwise) and applied by the host;
  those sides are documented in [services syscall ABI](/vm/services-syscall-abi.md) and
  [kFS services game side](/integration/kfs-services-game-side.md).
- An alternative that needs no service at all: extra *command* fields in the I/O layout appear in
  `commands` automatically and are written by plain assignment — but any new field changes the layout
  fingerprint, which every image is checked against at load
  ([risks and compatibility](/modding/risks-and-compatibility.md)).

[^parser-builtinflag]: Parser.cs:1585-1588
[^parser-builtincall]: Parser.cs:3351-3425
[^parser-bindings]: Parser.cs:141-143, 3085-3103
[^parser-servicecall]: Parser.cs:3105-3206
[^parser-logf]: Parser.cs:3208-3349
[^parser-trace]: Parser.cs:3113-3139
[^parser-sleepevent]: Parser.cs:3140-3193
[^frontend-builtinflag]: KcFrontEnd.cs:143-146
[^frontend-link]: KcFrontEnd.cs:424, 750, 774
[^frontend-commandwrites]: KcFrontEnd.cs:787-789
[^sysheaders-builtins]: SystemHeaders.cs:24-30
[^sysheaders-assert]: SystemHeaders.cs:15
[^sysheaders-kfsh]: SystemHeaders.cs:20
[^sema-classify]: Sema.cs:1492-1511
[^sema-membuiltin]: Sema.cs:1513-1547
[^sema-buswrite]: Sema.cs:81-92, 198-202, 277-284, 1018
[^lowering-builtins]: Lowering.cs:1242-1280
[^lowering-assert]: Lowering.cs:570-577
[^lowering-mem]: Lowering.cs:1411-1435
[^lowering-syscall]: Lowering.cs:1333-1409
[^lowering-address]: Lowering.cs:728-812
[^irbuilder-syscall]: IrBuilder.cs:335-340
[^logformat-parse]: LogFormat.cs:14-18, 258-296
[^logformat-layout]: LogFormat.cs:20-35
[^services-json-rows]: KSA.Kfs.Vm.Tables.Data.services.json:5-25
[^events-json-rows]: KSA.Kfs.Vm.Tables.Data.events.json:5-12
[^sysblock-json-fields]: KSA.Kfs.Vm.Tables.Data.sysblock.json:4-23
[^traps-json-user]: KSA.Kfs.Vm.Tables.Data.traps.json:5-21
[^gametables-services]: GameTables.cs:41, 67, 538-582
[^vehicleheader-objects]: VehicleHeader.cs:101-138
[^vehicleheader-msghdr]: VehicleHeader.cs:144
[^vehicleheader-sensors]: VehicleHeader.cs:89-92, 104-124 (emitted only when `layout.SensorFields.Length != 0`); KSA.Kfs.Host/LayoutGenerator.cs:83-84 (`Vehicle` builds its layout with `Array.Empty<IoField>()` sensor fields and sensor size 0)
[^busanalysis-544]: BusAnalysis.cs:108-114, 174-186
[^examples-kasm]: Examples.cs:51-52
[^examples-ascent]: Examples.cs:53, 58
[^offline-harness]: Experiments E3, E6, E8 and E19 in [the offline harness](/modding/offline-harness.md).
