using System.Text;
using caTTY.Core.Parsing;
using caTTY.Core.Types;
using NUnit.Framework;

namespace caTTY.Core.Tests.Unit.Parsing;

/// <summary>
///     Tests for CSI window manipulation sequence parsing.
///     Covers title stack operations, size queries, and graceful handling of unsupported operations.
/// </summary>
[TestFixture]
[Category("Unit")]
public class CsiParserWindowManipulationTests
{
    [SetUp]
    public void SetUp()
    {
        _parser = new CsiParser();
    }

    private CsiParser _parser = null!;

    [Test]
    public void ParseCsiSequence_TerminalSizeQuery_ParsesCorrectly()
    {
        // CSI 18 t - Terminal size query
        byte[] sequence = Encoding.ASCII.GetBytes("\x1b[18t");
        CsiMessage result = _parser.ParseCsiSequence(sequence, "\x1b[18t");

        Assert.That(result.Type, Is.EqualTo("csi.terminalSizeQuery"));
        Assert.That(result.Implemented, Is.True);
        Assert.That(result.Parameters, Is.EqualTo(new[] { 18 }));
        Assert.That(result.Raw, Is.EqualTo("\x1b[18t"));
    }

    [Test]
    public void ParseCsiSequence_PushIconNameToStack_ParsesCorrectly()
    {
        // CSI 22;1 t - Push icon name to stack
        byte[] sequence = Encoding.ASCII.GetBytes("\x1b[22;1t");
        CsiMessage result = _parser.ParseCsiSequence(sequence, "\x1b[22;1t");

        Assert.That(result.Type, Is.EqualTo("csi.windowManipulation"));
        Assert.That(result.Implemented, Is.True);
        Assert.That(result.Operation, Is.EqualTo(22));
        Assert.That(result.WindowParams, Is.EqualTo(new[] { 1 }));
        Assert.That(result.Parameters, Is.EqualTo(new[] { 22, 1 }));
        Assert.That(result.Raw, Is.EqualTo("\x1b[22;1t"));
    }

    [Test]
    public void ParseCsiSequence_PushWindowTitleToStack_ParsesCorrectly()
    {
        // CSI 22;2 t - Push window title to stack
        byte[] sequence = Encoding.ASCII.GetBytes("\x1b[22;2t");
        CsiMessage result = _parser.ParseCsiSequence(sequence, "\x1b[22;2t");

        Assert.That(result.Type, Is.EqualTo("csi.windowManipulation"));
        Assert.That(result.Implemented, Is.True);
        Assert.That(result.Operation, Is.EqualTo(22));
        Assert.That(result.WindowParams, Is.EqualTo(new[] { 2 }));
        Assert.That(result.Parameters, Is.EqualTo(new[] { 22, 2 }));
        Assert.That(result.Raw, Is.EqualTo("\x1b[22;2t"));
    }

    [Test]
    public void ParseCsiSequence_PopIconNameFromStack_ParsesCorrectly()
    {
        // CSI 23;1 t - Pop icon name from stack
        byte[] sequence = Encoding.ASCII.GetBytes("\x1b[23;1t");
        CsiMessage result = _parser.ParseCsiSequence(sequence, "\x1b[23;1t");

        Assert.That(result.Type, Is.EqualTo("csi.windowManipulation"));
        Assert.That(result.Implemented, Is.True);
        Assert.That(result.Operation, Is.EqualTo(23));
        Assert.That(result.WindowParams, Is.EqualTo(new[] { 1 }));
        Assert.That(result.Parameters, Is.EqualTo(new[] { 23, 1 }));
        Assert.That(result.Raw, Is.EqualTo("\x1b[23;1t"));
    }

    [Test]
    public void ParseCsiSequence_PopWindowTitleFromStack_ParsesCorrectly()
    {
        // CSI 23;2 t - Pop window title from stack
        byte[] sequence = Encoding.ASCII.GetBytes("\x1b[23;2t");
        CsiMessage result = _parser.ParseCsiSequence(sequence, "\x1b[23;2t");

        Assert.That(result.Type, Is.EqualTo("csi.windowManipulation"));
        Assert.That(result.Implemented, Is.True);
        Assert.That(result.Operation, Is.EqualTo(23));
        Assert.That(result.WindowParams, Is.EqualTo(new[] { 2 }));
        Assert.That(result.Parameters, Is.EqualTo(new[] { 23, 2 }));
        Assert.That(result.Raw, Is.EqualTo("\x1b[23;2t"));
    }

    [Test]
    public void ParseCsiSequence_UnsupportedTitleStackOperation_ParsesAsNotImplemented()
    {
        // CSI 22;3 t - Invalid sub-operation for title stack
        byte[] sequence = Encoding.ASCII.GetBytes("\x1b[22;3t");
        CsiMessage result = _parser.ParseCsiSequence(sequence, "\x1b[22;3t");

        Assert.That(result.Type, Is.EqualTo("csi.windowManipulation"));
        Assert.That(result.Implemented, Is.False); // Should not be marked as implemented
        Assert.That(result.Operation, Is.EqualTo(22));
        Assert.That(result.WindowParams, Is.EqualTo(new[] { 3 }));
        Assert.That(result.Parameters, Is.EqualTo(new[] { 22, 3 }));
        Assert.That(result.Raw, Is.EqualTo("\x1b[22;3t"));
    }

