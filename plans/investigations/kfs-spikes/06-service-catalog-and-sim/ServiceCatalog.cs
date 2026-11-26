// Single source of truth for the purrTTY kFS services.
//
// Everything a kFS service needs on the toolchain side is generated from the two lists below:
//   * the JSON rows appended to services.json         -> ServiceRowsJson()
//   * the enumerations merged into enums.json          -> EnumsJson()
//   * the text of <purrtty.h>                          -> Header()
//   * the names registered in SystemHeaders.Builtins   -> BuiltinNames
//   * id -> definition                                 -> ById
//   * a kASM .equ block for the enum constants         -> KasmEquates()
// SelfCheck() re-derives what the game's loader and compiler will check and fails loudly before the
// game gets a chance to throw (a prototype/row cell-count mismatch is a compiler *exception*, not a
// diagnostic; a kc-name grouping failure takes the whole table load down).
//
// This file deliberately has no dependency on KSA types, so it can be unit-tested on its own.

using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace PurrKfs;

// ---- Enumerations. The C# enums ARE the value lists: value names are snake_cased member names and
// a member's numeric value is its list position in enums.json (which numbers entries from 0). Gaps
// become `reservedN` filler entries, so one enumeration per part kind can hold the integer
// properties in [0..15] and the float properties in [16..], and each getter's argument domain is a
// sub-range (`enum:purr_light_prop[0..7]`). The VM then rejects a float selector passed to geti and
// vice versa at issue time. Fillers get no #define. ----

public enum PartsInfo { Generation, Dirty, Lights, Deployables, Actuators }
public enum PartKind { All, Light, Deployable, Actuator }
public enum LightProp
{
    Present, HasSwitch, On, Lit, Type, PartId, SwitchGroup, Overridden,
    Intensity = PropRanges.FirstFloat, RangeM, ColorR, ColorG, ColorB, InnerRad, OuterRad, StockIntensity,
}
public enum DeployProp { Present, State, Commanded, Moving, PartId, Links, Position = PropRanges.FirstFloat, Target, DurationS }
public enum ActuatorProp { Present, Moving, PartId, Links, Position = PropRanges.FirstFloat, Target, DurationS }
// Header-only constant groups (values returned by geti, never used as an argument domain).
public enum DeployState { Deployed, Retracted, Deploying, Retracting, Broken }
public enum LightType { Spot, Point }

public static class PropRanges
{
    public const int FirstFloat = 16;
    public static bool IsFloat(int v) => v >= FirstFloat;
}

/// <summary>An enumeration: <see cref="TableName"/> is the enums.json key (null = header only).</summary>
public sealed record EnumDef(string? TableName, string HeaderPrefix, Type Members, string Doc)
{
    public const string FillerPrefix = "reserved";

    /// The enums.json list: position = numeric value; gaps filled with `reservedN`.
    public IReadOnlyList<string> Values
    {
        get
        {
            var byValue = Enum.GetValues(Members).Cast<object>().ToDictionary(v => Convert.ToInt32(v), v => ServiceCatalog.Snake(v.ToString()!));
            int max = byValue.Keys.Max();
            return Enumerable.Range(0, max + 1).Select(i => byValue.TryGetValue(i, out var n) ? n : FillerPrefix + i).ToList();
        }
    }

    public static bool IsFiller(string value) => value.StartsWith(FillerPrefix, StringComparison.Ordinal);

    public IEnumerable<(string Macro, int Value)> Macros =>
        Values.Select((v, i) => (v, i)).Where(x => !IsFiller(x.v)).Select(x => (HeaderPrefix + x.v.ToUpperInvariant(), x.i));

    /// `enum:<key>[a..b]` covering the integer (floats: false) or float (floats: true) members.
    public string SubRange(bool floats)
    {
        var vals = Enum.GetValues(Members).Cast<object>().Select(Convert.ToInt32).Where(v => PropRanges.IsFloat(v) == floats).ToList();
        return $"enum:{TableName}[{vals.Min()}..{vals.Max()}]";
    }
}

/// <summary>One kC parameter. <see cref="CType"/> is a kC type the catalog knows how to flatten.</summary>
public sealed record Param(string CType, string Name, string Domain, string Doc);

