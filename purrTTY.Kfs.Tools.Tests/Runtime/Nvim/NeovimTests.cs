using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using NUnit.Framework;
using purrTTY.Kfs.Runtime;
using purrTTY.Kfs.Tools.Tests.Nvim;
using purrTTY.Kfs.Tools.Tests.Runtime.Lsp;

namespace purrTTY.Kfs.Tools.Tests.Runtime.Nvim;

/// <summary>
/// The shipped Neovim runtime files (<c>purrTTY.GameMod/nvim/</c>, plan §7.4) in a real headless
/// Neovim, launched exactly as <c>KfsNeovimLauncher</c> plans it (<see cref="NeovimLaunchPlan"/>:
/// <c>--cmd</c> entry, environment, working directory) from a runtime folder whose path contains
/// spaces, against real language servers on loopback port 0. Every Neovim gets its own HOME and XDG
/// folders (<see cref="NvimSandbox"/>), so the user's configuration is neither read nor touched.
/// Skipped silently without <c>nvim</c> 0.11+ on PATH.
/// </summary>
[TestFixture]
public sealed class NeovimTests : ToolsRuntimeFixture
{
    private const string Kasm = ".include <kfs.kinc>\n.tick_hz 10\n.task main rate_hz=10\n.func main\n    PUSH32 X_NOT_A_CONSTANT   \n    DROP\n    RET 0\n.endfunc\n; a comment\n";

    /// <summary>Fast reconnects for the tests (the default is 3 s).</summary>
    private static readonly object FastRetry = new { retry_interval_ms = 100 };

    [OneTimeSetUp]
    public void RequireNeovim() => Neovim.RequireOrIgnore();

    private static string Token() => "tok-" + Guid.NewGuid().ToString("N");

    private static JsonArray Array(JsonObject r, string name) => r[name] as JsonArray ?? [];

    private static string Diag(JsonNode? d) => $"{d!["lnum"]}:{d["col"]}-{d["end_col"]} {d["code"]}";

    [Test]
    public void Neovim_Attaches_Headless()
    {
        using var sb = new NvimSandbox();
        string root = sb.Dir("Kitten Space Agency/kfs");
        string bad = sb.Write("Kitten Space Agency/kfs/bad.c", Programs.Undeclared);
        string ok = sb.Write("Kitten Space Agency/kfs/ok.c", Programs.Clean);
        string kasm = sb.Write("Kitten Space Agency/kfs/prog.kasm", Kasm);
        string outside = sb.Write("elsewhere/plain.c", "int main(void) { return 0; }\n");
        string outsideKasm = sb.Write("elsewhere/lib.KASM", "NOP\n");
        using var server = FakeKfsServer.Start(Runtime.Tables, root, Token());

        using NvimRun run = sb.Start("attach", root, bad, server.Endpoint, new { ok, kasm, outside, outside_kasm = outsideKasm });
        Assert.That(run.Plan.NeovimArguments, Is.EqualTo(new[] { "--cmd", NeovimLaunchPlan.EntryCommand, "--", bad }));
        Assert.That(run.Plan.Environment[NeovimLaunchPlan.RuntimeVariable], Does.Contain(" "), "the runtime folder's path has a space");
        JsonObject r = run.Finish();

        Assert.That(r["bad"]!["ft"]!.GetValue<string>(), Is.EqualTo("kc"));
        Assert.That(r["bad"]!["encoding"]!.GetValue<string>(), Is.EqualTo("utf-16"));
        Assert.That((r["bad"]!["diagnostics"] as JsonArray)!.Select(Diag), Is.EqualTo(new[] { "4:21-28 KC-E003" }), () => r.ToJsonString());
        Assert.That(r["kc"]!["syntax"]!.GetValue<string>(), Is.EqualTo("c"));
        Assert.That(r["kc"]!["commentstring"]!.GetValue<string>(), Is.EqualTo("// %s"));
        Assert.That(r["kc"]!["cindent"]!.GetValue<bool>(), Is.True);

        Assert.That(r["ok"]!["ft"]!.GetValue<string>(), Is.EqualTo("kc"));
        Assert.That(r["ok"]!["same_client"]!.GetValue<bool>(), Is.True, "a second buffer shares the client");

        Assert.That(r["kasm"]!["ft"]!.GetValue<string>(), Is.EqualTo("kasm"));
        Assert.That(r["kasm"]!["same_client"]!.GetValue<bool>(), Is.True);
        Assert.That((r["kasm"]!["diagnostics"] as JsonArray)!.Select(Diag), Is.EqualTo(new[] { "4:4-27 KA-E003" }), () => r.ToJsonString());
        Assert.That(r["kasm"]!["commentstring"]!.GetValue<string>(), Is.EqualTo("; %s"));
        Assert.That(r["kasm"]!["syntax"]!.GetValue<string>(), Is.EqualTo("kasm"));
        JsonObject groups = r["kasm"]!["groups"]!.AsObject();
        Assert.That(groups.ToDictionary(g => g.Key, g => g.Value!.GetValue<string>()), Is.EquivalentTo(new Dictionary<string, string>
        {
            ["directive"] = "kasmDirective", ["mnemonic"] = "kasmMnemonic", ["constant"] = "kasmConstant", ["comment"] = "kasmComment",
        }), "the shipped syntax file is in effect");

        Assert.That(r["outside"]!["ft"]!.GetValue<string>(), Is.EqualTo("c"), "C files outside the workspace stay C");
        Assert.That(r["outside"]!["clients"]!.GetValue<int>(), Is.Zero);
        Assert.That(r["outside_kasm"]!["ft"]!.GetValue<string>(), Is.EqualTo("kasm"), "kASM everywhere, any letter case");
        Assert.That(r["outside_kasm"]!["clients"]!.GetValue<int>(), Is.Zero, "but attached only inside a workspace");
        Assert.That(r["treesitter_lang"]!.GetValue<string>(), Is.EqualTo("c"), "the C parser is registered for kc");
        Assert.That(r["kfs_clients"]!.GetValue<int>(), Is.EqualTo(1), "one client for the workspace");
        Assert.That(r["status"]!.GetValue<string>(), Is.EqualTo("kFS: connected"));
        Assert.That(r["info"]!.GetValue<string>(), Does.Contain("via environment (PURRTTY_KFS_LSP=" + server.Address + ")"));
        Assert.That(r["setup_calls"]!.GetValue<int>(), Is.EqualTo(1));
        Assert.That(Array(r, "commands").Select(c => c!.GetValue<string>()), Is.EqualTo(new[]
        {
            "KfsBuild", "KfsInfo", "KfsLoad", "KfsLog", "KfsManual", "KfsParts", "KfsReset", "KfsRestart", "KfsStart", "KfsStop", "KfsTarget", "KfsVerb",
        }));
        Assert.That(Array(r, "messages"), Is.Empty, "nothing to say when everything works");
        Assert.That(server.Initializes, Is.EqualTo(1));
        Assert.That(r["token_env"], Is.Null, "the token is not left in Neovim's environment (:terminal, :!, plugins)");
        Assert.That(r["state_text"]!.GetValue<string>(), Does.Not.Contain(server.Endpoint.Token), "nor in the state other plugins can read");
        Assert.That(r["info"]!.GetValue<string>(), Does.Not.Contain(server.Endpoint.Token));
    }

