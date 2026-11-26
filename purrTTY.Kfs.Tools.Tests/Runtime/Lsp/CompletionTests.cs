using System.Text.Json.Nodes;
using NUnit.Framework;
using purrTTY.Kfs.Lsp;

namespace purrTTY.Kfs.Tools.Tests.Runtime.Lsp;

/// <summary>Completion contexts (plan §6.5) through the whole server on the game's real tables.</summary>
[TestFixture]
public sealed class CompletionTests : ToolsRuntimeFixture
{
    private static FeatureSession Bench(TempWorkspace? ws = null, bool snippets = false)
    {
        ws ??= new TempWorkspace();
        return FeatureSession.Start(ws, KfsLanguageHost.ForBench(Runtime.Tables, ws.Root), snippets: snippets);
    }

    private static List<string> At(FeatureSession s, string uri, string text, int line, string? trigger = null) =>
        FeatureSession.Labels(s.CompleteAtEnd(uri, text, line, trigger));

    [Test]
    public void Preprocessor_Pragma_And_Include_Contexts()
    {
        var ws = new TempWorkspace()
            .Write("proj/main.c", "")
            .Write("proj/util.h", "/* util */\n")
            .Write("proj/dot3.kasm", ".func dot3\n RET 0\n.endfunc\n")
            .Write("proj/vehicle.h", "/* generated */\n")
            .Write("proj/notes.txt", "x");
        using FeatureSession s = Bench(ws);
        const string text = "#\n#inc\n#pragma \n#pragma kfs \n#pragma kfs tick_hz \n#pragma kfs task run \n#pragma kfs task run rate_hz=\n#pragma kfs bounds \n" +
            "#pragma kfs subscribe \n#pragma kfs subscribe MSG_FLIGHT_DATA \n#include <\n#include \"\n#pragma kfs tick_hz 10\nkfs_msg_flight_data_t fd;\nvoid run(void) { }\n";
        string uri = s.Open("proj/main.c", text);
        s.Open("proj/fresh.h", "/* unsaved, never on disk */\n");

        Assert.That(At(s, uri, text, 0, "#"), Is.SupersetOf(new[] { "include", "define", "pragma", "ifdef", "endif", "error", "line" }));
        JsonObject inc = s.CompleteAtEnd(uri, text, 1)!;
        Assert.That(FeatureSession.Labels(inc), Is.EqualTo(new[] { "include" }), "filtered by 'inc'");
        Assert.That(FeatureSession.Item(inc, "include")["textEdit"]!["range"]!.ToJsonString(), Is.EqualTo(HoverTests.Range(1, 1, 4)));
        Assert.That(At(s, uri, text, 2), Is.EquivalentTo(new[] { "kfs", "once" }));
        Assert.That(At(s, uri, text, 3), Is.EquivalentTo(new[] { "tick_hz", "task", "init", "optimize", "bounds", "publish", "subscribe", "claim", "strict" }));
        Assert.That(At(s, uri, text, 4), Is.EquivalentTo(new[] { "5", "10", "20", "25", "50", "100" }));
        Assert.That(At(s, uri, text, 5), Is.EquivalentTo(new[] { "rate_hz", "divisor", "priority" }));
        Assert.That(At(s, uri, text, 6), Is.EquivalentTo(new[] { "1", "2", "5", "10" }), "rates that divide the file's tick_hz 10");
        Assert.That(At(s, uri, text, 7), Is.EquivalentTo(new[] { "on", "off" }));
        Assert.That(At(s, uri, text, 8), Does.Contain("MSG_FLIGHT_DATA").And.Contain("MSG_NAV_STATE"));
        Assert.That(At(s, uri, text, 9), Is.EqualTo(new[] { "fd" }), "file-scope buffers of the message's type");

        JsonObject headers = s.CompleteAtEnd(uri, text, 10, "<")!;
        Assert.That(FeatureSession.Labels(headers), Is.SupersetOf(new[] { "kfs.h", "purrtty.h", "vehicle.h", "math.h", "vecmat.h", "string.h", "stdint.h" }));
        Assert.That(FeatureSession.Item(headers, "purrtty.h")["textEdit"]!["newText"]!.GetValue<string>(), Is.EqualTo("purrtty.h>"));
        Assert.That(FeatureSession.Item(headers, "vehicle.h")["documentation"]!["value"]!.GetValue<string>(), Does.Contain("bench"));

        Assert.That(At(s, uri, text, 11, "\""), Is.EquivalentTo(new[] { "util.h", "fresh.h", "dot3.kasm.h" }),
            "the folder's headers (disk and unsaved buffers), X.kasm.h for its kASM files; not vehicle.h, not itself, not other files");

        // A task name: the file's void f(void) functions.
        const string task = "#pragma kfs task \nvoid run(void) { }\nvoid other(void) { }\nint notatask(int x) { return x; }\n";
        string t = s.Open("t.c", task);
        Assert.That(At(s, t, task, 0), Is.EquivalentTo(new[] { "run", "other" }));
    }