public sealed record ServiceDef(
    ushort Id, string Name, string Returns, Param[] Params, string Cost,
    string Summary, string HostChecks, string ResultDoc = "")
{
    public bool HasResult => Returns != "void";
    public string Kind => HasResult ? "request+result" : "request";
    public string Symbol => "SVC_" + Name.ToUpperInvariant();
    public string[] ArgTypes => Params.SelectMany(p => ServiceCatalog.Cells(p.CType)).ToArray();
    /// A struct parameter's domain applies to each of its cells.
    public string[] ArgDomains => Params.SelectMany(p => ServiceCatalog.Cells(p.CType).Select(_ => p.Domain)).ToArray();
    public string[] ResultTypes => ServiceCatalog.Cells(Returns);
    public string Prototype =>
        $"{Returns} {Name}({(Params.Length == 0 ? "void" : string.Join(", ", Params.Select(p => $"{p.CType} {p.Name}")))})";
}

public static class ServiceCatalog
{
    public const ushort FirstId = 0x5000, LastId = 0x50FF;
    public const string HeaderName = "purrtty.h";

    static Param U(string name, string doc, string domain = "any") => new("uint32_t", name, domain, doc);
    static Param F(string name, string doc, string domain) => new("float", name, domain, doc);

    // Declared before Enums/Services: static initialisers run in textual order.
    static readonly EnumDef LightProps = new("purr_light_prop", "PURR_LIGHT_", typeof(LightProp),
        "light properties: integers (purr_light_geti) 0..15, floats (purr_light_getf) 16..");
    static readonly EnumDef DeployProps = new("purr_deploy_prop", "PURR_DEPLOY_", typeof(DeployProp),
        "deployable properties: integers (purr_deploy_geti) 0..15, floats (purr_deploy_getf) 16..; position and target are 0..1 of the travel");
    static readonly EnumDef ActuatorProps = new("purr_actuator_prop", "PURR_ACTUATOR_", typeof(ActuatorProp),
        "actuator properties: integers (purr_actuator_geti) 0..15, floats (purr_actuator_getf) 16..; position and target are 0..1 of the travel");

    public static readonly EnumDef[] Enums =
    {
        new("purr_parts_info",     "PURR_PARTS_",      typeof(PartsInfo),     "selectors for purr_parts_info()"),
        new("purr_part_kind",      "PURR_KIND_",       typeof(PartKind),      "part kinds for purr_parts_describe()"),
        LightProps, DeployProps, ActuatorProps,
        new(null,                  "PURR_STATE_",      typeof(DeployState),   "values of PURR_DEPLOY_STATE"),
        new(null,                  "PURR_LIGHT_TYPE_", typeof(LightType),     "values of PURR_LIGHT_TYPE"),
    };

    const string Light = "Light", Medium = "Medium";
    const string LightIdx = "light index out of range", AnimIdx = "index out of range";

