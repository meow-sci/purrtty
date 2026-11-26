// RULE FOR THIS FILE: no game type. Only the LSP structures the server uses.

using System.Text.Json.Nodes;

namespace purrTTY.Kfs.Lsp.Protocol;

/// <summary>How LSP <c>character</c> offsets count (<c>positionEncoding</c>, LSP 3.17).</summary>
public enum PositionEncoding
{
    /// <summary><c>utf-16</c>: UTF-16 code units — kFS's own column unit and the LSP default.</summary>
    Utf16,
    /// <summary><c>utf-8</c>: bytes of the UTF-8 encoding.</summary>
    Utf8,
    /// <summary><c>utf-32</c>: Unicode code points.</summary>
    Utf32,
}

/// <summary>Names of <see cref="PositionEncoding"/> values on the wire.</summary>
public static class PositionEncodings
{
    /// <summary>The wire name (<c>utf-16</c>, <c>utf-8</c>, <c>utf-32</c>).</summary>
    public static string Name(this PositionEncoding encoding) => encoding switch
    {
        PositionEncoding.Utf8 => "utf-8",
        PositionEncoding.Utf32 => "utf-32",
        _ => "utf-16",
    };

    /// <summary>The encoding for a wire name, or null for one the server does not know.</summary>
    public static PositionEncoding? Parse(string? name) => name switch
    {
        "utf-16" => PositionEncoding.Utf16,
        "utf-8" => PositionEncoding.Utf8,
        "utf-32" => PositionEncoding.Utf32,
        _ => null,
    };

    /// <summary>
    /// The encoding the server uses for a client that offered <paramref name="offered"/>
    /// (<c>general.positionEncodings</c>, in the client's order of preference): <c>utf-16</c> when it is
    /// offered or nothing is (LSP's mandatory default), else the first offered encoding the server
    /// supports, else <c>utf-16</c>.
    /// </summary>
    public static PositionEncoding Negotiate(IReadOnlyList<string>? offered)
    {
        if (offered is null || offered.Count == 0 || offered.Contains("utf-16")) return PositionEncoding.Utf16;
        foreach (string name in offered)
        {
            if (Parse(name) is { } encoding) return encoding;
        }
        return PositionEncoding.Utf16;
    }
}

/// <summary>An LSP position: 0-based line and character (in the negotiated encoding).</summary>
public readonly record struct LspPosition(int Line, int Character)
{
    /// <summary>Reads a position object; null when malformed.</summary>
    public static LspPosition? From(JsonNode? node) =>
        node is JsonObject o && LspJson.Int(o["line"]) is { } line && LspJson.Int(o["character"]) is { } character
            ? new LspPosition(line, character) : null;

    /// <summary>To the wire shape.</summary>
    public JsonObject ToJson() => new() { ["line"] = Line, ["character"] = Character };
}

/// <summary>An LSP range (end exclusive).</summary>
public readonly record struct LspRange(LspPosition Start, LspPosition End)
{
    /// <summary>A range on one line.</summary>
    public static LspRange OnLine(int line, int start, int end) => new(new LspPosition(line, start), new LspPosition(line, end));

    /// <summary>Reads a range object; null when malformed.</summary>
    public static LspRange? From(JsonNode? node) =>
        node is JsonObject o && LspPosition.From(o["start"]) is { } start && LspPosition.From(o["end"]) is { } end
            ? new LspRange(start, end) : null;

    /// <summary>To the wire shape.</summary>
    public JsonObject ToJson() => new() { ["start"] = Start.ToJson(), ["end"] = End.ToJson() };

    /// <inheritdoc/>
    public override string ToString() => $"{Start.Line}:{Start.Character}-{End.Line}:{End.Character}";
}

/// <summary>A location in a document.</summary>
/// <param name="Uri">The document.</param>
/// <param name="Range">The range.</param>
public sealed record LspLocation(string Uri, LspRange Range)
{
    /// <summary>To the wire shape.</summary>
    public JsonObject ToJson() => new() { ["uri"] = Uri, ["range"] = Range.ToJson() };
}

