using caTTY.Core.Terminal;
using caTTY.Core.Types;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using System.Text;

namespace caTTY.Core.Tests.Unit.Terminal;

/// <summary>
///     Tests for window manipulation functionality in TerminalEmulator.
///     Covers title stack operations, size queries, and graceful handling of unsupported operations.
/// </summary>
[TestFixture]
[Category("Unit")]
public class WindowManipulationTests
{
    [SetUp]
    public void SetUp()
    {
        _terminal = TerminalEmulator.Create(80, 24, 1000, NullLogger<TerminalEmulator>.Instance);
        
        // Set initial window title and icon name for testing
        _terminal.SetWindowTitle("Initial Title");
        _terminal.SetIconName("Initial Icon");
    }

    [TearDown]
    public void TearDown()
    {
        _terminal?.Dispose();
    }

    private TerminalEmulator _terminal = null!;

    [Test]
    public void HandleWindowManipulation_PushIconNameToStack_AddsToStack()
    {
        // Arrange
        string initialIconName = _terminal.GetIconName();
        Assert.That(_terminal.State.IconNameStack, Is.Empty, "Icon name stack should start empty");

        // Act
        _terminal.Write("\x1b[22;1t"); // CSI 22;1t - Push icon name to stack

        // Assert
        Assert.That(_terminal.State.IconNameStack, Has.Count.EqualTo(1), "Should have one item in icon name stack");
        Assert.That(_terminal.State.IconNameStack[0], Is.EqualTo(initialIconName), "Should push current icon name to stack");
    }

    [Test]
    public void HandleWindowManipulation_PushWindowTitleToStack_AddsToStack()
    {
        // Arrange
        string initialTitle = _terminal.GetWindowTitle();
        Assert.That(_terminal.State.TitleStack, Is.Empty, "Title stack should start empty");

        // Act
        _terminal.Write("\x1b[22;2t"); // CSI 22;2t - Push window title to stack

        // Assert
        Assert.That(_terminal.State.TitleStack, Has.Count.EqualTo(1), "Should have one item in title stack");
        Assert.That(_terminal.State.TitleStack[0], Is.EqualTo(initialTitle), "Should push current title to stack");
    }

    [Test]
    public void HandleWindowManipulation_PopIconNameFromStack_RestoresIconName()
    {
        // Arrange - Push current icon name, then change it
        _terminal.Write("\x1b[22;1t"); // CSI 22;1t - Push current icon name
        _terminal.SetIconName("Changed Icon");
        Assert.That(_terminal.GetIconName(), Is.EqualTo("Changed Icon"), "Icon name should be changed");

        // Act - Pop icon name from stack
        _terminal.Write("\x1b[23;1t"); // CSI 23;1t - Pop icon name from stack

        // Assert
        Assert.That(_terminal.GetIconName(), Is.EqualTo("Initial Icon"), "Should restore original icon name");
        Assert.That(_terminal.State.IconNameStack, Is.Empty, "Stack should be empty after pop");
    }

    [Test]
    public void HandleWindowManipulation_PopWindowTitleFromStack_RestoresTitle()
    {
        // Arrange - Push current title, then change it
        _terminal.Write("\x1b[22;2t"); // CSI 22;2t - Push current title
        _terminal.SetWindowTitle("Changed Title");
        Assert.That(_terminal.GetWindowTitle(), Is.EqualTo("Changed Title"), "Title should be changed");

        // Act - Pop title from stack
        _terminal.Write("\x1b[23;2t"); // CSI 23;2t - Pop title from stack

        // Assert
        Assert.That(_terminal.GetWindowTitle(), Is.EqualTo("Initial Title"), "Should restore original title");
        Assert.That(_terminal.State.TitleStack, Is.Empty, "Stack should be empty after pop");
    }

    [Test]
    public void HandleWindowManipulation_PopFromEmptyIconStack_DoesNothing()
    {
        // Arrange
        string currentIconName = _terminal.GetIconName();
        Assert.That(_terminal.State.IconNameStack, Is.Empty, "Icon name stack should be empty");

        // Act - Try to pop from empty stack
        _terminal.Write("\x1b[23;1t"); // CSI 23;1t - Pop icon name from stack

        // Assert
        Assert.That(_terminal.GetIconName(), Is.EqualTo(currentIconName), "Icon name should remain unchanged");
        Assert.That(_terminal.State.IconNameStack, Is.Empty, "Stack should remain empty");
    }

