// Experiments that answer the open questions (section 7 of the brief). Observations are INFO lines;
// where the outcome is a property the plan relies on, it is also a PASS/FAIL check.

using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using HarmonyLib;
using KSA.Kfs.Compiler;
using KSA.Kfs.Host;
using KSA.Kfs.KAsm;
using KSA.Kfs.KC;
using KSA.Kfs.Vm.Tables;
using SC = System.Console;

namespace PurrKfs;

static partial class Spike
{
    // ------------------------------------------------------------------ enums

    static void EnumExperiments(string stockKfsH)
    {
        Section("mod enums: side effects on stock headers and kASM symbols");
        var aug = KfsSim.Tables;
        var stock = KfsInstall.LoadStock();
        var layout = new FakeVehicle(aug, 1, "enum-probe").Layout;

        var kincStock = new BuiltinIncludes(stock, layout, "kfs.kinc").Constants;
        var kincAug = new BuiltinIncludes(aug, layout, "kfs.kinc").Constants;
        var added = kincAug.Keys.Except(kincStock.Keys).OrderBy(x => x).ToList();
        var removed = kincStock.Keys.Except(kincAug.Keys).ToList();
        var changed = kincStock.Keys.Intersect(kincAug.Keys).Where(k => kincStock[k] != kincAug[k]).ToList();
        Info($"<kfs.kinc> constants: stock {kincStock.Count}, augmented {kincAug.Count}; added {added.Count} [{string.Join(",", added.Take(4))},...]; removed {removed.Count}; changed {changed.Count}");
        Check("<kfs.kinc> only gains the 22 SVC_PURR_* symbols", added.Count == 22 && added.All(a => a.StartsWith("SVC_PURR_")) && removed.Count == 0 && changed.Count == 0);
        Check("mod enum constants are NOT in <kfs.kinc>", !kincAug.ContainsKey("PURR_LIGHT_ON") && !kincAug.Keys.Any(k => k.StartsWith("PURR_")));
        Check("stock kfs.h text unchanged", SystemHeaders.Files["kfs.h"] == stockKfsH);
        string vhStock = GeneratedHeaders.VehicleH(stock, layout, "enum-probe", "Bench");
        string vhAug = GeneratedHeaders.VehicleH(aug, layout, "enum-probe", "Bench");
        string vkStock = GeneratedHeaders.VehicleKinc(stock, layout, "enum-probe", "Bench");
        string vkAug = GeneratedHeaders.VehicleKinc(aug, layout, "enum-probe", "Bench");
        Check("generated vehicle.h / vehicle.kinc identical with and without the mod", vhStock == vhAug && vkStock == vkAug, $"vehicle.h {vhAug.Length} chars");
        var catStock = CatalogueEnums.All(stock); var catAug = CatalogueEnums.All(aug);
        Check("CatalogueEnums (the enum constants kASM gets) unchanged", catStock.SequenceEqual(catAug), $"{catAug.Count} constants");

        // A stock program compiles to the same image with or without the mod; images interoperate.
        const string stockSrc = "#include <kfs.h>\n#pragma kfs tick_hz 10\n#pragma kfs task t rate_hz=10\nuint32_t n;\nvoid t(void) { n = n + 1u; if (n == 3u) { kfs_logf(\"n=%u\", n); } }\n";
        var imgStock = KfsSim.Compile(stockSrc, layout, stock).Image!;
        var imgAug = KfsSim.Compile(stockSrc, layout, aug).Image!;
        Check("a stock-only program compiles byte-identically against stock and augmented tables", imgStock.SequenceEqual(imgAug), $"{imgAug.Length} B");
        var stockVehicle = new FakeVehicle(stock, 1, "stock-vehicle");
        var purrImage = KfsSim.Compile(Head + "#pragma kfs tick_hz 10\n#pragma kfs task t rate_hz=10\nvoid t(void) { purr_light_set_on(0u, 1u); }\n", layout).Image!;
        var lr = stockVehicle.Computer.Load(purrImage, stockVehicle.Layout);
        Info($"image using a purr service, loaded on a computer with STOCK tables: ok={lr.Ok} rule={lr.Rule} message={lr.Message}");
        Check("an image that uses a mod service is refused cleanly without the mod", !lr.Ok);

        // kASM: are the enum constants available automatically?
        var probe = new FakeVehicle(aug, 1, "kasm-probe");
        const string usesEnum = ".include <kfs.kinc>\n.tick_hz 10\n.global v, 4\n.task main rate_hz=10\n.func main\n    PUSH8 0\n    PUSH8 PURR_LIGHT_ON\n    SYSCALL SVC_PURR_LIGHT_GETI\n    STG v\n    RET 0\n.endfunc\n";
        var r1 = probe.Assemble(usesEnum);
        Info($"kASM 'PUSH8 PURR_LIGHT_ON' with only <kfs.kinc>: ok={r1.Ok} {string.Join(" | ", r1.Diagnostics)}");
        Check("kASM does not see the header's PURR_* constants automatically", !r1.Ok);
        var r2 = probe.Assemble(ServiceCatalog.KasmEquates() + usesEnum);
        Check("kASM works with the catalog's generated .equ block", r2.Ok, string.Join(" | ", r2.Diagnostics));
        // Optional: a postfix on BuiltinIncludes(GameTables, IoLayout, string) can publish them in <kfs.kinc>.
        var ctor = AccessTools.Constructor(typeof(BuiltinIncludes), new[] { typeof(GameTables), typeof(IoLayout), typeof(string) });
        var post = new HarmonyMethod(typeof(Spike), nameof(KincPost));
        KfsInstall.Harmony.Patch(ctor, postfix: post);
        var r3 = probe.Assemble(usesEnum);
        KfsInstall.Harmony.Unpatch(ctor, HarmonyPatchType.Postfix, KfsInstall.Harmony.Id);
        Check("a postfix on the BuiltinIncludes constructor puts PURR_* into <kfs.kinc>", r3.Ok, string.Join(" | ", r3.Diagnostics));
    }

