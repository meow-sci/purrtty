using System.Text.Json;
using caTTY.Core.Rpc;
using caTTY.Core.Types;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace caTTY.Core.Tests.Unit.Rpc;

/// <summary>
/// Unit tests for OSC-based RPC commands infrastructure.
/// Tests the OSC sequence format: ESC ] {command} ; {payload} BEL/ST
/// Uses TestOscRpcHandler test double to verify infrastructure without game-specific logic.
/// </summary>
[TestFixture]
[Category("Unit")]
public class OscRpcHandlerTests
{
    private TestOscRpcHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _handler = new TestOscRpcHandler(NullLogger.Instance);
    }

    #region IsPrivateCommand Tests

    [Test]
    public void IsPrivateCommand_Command1010_ReturnsTrue()
    {
        Assert.That(_handler.IsPrivateCommand(1010), Is.True);
    }

    [Test]
    public void IsPrivateCommand_Command1000_ReturnsTrue()
    {
        Assert.That(_handler.IsPrivateCommand(1000), Is.True);
    }

    [Test]
    public void IsPrivateCommand_Command999_ReturnsFalse()
    {
        Assert.That(_handler.IsPrivateCommand(999), Is.False);
    }

    [Test]
    public void IsPrivateCommand_StandardOscCommand_ReturnsFalse()
    {
        // OSC 0, 1, 2 are standard title commands
        Assert.That(_handler.IsPrivateCommand(0), Is.False);
        Assert.That(_handler.IsPrivateCommand(1), Is.False);
        Assert.That(_handler.IsPrivateCommand(2), Is.False);
        // OSC 52 is clipboard
        Assert.That(_handler.IsPrivateCommand(52), Is.False);
    }

    #endregion

    #region HandleCommand Tests

    [Test]
    public void HandleCommand_ValidJsonAction_CallsDispatchAction()
    {
        // Arrange
        string payload = "{\"action\":\"engine_ignite\"}";

        // Act
        _handler.HandleCommand(OscRpcHandler.JsonActionCommand, payload);

        // Assert
        Assert.That(_handler.DispatchedActions, Has.Count.EqualTo(1));
        Assert.That(_handler.DispatchedActions[0].action, Is.EqualTo("engine_ignite"));
    }

    [Test]
    public void HandleCommand_MultipleActions_CallsDispatchActionMultipleTimes()
    {
        // Arrange
        string payload1 = "{\"action\":\"engine_ignite\"}";
        string payload2 = "{\"action\":\"engine_shutdown\"}";

        // Act
        _handler.HandleCommand(OscRpcHandler.JsonActionCommand, payload1);
        _handler.HandleCommand(OscRpcHandler.JsonActionCommand, payload2);

        // Assert
        Assert.That(_handler.DispatchedActions, Has.Count.EqualTo(2));
        Assert.That(_handler.DispatchedActions[0].action, Is.EqualTo("engine_ignite"));
        Assert.That(_handler.DispatchedActions[1].action, Is.EqualTo("engine_shutdown"));
    }

    [Test]
    public void HandleCommand_ActionWithAdditionalProperties_PassesRootElement()
    {
        // Arrange
        string payload = "{\"action\":\"set_throttle\",\"value\":75}";

        // Act
        _handler.HandleCommand(OscRpcHandler.JsonActionCommand, payload);

        // Assert
        Assert.That(_handler.DispatchedActions, Has.Count.EqualTo(1));
        Assert.That(_handler.DispatchedActions[0].action, Is.EqualTo("set_throttle"));
        Assert.That(_handler.DispatchedActions[0].root.TryGetProperty("value", out var valueProp), Is.True);
        Assert.That(valueProp.GetInt32(), Is.EqualTo(75));
    }

    [Test]
    public void HandleCommand_EmptyPayload_DoesNotCallDispatchAction()
    {
        // Act
        _handler.HandleCommand(OscRpcHandler.JsonActionCommand, null);
        _handler.HandleCommand(OscRpcHandler.JsonActionCommand, "");

        // Assert - should handle gracefully without calling dispatch
        Assert.That(_handler.DispatchedActions, Is.Empty);
    }

    [Test]
    public void HandleCommand_InvalidJson_DoesNotCallDispatchAction()
    {
        // Arrange
        string payload = "not valid json";

        // Act
        _handler.HandleCommand(OscRpcHandler.JsonActionCommand, payload);

        // Assert - should handle gracefully without calling dispatch
        Assert.That(_handler.DispatchedActions, Is.Empty);
    }

    [Test]
    public void HandleCommand_MissingActionProperty_DoesNotCallDispatchAction()
    {
        // Arrange
        string payload = "{\"something\":\"else\"}";

        // Act
        _handler.HandleCommand(OscRpcHandler.JsonActionCommand, payload);

        // Assert - should handle gracefully without calling dispatch
        Assert.That(_handler.DispatchedActions, Is.Empty);
    }

    [Test]
    public void HandleCommand_EmptyActionValue_DoesNotCallDispatchAction()
    {
        // Arrange
        string payload = "{\"action\":\"\"}";

        // Act
        _handler.HandleCommand(OscRpcHandler.JsonActionCommand, payload);

        // Assert - should handle gracefully without calling dispatch
        Assert.That(_handler.DispatchedActions, Is.Empty);
    }

    [Test]
    public void HandleCommand_UnknownCommand_DoesNotCallDispatchAction()
    {
        // Arrange
        int unknownCommand = 9999;
        string payload = "{\"action\":\"test\"}";

        // Act
        _handler.HandleCommand(unknownCommand, payload);

        // Assert - unknown commands are ignored
        Assert.That(_handler.DispatchedActions, Is.Empty);
    }

    #endregion

    #region Constants Tests

    [Test]
    public void JsonActionCommand_Is1010()
    {
        Assert.That(OscRpcHandler.JsonActionCommand, Is.EqualTo(1010));
    }

    #endregion

    #region OSC Message Parsing Integration Tests

    [Test]
    public void OscPrivateMessage_Command1010_HasCorrectType()
    {
        // Arrange - this is how the OscParser would create the message
        var message = new XtermOscMessage
        {
            Type = "osc.private",
            Raw = "\x1b]1010;{\"action\":\"engine_ignite\"}\x07",
            Terminator = "BEL",
            Command = 1010,
            Payload = "{\"action\":\"engine_ignite\"}",
            Implemented = true
        };

        // Assert - type should be "osc.private" for all private-use commands
        Assert.That(message.Type, Is.EqualTo("osc.private"));
        Assert.That(message.Command, Is.EqualTo(1010));
        Assert.That(_handler.IsPrivateCommand(message.Command), Is.True);
    }

    [Test]
    public void OscPrivateMessage_PrivateRange_StartsAt1000()
    {
        // Private-use OSC commands start at 1000
        const int privateRangeStart = 1000;

        Assert.That(_handler.IsPrivateCommand(privateRangeStart), Is.True);
        Assert.That(_handler.IsPrivateCommand(privateRangeStart - 1), Is.False);
    }

    #endregion
}

/// <summary>
/// Test double for OscRpcHandler that captures dispatched actions for verification.
/// Used to test OscRpcHandler infrastructure without game-specific dependencies.
/// </summary>
internal class TestOscRpcHandler : OscRpcHandler
{
    public List<(string action, JsonElement root)> DispatchedActions { get; } = new();

    public TestOscRpcHandler(ILogger logger) : base(logger) { }

    protected override void DispatchAction(string action, JsonElement root)
    {
        // Clone the JsonElement to prevent it from being disposed
        using var doc = JsonDocument.Parse(root.GetRawText());
        DispatchedActions.Add((action, doc.RootElement.Clone()));
    }
}
