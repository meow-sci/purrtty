using System.Text;
using NUnit.Framework;
using PurrTTY.Terminal;
using PurrTTY.Terminal.Ghostty;
using PurrTTY.Terminal.Input;
using PurrTTY.Terminal.Rendering;

namespace PurrTTY.Terminal.Tests;

/// <summary>
/// Integration tests for <see cref="GhosttyTerminalSurface"/>. These verify
/// purrtty's integration with libghostty-vt — frame production, theming,
/// selection, OSC sidecar, and input encoding — not the engine's VT behavior
/// itself (which we trust).
/// </summary>
[TestFixture]
public sealed class GhosttyTerminalSurfaceTests
{
    private static GhosttyTerminalSurface NewSurface(int cols = 40, int rows = 10)
        => new(cols, rows);

    private static void WriteText(ITerminalSurface surface, string text)
        => surface.Write(Encoding.UTF8.GetBytes(text));

    private static string RowText(TerminalFrame frame, int row)
    {
        var sb = new StringBuilder();
        var cells = frame.RowData[row].Cells;
        for (int c = 0; c < frame.Cols; c++)
        {
            sb.Append(cells[c].Grapheme ?? " ");
        }

        return sb.ToString().TrimEnd();
    }

    [Test]
    public void Write_ProducesGraphemesAndCursorPosition()
    {
        using var surface = NewSurface();
        WriteText(surface, "hello world");
        var frame = surface.BuildFrame();

        Assert.That(RowText(frame, 0), Is.EqualTo("hello world"));
        Assert.That(frame.Cursor.X, Is.EqualTo(11));
        Assert.That(frame.Cursor.Y, Is.EqualTo(0));
        Assert.That(frame.Cursor.Visible, Is.True);
    }

    [Test]
    public void SetCursorStyle_AppliesAsDefaultAndYieldsToDecscusr()
    {
        using var surface = NewSurface();

        // The setting is the engine *default*: it applies immediately while the
        // app has not chosen a style, so a menu change is visible live.
        surface.SetCursorStyle(CursorShape.Underline, blink: true);
        var frame = surface.BuildFrame();
        Assert.That(frame.Cursor.Shape, Is.EqualTo(CursorShape.Underline));
        Assert.That(frame.Cursor.Blinking, Is.True);

        // An app's explicit DECSCUSR (CSI 6 q = steady bar) wins over the default...
        WriteText(surface, "\x1b[6 q");
        frame = surface.BuildFrame();
        Assert.That(frame.Cursor.Shape, Is.EqualTo(CursorShape.Bar));
        Assert.That(frame.Cursor.Blinking, Is.False);

        // ...and changing the default while overridden must not stomp the app's choice.
        surface.SetCursorStyle(CursorShape.Block, blink: false);
        frame = surface.BuildFrame();
        Assert.That(frame.Cursor.Shape, Is.EqualTo(CursorShape.Bar));

        // DECSCUSR reset (CSI 0 q) returns to the configured default.
        WriteText(surface, "\x1b[0 q");
        frame = surface.BuildFrame();
        Assert.That(frame.Cursor.Shape, Is.EqualTo(CursorShape.Block));
        Assert.That(frame.Cursor.Blinking, Is.False);
    }

    [Test]
    public void Sgr_ForegroundResolvesToPaletteColor()
    {
        using var surface = NewSurface();
        // Red foreground (SGR 31 → palette index 1), one glyph, reset.
        WriteText(surface, "\x1b[31mX\x1b[0m");
        var frame = surface.BuildFrame();

        var cellFg = frame.RowData[0].Cells[0].Fg;
        Assert.That(cellFg, Is.EqualTo(frame.Colors.Palette[1]),
            "SGR 31 should resolve to ANSI palette index 1 (red).");
        Assert.That(cellFg, Is.Not.EqualTo(frame.Colors.DefaultForeground));
    }