    [Test]
    public void ParseCsiSequence_MinimizeWindow_ParsesAsNotImplemented()
    {
        // CSI 2 t - Minimize window (not supported in game context)
        byte[] sequence = Encoding.ASCII.GetBytes("\x1b[2t");
        CsiMessage result = _parser.ParseCsiSequence(sequence, "\x1b[2t");

        Assert.That(result.Type, Is.EqualTo("csi.windowManipulation"));
        Assert.That(result.Implemented, Is.False); // Should not be marked as implemented
        Assert.That(result.Operation, Is.EqualTo(2));
        Assert.That(result.WindowParams, Is.Empty);
        Assert.That(result.Parameters, Is.EqualTo(new[] { 2 }));
        Assert.That(result.Raw, Is.EqualTo("\x1b[2t"));
    }

    [Test]
    public void ParseCsiSequence_RestoreWindow_ParsesAsNotImplemented()
    {
        // CSI 1 t - Restore window (not supported in game context)
        byte[] sequence = Encoding.ASCII.GetBytes("\x1b[1t");
        CsiMessage result = _parser.ParseCsiSequence(sequence, "\x1b[1t");

        Assert.That(result.Type, Is.EqualTo("csi.windowManipulation"));
        Assert.That(result.Implemented, Is.False); // Should not be marked as implemented
        Assert.That(result.Operation, Is.EqualTo(1));
        Assert.That(result.WindowParams, Is.Empty);
        Assert.That(result.Parameters, Is.EqualTo(new[] { 1 }));
        Assert.That(result.Raw, Is.EqualTo("\x1b[1t"));
    }

    [Test]
    public void ParseCsiSequence_ResizeWindow_ParsesAsNotImplemented()
    {
        // CSI 8;24;80 t - Resize window to 24 rows, 80 columns (not supported in game context)
        byte[] sequence = Encoding.ASCII.GetBytes("\x1b[8;24;80t");
        CsiMessage result = _parser.ParseCsiSequence(sequence, "\x1b[8;24;80t");

        Assert.That(result.Type, Is.EqualTo("csi.windowManipulation"));
        Assert.That(result.Implemented, Is.False); // Should not be marked as implemented
        Assert.That(result.Operation, Is.EqualTo(8));
        Assert.That(result.WindowParams, Is.EqualTo(new[] { 24, 80 }));
        Assert.That(result.Parameters, Is.EqualTo(new[] { 8, 24, 80 }));
        Assert.That(result.Raw, Is.EqualTo("\x1b[8;24;80t"));
    }

    [Test]
    public void ParseCsiSequence_WindowManipulationWithNoParameters_ReturnsUnknown()
    {
        // CSI t - Window manipulation with no parameters (invalid)
        byte[] sequence = Encoding.ASCII.GetBytes("\x1b[t");
        CsiMessage result = _parser.ParseCsiSequence(sequence, "\x1b[t");

        Assert.That(result.Type, Is.EqualTo("csi.unknown"));
        Assert.That(result.Implemented, Is.False);
        Assert.That(result.Parameters, Is.Empty);
        Assert.That(result.Raw, Is.EqualTo("\x1b[t"));
    }

    [Test]
    public void ParseCsiSequence_PrivateWindowManipulation_ReturnsUnknown()
    {
        // CSI ? 18 t - Private window manipulation (not standard)
        byte[] sequence = Encoding.ASCII.GetBytes("\x1b[?18t");
        CsiMessage result = _parser.ParseCsiSequence(sequence, "\x1b[?18t");

        Assert.That(result.Type, Is.EqualTo("csi.unknown"));
        Assert.That(result.Implemented, Is.False);
        Assert.That(result.IsPrivate, Is.True);
        Assert.That(result.Parameters, Is.EqualTo(new[] { 18 }));
        Assert.That(result.Raw, Is.EqualTo("\x1b[?18t"));
    }

    [Test]
    public void ParseCsiSequence_WindowManipulationWithPrefix_ReturnsUnknown()
    {
        // CSI > 18 t - Window manipulation with prefix (not standard)
        byte[] sequence = Encoding.ASCII.GetBytes("\x1b[>18t");
        CsiMessage result = _parser.ParseCsiSequence(sequence, "\x1b[>18t");

        Assert.That(result.Type, Is.EqualTo("csi.unknown"));
        Assert.That(result.Implemented, Is.False);
        Assert.That(result.Prefix, Is.EqualTo(">"));
        Assert.That(result.Parameters, Is.EqualTo(new[] { 18 }));
        Assert.That(result.Raw, Is.EqualTo("\x1b[>18t"));
    }

    [Test]
    public void ParseCsiSequence_TitleStackOperationsWithoutSubOperation_ParsesAsNotImplemented()
    {
        // CSI 22 t - Push operation without sub-operation (incomplete)
        byte[] sequence1 = Encoding.ASCII.GetBytes("\x1b[22t");
        CsiMessage result1 = _parser.ParseCsiSequence(sequence1, "\x1b[22t");

        Assert.That(result1.Type, Is.EqualTo("csi.windowManipulation"));
        Assert.That(result1.Implemented, Is.False); // Should not be marked as implemented
        Assert.That(result1.Operation, Is.EqualTo(22));
        Assert.That(result1.WindowParams, Is.Empty);
        Assert.That(result1.Parameters, Is.EqualTo(new[] { 22 }));

        // CSI 23 t - Pop operation without sub-operation (incomplete)
        byte[] sequence2 = Encoding.ASCII.GetBytes("\x1b[23t");
        CsiMessage result2 = _parser.ParseCsiSequence(sequence2, "\x1b[23t");

        Assert.That(result2.Type, Is.EqualTo("csi.windowManipulation"));
        Assert.That(result2.Implemented, Is.False); // Should not be marked as implemented
        Assert.That(result2.Operation, Is.EqualTo(23));
        Assert.That(result2.WindowParams, Is.Empty);
        Assert.That(result2.Parameters, Is.EqualTo(new[] { 23 }));
    }
}