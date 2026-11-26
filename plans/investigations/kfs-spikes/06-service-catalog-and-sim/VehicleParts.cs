// The parts abstraction the router talks to, and an in-memory simulation of it.
//
// Threading contract (what the real, game-backed implementation must honour):
//   * Count/Get* are called from the kFS dispatch (a solver worker thread in the game) inside the
//     SYSCALL instruction. They must be side-effect free and safe against the main thread: the real
//     implementation reads frozen arrays published by Rescan(), plus plain field reads.
//   * Rescan, Describe and every setter are called only from the main-thread pump.
// Index validity is the router's job; implementations may assume a valid index.
// No KSA types here either.

namespace PurrKfs;

public interface IVehicleParts
{
    uint Generation { get; }
    bool Dirty { get; }
    int LightCount { get; }
    int DeployableCount { get; }
    int ActuatorCount { get; }

    bool LightHasSwitch(int light);
    bool DeployableBroken(int index);

    uint LightGetI(int light, LightProp prop);
    float LightGetF(int light, LightProp prop);
    uint DeployGetI(int index, DeployProp prop);
    float DeployGetF(int index, DeployProp prop);
    uint ActuatorGetI(int index, ActuatorProp prop);
    float ActuatorGetF(int index, ActuatorProp prop);

    void Rescan();
    IEnumerable<string> Describe(PartKind kind);

    void LightSetOn(int light, bool on);
    void LightSetColor(int light, float r, float g, float b);
    void LightSetCone(int light, float innerRad, float outerRad);
    void LightSetIntensity(int light, float intensity);
    void LightSetRange(int light, float rangeM);
    void LightReset(int light);
    void LightsSetAll(bool on);
    void DeploySet(int index, bool deployed);
    void DeployToggle(int index);
    void ActuatorSet(int index, float value01);
}

public sealed class SimLight
{
    public uint PartId;
    public LightType Type = LightType.Spot;
    public bool HasSwitch = true, On, Powered = true, Overridden;
    public int SwitchGroup;
    public float Intensity, RangeM, R, G, B, Inner, Outer;
    public float StockIntensity = 2f, StockRangeM = 30f, StockR = 1f, StockG = 0.9f, StockB = 0.8f, StockInner = 0.3f, StockOuter = 0.6f;

    public SimLight ResetToStock()
    {
        Intensity = StockIntensity; RangeM = StockRangeM; R = StockR; G = StockG; B = StockB; Inner = StockInner; Outer = StockOuter;
        Overridden = false;
        return this;
    }
}

public enum AnimKind { Deployable, Actuator }

public sealed class SimAnim
{
    public AnimKind Kind;
    public uint PartId, Links = 1;
    public double DurationS = 2.0, GoalS, PositionS;
    public bool Broken;

    public bool Moving => !Broken && PositionS != GoalS;

    public DeployState State =>
        Broken ? DeployState.Broken
        : PositionS == GoalS ? (GoalS > 0 ? DeployState.Deployed : DeployState.Retracted)
        : PositionS < GoalS ? DeployState.Deploying : DeployState.Retracting;

    /// Slews toward the goal at one animation-second per simulated second.
    public void Step(double dt)
    {
        if (Broken || PositionS == GoalS) return;
        PositionS = PositionS < GoalS ? Math.Min(GoalS, PositionS + dt) : Math.Max(GoalS, PositionS - dt);
    }
}

public sealed class SimVehicleParts : IVehicleParts
{
    // Game rule mirrored from the stock light module: the outer cone is kept inside (1e-5, ~pi/2).
    public const float MinOuterRad = 1e-5f, MaxOuterRad = 1.5697963f;

    // Structure as the "vehicle" has it now, and the frozen lists kFS sees (published by Rescan).
    readonly List<SimLight> _liveLights = new();
    readonly List<SimAnim> _liveAnims = new();
    SimLight[] _lights = Array.Empty<SimLight>();
    SimAnim[] _deployables = Array.Empty<SimAnim>();
    SimAnim[] _actuators = Array.Empty<SimAnim>();

    public uint Generation { get; private set; }
    public bool Dirty { get; private set; }
    public double TimeS { get; private set; }
    public int RescanCount { get; private set; }
    /// (time, index, state) each time a deployable's derived state changes, for assertions.
    public List<(double TimeS, int Index, DeployState State)> DeployHistory { get; } = new();
    /// (time, index) when a setter was applied, for assertions.
    public List<(double TimeS, string What)> Applied { get; } = new();

