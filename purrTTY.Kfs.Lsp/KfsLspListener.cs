using System.Net;
using System.Net.Sockets;

namespace purrTTY.Kfs.Lsp;

/// <summary>Limits of a <see cref="KfsLspListener"/>: what an unauthenticated peer can cost the process.</summary>
public sealed class KfsLspListenerLimits
{
    /// <summary>Connections served at once (default 8); one more is closed at accept.</summary>
    public int MaxConnections { get; init; } = 8;

    /// <summary>Connections that have not completed a successful <c>initialize</c> yet (default 2); one more is closed at accept.</summary>
    public int MaxUnauthenticated { get; init; } = 2;

    /// <summary>How long a connection may take to complete a successful <c>initialize</c> (default 5 s); then it is closed.</summary>
    public TimeSpan InitializeTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>The socket's send timeout (default 10 s): a write to a peer that stopped reading fails, and the connection closes.</summary>
    public TimeSpan SendTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>The first wait after an accept that failed (default 100 ms); it doubles with each further failure in a row up to <see cref="MaxAcceptBackoff"/>.</summary>
    public TimeSpan AcceptBackoff { get; init; } = TimeSpan.FromMilliseconds(100);

    /// <summary>The longest wait between two failing accepts (default 1 s).</summary>
    public TimeSpan MaxAcceptBackoff { get; init; } = TimeSpan.FromSeconds(1);
}

/// <summary>
/// A loopback-only TCP accept loop (plan §6.1): one <see cref="KfsLanguageServer"/> per client, on its
/// own thread, several clients at once. <see cref="Start"/> binds (port 0 picks a free port:
/// <see cref="Port"/>); <see cref="Stop"/> stops accepting, closes every client and waits for every
/// thread it started (accept, client, writer and compile threads) to end.
/// <list type="bullet">
/// <item><b>Limits</b> (<see cref="KfsLspListenerLimits"/>): at most 8 connections, at most 2 of them
/// not yet authenticated (excess is closed at accept); a connection that has not completed a
/// successful <c>initialize</c> within 5 s is closed; a write blocked for 10 s closes its connection.</item>
/// <item><b>Robust accept loop.</b> Nothing a peer does can end it: a connection that fails while it is
/// being set up (a peer that reset it at once, a failed thread start) is disposed and logged at
/// Debug, and accepting carries on; a persistent accept error (<c>EMFILE</c>) backs off from 100 ms
/// to 1 s and is logged once per error code.</item>
/// </list>
/// </summary>
public sealed class KfsLspListener : IDisposable
{
    private readonly IKfsLanguageHost _host;
    private readonly Func<string, KfsLanguageServerOptions> _options;
    private readonly Action<KfsLanguageServer>? _configure;
    private readonly LspLogSink _log;
    private readonly IPAddress _address;
    private readonly int _requestedPort;
    private readonly object _gate = new();
    private readonly Dictionary<KfsLanguageServer, Thread> _clients = [];
    private readonly Dictionary<KfsLanguageServer, Timer> _deadlines = [];
    private readonly KfsLspListenerLimits _limits;
    private readonly ManualResetEventSlim _stopping = new(false);
    private readonly HashSet<string> _loggedAcceptErrors = new(StringComparer.Ordinal);
    private TcpListener? _listener;
    private long _refused;
    private Thread? _accept;
    private int _connections;
    private bool _stopped;

    /// <summary>A listener for <paramref name="host"/> on <paramref name="address"/>:<paramref name="port"/> (loopback only; port 0 picks one).</summary>
    /// <param name="host">The host every connection serves.</param>
    /// <param name="address">A loopback address (<c>127.0.0.1</c>, <c>::1</c>).</param>
    /// <param name="port">The port; 0 picks a free one.</param>
    /// <param name="options">The options of each connection's server, given its log name (default: <see cref="KfsLanguageServerOptions"/> with that name).</param>
    /// <param name="configure">Called for each new server before it reads anything: register feature providers here.</param>
    /// <param name="log">Where the listener itself logs.</param>
    /// <param name="limits">Connection limits and deadlines (default: <see cref="KfsLspListenerLimits"/>).</param>
    public KfsLspListener(IKfsLanguageHost host, IPAddress address, int port = 0, Func<string, KfsLanguageServerOptions>? options = null,
        Action<KfsLanguageServer>? configure = null, LspLogSink? log = null, KfsLspListenerLimits? limits = null)
    {
        _limits = limits ?? new KfsLspListenerLimits();
        ArgumentOutOfRangeException.ThrowIfLessThan(_limits.MaxConnections, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(_limits.MaxUnauthenticated, 1);
        _host = host ?? throw new ArgumentNullException(nameof(host));
        ArgumentNullException.ThrowIfNull(address);
        if (!IPAddress.IsLoopback(address)) throw new ArgumentException($"the language server listens on loopback only, not {address}", nameof(address));
        ArgumentOutOfRangeException.ThrowIfNegative(port);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, IPEndPoint.MaxPort);
        _address = address;
        _requestedPort = port;
        _options = options ?? (name => new KfsLanguageServerOptions { Name = name });
        _configure = configure;
        _log = log ?? LspLog.ModLog;
    }

