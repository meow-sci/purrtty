using purrTTY.Kfs.Catalog;
using purrTTY.Kfs.Runtime;

namespace purrTTY.Kfs.Testing;

/// <summary>A simulated light switch; lights holding the same instance switch together.</summary>
public sealed class SimSwitch
{
    /// <summary>The commanded state.</summary>
    public bool On { get; set; }
}

/// <summary>A simulated light emitter, as the vehicle has it now (not as a program's frozen list sees it).</summary>
public sealed class SimLight
{
    internal SimLight(long instanceId) => InstanceId = instanceId;

    /// <summary>Ranking key (the game: the module's instance id); later additions rank after earlier ones.</summary>
    public long InstanceId { get; }

    /// <summary>The part's display name.</summary>
    public string Name { get; set; } = "";

    /// <summary>The part kind's key.</summary>
    public string Key { get; set; } = "";

    /// <summary>The part's instance id.</summary>
    public uint PartId { get; set; }

    /// <summary>Spot or point.</summary>
    public LightType Type { get; set; }

    /// <summary>An interior fill light: ignores switch and power.</summary>
    public bool Interior { get; set; }

    /// <summary>The switch, or null for none. Share an instance to switch lights together.</summary>
    public SimSwitch? Switch { get; set; }

    /// <summary>Whether the light has electrical power.</summary>
    public bool Powered { get; set; } = true;

    /// <summary>False once the part left the vehicle (<see cref="SimVehicleParts.Detach(SimLight)"/>).</summary>
    public bool Present { get; internal set; } = true;

    /// <summary>True when the light has a usable switch.</summary>
    public bool HasSwitch => !Interior && Switch is not null;

    /// <summary>The switch state; true for a light without a switch (as programs read it).</summary>
    public bool On => Switch?.On ?? true;

    /// <summary>
    /// What <c>X_LIGHT_LIT</c> reads, modelled on the game's <c>LightModule.IsActive</c>
    /// (<c>Template.DisableInIva || !Part.IsLightSwitchedOff()</c>): always true for an interior
    /// fill light and for a light without a switch; otherwise the <b>live</b> switch and the
    /// switch's <b>committed</b> state (<see cref="SwitchCommitted"/>) must both be on. So switching
    /// off reads dark at once, switching on reads lit only after the next <see cref="SimVehicleParts.Step"/>.
    /// </summary>
    public bool Lit => Interior || Switch is null || (Switch.On && SwitchCommitted);

    /// <summary>
    /// The switch's state as the last <see cref="SimVehicleParts.Step"/> (or creation) committed it:
    /// switched on and powered, as the game's <c>PowerConsumer.UpdateModules</c> computes
    /// <c>PowerConsumerState.Active</c> in the physics job and <c>ApplyVehicleSolvers</c> commits it.
    /// </summary>
    public bool SwitchCommitted { get; private set; }

    /// <summary>Current intensity.</summary>
    public float Intensity { get; set; }

    /// <summary>Current range in metres.</summary>
    public float RangeM { get; set; }

    /// <summary>Current colour, red.</summary>
    public float R { get; set; }

    /// <summary>Current colour, green.</summary>
    public float G { get; set; }

    /// <summary>Current colour, blue.</summary>
    public float B { get; set; }

    /// <summary>Current inner cone half-angle.</summary>
    public float InnerRad { get; set; }

    /// <summary>Current outer cone half-angle.</summary>
    public float OuterRad { get; set; }

    /// <summary>True once a program changed the appearance, until reset.</summary>
    public bool Overridden { get; set; }

    /// <summary>The authored appearance <see cref="ResetAppearance"/> restores.</summary>
    public SimLightSpec Stock { get; internal set; } = new();

    /// <summary>Restores the authored appearance (not the switch).</summary>
    public void ResetAppearance()
    {
        Intensity = Stock.Intensity;
        RangeM = Stock.RangeM;
        R = Stock.R;
        G = Stock.G;
        B = Stock.B;
        InnerRad = Stock.InnerRad;
        OuterRad = Stock.OuterRad;
        Overridden = false;
    }