    public static readonly ServiceDef[] Services =
    {
        new(0x5000, "purr_parts_info", "uint32_t", new[] { U("what", "a PURR_PARTS_* selector", "enum:purr_parts_info") }, Light,
            "Reads a vehicle-wide parts figure: generation, dirty flag or a part count.", "—", "the selected figure"),
        new(0x5001, "purr_parts_rescan", "void", Array.Empty<Param>(), Medium,
            "Asks the host to rebuild the vehicle's part lists; PURR_PARTS_GENERATION increases once it has.", "—"),
        new(0x5002, "purr_parts_describe", "void", new[] { U("kind", "a PURR_KIND_* value", "enum:purr_part_kind") }, Medium,
            "Writes a description of the vehicle's parts of one kind to the program log.", "—"),

        new(0x5010, "purr_light_count", "uint32_t", Array.Empty<Param>(), Light,
            "Number of lights on the vehicle (indices are 0..count-1).", "—", "the number of lights"),
        new(0x5011, "purr_light_geti", "uint32_t", new[] { U("light", "light index"), U("prop", "a PURR_LIGHT_* integer property", LightProps.SubRange(floats: false)) }, Light,
            "Reads an integer property of a light; 0 for an index out of range.", "—", "the property"),
        new(0x5012, "purr_light_getf", "float", new[] { U("light", "light index"), U("prop", "a PURR_LIGHT_* float property", LightProps.SubRange(floats: true)) }, Light,
            "Reads a float property of a light; 0 for an index out of range.", "—", "the property"),
        new(0x5013, "purr_light_set_on", "void", new[] { U("light", "light index"), U("on", "0 off, otherwise on") }, Light,
            "Switches a light (and every light on the same switch) on or off.", LightIdx + "; light has no switch"),
        new(0x5014, "purr_light_set_color", "void", new[] { U("light", "light index"), new Param("vec3", "rgb", "finite:>=0", "colour, each component clamped to 0..1") }, Light,
            "Overrides a light's colour.", LightIdx),
        new(0x5015, "purr_light_set_cone", "void", new[] { U("light", "light index"), F("inner_rad", "inner cone angle, clamped to 0..outer", "finite:>=0"), F("outer_rad", "outer cone angle, clamped to 1e-5..1.5697963", "finite:>=0") }, Light,
            "Overrides a spot light's cone angles.", LightIdx),
        new(0x5016, "purr_light_set_intensity", "void", new[] { U("light", "light index"), F("intensity", "intensity, >= 0", "finite:>=0") }, Light,
            "Overrides a light's intensity.", LightIdx),
        new(0x5017, "purr_light_set_range", "void", new[] { U("light", "light index"), F("range_m", "range in metres, >= 0", "finite:>=0") }, Light,
            "Overrides a light's range.", LightIdx),
        new(0x5018, "purr_light_reset", "void", new[] { U("light", "light index") }, Light,
            "Restores a light's stock colour, cone, intensity and range.", LightIdx),
        new(0x5019, "purr_lights_set_all", "void", new[] { U("on", "0 off, otherwise on") }, Light,
            "Switches every light that has a switch on or off.", "—"),

        new(0x5020, "purr_deploy_count", "uint32_t", Array.Empty<Param>(), Light,
            "Number of deployables on the vehicle.", "—", "the number of deployables"),
        new(0x5021, "purr_deploy_geti", "uint32_t", new[] { U("index", "deployable index"), U("prop", "a PURR_DEPLOY_* integer property", DeployProps.SubRange(floats: false)) }, Light,
            "Reads an integer property of a deployable; 0 for an index out of range.", "—", "the property"),
        new(0x5022, "purr_deploy_getf", "float", new[] { U("index", "deployable index"), U("prop", "a PURR_DEPLOY_* float property", DeployProps.SubRange(floats: true)) }, Light,
            "Reads a float property of a deployable's animation; 0 for an index out of range.", "—", "the property"),
        new(0x5023, "purr_deploy_set", "void", new[] { U("index", "deployable index"), U("deployed", "0 retract, otherwise deploy") }, Light,
            "Deploys or retracts a deployable.", AnimIdx + "; deployable broken"),
        new(0x5024, "purr_deploy_toggle", "void", new[] { U("index", "deployable index") }, Light,
            "Flips a deployable's commanded state.", AnimIdx + "; deployable broken"),

        new(0x5030, "purr_actuator_count", "uint32_t", Array.Empty<Param>(), Light,
            "Number of actuators (position-controlled animations) on the vehicle.", "—", "the number of actuators"),
        new(0x5031, "purr_actuator_geti", "uint32_t", new[] { U("index", "actuator index"), U("prop", "a PURR_ACTUATOR_* integer property", ActuatorProps.SubRange(floats: false)) }, Light,
            "Reads an integer property of an actuator; 0 for an index out of range.", "—", "the property"),
        new(0x5032, "purr_actuator_getf", "float", new[] { U("index", "actuator index"), U("prop", "a PURR_ACTUATOR_* float property", ActuatorProps.SubRange(floats: true)) }, Light,
            "Reads a float property of an actuator's animation; 0 for an index out of range.", "—", "the property"),
        new(0x5033, "purr_actuator_set", "void", new[] { U("index", "actuator index"), F("value", "target position, clamped to 0..1 of the travel", "finite") }, Light,
            "Drives an actuator toward a position (0..1 of its travel).", AnimIdx),
    };

    public static readonly IReadOnlyDictionary<ushort, ServiceDef> ById = Services.ToDictionary(s => s.Id);
    public static IEnumerable<string> BuiltinNames => Services.Select(s => s.Name);

