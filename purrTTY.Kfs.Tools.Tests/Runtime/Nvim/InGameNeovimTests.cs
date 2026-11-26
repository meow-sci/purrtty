using System.Net;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using NUnit.Framework;
using purrTTY.Kfs.Lsp.InGame;
using purrTTY.Kfs.Lsp.Live;
using purrTTY.Kfs.Runtime;
using purrTTY.Kfs.Tools.Tests.Nvim;
using purrTTY.Kfs.Tools.Tests.Runtime.Lsp;

namespace purrTTY.Kfs.Tools.Tests.Runtime.Nvim;

/// <summary>
/// The three pieces together, end to end: a real headless Neovim with the shipped runtime files,
/// launched as <c>KfsNeovimLauncher</c> plans it (<see cref="NeovimLaunchPlan"/>, including
/// <c>PURRTTY_CONFIG_DIR</c>), against the server stack the mod runs (<see cref="InGameStack"/>: the
/// in-game host over a fake game side, the live feed, the output tee, the service core with its token
/// and info file, <c>StandardFeatures</c> + <c>HostFeatures</c>). The test thread is the game's main
/// thread: every wait pumps <see cref="InGameStack.Frame"/> unless a test says otherwise. Config and
/// workspace folders are temporary (their paths contain spaces); skipped silently without Neovim 0.11+.
/// </summary>
[TestFixture]
public sealed class InGameNeovimTests : ToolsRuntimeFixture
{
    private const string Lights = "#include <kfs.h>\n#include <purrtty.h>\n#pragma kfs tick_hz 10\n#pragma kfs task run rate_hz=10\nuint32_t n;\n" +
        "void run(void) { x_light_set_on(0u, X_LIGHT_ON); n = missing + 1u; }\n";

    private const string Needs4 = "#include <kfs.h>\n#include <vehicle.h>\n#pragma kfs tick_hz 10\n#pragma kfs claim ALLOCATION\n#pragma kfs task run rate_hz=10\n" +
        "#if THRUSTER_COUNT < 4\n#error \"this program needs four thrusters\"\n#endif\nvoid run(void) { commands.thruster[THRUSTER_COUNT - 1].pulse_s = 0.1f; }\n";

    private static readonly object FastRetry = new { retry_interval_ms = 100 };

    [OneTimeSetUp]
    public void RequireNeovim() => Neovim.RequireOrIgnore();

    private static string Diag(JsonNode? d) => $"{d!["lnum"]}:{d["col"]}-{d["end_col"]} {d["code"]}";

    private static IEnumerable<string> Diags(JsonNode? list) => (list as JsonArray ?? []).Select(Diag);

    private static List<string> Messages(JsonObject r) => (r["messages"] as JsonArray ?? []).Select(m => m!["msg"]!.GetValue<string>()).ToList();

    private static List<string> Strings(JsonNode? list) => (list as JsonArray ?? []).Select(s => s!.GetValue<string>()).ToList();

    /// <summary>A workspace and a non-default config folder in <paramref name="sb"/>, and the stack over them.</summary>
    private static InGameStack Stack(NvimSandbox sb, out string root, TimeSpan? verbTimeout = null)
    {
        root = sb.Dir("Kitten Space Agency/kfs");
        string config = sb.Dir("cfg dir");
        return InGameStack.Create(Runtime.Tables, root, config, verbTimeout);
    }

    private static (int Row, int Col) HoverAt(string text, string word)
    {
        string[] lines = text.Split('\n');
        int row = Array.FindIndex(lines, l => l.Contains(word, StringComparison.Ordinal));
        return (row, lines[row].IndexOf(word, StringComparison.Ordinal) + 2);
    }

