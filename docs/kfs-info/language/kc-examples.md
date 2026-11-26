---
type: How-To
title: kC worked examples
description: The idioms of real kC programs, drawn from the examples embedded in the game, plus five small complete programs constructed from constructs the front end is known to accept.
resource: ksa-decomp:KSA.Kfs.Host.Workbench/Examples.cs
tags: [kfs, language, kc, examples, idioms, how-to]
sources:
  - id: examples
    resource: ksa-decomp:KSA.Kfs.Host.Workbench/Examples.cs
    title: Examples (decompiled)
  - id: workbench
    resource: ksa-decomp:KSA.Kfs.Host.Workbench/Workbench.cs
    title: Workbench (decompiled)
  - id: parser
    resource: ksa-decomp:KSA.Kfs.KC/Parser.cs
    title: Parser (decompiled)
  - id: frontend
    resource: ksa-decomp:KSA.Kfs.KC/KcFrontEnd.cs
    title: KcFrontEnd (decompiled)
  - id: checks
    resource: ksa-decomp:KSA.Kfs.KC/Checks.cs
    title: Checks (decompiled)
generated: { by: claude-code/claude-fable-5-2, at: 2026-11-25T21:30:48Z }
status: stable
ksa_build: "2026.11.25.5949"
---

# The official examples

The game embeds its example projects as strings in `KSA.Kfs.Host.Workbench.Examples.Files`; they are
reproduced verbatim in [examples](/tooling/examples.md). The kC ones, and what each demonstrates:[^examples-files]

| project file | menu | demonstrates |
| --- | --- | --- |
| `kC_ascent_simple/ascent_simple.c` | kC | pilot mode: a `switch` state machine issuing `kfs_hold_heading`, `kfs_set_throttle`, `kfs_stage`, `kfs_plan_circularize`, `kfs_burn_start`, `kfs_logf`; three subscriptions |
| `kC_attitude_control/prograde_hold.c` | kC | full control: two tasks, publishing `MSG_WRENCH_CMD`, `claim ALLOCATION`, writing `commands.thruster[i].pulse_s`, `<vecmat.h>` |
| `kAsm_dot3/main.c` (+ `dot3_fast.kasm`) | kAsm | calling a kASM function through the generated `"dot3_fast.kasm.h"` |
| `ex_scope/scope.c` | Misc | globals watched on the oscilloscope; float integration |
| `ex_nan/nan.c` | Misc | `sqrtf` of a negative number, for the NaN watch |
| `ex_double/double.c` | (archived) | `double` arithmetic and a `static` helper function |
| `e2e04_kc/main.c`, `e2e04_kasm/main.c` | (archived) | array parameters as `const float *`, counted `for` loops |
| `ex_bus/attitude_hold.c` | (archived) | an `init` task plus two periodic tasks on the bus |
| `ex_stock/all_stock.c` | (archived) | the smallest valid program |

The projects in the "kC", "kAsm" and "Misc" groups are the ones the examples menu offers; the others
are written only when archived examples are requested.[^examples-menu]

The workbench's template for a new `.c` file is also a complete program:[^workbench-template]

```c
/* New kC program. Build with kfs build (or Save in the editor), load with kfs load, run with kfs start.
   kC's log and trace bindings arrive with the services (MS6): watch globals on the oscilloscope instead
   (kfs watch wave). */
#include <kfs.h>
#include <math.h>
#pragma kfs tick_hz 10
#pragma kfs task step rate_hz=10

uint32_t count;
float wave;

void step(void) {
    count++;
    wave = sinf(0.1f * (float)count);
}
```

Its comment is out of date for this build: `kfs_logf` and `trace` are bound (the shipped ascent
example calls `kfs_logf`).

# Idioms the official examples use

1. **Headers first, then pragmas, then globals, then tasks.** `#include <kfs.h>` always; `<math.h>`,
   `<vecmat.h>`, `<vehicle.h>` as needed.
2. **One `tick_hz`, one `task` pragma per task**, each pragma placed just above the globals and
   function it concerns. In a two-task file the second task's pragmas and buffers come after the
   first task's function.
