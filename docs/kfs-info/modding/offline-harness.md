---
type: How-To
title: Running kFS offline — the test harness and what it proved
description: How to load KSA.dll outside the game and drive the real kFS tables, compiler, VM, host and Workbench from a console or test project, with the complete spike program and the results of each experiment run against build 5949.
resource: ksa-decomp:KSA.Kfs.Host/FakeVehicle.cs
tags: [kfs, modding, testing, harness, harmony, spike, verified]
sources:
  - id: fake-vehicle
    resource: ksa-decomp:KSA.Kfs.Host/FakeVehicle.cs
    title: FakeVehicle (decompiled)
  - id: game-tables
    resource: ksa-decomp:KSA.Kfs.Vm.Tables/GameTables.cs
    title: GameTables (decompiled)
  - id: system-headers
    resource: ksa-decomp:KSA.Kfs.KC/SystemHeaders.cs
    title: SystemHeaders (decompiled)
  - id: host-computer
    resource: ksa-decomp:KSA.Kfs.Host/HostComputer.cs
    title: HostComputer (decompiled)
  - id: vehicle-computer
    resource: ksa-decomp:KSA.Kfs.Host/VehicleComputer.cs
    title: VehicleComputer (decompiled)
  - id: workbench
    resource: ksa-decomp:KSA.Kfs.Host.Workbench/Workbench.cs
    title: Workbench (decompiled)
  - id: kfs-integration
    resource: ksa-decomp:KSA.Kfs/KfsIntegration.cs
    title: KfsIntegration (decompiled)
  - id: compat-5949
    resource: ../../ksa-5949-compatibility.md
    title: purrTTY KSA 5949 compatibility review (offline Harmony probe)
generated: { by: claude-code/claude-fable-5-2, at: 2026-11-25T22:02:02Z }
verified: { by: process:kfs-offline-spike, at: 2026-11-25T22:38:23Z }
status: stable
ksa_build: "2026.11.25.5949"
---

# What this is

The kFS toolchain, VM and host are ordinary public .NET types with no dependency on a running
game: the tables load from resources embedded in `KSA.dll`, `FakeVehicle` is a public stand-in for
a vehicle's computer, and the game's own `Workbench` only needs a folder and a `TextWriter`.[^fake-vehicle][^workbench]
So a console program or a test project can compile kC, load the image, run ticks and observe
service requests **without launching KSA**.

This document records how to do that and what a series of experiments established. `verified` in
the frontmatter refers to exactly this: every row of the results table below was produced by
running a spike program against the unmodified `KSA.dll` of build 2026.11.25.5949 on 2026-11-25.
The programs are kept in the repository under `plans/investigations/kfs-spikes/` (01–04: tables,
services, the loader patch and the workbench; 05: a language server with Neovim; 06: a 22-service
family with simulated parts); the one reproduced below is 04. **Nothing here was run inside the
game**; what the harness cannot show is listed at the end.

Environment of the runs: macOS arm64, .NET SDK 11.0.100 targeting `net10.0` (runtime 10.0.15),
`Lib.Harmony` 2.4.2.

# Step 1 — load `KSA.dll` outside the game

`KSA.dll` is IL-only but its PE header declares machine `0x8664` (x64). On an x64 host it loads as
is. On arm64 (Apple Silicon, `macos-latest` CI) the runtime refuses it unless the machine field
says ARM64. The purrTTY compatibility probe solved this with a patched scratch copy;[^compat-5949]
the same two bytes can be patched **in memory**, leaving the snapshot untouched and copying nothing
into the test output:

```csharp
// Register before any KSA type is touched (keep KSA types out of this method's body).
AssemblyLoadContext.Default.Resolving += (ctx, name) =>
{
    string path = Path.Combine(ksaDllDir, name.Name + ".dll");
    if (!File.Exists(path)) return null;
    byte[] bytes = File.ReadAllBytes(path);
    int pe = BitConverter.ToInt32(bytes, 0x3C);                       // e_lfanew
    ushort machine = BitConverter.ToUInt16(bytes, pe + 4);           // IMAGE_FILE_HEADER.Machine
    if (machine == 0x8664 && RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
    {
        bytes[pe + 4] = 0x64; bytes[pe + 5] = 0xAA;                  // IMAGE_FILE_MACHINE_ARM64
    }
    return ctx.LoadFromStream(new MemoryStream(bytes));
};
```

