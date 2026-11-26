namespace purrTTY.Kfs.Runtime;

/// <summary>
/// Where the kFS language server can be reached (plan §7.2): its loopback address
/// (<c>127.0.0.1:&lt;port&gt;</c>) and the session token it requires at <c>initialize</c>. A plain
/// record so the server (which assigns it) and the Neovim launcher (which hands it to Neovim
/// through <see cref="NeovimLaunchPlan.LspVariable"/> and <see cref="NeovimLaunchPlan.TokenVariable"/>)
/// can both reference it.
/// </summary>
/// <param name="Address">The server's address, <c>host:port</c>.</param>
/// <param name="Token">The session token.</param>
public sealed record KfsLspEndpoint(string Address, string Token);

/// <summary>The operating-system conventions a <see cref="NeovimLaunchPlan"/> is made for.</summary>
public enum NeovimOsFamily
{
    /// <summary>macOS and Linux: <c>:</c>-separated <c>PATH</c>, <c>/</c> paths, a login-shell fallback.</summary>
    Unix,
    /// <summary>Windows: <c>;</c>-separated <c>PATH</c>, <c>nvim.exe</c>, no login shell.</summary>
    Windows,
}

/// <summary>How <see cref="NeovimLaunchPlan.For"/> found Neovim.</summary>
public enum NeovimResolution
{
    /// <summary>Not found, or the configuration is wrong: <see cref="NeovimLaunchPlan.Error"/> says why.</summary>
    None,
    /// <summary><c>nvim_path</c> from <c>kfs.toml</c>.</summary>
    ConfiguredPath,
    /// <summary>A directory of the game process's <c>PATH</c>.</summary>
    SearchPath,
    /// <summary>Unix only: started through the player's login shell, which searches its own <c>PATH</c> when the terminal starts.</summary>
    LoginShell,
}

/// <summary>Where <c>purr nvim</c> and the kFS menu open Neovim (<c>nvim_open_in</c> in <c>kfs.toml</c>).</summary>
public enum NeovimOpenIn
{
    /// <summary>A 2D purrTTY window (the default).</summary>
    Window,
    /// <summary>An in-world terminal (a camera billboard).</summary>
    InWorld,
}

/// <summary>Everything <see cref="NeovimLaunchPlan.For"/> needs, injected so the plan is deterministic.</summary>
public sealed class NeovimLaunchRequest
{
    /// <summary>The kFS workspace folder (absolute): the working directory and <c>PURRTTY_KFS_ROOT</c>.</summary>
    public required string WorkspaceRoot { get; init; }

    /// <summary>The file (or folder) to open, absolute; null opens the workspace root.</summary>
    public string? File { get; init; }

    /// <summary>The mod's <c>nvim/</c> runtime folder; used only when its entry script <c>purrtty_kfs.lua</c> exists. Null: none.</summary>
    public string? RuntimeDirectory { get; init; }

    /// <summary>The language server, or null when it is not running.</summary>
    public KfsLspEndpoint? Server { get; init; }

    /// <summary>
    /// The folder that <b>contains</b> purrTTY's <c>.purrTTY</c> configuration folder (where the
    /// server info file <c>.purrTTY/kfs-lsp.json</c> is written), absolute; passed as
    /// <see cref="NeovimLaunchPlan.ConfigVariable"/> so Neovim finds a restarted server (new port,
    /// new token) exactly. Null or not absolute: not passed (Neovim then guesses beside the workspace).
    /// </summary>
    public string? ConfigDirectory { get; init; }

    /// <summary><c>nvim_path</c> from <c>kfs.toml</c>; null or empty: not configured.</summary>
    public string? ConfiguredNvimPath { get; init; }

    /// <summary>The conventions to plan for.</summary>
    public NeovimOsFamily Os { get; init; }

    /// <summary>The game process's <c>PATH</c>.</summary>
    public string? PathVariable { get; init; }

    /// <summary>The game process's <c>SHELL</c> (Unix).</summary>
    public string? ShellVariable { get; init; }

