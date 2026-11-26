using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using purrTTY.Kfs.Lsp.Build;
using purrTTY.Kfs.Lsp.Documents;
using purrTTY.Kfs.Lsp.Protocol;

namespace purrTTY.Kfs.Lsp;

/// <summary>Settings of a <see cref="KfsLanguageServer"/>.</summary>
public sealed class KfsLanguageServerOptions
{
    /// <summary>Where the server logs (default: purrTTY's game log). Never the protocol stream.</summary>
    public LspLogSink Log { get; init; } = LspLog.ModLog;

    /// <summary>The build scheduler's settings (debounce, watchdog, compile function).</summary>
    public BuildSchedulerOptions Builds { get; init; } = new();

    /// <summary>The largest message accepted once initialized (default 16 MiB; a larger one is skipped).</summary>
    public int MaxMessageBytes { get; init; } = JsonRpcOptions.DefaultMaxMessageBytes;

    /// <summary>The largest message accepted before a successful <c>initialize</c> (default 64 KiB); a larger one closes the connection unread.</summary>
    public int MaxPreAuthMessageBytes { get; init; } = 64 * 1024;

    /// <summary>The most bytes of outgoing messages waiting for a peer that does not read (default 16 MiB); more cuts it off.</summary>
    public long MaxQueuedBytes { get; init; } = JsonRpcOptions.DefaultMaxQueuedBytes;

    /// <summary>A name for log lines (<c>stdio</c>, <c>tcp#2 127.0.0.1:50122</c>).</summary>
    public string Name { get; init; } = "client";

    /// <summary>The <c>serverInfo.name</c> sent to the client.</summary>
    public string ServerName { get; init; } = "purrTTY kFS";
}

/// <summary>Where a <see cref="KfsLanguageServer"/> is in the LSP lifecycle.</summary>
public enum LspServerState
{
    /// <summary>Waiting for <c>initialize</c>.</summary>
    Created,
    /// <summary>Initialized: serving.</summary>
    Running,
    /// <summary><c>shutdown</c> received: only <c>exit</c> is accepted.</summary>
    ShutDown,
    /// <summary>The connection is closed.</summary>
    Closed,
}

/// <summary>
/// The language server for one connection, over any pair of streams (plan §6.1–§6.2). Lifecycle:
/// <c>initialize</c> (the token check — <c>initializationOptions.token</c> must equal the host's
/// <see cref="IKfsLanguageHost.Token"/> when it has one, otherwise the server answers with an error
/// and closes; <c>positionEncoding</c> negotiated, <c>utf-16</c> preferred; capabilities: full text sync
/// plus whatever <see cref="Features"/> has registered), <c>initialized</c>, <c>shutdown</c>, and
/// <c>exit</c>, which closes <b>this connection only</b>, never the process. Before a successful
/// <c>initialize</c> a message may be at most <see cref="KfsLanguageServerOptions.MaxPreAuthMessageBytes"/>
/// (a larger one closes the connection unread) and, when the host has a token, any request but
/// <c>initialize</c> is answered <c>ServerNotInitialized</c> and closes the connection; nothing is
/// built (the compile thread starts with the first build). Documents:
/// <c>didOpen</c>/<c>didChange</c>/<c>didClose</c>/<c>didSave</c> feed the <see cref="Documents"/> and the
/// <see cref="Builds"/> scheduler, whose results are published as <c>textDocument/publishDiagnostics</c>
/// for every file of each project; <c>workspace/didChangeWatchedFiles</c> and a host
/// <see cref="IKfsLanguageHost.TargetChanged"/> re-diagnose every open project. Unknown requests are
/// answered <c>MethodNotFound</c>; unknown notifications are ignored.
/// </summary>
/// <remarks>
/// Threads: <see cref="Run"/> blocks its caller with the read loop (requests and notifications are
/// dispatched there, in order); the connection has its own writer thread and the scheduler its
/// compile thread. Feature handlers run on the read thread until they await.
/// </remarks>
public sealed class KfsLanguageServer : IDisposable
{
    private static readonly HashSet<string> BuiltIn = new(StringComparer.Ordinal)
    {
        "initialize", "initialized", "shutdown", "exit", "$/cancelRequest",
        "textDocument/didOpen", "textDocument/didChange", "textDocument/didClose", "textDocument/didSave",
        "textDocument/willSave", "textDocument/willSaveWaitUntil",
        "workspace/didChangeWatchedFiles", "workspace/executeCommand",
    };

