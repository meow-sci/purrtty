namespace purrTTY.Kfs.Catalog;

/// <summary>
/// Service ids of purrTTY's kFS services (plan §4.1). Together with <see cref="XCatalog"/> in
/// this file, the only place ids appear; the router switches on these.
/// </summary>
public static class Svc
{
    /// <summary><c>uint32_t x_parts_info(uint32_t what)</c></summary>
    public const ushort PartsInfo = 0x5000;
    /// <summary><c>void x_parts_rescan(void)</c></summary>
    public const ushort PartsRescan = 0x5001;
    /// <summary><c>void x_parts_describe(uint32_t kind)</c></summary>
    public const ushort PartsDescribe = 0x5002;

    /// <summary><c>uint32_t x_light_count(void)</c></summary>
    public const ushort LightCount = 0x5010;
    /// <summary><c>uint32_t x_light_geti(uint32_t light, uint32_t prop)</c></summary>
    public const ushort LightGeti = 0x5011;
    /// <summary><c>float x_light_getf(uint32_t light, uint32_t prop)</c></summary>
    public const ushort LightGetf = 0x5012;
    /// <summary><c>void x_light_set_on(uint32_t light, uint32_t on)</c></summary>
    public const ushort LightSetOn = 0x5013;
    /// <summary><c>void x_light_set_color(uint32_t light, vec3 rgb)</c></summary>
    public const ushort LightSetColor = 0x5014;
    /// <summary><c>void x_light_set_cone(uint32_t light, float inner_rad, float outer_rad)</c></summary>
    public const ushort LightSetCone = 0x5015;
    /// <summary><c>void x_light_set_intensity(uint32_t light, float intensity)</c></summary>
    public const ushort LightSetIntensity = 0x5016;
    /// <summary><c>void x_light_set_range(uint32_t light, float range_m)</c></summary>
    public const ushort LightSetRange = 0x5017;
    /// <summary><c>void x_light_reset(uint32_t light)</c></summary>
    public const ushort LightReset = 0x5018;
    /// <summary><c>void x_lights_set_all(uint32_t on)</c></summary>
    public const ushort LightsSetAll = 0x5019;

    /// <summary><c>uint32_t x_deploy_count(void)</c></summary>
    public const ushort DeployCount = 0x5020;
    /// <summary><c>uint32_t x_deploy_geti(uint32_t index, uint32_t prop)</c></summary>
    public const ushort DeployGeti = 0x5021;
    /// <summary><c>float x_deploy_getf(uint32_t index, uint32_t prop)</c></summary>
    public const ushort DeployGetf = 0x5022;
    /// <summary><c>void x_deploy_set(uint32_t index, uint32_t deployed)</c></summary>
    public const ushort DeploySet = 0x5023;
    /// <summary><c>void x_deploy_toggle(uint32_t index)</c></summary>
    public const ushort DeployToggle = 0x5024;

    /// <summary><c>uint32_t x_actuator_count(void)</c></summary>
    public const ushort ActuatorCount = 0x5030;
    /// <summary><c>uint32_t x_actuator_geti(uint32_t index, uint32_t prop)</c></summary>
    public const ushort ActuatorGeti = 0x5031;
    /// <summary><c>float x_actuator_getf(uint32_t index, uint32_t prop)</c></summary>
    public const ushort ActuatorGetf = 0x5032;
    /// <summary><c>void x_actuator_set(uint32_t index, float value)</c></summary>
    public const ushort ActuatorSet = 0x5033;
}

/// <summary>
/// THE list of purrTTY's kFS services and enumerations (plan §4.1). Every generated artefact —
/// the <c>services.json</c> rows, the <c>enums.json</c> entries, <c>&lt;purrtty.h&gt;</c>,
/// <c>purrtty.kinc</c>, the builtin names, the reference — comes from here through
/// <see cref="CatalogEmitter"/>, and <see cref="CatalogSelfCheck"/> verifies it before anything is
/// installed. Pure data: no game types.
/// </summary>
public static class XCatalog
{
    /// <summary>The kC header name programs include: <c>#include &lt;purrtty.h&gt;</c>.</summary>
    public const string HeaderName = "purrtty.h";

    /// <summary>The kASM include of the selector constants: <c>.include "purrtty.kinc"</c>.</summary>
    public const string KincName = "purrtty.kinc";

    /// <summary>Prefix of every service name and <c>enums.json</c> key.</summary>
    public const string NamePrefix = "x_";

    /// <summary>Prefix of every header constant.</summary>
    public const string ConstantPrefix = "X_";

    // Declared before Enums and Services: static initialisers run in textual order.

