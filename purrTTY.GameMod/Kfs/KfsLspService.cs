using KSA;
using KSA.Kfs;
using KSA.Kfs.Vm.Tables;
using purrTTY.Kfs.Commands;
using purrTTY.Kfs.Lsp;
using purrTTY.Kfs.Lsp.Features;
using purrTTY.Kfs.Lsp.InGame;
using purrTTY.Kfs.Runtime;
using purrTTY.Logging;

namespace purrTTY.GameMod.Kfs;

/// <summary>
/// The kC/kASM language server inside the game (plan §6.1, §6.7, §7.6; task C4): owns the in-game
/// host, the live feed, the output tee and the service core (listener, token, server info file), the
/// <c>purr lsp</c> verb, and the address the Neovim launcher passes on.
/// </summary>
/// <remarks>
/// <para><b>Start</b> (<see cref="Start"/>, from <c>TerminalMod.OnFullyLoaded</c> right after
/// <c>Patcher.patch()</c>, i.e. after the kFS extension's install; main thread):
/// a service left by an earlier load is stopped first; <c>kfs.toml</c> is read; <c>purr lsp</c> is
/// added; with <c>lsp_enabled = false</c> that is all. Otherwise: <c>KfsIntegration.Tables</c> (the
/// augmented tables when the extension is active, the stock ones when it is not); the tee replaces
/// <c>KfsIntegration.Output</c>; <c>KfsIntegration.Workbench</c> is read once, so the workbench —
/// created now if it did not exist (no folder is created) — writes to the tee, its deferred replies
/// included; the host and the core; the core starts (one status line in the log). A failure (port in
/// use…) leaves the mod working and is in <c>purr lsp</c> and the menu.</para>
/// <para><b>Per frame</b> (<see cref="Frame"/>, from <c>TerminalMod.OnAfterUi</c>): the host's frame
/// — workspace root, target, live program, queued commands, buffered output.</para>
/// <para><b>Stop</b> (<see cref="Dispose"/>, from <c>TerminalMod.Unload</c> before Harmony
/// unpatching): <c>purr lsp</c> removed; queued commands fail ("purrTTY is unloading"); the core
/// stops (connections closed, every thread joined, the info file deleted if it is still ours); the
/// feed stops; the tee is taken out when <c>Output</c> still holds it (else it becomes a
/// pass-through). Statics survive a reload: <see cref="Current"/> is cleared, and a next load
/// first stops a service a skipped unload left behind.</para>
/// <para><b>Prototype off, extension disabled.</b> The server runs anyway: diagnostics against the
/// tables the game has (stock ones without the extension, where <c>&lt;purrtty.h&gt;</c> is unknown,
/// as it is to the game's compiler then) are still right, and <c>kfs</c> commands answer what the
/// console answers ("kfs is off …"). Starting reads the workspace folder's path but creates no
/// folder and no file in it.</para>
/// </remarks>
internal sealed class KfsLspService : IDisposable
{
    private const string Verb = "lsp";
    private const string VerbHelp = "the kFS language server: address, clients, workspace, target [token | restart]";

    private InGameLanguageHost? _host;
    private LiveFeed? _feed;
    private OutputTeeInstallation? _tee;
    private LspServiceCore? _core;
    private IDisposable? _verb;
    private bool _allowVerb;
    private string? _offReason;
    private bool _disposed;

    /// <summary>The running service, for the menu, <c>purr lsp</c> and the Neovim launcher; null when none.</summary>
    internal static KfsLspService? Current { get; private set; }

    /// <summary>The address and token for Neovim, or null while the server is not running. Main thread.</summary>
    public KfsLspEndpoint? Endpoint => _core?.Endpoint;

    /// <summary>The server's address (<c>127.0.0.1:52344</c>) or null.</summary>
    public string? Address => _core?.Address;

    /// <summary>The menu's status line.</summary>
    public string MenuLine => LspServiceText.MenuLine(_core, _offReason);

    /// <summary>Starts the service (see the remarks); never throws. Main thread.</summary>
    public static KfsLspService Start()
    {
        try { Current?.Dispose(); }
        catch (Exception ex) { ModLog.Log.Warning($"purrTTY kFS LSP: stopping the previous service failed ({ex.Message})"); }

        var service = new KfsLspService();
        Current = service;
        try
        {
            service._verb = PurrConsole.Verbs.Add(Verb, VerbHelp, service.OnVerb, needsExtension: false);
        }
        catch (Exception ex)
        {
            ModLog.Log.Warning($"purrTTY kFS LSP: cannot add 'purr {Verb}' ({ex.Message})");
        }
        service.StartServer();
        return service;
    }

    /// <summary>Per frame, main thread, UI phase. Never throws.</summary>
    public void Frame()
    {
        if (_disposed) return;
        _host?.Frame();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Interlocked.Exchange(ref _verb, null)?.Dispose();
        StopServer("purrTTY is unloading");
        try { _feed?.Dispose(); }
        catch (Exception ex) { ModLog.Log.Warning($"purrTTY kFS LSP: stopping the live feed failed ({ex.Message})"); }
        _feed = null;
        RestoreTee();
        _host = null;
        if (ReferenceEquals(Current, this)) Current = null;
    }

    // ------------------------------------------------------------------ start / stop

