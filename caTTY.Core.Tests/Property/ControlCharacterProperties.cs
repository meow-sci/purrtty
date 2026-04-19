using caTTY.Core.Terminal;
using caTTY.Core.Types;
using FsCheck;
using NUnit.Framework;

namespace caTTY.Core.Tests.Property;

/// <summary>
///     Property-based tests for control character handling in the terminal emulator.
///     These tests verify universal properties that should hold for all valid inputs.
/// </summary>
[TestFixture]
[Category("Property")]
public class ControlCharacterProperties
{
    /// <summary>
    ///     Generator for valid control characters that the terminal should handle.
    /// </summary>
    public static Arbitrary<byte> ValidControlCharacterArb =>
        Arb.From(Gen.Elements(new byte[] { 0x07, 0x08, 0x09, 0x0A, 0x0D })); // BEL, BS, HT, LF, CR

    /// <summary>
    ///     Generator for printable ASCII characters.
    /// </summary>
    public static Arbitrary<byte> PrintableCharacterArb =>
        Arb.From(Gen.Choose(0x20, 0x7E).Select(i => (byte)i));

    /// <summary>
    ///     Generator for mixed sequences of control characters and printable text.
    /// </summary>
    public static Arbitrary<byte[]> MixedSequenceArb =>
        Arb.From(Gen.ArrayOf(Gen.OneOf(ValidControlCharacterArb.Generator, PrintableCharacterArb.Generator))
            .Where(arr => arr.Length > 0 && arr.Length <= 50));

    /// <summary>
    ///     **Feature: catty-ksa, Property 18: Control character processing**
    ///     **Validates: Requirements 10.1, 10.2, 10.3, 10.4, 10.5**
    ///     Property: For any sequence containing control characters, the terminal should
    ///     process them without crashing and maintain cursor position integrity.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property ControlCharacterProcessingMaintainsCursorIntegrity()
    {
        return Prop.ForAll(MixedSequenceArb, sequence =>
        {
            // Arrange
            var terminal = TerminalEmulator.Create(80, 24);

            // Act
            terminal.Write(sequence);

            // Assert - Cursor should always be within bounds
            int finalCursorRow = terminal.Cursor.Row;
            int finalCursorCol = terminal.Cursor.Col;

            return finalCursorRow >= 0 && finalCursorRow < terminal.Height &&
                   finalCursorCol >= 0 && finalCursorCol < terminal.Width;
        });
    }

    /// <summary>
    ///     **Feature: catty-ksa, Property 18b: Bell event consistency**
    ///     **Validates: Requirements 10.5**
    ///     Property: For any sequence containing bell characters, the number of bell events
    ///     should equal the number of bell characters in the sequence.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property BellEventCountMatchesBellCharacterCount()
    {
        return Prop.ForAll(MixedSequenceArb, sequence =>
        {
            // Arrange
            var terminal = TerminalEmulator.Create(80, 24);
            int bellEventCount = 0;
            terminal.Bell += (sender, args) => bellEventCount++;

            // Count expected bell characters
            int expectedBellCount = sequence.Count(b => b == 0x07);

            // Act
            terminal.Write(sequence);

            // Assert
            return bellEventCount == expectedBellCount;
        });
    }

    /// <summary>
    ///     **Feature: catty-ksa, Property 18c: Backspace cursor movement**
    ///     **Validates: Requirements 10.3**
    ///     Property: For any sequence of characters followed by backspaces, the cursor
    ///     should never move to negative coordinates.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property BackspaceNeverMovesToNegativeCoordinates()
    {
        Arbitrary<(int TextLength, int BackspaceCount)>? testCaseArb = Arb.From(
            Gen.Choose(1, 20).SelectMany(textLength =>
                Gen.Choose(0, textLength + 5).SelectMany(backspaceCount =>
                    Gen.Constant((TextLength: textLength, BackspaceCount: backspaceCount)))));

        return Prop.ForAll(testCaseArb, testCase =>
        {
            // Arrange
            var terminal = TerminalEmulator.Create(80, 24);

            // Write some text
            string text = new('A', testCase.TextLength);
            terminal.Write(text);

            // Write backspaces
            byte[] backspaces = new byte[testCase.BackspaceCount];
            Array.Fill(backspaces, (byte)0x08);

            // Act
            terminal.Write(backspaces);

            // Assert - Cursor should never go negative
            int cursorRow = terminal.Cursor.Row;
            int cursorCol = terminal.Cursor.Col;
            return cursorRow >= 0 && cursorCol >= 0;
        });
    }

