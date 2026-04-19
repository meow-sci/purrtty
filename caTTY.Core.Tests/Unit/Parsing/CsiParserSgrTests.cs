using NUnit.Framework;
using caTTY.Core.Parsing;
using System.Text;

namespace caTTY.Core.Tests.Unit.Parsing;

/// <summary>
///     Tests for CSI parser SGR command handling.
/// </summary>
[TestFixture]
public class CsiParserSgrTests
{
    private CsiParser _parser = null!;

    [SetUp]
    public void SetUp()
    {
        _parser = new CsiParser();
    }

    [Test]
    public void ParseCsiSequence_StandardSgr_ShouldReturnSgrMessage()
    {
        // Arrange
        string sequence = "\x1b[1;31m"; // Bold red
        byte[] bytes = Encoding.UTF8.GetBytes(sequence);

        // Act
        var message = _parser.ParseCsiSequence(bytes, sequence);

        // Assert
        Assert.That(message.Type, Is.EqualTo("csi.sgr"), "Should be SGR message type");
        Assert.That(message.Implemented, Is.True, "Should be implemented");
        Assert.That(message.Parameters, Is.EqualTo(new[] { 1, 31 }), "Should have correct parameters");
        Assert.That(message.Raw, Is.EqualTo(sequence), "Should preserve raw sequence");
    }

    [Test]
    public void ParseCsiSequence_SgrReset_ShouldReturnSgrMessage()
    {
        // Arrange
        string sequence = "\x1b[0m"; // Reset
        byte[] bytes = Encoding.UTF8.GetBytes(sequence);

        // Act
        var message = _parser.ParseCsiSequence(bytes, sequence);

        // Assert
        Assert.That(message.Type, Is.EqualTo("csi.sgr"), "Should be SGR message type");
        Assert.That(message.Implemented, Is.True, "Should be implemented");
        Assert.That(message.Parameters, Is.EqualTo(new[] { 0 }), "Should have reset parameter");
    }

    [Test]
    public void ParseCsiSequence_EmptySgr_ShouldReturnSgrMessage()
    {
        // Arrange
        string sequence = "\x1b[m"; // Empty parameters (equivalent to reset)
        byte[] bytes = Encoding.UTF8.GetBytes(sequence);

        // Act
        var message = _parser.ParseCsiSequence(bytes, sequence);

        // Assert
        Assert.That(message.Type, Is.EqualTo("csi.sgr"), "Should be SGR message type");
        Assert.That(message.Implemented, Is.True, "Should be implemented");
        Assert.That(message.Parameters, Is.EqualTo(new int[0]), "Should have empty parameters (SGR parser will default to reset)");
    }

    [Test]
    public void ParseCsiSequence_EnhancedSgr_ShouldReturnEnhancedSgrMessage()
    {
        // Arrange
        string sequence = "\x1b[>4;2m"; // Enhanced SGR with > prefix
        byte[] bytes = Encoding.UTF8.GetBytes(sequence);

        // Act
        var message = _parser.ParseCsiSequence(bytes, sequence);

        // Assert
        Assert.That(message.Type, Is.EqualTo("csi.enhancedSgrMode"), "Should be enhanced SGR message type");
        Assert.That(message.Parameters, Is.EqualTo(new[] { 4, 2 }), "Should have correct parameters");
    }

    [Test]
    public void ParseCsiSequence_PrivateSgr_ShouldReturnPrivateSgrMessage()
    {
        // Arrange
        string sequence = "\x1b[?4m"; // Private SGR with ? prefix
        byte[] bytes = Encoding.UTF8.GetBytes(sequence);

        // Act
        var message = _parser.ParseCsiSequence(bytes, sequence);

        // Assert
        Assert.That(message.Type, Is.EqualTo("csi.privateSgrMode"), "Should be private SGR message type");
        Assert.That(message.Parameters, Is.EqualTo(new[] { 4 }), "Should have correct parameters");
    }

    [Test]
    public void ParseCsiSequence_PrivateSgrImplemented_ShouldBeImplementedForMode4()
    {
        // Arrange
        string sequence = "\x1b[?4m"; // Private SGR mode 4 (underline)
        byte[] bytes = Encoding.UTF8.GetBytes(sequence);

        // Act
        var message = _parser.ParseCsiSequence(bytes, sequence);

        // Assert
        Assert.That(message.Type, Is.EqualTo("csi.privateSgrMode"), "Should be private SGR message type");
        Assert.That(message.Implemented, Is.True, "Mode 4 should be implemented");
        Assert.That(message.Parameters, Is.EqualTo(new[] { 4 }), "Should have correct parameters");
    }

    [Test]
    public void ParseCsiSequence_PrivateSgrUnknownMode_ShouldNotBeImplemented()
    {
        // Arrange
        string sequence = "\x1b[?7m"; // Private SGR mode 7 (unknown)
        byte[] bytes = Encoding.UTF8.GetBytes(sequence);

        // Act
        var message = _parser.ParseCsiSequence(bytes, sequence);

        // Assert
        Assert.That(message.Type, Is.EqualTo("csi.privateSgrMode"), "Should be private SGR message type");
        Assert.That(message.Implemented, Is.False, "Unknown modes should not be implemented");
        Assert.That(message.Parameters, Is.EqualTo(new[] { 7 }), "Should have correct parameters");
    }

