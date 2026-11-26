using purrTTY.Kfs.Catalog;

namespace purrTTY.Kfs.Runtime;

/// <summary>
/// A vehicle's lights and animations as kFS programs see them (plan §3.2): frozen lists addressed
/// by index, live reads, and commands applied by the pump. Two implementations exist — the
/// simulated parts of the test runtime and the game's live modules — and the router, the pump and
/// the host hooks are identical for both. Most implementations derive from
/// <see cref="VehiclePartsBase"/>, which supplies everything that is not a raw read or write.
/// </summary>
/// <remarks>
/// <para><b>Threads.</b> <see cref="Sets"/>, <see cref="Dirty"/>, <see cref="IsPresent"/>,
/// <see cref="HasSwitch"/> and the <c>…Get…</c> reads are called on the kFS dispatch thread (a
/// solver worker in the game), concurrently with the main thread: they must not write anything,
/// block, throw by design or allocate. <see cref="Rebuild"/> is called from either thread (on the
/// dispatch thread or by the pump when a run starts, on the main thread for <c>x_parts_rescan</c>)
/// and must be guarded. <see cref="Apply"/> is called only by <see cref="BindingPump"/> — the main thread in
/// the game, where no solver job runs.</para>
/// <para><b>Generations.</b> <see cref="IsPresent"/>, <see cref="HasSwitch"/> and
/// <see cref="Apply"/> receive slots of <b>any</b> generation: a command holds the slot resolved
/// at accept time, and the lists may have been rebuilt since (a rescan earlier in the batch, a new
/// run). An implementation reaches the part through the slot object, never through
/// <see cref="PartSlot.Index"/> into its current lists (see <see cref="VehiclePartsBase"/>).</para>
/// <para><b>Indices.</b> The reads take the index a program passed and return 0 for an index out
/// of range or a slot whose part is no longer on the vehicle (<c>…_PRESENT</c> then reads 0 too).</para>
/// </remarks>
public interface IVehicleParts
{
    /// <summary>The frozen lists; a new instance after every <see cref="Rebuild"/>. Never null.</summary>
    PartSets Sets { get; }

    /// <summary>True when the vehicle's part counts differ from the frozen <see cref="Sets"/> (a hint to rescan).</summary>
    bool Dirty { get; }

    /// <summary>
    /// Rebuilds <see cref="Sets"/> from the vehicle as it is now and increments the generation.
    /// Thread-safe. Called when a run starts (the first service call or pump after a start, reset
    /// or stop → start; see <see cref="VehicleBinding"/>) and for <c>x_parts_rescan</c>.
    /// </summary>
    void Rebuild();

    /// <summary>True while the slot's part is still on this vehicle (evaluated live).</summary>
    bool IsPresent(PartSlot slot);

    /// <summary>True when the light has a usable switch now: one on this vehicle, and not an interior fill light.</summary>
    bool HasSwitch(LightSlot slot);

    /// <summary>An integer property (<see cref="LightProp"/> below <see cref="PropertyRanges.FirstFloat"/>) of light <paramref name="index"/>; 0 when out of range or departed.</summary>
    uint LightGetI(int index, LightProp prop);

    /// <summary>A float property of light <paramref name="index"/>; 0 when out of range or departed.</summary>
    float LightGetF(int index, LightProp prop);

    /// <summary>An integer property of deployable <paramref name="index"/>; 0 when out of range or departed.</summary>
    uint DeployGetI(int index, DeployProp prop);

    /// <summary>A float property of deployable <paramref name="index"/>; 0 when out of range or departed.</summary>
    float DeployGetF(int index, DeployProp prop);

    /// <summary>An integer property of actuator <paramref name="index"/>; 0 when out of range or departed.</summary>
    uint ActuatorGetI(int index, ActuatorProp prop);

    /// <summary>A float property of actuator <paramref name="index"/>; 0 when out of range or departed.</summary>
    float ActuatorGetF(int index, ActuatorProp prop);

    /// <summary>
    /// Applies one command (pump only). <c>x_parts_rescan</c> never arrives here: the pump calls
    /// <see cref="Rebuild"/> for it. The command's slot may be of an older generation; the pump has
    /// checked that it is still present (a departed one is discarded and counted there). An
    /// exception is caught by the pump, counted and reported to the program.
    /// </summary>
    void Apply(in PartCommand command);
}

/// <summary>A command that could not be applied, with a reason fit for the program log.</summary>
public sealed class PartCommandException(string message) : InvalidOperationException(message);
