using NUnit.Framework;
using caTTY.Core.Terminal;
using caTTY.Core.Types;

namespace caTTY.Core.Tests.Integration;

/// <summary>
///     Integration tests for line insertion and deletion operations in the terminal emulator.
///     Tests the complete flow from CSI L/M sequences to screen buffer modifications.
/// </summary>
[TestFixture]
public class LineOperationIntegrationTests
{
    private TerminalEmulator _terminal = null!;

    [SetUp]
    public void SetUp()
    {
        _terminal = TerminalEmulator.Create(10, 5); // Small terminal for easier testing
    }

    [TearDown]
    public void TearDown()
    {
        _terminal?.Dispose();
    }

    [Test]
    public void InsertLines_ShouldInsertBlankLinesAtCursor()
    {
        // Arrange: Fill terminal with content
        _terminal.Write("Line1\r\nLine2\r\nLine3\r\nLine4\r\nLine5");
        
        // Move cursor to row 1 (second line)
        _terminal.Write("\x1b[2;1H");
        
        // Act: Insert 2 lines (CSI 2 L)
        _terminal.Write("\x1b[2L");
        
        // Assert: Lines should be shifted down and blank lines inserted
        Assert.That(GetRowText(0), Is.EqualTo("Line1     "), "First line should remain unchanged");
        Assert.That(GetRowText(1), Is.EqualTo("          "), "Second line should be blank (inserted)");
        Assert.That(GetRowText(2), Is.EqualTo("          "), "Third line should be blank (inserted)");
        Assert.That(GetRowText(3), Is.EqualTo("Line2     "), "Fourth line should be shifted Line2");
        Assert.That(GetRowText(4), Is.EqualTo("Line3     "), "Fifth line should be shifted Line3");
    }

    [Test]
    public void DeleteLines_ShouldDeleteLinesAtCursor()
    {
        // Arrange: Fill terminal with content
        _terminal.Write("Line1\r\nLine2\r\nLine3\r\nLine4\r\nLine5");
        
        // Move cursor to row 1 (second line)
        _terminal.Write("\x1b[2;1H");
        
        // Act: Delete 2 lines (CSI 2 M)
        _terminal.Write("\x1b[2M");
        
        // Assert: Lines should be shifted up and blank lines added at bottom
        Assert.That(GetRowText(0), Is.EqualTo("Line1     "), "First line should remain unchanged");
        Assert.That(GetRowText(1), Is.EqualTo("Line4     "), "Second line should be shifted Line4");
        Assert.That(GetRowText(2), Is.EqualTo("Line5     "), "Third line should be shifted Line5");
        Assert.That(GetRowText(3), Is.EqualTo("          "), "Fourth line should be blank");
        Assert.That(GetRowText(4), Is.EqualTo("          "), "Fifth line should be blank");
    }

    [Test]
    public void InsertLines_WithScrollRegion_ShouldOnlyAffectRegion()
    {
        // Arrange: Fill terminal with content
        _terminal.Write("Line1\r\nLine2\r\nLine3\r\nLine4\r\nLine5");
        
        // Set scroll region to rows 2-4 (1-indexed: CSI 2;4 r)
        _terminal.Write("\x1b[2;4r");
        
        // Move cursor to row 1 (second line, within scroll region)
        _terminal.Write("\x1b[2;1H");
        
        // Act: Insert 1 line (CSI L)
        _terminal.Write("\x1b[L");
        
        // Assert: Only scroll region should be affected
        Assert.That(GetRowText(0), Is.EqualTo("Line1     "), "Line above region should remain unchanged");
        Assert.That(GetRowText(1), Is.EqualTo("          "), "Second line should be blank (inserted)");
        Assert.That(GetRowText(2), Is.EqualTo("Line2     "), "Third line should be shifted Line2");
        Assert.That(GetRowText(3), Is.EqualTo("Line3     "), "Fourth line should be shifted Line3");
        Assert.That(GetRowText(4), Is.EqualTo("Line5     "), "Line below region should remain unchanged");
    }