    [Test]
    public void Configured_And_Relative_Roots_Are_Expanded()
    {
        using var sb = new NvimSandbox();
        string root = sb.Dir("ws/kfs");
        string inRoot = sb.Write("ws/kfs/here.c", "int x;\n");
        string home = sb.Write("home/kws/t.c", "int y;\n");
        string outside = sb.Write("elsewhere/plain.c", "int z;\n");
        // root = '~/kws' (HOME is the sandbox's), and PURRTTY_KFS_ROOT relative to the working
        // directory the launcher sets (the workspace root).
        using NvimRun run = sb.Start("roots", root, inRoot, server: null, new { files = new[] { inRoot, home, outside } },
            options: new { root = "~/kws", stdio = false }, environment: new Dictionary<string, string?> { [NeovimLaunchPlan.RootVariable] = "." });
        JsonObject r = run.Finish();
        Assert.That(Array(r, "ft").Select(f => f!.GetValue<string>()), Is.EqualTo(new[] { "kc", "kc", "c" }), () => r.ToJsonString());
        Assert.That(Array(r, "roots").Select(x => x!.GetValue<string>()), Has.Some.Contains(Canonical(Path.Combine(sb.Home, "kws")).Replace('\\', '/')).Or.Some.Contains(Path.Combine(sb.Home, "kws").Replace('\\', '/')),
            () => r.ToJsonString());
    }

    [Test]
    public void A_Peer_That_Never_Answers_Initialize_Is_Given_Up_And_Not_Announced()
    {
        using var sb = new NvimSandbox();
        string root = sb.Dir("ws/kfs");
        string bad = sb.Write("ws/kfs/bad.c", Programs.Undeclared);
        string config = sb.Dir("cfg");
        using var silent = new Peer(closeAtOnce: false);
        using NvimRun run = sb.Start("peers", root, bad, new KfsLspEndpoint(silent.Address, "tok"),
            options: new { retry_interval_ms = 100, initialize_timeout_ms = 300, max_attempts = 2, stdio = false }, configDirectory: config);
        run.WaitForSignal("failed");
        using var server = FakeKfsServer.Start(Runtime.Tables, root, Token(), "real");
        sb.Write("cfg/.purrTTY/kfs-lsp.json", server.InfoJson());
        run.Signal("server-written");
        JsonObject r = run.Finish();

        Assert.That(Array(r, "failed").Select(f => f!.GetValue<string>()), Has.Some.StartsWith("no answer to initialize within 300 ms 2 times in a row"), () => r.ToJsonString());
        Assert.That(r["server"]!.GetValue<string>(), Is.EqualTo("real"), "attached once a real server appeared");
        Assert.That(r["gone_messages"]!.GetValue<int>(), Is.Zero, "nothing was connected: no loss to announce");
        Assert.That(r["connecting"]!.GetValue<bool>(), Is.False);
        Assert.That(silent.Accepted, Is.EqualTo(4), "a probe and a connection for each of the two attempts, then no more");
    }

    [Test]
    public void A_Peer_That_Closes_Before_Initialize_Is_Not_Retried_Forever()
    {
        using var sb = new NvimSandbox();
        string root = sb.Dir("ws/kfs");
        string bad = sb.Write("ws/kfs/bad.c", Programs.Undeclared);
        string config = sb.Dir("cfg");
        using var closing = new Peer(closeAtOnce: true);
        using NvimRun run = sb.Start("peers", root, bad, new KfsLspEndpoint(closing.Address, "tok"),
            options: new { retry_interval_ms = 100, initialize_timeout_ms = 500, max_attempts = 3, stdio = false }, configDirectory: config);
        run.WaitForSignal("failed");
        int accepted = closing.Accepted;
        using var server = FakeKfsServer.Start(Runtime.Tables, root, Token(), "real");
        sb.Write("cfg/.purrTTY/kfs-lsp.json", server.InfoJson());
        run.Signal("server-written");
        JsonObject r = run.Finish();

        Assert.That(Array(r, "failed").Select(f => f!.GetValue<string>()), Has.Some.Contains("3 times in a row"), () => r.ToJsonString());
        Assert.That(r["gone_messages"]!.GetValue<int>(), Is.Zero, "a connection that never initialized is not announced as lost");
        Assert.That(accepted, Is.EqualTo(6), "a probe and a connection for each of the three attempts");
        Assert.That(closing.Accepted, Is.EqualTo(accepted), "and none after it was marked failed");
        Assert.That(r["server"]!.GetValue<string>(), Is.EqualTo("real"));
    }

    [Test]
    public void Log_Buffer_Deleted_Or_Unloaded_Is_Gone_And_Comes_Back_Proper()
    {
        using var sb = new NvimSandbox();
        string root = sb.Dir("ws/kfs");
        string a = sb.Write("ws/kfs/a.c", Programs.Clean);
        using NvimRun run = sb.Start("logbuf", root, a, server: null, options: new { stdio = false });
        JsonObject r = run.Finish();
        foreach (string how in new[] { "bdelete", "bunload" })
        {
            JsonNode x = r[how]!;
            Assert.That(x["gone"]!.GetValue<bool>(), Is.True, how);
            Assert.That(x["left"]!.GetValue<int>(), Is.Zero, $"{how}: no buffer of that name was brought back by an append");
            Assert.That(x["buftype"]!.GetValue<string>(), Is.EqualTo("nofile"), how);
            Assert.That(x["modifiable"]!.GetValue<bool>(), Is.False, how);
            Assert.That((x["lines"] as JsonArray)!.Select(l => l!.GetValue<string>()), Is.EqualTo(new[] { "after " + how }), $"{how}: a fresh buffer, no leading blank line");
        }
    }