    /// <summary>The bound port (valid after <see cref="Start"/>).</summary>
    public int Port { get; private set; }

    /// <summary>The bound address.</summary>
    public IPAddress Address => _address;

    /// <summary>The servers of the clients connected now.</summary>
    public IReadOnlyList<KfsLanguageServer> Clients
    {
        get { lock (_gate) return _clients.Keys.ToList(); }
    }

    /// <summary>Connections closed at accept because a limit was reached, since start.</summary>
    public long Refused => Interlocked.Read(ref _refused);

    /// <summary>Tests: replaces <c>AcceptTcpClient</c> (to inject a persistent accept error).</summary>
    internal Func<TcpListener, TcpClient>? AcceptOverride { get; init; }

    /// <summary>The limits in force.</summary>
    public KfsLspListenerLimits Limits => _limits;

    /// <summary>Raised (on the client's thread) when a client's server is created, before it reads.</summary>
    public event Action<KfsLanguageServer>? Connected;

    /// <summary>Binds and starts accepting. Throws <see cref="SocketException"/> when the port is taken.</summary>
    public void Start()
    {
        lock (_gate)
        {
            if (_listener is not null || _stopped) throw new InvalidOperationException("the listener was already started");
            var listener = new TcpListener(_address, _requestedPort);
            listener.Start();
            _listener = listener;
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            _accept = LspThreads.Start($"purrTTY kFS LSP accept :{Port}", AcceptLoop);
            if (_accept is null)
            {
                _stopped = true;
                try { listener.Stop(); }
                catch (SocketException) { /* stopping */ }
                throw new InvalidOperationException("no thread could be started for the accept loop (the process is at its thread limit)");
            }
        }
        _log.Safe(LspLogLevel.Info, $"listening on {_address}:{Port} (workspace {_host.WorkspaceRoot})");
    }

    /// <summary>
    /// Stops accepting, closes every client (pending writes are flushed; a client that does not
    /// read is cut off; <paramref name="abort"/>: every connection is cut off at once, nothing is
    /// flushed — the game's main thread must not wait) and waits up to <paramref name="timeout"/>
    /// (default 10 s) for every thread the listener started. True when they all ended.
    /// </summary>
    public bool Stop(TimeSpan? timeout = null, bool abort = false)
    {
        TimeSpan limit = timeout ?? TimeSpan.FromSeconds(10);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        Thread? accept;
        List<(KfsLanguageServer Server, Thread Thread)> clients;
        lock (_gate)
        {
            if (_stopped) return true;
            _stopped = true;
            _stopping.Set();
            try { _listener?.Stop(); }
            catch (SocketException) { /* stopping */ }
            accept = _accept;
            clients = _clients.Select(c => (c.Key, c.Value)).ToList();
        }
        bool ok = accept is null || accept == Thread.CurrentThread || accept.Join(Left(limit, clock));
        foreach ((KfsLanguageServer server, _) in clients)
        {
            if (abort) server.Abort("the listener stopped");
            else server.Close();
        }
        foreach ((KfsLanguageServer server, Thread thread) in clients)
        {
            if (thread == Thread.CurrentThread) continue;
            if (!thread.Join(Left(limit, clock) / 2))
            {
                server.Abort();
                ok &= thread.Join(Left(limit, clock));
            }
        }
        _log.Safe(LspLogLevel.Info, $"stopped listening on {_address}:{Port}");
        return ok;
    }

    /// <inheritdoc/>
    public void Dispose() => Stop();

    private static TimeSpan Left(TimeSpan limit, System.Diagnostics.Stopwatch clock)
    {
        TimeSpan left = limit - clock.Elapsed;
        return left > TimeSpan.Zero ? left : TimeSpan.Zero;
    }

