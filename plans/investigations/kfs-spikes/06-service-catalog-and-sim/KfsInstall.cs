// Installs the catalog into the game's kFS toolchain, exactly as the offline-harness program does:
//   1. Harmony postfix on GameTables.LoadEmbedded -> tables rebuilt by GameTables.Load(open, origin)
//      from the embedded streams, with services.json and enums.json augmented from the catalog.
//      Any exception keeps the stock tables (a throwing LoadEmbedded would leave kFS unusable).
//   2. SystemHeaders.Files["purrtty.h"] and SystemHeaders.Builtins.
//   3. The router's two postfixes on VehicleComputer (see PurrRouter).

using System.Reflection;
using System.Text;
using HarmonyLib;
using KSA.Kfs.KC;
using KSA.Kfs.Vm.Tables;

namespace PurrKfs;

public static class KfsInstall
{
    public const string Origin = "embedded:KSA+purrtty";
    public static Harmony Harmony { get; } = new("purrtty.kfs.spike06");
    public static string? AugmentFailure { get; private set; }
    public static bool TablesAlreadyCreatedBeforePatch { get; private set; }

    /// Extra transform applied after the catalog (used by experiments that need a deliberately broken row).
    public static Func<string, string, string>? ExtraTransform;

    public static void Install()
    {
        // The patch must land before the first read of KfsIntegration.Tables (it is cached in a private static).
        var field = AccessTools.Field(typeof(KSA.Kfs.KfsIntegration), "_tables");
        TablesAlreadyCreatedBeforePatch = field?.GetValue(null) != null;

        Harmony.Patch(AccessTools.Method(typeof(GameTables), nameof(GameTables.LoadEmbedded)),
            postfix: new HarmonyMethod(typeof(KfsInstall), nameof(LoadEmbeddedPost)));
        PurrRouter.Install(Harmony);
        PurrRouter.InstallBench(Harmony);

        SystemHeaders.Files[ServiceCatalog.HeaderName] = ServiceCatalog.Header();
        foreach (var n in ServiceCatalog.BuiltinNames) SystemHeaders.Builtins.Add(n);
        KcFrontEnd.Register();
    }

    static bool _inside;

    public static void LoadEmbeddedPost(ref GameTables __result)
    {
        if (_inside) return;
        _inside = true;
        try { __result = LoadAugmented(Origin); }
        catch (Exception e)
        {
            AugmentFailure = e.GetType().Name + ": " + e.Message;
            System.Console.WriteLine("[purrtty] kFS table augmentation failed, keeping stock tables: " + AugmentFailure);
        }
        finally { _inside = false; }
    }

    public static Stream? OpenStock(string file) =>
        typeof(GameTables).Assembly.GetManifestResourceStream(GameTables.RESOURCE_PREFIX + file);

    public static string ReadStock(string file)
    {
        using var s = OpenStock(file) ?? throw new FileNotFoundException(file);
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }

    /// Stock tables, loaded the same way but untouched (for comparisons).
    public static GameTables LoadStock() => GameTables.Load(OpenStock, "embedded:KSA (stock copy)");

    /// Tables with the catalog merged in, plus an optional extra transform (file, json) -> json.
    public static GameTables LoadAugmented(string origin, Func<string, string, string>? extra = null)
    {
        extra ??= ExtraTransform;
        return GameTables.Load(file =>
        {
            if (file != "services.json" && file != "enums.json") return OpenStock(file);
            string json = ReadStock(file);
            json = file == "services.json" ? ServiceCatalog.AugmentServices(json) : ServiceCatalog.AugmentEnums(json);
            if (extra != null) json = extra(file, json);
            return new MemoryStream(Encoding.UTF8.GetBytes(json));
        }, origin);
    }
}
