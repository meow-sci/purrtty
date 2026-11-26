using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using HarmonyLib;
using KSA.Kfs.Compiler;
using KSA.Kfs.Vm.Tables;

namespace KfsLspSpike;

/// Everything that touches the game's kFS types.
static class Kfs
{
    public static GameTables Tables = null!;
    public static IoLayout Layout = null!;
    /// kC name (text of the `kc` column before '(', backticks stripped) -> row.
    public static readonly Dictionary<string, ServiceRow> ServicesByName = new(StringComparer.Ordinal);

    public static void Init()
    {
        var h = new Harmony("kfs-lsp-spike");
        h.Patch(AccessTools.Method(typeof(GameTables), "LoadEmbedded"), postfix: new HarmonyMethod(typeof(Kfs), nameof(LoadEmbeddedPost)));

        KSA.Kfs.KC.SystemHeaders.Files["purrtty.h"] =
            "#pragma once\r\n#include <kfs.h>\r\n" +
            "void purr_light_set_on(uint32_t light, uint32_t on);\r\n" +
            "uint32_t purr_light_count(void);\r\n";
        KSA.Kfs.KC.SystemHeaders.Builtins.Add("purr_light_set_on");
        KSA.Kfs.KC.SystemHeaders.Builtins.Add("purr_light_count");
        // A third row that reproduces offline-harness E8 (multi-cell result makes kC lowering THROW),
        // so the server's survival of a compiler exception can be shown. Only <purrtty_crash.h> declares it.
        KSA.Kfs.KC.SystemHeaders.Files["purrtty_crash.h"] = "#pragma once\r\n#include <kfs.h>\r\nvec3 purr_crash_vec(void);\r\n";
        KSA.Kfs.KC.SystemHeaders.Builtins.Add("purr_crash_vec");
        KSA.Kfs.KC.KcFrontEnd.Register();

        Tables = KSA.Kfs.KfsIntegration.Tables;                 // the game's own lazy accessor (patched above)
        Layout = KSA.Kfs.Host.LayoutGenerator.Vehicle("lsp", 4, 1, 1);

        foreach (var row in Tables.ServicesData.Services)
        {
            string? name = KcName(row);
            if (name != null) ServicesByName[name] = row;
        }
    }

    public static string? KcName(ServiceRow row)
    {
        if (row.Reserved || string.IsNullOrWhiteSpace(row.Kc)) return null;
        string kc = row.Kc.Replace("`", "").Trim();
        int p = kc.IndexOf('(');
        string name = (p >= 0 ? kc[..p] : kc).Trim();
        if (name.Length == 0 || !name.All(c => char.IsLetterOrDigit(c) || c == '_')) return null;
        return name;
    }

    public static string Clean(string? s) => string.IsNullOrWhiteSpace(s) ? "—" : s.Trim();

    public readonly record struct Outcome(CompileResult? Result, Exception? Error, double Ms);

    /// Compile one buffer as a one-file project. Never throws.
    public static Outcome Compile(string fileName, string text)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var r = Compiler.Compile(new CompileInput
            {
                Files = new Dictionary<string, string>(StringComparer.Ordinal) { [fileName] = text },
                Part = Tables.BenchPart,
                Game = Tables,
                Layout = Layout,
                ProjectName = fileName,
            });
            return new Outcome(r, null, sw.Elapsed.TotalMilliseconds);
        }
        catch (Exception e)
        {
            return new Outcome(null, e, sw.Elapsed.TotalMilliseconds);
        }
    }

    static bool _inside;
    public static void LoadEmbeddedPost(ref GameTables __result)
    {
        if (_inside) return;
        _inside = true;
        try
        {
            var asm = typeof(GameTables).Assembly;
            __result = GameTables.Load(file =>
            {
                var st = asm.GetManifestResourceStream(GameTables.RESOURCE_PREFIX + file);
                if (st == null || file != "services.json") return st;
                using var r = new StreamReader(st);
                var root = JsonNode.Parse(r.ReadToEnd())!;
                var arr = root["services"]!.AsArray();
                arr.Add(JsonNode.Parse("""{ "id": 20496, "name": "purr_light_count", "kc": "`purr_light_count()`", "kscript": "—", "symbol": "SVC_PURR_LIGHT_COUNT", "kind": "request+result", "args": "—", "arg_types": [], "results": "`u32 count`", "result_types": ["u32"], "cost_class": "Light", "requires_stock": null, "arg_domains": [], "host_checks": "—", "summary": "Number of lights on the vehicle." }"""));
                arr.Add(JsonNode.Parse("""{ "id": 20498, "name": "purr_light_set_on", "kc": "`purr_light_set_on(light, on)`", "kscript": "—", "symbol": "SVC_PURR_LIGHT_SET_ON", "kind": "request", "args": "`u32 light`, `u32 on`", "arg_types": ["u32", "u32"], "results": "—", "result_types": [], "cost_class": "Light", "requires_stock": null, "arg_domains": ["any", "any"], "host_checks": "light index out of range", "summary": "Switches a light on or off." }"""));
                arr.Add(JsonNode.Parse("""{ "id": 20500, "name": "purr_crash_vec", "kc": "`purr_crash_vec()`", "kscript": "—", "symbol": "SVC_PURR_CRASH_VEC", "kind": "request+result", "args": "—", "arg_types": [], "results": "`vec3 v`", "result_types": ["f32", "f32", "f32"], "cost_class": "Light", "requires_stock": null, "arg_domains": [], "host_checks": "—", "summary": "SPIKE ONLY: a 3-cell result, which crashes the kC lowering (offline-harness E8)." }"""));
                return new MemoryStream(Encoding.UTF8.GetBytes(root.ToJsonString()));
            }, "embedded:KSA+purrtty");
        }
        catch (Exception e) { Log.W("augment failed: " + e.Message); }
        finally { _inside = false; }
    }
}
