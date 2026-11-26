using System.Reflection;
using KSA.Kfs;
using KSA.Kfs.Vm.Tables;

namespace purrTTY.Kfs.Tables;

/// <summary>What <see cref="TablesPublisher.Publish(GameTables)"/> found and did (plan §4.5 step 5).</summary>
public enum TablesPublishOutcome
{
    /// <summary><c>_tables</c> was null; the augmented instance was assigned and the game's accessor returns it.</summary>
    Published,
    /// <summary>
    /// <c>_tables</c> already held tables some build of purrTTY augmented (<see cref="TableAugmenter.IsAugmented"/>);
    /// they were kept. Whether they are of this catalogue is the caller's check (their origin against <see cref="TableAugmenter.OriginFor"/>).
    /// </summary>
    Reused,
    /// <summary><c>_tables</c> held someone else's tables (built before purrTTY); nothing was changed.</summary>
    Foreign,
    /// <summary>The field does not exist in this game build (or has another type); nothing was changed.</summary>
    FieldNotFound,
    /// <summary>The field was assigned but <c>KfsIntegration.Tables</c> did not return the instance; the assignment was undone.</summary>
    NotVisible,
}

/// <summary>The outcome of a publish and the instance now in effect.</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Tables">For <see cref="TablesPublishOutcome.Published"/> the given instance, for <see cref="TablesPublishOutcome.Reused"/> the instance already there, for <see cref="TablesPublishOutcome.Foreign"/> the foreign one; otherwise null.</param>
public sealed record TablesPublishResult(TablesPublishOutcome Outcome, GameTables? Tables);

/// <summary>
/// Puts the augmented tables where the game reads them. <c>KfsIntegration.Tables</c> is
/// <c>LazyInitializer.EnsureInitialized(ref _tables, GameTables.LoadEmbedded)</c>, which returns an
/// existing value untouched, so assigning the private static <c>_tables</c> while it is null means
/// the game's loader never runs. The field is read by reflection, which does not run the lazy
/// initialiser (it does run <c>KfsIntegration</c>'s static constructor, which only creates a
/// console writer). Never touches <c>KfsIntegration.Workbench</c>.
/// </summary>
public static class TablesPublisher
{
    /// <summary>The name of the private static field behind <c>KfsIntegration.Tables</c>.</summary>
    public const string FieldName = "_tables";

    /// <summary>The field behind <c>KfsIntegration.Tables</c>, or null when this game build has no static <see cref="GameTables"/> field of that name.</summary>
    public static FieldInfo? FindField()
    {
        FieldInfo? field = typeof(KfsIntegration).GetField(FieldName, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        return field is { IsStatic: true, IsInitOnly: false, IsLiteral: false } && field.FieldType == typeof(GameTables) ? field : null;
    }

    /// <summary>Publishes <paramref name="tables"/> through the real field.</summary>
    public static TablesPublishResult Publish(GameTables tables) => Publish(tables, FindField);

    /// <summary>As <see cref="Publish(GameTables)"/>, with the field lookup substituted (tests force <see cref="TablesPublishOutcome.FieldNotFound"/>).</summary>
    internal static TablesPublishResult Publish(GameTables tables, Func<FieldInfo?> findField)
    {
        ArgumentNullException.ThrowIfNull(tables);
        ArgumentNullException.ThrowIfNull(findField);

        FieldInfo? field = findField();
        if (field is null) return new(TablesPublishOutcome.FieldNotFound, null);

        var current = (GameTables?)field.GetValue(null);
        if (current is not null)
        {
            return TableAugmenter.IsAugmented(current.Origin)
                ? new(TablesPublishOutcome.Reused, current)
                : new(TablesPublishOutcome.Foreign, current);
        }

        field.SetValue(null, tables);
        if (ReferenceEquals(KfsIntegration.Tables, tables)) return new(TablesPublishOutcome.Published, tables);

        // The accessor does not read this field: put it back as it was. (The accessor itself
        // may have built stock tables just now; those are the game's and stay.)
        if (ReferenceEquals(field.GetValue(null), tables)) field.SetValue(null, null);
        return new(TablesPublishOutcome.NotVisible, null);
    }
}
