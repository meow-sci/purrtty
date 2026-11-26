using System.Text;
using System.Text.Json.Nodes;
using purrTTY.Kfs.Lsp.Documents;
using purrTTY.Kfs.Lsp.Live;
using purrTTY.Kfs.Lsp.Protocol;

namespace purrTTY.Kfs.Lsp.Features;

/// <summary>What a client subscribed to with <c>purrtty/subscribe</c>.</summary>
[Flags]
public enum LiveTopics
{
    /// <summary>Nothing.</summary>
    None = 0,
    /// <summary><c>purrtty/kfsLog</c>: new program-log lines.</summary>
    Log = 1,
    /// <summary><c>purrtty/kfsOutput</c>: text printed by <c>kfs</c> / <c>purr</c> console commands.</summary>
    Output = 2,
    /// <summary><c>purrtty/kfsState</c>: the program's state, on change, at most 5 Hz; and a trap as a diagnostic.</summary>
    State = 4,
}

/// <summary>Settings of a <see cref="LiveFeed"/>; every one is injectable for tests.</summary>
public sealed class LiveFeedOptions
{
    /// <summary>How often the feed polls while a client subscribes to <c>log</c> or <c>state</c> (default 100 ms).</summary>
    public TimeSpan Period { get; init; } = TimeSpan.FromMilliseconds(100);

    /// <summary>The shortest time between two state reads (default 200 ms: at most 5 Hz, plan §6.7).</summary>
    public TimeSpan StateInterval { get; init; } = TimeSpan.FromMilliseconds(200);

    /// <summary>The most log lines one poll copies (default 256; the game caps a program at 10 lines and 20 events per second).</summary>
    public int MaxLogLines { get; init; } = 256;

    /// <summary>
    /// How many of the log's last lines a client gets when it subscribes to <c>log</c> (default 100;
    /// 0: none), sent once as <c>purrtty/kfsLog { lines, tail: true }</c> before any newer line.
    /// </summary>
    public int LogTailLines { get; init; } = 100;

    /// <summary>The most output text buffered between two flushes (default 256 KiB; the rest is cut with a marker).</summary>
    public int MaxOutputChars { get; init; } = 256 * 1024;

    /// <summary>False: no timer; the owner calls <see cref="LiveFeed.Poll"/> (tests).</summary>
    public bool UseTimer { get; init; } = true;

    /// <summary>Milliseconds of a monotonic clock (default <see cref="Environment.TickCount64"/>).</summary>
    public Func<long> Clock { get; init; } = static () => Environment.TickCount64;

    /// <summary>Where the feed logs.</summary>
    public LspLogSink Log { get; init; } = LspLog.ModLog;
}

/// <summary>
/// The live data of plan §6.7 for every connection of one server: the subscriptions
/// (<c>purrtty/subscribe</c>), the program-log and state feeds, the output feed and the trap
/// diagnostic.
/// <list type="bullet">
/// <item><b>Polling only while subscribed.</b> A timer runs only while at least one client subscribes
/// to <c>log</c> or <c>state</c>; without one nothing of the source is read. <c>log</c> reads the log's
/// version each poll and copies the tail only when it changed; <c>state</c> reads the state (which
/// asks the computer for a snapshot) at most every <see cref="LiveFeedOptions.StateInterval"/> and
/// only while a client subscribes to <c>state</c>.</item>
/// <item><b>Log lines</b> arrive once and in order: the feed keeps the version it last delivered and
/// sends the lines added since, oldest first. A client that starts subscribing to <c>log</c> first
/// gets the log's last <see cref="LiveFeedOptions.LogTailLines"/> lines (one <c>purrtty/kfsLog</c>
/// with <c>tail: true</c>), then every line added after them; a change of source (another computer)
/// restarts from that source's current version.</item>
/// <item><b>State</b> is sent to a client when it differs from the last state sent to it.</item>
/// <item><b>A trap with a source location</b> is also published to each <c>state</c> client as an
/// error diagnostic (code <c>KFS-TRAP</c>) on that line, merged with the build diagnostics
/// (<see cref="KfsLanguageServer.SetExternalDiagnostics"/>), until the snapshot no longer carries it
/// (the program was reset, restarted, reloaded or stopped) or the client unsubscribes.</item>
/// <item><b>Output</b> (<see cref="WriteOutput"/>, the in-game output tee) is buffered only while a
/// client subscribes to <c>output</c> and sent by <see cref="FlushOutput"/> (the in-game host calls it
/// after each command and once per frame).</item>
/// </list>
/// A closed connection is removed (<see cref="Attach"/>). Thread-safe.
/// </summary>
public sealed class LiveFeed : IDisposable
{
    /// <summary>The source key of the trap diagnostic.</summary>
    public const string TrapSource = "trap";