Project reference, so the game DLLs are compile-time only and the resolver above supplies them at
run time:

```xml
<Reference Include="$(KSAFolder)*.dll">
  <Private>false</Private>
</Reference>
<PackageReference Include="Lib.Harmony" Version="2.4.2" />
```

Only `KSA.dll` needed the patch in the runs; the `Brutal.*` assemblies it pulls in loaded
unmodified. Embedded resources remain readable from a stream-loaded assembly
(`GetManifestResourceStream` is what `GameTables.LoadEmbedded` uses).[^game-tables]

# Step 2 — the pieces of a harness

| Need | Call | Notes |
| --- | --- | --- |
| Tables | `GameTables.LoadEmbedded()` or `GameTables.Load(Func<string, Stream?> open, string origin)` | The second overload runs the same validation on whatever streams it is given.[^game-tables] |
| kC front end | `KcFrontEnd.Register()` | Sets `Compiler.KcFrontEnd`; the `Workbench` constructor also does it. |
| A vehicle | `new FakeVehicle(tables, thrusters, name, part, engines, gimbals)` | Builds a `VehicleComputer` on the Bench part and a generated layout.[^fake-vehicle] |
| Compile | `Compiler.Compile(new CompileInput { Files, Part = tables.BenchPart, Game = tables, Layout = fake.Layout })` | Returns `CompileResult` with `Ok`, `Image`, `Diagnostics`. |
| Assemble | `fake.Assemble(source)` | kASM. |
| Load and start | `fake.Computer.Load(image, fake.Layout)`, `fake.Computer.Start(0L)` | Direct calls are fine in a single-threaded harness. |
| Run | `fake.Dispatch(timeNs, default(TruthSample))` | One dispatch; runs the ticks that are due. Returns `DispatchResult`. |
| Game-bound requests | `fake.Computer.TakeServiceRequests()` | What `KfsServices` would drain. |
| Program log and host events | `fake.Computer.Console.Snapshot()` | `LogLine(K, TimeS, Text)`. |
| The game's tooling | `new Workbench(tables, folder, writer) { ActiveVehicle = () => fake, ActiveVehicleIdSource = () => fake.Id }` | `FakeVehicle` implements `IVehicleTarget`.[^workbench] |

# The complete spike program

This is the last and most complete experiment: the game's own lazy table accessor, the game's own
`Workbench` building a kC file from a workspace folder, a load and start queued onto a fake
vehicle, and two mod-defined services observed and answered by Harmony postfixes.

