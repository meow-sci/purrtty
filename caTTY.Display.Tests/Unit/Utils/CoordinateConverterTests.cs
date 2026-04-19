using caTTY.Display.Utils;
using Brutal.Numerics;
using NUnit.Framework;

namespace caTTY.Display.Tests.Unit.Utils;

/// <summary>
/// Unit tests for CoordinateConverter class.
/// Tests specific examples and edge cases for coordinate conversion functionality.
/// </summary>
[TestFixture]
[Category("Unit")]
public class CoordinateConverterTests
{
    private CoordinateConverter _converter = null!;

    [SetUp]
    public void SetUp()
    {
        _converter = new CoordinateConverter();
    }

    [Test]
    public void Constructor_ShouldInitializeWithFallbackMetrics()
    {
        // Arrange & Act
        var converter = new CoordinateConverter();

        // Assert
        Assert.That(converter.CharacterWidth, Is.EqualTo(8.0f));
        Assert.That(converter.LineHeight, Is.EqualTo(16.0f));
        Assert.That(converter.TerminalOrigin.X, Is.EqualTo(0.0f));
        Assert.That(converter.TerminalOrigin.Y, Is.EqualTo(0.0f));
    }

    [Test]
    public void UpdateMetrics_WithValidValues_ShouldUpdateCorrectly()
    {
        // Arrange
        float charWidth = 12.0f;
        float lineHeight = 24.0f;
        var origin = new float2(100, 200);

        // Act
        _converter.UpdateMetrics(charWidth, lineHeight, origin);

        // Assert
        Assert.That(_converter.CharacterWidth, Is.EqualTo(charWidth));
        Assert.That(_converter.LineHeight, Is.EqualTo(lineHeight));
        Assert.That(_converter.TerminalOrigin.X, Is.EqualTo(origin.X));
        Assert.That(_converter.TerminalOrigin.Y, Is.EqualTo(origin.Y));
    }

    [Test]
    public void UpdateMetrics_WithZeroValues_ShouldUseFallbackMetrics()
    {
        // Arrange
        var origin = new float2(50, 75);

        // Act
        _converter.UpdateMetrics(0, 0, origin);

        // Assert
        Assert.That(_converter.CharacterWidth, Is.EqualTo(8.0f));
        Assert.That(_converter.LineHeight, Is.EqualTo(16.0f));
        Assert.That(_converter.TerminalOrigin.X, Is.EqualTo(origin.X));
        Assert.That(_converter.TerminalOrigin.Y, Is.EqualTo(origin.Y));
    }

    [Test]
    public void UpdateMetrics_WithNegativeValues_ShouldUseFallbackMetrics()
    {
        // Arrange
        var origin = new float2(25, 50);

        // Act
        _converter.UpdateMetrics(-5, -10, origin);

        // Assert
        Assert.That(_converter.CharacterWidth, Is.EqualTo(8.0f));
        Assert.That(_converter.LineHeight, Is.EqualTo(16.0f));
        Assert.That(_converter.TerminalOrigin.X, Is.EqualTo(origin.X));
        Assert.That(_converter.TerminalOrigin.Y, Is.EqualTo(origin.Y));
    }

