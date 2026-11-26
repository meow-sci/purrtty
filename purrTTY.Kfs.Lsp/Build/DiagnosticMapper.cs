using System.Text.RegularExpressions;
using KSA.Kfs.Compiler;
using KSA.Kfs.KC;
using purrTTY.Kfs.Lsp.Documents;
using purrTTY.Kfs.Lsp.Protocol;
using purrTTY.Kfs.Runtime;

namespace purrTTY.Kfs.Lsp.Build;

/// <summary>
/// kFS diagnostics → LSP diagnostics, per workspace path (plan §6.4):
/// <list type="bullet">
/// <item><b>Severity</b> error → 1, warning → 2; <b>code</b> as is (<c>KC-E003</c>); <b>source</b> <c>kfs</c>;
/// <b>message</b> with the hint on a second line (<c>hint: …</c>); a hint ending "at file:line" also
/// becomes <c>relatedInformation</c> on that line (workspace files only); <c>K?-W006</c> (never used) is
/// tagged unnecessary.</item>
/// <item><b>File</b> is a compiler file <b>name</b>, mapped through the project
/// (<see cref="KfsProject.PathOf"/>). A name that is no workspace file — <c>&lt;kfs.h&gt;</c>,
/// <c>&lt;vehicle.h&gt;</c>, <c>&lt;purrtty.h&gt;</c>, <c>&lt;math.c&gt;</c>, <c>X.kasm.h</c>, <c>&lt;predefined&gt;</c> — is
/// anchored on the <c>#include</c> line that pulled it in (whole line), with its location in front of
/// the message (<c>[in &lt;kfs.h&gt;:102:6] …</c>).</item>
/// <item><b>No position</b> (line 0, a null file — the project's name, a missing task, limits) is the
/// first line of the project's main file (<see cref="ProjectBuildResult.MainFile"/>).</item>
/// <item><b>Range.</b> kC: the token at the reported column (the game's <see cref="Lexer"/>; a
/// directive's <c>#</c> extends to the end of the line); when the text does not lex
/// (<c>LexException</c>) or no token starts there, the identifier at the column, else one character.
/// kASM (<c>.kasm</c>, <c>.kinc</c>; the assembler reports column 1): the whole trimmed line.</item>
/// <item><b>Positions</b> through <see cref="PositionMapper"/> in the negotiated encoding.</item>
/// </list>
/// Every path of the build gets an entry, empty when it has no diagnostic.
/// </summary>
public static partial class DiagnosticMapper
{
    /// <summary>Maps <paramref name="build"/>'s diagnostics; <paramref name="uriOf"/> turns a workspace path into the URI for related information.</summary>
    public static Dictionary<string, List<LspDiagnostic>> Map(ProjectBuildResult build, PositionEncoding encoding, Func<string, string> uriOf)
    {
        ArgumentNullException.ThrowIfNull(build);
        ArgumentNullException.ThrowIfNull(uriOf);
        var context = new Context(build, encoding, uriOf);
        var result = build.Paths.ToDictionary(p => p, _ => new List<LspDiagnostic>(), StringComparer.Ordinal);
        foreach (Diagnostic d in build.Diagnostics)
        {
            (string path, LspDiagnostic mapped) = context.MapOne(d);
            if (!result.TryGetValue(path, out var list)) result[path] = list = [];
            list.Add(mapped);
        }
        return result;
    }

    private sealed class Context(ProjectBuildResult build, PositionEncoding encoding, Func<string, string> uriOf)
    {
        private readonly Dictionary<string, LineIndex> _lines = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<Token>?> _tokens = new(StringComparer.Ordinal);

        public (string Path, LspDiagnostic Diagnostic) MapOne(Diagnostic d)
        {
            int severity = d.Severity == Severity.Error ? LspSeverity.Error : LspSeverity.Warning;
            string message = d.Message;
            string? path = d.File is { } name && build.Project is { } project ? project.PathOf(name) : null;
            LspRange range;

            if (path is not null && d.Line > 0)
            {
                range = PointRange(path, d.Line, d.Col);
            }
            else if (d.Line > 0 && d.File is { } virtualName)
            {
                // A system, generated or library header (or <predefined>): anchor on its #include line.
                (path, int line) = Anchor(virtualName);
                range = WholeLine(path, line);
                message = $"[in {virtualName}:{d.Line}:{d.Col}] {message}";
            }
            else
            {
                path = build.MainFile;
                range = WholeLine(path, 0);
                if (d.File is { } other && !IsProjectLevel(other)) message = $"[{other}] {message}";
            }

            List<LspRelatedInformation>? related = null;
            if (d.Hint is { } hint)
            {
                message += "\nhint: " + hint;
                if (Related(hint) is { } r) related = [r];
            }
            int[]? tags = d.Code.EndsWith("-W006", StringComparison.Ordinal) ? [1] : null;
            return (path, new LspDiagnostic(range, severity, d.Code, message, "kfs", related, tags));
        }

        private bool IsProjectLevel(string file) =>
            build.Project is { } p && (file == p.Name || p.PathOf(file) is not null) || file == build.MainFile || file == build.Project?.File;

        private LineIndex? Lines(string path)
        {
            if (_lines.TryGetValue(path, out var lines)) return lines;
            if (!build.Texts.TryGetValue(path, out string? text)) return null;
            return _lines[path] = new LineIndex(text);
        }

