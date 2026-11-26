// RULE FOR THIS FILE: no game type. The texts come from SystemHeaders.Files and VehicleHeader.Generate.

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace purrTTY.Kfs.Lsp.Index;

/// <summary>One system header for <c>#include &lt;…&gt;</c> completion and hover.</summary>
/// <param name="Name">The name between the brackets (<c>kfs.h</c>).</param>
/// <param name="Entry">Its completion / hover entry.</param>
public sealed record HeaderInfo(string Name, IndexEntry Entry);

/// <summary>
/// The names a set of C header texts declares, found by a small line scanner (plan §6.5): object-
/// and function-like <c>#define</c>s with their values, prototypes with their parameter names,
/// typedefs (one-line and <c>typedef struct { … } name;</c> blocks) and <c>extern</c> objects. A
/// <c>/* … */</c> comment directly above a declaration documents it; a comment above a run of
/// <c>#define</c>s documents the whole run (the way <c>&lt;purrtty.h&gt;</c> groups its constants).
/// Conditional directives are ignored, so a name defined in both branches of an <c>#ifdef</c> is
/// indexed once — the definition with an <c>__at</c> address wins for objects. Immutable.
/// </summary>
public sealed partial class HeaderIndex
{
    private readonly Dictionary<string, IndexEntry> _names;

    private HeaderIndex(Dictionary<string, IndexEntry> names, IReadOnlyList<HeaderInfo> headers)
    {
        _names = names;
        Headers = headers;
    }

    /// <summary>Every name, first definition (in header order) winning.</summary>
    public IReadOnlyDictionary<string, IndexEntry> Names => _names;

    /// <summary>The headers scanned, in order.</summary>
    public IReadOnlyList<HeaderInfo> Headers { get; }

    /// <summary>
    /// Scans <paramref name="headers"/> (name → text, in the order given). <paramref name="isBuiltin"/>
    /// tells which prototypes are compiler builtins (<c>SystemHeaders.Builtins</c>);
    /// <paramref name="describe"/> gives a header's one-line description (or null for the scanner's own).
    /// </summary>
    public static HeaderIndex Scan(IEnumerable<KeyValuePair<string, string>> headers, Func<string, bool>? isBuiltin = null,
        Func<string, string?>? describe = null)
    {
        ArgumentNullException.ThrowIfNull(headers);
        isBuiltin ??= _ => false;
        var names = new Dictionary<string, IndexEntry>(StringComparer.Ordinal);
        var infos = new List<HeaderInfo>();
        foreach ((string header, string text) in headers)
        {
            List<IndexEntry> entries = ScanHeader(header, text, isBuiltin);
            foreach (IndexEntry e in entries)
            {
                if (!names.TryGetValue(e.Name, out IndexEntry? existing)) names[e.Name] = e;
                else if (existing.Origin == e.Origin && e.Kind == EntryKind.Variable && e.Signature?.Contains("__at", StringComparison.Ordinal) == true) names[e.Name] = e;
            }
            infos.Add(new HeaderInfo(header, HeaderEntry(header, text, entries, describe?.Invoke(header))));
        }
        return new HeaderIndex(names, infos);
    }

    /// <summary>The entries of one header text, in text order.</summary>
    public static List<IndexEntry> ScanHeader(string header, string text, Func<string, bool> isBuiltin)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(text);
        string[] lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        var entries = new List<IndexEntry>();
        string? comment = null;     // the last comment block
        int commentEnd = -2;        // the line it ended on
        string? group = null;       // a comment heading a run of #defines

