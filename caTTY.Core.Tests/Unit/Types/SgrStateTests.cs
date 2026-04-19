using caTTY.Core.Types;
using NUnit.Framework;

namespace caTTY.Core.Tests.Unit.Types;

/// <summary>
///     Unit tests for SGR state functionality.
/// </summary>
[TestFixture]
public class SgrStateTests
{
    [Test]
    public void Constructor_WithNoParameters_CreatesDefaultState()
    {
        // Act
        var state = new SgrState();

        // Assert
        Assert.That(state.Bold, Is.False);
        Assert.That(state.Faint, Is.False);
        Assert.That(state.Italic, Is.False);
        Assert.That(state.Underline, Is.False);
        Assert.That(state.UnderlineStyle, Is.Null);
        Assert.That(state.Blink, Is.False);
        Assert.That(state.Inverse, Is.False);
        Assert.That(state.Hidden, Is.False);
        Assert.That(state.Strikethrough, Is.False);
        Assert.That(state.ForegroundColor, Is.Null);
        Assert.That(state.BackgroundColor, Is.Null);
        Assert.That(state.UnderlineColor, Is.Null);
        Assert.That(state.Font, Is.EqualTo(0));
    }

    [Test]
    public void Constructor_WithOtherState_CopiesAllProperties()
    {
        // Arrange
        var original = new SgrState
        {
            Bold = true,
            Italic = true,
            Underline = true,
            UnderlineStyle = UnderlineStyle.Double,
            Inverse = true,
            ForegroundColor = new Color(NamedColor.Red),
            BackgroundColor = new Color(NamedColor.Blue),
            Font = 2
        };

        // Act
        var copy = new SgrState(original);

        // Assert
        Assert.That(copy.Bold, Is.EqualTo(original.Bold));
        Assert.That(copy.Italic, Is.EqualTo(original.Italic));
        Assert.That(copy.Underline, Is.EqualTo(original.Underline));
        Assert.That(copy.UnderlineStyle, Is.EqualTo(original.UnderlineStyle));
        Assert.That(copy.Inverse, Is.EqualTo(original.Inverse));
        Assert.That(copy.ForegroundColor, Is.EqualTo(original.ForegroundColor));
        Assert.That(copy.BackgroundColor, Is.EqualTo(original.BackgroundColor));
        Assert.That(copy.Font, Is.EqualTo(original.Font));
    }

    [Test]
    public void Reset_WithModifiedState_ResetsToDefault()
    {
        // Arrange
        var state = new SgrState
        {
            Bold = true,
            Italic = true,
            ForegroundColor = new Color(NamedColor.Red),
            Font = 3
        };

        // Act
        state.Reset();

        // Assert
        Assert.That(state.Bold, Is.False);
        Assert.That(state.Italic, Is.False);
        Assert.That(state.ForegroundColor, Is.Null);
        Assert.That(state.Font, Is.EqualTo(0));
    }

    [Test]
    public void CreateDefault_ReturnsDefaultState()
    {
        // Act
        var state = SgrState.CreateDefault();

        // Assert
        Assert.That(state.Bold, Is.False);
        Assert.That(state.Italic, Is.False);
        Assert.That(state.ForegroundColor, Is.Null);
        Assert.That(state.Font, Is.EqualTo(0));
    }

    [Test]
    public void ToSgrAttributes_WithModifiedState_ReturnsCorrectAttributes()
    {
        // Arrange
        var state = new SgrState
        {
            Bold = true,
            Italic = true,
            Underline = true,
            UnderlineStyle = UnderlineStyle.Double,
            ForegroundColor = new Color(NamedColor.Red),
            BackgroundColor = new Color(NamedColor.Blue)
        };

        // Act
        var attributes = state.ToSgrAttributes();

        // Assert
        Assert.That(attributes.Bold, Is.True);
        Assert.That(attributes.Italic, Is.True);
        Assert.That(attributes.Underline, Is.True);
        Assert.That(attributes.UnderlineStyle, Is.EqualTo(UnderlineStyle.Double));
        Assert.That(attributes.ForegroundColor, Is.EqualTo(new Color(NamedColor.Red)));
        Assert.That(attributes.BackgroundColor, Is.EqualTo(new Color(NamedColor.Blue)));
    }

    [Test]
    public void ToSgrAttributes_WithNullUnderlineStyle_ReturnsNoneUnderlineStyle()
    {
        // Arrange
        var state = new SgrState { UnderlineStyle = null };

        // Act
        var attributes = state.ToSgrAttributes();

        // Assert
        Assert.That(attributes.UnderlineStyle, Is.EqualTo(UnderlineStyle.None));
    }

    [Test]
    public void FromSgrAttributes_WithAttributes_CreatesCorrectState()
    {
        // Arrange
        var attributes = new SgrAttributes(
            bold: true,
            italic: true,
            underline: true,
            underlineStyle: UnderlineStyle.Curly,
            foregroundColor: new Color(NamedColor.Green),
            backgroundColor: new Color(NamedColor.Yellow),
            font: 1);

        // Act
        var state = SgrState.FromSgrAttributes(attributes);

        // Assert
        Assert.That(state.Bold, Is.True);
        Assert.That(state.Italic, Is.True);
        Assert.That(state.Underline, Is.True);
        Assert.That(state.UnderlineStyle, Is.EqualTo(UnderlineStyle.Curly));
        Assert.That(state.ForegroundColor, Is.EqualTo(new Color(NamedColor.Green)));
        Assert.That(state.BackgroundColor, Is.EqualTo(new Color(NamedColor.Yellow)));
        Assert.That(state.Font, Is.EqualTo(1));
    }

