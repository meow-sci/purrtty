namespace purrTTY.Kfs.Testing;

/// <summary>A vector of three doubles (the harness's game-type-free stand-in for the game's <c>Vec3d</c>).</summary>
public readonly record struct SimVec3(double X, double Y, double Z)
{
    /// <summary>(0, 0, 0).</summary>
    public static readonly SimVec3 Zero = new(0, 0, 0);
}

/// <summary>A quaternion (x, y, z, w), the stand-in for the game's <c>Quatd</c>.</summary>
public readonly record struct SimQuat(double X, double Y, double Z, double W)
{
    /// <summary>(0, 0, 0, 1).</summary>
    public static readonly SimQuat Identity = new(0, 0, 0, 1);
}

/// <summary>
/// What <c>MSG_NAV_STATE</c> (0x0101) carries: the game's <c>NavTruth</c>, every field typed.
/// <c>soi_changes</c> is always 0 in the game (the host packs 0); set it through
/// <see cref="SimTelemetry.Leaves"/> if a test needs another value.
/// </summary>
public sealed class SimNav
{
    /// <summary><c>valid</c>. Default true.</summary>
    public bool Valid { get; set; } = true;

    /// <summary><c>parent_id</c> (the game: CRC-32 of the parent body's id).</summary>
    public uint ParentId { get; set; }

    /// <summary><c>r_cci_m</c>, metres (float64 in the message).</summary>
    public SimVec3 PositionCciM { get; set; }

    /// <summary><c>v_cci_mps</c>, metres per second (float64 in the message).</summary>
    public SimVec3 VelocityCciMps { get; set; }

    /// <summary><c>q_body_to_cci</c>. Default identity.</summary>
    public SimQuat BodyToCci { get; set; } = SimQuat.Identity;

    /// <summary><c>rates_body_rps</c>, radians per second.</summary>
    public SimVec3 RatesBodyRps { get; set; }

    /// <summary><c>accel_body_mps2</c>.</summary>
    public SimVec3 AccelBodyMps2 { get; set; }
}

/// <summary>
/// The typed part of <c>MSG_FLIGHT_DATA</c> (0x0102): the game's <c>FlightDataValues</c>, the
/// message's first 17 fields (doubles are narrowed to float32 in the message, bools to 0/1).
/// Defaults are the game's <c>FlightDataValues.Zero</c>: all zero, <see cref="HelmToBody"/> identity.
/// </summary>
/// <remarks>
/// The other eight fields of the message (<c>q_parent_fixed_to_cci</c>, <c>parent_spin_cci_rps</c>,
/// <c>target_dock_valid</c>, <c>target_q_dock_to_cci</c>, <c>target_dock_rates_cci_rps</c>,
/// <c>parent_mass_kg</c>, <c>q_parent_cce_to_cci</c>) are "leaves": in the game only the leaf
/// sampler fills them, and only for a program that also subscribes to one of
/// <c>MSG_BURN_STATUS</c>, <c>MSG_PILOT_INPUT</c>, <c>MSG_FC_SETTINGS</c>, <c>MSG_BURN_PLAN</c>.
/// In the harness they are reachable through <see cref="SimTelemetry.Leaves"/> regardless.
/// </remarks>
public sealed class SimFlight
{
    /// <summary><c>altitude_m</c>.</summary>
    public double AltitudeM { get; set; }

    /// <summary><c>radar_altitude_m</c>.</summary>
    public double RadarAltitudeM { get; set; }

    /// <summary><c>apoapsis_m</c>.</summary>
    public double ApoapsisM { get; set; }

    /// <summary><c>periapsis_m</c>.</summary>
    public double PeriapsisM { get; set; }

    /// <summary><c>time_to_apoapsis_s</c>.</summary>
    public double TimeToApoapsisS { get; set; }

    /// <summary><c>time_to_periapsis_s</c>.</summary>
    public double TimeToPeriapsisS { get; set; }

    /// <summary><c>speed_mps</c>.</summary>
    public double SpeedMps { get; set; }

    /// <summary><c>surface_speed_mps</c>.</summary>
    public double SurfaceSpeedMps { get; set; }

    /// <summary><c>vertical_speed_mps</c>.</summary>
    public double VerticalSpeedMps { get; set; }

    /// <summary><c>q_helm_to_body</c>. Default identity.</summary>
    public SimQuat HelmToBody { get; set; } = SimQuat.Identity;

    /// <summary><c>target_valid</c>.</summary>
    public bool TargetValid { get; set; }

    /// <summary><c>target_position_helm_m</c>.</summary>
    public SimVec3 TargetPositionHelmM { get; set; }

    /// <summary><c>target_velocity_helm_mps</c>.</summary>
    public SimVec3 TargetVelocityHelmMps { get; set; }

    /// <summary><c>target_range_m</c>.</summary>
    public double TargetRangeM { get; set; }

