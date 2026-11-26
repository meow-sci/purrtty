namespace purrTTY.Kfs.Tools.Tests.Lsp;

/// <summary>
/// A one-way in-memory byte pipe for driving a server without sockets. Every <c>Write</c> is one
/// chunk and every <c>Read</c> returns at most one chunk, so a test controls exactly how the bytes
/// are split across the reader's reads. Reading blocks until a chunk arrives or the pipe closes
/// (then 0: end of stream); writing to a closed pipe throws <see cref="IOException"/>.
/// </summary>
internal sealed class MemoryPipe
{
    private readonly object _gate = new();
    private readonly Queue<byte[]> _chunks = new();
    private byte[]? _current;
    private int _offset;
    private bool _closed;
    private bool _waiting;
    private long _waits;

    public void Write(ReadOnlySpan<byte> bytes)
    {
        lock (_gate)
        {
            if (_closed) throw new IOException("the pipe is closed");
            if (bytes.Length == 0) return;
            _chunks.Enqueue(bytes.ToArray());
            Monitor.PulseAll(_gate);
        }
    }

    public int Read(Span<byte> into)
    {
        lock (_gate)
        {
            while (_current is null)
            {
                if (_chunks.TryDequeue(out var next))
                {
                    _current = next;
                    _offset = 0;
                    break;
                }
                if (_closed) return 0;
                _waiting = true;
                _waits++;
                Monitor.Wait(_gate);
                _waiting = false;
            }
            int n = Math.Min(into.Length, _current!.Length - _offset);
            _current.AsSpan(_offset, n).CopyTo(into);
            _offset += n;
            if (_offset == _current.Length) _current = null;
            return n;
        }
    }

    public void Close()
    {
        lock (_gate)
        {
            _closed = true;
            Monitor.PulseAll(_gate);
        }
    }

    public bool IsClosed
    {
        get { lock (_gate) return _closed; }
    }

    /// <summary>How many times a reader has started waiting for a chunk.</summary>
    public long Waits
    {
        get { lock (_gate) return _waits; }
    }

    /// <summary>True when every chunk was read and a reader is waiting for more: what was sent has been processed.</summary>
    public bool IsDrained
    {
        get { lock (_gate) return _chunks.Count == 0 && _current is null && _waiting; }
    }

    /// <summary>The reading end as a stream (disposing it closes the pipe).</summary>
    public Stream Reader() => new End(this, reading: true);

    /// <summary>The writing end as a stream (disposing it closes the pipe).</summary>
    public Stream Writer() => new End(this, reading: false);

    /// <summary>Two pipes: what the server reads and writes, and the client's ends of both.</summary>
    public static (Stream ServerInput, Stream ServerOutput, Stream ClientInput, Stream ClientOutput, MemoryPipe ToServer, MemoryPipe ToClient) Duplex()
    {
        var toServer = new MemoryPipe();
        var toClient = new MemoryPipe();
        return (toServer.Reader(), toClient.Writer(), toClient.Reader(), toServer.Writer(), toServer, toClient);
    }

    private sealed class End(MemoryPipe pipe, bool reading) : Stream
    {
        public override bool CanRead => reading;
        public override bool CanWrite => !reading;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer) => reading ? pipe.Read(buffer) : throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (reading) throw new NotSupportedException();
            pipe.Write(buffer);
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            pipe.Close();
            base.Dispose(disposing);
        }
    }
}
