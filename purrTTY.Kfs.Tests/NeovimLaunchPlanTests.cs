using System.Diagnostics;
using NUnit.Framework;
using purrTTY.Kfs.Runtime;

namespace purrTTY.Kfs.Tests;

/// <summary>
/// <see cref="NeovimLaunchPlan"/>: argv, environment and working directory for every resolution
/// path on Unix and Windows conventions (pure: the OS, PATH, SHELL and the file probe are
/// injected), and the login-shell script executed for real by sh, bash, zsh and dash on a Unix host.
/// No game types.
/// </summary>
[TestFixture]
public sealed class NeovimLaunchPlanTests
{
    private const string Root = "/Users/kit/Documents/My Games/Kitten Space Agency/kfs";
    private const string Runtime = "/Users/kit/My Games/Kitten Space Agency/mods/purrTTY/nvim";
    private const string File = Root + "/x_blink/blink.c";

    private static readonly KfsLspEndpoint Server = new("127.0.0.1:41234", "s3cr3t");

    // The exact text, so a change to the script is a deliberate one (it runs in the player's shell).
    private const string ExpectedScript =
        "purrtty_nvim=$(command -v \"$1\" 2>/dev/null)\n" +
        "case \"$purrtty_nvim\" in\n" +
        "/*) shift; exec \"$purrtty_nvim\" \"$@\" ;;\n" +
        "esac\n" +
        "printf '%s\\n' 'purrTTY: Neovim was not found on the PATH of your login (non-interactive) shell, which reads ~/.zprofile or ~/.profile but not ~/.zshrc; set PATH there, or set nvim_path in kfs.toml' \"(searched by your login (non-interactive) shell: PATH=$PATH)\"\n" +
        "printf '%s' 'Press Enter to close this terminal. '\n" +
        "read -r purrtty_reply\n" +
        "exit 127\n";

    private static NeovimLaunchRequest Unix(IEnumerable<string> files, string? path = "/usr/bin:/bin", string? shell = "/bin/zsh",
        string? configured = null, string? runtime = Runtime, KfsLspEndpoint? server = null, string? file = File, string root = Root)
    {
        var set = new HashSet<string>(files, StringComparer.Ordinal);
        return new NeovimLaunchRequest
        {
            WorkspaceRoot = root, File = file, RuntimeDirectory = runtime, Server = server, ConfiguredNvimPath = configured,
            Os = NeovimOsFamily.Unix, PathVariable = path, ShellVariable = shell, FileExists = set.Contains,
        };
    }

    private static NeovimLaunchRequest Windows(IEnumerable<string> files, string? path, string? configured = null, string? shell = null)
    {
        var set = new HashSet<string>(files, StringComparer.OrdinalIgnoreCase);
        return new NeovimLaunchRequest
        {
            WorkspaceRoot = @"C:\Users\kit\Documents\My Games\Kitten Space Agency\kfs",
            File = @"C:\Users\kit\Documents\My Games\Kitten Space Agency\kfs\x_blink\blink.c",
            RuntimeDirectory = @"C:\Users\kit\Documents\My Games\Kitten Space Agency\mods\purrTTY\nvim",
            ConfiguredNvimPath = configured, Os = NeovimOsFamily.Windows, PathVariable = path, ShellVariable = shell,
            FileExists = set.Contains,
        };
    }

    private static readonly string[] RuntimeFiles = [Runtime + "/purrtty_kfs.lua", "/bin/sh", "/bin/zsh", "/bin/bash", "/bin/dash"];

    private static string[] NvimArgs(string file = File) => ["--cmd", NeovimLaunchPlan.EntryCommand, "--", file];

