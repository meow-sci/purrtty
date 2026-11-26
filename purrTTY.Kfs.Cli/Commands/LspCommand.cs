// Game types are allowed here: CliApp reaches this file only after KsaAssemblyResolver is installed.

using System.Net.Sockets;
using purrTTY.Kfs.Lsp;
using purrTTY.Kfs.Lsp.Features;

namespace purrTTY.Kfs.Cli.Commands;

/// <summary>
/// <c>purr-kfs lsp</c>: the standalone language server (plan §6.1, §7.6 path 3). <c>--stdio</c> serves
/// one client on stdin/stdout until it exits, the stream ends or the environment's cancellation
/// (Ctrl+C, SIGTERM: the server is cut off and the command returns at once, even while stdin stays
/// open — the read is on a background thread) (exit code: 0 after <c>shutdown</c> + <c>exit</c>, else
/// 1); <c>--listen</c> accepts loopback clients, prints <c>LISTENING &lt;port&gt;</c> on stdout (and
/// <c>TOKEN &lt;t&gt;</c> when it made the token itself: a TCP server never runs without one) and serves
/// until the environment's cancellation. Logs go to stderr.
/// </summary>
internal static class LspCommand
{
    public static int Run(LspOptions options, CliEnvironment environment, TextWriter stdout, TextWriter stderr)
    {
        LspLogSink log = LspLog.Writer(stderr, options.Verbose ? LspLogLevel.Debug : LspLogLevel.Info);
        RuntimeLease lease;
        try
        {
            lease = RuntimeLease.Acquire();
        }
        catch (InvalidOperationException e)
        {
            stderr.WriteLine("purr-kfs: the kFS runtime did not start: " + e.Message);
            return CliApp.ExitUsage;
        }

        using (lease)
        using (var host = new StandaloneLanguageHost(lease.Runtime.Tables, options.Workspace, options.Layout, options.Token, log))
        {
            log.Safe(LspLogLevel.Info, $"workspace {host.WorkspaceRoot}; build target {host.CurrentTarget} — {host.TargetSource}");
            return options.Stdio ? Stdio(host, environment, stderr, log) : Listen(host, options, environment, stdout, stderr, log);
        }
    }

    private static int Stdio(StandaloneLanguageHost host, CliEnvironment environment, TextWriter stderr, LspLogSink log)
    {
        if (environment.OpenStandardInput is null || environment.OpenStandardOutput is null)
        {
            stderr.WriteLine("purr-kfs lsp: --stdio needs the process's standard input and output");
            return CliApp.ExitUsage;
        }
        var server = new KfsLanguageServer(host, environment.OpenStandardInput(), environment.OpenStandardOutput(),
            new KfsLanguageServerOptions { Log = log, Name = "stdio" });
        Configure(server);
        // The read loop blocks in a read of stdin that nothing can interrupt while the pipe stays open:
        // run it on a background thread, and on cancellation cut the server off and return — the
        // process then exits, background threads and all.
        using var ended = new ManualResetEventSlim(false);
        var thread = new Thread(() =>
        {
            try { server.Run(); }
            catch (Exception e) { log.Safe(LspLogLevel.Error, $"stdio: the server failed: {e.GetType().Name}: {e.Message}"); }
            finally { ended.Set(); }
        }) { IsBackground = true, Name = "purr-kfs lsp stdio" };
        thread.Start();
        int which = WaitHandle.WaitAny([ended.WaitHandle, environment.Cancellation.WaitHandle]);
        if (which == 1)
        {
            log.Safe(LspLogLevel.Info, "stdio: stopped by a signal");
            server.Abort();
            ended.Wait(TimeSpan.FromSeconds(1));
            return 1;
        }
        return server.ExitCode;
    }

    /// <summary>
    /// The standard features, then the host features (plan §6.7): the <c>kfs.*</c> commands are
    /// registered so an editor gets a clear <c>ok: false</c> ("needs the game") instead of an unknown
    /// command — except <c>kfs.manual</c>, answered from the tables — and <c>purrtty/subscribe</c> is
    /// accepted with nothing ever sent (no live program outside the game).
    /// </summary>
    private static void Configure(KfsLanguageServer server)
    {
        StandardFeatures.AddTo(server);
        HostFeatures.AddTo(server);
    }

    private static int Listen(StandaloneLanguageHost host, LspOptions options, CliEnvironment environment, TextWriter stdout, TextWriter stderr, LspLogSink log)
    {
        using var listener = new KfsLspListener(host, options.Address, options.Port,
            name => new KfsLanguageServerOptions { Log = log, Name = name }, configure: Configure, log: log);
        try
        {
            listener.Start();
        }
        catch (SocketException e)
        {
            stderr.WriteLine($"purr-kfs lsp: cannot listen on {options.Address}:{options.Port}: {e.Message}");
            return CliApp.ExitUsage;
        }
        stdout.WriteLine($"LISTENING {listener.Port}");
        if (options.TokenGenerated) stdout.WriteLine($"TOKEN {options.Token}");
        stdout.Flush();
        environment.Cancellation.WaitHandle.WaitOne();
        listener.Stop();
        return CliApp.ExitOk;
    }
}
