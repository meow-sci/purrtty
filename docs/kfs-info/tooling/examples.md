---
type: Language Reference
title: kFS embedded example programs
description: Every kC and kASM example program embedded in the game (Examples.cs), reproduced verbatim with its name, menu placement and purpose.
resource: ksa-decomp:KSA.Kfs.Host.Workbench/Examples.cs
tags: [kfs, tooling, examples, kc, kasm]
sources:
  - id: examples
    resource: ksa-decomp:KSA.Kfs.Host.Workbench/Examples.cs
    title: Examples (decompiled)
  - id: workbench
    resource: ksa-decomp:KSA.Kfs.Host.Workbench/Workbench.cs
    title: Workbench (decompiled)
  - id: editor-window
    resource: ksa-decomp:KSA.Kfs/KfsEditorWindow.cs
    title: KfsEditorWindow (decompiled)
  - id: workspace
    resource: ksa-decomp:KSA.Kfs.Host.Workbench/Workspace.cs
    title: Workspace (decompiled)
generated: { by: claude-code/claude-fable-5-2, at: 2026-11-25T21:56:52Z }
status: stable
ksa_build: "2026.11.25.5949"
---

# Purpose

`KSA.Kfs.Host.Workbench.Examples` is a public static class holding **16 source files in 14 example
folders** as C# string literals.[^examples] They are the only official kC / kASM samples in this build.
This document reproduces every one of them verbatim. For the language itself see
[kC language reference](/language/kc-language-reference.md) and
[kASM assembly language](/compiler/kasm-assembly-language.md); for how the files reach disk see
[workspace and files](/tooling/workspace-and-files.md).

Reproduction notes:

- The embedded strings use CRLF (`\r\n`) line endings and each ends with one trailing line break.
  The fenced blocks below use LF and omit that final line break; everything else, including trailing
  spaces inside comments, is byte-for-byte what `Examples.Files` holds.
- The blocks were extracted from the decompiled string literals by script, not retyped.

# The `Examples` API

| member | meaning |
| --- | --- |
| `public static readonly IReadOnlyDictionary<string,string> Files` | workspace-relative path (`folder/file`) to file text; the runtime object is a `Dictionary<string,string>` with ordinal keys |
| `public static IReadOnlyList<(string Category, string[] Names)> Menu { get; }` | the three categories shown in the editor's File > Examples menu |
| `public static IReadOnlyList<string> Names { get; }` | the example folders listed in `Menu`, flattened (7 names) |
| `public static IReadOnlyList<string> AllNames { get; }` | every distinct folder in `Files`, ordinal order (14 names) |
| `public static List<string> FilesOf(string name)` | the files of one example folder, ordinal order |
| `public static string? MainFile(string name)` | the first `.c` file of the folder, else its first file; this is the file the editor opens |
| `public static List<string> Write(Workspace workspace, string name, bool overwrite)` | writes one example folder; returns the paths written |
| `public static List<string> Write(Workspace workspace, bool overwrite, bool archived = false)` | writes all `Names` examples, or every file in `Files` when `archived` is true |
| `public const string SCOPE`, `DOUBLE`, `NAN`, `HOT_KC`, `HOT_KASM`, `HOT_KASM_DOT`, `KASM_DOT3`, `KASM_DOT3_KASM`, `KASM_ENGINES_ON`, `KASM_HOLD_ATT`, `KASM_PROFILE`, `BUS_ATTITUDE`, `KC_ATTITUDE`, `BUS_SLOW`, `ALL_STOCK`, `ASCENT_SIMPLE` | the 16 paths as constants |

Both `Write` overloads skip a file that already exists unless `overwrite` is true.[^examples]

## Menu placement

`Menu` is built once in the static initialiser:[^examples]

| category | example folders, in menu order |
| --- | --- |
| `kC` | `kC_ascent_simple`, `kC_attitude_control` |
| `kAsm` | `kAsm_engines_on`, `kAsm_hold_att`, `kAsm_dot3` |
| `Misc` | `ex_scope`, `ex_nan` |

The other seven folders (`ex_double`, `e2e04_kc`, `e2e04_kasm`, `ex_kasm`, `ex_bus`, `ex_busmon`,
`ex_stock`) are in `Files` but not in `Menu`; the `kfs examples` help text calls them "the archived
ones" (see [console commands](/tooling/console-commands.md)).

## How an example reaches the workspace

| route | call | what is written |
| --- | --- | --- |
| editor menu File > Examples > *category* > *folder* | `Workbench.CopyExample(name)` | the folder's files that are not already there; then the folder's `MainFile` is opened in the editor[^editor-window][^workbench] |
| console `kfs examples` | `Workbench.ExamplesCommand(args)` → `Examples.Write(Workspace, overwrite, archived)` | the 7 menu examples; `--force` overwrites existing files, `--all` adds the archived ones[^workbench] |

Nothing writes the examples automatically: a fresh workspace is empty until one of these is used
(see [workspace and files](/tooling/workspace-and-files.md)).

# Catalogue