    [Test]
    public void FromSgrAttributes_WithNoneUnderlineStyle_SetsNullUnderlineStyle()
    {
        // Arrange
        var attributes = new SgrAttributes(underlineStyle: UnderlineStyle.None);

        // Act
        var state = SgrState.FromSgrAttributes(attributes);

        // Assert
        Assert.That(state.UnderlineStyle, Is.Null);
    }

    [Test]
    public void Equals_WithSameValues_ReturnsTrue()
    {
        // Arrange
        var state1 = new SgrState
        {
            Bold = true,
            Italic = true,
            ForegroundColor = new Color(NamedColor.Red)
        };
        var state2 = new SgrState
        {
            Bold = true,
            Italic = true,
            ForegroundColor = new Color(NamedColor.Red)
        };

        // Act & Assert
        Assert.That(state1.Equals(state2), Is.True);
        Assert.That(state1 == state2, Is.False); // Reference equality
    }

    [Test]
    public void Equals_WithDifferentValues_ReturnsFalse()
    {
        // Arrange
        var state1 = new SgrState { Bold = true };
        var state2 = new SgrState { Bold = false };

        // Act & Assert
        Assert.That(state1.Equals(state2), Is.False);
    }

    [Test]
    public void Equals_WithNull_ReturnsFalse()
    {
        // Arrange
        var state = new SgrState();

        // Act & Assert
        Assert.That(state.Equals(null), Is.False);
    }

    [Test]
    public void Equals_WithSameReference_ReturnsTrue()
    {
        // Arrange
        var state = new SgrState();

        // Act & Assert
        Assert.That(state.Equals(state), Is.True);
    }

    [Test]
    public void GetHashCode_WithSameValues_ReturnsSameHashCode()
    {
        // Arrange
        var state1 = new SgrState { Bold = true, Italic = true };
        var state2 = new SgrState { Bold = true, Italic = true };

        // Act & Assert
        Assert.That(state1.GetHashCode(), Is.EqualTo(state2.GetHashCode()));
    }

    [Test]
    public void GetHashCode_WithDifferentValues_ReturnsDifferentHashCode()
    {
        // Arrange
        var state1 = new SgrState { Bold = true };
        var state2 = new SgrState { Bold = false };

        // Act & Assert
        Assert.That(state1.GetHashCode(), Is.Not.EqualTo(state2.GetHashCode()));
    }

    [Test]
    public void ToString_WithDefaultState_ReturnsDefault()
    {
        // Arrange
        var state = new SgrState();

        // Act
        var result = state.ToString();

        // Assert
        Assert.That(result, Is.EqualTo("Default"));
    }

    [Test]
    public void ToString_WithModifiedState_ReturnsCorrectString()
    {
        // Arrange
        var state = new SgrState
        {
            Bold = true,
            Italic = true,
            ForegroundColor = new Color(NamedColor.Red)
        };

        // Act
        var result = state.ToString();

        // Assert
        Assert.That(result, Does.Contain("Bold"));
        Assert.That(result, Does.Contain("Italic"));
        Assert.That(result, Does.Contain("FG(Named(Red))"));
    }

    [Test]
    public void Properties_CanBeSetAndRetrieved()
    {
        // Arrange
        var state = new SgrState();

        // Act
        state.Bold = true;
        state.Faint = true;
        state.Italic = true;
        state.Underline = true;
        state.UnderlineStyle = UnderlineStyle.Dashed;
        state.Blink = true;
        state.Inverse = true;
        state.Hidden = true;
        state.Strikethrough = true;
        state.ForegroundColor = new Color(NamedColor.Cyan);
        state.BackgroundColor = new Color(NamedColor.Magenta);
        state.UnderlineColor = new Color(NamedColor.Yellow);
        state.Font = 5;

        // Assert
        Assert.That(state.Bold, Is.True);
        Assert.That(state.Faint, Is.True);
        Assert.That(state.Italic, Is.True);
        Assert.That(state.Underline, Is.True);
        Assert.That(state.UnderlineStyle, Is.EqualTo(UnderlineStyle.Dashed));
        Assert.That(state.Blink, Is.True);
        Assert.That(state.Inverse, Is.True);
        Assert.That(state.Hidden, Is.True);
        Assert.That(state.Strikethrough, Is.True);
        Assert.That(state.ForegroundColor, Is.EqualTo(new Color(NamedColor.Cyan)));
        Assert.That(state.BackgroundColor, Is.EqualTo(new Color(NamedColor.Magenta)));
        Assert.That(state.UnderlineColor, Is.EqualTo(new Color(NamedColor.Yellow)));
        Assert.That(state.Font, Is.EqualTo(5));
    }
}