    [Test]
    public void InGame_Attach_Diagnostics_Hover_And_Completion_Through_Neovim()
    {
        using var sb = new NvimSandbox();
        using InGameStack stack = Stack(sb, out string root);
        Assert.That(stack.StartService(), Is.True, stack.Core!.Status);
        string main = sb.Write("Kitten Space Agency/kfs/x_lights/main.c", Lights);
        string crlf = sb.Write("Kitten Space Agency/kfs/crlf/main.c", Lights.Replace("\n", "\r\n", StringComparison.Ordinal));
        string unicode = sb.Write("Kitten Space Agency/kfs/my ünï/main.c", Programs.Undeclared);
        (int row, int col) = HoverAt(Lights, "x_light_set_on");

        using NvimRun run = sb.Start("ingame_features", root, main, stack.Endpoint, new { hover_row = row, hover_col = col, crlf, unicode },
            configDirectory: stack.ConfigDirectory, pump: stack.Frame);
        Assert.That(run.Plan.Environment[NeovimLaunchPlan.ConfigVariable], Is.EqualTo(stack.ConfigDirectory));
        JsonObject r = run.Finish();

        int missing = Lights.Split('\n')[5].IndexOf("missing", StringComparison.Ordinal);
        string[] expected = [$"5:{missing}-{missing + 7} KC-E003"];
        Assert.That(Diags(r["diagnostics"]), Is.EqualTo(expected), () => r.ToJsonString());
        string hover = r["hover"]!.GetValue<string>();
        Assert.That(hover, Does.StartWith("```c\nvoid x_light_set_on(uint32_t light, uint32_t on);\n```"), hover);
        Assert.That(hover, Does.Contain("Service id `0x5013`"));
        Assert.That(Strings(r["completion"]), Is.SupersetOf(new[] { "x_light_count", "x_light_set_on", "x_light_set_color", "x_lights_set_all" }));

        Assert.That(r["crlf"]!["fileformat"]!.GetValue<string>(), Is.EqualTo("dos"));
        Assert.That(Diags(r["crlf"]!["diagnostics"]), Is.EqualTo(expected), "a CRLF file: the same positions");
        Assert.That(r["crlf"]!["hover"]!.GetValue<string>(), Is.EqualTo(hover));

        Assert.That(Diags(r["unicode"]!["diagnostics"]), Is.EqualTo(new[] { "4:21-28 KC-E003" }), "a folder with a space and non-ASCII letters");
        Assert.That(r["unicode"]!["uri"]!.GetValue<string>(), Does.Contain("my%20"));
        Assert.That(r["unicode"]!["same_client"]!.GetValue<bool>(), Is.True);
        Assert.That(r["kfs_clients"]!.GetValue<int>(), Is.EqualTo(1));
        Assert.That(r["topics"] as JsonArray, Is.Empty, "nothing subscribed at attach");
        Assert.That(stack.Feed.Subscribers, Is.Zero);
        Assert.That(r["info"]!.GetValue<string>(), Does.Contain("purrtty: subscribe=true vehicleCommands=true verb=false"));
        Assert.That(Messages(r), Is.Empty, "nothing to say when everything works");

        // The probe before the attach is not news in the game's log.
        Assert.That(stack.LogLines.Where(l => l.StartsWith("Info: ", StringComparison.Ordinal) && l.Contains("tcp#", StringComparison.Ordinal)
            && !l.Contains(": initialized (", StringComparison.Ordinal) && !l.Contains(": closed (", StringComparison.Ordinal)), Is.Empty, stack.Log);
        Assert.That(stack.LogLines.Count(l => l.Contains(": initialized (", StringComparison.Ordinal)), Is.EqualTo(1), stack.Log);
    }