    private void AcceptLoop()
    {
        TimeSpan backoff = TimeSpan.Zero;
        while (true)
        {
            TcpListener? listener;
            lock (_gate)
            {
                if (_stopped) return;
                listener = _listener;
            }
            if (listener is null) return;
            TcpClient client;
            try
            {
                client = AcceptOverride is { } accept ? accept(listener) : listener.AcceptTcpClient();
                backoff = TimeSpan.Zero;
            }
            catch (Exception e)
            {
                lock (_gate)
                {
                    if (_stopped) return;
                }
                // A persistent error (EMFILE: out of descriptors) must not spin: back off, log once per code.
                string code = e is SocketException se ? se.SocketErrorCode.ToString() : e.GetType().Name;
                bool first;
                lock (_gate) first = _loggedAcceptErrors.Add(code);
                _log.Safe(first ? LspLogLevel.Warning : LspLogLevel.Debug, $"accept failed ({code}): {e.Message}{(first ? "; retrying with a back-off (logged once per error)" : "")}");
                backoff = backoff == TimeSpan.Zero ? _limits.AcceptBackoff : TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, _limits.MaxAcceptBackoff.Ticks));
                _stopping.Wait(backoff);
                continue;
            }
            Serve(client);
        }
    }

    /// <summary>Sets up and starts one connection. Never throws: a connection that fails here is disposed and accepting carries on.</summary>
    private void Serve(TcpClient client)
    {
        string name = "tcp";
        KfsLanguageServer? server = null;
        Timer? deadline = null;
        bool started = false;
        try
        {
            EndPoint? remote = client.Client.RemoteEndPoint;
            if (remote is not IPEndPoint ip || !IPAddress.IsLoopback(ip.Address))
            {
                _log.Safe(LspLogLevel.Warning, $"refused a connection from {remote}: not loopback");
                return;
            }
            if (RefusedByLimits(remote)) return;
            // On macOS setsockopt on a socket the peer already reset throws (EINVAL): inside the try.
            client.NoDelay = true;
            client.SendTimeout = (int)Math.Clamp(_limits.SendTimeout.TotalMilliseconds, 1, int.MaxValue);
            int number = Interlocked.Increment(ref _connections);
            name = $"tcp#{number} {remote}";
            NetworkStream stream = client.GetStream();
            server = new KfsLanguageServer(_host, stream, stream, _options(name));
            try
            {
                _configure?.Invoke(server);
            }
            catch (Exception e)
            {
                _log.Safe(LspLogLevel.Error, $"{name}: could not start a server: {e.GetType().Name}: {e.Message}");
                return;
            }

            lock (_gate)
            {
                if (_stopped) return;
                KfsLanguageServer s = server;
                string n = name;
                var ready = new ManualResetEventSlim(false);
                Thread? thread = LspThreads.Start($"purrTTY kFS LSP {name}", () =>
                {
                    ready.Wait();
                    try
                    {
                        // Debug: every editor probes the port first (connect, close); the server's
                        // "initialized" line is the Info record of a real client.
                        _log.Safe(LspLogLevel.Debug, $"{n}: connected");
                        try { Connected?.Invoke(s); }
                        catch (Exception e) { _log.Safe(LspLogLevel.Error, $"{n}: a Connected handler threw {e.GetType().Name}: {e.Message}"); }
                        s.Run();
                    }
                    finally
                    {
                        lock (_gate)
                        {
                            if (_deadlines.Remove(s, out Timer? t)) t.Dispose();
                            _clients.Remove(s);
                        }
                        try { client.Dispose(); } catch { /* closing */ }
                        ready.Dispose();
                    }
                });
                if (thread is null)
                {
                    ready.Dispose();
                    return;
                }
                started = true;
                _clients[s] = thread;
                deadline = new Timer(_ => Expire(s, n), null, _limits.InitializeTimeout, Timeout.InfiniteTimeSpan);
                _deadlines[s] = deadline;
                ready.Set();
            }
        }
        catch (Exception e)
        {
            // A peer that reset the connection before we got to it, and the like: not worth more than Debug.
            _log.Safe(LspLogLevel.Debug, $"{name}: the connection failed while it was set up: {e.GetType().Name}: {e.Message}");
        }
        finally
        {
            if (!started)
            {
                // Whatever the configure callback registered on the host or a shared feed is released (Closed).
                try { server?.Dispose(); }
                catch (Exception d) { _log.Safe(LspLogLevel.Error, $"{name}: disposing the server threw {d.GetType().Name}: {d.Message}"); }
                try { client.Dispose(); } catch { /* closing */ }
            }
        }
    }

    /// <summary>True (the client is not served) when the connection or unauthenticated limit is reached.</summary>
    private bool RefusedByLimits(EndPoint remote)
    {
        int total, unauthenticated;
        lock (_gate)
        {
            total = _clients.Count;
            unauthenticated = _clients.Keys.Count(c => !c.IsAuthenticated);
        }
        string? why = total >= _limits.MaxConnections ? $"{total} connections are open (at most {_limits.MaxConnections})"
            : unauthenticated >= _limits.MaxUnauthenticated ? $"{unauthenticated} connections have not initialized yet (at most {_limits.MaxUnauthenticated})"
            : null;
        if (why is null) return false;
        long refused = Interlocked.Increment(ref _refused);
        // The first refusal, and then one in a thousand, at Warning: a flood must not flood the log.
        _log.Safe(refused == 1 || refused % 1000 == 0 ? LspLogLevel.Warning : LspLogLevel.Debug, $"refused a connection from {remote}: {why} ({refused} refused so far)");
        return true;
    }

    /// <summary>The initialize deadline of one connection (a timer callback: never throws).</summary>
    private void Expire(KfsLanguageServer server, string name)
    {
        try
        {
            if (server.IsAuthenticated || server.State == LspServerState.Closed) return;
            _log.Safe(LspLogLevel.Debug, $"{name}: no successful initialize within {_limits.InitializeTimeout.TotalSeconds:0.#} s; closing");
            server.Abort("no successful initialize in time");
        }
        catch (Exception e)
        {
            _log.Safe(LspLogLevel.Error, $"{name}: the initialize deadline failed: {e.GetType().Name}: {e.Message}");
        }
    }
}