| folder | files | menu | written by `kfs examples` | purpose |
| --- | --- | --- | --- | --- |
| `kC_ascent_simple` | `ascent_simple.c` | kC | yes | Pilot-mode ascent to a 200 km circular orbit: the program asks the stock flight computer for headings, throttle, staging and a circularisation burn through services, and reads `MSG_FLIGHT_DATA`, `MSG_VEHICLE_PROPS` and `MSG_BURN_STATUS`. |
| `kC_attitude_control` | `prograde_hold.c` | kC | yes | Full-control prograde hold: two 10 Hz tasks on the software bus; `attitude_control` publishes `MSG_WRENCH_CMD` (owning `ATTITUDE_CONTROL`), `allocate` claims `ALLOCATION` and writes `commands.thruster[i].pulse_s`. |
| `kAsm_engines_on` | `engines_on.kasm` | kAsm | yes | Pilot-mode kASM: one-shot `SVC_SET_THROTTLE` (1.0) and `SVC_ENGINES_ON`. |
| `kAsm_hold_att` | `hold_att.kasm` | kAsm | yes | Pilot-mode kASM: one-shot `SVC_HOLD_HEADING` (east, 45 degrees up, roll up). |
| `kAsm_dot3` | `dot3_fast.kasm`, `main.c` | kAsm | yes | Mixed project: `dot3_slow` in kC and `dot3_fast` in hand-written kASM, declared through the build-generated `dot3_fast.kasm.h`; meant for the Profiler tab. |
| `ex_scope` | `scope.c` | Misc | yes | Damped oscillator at 50 Hz for the oscilloscope (`kfs watch x`, Scope tab). |
| `ex_nan` | `nan.c` | Misc | yes | NaN watch demonstration: `sqrtf` of a value that goes negative from tick 101. |
| `ex_double` | `double.c` | (none) | only with `--all` | Profiler example: soft double arithmetic (`__muldf3`, `__adddf3`) on the K32F core. |
| `e2e04_kc` | `main.c` | (none) | only with `--all` | Step 1 of 2: a hot kC function (`dot3`) to profile. |
| `e2e04_kasm` | `dot3.kasm`, `main.c` | (none) | only with `--all` | Step 2 of 2: the same program with `dot3` replaced by hand-written kASM (`dot3.kasm`, declared through the generated `dot3.kasm.h`). |
| `ex_kasm` | `profile.kasm` | (none) | only with `--all` | kASM profile example with an exact per-tick cycle derivation in its header comment. |
| `ex_bus` | `attitude_hold.c` | (none) | only with `--all` | Attitude hold on the bus at 50 Hz with an init task (`#pragma kfs init capture`). |
| `ex_busmon` | `slow_pub.kasm` | (none) | only with `--all` | Bus monitor test fixture in kASM (`.publish` / `.subscribe`, `offsetof(...)`), with the expected monitor values derived in its header comment. |
| `ex_stock` | `all_stock.c` | (none) | only with `--all` | Smallest pilot-mode program: owns no block, one empty 10 Hz task. |

# Menu examples

## `kC_ascent_simple`

Pilot-mode ascent to a 200 km circular orbit: the program asks the stock flight computer for headings, throttle, staging and a circularisation burn through services, and reads `MSG_FLIGHT_DATA`, `MSG_VEHICLE_PROPS` and `MSG_BURN_STATUS`.

- Menu: File > Examples > kC
- Main file (opened by `CopyExample`): `kC_ascent_simple/ascent_simple.c`

### `kC_ascent_simple/ascent_simple.c`

Constant `Examples.ASCENT_SIMPLE`; string literal at Examples.cs:58.[^examples]

