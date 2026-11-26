// RULE FOR THIS FILE: no game type.

using System.Text.RegularExpressions;

namespace purrTTY.Kfs.Lsp.Features;

/// <summary>One line of kASM split the way the assembler splits it (comment, label, word, rest).</summary>
/// <param name="Label">The label, or null.</param>
/// <param name="LabelEnd">The UTF-16 column just past the label's colon (0 without a label).</param>
/// <param name="Word">The first token after the label (a directive, mnemonic or macro), or empty.</param>
/// <param name="WordStart">Its column.</param>
/// <param name="Rest">The operand text after the word, comment removed.</param>
/// <param name="RestStart">Its column.</param>
/// <param name="CommentStart">The column of a <c>;</c> comment, or -1.</param>
internal readonly record struct KasmLine(string? Label, int LabelEnd, string Word, int WordStart, string Rest, int RestStart, int CommentStart)
{
    public static KasmLine Parse(string line)
    {
        int comment = -1;
        bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] == '"') quoted = !quoted;
            else if (line[i] == ';' && !quoted)
            {
                comment = i;
                break;
            }
        }
        string code = comment >= 0 ? line[..comment] : line;
        string? label = null;
        int labelEnd = 0;
        Match m = SourceScan.KasmLabel().Match(code);
        if (m.Success)
        {
            label = m.Groups["name"].Value;
            labelEnd = m.Index + m.Length;
        }
        int w = labelEnd;
        while (w < code.Length && char.IsWhiteSpace(code[w])) w++;
        int e = w;
        while (e < code.Length && !char.IsWhiteSpace(code[e])) e++;
        int r = e;
        while (r < code.Length && char.IsWhiteSpace(code[r])) r++;
        return new KasmLine(label, labelEnd, code[w..e], w, code[r..].TrimEnd(), r, comment);
    }
}

/// <summary>A kASM <c>.func</c> of a document.</summary>
internal sealed record KasmFunction(string Name, int Line, int EndLine, string? Proto, List<(string Name, int Line)> Labels, List<(string Name, string Directive, int Line)> Slots);