    internal void CommitSwitchState() => SwitchCommitted = Switch is { On: true } && Powered;
}

/// <summary>A simulated animation: a goal and a position in seconds, slewing at one second per second.</summary>
public sealed class SimAnimation
{
    internal SimAnimation(long instanceId, PartKind kind)
    {
        InstanceId = instanceId;
        Kind = kind;
    }

    /// <summary>Ranking key; later additions rank after earlier ones.</summary>
    public long InstanceId { get; }

    /// <summary><see cref="PartKind.Deployable"/> or <see cref="PartKind.Actuator"/>.</summary>
    public PartKind Kind { get; }

    /// <summary>The part's display name.</summary>
    public string Name { get; set; } = "";

    /// <summary>The part kind's key.</summary>
    public string Key { get; set; } = "";

    /// <summary>The part's instance id.</summary>
    public uint PartId { get; set; }

    /// <summary>The authored length in seconds.</summary>
    public double DurationS { get; set; } = 2.0;

    /// <summary>The goal in seconds.</summary>
    public double GoalS { get; set; }

    /// <summary>The position in seconds.</summary>
    public double PositionS { get; set; }

    /// <summary>The link flags.</summary>
    public uint Links { get; set; }

    /// <summary>False once the part left the vehicle.</summary>
    public bool Present { get; internal set; } = true;

    /// <summary>The deployment state derived from the position and the goal now.</summary>
    public DeployState State => PartMath.State(PositionS, GoalS);

    /// <summary>
    /// The deployment state as of the last <see cref="SimVehicleParts.Step"/> (or creation): what a
    /// program reads as <c>X_DEPLOY_STATE</c>, as the game reports the <c>DeploymentState</c> its last
    /// physics step committed, so a goal set since then shows only after the next step.
    /// </summary>
    public DeployState CommittedState { get; internal set; }

    /// <summary>True while the position differs from the goal.</summary>
    public bool Moving => PartMath.Moving(PositionS, GoalS);

    /// <summary>The state seen at the last step (for <see cref="SimVehicleParts.DeployHistory"/>).</summary>
    internal DeployState LastSeenState { get; set; }
}

/// <summary>A light slot of the simulated back end.</summary>
public sealed class SimLightSlot(int index, SimLight light, int switchGroup)
    : LightSlot(index, light.PartId, light.Name, light.Key, switchGroup)
{
    /// <summary>The simulated light.</summary>
    public SimLight Light { get; } = light;
}

/// <summary>An animation slot of the simulated back end.</summary>
public sealed class SimAnimSlot(int index, SimAnimation animation)
    : AnimSlot(animation.Kind, index, animation.PartId, animation.Name, animation.Key, animation.Links)
{
    /// <summary>The simulated animation.</summary>
    public SimAnimation Animation { get; } = animation;
}

/// <summary>
/// The in-memory <see cref="IVehicleParts"/> of the test runtime (plan §5.4): plain fields behind
/// the same <see cref="VehiclePartsBase"/> and <see cref="PartMath"/> the game back end uses, plus
/// helpers to script the awkward cases (a part leaves, parts arrive, power fails, lights share a
/// switch). Single-threaded: the test drives the pump, <see cref="Step"/> and the dispatch in turn.
/// </summary>
public sealed class SimVehicleParts : VehiclePartsBase
{
    private readonly List<SimLight> _lights = [];
    private readonly List<SimAnimation> _animations = [];
    private long _nextInstance = 1;

    /// <summary>A simulated vehicle's parts from <paramref name="spec"/>.</summary>
    public SimVehicleParts(SimVehicleSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        foreach (SimLightSpec light in spec.Lights) AddLight(light);
        foreach (SimAnimationSpec anim in spec.Deployables) AddDeployable(anim);
        foreach (SimAnimationSpec anim in spec.Actuators) AddActuator(anim);
    }

    /// <summary>A simulated vehicle's parts with default lights, deployables and actuators.</summary>
    public SimVehicleParts(int lights = 0, int deployables = 0, int actuators = 0)
        : this(SimVehicleSpec.With(lights, deployables, actuators)) { }

