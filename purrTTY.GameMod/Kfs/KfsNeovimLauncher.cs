using KSA;
using KSA.Kfs;
using KSA.Kfs.Host.Workbench;
using purrTTY.Core.Terminal;
using purrTTY.Display.Ghostty;
using purrTTY.GameMod.InWorld;
using purrTTY.GameMod.InWorld.Settings;
using purrTTY.Kfs.Commands;
using purrTTY.Kfs.Runtime;
using purrTTY.Logging;

namespace purrTTY.GameMod.Kfs;

/// <summary>
/// Opens a kFS workspace file in the player's own Neovim, as the process of a purrTTY terminal
/// (plan §7.2): the <c>purr nvim [file]</c> verb and the kFS menu's Neovim entries. The launch is
/// planned by <see cref="NeovimLaunchPlan"/> (plain strings, unit-tested) and carried out here.
/// </summary>
/// <remarks>
/// <para><b>Threads.</b> Main thread only: <see cref="Open"/> runs from the <c>purr</c> console
/// command or a menu item (UI phase), <see cref="Pump"/> from <c>TerminalMod.OnAfterUi</c>. A 2D
/// window opens at once (as the menu's "New Window" does); an in-world terminal is queued and
/// created by <see cref="Pump"/>, because <see cref="InWorldTerminalManager.Create"/> needs an
/// active ImGui frame (the layouts precedent).</para>
/// <para><b>Sequence</b> (2D): validate the file against the workspace →
/// <c>Workbench.TargetActiveVehicle()</c> (only with the game's kFS prototype on and a controlled
/// vehicle: it creates the vehicle's kFS host; never throws) →
/// <see cref="NeovimLaunchPlan.For"/> → <c>ProcessLaunchOptions.CreateCustom</c> + environment
/// (the plan's variables added, <see cref="NeovimLaunchPlan.RemovedEnvironment"/> removed),
/// working directory, <c>CloseOnSuccessfulExit</c> (a Neovim that fails to start leaves its error
/// on screen) → <c>controller.OpenWindow(options, title)</c> →
/// <c>TryRename("kFS: &lt;file&gt;")</c> (a registry-unique variant when taken) →
/// <c>controller.IsVisible = true</c> (after <c>OpenWindow</c>: set first, it would open a default
/// shell) → focus the new window again (making the controller visible focuses the last-focused one).</para>
/// <para><b>Files.</b> An existing file or folder inside the workspace opens as is. A new name
/// with a kFS extension (<c>.c .h .kasm .kinc</c>) is <b>not</b> written, and neither is its
/// folder: Neovim opens an empty buffer, and the runtime files create the folder when <c>:w</c>
/// first writes it (a <c>BufWritePre</c> limited to the workspace), so quitting without saving
/// leaves nothing behind. Only without the runtime files is the folder created here (<c>:w</c>
/// could not create it). Anything outside the workspace (absolute paths, <c>..</c>) is refused.</para>
/// <para><b>Errors</b> (no workspace, a file outside it, Neovim not found, the terminal failing to
/// open) go to the <c>purr</c> output and the log, and the last one to <see cref="LastFailure"/>,
/// which the kFS menu shows under its Neovim entries; nothing is thrown into the UI. A process
/// that fails to <i>start</i> is reported inside the new window by the terminal itself.</para>
/// </remarks>
internal sealed class KfsNeovimLauncher : IDisposable
{
    private const string Verb = "nvim";
    private const string VerbHelp = "open a kFS workspace file (or the workspace) in your own Neovim, in a purrTTY terminal [file]";

    private readonly GhosttyTerminalController _controller;
    private readonly InWorldTerminalManager? _inWorld;
    private readonly Queue<PendingInWorld> _pendingInWorld = new();
    private IDisposable? _verb;
    private bool _disposed;

