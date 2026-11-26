using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using purrTTY.Kfs.Runtime;
using purrTTY.Kfs.Testing;

namespace purrTTY.Kfs.Cli.Commands;

/// <summary>How <c>check</c> and <c>run</c> print a build: diagnostics, the summary line, the JSON document.</summary>
internal static class BuildOutput
{
    /// <summary>The workspace path a diagnostic's file name maps to, or null for a virtual file (<c>&lt;kfs.h&gt;</c> …).</summary>
    public static string? PathOf(ProjectBuild build, string name)
    {
        if (build.Project is { } project) return project.PathOf(name);
        // Without a project the only diagnostic is the workbench's "kfs" error on the file itself.
        return string.Equals(name, build.File, StringComparison.Ordinal) ? build.File : null;
    }

    /// <summary><c>path:line:col: severity code: message</c>, the hint on the next line indented (the game's format with the file mapped to its workspace path).</summary>
    public static string Format(ProjectBuild build, BuildDiagnostic d)
    {
        string file = PathOf(build, d.File) ?? d.File;
        string severity = d.Severity == BuildSeverity.Error ? "error" : "warning";
        string text = string.Create(CultureInfo.InvariantCulture, $"{file}:{d.Line}:{d.Column}: {severity} {d.Code}: {d.Message}");
        return d.Hint is null ? text : text + "\n  hint: " + d.Hint;
    }

    /// <summary>
    /// <c>build ok: proj/main.c (kC project "proj", 2 files): 0 errors, 1 warning; code 120 bytes, RAM 64 bytes</c>,
    /// <c>build failed: proj/main.c (kC project "proj", 2 files): 2 errors, 0 warnings</c>,
    /// <c>build failed: x.h (no project): 1 error, 0 warnings</c>.
    /// </summary>
    public static string Summary(ProjectBuild build)
    {
        int errors = build.Result.Diagnostics.Count(d => d.Severity == BuildSeverity.Error);
        int warnings = build.Result.Diagnostics.Count - errors;
        string what = build.Project is { } p
            ? $"{p.LanguageName} project \"{p.Name}\", {Count(p.Files.Count, "file")}"
            : "no project";
        string text = $"build {(build.Ok ? "ok" : "failed")}: {build.File} ({what}): {Count(errors, "error")}, {Count(warnings, "warning")}";
        if (build.Ok) text += string.Create(CultureInfo.InvariantCulture, $"; code {build.CodeSize} bytes, RAM {build.RamRequired} bytes");
        return text;
    }

    /// <summary>Every diagnostic in the compiler's order, then the summary.</summary>
    public static void WriteText(TextWriter stdout, ProjectBuild build)
    {
        foreach (BuildDiagnostic d in build.Result.Diagnostics) CliApp.Lines(stdout, Format(build, d));
        stdout.WriteLine(Summary(build));
    }

    /// <summary>
    /// The <c>check --json</c> document (stable shape, keys in this order, <c>\n</c> line ends):
    /// <c>file</c>, <c>ok</c>, <c>errors</c>, <c>warnings</c>, <c>project</c> (<c>name</c>, <c>folder</c>,
    /// <c>language</c>, <c>files</c>: workspace paths, <c>overlay</c>: names, <c>includes</c>: kASM include name →
    /// path) or null, <c>reason</c>, <c>diagnostics</c> (<c>file</c>: the compiler's name, <c>path</c>: the
    /// workspace path or null, <c>line</c>, <c>column</c>, <c>severity</c>, <c>code</c>, <c>message</c>, <c>hint</c>),
    /// <c>image</c> (<c>bytes</c>, <c>code</c>, <c>ram</c>) or null, <c>tasks</c> (<c>name</c>, <c>wcet</c>, <c>budget</c>: cycles).
    /// </summary>
    public static string Json(ProjectBuild build)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions
        {
            Indented = true,
            NewLine = "\n",
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }))
        {
            int errors = build.Result.Diagnostics.Count(d => d.Severity == BuildSeverity.Error);
            w.WriteStartObject();
            w.WriteString("file", build.File);
            w.WriteBoolean("ok", build.Ok);
            w.WriteNumber("errors", errors);
            w.WriteNumber("warnings", build.Result.Diagnostics.Count - errors);

            if (build.Project is { } p)
            {
                w.WriteStartObject("project");
                w.WriteString("name", p.Name);
                w.WriteString("folder", p.Folder);
                w.WriteString("language", p.LanguageName);
                w.WriteStartArray("files");
                foreach (KfsProjectFile f in p.Files) w.WriteStringValue(f.Path);
                w.WriteEndArray();
                w.WriteStartArray("overlay");
                foreach (KfsProjectFile f in p.Files.Where(f => f.FromOverlay)) w.WriteStringValue(f.Name);
                w.WriteEndArray();
                w.WriteStartObject("includes");
                foreach ((string name, string path) in p.Includes.OrderBy(i => i.Key, StringComparer.Ordinal)) w.WriteString(name, path);
                w.WriteEndObject();
                w.WriteEndObject();
            }
            else
            {
                w.WriteNull("project");
            }
            if (build.Reason is null) w.WriteNull("reason");
            else w.WriteString("reason", build.Reason);

            w.WriteStartArray("diagnostics");
            foreach (BuildDiagnostic d in build.Result.Diagnostics)
            {
                w.WriteStartObject();
                w.WriteString("file", d.File);
                if (PathOf(build, d.File) is { } path) w.WriteString("path", path);
                else w.WriteNull("path");
                w.WriteNumber("line", d.Line);
                w.WriteNumber("column", d.Column);
                w.WriteString("severity", d.Severity == BuildSeverity.Error ? "error" : "warning");
                w.WriteString("code", d.Code);
                w.WriteString("message", d.Message);
                if (d.Hint is null) w.WriteNull("hint");
                else w.WriteString("hint", d.Hint);
                w.WriteEndObject();
            }
            w.WriteEndArray();

            if (build.Ok && build.Result.Image is { } image)
            {
                w.WriteStartObject("image");
                w.WriteNumber("bytes", image.Length);
                w.WriteNumber("code", build.CodeSize);
                w.WriteNumber("ram", build.RamRequired);
                w.WriteEndObject();
            }
            else
            {
                w.WriteNull("image");
            }

            w.WriteStartArray("tasks");
            foreach (TaskCost t in build.Tasks)
            {
                w.WriteStartObject();
                w.WriteString("name", t.Task);
                w.WriteNumber("wcet", t.Cycles);
                w.WriteNumber("budget", t.Budget);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary><c>1 file</c>, <c>2 files</c>.</summary>
    public static string Count(int n, string noun) => string.Create(CultureInfo.InvariantCulture, $"{n} {noun}{(n == 1 ? "" : "s")}");

    /// <summary>A number with up to three decimals, invariant (<c>2</c>, <c>0.9</c>, <c>1.571</c>).</summary>
    public static string Num(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