3. **State lives in globals** — both because locals do not persist between calls and because globals
   can be watched (`kfs watch x`). Uninitialised globals are used as zero.
4. **Units in names**: `altitude_m`, `pitch_rad`, `on_s`, `KP_PER_S2`, frame suffixes such as
   `prograde_BODY`, `v_CCI`.
5. **Float literals carry `f`** (`0.02f`), which keeps arithmetic in single precision and avoids
   `KC-W104`.
6. **Bus buffers are plain globals of the generated type**, declared next to their pragma:
   `kfs_msg_nav_state_t nav;`.
7. **"Nothing received yet" is `msg.hdr.seq == 0u`.**
8. **Booleans from the host are `kfs_bool32_t`**: tested with `!= 0u`, assigned `0u`.
9. **The task's own period is `sys.task_dt_s`.**
10. **Actuator loops run to the generated counts**:
    `for (int i = 0; i < THRUSTER_COUNT; i++) { … commands.thruster[i].pulse_s = …; }`.
11. **A mission sequence is an anonymous `enum` plus a `switch`** with a `case` for every enumerator
    (which also satisfies the `KC-W208` coverage check):
    `enum { LAUNCH, ASCENT, COAST, CIRCULARIZE, DONE } phase = LAUNCH;`.
12. **Pilot-mode programs issue service requests, not actuator writes**, and may repeat a request
    every tick (the ascent example re-issues `kfs_hold_heading` each tick during the gravity turn).
13. **Hot inner functions can be moved to kASM** and declared with `#include "name.kasm.h"`.

# Constructed examples

Each program below is **constructed from the front-end source, not compiled**: every construct in it
was checked against the parser, semantic rules and checks described in this directory, but none was
run through the game.

## 1. Heartbeat: logging and tracing

Constructed from the front-end source, not compiled.

```c
#include <kfs.h>
#pragma kfs tick_hz 10
#pragma kfs task report rate_hz=1

uint32_t beats;

void report(void)
{
    beats++;
    kfs_logf("beat %u at t=%.1f s", beats, sys.t_s);
    trace("beats", (float)beats);
}
```

- `rate_hz=1` divides the 10 Hz tick, giving a divisor of 10.[^frontend-entries]
- `%u` takes the 32-bit `beats`; `%.1f` takes the `float` `sys.t_s` without promotion.[^parser-logf]
- `trace` needs a literal channel name and any arithmetic value.

## 2. An `init` task, two rates, a constant table and provable loops

Constructed from the front-end source, not compiled.

```c
#include <kfs.h>
#include <math.h>
#pragma kfs tick_hz 50
#pragma kfs init setup
#pragma kfs task filter rate_hz=50
#pragma kfs task slow divisor=50

#define TAPS 4

static const float weights[TAPS] = { 0.4f, 0.3f, 0.2f, 0.1f };
static float history[TAPS];
float filtered;
uint32_t seconds;

void setup(void)
{
    for (int32_t i = 0; i < TAPS; i++)
        history[i] = 1.0f;
}

void filter(void)
{
    for (int32_t i = TAPS - 1; i > 0; i--)
        history[i] = history[i - 1];
    history[0] = sinf(sys.t_s);

    float acc = 0.0f;
    for (int32_t i = 0; i < TAPS; i++)
        acc += weights[i] * history[i];
    filtered = acc;
}

void slow(void)
{
    seconds++;
}
```

- Array sizes come from a macro, not a `const` variable (`KC-E105` otherwise).
- All three loops satisfy the bound prover: a local integer counter, a constant limit, one
  unconditional `++`/`--`, and a constant start, so no `KC-W711`.[^checks-bound]
- `slow` runs every 50th tick (`divisor=50`), i.e. once a second.
- `sinf` pulls the `<math.c>` library unit into the build; `sqrtf` would not.

## 3. Pilot mode: ask the stock flight computer once

Constructed from the front-end source, not compiled.

```c
#include <kfs.h>
#pragma kfs tick_hz 10
#pragma kfs task once rate_hz=10

static uint32_t done;

void once(void)
{
    if (done != 0u)
        return;
    kfs_hold_direction(KFS_VLF_PROGRADE, KFS_ROLL_FREE);
    kfs_set_throttle(0.5f);
    kfs_engines_on();
    done = 1u;
}
```

