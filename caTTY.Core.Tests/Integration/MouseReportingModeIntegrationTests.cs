using caTTY.Core.Terminal;
using caTTY.Core.Types;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace caTTY.Core.Tests.Integration;

/// <summary>
///     Integration tests for mouse reporting mode sequences.
///     Tests the complete flow from CSI parsing to terminal state changes.
/// </summary>
[TestFixture]
[Category("Integration")]
public class MouseReportingModeIntegrationTests
{
    private TerminalEmulator _terminal = null!;

    [SetUp]
    public void SetUp()
    {
        _terminal = TerminalEmulator.Create(80, 24, NullLogger<TerminalEmulator>.Instance);
    }

    [TearDown]
    public void TearDown()
    {
        _terminal?.Dispose();
    }

    [Test]
    public void Write_MouseMode1000Enable_EnablesClickTracking()
    {
        // Arrange
        string sequence = "\x1b[?1000h"; // CSI ? 1000 h
        
        // Act
        _terminal.Write(sequence);
        
        // Assert
        Assert.That(_terminal.State.MouseTrackingModeBits & 1, Is.EqualTo(1), "Mode 1000 bit should be set");
        Assert.That(_terminal.State.IsMouseReportingEnabled, Is.True, "Mouse reporting should be enabled");
    }

    [Test]
    public void Write_MouseMode1002Enable_EnablesButtonTracking()
    {
        // Arrange
        string sequence = "\x1b[?1002h"; // CSI ? 1002 h
        
        // Act
        _terminal.Write(sequence);
        
        // Assert
        Assert.That(_terminal.State.MouseTrackingModeBits & 2, Is.EqualTo(2), "Mode 1002 bit should be set");
        Assert.That(_terminal.State.IsMouseReportingEnabled, Is.True, "Mouse reporting should be enabled");
    }

    [Test]
    public void Write_MouseMode1003Enable_EnablesAnyEventTracking()
    {
        // Arrange
        string sequence = "\x1b[?1003h"; // CSI ? 1003 h
        
        // Act
        _terminal.Write(sequence);
        
        // Assert
        Assert.That(_terminal.State.MouseTrackingModeBits & 4, Is.EqualTo(4), "Mode 1003 bit should be set");
        Assert.That(_terminal.State.IsMouseReportingEnabled, Is.True, "Mouse reporting should be enabled");
    }

    [Test]
    public void Write_MouseMode1006Enable_EnablesSgrEncoding()
    {
        // Arrange
        string sequence = "\x1b[?1006h"; // CSI ? 1006 h
        
        // Act
        _terminal.Write(sequence);
        
        // Assert
        Assert.That(_terminal.State.MouseSgrEncodingEnabled, Is.True, "SGR encoding should be enabled");
    }

    [Test]
    public void Write_MultipleMouseModesEnable_EnablesAllModes()
    {
        // Arrange
        string sequence = "\x1b[?1000;1002;1006h"; // CSI ? 1000;1002;1006 h
        
        // Act
        _terminal.Write(sequence);
        
        // Assert
        Assert.That(_terminal.State.MouseTrackingModeBits & 1, Is.EqualTo(1), "Mode 1000 bit should be set");
        Assert.That(_terminal.State.MouseTrackingModeBits & 2, Is.EqualTo(2), "Mode 1002 bit should be set");
        Assert.That(_terminal.State.MouseSgrEncodingEnabled, Is.True, "SGR encoding should be enabled");
        Assert.That(_terminal.State.IsMouseReportingEnabled, Is.True, "Mouse reporting should be enabled");
    }

    [Test]
    public void Write_MouseModeDisable_DisablesMode()
    {
        // Arrange - Enable mode first
        _terminal.Write("\x1b[?1000h");
        Assert.That(_terminal.State.IsMouseReportingEnabled, Is.True, "Precondition: mouse reporting should be enabled");
        
        // Act - Disable mode
        _terminal.Write("\x1b[?1000l"); // CSI ? 1000 l
        
        // Assert
        Assert.That(_terminal.State.MouseTrackingModeBits & 1, Is.EqualTo(0), "Mode 1000 bit should be cleared");
        Assert.That(_terminal.State.IsMouseReportingEnabled, Is.False, "Mouse reporting should be disabled");
    }

    [Test]
    public void Write_PartialMouseModeDisable_KeepsOtherModes()
    {
        // Arrange - Enable multiple modes
        _terminal.Write("\x1b[?1000;1002h");
        Assert.That(_terminal.State.MouseTrackingModeBits, Is.EqualTo(3), "Precondition: both modes should be enabled");
        
        // Act - Disable only one mode
        _terminal.Write("\x1b[?1000l"); // CSI ? 1000 l
        
        // Assert
        Assert.That(_terminal.State.MouseTrackingModeBits & 1, Is.EqualTo(0), "Mode 1000 bit should be cleared");
        Assert.That(_terminal.State.MouseTrackingModeBits & 2, Is.EqualTo(2), "Mode 1002 bit should remain set");
        Assert.That(_terminal.State.IsMouseReportingEnabled, Is.True, "Mouse reporting should still be enabled");
    }

