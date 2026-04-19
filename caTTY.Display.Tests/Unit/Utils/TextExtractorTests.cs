using NUnit.Framework;
using caTTY.Core.Types;
using caTTY.Display.Types;
using caTTY.Display.Utils;
using System;
using System.Collections.Generic;

namespace caTTY.Display.Tests.Unit.Utils;

/// <summary>
/// Unit tests for the TextExtractor utility class.
/// Tests text extraction from terminal cells with various selection scenarios.
/// </summary>
[TestFixture]
[Category("Unit")]
public class TextExtractorTests
{
    private static ReadOnlyMemory<Cell>[] CreateTestViewport(string[] lines)
    {
        var viewport = new ReadOnlyMemory<Cell>[lines.Length];
        
        for (int row = 0; row < lines.Length; row++)
        {
            string line = lines[row];
            var cells = new Cell[Math.Max(line.Length, 80)]; // Ensure minimum width
            
            for (int col = 0; col < cells.Length; col++)
            {
                char ch = col < line.Length ? line[col] : ' ';
                cells[col] = new Cell(ch, SgrAttributes.Default);
            }
            
            viewport[row] = cells.AsMemory();
        }
        
        return viewport;
    }

    [Test]
    public void ExtractText_EmptySelection_ShouldReturnEmptyString()
    {
        // Arrange
        var viewport = CreateTestViewport(new[] { "Hello World" });
        var emptySelection = TextSelection.Empty(0, 0);

        // Act
        string result = TextExtractor.ExtractText(emptySelection, viewport, 80);

        // Assert
        Assert.That(result, Is.EqualTo(string.Empty));
    }

    [Test]
    public void ExtractText_SingleLineSelection_ShouldExtractCorrectText()
    {
        // Arrange
        var viewport = CreateTestViewport(new[] { "Hello World" });
        var selection = TextSelection.FromCoordinates(0, 6, 0, 10); // "World"

        // Act
        string result = TextExtractor.ExtractText(selection, viewport, 80);

        // Assert
        Assert.That(result, Is.EqualTo("World"));
    }

    [Test]
    public void ExtractText_MultiLineSelection_ShouldExtractWithLineEndings()
    {
        // Arrange
        var viewport = CreateTestViewport(new[] { 
            "First line", 
            "Second line", 
            "Third line" 
        });
        var selection = TextSelection.FromCoordinates(0, 6, 2, 5); // "line\nSecond line\nThird"

        // Act
        string result = TextExtractor.ExtractText(selection, viewport, 80);

        // Assert
        string expected = "line\nSecond line\nThird";
        Assert.That(result, Is.EqualTo(expected));
    }

    [Test]
    public void ExtractText_WithTrailingSpaces_ShouldTrimWhenRequested()
    {
        // Arrange
        var viewport = CreateTestViewport(new[] { "Hello   " });
        var selection = TextSelection.FromCoordinates(0, 0, 0, 7); // "Hello   "

        // Act
        string resultTrimmed = TextExtractor.ExtractText(selection, viewport, 80, trimTrailingSpaces: true);
        string resultNotTrimmed = TextExtractor.ExtractText(selection, viewport, 80, trimTrailingSpaces: false);

        // Assert
        Assert.That(resultTrimmed, Is.EqualTo("Hello"));
        Assert.That(resultNotTrimmed, Is.EqualTo("Hello   "));
    }

    [Test]
    public void ExtractText_WithNormalizeLineEndings_ShouldUseCorrectEndings()
    {
        // Arrange
        var viewport = CreateTestViewport(new[] { "Line 1", "Line 2" });
        var selection = TextSelection.FromCoordinates(0, 0, 1, 5); // "Line 1\nLine 2"

        // Act
        string resultNormalized = TextExtractor.ExtractText(selection, viewport, 80, normalizeLineEndings: true);
        string resultNotNormalized = TextExtractor.ExtractText(selection, viewport, 80, normalizeLineEndings: false);

        // Assert
        Assert.That(resultNormalized, Is.EqualTo("Line 1\nLine 2"));
        Assert.That(resultNotNormalized, Is.EqualTo($"Line 1{Environment.NewLine}Line 2"));
    }