    private void StartServer()
    {
        try
        {
            KfsConfig config = KfsConfig.Load();
            _allowVerb = config.LspAllowVehicleCommands;
            if (!config.LspEnabled)
            {
                _offReason = "off (lsp_enabled = false in kfs.toml)";
                ModLog.Log.Info($"purrTTY kFS LSP: {_offReason}");
                return;
            }
            _offReason = null;

            if (_host is null && !CreateHost()) return;

            InGameLanguageHost host = _host!;
            var features = new HostFeatureOptions { AllowVerb = _allowVerb, RunPurr = host.RunPurr, Feed = _feed };
            _core = new LspServiceCore(host, new LspServiceOptions
            {
                Port = config.LspPort,
                InfoFilePath = InfoFilePath(),
                KsaBuild = KsaBuild(),
                Configure = server =>
                {
                    StandardFeatures.AddTo(server);
                    HostFeatures.AddTo(server, features);
                },
            });
            _core.Start();   // logs its own status line (address, never the token)
        }
        catch (Exception ex)
        {
            _offReason = $"not running: start failed ({ex.GetType().Name}: {ex.Message})";
            ModLog.Log.Error($"purrTTY kFS LSP: {_offReason}; the rest of purrTTY is unaffected");
        }
    }

    /// <summary>The tables, the feed, the tee (then one workbench read) and the host; on a failure everything made is undone.</summary>
    private bool CreateHost()
    {
        LiveFeed? feed = null;
        OutputTeeInstallation? tee = null;
        try
        {
            GameTables tables = KfsIntegration.Tables;
            feed = new LiveFeed(() => _host?.LiveSource);
            tee = OutputTeeInstallation.Install(() => KfsIntegration.Output, writer => KfsIntegration.Output = writer, feed.WriteOutput, IsGameConsoleWriter);
            _ = KfsIntegration.Workbench;   // the workbench copies Output only when this getter runs
            var host = new InGameLanguageHost(tables, new KsaKfsGameSide()) { Tee = tee.Tee, Feed = feed };
            host.WorkspaceChanged += _ => _core?.WorkspaceChanged();
            (_feed, _tee, _host) = (feed, tee, host);
            return true;
        }
        catch (Exception ex)
        {
            tee?.Restore();
            feed?.Dispose();
            _offReason = $"not running: the in-game host could not be made ({ex.GetType().Name}: {ex.Message})";
            ModLog.Log.Error($"purrTTY kFS LSP: {_offReason}; the rest of purrTTY is unaffected");
            return false;
        }
    }

    /// <summary>
    /// The game's own console writer: <c>KfsIntegration.ConsoleWindowWriter</c>, private, the default of
    /// <c>KfsIntegration.Output</c> (decompiled <c>KfsIntegration.cs:91</c>). Only for it does
    /// <c>KfsIntegration.Error</c> skip its write (it logs the line, which the console shows).
    /// </summary>
    private static bool IsGameConsoleWriter(TextWriter writer) =>
        writer.GetType() == typeof(KfsIntegration).GetNestedType("ConsoleWindowWriter", System.Reflection.BindingFlags.NonPublic);

    private void StopServer(string reason)
    {
        try { _host?.Shutdown(reason); }
        catch (Exception ex) { ModLog.Log.Warning($"purrTTY kFS LSP: failing the queued commands threw ({ex.Message})"); }
        try { _core?.Stop(); }
        catch (Exception ex) { ModLog.Log.Warning($"purrTTY kFS LSP: stop failed ({ex.Message})"); }
        _core = null;
    }

    private void Restart(PurrContext context)
    {
        try { _core?.Stop(); }
        catch (Exception ex) { context.Print($"purr lsp: stop failed ({ex.Message})"); }
        _core = null;
        StartServer();
    }

    private void RestoreTee()
    {
        OutputTeeInstallation? tee = Interlocked.Exchange(ref _tee, null);
        if (tee is null) return;
        OutputTeeRestore outcome = tee.Restore();
        if (outcome == OutputTeeRestore.Foreign)
            ModLog.Log.Warning("purrTTY kFS LSP: another writer replaced KfsIntegration.Output after the language server; it was left in place (the server's tee now only passes text through)");
        try { _ = KfsIntegration.Workbench; }   // hand the workbench the restored writer at once
        catch (Exception ex) { ModLog.Log.Debug($"purrTTY kFS LSP: workbench read at stop failed ({ex.Message})"); }
    }

    // ------------------------------------------------------------------ purr lsp

    private void OnVerb(PurrContext context)
    {
        string args = context.Args.Trim();
        if (args.Equals("token", StringComparison.OrdinalIgnoreCase))
        {
            if (_core?.Token is not { } token)
            {
                context.Print("purr lsp: no token: " + (_offReason ?? _core?.Status ?? "the server is not running"));
                return;
            }
            // To the game's console only: the output tee would otherwise forward it to every editor
            // subscribed to output (their kfs://log buffer).
            OutputTee.EnterConsoleOnly();
            try { context.Print(token); }
            finally { OutputTee.ExitConsoleOnly(); }
            return;
        }
        if (args.Equals("restart", StringComparison.OrdinalIgnoreCase))
        {
            Restart(context);
        }
        else if (args.Length > 0)
        {
            context.Print($"purr lsp: unknown argument '{args}' (usage: purr lsp [token | restart])");
            return;
        }
        context.Print(LspServiceText.Describe(_core, _host, _allowVerb, _feed, _offReason));
    }

    // <config>/.purrTTY/kfs-lsp.json, <config> = KfsConfig.ConfigRoot(): the folder the Neovim
    // launcher passes as PURRTTY_CONFIG_DIR, so both always name the same file.
    private static string? InfoFilePath()
    {
        string? root = KfsConfig.ConfigRoot();
        return root is null ? null : Path.Combine(root, ".purrTTY", LspServerInfoFile.FileName);
    }

    private static string KsaBuild()
    {
        try { return VersionInfo.Current.VersionString; }
        catch { return ""; }
    }
}
