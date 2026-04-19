using caTTY.Core.Terminal;
using caTTY.Core.Types;
using FsCheck;
using NUnit.Framework;
using Microsoft.Extensions.Logging.Abstractions;

namespace caTTY.Core.Tests.Property;

/// <summary>
///     Property-based tests for alternate screen buffer operations in the terminal emulator.
///     These tests verify universal properties that should hold for all alternate screen buffer operations.
///     Validates Requirements 15.1, 15.2, 15.3, 15.4, 15.5.
/// </summary>
[TestFixture]
[Category("Property")]
public class AlternateScreenBufferProperties
{
    /// <summary>
    ///     Generator for valid terminal dimensions.
    /// </summary>
    public static Arbitrary<(int width, int height)> TerminalDimensionsArb =>
        Arb.From(Gen.Choose(10, 80).SelectMany(width =>
            Gen.Choose(5, 50).Select(height => (width, height))));

    /// <summary>
    ///     Generator for cursor positions.
    /// </summary>
    public static Arbitrary<(int row, int col)> CursorPositionArb =>
        Arb.From(Gen.Choose(1, 24).SelectMany(row =>
            Gen.Choose(1, 80).Select(col => (row, col))));

    /// <summary>
    ///     Generator for printable text content (avoiding control characters except tab).
    /// </summary>
    public static Arbitrary<string> PrintableTextArb =>
        Arb.From(Gen.ArrayOf(Gen.Choose(0x20, 0x7E).Select(c => (char)c))
            .Select(chars => new string(chars))
            .Where(s => s.Length > 0 && s.Length <= 30));

    /// <summary>
    ///     Generator for content items with optional newlines.
    /// </summary>
    public static Arbitrary<(string text, bool addNewline)> ContentItemArb =>
        Arb.From(PrintableTextArb.Generator.SelectMany(text =>
            Arb.Generate<bool>().Select(addNewline => (text, addNewline))));

    /// <summary>
    ///     Generator for arrays of content items.
    /// </summary>
    public static Arbitrary<(string text, bool addNewline)[]> ContentArrayArb =>
        Arb.From(Gen.ArrayOf(ContentItemArb.Generator)
            .Where(arr => arr.Length > 0 && arr.Length <= 10));

    /// <summary>
    ///     **Feature: catty-ksa, Property 29: Alternate screen buffer switching**
    ///     **Validates: Requirements 15.1, 15.2, 15.4**
    ///     Property: For any terminal state, switching to alternate screen buffer via DECSET 47
    ///     should result in using alternate buffer.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property AlternateScreenBufferSwitching()
    {
        return Prop.ForAll(TerminalDimensionsArb, ContentArrayArb, CursorPositionArb,
            (dimensions, contentItems, cursorPos) =>
        {
            var (width, height) = dimensions;

            using var terminal = TerminalEmulator.Create(width, height, 100, NullLogger.Instance);

            // Initially, terminal should be on primary screen
            bool initiallyOnPrimary = !terminal.State.IsAlternateScreenActive;

            // Write some content to primary screen
            foreach (var (text, addNewline) in contentItems)
            {
                terminal.Write(text);
                if (addNewline)
                {
                    terminal.Write("\r\n");
                }
            }

            // Move cursor to a specific position
            var clampedRow = Math.Min(cursorPos.row, height);
            var clampedCol = Math.Min(cursorPos.col, width);
            terminal.Write($"\x1b[{clampedRow};{clampedCol}H");

            // Capture primary screen state before switching
            var primaryCellsSnapshot = CaptureScreenContent(terminal, width, height);

            // Switch to alternate screen buffer (DECSET 47)
            terminal.Write("\x1b[?47h");

            // KEY PROPERTY: After DECSET 47, terminal should be using alternate buffer
            bool switchedToAlternate = terminal.State.IsAlternateScreenActive;

            // Write a unique marker to alternate screen at a known position
            terminal.Write("\x1b[1;1H"); // Move to top-left
            terminal.Write("XALTX"); // Unique marker that won't appear in random content

            bool markerWrittenToAlternate = terminal.ScreenBuffer.GetCell(0, 0).Character == 'X';

            // Switch back to primary screen (DECRST 47)
            terminal.Write("\x1b[?47l");

            // After switching back, should be on primary screen
            bool backToPrimary = !terminal.State.IsAlternateScreenActive;

            // KEY PROPERTY: Primary screen content should be exactly as it was before switching
            var primaryStateAfter = CaptureScreenContent(terminal, width, height);
            bool primaryContentPreserved = CompareScreenContent(primaryCellsSnapshot, primaryStateAfter, width, height);

            // Switch back to alternate to verify it still has the marker
            terminal.Write("\x1b[?47h");
            bool backToAlternate = terminal.State.IsAlternateScreenActive;

            bool markerStillPresent = terminal.ScreenBuffer.GetCell(0, 0).Character == 'X';

            return initiallyOnPrimary && switchedToAlternate && markerWrittenToAlternate &&
                   backToPrimary && primaryContentPreserved && backToAlternate && markerStillPresent;
        });
    }

