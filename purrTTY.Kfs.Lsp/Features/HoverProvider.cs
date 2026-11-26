using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using purrTTY.Kfs.Lsp.Build;
using purrTTY.Kfs.Lsp.Index;
using purrTTY.Kfs.Lsp.Protocol;

namespace purrTTY.Kfs.Lsp.Features;

/// <summary>
/// <c>textDocument/hover</c> (plan §6.5): the word under the cursor resolved against the indexes —
/// services (with the catalogue's documentation for <c>x_*</c>), library prototypes, constants and
/// macros of the system headers and of <c>&lt;vehicle.h&gt;</c> for the current target, members of
/// <c>sys</c>, <c>commands</c>, <c>sensors</c> and of message-struct variables, <c>#pragma kfs</c>
/// directives, preprocessor directives and system headers in kC; mnemonics (stack effect, cost on the
/// target's part), directives, <c>&lt;kfs.kinc&gt;</c> / <c>&lt;vehicle.kinc&gt;</c> / <c>purrtty.kinc</c>
/// symbols, the document's <c>.equ</c>s, data, slots and labels in kASM; diagnostic codes (the game's
/// manual page) in both; and the project's own functions and globals from the last successful build.
/// A position covered by a diagnostic of the current text gets that diagnostic's manual page too.
/// Anything else — locals, keywords, numbers, a member of an expression whose type is not known
/// cheaply — is <c>null</c>: nothing rather than a guess.
/// </summary>
/// <remarks>
/// Runs on the connection's read thread and never waits: it reads the document snapshot, the cached
/// index (<see cref="KfsIndexCache"/>) and the scheduler's last published results.
/// </remarks>
public sealed partial class HoverProvider : IHoverProvider
{
    /// <summary>A test seam: called first in every request (an exception thrown here must become an error response).</summary>
    internal Action<LspRequestContext>? Fault { get; set; }

    /// <inheritdoc/>
    public Task<JsonNode?> Hover(LspRequestContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        Fault?.Invoke(context);
        if (PositionRequest.From(context) is not { } request) return Task.FromResult<JsonNode?>(null);
        KfsIndex index = KfsIndexCache.For(context.Server.Host);
        (IndexEntry Entry, int Start, int End)? found = request.Language == SourceLanguage.Kasm ? Kasm(request, index) : Kc(request, index);

        // The manual page of a diagnostic of the current text under the cursor.
        (string Markdown, LspRange Range)? diagnostics = DiagnosticsAt(request, index);
        if (found is null && diagnostics is null) return Task.FromResult<JsonNode?>(null);

        bool markdown = request.Markdown("textDocument", "hover", "contentFormat");
        string text = found is { } f ? markdown ? f.Entry.Markdown : f.Entry.PlainText : "";
        if (diagnostics is { } d) text = Markup.Join(text, markdown && text.Length > 0 ? "---" : null, markdown ? d.Markdown : Markup.ToPlain(d.Markdown));
        LspRange range = found is { } w ? request.Range(w.Start, w.End) : diagnostics!.Value.Range;
        return Task.FromResult<JsonNode?>(new JsonObject
        {
            ["contents"] = new JsonObject { ["kind"] = markdown ? "markdown" : "plaintext", ["value"] = text },
            ["range"] = range.ToJson(),
        });
    }

    // ------------------------------------------------------------------ kC

