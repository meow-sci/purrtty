using System.Linq;
using KSA;

namespace purrTTY.GameMod.InWorld;

/// <summary>
///     Vehicle enumeration + resolution for in-world part anchoring. A part anchor can
///     target <b>any</b> vehicle in the current system, not just the controlled one;
///     an empty target id means "the controlled vehicle" so the anchor follows the
///     player by default. Mirrors the KSA registry walk
///     (<c>Universe.CurrentSystem.All</c>) used across the mods.
/// </summary>
public static class VehicleLookup
{
    /// <summary>All vehicles in the current system (empty when there is no system).</summary>
    public static List<Vehicle> GetAll() =>
        Universe.CurrentSystem?.All.UnsafeAsList().OfType<Vehicle>().ToList() ?? new List<Vehicle>();

    /// <summary>
    ///     Resolves a target-vehicle id to a vehicle: blank → the controlled vehicle;
    ///     otherwise the vehicle with that id, falling back to the controlled vehicle
    ///     if it can no longer be found (e.g. it was destroyed or left the system).
    /// </summary>
    public static Vehicle? Resolve(string? id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return Program.ControlledVehicle;
        }

        var system = Universe.CurrentSystem;
        if (system != null)
        {
            foreach (var vehicle in system.All.UnsafeAsList().OfType<Vehicle>())
            {
                if (vehicle.Id == id)
                {
                    return vehicle;
                }
            }
        }

        return Program.ControlledVehicle;
    }
}
