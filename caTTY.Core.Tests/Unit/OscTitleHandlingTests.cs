using NUnit.Framework;
using caTTY.Core.Terminal;
using caTTY.Core.Types;
using Microsoft.Extensions.Logging.Abstractions;

namespace caTTY.Core.Tests.Unit;

/// <summary>
///     Tests for OSC title handling functionality.
///     Validates OSC 0, OSC 1, and OSC 2 sequences for window title management.
/// </summary>
[TestFixture]
public class OscTitleHandlingTests
{
    private TerminalEmulator _terminal = null!;
    private string? _lastTitleChange;
    private string? _lastIconNameChange;

    [SetUp]
    public void SetUp()
    {
        _terminal = TerminalEmulator.Create(80, 24, NullLogger.Instance);
        _lastTitleChange = null;
        _lastIconNameChange = null;

        // Subscribe to title change events
        _terminal.TitleChanged += (sender, args) => _lastTitleChange = args.NewTitle;
        _terminal.IconNameChanged += (sender, args) => _lastIconNameChange = args.NewIconName;
    }

    [TearDown]
    public void TearDown()
    {
        _terminal?.Dispose();
    }

    [Test]
    public void OSC_0_SetsTitleAndIcon()
    {
        // OSC 0 ; title BEL
        string testTitle = "Test Window Title";
        string oscSequence = $"\x1b]0;{testTitle}\x07";

        _terminal.Write(oscSequence);

        // Verify both title and icon name are set
        Assert.That(_terminal.State.WindowProperties.Title, Is.EqualTo(testTitle));
        Assert.That(_terminal.State.WindowProperties.IconName, Is.EqualTo(testTitle));
        
        // Verify events were emitted
        Assert.That(_lastTitleChange, Is.EqualTo(testTitle));
        Assert.That(_lastIconNameChange, Is.EqualTo(testTitle));
    }

    [Test]
    public void OSC_2_SetsWindowTitleOnly()
    {
        // Set initial icon name
        _terminal.Write("\x1b]1;Initial Icon\x07");
        _lastIconNameChange = null; // Reset event tracking

        // OSC 2 ; title BEL
        string testTitle = "Window Title Only";
        string oscSequence = $"\x1b]2;{testTitle}\x07";

        _terminal.Write(oscSequence);

        // Verify only title is changed, icon name remains
        Assert.That(_terminal.State.WindowProperties.Title, Is.EqualTo(testTitle));
        Assert.That(_terminal.State.WindowProperties.IconName, Is.EqualTo("Initial Icon"));
        
        // Verify only title change event was emitted
        Assert.That(_lastTitleChange, Is.EqualTo(testTitle));
        Assert.That(_lastIconNameChange, Is.Null);
    }

    [Test]
    public void OSC_1_SetsIconNameOnly()
    {
        // Set initial title
        _terminal.Write("\x1b]2;Initial Title\x07");
        _lastTitleChange = null; // Reset event tracking

        // OSC 1 ; iconname BEL
        string testIconName = "Icon Name Only";
        string oscSequence = $"\x1b]1;{testIconName}\x07";

        _terminal.Write(oscSequence);

        // Verify only icon name is changed, title remains
        Assert.That(_terminal.State.WindowProperties.Title, Is.EqualTo("Initial Title"));
        Assert.That(_terminal.State.WindowProperties.IconName, Is.EqualTo(testIconName));
        
        // Verify only icon name change event was emitted
        Assert.That(_lastTitleChange, Is.Null);
        Assert.That(_lastIconNameChange, Is.EqualTo(testIconName));
    }

    [Test]
    public void OSC_EmptyTitle_HandledCorrectly()
    {
        // OSC 0 ; BEL (empty title)
        string oscSequence = "\x1b]0;\x07";

        _terminal.Write(oscSequence);

        // Verify empty title is set
        Assert.That(_terminal.State.WindowProperties.Title, Is.EqualTo(string.Empty));
        Assert.That(_terminal.State.WindowProperties.IconName, Is.EqualTo(string.Empty));
        
        // Verify events were emitted with empty strings
        Assert.That(_lastTitleChange, Is.EqualTo(string.Empty));
        Assert.That(_lastIconNameChange, Is.EqualTo(string.Empty));
    }

    [Test]
    public void OSC_WithSTTerminator_WorksCorrectly()
    {
        // OSC 2 ; title ST (ESC \)
        string testTitle = "Title with ST terminator";
        string oscSequence = $"\x1b]2;{testTitle}\x1b\\";

        _terminal.Write(oscSequence);

        // Verify title is set correctly
        Assert.That(_terminal.State.WindowProperties.Title, Is.EqualTo(testTitle));
        Assert.That(_lastTitleChange, Is.EqualTo(testTitle));
    }

    [Test]
    public void OSC_TitleReset_ClearsTitle()
    {
        // Set initial title
        _terminal.Write("\x1b]2;Initial Title\x07");
        
        // Reset title with empty OSC sequence
        _terminal.Write("\x1b]2;\x07");

        // Verify title is cleared
        Assert.That(_terminal.State.WindowProperties.Title, Is.EqualTo(string.Empty));
        Assert.That(_lastTitleChange, Is.EqualTo(string.Empty));
    }

    [Test]
    public void OSC_SpecialCharacters_HandledCorrectly()
    {
        // Test title with special characters (UTF-8)
        string testTitle = "Title with special chars: éñ中文";
        string oscSequence = $"\x1b]2;{testTitle}\x07";

        _terminal.Write(oscSequence);

        // Verify special characters are preserved
        Assert.That(_terminal.State.WindowProperties.Title, Is.EqualTo(testTitle));
        Assert.That(_lastTitleChange, Is.EqualTo(testTitle));
    }
}