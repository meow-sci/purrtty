// Spike 06: the purrTTY parts-service catalog, installed into the real kFS toolchain and run outside
// the game against simulated vehicle parts. Self-checking: prints PASS/FAIL per check, INFO for
// observations, and exits non-zero if any check failed.
//
//   KSA_DLL_DIR=<ksa-game-assemblies>/current/dll dotnet run

using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text.RegularExpressions;
using KSA.Kfs.Host;
using SC = System.Console;

namespace PurrKfs;

static class Entry
{
    static int Main(string[] args)
    {
        // Generation needs no game assemblies: print an artefact and exit.
        switch (args.FirstOrDefault())
        {
            case "--emit-header": SC.Write(ServiceCatalog.Header()); return 0;
            case "--emit-rows": foreach (var r in ServiceCatalog.ServiceRowsJson()) SC.WriteLine(r.ToJsonString()); return 0;
            case "--emit-enums": foreach (var (k, v) in ServiceCatalog.EnumsJson()) SC.WriteLine($"\"{k}\": {v.ToJsonString()}"); return 0;
            case "--emit-kinc": SC.Write(ServiceCatalog.KasmEquates()); return 0;
        }
        string dir = Environment.GetEnvironmentVariable("KSA_DLL_DIR")
            ?? throw new InvalidOperationException("set KSA_DLL_DIR to the folder holding KSA.dll");
        // Resolve game assemblies from the untouched snapshot; on arm64 patch the PE machine field IN MEMORY.
        // Registered before any KSA type is touched: keep KSA types out of this method.
        AssemblyLoadContext.Default.Resolving += (ctx, name) =>
        {
            string path = Path.Combine(dir, name.Name + ".dll");
            if (!File.Exists(path)) return null;
            byte[] bytes = File.ReadAllBytes(path);
            int pe = BitConverter.ToInt32(bytes, 0x3C);
            ushort machine = BitConverter.ToUInt16(bytes, pe + 4);
            if (machine == 0x8664 && RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
            {
                bytes[pe + 4] = 0x64; bytes[pe + 5] = 0xAA;      // IMAGE_FILE_MACHINE_ARM64
            }
            return ctx.LoadFromStream(new MemoryStream(bytes));
        };
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        return Spike.Run();
    }
}

static partial class Spike
{
    static int _pass, _fail;

    public static void Section(string title) => SC.WriteLine($"\n== {title}");
    public static void Info(string text) => SC.WriteLine("INFO " + text);
    public static void Check(string name, bool ok, string? detail = null)
    {
        if (ok) _pass++; else _fail++;
        SC.WriteLine($"{(ok ? "PASS" : "FAIL")} {name}{(string.IsNullOrEmpty(detail) ? "" : " | " + detail)}");
    }

    const string Head = "#include <kfs.h>\n#include <purrtty.h>\n";

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Run()
    {
        Section("catalog self-check (before touching the game)");
        var problems = ServiceCatalog.SelfCheck();
        Check("catalog self-check", problems.Count == 0, problems.Count == 0 ? $"{ServiceCatalog.Services.Length} services, {ServiceCatalog.Enums.Length} enum groups" : string.Join("; ", problems));

        string stockKfsH = KSA.Kfs.KC.SystemHeaders.Files["kfs.h"];
        KfsInstall.Install();
        Check("LoadEmbedded patched before KfsIntegration.Tables was created", !KfsInstall.TablesAlreadyCreatedBeforePatch);

        Guard(RowsLoad);
        Guard(AllServices);
        Guard(Rescan);
        Guard(Blink);
        Guard(LightShow);
        Guard(Deploy);
        Guard(ActuatorSweep);
        Guard(Rejections);
        Guard(Kasm);
        Guard(TwoVehicles);
        Guard(ReadBackTiming);
        Guard(Bench);
        Guard(() => EnumExperiments(stockKfsH));
        Guard(FillerVisibility);
        Guard(KincWorkspace);
        Guard(HeaderExperiments);
        Guard(CostExperiments);
        Guard(MistakeExperiments);
        Guard(LoaderRejections);
        Guard(AugmentFallback);

        Section("base-method postfix vs VehicleComputer");
        Info("HostComputer.ApplyRequest postfix calls by computer type: " + string.Join(", ", PurrRouter.BenchPostCalls.Select(kv => $"{kv.Key}={kv.Value}")));
        Check("the base postfix never fired for a VehicleComputer (no double handling)", PurrRouter.BenchPostCalls.GetValueOrDefault(nameof(VehicleComputer)) == 0
              && PurrRouter.AppliedIds.Values.Sum() > 0);

        Section("router faults");
        var faults = PurrRouter.Faults.ToArray();
        Check("no exception escaped into a patch body", faults.Length == 0, string.Join(" || ", faults.Take(3)));

        SC.WriteLine($"\n{_pass} passed, {_fail} failed");
        return _fail == 0 ? 0 : 1;
    }

    static void Guard(Action a)
    {
        try { a(); }
        catch (Exception e) { Check($"{a.Method.Name} ran without an exception", false, e.GetType().Name + ": " + e.Message.Split('\n')[0] + (e is KcCompileException ? "\n" + e.Message : "")); }
    }

    // ------------------------------------------------------------------ rows

