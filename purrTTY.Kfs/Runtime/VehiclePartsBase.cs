using System.Collections.Immutable;
using purrTTY.Kfs.Catalog;

namespace purrTTY.Kfs.Runtime;

/// <summary>
/// The shared part of every <see cref="IVehicleParts"/>: publishing the frozen lists, the
/// index/presence checks of the reads, the properties derived from captured descriptors or from
/// raw animation times, the clamps of <see cref="PartMath"/>, and the dispatch of a
/// <see cref="PartCommand"/> to one typed write. A back end supplies only what touches its parts:
/// <see cref="Collect"/>, the live checks, the raw reads and the raw writes.
/// </summary>
/// <remarks>
/// <para>The contract a back end implements against (what the base class guarantees):</para>
/// <list type="bullet">
/// <item><b>Raw reads from the getters</b> (dispatch thread): only for a slot taken from the
/// current <see cref="Sets"/> that <see cref="IsPresent"/> reported present just before.</item>
/// <item><b>Raw writes, and the raw reads <see cref="Apply"/> needs</b>
/// (<see cref="ReadLightType"/> for a cone, <see cref="ReadDurationS"/> and <see cref="ReadGoalS"/>
/// for a goal; pump, main thread): for the slot <b>resolved when the request was accepted</b>,
/// which may belong to an <b>older generation</b> than the current <see cref="Sets"/> — a
/// rescan earlier in the same batch, or a new run between accept and apply, rebuilt the lists.
/// The base class re-checks <see cref="IsPresent"/> on that slot first, so the part is still on
/// the vehicle; but its <see cref="PartSlot.Index"/> may now name a different part (or nothing)
/// in the current lists. A back end must therefore reach the part through the slot object itself
/// (the module reference its slot subclass carries), never by indexing the current lists or any
/// per-generation table with <c>slot.Index</c>. Values arrive clamped by <see cref="PartMath"/>.</item>
/// <item><see cref="IsPresent"/> and <see cref="HasSwitch"/> must accept a slot of any generation
/// and evaluate it live.</item>
/// </list>
/// </remarks>
public abstract class VehiclePartsBase : IVehicleParts
{
    private readonly object _rebuildGate = new();
    private PartSets? _sets;

    /// <summary>The parts of the vehicle as they are now, from which <see cref="Rebuild"/> freezes new lists.</summary>
    /// <param name="Lights">Every light emitter, ranked (the game: by module instance id), with indices 0..n-1.</param>
    /// <param name="Deployables">Every deployable animation, ranked, indices 0..n-1.</param>
    /// <param name="Actuators">Every actuator animation, ranked, indices 0..n-1.</param>
    protected readonly record struct Collected(LightSlot[] Lights, AnimSlot[] Deployables, AnimSlot[] Actuators);

    /// <inheritdoc/>
    /// <remarks>Built on first use when no rebuild happened yet (generation 1).</remarks>
    public PartSets Sets => Volatile.Read(ref _sets) ?? FirstBuild();

    /// <inheritdoc/>
    public abstract bool Dirty { get; }

    /// <inheritdoc/>
    public void Rebuild()
    {
        lock (_rebuildGate)
        {
            PublishLocked();
        }
    }

    /// <summary>
    /// Freezes the vehicle's parts into new slots (called under the rebuild lock, from either
    /// thread). Each array's slot indices must be 0..n-1 in order.
    /// </summary>
    protected abstract Collected Collect();

    /// <inheritdoc/>
    public abstract bool IsPresent(PartSlot slot);

    /// <inheritdoc/>
    public abstract bool HasSwitch(LightSlot slot);

    // ------------------------------------------------------------------ raw reads (present slots only)

    /// <summary>Commanded state of the light's switch; called only when <see cref="HasSwitch"/> is true.</summary>
    protected abstract bool ReadSwitchOn(LightSlot slot);

    /// <summary>True while the light actually emits (switched on and powered; always for interior fill lights).</summary>
    protected abstract bool ReadLit(LightSlot slot);

    /// <summary>The light's type.</summary>
    protected abstract LightType ReadLightType(LightSlot slot);

    /// <summary>True once a program changed the light's appearance (until reset).</summary>
    protected abstract bool ReadOverridden(LightSlot slot);

    /// <summary>A float light property (<see cref="LightProp.Intensity"/> and up).</summary>
    protected abstract float ReadLightF(LightSlot slot, LightProp prop);

    /// <summary>The animation's authored length in seconds.</summary>
    protected abstract double ReadDurationS(AnimSlot slot);

    /// <summary>The animation's current position in seconds (0..duration).</summary>
    protected abstract double ReadPositionS(AnimSlot slot);

    /// <summary>The animation's goal in seconds (0..duration).</summary>
    protected abstract double ReadGoalS(AnimSlot slot);

