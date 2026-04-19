using caTTY.Core.Types;
using caTTY.Core.Managers;

namespace caTTY.Core.Terminal;

/// <summary>
///     Interface for a terminal emulator that processes raw byte data and maintains screen state.
///     Provides a headless terminal implementation with no UI dependencies.
/// </summary>
public interface ITerminalEmulator : IDisposable
{
    /// <summary>
    ///     Gets the width of the terminal in columns.
    /// </summary>
    int Width { get; }


    /// <summary>
    ///     Gets whether RPC functionality is enabled for this terminal emulator.
    /// </summary>
    bool IsRpcEnabled { get; }

    /// <summary>
    ///     Gets the height of the terminal in rows.
    /// </summary>
    int Height { get; }

    /// <summary>
    ///     Gets the current screen buffer for rendering.
    /// </summary>
    IScreenBuffer ScreenBuffer { get; }

    /// <summary>
    ///     Gets the current cursor state.
    /// </summary>
    ICursor Cursor { get; }

    /// <summary>
    ///     Gets the scrollback buffer for accessing historical lines.
    /// </summary>
    IScrollbackBuffer ScrollbackBuffer { get; }

    /// <summary>
    ///     Gets the scrollback manager for viewport and scrollback operations.
    /// </summary>
    IScrollbackManager ScrollbackManager { get; }

    /// <summary>
    ///     Processes raw byte data from a shell or other source.
    ///     Can be called with partial chunks and in rapid succession.
    /// </summary>
    /// <param name="data">The raw byte data to process</param>
    void Write(ReadOnlySpan<byte> data);

    /// <summary>
    ///     Processes string data by converting to UTF-8 bytes.
    /// </summary>
    /// <param name="text">The text to process</param>
    void Write(string text);

    /// <summary>
    ///     Resizes the terminal to the specified dimensions.
    /// </summary>
    /// <param name="width">New width in columns</param>
    /// <param name="height">New height in rows</param>
    void Resize(int width, int height);

    /// <summary>
    ///     Scrolls the viewport up by the specified number of lines.
    ///     Disables auto-scroll if not already at the top.
    /// </summary>
    /// <param name="lines">Number of lines to scroll up</param>
    void ScrollViewportUp(int lines);

    /// <summary>
    ///     Scrolls the viewport down by the specified number of lines.
    ///     Re-enables auto-scroll if scrolled to the bottom.
    /// </summary>
    /// <param name="lines">Number of lines to scroll down</param>
    void ScrollViewportDown(int lines);

    /// <summary>
    ///     Scrolls to the top of the scrollback buffer.
    ///     Disables auto-scroll.
    /// </summary>
    void ScrollViewportToTop();

    /// <summary>
    ///     Scrolls to the bottom of the scrollback buffer.
    ///     Re-enables auto-scroll.
    /// </summary>
    void ScrollViewportToBottom();

    /// <summary>
    ///     Gets whether auto-scroll is currently enabled.
    ///     Auto-scroll is disabled when user scrolls up and re-enabled when they return to bottom.
    /// </summary>
    bool IsAutoScrollEnabled { get; }

    /// <summary>
    ///     Gets the current viewport offset from the bottom.
    ///     0 means viewing the most recent content, positive values mean scrolled up into history.
    /// </summary>
    int ViewportOffset { get; }

    /// <summary>
    ///     Event raised when the screen content has been updated and needs refresh.
    /// </summary>
    event EventHandler<ScreenUpdatedEventArgs>? ScreenUpdated;

    /// <summary>
    ///     Event raised when the terminal needs to send a response back to the shell.
    ///     Used for device query replies and other terminal-generated responses.
    /// </summary>
    event EventHandler<ResponseEmittedEventArgs>? ResponseEmitted;

    /// <summary>
    ///     Event raised when a bell character (BEL, 0x07) is received.
    /// </summary>
    event EventHandler<BellEventArgs>? Bell;

    /// <summary>
    ///     Event raised when the window title is changed via OSC sequences.
    /// </summary>
    event EventHandler<TitleChangeEventArgs>? TitleChanged;

    /// <summary>
    ///     Event raised when the icon name is changed via OSC sequences.
    /// </summary>
    event EventHandler<IconNameChangeEventArgs>? IconNameChanged;

    /// <summary>
    ///     Event raised when a clipboard operation is requested via OSC 52 sequences.
    /// </summary>
    event EventHandler<ClipboardEventArgs>? ClipboardRequest;

    /// <summary>
    ///     Wraps paste content with bracketed paste escape sequences if bracketed paste mode is enabled.
    ///     When bracketed paste mode is enabled (DECSET 2004), paste content is wrapped with:
    ///     - Start marker: ESC[200~
    ///     - End marker: ESC[201~
    /// </summary>
    /// <param name="pasteContent">The content to be pasted</param>
    /// <returns>The paste content, optionally wrapped with bracketed paste markers</returns>
    string WrapPasteContent(string pasteContent);

    /// <summary>
    ///     Wraps paste content with bracketed paste escape sequences if bracketed paste mode is enabled.
    ///     This overload accepts ReadOnlySpan&lt;char&gt; for performance-sensitive scenarios.
    /// </summary>
    /// <param name="pasteContent">The content to be pasted</param>
    /// <returns>The paste content, optionally wrapped with bracketed paste markers</returns>
    string WrapPasteContent(ReadOnlySpan<char> pasteContent);

    /// <summary>
    ///     Checks if bracketed paste mode is currently enabled.
    ///     This is a convenience method for external components that need to check paste mode state.
    /// </summary>
    /// <returns>True if bracketed paste mode is enabled, false otherwise</returns>
    bool IsBracketedPasteModeEnabled();

    /// <summary>
    ///     Sets the cursor style using DECSCUSR sequence parameters.
    /// </summary>
    /// <param name="style">Cursor style parameter from DECSCUSR sequence (0-6)</param>
    void SetCursorStyle(int style);

    /// <summary>
    ///     Sets the cursor style using the CursorStyle enum.
    /// </summary>
    /// <param name="style">The cursor style to set</param>
    void SetCursorStyle(CursorStyle style);

    /// <summary>
    ///     Sets insert mode state. When enabled, new characters are inserted, shifting existing characters right.
    ///     When disabled, new characters overwrite existing characters (default behavior).
    /// </summary>
    /// <param name="enabled">True to enable insert mode, false to disable</param>
    void SetInsertMode(bool enabled);
}