    [Test]
    public void SetTheme_DefaultColorsReflectedInFrame()
    {
        using var surface = NewSurface();
        var theme = new TerminalTheme
        {
            DefaultForeground = new RgbaColor(10, 200, 30),
            DefaultBackground = new RgbaColor(1, 2, 3),
        };
        for (int i = 0; i < 256; i++)
        {
            theme.Palette[i] = new RgbaColor((byte)i, 0, 0);
        }

        surface.SetTheme(theme);
        WriteText(surface, "Z");
        var frame = surface.BuildFrame();

        Assert.That(frame.Colors.DefaultForeground, Is.EqualTo(new RgbaColor(10, 200, 30)));
        Assert.That(frame.Colors.DefaultBackground, Is.EqualTo(new RgbaColor(1, 2, 3)));
        // The 'Z' cell uses default fg (no SGR color set).
        Assert.That(frame.RowData[0].Cells[0].Fg, Is.EqualTo(new RgbaColor(10, 200, 30)));
    }

    [Test]
    public void Sgr_BoldAndItalic_SetCellFlags()
    {
        using var surface = NewSurface();
        // Bold 'B', reset, italic 'I', reset.
        WriteText(surface, "\x1b[1mB\x1b[0m\x1b[3mI\x1b[0m");
        var frame = surface.BuildFrame();

        Assert.That(frame.RowData[0].Cells[0].Flags & CellFlags.Bold, Is.EqualTo(CellFlags.Bold),
            "SGR 1 should set the Bold flag on the cell.");
        Assert.That(frame.RowData[0].Cells[1].Flags & CellFlags.Italic, Is.EqualTo(CellFlags.Italic),
            "SGR 3 should set the Italic flag on the cell.");
    }

    [Test]
    public void Sgr_BackgroundOnBlankCells_PreservedUnderOptimizedReader()
    {
        // Regression guard for the optimized cell reader: it gates the bg/fg
        // reads behind has_styling, so a styled-but-textless cell (a space with a
        // background color) must still carry that background.
        using var surface = NewSurface();
        WriteText(surface, "\x1b[44m   \x1b[0m"); // three spaces, blue background
        var frame = surface.BuildFrame();

        var blue = frame.Colors.Palette[4];
        Assert.That(frame.RowData[0].Cells[0].Bg, Is.EqualTo(blue));
        Assert.That(frame.RowData[0].Cells[2].Bg, Is.EqualTo(blue));
        Assert.That(blue, Is.Not.EqualTo(frame.Colors.DefaultBackground));
    }

    [Test]
    public void EraseToEndOfLine_WithBackground_FillsTrailingCells()
    {
        // htop and friends paint a row by printing text then erasing to the end
        // of the line under a background color. Those trailing cells carry a
        // background but no text (and, as it turns out, has_styling == false), so
        // the reader must not skip their pre-resolved background.
        using var surface = NewSurface(20, 3);
        WriteText(surface, "\x1b[44mMENU\x1b[K");
        var frame = surface.BuildFrame();

        var blue = frame.Colors.Palette[4];
        Assert.That(blue, Is.Not.EqualTo(frame.Colors.DefaultBackground));
        Assert.That(frame.RowData[0].Cells[10].Bg, Is.EqualTo(blue),
            "cells erased to EOL under a background color must retain it");
    }

    [Test]
    public void ReverseVideo_SwapsResolvedForegroundAndBackground()
    {
        // The frontend does not swap on the Inverse flag; it relies on the engine
        // pre-resolving reverse video into the cell's fg/bg, which the reader reads
        // unconditionally. This covers SGR 7 (status bars, selected menu items).
        using var surface = NewSurface();
        var theme = new TerminalTheme
        {
            DefaultForeground = new RgbaColor(200, 200, 200),
            DefaultBackground = new RgbaColor(10, 10, 10),
        };
        for (int i = 0; i < 256; i++)
        {
            theme.Palette[i] = new RgbaColor((byte)i, 0, 0);
        }
        surface.SetTheme(theme);

        WriteText(surface, "N\x1b[7mR\x1b[0m"); // normal 'N', reverse-video 'R'
        var frame = surface.BuildFrame();

        var normal = frame.RowData[0].Cells[0];
        var reverse = frame.RowData[0].Cells[1];

        Assert.That(reverse.Fg, Is.EqualTo(normal.Bg), "reverse video draws the glyph in the background color");
        Assert.That(reverse.Bg, Is.EqualTo(normal.Fg), "reverse video fills the cell in the foreground color");
    }

