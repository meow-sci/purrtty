using System.Text.Json.Nodes;
using NUnit.Framework;
using purrTTY.Kfs.Catalog;
using purrTTY.Kfs.Lsp;
using purrTTY.Kfs.Lsp.Documents;
using purrTTY.Kfs.Tools.Tests.Lsp;

namespace purrTTY.Kfs.Tools.Tests.Runtime.Lsp;

/// <summary>
/// Diagnostics through the whole server — documents, the build scheduler on the game's real
/// compiler and assembler, the mapping of plan §6.4 — on real temporary workspaces.
/// </summary>
[TestFixture]
public sealed class DiagnosticsTests : ToolsRuntimeFixture
{
    /// <summary>A started, initialized server on <paramref name="ws"/> with a bench host.</summary>
    private static ServerHarness Serve(TempWorkspace ws, params string[] encodings)
    {
        var h = ServerHarness.Start(KfsLanguageHost.ForBench(Runtime.Tables, ws.Root));
        h.Client.Initialize(null, encodings);
        return h;
    }

    private static string Uri(TempWorkspace ws, string path) => new WorkspaceUris(ws.Root).ToUri(path);

    /// <summary>"line:char-line:char code" of each diagnostic, for compact assertions.</summary>
    private static List<string> Summary(JsonObject published) =>
        published["diagnostics"]!.AsArray().Select(d =>
        {
            JsonNode r = d!["range"]!;
            return $"{r["start"]!["line"]}:{r["start"]!["character"]}-{r["end"]!["line"]}:{r["end"]!["character"]} {d["code"]}";
        }).ToList();

    private static JsonObject OpenAndWait(ServerHarness h, string uri, string text, Func<JsonObject, bool>? until = null, int version = 1)
    {
        int mark = h.Client.Mark;
        h.Client.Open(uri, text, version);
        h.Flush();
        return h.Client.WaitForDiagnostics(uri, until, mark);
    }

    private static JsonObject ChangeAndWait(ServerHarness h, string uri, int version, string text, Func<JsonObject, bool>? until = null)
    {
        int mark = h.Client.Mark;
        h.Client.Change(uri, version, text);
        h.Flush();
        return h.Client.WaitForDiagnostics(uri, p => p["version"]?.GetValue<int>() == version && (until is null || until(p)), mark, $"diagnostics for version {version}");
    }

    // The spike's unicode.c: a tab, an astral character and two 2-byte characters before the error.
    private const string Unicode = "#include <kfs.h>\n#pragma kfs tick_hz 10\n#pragma kfs task t rate_hz=10\n\nvoid t(void)\n{\n\t/* 🐱 é */ kfs_logf(\"ü\"); oops_undeclared();\n}\n";

    [Test]
    public void Diagnostics_Map_Positions()
    {
        foreach ((string encoding, int start) in new[] { ("utf-16", 27), ("utf-8", 31), ("utf-32", 26) })
        {
            foreach (string text in new[] { Unicode, Unicode.ReplaceLineEndings("\r\n") })
            {
                using var ws = new TempWorkspace().Write("unicode.c", text);
                using ServerHarness h = Serve(ws, encoding);
                string uri = Uri(ws, "unicode.c");

                JsonObject published = OpenAndWait(h, uri, text);

                string what = $"{encoding}, {(text.Contains('\r') ? "CRLF" : "LF")}";
                Assert.That(Summary(published), Is.EqualTo(new[] { $"6:{start}-6:{start + 15} KC-E003" }), what);
                JsonNode d = published["diagnostics"]![0]!;
                Assert.That(d["severity"]!.GetValue<int>(), Is.EqualTo(1));
                Assert.That(d["source"]!.GetValue<string>(), Is.EqualTo("kfs"));
                Assert.That(d["message"]!.GetValue<string>(), Does.StartWith("'oops_undeclared' is not declared\nhint: "), what);
                Assert.That(published["version"]!.GetValue<int>(), Is.EqualTo(1));
            }
        }

        // Token widths: a warning on a name, and a pragma diagnostic (reported at the '#') covering its line.
        using (var ws = new TempWorkspace())
        {
            using ServerHarness h = Serve(ws);
            string uri = Uri(ws, "w.c");
            JsonObject published = OpenAndWait(h, uri, "#include <kfs.h>\n#pragma kfs tick_hz 7\n#pragma kfs task run rate_hz=10\nvoid run(void) { }\n");
            Assert.That(Summary(published), Is.EqualTo(new[] { "1:0-1:21 KC-E406" }), published.ToJsonString());
            string warn = Uri(ws, "warn.c");
            published = OpenAndWait(h, warn, "#include <kfs.h>\n#pragma kfs tick_hz 10\n#pragma kfs task run rate_hz=10\nvoid run(void)\n{\n    uint32_t unused_value;\n}\n");
            Assert.That(Summary(published), Is.EqualTo(new[] { "5:13-5:25 KC-W006" }), published.ToJsonString());
            JsonNode warning = published["diagnostics"]!.AsArray().First(d => d!["code"]!.GetValue<string>() == "KC-W006")!;
            Assert.That(warning["severity"]!.GetValue<int>(), Is.EqualTo(2));
            Assert.That(warning["tags"]![0]!.GetValue<int>(), Is.EqualTo(1), "unnecessary");
        }
    }