    /// <summary>
    ///     **Feature: catty-ksa, Property 18d: Tab stop consistency**
    ///     **Validates: Requirements 10.4, 19.1, 19.2**
    ///     Property: For any number of tab characters, the cursor should always land
    ///     on valid tab stop positions (multiples of 8) or at the right edge.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property TabCharactersLandOnValidTabStops()
    {
        return Prop.ForAll(Arb.From(Gen.Choose(1, 15)), tabCount =>
        {
            // Arrange
            var terminal = TerminalEmulator.Create(80, 24);

            // Act - Write multiple tabs
            byte[] tabs = new byte[tabCount];
            Array.Fill(tabs, (byte)0x09);
            terminal.Write(tabs);

            // Assert - Cursor should be at a valid tab stop or right edge
            int cursorCol = terminal.Cursor.Col;
            return cursorCol % 8 == 0 || cursorCol == terminal.Width - 1;
        });
    }

    /// <summary>
    ///     **Feature: catty-ksa, Property 18e: Control character state integrity**
    ///     **Validates: Requirements 10.1, 10.2, 10.3, 10.4, 10.5**
    ///     Property: For any sequence of control characters, the terminal state should
    ///     remain consistent and the terminal should be ready for subsequent operations.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property ControlCharactersMaintainStateIntegrity()
    {
        return Prop.ForAll(MixedSequenceArb, sequence =>
        {
            // Arrange
            var terminal = TerminalEmulator.Create(80, 24);

            // Act - Process the sequence
            terminal.Write(sequence);

            // Verify state integrity by performing a simple operation
            char testChar = 'X';
            (int Row, int Col) cursorBeforeTest = (terminal.Cursor.Row, terminal.Cursor.Col);

            terminal.Write(testChar.ToString());

            // Assert - The test character should be written correctly
            Cell cell = terminal.ScreenBuffer.GetCell(cursorBeforeTest.Row, cursorBeforeTest.Col);
            (int Row, int Col) cursorAfterTest = (terminal.Cursor.Row, terminal.Cursor.Col);

            // The character should be written and cursor should advance (unless at edge)
            bool characterWritten = cell.Character == testChar;
            bool cursorAdvanced = cursorAfterTest.Col > cursorBeforeTest.Col ||
                                  cursorAfterTest.Row > cursorBeforeTest.Row ||
                                  cursorBeforeTest.Col == terminal.Width - 1; // At right edge

            return characterWritten && cursorAdvanced;
        });
    }

    /*
    /// <summary>
    /// **Feature: catty-ksa, Property 18f: Carriage return and line feed consistency**
    /// **Validates: Requirements 10.1, 10.2**
    ///
    /// Property: For any sequence containing CR and LF characters, the cursor
    /// movement should be consistent with terminal line discipline.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property CarriageReturnLineFeedConsistency()
    {
        var operationsArb = Arb.From(
            Gen.ListOf(Gen.Elements(new[] { "text", "\r", "\n", "\r\n" }))
               .Where(list => list.Count > 0 && list.Count <= 10)
               .Select(list => list.ToArray()));

        return Prop.ForAll(operationsArb, (string[] operations) =>
        {
            // Arrange
            var terminal = TerminalEmulator.Create(80, 24);

            // Act
            foreach (var op in operations)
            {
                terminal.Write(op);
            }

            // Assert - Cursor should be within bounds and at valid position
            var cursor = terminal.Cursor;
            int cursorRow = cursor.Row;
            int cursorCol = cursor.Col;
            bool withinBounds = cursorRow >= 0 && cursorRow < terminal.Height &&
                               cursorCol >= 0 && cursorCol < terminal.Width;

            // After any CR, cursor should be at column 0
            // This is tested by writing a CR and checking position
            terminal.Write("\r");
            int finalCursorCol = terminal.Cursor.Col;
            bool crMovesToColumnZero = finalCursorCol == 0;

            return withinBounds && crMovesToColumnZero;
        });
    }
    */
}
