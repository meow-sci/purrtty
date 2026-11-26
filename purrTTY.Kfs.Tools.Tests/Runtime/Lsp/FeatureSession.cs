using System.Text.Json.Nodes;
using purrTTY.Kfs.Lsp;
using purrTTY.Kfs.Lsp.Documents;
using purrTTY.Kfs.Lsp.Features;
using purrTTY.Kfs.Tools.Tests.Lsp;

namespace purrTTY.Kfs.Tools.Tests.Runtime.Lsp;

/// <summary>
/// A server with the standard features (hover, completion) on a temporary workspace, initialized
/// like an editor that reads Markdown: helpers for hover and completion requests and their results.
/// </summary>
internal sealed class FeatureSession : IDisposable
{
    private FeatureSession(TempWorkspace workspace, KfsLanguageHost host, ServerHarness harness)
    {
        Workspace = workspace;
        Host = host;
        Harness = harness;
    }

    public TempWorkspace Workspace { get; }
    public KfsLanguageHost Host { get; }
    public ServerHarness Harness { get; }
    public LspTestClient Client => Harness.Client;

    /// <summary>
    /// A session on <paramref name="workspace"/> (disposed with it) for <paramref name="host"/>;
    /// <paramref name="configure"/> replaces <see cref="StandardFeatures.AddTo"/>.
    /// </summary>
    public static FeatureSession Start(TempWorkspace workspace, KfsLanguageHost host, string encoding = "utf-16", bool markdown = true,
        bool snippets = false, Action<KfsLanguageServer>? configure = null)
    {
        var harness = ServerHarness.Start(host, configure: configure ?? StandardFeatures.AddTo);
        var session = new FeatureSession(workspace, host, harness);
        JsonObject p = LspTestClient.InitializeParams(null, encoding);
        JsonArray Formats() => markdown ? new JsonArray("markdown", "plaintext") : new JsonArray("plaintext");
        p["capabilities"]!["textDocument"] = new JsonObject
        {
            ["hover"] = new JsonObject { ["contentFormat"] = Formats() },
            ["completion"] = new JsonObject
            {
                ["completionItem"] = new JsonObject { ["snippetSupport"] = snippets, ["documentationFormat"] = Formats() },
            },
        };
        session.Capabilities = (JsonObject)session.Client.Call("initialize", p)!["capabilities"]!;
        session.Client.Notify("initialized", new JsonObject());
        return session;
    }

    /// <summary>The server's capabilities from <c>initialize</c>.</summary>
    public JsonObject Capabilities { get; private set; } = null!;

    public string Uri(string path) => new WorkspaceUris(Workspace.Root).ToUri(path);

    /// <summary>Opens <paramref name="path"/> with <paramref name="text"/> (not written to disk) and returns its URI.</summary>
    public string Open(string path, string text, int version = 1)
    {
        string uri = Uri(path);
        Client.Open(uri, text, version, path.EndsWith(".kasm", StringComparison.Ordinal) || path.EndsWith(".kinc", StringComparison.Ordinal) ? "kasm" : "kc");
        return uri;
    }

    /// <summary>Builds everything open and waits for the diagnostics of <paramref name="uri"/> (after <paramref name="from"/>).</summary>
    public JsonObject Build(string uri, int from, Func<JsonObject, bool>? until = null)
    {
        Harness.Flush();
        return Client.WaitForDiagnostics(uri, until, from);
    }

    public static JsonObject Position(string uri, int line, int character) => new()
    {
        ["textDocument"] = new JsonObject { ["uri"] = uri },
        ["position"] = new JsonObject { ["line"] = line, ["character"] = character },
    };

    /// <summary>The whole <c>textDocument/hover</c> response.</summary>
    public JsonObject HoverResponse(string uri, int line, int character) => Client.Request("textDocument/hover", Position(uri, line, character));

    /// <summary>The hover result (null for none); fails on an error response.</summary>
    public JsonObject? Hover(string uri, int line, int character) => (JsonObject?)Client.Call("textDocument/hover", Position(uri, line, character));

    /// <summary>The hover's Markdown, or null.</summary>
    public string? HoverText(string uri, int line, int character) => Hover(uri, line, character)?["contents"]?["value"]?.GetValue<string>();

    /// <summary>The hover at the first occurrence of <paramref name="word"/> in <paramref name="text"/> (UTF-16 positions).</summary>
    public string? HoverOn(string uri, string text, string word, int offset = 1)
    {
        (int line, int character) = Find(text, word, offset);
        return HoverText(uri, line, character);
    }

    /// <summary>The completion result (null for none); <paramref name="trigger"/> sends a trigger-character context.</summary>
    public JsonObject? Complete(string uri, int line, int character, string? trigger = null)
    {
        JsonObject p = Position(uri, line, character);
        p["context"] = trigger is null ? new JsonObject { ["triggerKind"] = 1 } : new JsonObject { ["triggerKind"] = 2, ["triggerCharacter"] = trigger };
        return (JsonObject?)Client.Call("textDocument/completion", p);
    }

    /// <summary>The completion at the end of line <paramref name="line"/> of <paramref name="text"/>.</summary>
    public JsonObject? CompleteAtEnd(string uri, string text, int line, string? trigger = null)
    {
        string l = text.Replace("\r\n", "\n").Split('\n')[line];
        return Complete(uri, line, l.Length, trigger);
    }

    public static List<string> Labels(JsonObject? list) =>
        list?["items"]?.AsArray().Select(i => i!["label"]!.GetValue<string>()).ToList() ?? [];

    public static JsonObject Item(JsonObject? list, string label) =>
        (JsonObject)(list?["items"]?.AsArray().FirstOrDefault(i => i!["label"]!.GetValue<string>() == label)
            ?? throw new InvalidOperationException($"no item '{label}' in [{string.Join(", ", Labels(list).Take(60))}]"));

    /// <summary>The 0-based line and UTF-16 column of the <paramref name="offset"/>-th character of the first <paramref name="word"/>.</summary>
    public static (int Line, int Character) Find(string text, string word, int offset = 1)
    {
        string[] lines = text.Replace("\r\n", "\n").Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            int at = lines[i].IndexOf(word, StringComparison.Ordinal);
            if (at >= 0) return (i, at + offset);
        }
        throw new InvalidOperationException($"'{word}' is not in the text");
    }

    public void Dispose()
    {
        Harness.Dispose();
        Workspace.Dispose();
    }
}