    public IReadOnlyList<SimLight> Lights => _lights;
    public IReadOnlyList<SimAnim> Deployables => _deployables;
    public IReadOnlyList<SimAnim> Actuators => _actuators;

    public SimVehicleParts(int lights, int deployables, int actuators)
    {
        for (int i = 0; i < lights; i++) _liveLights.Add(new SimLight { PartId = 100u + (uint)i, SwitchGroup = i }.ResetToStock());
        for (int i = 0; i < deployables; i++) _liveAnims.Add(new SimAnim { Kind = AnimKind.Deployable, PartId = 200u + (uint)i });
        for (int i = 0; i < actuators; i++) _liveAnims.Add(new SimAnim { Kind = AnimKind.Actuator, PartId = 300u + (uint)i, DurationS = 1.0 });
        Publish();
    }

    /// Simulates a structural change (docking, staging): kFS keeps seeing the old lists until Rescan.
    public SimLight AddLightLater()
    {
        var l = new SimLight { PartId = 100u + (uint)_liveLights.Count, SwitchGroup = _liveLights.Count }.ResetToStock();
        _liveLights.Add(l);
        Dirty = true;
        return l;
    }

    void Publish()
    {
        _lights = _liveLights.ToArray();
        _deployables = _liveAnims.Where(a => a.Kind == AnimKind.Deployable).ToArray();
        _actuators = _liveAnims.Where(a => a.Kind == AnimKind.Actuator).ToArray();
        Generation++;
        Dirty = false;
    }

    public void Step(double dt)
    {
        TimeS += dt;
        for (int i = 0; i < _deployables.Length; i++)
        {
            var before = _deployables[i].State;
            _deployables[i].Step(dt);
            if (_deployables[i].State != before) DeployHistory.Add((TimeS, i, _deployables[i].State));
        }
        foreach (var a in _actuators) a.Step(dt);
    }

    // ---- IVehicleParts: reads ----
    public int LightCount => _lights.Length;
    public int DeployableCount => _deployables.Length;
    public int ActuatorCount => _actuators.Length;
    public bool LightHasSwitch(int light) => _lights[light].HasSwitch;
    public bool DeployableBroken(int index) => _deployables[index].Broken;

    public uint LightGetI(int i, LightProp p)
    {
        var l = _lights[i];
        return p switch
        {
            LightProp.Present => 1u,
            LightProp.HasSwitch => B(l.HasSwitch),
            LightProp.On => B(l.On),
            LightProp.Lit => B(l.On && l.Powered),
            LightProp.Type => (uint)l.Type,
            LightProp.PartId => l.PartId,
            LightProp.SwitchGroup => (uint)l.SwitchGroup,
            LightProp.Overridden => B(l.Overridden),
            _ => 0u,
        };
    }

    public float LightGetF(int i, LightProp p)
    {
        var l = _lights[i];
        return p switch
        {
            LightProp.Intensity => l.Intensity,
            LightProp.RangeM => l.RangeM,
            LightProp.ColorR => l.R,
            LightProp.ColorG => l.G,
            LightProp.ColorB => l.B,
            LightProp.InnerRad => l.Inner,
            LightProp.OuterRad => l.Outer,
            LightProp.StockIntensity => l.StockIntensity,
            _ => 0f,
        };
    }

    public uint DeployGetI(int i, DeployProp p)
    {
        var a = _deployables[i];
        return p switch
        {
            DeployProp.Present => 1u,
            DeployProp.State => (uint)a.State,
            DeployProp.Commanded => B(a.GoalS > 0),
            DeployProp.Moving => B(a.Moving),
            DeployProp.PartId => a.PartId,
            DeployProp.Links => a.Links,
            _ => 0u,
        };
    }

    public uint ActuatorGetI(int i, ActuatorProp p)
    {
        var a = _actuators[i];
        return p switch
        {
            ActuatorProp.Present => 1u,
            ActuatorProp.Moving => B(a.Moving),
            ActuatorProp.PartId => a.PartId,
            ActuatorProp.Links => a.Links,
            _ => 0u,
        };
    }

    public float DeployGetF(int i, DeployProp p) => p switch
    {
        DeployProp.Position => AnimPosition(_deployables[i]),
        DeployProp.Target => AnimTarget(_deployables[i]),
        DeployProp.DurationS => (float)_deployables[i].DurationS,
        _ => 0f,
    };

