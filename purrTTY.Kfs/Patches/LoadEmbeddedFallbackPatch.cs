using System.Reflection;
using HarmonyLib;
using KSA.Kfs.Vm.Tables;

namespace purrTTY.Kfs.Patches;

/// <summary>
/// The fallback of plan §4.5 step 5 (proven in spike 03): a postfix on the public static
/// <c>GameTables.LoadEmbedded()</c> that replaces its result with the augmented instance the
/// installer already built. Used only when <c>KfsIntegration._tables</c> cannot be found, so
/// "tables already built by someone else" can no longer be detected. Never throws, and does no
/// loading of its own: a nested <c>LoadEmbedded</c> call during its work (there is none today)
/// returns the stock result untouched.
/// </summary>
public static class LoadEmbeddedFallbackPatch
{
    private static GameTables? _tables;

    [ThreadStatic]
    private static bool _inside;

    /// <summary>The patch target, or null when this game build has no such method.</summary>
    public static MethodBase? TargetMethod() =>
        AccessTools.DeclaredMethod(typeof(GameTables), nameof(GameTables.LoadEmbedded), Type.EmptyTypes);

    /// <summary>The instance the postfix hands out, or null while not applied.</summary>
    public static GameTables? Tables => Volatile.Read(ref _tables);

    /// <summary>Makes <paramref name="target"/> return <paramref name="tables"/>.</summary>
    public static MethodInfo Apply(Harmony harmony, MethodBase target, GameTables tables)
    {
        ArgumentNullException.ThrowIfNull(harmony);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(tables);
        Volatile.Write(ref _tables, tables);
        try
        {
            return harmony.Patch(target, postfix: new HarmonyMethod(PostfixInfo()));
        }
        catch
        {
            Volatile.Write(ref _tables, null);
            throw;
        }
    }

    /// <summary>Removes this postfix (only this one) from <paramref name="target"/>.</summary>
    public static void Remove(Harmony harmony, MethodBase target)
    {
        ArgumentNullException.ThrowIfNull(harmony);
        ArgumentNullException.ThrowIfNull(target);
        harmony.Unpatch(target, PostfixInfo());
        Volatile.Write(ref _tables, null);
    }

    /// <summary>The postfix body.</summary>
    public static void Postfix(ref GameTables __result)
    {
        if (_inside) return;
        _inside = true;
        try
        {
            GameTables? tables = Volatile.Read(ref _tables);
            if (tables is not null) __result = tables;
        }
        catch (Exception e)
        {
            KfsLog.Once("tables fallback hook", e);
        }
        finally
        {
            _inside = false;
        }
    }

    private static MethodInfo PostfixInfo() => AccessTools.DeclaredMethod(typeof(LoadEmbeddedFallbackPatch), nameof(Postfix));
}