    [Test]
    public void Members_Of_Sys_Messages_And_Vectors()
    {
        using FeatureSession s = Bench();
        const string text = "#include <kfs.h>\n#include <vehicle.h>\nkfs_msg_flight_data_t fd;\nkfs_msg_vehicle_props_t vp;\nvoid f(const vec3 *v)\n{\n" +
            "    float a = sys.\n    a = fd.\n    a = vp.thruster[i].\n    a = vp.thruster[0].force_body_N.\n    a = v->\n    a = fd.hdr.\n    a = sensors.imu.\n    a = foo().\n    a = 1.\n}\n";
        string uri = s.Open("m.c", text);

        List<string> sys = At(s, uri, text, 6, ".");
        Assert.That(sys, Is.SupersetOf(new[] { "cycles", "budget", "tick", "t_s", "dt_s", "met_us", "rejected_requests" }));
        JsonObject fd = s.CompleteAtEnd(uri, text, 7, ".")!;
        Assert.That(FeatureSession.Labels(fd), Is.SupersetOf(new[] { "hdr", "altitude_m", "apoapsis_m", "speed_mps", "q_helm_to_body" }));
        string altitude = FeatureSession.Item(fd, "altitude_m")["documentation"]!["value"]!.GetValue<string>();
        Assert.That(altitude, Does.Contain("Unit: m (from the name's suffix)."));
        Assert.That(FeatureSession.Item(fd, "speed_mps")["documentation"]!["value"]!.GetValue<string>(), Does.Contain("Unit: m/s"));
        Assert.That(FeatureSession.Item(fd, "q_helm_to_body")["detail"]!.GetValue<string>(), Is.EqualTo("quat q_helm_to_body"));

        Assert.That(At(s, uri, text, 8, "."), Is.SupersetOf(new[] { "force_body_N", "torque_body_Nm", "min_pulse_s", "propellant_ok" }), "an element of a per-vehicle array");
        Assert.That(At(s, uri, text, 9, "."), Is.EquivalentTo(new[] { "x", "y", "z" }));
        Assert.That(At(s, uri, text, 10, ">"), Is.EquivalentTo(new[] { "x", "y", "z" }), "a pointer parameter, through ->");
        Assert.That(At(s, uri, text, 11, "."), Is.EquivalentTo(new[] { "seq", "source", "flags", "t_us" }));
        Assert.That(At(s, uri, text, 12, "."), Is.EquivalentTo(new[] { "accel_body_mps2", "rates_body_rps", "valid" }), "the bench's sensor block");
        Assert.That(s.CompleteAtEnd(uri, text, 13, ".")!["items"]!.AsArray(), Is.Empty, "a call's result: the type is not known cheaply");
        Assert.That(s.CompleteAtEnd(uri, text, 14, ".")!["items"]!.AsArray(), Is.Empty, "a float literal");
    }

