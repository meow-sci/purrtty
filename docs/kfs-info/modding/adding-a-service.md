---
type: How-To
title: Adding a service to kFS from a mod
description: The complete, tested recipe for a mod-defined kFS service — the table row and its validation rules, the kC header and builtin registration, the two host hooks for requests and results, the threading rules for acting on the game, and the constraints that will otherwise bite.
resource: ksa-decomp:KSA.Kfs.Vm.Tables/GameTables.cs
tags: [kfs, modding, services, harmony, how-to, extension]
sources:
  - id: game-tables
    resource: ksa-decomp:KSA.Kfs.Vm.Tables/GameTables.cs
    title: GameTables (decompiled)
  - id: arg-domains
    resource: ksa-decomp:KSA.Kfs.Vm/ArgDomains.cs
    title: ArgDomains (decompiled)
  - id: costs-json
    resource: ksa-decomp:KSA.Kfs.Vm.Tables.Data.costs.json
    title: Embedded cost table
  - id: system-headers
    resource: ksa-decomp:KSA.Kfs.KC/SystemHeaders.cs
    title: SystemHeaders (decompiled)
  - id: parser
    resource: ksa-decomp:KSA.Kfs.KC/Parser.cs
    title: kC Parser (decompiled)
  - id: host-computer
    resource: ksa-decomp:KSA.Kfs.Host/HostComputer.cs
    title: HostComputer (decompiled)
  - id: vehicle-computer
    resource: ksa-decomp:KSA.Kfs.Host/VehicleComputer.cs
    title: VehicleComputer (decompiled)
  - id: kfs-integration
    resource: ksa-decomp:KSA.Kfs/KfsIntegration.cs
    title: KfsIntegration (decompiled)
  - id: kfs-vehicle-host
    resource: ksa-decomp:KSA.Kfs/KfsVehicleHost.cs
    title: KfsVehicleHost (decompiled)
  - id: offline-harness
    resource: /modding/offline-harness.md
    title: Offline harness experiments E1–E12
  - id: builtin-includes
    resource: ksa-decomp:KSA.Kfs.KAsm/BuiltinIncludes.cs
    title: BuiltinIncludes (decompiled)
  - id: editor-model
    resource: ksa-decomp:KSA.Kfs.Host.Workbench/EditorModel.cs
    title: EditorModel (decompiled)
  - id: kc-front-end
    resource: ksa-decomp:KSA.Kfs.KC/KcFrontEnd.cs
    title: KcFrontEnd (decompiled)
  - id: dispatch-and-threading
    resource: /integration/dispatch-and-threading.md
    title: kFS dispatch and threading
generated: { by: claude-code/claude-fable-5-2, at: 2026-11-26T06:11:10Z }
status: stable
ksa_build: "2026.11.25.5949"
---

# The shape of the solution

A service call travels: kC call → `SYSCALL <id>` in the image → VM validates the argument cells →
(optional) host supplies result cells **synchronously** → request queued → after the tick the host
routes the request by id → game glue applies it. Everything up to the routing is data-driven from
the services table; the routing is three hard-coded `switch` statements. So a mod needs:

| # | Piece | Mechanism | Patch? |
| --- | --- | --- | --- |
| 1 | A row in the services table | Augment `services.json` as the loader reads it | Harmony postfix on `GameTables.LoadEmbedded` |
| 2 | A kC prototype | Add a header string to `SystemHeaders.Files` | none (public static dictionary) |
| 3 | The builtin name | Add to `SystemHeaders.Builtins` | none (public static set) |
| 4 | Request routing | Claim the id where the stock switch falls through | Harmony postfix on `VehicleComputer.ApplyRequest` |
| 5 | Results (only for a result-bearing service) | Fill the result cell | Harmony postfix on `VehicleComputer`'s `ISyscallHost.IssueResult` |
| 6 | Game effect | Queue work and apply it on the main thread | Harmony postfix on `KfsIntegration.BeforeInputEvents` |

