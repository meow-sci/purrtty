using KSA.Kfs;
using KSA.Kfs.Host;
using purrTTY.Kfs.Runtime;

namespace purrTTY.Kfs.Game;

/// <summary>
/// The game's hosts for <see cref="MainThreadPump"/>: <see cref="HostDirectory"/> filtered to the
/// hosts that are alive (<c>KfsVehicleHost.IsAlive</c>: vehicle not disposed, in the current star
/// system, still pointing at this host). The game's own <c>ReleaseDead</c> sweep runs only while the
/// kFS flag is on, so a dead host can stay in <c>AllHosts</c> while it is off; filtering here lets
/// the pump prune its binding all the same, and the fallback never binds one.
/// </summary>
public sealed class GameHostSource(HostDirectory directory) : IPumpHostSource
{
    private readonly HostDirectory _directory = directory ?? throw new ArgumentNullException(nameof(directory));
    private readonly List<KfsVehicleHost> _scratch = new(8);

    /// <inheritdoc/>
    public void Snapshot(List<PumpHost> into)
    {
        into.Clear();
        _directory.Snapshot(_scratch);
        try
        {
            for (int i = 0; i < _scratch.Count; i++)
            {
                KfsVehicleHost host = _scratch[i];
                if (host.IsAlive) into.Add(new PumpHost(host.Computer, host.Id ?? string.Empty, host));
            }
        }
        finally
        {
            _scratch.Clear();   // hold no host between frames
        }
    }

    /// <inheritdoc/>
    public bool TryFind(VehicleComputer computer, out PumpHost host)
    {
        if (_directory.FindByComputer(computer) is { IsAlive: true } found)
        {
            host = new PumpHost(found.Computer, found.Id ?? string.Empty, found);
            return true;
        }
        host = default;
        return false;
    }

    /// <inheritdoc/>
    public IVehicleParts CreateParts(in PumpHost host) =>
        host.Source is KfsVehicleHost game
            ? new KsaVehicleParts(game.Vehicle)
            : throw new ArgumentException("not a host of the game", nameof(host));

    /// <inheritdoc/>
    public bool Owns(VehicleBinding binding) => binding.Parts is KsaVehicleParts;
}
