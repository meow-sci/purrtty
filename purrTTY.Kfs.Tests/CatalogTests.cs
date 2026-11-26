using System.Reflection;
using NUnit.Framework;
using purrTTY.Kfs.Catalog;

namespace purrTTY.Kfs.Tests;

/// <summary>
/// The catalogue, its emitter and its self-check as pure code: no game assemblies needed. The
/// negative cases each break one definition of a copy of the real catalogue and assert that the
/// self-check reports exactly that rule.
/// </summary>
[TestFixture]
public sealed class CatalogTests
{
    private static ServiceCatalog Real => XCatalog.Catalog;

    [Test]
    public void Catalog_SelfCheck_Passes()
    {
        var problems = CatalogSelfCheck.Run();
        Assert.That(problems, Is.Empty, () => "self-check problems:\n" + string.Join("\n", problems));
    }

    [Test]
    public void Catalog_Has_The_Planned_Surface()
    {
        Assert.That(XCatalog.Services, Has.Count.EqualTo(22));
        Assert.That(XCatalog.Services.Select(s => s.Id), Is.Ordered.And.Unique);
        Assert.That(XCatalog.Services.Min(s => s.Id), Is.EqualTo(0x5000));
        Assert.That(XCatalog.Services.Max(s => s.Id), Is.EqualTo(0x5033));
        Assert.That(XCatalog.Services.Count(s => s.HasResult), Is.EqualTo(10), "count + getters");
        Assert.That(XCatalog.Services.All(s => s.Name.StartsWith("x_", StringComparison.Ordinal)), Is.True);

        Assert.That(CatalogEmitter.EnumEntries(Real).Select(e => e.Key),
            Is.EqualTo(new[] { "x_parts_info", "x_part_kind", "x_light_prop", "x_deploy_prop", "x_actuator_prop" }));
        Assert.That(XCatalog.LightPropEnum.Values, Has.Count.EqualTo(24));
        Assert.That(XCatalog.DeployPropEnum.Values, Has.Count.EqualTo(19));
        Assert.That(XCatalog.ActuatorPropEnum.Values, Has.Count.EqualTo(19));

        var domains = XCatalog.Services.Where(s => s.Name.EndsWith("_geti", StringComparison.Ordinal) || s.Name.EndsWith("_getf", StringComparison.Ordinal))
            .ToDictionary(s => s.Name, s => s.Params[1].Domain);
        Assert.That(domains, Is.EquivalentTo(new Dictionary<string, string>
        {
            ["x_light_geti"] = "enum:x_light_prop[0..7]",
            ["x_light_getf"] = "enum:x_light_prop[16..23]",
            ["x_deploy_geti"] = "enum:x_deploy_prop[0..5]",
            ["x_deploy_getf"] = "enum:x_deploy_prop[16..18]",
            ["x_actuator_geti"] = "enum:x_actuator_prop[0..3]",
            ["x_actuator_getf"] = "enum:x_actuator_prop[16..18]",
        }));
        Assert.That(Real.ByName("x_parts_info")!.Params[0].Domain, Is.EqualTo("enum:x_parts_info"));
        Assert.That(Real.ByName("x_parts_describe")!.Params[0].Domain, Is.EqualTo("enum:x_part_kind"));
    }

    [Test]
    public void Svc_Constants_Match_The_Catalogue()
    {
        var constants = typeof(Svc).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral)
            .ToDictionary(f => f.Name, f => (ushort)f.GetRawConstantValue()!);

