using KSA.Kfs.Host;
using purrTTY.Kfs.Runtime;

namespace purrTTY.Kfs.Game;

/// <summary>One live host as the pump sees it.</summary>
/// <param name="Computer">The host's computer (the registry key).</param>
/// <param name="Key">The ordering key: the game's <c>Vehicle.Id</c>, the order stock kFS runs its main-thread work in.</param>
/// <param name="Source">Whatever the source needs to build the host's parts (the game: its <c>KfsVehicleHost</c>).</param>
public readonly record struct PumpHost(VehicleComputer Computer, string Key, object? Source);

/// <summary>
/// The pump's view of the game's hosts (plan §4.12): the list of live hosts, how to build a host's
/// parts back end, and which bindings are the source's to prune. The game's is
/// <see cref="GameHostSource"/>; tests drive the same pump with simulated hosts.
/// </summary>
public interface IPumpHostSource
{
    /// <summary>Fills <paramref name="into"/> (cleared first) with the live hosts, in any order. Main thread; must cost almost nothing when there are none.</summary>
    void Snapshot(List<PumpHost> into);

    /// <summary>The live host whose computer is <paramref name="computer"/>. Any thread (the dispatch-thread fallback).</summary>
    bool TryFind(VehicleComputer computer, out PumpHost host);

    /// <summary>A new parts back end for <paramref name="host"/>; may throw (the host is then skipped this frame).</summary>
    IVehicleParts CreateParts(in PumpHost host);

    /// <summary>True when <paramref name="binding"/> was made for one of this source's hosts, so the pump may prune it once its host is gone.</summary>
    bool Owns(VehicleBinding binding);
}

/// <summary>
/// Discovery, drain and pruning, once per frame on the main thread (plan §4.12 as amended by
/// §8.6): for each live host, ordered by <see cref="PumpHost.Key"/>, find or create its binding,
/// mark it seen and run <see cref="BindingPump.DrainAndApply"/>; then unbind every binding of the
/// source that was not seen — its host is gone, and its queued commands are discarded, as stock
/// kFS discards the main-thread work of a dead host. Also the dispatch-thread fallback
/// (<see cref="BindFromDispatch"/>) for a host created, loaded and ticked before the pump saw it.
/// </summary>
/// <remarks>
/// <para>The game runs the <see cref="Active"/> pump from the <c>KfsIntegration.BeforeInputEvents</c>
/// postfix: every frame, also while paused and while the kFS flag is off (so pruning still
/// happens). With no hosts a frame is one source snapshot (in the game: one lock and an empty
/// copy) and an empty prune loop; with hosts and nothing queued, one lookup per host. It
/// allocates only when it binds or prunes.</para>
/// <para>A factory or drain that throws affects only that host for that frame (logged once per
/// kind); <see cref="RunFrame"/> itself never throws.</para>
/// </remarks>
public sealed class MainThreadPump
{
    private static MainThreadPump? _active;

    private readonly IPumpHostSource _source;
    private readonly List<PumpHost> _hosts = new(8);
    private readonly HashSet<VehicleBinding> _seen = new(ReferenceEqualityComparer.Instance);
    private long _frames, _bound, _pruned, _drained, _fallbackBinds, _failures;

    /// <summary>A pump over <paramref name="source"/>.</summary>
    public MainThreadPump(IPumpHostSource source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        BindFromDispatchDelegate = BindFromDispatch;
    }

    /// <summary>The pump the game hook runs, or null (offline, or not installed with game services).</summary>
    public static MainThreadPump? Active => Volatile.Read(ref _active);

    /// <summary>Runs one frame of the <see cref="Active"/> pump; does nothing when there is none. Called by the <c>BeforeInputEvents</c> postfix.</summary>
    public static void Run() => Volatile.Read(ref _active)?.RunFrame();

    /// <summary>Makes <paramref name="pump"/> the one the game hook runs.</summary>
    internal static void Activate(MainThreadPump pump) => Volatile.Write(ref _active, pump);

    /// <summary>Stops the game hook from running <paramref name="pump"/> (no effect if another pump is active).</summary>
    internal static void Deactivate(MainThreadPump pump) => Interlocked.CompareExchange(ref _active, null, pump);

    /// <summary><see cref="BindFromDispatch"/> as a delegate allocated once (what <see cref="PartsRegistry.Fallback"/> is set to).</summary>
    public Func<VehicleComputer, VehicleBinding?> BindFromDispatchDelegate { get; }

    /// <summary>Frames run.</summary>
    public long Frames => Interlocked.Read(ref _frames);

