using caTTY.Core.Terminal;
using caTTY.Core.Types;
using FsCheck;
using NUnit.Framework;

namespace caTTY.Core.Tests.Property;

/// <summary>
///     Property-based tests for cursor movement sequences in the terminal emulator.
///     These tests verify universal properties that should hold for all cursor movement operations.
///     Validates Requirements 8.4, 11.1, 11.2, 11.3, 11.4, 11.5.
/// </summary>
[TestFixture]
[Category("Property")]
public class CursorMovementProperties
{
    /// <summary>
    ///     Generator for valid cursor movement sequences.
    /// </summary>
    public static Arbitrary<string> CursorMovementSequenceArb =>
        Arb.From(Gen.Elements("\x1b[A", "\x1b[B", "\x1b[C", "\x1b[D", "\x1b[2A", "\x1b[3B", "\x1b[5C", "\x1b[4D",
            "\x1b[10;20H", "\x1b[1;1H", "\x1b[H"));

    /// <summary>
    ///     **Feature: catty-ksa, Property 13: Cursor movement sequences**
    ///     **Validates: Requirements 8.4, 11.1, 11.2, 11.3, 11.4, 11.5**
    ///     Property: For any cursor movement sequence, the cursor should never move outside the terminal boundaries.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property CursorMovementMaintainsBounds()
    {
        return Prop.ForAll(CursorMovementSequenceArb, sequence =>
        {
            // Arrange
            var terminal = TerminalEmulator.Create(80, 24);

            // Start from a position within bounds
            terminal.Write("\x1b[12;40H"); // Middle of screen

            // Act: Apply cursor movement sequence
            terminal.Write(sequence);

            // Assert: Cursor should remain within bounds
            ICursor cursor = terminal.Cursor;
            bool withinBounds = cursor.Row >= 0 && cursor.Row < 24 &&
                                cursor.Col >= 0 && cursor.Col < 80;

            // Additional verification: terminal should still be functional
            terminal.Write("X");
            bool terminalFunctional = true; // If we get here without exception, it's functional

            return withinBounds && terminalFunctional;
        });
    }

    /// <summary>
    ///     Property: Cursor movement sequences are deterministic.
    ///     Applying the same cursor movement sequence should always produce the same result.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property CursorMovementIsDeterministic()
    {
        return Prop.ForAll(CursorMovementSequenceArb, sequence =>
        {
            // Arrange: Create two identical terminals
            var terminal1 = TerminalEmulator.Create(80, 24);
            var terminal2 = TerminalEmulator.Create(80, 24);

            // Start both from the same position
            terminal1.Write("\x1b[10;15H");
            terminal2.Write("\x1b[10;15H");

            // Act: Apply the same sequence to both
            terminal1.Write(sequence);
            terminal2.Write(sequence);

            // Assert: Both cursors should be at the same position
            ICursor cursor1 = terminal1.Cursor;
            ICursor cursor2 = terminal2.Cursor;

            return cursor1.Row == cursor2.Row && cursor1.Col == cursor2.Col;
        });
    }

    /// <summary>
    ///     Property: Cursor movement preserves terminal state integrity.
    ///     After any cursor movement, the terminal should remain in a valid state.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property CursorMovementPreservesStateIntegrity()
    {
        return Prop.ForAll(CursorMovementSequenceArb, sequence =>
        {
            // Arrange
            var terminal = TerminalEmulator.Create(80, 24);

            // Write some content first
            terminal.Write("Hello World");

            // Act: Apply cursor movement
            terminal.Write(sequence);

            // Assert: Terminal should still be functional
            ICursor cursor = terminal.Cursor;

            // Verify cursor is valid
            bool cursorValid = cursor.Row >= 0 && cursor.Row < 24 &&
                               cursor.Col >= 0 && cursor.Col < 80;

            // Verify we can still write content
            terminal.Write("TEST");
            ICursor finalCursor = terminal.Cursor;
            bool canWrite = finalCursor.Row >= 0 && finalCursor.Row < 24 &&
                            finalCursor.Col >= 0 && finalCursor.Col < 80;

            return cursorValid && canWrite;
        });
    }

    /// <summary>
    ///     **Feature: catty-ksa, Property 12: Cursor wrapping behavior**
    ///     **Validates: Requirements 8.3**
    ///     Property: For any terminal with auto-wrap enabled, writing characters at the right edge should wrap to the next line.
    ///     For any terminal with auto-wrap disabled, writing characters at the right edge should keep cursor at the edge.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property CursorWrappingBehavior()
    {
        return Prop.ForAll(
            Arb.From(Gen.Choose(10, 120)), // Terminal width
            Arb.From(Gen.Choose(5, 50)),   // Terminal height
            Arb.From(Gen.Choose(1, 10)),   // Number of characters to write
            (width, height, charCount) =>
            {
                // Test with auto-wrap enabled
                var terminalWithWrap = TerminalEmulator.Create(width, height);

                // Enable auto-wrap mode (should be default, but make it explicit)
                terminalWithWrap.Write("\x1b[?7h");

                // Position cursor at right edge
                terminalWithWrap.Write($"\x1b[1;{width}H");

                // Write characters that should trigger wrapping
                string testChars = new string('X', charCount);
                terminalWithWrap.Write(testChars);

                ICursor cursorWithWrap = terminalWithWrap.Cursor;

                // Test with auto-wrap disabled
                var terminalWithoutWrap = TerminalEmulator.Create(width, height);

                // Disable auto-wrap mode
                terminalWithoutWrap.Write("\x1b[?7l");

                // Position cursor at right edge
                terminalWithoutWrap.Write($"\x1b[1;{width}H");

                // Write the same characters
                terminalWithoutWrap.Write(testChars);

                ICursor cursorWithoutWrap = terminalWithoutWrap.Cursor;

                // Verify wrapping behavior
                bool wrapBehaviorCorrect;
                if (charCount > 1)
                {
                    // With auto-wrap: cursor should have moved to next line(s)
                    // Without auto-wrap: cursor should stay at right edge
                    wrapBehaviorCorrect = cursorWithWrap.Row > 0 && cursorWithoutWrap.Row == 0 &&
                                         cursorWithoutWrap.Col == width - 1;
                }
                else
                {
                    // Single character at edge: both should behave the same initially
                    // The difference appears when writing additional characters
                    wrapBehaviorCorrect = true;
                }

                // Verify cursors are within bounds
                bool cursorsInBounds = cursorWithWrap.Row >= 0 && cursorWithWrap.Row < height &&
                                      cursorWithWrap.Col >= 0 && cursorWithWrap.Col < width &&
                                      cursorWithoutWrap.Row >= 0 && cursorWithoutWrap.Row < height &&
                                      cursorWithoutWrap.Col >= 0 && cursorWithoutWrap.Col < width;

                terminalWithWrap.Dispose();
                terminalWithoutWrap.Dispose();

                return wrapBehaviorCorrect && cursorsInBounds;
            });
    }
}