    [Test]
    public void Build_Sends_Exactly_The_Named_File()
    {
        using var sb = new NvimSandbox();
        string root = sb.Dir("ws/kfs");
        string main = sb.Write("ws/kfs/main.c", Programs.Clean);
        string t1 = sb.Write("ws/kfs/t1.c", Programs.Clean);
        string tBracket = sb.Write("ws/kfs/t[1].c", Programs.Clean);
        string orig = sb.Write("ws/kfs/a.c.orig", "old\n");
        string aC = sb.Write("ws/kfs/a.c", Programs.Clean);
        using var server = FakeKfsServer.Start(Runtime.Tables, root, Token());
        // t1.c and a.c.orig are loaded; t[1].c and a.c are not.
        using NvimRun run = sb.Start("exact", root, main, server.Endpoint, new { loaded = new[] { t1, orig }, build = new[] { tBracket, aC } });
        run.Finish();
        var sent = server.Commands.Select(c => Arguments(c.Arguments)).ToList();
        Assert.That(sent, Is.EqualTo(new[] { $"[file:{Canonical(tBracket)}]", $"[file:{Canonical(aC)}]" }));
    }

    [Test]
    public void Server_Strings_Are_Cleaned_Before_They_Reach_Buffers_Messages_And_The_Status()
    {
        using var sb = new NvimSandbox();
        string root = sb.Dir("ws/kfs");
        string a = sb.Write("ws/kfs/a.c", Programs.Clean);
        using var server = FakeKfsServer.Start(Runtime.Tables, root, Token());
        using NvimRun run = sb.Start("sanitize", root, a, server.Endpoint);
        run.WaitForSignal("ready");
        Assert.That(FakeKfsServer.WaitUntil(() => server.Subscriptions.Contains("log,output,state")), Is.True);
        string longLine = new string('x', 3000);
        Broadcast(server, "purrtty/kfsLog", new JsonObject { ["lines"] = new JsonArray("\u001b[31mred\u001b[0m and \u0007bell", "two\nlines\r\nhere", longLine) });
        Broadcast(server, "purrtty/kfsOutput", new JsonObject { ["text"] = "out\u001b]0;title\u0007put" });
        JsonObject trap = new() { ["message"] = "trap DIV_ZERO in task 0,\nfunction step", ["uri"] = new Uri(a).AbsoluteUri, ["line"] = 2 };
        Broadcast(server, "purrtty/kfsState", new JsonObject { ["program"] = "a\n.c", ["state"] = "Halted", ["tick"] = 7, ["overruns"] = 0, ["trap"] = trap });
        Broadcast(server, "purrtty/kfsLog", new JsonObject { ["lines"] = new JsonArray("end") });
        JsonObject r = run.Finish();

        var lines = Array(r, "lines").Select(l => l!.GetValue<string>()).ToList();
        Assert.That(lines.Take(4), Is.EqualTo(new[] { "red and ?bell", "two", "lines", "here" }), string.Join(" | ", lines));
        Assert.That(lines[4], Has.Length.EqualTo(2003).And.EndsWith("..."), "a very long line is cut");
        Assert.That(lines, Does.Contain("> output"));
        Assert.That(lines, Does.Contain("!! trap DIV_ZERO in task 0, function step (a.c:3)"));
        Assert.That(r["status"]!.GetValue<string>(), Is.EqualTo("kFS: Halted a .c tick 7, trap DIV_ZERO in task 0, function step (a.c:3)"));
        Assert.That(Array(r, "trap_messages"), Has.Count.EqualTo(1));
        Assert.That(Array(r, "errors"), Is.Empty, () => r.ToJsonString());
    }

    [Test]
    public void New_File_Folders_Are_Made_At_The_First_Write_Inside_The_Workspace_Only()
    {
        using var sb = new NvimSandbox();
        string root = sb.Dir("ws/kfs");
        string inside = Path.Combine(root, "new", "deep", "n.c");
        string outside = Path.Combine(sb.Base, "elsewhere", "missing", "o.c");
        using NvimRun run = sb.Start("newfile", root, inside, server: null, new { inside, outside }, options: new { stdio = false });
        JsonObject r = run.Finish();
        Assert.That((r["inside_ok"]!.GetValue<bool>(), r["inside_exists"]!.GetValue<bool>()), Is.EqualTo((true, true)), () => r.ToJsonString());
        Assert.That(File.ReadAllText(inside), Is.EqualTo("int x;\n"));
        Assert.That((r["outside_ok"]!.GetValue<bool>(), r["outside_dir"]!.GetValue<bool>()), Is.EqualTo((false, false)), "outside the workspace Neovim behaves as always");
    }

    [Test]
    public void Buffer_Options_Can_Be_Left_To_The_Player()
    {
        using var sb = new NvimSandbox();
        string root = sb.Dir("ws/kfs");
        string a = sb.Write("ws/kfs/a.c", Programs.Clean);
        using var server = FakeKfsServer.Start(Runtime.Tables, root, Token());
        using NvimRun run = sb.Start("options", root, a, server.Endpoint, options: new { buffer_options = false });
        JsonObject r = run.Finish();
        Assert.That(r["ft"]!.GetValue<string>(), Is.EqualTo("kc"));
        Assert.That(r["marker"], Is.Null, "purrTTY's buffer set-up did not run");
        Assert.That(r["cindent"]!.GetValue<bool>(), Is.False);
        Assert.That(r["commentstring"]!.GetValue<string>(), Is.Not.EqualTo("// %s"));
    }

    [Test]
    public void Kasm_Mnemonics_Are_Highlighted_At_Statement_Position_Only()
    {
        using var sb = new NvimSandbox();
        string root = sb.Dir("ws/kfs");
        string file = sb.Write("ws/kfs/names.kasm", string.Join("\n",
            "copy:",                    // 1: a label named like a mnemonic
            "loop: COPY",               // 2: a mnemonic after a label
            "    PUSH32 index",         // 3: an operand named like one
            "    zero",                 // 4: ZERO at statement position is one
            ".func enter",              // 5: a directive's argument
            "    PUSH8 ';'",            // 6: kASM has no character literals: ';' starts a comment, as in the assembler
            "    ret;done",             // 7: a mnemonic right before a comment, any letter case
            "    ADD64 x",              // 8
            "    ADDX y",               // 9: not a mnemonic
            ""));
        object[][] probes =
        [
            [1, 1, "copy"], [2, 7, "COPY"], [3, 5, "PUSH32"], [3, 12, "index"], [4, 5, "zero"], [5, 7, "enter"],
            [6, 12, ";"], [7, 5, "ret"], [7, 8, ";"], [8, 5, "ADD64"], [9, 5, "ADDX"],
        ];
        using NvimRun run = sb.Start("kasm_syntax", root, file, server: null, new { probes });
        JsonObject r = run.Finish();

        var seen = Array(r, "groups").Select(g => $"{g!["at"]} {g["text"]}={g["group"]}").ToList();
        Assert.That(seen, Is.EqualTo(new[]
        {
            "1:1 copy=kasmLabel", "2:7 COPY=kasmMnemonic", "3:5 PUSH32=kasmMnemonic", "3:12 index=", "4:5 zero=kasmMnemonic",
            "5:7 enter=", "6:12 ;=kasmComment", "7:5 ret=kasmMnemonic", "7:8 ;=kasmComment", "8:5 ADD64=kasmMnemonic", "9:5 ADDX=",
        }), r.ToJsonString());
    }