    /// <summary>Bindings this pump created.</summary>
    public long Bound => Interlocked.Read(ref _bound);

    /// <summary>Bindings it pruned (their host was gone).</summary>
    public long Pruned => Interlocked.Read(ref _pruned);

    /// <summary>Drains run (one per live host per frame).</summary>
    public long Drained => Interlocked.Read(ref _drained);

    /// <summary>Bindings the dispatch-thread fallback created.</summary>
    public long FallbackBinds => Interlocked.Read(ref _fallbackBinds);

    /// <summary>Hosts skipped because building their parts or draining them threw.</summary>
    public long Failures => Interlocked.Read(ref _failures);

    /// <summary>One frame: discover, drain in key order, prune (main thread). Never throws.</summary>
    public void RunFrame()
    {
        Interlocked.Increment(ref _frames);
        try
        {
            _source.Snapshot(_hosts);
        }
        catch (Exception e)
        {
            // Nothing is known about the hosts this frame: prune nothing.
            Interlocked.Increment(ref _failures);
            KfsLog.Once("pump snapshot", e);
            return;
        }
        SortByKey(_hosts);

        _seen.Clear();
        for (int i = 0; i < _hosts.Count; i++)
        {
            VehicleBinding? binding = Bind(_hosts[i]);
            if (binding is null) continue;
            _seen.Add(binding);
            try
            {
                BindingPump.DrainAndApply(binding);
                Interlocked.Increment(ref _drained);
            }
            catch (Exception e)
            {
                Interlocked.Increment(ref _failures);
                KfsLog.Once("pump drain", e);
            }
        }
        _hosts.Clear();   // hold no host between frames
        Prune();
    }

    /// <summary>
    /// The fallback of <see cref="PartsRegistry.Find"/> (dispatch thread): binds
    /// <paramref name="computer"/> when the source knows it as a live host the pump has not seen
    /// yet. Returns null for an unknown computer and while the registry is closed. Binding is
    /// created under the registry's lock, so this and the pump create one binding per computer.
    /// </summary>
    public VehicleBinding? BindFromDispatch(VehicleComputer computer)
    {
        if (computer is null || !_source.TryFind(computer, out PumpHost host)) return null;
        if (PartsRegistry.TryGet(computer, out VehicleBinding? existing)) return existing;
        IVehicleParts parts = _source.CreateParts(host);
        VehicleBinding? binding = PartsRegistry.TryBind(computer, parts);
        if (binding is not null && ReferenceEquals(binding.Parts, parts)) Interlocked.Increment(ref _fallbackBinds);
        return binding;
    }

    private VehicleBinding? Bind(in PumpHost host)
    {
        if (host.Computer is null) return null;
        if (PartsRegistry.TryGet(host.Computer, out VehicleBinding? existing)) return existing;
        try
        {
            IVehicleParts parts = _source.CreateParts(host);
            VehicleBinding? binding = PartsRegistry.TryBind(host.Computer, parts);
            if (binding is not null && ReferenceEquals(binding.Parts, parts)) Interlocked.Increment(ref _bound);
            return binding;
        }
        catch (Exception e)
        {
            Interlocked.Increment(ref _failures);
            KfsLog.Once("pump bind", e);
            return null;
        }
    }

    private void Prune()
    {
        IReadOnlyList<VehicleBinding> live = PartsRegistry.Live;
        for (int i = 0; i < live.Count; i++)
        {
            VehicleBinding binding = live[i];
            if (_seen.Contains(binding)) continue;
            bool owned;
            try
            {
                owned = _source.Owns(binding);
            }
            catch (Exception e)
            {
                KfsLog.Once("pump prune", e);
                continue;
            }
            if (owned && PartsRegistry.Unbind(binding.Computer)) Interlocked.Increment(ref _pruned);
        }
        _seen.Clear();
    }

    /// <summary>
    /// Ordinal order of <see cref="PumpHost.Key"/> — <c>string.CompareOrdinal</c> of the vehicle
    /// ids, as stock kFS sorts its hosts. An insertion sort: a handful of hosts, usually already in
    /// order, and unlike <c>List.Sort(IComparer)</c> it allocates nothing.
    /// </summary>
    internal static void SortByKey(List<PumpHost> hosts)
    {
        for (int i = 1; i < hosts.Count; i++)
        {
            PumpHost host = hosts[i];
            int j = i - 1;
            while (j >= 0 && string.CompareOrdinal(hosts[j].Key, host.Key) > 0)
            {
                hosts[j + 1] = hosts[j];
                j--;
            }
            hosts[j + 1] = host;
        }
    }
}
