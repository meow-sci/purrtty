using System.Text;
using System.Text.Json.Nodes;
using NUnit.Framework;
using purrTTY.Kfs.Lsp;
using purrTTY.Kfs.Lsp.InGame;
using purrTTY.Kfs.Lsp.Protocol;

namespace purrTTY.Kfs.Tools.Tests.Lsp;

/// <summary>
/// The review's robustness findings at the level of the pieces, without the game: framing that only
/// accepts LSP's headers (B5), a body buffer that grows with the bytes that arrive (A2), the
/// writer's byte bound (A3), the thread entry points' catch-all and start failures (A1), the
/// main-thread queue's per-frame budget, busy refusal and coalescing (B3), and the output tee's
/// forwarding rules (C3, the token print).
/// </summary>
[TestFixture]
public sealed class HardeningUnitTests
{
    private static readonly TimeSpan Long = LspTestClient.Timeout;

    private sealed class Connection : IDisposable
    {
        private readonly Thread _thread;

        public Connection(JsonRpcOptions? options = null, Func<JsonRpcRequest, CancellationToken, Task<JsonNode?>>? handler = null)
        {
            var (serverIn, serverOut, clientIn, clientOut, toServer, toClient) = MemoryPipe.Duplex();
            ToServer = toServer;
            ToClient = toClient;
            Rpc = new JsonRpcConnection(serverIn, serverOut, options) { RequestHandler = handler ?? ((r, _) => Task.FromResult(r.Params?.DeepClone())) };
            Client = new LspTestClient(clientIn, clientOut);
            _thread = new Thread(Rpc.Run) { IsBackground = true, Name = "hardening rpc" };
            _thread.Start();
        }

        public JsonRpcConnection Rpc { get; }
        public LspTestClient Client { get; }
        public MemoryPipe ToServer { get; }
        public MemoryPipe ToClient { get; }

        public bool Ended() => _thread.Join(Long);

        public void Dispose()
        {
            Client.Dispose();
            Rpc.Abort();
            Assert.That(Ended(), Is.True);
        }
    }

    private static string Framed(string body) => $"Content-Length: {Encoding.UTF8.GetByteCount(body)}\r\n\r\n{body}";

    // ------------------------------------------------------------------ B5: only LSP headers

    [TestCase("POST /a:b HTTP/1.1\r\nHost: 127.0.0.1\r\nContent-Type: text/plain\r\nContent-Length: 2\r\n\r\n{}")]
    [TestCase("GET / HTTP/1.1\r\n\r\n")]
    [TestCase("Content-Length: 2\r\nX-Anything: 1\r\n\r\n{}")]
    [TestCase("Content-Length: 2\r\nContent-Length: 2\r\n\r\n{}")]
    public void Framing_Accepts_Only_Lsp_Headers(string input)
    {
        using var c = new Connection();
        c.Client.SendRaw(input);
        Assert.That(c.Ended(), Is.True, "closed");
        Assert.That(c.Client.WaitForEnd(), Is.True);
        Assert.That(c.Client.Messages, Is.Empty, "nothing is answered to something that is not an LSP client");
    }

    [Test]
    public void Framing_Header_Count_And_Size_Are_Capped()
    {
        using (var c = new Connection())
        {
            c.Client.SendRaw(string.Concat(Enumerable.Repeat("Content-Type: x\r\n", 5)) + "Content-Length: 2\r\n\r\n{}");
            Assert.That(c.Ended(), Is.True);
            Assert.That(c.Rpc.CloseReason, Does.Contain("too many header lines"));
        }
        using (var c = new Connection())
        {
            c.Client.SendRaw(new string('\n', 5000));   // blank lines count as header bytes
            Assert.That(c.Ended(), Is.True);
            Assert.That(c.Rpc.CloseReason, Does.Contain("header bytes"));
        }
        using (var c = new Connection())
        {
            c.Client.SendRaw("content-length: 13\r\ncontent-type: application/vscode-jsonrpc; charset=utf-8\r\n\r\n" + "{\"id\":1,\"jsonrpc\":\"2.0\",\"method\":\"m\"}"[..13]);
            c.Client.SendRaw(Framed("{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"echo\",\"params\":[2]}"));
            Assert.That(c.Client.WaitForResponse(2)["result"]![0]!.GetValue<int>(), Is.EqualTo(2), "header names are case-insensitive");
        }
    }

    // ------------------------------------------------------------------ A2: no up-front allocation