    [Test]
    public void ExtractText_OutOfBounds_ShouldHandleGracefully()
    {
        // Arrange
        var viewport = CreateTestViewport(new[] { "Short" });
        var selection = TextSelection.FromCoordinates(0, 0, 0, 100); // Beyond line length

        // Act
        string result = TextExtractor.ExtractText(selection, viewport, 80);

        // Assert
        Assert.That(result, Does.StartWith("Short"));
    }

    [Test]
    public void ExtractText_SelectionBeyondViewport_ShouldHandleGracefully()
    {
        // Arrange
        var viewport = CreateTestViewport(new[] { "Line 1", "Line 2" });
        var selection = TextSelection.FromCoordinates(0, 0, 1, 5); // Select from start to end of second line

        // Act
        string result = TextExtractor.ExtractText(selection, viewport, 80);

        // Assert - Should extract what's available within the viewport bounds
        Assert.That(result, Does.StartWith("Line 1"));
        Assert.That(result, Does.Contain("Line 2"));
    }

    [Test]
    public void ExtractAllText_ShouldExtractEntireViewport()
    {
        // Arrange
        var viewport = CreateTestViewport(new[] { 
            "First line", 
            "Second line", 
            "Third line" 
        });

        // Act
        string result = TextExtractor.ExtractAllText(viewport);

        // Assert
        string expected = "First line\nSecond line\nThird line";
        Assert.That(result, Is.EqualTo(expected));
    }

    [Test]
    public void ExtractAllText_EmptyViewport_ShouldReturnEmptyString()
    {
        // Arrange
        var emptyViewport = Array.Empty<ReadOnlyMemory<Cell>>();

        // Act
        string result = TextExtractor.ExtractAllText(emptyViewport);

        // Assert
        Assert.That(result, Is.EqualTo(string.Empty));
    }

    [Test]
    public void IsLineWrapped_ShouldDetectWrappedLines()
    {
        // Arrange
        var fullLine = new Cell[80];
        var partialLine = new Cell[40];
        
        // Fill full line with non-whitespace at the end
        for (int i = 0; i < fullLine.Length; i++)
        {
            fullLine[i] = new Cell(i == fullLine.Length - 1 ? 'X' : 'A', SgrAttributes.Default);
        }
        
        // Fill partial line
        for (int i = 0; i < partialLine.Length; i++)
        {
            partialLine[i] = new Cell('A', SgrAttributes.Default);
        }

        // Act
        bool fullLineWrapped = TextExtractor.IsLineWrapped(fullLine.AsSpan(), 80);
        bool partialLineWrapped = TextExtractor.IsLineWrapped(partialLine.AsSpan(), 80);

        // Assert
        Assert.That(fullLineWrapped, Is.True);
        Assert.That(partialLineWrapped, Is.False);
    }

    [Test]
    public void ExtractTextWithWrappedLines_ShouldHandleWrapping()
    {
        // Arrange - create a scenario with wrapped lines
        var viewport = CreateTestViewport(new[] { 
            "This is a very long line that would wrap", // Assume this wraps
            "to the next line and continues here",      // Continuation
            "This is a separate line"                   // Not wrapped
        });
        
        var selection = TextSelection.FromCoordinates(0, 0, 2, 10);

        // Act
        string result = TextExtractor.ExtractTextWithWrappedLines(selection, viewport, 40);

        // Assert - should contain the text (exact behavior depends on wrapping detection)
        Assert.That(result, Does.Contain("This is a very long line"));
        Assert.That(result, Does.Contain("This is a s")); // Partial match due to selection bounds
    }

    [Test]
    public void ExtractText_WithNullCharacters_ShouldReplaceWithSpaces()
    {
        // Arrange
        var viewport = new ReadOnlyMemory<Cell>[1];
        var cells = new Cell[5];
        cells[0] = new Cell('H', SgrAttributes.Default);
        cells[1] = new Cell('\0', SgrAttributes.Default); // Null character
        cells[2] = new Cell('l', SgrAttributes.Default);
        cells[3] = new Cell('\0', SgrAttributes.Default); // Null character
        cells[4] = new Cell('o', SgrAttributes.Default);
        viewport[0] = cells.AsMemory();
        
        var selection = TextSelection.FromCoordinates(0, 0, 0, 4);

        // Act
        string result = TextExtractor.ExtractText(selection, viewport, 80);

        // Assert
        Assert.That(result, Is.EqualTo("H l o"));
    }
}