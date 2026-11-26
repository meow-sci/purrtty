using System.Text.Json.Nodes;
using KSA.Kfs.Host;
using NUnit.Framework;
using purrTTY.Kfs.Lsp;

namespace purrTTY.Kfs.Tools.Tests.Runtime.Lsp;

/// <summary>
/// Hover (plan §6.5) through the whole server on the game's real tables, headers and build target:
/// mod and stock services, constants, library prototypes, target-dependent names, members, kASM
/// mnemonics and symbols, diagnostic codes.
/// </summary>
[TestFixture]
public sealed class HoverTests : ToolsRuntimeFixture
{
    private static FeatureSession Bench(string encoding = "utf-16", bool markdown = true)
    {
        var ws = new TempWorkspace();
        return FeatureSession.Start(ws, KfsLanguageHost.ForBench(Runtime.Tables, ws.Root), encoding, markdown);
    }

    private const string Lights = "#include <kfs.h>\n#include <math.h>\n#include <purrtty.h>\n#pragma kfs tick_hz 10\n#pragma kfs task run rate_hz=10\n\n" +
        "kfs_msg_flight_data_t fd;\n/* KC-E003 is what a typo gives */\nvoid run(void)\n{\n    x_light_set_on(0, X_LIGHT_ON);\n    kfs_set_throttle(sinf(fd.altitude_m * DEG2RAD));\n" +
        "    float t = sys.t_s + (float)THRUSTER_COUNT + (float)KFS_BURN_DONE;\n    commands.engine[0].throttle = t;\n}\n";

    [Test]
    public void Hover_And_Completion_Know_Mod_Services()
    {
        using FeatureSession s = Bench();
        string uri = s.Open("lights.c", Lights);

        JsonObject hover = s.Hover(uri, 10, 8)!;
        string text = hover["contents"]!["value"]!.GetValue<string>();
        Assert.That(hover["contents"]!["kind"]!.GetValue<string>(), Is.EqualTo("markdown"));
        Assert.That(text, Does.StartWith("```c\nvoid x_light_set_on(uint32_t light, uint32_t on);\n```"));
        Assert.That(text, Does.Contain("Switches a light's part on or off"), "the summary");
        Assert.That(text, Does.Contain("- `light` (`uint32_t`): light index"), "the arguments with their documentation");
        Assert.That(text, Does.Contain("- `on` (`uint32_t`): 0 switches off, anything else on"));
        Assert.That(text, Does.Contain("On and off are per part"), "the catalogue's longer documentation");
        Assert.That(text, Does.Contain("**Rejected by the host when:** light index out of range or departed; light has no switch."));
        Assert.That(text, Does.Contain("Service id `0x5013`"), "the id");
        Assert.That(text, Does.Contain("`SVC_X_LIGHT_SET_ON`"));
        Assert.That(text, Does.Contain("cost class Light (10 cycles on Bench, K32F)"));
        Assert.That(hover["range"]!.ToJsonString(), Is.EqualTo(Range(10, 4, 18)), "the hovered word");

        // Completing x_li offers the light services, documented.
        string partial = s.Open("partial.c", "#include <purrtty.h>\nvoid f(void) { x_li }\n");
        JsonObject list = s.Complete(partial, 1, 19)!;
        List<string> labels = FeatureSession.Labels(list);
        Assert.That(labels, Is.SupersetOf(new[] { "x_light_count", "x_light_geti", "x_light_getf", "x_light_set_on", "x_light_set_color", "x_light_reset", "x_lights_set_all" }));
        Assert.That(labels.Where(l => !System.Text.RegularExpressions.Regex.IsMatch(l, "x.*l.*i", System.Text.RegularExpressions.RegexOptions.IgnoreCase)), Is.Empty,
            "filtered by the prefix (a case-insensitive subsequence)");
        JsonObject item = FeatureSession.Item(list, "x_light_set_on");
        Assert.That(item["kind"]!.GetValue<int>(), Is.EqualTo(3), "Function");
        Assert.That(item["detail"]!.GetValue<string>(), Is.EqualTo("void x_light_set_on(uint32_t light, uint32_t on)"));
        Assert.That(item["documentation"]!["kind"]!.GetValue<string>(), Is.EqualTo("markdown"));
        Assert.That(item["documentation"]!["value"]!.GetValue<string>(), Does.Contain("Service id `0x5013`"));
        Assert.That(item["textEdit"]!["range"]!.ToJsonString(), Is.EqualTo(Range(1, 15, 19)), "replaces exactly 'x_li'");
        Assert.That(item["textEdit"]!["newText"]!.GetValue<string>(), Is.EqualTo("x_light_set_on"));
        Assert.That(item["insertTextFormat"]!.GetValue<int>(), Is.EqualTo(1), "no snippets: the client did not offer them");
    }