    /// <summary><c>x_parts_info</c> selectors.</summary>
    public static readonly EnumDef PartsInfoEnum = EnumDef.From<PartsInfo>("x_parts_info", "X_PARTS_",
        "selectors of x_parts_info");

    /// <summary><c>x_parts_describe</c> kinds.</summary>
    public static readonly EnumDef PartKindEnum = EnumDef.From<PartKind>("x_part_kind", "X_KIND_",
        "part kinds of x_parts_describe");

    /// <summary>Light properties.</summary>
    public static readonly EnumDef LightPropEnum = EnumDef.From<LightProp>("x_light_prop", "X_LIGHT_",
        "light properties: integers 0..15 (x_light_geti), floats 16 and up (x_light_getf)", PropertyRanges.FirstFloat);

    /// <summary>Deployable properties.</summary>
    public static readonly EnumDef DeployPropEnum = EnumDef.From<DeployProp>("x_deploy_prop", "X_DEPLOY_",
        "deployable properties: integers 0..15 (x_deploy_geti), floats 16 and up (x_deploy_getf); position and target are 0..1 of the travel",
        PropertyRanges.FirstFloat);

    /// <summary>Actuator properties.</summary>
    public static readonly EnumDef ActuatorPropEnum = EnumDef.From<ActuatorProp>("x_actuator_prop", "X_ACTUATOR_",
        "actuator properties: integers 0..15 (x_actuator_geti), floats 16 and up (x_actuator_getf); position and target are 0..1 of the travel",
        PropertyRanges.FirstFloat);

    /// <summary>Values of <c>X_DEPLOY_STATE</c> (header only).</summary>
    public static readonly EnumDef DeployStateEnum = EnumDef.From<DeployState>(null, "X_STATE_",
        "values of X_DEPLOY_STATE: X_STATE_NONE (0) where there is no part; X_STATE_BROKEN is never produced in this game build");

    /// <summary>Values of <c>X_LIGHT_TYPE</c> (header only).</summary>
    public static readonly EnumDef LightTypeEnum = EnumDef.From<LightType>(null, "X_LIGHT_TYPE_",
        "values of X_LIGHT_TYPE");

    /// <summary>Every enumeration and constant group, in header order.</summary>
    public static readonly IReadOnlyList<EnumDef> Enums =
    [
        PartsInfoEnum, PartKindEnum, LightPropEnum, DeployPropEnum, ActuatorPropEnum, DeployStateEnum, LightTypeEnum,
    ];

    private const string None = "—";
    private const string AnyIndex = "any";
    private const string NonNegative = "finite:>=0";
    private const string Finite = "finite";

    private const string GetterDoc =
        "Never rejects the index: an index out of range, or a part that has left the vehicle, reads 0, " +
        "so the PRESENT property can be used to probe. Check PRESENT before trusting another property: 0 is also " +
        "a real value of some (a spot light's TYPE, a SWITCH_GROUP of light 0); X_DEPLOY_STATE reads X_STATE_NONE. " +
        "A selector of the other getter is rejected by the VM at issue time.";

    private const string AppearanceDoc =
        "The first appearance write gives this light a private copy of its template, so other lights of the same " +
        "part type are unaffected. The override lasts until x_light_reset, a reload or the part's destruction, and is not saved.";

