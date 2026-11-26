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
    static readonly string Dir = Environment.GetEnvironmentVariable("KSA_DLL_DIR")
        ?? throw new InvalidOperationException("set KSA_DLL_DIR to the folder holding KSA.dll");

    static int Main()
    {
        // Resolve game assemblies from the untouched snapshot; on arm64 patch the PE machine field IN MEMORY.
        AssemblyLoadContext.Default.Resolving += (ctx, name) =>
        {
            string path = Path.Combine(Dir, name.Name + ".dll");
            if (!File.Exists(path)) return null;
            byte[] bytes = File.ReadAllBytes(path);
            int pe = BitConverter.ToInt32(bytes, 0x3C);
            ushort machine = BitConverter.ToUInt16(bytes, pe + 4);
            if (machine == 0x8664 && RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
            {
                bytes[pe + 4] = 0x64; bytes[pe + 5] = 0xAA;      // IMAGE_FILE_MACHINE_ARM64
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
        h.Patch(AccessTools.Method(typeof(KSA.Kfs.Vm.Tables.GameTables), "LoadEmbedded"), postfix: new HarmonyMethod(typeof(Spike), nameof(LoadEmbeddedPost)));
        h.Patch(AccessTools.Method(typeof(KSA.Kfs.Host.VehicleComputer), "ApplyRequest"), postfix: new HarmonyMethod(typeof(Spike), nameof(ApplyPost)));
        h.Patch(AccessTools.Method(typeof(KSA.Kfs.Host.VehicleComputer), "KSA.Kfs.Vm.ISyscallHost.IssueResult"), postfix: new HarmonyMethod(typeof(Spike), nameof(IssuePost)));

        KSA.Kfs.KC.SystemHeaders.Files["purrtty.h"] = "#pragma once\r\n#include <kfs.h>\r\nvoid purr_light_set_on(uint32_t light, uint32_t on);\r\nuint32_t purr_light_count(void);\r\n";
        KSA.Kfs.KC.SystemHeaders.Builtins.Add("purr_light_set_on");
        KSA.Kfs.KC.SystemHeaders.Builtins.Add("purr_light_count");

        var tables = KSA.Kfs.KfsIntegration.Tables;                       // the game's own lazy accessor
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
        if (built != null) foreach (var d in built.Diagnostics) SC.WriteLine("  diag: " + d);
        SC.WriteLine("select vehicle: " + wb.SelectTarget("vehicle"));
        SC.WriteLine("load: " + wb.Load("lights.c --vehicle"));
        wb.Start();
        for (int f = 0; f < 8; f++)
        {
            wb.Frame(0.1);
            var dr = fake.Dispatch((long)(f * 0.1 * 1e9), default(KSA.Kfs.Host.TruthSample));
            var mod = Drain();
            SC.WriteLine($"frame {f}: ticks={dr.TicksRun} state={fake.Computer.State} mod=[{string.Join("; ", mod.Select(r => $"0x{r.Id:X4}({string.Join(",", r.Args)})"))}]");
        }
        foreach (var l in fake.Computer.Console.Snapshot()) SC.WriteLine($"  program log k={l.K}: {l.Text}");
        SC.WriteLine("workbench out: " + outp.ToString().Replace("\n", " | "));
        Directory.Delete(ws, true);
        return 0;
    }

    static readonly List<KSA.Kfs.Vm.ServiceRequest> Seen = new();
    static List<KSA.Kfs.Vm.ServiceRequest> Drain() { var l = new List<KSA.Kfs.Vm.ServiceRequest>(Seen); Seen.Clear(); return l; }

    public static void ApplyPost(KSA.Kfs.Host.VehicleComputer __instance, KSA.Kfs.Vm.ServiceRequest request, ref bool __result)
    {
        if (!__result && request.Id >= 0x5000 && request.Id <= 0x50FF) { Seen.Add(request); __result = true; }
    }

    public static void IssuePost(KSA.Kfs.Host.VehicleComputer __instance, ushort service, ReadOnlySpan<uint> args, Span<uint> results)
    {
        if (service == 0x5010 && results.Length > 0) results[0] = 3u;
    }

    static bool _inside;
    public static void LoadEmbeddedPost(ref KSA.Kfs.Vm.Tables.GameTables __result)
    {
        if (_inside) return;
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
