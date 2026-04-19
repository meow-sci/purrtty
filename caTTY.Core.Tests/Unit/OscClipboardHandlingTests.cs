using System;
using System.Text;
using NUnit.Framework;
using caTTY.Core.Terminal;
using Microsoft.Extensions.Logging.Abstractions;

namespace caTTY.Core.Tests.Unit;

/// <summary>
///     Tests for OSC 52 clipboard handling functionality.
///     Validates clipboard operations, base64 decoding, and safety limits.
/// </summary>
[TestFixture]
public class OscClipboardHandlingTests
{
    private TerminalEmulator _terminal = null!;
    private ClipboardEventArgs? _lastClipboardEvent;

    [SetUp]
    public void SetUp()
    {
        _terminal = TerminalEmulator.Create(80, 24, NullLogger.Instance);
        _lastClipboardEvent = null;

        // Subscribe to clipboard events
        _terminal.ClipboardRequest += (sender, args) => _lastClipboardEvent = args;
    }

    [TearDown]
    public void TearDown()
    {
        _terminal?.Dispose();
    }

    [Test]
    public void OSC_52_ClipboardData_DecodesBase64Correctly()
    {
        // OSC 52 ; c ; base64data BEL
        string testData = "Hello World";
        string base64Data = Convert.ToBase64String(Encoding.UTF8.GetBytes(testData));
        string oscSequence = $"\x1b]52;c;{base64Data}\x07";

        _terminal.Write(oscSequence);

        // Verify clipboard event was emitted with correct data
        Assert.That(_lastClipboardEvent, Is.Not.Null);
        Assert.That(_lastClipboardEvent!.SelectionTarget, Is.EqualTo("c"));
        Assert.That(_lastClipboardEvent.Data, Is.EqualTo(testData));
        Assert.That(_lastClipboardEvent.IsQuery, Is.False);
    }

    [Test]
    public void OSC_52_ClipboardQuery_HandledCorrectly()
    {
        // OSC 52 ; c ; ? BEL (clipboard query)
        string oscSequence = "\x1b]52;c;?\x07";

        _terminal.Write(oscSequence);

        // Verify clipboard query event was emitted
        Assert.That(_lastClipboardEvent, Is.Not.Null);
        Assert.That(_lastClipboardEvent!.SelectionTarget, Is.EqualTo("c"));
        Assert.That(_lastClipboardEvent.Data, Is.Null);
        Assert.That(_lastClipboardEvent.IsQuery, Is.True);
    }

    [Test]
    public void OSC_52_ClipboardClear_HandledCorrectly()
    {
        // OSC 52 ; c ; BEL (empty data = clear)
        string oscSequence = "\x1b]52;c;\x07";

        _terminal.Write(oscSequence);

        // Verify clipboard clear event was emitted
        Assert.That(_lastClipboardEvent, Is.Not.Null);
        Assert.That(_lastClipboardEvent!.SelectionTarget, Is.EqualTo("c"));
        Assert.That(_lastClipboardEvent.Data, Is.EqualTo(string.Empty));
        Assert.That(_lastClipboardEvent.IsQuery, Is.False);
    }

    [Test]
    public void OSC_52_PrimarySelection_HandledCorrectly()
    {
        // OSC 52 ; p ; base64data BEL (primary selection)
        string testData = "Selected text";
        string base64Data = Convert.ToBase64String(Encoding.UTF8.GetBytes(testData));
        string oscSequence = $"\x1b]52;p;{base64Data}\x07";

        _terminal.Write(oscSequence);

        // Verify primary selection event was emitted
        Assert.That(_lastClipboardEvent, Is.Not.Null);
        Assert.That(_lastClipboardEvent!.SelectionTarget, Is.EqualTo("p"));
        Assert.That(_lastClipboardEvent.Data, Is.EqualTo(testData));
        Assert.That(_lastClipboardEvent.IsQuery, Is.False);
    }

    [Test]
    public void OSC_52_MultipleSelections_HandledCorrectly()
    {
        // OSC 52 ; pc ; base64data BEL (multiple selections)
        string testData = "Multi-selection data";
        string base64Data = Convert.ToBase64String(Encoding.UTF8.GetBytes(testData));
        string oscSequence = $"\x1b]52;pc;{base64Data}\x07";

        _terminal.Write(oscSequence);

        // Verify multiple selection event was emitted
        Assert.That(_lastClipboardEvent, Is.Not.Null);
        Assert.That(_lastClipboardEvent!.SelectionTarget, Is.EqualTo("pc"));
        Assert.That(_lastClipboardEvent.Data, Is.EqualTo(testData));
        Assert.That(_lastClipboardEvent.IsQuery, Is.False);
    }