    /// <summary>Every service, in id order.</summary>
    public static readonly IReadOnlyList<ServiceDef> Services =
    [
        // ---- Bookkeeping
        Get(Svc.PartsInfo, "x_parts_info", "uint32_t", CostClasses.Light,
            [Select("what", "an X_PARTS_* selector", PartsInfoEnum, PartsInfoEnum.RealValues)],
            "Reads a vehicle-wide parts figure: the list generation, the dirty flag or a part count.",
            "X_PARTS_GENERATION changes each time the part lists are rebuilt (each run, x_parts_rescan). " +
            "X_PARTS_DIRTY is 1 when the vehicle's module counts differ from the frozen lists, a hint to rescan. " +
            "X_PARTS_LIGHTS, X_PARTS_DEPLOYABLES and X_PARTS_ACTUATORS are the list lengths.",
            "the selected figure"),
        Act(Svc.PartsRescan, "x_parts_rescan", CostClasses.Medium, [],
            "Rebuilds the vehicle's part lists; indices may change and X_PARTS_GENERATION changes.",
            "Lists are frozen when a run starts and change only here, never spontaneously. Commands issued " +
            "before the rescan still reach the part they named. The rebuild happens at the next frame boundary, " +
            "once however many rescans a tick issues: poll X_PARTS_GENERATION.",
            None),
        Act(Svc.PartsDescribe, "x_parts_describe", CostClasses.Medium,
            [Select("kind", "an X_KIND_* value", PartKindEnum, PartKindEnum.RealValues)],
            "Writes the index table of one part kind, or of all kinds, to the program log.",
            "Shows which index is which part. The table is written when the calling tick ends; a second call within " +
            "the same second of computer time is rejected.",
            "more than one table per second"),

        // ---- Lights
        Get(Svc.LightCount, "x_light_count", "uint32_t", CostClasses.Light, [],
            "Number of lights on the vehicle; indices are 0 to count-1.",
            "Every light emitter of the vehicle, interior fill lights included, ranked by part instance id in the " +
            "list frozen when the run started.",
            "the number of lights"),
        Get(Svc.LightGeti, "x_light_geti", "uint32_t", CostClasses.Light,
            [Index("light", "light index"), IntSelector(LightPropEnum)],
            "Reads an integer property of a light; 0 for an index out of range.",
            GetterDoc, "the property value"),
        Get(Svc.LightGetf, "x_light_getf", "float", CostClasses.Light,
            [Index("light", "light index"), FloatSelector(LightPropEnum)],
            "Reads a float property of a light; 0 for an index out of range.",
            GetterDoc, "the property value"),
        Act(Svc.LightSetOn, "x_light_set_on", CostClasses.Light,
            [Index("light", "light index"), Index("on", "0 switches off, anything else on")],
            "Switches a light's part on or off; every light on the same switch follows.",
            "On and off are per part: emitters sharing a part switch together (see X_LIGHT_SWITCH_GROUP). " +
            "A light without a switch (X_LIGHT_HAS_SWITCH 0) is rejected.",
            "light index out of range or departed; light has no switch"),
        Act(Svc.LightSetColor, "x_light_set_color", CostClasses.Light,
            [Index("light", "light index"), Vec3("rgb", "linear RGB, each component clamped to 0..1", NonNegative)],
            "Overrides a light's emission colour (linear RGB, each component clamped to 0..1).",
            AppearanceDoc,
            "light index out of range or departed"),
        Act(Svc.LightSetCone, "x_light_set_cone", CostClasses.Light,
            [Index("light", "light index"),
             Float("inner_rad", "inner cone half-angle in radians, clamped to 0..outer_rad", NonNegative),
             Float("outer_rad", "outer cone half-angle in radians, clamped to 1e-5..1.5697963", NonNegative)],
            "Overrides a spot light's cone half-angles, in radians; ignored for point lights.",
            AppearanceDoc,
            "light index out of range or departed"),
        Act(Svc.LightSetIntensity, "x_light_set_intensity", CostClasses.Light,
            [Index("light", "light index"), Float("intensity", "unitless, >= 0, clamped to at most 1e6 (stock parts use 0.05..50)", NonNegative)],
            "Overrides a light's intensity (0 to 1e6).",
            AppearanceDoc + " X_LIGHT_STOCK_INTENSITY is the authored value to scale from.",
            "light index out of range or departed"),
        Act(Svc.LightSetRange, "x_light_set_range", CostClasses.Light,
            [Index("light", "light index"), Float("range_m", "range in metres, >= 0, clamped to at most 1e6", NonNegative)],
            "Overrides a light's range in metres (0 to 1e6).",
            AppearanceDoc,
            "light index out of range or departed"),
        Act(Svc.LightReset, "x_light_reset", CostClasses.Light,
            [Index("light", "light index")],
            "Restores a light's stock colour, cone, intensity and range (not its on/off state).",
            "Undoes every appearance override of this light; X_LIGHT_OVERRIDDEN reads 0 afterwards.",
            "light index out of range or departed"),
        Act(Svc.LightsSetAll, "x_lights_set_all", CostClasses.Light,
            [Index("on", "0 switches off, anything else on")],
            "Switches every light that has a switch on or off, like the game's lights key.",
            "Lights without a switch are left alone.",
            None),

        // ---- Deployables: animations the stock part window shows as Deploy / Retract
        Get(Svc.DeployCount, "x_deploy_count", "uint32_t", CostClasses.Light, [],
            "Number of deployables (parts the game shows with Deploy and Retract).",
            "Animations with Deploy / Retract buttons and a duration above zero, ranked by part instance id in the " +
            "list frozen when the run started.",
            "the number of deployables"),
        Get(Svc.DeployGeti, "x_deploy_geti", "uint32_t", CostClasses.Light,
            [Index("index", "deployable index"), IntSelector(DeployPropEnum)],
            "Reads an integer property of a deployable; 0 for an index out of range.",
            GetterDoc, "the property value"),
        Get(Svc.DeployGetf, "x_deploy_getf", "float", CostClasses.Light,
            [Index("index", "deployable index"), FloatSelector(DeployPropEnum)],
            "Reads a float property of a deployable; 0 for an index out of range.",
            GetterDoc, "the property value"),
        Act(Svc.DeploySet, "x_deploy_set", CostClasses.Light,
            [Index("index", "deployable index"), Index("deployed", "0 retracts, anything else deploys")],
            "Deploys (1) or retracts (0) a deployable.",
            "The animation moves at its authored rate: poll X_DEPLOY_STATE or X_DEPLOY_POSITION.",
            "deployable index out of range or departed"),
        Act(Svc.DeployToggle, "x_deploy_toggle", CostClasses.Light,
            [Index("index", "deployable index")],
            "Flips a deployable's commanded state; valid mid-travel.",
            "The animation reverses from where it is.",
            "deployable index out of range or departed"),

        // ---- Actuators: animations the stock part window shows as a 0..1 slider
        Get(Svc.ActuatorCount, "x_actuator_count", "uint32_t", CostClasses.Light, [],
            "Number of actuators (parts the game shows with a 0..1 slider).",
            "Animations without Deploy / Retract buttons, ranked by part instance id in the list frozen when the run started.",
            "the number of actuators"),
        Get(Svc.ActuatorGeti, "x_actuator_geti", "uint32_t", CostClasses.Light,
            [Index("index", "actuator index"), IntSelector(ActuatorPropEnum)],
            "Reads an integer property of an actuator; 0 for an index out of range.",
            GetterDoc, "the property value"),
        Get(Svc.ActuatorGetf, "x_actuator_getf", "float", CostClasses.Light,
            [Index("index", "actuator index"), FloatSelector(ActuatorPropEnum)],
            "Reads a float property of an actuator; 0 for an index out of range.",
            GetterDoc, "the property value"),
        Act(Svc.ActuatorSet, "x_actuator_set", CostClasses.Light,
            [Index("index", "actuator index"), Float("value", "target position, 0..1 of the travel (clamped)", Finite)],
            "Drives an actuator toward a position, 0..1 of its travel.",
            "The part moves at its authored rate; poll X_ACTUATOR_POSITION. A program that changes the target every " +
            "tick keeps the part moving and the vehicle off rails.",
            "actuator index out of range or departed"),
    ];

