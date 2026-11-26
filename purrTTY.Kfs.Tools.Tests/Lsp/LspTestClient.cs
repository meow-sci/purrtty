using System.Text;
using System.Text.Json.Nodes;

namespace purrTTY.Kfs.Tools.Tests.Lsp;

/// <summary>
/// A minimal LSP client for tests: frames and sends requests and notifications, reads every message
/// the server sends on a background thread and lets the test wait for one — a response, a
/// notification, a publish of diagnostics, the end of the stream — always on a condition with a
/// generous timeout, never a fixed sleep.
/// </summary>
internal sealed class LspTestClient : IDisposable
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private readonly Stream _input;
    private readonly Stream _output;
    private readonly object _gate = new();
    private readonly List<JsonObject> _messages = [];
    private readonly Thread _reader;
    private int _nextId;
    private bool _ended;
    private string? _readError;

    public LspTestClient(Stream fromServer, Stream toServer)
    {
        _input = fromServer;
        _output = toServer;
        _reader = new Thread(ReadLoop) { IsBackground = true, Name = "LSP test client reader" };
        _reader.Start();
    }

    /// <summary>Every message received so far.</summary>
    public IReadOnlyList<JsonObject> Messages
    {
        get { lock (_gate) return _messages.ToList(); }
    }

    /// <summary>True once the server's stream ended.</summary>
    public bool Ended
    {
        get { lock (_gate) return _ended; }
    }

    // ------------------------------------------------------------------ sending

    public void SendRaw(byte[] bytes)
    {
        try
        {
            _output.Write(bytes, 0, bytes.Length);
            _output.Flush();
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException)
        {
            // The server closed; the test asserts on what it received.
        }
    }

    public void SendRaw(string text) => SendRaw(Encoding.UTF8.GetBytes(text));

    public static byte[] Frame(JsonObject message)
    {
        byte[] body = Encoding.UTF8.GetBytes(message.ToJsonString());
        return [.. Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n"), .. body];
    }

    public void Send(JsonObject message) => SendRaw(Frame(message));

    public int SendRequest(string method, JsonNode? @params = null)
    {
        int id = Interlocked.Increment(ref _nextId);
        var message = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method };
        if (@params is not null) message["params"] = @params;
        Send(message);
        return id;
    }

    public void Notify(string method, JsonNode? @params = null)
    {
        var message = new JsonObject { ["jsonrpc"] = "2.0", ["method"] = method };
        if (@params is not null) message["params"] = @params;
        Send(message);
    }

    /// <summary>Sends a request and waits for its response (the whole message: <c>result</c> or <c>error</c>).</summary>
    public JsonObject Request(string method, JsonNode? @params = null)
    {
        int id = SendRequest(method, @params);
        return WaitForResponse(id);
    }

    /// <summary>Sends a request and returns its <c>result</c>; fails the test on an error response.</summary>
    public JsonNode? Call(string method, JsonNode? @params = null)
    {
        JsonObject response = Request(method, @params);
        if (response["error"] is { } error) throw new InvalidOperationException($"{method} failed: {error.ToJsonString()}");
        return response["result"];
    }

    /// <summary>A round trip with a method the server does not know: proves every earlier message was processed.</summary>
    public void Sync()
    {
        JsonObject r = Request("purrtty/test/sync");
        if (r["error"]?["code"]?.GetValue<int>() != -32601) throw new InvalidOperationException("sync: unexpected answer " + r.ToJsonString());
    }

    // ------------------------------------------------------------------ the LSP surface

    public static JsonObject InitializeParams(string? token = null, params string[] encodings)
    {
        var p = new JsonObject
        {
            ["processId"] = null,
            ["rootUri"] = null,
            ["capabilities"] = new JsonObject
            {
                ["general"] = new JsonObject { ["positionEncodings"] = new JsonArray(encodings.Select(e => (JsonNode)JsonValue.Create(e)).ToArray()) },
            },
        };
        if (token is not null) p["initializationOptions"] = new JsonObject { ["token"] = token };
        return p;
    }

    /// <summary><c>initialize</c> + <c>initialized</c>; returns the result.</summary>
    public JsonObject Initialize(string? token = null, params string[] encodings)
    {
        if (encodings.Length == 0) encodings = ["utf-8", "utf-16", "utf-32"];
        JsonNode? result = Call("initialize", InitializeParams(token, encodings));
        Notify("initialized", new JsonObject());
        return (JsonObject)result!;
    }

    public void Open(string uri, string text, int version = 1, string languageId = "kc") =>
        Notify("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject { ["uri"] = uri, ["languageId"] = languageId, ["version"] = version, ["text"] = text },
        });

    public void Change(string uri, int version, string text) =>
        Notify("textDocument/didChange", new JsonObject
        {
            ["textDocument"] = new JsonObject { ["uri"] = uri, ["version"] = version },
            ["contentChanges"] = new JsonArray(new JsonObject { ["text"] = text }),
        });

    public void Close(string uri) =>
        Notify("textDocument/didClose", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = uri } });

    /// <summary><c>shutdown</c> then <c>exit</c>.</summary>
    public void ShutdownAndExit()
    {
        Call("shutdown");
        Notify("exit");
    }

    // ------------------------------------------------------------------ waiting

    public JsonObject WaitForResponse(int id) =>
        WaitFor(m => m["method"] is null && m["id"]?.GetValueKind() == System.Text.Json.JsonValueKind.Number && m["id"]!.GetValue<int>() == id, $"the response to request {id}");

    /// <summary>Waits for a message (already received or arriving) after index <paramref name="from"/>.</summary>
    public JsonObject WaitFor(Func<JsonObject, bool> predicate, string what, int from = 0)
    {
        var deadline = DateTime.UtcNow + Timeout;
        lock (_gate)
        {
            while (true)
            {
                for (int i = from; i < _messages.Count; i++)
                {
                    if (predicate(_messages[i])) return _messages[i];
                }
                if (_ended) throw new TimeoutException($"the server closed the stream before {what} ({_readError ?? "end of stream"}).\n{Dump()}");
                TimeSpan left = deadline - DateTime.UtcNow;
                if (left <= TimeSpan.Zero) throw new TimeoutException($"no {what} within {Timeout.TotalSeconds} s.\n{Dump()}");
                Monitor.Wait(_gate, left);
            }
        }
    }

    /// <summary>The index the next message will have: pass it as <c>from</c> to wait only for later ones.</summary>
    public int Mark
    {
        get { lock (_gate) return _messages.Count; }
    }

    /// <summary>Waits for a <c>publishDiagnostics</c> for <paramref name="uri"/> (after <paramref name="from"/>) that satisfies <paramref name="predicate"/>.</summary>
    public JsonObject WaitForDiagnostics(string uri, Func<JsonObject, bool>? predicate = null, int from = 0, string? what = null) =>
        (JsonObject)WaitFor(m => m["method"]?.GetValue<string>() == "textDocument/publishDiagnostics"
            && Same(m["params"]?["uri"]?.GetValue<string>(), uri)
            && (predicate is null || predicate((JsonObject)m["params"]!)), what ?? $"diagnostics for {uri}", from)["params"]!;

    /// <summary>Every <c>publishDiagnostics</c> params for <paramref name="uri"/> received after <paramref name="from"/>.</summary>
    public List<JsonObject> Publishes(string uri, int from = 0)
    {
        lock (_gate)
        {
            return _messages.Skip(from)
                .Where(m => m["method"]?.GetValue<string>() == "textDocument/publishDiagnostics" && Same(m["params"]?["uri"]?.GetValue<string>(), uri))
                .Select(m => (JsonObject)m["params"]!).ToList();
        }
    }

    public bool WaitForEnd(TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? Timeout);
        lock (_gate)
        {
            while (!_ended)
            {
                TimeSpan left = deadline - DateTime.UtcNow;
                if (left <= TimeSpan.Zero) return false;
                Monitor.Wait(_gate, left);
            }
            return true;
        }
    }

    /// <summary>URIs are compared after decoding (the server may percent-encode what the test did not).</summary>
    public static bool Same(string? a, string? b) =>
        a is not null && b is not null && (a == b || Uri.UnescapeDataString(a) == Uri.UnescapeDataString(b));

    public string Dump()
    {
        lock (_gate)
        {
            var sb = new StringBuilder("received:\n");
            foreach (JsonObject m in _messages.TakeLast(40)) sb.Append("  ").Append(Shorten(m.ToJsonString())).Append('\n');
            return sb.ToString();
        }
    }

    private static string Shorten(string s) => s.Length <= 600 ? s : s[..600] + "…";

    // ------------------------------------------------------------------ reading

    private void ReadLoop()
    {
        try
        {
            while (ReadMessage() is { } message)
            {
                lock (_gate)
                {
                    _messages.Add(message);
                    Monitor.PulseAll(_gate);
                }
            }
        }
        catch (Exception e)
        {
            lock (_gate) _readError = e.GetType().Name + ": " + e.Message;
        }
        finally
        {
            lock (_gate)
            {
                _ended = true;
                Monitor.PulseAll(_gate);
            }
        }
    }

    private JsonObject? ReadMessage()
    {
        int length = -1;
        while (true)
        {
            string? line = ReadLine();
            if (line is null) return null;
            if (line.Length == 0)
            {
                if (length >= 0) break;
                continue;
            }
            int colon = line.IndexOf(':');
            if (colon > 0 && line[..colon].Trim().Equals("Content-Length", StringComparison.OrdinalIgnoreCase)) length = int.Parse(line[(colon + 1)..].Trim());
        }
        var body = new byte[length];
        int got = 0;
        while (got < length)
        {
            int n = _input.Read(body, got, length - got);
            if (n <= 0) return null;
            got += n;
        }
        return (JsonObject)JsonNode.Parse(body)!;
    }

    private string? ReadLine()
    {
        var bytes = new List<byte>();
        while (true)
        {
            int b = _input.ReadByte();
            if (b < 0) return bytes.Count == 0 ? null : Encoding.ASCII.GetString(bytes.ToArray());
            if (b == '\n') return Encoding.ASCII.GetString(bytes.ToArray()).TrimEnd('\r');
            bytes.Add((byte)b);
        }
    }

    public void Dispose()
    {
        try { _output.Dispose(); } catch { /* closing */ }
        try { _input.Dispose(); } catch { /* closing */ }
        _reader.Join(TimeSpan.FromSeconds(10));
    }
}