    [Test]
    public void OSC_52_InvalidBase64_IgnoredGracefully()
    {
        // OSC 52 ; c ; invalidbase64 BEL
        string oscSequence = "\x1b]52;c;invalid-base64-data!\x07";

        _terminal.Write(oscSequence);

        // Verify no clipboard event was emitted for invalid base64
        Assert.That(_lastClipboardEvent, Is.Null);
    }

    [Test]
    public void OSC_52_LargeBase64Data_RejectedSafely()
    {
        // Create base64 data that exceeds the safety limit (4096 chars)
        string largeData = new string('A', 5000);
        string oscSequence = $"\x1b]52;c;{largeData}\x07";

        _terminal.Write(oscSequence);

        // Verify no clipboard event was emitted for oversized data
        Assert.That(_lastClipboardEvent, Is.Null);
    }

    [Test]
    public void OSC_52_LargeDecodedData_RejectedSafely()
    {
        // Create data that when decoded exceeds the safety limit (2048 bytes)
        string largeText = new string('X', 2500);
        string base64Data = Convert.ToBase64String(Encoding.UTF8.GetBytes(largeText));
        string oscSequence = $"\x1b]52;c;{base64Data}\x07";

        _terminal.Write(oscSequence);

        // Verify no clipboard event was emitted for oversized decoded data
        Assert.That(_lastClipboardEvent, Is.Null);
    }

    [Test]
    public void OSC_52_EmptySelectionTarget_IgnoredGracefully()
    {
        // OSC 52 ; ; base64data BEL (empty selection target)
        string testData = "Test data";
        string base64Data = Convert.ToBase64String(Encoding.UTF8.GetBytes(testData));
        string oscSequence = $"\x1b]52;;{base64Data}\x07";

        _terminal.Write(oscSequence);

        // Verify no clipboard event was emitted for empty selection target
        Assert.That(_lastClipboardEvent, Is.Null);
    }

    [Test]
    public void OSC_52_MalformedPayload_IgnoredGracefully()
    {
        // OSC 52 without proper semicolon structure
        string oscSequence = "\x1b]52\x07"; // Missing semicolons entirely

        _terminal.Write(oscSequence);

        // Verify no clipboard event was emitted for malformed payload
        Assert.That(_lastClipboardEvent, Is.Null);
    }

    [Test]
    public void OSC_52_WithSTTerminator_WorksCorrectly()
    {
        // OSC 52 ; c ; base64data ST (ESC \)
        string testData = "ST terminator test";
        string base64Data = Convert.ToBase64String(Encoding.UTF8.GetBytes(testData));
        string oscSequence = $"\x1b]52;c;{base64Data}\x1b\\";

        _terminal.Write(oscSequence);

        // Verify clipboard event was emitted correctly with ST terminator
        Assert.That(_lastClipboardEvent, Is.Not.Null);
        Assert.That(_lastClipboardEvent!.SelectionTarget, Is.EqualTo("c"));
        Assert.That(_lastClipboardEvent.Data, Is.EqualTo(testData));
        Assert.That(_lastClipboardEvent.IsQuery, Is.False);
    }

    [Test]
    public void OSC_52_UTF8Data_HandledCorrectly()
    {
        // Test clipboard with UTF-8 characters
        string testData = "UTF-8 test: éñ中文🚀";
        string base64Data = Convert.ToBase64String(Encoding.UTF8.GetBytes(testData));
        string oscSequence = $"\x1b]52;c;{base64Data}\x07";

        _terminal.Write(oscSequence);

        // Verify UTF-8 characters are preserved
        Assert.That(_lastClipboardEvent, Is.Not.Null);
        Assert.That(_lastClipboardEvent!.SelectionTarget, Is.EqualTo("c"));
        Assert.That(_lastClipboardEvent.Data, Is.EqualTo(testData));
        Assert.That(_lastClipboardEvent.IsQuery, Is.False);
    }

    [Test]
    public void OSC_52_TypeScriptCompatibility_HelloWorld()
    {
        // Test case matching TypeScript: "Hello World" -> base64
        string testData = "Hello World";
        string base64Data = Convert.ToBase64String(Encoding.UTF8.GetBytes(testData)); // "SGVsbG8gV29ybGQ="
        string oscSequence = $"\x1b]52;c;{base64Data}\x07";

        _terminal.Write(oscSequence);

        // Verify compatibility with TypeScript test expectations
        Assert.That(_lastClipboardEvent, Is.Not.Null);
        Assert.That(_lastClipboardEvent!.SelectionTarget, Is.EqualTo("c"));
        Assert.That(_lastClipboardEvent.Data, Is.EqualTo(testData));
        Assert.That(_lastClipboardEvent.IsQuery, Is.False);
        
        // Verify the base64 encoding matches expected value
        Assert.That(base64Data, Is.EqualTo("SGVsbG8gV29ybGQ="));
    }
}