    /// <summary>Simulated seconds stepped so far.</summary>
    public double TimeS { get; private set; }

    /// <summary>Every command applied, with the simulated time of the pump that applied it.</summary>
    public List<(double TimeS, PartCommand Command)> Applied { get; } = [];

    /// <summary>
    /// Each change of a deployable's derived state, observed at the end of a <see cref="Step"/>
    /// (so a goal the pump set counts at the step that follows): time, index in the frozen list,
    /// new state.
    /// </summary>
    public List<(double TimeS, int Index, DeployState State)> DeployHistory { get; } = [];

    /// <summary>Every light ever on the vehicle (detached ones included), in ranking order.</summary>
    public IReadOnlyList<SimLight> AllLights => _lights;

    /// <summary>Every animation ever on the vehicle, in ranking order.</summary>
    public IReadOnlyList<SimAnimation> AllAnimations => _animations;

    /// <summary>The lights by the index a program uses now (the frozen list).</summary>
    public IReadOnlyList<SimLight> Lights => Sets.Lights.Select(s => ((SimLightSlot)s).Light).ToArray();

    /// <summary>The deployables by index (the frozen list).</summary>
    public IReadOnlyList<SimAnimation> Deployables => Sets.Deployables.Select(s => ((SimAnimSlot)s).Animation).ToArray();

    /// <summary>The actuators by index (the frozen list).</summary>
    public IReadOnlyList<SimAnimation> Actuators => Sets.Actuators.Select(s => ((SimAnimSlot)s).Animation).ToArray();

    /// <summary>Light <paramref name="index"/> of the frozen list.</summary>
    public SimLight Light(int index) => ((SimLightSlot)Sets.Lights[index]).Light;

    /// <summary>Deployable <paramref name="index"/> of the frozen list.</summary>
    public SimAnimation Deployable(int index) => ((SimAnimSlot)Sets.Deployables[index]).Animation;

    /// <summary>Actuator <paramref name="index"/> of the frozen list.</summary>
    public SimAnimation Actuator(int index) => ((SimAnimSlot)Sets.Actuators[index]).Animation;

    // ------------------------------------------------------------------ scripting

    /// <summary>Adds a light to the vehicle; programs see it only after a rescan or in a new run (dirty until then).</summary>
    public SimLight AddLight(SimLightSpec? spec = null)
    {
        spec ??= new SimLightSpec { Name = $"Light {_lights.Count}" };
        var light = new SimLight(_nextInstance++)
        {
            Name = spec.Name,
            Key = spec.Key,
            PartId = spec.PartId ?? 100u + (uint)_lights.Count,
            Type = spec.Type,
            Interior = spec.Interior,
            Powered = spec.Powered,
            Stock = spec,
        };
        if (spec.SharesSwitchWith is int other) light.Switch = _lights[other].Switch;
        else if (spec.HasSwitch && !spec.Interior) light.Switch = new SimSwitch { On = spec.On };
        light.ResetAppearance();
        light.CommitSwitchState();
        _lights.Add(light);
        return light;
    }

    /// <summary>Adds a deployable (dirty until a rescan).</summary>
    public SimAnimation AddDeployable(SimAnimationSpec? spec = null) => AddAnimation(PartKind.Deployable, spec, 200u);

    /// <summary>Adds an actuator (dirty until a rescan).</summary>
    public SimAnimation AddActuator(SimAnimationSpec? spec = null) => AddAnimation(PartKind.Actuator, spec, 300u);

    /// <summary>The light's part leaves the vehicle (staging, undocking): its slot stays, reads 0, writes are rejected.</summary>
    public void Detach(SimLight light) => light.Present = false;

    /// <summary>The animation's part leaves the vehicle.</summary>
    public void Detach(SimAnimation animation) => animation.Present = false;

    /// <summary>Gives <paramref name="light"/> the switch of <paramref name="from"/> (emitters of one part).</summary>
    public static void ShareSwitch(SimLight from, SimLight light) => light.Switch = from.Switch;

