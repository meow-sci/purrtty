using caTTY.Core.Rpc;
using NUnit.Framework;

namespace caTTY.Core.Tests.Unit.Rpc;

/// <summary>
/// Tests for RPC component integration.
/// </summary>
[TestFixture]
[Category("Unit")]
public class RpcIntegrationTests
{
    private RpcSequenceDetector _detector = null!;
    private RpcSequenceParser _parser = null!;

    [SetUp]
    public void SetUp()
    {
        _detector = new RpcSequenceDetector();
        _parser = new RpcSequenceParser();
    }

    [Test]
    public void RpcComponents_WithValidSequence_WorkTogether()
    {
        // Arrange: RPC sequence ESC [ > 1001 ; 1 ; F
        byte[] rpcSequence = { 0x1B, 0x5B, 0x3E, 0x31, 0x30, 0x30, 0x31, 0x3B, 0x31, 0x3B, 0x46 };

        // Act & Assert: Detection
        bool isRpc = _detector.IsRpcSequence(rpcSequence);
        Assert.That(isRpc, Is.True, "Sequence should be detected as RPC");

        var sequenceType = _detector.GetSequenceType(rpcSequence);
        Assert.That(sequenceType, Is.EqualTo(RpcSequenceType.Valid), "Sequence should be valid RPC");

        // Act & Assert: Parsing
        bool canParse = _parser.TryParseRpcSequence(rpcSequence, out var message);
        Assert.That(canParse, Is.True, "Sequence should be parseable");
        Assert.That(message, Is.Not.Null, "Parsed message should not be null");
        Assert.That(message!.CommandId, Is.EqualTo(1001));
        Assert.That(message.Version, Is.EqualTo(1));
        Assert.That(message.CommandType, Is.EqualTo(RpcCommandType.FireAndForget));
    }

    [Test]
    public void RpcComponents_WithQuerySequence_WorkTogether()
    {
        // Arrange: RPC query sequence ESC [ > 2001 ; 1 ; Q
        byte[] rpcSequence = { 0x1B, 0x5B, 0x3E, 0x32, 0x30, 0x30, 0x31, 0x3B, 0x31, 0x3B, 0x51 };

        // Act & Assert: Detection
        bool isRpc = _detector.IsRpcSequence(rpcSequence);
        Assert.That(isRpc, Is.True, "Sequence should be detected as RPC");

        var sequenceType = _detector.GetSequenceType(rpcSequence);
        Assert.That(sequenceType, Is.EqualTo(RpcSequenceType.Valid), "Sequence should be valid RPC");

        // Act & Assert: Parsing
        bool canParse = _parser.TryParseRpcSequence(rpcSequence, out var message);
        Assert.That(canParse, Is.True, "Sequence should be parseable");
        Assert.That(message, Is.Not.Null, "Parsed message should not be null");
        Assert.That(message!.CommandId, Is.EqualTo(2001));
        Assert.That(message.Version, Is.EqualTo(1));
        Assert.That(message.CommandType, Is.EqualTo(RpcCommandType.Query));
    }
}