        Assert.That(constants.Values, Is.EquivalentTo(XCatalog.Services.Select(s => s.Id)));
        foreach (var s in XCatalog.Services)
        {
            // x_light_set_on → LightSetOn
            string expected = string.Concat(s.Name["x_".Length..].Split('_').Select(w => char.ToUpperInvariant(w[0]) + w[1..]));
            Assert.That(constants.TryGetValue(expected, out var id) && id == s.Id, Is.True, $"Svc.{expected} must be 0x{s.Id:X4} ({s.Name})");
        }
    }

    [Test]
    public void Enum_Positions_Are_The_CSharp_Values()
    {
        AssertPositions<PartsInfo>(XCatalog.PartsInfoEnum);
        AssertPositions<PartKind>(XCatalog.PartKindEnum);
        AssertPositions<LightProp>(XCatalog.LightPropEnum);
        AssertPositions<DeployProp>(XCatalog.DeployPropEnum);
        AssertPositions<ActuatorProp>(XCatalog.ActuatorPropEnum);
        AssertPositions<DeployState>(XCatalog.DeployStateEnum);
        AssertPositions<LightType>(XCatalog.LightTypeEnum);

        Assert.That(XCatalog.LightPropEnum.Values[8], Is.EqualTo("reserved8"));
        Assert.That(XCatalog.LightPropEnum.Values[15], Is.EqualTo("reserved15"));
        Assert.That(XCatalog.LightPropEnum.Values[16], Is.EqualTo("intensity"));
        Assert.That(XCatalog.LightPropEnum.Constants, Does.Contain(("X_LIGHT_HAS_SWITCH", 1)).And.Contain(("X_LIGHT_RANGE_M", 17)));
        Assert.That(XCatalog.DeployStateEnum.InTable, Is.False);
        Assert.That(XCatalog.LightTypeEnum.Constants, Is.EqualTo(new[] { ("X_LIGHT_TYPE_SPOT", 0), ("X_LIGHT_TYPE_POINT", 1) }));
    }

    private static void AssertPositions<T>(EnumDef def) where T : struct, Enum
    {
        foreach (T member in Enum.GetValues<T>())
        {
            int value = Convert.ToInt32(member);
            Assert.That(def.Values[value], Is.EqualTo(EnumDef.Snake(member.ToString())), $"{typeof(T).Name}.{member}");
            Assert.That(def.Constants, Does.Contain((def.CPrefix + EnumDef.Snake(member.ToString()).ToUpperInvariant(), value)));
        }
        Assert.That(def.Values.Count(v => !EnumDef.IsFiller(v)), Is.EqualTo(Enum.GetValues<T>().Length), typeof(T).Name);
    }

    [Test]
    public void Header_Has_No_Function_Body_And_No_Filler()
    {
        string header = CatalogEmitter.Header(Real);

        Assert.That(header, Does.StartWith("#pragma once\r\n"));
        Assert.That(header, Does.Contain("\r\n#include <kfs.h>\r\n"));
        Assert.That(header.Replace("\r\n", "", StringComparison.Ordinal), Does.Not.Contain("\n").And.Not.Contain("\r"), "only \\r\\n line ends");
        Assert.That(header, Does.Not.Contain("{").And.Not.Contain("}"), "no function bodies");
        Assert.That(header, Does.Not.Contain("reserved").IgnoreCase, "no filler names");
        Assert.That(header.All(c => c is '\r' or '\n' || (c >= 0x20 && c <= 0x7E)), Is.True, "printable ASCII");

        foreach (var s in XCatalog.Services) Assert.That(header, Does.Contain("\r\n" + s.Prototype + ";\r\n"), s.Name);
        foreach (var (name, value) in XCatalog.Enums.SelectMany(e => e.Constants))
            Assert.That(header, Does.Contain($"\r\n#define {name} {value}\r\n"), name);
        Assert.That(header.Split("#define ").Length - 1, Is.EqualTo(XCatalog.Enums.Sum(e => e.Constants.Count())), "one #define per real value");
    }

    [Test]
    public void Kinc_And_Reference_Cover_Every_Constant_And_Service()
    {
        string kinc = CatalogEmitter.Kinc(Real);
        string md = CatalogEmitter.MarkdownReference(Real);

        Assert.That(kinc, Does.Not.Contain("reserved").IgnoreCase);
        foreach (var (name, value) in XCatalog.Enums.SelectMany(e => e.Constants))
        {
            Assert.That(kinc, Does.Contain($"\r\n.equ {name}, {value}\r\n"), name);
            Assert.That(md, Does.Contain($"| `{name}` | {value} |"), name);
        }
        foreach (var s in XCatalog.Services) Assert.That(md, Does.Contain($"`{s.Prototype}`"), s.Name);
        Assert.That(CatalogEmitter.BuiltinNames(Real), Is.EqualTo(XCatalog.Services.Select(s => s.Name)));
    }

    /// <summary>
    /// The emitter's exact bytes for a one-service, one-enumeration catalogue, written out by hand
    /// here (replaces a test that compared the emitter's output with itself): line ends, comment
    /// layout, constant and prototype lines, JSON key order, indentation and escaping. The full
    /// catalogue's bytes are pinned by the snapshots.
    /// </summary>
    [Test]
    public void Emitter_Writes_The_Documented_Bytes()
    {
        var tiny = new ServiceCatalog(
            [
                new ServiceDef(0x5000, "x_probe", ServiceKinds.RequestResult,
                    [new ParamDef("what", "uint32_t", ["u32"], "enum:x_probe_sel", "a selector", [0, 2])],
                    "uint32_t", ["u32"], CostClasses.Light, "Reads a probe.", "—", "Longer doc.", "the value"),
            ],
            [new EnumDef("x_probe_sel", "X_PROBE_", ["alpha", "reserved1", "gamma"], "probe selectors")]);

        string header = CatalogEmitter.Header(tiny);
        Assert.That(header, Does.StartWith("#pragma once\r\n/* <purrtty.h> - purrTTY vehicle-parts services for kFS.\r\n"));
        Assert.That(header[header.IndexOf("#include <kfs.h>", StringComparison.Ordinal)..], Is.EqualTo(
            "#include <kfs.h>\r\n" +
            "\r\n" +
            "/* x_probe_sel: probe selectors */\r\n" +
            "#define X_PROBE_ALPHA 0\r\n" +
            "#define X_PROBE_GAMMA 2\r\n" +
            "\r\n" +
            "/* x_probe - Reads a probe.\r\n" +
            "   what: a selector.\r\n" +
            "   Returns the value.\r\n" +
            "   Longer doc.\r\n" +
            "   Service 0x5000 SVC_X_PROBE, cost class Light. */\r\n" +
            "uint32_t x_probe(uint32_t what);\r\n"));

        Assert.That(CatalogEmitter.Kinc(tiny), Is.EqualTo(
            "; purrtty.kinc - purrTTY kFS constants for kASM. Generated from purrTTY's service\r\n" +
            "; catalogue; do not edit. The SVC_X_* service symbols come from <kfs.kinc>.\r\n" +
            "\r\n" +
            "; x_probe_sel: probe selectors\r\n" +
            ".equ X_PROBE_ALPHA, 0\r\n" +
            ".equ X_PROBE_GAMMA, 2\r\n"));

        Assert.That(CatalogEmitter.ServiceRowsJson(tiny), Is.EqualTo("""
            [
              {
                "id": 20480,
                "name": "x_probe",
                "kc": "`x_probe(what)`",
                "kscript": "—",
                "symbol": "SVC_X_PROBE",
                "kind": "request+result",
                "args": "`u32 what`",
                "arg_types": [
                  "u32"
                ],
                "results": "`u32 value`",
                "result_types": [
                  "u32"
                ],
                "cost_class": "Light",
                "requires_stock": null,
                "arg_domains": [
                  "enum:x_probe_sel"
                ],
                "host_checks": "—",
                "summary": "Reads a probe."
              }
            ]
            """.Replace("\r\n", "\n", StringComparison.Ordinal)), "plain integer id, the loader's key order, \\n line ends, non-ASCII as is");

        Assert.That(CatalogEmitter.EnumsJson(tiny), Is.EqualTo("""
            {
              "x_probe_sel": [
                "alpha",
                "reserved1",
                "gamma"
              ]
            }
            """.Replace("\r\n", "\n", StringComparison.Ordinal)), "fillers kept in the table, so a position is the value");
    }

    // ------------------------------------------------------------------ negative cases

    private static ServiceCatalog Change(ushort id, Func<ServiceDef, ServiceDef> change) => Real.WithService(id, change);

    private static ServiceDef ChangeParam(ServiceDef s, int index, Func<ParamDef, ParamDef> change) =>
        s with { Params = s.Params.Select((p, i) => i == index ? change(p) : p).ToArray() };

    private static ServiceCatalog ChangeEnum(EnumDef def, Func<EnumDef, EnumDef> change) =>
        Real with { Enums = Real.Enums.Select(e => ReferenceEquals(e, def) ? change(e) : e).ToArray() };

    private static TestCaseData Case(string name, CatalogRule rule, Func<ServiceCatalog> broken,
        ushort[]? stockIds = null, string[]? stockEnumKeys = null) =>
        new TestCaseData(broken, rule, stockIds, stockEnumKeys).SetName("SelfCheck_Reports_" + name);

    private static IEnumerable<TestCaseData> BrokenCatalogues()
    {
        // Rule 1: ids.
        yield return Case("Id_Outside_Block", CatalogRule.IdOutsideBlock, () => Change(Svc.LightCount, s => s with { Id = 0x4FFF }));
        yield return Case("Id_Above_Block", CatalogRule.IdOutsideBlock, () => Change(Svc.LightCount, s => s with { Id = 0x5100 }));
        yield return Case("Id_Duplicate", CatalogRule.IdDuplicate, () => Change(Svc.LightGeti, s => s with { Id = Svc.LightCount }));
        yield return Case("Id_In_Stock_Table", CatalogRule.IdInStockTable, () => Real, stockIds: [1, 2, Svc.PartsInfo]);

        // Names.
        yield return Case("Name_Without_Prefix", CatalogRule.Name, () => Change(Svc.LightCount, s => s with { Name = "purr_light_count" }));
        yield return Case("Name_Not_An_Identifier", CatalogRule.Name, () => Change(Svc.LightCount, s => s with { Name = "x_light-count" }));
        yield return Case("Name_Duplicate", CatalogRule.Name, () => Change(Svc.LightCount, s => s with { Name = "x_deploy_count" }));
        yield return Case("Param_Name_Is_A_Keyword", CatalogRule.Name, () => Change(Svc.LightSetOn, s => ChangeParam(s, 1, p => p with { Name = "float" })));

        // Kind.
        yield return Case("Kind_Invalid", CatalogRule.Kind, () => Change(Svc.LightCount, s => s with { Kind = "query" }));
        yield return Case("Kind_Disagrees_With_Result", CatalogRule.Kind, () => Change(Svc.LightSetOn, s => s with { Kind = ServiceKinds.RequestResult }));

        // Rule 2: parameter cells, types and counts.
        yield return Case("Float_Param_On_U32_Cell", CatalogRule.ParamCells,
            () => Change(Svc.LightSetIntensity, s => ChangeParam(s, 1, p => p with { CellTypes = ["u32"], Domain = "any" })));
        yield return Case("Vec3_Param_On_One_Cell", CatalogRule.ParamCells,
            () => Change(Svc.LightSetColor, s => ChangeParam(s, 1, p => p with { CellTypes = ["f32"] })));
        yield return Case("Unknown_C_Type", CatalogRule.ParamCells,
            () => Change(Svc.LightSetOn, s => ChangeParam(s, 1, p => p with { CType = "uint8_t" })));

        // Rule 3: results.
        yield return Case("Multi_Cell_Result", CatalogRule.Result,
            () => Change(Svc.LightCount, s => s with { ResultCType = "vec3", ResultCells = ["f32", "f32", "f32"] }));
        yield return Case("Void_Prototype_With_Result_Cell", CatalogRule.Result,
            () => Change(Svc.LightSetOn, s => s with { Kind = ServiceKinds.RequestResult, ResultCells = ["u32"], ResultDoc = "x" }));
        yield return Case("Value_Prototype_Without_Result_Cell", CatalogRule.Result,
            () => Change(Svc.LightCount, s => s with { Kind = ServiceKinds.Request, ResultCells = [] }));
        yield return Case("Float_Result_On_U32_Cell", CatalogRule.Result,
            () => Change(Svc.LightGetf, s => s with { ResultCells = ["u32"] }));

        // Rule 4: kc names group onto cells. Two adjacent parameters with the same domain, fewer
        // names than cells: the loader merges the first cells into the first name.
        yield return Case("Kc_Names_Misgroup", CatalogRule.Grouping,
            () => Change(Svc.LightSetColor, s => ChangeParam(s, 0, p => p with { CType = "float", CellTypes = ["f32"], Domain = "finite:>=0" })));

        // Domains.
        yield return Case("Float_With_Domain_Any", CatalogRule.Domain,
            () => Change(Svc.LightSetIntensity, s => ChangeParam(s, 1, p => p with { Domain = "any" })));
        yield return Case("Finite_On_U32", CatalogRule.Domain,
            () => Change(Svc.LightSetOn, s => ChangeParam(s, 1, p => p with { Domain = "finite" })));
        yield return Case("Domain_Not_Used_By_Purrtty", CatalogRule.Domain,
            () => Change(Svc.LightSetOn, s => ChangeParam(s, 1, p => p with { Domain = "count" })));

        // Rule 5: enum domains name a defined enumeration and admit exactly the intended values.
        yield return Case("Enum_Domain_Unknown_Key", CatalogRule.EnumDomain,
            () => Change(Svc.LightGeti, s => ChangeParam(s, 1, p => p with { Domain = "enum:x_nope[0..7]" })));
        yield return Case("Enum_Domain_Admits_Filler", CatalogRule.EnumDomain,
            () => Change(Svc.LightGeti, s => ChangeParam(s, 1, p => p with { Domain = "enum:x_light_prop[0..8]" })));
        yield return Case("Enum_Domain_Misses_A_Value", CatalogRule.EnumDomain,
            () => Change(Svc.LightGeti, s => ChangeParam(s, 1, p => p with { Domain = "enum:x_light_prop[0..6]" })));
        yield return Case("Enum_Domain_Outside_List", CatalogRule.EnumDomain,
            () => Change(Svc.LightGeti, s => ChangeParam(s, 1, p => p with { Domain = "enum:x_light_prop[0..99]" })));
        yield return Case("Integer_Getter_Selects_Float_Properties", CatalogRule.EnumDomain,
            () => Change(Svc.LightGeti, s => ChangeParam(s, 1, p => p with { Domain = "enum:x_light_prop[16..23]", Selects = [16, 17, 18, 19, 20, 21, 22, 23] })));
        yield return Case("Enum_Domain_Without_Intended_Values", CatalogRule.EnumDomain,
            () => Change(Svc.PartsInfo, s => ChangeParam(s, 0, p => p with { Selects = null })));

        // Rule 6: cost classes.
        yield return Case("Cost_Class_Unknown", CatalogRule.CostClass, () => Change(Svc.LightCount, s => s with { CostClass = "Cheap" }));

        // Enumeration definitions.
        yield return Case("Filler_Misnamed", CatalogRule.Enum,
            () => ChangeEnum(XCatalog.LightPropEnum, e => e with { Values = e.Values.Select((v, i) => i == 8 ? "reserved9" : v).ToArray() }));
        yield return Case("Constant_Defined_Twice", CatalogRule.Enum,
            () => Real with { Enums = [.. Real.Enums, XCatalog.LightTypeEnum] });
        yield return Case("Enum_Key_Collides_With_Stock", CatalogRule.Enum, () => Real, stockEnumKeys: ["direction", "x_light_prop"]);
        yield return Case("Enum_Key_Without_Prefix", CatalogRule.Name,
            () => ChangeEnum(XCatalog.PartKindEnum, e => e with { Key = "part_kind" }));

        // Text that would break the header or the row.
        yield return Case("Blank_Summary", CatalogRule.Text, () => Change(Svc.LightCount, s => s with { Summary = " " }));
        yield return Case("Comment_End_In_Doc", CatalogRule.Text, () => Change(Svc.LightCount, s => s with { Doc = "ends */ here" }));
        yield return Case("Filler_Word_In_Doc", CatalogRule.Text, () => Change(Svc.LightCount, s => s with { Doc = "reserved for later" }));
        yield return Case("Non_Ascii_Summary", CatalogRule.Text, () => Change(Svc.LightCount, s => s with { Summary = "Number of lights — all of them." }));

        // Malformed beyond checking: reported, never thrown.
        yield return Case("Null_Params", CatalogRule.Malformed, () => Change(Svc.LightCount, s => s with { Params = null! }));
    }

    [TestCaseSource(nameof(BrokenCatalogues))]
    public void SelfCheck_Reports(Func<ServiceCatalog> broken, CatalogRule rule, ushort[]? stockIds, string[]? stockEnumKeys)
    {
        IReadOnlyList<CatalogProblem> problems = Array.Empty<CatalogProblem>();
        Assert.DoesNotThrow(() => problems = CatalogSelfCheck.Run(broken(), stockIds, stockEnumKeys));
        Assert.That(problems.Select(p => p.Rule), Does.Contain(rule), () => "problems:\n" + string.Join("\n", problems));
    }

    // Header parse-back, on deliberately tampered header text.

    private static IEnumerable<TestCaseData> TamperedHeaders()
    {
        static TestCaseData H(string name, Func<string, string> tamper) =>
            new TestCaseData(tamper).SetName("SelfCheck_Header_Reports_" + name);

        yield return H("Changed_Return_Type", h => h.Replace("\r\nfloat x_light_getf(", "\r\nuint32_t x_light_getf(", StringComparison.Ordinal));
        yield return H("Changed_Param_Type", h => h.Replace("x_light_set_intensity(uint32_t light, float intensity)", "x_light_set_intensity(uint32_t light, uint32_t intensity)", StringComparison.Ordinal));
        yield return H("Missing_Prototype", h => h.Replace("void x_parts_rescan(void);\r\n", "", StringComparison.Ordinal));
        yield return H("Extra_Prototype", h => h + "void x_extra(void);\r\n");
        yield return H("Function_Body", h => h + "static uint32_t x_helper(void) { return 0u; }\r\n");
        yield return H("Missing_Define", h => h.Replace("#define X_LIGHT_ON 2\r\n", "", StringComparison.Ordinal));
        yield return H("Wrong_Define_Value", h => h.Replace("#define X_LIGHT_ON 2\r\n", "#define X_LIGHT_ON 3\r\n", StringComparison.Ordinal));
        yield return H("Filler_Define", h => h + "#define X_LIGHT_RESERVED8 8\r\n");
        yield return H("Lf_Line_Ends", h => h.Replace("\r\n", "\n", StringComparison.Ordinal));
        yield return H("No_Kfs_Include", h => h.Replace("#include <kfs.h>\r\n", "", StringComparison.Ordinal));
    }

    [TestCaseSource(nameof(TamperedHeaders))]
    public void SelfCheck_Header_Reports(Func<string, string> tamper)
    {
        string header = CatalogEmitter.Header(Real);
        string tampered = tamper(header);
        Assert.That(tampered, Is.Not.EqualTo(header), "the tamper must change the header");

        Assert.That(CatalogSelfCheck.CheckHeader(Real, header), Is.Empty);
        var problems = CatalogSelfCheck.CheckHeader(Real, tampered);
        Assert.That(problems.Select(p => p.Rule), Has.Some.EqualTo(CatalogRule.Header), () => "problems:\n" + string.Join("\n", problems));
    }
}
