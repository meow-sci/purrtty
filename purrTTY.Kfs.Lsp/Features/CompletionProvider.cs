using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using purrTTY.Kfs.Lsp.Index;
using purrTTY.Kfs.Lsp.Protocol;
using purrTTY.Kfs.Runtime;

namespace purrTTY.Kfs.Lsp.Features;

/// <summary>
/// <c>textDocument/completion</c> (plan §6.5). The context is decided by scanning the current line
/// up to the cursor; no parser runs.
/// <list type="bullet">
/// <item>kC: after <c>#</c> the preprocessor directives; after <c>#pragma</c> <c>kfs</c>/<c>once</c>;
/// after <c>#pragma kfs</c> its directives, then their arguments (tick rates; <c>void f(void)</c>
/// functions and the task options; rates that divide the program's tick rate after <c>rate_hz=</c>;
/// <c>0</c>/<c>1</c>, <c>on</c>/<c>off</c>, <c>MSG_*</c>, <c>ALLOCATION</c>); inside
/// <c>#include &lt;</c> the system headers (<c>purrtty.h</c> and the target's <c>vehicle.h</c> among
/// them); inside <c>#include "</c> the project's headers (and <c>X.kasm.h</c> for its kASM files);
/// after <c>.</c> / <c>-&gt;</c> the members of <c>commands</c>, <c>sensors</c>, <c>sys</c> or of a
/// variable the document declares with a known struct type (message structs, their element types,
/// the vector types), nothing when the type is not known; otherwise identifiers — services, library
/// prototypes, header and <c>&lt;vehicle.h&gt;</c> names, the accepted keywords and the project's own
/// functions and globals from the last successful build.</item>
/// <item>kASM: at the word position mnemonics, directives and the file's macros; in operand position
/// labels (jumps, <c>.loop_bound</c>), functions (<c>CALL</c>, entry directives), <c>SVC_*</c> first
/// after <c>SYSCALL</c>, else slots, <c>.equ</c>s, strings, data and every built-in-include and
/// <c>purrtty.kinc</c> symbol; types inside <c>sizeof(</c>/<c>alignof(</c>/<c>offsetof(</c>;
/// directive arguments (<c>.include</c> targets, tick rates, task options, messages).</item>
/// </list>
/// Nothing in a comment or a string (except an include name). A request opened by a trigger
/// character that does not start one of these contexts (a <c>.</c> in <c>1.</c>, <c>&lt;</c> in
/// <c>a &lt; b</c>) gets an empty list without touching the indexes' identifier lists.
/// </summary>
/// <remarks>
/// Trigger characters: <c>.</c> (members, kASM directives and dotted symbols), <c>&gt;</c> (only as
/// the end of <c>-&gt;</c>), <c>#</c> (directives), <c>&lt;</c> and <c>"</c> (include names). Not the
/// space: it would open a list after every word of kC; identifiers complete as the client asks while
/// typing. kASM <c>.include &lt;kfs.kinc&gt;</c> symbols are offered whether or not the include is
/// there; without it the item says which include it needs and sorts after the reachable ones. Runs on
/// the read thread; never waits for a build.
/// </remarks>
public sealed partial class CompletionProvider : ICompletionProvider
{
    /// <summary>The trigger characters (see the remarks).</summary>
    public static IReadOnlyList<string> Triggers { get; } = [".", ">", "#", "<", "\""];

    /// <inheritdoc/>
    public IReadOnlyList<string> TriggerCharacters => Triggers;

    /// <summary>A test seam: called first in every request.</summary>
    internal Action<LspRequestContext>? Fault { get; set; }

    /// <inheritdoc/>
    public Task<JsonNode?> Complete(LspRequestContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        Fault?.Invoke(context);
        if (PositionRequest.From(context) is not { } request) return Task.FromResult<JsonNode?>(null);
        string? trigger = LspJson.Int(LspJson.Get(context.Params, "context", "triggerKind")) == 2
            ? LspJson.String(LspJson.Get(context.Params, "context", "triggerCharacter"))
            : null;
        KfsIndex index = KfsIndexCache.For(context.Server.Host);
        CompletionList? list = request.Language == SourceLanguage.Kasm ? Kasm(request, index, trigger) : Kc(request, index, trigger);
        return Task.FromResult<JsonNode?>((list?.ToJson() ?? CompletionList.Empty()));
    }

