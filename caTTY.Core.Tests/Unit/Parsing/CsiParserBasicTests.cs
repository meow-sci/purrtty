using System.Text;
using caTTY.Core.Parsing;
using caTTY.Core.Types;
using NUnit.Framework;

namespace caTTY.Core.Tests.Unit.Parsing;

[TestFixture]
[Category("Unit")]
public class CsiParserBasicTests
{
    [SetUp]
    public void SetUp()
    {
        _parser = new CsiParser();
    }

    private CsiParser _parser = null!;

    [Test]
    public void CsiParser_CanBeCreated()
    {
        var parser = new CsiParser();
        Assert.That(parser, Is.Not.Null);
    }

    [Test]
    public void GetParameter_WithValidIndex_ReturnsValue()
    {
        int[] parameters = new[] { 10, 20, 30 };

        int result = _parser.GetParameter(parameters, 1, 99);

        Assert.That(result, Is.EqualTo(20));
    }

    [Test]
    public void GetParameter_WithInvalidIndex_ReturnsFallback()
    {
        int[] parameters = new[] { 10, 20 };

        Assert.That(_parser.GetParameter(parameters, 5, 99), Is.EqualTo(99));
        Assert.That(_parser.GetParameter(parameters, -1, 99), Is.EqualTo(99));
    }

    [Test]
    public void TryParseParameters_EmptyString_ReturnsEmptyArray()
    {
        bool result =
            _parser.TryParseParameters("".AsSpan(), out int[] parameters, out bool isPrivate, out string? prefix);

        Assert.That(result, Is.True);
        Assert.That(parameters, Is.Empty);
        Assert.That(isPrivate, Is.False);
        Assert.That(prefix, Is.Null);
    }

    [Test]
    public void TryParseParameters_PrivateMode_SetsPrivateFlag()
    {
        bool result = _parser.TryParseParameters("?1;2".AsSpan(), out int[] parameters, out bool isPrivate,
            out string? prefix);

        Assert.That(result, Is.True);
        Assert.That(parameters, Is.EqualTo(new[] { 1, 2 }));
        Assert.That(isPrivate, Is.True);
        Assert.That(prefix, Is.Null);
    }

    [Test]
    public void TryParseParameters_PrefixMode_SetsPrefix()
    {
        bool result = _parser.TryParseParameters(">4;5".AsSpan(), out int[] parameters, out bool isPrivate,
            out string? prefix);

        Assert.That(result, Is.True);
        Assert.That(parameters, Is.EqualTo(new[] { 4, 5 }));
        Assert.That(isPrivate, Is.False);
        Assert.That(prefix, Is.EqualTo(">"));
    }

    [Test]
    public void TryParseParameters_SemicolonSeparated_ParsesCorrectly()
    {
        bool result = _parser.TryParseParameters("1;2;3".AsSpan(), out int[] parameters, out bool isPrivate,
            out string? prefix);

        Assert.That(result, Is.True);
        Assert.That(parameters, Is.EqualTo(new[] { 1, 2, 3 }));
        Assert.That(isPrivate, Is.False);
        Assert.That(prefix, Is.Null);
    }

    [Test]
    public void ParseCsiSequence_CursorUp_ParsesCorrectly()
    {
        byte[] sequence = Encoding.ASCII.GetBytes("\x1b[5A");
        CsiMessage result = _parser.ParseCsiSequence(sequence, "\x1b[5A");

        Assert.That(result.Type, Is.EqualTo("csi.cursorUp"));
        Assert.That(result.Implemented, Is.True);
        Assert.That(result.Count, Is.EqualTo(5));
        Assert.That(result.Parameters, Is.EqualTo(new[] { 5 }));
    }

    [Test]
    public void ParseCsiSequence_CursorPosition_ParsesCorrectly()
    {
        byte[] sequence = Encoding.ASCII.GetBytes("\x1b[10;20H");
        CsiMessage result = _parser.ParseCsiSequence(sequence, "\x1b[10;20H");

        Assert.That(result.Type, Is.EqualTo("csi.cursorPosition"));
        Assert.That(result.Implemented, Is.True);
        Assert.That(result.Row, Is.EqualTo(10));
        Assert.That(result.Column, Is.EqualTo(20));
        Assert.That(result.Parameters, Is.EqualTo(new[] { 10, 20 }));
    }