    [Test]
    public void Identifiers_Keywords_And_Project_Symbols()
    {
        using FeatureSession s = Bench(snippets: true);
        const string good = "#include <kfs.h>\n#pragma kfs tick_hz 10\n#pragma kfs task run rate_hz=10\nuint32_t counter;\nuint32_t twice(uint32_t x) { return x * 2u; }\nvoid run(void) { counter = twice(counter); }\n";
        string uri = s.Open("p.c", good);
        int mark = s.Client.Mark;
        s.Build(uri, mark, p => p["diagnostics"]!.AsArray().Count == 0);

        // The buffer no longer builds: completion and hover still work from the indexes and the last good build.
        const string broken = "#include <kfs.h>\n#pragma kfs tick_hz 10\n#pragma kfs task run rate_hz=10\nuint32_t counter;\nuint32_t twice(uint32_t x) { return x * 2u; }\nvoid run(void) { counter = tw\n";
        mark = s.Client.Mark;
        s.Client.Change(uri, 2, broken);
        s.Build(uri, mark, p => p["version"]?.GetValue<int>() == 2 && p["diagnostics"]!.AsArray().Count > 0);

        JsonObject list = s.Complete(uri, 5, 29)!;
        JsonObject twice = FeatureSession.Item(list, "twice");
        Assert.That(twice["kind"]!.GetValue<int>(), Is.EqualTo(3));
        Assert.That(twice["detail"]!.GetValue<string>(), Is.EqualTo("uint32_t twice(uint32_t x)"), "the declaration from the built text");
        Assert.That(twice["textEdit"]!["newText"]!.GetValue<string>(), Is.EqualTo("twice(${1:x})$0"), "a call snippet: the client takes snippets");
        Assert.That(twice["insertTextFormat"]!.GetValue<int>(), Is.EqualTo(2));
        Assert.That(twice["sortText"]!.GetValue<string>(), Does.StartWith("00"), "the project's own names first");

        string hover = s.HoverOn(uri, broken, "twice", 2)!;
        Assert.That(hover, Does.StartWith("```c\nuint32_t twice(uint32_t x);\n```"));
        Assert.That(hover, Does.Contain("kC function in `p.c` line 5."));
        Assert.That(hover, Does.Contain("From the last successful build"));
        Assert.That(s.HoverOn(uri, broken, "counter;", 2), Does.Contain("Global in `p.c` line 4: 4 bytes"));
        Assert.That(s.HoverOn(uri, broken, "run(void)", 2), Does.Contain("A periodic task: rate divisor 1, priority 0."));

        // Identifiers: services, prototypes, header names, accepted keywords; not unsupported ones.
        const string any = "void f(void) {  }\n";
        string a = s.Open("any.c", any);
        List<string> all = FeatureSession.Labels(s.Complete(a, 0, 15));
        Assert.That(all, Is.SupersetOf(new[] { "kfs_set_throttle", "x_light_set_on", "sqrtf", "vec3_add", "memcpy", "KFS_BURN_DONE", "THRUSTER_COUNT", "MSG_FLIGHT_DATA", "sys", "commands", "uint32_t", "vec3", "while", "return", "sizeof", "_Static_assert" }));
        Assert.That(all, Has.None.AnyOf("union", "goto", "_Generic", "_Atomic", "nullptr", "constexpr", "typeof", "_Noreturn", "__kfs_pragma__", "__at", "__kfs_offsetof", "auto"));
        Assert.That(all, Has.None.StartsWith("__"), "internal names stay out of the list");

        // Nothing in comments and strings; nothing for a trigger that does not start a context.
        const string quiet = "void f(void) { /* x_l */ kfs_logf(\"x_l\"); if (a < b) { } }\n";
        string q = s.Open("q.c", quiet);
        Assert.That(s.Complete(q, 0, 21)!["items"]!.AsArray(), Is.Empty, "in a comment");
        Assert.That(s.Complete(q, 0, 38)!["items"]!.AsArray(), Is.Empty, "in a string");
        Assert.That(s.Complete(q, 0, 49, "<")!["items"]!.AsArray(), Is.Empty, "'<' of a comparison");
    }

