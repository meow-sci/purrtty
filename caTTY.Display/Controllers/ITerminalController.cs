using System.Text;
using caTTY.Display.Types;

namespace caTTY.Display.Controllers;

/// <summary>
///     Interface for a terminal controller that handles ImGui display and input.
///     This controller bridges the headless terminal emulator with ImGui rendering.
/// </summary>
public interface ITerminalController : IDisposable
{
    /// <summary>
    ///     Gets or sets whether the terminal window is visible.
    /// </summary>
    bool IsVisible { get; set; }

    /// <summary>
    ///     Gets whether the terminal window currently has focus.
    /// </summary>
    bool HasFocus { get; }

    /// <summary>
    ///     Gets whether input capture is currently active.
    ///     When true, terminal should suppress game hotkeys bound to typing.
    /// </summary>
    bool IsInputCaptureActive { get; }

    /// <summary>
    ///     Determines whether the terminal should capture input based on focus and visibility.
    ///     When terminal is focused, it should suppress game hotkeys bound to typing.
    ///     When terminal is unfocused/hidden, all input should pass through to game.
    /// </summary>
    /// <returns>True if terminal should capture input, false if input should pass to game</returns>
    bool ShouldCaptureInput();

    /// <summary>
    ///     Forces the terminal to gain focus.
    ///     This can be used by external systems to programmatically focus the terminal.
    /// </summary>
    void ForceFocus();

    /// <summary>
    ///     Updates the controller state. Should be called each frame.
    /// </summary>
    /// <param name="deltaTime">Time elapsed since last update in seconds</param>
    void Update(float deltaTime);

    /// <summary>
    ///     Renders the terminal ImGui window and handles input.
    ///     Should be called during the ImGui render phase.
    /// </summary>
    void Render();

    /// <summary>
    ///     Gets the current terminal dimensions.
    ///     Useful for debugging and integration testing.
    /// </summary>
    /// <returns>Current terminal dimensions (width, height)</returns>
    (int width, int height) GetTerminalDimensions();

    /// <summary>
    ///     Manually triggers a terminal resize to the specified dimensions.
    ///     This method can be used for testing or external resize requests.
    /// </summary>
    /// <param name="cols">New width in columns</param>
    /// <param name="rows">New height in rows</param>
    /// <exception cref="ArgumentException">Thrown when dimensions are invalid</exception>
    void ResizeTerminal(int cols, int rows);

    /// <summary>
    ///     Gets the current text selection.
    /// </summary>
    /// <returns>The current selection</returns>
    TextSelection GetCurrentSelection();

    /// <summary>
    ///     Sets the current text selection.
    /// </summary>
    /// <param name="selection">The selection to set</param>
    void SetSelection(TextSelection selection);

    /// <summary>
    ///     Copies the current selection to the clipboard.
    /// </summary>
    /// <returns>True if text was copied successfully, false otherwise</returns>
    bool CopySelectionToClipboard();

    /// <summary>
    ///     Pastes text from the clipboard to the terminal.
    /// </summary>
    void PasteFromClipboard();

    /// <summary>
    ///     Event raised when user input should be sent to the process.
    ///     The string contains the encoded bytes/escape sequences to send.
    /// </summary>
    event EventHandler<DataInputEventArgs>? DataInput;

    /// <summary>
    ///     Event raised when the terminal focus state changes.
    /// </summary>
    event EventHandler<FocusChangedEventArgs>? FocusChanged;
}

/// <summary>
///     Event arguments for data input from the terminal controller.
/// </summary>
public class DataInputEventArgs : EventArgs
{
    /// <summary>
    ///     Creates new data input event arguments.
    /// </summary>
    /// <param name="data">The input data as a string</param>
    /// <param name="bytes">The input data as raw bytes</param>
    public DataInputEventArgs(string data, byte[] bytes)
    {
        Data = data;
        Bytes = bytes;
    }

    /// <summary>
    ///     Creates new data input event arguments from a string.
    /// </summary>
    /// <param name="data">The input data as a string</param>
    public DataInputEventArgs(string data) : this(data, Encoding.UTF8.GetBytes(data))
    {
    }

    /// <summary>
    ///     Creates new data input event arguments from bytes.
    /// </summary>
    /// <param name="bytes">The input data as raw bytes</param>
    public DataInputEventArgs(byte[] bytes) : this(Encoding.UTF8.GetString(bytes), bytes)
    {
    }

    /// <summary>
    ///     The input data as a string (may contain escape sequences).
    /// </summary>
    public string Data { get; }

    /// <summary>
    ///     The input data as raw bytes.
    /// </summary>
    public byte[] Bytes { get; }
}

/// <summary>
///     Event arguments for focus state changes in the terminal controller.
/// </summary>
public class FocusChangedEventArgs : EventArgs
{
    /// <summary>
    ///     Creates new focus changed event arguments.
    /// </summary>
    /// <param name="hasFocus">Whether the terminal currently has focus</param>
    /// <param name="previousFocus">Whether the terminal had focus in the previous state</param>
    public FocusChangedEventArgs(bool hasFocus, bool previousFocus)
    {
        HasFocus = hasFocus;
        PreviousFocus = previousFocus;
    }

    /// <summary>
    ///     Whether the terminal currently has focus.
    /// </summary>
    public bool HasFocus { get; }

    /// <summary>
    ///     Whether the terminal had focus in the previous state.
    /// </summary>
    public bool PreviousFocus { get; }

    /// <summary>
    ///     Whether this represents a focus gained event.
    /// </summary>
    public bool FocusGained => HasFocus && !PreviousFocus;

    /// <summary>
    ///     Whether this represents a focus lost event.
    /// </summary>
    public bool FocusLost => !HasFocus && PreviousFocus;
}
