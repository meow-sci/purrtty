using System.Reflection;
using HarmonyLib;
using KSA.Kfs.Host;
using KSA.Kfs.Vm;
using purrTTY.Kfs.Catalog;
using purrTTY.Kfs.Runtime;

namespace purrTTY.Kfs.Patches;

/// <summary>
/// Optional patch 4 of plan §4.6: a postfix on the base <c>HostComputer.ApplyRequest</c>
/// (<c>protected virtual bool</c>, body <c>return false</c>). <c>VehicleComputer</c> overrides it
/// without calling the base, so this runs only for hosts without a vehicle — in practice the
/// workbench's <c>BenchMachine</c>. For ids in purrTTY's block that fell through it claims the
/// request: a getter silently (the program already read 0: the bench has no syscall host), a
/// setter with the program-log line <c>rejected &lt;name&gt;: x_ services need a vehicle (the
/// bench has no parts)</c> and the machine's rejected-request counter incremented — instead of
/// the stock "no vehicle to act on" for every call.
/// </summary>
/// <remarks>
/// The rejection reproduces the protected <c>HostComputer.RejectByHost</c> from public members only
/// (<c>Machine.CountRejectedRequest()</c>, <c>Console.EventAllowed/Event</c>, <c>LastK</c>,
/// <c>TickHz</c>), as spike 06 did, rather than through a reflected delegate to it: every member
/// is checked by the compiler against each game build, so a game change breaks the build instead
/// of failing at run time, and there is no resolution step that could fail at install.
/// Not part of the all-or-nothing set: the target is a one-line virtual the JIT may inline, and
/// losing the patch only costs the friendlier message.
/// </remarks>
public static class BenchApplyRequestPatch
{
    /// <summary>The reason logged for a setter on the bench.</summary>
    public const string Reason = "x_ services need a vehicle (the bench has no parts)";

    private static readonly string?[] Names = BuildNames();
    private static long _calls;
    private static long _vehicleCalls;
    private static long _claimed;
    private static long _rejected;

    /// <summary>The patch target, or null when this game build has no such method.</summary>
    public static MethodBase? TargetMethod()
    {
        MethodInfo? method = AccessTools.DeclaredMethod(typeof(HostComputer), "ApplyRequest", [typeof(ServiceRequest)]);
        return method is { IsVirtual: true } && method.ReturnType == typeof(bool) ? method : null;
    }

    /// <summary>Times the postfix ran, for any host (process-wide, since start).</summary>
    public static long Calls => Interlocked.Read(ref _calls);

    /// <summary>Times it ran for a <see cref="VehicleComputer"/> — must stay 0 (the override never calls the base); such calls are ignored.</summary>
    public static long VehicleComputerCalls => Interlocked.Read(ref _vehicleCalls);

    /// <summary>Requests claimed.</summary>
    public static long Claimed => Interlocked.Read(ref _claimed);

    /// <summary>Of those, setters rejected with <see cref="Reason"/>.</summary>
    public static long Rejected => Interlocked.Read(ref _rejected);

    /// <summary>Applies the postfix to <paramref name="target"/> (from <see cref="TargetMethod"/>).</summary>
    public static MethodInfo Apply(Harmony harmony, MethodBase target)
    {
        ArgumentNullException.ThrowIfNull(harmony);
        ArgumentNullException.ThrowIfNull(target);
        return harmony.Patch(target, postfix: new HarmonyMethod(PostfixInfo()));
    }

    /// <summary>Removes this postfix (only this one) from <paramref name="target"/>.</summary>
    public static void Remove(Harmony harmony, MethodBase target)
    {
        ArgumentNullException.ThrowIfNull(harmony);
        ArgumentNullException.ThrowIfNull(target);
        harmony.Unpatch(target, PostfixInfo());
    }

    /// <summary>The postfix body. Never throws.</summary>
    public static void Postfix(HostComputer __instance, ServiceRequest request, ref bool __result)
    {
        try
        {
            Interlocked.Increment(ref _calls);
            if (__instance is VehicleComputer)
            {
                // Not reachable today (VehicleComputer's override does not call the base); a
                // vehicle's requests belong to ApplyRequestPatch, never to the bench answer.
                Interlocked.Increment(ref _vehicleCalls);
                return;
            }
            if (__result || request is null || !KfsBuildInfo.Owns(request.Id)) return;

            __result = true;
            Interlocked.Increment(ref _claimed);
            if (ServiceRouter.HasResult(request.Id)) return;   // a getter: silent, its result was 0

            Interlocked.Increment(ref _rejected);
            __instance.Machine.CountRejectedRequest();
            if (__instance.Console.EventAllowed(__instance.LastK, __instance.TickHz))
            {
                string name = Names[request.Id - KfsBuildInfo.ServiceIdFirst] ?? $"0x{request.Id:X4}";
                __instance.Console.Event(__instance.LastK, __instance.TickHz, "rejected " + name + ": " + Reason);
            }
        }
        catch (Exception e)
        {
            KfsLog.Once("bench hook", e);
        }
    }

    private static MethodInfo PostfixInfo() => AccessTools.DeclaredMethod(typeof(BenchApplyRequestPatch), nameof(Postfix));

    private static string?[] BuildNames()
    {
        var names = new string?[KfsBuildInfo.ServiceIdLast - KfsBuildInfo.ServiceIdFirst + 1];
        foreach (ServiceDef s in XCatalog.Services)
        {
            if (KfsBuildInfo.Owns(s.Id)) names[s.Id - KfsBuildInfo.ServiceIdFirst] = s.Name;
        }
        return names;
    }
}
