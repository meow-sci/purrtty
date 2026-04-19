using caTTY.Core.Managers;
using caTTY.Core.Types;
using NUnit.Framework;

namespace caTTY.Core.Tests.Unit.Managers;

/// <summary>
///     Unit tests for ScreenBufferManager class.
///     Tests screen buffer operations in isolation.
/// </summary>
[TestFixture]
public class ScreenBufferManagerTests
{
    private IScreenBuffer _mockScreenBuffer = null!;
    private ScreenBufferManager _manager = null!;

    [SetUp]
    public void SetUp()
    {
        _mockScreenBuffer = new ScreenBuffer(80, 24);
        _manager = new ScreenBufferManager(_mockScreenBuffer);
    }

    [Test]
    public void Constructor_WithNullScreenBuffer_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new ScreenBufferManager(null!));
    }

    [Test]
    public void Width_ReturnsScreenBufferWidth()
    {
        Assert.That(_manager.Width, Is.EqualTo(80));
    }

    [Test]
    public void Height_ReturnsScreenBufferHeight()
    {
        Assert.That(_manager.Height, Is.EqualTo(24));
    }

    [Test]
    public void GetCell_WithValidCoordinates_ReturnsCell()
    {
        var cell = new Cell('A', SgrAttributes.Default, false);
        _mockScreenBuffer.SetCell(5, 10, cell);

        var result = _manager.GetCell(5, 10);

        Assert.That(result.Character, Is.EqualTo('A'));
    }

    [Test]
    public void GetCell_WithInvalidCoordinates_ReturnsEmptyCell()
    {
        var result = _manager.GetCell(-1, -1);
        Assert.That(result, Is.EqualTo(Cell.Empty));

        result = _manager.GetCell(100, 100);
        Assert.That(result, Is.EqualTo(Cell.Empty));
    }

    [Test]
    public void SetCell_WithValidCoordinates_SetsCell()
    {
        var cell = new Cell('B', SgrAttributes.Default, false);

        _manager.SetCell(3, 7, cell);

        var result = _mockScreenBuffer.GetCell(3, 7);
        Assert.That(result.Character, Is.EqualTo('B'));
    }

    [Test]
    public void SetCell_WithInvalidCoordinates_DoesNothing()
    {
        var cell = new Cell('C', SgrAttributes.Default, false);

        Assert.DoesNotThrow(() => _manager.SetCell(-1, -1, cell));
        Assert.DoesNotThrow(() => _manager.SetCell(100, 100, cell));
    }

    [Test]
    public void Clear_ClearsEntireBuffer()
    {
        // Set some cells first
        _manager.SetCell(0, 0, new Cell('X', SgrAttributes.Default, false));
        _manager.SetCell(10, 10, new Cell('Y', SgrAttributes.Default, false));

        _manager.Clear();

        // Verify cells are cleared
        Assert.That(_manager.GetCell(0, 0), Is.EqualTo(Cell.Empty));
        Assert.That(_manager.GetCell(10, 10), Is.EqualTo(Cell.Empty));
    }

    [Test]
    public void ClearRegion_WithValidBounds_ClearsRegion()
    {
        // Fill buffer with test data
        for (int row = 0; row < 5; row++)
        {
            for (int col = 0; col < 10; col++)
            {
                _manager.SetCell(row, col, new Cell('X', SgrAttributes.Default, false));
            }
        }

        // Clear region (1,1) to (3,5)
        _manager.ClearRegion(1, 1, 3, 5);

        // Verify region is cleared
        for (int row = 1; row <= 3; row++)
        {
            for (int col = 1; col <= 5; col++)
            {
                Assert.That(_manager.GetCell(row, col), Is.EqualTo(Cell.Empty));
            }
        }

        // Verify outside region is not cleared
        Assert.That(_manager.GetCell(0, 0).Character, Is.EqualTo('X'));
        Assert.That(_manager.GetCell(4, 9).Character, Is.EqualTo('X'));
    }

    [Test]
    public void ScrollUp_WithValidLines_ScrollsBuffer()
    {
        // Set test data
        _manager.SetCell(0, 0, new Cell('A', SgrAttributes.Default, false));
        _manager.SetCell(1, 0, new Cell('B', SgrAttributes.Default, false));
        _manager.SetCell(2, 0, new Cell('C', SgrAttributes.Default, false));

        _manager.ScrollUp(1);

        // Verify scroll
        Assert.That(_manager.GetCell(0, 0).Character, Is.EqualTo('B'));
        Assert.That(_manager.GetCell(1, 0).Character, Is.EqualTo('C'));
        Assert.That(_manager.GetCell(2, 0), Is.EqualTo(Cell.Empty));
    }

    [Test]
    public void ScrollDown_WithValidLines_ScrollsBuffer()
    {
        // Set test data
        _manager.SetCell(0, 0, new Cell('A', SgrAttributes.Default, false));
        _manager.SetCell(1, 0, new Cell('B', SgrAttributes.Default, false));

        _manager.ScrollDown(1);

        // Verify scroll
        Assert.That(_manager.GetCell(0, 0), Is.EqualTo(Cell.Empty));
        Assert.That(_manager.GetCell(1, 0).Character, Is.EqualTo('A'));
        Assert.That(_manager.GetCell(2, 0).Character, Is.EqualTo('B'));
    }

    [Test]
    public void GetRow_WithValidRow_ReturnsRowSpan()
    {
        // Set test data in row 5
        for (int col = 0; col < 10; col++)
        {
            _manager.SetCell(5, col, new Cell((char)('A' + col), SgrAttributes.Default, false));
        }

        var row = _manager.GetRow(5);

        Assert.That(row.Length, Is.EqualTo(80));
        for (int col = 0; col < 10; col++)
        {
            Assert.That(row[col].Character, Is.EqualTo((char)('A' + col)));
        }
    }

    [Test]
    public void GetRow_WithInvalidRow_ReturnsEmptySpan()
    {
        var row = _manager.GetRow(-1);
        Assert.That(row.IsEmpty, Is.True);

        row = _manager.GetRow(100);
        Assert.That(row.IsEmpty, Is.True);
    }

    [Test]
    public void Resize_WithValidDimensions_UpdatesDimensions()
    {
        // Arrange
        int newWidth = 100;
        int newHeight = 50;

        // Act
        _manager.Resize(newWidth, newHeight);

        // Assert
        Assert.That(_manager.Width, Is.EqualTo(newWidth));
        Assert.That(_manager.Height, Is.EqualTo(newHeight));
    }

    [Test]
    public void Resize_WithSameDimensions_DoesNothing()
    {
        // Arrange
        int originalWidth = _manager.Width;
        int originalHeight = _manager.Height;

        // Act
        _manager.Resize(originalWidth, originalHeight);

        // Assert
        Assert.That(_manager.Width, Is.EqualTo(originalWidth));
        Assert.That(_manager.Height, Is.EqualTo(originalHeight));
    }

    [Test]
    public void Resize_WithInvalidDimensions_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _manager.Resize(0, 24));
        Assert.Throws<ArgumentOutOfRangeException>(() => _manager.Resize(80, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => _manager.Resize(1001, 24));
        Assert.Throws<ArgumentOutOfRangeException>(() => _manager.Resize(80, 1001));
    }
}