using KSA.Kfs.Host;
using KSA.Kfs.Vm;
using purrTTY.Kfs.Catalog;

namespace purrTTY.Kfs.Runtime;

/// <summary>
/// Routes purrTTY's service ids between the game's VM and a vehicle's <see cref="IVehicleParts"/>
/// (plan §4.9). Both entry points run on the kFS dispatch thread, are reached through the host
/// hooks (<see cref="Patches.IssueResultPatch"/>, <see cref="Patches.ApplyRequestPatch"/>), and do
/// not allocate per call after warm-up except to write a rejection or a describe table.
/// </summary>
public static class ServiceRouter
{
    private static readonly bool[] ResultBearing = BuildResultBearing();

    /// <summary>
    /// Test seam: when set, called first in <see cref="Answer"/> and <see cref="Accept"/> with the
    /// service id, outside the router's own fault handling, so a test can prove the host hooks'
    /// last-resort catches. Null (one static read per call) outside tests.
    /// </summary>
    internal static Action<ushort>? EntryFault;

    /// <summary>True when <paramref name="id"/> is one of the catalogue's result-bearing services (the getters).</summary>
    public static bool HasResult(ushort id)
    {
        int slot = id - KfsBuildInfo.ServiceIdFirst;
        return (uint)slot < (uint)ResultBearing.Length && ResultBearing[slot];
    }

    /// <summary>
    /// The result cell of a getter, inside <c>SYSCALL</c> (mid-tick; the VM has checked the
    /// argument domains). Never rejects: no binding, an index out of range or a departed part reads
    /// 0. An exception from the back end is caught, counted and answered with 0. The first call of
    /// a new run rebuilds the lists first (<see cref="VehicleBinding.SyncRun"/>).
    /// </summary>
    public static uint Answer(VehicleComputer computer, ushort id, ReadOnlySpan<uint> args)
    {
        EntryFault?.Invoke(id);
        VehicleBinding? binding = PartsRegistry.Find(computer);
        if (binding is null) return 0u;
        binding.SyncRun();
        binding.CountAnswered();
        try
        {
            return Answer(binding.Parts, id, args);
        }
        catch (Exception e)
        {
            binding.CountFault();
            KfsLog.Once("answer", e);
            return 0u;
        }
    }

    private static uint Answer(IVehicleParts parts, ushort id, ReadOnlySpan<uint> a)
    {
        switch (id)
        {
            case Svc.PartsInfo:
                return (PartsInfo)Arg(a, 0) switch
                {
                    PartsInfo.Generation => (uint)parts.Sets.Generation,
                    PartsInfo.Dirty => parts.Dirty ? 1u : 0u,
                    PartsInfo.Lights => (uint)parts.Sets.Lights.Length,
                    PartsInfo.Deployables => (uint)parts.Sets.Deployables.Length,
                    PartsInfo.Actuators => (uint)parts.Sets.Actuators.Length,
                    _ => 0u,
                };
            case Svc.LightCount: return (uint)parts.Sets.Lights.Length;
            case Svc.LightGeti: return parts.LightGetI(Index(a), (LightProp)Arg(a, 1));
            case Svc.LightGetf: return Bits(parts.LightGetF(Index(a), (LightProp)Arg(a, 1)));
            case Svc.DeployCount: return (uint)parts.Sets.Deployables.Length;
            case Svc.DeployGeti: return parts.DeployGetI(Index(a), (DeployProp)Arg(a, 1));
            case Svc.DeployGetf: return Bits(parts.DeployGetF(Index(a), (DeployProp)Arg(a, 1)));
            case Svc.ActuatorCount: return (uint)parts.Sets.Actuators.Length;
            case Svc.ActuatorGeti: return parts.ActuatorGetI(Index(a), (ActuatorProp)Arg(a, 1));
            case Svc.ActuatorGetf: return Bits(parts.ActuatorGetF(Index(a), (ActuatorProp)Arg(a, 1)));
            default: return 0u;
        }
    }

