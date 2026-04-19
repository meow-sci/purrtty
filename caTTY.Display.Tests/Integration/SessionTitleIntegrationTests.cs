using NUnit.Framework;
using caTTY.Display.Controllers;
using caTTY.Core.Terminal;
using caTTY.Display.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Threading.Tasks;

namespace caTTY.Display.Tests.Integration;

/// <summary>
///     Integration tests for session title changes in TerminalController.
///     Validates that the UI layer properly handles title change events from sessions.
/// </summary>
[TestFixture]
[Category("Integration")]
public class SessionTitleIntegrationTests
{
    private TerminalController _controller = null!;
    private SessionManager _sessionManager = null!;
    private TerminalRenderingConfig _renderingConfig = null!;
    private TerminalFontConfig _fontConfig = null!;

    [SetUp]
    public void SetUp()
    {
        _sessionManager = new SessionManager();
        _renderingConfig = new TerminalRenderingConfig();
        _fontConfig = new TerminalFontConfig
        {
            RegularFontName = "HackNerdFontMono-Regular",
            FontSize = 14.0f
        };
        
        _controller = new TerminalController(_sessionManager, _renderingConfig, _fontConfig);
    }

    [TearDown]
    public void TearDown()
    {
        _controller?.Dispose();
        _sessionManager?.Dispose();
    }

    [Test]
    public async Task TerminalController_HandlesSessionTitleChanges()
    {
        // Arrange - Create a session (it will start with a default process title)
        var session = await _sessionManager.CreateSessionAsync("Initial Title");
        
        // The session will initially have the process command as title, not our provided title
        // This is expected behavior since the session starts a real process
        Assert.That(session.Title, Is.Not.Null.And.Not.Empty, "Session should have a title");

        // Act - Simulate OSC sequence changing the title (like htop would do)
        string newTitle = "htop";
        string oscSequence = $"\x1b]0;{newTitle}\x07";
        session.Terminal.Write(oscSequence);

        // Assert - Session title should be updated to the OSC title
        Assert.That(session.Title, Is.EqualTo(newTitle), 
            "Session title should update when terminal emits OSC title change sequence");
    }

    [Test]
    public async Task TerminalController_HandlesMultipleSessionTitleChanges()
    {
        // Arrange - Create multiple sessions
        var session1 = await _sessionManager.CreateSessionAsync("Terminal 1");
        var session2 = await _sessionManager.CreateSessionAsync("Terminal 2");

        // Act - Change titles in both sessions
        session1.Terminal.Write("\x1b]0;vim ~/.bashrc\x07");
        session2.Terminal.Write("\x1b]0;htop\x07");

        // Assert - Both sessions should have updated titles
        Assert.That(session1.Title, Is.EqualTo("vim ~/.bashrc"));
        Assert.That(session2.Title, Is.EqualTo("htop"));
    }

    [Test]
    public async Task TerminalController_HandlesEmptyTitleChange()
    {
        // Arrange
        var session = await _sessionManager.CreateSessionAsync("Initial Title");

        // Act - Send empty title OSC sequence
        session.Terminal.Write("\x1b]0;\x07");

        // Assert - Title should be empty
        Assert.That(session.Title, Is.EqualTo(string.Empty));
    }

    [Test]
    public async Task TerminalController_HandlesSpecialCharactersInTitle()
    {
        // Arrange
        var session = await _sessionManager.CreateSessionAsync("Initial Title");

        // Act - Send title with special characters
        string specialTitle = "htop - système éñ中文";
        session.Terminal.Write($"\x1b]0;{specialTitle}\x07");

        // Assert - Special characters should be preserved
        Assert.That(session.Title, Is.EqualTo(specialTitle));
    }
}