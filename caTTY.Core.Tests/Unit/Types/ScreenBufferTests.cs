using caTTY.Core.Types;
using NUnit.Framework;

namespace caTTY.Core.Tests.Unit.Types;

/// <summary>
///     Unit tests for ScreenBuffer class.
///     Tests screen buffer operations including resize functionality.
/// </summary>
[TestFixture]
public class ScreenBufferTests
{
    private ScreenBuffer _buffer = null!;

    [SetUp]
    public void SetUp()
    {
        _buffer = new ScreenBuffer(80, 24);
    }

    [Test]
    public void Constructor_WithValidDimensions_CreatesBuffer()
    {
        var buffer = new ScreenBuffer(100, 50);
        
        Assert.That(buffer.Width, Is.EqualTo(100));
        Assert.That(buffer.Height, Is.EqualTo(50));
    }

    [Test]
    public void Constructor_WithInvalidDimensions_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ScreenBuffer(0, 24));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ScreenBuffer(80, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ScreenBuffer(-1, 24));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ScreenBuffer(80, -1));
    }

    [Test]
    public void Resize_WithLargerDimensions_PreservesContent()
    {
        // Arrange - set some test content
        var testCell = new Cell('A', SgrAttributes.Default, false);
        _buffer.SetCell(5, 10, testCell);
        _buffer.SetCell(20, 70, new Cell('B', SgrAttributes.Default, false));

        // Act - resize to larger dimensions
        _buffer.Resize(120, 40);

        // Assert - content is preserved and dimensions updated
        Assert.That(_buffer.Width, Is.EqualTo(120));
        Assert.That(_buffer.Height, Is.EqualTo(40));
        Assert.That(_buffer.GetCell(5, 10).Character, Is.EqualTo('A'));
        Assert.That(_buffer.GetCell(20, 70).Character, Is.EqualTo('B'));
    }

    [Test]
    public void Resize_WithSmallerWidth_TruncatesContent()
    {
        // Arrange - set content beyond new width
        _buffer.SetCell(10, 70, new Cell('X', SgrAttributes.Default, false));
        _buffer.SetCell(10, 30, new Cell('Y', SgrAttributes.Default, false));

        // Act - resize to smaller width
        _buffer.Resize(50, 24);

        // Assert - content within new bounds is preserved, beyond is lost
        Assert.That(_buffer.Width, Is.EqualTo(50));
        Assert.That(_buffer.Height, Is.EqualTo(24));
        Assert.That(_buffer.GetCell(10, 30).Character, Is.EqualTo('Y')); // Within bounds
        // Content at (10, 70) is now out of bounds and inaccessible
    }

    [Test]
    public void Resize_WithSmallerHeight_TruncatesRows()
    {
        // Arrange - set content beyond new height
        _buffer.SetCell(20, 10, new Cell('X', SgrAttributes.Default, false));
        _buffer.SetCell(5, 10, new Cell('Y', SgrAttributes.Default, false));

        // Act - resize to smaller height
        _buffer.Resize(80, 15);

        // Assert - content within new bounds is preserved
        Assert.That(_buffer.Width, Is.EqualTo(80));
        Assert.That(_buffer.Height, Is.EqualTo(15));
        Assert.That(_buffer.GetCell(5, 10).Character, Is.EqualTo('Y')); // Within bounds
        // Content at (20, 10) is now out of bounds and inaccessible
    }

    [Test]
    public void Resize_WithSameDimensions_DoesNothing()
    {
        // Arrange - set some content
        var testCell = new Cell('T', SgrAttributes.Default, false);
        _buffer.SetCell(10, 10, testCell);

        // Act - resize to same dimensions
        _buffer.Resize(80, 24);

        // Assert - no change
        Assert.That(_buffer.Width, Is.EqualTo(80));
        Assert.That(_buffer.Height, Is.EqualTo(24));
        Assert.That(_buffer.GetCell(10, 10).Character, Is.EqualTo('T'));
    }

    [Test]
    public void Resize_WithInvalidDimensions_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _buffer.Resize(0, 24));
        Assert.Throws<ArgumentOutOfRangeException>(() => _buffer.Resize(80, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => _buffer.Resize(-1, 24));
        Assert.Throws<ArgumentOutOfRangeException>(() => _buffer.Resize(80, -1));
    }

    [Test]
    public void Resize_NewCellsAreEmpty()
    {
        // Act - resize to larger dimensions
        _buffer.Resize(100, 30);

        // Assert - new cells are empty
        Assert.That(_buffer.GetCell(25, 85), Is.EqualTo(Cell.Empty)); // New row, new column
        Assert.That(_buffer.GetCell(10, 85), Is.EqualTo(Cell.Empty)); // Existing row, new column
        Assert.That(_buffer.GetCell(25, 10), Is.EqualTo(Cell.Empty)); // New row, existing column
    }

    [Test]
    public void Resize_PreservesTopToBottomRows()
    {
        // Arrange - fill first few rows with identifiable content
        for (int row = 0; row < 5; row++)
        {
            for (int col = 0; col < 10; col++)
            {
                _buffer.SetCell(row, col, new Cell((char)('A' + row), SgrAttributes.Default, false));
            }
        }

        // Act - resize to smaller height
        _buffer.Resize(80, 3);

        // Assert - top rows are preserved
        Assert.That(_buffer.Height, Is.EqualTo(3));
        for (int col = 0; col < 10; col++)
        {
            Assert.That(_buffer.GetCell(0, col).Character, Is.EqualTo('A')); // Row 0 preserved
            Assert.That(_buffer.GetCell(1, col).Character, Is.EqualTo('B')); // Row 1 preserved
            Assert.That(_buffer.GetCell(2, col).Character, Is.EqualTo('C')); // Row 2 preserved
        }
    }
}