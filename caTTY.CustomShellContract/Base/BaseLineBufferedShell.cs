using System.Text;

namespace caTTY.Core.Terminal;

/// <summary>
///     Base class for custom shells that implement line buffering with command history
///     and escape sequence handling. Extends BaseChannelOutputShell with input processing
///     features like backspace, enter, arrow key navigation, and Ctrl+L clear screen.
/// </summary>
public abstract class BaseLineBufferedShell : BaseChannelOutputShell
{
    /// <summary>
    ///     Current line being edited by the user.
    /// </summary>
    private readonly StringBuilder _lineBuffer = new();

    /// <summary>
    ///     Command history storage.
    /// </summary>
    private readonly List<string> _commandHistory = new();

    /// <summary>
    ///     Current position in command history (-1 means not navigating).
    /// </summary>
    private int _historyIndex = -1;

    /// <summary>
    ///     Saved current line when navigating history.
    /// </summary>
    private string _savedCurrentLine = string.Empty;

    /// <summary>
    ///     Current cursor position in the line buffer (0-indexed).
    /// </summary>
    private int _cursorPosition = 0;

    /// <summary>
    ///     Escape sequence state machine states.
    /// </summary>
    private enum EscapeState
    {
        /// <summary>Normal character processing</summary>
        None,
        /// <summary>ESC received, waiting for next byte</summary>
        Escape,
        /// <summary>CSI sequence in progress (ESC [ received)</summary>
        Csi
    }

    /// <summary>
    ///     Current escape sequence processing state.
    /// </summary>
    private EscapeState _escapeState = EscapeState.None;

    /// <summary>
    ///     Buffer for collecting CSI sequence parameters.
    /// </summary>
    private readonly StringBuilder _escapeBuffer = new();

    /// <summary>
    ///     Terminal width in columns.
    /// </summary>
    private int _terminalWidth = 80;

    /// <summary>
    ///     Terminal height in rows.
    /// </summary>
    private int _terminalHeight = 24;

    // Control character constants
    /// <summary>Ctrl+C (cancel line)</summary>
    protected const byte CtrlC = 0x03;
    /// <summary>Ctrl+H (Ctrl+Backspace - delete previous word)</summary>
    protected const byte CtrlH = 0x08;
    /// <summary>Ctrl+L (clear screen)</summary>
    protected const byte CtrlL = 0x0C;
    /// <summary>Ctrl+W (delete previous word)</summary>
    protected const byte CtrlW = 0x17;
    /// <summary>Carriage return (Enter key)</summary>
    protected const byte CarriageReturn = 0x0D;
    /// <summary>Line feed</summary>
    protected const byte LineFeed = 0x0A;
    /// <summary>Backspace (DEL)</summary>
    protected const byte Backspace = 0x7F;

    /// <summary>
    ///     Gets the command history for testing/inspection purposes.
    /// </summary>
    protected IReadOnlyList<string> CommandHistory => _commandHistory;

    /// <summary>
    ///     Gets the current line buffer content for testing/inspection purposes.
    /// </summary>
    protected string CurrentLine
    {
        get
        {
            lock (_lock)
            {
                return _lineBuffer.ToString();
            }
        }
    }

    /// <summary>
    ///     Gets the current cursor position for testing/inspection purposes.
    /// </summary>
    protected int CursorPosition => _cursorPosition;

    /// <summary>
    ///     Abstract method called when user presses Enter to execute a command.
    /// </summary>
    /// <param name="commandLine">The command line to execute (trimmed)</param>
    protected abstract void ExecuteCommandLine(string commandLine);

    /// <summary>
    ///     Abstract method called when user presses Ctrl+L to clear the screen.
    ///     Default behavior should send ESC[2J ESC[H.
    /// </summary>
    protected abstract void HandleClearScreen();

    /// <summary>
    ///     Abstract method to get the current prompt string.
    /// </summary>
    /// <returns>The prompt string to display</returns>
    protected abstract string GetPrompt();

    /// <inheritdoc />
    public override Task WriteInputAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        if (!IsRunning)
        {
            throw new InvalidOperationException("Shell is not running");
        }

        var bytes = data.Span;