    [Test]
    public void Kasm_Mnemonics_Directives_And_Operands()
    {
        var ws = new TempWorkspace().Write("k/purrtty.kinc", "; constants\n").Write("k/util.kasm", ".func helper\n RET 0\n.endfunc\n");
        using FeatureSession s = Bench(ws);
        const string text = ".include <kfs.kinc>\n.include \"purrtty.kinc\"\n.equ ID, 1\n.func run\n    .local i\nloop:\n    PUS\n    SYSCALL SVC_X_LI\n    JNZ \n" +
            "    PUSH32 sys.t\n    PUSH32 sizeof(v\n.fu\n.include <\n.include \"\n    CALL \n    PUSH32 X_LIGHT_O\n    STL \n.task run \n.endfunc\n";
        string uri = s.Open("k/main.kasm", text);

        JsonObject mnemonics = s.CompleteAtEnd(uri, text, 6)!;
        Assert.That(FeatureSession.Labels(mnemonics), Is.SupersetOf(new[] { "PUSH8", "PUSH16", "PUSH32", "PUSH64" }));
        Assert.That(FeatureSession.Item(mnemonics, "PUSH32")["textEdit"]!["range"]!.ToJsonString(), Is.EqualTo(HoverTests.Range(6, 4, 7)));
        Assert.That(FeatureSession.Item(mnemonics, "PUSH32")["detail"]!.GetValue<string>(), Does.Contain("cycles"));

        JsonObject services = s.CompleteAtEnd(uri, text, 7)!;
        Assert.That(FeatureSession.Labels(services), Is.SupersetOf(new[] { "SVC_X_LIGHT_SET_ON", "SVC_X_LIGHT_COUNT", "SVC_X_LIGHTS_SET_ALL" }));
        JsonObject on = FeatureSession.Item(services, "SVC_X_LIGHT_SET_ON");
        Assert.That(on["detail"]!.GetValue<string>(), Is.EqualTo("= 0x5013 (x_light_set_on)"), "<kfs.kinc> is included: no note");
        Assert.That(on["sortText"]!.GetValue<string>(), Does.StartWith("00"), "services first after SYSCALL");
        Assert.That(on["documentation"]!["value"]!.GetValue<string>(), Does.Contain("Switches a light's part on or off"));

        Assert.That(At(s, uri, text, 8), Is.EqualTo(new[] { "loop" }), "the function's labels");
        Assert.That(At(s, uri, text, 9), Does.Contain("sys.t_s").And.Contain("sys.tick"));
        Assert.That(At(s, uri, text, 10), Is.SupersetOf(new[] { "vec3", "dvec3" }), "types inside sizeof(");
        Assert.That(At(s, uri, text, 11, "."), Is.SupersetOf(new[] { ".func", ".endfunc" }));
        Assert.That(At(s, uri, text, 12, "<"), Is.EquivalentTo(new[] { "kfs.kinc", "vehicle.kinc" }));
        Assert.That(At(s, uri, text, 13, "\""), Is.EquivalentTo(new[] { "purrtty.kinc", "util.kasm" }));
        Assert.That(At(s, uri, text, 14), Is.EqualTo(new[] { "run" }), "functions of the file");
        JsonObject constants = s.CompleteAtEnd(uri, text, 15)!;
        Assert.That(FeatureSession.Item(constants, "X_LIGHT_ON")["detail"]!.GetValue<string>(), Is.EqualTo("= 2"), "purrtty.kinc is included");
        Assert.That(At(s, uri, text, 16), Does.Contain("i").And.Contain("ID"), "slots and .equ names");
        Assert.That(At(s, uri, text, 17), Is.EquivalentTo(new[] { "rate_hz", "divisor", "priority" }));

        // Without the includes, the symbols are still offered, each saying which include it needs, sorted after.
        const string bare = ".func run\n    SYSCALL SVC_X_LIGHT_SET\n    PUSH32 X_LIGHT_O\n    RET 0\n.endfunc\n";
        string b = s.Open("k2/bare.kasm", bare);
        JsonObject needs = s.CompleteAtEnd(b, bare, 1)!;
        JsonObject set = FeatureSession.Item(needs, "SVC_X_LIGHT_SET_ON");
        Assert.That(set["detail"]!.GetValue<string>(), Is.EqualTo("= 0x5013 (x_light_set_on)  (needs .include <kfs.kinc>)"));
        Assert.That(set["sortText"]!.GetValue<string>(), Does.StartWith("10"));
        Assert.That(FeatureSession.Item(s.CompleteAtEnd(b, bare, 2)!, "X_LIGHT_ON")["detail"]!.GetValue<string>(), Is.EqualTo("= 2  (needs .include \"purrtty.kinc\")"));
    }
}