    [Test]
    public void TrueColorBackground_ResolvesExactRgb()
    {
        // SGR 48;2;r;g;b direct-color background.
        using var surface = NewSurface();
        WriteText(surface, "\x1b[48;2;12;34;56mX\x1b[0m");
        var frame = surface.BuildFrame();

        Assert.That(frame.RowData[0].Cells[0].Bg, Is.EqualTo(new RgbaColor(12, 34, 56)));
    }

    [Test]
    public void ClearScreenWithBackground_FillsBlankCells()
    {
        // ED (CSI 2 J) under a background color performs background-color erase:
        // every cleared cell carries that background even though none have text.
        using var surface = NewSurface(20, 3);
        WriteText(surface, "\x1b[42m\x1b[2J");
        var frame = surface.BuildFrame();

        var green = frame.Colors.Palette[2];
        Assert.That(green, Is.Not.EqualTo(frame.Colors.DefaultBackground));
        Assert.That(frame.RowData[1].Cells[5].Bg, Is.EqualTo(green));
    }

    [Test]
    public void Resize_UpdatesDimensions()
    {
        using var surface = NewSurface(40, 10);
        surface.Resize(80, 24, 8, 16);
        var frame = surface.BuildFrame();

        Assert.That(surface.Cols, Is.EqualTo(80));
        Assert.That(surface.Rows, Is.EqualTo(24));
        Assert.That(frame.Cols, Is.EqualTo(80));
        Assert.That(frame.Rows, Is.EqualTo(24));
    }

    [Test]
    public void SelectAll_ReturnsTextAndMarksRows()
    {
        using var surface = NewSurface();
        WriteText(surface, "alpha beta");
        surface.BuildFrame();

        surface.SelectAll();
        var frame = surface.BuildFrame();

        Assert.That(surface.GetSelectionText(), Does.Contain("alpha beta"));
        Assert.That(frame.RowData[0].HasSelection, Is.True);
    }

    [Test]
    public void HasSelection_TracksSelectionLifecycle()
    {
        // The cheap native probe used by the context menu's enable state — it
        // must agree with the full text-extraction path at every step.
        using var surface = NewSurface();
        WriteText(surface, "alpha beta");
        surface.BuildFrame();

        Assert.That(surface.HasSelection, Is.False);

        surface.SelectAll();
        Assert.That(surface.HasSelection, Is.True);

        surface.ClearSelection();
        Assert.That(surface.HasSelection, Is.False);
    }

    [Test]
    public void SelectWord_SelectsSingleWord()
    {
        using var surface = NewSurface();
        WriteText(surface, "alpha beta gamma");
        surface.BuildFrame();

        surface.SelectWord(new GridPoint(7, 0)); // inside "beta"
        surface.BuildFrame();

        Assert.That(surface.GetSelectionText(), Is.EqualTo("beta"));
    }

    [Test]
    public void BeginExtendSelect_AnchorSurvivesViewportScroll()
    {
        // The drag anchor is pinned to content (a GridRef), so it must stay put
        // when the viewport scrolls between Begin and Extend — exactly what the
        // controller's drag-autoscroll relies on.
        using var surface = NewSurface(20, 3);
        for (int i = 0; i < 10; i++)
        {
            WriteText(surface, i == 9 ? "row09" : $"row{i:00}\r\n");
        }
        surface.BuildFrame();

        surface.ScrollToTop();          // viewport now shows the oldest rows
        surface.BuildFrame();
        surface.BeginSelectCells(new GridPoint(0, 0)); // anchor on "row00"

        surface.ScrollToBottom();       // viewport jumps to the newest rows
        surface.BuildFrame();
        surface.ExtendSelectCells(new GridPoint(4, 2)); // head on "row09"
        surface.BuildFrame();

        var text = surface.GetSelectionText();
        Assert.That(text, Is.Not.Null);
        Assert.That(text, Does.Contain("row00"), "anchor should have stayed pinned to the first row");
        Assert.That(text, Does.Contain("row09"), "selection should extend to the last row");
    }