    [Test]
    public void NeovimLaunchPlan_Configured_Path_Is_Used_As_Is()
    {
        NeovimLaunchPlan plan = NeovimLaunchPlan.For(Unix([.. RuntimeFiles, "/opt/nvim/bin/nvim", "/usr/bin/nvim"], configured: " /opt/nvim/bin/nvim ", server: Server));

        Assert.That(plan.Succeeded, Is.True, plan.Error);
        Assert.That(plan.Resolution, Is.EqualTo(NeovimResolution.ConfiguredPath));
        Assert.That(plan.Executable, Is.EqualTo("/opt/nvim/bin/nvim"), "configured wins over PATH");
        Assert.That(plan.Arguments, Is.EqualTo(NvimArgs()));
        Assert.That(plan.NeovimArguments, Is.EqualTo(plan.Arguments));
        Assert.That(plan.WorkingDirectory, Is.EqualTo(Root));
        Assert.That(plan.Environment, Is.EquivalentTo(new Dictionary<string, string>
        {
            ["PURRTTY_KFS_NVIM"] = Runtime, ["PURRTTY_KFS_ROOT"] = Root, ["PURRTTY_KFS_LSP"] = "127.0.0.1:41234", ["PURRTTY_KFS_TOKEN"] = "s3cr3t",
            ["SHELL"] = "/bin/zsh",
        }));
        Assert.That(plan.Description, Does.Contain("nvim_path in kfs.toml"));
    }

    [Test]
    public void NeovimLaunchPlan_Configured_Path_Missing_Is_An_Error_Not_A_Fallback()
    {
        NeovimLaunchPlan plan = NeovimLaunchPlan.For(Unix([.. RuntimeFiles, "/usr/bin/nvim"], configured: "/opt/nvim/bin/nvim"));

        Assert.That(plan.Succeeded, Is.False);
        Assert.That(plan.Resolution, Is.EqualTo(NeovimResolution.None));
        Assert.That(plan.Executable, Is.Empty);
        Assert.That(plan.Error, Is.EqualTo("nvim_path in kfs.toml names '/opt/nvim/bin/nvim', which does not exist; fix it, or remove it to search the PATH"));

        NeovimLaunchPlan relative = NeovimLaunchPlan.For(Unix([.. RuntimeFiles, "/usr/bin/nvim"], configured: "bin/nvim"));
        Assert.That(relative.Succeeded, Is.False);
        Assert.That(relative.Error, Does.StartWith("nvim_path in kfs.toml must be an absolute path"));
    }

    [Test]
    public void NeovimLaunchPlan_Unix_Path_Search_Skips_Relative_Entries()
    {
        NeovimLaunchPlan plan = NeovimLaunchPlan.For(Unix([.. RuntimeFiles, "bin/nvim", "/opt/homebrew/bin/nvim"], path: "::bin:/usr/bin:/opt/homebrew/bin/"));

        Assert.That(plan.Resolution, Is.EqualTo(NeovimResolution.SearchPath), plan.Error);
        Assert.That(plan.Executable, Is.EqualTo("/opt/homebrew/bin/nvim"));
        Assert.That(plan.Arguments, Is.EqualTo(NvimArgs()));
        Assert.That(plan.Environment.Keys, Is.EquivalentTo(new[] { "PURRTTY_KFS_NVIM", "PURRTTY_KFS_ROOT", "SHELL" }), "no server: no LSP variables");
        Assert.That(plan.Description, Is.EqualTo($"nvim /opt/homebrew/bin/nvim (found on PATH) on {File}, no language server"));
    }

    [TestCase("/bin/zsh", "/bin/zsh")]
    [TestCase("/bin/bash", "/bin/bash")]
    [TestCase("/bin/sh", "/bin/sh")]
    [TestCase("/bin/dash", "/bin/dash")]
    [TestCase("/opt/homebrew/bin/fish", "/bin/sh")]
    [TestCase("/usr/bin/nu", "/bin/sh")]
    [TestCase("zsh", "/bin/sh")]
    [TestCase("/usr/local/bin/zsh", "/bin/sh")]   // named zsh, but not there
    [TestCase(null, "/bin/sh")]
    public void NeovimLaunchPlan_Unix_Login_Shell(string? shell, string expectedShell)
    {
        string[] files = [.. RuntimeFiles, "/opt/homebrew/bin/fish", "/usr/bin/nu"];
        NeovimLaunchPlan plan = NeovimLaunchPlan.For(Unix(files, path: "/usr/bin:/bin", shell: shell));

        Assert.That(plan.Resolution, Is.EqualTo(NeovimResolution.LoginShell), plan.Error);
        Assert.That(plan.Executable, Is.EqualTo(expectedShell));
        string[] expected = ["-lc", ExpectedScript, "sh", "nvim", .. NvimArgs()];
        Assert.That(plan.Arguments, Is.EqualTo(expected));
        Assert.That(plan.NeovimArguments, Is.EqualTo(NvimArgs()));
        Assert.That(plan.WorkingDirectory, Is.EqualTo(Root));
        Assert.That(plan.Description, Does.StartWith($"nvim via login shell ({expectedShell} -l"));
        if (shell is not null && shell != expectedShell)
        {
            Assert.That(plan.Description, Does.Contain($"because $SHELL ({shell}) is not an existing sh, bash, zsh or dash"));
        }
    }

