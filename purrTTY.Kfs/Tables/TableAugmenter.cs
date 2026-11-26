using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using KSA.Kfs.Vm.Tables;
using purrTTY.Kfs.Catalog;

namespace purrTTY.Kfs.Tables;

/// <summary>
/// Builds the game's kFS tables with purrTTY's catalogue merged in (plan §4.5 step 4): the stock
/// <c>services.json</c> gains one row per service and the stock <c>enums.json</c> gains the
/// catalogue's enumerations; every other file, every stock row and both table versions are passed
/// through unchanged. The result comes from the game's own public loader
/// (<see cref="GameTables.Load(Func{string, Stream?}, string)"/>), so the game's validation runs
/// over the mod rows. Pure: no statics, no caching, no patching. A loader exception propagates;
/// the caller decides what to do with it.
/// </summary>
public static class TableAugmenter
{
    /// <summary>
    /// The prefix of the <see cref="GameTables.Origin"/> of every augmented instance; how purrTTY
    /// recognises tables published by some build of itself (<see cref="IsAugmented"/>). The full
    /// origin adds the catalogue's content hash (<see cref="OriginFor"/>).
    /// </summary>
    public const string AugmentedOrigin = "embedded:KSA+purrtty";

    /// <summary>What separates <see cref="AugmentedOrigin"/> from the catalogue hash in an origin.</summary>
    public const string CatalogMarker = ";catalog=";

    /// <summary>
    /// The origin of tables built from <paramref name="catalog"/>:
    /// <c>embedded:KSA+purrtty;catalog=</c> + the first 16 hex digits of the SHA-256 of the
    /// catalogue's emitted <c>services.json</c> rows and <c>enums.json</c> entries — exactly the
    /// text merged into the tables, so any difference that reaches the game (ids, names, argument
    /// cells and domains, result cells, kinds, cost classes, enumeration lists, even a summary)
    /// changes it. The installer reuses already-published tables only when their origin equals
    /// this; field-by-field comparison against the game's parsed rows would depend on the shape of
    /// the game's <c>ServiceInfo</c>, which kFS may change.
    /// </summary>
    public static string OriginFor(ServiceCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        string content = CatalogEmitter.ServiceRowsJson(catalog) + "\n" + CatalogEmitter.EnumsJson(catalog);
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return AugmentedOrigin + CatalogMarker + Convert.ToHexStringLower(hash, 0, 8);
    }

    /// <summary>True when <paramref name="origin"/> is that of tables some build of purrTTY augmented (with or without a catalogue hash).</summary>
    public static bool IsAugmented(string? origin) =>
        origin == AugmentedOrigin || (origin is not null && origin.StartsWith(AugmentedOrigin + CatalogMarker, StringComparison.Ordinal));

    /// <summary>The services table file.</summary>
    public const string ServicesFile = "services.json";

    /// <summary>The enumerations file.</summary>
    public const string EnumsFile = "enums.json";

    /// <summary>
    /// Tables built from <paramref name="stockOpen"/> with the real catalogue merged in.
    /// </summary>
    /// <param name="stockOpen">Opens a stock table file by name (as <see cref="GameTables.Load(Func{string, Stream?}, string)"/> asks), or returns null.</param>
    /// <param name="origin">The tables' origin; null (the default) for <see cref="OriginFor"/> of the catalogue — only a test needs another.</param>
    /// <exception cref="GameTablesException">The game's loader rejected the tables.</exception>
    /// <exception cref="InvalidOperationException">A catalogue id or enumeration key is already in the stock file.</exception>
    public static GameTables Build(Func<string, Stream?> stockOpen, string? origin = null) =>
        Build(stockOpen, XCatalog.Catalog, origin);

    /// <summary>As <see cref="Build(Func{string, Stream?}, string?)"/>, for any catalogue.</summary>
    public static GameTables Build(Func<string, Stream?> stockOpen, ServiceCatalog catalog, string? origin = null)
    {
        ArgumentNullException.ThrowIfNull(stockOpen);
        ArgumentNullException.ThrowIfNull(catalog);
        return GameTables.Load(file => Open(stockOpen, catalog, file), origin ?? OriginFor(catalog));
    }

    /// <summary>The stock table file <paramref name="file"/> embedded in <c>KSA.dll</c>, or null.</summary>
    public static Stream? OpenStock(string file) =>
        typeof(GameTables).Assembly.GetManifestResourceStream(GameTables.RESOURCE_PREFIX + file);

    /// <summary>
    /// The stock <c>services.json</c> text with the catalogue's rows appended. Everything else in
    /// the file (version, source, stock rows) is kept.
    /// </summary>
    /// <exception cref="InvalidOperationException">The file has no <c>services</c> array, or already has a catalogue id.</exception>
    public static string AugmentServices(string stockJson, ServiceCatalog catalog)
    {
        var root = JsonNode.Parse(stockJson)?.AsObject() ?? throw new InvalidOperationException($"{ServicesFile}: not a JSON object");
        var rows = root["services"]?.AsArray() ?? throw new InvalidOperationException($"{ServicesFile}: no 'services' array");
        var taken = rows.Select(r => r?["id"]?.GetValue<int>()).Where(id => id is not null).Select(id => id!.Value).ToHashSet();
        foreach (var s in catalog.Services)
        {
            if (taken.Contains(s.Id)) throw new InvalidOperationException($"{ServicesFile} already has id {s.Id} (0x{s.Id:X4}, {s.Name})");
            rows.Add(CatalogEmitter.Row(s));
        }
        return root.ToJsonString(CatalogEmitter.JsonOptions);
    }

    /// <summary>The stock <c>enums.json</c> text with the catalogue's enumerations added.</summary>
    /// <exception cref="InvalidOperationException">The file has no <c>enums</c> object, or already has a catalogue key.</exception>
    public static string AugmentEnums(string stockJson, ServiceCatalog catalog)
    {
        var root = JsonNode.Parse(stockJson)?.AsObject() ?? throw new InvalidOperationException($"{EnumsFile}: not a JSON object");
        var enums = root["enums"]?.AsObject() ?? throw new InvalidOperationException($"{EnumsFile}: no 'enums' object");
        foreach (var (key, values) in CatalogEmitter.EnumsObject(catalog).ToArray())
        {
            if (enums.ContainsKey(key)) throw new InvalidOperationException($"{EnumsFile} already has '{key}'");
            enums[key] = values?.DeepClone();
        }
        return root.ToJsonString(CatalogEmitter.JsonOptions);
    }

    private static Stream? Open(Func<string, Stream?> stockOpen, ServiceCatalog catalog, string file)
    {
        if (file is not (ServicesFile or EnumsFile)) return stockOpen(file);

        string stock;
        using (var stream = stockOpen(file))
        {
            if (stream is null) return null;   // the loader reports the missing file
            using var reader = new StreamReader(stream, Encoding.UTF8);
            stock = reader.ReadToEnd();
        }
        string augmented = file == ServicesFile ? AugmentServices(stock, catalog) : AugmentEnums(stock, catalog);
        return new MemoryStream(Encoding.UTF8.GetBytes(augmented), writable: false);
    }
}