    static void RowsLoad()
    {
        Section("tables: do the rows load?");
        var t = KfsSim.Tables;
        Info($"tables origin={t.Origin} services={t.Services.Count} service_table_version={t.ServiceTableVersion} enums={t.EnumsData.Enums.Count}");
        Check("augmented tables are in use (no fallback)", t.Origin == KfsInstall.Origin, KfsInstall.AugmentFailure);
        var missing = ServiceCatalog.Services.Where(s => !t.Services.ContainsKey(s.Id)).Select(s => s.Name).ToList();
        Check("every catalog row loaded", missing.Count == 0, missing.Count == 0 ? $"{ServiceCatalog.Services.Length} rows" : "missing " + string.Join(",", missing));
        var bad = ServiceCatalog.Services.Where(s => t.Services.TryGetValue(s.Id, out var i) &&
            (i.ArgCells != s.ArgTypes.Length || i.ResultCells != s.ResultTypes.Length || i.Name != s.Name || i.CostClass != s.Cost)).Select(s => s.Name).ToList();
        Check("loaded rows match the catalog (cells, results, name, cost)", bad.Count == 0, string.Join(",", bad));
        var color = t.Services[0x5014];
        Info($"purr_light_set_color: argument names [{string.Join(",", color.ArgumentNames!)}], cell->argument [{string.Join(",", color.ArgumentOf!)}], domains [{string.Join(",", color.Domains!)}]");
        Check("vec3 argument groups as one kc name (light, rgb)", color.ArgumentNames!.SequenceEqual(new[] { "light", "rgb" }) && color.ArgumentOf!.SequenceEqual(new[] { 0, 1, 1, 1 }));
        var enumsOk = ServiceCatalog.Enums.Where(e => e.TableName != null).All(e => t.EnumsData.Enums.TryGetValue(e.TableName!, out var v) && v.SequenceEqual(e.Values));
        Check("mod enumerations are in enums.json", enumsOk, string.Join(", ", ServiceCatalog.Enums.Where(e => e.TableName != null).Select(e => $"{e.TableName}[{e.Values.Count}]")));
        foreach (var d in new[] { "enum:purr_light_prop[0..7]", "enum:purr_light_prop[16..23]", "enum:purr_deploy_prop[16..18]", "enum:purr_actuator_prop[0..3]" })
            Info($"EnumDomain({d}) = {t.EnumDomain(d)}");
        Info("getter domains: " + string.Join(", ", ServiceCatalog.Services.Where(s => s.Name.Contains("_get")).Select(s => $"{s.Name}={t.Services[s.Id].Domains![1]}")));
        Info("purr_light_prop list: [" + string.Join(",", t.EnumsData.Enums["purr_light_prop"]) + "]");
    }

    // ------------------------------------------------------------------ every service