    [Test]
    public void NeovimLaunchPlan_Unix_Without_Any_Shell_Is_An_Error()
    {
        NeovimLaunchPlan plan = NeovimLaunchPlan.For(Unix([Runtime + "/purrtty_kfs.lua"], path: "/usr/bin", shell: "/bin/zsh"));

        Assert.That(plan.Succeeded, Is.False);
        Assert.That(plan.Error, Does.Contain("set nvim_path in kfs.toml"));
    }

    [Test]
    public void NeovimLaunchPlan_Runtime_Files_Absent_Gives_Plain_Neovim()
    {
        foreach (string? runtime in new[] { null, "", Runtime, "relative/nvim" })
        {
            // Runtime exists as a folder name only: without its entry script it is not used.
            NeovimLaunchPlan plan = NeovimLaunchPlan.For(Unix(["/usr/bin/nvim"], runtime: runtime, server: Server));

            Assert.That(plan.Resolution, Is.EqualTo(NeovimResolution.SearchPath), $"runtime '{runtime}': {plan.Error}");
            Assert.That(plan.UsesRuntimeFiles, Is.False);
            Assert.That(plan.Arguments, Is.EqualTo(new[] { "--", File }), $"runtime '{runtime}'");
            Assert.That(plan.Environment.ContainsKey("PURRTTY_KFS_NVIM"), Is.False);
            Assert.That(plan.Environment["PURRTTY_KFS_LSP"], Is.EqualTo("127.0.0.1:41234"));
            Assert.That(plan.Description, Does.Contain("without the purrTTY runtime files"));
        }
    }

    [Test]
    public void NeovimLaunchPlan_Server_Info_Present_Absent_And_Blank()
    {
        NeovimLaunchPlan with = NeovimLaunchPlan.For(Unix([.. RuntimeFiles, "/usr/bin/nvim"], server: Server));
        NeovimLaunchPlan without = NeovimLaunchPlan.For(Unix([.. RuntimeFiles, "/usr/bin/nvim"]));
        NeovimLaunchPlan blank = NeovimLaunchPlan.For(Unix([.. RuntimeFiles, "/usr/bin/nvim"], server: new KfsLspEndpoint(" ", "t")));

        Assert.That(with.Environment["PURRTTY_KFS_LSP"], Is.EqualTo("127.0.0.1:41234"));
        Assert.That(with.Environment["PURRTTY_KFS_TOKEN"], Is.EqualTo("s3cr3t"));
        foreach (NeovimLaunchPlan p in new[] { without, blank })
        {
            Assert.That(p.Environment.ContainsKey("PURRTTY_KFS_LSP") || p.Environment.ContainsKey("PURRTTY_KFS_TOKEN"), Is.False);
        }
        Assert.That(with.Arguments, Is.EqualTo(without.Arguments), "the server travels in the environment only");
    }

