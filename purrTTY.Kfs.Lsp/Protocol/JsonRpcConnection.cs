// RULE FOR THIS FILE: no game type. The protocol layer is tested without the game.

using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace purrTTY.Kfs.Lsp.Protocol;

/// <summary>JSON-RPC and LSP error codes the server uses.</summary>
public static class JsonRpcErrorCodes
{
    /// <summary>The body is not valid JSON.</summary>
    public const int ParseError = -32700;
    /// <summary>The JSON is not a request, notification or response.</summary>
    public const int InvalidRequest = -32600;
    /// <summary>No handler for the method.</summary>
    public const int MethodNotFound = -32601;
    /// <summary>The parameters are missing or malformed.</summary>
    public const int InvalidParams = -32602;
    /// <summary>The handler failed.</summary>
    public const int InternalError = -32603;
    /// <summary>A request before <c>initialize</c>.</summary>
    public const int ServerNotInitialized = -32002;
    /// <summary>The request was refused (the token check, a command that cannot run).</summary>
    public const int RequestFailed = -32803;
    /// <summary>The client cancelled the request.</summary>
    public const int RequestCancelled = -32800;
}

/// <summary>An error a request handler throws to answer with a JSON-RPC error.</summary>
public sealed class JsonRpcException(int code, string message, JsonNode? data = null) : Exception(message)
{
    /// <summary>The error code (<see cref="JsonRpcErrorCodes"/>).</summary>
    public int Code { get; } = code;

    /// <summary>Optional error data.</summary>
    public JsonNode? ErrorData { get; } = data;
}

/// <summary>A request received from the peer.</summary>
public sealed class JsonRpcRequest
{
    internal JsonRpcRequest(JsonNode id, string method, JsonNode? @params)
    {
        Id = id;
        Method = method;
        Params = @params;
    }

    /// <summary>The request id (a number or a string).</summary>
    public JsonNode Id { get; }

    /// <summary>The method.</summary>
    public string Method { get; }

    /// <summary>The parameters, or null.</summary>
    public JsonNode? Params { get; }

    /// <summary>Set by a handler: close the connection once the response has been written (the token refusal).</summary>
    public bool CloseAfterResponse { get; set; }

    internal string Key => JsonRpcConnection.IdKey(Id);
}

/// <summary>A notification received from the peer.</summary>
/// <param name="Method">The method.</param>
/// <param name="Params">The parameters, or null.</param>
public sealed record JsonRpcNotification(string Method, JsonNode? Params);

/// <summary>Limits of a <see cref="JsonRpcConnection"/>.</summary>
public sealed class JsonRpcOptions
{
    /// <summary>The default <see cref="MaxMessageBytes"/>: 16 MiB (four times the game's 4 MiB source limit, JSON-escaped).</summary>
    public const int DefaultMaxMessageBytes = 16 * 1024 * 1024;

    /// <summary>The default <see cref="MaxQueuedBytes"/>: 16 MiB.</summary>
    public const int DefaultMaxQueuedBytes = 16 * 1024 * 1024;

    /// <summary>The largest message body accepted; a larger one is read and discarded (default 16 MiB). See <see cref="JsonRpcConnection.LimitMessages"/> for a stricter limit that closes.</summary>
    public int MaxMessageBytes { get; init; } = DefaultMaxMessageBytes;

    /// <summary>The longest header line accepted; a longer one closes the connection (default 1 KiB).</summary>
    public int MaxHeaderLineBytes { get; init; } = 1024;

    /// <summary>The most header bytes of one message, blank lines before it included; more closes the connection (default 2 KiB).</summary>
    public int MaxHeaderBytes { get; init; } = 2048;

    /// <summary>The most header lines of one message; more closes the connection (default 4: LSP defines two).</summary>
    public int MaxHeaderLines { get; init; } = 4;

    /// <summary>
    /// The most bytes of outgoing frames waiting for the writer (default 16 MiB). A send that would
    /// queue more aborts the connection: the peer is not reading, and the server must not grow for it.
    /// </summary>
    public long MaxQueuedBytes { get; init; } = DefaultMaxQueuedBytes;