    [Test]
    public void Diagnostics_Cover_Whole_Project()
    {
        using var ws = new TempWorkspace()
            .Write("proj/a.c", "#include <kfs.h>\n#include \"b.h\"\n#pragma kfs tick_hz 10\n#pragma kfs task run rate_hz=10\nuint32_t n;\nvoid run(void) { n = twice(n); }\n")
            .Write("proj/b.h", "uint32_t twice(uint32_t x) { return x * 2u + not_here; }\n")
            .Write("proj/c.h", "/* clean */\n");
        using ServerHarness h = Serve(ws);
        string a = Uri(ws, "proj/a.c"), b = Uri(ws, "proj/b.h"), c = Uri(ws, "proj/c.h");

        int mark = h.Client.Mark;
        h.Client.Open(a, File.ReadAllText(ws.Full("proj/a.c")));
        h.Flush();

        // Only a.c is open; the error in b.h is published for b.h, and every file of the folder gets a list.
        JsonObject forB = h.Client.WaitForDiagnostics(b, null, mark);
        Assert.That(Summary(forB), Is.EqualTo(new[] { "0:45-0:53 KC-E003" }));
        Assert.That(forB.ContainsKey("version"), Is.False, "b.h is not open");
        Assert.That(h.Client.WaitForDiagnostics(a, null, mark)["diagnostics"]!.AsArray(), Is.Empty);
        Assert.That(h.Client.WaitForDiagnostics(c, null, mark)["diagnostics"]!.AsArray(), Is.Empty);

        // Fixing b.h on disk (and telling the server) publishes an empty list for it.
        ws.Write("proj/b.h", "uint32_t twice(uint32_t x) { return x * 2u; }\n");
        mark = h.Client.Mark;
        h.Client.Notify("workspace/didChangeWatchedFiles", new JsonObject { ["changes"] = new JsonArray(new JsonObject { ["uri"] = b, ["type"] = 2 }) });
        h.Flush();
        Assert.That(h.Client.WaitForDiagnostics(b, null, mark)["diagnostics"]!.AsArray(), Is.Empty);

        // Broken again; then closing the last document of the project clears what it published.
        ws.Write("proj/b.h", "uint32_t twice(uint32_t x) { return x * 2u + not_here; }\n");
        mark = h.Client.Mark;
        h.Client.Notify("workspace/didChangeWatchedFiles", new JsonObject { ["changes"] = new JsonArray(new JsonObject { ["uri"] = b, ["type"] = 2 }) });
        h.Flush();
        Assert.That(Summary(h.Client.WaitForDiagnostics(b, null, mark)), Is.EqualTo(new[] { "0:45-0:53 KC-E003" }));
        mark = h.Client.Mark;
        h.Client.Close(a);
        h.Client.Sync();
        Assert.That(h.Client.WaitForDiagnostics(b, null, mark)["diagnostics"]!.AsArray(), Is.Empty);
        Assert.That(h.Builds.OpenProjects, Is.Empty);
    }

