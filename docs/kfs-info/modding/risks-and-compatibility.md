---
type: Concept
title: Risks and compatibility rules for kFS mods
description: The invariants a kFS-extending mod must keep, the ways it can break the player's game or programs, how each failure shows up, and what to re-check when the game updates.
resource: ksa-decomp:KSA.Kfs/KfsDispatch.cs
tags: [kfs, modding, risk, compatibility, threading, upgrade]
sources:
  - id: kfs-dispatch
    resource: ksa-decomp:KSA.Kfs/KfsDispatch.cs
    title: KfsDispatch (decompiled)
  - id: kfs-integration
    resource: ksa-decomp:KSA.Kfs/KfsIntegration.cs
    title: KfsIntegration (decompiled)
  - id: kfs-prototype
    resource: ksa-decomp:KSA.Kfs/KfsPrototype.cs
    title: KfsPrototype (decompiled)
  - id: image-reader
    resource: ksa-decomp:KSA.Kfs.Vm.Image/ImageReader.cs
    title: ImageReader (decompiled)
  - id: game-tables
    resource: ksa-decomp:KSA.Kfs.Vm.Tables/GameTables.cs
    title: GameTables (decompiled)
  - id: offline-harness
    resource: /modding/offline-harness.md
    title: Offline harness experiments
generated: { by: claude-code/claude-fable-5-2, at: 2026-11-25T22:02:02Z }
status: stable
ksa_build: "2026.11.25.5949"
---

# The situation

kFS in this build is a prototype: the flag class is literally named `KfsPrototype`, sleeping,
events, kScript, non-volatile memory and per-part processors are stubs, and the data tables cite
milestones ("MS7", "MS9") still to come.[^kfs-prototype] Nothing is versioned as an API. A mod that
extends it should expect the internals to move and be built so that when they do, **the mod fails
soft and kFS keeps working**.

# Invariants

## Never change what stock programs depend on

| Do not | Because |
| --- | --- |
| Bump `version` in `services.json` or `messages.json` | Both are written into every image and compared at load; all existing images would be rejected.[^image-reader] |
| Change, remove or renumber a stock service, message, enum value or opcode | Images and sources encode them. |
| Change a vehicle's generated I/O layout | The layout CRC is in every image; every program for that vehicle would need rebuilding, and command writes require full control. |
| Replace the `kfs.h` entry of `SystemHeaders.Files` | Stock programs and examples include it; add a separate header instead. |
| Reorder an `enums.json` list | Values are positions. |

Adding rows is safe in both directions: stock images load unchanged with the mod present, and an
image that uses a mod service fails cleanly at load without the mod ("unknown service id").

## Never throw into kFS

| Where mod code runs | What an escaping exception does |
| --- | --- |
| Inside a dispatch (`ApplyRequest`, `IssueResult`, anything under `KfsVehicleHost.Dispatch`) | Caught by `KfsDispatch.TryComputeControl`: the vehicle's computer is **halted with a sticky host fault** and the stock flight computer takes over mid-flight.[^kfs-dispatch] |
| In `GameTables.LoadEmbedded` | The lazy initialiser leaves the tables null and rethrows on every access: **kFS is unusable** for the session.[^kfs-integration] |
| In a `BeforeInputEvents` postfix | Nothing in kFS catches it; it propagates into the game's frame preparation. |
| In the kC compiler (mismatched prototype, multi-cell result) | A build fails with an internal error instead of a diagnostic.[^offline-harness] |

Wrap every entry point in `try`/`catch`, log once, and degrade: reject the request, return zero,
keep the stock tables.

## Never write the game from a worker thread

The dispatch runs on solver and `VehicleWorker` threads while the main thread draws UI and
renders, and vehicles in different physics bubbles dispatch in parallel. Mod code there must:

- treat the live vehicle as read-only, and limit reads to plain field reads of the kind stock kFS
  itself performs;
- keep static state thread-safe and per-computer state confined to that computer's dispatch;
- take no blocking locks, allocate nothing per call after warm-up, and stay short — its time is
  charged to the program's 4 ms frame budget and can make the program skip ticks;
- push every mutation to a queue drained in the main-thread sync window.

See [dispatch and threading](/integration/dispatch-and-threading.md).

## Validate every number

