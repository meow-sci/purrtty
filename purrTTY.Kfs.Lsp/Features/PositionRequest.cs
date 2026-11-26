// RULE FOR THIS FILE: no game type.

using System.Text.Json.Nodes;
using purrTTY.Kfs.Lsp.Documents;
using purrTTY.Kfs.Lsp.Protocol;

namespace purrTTY.Kfs.Lsp.Features;

/// <summary>Which language a document is: by extension (<c>.kasm</c>/<c>.kinc</c> vs <c>.c</c>/<c>.h</c>), else by language id.</summary>
public enum SourceLanguage
{
    /// <summary>kC (<c>.c</c>, <c>.h</c>, and anything not kASM).</summary>
    Kc,
    /// <summary>kASM (<c>.kasm</c>, <c>.kinc</c>, language id <c>kasm</c>).</summary>
    Kasm,
}

/// <summary>
/// A <c>textDocument/hover</c> or <c>textDocument/completion</c> request resolved against the open
/// document: the snapshot of its text (immutable, so a concurrent change cannot tear it), the line,
/// and the cursor as a UTF-16 index into the line. <see cref="From"/> returns null — never throws —
/// for a document that is not open, missing parameters, or a line past the end of the text; a
/// character past the end of its line is the end of the line, as LSP prescribes.
/// </summary>
internal sealed class PositionRequest
{
    private PositionRequest(LspRequestContext context, LspDocument document, int line, string text, int cursor, LspPosition position)
    {
        Context = context;
        Document = document;
        Line = line;
        LineText = text;
        Cursor = cursor;
        Position = position;
        Language = LanguageOf(document);
    }

    public LspRequestContext Context { get; }
    public KfsLanguageServer Server => Context.Server;
    public LspDocument Document { get; }
    public LineIndex Lines => Document.Lines;
    public int Line { get; }
    public string LineText { get; }

    /// <summary>The cursor: a UTF-16 index into <see cref="LineText"/> (0 … length).</summary>
    public int Cursor { get; }

    public LspPosition Position { get; }
    public SourceLanguage Language { get; }
    public PositionEncoding Encoding => Server.Encoding;

    /// <summary>The text of the line before the cursor.</summary>
    public string Before => LineText[..Cursor];

    public static PositionRequest? From(LspRequestContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (LspJson.DocumentUri(context.Params) is not { } uri) return null;
        if (LspPosition.From(LspJson.Get(context.Params, "position")) is not { } position) return null;
        if (context.Server.Documents.Get(uri) is not { } document) return null;
        LineIndex lines = document.Lines;
        if (position.Line < 0 || position.Line >= lines.Count || position.Character < 0) return null;
        string text = lines.Line(position.Line);
        PositionEncoding encoding = context.Server.Encoding;
        // LSP: "If the character value is greater than the line length it defaults back to the line length."
        int end = PositionMapper.ToCharacter(text, text.Length, encoding);
        if (position.Character > end) position = position with { Character = end };
        int cursor = PositionMapper.ToUtf16(text, position.Character, encoding);
        return new PositionRequest(context, document, position.Line, text, cursor, position);
    }

    /// <summary>The LSP range of UTF-16 columns <paramref name="start"/>–<paramref name="end"/> of this line.</summary>
    public LspRange Range(int start, int end) => PositionMapper.Range(Lines, Line, start, end, Encoding);

    public static SourceLanguage LanguageOf(LspDocument document)
    {
        string name = document.Path ?? document.Uri;
        int query = name.IndexOfAny(['?', '#']);
        if (query >= 0 && document.Path is null) name = name[..query];
        string extension = Path.GetExtension(name).ToLowerInvariant();
        if (extension is ".kasm" or ".kinc") return SourceLanguage.Kasm;
        if (extension is ".c" or ".h") return SourceLanguage.Kc;
        return document.LanguageId.Equals("kasm", StringComparison.OrdinalIgnoreCase) ? SourceLanguage.Kasm : SourceLanguage.Kc;
    }

    /// <summary>
    /// True when the client's format list at <paramref name="path"/> (below <c>capabilities</c>:
    /// <c>textDocument.hover.contentFormat</c>, <c>…completionItem.documentationFormat</c>) offers
    /// Markdown. A client that sends no list gets plain text, as LSP prescribes.
    /// </summary>
    public bool Markdown(params string[] path) =>
        LspJson.Get(Server.ClientCapabilities, path) is JsonArray formats && formats.Any(f => LspJson.String(f) == "markdown");

    /// <summary>True when the client offers snippets in completion items.</summary>
    public bool Snippets =>
        LspJson.Bool(LspJson.Get(Server.ClientCapabilities, "textDocument", "completion", "completionItem", "snippetSupport")) == true;
}