    /// Cells of a kC type as the services table spells them. A struct flattens to its scalar members,
    /// in declaration order (that is what kC's Lowering.Syscall pushes).
    public static string[] Cells(string ctype) => ctype switch
    {
        "void" => Array.Empty<string>(),
        "uint32_t" => new[] { "u32" },
        "int32_t" => new[] { "i32" },
        "float" => new[] { "f32" },
        "vec3" => new[] { "f32", "f32", "f32" },
        "quat" => new[] { "f32", "f32", "f32", "f32" },
        _ => throw new InvalidOperationException($"catalog: no cell mapping for kC type '{ctype}'"),
    };

    public static string Snake(string pascal) => Regex.Replace(pascal, "(?<!^)([A-Z])", "_$1").ToLowerInvariant();

    // ------------------------------------------------------------------ generation

    public static JsonObject Row(ServiceDef s)
    {
        static JsonArray Arr(IEnumerable<string> xs) => new(xs.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray());
        string args = s.Params.Length == 0 ? "—"
            : string.Join(", ", s.Params.Select(p => Cells(p.CType).Length == 1
                ? $"`{Cells(p.CType)[0]} {p.Name}`"
                : $"`{p.CType} {p.Name}` ({Cells(p.CType)[0]} ×{Cells(p.CType).Length})"));
        return new JsonObject
        {
            ["id"] = (int)s.Id,               // int, not ushort: JsonValue<ushort> does not read back as int
            ["name"] = s.Name,
            // The text before '(' is the name kC binds; the names inside group onto arg cells (D-118 (4)).
            ["kc"] = $"`{s.Name}({string.Join(", ", s.Params.Select(p => p.Name))})`",
            ["kscript"] = "—",
            ["symbol"] = s.Symbol,
            ["kind"] = s.Kind,
            ["args"] = args,
            ["arg_types"] = Arr(s.ArgTypes),
            ["results"] = s.HasResult ? $"`{s.ResultTypes[0]} value`" : "—",
            ["result_types"] = Arr(s.ResultTypes),
            ["cost_class"] = s.Cost,
            ["requires_stock"] = null,
            ["arg_domains"] = Arr(s.ArgDomains),
            ["host_checks"] = s.HostChecks,
            ["summary"] = s.Summary,
        };
    }

    public static IEnumerable<JsonObject> ServiceRowsJson() => Services.Select(Row);

    public static IEnumerable<(string Key, JsonArray Values)> EnumsJson() =>
        Enums.Where(e => e.TableName != null)
             .Select(e => (e.TableName!, new JsonArray(e.Values.Select(v => (JsonNode?)JsonValue.Create(v)).ToArray())));

    /// The augmented services.json text: the stock file with the catalog rows appended.
    public static string AugmentServices(string stockJson)
    {
        var root = JsonNode.Parse(stockJson)!;
        var arr = root["services"]!.AsArray();
        var taken = arr.Select(r => (int)r!["id"]!).ToHashSet();
        foreach (var row in ServiceRowsJson())
        {
            if (taken.Contains((int)row["id"]!)) throw new InvalidOperationException($"services.json already has id {row["id"]}");
            arr.Add(row);
        }
        return root.ToJsonString();
    }

    /// The augmented enums.json text: the stock file with the catalog enumerations merged in.
    public static string AugmentEnums(string stockJson)
    {
        var root = JsonNode.Parse(stockJson)!;
        var obj = root["enums"]!.AsObject();
        foreach (var (key, values) in EnumsJson())
        {
            if (obj.ContainsKey(key)) throw new InvalidOperationException($"enums.json already has '{key}'");
            obj[key] = values;
        }
        return root.ToJsonString();
    }

