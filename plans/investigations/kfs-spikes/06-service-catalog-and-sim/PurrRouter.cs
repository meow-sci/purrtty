// Routes purrTTY service ids (0x5000-0x50FF) between kFS and an IVehicleParts.
//
// Two Harmony postfixes on VehicleComputer, both on the kFS dispatch thread:
//   * ISyscallHost.IssueResult - runs INSIDE the SYSCALL instruction, mid-tick, after the VM has
//     already checked arg_domains. Fills the single result cell by reading the parts object.
//     Must be side-effect free: the tick may still overrun and its requests be discarded.
//   * ApplyRequest - runs after a tick completed, once per queued request. The stock switch returns
//     false for ids it doesn't know; we turn that into true (claim), validate the index against the
//     vehicle, reject with VehicleComputer.RejectService (program log + sys.rejected_requests) or
//     queue the command. Every request in our block is claimed, getters included: the VM queues a
//     request for result-bearing services too, and an unclaimed one is rejected "no vehicle to act on".
// Pump() stands in for the main-thread hook (a postfix on KfsIntegration.BeforeInputEvents in the
// game): it drains the queues and applies the commands. No game state is touched on the dispatch thread.

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using HarmonyLib;
using KSA.Kfs.Host;
using KSA.Kfs.Vm;

namespace PurrKfs;

public static class PurrRouter
{
    sealed class Binding(VehicleComputer computer, IVehicleParts parts)
    {
        public readonly VehicleComputer Computer = computer;
        public readonly IVehicleParts Parts = parts;
        public readonly ConcurrentQueue<ServiceRequest> Pending = new();
    }

    static readonly ConditionalWeakTable<VehicleComputer, Binding> Map = new();

    // Diagnostics for the spike's assertions.
    public static readonly ConcurrentDictionary<ushort, int> IssuedIds = new();
    public static readonly ConcurrentDictionary<ushort, int> AppliedIds = new();
    public static readonly ConcurrentQueue<string> Faults = new();

    public static bool Mine(ushort id) => id >= ServiceCatalog.FirstId && id <= ServiceCatalog.LastId;

    public static void Install(Harmony h)
    {
        h.Patch(AccessTools.Method(typeof(VehicleComputer), "ApplyRequest"),
            postfix: new HarmonyMethod(typeof(PurrRouter), nameof(ApplyPost)));
        h.Patch(AccessTools.Method(typeof(VehicleComputer), "KSA.Kfs.Vm.ISyscallHost.IssueResult"),
            postfix: new HarmonyMethod(typeof(PurrRouter), nameof(IssuePost)));
    }

    // ------------------------------------------------------------------ bench (no vehicle)

    /// Calls of the base-method postfix, by runtime type of the computer (VehicleComputer must stay 0:
    /// its override never calls base, so a vehicle's requests are not double-handled).
    public static readonly ConcurrentDictionary<string, int> BenchPostCalls = new();
    public const string BenchReason = "purr services need a vehicle (the bench has no parts)";

    /// Postfix on the base HostComputer.ApplyRequest (`protected virtual bool`, body `return false`).
    /// BenchMachine does not override it, so on the bench this is where an unknown id lands.
    public static void InstallBench(Harmony h) =>
        h.Patch(AccessTools.Method(typeof(HostComputer), "ApplyRequest"),
            postfix: new HarmonyMethod(typeof(PurrRouter), nameof(BenchApplyPost)));

    public static void BenchApplyPost(HostComputer __instance, ServiceRequest request, ref bool __result)
    {
        BenchPostCalls.AddOrUpdate(__instance.GetType().Name, 1, (_, n) => n + 1);
        if (__result || !Mine(request.Id)) return;
        __result = true;
        try
        {
            // Getters: claim silently; with no syscall host the program already got 0.
            if (ServiceCatalog.ById.TryGetValue(request.Id, out var def) && def.HasResult) return;
            RejectLikeHost(__instance, request, BenchReason);
        }
        catch (Exception e) { Faults.Enqueue($"BenchApplyRequest 0x{request.Id:X4}: {e}"); }
    }

