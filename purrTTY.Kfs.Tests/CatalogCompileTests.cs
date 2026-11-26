using System.Text;
using KSA.Kfs.Host;
using KSA.Kfs.Host.Workbench;
using KSA.Kfs.KC;
using KSA.Kfs.Vm.Tables;
using NUnit.Framework;
using purrTTY.Kfs.Catalog;

namespace purrTTY.Kfs.Tests;

/// <summary>
/// The augmented tables and <c>&lt;purrtty.h&gt;</c> through the game's real kC compiler and kASM
/// assembler: stock programs are unaffected, the header costs nothing, and every prototype binds
/// to its row. Tests that need the header install it with <see cref="SystemHeaderScope"/>, which
/// restores the game's process-wide header statics afterwards.
/// </summary>
[TestFixture]
public sealed class CatalogCompileTests : GameFixture
{
    // Stock examples this harness cannot build with STOCK tables, and why. Each is excluded from
    // the comparison (not silently skipped): the test asserts that exactly these fail with stock
    // tables. Empty: every stock example builds with the layouts below.
    private static readonly Dictionary<string, string> UnbuildableExamples = new(StringComparer.Ordinal);

    [TearDown]
    public void HeaderStaticsAreClean()
    {
        Assert.That(SystemHeaderScope.IsClean(), Is.True, "a test left <purrtty.h> or its builtins in SystemHeaders");
    }

    [Test]
    public void Stock_Examples_Compile_Identically()
    {
        GameTables stock = KfsTables.Stock();
        GameTables aug = KfsTables.Augmented();

        var projects = Examples.Files
            .GroupBy(f => Workspace.FolderOf(f.Key), StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .ToList();
        Assert.That(projects, Has.Count.GreaterThan(0));

        var built = new List<string>();
        var failures = new List<string>();
        var unbuildable = new List<string>();

        foreach (var project in projects)
        {
            // A workspace folder is one project; files are keyed by their name inside it.
            var files = project.ToDictionary(f => f.Key[(project.Key.Length + 1)..], f => f.Value, StringComparer.Ordinal);
            int layoutsBuilt = 0;
            foreach (string layoutName in new[] { "vehicle", "bench" })
            {
                string? outcome = CompareExample(stock, aug, layoutName, files, out bool builtWithStock);
                if (!builtWithStock) continue;
                layoutsBuilt++;
                if (outcome is null) built.Add($"{project.Key} ({layoutName})");
                else failures.Add($"{project.Key} ({layoutName}): {outcome}");
            }
            if (layoutsBuilt == 0) unbuildable.Add(project.Key);
        }

        Assert.That(failures, Is.Empty, () => "examples that differ with augmented tables:\n" + string.Join("\n", failures));
        Assert.That(unbuildable, Is.EquivalentTo(UnbuildableExamples.Keys),
            "stock examples this harness cannot build with stock tables must be exactly the documented exclusions");
        Assert.That(built.Select(b => b.Split(' ')[0]).Distinct().Count(), Is.EqualTo(projects.Count - UnbuildableExamples.Count),
            "every other example was compared");
    }

    /// <summary>
    /// Builds one example project with stock tables and, with the header installed as in the game,
    /// with augmented tables. Null when both built to identical bytes; otherwise what differed.
    /// </summary>
    private static string? CompareExample(GameTables stock, GameTables aug, string layoutName,
        Dictionary<string, string> files, out bool builtWithStock)
    {
        byte[]? stockImage = Build(stock, Layout(stock, layoutName), files, out string stockDiagnostics);
        builtWithStock = stockImage is not null;
        if (stockImage is null) return "stock build failed:\n" + stockDiagnostics;

        byte[]? augImage;
        string augDiagnostics;
        using (SystemHeaderScope.Install())
        {
            augImage = Build(aug, Layout(aug, layoutName), files, out augDiagnostics);
        }
        if (augImage is null) return "augmented build failed:\n" + augDiagnostics;
        if (augDiagnostics != stockDiagnostics) return $"diagnostics differ:\nstock:\n{stockDiagnostics}\naugmented:\n{augDiagnostics}";
        if (!augImage.AsSpan().SequenceEqual(stockImage)) return $"images differ ({stockImage.Length} B stock, {augImage.Length} B augmented)";
        return null;
    }

    private static IoLayout Layout(GameTables tables, string name) =>
        name == "bench" ? tables.BenchLayout : LayoutGenerator.Vehicle("t", 4, 1, 1);

    /// <summary>Builds a project as the game's Workbench does: kC when it has a .c file, else kASM.</summary>
    private static byte[]? Build(GameTables tables, IoLayout layout, Dictionary<string, string> files, out string diagnostics)
    {
        if (files.Keys.Any(f => f.EndsWith(".c", StringComparison.OrdinalIgnoreCase)))
        {
            var r = KfsTables.CompileKc(tables, layout, files);
            diagnostics = KfsTables.Text(r.Diagnostics);
            return r.Ok ? r.Image : null;
        }
        var a = KfsTables.Assemble(tables, layout, files);
        diagnostics = KfsTables.Text(a.Diagnostics);
        return a.Ok ? a.Image : null;
    }

    [Test]
    public void Header_Compiles_Alone()
    {
        GameTables aug = KfsTables.Augmented();
        IoLayout layout = LayoutGenerator.Vehicle("t", 4, 1, 1);
        const string task = """
            #pragma kfs tick_hz 10
            #pragma kfs task t rate_hz=10
            uint32_t n;
            void t(void) { n = n + 1u; }

            """;
        const string without = "#include <kfs.h>\n" + task;
        const string with = "#include <kfs.h>\n#include <purrtty.h>\n" + task;

        using (SystemHeaderScope.Install())
        {
            var plain = KfsTables.CompileKc(aug, layout, Single(without));
            var included = KfsTables.CompileKc(aug, layout, Single(with));
            Assert.That(plain.Ok, Is.True, () => KfsTables.Text(plain.Diagnostics));
            Assert.That(included.Ok, Is.True, () => "with <purrtty.h>:\n" + KfsTables.Text(included.Diagnostics));
            Assert.That(included.Diagnostics, Is.Empty, () => "including the header must raise nothing:\n" + KfsTables.Text(included.Diagnostics));
            Assert.That(included.Report!.Program.Code.Total, Is.EqualTo(plain.Report!.Program.Code.Total), "including the header adds no code");

            // Without debug information (which records the extra file) the images are identical.
            var plainNoDebug = KfsTables.CompileKc(aug, layout, Single(without), emitDebug: false);
            var includedNoDebug = KfsTables.CompileKc(aug, layout, Single(with), emitDebug: false);
            Assert.That(includedNoDebug.Image, Is.EqualTo(plainNoDebug.Image), "images without debug info");
        }
    }

    [Test]
    public void Header_Is_Unknown_Without_Registration()
    {
        // The header lives only in SystemHeaders: no registration, no header (A3 installs it).
        GameTables aug = KfsTables.Augmented();
        var r = KfsTables.CompileKc(aug, LayoutGenerator.Vehicle("t", 4, 1, 1),
            Single("#include <kfs.h>\n#include <purrtty.h>\n#pragma kfs tick_hz 10\n#pragma kfs task t rate_hz=10\nvoid t(void) { }\n"));
        Assert.That(r.Ok, Is.False);
        Assert.That(KfsTables.Text(r.Diagnostics), Does.Contain("purrtty.h"));
    }

    [Test]
    public void Every_Prototype_Compiles()
    {
        GameTables aug = KfsTables.Augmented();
        string program = EveryServiceProgram();

        using (SystemHeaderScope.Install())
        {
            var r = KfsTables.CompileKc(aug, LayoutGenerator.Vehicle("t", 4, 1, 1), Single(program));
            Assert.That(r.Ok, Is.True, () => "compile failed:\n" + KfsTables.Text(r.Diagnostics) + "\n\nprogram:\n" + program);
            Assert.That(KfsTables.HasErrors(r.Diagnostics), Is.False);
            Assert.That(r.Diagnostics, Is.Empty, () => KfsTables.Text(r.Diagnostics));
        }
    }

    [Test]
    public void Kinc_Assembles_With_Augmented_Tables()
    {
        GameTables aug = KfsTables.Augmented();
        const string program = """
            .include <kfs.kinc>
            .include "purrtty.kinc"
            .tick_hz 10
            .task main rate_hz=10
            .func main
                PUSH8  0
                PUSH8  1
                SYSCALL SVC_X_LIGHT_SET_ON
                PUSH8  X_KIND_ALL
                SYSCALL SVC_X_PARTS_DESCRIBE
                RET    0
            .endfunc

            """;
        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["lights.kasm"] = program,
            [XCatalog.KincName] = CatalogEmitter.Kinc(XCatalog.Catalog),
        };

        var r = KfsTables.Assemble(aug, LayoutGenerator.Vehicle("t", 4, 1, 1), files);
        Assert.That(r.Ok, Is.True, () => KfsTables.Text(r.Diagnostics));

        // With stock tables the service symbols do not exist.
        var stock = KfsTables.Assemble(KfsTables.Stock(), LayoutGenerator.Vehicle("t", 4, 1, 1), files);
        Assert.That(stock.Ok, Is.False);
        Assert.That(KfsTables.Text(stock.Diagnostics), Does.Contain("SVC_X_LIGHT_SET_ON"));
    }