    /// <summary>
    /// The game's own deployment state of a deployable (<c>KeyframeAnimationModule.DeploymentState</c>,
    /// numbered as the game numbers it: Deployed 0 … Broken 4), or null to derive the state from
    /// <see cref="ReadPositionS"/> and <see cref="ReadGoalS"/> (the default). The base class converts
    /// it with <see cref="PartMath.FromGameDeploymentState"/>, so both back ends report the
    /// program-facing <see cref="DeployState"/> values.
    /// </summary>
    protected virtual int? ReadGameDeploymentState(AnimSlot slot) => null;

    // ------------------------------------------------------------------ raw writes (pump only)

    /// <summary>Switches the light's effective switch (every light sharing it follows).</summary>
    protected abstract void WriteSwitchOn(LightSlot slot, bool on);

    /// <summary>Switches every light of the vehicle that has a switch, like the game's lights key.</summary>
    protected abstract void WriteAllSwitches(bool on);

    /// <summary>Sets the light's colour (components already clamped to [0, 1]).</summary>
    protected abstract void WriteColor(LightSlot slot, float r, float g, float b);

    /// <summary>Sets a spot light's cone (already clamped and ordered; never called for point lights).</summary>
    protected abstract void WriteCone(LightSlot slot, float innerRad, float outerRad);

    /// <summary>Sets the light's intensity (already within [0, <see cref="PartMath.MaxIntensity"/>]).</summary>
    protected abstract void WriteIntensity(LightSlot slot, float intensity);

    /// <summary>Sets the light's range in metres (already within [0, <see cref="PartMath.MaxRangeM"/>]).</summary>
    protected abstract void WriteRange(LightSlot slot, float rangeM);

    /// <summary>Restores the light's stock appearance (not its on/off state).</summary>
    protected abstract void WriteReset(LightSlot slot);

    /// <summary>Sets the animation's goal in seconds (already within 0..duration).</summary>
    protected abstract void WriteGoalS(AnimSlot slot, double goalS);

    // ------------------------------------------------------------------ reads

    /// <inheritdoc/>
    public uint LightGetI(int index, LightProp prop)
    {
        if (Light(index) is not { } slot) return 0u;
        return prop switch
        {
            LightProp.Present => 1u,
            LightProp.HasSwitch => Bit(HasSwitch(slot)),
            LightProp.On => HasSwitch(slot) ? Bit(ReadSwitchOn(slot)) : 1u,
            LightProp.Lit => Bit(ReadLit(slot)),
            LightProp.Type => (uint)ReadLightType(slot),
            LightProp.PartId => slot.PartId,
            LightProp.SwitchGroup => (uint)slot.SwitchGroup,
            LightProp.Overridden => Bit(ReadOverridden(slot)),
            _ => 0u,
        };
    }

    /// <inheritdoc/>
    public float LightGetF(int index, LightProp prop)
    {
        if (prop < LightProp.Intensity || Light(index) is not { } slot) return 0f;
        return ReadLightF(slot, prop);
    }

    /// <inheritdoc/>
    public uint DeployGetI(int index, DeployProp prop)
    {
        if (Anim(Sets.Deployables, index) is not { } slot) return 0u;
        return prop switch
        {
            DeployProp.Present => 1u,
            DeployProp.State => (uint)DeployStateOf(slot),
            DeployProp.Commanded => Bit(ReadGoalS(slot) > 0),
            DeployProp.Moving => Bit(PartMath.Moving(ReadPositionS(slot), ReadGoalS(slot))),
            DeployProp.PartId => slot.PartId,
            DeployProp.Links => slot.Links,
            _ => 0u,
        };
    }

    /// <inheritdoc/>
    public float DeployGetF(int index, DeployProp prop)
    {
        if (Anim(Sets.Deployables, index) is not { } slot) return 0f;
        return prop switch
        {
            DeployProp.Position => PartMath.SecondsToFraction(ReadPositionS(slot), ReadDurationS(slot)),
            DeployProp.Target => PartMath.SecondsToFraction(ReadGoalS(slot), ReadDurationS(slot)),
            DeployProp.DurationS => (float)ReadDurationS(slot),
            _ => 0f,
        };
    }

    /// <inheritdoc/>
    public uint ActuatorGetI(int index, ActuatorProp prop)
    {
        if (Anim(Sets.Actuators, index) is not { } slot) return 0u;
        return prop switch
        {
            ActuatorProp.Present => 1u,
            ActuatorProp.Moving => Bit(PartMath.Moving(ReadPositionS(slot), ReadGoalS(slot))),
            ActuatorProp.PartId => slot.PartId,
            ActuatorProp.Links => slot.Links,
            _ => 0u,
        };
    }