    static void AllServices()
    {
        Section("every service from one kC program");
        PurrRouter.IssuedIds.Clear(); PurrRouter.AppliedIds.Clear();
        var sim = KfsSim.Create("all", lights: 2, deployables: 2, actuators: 1, p => p.Lights[1].HasSwitch = false);
        sim.LoadKc(Head + """
#pragma kfs tick_hz 10
#pragma kfs task step rate_hz=10
uint32_t n;
vec3 rgb;
void step(void) {
    uint32_t k = n;
    n = n + 1u;
    if (k == 0u) {
        kfs_logf("all info gen=%u dirty=%u lights=%u deploy=%u act=%u",
                 purr_parts_info(PURR_PARTS_GENERATION), purr_parts_info(PURR_PARTS_DIRTY),
                 purr_parts_info(PURR_PARTS_LIGHTS), purr_parts_info(PURR_PARTS_DEPLOYABLES),
                 purr_parts_info(PURR_PARTS_ACTUATORS));
        kfs_logf("all counts lights=%u deploy=%u act=%u", purr_light_count(), purr_deploy_count(), purr_actuator_count());
        purr_parts_rescan();
        purr_parts_describe(PURR_KIND_ALL);
    } else if (k == 2u) {
        kfs_logf("all light0 present=%u switch=%u on=%u lit=%u type=%u part=%u group=%u ovr=%u",
                 purr_light_geti(0u, PURR_LIGHT_PRESENT), purr_light_geti(0u, PURR_LIGHT_HAS_SWITCH),
                 purr_light_geti(0u, PURR_LIGHT_ON), purr_light_geti(0u, PURR_LIGHT_LIT),
                 purr_light_geti(0u, PURR_LIGHT_TYPE), purr_light_geti(0u, PURR_LIGHT_PART_ID),
                 purr_light_geti(0u, PURR_LIGHT_SWITCH_GROUP), purr_light_geti(0u, PURR_LIGHT_OVERRIDDEN));
        kfs_logf("all light0 int=%.2f range=%.1f rgb=%.2f,%.2f,%.2f cone=%.2f,%.2f stock=%.2f",
                 purr_light_getf(0u, PURR_LIGHT_INTENSITY), purr_light_getf(0u, PURR_LIGHT_RANGE_M),
                 purr_light_getf(0u, PURR_LIGHT_COLOR_R), purr_light_getf(0u, PURR_LIGHT_COLOR_G),
                 purr_light_getf(0u, PURR_LIGHT_COLOR_B), purr_light_getf(0u, PURR_LIGHT_INNER_RAD),
                 purr_light_getf(0u, PURR_LIGHT_OUTER_RAD), purr_light_getf(0u, PURR_LIGHT_STOCK_INTENSITY));
        purr_light_set_on(0u, 1u);
        rgb.x = 0.1f; rgb.y = 0.2f; rgb.z = 0.3f;
        purr_light_set_color(0u, rgb);
        purr_light_set_cone(0u, 0.1f, 0.5f);
        purr_light_set_intensity(0u, 3.0f);
        purr_light_set_range(0u, 40.0f);
    } else if (k == 4u) {
        kfs_logf("all after-set on=%u ovr=%u int=%.2f range=%.1f r=%.2f inner=%.2f",
                 purr_light_geti(0u, PURR_LIGHT_ON), purr_light_geti(0u, PURR_LIGHT_OVERRIDDEN),
                 purr_light_getf(0u, PURR_LIGHT_INTENSITY), purr_light_getf(0u, PURR_LIGHT_RANGE_M),
                 purr_light_getf(0u, PURR_LIGHT_COLOR_R), purr_light_getf(0u, PURR_LIGHT_INNER_RAD));
        purr_light_reset(0u);
        purr_lights_set_all(0u);
        purr_deploy_set(0u, 1u);
        purr_deploy_toggle(1u);
        purr_actuator_set(0u, 0.5f);
    } else if (k == 6u) {
        kfs_logf("all after-reset on=%u ovr=%u int=%.2f gen=%u",
                 purr_light_geti(0u, PURR_LIGHT_ON), purr_light_geti(0u, PURR_LIGHT_OVERRIDDEN),
                 purr_light_getf(0u, PURR_LIGHT_INTENSITY), purr_parts_info(PURR_PARTS_GENERATION));
    } else if (k == 12u) {
        kfs_logf("all deploy0 present=%u state=%u cmd=%u moving=%u part=%u links=%u pos=%.2f tgt=%.2f",
                 purr_deploy_geti(0u, PURR_DEPLOY_PRESENT), purr_deploy_geti(0u, PURR_DEPLOY_STATE),
                 purr_deploy_geti(0u, PURR_DEPLOY_COMMANDED), purr_deploy_geti(0u, PURR_DEPLOY_MOVING),
                 purr_deploy_geti(0u, PURR_DEPLOY_PART_ID), purr_deploy_geti(0u, PURR_DEPLOY_LINKS),
                 purr_deploy_getf(0u, PURR_DEPLOY_POSITION), purr_deploy_getf(0u, PURR_DEPLOY_TARGET));
        kfs_logf("all deploy1 state=%u cmd=%u dur=%.2f", purr_deploy_geti(1u, PURR_DEPLOY_STATE),
                 purr_deploy_geti(1u, PURR_DEPLOY_COMMANDED), purr_deploy_getf(1u, PURR_DEPLOY_DURATION_S));
        kfs_logf("all act0 present=%u moving=%u part=%u links=%u pos=%.2f tgt=%.2f dur=%.2f",
                 purr_actuator_geti(0u, PURR_ACTUATOR_PRESENT), purr_actuator_geti(0u, PURR_ACTUATOR_MOVING),
                 purr_actuator_geti(0u, PURR_ACTUATOR_PART_ID), purr_actuator_geti(0u, PURR_ACTUATOR_LINKS),
                 purr_actuator_getf(0u, PURR_ACTUATOR_POSITION), purr_actuator_getf(0u, PURR_ACTUATOR_TARGET),
                 purr_actuator_getf(0u, PURR_ACTUATOR_DURATION_S));
        kfs_logf("all consts rejected=%u STATE_DEPLOYING=%u TYPE_POINT=%u KIND_ACTUATOR=%u",
                 sys.rejected_requests, PURR_STATE_DEPLOYING, PURR_LIGHT_TYPE_POINT, PURR_KIND_ACTUATOR);
    }
}
""");
        Info($"compiled: image {sim.LastCompile!.Image!.Length} B, WCET {string.Join(", ", sim.LastCompile.Report!.Wcet.Select(w => $"{w.Task} {w.Cycles}/{w.Budget}"))}, diagnostics {sim.LastCompile.Diagnostics.Count}");
        foreach (var d in sim.LastCompile.Diagnostics) Info("  diag " + d);
        sim.Run(1.5);
        SC.WriteLine(sim.Dump());
        var seen = PurrRouter.AppliedIds.Keys.ToHashSet();
        var notSeen = ServiceCatalog.Services.Where(s => !seen.Contains(s.Id)).Select(s => s.Name).ToList();
        Check("all 22 services reached ApplyRequest", notSeen.Count == 0, notSeen.Count == 0 ? null : "missing " + string.Join(",", notSeen));
        var resultIds = ServiceCatalog.Services.Where(s => s.HasResult).Select(s => s.Id).ToList();
        Check("every result-bearing service was answered by IssueResult", resultIds.All(PurrRouter.IssuedIds.ContainsKey), $"{resultIds.Count} getters");
        string log = string.Join("\n", sim.LogText);
        Check("parts_info/counts read the sim", log.Contains("all info gen=1 dirty=0 lights=2 deploy=2 act=1") && log.Contains("all counts lights=2 deploy=2 act=1"));
        Check("rescan bumped the generation; describe wrote to the program log", log.Contains("gen=2") && log.Contains("parts gen 2: 2 lights") && log.Contains("light 1: part 101 spot switch none"));
        Check("light getters (stock values)", log.Contains("all light0 present=1 switch=1 on=0 lit=0 type=0 part=100 group=0 ovr=0")
              && log.Contains("all light0 int=2.00 range=30.0 rgb=1.00,0.90,0.80 cone=0.30,0.60 stock=2.00"));
        Check("setters visible two ticks later", log.Contains("all after-set on=1 ovr=1 int=3.00 range=40.0 r=0.10 inner=0.10"));
        Check("reset + set_all", log.Contains("all after-reset on=0 ovr=0 int=2.00 gen=2"));
        // deploy_set in tick 4 is applied by the pump of frame 5 (parts clock 0.4 s), then the world
        // steps; at tick 12 (1.2 s) the 2 s animation has run 0.8 s = 0.40 of its travel.
        Check("deployables and actuator getters", log.Contains("all deploy0 present=1 state=2 cmd=1 moving=1 part=200 links=1 pos=0.40 tgt=1.00")
              && log.Contains("all deploy1 state=2 cmd=1 dur=2.00") && log.Contains("all act0 present=1 moving=0 part=300 links=1 pos=0.50 tgt=0.50 dur=1.00"));
        Check("header constants usable in expressions", log.Contains("all consts rejected=0 STATE_DEPLOYING=2 TYPE_POINT=1 KIND_ACTUATOR=3"));
        PurrRouter.Detach(sim.Computer);
    }

