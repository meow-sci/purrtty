using System.Diagnostics;
using System.Globalization;
using KSA.Kfs.Compiler;
using purrTTY.Kfs.Lsp.Documents;
using purrTTY.Kfs.Runtime;

namespace purrTTY.Kfs.Lsp.Build;

/// <summary>Settings of a <see cref="BuildScheduler"/>; every one is injectable for tests.</summary>
public sealed class BuildSchedulerOptions
{
    /// <summary>The default debounce (plan §6.3).</summary>
    public static readonly TimeSpan DefaultDebounce = TimeSpan.FromMilliseconds(120);

    /// <summary>The default watchdog limit (plan §6.3).</summary>
    public static readonly TimeSpan DefaultCompileTimeLimit = TimeSpan.FromSeconds(10);

    /// <summary>
    /// How long after the last change of a project it is built (default 120 ms; each change restarts
    /// it). <see cref="Timeout.InfiniteTimeSpan"/>: only <see cref="BuildScheduler.Flush"/> starts builds (tests).
    /// </summary>
    public TimeSpan Debounce { get; init; } = DefaultDebounce;

    /// <summary>A compile running longer than this is abandoned (default 10 s).</summary>
    public TimeSpan CompileTimeLimit { get; init; } = DefaultCompileTimeLimit;

    /// <summary>
    /// How many abandoned compile threads may still be running <b>in the process</b> (every
    /// connection's together, <see cref="LspThreads.Retired"/>) before a further stuck compile is not
    /// replaced and compiles are refused (default 2).
    /// </summary>
    public int MaxRetiredThreads { get; init; } = 2;

    /// <summary>The compile function; null: <see cref="BuildScheduler.DefaultCompile"/>.</summary>
    public ProjectCompileFunction? Compile { get; init; }

    /// <summary>Tests: called with the timer's name (<c>debounce</c>, <c>watchdog</c>) at the start of each timer callback; may throw to prove the callback's guard.</summary>
    internal Action<string>? TimerFault { get; init; }
}

/// <summary>
/// Owns all compiling for one language-server connection (plan §6.3).
/// <list type="number">
/// <item>A change marks the document's project dirty — its own (the folder, or a root <c>.c</c> /
/// <c>.kasm</c>), and every open project whose last build read that path — and restarts that
/// project's debounce.</item>
/// <item>When it fires the project is queued (once: a newer request replaces the queued one, since
/// the input is captured only when the compile starts); the file set comes from
/// <see cref="ProjectResolver"/> over the disk with <b>every open buffer</b> overlaid.</item>
/// <item>Compiles run one at a time on the scheduler's own long-lived, below-normal-priority thread
/// with a 16 MiB stack (<c>Compiler.COMPILE_THREAD_STACK_BYTES</c>, the front end's nesting limits
/// assume it), kC through <c>Compiler.CompileOnThisThread</c>, pure kASM through the
/// <c>Assembler</c> with <c>ProjectFiles</c> (<see cref="ProjectCompiler"/>).</item>
/// <item>A result is published (<see cref="Built"/>) only if no change touched the project while it
/// compiled and every open buffer it used is still at the version it read; otherwise it is dropped
/// and the newer request builds.</item>
/// <item>Every compile is wrapped in <c>try</c>/<c>catch</c>: an exception becomes one <c>KFS-LSP</c>
/// error and the scheduler carries on.</item>
/// <item><b>Watchdog.</b> A compile that runs past <see cref="BuildSchedulerOptions.CompileTimeLimit"/>
/// is abandoned: a <c>KFS-LSP</c> error is published, its thread is retired (it ends when the compile
/// returns, if ever) and a fresh thread takes over. With
/// <see cref="BuildSchedulerOptions.MaxRetiredThreads"/> retired threads still running in the process
/// (all connections together), a further stuck compile is not replaced: compiles are refused, with a
/// <c>KFS-LSP</c> error saying so, until a retired thread ends.</item>
/// <item><b>Lazy thread.</b> The compile thread starts with the first build (a <c>didOpen</c>, after
/// <c>initialize</c>), never for a connection that builds nothing. A thread that cannot be started
/// (the process's thread limit) is reported as a <c>KFS-LSP</c> error and retried at the next build.</item>
/// <item>Timer callbacks never throw (an exception is logged); a running compile is counted in
/// <c>SystemHeaderReaders</c>, so the extension never changes the game's header table under it.</item>
/// </list>
/// </summary>
public sealed class BuildScheduler : IDisposable
{
    /// <summary>The code of the scheduler's own diagnostics.</summary>
    public const string ErrorCode = "KFS-LSP";