    [Test]
    public void A_Declared_Length_Is_Not_Allocated_Before_The_Bytes_Arrive()
    {
        using var c = new Connection(new JsonRpcOptions { MaxMessageBytes = 64 * 1024 * 1024 });
        c.Client.SendRaw(Framed("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"echo\",\"params\":[1]}"));
        c.Client.WaitForResponse(1);   // warmed up: the read thread is in its loop

        Assert.That(SpinWait.SpinUntil(() => c.ToServer.IsDrained, Long), Is.True);
        long waits = c.ToServer.Waits;
        long before = GC.GetTotalAllocatedBytes(precise: true);
        // A header declaring 48 MiB and one byte of body (twenty such connections were +1281 MiB):
        // the old code allocated the declared length at once.
        c.Client.SendRaw("Content-Length: 50331648\r\n\r\n{");
        Assert.That(SpinWait.SpinUntil(() => c.ToServer.IsDrained && c.ToServer.Waits > waits, Long), Is.True, "the server read what was sent and waits for more");
        long allocated = GC.GetTotalAllocatedBytes(precise: true) - before;
        Assert.That(allocated, Is.LessThan(8L * 1024 * 1024), $"allocated {allocated} bytes for a 1-byte body declared as 48 MiB");
    }

    [Test]
    public void LimitMessages_Closes_On_A_Larger_Message_Unread()
    {
        using var c = new Connection();
        c.Rpc.LimitMessages(1024, closeWhenExceeded: true);
        c.Client.SendRaw("Content-Length: 2000\r\n\r\n");
        Assert.That(c.Ended(), Is.True);
        Assert.That(c.Rpc.CloseReason, Does.Contain("before initialize (at most 1024 bytes)"));
    }

    // ------------------------------------------------------------------ A3: the writer's bound