    [Test]
    public void Write_SgrEncodingDisable_DisablesSgrEncoding()
    {
        // Arrange - Enable SGR encoding first
        _terminal.Write("\x1b[?1006h");
        Assert.That(_terminal.State.MouseSgrEncodingEnabled, Is.True, "Precondition: SGR encoding should be enabled");
        
        // Act - Disable SGR encoding
        _terminal.Write("\x1b[?1006l"); // CSI ? 1006 l
        
        // Assert
        Assert.That(_terminal.State.MouseSgrEncodingEnabled, Is.False, "SGR encoding should be disabled");
    }

    [Test]
    public void Write_ComplexMouseModeSequence_HandlesCorrectly()
    {
        // Test a complex sequence that enables and disables various modes
        
        // Initially off
        Assert.That(_terminal.State.IsMouseReportingEnabled, Is.False);
        Assert.That(_terminal.State.MouseSgrEncodingEnabled, Is.False);
        
        // Enable click tracking and SGR encoding
        _terminal.Write("\x1b[?1000;1006h");
        Assert.That(_terminal.State.MouseTrackingModeBits & 1, Is.EqualTo(1));
        Assert.That(_terminal.State.MouseSgrEncodingEnabled, Is.True);
        
        // Add button tracking
        _terminal.Write("\x1b[?1002h");
        Assert.That(_terminal.State.MouseTrackingModeBits & 2, Is.EqualTo(2));
        Assert.That(_terminal.State.MouseTrackingModeBits & 1, Is.EqualTo(1)); // Should still be set
        
        // Add any event tracking
        _terminal.Write("\x1b[?1003h");
        Assert.That(_terminal.State.MouseTrackingModeBits & 4, Is.EqualTo(4));
        Assert.That(_terminal.State.MouseTrackingModeBits & 3, Is.EqualTo(3)); // 1000 and 1002 should still be set
        
        // Disable click tracking only
        _terminal.Write("\x1b[?1000l");
        Assert.That(_terminal.State.MouseTrackingModeBits & 1, Is.EqualTo(0));
        Assert.That(_terminal.State.MouseTrackingModeBits & 6, Is.EqualTo(6)); // 1002 and 1003 should remain
        Assert.That(_terminal.State.IsMouseReportingEnabled, Is.True); // Still enabled due to other modes
        
        // Disable all remaining tracking modes
        _terminal.Write("\x1b[?1002;1003l");
        Assert.That(_terminal.State.MouseTrackingModeBits, Is.EqualTo(0));
        Assert.That(_terminal.State.IsMouseReportingEnabled, Is.False);
        Assert.That(_terminal.State.MouseSgrEncodingEnabled, Is.True); // SGR encoding should still be enabled
        
        // Disable SGR encoding
        _terminal.Write("\x1b[?1006l");
        Assert.That(_terminal.State.MouseSgrEncodingEnabled, Is.False);
    }

    [Test]
    public void Write_InvalidMouseMode_IgnoresMode()
    {
        // Arrange
        var initialBits = _terminal.State.MouseTrackingModeBits;
        var initialSgr = _terminal.State.MouseSgrEncodingEnabled;
        
        // Act - Try to set an invalid mouse mode
        _terminal.Write("\x1b[?1001h"); // Invalid mouse mode
        
        // Assert - State should be unchanged
        Assert.That(_terminal.State.MouseTrackingModeBits, Is.EqualTo(initialBits));
        Assert.That(_terminal.State.MouseSgrEncodingEnabled, Is.EqualTo(initialSgr));
    }

    [Test]
    public void Write_MouseModeWithOtherDecModes_HandlesCorrectly()
    {
        // Test mouse modes mixed with other DEC modes
        
        // Act - Enable mouse mode with cursor visibility and alternate screen
        _terminal.Write("\x1b[?25;1000;47h"); // Cursor visible + mouse click + alternate screen
        
        // Assert
        Assert.That(_terminal.State.CursorVisible, Is.True, "Cursor should be visible");
        Assert.That(_terminal.State.MouseTrackingModeBits & 1, Is.EqualTo(1), "Mouse click mode should be enabled");
        Assert.That(_terminal.State.IsAlternateScreenActive, Is.True, "Alternate screen should be active");
    }

    [Test]
    public void Write_MouseModeSequenceFragmented_HandlesCorrectly()
    {
        // Test that mouse mode sequences work even when fragmented across multiple Write calls
        
        // Act - Send sequence in fragments
        _terminal.Write("\x1b[?10");
        _terminal.Write("00;10");
        _terminal.Write("06h");
        
        // Assert
        Assert.That(_terminal.State.MouseTrackingModeBits & 1, Is.EqualTo(1), "Mode 1000 should be enabled");
        Assert.That(_terminal.State.MouseSgrEncodingEnabled, Is.True, "SGR encoding should be enabled");
    }
}