    [Test]
    public void ParseCsiSequence_PrivateSgrMultipleParams_ShouldNotBeImplemented()
    {
        // Arrange
        string sequence = "\x1b[?4;5m"; // Private SGR with multiple parameters
        byte[] bytes = Encoding.UTF8.GetBytes(sequence);

        // Act
        var message = _parser.ParseCsiSequence(bytes, sequence);

        // Assert
        Assert.That(message.Type, Is.EqualTo("csi.privateSgrMode"), "Should be private SGR message type");
        Assert.That(message.Implemented, Is.False, "Multiple parameters should not be implemented");
        Assert.That(message.Parameters, Is.EqualTo(new[] { 4, 5 }), "Should have correct parameters");
    }

    [Test]
    public void ParseCsiSequence_SgrWithIntermediate_ShouldReturnSgrWithIntermediateMessage()
    {
        // Arrange
        string sequence = "\x1b[0%m"; // SGR with intermediate character
        byte[] bytes = Encoding.UTF8.GetBytes(sequence);

        // Act
        var message = _parser.ParseCsiSequence(bytes, sequence);

        // Assert
        Assert.That(message.Type, Is.EqualTo("csi.sgrWithIntermediate"), "Should be SGR with intermediate message type");
        Assert.That(message.Parameters, Is.EqualTo(new[] { 0 }), "Should have correct parameters");
        Assert.That(message.Intermediate, Is.EqualTo("%"), "Should have correct intermediate character");
        Assert.That(message.Implemented, Is.True, "CSI 0 % m should be implemented");
    }

    [Test]
    public void ParseCsiSequence_SgrWithIntermediate_PercentReset_ShouldBeImplemented()
    {
        // Arrange
        string sequence = "\x1b[0%m"; // SGR reset with % intermediate
        byte[] bytes = Encoding.UTF8.GetBytes(sequence);

        // Act
        var message = _parser.ParseCsiSequence(bytes, sequence);

        // Assert
        Assert.That(message.Type, Is.EqualTo("csi.sgrWithIntermediate"), "Should be SGR with intermediate message type");
        Assert.That(message.Implemented, Is.True, "CSI 0 % m should be implemented");
        Assert.That(message.Parameters, Is.EqualTo(new[] { 0 }), "Should have parameter 0");
        Assert.That(message.Intermediate, Is.EqualTo("%"), "Should have % intermediate");
    }

    [Test]
    public void ParseCsiSequence_SgrWithIntermediate_NonZeroParameter_ShouldNotBeImplemented()
    {
        // Arrange
        string sequence = "\x1b[1%m"; // SGR with % intermediate but non-zero parameter
        byte[] bytes = Encoding.UTF8.GetBytes(sequence);

        // Act
        var message = _parser.ParseCsiSequence(bytes, sequence);

        // Assert
        Assert.That(message.Type, Is.EqualTo("csi.sgrWithIntermediate"), "Should be SGR with intermediate message type");
        Assert.That(message.Implemented, Is.False, "CSI 1 % m should not be implemented");
        Assert.That(message.Parameters, Is.EqualTo(new[] { 1 }), "Should have parameter 1");
        Assert.That(message.Intermediate, Is.EqualTo("%"), "Should have % intermediate");
    }

    [Test]
    public void ParseCsiSequence_SgrWithIntermediate_MultipleParameters_ShouldNotBeImplemented()
    {
        // Arrange
        string sequence = "\x1b[0;1%m"; // SGR with % intermediate but multiple parameters
        byte[] bytes = Encoding.UTF8.GetBytes(sequence);

        // Act
        var message = _parser.ParseCsiSequence(bytes, sequence);

        // Assert
        Assert.That(message.Type, Is.EqualTo("csi.sgrWithIntermediate"), "Should be SGR with intermediate message type");
        Assert.That(message.Implemented, Is.False, "CSI 0;1 % m should not be implemented");
        Assert.That(message.Parameters, Is.EqualTo(new[] { 0, 1 }), "Should have parameters 0,1");
        Assert.That(message.Intermediate, Is.EqualTo("%"), "Should have % intermediate");
    }

    [Test]
    public void ParseCsiSequence_SgrWithIntermediate_DifferentIntermediate_ShouldNotBeImplemented()
    {
        // Arrange
        string sequence = "\x1b[0\"m"; // SGR with different intermediate character
        byte[] bytes = Encoding.UTF8.GetBytes(sequence);

        // Act
        var message = _parser.ParseCsiSequence(bytes, sequence);

        // Assert
        Assert.That(message.Type, Is.EqualTo("csi.sgrWithIntermediate"), "Should be SGR with intermediate message type");
        Assert.That(message.Implemented, Is.False, "CSI 0 \" m should not be implemented");
        Assert.That(message.Parameters, Is.EqualTo(new[] { 0 }), "Should have parameter 0");
        Assert.That(message.Intermediate, Is.EqualTo("\""), "Should have \" intermediate");
    }

    [Test]
    public void ParseCsiSequence_SgrWithIntermediate_SpaceIntermediate_ShouldNotBeImplemented()
    {
        // Arrange
        string sequence = "\x1b[0 m"; // SGR with space intermediate character
        byte[] bytes = Encoding.UTF8.GetBytes(sequence);

        // Act
        var message = _parser.ParseCsiSequence(bytes, sequence);

        // Assert
        Assert.That(message.Type, Is.EqualTo("csi.sgrWithIntermediate"), "Should be SGR with intermediate message type");
        Assert.That(message.Implemented, Is.False, "CSI 0 SP m should not be implemented");
        Assert.That(message.Parameters, Is.EqualTo(new[] { 0 }), "Should have parameter 0");
        Assert.That(message.Intermediate, Is.EqualTo(" "), "Should have space intermediate");
    }
}