    /// <summary>Cuts or restores electrical power to every light.</summary>
    public void SetPower(bool powered)
    {
        foreach (SimLight light in _lights) light.Powered = powered;
    }

    /// <summary>
    /// Advances the simulated world by <paramref name="dtSeconds"/> (one physics job and its commit):
    /// animations slew toward their goals at one animation-second per second and commit their
    /// <see cref="SimAnimation.CommittedState"/> (recording state changes of deployables), and each
    /// light commits its switch's state (<see cref="SimLight.SwitchCommitted"/>: switch and power).
    /// </summary>
    public void Step(double dtSeconds)
    {
        TimeS += dtSeconds;
        foreach (SimLight light in _lights) light.CommitSwitchState();
        var deployables = Sets.Deployables;
        foreach (SimAnimation anim in _animations)
        {
            anim.PositionS = PartMath.Slew(anim.PositionS, anim.GoalS, dtSeconds);
            DeployState state = anim.CommittedState = anim.State;
            if (state == anim.LastSeenState) continue;
            anim.LastSeenState = state;
            if (anim.Kind != PartKind.Deployable) continue;
            int index = -1;
            for (int i = 0; i < deployables.Length; i++)
            {
                if (ReferenceEquals(((SimAnimSlot)deployables[i]).Animation, anim)) { index = i; break; }
            }
            DeployHistory.Add((TimeS, index, state));
        }
    }

    // ------------------------------------------------------------------ IVehicleParts

    /// <inheritdoc/>
    public override bool Dirty
    {
        get
        {
            PartSets sets = Sets;
            int lights = 0, deployables = 0, actuators = 0;
            foreach (SimLight light in _lights) if (light.Present) lights++;
            foreach (SimAnimation anim in _animations)
            {
                if (!anim.Present || anim.DurationS <= 0) continue;
                if (anim.Kind == PartKind.Deployable) deployables++;
                else actuators++;
            }
            return lights != sets.Lights.Length || deployables != sets.Deployables.Length || actuators != sets.Actuators.Length;
        }
    }

    /// <inheritdoc/>
    public override bool IsPresent(PartSlot slot) => slot switch
    {
        SimLightSlot light => light.Light.Present,
        SimAnimSlot anim => anim.Animation.Present,
        _ => false,
    };

    /// <inheritdoc/>
    public override bool HasSwitch(LightSlot slot) => slot is SimLightSlot light && light.Light.HasSwitch;

    /// <inheritdoc/>
    public override void Apply(in PartCommand command)
    {
        base.Apply(command);
        Applied.Add((TimeS, command));
    }

    /// <summary>How many times the lists were built (<see cref="IVehicleParts.Rebuild"/> and the first build).</summary>
    public int Builds { get; private set; }

    /// <inheritdoc/>
    protected override Collected Collect()
    {
        Builds++;
        SimLight[] lights = _lights.Where(l => l.Present).OrderBy(l => l.InstanceId).ToArray();
        int[] groups = PartMath.SwitchGroups(lights.Select(l => l.HasSwitch ? l.Switch : null).ToArray());
        SimAnimation[] anims = _animations.Where(a => a.Present && a.DurationS > 0).OrderBy(a => a.InstanceId).ToArray();
        return new Collected(
            lights.Select((l, i) => (LightSlot)new SimLightSlot(i, l, groups[i])).ToArray(),
            anims.Where(a => a.Kind == PartKind.Deployable).Select((a, i) => (AnimSlot)new SimAnimSlot(i, a)).ToArray(),
            anims.Where(a => a.Kind == PartKind.Actuator).Select((a, i) => (AnimSlot)new SimAnimSlot(i, a)).ToArray());
    }

    /// <inheritdoc/>
    protected override bool ReadSwitchOn(LightSlot slot) => L(slot).On;

    /// <inheritdoc/>
    protected override bool ReadLit(LightSlot slot) => L(slot).Lit;

    /// <inheritdoc/>
    protected override LightType ReadLightType(LightSlot slot) => L(slot).Type;