    [Test]
    public void ParseCsiSequence_DecModeSet_ParsesCorrectly()
    {
        byte[] sequence = Encoding.ASCII.GetBytes("\x1b[?1;2h");
        CsiMessage result = _parser.ParseCsiSequence(sequence, "\x1b[?1;2h");

        Assert.That(result.Type, Is.EqualTo("csi.decModeSet"));
        Assert.That(result.Implemented, Is.True);
        Assert.That(result.DecModes, Is.EqualTo(new[] { 1, 2 }));
        Assert.That(result.Parameters, Is.EqualTo(new[] { 1, 2 }));
    }

    [Test]
    public void ParseCsiSequence_BracketedPasteModeSet_ParsesCorrectly()
    {
        byte[] sequence = Encoding.ASCII.GetBytes("\x1b[?2004h");
        CsiMessage result = _parser.ParseCsiSequence(sequence, "\x1b[?2004h");

        Assert.That(result.Type, Is.EqualTo("csi.decModeSet"));
        Assert.That(result.Implemented, Is.True);
        Assert.That(result.DecModes, Is.EqualTo(new[] { 2004 }));
        Assert.That(result.Parameters, Is.EqualTo(new[] { 2004 }));
    }

    [Test]
    public void ParseCsiSequence_BracketedPasteModeReset_ParsesCorrectly()
    {
        byte[] sequence = Encoding.ASCII.GetBytes("\x1b[?2004l");
        CsiMessage result = _parser.ParseCsiSequence(sequence, "\x1b[?2004l");

        Assert.That(result.Type, Is.EqualTo("csi.decModeReset"));
        Assert.That(result.Implemented, Is.True);
        Assert.That(result.DecModes, Is.EqualTo(new[] { 2004 }));
        Assert.That(result.Parameters, Is.EqualTo(new[] { 2004 }));
    }

    [Test]
    public void ParseCsiSequence_EraseInDisplay_ParsesCorrectly()
    {
        byte[] sequence = Encoding.ASCII.GetBytes("\x1b[2J");
        CsiMessage result = _parser.ParseCsiSequence(sequence, "\x1b[2J");

        Assert.That(result.Type, Is.EqualTo("csi.eraseInDisplay"));
        Assert.That(result.Implemented, Is.True);
        Assert.That(result.Mode, Is.EqualTo(2));
    }

    [Test]
    public void ParseCsiSequence_EraseInLine_ParsesCorrectly()
    {
        // Test all erase in line modes
        int[] modes = new[] { 0, 1, 2 };
        string[] expectedSequences = new[] { "\x1b[K", "\x1b[1K", "\x1b[2K" };

        for (int i = 0; i < modes.Length; i++)
        {
            byte[] sequence = Encoding.ASCII.GetBytes(expectedSequences[i]);
            CsiMessage result = _parser.ParseCsiSequence(sequence, expectedSequences[i]);

            Assert.That(result.Type, Is.EqualTo("csi.eraseInLine"));
            Assert.That(result.Implemented, Is.True);
            Assert.That(result.Mode, Is.EqualTo(modes[i]));
        }
    }

    [Test]
    public void ParseCsiSequence_SelectiveEraseSequences_ParsesCorrectly()
    {
        // Selective erase in display
        byte[] selectiveEraseDisplay = Encoding.ASCII.GetBytes("\x1b[?2J");
        CsiMessage result1 = _parser.ParseCsiSequence(selectiveEraseDisplay, "\x1b[?2J");

        Assert.That(result1.Type, Is.EqualTo("csi.selectiveEraseInDisplay"));
        Assert.That(result1.Implemented, Is.True);
        Assert.That(result1.Mode, Is.EqualTo(2));

        // Selective erase in line
        byte[] selectiveEraseLine = Encoding.ASCII.GetBytes("\x1b[?1K");
        CsiMessage result2 = _parser.ParseCsiSequence(selectiveEraseLine, "\x1b[?1K");

        Assert.That(result2.Type, Is.EqualTo("csi.selectiveEraseInLine"));
        Assert.That(result2.Implemented, Is.True);
        Assert.That(result2.Mode, Is.EqualTo(1));
    }