    [Test]
    public void Unsaved_Buffers_Overlay_Disk()
    {
        // On disk the project builds; the editor changes both files, and only both edits together build clean.
        using var ws = new TempWorkspace()
            .Write("proj/main.c", "#include <kfs.h>\n#include \"util.h\"\n#pragma kfs tick_hz 10\n#pragma kfs task run rate_hz=10\nuint32_t n;\nvoid run(void) { n = old(n); }\n")
            .Write("proj/util.h", "uint32_t old(uint32_t x) { return x; }\n");
        using ServerHarness h = Serve(ws);
        string main = Uri(ws, "proj/main.c"), util = Uri(ws, "proj/util.h");
        const string mainEdit = "#include <kfs.h>\n#include \"util.h\"\n#pragma kfs tick_hz 10\n#pragma kfs task run rate_hz=10\nuint32_t n;\nvoid run(void) { n = twice(n); }\n";
        const string utilEdit = "uint32_t twice(uint32_t x) { return x * 2u; }\n";

        JsonObject first = OpenAndWait(h, main, mainEdit);
        Assert.That(Summary(first), Is.EqualTo(new[] { "5:21-5:26 KC-E003" }), "main.c's buffer is built, util.h still from disk");

        JsonObject both = OpenAndWait(h, util, utilEdit, version: 4);
        Assert.That(both["diagnostics"]!.AsArray(), Is.Empty, "util.h's buffer is built too");
        Assert.That(both["version"]!.GetValue<int>(), Is.EqualTo(4));
        Assert.That(h.Client.WaitForDiagnostics(main, p => p["diagnostics"]!.AsArray().Count == 0)["version"]!.GetValue<int>(), Is.EqualTo(1));

        // The disk was never touched; closing util.h builds main.c against the disk again.
        Assert.That(File.ReadAllText(ws.Full("proj/util.h")), Does.Contain("old"));
        int mark = h.Client.Mark;
        h.Client.Close(util);
        h.Flush();
        Assert.That(Summary(h.Client.WaitForDiagnostics(main, null, mark)), Is.EqualTo(new[] { "5:21-5:26 KC-E003" }));
    }

    [Test]
    public void Unsaved_Include_Of_An_Overlay_Only_File()
    {
        using var ws = new TempWorkspace().Write("main.c", Programs.Clean);
        using ServerHarness h = Serve(ws);
        string main = Uri(ws, "main.c"), x = Uri(ws, "x.h");

        // x.h exists only in the editor; main.c's unsaved #include "x.h" finds it.
        int mark = h.Client.Mark;
        h.Client.Open(x, "uint32_t helper(uint32_t v) { return v + broken; }\n");
        h.Client.Open(main, "#include <kfs.h>\n#include \"x.h\"\n#pragma kfs tick_hz 10\n#pragma kfs task run rate_hz=10\nuint32_t n;\nvoid run(void) { n = helper(n); }\n");
        h.Flush();
        Assert.That(Summary(h.Client.WaitForDiagnostics(x, p => p["diagnostics"]!.AsArray().Count > 0, mark)), Is.EqualTo(new[] { "0:41-0:47 KC-E003" }));
        Assert.That(h.Client.WaitForDiagnostics(main, null, mark)["diagnostics"]!.AsArray(), Is.Empty, "no 'can't find x.h'");

        JsonObject fixedX = ChangeAndWait(h, x, 2, "uint32_t helper(uint32_t v) { return v + 1u; }\n");
        Assert.That(fixedX["diagnostics"]!.AsArray(), Is.Empty);
        Assert.That(File.Exists(ws.Full("x.h")), Is.False);
    }

    [Test]
    public void Header_Diagnostics_Anchor_On_Include()
    {
        using var ws = new TempWorkspace();
        using ServerHarness h = Serve(ws);

        // A macro that breaks a prototype inside <kfs.h> (spike 05's header_error.c) and one inside <purrtty.h>.
        string kfs = Uri(ws, "kfs_break.c");
        JsonObject published = OpenAndWait(h, kfs, "/* breaks <kfs.h> */\n#define kfs_halt 42\n#include <kfs.h>\n#pragma kfs tick_hz 10\n#pragma kfs task t rate_hz=10\n\nvoid t(void) { }\n");
        JsonNode d = published["diagnostics"]![0]!;
        Assert.That(Summary(published)[0], Is.EqualTo("2:0-2:16 KC-E012"), "the whole #include <kfs.h> line");
        Assert.That(d["message"]!.GetValue<string>(), Does.Match(@"^\[in <kfs\.h>:\d+:\d+\] "));

        string purr = Uri(ws, "purr_break.c");
        published = OpenAndWait(h, purr, "#include <kfs.h>\n#define x_light_count 42\n#include <purrtty.h>\n#pragma kfs tick_hz 10\n#pragma kfs task t rate_hz=10\nvoid t(void) { }\n");
        Assert.That(Summary(published)[0], Is.EqualTo("2:0-2:20 KC-E012"), published.ToJsonString());
        Assert.That(published["diagnostics"]![0]!["message"]!.GetValue<string>(), Does.StartWith("[in <purrtty.h>:"));
    }