    private readonly KfsLanguageServerOptions _options;
    private readonly JsonRpcConnection _connection;
    private readonly LspLogSink _log;
    private readonly object _stateGate = new();
    private readonly object _publishGate = new();
    private readonly Dictionary<string, long> _lastSequence = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _projectPaths = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SortedDictionary<string, List<LspDiagnostic>>> _contributions = new(StringComparer.Ordinal);
    private LspServerState _state = LspServerState.Created;
    private bool _shutdownReceived;
    private bool _initialized;
    private int _lifetime;   // 0 not run, 1 running, 2 finished (Closed raised)

    /// <summary>A server for <paramref name="host"/> over <paramref name="input"/> / <paramref name="output"/> (closed with the connection when <paramref name="ownsStreams"/>).</summary>
    public KfsLanguageServer(IKfsLanguageHost host, Stream input, Stream output, KfsLanguageServerOptions? options = null, bool ownsStreams = true)
    {
        Host = host ?? throw new ArgumentNullException(nameof(host));
        _options = options ?? new KfsLanguageServerOptions();
        _log = _options.Log;
        _connection = new JsonRpcConnection(input, output, new JsonRpcOptions
        {
            MaxMessageBytes = _options.MaxMessageBytes,
            MaxQueuedBytes = _options.MaxQueuedBytes,
            Name = _options.Name,
            Log = _log,
        }, ownsStreams)
        {
            RequestHandler = OnRequest,
            NotificationHandler = OnNotification,
        };
        _connection.LimitMessages(_options.MaxPreAuthMessageBytes, closeWhenExceeded: true);
        Documents = new DocumentStore(new WorkspaceUris(host.WorkspaceRoot));
        Builds = new BuildScheduler(host, Documents, _options.Builds, _log, _options.Name);
        Builds.Built += OnBuilt;
        Builds.Removed += OnRemoved;
        Host.TargetChanged += OnTargetChanged;
    }

    /// <summary>The host.</summary>
    public IKfsLanguageHost Host { get; }

    /// <summary>The feature providers; register before the client's <c>initialize</c>.</summary>
    public LspFeatures Features { get; } = new();

    /// <summary>The open documents.</summary>
    public DocumentStore Documents { get; }

    /// <summary>The build scheduler: the last (good) build per project, its <c>KfsProject</c> and compile report.</summary>
    public BuildScheduler Builds { get; }

    /// <summary>The negotiated position encoding (<c>utf-16</c> until <c>initialize</c>).</summary>
    public PositionEncoding Encoding { get; private set; } = PositionEncoding.Utf16;

    /// <summary>The client's <c>capabilities</c> from <c>initialize</c>, or null before it.</summary>
    public JsonObject? ClientCapabilities { get; private set; }

    /// <summary>The client's <c>initializationOptions</c>, or null.</summary>
    public JsonObject? InitializationOptions { get; private set; }

    /// <summary>The lifecycle state.</summary>
    public LspServerState State
    {
        get { lock (_stateGate) return _state; }
    }

    /// <summary>True once <c>initialize</c> succeeded (the token, when the host has one, was right).</summary>
    public bool IsAuthenticated
    {
        get { lock (_stateGate) return _initialized; }
    }

    /// <summary>Bytes of outgoing messages waiting for the client now.</summary>
    public long QueuedBytes => _connection.QueuedBytes;

