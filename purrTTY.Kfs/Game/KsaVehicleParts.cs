using KSA;
using purrTTY.Kfs.Catalog;
using purrTTY.Kfs.Runtime;
using AnimStates = KSA.ModuleStateful<KSA.KeyframeAnimationModule, KSA.KeyframeAnimationModule.State, KSA.EmptyStruct, KSA.EmptyStruct>;

namespace purrTTY.Kfs.Game;

/// <summary>A light slot of the game back end: the live <see cref="LightModule"/> it addresses.</summary>
public sealed class KsaLightSlot : LightSlot
{
    internal KsaLightSlot(int index, LightModule module, int switchGroup)
        : base(index, KsaVehicleParts.PartIdOf(module), KsaVehicleParts.PartNameOf(module), KsaVehicleParts.KeyOf(module), switchGroup)
    {
        Module = module;
    }

    /// <summary>The live module (never null).</summary>
    public LightModule Module { get; }
}

/// <summary>An animation slot of the game back end: the live <see cref="KeyframeAnimationModule"/> it addresses.</summary>
public sealed class KsaAnimSlot : AnimSlot
{
    internal KsaAnimSlot(PartKind kind, int index, KeyframeAnimationModule module, uint links)
        : base(kind, index, KsaVehicleParts.PartIdOf(module), KsaVehicleParts.PartNameOf(module), KsaVehicleParts.KeyOf(module), links)
    {
        Module = module;
    }

    /// <summary>The live module (never null).</summary>
    public KeyframeAnimationModule Module { get; }
}

/// <summary>
/// The game back end of <see cref="IVehicleParts"/> (plan §4.8, §4.10, §4.11): a live
/// <see cref="Vehicle"/>'s lights and keyframe animations. Supplies only what touches game
/// objects — collection, the live checks, raw reads and raw writes — on top of
/// <see cref="VehiclePartsBase"/>, exactly as <c>SimVehicleParts</c> does for the test runtime.
/// </summary>
/// <remarks>
/// <para><b>Lists</b> (<see cref="Collect"/>): lights are every <see cref="LightModule"/> of the
/// vehicle's part tree (emitters, interior fill lights included), ordered by module
/// <see cref="ModuleBase.InstanceId"/>; animations are every <see cref="KeyframeAnimationModule"/>
/// whose <c>Shared.Duration</c> is above zero, ordered the same way and split by
/// <see cref="KeyframeAnimationModule.ShowDeployRetract"/> into deployables and actuators. The
/// descriptors (part instance id, display name, key, switch group, link flags) are captured then.
/// <see cref="Collect"/> may run on a dispatch thread (the first service call of a new run): it
/// only reads.</para>
/// <para><b>Dispatch-thread members</b> (<see cref="Dirty"/>, <see cref="IsPresent"/>,
/// <see cref="HasSwitch"/>, every raw read): single field reads and bounds-checked span reads, no
/// allocation, no LINQ, no locks. The part tree is restructured only on the main thread in the
/// solver-idle window, when no dispatch runs; the UI phase may still change single fields (the
/// part window's checkbox and slider) concurrently, which reads at worst one value stale (plan §9 R7).</para>
/// <para><b>Writes</b> — main thread only, called by the pump: switch writes go to the light's
/// effective switch read live each time; appearance writes go to the light's private template
/// (<see cref="LightOverrides"/>); animation commands write <see cref="KeyframeAnimationModule.TimeGoal"/>.
/// <c>KeyframeAnimationModule.State</c> and <c>PowerConsumerState</c> are never written: they are
/// outputs of the physics step.</para>
/// <para><b>Generations</b>: every member that takes a slot reaches the part through the slot's
/// module reference, never through <see cref="PartSlot.Index"/>, so a slot of an older generation
/// is handled correctly.</para>
/// </remarks>
public sealed class KsaVehicleParts : VehiclePartsBase
{
    private static readonly Comparison<LightModule> LightsByInstanceId = static (a, b) => a.InstanceId.CompareTo(b.InstanceId);
    private static readonly Comparison<KeyframeAnimationModule> AnimationsByInstanceId = static (a, b) => a.InstanceId.CompareTo(b.InstanceId);

    /// <summary>The parts of <paramref name="vehicle"/>. Builds nothing until the lists are first needed.</summary>
    public KsaVehicleParts(Vehicle vehicle)
    {
        Vehicle = vehicle ?? throw new ArgumentNullException(nameof(vehicle));
    }

    /// <summary>The vehicle (a host's vehicle never changes).</summary>
    public Vehicle Vehicle { get; }

    // ------------------------------------------------------------------ live checks (dispatch thread)

