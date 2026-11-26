using System.Text;
using System.Text.Json.Nodes;
using HarmonyLib;
using KSA.Kfs.Compiler;
using KSA.Kfs.Host;
using KSA.Kfs.KAsm;
using KSA.Kfs.KC;
using KSA.Kfs.Vm;
using KSA.Kfs.Vm.Tables;
using SC = System.Console;

var asm = typeof(GameTables).Assembly;
string Row(int id, string name, string kc, string sym, string kind, string[] at, string[] rt, string[] dom) =>
    $$"""{ "id": {{id}}, "name": "{{name}}", "kc": "`{{kc}}`", "kscript": "`{{name}}()`", "symbol": "{{sym}}", "kind": "{{kind}}", "args": "x", "arg_types": [{{string.Join(",", at.Select(a => $"\"{a}\""))}}], "results": "x", "result_types": [{{string.Join(",", rt.Select(a => $"\"{a}\""))}}], "cost_class": "Light", "requires_stock": null, "arg_domains": [{{string.Join(",", dom.Select(a => $"\"{a}\""))}}], "host_checks": "—", "summary": "spike" }""";
string[] rows = {
    Row(256, "light_set", "kfs_light_set(i, on)", "SVC_LIGHT_SET", "request", new[]{"u32","u32"}, new string[0], new[]{"any","any"}),
    Row(257, "light_count", "kfs_light_count()", "SVC_LIGHT_COUNT", "request+result", new string[0], new[]{"u32"}, new string[0]),
    Row(258, "light_set_color", "kfs_light_set_color(i, rgb)", "SVC_LIGHT_SET_COLOR", "request", new[]{"u32","f32","f32","f32"}, new string[0], new[]{"any","finite:[0,1]","finite:[0,1]","finite:[0,1]"}),
    Row(259, "light_get", "kfs_light_get(i, what)", "SVC_LIGHT_GET", "request+result", new[]{"u32","u32"}, new[]{"f32"}, new[]{"any","any"}),
    Row(260, "light_color", "kfs_light_color(i)", "SVC_LIGHT_COLOR", "request+result", new[]{"u32"}, new[]{"f32","f32","f32"}, new[]{"any"}),
};
GameTables Aug(IEnumerable<string> add) => GameTables.Load(file =>
{
    var st = asm.GetManifestResourceStream(GameTables.RESOURCE_PREFIX + file);
    if (st == null || file != "services.json") return st;
    using var r = new StreamReader(st);
    var root = JsonNode.Parse(r.ReadToEnd())!;
    var arr = root["services"]!.AsArray();
    foreach (var a in add) arr.Add(JsonNode.Parse(a));
    return new MemoryStream(Encoding.UTF8.GetBytes(root.ToJsonString()));
}, "spike:augmented");

var h = new Harmony("spike");
h.Patch(AccessTools.Method(typeof(VehicleComputer), "ApplyRequest"), postfix: new HarmonyMethod(typeof(P), nameof(P.ApplyPost)));
h.Patch(AccessTools.Method(typeof(VehicleComputer), "KSA.Kfs.Vm.ISyscallHost.IssueResult"), postfix: new HarmonyMethod(typeof(P), nameof(P.IssuePost)));

GameTables tables;
try { tables = Aug(rows); SC.WriteLine("tables ok: " + tables.Services.Count); }
catch (Exception e) { SC.WriteLine("TABLES FAILED: " + e.Message); return; }
KcFrontEnd.Register();
SystemHeaders.Files["purrtty.h"] = "#pragma once\r\n#include <kfs.h>\r\nvoid kfs_light_set(uint32_t index, uint32_t on);\r\nuint32_t kfs_light_count(void);\r\nvoid kfs_light_set_color(uint32_t index, vec3 rgb);\r\nfloat kfs_light_get(uint32_t index, uint32_t what);\r\nvec3 kfs_light_color(uint32_t index);\r\n";
foreach (var n in new[]{"kfs_light_set","kfs_light_count","kfs_light_set_color","kfs_light_get","kfs_light_color"}) SystemHeaders.Builtins.Add(n);