    /// <summary>The catalogue as one value, for the emitter and the self-check.</summary>
    public static readonly ServiceCatalog Catalog = new(Services, Enums);

    // ------------------------------------------------------------------ definition helpers

    private static ServiceDef Get(ushort id, string name, string returns, string cost, ParamDef[] ps,
        string summary, string doc, string resultDoc) =>
        new(id, name, ServiceKinds.RequestResult, ps, returns, [returns == "float" ? "f32" : "u32"], cost, summary, None, doc, resultDoc);

    private static ServiceDef Act(ushort id, string name, string cost, ParamDef[] ps, string summary, string doc, string hostChecks) =>
        new(id, name, ServiceKinds.Request, ps, "void", [], cost, summary, hostChecks, doc);

    private static ParamDef Index(string name, string doc) => new(name, "uint32_t", ["u32"], AnyIndex, doc);

    private static ParamDef Float(string name, string doc, string domain) => new(name, "float", ["f32"], domain, doc);

    private static ParamDef Vec3(string name, string doc, string domain) => new(name, "vec3", ["f32", "f32", "f32"], domain, doc);

    /// <summary>A <c>u32</c> selector whose domain admits exactly <paramref name="values"/>.</summary>
    private static ParamDef Select(string name, string doc, EnumDef e, IEnumerable<int> values)
    {
        int[] admitted = values.Order().ToArray();
        bool whole = admitted.Length == e.Values.Count;
        string domain = whole ? $"enum:{e.Key}" : $"enum:{e.Key}[{admitted[0]}..{admitted[^1]}]";
        return new ParamDef(name, "uint32_t", ["u32"], domain, doc, admitted);
    }

    private static ParamDef IntSelector(EnumDef e) => PropertySelector(e, "integer", e.RealValues.Where(v => v < e.FirstFloat));

    private static ParamDef FloatSelector(EnumDef e) => PropertySelector(e, "float", e.RealValues.Where(v => v >= e.FirstFloat));

    private static ParamDef PropertySelector(EnumDef e, string kind, IEnumerable<int> values)
    {
        int[] admitted = values.Order().ToArray();
        string first = e.Constants.First(c => c.Value == admitted[0]).Name;
        string last = e.Constants.First(c => c.Value == admitted[^1]).Name;
        return Select("prop", $"an {e.CPrefix}* {kind} property, {first} to {last}", e, admitted);
    }
}
