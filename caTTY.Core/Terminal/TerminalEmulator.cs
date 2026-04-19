using caTTY.Core.Parsing;
using caTTY.Core.Types;
using caTTY.Core.Managers;
using caTTY.Core.Tracing;
using caTTY.Core.Rpc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace caTTY.Core.Terminal;

/// <summary>
///     Core terminal emulator implementation that processes raw byte data and maintains screen state.
///     This is a headless implementation with no UI dependencies.
/// </summary>
public class TerminalEmulator : ITerminalEmulator, ICursorPositionProvider
{
    private ILogger _logger;
    private Parser _parser;
    private IScreenBufferManager _screenBufferManager;
    private ICursorManager _cursorManager;
    private IModeManager _modeManager;
    private IAttributeManager _attributeManager;
    private IScrollbackManager _scrollbackManager;
    private IScrollbackBuffer _scrollbackBuffer;
    private IAlternateScreenManager _alternateScreenManager;
    private ICharacterSetManager _characterSetManager;

    // Operation classes
    private EmulatorOps.TerminalViewportOps _viewportOps;
    private EmulatorOps.TerminalResizeOps _resizeOps;
    private EmulatorOps.TerminalCursorMovementOps _cursorMovementOps;
    private EmulatorOps.TerminalCursorSaveRestoreOps _cursorSaveRestoreOps;
    private EmulatorOps.TerminalCursorStyleOps _cursorStyleOps;
    private EmulatorOps.TerminalEraseInDisplayOps _eraseInDisplayOps;
    private EmulatorOps.TerminalEraseInLineOps _eraseInLineOps;
    private EmulatorOps.TerminalSelectiveEraseInDisplayOps _selectiveEraseInDisplayOps;
    private EmulatorOps.TerminalSelectiveEraseInLineOps _selectiveEraseInLineOps;
    private EmulatorOps.TerminalScrollOps _scrollOps;
    private EmulatorOps.TerminalScrollRegionOps _scrollRegionOps;
    private EmulatorOps.TerminalInsertLinesOps _insertLinesOps;
    private EmulatorOps.TerminalDeleteLinesOps _deleteLinesOps;
    private EmulatorOps.TerminalInsertCharsOps _insertCharsOps;
    private EmulatorOps.TerminalDeleteCharsOps _deleteCharsOps;
    private EmulatorOps.TerminalEraseCharsOps _eraseCharsOps;
    private EmulatorOps.TerminalInsertModeOps _insertModeOps;
    private EmulatorOps.TerminalAlternateScreenOps _alternateScreenOps;
    private EmulatorOps.TerminalDecModeOps _decModeOps;
    private EmulatorOps.TerminalPrivateModesOps _privateModesOps;
    private EmulatorOps.TerminalBracketedPasteOps _bracketedPasteOps;
    private EmulatorOps.TerminalOscTitleIconOps _oscTitleIconOps;
    private EmulatorOps.TerminalOscWindowManipulationOps _oscWindowManipulationOps;
    private EmulatorOps.TerminalOscClipboardOps _oscClipboardOps;
    private EmulatorOps.TerminalOscHyperlinkOps _oscHyperlinkOps;
    private EmulatorOps.TerminalOscColorQueryOps _oscColorQueryOps;
    private EmulatorOps.TerminalCharsetDesignationOps _charsetDesignationOps;
    private EmulatorOps.TerminalCharsetTranslationOps _charsetTranslationOps;
    private EmulatorOps.TerminalLineFeedOps _lineFeedOps;
    private EmulatorOps.TerminalIndexOps _indexOps;
    private EmulatorOps.TerminalCarriageReturnOps _carriageReturnOps;
    private EmulatorOps.TerminalBellOps _bellOps;
    private EmulatorOps.TerminalBackspaceOps _backspaceOps;
    private EmulatorOps.TerminalTabOps _tabOps;
    private EmulatorOps.TerminalResponseOps _responseOps;
    private EmulatorOps.TerminalScreenUpdateOps _screenUpdateOps;
    private EmulatorOps.TerminalTitleIconEventsOps _titleIconEventsOps;
    private EmulatorOps.TerminalInputOps _inputOps;
    private EmulatorOps.TerminalResetOps _resetOps;

    // Optional RPC components for game integration
    private IRpcHandler? _rpcHandler;

    private bool _disposed;

    /// <summary>
    ///     Internal constructor used by builder.
    /// </summary>
    internal TerminalEmulator()
    {
        // Fields will be initialized by Initialize method called from builder
        // Suppress nullability warnings - fields are initialized immediately by builder
        State = null!;
        ScreenBuffer = null!;
        Cursor = null!;
        _scrollbackBuffer = null!;
        _scrollbackManager = null!;
        _screenBufferManager = null!;
        _cursorManager = null!;
        _modeManager = null!;
        _attributeManager = null!;
        _alternateScreenManager = null!;
        _characterSetManager = null!;
        _viewportOps = null!;
        _resizeOps = null!;
        _cursorMovementOps = null!;
        _cursorSaveRestoreOps = null!;
        _cursorStyleOps = null!;
        _eraseInDisplayOps = null!;
        _eraseInLineOps = null!;
        _selectiveEraseInDisplayOps = null!;
        _selectiveEraseInLineOps = null!;
        _scrollOps = null!;
        _scrollRegionOps = null!;
        _insertLinesOps = null!;
        _deleteLinesOps = null!;
        _insertCharsOps = null!;
        _deleteCharsOps = null!;
        _eraseCharsOps = null!;
        _insertModeOps = null!;
        _alternateScreenOps = null!;
        _decModeOps = null!;
        _privateModesOps = null!;
        _bracketedPasteOps = null!;
        _oscTitleIconOps = null!;
        _oscWindowManipulationOps = null!;
        _oscClipboardOps = null!;
        _oscHyperlinkOps = null!;
        _oscColorQueryOps = null!;
        _charsetDesignationOps = null!;
        _charsetTranslationOps = null!;
        _lineFeedOps = null!;
        _indexOps = null!;
        _carriageReturnOps = null!;
        _bellOps = null!;
        _backspaceOps = null!;
        _tabOps = null!;
        _responseOps = null!;
        _screenUpdateOps = null!;
        _titleIconEventsOps = null!;
        _inputOps = null!;
        _resetOps = null!;
        _parser = null!;
        _logger = null!;
    }

