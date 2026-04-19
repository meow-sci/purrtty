using caTTY.Core.Terminal;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace caTTY.Core.Tests.Unit.Terminal;

[TestFixture]
public class AlternateScreenTests
{
    private TerminalEmulator _terminal = null!;

    [SetUp]
    public void SetUp()
    {
        _terminal = TerminalEmulator.Create(80, 24, 1000, NullLogger.Instance);
    }

    [TearDown]
    public void TearDown()
    {
        _terminal.Dispose();
    }

    [Test]
    public void Decset47_SwitchesToAlternate_AndBack_PreservesPrimaryContent()
    {
        _terminal.Write("Primary screen content");
        Assert.That(_terminal.ScreenBuffer.GetCell(0, 0).Character, Is.EqualTo('P'));

        _terminal.Write("\x1b[?47h");
        Assert.That(_terminal.State.IsAlternateScreenActive, Is.True);

        _terminal.Write("Alternate screen");
        Assert.That(_terminal.ScreenBuffer.GetCell(0, 0).Character, Is.EqualTo('A'));

        _terminal.Write("\x1b[?47l");
        Assert.That(_terminal.State.IsAlternateScreenActive, Is.False);

        Assert.That(_terminal.ScreenBuffer.GetCell(0, 0).Character, Is.EqualTo('P'));

        // Switch again: alternate content should still be there (independent buffer persistence)
        _terminal.Write("\x1b[?47h");
        Assert.That(_terminal.ScreenBuffer.GetCell(0, 0).Character, Is.EqualTo('A'));
    }

    [Test]
    public void Decset1047_SavesAndRestoresCursor_OnExit()
    {
        _terminal.Write("\x1b[5;10H");
        _terminal.Write("Test");

        // After writing "Test" starting at col 10 (1-based), cursor should be col 13 (0-based).
        Assert.That(_terminal.Cursor.Col, Is.EqualTo(13));
        Assert.That(_terminal.Cursor.Row, Is.EqualTo(4));

        _terminal.Write("\x1b[?1047h");
        Assert.That(_terminal.State.IsAlternateScreenActive, Is.True);

        _terminal.Write("\x1b[1;1H");
        _terminal.Write("Alt");

        _terminal.Write("\x1b[?1047l");
        Assert.That(_terminal.State.IsAlternateScreenActive, Is.False);

        Assert.That(_terminal.Cursor.Col, Is.EqualTo(13));
        Assert.That(_terminal.Cursor.Row, Is.EqualTo(4));
    }

    [Test]
    public void Decset1049_ClearsAlternate_OnEnter_AndPreservesPrimaryOnExit()
    {
        _terminal.Write("Primary");
        Assert.That(_terminal.ScreenBuffer.GetCell(0, 0).Character, Is.EqualTo('P'));

        _terminal.Write("\x1b[?1049h");
        Assert.That(_terminal.State.IsAlternateScreenActive, Is.True);

        // Alternate should be clear and cursor at origin
        Assert.That(_terminal.Cursor.Col, Is.EqualTo(0));
        Assert.That(_terminal.Cursor.Row, Is.EqualTo(0));
        Assert.That(_terminal.ScreenBuffer.GetCell(0, 0).Character, Is.EqualTo(' '));

        _terminal.Write("Alternate");
        Assert.That(_terminal.ScreenBuffer.GetCell(0, 0).Character, Is.EqualTo('A'));

        _terminal.Write("\x1b[?1049l");
        Assert.That(_terminal.State.IsAlternateScreenActive, Is.False);

        // Primary content should be preserved
        Assert.That(_terminal.ScreenBuffer.GetCell(0, 0).Character, Is.EqualTo('P'));
    }
}