        for (int i = 0; i < bytes.Length; i++)
        {
            var b = bytes[i];

            // Escape sequence state machine
            switch (_escapeState)
            {
                case EscapeState.None:
                    if (b == 0x1B) // ESC
                    {
                        _escapeState = EscapeState.Escape;
                        _escapeBuffer.Clear();
                    }
                    else
                    {
                        HandleNormalByte(b);
                    }
                    break;

                case EscapeState.Escape:
                    if (b == '[')
                    {
                        _escapeState = EscapeState.Csi;
                        _escapeBuffer.Clear();
                    }
                    else if (b == 'b')
                    {
                        // ESC b - Ctrl+Left (move to previous word)
                        MoveCursorToPreviousWord();
                        _escapeState = EscapeState.None;
                    }
                    else if (b == 'f')
                    {
                        // ESC f - Ctrl+Right (move to next word)
                        MoveCursorToNextWord();
                        _escapeState = EscapeState.None;
                    }
                    else
                    {
                        // Unknown escape sequence, reset
                        _escapeState = EscapeState.None;
                    }
                    break;

                case EscapeState.Csi:
                    if (b >= 0x40 && b <= 0x7E) // Final byte
                    {
                        HandleCsiSequence((char)b);
                        _escapeState = EscapeState.None;
                    }
                    else
                    {
                        _escapeBuffer.Append((char)b);
                    }
                    break;
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    ///     Handles a CSI sequence final byte.
    /// </summary>
    /// <param name="finalByte">The final byte of the CSI sequence</param>
    private void HandleCsiSequence(char finalByte)
    {
        string param = _escapeBuffer.ToString();

        // Handle parameterized sequences
        if (finalByte == '~')
        {
            if (param == "3")
            {
                DeleteCharacterAtCursor();
            }
            return;
        }

        // Check for Ctrl modifier (1;5)
        bool ctrlModifier = param.StartsWith("1;5");

        // Handle simple sequences
        switch (finalByte)
        {
            case 'A': // Up arrow
                NavigateHistoryUp();
                break;
            case 'B': // Down arrow
                NavigateHistoryDown();
                break;
            case 'C': // Right arrow
                if (ctrlModifier)
                    MoveCursorToNextWord();
                else
                    MoveCursorRight();
                break;
            case 'D': // Left arrow
                if (ctrlModifier)
                    MoveCursorToPreviousWord();
                else
                    MoveCursorLeft();
                break;
            case 'H': // Home
                MoveCursorToStart();
                break;
            case 'F': // End
                MoveCursorToEnd();
                break;
            // Other CSI sequences can be added here in the future
        }
    }

    /// <summary>
    ///     Handles a normal (non-escape) byte.
    /// </summary>
    /// <param name="b">The byte to handle</param>
    private void HandleNormalByte(byte b)
    {
        // Handle special control characters
        if (b == CtrlC)
        {
            // Ctrl+C: Cancel line
            HandleCancelLine();
            return;
        }
        else if (b == CtrlW)
        {
            // Ctrl+W: Delete previous word
            HandleDeleteWord();
            return;
        }
        else if (b == CtrlH)
        {
            // Ctrl+H (Ctrl+Backspace): Delete previous word
            HandleDeleteWord();
            return;
        }
        else if (b == CtrlL)
        {
            // Ctrl+L: Clear screen
            HandleClearScreen();
            SendPrompt();
        }
        else if (b == CarriageReturn || b == LineFeed)
        {
            // Enter: Execute command
            string commandLine;
            lock (_lock)
            {
                commandLine = _lineBuffer.ToString();
                _lineBuffer.Clear();
                _cursorPosition = 0;
            }

            // Echo newline
            SendOutput("\r\n");

            // Execute command if not empty
            if (!string.IsNullOrWhiteSpace(commandLine))
            {
                string trimmedCommand = commandLine.Trim();

                // Add to history (avoid consecutive duplicates)
                if (_commandHistory.Count == 0 || _commandHistory[^1] != trimmedCommand)
                {
                    _commandHistory.Add(trimmedCommand);
                }

                // Reset history navigation
                _historyIndex = -1;
                _savedCurrentLine = string.Empty;

                ExecuteCommandLine(trimmedCommand);
            }
            else
            {
                // Empty command, just show new prompt
                _historyIndex = -1;
                _savedCurrentLine = string.Empty;
                SendPrompt();
            }
        }
        else if (b == Backspace)
        {
            // Backspace (0x7F): Remove character before cursor
            lock (_lock)
            {
                if (_cursorPosition == 0)
                {
                    // At start - do nothing
                    return;
                }
                else if (_cursorPosition == _lineBuffer.Length)
                {
                    // At end - current behavior
                    _lineBuffer.Length--;
                    _cursorPosition--;
                    SendOutput("\x1b[D\x1b[K");
                }
                else
                {
                    // Mid-line - delete and redraw tail
                    _lineBuffer.Remove(_cursorPosition - 1, 1);
                    _cursorPosition--;

                    string tail = _lineBuffer.ToString(_cursorPosition, _lineBuffer.Length - _cursorPosition);
                    SendOutput($"\x1b[D{tail} \x1b[{tail.Length + 1}D");
                }
            }
        }
        else if (b >= 0x20 && b < 0x7F)
        {
            // Printable ASCII character
            lock (_lock)
            {
                if (_cursorPosition == _lineBuffer.Length)
                {
                    // At end - append (current behavior)
                    _lineBuffer.Append((char)b);
                    SendOutput(new byte[] { b });
                }
                else
                {
                    // Mid-line - insert
                    _lineBuffer.Insert(_cursorPosition, (char)b);

                    // Redraw: insert char + tail + move cursor back
                    string tail = _lineBuffer.ToString(_cursorPosition + 1, _lineBuffer.Length - _cursorPosition - 1);
                    SendOutput($"{(char)b}{tail}\x1b[{tail.Length}D");
                }
                _cursorPosition++;
            }
        }
        // Note: We ignore other control characters and non-ASCII bytes for now
        // A more sophisticated implementation would handle UTF-8 multi-byte sequences
    }

    /// <summary>
    ///     Navigates to the previous command in history.
    /// </summary>
    private void NavigateHistoryUp()
    {
        if (_commandHistory.Count == 0)
        {
            return;
        }

        lock (_lock)
        {
            // Save current line if starting navigation
            if (_historyIndex == -1)
            {
                _savedCurrentLine = _lineBuffer.ToString();
                _historyIndex = _commandHistory.Count;
            }

            // Move up in history
            if (_historyIndex > 0)
            {
                _historyIndex--;
                ReplaceLineBuffer(_commandHistory[_historyIndex]);
            }
        }
    }

    /// <summary>
    ///     Navigates to the next command in history.
    /// </summary>
    private void NavigateHistoryDown()
    {
        if (_historyIndex == -1)
        {
            return; // Not navigating
        }

        lock (_lock)
        {
            _historyIndex++;

            if (_historyIndex >= _commandHistory.Count)
            {
                // Past end of history, restore saved line
                _historyIndex = -1;
                ReplaceLineBuffer(_savedCurrentLine);
                _savedCurrentLine = string.Empty;
            }
            else
            {
                ReplaceLineBuffer(_commandHistory[_historyIndex]);
            }
        }
    }

    /// <summary>
    ///     Moves the cursor one position to the right within the line buffer.
    /// </summary>
    private void MoveCursorRight()
    {
        lock (_lock)
        {
            if (_cursorPosition < _lineBuffer.Length)
            {
                _cursorPosition++;
                SendOutput("\x1b[C");
            }
        }
    }

    /// <summary>
    ///     Moves the cursor one position to the left within the line buffer.
    /// </summary>
    private void MoveCursorLeft()
    {
        lock (_lock)
        {
            if (_cursorPosition > 0)
            {
                _cursorPosition--;
                SendOutput("\x1b[D");
            }
        }
    }

    /// <summary>
    ///     Moves the cursor to the start of the line buffer.
    /// </summary>
    private void MoveCursorToStart()
    {
        lock (_lock)
        {
            int distance = _cursorPosition;
            if (distance > 0)
            {
                _cursorPosition = 0;
                SendOutput($"\x1b[{distance}D");
            }
        }
    }

    /// <summary>
    ///     Moves the cursor to the end of the line buffer.
    /// </summary>
    private void MoveCursorToEnd()
    {
        lock (_lock)
        {
            int distance = _lineBuffer.Length - _cursorPosition;
            if (distance > 0)
            {
                _cursorPosition = _lineBuffer.Length;
                SendOutput($"\x1b[{distance}C");
            }
        }
    }

    /// <summary>
    ///     Moves the cursor to the start of the next word (Ctrl+Right).
    /// </summary>
    private void MoveCursorToNextWord()
    {
        lock (_lock)
        {
            if (_cursorPosition >= _lineBuffer.Length)
            {
                return;
            }

            int pos = _cursorPosition;

            // Skip current word (non-whitespace characters)
            while (pos < _lineBuffer.Length && !char.IsWhiteSpace(_lineBuffer[pos]))
                pos++;

            // Skip whitespace
            while (pos < _lineBuffer.Length && char.IsWhiteSpace(_lineBuffer[pos]))
                pos++;

            int distance = pos - _cursorPosition;
            if (distance > 0)
            {
                _cursorPosition = pos;
                SendOutput($"\x1b[{distance}C");
            }
        }
    }

    /// <summary>
    ///     Moves the cursor to the start of the previous word (Ctrl+Left).
    /// </summary>
    private void MoveCursorToPreviousWord()
    {
        lock (_lock)
        {
            if (_cursorPosition == 0)
            {
                return;
            }

            int wordStart = FindPreviousWordBoundary();
            int distance = _cursorPosition - wordStart;

            if (distance > 0)
            {
                _cursorPosition = wordStart;
                SendOutput($"\x1b[{distance}D");
            }
        }
    }

    /// <summary>
    ///     Deletes the character at the current cursor position (forward deletion).
    /// </summary>
    private void DeleteCharacterAtCursor()
    {
        lock (_lock)
        {
            if (_cursorPosition >= _lineBuffer.Length)
            {
                // At end - nothing to delete
                return;
            }

            // Delete character at cursor position
            _lineBuffer.Remove(_cursorPosition, 1);

            // Redraw: output tail + space + move cursor back
            string tail = _lineBuffer.ToString(_cursorPosition, _lineBuffer.Length - _cursorPosition);
            SendOutput($"{tail} \x1b[{tail.Length + 1}D");
        }
    }

    /// <summary>
    ///     Replaces the current line buffer with new text and redraws the line.
    /// </summary>
    /// <param name="newText">The new text for the line buffer</param>
    private void ReplaceLineBuffer(string newText)
    {
        // Clear current line: move to start, erase to end
        SendOutput($"\r{GetPrompt()}\x1b[K");

        // Update buffer
        _lineBuffer.Clear();
        _lineBuffer.Append(newText);
        _cursorPosition = _lineBuffer.Length;

        // Display new text
        SendOutput(newText);
    }

    /// <summary>
    ///     Handles Ctrl+C to cancel the current line and start fresh.
    /// </summary>
    private void HandleCancelLine()
    {
        lock (_lock)
        {
            _lineBuffer.Clear();
            _cursorPosition = 0;
            _historyIndex = -1;
            _savedCurrentLine = string.Empty;
        }

        SendOutput("^C\r\n");
        SendOutput(GetPrompt());
    }

    /// <summary>
    ///     Handles Ctrl+W to delete the previous word.
    /// </summary>
    private void HandleDeleteWord()
    {
        lock (_lock)
        {
            int wordStart = FindPreviousWordBoundary();
            if (wordStart < _cursorPosition)
            {
                int deleteCount = _cursorPosition - wordStart;
                _lineBuffer.Remove(wordStart, deleteCount);
                _cursorPosition = wordStart;

                // Redraw from cursor to end
                string tail = _lineBuffer.ToString(_cursorPosition, _lineBuffer.Length - _cursorPosition);
                SendOutput($"\x1b[{deleteCount}D{tail}{new string(' ', deleteCount)}\x1b[{tail.Length + deleteCount}D");
            }
        }
    }

    /// <summary>
    ///     Finds the start position of the previous word for Ctrl+W deletion.
    /// </summary>
    /// <returns>The position where the previous word starts</returns>
    private int FindPreviousWordBoundary()
    {
        if (_cursorPosition == 0) return 0;

        int pos = _cursorPosition - 1;

        // Skip trailing whitespace
        while (pos > 0 && char.IsWhiteSpace(_lineBuffer[pos]))
            pos--;

        // If we're at position 0 and it's whitespace, delete everything
        if (pos == 0 && char.IsWhiteSpace(_lineBuffer[0]))
            return 0;

        // Skip word characters
        while (pos > 0 && !char.IsWhiteSpace(_lineBuffer[pos]))
            pos--;

        // If we stopped at whitespace (not at start), move forward one
        if (pos > 0)
            pos++;

        return pos;
    }

    /// <summary>
    ///     Sends the command prompt to the terminal.
    /// </summary>
    protected void SendPrompt()
    {
        SendOutput(GetPrompt());
    }

    /// <summary>
    ///     Sends text output to the terminal via the channel-based output pump.
    ///     Output is sent as stdout.
    /// </summary>
    /// <param name="text">The text to send</param>
    protected void SendOutput(string text)
    {
        QueueOutput(text, ShellOutputType.Stdout);
    }

    /// <summary>
    ///     Sends raw byte data to the terminal via the channel-based output pump.
    ///     Output is sent as stdout.
    /// </summary>
    /// <param name="data">The data to send</param>
    protected void SendOutput(byte[] data)
    {
        QueueOutput(data, ShellOutputType.Stdout);
    }

    /// <summary>
    ///     Sends text error output to the terminal via the channel-based output pump.
    ///     Output is sent as stderr.
    /// </summary>
    /// <param name="text">The error text to send</param>
    protected void SendError(string text)
    {
        QueueOutput(text, ShellOutputType.Stderr);
    }

    /// <summary>
    ///     Sends raw byte error data to the terminal via the channel-based output pump.
    ///     Output is sent as stderr.
    /// </summary>
    /// <param name="data">The error data to send</param>
    protected void SendError(byte[] data)
    {
        QueueOutput(data, ShellOutputType.Stderr);
    }

    /// <inheritdoc />
    public override void NotifyTerminalResize(int width, int height)
    {
        lock (_lock)
        {
            _terminalWidth = width;
            _terminalHeight = height;
        }
    }
}