    [Test]
    public void HandleWindowManipulation_PopFromEmptyTitleStack_DoesNothing()
    {
        // Arrange
        string currentTitle = _terminal.GetWindowTitle();
        Assert.That(_terminal.State.TitleStack, Is.Empty, "Title stack should be empty");

        // Act - Try to pop from empty stack
        _terminal.Write("\x1b[23;2t"); // CSI 23;2t - Pop title from stack

        // Assert
        Assert.That(_terminal.GetWindowTitle(), Is.EqualTo(currentTitle), "Title should remain unchanged");
        Assert.That(_terminal.State.TitleStack, Is.Empty, "Stack should remain empty");
    }

    [Test]
    public void HandleWindowManipulation_MultipleStackOperations_WorksCorrectly()
    {
        // Arrange - Set up multiple titles/icons
        _terminal.SetWindowTitle("Title 1");
        _terminal.SetIconName("Icon 1");

        // Act & Assert - Push first set
        _terminal.Write("\x1b[22;2t"); // CSI 22;2t - Push title
        _terminal.Write("\x1b[22;1t"); // CSI 22;1t - Push icon
        Assert.That(_terminal.State.TitleStack, Has.Count.EqualTo(1));
        Assert.That(_terminal.State.IconNameStack, Has.Count.EqualTo(1));

        // Change titles/icons and push again
        _terminal.SetWindowTitle("Title 2");
        _terminal.SetIconName("Icon 2");
        _terminal.Write("\x1b[22;2t"); // CSI 22;2t - Push title
        _terminal.Write("\x1b[22;1t"); // CSI 22;1t - Push icon
        Assert.That(_terminal.State.TitleStack, Has.Count.EqualTo(2));
        Assert.That(_terminal.State.IconNameStack, Has.Count.EqualTo(2));

        // Change again
        _terminal.SetWindowTitle("Title 3");
        _terminal.SetIconName("Icon 3");

        // Pop in reverse order (LIFO)
        _terminal.Write("\x1b[23;1t"); // CSI 23;1t - Pop icon
        Assert.That(_terminal.GetIconName(), Is.EqualTo("Icon 2"));
        Assert.That(_terminal.State.IconNameStack, Has.Count.EqualTo(1));

        _terminal.Write("\x1b[23;2t"); // CSI 23;2t - Pop title
        Assert.That(_terminal.GetWindowTitle(), Is.EqualTo("Title 2"));
        Assert.That(_terminal.State.TitleStack, Has.Count.EqualTo(1));

        _terminal.Write("\x1b[23;1t"); // CSI 23;1t - Pop icon
        Assert.That(_terminal.GetIconName(), Is.EqualTo("Icon 1"));
        Assert.That(_terminal.State.IconNameStack, Is.Empty);

        _terminal.Write("\x1b[23;2t"); // CSI 23;2t - Pop title
        Assert.That(_terminal.GetWindowTitle(), Is.EqualTo("Title 1"));
        Assert.That(_terminal.State.TitleStack, Is.Empty);
    }

    [Test]
    public void HandleWindowManipulation_TerminalSizeQuery_EmitsResponse()
    {
        // Arrange
        string? emittedResponse = null;
        _terminal.ResponseEmitted += (sender, args) => emittedResponse = Encoding.UTF8.GetString(args.ResponseData.Span);

        // Act
        _terminal.Write("\x1b[18t"); // CSI 18t - Query terminal size

        // Assert
        Assert.That(emittedResponse, Is.Not.Null, "Should emit a response");
        Assert.That(emittedResponse, Is.EqualTo($"\x1b[8;{_terminal.Height};{_terminal.Width}t"),
            "Should emit correct terminal size response");
    }

