using System.Text;
using caTTY.Core.Parsing;

namespace caTTY.Core.Terminal.EmulatorOps;

/// <summary>
///     Handles input operations for the terminal emulator.
///     Extracted from TerminalEmulator to reduce file size and improve maintainability.
/// </summary>
internal class TerminalInputOps
{
    private readonly Func<Parser> _getParser;
    private readonly Action _throwIfDisposed;
    private readonly Action _onScreenUpdated;

    /// <summary>
    ///     Creates a new input operations handler.
    /// </summary>
    /// <param name="getParser">Function to get the parser instance for processing input bytes</param>
    /// <param name="throwIfDisposed">Action to check if terminal is disposed</param>
    /// <param name="onScreenUpdated">Action to invoke when screen is updated</param>
    public TerminalInputOps(Func<Parser> getParser, Action throwIfDisposed, Action onScreenUpdated)
    {
        _getParser = getParser;
        _throwIfDisposed = throwIfDisposed;
        _onScreenUpdated = onScreenUpdated;
    }

    /// <summary>
    ///     Processes raw byte data from a shell or other source.
    ///     Can be called with partial chunks and in rapid succession.
    /// </summary>
    /// <param name="data">The raw byte data to process</param>
    public void Write(ReadOnlySpan<byte> data)
    {
        _throwIfDisposed();

        if (data.IsEmpty)
        {
            return;
        }

        // Use parser for proper UTF-8 decoding and escape sequence handling
        _getParser().PushBytes(data);

        // Notify that the screen has been updated
        _onScreenUpdated();
    }

    /// <summary>
    ///     Processes string data by converting to UTF-8 bytes.
    /// </summary>
    /// <param name="text">The text to process</param>
    public void Write(string text)
    {
        _throwIfDisposed();

        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        // Convert string to UTF-8 bytes and process
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        Write(bytes.AsSpan());
    }

    /// <summary>
    ///     Flushes any incomplete UTF-8 sequences in the parser.
    ///     This should be called when no more input is expected to ensure
    ///     incomplete sequences are handled gracefully.
    /// </summary>
    public void FlushIncompleteSequences()
    {
        _throwIfDisposed();
        _getParser().FlushIncompleteSequences();
        _onScreenUpdated();
    }
}