    /// <summary>
    ///     **Feature: catty-ksa, Property 30: Screen buffer round-trip**
    ///     **Validates: Requirements 15.2, 15.3**
    ///     Property: For any terminal state, switching to alternate screen then back to normal
    ///     should restore the original screen buffer.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property ScreenBufferRoundTrip()
    {
        return Prop.ForAll(TerminalDimensionsArb, ContentArrayArb, ContentArrayArb,
            (dimensions, primaryContent, alternateContent) =>
        {
            var (width, height) = dimensions;

            using var terminal = TerminalEmulator.Create(width, height, 100, NullLogger.Instance);

            // Write content to primary screen
            foreach (var (text, addNewline) in primaryContent)
            {
                terminal.Write(text);
                if (addNewline)
                {
                    terminal.Write("\r\n");
                }
            }

            // Capture the complete primary screen state before switching
            var primaryCellsSnapshot = CaptureScreenContent(terminal, width, height);
            var primaryCursorXBefore = terminal.Cursor.Col;
            var primaryCursorYBefore = terminal.Cursor.Row;

            // Switch to alternate screen buffer (DECSET 47)
            terminal.Write("\x1b[?47h");
            bool switchedToAlternate = terminal.State.IsAlternateScreenActive;

            // Write different content to alternate screen
            foreach (var (text, addNewline) in alternateContent)
            {
                terminal.Write(text);
                if (addNewline)
                {
                    terminal.Write("\r\n");
                }
            }

            // Verify we're still on alternate screen
            bool stillOnAlternate = terminal.State.IsAlternateScreenActive;

            // Switch back to primary screen (DECRST 47)
            terminal.Write("\x1b[?47l");

            // KEY PROPERTY: After round-trip, should be back on primary screen
            bool backToPrimary = !terminal.State.IsAlternateScreenActive;

            // KEY PROPERTY: Primary screen content should be exactly as it was before switching
            bool contentPreserved = CompareScreenContent(primaryCellsSnapshot,
                CaptureScreenContent(terminal, width, height), width, height);

            // Verify cursor position is preserved
            bool cursorPreserved = terminal.Cursor.Col == primaryCursorXBefore &&
                                   terminal.Cursor.Row == primaryCursorYBefore;

            return switchedToAlternate && stillOnAlternate && backToPrimary &&
                   contentPreserved && cursorPreserved;
        });
    }