    /// <summary>True when a file exists at the given absolute path.</summary>
    public required Func<string, bool> FileExists { get; init; }

    /// <summary>A request with this process's OS, <c>PATH</c>, <c>SHELL</c> and <see cref="System.IO.File.Exists"/>.</summary>
    public static NeovimLaunchRequest ForThisProcess(string workspaceRoot, string? file, string? runtimeDirectory,
        KfsLspEndpoint? server, string? configuredNvimPath, string? configDirectory = null) => new()
    {
        WorkspaceRoot = workspaceRoot,
        File = file,
        RuntimeDirectory = runtimeDirectory,
        Server = server,
        ConfigDirectory = configDirectory,
        ConfiguredNvimPath = configuredNvimPath,
        Os = OperatingSystem.IsWindows() ? NeovimOsFamily.Windows : NeovimOsFamily.Unix,
        PathVariable = Environment.GetEnvironmentVariable("PATH"),
        ShellVariable = Environment.GetEnvironmentVariable("SHELL"),
        FileExists = System.IO.File.Exists,
    };
}

/// <summary>
/// The launch of the player's own Neovim as a purrTTY terminal's process (plan §7.2), as plain
/// strings: executable, discrete arguments, environment, working directory. No terminal and no
/// game types, so every resolution path is unit-tested; <c>KfsNeovimLauncher</c> (purrTTY.GameMod)
/// turns it into <c>ProcessLaunchOptions.CreateCustom(Executable, Arguments)</c>.
/// </summary>
/// <remarks>
/// <para><b>Quoting model.</b> <see cref="Arguments"/> are discrete argv elements and are never
/// joined here: on Unix the terminal execs them as they are, on Windows it quotes each one by the
/// <c>CommandLineToArgvW</c> rules. Nothing here builds a command line except the fixed
/// <see cref="LoginShellScript"/>, which receives everything else as positional parameters
/// (<c>"$@"</c>), so no path or file name is ever parsed by a shell. The runtime folder travels in
/// <see cref="RuntimeVariable"/>, never in the <c>--cmd</c> text, because the mod's default folder
/// contains spaces and <c>:set runtimepath^=</c> would split on them. The file comes after
/// <c>--</c>, so a name starting with <c>-</c> or <c>+</c> is not taken for an option.</para>
/// <para><b>Resolving <c>nvim</c></b>, in order: (1) <c>nvim_path</c> when set — it must be an
/// absolute path to an existing file, otherwise the plan fails (no silent fallback; on Windows a
/// <c>.cmd</c>/<c>.bat</c> is refused, the terminal cannot start a script); (2) the game's
/// <c>PATH</c>, absolute directories only (<c>nvim</c> on Unix, <c>nvim.exe</c> on Windows — the
/// terminal does not honour <c>PATHEXT</c>, so a <c>nvim.cmd</c> shim is reported as not
/// launchable); (3) Unix only, the login shell: <c>$SHELL -lc</c> <see cref="LoginShellScript"/>
/// <c>sh nvim &lt;nvim arguments&gt;</c> for <c>sh</c>, <c>bash</c>, <c>zsh</c> and <c>dash</c>,
/// <c>/bin/sh -lc …</c> for any other shell (fish, nu, tcsh, … whose <c>-c</c> language differs);
/// (4) otherwise a failed plan.</para>
/// </remarks>
public sealed class NeovimLaunchPlan
{
    /// <summary>The mod's runtime folder (present only when its entry script exists).</summary>
    public const string RuntimeVariable = "PURRTTY_KFS_NVIM";

    /// <summary>The kFS workspace root; present in every launch, so it also marks a kFS editor terminal (<see cref="IsKfsEditorEnvironment"/>).</summary>
    public const string RootVariable = "PURRTTY_KFS_ROOT";

    /// <summary>The language server's address (only when it is running).</summary>
    public const string LspVariable = "PURRTTY_KFS_LSP";