    [Test]
    public void Discovery_Uses_The_Server_Info_File()
    {
        // (1) Outside the game, nothing in the environment: the default sibling location under the
        // user's Documents folder, which also makes the default workspace a root.
        using (var sb = new NvimSandbox())
        {
            string root = Path.Combine(sb.GameFolder, "kfs");
            Directory.CreateDirectory(root);
            string bad = Path.Combine(root, "bad.c");
            File.WriteAllText(bad, Programs.Undeclared);
            using var server = FakeKfsServer.Start(Runtime.Tables, root, Token(), "default");
            sb.Write(Path.GetRelativePath(sb.Base, Path.Combine(sb.GameFolder, ".purrTTY", "kfs-lsp.json")), server.InfoJson());
            using NvimRun run = sb.Start("discover", root, bad, server: null, environment: new Dictionary<string, string?> { [NeovimLaunchPlan.RootVariable] = null });
            JsonObject r = run.Finish();
            Assert.That(r["kind"]!.GetValue<string>(), Is.EqualTo("info"), () => r.ToJsonString());
            Assert.That(r["server"]!.GetValue<string>(), Is.EqualTo("default"));
            Assert.That(r["info"]!.GetValue<string>(), Does.Contain("(from server info file ").Or.Contain("(from default workspace location)"));
            Assert.That(Array(r, "messages"), Is.Empty);
        }

        // (2) PURRTTY_CONFIG_DIR names the folder holding .purrTTY; the workspace is elsewhere and
        // known only from the file's workspace_root.
        using (var sb = new NvimSandbox())
        {
            string root = sb.Dir("my ws/kfs");
            string bad = sb.Write("my ws/kfs/bad.c", Programs.Undeclared);
            using var server = FakeKfsServer.Start(Runtime.Tables, root, Token(), "configdir");
            string config = sb.Dir("cfg dir");
            sb.Write("cfg dir/.purrTTY/kfs-lsp.json", server.InfoJson());
            using NvimRun run = sb.Start("discover", root, bad, server: null,
                environment: new Dictionary<string, string?> { [NeovimLaunchPlan.RootVariable] = null, ["PURRTTY_CONFIG_DIR"] = config });
            JsonObject r = run.Finish();
            Assert.That(r["kind"]!.GetValue<string>(), Is.EqualTo("info"));
            Assert.That(r["server"]!.GetValue<string>(), Is.EqualTo("configdir"));
            Assert.That(r["how"]!.GetValue<string>(), Does.Contain("cfg dir/.purrTTY/kfs-lsp.json"));
        }

        // (3) Launched by the mod while its server was not running (no PURRTTY_KFS_LSP), then it
        // started: the file beside PURRTTY_KFS_ROOT. This server has no in-game capabilities, so the
        // commands say plainly what is missing.
        using (var sb = new NvimSandbox())
        {
            string root = sb.Dir("Kitten Space Agency/kfs");
            string bad = sb.Write("Kitten Space Agency/kfs/bad.c", Programs.Undeclared);
            using var server = FakeKfsServer.Start(Runtime.Tables, root, Token(), "sibling", inGame: false);
            sb.Write("Kitten Space Agency/.purrTTY/kfs-lsp.json", server.InfoJson());
            using NvimRun run = sb.Start("discover", root, bad, server: null, new { commands = true });
            JsonObject r = run.Finish();
            Assert.That(r["server"]!.GetValue<string>(), Is.EqualTo("sibling"));
            Assert.That(r["build"]!["ok"]!.GetValue<bool>(), Is.False);
            Assert.That(r["build"]!["error"]!.GetValue<string>(), Does.Contain("offers diagnostics, hover and completion only"));
            Assert.That(Array(r, "log_messages"), Has.Count.EqualTo(1));
            Assert.That(server.Commands, Is.Empty, "nothing was sent");
        }
    }