    // ------------------------------------------------------------------ rescan

    static void Rescan()
    {
        Section("rescan: structure changes stay invisible until purr_parts_rescan");
        var sim = KfsSim.Create("rescan", lights: 2);
        sim.Parts.AddLightLater();                         // e.g. a docking added a light part
        sim.LoadKc(Head + """
#pragma kfs tick_hz 10
#pragma kfs task t rate_hz=10
uint32_t n;
void t(void) {
    uint32_t k = n;
    n = n + 1u;
    if (k == 0u || k == 3u) {
        kfs_logf("rescan k=%u gen=%u dirty=%u lights=%u", k, purr_parts_info(PURR_PARTS_GENERATION),
                 purr_parts_info(PURR_PARTS_DIRTY), purr_light_count());
    }
    if (k == 1u) {
        purr_parts_rescan();
    }
}
""");
        sim.Run(0.5);
        Check("dirty and old count before, new generation and count after", sim.LogText.Contains("rescan k=0 gen=1 dirty=1 lights=2") && sim.LogText.Contains("rescan k=3 gen=2 dirty=0 lights=3"),
              string.Join(" | ", sim.LogText.Where(t => t.StartsWith("rescan"))));
        PurrRouter.Detach(sim.Computer);
    }

    // ------------------------------------------------------------------ blink

    static void Blink()
    {
        Section("blink: light 0 toggles every 5 ticks");
        var sim = KfsSim.Create("blink", lights: 3, configure: p => p.Lights[2].SwitchGroup = 0);
        sim.LoadKc(Head + """
#pragma kfs tick_hz 10
#pragma kfs task blink rate_hz=10
uint32_t n;
uint32_t on;
void blink(void) {
    if (n % 5u == 0u) {
        on = on ^ 1u;
        purr_light_set_on(0u, on);
    }
    n = n + 1u;
}
""");
        var l0 = new List<bool>(); var l1 = new List<bool>(); var l2 = new List<bool>();
        sim.Run(3.0, 0.1, s => { l0.Add(s.Parts.Lights[0].On); l1.Add(s.Parts.Lights[1].On); l2.Add(s.Parts.Lights[2].On); });
        // Command in tick k (frame k) is applied by the pump at the start of frame k+1.
        var expected = Enumerable.Range(0, l0.Count).Select(f => f >= 1 && ((f - 1) / 5) % 2 == 0).ToList();
        string Bits(List<bool> b) => string.Concat(b.Select(x => x ? '1' : '0'));
        Info($"frame-by-frame light0 {Bits(l0)}");
        Check("light 0 on/off sequence", l0.SequenceEqual(expected), "expected " + Bits(expected));
        Check("light 2 (same switch group) follows light 0", l2.SequenceEqual(l0));
        Check("light 1 (own switch) untouched", l1.All(x => !x));
        PurrRouter.Detach(sim.Computer);
    }

    // ------------------------------------------------------------------ light show

