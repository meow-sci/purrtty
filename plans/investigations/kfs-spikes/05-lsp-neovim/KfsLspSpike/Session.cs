using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using KSA.Kfs.Compiler;

namespace KfsLspSpike;

/// One LSP connection: hand-rolled JSON-RPC with Content-Length framing.
sealed class Session
{
    readonly string _tag;
    readonly BufferedStream _in;
    readonly Stream _out;
    readonly object _writeLock = new();
    readonly string? _forceEncoding;
    readonly int _debounceMs;
    readonly bool _exitProcessOnExit;
    readonly ConcurrentDictionary<string, Doc> _docs = new();
    string _enc = "utf-16";
    bool _shutdown;
    public int ExitCode { get; private set; } = 1;

    static int _running, _maxRunning;

    sealed class Doc
    {
        public required string Uri, Name;
        public string Text = "";
        public int? Version;
        public int Gen, PublishedGen;
        public bool Closed;
    }

    public Session(string tag, Stream input, Stream output, string? forceEncoding, int debounceMs, bool exitProcessOnExit)
    {
        _tag = tag; _in = new BufferedStream(input, 65536); _out = output;
        _forceEncoding = forceEncoding; _debounceMs = debounceMs; _exitProcessOnExit = exitProcessOnExit;
    }

    // ---------------------------------------------------------------- framing