Pieces 1–5 were exercised end to end outside the game; see
[the offline harness](/modding/offline-harness.md) for the working code and output.[^offline-harness]
Piece 6 follows the stock pattern. purrTTY's kFS extension in this repository implements all six
(`purrTTY.Kfs/`); its queue-and-apply step runs in the offline tests, driven by the test's own
frame loop, but the `BeforeInputEvents` postfix itself is called only by the game. **No piece has
been run in the game.**

kASM needs nothing beyond piece 1: `SYSCALL SVC_<SYMBOL>` resolves from the row's `symbol`,
because the game generates `<kfs.kinc>` from the service rows on every assembly.[^builtin-includes]
Selector constants are another matter: kASM sees only the stock enumeration constants, so a mod
supplies its own as a `.kinc` file of equates. That file holds the mod's constants **only**; the
`SVC_*` symbols of the mod's services are already in `<kfs.kinc>` and must not be repeated in it.

# 1 — The table row

## Injecting it

`KfsIntegration.Tables` is created on first use by `GameTables.LoadEmbedded()`; the game does not
touch it during start-up.[^kfs-integration] A postfix on the public static `LoadEmbedded` that
replaces `__result` with tables built by the public `GameTables.Load(Func<string, Stream?> open, string origin)`
gives the **whole toolchain one augmented instance** — compiler, assembler, VM, cost model, image
loader, reference window:

```csharp
static bool _inside;
static void Postfix(ref GameTables __result)
{
    if (_inside) return;                       // GameTables.Load is not LoadEmbedded, but be defensive
    _inside = true;
    try
    {
        var asm = typeof(GameTables).Assembly;
        __result = GameTables.Load(file =>
        {
            Stream? stock = asm.GetManifestResourceStream(GameTables.RESOURCE_PREFIX + file);
            return file == "services.json" && stock != null ? Augment(stock) : stock;
        }, "embedded:KSA+mymod");
    }
    catch (Exception e) { Log(e); /* keep the stock tables: kFS must still work */ }
    finally { _inside = false; }
}
```

An alternative that needs no patch: build the tables the same way and assign them to the private
static `KfsIntegration._tables` by reflection while it is still null —
`LazyInitializer.EnsureInitialized` returns an existing value untouched, so the game's loader then
never runs. It trades a patch on a public method for a dependency on a private field name, and it
lets a mod detect that the tables were already built by someone else. The experiments ran the
postfix; the assignment was executed later, offline, by purrTTY's installer
(`purrTTY.Kfs/Tables/TablesPublisher.cs`, with the postfix kept as a fallback for a renamed field;
tests in `purrTTY.Kfs.Tests/Installer/`): after the assignment `KfsIntegration.Tables` returns the
assigned instance. `Unverified:` in the running game, where the question is whether anything reads
the tables before a mod's code runs.

Rules:

- **Apply the patch before the first read of `KfsIntegration.Tables`.** After that the instance is
  cached in a private static and already captured by the `Workbench`, every `VehicleComputer` and
  every `KvmMachine`. Mutating the loaded instance in place is possible but leaves stale caches
  (the kC parser's name bindings, each machine's argument-domain cache, generated header text) and
  races the dispatch threads — do not.
- **Catch a failed augmentation and return the stock tables.** If `LoadEmbedded` throws, the lazy
  initialiser leaves the field null and every later access rethrows, so kFS becomes unusable.
- **Do not change `version`** in `services.json` (or in `messages.json`). Those two numbers are
  written into every image and compared at load; bumping them invalidates all existing images.
  Left alone, stock images keep loading with the mod, and images that use a mod id fail cleanly
  without it ("unknown service id").
- The JSON reader rejects unknown keys, comments and trailing commas, and requires constructor
  parameters — see [the tables loader](/tables/game-tables-loader.md).

## What a row must contain

A complete row, as accepted by the loader:

```json
{ "id": 20498, "name": "purr_light_set_on",
  "kc": "`purr_light_set_on(light, on)`", "kscript": "—", "symbol": "SVC_PURR_LIGHT_SET_ON",
  "kind": "request",
  "args": "`u32 light`, `u32 on`",          "arg_types": ["u32", "u32"],
  "results": "—",                            "result_types": [],
  "cost_class": "Light", "requires_stock": null,
  "arg_domains": ["any", "any"], "host_checks": "light index out of range",
  "summary": "Switches a light on or off." }
```

| Field | Rule | Source |
| --- | --- | --- |
| `id` | 0–65535 and unused. Stock uses 1–4, 16, 17, 32, 48–57, 64–67. A duplicate throws "id N listed twice". | [^game-tables] |
| `kind` | `request` or `request+result`. (`query` passes the loader but traps `BAD_SYSCALL` at run time.) | [^game-tables] |
| `arg_types`, `result_types` | Both required (use `[]`). Cell types: `i32`, `u32`, `f32`, `ptr`, `cell` (1 cell each), `i64`, `f64` (2 cells). | [^game-tables] |
| `cost_class` | Must be one of the cost table's classes: `Light` (10 cycles, 20 on K8), `Medium` (30/60), `Heavy` (100/200). | [^costs-json] |
| `symbol` | Required. Becomes the kASM symbol. | [^game-tables] |
| `summary` | Required, non-blank. Shown in the reference window. | [^game-tables] |
| `arg_domains` | Required, exactly one entry per `arg_types` entry, each fitting its type (table below). | [^game-tables] |
| `host_checks` | Required (any string; `"—"` is what stock uses for none). | [^game-tables] |
| `kc` | The kC spelling in backticks. The text before `(` is the name the parser binds; the names inside `()` must group onto the argument cells (below). | [^game-tables][^parser] |
| `requires_stock` | Leave `null` unless the service steers the stock flight computer; a non-null value makes kC reject the call in a program that claims `ALLOCATION`. | [kC builtins](/language/kc-builtins-and-intrinsics.md) |
| `results` | Prose shown, with `kc` and `summary`, on the reference window's Services page. | [services table](/tables/services-table.md) |
| `kscript`, `args` | Carried in the row but displayed nowhere in this build; `args` is required by the row type. | [services table](/tables/services-table.md) |

An incomplete row throws `services.json: service '<name>' is incomplete`.

## Argument domains

The VM checks every argument cell against its domain when the call is issued; a failure produces a
readable event (`rejected <name> argument N = <value>: <reason>`) and the request never reaches
the host.

| Domain | Type | Check at issue |
| --- | --- | --- |
| `any` | any | none |
| `finite` | `f32` | not NaN or infinite |
| `finite:>=0` | `f32` | finite and ≥ 0 |
| `finite:[lo,hi]` | `f32` | finite and inside the closed range |
| `enum:<name>` or `enum:<name>[a..b]` | `u32` | value within the named enumeration of `enums.json` (or the sub-range) |
| `count`, `format` | `u32` | **none** at issue time (the loader only checks the type); the `log` service validates its format index separately, as a trap |
| `handle` | `u32` | non-zero (burn handles) |
| `channel` | `u32` | index of a string-table entry declared as a channel |
| `event`, `event_param`, `future` | `u32`, `cell`, `i64` | sleep-service specifics |
| `ptr` | `ptr` | **none** — a handler that dereferences it must range-check |
| `quat`, `vec_nonzero` | `f32` | look **ahead** at the following cells — a misplaced one can index past the argument array and throw |

Guidance: use `finite` (or a range) on every float so a NaN can never reach a game field; use
`any` for indices and validate them in the handler, where the vehicle is known; avoid `quat` and
`vec_nonzero` unless reproducing their exact stock arrangement.[^arg-domains] A new enumeration
key added to `enums.json` by the same augmentation is immediately usable as `enum:<key>`.

## How `kc` argument names group onto cells