    /// <summary>The diagnostic code of a trap.</summary>
    public const string TrapCode = "KFS-TRAP";

    private readonly Func<ILiveSource?> _source;
    private readonly LiveFeedOptions _options;
    private readonly object _gate = new();
    private readonly object _pollGate = new();
    private readonly object _outputGate = new();
    private readonly Dictionary<KfsLanguageServer, Subscriber> _subscribers = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<KfsLanguageServer> _attached = new(ReferenceEqualityComparer.Instance);
    private readonly List<string> _lines = [];
    private readonly StringBuilder _output = new();
    private readonly Timer? _timer;
    private ILiveSource? _lastSource;
    private long _logVersion;
    private bool _logBaseline = true;
    private long _lastStateRead = long.MinValue;
    private LiveState? _lastState;
    private bool _timerArmed;
    private bool _outputTruncated;
    private int _outputSubscribers;
    private long _polls, _logReads, _stateReads;
    private bool _disposed;

    /// <summary>A feed over <paramref name="source"/> (called on the poll thread; null: no program to read).</summary>
    public LiveFeed(Func<ILiveSource?> source, LiveFeedOptions? options = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _options = options ?? new LiveFeedOptions();
        if (_options.UseTimer) _timer = new Timer(_ => OnTimer());   // OnTimer never throws
    }

    /// <summary>True while the poll timer runs (some client subscribes to <c>log</c> or <c>state</c>).</summary>
    public bool IsPolling
    {
        get { lock (_gate) return _timerArmed; }
    }

    /// <summary>Polls that read something (tests and <c>purr lsp</c>).</summary>
    public long Polls => Interlocked.Read(ref _polls);

    /// <summary>How many times the log tail was copied.</summary>
    public long LogReads => Interlocked.Read(ref _logReads);

    /// <summary>How many times the state was read.</summary>
    public long StateReads => Interlocked.Read(ref _stateReads);

    /// <summary>The connections with a non-empty subscription.</summary>
    public int Subscribers
    {
        get { lock (_gate) return _subscribers.Count; }
    }

    /// <summary>The topics <paramref name="server"/> subscribes to.</summary>
    public LiveTopics TopicsOf(KfsLanguageServer server)
    {
        lock (_gate) return _subscribers.TryGetValue(server, out var s) ? s.Topics : LiveTopics.None;
    }

    /// <summary>Removes <paramref name="server"/>'s subscription when its connection closes. Call once per connection (<see cref="HostFeatures.AddTo"/> does).</summary>
    public void Attach(KfsLanguageServer server)
    {
        ArgumentNullException.ThrowIfNull(server);
        lock (_gate)
        {
            if (_disposed || !_attached.Add(server)) return;
        }
        server.Closed += OnClosed;
    }

    /// <summary>Replaces <paramref name="server"/>'s subscription with <paramref name="topics"/> (<see cref="LiveTopics.None"/> unsubscribes).</summary>
    public void Subscribe(KfsLanguageServer server, LiveTopics topics)
    {
        ArgumentNullException.ThrowIfNull(server);
        Subscriber? cleared = null;
        lock (_gate)
        {
            if (_disposed) return;
            _subscribers.TryGetValue(server, out var current);
            if (topics == LiveTopics.None)
            {
                if (current is not null)
                {
                    _subscribers.Remove(server);
                    cleared = current;
                }
            }
            else
            {
                if (current is null) _subscribers[server] = current = new Subscriber(server);
                Subscriber self = current;
                if ((topics & LiveTopics.Log) != 0 && (self.Topics & LiveTopics.Log) == 0) self.WantsTail = _options.LogTailLines > 0;
                if ((topics & LiveTopics.Log) == 0) self.WantsTail = false;
                // The first log subscriber starts from the log's version at its next poll (its tail is sent separately, WantsTail);
                // a later one joins the running feed.
                if ((topics & LiveTopics.Log) != 0 && (self.Topics & LiveTopics.Log) == 0
                    && !_subscribers.Values.Any(s => !ReferenceEquals(s, self) && (s.Topics & LiveTopics.Log) != 0))
                    _logBaseline = true;
                if ((topics & LiveTopics.State) == 0 && (current.Topics & LiveTopics.State) != 0) cleared = current;
                if ((topics & LiveTopics.State) != 0 && (current.Topics & LiveTopics.State) == 0) current.LastState = null;
                current.Topics = topics;
            }
            _outputSubscribers = _subscribers.Values.Count(s => (s.Topics & LiveTopics.Output) != 0);
            UpdateTimer();
        }
        if (cleared?.TrapShown is not null) ClearTrap(cleared);
    }

