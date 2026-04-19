using NUnit.Framework;
using caTTY.Display.Types;

namespace caTTY.Display.Tests.Unit.Types;

/// <summary>
/// Unit tests for the TextSelection and SelectionPosition types.
/// Tests selection creation, normalization, and position operations.
/// </summary>
[TestFixture]
[Category("Unit")]
public class TextSelectionTests
{
    [Test]
    public void SelectionPosition_Constructor_ShouldClampNegativeValues()
    {
        // Arrange & Act
        var position = new SelectionPosition(-5, -10);

        // Assert
        Assert.That(position.Row, Is.EqualTo(0));
        Assert.That(position.Col, Is.EqualTo(0));
    }

    [Test]
    public void SelectionPosition_CompareTo_ShouldOrderCorrectly()
    {
        // Arrange
        var pos1 = new SelectionPosition(1, 5);
        var pos2 = new SelectionPosition(1, 10);
        var pos3 = new SelectionPosition(2, 0);

        // Act & Assert
        Assert.That(pos1.CompareTo(pos2), Is.LessThan(0));
        Assert.That(pos2.CompareTo(pos1), Is.GreaterThan(0));
        Assert.That(pos1.CompareTo(pos3), Is.LessThan(0));
        Assert.That(pos1.CompareTo(new SelectionPosition(1, 5)), Is.EqualTo(0));
    }

    [Test]
    public void TextSelection_Constructor_ShouldNormalizePositions()
    {
        // Arrange
        var start = new SelectionPosition(5, 10);
        var end = new SelectionPosition(2, 5);

        // Act
        var selection = new TextSelection(start, end);

        // Assert - positions should be swapped so Start is before End
        Assert.That(selection.Start, Is.EqualTo(end));
        Assert.That(selection.End, Is.EqualTo(start));
    }

    [Test]
    public void TextSelection_FromCoordinates_ShouldCreateNormalizedSelection()
    {
        // Act
        var selection = TextSelection.FromCoordinates(10, 20, 5, 15);

        // Assert
        Assert.That(selection.Start.Row, Is.EqualTo(5));
        Assert.That(selection.Start.Col, Is.EqualTo(15));
        Assert.That(selection.End.Row, Is.EqualTo(10));
        Assert.That(selection.End.Col, Is.EqualTo(20));
    }

    [Test]
    public void TextSelection_Empty_ShouldCreateEmptySelection()
    {
        // Act
        var selection = TextSelection.Empty(5, 10);

        // Assert
        Assert.That(selection.IsEmpty, Is.True);
        Assert.That(selection.Start.Row, Is.EqualTo(5));
        Assert.That(selection.Start.Col, Is.EqualTo(10));
        Assert.That(selection.End.Row, Is.EqualTo(5));
        Assert.That(selection.End.Col, Is.EqualTo(10));
    }

    [Test]
    public void TextSelection_IsMultiLine_ShouldDetectMultiLineSelections()
    {
        // Arrange
        var singleLine = TextSelection.FromCoordinates(5, 10, 5, 20);
        var multiLine = TextSelection.FromCoordinates(5, 10, 7, 5);

        // Assert
        Assert.That(singleLine.IsMultiLine, Is.False);
        Assert.That(multiLine.IsMultiLine, Is.True);
    }

    [Test]
    public void TextSelection_Contains_ShouldDetectPositionsWithinSelection()
    {
        // Arrange
        var selection = TextSelection.FromCoordinates(2, 5, 4, 10);

        // Act & Assert
        Assert.That(selection.Contains(2, 5), Is.True);  // Start position
        Assert.That(selection.Contains(4, 10), Is.True); // End position
        Assert.That(selection.Contains(3, 7), Is.True);  // Middle position
        Assert.That(selection.Contains(1, 5), Is.False); // Before start
        Assert.That(selection.Contains(5, 10), Is.False); // After end
        Assert.That(selection.Contains(2, 4), Is.False); // Same row, before start col
        Assert.That(selection.Contains(4, 11), Is.False); // Same row, after end col
    }

    [Test]
    public void TextSelection_ExtendTo_ShouldCreateExtendedSelection()
    {
        // Arrange
        var originalSelection = TextSelection.FromCoordinates(2, 5, 3, 10);
        var newPosition = new SelectionPosition(5, 15);

        // Act
        var extendedSelection = originalSelection.ExtendTo(newPosition);

        // Assert
        Assert.That(extendedSelection.Start, Is.EqualTo(originalSelection.Start));
        Assert.That(extendedSelection.End, Is.EqualTo(newPosition));
    }

    [Test]
    public void TextSelection_ExtendTo_WithEarlierPosition_ShouldNormalize()
    {
        // Arrange
        var originalSelection = TextSelection.FromCoordinates(5, 10, 7, 15);
        var earlierPosition = new SelectionPosition(3, 5);

        // Act
        var extendedSelection = originalSelection.ExtendTo(earlierPosition);

        // Assert - should normalize so earlier position becomes Start
        Assert.That(extendedSelection.Start, Is.EqualTo(earlierPosition));
        Assert.That(extendedSelection.End, Is.EqualTo(originalSelection.Start));
    }

    [Test]
    public void TextSelection_Equality_ShouldWorkCorrectly()
    {
        // Arrange
        var selection1 = TextSelection.FromCoordinates(2, 5, 4, 10);
        var selection2 = TextSelection.FromCoordinates(2, 5, 4, 10);
        var selection3 = TextSelection.FromCoordinates(2, 5, 4, 11);

        // Assert
        Assert.That(selection1, Is.EqualTo(selection2));
        Assert.That(selection1, Is.Not.EqualTo(selection3));
        Assert.That(selection1 == selection2, Is.True);
        Assert.That(selection1 != selection3, Is.True);
    }

    [Test]
    public void TextSelection_ToString_ShouldProvideReadableOutput()
    {
        // Arrange
        var emptySelection = TextSelection.Empty(5, 10);
        var normalSelection = TextSelection.FromCoordinates(2, 5, 4, 10);

        // Act
        string emptyString = emptySelection.ToString();
        string normalString = normalSelection.ToString();

        // Assert
        Assert.That(emptyString, Does.Contain("Empty"));
        Assert.That(emptyString, Does.Contain("(5, 10)"));
        Assert.That(normalString, Does.Contain("(2, 5)"));
        Assert.That(normalString, Does.Contain("(4, 10)"));
    }

    [Test]
    public void SelectionPosition_Operators_ShouldWorkCorrectly()
    {
        // Arrange
        var pos1 = new SelectionPosition(2, 5);
        var pos2 = new SelectionPosition(2, 10);
        var pos3 = new SelectionPosition(3, 0);

        // Assert
        Assert.That(pos1 < pos2, Is.True);
        Assert.That(pos2 > pos1, Is.True);
        Assert.That(pos1 <= pos2, Is.True);
        Assert.That(pos2 >= pos1, Is.True);
        Assert.That(pos1 < pos3, Is.True);
        Assert.That(pos1 == new SelectionPosition(2, 5), Is.True);
        Assert.That(pos1 != pos2, Is.True);
    }
}