    [Test]
    public void DeleteLines_WithScrollRegion_ShouldOnlyAffectRegion()
    {
        // Arrange: Fill terminal with content
        _terminal.Write("Line1\r\nLine2\r\nLine3\r\nLine4\r\nLine5");
        
        // Set scroll region to rows 2-4 (1-indexed: CSI 2;4 r)
        _terminal.Write("\x1b[2;4r");
        
        // Move cursor to row 1 (second line, within scroll region)
        _terminal.Write("\x1b[2;1H");
        
        // Act: Delete 1 line (CSI M)
        _terminal.Write("\x1b[M");
        
        // Assert: Only scroll region should be affected
        Assert.That(GetRowText(0), Is.EqualTo("Line1     "), "Line above region should remain unchanged");
        Assert.That(GetRowText(1), Is.EqualTo("Line3     "), "Second line should be shifted Line3");
        Assert.That(GetRowText(2), Is.EqualTo("Line4     "), "Third line should be shifted Line4");
        Assert.That(GetRowText(3), Is.EqualTo("          "), "Fourth line should be blank");
        Assert.That(GetRowText(4), Is.EqualTo("Line5     "), "Line below region should remain unchanged");
    }

    [Test]
    public void InsertLines_OutsideScrollRegion_ShouldHaveNoEffect()
    {
        // Arrange: Fill terminal with content
        _terminal.Write("Line1\r\nLine2\r\nLine3\r\nLine4\r\nLine5");
        
        // Set scroll region to rows 2-4 (1-indexed: CSI 2;4 r)
        _terminal.Write("\x1b[2;4r");
        
        // Move cursor to row 0 (first line, outside scroll region)
        _terminal.Write("\x1b[1;1H");
        
        // Act: Insert 1 line (CSI L)
        _terminal.Write("\x1b[L");
        
        // Assert: Nothing should change
        Assert.That(GetRowText(0), Is.EqualTo("Line1     "), "First line should remain unchanged");
        Assert.That(GetRowText(1), Is.EqualTo("Line2     "), "Second line should remain unchanged");
        Assert.That(GetRowText(2), Is.EqualTo("Line3     "), "Third line should remain unchanged");
        Assert.That(GetRowText(3), Is.EqualTo("Line4     "), "Fourth line should remain unchanged");
        Assert.That(GetRowText(4), Is.EqualTo("Line5     "), "Fifth line should remain unchanged");
    }

    [Test]
    public void DeleteLines_OutsideScrollRegion_ShouldHaveNoEffect()
    {
        // Arrange: Fill terminal with content
        _terminal.Write("Line1\r\nLine2\r\nLine3\r\nLine4\r\nLine5");
        
        // Set scroll region to rows 2-4 (1-indexed: CSI 2;4 r)
        _terminal.Write("\x1b[2;4r");
        
        // Move cursor to row 4 (fifth line, outside scroll region)
        _terminal.Write("\x1b[5;1H");
        
        // Act: Delete 1 line (CSI M)
        _terminal.Write("\x1b[M");
        
        // Assert: Nothing should change
        Assert.That(GetRowText(0), Is.EqualTo("Line1     "), "First line should remain unchanged");
        Assert.That(GetRowText(1), Is.EqualTo("Line2     "), "Second line should remain unchanged");
        Assert.That(GetRowText(2), Is.EqualTo("Line3     "), "Third line should remain unchanged");
        Assert.That(GetRowText(3), Is.EqualTo("Line4     "), "Fourth line should remain unchanged");
        Assert.That(GetRowText(4), Is.EqualTo("Line5     "), "Fifth line should remain unchanged");
    }

