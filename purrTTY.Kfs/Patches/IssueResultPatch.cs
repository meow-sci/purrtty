using System.Reflection;
using HarmonyLib;
using KSA.Kfs.Host;
using KSA.Kfs.Vm;
using purrTTY.Kfs.Runtime;

namespace purrTTY.Kfs.Patches;

/// <summary>
/// Patch 2 of plan §4.6: a postfix on <c>VehicleComputer</c>'s explicit implementation of
/// <c>KSA.Kfs.Vm.ISyscallHost.IssueResult(ushort, ReadOnlySpan&lt;uint&gt;, Span&lt;uint&gt;)</c>,
/// which the VM calls inside <c>SYSCALL</c> (dispatch thread, mid-tick) for every result-bearing
/// service. The stock body fills only the burn-handle services; the postfix fills the result cell
/// of purrTTY's getters through <see cref="ServiceRouter.Answer"/>.
/// </summary>
/// <remarks>Resolving the target (<see cref="TargetMethod"/>) is separate from applying it, so an
/// installer can resolve every target before patching anything.</remarks>
public static class IssueResultPatch
{
    /// <summary>
    /// The patch target, found through the interface map (robust to the compiler's naming of
    /// explicit implementations), or null when this game build has no such method.
    /// </summary>
    public static MethodBase? TargetMethod()
    {
        Type host = typeof(VehicleComputer);
        if (!typeof(ISyscallHost).IsAssignableFrom(host)) return null;
        InterfaceMapping map = host.GetInterfaceMap(typeof(ISyscallHost));
        for (int i = 0; i < map.InterfaceMethods.Length; i++)
        {
            if (map.InterfaceMethods[i].Name == nameof(ISyscallHost.IssueResult) && map.TargetMethods[i].DeclaringType == host)
                return map.TargetMethods[i];
        }
        return null;
    }

    /// <summary>The postfix as a Harmony patch method.</summary>
    public static HarmonyMethod PostfixMethod() => new(AccessTools.DeclaredMethod(typeof(IssueResultPatch), nameof(Postfix)));

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
        harmony.Unpatch(target, AccessTools.DeclaredMethod(typeof(IssueResultPatch), nameof(Postfix)));
    }

    /// <summary>The postfix body. Never throws: on an exception the result is 0 and the failure is logged once per kind.</summary>
    public static void Postfix(VehicleComputer __instance, ushort service, ReadOnlySpan<uint> args, Span<uint> results)
    {
        try
        {
            if (results.Length == 0 || !KfsBuildInfo.Owns(service)) return;
            results[0] = ServiceRouter.Answer(__instance, service, args);
        }
        catch (Exception e)
        {
            KfsLog.Once("answer hook", e);
            if (results.Length != 0) results[0] = 0u;
        }
    }
}