    // ------------------------------------------------------------------ kC

    private static CompletionList? Kc(PositionRequest request, KfsIndex index, string? trigger)
    {
        string before = request.Before;

        if (IncludeOpen().Match(before) is { Success: true } include)
        {
            var list = new CompletionList(request, include.Groups["p"].Index);
            if (include.Groups["open"].Value == "<")
            {
                bool closed = list.After.Contains('>');
                foreach (HeaderInfo h in index.SystemHeaders) list.Add(h.Entry, h.Name + (closed ? "" : ">"));
            }
            else
            {
                ProjectFiles(request, list, kasm: false);
            }
            return list;
        }
        if (SourceScan.InCommentOrString(before)) return null;

        if (PragmaKfsOpen().Match(before) is { Success: true } pragma)
        {
            var list = new CompletionList(request, pragma.Groups["p"].Index);
            foreach (IndexEntry p in index.Tables.Pragmas.Values) list.Add(p, p.Name + " ");
            return list;
        }
        if (PragmaArgs().Match(before) is { Success: true } args) return PragmaArguments(request, index, args);
        if (PragmaOpen().Match(before) is { Success: true } bare)
        {
            var list = new CompletionList(request, bare.Groups["p"].Index);
            list.AddValue("kfs", "#pragma kfs …", "kFS program structure: `tick_hz`, `task`, `init`, `optimize`, `bounds`, `publish`, `subscribe`, `claim`.", EntryKind.Keyword, "kfs ", 0);
            list.AddValue("once", "#pragma once", "Include this file at most once per translation unit.", EntryKind.Keyword);
            return list;
        }
        if (DirectiveOpen().Match(before) is { Success: true } directive)
        {
            var list = new CompletionList(request, directive.Groups["p"].Index);
            foreach (IndexEntry d in index.Tables.PreprocessorDirectives.Values) list.Add(d, d.Name is "else" or "endif" ? d.Name : d.Name + " ");
            return list;
        }

        int start = request.Cursor;
        while (start > 0 && SourceScan.IsIdentifierChar(before[start - 1])) start--;
        if (start < request.Cursor && char.IsDigit(before[start])) return null;

        // Members.
        int op = start;
        while (op > 0 && char.IsWhiteSpace(before[op - 1])) op--;
        bool dot = op >= 1 && before[op - 1] == '.' && !(op >= 2 && char.IsDigit(before[op - 2]));
        bool arrow = op >= 2 && before[op - 1] == '>' && before[op - 2] == '-';
        if (dot || arrow)
        {
            if (Words.Chain(before, op) is not { } chain) return null;
            if (Words.Resolve(HoverProvider.RootShape(request, index, chain.Root), chain.Members) is not { } shape) return null;
            var list = new CompletionList(request, start);
            foreach (ShapeMember m in shape.Members) list.Add(m.Entry);
            return list;
        }
        if (trigger is not null) return null;
        if (before.TrimStart().StartsWith('#') && !DefineOrCondition().IsMatch(before)) return null;

        var identifiers = new CompletionList(request, start);
        ProjectSymbols project = ProjectSymbols.For(request.Server, request.Document.Path);
        foreach (IndexEntry e in project.Names.Values) identifiers.Add(e, call: e.Kind == EntryKind.ProjectFunction);
        foreach (IndexEntry e in index.KcIdentifiers) identifiers.Add(e, call: e.Kind is EntryKind.Service or EntryKind.Function);
        return identifiers;
    }

