using KSA.Kfs.Host;
using purrTTY.Kfs.Game;
using purrTTY.Kfs.Runtime;

namespace purrTTY.Kfs.Testing;

/// <summary>
/// Simulated game hosts for <see cref="MainThreadPump"/>: what <c>KfsVehicleHost.AllHosts</c> is
/// in the game, as a list the test edits — each host a game <see cref="FakeVehicle"/> computer
/// with a key standing in for <c>Vehicle.Id</c>, whose parts come from <see cref="Factory"/>.
/// Thread-safe, so the pump (main) and the dispatch-thread fallback can use it at once.
/// </summary>
public sealed class SimHostSource : IPumpHostSource
{
    private readonly object _gate = new();
    private readonly KfsRuntime _runtime;
    private readonly List<(string Key, FakeVehicle Fake)> _hosts = [];
    private readonly HashSet<IVehicleParts> _created = new(ReferenceEqualityComparer.Instance);
    private int _snapshots, _createdCount;

    /// <summary>Hosts on the computers of fake vehicles built on <paramref name="runtime"/>'s tables.</summary>
    public SimHostSource(KfsRuntime runtime) => _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));

    /// <summary>Builds a host's parts from its key (default: one simulated light). May throw (fault injection).</summary>
    public Func<string, IVehicleParts> Factory { get; set; } = _ => new SimVehicleParts(lights: 1);

    /// <summary>Snapshots taken.</summary>
    public int Snapshots
    {
        get { lock (_gate) return _snapshots; }
    }

    /// <summary>Parts back ends <see cref="Factory"/> built for the pump or the fallback (including losers of a bind race).</summary>
    public int CreatedCount
    {
        get { lock (_gate) return _createdCount; }
    }

    /// <summary>Adds a live host; returns its computer.</summary>
    public VehicleComputer Add(string key)
    {
        var fake = new FakeVehicle(_runtime.Tables, 1, key);
        lock (_gate) _hosts.Add((key, fake));
        return fake.Computer;
    }

    /// <summary>Removes the host <paramref name="key"/> (its vehicle died); false when there was none.</summary>
    public bool Remove(string key)
    {
        lock (_gate) return _hosts.RemoveAll(h => h.Key == key) > 0;
    }

    /// <summary>The computer of host <paramref name="key"/>.</summary>
    public VehicleComputer Computer(string key)
    {
        lock (_gate) return _hosts.First(h => h.Key == key).Fake.Computer;
    }

    /// <summary>Every computer this source ever listed, current hosts first (for cleaning up).</summary>
    public IReadOnlyList<VehicleComputer> Computers
    {
        get { lock (_gate) return _hosts.Select(h => h.Fake.Computer).ToList(); }
    }

    /// <inheritdoc/>
    public void Snapshot(List<PumpHost> into)
    {
        into.Clear();
        lock (_gate)
        {
            _snapshots++;
            foreach (var (key, fake) in _hosts) into.Add(new PumpHost(fake.Computer, key, key));
        }
    }

    /// <inheritdoc/>
    public bool TryFind(VehicleComputer computer, out PumpHost host)
    {
        lock (_gate)
        {
            foreach (var (key, fake) in _hosts)
            {
                if (!ReferenceEquals(fake.Computer, computer)) continue;
                host = new PumpHost(fake.Computer, key, key);
                return true;
            }
        }
        host = default;
        return false;
    }

    /// <inheritdoc/>
    public IVehicleParts CreateParts(in PumpHost host)
    {
        IVehicleParts parts = Factory((string)host.Source!);
        lock (_gate)
        {
            _created.Add(parts);
            _createdCount++;
        }
        return parts;
    }

    /// <inheritdoc/>
    public bool Owns(VehicleBinding binding)
    {
        lock (_gate) return _created.Contains(binding.Parts);
    }
}