    [Test]
    public void InGame_Commands_Reach_The_Game_On_The_Main_Thread_And_Their_Output_Comes_Back()
    {
        using var sb = new NvimSandbox();
        using InGameStack stack = Stack(sb, out string root);
        string main = sb.Write("Kitten Space Agency/kfs/x_lights/main.c", Programs.Clean);
        string header = sb.Write("Kitten Space Agency/kfs/x_lights/util.h", "#define U 1\n");
        string spaced = sb.Write("Kitten Space Agency/kfs/my prog/main.c", Programs.Clean);
        stack.Game.Vehicle = InGameStack.VehicleTarget(Runtime.Tables, 4);
        stack.Game.DiskProbe = () => File.ReadAllText(main) + "|" + File.ReadAllText(header);
        const string BuildText = "x_lights/main.c:5:22: error KC-E003: 'missing' is not declared\nx_lights/main.c: 1 error(s), 3 ms\n";
        const string Manual = "KC-E003 undeclared name\n\nA name was used before it was declared.\nDeclare it, or include the header that does.\n";
        stack.Game.Print = line => line switch
        {
            "kfs build x_lights/main.c" => BuildText,
            "kfs manual KC-E003" => Manual,
            "purr parts lights" => "light 0: lamp\nlight 1: lamp\n",
            _ => line + ": done\n",
        };
        Assert.That(stack.StartService(), Is.True);

        using NvimRun run = sb.Start("ingame_commands", root, main, stack.Endpoint, new { header, spaced },
            configDirectory: stack.ConfigDirectory, pump: stack.Frame);
        JsonObject r = run.Finish();

        IReadOnlyList<GameLine> ran = stack.Game.Ran;
        Assert.That(ran.Select(l => l.Line), Is.EqualTo(new[]
        {
            "kfs build x_lights/main.c", "kfs load x_lights/main.c", "kfs start", "kfs stop", "kfs reset", "kfs target vehicle",
            "kfs load x_lights/main.c --vehicle", "purr parts lights", "kfs manual KC-E003",
        }), () => r.ToJsonString() + "\n" + stack.Log);
        Assert.That(ran.Select(l => l.Thread).Distinct(), Is.EqualTo(new[] { Environment.CurrentManagedThreadId }), "every line ran on the pumping thread");
        Assert.That(ran[0].Disk, Does.Contain("// edited in main.c").And.Contain("// edited in util.h"), "modified workspace buffers were written before kfs build ran");
        Assert.That((r["modified_after_build"]!["a"]!.GetValue<bool>(), r["modified_after_build"]!["h"]!.GetValue<bool>()), Is.EqualTo((false, false)));

        // ok = the game ran it; its text comes back verbatim (here a build error, shown, never called a success).
        Assert.That(r["build"]!["ok"]!.GetValue<bool>(), Is.True);
        Assert.That(r["build"]!["output"]!.GetValue<string>(), Is.EqualTo(BuildText));
        Assert.That(Strings(r["build_output"]), Is.EqualTo(new[] { "-- :KfsBuild" }.Concat(BuildText.TrimEnd().Split('\n'))));
        Assert.That(r["start"]!["output"]!.GetValue<string>(), Is.EqualTo("kfs start: done\n"));
        Assert.That(r["load_vehicle"]!["ok"]!.GetValue<bool>(), Is.True);
        Assert.That(r["parts"]!["output"]!.GetValue<string>(), Is.EqualTo("light 0: lamp\nlight 1: lamp\n"));
        Assert.That(Strings(r["manual_output"]), Is.EqualTo(new[] { "-- :KfsManual" }.Concat(Manual.TrimEnd().Split('\n'))));

        // The state subscription starts with the first load that ran, not at attach or after a build.
        Assert.That(r["topics_at_attach"] as JsonArray, Is.Empty);
        Assert.That(r["topics_after_build"] as JsonArray, Is.Empty);
        Assert.That(Strings(r["topics_after_load"]), Is.EqualTo(new[] { "state" }));

        // Refusals: one clear warning each, nothing reached the game, nothing written for them.
        Assert.That(r["load_spaced"]!["ok"]!.GetValue<bool>(), Is.False);
        Assert.That(r["verb"]!["ok"]!.GetValue<bool>(), Is.False);
        Assert.That(r["spaced_modified_after_verb"]!.GetValue<bool>(), Is.True, "a verb the server refuses writes nothing first");
        List<string> messages = Messages(r);
        Assert.That(messages, Does.Contain("kFS: :KfsStart: kfs start: done"));
        Assert.That(messages, Does.Contain("kFS: :KfsLoad failed: kfs load splits its arguments at spaces, so it cannot load 'my prog/main.c': rename the file or its folder"));
        Assert.That(messages, Does.Contain("kFS: :KfsVerb failed: kfs.verb is switched off: set lsp_allow_vehicle_commands = true in kfs.toml to run any kfs command from the editor"));
        Assert.That(messages.Count(m => m.Contains("failed", StringComparison.Ordinal)), Is.EqualTo(2), string.Join("\n", messages));
        Assert.That(messages, Has.None.Contains(": done\n"), "no message ends in a raw newline");
    }