    /// <summary>A kC program that calls every catalogue service once, with valid arguments.</summary>
    private static string EveryServiceProgram()
    {
        var sb = new StringBuilder();
        sb.Append("#include <kfs.h>\n#include <purrtty.h>\n#pragma kfs tick_hz 10\n#pragma kfs task run rate_hz=10\n");
        sb.Append("uint32_t u;\nfloat f;\nvec3 c;\n\nvoid run(void)\n{\n");
        foreach (var s in XCatalog.Services)
        {
            string call = $"{s.Name}({string.Join(", ", s.Params.Select(Argument))})";
            sb.Append(s.ResultCType switch
            {
                "void" => $"    {call};\n",
                "float" => $"    f = f + {call};\n",
                _ => $"    u = u + {call};\n",
            });
        }
        sb.Append("}\n");
        return sb.ToString();

        static string Argument(ParamDef p)
        {
            if (p.Selects is { Count: > 0 } selects)
            {
                var e = XCatalog.Catalog.EnumByKey(p.Domain["enum:".Length..].Split('[')[0])!;
                return e.Constants.Single(c => c.Value == selects[^1]).Name;
            }
            return p.CType switch
            {
                "float" => "0.5f",
                "vec3" => "c",
                _ => "0u",
            };
        }
    }

    private static Dictionary<string, string> Single(string source) =>
        new(StringComparer.Ordinal) { ["main.c"] = source };
}
