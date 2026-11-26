# Spike 06 — purrTTY service catalog and simulated parts

Throw-away feasibility spike for the kFS parts extension (`<purrtty.h>`, services `0x5000–0x50FF`).
It installs the full planned API surface into the **unmodified** game toolchain outside the game,
compiles kC with the game's own compiler, runs it on the game's own VM and `FakeVehicle`, and routes
the mod services to an in-memory parts simulation. Nothing here was run inside the game.

## Files

| File | What |
| --- | --- |
| `ServiceCatalog.cs` | Single source of truth: 22 service definitions + 7 enum/constant groups → services.json rows, enums.json entries, `purrtty.h` text, builtin names, id lookup, a kASM `.equ` block, and `SelfCheck()` (re-derives the loader/compiler rules against the *generated header text*). No KSA types. |
| `VehicleParts.cs` | `IVehicleParts` (frozen lists + generation; getters for every `geti`/`getf` property; one method per setter) and `SimVehicleParts` (switch groups, overrides/reset, clamping, animations that slew at 1 s/s and derive their state). No KSA types. |
| `KfsInstall.cs` | Harmony postfix on `GameTables.LoadEmbedded` (augmented `services.json` + `enums.json`, stock tables on any failure), `SystemHeaders.Files["purrtty.h"]`, `SystemHeaders.Builtins`. |
| `PurrRouter.cs` | Postfixes on `VehicleComputer.ApplyRequest` (claim, validate index, `RejectService` or queue) and `ISyscallHost.IssueResult` (answer getters); a postfix on the base `HostComputer.ApplyRequest` for the bench; `Pump()` stands in for the main-thread hook; `ConditionalWeakTable<VehicleComputer, …>`. |
| `KfsSim.cs` | Test-facing API: `KfsSim.Create(name, lights, deployables, actuators)`, `LoadKc`, `LoadKasm`, `Run(seconds, frameDt)` (per frame: `Pump()`, `Parts.Step(dt)`, `fake.Dispatch`), `RunTogether`, `Log`. |
| `Program.cs` | Resolver entry point and the scenario checks (every service, rescan, blink, light show, deploy, actuator sweep, rejections, kASM, two vehicles, read-back timing, bench). |
| `Experiments.cs` | The open questions: enum side effects, header contents, code size, cost, definition mistakes, loader messages, fallback. |

## Running

```sh
KSA_DLL_DIR=/path/to/ksa-game-assemblies/current/dll dotnet run
```

Exit code is non-zero if any `FAIL` line is printed. The generated artefacts need no game DLLs at run
time: `dotnet run -- --emit-header` (also `--emit-rows`, `--emit-enums`, `--emit-kinc`).

Environment of the recorded run: macOS arm64, .NET SDK 11.0.100 targeting `net10.0`, `Lib.Harmony`
2.4.2, KSA build 2026.11.25.5949 assemblies.

## Findings

**All 22 rows load as designed** (ids, new `enum:purr_*` keys added in the same augmentation,
`finite:>=0`, and `purr_light_set_color(light, rgb)` grouping its four cells as `[0,1,1,1]`). No
deviation from the API table was needed. Interpretations the table left open, chosen here:
`position`/`target` are reported as 0..1 of the travel (`duration_s` in seconds); getters never
reject (out-of-range index → 0, so `*_PRESENT` can probe), only setters do; `purr_light_set_on` on a
light without a switch is a host rejection; `purr_light_reset` restores appearance only (not on/off);
`PURR_STATE_*` and `PURR_LIGHT_TYPE_*` are header-only constants, not `enums.json` entries.

**Headers.** A `<…>` system header may contain a `static`, `static inline` or non-static function
with a body, an anonymous `enum {…}` and a named enum type, and may `#include <vecmat.h>`; all
compiled and ran. But: an unused `static`/`static inline` helper raises `KC-W006 'purr_deg' is never
used` in *every* program that includes the header; a non-static body included from two `.c` files is
`KC-E016` (defined twice). Prefer macros for helpers. Including `<purrtty.h>` without calling
anything adds 0 code bytes; the image grows 28 B only from debug info (file table) — identical
image size with `EmitDebug = false`.