    [Test]
    public void NeovimLaunchPlan_Passes_The_Config_Folder_When_Absolute()
    {
        NeovimLaunchPlan with = NeovimLaunchPlan.For(Unix([.. RuntimeFiles, "/usr/bin/nvim"], server: Server));
        Assert.That(with.Environment.ContainsKey(NeovimLaunchPlan.ConfigVariable), Is.False, "not given: not passed");

        NeovimLaunchRequest request = Unix([.. RuntimeFiles, "/usr/bin/nvim"], server: Server);
        NeovimLaunchPlan plan = NeovimLaunchPlan.For(new NeovimLaunchRequest
        {
            WorkspaceRoot = request.WorkspaceRoot, File = request.File, RuntimeDirectory = request.RuntimeDirectory, Server = request.Server,
            Os = request.Os, PathVariable = request.PathVariable, ShellVariable = request.ShellVariable, FileExists = request.FileExists,
            ConfigDirectory = "/home/kit/My Games/Kitten Space Agency",
        });
        Assert.That(NeovimLaunchPlan.ConfigVariable, Is.EqualTo("PURRTTY_CONFIG_DIR"), "the name the runtime files read");
        Assert.That(plan.Environment[NeovimLaunchPlan.ConfigVariable], Is.EqualTo("/home/kit/My Games/Kitten Space Agency"), "spaces are fine: an environment value");
        Assert.That(plan.Arguments, Is.EqualTo(with.Arguments), "the environment only");

        foreach (string? bad in new[] { "", "relative/cfg", @"C:\cfg" })
        {
            NeovimLaunchPlan p = NeovimLaunchPlan.For(new NeovimLaunchRequest
            {
                WorkspaceRoot = request.WorkspaceRoot, File = request.File, Os = request.Os, PathVariable = request.PathVariable,
                FileExists = request.FileExists, ConfigDirectory = bad,
            });
            Assert.That(p.Environment.ContainsKey(NeovimLaunchPlan.ConfigVariable), Is.False, $"'{bad}' is not an absolute Unix path");
        }

        var windows = Windows(["C:\\nvim\\nvim.exe"], path: "C:\\nvim");
        NeovimLaunchPlan w = NeovimLaunchPlan.For(new NeovimLaunchRequest
        {
            WorkspaceRoot = windows.WorkspaceRoot, File = windows.File, Os = windows.Os, PathVariable = windows.PathVariable,
            FileExists = windows.FileExists, ConfigDirectory = @"C:\Users\kit\Documents\My Games\Kitten Space Agency",
        });
        Assert.That(w.Succeeded, Is.True, w.Error);
        Assert.That(w.Environment[NeovimLaunchPlan.ConfigVariable], Is.EqualTo(@"C:\Users\kit\Documents\My Games\Kitten Space Agency"));
    }

    [Test]
    public void NeovimLaunchPlan_No_File_Opens_The_Workspace_Root()
    {
        NeovimLaunchPlan plan = NeovimLaunchPlan.For(Unix([.. RuntimeFiles, "/usr/bin/nvim"], file: null));

        Assert.That(plan.Arguments, Is.EqualTo(NvimArgs(Root)));
        Assert.That(plan.WorkingDirectory, Is.EqualTo(Root));
        Assert.That(plan.Description, Does.Contain("on the workspace"));
    }

    [Test]
    public void NeovimLaunchPlan_Spaces_And_Quotes_Stay_Out_Of_The_Command_Text()
    {
        const string root = "/Users/kit/My \"Games\"/Kitten's Space Agency/kfs";
        const string runtime = "/Users/kit/My \"Games\"/Kitten's Space Agency/mods/purrTTY, beta/nvim";
        const string file = root + "/-x 'odd' \"name\".c";
        NeovimLaunchPlan plan = NeovimLaunchPlan.For(Unix([runtime + "/purrtty_kfs.lua", "/usr/bin/nvim"], runtime: runtime, file: file, root: root));

        Assert.That(plan.Arguments, Is.EqualTo(new[] { "--cmd", "lua dofile(vim.env.PURRTTY_KFS_NVIM .. '/purrtty_kfs.lua')", "--", file }),
            "the file is one argument after --, so its leading '-' is not an option");
        Assert.That(plan.Environment["PURRTTY_KFS_NVIM"], Is.EqualTo(runtime));
        Assert.That(plan.Environment["PURRTTY_KFS_ROOT"], Is.EqualTo(root));
        Assert.That(plan.WorkingDirectory, Is.EqualTo(root));
    }

    [Test]
    public void NeovimLaunchPlan_Relative_Workspace_Root_Is_An_Error()
    {
        NeovimLaunchPlan plan = NeovimLaunchPlan.For(Unix(["/usr/bin/nvim"], root: "kfs"));
        Assert.That(plan.Succeeded, Is.False);
        Assert.That(plan.Error, Does.Contain("not an absolute path"));
    }