    /// <inheritdoc/>
    protected override bool ReadOverridden(LightSlot slot) => L(slot).Overridden;

    /// <inheritdoc/>
    protected override float ReadLightF(LightSlot slot, LightProp prop)
    {
        SimLight l = L(slot);
        return prop switch
        {
            LightProp.Intensity => l.Intensity,
            LightProp.RangeM => l.RangeM,
            LightProp.ColorR => l.R,
            LightProp.ColorG => l.G,
            LightProp.ColorB => l.B,
            LightProp.InnerRad => l.InnerRad,
            LightProp.OuterRad => l.OuterRad,
            LightProp.StockIntensity => l.Stock.Intensity,
            _ => 0f,
        };
    }

    /// <inheritdoc/>
    protected override double ReadDurationS(AnimSlot slot) => A(slot).DurationS;

    /// <inheritdoc/>
    protected override double ReadPositionS(AnimSlot slot) => A(slot).PositionS;

    /// <inheritdoc/>
    protected override double ReadGoalS(AnimSlot slot) => A(slot).GoalS;

    /// <inheritdoc/>
    /// <remarks><see cref="SimAnimation.CommittedState"/> in the game's numbering (Deployed 0 …), as the game back end reads its committed state.</remarks>
    protected override int? ReadGameDeploymentState(AnimSlot slot) =>
        A(slot).CommittedState is DeployState s and not DeployState.None ? (int)s - 1 : null;

    /// <inheritdoc/>
    protected override void WriteSwitchOn(LightSlot slot, bool on) => L(slot).Switch!.On = on;

    /// <inheritdoc/>
    protected override void WriteAllSwitches(bool on)
    {
        foreach (SimLight light in _lights)
        {
            if (light.Present && light.HasSwitch) light.Switch!.On = on;
        }
    }

    /// <inheritdoc/>
    protected override void WriteColor(LightSlot slot, float r, float g, float b)
    {
        SimLight l = L(slot);
        (l.R, l.G, l.B, l.Overridden) = (r, g, b, true);
    }

    /// <inheritdoc/>
    protected override void WriteCone(LightSlot slot, float innerRad, float outerRad)
    {
        SimLight l = L(slot);
        (l.InnerRad, l.OuterRad, l.Overridden) = (innerRad, outerRad, true);
    }

    /// <inheritdoc/>
    protected override void WriteIntensity(LightSlot slot, float intensity)
    {
        SimLight l = L(slot);
        (l.Intensity, l.Overridden) = (intensity, true);
    }

    /// <inheritdoc/>
    protected override void WriteRange(LightSlot slot, float rangeM)
    {
        SimLight l = L(slot);
        (l.RangeM, l.Overridden) = (rangeM, true);
    }

    /// <inheritdoc/>
    protected override void WriteReset(LightSlot slot) => L(slot).ResetAppearance();

    /// <inheritdoc/>
    protected override void WriteGoalS(AnimSlot slot, double goalS) => A(slot).GoalS = goalS;

    private static SimLight L(LightSlot slot) => ((SimLightSlot)slot).Light;

    private static SimAnimation A(AnimSlot slot) => ((SimAnimSlot)slot).Animation;

    private SimAnimation AddAnimation(PartKind kind, SimAnimationSpec? spec, uint partBase)
    {
        int ofKind = _animations.Count(a => a.Kind == kind);
        spec ??= new SimAnimationSpec { Name = $"{PartsText.Noun(kind)} {ofKind}" };
        double start = PartMath.FractionToSeconds((float)spec.InitialFraction, spec.DurationS);
        var anim = new SimAnimation(_nextInstance++, kind)
        {
            Name = spec.Name,
            Key = spec.Key,
            PartId = spec.PartId ?? partBase + (uint)ofKind,
            DurationS = spec.DurationS,
            GoalS = start,
            PositionS = start,
            Links = spec.Links,
        };
        anim.LastSeenState = anim.CommittedState = anim.State;
        _animations.Add(anim);
        return anim;
    }
}
