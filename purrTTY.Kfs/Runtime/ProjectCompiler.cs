using System.Diagnostics;
using KSA.Kfs.Compiler;
using KSA.Kfs.Compiler.Ir;
using KSA.Kfs.KAsm;
using KSA.Kfs.Vm.Image;
using KSA.Kfs.Vm.Tables;

namespace purrTTY.Kfs.Runtime;

/// <summary>
/// The outcome of <see cref="ProjectCompiler.Build"/>, with the game's own types: every diagnostic
/// (file <b>names</b> as the compiler reported them — map them with <see cref="KfsProject.PathOf"/>),
/// the validated image and its sizes, the IR module (also after a failure, as far as it got), the
/// compile report (successful builds only) — or the exception the toolchain threw.
/// </summary>
/// <param name="File">The file the build was asked for (workspace-relative).</param>
/// <param name="Project">The project, or null when there was none (<see cref="Reason"/>).</param>
/// <param name="Reason">Why there was no project, else null.</param>
/// <param name="Ok">True when an image was produced and validated.</param>
/// <param name="Image">The validated image, or null.</param>
/// <param name="Diagnostics">Every diagnostic in the toolchain's order (plus the workbench's own <c>kfs</c> / <c>K?-E900</c> ones).</param>
/// <param name="Module">The IR module, or null.</param>
/// <param name="Report">The compile report (successful builds), or null.</param>
/// <param name="CodeSize">The validated image's code bytes (0 when the build failed).</param>
/// <param name="RamRequired">The validated image's RAM bytes (0 when the build failed).</param>
/// <param name="Exception">What the toolchain threw (the build then has no diagnostics of its own), else null.</param>
/// <param name="Elapsed">How long the build took.</param>
public sealed record ProjectCompileOutcome(string File, KfsProject? Project, string? Reason, bool Ok, byte[]? Image,
    IReadOnlyList<Diagnostic> Diagnostics, IrModule? Module, CompileReport? Report, uint CodeSize, uint RamRequired,
    Exception? Exception, TimeSpan Elapsed)
{
    /// <summary><c>kC</c>, <c>kASM</c>, or null without a project.</summary>
    public string? Language => Project?.LanguageName;
}

/// <summary>
/// Builds a resolved project the way the game's workbench does (<c>Workbench.BuildText</c>,
/// decompiled <c>Workbench.cs:483-587</c>), shared by <c>purr-kfs</c>, the test runtime and the
/// language server so all three build identically: no project → one <c>kfs</c> error at
/// <c>file:0:0</c>; a kC project → the kC compiler with the file map and the project name; a
/// pure-kASM project → <c>new Assembler(tables, part, layout) { ProjectFiles }</c> over the
/// <c>.kasm</c> files in project order, <c>ProjectFiles</c> reading through
/// <see cref="KfsProject.ReadInclude"/> (overlay first — the game reads the disk only); then the
/// image is validated with <c>ImageReader.Read</c>, a failure adding the game's <c>KC-E900</c> /
/// <c>KA-E900</c> error. It writes nothing (no <c>vehicle.h</c>). An exception from the toolchain is
/// caught and returned in <see cref="ProjectCompileOutcome.Exception"/>; the caller reports it.
/// </summary>
public static class ProjectCompiler
{
    /// <summary>
    /// Builds <paramref name="resolution"/> (resolved for <paramref name="file"/>) for
    /// <paramref name="part"/> against <paramref name="layout"/> on <paramref name="tables"/>.
    /// </summary>
    /// <param name="resolution">The resolved project.</param>
    /// <param name="file">The file the build is for (workspace-relative).</param>
    /// <param name="layout">The vehicle layout.</param>
    /// <param name="tables">The tables (the augmented ones, for <c>x_</c> services).</param>
    /// <param name="part">The processor part.</param>
    /// <param name="compileKc">
    /// How a kC project is compiled: default <c>Compiler.Compile</c> (the game's own: a new 16 MiB
    /// thread per call, 30 s timeout). The language server passes <c>Compiler.CompileOnThisThread</c>
    /// and calls this on its own 16 MiB thread.
    /// </param>
    public static ProjectCompileOutcome Build(ProjectResolution resolution, string file, IoLayout layout,
        GameTables tables, PartDescription part, Func<CompileInput, CompileResult>? compileKc = null)
    {
        ArgumentNullException.ThrowIfNull(resolution);
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(tables);
        ArgumentNullException.ThrowIfNull(part);
        compileKc ??= Compiler.Compile;
        var clock = Stopwatch.StartNew();

        if (resolution.Project is not { } project)
        {
            // Workbench.cs:497-500.
            var why = new Diagnostic(Severity.Error, "kfs", file, 0, 0, resolution.Reason ?? "nothing to build");
            return new ProjectCompileOutcome(file, null, resolution.Reason, false, null, [why], null, null, 0, 0, null, clock.Elapsed);
        }

        IReadOnlyList<Diagnostic> diagnostics;
        byte[]? image;
        CompileReport? report;
        IrModule? module;
        try
        {
            if (project.Language == KfsProjectLanguage.Kc)
            {
                CompileResult r = compileKc(new CompileInput
                {
                    Files = project.FileMap,
                    Part = part,
                    Game = tables,
                    Layout = layout,
                    ProjectName = project.Name,
                });
                (diagnostics, image, report, module) = (r.Diagnostics, r.Ok ? r.Image : null, r.Report, r.Module);
            }
            else
            {
                AssembleResult r = new Assembler(tables, part, layout)
                {
                    ProjectFiles = project.ReadInclude,
                }.Assemble(project.KasmSources.Select(f => new SourceFile(f.Name, f.Text)).ToList());
                (diagnostics, image, report, module) = (r.Diagnostics, r.Ok ? r.Image : null, r.Report, r.Module);
            }
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return new ProjectCompileOutcome(file, project, null, false, null, [], null, null, 0, 0, e, clock.Elapsed);
        }

        if (image is null)
            return new ProjectCompileOutcome(file, project, null, false, null, diagnostics, module, null, 0, 0, null, clock.Elapsed);

        LoadResult load;
        KvmImage? validated;
        try
        {
            load = ImageReader.Read(image, part, tables, layout, out validated);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return new ProjectCompileOutcome(file, project, null, false, null, diagnostics, module, null, 0, 0, e, clock.Elapsed);
        }
        if (!load.Ok || validated is null)
        {
            // Workbench.cs:551-570.
            var all = new List<Diagnostic>(diagnostics)
            {
                new(Severity.Error, project.Language == KfsProjectLanguage.Kc ? "KC-E900" : "KA-E900", file, 0, 0,
                    $"the built image fails validation: {load}", "Report this with the source."),
            };
            return new ProjectCompileOutcome(file, project, null, false, null, all, module, null, 0, 0, null, clock.Elapsed);
        }

        return new ProjectCompileOutcome(file, project, null, true, image, diagnostics, module, report,
            validated.CodeSize, validated.RamRequired, null, clock.Elapsed);
    }
}