    [Test]
    public void InsertLines_WithSgrAttributes_ShouldUseCurrentAttributes()
    {
        // Arrange: Set SGR attributes (bold red)
        _terminal.Write("\x1b[1;31m");
        
        // Move cursor to row 0
        _terminal.Write("\x1b[1;1H");
        
        // Act: Insert 1 line (CSI L)
        _terminal.Write("\x1b[L");
        
        // Assert: Inserted line should have current SGR attributes
        var cell = _terminal.ScreenBuffer.GetCell(0, 0);
        Assert.That(cell.Character, Is.EqualTo(' '), "Character should be space");
        Assert.That(cell.Attributes.Bold, Is.True, "Cell should be bold");
        Assert.That(cell.Attributes.ForegroundColor, Is.Not.Null, "Cell should have foreground color");
        Assert.That(cell.Attributes.ForegroundColor!.Value.NamedColor, Is.EqualTo(NamedColor.Red), "Cell should be red");
    }

    [Test]
    public void DeleteLines_WithSgrAttributes_ShouldUseCurrentAttributes()
    {
        // Arrange: Fill terminal with content
        _terminal.Write("Line1\r\nLine2\r\nLine3\r\nLine4\r\nLine5");
        
        // Set SGR attributes (bold blue)
        _terminal.Write("\x1b[1;34m");
        
        // Move cursor to row 0
        _terminal.Write("\x1b[1;1H");
        
        // Act: Delete 1 line (CSI M)
        _terminal.Write("\x1b[M");
        
        // Assert: New blank line at bottom should have current SGR attributes
        var cell = _terminal.ScreenBuffer.GetCell(4, 0);
        Assert.That(cell.Character, Is.EqualTo(' '), "Character should be space");
        Assert.That(cell.Attributes.Bold, Is.True, "Cell should be bold");
        Assert.That(cell.Attributes.ForegroundColor, Is.Not.Null, "Cell should have foreground color");
        Assert.That(cell.Attributes.ForegroundColor!.Value.NamedColor, Is.EqualTo(NamedColor.Blue), "Cell should be blue");
    }

    [Test]
    public void InsertLines_WithCharacterProtection_ShouldUseCurrentProtection()
    {
        // Arrange: Set character protection
        _terminal.Write("\x1b[2\"q"); // DECSCA - set character protection
        
        // Move cursor to row 0
        _terminal.Write("\x1b[1;1H");
        
        // Act: Insert 1 line (CSI L)
        _terminal.Write("\x1b[L");
        
        // Assert: Inserted line should have current character protection
        var cell = _terminal.ScreenBuffer.GetCell(0, 0);
        Assert.That(cell.Character, Is.EqualTo(' '), "Character should be space");
        Assert.That(cell.IsProtected, Is.True, "Cell should be protected");
    }

    [Test]
    public void DeleteLines_WithCharacterProtection_ShouldUseCurrentProtection()
    {
        // Arrange: Fill terminal with content
        _terminal.Write("Line1\r\nLine2\r\nLine3\r\nLine4\r\nLine5");
        
        // Set character protection
        _terminal.Write("\x1b[2\"q"); // DECSCA - set character protection
        
        // Move cursor to row 0
        _terminal.Write("\x1b[1;1H");
        
        // Act: Delete 1 line (CSI M)
        _terminal.Write("\x1b[M");
        
        // Assert: New blank line at bottom should have current character protection
        var cell = _terminal.ScreenBuffer.GetCell(4, 0);
        Assert.That(cell.Character, Is.EqualTo(' '), "Character should be space");
        Assert.That(cell.IsProtected, Is.True, "Cell should be protected");
    }

    [Test]
    public void InsertLines_DefaultParameter_ShouldInsertOneLine()
    {
        // Arrange: Fill terminal with content
        _terminal.Write("Line1\r\nLine2\r\nLine3\r\nLine4\r\nLine5");
        
        // Move cursor to row 1
        _terminal.Write("\x1b[2;1H");
        
        // Act: Insert lines without parameter (CSI L)
        _terminal.Write("\x1b[L");
        
        // Assert: Should insert 1 line by default
        Assert.That(GetRowText(0), Is.EqualTo("Line1     "), "First line should remain unchanged");
        Assert.That(GetRowText(1), Is.EqualTo("          "), "Second line should be blank (inserted)");
        Assert.That(GetRowText(2), Is.EqualTo("Line2     "), "Third line should be shifted Line2");
        Assert.That(GetRowText(3), Is.EqualTo("Line3     "), "Fourth line should be shifted Line3");
        Assert.That(GetRowText(4), Is.EqualTo("Line4     "), "Fifth line should be shifted Line4");
    }

