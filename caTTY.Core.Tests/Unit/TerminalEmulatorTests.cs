using caTTY.Core.Terminal;
using caTTY.Core.Types;
using NUnit.Framework;

namespace caTTY.Core.Tests.Unit;

/// <summary>
///     Unit tests for the TerminalEmulator class.
/// </summary>
[TestFixture]
[Category("Unit")]
public class TerminalEmulatorTests
{
    /// <summary>
    ///     Tests that TerminalEmulator constructor creates a terminal with valid dimensions.
    /// </summary>
    [Test]
    public void Constructor_WithValidDimensions_CreatesTerminal()
    {
        // Arrange & Act
        var terminal = TerminalEmulator.Create(80, 24);

        // Assert
        Assert.That(terminal.Width, Is.EqualTo(80));
        Assert.That(terminal.Height, Is.EqualTo(24));
        Assert.That(terminal.Cursor.Row, Is.EqualTo(0));
        Assert.That(terminal.Cursor.Col, Is.EqualTo(0));
    }

    /// <summary>
    ///     Tests that TerminalEmulator constructor throws ArgumentOutOfRangeException for invalid width.
    /// </summary>
    [Test]
    public void Constructor_WithInvalidWidth_ThrowsArgumentOutOfRangeException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => TerminalEmulator.Create(0, 24));
        Assert.Throws<ArgumentOutOfRangeException>(() => TerminalEmulator.Create(-1, 24));
        Assert.Throws<ArgumentOutOfRangeException>(() => TerminalEmulator.Create(1001, 24));
    }

    /// <summary>
    ///     Tests that TerminalEmulator constructor throws ArgumentOutOfRangeException for invalid height.
    /// </summary>
    [Test]
    public void Constructor_WithInvalidHeight_ThrowsArgumentOutOfRangeException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => TerminalEmulator.Create(80, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => TerminalEmulator.Create(80, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => TerminalEmulator.Create(80, 1001));
    }

    /// <summary>
    ///     Tests that Write with a printable character writes the character at cursor position and advances cursor.
    /// </summary>
    [Test]
    public void Write_WithPrintableCharacter_WritesCharacterAtCursor()
    {
        // Arrange
        var terminal = TerminalEmulator.Create(80, 24);
        byte[] data = new[] { (byte)'A' };

        // Act
        terminal.Write(data);

        // Assert
        Cell cell = terminal.ScreenBuffer.GetCell(0, 0);
        Assert.That(cell.Character, Is.EqualTo('A'));
        Assert.That(terminal.Cursor.Col, Is.EqualTo(1)); // Cursor should advance
    }

    /// <summary>
    ///     Tests that Write with multiple characters writes all characters sequentially.
    /// </summary>
    [Test]
    public void Write_WithMultipleCharacters_WritesAllCharacters()
    {
        // Arrange
        var terminal = TerminalEmulator.Create(80, 24);
        byte[] data = new[] { (byte)'H', (byte)'e', (byte)'l', (byte)'l', (byte)'o' };

        // Act
        terminal.Write(data);

        // Assert
        Assert.That(terminal.ScreenBuffer.GetCell(0, 0).Character, Is.EqualTo('H'));
        Assert.That(terminal.ScreenBuffer.GetCell(0, 1).Character, Is.EqualTo('e'));
        Assert.That(terminal.ScreenBuffer.GetCell(0, 2).Character, Is.EqualTo('l'));
        Assert.That(terminal.ScreenBuffer.GetCell(0, 3).Character, Is.EqualTo('l'));
        Assert.That(terminal.ScreenBuffer.GetCell(0, 4).Character, Is.EqualTo('o'));
        Assert.That(terminal.Cursor.Col, Is.EqualTo(5));
    }

    /// <summary>
    ///     Tests that Write with a string writes the string content to the terminal.
    /// </summary>
    [Test]
    public void Write_WithString_WritesStringContent()
    {
        // Arrange
        var terminal = TerminalEmulator.Create(80, 24);

        // Act
        terminal.Write("Test");

        // Assert
        Assert.That(terminal.ScreenBuffer.GetCell(0, 0).Character, Is.EqualTo('T'));
        Assert.That(terminal.ScreenBuffer.GetCell(0, 1).Character, Is.EqualTo('e'));
        Assert.That(terminal.ScreenBuffer.GetCell(0, 2).Character, Is.EqualTo('s'));
        Assert.That(terminal.ScreenBuffer.GetCell(0, 3).Character, Is.EqualTo('t'));
    }

    /// <summary>
    ///     Tests that Write with line feed (LF) moves cursor down one row.
    /// </summary>
    [Test]
    public void Write_WithLineFeed_MovesCursorDown()
    {
        // Arrange
        var terminal = TerminalEmulator.Create(80, 24);
        terminal.Write("A");
        byte[] data = new byte[] { 0x0A }; // LF

        // Act
        terminal.Write(data);

        // Assert
        Assert.That(terminal.Cursor.Row, Is.EqualTo(1));
        Assert.That(terminal.Cursor.Col, Is.EqualTo(1)); // Line feed only moves down, keeps same column
    }

    /// <summary>
    ///     Tests that Write with carriage return (CR) moves cursor to column zero.
    /// </summary>
    [Test]
    public void Write_WithCarriageReturn_MovesCursorToColumnZero()
    {
        // Arrange
        var terminal = TerminalEmulator.Create(80, 24);
        terminal.Write("Hello");
        byte[] data = new byte[] { 0x0D }; // CR

        // Act
        terminal.Write(data);

        // Assert
        Assert.That(terminal.Cursor.Row, Is.EqualTo(0));
        Assert.That(terminal.Cursor.Col, Is.EqualTo(0));
    }

    /// <summary>
    ///     Tests that Write with CRLF sequence moves cursor to the beginning of the next line.
    /// </summary>
    [Test]
    public void Write_WithCRLF_MovesCursorToNextLineStart()
    {
        // Arrange
        var terminal = TerminalEmulator.Create(80, 24);
        terminal.Write("Hello");
        byte[] data = new byte[] { 0x0D, 0x0A }; // CR LF

        // Act
        terminal.Write(data);

        // Assert
        Assert.That(terminal.Cursor.Row, Is.EqualTo(1));
        Assert.That(terminal.Cursor.Col, Is.EqualTo(0));
    }

    /// <summary>
    ///     Tests that Write with tab character moves cursor to the next tab stop.
    /// </summary>
    [Test]
    public void Write_WithTab_MovesCursorToNextTabStop()
    {
        // Arrange
        var terminal = TerminalEmulator.Create(80, 24);
        byte[] data = new byte[] { 0x09 }; // TAB

        // Act
        terminal.Write(data);

        // Assert
        Assert.That(terminal.Cursor.Col, Is.EqualTo(8)); // Next tab stop at column 8
    }

    /// <summary>
    ///     Tests that Write with bell character raises Bell event.
    /// </summary>
    [Test]
    public void Write_WithBell_RaisesBellEvent()
    {
        // Arrange
        var terminal = TerminalEmulator.Create(80, 24);
        bool bellEventRaised = false;
        BellEventArgs? bellEventArgs = null;
        terminal.Bell += (sender, args) =>
        {
            bellEventRaised = true;
            bellEventArgs = args;
        };
        byte[] data = new byte[] { 0x07 }; // BEL

        // Act
        terminal.Write(data);

        // Assert
        Assert.That(bellEventRaised, Is.True);
        Assert.That(bellEventArgs, Is.Not.Null);
        Assert.That(bellEventArgs.Timestamp, Is.LessThanOrEqualTo(DateTime.UtcNow));
    }

    /// <summary>
    ///     Tests that Write with backspace character moves cursor left if not at column 0.
    /// </summary>
    [Test]
    public void Write_WithBackspace_MovesCursorLeft()
    {
        // Arrange
        var terminal = TerminalEmulator.Create(80, 24);
        terminal.Write("ABC"); // Move cursor to column 3
        byte[] data = new byte[] { 0x08 }; // BS

        // Act
        terminal.Write(data);

        // Assert
        Assert.That(terminal.Cursor.Col, Is.EqualTo(2)); // Should move left from 3 to 2
        Assert.That(terminal.Cursor.Row, Is.EqualTo(0)); // Row should stay the same
    }

    /// <summary>
    ///     Tests that Write with backspace character at column 0 does not move cursor.
    /// </summary>
    [Test]
    public void Write_WithBackspaceAtColumnZero_DoesNotMoveCursor()
    {
        // Arrange
        var terminal = TerminalEmulator.Create(80, 24);
        // Cursor is already at (0, 0)
        byte[] data = new byte[] { 0x08 }; // BS

        // Act
        terminal.Write(data);

        // Assert
        Assert.That(terminal.Cursor.Col, Is.EqualTo(0)); // Should stay at column 0
        Assert.That(terminal.Cursor.Row, Is.EqualTo(0)); // Row should stay the same
    }

    /// <summary>
    ///     Tests that Write with multiple control characters handles them all correctly.
    /// </summary>
    [Test]
    public void Write_WithMultipleControlCharacters_HandlesAllCorrectly()
    {
        // Arrange
        var terminal = TerminalEmulator.Create(80, 24);
        bool bellEventRaised = false;
        int bellCount = 0;
        terminal.Bell += (sender, args) =>
        {
            bellEventRaised = true;
            bellCount++;
        };
        byte[] data = new byte[] { 0x07, 0x07, 0x07 }; // Three BEL characters

        // Act
        terminal.Write(data);

        // Assert
        Assert.That(bellEventRaised, Is.True);
        Assert.That(bellCount, Is.EqualTo(3));
    }

    /// <summary>
    ///     Tests that Write with mixed control characters and text works correctly.
    /// </summary>
    [Test]
    public void Write_WithMixedControlCharactersAndText_WorksCorrectly()
    {
        // Arrange
        var terminal = TerminalEmulator.Create(80, 24);
        byte[] data = new byte[] { (byte)'a', 0x07, (byte)'b', 0x08, (byte)'c' }; // a BEL b BS c

        // Act
        terminal.Write(data);

        // Assert
        // Sequence: 'a' at (0,0) → cursor (0,1), BEL → cursor (0,1), 'b' at (0,1) → cursor (0,2), BS → cursor (0,1), 'c' at (0,1) → cursor (0,2)
        Assert.That(terminal.ScreenBuffer.GetCell(0, 0).Character, Is.EqualTo('a')); // 'a' remains at (0,0)
        Assert.That(terminal.ScreenBuffer.GetCell(0, 1).Character, Is.EqualTo('c')); // 'c' overwrote 'b' at (0,1)
        Assert.That(terminal.Cursor.Col, Is.EqualTo(2)); // Cursor at column 2 after writing 'c'
    }

    /// <summary>
    ///     Tests that tab character moves to correct tab stops with default 8-column spacing.
    /// </summary>
    [Test]
    public void Write_WithTabCharacter_MovesToCorrectTabStops()
    {
        // Arrange
        var terminal = TerminalEmulator.Create(80, 24);

        // Act & Assert - Test multiple tab stops
        terminal.Write("\t"); // Should go to column 8
        Assert.That(terminal.Cursor.Col, Is.EqualTo(8));

        terminal.Write("\t"); // Should go to column 16
        Assert.That(terminal.Cursor.Col, Is.EqualTo(16));

        terminal.Write("\t"); // Should go to column 24
        Assert.That(terminal.Cursor.Col, Is.EqualTo(24));
    }

    /// <summary>
    ///     Tests that tab character at near right edge goes to right edge.
    /// </summary>
    [Test]
    public void Write_WithTabNearRightEdge_GoesToRightEdge()
    {
        // Arrange
        var terminal = TerminalEmulator.Create(10, 24); // Small width
        terminal.Write("1234567"); // Move to column 7

        // Act
        terminal.Write("\t"); // Should go to column 8 (next tab stop)

        // Assert
        Assert.That(terminal.Cursor.Col, Is.EqualTo(8));

        // Another tab should go to right edge (column 9)
        terminal.Write("\t");
        Assert.That(terminal.Cursor.Col, Is.EqualTo(9));
    }

    /// <summary>
    ///     Tests that backspace clears wrap pending state.
    /// </summary>
    [Test]
    public void Write_WithBackspaceAfterWrapPending_ClearsWrapPending()
    {
        // Arrange
        var terminal = TerminalEmulator.Create(5, 24); // Small width
        terminal.Write("12345"); // Fill first line, should set wrap pending

        // Act
        terminal.Write("\x08"); // Backspace

        // Assert
        Assert.That(terminal.Cursor.Col, Is.EqualTo(3)); // Should move back from 4 to 3
        Assert.That(terminal.Cursor.Row, Is.EqualTo(0)); // Should stay on same row

        // Writing another character should not wrap
        terminal.Write("X");
        Assert.That(terminal.Cursor.Row, Is.EqualTo(0)); // Should still be on row 0
        Assert.That(terminal.Cursor.Col, Is.EqualTo(4)); // Should be at column 4
    }

    /// <summary>
    ///     Tests that tab clears wrap pending state.
    /// </summary>
    [Test]
    public void Write_WithTabAfterWrapPending_ClearsWrapPending()
    {
        // Arrange
        var terminal = TerminalEmulator.Create(5, 24); // Small width
        terminal.Write("12345"); // Fill first line, should set wrap pending

        // Act
        terminal.Write("\t"); // Tab

        // Assert
        Assert.That(terminal.Cursor.Col, Is.EqualTo(4)); // Should stay at right edge
        Assert.That(terminal.Cursor.Row, Is.EqualTo(0)); // Should stay on same row
    }

    /// <summary>
    ///     Tests that Write with DEL character ignores the character and doesn't move cursor.
    /// </summary>
    [Test]
    public void Write_WithDEL_IgnoresCharacter()
    {
        // Arrange
        var terminal = TerminalEmulator.Create(80, 24);
        terminal.Write("A");
        byte[] data = new byte[] { 0x7F }; // DEL

        // Act
        terminal.Write(data);

        // Assert
        Assert.That(terminal.Cursor.Col, Is.EqualTo(1)); // Cursor should not move
        Assert.That(terminal.ScreenBuffer.GetCell(0, 1).Character, Is.EqualTo(' ')); // Should remain empty
    }

    /// <summary>
    ///     Tests that Write at the right edge of terminal wraps to the next line.
    /// </summary>
    [Test]
    public void Write_AtRightEdge_WrapsToNextLine()
    {
        // Arrange
        var terminal = TerminalEmulator.Create(5, 24); // Small width for testing
        terminal.Write("12345"); // Fill first line

        // Act
        terminal.Write("6"); // Should wrap to next line

        // Assert
        Assert.That(terminal.Cursor.Row, Is.EqualTo(1));
        Assert.That(terminal.Cursor.Col, Is.EqualTo(1));
        Assert.That(terminal.ScreenBuffer.GetCell(1, 0).Character, Is.EqualTo('6'));
    }

    /// <summary>
    ///     Tests that Write raises ScreenUpdated event when content is written.
    /// </summary>
    [Test]
    public void Write_RaisesScreenUpdatedEvent()
    {
        // Arrange
        var terminal = TerminalEmulator.Create(80, 24);
        bool eventRaised = false;
        terminal.ScreenUpdated += (sender, args) => eventRaised = true;

        // Act
        terminal.Write("A");

        // Assert
        Assert.That(eventRaised, Is.True);
    }

    /// <summary>
    ///     Tests that Write with empty data does not raise ScreenUpdated event.
    /// </summary>
    [Test]
    public void Write_WithEmptyData_DoesNotRaiseEvent()
    {
        // Arrange
        var terminal = TerminalEmulator.Create(80, 24);
        bool eventRaised = false;
        terminal.ScreenUpdated += (sender, args) => eventRaised = true;

        // Act
        terminal.Write(ReadOnlySpan<byte>.Empty);

        // Assert
        Assert.That(eventRaised, Is.False);
    }

    /// <summary>
    ///     Tests that Dispose can be called multiple times without throwing.
    /// </summary>
    [Test]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        // Arrange
        var terminal = TerminalEmulator.Create(80, 24);

        // Act & Assert
        Assert.DoesNotThrow(() =>
        {
            terminal.Dispose();
            terminal.Dispose();
        });
    }

    /// <summary>
    ///     Tests that Write throws ObjectDisposedException after the terminal is disposed.
    /// </summary>
    [Test]
    public void Write_AfterDispose_ThrowsObjectDisposedException()
    {
        // Arrange
        var terminal = TerminalEmulator.Create(80, 24);
        terminal.Dispose();

        // Act & Assert
        Assert.Throws<ObjectDisposedException>(() => terminal.Write("Test"));
    }

    /// <summary>
    ///     Tests that ScrollViewportUp disables auto-scroll and updates viewport offset.
    /// </summary>
    [Test]
    public void ScrollViewportUp_FromBottom_DisablesAutoScroll()
    {
        // Arrange
        var terminal = TerminalEmulator.Create(80, 3, 100); // Small terminal to force scrollback
        
        // Fill the screen and add more content to create scrollback
        for (int i = 0; i < 6; i++) // More lines than screen height
        {
            terminal.Write($"Line {i}\n");
        }
        
        Assert.That(terminal.IsAutoScrollEnabled, Is.True);
        Assert.That(terminal.ViewportOffset, Is.EqualTo(0));

        // Act
        terminal.ScrollViewportUp(2);

        // Assert
        Assert.That(terminal.IsAutoScrollEnabled, Is.False);
        Assert.That(terminal.ViewportOffset, Is.EqualTo(2));
    }

    /// <summary>
    ///     Tests that ScrollViewportDown to bottom re-enables auto-scroll.
    /// </summary>
    [Test]
    public void ScrollViewportDown_ToBottom_EnablesAutoScroll()
    {
        // Arrange
        var terminal = TerminalEmulator.Create(80, 3, 100); // Small terminal to force scrollback
        
        // Fill the screen and add more content to create scrollback
        for (int i = 0; i < 6; i++) // More lines than screen height
        {
            terminal.Write($"Line {i}\n");
        }
        
        terminal.ScrollViewportUp(3); // Scroll up first
        Assert.That(terminal.IsAutoScrollEnabled, Is.False);

        // Act
        terminal.ScrollViewportDown(3); // Scroll back to bottom

        // Assert
        Assert.That(terminal.IsAutoScrollEnabled, Is.True);
        Assert.That(terminal.ViewportOffset, Is.EqualTo(0));
    }

    /// <summary>
    ///     Tests that ScrollViewportToTop scrolls to the top and disables auto-scroll.
    /// </summary>
    [Test]
    public void ScrollViewportToTop_DisablesAutoScroll()
    {
        // Arrange
        var terminal = TerminalEmulator.Create(80, 3, 100); // Small terminal to force scrollback
        
        // Fill the screen and add more content to create scrollback
        for (int i = 0; i < 6; i++) // More lines than screen height
        {
            terminal.Write($"Line {i}\n");
        }

        // Act
        terminal.ScrollViewportToTop();

        // Assert
        Assert.That(terminal.IsAutoScrollEnabled, Is.False);
        Assert.That(terminal.ViewportOffset, Is.GreaterThan(0));
    }

    /// <summary>
    ///     Tests that ScrollViewportToBottom scrolls to bottom and enables auto-scroll.
    /// </summary>
    [Test]
    public void ScrollViewportToBottom_EnablesAutoScroll()
    {
        // Arrange
        var terminal = TerminalEmulator.Create(80, 3, 100); // Small terminal to force scrollback
        
        // Fill the screen and add more content to create scrollback
        for (int i = 0; i < 6; i++) // More lines than screen height
        {
            terminal.Write($"Line {i}\n");
        }
        
        terminal.ScrollViewportToTop(); // Scroll to top first
        Assert.That(terminal.IsAutoScrollEnabled, Is.False);

        // Act
        terminal.ScrollViewportToBottom();

        // Assert
        Assert.That(terminal.IsAutoScrollEnabled, Is.True);
        Assert.That(terminal.ViewportOffset, Is.EqualTo(0));
    }

    /// <summary>
    ///     Tests that viewport methods throw ObjectDisposedException after disposal.
    /// </summary>
    [Test]
    public void ViewportMethods_AfterDispose_ThrowObjectDisposedException()
    {
        // Arrange
        var terminal = TerminalEmulator.Create(80, 24);
        terminal.Dispose();

        // Act & Assert
        Assert.Throws<ObjectDisposedException>(() => terminal.ScrollViewportUp(1));
        Assert.Throws<ObjectDisposedException>(() => terminal.ScrollViewportDown(1));
        Assert.Throws<ObjectDisposedException>(() => terminal.ScrollViewportToTop());
        Assert.Throws<ObjectDisposedException>(() => terminal.ScrollViewportToBottom());
    }

    /// <summary>
    ///     Tests that Resize with valid dimensions updates terminal dimensions.
    /// </summary>
    [Test]
    public void Resize_WithValidDimensions_UpdatesDimensions()
    {
        // Arrange
        var terminal = TerminalEmulator.Create(80, 24);

        // Act
        terminal.Resize(100, 30);

        // Assert
        Assert.That(terminal.Width, Is.EqualTo(100));
        Assert.That(terminal.Height, Is.EqualTo(30));
    }

    /// <summary>
    ///     Tests that Resize preserves cursor position when possible.
    /// </summary>
    [Test]
    public void Resize_PreservesCursorPosition()
    {
        // Arrange
        var terminal = TerminalEmulator.Create(80, 24);
        terminal.Write("Hello");
        // Cursor should be at (0, 5)

        // Act - resize to larger dimensions
        terminal.Resize(120, 40);

        // Assert - cursor position preserved
        Assert.That(terminal.Cursor.Row, Is.EqualTo(0));
        Assert.That(terminal.Cursor.Col, Is.EqualTo(5));
    }

    /// <summary>
    ///     Tests that Resize clamps cursor position when dimensions shrink.
    /// </summary>
    [Test]
    public void Resize_ClampsCursorPosition()
    {
        // Arrange
        var terminal = TerminalEmulator.Create(80, 24);
        // Move cursor to position that will be out of bounds after resize
        terminal.Write(new string(' ', 70)); // Move cursor to column 70
        
        // Act - resize to smaller width
        terminal.Resize(50, 24);

        // Assert - cursor clamped to new bounds
        Assert.That(terminal.Width, Is.EqualTo(50));
        Assert.That(terminal.Cursor.Col, Is.LessThan(50));
    }

    /// <summary>
    ///     Tests that Resize preserves content within new bounds.
    /// </summary>
    [Test]
    public void Resize_PreservesContentWithinBounds()
    {
        // Arrange
        var terminal = TerminalEmulator.Create(80, 24);
        terminal.Write("Test content");

        // Act - resize to larger dimensions
        terminal.Resize(120, 40);

        // Assert - content preserved
        Assert.That(terminal.ScreenBuffer.GetCell(0, 0).Character, Is.EqualTo('T'));
        Assert.That(terminal.ScreenBuffer.GetCell(0, 1).Character, Is.EqualTo('e'));
        Assert.That(terminal.ScreenBuffer.GetCell(0, 2).Character, Is.EqualTo('s'));
        Assert.That(terminal.ScreenBuffer.GetCell(0, 3).Character, Is.EqualTo('t'));
    }

    /// <summary>
    ///     Tests that Resize with same dimensions does nothing.
    /// </summary>
    [Test]
    public void Resize_WithSameDimensions_DoesNothing()
    {
        // Arrange
        var terminal = TerminalEmulator.Create(80, 24);
        terminal.Write("Test");
        int originalCursorCol = terminal.Cursor.Col;

        // Act
        terminal.Resize(80, 24);

        // Assert - no change
        Assert.That(terminal.Width, Is.EqualTo(80));
        Assert.That(terminal.Height, Is.EqualTo(24));
        Assert.That(terminal.Cursor.Col, Is.EqualTo(originalCursorCol));
        Assert.That(terminal.ScreenBuffer.GetCell(0, 0).Character, Is.EqualTo('T'));
    }

    /// <summary>
    ///     Tests that Resize with invalid dimensions throws ArgumentOutOfRangeException.
    /// </summary>
    [Test]
    public void Resize_WithInvalidDimensions_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var terminal = TerminalEmulator.Create(80, 24);

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => terminal.Resize(0, 24));
        Assert.Throws<ArgumentOutOfRangeException>(() => terminal.Resize(80, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => terminal.Resize(1001, 24));
        Assert.Throws<ArgumentOutOfRangeException>(() => terminal.Resize(80, 1001));
    }

    /// <summary>
    ///     Tests that Resize after disposal throws ObjectDisposedException.
    /// </summary>
    [Test]
    public void Resize_AfterDispose_ThrowsObjectDisposedException()
    {
        // Arrange
        var terminal = TerminalEmulator.Create(80, 24);
        terminal.Dispose();

        // Act & Assert
        Assert.Throws<ObjectDisposedException>(() => terminal.Resize(100, 30));
    }

    /// <summary>
    ///     Tests that CSI save/restore private mode sequences work correctly.
    /// </summary>
    [Test]
    public void Write_CsiSaveRestorePrivateModes_WorksCorrectly()
    {
        // Arrange
        var terminal = TerminalEmulator.Create(80, 24);

        // Set some initial modes
        terminal.Write("\x1b[?1h");    // Application cursor keys on
        terminal.Write("\x1b[?7l");    // Auto-wrap off
        terminal.Write("\x1b[?25l");   // Cursor invisible

        // Verify initial state
        Assert.That(terminal.ModeManager.ApplicationCursorKeys, Is.True);
        Assert.That(terminal.ModeManager.AutoWrapMode, Is.False);
        Assert.That(terminal.ModeManager.CursorVisible, Is.False);

        // Save modes 1 and 25
        terminal.Write("\x1b[?1;25s");

        // Change the modes
        terminal.Write("\x1b[?1l");    // Application cursor keys off
        terminal.Write("\x1b[?25h");   // Cursor visible

        // Verify changed state
        Assert.That(terminal.ModeManager.ApplicationCursorKeys, Is.False);
        Assert.That(terminal.ModeManager.CursorVisible, Is.True);

        // Restore the saved modes
        terminal.Write("\x1b[?1;25r");

        // Verify restored state (only modes 1 and 25 should be restored)
        Assert.That(terminal.ModeManager.ApplicationCursorKeys, Is.True);  // Restored
        Assert.That(terminal.ModeManager.CursorVisible, Is.False);         // Restored
        Assert.That(terminal.ModeManager.AutoWrapMode, Is.False);          // Not saved/restored, kept current value

        terminal.Dispose();
    }

    /// <summary>
    ///     Tests that CSI cursor style sequence (DECSCUSR) works correctly.
    /// </summary>
    [Test]
    public void Write_CsiSetCursorStyle_WorksCorrectly()
    {
        // Arrange
        var terminal = TerminalEmulator.Create(80, 24);

        // Test different cursor styles
        terminal.Write("\x1b[2 q");    // Steady block
        Assert.That(terminal.State.CursorStyle, Is.EqualTo(CursorStyle.SteadyBlock));

        terminal.Write("\x1b[4 q");    // Steady underline
        Assert.That(terminal.State.CursorStyle, Is.EqualTo(CursorStyle.SteadyUnderline));

        terminal.Write("\x1b[6 q");    // Steady bar
        Assert.That(terminal.State.CursorStyle, Is.EqualTo(CursorStyle.SteadyBar));

        // Test invalid style (should default to default style)
        terminal.Write("\x1b[10 q");   // Invalid style
        Assert.That(terminal.State.CursorStyle, Is.EqualTo(CursorStyle.Default));

        // Test style 0 (should map to default, which is equivalent to blinking block)
        terminal.Write("\x1b[0 q");    // Style 0 maps to default
        Assert.That(terminal.State.CursorStyle, Is.EqualTo(CursorStyle.Default));

        terminal.Dispose();
    }

    /// <summary>
    ///     Tests that bracketed paste mode is properly tracked.
    /// </summary>
    [Test]
    public void Write_BracketedPasteMode_WorksCorrectly()
    {
        // Arrange
        var terminal = TerminalEmulator.Create(80, 24);

        // Initially should be off
        Assert.That(terminal.ModeManager.BracketedPasteMode, Is.False);
        Assert.That(terminal.State.BracketedPasteMode, Is.False);

        // Enable bracketed paste mode
        terminal.Write("\x1b[?2004h");
        Assert.That(terminal.ModeManager.BracketedPasteMode, Is.True);
        Assert.That(terminal.State.BracketedPasteMode, Is.True);

        // Disable bracketed paste mode
        terminal.Write("\x1b[?2004l");
        Assert.That(terminal.ModeManager.BracketedPasteMode, Is.False);
        Assert.That(terminal.State.BracketedPasteMode, Is.False);

        terminal.Dispose();
    }

    /// <summary>
    ///     Tests that paste content is wrapped with escape sequences when bracketed paste mode is enabled.
    /// </summary>
    [Test]
    public void WrapPasteContent_WithBracketedPasteModeEnabled_WrapsContent()
    {
        // Arrange
        var terminal = TerminalEmulator.Create(80, 24);
        terminal.Write("\x1b[?2004h"); // Enable bracketed paste mode

        // Act
        string result = terminal.WrapPasteContent("hello world");

        // Assert
        Assert.That(result, Is.EqualTo("\x1b[200~hello world\x1b[201~"));

        terminal.Dispose();
    }

    /// <summary>
    ///     Tests that paste content is not wrapped when bracketed paste mode is disabled.
    /// </summary>
    [Test]
    public void WrapPasteContent_WithBracketedPasteModeDisabled_DoesNotWrapContent()
    {
        // Arrange
        var terminal = TerminalEmulator.Create(80, 24);
        // Bracketed paste mode is disabled by default

        // Act
        string result = terminal.WrapPasteContent("hello world");

        // Assert
        Assert.That(result, Is.EqualTo("hello world"));

        terminal.Dispose();
    }

    /// <summary>
    ///     Tests that empty paste content is handled correctly.
    /// </summary>
    [Test]
    public void WrapPasteContent_WithEmptyContent_HandlesCorrectly()
    {
        // Arrange
        var terminal = TerminalEmulator.Create(80, 24);
        terminal.Write("\x1b[?2004h"); // Enable bracketed paste mode

        // Act & Assert
        Assert.That(terminal.WrapPasteContent(""), Is.EqualTo(""));
        Assert.That(terminal.WrapPasteContent(null!), Is.EqualTo(null));

        terminal.Dispose();
    }

    /// <summary>
    ///     Tests that the ReadOnlySpan overload works correctly.
    /// </summary>
    [Test]
    public void WrapPasteContent_WithReadOnlySpan_WorksCorrectly()
    {
        // Arrange
        var terminal = TerminalEmulator.Create(80, 24);
        terminal.Write("\x1b[?2004h"); // Enable bracketed paste mode

        // Act
        ReadOnlySpan<char> content = "test content".AsSpan();
        string result = terminal.WrapPasteContent(content);

        // Assert
        Assert.That(result, Is.EqualTo("\x1b[200~test content\x1b[201~"));

        terminal.Dispose();
    }

    /// <summary>
    ///     Tests that the bracketed paste mode state check method works correctly.
    /// </summary>
    [Test]
    public void IsBracketedPasteModeEnabled_ReflectsCurrentState()
    {
        // Arrange
        var terminal = TerminalEmulator.Create(80, 24);

        // Initially should be false
        Assert.That(terminal.IsBracketedPasteModeEnabled(), Is.False);

        // Enable bracketed paste mode
        terminal.Write("\x1b[?2004h");
        Assert.That(terminal.IsBracketedPasteModeEnabled(), Is.True);

        // Disable bracketed paste mode
        terminal.Write("\x1b[?2004l");
        Assert.That(terminal.IsBracketedPasteModeEnabled(), Is.False);

        terminal.Dispose();
    }

    /// <summary>
    ///     Tests that DEC soft reset (CSI ! p) resets terminal modes without clearing screen content.
    /// </summary>
    [Test]
    public void Write_DecSoftReset_ResetsModeWithoutClearingScreen()
    {
        // Arrange
        var terminal = TerminalEmulator.Create(80, 24);
        
        // Write some content to the screen
        terminal.Write("Hello World");
        terminal.Write("\x1b[10;20H"); // Move cursor to (10, 20)
        
        // Set some non-default modes
        terminal.Write("\x1b[?1h");    // Application cursor keys on
        terminal.Write("\x1b[?7l");    // Auto-wrap off
        terminal.Write("\x1b[?25l");   // Cursor invisible
        terminal.Write("\x1b[1;31m");  // Bold red text
        
        // Verify initial state
        Assert.That(terminal.ScreenBuffer.GetCell(0, 0).Character, Is.EqualTo('H'), "Screen content should be present");
        Assert.That(terminal.Cursor.Row, Is.EqualTo(9), "Cursor should be at row 9 (0-based)");
        Assert.That(terminal.Cursor.Col, Is.EqualTo(19), "Cursor should be at col 19 (0-based)");
        Assert.That(terminal.ModeManager.ApplicationCursorKeys, Is.True, "Application cursor keys should be on");
        Assert.That(terminal.ModeManager.AutoWrapMode, Is.False, "Auto-wrap should be off");
        Assert.That(terminal.ModeManager.CursorVisible, Is.False, "Cursor should be invisible");
        Assert.That(terminal.AttributeManager.CurrentAttributes.Bold, Is.True, "Text should be bold");

        // Act: Perform soft reset
        terminal.Write("\x1b[!p");

        // Assert: Screen content should be preserved
        Assert.That(terminal.ScreenBuffer.GetCell(0, 0).Character, Is.EqualTo('H'), "Screen content should be preserved");
        
        // Assert: Cursor should be reset to home position (0,0)
        Assert.That(terminal.Cursor.Row, Is.EqualTo(0), "Cursor should be reset to row 0");
        Assert.That(terminal.Cursor.Col, Is.EqualTo(0), "Cursor should be reset to col 0");
        
        // Assert: Terminal modes should be reset to defaults
        Assert.That(terminal.ModeManager.ApplicationCursorKeys, Is.False, "Application cursor keys should be reset to off");
        Assert.That(terminal.ModeManager.AutoWrapMode, Is.True, "Auto-wrap should be reset to on");
        Assert.That(terminal.ModeManager.CursorVisible, Is.True, "Cursor should be reset to visible");
        
        // Assert: SGR attributes should be reset to defaults
        Assert.That(terminal.AttributeManager.CurrentAttributes.Bold, Is.False, "Bold should be reset");
        Assert.That(terminal.AttributeManager.CurrentAttributes.ForegroundColor, Is.Null, "Foreground color should be reset");
        Assert.That(terminal.AttributeManager.CurrentAttributes, Is.EqualTo(SgrAttributes.Default), "All attributes should be default");
        
        // Assert: Cursor style should be reset to default
        Assert.That(terminal.State.CursorStyle, Is.EqualTo(CursorStyle.BlinkingBlock), "Cursor style should be reset to default");
        
        // Assert: Saved cursor positions should be cleared
        Assert.That(terminal.State.SavedCursor, Is.Null, "DEC saved cursor should be cleared");
        Assert.That(terminal.State.AnsiSavedCursor, Is.Null, "ANSI saved cursor should be cleared");
        
        // Assert: Wrap pending should be cleared
        Assert.That(terminal.State.WrapPending, Is.False, "Wrap pending should be cleared");

        terminal.Dispose();
    }

    /// <summary>
    ///     Tests that soft reset resets scroll region to full screen.
    /// </summary>
    [Test]
    public void Write_DecSoftReset_ResetsScrollRegion()
    {
        // Arrange
        var terminal = TerminalEmulator.Create(80, 24);
        
        // Set a custom scroll region
        terminal.Write("\x1b[5;20r"); // Set scroll region from row 5 to 20
        
        // Verify scroll region is set
        Assert.That(terminal.State.ScrollTop, Is.EqualTo(4), "Scroll top should be set (0-based)");
        Assert.That(terminal.State.ScrollBottom, Is.EqualTo(19), "Scroll bottom should be set (0-based)");

        // Act: Perform soft reset
        terminal.Write("\x1b[!p");

        // Assert: Scroll region should be reset to full screen
        Assert.That(terminal.State.ScrollTop, Is.EqualTo(0), "Scroll top should be reset to 0");
        Assert.That(terminal.State.ScrollBottom, Is.EqualTo(23), "Scroll bottom should be reset to full screen");

        terminal.Dispose();
    }

    /// <summary>
    ///     Tests that soft reset resets character sets to defaults.
    /// </summary>
    [Test]
    public void Write_DecSoftReset_ResetsCharacterSets()
    {
        // Arrange
        var terminal = TerminalEmulator.Create(80, 24);
        
        // Set non-default character sets
        terminal.Write("\x1b(0"); // Set G0 to DEC Special Character Set
        terminal.Write("\x1b)A"); // Set G1 to UK character set
        
        // Verify character sets are changed
        Assert.That(terminal.State.CharacterSets.G0, Is.EqualTo("0"), "G0 should be DEC Special");
        Assert.That(terminal.State.CharacterSets.G1, Is.EqualTo("A"), "G1 should be UK");

        // Act: Perform soft reset
        terminal.Write("\x1b[!p");

        // Assert: Character sets should be reset to ASCII
        Assert.That(terminal.State.CharacterSets.G0, Is.EqualTo("B"), "G0 should be reset to ASCII");
        Assert.That(terminal.State.CharacterSets.G1, Is.EqualTo("B"), "G1 should be reset to ASCII");
        Assert.That(terminal.State.CharacterSets.G2, Is.EqualTo("B"), "G2 should be reset to ASCII");
        Assert.That(terminal.State.CharacterSets.G3, Is.EqualTo("B"), "G3 should be reset to ASCII");
        Assert.That(terminal.State.CharacterSets.Current, Is.EqualTo(CharacterSetKey.G0), "Current should be G0");

        terminal.Dispose();
    }

    /// <summary>
    ///     Tests that soft reset resets tab stops to defaults.
    /// </summary>
    [Test]
    public void Write_DecSoftReset_ResetsTabStops()
    {
        // Arrange
        var terminal = TerminalEmulator.Create(80, 24);
        
        // Clear all tab stops and set custom ones
        terminal.Write("\x1b[3g"); // Clear all tab stops
        terminal.Write("\x1b[10G\x1bH"); // Go to column 10 and set tab stop
        terminal.Write("\x1b[20G\x1bH"); // Go to column 20 and set tab stop
        
        // Verify custom tab stops
        Assert.That(terminal.State.TabStops[8], Is.False, "Default tab stop at 8 should be cleared");
        Assert.That(terminal.State.TabStops[9], Is.True, "Custom tab stop at 10 should be set (0-based)");
        Assert.That(terminal.State.TabStops[19], Is.True, "Custom tab stop at 20 should be set (0-based)");

        // Act: Perform soft reset
        terminal.Write("\x1b[!p");

        // Assert: Tab stops should be reset to defaults (every 8 columns)
        Assert.That(terminal.State.TabStops[8], Is.True, "Default tab stop at 8 should be restored");
        Assert.That(terminal.State.TabStops[16], Is.True, "Default tab stop at 16 should be restored");
        Assert.That(terminal.State.TabStops[24], Is.True, "Default tab stop at 24 should be restored");
        Assert.That(terminal.State.TabStops[9], Is.False, "Custom tab stop at 10 should be cleared");
        Assert.That(terminal.State.TabStops[19], Is.False, "Custom tab stop at 20 should be cleared");

        terminal.Dispose();
    }

    /// <summary>
    ///     Tests that soft reset resets character protection to unprotected.
    /// </summary>
    [Test]
    public void Write_DecSoftReset_ResetsCharacterProtection()
    {
        // Arrange
        var terminal = TerminalEmulator.Create(80, 24);
        
        // Set character protection
        terminal.Write("\x1b[2\"q"); // Set character protection to protected
        
        // Verify character protection is set
        Assert.That(terminal.AttributeManager.CurrentCharacterProtection, Is.True, "Character protection should be enabled");

        // Act: Perform soft reset
        terminal.Write("\x1b[!p");

        // Assert: Character protection should be reset to unprotected
        Assert.That(terminal.AttributeManager.CurrentCharacterProtection, Is.False, "Character protection should be reset to unprotected");

        terminal.Dispose();
    }

    /// <summary>
    ///     Tests that soft reset resets UTF-8 mode to enabled.
    /// </summary>
    [Test]
    public void Write_DecSoftReset_ResetsUtf8Mode()
    {
        // Arrange
        var terminal = TerminalEmulator.Create(80, 24);
        
        // Disable UTF-8 mode (this would normally be done through some sequence, but we'll set it directly for testing)
        terminal.State.Utf8Mode = false;
        
        // Verify UTF-8 mode is disabled
        Assert.That(terminal.State.Utf8Mode, Is.False, "UTF-8 mode should be disabled");

        // Act: Perform soft reset
        terminal.Write("\x1b[!p");

        // Assert: UTF-8 mode should be reset to enabled
        Assert.That(terminal.State.Utf8Mode, Is.True, "UTF-8 mode should be reset to enabled");

        terminal.Dispose();
    }

    /// <summary>
    ///     Tests that soft reset preserves scrollback buffer content.
    /// </summary>
    [Test]
    public void Write_DecSoftReset_PreservesScrollbackContent()
    {
        // Arrange
        var terminal = TerminalEmulator.Create(80, 3, 100); // Small terminal to force scrollback
        
        // Fill the screen and add more content to create scrollback
        for (int i = 0; i < 6; i++) // More lines than screen height
        {
            terminal.Write($"Scrollback Line {i}\n");
        }
        
        // Verify scrollback has content
        int scrollbackLines = terminal.ScrollbackManager.CurrentLines;
        Assert.That(scrollbackLines, Is.GreaterThan(0), "Scrollback should have content");

        // Act: Perform soft reset
        terminal.Write("\x1b[!p");

        // Assert: Scrollback content should be preserved
        Assert.That(terminal.ScrollbackManager.CurrentLines, Is.EqualTo(scrollbackLines), "Scrollback content should be preserved");

        terminal.Dispose();
    }

    /// <summary>
    ///     Tests that soft reset can be called multiple times without issues.
    /// </summary>
    [Test]
    public void Write_DecSoftReset_CanBeCalledMultipleTimes()
    {
        // Arrange
        var terminal = TerminalEmulator.Create(80, 24);
        
        // Write some content and set some modes
        terminal.Write("Test content");
        terminal.Write("\x1b[?1h"); // Application cursor keys on
        terminal.Write("\x1b[1m");  // Bold

        // Act: Perform multiple soft resets
        terminal.Write("\x1b[!p");
        terminal.Write("\x1b[!p");
        terminal.Write("\x1b[!p");

        // Assert: Should be in consistent reset state
        Assert.That(terminal.Cursor.Row, Is.EqualTo(0), "Cursor should be at home");
        Assert.That(terminal.Cursor.Col, Is.EqualTo(0), "Cursor should be at home");
        Assert.That(terminal.ModeManager.ApplicationCursorKeys, Is.False, "Application cursor keys should be off");
        Assert.That(terminal.AttributeManager.CurrentAttributes.Bold, Is.False, "Bold should be off");
        Assert.That(terminal.ScreenBuffer.GetCell(0, 0).Character, Is.EqualTo('T'), "Screen content should be preserved");

        terminal.Dispose();
    }

    /// <summary>
    ///     Tests that soft reset updates all manager states consistently.
    /// </summary>
    [Test]
    public void Write_DecSoftReset_UpdatesAllManagerStates()
    {
        // Arrange
        var terminal = TerminalEmulator.Create(80, 24);
        
        // Set various states
        terminal.Write("\x1b[10;20H"); // Move cursor
        terminal.Write("\x1b[?7l");    // Auto-wrap off
        terminal.Write("\x1b[?25l");   // Cursor invisible
        terminal.Write("\x1b[2 q");    // Steady block cursor
        terminal.Write("\x1b[1;31m");  // Bold red

        // Act: Perform soft reset
        terminal.Write("\x1b[!p");

        // Assert: All managers should be in sync with terminal state
        Assert.That(terminal.CursorManager.Row, Is.EqualTo(terminal.State.CursorY), "Cursor manager row should match state");
        Assert.That(terminal.CursorManager.Column, Is.EqualTo(terminal.State.CursorX), "Cursor manager column should match state");
        Assert.That(terminal.CursorManager.Visible, Is.EqualTo(terminal.State.CursorVisible), "Cursor manager visibility should match state");
        Assert.That(terminal.CursorManager.Style, Is.EqualTo(terminal.State.CursorStyle), "Cursor manager style should match state");
        Assert.That(terminal.ModeManager.AutoWrapMode, Is.EqualTo(terminal.State.AutoWrapMode), "Mode manager auto-wrap should match state");
        Assert.That(terminal.ModeManager.ApplicationCursorKeys, Is.EqualTo(terminal.State.ApplicationCursorKeys), "Mode manager app cursor keys should match state");
        Assert.That(terminal.ModeManager.CursorVisible, Is.EqualTo(terminal.State.CursorVisible), "Mode manager cursor visibility should match state");
        Assert.That(terminal.ModeManager.OriginMode, Is.EqualTo(terminal.State.OriginMode), "Mode manager origin mode should match state");
        Assert.That(terminal.AttributeManager.CurrentAttributes, Is.EqualTo(terminal.State.CurrentSgrState), "Attribute manager should match state");

        terminal.Dispose();
    }

    /// <summary>
    ///     Tests that insert mode can be enabled and disabled via CSI sequences.
    /// </summary>
    [Test]
    public void InsertMode_EnableAndDisable_UpdatesModeState()
    {
        // Arrange
        var terminal = TerminalEmulator.Create(80, 24);

        // Act - Enable insert mode (CSI 4 h)
        terminal.Write("\x1b[4h");

        // Assert - Insert mode should be enabled
        Assert.That(terminal.ModeManager.InsertMode, Is.True, "Insert mode should be enabled after CSI 4 h");

        // Act - Disable insert mode (CSI 4 l)
        terminal.Write("\x1b[4l");

        // Assert - Insert mode should be disabled
        Assert.That(terminal.ModeManager.InsertMode, Is.False, "Insert mode should be disabled after CSI 4 l");

        terminal.Dispose();
    }

    /// <summary>
    ///     Tests that characters are inserted (not overwritten) when insert mode is enabled.
    /// </summary>
    [Test]
    public void InsertMode_Enabled_InsertsCharactersInsteadOfOverwriting()
    {
        // Arrange
        var terminal = TerminalEmulator.Create(80, 24);
        
        // Write initial text
        terminal.Write("ABCDEF");
        
        // Move cursor to position 2 (between B and C)
        terminal.Write("\x1b[1;3H"); // Row 1, Column 3 (1-indexed)
        
        // Enable insert mode
        terminal.Write("\x1b[4h");

        // Act - Write a character in insert mode
        terminal.Write("X");

        // Assert - Character should be inserted, shifting existing characters right
        Assert.That(terminal.ScreenBuffer.GetCell(0, 0).Character, Is.EqualTo('A'), "First character should remain");
        Assert.That(terminal.ScreenBuffer.GetCell(0, 1).Character, Is.EqualTo('B'), "Second character should remain");
        Assert.That(terminal.ScreenBuffer.GetCell(0, 2).Character, Is.EqualTo('X'), "Inserted character should be at cursor position");
        Assert.That(terminal.ScreenBuffer.GetCell(0, 3).Character, Is.EqualTo('C'), "Third character should be shifted right");
        Assert.That(terminal.ScreenBuffer.GetCell(0, 4).Character, Is.EqualTo('D'), "Fourth character should be shifted right");
        Assert.That(terminal.ScreenBuffer.GetCell(0, 5).Character, Is.EqualTo('E'), "Fifth character should be shifted right");
        Assert.That(terminal.ScreenBuffer.GetCell(0, 6).Character, Is.EqualTo('F'), "Sixth character should be shifted right");

        terminal.Dispose();
    }

    /// <summary>
    ///     Tests that characters are overwritten (default behavior) when insert mode is disabled.
    /// </summary>
    [Test]
    public void InsertMode_Disabled_OverwritesCharacters()
    {
        // Arrange
        var terminal = TerminalEmulator.Create(80, 24);
        
        // Write initial text
        terminal.Write("ABCDEF");
        
        // Move cursor to position 2 (at character C)
        terminal.Write("\x1b[1;3H"); // Row 1, Column 3 (1-indexed)
        
        // Ensure insert mode is disabled (default state)
        terminal.Write("\x1b[4l");

        // Act - Write a character in replace mode (default)
        terminal.Write("X");

        // Assert - Character should overwrite existing character
        Assert.That(terminal.ScreenBuffer.GetCell(0, 0).Character, Is.EqualTo('A'), "First character should remain");
        Assert.That(terminal.ScreenBuffer.GetCell(0, 1).Character, Is.EqualTo('B'), "Second character should remain");
        Assert.That(terminal.ScreenBuffer.GetCell(0, 2).Character, Is.EqualTo('X'), "Character should overwrite at cursor position");
        Assert.That(terminal.ScreenBuffer.GetCell(0, 3).Character, Is.EqualTo('D'), "Fourth character should remain (not shifted)");
        Assert.That(terminal.ScreenBuffer.GetCell(0, 4).Character, Is.EqualTo('E'), "Fifth character should remain (not shifted)");
        Assert.That(terminal.ScreenBuffer.GetCell(0, 5).Character, Is.EqualTo('F'), "Sixth character should remain (not shifted)");

        terminal.Dispose();
    }

    /// <summary>
    ///     Tests that insert mode handles line overflow correctly by truncating characters at the right edge.
    /// </summary>
    [Test]
    public void InsertMode_LineOverflow_TruncatesAtRightEdge()
    {
        // Arrange
        var terminal = TerminalEmulator.Create(10, 24); // Small width for testing overflow
        
        // Fill the line completely
        terminal.Write("0123456789");
        
        // Move cursor to position 5
        terminal.Write("\x1b[1;6H"); // Row 1, Column 6 (1-indexed)
        
        // Enable insert mode
        terminal.Write("\x1b[4h");

        // Act - Insert a character when line is full
        terminal.Write("X");

        // Assert - Character should be inserted, rightmost character should be lost
        Assert.That(terminal.ScreenBuffer.GetCell(0, 0).Character, Is.EqualTo('0'), "First character should remain");
        Assert.That(terminal.ScreenBuffer.GetCell(0, 4).Character, Is.EqualTo('4'), "Character before insertion point should remain");
        Assert.That(terminal.ScreenBuffer.GetCell(0, 5).Character, Is.EqualTo('X'), "Inserted character should be at cursor position");
        Assert.That(terminal.ScreenBuffer.GetCell(0, 6).Character, Is.EqualTo('5'), "Characters should be shifted right");
        Assert.That(terminal.ScreenBuffer.GetCell(0, 7).Character, Is.EqualTo('6'), "Characters should be shifted right");
        Assert.That(terminal.ScreenBuffer.GetCell(0, 8).Character, Is.EqualTo('7'), "Characters should be shifted right");
        Assert.That(terminal.ScreenBuffer.GetCell(0, 9).Character, Is.EqualTo('8'), "Last visible character should be shifted");
        // Character '9' should be lost due to overflow

        terminal.Dispose();
    }

    /// <summary>
    ///     Tests that insert mode preserves character attributes when shifting characters.
    /// </summary>
    [Test]
    public void InsertMode_PreservesCharacterAttributes_WhenShifting()
    {
        // Arrange
        var terminal = TerminalEmulator.Create(80, 24);
        
        // Write text with bold attribute
        terminal.Write("\x1b[1mABC\x1b[0m"); // Bold ABC, then reset
        terminal.Write("DEF"); // Normal DEF
        
        // Move cursor to position 2 (between B and C)
        terminal.Write("\x1b[1;3H"); // Row 1, Column 3 (1-indexed)
        
        // Enable insert mode
        terminal.Write("\x1b[4h");

        // Act - Insert a character with italic attribute
        terminal.Write("\x1b[3mX\x1b[0m"); // Italic X, then reset

        // Assert - Attributes should be preserved when characters are shifted
        Assert.That(terminal.ScreenBuffer.GetCell(0, 0).Attributes.Bold, Is.True, "First character should remain bold");
        Assert.That(terminal.ScreenBuffer.GetCell(0, 1).Attributes.Bold, Is.True, "Second character should remain bold");
        Assert.That(terminal.ScreenBuffer.GetCell(0, 2).Attributes.Italic, Is.True, "Inserted character should be italic");
        Assert.That(terminal.ScreenBuffer.GetCell(0, 3).Attributes.Bold, Is.True, "Shifted character should preserve bold attribute");
        Assert.That(terminal.ScreenBuffer.GetCell(0, 4).Attributes.Bold, Is.False, "Character after bold section should not be bold");

        terminal.Dispose();
    }

    /// <summary>
    ///     Tests that insert mode at the right edge of the screen behaves correctly.
    /// </summary>
    [Test]
    public void InsertMode_AtRightEdge_HandlesCorrectly()
    {
        // Arrange
        var terminal = TerminalEmulator.Create(5, 24); // Very small width
        
        // Fill the line
        terminal.Write("ABCDE");
        
        // Move cursor to the last position
        terminal.Write("\x1b[1;5H"); // Row 1, Column 5 (1-indexed)
        
        // Enable insert mode
        terminal.Write("\x1b[4h");

        // Act - Try to insert at the right edge
        terminal.Write("X");

        // Assert - Should handle edge case gracefully
        Assert.That(terminal.ScreenBuffer.GetCell(0, 4).Character, Is.EqualTo('X'), "Character should be inserted at right edge");

        terminal.Dispose();
    }
}