    /// <summary>
    /// A request in purrTTY's block after its tick completed (already claimed by the caller).
    /// Getters were answered at issue and are ignored here; <c>x_parts_describe</c> writes its
    /// table now (one per second of computer time); every other request is checked against the
    /// frozen lists — index, presence, switch — and rejected through
    /// <see cref="VehicleComputer.RejectService"/> or queued as a <see cref="PartCommand"/> holding
    /// the resolved slot. <c>x_parts_rescan</c> is queued too, so it takes effect in order. When
    /// the back end throws while a request is checked, the request is rejected
    /// (<see cref="PartsText.InternalError"/>, counted in <c>sys.rejected_requests</c>) rather than
    /// dropped, and the fault is counted and logged once.
    /// </summary>
    public static void Accept(VehicleComputer computer, ServiceRequest request)
    {
        EntryFault?.Invoke(request.Id);
        VehicleBinding? binding = PartsRegistry.Find(computer);
        if (binding is null)
        {
            computer.RejectService(request, PartsText.NoBinding);
            return;
        }
        binding.SyncRun();
        if (HasResult(request.Id)) return;   // answered at issue; the VM queues getters too
        try
        {
            Accept(computer, binding, request);
        }
        catch (Exception e)
        {
            binding.CountFault();
            KfsLog.Once("accept", e);
            RejectAfterFault(computer, binding, request);
        }
    }

    /// <summary>Rejects a request whose handling failed; never throws.</summary>
    internal static void RejectAfterFault(VehicleComputer computer, VehicleBinding? binding, ServiceRequest request)
    {
        try
        {
            binding?.CountRejected();
            computer.RejectService(request, PartsText.InternalError);
        }
        catch (Exception e)
        {
            KfsLog.Once("reject after fault", e);
        }
    }

    private static void Accept(VehicleComputer computer, VehicleBinding binding, ServiceRequest request)
    {
        uint[] a = request.Args ?? [];
        ushort id = request.Id;
        switch (id)
        {
            case Svc.PartsRescan:
                binding.Enqueue(new PartCommand(id, null));
                return;
            case Svc.PartsDescribe:
                Describe(computer, binding, request, (PartKind)Arg(a, 0));
                return;
            case Svc.LightsSetAll:
                binding.Enqueue(new PartCommand(id, null, U: Arg(a, 0) != 0 ? 1u : 0u));
                return;
            case Svc.LightSetOn:
                if (Resolve(computer, binding, request, PartKind.Light, requireSwitch: true) is { } on)
                    binding.Enqueue(new PartCommand(id, on, U: Arg(a, 1) != 0 ? 1u : 0u));
                return;
            case Svc.LightSetColor:
                if (Resolve(computer, binding, request, PartKind.Light) is { } color)
                    binding.Enqueue(new PartCommand(id, color, A: Float(a, 1), B: Float(a, 2), C: Float(a, 3)));
                return;
            case Svc.LightSetCone:
                if (Resolve(computer, binding, request, PartKind.Light) is { } cone)
                    binding.Enqueue(new PartCommand(id, cone, A: Float(a, 1), B: Float(a, 2)));
                return;
            case Svc.LightSetIntensity:
            case Svc.LightSetRange:
                if (Resolve(computer, binding, request, PartKind.Light) is { } light)
                    binding.Enqueue(new PartCommand(id, light, A: Float(a, 1)));
                return;
            case Svc.LightReset:
                if (Resolve(computer, binding, request, PartKind.Light) is { } reset)
                    binding.Enqueue(new PartCommand(id, reset));
                return;
            case Svc.DeploySet:
                if (Resolve(computer, binding, request, PartKind.Deployable) is { } deploy)
                    binding.Enqueue(new PartCommand(id, deploy, U: Arg(a, 1) != 0 ? 1u : 0u));
                return;
            case Svc.DeployToggle:
                if (Resolve(computer, binding, request, PartKind.Deployable) is { } toggle)
                    binding.Enqueue(new PartCommand(id, toggle));
                return;
            case Svc.ActuatorSet:
                if (Resolve(computer, binding, request, PartKind.Actuator) is { } actuator)
                    binding.Enqueue(new PartCommand(id, actuator, A: Float(a, 1)));
                return;
            default:
                Reject(computer, binding, request, PartsText.UnknownService);
                return;
        }
    }