    [Test]
    public void Stock_Services_Constants_And_Library_Prototypes()
    {
        using FeatureSession s = Bench();
        string uri = s.Open("lights.c", Lights);

        string throttle = s.HoverOn(uri, Lights, "kfs_set_throttle")!;
        Assert.That(throttle, Does.StartWith("```c\nvoid kfs_set_throttle(float x);\n```"));
        Assert.That(throttle, Does.Contain("Service id `0x0034` (52) · `SVC_SET_THROTTLE`"));
        Assert.That(throttle, Does.Contain("`x` `finite:[0,1]`"), "the VM's argument check");
        Assert.That(throttle, Does.Contain("**Steers the stock GUIDANCE block:**"), "the stock requirement");
        Assert.That(throttle, Does.Contain("`KC-E501`"));

        string sinf = s.HoverOn(uri, Lights, "sinf")!;
        Assert.That(sinf, Does.StartWith("```c\nfloat sinf(float x);\n```"));
        Assert.That(sinf, Does.Contain("Declared in `<math.h>`."));
        Assert.That(sinf, Does.Contain("library routine"));

        string light = s.HoverOn(uri, Lights, "X_LIGHT_ON")!;
        Assert.That(light, Does.StartWith("```c\n#define X_LIGHT_ON 2\n```"));
        Assert.That(light, Does.Contain("light properties: integers 0..15 (x_light_geti)"), "the group's comment in <purrtty.h>");
        Assert.That(light, Does.Contain("Defined in `<purrtty.h>`."));

        string burn = s.HoverOn(uri, Lights, "KFS_BURN_DONE")!;
        Assert.That(burn, Does.StartWith("```c\n#define KFS_BURN_DONE 4\n```"));
        Assert.That(burn, Does.Contain("Defined in `<kfs.h>`."));
        Assert.That(s.HoverOn(uri, Lights, "DEG2RAD"), Does.StartWith("```c\n#define DEG2RAD 0.017453292519943295f\n```"));

        // Directives and headers.
        string include = s.HoverOn(uri, Lights, "purrtty.h", 2)!;
        Assert.That(include, Does.StartWith("```c\n#include <purrtty.h>\n```"));
        Assert.That(include, Does.Contain("purrTTY vehicle-parts services for kFS"));
        Assert.That(s.HoverOn(uri, Lights, "tick_hz"), Does.Contain("N must be one of 5, 10, 20, 25, 50, 100 Hz"));
        Assert.That(s.HoverOn(uri, Lights, "task"), Does.StartWith("```c\n#pragma kfs task NAME rate_hz=N | divisor=N [priority=P]\n```"));
        Assert.That(s.HoverOn(uri, Lights, "pragma"), Does.Contain("#pragma kfs directive"));

        // Keywords, locals, numbers: nothing.
        Assert.That(s.HoverOn(uri, Lights, "void"), Is.Null, "a keyword");
        Assert.That(s.HoverOn(uri, Lights, "float t", 7), Is.Null, "a local");
        Assert.That(s.HoverOn(uri, Lights, "(0,", 1), Is.Null, "a number");
    }