    /// <summary>
    ///     Initializes all fields. Called by TerminalEmulatorBuilder after construction.
    /// </summary>
    internal void Initialize(
        ICursor cursor,
        TerminalState state,
        IScreenBuffer screenBuffer,
        IScrollbackBuffer scrollbackBuffer,
        IScrollbackManager scrollbackManager,
        IScreenBufferManager screenBufferManager,
        ICursorManager cursorManager,
        IModeManager modeManager,
        IAttributeManager attributeManager,
        IAlternateScreenManager alternateScreenManager,
        ICharacterSetManager characterSetManager,
        EmulatorOps.TerminalViewportOps viewportOps,
        EmulatorOps.TerminalResizeOps resizeOps,
        EmulatorOps.TerminalCursorMovementOps cursorMovementOps,
        EmulatorOps.TerminalCursorSaveRestoreOps cursorSaveRestoreOps,
        EmulatorOps.TerminalCursorStyleOps cursorStyleOps,
        EmulatorOps.TerminalEraseInDisplayOps eraseInDisplayOps,
        EmulatorOps.TerminalEraseInLineOps eraseInLineOps,
        EmulatorOps.TerminalSelectiveEraseInDisplayOps selectiveEraseInDisplayOps,
        EmulatorOps.TerminalSelectiveEraseInLineOps selectiveEraseInLineOps,
        EmulatorOps.TerminalScrollOps scrollOps,
        EmulatorOps.TerminalScrollRegionOps scrollRegionOps,
        EmulatorOps.TerminalInsertLinesOps insertLinesOps,
        EmulatorOps.TerminalDeleteLinesOps deleteLinesOps,
        EmulatorOps.TerminalInsertCharsOps insertCharsOps,
        EmulatorOps.TerminalDeleteCharsOps deleteCharsOps,
        EmulatorOps.TerminalEraseCharsOps eraseCharsOps,
        EmulatorOps.TerminalInsertModeOps insertModeOps,
        EmulatorOps.TerminalAlternateScreenOps alternateScreenOps,
        EmulatorOps.TerminalDecModeOps decModeOps,
        EmulatorOps.TerminalPrivateModesOps privateModesOps,
        EmulatorOps.TerminalBracketedPasteOps bracketedPasteOps,
        EmulatorOps.TerminalOscTitleIconOps oscTitleIconOps,
        EmulatorOps.TerminalOscWindowManipulationOps oscWindowManipulationOps,
        EmulatorOps.TerminalOscClipboardOps oscClipboardOps,
        EmulatorOps.TerminalOscHyperlinkOps oscHyperlinkOps,
        EmulatorOps.TerminalOscColorQueryOps oscColorQueryOps,
        EmulatorOps.TerminalCharsetDesignationOps charsetDesignationOps,
        EmulatorOps.TerminalCharsetTranslationOps charsetTranslationOps,
        EmulatorOps.TerminalLineFeedOps lineFeedOps,
        EmulatorOps.TerminalIndexOps indexOps,
        EmulatorOps.TerminalCarriageReturnOps carriageReturnOps,
        EmulatorOps.TerminalBellOps bellOps,
        EmulatorOps.TerminalBackspaceOps backspaceOps,
        EmulatorOps.TerminalTabOps tabOps,
        EmulatorOps.TerminalResponseOps responseOps,
        EmulatorOps.TerminalScreenUpdateOps screenUpdateOps,
        EmulatorOps.TerminalTitleIconEventsOps titleIconEventsOps,
        EmulatorOps.TerminalInputOps inputOps,
        EmulatorOps.TerminalResetOps resetOps,
        Parser parser,
        IRpcHandler? rpcHandler,
        ILogger logger)
    {
        Cursor = cursor;
        State = state;
        ScreenBuffer = screenBuffer;
        _scrollbackBuffer = scrollbackBuffer;
        _scrollbackManager = scrollbackManager;
        _screenBufferManager = screenBufferManager;
        _cursorManager = cursorManager;
        _modeManager = modeManager;
        _attributeManager = attributeManager;
        _alternateScreenManager = alternateScreenManager;
        _characterSetManager = characterSetManager;
        _viewportOps = viewportOps;
        _resizeOps = resizeOps;
        _cursorMovementOps = cursorMovementOps;
        _cursorSaveRestoreOps = cursorSaveRestoreOps;
        _cursorStyleOps = cursorStyleOps;
        _eraseInDisplayOps = eraseInDisplayOps;
        _eraseInLineOps = eraseInLineOps;
        _selectiveEraseInDisplayOps = selectiveEraseInDisplayOps;
        _selectiveEraseInLineOps = selectiveEraseInLineOps;
        _scrollOps = scrollOps;
        _scrollRegionOps = scrollRegionOps;
        _insertLinesOps = insertLinesOps;
        _deleteLinesOps = deleteLinesOps;
        _insertCharsOps = insertCharsOps;
        _deleteCharsOps = deleteCharsOps;
        _eraseCharsOps = eraseCharsOps;
        _insertModeOps = insertModeOps;
        _alternateScreenOps = alternateScreenOps;
        _decModeOps = decModeOps;
        _privateModesOps = privateModesOps;
        _bracketedPasteOps = bracketedPasteOps;
        _oscTitleIconOps = oscTitleIconOps;
        _oscWindowManipulationOps = oscWindowManipulationOps;
        _oscClipboardOps = oscClipboardOps;
        _oscHyperlinkOps = oscHyperlinkOps;
        _oscColorQueryOps = oscColorQueryOps;
        _charsetDesignationOps = charsetDesignationOps;
        _charsetTranslationOps = charsetTranslationOps;
        _lineFeedOps = lineFeedOps;
        _indexOps = indexOps;
        _carriageReturnOps = carriageReturnOps;
        _bellOps = bellOps;
        _backspaceOps = backspaceOps;
        _tabOps = tabOps;
        _responseOps = responseOps;
        _screenUpdateOps = screenUpdateOps;
        _titleIconEventsOps = titleIconEventsOps;
        _inputOps = inputOps;
        _resetOps = resetOps;
        _parser = parser;
        _rpcHandler = rpcHandler;
        _logger = logger;
        _disposed = false;
    }

    /// <summary>
    ///     Creates a new terminal emulator with the specified dimensions.
    ///     Uses TerminalEmulatorBuilder for initialization.
    /// </summary>
    /// <param name="width">Width in columns</param>
    /// <param name="height">Height in rows</param>
    /// <param name="logger">Optional logger for debugging (uses NullLogger if not provided)</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when dimensions are invalid</exception>
    public static TerminalEmulator Create(int width, int height, ILogger? logger = null)
    {
        return TerminalEmulatorBuilder.Build(width, height, 1000, logger, null);
    }