    [Test]
    public void Discovery_Falls_Back_To_Purr_Kfs_On_Path()
    {
        // The wrapper is a POSIX shell script; on Windows the fallback would need a .cmd shim.
        if (OperatingSystem.IsWindows()) Assert.Ignore();
        string? dotnet = DotnetHost();
        // No dotnet executable next to the running runtime: cannot start the built CLI.
        if (dotnet is null) Assert.Ignore();

        using var sb = new NvimSandbox();
        string root = sb.Dir("ws/kfs");
        string bad = sb.Write("ws/kfs/bad.c", Programs.Undeclared);
        string wrapper = Path.Combine(sb.Bin, "purr-kfs");
        File.WriteAllText(wrapper, $"#!/bin/sh\nexec {Sh(dotnet)} {Sh(Path.Combine(AppContext.BaseDirectory, "purr-kfs.dll"))} \"$@\"\n");
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(wrapper, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        using NvimRun run = sb.Start("discover", root, bad, server: null, new { commands = true });
        JsonObject r = run.Finish();
        Assert.That(r["kind"]!.GetValue<string>(), Is.EqualTo("stdio"), () => r.ToJsonString());
        Assert.That(r["how"]!.GetValue<string>(), Does.Contain(wrapper));
        Assert.That((r["diagnostics"] as JsonArray)!.Select(Diag), Is.EqualTo(new[] { "4:21-28 KC-E003" }), "the standalone server's own diagnostics");

        // purr-kfs lsp registers the kfs.* commands and refuses them itself: its answer is the one
        // message; nothing is written for a command the server said it cannot run.
        string refusal = purrTTY.Kfs.Lsp.Features.KfsCommandProvider.NeedsTheGame("kfs.build");
        Assert.That(r["build"]!["ok"]!.GetValue<bool>(), Is.False);
        Assert.That(r["build"]!["error"]!.GetValue<string>(), Is.EqualTo(refusal));
        Assert.That(r["modified_after_build"]!.GetValue<bool>(), Is.True, "the unsaved buffer was not written");
        var messages = (r["messages"] as JsonArray)!.Select(m => m!["msg"]!.GetValue<string>()).ToList();
        Assert.That(messages.Where(m => m.Contains(":KfsBuild", StringComparison.Ordinal)), Is.EqualTo(new[] { "kFS: :KfsBuild failed: " + refusal }));
        Assert.That(r["manual"]!["ok"]!.GetValue<bool>(), Is.True, "kfs.manual is answered from the tables outside the game");
        Assert.That(r["manual"]!["output"]!.GetValue<string>(), Does.Contain("E003"));
        Assert.That(Array(r, "log_messages"), Has.Count.EqualTo(1), ":KfsLog says the standalone server sends no program log (subscribe=false)");
    }

    [Test]
    public void Nothing_Available_Leaves_Plain_Buffers_And_One_Message()
    {
        using var sb = new NvimSandbox();
        string root = sb.Dir("ws/kfs");
        string bad = sb.Write("ws/kfs/bad.c", Programs.Undeclared);
        using NvimRun run = sb.Start("none", root, bad, server: null, options: FastRetry);
        JsonObject r = run.Finish();
        Assert.That(r["ft"]!.GetValue<string>(), Is.EqualTo("kc"));
        Assert.That(r["syntax"]!.GetValue<string>(), Is.EqualTo("c"));
        Assert.That(r["clients"]!.GetValue<int>(), Is.Zero);
        Assert.That(r["none_messages"]!.GetValue<int>(), Is.EqualTo(1), () => r.ToJsonString());
        Assert.That(r["build"]!["ok"]!.GetValue<bool>(), Is.False);
        Assert.That(r["build"]!["error"]!.GetValue<string>(), Does.Contain("not connected"));
        Assert.That(Array(r, "messages").Count, Is.EqualTo(2), "the one discovery message and the :KfsBuild answer: " + r.ToJsonString());
        Assert.That(r["info"]!.GetValue<string>(), Does.Contain("purr-kfs on PATH: not found").And.Contain("kfs-lsp.json: none"));
        Assert.That(r["timer_while_open"]!.GetValue<bool>(), Is.True, "it keeps looking while a kC buffer is open");
    }

    [Test]
    public void Stale_Info_Files_Are_Ignored_Until_A_Server_Appears()
    {
        using var sb = new NvimSandbox();
        string root = sb.Dir("ws/kfs");
        string bad = sb.Write("ws/kfs/bad.c", Programs.Undeclared);
        string config = sb.Dir("cfg");
        string info = Path.Combine(config, ".purrTTY", "kfs-lsp.json");
        Directory.CreateDirectory(Path.GetDirectoryName(info)!);
        using var server = FakeKfsServer.Start(Runtime.Tables, root, Token(), "appeared");

        // A live server, but the file's process is gone: stale.
        File.WriteAllText(info, server.InfoJson(DeadPid()));
        using NvimRun run = sb.Start("appear", root, bad, server: null, options: FastRetry,
            environment: new Dictionary<string, string?> { [NeovimLaunchPlan.RootVariable] = null, ["PURRTTY_CONFIG_DIR"] = config });
        run.WaitForSignal("dead-pid-skipped");

        // A live process, but nothing listens on the address: stale.
        File.WriteAllText(info, server.InfoJson().Replace(server.Address, "127.0.0.1:" + ClosedPort(), StringComparison.Ordinal));
        run.Signal("refused-port-written");
        run.WaitForSignal("refused-port-skipped");
        Assert.That(server.Initializes, Is.Zero, "no stale file was used");

        File.WriteAllText(info, server.InfoJson());
        run.Signal("good-written");
        JsonObject r = run.Finish();
        Assert.That(r["clients_dead_pid"]!.GetValue<int>(), Is.Zero);
        Assert.That(r["clients_refused"]!.GetValue<int>(), Is.Zero);
        Assert.That(r["server"]!.GetValue<string>(), Is.EqualTo("appeared"));
        Assert.That(r["kind"]!.GetValue<string>(), Is.EqualTo("info"));
        Assert.That((r["diagnostics"] as JsonArray)!.Select(Diag), Is.EqualTo(new[] { "4:21-28 KC-E003" }));
        Assert.That(r["none_messages"]!.GetValue<int>(), Is.EqualTo(1));
        Assert.That(server.Initializes, Is.EqualTo(1));
    }

    [Test]
    public void Server_Restart_Detaches_And_Reattaches()
    {
        using var sb = new NvimSandbox();
        string root = sb.Dir("Kitten Space Agency/kfs");
        string bad = sb.Write("Kitten Space Agency/kfs/bad.c", Programs.Undeclared);
        string second = sb.Write("Kitten Space Agency/kfs/ok.c", Programs.Clean);
        string info = Path.Combine(sb.Base, "Kitten Space Agency", ".purrTTY", "kfs-lsp.json");
        Directory.CreateDirectory(Path.GetDirectoryName(info)!);

        var a = FakeKfsServer.Start(Runtime.Tables, root, Token(), "A");
        FakeKfsServer? b = null;
        try
        {
            File.WriteAllText(info, a.InfoJson());
            using NvimRun run = sb.Start("restart", root, bad, a.Endpoint, new { second }, FastRetry);
            run.WaitForSignal("attached-a");

            // The game quits: its server stops and removes the info file.
            a.Dispose();
            File.Delete(info);
            run.Signal("a-stopped");
            run.WaitForSignal("detached");

            // The game is back: another port, another token, a new info file. The environment still
            // names the old address, which no longer answers.
            b = FakeKfsServer.Start(Runtime.Tables, root, Token(), "B");
            Assert.That(b.Port, Is.Not.EqualTo(a.Port));
            File.WriteAllText(info, b.InfoJson());
            run.Signal("b-started");
            JsonObject r = run.Finish();

            Assert.That(r["first"]!.GetValue<string>(), Is.EqualTo("A"));
            Assert.That(r["second"]!.GetValue<string>(), Is.EqualTo("B"));
            Assert.That(r["same_client"]!.GetValue<bool>(), Is.True, "both buffers on the one new client");
            Assert.That(r["new_client"]!.GetValue<bool>(), Is.True);
            Assert.That((r["diagnostics"] as JsonArray)!.Select(Diag), Is.EqualTo(new[] { "4:21-28 KC-E003" }));
            Assert.That(r["gone_messages"]!.GetValue<int>(), Is.EqualTo(1));
            Assert.That(r["back_messages"]!.GetValue<int>(), Is.EqualTo(1));
            Assert.That(r["none_messages"]!.GetValue<int>(), Is.Zero, "the loss was already announced");
            Assert.That(r["restart_same"]!.GetValue<bool>(), Is.True, ":KfsRestart reconnects both buffers to one client");
            Assert.That(b.Initializes, Is.EqualTo(2), "the reattach and :KfsRestart");
        }
        finally
        {
            a.Dispose();
            b?.Dispose();
        }
    }

    [Test]
    public void Wrong_Token_Is_Reported_Once_And_Not_Retried()
    {
        using var sb = new NvimSandbox();
        string root = sb.Dir("ws/kfs");
        string bad = sb.Write("ws/kfs/bad.c", Programs.Undeclared);
        using var server = FakeKfsServer.Start(Runtime.Tables, root, Token());
        using NvimRun run = sb.Start("token", root, bad, new KfsLspEndpoint(server.Address, "not-the-token"), options: FastRetry);
        JsonObject r = run.Finish();
        Assert.That(r["refused_messages"]!.GetValue<int>(), Is.EqualTo(1), () => r.ToJsonString());
        Assert.That(r["none_messages"]!.GetValue<int>(), Is.Zero);
        Assert.That(r["clients"]!.GetValue<int>(), Is.Zero);
        Assert.That(Array(r, "errors"), Is.Empty, "no Lua or LSP error from Neovim itself");
        Assert.That(r["tried"]!.ToJsonString(), Does.Contain("refused earlier"));
        Assert.That(r["refused_after_restart"]!.GetValue<int>(), Is.EqualTo(2), ":KfsRestart tries again and says so again");
        Assert.That(server.Initializes, Is.EqualTo(2), "one initialize however often discovery ran, one more after :KfsRestart");
        Assert.That(server.Log, Does.Contain("with a wrong token"));
    }

    [Test]
    public void Commands_Send_The_Contract_And_Write_Workspace_Buffers_First()
    {
        using var sb = new NvimSandbox();
        string root = sb.Dir("Kitten Space Agency/kfs");
        string a = sb.Write("Kitten Space Agency/kfs/a.c", Programs.Undeclared);
        string header = sb.Write("Kitten Space Agency/kfs/b.h", "#define B 1\n");
        string outside = sb.Write("elsewhere/outside.c", "int o;\n");
        string parts = string.Join("\n", Enumerable.Range(0, 30).Select(i => $"light {i}: lamp {i}"));
        using var server = FakeKfsServer.Start(Runtime.Tables, root, Token(), result: (command, _) => command switch
        {
            "kfs.parts" => new JsonObject { ["ok"] = true, ["output"] = parts, ["error"] = null },
            "kfs.manual" => new JsonObject { ["ok"] = true, ["output"] = "KC-E003: undeclared name\nDeclare it first.\n", ["error"] = null },
            "kfs.load" => new JsonObject { ["ok"] = false, ["output"] = "built a.c\nno vehicle to load into", ["error"] = "no vehicle" },
            "kfs.verb" => new JsonObject { ["ok"] = false, ["output"] = "", ["error"] = "kfs.verb is disabled (lsp_allow_vehicle_commands)" },
            // A server and client that disagree on the command list: the server's InvalidParams.
            "kfs.reset" => throw new purrTTY.Kfs.Lsp.Protocol.JsonRpcException(purrTTY.Kfs.Lsp.Protocol.JsonRpcErrorCodes.InvalidParams, "unknown command 'kfs.reset'"),
            "kfs.stop" => new JsonObject { ["ok"] = true, ["output"] = "", ["error"] = null },
            _ => null!,
        });
        server.DiskProbe = () => new Dictionary<string, string?>
        {
            ["a"] = File.ReadAllText(a),
            ["b"] = File.ReadAllText(header),
            ["outside"] = File.ReadAllText(outside),
        };

        using NvimRun run = sb.Start("commands", root, a, server.Endpoint, new { header, outside });
        JsonObject r = run.Finish();

        var sent = server.Commands.Select(c => $"{c.Command} {Arguments(c.Arguments)}").ToList();
        string aUri = "file:" + Canonical(a);
        Assert.That(sent, Is.EqualTo(new[]
        {
            $"kfs.build [{aUri}]", $"kfs.load [{aUri}]", "kfs.start []", "kfs.stop []", "kfs.reset []", "kfs.target [bench]",
            "kfs.parts []", "kfs.parts [lights]", "kfs.manual [KC-E003]", "kfs.manual [KA-E001]", "kfs.verb [status]", "kfs.start []",
        }), () => r.ToJsonString());

        ReceivedCommand build = server.Commands[0];
        Assert.That(build.Disk["a"], Does.EndWith("// edited in a.c\n"), "a.c was written before kfs.build arrived");
        Assert.That(build.Disk["b"], Does.EndWith("// edited in b.h\n"), "every modified workspace buffer is written");
        Assert.That(build.Disk["outside"], Is.EqualTo("int o;\n"), "buffers outside the workspace are not");
        Assert.That(File.ReadAllText(outside), Is.EqualTo("int o;\n"));
        Assert.That(r["modified_after_build"]!.ToJsonString(), Does.Contain("\"a\":false").And.Contain("\"b\":false").And.Contain("\"outside\":true"));

        Assert.That(r["build"]!["ok"]!.GetValue<bool>(), Is.True);
        Assert.That(r["load"]!["ok"]!.GetValue<bool>(), Is.False);
        Assert.That(r["load"]!["error"]!.GetValue<string>(), Is.EqualTo("no vehicle"));
        Assert.That(Array(r, "load_output").Select(l => l!.GetValue<string>()), Is.EqualTo(new[] { "-- :KfsLoad (failed: no vehicle)", "built a.c", "no vehicle to load into" }));
        Assert.That(r["target_bad"]!["ok"]!.GetValue<bool>(), Is.False);
        Assert.That(Array(r, "parts_output").Select(l => l!.GetValue<string>()), Is.EqualTo(new[] { "-- :KfsParts" }.Concat(parts.Split('\n'))), "long output goes to kfs://output");
        Assert.That(r["current_is_source"]!.GetValue<bool>(), Is.True, "the output split does not take the cursor");
        Assert.That(r["verb"]!["ok"]!.GetValue<bool>(), Is.False);
        Assert.That(r["modified_at_end"]!["outside"]!.GetValue<bool>(), Is.True);

        var messages = Array(r, "messages").Select(m => m!["msg"]!.GetValue<string>()).ToList();
        Assert.That(messages, Does.Contain("kFS: :KfsBuild: kfs.build done"));
        Assert.That(messages, Does.Contain("kFS: :KfsLoad failed: no vehicle"));
        Assert.That(messages, Does.Contain("kFS: :KfsVerb failed: kfs.verb is disabled (lsp_allow_vehicle_commands)"));
        Assert.That(messages, Does.Contain("kFS: :KfsTarget takes vehicle or bench"));
        Assert.That(messages, Does.Contain("kFS: :KfsReset failed: the language server does not know kfs.reset (unknown command 'kfs.reset')"));
        Assert.That(r["reset"]!["ok"]!.GetValue<bool>(), Is.False);
        Assert.That(messages, Does.Contain("kFS: :KfsStop: sent; the game printed nothing"), "ran, printed nothing: not called done");
        Assert.That(Array(r, "messages").Where(m => m!["level"]!.GetValue<int>() >= 4).Select(m => m!["msg"]!.GetValue<string>()), Is.Empty, "no errors raised");
    }

    [Test]
    public void Log_Buffer_Follows_The_Feeds()
    {
        using var sb = new NvimSandbox();
        string root = sb.Dir("ws/kfs");
        string a = sb.Write("ws/kfs/a.c", Programs.Clean);
        using var server = FakeKfsServer.Start(Runtime.Tables, root, Token());
        using NvimRun run = sb.Start("log", root, a, server.Endpoint, options: new { log_max_lines = 20 });

        run.WaitForSignal("log-open");
        Assert.That(FakeKfsServer.WaitUntil(() => server.Subscriptions.Contains("log,output,state")), Is.True, () => string.Join(" | ", server.Subscriptions));
        Assert.That(server.Subscriptions[0], Is.EqualTo("state"), "status() subscribed to the state first");
        Broadcast(server, "purrtty/kfsLog", new JsonObject { ["lines"] = new JsonArray("one", "two") });
        Broadcast(server, "purrtty/kfsOutput", new JsonObject { ["text"] = "out1\nout2\n" });
        Broadcast(server, "purrtty/kfsLog", new JsonObject { ["lines"] = new JsonArray("three") });
        Broadcast(server, "purrtty/kfsState", State(null));

        run.WaitForSignal("scrolled");
        Broadcast(server, "purrtty/kfsLog", new JsonObject { ["lines"] = new JsonArray(Enumerable.Range(1, 30).Select(i => (JsonNode)JsonValue.Create("L" + i)).ToArray()) });
        JsonObject trap = new() { ["message"] = "boom", ["uri"] = new Uri(a).AbsoluteUri, ["line"] = 2 };
        Broadcast(server, "purrtty/kfsState", State(trap));
        Broadcast(server, "purrtty/kfsState", State(trap.DeepClone().AsObject()));
        Broadcast(server, "purrtty/kfsLog", new JsonObject { ["lines"] = new JsonArray("after-trap") });

        run.WaitForSignal("wiped");
        Assert.That(FakeKfsServer.WaitUntil(() => server.Subscriptions[^1] == "state"), Is.True, () => string.Join(" | ", server.Subscriptions));
        run.Signal("unsubscribed");
        JsonObject r = run.Finish();

        Assert.That(r["status_connected"]!.GetValue<string>(), Is.EqualTo("kFS: connected"));
        Assert.That(Array(r, "first").Select(l => l!.GetValue<string>()), Is.EqualTo(new[] { "one", "two", "> out1", "> out2", "three" }));
        Assert.That(r["status_running"]!.GetValue<string>(), Is.EqualTo("kFS: running a.c tick 42"));
        Assert.That(r["followed"]!.GetValue<bool>(), Is.True);
        var expected = Enumerable.Range(13, 18).Select(i => "L" + i).Append("!! trap boom (a.c:3)").Append("after-trap");
        Assert.That(Array(r, "second").Select(l => l!.GetValue<string>()), Is.EqualTo(expected), "capped at log_max_lines");
        Assert.That(r["cursor_line"]!.GetValue<int>(), Is.LessThan(20), "a window scrolled up does not follow");
        Assert.That(r["trap_messages"]!.GetValue<int>(), Is.EqualTo(1), "a trap is announced once");
        Assert.That(r["status_trap"]!.GetValue<string>(), Is.EqualTo("kFS: stopped a.c tick 42 2 overruns, trap boom (a.c:3)"));
        Assert.That(r["modifiable"]!.GetValue<bool>(), Is.False);
    }

    [Test]
    public void Loaded_Twice_Or_As_A_Plugin_Sets_Up_Once()
    {
        // As an ordinary plugin only (a package in the data folder), no --cmd entry.
        JsonObject plugin = RunTwice(entry: false, package: true, resetRuntimepath: false);
        Assert.That(plugin["setup_calls"]!.GetValue<int>(), Is.EqualTo(1));
        Assert.That(plugin["plugin_files"]!.GetValue<int>(), Is.EqualTo(1));
        int autocmds = plugin["autocmds"]!.GetValue<int>();
        Assert.That(autocmds, Is.EqualTo(6), "FileType and Syntax for kc and for kasm, BufWritePre, VimLeavePre");

        // The launcher's entry and the plugin both: setup() twice, one set of everything.
        JsonObject both = RunTwice(entry: true, package: true, resetRuntimepath: false);
        Assert.That(both["setup_calls"]!.GetValue<int>(), Is.EqualTo(2));
        Assert.That(both["autocmds"]!.GetValue<int>(), Is.EqualTo(autocmds));
        Assert.That(both["kfs_clients"]!.GetValue<int>(), Is.EqualTo(1));
        Assert.That(both["commands"]!.ToJsonString(), Is.EqualTo(plugin["commands"]!.ToJsonString()));

        // A plugin manager that resets 'runtimepath' and turns 'loadplugins' off in init.lua.
        JsonObject reset = RunTwice(entry: true, package: false, resetRuntimepath: true);
        Assert.That(reset["setup_calls"]!.GetValue<int>(), Is.EqualTo(1));
        Assert.That(reset["loadplugins"]!.GetValue<bool>(), Is.False, "the test's init.lua ran");
        Assert.That(reset["autocmds"]!.GetValue<int>(), Is.EqualTo(autocmds));
        Assert.That(reset["ft"]!.GetValue<string>(), Is.EqualTo("kc"));
    }

    private JsonObject RunTwice(bool entry, bool package, bool resetRuntimepath)
    {
        using var sb = new NvimSandbox();
        string root = sb.Dir("ws/kfs");
        string bad = sb.Write("ws/kfs/bad.c", Programs.Undeclared);
        if (package) NvimSandbox.CopyTree(sb.RuntimeDirectory, Path.Combine(sb.DataHome, "nvim", "site", "pack", "purrtty", "start", "purrtty_kfs"));
        if (resetRuntimepath)
        {
            sb.Write(Path.GetRelativePath(sb.Base, Path.Combine(sb.ConfigHome, "nvim", "init.lua")),
                "-- what lazy.nvim does at start-up: its own runtimepath, its own loader, no plugin/ scan\n" +
                "vim.go.loadplugins = false\n" +
                "vim.opt.rtp = { vim.fn.stdpath('config'), vim.env.VIMRUNTIME, vim.fn.stdpath('config') .. '/after' }\n" +
                "vim.loader.enable()\n");
        }
        using var server = FakeKfsServer.Start(Runtime.Tables, root, Token());
        using NvimRun run = sb.Start("twice", root, bad, server.Endpoint, runtimeFiles: entry);
        JsonObject r = run.Finish();
        Assert.That(r["ft"]!.GetValue<string>(), Is.EqualTo("kc"));
        Assert.That((r["diagnostics"] as JsonArray)!.Select(Diag), Is.EqualTo(new[] { "4:21-28 KC-E003" }));
        Assert.That(Array(r, "messages"), Is.Empty);
        return r;
    }

    [Test]
    public void Older_Neovim_Gets_One_Message_And_Nothing_Else()
    {
        using var sb = new NvimSandbox();
        string root = sb.Dir("ws/kfs");
        string bad = sb.Write("ws/kfs/bad.c", Programs.Undeclared);
        using NvimRun run = sb.Start("old", root, bad, server: null, new { pretend_old = true });
        JsonObject r = run.Finish();
        Assert.That(r["messages_old"]!.GetValue<int>(), Is.EqualTo(1));
        Assert.That(Array(r, "messages"), Has.Count.EqualTo(1));
        Assert.That(r["autocmds"]!.GetValue<int>(), Is.Zero);
        Assert.That(Array(r, "commands"), Is.Empty);
        Assert.That(r["ft"]!.GetValue<string>(), Is.EqualTo("c"), "no kc rule was added");
    }

    [Test]
    public void Help_File_Tags_Are_Well_Formed()
    {
        using var sb = new NvimSandbox();
        string doc = Path.Combine(sb.RuntimeDirectory, "doc");
        var psi = new ProcessStartInfo(Neovim.Path!) { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        const string HelpTags = "lua local ok, e = pcall(vim.cmd.helptags, vim.env.KFS_DOC) if not ok then io.stderr:write(tostring(e)) vim.cmd('cquit 1') end";
        foreach (string a in new[] { "--headless", "--clean", "-c", HelpTags, "-c", "qa!" }) psi.ArgumentList.Add(a);
        psi.Environment["KFS_DOC"] = doc;
        psi.Environment["HOME"] = sb.Home;
        psi.Environment["XDG_CONFIG_HOME"] = sb.ConfigHome;
        psi.Environment["XDG_DATA_HOME"] = sb.DataHome;
        psi.Environment["XDG_STATE_HOME"] = sb.StateHome;
        psi.Environment["XDG_CACHE_HOME"] = sb.CacheHome;
        using Process p = Process.Start(psi)!;
        p.StandardInput.Close();
        Task<string> stderr = p.StandardError.ReadToEndAsync();
        Task<string> stdout = p.StandardOutput.ReadToEndAsync();
        Assert.That(p.WaitForExit(30000), Is.True);
        Assert.That(p.ExitCode, Is.Zero, () => stdout.Result + stderr.Result);
        Assert.That(stderr.Result + stdout.Result, Is.Empty);

        string text = File.ReadAllText(Path.Combine(doc, "purrtty-kfs.txt"));
        var tags = File.ReadAllLines(Path.Combine(doc, "tags")).Select(l => l.Split('\t')[0]).ToHashSet(StringComparer.Ordinal);
        foreach (string command in new[] { "KfsBuild", "KfsLoad", "KfsStart", "KfsStop", "KfsReset", "KfsTarget", "KfsParts", "KfsManual", "KfsVerb", "KfsLog", "KfsInfo", "KfsRestart" })
        {
            Assert.That(tags, Does.Contain(":" + command), "every command has a tag");
        }
        Assert.That(tags, Does.Contain("purrtty-kfs.txt").And.Contain("purrtty-kfs-discovery").And.Contain("purrtty-kfs-info-file"));
        // Every |purrtty-kfs-…| link points at a tag of this file.
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(text, @"\|(purrtty-kfs[^|\s]*|:Kfs\w+)\|"))
        {
            Assert.That(tags, Does.Contain(m.Groups[1].Value), m.Value);
        }
        Assert.That(text.Split('\n').Where(l => l.Length > 78 && !l.Contains("vim:", StringComparison.Ordinal)), Is.Empty, "lines fit 'textwidth' 78");
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>A loopback TCP peer that is not a language server: it accepts and then holds the connection silently, or closes it at once.</summary>
    private sealed class Peer : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly List<TcpClient> _held = [];
        private int _accepted;

        public Peer(bool closeAtOnce)
        {
            _listener.Start();
            Address = $"127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}";
            _ = Task.Run(async () =>
            {
                while (true)
                {
                    TcpClient c;
                    try { c = await _listener.AcceptTcpClientAsync(); }
                    catch (Exception e) when (e is SocketException or ObjectDisposedException or InvalidOperationException) { return; }
                    Interlocked.Increment(ref _accepted);
                    if (closeAtOnce) c.Dispose();
                    else lock (_held) _held.Add(c);
                }
            });
        }

        public string Address { get; }

        public int Accepted => Volatile.Read(ref _accepted);

        public void Dispose()
        {
            _listener.Stop();
            lock (_held) foreach (TcpClient c in _held) c.Dispose();
        }
    }

    private static void Broadcast(FakeKfsServer server, string method, JsonObject @params) =>
        Assert.That(server.Broadcast(method, @params), Is.EqualTo(1), method);

    private static JsonObject State(JsonObject? trap) => trap is null
        ? new JsonObject { ["program"] = "a.c", ["state"] = "running", ["tick"] = 42, ["overruns"] = 0, ["trap"] = null, ["extra"] = 1 }
        : new JsonObject { ["program"] = "a.c", ["state"] = "stopped", ["tick"] = 42, ["overruns"] = 2, ["trap"] = trap };

    // ["file:///a%20b/a.c"] -> [file:/a b/a.c]; ["bench"] -> [bench]
    private static string Arguments(string json)
    {
        var items = (JsonNode.Parse(json) as JsonArray ?? []).Select(n =>
        {
            string s = n!.GetValue<string>();
            return s.StartsWith("file:", StringComparison.Ordinal) ? "file:" + Canonical(new Uri(s).LocalPath) : s;
        });
        return "[" + string.Join(",", items) + "]";
    }

    // macOS: the temp folder /var/... is /private/var/...; Neovim names buffers by the resolved path.
    private static string Canonical(string path) =>
        OperatingSystem.IsMacOS() && path.StartsWith("/private/", StringComparison.Ordinal) ? path["/private".Length..] : path;

    private static int DeadPid()
    {
        var psi = OperatingSystem.IsWindows() ? new ProcessStartInfo("cmd.exe", "/c exit 0") : new ProcessStartInfo("/bin/sh", "-c true");
        psi.UseShellExecute = false;
        using Process p = Process.Start(psi)!;
        Assert.That(p.WaitForExit(30000), Is.True);
        return p.Id;
    }

    private static int ClosedPort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    // The dotnet host of the running runtime: <root>/shared/Microsoft.NETCore.App/<version>/ -> <root>/dotnet.
    private static string? DotnetHost()
    {
        string runtime = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
        string? root = Path.GetFullPath(Path.Combine(runtime, "..", "..", ".."));
        string candidate = Path.Combine(root, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
        return File.Exists(candidate) ? candidate : null;
    }

    private static string Sh(string s) => "'" + s.Replace("'", "'\\''", StringComparison.Ordinal) + "'";
}