    /// <summary>
    ///     **Feature: catty-ksa, Property 31: Buffer content preservation**
    ///     **Validates: Requirements 15.3, 15.5**
    ///     Property: For any content written to one screen buffer, switching to the other buffer
    ///     and back should preserve the original content unchanged.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 50, QuietOnSuccess = true)]
    public FsCheck.Property BufferContentPreservation()
    {
        return Prop.ForAll(TerminalDimensionsArb, ContentArrayArb, ContentArrayArb,
            (dimensions, primaryContent1, alternateContent1) =>
        {
            var (width, height) = dimensions;

            using var terminal = TerminalEmulator.Create(width, height, 100, NullLogger.Instance);

            // Phase 1: Write initial content to primary screen
            foreach (var (text, addNewline) in primaryContent1)
            {
                terminal.Write(text);
                if (addNewline)
                {
                    terminal.Write("\r\n");
                }
            }

            // Capture primary screen state after first write
            var primaryCells1 = CaptureScreenContent(terminal, width, height);

            // Phase 2: Switch to alternate and write content
            terminal.Write("\x1b[?47h");
            bool switchedToAlternate = terminal.State.IsAlternateScreenActive;

            foreach (var (text, addNewline) in alternateContent1)
            {
                terminal.Write(text);
                if (addNewline)
                {
                    terminal.Write("\r\n");
                }
            }

            // Capture alternate screen state after first write
            var alternateCells1 = CaptureScreenContent(terminal, width, height);

            // Phase 3: Switch back to primary and verify preservation
            terminal.Write("\x1b[?47l");
            bool backToPrimary1 = !terminal.State.IsAlternateScreenActive;

            var primaryCells1Restored = CaptureScreenContent(terminal, width, height);
            bool primaryPreserved1 = CompareScreenContent(primaryCells1, primaryCells1Restored, width, height);

            // Phase 4: Switch to alternate and verify its content is still preserved
            terminal.Write("\x1b[?47h");
            bool switchedToAlternate2 = terminal.State.IsAlternateScreenActive;

            var alternateCells1Restored = CaptureScreenContent(terminal, width, height);
            bool alternatePreserved1 = CompareScreenContent(alternateCells1, alternateCells1Restored, width, height);

            // KEY PROPERTY: Each buffer independently preserves its content across multiple switches
            return switchedToAlternate && backToPrimary1 && primaryPreserved1 &&
                   switchedToAlternate2 && alternatePreserved1;
        });
    }

    /// <summary>
    ///     **Feature: catty-ksa, Property 32: Alternate screen scrollback isolation**
    ///     **Validates: Requirements 15.3**
    ///     Property: For any scrolling operations in alternate screen mode,
    ///     scrollback buffer should not be affected.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property AlternateScreenScrollbackIsolation()
    {
        return Prop.ForAll(TerminalDimensionsArb, ContentArrayArb,
            (dimensions, contentItems) =>
        {
            var (width, height) = dimensions;

            using var terminal = TerminalEmulator.Create(width, height, 100, NullLogger.Instance);

            // Add some content to scrollback in primary screen first
            for (int i = 0; i < height + 2; i++)
            {
                terminal.Write($"Primary line {i}\r\n");
            }

            var initialScrollbackLines = terminal.ScrollbackManager.CurrentLines;
            bool hasInitialScrollback = initialScrollbackLines > 0;

            // Switch to alternate screen
            terminal.Write("\x1b[?47h");
            bool switchedToAlternate = terminal.State.IsAlternateScreenActive;

            // Fill alternate screen and cause scrolling
            for (int i = 0; i < height + 5; i++)
            {
                terminal.Write($"Alternate line {i}\r\n");
            }

            // Write additional content to cause more scrolling
            foreach (var (text, addNewline) in contentItems)
            {
                terminal.Write(text);
                if (addNewline)
                {
                    terminal.Write("\r\n");
                }
            }

            // KEY PROPERTY: Scrollback should not have increased while in alternate screen
            var scrollbackAfterAlternate = terminal.ScrollbackManager.CurrentLines;
            bool scrollbackUnchanged = scrollbackAfterAlternate == initialScrollbackLines;

            // Switch back to primary screen
            terminal.Write("\x1b[?47l");
            bool backToPrimary = !terminal.State.IsAlternateScreenActive;

            // Verify scrollback is still the same
            var finalScrollbackLines = terminal.ScrollbackManager.CurrentLines;
            bool scrollbackStillUnchanged = finalScrollbackLines == initialScrollbackLines;

            return hasInitialScrollback && switchedToAlternate && scrollbackUnchanged &&
                   backToPrimary && scrollbackStillUnchanged;
        });
    }