/// <summary>What one kASM text defines, read line by line (no assembler run): enough for operand completion and hover.</summary>
internal sealed class KasmScan
{
    public List<KasmFunction> Functions { get; } = [];
    public Dictionary<string, (int Line, string Value)> Equs { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, (int Line, string Directive, string Text)> Data { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, int> Macros { get; } = new(StringComparer.Ordinal);
    public HashSet<string> Externs { get; } = new(StringComparer.Ordinal);
    public HashSet<string> Includes { get; } = new(StringComparer.Ordinal);

    public KasmFunction? FunctionAt(int line) => Functions.FirstOrDefault(f => f.Line <= line && line <= f.EndLine);

    public static KasmScan Of(string text)
    {
        var scan = new KasmScan();
        string[] lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        string? proto = null;
        (string Name, int Line, string? Proto, List<(string, int)> Labels, List<(string, string, int)> Slots)? open = null;
        bool inMacro = false;
        for (int i = 0; i < lines.Length; i++)
        {
            KasmLine k = KasmLine.Parse(lines[i]);
            string word = k.Word.ToLowerInvariant();
            if (inMacro)
            {
                if (word == ".endm") inMacro = false;
                continue;
            }
            if (k.Label is { } label && open is { } f) f.Labels.Add((label, i));
            string first = FirstName(k.Rest);
            switch (word)
            {
                case ".macro":
                    if (first.Length > 0) scan.Macros.TryAdd(first, i);
                    inMacro = true;
                    break;
                case ".include":
                    string target = k.Rest.Trim();
                    if (target.Length >= 2) scan.Includes.Add(target[1..^1].Trim());
                    break;
                case ".equ":
                    int comma = k.Rest.IndexOf(',');
                    if (first.Length > 0) scan.Equs.TryAdd(first, (i, comma >= 0 ? k.Rest[(comma + 1)..].Trim() : ""));
                    break;
                case ".string":
                case ".format":
                case ".global":
                case ".data":
                case ".const":
                    if (first.Length > 0) scan.Data.TryAdd(first, (i, word, lines[i].Trim()));
                    break;
                case ".publish":
                case ".subscribe":
                    string[] parts = k.Rest.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2) scan.Data.TryAdd(parts[1], (i, word, lines[i].Trim()));
                    break;
                case ".extern":
                    foreach (string name in k.Rest.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) scan.Externs.Add(name);
                    break;
                case ".proto":
                    proto = k.Rest.Trim().Trim('"');
                    break;
                case ".func":
                    if (open is { } previous) scan.Functions.Add(new KasmFunction(previous.Name, previous.Line, i - 1, previous.Proto, previous.Labels, previous.Slots));
                    open = (first, i, proto, [], []);
                    proto = null;
                    break;
                case ".endfunc":
                    if (open is { } closing) scan.Functions.Add(new KasmFunction(closing.Name, closing.Line, i, closing.Proto, closing.Labels, closing.Slots));
                    open = null;
                    break;
                case ".args":
                case ".args64":
                case ".local":
                case ".local64":
                    if (open is { } owner)
                    {
                        if (word.StartsWith(".local", StringComparison.Ordinal))
                        {
                            if (first.Length > 0) owner.Slots.Add((first, word, i));
                        }
                        else
                        {
                            foreach (string name in k.Rest.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                                if (SourceScan.Identifier().IsMatch(name)) owner.Slots.Add((name, word, i));
                        }
                    }
                    break;
            }
        }
        if (open is { } last) scan.Functions.Add(new KasmFunction(last.Name, last.Line, lines.Length - 1, last.Proto, last.Labels, last.Slots));
        return scan;
    }

    private static string FirstName(string rest)
    {
        Match m = SourceScan.LeadingName().Match(rest);
        return m.Success ? m.Value : "";
    }
}

/// <summary>Light scans of kC and kASM text.</summary>
internal static partial class SourceScan
{
    /// <summary>
    /// The type a kC document declares <paramref name="name"/> with, among <paramref name="known"/>
    /// types (<c>kfs_msg_flight_data_t fd;</c>, <c>const vec3 *v</c>, a parameter <c>(quat q)</c>), or
    /// null. The last declaration before <paramref name="beforeOffset"/> wins, else the first after.
    /// </summary>
    public static string? DeclaredType(string text, string name, Func<string, bool> known, int beforeOffset)
    {
        var pattern = new Regex(@"\b(?<type>[A-Za-z_]\w*)\s*(?:\*\s*|const\s+)*\b" + Regex.Escape(name) + @"\s*(?:\[[^\]]*\]\s*)*(?=[;,=)\[])");
        string? before = null, after = null;
        foreach (Match m in pattern.Matches(text))
        {
            string type = m.Groups["type"].Value;
            if (!known(type)) continue;
            if (m.Index < beforeOffset) before = type;
            else after ??= type;
        }
        return before ?? after;
    }

    /// <summary>The <c>#pragma kfs tick_hz N</c> of a kC text, or null.</summary>
    public static int? TickHz(string text) =>
        TickPragma().Match(text) is { Success: true } m && int.TryParse(m.Groups[1].Value, out int hz) ? hz : null;

    /// <summary>The kASM <c>.tick_hz N</c> of a text, or null.</summary>
    public static int? KasmTickHz(string text) =>
        KasmTick().Match(text) is { Success: true } m && int.TryParse(m.Groups[1].Value, out int hz) ? hz : null;

    /// <summary>The names of <c>void f(void)</c> functions a kC text declares or defines (task and init candidates).</summary>
    public static IEnumerable<string> VoidFunctions(string text) =>
        VoidVoid().Matches(text).Select(m => m.Groups["name"].Value).Distinct(StringComparer.Ordinal);

    /// <summary>
    /// True when <paramref name="before"/> (a kC line up to the cursor) ends inside a string or
    /// character literal or a <c>//</c> comment — or inside a <c>/* … */</c> comment begun on this line.
    /// </summary>
    public static bool InCommentOrString(string before)
    {
        char quote = '\0';
        for (int i = 0; i < before.Length; i++)
        {
            char c = before[i];
            if (quote != '\0')
            {
                if (c == '\\') i++;
                else if (c == quote) quote = '\0';
                continue;
            }
            if (c is '"' or '\'') quote = c;
            else if (c == '/' && i + 1 < before.Length && before[i + 1] == '/') return true;
            else if (c == '/' && i + 1 < before.Length && before[i + 1] == '*')
            {
                int end = before.IndexOf("*/", i + 2, StringComparison.Ordinal);
                if (end < 0) return true;
                i = end + 1;
            }
        }
        return quote != '\0';
    }

    public static bool IsIdentifierChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    [GeneratedRegex(@"^\s*(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*:(?!\S)")]
    internal static partial Regex KasmLabel();

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_.]*")]
    internal static partial Regex LeadingName();

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_.]*$")]
    internal static partial Regex Identifier();

    [GeneratedRegex(@"^\s*#\s*pragma\s+kfs\s+tick_hz\s+(\d+)", RegexOptions.Multiline)]
    private static partial Regex TickPragma();

    [GeneratedRegex(@"^\s*\.tick_hz\s+(\d+)", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex KasmTick();

    [GeneratedRegex(@"\bvoid\s+(?<name>[A-Za-z_]\w*)\s*\(\s*void\s*\)")]
    private static partial Regex VoidVoid();
}