    [Test]
    public void DeleteLines_DefaultParameter_ShouldDeleteOneLine()
    {
        // Arrange: Fill terminal with content
        _terminal.Write("Line1\r\nLine2\r\nLine3\r\nLine4\r\nLine5");
        
        // Move cursor to row 1
        _terminal.Write("\x1b[2;1H");
        
        // Act: Delete lines without parameter (CSI M)
        _terminal.Write("\x1b[M");
        
        // Assert: Should delete 1 line by default
        Assert.That(GetRowText(0), Is.EqualTo("Line1     "), "First line should remain unchanged");
        Assert.That(GetRowText(1), Is.EqualTo("Line3     "), "Second line should be shifted Line3");
        Assert.That(GetRowText(2), Is.EqualTo("Line4     "), "Third line should be shifted Line4");
        Assert.That(GetRowText(3), Is.EqualTo("Line5     "), "Fourth line should be shifted Line5");
        Assert.That(GetRowText(4), Is.EqualTo("          "), "Fifth line should be blank");
    }

    /// <summary>
    ///     Helper method to get the text content of a row as a string.
    /// </summary>
    /// <param name="row">Row index (0-based)</param>
    /// <returns>String representation of the row</returns>
    private string GetRowText(int row)
    {
        var rowSpan = _terminal.ScreenBuffer.GetRow(row);
        var chars = new char[rowSpan.Length];
        for (int i = 0; i < rowSpan.Length; i++)
        {
            chars[i] = rowSpan[i].Character;
        }
        return new string(chars);
    }

    #region Character Operation Tests

    [Test]
    public void InsertCharacters_ShouldInsertBlankCharactersAtCursor()
    {
        // Arrange: Write some text
        _terminal.Write("ABCDEFGHIJ");
        
        // Move cursor to column 3 (after 'C')
        _terminal.Write("\x1b[1;4H");
        
        // Act: Insert 3 characters (CSI 3 @)
        _terminal.Write("\x1b[3@");
        
        // Assert: Characters should be shifted right and blanks inserted
        Assert.That(GetRowText(0), Is.EqualTo("ABC   DEFG"), "Characters should be shifted right with blanks inserted");
    }

    [Test]
    public void DeleteCharacters_ShouldDeleteCharactersAtCursor()
    {
        // Arrange: Write some text
        _terminal.Write("ABCDEFGHIJ");
        
        // Move cursor to column 3 (at 'D')
        _terminal.Write("\x1b[1;4H");
        
        // Act: Delete 3 characters (CSI 3 P)
        _terminal.Write("\x1b[3P");
        
        // Assert: Characters should be shifted left and blanks added at end
        Assert.That(GetRowText(0), Is.EqualTo("ABCGHIJ   "), "Characters should be shifted left with blanks at end");
    }

    [Test]
    public void InsertCharacters_AtLastColumn_ShouldInsertAndShift()
    {
        // Arrange: Write some text
        _terminal.Write("ABCDEFGHIJ");
        
        // Move cursor to last column (column 10, which gets clamped to column 9)
        _terminal.Write("\x1b[1;10H");
        
        // Verify cursor is at last valid column
        Assert.That(_terminal.Cursor.Col, Is.EqualTo(9), "Cursor should be at last valid column");
        
        // Act: Insert 1 character (CSI @)
        _terminal.Write("\x1b[@");
        
        // Assert: Character should be inserted at cursor position, shifting the last character off
        Assert.That(GetRowText(0), Is.EqualTo("ABCDEFGHI "), "Last character should be shifted off and blank inserted");
    }

    [Test]
    public void DeleteCharacters_AtLineEnd_ShouldDeleteFromCursor()
    {
        // Arrange: Write some text
        _terminal.Write("ABCDEFGHIJ");
        
        // Move cursor to column 8 (at 'I')
        _terminal.Write("\x1b[1;9H");
        
        // Act: Delete 5 characters (more than remaining)
        _terminal.Write("\x1b[5P");
        
        // Assert: Should delete remaining characters and add blanks
        Assert.That(GetRowText(0), Is.EqualTo("ABCDEFGH  "), "Should delete remaining characters and add blanks");
    }