    /// <inheritdoc/>
    public override bool Dirty
    {
        get
        {
            PartSets sets = Sets;
            PartTree? tree = LiveTree();
            if (tree is null) return sets.Count(PartKind.All) > 0;   // gone: every frozen slot departed

            ModuleList modules = tree.Modules;
            int lights = modules.Get<LightModule>().Length;
            int deployables = 0, actuators = 0;
            Span<KeyframeAnimationModule> animations = modules.Get<KeyframeAnimationModule>();
            for (int i = 0; i < animations.Length; i++)
            {
                KeyframeAnimationModule m = animations[i];
                if (!HasTravel(m)) continue;
                if (m.ShowDeployRetract) deployables++;
                else actuators++;
            }
            return KsaPartRules.Dirty(lights, deployables, actuators,
                sets.Lights.Length, sets.Deployables.Length, sets.Actuators.Length, AnyDeparted(sets, tree));
        }
    }

    /// <inheritdoc/>
    /// <remarks>The bundle's recipe P-1: the module's part is on this vehicle's part tree and the vehicle is not disposed.</remarks>
    public override bool IsPresent(PartSlot slot)
    {
        PartTree? tree = LiveTree();
        if (tree is null) return false;
        return slot switch
        {
            KsaLightSlot light => OnTree(light.Module, tree),
            KsaAnimSlot anim => OnTree(anim.Module, tree),
            _ => false,
        };
    }

    /// <inheritdoc/>
    public override bool HasSwitch(LightSlot slot) => slot is KsaLightSlot light && EffectiveSwitch(light.Module) is not null;

    // ------------------------------------------------------------------ collection (either thread; reads only)

    /// <inheritdoc/>
    protected override Collected Collect()
    {
        PartTree? tree = LiveTree();
        if (tree is null) return new Collected([], [], []);
        ModuleList modules = tree.Modules;

        // Lights, by module instance id; switch groups from the effective switches.
        LightModule[] lights = modules.Get<LightModule>().ToArray();
        Array.Sort(lights, LightsByInstanceId);
        var switches = new PowerConsumer?[lights.Length];
        for (int i = 0; i < lights.Length; i++) switches[i] = EffectiveSwitch(lights[i], tree);
        int[] groups = PartMath.SwitchGroups(switches);
        var lightSlots = new LightSlot[lights.Length];
        for (int i = 0; i < lights.Length; i++) lightSlots[i] = new KsaLightSlot(i, lights[i], groups[i]);

        // Animations with a usable length, by module instance id, split by the stock widget.
        Span<KeyframeAnimationModule> all = modules.Get<KeyframeAnimationModule>();
        var animations = new List<KeyframeAnimationModule>(all.Length);
        foreach (KeyframeAnimationModule m in all)
        {
            if (HasTravel(m)) animations.Add(m);
        }
        animations.Sort(AnimationsByInstanceId);

        HashSet<KeyframeAnimationModule> solarDriven = SolarDriven(modules);
        var deployables = new List<AnimSlot>();
        var actuators = new List<AnimSlot>();
        foreach (KeyframeAnimationModule m in animations)
        {
            uint links = LinksOf(m, solarDriven);
            if (KsaPartRules.AnimationKind(m.ShowDeployRetract) == PartKind.Deployable)
                deployables.Add(new KsaAnimSlot(PartKind.Deployable, deployables.Count, m, links));
            else
                actuators.Add(new KsaAnimSlot(PartKind.Actuator, actuators.Count, m, links));
        }
        return new Collected(lightSlots, [.. deployables], [.. actuators]);
    }

    // ------------------------------------------------------------------ raw reads (dispatch thread, present slots)

    /// <inheritdoc/>
    protected override bool ReadSwitchOn(LightSlot slot) => EffectiveSwitch(L(slot)) is not { } sw || sw.LightIsActive;

    /// <inheritdoc/>
    /// <remarks>
    /// <c>LightModule.IsActive</c> = <c>Template.DisableInIva || !Parent.FullPart.IsLightSwitchedOff()</c>, which is
    /// true for an interior fill light, for a part with no light switch and for a switch on another tree; otherwise
    /// it needs the switch's <b>live</b> <c>LightIsActive</c> and its <b>committed</b> <c>PowerConsumerState.Active</c>
    /// (<c>PowerConsumer.IsSwitchedOn</c>; on and powered as of the last physics step). Switching off therefore
    /// reads dark at once, switching on reads lit one frame later.
    /// </remarks>
    protected override bool ReadLit(LightSlot slot) => L(slot).IsActive;

    /// <inheritdoc/>
    protected override LightType ReadLightType(LightSlot slot) =>
        L(slot).Template is { } t ? KsaPartRules.LightTypeOf((int)t.Type) : LightType.Point;

    /// <inheritdoc/>
    protected override bool ReadOverridden(LightSlot slot) => LightOverrides.IsOverridden(L(slot));