    private static (IndexEntry, int, int)? Kc(PositionRequest request, KfsIndex index)
    {
        string line = request.LineText;
        if (Words.DiagnosticCode(line, request.Cursor) is { } code)
            return index.Tables.Manual(code.Code) is { } page ? (Page(code.Code, page), code.Start, code.End) : null;

        // #include <name>
        if (IncludeLine().Match(line) is { Success: true } include && include.Groups["name"] is { } name
            && request.Cursor >= name.Index && request.Cursor <= name.Index + name.Length)
        {
            if (include.Groups["open"].Value == "<")
                return index.SystemHeaders.FirstOrDefault(h => h.Name == name.Value) is { } header ? (header.Entry, name.Index, name.Index + name.Length) : null;
            return null;
        }

        if (Words.At(line, request.Cursor, SourceLanguage.Kc) is not { } word) return null;
        string text = line[word.Start..word.End];
        if (char.IsDigit(text[0])) return null;

        // Preprocessor directive words.
        if (Directive().Match(line) is { Success: true } directive)
        {
            Group d = directive.Groups["directive"];
            if (word.Start == d.Index)
                return index.Tables.PreprocessorDirectives.TryGetValue(text, out IndexEntry? pp) ? (pp, directive.Groups["hash"].Index, word.End) : null;
            if (d.Value == "pragma" && PragmaKfs().Match(line) is { Success: true } pragma && pragma.Groups["word"].Index == word.Start)
                return index.Tables.Pragmas.TryGetValue(text, out IndexEntry? p) ? (p, word.Start, word.End) : null;
            if (d.Value == "pragma" && text is "kfs" or "once") return null;
        }

        // A member: resolve the chain, or say nothing.
        int before = word.Start;
        while (before > 0 && char.IsWhiteSpace(line[before - 1])) before--;
        if (before >= 1 && (line[before - 1] == '.' && !(before >= 2 && char.IsDigit(line[before - 2])) || before >= 2 && line[before - 1] == '>' && line[before - 2] == '-'))
        {
            if (Words.Chain(line, before) is not { } chain) return null;
            MemberShape? shape = Words.Resolve(RootShape(request, index, chain.Root), chain.Members);
            return shape?.Find(text) is { } member ? (member.Entry, word.Start, word.End) : null;
        }

        ProjectSymbols project = ProjectSymbols.For(request.Server, request.Document.Path);
        if (project.Names.TryGetValue(text, out IndexEntry? own)) return (own, word.Start, word.End);
        if (index.KcNames.TryGetValue(text, out IndexEntry? entry)) return (entry, word.Start, word.End);
        return null;
    }

    /// <summary>The shape of a chain root: a header object (<c>sys</c>, <c>commands</c>, <c>sensors</c>) or a variable the document declares with a known struct type.</summary>
    internal static MemberShape? RootShape(PositionRequest request, KfsIndex index, string root)
    {
        if (index.Root(root) is { } shape) return shape;
        int offset = request.Lines.Start(request.Line);
        return SourceScan.DeclaredType(request.Document.Text, root, t => index.Shape(t) is not null, offset) is { } type ? index.Shape(type) : null;
    }

    // ------------------------------------------------------------------ kASM

    private static (IndexEntry, int, int)? Kasm(PositionRequest request, KfsIndex index)
    {
        string line = request.LineText;
        if (Words.DiagnosticCode(line, request.Cursor) is { } code)
            return index.Tables.Manual(code.Code) is { } page ? (Page(code.Code, page), code.Start, code.End) : null;
        KasmLine k = KasmLine.Parse(line);
        if (k.CommentStart >= 0 && request.Cursor > k.CommentStart) return null;
        if (Words.At(line, request.Cursor, SourceLanguage.Kasm) is not { } word) return null;
        string text = line[word.Start..word.End];
        if (char.IsDigit(text[0])) return null;
        KasmScan scan = KasmScan.Of(request.Document.Text);

        // The label of the line.
        if (k.Label is { } label && word.End <= k.LabelEnd)
            return (LabelEntry(label, scan.FunctionAt(request.Line)?.Name, request.Line), word.Start, word.End);

        // The word slot: directive, mnemonic or macro.
        if (word.Start == k.WordStart)
        {
            if (text.StartsWith('.')) return index.Tables.KasmDirectives.TryGetValue(text, out IndexEntry? d) ? (d, word.Start, word.End) : null;
            if (scan.Macros.TryGetValue(text, out int macroLine))
                return (new IndexEntry(text, EntryKind.LocalSymbol, "macro", $".macro {text}", $"A macro of this file, defined on line {macroLine + 1}.", "kasm"), word.Start, word.End);
            return index.Tables.Opcodes.TryGetValue(text, out IndexEntry? op) ? (op, word.Start, word.End) : null;
        }

        // .include <kfs.kinc>
        if (k.Word.Equals(".include", StringComparison.OrdinalIgnoreCase))
        {
            return text switch
            {
                "kfs.kinc" => (Include("kfs.kinc", "the table constants and types: `SVC_*` service ids, `sys` and `sys.*` addresses, `TRAP_*` codes, the catalogue enumerations, the vector types."), word.Start, word.End),
                "vehicle.kinc" => (Include("vehicle.kinc", $"the layout's constants and types for {index.Target.TargetName}: `commands`/`sensors` and their scalar fields, `THRUSTER_COUNT` …, `MSG_*` ids and the message struct types."), word.Start, word.End),
                _ => null,
            };
        }

        return KasmOperand(request, index, scan, text) is { } entry ? (entry, word.Start, word.End) : null;
    }