```c
/* A simple ascent to orbit for the Rocket, from Earth's LC-39A launch site to 
   a 200 km circular orbit.

   The stock flight computer steers the rocket. This program tells it what to
   do, as a pilot would:
     1. LAUNCH:   Point straight up, full throttle, engines on.
     2. ASCENT:   Gravity turn to the east, MECO when apogee reaches 200 km,
                  drop the empty first stage.
     3. COAST:    Coast up to the apoapsis. Once out of the air, plan a
                  circularization burn and start it on the upper stage's engine.
     4. CIRCULARIZE: The flight computer flies the burn; wait for it to end.
     5. DONE:     In orbit. */
#include <kfs.h>
#include <vehicle.h>

#pragma kfs tick_hz 10
#pragma kfs subscribe MSG_FLIGHT_DATA flight
#pragma kfs subscribe MSG_VEHICLE_PROPS props
#pragma kfs subscribe MSG_BURN_STATUS burn_status
#pragma kfs task fly rate_hz=10

#define TARGET_ALTITUDE_M  (200.0f * KM)  /* the atmosphere ends at 167 km */
#define TURN_END_ALT_M     (100.0f * KM)  /* where the turn ends */
#define FINAL_PITCH_DEG    10.0f          /* pitch above the horizon after it */
#define EAST_DEG           90.0f          /* launching east borrows the Earth's spin */

kfs_msg_flight_data_t flight;       /* altitude, apoapsis, ... (updated by the game) */
kfs_msg_vehicle_props_t props;      /* mass, engines, ... */
kfs_msg_burn_status_t burn_status;  /* how the planned burn is going */

enum { LAUNCH, ASCENT, COAST, CIRCULARIZE, DONE } phase = LAUNCH;
kfs_burn_t circ_burn;
/* How the circularisation ended (KFS_BURN_DONE when it worked). */
uint32_t final_state;

void fly(void)
{
    switch (phase) {
    case LAUNCH:
        kfs_hold_heading(EAST_DEG * DEG2RAD, 90.0f * DEG2RAD, KFS_ROLL_UP);
        kfs_set_throttle(1.0f);
        kfs_engines_on();
        phase = ASCENT;
        break;

    case ASCENT:
        /* Gravity turn from 90 degrees to FINAL_PITCH_DEG at TURN_END_ALT_M. */
        if (flight.apoapsis_m < TARGET_ALTITUDE_M) {
            float pitch_deg = 90.0f - (90.0f - FINAL_PITCH_DEG)
                              * flight.altitude_m / TURN_END_ALT_M;
            if (pitch_deg < FINAL_PITCH_DEG) pitch_deg = FINAL_PITCH_DEG;
            kfs_hold_heading(EAST_DEG * DEG2RAD, pitch_deg * DEG2RAD, KFS_ROLL_UP);
            break;
        }
        /* MECO: Drop the first stage (the decoupler), stage again to activate
           the upper stage's engine, and point along the direction of travel. */
        kfs_engines_off();
        kfs_stage();
        kfs_stage();
        kfs_hold_direction(KFS_VLF_PROGRADE, KFS_ROLL_FREE);
        kfs_logf("MECO: altitude %.1f km, apoapsis %.1f km",
                 flight.altitude_m / KM, flight.apoapsis_m / KM);
        phase = COAST;
        break;

    case COAST:
        /* Wait until we're exo-atmosphere to plan circularization (drag would
           spoil the plan), and only when the upper engine is active and has
           propellant. A newly-activated engine reports its propellant a moment
           later, and a burn started before then ends at once. */
        if (flight.in_atmosphere) break;
        for (int32_t i = 0; i < ENGINE_COUNT; ++i) {
            if (props.engine[i].active != 0u && props.engine[i].propellant_ok != 0u) {
                circ_burn = kfs_plan_circularize(KFS_AT_APOAPSIS);
                kfs_burn_start(circ_burn);
                kfs_logf("circularization planned: apoapsis %.1f km in %.0f s",
                         flight.apoapsis_m / KM, flight.time_to_apoapsis_s);
                phase = CIRCULARIZE;
                break;
            }
        }
        break;

    case CIRCULARIZE:
        if (burn_status.burn == circ_burn
            && (burn_status.state == KFS_BURN_DONE || burn_status.state == KFS_BURN_ABORTED)) {
            final_state = burn_status.state;
            kfs_logf("circularization ended (state %u): apoapsis %.1f km, periapsis %.1f km", final_state,
                     flight.apoapsis_m / KM, flight.periapsis_m / KM);
            phase = DONE;
        }
        break;

    case DONE:
        break;
    }
}
```

## `kC_attitude_control`

Full-control prograde hold: two 10 Hz tasks on the software bus; `attitude_control` publishes `MSG_WRENCH_CMD` (owning `ATTITUDE_CONTROL`), `allocate` claims `ALLOCATION` and writes `commands.thruster[i].pulse_s`.

- Menu: File > Examples > kC
- Main file (opened by `CopyExample`): `kC_attitude_control/prograde_hold.c`

### `kC_attitude_control/prograde_hold.c`

Constant `Examples.KC_ATTITUDE`; string literal at Examples.cs:53.[^examples]