    [Test]
    public void Members_Of_Sys_Commands_And_Messages()
    {
        using FeatureSession s = Bench();
        string uri = s.Open("lights.c", Lights);

        string t = s.HoverOn(uri, Lights, "sys.t_s", 5)!;
        Assert.That(t, Does.StartWith("```c\nconst float sys.t_s;\n```"));
        Assert.That(t, Does.Contain("Seconds since reset"));
        Assert.That(t, Does.Contain("Unit: s."));
        Assert.That(t, Does.Contain("address 0xA000000C"));

        string altitude = s.HoverOn(uri, Lights, "altitude_m")!;
        Assert.That(altitude, Does.StartWith("```c\nfloat altitude_m;\n```"));
        Assert.That(altitude, Does.Contain("Above mean radius"), "the catalogue's meaning");
        Assert.That(altitude, Does.Contain("Unit: m (from the name's suffix)."));
        Assert.That(altitude, Does.Contain("Offset 16 in `kfs_msg_flight_data_t`"));

        string throttle = s.HoverOn(uri, Lights, "throttle =", 2)!;
        Assert.That(throttle, Does.StartWith("```c\nfloat throttle;\n```"));
        Assert.That(throttle, Does.Contain("field of the command block"));

        string fd = s.HoverOn(uri, Lights, "kfs_msg_flight_data_t")!;
        Assert.That(fd, Does.Contain("Message `0x0102` MSG_FLIGHT_DATA"));
    }

    [Test]
    public void Target_Dependent_Names_Follow_The_Build_Target()
    {
        var ws = new TempWorkspace();
        KfsLanguageHost host = KfsLanguageHost.ForBench(Runtime.Tables, ws.Root);
        using FeatureSession s = FeatureSession.Start(ws, host);
        const string text = "#include <kfs.h>\n#include <vehicle.h>\nuint32_t n = THRUSTER_COUNT;\nvoid f(void) { commands. }\n";
        string uri = s.Open("t.c", text);

        Assert.That(s.HoverOn(uri, text, "THRUSTER_COUNT"), Does.StartWith("```c\n#define THRUSTER_COUNT 12\n```").And.Contain("(bench): 12"));
        Assert.That(FeatureSession.Labels(s.Complete(uri, 3, 24, ".")), Is.EquivalentTo(new[] { "engine", "gimbal", "thruster" }));
        Assert.That(FeatureSession.Item(s.Complete(uri, 3, 24, "."), "thruster")["detail"]!.GetValue<string>(), Is.EqualTo("kfs_thruster_cmd_t thruster[12]"));

        SetTarget(host, 2, 1, 0, "vehicle 7");
        Assert.That(s.HoverOn(uri, text, "THRUSTER_COUNT"), Does.StartWith("```c\n#define THRUSTER_COUNT 2\n```").And.Contain("(vehicle 7): 2"));
        Assert.That(FeatureSession.Labels(s.Complete(uri, 3, 24, ".")), Is.EquivalentTo(new[] { "engine", "thruster" }), "this vehicle has no gimbal");
        Assert.That(FeatureSession.Item(s.Complete(uri, 3, 24, "."), "thruster")["detail"]!.GetValue<string>(), Is.EqualTo("kfs_thruster_cmd_t thruster[2]"));

        SetTarget(host, 0, 3, 3, "vehicle 8");
        Assert.That(FeatureSession.Labels(s.Complete(uri, 3, 24, ".")), Is.EquivalentTo(new[] { "engine", "gimbal" }));
        Assert.That(s.HoverOn(uri, text, "THRUSTER_COUNT"), Does.Contain("does not exist on this target"));
    }

    private static void SetTarget(KfsLanguageHost host, int thrusters, int engines, int gimbals, string name) =>
        host.SetTarget(new BuildTarget(host.Tables.BenchPart, LayoutGenerator.Vehicle(name, thrusters, engines, gimbals), name));

