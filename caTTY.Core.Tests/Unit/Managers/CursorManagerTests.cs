using caTTY.Core.Managers;
using caTTY.Core.Types;
using NUnit.Framework;

namespace caTTY.Core.Tests.Unit.Managers;

/// <summary>
///     Unit tests for CursorManager class.
///     Tests cursor positioning and state management in isolation.
/// </summary>
[TestFixture]
public class CursorManagerTests
{
    private ICursor _mockCursor = null!;
    private CursorManager _manager = null!;

    [SetUp]
    public void SetUp()
    {
        _mockCursor = new Cursor();
        _manager = new CursorManager(_mockCursor);
    }

    [Test]
    public void Constructor_WithNullCursor_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new CursorManager(null!));
    }

    [Test]
    public void Row_GetSet_UpdatesCursorPosition()
    {
        _manager.Row = 5;
        Assert.That(_manager.Row, Is.EqualTo(5));
        Assert.That(_mockCursor.Row, Is.EqualTo(5));
    }

    [Test]
    public void Column_GetSet_UpdatesCursorPosition()
    {
        _manager.Column = 10;
        Assert.That(_manager.Column, Is.EqualTo(10));
        Assert.That(_mockCursor.Col, Is.EqualTo(10));
    }

    [Test]
    public void Visible_GetSet_UpdatesCursorVisibility()
    {
        _manager.Visible = false;
        Assert.That(_manager.Visible, Is.False);
        Assert.That(_mockCursor.Visible, Is.False);

        _manager.Visible = true;
        Assert.That(_manager.Visible, Is.True);
        Assert.That(_mockCursor.Visible, Is.True);
    }

    [Test]
    public void Style_GetSet_UpdatesCursorStyle()
    {
        _manager.Style = CursorStyle.SteadyBlock;
        Assert.That(_manager.Style, Is.EqualTo(CursorStyle.SteadyBlock));
    }

    [Test]
    public void WrapPending_InitiallyFalse()
    {
        Assert.That(_manager.WrapPending, Is.False);
    }

    [Test]
    public void MoveTo_UpdatesPositionAndClearsWrapPending()
    {
        _manager.SetWrapPending(true);

        _manager.MoveTo(3, 7);

        Assert.That(_manager.Row, Is.EqualTo(3));
        Assert.That(_manager.Column, Is.EqualTo(7));
        Assert.That(_manager.WrapPending, Is.False);
    }

    [Test]
    public void MoveUp_UpdatesPositionAndClearsWrapPending()
    {
        _manager.MoveTo(10, 5);
        _manager.SetWrapPending(true);

        _manager.MoveUp(3);

        Assert.That(_manager.Row, Is.EqualTo(7));
        Assert.That(_manager.Column, Is.EqualTo(5));
        Assert.That(_manager.WrapPending, Is.False);
    }

    [Test]
    public void MoveUp_WithZeroOrNegative_DoesNothing()
    {
        _manager.MoveTo(5, 5);

        _manager.MoveUp(0);
        Assert.That(_manager.Row, Is.EqualTo(5));

        _manager.MoveUp(-1);
        Assert.That(_manager.Row, Is.EqualTo(5));
    }

    [Test]
    public void MoveUp_BeyondTopBoundary_ClampsToZero()
    {
        _manager.MoveTo(2, 5);

        _manager.MoveUp(5);

        Assert.That(_manager.Row, Is.EqualTo(0));
        Assert.That(_manager.Column, Is.EqualTo(5));
    }

    [Test]
    public void MoveDown_UpdatesPositionAndClearsWrapPending()
    {
        _manager.MoveTo(5, 10);
        _manager.SetWrapPending(true);

        _manager.MoveDown(2);

        Assert.That(_manager.Row, Is.EqualTo(7));
        Assert.That(_manager.Column, Is.EqualTo(10));
        Assert.That(_manager.WrapPending, Is.False);
    }

    [Test]
    public void MoveLeft_UpdatesPositionAndClearsWrapPending()
    {
        _manager.MoveTo(5, 10);
        _manager.SetWrapPending(true);

        _manager.MoveLeft(3);

        Assert.That(_manager.Row, Is.EqualTo(5));
        Assert.That(_manager.Column, Is.EqualTo(7));
        Assert.That(_manager.WrapPending, Is.False);
    }

    [Test]
    public void MoveLeft_BeyondLeftBoundary_ClampsToZero()
    {
        _manager.MoveTo(5, 2);

        _manager.MoveLeft(5);

        Assert.That(_manager.Row, Is.EqualTo(5));
        Assert.That(_manager.Column, Is.EqualTo(0));
    }

    [Test]
    public void MoveRight_UpdatesPositionAndClearsWrapPending()
    {
        _manager.MoveTo(5, 10);
        _manager.SetWrapPending(true);

        _manager.MoveRight(3);

        Assert.That(_manager.Row, Is.EqualTo(5));
        Assert.That(_manager.Column, Is.EqualTo(13));
        Assert.That(_manager.WrapPending, Is.False);
    }

    [Test]
    public void SavePosition_StoresCurrentPosition()
    {
        _manager.MoveTo(8, 15);

        _manager.SavePosition();

        // Move to different position
        _manager.MoveTo(2, 3);
        Assert.That(_manager.Row, Is.EqualTo(2));
        Assert.That(_manager.Column, Is.EqualTo(3));
    }

    [Test]
    public void RestorePosition_RestoresSavedPosition()
    {
        _manager.MoveTo(8, 15);
        _manager.SavePosition();
        _manager.MoveTo(2, 3);

        _manager.RestorePosition();

        Assert.That(_manager.Row, Is.EqualTo(8));
        Assert.That(_manager.Column, Is.EqualTo(15));
        Assert.That(_manager.WrapPending, Is.False);
    }

    [Test]
    public void RestorePosition_WithoutSavedPosition_DoesNothing()
    {
        _manager.MoveTo(5, 10);

        _manager.RestorePosition();

        Assert.That(_manager.Row, Is.EqualTo(5));
        Assert.That(_manager.Column, Is.EqualTo(10));
    }

    [Test]
    public void ClampToBuffer_WithinBounds_DoesNothing()
    {
        _manager.MoveTo(5, 10);

        _manager.ClampToBuffer(80, 24);

        Assert.That(_manager.Row, Is.EqualTo(5));
        Assert.That(_manager.Column, Is.EqualTo(10));
    }

    [Test]
    public void ClampToBuffer_OutOfBounds_ClampsPosition()
    {
        _manager.MoveTo(30, 100);

        _manager.ClampToBuffer(80, 24);

        Assert.That(_manager.Row, Is.EqualTo(23));
        Assert.That(_manager.Column, Is.EqualTo(79));
        Assert.That(_manager.WrapPending, Is.False);
    }

    [Test]
    public void SetWrapPending_UpdatesWrapPendingState()
    {
        _manager.SetWrapPending(true);
        Assert.That(_manager.WrapPending, Is.True);

        _manager.SetWrapPending(false);
        Assert.That(_manager.WrapPending, Is.False);
    }

    [Test]
    public void AdvanceCursor_WithWrapPending_WrapsToNextLine()
    {
        _manager.MoveTo(5, 10);
        _manager.SetWrapPending(true);

        bool wrapped = _manager.AdvanceCursor(80, true);

        Assert.That(wrapped, Is.True);
        Assert.That(_manager.Row, Is.EqualTo(6));
        Assert.That(_manager.Column, Is.EqualTo(0));
        Assert.That(_manager.WrapPending, Is.False);
    }

    [Test]
    public void AdvanceCursor_NormalAdvancement_MovesRight()
    {
        _manager.MoveTo(5, 10);

        bool wrapped = _manager.AdvanceCursor(80, true);

        Assert.That(wrapped, Is.False);
        Assert.That(_manager.Row, Is.EqualTo(5));
        Assert.That(_manager.Column, Is.EqualTo(11));
        Assert.That(_manager.WrapPending, Is.False);
    }

    [Test]
    public void AdvanceCursor_AtRightEdgeWithAutoWrap_SetsWrapPending()
    {
        _manager.MoveTo(5, 79);

        bool wrapped = _manager.AdvanceCursor(80, true);

        Assert.That(wrapped, Is.False);
        Assert.That(_manager.Row, Is.EqualTo(5));
        Assert.That(_manager.Column, Is.EqualTo(79));
        Assert.That(_manager.WrapPending, Is.True);
    }

    [Test]
    public void AdvanceCursor_AtRightEdgeWithoutAutoWrap_StaysAtEdge()
    {
        _manager.MoveTo(5, 79);

        bool wrapped = _manager.AdvanceCursor(80, false);

        Assert.That(wrapped, Is.False);
        Assert.That(_manager.Row, Is.EqualTo(5));
        Assert.That(_manager.Column, Is.EqualTo(79));
        Assert.That(_manager.WrapPending, Is.False);
    }
}