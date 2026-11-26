using System.Text.Json;
using NUnit.Framework;
using purrTTY.Kfs.Catalog;

namespace purrTTY.Kfs.Tools.Tests.Runtime;

/// <summary><c>purr-kfs check</c> on real temporary workspaces, in process, on the shared runtime.</summary>
[TestFixture]
public sealed class CliCheckTests : ToolsRuntimeFixture
{
    internal const string Blink = """
        #include <kfs.h>
        #include <purrtty.h>
        #pragma kfs tick_hz 10
        #pragma kfs task run rate_hz=10
        uint32_t n;
        void run(void)
        {
            if (n == 0u) { kfs_logf("lights: %u", x_light_count()); }
            x_light_set_on(0u, (n / 5u) % 2u);
            n = n + 1u;
        }

        """;

    [Test]
    public void Cli_Check_Clean_File_Exits_0_With_The_Summary()
    {
        using var ws = new TempWorkspace().Write("blink.c", Blink);

        CliOutcome r = PurrKfs.In(ws, "check", "blink.c");

        Assert.That(r.ExitCode, Is.EqualTo(0), r.ToString);
        Assert.That(r.Stderr, Is.Empty);
        Assert.That(r.Stdout, Does.Match("^build ok: blink\\.c \\(kC project \"blink\", 1 file\\): 0 errors, 0 warnings; code [1-9][0-9]* bytes, RAM [1-9][0-9]* bytes\n$"), r.ToString);
    }

    [Test]
    public void Cli_Check_Warnings_Exit_0_And_Print_The_Hint_Indented()
    {
        using var ws = new TempWorkspace().Write("warn.c", "#include <kfs.h>\n#pragma kfs tick_hz 10\n#pragma kfs task run rate_hz=10\nuint32_t n;\nvoid run(void)\n{\n    uint32_t unused;\n    n = n + 1u;\n}\n");

        CliOutcome r = PurrKfs.In(ws, "check", "warn.c");

        Assert.That(r.ExitCode, Is.EqualTo(0), r.ToString);
        Assert.That(r.Stdout, Does.StartWith(
            "warn.c:7:14: warning KC-W006: 'unused' is never used\n" +
            "  hint: Remove it, or cast it to `(void)` if it is kept on purpose.\n" +
            "build ok: warn.c (kC project \"warn\", 1 file): 0 errors, 1 warning; code "), r.ToString);
    }

    [Test]
    public void Cli_Check_Errors_Exit_1_In_The_Documented_Format()
    {
        using var ws = new TempWorkspace()
            .Write("proj/main.c", "#include <kfs.h>\n#include \"util.h\"\n#pragma kfs tick_hz 10\n#pragma kfs task run rate_hz=10\nuint32_t n;\nvoid run(void) { n = twice(n) + missing; }\n")
            .Write("proj/util.h", "uint32_t twice(uint32_t x) { return x * 2u + undefined_thing; }\n");

        CliOutcome r = PurrKfs.In(ws, "check", "proj/main.c");

        // The error inside the second project file is reported at its workspace path.
        Assert.That(r.ExitCode, Is.EqualTo(1), r.ToString);
        Assert.That(r.Stderr, Is.Empty);
        Assert.That(r.Stdout, Is.EqualTo(
            "proj/util.h:1:46: error KC-E003: 'undefined_thing' is not declared\n" +
            "proj/main.c:6:33: error KC-E003: 'missing' is not declared\n" +
            "build failed: proj/main.c (kC project \"proj\", 2 files): 2 errors, 0 warnings\n"));

        // Any file of the folder checks the same project.
        Assert.That(PurrKfs.In(ws, "check", "proj/util.h").Stdout, Does.EndWith("build failed: proj/util.h (kC project \"proj\", 2 files): 2 errors, 0 warnings\n"));
    }