    /// <summary>The language server's session token (only when it is running).</summary>
    public const string TokenVariable = "PURRTTY_KFS_TOKEN";

    /// <summary>
    /// The folder containing <c>.purrTTY/</c>, where the server info file is (present when the request
    /// has an absolute <see cref="NeovimLaunchRequest.ConfigDirectory"/>). The runtime files then read
    /// the info file there and nowhere else, so a <c>purr lsp restart</c> is found exactly.
    /// </summary>
    public const string ConfigVariable = "PURRTTY_CONFIG_DIR";

    /// <summary>The entry script inside the runtime folder.</summary>
    public const string EntryScript = "purrtty_kfs.lua";

    /// <summary>The <c>--cmd</c> text: loads the entry script by the path in <see cref="RuntimeVariable"/>.</summary>
    public const string EntryCommand = "lua dofile(vim.env." + RuntimeVariable + " .. '/" + EntryScript + "')";

    /// <summary>
    /// What the login-shell script prints when the shell cannot find Neovim either. The shell is
    /// a login shell but not an interactive one: it reads <c>~/.zprofile</c> / <c>~/.profile</c>
    /// (<c>~/.bash_profile</c>), not <c>~/.zshrc</c> or what an interactivity guard protects.
    /// </summary>
    public const string NotFoundMessage =
        "purrTTY: Neovim was not found on the PATH of your login (non-interactive) shell, which reads " +
        "~/.zprofile or ~/.profile but not ~/.zshrc; set PATH there, or set nvim_path in kfs.toml";

    /// <summary>
    /// Variables of <c>ProcessLaunchOptions.CreateDefault</c> (and possibly the game's own
    /// environment) the editor must not inherit: tools Neovim runs through pipes (<c>:!</c>,
    /// <c>:make</c>, linters) would otherwise write colour codes into the quickfix list.
    /// </summary>
    public static readonly IReadOnlyList<string> RemovedVariables = ["FORCE_COLOR", "CLICOLOR_FORCE"];

    /// <summary>The message of a plan for an in-world terminal when there are none in this session.</summary>
    public const string InWorldUnavailable = "in-world terminals are not available in this session; set nvim_open_in = \"window\" in kfs.toml";

    /// <summary>
    /// The script the login shell runs (<c>-lc</c>), with <c>$0</c> = <c>sh</c>, <c>$1</c> =
    /// <c>nvim</c> and the Neovim arguments after it. POSIX <c>sh</c> that <c>bash</c>,
    /// <c>zsh</c> and <c>dash</c> run alike. Neovim found as a file: <c>exec</c> it with the
    /// arguments untouched. Not found (or only an alias or function): the terminal would close on
    /// exit, so it prints <see cref="NotFoundMessage"/> and the shell's <c>PATH</c>, waits for
    /// Enter, and exits 127; <c>read</c> returns at once on a closed stdin, so it never hangs there.
    /// A profile that <c>exec</c>s another program (a <c>tmux</c> or another shell) replaces the
    /// shell before the script runs, so Neovim never starts: set <c>nvim_path</c> then.
    /// </summary>
    public const string LoginShellScript =
        "purrtty_nvim=$(command -v \"$1\" 2>/dev/null)\n" +
        "case \"$purrtty_nvim\" in\n" +
        "/*) shift; exec \"$purrtty_nvim\" \"$@\" ;;\n" +
        "esac\n" +
        "printf '%s\\n' '" + NotFoundMessage + "' \"(searched by your login (non-interactive) shell: PATH=$PATH)\"\n" +
        "printf '%s' 'Press Enter to close this terminal. '\n" +
        "read -r purrtty_reply\n" +
        "exit 127\n";

    /// <summary>The <c>$0</c> the login shell gives the script (only shown in its message).</summary>
    public const string LoginShellArgv0 = "sh";

    /// <summary>The shell used when <c>$SHELL</c> is missing or not one the script is written for.</summary>
    public const string FallbackShell = "/bin/sh";

