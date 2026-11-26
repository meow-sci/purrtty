using KSA.Kfs.Host;
using KSA.Kfs.Host.Workbench;
using KSA.Kfs.Vm.Tables;
using NUnit.Framework;
using purrTTY.Kfs.Catalog;
using purrTTY.Kfs.Runtime;
using purrTTY.Kfs.Testing;

namespace purrTTY.Kfs.Tools.Tests.Runtime;

/// <summary>
/// <see cref="ProjectResolver"/> and <see cref="ProjectBuilder"/> against the game itself: on a real
/// temporary workspace, with no overlay, the resolver yields the same projects as the game's
/// <c>Workspace.ProjectOf</c>, and the builder the same images and diagnostics as the game's
/// <c>Workbench.BuildText</c> — the stock examples and purrTTY programs (kC with <c>&lt;purrtty.h&gt;</c>,
/// pure kASM with <c>.include "purrtty.kinc"</c>).
/// </summary>
[TestFixture]
public sealed class ProjectParityTests : ToolsRuntimeFixture
{
    /// <summary>Every shape of ProjectResolverTests, on disk.</summary>
    private static void WriteShapes(TempWorkspace ws)
    {
        ws.Write("proj/main.c", "#include <kfs.h>\n#include \"util.h\"\n")
            .Write("proj/util.h", "/* util */\n").Write("proj/b.kasm", "; b\n").Write("proj/c.kinc", "; c\n")
            .Write("proj/vehicle.h", "/* generated */\n").Write("proj/vehicle.kinc", "; generated\n")
            .Write("proj/notes.txt", "not a source\n").Write("proj/.hidden.h", "/* hidden */\n")
            .Write("proj/sub/deep.c", "/* deep */\n")
            .Write("kproj/main.kasm", ".include \"local.kinc\"\n").Write("kproj/local.kinc", "; local\n")
            .Write("main.c", """
                #include <kfs.h>
                #include "a.h"
                #include "sub/x.h"
                #include "missing.h"
                  #  include   "skipped.h"
                /* gap */
                  #  include   "dot.kasm.h"
                // #include "commented.h"
                /*
                #include "incomment.h"
                */
                #include "vehicle.h"
                #include "UTIL.h"
                """)
            .Write("a.h", "#include \"b.h\"\n").Write("b.h", "#include \"a.h\"\n#include \"c.h\"\n").Write("c.h", "/* c */\n")
            .Write("dot.kasm", "; dot\n").Write("skipped.h", "").Write("commented.h", "").Write("incomment.h", "")
            .Write("vehicle.h", "/* root vehicle.h is not excluded */\n").Write("util.h", "/* root util */\n")
            .Write("sub/x.h", "").Write("sub/f.kasm", "")
            .Write("k.c", "#include \"sub/f.kasm.h\"\n#include \"gone.kasm.h\"\n")
            .Write("prog.kasm", ".include \"purrtty.kinc\"\n")
            .Write("consts.kinc", "; consts\n")
            .Write("odd.txt", "a root file with another extension builds as kASM\n");
    }

    private static readonly string[] ShapeFiles =
    [
        "proj/main.c", "proj/util.h", "proj/vehicle.h", "proj/absent.c", "proj/sub/deep.c", "kproj/main.kasm",
        "main.c", "k.c", "prog.kasm", "odd.txt", "util.h", "consts.kinc",
    ];

    [Test]
    public void Parity_Resolver_Matches_Workspace_ProjectOf_On_Disk()
    {
        using var ws = new TempWorkspace();
        WriteShapes(ws);
        var resolver = ProjectResolver.ForDisk(ws.Root);
        var mismatches = new List<string>();
        foreach (string file in ShapeFiles)
        {
            string? difference = Compare(ws.Root, resolver, file);
            if (difference is not null) mismatches.Add($"{file}: {difference}");
        }
        Assert.That(mismatches, Is.Empty, () => string.Join("\n", mismatches));
    }

    /// <summary>Null when the resolver and the game agree on <paramref name="file"/>'s project.</summary>
    private static string? Compare(string root, ProjectResolver resolver, string file)
    {
        BuildProject? game = new Workspace(root).ProjectOf(file, null, null, out string? why);
        ProjectResolution mine = resolver.Resolve(file);
        if (game is null || mine.Project is null)
        {
            return game is null && mine.Project is null && why == mine.Reason ? null : $"game {(game is null ? "none: " + why : "a project")}, resolver {(mine.Project is null ? "none: " + mine.Reason : "a project")}";
        }
        KfsProject p = mine.Project;
        string gameShape = $"{game.Folder}|{game.Name}|{(game.IsKc ? "kC" : "kASM")}|{string.Join(",", game.Files.Select(f => f.Key + "=" + f.Value.Length))}";
        string myShape = $"{p.Folder}|{p.Name}|{p.LanguageName}|{string.Join(",", p.FileMap.Select(f => f.Key + "=" + f.Value.Length))}";
        if (gameShape != myShape) return $"game {gameShape}\n    resolver {myShape}";
        foreach ((string name, string text) in game.Files)
        {
            if (p.FileMap[name] != text) return $"text of {name} differs";
            if (p.PathOf(name) != game.WorkspacePath(name)) return $"path of {name}: {p.PathOf(name)} vs {game.WorkspacePath(name)}";
        }
        return null;
    }