    /// <inheritdoc/>
    protected override float ReadLightF(LightSlot slot, LightProp prop)
    {
        LightModule m = L(slot);
        if (prop == LightProp.StockIntensity) return LightOverrides.StockTemplate(m)?.Intensity?.Value ?? 0f;
        LightModule.TemplateData? t = m.Template;
        if (t is null) return 0f;
        return prop switch
        {
            LightProp.Intensity => t.Intensity?.Value ?? 0f,
            LightProp.RangeM => t.Range?.Value ?? 0f,
            LightProp.ColorR => t.ColorRgb?.R ?? 0f,
            LightProp.ColorG => t.ColorRgb?.G ?? 0f,
            LightProp.ColorB => t.ColorRgb?.B ?? 0f,
            LightProp.InnerRad => t.InnerAngle?.Value ?? 0f,
            LightProp.OuterRad => t.OuterAngle?.Value ?? 0f,
            _ => 0f,
        };
    }

    /// <inheritdoc/>
    protected override double ReadDurationS(AnimSlot slot) => A(slot).Shared?.Duration ?? 0f;

    /// <inheritdoc/>
    /// <remarks>The committed <c>State.TimeCurrent</c> of the module's own tree; the goal when it cannot be read (gatOS's fallback).</remarks>
    protected override double ReadPositionS(AnimSlot slot)
    {
        KeyframeAnimationModule m = A(slot);
        return TryCommittedState(m, out KeyframeAnimationModule.State state) ? state.TimeCurrent : m.TimeGoal;
    }

    /// <inheritdoc/>
    protected override double ReadGoalS(AnimSlot slot) => A(slot).TimeGoal;

    /// <inheritdoc/>
    /// <remarks>
    /// The game's committed <c>State.DeploymentState</c>, not a state derived from the times: a
    /// solar-tracking array rotating back to zero before it folds reports <c>Retracting</c> while
    /// its goal still equals its position. When the state cannot be read, the state of an
    /// animation resting at its goal.
    /// </remarks>
    protected override int? ReadGameDeploymentState(AnimSlot slot)
    {
        KeyframeAnimationModule m = A(slot);
        return TryCommittedState(m, out KeyframeAnimationModule.State state)
            ? (int)state.DeploymentState
            : KsaPartRules.FallbackGameDeploymentState(m.TimeGoal);
    }

    // ------------------------------------------------------------------ raw writes (main thread, the pump)

    /// <inheritdoc/>
    protected override void WriteSwitchOn(LightSlot slot, bool on)
    {
        PowerConsumer sw = EffectiveSwitch(L(slot)) ?? throw new PartCommandException($"light {slot.Index} has no switch");
        sw.LightIsActive = on;
    }

    /// <inheritdoc/>
    /// <remarks>gatOS's <c>LightActuator.SetMaster</c>: <c>Vehicle.ToggleLights</c> as a set — <c>LightsOn</c> (so the lights key stays coherent) and every light switch of this vehicle.</remarks>
    protected override void WriteAllSwitches(bool on)
    {
        PartTree tree = LiveTree() ?? throw new PartCommandException("the vehicle is gone");
        Vehicle.LightsOn = on;
        Span<PowerConsumer> consumers = tree.Modules.Get<PowerConsumer>();
        for (int i = 0; i < consumers.Length; i++)
        {
            if (consumers[i].LightSwitch) consumers[i].LightIsActive = on;
        }
    }

    /// <inheritdoc/>
    protected override void WriteColor(LightSlot slot, float r, float g, float b)
    {
        ColorRgbReference color = LightOverrides.EnsurePrivate(L(slot)).ColorRgb;
        color.R = r;
        color.G = g;
        color.B = b;
        color.IndexedColor = IndexedColor.Invalid;   // otherwise OnDataLoad re-derives R/G/B from the named colour
        color.OnDataLoad(null!);                     // recomputes the Value the renderer reads; the mod argument is unused
    }

    /// <inheritdoc/>
    protected override void WriteCone(LightSlot slot, float innerRad, float outerRad)
    {
        LightModule.TemplateData mine = LightOverrides.EnsurePrivate(L(slot));
        mine.InnerAngle.Value = innerRad;   // already ≤ outer (PartMath.Cone): the renderer would swap them otherwise
        mine.OuterAngle.Value = outerRad;
    }

    /// <inheritdoc/>
    protected override void WriteIntensity(LightSlot slot, float intensity) =>
        LightOverrides.EnsurePrivate(L(slot)).Intensity.Value = intensity;

    /// <inheritdoc/>
    protected override void WriteRange(LightSlot slot, float rangeM) =>
        LightOverrides.EnsurePrivate(L(slot)).Range.Value = rangeM;

    /// <inheritdoc/>
    protected override void WriteReset(LightSlot slot) => LightOverrides.Reset(L(slot));

