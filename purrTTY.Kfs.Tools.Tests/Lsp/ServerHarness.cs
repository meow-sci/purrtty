using purrTTY.Kfs.Lsp;
using purrTTY.Kfs.Lsp.Build;

namespace purrTTY.Kfs.Tools.Tests.Lsp;

/// <summary>
/// One <see cref="KfsLanguageServer"/> over in-memory pipes, run on its own thread, with a
/// <see cref="LspTestClient"/> on the other end. Dispose closes the client's ends, waits for the
/// server's thread and fails if the server's <c>Run</c> threw.
/// </summary>
internal sealed class ServerHarness : IDisposable
{
    private readonly Thread _thread;
    private Exception? _failure;

    private ServerHarness(IKfsLanguageHost host, KfsLanguageServerOptions options, Action<KfsLanguageServer>? configure)
    {
        var (serverIn, serverOut, clientIn, clientOut, toServer, toClient) = MemoryPipe.Duplex();
        ToServer = toServer;
        ToClient = toClient;
        Server = new KfsLanguageServer(host, serverIn, serverOut, options);
        configure?.Invoke(Server);
        Client = new LspTestClient(clientIn, clientOut);
        _thread = new Thread(() =>
        {
            try { Server.Run(); }
            catch (Exception e) { _failure = e; }
        }) { IsBackground = true, Name = "LSP test server" };
        _thread.Start();
    }

    public KfsLanguageServer Server { get; }
    public LspTestClient Client { get; }
    public MemoryPipe ToServer { get; }
    public MemoryPipe ToClient { get; }

    /// <summary>The scheduler of the server.</summary>
    public BuildScheduler Builds => Server.Builds;

    /// <summary>
    /// A server for <paramref name="host"/>. Default builds: an <b>infinite</b> debounce (only
    /// <see cref="Flush"/> starts a build), so a test decides exactly when; quiet log.
    /// </summary>
    public static ServerHarness Start(IKfsLanguageHost host, BuildSchedulerOptions? builds = null, Action<KfsLanguageServer>? configure = null,
        LspLogSink? log = null) =>
        new(host, new KfsLanguageServerOptions
        {
            Log = log ?? LspLog.None,
            Name = "test",
            Builds = builds ?? new BuildSchedulerOptions { Debounce = System.Threading.Timeout.InfiniteTimeSpan },
        }, configure);

    /// <summary>Waits until the server processed everything sent so far, then starts the builds it made dirty.</summary>
    public void Flush()
    {
        Client.Sync();
        Server.Builds.Flush();
    }

    /// <summary>True when the server's <c>Run</c> returned within the timeout.</summary>
    public bool WaitForServerEnd(TimeSpan? timeout = null) => _thread.Join(timeout ?? LspTestClient.Timeout);

    public void Dispose()
    {
        Client.Dispose();
        Server.Close();
        bool ended = _thread.Join(LspTestClient.Timeout);
        if (!ended) throw new InvalidOperationException("the server's Run did not return after its streams closed");
        if (_failure is not null) throw new InvalidOperationException("the server's Run threw", _failure);
    }
}