```c
/* Point prograde: A bus example in full control (no stock flight computer).
   Two 10 Hz tasks in one file on the software bus:

     attitude_control - subscribes MSG_NAV_STATE and MSG_VEHICLE_PROPS and publishes
                        MSG_WRENCH_CMD (so it owns ATTITUDE_CONTROL)
     allocate - subscribes MSG_WRENCH_CMD and MSG_VEHICLE_PROPS, claims ALLOCATION.
   
   Declared in this order in one file, attitude_control runs first, so allocate sees 
   this tick's wrench.

   The target is prograde: the vehicle's nose, body +X (KSA's body frame is X
   forward, Y right, Z down), along its velocity relative to the parent body in
   CCI. Roll about the nose is left free and only damped. */
#include <kfs.h>
#include <math.h>
#include <vecmat.h>
#include <vehicle.h>

#pragma kfs tick_hz 10
#pragma kfs subscribe MSG_NAV_STATE     nav
#pragma kfs subscribe MSG_VEHICLE_PROPS props
#pragma kfs publish   MSG_WRENCH_CMD    wrench       /* owns ATTITUDE_CONTROL */
#pragma kfs task attitude_control rate_hz=10

kfs_msg_nav_state_t     nav;
kfs_msg_vehicle_props_t props;
kfs_msg_wrench_cmd_t    wrench;

vec3  prograde_BODY;     /* prograde in the body frame; watch these with kfs watch */
vec3  err_BODY_rad;      /* the rotation that takes the nose to prograde */
float point_err_rad;     /* the angle between the nose and prograde */

#define KP_PER_S2  0.4f    /* rad/s^2 per rad of pointing error */
#define KD_PER_S   1.2f    /* rad/s^2 per rad/s of rate */

void attitude_control(void)
{
    wrench.force_body_N = vec3_zero();
    wrench.throttle     = 0.0f;
    wrench.engines_on   = 0u;
    if (nav.hdr.seq == 0u) {    /* no navigation yet: command nothing */
        wrench.torque_body_Nm = vec3_zero();
        return;
    }

    /* Velocity into the body frame: q_body_to_cci takes BODY to CCI, so its
       inverse takes CCI to BODY. */
    vec3 v_CCI = vec3_from_d(nav.v_cci_mps);
    prograde_BODY = vec3_normalize(quat_rotate(quat_inverse(nav.q_body_to_cci), v_CCI));

    /* Rotating the nose about nose × target moves it toward the target, by the
       angle between them. */
    vec3  nose  = vec3_make(1.0f, 0.0f, 0.0f);
    vec3  axis  = vec3_cross(nose, prograde_BODY);
    float s     = vec3_length(axis);
    float c     = vec3_dot(nose, prograde_BODY);
    point_err_rad = atan2f(s, c);
    if (s > 1e-6f) {
        err_BODY_rad = vec3_scale(axis, point_err_rad / s);
    } else if (c < 0.0f) {
        /* Exactly retrograde: any axis across the nose will do. */
        err_BODY_rad = vec3_make(0.0f, point_err_rad, 0.0f);
    } else {
        err_BODY_rad = vec3_zero();
    }

    vec3 alpha = vec3_sub(vec3_scale(err_BODY_rad, KP_PER_S2),
                          vec3_scale(nav.rates_body_rps, KD_PER_S));
    wrench.torque_body_Nm = mat3_mul_vec3(props.I_body_kgm2, alpha);
}

/* The allocator: it reads attitude_control's wrench through its own
   subscription buffer, not the publication buffer. */
#pragma kfs claim ALLOCATION
#pragma kfs subscribe MSG_WRENCH_CMD wrench_in
#pragma kfs task allocate rate_hz=10

kfs_msg_wrench_cmd_t wrench_in;

void allocate(void)
{
    const float dt_s = sys.task_dt_s;    /* this task's period: 0.1 s at 10 Hz */
    for (int i = 0; i < THRUSTER_COUNT; i++) {
        vec3 tq = props.thruster[i].torque_body_Nm;
        float along = vec3_dot(tq, wrench_in.torque_body_Nm);
        float on_s = (along > 0.0f)
            ? fminf(along / vec3_dot(tq, tq), 1.0f) * dt_s
            : 0.0f;
        /* This allocator drops sub-minimum pulses. Left alone, the platform
           rounds them up. */
        commands.thruster[i].pulse_s =
            (on_s >= props.thruster[i].min_pulse_s) ? on_s : 0.0f;
    }
}
```

## `kAsm_engines_on`

Pilot-mode kASM: one-shot `SVC_SET_THROTTLE` (1.0) and `SVC_ENGINES_ON`.

- Menu: File > Examples > kAsm
- Main file (opened by `CopyExample`): `kAsm_engines_on/engines_on.kasm`

### `kAsm_engines_on/engines_on.kasm`

Constant `Examples.KASM_ENGINES_ON`; string literal at Examples.cs:51.[^examples]

```kasm
; engines_on.kasm: Pilot mode. The stock flight computer flies the vehicle;
; this program asks it, once, for full throttle and the engine switch on. A
; service's arguments go on the stack first, the first one deepest, and SYSCALL
; pops them.
.include <kfs.kinc>
.tick_hz 10
.global done, 4                     ; 1 once the requests are made

.task seq rate_hz=10
.func seq
    LDG   done                      ; done already: nothing more to ask
    JNZ   out
    PUSH32 1.0f                     ; f32 throttle: full
    SYSCALL SVC_SET_THROTTLE        ; pops 1, pushes 0
    SYSCALL SVC_ENGINES_ON          ; pops 0, pushes 0
    PUSH8 1
    STG   done
out:
    RET   0
.endfunc
```

## `kAsm_hold_att`

Pilot-mode kASM: one-shot `SVC_HOLD_HEADING` (east, 45 degrees up, roll up).

- Menu: File > Examples > kAsm
- Main file (opened by `CopyExample`): `kAsm_hold_att/hold_att.kasm`

### `kAsm_hold_att/hold_att.kasm`

Constant `Examples.KASM_HOLD_ATT`; string literal at Examples.cs:52.[^examples]

```kasm
; hold_att.kasm: Pilot mode. Asks the stock flight computer to hold the nose
; on a compass heading of 90 degrees (east), 45 degrees above the horizon,
; roll up. It keeps holding it until something else asks for another attitude.
; Angles are radians, written as float32 constants.
.include <kfs.kinc>
.tick_hz 10
.global done, 4                     ; 1 once the request is made

.task seq rate_hz=10
.func seq
    LDG   done
    JNZ   out
    PUSH32 1.5707964f               ; f32 heading_rad: 90 degrees, east     (deepest)
    PUSH32 0.7853982f               ; f32 pitch_rad: 45 degrees above the horizon
    PUSH8 KFS_ROLL_UP               ; u32 roll: up                      (on top)
    SYSCALL SVC_HOLD_HEADING        ; pops 3, pushes 0
    PUSH8 1
    STG   done
out:
    RET   0
.endfunc
```

## `kAsm_dot3`

Mixed project: `dot3_slow` in kC and `dot3_fast` in hand-written kASM, declared through the build-generated `dot3_fast.kasm.h`; meant for the Profiler tab.

- Menu: File > Examples > kAsm
- Main file (opened by `CopyExample`): `kAsm_dot3/main.c`

### `kAsm_dot3/dot3_fast.kasm`

Constant `Examples.KASM_DOT3_KASM`; string literal at Examples.cs:50.[^examples]