    JsonObject? Read()
    {
        int length = -1;
        var line = new StringBuilder();
        while (true)
        {
            int b = _in.ReadByte();
            if (b < 0) return null;
            if (b == '\n')
            {
                string l = line.ToString().TrimEnd('\r');
                line.Clear();
                if (l.Length == 0) { if (length >= 0) break; continue; }
                int c = l.IndexOf(':');
                if (c > 0 && l[..c].Trim().Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                    length = int.Parse(l[(c + 1)..].Trim());
            }
            else line.Append((char)b);
        }
        byte[] body = new byte[length];
        int got = 0;
        while (got < length)
        {
            int n = _in.Read(body, got, length - got);
            if (n <= 0) return null;
            got += n;
        }
        return JsonNode.Parse(body) as JsonObject;
    }

    void Send(JsonObject msg)
    {
        msg["jsonrpc"] = "2.0";
        byte[] body = Encoding.UTF8.GetBytes(msg.ToJsonString());
        byte[] head = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");
        lock (_writeLock)
        {
            try { _out.Write(head); _out.Write(body); _out.Flush(); }
            catch (Exception e) { Log.W($"{_tag} write failed: {e.GetType().Name}: {e.Message}"); }
        }
    }

    void Reply(JsonNode? id, JsonNode? result) => Send(new JsonObject { ["id"] = id?.DeepClone(), ["result"] = result });
    void ReplyError(JsonNode? id, int code, string message) =>
        Send(new JsonObject { ["id"] = id?.DeepClone(), ["error"] = new JsonObject { ["code"] = code, ["message"] = message } });
    void Notify(string method, JsonNode? p) => Send(new JsonObject { ["method"] = method, ["params"] = p });

    // ---------------------------------------------------------------- loop

    public void Run()
    {
        while (true)
        {
            JsonObject? msg;
            try { msg = Read(); }
            catch (Exception e) { Log.W($"{_tag} read failed: {e.GetType().Name}: {e.Message}"); break; }
            if (msg == null) { Log.W($"{_tag} EOF"); break; }
            string? method = msg["method"]?.GetValue<string>();
            JsonNode? id = msg["id"];
            if (method == null) { Log.W($"{_tag} <- response #{id} (ignored)"); continue; }
            Log.W($"{_tag} <- {method}{(id != null ? $" #{id.ToJsonString()}" : "")}");
            var p = msg["params"] as JsonObject;
            try
            {
                switch (method)
                {
                    case "initialize": Reply(id, Initialize(p)); break;
                    case "initialized": break;
                    case "shutdown": _shutdown = true; Reply(id, null); break;
                    case "exit":
                        ExitCode = _shutdown ? 0 : 1;
                        Log.W($"{_tag} exit (process exit: {_exitProcessOnExit})");
                        return;
                    case "textDocument/didOpen": DidOpen(p!); break;
                    case "textDocument/didChange": DidChange(p!); break;
                    case "textDocument/didClose": DidClose(p!); break;
                    case "textDocument/hover": Reply(id, Hover(p!)); break;
                    case "textDocument/completion": Reply(id, Completion(p!)); break;
                    default:
                        if (id != null) ReplyError(id, -32601, "method not found: " + method);
                        break;
                }
            }
            catch (Exception e)
            {
                Log.W($"{_tag} handler {method} threw {e.GetType().Name}: {e.Message}");
                if (id != null) ReplyError(id, -32603, e.Message);
            }
        }
    }

    JsonObject Initialize(JsonObject? p)
    {
        var offered = (p?["capabilities"]?["general"]?["positionEncodings"] as JsonArray)?
            .Select(n => n!.GetValue<string>()).ToList() ?? new List<string>();
        // kFS columns are UTF-16 code units, so utf-16 needs no conversion; it is also the LSP default
        // that every client must support. Other encodings are converted (and exercised with --encoding).
        if (_forceEncoding != null && offered.Contains(_forceEncoding)) _enc = _forceEncoding;
        else if (offered.Count == 0 || offered.Contains("utf-16")) _enc = "utf-16";
        else _enc = offered[0];
        Log.W($"{_tag} client offered positionEncodings [{string.Join(",", offered)}] -> using {_enc}");
        return new JsonObject
        {
            ["capabilities"] = new JsonObject
            {
                ["positionEncoding"] = _enc,
                ["textDocumentSync"] = new JsonObject { ["openClose"] = true, ["change"] = 1 },
                ["hoverProvider"] = true,
                ["completionProvider"] = new JsonObject { ["resolveProvider"] = false },
            },
            ["serverInfo"] = new JsonObject { ["name"] = "kfs-lsp-spike", ["version"] = "0.0" },
        };
    }

    // ---------------------------------------------------------------- documents

    void DidOpen(JsonObject p)
    {
        var td = p["textDocument"]!;
        string uri = td["uri"]!.GetValue<string>();
        var doc = new Doc { Uri = uri, Name = Path.GetFileName(new Uri(uri).LocalPath), Text = td["text"]!.GetValue<string>(), Version = td["version"]?.GetValue<int>() };
        _docs[uri] = doc;
        Schedule(doc);
    }

    void DidChange(JsonObject p)
    {
        string uri = p["textDocument"]!["uri"]!.GetValue<string>();
        if (!_docs.TryGetValue(uri, out var doc)) return;
        var changes = p["contentChanges"]!.AsArray();
        lock (doc)
        {
            doc.Text = changes[^1]!["text"]!.GetValue<string>();          // full sync: last change wins
            doc.Version = p["textDocument"]!["version"]?.GetValue<int>();
        }
        Schedule(doc);
    }

    void DidClose(JsonObject p)
    {
        string uri = p["textDocument"]!["uri"]!.GetValue<string>();
        if (_docs.TryRemove(uri, out var doc)) lock (doc) { doc.Closed = true; }
        Notify("textDocument/publishDiagnostics", new JsonObject { ["uri"] = uri, ["diagnostics"] = new JsonArray() });
    }

    void Schedule(Doc doc)
    {
        int gen; string text; int? ver;
        lock (doc) { gen = ++doc.Gen; text = doc.Text; ver = doc.Version; }
        Task.Run(async () =>
        {
            if (_debounceMs > 0)
            {
                await Task.Delay(_debounceMs);
                lock (doc) if (doc.Gen != gen) { Log.W($"{_tag} {doc.Name} v{ver}: superseded before compile"); return; }
            }
            int now = Interlocked.Increment(ref _running);
            int max; do { max = _maxRunning; } while (now > max && Interlocked.CompareExchange(ref _maxRunning, now, max) != max);
            var o = Kfs.Compile(doc.Name, text);
            Interlocked.Decrement(ref _running);
            string what = o.Error != null ? $"THREW {o.Error.GetType().Name}: {o.Error.Message}"
                                          : $"ok={o.Result!.Ok} diags={o.Result.Diagnostics.Count}";
            lock (doc)
            {
                if (doc.Closed || doc.Gen != gen || gen < doc.PublishedGen)
                {
                    Log.W($"{_tag} compile {doc.Name} v{ver} gen{gen}: {o.Ms:0.0} ms, {what} -> DROPPED (superseded by gen{doc.Gen}; concurrent={now}, max={_maxRunning})");
                    return;
                }
                Log.W($"{_tag} compile {doc.Name} v{ver} gen{gen}: {o.Ms:0.0} ms, {what} (concurrent={now}, max={_maxRunning})");
                doc.PublishedGen = gen;
                var diags = new JsonArray();
                var lines = SplitLines(text);
                if (o.Error != null)
                    diags.Add(MakeDiag(0, 0, 0, 1, "KFS-LSP", $"compiler threw {o.Error.GetType().Name}: {o.Error.Message}"));
                else
                    foreach (var d in o.Result!.Diagnostics) diags.Add(ToLsp(d, doc.Name, lines));
                var prm = new JsonObject { ["uri"] = doc.Uri, ["diagnostics"] = diags };
                if (ver != null) prm["version"] = ver;
                Notify("textDocument/publishDiagnostics", prm);
            }
        });
    }

    // ---------------------------------------------------------------- positions

    /// Same normalisation as KSA.Kfs.KC.Lexer: CRLF and lone CR become LF.
    static string[] SplitLines(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

    static bool IsIdent(char c) => char.IsLetterOrDigit(c) || c == '_';

    /// UTF-16 index within a line -> offset in the negotiated encoding.
    int Encode(string line, int u16)
    {
        u16 = Math.Clamp(u16, 0, line.Length);
        if (_enc == "utf-8") return Encoding.UTF8.GetByteCount(line.AsSpan(0, u16));
        if (_enc == "utf-32") { int n = 0; for (int i = 0; i < u16; i++) { if (char.IsHighSurrogate(line[i]) && i + 1 < u16) i++; n++; } return n; }
        return u16;
    }

    /// Offset in the negotiated encoding -> UTF-16 index within a line.
    int Decode(string line, int off)
    {
        if (_enc == "utf-16") return Math.Clamp(off, 0, line.Length);
        int i = 0, acc = 0;
        while (i < line.Length && acc < off)
        {
            int w = char.IsSurrogatePair(line, i) ? 2 : 1;
            acc += _enc == "utf-8" ? Encoding.UTF8.GetByteCount(line.AsSpan(i, w)) : 1;
            i += w;
        }
        return i;
    }

    JsonObject Pos(string line, int lineNo, int u16) => new() { ["line"] = lineNo, ["character"] = Encode(line, u16) };

    static JsonObject MakeDiag(int l0, int c0, int l1, int c1, string code, string message, int severity = 1) => new()
    {
        ["range"] = new JsonObject { ["start"] = new JsonObject { ["line"] = l0, ["character"] = c0 }, ["end"] = new JsonObject { ["line"] = l1, ["character"] = c1 } },
        ["severity"] = severity, ["code"] = code, ["source"] = "kfs", ["message"] = message,
    };

    /// kFS diagnostics are points: 1-based line, 1-based column counted in UTF-16 code units of the
    /// CR/LF-normalised text (a tab is one column). There is no end/length; we extend the range to the
    /// end of the identifier (or one character) at that point.
    JsonObject ToLsp(Diagnostic d, string docName, string[] lines)
    {
        string msg = d.Message;
        int line, c0, c1;
        if (d.File == docName && d.Line > 0)
        {
            line = Math.Min(d.Line - 1, lines.Length - 1);
            string L = lines[line];
            c0 = Math.Clamp(d.Col - 1, 0, L.Length);
            c1 = c0;
            if (c1 < L.Length && IsIdent(L[c1])) while (c1 < L.Length && IsIdent(L[c1])) c1++;
            else if (c1 < L.Length && !char.IsWhiteSpace(L[c1])) c1 += char.IsSurrogatePair(L, c1) ? 2 : 1;
        }
        else
        {
            // In a header (system or other) or project-level (Line 0): anchor on the first #include line.
            line = Array.FindIndex(lines, l => l.TrimStart().StartsWith("#include") && l.Contains(d.File));
            if (line < 0) line = Array.FindIndex(lines, l => l.TrimStart().StartsWith("#include"));
            if (line < 0) line = 0;
            c0 = 0; c1 = lines[line].Length;
            msg = (d.Line > 0 ? $"[in {d.File}:{d.Line}:{d.Col}] " : $"[{d.File}] ") + msg;
        }
        if (d.Hint != null) msg += "\nhint: " + d.Hint;
        var j = MakeDiag(line, 0, line, 0, d.Code, msg, d.Severity == Severity.Error ? 1 : 2);
        j["range"] = new JsonObject { ["start"] = Pos(lines[line], line, c0), ["end"] = Pos(lines[line], line, c1) };
        return j;
    }

    // ---------------------------------------------------------------- hover / completion

    JsonNode? Hover(JsonObject p)
    {
        string uri = p["textDocument"]!["uri"]!.GetValue<string>();
        if (!_docs.TryGetValue(uri, out var doc)) return null;
        string[] lines; lock (doc) lines = SplitLines(doc.Text);
        int ln = p["position"]!["line"]!.GetValue<int>();
        if (ln < 0 || ln >= lines.Length) return null;
        string L = lines[ln];
        int i = Decode(L, p["position"]!["character"]!.GetValue<int>());
        if ((i >= L.Length || !IsIdent(L[i])) && i > 0 && IsIdent(L[i - 1])) i--;
        if (i >= L.Length || !IsIdent(L[i])) return null;
        int s = i, e = i;
        while (s > 0 && IsIdent(L[s - 1])) s--;
        while (e < L.Length && IsIdent(L[e])) e++;
        string word = L[s..e];
        if (!Kfs.ServicesByName.TryGetValue(word, out var row)) return null;
        string md =
            $"```c\n{Kfs.Clean(row.Kc).Replace("`", "")}\n```\n" +
            $"{Kfs.Clean(row.Summary)}\n\n" +
            $"- **args:** {Kfs.Clean(row.Args)}\n" +
            $"- **results:** {Kfs.Clean(row.Results)}\n" +
            $"- **cost class:** {Kfs.Clean(row.CostClass)}\n" +
            $"- **host checks:** {Kfs.Clean(row.HostChecks)}\n" +
            $"- **service:** `{row.Symbol}` 0x{row.Id:X4} ({Kfs.Clean(row.Kind)})";
        return new JsonObject
        {
            ["contents"] = new JsonObject { ["kind"] = "markdown", ["value"] = md },
            ["range"] = new JsonObject { ["start"] = Pos(L, ln, s), ["end"] = Pos(L, ln, e) },
        };
    }

    JsonNode Completion(JsonObject p)
    {
        var items = new JsonArray();
        foreach (var (name, row) in Kfs.ServicesByName.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            items.Add(new JsonObject
            {
                ["label"] = name,
                ["kind"] = 3,                                                         // Function
                ["detail"] = Kfs.Clean(row.Kc).Replace("`", ""),
                ["documentation"] = new JsonObject { ["kind"] = "markdown", ["value"] = Kfs.Clean(row.Summary) },
                ["insertText"] = name,
            });
        return new JsonObject { ["isIncomplete"] = false, ["items"] = items };
    }
}
