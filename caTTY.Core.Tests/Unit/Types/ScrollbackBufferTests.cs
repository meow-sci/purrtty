using NUnit.Framework;
using caTTY.Core.Types;

namespace caTTY.Core.Tests.Unit.Types;

[TestFixture]
public class ScrollbackBufferTests
{
    private ScrollbackBuffer _scrollbackBuffer = null!;
    private const int TestColumns = 80;
    private const int TestMaxLines = 5;

    [SetUp]
    public void SetUp()
    {
        _scrollbackBuffer = new ScrollbackBuffer(TestMaxLines, TestColumns);
    }

    [TearDown]
    public void TearDown()
    {
        _scrollbackBuffer?.Dispose();
    }

    [Test]
    public void Constructor_ValidParameters_InitializesCorrectly()
    {
        // Arrange & Act
        using var buffer = new ScrollbackBuffer(100, 80);

        // Assert
        Assert.That(buffer.MaxLines, Is.EqualTo(100));
        Assert.That(buffer.CurrentLines, Is.EqualTo(0));
    }

    [Test]
    public void Constructor_InvalidParameters_ThrowsException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => new ScrollbackBuffer(-1, 80));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ScrollbackBuffer(100, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ScrollbackBuffer(100, -1));
    }

    [Test]
    public void AddLine_SingleLine_StoresCorrectly()
    {
        // Arrange
        var line = CreateTestLine("Hello World");

        // Act
        _scrollbackBuffer.AddLine(line);

        // Assert
        Assert.That(_scrollbackBuffer.CurrentLines, Is.EqualTo(1));
        var retrievedLine = _scrollbackBuffer.GetLine(0);
        Assert.That(GetLineText(retrievedLine), Is.EqualTo("Hello World" + new string(' ', TestColumns - 11)));
    }

    [Test]
    public void AddLine_MultipleLines_StoresInOrder()
    {
        // Arrange & Act
        _scrollbackBuffer.AddLine(CreateTestLine("Line 1"));
        _scrollbackBuffer.AddLine(CreateTestLine("Line 2"));
        _scrollbackBuffer.AddLine(CreateTestLine("Line 3"));

        // Assert
        Assert.That(_scrollbackBuffer.CurrentLines, Is.EqualTo(3));
        Assert.That(GetLineText(_scrollbackBuffer.GetLine(0)), Does.StartWith("Line 1"));
        Assert.That(GetLineText(_scrollbackBuffer.GetLine(1)), Does.StartWith("Line 2"));
        Assert.That(GetLineText(_scrollbackBuffer.GetLine(2)), Does.StartWith("Line 3"));
    }

    [Test]
    public void AddLine_ExceedsMaxLines_ImplementsCircularBuffer()
    {
        // Arrange & Act
        for (int i = 0; i < TestMaxLines + 2; i++)
        {
            var line = CreateTestLine($"Line {i}");
            _scrollbackBuffer.AddLine(line);
        }

        // Assert
        Assert.That(_scrollbackBuffer.CurrentLines, Is.EqualTo(TestMaxLines));
        
        // Should have lines 2, 3, 4, 5, 6 (oldest lines 0, 1 removed)
        var firstLine = _scrollbackBuffer.GetLine(0);
        Assert.That(GetLineText(firstLine), Does.StartWith("Line 2"));
        
        var lastLine = _scrollbackBuffer.GetLine(TestMaxLines - 1);
        Assert.That(GetLineText(lastLine), Does.StartWith("Line 6"));
    }

    [Test]
    public void AddLine_ShortLine_PadsWithSpaces()
    {
        // Arrange
        var shortLine = CreateTestLine("Hi");

        // Act
        _scrollbackBuffer.AddLine(shortLine);

        // Assert
        var retrievedLine = _scrollbackBuffer.GetLine(0);
        Assert.That(retrievedLine.Length, Is.EqualTo(TestColumns));
        Assert.That(GetLineText(retrievedLine), Is.EqualTo("Hi" + new string(' ', TestColumns - 2)));
    }

    [Test]
    public void AddLine_LongLine_TruncatesToColumns()
    {
        // Arrange
        var longText = new string('X', TestColumns + 10);
        var longLine = CreateTestLine(longText);

        // Act
        _scrollbackBuffer.AddLine(longLine);

        // Assert
        var retrievedLine = _scrollbackBuffer.GetLine(0);
        Assert.That(retrievedLine.Length, Is.EqualTo(TestColumns));
        Assert.That(GetLineText(retrievedLine), Is.EqualTo(new string('X', TestColumns)));
    }

    [Test]
    public void GetLine_InvalidIndex_ThrowsException()
    {
        // Arrange
        _scrollbackBuffer.AddLine(CreateTestLine("Test"));

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => _scrollbackBuffer.GetLine(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => _scrollbackBuffer.GetLine(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => _scrollbackBuffer.GetLine(2));
    }

    [Test]
    public void Clear_RemovesAllLines()
    {
        // Arrange
        _scrollbackBuffer.AddLine(CreateTestLine("Line 1"));
        _scrollbackBuffer.AddLine(CreateTestLine("Line 2"));

        // Act
        _scrollbackBuffer.Clear();

        // Assert
        Assert.That(_scrollbackBuffer.CurrentLines, Is.EqualTo(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => _scrollbackBuffer.GetLine(0));
    }

    [Test]
    public void AddLine_ZeroMaxLines_DoesNothing()
    {
        // Arrange
        using var buffer = new ScrollbackBuffer(0, TestColumns);
        var line = CreateTestLine("Test");

        // Act
        buffer.AddLine(line);

        // Assert
        Assert.That(buffer.CurrentLines, Is.EqualTo(0));
    }

    [Test]
    public void AddLine_PreservesAttributes()
    {
        // Arrange
        var cells = new Cell[TestColumns];
        var boldAttributes = new SgrAttributes(bold: true);
        for (int i = 0; i < TestColumns; i++)
        {
            char ch = i < 4 ? "Test"[i] : ' ';
            cells[i] = new Cell(ch, boldAttributes);
        }

        // Act
        _scrollbackBuffer.AddLine(cells);

        // Assert
        var retrievedLine = _scrollbackBuffer.GetLine(0);
        Assert.That(retrievedLine[0].Attributes.Bold, Is.True);
        Assert.That(retrievedLine[1].Attributes.Bold, Is.True);
        Assert.That(retrievedLine[2].Attributes.Bold, Is.True);
        Assert.That(retrievedLine[3].Attributes.Bold, Is.True);
    }

    private ReadOnlySpan<Cell> CreateTestLine(string text)
    {
        var cells = new Cell[Math.Min(text.Length, TestColumns)];
        for (int i = 0; i < cells.Length; i++)
        {
            cells[i] = new Cell(text[i], SgrAttributes.Default);
        }
        return cells;
    }

    private string GetLineText(ReadOnlySpan<Cell> line)
    {
        var chars = new char[line.Length];
        for (int i = 0; i < line.Length; i++)
        {
            chars[i] = line[i].Character;
        }
        return new string(chars);
    }
}