/// <summary><c>DiagnosticRelatedInformation</c>.</summary>
/// <param name="Location">Where.</param>
/// <param name="Message">What is there.</param>
public sealed record LspRelatedInformation(LspLocation Location, string Message)
{
    /// <summary>To the wire shape.</summary>
    public JsonObject ToJson() => new() { ["location"] = Location.ToJson(), ["message"] = Message };
}

/// <summary>LSP diagnostic severities.</summary>
public static class LspSeverity
{
    /// <summary>Error.</summary>
    public const int Error = 1;
    /// <summary>Warning.</summary>
    public const int Warning = 2;
    /// <summary>Information.</summary>
    public const int Information = 3;
    /// <summary>Hint.</summary>
    public const int Hint = 4;
}

/// <summary>An LSP diagnostic as the server publishes it.</summary>
/// <param name="Range">The range in the document (negotiated encoding).</param>
/// <param name="Severity"><see cref="LspSeverity"/>.</param>
/// <param name="Code">The kFS code (<c>KC-E003</c>, <c>KA-W711</c>, <c>kfs</c>, <c>KFS-LSP</c>).</param>
/// <param name="Message">The message; a hint follows on a second line.</param>
/// <param name="Source">The source (<c>kfs</c>).</param>
/// <param name="RelatedInformation">Other locations, or null.</param>
/// <param name="Tags">Diagnostic tags (1 = unnecessary), or null.</param>
public sealed record LspDiagnostic(LspRange Range, int Severity, string Code, string Message, string Source = "kfs",
    IReadOnlyList<LspRelatedInformation>? RelatedInformation = null, IReadOnlyList<int>? Tags = null)
{
    /// <summary>To the wire shape.</summary>
    public JsonObject ToJson()
    {
        var o = new JsonObject
        {
            ["range"] = Range.ToJson(),
            ["severity"] = Severity,
            ["code"] = Code,
            ["source"] = Source,
            ["message"] = Message,
        };
        if (RelatedInformation is { Count: > 0 }) o["relatedInformation"] = new JsonArray(RelatedInformation.Select(r => (JsonNode)r.ToJson()).ToArray());
        if (Tags is { Count: > 0 }) o["tags"] = new JsonArray(Tags.Select(t => (JsonNode)JsonValue.Create(t)).ToArray());
        return o;
    }
}

/// <summary>Small readers for the loosely typed parameters clients send.</summary>
public static class LspJson
{
    /// <summary>A string value, or null.</summary>
    public static string? String(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue(out string? s) ? s : null;

    /// <summary>An integer value, or null.</summary>
    public static int? Int(JsonNode? node)
    {
        if (node is not JsonValue v) return null;
        if (v.TryGetValue(out int i)) return i;
        if (v.TryGetValue(out long l) && l is >= int.MinValue and <= int.MaxValue) return (int)l;
        if (v.TryGetValue(out double d) && d == Math.Floor(d) && d is >= int.MinValue and <= int.MaxValue) return (int)d;
        return null;
    }

    /// <summary>A boolean value, or null.</summary>
    public static bool? Bool(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue(out bool b) ? b : null;

    /// <summary>
    /// The node at <paramref name="path"/> below <paramref name="node"/> (object members only), or null —
    /// never throws, unlike <see cref="JsonNode"/>'s indexer on a value.
    /// </summary>
    public static JsonNode? Get(JsonNode? node, params string[] path)
    {
        foreach (string key in path)
        {
            if (node is not JsonObject o) return null;
            node = o[key];
        }
        return node;
    }

    /// <summary>The <c>textDocument.uri</c> of a parameters object, or null.</summary>
    public static string? DocumentUri(JsonNode? @params) => String(Get(@params, "textDocument", "uri"));
}
