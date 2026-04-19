using caTTY.Core.Terminal;
using caTTY.Core.Types;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace caTTY.Core.Tests.Unit;

/// <summary>
///     Unit tests for mouse mode state tracking in terminal emulator.
///     Verifies that mouse reporting modes are correctly tracked and managed.
/// </summary>
[TestFixture]
[Category("Unit")]
public class MouseModeStateTrackingTests
{
    private TerminalEmulator _terminal = null!;
    private ILogger<TerminalEmulator> _logger = null!;

    [SetUp]
    public void SetUp()
    {
        _logger = NullLogger<TerminalEmulator>.Instance;
        _terminal = TerminalEmulator.Create(80, 24, _logger);
    }

    [TearDown]
    public void TearDown()
    {
        _terminal?.Dispose();
    }

    [Test]
    public void SetDecMode_MouseMode1000_EnablesClickTracking()
    {
        // Act
        _terminal.SetDecMode(1000, true);
        
        // Assert
        Assert.That(_terminal.State.MouseTrackingModeBits & 1, Is.EqualTo(1), "Mode 1000 bit should be set");
        Assert.That(_terminal.State.IsMouseReportingEnabled, Is.True, "Mouse reporting should be enabled");
    }

    [Test]
    public void SetDecMode_MouseMode1002_EnablesButtonTracking()
    {
        // Act
        _terminal.SetDecMode(1002, true);
        
        // Assert
        Assert.That(_terminal.State.MouseTrackingModeBits & 2, Is.EqualTo(2), "Mode 1002 bit should be set");
        Assert.That(_terminal.State.IsMouseReportingEnabled, Is.True, "Mouse reporting should be enabled");
    }

    [Test]
    public void SetDecMode_MouseMode1003_EnablesAnyEventTracking()
    {
        // Act
        _terminal.SetDecMode(1003, true);
        
        // Assert
        Assert.That(_terminal.State.MouseTrackingModeBits & 4, Is.EqualTo(4), "Mode 1003 bit should be set");
        Assert.That(_terminal.State.IsMouseReportingEnabled, Is.True, "Mouse reporting should be enabled");
    }

    [Test]
    public void SetDecMode_MouseMode1006_EnablesSgrEncoding()
    {
        // Act
        _terminal.SetDecMode(1006, true);
        
        // Assert
        Assert.That(_terminal.State.MouseSgrEncodingEnabled, Is.True, "SGR encoding should be enabled");
    }

    [Test]
    public void SetDecMode_MultipleModes_EnablesAllModes()
    {
        // Act
        _terminal.SetDecMode(1000, true);
        _terminal.SetDecMode(1002, true);
        _terminal.SetDecMode(1006, true);
        
        // Assert
        Assert.That(_terminal.State.MouseTrackingModeBits & 1, Is.EqualTo(1), "Mode 1000 bit should be set");
        Assert.That(_terminal.State.MouseTrackingModeBits & 2, Is.EqualTo(2), "Mode 1002 bit should be set");
        Assert.That(_terminal.State.MouseSgrEncodingEnabled, Is.True, "SGR encoding should be enabled");
        Assert.That(_terminal.State.IsMouseReportingEnabled, Is.True, "Mouse reporting should be enabled");
    }

    [Test]
    public void SetDecMode_DisableMode_ClearsBit()
    {
        // Arrange
        _terminal.SetDecMode(1000, true);
        _terminal.SetDecMode(1002, true);
        
        // Act
        _terminal.SetDecMode(1000, false);
        
        // Assert
        Assert.That(_terminal.State.MouseTrackingModeBits & 1, Is.EqualTo(0), "Mode 1000 bit should be cleared");
        Assert.That(_terminal.State.MouseTrackingModeBits & 2, Is.EqualTo(2), "Mode 1002 bit should remain set");
        Assert.That(_terminal.State.IsMouseReportingEnabled, Is.True, "Mouse reporting should still be enabled");
    }

    [Test]
    public void SetDecMode_DisableAllModes_DisablesMouseReporting()
    {
        // Arrange
        _terminal.SetDecMode(1000, true);
        _terminal.SetDecMode(1002, true);
        _terminal.SetDecMode(1003, true);
        
        // Act
        _terminal.SetDecMode(1000, false);
        _terminal.SetDecMode(1002, false);
        _terminal.SetDecMode(1003, false);
        
        // Assert
        Assert.That(_terminal.State.MouseTrackingModeBits, Is.EqualTo(0), "All mode bits should be cleared");
        Assert.That(_terminal.State.IsMouseReportingEnabled, Is.False, "Mouse reporting should be disabled");
    }

    [Test]
    public void SetDecMode_DisableSgrEncoding_ClearsSgrFlag()
    {
        // Arrange
        _terminal.SetDecMode(1006, true);
        
        // Act
        _terminal.SetDecMode(1006, false);
        
        // Assert
        Assert.That(_terminal.State.MouseSgrEncodingEnabled, Is.False, "SGR encoding should be disabled");
    }

    [Test]
    public void SetDecMode_InvalidMouseMode_IgnoresMode()
    {
        // Arrange
        var initialBits = _terminal.State.MouseTrackingModeBits;
        
        // Act
        _terminal.SetDecMode(1001, true); // Invalid mouse mode
        
        // Assert
        Assert.That(_terminal.State.MouseTrackingModeBits, Is.EqualTo(initialBits), "Mode bits should be unchanged");
    }

    [Test]
    public void SetDecMode_ModeStateTransitions_WorkCorrectly()
    {
        // Test: Off -> Click -> Button -> Any -> Off
        
        // Initially off
        Assert.That(_terminal.State.IsMouseReportingEnabled, Is.False);
        
        // Enable click tracking
        _terminal.SetDecMode(1000, true);
        Assert.That(_terminal.State.MouseTrackingModeBits, Is.EqualTo(1));
        Assert.That(_terminal.State.IsMouseReportingEnabled, Is.True);
        
        // Enable button tracking (both should be active)
        _terminal.SetDecMode(1002, true);
        Assert.That(_terminal.State.MouseTrackingModeBits, Is.EqualTo(3)); // 1 + 2
        Assert.That(_terminal.State.IsMouseReportingEnabled, Is.True);
        
        // Enable any event tracking (all should be active)
        _terminal.SetDecMode(1003, true);
        Assert.That(_terminal.State.MouseTrackingModeBits, Is.EqualTo(7)); // 1 + 2 + 4
        Assert.That(_terminal.State.IsMouseReportingEnabled, Is.True);
        
        // Disable all modes
        _terminal.SetDecMode(1000, false);
        _terminal.SetDecMode(1002, false);
        _terminal.SetDecMode(1003, false);
        Assert.That(_terminal.State.MouseTrackingModeBits, Is.EqualTo(0));
        Assert.That(_terminal.State.IsMouseReportingEnabled, Is.False);
    }
}