    /// <summary>
    ///     Creates a new terminal emulator with the specified dimensions, scrollback, and optional RPC handler.
    ///     Uses TerminalEmulatorBuilder for initialization.
    /// </summary>
    /// <param name="width">Width in columns</param>
    /// <param name="height">Height in rows</param>
    /// <param name="scrollbackLines">Maximum number of scrollback lines (default: 1000)</param>
    /// <param name="logger">Optional logger for debugging (uses NullLogger if not provided)</param>
    /// <param name="rpcHandler">Optional RPC handler for game integration (null disables RPC functionality)</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when dimensions are invalid</exception>
    public static TerminalEmulator Create(int width, int height, int scrollbackLines, ILogger? logger = null, IRpcHandler? rpcHandler = null, IOscRpcHandler? oscRpcHandler = null)
    {
        return TerminalEmulatorBuilder.Build(width, height, scrollbackLines, logger, rpcHandler, oscRpcHandler);
    }

    /// <summary>
    ///     Gets the current terminal state.
    /// </summary>
    public TerminalState State { get; private set; }

    /// <summary>
    ///     Gets the screen buffer manager for buffer operations.
    /// </summary>
    public IScreenBufferManager ScreenBufferManager => _screenBufferManager;

    /// <summary>
    ///     Gets the cursor manager for cursor operations.
    /// </summary>
    public ICursorManager CursorManager => _cursorManager;

    /// <summary>
    ///     Gets the mode manager for terminal mode operations.
    /// </summary>
    public IModeManager ModeManager => _modeManager;

    /// <summary>
    ///     Gets the attribute manager for SGR attribute operations.
    /// </summary>
    public IAttributeManager AttributeManager => _attributeManager;

    /// <summary>
    ///     Gets the alternate screen manager for buffer switching operations.
    /// </summary>
    public IAlternateScreenManager AlternateScreenManager => _alternateScreenManager;

    /// <summary>
    ///     Gets the width of the terminal in columns.
    /// </summary>
    public int Width => ScreenBuffer.Width;

    /// <summary>
    ///     Gets the height of the terminal in rows.
    /// </summary>
    public int Height => ScreenBuffer.Height;

    /// <summary>
    ///     Gets the current screen buffer for rendering.
    /// </summary>
    public IScreenBuffer ScreenBuffer { get; private set; }

    /// <summary>
    ///     Gets the current cursor state.
    /// </summary>
    public ICursor Cursor { get; private set; }

    /// <summary>
    /// Gets the current cursor row position (0-based) for tracing purposes.
    /// </summary>
    public int Row => _cursorManager.Row;

    /// <summary>
    /// Gets the current cursor column position (0-based) for tracing purposes.
    /// </summary>
    public int Column => _cursorManager.Column;

    /// <summary>
    ///     Gets the scrollback buffer for accessing historical lines.
    /// </summary>
    public IScrollbackBuffer ScrollbackBuffer => _scrollbackBuffer;

    /// <summary>
    ///     Gets the scrollback manager for viewport and scrollback operations.
    /// </summary>
    public IScrollbackManager ScrollbackManager => _scrollbackManager;

    /// <summary>
    ///     Gets whether RPC functionality is enabled for this terminal emulator.
    /// </summary>
    public bool IsRpcEnabled => _rpcHandler != null && _rpcHandler.IsEnabled;

    /// <summary>
    ///     Gets the RPC handler if RPC functionality is enabled, null otherwise.
    /// </summary>
    public IRpcHandler? RpcHandler => _rpcHandler;

    /// <summary>
    ///     Event raised when the screen content has been updated and needs refresh.
    /// </summary>
    public event EventHandler<ScreenUpdatedEventArgs>? ScreenUpdated;

    /// <summary>
    ///     Event raised when the terminal needs to send a response back to the shell.
    /// </summary>
    public event EventHandler<ResponseEmittedEventArgs>? ResponseEmitted;

    /// <summary>
    ///     Event raised when a bell character (BEL, 0x07) is received.
    /// </summary>
    public event EventHandler<BellEventArgs>? Bell;

    /// <summary>
    ///     Event raised when the window title is changed via OSC sequences.
    /// </summary>
#pragma warning disable CS0067 // Event is used via helper method
    public event EventHandler<TitleChangeEventArgs>? TitleChanged;
#pragma warning restore CS0067

    /// <summary>
    ///     Event raised when the icon name is changed via OSC sequences.
    /// </summary>
#pragma warning disable CS0067 // Event is used via helper method
    public event EventHandler<IconNameChangeEventArgs>? IconNameChanged;
#pragma warning restore CS0067

    /// <summary>
    ///     Event raised when a clipboard operation is requested via OSC 52 sequences.
    /// </summary>
    public event EventHandler<ClipboardEventArgs>? ClipboardRequest;

    /// <summary>
    ///     Processes raw byte data from a shell or other source.
    ///     Can be called with partial chunks and in rapid succession.
    /// </summary>
    /// <param name="data">The raw byte data to process</param>
    public void Write(ReadOnlySpan<byte> data)
    {
        _inputOps.Write(data);
    }

    /// <summary>
    ///     Processes string data by converting to UTF-8 bytes.
    /// </summary>
    /// <param name="text">The text to process</param>
    public void Write(string text)
    {
        _inputOps.Write(text);
    }

    /// <summary>
    ///     Resizes the terminal to the specified dimensions.
    ///     Preserves cursor position and updates scrollback during resize operations.
    ///     Uses simple resize policy: height change preserves top-to-bottom rows,
    ///     width change truncates/pads each row without complex reflow.
    /// </summary>
    /// <param name="width">New width in columns</param>
    /// <param name="height">New height in rows</param>
    public void Resize(int width, int height)
    {
        ThrowIfDisposed();
        _resizeOps.Resize(width, height);
    }