    private static CompletionList PragmaArguments(PositionRequest request, KfsIndex index, Match args)
    {
        string directive = args.Groups["d"].Value;
        string text = args.Groups["args"].Value;
        int start = request.Cursor;
        while (start > 0 && SourceScan.IsIdentifierChar(request.LineText[start - 1])) start--;
        var list = new CompletionList(request, start);
        string[] done = text[..(start - args.Groups["args"].Index)].Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        bool afterEquals = start > 0 && request.LineText[start - 1] == '=';
        switch (directive)
        {
            case "tick_hz" when done.Length == 0:
                TickRates(list, index, null);
                break;
            case "task" or "init":
                if (afterEquals)
                {
                    if (done.Length > 0 && done[^1].EndsWith("rate_hz=", StringComparison.Ordinal))
                        TickRates(list, index, SourceScan.TickHz(request.Document.Text) ?? index.Tables.DefaultTickHz);
                }
                else if (done.Length == 0)
                {
                    TaskFunctions(list, request, SourceScan.VoidFunctions(request.Document.Text));
                }
                else
                {
                    foreach (IndexEntry o in index.Tables.TaskOptions)
                    {
                        if (directive == "init" && o.Name != "priority") continue;
                        if (done.Any(d => d.StartsWith(o.Name + "=", StringComparison.Ordinal))) continue;
                        list.Add(o);
                    }
                }
                break;
            case "optimize" when done.Length == 0:
                list.AddValue("0", "optimizer off", "No optimisation for this translation unit.");
                list.AddValue("1", "optimizer on", "Optimise this translation unit.");
                break;
            case "bounds" when done.Length == 0:
                list.AddValue("on", "bounds checks on", "Array indices are checked (`BOUNDS` trap).");
                list.AddValue("off", "bounds checks off", "No array bounds checks.");
                break;
            case "publish" or "subscribe" when done.Length == 0:
                foreach (string message in index.Target.MessageTypes.Keys)
                {
                    if (index.KcNames.TryGetValue(message, out IndexEntry? e)) list.Add(e, message + " ");
                }
                break;
            case "publish" or "subscribe" when done.Length == 1:
                if (index.Target.MessageTypes.TryGetValue(done[0], out string? type))
                {
                    foreach (Match m in new Regex(@"\b" + Regex.Escape(type) + @"\s+(?<name>[A-Za-z_]\w*)\s*[;=]").Matches(request.Document.Text))
                        list.AddValue(m.Groups["name"].Value, type, $"A file-scope `{type}` of this file.", EntryKind.Variable);
                }
                break;
            case "claim" when done.Length == 0:
                list.AddValue("ALLOCATION", "the only claim", index.Tables.Pragmas["claim"].Documentation, EntryKind.Constant);
                break;
        }
        return list;
    }

    private static void TaskFunctions(CompletionList list, PositionRequest request, IEnumerable<string> names)
    {
        ProjectSymbols project = ProjectSymbols.For(request.Server, request.Document.Path);
        foreach (string name in names)
        {
            if (project.Names.TryGetValue(name, out IndexEntry? e)) list.Add(e, name + " ");
            else list.AddValue(name, "void " + name + "(void)", "A `void f(void)` function of this file.", EntryKind.ProjectFunction, name + " ", 0);
        }
    }

    private static void TickRates(CompletionList list, KfsIndex index, int? divides)
    {
        foreach (int hz in index.Tables.TickRates)
        {
            if (divides is { } tick && (hz > tick || tick % hz != 0)) continue;
            string text = hz.ToString(CultureInfo.InvariantCulture);
            list.AddValue(text, divides is null ? $"{text} Hz" : $"{text} Hz (divides {divides} Hz)",
                divides is null ? $"An allowed tick rate (default {index.Tables.DefaultTickHz} Hz)." : $"A task rate that divides the program's tick rate of {divides} Hz.",
                group: hz == index.Tables.DefaultTickHz ? 0 : 1);
        }
        if (divides is { } t && t > 0)
        {
            // Rates below the slowest tick rate that still divide it.
            for (int hz = 1; hz < index.Tables.TickRates.Min(); hz++)
            {
                if (t % hz == 0) list.AddValue(hz.ToString(CultureInfo.InvariantCulture), $"{hz} Hz (divides {t} Hz)", $"A task rate that divides the program's tick rate of {t} Hz.", group: 2);
            }
        }
    }

