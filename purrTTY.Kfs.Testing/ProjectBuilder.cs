using KSA.Kfs;
using KSA.Kfs.Compiler;
using KSA.Kfs.Vm.Tables;
using purrTTY.Kfs.Runtime;

namespace purrTTY.Kfs.Testing;

/// <summary>A task's worst-case execution time against its budget, from the compiler's report.</summary>
/// <param name="Task">The task name.</param>
/// <param name="Cycles">The worst case, in cycles.</param>
/// <param name="Budget">The cycles the task may use per run.</param>
public sealed record TaskCost(string Task, long Cycles, long Budget);

/// <summary>
/// The outcome of building a resolved project (<see cref="ProjectBuilder"/>), in a shape with no
/// game types: the build result (diagnostics carry the compiler's file <b>names</b>; map them with
/// <see cref="KfsProject.PathOf"/>), the image's code and RAM sizes and each task's WCET.
/// </summary>
/// <param name="File">The file the build was asked for (workspace-relative).</param>
/// <param name="Project">The project, or null when there was none (<see cref="Reason"/>).</param>
/// <param name="Reason">Why there was no project, else null.</param>
/// <param name="Result">The build result; without a project, one <c>kfs</c> error as the workbench reports it.</param>
/// <param name="CodeSize">The validated image's code bytes (0 when the build failed).</param>
/// <param name="RamRequired">The validated image's RAM bytes (0 when the build failed).</param>
/// <param name="Tasks">Per-task WCET and budget from the report (empty when the build failed).</param>
public sealed record ProjectBuild(string File, KfsProject? Project, string? Reason, BuildResult Result,
    uint CodeSize, uint RamRequired, IReadOnlyList<TaskCost> Tasks)
{
    /// <summary>True when an image was produced and validated.</summary>
    public bool Ok => Result.Ok;

    /// <summary><c>kC</c>, <c>kASM</c>, or null without a project.</summary>
    public string? Language => Project?.LanguageName;

    /// <summary>The workspace path of a diagnostic's or trap's file name, or the name itself when it is no workspace file.</summary>
    public string DisplayPath(string name) =>
        Project?.PathOf(name) ?? name;
}

/// <summary>
/// Builds a <see cref="ProjectResolution"/> the way the game's workbench does
/// (<c>Workbench.BuildText</c>, decompiled <c>Workbench.cs:483-587</c>), through
/// <see cref="ProjectCompiler"/> — the one implementation <c>purr-kfs</c>, the tests and the language
/// server share — with the game's own <c>Compiler.Compile</c>, and converts the outcome to game-free
/// types. Unlike the workbench it writes nothing (no <c>vehicle.h</c>), and an exception from the
/// game's toolchain becomes one <c>KFS-TOOL</c> error instead of escaping.
/// </summary>
public static class ProjectBuilder
{
    /// <summary>The code of the diagnostic an exception from the game's compiler becomes.</summary>
    public const string ToolErrorCode = "KFS-TOOL";

    /// <summary>
    /// Builds <paramref name="resolution"/> (resolved for <paramref name="file"/>) against
    /// <paramref name="layout"/> on <paramref name="tables"/> (default: <c>KfsIntegration.Tables</c>, the
    /// augmented tables while a <see cref="KfsRuntime"/> is alive) for <paramref name="part"/>
    /// (default: the tables' bench part, the workbench's own target).
    /// </summary>
    public static ProjectBuild Build(ProjectResolution resolution, string file, IoLayout layout,
        GameTables? tables = null, PartDescription? part = null)
    {
        ArgumentNullException.ThrowIfNull(resolution);
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(layout);
        tables ??= KfsIntegration.Tables;
        part ??= tables.BenchPart;

        ProjectCompileOutcome outcome = ProjectCompiler.Build(resolution, file, layout, tables, part, Compiler.Compile);
        if (outcome.Project is not { } project)
            return new ProjectBuild(file, null, outcome.Reason, new BuildResult(false, null, Convert(outcome.Diagnostics)), 0, 0, []);

        if (outcome.Exception is { } e)
        {
            var thrown = new BuildDiagnostic(file, 0, 0, ToolErrorCode, BuildSeverity.Error,
                $"the game's {project.LanguageName} toolchain threw {e.GetType().Name}: {e.Message}", "This is a bug in kFS or in purr-kfs; report it with the source.");
            return new ProjectBuild(file, project, null, new BuildResult(false, null, [thrown]), 0, 0, []);
        }

        List<BuildDiagnostic> converted = Convert(outcome.Diagnostics);
        if (!outcome.Ok) return new ProjectBuild(file, project, null, new BuildResult(false, null, converted), 0, 0, []);

        var tasks = outcome.Report?.Wcet.Select(w => new TaskCost(w.Task, w.Cycles, w.Budget)).ToList() ?? [];
        return new ProjectBuild(file, project, null, new BuildResult(true, outcome.Image, converted), outcome.CodeSize, outcome.RamRequired, tasks);
    }

    private static List<BuildDiagnostic> Convert(IEnumerable<Diagnostic> diagnostics) =>
        diagnostics.Select(d => new BuildDiagnostic(d.File, d.Line, d.Col, d.Code,
            d.Severity == Severity.Error ? BuildSeverity.Error : BuildSeverity.Warning, d.Message, d.Hint)).ToList();
}
