using KSA.Kfs.Host;
using KSA.Kfs.Vm.Tables;

namespace purrTTY.Kfs.Testing;

/// <summary>Converts a <see cref="SimTelemetry"/> into the game's <c>HostSample</c> for one vehicle's layout.</summary>
internal sealed class TelemetrySampler
{
    private readonly GameTables _tables;
    private readonly IoLayout _layout;
    private readonly int _thrusters, _engines, _gimbals;
    private readonly Dictionary<string, MessageLayout?> _byName = new(StringComparer.Ordinal);

    internal TelemetrySampler(GameTables tables, IoLayout layout, int thrusters, int engines, int gimbals)
    {
        _tables = tables;
        _layout = layout;
        (_thrusters, _engines, _gimbals) = (thrusters, engines, gimbals);
    }

    /// <summary>The sample, with an (empty) entry for every MS-G3 message the computer publishes, as the game carries them all.</summary>
    internal HostSample ToHostSample(SimTelemetry t, IReadOnlyList<ushort> hostPublished)
    {
        ArgumentNullException.ThrowIfNull(t);
        var values = new Dictionary<ushort, IReadOnlyDictionary<string, double>>();
        foreach (ushort id in hostPublished)
        {
            if (HostMessages.IsMsG3(id)) values[id] = new Dictionary<string, double>(StringComparer.Ordinal);
        }

        if (t.BurnStatus is { } burn)
        {
            values[HostMessages.BURN_STATUS] = new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["burn"] = burn.Burn,
                ["state"] = (uint)burn.State,
                ["t_ignition_us"] = burn.IgnitionUs,
                ["dv_remaining_mps"] = burn.DvRemainingMps,
                ["dv_vlf_mps.x"] = burn.DvVlfMps.X,
                ["dv_vlf_mps.y"] = burn.DvVlfMps.Y,
                ["dv_vlf_mps.z"] = burn.DvVlfMps.Z,
                ["end_cause"] = burn.EndCause,
            };
        }

        foreach ((string message, Dictionary<string, double> fields) in t.Leaves)
        {
            MessageLayout layout = Message(message);
            var merged = values.TryGetValue(layout.Id, out var existing)
                ? new Dictionary<string, double>(existing, StringComparer.Ordinal)
                : new Dictionary<string, double>(StringComparer.Ordinal);
            foreach ((string path, double value) in fields)
            {
                if (!layout.Leaves.Any(l => l.Path == path))
                    throw new ArgumentException($"telemetry: {message} has no leaf '{path}' on this vehicle's layout");
                merged[path] = value;
            }
            values[layout.Id] = merged;
        }

        return new HostSample(Nav(t.Nav), Flight(t.Flight), Props(t.Props), values);
    }

    private MessageLayout Message(string name)
    {
        if (!_byName.TryGetValue(name, out MessageLayout? layout))
            _byName[name] = layout = MessageLayout.Find(_tables, _layout, name);
        if (layout is null) throw new ArgumentException($"telemetry: no message named '{name}'");
        if (!HostMessages.MsG3Order.Contains(layout.Id))
            throw new ArgumentException($"telemetry: {name} is not published by the host; only {string.Join(", ", HostMessages.MsG3Order.Select(id => MessageLayout.Find(_tables, _layout, id)?.Name))} can be scripted");
        return layout;
    }

    private static Vec3d V(SimVec3 v) => new(v.X, v.Y, v.Z);

    private static Quatd Q(SimQuat q) => new(q.X, q.Y, q.Z, q.W);

    private static NavTruth Nav(SimNav n) =>
        new(V(n.PositionCciM), V(n.VelocityCciMps), Q(n.BodyToCci), V(n.RatesBodyRps), V(n.AccelBodyMps2), n.ParentId, n.Valid);

    private static FlightDataValues Flight(SimFlight f) =>
        new(f.AltitudeM, f.RadarAltitudeM, f.ApoapsisM, f.PeriapsisM, f.TimeToApoapsisS, f.TimeToPeriapsisS, f.SpeedMps,
            f.SurfaceSpeedMps, f.VerticalSpeedMps, Q(f.HelmToBody), f.TargetValid, V(f.TargetPositionHelmM),
            V(f.TargetVelocityHelmMps), f.TargetRangeM, f.TargetSpeedRelMps, f.InAtmosphere, f.AmbientPressurePa);

    private VehiclePropsValues Props(SimProps p)
    {
        if (p.InertiaRowMajor is not { Length: 9 }) throw new ArgumentException("telemetry: Props.InertiaRowMajor needs 9 values");
        Check("thruster", p.Thrusters.Count, _thrusters);
        Check("engine", p.Engines.Count, _engines);
        Check("gimbal", p.Gimbals.Count, _gimbals);
        var thrusters = new ThrusterProps[_thrusters];
        for (int i = 0; i < thrusters.Length; i++)
        {
            SimThruster t = i < p.Thrusters.Count ? p.Thrusters[i] : new SimThruster();
            thrusters[i] = new ThrusterProps(V(t.ForceBodyN), V(t.TorqueBodyNm), t.MinPulseS, t.PropellantOk);
        }
        var engines = new EngineProps[_engines];
        for (int i = 0; i < engines.Length; i++)
        {
            SimEngine e = i < p.Engines.Count ? p.Engines[i] : new SimEngine();
            engines[i] = new EngineProps(e.MaxThrustN, e.MinThrottle, e.IspS, e.Active, e.PropellantOk);
        }
        var gimbals = new GimbalProps[_gimbals];
        for (int i = 0; i < gimbals.Length; i++)
        {
            SimGimbal g = i < p.Gimbals.Count ? p.Gimbals[i] : new SimGimbal();
            gimbals[i] = new GimbalProps(g.RangeYRad, g.RangeZRad);
        }
        return new VehiclePropsValues(p.MassKg, (double[])p.InertiaRowMajor.Clone(), V(p.ComBodyM), thrusters, engines, gimbals);
    }

    private static void Check(string group, int given, int layout)
    {
        if (given > layout)
            throw new ArgumentException($"telemetry: {given} {group}(s) given; this vehicle's layout has {layout}");
    }
}