If the `kc` signature has as many names as `arg_types` has entries (or none), the mapping is one
to one. If it has fewer, **consecutive cells with the same `arg_domains` string are merged, left
to right**, until the counts match; if they cannot match the loader throws.[^game-tables] So
`f(light, rgb)` over `["u32","f32","f32","f32"]` with domains `["any","finite","finite","finite"]`
groups as `light` + `rgb`. The grouping only affects how a rejected argument is described, but a
signature that cannot be grouped fails the whole table load — give adjacent arguments different
domains, or one name per cell, when in doubt.

# 2 and 3 — making it callable from kC

`<kfs.h>` is a hard-coded string in `SystemHeaders.Files`, a
`public static readonly Dictionary<string, string>`; headers are read from it at every compile with
no cache. A function is a *builtin* — may be declared without a body — only when its name is in
`SystemHeaders.Builtins` **and** it was declared in a header included with `<…>`.[^system-headers]
The parser then binds a builtin call to the service whose `kc` column starts with that
name.[^parser]

```csharp
SystemHeaders.Files["purrtty.h"] =
    "#pragma once\r\n#include <kfs.h>\r\n" +
    "void purr_light_set_on(uint32_t light, uint32_t on);\r\n" +
    "uint32_t purr_light_count(void);\r\n";
SystemHeaders.Builtins.Add("purr_light_set_on");
SystemHeaders.Builtins.Add("purr_light_count");
```

What goes wrong, and how it reports:

| Mistake | Symptom |
| --- | --- |
| No prototype | `KC-E003: '<name>' is not declared` |
| Prototype present, name not in `Builtins` | `KC-E304: '<name>' is declared but never defined` |
| Prototype's flattened cell count ≠ the row's cell count | an **exception** inside the compiler, not a diagnostic |
| Result type wider than one cell (for example `vec3`) | an **exception** inside the compiler (`builder invariant`) |
| Rows added after the first kC compile with those tables | invisible to kC: the parser caches name bindings per tables instance |

Therefore **generate the JSON rows, the header text and the builtin names from one list of
definitions**, so they cannot disagree, and check cell *types* as well as counts: a `float`
parameter against a `u32` cell compiles silently and passes float bits. A struct argument such as
`vec3` flattens to its member cells; a struct *result* is not supported — return one `uint32_t` or
one `float`. A prototype that returns a value for a row without a result, or is `void` for a row
with one, also throws.

Keep the header to prototypes and `#define`s. Function bodies do compile, but an unused `static`
helper raises `KC-W006` in every program that includes the header and a non-static body breaks
projects with two source files (`KC-E016`). A prototype-only header adds no code to a program.

Prefer a separate header (`<purrtty.h>`) over editing the `kfs.h` entry: it keeps the stock header
byte-identical and makes mod dependence visible in the program. The header is global to the
process, not per vehicle.

# 4 — routing the request

After a tick that was not abandoned, the VM delivers each queued request to
`HostComputer`'s `ISyscallSink.Request`:[^host-computer]

```csharp
if (!ApplyHostRequest(request) && !ApplyRequest(request))
    RejectByHost(request, "no vehicle to act on");
```

`VehicleComputer.ApplyRequest` (`protected override bool ApplyRequest(ServiceRequest request)`)
returns `false` for an id it does not know.[^vehicle-computer] A postfix that turns that `false`
into `true` claims the request:

```csharp
[HarmonyPatch(typeof(VehicleComputer), "ApplyRequest")]
static class ApplyRequestPatch
{
    static void Postfix(VehicleComputer __instance, ServiceRequest request, ref bool __result)
    {
        if (__result || !Mine(request.Id)) return;
        try { __result = Router.Accept(__instance, request); }   // decode, validate, enqueue; no game writes here
        catch (Exception e) { Log(e); __result = true; }          // never let an exception reach the dispatch
    }
}
```