        private LspRange PointRange(string path, int line, int column)
        {
            if (Lines(path) is not { } lines) return LspRange.OnLine(line - 1, 0, 0);
            int l = Math.Min(line - 1, lines.Count - 1);
            string text = lines.Line(l);
            if (IsKasm(path)) return Trimmed(lines, l);

            int start = Math.Clamp(column - 1, 0, text.Length);
            int end = TokenEnd(path, lines, l, line, column) ?? FallbackEnd(text, start);
            return PositionMapper.Range(lines, l, start, Math.Min(end, text.Length), encoding);
        }

        /// <summary>The end (UTF-16 index in the line) of the token the lexer starts at (line, column), or null.</summary>
        private int? TokenEnd(string path, LineIndex lines, int l, int line, int column)
        {
            if (!_tokens.TryGetValue(path, out var tokens))
            {
                tokens = Lex(path, lines.Text);
                _tokens[path] = tokens;
            }
            Token? token = tokens?.FirstOrDefault(t => t.At.Line == line && t.At.Col == column);
            if (tokens is null)
            {
                // The file does not lex as a whole (an unterminated comment or literal elsewhere): try the line alone.
                List<Token>? lineTokens = Lex(path, lines.Line(l));
                token = lineTokens?.FirstOrDefault(t => t.At.Line == 1 && t.At.Col == column);
            }
            if (token is null || token.Kind == TokKind.Eof) return null;
            string text = lines.Line(l);
            if (token is { Kind: TokKind.Punct, Text: "#", LineStart: true }) return text.TrimEnd().Length;
            return column - 1 + token.Text.Length;
        }

        private static List<Token>? Lex(string name, string text)
        {
            try
            {
                return new Lexer(name, text).Lex();
            }
            catch (LexException)
            {
                return null;
            }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException or IndexOutOfRangeException)
            {
                return null;
            }
        }

        private static int FallbackEnd(string text, int start)
        {
            if (start >= text.Length) return start;
            if (IsIdentifier(text[start]))
            {
                int end = start;
                while (end < text.Length && IsIdentifier(text[end])) end++;
                return end;
            }
            return start + (char.IsSurrogatePair(text, start) ? 2 : 1);
        }

        private static bool IsIdentifier(char c) => char.IsLetterOrDigit(c) || c == '_';

        private LspRange WholeLine(string path, int line)
        {
            if (Lines(path) is not { } lines) return LspRange.OnLine(line, 0, 0);
            return Trimmed(lines, Math.Min(line, lines.Count - 1));
        }

        private LspRange Trimmed(LineIndex lines, int l)
        {
            string text = lines.Line(l);
            int start = text.Length - text.TrimStart().Length;
            int end = text.TrimEnd().Length;
            if (end <= start) start = end = 0;
            return PositionMapper.Range(lines, l, start, end, encoding);
        }

        /// <summary>The project file and 0-based line of the <c>#include</c> that pulled in <paramref name="name"/>; else the main file's first <c>#include</c>, else its first line.</summary>
        private (string Path, int Line) Anchor(string name)
        {
            string bare = name.Trim('<', '>');
            var wanted = new HashSet<string>(StringComparer.Ordinal) { bare };
            // Library sources are compiled because their header was included: <math.c> ← <math.h>.
            if (bare.EndsWith(".c", StringComparison.Ordinal)) wanted.Add(bare[..^2] + ".h");
            if (bare.EndsWith(".kasm", StringComparison.Ordinal)) wanted.Add(bare[..^5] + ".h");

            foreach (string path in SearchOrder())
            {
                if (Lines(path) is not { } lines) continue;
                for (int i = 0; i < lines.Count; i++)
                {
                    Match m = IncludeLine().Match(lines.Line(i));
                    if (m.Success && wanted.Contains(m.Groups["name"].Value)) return (path, i);
                }
            }
            if (Lines(build.MainFile) is { } main)
            {
                for (int i = 0; i < main.Count; i++)
                {
                    if (IncludeLine().IsMatch(main.Line(i))) return (build.MainFile, i);
                }
            }
            return (build.MainFile, 0);
        }

        private IEnumerable<string> SearchOrder()
        {
            yield return build.MainFile;
            if (build.Project is null) yield break;
            foreach (KfsProjectFile f in build.Project.Files)
            {
                if (f.Path != build.MainFile) yield return f.Path;
            }
        }

        private LspRelatedInformation? Related(string hint)
        {
            Match m = HintLocation().Match(hint);
            if (!m.Success || build.Project is not { } project) return null;
            if (project.PathOf(m.Groups["file"].Value) is not { } path) return null;
            if (!int.TryParse(m.Groups["line"].Value, out int line) || line <= 0) return null;
            LspRange range = Lines(path) is { } lines ? Trimmed(lines, Math.Min(line - 1, lines.Count - 1)) : LspRange.OnLine(line - 1, 0, 0);
            return new LspRelatedInformation(new LspLocation(uriOf(path), range), hint);
        }
    }

    private static bool IsKasm(string path) =>
        path.EndsWith(".kasm", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".kinc", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex("^\\s*#\\s*include\\s*[<\"](?<name>[^>\"]+)[>\"]")]
    private static partial Regex IncludeLine();

    // "The first definition is at main.c:3." — the last " at file:line" of the hint (SourceLink's shape).
    [GeneratedRegex("^.* at (?<file>\\S+):(?<line>\\d+)\\.?\\s*$")]
    private static partial Regex HintLocation();
}
