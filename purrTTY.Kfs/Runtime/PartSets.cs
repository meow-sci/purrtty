using System.Collections.Immutable;
using purrTTY.Kfs.Catalog;

namespace purrTTY.Kfs.Runtime;

/// <summary>
/// One part module in a frozen list: the descriptors captured when the list was built. A queued
/// <see cref="PartCommand"/> holds the slot itself, not its index, so a rebuild between accept and
/// apply cannot redirect it. Back ends derive from <see cref="LightSlot"/> / <see cref="AnimSlot"/>
/// to carry their own module reference (a game <c>LightModule</c>, a simulated light).
/// </summary>
public abstract class PartSlot
{
    /// <summary>Creates a slot.</summary>
    /// <param name="index">Position in its list (what a program passes).</param>
    /// <param name="partId">The part's instance id (session-only).</param>
    /// <param name="partName">The part's display name.</param>
    /// <param name="key">A stable key of the part's kind (the game's template id), or empty.</param>
    protected PartSlot(int index, uint partId, string partName, string key)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        Index = index;
        PartId = partId;
        PartName = partName ?? string.Empty;
        Key = key ?? string.Empty;
    }

    /// <summary>Which list the slot belongs to.</summary>
    public abstract PartKind Kind { get; }

    /// <summary>Position in its list.</summary>
    public int Index { get; }

    /// <summary>The part's instance id, captured at build time.</summary>
    public uint PartId { get; }

    /// <summary>The part's display name, captured at build time.</summary>
    public string PartName { get; }

    /// <summary>A stable key of the part's kind (the game's template id), captured at build time; may be empty.</summary>
    public string Key { get; }

    /// <summary>The noun programs see in texts: <c>light</c>, <c>deployable</c>, <c>actuator</c>.</summary>
    public string Noun => PartsText.Noun(Kind);

    /// <inheritdoc/>
    public override string ToString() => $"{Noun} {Index}: {PartName} (part {PartId})";
}

/// <summary>A light emitter in the frozen light list.</summary>
public class LightSlot : PartSlot
{
    /// <summary>Creates a light slot.</summary>
    /// <param name="index">Position in the light list.</param>
    /// <param name="partId">The part's instance id.</param>
    /// <param name="partName">The part's display name.</param>
    /// <param name="key">The part kind's key, or empty.</param>
    /// <param name="switchGroup">Lowest light index sharing this light's switch at build time (its own index when it has none); see <see cref="PartMath.SwitchGroups{TSwitch}"/>.</param>
    public LightSlot(int index, uint partId, string partName, string key, int switchGroup)
        : base(index, partId, partName, key)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(switchGroup);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(switchGroup, index);
        SwitchGroup = switchGroup;
    }

    /// <inheritdoc/>
    public override PartKind Kind => PartKind.Light;

    /// <summary>Lowest light index sharing this light's switch, captured at build time.</summary>
    public int SwitchGroup { get; }
}

/// <summary>An animation (deployable or actuator) in a frozen list.</summary>
public class AnimSlot : PartSlot
{
    /// <summary>Link flag: the animation drives a solar panel.</summary>
    public const uint LinkSolarPanel = 1;

    /// <summary>Link flag: a parachute's state machine also writes the goal.</summary>
    public const uint LinkParachute = 2;

    /// <summary>Link flag: the animation aims a light.</summary>
    public const uint LinkLight = 4;

    /// <summary>Creates an animation slot.</summary>
    /// <param name="kind"><see cref="PartKind.Deployable"/> or <see cref="PartKind.Actuator"/>.</param>
    /// <param name="index">Position in its list.</param>
    /// <param name="partId">The part's instance id.</param>
    /// <param name="partName">The part's display name.</param>
    /// <param name="key">The part kind's key, or empty.</param>
    /// <param name="links">The <c>Link*</c> flags, captured at build time.</param>
    public AnimSlot(PartKind kind, int index, uint partId, string partName, string key, uint links)
        : base(index, partId, partName, key)
    {
        if (kind is not (PartKind.Deployable or PartKind.Actuator))
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "an animation is a deployable or an actuator");
        Kind = kind;
        Links = links;
    }

    /// <inheritdoc/>
    public override PartKind Kind { get; }

    /// <summary>The <c>Link*</c> flags captured at build time.</summary>
    public uint Links { get; }
}

/// <summary>
/// The frozen part lists a program addresses by index, plus their generation. Immutable once
/// constructed; a back end publishes a new instance on every rebuild (with <see cref="Volatile.Write{T}(ref T, T)"/>),
/// so a reader on the dispatch thread sees either the old or the new lists, never a mix.
/// </summary>
public sealed class PartSets
{
    /// <summary>No parts, generation 0.</summary>
    public static PartSets Empty { get; } = new(0, [], [], []);

    /// <summary>Creates the lists; each slot's <see cref="PartSlot.Index"/> must equal its position and its kind must match the list.</summary>
    /// <exception cref="ArgumentException">A slot is misplaced or of the wrong kind.</exception>
    public PartSets(int generation, ImmutableArray<LightSlot> lights, ImmutableArray<AnimSlot> deployables, ImmutableArray<AnimSlot> actuators)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(generation);
        Check(lights, PartKind.Light, nameof(lights));
        Check(deployables, PartKind.Deployable, nameof(deployables));
        Check(actuators, PartKind.Actuator, nameof(actuators));
        Generation = generation;
        Lights = lights;
        Deployables = deployables;
        Actuators = actuators;
    }

    /// <summary>Changes on every rebuild (each new run, <c>x_parts_rescan</c>); starts at 1 for the first build.</summary>
    public int Generation { get; }

    /// <summary>The lights, by index.</summary>
    public ImmutableArray<LightSlot> Lights { get; }

    /// <summary>The deployables, by index.</summary>
    public ImmutableArray<AnimSlot> Deployables { get; }

    /// <summary>The actuators, by index.</summary>
    public ImmutableArray<AnimSlot> Actuators { get; }

    /// <summary>Length of the list of <paramref name="kind"/> (<see cref="PartKind.All"/>: the sum).</summary>
    public int Count(PartKind kind) => kind switch
    {
        PartKind.Light => Lights.Length,
        PartKind.Deployable => Deployables.Length,
        PartKind.Actuator => Actuators.Length,
        _ => Lights.Length + Deployables.Length + Actuators.Length,
    };

    /// <summary>The slot at <paramref name="index"/> of the list of <paramref name="kind"/>, or null when out of range.</summary>
    public PartSlot? Slot(PartKind kind, uint index) => kind switch
    {
        PartKind.Light => index < (uint)Lights.Length ? Lights[(int)index] : null,
        PartKind.Deployable => index < (uint)Deployables.Length ? Deployables[(int)index] : null,
        PartKind.Actuator => index < (uint)Actuators.Length ? Actuators[(int)index] : null,
        _ => null,
    };

    private static void Check<TSlot>(ImmutableArray<TSlot> slots, PartKind kind, string name) where TSlot : PartSlot
    {
        if (slots.IsDefault) throw new ArgumentException("default array", name);
        for (int i = 0; i < slots.Length; i++)
        {
            TSlot slot = slots[i] ?? throw new ArgumentException($"slot {i} is null", name);
            if (slot.Index != i) throw new ArgumentException($"slot at position {i} has index {slot.Index}", name);
            if (slot.Kind != kind) throw new ArgumentException($"slot {i} is a {slot.Kind}, not a {kind}", name);
        }
    }
}