```kasm
; dot3_fast: dot3 by hand (for float[3]): The stack column shows the operand 
; depth after each line; it peaks at 4 and returns with 1 (the float result).
.include <kfs.kinc>

.proto "float dot3_fast(const float *a, const float *b)"
.func dot3_fast
    .args a, b
    LDL   a
    LDW
    LDL   b
    LDW
    FMUL
    LDL   a
    PUSH8 4
    ADD
    LDW
    LDL   b
    PUSH8 4
    ADD
    LDW
    FMUL
    FADD
    LDL   a
    PUSH8 8
    ADD
    LDW
    LDL   b
    PUSH8 8
    ADD
    LDW
    FMUL
    FADD
    RET   1
.endfunc
```

### `kAsm_dot3/main.c`

Constant `Examples.KASM_DOT3`; string literal at Examples.cs:49.[^examples]

```c
/* dot3 two ways, for the profiler: dot3_slow in kC below, and dot3_fast written
   by hand in kASM (dot3_fast.kasm), declared through the dot3_fast.kasm.h the
   build generates. step calls each 20 times a tick. Load it, start it and open
   the Profiler tab. Compare the two functions' cycles per call. The Code tab's
   cycles column shows each line's cost too. */
#include <kfs.h>
#include "dot3_fast.kasm.h"
#pragma kfs tick_hz 50
#pragma kfs task step rate_hz=50

float va[3] = { 1.0f, 2.0f, 3.0f };
float vb[3] = { 4.0f, 5.0f, 6.0f };
float out_slow;
float out_fast;

float dot3_slow(const float *a, const float *b) {
    float s = 0.0f;
    int32_t i;
    for (i = 0; i < 3; i++)
        s += a[i] * b[i];
    return s;
}

void step(void) {
    float slow = 0.0f;
    float fast = 0.0f;
    int32_t k;
    for (k = 0; k < 20; k++)
        slow += dot3_slow(va, vb);
    for (k = 0; k < 20; k++)
        fast += dot3_fast(va, vb);
    out_slow = slow;
    out_fast = fast;
}
```

## `ex_scope`

Damped oscillator at 50 Hz for the oscilloscope (`kfs watch x`, Scope tab).

- Menu: File > Examples > Misc
- Main file (opened by `CopyExample`): `ex_scope/scope.c`

### `ex_scope/scope.c`

Constant `Examples.SCOPE`; string literal at Examples.cs:43.[^examples]

```c
/* Oscilloscope example: a damped oscillator integrated at 50 Hz, restarted
   every 10 s. Watch x and v with kfs watch x and kfs watch v, or the editor's
   scope pane (Watch). */
#include <kfs.h>
#pragma kfs tick_hz 50
#pragma kfs task step rate_hz=50
#define DT 0.02f
#define OMEGA 3.0f
#define ZETA 0.05f

float x = 1.0f;
float v = 0.0f;
uint32_t ticks;

void step(void) {
    float a = -2.0f * ZETA * OMEGA * v - OMEGA * OMEGA * x;
    v += a * DT;
    x += v * DT;
    ticks++;
    if (ticks % 500u == 0u) {
        x = 1.0f;
        v = 0.0f;
    }
}
```

## `ex_nan`

NaN watch demonstration: `sqrtf` of a value that goes negative from tick 101.

- Menu: File > Examples > Misc
- Main file (opened by `CopyExample`): `ex_nan/nan.c`

### `ex_nan/nan.c`

Constant `Examples.NAN`; string literal at Examples.cs:45.[^examples]

```c
/* NaN watch example: level falls by 0.01 a tick, and from the 101st tick the
   square root's argument is negative, so root and then filtered become NaN. The
   NaN watch names the line and the tick, and lists the globals that went NaN.
   */
#include <kfs.h>
#include <math.h>
#pragma kfs tick_hz 50
#pragma kfs task step rate_hz=50

uint32_t count;
float level = 1.0f;
float root;
float filtered;

void step(void) {
    count++;
    level = 1.0f - 0.01f * (float)count;
    root = sqrtf(level);
    filtered = 0.9f * filtered + 0.1f * root;
}
```

# Archived examples

These are embedded but absent from `Menu` and `Names`; only `kfs examples --all` (or
`Examples.Write(workspace, name, overwrite)` called directly) writes them.

## `ex_double`

Profiler example: soft double arithmetic (`__muldf3`, `__adddf3`) on the K32F core.

- Menu: not in the menu (archived; `kfs examples --all`)
- Main file (opened by `CopyExample`): `ex_double/double.c`

### `ex_double/double.c`

Constant `Examples.DOUBLE`; string literal at Examples.cs:44.[^examples]

```c
/* Profiler example: double arithmetic is soft on the Mk2 (K32F), so __muldf3
   and __adddf3 show up as children of the function that ran them, and the
   line's cost names them. kfs prof, or the editor's profiler pane. On the Mk3
   (K32D) the same lines are hardware: try Preview. */
#include <kfs.h>
#pragma kfs tick_hz 50
#pragma kfs task step rate_hz=50

double pos = 0.0;
double vel = 1.0;
float pos_f;

static double integrate(double p, double v, double dt) {
    return p + v * dt;
}

void step(void) {
    pos = integrate(pos, vel, 0.02);
    pos_f = (float)pos;
}
```

## `e2e04_kc`

Step 1 of 2: a hot kC function (`dot3`) to profile.