    /// <summary>A name for log lines (the connection's peer).</summary>
    public string Name { get; init; } = "client";

    /// <summary>Where the connection logs.</summary>
    public LspLogSink Log { get; init; } = LspLog.None;
}

/// <summary>
/// JSON-RPC 2.0 with LSP's <c>Content-Length</c> framing over any pair of streams. <see cref="Run"/>
/// is the read loop (it blocks the calling thread until the connection closes); writes go through
/// one queue drained by the connection's own writer thread, so they are serialised and never block a
/// caller. Requests are handed to <see cref="RequestHandler"/> on the read thread in arrival order and
/// answered when the returned task completes; <c>$/cancelRequest</c> cancels the token the handler got.
/// Malformed input never throws out of the read loop: invalid JSON or a body that is no message is
/// answered with an error and the loop carries on; framing it cannot recover from (no or a bad
/// <c>Content-Length</c>, a header other than <c>Content-Length</c> / <c>Content-Type</c> — an HTTP
/// request line, say —, too many or too long header lines, end of stream inside a message) closes the
/// connection cleanly. Nothing is ever written anywhere but the output stream.
/// <para><b>Bounded memory.</b> A body is read into a buffer that grows with the bytes that actually
/// arrive (never allocated up front from the declared length); outgoing frames are bounded by
/// <see cref="JsonRpcOptions.MaxQueuedBytes"/> — a peer that does not read is cut off
/// (<see cref="Abort"/>) instead of growing the queue.</para>
/// </summary>
public sealed class JsonRpcConnection : IDisposable
{
    private static readonly JsonDocumentOptions ParseOptions = new() { MaxDepth = 256 };

    private readonly Stream _input;
    private readonly Stream _output;
    private readonly bool _ownsStreams;
    private readonly JsonRpcOptions _options;
    private readonly BlockingCollection<byte[]> _outgoing = new(new ConcurrentQueue<byte[]>());
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _incoming = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonNode?>> _outgoingRequests = new(StringComparer.Ordinal);
    private readonly byte[] _buffer = new byte[64 * 1024];
    private readonly object _closeGate = new();
    private readonly ManualResetEventSlim _writerDone = new(false);
    private int _bufferStart, _bufferEnd;
    private long _nextId;
    private long _queuedBytes;
    private int _limitBytes = int.MaxValue;
    private bool _limitCloses;
    private Thread? _writer;
    private bool _closing;
    private bool _started;

    /// <summary>A connection over <paramref name="input"/> and <paramref name="output"/> (they may be the same duplex stream).</summary>
    /// <param name="input">What the peer sends.</param>
    /// <param name="output">Where the connection writes.</param>
    /// <param name="options">Limits, name and log.</param>
    /// <param name="ownsStreams">Dispose both streams when the connection closes (default).</param>
    public JsonRpcConnection(Stream input, Stream output, JsonRpcOptions? options = null, bool ownsStreams = true)
    {
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _options = options ?? new JsonRpcOptions();
        _ownsStreams = ownsStreams;
    }

    /// <summary>Handles a request; the task's result is the response's <c>result</c>. Throw <see cref="JsonRpcException"/> for an error response. Unset: every request is <c>MethodNotFound</c>.</summary>
    public Func<JsonRpcRequest, CancellationToken, Task<JsonNode?>>? RequestHandler { get; set; }

    /// <summary>Handles a notification, on the read thread, in arrival order. <c>$/cancelRequest</c> is handled by the connection.</summary>
    public Action<JsonRpcNotification>? NotificationHandler { get; set; }

    /// <summary>True once the connection is closing or closed.</summary>
    public bool IsClosed
    {
        get { lock (_closeGate) return _closing; }
    }

    /// <summary>Why the connection closed (end of stream, exit, a framing error, …), once it has.</summary>
    public string? CloseReason { get; private set; }

    /// <summary>Bytes of outgoing frames waiting for the writer now.</summary>
    public long QueuedBytes => Interlocked.Read(ref _queuedBytes);