**Property enumerations (revision 2).** One enumeration per part kind — `purr_light_prop`,
`purr_deploy_prop`, `purr_actuator_prop` — with integer properties at 0..15, float properties at 16..,
and `reservedN` fillers in the gap (no `#define`s for them). Each getter's selector domain is a
sub-range: `purr_light_geti` `enum:purr_light_prop[0..7]`, `purr_light_getf`
`enum:purr_light_prop[16..23]`, deploy `[0..5]`/`[16..18]`, actuator `[0..3]`/`[16..18]`. The
loader's sub-range syntax works for mod enums (same `GameTables.EnumDomain` path as stock
`enum:direction[2..21]`) and every row loads. Cross-use is now rejected at issue time and reads 0:
`rejected purr_light_geti argument 1 = 16: not in purr_light_prop[0..7]`,
`rejected purr_light_getf argument 1 = 2: not in purr_light_prop[16..23]`,
`rejected purr_light_geti argument 1 = 9: not in purr_light_prop[0..7]`,
`rejected purr_light_geti argument 1 = 99: not in purr_light_prop[0..7]`. The fillers exist only in
the enums.json data (`GameTables.EnumsData`); nothing in the game reads that data for display, and
they appear in no header, kASM constant set or service row.

**kASM constants.** `ServiceCatalog.KasmEquates()` generates `purrtty.kinc` (`.equ PURR_LIGHT_ON, 2`
…). Written into a Workbench workspace next to `lights.kasm`, `.include "purrtty.kinc"` resolves
through the Workbench's project-file lookup, the game's `Workbench.Build` assembles it, and the
program reads `PURR_LIGHT_ON` and switches the light. The alternative, a postfix on the
`BuiltinIncludes(GameTables, IoLayout, string)` constructor that adds the constants to
`Constants` when `file == "kfs.kinc"`, also works (tested, then unpatched).

**Enums.** Adding mod enums to `enums.json` changes nothing else: `<kfs.kinc>` only gains the 22
`SVC_PURR_*` symbols, `CatalogueEnums`, `vehicle.h`/`vehicle.kinc` and `kfs.h` are unchanged, and a
stock program compiles byte-identically. The enum constants are **not** visible to kASM
(`KA-E003 unknown symbol 'PURR_LIGHT_ON'`); a generated `.equ` block works, and so does a postfix on
the `BuiltinIncludes(GameTables, IoLayout, string)` constructor that adds them to `<kfs.kinc>`. An
image using a mod service loaded on stock tables is refused: `unknown service id 0x5013`.

**Read-back timing.** IssueResult reads the parts object mid-tick; setters are applied by the pump
before the next dispatch. A value set in tick k reads back in the **first tick of the next
dispatch**: k+1 when a frame runs at most one tick (frame 0.1 s or 0.05 s at 10 Hz; also 0.2 s,
where tick k was the last of its dispatch), k+2 at 0.3 s frames (tick k+1 ran in the same dispatch).
Programs must poll rather than assume a fixed delay.

**Cost** (Bench part, 50 Hz, budget 20,000 cycles/tick; WCET and measured cycles agree exactly): a
Light call with 0–2 scalar arguments costs 22–25 cycles, `set_cone` 25, `set_color` with a `vec3` 55,
a Medium call 40. By cycles ~900 Light calls fit a 50 Hz tick, but the VM caps a tick at 256 service
calls (all services, `kfs_logf` included) and drops the rest with `dropped 44 service request(s): a
tick may issue 256`; 256 Light calls ≈ 5,600 cycles (28 %).

**Bench.** No syscall host, so every mod getter returns 0. Unpatched, every mod request (getters
included) is rejected `no vehicle to act on`. A postfix on the base `HostComputer.ApplyRequest`
(`protected virtual`, body `return false`; `BenchMachine` doesn't override it, `VehicleComputer`
overrides it without calling base) patches cleanly: getters are claimed silently and setters log
`rejected purr_light_set_on: purr services need a vehicle (the bench has no parts)` and count in
`sys.rejected_requests` — produced from public members only (`Machine.CountRejectedRequest()`,
`Console.EventAllowed/Event`, `LastK`, `TickHz`), no reflection. Over the whole run the postfix fired
5 times, all on `BenchMachine`, never on a `VehicleComputer`.