- Menu: not in the menu (archived; `kfs examples --all`)
- Main file (opened by `CopyExample`): `e2e04_kc/main.c`

### `e2e04_kc/main.c`

Constant `Examples.HOT_KC`; string literal at Examples.cs:46.[^examples]

```c
/* Step 1 of 2: a hot kC function. dot3 runs 20 times a tick; profile it with
   kfs prof and note dot3's cycles per call. Step 2 is e2e04_kasm/: the same
   program with dot3 in kASM. */
#include <kfs.h>
#pragma kfs tick_hz 50
#pragma kfs task step rate_hz=50

float va[3] = { 1.0f, 2.0f, 3.0f };
float vb[3] = { 4.0f, 5.0f, 6.0f };
float out;

float dot3(const float *a, const float *b) {
    float s = 0.0f;
    int32_t i;
    for (i = 0; i < 3; i++)
        s += a[i] * b[i];
    return s;
}

void step(void) {
    float acc = 0.0f;
    int32_t k;
    for (k = 0; k < 20; k++)
        acc += dot3(va, vb);
    out = acc;
}
```

## `e2e04_kasm`

Step 2 of 2: the same program with `dot3` replaced by hand-written kASM (`dot3.kasm`, declared through the generated `dot3.kasm.h`).

- Menu: not in the menu (archived; `kfs examples --all`)
- Main file (opened by `CopyExample`): `e2e04_kasm/main.c`

### `e2e04_kasm/dot3.kasm`

Constant `Examples.HOT_KASM_DOT`; string literal at Examples.cs:48.[^examples]

```kasm
; dot3 by hand (for float[3]): 46 cycles on the Mk2. The stack column shows the
; operand depth after each line; it peaks at 4 and returns with 1 (the float
; result).
.include <kfs.kinc>

.proto "float dot3(const float *a, const float *b)"
.func dot3
    .args a, b
    LDL   a
    LDW
    LDL   b
    LDW
    FMUL
    LDL   a
    PUSH8 4
    ADD
    LDW
    LDL   b
    PUSH8 4
    ADD
    LDW
    FMUL
    FADD
    LDL   a
    PUSH8 8
    ADD
    LDW
    LDL   b
    PUSH8 8
    ADD
    LDW
    FMUL
    FADD
    RET   1
.endfunc
```

### `e2e04_kasm/main.c`

Constant `Examples.HOT_KASM`; string literal at Examples.cs:47.[^examples]

```c
/* Step 2 of 2: dot3 replaced by the hand-written kASM in dot3.kasm, declared
   through the generated dot3.kasm.h. Profile again: dot3 costs 46 cycles a call
   on the Mk2, and step's total falls by the difference times 20. */
#include <kfs.h>
#include "dot3.kasm.h"
#pragma kfs tick_hz 50
#pragma kfs task step rate_hz=50

float va[3] = { 1.0f, 2.0f, 3.0f };
float vb[3] = { 4.0f, 5.0f, 6.0f };
float out;

void step(void) {
    float acc = 0.0f;
    int32_t k;
    for (k = 0; k < 20; k++)
        acc += dot3(va, vb);
    out = acc;
}
```

## `ex_kasm`

kASM profile example with an exact per-tick cycle derivation in its header comment.

- Menu: not in the menu (archived; `kfs examples --all`)
- Main file (opened by `CopyExample`): `ex_kasm/profile.kasm`

### `ex_kasm/profile.kasm`

Constant `Examples.KASM_PROFILE`; string literal at Examples.cs:54.[^examples]

```kasm
; kASM profile example: one double multiply and one double add a tick, soft on
; the Mk2 (K32F). Per tick: t self 20 (call 3, ENTER 6, PUSH64 2 x3, DROP2 2,
; RET 3), __muldf3 120, __adddf3 80 = 220. The cost column is exact; the stack
; column shows the depth after each line in cells (a double is 2).
.include <kfs.kinc>
.tick_hz 50

.task t rate_hz=50
.func t
    PUSH64 2.0
    PUSH64 3.0
    DMUL
    PUSH64 1.0
    DADD
    DROP2
    RET   0
.endfunc
```

## `ex_bus`

Attitude hold on the bus at 50 Hz with an init task (`#pragma kfs init capture`).

- Menu: not in the menu (archived; `kfs examples --all`)
- Main file (opened by `CopyExample`): `ex_bus/attitude_hold.c`

### `ex_bus/attitude_hold.c`

Constant `Examples.BUS_ATTITUDE`; string literal at Examples.cs:55.[^examples]