    /// <summary>
    /// The project files an include can name: a document in a folder sees the files directly in that
    /// folder (bare names), a root-level one the root's files — on disk plus the open buffers, never
    /// itself, never <c>vehicle.h</c> / <c>vehicle.kinc</c>. kC: headers and <c>X.kasm.h</c> for each
    /// <c>X.kasm</c>; kASM: <c>.kinc</c> and <c>.kasm</c> files.
    /// </summary>
    private static void ProjectFiles(PositionRequest request, CompletionList list, bool kasm)
    {
        if (request.Document.Path is not { } path) return;
        string folder = ProjectResolver.FolderOf(path);
        var names = new SortedSet<string>(StringComparer.Ordinal);
        try
        {
            if (new DiskWorkspaceFiles(request.Server.Host.WorkspaceRoot).FilesIn(folder) is { } onDisk) names.UnionWith(onDisk);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // An unreadable folder: the open buffers still count.
        }
        foreach (string open in request.Server.Documents.Overlay().Keys)
        {
            if (ProjectResolver.FolderOf(open) == folder) names.Add(ProjectResolver.NameOf(open));
        }
        names.Remove(ProjectResolver.NameOf(path));
        bool closed = list.After.Contains('"');
        foreach (string name in names)
        {
            if (!ProjectResolver.IsProjectSource(name)) continue;
            string extension = Path.GetExtension(name).ToLowerInvariant();
            if (kasm)
            {
                if (extension is ".kinc" or ".kasm") list.AddValue(name, "project file", $"`{(folder.Length == 0 ? name : folder + "/" + name)}`", EntryKind.File, name + (closed ? "" : "\""), extension == ".kinc" ? 0 : 1);
            }
            else if (extension == ".h")
            {
                list.AddValue(name, "project header", $"`{(folder.Length == 0 ? name : folder + "/" + name)}`", EntryKind.File, name + (closed ? "" : "\""), 0);
            }
            else if (extension == ".kasm")
            {
                list.AddValue(name + ".h", "generated from " + name, $"The prototypes of `{name}`'s `.proto` lines, generated by the compiler.", EntryKind.File, name + ".h" + (closed ? "" : "\""), 1);
            }
        }
    }

    // ------------------------------------------------------------------ kASM

    private static readonly HashSet<string> Jumps = new(StringComparer.OrdinalIgnoreCase) { "JMP", "JZ", "JNZ" };
    private static readonly HashSet<string> Calls = new(StringComparer.OrdinalIgnoreCase) { "CALL", "CALLF" };