    /// <summary><c>target_speed_rel_mps</c>.</summary>
    public double TargetSpeedRelMps { get; set; }

    /// <summary><c>in_atmosphere</c>.</summary>
    public bool InAtmosphere { get; set; }

    /// <summary><c>ambient_pressure_pa</c>.</summary>
    public double AmbientPressurePa { get; set; }
}

/// <summary>One <c>thruster[i]</c> of <c>MSG_VEHICLE_PROPS</c> (typed members). Defaults are the game's <c>FakeVehicle</c>'s.</summary>
public sealed class SimThruster
{
    /// <summary><c>force_body_N</c>.</summary>
    public SimVec3 ForceBodyN { get; set; }

    /// <summary><c>torque_body_Nm</c>. Default (1, 0, 0), as the game's <c>FakeVehicle</c> gives every thruster.</summary>
    public SimVec3 TorqueBodyNm { get; set; } = new(1, 0, 0);

    /// <summary><c>min_pulse_s</c>.</summary>
    public double MinPulseS { get; set; }

    /// <summary><c>propellant_ok</c>. Default true.</summary>
    public bool PropellantOk { get; set; } = true;
}

/// <summary>One <c>engine[i]</c> of <c>MSG_VEHICLE_PROPS</c> (typed members). Defaults are the game's <c>FakeVehicle</c>'s: inactive, propellant ok.</summary>
public sealed class SimEngine
{
    /// <summary><c>max_thrust_N</c>.</summary>
    public double MaxThrustN { get; set; }

    /// <summary><c>min_throttle</c>.</summary>
    public double MinThrottle { get; set; }

    /// <summary><c>isp_s</c>.</summary>
    public double IspS { get; set; }

    /// <summary><c>active</c>.</summary>
    public bool Active { get; set; }

    /// <summary><c>propellant_ok</c>. Default true.</summary>
    public bool PropellantOk { get; set; } = true;
}

/// <summary>One <c>gimbal[i]</c> of <c>MSG_VEHICLE_PROPS</c> (typed members).</summary>
public sealed class SimGimbal
{
    /// <summary><c>range_y_rad</c>.</summary>
    public double RangeYRad { get; set; }

    /// <summary><c>range_z_rad</c>.</summary>
    public double RangeZRad { get; set; }
}

/// <summary>
/// The typed part of <c>MSG_VEHICLE_PROPS</c> (0x0401): the game's <c>VehiclePropsValues</c>.
/// Defaults are the game's <c>FakeVehicle.Props()</c>: mass 1 kg, identity inertia, centre of
/// mass at the origin. The element lists may be shorter than the vehicle's layout (missing
/// elements take the defaults of <see cref="SimThruster"/>, <see cref="SimEngine"/>,
/// <see cref="SimGimbal"/>), never longer.
/// </summary>
/// <remarks>
/// Leaf-only members — <c>max_g_load</c>, <c>ang_accel_body_rps2</c>, the <c>rcs_*</c> vectors,
/// <c>thruster[i].control_map</c>/<c>*_ctrl_*</c>/<c>*_fraction</c>, every <c>engine[i]</c> member
/// after <c>propellant_ok</c> (mass flow, cores, nozzles, tables) and every <c>gimbal[i]</c> member
/// after <c>range_z_rad</c> — are filled in the game only by the leaf sampler (see
/// <see cref="SimFlight"/>); in the harness, through <see cref="SimTelemetry.Leaves"/>.
/// </remarks>
public sealed class SimProps
{
    /// <summary><c>mass_kg</c>. Default 1.</summary>
    public double MassKg { get; set; } = 1.0;

    /// <summary><c>I_body_kgm2</c>, row-major (9 values). Default identity.</summary>
    public double[] InertiaRowMajor { get; set; } = [1, 0, 0, 0, 1, 0, 0, 0, 1];

    /// <summary><c>com_body_m</c>.</summary>
    public SimVec3 ComBodyM { get; set; }

    /// <summary>The thrusters, by index.</summary>
    public List<SimThruster> Thrusters { get; set; } = [];

    /// <summary>The engines, by index.</summary>
    public List<SimEngine> Engines { get; set; } = [];

    /// <summary>The gimbals, by index.</summary>
    public List<SimGimbal> Gimbals { get; set; } = [];
}

/// <summary>The burn states of <c>MSG_BURN_STATUS.state</c> (the game's <c>BurnStatusState</c>, the program's <c>KFS_BURN_*</c>).</summary>
public enum SimBurnState : uint
{
    /// <summary>No burn.</summary>
    None = 0,
    /// <summary>Planned.</summary>
    Planned = 1,
    /// <summary>Turning to the burn attitude.</summary>
    Aligning = 2,
    /// <summary>Burning.</summary>
    Burning = 3,
    /// <summary>Ended as planned.</summary>
    Done = 4,
    /// <summary>Ended otherwise.</summary>
    Aborted = 5,
    /// <summary>Accepted, not yet planned by the game.</summary>
    Pending = 6,
}

