// RULE FOR THIS FILE: no game type.

using System.Text;
using System.Text.Json.Nodes;
using purrTTY.Kfs.Lsp.Index;
using purrTTY.Kfs.Lsp.Protocol;

namespace purrTTY.Kfs.Lsp.Features;

/// <summary>
/// The items of one completion response. Every item replaces exactly the typed prefix
/// (<see cref="PrefixStart"/> to the cursor) through a <c>textEdit</c> in the negotiated encoding.
/// Items are filtered by the prefix as a case-insensitive subsequence of the label: a superset of
/// what any client's fuzzy filter keeps, and typing more only narrows it, so the list is complete
/// (<c>isIncomplete: false</c>).
/// </summary>
internal sealed class CompletionList
{
    private readonly PositionRequest _request;
    private readonly JsonObject _range;
    private readonly bool _markdown;
    private readonly HashSet<string> _labels = new(StringComparer.Ordinal);
    private readonly List<JsonObject> _items = [];

    public CompletionList(PositionRequest request, int prefixStart)
    {
        _request = request;
        PrefixStart = prefixStart;
        Prefix = request.LineText[prefixStart..request.Cursor];
        _range = request.Range(prefixStart, request.Cursor).ToJson();
        _markdown = request.Markdown("textDocument", "completion", "completionItem", "documentationFormat");
        Snippets = request.Snippets;
    }

    public int PrefixStart { get; }
    public string Prefix { get; }
    public bool Snippets { get; }
    public int Count => _items.Count;

    /// <summary>The text after the cursor on the line.</summary>
    public string After => _request.LineText[_request.Cursor..];

    /// <summary>
    /// Adds <paramref name="entry"/> unless its label is taken or does not match the prefix.
    /// <paramref name="newText"/> replaces the entry's insert text; <paramref name="group"/> its sort
    /// group; <paramref name="note"/> is appended to the detail; <paramref name="call"/> makes a call
    /// snippet when the client takes snippets and no <c>(</c> follows the cursor.
    /// </summary>
    public void Add(IndexEntry entry, string? newText = null, int? group = null, string? note = null, bool call = false, string? label = null)
    {
        label ??= entry.Name;
        if (!Matches(label) || !_labels.Add(label)) return;
        string text = newText ?? entry.InsertText ?? entry.Name;
        int format = 1;
        if (call && Snippets && entry.Parameters is { } parameters && !After.TrimStart().StartsWith('('))
        {
            text = Escape(text) + "(" + string.Join(", ", parameters.Select((p, i) => $"${{{i + 1}:{Escape(p)}}}")) + ")$0";
            format = 2;
        }
        var item = new JsonObject
        {
            ["label"] = label,
            ["kind"] = Kind(entry.Kind),
            ["detail"] = note is null ? entry.Detail : $"{entry.Detail}  {note}",
            ["sortText"] = $"{group ?? entry.SortGroup:D2}{label}",
            ["filterText"] = label,
            ["textEdit"] = new JsonObject { ["range"] = _range.DeepClone(), ["newText"] = text },
            ["insertTextFormat"] = format,
        };
        string documentation = _markdown ? entry.Markdown : entry.PlainText;
        if (documentation.Length > 0) item["documentation"] = new JsonObject { ["kind"] = _markdown ? "markdown" : "plaintext", ["value"] = documentation };
        _items.Add(item);
    }

    /// <summary>Adds a plain value (a tick rate, <c>ALLOCATION</c>, a file).</summary>
    public void AddValue(string label, string detail, string documentation, EntryKind kind = EntryKind.Value, string? newText = null, int group = 1) =>
        Add(new IndexEntry(label, kind, detail, null, documentation), newText, group);

    public JsonObject ToJson() => new()
    {
        ["isIncomplete"] = false,
        ["items"] = new JsonArray(_items.Select(i => (JsonNode)i).ToArray()),
    };

    public static JsonObject Empty() => new() { ["isIncomplete"] = false, ["items"] = new JsonArray() };

    /// <summary>True when <see cref="Prefix"/> is a case-insensitive subsequence of <paramref name="label"/>.</summary>
    public bool Matches(string label)
    {
        int j = 0;
        foreach (char c in label)
        {
            if (j == Prefix.Length) break;
            if (char.ToLowerInvariant(c) == char.ToLowerInvariant(Prefix[j])) j++;
        }
        return j == Prefix.Length;
    }

    private static string Escape(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char c in s)
        {
            if (c is '$' or '}' or '\\') sb.Append('\\');
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>LSP <c>CompletionItemKind</c>.</summary>
    private static int Kind(EntryKind kind) => kind switch
    {
        EntryKind.Service or EntryKind.Function or EntryKind.ProjectFunction => 3,
        EntryKind.Field => 5,
        EntryKind.Variable or EntryKind.ProjectGlobal or EntryKind.LocalSymbol => 6,
        EntryKind.Header => 9,
        EntryKind.Value => 12,
        EntryKind.Keyword or EntryKind.Directive or EntryKind.Pragma or EntryKind.KasmDirective => 14,
        EntryKind.File => 17,
        EntryKind.Macro or EntryKind.Constant => 21,
        EntryKind.Type => 22,
        EntryKind.Opcode => 24,
        _ => 1,
    };
}