    /// <summary>The LSP exit code once closed: 0 when <c>shutdown</c> came before <c>exit</c>, else 1.</summary>
    public int ExitCode { get; private set; } = 1;

    /// <summary>The connection's name in log lines.</summary>
    public string Name => _options.Name;

    /// <summary>Why the connection closed, once it has.</summary>
    public string? CloseReason => _connection.CloseReason;

    /// <summary>
    /// Raised once when the server stopped: when <see cref="Run"/> returns, or at <see cref="Dispose"/>
    /// for a server that never ran (a listener stopping between accept and run), so every handler
    /// registered on the host or a shared feed is always released.
    /// </summary>
    public event Action<KfsLanguageServer>? Closed;

    /// <summary>Serves the connection on the calling thread until it closes (exit, end of stream, refusal, <see cref="Close"/>). Never throws. A disposed server returns at once.</summary>
    public void Run()
    {
        if (Interlocked.CompareExchange(ref _lifetime, 1, 0) != 0) return;
        try
        {
            _connection.Run();
        }
        finally
        {
            Finish(_connection.CloseReason ?? "end of stream");
        }
    }

    /// <summary>Releases the host subscription and the scheduler, marks the server closed and raises <see cref="Closed"/>.</summary>
    private void Finish(string reason)
    {
        Volatile.Write(ref _lifetime, 2);
        Host.TargetChanged -= OnTargetChanged;
        Builds.Dispose();
        bool initialized;
        lock (_stateGate)
        {
            _state = LspServerState.Closed;
            initialized = _initialized;
        }
        // A connection that never sent initialize (the editor's port probe, a scanner) is noise at Info.
        _log.Safe(initialized ? LspLogLevel.Info : LspLogLevel.Debug, $"{Name}: closed ({reason})");
        try { Closed?.Invoke(this); }
        catch (Exception e) { _log.Safe(LspLogLevel.Error, $"{Name}: a Closed handler threw {e.GetType().Name}: {e.Message}"); }
    }

    /// <summary>Sends a server→client notification (queued; ignored once closed).</summary>
    public void Notify(string method, JsonNode? @params) => _connection.Notify(method, @params);

    /// <summary>Sends a server→client request.</summary>
    public Task<JsonNode?> Request(string method, JsonNode? @params, CancellationToken cancellationToken = default) =>
        _connection.SendRequest(method, @params, cancellationToken);

    /// <summary>Closes the connection (pending writes are flushed); <see cref="Run"/> returns.</summary>
    public void Close() => _connection.Close();

    /// <summary>Closes the connection at once, dropping pending writes (a stuck peer).</summary>
    public void Abort() => _connection.Abort();

    /// <summary>Closes the connection at once with <paramref name="reason"/> (the listener's deadlines).</summary>
    internal void Abort(string reason) => _connection.Abort(reason);

    /// <inheritdoc/>
    public void Dispose()
    {
        Close();
        // Never ran: Run will not raise Closed, so do it here (once). Running: Run's own end does.
        if (Interlocked.CompareExchange(ref _lifetime, 2, 0) == 0) Finish("disposed before it ran");
    }

    internal static bool IsBuiltIn(string method) => BuiltIn.Contains(method);

    // ------------------------------------------------------------------ requests

