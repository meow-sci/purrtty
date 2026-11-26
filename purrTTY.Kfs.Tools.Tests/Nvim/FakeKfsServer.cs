using System.Net;
using System.Text.Json.Nodes;
using KSA.Kfs.Vm.Tables;
using purrTTY.Kfs.Lsp;
using purrTTY.Kfs.Lsp.Features;
using purrTTY.Kfs.Runtime;

namespace purrTTY.Kfs.Tools.Tests.Nvim;

/// <summary>One <c>workspace/executeCommand</c> the fake server received.</summary>
/// <param name="Command">The command name (<c>kfs.build</c>).</param>
/// <param name="Arguments">Its arguments as JSON (<c>["file:///…"]</c>).</param>
/// <param name="Disk">What <see cref="FakeKfsServer.DiskProbe"/> read when the command arrived (the files the test cares about).</param>
internal sealed record ReceivedCommand(string Command, string Arguments, IReadOnlyDictionary<string, string?> Disk);

/// <summary>
/// A real kFS language server (<see cref="KfsLspListener"/> over a bench <see cref="KfsLanguageHost"/>
/// on loopback port 0, with a token) plus fakes of the in-game server's side of the wire contract,
/// registered through the library's public API only: every contract command
/// (<see cref="IExecuteCommandHandler"/>), the <c>purrtty/subscribe</c> notification, the
/// <c>capabilities.experimental.purrtty</c> block, and <see cref="Broadcast"/> for the
/// <c>purrtty/kfs*</c> notifications. With <c>inGame: false</c> it is a plain server (diagnostics only).
/// </summary>
internal sealed class FakeKfsServer : IDisposable
{
    public static readonly string[] ContractCommands =
        ["kfs.build", "kfs.load", "kfs.start", "kfs.stop", "kfs.reset", "kfs.target", "kfs.manual", "kfs.parts", "kfs.verb"];

    private readonly object _gate = new();
    private readonly List<ReceivedCommand> _commands = [];
    private readonly List<string> _subscriptions = [];
    private readonly List<string> _log = [];
    private readonly KfsLspListener _listener;
    private int _initializes;

    private FakeKfsServer(KfsLspListener listener) => _listener = listener;

    /// <summary>
    /// Starts a server for <paramref name="workspaceRoot"/>. <paramref name="name"/> is its
    /// <c>serverInfo.name</c> (to tell two servers apart). <paramref name="result"/> answers each
    /// command (default: <c>ok</c> with "&lt;command&gt; done").
    /// </summary>
    public static FakeKfsServer Start(GameTables tables, string workspaceRoot, string token, string name = "fake", bool inGame = true,
        Func<string, JsonArray?, JsonObject>? result = null)
    {
        FakeKfsServer? self = null;
        var host = KfsLanguageHost.ForBench(tables, workspaceRoot, token);
        LspLogSink log = (level, message) =>
        {
            FakeKfsServer? s = self;
            if (s is null) return;
            lock (s._gate)
            {
                s._log.Add($"{level}: {message}");
                if (message.Contains(": initialize ", StringComparison.Ordinal) || message.Contains(": initialized (", StringComparison.Ordinal)) s._initializes++;
            }
        };
        var listener = new KfsLspListener(host, IPAddress.Loopback, 0,
            n => new KfsLanguageServerOptions { Log = log, Name = n, ServerName = name },
            configure: server =>
            {
                StandardFeatures.AddTo(server);
                if (inGame) self!.AddFakes(server, result);
            },
            log: log);
        self = new FakeKfsServer(listener) { Host = host };
        listener.Start();
        return self;
    }

    public KfsLanguageHost Host { get; private init; } = null!;

    /// <summary><c>127.0.0.1:&lt;port&gt;</c>.</summary>
    public string Address => $"127.0.0.1:{_listener.Port}";

    public int Port => _listener.Port;