    /// Same program-log line and counter as the protected HostComputer.RejectByHost, from public
    /// members only (no reflection): Machine.CountRejectedRequest, Console.EventAllowed/Event, LastK, TickHz.
    static void RejectLikeHost(HostComputer c, ServiceRequest r, string reason)
    {
        c.Machine.CountRejectedRequest();
        if (c.Console.EventAllowed(c.LastK, c.TickHz))
        {
            string name = ServiceCatalog.ById.TryGetValue(r.Id, out var d) ? d.Name : $"0x{r.Id:X4}";
            c.Console.Event(c.LastK, c.TickHz, "rejected " + name + ": " + reason);
        }
    }

    public static void Attach(VehicleComputer c, IVehicleParts parts) => Map.AddOrUpdate(c, new Binding(c, parts));
    public static void Detach(VehicleComputer c) => Map.Remove(c);

    // ------------------------------------------------------------------ dispatch thread

    public static void IssuePost(VehicleComputer __instance, ushort service, ReadOnlySpan<uint> args, Span<uint> results)
    {
        if (results.Length == 0 || !Mine(service)) return;
        try
        {
            IssuedIds.AddOrUpdate(service, 1, (_, n) => n + 1);
            results[0] = Map.TryGetValue(__instance, out var b) ? Answer(b.Parts, service, args) : 0u;
        }
        catch (Exception e) { Faults.Enqueue($"IssueResult 0x{service:X4}: {e}"); results[0] = 0u; }
    }

    static uint Answer(IVehicleParts p, ushort id, ReadOnlySpan<uint> a)
    {
        // Getters never reject: an out-of-range index reads 0, so PURR_*_PRESENT can probe an index.
        // Selector values are already range-checked by the VM (enum:<name> domain) before we run.
        static uint F(float f) => BitConverter.SingleToUInt32Bits(f);
        int i = a.Length > 0 ? (int)Math.Min(a[0], int.MaxValue) : 0;
        uint sel = a.Length > 1 ? a[1] : 0u;
        switch (id)
        {
            case 0x5000:
                return (PartsInfo)a[0] switch
                {
                    PartsInfo.Generation => p.Generation,
                    PartsInfo.Dirty => p.Dirty ? 1u : 0u,
                    PartsInfo.Lights => (uint)p.LightCount,
                    PartsInfo.Deployables => (uint)p.DeployableCount,
                    PartsInfo.Actuators => (uint)p.ActuatorCount,
                    _ => 0u,
                };
            case 0x5010: return (uint)p.LightCount;
            case 0x5011: return i < p.LightCount ? p.LightGetI(i, (LightProp)sel) : 0u;
            case 0x5012: return i < p.LightCount ? F(p.LightGetF(i, (LightProp)sel)) : 0u;
            case 0x5020: return (uint)p.DeployableCount;
            case 0x5021: return i < p.DeployableCount ? p.DeployGetI(i, (DeployProp)sel) : 0u;
            case 0x5022: return i < p.DeployableCount ? F(p.DeployGetF(i, (DeployProp)sel)) : 0u;
            case 0x5030: return (uint)p.ActuatorCount;
            case 0x5031: return i < p.ActuatorCount ? p.ActuatorGetI(i, (ActuatorProp)sel) : 0u;
            case 0x5032: return i < p.ActuatorCount ? F(p.ActuatorGetF(i, (ActuatorProp)sel)) : 0u;
            default: return 0u;
        }
    }