    static void FillerVisibility()
    {
        Section("where do the reserved filler entries show up?");
        var t = KfsSim.Tables;
        var layout = new FakeVehicle(t, 1, "filler-probe").Layout;
        // The filler names are reserved<N>; the stock services table has unrelated "Reserved" prose (id 4).
        bool Has(string text) => Regex.IsMatch(text, @"reserved\d+", RegexOptions.IgnoreCase);
        var places = new (string Where, bool Shows)[]
        {
            ("enums.json data (GameTables.EnumsData)", t.EnumsData.Enums.Values.Any(v => v.Any(Has))),
            ("<purrtty.h>", Has(SystemHeaders.Get(ServiceCatalog.HeaderName)!)),
            ("generated purrtty.kinc", Has(ServiceCatalog.KasmEquates())),
            ("<kfs.kinc> constants", new BuiltinIncludes(t, layout, "kfs.kinc").Constants.Keys.Any(Has)),
            ("CatalogueEnums", CatalogueEnums.All(t).Any(e => Has(e.Name))),
            ("vehicle.h / vehicle.kinc", Has(GeneratedHeaders.VehicleH(t, layout, "x", "Bench")) || Has(GeneratedHeaders.VehicleKinc(t, layout, "x", "Bench"))),
            ("services rows (domains, prose)", t.ServicesData.Services.Any(s => Has(string.Join(" ", s.ArgDomains ?? new()) + s.Args + s.Summary + s.HostChecks))),
        };
        foreach (var (where, shows) in places) Info($"filler visible in {where,-40} {(shows ? "YES" : "no")}");
        Check("fillers appear only in the enums.json data", places[0].Shows && places.Skip(1).All(p => !p.Shows));
    }