    /// <summary>
    /// One round of the log and state feeds (the timer calls it; tests call it directly). Reads
    /// nothing when no client subscribes to <c>log</c> or <c>state</c>. Never throws.
    /// </summary>
    public void Poll()
    {
        if (!Monitor.TryEnter(_pollGate)) return;   // a slow poll is still running: skip this one
        try
        {
            PollCore();
        }
        catch (Exception e)
        {
            _options.Log.Safe(LspLogLevel.Warning, $"live feed: a poll failed: {e.GetType().Name}: {e.Message}");
        }
        finally
        {
            Monitor.Exit(_pollGate);
        }
    }

    /// <summary>Text printed by a <c>kfs</c> / <c>purr</c> console command (the in-game output tee). Buffered only while a client subscribes to <c>output</c>.</summary>
    public void WriteOutput(string? text)
    {
        if (string.IsNullOrEmpty(text) || Volatile.Read(ref _outputSubscribers) == 0) return;
        lock (_outputGate)
        {
            int room = _options.MaxOutputChars - _output.Length;
            if (room <= 0)
            {
                _outputTruncated = true;
                return;
            }
            _output.Append(text.Length <= room ? text : text[..room]);
            if (text.Length > room) _outputTruncated = true;
        }
    }

    /// <summary>True when output is buffered.</summary>
    public bool HasOutput
    {
        get { lock (_outputGate) return _output.Length > 0 || _outputTruncated; }
    }

    /// <summary>Sends the buffered output to every <c>output</c> client as one <c>purrtty/kfsOutput</c>. Cheap when nothing is buffered.</summary>
    public void FlushOutput()
    {
        string text;
        lock (_outputGate)
        {
            if (_output.Length == 0 && !_outputTruncated) return;
            text = _output.ToString();
            if (_outputTruncated) text += $"\n(output cut at {_options.MaxOutputChars} characters)\n";
            _output.Clear();
            _outputTruncated = false;
        }
        foreach (KfsLanguageServer server in SubscribersOf(LiveTopics.Output))
        {
            server.Notify("purrtty/kfsOutput", new JsonObject { ["text"] = text });
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        List<KfsLanguageServer> attached;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _subscribers.Clear();
            _outputSubscribers = 0;
            _timerArmed = false;
            attached = _attached.ToList();
            _attached.Clear();
        }
        foreach (KfsLanguageServer server in attached) server.Closed -= OnClosed;
        if (_timer is not null)
        {
            using var done = new ManualResetEvent(false);
            // On the game's main thread at unload: a poll only takes locks and queues messages; wait a moment, not more.
            if (_timer.Dispose(done)) done.WaitOne(TimeSpan.FromSeconds(1));
        }
        // Waits (briefly) for a poll started before the timer was disposed; it finds the feed disposed.
        if (Monitor.TryEnter(_pollGate, TimeSpan.FromSeconds(1))) Monitor.Exit(_pollGate);
    }

    // ------------------------------------------------------------------ polling

    private sealed class Subscriber(KfsLanguageServer server)
    {
        public KfsLanguageServer Server { get; } = server;
        public LiveTopics Topics { get; set; }
        public LiveState? LastState { get; set; }
        public (string Path, int Line, string Message)? TrapShown { get; set; }
        /// <summary>Owed the log's tail (it just subscribed to <c>log</c>); set and cleared under the feed's gate.</summary>
        public bool WantsTail { get; set; }
    }

    private void OnTimer()
    {
        try
        {
            lock (_gate)
            {
                if (_disposed || !_timerArmed) return;
            }
            Poll();
        }
        catch (Exception e)
        {
            // A timer callback must never throw: that ends the process.
            _options.Log.Safe(LspLogLevel.Error, $"live feed: the poll timer failed: {e.GetType().Name}: {e.Message}");
        }
    }

