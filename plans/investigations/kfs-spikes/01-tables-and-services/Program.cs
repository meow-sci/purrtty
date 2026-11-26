using System.Text;
using System.Text.Json.Nodes;
using HarmonyLib;
using KSA.Kfs.Compiler;
using KSA.Kfs.Host;
using KSA.Kfs.KC;
using KSA.Kfs.Vm;
using KSA.Kfs.Vm.Tables;

var asm = typeof(GameTables).Assembly;
GameTables Aug() => GameTables.Load(file =>
{
    var st = asm.GetManifestResourceStream(GameTables.RESOURCE_PREFIX + file);
    if (st == null || file != "services.json") return st;
    using var r = new StreamReader(st);
    var root = JsonNode.Parse(r.ReadToEnd())!;
    var arr = root["services"]!.AsArray();
    arr.Add(JsonNode.Parse("""
    { "id": 256, "name": "light_set", "kc": "`kfs_light_set(i, on)`", "kscript": "`light_set(i, on)`", "symbol": "SVC_LIGHT_SET", "kind": "request", "args": "`u32 index`, `u32 on`", "arg_types": ["u32", "u32"], "results": "—", "result_types": [], "cost_class": "Light", "requires_stock": null, "arg_domains": ["any", "any"], "host_checks": "—", "summary": "Turns light i on or off." }
    """));
    arr.Add(JsonNode.Parse("""
    { "id": 257, "name": "light_count", "kc": "`kfs_light_count()`", "kscript": "`light_count()`", "symbol": "SVC_LIGHT_COUNT", "kind": "request+result", "args": "—", "arg_types": [], "results": "`u32 count`", "result_types": ["u32"], "cost_class": "Light", "requires_stock": null, "arg_domains": [], "host_checks": "—", "summary": "Returns the number of lights." }
    """));
    return new MemoryStream(Encoding.UTF8.GetBytes(root.ToJsonString()));
}, "spike:augmented");

var h = new Harmony("spike");
var mApply = AccessTools.Method(typeof(VehicleComputer), "ApplyRequest");
var mIssue = AccessTools.Method(typeof(VehicleComputer), "KSA.Kfs.Vm.ISyscallHost.IssueResult");
System.Console.WriteLine($"targets: apply={mApply != null} issue={mIssue != null}");
h.Patch(mApply, postfix: new HarmonyMethod(typeof(P), nameof(P.ApplyPost)));
h.Patch(mIssue, postfix: new HarmonyMethod(typeof(P), nameof(P.IssuePost)));
var tables = Aug();
KcFrontEnd.Register();
SystemHeaders.Files["purrtty.h"] = "#pragma once\r\n#include <kfs.h>\r\nvoid kfs_light_set(uint32_t index, uint32_t on);\r\nuint32_t kfs_light_count(void);\r\n";
SystemHeaders.Builtins.Add("kfs_light_set");
SystemHeaders.Builtins.Add("kfs_light_count");
var fake = new FakeVehicle(tables, thrusters: 4, engines: 1, gimbals: 1);
System.Console.WriteLine($"layout fp={fake.Layout}");

CompileResult Build(string src)
{
    var res = Compiler.Compile(new CompileInput
    {
        Files = new Dictionary<string, string> { ["main.c"] = src },
        Part = tables.BenchPart, Game = tables, Layout = fake.Layout, ProjectName = "spike",
    });
    System.Console.WriteLine($"compile ok={res.Ok} image={(res.Image?.Length ?? -1)} bytes");
    foreach (var d in res.Diagnostics) System.Console.WriteLine("  diag: " + d);
    return res;
}

string[] progs = {
// A: call the new service by its kC name
"""
#include <kfs.h>
#include <purrtty.h>
#pragma kfs tick_hz 10
#pragma kfs task fly rate_hz=10
uint32_t n;
uint32_t lights;
void fly(void) {
    n++;
    if (n == 2u) { kfs_light_set(3u, 1u); }
    if (n == 3u) { lights = kfs_light_count(); kfs_logf("lights %u", lights); }
}
""",
};
foreach (var src in progs)
{
    var res = Build(src);
    if (!res.Ok) continue;
    var lr = fake.Computer.Load(res.Image!, fake.Layout);
    System.Console.WriteLine("load: " + lr);
    fake.Computer.Start(0L);
    for (int f = 0; f < 8; f++)
    {
        long t = (long)(f * 0.1 * 1e9);
        var dr = fake.Dispatch(t, default(TruthSample));
        var reqs = fake.Computer.TakeServiceRequests();
        foreach (var l in fake.Computer.Console.Snapshot()) System.Console.WriteLine($"    log k={l.K} {l.Text}");
        fake.Computer.Console.Clear();
        System.Console.WriteLine($"frame {f}: ticks={dr.TicksRun} under={dr.UnderControl} state={fake.Computer.State} mod=[{string.Join("; ", P.Drain().Select(r => $"id={r.Id} args=({string.Join(",", r.Args)}) results=({(r.Results==null?"":string.Join(",", r.Results))}) task={r.Task}"))}] reqs=[{string.Join("; ", reqs.Select(r => $"id={r.Id} args=({string.Join(",", r.Args)}) results=({(r.Results==null?"":string.Join(",", r.Results))})"))}]");
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
    }
}