    // Shells whose `-l -c script name args…` runs LoginShellScript as written (tested in
    // NeovimLaunchPlanTests for sh, bash and zsh; dash is the POSIX sh of Debian/Ubuntu).
    private static readonly string[] s_scriptShells = ["sh", "bash", "zsh", "dash"];

    // Found on a Windows PATH, these are reported (the terminal cannot start them), never launched.
    private static readonly string[] s_windowsShims = ["nvim.cmd", "nvim.bat"];

    private NeovimLaunchPlan() { }

    /// <summary>True when Neovim can be started as planned; false: <see cref="Error"/> says why.</summary>
    public bool Succeeded => Resolution != NeovimResolution.None;

    /// <summary>Why the plan failed (a full sentence for the player), else null.</summary>
    public string? Error { get; private init; }

    /// <summary>Which resolution path was taken.</summary>
    public NeovimResolution Resolution { get; private init; }

    /// <summary>The terminal's process: Neovim, or the login shell. Empty when the plan failed.</summary>
    public string Executable { get; private init; } = "";

    /// <summary>The process's arguments, discrete (without argv[0]).</summary>
    public IReadOnlyList<string> Arguments { get; private init; } = [];

    /// <summary>What Neovim itself receives (equal to <see cref="Arguments"/> unless started through the login shell).</summary>
    public IReadOnlyList<string> NeovimArguments { get; private init; } = [];

    /// <summary>Variables added to the terminal's environment.</summary>
    public IReadOnlyDictionary<string, string> Environment { get; private init; } = new Dictionary<string, string>();

    /// <summary>Variables removed from the terminal's environment (<see cref="RemovedVariables"/>; empty when the plan failed).</summary>
    public IReadOnlyList<string> RemovedEnvironment { get; private init; } = [];

    /// <summary>The working directory: the workspace root.</summary>
    public string WorkingDirectory { get; private init; } = "";

    /// <summary>True when the runtime files are loaded (<c>--cmd</c>).</summary>
    public bool UsesRuntimeFiles { get; private init; }

    /// <summary>One line for the player: what runs and how it was found (<c>nvim /opt/homebrew/bin/nvim (found on PATH)</c>).</summary>
    public string Description { get; private init; } = "";

    /// <summary>True when <paramref name="environment"/> is that of a terminal this plan launched (it carries <see cref="RootVariable"/>).</summary>
    public static bool IsKfsEditorEnvironment(IReadOnlyDictionary<string, string>? environment) =>
        environment is not null && environment.ContainsKey(RootVariable);

    /// <summary>Parses <c>nvim_open_in</c>: <c>window</c> or <c>inworld</c> (also <c>in-world</c>, <c>in_world</c>), any case.</summary>
    public static bool TryParseOpenIn(string? text, out NeovimOpenIn value)
    {
        string t = (text ?? "").Trim().Replace("-", "", StringComparison.Ordinal).Replace("_", "", StringComparison.Ordinal);
        if (t.Equals("window", StringComparison.OrdinalIgnoreCase)) { value = NeovimOpenIn.Window; return true; }
        if (t.Equals("inworld", StringComparison.OrdinalIgnoreCase)) { value = NeovimOpenIn.InWorld; return true; }
        value = NeovimOpenIn.Window;
        return false;
    }

    /// <summary>
    /// Why Neovim cannot be opened as <paramref name="request"/> plans it in <paramref name="openIn"/>,
    /// or null when it can (a login-shell plan counts as possible: the shell searches when the terminal
    /// starts). For the kFS menu, evaluated when it opens: the entries are greyed with this reason.
    /// </summary>
    public static string? Problem(NeovimLaunchRequest request, NeovimOpenIn openIn, bool inWorldAvailable)
    {
        NeovimLaunchPlan plan = For(request);
        if (!plan.Succeeded) return plan.Error;
        return openIn == NeovimOpenIn.InWorld && !inWorldAvailable ? InWorldUnavailable : null;
    }

