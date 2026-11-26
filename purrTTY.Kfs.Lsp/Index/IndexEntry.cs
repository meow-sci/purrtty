// RULE FOR THIS FILE: no game type.

using System.Text;

namespace purrTTY.Kfs.Lsp.Index;

/// <summary>What an <see cref="IndexEntry"/> names; decides the completion item kind.</summary>
public enum EntryKind
{
    /// <summary>A kFS service (stock or <c>x_</c>): a kC function or a kASM <c>SVC_*</c> symbol.</summary>
    Service,
    /// <summary>A library or builtin prototype of a system header.</summary>
    Function,
    /// <summary>A <c>#define</c> of a system header or of <c>&lt;vehicle.h&gt;</c>.</summary>
    Macro,
    /// <summary>A kASM constant of a built-in include or of <c>purrtty.kinc</c>.</summary>
    Constant,
    /// <summary>A type: a typedef of a header or a kASM type.</summary>
    Type,
    /// <summary>A struct member (<c>sys.*</c>, <c>commands.*</c>, a message field).</summary>
    Field,
    /// <summary>A header object (<c>sys</c>, <c>commands</c>, <c>sensors</c>).</summary>
    Variable,
    /// <summary>A kC keyword.</summary>
    Keyword,
    /// <summary>A preprocessor directive (<c>#include</c> …).</summary>
    Directive,
    /// <summary>A <c>#pragma kfs</c> directive.</summary>
    Pragma,
    /// <summary>A system header (<c>#include &lt;…&gt;</c>).</summary>
    Header,
    /// <summary>A kASM mnemonic.</summary>
    Opcode,
    /// <summary>A kASM directive (<c>.func</c> …).</summary>
    KasmDirective,
    /// <summary>A function of the project (from the last successful build).</summary>
    ProjectFunction,
    /// <summary>A global of the project (from the last successful build).</summary>
    ProjectGlobal,
    /// <summary>A kASM label, <c>.equ</c> constant, slot or macro of the document.</summary>
    LocalSymbol,
    /// <summary>A project file (<c>#include "…"</c>).</summary>
    File,
    /// <summary>A value of a directive argument (a tick rate, <c>ALLOCATION</c>, <c>on</c>).</summary>
    Value,
}

/// <summary>
/// One documented name of an index: immutable, rendered once when the index is built, shared by
/// hover and completion.
/// </summary>
/// <param name="Name">The name as written in source (the completion label).</param>
/// <param name="Kind">What it is.</param>
/// <param name="Detail">One short line (completion <c>detail</c>): a prototype, a value, a type.</param>
/// <param name="Signature">The code block of a hover (a prototype, a definition, a mnemonic with its immediates), or null.</param>
/// <param name="Documentation">Markdown below the signature; may be empty.</param>
/// <param name="SignatureLanguage">The fence language of <paramref name="Signature"/> (<c>c</c>, <c>kasm</c>).</param>
public sealed record IndexEntry(string Name, EntryKind Kind, string Detail, string? Signature, string Documentation, string SignatureLanguage = "c")
{
    private string? _markdown;
    private string? _plain;

    /// <summary>The copy <c>with</c> makes: every property, but not the rendered texts (they depend on what <c>with</c> changes).</summary>
    private IndexEntry(IndexEntry original)
    {
        Name = original.Name;
        Kind = original.Kind;
        Detail = original.Detail;
        Signature = original.Signature;
        Documentation = original.Documentation;
        SignatureLanguage = original.SignatureLanguage;
        Parameters = original.Parameters;
        Origin = original.Origin;
        InsertText = original.InsertText;
        SortGroup = original.SortGroup;
    }

    /// <summary>The parameter names in order (for a call snippet), or null when the entry is no function.</summary>
    public IReadOnlyList<string>? Parameters { get; init; }

    /// <summary>
    /// Where the name comes from: the header (<c>kfs.h</c>, <c>vehicle.h</c>) or kASM include
    /// (<c>kfs.kinc</c>, <c>purrtty.kinc</c>) that defines it, or null when it needs none.
    /// </summary>
    public string? Origin { get; init; }

    /// <summary>The text completion inserts instead of <see cref="Name"/> (a header name with its closing bracket), or null.</summary>
    public string? InsertText { get; init; }

    /// <summary>Sort group (lower first) for completion.</summary>
    public int SortGroup { get; init; } = 5;

    /// <summary>The full hover / documentation text in Markdown: the fenced signature, then the documentation.</summary>
    public string Markdown => _markdown ??= Markup.Join(Signature is null ? null : Markup.Fence(Signature, SignatureLanguage), Documentation);

    /// <summary>The same as plain text, for clients without Markdown.</summary>
    public string PlainText => _plain ??= Markup.ToPlain(Markdown);
}

/// <summary>Small Markdown helpers shared by the indexes and the providers.</summary>
public static class Markup
{
    /// <summary>A fenced code block.</summary>
    public static string Fence(string code, string language) => $"```{language}\n{code.TrimEnd()}\n```";

    /// <summary>Inline code, safe for any text (a longer backtick run when the text holds backticks).</summary>
    public static string Code(string text)
    {
        if (!text.Contains('`')) return "`" + text + "`";
        int run = 1, current = 0;
        foreach (char c in text)
        {
            current = c == '`' ? current + 1 : 0;
            run = Math.Max(run, current + 1);
        }
        string fence = new('`', run);
        return $"{fence} {text} {fence}";
    }

    /// <summary>The non-empty parts, separated by blank lines.</summary>
    public static string Join(params string?[] parts) =>
        string.Join("\n\n", parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()));

    /// <summary>
    /// Markdown to plain text: code fences dropped (their content kept), <c>**</c> and inline backticks
    /// removed. Good enough for the small vocabulary the indexes use.
    /// </summary>
    public static string ToPlain(string markdown)
    {
        var sb = new StringBuilder(markdown.Length);
        foreach (string line in markdown.Split('\n'))
        {
            if (line.StartsWith("```", StringComparison.Ordinal)) continue;
            sb.Append(line.Replace("**", "", StringComparison.Ordinal).Replace("`", "", StringComparison.Ordinal)).Append('\n');
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>A unit for a kFS field name from its suffix (<c>altitude_m</c> → m, <c>rates_body_rps</c> → rad/s), or null.</summary>
    public static string? UnitOf(string fieldName)
    {
        int underscore = fieldName.LastIndexOf('_');
        if (underscore < 0 || underscore == fieldName.Length - 1) return null;
        return Units.GetValueOrDefault(fieldName[(underscore + 1)..]);
    }

    // Suffixes of the message catalogue's field names (messages.json) and the sysblock's.
    private static readonly Dictionary<string, string> Units = new(StringComparer.Ordinal)
    {
        ["m"] = "m",
        ["km"] = "km",
        ["mps"] = "m/s",
        ["mps2"] = "m/s²",
        ["rad"] = "rad",
        ["rps"] = "rad/s",
        ["rps2"] = "rad/s²",
        ["s"] = "s",
        ["us"] = "µs",
        ["kg"] = "kg",
        ["kgps"] = "kg/s",
        ["kgm2"] = "kg·m²",
        ["N"] = "N",
        ["Nm"] = "N·m",
        ["Nms"] = "N·m·s",
        ["Ns"] = "N·s",
        ["pa"] = "Pa",
        ["Pa"] = "Pa",
        ["hz"] = "Hz",
        ["deg"] = "°",
    };
}
