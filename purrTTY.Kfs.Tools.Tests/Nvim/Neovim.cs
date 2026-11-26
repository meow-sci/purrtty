using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using NUnit.Framework;
using purrTTY.Kfs.Runtime;

namespace purrTTY.Kfs.Tools.Tests.Nvim;

/// <summary>Where the test copies of the Neovim files are.</summary>
internal static class NvimFiles
{
    /// <summary>The shipped runtime files (<c>purrTTY.GameMod/nvim/**</c>), copied to the test output.</summary>
    public static string Shipped => Path.Combine(AppContext.BaseDirectory, "nvim");

    /// <summary>The Lua drivers of these tests (<c>Nvim/lua/*.lua</c>).</summary>
    public static string Scripts => Path.Combine(AppContext.BaseDirectory, "Nvim", "lua");
}

/// <summary>
/// The <c>nvim</c> on this machine's <c>PATH</c>, if it is new enough for the runtime files
/// (<c>require('purrtty_kfs').min_nvim</c>, 0.11). Tests skip silently otherwise.
/// </summary>
internal static partial class Neovim
{
    public static readonly Version Minimum = new(0, 11, 0);

    private static readonly Lazy<(string? Path, Version? Version)> s_found = new(Find);

    public static string? Path => s_found.Value.Path;

    public static Version? Version => s_found.Value.Version;

    /// <summary>Skips the calling test or fixture (silently, with the reason here) when Neovim is missing or too old.</summary>
    public static void RequireOrIgnore()
    {
        // No nvim on PATH, or one older than the runtime files support: nothing to test against.
        if (Path is null) Assert.Ignore();
    }

    [GeneratedRegex(@"^NVIM v(\d+)\.(\d+)\.(\d+)")]
    private static partial Regex VersionLine();

    private static (string?, Version?) Find()
    {
        string name = OperatingSystem.IsWindows() ? "nvim.exe" : "nvim";
        foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(System.IO.Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate;
            try { candidate = System.IO.Path.Combine(dir.Trim('"'), name); }
            catch (ArgumentException) { continue; }
            if (!System.IO.Path.IsPathRooted(candidate) || !File.Exists(candidate)) continue;
            Version? v = VersionOf(candidate);
            return v is not null && v >= Minimum ? (candidate, v) : (null, v);
        }
        return (null, null);
    }

    private static Version? VersionOf(string nvim)
    {
        try
        {
            var psi = new ProcessStartInfo(nvim) { RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true, UseShellExecute = false };
            psi.ArgumentList.Add("--version");
            using Process p = Process.Start(psi)!;
            p.StandardInput.Close();
            Task<string> stdout = p.StandardOutput.ReadToEndAsync();
            _ = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(20000))
            {
                p.Kill(true);
                return null;
            }
            Match m = VersionLine().Match(stdout.Result);
            return m.Success ? new Version(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value)) : null;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return null;
        }
    }
}

