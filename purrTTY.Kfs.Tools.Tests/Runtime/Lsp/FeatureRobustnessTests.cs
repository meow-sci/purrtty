using System.Text.Json.Nodes;
using NUnit.Framework;
using purrTTY.Kfs.Lsp;
using purrTTY.Kfs.Lsp.Features;
using purrTTY.Kfs.Lsp.Protocol;
using purrTTY.Kfs.Tools.Tests.Lsp;

namespace purrTTY.Kfs.Tools.Tests.Runtime.Lsp;

/// <summary>Hover and completion: positions in three encodings, bad requests, provider faults, capabilities.</summary>
[TestFixture]
public sealed class FeatureRobustnessTests : ToolsRuntimeFixture
{
    private static FeatureSession Bench(string encoding = "utf-16", bool markdown = true, Action<KfsLanguageServer>? configure = null)
    {
        var ws = new TempWorkspace();
        return FeatureSession.Start(ws, KfsLanguageHost.ForBench(Runtime.Tables, ws.Root), encoding, markdown, configure: configure);
    }

    // A tab, an astral character (two UTF-16 units, four UTF-8 bytes) and an accented one (two
    // UTF-8 bytes) before the word on both lines.
    private const string Text = "#include <purrtty.h>\nvoid f(void)\n{\n\t/* 🐱 é */ x_light_set_on(0, 1);\n\t/* 🐱 é */ x_li\n}\n";

    [Test]
    public void Positions_In_Every_Encoding()
    {
        // The word starts at UTF-16 column 12: 15 UTF-8 bytes, 11 code points.
        foreach ((string encoding, int start) in new[] { ("utf-16", 12), ("utf-8", 15), ("utf-32", 11) })
        {
            foreach (string text in new[] { Text, Text.ReplaceLineEndings("\r\n") })
            {
                using FeatureSession s = Bench(encoding);
                Assert.That(s.Capabilities["positionEncoding"]!.GetValue<string>(), Is.EqualTo(encoding));
                string uri = s.Open("u.c", text);
                string what = $"{encoding}, {(text.Contains('\r') ? "CRLF" : "LF")}";

                foreach (int inside in new[] { start, start + 5, start + 13 })
                {
                    JsonObject hover = s.Hover(uri, 3, inside)!;
                    Assert.That(hover["range"]!.ToJsonString(), Is.EqualTo(HoverTests.Range(3, start, start + 14)), $"{what}, hover at {inside}");
                    Assert.That(hover["contents"]!["value"]!.GetValue<string>(), Does.Contain("x_light_set_on"));
                }
                Assert.That(s.Hover(uri, 3, start - 1), Is.Null, $"{what}: the space before the word");

                JsonObject list = s.Complete(uri, 4, start + 4)!;
                JsonObject item = FeatureSession.Item(list, "x_light_set_on");
                Assert.That(item["textEdit"]!["range"]!.ToJsonString(), Is.EqualTo(HoverTests.Range(4, start, start + 4)), what);
                Assert.That(item["textEdit"]!["newText"]!.GetValue<string>(), Is.EqualTo("x_light_set_on"));
            }
        }
    }