    static void LightShow()
    {
        Section("light show: colour/cone/intensity on light 1, read back, clamp, reset");
        var sim = KfsSim.Create("show", lights: 3);
        sim.LoadKc(Head + """
#pragma kfs tick_hz 10
#pragma kfs task show rate_hz=10
uint32_t n;
vec3 c;
void show(void) {
    uint32_t k = n;
    n = n + 1u;
    if (k == 1u) {
        c.x = 1.0f; c.y = 0.5f; c.z = 0.25f;
        purr_light_set_color(1u, c);
        purr_light_set_cone(1u, 0.2f, 0.6f);
        purr_light_set_intensity(1u, 5.0f);
        purr_light_set_range(1u, 25.0f);
    } else if (k == 3u) {
        kfs_logf("show set rgb=%.3f,%.3f,%.3f cone=%.3f,%.3f int=%.3f range=%.3f ovr=%u",
                 purr_light_getf(1u, PURR_LIGHT_COLOR_R), purr_light_getf(1u, PURR_LIGHT_COLOR_G),
                 purr_light_getf(1u, PURR_LIGHT_COLOR_B), purr_light_getf(1u, PURR_LIGHT_INNER_RAD),
                 purr_light_getf(1u, PURR_LIGHT_OUTER_RAD), purr_light_getf(1u, PURR_LIGHT_INTENSITY),
                 purr_light_getf(1u, PURR_LIGHT_RANGE_M), purr_light_geti(1u, PURR_LIGHT_OVERRIDDEN));
    } else if (k == 4u) {
        c.x = 2.0f; c.y = 0.0f; c.z = 0.0f;
        purr_light_set_color(1u, c);
        purr_light_set_cone(1u, 2.0f, 3.0f);
    } else if (k == 6u) {
        kfs_logf("show clamp rgb=%.3f,%.3f,%.3f cone=%.7f,%.7f",
                 purr_light_getf(1u, PURR_LIGHT_COLOR_R), purr_light_getf(1u, PURR_LIGHT_COLOR_G),
                 purr_light_getf(1u, PURR_LIGHT_COLOR_B), purr_light_getf(1u, PURR_LIGHT_INNER_RAD),
                 purr_light_getf(1u, PURR_LIGHT_OUTER_RAD));
    } else if (k == 7u) {
        purr_light_reset(1u);
    } else if (k == 9u) {
        kfs_logf("show reset rgb=%.3f,%.3f,%.3f cone=%.3f,%.3f int=%.3f range=%.3f ovr=%u",
                 purr_light_getf(1u, PURR_LIGHT_COLOR_R), purr_light_getf(1u, PURR_LIGHT_COLOR_G),
                 purr_light_getf(1u, PURR_LIGHT_COLOR_B), purr_light_getf(1u, PURR_LIGHT_INNER_RAD),
                 purr_light_getf(1u, PURR_LIGHT_OUTER_RAD), purr_light_getf(1u, PURR_LIGHT_INTENSITY),
                 purr_light_getf(1u, PURR_LIGHT_RANGE_M), purr_light_geti(1u, PURR_LIGHT_OVERRIDDEN));
    }
}
""");
        SimLight? at3 = null, at6 = null;
        static SimLight Copy(SimLight l) => (SimLight)typeof(object).GetMethod("MemberwiseClone", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(l, null)!;
        sim.Run(1.2, 0.1, s => { if (s.Frame == 3) at3 = Copy(s.Parts.Lights[1]); if (s.Frame == 6) at6 = Copy(s.Parts.Lights[1]); });
        SC.WriteLine(sim.Dump());
        Check("sim state after the overrides", at3 is { R: 1f, G: 0.5f, B: 0.25f, Inner: 0.2f, Outer: 0.6f, Intensity: 5f, RangeM: 25f, Overridden: true });
        Check("logged read-back of the overrides", sim.LogText.Contains("show set rgb=1.000,0.500,0.250 cone=0.200,0.600 int=5.000 range=25.000 ovr=1"));
        Check("host clamps colour to [0,1] and the cone to outer<=1.5697963, inner<=outer",
              at6 is { R: 1f, G: 0f, B: 0f } && at6.Outer == SimVehicleParts.MaxOuterRad && at6.Inner == at6.Outer
              && sim.LogText.Contains("show clamp rgb=1.000,0.000,0.000 cone=1.5697963,1.5697963"));
        var l = sim.Parts.Lights[1];
        Check("reset restores stock values (sim)", l is { R: 1f, G: 0.9f, B: 0.8f, Inner: 0.3f, Outer: 0.6f, Intensity: 2f, RangeM: 30f, Overridden: false });
        Check("reset restores stock values (logged)", sim.LogText.Contains("show reset rgb=1.000,0.900,0.800 cone=0.300,0.600 int=2.000 range=30.000 ovr=0"));
        Check("other lights untouched", !sim.Parts.Lights[0].Overridden && !sim.Parts.Lights[2].Overridden);
        PurrRouter.Detach(sim.Computer);
    }

    // ------------------------------------------------------------------ deploy

    static void Deploy()
    {
        Section("deploy: purr_deploy_set(0, 1) and poll the state");
        var sim = KfsSim.Create("deploy", deployables: 1, configure: p => p.Deployables[0].DurationS = 2.0);
        sim.LoadKc(Head + """
#pragma kfs tick_hz 10
#pragma kfs task dep rate_hz=10
uint32_t n;
uint32_t last = 99u;
void dep(void) {
    uint32_t k = n;
    uint32_t s;
    n = n + 1u;
    if (k == 1u) {
        purr_deploy_set(0u, 1u);
    }
    s = purr_deploy_geti(0u, PURR_DEPLOY_STATE);
    if (s != last) {
        kfs_logf("deploy k=%u state=%u pos=%.2f", k, s, purr_deploy_getf(0u, PURR_DEPLOY_POSITION));
        last = s;
        if (s == PURR_STATE_DEPLOYED) {
            kfs_logf("deployed at tick %u", k);
        }
    }
}
""");
        sim.Run(3.0);
        SC.WriteLine(sim.Dump());
        var rx = new Regex(@"^deploy k=(\d+) state=(\d+)");
        var seq = sim.LogText.Select(t => rx.Match(t)).Where(m => m.Success).Select(m => (K: int.Parse(m.Groups[1].Value), S: int.Parse(m.Groups[2].Value))).ToList();
        Check("program saw retracted -> deploying -> deployed", seq.Select(x => x.S).SequenceEqual(new[] { 1, 2, 0 }), string.Join(" ", seq.Select(x => $"k{x.K}:{x.S}")));
        int ticks = seq.Count == 3 ? seq[2].K - 1 : -1;
        Check("command tick -> deployed tick = duration (20 ticks at 10 Hz)", ticks == 20, $"{ticks} ticks");
        var applied = sim.Parts.Applied.First(a => a.What.StartsWith("deploy 0 set"));
        var done = sim.Parts.DeployHistory.First(h => h.State == DeployState.Deployed);
        var hist = string.Join(" ", sim.Parts.DeployHistory.Select(h => $"{h.TimeS:0.0}s:{h.State}"));
        Check("sim: applied -> deployed took exactly duration simulated seconds", Math.Abs(done.TimeS - applied.TimeS - 2.0) < 1e-9, $"applied {applied.TimeS:0.0}s, history {hist}");
        PurrRouter.Detach(sim.Computer);
    }

    // ------------------------------------------------------------------ actuator sweep

    static void ActuatorSweep()
    {
        Section("actuator sweep: triangle wave on actuator 0");
        var sim = KfsSim.Create("sweep", actuators: 1, configure: p => p.Actuators[0].DurationS = 1.0);
        sim.LoadKc(Head + """
#pragma kfs tick_hz 10
#pragma kfs task sweep rate_hz=10
uint32_t n;
float cmd;
void sweep(void) {
    uint32_t k = n;
    float ph;
    n = n + 1u;
    ph = (float)(k % 40u) / 20.0f;
    if (ph < 1.0f) { cmd = ph; } else { cmd = 2.0f - ph; }
    if (k % 5u == 0u) {
        kfs_logf("sweep k=%u cmd=%.3f pos=%.3f", k, cmd, purr_actuator_getf(0u, PURR_ACTUATOR_POSITION));
    }
    purr_actuator_set(0u, cmd);
}
""");
        double maxSimErr = 0;
        sim.Run(8.0, 0.1, s => { if (s.Frame >= 2) { var a = s.Parts.Actuators[0]; maxSimErr = Math.Max(maxSimErr, Math.Abs(a.PositionS - a.GoalS)); } });
        static float Tri(int k) { float ph = (k % 40) / 20.0f; return ph < 1f ? ph : 2f - ph; }
        var rx = new Regex(@"^sweep k=(\d+) cmd=([\d.]+) pos=([\d.]+)");
        var rows = sim.LogText.Select(t => rx.Match(t)).Where(m => m.Success)
            .Select(m => (K: int.Parse(m.Groups[1].Value), Cmd: double.Parse(m.Groups[2].Value), Pos: double.Parse(m.Groups[3].Value))).ToList();
        Info("logged: " + string.Join(" ", rows.Take(12).Select(r => $"k{r.K}:{r.Cmd:0.00}/{r.Pos:0.00}")) + " ...");
        double maxErr = rows.Where(r => r.K > 0).Max(r => Math.Abs(r.Pos - Tri(r.K - 1)));
        Check("position read in tick k equals the command of tick k-1", rows.Count >= 15 && maxErr < 0.0015, $"{rows.Count} samples, max |pos - cmd(k-1)| = {maxErr:0.0000}");
        Check("sim position reaches each goal within the frame (wave slope 0.5/s < slew 1/s)", maxSimErr < 1e-9, $"max {maxSimErr:0.000000}");
        PurrRouter.Detach(sim.Computer);
    }

    // ------------------------------------------------------------------ rejections

    static void Rejections()
    {
        Section("rejections: index, enum selector, NaN, negative, no switch");
        var sim = KfsSim.Create("rej", lights: 2, deployables: 1, actuators: 1, p => p.Lights[1].HasSwitch = false);
        sim.LoadKc(Head + """
#pragma kfs tick_hz 10
#pragma kfs task rej rate_hz=10
uint32_t nanbits;          /* kC v1 has no unions (KC-E641): reinterpret through a pointer */
uint32_t n;
uint32_t r0;
void rej(void) {
    uint32_t k = n;
    n = n + 1u;
    if (k == 1u) {
        r0 = sys.rejected_requests;
        purr_light_set_on(99u, 1u);
        purr_deploy_set(7u, 1u);
        purr_actuator_set(5u, 0.5f);
    } else if (k == 2u) {
        kfs_logf("rej range before=%u after=%u", r0, sys.rejected_requests);
        r0 = sys.rejected_requests;
        kfs_logf("rej enum geti(INTENSITY)=%u getf(ON)=%.3f geti(9)=%u geti(99)=%u",
                 purr_light_geti(0u, PURR_LIGHT_INTENSITY), purr_light_getf(0u, PURR_LIGHT_ON),
                 purr_light_geti(0u, 9u), purr_light_geti(0u, 99u));
        kfs_logf("rej enum same-tick delta=%u (controls: geti(ON)=%u getf(INTENSITY)=%.3f getf(9)=%.3f)",
                 sys.rejected_requests - r0, purr_light_geti(0u, PURR_LIGHT_ON),
                 purr_light_getf(0u, PURR_LIGHT_INTENSITY), purr_light_getf(0u, 9u));
    } else if (k == 3u) {
        r0 = sys.rejected_requests;
        nanbits = 0x7FC00000u;
        purr_actuator_set(0u, *(float *)&nanbits);
        purr_light_set_intensity(0u, -1.0f);
        purr_light_set_on(1u, 1u);
        kfs_logf("rej nan/neg same-tick delta=%u", sys.rejected_requests - r0);
    } else if (k == 4u) {
        kfs_logf("rej next-tick delta=%u probe=%u", sys.rejected_requests - r0, purr_light_geti(99u, PURR_LIGHT_PRESENT));
    }
}
""");
        sim.Run(0.6);
        SC.WriteLine(sim.Dump());
        var log = sim.LogText.ToList();
        bool Has(string s) => log.Any(t => t.Contains(s, StringComparison.Ordinal));
        Check("host rejection text for out-of-range indices", Has("rejected purr_light_set_on: light 99 out of range (this vehicle has 2)")
              && Has("rejected purr_deploy_set: deployable 7 out of range") && Has("rejected purr_actuator_set: actuator 5 out of range"));
        var m = log.Select(t => Regex.Match(t, @"rej range before=(\d+) after=(\d+)")).FirstOrDefault(x => x.Success);
        Check("sys.rejected_requests counts host rejections (visible next tick)", m != null && int.Parse(m.Groups[2].Value) - int.Parse(m.Groups[1].Value) == 3, m?.Value);
        foreach (var t in log.Where(t => t.Contains("argument 1 = ") && t.Contains("purr_light_get"))) Info("selector rejection: " + t);
        Check("geti(PURR_LIGHT_INTENSITY) rejected at issue by the [0..7] sub-range", Has("rejected purr_light_geti argument 1 = 16: not in purr_light_prop[0..7]"));
        Check("getf(PURR_LIGHT_ON) rejected at issue by the [16..23] sub-range", Has("rejected purr_light_getf argument 1 = 2: not in purr_light_prop[16..23]"));
        Check("filler 9 and out-of-list 99 rejected by geti; filler 9 by getf", Has("rejected purr_light_geti argument 1 = 9: not in purr_light_prop[0..7]")
              && Has("rejected purr_light_geti argument 1 = 99: not in purr_light_prop[0..7]") && Has("rejected purr_light_getf argument 1 = 9: not in purr_light_prop[16..23]"));
        Check("rejected selectors read 0, valid ones still work, all counted in the same tick",
              Has("rej enum geti(INTENSITY)=0 getf(ON)=0.000 geti(9)=0 geti(99)=0") && Has("rej enum same-tick delta=4 (controls: geti(ON)=0 getf(INTENSITY)=2.000 getf(9)=0.000)"));
        Check("NaN to purr_actuator_set rejected at issue (finite)", Has("rejected purr_actuator_set argument 1 = nan: not finite"), log.FirstOrDefault(t => t.Contains("argument 1 = nan")));
        Check("negative intensity rejected at issue (finite:>=0)", Has("rejected purr_light_set_intensity argument 1 = -1"), log.FirstOrDefault(t => t.Contains("set_intensity argument")));
        Check("light without a switch rejected by the host", Has("rejected purr_light_set_on: light 1 has no switch"));
        Check("issue-time rejections count in the same tick; host rejection next tick", Has("rej nan/neg same-tick delta=2") && Has("rej next-tick delta=3 probe=0"));
        Check("rejected requests never reached the parts", sim.Parts.Actuators[0].GoalS == 0 && sim.Parts.Lights[0].Intensity == 2f && !sim.Parts.Lights[1].On && sim.Parts.Applied.Count == 0,
              $"applied: {string.Join(",", sim.Parts.Applied.Select(a => a.What))}");
        PurrRouter.Detach(sim.Computer);
    }

    // ------------------------------------------------------------------ kASM

    static void Kasm()
    {
        Section("kASM: SYSCALL SVC_PURR_LIGHT_SET_ON");
        var sim = KfsSim.Create("kasm", lights: 2);
        sim.LoadKasm("""
.include <kfs.kinc>
.tick_hz 10
.global done, 4
.global count, 4

.task main rate_hz=10
.func main
    LDG   done
    JNZ   out
    SYSCALL SVC_PURR_LIGHT_COUNT    ; pops 0, pushes 1
    STG   count
    PUSH8 1                         ; u32 light (deepest)
    PUSH8 1                         ; u32 on
    SYSCALL SVC_PURR_LIGHT_SET_ON   ; pops 2
    PUSH8 1
    STG   done
out:
    RET   0
.endfunc
""");
        sim.Run(0.3);
        Check("kASM SYSCALL reached the sim: light 1 on, light 0 off", sim.Parts.Lights[1].On && !sim.Parts.Lights[0].On, sim.Dump());
        PurrRouter.Detach(sim.Computer);
    }

    // ------------------------------------------------------------------ two vehicles

    static void TwoVehicles()
    {
        Section("same kC on two vehicles with different light counts");
        const string src = Head + """
#pragma kfs tick_hz 10
#pragma kfs task t rate_hz=10
uint32_t n;
void t(void) {
    uint32_t c;
    if (n == 0u) {
        c = purr_light_count();
        kfs_logf("vehicle has %u lights", c);
        purr_light_set_on(c - 1u, 1u);
    }
    n = n + 1u;
}
""";
        var a = KfsSim.Create("veh-a", lights: 2).LoadKc(src);
        var b = KfsSim.Create("veh-b", lights: 5).LoadKc(src);
        KfsSim.RunTogether(new[] { a, b }, 0.3);
        Check("vehicle A logs 2 lights, vehicle B logs 5", a.LogText.Contains("vehicle has 2 lights") && b.LogText.Contains("vehicle has 5 lights"));
        Check("each vehicle switched its own last light", a.Parts.Lights[1].On && !a.Parts.Lights[0].On && b.Parts.Lights[4].On && b.Parts.Lights.Take(4).All(l => !l.On));
        PurrRouter.Detach(a.Computer); PurrRouter.Detach(b.Computer);
    }

    // ------------------------------------------------------------------ read-back timing

    static void ReadBackTiming()
    {
        Section("read-back timing: set in tick 2, read every tick");
        const string src = Head + """
#pragma kfs tick_hz 10
#pragma kfs task rb rate_hz=10
uint32_t n;
void rb(void) {
    uint32_t k = n;
    n = n + 1u;
    if (k == 2u) {
        purr_light_set_on(0u, 1u);
    }
    if (k >= 2u && k <= 6u) {
        kfs_logf("rb k=%u on=%u", k, purr_light_geti(0u, PURR_LIGHT_ON));
    }
}
""";
        foreach (double dt in new[] { 0.1, 0.05, 0.2, 0.3 })
        {
            var sim = KfsSim.Create($"rb-{dt}", lights: 1).LoadKc(src);
            var ticksPerFrame = new List<int>();
            sim.Run(1.2, dt, afterDispatch: (_, dr) => ticksPerFrame.Add(dr.TicksRun));
            var first = sim.LogText.Select(t => Regex.Match(t, @"^rb k=(\d+) on=1")).FirstOrDefault(x => x.Success);
            int k = first == null ? -1 : int.Parse(first.Groups[1].Value);
            Info($"frame dt {dt:0.00}s (ticks per dispatch {string.Join("", ticksPerFrame.Take(8))}...): set in tick 2, first on=1 in tick {k} -> {k - 2} tick(s) later; log: {string.Join(" | ", sim.LogText.Where(t => t.StartsWith("rb")))}");
            if (dt == 0.1) Check("one tick per frame: read-back in tick k+1", k == 3);
            if (dt == 0.05) Check("frames faster than ticks: still tick k+1", k == 3);
            PurrRouter.Detach(sim.Computer);
        }
    }

    // ------------------------------------------------------------------ bench

    static void Bench()
    {
        Section("bench (BenchMachine, no vehicle) with the HostComputer.ApplyRequest postfix");
        var t = KfsSim.Tables;
        var r = KfsSim.Compile(Head + """
#pragma kfs tick_hz 10
#pragma kfs task b rate_hz=10
uint32_t n;
void b(void) {
    if (n == 0u) {
        kfs_logf("bench count=%u present=%u intensity=%.2f", purr_light_count(), purr_light_geti(0u, PURR_LIGHT_PRESENT), purr_light_getf(0u, PURR_LIGHT_INTENSITY));
        purr_light_set_on(0u, 1u);
        purr_parts_rescan();
    }
    if (n == 1u) {
        kfs_logf("bench rejected_requests=%u", sys.rejected_requests);
    }
    n = n + 1u;
}
""", t.BenchLayout);
        Check("compiles against the bench layout", r.Ok, string.Join("; ", r.Diagnostics));
        var before = PurrRouter.BenchPostCalls.GetValueOrDefault(nameof(BenchMachine));
        var bench = new BenchMachine(t);
        var lr = bench.LoadBench(r.Image!);
        bench.Start();
        int ran = bench.Tick(3);
        var log = bench.Console.Snapshot().Select(l => l.Text).ToList();
        foreach (var l in log) SC.WriteLine("    bench: " + l);
        Check("bench: results are zero", log.Contains("bench count=0 present=0 intensity=0.00"), $"load {lr.Ok}, ticks {ran}");
        Check("bench: getters claimed silently, setters rejected with the purr text",
              log.Count(l => l.StartsWith("rejected")) == 2 && !log.Any(l => l.Contains("no vehicle to act on"))
              && log.Contains($"rejected purr_light_set_on: {PurrRouter.BenchReason}") && log.Contains($"rejected purr_parts_rescan: {PurrRouter.BenchReason}"));
        Check("bench: the rejections count in sys.rejected_requests", log.Contains("bench rejected_requests=2"));
        Check("bench postfix fired on BenchMachine", PurrRouter.BenchPostCalls.GetValueOrDefault(nameof(BenchMachine)) - before == 5, $"{PurrRouter.BenchPostCalls.GetValueOrDefault(nameof(BenchMachine)) - before} calls (4 getters/setters + 1 rescan)");
    }
}