    [Test]
    public void BeginExtendSelect_AnchorPrunedFromScrollback_StaysValid()
    {
        // The drag anchor is a *tracked* grid ref. When heavy output prunes the
        // anchored page out of scrollback mid-drag, the engine relocates the
        // tracked pin to the oldest surviving content (an untracked anchor
        // would be a dangling page pointer here — corrupt selection or crash).
        // maxScrollback: 0 keeps only the active screen, so the anchor row is
        // reclaimed almost immediately.
        using var surface = new GhosttyTerminalSurface(20, 5, maxScrollback: 0);
        WriteText(surface, "anchor row\r\n");
        surface.BuildFrame();
        surface.BeginSelectCells(new GridPoint(0, 0)); // anchor on "anchor row"

        for (int i = 0; i < 2000; i++)
        {
            WriteText(surface, $"filler line {i}\r\n"); // prunes the anchor's page
        }
        surface.BuildFrame();

        surface.ExtendSelectCells(new GridPoint(4, 2));
        surface.BuildFrame();

        // The drag either ended (anchor discarded) or clamped to surviving
        // content — both are graceful; what matters is the selection (if any)
        // is coherent live content, not garbage from a freed page.
        var text = surface.GetSelectionText();
        if (text is not null)
        {
            Assert.That(text, Does.Contain("filler line"),
                "a pruned anchor must resolve to surviving content");
        }

        // The anchor mechanism re-arms for the next drag.
        surface.BeginSelectCells(new GridPoint(0, 0));
        surface.ExtendSelectCells(new GridPoint(10, 0));
        surface.BuildFrame();
        Assert.That(surface.GetSelectionText(), Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public void Write_BeyondInboxCap_DropsButStaysCoherent()
    {
        // Hidden-terminal scenario: PTY output arrives but nothing ticks
        // BuildFrame. The inbox must cap (8 MiB) instead of growing without
        // bound, catch-up must be chunked (1 MiB per tick), and the surface
        // must stay usable after the drop.
        using var surface = NewSurface();
        var chunk = new byte[1024 * 1024];
        Array.Fill(chunk, (byte)'x');
        for (int i = 0; i < 12; i++)
        {
            surface.Write(chunk); // 12 MiB offered, > the 8 MiB cap
        }

        surface.BuildFrame();
        Assert.That(surface.LastFrameStats.BytesConsumed, Is.EqualTo(1024 * 1024),
            "backlog catch-up should be chunked, not one giant engine write");

        long consumed = surface.LastFrameStats.BytesConsumed;
        for (int i = 0; i < 16; i++)
        {
            surface.BuildFrame();
            consumed += surface.LastFrameStats.BytesConsumed;
        }

        Assert.That(consumed, Is.LessThanOrEqualTo(8L * 1024 * 1024),
            "everything past the cap should have been dropped");

        // Output accepted after the drop still parses correctly (the drop
        // boundary is healed with CAN+ST before new bytes are enqueued).
        WriteText(surface, "\x1b[2J\x1b[Hstill alive");
        var frame = surface.BuildFrame();
        Assert.That(RowText(frame, 0), Is.EqualTo("still alive"));
    }

    [Test]
    public void ExtendSelect_WithoutBegin_IsNoOp()
    {
        using var surface = NewSurface();
        WriteText(surface, "nothing selected");
        surface.BuildFrame();

        surface.ExtendSelectCells(new GridPoint(3, 0));
        surface.BuildFrame();

        Assert.That(surface.GetSelectionText(), Is.Null);
    }

    [Test]
    public void SelectLine_SelectsWholeLine()
    {
        using var surface = NewSurface();
        WriteText(surface, "the whole line here");
        surface.BuildFrame();

        surface.SelectLine(new GridPoint(3, 0));
        surface.BuildFrame();

        Assert.That(surface.GetSelectionText(), Is.EqualTo("the whole line here"));
    }

    [Test]
    public void ClearSelection_RemovesSelection()
    {
        using var surface = NewSurface();
        WriteText(surface, "stuff");
        surface.BuildFrame();
        surface.SelectAll();
        surface.BuildFrame();

        surface.ClearSelection();
        var frame = surface.BuildFrame();

        Assert.That(surface.GetSelectionText(), Is.Null);
        Assert.That(frame.RowData[0].HasSelection, Is.False);
    }

    [Test]
    public void Osc52_RaisesClipboardRequestWithDecodedText()
    {
        using var surface = NewSurface();
        ClipboardRequest? captured = null;
        surface.ClipboardRequested += req => captured = req;

        // base64("hi there") = "aGkgdGhlcmU="
        WriteText(surface, "\x1b]52;c;aGkgdGhlcmU=\x07");
        surface.BuildFrame();

        Assert.That(captured, Is.Not.Null);
        Assert.That(captured!.Target, Is.EqualTo("c"));
        Assert.That(captured.Text, Is.EqualTo("hi there"));
    }

    [Test]
    public void Osc1_RaisesIconNameChanged()
    {
        using var surface = NewSurface();
        string? icon = null;
        surface.IconNameChanged += name => icon = name;

        WriteText(surface, "\x1b]1;myicon\x07");
        surface.BuildFrame();

        Assert.That(icon, Is.EqualTo("myicon"));
    }

    [Test]
    public void TitleChange_RaisesTitleChanged()
    {
        using var surface = NewSurface();
        string? title = null;
        surface.TitleChanged += t => title = t;

        WriteText(surface, "\x1b]0;My Title\x07");
        surface.BuildFrame();

        Assert.That(title, Is.EqualTo("My Title"));
    }

    [Test]
    public void EncodeKey_NamedKeysEncodeWithoutText()
    {
        using var surface = NewSurface();
        surface.BuildFrame();

        Span<byte> buf = stackalloc byte[32];

        // Named keys (Enter/Tab/arrows) encode from Key alone; Text is only for
        // printable input. This mirrors how a frontend must build key events.
        int enter = surface.EncodeKey(new TerminalKeyEvent(TerminalKey.Enter), buf);
        Assert.That(buf[..enter].ToArray(), Is.EqualTo(new[] { (byte)'\r' }));

        int tab = surface.EncodeKey(new TerminalKeyEvent(TerminalKey.Tab), buf);
        Assert.That(buf[..tab].ToArray(), Is.EqualTo(new[] { (byte)'\t' }));

        int up = surface.EncodeKey(new TerminalKeyEvent(TerminalKey.ArrowUp), buf);
        Assert.That(Encoding.ASCII.GetString(buf[..up]), Is.EqualTo("\x1b[A"));
    }

    [Test]
    public void EncodeKey_PrintableUsesText()
    {
        using var surface = NewSurface();
        surface.BuildFrame();

        Span<byte> buf = stackalloc byte[32];
        int n = surface.EncodeKey(new TerminalKeyEvent(TerminalKey.A, KeyAction.Press, text: "a"), buf);

        Assert.That(Encoding.ASCII.GetString(buf[..n]), Is.EqualTo("a"));
    }

    [Test]
    public void EncodeMouse_SgrButtonsMapToWireCodes()
    {
        // Enable normal mouse tracking (1000) + SGR extended encoding (1006). The
        // neutral MouseButton ordering (Left=0/Middle=1/Right=2) is NOT libghostty's
        // (Left=1/Right=2/Middle=3), so the backend must translate — a straight cast
        // would mis-send Left as "unknown" and Middle as "left".
        using var surface = NewSurface(20, 5);
        WriteText(surface, "\x1b[?1000h\x1b[?1006h");
        surface.BuildFrame();
        surface.SetMouseGeometry(160, 80, 8, 16);
        Assert.That(surface.IsMouseTrackingEnabled, Is.True);

        Assert.That(EncodeMouseSgr(surface, MouseAction.Press, MouseButton.Left, 0, 0),
            Is.EqualTo("\x1b[<0;1;1M"), "left press → SGR wire button 0 at cell (1,1)");
        Assert.That(EncodeMouseSgr(surface, MouseAction.Press, MouseButton.Middle, 0, 0),
            Is.EqualTo("\x1b[<1;1;1M"), "middle press → SGR wire button 1");
        Assert.That(EncodeMouseSgr(surface, MouseAction.Press, MouseButton.Right, 0, 0),
            Is.EqualTo("\x1b[<2;1;1M"), "right press → SGR wire button 2");
        Assert.That(EncodeMouseSgr(surface, MouseAction.Release, MouseButton.Left, 0, 0),
            Is.EqualTo("\x1b[<0;1;1m"), "release uses a lowercase 'm' terminator");
    }

    [Test]
    public void EncodeMouse_UsesSurfaceLocalPixels()
    {
        // The encoder converts surface-local pixels (0,0 = grid top-left) to cells,
        // so the frontend must pass mouse-minus-canvas, not screen-global coords.
        using var surface = NewSurface(20, 5);
        WriteText(surface, "\x1b[?1000h\x1b[?1006h");
        surface.BuildFrame();
        surface.SetMouseGeometry(160, 80, 8, 16); // 8×16 cells

        Assert.That(EncodeMouseSgr(surface, MouseAction.Press, MouseButton.Left, 8, 16),
            Is.EqualTo("\x1b[<0;2;2M"), "pixel (8,16) with 8×16 cells is cell (2,2)");
        Assert.That(EncodeMouseSgr(surface, MouseAction.Press, MouseButton.Left, 24, 48),
            Is.EqualTo("\x1b[<0;4;4M"), "pixel (24,48) is cell (4,4)");
    }

    [Test]
    public void EncodeMouse_MotionReportsDragInButtonEventTracking()
    {
        // Button-event tracking (1002) + SGR (1006): a drag (motion with the left
        // button held) must report so apps like nvim update a selection live, not
        // only on release. SGR motion adds 32 to the button code → left (0) + 32 = 32.
        using var surface = NewSurface(20, 5);
        WriteText(surface, "\x1b[?1002h\x1b[?1006h");
        surface.BuildFrame();
        surface.SetMouseGeometry(160, 80, 8, 16);

        Assert.That(EncodeMouseSgr(surface, MouseAction.Motion, MouseButton.Left, 24, 48),
            Is.EqualTo("\x1b[<32;4;4M"), "left-drag motion → SGR button 0+32 at cell (4,4)");
    }

    [Test]
    public void EncodeMouse_MotionWithoutButtonIsDroppedInButtonEventTracking()
    {
        // 1002 reports motion only while a button is held; a no-button hover must
        // produce nothing (the encoder is mode-aware and drops it).
        using var surface = NewSurface(20, 5);
        WriteText(surface, "\x1b[?1002h\x1b[?1006h");
        surface.BuildFrame();
        surface.SetMouseGeometry(160, 80, 8, 16);

        Assert.That(EncodeMouseSgr(surface, MouseAction.Motion, MouseButton.None, 24, 48),
            Is.EqualTo(string.Empty), "no-button hover in 1002 emits no report");
    }

    [Test]
    public void EncodeMouse_MotionDroppedInNormalTracking()
    {
        // Normal tracking (1000) reports presses/releases only — motion offered by
        // the frontend must be dropped by the encoder, never sent to the PTY.
        using var surface = NewSurface(20, 5);
        WriteText(surface, "\x1b[?1000h\x1b[?1006h");
        surface.BuildFrame();
        surface.SetMouseGeometry(160, 80, 8, 16);

        Assert.That(EncodeMouseSgr(surface, MouseAction.Motion, MouseButton.Left, 24, 48),
            Is.EqualTo(string.Empty), "1000 tracking emits no motion report");
    }

    private static string EncodeMouseSgr(ITerminalSurface surface, MouseAction action, MouseButton button, float x, float y)
    {
        var ev = new TerminalMouseEvent { Action = action, Button = button, X = x, Y = y };
        Span<byte> buf = stackalloc byte[32];
        int n = surface.EncodeMouse(ev, buf);
        return Encoding.ASCII.GetString(buf[..n]);
    }

    [Test]
    public void BracketedPaste_WrapsWhenModeEnabled()
    {
        using var surface = NewSurface();
        WriteText(surface, "\x1b[?2004h"); // enable bracketed paste
        surface.BuildFrame();

        Assert.That(surface.IsBracketedPasteEnabled, Is.True);

        var encoded = surface.EncodePaste(Encoding.UTF8.GetBytes("paste"));
        var text = Encoding.ASCII.GetString(encoded);

        Assert.That(text, Does.Contain("paste"));
        Assert.That(text, Does.StartWith("\x1b[200~"));
        Assert.That(text, Does.EndWith("\x1b[201~"));
    }

    [Test]
    public void PtyReply_EmittedForDeviceStatusReport()
    {
        using var surface = NewSurface();
        byte[]? reply = null;
        surface.PtyReply += bytes => reply = bytes;

        // DSR — request cursor position report (CSI 6 n). Engine replies via PTY.
        WriteText(surface, "\x1b[6n");
        surface.BuildFrame();

        Assert.That(reply, Is.Not.Null);
        Assert.That(reply!.Length, Is.GreaterThan(0));
        Assert.That(reply[0], Is.EqualTo((byte)0x1b)); // CSI reply
    }

    [Test]
    public void Generation_AdvancesOnContentChange()
    {
        using var surface = NewSurface();
        WriteText(surface, "a");
        long g1 = surface.BuildFrame().Generation;

        WriteText(surface, "b");
        long g2 = surface.BuildFrame().Generation;

        Assert.That(g2, Is.GreaterThan(g1));
    }

    [Test]
    public void Generation_StableWhenNothingChanges()
    {
        // The engine's dirty tracking must let idle ticks skip the frame
        // rebuild entirely — generation only moves on real changes.
        using var surface = NewSurface();
        WriteText(surface, "hello");
        surface.BuildFrame();

        long g1 = surface.BuildFrame().Generation;
        long g2 = surface.BuildFrame().Generation;
        long g3 = surface.BuildFrame().Generation;

        Assert.That(g2, Is.EqualTo(g1));
        Assert.That(g3, Is.EqualTo(g1));
    }

    [Test]
    public void PartialUpdate_RefreshesDirtyRowAndPreservesCleanRows()
    {
        using var surface = NewSurface();
        WriteText(surface, "first\r\nsecond");
        surface.BuildFrame();

        // Overwrite row 0 only; row 1 is clean in the engine's dirty tracking,
        // so the frame must keep its cached cells for it.
        WriteText(surface, "\x1b[1;1HFIRST");
        var frame = surface.BuildFrame();

        Assert.That(RowText(frame, 0), Is.EqualTo("FIRST"));
        Assert.That(RowText(frame, 1), Is.EqualTo("second"));
    }

    [Test]
    public void Underline_SetsRowDecorationFlag()
    {
        using var surface = NewSurface();
        WriteText(surface, "\x1b[4mU\x1b[0m");
        var frame = surface.BuildFrame();

        Assert.That(frame.RowData[0].Cells[0].Underline, Is.EqualTo(UnderlineStyle.Single));
        Assert.That(frame.RowData[0].HasDecorations, Is.True);
        Assert.That(frame.RowData[1].HasDecorations, Is.False);
    }

    [Test]
    public void GraphemeCluster_ReadAsSingleCellWithWideSpacer()
    {
        using var surface = NewSurface();
        WriteText(surface, "e\u0301 \U0001F44D"); // e + combining acute (cluster), space, thumbs-up (wide)
        var frame = surface.BuildFrame();

        var cells = frame.RowData[0].Cells;
        Assert.That(cells[0].Grapheme, Is.EqualTo("e\u0301"));
        Assert.That(cells[2].Grapheme, Is.EqualTo("\U0001F44D"));
        Assert.That(cells[2].Width, Is.EqualTo(CellWidth.Wide));
        Assert.That(cells[3].Width, Is.EqualTo(CellWidth.Spacer));
    }

    [Test]
    public void SynchronizedOutput_WithholdsPartialFrames()
    {
        using var surface = NewSurface();
        WriteText(surface, "before");
        surface.BuildFrame();

        // App begins a synchronized update (DEC 2026), clears, redraws — the
        // frame must keep showing the last complete state until the mode ends.
        WriteText(surface, "\x1b[?2026h\x1b[2J\x1b[Hafter");
        var held = surface.BuildFrame();
        Assert.That(RowText(held, 0), Is.EqualTo("before"),
            "frame must hold while synchronized output is active");

        WriteText(surface, "\x1b[?2026l");
        var released = surface.BuildFrame();
        Assert.That(RowText(released, 0), Is.EqualTo("after"));
    }

    [Test]
    public void RawCellLayout_MatchesNativeAccessors()
    {
        // The fast frame reader decodes the packed u64 cell managed-side; this
        // cross-checks the decode against the native accessors so a native pin
        // bump that changes the layout fails loudly here.
        Assert.That(global::Ghostty.Vt.RawCellLayout.Validate(out var error), Is.True, error);
    }

    [Test]
    public void Scrollback_AccumulatesBeyondViewport()
    {
        using var surface = NewSurface(20, 5);
        for (int i = 0; i < 100; i++)
        {
            WriteText(surface, $"line {i}\r\n");
        }

        var frame = surface.BuildFrame();
        Assert.That(frame.Scrollbar.ScrollbackHeight, Is.GreaterThan(0),
            "100 lines into a 5-row viewport should produce scrollback.");
    }
}