The game does not validate what ends up in its fields. A NaN animation goal never settles and pins
the vehicle off rails; an inverted cone is silently swapped by the renderer. Declare `finite…`
domains so the VM rejects non-finite arguments, and clamp in the handler. See
[control recipes](/game-targets/control-recipes.md).

# Failure modes and how they show up

| Failure | Symptom | Defence |
| --- | --- | --- |
| Augmented table fails validation (for example a future stock row takes one of the mod's ids) | Mod's `GameTables.Load` throws inside the postfix | Catch, log, return stock tables, disable the mod's services |
| Patch target renamed or reshaped | `AccessTools` returns null / Harmony throws on apply | Apply each patch independently; if any of the set is missing, do not install the header or the rows |
| Tables already built when the mod patches | Rows absent everywhere | Check the private static `_tables` at patch time; log; do not half-enable |
| Header declares a service whose row is missing | kC compiles the call as an ordinary undefined function (`KC-E304`) | Install header and builtins only after the augmented tables were actually produced |
| Program built with the mod, run without it | Load rejected: unknown service id | Expected; document for players |
| Program calls a mod service on the bench | "rejected …: no vehicle to act on"; results 0 | Expected; document |
| Indices shift after docking or a reload | A program addresses the wrong light | Frozen, `InstanceId`-ordered lists with a generation counter; never persist indices |
| Mod state for a dead host | Leak, or writes to a disposed vehicle | Key state on the host object; sweep on `IsAlive`; check the module is still in the vehicle's tree before every write |
| A method the mod patches gets inlined at its call site | Patch silently never fires | Prefer large, non-trivial targets; self-test each hook once and report |

# When the game updates

Re-run the offline harness first: it loads the real tables, compiles real kC and exercises the
host hooks, so most breakage appears as a failing test.[^offline-harness] Then check, in order:

1. The eight touch points between the game and kFS — [source map](/overview/source-map.md).
2. `GameTables.LoadEmbedded` / `Load(Func<string, Stream?>, string)` signatures and the service-row
   schema (`ServiceRow`, `BuildServices`, `CheckDomains`).[^game-tables]
3. `SystemHeaders.Files` / `Builtins` still public, static and consulted live; the parser still
   binds on the `kc` column.
4. `VehicleComputer.ApplyRequest` and its explicit `ISyscallHost.IssueResult`; the fall-through
   rejection in `HostComputer`.
5. `KfsIntegration.BeforeInputEvents` still called once per frame in the solver-idle window.
6. Stock service ids — has a new stock row landed in the mod's block?
7. The game members the feature drives (`Part.LightSwitch`, `PowerConsumer.LightIsActive`,
   `LightModule.Template` and its fields, `KeyframeAnimationModule.TimeGoal`, `Shared.Duration`,
   `ShowDeployRetract`).
8. Whether stock kFS has gained the feature itself. When it does, retire the mod's version rather
   than compete with it.

The repository's `upgrade-ksa` skill and `docs/ksa-*-compatibility.md` reviews are the place to
record the outcome.

# What has and has not been verified

| Claim | Status |
| --- | --- |
| Table augmentation, kC header and builtin binding, kASM symbols, argument-domain enforcement, request interception, synchronous results, the workbench path | Executed offline against the real `KSA.dll` |
| Thread model, frame order, lifecycle, load checks | Read from decompiled code |
| Light and animation writes | Read from decompiled code; the same writes are made in game by gatOS and zippo |
| Harmony behaviour under the game's runtime (inlining, patch timing relative to StarMap load) | **Not verified** — needs an in-game run |
| Any of it in a running game with this mod | **Not verified** |

# Modding notes

- Ship the kFS extension behind its own switch, independent of the terminal features, so a kFS
  break in a game update can be turned off without losing the rest of the mod.
- Log one clear line at start-up stating which hooks applied and whether the services are live;
  it is the first thing to ask a player for.

[^kfs-prototype]: KfsPrototype.cs:6–28.
[^kfs-dispatch]: KfsDispatch.cs:19–27.
[^kfs-integration]: KfsIntegration.cs:89.
[^image-reader]: ImageReader.cs:642–649 (table versions), :773–780 (part and layout fingerprint), :557–560 (unknown service id).
[^game-tables]: GameTables.cs:75–126, :538–690.
[^offline-harness]: Experiments E3, E8 and E10, and the "what the harness cannot show" list.