    public KfsNeovimLauncher(GhosttyTerminalController controller, InWorldTerminalManager? inWorld)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _inWorld = inWorld;
        RuntimeDirectory = FindRuntimeDirectory();
        try
        {
            // needsExtension: false — editing works with the kFS extension disabled.
            _verb = PurrConsole.Verbs.Add(Verb, VerbHelp, OnVerb, needsExtension: false);
        }
        catch (Exception ex)
        {
            ModLog.Log.Warning($"purrTTY kFS: cannot add 'purr {Verb}' ({ex.Message}); the kFS menu still opens Neovim");
        }
    }

    /// <summary>
    /// The language server's address and token, or null while it is not running. <c>TerminalMod</c>
    /// assigns it from the in-game language-server service (<c>KfsLspService</c>); it stays unset
    /// when that service is not created, and Neovim then starts without a server address.
    /// Read on the main thread at each launch; an exception counts as "not running".
    /// </summary>
    public Func<KfsLspEndpoint?>? ServerInfo { get; set; }

    /// <summary><c>&lt;directory of this assembly&gt;/nvim</c>, the runtime files (plan §7.4); null when the assembly has no location.</summary>
    public string? RuntimeDirectory { get; }

    /// <summary>
    /// Why the last launch failed (the console text, without the <c>purr nvim:</c> prefix), or
    /// null when it succeeded or none was attempted. Main thread; the kFS menu draws it.
    /// </summary>
    public string? LastFailure { get; private set; }

    /// <summary>
    /// Null when Neovim can be opened (a login-shell launch counts: it searches when the terminal
    /// starts), else why not: no workspace, <c>nvim_path</c> wrong, no <c>nvim</c> (or only a shim)
    /// on the game's PATH, in-world wanted but unavailable. Reads <c>kfs.toml</c> and probes the
    /// PATH: call when the menu opens, not per frame. Main thread.
    /// </summary>
    public string? LaunchProblem()
    {
        if (WorkspaceProblem() is { } workspace) return workspace;
        try
        {
            KfsConfig config = KfsConfig.Load();
            string root = Path.GetFullPath(KfsIntegration.Workbench.Workspace.Folder);
            NeovimLaunchRequest request = NeovimLaunchRequest.ForThisProcess(root, null, RuntimeDirectory, null, config.NvimPath, KfsConfig.ConfigRoot());
            return NeovimLaunchPlan.Problem(request, config.NvimOpenIn, _inWorld is not null);
        }
        catch (Exception ex)
        {
            return $"cannot plan the launch ({ex.GetType().Name}: {ex.Message})";
        }
    }

    /// <summary>
    /// Null when the kFS workspace is usable, else why not (for the menu). Touches no file: the
    /// workbench getter and the folder path only (the workbench is created on first use).
    /// </summary>
    public string? WorkspaceProblem()
    {
        try
        {
            string folder = KfsIntegration.Workbench.Workspace.Folder;
            return string.IsNullOrEmpty(folder) ? "the kFS workspace has no folder" : null;
        }
        catch (Exception ex)
        {
            return $"the kFS workbench is unavailable ({ex.GetType().Name}: {ex.Message})";
        }
    }

    /// <summary>
    /// The workspace's source files (<c>.c .h .kasm .kinc</c>), workspace-relative, as
    /// <c>Workspace.List()</c> orders them (root files, then one folder level). Reads the disk:
    /// call when the menu opens, not per frame. A missing folder lists nothing and is not created
    /// (<c>Workspace.List</c> would create it): only a launch creates it.
    /// </summary>
    public string[] ListSources(out string? problem)
    {
        problem = null;
        try
        {
            Workspace workspace = KfsIntegration.Workbench.Workspace;
            if (!Directory.Exists(workspace.Folder)) return [];
            List<string> all = workspace.List();
            var sources = new List<string>(all.Count);
            foreach (string file in all)
            {
                if (HasKfsExtension(file)) sources.Add(file);
            }
            return sources.ToArray();
        }
        catch (Exception ex)
        {
            problem = $"cannot list the kFS workspace ({ex.Message})";
            return [];
        }
    }

    /// <summary>
    /// Opens <paramref name="workspaceRelativeFile"/> (null or empty: the workspace root) in Neovim,
    /// in <paramref name="target"/> (null: <c>nvim_open_in</c> from <c>kfs.toml</c>). Writes what it
    /// did, or why it could not, to <paramref name="report"/>. Main thread. Never throws.
    /// </summary>
    public void Open(string? workspaceRelativeFile, NeovimOpenIn? target, Action<string> report)
    {
        LastFailure = null;
        try
        {
            OpenCore(workspaceRelativeFile, target, report);
        }
        catch (Exception ex)
        {
            Fail(report, $"failed, which is a bug in purrTTY: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>Per frame from <c>TerminalMod.OnAfterUi</c> (inside the ImGui frame): creates queued in-world terminals.</summary>
    public void Pump()
    {
        while (!_disposed && _pendingInWorld.Count > 0)
        {
            PendingInWorld p = _pendingInWorld.Dequeue();
            try
            {
                CreateInWorld(p);
            }
            catch (Exception ex)
            {
                Fail(p.Report, $"could not open the in-world terminal ({ex.GetType().Name}: {ex.Message})");
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _pendingInWorld.Clear();
        Interlocked.Exchange(ref _verb, null)?.Dispose();
        ServerInfo = null;
    }

    // ------------------------------------------------------------------ the verb

    private void OnVerb(PurrContext context)
    {
        string file = Unquote(context.Args);
        Open(file.Length == 0 ? null : file, null, context.Print);
    }

    private static string Unquote(string text)
    {
        string t = text.Trim();
        if (t.Length >= 2 && ((t[0] == '"' && t[^1] == '"') || (t[0] == '\'' && t[^1] == '\''))) t = t[1..^1].Trim();
        return t;
    }

    // ------------------------------------------------------------------ the launch

    private void OpenCore(string? input, NeovimOpenIn? requestedTarget, Action<string> report)
    {
        if (_disposed)
        {
            Fail(report, "purrTTY is shutting down");
            return;
        }

        KfsConfig config = KfsConfig.Load();   // nvim_path / nvim_open_in apply without a restart
        NeovimOpenIn target = requestedTarget ?? config.NvimOpenIn;

        Workspace workspace;
        string root;
        try
        {
            workspace = KfsIntegration.Workbench.Workspace;
            workspace.Ensure();
            root = Path.GetFullPath(workspace.Folder);
        }
        catch (Exception ex)
        {
            Fail(report, $"no kFS workspace ({ex.GetType().Name}: {ex.Message})");
            return;
        }

        string? file = null;
        bool created = false;
        string shownName = "workspace";
        string shownPath = "the workspace";
        if (!string.IsNullOrWhiteSpace(input))
        {
            if (!TryResolveFile(root, input, out file, out string relative, out created, out string? error))
            {
                Fail(report, error!);
                return;
            }
            shownName = Path.GetFileName(file.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            shownPath = created ? relative + " (new file: :w creates it)" : relative;
        }

        string targeting = TargetControlledVehicle();

        // PURRTTY_CONFIG_DIR: the folder holding .purrTTY/, where the server writes kfs-lsp.json
        // (KfsConfig is the one place both resolve it), so Neovim finds the server again after
        // `purr lsp restart` (new port and token) without guessing.
        NeovimLaunchPlan plan = NeovimLaunchPlan.For(NeovimLaunchRequest.ForThisProcess(
            root, file, RuntimeDirectory, CurrentServer(), config.NvimPath, KfsConfig.ConfigRoot()));
        if (!plan.Succeeded)
        {
            Fail(report, plan.Error!);
            return;
        }
        // Without the runtime files nothing creates a new file's folder at :w (their BufWritePre does).
        if (created && !plan.UsesRuntimeFiles && !TryCreateFolderOf(file!, report)) return;

        ProcessLaunchOptions options = ProcessLaunchOptions.CreateCustom(plan.Executable, [.. plan.Arguments]);
        options.WorkingDirectory = plan.WorkingDirectory;
        foreach ((string key, string value) in plan.Environment) options.EnvironmentVariables[key] = value;
        foreach (string variable in plan.RemovedEnvironment)
        {
            options.EnvironmentVariables.Remove(variable);       // CreateDefault's FORCE_COLOR / CLICOLOR_FORCE
            options.RemovedEnvironmentVariables.Add(variable);   // and the game's own, if it has them
        }
        // Closes when Neovim quits normally; a launch that fails (Neovim crashing at start, a
        // wrapper or shim whose target is missing) keeps its window and error text, " (exit N)".
        options.CloseOnSuccessfulExit = true;

        string title = "kFS: " + shownName;
        string description = plan.Description + targeting;
        if (target == NeovimOpenIn.InWorld)
        {
            if (_inWorld is null)
            {
                Fail(report, NeovimLaunchPlan.InWorldUnavailable);
                return;
            }
            _pendingInWorld.Enqueue(new PendingInWorld(options, title, shownPath, description, report));
            return;
        }

        TerminalWindow window;
        try
        {
            window = _controller.OpenWindow(options, shownName);
        }
        catch (Exception ex)
        {
            Fail(report, $"could not open a terminal window ({ex.GetType().Name}: {ex.Message})");
            return;
        }

        string name = Rename(window, title);
        _controller.IsVisible = true;   // after OpenWindow: set first, it would open a default shell
        window.RequestFocus();          // becoming visible focuses the last-focused window
        Succeed(report, $"opened {shownPath} in window '{name}': {description}");
    }

    private void CreateInWorld(PendingInWorld p)
    {
        if (_inWorld is null) return;
        var record = new InWorldTerminalRecord
        {
            Name = p.Title,                     // Create makes it registry-unique
            Cols = 120,
            Rows = 40,
            Launch = p.Options,
            Mode = InWorldTerminalRecord.ModeBillboard,   // in front of the camera, wherever it is
            BillboardClickToFocus = true,                 // click it to type again after clicking away
        };
        InWorldTerminalInstance? instance = _inWorld.Create(record);
        if (instance is null)
        {
            Fail(p.Report, "could not create the in-world terminal (see the log)");
            return;
        }
        _inWorld.Focus(instance);
        Succeed(p.Report, $"opened {p.ShownPath} in in-world terminal '{instance.Name}': {p.Description}");
    }

    private static string Rename(TerminalWindow window, string title)
    {
        if (window.TryRename(title)) return window.Name;
        // Taken (a second editor on the same file name): the registry's unique variant.
        // If even that fails the window keeps its default name.
        window.TryRename(TerminalTargetRegistry.SuggestUniqueName(title));
        return window.Name;
    }

    // So the editor's diagnostics and a later `kfs load` agree on the vehicle's layout (plan §7.2).
    // Only with the game's kFS prototype on: targeting creates the vehicle's KfsVehicleHost and
    // switches `kfs` from the bench to the vehicle, which is the player's choice while it is off.
    // Returns what the launch message adds about it.
    private static string TargetControlledVehicle()
    {
        try
        {
            if (Program.ControlledVehicle is not { } vehicle) return "; no controlled vehicle: diagnostics use the bench layout";
            if (!KfsPrototype.Enabled) return "; the kFS prototype is off: diagnostics use the bench layout (kfs target vehicle switches)";
            KfsIntegration.Workbench.TargetActiveVehicle();
            return $"; kfs targets {vehicle.Id}";
        }
        catch (Exception ex)
        {
            ModLog.Log.Warning($"purrTTY kFS: could not target the controlled vehicle ({ex.GetType().Name}: {ex.Message}); diagnostics use the bench layout");
            return "; could not target the controlled vehicle: diagnostics use the bench layout";
        }
    }

    private bool TryCreateFolderOf(string file, Action<string> report)
    {
        try
        {
            string? directory = Path.GetDirectoryName(file);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            return true;
        }
        catch (Exception ex)
        {
            Fail(report, $"cannot create the folder for {file} ({ex.Message})");
            return false;
        }
    }

    private KfsLspEndpoint? CurrentServer()
    {
        try
        {
            return ServerInfo?.Invoke();
        }
        catch (Exception ex)
        {
            ModLog.Log.Warning($"purrTTY kFS: language server info failed ({ex.Message}); Neovim starts without it");
            return null;
        }
    }

    /// <summary>
    /// Resolves a workspace-relative (or absolute, inside the workspace) path. An existing file or
    /// folder is accepted; a missing one only with a kFS extension (<paramref name="created"/>):
    /// nothing is written for it here.
    /// </summary>
    private static bool TryResolveFile(string root, string input, out string absolute, out string relative, out bool created, out string? error)
    {
        absolute = "";
        relative = "";
        created = false;
        error = null;

        string text = input.Trim().Replace('\\', '/');
        string full;
        try
        {
            full = Path.GetFullPath(Path.IsPathRooted(text) ? text : Path.Combine(root, text.Replace('/', Path.DirectorySeparatorChar)));
        }
        catch (Exception ex)
        {
            error = $"'{input}' is not a usable path ({ex.Message})";
            return false;
        }

        StringComparison comparison = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        string rootNoSep = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        bool inside = full.Equals(rootNoSep, comparison) || full.StartsWith(rootNoSep + Path.DirectorySeparatorChar, comparison);
        if (!inside)
        {
            error = $"'{input}' is outside the kFS workspace ({root}); give a path relative to it, like x_blink/blink.c";
            return false;
        }

        relative = Path.GetRelativePath(rootNoSep, full).Replace('\\', '/');
        absolute = full;
        if (File.Exists(full) || Directory.Exists(full)) return true;

        if (!HasKfsExtension(full))
        {
            error = $"{relative} does not exist; a new kFS file must end in .c, .h, .kasm or .kinc";
            return false;
        }
        created = true;
        return true;
    }

    private static bool HasKfsExtension(string file)
    {
        string extension = Path.GetExtension(file);
        foreach (string e in Workspace.Extensions)
        {
            if (string.Equals(e, extension, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static string? FindRuntimeDirectory()
    {
        try
        {
            // Beside the mod's assemblies, like ThemeCatalog's TerminalThemes/ (plan §7.5).
            string? directory = Path.GetDirectoryName(typeof(KfsNeovimLauncher).Assembly.Location);
            return string.IsNullOrEmpty(directory) ? null : Path.Combine(directory, "nvim");
        }
        catch
        {
            return null;
        }
    }

    private void Succeed(Action<string> report, string message)
    {
        LastFailure = null;
        Report(report, $"purr nvim: {message}");
        try { ModLog.Log.Info($"purrTTY kFS: {message}"); } catch { /* logging never fails the launch */ }
    }

    private void Fail(Action<string> report, string message)
    {
        LastFailure = message;
        Report(report, $"purr nvim: {message}");
        try { ModLog.Log.Warning($"purrTTY kFS: Neovim not opened: {message}"); } catch { /* as above */ }
    }

    private static void Report(Action<string> report, string line)
    {
        try { report(line); } catch { /* the console went away; the log has it */ }
    }

    private sealed record PendingInWorld(ProcessLaunchOptions Options, string Title, string ShownPath, string Description, Action<string> Report);
}
