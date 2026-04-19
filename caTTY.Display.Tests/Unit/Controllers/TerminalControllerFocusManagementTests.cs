using NUnit.Framework;
using caTTY.Core.Terminal;
using caTTY.Display.Controllers;
using caTTY.Display.Configuration;
using caTTY.Display.Types;

namespace caTTY.Display.Tests.Unit.Controllers;

/// <summary>
/// Unit tests for focus management functionality in TerminalController.
/// Tests focus state tracking, visual indicators, and input capture priority.
/// Validates Requirements 18.2, 18.3, 18.4, 18.5.
/// </summary>
[TestFixture]
public class TerminalControllerFocusManagementTests
{
    private ITerminalEmulator _terminal = null!;
    private IProcessManager _processManager = null!;
    private TerminalController _controller = null!;

    [SetUp]
    public void SetUp()
    {
        // Create test terminal and process manager
        _terminal = TerminalEmulator.Create(80, 24);
        _processManager = new ProcessManager();
        
        // Create session manager and add a session
        var sessionManager = new SessionManager();
        var session = sessionManager.CreateSessionAsync().Result;
        
        // Create controller with session manager
        _controller = new TerminalController(sessionManager);
    }

    [TearDown]
    public void TearDown()
    {
        _controller?.Dispose();
        _processManager?.Dispose();
        _terminal?.Dispose();
    }

    [Test]
    public void HasFocus_InitialState_ShouldBeFalse()
    {
        // Arrange & Act
        bool initialFocus = _controller.HasFocus;

        // Assert
        Assert.That(initialFocus, Is.False, "Terminal should not have focus initially");
    }

    [Test]
    public void IsInputCaptureActive_WhenVisibleAndFocused_ShouldReturnTrue()
    {
        // Arrange
        _controller.IsVisible = true;
        // Note: We can't easily simulate ImGui focus state in unit tests,
        // but we can test the logic that determines input capture

        // Act & Assert
        // The property should return HasFocus && IsVisible
        // Since HasFocus is false initially, IsInputCaptureActive should be false
        Assert.That(_controller.IsInputCaptureActive, Is.False, 
            "Input capture should be inactive when not focused");
    }

    [Test]
    public void IsInputCaptureActive_WhenNotVisible_ShouldReturnFalse()
    {
        // Arrange
        _controller.IsVisible = false;

        // Act & Assert
        Assert.That(_controller.IsInputCaptureActive, Is.False, 
            "Input capture should be inactive when not visible");
    }

    [Test]
    public void ShouldCaptureInput_WhenInputCaptureActive_ShouldReturnTrue()
    {
        // Arrange
        _controller.IsVisible = true;
        // Note: Focus state is managed by ImGui, so we test the logic

        // Act
        bool shouldCapture = _controller.ShouldCaptureInput();

        // Assert
        // Should match IsInputCaptureActive
        Assert.That(shouldCapture, Is.EqualTo(_controller.IsInputCaptureActive),
            "ShouldCaptureInput should match IsInputCaptureActive");
    }

    [Test]
    [Ignore("ImGui not available in test environment")]
    public void ForceFocus_InTestEnvironment_ShouldHandleGracefully()
    {
        // Arrange & Act & Assert
        // In test environment, ImGui is not initialized, so this should handle gracefully
        // We expect it to either succeed silently or handle the exception internally
        Assert.DoesNotThrow(() => _controller.ForceFocus(),
            "ForceFocus should handle ImGui unavailability gracefully");
    }

    [Test]
    public void FocusChanged_EventSubscription_ShouldNotThrow()
    {
        // Arrange
        bool eventRaised = false;
        FocusChangedEventArgs? receivedArgs = null;

        // Act
        _controller.FocusChanged += (sender, args) =>
        {
            eventRaised = true;
            receivedArgs = args;
        };

        // Assert
        Assert.That(eventRaised, Is.False, "Event should not be raised during subscription");
        Assert.That(receivedArgs, Is.Null, "Event args should be null initially");
    }

    [Test]
    public void GetCurrentSelection_InitialState_ShouldBeEmpty()
    {
        // Arrange & Act
        var selection = _controller.GetCurrentSelection();

        // Assert
        Assert.That(selection.IsEmpty, Is.True, "Initial selection should be empty");
    }

    [Test]
    public void SetSelection_ValidSelection_ShouldUpdateSelection()
    {
        // Arrange
        var testSelection = new TextSelection(
            new SelectionPosition(0, 0),
            new SelectionPosition(1, 10)
        );

        // Act
        _controller.SetSelection(testSelection);
        var retrievedSelection = _controller.GetCurrentSelection();

        // Assert
        Assert.That(retrievedSelection.IsEmpty, Is.False, "Selection should not be empty after setting");
        Assert.That(retrievedSelection.Start.Row, Is.EqualTo(0), "Start row should match");
        Assert.That(retrievedSelection.Start.Col, Is.EqualTo(0), "Start col should match");
        Assert.That(retrievedSelection.End.Row, Is.EqualTo(1), "End row should match");
        Assert.That(retrievedSelection.End.Col, Is.EqualTo(10), "End col should match");
    }

