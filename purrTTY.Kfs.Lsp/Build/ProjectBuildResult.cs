using KSA.Kfs.Compiler;
using KSA.Kfs.Vm.Tables;
using purrTTY.Kfs.Runtime;

namespace purrTTY.Kfs.Lsp.Build;

/// <summary>What one scheduled compile is asked to build (the input of a <see cref="ProjectCompileFunction"/>).</summary>
/// <param name="Project">The resolved project, every open buffer overlaid.</param>
/// <param name="File">The file the project was resolved for (workspace-relative).</param>
/// <param name="Target">The part and layout, captured when the compile started.</param>
/// <param name="Tables">The host's tables.</param>
public sealed record ProjectCompileRequest(KfsProject Project, string File, BuildTarget Target, GameTables Tables);

/// <summary>
/// Compiles one project. The default (<see cref="BuildScheduler.DefaultCompile"/>) is
/// <see cref="ProjectCompiler"/> with <c>Compiler.CompileOnThisThread</c>; tests inject failing or
/// blocking ones. Runs on the scheduler's compile thread; may throw (the scheduler catches it).
/// </summary>
public delegate ProjectCompileOutcome ProjectCompileFunction(ProjectCompileRequest request);

/// <summary>What became of a scheduled build.</summary>
public enum ProjectBuildKind
{
    /// <summary>The toolchain ran (the build may still have failed: see <see cref="ProjectBuildResult.Ok"/>).</summary>
    Built,
    /// <summary>The file set could not be resolved (a read failure); one <c>kfs</c> error.</summary>
    NoProject,
    /// <summary>The toolchain threw; one <c>KFS-LSP</c> error.</summary>
    CompilerThrew,
    /// <summary>The compile ran past the watchdog's limit and was abandoned; one <c>KFS-LSP</c> error.</summary>
    Abandoned,
    /// <summary>Compiles are suspended (too many abandoned compiles still running); one <c>KFS-LSP</c> error.</summary>
    Refused,
}

/// <summary>
/// One finished (or abandoned, or refused) build of a project, as the scheduler publishes it. It is
/// immutable; feature providers read the last one of a project, or the last successful one, through
/// <see cref="BuildScheduler.ResultFor"/>.
/// </summary>
public sealed class ProjectBuildResult
{
    internal ProjectBuildResult(string key, long sequence, ProjectBuildKind kind, string mainFile, KfsProject? project,
        ProjectCompileOutcome? outcome, IReadOnlyList<Diagnostic> diagnostics, IReadOnlyDictionary<string, string> texts,
        IReadOnlyDictionary<string, int> versions, IReadOnlySet<string> paths, BuildTarget target, TimeSpan elapsed)
    {
        Key = key;
        Sequence = sequence;
        Kind = kind;
        MainFile = mainFile;
        Project = project;
        Outcome = outcome;
        Diagnostics = diagnostics;
        Texts = texts;
        Versions = versions;
        Paths = paths;
        Target = target;
        Elapsed = elapsed;
    }

    /// <summary>The project's key: <c>folder:&lt;folder&gt;</c> or <c>root:&lt;file&gt;</c>.</summary>
    public string Key { get; }

    /// <summary>Grows with every compile the scheduler starts; a later result of a project has a larger one.</summary>
    public long Sequence { get; }

    /// <summary>What became of the build.</summary>
    public ProjectBuildKind Kind { get; }

    /// <summary>True when an image was produced and validated.</summary>
    public bool Ok => Kind == ProjectBuildKind.Built && Outcome is { Ok: true };

    /// <summary>
    /// The workspace path "no position" diagnostics are shown on: a root project's file; in a folder,
    /// <c>main.c</c> / <c>main.kasm</c> when there is one, else the first <c>.c</c> (kC) or <c>.kasm</c>
    /// (kASM) file, else the first file.
    /// </summary>
    public string MainFile { get; }

    /// <summary>The project that was built (every open buffer overlaid), or null.</summary>
    public KfsProject? Project { get; }

    /// <summary>The toolchain's outcome (diagnostics, image, IR module, compile report), or null when it did not run to the end.</summary>
    public ProjectCompileOutcome? Outcome { get; }

    /// <summary>The diagnostics to publish: the toolchain's, or the scheduler's one <c>KFS-LSP</c> / <c>kfs</c> error.</summary>
    public IReadOnlyList<Diagnostic> Diagnostics { get; }

    /// <summary>The text each workspace path had in this build (project files and the kASM includes read).</summary>
    public IReadOnlyDictionary<string, string> Texts { get; }

    /// <summary>The client's version of each open buffer the build used, by path.</summary>
    public IReadOnlyDictionary<string, int> Versions { get; }

    /// <summary>Every workspace path the build covers (its files, the includes it read, the main file): diagnostics are published for each.</summary>
    public IReadOnlySet<string> Paths { get; }

    /// <summary>The target the build was for.</summary>
    public BuildTarget Target { get; }

    /// <summary>How long resolving and compiling took.</summary>
    public TimeSpan Elapsed { get; }

    /// <inheritdoc/>
    public override string ToString() =>
        $"{Key} #{Sequence}: {Kind}{(Kind == ProjectBuildKind.Built ? Ok ? " ok" : " failed" : "")}, {Diagnostics.Count} diagnostic(s), {Elapsed.TotalMilliseconds:0.0} ms";
}
