using System.Reflection;
using HarmonyLib;
using KSA.Kfs;
using purrTTY.Kfs.Game;

namespace purrTTY.Kfs.Patches;

/// <summary>
/// Patch 3 of plan §4.6: a postfix on <c>KSA.Kfs.KfsIntegration.BeforeInputEvents()</c>
/// (<c>public static void</c>), which <c>Program.PrepareFrame</c> calls once per frame on the main
/// thread in the window where no vehicle solver job runs — after the previous job's results were
/// applied, before the next one starts. Stock kFS drains its own main-thread work there. The
/// postfix runs <see cref="MainThreadPump.Run"/>: discovery, drain-and-apply, pruning. The stock
/// body does nothing while the kFS flag is off; the postfix runs regardless, so pruning still
/// happens. Offline the method exists in <c>KSA.dll</c> but the game never calls it, and with no
/// <see cref="MainThreadPump.Active"/> pump the postfix does nothing.
/// </summary>
/// <remarks>Resolving the target (<see cref="TargetMethod"/>) is separate from applying it, so an
/// installer can resolve every target before patching anything.</remarks>
public static class BeforeInputEventsPatch
{
    private static long _calls;

    /// <summary>The patch target, or null when this game build has no such method.</summary>
    public static MethodBase? TargetMethod()
    {
        MethodInfo? method = AccessTools.DeclaredMethod(typeof(KfsIntegration), nameof(KfsIntegration.BeforeInputEvents), Type.EmptyTypes);
        return method is { IsStatic: true } && method.ReturnType == typeof(void) ? method : null;
    }

    /// <summary>Times the postfix ran (process-wide, since start): non-zero in the game proves the hook fires (plan §10 V2).</summary>
    public static long Calls => Interlocked.Read(ref _calls);

    /// <summary>The postfix as a Harmony patch method.</summary>
    public static HarmonyMethod PostfixMethod() => new(PostfixInfo());

    /// <summary>Applies the postfix to <paramref name="target"/> (from <see cref="TargetMethod"/>).</summary>
    public static MethodInfo Apply(Harmony harmony, MethodBase target)
    {
        ArgumentNullException.ThrowIfNull(harmony);
        ArgumentNullException.ThrowIfNull(target);
        return harmony.Patch(target, postfix: PostfixMethod());
    }

    /// <summary>Removes this postfix (only this one) from <paramref name="target"/>.</summary>
    public static void Remove(Harmony harmony, MethodBase target)
    {
        ArgumentNullException.ThrowIfNull(harmony);
        ArgumentNullException.ThrowIfNull(target);
        harmony.Unpatch(target, PostfixInfo());
    }

    /// <summary>
    /// The postfix body. Never throws: nothing in kFS catches an exception here — it would
    /// propagate into the game's frame preparation — so a failure is logged once and counted.
    /// </summary>
    public static void Postfix()
    {
        try
        {
            Interlocked.Increment(ref _calls);
            MainThreadPump.Run();
        }
        catch (Exception e)
        {
            KfsLog.Once("pump hook", e);
        }
    }

    private static MethodInfo PostfixInfo() => AccessTools.DeclaredMethod(typeof(BeforeInputEventsPatch), nameof(Postfix));
}
