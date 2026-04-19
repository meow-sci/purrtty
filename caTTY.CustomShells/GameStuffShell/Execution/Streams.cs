using System.Text;
using System.Threading.Channels;

namespace caTTY.CustomShells.GameStuffShell.Execution;

/// <summary>
/// Represents a set of streams (stdin, stdout, stderr) for a program.
/// </summary>
public sealed class StreamSet
{
    /// <summary>
    /// Gets the stdin reader.
    /// </summary>
    public IStreamReader Stdin { get; }

    /// <summary>
    /// Gets the stdout writer.
    /// </summary>
    public IStreamWriter Stdout { get; }

    /// <summary>
    /// Gets the stderr writer.
    /// </summary>
    public IStreamWriter Stderr { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="StreamSet"/> class.
    /// </summary>
    public StreamSet(IStreamReader stdin, IStreamWriter stdout, IStreamWriter stderr)
    {
        Stdin = stdin;
        Stdout = stdout;
        Stderr = stderr;
    }
}

/// <summary>
/// Interface for reading from a stream (UTF-8 text only).
/// </summary>
public interface IStreamReader
{
    /// <summary>
    /// Reads all available text from the stream.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The read text.</returns>
    Task<string> ReadAllAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Interface for writing to a stream (UTF-8 text only).
/// </summary>
public interface IStreamWriter
{
    /// <summary>
    /// Writes text to the stream.
    /// </summary>
    /// <param name="text">The text to write.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task WriteAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>
    /// Flushes any buffered data.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task FlushAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// A stream reader that reads from an in-memory buffer.
/// </summary>
public sealed class BufferedStreamReader : IStreamReader
{
    private readonly string _content;

    /// <summary>
    /// Initializes a new instance of the <see cref="BufferedStreamReader"/> class.
    /// </summary>
    /// <param name="content">The buffered content.</param>
    public BufferedStreamReader(string content)
    {
        _content = content;
    }

    /// <inheritdoc/>
    public Task<string> ReadAllAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_content);
    }
}

/// <summary>
/// A stream reader that always returns empty content.
/// </summary>
public sealed class EmptyStreamReader : IStreamReader
{
    /// <summary>
    /// Gets the singleton instance.
    /// </summary>
    public static EmptyStreamReader Instance { get; } = new();

    private EmptyStreamReader()
    {
    }

    /// <inheritdoc/>
    public Task<string> ReadAllAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(string.Empty);
    }
}

/// <summary>
/// A stream writer that buffers writes to an in-memory string builder.
/// </summary>
public sealed class BufferedStreamWriter : IStreamWriter
{
    private readonly StringBuilder _buffer = new();

    /// <summary>
    /// Gets the buffered content.
    /// </summary>
    public string GetContent() => _buffer.ToString();

    /// <inheritdoc/>
    public Task WriteAsync(string text, CancellationToken cancellationToken = default)
    {
        _buffer.Append(text);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task FlushAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}

/// <summary>
/// A stream writer that discards all writes (like /dev/null).
/// </summary>
public sealed class NullStreamWriter : IStreamWriter
{
    /// <summary>
    /// Gets the singleton instance.
    /// </summary>
    public static NullStreamWriter Instance { get; } = new();

    private NullStreamWriter()
    {
    }

    /// <inheritdoc/>
    public Task WriteAsync(string text, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task FlushAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}

/// <summary>
/// A stream writer that writes to the terminal's output (stdout or stderr).
/// </summary>
public sealed class TerminalStreamWriter : IStreamWriter
{
    private readonly Action<string, bool> _writeCallback;
    private readonly bool _isError;

    /// <summary>
    /// Initializes a new instance of the <see cref="TerminalStreamWriter"/> class.
    /// </summary>
    /// <param name="writeCallback">Callback to write to terminal (text, isError).</param>
    /// <param name="isError">Whether this writer is for stderr.</param>
    public TerminalStreamWriter(Action<string, bool> writeCallback, bool isError)
    {
        _writeCallback = writeCallback;
        _isError = isError;
    }

    /// <inheritdoc/>
    public Task WriteAsync(string text, CancellationToken cancellationToken = default)
    {
        _writeCallback(text, _isError);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task FlushAsync(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
