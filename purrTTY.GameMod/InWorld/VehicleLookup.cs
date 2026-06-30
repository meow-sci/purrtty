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

    /// <summary>
    ///     Finds the vehicle that currently contains <paramref name="part"/> (a top-level
    ///     part or a sub-part), matched by <b>object identity</b>, or null if the part is
    ///     no longer attached to any vehicle (e.g. destroyed). Used to follow a
    ///     specifically-anchored part across a vessel split/dock: KSA moves the same
    ///     <see cref="Part"/> instance into the new vehicle (it does not rebuild it), so a
    ///     reference match tracks it. Identity is session-only (the running id / object is
    ///     per-run), so a persisted anchor still re-resolves from vehicle + part id on load.
    /// </summary>
    public static Vehicle? FindContaining(Part part)
    {
        var system = Universe.CurrentSystem;
        if (system == null)
        {
            return null;
        }

        foreach (var vehicle in system.All.UnsafeAsList().OfType<Vehicle>())
        {
            foreach (Part p in vehicle.Parts.Parts)
            {
                if (ReferenceEquals(p, part))
                {
                    return vehicle;
                }

                foreach (Part sub in p.SubParts)
                {
                    if (ReferenceEquals(sub, part))
                    {
                        return vehicle;
                    }
                }
            }
        }

        return null;
    }
}