    private Task<JsonNode?> OnRequest(JsonRpcRequest request, CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        _log.Safe(LspLogLevel.Debug, $"{Name}: <- {request.Method} #{request.Id.ToJsonString()}");
        JsonObject? @params = request.Params as JsonObject;
        LspServerState state = State;

        if (request.Method == "initialize") return Task.FromResult(Initialize(request, @params));
        if (state == LspServerState.Created)
        {
            if (Host.Token is not null)
            {
                // Not authenticated: one answer, then the connection is closed (no flood of answers).
                _log.Safe(LspLogLevel.Debug, $"{Name}: {request.Method} before initialize; closing");
                request.CloseAfterResponse = true;
                lock (_stateGate) _state = LspServerState.ShutDown;
            }
            throw new JsonRpcException(JsonRpcErrorCodes.ServerNotInitialized, $"{request.Method} before initialize");
        }
        if (state != LspServerState.Running)
            throw new JsonRpcException(JsonRpcErrorCodes.InvalidRequest, $"{request.Method} after shutdown");

        if (request.Method == "shutdown")
        {
            lock (_stateGate)
            {
                _state = LspServerState.ShutDown;
                _shutdownReceived = true;
            }
            return Task.FromResult<JsonNode?>(null);
        }

        var context = new LspRequestContext(this, request.Method, @params);
        Task<JsonNode?> task;
        if (request.Method == "workspace/executeCommand")
        {
            string? command = LspJson.String(@params?["command"]);
            if (command is null || Features.Command(command) is not { } handler)
                throw new JsonRpcException(JsonRpcErrorCodes.InvalidParams, $"unknown command '{command}'");
            task = handler.Execute(context, command, @params?["arguments"] as JsonArray, cancellationToken);
        }
        else if (Features.Request(request.Method) is { } feature)
        {
            task = feature(context, cancellationToken);
        }
        else
        {
            throw new JsonRpcException(JsonRpcErrorCodes.MethodNotFound, "method not found: " + request.Method);
        }

        if (_log != LspLog.None)
            task.ContinueWith(t => _log.Safe(LspLogLevel.Debug, $"{Name}: {request.Method} answered in {clock.Elapsed.TotalMilliseconds:0.0} ms ({t.Status})"), TaskScheduler.Default);
        return task;
    }

    private JsonNode? Initialize(JsonRpcRequest request, JsonObject? @params)
    {
        lock (_stateGate)
        {
            if (_state != LspServerState.Created)
                throw new JsonRpcException(JsonRpcErrorCodes.InvalidRequest, "initialize was already received");
        }

        if (Host.Token is { } token)
        {
            string? given = LspJson.String(LspJson.Get(@params, "initializationOptions", "token"));
            if (given is null || !CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(given), System.Text.Encoding.UTF8.GetBytes(token)))
            {
                _log.Safe(LspLogLevel.Warning, $"{Name}: initialize {(given is null ? "without a token" : "with a wrong token")}; refused, closing");
                request.CloseAfterResponse = true;
                lock (_stateGate) _state = LspServerState.ShutDown;
                throw new JsonRpcException(JsonRpcErrorCodes.RequestFailed,
                    given is null ? "this server needs initializationOptions.token" : "initializationOptions.token is wrong");
            }
        }
        lock (_stateGate) _initialized = true;
        _connection.LiftMessageLimit();

        var offered = (LspJson.Get(@params, "capabilities", "general", "positionEncodings") as JsonArray)?
            .Select(LspJson.String).Where(s => s is not null).Select(s => s!).ToList();
        Encoding = PositionEncodings.Negotiate(offered);
        ClientCapabilities = (LspJson.Get(@params, "capabilities") as JsonObject)?.DeepClone() as JsonObject;
        InitializationOptions = (LspJson.Get(@params, "initializationOptions") as JsonObject)?.DeepClone() as JsonObject;