    public static string Header()
    {
        var sb = new StringBuilder();
        sb.Append("#pragma once\n");
        sb.Append("/* <purrtty.h> - purrTTY vehicle-parts services for kFS (service ids 0x5000-0x50FF).\n");
        sb.Append("   Generated from the purrTTY service catalog; do not edit. Every function is a kFS\n");
        sb.Append("   service (SYSCALL). Setters take effect on a later tick: read a value back no\n");
        sb.Append("   earlier than the next tick. Indices are per vehicle and 0-based. */\n");
        sb.Append("#include <kfs.h>\n");
        foreach (var e in Enums)
        {
            sb.Append($"\n/* {(e.TableName ?? "constants")}: {e.Doc} */\n");
            foreach (var (macro, value) in e.Macros) sb.Append($"#define {macro} {value}\n");
        }
        foreach (var group in Services.GroupBy(s => s.Id >> 4))
        {
            sb.Append('\n');
            foreach (var s in group)
            {
                sb.Append($"/* {s.Summary}");
                foreach (var p in s.Params) sb.Append($"\n   {p.Name}: {p.Doc}.");
                if (s.HasResult) sb.Append($"\n   Returns {s.ResultDoc}.");
                if (s.HostChecks != "—") sb.Append($"\n   Rejected (program log, sys.rejected_requests) when: {s.HostChecks}.");
                sb.Append($"\n   Service 0x{s.Id:X4} {s.Symbol}, cost class {s.Cost}. */\n");
                sb.Append(s.Prototype).Append(";\n");
            }
        }
        return sb.ToString();
    }

    /// kASM cannot include a mod file with <...> (only kfs.kinc and vehicle.kinc exist), so the enum
    /// constants are offered as a .equ block a program can paste or include as a project file.
    public static string KasmEquates()
    {
        var sb = new StringBuilder("; purrtty.kinc - generated from the purrTTY service catalog\n");
        foreach (var e in Enums) foreach (var (m, v) in e.Macros) sb.Append($".equ {m}, {v}\n");
        return sb.ToString();
    }

    // ------------------------------------------------------------------ self-check

    static readonly Regex EnumDomainRx = new(@"^enum:(?<key>\w+)(\[(?<a>\d+)\.\.(?<b>\d+)\])?$");

    /// `enum:<key>` or `enum:<key>[a..b]` (GameTables.EnumDomain): the key must exist, a sub-range must
    /// lie inside the list, and (our own rule) must not cover a reserved filler entry.
    static bool EnumDomainOk(string d, HashSet<string> keys, IReadOnlySet<string>? stockKeys, List<string> problems, string who)
    {
        var m = EnumDomainRx.Match(d);
        if (!m.Success) return false;
        string key = m.Groups["key"].Value;
        if (stockKeys?.Contains(key) == true) return true;
        var def = Enums.FirstOrDefault(e => e.TableName == key);
        if (def == null) return false;
        if (!m.Groups["a"].Success) return true;
        int lo = int.Parse(m.Groups["a"].Value), hi = int.Parse(m.Groups["b"].Value);
        if (lo > hi || hi >= def.Values.Count) return false;
        if (def.Values.Skip(lo).Take(hi - lo + 1).Any(EnumDef.IsFiller)) problems.Add($"{who}: domain {d} admits reserved filler values");
        return true;
    }

