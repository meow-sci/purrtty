using NUnit.Framework;
using caTTY.Core.Terminal;
using Microsoft.Extensions.Logging.Abstractions;
using System;

namespace caTTY.Core.Tests.Unit.Terminal;

/// <summary>
///     Tests for TerminalSession title change functionality.
///     Validates that session titles update when terminal emits title change events.
/// </summary>
[TestFixture]
[Category("Unit")]
public class TerminalSessionTitleTests
{
    private TerminalEmulator _terminal = null!;
    private caTTY.Core.Tests.Property.MockProcessManager _processManager = null!;
    private TerminalSession _session = null!;
    private string? _lastTitleChange;

    [SetUp]
    public void SetUp()
    {
        _terminal = TerminalEmulator.Create(80, 24, NullLogger.Instance);
        _processManager = new caTTY.Core.Tests.Property.MockProcessManager();
        _session = new TerminalSession(Guid.NewGuid(), "Initial Title", _terminal, _processManager);
        
        // Subscribe to session title change events
        _session.TitleChanged += (sender, args) => _lastTitleChange = args.NewTitle;
    }

    [TearDown]
    public void TearDown()
    {
        _session?.Dispose();
        _terminal?.Dispose();
        _processManager?.Dispose();
    }

    [Test]
    public void Session_InitialTitle_IsSetCorrectly()
    {
        // Verify initial title is set
        Assert.That(_session.Title, Is.EqualTo("Initial Title"));
        Assert.That(_session.Settings.Title, Is.EqualTo("Initial Title"));
    }

    [Test]
    public void Session_TitleChangesWhenTerminalEmitsTitleChangeEvent()
    {
        // Arrange - Send OSC sequence to change terminal title
        string newTitle = "htop";
        string oscSequence = $"\x1b]0;{newTitle}\x07";

        // Act - Write OSC sequence to terminal (simulates what htop would send)
        _terminal.Write(oscSequence);

        // Assert - Session title should be updated
        Assert.That(_session.Title, Is.EqualTo(newTitle), "Session title should update when terminal emits title change");
        Assert.That(_session.Settings.Title, Is.EqualTo(newTitle), "Session settings title should also update");
        Assert.That(_lastTitleChange, Is.EqualTo(newTitle), "Title change event should be emitted");
    }

    [Test]
    public void Session_TitleChangesWithOSC2Sequence()
    {
        // Arrange - OSC 2 sets window title only
        string newTitle = "vim ~/.bashrc";
        string oscSequence = $"\x1b]2;{newTitle}\x07";

        // Act
        _terminal.Write(oscSequence);

        // Assert
        Assert.That(_session.Title, Is.EqualTo(newTitle));
        Assert.That(_lastTitleChange, Is.EqualTo(newTitle));
    }

    [Test]
    public void Session_TitleChangesWithSTTerminator()
    {
        // Arrange - OSC with ST terminator (ESC \)
        string newTitle = "nano file.txt";
        string oscSequence = $"\x1b]0;{newTitle}\x1b\\";

        // Act
        _terminal.Write(oscSequence);

        // Assert
        Assert.That(_session.Title, Is.EqualTo(newTitle));
        Assert.That(_lastTitleChange, Is.EqualTo(newTitle));
    }

    [Test]
    public void Session_EmptyTitleHandledCorrectly()
    {
        // Arrange - Empty title OSC sequence
        string oscSequence = "\x1b]0;\x07";

        // Act
        _terminal.Write(oscSequence);

        // Assert
        Assert.That(_session.Title, Is.EqualTo(string.Empty));
        Assert.That(_lastTitleChange, Is.EqualTo(string.Empty));
    }

    [Test]
    public void Session_SpecialCharactersInTitlePreserved()
    {
        // Arrange - Title with special characters
        string newTitle = "htop - système éñ中文";
        string oscSequence = $"\x1b]0;{newTitle}\x07";

        // Act
        _terminal.Write(oscSequence);

        // Assert
        Assert.That(_session.Title, Is.EqualTo(newTitle));
        Assert.That(_lastTitleChange, Is.EqualTo(newTitle));
    }

    [Test]
    public void Session_MultipleTitleChangesWork()
    {
        // Arrange & Act - Multiple title changes
        _terminal.Write("\x1b]0;First Title\x07");
        Assert.That(_session.Title, Is.EqualTo("First Title"));

        _terminal.Write("\x1b]0;Second Title\x07");
        Assert.That(_session.Title, Is.EqualTo("Second Title"));

        _terminal.Write("\x1b]0;Final Title\x07");
        Assert.That(_session.Title, Is.EqualTo("Final Title"));
        Assert.That(_lastTitleChange, Is.EqualTo("Final Title"));
    }

    [Test]
    public void Session_ManualTitleChangeEmitsEvent()
    {
        // Arrange
        string newTitle = "Manually Set Title";

        // Act - Manually set title
        _session.Title = newTitle;

        // Assert
        Assert.That(_session.Title, Is.EqualTo(newTitle));
        Assert.That(_session.Settings.Title, Is.EqualTo(newTitle));
        Assert.That(_lastTitleChange, Is.EqualTo(newTitle));
    }
}