    /// <summary>A stream whose writes block until released (a peer that stopped reading, without sockets).</summary>
    private sealed class StuckStream : Stream
    {
        private readonly ManualResetEventSlim _release = new(false);
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count)
        {
            _release.Wait(Long);
            throw new IOException("the stream was closed");
        }
        protected override void Dispose(bool disposing)
        {
            _release.Set();
            base.Dispose(disposing);
        }
    }

    [Test]
    public void The_Write_Queue_Is_Bounded_By_Bytes_And_Overflow_Aborts()
    {
        var input = new MemoryPipe();
        var output = new StuckStream();
        var rpc = new JsonRpcConnection(input.Reader(), output, new JsonRpcOptions { MaxQueuedBytes = 256 * 1024 });
        var thread = new Thread(rpc.Run) { IsBackground = true };
        thread.Start();
        string text = new('x', 4096);
        long peak = 0;
        for (int i = 0; i < 10_000 && !rpc.IsClosed; i++)
        {
            rpc.Notify("purrtty/kfsOutput", new JsonObject { ["text"] = text });
            peak = Math.Max(peak, rpc.QueuedBytes);
        }
        Assert.That(rpc.IsClosed, Is.True, "a peer that does not read is cut off");
        Assert.That(rpc.CloseReason, Does.Contain("stopped reading"));
        Assert.That(peak, Is.LessThanOrEqualTo(256 * 1024 + 8 * 1024), "never more than the bound (plus one frame)");
        Assert.That(thread.Join(Long), Is.True, "Run returns: the abort unblocked the writer and the reader");
    }

    // ------------------------------------------------------------------ A1: thread entry points

    [Test]
    public void An_Exception_Leaving_A_Library_Thread_Is_Logged_Not_Fatal()
    {
        var lines = new List<string>();
        LspLogSink previous = LspThreads.Log;
        LspThreads.Log = (level, message) => { lock (lines) lines.Add($"{level}: {message}"); };
        try
        {
            int baseline = LspThreads.Alive;
            Thread? t = LspThreads.Start("hardening thrower", () => throw new InvalidOperationException("injected"));
            Assert.That(t, Is.Not.Null);
            Assert.That(t!.Join(Long), Is.True);
            Assert.That(LspThreads.WaitUntilAtMost(baseline, Long), Is.True, "counted out");
            lock (lines) Assert.That(lines, Has.Some.Contains("'hardening thrower' failed and ended: InvalidOperationException: injected"));

            LspThreads.FailNextStarts(1);
            Assert.That(LspThreads.Start("hardening no-start", () => { }), Is.Null, "a failed start is reported, not thrown");
            Assert.That(LspThreads.Alive, Is.EqualTo(baseline));
            lock (lines) Assert.That(lines, Has.Some.Contains("could not start the thread 'hardening no-start'"));
        }
        finally
        {
            LspThreads.FailNextStarts(0);
            LspThreads.Log = previous;
        }
    }

    [Test]
    public void A_Connection_Whose_Writer_Cannot_Start_Closes_Cleanly()
    {
        var (serverIn, serverOut, clientIn, _, _, _) = MemoryPipe.Duplex();
        var rpc = new JsonRpcConnection(serverIn, serverOut);
        LspLogSink previous = LspThreads.Log;
        LspThreads.Log = LspLog.None;
        try
        {
            LspThreads.FailNextStarts(1);
            Assert.DoesNotThrow(rpc.Run, "Run never throws");
            Assert.That(rpc.CloseReason, Is.EqualTo("no writer thread could be started"));
            Assert.That(clientIn.Read(new byte[1], 0, 1), Is.Zero, "the streams are closed");
        }
        finally
        {
            LspThreads.FailNextStarts(0);
            LspThreads.Log = previous;
        }
    }

    // ------------------------------------------------------------------ B3: the main-thread queue

    [Test]
    public void A_Pump_Runs_At_Least_One_Call_And_More_Only_Within_Its_Budget()
    {
        TimeSpan now = TimeSpan.Zero;
        var queue = new MainThreadQueue(maxPending: 8, budget: TimeSpan.FromMilliseconds(4), clock: () => now);
        var ran = new List<int>();
        var tasks = new List<Task<int>>();
        for (int i = 0; i < 8; i++)
        {
            int n = i;
            tasks.Add(queue.Post(() => { ran.Add(n); now += TimeSpan.FromMilliseconds(3); return n; }, Long));   // each "takes" 3 ms
        }
        Assert.That(queue.Pump(), Is.EqualTo(2), "3 ms + 3 ms: the second starts within the 4 ms budget, the third does not");
        Assert.That(queue.Pump(), Is.EqualTo(2));
        var slow = new MainThreadQueue(budget: TimeSpan.FromMilliseconds(4), clock: () => now);
        slow.Post(() => { now += TimeSpan.FromSeconds(1); return 0; }, Long);
        slow.Post(() => 1, Long);
        Assert.That(slow.Pump(), Is.EqualTo(1), "one slow call is the frame's whole share");
        Assert.That(slow.Pump(), Is.EqualTo(1));
        var one = new MainThreadQueue(budget: TimeSpan.Zero);
        for (int i = 0; i < 3; i++) one.Post(() => 0, Long);
        Assert.That(one.Pump(), Is.EqualTo(1), "budget zero: exactly one per pump");
        Assert.That(ran, Is.EqualTo(new[] { 0, 1, 2, 3 }));
    }

    [Test]
    public void Identical_Coalescable_Calls_Share_One_Run_And_Others_Do_Not()
    {
        var queue = new MainThreadQueue(maxPending: 2, budget: TimeSpan.FromSeconds(30));
        int runs = 0;
        Task<int> a = queue.Post(() => ++runs, Long, coalesceKey: "kfs build p/main.c");
        Task<int> b = queue.Post(() => ++runs, Long, coalesceKey: "kfs build p/main.c");
        using var cts = new CancellationTokenSource();
        Task<int> c = queue.Post(() => ++runs, Long, cts.Token, coalesceKey: "kfs build p/main.c");
        Assert.That(queue.Pending, Is.EqualTo(1), "joined callers take no extra slot");
        cts.Cancel();
        Assert.That(c.IsCanceled, Is.True, "one joined caller cancels alone");
        Task<int> other = queue.Post(() => ++runs, Long, coalesceKey: "kfs build q/main.c");
        Task<int> busy = queue.Post(() => ++runs, Long);
        Assert.That(busy.IsFaulted && busy.Exception!.InnerException is MainThreadBusyException, Is.True, "two waiting: busy");
        Assert.That(queue.Pump(), Is.EqualTo(2));
        Assert.That((a.Result, b.Result, other.Result, runs), Is.EqualTo((1, 1, 2, 2)), "one run for the identical pair");

        // Only the last waiting call is joined: build, load, build is three runs in that order.
        var order = new List<string>();
        var q2 = new MainThreadQueue(budget: TimeSpan.FromSeconds(30));
        q2.Post(() => { order.Add("build"); return 0; }, Long, coalesceKey: "kfs build x");
        q2.Post(() => { order.Add("load"); return 0; }, Long);
        q2.Post(() => { order.Add("build"); return 0; }, Long, coalesceKey: "kfs build x");
        q2.Pump();
        Assert.That(order, Is.EqualTo(new[] { "build", "load", "build" }));

        Assert.That(InGameLanguageHost.CoalesceKey("kfs", "build proj/main.c"), Is.EqualTo("kfs build proj/main.c"));
        Assert.That(InGameLanguageHost.CoalesceKey("kfs", "manual KC-E003"), Is.Not.Null);
        foreach (string line in new[] { "load proj/main.c", "start", "stop", "reset", "target vehicle" })
            Assert.That(InGameLanguageHost.CoalesceKey("kfs", line), Is.Null, line);
        Assert.That(InGameLanguageHost.CoalesceKey("purr", "parts lights"), Is.Null);
    }

    [Test]
    public void Every_Waiter_Of_A_Coalesced_Call_Gets_The_Same_Failure()
    {
        var queue = new MainThreadQueue();
        Task<int> a = queue.Post<int>(() => throw new InvalidOperationException("verb broke"), Long, coalesceKey: "k");
        Task<int> b = queue.Post<int>(() => throw new InvalidOperationException("never run"), Long, coalesceKey: "k");
        queue.Pump();
        Assert.That(a.Exception!.InnerException!.Message, Is.EqualTo("verb broke"));
        Assert.That(b.Exception!.InnerException!.Message, Is.EqualTo("verb broke"));
        Task<int> late = queue.Post(() => 1, TimeSpan.FromMilliseconds(10), coalesceKey: "t");
        Task<int> lateToo = queue.Post(() => 2, Long, coalesceKey: "t");
        var e = Assert.Throws<AggregateException>(() => lateToo.Wait(Long));
        Assert.That(e!.InnerException, Is.TypeOf<MainThreadTimeoutException>(), "the joined caller shares the first one's deadline");
        Assert.That(late.IsFaulted, Is.True);
    }

    // ------------------------------------------------------------------ C3 and the token print: the output tee

    [Test]
    public void Clients_Only_Suppresses_Forwarding_Only_In_Front_Of_The_Game_Console()
    {
        var foreign = new StringWriter { NewLine = "\n" };
        TextWriter slot = foreign;
        var sink = new StringBuilder();
        OutputTeeInstallation tee = OutputTeeInstallation.Install(() => slot, w => slot = w, t => sink.Append(t), isGameConsole: _ => false);
        Assert.That(tee.Tee.OriginalIsGameConsole, Is.False);
        OutputTee.EnterClientsOnly();
        try { slot.WriteLine("kfs: an error"); }
        finally { OutputTee.ExitClientsOnly(); }
        Assert.That(foreign.ToString(), Is.EqualTo("kfs: an error\n"), "another mod's writer still gets the error line it got without the tee");
        Assert.That(sink.ToString(), Is.EqualTo("kfs: an error\n"));
        tee.Restore();

        var console = new StringWriter { NewLine = "\n" };
        slot = console;
        sink.Clear();
        tee = OutputTeeInstallation.Install(() => slot, w => slot = w, t => sink.Append(t), isGameConsole: w => ReferenceEquals(w, console));
        OutputTee.EnterClientsOnly();
        try { slot.WriteLine("kfs: an error"); }
        finally { OutputTee.ExitClientsOnly(); }
        Assert.That(console.ToString(), Is.Empty, "the game's console already shows the logged error: not twice");
        Assert.That(sink.ToString(), Is.EqualTo("kfs: an error\n"));
        tee.Restore();
    }

    [Test]
    public void Console_Only_Text_Never_Reaches_The_Feed_Or_A_Capture()
    {
        var console = new StringWriter { NewLine = "\n" };
        TextWriter slot = console;
        var sink = new StringBuilder();
        OutputTeeInstallation tee = OutputTeeInstallation.Install(() => slot, w => slot = w, t => sink.Append(t));
        using (OutputTee.Capture capture = tee.Tee.BeginCapture())
        {
            OutputTee.EnterConsoleOnly();
            try { slot.WriteLine("SECRET-TOKEN"); }
            finally { OutputTee.ExitConsoleOnly(); }
            slot.WriteLine("purr lsp: listening");
            Assert.That(capture.Text, Is.EqualTo("purr lsp: listening\n"));
        }
        Assert.That(console.ToString(), Is.EqualTo("SECRET-TOKEN\npurr lsp: listening\n"), "the console shows it");
        Assert.That(sink.ToString(), Is.EqualTo("purr lsp: listening\n"), "editors subscribed to output never see it");
        tee.Restore();
    }
}
