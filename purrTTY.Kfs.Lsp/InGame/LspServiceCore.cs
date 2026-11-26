using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using KSA.Kfs.Vm.Tables;
using purrTTY.Kfs.Runtime;

namespace purrTTY.Kfs.Lsp.InGame;

/// <summary>Session tokens (plan R12).</summary>
public static class LspToken
{
    /// <summary>A new token: 32 bytes of the cryptographic RNG (256 bits) as base64url without padding (43 characters).</summary>
    public static string New()
    {
        byte[] bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}

/// <summary>Settings of an <see cref="LspServiceCore"/>.</summary>
public sealed class LspServiceOptions
{
    /// <summary>The loopback address (default <c>127.0.0.1</c>).</summary>
    public IPAddress Address { get; init; } = IPAddress.Loopback;

    /// <summary>The port; 0 picks a free one (default).</summary>
    public int Port { get; init; }

    /// <summary>The server info file; null: none is written.</summary>
    public string? InfoFilePath { get; init; }

    /// <summary>The game's build for the info file.</summary>
    public string KsaBuild { get; init; } = "";

    /// <summary>The process id for the info file.</summary>
    public int ProcessId { get; init; } = Environment.ProcessId;

    /// <summary>Called for each new connection's server before it reads anything (register the features).</summary>
    public Action<KfsLanguageServer>? Configure { get; init; }

    /// <summary>The options of each connection's server, given its log name; null: defaults with <see cref="Log"/>.</summary>
    public Func<string, KfsLanguageServerOptions>? ServerOptions { get; init; }

    /// <summary>Where the service, the listener and (by default) the connections log.</summary>
    public LspLogSink Log { get; init; } = LspLog.ModLog;

    /// <summary>Makes a token per start (default <see cref="LspToken.New"/>).</summary>
    public Func<string> NewToken { get; init; } = LspToken.New;

    /// <summary>
    /// How long <see cref="LspServiceCore.Stop"/> — called on the game's main thread — waits for the
    /// listener's threads after cutting every connection off (default 500 ms). Retired compile threads
    /// (an abandoned compile, which cannot be interrupted) are never waited for.
    /// </summary>
    public TimeSpan StopTimeout { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>The listener's connection limits and deadlines (default: <see cref="KfsLspListenerLimits"/>).</summary>
    public KfsLspListenerLimits? Limits { get; init; }
}

/// <summary>
/// The language-server service inside the game, without the game (plan §6.1, §7.6): owns the
/// loopback listener, the per-start token and the server info file, and keeps a one-line status.
/// <list type="bullet">
/// <item><see cref="Start"/>: a new token; the listener on the configured port (port in use → a
/// clean failure in <see cref="Status"/>, nothing left behind); the info file; one status line in the
/// log with the address and never the token.</item>
/// <item><see cref="WorkspaceChanged"/> rewrites the info file.</item>
/// <item><see cref="Stop"/> (the game's main thread): cuts every connection off at once, waits briefly
/// (<see cref="LspServiceOptions.StopTimeout"/>, 500 ms) for the threads the listener started —
/// <see cref="LspThreads.Active"/> back to its value at start; retired compile threads are not waited
/// for, and a compile still running is reported in <see cref="Status"/> (the extension's uninstall
/// then leaves its header registered) —, deletes the info file when it is provably this start's,
/// forgets the token.</item>
/// </list>
/// Calls are serialised; none throws.
/// </summary>
public sealed class LspServiceCore : IDisposable
{
    private readonly IKfsLanguageHost _host;
    private readonly LspServiceOptions _options;
    private readonly object _gate = new();
    private KfsLspListener? _listener;
    private string? _token;
    private string? _address;
    private string _status = "not started";
    private string? _infoProblem;
    private int _threadsBefore;

    /// <summary>A service for <paramref name="host"/>.</summary>
    public LspServiceCore(IKfsLanguageHost host, LspServiceOptions options)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>True while listening.</summary>
    public bool IsRunning
    {
        get { lock (_gate) return _listener is not null; }
    }