        var capabilities = new JsonObject
        {
            ["positionEncoding"] = Encoding.Name(),
            ["textDocumentSync"] = new JsonObject
            {
                ["openClose"] = true,
                ["change"] = 1, // full
                ["save"] = new JsonObject { ["includeText"] = false },
            },
        };
        Features.Advertise(capabilities);
        lock (_stateGate) _state = LspServerState.Running;
        _log.Safe(LspLogLevel.Info, $"{Name}: initialized ({Encoding.Name()}; client offered [{string.Join(", ", offered ?? [])}]); workspace {Host.WorkspaceRoot}, target {Host.CurrentTarget}");
        return new JsonObject
        {
            ["capabilities"] = capabilities,
            ["serverInfo"] = new JsonObject { ["name"] = _options.ServerName, ["version"] = Version },
        };
    }

    private static string Version
    {
        get
        {
            string? v = typeof(KfsLanguageServer).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (string.IsNullOrEmpty(v)) return "0.0.0";
            int plus = v.IndexOf('+', StringComparison.Ordinal);
            return plus >= 0 ? v[..plus] : v;
        }
    }

    // ------------------------------------------------------------------ notifications

    private void OnNotification(JsonRpcNotification notification)
    {
        _log.Safe(LspLogLevel.Debug, $"{Name}: <- {notification.Method}");
        if (notification.Method == "exit")
        {
            lock (_stateGate) ExitCode = _shutdownReceived ? 0 : 1;
            _connection.Close("exit");
            return;
        }
        if (State != LspServerState.Running) return; // before initialize or after shutdown: dropped
        JsonObject? @params = notification.Params as JsonObject;
        switch (notification.Method)
        {
            case "initialized":
                return;
            case "textDocument/didOpen":
                DidOpen(@params);
                return;
            case "textDocument/didChange":
                DidChange(@params);
                return;
            case "textDocument/didClose":
                DidClose(@params);
                return;
            case "textDocument/didSave":
                if (LspJson.DocumentUri(@params) is { } saved && Documents.Get(saved)?.Path is { } savedPath) Builds.Touch(savedPath);
                return;
            case "workspace/didChangeWatchedFiles":
                Builds.InvalidateAll();
                return;
        }
        if (Features.Notification(notification.Method) is { } handler) handler(new LspRequestContext(this, notification.Method, @params));
    }

    private void DidOpen(JsonObject? @params)
    {
        JsonNode? document = LspJson.Get(@params, "textDocument");
        if (LspJson.String(LspJson.Get(document, "uri")) is not { } uri || LspJson.String(LspJson.Get(document, "text")) is not { } text)
        {
            _log.Safe(LspLogLevel.Warning, $"{Name}: didOpen without textDocument.uri or text (ignored)");
            return;
        }
        LspDocument doc = Documents.Open(uri, LspJson.String(LspJson.Get(document, "languageId")) ?? "", LspJson.Int(LspJson.Get(document, "version")) ?? 0, text);
        if (doc.Path is { } path) Builds.Opened(path);
        else _log.Safe(LspLogLevel.Info, $"{Name}: {uri} is outside the workspace {Host.WorkspaceRoot}; it is not built");
    }

    private void DidChange(JsonObject? @params)
    {
        if (LspJson.DocumentUri(@params) is not { } uri || LspJson.Get(@params, "contentChanges") is not JsonArray changes) return;
        int version = LspJson.Int(LspJson.Get(@params, "textDocument", "version")) ?? 0;
        var edits = new List<(LspRange?, string)>();
        foreach (JsonNode? change in changes)
        {
            if (LspJson.String(LspJson.Get(change, "text")) is not { } text) continue;
            edits.Add((LspRange.From(LspJson.Get(change, "range")), text));
        }
        if (Documents.Apply(uri, version, edits, Encoding) is { Path: { } path }) Builds.Touch(path);
    }

    private void DidClose(JsonObject? @params)
    {
        if (LspJson.DocumentUri(@params) is not { } uri) return;
        if (Documents.Close(uri) is { Path: { } path }) Builds.Closed(path);
    }

    private void OnTargetChanged()
    {
        _log.Safe(LspLogLevel.Info, $"{Name}: the build target is now {Host.CurrentTarget}; re-diagnosing the open projects");
        Builds.InvalidateAll();
    }

    // ------------------------------------------------------------------ diagnostics

    private void OnBuilt(ProjectBuildResult build)
    {
        if (State == LspServerState.Closed) return;
        Dictionary<string, List<LspDiagnostic>> mapped = DiagnosticMapper.Map(build, Encoding, Documents.UriOf);
        lock (_publishGate)
        {
            if (_lastSequence.TryGetValue(build.Key, out long last) && last >= build.Sequence) return;
            _lastSequence[build.Key] = build.Sequence;
            var paths = new SortedSet<string>(mapped.Keys, StringComparer.Ordinal);
            if (_projectPaths.TryGetValue(build.Key, out var before)) paths.UnionWith(before);
            foreach (string path in paths)
            {
                if (!_contributions.TryGetValue(path, out var byProject)) _contributions[path] = byProject = new(StringComparer.Ordinal);
                if (mapped.TryGetValue(path, out var list) && list.Count > 0) byProject[build.Key] = list;
                else byProject.Remove(build.Key);
                Publish(path, build.Versions.TryGetValue(path, out int v) ? v : null);
            }
            _projectPaths[build.Key] = new HashSet<string>(mapped.Keys, StringComparer.Ordinal);
        }
    }

    private void OnRemoved(string key)
    {
        lock (_publishGate)
        {
            // A tombstone, not a removal: a build of this project that was already past its publish
            // decision when the document closed carries a sequence at most the scheduler's current one
            // and is dropped by OnBuilt; a build after a reopen gets a larger one and is published.
            long now = Builds.Sequence;
            if (!_lastSequence.TryGetValue(key, out long last) || last < now) _lastSequence[key] = now;
            if (!_projectPaths.Remove(key, out var paths)) return;
            foreach (string path in paths.Order(StringComparer.Ordinal))
            {
                if (!_contributions.TryGetValue(path, out var byProject) || !byProject.Remove(key)) continue;
                Publish(path, null);
            }
        }
    }

    /// <summary>
    /// Replaces the diagnostics that <paramref name="source"/> (not a build: a runtime trap, task C4)
    /// contributes, by workspace path; paths it contributed before and not now are cleared. They are
    /// published merged with the builds' diagnostics of the same path and survive later builds until
    /// replaced. An empty map clears them. Any thread.
    /// </summary>
    public void SetExternalDiagnostics(string source, IReadOnlyDictionary<string, IReadOnlyList<LspDiagnostic>> byPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(source);
        ArgumentNullException.ThrowIfNull(byPath);
        if (State == LspServerState.Closed) return;
        string key = "external:" + source;   // never a project key (folder:… / root:…)
        lock (_publishGate)
        {
            var paths = new SortedSet<string>(byPath.Keys, StringComparer.Ordinal);
            if (_projectPaths.TryGetValue(key, out var before)) paths.UnionWith(before);
            foreach (string path in paths)
            {
                if (!_contributions.TryGetValue(path, out var byProject)) _contributions[path] = byProject = new(StringComparer.Ordinal);
                if (byPath.TryGetValue(path, out var list) && list.Count > 0) byProject[key] = list.ToList();
                else byProject.Remove(key);
                Publish(path, null);
            }
            if (byPath.Count == 0) _projectPaths.Remove(key);
            else _projectPaths[key] = new HashSet<string>(byPath.Keys, StringComparer.Ordinal);
        }
    }

    /// <summary>Publishes the union of every project's diagnostics for <paramref name="path"/> (under <see cref="_publishGate"/>).</summary>
    private void Publish(string path, int? version)
    {
        var all = new List<LspDiagnostic>();
        var seen = new HashSet<LspDiagnostic>();
        if (_contributions.TryGetValue(path, out var byProject))
        {
            foreach (List<LspDiagnostic> list in byProject.Values)
            {
                foreach (LspDiagnostic d in list)
                {
                    if (seen.Add(d with { RelatedInformation = null, Tags = null })) all.Add(d);
                }
            }
            if (byProject.Count == 0) _contributions.Remove(path);
        }
        LspDocument? open = Documents.ByPath(path);
        var @params = new JsonObject { ["uri"] = open?.Uri ?? Documents.UriOf(path) };
        if (version is { } v && open is not null && open.Version == v) @params["version"] = v;
        @params["diagnostics"] = new JsonArray(all.Select(d => (JsonNode)d.ToJson()).ToArray());
        _connection.Notify("textDocument/publishDiagnostics", @params);
    }
}