**Mistakes that throw instead of diagnosing** (all `InvalidOperationException` out of
`Compiler.Compile`): prototype cells ≠ row cells (`purr_mx_short: 1 cells for 2 (kC §10.2
binding)`, also 4-for-1 with a `vec3`, and `int64_t` 2-for-1); a `vec3` result (`RET 0 with 2 cells
on the stack … (builder invariant)`); prototype returns a value but the row has none (`operand stack
below the floor …`); prototype `void` but the row has a result (`RET 0 with 1 cells on the stack …`).
A `float` parameter against a `u32` cell compiles silently and sends float bits. A builtin with no
row, or a row whose `kc` names another function, is a diagnostic (`KC-E000 … not implemented until
MS6`). Loader messages for bad rows are quoted in the output below.

## Output of the recorded run

```text
== catalog self-check (before touching the game)
PASS catalog self-check | 22 services, 7 enum groups
PASS LoadEmbedded patched before KfsIntegration.Tables was created

== tables: do the rows load?
INFO tables origin=embedded:KSA+purrtty services=42 service_table_version=1 enums=19
PASS augmented tables are in use (no fallback)
PASS every catalog row loaded | 22 rows
PASS loaded rows match the catalog (cells, results, name, cost)
INFO purr_light_set_color: argument names [light,rgb], cell->argument [0,1,1,1], domains [any,finite:>=0,finite:>=0,finite:>=0]
PASS vec3 argument groups as one kc name (light, rgb)
PASS mod enumerations are in enums.json | purr_parts_info[5], purr_part_kind[4], purr_light_prop[24], purr_deploy_prop[19], purr_actuator_prop[19]
INFO EnumDomain(enum:purr_light_prop[0..7]) = (purr_light_prop, 0, 7)
INFO EnumDomain(enum:purr_light_prop[16..23]) = (purr_light_prop, 16, 23)
INFO EnumDomain(enum:purr_deploy_prop[16..18]) = (purr_deploy_prop, 16, 18)
INFO EnumDomain(enum:purr_actuator_prop[0..3]) = (purr_actuator_prop, 0, 3)
INFO getter domains: purr_light_geti=enum:purr_light_prop[0..7], purr_light_getf=enum:purr_light_prop[16..23], purr_deploy_geti=enum:purr_deploy_prop[0..5], purr_deploy_getf=enum:purr_deploy_prop[16..18], purr_actuator_geti=enum:purr_actuator_prop[0..3], purr_actuator_getf=enum:purr_actuator_prop[16..18]
INFO purr_light_prop list: [present,has_switch,on,lit,type,part_id,switch_group,overridden,reserved8,reserved9,reserved10,reserved11,reserved12,reserved13,reserved14,reserved15,intensity,range_m,color_r,color_g,color_b,inner_rad,outer_rad,stock_intensity]

== every service from one kC program
INFO compiled: image 3556 B, WCET step 1144/100000, diagnostics 0
    k=0 loaded 3556 bytes on Bench: tick_hz 10, budget 100000 cycles, 1 task(s), layout 0x813232B4
    k=0 start: Running
    k=0 all info gen=1 dirty=0 lights=2 deploy=2 act=1
    k=0 all counts lights=2 deploy=2 act=1
    k=0 parts gen 2: 2 lights, 2 deployables, 1 actuators
    k=0 light 0: part 100 spot switch 0 off intensity 2 range 30 m
    k=0 light 1: part 101 spot switch none off intensity 2 range 30 m
    k=0 deployable 0: part 200 retracted 2 s
    k=0 deployable 1: part 201 retracted 2 s
    k=0 actuator 0: part 300 at 0 of 1 s
    k=2 all light0 present=1 switch=1 on=0 lit=0 type=0 part=100 group=0 ovr=0
    k=2 all light0 int=2.00 range=30.0 rgb=1.00,0.90,0.80 cone=0.30,0.60 stock=2.00
    k=4 all after-set on=1 ovr=1 int=3.00 range=40.0 r=0.10 inner=0.10
    k=6 all after-reset on=0 ovr=0 int=2.00 gen=2
    k=12 all deploy0 present=1 state=2 cmd=1 moving=1 part=200 links=1 pos=0.40 tgt=1.00
    k=12 all deploy1 state=2 cmd=1 dur=2.00
    k=12 all act0 present=1 moving=0 part=300 links=1 pos=0.50 tgt=0.50 dur=1.00
    k=12 all consts rejected=0 STATE_DEPLOYING=2 TYPE_POINT=1 KIND_ACTUATOR=3
PASS all 22 services reached ApplyRequest
PASS every result-bearing service was answered by IssueResult | 10 getters
PASS parts_info/counts read the sim
PASS rescan bumped the generation; describe wrote to the program log
PASS light getters (stock values)
PASS setters visible two ticks later
PASS reset + set_all
PASS deployables and actuator getters
PASS header constants usable in expressions

== rescan: structure changes stay invisible until purr_parts_rescan
PASS dirty and old count before, new generation and count after | rescan k=0 gen=1 dirty=1 lights=2 | rescan k=3 gen=2 dirty=0 lights=3

== blink: light 0 toggles every 5 ticks
INFO frame-by-frame light0 011111000001111100000111110000
PASS light 0 on/off sequence | expected 011111000001111100000111110000
PASS light 2 (same switch group) follows light 0
PASS light 1 (own switch) untouched

== light show: colour/cone/intensity on light 1, read back, clamp, reset
    k=0 loaded 2348 bytes on Bench: tick_hz 10, budget 100000 cycles, 1 task(s), layout 0x813232B4
    k=0 start: Running
    k=3 show set rgb=1.000,0.500,0.250 cone=0.200,0.600 int=5.000 range=25.000 ovr=1
    k=6 show clamp rgb=1.000,0.000,0.000 cone=1.5697963,1.5697963
    k=9 show reset rgb=1.000,0.900,0.800 cone=0.300,0.600 int=2.000 range=30.000 ovr=0
PASS sim state after the overrides
PASS logged read-back of the overrides
PASS host clamps colour to [0,1] and the cone to outer<=1.5697963, inner<=outer
PASS reset restores stock values (sim)
PASS reset restores stock values (logged)
PASS other lights untouched

== deploy: purr_deploy_set(0, 1) and poll the state
    k=0 loaded 1228 bytes on Bench: tick_hz 10, budget 100000 cycles, 1 task(s), layout 0x813232B4
    k=0 start: Running
    k=0 deploy k=0 state=1 pos=0.00
    k=2 deploy k=2 state=2 pos=0.05
    k=21 deploy k=21 state=0 pos=1.00
    k=21 deployed at tick 21
PASS program saw retracted -> deploying -> deployed | k0:1 k2:2 k21:0
PASS command tick -> deployed tick = duration (20 ticks at 10 Hz) | 20 ticks
PASS sim: applied -> deployed took exactly duration simulated seconds | applied 0.1s, history 2.1s:Deployed

== actuator sweep: triangle wave on actuator 0
INFO logged: k0:0.00/0.00 k5:0.25/0.20 k10:0.50/0.45 k15:0.75/0.70 k20:1.00/0.95 k25:0.75/0.80 k30:0.50/0.55 k35:0.25/0.30 k40:0.00/0.05 k45:0.25/0.20 k50:0.50/0.45 k55:0.75/0.70 ...
PASS position read in tick k equals the command of tick k-1 | 16 samples, max |pos - cmd(k-1)| = 0.0000
PASS sim position reaches each goal within the frame (wave slope 0.5/s < slew 1/s) | max 0.000000

== rejections: index, enum selector, NaN, negative, no switch
    k=0 loaded 1964 bytes on Bench: tick_hz 10, budget 100000 cycles, 1 task(s), layout 0x813232B4
    k=0 start: Running
    k=1 rejected purr_light_set_on: light 99 out of range (this vehicle has 2)
    k=1 rejected purr_deploy_set: deployable 7 out of range (this vehicle has 1)
    k=1 rejected purr_actuator_set: actuator 5 out of range (this vehicle has 1)
    k=2 rej range before=0 after=3
    k=2 rej enum geti(INTENSITY)=0 getf(ON)=0.000 geti(9)=0 geti(99)=0
    k=2 rej enum same-tick delta=4 (controls: geti(ON)=0 getf(INTENSITY)=2.000 getf(9)=0.000)
    k=2 rejected purr_light_geti argument 1 = 16: not in purr_light_prop[0..7]
    k=2 rejected purr_light_getf argument 1 = 2: not in purr_light_prop[16..23]
    k=2 rejected purr_light_geti argument 1 = 9: not in purr_light_prop[0..7]
    k=2 rejected purr_light_geti argument 1 = 99: not in purr_light_prop[0..7]
    k=2 rejected purr_light_getf argument 1 = 9: not in purr_light_prop[16..23]
    k=3 rejected purr_light_set_on: light 1 has no switch
    k=3 rej nan/neg same-tick delta=2
    k=3 rejected purr_actuator_set argument 1 = nan: not finite
    k=3 rejected purr_light_set_intensity argument 1 = -1: below 0
    k=4 rej next-tick delta=3 probe=0
PASS host rejection text for out-of-range indices
PASS sys.rejected_requests counts host rejections (visible next tick) | rej range before=0 after=3
INFO selector rejection: rejected purr_light_geti argument 1 = 16: not in purr_light_prop[0..7]
INFO selector rejection: rejected purr_light_getf argument 1 = 2: not in purr_light_prop[16..23]
INFO selector rejection: rejected purr_light_geti argument 1 = 9: not in purr_light_prop[0..7]
INFO selector rejection: rejected purr_light_geti argument 1 = 99: not in purr_light_prop[0..7]
INFO selector rejection: rejected purr_light_getf argument 1 = 9: not in purr_light_prop[16..23]
PASS geti(PURR_LIGHT_INTENSITY) rejected at issue by the [0..7] sub-range
PASS getf(PURR_LIGHT_ON) rejected at issue by the [16..23] sub-range
PASS filler 9 and out-of-list 99 rejected by geti; filler 9 by getf
PASS rejected selectors read 0, valid ones still work, all counted in the same tick
PASS NaN to purr_actuator_set rejected at issue (finite) | rejected purr_actuator_set argument 1 = nan: not finite
PASS negative intensity rejected at issue (finite:>=0) | rejected purr_light_set_intensity argument 1 = -1: below 0
PASS light without a switch rejected by the host
PASS issue-time rejections count in the same tick; host rejection next tick
PASS rejected requests never reached the parts | applied: 

== kASM: SYSCALL SVC_PURR_LIGHT_SET_ON
PASS kASM SYSCALL reached the sim: light 1 on, light 0 off |     k=0 loaded 692 bytes on Bench: tick_hz 10, budget 100000 cycles, 1 task(s), layout 0x813232B4
    k=0 start: Running

== same kC on two vehicles with different light counts
PASS vehicle A logs 2 lights, vehicle B logs 5
PASS each vehicle switched its own last light

== read-back timing: set in tick 2, read every tick
INFO frame dt 0.10s (ticks per dispatch 11111111...): set in tick 2, first on=1 in tick 3 -> 1 tick(s) later; log: rb k=2 on=0 | rb k=3 on=1 | rb k=4 on=1 | rb k=5 on=1 | rb k=6 on=1
PASS one tick per frame: read-back in tick k+1
INFO frame dt 0.05s (ticks per dispatch 10101010...): set in tick 2, first on=1 in tick 3 -> 1 tick(s) later; log: rb k=2 on=0 | rb k=3 on=1 | rb k=4 on=1 | rb k=5 on=1 | rb k=6 on=1
PASS frames faster than ticks: still tick k+1
INFO frame dt 0.20s (ticks per dispatch 122222...): set in tick 2, first on=1 in tick 3 -> 1 tick(s) later; log: rb k=2 on=0 | rb k=3 on=1 | rb k=4 on=1 | rb k=5 on=1 | rb k=6 on=1
INFO frame dt 0.30s (ticks per dispatch 1333...): set in tick 2, first on=1 in tick 4 -> 2 tick(s) later; log: rb k=2 on=0 | rb k=3 on=0 | rb k=4 on=1 | rb k=5 on=1 | rb k=6 on=1

== bench (BenchMachine, no vehicle) with the HostComputer.ApplyRequest postfix
PASS compiles against the bench layout
    bench: loaded 1072 bytes on Bench: tick_hz 10, budget 100000 cycles, 1 task(s), layout 0x070A6447
    bench: start: Running
    bench: bench count=0 present=0 intensity=0.00
    bench: rejected purr_light_set_on: purr services need a vehicle (the bench has no parts)
    bench: rejected purr_parts_rescan: purr services need a vehicle (the bench has no parts)
    bench: bench rejected_requests=2
PASS bench: results are zero | load True, ticks 3
PASS bench: getters claimed silently, setters rejected with the purr text
PASS bench: the rejections count in sys.rejected_requests
PASS bench postfix fired on BenchMachine | 5 calls (4 getters/setters + 1 rescan)

== mod enums: side effects on stock headers and kASM symbols
INFO <kfs.kinc> constants: stock 100, augmented 122; added 22 [SVC_PURR_ACTUATOR_COUNT,SVC_PURR_ACTUATOR_GETF,SVC_PURR_ACTUATOR_GETI,SVC_PURR_ACTUATOR_SET,...]; removed 0; changed 0
PASS <kfs.kinc> only gains the 22 SVC_PURR_* symbols
PASS mod enum constants are NOT in <kfs.kinc>
PASS stock kfs.h text unchanged
PASS generated vehicle.h / vehicle.kinc identical with and without the mod | vehicle.h 5914 chars
PASS CatalogueEnums (the enum constants kASM gets) unchanged | 46 constants
PASS a stock-only program compiles byte-identically against stock and augmented tables | 712 B
INFO image using a purr service, loaded on a computer with STOCK tables: ok=False rule=syscall message=SYSCALL at 0x8: unknown service id 0x5013
PASS an image that uses a mod service is refused cleanly without the mod
INFO kASM 'PUSH8 PURR_LIGHT_ON' with only <kfs.kinc>: ok=False program.kasm:7:1: error KA-E003: unknown symbol 'PURR_LIGHT_ON'
  hint: `.include` the file that defines it, or `.extern` it.
PASS kASM does not see the header's PURR_* constants automatically
PASS kASM works with the catalog's generated .equ block
PASS a postfix on the BuiltinIncludes constructor puts PURR_* into <kfs.kinc>

== where do the reserved filler entries show up?
INFO filler visible in enums.json data (GameTables.EnumsData)   YES
INFO filler visible in <purrtty.h>                              no
INFO filler visible in generated purrtty.kinc                   no
INFO filler visible in <kfs.kinc> constants                     no
INFO filler visible in CatalogueEnums                           no
INFO filler visible in vehicle.h / vehicle.kinc                 no
INFO filler visible in services rows (domains, prose)           no
PASS fillers appear only in the enums.json data

== kASM: purrtty.kinc as a workspace file, built by the game's Workbench
INFO purrtty.kinc head: ; purrtty.kinc - generated from the purrTTY service catalog / .equ PURR_PARTS_GENERATION, 0 / .equ PURR_PARTS_DIRTY, 1
PASS Workbench assembles lights.kasm including "purrtty.kinc"
PASS the kASM program used PURR_LIGHT_ON and switched light 0 on

== what can a system header contain?
PASS header: static function with a body | diagnostics: none; ran: deg=57.296
PASS header: static inline function | diagnostics: none; ran: deg=57.296
PASS header: non-static function with a body | diagnostics: none; ran: deg=57.296
PASS header: anonymous enum | diagnostics: none; ran: deg=57.296 on=0
PASS header: named enum type | diagnostics: none; ran: deg=57.296 on=0
PASS header: header that includes <vecmat.h> | diagnostics: none; ran: deg=57.296 | light0 rgb=(0.5,0.25,0.125)
INFO static header function included from two .c files -> OK (none)
INFO static inline header function included from two .c files -> OK (none)
INFO non-static header function included from two .c files -> error: <purr_exp.h>:3:7: error KC-E016: 'purr_deg' is defined in '<purr_exp.h>' and in '<purr_exp.h>'
  hint: The first definition is at <purr_exp.h>:3.
INFO size <kfs.h> only                     image 552 B (no debug info 244 B), code 19 B, diagnostics: none
INFO size + <purrtty.h>                    image 580 B (no debug info 244 B), code 19 B, diagnostics: none
INFO size + unused static helper           image 608 B (no debug info 244 B), code 19 B, diagnostics: <purr_exp.h>:3:14: warning KC-W006: 'purr_deg' is never used
  hint: Remove it, or cast to `(void)` if it is kept on purpose.
INFO size + unused static inline helper    image 608 B (no debug info 244 B), code 19 B, diagnostics: <purr_exp.h>:3:21: warning KC-W006: 'purr_deg' is never used
  hint: Remove it, or cast to `(void)` if it is kept on purpose.
INFO size + #include <vecmat.h> (unused)   image 728 B (no debug info 244 B), code 19 B, diagnostics: none
PASS including <purrtty.h> without calling anything adds no code and, without debug info, no bytes

== cost per service call (50 Hz task)
INFO empty task                       WCET    12 (+   0)  run    12 (+   0)  budget 20000
INFO 1x purr_light_set_on             WCET    34 (+  22)  run    34 (+  22)  budget 20000
INFO 10x purr_light_set_on            WCET   232 (+ 220)  run   232 (+ 220)  budget 20000
INFO g = purr_light_count()           WCET    34 (+  22)  run    34 (+  22)  budget 20000
INFO g = purr_light_geti(0, ON)       WCET    36 (+  24)  run    36 (+  24)  budget 20000
INFO purr_light_set_color(0, c)       WCET    67 (+  55)  run    67 (+  55)  budget 20000
INFO purr_light_set_cone(0, .1, .2)   WCET    37 (+  25)  run    37 (+  25)  budget 20000
INFO purr_parts_rescan() [Medium]     WCET    52 (+  40)  run    52 (+  40)  budget 20000
INFO 300 calls in a loop: WCET 12324/20000; log: dropped 44 service request(s): a tick may issue 256 | dropped 44 service request(s): a tick may issue 256
PASS calls beyond 256 per tick are dropped by the VM with an event
INFO 50 Hz budget on the bench part: 20000 cycles per tick

== definition mistakes: diagnostic or compiler exception?
INFO prototype 1 cell, row 2 cells                              -> EXCEPTION InvalidOperationException: purr_mx_short: 1 cells for 2 (kC §10.2 binding)
INFO prototype 4 cells (u32+vec3), row 1 cell                   -> EXCEPTION InvalidOperationException: purr_mx_long: 4 cells for 1 (kC §10.2 binding)
INFO vec3 result, row 3 result cells                            -> EXCEPTION InvalidOperationException: RET 0 with 2 cells on the stack in 't' (builder invariant)
INFO prototype returns uint32_t, row has no result (value used) -> EXCEPTION InvalidOperationException: operand stack below the floor after an instruction in 't' (builder invariant)
INFO prototype returns void, row has a u32 result               -> EXCEPTION InvalidOperationException: RET 0 with 1 cells on the stack in 't' (builder invariant)
INFO prototype float, row u32 (same cell count)                 -> compiles OK (none)
INFO prototype int64_t (2 cells), row u32                       -> EXCEPTION InvalidOperationException: purr_mx_i64: 2 cells for 1 (kC §10.2 binding)
INFO kc column names a different function                       -> diagnostic: main.c:7:18: error KC-E000: 'purr_mx_named' (the service bindings of kC §10.2, §9.5, §10.3) is not implemented until MS6
  hint: This is a milestone limit of the current kFS build, not a kC rule.
INFO builtin + prototype, no row at all                         -> diagnostic: main.c:7:18: error KC-E000: 'purr_mx_norow' (the service bindings of kC §10.2, §9.5, §10.3) is not implemented until MS6
  hint: This is a milestone limit of the current kFS build, not a kC rule.

== loader messages for bad rows (what the fallback would print)
INFO enum domain naming an unknown enumeration                  -> GameTablesException: services.json: service 'purr_lx1' argument 0: domain 'enum:purr_nope' doesn't fit type 'u32' (kVM §4.8)
INFO finite:>=0 on a u32 cell                                   -> GameTablesException: services.json: service 'purr_lx2' argument 0: domain 'finite:>=0' doesn't fit type 'u32' (kVM §4.8)
INFO vec3 cells with different domains (kc names can't group)   -> GameTablesException: services.json: service 'purr_lx3': the kC column's 2 arguments don't group its 4 arg_types entries (D-118 (4))
INFO duplicate id                                               -> GameTablesException: services.json: id 20480 listed twice
INFO empty summary                                              -> GameTablesException: services.json: service 'purr_lx5' is incomplete
INFO unknown cost class                                         -> GameTablesException: services.json: service 'purr_lx6' is incomplete
INFO unknown key in the row                                     -> GameTablesException: services.json: The JSON property 'note' could not be mapped to any .NET member contained in type 'KSA.Kfs.Vm.Tables.ServiceRow'.
INFO enum sub-range enum:purr_light_prop[2..3] (should load)    -> loads (43 services)

== augmentation failure keeps the stock tables
[purrtty] kFS table augmentation failed, keeping stock tables: GameTablesException: services.json: service 'trace' is incomplete
PASS a failing augmentation returns usable stock tables | origin embedded:KSA, reason: GameTablesException: services.json: service 'trace' is incomplete

== base-method postfix vs VehicleComputer
INFO HostComputer.ApplyRequest postfix calls by computer type: BenchMachine=5
PASS the base postfix never fired for a VehicleComputer (no double handling)

== router faults
PASS no exception escaped into a patch body

76 passed, 0 failed
```