    private static CompletionList? Kasm(PositionRequest request, KfsIndex index, string? trigger)
    {
        string before = request.Before;
        KasmLine k = KasmLine.Parse(before);
        if (k.CommentStart >= 0) return null;
        if (trigger is "#" or ">") return null;

        int start = request.Cursor;
        while (start > 0 && (SourceScan.IsIdentifierChar(before[start - 1]) || before[start - 1] == '.')) start--;

        // The word position.
        if (k.Rest.Length == 0 && (k.Word.Length == 0 || k.WordStart + k.Word.Length == before.Length))
        {
            start = k.Word.Length == 0 ? request.Cursor : k.WordStart;
            if (trigger is "<" or "\"") return null;
            var list = new CompletionList(request, start);
            bool directive = list.Prefix.StartsWith('.');
            foreach (IndexEntry d in index.Tables.KasmDirectives.Values) list.Add(d, d.Name + (d.Name.StartsWith(".end", StringComparison.Ordinal) ? "" : " "), group: directive ? 1 : 7);
            if (directive) return list;
            KasmScan scan = KasmScan.Of(request.Document.Text);
            foreach ((string name, int line) in scan.Macros)
                list.Add(new IndexEntry(name, EntryKind.LocalSymbol, "macro", $".macro {name}", $"A macro of this file (line {line + 1}).", "kasm"), name + " ", 2);
            foreach (IndexEntry op in index.Tables.Opcodes.Values) list.Add(op, op.Name + (op.Signature!.Contains(' ') ? " " : ""), group: 3);
            return list;
        }

        string word = k.Word;
        string operands = before[Math.Min(k.RestStart, before.Length)..];

        if (word.Equals(".include", StringComparison.OrdinalIgnoreCase))
        {
            if (Regex.Match(operands, @"^\s*""(?<p>[^""]*)$") is { Success: true } quoted)
            {
                var files = new CompletionList(request, k.RestStart + quoted.Groups["p"].Index);
                ProjectFiles(request, files, kasm: true);
                return files;
            }
            if (Regex.Match(operands, @"^\s*<(?<p>[\w.]*)$") is { Success: true } angled)
            {
                var builtin = new CompletionList(request, k.RestStart + angled.Groups["p"].Index);
                bool closed = builtin.After.Contains('>');
                builtin.AddValue("kfs.kinc", "<kfs.kinc>", "`SVC_*`, `sys.*`, `TRAP_*`, the catalogue enumerations and the vector types.", EntryKind.Header, "kfs.kinc" + (closed ? "" : ">"), 0);
                builtin.AddValue("vehicle.kinc", "<vehicle.kinc>", $"The layout's symbols for {index.Target.TargetName}: `commands.*`, `sensors.*`, `*_COUNT`, `MSG_*`, the message types.", EntryKind.Header, "vehicle.kinc" + (closed ? "" : ">"), 0);
                return builtin;
            }
            return null;
        }
        if (trigger is "<" or "\"") return null;
        if (start < request.Cursor && char.IsDigit(before[start])) return null;
        var operandList = new CompletionList(request, start);
        string[] done = before[Math.Min(k.RestStart, start)..start].Split([' ', '\t', ','], StringSplitOptions.RemoveEmptyEntries);
        KasmScan doc = KasmScan.Of(request.Document.Text);
        KasmFunction? function = doc.FunctionAt(request.Line);
        string lower = word.ToLowerInvariant();

        switch (lower)
        {
            case ".tick_hz":
                if (done.Length == 0) TickRates(operandList, index, null);
                return operandList;
            case ".task" or ".init" or ".program":
                bool afterEquals = start > 0 && before[start - 1] == '=';
                if (afterEquals)
                {
                    if (done.Length > 0 && done[^1].EndsWith("rate_hz=", StringComparison.Ordinal))
                        TickRates(operandList, index, SourceScan.KasmTickHz(request.Document.Text) ?? index.Tables.DefaultTickHz);
                }
                else if (done.Length == 0)
                {
                    foreach (KasmFunction f in doc.Functions)
                        operandList.AddValue(f.Name, ".func " + f.Name, $"A function of this file (line {f.Line + 1}).", EntryKind.ProjectFunction, f.Name + " ", 0);
                }
                else if (lower == ".task")
                {
                    foreach (IndexEntry o in index.Tables.TaskOptions)
                        if (!done.Any(d => d.StartsWith(o.Name + "=", StringComparison.Ordinal))) operandList.Add(o);
                }
                else if (lower == ".program")
                {
                    operandList.AddValue("share=", "share=S", "The program task's share of the tick, 1–100 percent.", newText: "share=");
                }
                return operandList;
            case ".publish" or ".subscribe":
                if (done.Length == 0)
                {
                    foreach (string message in index.Target.MessageTypes.Keys)
                        if (index.KasmNames.TryGetValue(message, out IndexEntry? e)) operandList.Add(e, message + " ");
                }
                return operandList;
            case ".claim":
                if (done.Length == 0) operandList.AddValue("ALLOCATION", "the only claim", index.Tables.KasmDirectives[".claim"].Documentation, EntryKind.Constant);
                return operandList;
            case ".loop_bound":
                if (done.Length == 0) Labels(operandList, request, function);
                return operandList;
        }
        if (Jumps.Contains(word))
        {
            Labels(operandList, request, function);
            return operandList;
        }
        if (Calls.Contains(word) || lower == ".extern")
        {
            foreach (KasmFunction f in doc.Functions)
                operandList.AddValue(f.Name, f.Proto ?? ".func " + f.Name, $"A function of this file (line {f.Line + 1}).", EntryKind.ProjectFunction, group: 0);
            foreach (string name in doc.Externs) operandList.AddValue(name, ".extern", "Declared `.extern` in this file.", EntryKind.ProjectFunction, group: 1);
            foreach (IndexEntry e in ProjectSymbols.For(request.Server, request.Document.Path).Names.Values)
                if (e.Kind == EntryKind.ProjectFunction) operandList.Add(e, group: 2);
            return operandList;
        }
        if (OpenTypeOperator().IsMatch(before[..start]))
        {
            foreach (IndexEntry t in index.KasmTypes.Values) operandList.Add(t);
            return operandList;
        }

        // Expression symbols, in the assembler's lookup order.
        HashSet<string> includes = Includes(request, doc);
        if (function is not null)
        {
            foreach (var slot in function.Slots)
                operandList.Add(new IndexEntry(slot.Name, EntryKind.LocalSymbol, slot.Directive + " slot", $"{slot.Directive} {slot.Name}", $"A frame slot of `{function.Name}`: its frame offset.", "kasm"), group: 0);
        }
        bool syscall = word.Equals("SYSCALL", StringComparison.OrdinalIgnoreCase);
        foreach ((string name, (int line, string value)) in doc.Equs)
            operandList.Add(new IndexEntry(name, EntryKind.LocalSymbol, "= " + value, $".equ {name}, {value}", $"A constant of this file (line {line + 1}).", "kasm"), group: syscall ? 2 : 1);
        foreach ((string name, (int line, string directive, string text)) in doc.Data)
            operandList.Add(new IndexEntry(name, EntryKind.LocalSymbol, directive, text, $"Defined on line {line + 1} of this file.", "kasm"), group: 2);
        foreach (IndexEntry e in index.KasmNames.Values)
        {
            bool reachable = e.Origin is null || includes.Contains(e.Origin);
            bool service = e.Name.StartsWith("SVC_", StringComparison.Ordinal);
            int group = (syscall ? service ? 0 : 4 : e.SortGroup + 1) + (reachable ? 0 : 10);
            string? note = reachable ? null : e.Origin!.EndsWith(".kinc", StringComparison.Ordinal) && e.Origin != "purrtty.kinc"
                ? $"(needs .include <{e.Origin}>)"
                : $"(needs .include \"{e.Origin}\")";
            operandList.Add(e, group: group, note: note);
        }
        foreach ((string name, string syntax, string opDoc) in StaticTables.KasmOperators)
            operandList.Add(new IndexEntry(name, EntryKind.Keyword, syntax, syntax, opDoc, "kasm"), name + "(", 8);
        return operandList;
    }

