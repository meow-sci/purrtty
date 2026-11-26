namespace purrTTY.Kfs.Catalog;

// The selector and constant enumerations of plan §4.1. A member's numeric value IS its position
// in enums.json and the value of its header #define; its name, snake_cased, is the enums.json
// entry (HasSwitch → has_switch → X_LIGHT_HAS_SWITCH). These lists are part of the image format:
// never renumber or reorder a member. A new integer property takes the next value below
// PropertyRanges.FirstFloat, a new float property the next value after the last float.

/// <summary>Where integer and float properties lie in a property enumeration.</summary>
public static class PropertyRanges
{
    /// <summary>Integer properties are below this value (read with <c>…_geti</c>), float properties from it on (<c>…_getf</c>).</summary>
    public const int FirstFloat = 16;
}

/// <summary>Selectors of <c>x_parts_info</c> (<c>X_PARTS_*</c>, enumeration <c>x_parts_info</c>).</summary>
public enum PartsInfo : uint
{
    /// <summary>Generation of the frozen part lists; changes on every rebuild.</summary>
    Generation = 0,
    /// <summary>1 when the vehicle's module counts differ from the frozen lists.</summary>
    Dirty = 1,
    /// <summary>Number of lights.</summary>
    Lights = 2,
    /// <summary>Number of deployables.</summary>
    Deployables = 3,
    /// <summary>Number of actuators.</summary>
    Actuators = 4,
}

/// <summary>Part kinds for <c>x_parts_describe</c> (<c>X_KIND_*</c>, enumeration <c>x_part_kind</c>).</summary>
public enum PartKind : uint
{
    /// <summary>Every kind.</summary>
    All = 0,
    /// <summary>Lights.</summary>
    Light = 1,
    /// <summary>Deployables.</summary>
    Deployable = 2,
    /// <summary>Actuators.</summary>
    Actuator = 3,
}

/// <summary>Light properties (<c>X_LIGHT_*</c>, enumeration <c>x_light_prop</c>): integers 0–15, floats 16 and up.</summary>
public enum LightProp : uint
{
    /// <summary>1 while the light is still on this vehicle.</summary>
    Present = 0,
    /// <summary>1 when the light has a usable switch.</summary>
    HasSwitch = 1,
    /// <summary>Commanded state of the switch (1 for switchless lights).</summary>
    On = 2,
    /// <summary>Actually emitting: switched on and powered.</summary>
    Lit = 3,
    /// <summary>A <see cref="LightType"/> value.</summary>
    Type = 4,
    /// <summary>The part's instance id (session-only).</summary>
    PartId = 5,
    /// <summary>Lowest light index sharing this light's switch.</summary>
    SwitchGroup = 6,
    /// <summary>1 once a program changed the light's appearance.</summary>
    Overridden = 7,
    /// <summary>Current intensity.</summary>
    Intensity = PropertyRanges.FirstFloat,
    /// <summary>Current range in metres.</summary>
    RangeM = 17,
    /// <summary>Current colour, red.</summary>
    ColorR = 18,
    /// <summary>Current colour, green.</summary>
    ColorG = 19,
    /// <summary>Current colour, blue.</summary>
    ColorB = 20,
    /// <summary>Current inner cone half-angle in radians.</summary>
    InnerRad = 21,
    /// <summary>Current outer cone half-angle in radians.</summary>
    OuterRad = 22,
    /// <summary>The part's authored intensity.</summary>
    StockIntensity = 23,
}

/// <summary>Deployable properties (<c>X_DEPLOY_*</c>, enumeration <c>x_deploy_prop</c>): integers 0–15, floats 16 and up.</summary>
public enum DeployProp : uint
{
    /// <summary>1 while the deployable is still on this vehicle.</summary>
    Present = 0,
    /// <summary>A <see cref="DeployState"/> value.</summary>
    State = 1,
    /// <summary>1 when the goal is above zero.</summary>
    Commanded = 2,
    /// <summary>1 while the position differs from the goal.</summary>
    Moving = 3,
    /// <summary>The part's instance id (session-only).</summary>
    PartId = 4,
    /// <summary>Bit 1: drives a solar panel; bit 2: also driven by a parachute; bit 4: aims a light.</summary>
    Links = 5,
    /// <summary>Position, 0–1 of the travel.</summary>
    Position = PropertyRanges.FirstFloat,
    /// <summary>Target, 0–1 of the travel.</summary>
    Target = 17,
    /// <summary>The animation's authored length in simulated seconds.</summary>
    DurationS = 18,
}

/// <summary>Actuator properties (<c>X_ACTUATOR_*</c>, enumeration <c>x_actuator_prop</c>): integers 0–15, floats 16 and up.</summary>
public enum ActuatorProp : uint
{
    /// <summary>1 while the actuator is still on this vehicle.</summary>
    Present = 0,
    /// <summary>1 while the position differs from the goal.</summary>
    Moving = 1,
    /// <summary>The part's instance id (session-only).</summary>
    PartId = 2,
    /// <summary>As <see cref="DeployProp.Links"/>.</summary>
    Links = 3,
    /// <summary>Position, 0–1 of the travel.</summary>
    Position = PropertyRanges.FirstFloat,
    /// <summary>Target, 0–1 of the travel.</summary>
    Target = 17,
    /// <summary>The animation's authored length in simulated seconds.</summary>
    DurationS = 18,
}

/// <summary>
/// Values of <c>X_DEPLOY_STATE</c> (<c>X_STATE_*</c>; header-only, not in <c>enums.json</c>), as
/// programs read them. <see cref="None"/> is 0 so that the "nothing" every getter reads where there
/// is no part (an index out of range, a departed part, the bench, a failing back end) can never be
/// mistaken for a state. The game's own <c>KeyframeAnimationModule.DeploymentState</c> is
/// numbered differently (Deployed 0 … Broken 4); <see cref="Runtime.PartMath.FromGameDeploymentState"/>
/// converts at the boundary.
/// </summary>
public enum DeployState : uint
{
    /// <summary>No part: what an absent, out-of-range or unreadable deployable reads.</summary>
    None = 0,
    /// <summary>Fully deployed.</summary>
    Deployed = 1,
    /// <summary>Fully retracted.</summary>
    Retracted = 2,
    /// <summary>Moving toward deployed.</summary>
    Deploying = 3,
    /// <summary>Moving toward retracted.</summary>
    Retracting = 4,
    /// <summary>Broken (never produced in this game build).</summary>
    Broken = 5,
}

/// <summary>Values of <c>X_LIGHT_TYPE</c> (<c>X_LIGHT_TYPE_*</c>; header-only, not in <c>enums.json</c>).</summary>
public enum LightType : uint
{
    /// <summary>A spot light (has a cone).</summary>
    Spot = 0,
    /// <summary>A point light.</summary>
    Point = 1,
}