    [Test]
    public void InGame_A_Command_While_The_Main_Thread_Does_Not_Pump_Times_Out_Cleanly()
    {
        using var sb = new NvimSandbox();
        using InGameStack stack = Stack(sb, out string root, verbTimeout: TimeSpan.FromSeconds(1));
        string main = sb.Write("Kitten Space Agency/kfs/x_lights/main.c", Lights);
        Assert.That(stack.StartService(), Is.True);
        (int row, int col) = HoverAt(Lights, "x_light_set_on");

        using NvimRun run = sb.Start("ingame_timeout", root, main, stack.Endpoint, new { hover_row = row, hover_col = col },
            configDirectory: stack.ConfigDirectory, pump: stack.Frame);
        run.WaitForSignal("ready");
        run.Signal("not-pumping");
        run.WaitForSignal("timed-out", pump: false);   // a loading screen: no frame runs
        Assert.That(stack.Game.Ran, Is.Empty);
        run.Signal("pumping");
        JsonObject r = run.Finish();

        Assert.That(r["hover_while_waiting"]!.GetValue<bool>(), Is.True, "the server answered other requests meanwhile");
        Assert.That(r["pending_after_hover"]!.GetValue<bool>(), Is.True);
        Assert.That(r["start"]!["ok"]!.GetValue<bool>(), Is.False);
        Assert.That(r["start"]!["error"]!.GetValue<string>(), Does.Contain("kfs start: the game's main thread did not run the command within 1 s").And.Contain("nothing was run"));
        Assert.That(r["waited_ms"]!.GetValue<double>(), Is.GreaterThanOrEqualTo(900).And.LessThan(15000));
        Assert.That(r["stop"]!["ok"]!.GetValue<bool>(), Is.True, "commands work again once frames run");
        Assert.That(stack.Game.Ran.Select(l => l.Line), Is.EqualTo(new[] { "kfs stop" }), "the timed-out start never ran later");
        Assert.That(Messages(r), Does.Contain("kFS: :KfsStart failed: " + r["start"]!["error"]!.GetValue<string>()));
    }