    [Test]
    public void Cli_Check_Pure_Kasm_Errors_Map_Includes_To_Their_Paths()
    {
        using var ws = new TempWorkspace()
            .Write("bad.kasm", ".include <kfs.kinc>\n.include \"purrtty.kinc\"\n.tick_hz 10\n.task main rate_hz=10\n.func main\n    PUSH32 X_NOT_A_CONSTANT\n    DROP\n    RET 0\n.endfunc\n")
            .Write("kp/main.kasm", ".include <kfs.kinc>\n.include \"shared.kinc\"\n.include \"lib/defs.kinc\"\n.tick_hz 10\n.task main rate_hz=10\n.func main\n    RET 0\n.endfunc\n")
            .Write("kp/lib/defs.kinc", "; below the project folder: no project file, reached only by .include\n.equ ALSO_ONLY_A_NAME\n")
            .Write("shared.kinc", "; shared by the workspace\n.equ ONLY_A_NAME\n");

        CliOutcome bad = PurrKfs.In(ws, "check", "bad.kasm");
        Assert.That(bad.ExitCode, Is.EqualTo(1), bad.ToString);
        Assert.That(bad.Stdout, Is.EqualTo(
            "bad.kasm:2:1: error KA-E015: can't find 'purrtty.kinc'\n" +
            "  hint: `<>` searches generated and library includes, `\"\"` the project.\n" +
            "bad.kasm:6:1: error KA-E003: unknown symbol 'X_NOT_A_CONSTANT'\n" +
            "  hint: `.include` the file that defines it, or `.extern` it.\n" +
            "build failed: bad.kasm (kASM project \"bad\", 1 file): 2 errors, 0 warnings\n"));

        // The folder project reads shared.kinc from the workspace root (there is no kp/shared.kinc)
        // and lib/defs.kinc from kp/lib/: each error is reported at the file that was read.
        CliOutcome folder = PurrKfs.In(ws, "check", "kp/main.kasm");
        Assert.That(folder.ExitCode, Is.EqualTo(1), folder.ToString);
        Assert.That(folder.Stdout, Is.EqualTo(
            "shared.kinc:2:1: error KA-E012: `.equ name, value`\n" +
            "kp/lib/defs.kinc:2:1: error KA-E012: `.equ name, value`\n" +
            "build failed: kp/main.kasm (kASM project \"kp\", 1 file): 2 errors, 0 warnings\n"));

        // With purrtty.kinc in the workspace the first program is down to its own error.
        ws.Write("purrtty.kinc", CatalogEmitter.Kinc(XCatalog.Catalog));
        Assert.That(PurrKfs.In(ws, "check", "bad.kasm").Stdout, Does.StartWith("bad.kasm:6:1: error KA-E003: unknown symbol 'X_NOT_A_CONSTANT'\n"));
    }

    [Test]
    public void Cli_Check_Root_Header_Is_No_Project()
    {
        using var ws = new TempWorkspace().Write("util.h", "/* a header */\n");

        CliOutcome r = PurrKfs.In(ws, "check", "util.h");

        Assert.That(r.ExitCode, Is.EqualTo(1), r.ToString);
        Assert.That(r.Stdout, Is.EqualTo(
            "util.h:0:0: error kfs: util.h is a header: build a .c or .kasm file that includes it (a subfolder builds as one project)\n" +
            "build failed: util.h (no project): 1 error, 0 warnings\n"));
    }