    /// <summary>Plans the launch. Never throws for bad input: a failed plan carries <see cref="Error"/>.</summary>
    public static NeovimLaunchPlan For(NeovimLaunchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.FileExists);
        bool windows = request.Os == NeovimOsFamily.Windows;
        string root = request.WorkspaceRoot ?? "";
        if (root.Length == 0 || !IsRooted(root, windows))
        {
            return Failed($"the kFS workspace folder is not an absolute path ('{root}')");
        }

        // Environment and Neovim's own arguments are the same for every resolution path.
        var environment = new Dictionary<string, string>(StringComparer.Ordinal) { [RootVariable] = root };
        var nvimArgs = new List<string>(4);
        bool runtime = false;
        string? runtimeDir = request.RuntimeDirectory;
        if (!string.IsNullOrEmpty(runtimeDir) && IsRooted(runtimeDir, windows) && Exists(request, Join(runtimeDir, EntryScript, windows)))
        {
            runtime = true;
            environment[RuntimeVariable] = runtimeDir;
            nvimArgs.Add("--cmd");
            nvimArgs.Add(EntryCommand);
        }
        if (request.Server is { } server && !string.IsNullOrWhiteSpace(server.Address))
        {
            environment[LspVariable] = server.Address;
            environment[TokenVariable] = server.Token ?? "";
        }
        if (request.ConfigDirectory is { Length: > 0 } config && IsRooted(config, windows))
        {
            environment[ConfigVariable] = config;
        }
        // The player's own shell, passed on explicitly: the terminal points SHELL at the process
        // it starts when that is a shell, which for a fish or nu user is the /bin/sh fallback below,
        // and Neovim takes 'shell' (and :terminal) from SHELL.
        if (!windows && (request.ShellVariable ?? "").Trim() is { Length: > 0 } userShell && userShell.StartsWith('/') && Exists(request, userShell))
        {
            environment["SHELL"] = userShell;
        }
        nvimArgs.Add("--");
        nvimArgs.Add(string.IsNullOrEmpty(request.File) ? root : request.File);

        string target = string.IsNullOrEmpty(request.File) ? "the workspace" : request.File;
        string extras = (runtime ? "" : ", without the purrTTY runtime files") +
                        (environment.ContainsKey(LspVariable) ? "" : ", no language server");

        // (1) the configured path: exactly that file, or an error.
        string configured = (request.ConfiguredNvimPath ?? "").Trim();
        if (configured.Length > 0)
        {
            if (!IsRooted(configured, windows))
            {
                return Failed($"nvim_path in kfs.toml must be an absolute path to the nvim executable (it is '{configured}')");
            }
            if (!Exists(request, configured))
            {
                return Failed($"nvim_path in kfs.toml names '{configured}', which does not exist; fix it, or remove it to search the PATH");
            }
            if (windows && IsScript(configured))
            {
                return Failed($"nvim_path in kfs.toml names a script ('{configured}'); purrTTY cannot start .cmd/.bat files yet, so point it at nvim.exe itself");
            }
            return Planned(NeovimResolution.ConfiguredPath, configured, nvimArgs, nvimArgs, environment, root, runtime,
                $"nvim {configured} (nvim_path in kfs.toml) on {target}{extras}");
        }

        // (2) the game process's PATH.
        string? shim = null;
        foreach (string directory in SplitPath(request.PathVariable, windows))
        {
            string candidate = Join(directory, windows ? "nvim.exe" : "nvim", windows);
            if (Exists(request, candidate))
            {
                return Planned(NeovimResolution.SearchPath, candidate, nvimArgs, nvimArgs, environment, root, runtime,
                    $"nvim {candidate} (found on PATH) on {target}{extras}");
            }
            if (windows && shim is null)
            {
                foreach (string script in s_windowsShims)
                {
                    string s = Join(directory, script, windows);
                    if (Exists(request, s)) { shim = s; break; }
                }
            }
        }