    /// <inheritdoc/>
    protected override void WriteGoalS(AnimSlot slot, double goalS) => A(slot).TimeGoal = (float)goalS;

    // ------------------------------------------------------------------ helpers

    /// <summary>The vehicle's part tree, or null once the vehicle is disposed.</summary>
    private PartTree? LiveTree()
    {
        Vehicle v = Vehicle;
        return v.IsDisposed ? null : v.Parts;
    }

    private static bool OnTree(ModuleBase module, PartTree tree) => module.Parent is { } part && ReferenceEquals(part.Tree, tree);

    private bool AnyDeparted(PartSets sets, PartTree tree)
    {
        foreach (LightSlot s in sets.Lights)
        {
            if (s is not KsaLightSlot l || !OnTree(l.Module, tree)) return true;
        }
        foreach (AnimSlot s in sets.Deployables)
        {
            if (s is not KsaAnimSlot a || !OnTree(a.Module, tree)) return true;
        }
        foreach (AnimSlot s in sets.Actuators)
        {
            if (s is not KsaAnimSlot a || !OnTree(a.Module, tree)) return true;
        }
        return false;
    }

    /// <summary>The light's effective switch now (plan §4.10), read live: never cached, <c>Part.ResolveLightSwitch</c> can reassign it.</summary>
    private PowerConsumer? EffectiveSwitch(LightModule m) => LiveTree() is { } tree ? EffectiveSwitch(m, tree) : null;

    private static PowerConsumer? EffectiveSwitch(LightModule m, PartTree tree)
    {
        PowerConsumer? sw = m.Parent?.FullPart.LightSwitch;
        return KsaPartRules.EffectiveSwitch(m.Template?.DisableInIva ?? false, sw, sw?.Parent?.Tree, tree);
    }

    private static bool HasTravel(KeyframeAnimationModule m) => m.Shared is { } shared && KsaPartRules.HasTravel(shared.Duration);

    /// <summary>Every animation a solar panel of this tree is gated by (<c>SolarPanel.KeyframeAnimationModule</c>, public, set in <c>OnFullPartCreated</c>).</summary>
    private static HashSet<KeyframeAnimationModule> SolarDriven(ModuleList modules)
    {
        var driven = new HashSet<KeyframeAnimationModule>(ReferenceEqualityComparer.Instance);
        foreach (SolarPanel panel in modules.Get<SolarPanel>())
        {
            if (panel.KeyframeAnimationModule is { } m) driven.Add(m);
        }
        return driven;
    }

    private static uint LinksOf(KeyframeAnimationModule m, HashSet<KeyframeAnimationModule> solarDriven)
    {
        Part? part = m.Parent;
        if (part is null) return 0u;
        ModuleList fullSubtree = part.FullPart.SubtreeModules;
        // A parachute drives the FIRST animation of its full part's subtree (Parachute.OnFullPartCreated
        // → the private _bayDoorAnim): the same rule, reproduced from public members.
        bool parachute = fullSubtree.HasAny<Parachute>() && KsaPartRules.IsFirst<KeyframeAnimationModule>(fullSubtree.Get<KeyframeAnimationModule>(), m);
        bool light = part.SubtreeModules.HasAny<LightModule>();
        return KsaPartRules.Links(solarDriven.Contains(m), parachute, light);
    }

    /// <summary>
    /// The committed state of <paramref name="m"/> in its <b>own</b> tree's state list (its
    /// <c>StatesIdx</c> indexes that list), bounds-checked; false when it cannot be read.
    /// Allocation-free (gatOS's <c>VesselReader.SampleAnimations</c>, with the bounds check).
    /// </summary>
    private static bool TryCommittedState(KeyframeAnimationModule m, out KeyframeAnimationModule.State state)
    {
        if (m.Parent?.Tree?.States is { } states && AnimStates.TryGetFrom(states, out AnimStates.StateList list))
        {
            ReadOnlySpan<KeyframeAnimationModule.State> committed = list.States;
            int index = m.StatesIdx;
            if ((uint)index < (uint)committed.Length)
            {
                state = committed[index];
                return true;
            }
        }
        state = default;
        return false;
    }

    private static LightModule L(LightSlot slot) => ((KsaLightSlot)slot).Module;

    private static KeyframeAnimationModule A(AnimSlot slot) => ((KsaAnimSlot)slot).Module;

    // ------------------------------------------------------------------ descriptors (captured at build time)

    internal static uint PartIdOf(ModuleBase m) => m.Parent?.FullPart.InstanceId ?? 0u;

    internal static string PartNameOf(ModuleBase m) => m.Parent?.FullPart.DisplayName ?? string.Empty;

    internal static string KeyOf(ModuleBase m) => KsaPartRules.Key(m.Parent?.FullPart.Template?.Id, m.TemplateId);
}
