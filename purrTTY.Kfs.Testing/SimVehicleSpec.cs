using purrTTY.Kfs.Catalog;

namespace purrTTY.Kfs.Testing;

/// <summary>
/// A simulated vehicle: the game's <c>FakeVehicle</c> layout (thrusters, engines, gimbals — the
/// element counts of <c>MSG_VEHICLE_PROPS</c> and the <c>THRUSTER_COUNT</c>/<c>ENGINE_COUNT</c>/<c>GIMBAL_COUNT</c>
/// of <c>&lt;vehicle.h&gt;</c>) plus simulated lights and animations.
/// </summary>
public sealed record SimVehicleSpec
{
    /// <summary>The vehicle (and computer) name.</summary>
    public string Name { get; init; } = "sim";

    /// <summary>Thrusters in the generated layout (the game's <c>FakeVehicle</c> needs at least one).</summary>
    public int Thrusters { get; init; } = 1;

    /// <summary>Engines in the generated layout.</summary>
    public int Engines { get; init; }

    /// <summary>Gimbals in the generated layout.</summary>
    public int Gimbals { get; init; }

    /// <summary>The lights, in ranking order (light i gets part id 100 + i unless its spec says otherwise).</summary>
    public IReadOnlyList<SimLightSpec> Lights { get; init; } = [];

    /// <summary>The deployables (part ids 200 + i by default).</summary>
    public IReadOnlyList<SimAnimationSpec> Deployables { get; init; } = [];

    /// <summary>The actuators (part ids 300 + i by default).</summary>
    public IReadOnlyList<SimAnimationSpec> Actuators { get; init; } = [];

    /// <summary>A vehicle with that many default lights, deployables (2 s) and actuators (1 s), engines, gimbals and thrusters.</summary>
    public static SimVehicleSpec With(int lights = 0, int deployables = 0, int actuators = 0, string name = "sim",
        int engines = 0, int gimbals = 0, int thrusters = 1) => new()
    {
        Name = name,
        Engines = engines,
        Gimbals = gimbals,
        Thrusters = thrusters,
        Lights = Enumerable.Range(0, lights).Select(i => new SimLightSpec { Name = $"Light {i}" }).ToArray(),
        Deployables = Enumerable.Range(0, deployables).Select(i => new SimAnimationSpec { Name = $"Deployable {i}" }).ToArray(),
        Actuators = Enumerable.Range(0, actuators).Select(i => new SimAnimationSpec { Name = $"Actuator {i}", DurationS = 1.0 }).ToArray(),
    };
}

/// <summary>A simulated light emitter. Defaults are a switched, powered spot light.</summary>
public sealed record SimLightSpec
{
    /// <summary>The part's display name.</summary>
    public string Name { get; init; } = "Light";

    /// <summary>The part kind's key (the game's template id).</summary>
    public string Key { get; init; } = "sim_light";

    /// <summary>The part id; null for the default (100 + position).</summary>
    public uint? PartId { get; init; }

    /// <summary>Spot or point.</summary>
    public LightType Type { get; init; } = LightType.Spot;

    /// <summary>An interior fill light: no switch, always lit.</summary>
    public bool Interior { get; init; }

    /// <summary>False for a light whose part has no switch (it reads lit whatever its power, as in the game).</summary>
    public bool HasSwitch { get; init; } = true;

    /// <summary>Index of an earlier light whose switch this one shares (emitters of one part); null for its own switch.</summary>
    public int? SharesSwitchWith { get; init; }

    /// <summary>Initial switch state.</summary>
    public bool On { get; init; }

    /// <summary>Whether the light has electrical power.</summary>
    public bool Powered { get; init; } = true;

    /// <summary>Authored intensity.</summary>
    public float Intensity { get; init; } = 2f;

    /// <summary>Authored range in metres.</summary>
    public float RangeM { get; init; } = 30f;

    /// <summary>Authored colour, red.</summary>
    public float R { get; init; } = 1f;

    /// <summary>Authored colour, green.</summary>
    public float G { get; init; } = 0.9f;

    /// <summary>Authored colour, blue.</summary>
    public float B { get; init; } = 0.8f;

    /// <summary>Authored inner cone half-angle in radians.</summary>
    public float InnerRad { get; init; } = 0.3f;

    /// <summary>Authored outer cone half-angle in radians.</summary>
    public float OuterRad { get; init; } = 0.6f;
}

/// <summary>A simulated animation (deployable or actuator).</summary>
public sealed record SimAnimationSpec
{
    /// <summary>The part's display name.</summary>
    public string Name { get; init; } = "Animation";

    /// <summary>The part kind's key.</summary>
    public string Key { get; init; } = "sim_animation";

    /// <summary>The part id; null for the default (200 + position for deployables, 300 + position for actuators).</summary>
    public uint? PartId { get; init; }

    /// <summary>The authored length in seconds; an animation with a duration of 0 or less is in no list (as in the game).</summary>
    public double DurationS { get; init; } = 2.0;

    /// <summary>Initial position and goal, as a fraction of the travel.</summary>
    public double InitialFraction { get; init; }

    /// <summary>The link flags (<c>AnimSlot.Link*</c>).</summary>
    public uint Links { get; init; }
}