```csharp
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text;
using System.Text.Json.Nodes;
using HarmonyLib;
using SC = System.Console;

static class Entry
{
    const string Dir = "<ksa-game-assemblies>/current/dll/";

    static int Main()
    {
        AssemblyLoadContext.Default.Resolving += (ctx, name) =>
        {
            string path = Path.Combine(Dir, name.Name + ".dll");
            if (!File.Exists(path)) return null;
            byte[] bytes = File.ReadAllBytes(path);
            int pe = BitConverter.ToInt32(bytes, 0x3C);
            ushort machine = BitConverter.ToUInt16(bytes, pe + 4);
            if (machine == 0x8664 && RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
            {
                bytes[pe + 4] = 0x64; bytes[pe + 5] = 0xAA;
                SC.WriteLine($"[resolver] {name.Name}: AMD64 -> ARM64 in memory");
            }
            return ctx.LoadFromStream(new MemoryStream(bytes));
        };
        return Spike.Run();
    }
}

static class Spike
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Run()
    {
        var h = new Harmony("spike4");
        h.Patch(AccessTools.Method(typeof(KSA.Kfs.Vm.Tables.GameTables), "LoadEmbedded"),
                postfix: new HarmonyMethod(typeof(Spike), nameof(LoadEmbeddedPost)));
        h.Patch(AccessTools.Method(typeof(KSA.Kfs.Host.VehicleComputer), "ApplyRequest"),
                postfix: new HarmonyMethod(typeof(Spike), nameof(ApplyPost)));
        h.Patch(AccessTools.Method(typeof(KSA.Kfs.Host.VehicleComputer), "KSA.Kfs.Vm.ISyscallHost.IssueResult"),
                postfix: new HarmonyMethod(typeof(Spike), nameof(IssuePost)));

        KSA.Kfs.KC.SystemHeaders.Files["purrtty.h"] =
            "#pragma once\r\n#include <kfs.h>\r\n" +
            "void purr_light_set_on(uint32_t light, uint32_t on);\r\n" +
            "uint32_t purr_light_count(void);\r\n";
        KSA.Kfs.KC.SystemHeaders.Builtins.Add("purr_light_set_on");
        KSA.Kfs.KC.SystemHeaders.Builtins.Add("purr_light_count");

        var tables = KSA.Kfs.KfsIntegration.Tables;                 // the game's own lazy accessor
        SC.WriteLine($"tables: {tables.Origin} services={tables.Services.Count}");

        string ws = Path.Combine(Path.GetTempPath(), "kfs-spike-ws-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(ws);
        var outp = new StringWriter();
        var wb = new KSA.Kfs.Host.Workbench.Workbench(tables, ws, outp);
        var fake = new KSA.Kfs.Host.FakeVehicle(tables, thrusters: 4, engines: 1, gimbals: 1, name: "fake-1");
        wb.ActiveVehicle = () => fake;
        wb.ActiveVehicleIdSource = () => fake.Id;
        wb.Workspace.Write("lights.c", """
#include <kfs.h>
#include <purrtty.h>
#pragma kfs tick_hz 10
#pragma kfs task blink rate_hz=10
uint32_t n;
void blink(void) {
    n++;
    if (n == 2u) kfs_logf("lights: %u", purr_light_count());
    if (n % 2u == 0u) purr_light_set_on(0u, (n / 2u) % 2u);
}
""");
        var built = wb.Build("lights.c");
        SC.WriteLine($"workbench build: ok={built?.Ok} lang={built?.Language} image={(built?.Image?.Length ?? -1)}");
        SC.WriteLine("select vehicle: " + wb.SelectTarget("vehicle"));
        SC.WriteLine("load: " + wb.Load("lights.c --vehicle"));
        wb.Start();
        for (int f = 0; f < 8; f++)
        {
            wb.Frame(0.1);
            var dr = fake.Dispatch((long)(f * 0.1 * 1e9), default(KSA.Kfs.Host.TruthSample));
            var mod = Drain();
            SC.WriteLine($"frame {f}: ticks={dr.TicksRun} state={fake.Computer.State} " +
                         $"mod=[{string.Join("; ", mod.Select(r => $"0x{r.Id:X4}({string.Join(",", r.Args)})"))}]");
        }
        foreach (var l in fake.Computer.Console.Snapshot()) SC.WriteLine($"  program log k={l.K}: {l.Text}");
        Directory.Delete(ws, true);
        return 0;
    }

    static readonly List<KSA.Kfs.Vm.ServiceRequest> Seen = new();
    static List<KSA.Kfs.Vm.ServiceRequest> Drain() { var l = new List<KSA.Kfs.Vm.ServiceRequest>(Seen); Seen.Clear(); return l; }

    // Claim requests in the mod's id block that the stock switch did not handle.
    public static void ApplyPost(KSA.Kfs.Host.VehicleComputer __instance, KSA.Kfs.Vm.ServiceRequest request, ref bool __result)
    {
        if (!__result && request.Id >= 0x5000 && request.Id <= 0x50FF) { Seen.Add(request); __result = true; }
    }

    // Supply the synchronous result cell of a result-bearing mod service.
    public static void IssuePost(KSA.Kfs.Host.VehicleComputer __instance, ushort service, ReadOnlySpan<uint> args, Span<uint> results)
    {
        if (service == 0x5010 && results.Length > 0) results[0] = 3u;
    }

    static bool _inside;
    public static void LoadEmbeddedPost(ref KSA.Kfs.Vm.Tables.GameTables __result)
    {
        if (_inside) return;                                         // our own nested Load must not recurse
        _inside = true;
        try
        {
            var asm = typeof(KSA.Kfs.Vm.Tables.GameTables).Assembly;
            __result = KSA.Kfs.Vm.Tables.GameTables.Load(file =>
            {
                var st = asm.GetManifestResourceStream(KSA.Kfs.Vm.Tables.GameTables.RESOURCE_PREFIX + file);
                if (st == null || file != "services.json") return st;
                using var r = new StreamReader(st);
                var root = JsonNode.Parse(r.ReadToEnd())!;
                var arr = root["services"]!.AsArray();
                arr.Add(JsonNode.Parse("""{ "id": 20496, "name": "purr_light_count", "kc": "`purr_light_count()`", "kscript": "—", "symbol": "SVC_PURR_LIGHT_COUNT", "kind": "request+result", "args": "—", "arg_types": [], "results": "`u32 count`", "result_types": ["u32"], "cost_class": "Light", "requires_stock": null, "arg_domains": [], "host_checks": "—", "summary": "Number of lights on the vehicle." }"""));
                arr.Add(JsonNode.Parse("""{ "id": 20498, "name": "purr_light_set_on", "kc": "`purr_light_set_on(light, on)`", "kscript": "—", "symbol": "SVC_PURR_LIGHT_SET_ON", "kind": "request", "args": "`u32 light`, `u32 on`", "arg_types": ["u32", "u32"], "results": "—", "result_types": [], "cost_class": "Light", "requires_stock": null, "arg_domains": ["any", "any"], "host_checks": "light index out of range", "summary": "Switches a light on or off." }"""));
                return new MemoryStream(Encoding.UTF8.GetBytes(root.ToJsonString()));
            }, "embedded:KSA+purrtty");
        }
        catch (Exception e) { SC.WriteLine("augment failed: " + e.Message); }
        finally { _inside = false; }
    }
}
```