    [Test]
    public void InsertCharacters_WithSgrAttributes_ShouldUseCurrentAttributes()
    {
        // Arrange: Write some text
        _terminal.Write("ABCD");
        
        // Set SGR attributes (bold green)
        _terminal.Write("\x1b[1;32m");
        
        // Move cursor to column 2 (after 'B')
        _terminal.Write("\x1b[1;3H");
        
        // Act: Insert 2 characters (CSI 2 @)
        _terminal.Write("\x1b[2@");
        
        // Assert: Inserted characters should have current SGR attributes
        var cell1 = _terminal.ScreenBuffer.GetCell(0, 2);
        var cell2 = _terminal.ScreenBuffer.GetCell(0, 3);
        
        Assert.That(cell1.Character, Is.EqualTo(' '), "First inserted character should be space");
        Assert.That(cell1.Attributes.Bold, Is.True, "First inserted cell should be bold");
        Assert.That(cell1.Attributes.ForegroundColor!.Value.NamedColor, Is.EqualTo(NamedColor.Green), "First inserted cell should be green");
        
        Assert.That(cell2.Character, Is.EqualTo(' '), "Second inserted character should be space");
        Assert.That(cell2.Attributes.Bold, Is.True, "Second inserted cell should be bold");
        Assert.That(cell2.Attributes.ForegroundColor!.Value.NamedColor, Is.EqualTo(NamedColor.Green), "Second inserted cell should be green");
    }

    [Test]
    public void DeleteCharacters_WithSgrAttributes_ShouldUseCurrentAttributesForBlanks()
    {
        // Arrange: Write some text
        _terminal.Write("ABCDEFGHIJ");
        
        // Set SGR attributes (bold yellow)
        _terminal.Write("\x1b[1;33m");
        
        // Move cursor to column 3 (at 'D')
        _terminal.Write("\x1b[1;4H");
        
        // Act: Delete 3 characters (CSI 3 P)
        _terminal.Write("\x1b[3P");
        
        // Assert: New blank characters at end should have current SGR attributes
        var cell1 = _terminal.ScreenBuffer.GetCell(0, 8);
        var cell2 = _terminal.ScreenBuffer.GetCell(0, 9);
        
        Assert.That(cell1.Character, Is.EqualTo(' '), "First blank character should be space");
        Assert.That(cell1.Attributes.Bold, Is.True, "First blank cell should be bold");
        Assert.That(cell1.Attributes.ForegroundColor!.Value.NamedColor, Is.EqualTo(NamedColor.Yellow), "First blank cell should be yellow");
        
        Assert.That(cell2.Character, Is.EqualTo(' '), "Second blank character should be space");
        Assert.That(cell2.Attributes.Bold, Is.True, "Second blank cell should be bold");
        Assert.That(cell2.Attributes.ForegroundColor!.Value.NamedColor, Is.EqualTo(NamedColor.Yellow), "Second blank cell should be yellow");
    }

    [Test]
    public void InsertCharacters_WithCharacterProtection_ShouldUseCurrentProtection()
    {
        // Arrange: Write some text
        _terminal.Write("ABCD");
        
        // Set character protection
        _terminal.Write("\x1b[2\"q"); // DECSCA - set character protection
        
        // Move cursor to column 2 (after 'B')
        _terminal.Write("\x1b[1;3H");
        
        // Act: Insert 1 character (CSI @)
        _terminal.Write("\x1b[@");
        
        // Assert: Inserted character should have current character protection
        var cell = _terminal.ScreenBuffer.GetCell(0, 2);
        Assert.That(cell.Character, Is.EqualTo(' '), "Character should be space");
        Assert.That(cell.IsProtected, Is.True, "Cell should be protected");
    }