void RunKc(string title, string src, int frames = 6)
{
    SC.WriteLine($"=== {title}");
    var fake = new FakeVehicle(tables, thrusters: 4, engines: 1, gimbals: 1);
    var res = Compiler.Compile(new CompileInput { Files = new Dictionary<string, string> { ["main.c"] = src }, Part = tables.BenchPart, Game = tables, Layout = fake.Layout, ProjectName = "spike" });
    SC.WriteLine($"compile ok={res.Ok} image={(res.Image?.Length ?? -1)}");
    foreach (var d in res.Diagnostics) SC.WriteLine("  diag: " + d);
    if (!res.Ok) return;
    SC.WriteLine("load: " + fake.Computer.Load(res.Image!, fake.Layout));
    fake.Computer.Start(0L);
    for (int f = 0; f < frames; f++)
    {
        var dr = fake.Dispatch((long)(f * 0.1 * 1e9), default(TruthSample));
        foreach (var l in fake.Computer.Console.Snapshot()) SC.WriteLine($"    log k={l.K} {l.Text}");
        fake.Computer.Console.Clear();
        foreach (var r in P.Drain()) SC.WriteLine($"    MOD id={r.Id} args=({string.Join(",", r.Args.Select(a => a + "/" + BitConverter.UInt32BitsToSingle(a)))}) results=({(r.Results == null ? "" : string.Join(",", r.Results))})");
        SC.WriteLine($"  frame {f}: ticks={dr.TicksRun} state={fake.Computer.State}");
    }
}

RunKc("float + vec3 args, f32 result", """
#include <kfs.h>
#include <purrtty.h>
#include <vecmat.h>
#pragma kfs tick_hz 10
#pragma kfs task fly rate_hz=10
uint32_t n;
float got;
void fly(void) {
    n++;
    if (n == 1u) kfs_light_set_color(2u, vec3_make(1.0f, 0.5f, 0.25f));
    if (n == 2u) kfs_light_set_color(2u, vec3_make(2.0f, 0.5f, 0.25f));   /* out of domain */
    if (n == 3u) { got = kfs_light_get(1u, 4u); kfs_logf("got %.3f", got); }
}
""");

// kASM path
SC.WriteLine("=== kASM");
{
    var fake = new FakeVehicle(tables, thrusters: 4, engines: 1, gimbals: 1);
    var src = """
.include <kfs.kinc>
.tick_hz 10
.task go rate_hz=10
.func go
    PUSH8 3
    PUSH8 1
    SYSCALL SVC_LIGHT_SET
    RET   0
.endfunc
""";
    var ar = fake.Assemble(src);
    SC.WriteLine($"assemble ok={ar.Ok}");
    foreach (var d in ar.Diagnostics) SC.WriteLine("  diag: " + d);
    if (ar.Ok)
    {
        SC.WriteLine("load: " + fake.Computer.Load(ar.Image!, fake.Layout));
        fake.Computer.Start(0L);
        for (int f = 0; f < 2; f++)
        {
            var dr = fake.Dispatch((long)(f * 0.1 * 1e9), default(TruthSample));
            foreach (var l in fake.Computer.Console.Snapshot()) SC.WriteLine($"    log k={l.K} {l.Text}");
            fake.Computer.Console.Clear();
            foreach (var r in P.Drain()) SC.WriteLine($"    MOD id={r.Id} args=({string.Join(",", r.Args)})");
        }
    }
}

static class P
{
    static readonly List<ServiceRequest> Seen = new();
    public static List<ServiceRequest> Drain() { var l = new List<ServiceRequest>(Seen); Seen.Clear(); return l; }
    public static void ApplyPost(VehicleComputer __instance, ServiceRequest request, ref bool __result)
    {
        if (!__result && request.Id >= 256) { Seen.Add(request); __result = true; }
    }
    public static void IssuePost(VehicleComputer __instance, ushort service, ReadOnlySpan<uint> args, Span<uint> results)
    {
        if (service == 257 && results.Length > 0) results[0] = 7u;
        if (service == 259 && results.Length > 0) results[0] = BitConverter.SingleToUInt32Bits(args.Length * 1.5f + args[0]);
        if (service == 260 && results.Length >= 3) { results[0] = BitConverter.SingleToUInt32Bits(0.1f); results[1] = BitConverter.SingleToUInt32Bits(0.2f); results[2] = BitConverter.SingleToUInt32Bits(0.3f); }
    }
}