    public float ActuatorGetF(int i, ActuatorProp p) => p switch
    {
        ActuatorProp.Position => AnimPosition(_actuators[i]),
        ActuatorProp.Target => AnimTarget(_actuators[i]),
        ActuatorProp.DurationS => (float)_actuators[i].DurationS,
        _ => 0f,
    };

    // position/target are reported as a fraction of the travel, duration in seconds.
    static float AnimPosition(SimAnim a) => (float)(a.PositionS / a.DurationS);
    static float AnimTarget(SimAnim a) => (float)(a.GoalS / a.DurationS);

    static uint B(bool b) => b ? 1u : 0u;

    public void Rescan() { RescanCount++; Publish(); }

    public IEnumerable<string> Describe(PartKind kind)
    {
        yield return $"parts gen {Generation}{(Dirty ? " (dirty)" : "")}: {LightCount} lights, {DeployableCount} deployables, {ActuatorCount} actuators";
        if (kind is PartKind.All or PartKind.Light)
            for (int i = 0; i < _lights.Length; i++)
            {
                var l = _lights[i];
                yield return $"light {i}: part {l.PartId} {l.Type.ToString().ToLowerInvariant()} switch {(l.HasSwitch ? l.SwitchGroup.ToString() : "none")} {(l.On ? "on" : "off")} intensity {l.Intensity:0.###} range {l.RangeM:0.#} m";
            }
        if (kind is PartKind.All or PartKind.Deployable)
            for (int i = 0; i < _deployables.Length; i++)
                yield return $"deployable {i}: part {_deployables[i].PartId} {_deployables[i].State.ToString().ToLowerInvariant()} {_deployables[i].DurationS:0.##} s";
        if (kind is PartKind.All or PartKind.Actuator)
            for (int i = 0; i < _actuators.Length; i++)
                yield return $"actuator {i}: part {_actuators[i].PartId} at {AnimPosition(_actuators[i]):0.###} of {_actuators[i].DurationS:0.##} s";
    }

    // ---- IVehicleParts: commands (main thread) ----
    public void LightSetOn(int i, bool on)
    {
        // Lights on the same switch move together, like parts sharing one stock light switch.
        int group = _lights[i].SwitchGroup;
        foreach (var l in _lights) if (l.HasSwitch && l.SwitchGroup == group) l.On = on;
        Note($"light {i} on={on}");
    }

    public void LightSetColor(int i, float r, float g, float b)
    {
        var l = _lights[i];
        l.R = Math.Clamp(r, 0f, 1f); l.G = Math.Clamp(g, 0f, 1f); l.B = Math.Clamp(b, 0f, 1f);
        l.Overridden = true;
        Note($"light {i} color");
    }

    public void LightSetCone(int i, float inner, float outer)
    {
        var l = _lights[i];
        l.Outer = Math.Clamp(outer, MinOuterRad, MaxOuterRad);
        l.Inner = Math.Clamp(inner, 0f, l.Outer);
        l.Overridden = true;
        Note($"light {i} cone");
    }

    public void LightSetIntensity(int i, float v) { _lights[i].Intensity = Math.Max(0f, v); _lights[i].Overridden = true; Note($"light {i} intensity"); }
    public void LightSetRange(int i, float v) { _lights[i].RangeM = Math.Max(0f, v); _lights[i].Overridden = true; Note($"light {i} range"); }
    public void LightReset(int i) { _lights[i].ResetToStock(); Note($"light {i} reset"); }
    public void LightsSetAll(bool on) { foreach (var l in _lights) if (l.HasSwitch) l.On = on; Note($"all lights on={on}"); }

    public void DeploySet(int i, bool deployed) { var a = _deployables[i]; a.GoalS = deployed ? a.DurationS : 0; Note($"deploy {i} set {deployed}"); }
    public void DeployToggle(int i) { var a = _deployables[i]; a.GoalS = a.GoalS > 0 ? 0 : a.DurationS; Note($"deploy {i} toggle"); }
    public void ActuatorSet(int i, float v) { var a = _actuators[i]; a.GoalS = Math.Clamp(v, 0f, 1f) * a.DurationS; Note($"actuator {i} set"); }

    void Note(string what) => Applied.Add((TimeS, what));
}