Output of that run (workbench build report trimmed):

```text
[resolver] KSA: AMD64 -> ARM64 in memory
tables: embedded:KSA+purrtty services=22
workbench build: ok=True lang=kC image=852
select vehicle: True
load: True
frame 0: ticks=1 state=Running mod=[]
frame 1: ticks=1 state=Running mod=[0x5010(); 0x5012(0,1)]
frame 2: ticks=1 state=Running mod=[]
frame 3: ticks=1 state=Running mod=[0x5012(0,0)]
frame 4: ticks=1 state=Running mod=[]
frame 5: ticks=1 state=Running mod=[0x5012(0,1)]
frame 6: ticks=1 state=Running mod=[]
frame 7: ticks=1 state=Running mod=[0x5012(0,0)]
  program log k=0: loaded 852 bytes on Bench: tick_hz 10, budget 100000 cycles, 1 task(s), layout 0x002504E4
  program log k=0: start: Running
  program log k=1: lights: 3
```

The workbench's own output for the same run included
`queued load onto vehicle fake-1 (4 thrusters, layout 0x002504E4); kfs start boots it` and
`queued start on vehicle fake-1`.

# Results of the experiments

Each row is one observation from a run of the harness; "kC" programs were compiled by the real
front end and executed by the real VM. E1–E12 come from spikes 01–04, E13–E21 from spike 06 and
E25–E27 from spike 06 and E22–E24 from spike 05.