    static void KincWorkspace()
    {
        Section("kASM: purrtty.kinc as a workspace file, built by the game's Workbench");
        string ws = Path.Combine(Path.GetTempPath(), "kfs-spike06-ws-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(ws);
        try
        {
            var sim = KfsSim.Create("kinc", lights: 1);
            var wb = new KSA.Kfs.Host.Workbench.Workbench(KfsSim.Tables, ws, new StringWriter());
            wb.Workspace.Write("purrtty.kinc", ServiceCatalog.KasmEquates());
            wb.Workspace.Write("lights.kasm", """
.include <kfs.kinc>
.include "purrtty.kinc"
.tick_hz 10
.global done, 4
.global on, 4

.task main rate_hz=10
.func main
    LDG   done
    JNZ   out
    PUSH8 0                         ; u32 light
    PUSH8 PURR_LIGHT_ON             ; u32 prop: from purrtty.kinc
    SYSCALL SVC_PURR_LIGHT_GETI     ; pops 2, pushes 1
    STG   on
    PUSH8 0
    PUSH8 1
    SYSCALL SVC_PURR_LIGHT_SET_ON
    PUSH8 1
    STG   done
out:
    RET   0
.endfunc
""");
            var built = wb.Build("lights.kasm", KfsSim.Tables.BenchPart, sim.Fake.Layout);
            Info("purrtty.kinc head: " + string.Join(" / ", ServiceCatalog.KasmEquates().Split('\n').Take(3)));
            Check("Workbench assembles lights.kasm including \"purrtty.kinc\"", built is { Ok: true, Image: not null }, built == null ? "null" : string.Join(" | ", built.Diagnostics));
            if (built?.Image != null)
            {
                sim.LoadImage(built.Image).Run(0.3);
                Check("the kASM program used PURR_LIGHT_ON and switched light 0 on", sim.Parts.Lights[0].On && PurrRouter.IssuedIds.ContainsKey(0x5011));
            }
            PurrRouter.Detach(sim.Computer);
        }
        finally { Directory.Delete(ws, true); }
    }

    public static void KincPost(BuiltinIncludes __instance, string file)
    {
        if (file != "kfs.kinc") return;
        foreach (var e in ServiceCatalog.Enums) foreach (var (m, v) in e.Macros) __instance.Constants.TryAdd(m, v);
    }

    // ------------------------------------------------------------------ headers

    static CompileResult CompileWith(string header, string headerBody, string program)
    {
        SystemHeaders.Files[header] = headerBody;
        try { return KfsSim.Compile(program, new FakeVehicle(KfsSim.Tables, 1, "hdr").Layout); }
        catch (Exception e) { throw new Exception($"compile threw {e.GetType().Name}: {e.Message}", e); }
    }

    static string Diags(CompileResult r) => r.Diagnostics.Count == 0 ? "none" : string.Join(" | ", r.Diagnostics.Select(d => d.ToString()));

    static void HeaderExperiments()
    {
        Section("what can a system header contain?");
        const string prog = "#pragma kfs tick_hz 10\n#pragma kfs task t rate_hz=10\n";

        // A function with a body in a <...> header, used and logged.
        var cases = new (string Name, string Header, string Body)[]
        {
            ("static function with a body", "static float purr_deg(float rad) { return rad * 57.29578f; }\n",
             "void t(void) { kfs_logf(\"deg=%.3f\", purr_deg(1.0f)); kfs_halt(); }\n"),
            ("static inline function", "static inline float purr_deg(float rad) { return rad * 57.29578f; }\n",
             "void t(void) { kfs_logf(\"deg=%.3f\", purr_deg(1.0f)); kfs_halt(); }\n"),
            ("non-static function with a body", "float purr_deg(float rad) { return rad * 57.29578f; }\n",
             "void t(void) { kfs_logf(\"deg=%.3f\", purr_deg(1.0f)); kfs_halt(); }\n"),
            ("anonymous enum", "enum { PURR_X_A, PURR_X_ON = 2 };\n",
             "void t(void) { kfs_logf(\"deg=%.3f on=%u\", 57.296f, purr_light_geti(0u, PURR_X_ON)); kfs_halt(); }\n"),
            ("named enum type", "enum purr_light_iprop_e { PURR_E_PRESENT, PURR_E_HAS_SWITCH, PURR_E_ON };\n",
             "void t(void) { enum purr_light_iprop_e p = PURR_E_ON; kfs_logf(\"deg=%.3f on=%u\", 57.296f, purr_light_geti(0u, p)); kfs_halt(); }\n"),
            ("header that includes <vecmat.h>", "#include <vecmat.h>\n",
             "void t(void) { purr_light_set_color(0u, vec3_scale(vec3_make(1.0f, 0.5f, 0.25f), 0.5f)); kfs_logf(\"deg=%.3f\", 57.296f); kfs_halt(); }\n"),
        };
        foreach (var (name, header, body) in cases)
        {
            string hname = "purr_exp.h";
            string hdr = "#pragma once\n#include <purrtty.h>\n" + header;
            CompileResult r;
            try { r = CompileWith(hname, hdr, $"#include <kfs.h>\n#include <{hname}>\n" + prog + body); }
            catch (Exception e) { Check($"header: {name}", false, e.Message); continue; }
            string outcome = "";
            if (r.Ok)
            {
                var sim = KfsSim.Create("hdr", lights: 1);
                sim.LoadKc($"#include <kfs.h>\n#include <{hname}>\n" + prog + body);
                sim.Run(0.3);
                outcome = string.Join(" | ", sim.LogText.Where(t => t.StartsWith("deg") || t.StartsWith("rejected")));
                if (name.Contains("vecmat")) outcome += $" | light0 rgb=({sim.Parts.Lights[0].R},{sim.Parts.Lights[0].G},{sim.Parts.Lights[0].B})";
                PurrRouter.Detach(sim.Computer);
            }
            Check($"header: {name}", r.Ok && outcome.Contains("deg=57.29"), $"diagnostics: {Diags(r)}; ran: {outcome}");
        }

        // A header function body included from two translation units of one program.
        foreach (var (name, fn) in new[]
        {
            ("static", "static float purr_deg(float rad) { return rad * 57.29578f; }\n"),
            ("static inline", "static inline float purr_deg(float rad) { return rad * 57.29578f; }\n"),
            ("non-static", "float purr_deg(float rad) { return rad * 57.29578f; }\n"),
        })
        {
            SystemHeaders.Files["purr_exp.h"] = "#pragma once\n#include <purrtty.h>\n" + fn;
            var tables = KfsSim.Tables;
            CompileResult r2;
            string outcome;
            try
            {
                r2 = Compiler.Compile(new CompileInput
                {
                    Files = new Dictionary<string, string>
                    {
                        ["main.c"] = "#include <kfs.h>\n#include <purr_exp.h>\n" + prog + "float a;\nfloat other(void);\nvoid t(void) { a = purr_deg(1.0f) + other(); }\n",
                        ["other.c"] = "#include <kfs.h>\n#include <purr_exp.h>\nfloat other(void) { return purr_deg(2.0f); }\n",
                    },
                    Part = tables.BenchPart, Game = tables, Layout = new FakeVehicle(tables, 1, "two").Layout,
                });
                outcome = r2.Ok ? $"OK ({Diags(r2)})" : $"error: {Diags(r2)}";
            }
            catch (Exception e) { outcome = $"EXCEPTION {e.GetType().Name}: {e.Message}"; }
            Info($"{name} header function included from two .c files -> {outcome}");
        }

        // Code size of including the header when nothing is called.
        const string task = "#pragma kfs tick_hz 10\n#pragma kfs task t rate_hz=10\nuint32_t n;\nvoid t(void) { n = n + 1u; }\n";
        var layout = new FakeVehicle(KfsSim.Tables, 1, "size").Layout;
        string Size(string src)
        {
            var r = KfsSim.Compile(src, layout);
            var nodebug = KfsSim.Compile(src, layout, options: new CompileOptions { EmitDebug = false });
            return $"image {r.Image?.Length ?? -1} B (no debug info {nodebug.Image?.Length ?? -1} B), code {r.Report?.Program.Code.Total ?? -1} B, diagnostics: {Diags(r)}";
        }
        var results = new List<(string Name, string Size)>
        {
            ("<kfs.h> only", Size("#include <kfs.h>\n" + task)),
            ("+ <purrtty.h>", Size("#include <kfs.h>\n#include <purrtty.h>\n" + task)),
        };
        foreach (var (name, extra) in new[]
        {
            ("+ unused static helper", "static float purr_deg(float rad) { return rad * 57.29578f; }\n"),
            ("+ unused static inline helper", "static inline float purr_deg(float rad) { return rad * 57.29578f; }\n"),
            ("+ #include <vecmat.h> (unused)", "#include <vecmat.h>\n"),
        })
        {
            SystemHeaders.Files["purr_exp.h"] = "#pragma once\n#include <purrtty.h>\n" + extra;
            results.Add((name, Size("#include <kfs.h>\n#include <purr_exp.h>\n" + task)));
        }
        foreach (var (name, size) in results) Info($"size {name,-32} {size}");
        string Code(string s) => Regex.Match(s, @"code (\d+) B").Groups[1].Value;
        string NoDebug(string s) => Regex.Match(s, @"no debug info (\d+) B").Groups[1].Value;
        Check("including <purrtty.h> without calling anything adds no code and, without debug info, no bytes",
              Code(results[0].Size) == Code(results[1].Size) && NoDebug(results[0].Size) == NoDebug(results[1].Size));
        SystemHeaders.Files.Remove("purr_exp.h");
    }

    // ------------------------------------------------------------------ cost

    static void CostExperiments()
    {
        Section("cost per service call (50 Hz task)");
        string Prog(string body) => Head + "#pragma kfs tick_hz 50\n#pragma kfs task t rate_hz=50\nuint32_t g;\nvec3 c;\nvoid t(void) {\n" + body + "\n}\n";
        string ten = string.Concat(Enumerable.Repeat("    purr_light_set_on(0u, 1u);\n", 10));
        var bodies = new (string Name, string Body)[]
        {
            ("empty task", ""),
            ("1x purr_light_set_on", "    purr_light_set_on(0u, 1u);"),
            ("10x purr_light_set_on", ten),
            ("g = purr_light_count()", "    g = purr_light_count();"),
            ("g = purr_light_geti(0, ON)", "    g = purr_light_geti(0u, PURR_LIGHT_ON);"),
            ("purr_light_set_color(0, c)", "    purr_light_set_color(0u, c);"),
            ("purr_light_set_cone(0, .1, .2)", "    purr_light_set_cone(0u, 0.1f, 0.2f);"),
            ("purr_parts_rescan() [Medium]", "    purr_parts_rescan();"),
        };
        long? baseW = null; long? baseRun = null; long budget = 0;
        foreach (var (name, body) in bodies)
        {
            var sim = KfsSim.Create("cost", lights: 1);
            sim.LoadKc(Prog(body));
            var w = sim.LastCompile!.Report!.Wcet.Single(x => x.Task == "t");
            budget = w.Budget;
            var cycles = new List<uint>();
            sim.Run(0.1, 0.02, afterDispatch: (s, dr) => { if (s.Computer.LastTick is { } lt) cycles.Add(lt.Vm.CyclesUsed); });
            baseW ??= w.Cycles; baseRun ??= cycles.Last();
            Info($"{name,-32} WCET {w.Cycles,5} (+{w.Cycles - baseW,4})  run {cycles.Last(),5} (+{cycles.Last() - baseRun,4})  budget {w.Budget}");
            PurrRouter.Detach(sim.Computer);
        }

        // The VM cap: 256 service calls per tick.
        var many = KfsSim.Create("cap", lights: 1);
        many.LoadKc(Prog("    uint32_t i;\n    for (i = 0u; i < 300u; i++) { purr_light_set_on(0u, i & 1u); }"));
        var wc = many.LastCompile!.Report!.Wcet.Single(x => x.Task == "t");
        many.Run(0.04, 0.02);
        Info($"300 calls in a loop: WCET {wc.Cycles}/{wc.Budget}; log: {string.Join(" | ", many.LogText.Where(t => !t.StartsWith("loaded") && !t.StartsWith("start")).Take(3))}");
        Check("calls beyond 256 per tick are dropped by the VM with an event", many.LogText.Any(t => t.Contains("dropped 44 service request(s)")));
        PurrRouter.Detach(many.Computer);
        Info($"50 Hz budget on the bench part: {budget} cycles per tick");
    }

    // ------------------------------------------------------------------ definition mistakes

    static void MistakeExperiments()
    {
        Section("definition mistakes: diagnostic or compiler exception?");
        // Deliberately inconsistent rows, loaded into a separate tables instance (ids 0x50F0+).
        string R(int id, string name, string kc, string kind, string argTypes, string doms, string resTypes) =>
            $$"""{ "id": {{id}}, "name": "{{name}}", "kc": "`{{kc}}`", "kscript": "—", "symbol": "SVC_{{name.ToUpperInvariant()}}", "kind": "{{kind}}", "args": "—", "arg_types": [{{argTypes}}], "results": "—", "result_types": [{{resTypes}}], "cost_class": "Light", "requires_stock": null, "arg_domains": [{{doms}}], "host_checks": "—", "summary": "mistake probe" }""";
        var rows = new[]
        {
            R(0x50F0, "purr_mx_short", "purr_mx_short(a, b)", "request", "\"u32\",\"u32\"", "\"any\",\"any\"", ""),
            R(0x50F1, "purr_mx_long", "purr_mx_long(a)", "request", "\"u32\"", "\"any\"", ""),
            R(0x50F2, "purr_mx_vecres", "purr_mx_vecres()", "request+result", "", "", "\"f32\",\"f32\",\"f32\""),
            R(0x50F3, "purr_mx_noresult", "purr_mx_noresult()", "request", "", "", ""),
            R(0x50F4, "purr_mx_voidproto", "purr_mx_voidproto()", "request+result", "", "", "\"u32\""),
            R(0x50F5, "purr_mx_floatproto", "purr_mx_floatproto(x)", "request", "\"u32\"", "\"any\"", ""),
            R(0x50F6, "purr_mx_i64", "purr_mx_i64(t)", "request", "\"u32\"", "\"any\"", ""),
            R(0x50F7, "purr_mx_named", "purr_mx_other(a)", "request", "\"u32\"", "\"any\"", ""),
        };
        var tables = KfsInstall.LoadAugmented("mistakes", (file, json) =>
        {
            if (file != "services.json") return json;
            var root = JsonNode.Parse(json)!;
            foreach (var r in rows) root["services"]!.AsArray().Add(JsonNode.Parse(r));
            return root.ToJsonString();
        });
        SystemHeaders.Files["purr_mx.h"] = """
#pragma once
#include <kfs.h>
void purr_mx_short(uint32_t a);
void purr_mx_long(uint32_t a, vec3 v);
vec3 purr_mx_vecres(void);
uint32_t purr_mx_noresult(void);
void purr_mx_voidproto(void);
void purr_mx_floatproto(float x);
void purr_mx_i64(int64_t t);
void purr_mx_named(uint32_t a);
void purr_mx_norow(void);
""";
        foreach (var n in new[] { "purr_mx_short", "purr_mx_long", "purr_mx_vecres", "purr_mx_noresult", "purr_mx_voidproto", "purr_mx_floatproto", "purr_mx_i64", "purr_mx_named", "purr_mx_norow" })
            SystemHeaders.Builtins.Add(n);
        var layout = new FakeVehicle(tables, 1, "mx").Layout;
        var probes = new (string What, string Call)[]
        {
            ("prototype 1 cell, row 2 cells", "purr_mx_short(1u);"),
            ("prototype 4 cells (u32+vec3), row 1 cell", "vec3 v; v.x = 1.0f; v.y = 2.0f; v.z = 3.0f; purr_mx_long(1u, v);"),
            ("vec3 result, row 3 result cells", "vec3 v; v = purr_mx_vecres(); g = (uint32_t)v.x;"),
            ("prototype returns uint32_t, row has no result (value used)", "g = purr_mx_noresult();"),
            ("prototype returns void, row has a u32 result", "purr_mx_voidproto();"),
            ("prototype float, row u32 (same cell count)", "purr_mx_floatproto(1.5f);"),
            ("prototype int64_t (2 cells), row u32", "purr_mx_i64(5);"),
            ("kc column names a different function", "purr_mx_named(1u);"),
            ("builtin + prototype, no row at all", "purr_mx_norow();"),
        };
        foreach (var (what, call) in probes)
        {
            string src = "#include <kfs.h>\n#include <purr_mx.h>\n#pragma kfs tick_hz 10\n#pragma kfs task t rate_hz=10\nuint32_t g;\nvoid t(void) {\n    " + call + "\n}\n";
            string outcome;
            try
            {
                var r = KfsSim.Compile(src, layout, tables);
                outcome = r.Ok ? $"compiles OK ({Diags(r)})" : $"diagnostic: {Diags(r)}";
            }
            catch (Exception e) { outcome = $"EXCEPTION {e.GetType().Name}: {e.Message.Split('\n')[0]}"; }
            Info($"{what,-58} -> {outcome}");
        }
        SystemHeaders.Files.Remove("purr_mx.h");
    }

    // ------------------------------------------------------------------ loader messages

    static void LoaderRejections()
    {
        Section("loader messages for bad rows (what the fallback would print)");
        JsonObject Good() => ServiceCatalog.Row(ServiceCatalog.ById[0x5014]);
        var bad = new (string What, Action<JsonObject> Edit)[]
        {
            ("enum domain naming an unknown enumeration", r => { r["id"] = 0x50E0; r["name"] = "purr_lx1"; r["kc"] = "`purr_lx1(light, rgb)`"; r["arg_types"] = new JsonArray("u32", "f32", "f32", "f32"); r["arg_domains"] = new JsonArray("enum:purr_nope", "finite", "finite", "finite"); }),
            ("finite:>=0 on a u32 cell", r => { r["id"] = 0x50E1; r["name"] = "purr_lx2"; r["arg_domains"] = new JsonArray("finite:>=0", "finite:>=0", "finite:>=0", "finite:>=0"); }),
            ("vec3 cells with different domains (kc names can't group)", r => { r["id"] = 0x50E2; r["name"] = "purr_lx3"; r["arg_domains"] = new JsonArray("any", "finite", "finite:>=0", "finite"); }),
            ("duplicate id", r => { r["id"] = 0x5000; r["name"] = "purr_lx4"; }),
            ("empty summary", r => { r["id"] = 0x50E4; r["name"] = "purr_lx5"; r["summary"] = " "; }),
            ("unknown cost class", r => { r["id"] = 0x50E5; r["name"] = "purr_lx6"; r["cost_class"] = "Cheap"; }),
            ("unknown key in the row", r => { r["id"] = 0x50E6; r["name"] = "purr_lx7"; r["note"] = "x"; }),
            ("enum sub-range enum:purr_light_prop[2..3] (should load)", r => { r["id"] = 0x50E7; r["name"] = "purr_lx8"; r["kc"] = "`purr_lx8(light, prop)`"; r["arg_types"] = new JsonArray("u32", "u32"); r["arg_domains"] = new JsonArray("any", "enum:purr_light_prop[2..3]"); }),
        };
        foreach (var (what, edit) in bad)
        {
            var row = Good(); edit(row);
            string outcome;
            try
            {
                var t = KfsInstall.LoadAugmented("bad-row", (file, json) =>
                {
                    if (file != "services.json") return json;
                    var root = JsonNode.Parse(json)!; root["services"]!.AsArray().Add(row); return root.ToJsonString();
                });
                outcome = $"loads ({t.Services.Count} services)";
            }
            catch (Exception e) { outcome = $"{e.GetType().Name}: {e.Message}"; }
            Info($"{what,-58} -> {outcome}");
        }
    }

    static void AugmentFallback()
    {
        Section("augmentation failure keeps the stock tables");
        KfsInstall.ExtraTransform = (file, json) => file == "services.json" ? json.Replace("\"cost_class\":\"Light\"", "\"cost_class\":\"Cheap\"") : json;
        try
        {
            var t = GameTables.LoadEmbedded();                  // patched; KfsIntegration.Tables is already cached
            Check("a failing augmentation returns usable stock tables", t.Origin == "embedded:KSA" && !t.Services.ContainsKey(0x5000), $"origin {t.Origin}, reason: {KfsInstall.AugmentFailure}");
        }
        finally { KfsInstall.ExtraTransform = null; }
    }
}