/// <summary>
/// <c>MSG_BURN_STATUS</c> (0x0402) as a test scripts it. The harness does not fly burns: nothing
/// fills this message unless a test does (the game fills it from its flight computer).
/// </summary>
public sealed class SimBurnStatus
{
    /// <summary><c>burn</c>: the handle (as returned by <c>kfs_plan_*</c>).</summary>
    public uint Burn { get; set; }

    /// <summary><c>state</c>.</summary>
    public SimBurnState State { get; set; }

    /// <summary><c>t_ignition_us</c>.</summary>
    public long IgnitionUs { get; set; }

    /// <summary><c>dv_remaining_mps</c>.</summary>
    public double DvRemainingMps { get; set; }

    /// <summary><c>dv_vlf_mps</c>.</summary>
    public SimVec3 DvVlfMps { get; set; }

    /// <summary><c>end_cause</c> (<c>KFS_BURN_END_*</c>).</summary>
    public uint EndCause { get; set; }
}

/// <summary>
/// What the game would publish to a program in one sample, in plain types: the harness converts it
/// to the game's <c>HostSample</c> and hands it to <c>FakeVehicle.Dispatch(timeNs, Func&lt;HostSample&gt;)</c>.
/// As in the game, the sample is taken at most once per dispatch, just before the first tick that
/// dispatch runs, and every tick of the dispatch sees it; a message reaches a program only if the
/// program subscribes to it (<c>#pragma kfs subscribe</c>).
/// </summary>
/// <remarks>
/// <para>Three layers, applied in the game's order, each overwriting the one before where they overlap:</para>
/// <list type="number">
/// <item><b>Typed</b>: <see cref="Nav"/> (every field of <c>MSG_NAV_STATE</c>), <see cref="Flight"/>
/// (the first 17 fields of <c>MSG_FLIGHT_DATA</c>), <see cref="Props"/> (the typed members of
/// <c>MSG_VEHICLE_PROPS</c>).</item>
/// <item><see cref="BurnStatus"/>, converted to the leaves of <c>MSG_BURN_STATUS</c>.</item>
/// <item><see cref="Leaves"/>: any field of any host-published message by its leaf path
/// (<c>"parent_mass_kg"</c>, <c>"engine[0].mass_flow_kgps"</c>, <c>"target_dock_valid"</c> …),
/// passed as the game's <c>HostSample.Values</c> — a channel the game's own sampler never uses but
/// its packer honours. This is how "leaf" fields, which in the game only the leaf sampler fills,
/// are reachable in the harness. A path the message does not have fails the frame with an
/// <see cref="ArgumentException"/>.</item>
/// </list>
/// <para>Not scriptable: the 16-byte message header (<c>hdr.seq</c> = k + 1, <c>hdr.t_us</c> =
/// the tick's mission time, source 0xFFFF, flags 1 — written by the host from the tick number) and
/// <c>sys.soi_changes</c> (always 0). Messages the host never publishes (<c>MSG_ATT_TARGET</c>,
/// <c>MSG_GUIDANCE_STATUS</c>, <c>MSG_WRENCH_CMD</c>) cannot be scripted. As in the game,
/// <c>MSG_BURN_STATUS</c>, <c>MSG_PILOT_INPUT</c>, <c>MSG_FC_SETTINGS</c> and <c>MSG_BURN_PLAN</c>
/// are delivered every tick to a program that subscribes to them (zero bodies unless scripted).</para>
/// </remarks>
public sealed class SimTelemetry
{
    /// <summary><c>MSG_NAV_STATE</c>.</summary>
    public SimNav Nav { get; set; } = new();

    /// <summary>The typed fields of <c>MSG_FLIGHT_DATA</c>.</summary>
    public SimFlight Flight { get; set; } = new();

    /// <summary>The typed members of <c>MSG_VEHICLE_PROPS</c>.</summary>
    public SimProps Props { get; set; } = new();

    /// <summary><c>MSG_BURN_STATUS</c>, or null for a zero body.</summary>
    public SimBurnStatus? BurnStatus { get; set; }

    /// <summary>Raw leaf values: message name (<c>"MSG_FLIGHT_DATA"</c>) → leaf path → value; applied last.</summary>
    public Dictionary<string, Dictionary<string, double>> Leaves { get; } = new(StringComparer.Ordinal);

    /// <summary>Sets one raw leaf (see <see cref="Leaves"/>) and returns this telemetry.</summary>
    public SimTelemetry Leaf(string message, string path, double value)
    {
        ArgumentException.ThrowIfNullOrEmpty(message);
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (!Leaves.TryGetValue(message, out var fields)) Leaves[message] = fields = new(StringComparer.Ordinal);
        fields[path] = value;
        return this;
    }
}