| # | Question | Result |
| --- | --- | --- |
| E1 | Do the embedded tables load outside the game? | Yes: `origin=embedded:KSA`, 20 wired services, service-table version 1, message-catalogue version 4, 5 catalogue parts, Bench = K32F / 1 MHz / 131,072 B RAM. |
| E2 | Does the loader accept extra service rows? | Yes. `GameTables.Load(open, origin)` given the stock `services.json` plus rows with ids 256–260 (later `0x5010`, `0x5012`) returned tables containing them; typed `arg_types`, `result_types` and `arg_domains` were accepted. |
| E3 | Is a new row enough for kC? | No. Calling it without a prototype: `KC-E003: 'kfs_light_set' is not declared`. With a prototype in a `<…>` header but the name missing from `SystemHeaders.Builtins`: `KC-E304: '…' is declared but never defined`. With both, the call compiles and binds to the row whose `kc` column starts with that name. |
| E4 | What does the stock host do with such a request? | `rejected light_set: no vehicle to act on` in the program log; a result-bearing call returns 0. |
| E5 | Can Harmony intercept it? | Yes. A postfix on `VehicleComputer.ApplyRequest` received `ServiceRequest { Id = 256, Args = (3, 1), Task = 0 }` and, by setting `__result = true`, suppressed the rejection. A postfix on the explicit implementation `KSA.Kfs.Vm.ISyscallHost.IssueResult` (parameters `ushort`, `ReadOnlySpan<uint>`, `Span<uint>`) wrote `results[0] = 7` and the program logged `lights 7`. |
| E6 | Float and aggregate arguments? | `void f(uint32_t, vec3)` against `arg_types ["u32","f32","f32","f32"]` produced `Args = (2, 1.0, 0.5, 0.25)` as raw float bits — a `vec3` flattens to three cells. A `float` result from `result_types ["f32"]` reached the program intact. |
| E7 | Are argument domains enforced for new rows? | Yes. With `arg_domains ["any","finite:[0,1]","finite:[0,1]","finite:[0,1]"]`, passing 2.0 produced `rejected light_set_color argument 1 = (2, 0.5, 0.25): outside [0, 1]` and the request never reached the host hook. |
| E8 | Multi-cell results? | **Not usable from kC.** A prototype returning `vec3` against `result_types ["f32","f32","f32"]` made the compiler throw an `InvalidOperationException` from `Lowering` — an exception, not a diagnostic. The text depends on the code around the call: `label Lend0 reached by fall-through with height 2 and by a jump with 0 ('fly', builder invariant)` in one program, `RET 0 with 2 cells on the stack in 't' (builder invariant)` in another. Keep results to one cell. |
| E9 | kASM? | Automatic. `SYSCALL SVC_LIGHT_SET` assembled with no extra declaration (the symbol comes from the row's `symbol`), and the request arrived as `Id = 256, Args = (3, 1)`. Immediates are pushed with `PUSH8`, not `PUSH`. |
| E10 | Does a patch on `GameTables.LoadEmbedded` reach the game's accessor? | Yes. With a postfix replacing `__result`, `KfsIntegration.Tables` returned the augmented instance (`origin=embedded:KSA+purrtty`), the same object on every read, and the postfix ran once. The private static `KfsIntegration._tables` was null before the first read. |
| E11 | Does the game's tooling path honour all of the above? | Yes — the run shown above: `Workbench.Build`, `SelectTarget("vehicle")`, `Load("… --vehicle")`, `Start()`, then dispatches. |
| E12 | Side observation | After a `kfs_stage()` request the computer ran no further ticks (`TicksRun = 0`) in a harness that never called `StageApplied()`: a pending stage holds the program until the game applies it. |
| E13 | Does a whole service family work? | Yes. Spike 06 defined 22 services (ids `0x5000–0x5033`: bookkeeping, lights, deployables, actuators) and their enumerations from one definition list, generated the rows, the enum entries and a 22-prototype header, and ran kC and kASM programs calling every one against a simulated parts back end: in its final form 76 checks passed, none failed. The augmented tables reported 42 services. |
| E14 | What may a mod header contain? | Function bodies (`static`, `static inline`, plain), anonymous and named `enum`s and `#include <vecmat.h>` all compile. But an unused `static`/inline helper raises `KC-W006 … never used` in every program that includes the header, and a non-static body included from two `.c` files is `KC-E016`. Use `#define`s and prototypes only. Including a prototype-only header adds **0 bytes of code** (28 bytes of debug info). |
| E15 | Do new `enums.json` keys disturb anything? | No. They are usable at once as `enum:<key>` domains (an unknown key fails the load with `domain 'enum:…' doesn't fit type 'u32'`). `kfs.h`, `vehicle.h`, `vehicle.kinc` and the kASM enum constants are unchanged, and a stock program compiles byte-identically. kASM does **not** get the new constants (`KA-E003 unknown symbol`); it gets only the `SVC_*` symbols. |
| E16 | When is a write readable? | With commands applied once per frame before the dispatch (the game's order), a value set in tick *k* is read back in the **first tick of the next dispatch**: *k+1* at one tick per frame, later when several ticks share a frame; never in the same tick. |
| E17 | What does a call cost? | On the Bench processor at 50 Hz (budget 20,000 cycles), measured cycles equal the compile report's WCET: 22–25 cycles for a Light-class call with up to two scalar arguments, 55 with a `vec3`, 40 for Medium. The binding limit is the VM's 256 service calls per tick (`kfs_logf` counts): the 257th onward are dropped with `dropped N service request(s)`. |
| E18 | Result-bearing calls and the request queue | The VM queues a request for result-bearing calls as well. An unclaimed getter is therefore rejected "no vehicle to act on" **after** having returned its (zero) result — on the bench every mod call, getters included, is rejected that way. A mod must claim every id in its block. |
| E19 | Which definition mistakes throw instead of diagnosing? | All of these escape `Compiler.Compile` as `InvalidOperationException`: prototype cell count ≠ row cell count (`<name>: 1 cells for 2 (kC §10.2 binding)`); a struct result; a prototype returning a value for a row with no result (`operand stack below the floor…`); a `void` prototype for a row with a result (`RET 0 with 1 cells…`). Silent: a `float` parameter against a `u32` cell compiles and passes float bits. |
| E20 | Row JSON details | An id emitted as a `ushort`-typed JSON value made the game's reader fail (`UInt16 cannot be converted to Int32`) and, correctly, the augmentation fell back to stock tables; emit ids as plain integers. Loader messages seen for bad rows: `the kC column's 2 arguments don't group its 4 arg_types entries (D-118 (4))`, `id 20480 listed twice`, `service '…' is incomplete`, `The JSON property 'note' could not be mapped…`. |
| E21 | Timing of rejection counters | An issue-time (domain) rejection increments `sys.rejected_requests` in the same tick; a host rejection is visible from the next tick. Rejection *texts* are host events, limited to 20 per second (program log lines: 10 per second). |
| E25 | Do `enum:<key>[a..b]` sub-range domains work for mod enumerations? | Yes, exactly like the stock `enum:direction[2..21]`. With one 24-entry `purr_light_prop` list (integer properties 0–7, `reserved8`…`reserved15` fillers, float properties 16–23) and domains `[0..7]` / `[16..23]`, the VM rejected each mix-up at issue time: `rejected purr_light_geti argument 1 = 16: not in purr_light_prop[0..7]`, `rejected purr_light_getf argument 1 = 2: not in purr_light_prop[16..23]`, and likewise for a filler value (9) and an out-of-list value (99). The fillers appear only in the loaded enum data — not in any header, `.kinc` or symbol table. |
| E26 | Can the bench be given a clearer answer? | Yes. A postfix on the base `HostComputer.ApplyRequest` (which `VehicleComputer` overrides without calling) fired only for `BenchMachine`; it claimed getters silently and logged `rejected purr_light_set_on: purr services need a vehicle (the bench has no parts)` for setters using public members only (`Machine.CountRejectedRequest()`, `Console.EventAllowed/Event`, `LastK`, `TickHz`). **Unverified:** whether the JIT inlines that one-line virtual at its call site in the game. |
| E27 | How do kASM programs get mod constants? | A generated `purrtty.kinc` of `.equ NAME, value` lines, written into a workspace and pulled in with `.include "purrtty.kinc"`, built through the game's `Workbench.Build`. Alternatively a postfix on the `BuiltinIncludes(GameTables, IoLayout, string)` constructor that `TryAdd`s to `__instance.Constants` for `kfs.kinc` — also run successfully. |
| E22 | Can a language server be driven by the game's compiler? | Yes. Spike 05's hand-rolled LSP server compiled buffers with `Compiler.Compile` and a headless Neovim 0.12.6 attached over TCP (`vim.lsp.rpc.connect`) and over stdio, receiving diagnostics, hover and completion, including for mod services. |
| E23 | Diagnostic positions | A diagnostic is a point: 1-based line, 1-based column counted in UTF-16 code units after CRLF→LF normalisation (a tab is 1, a non-BMP character 2); no end or length; line 0 / column 0 means "no position"; a diagnostic inside a header names it as `<kfs.h>:102:6`. Ranges derived from the token at that position mapped correctly in UTF-8, UTF-16 and UTF-32 client encodings. |
| E24 | Compile latency and concurrency | First compile in a process 80–95 ms (kFS start-up ≈ 190 ms); warm compiles 0.2–2 ms for small files, ≈ 19 ms median (≤ 46 ms) for the largest stock examples. 400 compiles of 12 files on 8 threads, five times, matched serial results; up to 7 overlapping compiles of one document caused no error. A compile can still throw (E19), and one that hits the 30 s limit leaves its thread running. The compiler recovers and reports several errors per pass: 100 errors plus a "too many errors" marker; warnings are not capped. |

# What the harness cannot show

- Anything about real vehicles: `KfsVehicleHost`, `KfsServices`, `KfsControllerSet` and
  `KfsHostSampler` need a live `Vehicle`. The fake vehicle has no parts, lights or animations.
- Threading. The harness is single-threaded; in the game the dispatch runs on solver threads while
  the UI runs on the main thread — see [dispatch and threading](/integration/dispatch-and-threading.md).
- Whether the JIT inlines a patched method at some call site in the running game.
- Loader timing: when StarMap lets a mod patch relative to the first use of `KfsIntegration.Tables`.
- Rendering and simulation effects of changing a light or an animation. Spike 06's lights and
  animations are an in-memory model with the documented clamps and slew rate, not the game's.

# Modding notes

- The harness makes the risky half of a kFS extension — table rows, header text, compiler binding,
  VM marshalling, host interception — testable in CI with no game process. Put one test per
  service that compiles a kC call, runs it and asserts the decoded request.
- Keep game DLLs out of the test output (`Private=false`) and resolve them from the directory the
  build already uses (`KSAFolder`, see `Directory.Build.props`).
- Harmony patches applied in a test process are process-wide; unpatch in teardown and do not run
  such fixtures in parallel.
- Because every load of a fresh `GameTables` re-parses and re-validates the JSON, a table-shape
  change in a game update shows up here as a failing test rather than as a broken kFS in game.

[^fake-vehicle]: FakeVehicle.cs:11–110 (constructor :31–44, `Assemble` :46–49, `Boot` :51–64, `Dispatch` :102–110).
[^game-tables]: GameTables.cs:75–126 (`Load` overloads and `LoadEmbedded`), :21 (`RESOURCE_PREFIX`).
[^system-headers]: SystemHeaders.cs:8–41.
[^host-computer]: HostComputer.cs:670–676.
[^vehicle-computer]: VehicleComputer.cs:174–239.
[^workbench]: Workbench.cs:70–80 (`Workspace`, `ActiveVehicle`), :364 (constructor), :659 (`Build`), :739 (`Load`), :944 (`SelectTarget`), :973 (`Start`), :1168 (`Frame`); IVehicleTarget.cs:5–14.
[^kfs-integration]: KfsIntegration.cs:85–89.
[^compat-5949]: docs/ksa-5949-compatibility.md — "Only a scratch copy of IL-only `KSA.dll` had its PE machine flag changed from x64 to arm64."
