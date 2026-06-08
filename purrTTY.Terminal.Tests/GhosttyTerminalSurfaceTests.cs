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