    [Test]
    public void InGame_Log_Tail_Lines_Output_State_And_Trap()
    {
        using var sb = new NvimSandbox();
        using InGameStack stack = Stack(sb, out string root);
        string blink = sb.Write("Kitten Space Agency/kfs/x_blink/blink.c", Programs.Clean);
        stack.Live.Log("[k 0 t 0.00] old 1", "[k 0 t 0.00] old 2", "[k 0 t 0.00] old 3");
        Assert.That(stack.StartService(), Is.True);

        using NvimRun run = sb.Start("ingame_log", root, blink, stack.Endpoint, configDirectory: stack.ConfigDirectory, pump: stack.Frame);
        run.WaitForSignal("attached");
        Assert.That((stack.Feed.Subscribers, stack.Feed.IsPolling), Is.EqualTo((0, false)), "attaching subscribes to nothing: the computer is not asked for snapshots");
        run.Signal("checked-idle");
        run.WaitForSignal("tail-shown");
        stack.Live.Log("[k 1 t 0.10] new 1");
        run.WaitForSignal("started");
        stack.Live.Log("[k 2 t 0.20] new 2");
        stack.Live.State = new LiveState("x_blink", "Running", 7, 0, null);
        run.WaitForSignal("state-seen");
        stack.Live.State = new LiveState("x_blink", "Halted", 9, 0, new LiveTrap("trap DIV_ZERO in task 0, function run", "blink.c", "x_blink/blink.c", 5));
        run.WaitForSignal("trap-shown");
        stack.Live.State = new LiveState("x_blink", "Running", 1, 0, null);
        JsonObject r = run.Finish();

        Assert.That(r["topics_at_attach"] as JsonArray, Is.Empty);
        Assert.That(Strings(r["tail"]), Is.EqualTo(new[] { "-- the program log before this (its last 3 lines) --", "[k 0 t 0.00] old 1", "[k 0 t 0.00] old 2", "[k 0 t 0.00] old 3" }));
        Assert.That(Strings(r["log"]).Skip(4), Is.EqualTo(new[] { "[k 1 t 0.10] new 1", "> kfs start: done", "[k 2 t 0.20] new 2" }), "in order, each once");
        Assert.That(r["status_running"]!.GetValue<string>(), Is.EqualTo("kFS: Running x_blink tick 7"));
        Assert.That(Diags(r["trap"]), Is.EqualTo(new[] { "4:0-30 KFS-TRAP" }), "on the trapping line, trimmed");
        Assert.That(r["trap_message"]!.GetValue<string>(), Does.StartWith("trap DIV_ZERO in task 0, function run: the program halted here"));
        Assert.That(r["status_trap"]!.GetValue<string>(), Is.EqualTo("kFS: Halted x_blink tick 9, trap DIV_ZERO in task 0, function run (blink.c:5)"));
        Assert.That(r["trap_messages"]!.GetValue<int>(), Is.EqualTo(1), "announced once");
        Assert.That(Strings(r["log_end"]), Does.Contain("!! trap DIV_ZERO in task 0, function run (blink.c:5)"), "no doubled \"trap: trap\"");
    }

    [Test]
    public void InGame_Discovery_Through_The_Info_File_And_Service_Restart()
    {
        // Neovim first (the mod's server was off: no PURRTTY_KFS_LSP), the service after it, then
        // `purr lsp restart` with lsp_allow_vehicle_commands now true: new port, new token.
        using var sb = new NvimSandbox();
        using InGameStack stack = Stack(sb, out string root);
        string bad = sb.Write("Kitten Space Agency/kfs/x_a/bad.c", Programs.Undeclared);
        using NvimRun run = sb.Start("ingame_restart", root, bad, server: null, new { launched_without_server = true }, FastRetry,
            configDirectory: stack.ConfigDirectory, pump: stack.Frame);
        run.WaitForSignal("looked");
        Assert.That(stack.StartService(), Is.True);
        LspServerInfo first = LspServerInfoFile.Read(stack.InfoFile)!;
        run.WaitForSignal("attached-1");
        Assert.That(stack.Restart(allowVerb: true), Is.True);
        LspServerInfo second = LspServerInfoFile.Read(stack.InfoFile)!;
        Assert.That((second.Address, second.Token), Is.Not.EqualTo((first.Address, first.Token)));
        run.Signal("restarted");
        JsonObject r = run.Finish();

        string how = "server info file " + stack.InfoFile.Replace('\\', '/');
        Assert.That(r["first_how"]!.GetValue<string>(), Is.EqualTo(how), () => r.ToJsonString());
        Assert.That(r["second_how"]!.GetValue<string>(), Is.EqualTo(how));
        Assert.That(Diags(r["diagnostics2"]), Is.EqualTo(new[] { "4:21-28 KC-E003" }));
        Assert.That(r["verb"]!["ok"]!.GetValue<bool>(), Is.True, r["verb"]!.ToJsonString());
        Assert.That(stack.Game.Ran.Select(l => l.Line), Is.EqualTo(new[] { "kfs start", "kfs state", "kfs start" }));
        Assert.That((r["none"]!.GetValue<int>(), r["gone"]!.GetValue<int>(), r["back"]!.GetValue<int>(), r["refused"]!.GetValue<int>()), Is.EqualTo((1, 1, 1, 0)),
            string.Join("\n", Messages(r)));
    }