    [Test]
    public void Kasm_Opcode_Hover_And_Symbols()
    {
        using FeatureSession s = Bench();
        const string text = ".include <kfs.kinc>\n.include \"purrtty.kinc\"\n.equ LIGHT, 0\n.tick_hz 10\n.task run rate_hz=10\n.func run\n    .local i\n" +
            "top:\n    PUSH32 LIGHT\n    PUSH32 X_LIGHT_ON\n    SYSCALL SVC_X_LIGHT_SET_ON\n    LDG sys.cycles\n    STL i\n    RET 0\n.endfunc\n";
        string uri = s.Open("k/main.kasm", text);

        string syscall = s.HoverOn(uri, text, "SYSCALL")!;
        Assert.That(syscall, Does.StartWith("```kasm\nSYSCALL id:u16\n```"));
        Assert.That(syscall, Does.Contain("**Stack:** Service-defined"), "the stack effect");
        Assert.That(syscall, Does.Contain("**Cost on Bench (K32F):** 10 cycles."), "the cost on the target's part");
        Assert.That(syscall, Does.Contain("Opcode `0x78`, 3 bytes"));

        string ldl = s.HoverOn(uri, text, "STL")!;
        Assert.That(ldl, Does.Contain("pops 1, pushes 0"));

        string svc = s.HoverOn(uri, text, "SVC_X_LIGHT_SET_ON")!;
        Assert.That(svc, Does.Contain("SYSCALL SVC_X_LIGHT_SET_ON    ; = 0x5013"));
        Assert.That(svc, Does.Contain("In kASM: push 2 argument cells"));
        Assert.That(s.HoverOn(uri, text, "sys.cycles", 5), Does.Contain("Cycles used so far in the current tick"), "a dotted <kfs.kinc> symbol");
        Assert.That(s.HoverOn(uri, text, "X_LIGHT_ON"), Does.Contain(".equ X_LIGHT_ON, 2").And.Contain("purrtty.kinc"));
        Assert.That(s.HoverOn(uri, text, "PUSH32 LIGHT", 8), Does.Contain(".equ LIGHT, 0"), "a .equ of the file");
        Assert.That(s.HoverOn(uri, text, "STL i", 4), Does.Contain("frame slot of `run`"));
        Assert.That(s.HoverOn(uri, text, "top:"), Does.Contain("A label of `run`"));
        Assert.That(s.HoverOn(uri, text, ".func"), Does.StartWith("```kasm\n.func name … .endfunc\n```"));
        Assert.That(s.HoverOn(uri, text, "kfs.kinc", 2), Does.Contain("A built-in include"));
    }

    [Test]
    public void Diagnostic_Codes_Give_The_Manual()
    {
        using FeatureSession s = Bench();
        string uri = s.Open("lights.c", Lights);
        string page = s.HoverOn(uri, Lights, "KC-E003", 3)!;
        Assert.That(page, Does.StartWith("**KC-E003: Unknown symbol or undeclared identifier**"));
        Assert.That(page, Does.Contain("severity E (default E); range 0xx; languages KC, KS, KA"));
        Assert.That(s.Hover(uri, 7, 4)!["range"]!.ToJsonString(), Is.EqualTo(Range(7, 3, 10)));

        // A diagnostic of the current text under the cursor adds its manual page.
        const string broken = "#include <kfs.h>\n#pragma kfs tick_hz 10\n#pragma kfs task run rate_hz=10\nvoid run(void) { oops_undeclared(); }\n";
        string b = s.Open("broken.c", broken);
        int mark = s.Client.Mark;
        s.Build(b, mark, p => p["diagnostics"]!.AsArray().Count > 0);
        string here = s.HoverOn(b, broken, "oops_undeclared", 3)!;
        Assert.That(here, Does.Contain("**KC-E003: Unknown symbol or undeclared identifier**"));
    }

    internal static string Range(int line, int start, int end) =>
        new JsonObject
        {
            ["start"] = new JsonObject { ["line"] = line, ["character"] = start },
            ["end"] = new JsonObject { ["line"] = line, ["character"] = end },
        }.ToJsonString();
}