    private static void Labels(CompletionList list, PositionRequest request, KasmFunction? function)
    {
        if (function is null) return;
        foreach ((string name, int line) in function.Labels)
            list.AddValue(name, "label", $"A label of `{function.Name}` (line {line + 1}).", EntryKind.LocalSymbol, group: 0);
        foreach (ProjectLabel label in ProjectSymbols.For(request.Server, request.Document.Path).Labels)
        {
            if (label.Function == function.Name)
                list.AddValue(label.Name, "label (last build)", $"A label of `{function.Name}` in the last successful build ({label.File} line {label.Line}).", EntryKind.LocalSymbol, group: 1);
        }
    }

    /// <summary>The includes of the document, and of every file of its last build (a built-in include counts for the whole assembly).</summary>
    private static HashSet<string> Includes(PositionRequest request, KasmScan doc)
    {
        var includes = new HashSet<string>(doc.Includes, StringComparer.Ordinal);
        if (request.Document.Path is { } path && request.Server.Builds.ResultFor(path) is { } build)
        {
            foreach ((string other, string text) in build.Texts)
            {
                if (other == path) continue;
                foreach (Match m in KasmInclude().Matches(text)) includes.Add(m.Groups[1].Value.Trim());
            }
        }
        return includes;
    }

    [GeneratedRegex(@"^\s*#\s*include\s*(?<open>[<""])(?<p>[^>""]*)$")]
    private static partial Regex IncludeOpen();

    [GeneratedRegex(@"^\s*#\s*pragma\s+kfs\s+(?<p>\w*)$")]
    private static partial Regex PragmaKfsOpen();

    [GeneratedRegex(@"^\s*#\s*pragma\s+kfs\s+(?<d>\w+)\s+(?<args>.*)$")]
    private static partial Regex PragmaArgs();

    [GeneratedRegex(@"^\s*#\s*pragma\s+(?<p>\w*)$")]
    private static partial Regex PragmaOpen();

    [GeneratedRegex(@"^\s*#\s*(?<p>\w*)$")]
    private static partial Regex DirectiveOpen();

    [GeneratedRegex(@"^\s*#\s*(define|undef|if|ifdef|ifndef|elif)\b")]
    private static partial Regex DefineOrCondition();

    [GeneratedRegex(@"\b(sizeof|alignof|offsetof)\s*\(\s*[\w.]*$")]
    private static partial Regex OpenTypeOperator();

    [GeneratedRegex(@"^[ \t]*\.include[ \t]+[<""]([^>""]+)[>""]", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex KasmInclude();
}