    [Test]
    public void InGame_Restart_On_A_Fixed_Port_Uses_The_New_Token_Without_A_Refusal()
    {
        // lsp_port fixed: the restarted server has the address in Neovim's environment, with a new
        // token. The info file (PURRTTY_CONFIG_DIR) says so; the stale environment is skipped.
        using var sb = new NvimSandbox();
        using InGameStack stack = Stack(sb, out string root);
        string bad = sb.Write("Kitten Space Agency/kfs/x_a/bad.c", Programs.Undeclared);
        int port = FreePort();
        Assert.That(stack.StartService(port: port), Is.True, stack.Core!.Status);
        using NvimRun run = sb.Start("ingame_restart", root, bad, stack.Endpoint, options: FastRetry,
            configDirectory: stack.ConfigDirectory, pump: stack.Frame);
        run.WaitForSignal("attached-1");
        string oldToken = stack.Core!.Token!;
        if (!stack.Restart(allowVerb: true, port: port)) Assert.Fail("the fixed port could not be bound again: " + stack.Core?.Status);
        Assert.That(stack.Core!.Token, Is.Not.EqualTo(oldToken));
        run.Signal("restarted");
        JsonObject r = run.Finish();

        Assert.That(r["first_how"]!.GetValue<string>(), Does.StartWith("environment"));
        Assert.That(r["second_how"]!.GetValue<string>(), Does.StartWith("server info file"));
        Assert.That(r["tried"]!.ToJsonString(), Does.Contain("superseded by"));
        Assert.That(r["refused"]!.GetValue<int>(), Is.Zero, "no 'refused this session' for a restart: " + string.Join("\n", Messages(r)));
        Assert.That(stack.Game.Ran.Select(l => l.Line), Is.EqualTo(new[] { "kfs start", "kfs state", "kfs start" }));
        Assert.That(stack.LogLines.Count(l => l.Contains("with a wrong token", StringComparison.Ordinal)), Is.Zero, stack.Log);
    }

    [Test]
    public void InGame_A_Target_Change_Rediagnoses_Two_Neovims_At_Once()
    {
        using var sb = new NvimSandbox();
        using var sb2 = new NvimSandbox();   // its own XDG folders: no swap-file clash on the shared file
        using InGameStack stack = Stack(sb, out string root);
        string file = sb.Write("Kitten Space Agency/kfs/x_v/needs4.c", Needs4);
        stack.Game.Next = InGameStack.VehicleTarget(Runtime.Tables, 1);
        stack.Frame();
        Assert.That(stack.StartService(), Is.True);

        using NvimRun one = sb.Start("ingame_target", root, file, stack.Endpoint, configDirectory: stack.ConfigDirectory, pump: stack.Frame);
        using NvimRun two = sb2.Start("ingame_target", root, file, stack.Endpoint, configDirectory: stack.ConfigDirectory, pump: stack.Frame);
        one.WaitForSignal("error-1");
        two.WaitForSignal("error-1");
        stack.Game.Next = InGameStack.VehicleTarget(Runtime.Tables, 4);
        stack.Frame();
        one.Signal("switched-4");
        two.Signal("switched-4");
        one.WaitForSignal("clean");
        two.WaitForSignal("clean");
        stack.Game.Next = InGameStack.VehicleTarget(Runtime.Tables, 2);
        stack.Frame();
        one.Signal("switched-2");
        two.Signal("switched-2");
        foreach (NvimRun run in new[] { one, two })
        {
            JsonObject r = run.Finish();
            Assert.That((r["first"]!.GetValue<string>(), r["last"]!.GetValue<string>()), Is.EqualTo(("KC-E014", "KC-E014")));
        }
        Assert.That(stack.LogLines.Count(l => l.Contains(": initialized (", StringComparison.Ordinal)), Is.EqualTo(2), "two clients, one server");
    }

    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }
}