    /// <summary>
    /// A stricter limit for the messages read from now on: a body over <paramref name="maxBytes"/>
    /// is skipped as usual, or — <paramref name="closeWhenExceeded"/> — closes the connection without
    /// reading it (before authentication). <see cref="JsonRpcOptions.MaxMessageBytes"/> still applies.
    /// </summary>
    public void LimitMessages(int maxBytes, bool closeWhenExceeded)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxBytes);
        Volatile.Write(ref _limitCloses, closeWhenExceeded);
        Volatile.Write(ref _limitBytes, maxBytes);
    }

    /// <summary>Lifts a <see cref="LimitMessages"/> limit (authenticated).</summary>
    public void LiftMessageLimit()
    {
        Volatile.Write(ref _limitBytes, int.MaxValue);
        Volatile.Write(ref _limitCloses, false);
    }

    /// <summary>Raised once, after the read loop and the writer have stopped.</summary>
    public event Action? Closed;

    /// <summary>
    /// Runs the read loop on the calling thread until the connection closes (end of stream, an
    /// unrecoverable framing error, <see cref="Close"/>), then waits for the writer to drain. Never throws.
    /// </summary>
    public void Run()
    {
        lock (_closeGate)
        {
            if (_started) throw new InvalidOperationException("the connection is already running");
            _started = true;
            _writer = LspThreads.Start($"purrTTY kFS LSP writer ({_options.Name})", WriteLoop);
        }
        if (_writer is null)
        {
            // No thread for the writer (the process is at its thread limit): nothing can be answered.
            Close("no writer thread could be started");
            DisposeStreams();
            _writerDone.Set();
        }
        try
        {
            if (_writer is not null) ReadLoop();
        }
        catch (Exception e)
        {
            // Defensive: ReadLoop handles everything it expects.
            _options.Log.Safe(LspLogLevel.Error, $"{_options.Name}: read loop failed: {e.GetType().Name}: {e.Message}");
            Close("read loop failed");
        }
        Close(CloseReason ?? "end of stream");
        _writerDone.Wait(TimeSpan.FromSeconds(10));
        foreach (var pending in _incoming.Values) TryCancel(pending);
        foreach (var request in _outgoingRequests.Values) request.TrySetException(new IOException("the connection closed"));
        _outgoingRequests.Clear();
        try { Closed?.Invoke(); }
        catch (Exception e) { _options.Log.Safe(LspLogLevel.Error, $"{_options.Name}: a Closed handler threw {e.GetType().Name}: {e.Message}"); }
    }

    /// <summary>Sends a notification (queued; never blocks). Ignored once closed.</summary>
    public void Notify(string method, JsonNode? @params)
    {
        ArgumentNullException.ThrowIfNull(method);
        var message = new JsonObject { ["jsonrpc"] = "2.0", ["method"] = method };
        if (@params is not null) message["params"] = @params;
        Send(message);
    }

    /// <summary>Sends a request to the peer; the task completes with its <c>result</c>, or fails with <see cref="JsonRpcException"/> (an error response) or <see cref="IOException"/> (the connection closed).</summary>
    public Task<JsonNode?> SendRequest(string method, JsonNode? @params, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(method);
        string id = "s" + Interlocked.Increment(ref _nextId).ToString(CultureInfo.InvariantCulture);
        var completion = new TaskCompletionSource<JsonNode?>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (IsClosed)
        {
            completion.SetException(new IOException("the connection is closed"));
            return completion.Task;
        }
        _outgoingRequests[IdKey(JsonValue.Create(id))] = completion;
        if (cancellationToken.CanBeCanceled)
        {
            cancellationToken.Register(() =>
            {
                if (_outgoingRequests.TryRemove(IdKey(JsonValue.Create(id)), out var c) && c.TrySetCanceled(cancellationToken))
                    Notify("$/cancelRequest", new JsonObject { ["id"] = id });
            });
        }
        var message = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method };
        if (@params is not null) message["params"] = @params;
        Send(message);
        return completion.Task;
    }

    /// <summary>Closes the connection: pending writes are flushed, then the streams are closed and <see cref="Run"/> returns.</summary>
    public void Close() => Close("closed by the server");

    /// <summary>
    /// Closes the connection at once: the streams are disposed without flushing pending writes, which
    /// also unblocks a writer stuck on a peer that stopped reading (a listener's <c>Stop</c>).
    /// </summary>
    public void Abort() => Abort("aborted");

    internal void Abort(string reason)
    {
        Close(reason);
        if (_ownsStreams) DisposeStreams();
    }

    /// <inheritdoc/>
    public void Dispose() => Close();

    internal void Close(string reason)
    {
        lock (_closeGate)
        {
            if (_closing) return;
            _closing = true;
            CloseReason = reason;
        }
        _options.Log.Safe(LspLogLevel.Debug, $"{_options.Name}: closing ({reason})");
        _outgoing.CompleteAdding();
        bool started;
        lock (_closeGate) started = _started;
        if (!started)
        {
            // Never ran: no writer to close the streams.
            DisposeStreams();
            _writerDone.Set();
        }
    }

    // ------------------------------------------------------------------ writing

    private void Send(JsonObject message)
    {
        byte[] body;
        try
        {
            body = JsonSerializer.SerializeToUtf8Bytes(message);
        }
        catch (Exception e)
        {
            _options.Log.Safe(LspLogLevel.Error, $"{_options.Name}: could not serialise a message: {e.GetType().Name}: {e.Message}");
            return;
        }
        byte[] header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");
        var frame = new byte[header.Length + body.Length];
        header.CopyTo(frame, 0);
        body.CopyTo(frame, header.Length);
        if (IsClosed) return;
        long queued = Interlocked.Add(ref _queuedBytes, frame.Length);
        if (queued > _options.MaxQueuedBytes && queued != frame.Length)
        {
            // One frame alone may exceed the bound (it was within MaxMessageBytes to make); more may not.
            Interlocked.Add(ref _queuedBytes, -frame.Length);
            Warn($"the peer is not reading: {queued - frame.Length} bytes are waiting to be sent (at most {_options.MaxQueuedBytes}); the connection is cut off");
            Abort("the peer stopped reading (write queue full)");
            return;
        }
        try
        {
            _outgoing.Add(frame);
        }
        catch (InvalidOperationException)
        {
            // Closing: adding is complete; the message is dropped.
            Interlocked.Add(ref _queuedBytes, -frame.Length);
        }
    }

    private void WriteLoop()
    {
        try
        {
            foreach (byte[] frame in _outgoing.GetConsumingEnumerable())
            {
                try
                {
                    _output.Write(frame, 0, frame.Length);
                    _output.Flush();
                }
                catch (Exception e)
                {
                    // IOException (a send timeout: the peer stopped reading; a reset), ObjectDisposedException
                    // (aborted), or anything else a stream throws: the connection is over either way.
                    _options.Log.Safe(LspLogLevel.Debug, $"{_options.Name}: write failed: {e.GetType().Name}: {e.Message}");
                    Close("write failed");
                    break;
                }
                finally
                {
                    Interlocked.Add(ref _queuedBytes, -frame.Length);
                }
            }
        }
        catch (Exception e)
        {
            _options.Log.Safe(LspLogLevel.Error, $"{_options.Name}: the writer failed: {e.GetType().Name}: {e.Message}");
            Close("the writer failed");
        }
        finally
        {
            DisposeStreams();
            _writerDone.Set();
        }
    }

    private void DisposeStreams()
    {
        if (!_ownsStreams) return;
        try { _output.Dispose(); } catch { /* closing */ }
        try { _input.Dispose(); } catch { /* closing */ }
    }

    private void Respond(JsonNode id, JsonNode? result)
    {
        Send(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id.DeepClone(), ["result"] = result });
    }

    private void RespondError(JsonNode? id, int code, string message, JsonNode? data = null)
    {
        var error = new JsonObject { ["code"] = code, ["message"] = message };
        if (data is not null) error["data"] = data.DeepClone();
        Send(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id?.DeepClone(), ["error"] = error });
    }

    // ------------------------------------------------------------------ reading

    private void ReadLoop()
    {
        while (!IsClosed)
        {
            byte[]? body = ReadMessage(out string? failure);
            if (body is null)
            {
                Close(failure ?? "end of stream");
                return;
            }
            if (body.Length == 0) continue; // an oversized body that was skipped
            if (IsClosed) return;
            Dispatch(body);
        }
    }

    /// <summary>One message body; null at end of stream or on a framing error (<paramref name="failure"/>); empty for a skipped body.</summary>
    private byte[]? ReadMessage(out string? failure)
    {
        failure = null;
        long length = -1;
        int headers = 0;
        int headerBytes = 0;
        while (true)
        {
            string? line = ReadHeaderLine(out bool tooLong, out int consumed);
            headerBytes += consumed;
            if (tooLong)
            {
                failure = $"a header line is longer than {_options.MaxHeaderLineBytes} bytes";
                Warn(failure);
                return null;
            }
            if (headerBytes > _options.MaxHeaderBytes)
            {
                failure = $"more than {_options.MaxHeaderBytes} header bytes";
                Warn(failure);
                return null;
            }
            if (line is null)
            {
                if (headers > 0 || length >= 0) failure = "end of stream inside a header";
                return null;
            }
            if (line.Length == 0)
            {
                if (headers == 0) continue; // stray blank line between messages (counted in headerBytes)
                if (length < 0)
                {
                    failure = "a message without Content-Length";
                    Warn(failure);
                    return null;
                }
                break;
            }
            if (++headers > _options.MaxHeaderLines)
            {
                failure = "too many header lines";
                Warn(failure);
                return null;
            }
            int colon = line.IndexOf(':');
            if (colon <= 0)
            {
                failure = $"a malformed header line '{Shorten(line)}'";
                Warn(failure);
                return null;
            }
            ReadOnlySpan<char> name = line.AsSpan(0, colon);
            if (name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
            {
                if (length >= 0)
                {
                    failure = "a second Content-Length header";
                    Warn(failure);
                    return null;
                }
                if (!long.TryParse(line.AsSpan(colon + 1).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out length) || length < 0)
                {
                    failure = $"an invalid Content-Length '{Shorten(line[(colon + 1)..].Trim())}'";
                    Warn(failure);
                    return null;
                }
            }
            else if (!name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
            {
                // LSP's base protocol has these two headers only. Anything else — an HTTP request line
                // ("POST /a:b HTTP/1.1") or header from a web page, a scanner — is not a client.
                failure = $"not an LSP header: '{Shorten(line)}'";
                Warn(failure);
                return null;
            }
        }

        int limit = Volatile.Read(ref _limitBytes);
        if (length > limit && Volatile.Read(ref _limitCloses))
        {
            failure = $"a {length}-byte message before initialize (at most {limit} bytes)";
            Warn(failure);
            return null;
        }
        if (length > _options.MaxMessageBytes || length > limit)
        {
            Warn($"a {length}-byte message is larger than {Math.Min(limit, _options.MaxMessageBytes)} bytes; skipped");
            if (!Skip(length))
            {
                failure = "end of stream inside a message";
                return null;
            }
            return [];
        }
        byte[]? body = ReadBody((int)length);
        if (body is null)
        {
            failure = "end of stream inside a message";
            return null;
        }
        return body.Length == 0 ? "{}"u8.ToArray() : body; // an empty body is invalid JSON-RPC, answered below
    }

    private string? ReadHeaderLine(out bool tooLong, out int consumed)
    {
        tooLong = false;
        consumed = 0;
        var line = new List<byte>(64);
        while (true)
        {
            if (_bufferStart == _bufferEnd && !Fill()) return line.Count > 0 ? Encoding.ASCII.GetString(line.ToArray()).TrimEnd('\r') : null;
            int newline = Array.IndexOf(_buffer, (byte)'\n', _bufferStart, _bufferEnd - _bufferStart);
            int end = newline >= 0 ? newline : _bufferEnd;
            int take = Math.Min(end - _bufferStart, _options.MaxHeaderLineBytes + 1 - line.Count);
            for (int i = 0; i < take; i++) line.Add(_buffer[_bufferStart + i]);
            consumed += (newline >= 0 ? newline + 1 : _bufferEnd) - _bufferStart;
            _bufferStart = newline >= 0 ? newline + 1 : _bufferEnd;
            if (line.Count > _options.MaxHeaderLineBytes)
            {
                tooLong = true;
                return null;
            }
            if (newline >= 0)
            {
                int count = line.Count;
                if (count > 0 && line[count - 1] == '\r') count--;
                return Encoding.ASCII.GetString(line.ToArray(), 0, count);
            }
        }
    }

    /// <summary>A body of <paramref name="length"/> bytes, in a buffer that grows as they arrive; null at end of stream.</summary>
    private byte[]? ReadBody(int length)
    {
        var body = new byte[Math.Min(length, _buffer.Length)];
        int got = 0;
        while (got < length)
        {
            if (_bufferStart == _bufferEnd && !Fill()) return null;
            if (got == body.Length) Array.Resize(ref body, (int)Math.Min(length, body.Length * 2L));
            int n = Math.Min(body.Length - got, _bufferEnd - _bufferStart);
            Buffer.BlockCopy(_buffer, _bufferStart, body, got, n);
            _bufferStart += n;
            got += n;
        }
        return body;
    }

    private bool Skip(long count)
    {
        while (count > 0)
        {
            if (_bufferStart == _bufferEnd && !Fill()) return false;
            int n = (int)Math.Min(count, _bufferEnd - _bufferStart);
            _bufferStart += n;
            count -= n;
        }
        return true;
    }

    private bool Fill()
    {
        try
        {
            int n = _input.Read(_buffer, 0, _buffer.Length);
            _bufferStart = 0;
            _bufferEnd = Math.Max(n, 0);
            return n > 0;
        }
        catch (Exception e)
        {
            // IOException (a reset peer), ObjectDisposedException (aborted), or whatever a stream throws.
            if (!IsClosed) _options.Log.Safe(LspLogLevel.Debug, $"{_options.Name}: read failed: {e.GetType().Name}: {e.Message}");
            _bufferStart = _bufferEnd = 0;
            return false;
        }
    }

    private void Dispatch(byte[] body)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(body, documentOptions: ParseOptions);
        }
        catch (JsonException e)
        {
            Warn($"invalid JSON ({e.Message})");
            RespondError(null, JsonRpcErrorCodes.ParseError, "invalid JSON: " + e.Message);
            return;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        {
            Warn($"invalid JSON ({e.Message})");
            RespondError(null, JsonRpcErrorCodes.ParseError, "invalid JSON: " + e.Message);
            return;
        }

        if (node is not JsonObject message)
        {
            RespondError(null, JsonRpcErrorCodes.InvalidRequest, "a JSON-RPC message must be an object (batches are not supported)");
            return;
        }

        JsonNode? id = message["id"];
        if (message["method"] is JsonValue methodValue && methodValue.TryGetValue(out string? method) && method is not null)
        {
            JsonNode? @params = message["params"];
            if (id is null) HandleNotification(method, @params);
            else if (IsValidId(id)) HandleRequest(new JsonRpcRequest(id.DeepClone(), method, @params?.DeepClone()));
            else RespondError(null, JsonRpcErrorCodes.InvalidRequest, "a request id must be a number or a string");
            return;
        }
        if (id is not null && (message.ContainsKey("result") || message.ContainsKey("error")))
        {
            HandleResponse(id, message);
            return;
        }
        RespondError(IsValidId(id) ? id : null, JsonRpcErrorCodes.InvalidRequest, "not a JSON-RPC request, notification or response");
    }

    private void HandleNotification(string method, JsonNode? @params)
    {
        if (method == "$/cancelRequest")
        {
            JsonNode? id = (@params as JsonObject)?["id"];
            if (id is not null && IsValidId(id) && _incoming.TryGetValue(IdKey(id), out var source)) TryCancel(source);
            return;
        }
        try
        {
            NotificationHandler?.Invoke(new JsonRpcNotification(method, @params?.DeepClone()));
        }
        catch (Exception e)
        {
            _options.Log.Safe(LspLogLevel.Error, $"{_options.Name}: notification {method} failed: {e.GetType().Name}: {e.Message}");
        }
    }

    private void HandleRequest(JsonRpcRequest request)
    {
        var source = new CancellationTokenSource();
        string key = request.Key;
        if (!_incoming.TryAdd(key, source))
        {
            source.Dispose();
            RespondError(request.Id, JsonRpcErrorCodes.InvalidRequest, $"request id {request.Id.ToJsonString()} is already in use");
            return;
        }

        Task<JsonNode?> task;
        try
        {
            task = RequestHandler is { } handler
                ? handler(request, source.Token) ?? Task.FromResult<JsonNode?>(null)
                : Task.FromException<JsonNode?>(new JsonRpcException(JsonRpcErrorCodes.MethodNotFound, "method not found: " + request.Method));
        }
        catch (Exception e)
        {
            task = Task.FromException<JsonNode?>(e);
        }

        if (task.IsCompleted) Complete(request, task, source);
        else task.ContinueWith(t => Complete(request, t, source), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private void Complete(JsonRpcRequest request, Task<JsonNode?> task, CancellationTokenSource source)
    {
        _incoming.TryRemove(request.Key, out _);
        bool cancelled = source.IsCancellationRequested;
        source.Dispose();
        if (task.IsCompletedSuccessfully && !cancelled)
        {
            Respond(request.Id, task.Result);
        }
        else if (task.IsCanceled || cancelled || task.Exception?.InnerException is OperationCanceledException)
        {
            RespondError(request.Id, JsonRpcErrorCodes.RequestCancelled, "the request was cancelled");
        }
        else
        {
            Exception error = task.Exception?.InnerException ?? (Exception?)task.Exception ?? new InvalidOperationException("the request failed");
            if (error is JsonRpcException rpc)
            {
                RespondError(request.Id, rpc.Code, rpc.Message, rpc.ErrorData);
            }
            else
            {
                _options.Log.Safe(LspLogLevel.Error, $"{_options.Name}: {request.Method} failed: {error.GetType().Name}: {error.Message}");
                RespondError(request.Id, JsonRpcErrorCodes.InternalError, $"{request.Method} failed: {error.GetType().Name}: {error.Message}");
            }
        }
        if (request.CloseAfterResponse) Close("closed after the response to " + request.Method);
    }

    private void HandleResponse(JsonNode id, JsonObject message)
    {
        if (!IsValidId(id) || !_outgoingRequests.TryRemove(IdKey(id), out var completion))
        {
            _options.Log.Safe(LspLogLevel.Debug, $"{_options.Name}: a response to an unknown request {id.ToJsonString()} (ignored)");
            return;
        }
        if (message["error"] is JsonObject error)
        {
            int code = error["code"] is JsonValue c && c.TryGetValue(out int n) ? n : JsonRpcErrorCodes.InternalError;
            string text = error["message"] is JsonValue m && m.TryGetValue(out string? s) && s is not null ? s : "error";
            completion.TrySetException(new JsonRpcException(code, text, error["data"]?.DeepClone()));
        }
        else
        {
            completion.TrySetResult(message["result"]?.DeepClone());
        }
    }

    // ------------------------------------------------------------------ helpers

    internal static string IdKey(JsonNode id) => id.ToJsonString();

    private static bool IsValidId(JsonNode? id) =>
        id is JsonValue v && (v.GetValueKind() is JsonValueKind.Number or JsonValueKind.String);

    private static void TryCancel(CancellationTokenSource source)
    {
        try { source.Cancel(); }
        catch (ObjectDisposedException) { /* already answered */ }
        catch (AggregateException) { /* a registration threw; the request still ends */ }
    }

    private void Warn(string what) => _options.Log.Safe(LspLogLevel.Warning, $"{_options.Name}: {what}");

    private static string Shorten(string text) => text.Length <= 60 ? text : text[..60] + "…";
}