    private void UpdateTimer()
    {
        bool want = !_disposed && _subscribers.Values.Any(s => (s.Topics & (LiveTopics.Log | LiveTopics.State)) != 0);
        if (want == _timerArmed) return;
        _timerArmed = want;
        if (_timer is null) return;
        try { _timer.Change(want ? _options.Period : Timeout.InfiniteTimeSpan, want ? _options.Period : Timeout.InfiniteTimeSpan); }
        catch (ObjectDisposedException) { /* disposing */ }
    }

    private void PollCore()
    {
        List<Subscriber> subscribers;
        bool wantLog, wantState, baseline;
        lock (_gate)
        {
            if (_disposed) return;
            subscribers = _subscribers.Values.Where(s => (s.Topics & (LiveTopics.Log | LiveTopics.State)) != 0).ToList();
            wantLog = subscribers.Any(s => (s.Topics & LiveTopics.Log) != 0);
            wantState = subscribers.Any(s => (s.Topics & LiveTopics.State) != 0);
            baseline = _logBaseline;
            _logBaseline = false;
        }
        if (subscribers.Count == 0) return;
        Interlocked.Increment(ref _polls);

        ILiveSource? source = _source();
        if (!ReferenceEquals(source, _lastSource))
        {
            _lastSource = source;
            baseline = true;
            _lastState = null;
            _lastStateRead = long.MinValue;
        }
        if (source is null)
        {
            foreach (Subscriber s in subscribers)
            {
                if (s.TrapShown is not null) ClearTrap(s);
            }
            return;
        }

        if (wantLog) PollLog(source, subscribers, baseline);
        else if (baseline) _logBaseline = true;   // the next log subscriber starts from the version of its time

        if (wantState) PollState(source, subscribers);
    }

    private void PollLog(ILiveSource source, List<Subscriber> subscribers, bool baseline)
    {
        List<Subscriber> logged = subscribers.Where(s => (s.Topics & LiveTopics.Log) != 0).ToList();
        List<Subscriber> tails;
        lock (_gate) tails = logged.Where(s => s.WantsTail).ToList();
        long version = source.LogVersion;
        if (tails.Count == 0)
        {
            if (baseline)
            {
                _logVersion = version;
                return;
            }
            if (version == _logVersion) return;
        }
        int want = Math.Max(_options.MaxLogLines, tails.Count > 0 ? _options.LogTailLines : 0);
        Interlocked.Increment(ref _logReads);
        long copied = source.CopyLog(want, _lines);
        long before = _logVersion;
        _logVersion = copied;

        // The lines added since the last delivery, for the clients already following the log.
        long fresh = baseline ? 0 : copied - before;
        if (fresh > 0)
        {
            int take = (int)Math.Min(fresh, _lines.Count);
            var lines = new JsonArray();
            // Fewer lines than versions: the log was cleared or trimmed (nothing lost to us), unless the
            // copy was full — then more arrived than one poll copies.
            if (fresh > _lines.Count && _lines.Count >= want) lines.Add($"({fresh - _lines.Count} log lines were not delivered: more than {want} arrived between two polls)");
            for (int i = _lines.Count - take; i < _lines.Count; i++) lines.Add(_lines[i]);
            if (lines.Count > 0)
            {
                foreach (Subscriber s in logged)
                {
                    if (!tails.Contains(s)) s.Server.Notify("purrtty/kfsLog", new JsonObject { ["lines"] = lines.DeepClone() });
                }
            }
        }

        // A new log client: the log's tail up to the same version, so the next poll continues it.
        if (tails.Count == 0) return;
        lock (_gate)
        {
            foreach (Subscriber t in tails) t.WantsTail = false;
        }
        int count = Math.Min(_options.LogTailLines, _lines.Count);
        if (count == 0) return;
        var tail = new JsonArray();
        for (int i = _lines.Count - count; i < _lines.Count; i++) tail.Add(_lines[i]);
        foreach (Subscriber t in tails) t.Server.Notify("purrtty/kfsLog", new JsonObject { ["lines"] = tail.DeepClone(), ["tail"] = true });
    }