    public static void ApplyPost(VehicleComputer __instance, ServiceRequest request, ref bool __result)
    {
        if (__result || !Mine(request.Id)) return;
        __result = true;                                   // claim everything in our block
        try
        {
            AppliedIds.AddOrUpdate(request.Id, 1, (_, n) => n + 1);
            if (!ServiceCatalog.ById.TryGetValue(request.Id, out var def))
            {
                __instance.RejectService(request, "not a purrTTY service in this version");
                return;
            }
            if (!Map.TryGetValue(__instance, out var b))
            {
                __instance.RejectService(request, "purrTTY has no parts view of this vehicle");
                return;
            }
            if (def.HasResult) return;                      // getters: answered at issue, nothing to apply
            string? why = Validate(b.Parts, request);
            if (why != null) { __instance.RejectService(request, why); return; }
            b.Pending.Enqueue(request);
        }
        catch (Exception e) { Faults.Enqueue($"ApplyRequest 0x{request.Id:X4}: {e}"); }
    }

    /// Host checks that need the vehicle. Runs on the dispatch thread: reads only frozen structure.
    static string? Validate(IVehicleParts p, ServiceRequest r)
    {
        uint i = r.Args.Length > 0 ? r.Args[0] : 0;
        switch (r.Id)
        {
            case 0x5013:
                if (i >= p.LightCount) return LightRange(i, p);
                return p.LightHasSwitch((int)i) ? null : $"light {i} has no switch";
            case >= 0x5014 and <= 0x5018:
                return i >= p.LightCount ? LightRange(i, p) : null;
            case 0x5023:
            case 0x5024:
                if (i >= p.DeployableCount) return $"deployable {i} out of range (this vehicle has {p.DeployableCount})";
                return p.DeployableBroken((int)i) ? $"deployable {i} is broken" : null;
            case 0x5033:
                return i >= p.ActuatorCount ? $"actuator {i} out of range (this vehicle has {p.ActuatorCount})" : null;
            default:
                return null;
        }
    }

    static string LightRange(uint i, IVehicleParts p) => $"light {i} out of range (this vehicle has {p.LightCount})";

    // ------------------------------------------------------------------ main thread

    /// Drains every vehicle's queue and applies the commands. Returns the number applied.
    public static int Pump()
    {
        int n = 0;
        foreach (var kv in Map) n += Pump(kv.Value);
        return n;
    }

    static int Pump(Binding b)
    {
        int n = 0;
        while (b.Pending.TryDequeue(out var r))
        {
            try { Apply(b, r); n++; }
            catch (Exception e) { Faults.Enqueue($"Pump 0x{r.Id:X4}: {e}"); }
        }
        return n;
    }

    static void Apply(Binding b, ServiceRequest r)
    {
        var p = b.Parts;
        uint[] a = r.Args;
        static float F(uint bits) => BitConverter.UInt32BitsToSingle(bits);
        int i = a.Length > 0 ? (int)a[0] : 0;
        // Structure may have changed (rescan) between validation and now: re-check the index.
        switch (r.Id)
        {
            case 0x5001: p.Rescan(); break;
            case 0x5002: foreach (var line in p.Describe((PartKind)a[0])) b.Computer.Event(line); break;
            case 0x5013: if (i < p.LightCount) p.LightSetOn(i, a[1] != 0); break;
            case 0x5014: if (i < p.LightCount) p.LightSetColor(i, F(a[1]), F(a[2]), F(a[3])); break;
            case 0x5015: if (i < p.LightCount) p.LightSetCone(i, F(a[1]), F(a[2])); break;
            case 0x5016: if (i < p.LightCount) p.LightSetIntensity(i, F(a[1])); break;
            case 0x5017: if (i < p.LightCount) p.LightSetRange(i, F(a[1])); break;
            case 0x5018: if (i < p.LightCount) p.LightReset(i); break;
            case 0x5019: p.LightsSetAll(a[0] != 0); break;
            case 0x5023: if (i < p.DeployableCount) p.DeploySet(i, a[1] != 0); break;
            case 0x5024: if (i < p.DeployableCount) p.DeployToggle(i); break;
            case 0x5033: if (i < p.ActuatorCount) p.ActuatorSet(i, F(a[1])); break;
        }
    }
}