    /// <summary><c>127.0.0.1:&lt;port&gt;</c> while running, else null.</summary>
    public string? Address
    {
        get { lock (_gate) return _address; }
    }

    /// <summary>This start's token while running, else null.</summary>
    public string? Token
    {
        get { lock (_gate) return _token; }
    }

    /// <summary>The address and token for a client (the Neovim launcher), or null while not running.</summary>
    public KfsLspEndpoint? Endpoint
    {
        get
        {
            lock (_gate) return _address is not null && _token is not null ? new KfsLspEndpoint(_address, _token) : null;
        }
    }

    /// <summary>Connected clients now.</summary>
    public int Clients
    {
        get
        {
            KfsLspListener? listener;
            lock (_gate) listener = _listener;
            return listener?.Clients.Count ?? 0;
        }
    }

    /// <summary>One line: <c>listening on 127.0.0.1:52344 …</c>, <c>not running: …</c>, <c>stopped</c>. Never the token.</summary>
    public string Status
    {
        get { lock (_gate) return _status; }
    }

    /// <summary>The info file's path (null: none), and why it could not be written (null: it was).</summary>
    public (string? Path, string? Problem) InfoFile
    {
        get { lock (_gate) return (_options.InfoFilePath, _infoProblem); }
    }

    /// <summary>Starts listening; true on success. Already running: true, nothing changes.</summary>
    public bool Start()
    {
        lock (_gate)
        {
            if (_listener is not null) return true;
            string token;
            try
            {
                token = _options.NewToken();
                if (string.IsNullOrEmpty(token)) throw new InvalidOperationException("the token source returned nothing");
            }
            catch (Exception e)
            {
                return Fail($"no token ({e.GetType().Name}: {e.Message})");
            }

            _threadsBefore = LspThreads.Active;
            var host = new TokenHost(_host, token);
            LspLogSink log = _options.Log;
            var listener = new KfsLspListener(host, _options.Address, _options.Port,
                _options.ServerOptions ?? (name => new KfsLanguageServerOptions { Name = name, Log = log }),
                _options.Configure, ListenerLog(log), _options.Limits);
            try
            {
                listener.Start();
            }
            catch (Exception e) when (e is SocketException or InvalidOperationException or UnauthorizedAccessException)
            {
                listener.Stop(_options.StopTimeout);
                string why = e is SocketException { SocketErrorCode: SocketError.AddressAlreadyInUse }
                    ? $"port {_options.Port} is in use"
                    : $"{e.GetType().Name}: {e.Message}";
                return Fail($"cannot listen on {_options.Address}:{_options.Port} ({why}); set lsp_port = 0 in kfs.toml to pick a free port");
            }

            _listener = listener;
            _token = token;
            _address = $"{FormatAddress(_options.Address)}:{listener.Port.ToString(CultureInfo.InvariantCulture)}";
            WriteInfoFile();
            _status = $"listening on {_address}, workspace {_host.WorkspaceRoot}" + InfoWords();
            _options.Log.Safe(LspLogLevel.Info, _status);
            return true;
        }
    }

