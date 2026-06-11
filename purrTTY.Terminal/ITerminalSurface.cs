using PurrTTY.Terminal.Input;
using PurrTTY.Terminal.Rendering;

namespace PurrTTY.Terminal;

/// <summary>Payload for an OSC 52 clipboard request emitted by the terminal.</summary>
public sealed class ClipboardRequest
{
    /// <summary>Clipboard selection target (e.g. "c" for clipboard, "p" for primary).</summary>
    public required string Target { get; init; }

    /// <summary>The text to place on the clipboard (already base64-decoded), or null for a query.</summary>
    public string? Text { get; init; }
}

/// <summary>
/// The renderer-neutral terminal backend contract. Frontends drive it with
/// neutral commands/events and draw the <see cref="TerminalFrame"/> it produces.
/// No ImGui / Vulkan / KSA types appear here by design.
///
/// Threading: <see cref="Write"/> is safe to call from any thread (e.g. a PTY
/// read thread); it queues bytes. Every other member must be called on the
/// frontend tick thread. Queued input is applied during <see cref="BuildFrame"/>.
/// </summary>
public interface ITerminalSurface : IDisposable
{
    int Cols { get; }
    int Rows { get; }

    // ---- IN: data + viewport ----

    /// <summary>Queues terminal output bytes (from the PTY). Thread-safe.</summary>
    void Write(ReadOnlySpan<byte> data);

    /// <summary>Resizes the grid. Cell pixel size feeds mouse/pixel reporting.</summary>
    void Resize(int cols, int rows, int cellPixelWidth = 0, int cellPixelHeight = 0);

    void ScrollBy(int deltaRows);
    void ScrollToTop();
    void ScrollToBottom();

    // ---- IN: selection ----

    /// <summary>Sets a raw cell selection between two viewport points.</summary>
    void SelectCells(GridPoint anchor, GridPoint head, bool rectangle = false);

    /// <summary>
    /// Begins a drag selection, pinning the anchor to the content under a
    /// viewport point. The anchor stays fixed to that content even if the
    /// viewport scrolls (e.g. autoscroll during the drag), so the selection
    /// grows correctly into scrollback. Pair with <see cref="ExtendSelectCells"/>.
    /// </summary>
    void BeginSelectCells(GridPoint anchor, bool rectangle = false);

    /// <summary>
    /// Extends the in-progress drag selection to a viewport point. No-op until
    /// <see cref="BeginSelectCells"/> has set an anchor.
    /// </summary>
    void ExtendSelectCells(GridPoint head);

    /// <summary>Selects the word under a viewport point.</summary>
    void SelectWord(GridPoint point);

    /// <summary>Selects the logical line under a viewport point.</summary>
    void SelectLine(GridPoint point);

    /// <summary>Selects all content.</summary>
    void SelectAll();

    void ClearSelection();

    /// <summary>True when there is an active selection (cheap; no text extraction).</summary>
    bool HasSelection { get; }

    /// <summary>Returns the active selection as plain text, or null if none.</summary>
    string? GetSelectionText();

    // ---- IN: theme + cursor ----

    void SetTheme(TerminalTheme theme);
    void SetCursorStyle(CursorShape shape, bool blink);

    // ---- IN: input encoding (returns bytes destined for the PTY) ----

    /// <summary>Encodes a key event into PTY bytes; returns count written to <paramref name="output"/>.</summary>
    int EncodeKey(in TerminalKeyEvent keyEvent, Span<byte> output);

    /// <summary>Encodes a mouse event into PTY bytes; returns count written (0 if mouse reporting is off).</summary>
    int EncodeMouse(in TerminalMouseEvent mouseEvent, Span<byte> output);

    /// <summary>Sets surface geometry used to map mouse pixel coords to cells.</summary>
    void SetMouseGeometry(int surfacePixelWidth, int surfacePixelHeight, int cellPixelWidth, int cellPixelHeight);

    /// <summary>True when the app has enabled bracketed paste mode.</summary>
    bool IsBracketedPasteEnabled { get; }

    /// <summary>Wraps text for paste (bracketed if the mode is active) into PTY bytes.</summary>
    byte[] EncodePaste(ReadOnlySpan<byte> text);

    /// <summary>True when the app has enabled any mouse tracking mode.</summary>
    bool IsMouseTrackingEnabled { get; }

    // ---- OUT: frame ----

    /// <summary>Applies queued input and returns the current frame snapshot.</summary>
    TerminalFrame BuildFrame();

    /// <summary>The most recently built frame (does not apply queued input).</summary>
    TerminalFrame CurrentFrame { get; }

    // ---- OUT: events (raised on the tick thread, during BuildFrame) ----

    /// <summary>Engine replies (DA/DSR/etc.) that must be written back to the PTY.</summary>
    event Action<byte[]>? PtyReply;

    event Action? Bell;
    event Action<string>? TitleChanged;
    event Action<string>? IconNameChanged;
    event Action<ClipboardRequest>? ClipboardRequested;

    /// <summary>Raised when a BuildFrame produced visibly different content.</summary>
    event Action? FrameChanged;
}