        if (windows)
        {
            // (4) on Windows there is no login-shell step.
            return Failed(shim is null
                ? "Neovim was not found: no nvim.exe on the game's PATH; install Neovim or set nvim_path in kfs.toml to nvim.exe"
                : $"Neovim was not found as nvim.exe on the game's PATH; there is only the script shim {shim}, which purrTTY cannot start yet; set nvim_path in kfs.toml to the real nvim.exe");
        }

        // (3) Unix: the login shell searches its own PATH when the terminal starts.
        string? shell = ScriptShell(request);
        if (shell is null)
        {
            return Failed($"Neovim was not found on the game's PATH and there is no {FallbackShell} to search a login PATH with; set nvim_path in kfs.toml");
        }
        var shellArgs = new List<string>(nvimArgs.Count + 4) { "-lc", LoginShellScript, LoginShellArgv0, "nvim" };
        shellArgs.AddRange(nvimArgs);
        string viaShell = request.ShellVariable is { Length: > 0 } s0 && !string.Equals(s0, shell, StringComparison.Ordinal)
            ? $"{shell} -l, because $SHELL ({s0}) is not an existing sh, bash, zsh or dash"
            : $"{shell} -l";
        return Planned(NeovimResolution.LoginShell, shell, shellArgs, nvimArgs, environment, root, runtime,
            $"nvim via login shell ({viaShell}; not on the game's PATH) on {target}{extras}");
    }

    private static NeovimLaunchPlan Failed(string error) => new() { Resolution = NeovimResolution.None, Error = error, Description = error };

    private static NeovimLaunchPlan Planned(NeovimResolution resolution, string executable, List<string> arguments,
        List<string> nvimArguments, Dictionary<string, string> environment, string root, bool runtime, string description) => new()
    {
        Resolution = resolution,
        Executable = executable,
        Arguments = arguments.ToArray(),
        NeovimArguments = nvimArguments.ToArray(),
        Environment = environment,
        RemovedEnvironment = RemovedVariables,
        WorkingDirectory = root,
        UsesRuntimeFiles = runtime,
        Description = description,
    };

    // $SHELL when it is an absolute path to an existing sh/bash/zsh/dash, else /bin/sh when it exists.
    private static string? ScriptShell(NeovimLaunchRequest request)
    {
        string shell = (request.ShellVariable ?? "").Trim();
        if (shell.StartsWith('/') && Exists(request, shell))
        {
            string name = shell[(shell.LastIndexOf('/') + 1)..];
            if (Array.IndexOf(s_scriptShells, name) >= 0) return shell;
        }
        return Exists(request, FallbackShell) ? FallbackShell : null;
    }

    private static bool Exists(NeovimLaunchRequest request, string path)
    {
        try { return request.FileExists(path); }
        catch { return false; }
    }

    private static bool IsScript(string path) =>
        path.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".bat", StringComparison.OrdinalIgnoreCase);

    // Absolute by the target OS's rules (not this process's: Path.IsPathRooted on macOS says C:\ is relative).
    private static bool IsRooted(string path, bool windows)
    {
        if (!windows) return path.StartsWith('/');
        if (path.StartsWith(@"\\", StringComparison.Ordinal)) return true;
        return path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && (path[2] == '\\' || path[2] == '/');
    }

    private static string Join(string directory, string name, bool windows)
    {
        char separator = windows ? '\\' : '/';
        string d = directory.TrimEnd(windows ? ['\\', '/'] : ['/']);
        return d + separator + name;
    }

    // PATH entries, absolute ones only: an empty or relative entry would search the game's working directory.
    private static IEnumerable<string> SplitPath(string? path, bool windows)
    {
        if (string.IsNullOrEmpty(path)) yield break;
        foreach (string entry in path.Split(windows ? ';' : ':', StringSplitOptions.RemoveEmptyEntries))
        {
            string e = windows ? entry.Trim().Trim('"') : entry;
            if (e.Length > 0 && IsRooted(e, windows)) yield return e;
        }
    }
}