    /// <summary>
    ///     Disposes the terminal emulator and releases all resources.
    /// </summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            _scrollbackBuffer?.Dispose();
            (_scrollbackManager as IDisposable)?.Dispose();
            _disposed = true;
        }
    }

    /// <summary>
    ///     Scrolls the viewport up by the specified number of lines.
    ///     Disables auto-scroll if not already at the top.
    /// </summary>
    /// <param name="lines">Number of lines to scroll up</param>
    public void ScrollViewportUp(int lines)
    {
        ThrowIfDisposed();
        _viewportOps.ScrollViewportUp(lines);
    }

    /// <summary>
    ///     Scrolls the viewport down by the specified number of lines.
    ///     Re-enables auto-scroll if scrolled to the bottom.
    /// </summary>
    /// <param name="lines">Number of lines to scroll down</param>
    public void ScrollViewportDown(int lines)
    {
        ThrowIfDisposed();
        _viewportOps.ScrollViewportDown(lines);
    }

    /// <summary>
    ///     Scrolls to the top of the scrollback buffer.
    ///     Disables auto-scroll.
    /// </summary>
    public void ScrollViewportToTop()
    {
        ThrowIfDisposed();
        _viewportOps.ScrollViewportToTop();
    }

    /// <summary>
    ///     Scrolls to the bottom of the scrollback buffer.
    ///     Re-enables auto-scroll.
    /// </summary>
    public void ScrollViewportToBottom()
    {
        ThrowIfDisposed();
        _viewportOps.ScrollViewportToBottom();
    }

    /// <summary>
    ///     Gets whether auto-scroll is currently enabled.
    ///     Auto-scroll is disabled when user scrolls up and re-enabled when they return to bottom.
    /// </summary>
    public bool IsAutoScrollEnabled => _scrollbackManager.AutoScrollEnabled;

    /// <summary>
    ///     Gets the current viewport offset from the bottom.
    ///     0 means viewing the most recent content, positive values mean scrolled up into history.
    /// </summary>
    public int ViewportOffset => _scrollbackManager.ViewportOffset;

    /// <summary>
    ///     Flushes any incomplete UTF-8 sequences in the parser.
    ///     This should be called when no more input is expected to ensure
    ///     incomplete sequences are handled gracefully.
    /// </summary>
    public void FlushIncompleteSequences()
    {
        _inputOps.FlushIncompleteSequences();
    }

    /// <summary>
    ///     Enables or disables RPC functionality at runtime.
    ///     This allows dynamic control over RPC processing without recreating the terminal.
    /// </summary>
    /// <param name="enabled">True to enable RPC processing, false to disable</param>
    /// <returns>True if the setting was applied, false if RPC handler is not available</returns>
    public bool SetRpcEnabled(bool enabled)
    {
        ThrowIfDisposed();

        if (_rpcHandler == null)
        {
            _logger.LogWarning("Cannot set RPC enabled state - no RPC handler available");
            return false;
        }

        bool previousState = _rpcHandler.IsEnabled;
        _rpcHandler.IsEnabled = enabled;

        _logger.LogDebug("RPC functionality {Action} (was {PreviousState})",
            enabled ? "enabled" : "disabled",
            previousState ? "enabled" : "disabled");

        return true;
    }

    /// <summary>
    ///     Handles a line feed (LF) character - move down one line, keeping same column.
    ///     In raw terminal mode, LF only moves down without changing column position.
    ///     Uses cursor manager for proper cursor management.
    /// </summary>
    internal void HandleLineFeed() => _lineFeedOps.HandleLineFeed();

    /// <summary>
    ///     Handles index operation (ESC D) - move cursor down one line without changing column.
    ///     Used by ESC D sequence.
    /// </summary>
    internal void HandleIndex() => _indexOps.HandleIndex();

    /// <summary>
    ///     Handles a carriage return (CR) character - move to column 0.
    ///     Uses cursor manager for proper cursor management.
    /// </summary>
    internal void HandleCarriageReturn() => _carriageReturnOps.HandleCarriageReturn();

    /// <summary>
    ///     Handles a bell character (BEL) - emit bell event for notification.
    /// </summary>
    internal void HandleBell()
    {
        _bellOps.HandleBell();
    }

    /// <summary>
    ///     Sets the window title and emits a title change event.
    ///     Handles empty titles and title reset.
    /// </summary>
    /// <param name="title">The new window title</param>
    internal void SetWindowTitle(string title) => _oscTitleIconOps.SetWindowTitle(title);

    /// <summary>
    ///     Sets the icon name and emits an icon name change event.
    ///     Handles empty icon names and icon name reset.
    /// </summary>
    /// <param name="iconName">The new icon name</param>
    internal void SetIconName(string iconName) => _oscTitleIconOps.SetIconName(iconName);

    /// <summary>
    ///     Sets both window title and icon name to the same value.
    ///     Emits both title change and icon name change events.
    /// </summary>
    /// <param name="title">The new title and icon name</param>
    internal void SetTitleAndIcon(string title) => _oscTitleIconOps.SetTitleAndIcon(title);

    /// <summary>
    ///     Gets the current window title.
    /// </summary>
    /// <returns>The current window title</returns>
    internal string GetWindowTitle() => _oscTitleIconOps.GetWindowTitle();

    /// <summary>
    ///     Gets the current icon name.
    /// </summary>
    /// <returns>The current icon name</returns>
    internal string GetIconName() => _oscTitleIconOps.GetIconName();

    /// <summary>
    ///     Handles window manipulation sequences (CSI Ps t).
    ///     Implements title stack operations for vi compatibility and window size queries.
    ///     Gracefully handles unsupported operations (minimize/restore) in game context.
    /// </summary>
    /// <param name="operation">The window manipulation operation code</param>
    /// <param name="parameters">Additional parameters for the operation</param>
    internal void HandleWindowManipulation(int operation, int[] parameters)
    {
        _oscWindowManipulationOps.HandleWindowManipulation(operation, parameters);
    }

    /// <summary>
    ///     Gets the current foreground color for color queries.
    ///     Returns the current SGR foreground color or default terminal theme color.
    /// </summary>
    /// <returns>RGB color values for the current foreground color</returns>
    internal (byte Red, byte Green, byte Blue) GetCurrentForegroundColor()
        => _oscColorQueryOps.GetCurrentForegroundColor();

    /// <summary>
    ///     Gets the current background color for color queries.
    ///     Returns the current SGR background color or default terminal theme color.
    /// </summary>
    /// <returns>RGB color values for the current background color</returns>
    internal (byte Red, byte Green, byte Blue) GetCurrentBackgroundColor()
        => _oscColorQueryOps.GetCurrentBackgroundColor();


    /// <summary>
    ///     Handles clipboard operations from OSC 52 sequences.
    ///     Parses selection targets and clipboard data, applies safety limits,
    ///     and emits clipboard events for game integration.
    /// </summary>
    /// <param name="payload">The OSC 52 payload (selection;data)</param>
    internal void HandleClipboard(string payload) => _oscClipboardOps.HandleClipboard(payload);

    /// <summary>
    ///     Handles hyperlink operations from OSC 8 sequences.
    ///     Associates URLs with character ranges by setting current hyperlink state.
    ///     Clears hyperlink state when empty URL is provided.
    /// </summary>
    /// <param name="url">The hyperlink URL, or empty string to clear hyperlink state</param>
    internal void HandleHyperlink(string url) => _oscHyperlinkOps.HandleHyperlink(url);

    /// <summary>
    ///     Handles a backspace character (BS) - move cursor one position left if not at column 0.
    ///     Uses cursor manager for proper cursor management.
    /// </summary>
    internal void HandleBackspace() => _backspaceOps.HandleBackspace();

    /// <summary>
    ///     Handles a tab character - move to next tab stop using terminal state.
    /// </summary>
    internal void HandleTab() => _tabOps.HandleTab();

    /// <summary>
    ///     Writes a character at the current cursor position and advances the cursor.
    ///     Implements proper auto-wrap behavior matching TypeScript reference implementation.
    /// </summary>
    /// <param name="character">The character to write</param>
    internal void WriteCharacterAtCursor(char character)
    {
        // Bounds check - ensure we have valid dimensions
        if (Width <= 0 || Height <= 0)
        {
            return;
        }

        // Bounds check - ensure cursor Y is within screen bounds
        if (_cursorManager.Row < 0 || _cursorManager.Row >= Height)
        {
            return;
        }

        // Clamp cursor X to valid range
        if (_cursorManager.Column < 0)
        {
            _cursorManager.MoveTo(_cursorManager.Row, 0);
        }

        // Handle wrap pending state - if set, wrap to next line first
        // This matches TypeScript putChar behavior: wrap pending triggers on next character
        if (_modeManager.AutoWrapMode && _cursorManager.WrapPending)
        {
            _cursorManager.MoveTo(_cursorManager.Row, 0);

            // Move to next line
            if (_cursorManager.Row + 1 >= Height)
            {
                // At bottom - need to scroll up by one line within scroll region
                _screenBufferManager.ScrollUpInRegion(1, State.ScrollTop, State.ScrollBottom, _attributeManager.CurrentAttributes);
                _cursorManager.MoveTo(Height - 1, 0);
            }
            else
            {
                _cursorManager.MoveTo(_cursorManager.Row + 1, 0);
            }

            _cursorManager.SetWrapPending(false);
        }

        // Clamp cursor X to screen bounds (best-effort recovery)
        if (_cursorManager.Column >= Width)
        {
            _cursorManager.MoveTo(_cursorManager.Row, Width - 1);
        }

        // Write the character to the screen buffer with current SGR attributes, protection status, and hyperlink URL
        // Determine if this is a wide character for proper cell marking
        bool isWide = IsWideCharacter(character);

        // Handle insert mode - shift existing characters right if insert mode is enabled
        if (_modeManager.InsertMode)
        {
            // Insert mode: shift existing characters right before writing new character
            int charactersToShift = isWide ? 2 : 1;
            _insertModeOps.ShiftCharactersRight(_cursorManager.Row, _cursorManager.Column, charactersToShift);
        }

        var cell = new Cell(character, _attributeManager.CurrentAttributes, _attributeManager.CurrentCharacterProtection, _attributeManager.CurrentHyperlinkUrl, isWide);
        _screenBufferManager.SetCell(_cursorManager.Row, _cursorManager.Column, cell);

        // For wide characters, also mark the next cell as part of the wide character
        // NOTE: 64e8b6190d4498d3b1cd2e1b3e07e7587e685967 implemented this and it broke cursor positioning entirely on the line discipline, removed it

        // Handle cursor advancement and wrap pending logic
        if (_cursorManager.Column == Width - 1)
        {
            // At right edge - set wrap pending if auto-wrap is enabled
            if (_modeManager.AutoWrapMode)
            {
                _cursorManager.SetWrapPending(true);
            }
            // Cursor stays at right edge (don't advance beyond)
        }
        else
        {
            // Normal advancement - move cursor right
            // For wide characters, advance by 2 if there's space, otherwise treat as normal
            int advanceAmount = 1;
            if (isWide && _cursorManager.Column + 1 < Width - 1)
            {
                // Wide character with room for 2 cells - advance by 2
                advanceAmount = 2;
                // Note: We don't overwrite the next cell, just advance the cursor
                // The rendering system should handle wide character display
            }

            _cursorManager.MoveTo(_cursorManager.Row, _cursorManager.Column + advanceAmount);
        }

        // Sync state with managers
        State.CursorX = _cursorManager.Column;
        State.CursorY = _cursorManager.Row;
        State.WrapPending = _cursorManager.WrapPending;
        State.AutoWrapMode = _modeManager.AutoWrapMode;
    }

    /// <summary>
    ///     Determines if a character is wide (occupies two terminal cells).
    ///     Based on Unicode East Asian Width property for CJK characters.
    /// </summary>
    /// <param name="character">The character to check</param>
    /// <returns>True if the character is wide, false otherwise</returns>
    private static bool IsWideCharacter(char character)
    {

        // For now, disable wide character detection to maintain compatibility with existing tests
        // The existing tests expect CJK characters to be treated as single-width
        // TODO: Implement proper wide character handling based on Unicode East Asian Width property

        // NOTE: 64e8b6190d4498d3b1cd2e1b3e07e7587e685967 implemented this and it broke cursor positioning entirely on the line discipline

        return false;
    }

    /// <summary>
    ///     Emits a response string back to the shell process.
    ///     Used for device queries and other terminal responses.
    /// </summary>
    /// <param name="responseText">The response text to emit</param>
    internal void EmitResponse(string responseText)
    {
        _responseOps.EmitResponse(responseText);
    }

    /// <summary>
    ///     Raises the ScreenUpdated event.
    /// </summary>
    private void OnScreenUpdated()
    {
        _screenUpdateOps.OnScreenUpdated();
    }

    /// <summary>
    ///     Internal helper for builder - gets the parser instance.
    /// </summary>
    internal Parser GetParser() => _parser;

    /// <summary>
    ///     Internal helper for builder - throws if disposed.
    /// </summary>
    internal void ThrowIfDisposedInternal() => ThrowIfDisposed();

    /// <summary>
    ///     Internal helper for builder - calls OnScreenUpdated.
    /// </summary>
    internal void OnScreenUpdatedInternal() => OnScreenUpdated();

    /// <summary>
    ///     Internal helper for builder - raises OnBell event.
    /// </summary>
    internal void OnBellInternal() => OnBell();

    /// <summary>
    ///     Internal helper for builder - raises ScreenUpdated event with args.
    /// </summary>
    internal void OnScreenUpdatedEvent(ScreenUpdatedEventArgs e) => ScreenUpdated?.Invoke(this, e);

    /// <summary>
    ///     Internal helper for builder - raises TitleChanged event with string.
    /// </summary>
    internal void OnTitleChangedEvent(string title) => OnTitleChanged(title);

    /// <summary>
    ///     Internal helper for builder - raises IconNameChanged event with string.
    /// </summary>
    internal void OnIconNameChangedEvent(string iconName) => OnIconNameChanged(iconName);

    /// <summary>
    ///     Internal helper for builder - raises ClipboardRequest event.
    /// </summary>
    internal void OnClipboardRequestInternal(string selectionTarget, string? data, bool isQuery) =>
        OnClipboardRequest(selectionTarget, data, isQuery);

    /// <summary>
    ///     Internal helper for builder - raises ResponseEmitted event with args.
    /// </summary>
    internal void OnResponseEmittedEvent(ResponseEmittedEventArgs e) => ResponseEmitted?.Invoke(this, e);

    /// <summary>
    ///     Internal helper for builder - raises TitleChanged event with event args.
    /// </summary>
    internal void RaiseTitleChanged(TitleChangeEventArgs e) => TitleChanged?.Invoke(this, e);

    /// <summary>
    ///     Internal helper for builder - raises IconNameChanged event with event args.
    /// </summary>
    internal void RaiseIconNameChanged(IconNameChangeEventArgs e) => IconNameChanged?.Invoke(this, e);

    /// <summary>
    ///     Raises the ResponseEmitted event.
    /// </summary>
    /// <param name="responseData">The response data to emit</param>
    protected void OnResponseEmitted(ReadOnlyMemory<byte> responseData)
    {
        _responseOps.OnResponseEmitted(responseData);
    }

    /// <summary>
    ///     Raises the ResponseEmitted event with string data.
    /// </summary>
    /// <param name="responseText">The response text to emit</param>
    protected void OnResponseEmitted(string responseText)
    {
        _responseOps.OnResponseEmitted(responseText);
    }

    /// <summary>
    ///     Handles scroll up sequence (CSI S) - scroll screen up by specified lines.
    ///     Implements CSI Ps S (Scroll Up) sequence.
    /// </summary>
    /// <param name="lines">Number of lines to scroll up (default: 1)</param>
    internal void ScrollScreenUp(int lines = 1)
    {
        _scrollOps.ScrollScreenUp(lines);
    }

    /// <summary>
    ///     Handles scroll down sequence (CSI T) - scroll screen down by specified lines.
    ///     Implements CSI Ps T (Scroll Down) sequence.
    /// </summary>
    /// <param name="lines">Number of lines to scroll down (default: 1)</param>
    internal void ScrollScreenDown(int lines = 1)
    {
        _scrollOps.ScrollScreenDown(lines);
    }

    /// <summary>
    ///     Sets the scroll region (DECSTBM - Set Top and Bottom Margins).
    ///     Implements CSI Ps ; Ps r sequence.
    /// </summary>
    /// <param name="top">Top boundary (1-indexed, null for default)</param>
    /// <param name="bottom">Bottom boundary (1-indexed, null for default)</param>
    internal void SetScrollRegion(int? top, int? bottom)
    {
        _scrollRegionOps.SetScrollRegion(top, bottom);
    }

    /// <summary>
    ///     Sets a DEC private mode.
    /// </summary>
    /// <param name="mode">The DEC mode number</param>
    /// <param name="enabled">True to enable, false to disable</param>
    internal void SetDecMode(int mode, bool enabled)
    {
        _decModeOps.SetDecMode(mode, enabled);
    }


    /// <summary>
    ///     Moves the cursor up by the specified number of lines.
    /// </summary>
    /// <param name="count">Number of lines to move up (minimum 1)</param>
    internal void MoveCursorUp(int count)
    {
        _cursorMovementOps.MoveCursorUp(count);
    }

    /// <summary>
    ///     Moves the cursor down by the specified number of lines.
    /// </summary>
    /// <param name="count">Number of lines to move down (minimum 1)</param>
    internal void MoveCursorDown(int count)
    {
        _cursorMovementOps.MoveCursorDown(count);
    }

    /// <summary>
    ///     Moves the cursor forward (right) by the specified number of columns.
    /// </summary>
    /// <param name="count">Number of columns to move forward (minimum 1)</param>
    internal void MoveCursorForward(int count)
    {
        _cursorMovementOps.MoveCursorForward(count);
    }

    /// <summary>
    ///     Moves the cursor backward (left) by the specified number of columns.
    /// </summary>
    /// <param name="count">Number of columns to move backward (minimum 1)</param>
    internal void MoveCursorBackward(int count)
    {
        _cursorMovementOps.MoveCursorBackward(count);
    }

    /// <summary>
    ///     Sets the cursor to an absolute position.
    /// </summary>
    /// <param name="row">Target row (1-based, will be converted to 0-based)</param>
    /// <param name="column">Target column (1-based, will be converted to 0-based)</param>
    internal void SetCursorPosition(int row, int column)
    {
        _cursorMovementOps.SetCursorPosition(row, column);
    }

    /// <summary>
    ///     Sets the cursor to an absolute column position on the current row.
    ///     Implements CSI G (Cursor Horizontal Absolute) sequence.
    /// </summary>
    /// <param name="column">Target column (1-based, will be converted to 0-based)</param>
    internal void SetCursorColumn(int column)
    {
        _cursorMovementOps.SetCursorColumn(column);
    }

    /// <summary>
    ///     Clears the display according to the specified erase mode.
    ///     Implements CSI J (Erase in Display) sequence.
    /// </summary>
    /// <param name="mode">Erase mode: 0=cursor to end, 1=start to cursor, 2=entire screen, 3=entire screen and scrollback</param>
    internal void ClearDisplay(int mode)
    {
        _eraseInDisplayOps.ClearDisplay(mode);
    }

    /// <summary>
    ///     Clears the current line according to the specified erase mode.
    ///     Implements CSI K (Erase in Line) sequence.
    /// </summary>
    /// <param name="mode">Erase mode: 0=cursor to end of line, 1=start of line to cursor, 2=entire line</param>
    internal void ClearLine(int mode)
    {
        _eraseInLineOps.ClearLine(mode);
    }

    /// <summary>
    ///     Clears the display selectively according to the specified erase mode.
    ///     Implements CSI ? J (Selective Erase in Display) sequence.
    ///     Only erases unprotected cells, preserving protected cells.
    /// </summary>
    /// <param name="mode">Erase mode: 0=cursor to end, 1=start to cursor, 2=entire screen, 3=entire screen and scrollback</param>
    internal void ClearDisplaySelective(int mode)
    {
        _selectiveEraseInDisplayOps.ClearDisplaySelective(mode);
    }

    /// <summary>
    ///     Clears the current line selectively according to the specified erase mode.
    ///     Implements CSI ? K (Selective Erase in Line) sequence.
    ///     Only erases unprotected cells, preserving protected cells.
    /// </summary>
    /// <param name="mode">Erase mode: 0=cursor to end of line, 1=start of line to cursor, 2=entire line</param>
    internal void ClearLineSelective(int mode) => _selectiveEraseInLineOps.ClearLineSelective(mode);

    /// <summary>
    ///     Sets the character protection attribute for subsequently written characters.
    ///     Implements DECSCA (CSI Ps " q) sequence.
    /// </summary>
    /// <param name="isProtected">Whether new characters should be protected from selective erase</param>
    internal void SetCharacterProtection(bool isProtected)
    {
        _attributeManager.CurrentCharacterProtection = isProtected;
    }

    /// <summary>
    ///     Saves the current cursor position for later restoration with ESC 8.
    ///     Implements ESC 7 (Save Cursor) sequence.
    /// </summary>
    internal void SaveCursorPosition()
    {
        _cursorSaveRestoreOps.SaveCursorPosition();
    }

    /// <summary>
    ///     Restores the previously saved cursor position.
    ///     Implements ESC 8 (Restore Cursor) sequence.
    /// </summary>
    internal void RestoreCursorPosition()
    {
        _cursorSaveRestoreOps.RestoreCursorPosition();
    }

    /// <summary>
    ///     Saves the current cursor position using ANSI style (CSI s).
    ///     This is separate from DEC cursor save/restore (ESC 7/8) to maintain compatibility.
    ///     Implements CSI s sequence.
    /// </summary>
    internal void SaveCursorPositionAnsi()
    {
        _cursorSaveRestoreOps.SaveCursorPositionAnsi();
    }

    /// <summary>
    ///     Restores the previously saved ANSI cursor position.
    ///     This is separate from DEC cursor save/restore (ESC 7/8) to maintain compatibility.
    ///     Implements CSI u sequence.
    /// </summary>
    internal void RestoreCursorPositionAnsi()
    {
        _cursorSaveRestoreOps.RestoreCursorPositionAnsi();
    }

    /// <summary>
    ///     Handles reverse index (ESC M) - move cursor up; if at top margin, scroll region down.
    ///     Used by full-screen applications like less to scroll the display down within the scroll region.
    /// </summary>
    internal void HandleReverseIndex()
    {
        _scrollOps.HandleReverseIndex();
    }

    /// <summary>
    ///     Sets a tab stop at the current cursor position.
    ///     Implements ESC H (Horizontal Tab Set) sequence.
    /// </summary>
    internal void SetTabStopAtCursor() => _tabOps.SetTabStopAtCursor();

    /// <summary>
    ///     Moves cursor forward to the next tab stop.
    ///     Implements CSI I (Cursor Forward Tab) sequence.
    /// </summary>
    /// <param name="count">Number of tab stops to move forward</param>
    internal void CursorForwardTab(int count) => _tabOps.CursorForwardTab(count);

    /// <summary>
    ///     Moves cursor backward to the previous tab stop.
    ///     Implements CSI Z (Cursor Backward Tab) sequence.
    /// </summary>
    /// <param name="count">Number of tab stops to move backward</param>
    internal void CursorBackwardTab(int count) => _tabOps.CursorBackwardTab(count);

    /// <summary>
    ///     Clears the tab stop at the current cursor position.
    ///     Implements CSI g (Tab Clear) sequence with mode 0.
    /// </summary>
    internal void ClearTabStopAtCursor() => _tabOps.ClearTabStopAtCursor();

    /// <summary>
    ///     Clears all tab stops.
    ///     Implements CSI 3 g (Tab Clear) sequence with mode 3.
    /// </summary>
    internal void ClearAllTabStops() => _tabOps.ClearAllTabStops();

    /// <summary>
    ///     Inserts blank lines at the cursor position within the scroll region.
    ///     Implements CSI L (Insert Lines) sequence.
    ///     Lines below the cursor are shifted down, and lines that would go beyond
    ///     the scroll region bottom are lost.
    /// </summary>
    /// <param name="count">Number of lines to insert (minimum 1)</param>
    internal void InsertLinesInRegion(int count)
    {
        _insertLinesOps.InsertLinesInRegion(count);
    }

    /// <summary>
    ///     Deletes lines at the cursor position within the scroll region.
    ///     Implements CSI M (Delete Lines) sequence.
    ///     Lines below the cursor are shifted up, and blank lines are added
    ///     at the bottom of the scroll region.
    /// </summary>
    /// <param name="count">Number of lines to delete (minimum 1)</param>
    internal void DeleteLinesInRegion(int count) =>
        _deleteLinesOps.DeleteLinesInRegion(count);

    /// <summary>
    ///     Inserts blank characters at the cursor position within the current line.
    ///     Implements CSI @ (Insert Characters) sequence.
    ///     Characters to the right of the cursor are shifted right, and characters
    ///     that would go beyond the line end are lost.
    /// </summary>
    /// <param name="count">Number of characters to insert (minimum 1)</param>
    internal void InsertCharactersInLine(int count) =>
        _insertCharsOps.InsertCharactersInLine(count);

    /// <summary>
    ///     Deletes characters at the cursor position within the current line.
    ///     Implements CSI P (Delete Characters) sequence.
    ///     Characters to the right of the cursor are shifted left, and blank characters
    ///     are added at the end of the line.
    /// </summary>
    /// <param name="count">Number of characters to delete (minimum 1)</param>
    internal void DeleteCharactersInLine(int count) =>
        _deleteCharsOps.DeleteCharactersInLine(count);

    /// <summary>
    ///     Erases characters at the cursor position within the current line.
    ///     Implements CSI X (Erase Character) sequence.
    ///     Erases characters by replacing them with blank characters using current SGR attributes.
    ///     Does not move the cursor or shift other characters.
    /// </summary>
    /// <param name="count">Number of characters to erase (minimum 1)</param>
    internal void EraseCharactersInLine(int count) =>
        _eraseCharsOps.EraseCharactersInLine(count);

    /// <summary>
    ///     Resets the terminal to its initial state.
    ///     Implements ESC c (Reset to Initial State) sequence.
    /// </summary>
    internal void ResetToInitialState() => _resetOps.ResetToInitialState();

    /// <summary>
    ///     Performs a soft reset of the terminal.
    ///     Implements CSI ! p (DECSTR - DEC Soft Terminal Reset) sequence.
    ///     Resets terminal modes and state without clearing the screen buffer or cursor position.
    /// </summary>
    public void SoftReset() => _resetOps.SoftReset();

    /// <summary>
    ///     Designates a character set to a specific G slot.
    ///     Implements ESC ( X, ESC ) X, ESC * X, ESC + X sequences.
    /// </summary>
    /// <param name="slot">The G slot to designate (G0, G1, G2, G3)</param>
    /// <param name="charset">The character set identifier</param>
    internal void DesignateCharacterSet(string slot, string charset)
        => _charsetDesignationOps.DesignateCharacterSet(slot, charset);

    /// <summary>
    ///     Handles shift-in (SI) control character.
    ///     Switches active character set to G0.
    /// </summary>
    internal void HandleShiftIn() => _charsetTranslationOps.HandleShiftIn();

    /// <summary>
    ///     Handles shift-out (SO) control character.
    ///     Switches active character set to G1.
    /// </summary>
    internal void HandleShiftOut() => _charsetTranslationOps.HandleShiftOut();

    /// <summary>
    ///     Translates a character according to the current character set.
    ///     Handles DEC Special Graphics and other character set mappings.
    /// </summary>
    /// <param name="ch">The character to translate</param>
    /// <returns>The translated character string</returns>
    internal string TranslateCharacter(char ch) => _charsetTranslationOps.TranslateCharacter(ch);

    /// <summary>
    ///     Generates a character set query response.
    /// </summary>
    /// <returns>The character set query response string</returns>
    internal string GenerateCharacterSetQueryResponse() => _charsetTranslationOps.GenerateCharacterSetQueryResponse();

    /// <summary>
    ///     Raises the Bell event.
    /// </summary>
    private void OnBell()
    {
        Bell?.Invoke(this, new BellEventArgs());
    }

    /// <summary>
    ///     Raises the TitleChanged event.
    /// </summary>
    /// <param name="newTitle">The new window title</param>
    private void OnTitleChanged(string newTitle) => _titleIconEventsOps.OnTitleChanged(newTitle);

    /// <summary>
    ///     Raises the IconNameChanged event.
    /// </summary>
    /// <param name="newIconName">The new icon name</param>
    private void OnIconNameChanged(string newIconName) => _titleIconEventsOps.OnIconNameChanged(newIconName);

    /// <summary>
    ///     Raises the ClipboardRequest event.
    /// </summary>
    /// <param name="selectionTarget">The selection target (e.g., "c" for clipboard, "p" for primary)</param>
    /// <param name="data">The clipboard data (null for queries)</param>
    /// <param name="isQuery">Whether this is a clipboard query operation</param>
    private void OnClipboardRequest(string selectionTarget, string? data, bool isQuery = false)
    {
        ClipboardRequest?.Invoke(this, new ClipboardEventArgs(selectionTarget, data, isQuery));
    }

    /// <summary>
    ///     Throws an ObjectDisposedException if the terminal has been disposed.
    /// </summary>
    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(TerminalEmulator));
        }
    }

    /// <summary>
    ///     Saves the current state of specified private modes for later restoration.
    /// </summary>
    /// <param name="modes">Array of private mode numbers to save</param>
    internal void SavePrivateModes(int[] modes)
    {
        _privateModesOps.SavePrivateModes(modes);
    }

    /// <summary>
    ///     Restores the previously saved state of specified private modes.
    /// </summary>
    /// <param name="modes">Array of private mode numbers to restore</param>
    internal void RestorePrivateModes(int[] modes)
    {
        _privateModesOps.RestorePrivateModes(modes);
    }

    /// <summary>
    ///     Sets the cursor style (DECSCUSR).
    /// </summary>
    /// <param name="style">Cursor style parameter from DECSCUSR sequence (0-6)</param>
    public void SetCursorStyle(int style)
    {
        _cursorStyleOps.SetCursorStyle(style);
    }

    /// <summary>
    ///     Sets the cursor style using the CursorStyle enum.
    /// </summary>
    /// <param name="style">The cursor style to set</param>
    public void SetCursorStyle(CursorStyle style)
    {
        _cursorStyleOps.SetCursorStyle(style);
    }

    /// <summary>
    ///     Sets insert mode state. When enabled, new characters are inserted, shifting existing characters right.
    ///     When disabled, new characters overwrite existing characters (default behavior).
    /// </summary>
    /// <param name="enabled">True to enable insert mode, false to disable</param>
    public void SetInsertMode(bool enabled)
    {
        _insertModeOps.SetInsertMode(enabled);
    }

    /// <summary>
    ///     Wraps paste content with bracketed paste escape sequences if bracketed paste mode is enabled.
    ///     When bracketed paste mode is enabled (DECSET 2004), paste content is wrapped with:
    ///     - Start marker: ESC[200~
    ///     - End marker: ESC[201~
    /// </summary>
    /// <param name="pasteContent">The content to be pasted</param>
    /// <returns>The paste content, optionally wrapped with bracketed paste markers</returns>
    public string WrapPasteContent(string pasteContent) => _bracketedPasteOps.WrapPasteContent(pasteContent);

    /// <summary>
    ///     Wraps paste content with bracketed paste escape sequences if bracketed paste mode is enabled.
    ///     This overload accepts ReadOnlySpan&lt;char&gt; for performance-sensitive scenarios.
    /// </summary>
    /// <param name="pasteContent">The content to be pasted</param>
    /// <returns>The paste content, optionally wrapped with bracketed paste markers</returns>
    public string WrapPasteContent(ReadOnlySpan<char> pasteContent) => _bracketedPasteOps.WrapPasteContent(pasteContent);

    /// <summary>
    ///     Checks if bracketed paste mode is currently enabled.
    ///     This is a convenience method for external components that need to check paste mode state.
    /// </summary>
    /// <returns>True if bracketed paste mode is enabled, false otherwise</returns>
    public bool IsBracketedPasteModeEnabled() => _bracketedPasteOps.IsBracketedPasteModeEnabled();

}