    [Test]
    public void No_Position_Diagnostic_Lands_On_The_Main_File()
    {
        // A folder project with no task: KC-E401 at (project, 0, 0), shown on main.c's first line
        // although only the other file is open.
        using var ws = new TempWorkspace()
            .Write("np/main.c", "#include <kfs.h>\nuint32_t n;\n")
            .Write("np/zz.c", "#include <kfs.h>\nuint32_t m;\n");
        using ServerHarness h = Serve(ws);
        string main = Uri(ws, "np/main.c"), other = Uri(ws, "np/zz.c");

        int mark = h.Client.Mark;
        h.Client.Open(other, "#include <kfs.h>\nuint32_t m;\n");
        h.Flush();
        JsonObject published = h.Client.WaitForDiagnostics(main, p => p["diagnostics"]!.AsArray().Count > 0, mark);
        Assert.That(Summary(published), Is.EqualTo(new[] { "0:0-0:16 KC-E401" }));
        Assert.That(h.Client.WaitForDiagnostics(other, null, mark)["diagnostics"]!.AsArray(), Is.Empty);
    }

    [Test]
    public void Kasm_Diagnostics_Use_The_Whole_Trimmed_Line()
    {
        string kinc = CatalogEmitter.Kinc(XCatalog.Catalog);
        using var ws = new TempWorkspace();
        using ServerHarness h = Serve(ws);
        string bad = Uri(ws, "bad.kasm");

        JsonObject published = OpenAndWait(h, bad, ".include <kfs.kinc>\n.include \"purrtty.kinc\"\n.tick_hz 10\n.task main rate_hz=10\n.func main\n    PUSH32 X_NOT_A_CONSTANT   \n    DROP\n    RET 0\n.endfunc\n");
        Assert.That(Summary(published), Is.EqualTo(new[] { "1:0-1:23 KA-E015", "5:4-5:27 KA-E003" }), published.ToJsonString());

        // With purrtty.kinc in the workspace (here an unsaved buffer) only the unknown symbol is left;
        // an error inside the include is published for the include.
        string kincUri = Uri(ws, "purrtty.kinc");
        int mark = h.Client.Mark;
        h.Client.Open(kincUri, kinc + "\n.equ BROKEN\n", languageId: "kasm");
        h.Flush();
        JsonObject inKinc = h.Client.WaitForDiagnostics(kincUri, p => p["diagnostics"]!.AsArray().Count > 0, mark);
        int brokenLine = new LineIndex(kinc + "\n.equ BROKEN\n").Count - 2;
        Assert.That(Summary(inKinc), Is.EqualTo(new[] { $"{brokenLine}:0-{brokenLine}:11 KA-E012" }));
        Assert.That(Summary(h.Client.WaitForDiagnostics(bad, p => p["diagnostics"]!.AsArray().Count == 1, mark)), Is.EqualTo(new[] { "5:4-5:27 KA-E003" }));
    }

    [Test]
    public void Kasm_Project_Including_Purrtty_Kinc_Builds_Clean()
    {
        using var ws = new TempWorkspace().Write("purrtty.kinc", CatalogEmitter.Kinc(XCatalog.Catalog));
        using ServerHarness h = Serve(ws);
        string lights = Uri(ws, "lights/main.kasm");
        // A folder project: .include "purrtty.kinc" falls back to the workspace root's copy.
        JsonObject published = OpenAndWait(h, lights,
            ".include <kfs.kinc>\n.include \"purrtty.kinc\"\n.tick_hz 10\n.task main rate_hz=10\n.func main\n    PUSH32 X_LIGHT_ON\n    DROP\n    RET 0\n.endfunc\n");
        Assert.That(published["diagnostics"]!.AsArray(), Is.Empty, published.ToJsonString());
        Assert.That(h.Builds.ResultFor("lights/main.kasm")!.Paths, Does.Contain("purrtty.kinc"), "the include read is part of the build");
    }

    [Test]
    public void Mod_Services_Are_Known()
    {
        using var ws = new TempWorkspace();
        using ServerHarness h = Serve(ws);
        string uri = Uri(ws, "blink.c");

        JsonObject clean = OpenAndWait(h, uri, CliCheckTests.Blink);
        Assert.That(clean["diagnostics"]!.AsArray(), Is.Empty, clean.ToJsonString());
        Assert.That(h.Builds.ResultFor("blink.c", successfulOnly: true)?.Ok, Is.True);

        JsonObject typo = ChangeAndWait(h, uri, 2, CliCheckTests.Blink.Replace("x_light_set_on(", "x_light_set_onn("));
        Assert.That(Summary(typo), Is.EqualTo(new[] { "8:4-8:19 KC-E003" }), typo.ToJsonString());
        Assert.That(h.Builds.ResultFor("blink.c")!.Ok, Is.False);
        Assert.That(h.Builds.ResultFor("blink.c", successfulOnly: true)!.Ok, Is.True, "the last good build is kept");
    }
}
