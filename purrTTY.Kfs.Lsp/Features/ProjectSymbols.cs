using System.Runtime.CompilerServices;
using KSA.Kfs.Compiler;
using KSA.Kfs.Compiler.Ir;
using purrTTY.Kfs.Lsp.Build;
using purrTTY.Kfs.Lsp.Index;
using IrEntryKind = KSA.Kfs.Compiler.Ir.EntryKind;
using EntryKind = purrTTY.Kfs.Lsp.Index.EntryKind;

namespace purrTTY.Kfs.Lsp.Features;

/// <summary>A label of a kASM function of the last successful build.</summary>
internal sealed record ProjectLabel(string Function, string Name, string File, int Line);

/// <summary>
/// The project's own functions and globals from the <b>last successful</b> build of a project
/// (<c>IrModule.Functions</c> / <c>.Globals</c>, library and generated ones left out), as index
/// entries. What a build result holds is the name, the file and line of the declaration, the entry
/// role (task, init, program, with divisor and priority), the parameter slots and, for kASM, the
/// <c>.proto</c>; the declaration's source line is taken from the text that build compiled. It does
/// not hold return types, comments, locals' positions or anything of a failed build. Built once per
/// build result and cached with it.
/// </summary>
internal sealed class ProjectSymbols
{
    private static readonly ConditionalWeakTable<ProjectBuildResult, ProjectSymbols> Cache = new();

    public static readonly ProjectSymbols Empty = new();

    private ProjectSymbols() { }

    public Dictionary<string, IndexEntry> Names { get; } = new(StringComparer.Ordinal);
    public List<ProjectLabel> Labels { get; } = [];

    /// <summary>The build this came from, or null.</summary>
    public ProjectBuildResult? Build { get; private set; }

    /// <summary>The symbols of the last successful build of the project that <paramref name="path"/> belongs to (empty without one).</summary>
    public static ProjectSymbols For(KfsLanguageServer server, string? path)
    {
        if (path is null || server.Builds.ResultFor(path, successfulOnly: true) is not { Outcome.Module: { } } build) return Empty;
        return Cache.GetValue(build, From);
    }

    private static ProjectSymbols From(ProjectBuildResult build)
    {
        var symbols = new ProjectSymbols { Build = build };
        IrModule module = build.Outcome!.Module!;
        string age = $"From the last successful build ({build.Target.DisplayName}), which may be older than the buffer.";
        foreach (IrFunction f in module.Functions)
        {
            if (f.At.File is null || f.At.File.StartsWith('<') || f.Name.Contains('.')) continue;
            string? line = SourceLine(build, f.At);
            bool kasm = f.Language == "kasm";
            string? signature = kasm ? f.Proto is { } proto ? proto.TrimEnd().TrimEnd(';') + ";" : $".func {f.Name}" : Declaration(line, f.At.Col, untilParen: true);
            string? role = f.Entry switch
            {
                IrEntryKind.Periodic => $"A periodic task: rate divisor {f.RateDivisor}, priority {f.Priority}.",
                IrEntryKind.Init => "The init task.",
                IrEntryKind.Program => $"The program task{(f.Share > 0 ? $" (share {f.Share} %)" : "")}.",
                _ => null,
            };
            var parameters = f.Slots.Where(s => s.Argument && !s.Temporary).Select(s => s.Name).ToList();
            string where = $"{(kasm ? "kASM" : "kC")} function{(f.Static ? " (static)" : "")} in `{f.At.File}` line {f.At.Line}.";
            string known = signature is null || kasm && f.Proto is null
                ? "Its prototype is not in the build result" + (kasm ? " (no `.proto`)." : "; the declaration line could not be read.")
                : "";
            symbols.Names.TryAdd(f.Name, new IndexEntry(f.Name, EntryKind.ProjectFunction, signature is null ? "function" : Shorten(signature.TrimEnd(';')),
                signature, Markup.Join(where, role, parameters.Count > 0 && signature is null ? "Parameters: " + string.Join(", ", parameters.Select(Markup.Code)) + "." : null, known, age),
                kasm && f.Proto is null ? "kasm" : "c")
            {
                SortGroup = 0,
                Parameters = parameters,
            });
            foreach (IrItem item in f.Body)
            {
                if (item is IrLabel label) symbols.Labels.Add(new ProjectLabel(f.Name, label.Name, label.At.File ?? "", label.At.Line));
            }
        }
        foreach (IrGlobal g in module.Globals)
        {
            if (g.Generated || g.At.File is null || g.At.File.StartsWith('<') || g.Name.StartsWith('.')) continue;
            string? signature = Declaration(SourceLine(build, g.At), g.At.Col, untilParen: false);
            symbols.Names.TryAdd(g.Name, new IndexEntry(g.Name, EntryKind.ProjectGlobal, signature is null ? $"global, {g.Size} bytes" : Shorten(signature.TrimEnd(';')),
                signature, Markup.Join($"Global{(g.Const ? " (read-only)" : "")} in `{g.At.File}` line {g.At.Line}: {g.Size} bytes, align {g.Align}.", age))
            {
                SortGroup = 0,
            });
        }
        return symbols;
    }

    /// <summary>The text of the line a span points at, from the text the build compiled.</summary>
    private static string? SourceLine(ProjectBuildResult build, Span at)
    {
        if (build.Project?.PathOf(at.File) is not { } path || !build.Texts.TryGetValue(path, out string? text) || at.Line <= 0) return null;
        var lines = new Documents.LineIndex(text);
        return at.Line - 1 < lines.Count ? lines.Line(at.Line - 1) : null;
    }

    /// <summary>The declaration on <paramref name="line"/> around the name at 1-based <paramref name="col"/>: up to the parameter list's <c>)</c>, or up to <c>=</c>, <c>;</c>, <c>{</c>.</summary>
    private static string? Declaration(string? line, int col, bool untilParen)
    {
        if (line is null) return null;
        int name = Math.Clamp(col - 1, 0, line.Length);
        int end = -1;
        if (untilParen)
        {
            int open = line.IndexOf('(', name);
            if (open < 0) return null;
            int depth = 0;
            for (int i = open; i < line.Length; i++)
            {
                if (line[i] == '(') depth++;
                else if (line[i] == ')' && --depth == 0)
                {
                    end = i + 1;
                    break;
                }
            }
            if (end < 0) return null;
        }
        else
        {
            end = line.IndexOfAny(['=', ';', '{'], name);
            if (end < 0) end = line.Length;
        }
        string declaration = line[..end].Trim();
        return declaration.Length == 0 ? null : declaration + ";";
    }

    private static string Shorten(string s) => s.Length <= 80 ? s : s[..77] + "…";
}