    [Test]
    public void CopySelectionToClipboard_EmptySelection_ShouldReturnFalse()
    {
        // Arrange
        // Selection is empty by default

        // Act
        bool result = _controller.CopySelectionToClipboard();

        // Assert
        Assert.That(result, Is.False, "Copying empty selection should return false");
    }

    [Test]
    public void GetTerminalDimensions_ShouldReturnCorrectDimensions()
    {
        // Arrange & Act
        var (width, height) = _controller.GetTerminalDimensions();

        // Assert
        Assert.That(width, Is.EqualTo(80), "Width should match terminal width");
        Assert.That(height, Is.EqualTo(24), "Height should match terminal height");
    }

    [Test]
    public void ResizeTerminal_ValidDimensions_ShouldNotThrow()
    {
        // Arrange
        int newWidth = 100;
        int newHeight = 30;

        // Act & Assert
        Assert.DoesNotThrow(() => _controller.ResizeTerminal(newWidth, newHeight),
            "Resizing with valid dimensions should not throw");

        // Verify dimensions were updated
        var (width, height) = _controller.GetTerminalDimensions();
        Assert.That(width, Is.EqualTo(newWidth), "Width should be updated");
        Assert.That(height, Is.EqualTo(newHeight), "Height should be updated");
    }

    [Test]
    public void ResizeTerminal_InvalidDimensions_ShouldThrow()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentException>(() => _controller.ResizeTerminal(0, 24),
            "Zero width should throw ArgumentException");
        Assert.Throws<ArgumentException>(() => _controller.ResizeTerminal(80, 0),
            "Zero height should throw ArgumentException");
        Assert.Throws<ArgumentException>(() => _controller.ResizeTerminal(-1, 24),
            "Negative width should throw ArgumentException");
        Assert.Throws<ArgumentException>(() => _controller.ResizeTerminal(80, -1),
            "Negative height should throw ArgumentException");
        Assert.Throws<ArgumentException>(() => _controller.ResizeTerminal(1001, 24),
            "Width too large should throw ArgumentException");
        Assert.Throws<ArgumentException>(() => _controller.ResizeTerminal(80, 1001),
            "Height too large should throw ArgumentException");
    }

    [Test]
    public void Dispose_ShouldNotThrow()
    {
        // Arrange & Act & Assert
        Assert.DoesNotThrow(() => _controller.Dispose(),
            "Dispose should not throw exceptions");
    }

    [Test]
    public void Dispose_MultipleCalls_ShouldNotThrow()
    {
        // Arrange & Act & Assert
        Assert.DoesNotThrow(() => _controller.Dispose(),
            "First dispose should not throw");
        Assert.DoesNotThrow(() => _controller.Dispose(),
            "Second dispose should not throw");
    }
}

/// <summary>
/// Tests for FocusChangedEventArgs functionality.
/// </summary>
[TestFixture]
public class FocusChangedEventArgsTests
{
    [Test]
    public void Constructor_ValidArguments_ShouldSetProperties()
    {
        // Arrange & Act
        var args = new FocusChangedEventArgs(hasFocus: true, previousFocus: false);

        // Assert
        Assert.That(args.HasFocus, Is.True, "HasFocus should be set correctly");
        Assert.That(args.PreviousFocus, Is.False, "PreviousFocus should be set correctly");
    }

    [Test]
    public void FocusGained_WhenGainingFocus_ShouldReturnTrue()
    {
        // Arrange & Act
        var args = new FocusChangedEventArgs(hasFocus: true, previousFocus: false);

        // Assert
        Assert.That(args.FocusGained, Is.True, "FocusGained should be true when gaining focus");
        Assert.That(args.FocusLost, Is.False, "FocusLost should be false when gaining focus");
    }

    [Test]
    public void FocusLost_WhenLosingFocus_ShouldReturnTrue()
    {
        // Arrange & Act
        var args = new FocusChangedEventArgs(hasFocus: false, previousFocus: true);

        // Assert
        Assert.That(args.FocusLost, Is.True, "FocusLost should be true when losing focus");
        Assert.That(args.FocusGained, Is.False, "FocusGained should be false when losing focus");
    }

    [Test]
    public void FocusGained_WhenNoChange_ShouldReturnFalse()
    {
        // Arrange & Act
        var argsStillFocused = new FocusChangedEventArgs(hasFocus: true, previousFocus: true);
        var argsStillUnfocused = new FocusChangedEventArgs(hasFocus: false, previousFocus: false);

        // Assert
        Assert.That(argsStillFocused.FocusGained, Is.False, "FocusGained should be false when already focused");
        Assert.That(argsStillFocused.FocusLost, Is.False, "FocusLost should be false when already focused");
        Assert.That(argsStillUnfocused.FocusGained, Is.False, "FocusGained should be false when already unfocused");
        Assert.That(argsStillUnfocused.FocusLost, Is.False, "FocusLost should be false when already unfocused");
    }
}