This is the kC counterpart of the shipped kASM examples `kAsm_engines_on` and `kAsm_hold_att`. The
program publishes nothing and claims nothing, so the three `GUIDANCE` services are allowed.[^frontend-owner]

## 4. Reading a host-published message

Constructed from the front-end source, not compiled.

```c
#include <kfs.h>
#include <vehicle.h>
#pragma kfs tick_hz 10
#pragma kfs subscribe MSG_FLIGHT_DATA flight
#pragma kfs task watch rate_hz=10

kfs_msg_flight_data_t flight;
float max_altitude_m;

void watch(void)
{
    if (flight.hdr.seq == 0u)
        return;
    if (flight.altitude_m > max_altitude_m)
        max_altitude_m = flight.altitude_m;
}
```

`flight` must be a non-`static` file-scope object of exactly the generated type.[^frontend-bus]

## 5. Struct values, designated initialisers and `<vecmat.h>`

Constructed from the front-end source, not compiled.

```c
#include <kfs.h>
#include <vecmat.h>
#pragma kfs tick_hz 10
#pragma kfs task spin rate_hz=10

typedef struct { vec3 axis; float rate_rps; } spin_t;

static const spin_t plan = { .axis = { 0.0f, 0.0f, 1.0f }, .rate_rps = 0.1f };
quat attitude = { 0.0f, 0.0f, 0.0f, 1.0f };

void spin(void)
{
    quat step = quat_from_axis_angle(plan.axis, plan.rate_rps * sys.task_dt_s);
    attitude = quat_normalize(quat_compose(attitude, step));
}
```

- Member designators (`.axis =`) and nested braces are accepted in static initialisers.[^parser-braced]
- Structs are passed, returned and assigned by value; a local struct may be initialised from a call.
- `<vecmat.h>` brings in both `<vecmat.c>` and `<math.c>`; only the reachable functions are kept.

# Things that look right but are rejected

| attempt | result |
| --- | --- |
| `int main(void) { … }` | `KC-E409` |
| `const int N = 8; float buf[N];` | `KC-E105` at file scope, `KC-E642` in a function |
| `while (1) { … }` in a task | compiles, with `KC-W711`; and a periodic task that never returns overruns its tick |
| `printf("x=%d\n", x);` | `KC-E645` |
| `kfs_logf(fmt_variable, x);` | `KC-E553` |
| `kfs_logf("%s", "text");` | `KC-E550` |
| `uint32_t *p = (uint32_t *)0x80000000;` | `KC-E644` |
| `void (*cb)(void);` | `KC-E303` |
| `#include "kfs.h"` | `KC-E012` at the `__at` line: system headers need `<…>` |
| `#pragma kfs task step rate_hz=RATE` with `RATE` a macro | `KC-E012`: macros are not expanded in `#pragma kfs` |
| writing `commands.…` without `#pragma kfs claim ALLOCATION` | `KC-E544` (back end) |

# Modding notes

- `Examples.Files` is a `public static readonly IReadOnlyDictionary<string, string>` built from a
  dictionary literal, and `Examples.Menu` an immutable list; a mod that wants to ship its own example
  projects should write files into the workspace itself rather than try to extend these
  ([workspace and files](/tooling/workspace-and-files.md)).
- When writing examples for mod-provided features, keep to the idioms above; in particular a
  mod-provided header must be included with angle brackets for its builtin prototypes to work
  ([kC generated headers](/language/kc-generated-headers.md)).

[^examples-files]: Examples.cs:41-59
[^examples-menu]: Examples.cs:61-79, 111-123
[^workbench-template]: Workbench.cs:2167-2168
[^frontend-entries]: KcFrontEnd.cs:598-627
[^frontend-owner]: KcFrontEnd.cs:204-244
[^frontend-bus]: KcFrontEnd.cs:283-314
[^parser-logf]: Parser.cs:3233-3349
[^parser-braced]: Parser.cs:1806-1883
[^checks-bound]: Checks.cs:533-657