    [Test]
    public void Bad_Requests_Answer_Null_And_Never_Throw()
    {
        using FeatureSession s = Bench();
        string uri = s.Open("u.c", Text);
        string missing = s.Uri("never-opened.c");

        Assert.That(s.Hover(missing, 0, 0), Is.Null, "a document that is not open");
        Assert.That(s.Complete(missing, 0, 0), Is.Null);
        Assert.That(s.Hover(uri, 99, 0), Is.Null, "a line past the end of the file");
        Assert.That(s.Complete(uri, 99, 0), Is.Null);
        // LSP: a character past the end of its line is the end of the line (D3), never null.
        Assert.That(s.Complete(uri, 4, 99)?.ToJsonString(), Is.EqualTo(s.Complete(uri, 4, 16)?.ToJsonString()), "a character past the end of the line is clamped to it");
        Assert.That(s.Hover(uri, 1, 99)?.ToJsonString(), Is.EqualTo(s.Hover(uri, 1, 12)?.ToJsonString()), "hover too (\"void f(void)\" has 12 characters)");
        Assert.That(s.Hover(uri, -1, 0), Is.Null);
        Assert.That(s.Hover(uri, 0, -3), Is.Null);
        Assert.That(s.Client.Call("textDocument/hover", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = uri } }), Is.Null, "no position");
        Assert.That(s.Client.Call("textDocument/completion", new JsonObject()), Is.Null, "no parameters");
        Assert.That(s.Client.Call("textDocument/hover", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = uri }, ["position"] = "x" }), Is.Null, "a malformed position");

        // The end of a line is a valid position; a document closed again is not open.
        Assert.That(s.Complete(uri, 4, 16)!["items"]!.AsArray(), Is.Not.Empty, "at the end of a line");
        Assert.That(s.Hover(uri, 6, 0), Is.Null, "the empty last line");
        s.Client.Close(uri);
        Assert.That(s.Hover(uri, 3, 13), Is.Null, "closed");

        // A client that reads no Markdown gets plain text.
        using FeatureSession plain = Bench(markdown: false);
        string p = plain.Open("u.c", Text);
        JsonObject hover = plain.Hover(p, 3, 13)!;
        Assert.That(hover["contents"]!["kind"]!.GetValue<string>(), Is.EqualTo("plaintext"));
        Assert.That(hover["contents"]!["value"]!.GetValue<string>(), Does.StartWith("void x_light_set_on(uint32_t light, uint32_t on);\n"));
        Assert.That(hover["contents"]!["value"]!.GetValue<string>(), Does.Not.Contain("```").And.Not.Contain("**"));
        Assert.That(FeatureSession.Item(plain.Complete(p, 4, 16), "x_light_set_on")["documentation"]!["kind"]!.GetValue<string>(), Is.EqualTo("plaintext"));
    }

    [Test]
    public void A_Provider_Exception_Is_An_Error_Response_And_The_Server_Keeps_Serving()
    {
        int hoverFaults = 1, completionFaults = 1;
        var hover = new HoverProvider { Fault = _ => { if (Interlocked.Exchange(ref hoverFaults, 0) == 1) throw new InvalidOperationException("injected hover fault"); } };
        var completion = new CompletionProvider { Fault = _ => { if (Interlocked.Exchange(ref completionFaults, 0) == 1) throw new FormatException("injected completion fault"); } };
        using FeatureSession s = Bench(configure: server =>
        {
            server.Features.Add(hover);
            server.Features.Add(completion);
        });
        string uri = s.Open("u.c", Text);

        JsonObject failed = s.HoverResponse(uri, 3, 13);
        Assert.That(failed["error"]?["code"]?.GetValue<int>(), Is.EqualTo(JsonRpcErrorCodes.InternalError), failed.ToJsonString());
        Assert.That(failed["error"]!["message"]!.GetValue<string>(), Does.Contain("injected hover fault"));
        JsonObject failedCompletion = s.Client.Request("textDocument/completion", FeatureSession.Position(uri, 4, 16));
        Assert.That(failedCompletion["error"]?["code"]?.GetValue<int>(), Is.EqualTo(JsonRpcErrorCodes.InternalError));

        Assert.That(s.HoverText(uri, 3, 13), Does.Contain("x_light_set_on"), "the next hover works");
        Assert.That(FeatureSession.Labels(s.Complete(uri, 4, 16)), Does.Contain("x_light_set_on"), "the next completion works");
    }

    [Test]
    public void Capabilities_Advertise_Hover_And_Completion_Only_When_Registered()
    {
        using (FeatureSession s = Bench())
        {
            Assert.That(s.Capabilities["hoverProvider"]!.GetValue<bool>(), Is.True);
            JsonObject completion = s.Capabilities["completionProvider"]!.AsObject();
            Assert.That(completion["triggerCharacters"]!.AsArray().Select(c => c!.GetValue<string>()), Is.EqualTo(new[] { ".", ">", "#", "<", "\"" }));
            Assert.That(completion["resolveProvider"]!.GetValue<bool>(), Is.False);
        }

        using var ws = new TempWorkspace();
        using var bare = ServerHarness.Start(KfsLanguageHost.ForBench(Runtime.Tables, ws.Root));
        JsonObject caps = bare.Client.Initialize()["capabilities"]!.AsObject();
        Assert.That(caps.ContainsKey("hoverProvider"), Is.False);
        Assert.That(caps.ContainsKey("completionProvider"), Is.False);
        Assert.That(bare.Client.Request("textDocument/hover", FeatureSession.Position("file:///x.c", 0, 0))["error"]?["code"]?.GetValue<int>(),
            Is.EqualTo(JsonRpcErrorCodes.MethodNotFound));
    }
}