    /// <summary>Stops listening (see the class remarks); true when every thread ended in time. Not running: true.</summary>
    public bool Stop()
    {
        KfsLspListener? listener;
        string? token;
        lock (_gate)
        {
            listener = _listener;
            token = _token;
            if (listener is null) return true;
            _listener = null;
            _token = null;
            _address = null;
        }
        var clock = System.Diagnostics.Stopwatch.StartNew();
        // On the game's main thread: every connection is cut off at once (nothing is flushed), and the
        // wait is short. An idle compile thread ends at once; a busy one is retired by its scheduler's
        // Dispose and ends when its compile returns — never waited for here. The extension's uninstall
        // checks SystemHeaderReaders before it touches the game's header table.
        bool joined = listener.Stop(_options.StopTimeout, abort: true);
        TimeSpan left = _options.StopTimeout - clock.Elapsed;
        joined &= LspThreads.WaitUntilActiveAtMost(_threadsBefore, left > TimeSpan.Zero ? left : TimeSpan.Zero);
        int compiles = purrTTY.Kfs.Tables.SystemHeaderReaders.Compiles;
        string still = compiles > 0 ? $"; {compiles} compile{(compiles == 1 ? " is" : "s are")} still running and will end by {(compiles == 1 ? "itself" : "themselves")}" : "";
        lock (_gate)
        {
            if (_options.InfoFilePath is { } path && token is not null && !LspServerInfoFile.DeleteIfOurs(path, token, _options.ProcessId))
                _options.Log.Safe(LspLogLevel.Debug, $"the server info file {path} was left in place (not this start's, or not deletable)");
            _infoProblem = null;
            _status = (joined ? "stopped" : "stopped (some threads did not end in time)") + still;
        }
        _options.Log.Safe(joined && still.Length == 0 ? LspLogLevel.Info : LspLogLevel.Warning,
            (joined ? "stopped" : "stopped; some language-server threads did not end in time") + still);
        return joined;
    }

    /// <summary>Stops and starts again with a new token; true when it is running afterwards.</summary>
    public bool Restart()
    {
        Stop();
        return Start();
    }

    /// <summary>The workspace folder changed: rewrites the info file (no-op while not running).</summary>
    public void WorkspaceChanged()
    {
        lock (_gate)
        {
            if (_listener is null) return;
            WriteInfoFile();
            _status = $"listening on {_address}, workspace {_host.WorkspaceRoot}" + InfoWords();
        }
        _options.Log.Safe(LspLogLevel.Info, $"workspace is now {_host.WorkspaceRoot}; new connections use it" + InfoWords());
    }

    /// <inheritdoc/>
    public void Dispose() => Stop();

    // ------------------------------------------------------------------ helpers (under _gate)

    private bool Fail(string why)
    {
        _status = "not running: " + why;
        _options.Log.Safe(LspLogLevel.Warning, _status);
        return false;
    }

    private void WriteInfoFile()
    {
        _infoProblem = null;
        if (_options.InfoFilePath is not { } path || _address is null || _token is null) return;
        try
        {
            LspServerInfoFile.Write(path, new LspServerInfo(_address, _token, _options.ProcessId, _options.KsaBuild, _host.WorkspaceRoot));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            _infoProblem = e.Message;
        }
    }

    private string InfoWords() =>
        _options.InfoFilePath is null ? "" : _infoProblem is null ? $", server info {_options.InfoFilePath}" : $", no server info file ({_infoProblem})";

    private static string FormatAddress(IPAddress address) =>
        address.AddressFamily == AddressFamily.InterNetworkV6 ? $"[{address}]" : address.ToString();

    /// <summary>The listener's log without its own "listening on" line (the service's status line says it, with more).</summary>
    private static LspLogSink ListenerLog(LspLogSink log) => (level, message) =>
    {
        if (level == LspLogLevel.Info && (message.StartsWith("listening on ", StringComparison.Ordinal) || message.StartsWith("stopped listening", StringComparison.Ordinal))) return;
        log(level, message);
    };

    /// <summary>The host as one start of the service serves it: everything from the inner host, the token of this start.</summary>
    private sealed class TokenHost(IKfsLanguageHost inner, string token) : IKfsLanguageHost
    {
        public GameTables Tables => inner.Tables;
        public string WorkspaceRoot => inner.WorkspaceRoot;
        public BuildTarget CurrentTarget => inner.CurrentTarget;

        public event Action? TargetChanged
        {
            add => inner.TargetChanged += value;
            remove => inner.TargetChanged -= value;
        }

        public bool CanControlVehicle => inner.CanControlVehicle;
        public Task<CommandResult> RunVerb(string verb, CancellationToken ct) => inner.RunVerb(verb, ct);
        public ILiveProgram? Live => inner.Live;
        public string? Token => token;
    }
}