    [Test]
    public void HandleWindowManipulation_UnsupportedOperations_GracefullyIgnored()
    {
        // Arrange
        string initialTitle = _terminal.GetWindowTitle();
        string initialIcon = _terminal.GetIconName();
        string? emittedResponse = null;
        _terminal.ResponseEmitted += (sender, args) => emittedResponse = Encoding.UTF8.GetString(args.ResponseData.Span);

        // Act - Test various unsupported operations
        _terminal.Write("\x1b[1t"); // CSI 1t - Restore window
        _terminal.Write("\x1b[2t"); // CSI 2t - Minimize window
        _terminal.Write("\x1b[8;24;80t"); // CSI 8;24;80t - Resize window
        _terminal.Write("\x1b[99;1;2;3t"); // CSI 99;1;2;3t - Unknown operation

        // Assert - Nothing should change
        Assert.That(_terminal.GetWindowTitle(), Is.EqualTo(initialTitle), "Title should remain unchanged");
        Assert.That(_terminal.GetIconName(), Is.EqualTo(initialIcon), "Icon should remain unchanged");
        Assert.That(_terminal.State.TitleStack, Is.Empty, "Title stack should remain empty");
        Assert.That(_terminal.State.IconNameStack, Is.Empty, "Icon stack should remain empty");
        Assert.That(emittedResponse, Is.Null, "Should not emit any responses");
    }

    [Test]
    public void HandleWindowManipulation_InvalidSubOperations_GracefullyIgnored()
    {
        // Arrange
        string initialTitle = _terminal.GetWindowTitle();
        string initialIcon = _terminal.GetIconName();

        // Act - Test invalid sub-operations for title stack
        _terminal.Write("\x1b[22;0t"); // CSI 22;0t - Invalid push sub-operation
        _terminal.Write("\x1b[22;3t"); // CSI 22;3t - Invalid push sub-operation
        _terminal.Write("\x1b[23;0t"); // CSI 23;0t - Invalid pop sub-operation
        _terminal.Write("\x1b[23;3t"); // CSI 23;3t - Invalid pop sub-operation

        // Assert - Nothing should change
        Assert.That(_terminal.GetWindowTitle(), Is.EqualTo(initialTitle), "Title should remain unchanged");
        Assert.That(_terminal.GetIconName(), Is.EqualTo(initialIcon), "Icon should remain unchanged");
        Assert.That(_terminal.State.TitleStack, Is.Empty, "Title stack should remain empty");
        Assert.That(_terminal.State.IconNameStack, Is.Empty, "Icon stack should remain empty");
    }

    [Test]
    public void HandleWindowManipulation_OperationsWithoutParameters_GracefullyIgnored()
    {
        // Arrange
        string initialTitle = _terminal.GetWindowTitle();
        string initialIcon = _terminal.GetIconName();

        // Act - Test operations that require parameters but don't have them
        _terminal.Write("\x1b[22t"); // CSI 22t - Push without sub-operation
        _terminal.Write("\x1b[23t"); // CSI 23t - Pop without sub-operation

        // Assert - Nothing should change
        Assert.That(_terminal.GetWindowTitle(), Is.EqualTo(initialTitle), "Title should remain unchanged");
        Assert.That(_terminal.GetIconName(), Is.EqualTo(initialIcon), "Icon should remain unchanged");
        Assert.That(_terminal.State.TitleStack, Is.Empty, "Title stack should remain empty");
        Assert.That(_terminal.State.IconNameStack, Is.Empty, "Icon stack should remain empty");
    }

    [Test]
    public void HandleWindowManipulation_IntegrationWithCsiSequence_WorksCorrectly()
    {
        // Arrange
        string? emittedResponse = null;
        _terminal.ResponseEmitted += (sender, args) => emittedResponse = Encoding.UTF8.GetString(args.ResponseData.Span);

        // Act - Send CSI window manipulation sequences through the terminal
        _terminal.Write("\x1b[22;2t"); // Push title to stack
        _terminal.SetWindowTitle("New Title");
        _terminal.Write("\x1b[23;2t"); // Pop title from stack
        _terminal.Write("\x1b[18t"); // Query terminal size

        // Assert
        Assert.That(_terminal.GetWindowTitle(), Is.EqualTo("Initial Title"), "Should restore original title");
        Assert.That(emittedResponse, Is.Not.Null, "Should emit terminal size response");
        Assert.That(emittedResponse, Is.EqualTo($"\x1b[8;{_terminal.Height};{_terminal.Width}t"));
    }
}