    [Test]
    public void Parity_Stock_Examples_Build_As_The_Workbench_Builds_Them()
    {
        using var ws = new TempWorkspace();
        List<string> written = WriteExamples(ws.Root);
        Assert.That(written, Has.Count.EqualTo(Examples.Files.Count));

        var failures = new List<string>();
        int compared = 0;
        foreach (string file in Examples.Files.Keys.Order(StringComparer.Ordinal))
        {
            foreach (string layout in new[] { "vehicle", "bench" })
            {
                string? difference = CompareBuild(ws.Root, file, layout, out bool ok);
                if (difference is not null) failures.Add($"{file} ({layout}): {difference}");
                if (ok) compared++;
            }
        }
        Assert.That(failures, Is.Empty, () => string.Join("\n", failures));
        Assert.That(compared, Is.GreaterThanOrEqualTo(Examples.Files.Count), "most example builds produce an image, so images were compared");
    }

    [Test]
    public void Parity_PurrTTY_Programs_Build_As_The_Workbench_Builds_Them()
    {
        using var ws = new TempWorkspace();
        string kinc = CatalogEmitter.Kinc(XCatalog.Catalog);
        const string kasm = """
            .include <kfs.kinc>
            .include "purrtty.kinc"
            .tick_hz 10
            .task main rate_hz=10
            .func main
                PUSH32 X_LIGHT_ON
                DROP
                RET 0
            .endfunc
            """;
        ws.Write("blink.c", "#include <kfs.h>\n#include <purrtty.h>\n#pragma kfs tick_hz 10\n#pragma kfs task run rate_hz=10\nuint32_t n;\nvoid run(void) { x_light_set_on(0u, (n / 5u) % 2u); n = n + 1u; }\n")
            .Write("lights.kasm", kasm).Write("purrtty.kinc", kinc)            // root: .include from the root
            .Write("kfolder/main.kasm", kasm)                                    // folder: falls back to the root's purrtty.kinc
            .Write("kown/main.kasm", kasm).Write("kown/purrtty.kinc", kinc)    // folder: its own copy first
            .Write("broken.kasm", ".include \"nowhere.kinc\"\n")
            .Write("lonely.h", "");

        var failures = new List<string>();
        int ok = 0;
        foreach (string file in new[] { "blink.c", "lights.kasm", "kfolder/main.kasm", "kown/main.kasm", "kown/purrtty.kinc", "broken.kasm", "lonely.h" })
        {
            string? difference = CompareBuild(ws.Root, file, "vehicle", out bool built);
            if (difference is not null) failures.Add($"{file}: {difference}");
            if (built) ok++;
        }
        Assert.That(failures, Is.Empty, () => string.Join("\n", failures));
        Assert.That(ok, Is.EqualTo(5), "all but broken.kasm and lonely.h build");
    }

    private static List<string> WriteExamples(string root) => Examples.Write(new Workspace(root), overwrite: true, archived: true);

    /// <summary>Null when the workbench and the builder agree (ok, diagnostics, image bytes).</summary>
    private static string? CompareBuild(string root, string file, string layoutName, out bool ok)
    {
        GameTables tables = Runtime.Tables;
        IoLayout layout = layoutName == "bench" ? tables.BenchLayout : LayoutGenerator.Vehicle("t", 4, 1, 1);
        var workbench = new Workbench(tables, root, TextWriter.Null);
        string text = File.ReadAllText(Path.Combine(root, file));
        AssembleOutcome game = workbench.BuildText(file, text, tables.BenchPart, layout);
        ProjectBuild mine = ProjectBuilder.Build(ProjectResolver.ForDisk(root).Resolve(file), file, layout, tables, tables.BenchPart);
        ok = game.Ok && mine.Ok;

        string gameDiagnostics = string.Join("\n", game.Diagnostics.Select(d => d.ToString()));
        string myDiagnostics = mine.Result.Text;
        if (game.Ok != mine.Ok) return $"ok: game {game.Ok}, builder {mine.Ok}\n  game:\n{gameDiagnostics}\n  builder:\n{myDiagnostics}";
        if (gameDiagnostics != myDiagnostics) return $"diagnostics differ\n  game:\n{gameDiagnostics}\n  builder:\n{myDiagnostics}";
        if (!game.Ok) return null;
        if (!game.Image!.AsSpan().SequenceEqual(mine.Result.Image)) return $"images differ ({game.Image!.Length} vs {mine.Result.Image!.Length} bytes)";
        if ((game.CodeSize, game.RamRequired) != (mine.CodeSize, mine.RamRequired)) return $"sizes differ: {game.CodeSize}/{game.RamRequired} vs {mine.CodeSize}/{mine.RamRequired}";
        var gameWcet = game.Report?.Wcet.Select(w => $"{w.Task} {w.Cycles}/{w.Budget}") ?? [];
        var myWcet = mine.Tasks.Select(t => $"{t.Task} {t.Cycles}/{t.Budget}");
        if (!gameWcet.SequenceEqual(myWcet)) return "WCET differs";
        return null;
    }
}
