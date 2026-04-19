using caTTY.Core.Parsing;
using NUnit.Framework;

namespace caTTY.Core.Tests.Unit;

/// <summary>
///     Unit tests for mouse mode parsing in CSI sequences.
///     Verifies that mouse reporting mode sequences are correctly parsed.
/// </summary>
[TestFixture]
[Category("Unit")]
public class MouseModeParsingTests
{
    private CsiParser _parser = null!;

    [SetUp]
    public void SetUp()
    {
        _parser = new CsiParser();
    }

    [Test]
    public void ParseCsiSequence_MouseMode1000Enable_ParsesCorrectly()
    {
        // Arrange
        var sequence = System.Text.Encoding.UTF8.GetBytes("\x1b[?1000h");
        
        // Act
        var message = _parser.ParseCsiSequence(sequence, "CSI ? 1000 h");
        
        // Assert
        Assert.That(message.Type, Is.EqualTo("csi.decModeSet"));
        Assert.That(message.DecModes, Is.Not.Null);
        Assert.That(message.DecModes, Contains.Item(1000));
        Assert.That(message.Implemented, Is.True);
    }

    [Test]
    public void ParseCsiSequence_MouseMode1002Enable_ParsesCorrectly()
    {
        // Arrange
        var sequence = System.Text.Encoding.UTF8.GetBytes("\x1b[?1002h");
        
        // Act
        var message = _parser.ParseCsiSequence(sequence, "CSI ? 1002 h");
        
        // Assert
        Assert.That(message.Type, Is.EqualTo("csi.decModeSet"));
        Assert.That(message.DecModes, Is.Not.Null);
        Assert.That(message.DecModes, Contains.Item(1002));
        Assert.That(message.Implemented, Is.True);
    }

    [Test]
    public void ParseCsiSequence_MouseMode1003Enable_ParsesCorrectly()
    {
        // Arrange
        var sequence = System.Text.Encoding.UTF8.GetBytes("\x1b[?1003h");
        
        // Act
        var message = _parser.ParseCsiSequence(sequence, "CSI ? 1003 h");
        
        // Assert
        Assert.That(message.Type, Is.EqualTo("csi.decModeSet"));
        Assert.That(message.DecModes, Is.Not.Null);
        Assert.That(message.DecModes, Contains.Item(1003));
        Assert.That(message.Implemented, Is.True);
    }

    [Test]
    public void ParseCsiSequence_MouseMode1006Enable_ParsesCorrectly()
    {
        // Arrange
        var sequence = System.Text.Encoding.UTF8.GetBytes("\x1b[?1006h");
        
        // Act
        var message = _parser.ParseCsiSequence(sequence, "CSI ? 1006 h");
        
        // Assert
        Assert.That(message.Type, Is.EqualTo("csi.decModeSet"));
        Assert.That(message.DecModes, Is.Not.Null);
        Assert.That(message.DecModes, Contains.Item(1006));
        Assert.That(message.Implemented, Is.True);
    }

    [Test]
    public void ParseCsiSequence_MultipleMouseModes_ParsesCorrectly()
    {
        // Arrange
        var sequence = System.Text.Encoding.UTF8.GetBytes("\x1b[?1000;1006h");
        
        // Act
        var message = _parser.ParseCsiSequence(sequence, "CSI ? 1000;1006 h");
        
        // Assert
        Assert.That(message.Type, Is.EqualTo("csi.decModeSet"));
        Assert.That(message.DecModes, Is.Not.Null);
        Assert.That(message.DecModes, Contains.Item(1000));
        Assert.That(message.DecModes, Contains.Item(1006));
        Assert.That(message.Implemented, Is.True);
    }

    [Test]
    public void ParseCsiSequence_MouseModeDisable_ParsesCorrectly()
    {
        // Arrange
        var sequence = System.Text.Encoding.UTF8.GetBytes("\x1b[?1000l");
        
        // Act
        var message = _parser.ParseCsiSequence(sequence, "CSI ? 1000 l");
        
        // Assert
        Assert.That(message.Type, Is.EqualTo("csi.decModeReset"));
        Assert.That(message.DecModes, Is.Not.Null);
        Assert.That(message.DecModes, Contains.Item(1000));
        Assert.That(message.Implemented, Is.True);
    }

    [Test]
    public void ParseCsiSequence_AllMouseModes_ParsesCorrectly()
    {
        // Arrange
        var sequence = System.Text.Encoding.UTF8.GetBytes("\x1b[?1000;1002;1003;1006h");
        
        // Act
        var message = _parser.ParseCsiSequence(sequence, "CSI ? 1000;1002;1003;1006 h");
        
        // Assert
        Assert.That(message.Type, Is.EqualTo("csi.decModeSet"));
        Assert.That(message.DecModes, Is.Not.Null);
        Assert.That(message.DecModes, Contains.Item(1000));
        Assert.That(message.DecModes, Contains.Item(1002));
        Assert.That(message.DecModes, Contains.Item(1003));
        Assert.That(message.DecModes, Contains.Item(1006));
        Assert.That(message.Implemented, Is.True);
    }
}