    [Test]
    public void DeleteCharacters_ShouldNotUseCharacterProtectionForBlanks()
    {
        // Arrange: Write some text
        _terminal.Write("ABCDEFGHIJ");
        
        // Set character protection
        _terminal.Write("\x1b[2\"q"); // DECSCA - set character protection
        
        // Move cursor to column 3 (at 'D')
        _terminal.Write("\x1b[1;4H");
        
        // Act: Delete 3 characters (CSI 3 P)
        _terminal.Write("\x1b[3P");
        
        // Assert: New blank characters should not be protected (following TypeScript behavior)
        var cell1 = _terminal.ScreenBuffer.GetCell(0, 8);
        var cell2 = _terminal.ScreenBuffer.GetCell(0, 9);
        
        Assert.That(cell1.Character, Is.EqualTo(' '), "First blank character should be space");
        Assert.That(cell1.IsProtected, Is.False, "First blank cell should not be protected");
        
        Assert.That(cell2.Character, Is.EqualTo(' '), "Second blank character should be space");
        Assert.That(cell2.IsProtected, Is.False, "Second blank cell should not be protected");
    }

    [Test]
    public void InsertCharacters_DefaultParameter_ShouldInsertOneCharacter()
    {
        // Arrange: Write some text
        _terminal.Write("ABCD");
        
        // Move cursor to column 2 (after 'B')
        _terminal.Write("\x1b[1;3H");
        
        // Act: Insert characters without parameter (CSI @)
        _terminal.Write("\x1b[@");
        
        // Assert: Should insert 1 character by default
        Assert.That(GetRowText(0), Is.EqualTo("AB CD     "), "Should insert 1 character by default");
    }

    [Test]
    public void DeleteCharacters_DefaultParameter_ShouldDeleteOneCharacter()
    {
        // Arrange: Write some text
        _terminal.Write("ABCDEFGHIJ");
        
        // Move cursor to column 3 (at 'D')
        _terminal.Write("\x1b[1;4H");
        
        // Act: Delete characters without parameter (CSI P)
        _terminal.Write("\x1b[P");
        
        // Assert: Should delete 1 character by default
        Assert.That(GetRowText(0), Is.EqualTo("ABCEFGHIJ "), "Should delete 1 character by default");
    }

    [Test]
    public void InsertCharacters_OutsideBuffer_ShouldHaveNoEffect()
    {
        // Arrange: Write some text
        _terminal.Write("ABCD");
        
        // Move cursor outside buffer bounds
        _terminal.Write("\x1b[1;15H");
        
        // Act: Insert characters (CSI 3 @)
        _terminal.Write("\x1b[3@");
        
        // Assert: Nothing should change
        Assert.That(GetRowText(0), Is.EqualTo("ABCD      "), "Text should remain unchanged");
    }

    [Test]
    public void DeleteCharacters_OutsideBuffer_ShouldHaveNoEffect()
    {
        // Arrange: Write some text
        _terminal.Write("ABCD");
        
        // Move cursor outside buffer bounds
        _terminal.Write("\x1b[1;15H");
        
        // Act: Delete characters (CSI 3 P)
        _terminal.Write("\x1b[3P");
        
        // Assert: Nothing should change
        Assert.That(GetRowText(0), Is.EqualTo("ABCD      "), "Text should remain unchanged");
    }

    [Test]
    public void CharacterOperations_ShouldClearWrapPending()
    {
        // Arrange: Write some text
        _terminal.Write("ABCDEFGHIJ");
        
        // Move cursor to middle of line (column 5)
        _terminal.Write("\x1b[1;5H");
        
        // Act: Insert character (CSI @)
        _terminal.Write("\x1b[@");
        
        // Assert: Character should be inserted, shifting others right
        Assert.That(GetRowText(0), Is.EqualTo("ABCD EFGHI"), "Character should be inserted at cursor position");
        
        // Write another character - should be written at cursor position
        _terminal.Write("X");
        
        // Assert: Character should be written at cursor position (column 5, where the blank was inserted)
        Assert.That(GetRowText(0), Is.EqualTo("ABCDXEFGHI"), "Character should be written at cursor position");
    }

    #endregion
}