```c
/* Attitude hold: two 50 Hz tasks in one file on the software bus.
   attitude_control subscribes MSG_NAV_STATE and MSG_VEHICLE_PROPS, publishes
   MSG_WRENCH_CMD (so it owns ATTITUDE_CONTROL) and holds the attitude init read
   from MSG_NAV_STATE on the first boot tick; allocate subscribes MSG_WRENCH_CMD
   and MSG_VEHICLE_PROPS and claims ALLOCATION, with a crude allocator. Declared
   in this order in one file, attitude_control runs first, so allocate sees this
   tick's wrench. */
#include <kfs.h>
#include <vecmat.h>
#include <vehicle.h>

#pragma kfs init capture
#pragma kfs subscribe MSG_NAV_STATE     nav
#pragma kfs subscribe MSG_VEHICLE_PROPS props
#pragma kfs publish   MSG_WRENCH_CMD    wrench       /* owns ATTITUDE_CONTROL */
#pragma kfs task attitude_control rate_hz=50

kfs_msg_nav_state_t     nav;
kfs_msg_vehicle_props_t props;
kfs_msg_wrench_cmd_t    wrench;

quat q_hold;             /* the attitude to hold (BODY to CCI): the first seen */
uint32_t hold_seq;       /* the seq of that navigation message (0: none yet) */
vec3 err_BODY_rad;       /* watch these with kfs watch */

#define KP_PER_S2  0.4f    /* rad/s^2 per rad of attitude error */
#define KD_PER_S   1.2f    /* rad/s^2 per rad/s of rate */

void capture(void)
{
    q_hold = nav.q_body_to_cci;
    hold_seq = nav.hdr.seq;
}

void attitude_control(void)
{
    /* No navigation on the boot tick: hold the first one we get. */
    if (hold_seq == 0u && nav.hdr.seq != 0u) {
        q_hold = nav.q_body_to_cci;
        hold_seq = nav.hdr.seq;
    }
    err_BODY_rad = quat_attitude_error(q_hold, nav.q_body_to_cci);
    vec3 alpha = vec3_sub(vec3_scale(err_BODY_rad, KP_PER_S2),
                          vec3_scale(nav.rates_body_rps, KD_PER_S));
    wrench.torque_body_Nm = mat3_mul_vec3(props.I_body_kgm2, alpha);
    wrench.force_body_N   = vec3_zero();
    wrench.throttle       = 0.0f;
    wrench.engines_on     = 0u;
}

/* The allocator: it reads attitude_control's wrench through its own
   subscription buffer, not the publication buffer. */
#pragma kfs claim ALLOCATION
#pragma kfs subscribe MSG_WRENCH_CMD wrench_in
#pragma kfs task allocate rate_hz=50

kfs_msg_wrench_cmd_t wrench_in;

void allocate(void)
{
    const float dt_s = sys.task_dt_s;    /* this task's period: 0.02 s at 50 Hz */
    for (int i = 0; i < THRUSTER_COUNT; i++) {
        vec3 tq = props.thruster[i].torque_body_Nm;
        float along = vec3_dot(tq, wrench_in.torque_body_Nm);
        float on_s = (along > 0.0f)
            ? fminf(along / vec3_dot(tq, tq), 1.0f) * dt_s
            : 0.0f;
        /* This allocator drops sub-minimum pulses. Left alone, the platform
           rounds them up. */
        commands.thruster[i].pulse_s =
            (on_s >= props.thruster[i].min_pulse_s) ? on_s : 0.0f;
    }
}
```

## `ex_busmon`

Bus monitor test fixture in kASM (`.publish` / `.subscribe`, `offsetof(...)`), with the expected monitor values derived in its header comment.

- Menu: not in the menu (archived; `kfs examples --all`)
- Main file (opened by `CopyExample`): `ex_busmon/slow_pub.kasm`

### `ex_busmon/slow_pub.kasm`

Constant `Examples.BUS_SLOW`; string literal at Examples.cs:56.[^examples]

```kasm
; TICKS: 60
; EXPECTED: `slow` (10 Hz on a 50 Hz computer, divisor 5) runs at ticks 0, 5,
;   10, ..., writes sys.tick as a float into torque_body_Nm.x (a staircase 0, 5,
;   10, ... when plotted) and publishes MSG_WRENCH_CMD on completion, so after
;   tick k the latched slot has seq floor(k/5) + 1, source 0, flags 0 and t_us =
;   met_us of tick 5*floor(k/5) = 100,000*floor(k/5). The bus monitor after tick
;   k: publisher computer 0, task slow; age = met_us(k) - t_us = 20,000*(k mod
;   5) us, (k mod 5) ticks; rate = (seq_k - seq_(k-N)) * 50 / N with N = min(50,
;   k + 1): 50 after tick 0 (one publication in one tick), 10 after tick 4 (1 *
;   50 / 5) and after tick 9 (2 * 50 / 10), and 10 from tick 49 on (ten
;   publications in the last fifty ticks). `fast` subscribes the message, so the
;   slot is in Messages[] with role 3.
; Bus monitor fixture; the oracle is the derivation above, never the monitor's
; output.
.include <kfs.kinc>
.include <vehicle.kinc>
.tick_hz 50
.publish   MSG_WRENCH_CMD out
.subscribe MSG_WRENCH_CMD seen
.global last_seq, 4

.task fast rate_hz=50
.func fast
    LDG   seen + offsetof(kfs_msg_wrench_cmd_t, hdr.seq)
    STG   last_seq
    RET   0
.endfunc

.task slow rate_hz=10
.func slow
    LDG   sys.tick
    I2F
    STG   out + offsetof(kfs_msg_wrench_cmd_t, torque_body_Nm.x)
    RET   0
.endfunc
```

## `ex_stock`

Smallest pilot-mode program: owns no block, one empty 10 Hz task.

- Menu: not in the menu (archived; `kfs examples --all`)
- Main file (opened by `CopyExample`): `ex_stock/all_stock.c`

### `ex_stock/all_stock.c`

Constant `Examples.ALL_STOCK`; string literal at Examples.cs:57.[^examples]