    private void PollState(ILiveSource source, List<Subscriber> subscribers)
    {
        long now = _options.Clock();
        LiveState? state = _lastState;
        if (state is null || _lastStateRead == long.MinValue || now - _lastStateRead >= (long)_options.StateInterval.TotalMilliseconds)
        {
            _lastStateRead = now;
            Interlocked.Increment(ref _stateReads);
            state = source.ReadState();
            _lastState = state;
        }
        if (state is null) return;
        foreach (Subscriber s in subscribers)
        {
            if ((s.Topics & LiveTopics.State) == 0) continue;
            if (!Equals(s.LastState, state))
            {
                s.LastState = state;
                s.Server.Notify("purrtty/kfsState", StateJson(s.Server, state));
            }
            ShowTrap(s, state.Trap);
        }
    }

    /// <summary>The <c>purrtty/kfsState</c> payload of <paramref name="state"/> for <paramref name="server"/> (its document URIs).</summary>
    public static JsonObject StateJson(KfsLanguageServer server, LiveState state)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(state);
        JsonObject? trap = null;
        if (state.Trap is { } t)
        {
            trap = new JsonObject
            {
                ["message"] = t.Message,
                ["uri"] = t.Path is null ? null : server.Documents.UriOf(t.Path),
                ["line"] = t.Line > 0 ? t.Line - 1 : null,
            };
        }
        return new JsonObject
        {
            ["program"] = state.Program,
            ["state"] = state.State,
            ["tick"] = state.Tick,
            ["overruns"] = state.Overruns,
            ["trap"] = trap,
        };
    }

    private static void ShowTrap(Subscriber s, LiveTrap? trap)
    {
        (string, int, string)? wanted = trap is { Path: { } path, Line: > 0 } ? (path, trap.Line, trap.Message) : null;
        if (wanted == s.TrapShown) return;
        s.TrapShown = wanted;
        if (wanted is not { } w)
        {
            s.Server.SetExternalDiagnostics(TrapSource, new Dictionary<string, IReadOnlyList<LspDiagnostic>>());
            return;
        }
        LspRange range = LineRange(s.Server, w.Item1, w.Item2 - 1);
        var diagnostic = new LspDiagnostic(range, LspSeverity.Error, TrapCode,
            $"{w.Item3}: the program halted here\nhint: kfs reset (or kfs.reset) restarts it; the diagnostic stays until then");
        s.Server.SetExternalDiagnostics(TrapSource, new Dictionary<string, IReadOnlyList<LspDiagnostic>> { [w.Item1] = [diagnostic] });
    }

    private void ClearTrap(Subscriber s)
    {
        s.TrapShown = null;
        try { s.Server.SetExternalDiagnostics(TrapSource, new Dictionary<string, IReadOnlyList<LspDiagnostic>>()); }
        catch (Exception e) { _options.Log.Safe(LspLogLevel.Warning, $"live feed: clearing a trap diagnostic failed: {e.Message}"); }
    }

    /// <summary>The trimmed text of 0-based <paramref name="line"/> of a workspace file (the open buffer, else the disk), in the client's encoding.</summary>
    private static LspRange LineRange(KfsLanguageServer server, string path, int line)
    {
        LineIndex? lines = server.Documents.ByPath(path)?.Lines;
        if (lines is null)
        {
            try
            {
                string full = Path.Combine(server.Host.WorkspaceRoot, path.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(full)) lines = new LineIndex(purrTTY.Kfs.Runtime.BoundedFile.ReadText(full, purrTTY.Kfs.Runtime.ProjectResolver.MaxSourceBytes));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
            {
                lines = null;
            }
        }
        if (lines is null || line >= lines.Count) return LspRange.OnLine(line, 0, 0);
        string text = lines.Line(line);
        int start = text.Length - text.TrimStart().Length;
        int end = text.TrimEnd().Length;
        if (end <= start) start = end = 0;
        return PositionMapper.Range(lines, line, start, end, server.Encoding);
    }

    private List<KfsLanguageServer> SubscribersOf(LiveTopics topic)
    {
        lock (_gate) return _subscribers.Values.Where(s => (s.Topics & topic) != 0).Select(s => s.Server).ToList();
    }

    private void OnClosed(KfsLanguageServer server)
    {
        lock (_gate)
        {
            _attached.Remove(server);
            _subscribers.Remove(server);
            _outputSubscribers = _subscribers.Values.Count(s => (s.Topics & LiveTopics.Output) != 0);
            UpdateTimer();
        }
        server.Closed -= OnClosed;
    }
}
