using caTTY.Core.Terminal;
using caTTY.Core.Rpc;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace caTTY.Core.Tests.Unit.Terminal;

[TestFixture]
[Category("Unit")]
public class TerminalEmulatorRpcIntegrationTests
{
    private TerminalEmulator _terminal = null!;
    private MockRpcHandler _mockRpcHandler = null!;
    private ILogger _logger = null!;

    [SetUp]
    public void SetUp()
    {
        _logger = new TestLogger();
        _mockRpcHandler = new MockRpcHandler();
        _terminal = TerminalEmulator.Create(80, 24, 1000, _logger, _mockRpcHandler);
    }

    [TearDown]
    public void TearDown()
    {
        _terminal?.Dispose();
    }

    [Test]
    public void Constructor_WithRpcHandler_EnablesRpcFunctionality()
    {
        // Arrange & Act - done in SetUp

        // Assert
        Assert.That(_terminal.IsRpcEnabled, Is.True);
        Assert.That(_terminal.RpcHandler, Is.EqualTo(_mockRpcHandler));
    }

    [Test]
    public void Constructor_WithoutRpcHandler_DisablesRpcFunctionality()
    {
        // Arrange & Act
        using var terminal = TerminalEmulator.Create(80, 24, 1000, _logger, null);

        // Assert
        Assert.That(terminal.IsRpcEnabled, Is.False);
        Assert.That(terminal.RpcHandler, Is.Null);
    }

    [Test]
    public void SetRpcEnabled_WithRpcHandler_ChangesEnabledState()
    {
        // Arrange
        Assert.That(_terminal.IsRpcEnabled, Is.True);

        // Act - disable RPC
        bool result1 = _terminal.SetRpcEnabled(false);

        // Assert
        Assert.That(result1, Is.True);
        Assert.That(_terminal.IsRpcEnabled, Is.False);
        Assert.That(_mockRpcHandler.IsEnabled, Is.False);

        // Act - re-enable RPC
        bool result2 = _terminal.SetRpcEnabled(true);

        // Assert
        Assert.That(result2, Is.True);
        Assert.That(_terminal.IsRpcEnabled, Is.True);
        Assert.That(_mockRpcHandler.IsEnabled, Is.True);
    }

    [Test]
    public void SetRpcEnabled_WithoutRpcHandler_ReturnsFalse()
    {
        // Arrange
        using var terminal = TerminalEmulator.Create(80, 24, 1000, _logger, null);

        // Act
        bool result = terminal.SetRpcEnabled(true);

        // Assert
        Assert.That(result, Is.False);
        Assert.That(terminal.IsRpcEnabled, Is.False);
    }

    [Test]
    public void Write_WithValidRpcSequence_CallsRpcHandler()
    {
        // Arrange
        string rpcSequence = "\x1b[>1001;1;F"; // ESC [ > 1001 ; 1 ; F (ignite engine command)

        // Act
        _terminal.Write(rpcSequence);

        // Assert
        Assert.That(_mockRpcHandler.ReceivedMessages, Has.Count.EqualTo(1));
        var message = _mockRpcHandler.ReceivedMessages[0];
        Assert.That(message.CommandId, Is.EqualTo(1001));
        Assert.That(message.Version, Is.EqualTo(1));
        Assert.That(message.CommandType, Is.EqualTo(RpcCommandType.FireAndForget));
    }

    [Test]
    public void Write_WithRpcDisabled_DoesNotCallRpcHandler()
    {
        // Arrange
        _terminal.SetRpcEnabled(false);
        string rpcSequence = "\x1b[>1001;1;F"; // ESC [ > 1001 ; 1 ; F

        // Act
        _terminal.Write(rpcSequence);

        // Assert
        Assert.That(_mockRpcHandler.ReceivedMessages, Has.Count.EqualTo(0));
    }

    [Test]
    public void Write_WithMalformedRpcSequence_CallsMalformedHandler()
    {
        // Arrange
        string malformedSequence = "\x1b[>999;1;F"; // Invalid command ID (below 1000)

        // Act
        _terminal.Write(malformedSequence);

        // Assert
        Assert.That(_mockRpcHandler.MalformedSequences, Has.Count.EqualTo(1));
        var (sequence, type) = _mockRpcHandler.MalformedSequences[0];
        Assert.That(type, Is.EqualTo(RpcSequenceType.InvalidCommandId));
    }

    [Test]
    public void Write_WithStandardTerminalSequence_DoesNotAffectRpcHandler()
    {
        // Arrange
        string standardSequence = "\x1b[2J"; // Clear screen (standard CSI sequence)

        // Act
        _terminal.Write(standardSequence);

        // Assert
        Assert.That(_mockRpcHandler.ReceivedMessages, Has.Count.EqualTo(0));
        Assert.That(_mockRpcHandler.MalformedSequences, Has.Count.EqualTo(0));
    }

    private class MockRpcHandler : IRpcHandler
    {
        public List<RpcMessage> ReceivedMessages { get; } = new();
        public List<(byte[] Sequence, RpcSequenceType Type)> MalformedSequences { get; } = new();
        public bool IsEnabled { get; set; } = true;

        public void HandleRpcMessage(RpcMessage message)
        {
            ReceivedMessages.Add(message);
        }

        public void HandleMalformedRpcSequence(ReadOnlySpan<byte> rawSequence, RpcSequenceType sequenceType)
        {
            MalformedSequences.Add((rawSequence.ToArray(), sequenceType));
        }
    }

    private class TestLogger : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }
}