    [Test]
    public void ParseCsiSequence_UnknownSequence_ReturnsUnknown()
    {
        byte[] sequence = Encoding.ASCII.GetBytes("\x1b[99z");
        CsiMessage result = _parser.ParseCsiSequence(sequence, "\x1b[99z");

        Assert.That(result.Type, Is.EqualTo("csi.unknown"));
        Assert.That(result.Implemented, Is.False);
        Assert.That(result.Parameters, Is.EqualTo(new[] { 99 }));
    }

    [Test]
    public void ParseCsiSequence_TabCommands_ParseCorrectly()
    {
        // Forward tab
        byte[] forwardTab = Encoding.ASCII.GetBytes("\x1b[3I");
        CsiMessage result1 = _parser.ParseCsiSequence(forwardTab, "\x1b[3I");

        Assert.That(result1.Type, Is.EqualTo("csi.cursorForwardTab"));
        Assert.That(result1.Implemented, Is.True);
        Assert.That(result1.Count, Is.EqualTo(3));

        // Backward tab
        byte[] backwardTab = Encoding.ASCII.GetBytes("\x1b[2Z");
        CsiMessage result2 = _parser.ParseCsiSequence(backwardTab, "\x1b[2Z");

        Assert.That(result2.Type, Is.EqualTo("csi.cursorBackwardTab"));
        Assert.That(result2.Implemented, Is.True);
        Assert.That(result2.Count, Is.EqualTo(2));

        // Tab clear
        byte[] tabClear = Encoding.ASCII.GetBytes("\x1b[3g");
        CsiMessage result3 = _parser.ParseCsiSequence(tabClear, "\x1b[3g");

        Assert.That(result3.Type, Is.EqualTo("csi.tabClear"));
        Assert.That(result3.Implemented, Is.True);
        Assert.That(result3.Mode, Is.EqualTo(3));
    }

    [Test]
    public void ParseCsiSequence_ScrollSequences_ParseCorrectly()
    {
        // Scroll up (CSI S)
        byte[] scrollUp = Encoding.ASCII.GetBytes("\x1b[3S");
        CsiMessage result1 = _parser.ParseCsiSequence(scrollUp, "\x1b[3S");

        Assert.That(result1.Type, Is.EqualTo("csi.scrollUp"));
        Assert.That(result1.Implemented, Is.True);
        Assert.That(result1.Lines, Is.EqualTo(3));
        Assert.That(result1.Parameters, Is.EqualTo(new[] { 3 }));

        // Scroll up with default parameter
        byte[] scrollUpDefault = Encoding.ASCII.GetBytes("\x1b[S");
        CsiMessage result2 = _parser.ParseCsiSequence(scrollUpDefault, "\x1b[S");

        Assert.That(result2.Type, Is.EqualTo("csi.scrollUp"));
        Assert.That(result2.Implemented, Is.True);
        Assert.That(result2.Lines, Is.EqualTo(1)); // Default parameter
        Assert.That(result2.Parameters, Is.Empty);

        // Scroll down (CSI T)
        byte[] scrollDown = Encoding.ASCII.GetBytes("\x1b[5T");
        CsiMessage result3 = _parser.ParseCsiSequence(scrollDown, "\x1b[5T");

        Assert.That(result3.Type, Is.EqualTo("csi.scrollDown"));
        Assert.That(result3.Implemented, Is.True);
        Assert.That(result3.Lines, Is.EqualTo(5));
        Assert.That(result3.Parameters, Is.EqualTo(new[] { 5 }));

        // Scroll down with default parameter
        byte[] scrollDownDefault = Encoding.ASCII.GetBytes("\x1b[T");
        CsiMessage result4 = _parser.ParseCsiSequence(scrollDownDefault, "\x1b[T");

        Assert.That(result4.Type, Is.EqualTo("csi.scrollDown"));
        Assert.That(result4.Implemented, Is.True);
        Assert.That(result4.Lines, Is.EqualTo(1)); // Default parameter
        Assert.That(result4.Parameters, Is.Empty);
    }