    /// <summary>The slot a setter names, or null after rejecting the request.</summary>
    private static PartSlot? Resolve(VehicleComputer computer, VehicleBinding binding, ServiceRequest request,
        PartKind kind, bool requireSwitch = false)
    {
        IVehicleParts parts = binding.Parts;
        PartSets sets = parts.Sets;
        uint index = Arg(request.Args, 0);
        if (sets.Slot(kind, index) is not { } slot)
        {
            if (!RejectedSilently(computer, binding)) Reject(computer, binding, request, PartsText.OutOfRange(kind, index, sets.Count(kind)));
            return null;
        }
        if (!parts.IsPresent(slot))
        {
            if (!RejectedSilently(computer, binding)) Reject(computer, binding, request, PartsText.Departed(slot));
            return null;
        }
        if (requireSwitch && slot is LightSlot light && !parts.HasSwitch(light))
        {
            if (!RejectedSilently(computer, binding)) Reject(computer, binding, request, PartsText.NoSwitch(light));
            return null;
        }
        return slot;
    }

    private static void Describe(VehicleComputer computer, VehicleBinding binding, ServiceRequest request, PartKind kind)
    {
        if (!binding.ShouldReport(BindingEvent.Describe, computer.LastK, computer.TickHz))
        {
            Reject(computer, binding, request, PartsText.DescribeLimited);
            return;
        }
        binding.CountAccepted();
        // VehicleComputer.Event is not rate-limited by the host; the limit above bounds it.
        foreach (string line in PartsText.Describe(binding.Parts, kind)) computer.Event(line);
    }

    private static void Reject(VehicleComputer computer, VehicleBinding binding, ServiceRequest request, string reason)
    {
        binding.CountRejected();
        computer.RejectService(request, reason);
    }

    /// <summary>
    /// Counts a rejection without its text when the host's event limiter would drop that text
    /// anyway (<c>HostComputer.RejectByHost</c>: count, then the event only if
    /// <c>ConsoleLog.EventAllowed</c>), so a program hammering a bad index formats nothing per call
    /// on the solver thread — as <see cref="Patches.BenchApplyRequestPatch"/> does. True when it
    /// did (the caller then skips <see cref="Reject"/>); false: the caller rejects with the text.
    /// </summary>
    private static bool RejectedSilently(VehicleComputer computer, VehicleBinding binding)
    {
        if (computer.Console.EventAllowed(computer.LastK, computer.TickHz)) return false;
        binding.CountRejected();
        computer.Machine.CountRejectedRequest();
        return true;
    }

    private static uint Arg(ReadOnlySpan<uint> args, int i) => (uint)i < (uint)args.Length ? args[i] : 0u;

    private static float Float(uint[] args, int i) => BitConverter.UInt32BitsToSingle(Arg(args, i));

    /// <summary>The index argument as an int; -1 (out of range everywhere) when it does not fit.</summary>
    private static int Index(ReadOnlySpan<uint> args)
    {
        uint index = args.Length > 0 ? args[0] : uint.MaxValue;
        return index > int.MaxValue ? -1 : (int)index;
    }

    private static uint Bits(float value) => BitConverter.SingleToUInt32Bits(value);

    private static bool[] BuildResultBearing()
    {
        var table = new bool[KfsBuildInfo.ServiceIdLast - KfsBuildInfo.ServiceIdFirst + 1];
        foreach (ServiceDef s in XCatalog.Services) table[s.Id - KfsBuildInfo.ServiceIdFirst] = s.HasResult;
        return table;
    }
}