    private static int _threadNumbers;

    private readonly IKfsLanguageHost _host;
    private readonly DocumentStore _documents;
    private readonly BuildSchedulerOptions _options;
    private readonly ProjectCompileFunction _compile;
    private readonly LspLogSink _log;
    private readonly string _name;
    private readonly ProjectResolver _resolver;
    private readonly object _gate = new();
    private readonly Dictionary<string, ProjectState> _projects = new(StringComparer.Ordinal);
    private readonly List<string> _queue = [];
    private readonly List<Worker> _retired = [];
    private readonly Timer _debounce;
    private readonly Timer _watchdog;
    private Worker? _worker;
    private long _sequence;
    private int _threadsStarted;
    private bool _refusing;
    private bool _disposed;

    /// <summary>A scheduler building the open documents of <paramref name="documents"/> for <paramref name="host"/>.</summary>
    public BuildScheduler(IKfsLanguageHost host, DocumentStore documents, BuildSchedulerOptions? options = null, LspLogSink? log = null, string name = "client")
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _documents = documents ?? throw new ArgumentNullException(nameof(documents));
        _options = options ?? new BuildSchedulerOptions();
        if (_options.MaxRetiredThreads < 0) throw new ArgumentOutOfRangeException(nameof(options), "MaxRetiredThreads must not be negative");
        _compile = _options.Compile ?? DefaultCompile;
        _log = log ?? LspLog.None;
        _name = name;
        _resolver = new ProjectResolver(new DiskWorkspaceFiles(host.WorkspaceRoot));
        _debounce = new Timer(_ => Guard("debounce", OnDebounce));
        _watchdog = new Timer(_ => Guard("watchdog", OnWatchdog));
    }

    /// <summary>The compile <see cref="BuildSchedulerOptions.Compile"/> defaults to: <see cref="ProjectCompiler"/> with <c>Compiler.CompileOnThisThread</c>.</summary>
    public static ProjectCompileOutcome DefaultCompile(ProjectCompileRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ProjectCompiler.Build(new ProjectResolution(request.Project, null), request.File, request.Target.Layout,
            request.Tables, request.Target.Part, Compiler.CompileOnThisThread);
    }

    /// <summary>
    /// Raised after each published build, on the compile thread (or the watchdog's timer thread for an
    /// abandoned one). Handlers must not block for long; a later result of a project has a larger
    /// <see cref="ProjectBuildResult.Sequence"/> (drop an earlier one that arrives late).
    /// </summary>
    public event Action<ProjectBuildResult>? Built;

    /// <summary>Raised with a project's key when no open document belongs to it any more (on the thread that closed the document).</summary>
    public event Action<string>? Removed;

    /// <summary>The options in use.</summary>
    public BuildSchedulerOptions Options => _options;

    /// <summary>Compile threads this scheduler has started (the first one and each fresh one after an abandoned compile).</summary>
    public int ThreadsStarted
    {
        get { lock (_gate) return _threadsStarted; }
    }

    /// <summary>The last build sequence number handed out (a result with a larger one is newer).</summary>
    public long Sequence
    {
        get { lock (_gate) return _sequence; }
    }

    /// <summary>Abandoned compile threads of this scheduler still running.</summary>
    public int RetiredThreadsAlive
    {
        get { lock (_gate) return _retired.Count; }
    }

    /// <summary>True while compiles are refused (too many abandoned compiles still running).</summary>
    public bool Refusing
    {
        get { lock (_gate) return _refusing; }
    }

    /// <summary>The managed thread id of the current compile thread (null before the first build and after dispose).</summary>
    public int? CompileThreadId
    {
        get { lock (_gate) return _worker?.Thread?.ManagedThreadId; }
    }

    /// <summary>The keys of the projects with at least one open document.</summary>
    public IReadOnlyList<string> OpenProjects
    {
        get { lock (_gate) return _projects.Keys.Order(StringComparer.Ordinal).ToList(); }
    }

    /// <summary>The project key a workspace path builds as on its own: <c>folder:&lt;folder&gt;</c>, <c>root:&lt;file&gt;</c>, or null for a root-level header (no project, by design).</summary>
    public static string? OwnKey(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        string folder = ProjectResolver.FolderOf(path);
        if (folder.Length > 0) return "folder:" + folder;
        string extension = Path.GetExtension(path).ToLowerInvariant();
        return extension is ".h" or ".kinc" ? null : "root:" + path;
    }

    /// <summary>
    /// The last published build of a project, or its last successful one. <paramref name="keyOrPath"/>
    /// is a project key, or a workspace path (its own project, else any open project that built it).
    /// </summary>
    public ProjectBuildResult? ResultFor(string keyOrPath, bool successfulOnly = false)
    {
        ArgumentNullException.ThrowIfNull(keyOrPath);
        lock (_gate)
        {
            ProjectState? state = _projects.GetValueOrDefault(keyOrPath);
            if (state is null && OwnKey(keyOrPath) is { } own) state = _projects.GetValueOrDefault(own);
            state ??= _projects.Values.FirstOrDefault(s => s.Members.Contains(keyOrPath));
            return state is null ? null : successfulOnly ? state.LastGood : state.Last;
        }
    }

    // ------------------------------------------------------------------ input from the server

    /// <summary>
    /// A document at <paramref name="path"/> was opened: as <see cref="Touch"/>; and when it belongs
    /// to no open project yet and has no project of its own (a root-level header or <c>.kinc</c>), every
    /// open project is rebuilt — one of them may have failed to find it (an unsaved new include).
    /// </summary>
    public void Opened(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        lock (_gate)
        {
            if (_disposed) return;
            if (OwnKey(path) is null && !_projects.Values.Any(s => s.Members.Contains(path)))
            {
                foreach (ProjectState state in _projects.Values) MarkDirty(state);
                return;
            }
        }
        Touch(path);
    }

    /// <summary>A document at <paramref name="path"/> was opened, changed or saved: mark its projects dirty.</summary>
    public void Touch(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        lock (_gate)
        {
            if (_disposed) return;
            if (OwnKey(path) is { } key && !_projects.ContainsKey(key))
            {
                _projects[key] = new ProjectState(key, key.StartsWith("root:", StringComparison.Ordinal) ? key[5..] : path);
            }
            foreach (ProjectState state in _projects.Values)
            {
                if (state.Key == OwnKey(path) || state.Members.Contains(path)) MarkDirty(state);
            }
        }
    }

    /// <summary>
    /// A document at <paramref name="path"/> was closed (and is no longer in the store): projects with
    /// no open document left are removed (<see cref="Removed"/>), the others rebuild with the disk's text.
    /// </summary>
    public void Closed(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var removed = new List<string>();
        lock (_gate)
        {
            if (_disposed) return;
            var open = _documents.Overlay().Keys.ToList();
            foreach (ProjectState state in _projects.Values.ToList())
            {
                bool stillOpen = open.Any(p => OwnKey(p) == state.Key || state.Members.Contains(p));
                if (!stillOpen)
                {
                    _projects.Remove(state.Key);
                    _queue.Remove(state.Key);
                    removed.Add(state.Key);
                }
                else if (state.Key == OwnKey(path) || state.Members.Contains(path))
                {
                    MarkDirty(state);
                }
            }
            Monitor.PulseAll(_gate);
        }
        foreach (string key in removed) Raise(Removed, key);
    }

    /// <summary>Marks every open project dirty (the target changed, files changed on disk).</summary>
    public void InvalidateAll()
    {
        lock (_gate)
        {
            if (_disposed) return;
            foreach (ProjectState state in _projects.Values) MarkDirty(state);
        }
    }

    /// <summary>Queues every dirty project now, without waiting for its debounce (deterministic tests).</summary>
    public void Flush()
    {
        List<ProjectBuildResult> refused;
        lock (_gate)
        {
            if (_disposed) return;
            refused = QueueDue(DateTime.MaxValue);
        }
        foreach (ProjectBuildResult r in refused) Raise(Built, r);
    }

    /// <summary>
    /// Waits until nothing is dirty, queued or compiling (true), or the timeout passes (false). With
    /// an infinite debounce a dirty project never becomes due by itself: call <see cref="Flush"/> first.
    /// </summary>
    public bool WaitForIdle(TimeSpan timeout)
    {
        var clock = Stopwatch.StartNew();
        lock (_gate)
        {
            while (!_disposed && (_queue.Count > 0 || _worker?.Job is not null || _projects.Values.Any(s => s.Due is not null)))
            {
                TimeSpan left = timeout - clock.Elapsed;
                if (left <= TimeSpan.Zero) return false;
                Monitor.Wait(_gate, left);
            }
            return true;
        }
    }

    /// <summary>Waits until every retired thread has ended (true) or the timeout passes (false).</summary>
    public bool WaitForRetiredThreads(TimeSpan timeout)
    {
        var clock = Stopwatch.StartNew();
        lock (_gate)
        {
            while (_retired.Count > 0)
            {
                TimeSpan left = timeout - clock.Elapsed;
                if (left <= TimeSpan.Zero) return false;
                Monitor.Wait(_gate, left);
            }
            return true;
        }
    }

    /// <summary>
    /// Stops the scheduler: no further compile starts; an idle compile thread ends at once (joined), a
    /// busy one when its compile returns. Retired threads are never waited for.
    /// </summary>
    public void Dispose()
    {
        Worker? worker;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            worker = _worker;
            _worker = null;
            if (worker is not null) worker.Retired = worker.Job is not null;
            // A busy thread ends when its compile returns; a stop does not wait for it.
            if (worker is { Retired: true, Thread: { } busy }) LspThreads.Retire(busy);
            _queue.Clear();
            Monitor.PulseAll(_gate);
        }
        _debounce.Dispose();
        _watchdog.Dispose();
        if (worker is { Job: null } idle && idle.Thread is { } thread && thread != Thread.CurrentThread) thread.Join(TimeSpan.FromSeconds(5));
    }

    // ------------------------------------------------------------------ scheduling

    private void MarkDirty(ProjectState state)
    {
        state.Epoch++;
        if (state.Queued) return; // the queued request reads the newest text when it starts
        if (_options.Debounce == Timeout.InfiniteTimeSpan)
        {
            state.Due = DateTime.MaxValue;
            return;
        }
        state.Due = DateTime.UtcNow + _options.Debounce;
        ArmDebounce();
    }

    private void ArmDebounce()
    {
        DateTime? next = null;
        foreach (ProjectState s in _projects.Values)
        {
            if (s.Due is { } due && due != DateTime.MaxValue && (next is null || due < next)) next = due;
        }
        if (next is null) return;
        TimeSpan wait = next.Value - DateTime.UtcNow;
        if (wait < TimeSpan.Zero) wait = TimeSpan.Zero;
        try { _debounce.Change(wait, Timeout.InfiniteTimeSpan); }
        catch (ObjectDisposedException) { /* disposing */ }
    }

    private void OnDebounce()
    {
        List<ProjectBuildResult> refused;
        lock (_gate)
        {
            if (_disposed) return;
            refused = QueueDue(DateTime.UtcNow);
            ArmDebounce();
        }
        foreach (ProjectBuildResult r in refused) Raise(Built, r);
    }

    /// <summary>Moves every project due by <paramref name="now"/> to the queue; while refusing, also makes the refusal results to publish.</summary>
    private List<ProjectBuildResult> QueueDue(DateTime now)
    {
        var refused = new List<ProjectBuildResult>();
        if (_refusing && LspThreads.Retired < _options.MaxRetiredThreads) Unrefuse();
        foreach (ProjectState state in _projects.Values.OrderBy(s => s.Key, StringComparer.Ordinal))
        {
            if (state.Due is not { } due || due > now) continue;
            state.Due = null;
            if (!state.Queued)
            {
                state.Queued = true;
                _queue.Add(state.Key);
            }
            if (_refusing && state.RefusedEpoch != state.Epoch)
            {
                state.RefusedEpoch = state.Epoch;
                refused.Add(SchedulerResult(state, ProjectBuildKind.Refused,
                    $"compiles are suspended: {LspThreads.Retired + 1} compiles did not finish in {Seconds(_options.CompileTimeLimit)} s and are still running; this project builds when one of them ends",
                    "The input that hangs the compiler is probably the last edit of the project that first reported 'compile did not finish'."));
            }
        }
        if (_queue.Count > 0 && !_refusing) EnsureWorker(refused);
        Monitor.PulseAll(_gate);
        return refused;
    }

    /// <summary>
    /// Under <see cref="_gate"/>, with work queued and no compile thread yet (the first build, or a
    /// start that failed): starts one. A thread that cannot be started is reported once per edit.
    /// </summary>
    private void EnsureWorker(List<ProjectBuildResult> refused)
    {
        if (_disposed || _worker is not null) return;
        _worker = StartWorker();
        if (_worker is not null) return;
        foreach (string key in _queue)
        {
            if (!_projects.TryGetValue(key, out var state) || state.RefusedEpoch == state.Epoch) continue;
            state.RefusedEpoch = state.Epoch;
            refused.Add(SchedulerResult(state, ProjectBuildKind.Refused,
                "the language server could not start a compile thread (the game is at its thread limit); this project builds at the next edit",
                "Close other editors or connections, or restart the language server with purr lsp restart."));
        }
    }

    // ------------------------------------------------------------------ compile threads

    private sealed class Worker
    {
        public required int Number { get; init; }
        public Thread? Thread { get; set; }
        public Job? Job { get; set; }
        public bool Retired { get; set; }
    }

    private sealed record Job(long Sequence, string Key, long Epoch, Stopwatch Clock);

    /// <summary>A new compile thread (under <see cref="_gate"/>), or null when none could be started.</summary>
    private Worker? StartWorker()
    {
        var worker = new Worker { Number = Interlocked.Increment(ref _threadNumbers) };
        // The thread waits for _gate before it looks at worker.Thread: it is set before it runs.
        worker.Thread = LspThreads.Start($"purrTTY kFS LSP compile #{worker.Number} ({_name})", () => WorkLoop(worker),
            Compiler.COMPILE_THREAD_STACK_BYTES, ThreadPriority.BelowNormal);
        if (worker.Thread is null)
        {
            _log.Safe(LspLogLevel.Error, $"{_name}: no compile thread could be started; builds wait for the next edit");
            return null;
        }
        _threadsStarted++;
        return worker;
    }

    /// <summary>The compile thread: <see cref="WorkLoopCore"/>, restarted after anything it did not expect (logged), so one bad build never ends compiling.</summary>
    private void WorkLoop(Worker worker)
    {
        while (true)
        {
            try
            {
                WorkLoopCore(worker);
                return;
            }
            catch (Exception e)
            {
                _log.Safe(LspLogLevel.Error, $"{_name}: the compile thread hit {e.GetType().Name}: {e.Message}; it carries on with the next build");
                lock (_gate)
                {
                    worker.Job = null;
                    if (worker.Retired)
                    {
                        _retired.Remove(worker);
                        Monitor.PulseAll(_gate);
                        return;
                    }
                    DisarmWatchdog();
                    Monitor.PulseAll(_gate);
                    if (_disposed) return;
                }
            }
        }
    }

    private void WorkLoopCore(Worker worker)
    {
        while (true)
        {
            ProjectState state;
            Job job;
            IReadOnlyDictionary<string, LspDocument> overlay;
            BuildTarget target;
            lock (_gate)
            {
                while (!_disposed && !worker.Retired && (_queue.Count == 0 || _worker != worker)) Monitor.Wait(_gate);
                if (_disposed || worker.Retired) return;
                string key = _queue[0];
                _queue.RemoveAt(0);
                if (!_projects.TryGetValue(key, out var found)) continue;
                state = found;
                state.Queued = false;
                job = new Job(++_sequence, key, state.Epoch, Stopwatch.StartNew());
                worker.Job = job;
                overlay = _documents.Overlay();
                target = _host.CurrentTarget;
                ArmWatchdog();
            }

            ProjectBuildResult result;
            // Counted while it runs (a retired one too, until it returns): the extension's uninstall
            // leaves the game's header table alone while any compile may read it.
            using (purrTTY.Kfs.Tables.SystemHeaderReaders.EnterCompile())
                result = Run(state, job, overlay, target);

            bool publish;
            lock (_gate)
            {
                worker.Job = null;
                if (worker.Retired)
                {
                    _retired.Remove(worker);
                    _log.Safe(LspLogLevel.Info, $"{_name}: an abandoned compile of {job.Key} returned after {Seconds(job.Clock.Elapsed)} s; its thread ends");
                    if (_refusing && LspThreads.Retired - 1 < _options.MaxRetiredThreads) Unrefuse();
                    Monitor.PulseAll(_gate);
                    return;
                }
                DisarmWatchdog();
                _refusing = false;
                publish = !_disposed && IsFresh(state, job, result, overlay);
                if (publish)
                {
                    state.Last = result;
                    if (result.Ok) state.LastGood = result;
                    state.Members = new HashSet<string>(result.Paths, StringComparer.Ordinal);
                    state.MainFile = result.MainFile;
                }
                Monitor.PulseAll(_gate);
            }
            if (publish)
            {
                _log.Safe(LspLogLevel.Debug, $"{_name}: {result}");
                Raise(Built, result);
            }
            else
            {
                _log.Safe(LspLogLevel.Debug, $"{_name}: {result} -> dropped (superseded)");
            }
        }
    }

    private bool IsFresh(ProjectState state, Job job, ProjectBuildResult result, IReadOnlyDictionary<string, LspDocument> overlay)
    {
        if (!_projects.TryGetValue(state.Key, out var current) || !ReferenceEquals(current, state) || state.Epoch != job.Epoch) return false;
        foreach (string path in result.Paths)
        {
            long? then = overlay.TryGetValue(path, out var a) ? a.Generation : null;
            long? now = _documents.ByPath(path)?.Generation;
            if (then != now) return false;
        }
        return true;
    }

    /// <summary>Resolves and compiles one project; never throws.</summary>
    private ProjectBuildResult Run(ProjectState state, Job job, IReadOnlyDictionary<string, LspDocument> overlay, BuildTarget target)
    {
        var texts = overlay.ToDictionary(p => p.Key, p => p.Value.Text, StringComparer.Ordinal);
        ProjectResolution resolution;
        try
        {
            resolution = _resolver.Resolve(state.ResolveFile, texts);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            resolution = new ProjectResolution(null, $"the project could not be resolved: {e.GetType().Name}: {e.Message}");
        }

        if (resolution.Project is not { } project)
        {
            string file = state.ResolveFile;
            var why = new Diagnostic(Severity.Error, "kfs", file, 0, 0, resolution.Reason ?? "nothing to build");
            return Result(state, job, ProjectBuildKind.NoProject, file, null, null, [why], overlay, target);
        }

        string main = MainFileOf(project);
        ProjectCompileOutcome? outcome;
        try
        {
            outcome = _compile(new ProjectCompileRequest(project, state.ResolveFile, target, _host.Tables));
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            outcome = new ProjectCompileOutcome(state.ResolveFile, project, null, false, null, [], null, null, 0, 0, e, job.Clock.Elapsed);
        }

        if (outcome.Exception is { } thrown)
        {
            _log.Safe(LspLogLevel.Warning, $"{_name}: the {project.LanguageName} toolchain threw on {job.Key}: {thrown.GetType().Name}: {thrown.Message}");
            var error = new Diagnostic(Severity.Error, ErrorCode, project.Name, 0, 0,
                $"the game's {project.LanguageName} toolchain threw {thrown.GetType().Name}: {thrown.Message}",
                "This is a bug in kFS, not necessarily in your program; the language server carries on. Report it with the source.");
            return Result(state, job, ProjectBuildKind.CompilerThrew, main, project, outcome, [error], overlay, target);
        }
        return Result(state, job, ProjectBuildKind.Built, main, project, outcome, outcome.Diagnostics, overlay, target);
    }

    private ProjectBuildResult Result(ProjectState state, Job job, ProjectBuildKind kind, string main, KfsProject? project,
        ProjectCompileOutcome? outcome, IReadOnlyList<Diagnostic> diagnostics, IReadOnlyDictionary<string, LspDocument> overlay, BuildTarget target)
    {
        var texts = new Dictionary<string, string>(StringComparer.Ordinal);
        var paths = new HashSet<string>(StringComparer.Ordinal) { main };
        if (project is not null)
        {
            foreach (KfsProjectFile f in project.Files)
            {
                texts[f.Path] = f.Text;
                paths.Add(f.Path);
            }
            foreach ((string name, string path) in project.Includes)
            {
                paths.Add(path);
                if (texts.ContainsKey(path)) continue;
                try
                {
                    if (project.ReadInclude(name) is { } text) texts[path] = text;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    // The include could not be read again: its diagnostics get no range beyond the line start.
                }
            }
        }
        if (!texts.ContainsKey(main) && ReadText(main, overlay) is { } mainText) texts[main] = mainText;
        var versions = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (string path in paths)
        {
            if (overlay.TryGetValue(path, out var doc)) versions[path] = doc.Version;
        }
        return new ProjectBuildResult(state.Key, job.Sequence, kind, main, project, outcome, diagnostics, texts, versions, paths, target, job.Clock.Elapsed);
    }

    /// <summary>A result the scheduler makes itself (abandoned, refused): one <c>KFS-LSP</c> error on the main file, for the project's last known paths.</summary>
    private ProjectBuildResult SchedulerResult(ProjectState state, ProjectBuildKind kind, string message, string hint)
    {
        IReadOnlyDictionary<string, LspDocument> overlay = _documents.Overlay();
        string main = state.MainFile ?? state.ResolveFile;
        var paths = new HashSet<string>(state.Members, StringComparer.Ordinal) { main };
        var texts = new Dictionary<string, string>(StringComparer.Ordinal);
        if (ReadText(main, overlay) is { } text) texts[main] = text;
        var versions = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (string path in paths)
        {
            if (overlay.TryGetValue(path, out var doc)) versions[path] = doc.Version;
        }
        string projectName = state.Last?.Project?.Name ?? ProjectResolver.NameOf(state.ResolveFile);
        var error = new Diagnostic(Severity.Error, ErrorCode, projectName, 0, 0, message, hint);
        return new ProjectBuildResult(state.Key, ++_sequence, kind, main, state.Last?.Project, null, [error], texts, versions, paths, _host.CurrentTarget, TimeSpan.Zero);
    }

    private string? ReadText(string path, IReadOnlyDictionary<string, LspDocument> overlay)
    {
        if (overlay.TryGetValue(path, out var doc)) return doc.Text;
        try
        {
            return _resolver.Store.Exists(path) ? _resolver.Store.Read(path) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The file "no position" diagnostics are shown on (<see cref="ProjectBuildResult.MainFile"/>).</summary>
    public static string MainFileOf(KfsProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (project.Folder.Length == 0 || project.Files.Count == 0) return project.File;
        string extension = project.Language == KfsProjectLanguage.Kc ? ".c" : ".kasm";
        KfsProjectFile? main = project.Files.FirstOrDefault(f => string.Equals(f.Name, "main" + extension, StringComparison.OrdinalIgnoreCase))
            ?? project.Files.FirstOrDefault(f => f.Name.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            ?? project.Files[0];
        return main.Path;
    }

    // ------------------------------------------------------------------ the watchdog

    private void ArmWatchdog()
    {
        try { _watchdog.Change(_options.CompileTimeLimit, Timeout.InfiniteTimeSpan); }
        catch (ObjectDisposedException) { /* disposing */ }
    }

    private void DisarmWatchdog()
    {
        try { _watchdog.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan); }
        catch (ObjectDisposedException) { /* disposing */ }
    }

    private void OnWatchdog()
    {
        ProjectBuildResult? report = null;
        lock (_gate)
        {
            if (_disposed || _worker is not { Job: { } job } worker) return;
            TimeSpan left = _options.CompileTimeLimit - job.Clock.Elapsed;
            if (left > TimeSpan.Zero)
            {
                // A new compile started after the timer was armed for an earlier one.
                try { _watchdog.Change(left, Timeout.InfiniteTimeSpan); }
                catch (ObjectDisposedException) { }
                return;
            }
            if (!_projects.TryGetValue(job.Key, out var state)) state = null;
            string limit = Seconds(_options.CompileTimeLimit);
            int retired = LspThreads.Retired;   // process-wide: every connection's abandoned compiles
            if (retired < _options.MaxRetiredThreads)
            {
                Retire(worker);
                _worker = StartWorker();
                retired = LspThreads.Retired;
                _log.Safe(LspLogLevel.Warning, $"{_name}: the compile of {job.Key} did not finish in {limit} s; its thread is retired ({retired} of at most {_options.MaxRetiredThreads} in the process) and a fresh one takes over");
                if (state is not null)
                {
                    report = SchedulerResult(state, ProjectBuildKind.Abandoned,
                        $"the compile did not finish in {limit} s and was abandoned (its thread is left to end by itself: {retired} of at most {_options.MaxRetiredThreads})",
                        "The compiler cannot be cancelled. The next edit compiles on a fresh thread; this is a bug in kFS worth reporting with the source.");
                }
            }
            else
            {
                _refusing = true;
                _log.Safe(LspLogLevel.Error, $"{_name}: the compile of {job.Key} did not finish in {limit} s and {retired} abandoned compiles are still running in the process: compiles are refused until one ends");
                if (state is not null)
                {
                    state.RefusedEpoch = state.Epoch;
                    report = SchedulerResult(state, ProjectBuildKind.Abandoned,
                        $"the compile did not finish in {limit} s; {retired} earlier compiles are still running, so no further compile starts until one of them ends",
                        "The compiler cannot be cancelled. This is a bug in kFS worth reporting with the source.");
                }
            }
            Monitor.PulseAll(_gate);
        }
        if (report is not null) Raise(Built, report);
    }

    /// <summary>A retired thread ended while compiles were refused: retire the stuck current one in its place and start a fresh thread.</summary>
    private void Unrefuse()
    {
        if (_disposed) return;
        _refusing = false;
        if (_worker is { Job: not null } stuck)
        {
            Retire(stuck);
            _worker = StartWorker();
            _log.Safe(LspLogLevel.Info, $"{_name}: compiles resume on a fresh thread");
        }
    }

    /// <summary>Retires <paramref name="worker"/> (under <see cref="_gate"/>): counted apart, process-wide, until its compile returns.</summary>
    private void Retire(Worker worker)
    {
        worker.Retired = true;
        _retired.Add(worker);
        if (worker.Thread is { } thread) LspThreads.Retire(thread);
    }

    /// <summary>A timer callback that never throws: an exception there would end the process.</summary>
    private void Guard(string what, Action callback)
    {
        try
        {
            _options.TimerFault?.Invoke(what);
            callback();
        }
        catch (Exception e) { _log.Safe(LspLogLevel.Error, $"{_name}: the {what} timer failed: {e.GetType().Name}: {e.Message}"); }
    }

    // ------------------------------------------------------------------ helpers

    private sealed class ProjectState(string key, string resolveFile)
    {
        public string Key { get; } = key;
        public string ResolveFile { get; } = resolveFile;
        public HashSet<string> Members { get; set; } = new(StringComparer.Ordinal);
        public string? MainFile { get; set; }
        public long Epoch { get; set; }
        public long RefusedEpoch { get; set; } = -1;
        public DateTime? Due { get; set; }
        public bool Queued { get; set; }
        public ProjectBuildResult? Last { get; set; }
        public ProjectBuildResult? LastGood { get; set; }
    }

    private void Raise<T>(Action<T>? handlers, T value)
    {
        if (handlers is null) return;
        foreach (Action<T> handler in handlers.GetInvocationList().Cast<Action<T>>())
        {
            try { handler(value); }
            catch (Exception e) { _log.Safe(LspLogLevel.Error, $"{_name}: a build handler threw {e.GetType().Name}: {e.Message}"); }
        }
    }

    private static string Seconds(TimeSpan t) => t.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture);
}