```c
/* all_stock.c: owns no block, so the computer runs in pilot mode and the game's
   own flight computer flies the vehicle. Load it with kfs load
   ex_stock/all_stock.c --vehicle and kfs start; the UI's attitude and burn
   settings work as without kFS. */
#pragma kfs tick_hz 10
#pragma kfs task idle rate_hz=10
void idle(void) { }
```

# Things the examples show

Read directly from the sources above; the linked documents explain each construct.

| construct | seen in | see |
| --- | --- | --- |
| `#pragma kfs tick_hz N`, `#pragma kfs task NAME rate_hz=N` | every kC example | [kC program structure](/language/kc-program-structure.md) |
| `#pragma kfs init NAME` | `ex_bus/attitude_hold.c` | [kC program structure](/language/kc-program-structure.md) |
| `#pragma kfs subscribe MSG var`, `#pragma kfs publish MSG var`, `#pragma kfs claim ALLOCATION` | `kC_attitude_control`, `ex_bus`, `kC_ascent_simple` | [message bus](/host/message-bus.md), [ownership and control modes](/host/ownership-and-control-modes.md) |
| `#include <kfs.h>`, `<math.h>`, `<vecmat.h>`, `<vehicle.h>` | kC examples | [kC generated headers](/language/kc-generated-headers.md), [kC standard library](/language/kc-standard-library.md) |
| `#include "name.kasm.h"` for a kASM function declared with `.proto` | `kAsm_dot3`, `e2e04_kasm` | [build and load flow](/tooling/build-and-load-flow.md) |
| service calls `kfs_hold_heading`, `kfs_set_throttle`, `kfs_engines_on`, `kfs_engines_off`, `kfs_stage`, `kfs_hold_direction`, `kfs_plan_circularize`, `kfs_burn_start`, `kfs_logf` | `kC_ascent_simple` | [services table](/tables/services-table.md) |
| `SYSCALL SVC_SET_THROTTLE`, `SVC_ENGINES_ON`, `SVC_HOLD_HEADING` with arguments pushed first-deepest | `kAsm_engines_on`, `kAsm_hold_att` | [services syscall ABI](/vm/services-syscall-abi.md) |
| `commands.thruster[i].pulse_s`, `props.thruster[i]`, `THRUSTER_COUNT`, `ENGINE_COUNT`, `sys.task_dt_s` | `kC_attitude_control`, `ex_bus`, `kC_ascent_simple` | [I/O layout generation](/host/io-layout-generation.md), [memory map and system block](/vm/memory-map-and-sysblock.md) |
| `.include <kfs.kinc>`, `.tick_hz`, `.global`, `.task`, `.func`/`.endfunc`, `.args`, `.proto`, `.publish`, `.subscribe`, `offsetof(...)` | kASM examples | [kASM assembly language](/compiler/kasm-assembly-language.md) |

None of the 16 files touches lights or part animations: in this build the examples exercise only
attitude holds, throttle and the engine switch, staging, planned burns, logging, RCS thruster pulses
and the software bus.

# Modding notes

- **Visibility.** `Examples` and all members listed above are `public static`. `Files` is declared
  `IReadOnlyDictionary<string,string>` but is initialised with a plain
  `new Dictionary<string,string>(StringComparer.Ordinal)`, so its runtime object is mutable through a
  cast.[^examples] `Menu`, `Names` and `AllNames` are get-only auto-properties computed once in the
  static initialiser, so entries added to `Files` afterwards do not appear in them.
- **Shipping extra examples from a mod.** The lowest-risk route does not touch `Examples` at all:
  call the public `Workspace.Write(file, text)` on `KfsIntegration.Workbench.Workspace` to drop a
  project folder into the workspace, then `Workbench.Edit(file)` to open it.[^workspace][^workbench]
  To make a mod example appear under File > Examples, the menu data has to change:
  `KfsEditorWindow.DrawExampleItems` (private) iterates `Examples.Menu` every frame and calls
  `Workbench.CopyExample(name)`, which resolves the name through `Examples.MainFile` and
  `Examples.Write`, both of which read `Examples.Files`.[^editor-window][^workbench] `Inferred:` a
  Harmony postfix on the `Examples.Menu` getter that appends a category, plus entries added to the
  `Files` dictionary, is therefore enough for the menu route; `kfs examples` (which filters by
  `Names`) would additionally need the `Names` getter patched. This was not tested in the game.
- **Threading.** `Examples.Write` does synchronous file I/O on the caller's thread; both stock
  callers run on the main thread (console command or ImGui menu click).
- **Overwrite semantics.** Existing files are never replaced unless `--force` / `overwrite: true`, so
  a player's edits to an example survive a second copy.

[^examples]: Examples.cs — path constants :9-39; `Files` dictionary :41-59 (one literal per line, 43-58); `Menu` :61-79; `Names` :81; `AllNames` :83-84; `FilesOf` :86-89; `MainFile` :91-95; `Write(workspace, name, overwrite)` :97-109; `Write(workspace, overwrite, archived)` :111-123.
[^workbench]: Workbench.cs — `ExamplesCommand` :2135-2140; `CopyExample` :2142-2154; `Edit` :420-451.
[^editor-window]: KfsEditorWindow.cs — File > Examples submenu :970-974; `DrawExampleItems` :1013-1035.
[^workspace]: Workspace.cs — `Write` :105-111.
