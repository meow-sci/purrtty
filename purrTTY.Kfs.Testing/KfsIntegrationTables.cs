using KSA.Kfs.Vm.Tables;
using purrTTY.Kfs.Tables;

namespace purrTTY.Kfs.Testing;

/// <summary>
/// Testing-only access to the game's private static <c>KfsIntegration._tables</c> (the cache behind
/// <c>KfsIntegration.Tables</c>): read it without running the lazy loader, reset it so the next
/// install in the process starts clean, or pre-seed it with someone else's tables. Never use this
/// in the mod: the installer publishes through <see cref="TablesPublisher"/>.
/// </summary>
public static class KfsIntegrationTables
{
    /// <summary>The cached tables, or null; does not run the game's loader.</summary>
    /// <exception cref="InvalidOperationException">The field is not in this game build.</exception>
    public static GameTables? Read() => (GameTables?)Field().GetValue(null);

    /// <summary>True when nothing is cached (the game's next read would load the tables).</summary>
    public static bool IsNull => Read() is null;

    /// <summary>Clears the cache.</summary>
    public static void Reset() => Field().SetValue(null, null);

    /// <summary>Puts <paramref name="tables"/> in the cache, as if the game had built them first.</summary>
    public static void Seed(GameTables tables)
    {
        ArgumentNullException.ThrowIfNull(tables);
        Field().SetValue(null, tables);
    }

    /// <summary>Seeds the cache with tables from the untouched stock streams (a foreign instance) and returns them.</summary>
    public static GameTables SeedStock()
    {
        GameTables stock = GameTables.Load(TableAugmenter.OpenStock, "test:foreign-stock");
        Seed(stock);
        return stock;
    }

    private static System.Reflection.FieldInfo Field() =>
        TablesPublisher.FindField() ?? throw new InvalidOperationException($"KfsIntegration.{TablesPublisher.FieldName} not found in this game build");
}