    [Test]
    public void ParseCsiSequence_DeviceQueries_ParseCorrectly()
    {
        // Primary DA
        byte[] primaryDa = Encoding.ASCII.GetBytes("\x1b[c");
        CsiMessage result1 = _parser.ParseCsiSequence(primaryDa, "\x1b[c");

        Assert.That(result1.Type, Is.EqualTo("csi.deviceAttributesPrimary"));
        Assert.That(result1.Implemented, Is.True);

        // Secondary DA
        byte[] secondaryDa = Encoding.ASCII.GetBytes("\x1b[>c");
        CsiMessage result2 = _parser.ParseCsiSequence(secondaryDa, "\x1b[>c");

        Assert.That(result2.Type, Is.EqualTo("csi.deviceAttributesSecondary"));
        Assert.That(result2.Implemented, Is.True);

        // Cursor position report
        byte[] cpr = Encoding.ASCII.GetBytes("\x1b[6n");
        CsiMessage result3 = _parser.ParseCsiSequence(cpr, "\x1b[6n");

        Assert.That(result3.Type, Is.EqualTo("csi.cursorPositionReport"));
        Assert.That(result3.Implemented, Is.True);
    }

    [Test]
    public void ParseCsiSequence_AnsiCursorSaveRestore_ParsesCorrectly()
    {
        // ANSI cursor save (CSI s)
        byte[] cursorSave = Encoding.ASCII.GetBytes("\x1b[s");
        CsiMessage result1 = _parser.ParseCsiSequence(cursorSave, "\x1b[s");

        Assert.That(result1.Type, Is.EqualTo("csi.saveCursorPosition"));
        Assert.That(result1.Implemented, Is.True);
        Assert.That(result1.Parameters, Is.Empty);
        Assert.That(result1.Raw, Is.EqualTo("\x1b[s"));

        // ANSI cursor restore (CSI u)
        byte[] cursorRestore = Encoding.ASCII.GetBytes("\x1b[u");
        CsiMessage result2 = _parser.ParseCsiSequence(cursorRestore, "\x1b[u");

        Assert.That(result2.Type, Is.EqualTo("csi.restoreCursorPosition"));
        Assert.That(result2.Implemented, Is.True);
        Assert.That(result2.Parameters, Is.Empty);
        Assert.That(result2.Raw, Is.EqualTo("\x1b[u"));
    }

    [Test]
    public void ParseCsiSequence_AnsiCursorSaveRestore_DistinguishedFromPrivateModes()
    {
        // ANSI cursor save (CSI s) - non-private
        byte[] ansiSave = Encoding.ASCII.GetBytes("\x1b[s");
        CsiMessage result1 = _parser.ParseCsiSequence(ansiSave, "\x1b[s");

        Assert.That(result1.Type, Is.EqualTo("csi.saveCursorPosition"));
        Assert.That(result1.Implemented, Is.True);

        // Private mode save (CSI ? 1 s) - should be different
        byte[] privateSave = Encoding.ASCII.GetBytes("\x1b[?1s");
        CsiMessage result2 = _parser.ParseCsiSequence(privateSave, "\x1b[?1s");

        Assert.That(result2.Type, Is.EqualTo("csi.savePrivateMode"));
        Assert.That(result2.Implemented, Is.True);
        Assert.That(result2.DecModes, Is.EqualTo(new[] { 1 }));

        // Private mode restore (CSI ? 1 r) - should be different from scroll region
        byte[] privateRestore = Encoding.ASCII.GetBytes("\x1b[?1r");
        CsiMessage result3 = _parser.ParseCsiSequence(privateRestore, "\x1b[?1r");

        Assert.That(result3.Type, Is.EqualTo("csi.restorePrivateMode"));
        Assert.That(result3.Implemented, Is.True);
        Assert.That(result3.DecModes, Is.EqualTo(new[] { 1 }));
    }
}