    [Test]
    public void Cli_Check_Json_Has_The_Documented_Shape()
    {
        using var ws = new TempWorkspace()
            .Write("blink.c", Blink)
            .Write("proj/main.c", "#include <kfs.h>\n#pragma kfs tick_hz 10\n#pragma kfs task run rate_hz=10\nvoid run(void) { missing = 1u; }\n")
            .Write("proj/extra.h", "");

        CliOutcome ok = PurrKfs.In(ws, "check", "blink.c", "--json");
        Assert.That(ok.ExitCode, Is.EqualTo(0), ok.ToString);
        using (JsonDocument doc = JsonDocument.Parse(ok.Stdout))
        {
            JsonElement root = doc.RootElement;
            Assert.That(root.EnumerateObject().Select(p => p.Name),
                Is.EqualTo(new[] { "file", "ok", "errors", "warnings", "project", "reason", "diagnostics", "image", "tasks" }));
            Assert.That((root.GetProperty("file").GetString(), root.GetProperty("ok").GetBoolean(), root.GetProperty("errors").GetInt32()),
                Is.EqualTo(("blink.c", true, 0)));
            JsonElement project = root.GetProperty("project");
            Assert.That(project.EnumerateObject().Select(p => p.Name), Is.EqualTo(new[] { "name", "folder", "language", "files", "overlay", "includes" }));
            Assert.That((project.GetProperty("name").GetString(), project.GetProperty("language").GetString(), project.GetProperty("files")[0].GetString()),
                Is.EqualTo(("blink", "kC", "blink.c")));
            JsonElement image = root.GetProperty("image");
            Assert.That(image.GetProperty("bytes").GetInt32(), Is.GreaterThan(image.GetProperty("code").GetInt32()));
            JsonElement task = root.GetProperty("tasks").EnumerateArray().Single();
            Assert.That((task.GetProperty("name").GetString(), task.GetProperty("budget").GetInt64()), Is.EqualTo(("run", 100000L)));
            Assert.That(task.GetProperty("wcet").GetInt64(), Is.InRange(1L, 100000L));
        }
        Assert.That(ok.Stdout, Does.Not.Contain("\r"), "\\n line ends");

        CliOutcome failed = PurrKfs.In(ws, "check", "proj/main.c", "--json");
        Assert.That(failed.ExitCode, Is.EqualTo(1), failed.ToString);
        using (JsonDocument doc = JsonDocument.Parse(failed.Stdout))
        {
            JsonElement root = doc.RootElement;
            Assert.That((root.GetProperty("ok").GetBoolean(), root.GetProperty("image").ValueKind, root.GetProperty("tasks").GetArrayLength()),
                Is.EqualTo((false, JsonValueKind.Null, 0)));
            Assert.That(root.GetProperty("project").GetProperty("files").EnumerateArray().Select(f => f.GetString()), Is.EqualTo(new[] { "proj/extra.h", "proj/main.c" }));
            JsonElement d = root.GetProperty("diagnostics").EnumerateArray().First();
            Assert.That(d.EnumerateObject().Select(p => p.Name), Is.EqualTo(new[] { "file", "path", "line", "column", "severity", "code", "message", "hint" }));
            Assert.That((d.GetProperty("file").GetString(), d.GetProperty("path").GetString(), d.GetProperty("severity").GetString(), d.GetProperty("code").GetString()),
                Is.EqualTo(("main.c", "proj/main.c", "error", "KC-E003")));
        }

        // Deterministic: the same document every time.
        Assert.That(PurrKfs.In(ws, "check", "blink.c", "--json").Stdout, Is.EqualTo(ok.Stdout));
    }

    [Test]
    public void Cli_Check_And_Run_Write_Nothing_Into_The_Workspace()
    {
        using var ws = new TempWorkspace().Write("blink.c", Blink).Write("proj/main.c", Blink).Write("prog.kasm", "; empty\n");
        IReadOnlyList<string> before = ws.Snapshot();

        PurrKfs.In(ws, "check", "blink.c");
        PurrKfs.In(ws, "check", "proj/main.c", "--json");
        PurrKfs.In(ws, "check", "prog.kasm");
        PurrKfs.In(ws, "run", "proj/main.c", "--lights", "1", "--for", "1s");

        Assert.That(ws.Snapshot(), Is.EqualTo(before), "no vehicle.h, no image, no report");
    }

    [Test]
    public void Cli_Check_Accepts_An_Absolute_Path_Inside_The_Workspace()
    {
        using var ws = new TempWorkspace().Write("proj/main.c", Blink);

        CliOutcome r = PurrKfs.In(ws, "check", ws.Full("proj/main.c"));

        Assert.That(r.ExitCode, Is.EqualTo(0), r.ToString);
        Assert.That(r.Stdout, Does.StartWith("build ok: proj/main.c (kC project \"proj\", 1 file)"));
    }
}
