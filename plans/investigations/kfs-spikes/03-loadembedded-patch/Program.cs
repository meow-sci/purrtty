using System.Text;
using System.Text.Json.Nodes;
using HarmonyLib;
using KSA.Kfs;
using KSA.Kfs.Vm.Tables;
using SC = System.Console;

// (a) Harmony postfix on the public static GameTables.LoadEmbedded, then read through KfsIntegration.Tables
var h = new Harmony("spike3");
h.Patch(AccessTools.Method(typeof(GameTables), nameof(GameTables.LoadEmbedded)), postfix: new HarmonyMethod(typeof(P), nameof(P.LoadEmbeddedPost)));
var field = AccessTools.Field(typeof(KfsIntegration), "_tables");
SC.WriteLine($"_tables field found={field != null} initially null={field!.GetValue(null) == null}");
var t = KfsIntegration.Tables;
SC.WriteLine($"KfsIntegration.Tables: origin={t.Origin} services={t.Services.Count} has256={t.Services.ContainsKey(256)} rows={t.ServicesData.Services.Count} svcver={t.ServiceTableVersion}");
SC.WriteLine($"same instance on 2nd read={ReferenceEquals(t, KfsIntegration.Tables)} postfix calls={P.Calls}");

static class P
{
    public static int Calls;
    static bool _inside;
    public static void LoadEmbeddedPost(ref GameTables __result)
    {
        Calls++;
        if (_inside) return;                  // our own nested Load must not recurse
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
                root["services"]!.AsArray().Add(JsonNode.Parse("""
                { "id": 256, "name": "light_set", "kc": "`kfs_light_set(i, on)`", "kscript": "`light_set(i, on)`", "symbol": "SVC_LIGHT_SET", "kind": "request", "args": "`u32 index`, `u32 on`", "arg_types": ["u32", "u32"], "results": "—", "result_types": [], "cost_class": "Light", "requires_stock": null, "arg_domains": ["any", "any"], "host_checks": "—", "summary": "Turns light i on or off." }
                """));
                return new MemoryStream(Encoding.UTF8.GetBytes(root.ToJsonString()));
            }, "embedded:KSA+purrtty");
        }
        catch (Exception e) { SC.WriteLine("augment failed, keeping stock: " + e.Message); }
        finally { _inside = false; }
    }
}
