using purrTTY.Kfs.Catalog;

namespace purrTTY.Kfs.Runtime;

/// <summary>
/// The pure clamps and conversions of plan §4.1, shared by every parts back end (the simulated one
/// in tests and the game one) so that a program sees the same values from both. No game types, no
/// state. Every float from a program has already been checked by the VM (the rows' argument
/// domains: finite, and ≥ 0 for magnitudes), but every entry point here is still total: any
/// input — NaN, ±∞, <see cref="float.MaxValue"/> — gives a finite result inside the documented
/// range and never throws, so a back end can never be handed a value the game would choke on.
/// </summary>
public static class PartMath
{
    /// <summary>Smallest outer cone half-angle in radians (the stock light module's lower bound).</summary>
    public const float MinOuterConeRad = 1e-5f;

    /// <summary>Largest outer cone half-angle in radians (just under π/2, the stock upper bound).</summary>
    public const float MaxOuterConeRad = 1.5697963f;

    /// <summary>
    /// Largest light intensity a program can set (stock parts use 0.05–50). The VM's
    /// <c>finite:&gt;=0</c> domain admits up to <see cref="float.MaxValue"/>, which no renderer
    /// should be handed.
    /// </summary>
    public const float MaxIntensity = 1e6f;

    /// <summary>Largest light range in metres a program can set (1000 km).</summary>
    public const float MaxRangeM = 1e6f;

    /// <summary>A colour component clamped to [0, 1]; NaN reads 0.</summary>
    public static float Color(float component) => float.IsNaN(component) ? 0f : Math.Clamp(component, 0f, 1f);

    /// <summary>An outer cone half-angle clamped to [<see cref="MinOuterConeRad"/>, <see cref="MaxOuterConeRad"/>]; NaN reads the minimum.</summary>
    public static float OuterCone(float outerRad) =>
        float.IsNaN(outerRad) ? MinOuterConeRad : Math.Clamp(outerRad, MinOuterConeRad, MaxOuterConeRad);

    /// <summary>
    /// An inner cone half-angle clamped to [0, outer], where outer is
    /// <paramref name="clampedOuterRad"/> banded by <see cref="OuterCone"/> first (a no-op for a
    /// value that already is); NaN reads 0.
    /// </summary>
    public static float InnerCone(float innerRad, float clampedOuterRad) =>
        float.IsNaN(innerRad) ? 0f : Math.Clamp(innerRad, 0f, OuterCone(clampedOuterRad));

    /// <summary>
    /// Both cone half-angles as a light gets them: the outer one clamped to its band first, then
    /// the inner one to [0, outer], so the inner cone never exceeds the outer.
    /// </summary>
    public static (float InnerRad, float OuterRad) Cone(float innerRad, float outerRad)
    {
        float outer = OuterCone(outerRad);
        return (InnerCone(innerRad, outer), outer);
    }

    /// <summary>A light intensity clamped to [0, <see cref="MaxIntensity"/>]; NaN reads 0.</summary>
    public static float Intensity(float value) => Magnitude(value, MaxIntensity);

    /// <summary>A light range in metres clamped to [0, <see cref="MaxRangeM"/>]; NaN reads 0.</summary>
    public static float RangeM(float value) => Magnitude(value, MaxRangeM);

    /// <summary>A fraction of an animation's travel clamped to [0, 1]; NaN reads 0.</summary>
    public static float Fraction(float value) => float.IsNaN(value) ? 0f : Math.Clamp(value, 0f, 1f);

    /// <summary>
    /// A goal in seconds from a fraction of the travel: the fraction clamped to [0, 1] times the
    /// animation's duration. A duration that is not finite and positive gives 0.
    /// </summary>
    public static double FractionToSeconds(float fraction, double durationS) =>
        UsableDuration(durationS) ? Fraction(fraction) * durationS : 0d;

    /// <summary>
    /// A position or goal in seconds as a fraction of the travel, clamped to [0, 1]; 0 for a
    /// duration that is not finite and positive, and for a NaN time.
    /// </summary>
    public static float SecondsToFraction(double seconds, double durationS)
    {
        if (!UsableDuration(durationS) || double.IsNaN(seconds)) return 0f;
        return (float)Math.Clamp(seconds / durationS, 0d, 1d);
    }

    /// <summary>True while the position differs from the goal (a non-finite time counts as 0).</summary>
    public static bool Moving(double positionS, double goalS) => Time(positionS) != Time(goalS);

    /// <summary>
    /// The deployment state of an animation as programs read it: at its goal it is
    /// <see cref="DeployState.Deployed"/> when the goal is above zero and
    /// <see cref="DeployState.Retracted"/> otherwise; on its way it is
    /// <see cref="DeployState.Deploying"/> or <see cref="DeployState.Retracting"/> by direction (the
    /// game's <c>KeyframeAnimationModule.DeriveDeploymentState</c>, numbered for programs). A
    /// non-finite time counts as 0. Never <see cref="DeployState.None"/>: that is for no part.
    /// </summary>
    public static DeployState State(double positionS, double goalS)
    {
        double position = Time(positionS), goal = Time(goalS);
        return position == goal ? (goal > 0 ? DeployState.Deployed : DeployState.Retracted)
            : position < goal ? DeployState.Deploying
            : DeployState.Retracting;
    }

    /// <summary>
    /// The game's <c>KeyframeAnimationModule.DeploymentState</c> (Deployed 0, Retracted 1,
    /// Deploying 2, Retracting 3, Broken 4) as programs read it (<see cref="DeployState"/>, 1–5); a
    /// value the game does not define reads <see cref="DeployState.None"/>.
    /// </summary>
    public static DeployState FromGameDeploymentState(int gameState) =>
        gameState is >= 0 and <= 4 ? (DeployState)(uint)(gameState + 1) : DeployState.None;

    /// <summary>
    /// The position after <paramref name="dtSeconds"/> of slewing toward <paramref name="goalS"/> at
    /// one animation-second per simulated second, landing exactly on the goal. A non-finite
    /// position or goal counts as 0; a time step that is not positive (or NaN) moves nothing.
    /// </summary>
    public static double Slew(double positionS, double goalS, double dtSeconds)
    {
        double position = Time(positionS), goal = Time(goalS);
        if (position == goal || !(dtSeconds > 0)) return position;
        return position < goal ? Math.Min(goal, position + dtSeconds) : Math.Max(goal, position - dtSeconds);
    }

    /// <summary>
    /// The switch group of each light: the lowest index whose switch is the same object, or the
    /// light's own index when it has none (null). The switch objects are compared by reference.
    /// </summary>
    public static int[] SwitchGroups<TSwitch>(IReadOnlyList<TSwitch?> switches) where TSwitch : class
    {
        ArgumentNullException.ThrowIfNull(switches);
        var groups = new int[switches.Count];
        var first = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);
        for (int i = 0; i < groups.Length; i++)
        {
            TSwitch? sw = switches[i];
            if (sw is null) { groups[i] = i; continue; }
            if (!first.TryGetValue(sw, out int group)) first[sw] = group = i;
            groups[i] = group;
        }
        return groups;
    }

    private static float Magnitude(float value, float max) => float.IsNaN(value) ? 0f : Math.Clamp(value, 0f, max);

    private static bool UsableDuration(double durationS) => double.IsFinite(durationS) && durationS > 0;

    private static double Time(double seconds) => double.IsFinite(seconds) ? seconds : 0d;
}