    [Test]
    public void PixelToCell_AtOrigin_ShouldReturnFirstCell()
    {
        // Arrange
        _converter.UpdateMetrics(10, 20, new float2(100, 200));
        var pixelPos = new float2(100, 200);

        // Act
        var result = _converter.PixelToCell(pixelPos, 80, 25);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Value.X1, Is.EqualTo(1));
        Assert.That(result.Value.Y1, Is.EqualTo(1));
    }

    [Test]
    public void PixelToCell_InSecondCell_ShouldReturnCorrectCoordinates()
    {
        // Arrange
        _converter.UpdateMetrics(10, 20, new float2(0, 0));
        var pixelPos = new float2(15, 25); // Middle of second column, second row

        // Act
        var result = _converter.PixelToCell(pixelPos, 80, 25);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Value.X1, Is.EqualTo(2)); // Second column (1-based)
        Assert.That(result.Value.Y1, Is.EqualTo(2)); // Second row (1-based)
    }

    [Test]
    public void PixelToCell_OutOfBounds_ShouldClampToValidRange()
    {
        // Arrange
        _converter.UpdateMetrics(10, 20, new float2(0, 0));
        var pixelPos = new float2(1000, 1000); // Way outside terminal

        // Act
        var result = _converter.PixelToCell(pixelPos, 80, 25);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Value.X1, Is.EqualTo(80)); // Clamped to max width
        Assert.That(result.Value.Y1, Is.EqualTo(25)); // Clamped to max height
    }

    [Test]
    public void PixelToCell_NegativeCoordinates_ShouldClampToMinimum()
    {
        // Arrange
        _converter.UpdateMetrics(10, 20, new float2(100, 200));
        var pixelPos = new float2(50, 150); // Before terminal origin

        // Act
        var result = _converter.PixelToCell(pixelPos, 80, 25);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result.Value.X1, Is.EqualTo(1)); // Clamped to minimum
        Assert.That(result.Value.Y1, Is.EqualTo(1)); // Clamped to minimum
    }

    [Test]
    public void CellToPixel_FirstCell_ShouldReturnOrigin()
    {
        // Arrange
        var origin = new float2(100, 200);
        _converter.UpdateMetrics(10, 20, origin);

        // Act
        var result = _converter.CellToPixel(1, 1);

        // Assert
        Assert.That(result.X, Is.EqualTo(origin.X));
        Assert.That(result.Y, Is.EqualTo(origin.Y));
    }

    [Test]
    public void CellToPixel_SecondCell_ShouldReturnCorrectPixelPosition()
    {
        // Arrange
        _converter.UpdateMetrics(10, 20, new float2(0, 0));

        // Act
        var result = _converter.CellToPixel(2, 3);

        // Assert
        Assert.That(result.X, Is.EqualTo(10.0f)); // Second column: (2-1) * 10
        Assert.That(result.Y, Is.EqualTo(40.0f)); // Third row: (3-1) * 20
    }

    [Test]
    public void CellToPixel_WithZeroCoordinates_ShouldHandleGracefully()
    {
        // Arrange
        _converter.UpdateMetrics(10, 20, new float2(50, 75));

        // Act
        var result = _converter.CellToPixel(0, 0);

        // Assert
        Assert.That(result.X, Is.EqualTo(50.0f)); // Origin X (clamped to 0-based)
        Assert.That(result.Y, Is.EqualTo(75.0f)); // Origin Y (clamped to 0-based)
    }

    [Test]
    public void IsWithinBounds_InsideTerminal_ShouldReturnTrue()
    {
        // Arrange
        _converter.UpdateMetrics(10, 20, new float2(100, 200));
        var pixelPos = new float2(150, 250);
        var terminalSize = new float2(200, 300); // 20x15 cells

        // Act
        var result = _converter.IsWithinBounds(pixelPos, terminalSize);

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void IsWithinBounds_OutsideTerminal_ShouldReturnFalse()
    {
        // Arrange
        _converter.UpdateMetrics(10, 20, new float2(100, 200));
        var pixelPos = new float2(350, 550); // Outside terminal bounds
        var terminalSize = new float2(200, 300);

        // Act
        var result = _converter.IsWithinBounds(pixelPos, terminalSize);

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void IsWithinBounds_AtExactBoundary_ShouldReturnFalse()
    {
        // Arrange
        _converter.UpdateMetrics(10, 20, new float2(0, 0));
        var pixelPos = new float2(100, 200); // Exactly at right/bottom edge
        var terminalSize = new float2(100, 200);

        // Act
        var result = _converter.IsWithinBounds(pixelPos, terminalSize);

        // Assert
        Assert.That(result, Is.False); // Boundary is exclusive
    }

    [Test]
    public void RoundTripConversion_ShouldBeConsistent()
    {
        // Arrange
        _converter.UpdateMetrics(12, 24, new float2(50, 100));
        var originalPixel = new float2(74, 148); // Within second cell

        // Act
        var cellCoords = _converter.PixelToCell(originalPixel, 80, 25);
        Assert.That(cellCoords, Is.Not.Null);
        
        var backToPixel = _converter.CellToPixel(cellCoords.Value.X1, cellCoords.Value.Y1);

        // Assert - should be at the top-left of the cell containing the original pixel
        // Calculate expected cell: pixel 74 with origin 50 and width 12 -> (74-50)/12 = 2.0 -> cell 3 (1-based)
        // Cell 3 top-left: 50 + (3-1)*12 = 74
        Assert.That(backToPixel.X, Is.EqualTo(74.0f)); // Cell 3: 50 + (3-1)*12 = 74
        Assert.That(backToPixel.Y, Is.EqualTo(148.0f)); // Cell 3: 100 + (3-1)*24 = 148
        
        // Original pixel should be within the cell boundaries
        Assert.That(originalPixel.X, Is.GreaterThanOrEqualTo(backToPixel.X));
        Assert.That(originalPixel.X, Is.LessThan(backToPixel.X + 12));
        Assert.That(originalPixel.Y, Is.GreaterThanOrEqualTo(backToPixel.Y));
        Assert.That(originalPixel.Y, Is.LessThan(backToPixel.Y + 24));
    }
}