    /// <summary>A kASM operand name, in the assembler's lookup order: slot, <c>.equ</c>, string, built-in constant, global; then functions, labels and types.</summary>
    internal static IndexEntry? KasmOperand(PositionRequest request, KfsIndex index, KasmScan scan, string text)
    {
        KasmFunction? function = scan.FunctionAt(request.Line);
        if (function?.Slots.FirstOrDefault(s => s.Name == text) is { Name: not null } slot)
            return new IndexEntry(text, EntryKind.LocalSymbol, slot.Directive, $"{slot.Directive} {text}",
                $"A frame slot of `{function.Name}` (line {slot.Line + 1}); in an operand it is the slot's frame offset.", "kasm");
        if (scan.Equs.TryGetValue(text, out var equ))
            return new IndexEntry(text, EntryKind.LocalSymbol, "= " + equ.Value, $".equ {text}, {equ.Value}", $"A constant of this file (line {equ.Line + 1}).", "kasm");
        if (scan.Data.TryGetValue(text, out var data))
            return new IndexEntry(text, EntryKind.LocalSymbol, data.Directive, data.Text, data.Directive is ".string" or ".format"
                ? $"A {data.Directive[1..]} of this file (line {data.Line + 1}): the name is its string-table index."
                : $"A global of this file (line {data.Line + 1}): the name is its address.", "kasm");
        if (index.KasmNames.TryGetValue(text, out IndexEntry? builtin)) return builtin;
        ProjectSymbols project = ProjectSymbols.For(request.Server, request.Document.Path);
        if (project.Names.TryGetValue(text, out IndexEntry? own)) return own;
        if (scan.Functions.FirstOrDefault(f => f.Name == text) is { } func)
            return new IndexEntry(text, EntryKind.LocalSymbol, ".func", func.Proto ?? $".func {text}", $"A function of this file (line {func.Line + 1}).", func.Proto is null ? "kasm" : "c");
        if (function?.Labels.Any(l => l.Name == text) == true) return LabelEntry(text, function.Name, function.Labels.First(l => l.Name == text).Line);
        if (index.KasmTypes.TryGetValue(text, out IndexEntry? type)) return type;
        if (StaticTables.KasmOperators.FirstOrDefault(o => o.Name == text) is { Name: not null } op)
            return new IndexEntry(op.Name, EntryKind.Keyword, op.Syntax, op.Syntax, op.Doc, "kasm");
        return null;
    }

    private static IndexEntry LabelEntry(string label, string? function, int line) =>
        new(label, EntryKind.LocalSymbol, "label", $"{label}:", function is null
            ? $"A label (line {line + 1})."
            : $"A label of `{function}` (line {line + 1}); labels are local to their function: `JMP`, `JZ`, `JNZ`, `.loop_bound`.", "kasm");

    private static IndexEntry Include(string name, string what) =>
        new(name, EntryKind.Header, name, $".include <{name}>", $"A built-in include: it adds no text but {what} It takes effect for the whole assembly.", "kasm");

    private static IndexEntry Page(string code, string page) => new(code, EntryKind.Value, code, null, page);

    // ------------------------------------------------------------------ diagnostics under the cursor

    private static (string, LspRange)? DiagnosticsAt(PositionRequest request, KfsIndex index)
    {
        if (request.Document.Path is not { } path || request.Server.Builds.ResultFor(path) is not { } build) return null;
        if (!build.Texts.TryGetValue(path, out string? built) || built != request.Document.Text) return null; // positions of another text
        Dictionary<string, List<LspDiagnostic>> mapped = DiagnosticMapper.Map(build, request.Encoding, request.Server.Documents.UriOf);
        if (!mapped.TryGetValue(path, out List<LspDiagnostic>? diagnostics)) return null;
        var pages = new List<string>();
        LspRange? range = null;
        foreach (LspDiagnostic d in diagnostics)
        {
            if (!Contains(d.Range, request.Position)) continue;
            if (index.Tables.Manual(d.Code) is not { } page || pages.Contains(page)) continue;
            pages.Add(page);
            range ??= d.Range;
        }
        return range is { } r ? (string.Join("\n\n", pages), r) : null;
    }

    private static bool Contains(LspRange range, LspPosition p) =>
        (p.Line > range.Start.Line || p.Line == range.Start.Line && p.Character >= range.Start.Character)
        && (p.Line < range.End.Line || p.Line == range.End.Line && p.Character < range.End.Character);

    [GeneratedRegex(@"^\s*#\s*include\s*(?<open>[<""])(?<name>[^>""]*)")]
    internal static partial Regex IncludeLine();

    [GeneratedRegex(@"^\s*(?<hash>#)\s*(?<directive>[A-Za-z_]\w*)")]
    internal static partial Regex Directive();

    [GeneratedRegex(@"^\s*#\s*pragma\s+kfs\s+(?<word>[A-Za-z_]\w*)")]
    internal static partial Regex PragmaKfs();
}