    [Test]
    public void NeovimLaunchPlan_Windows_Path_Finds_Nvim_Exe()
    {
        NeovimLaunchPlan plan = NeovimLaunchPlan.For(Windows(
            [@"C:\Program Files\Neovim\bin\nvim.exe", @"C:\Users\kit\Documents\My Games\Kitten Space Agency\mods\purrTTY\nvim\purrtty_kfs.lua"],
            path: @"C:\WINDOWS\system32;;relative;""C:\Program Files\Neovim\bin\"";C:\other", shell: "/bin/zsh"));

        Assert.That(plan.Resolution, Is.EqualTo(NeovimResolution.SearchPath), plan.Error);
        Assert.That(plan.Executable, Is.EqualTo(@"C:\Program Files\Neovim\bin\nvim.exe"));
        Assert.That(plan.Arguments, Is.EqualTo(new[]
        {
            "--cmd", NeovimLaunchPlan.EntryCommand, "--", @"C:\Users\kit\Documents\My Games\Kitten Space Agency\kfs\x_blink\blink.c",
        }));
        Assert.That(plan.Environment["PURRTTY_KFS_NVIM"], Is.EqualTo(@"C:\Users\kit\Documents\My Games\Kitten Space Agency\mods\purrTTY\nvim"));
        Assert.That(plan.WorkingDirectory, Is.EqualTo(@"C:\Users\kit\Documents\My Games\Kitten Space Agency\kfs"));
    }

    [Test]
    public void NeovimLaunchPlan_Windows_Cmd_Shim_Only_Is_Not_Found_And_Says_Why()
    {
        NeovimLaunchPlan plan = NeovimLaunchPlan.For(Windows([@"C:\Users\kit\scoop\shims\nvim.cmd"], path: @"C:\WINDOWS;C:\Users\kit\scoop\shims", shell: "/bin/zsh"));

        Assert.That(plan.Succeeded, Is.False, "a .cmd is never launched, and Windows has no login-shell step");
        Assert.That(plan.Error, Is.EqualTo(@"Neovim was not found as nvim.exe on the game's PATH; there is only the script shim C:\Users\kit\scoop\shims\nvim.cmd, which purrTTY cannot start yet; set nvim_path in kfs.toml to the real nvim.exe"));

        NeovimLaunchPlan none = NeovimLaunchPlan.For(Windows([], path: @"C:\WINDOWS"));
        Assert.That(none.Error, Is.EqualTo("Neovim was not found: no nvim.exe on the game's PATH; install Neovim or set nvim_path in kfs.toml to nvim.exe"));
    }

    [Test]
    public void NeovimLaunchPlan_Windows_Configured_Path()
    {
        const string exe = @"C:\Tools\Neovim\bin\nvim.exe";
        NeovimLaunchPlan ok = NeovimLaunchPlan.For(Windows([exe], path: null, configured: exe));
        Assert.That(ok.Resolution, Is.EqualTo(NeovimResolution.ConfiguredPath), ok.Error);
        Assert.That(ok.Executable, Is.EqualTo(exe));
        Assert.That(ok.Arguments, Is.EqualTo(new[] { "--", @"C:\Users\kit\Documents\My Games\Kitten Space Agency\kfs\x_blink\blink.c" }), "no runtime files in this request");

        NeovimLaunchPlan missing = NeovimLaunchPlan.For(Windows([exe], path: @"C:\Tools\Neovim\bin", configured: @"D:\nvim.exe"));
        Assert.That(missing.Succeeded, Is.False, "no fallback to the PATH");
        Assert.That(missing.Error, Does.Contain("does not exist"));

        NeovimLaunchPlan script = NeovimLaunchPlan.For(Windows([@"C:\shims\nvim.cmd"], path: null, configured: @"C:\shims\nvim.cmd"));
        Assert.That(script.Succeeded, Is.False);
        Assert.That(script.Error, Does.Contain("cannot start .cmd/.bat files"));
    }

    [TestCase("window", true, NeovimOpenIn.Window)]
    [TestCase(" Window ", true, NeovimOpenIn.Window)]
    [TestCase("inworld", true, NeovimOpenIn.InWorld)]
    [TestCase("InWorld", true, NeovimOpenIn.InWorld)]
    [TestCase("in-world", true, NeovimOpenIn.InWorld)]
    [TestCase("in_world", true, NeovimOpenIn.InWorld)]
    [TestCase("", false, NeovimOpenIn.Window)]
    [TestCase(null, false, NeovimOpenIn.Window)]
    [TestCase("tab", false, NeovimOpenIn.Window)]
    public void NeovimLaunchPlan_Parses_Nvim_Open_In(string? text, bool ok, NeovimOpenIn expected)
    {
        Assert.That(NeovimLaunchPlan.TryParseOpenIn(text, out NeovimOpenIn value), Is.EqualTo(ok));
        Assert.That(value, Is.EqualTo(expected));
    }

    [Test]
    public void NeovimLaunchPlan_Marks_Its_Terminals()
    {
        NeovimLaunchPlan plan = NeovimLaunchPlan.For(Unix([.. RuntimeFiles, "/usr/bin/nvim"]));
        Assert.That(NeovimLaunchPlan.IsKfsEditorEnvironment(plan.Environment), Is.True);
        Assert.That(NeovimLaunchPlan.IsKfsEditorEnvironment(new Dictionary<string, string> { ["TERM"] = "xterm-256color" }), Is.False);
        Assert.That(NeovimLaunchPlan.IsKfsEditorEnvironment(null), Is.False);
    }

    // ------------------------------------------------------------------ the script, run for real

    [TestCase("/bin/sh")]
    [TestCase("/bin/bash")]
    [TestCase("/bin/zsh")]
    [TestCase("/bin/dash")]
    public void NeovimLaunchPlan_Login_Shell_Script_Passes_Arguments_Exactly(string shell)
    {
        using var t = new ScriptHost(shell);
        string fake = Path.Combine(t.Bin, "nvim");
        System.IO.File.WriteAllText(fake,
            "#!/bin/sh\n" +
            "{ printf 'cwd=%s\\0' \"$(pwd)\"; printf 'nvim=%s\\0' \"$PURRTTY_KFS_NVIM\"; printf 'root=%s\\0' \"$PURRTTY_KFS_ROOT\";" +
            " printf 'lsp=%s\\0' \"$PURRTTY_KFS_LSP\"; printf 'token=%s\\0' \"$PURRTTY_KFS_TOKEN\";" +
            " for a in \"$@\"; do printf 'arg=%s\\0' \"$a\"; done; } > \"$PURRTTY_TEST_OUT\"\n" +
            "exit 0\n");
        if (!OperatingSystem.IsWindows())   // always true here (ScriptHost skips on Windows); tells the analyzer
        {
            System.IO.File.SetUnixFileMode(fake, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        string file = Path.Combine(t.Workspace, "-my \"odd\" file's $HOME `x`.c");
        NeovimLaunchPlan plan = t.Plan(file, new KfsLspEndpoint("127.0.0.1:5000", "tok en"));
        Assert.That(plan.Resolution, Is.EqualTo(NeovimResolution.LoginShell), plan.Error);

        (int exit, string stdout) = t.Run(plan, closeStdin: false);

        Assert.That(exit, Is.EqualTo(0), $"output: {stdout}");
        string[] recorded = System.IO.File.ReadAllText(t.Out).Split('\0', StringSplitOptions.RemoveEmptyEntries);
        string[] args = recorded.Where(r => r.StartsWith("arg=", StringComparison.Ordinal)).Select(r => r[4..]).ToArray();
        Assert.That(args, Is.EqualTo(new[] { "--cmd", NeovimLaunchPlan.EntryCommand, "--", file }), "every argument arrives as it was planned");
        Assert.That(recorded, Does.Contain("nvim=" + t.Runtime));
        Assert.That(recorded, Does.Contain("root=" + t.Workspace));
        Assert.That(recorded, Does.Contain("lsp=127.0.0.1:5000"));
        Assert.That(recorded, Does.Contain("token=tok en"));
        string cwd = recorded.Single(r => r.StartsWith("cwd=", StringComparison.Ordinal));
        Assert.That(cwd, Does.EndWith("/" + Path.GetFileName(t.Workspace)), "the workspace is the working directory (/var may resolve to /private/var)");
    }

    [Test]
    public void NeovimLaunchPlan_Passes_The_Players_Shell_And_Removes_Forced_Colour()
    {
        // A fish user: the login-shell fallback is /bin/sh, which the terminal would make SHELL.
        NeovimLaunchPlan fish = NeovimLaunchPlan.For(Unix(RuntimeFiles.Append("/opt/homebrew/bin/fish"), shell: "/opt/homebrew/bin/fish"));
        Assert.That((fish.Resolution, fish.Executable), Is.EqualTo((NeovimResolution.LoginShell, "/bin/sh")));
        Assert.That(fish.Environment["SHELL"], Is.EqualTo("/opt/homebrew/bin/fish"), "Neovim's 'shell' and :terminal stay the player's");

        NeovimLaunchPlan found = NeovimLaunchPlan.For(Unix(RuntimeFiles.Append("/usr/bin/nvim"), shell: "/bin/zsh"));
        Assert.That(found.Environment["SHELL"], Is.EqualTo("/bin/zsh"));
        Assert.That(found.RemovedEnvironment, Is.EqualTo(new[] { "FORCE_COLOR", "CLICOLOR_FORCE" }), "pipes of :! and :make get no colour codes");

        Assert.That(NeovimLaunchPlan.For(Unix(RuntimeFiles.Append("/usr/bin/nvim"), shell: null)).Environment.ContainsKey("SHELL"), Is.False);
        Assert.That(NeovimLaunchPlan.For(Unix(RuntimeFiles.Append("/usr/bin/nvim"), shell: "/no/such/shell")).Environment.ContainsKey("SHELL"), Is.False);
        Assert.That(NeovimLaunchPlan.For(Unix(RuntimeFiles.Append("/usr/bin/nvim"), shell: "zsh")).Environment.ContainsKey("SHELL"), Is.False, "relative: not passed");
        Assert.That(NeovimLaunchPlan.For(Windows([@"C:\nvim\nvim.exe"], @"C:\nvim", shell: @"C:\Windows\cmd.exe")).Environment.ContainsKey("SHELL"), Is.False, "Windows has no SHELL");
        Assert.That(NeovimLaunchPlan.For(Unix([], path: "", shell: null, configured: "relative/nvim")).RemovedEnvironment, Is.Empty, "a failed plan removes nothing");
    }

    [Test]
    public void NeovimLaunchPlan_Problem_Is_What_The_Menu_Greys_Its_Entries_With()
    {
        Assert.That(NeovimLaunchPlan.Problem(Unix(RuntimeFiles.Append("/usr/bin/nvim")), NeovimOpenIn.Window, inWorldAvailable: false), Is.Null);
        Assert.That(NeovimLaunchPlan.Problem(Unix(RuntimeFiles), NeovimOpenIn.Window, false), Is.Null, "the login shell searches when the terminal starts");
        Assert.That(NeovimLaunchPlan.Problem(Unix(RuntimeFiles.Append("/usr/bin/nvim"), configured: "/opt/nvim"), NeovimOpenIn.Window, true),
            Does.Contain("nvim_path in kfs.toml names '/opt/nvim', which does not exist"));
        Assert.That(NeovimLaunchPlan.Problem(Windows([@"C:\tools\nvim.cmd"], @"C:\tools"), NeovimOpenIn.Window, true), Does.Contain("script shim"));
        Assert.That(NeovimLaunchPlan.Problem(Unix(RuntimeFiles.Append("/usr/bin/nvim")), NeovimOpenIn.InWorld, inWorldAvailable: false), Is.EqualTo(NeovimLaunchPlan.InWorldUnavailable));
        Assert.That(NeovimLaunchPlan.Problem(Unix(RuntimeFiles.Append("/usr/bin/nvim")), NeovimOpenIn.InWorld, inWorldAvailable: true), Is.Null);
    }

    [TestCase("/bin/sh")]
    [TestCase("/bin/bash")]
    [TestCase("/bin/zsh")]
    [TestCase("/bin/dash")]
    public void NeovimLaunchPlan_Login_Shell_Script_Without_Nvim_Says_So_And_Fails(string shell)
    {
        using var t = new ScriptHost(shell);
        NeovimLaunchPlan plan = t.Plan(Path.Combine(t.Workspace, "a.c"), null);

        (int exit, string stdout) = t.Run(plan, closeStdin: true);

        Assert.That(exit, Is.EqualTo(127), $"output: {stdout}");
        Assert.That(stdout, Does.StartWith(NeovimLaunchPlan.NotFoundMessage + "\n(searched by your login (non-interactive) shell: PATH=" + t.Bin + ")\nPress Enter to close this terminal. "));
    }

    /// <summary>
    /// A temporary folder whose path holds spaces and quotes, with a workspace, a runtime folder, an
    /// empty <c>bin</c> for <c>PATH</c>, and the plan's login-shell argv run by <paramref name="shell"/>.
    /// </summary>
    private sealed class ScriptHost : IDisposable
    {
        private readonly string _shell;
        private readonly string _top;

        public ScriptHost(string shell)
        {
            // Skipped silently: the script is a Unix fallback, and a host may lack one of the shells.
            if (OperatingSystem.IsWindows() || !System.IO.File.Exists(shell)) Assert.Ignore();
            _shell = shell;
            _top = Path.Combine(Path.GetTempPath(), $"purrtty nvim 'plan\" {Guid.NewGuid():N}");
            Bin = Path.Combine(_top, "b i n");
            Workspace = Path.Combine(_top, "My Games", "work 'space\"");
            Runtime = Path.Combine(_top, "mods", "purr \"tty\", 'nvim'");
            Out = Path.Combine(_top, "recorded args");
            Directory.CreateDirectory(Bin);
            Directory.CreateDirectory(Workspace);
            Directory.CreateDirectory(Runtime);
            System.IO.File.WriteAllText(Path.Combine(Runtime, NeovimLaunchPlan.EntryScript), "-- entry\n");
        }

        public string Bin { get; }
        public string Workspace { get; }
        public string Runtime { get; }
        public string Out { get; }

        // nvim is never on the game's PATH here, so the plan takes the login-shell path with this shell.
        public NeovimLaunchPlan Plan(string file, KfsLspEndpoint? server) => NeovimLaunchPlan.For(new NeovimLaunchRequest
        {
            WorkspaceRoot = Workspace, File = file, RuntimeDirectory = Runtime, Server = server,
            Os = NeovimOsFamily.Unix, PathVariable = "/nonexistent-purrtty-dir", ShellVariable = _shell,
            FileExists = System.IO.File.Exists,
        });

        /// <summary>
        /// Runs the plan's argv with <c>-c</c> in place of <c>-lc</c> and a clean environment
        /// (PATH = the test's bin, HOME = the temp folder): a login shell would read this machine's
        /// profiles, whose PATH may hold a real Neovim. The script, $0 and every argument are the plan's.
        /// </summary>
        public (int Exit, string Stdout) Run(NeovimLaunchPlan plan, bool closeStdin)
        {
            Assert.That(plan.Executable, Is.EqualTo(_shell));
            Assert.That(plan.Arguments[0], Is.EqualTo("-lc"));
            var start = new ProcessStartInfo(plan.Executable)
            {
                WorkingDirectory = plan.WorkingDirectory,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            start.ArgumentList.Add("-c");
            foreach (string a in plan.Arguments.Skip(1)) start.ArgumentList.Add(a);
            start.Environment.Clear();
            start.Environment["PATH"] = Bin;
            start.Environment["HOME"] = _top;
            start.Environment["PURRTTY_TEST_OUT"] = Out;
            foreach ((string k, string v) in plan.Environment) start.Environment[k] = v;

            using Process process = Process.Start(start) ?? throw new InvalidOperationException("no process");
            if (closeStdin) process.StandardInput.Close();   // EOF: `read` must return, not wait for a key
            Task<string> stdout = process.StandardOutput.ReadToEndAsync();
            Task<string> stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(20_000))
            {
                process.Kill(entireProcessTree: true);
                Assert.Fail($"{_shell} did not exit within 20 s; stdout so far: {(stdout.IsCompleted ? stdout.Result : "(pending)")}");
            }
            process.WaitForExit();   // drains the redirected streams
            Assert.That(stderr.Result, Is.Empty, "the script writes nothing to stderr");
            return (process.ExitCode, stdout.Result);
        }

        public void Dispose()
        {
            try { Directory.Delete(_top, recursive: true); }
            catch { /* best effort */ }
        }
    }
}