/// <summary>
/// A hermetic place to run Neovim: a temporary base folder (its path contains spaces) with its own
/// <c>HOME</c>/<c>USERPROFILE</c>, the four XDG directories, a <c>bin</c> folder that is the whole
/// <c>PATH</c> besides the system folders, and a copy of the shipped runtime files at
/// <c>&lt;base&gt;/purrTTY mod/nvim</c>. Deleted on dispose.
/// </summary>
internal sealed class NvimSandbox : IDisposable
{
    public NvimSandbox()
    {
        Base = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "purrtty nvim " + Guid.NewGuid().ToString("N")[..12]);
        Home = Dir("home");
        ConfigHome = Dir("xdg/config");
        DataHome = Dir("xdg/data");
        StateHome = Dir("xdg/state");
        CacheHome = Dir("xdg/cache");
        Bin = Dir("bin");
        RuntimeDirectory = System.IO.Path.Combine(Base, "purrTTY mod", "nvim");
        CopyTree(NvimFiles.Shipped, RuntimeDirectory);
    }

    public string Base { get; }
    public string Home { get; }
    public string ConfigHome { get; }
    public string DataHome { get; }
    public string StateHome { get; }
    public string CacheHome { get; }
    public string Bin { get; }

    /// <summary>The runtime files the launcher points <c>PURRTTY_KFS_NVIM</c> at.</summary>
    public string RuntimeDirectory { get; }

    /// <summary>A folder below the base (created).</summary>
    public string Dir(string relative)
    {
        string full = System.IO.Path.Combine(Base, relative.Replace('/', System.IO.Path.DirectorySeparatorChar));
        Directory.CreateDirectory(full);
        return full;
    }

    /// <summary>Writes a file below the base, creating its folders; returns its absolute path.</summary>
    public string Write(string relative, string text)
    {
        string full = System.IO.Path.Combine(Base, relative.Replace('/', System.IO.Path.DirectorySeparatorChar));
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text);
        return full;
    }

    /// <summary><c>&lt;home&gt;/Documents/My Games/Kitten Space Agency</c>: the game's default folder for this sandbox's user.</summary>
    public string GameFolder => System.IO.Path.Combine(Home, "Documents", "My Games", "Kitten Space Agency");

    public static void CopyTree(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (string file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            string target = System.IO.Path.Combine(to, System.IO.Path.GetRelativePath(from, file));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    /// <summary>
    /// Starts Neovim the way <c>KfsNeovimLauncher</c> does: <see cref="NeovimLaunchPlan.For"/> for the
    /// workspace, file, runtime folder and server, its executable, arguments, environment and
    /// working directory as planned. The test adds only <c>--headless</c> and, before the plan's
    /// arguments, <c>--cmd</c> loading the test driver (<c>Nvim/lua/driver.lua</c>), which runs
    /// <paramref name="scenario"/> (<c>Nvim/lua/&lt;scenario&gt;.lua</c>) once Neovim has started.
    /// </summary>
    /// <param name="scenario">The Lua scenario's file name without extension.</param>
    /// <param name="workspaceRoot">The plan's workspace root (<c>PURRTTY_KFS_ROOT</c>).</param>
    /// <param name="file">The file to open (null: the workspace).</param>
    /// <param name="server">The language server endpoint, or null when it is "not running".</param>
    /// <param name="args">Passed to the scenario as <c>T.args</c> (JSON).</param>
    /// <param name="options">Set as <c>vim.g.purrtty_kfs</c> before the runtime files load.</param>
    /// <param name="runtimeFiles">False: the plan gets no runtime folder (no <c>--cmd</c> entry).</param>
    /// <param name="environment">Last changes to the environment (null removes a variable).</param>
    /// <param name="configDirectory">The plan's config folder (<c>PURRTTY_CONFIG_DIR</c>, the folder holding <c>.purrTTY</c>), as the mod's launcher passes it.</param>
    /// <param name="pump">Called on the test thread while it waits for Neovim (the game's UI phase: <see cref="NvimRun.Pump"/>).</param>
    public NvimRun Start(string scenario, string workspaceRoot, string? file, KfsLspEndpoint? server, object? args = null,
        object? options = null, bool runtimeFiles = true, IReadOnlyDictionary<string, string?>? environment = null,
        string? configDirectory = null, Action? pump = null)
    {
        string nvim = Neovim.Path ?? throw new InvalidOperationException("no usable nvim");
        NeovimLaunchPlan plan = NeovimLaunchPlan.For(new NeovimLaunchRequest
        {
            WorkspaceRoot = workspaceRoot,
            File = file,
            RuntimeDirectory = runtimeFiles ? RuntimeDirectory : null,
            Server = server,
            ConfigDirectory = configDirectory,
            Os = OperatingSystem.IsWindows() ? NeovimOsFamily.Windows : NeovimOsFamily.Unix,
            PathVariable = System.IO.Path.GetDirectoryName(nvim),
            ShellVariable = null,
            FileExists = File.Exists,
        });
        Assert.That(plan.Succeeded, Is.True, plan.Error);
        Assert.That(plan.Resolution, Is.EqualTo(NeovimResolution.SearchPath));
        Assert.That(plan.UsesRuntimeFiles, Is.EqualTo(runtimeFiles));

        string id = scenario + "-" + Guid.NewGuid().ToString("N")[..8];
        string signals = Dir("signals/" + id);
        string result = System.IO.Path.Combine(Base, id + ".result.json");

        var psi = new ProcessStartInfo(plan.Executable)
        {
            WorkingDirectory = plan.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        psi.ArgumentList.Add("--headless");
        psi.ArgumentList.Add("--cmd");
        psi.ArgumentList.Add("lua dofile(vim.env.KFS_TEST_DRIVER)");
        foreach (string a in plan.Arguments) psi.ArgumentList.Add(a);

        // Hermetic: nothing of the user's Neovim, purrTTY or XDG set-up leaks in.
        foreach (string k in psi.Environment.Keys.ToList())
        {
            if (k.StartsWith("PURRTTY_", StringComparison.OrdinalIgnoreCase) || k.StartsWith("XDG_", StringComparison.OrdinalIgnoreCase) ||
                k.StartsWith("NVIM", StringComparison.OrdinalIgnoreCase) || k.StartsWith("VIM", StringComparison.OrdinalIgnoreCase) ||
                k.Equals("MYVIMRC", StringComparison.OrdinalIgnoreCase) || k.Equals("OneDrive", StringComparison.OrdinalIgnoreCase))
            {
                psi.Environment.Remove(k);
            }
        }
        psi.Environment["HOME"] = Home;
        psi.Environment["USERPROFILE"] = Home;
        psi.Environment["XDG_CONFIG_HOME"] = ConfigHome;
        psi.Environment["XDG_DATA_HOME"] = DataHome;
        psi.Environment["XDG_STATE_HOME"] = StateHome;
        psi.Environment["XDG_CACHE_HOME"] = CacheHome;
        psi.Environment["PATH"] = OperatingSystem.IsWindows()
            ? Bin + ";" + Environment.GetEnvironmentVariable("SystemRoot") + "\\System32"
            : Bin + ":/usr/bin:/bin";
        psi.Environment["KSA_DLL_DIR"] = ModuleInit.Directory;
        foreach ((string k, string v) in plan.Environment) psi.Environment[k] = v;
        psi.Environment["KFS_TEST_DRIVER"] = System.IO.Path.Combine(NvimFiles.Scripts, "driver.lua");
        psi.Environment["KFS_TEST_SCENARIO"] = System.IO.Path.Combine(NvimFiles.Scripts, scenario + ".lua");
        psi.Environment["KFS_TEST_RESULT"] = result;
        psi.Environment["KFS_TEST_SIGNALS"] = signals;
        psi.Environment["KFS_TEST_ARGS"] = JsonSerializer.Serialize(args ?? new { });
        psi.Environment["KFS_TEST_OPTS"] = JsonSerializer.Serialize(options ?? new { });
        if (environment is not null)
        {
            foreach ((string k, string? v) in environment)
            {
                if (v is null) psi.Environment.Remove(k);
                else psi.Environment[k] = v;
            }
        }
        return new NvimRun(psi, plan, result, signals) { Pump = pump };
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Base)) Directory.Delete(Base, recursive: true);
        }
        catch (IOException)
        {
            // Best effort: a temp folder left behind is harmless.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>One running headless Neovim and its scenario's result.</summary>
internal sealed class NvimRun : IDisposable
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(90);

    private readonly Process _process;
    private readonly StringBuilder _stdout = new();
    private readonly StringBuilder _stderr = new();
    private readonly string _result;
    private readonly string _signals;

    public NvimRun(ProcessStartInfo psi, NeovimLaunchPlan plan, string result, string signals)
    {
        Plan = plan;
        _result = result;
        _signals = signals;
        _process = new Process { StartInfo = psi };
        _process.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (_stdout) _stdout.AppendLine(e.Data); };
        _process.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (_stderr) _stderr.AppendLine(e.Data); };
        _process.Start();
        _process.StandardInput.Close();
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
    }

    public NeovimLaunchPlan Plan { get; }

    /// <summary>
    /// The game's main thread, simulated: called on the test thread at every poll of every wait
    /// (<see cref="WaitForSignal"/>, <see cref="Finish"/>) unless the wait says otherwise — as the
    /// game's UI phase calls <c>InGameLanguageHost.Frame</c> once per frame. Null: nothing.
    /// </summary>
    public Action? Pump { get; init; }

    /// <summary>True once <c>T.signal(name)</c> was called (no wait).</summary>
    public bool HasSignalled(string name) => File.Exists(Path.Combine(_signals, name));

    /// <summary>True when Neovim has exited.</summary>
    public bool HasExited => _process.HasExited;

    /// <summary>Waits until the scenario called <c>T.signal(name)</c>, pumping unless <paramref name="pump"/> is false; fails with the output when Neovim exits or the time is up.</summary>
    public void WaitForSignal(string name, bool pump = true)
    {
        string path = Path.Combine(_signals, name);
        bool ok = FakeKfsServer.WaitUntil(() =>
        {
            if (pump) Pump?.Invoke();
            return File.Exists(path) || _process.HasExited;
        }, Timeout);
        if (!ok || !File.Exists(path)) Assert.Fail($"Neovim never signalled '{name}'.\n{Dump()}");
    }

    /// <summary>Lets the scenario's <c>T.wait_signal(name)</c> go on.</summary>
    public void Signal(string name) => File.WriteAllText(Path.Combine(_signals, "c-" + name), "");

    /// <summary>Waits for Neovim to exit and returns the scenario's result; fails on an error, a timeout or a non-zero exit.</summary>
    public JsonObject Finish()
    {
        bool exited = Pump is { } pump
            ? FakeKfsServer.WaitUntil(() => { pump(); return _process.HasExited; }, Timeout)
            : _process.WaitForExit(Timeout);
        if (!exited)
        {
            try { _process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            Assert.Fail($"Neovim did not exit within {Timeout.TotalSeconds} s.\n{Dump()}");
        }
        _process.WaitForExit(); // drains the output events
        JsonObject? result = File.Exists(_result) ? JsonNode.Parse(File.ReadAllText(_result)) as JsonObject : null;
        if (result is null) Assert.Fail($"the scenario wrote no result (exit {_process.ExitCode}).\n{Dump()}");
        if (result!["error"] is { } error) Assert.Fail($"the scenario failed: {error}\n{Dump()}");
        Assert.That(_process.ExitCode, Is.EqualTo(0), Dump);
        return result;
    }

    public string Dump()
    {
        string partial = File.Exists(_result) ? File.ReadAllText(_result) : "(none)";
        lock (_stdout) lock (_stderr)
        {
            return $"argv: {string.Join(" | ", _process.StartInfo.ArgumentList)}\nresult: {partial}\n--- stdout ---\n{_stdout}--- stderr ---\n{_stderr}";
        }
    }

    public void Dispose()
    {
        try
        {
            if (!_process.HasExited) _process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }
        _process.Dispose();
    }
}