    /// <inheritdoc/>
    public float ActuatorGetF(int index, ActuatorProp prop)
    {
        if (Anim(Sets.Actuators, index) is not { } slot) return 0f;
        return prop switch
        {
            ActuatorProp.Position => PartMath.SecondsToFraction(ReadPositionS(slot), ReadDurationS(slot)),
            ActuatorProp.Target => PartMath.SecondsToFraction(ReadGoalS(slot), ReadDurationS(slot)),
            ActuatorProp.DurationS => (float)ReadDurationS(slot),
            _ => 0f,
        };
    }

    // ------------------------------------------------------------------ commands

    /// <inheritdoc/>
    /// <remarks>
    /// The command's slot may be of an older generation (see the class remarks). A slot whose part
    /// is no longer present is refused here too (<see cref="PartCommandException"/>, no raw call),
    /// so no caller can hand a raw write a departed part; the pump checks first and counts such a
    /// command as discarded.
    /// </remarks>
    public virtual void Apply(in PartCommand command)
    {
        if (command.Slot is { } target && !IsPresent(target))
            throw new PartCommandException($"{target.Noun} {target.Index} left the vehicle");
        switch (command.Service)
        {
            case Svc.LightSetOn:
            {
                LightSlot light = LightOf(command);
                if (!HasSwitch(light)) throw new PartCommandException($"light {light.Index} has no switch");
                WriteSwitchOn(light, command.U != 0);
                break;
            }
            case Svc.LightSetColor:
                WriteColor(LightOf(command), PartMath.Color(command.A), PartMath.Color(command.B), PartMath.Color(command.C));
                break;
            case Svc.LightSetCone:
            {
                LightSlot light = LightOf(command);
                if (ReadLightType(light) == LightType.Point) break;   // a point light has no cone
                var (inner, outer) = PartMath.Cone(command.A, command.B);
                WriteCone(light, inner, outer);
                break;
            }
            case Svc.LightSetIntensity:
                WriteIntensity(LightOf(command), PartMath.Intensity(command.A));
                break;
            case Svc.LightSetRange:
                WriteRange(LightOf(command), PartMath.RangeM(command.A));
                break;
            case Svc.LightReset:
                WriteReset(LightOf(command));
                break;
            case Svc.LightsSetAll:
                WriteAllSwitches(command.U != 0);
                break;
            case Svc.DeploySet:
            {
                AnimSlot anim = AnimOf(command, PartKind.Deployable);
                WriteGoalS(anim, PartMath.FractionToSeconds(command.U != 0 ? 1f : 0f, ReadDurationS(anim)));
                break;
            }
            case Svc.DeployToggle:
            {
                AnimSlot anim = AnimOf(command, PartKind.Deployable);
                double duration = ReadDurationS(anim);
                WriteGoalS(anim, ReadGoalS(anim) > 0 ? 0d : PartMath.FractionToSeconds(1f, duration));
                break;
            }
            case Svc.ActuatorSet:
            {
                AnimSlot anim = AnimOf(command, PartKind.Actuator);
                WriteGoalS(anim, PartMath.FractionToSeconds(command.A, ReadDurationS(anim)));
                break;
            }
            default:
                throw new PartCommandException($"{command.Name} is not a parts command");
        }
    }

    // ------------------------------------------------------------------ helpers

    private static uint Bit(bool value) => value ? 1u : 0u;

    private DeployState DeployStateOf(AnimSlot slot) =>
        ReadGameDeploymentState(slot) is int game
            ? PartMath.FromGameDeploymentState(game)
            : PartMath.State(ReadPositionS(slot), ReadGoalS(slot));

    private LightSlot? Light(int index)
    {
        ImmutableArray<LightSlot> lights = Sets.Lights;
        if ((uint)index >= (uint)lights.Length) return null;
        LightSlot slot = lights[index];
        return IsPresent(slot) ? slot : null;
    }

    private AnimSlot? Anim(ImmutableArray<AnimSlot> list, int index)
    {
        if ((uint)index >= (uint)list.Length) return null;
        AnimSlot slot = list[index];
        return IsPresent(slot) ? slot : null;
    }

    private static LightSlot LightOf(in PartCommand command) =>
        command.Slot as LightSlot ?? throw new PartCommandException($"{command.Name} needs a light");

    private static AnimSlot AnimOf(in PartCommand command, PartKind kind) =>
        command.Slot is AnimSlot anim && anim.Kind == kind
            ? anim
            : throw new PartCommandException($"{command.Name} needs a {PartsText.Noun(kind)}");

    private PartSets FirstBuild()
    {
        lock (_rebuildGate)
        {
            return Volatile.Read(ref _sets) ?? PublishLocked();
        }
    }

    private PartSets PublishLocked()
    {
        int generation = (Volatile.Read(ref _sets)?.Generation ?? 0) + 1;
        Collected parts = Collect();
        var sets = new PartSets(generation,
            ImmutableArray.Create(parts.Lights ?? []),
            ImmutableArray.Create(parts.Deployables ?? []),
            ImmutableArray.Create(parts.Actuators ?? []));
        Volatile.Write(ref _sets, sets);
        return sets;
    }
}