    /// <summary>The endpoint as the launcher would get it.</summary>
    public KfsLspEndpoint Endpoint => new(Address, Host.Token!);

    /// <summary>Reads the files whose content a command records (<see cref="ReceivedCommand.Disk"/>).</summary>
    public Func<IReadOnlyDictionary<string, string?>> DiskProbe { get; set; } = () => new Dictionary<string, string?>();

    public IReadOnlyList<ReceivedCommand> Commands
    {
        get { lock (_gate) return _commands.ToList(); }
    }

    /// <summary>Each <c>purrtty/subscribe</c>'s topics, comma-joined, in arrival order.</summary>
    public IReadOnlyList<string> Subscriptions
    {
        get { lock (_gate) return _subscriptions.ToList(); }
    }

    /// <summary><c>initialize</c> requests seen (accepted or refused).</summary>
    public int Initializes
    {
        get { lock (_gate) return _initializes; }
    }

    public string Log
    {
        get { lock (_gate) return string.Join("\n", _log); }
    }

    /// <summary>Clients that finished <c>initialize</c>.</summary>
    public int RunningClients => _listener.Clients.Count(c => c.State == LspServerState.Running);

    /// <summary>Sends a notification to every initialized client; returns how many got it.</summary>
    public int Broadcast(string method, JsonObject @params)
    {
        int n = 0;
        foreach (KfsLanguageServer s in _listener.Clients)
        {
            if (s.State != LspServerState.Running) continue;
            s.Notify(method, @params.DeepClone());
            n++;
        }
        return n;
    }

    /// <summary>The server info file content for this server (plan §7.6).</summary>
    public string InfoJson(int? pid = null) => new JsonObject
    {
        ["address"] = Address,
        ["token"] = Host.Token,
        ["pid"] = pid ?? Environment.ProcessId,
        ["ksa_build"] = "test",
        ["workspace_root"] = Host.WorkspaceRoot,
        ["future_key"] = "ignored",
    }.ToJsonString();

    /// <summary>Waits (deadline-bounded polling: there is no event for these) until <paramref name="condition"/> holds.</summary>
    public static bool WaitUntil(Func<bool> condition, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(30));
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) return false;
            Thread.Sleep(10); // polling interval inside a deadline-bounded wait, not a guess at a duration
        }
        return true;
    }

    public void Dispose() => _listener.Stop();

    private void AddFakes(KfsLanguageServer server, Func<string, JsonArray?, JsonObject>? result)
    {
        server.Features.Add(new FakeCommands(this, result));
        server.Features.AddNotification("purrtty/subscribe", context =>
        {
            string topics = string.Join(",", (context.Params?["topics"] as JsonArray ?? []).Select(t => t?.GetValue<string>()));
            lock (_gate) _subscriptions.Add(topics);
        });
        // A request nobody sends: the public way to contribute capabilities.experimental.
        server.Features.AddRequest("purrtty/test", (_, _) => Task.FromResult<JsonNode?>(null), capabilities =>
            capabilities["experimental"] = new JsonObject
            {
                ["purrtty"] = new JsonObject { ["subscribe"] = true, ["vehicleCommands"] = true, ["verb"] = true },
            });
    }

    private sealed class FakeCommands(FakeKfsServer owner, Func<string, JsonArray?, JsonObject>? result) : IExecuteCommandHandler
    {
        IReadOnlyList<string> IExecuteCommandHandler.Commands => ContractCommands;

        public Task<JsonNode?> Execute(LspRequestContext context, string command, JsonArray? arguments, CancellationToken cancellationToken)
        {
            var received = new ReceivedCommand(command, arguments?.ToJsonString() ?? "null", owner.DiskProbe());
            lock (owner._gate) owner._commands.Add(received);
            JsonObject answer = result?.Invoke(command, arguments)
                ?? new JsonObject { ["ok"] = true, ["output"] = command + " done", ["error"] = null };
            return Task.FromResult<JsonNode?>(answer);
        }
    }
}