    /// Independent re-derivation of the rules the game enforces, run against the *generated* text.
    /// Returns a list of problems (empty = consistent).
    public static List<string> SelfCheck(IReadOnlySet<string>? stockEnumKeys = null)
    {
        var problems = new List<string>();
        // Independent C-type cell sizes (not Cells()): this is what the compiler will push.
        var cTypeCells = new Dictionary<string, int> { ["uint32_t"] = 1, ["int32_t"] = 1, ["float"] = 1, ["vec3"] = 3, ["quat"] = 4, ["int64_t"] = 2, ["double"] = 2 };
        var rowCellSize = new Dictionary<string, int> { ["u32"] = 1, ["i32"] = 1, ["f32"] = 1, ["ptr"] = 1, ["cell"] = 1, ["i64"] = 2, ["f64"] = 2 };
        var protoRx = new Regex(@"^(?<ret>\w+)\s+(?<name>\w+)\((?<params>[^)]*)\);$", RegexOptions.Multiline);
        var protos = protoRx.Matches(Header()).ToDictionary(m => m.Groups["name"].Value);
        var enumKeys = Enums.Where(e => e.TableName != null).Select(e => e.TableName!).ToHashSet();

        if (Services.Select(s => s.Id).Distinct().Count() != Services.Length) problems.Add("duplicate service id");
        if (Services.Select(s => s.Name).Distinct().Count() != Services.Length) problems.Add("duplicate service name");
        var macros = Enums.SelectMany(e => e.Macros.Select(m => m.Macro)).ToList();
        foreach (var dup in macros.GroupBy(m => m).Where(g => g.Count() > 1)) problems.Add($"macro {dup.Key} defined twice");
        if (stockEnumKeys != null) foreach (var k in enumKeys.Where(stockEnumKeys.Contains)) problems.Add($"enum '{k}' collides with a stock enumeration");

        foreach (var s in Services)
        {
            string who = $"0x{s.Id:X4} {s.Name}";
            if (s.Id < FirstId || s.Id > LastId) problems.Add($"{who}: id outside the purrTTY block");
            if (!protos.TryGetValue(s.Name, out var proto)) { problems.Add($"{who}: no prototype in the header"); continue; }

            // 1. prototype cells == row cells (a mismatch is an InvalidOperationException in kC lowering).
            string ps = proto.Groups["params"].Value.Trim();
            int protoCells = ps == "void" ? 0 : ps.Split(',').Sum(p => cTypeCells[p.Trim().Split(' ')[0]]);
            int rowCells = s.ArgTypes.Sum(t => rowCellSize[t]);
            if (protoCells != rowCells) problems.Add($"{who}: prototype pushes {protoCells} cells, row has {rowCells}");
            // Same count is not enough: `float x` against a u32 cell compiles silently and sends float bits.
            var cTypeCellTypes = new Dictionary<string, string[]> { ["uint32_t"] = new[] { "u32" }, ["int32_t"] = new[] { "i32" }, ["float"] = new[] { "f32" }, ["vec3"] = new[] { "f32", "f32", "f32" }, ["quat"] = new[] { "f32", "f32", "f32", "f32" } };
            var protoTypes = ps == "void" ? Array.Empty<string>() : ps.Split(',').SelectMany(p => cTypeCellTypes.GetValueOrDefault(p.Trim().Split(' ')[0]) ?? new[] { "?" }).ToArray();
            if (!protoTypes.SequenceEqual(s.ArgTypes)) problems.Add($"{who}: prototype cell types [{string.Join(",", protoTypes)}] differ from the row's [{string.Join(",", s.ArgTypes)}]");

            // 2. results: at most one cell, and the prototype's return agrees with the row.
            string ret = proto.Groups["ret"].Value;
            int retCells = ret == "void" ? 0 : cTypeCells[ret];
            if (s.ResultTypes.Length > 1 || retCells > 1) problems.Add($"{who}: multi-cell result (crashes kC lowering)");
            if (retCells != s.ResultTypes.Length) problems.Add($"{who}: prototype returns {retCells} cells, row {s.ResultTypes.Length}");
            else if (ret != "void" && !(cTypeCellTypes.GetValueOrDefault(ret) ?? Array.Empty<string>()).SequenceEqual(s.ResultTypes))
                problems.Add($"{who}: prototype return type '{ret}' differs from the row's result type");
            if (s.HasResult != (s.Kind == "request+result")) problems.Add($"{who}: kind/result mismatch");

            // 3. domains: one per cell, each fitting its type (GameTables.CheckDomains).
            if (s.ArgDomains.Length != s.ArgTypes.Length) problems.Add($"{who}: domains/types count differ");
            for (int i = 0; i < s.ArgTypes.Length; i++)
            {
                string d = s.ArgDomains[i], t = s.ArgTypes[i];
                bool fits = d switch
                {
                    "any" => true,
                    "finite" or "finite:>=0" => t == "f32",
                    _ when d.StartsWith("enum:") => t == "u32" && EnumDomainOk(d, enumKeys, stockEnumKeys, problems, who),
                    _ => false,
                };
                if (!fits) problems.Add($"{who}: argument cell {i} domain '{d}' doesn't fit '{t}' or names an unknown enum");
                if (t == "f32" && d == "any") problems.Add($"{who}: float cell {i} has no finite domain (NaN could reach the game)");
            }

            // 4. kc-name grouping, the loader's algorithm (GameTables.Arguments) verbatim.
            int names = s.Params.Length, count = s.ArgTypes.Length;
            if (names != count && names != 0)
            {
                int group = 0, merges = count - names;
                for (int i = 1; i < count; i++)
                {
                    if (merges > 0 && s.ArgDomains[i] == s.ArgDomains[i - 1]) merges--; else group++;
                }
                if (group + 1 != names || merges != 0) problems.Add($"{who}: kc names don't group onto the arg cells (table load would fail)");
            }
            if (string.IsNullOrWhiteSpace(s.Summary)) problems.Add($"{who}: empty summary");
        }
        return problems;
    }
}
