using System.Reflection;
using HarmonyLib;
using KSA.Kfs.Host;
using KSA.Kfs.Vm;
using purrTTY.Kfs.Runtime;

namespace purrTTY.Kfs.Patches;

/// <summary>
/// Patch 1 of plan §4.6: a postfix on <c>VehicleComputer.ApplyRequest</c>
/// (<c>protected override bool ApplyRequest(ServiceRequest)</c>), which the host calls on the
/// dispatch thread for every request a completed tick queued. The stock switch returns false for
/// ids it does not know; the postfix claims every id in purrTTY's block — so none is ever rejected
/// "no vehicle to act on" — and hands it to <see cref="ServiceRouter.Accept"/>.
/// </summary>
/// <remarks>Resolving the target (<see cref="TargetMethod"/>) is separate from applying it, so an
/// installer can resolve every target before patching anything.</remarks>
public static class ApplyRequestPatch
{
    /// <summary>The patch target, or null when this game build has no such method.</summary>
    public static MethodBase? TargetMethod() =>
        AccessTools.DeclaredMethod(typeof(VehicleComputer), "ApplyRequest", [typeof(ServiceRequest)]);

    /// <summary>The postfix as a Harmony patch method.</summary>
    public static HarmonyMethod PostfixMethod() => new(AccessTools.DeclaredMethod(typeof(ApplyRequestPatch), nameof(Postfix)));

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
        harmony.Unpatch(target, AccessTools.DeclaredMethod(typeof(ApplyRequestPatch), nameof(Postfix)));
    }

    /// <summary>
    /// The postfix body. Never throws: an exception escaping the router is logged once per kind,
    /// and the request stays claimed but is rejected (<c>internal error</c>, counted in
    /// <c>sys.rejected_requests</c>) rather than silently dropped.
    /// </summary>
    public static void Postfix(VehicleComputer __instance, ServiceRequest request, ref bool __result)
    {
        bool claimed = false;
        try
        {
            if (__result || request is null || !KfsBuildInfo.Owns(request.Id)) return;
            __result = claimed = true;   // ours either way: never "no vehicle to act on"
            ServiceRouter.Accept(__instance, request);
        }
        catch (Exception e)
        {
            KfsLog.Once("accept hook", e);
            if (claimed) ServiceRouter.RejectAfterFault(__instance, null, request);
        }
    }
}