    /// <summary>
    ///     **Feature: catty-ksa, Property 33: Cursor save and restore with mode 1047**
    ///     **Validates: Requirements 15.2, 15.5**
    ///     Property: For any cursor position, DECSET 1047 should save cursor position
    ///     and DECRST 1047 should restore it.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property CursorSaveRestoreMode1047()
    {
        return Prop.ForAll(TerminalDimensionsArb, CursorPositionArb, PrintableTextArb,
            (dimensions, cursorPos, testText) =>
        {
            var (width, height) = dimensions;

            using var terminal = TerminalEmulator.Create(width, height, 100, NullLogger.Instance);

            // Move cursor to a specific position and write text
            var clampedRow = Math.Min(cursorPos.row, height);
            var clampedCol = Math.Min(cursorPos.col, width);
            terminal.Write($"\x1b[{clampedRow};{clampedCol}H");
            terminal.Write(testText);

            var savedCursorX = terminal.Cursor.Col;
            var savedCursorY = terminal.Cursor.Row;

            // Switch to alternate with cursor save (DECSET 1047)
            terminal.Write("\x1b[?1047h");
            bool switchedToAlternate = terminal.State.IsAlternateScreenActive;

            // Move cursor in alternate screen
            terminal.Write("\x1b[1;1H");
            terminal.Write("Alt");

            bool cursorMovedInAlternate = terminal.Cursor.Col != savedCursorX ||
                                          terminal.Cursor.Row != savedCursorY;

            // Switch back with cursor restore (DECRST 1047)
            terminal.Write("\x1b[?1047l");
            bool backToPrimary = !terminal.State.IsAlternateScreenActive;

            // KEY PROPERTY: Cursor should be restored to saved position
            bool cursorRestored = terminal.Cursor.Col == savedCursorX &&
                                  terminal.Cursor.Row == savedCursorY;

            return switchedToAlternate && cursorMovedInAlternate && backToPrimary && cursorRestored;
        });
    }

    /// <summary>
    ///     **Feature: catty-ksa, Property 34: Clear alternate screen with mode 1049**
    ///     **Validates: Requirements 15.1, 15.2, 15.5**
    ///     Property: For any terminal state, DECSET 1049 should clear alternate screen
    ///     and position cursor at origin.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property ClearAlternateScreenMode1049()
    {
        return Prop.ForAll(TerminalDimensionsArb, PrintableTextArb,
            (dimensions, primaryText) =>
        {
            var (width, height) = dimensions;

            using var terminal = TerminalEmulator.Create(width, height, 100, NullLogger.Instance);

            // Write to primary screen
            terminal.Write(primaryText);
            var primarySnapshot = CaptureScreenContent(terminal, width, height);

            // Switch to alternate with cursor save and clear (DECSET 1049)
            terminal.Write("\x1b[?1049h");
            bool switchedToAlternate = terminal.State.IsAlternateScreenActive;

            // KEY PROPERTY: Alternate screen should be clear and cursor at origin
            bool cursorAtOrigin = terminal.Cursor.Col == 0 && terminal.Cursor.Row == 0;
            bool screenCleared = terminal.ScreenBuffer.GetCell(0, 0).Character == ' ';

            // Write to alternate screen
            terminal.Write("Alternate");

            // Switch back (DECRST 1049)
            terminal.Write("\x1b[?1049l");
            bool backToPrimary = !terminal.State.IsAlternateScreenActive;

            // KEY PROPERTY: Primary screen content should be preserved
            var primaryRestored = CaptureScreenContent(terminal, width, height);
            bool primaryPreserved = CompareScreenContent(primarySnapshot, primaryRestored, width, height);

            return switchedToAlternate && cursorAtOrigin && screenCleared &&
                   backToPrimary && primaryPreserved;
        });
    }

    /// <summary>
    ///     Helper method to capture current screen content.
    /// </summary>
    private static Cell[,] CaptureScreenContent(TerminalEmulator terminal, int width, int height)
    {
        var content = new Cell[height, width];

        for (int row = 0; row < height; row++)
        {
            for (int col = 0; col < width; col++)
            {
                content[row, col] = terminal.ScreenBuffer.GetCell(row, col);
            }
        }

        return content;
    }

    /// <summary>
    ///     Helper method to compare two screen content snapshots.
    /// </summary>
    private static bool CompareScreenContent(Cell[,] content1, Cell[,] content2, int width, int height)
    {
        for (int row = 0; row < height; row++)
        {
            for (int col = 0; col < width; col++)
            {
                if (content1[row, col].Character != content2[row, col].Character)
                {
                    return false;
                }
            }
        }
        return true;
    }
}