- This runs on a **solver/worker thread, inside the dispatch**. Do not mutate game objects here.
- `request.Args` are raw cells: read floats with `BitConverter.UInt32BitsToSingle`.
- To refuse a request with a message the player sees in the program log, call
  `__instance.RejectService(request, "reason")` and still return `true`.
- Requests exist only for ticks that completed: an overrun or a trap discards the tick's requests
  before delivery.
- The VM allows 256 service calls per tick; beyond that calls are dropped with an event.
- A `BenchMachine` is not a `VehicleComputer`: on the bench a mod service is rejected with
  "no vehicle to act on", which is accurate.
- **Claim every id in the block, result-bearing ones included.** The VM queues a request for a
  result-bearing call too; left unclaimed it is rejected after having returned its result.
- A host rejection increments `sys.rejected_requests` from the next tick; the rejection text is a
  host event and subject to the log's limit of 20 events per second.
- An exception thrown here is caught by `KfsDispatch`, **halts the player's computer with a host
  fault** and hands the vehicle to the stock flight computer. Catch everything.

To find the vehicle for a `VehicleComputer`, keep a
`ConditionalWeakTable<VehicleComputer, TState>` filled from the host list
(`KfsVehicleHost.AllHosts`, an internal static list that must be locked, or a postfix on the
private `KfsVehicleHost(Vehicle, GameTables)` constructor); `host.Computer` and `host.Vehicle` are
public.[^kfs-vehicle-host] See [patch targets and seams](/integration/patch-targets-and-seams.md).

An alternative is to append the request to `VehicleComputer`'s private `_cooked` list (what the
stock cases do) and intercept the private `KfsServices.Apply` later in the same dispatch, which
supplies the host, the worker flight computer and the `fallback` flag. It needs two patches on
private members instead of one on a protected method; use it only if the handler needs that
context.

# 5 — returning a value

Results are produced **inside the `SYSCALL` instruction**, before the request is queued: the VM
calls `SyscallHost?.IssueResult(id, args, results)` whenever the row has result cells and pushes
the cells at once. `VehicleComputer` implements it explicitly and only answers ids 64 and
65.[^vehicle-computer]

```csharp
static MethodBase TargetMethod() =>
    AccessTools.Method(typeof(VehicleComputer), "KSA.Kfs.Vm.ISyscallHost.IssueResult");

static void Postfix(VehicleComputer __instance, ushort service, ReadOnlySpan<uint> args, Span<uint> results)
{
    if (results.Length == 0 || !Mine(service)) return;
    try { results[0] = Router.Answer(__instance, service, args); }
    catch (Exception e) { Log(e); results[0] = 0; }
}
```

Constraints:

- **Synchronous, on the dispatch thread, mid-tick.** There is no way to suspend the program and
  answer later. The answer must come from data that is safe to read on a worker thread — plain
  field reads of live objects (which is what stock kFS does when sampling) or a snapshot published
  from the main thread. A query whose answer must be computed on the main thread has to be
  modelled as request-then-poll.
- **One cell.** Encode a float with `BitConverter.SingleToUInt32Bits`.
- It runs before the tick is known to have completed: if the tick later overruns, the request is
  dropped but the program already has its result. Make getters free of side effects.
- On the bench there is no syscall host, so results are zero.
- `KvmMachine.SyscallHost` has a public setter; wrapping the original host in a decorator is a
  patch-free alternative, but it must be re-installed for every computer and must forward ids 64
  and 65. The Harmony postfix is simpler and was the variant tested.

# 6 — acting on the game

Stock kFS never mutates live game objects from the dispatch. `KfsServices` queues `MainWork` under
a lock and drains it in `MainThread()`, called from `KfsIntegration.BeforeInputEvents()` — on the
main thread, in the one window per frame where no vehicle-solver job is running, immediately
before the game applies its own input events.[^kfs-integration] A mod should do the same with its
own queue and a postfix on that public static, parameterless method:

```csharp
[HarmonyPatch(typeof(KfsIntegration), nameof(KfsIntegration.BeforeInputEvents))]
static class BeforeInputEventsPatch
{
    static void Postfix()
    {
        try { MainThreadPump.Run(); }          // refresh per-host state, drain queues, apply effects
        catch (Exception e) { Log(e); }        // nothing in kFS catches an exception here
    }
}
```

- The postfix runs every frame, including while paused and while the kFS flag is off; gate on
  `KfsPrototype.Enabled` if the work should stop with kFS.
- The original prunes dead hosts (`KfsVehicleHost.ReleaseDead()`) **only while the kFS switch is
  on** — its whole body is inside `if (KfsPrototype.Enabled)`. A postfix that also runs with the
  switch off must not assume the host list is pruned: test `host.IsAlive` (public) for every host
  it takes from the list, and drop its own state for hosts that fail the test.[^kfs-integration-prune]
- Effects land one frame after the request, and not all at once. A program that sets a value and
  reads it back in the same tick sees the old value. From the next frame's first tick it sees
  what the hook wrote into **fields** (a switch flag, an animation goal); **committed state**
  derived from them (an animation's position and deployment state, whether a consumer is active)
  follows one frame later, because the dispatch runs before the module update inside the solver
  job. Getters should document which of the two they return.[^dispatch-and-threading]
- Structure changes of the same frame are split around the hook: a contact docking, a destruction
  or a part failure has already been applied when it runs; a decoupling or a docking requested
  through the game's input events is applied right after it.[^dispatch-and-threading]
- For the specific game operations of the parts extension, see
  [control recipes](/game-targets/control-recipes.md).

A UI callback (`[StarMapAfterGui]`, a registered ImGui window) is **not** equivalent: it runs
while dispatches are in flight.

# Taking it out again

A mod that can be unloaded has to undo pieces 2–6; the table (piece 1) cannot be withdrawn — every
computer and the workbench hold the instance — and does not need to be: with the hooks gone its
extra rows only reject.

The header is the delicate part. `SystemHeaders.Files` and `SystemHeaders.Builtins` are a plain
`Dictionary` and `HashSet`, and the compiler reads them on **whatever thread is compiling**:
`KcFrontEnd.Lower` calls `SystemHeaders.Get(name)` for every `<…>` include and tests
`SystemHeaders.Builtins.Contains(…)`.[^kc-front-end] The stock editor compiles on a thread-pool
thread (`EditorModel` starts `Task.Run` when its `Background` flag is set, and `Building` is true
while that task is pending), and a mod's own tooling may compile on its own threads.[^editor-model]
Removing an entry while such a compile runs is a concurrent write to a collection that is being
read. So:

- remove the header and the builtin names on the main thread, and only when no compile is in
  flight: none of the mod's own, and `Workbench.Editor.Building` false;
- to ask the second question without creating the workbench (the public `KfsIntegration.Workbench`
  getter creates it on first use), read the private static `KfsIntegration._workbench`;
- if a compile is running, leave the registration in place and take it over at the next install —
  a header without its hooks only produces programs whose mod calls are rejected.

The same rule covers the install: finish every change to these statics before the first compile
can start. purrTTY implements this in `purrTTY.Kfs/Tables/SystemHeaderRegistration.cs` and
`StockEditorProbe.cs` (tested offline in `purrTTY.Kfs.Tests/Installer/HeaderInUseTests.cs`).

# Checklist

- [ ] Ids in a block no stock row uses; one definition list generates rows, header and builtins;
      ids are emitted as plain JSON integers.
- [ ] Table versions untouched; augmentation failure falls back to stock tables with a log line.
- [ ] `LoadEmbedded` patched before anything reads `KfsIntegration.Tables` (check that the private
      static is still null when patching, and log if it is not).
- [ ] Every float argument has a `finite…` domain; results are a single cell.
- [ ] Dispatch-side patches allocate nothing per call after warm-up, take no blocking locks, and
      catch everything.
- [ ] No game writes outside the main-thread pump.
- [ ] One offline test per service: compile a kC call, run it on a `FakeVehicle`, assert the
      decoded request and the returned value.
- [ ] The main-thread hook filters hosts by `IsAlive` instead of relying on the stock pruning.
- [ ] The header and builtin names are removed at unload only when no compile can be running.
- [ ] A kASM constants file contains no `SVC_*` name.

# Modding notes

- The two host patches are on `VehicleComputer`, a sealed class in the host layer; the table patch
  is on a public static factory. None of them touches the 3,000-line UI or the VM interpreter, so
  they are among the lowest-drift targets kFS offers.
- Mod-added rows appear in the in-game reference window's Services page automatically when the
  tables were augmented before the window was first opened — see
  [reference window](/tooling/reference-window.md).
- Other mods adding services would collide on ids and on the `LoadEmbedded` postfix order; a
  shared convention does not exist yet. Pick a distinctive block and fail soft on a duplicate id.

[^game-tables]: GameTables.cs:75–126 (`Load`, `LoadEmbedded`), :538–582 (`BuildServices`: id range, kinds, completeness, duplicate ids), :584–639 (`Arguments`: grouping of `kc` names onto cells), :641–690 (`CheckDomains`), :692–730 (`FiniteRange`, `EnumDomain`).
[^arg-domains]: ArgDomains.cs:52–150 (`Check`: `finite`, `finite:>=0`, `quat`, `vec_nonzero`, `event`, `event_param`, `future`, `handle`, `channel`), :150–178 (enum and range lookups; `any`, `ptr`, `format`, `count` are not checked).
[^costs-json]: KSA.Kfs.Vm.Tables.Data.costs.json — `service_classes`.
[^system-headers]: SystemHeaders.cs:8–22 (`Files`), :24–30 (`Builtins`), :34–41 (`Get`).
[^parser]: Parser.cs:3085–3103 (service bindings from the `kc` column, cached per tables instance), :1585–1588 (builtin test).
[^host-computer]: HostComputer.cs:670–676 (`ISyscallSink.Request`), :737–745 (`RejectByHost`), :747–811 (`ApplyHostRequest`), :813–816 (`ApplyRequest`).
[^vehicle-computer]: VehicleComputer.cs:149 (`Machine.SyscallHost = this`), :153–162 (`TakeServiceRequests`), :164–167 (`RejectService`), :174–183 (`IssueResult`), :185–239 (`ApplyRequest`).
[^kfs-integration]: KfsIntegration.cs:85–89 (lazy `Tables`), :146–156 (`RegisterCommands` does not read the tables), :158–176 (`BeforeInputEvents`).
[^kfs-vehicle-host]: KfsVehicleHost.cs:39 (`AllHosts`), :61–79 (`Vehicle`, `Computer`), :83–93 (`IsAlive`), :105–118 (constructor).
[^offline-harness]: Experiments E2–E11 in the offline harness document.
[^builtin-includes]: BuiltinIncludes.cs:43-46.
[^kfs-integration-prune]: KfsIntegration.cs:158-176 (`BeforeInputEvents`; `KfsVehicleHost.ReleaseDead()` at :163 inside `if (KfsPrototype.Enabled)` :160); KfsVehicleHost.cs:82-92 (`IsAlive`), :148-164 (`ReleaseDead`).
[^dispatch-and-threading]: "What a dispatch reads: live fields and committed state" and "Where a vehicle's structure changes".
[^kc-front-end]: KcFrontEnd.cs:77 (`SystemHeaders.Get(name)` in the system-include lookup), :143 (`SystemHeaders.Builtins.Contains`); Parser.cs:1585 (`InSystemHeader && SystemHeaders.Builtins.Contains(text)`).
[^editor-model]: EditorModel.cs:131 (`Background`), :133 (`Building => _pending != null`), :412-418 (`_pending = Task.Run(function)`).