        for (int i = 0; i < lines.Length; i++)
        {
            string t = lines[i].Trim();
            if (t.Length == 0)
            {
                group = null;
                comment = null;
                continue;
            }
            if (t.StartsWith("/*", StringComparison.Ordinal))
            {
                var block = new StringBuilder();
                int j = i;
                while (true)
                {
                    block.Append(lines[j]).Append('\n');
                    if (lines[j].Contains("*/", StringComparison.Ordinal) && (j > i || lines[j].IndexOf("*/", StringComparison.Ordinal) > lines[j].IndexOf("/*", StringComparison.Ordinal))) break;
                    if (j + 1 >= lines.Length) break;
                    j++;
                }
                comment = CleanComment(block.ToString());
                commentEnd = j;
                group = comment;
                i = j;
                continue;
            }
            string? above = commentEnd == i - 1 ? comment : null;

            if (Define().Match(t) is { Success: true } d)
            {
                entries.Add(DefineEntry(header, d, above ?? group));
                continue;
            }
            group = null;

            if (t.StartsWith("typedef", StringComparison.Ordinal))
            {
                int end = i;
                string declaration = t;
                if (!t.EndsWith(';') || Depth(t) != 0)
                {
                    var block = new StringBuilder(lines[i].TrimEnd());
                    int depth = Depth(t);
                    while (end + 1 < lines.Length)
                    {
                        end++;
                        block.Append('\n').Append(lines[end].TrimEnd());
                        depth += Depth(lines[end]);
                        if (depth <= 0 && lines[end].TrimEnd().EndsWith(';')) break;
                    }
                    declaration = block.ToString();
                }
                if (TypedefName().Match(declaration) is { Success: true } name)
                    entries.Add(TypedefEntry(header, name.Groups["name"].Value, declaration, above));
                i = end;
                continue;
            }

            if (Extern().Match(t) is { Success: true } x)
            {
                entries.Add(new IndexEntry(x.Groups["name"].Value, EntryKind.Variable, Shorten(t.TrimEnd(';')), t,
                    Markup.Join(above, $"Declared in {Markup.Code($"<{header}>")}."))
                {
                    Origin = header,
                    SortGroup = 2,
                });
                continue;
            }

            if (Prototype().Match(t) is { Success: true } p)
            {
                entries.Add(PrototypeEntry(header, p, above, isBuiltin(p.Groups["name"].Value)));
            }
        }
        return entries;
    }

    // ------------------------------------------------------------------ entries

    private static IndexEntry DefineEntry(string header, Match d, string? doc)
    {
        string name = d.Groups["name"].Value;
        string parameters = d.Groups["params"].Value;
        string body = StripTrailingComment(d.Groups["body"].Value, out string? trailing).Trim();
        string signature = $"#define {name}{parameters}{(body.Length > 0 ? " " + body : "")}";
        string? value = parameters.Length == 0 ? NumericValue(body) : null;
        string documentation = Markup.Join(
            trailing ?? doc,
            value,
            body.Length == 0 && parameters.Length == 0 ? "Defined with an empty body (a feature or include-guard macro)." : null,
            $"Defined in {Markup.Code($"<{header}>")}.");
        return new IndexEntry(name, EntryKind.Macro, Shorten(signature), signature, documentation)
        {
            Origin = header,
            SortGroup = name.StartsWith("__", StringComparison.Ordinal) ? 8 : 3,
            Parameters = parameters.Length > 0 ? SplitParameters(parameters.Trim('(', ')')) : null,
        };
    }

    private static IndexEntry PrototypeEntry(string header, Match p, string? doc, bool builtin)
    {
        string name = p.Groups["name"].Value;
        string signature = p.Value.Trim();
        if (doc is not null && doc.StartsWith(name + " - ", StringComparison.Ordinal)) doc = doc[(name.Length + 3)..];
        string kind = builtin
            ? "A compiler builtin: no library code."
            : "A library routine of the game's kC library, compiled into the image only when it is called.";
        return new IndexEntry(name, EntryKind.Function, Shorten(signature.TrimEnd(';')), signature,
            Markup.Join(doc, kind, $"Declared in {Markup.Code($"<{header}>")}."))
        {
            Origin = header,
            SortGroup = 2,
            Parameters = SplitParameters(p.Groups["params"].Value),
        };
    }

    private static IndexEntry TypedefEntry(string header, string name, string declaration, string? doc)
    {
        string signature = Pretty(declaration);
        string detail = declaration.Contains('{') ? $"struct {name}" : Shorten(declaration.TrimEnd(';'));
        return new IndexEntry(name, EntryKind.Type, detail, signature, Markup.Join(doc, $"Defined in {Markup.Code($"<{header}>")}."))
        {
            Origin = header,
            SortGroup = 4,
        };
    }

    private static IndexEntry HeaderEntry(string header, string text, List<IndexEntry> entries, string? description)
    {
        int prototypes = entries.Count(e => e.Kind == EntryKind.Function);
        int macros = entries.Count(e => e.Kind == EntryKind.Macro);
        int types = entries.Count(e => e.Kind == EntryKind.Type);
        var parts = new List<string>();
        if (prototypes > 0) parts.Add($"{prototypes} prototype{(prototypes == 1 ? "" : "s")}");
        if (macros > 0) parts.Add($"{macros} macro{(macros == 1 ? "" : "s")}");
        if (types > 0) parts.Add($"{types} type{(types == 1 ? "" : "s")}");
        string counts = parts.Count == 0 ? "declares nothing" : "declares " + string.Join(", ", parts);
        string? banner = description ?? FirstComment(text);
        string sample = string.Join(", ", entries.Where(e => e.Kind is EntryKind.Function or EntryKind.Macro && !e.Name.StartsWith("__", StringComparison.Ordinal))
            .Take(8).Select(e => Markup.Code(e.Name)));
        return new IndexEntry(header, EntryKind.Header, banner ?? counts, $"#include <{header}>",
            Markup.Join(banner, $"It {counts}{(sample.Length > 0 ? ": " + sample + (prototypes + macros > 8 ? ", …" : "") : "")}."))
        {
            InsertText = header,
            SortGroup = header == "kfs.h" ? 0 : 1,
        };
    }

    // ------------------------------------------------------------------ text helpers

    /// <summary>The parameter names of a C parameter list (<c>void</c> and <c>...</c> dropped).</summary>
    public static IReadOnlyList<string> SplitParameters(string parameters)
    {
        var names = new List<string>();
        foreach (string raw in parameters.Split(','))
        {
            string p = raw.Trim();
            if (p.Length == 0 || p == "void" || p == "...") continue;
            Match m = LastIdentifier().Match(p);
            names.Add(m.Success ? m.Value : p);
        }
        return names;
    }

    /// <summary>A value line for a numeric macro body (<c>0x0102</c> → "Value: 258 (0x0102)"), or null.</summary>
    internal static string? NumericValue(string body)
    {
        string b = body.Trim();
        while (b.StartsWith('(') && b.EndsWith(')')) b = b[1..^1].Trim();
        string digits = b.TrimEnd('u', 'U', 'l', 'L');
        if (digits.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            && ulong.TryParse(digits[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong hex))
            return $"Value: {hex.ToString(CultureInfo.InvariantCulture)} ({b}).";
        if (long.TryParse(digits, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long dec) && dec >= 16)
            return $"Value: {dec.ToString(CultureInfo.InvariantCulture)} (0x{dec:X}).";
        return null;
    }

    private static string StripTrailingComment(string body, out string? comment)
    {
        comment = null;
        int block = body.IndexOf("/*", StringComparison.Ordinal);
        if (block >= 0)
        {
            int end = body.IndexOf("*/", block, StringComparison.Ordinal);
            comment = CleanComment(end > block ? body[block..(end + 2)] : body[block..]);
            return body[..block];
        }
        int line = body.IndexOf("//", StringComparison.Ordinal);
        if (line >= 0)
        {
            comment = body[(line + 2)..].Trim();
            return body[..line];
        }
        return body;
    }

    /// <summary>A comment's text: delimiters and leading <c>*</c> removed; lines that end a sentence start a new paragraph.</summary>
    internal static string CleanComment(string block)
    {
        string inner = block.Trim();
        if (inner.StartsWith("/*", StringComparison.Ordinal)) inner = inner[2..];
        if (inner.EndsWith("*/", StringComparison.Ordinal)) inner = inner[..^2];
        var sb = new StringBuilder();
        foreach (string raw in inner.Split('\n'))
        {
            string line = raw.Trim();
            if (line.StartsWith('*')) line = line[1..].TrimStart();
            if (line.Length == 0) continue;
            if (sb.Length > 0) sb.Append(sb[^1] is '.' or ':' ? "\n\n" : " ");
            sb.Append(line);
        }
        return sb.ToString();
    }

    private static string? FirstComment(string text)
    {
        string t = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        int start = t.IndexOf("/*", StringComparison.Ordinal);
        if (start < 0) return null;
        // Only a banner: the comment must come before the first declaration.
        string before = t[..start];
        if (before.Split('\n').Any(l => l.Trim().Length > 0 && !l.TrimStart().StartsWith('#'))) return null;
        int end = t.IndexOf("*/", start, StringComparison.Ordinal);
        string comment = CleanComment(end > start ? t[start..(end + 2)] : t[start..]);
        int stop = comment.IndexOf(". ", StringComparison.Ordinal);
        return stop > 0 ? comment[..(stop + 1)] : comment;
    }

    private static int Depth(string line) => line.Count(c => c == '{') - line.Count(c => c == '}');

    /// <summary>A one-line struct typedef over 100 characters, one member per line.</summary>
    private static string Pretty(string declaration)
    {
        if (declaration.Contains('\n') || declaration.Length <= 100 || !declaration.Contains('{')) return declaration;
        int open = declaration.IndexOf('{');
        int close = declaration.LastIndexOf('}');
        if (close < open) return declaration;
        var members = declaration[(open + 1)..close].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return declaration[..(open + 1)] + "\n" + string.Concat(members.Select(m => "    " + m + ";\n")) + declaration[close..];
    }

    private static string Shorten(string s)
    {
        s = string.Join(' ', s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return s.Length <= 80 ? s : s[..77] + "…";
    }

    [GeneratedRegex(@"^#\s*define\s+(?<name>[A-Za-z_]\w*)(?<params>\([^)]*\))?(?<body>.*)$")]
    private static partial Regex Define();

    [GeneratedRegex(@"(?<name>[A-Za-z_]\w*)\s*;\s*$")]
    private static partial Regex TypedefName();

    [GeneratedRegex(@"^extern\s+.*?\b(?<name>[A-Za-z_]\w*)\s*(?:__at\s*\([^)]*\))?\s*;$")]
    private static partial Regex Extern();

    [GeneratedRegex(@"^(?!typedef\b|extern\b|return\b|#)(?:(?:const|unsigned|signed|struct|static|inline)\s+)*[A-Za-z_]\w*(?:\s*\*)*\s*\b(?<name>[A-Za-z_]\w*)\s*\((?<params>[^()]*)\)\s*;$")]
    private static partial Regex Prototype();

    [GeneratedRegex(@"[A-Za-z_]\w*(?=\s*(\[[^\]]*\])?\s*$)")]
    private static partial Regex LastIdentifier();
}
