using System.Text.Json;
using caTTY.Core.Rpc.Socket;
using caTTY.TermSequenceRpc.SocketRpc;
using caTTY.TermSequenceRpc.SocketRpc.Actions;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace caTTY.TermSequenceRpc.Tests.Unit.SocketRpc;

/// <summary>
/// Unit tests for KSA socket RPC handler.
/// Tests action registration, routing, and error handling without requiring actual KSA game context.
/// </summary>
[TestFixture]
[Category("Unit")]
public class KsaSocketRpcHandlerTests
{
    private KsaSocketRpcHandler _handler = null!;

    [SetUp]
    public void SetUp()
    {
        _handler = new KsaSocketRpcHandler(NullLogger.Instance);
    }

    #region Instantiation Tests

    [Test]
    public void Constructor_WithLogger_DoesNotThrow()
    {
        Assert.DoesNotThrow(() => new KsaSocketRpcHandler(NullLogger.Instance));
    }

    [Test]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new KsaSocketRpcHandler(null!));
    }

    #endregion

    #region Unknown/Missing Action Handling

    [Test]
    public void HandleRequest_UnknownAction_ReturnsErrorResponse()
    {
        // Arrange
        var request = new SocketRpcRequest { Action = "unknown-action" };

        // Act
        var response = _handler.HandleRequest(request);

        // Assert
        Assert.That(response.Success, Is.False);
        Assert.That(response.Error, Does.Contain("Unknown action"));
        Assert.That(response.Error, Does.Contain("unknown-action"));
    }

    [Test]
    public void HandleRequest_EmptyAction_ReturnsErrorResponse()
    {
        // Arrange
        var request = new SocketRpcRequest { Action = "" };

        // Act
        var response = _handler.HandleRequest(request);

        // Assert
        Assert.That(response.Success, Is.False);
        Assert.That(response.Error, Does.Contain("Missing action"));
    }

    [Test]
    public void HandleRequest_NullAction_ReturnsErrorResponse()
    {
        // Arrange
        var request = new SocketRpcRequest { Action = null! };

        // Act
        var response = _handler.HandleRequest(request);

        // Assert
        Assert.That(response.Success, Is.False);
        Assert.That(response.Error, Does.Contain("Missing action"));
    }

    #endregion

    #region Custom Action Registration

    [Test]
    public void RegisterAction_CustomAction_CanBeInvoked()
    {
        // Arrange
        var customAction = new TestAction("custom-test", SocketRpcResponse.Ok("custom-result"));
        _handler.RegisterAction(customAction);

        var request = new SocketRpcRequest { Action = "custom-test" };

        // Act
        var response = _handler.HandleRequest(request);

        // Assert
        Assert.That(response.Success, Is.True);
        Assert.That(response.Data, Is.EqualTo("custom-result"));
    }

    [Test]
    public void RegisterAction_MultipleCustomActions_AllCanBeInvoked()
    {
        // Arrange
        var action1 = new TestAction("action-one", SocketRpcResponse.Ok("result-one"));
        var action2 = new TestAction("action-two", SocketRpcResponse.Ok("result-two"));
        _handler.RegisterAction(action1);
        _handler.RegisterAction(action2);

        // Act
        var response1 = _handler.HandleRequest(new SocketRpcRequest { Action = "action-one" });
        var response2 = _handler.HandleRequest(new SocketRpcRequest { Action = "action-two" });

        // Assert
        Assert.That(response1.Success, Is.True);
        Assert.That(response1.Data, Is.EqualTo("result-one"));
        Assert.That(response2.Success, Is.True);
        Assert.That(response2.Data, Is.EqualTo("result-two"));
    }

    #endregion

    #region Action Overwriting Behavior

    [Test]
    public void RegisterAction_SameActionNameTwice_OverwritesExistingAction()
    {
        // Arrange
        var action1 = new TestAction("test-action", SocketRpcResponse.Ok("first"));
        var action2 = new TestAction("test-action", SocketRpcResponse.Ok("second"));

        _handler.RegisterAction(action1);
        _handler.RegisterAction(action2);

        var request = new SocketRpcRequest { Action = "test-action" };

        // Act
        var response = _handler.HandleRequest(request);

        // Assert
        Assert.That(response.Success, Is.True);
        Assert.That(response.Data, Is.EqualTo("second"), 
            "Second registered action should overwrite the first");
    }

    [Test]
    public void RegisterAction_OverwriteDefaultAction_UsesCustomImplementation()
    {
        // Arrange - default action is "list-crafts"
        var customListCrafts = new TestAction("list-crafts", SocketRpcResponse.Ok("overridden"));
        _handler.RegisterAction(customListCrafts);

        var request = new SocketRpcRequest { Action = "list-crafts" };

        // Act
        var response = _handler.HandleRequest(request);

        // Assert
        Assert.That(response.Success, Is.True);
        Assert.That(response.Data, Is.EqualTo("overridden"), 
            "Custom action should override default implementation");
    }

    #endregion

    #region Case-Insensitive Action Names

    [Test]
    public void HandleRequest_ActionNameIsCaseInsensitive_LowercaseQuery()
    {
        // Arrange
        var customAction = new TestAction("My-Action", SocketRpcResponse.Ok("found"));
        _handler.RegisterAction(customAction);

        var request = new SocketRpcRequest { Action = "my-action" }; // lowercase

        // Act
        var response = _handler.HandleRequest(request);

        // Assert
        Assert.That(response.Success, Is.True);
        Assert.That(response.Data, Is.EqualTo("found"));
    }

    [Test]
    public void HandleRequest_ActionNameIsCaseInsensitive_UppercaseQuery()
    {
        // Arrange
        var customAction = new TestAction("test-action", SocketRpcResponse.Ok("found"));
        _handler.RegisterAction(customAction);

        var request = new SocketRpcRequest { Action = "TEST-ACTION" }; // uppercase

        // Act
        var response = _handler.HandleRequest(request);

        // Assert
        Assert.That(response.Success, Is.True);
        Assert.That(response.Data, Is.EqualTo("found"));
    }

    [Test]
    public void HandleRequest_ActionNameIsCaseInsensitive_MixedCaseQuery()
    {
        // Arrange
        var customAction = new TestAction("MyAction", SocketRpcResponse.Ok("found"));
        _handler.RegisterAction(customAction);

        var request = new SocketRpcRequest { Action = "myACTION" }; // mixed case

        // Act
        var response = _handler.HandleRequest(request);

        // Assert
        Assert.That(response.Success, Is.True);
        Assert.That(response.Data, Is.EqualTo("found"));
    }

    #endregion

    #region Parameter Passing to Actions

    [Test]
    public void HandleRequest_WithNoParams_PassesNullToAction()
    {
        // Arrange
        JsonElement? receivedParams = null;
        var customAction = new TestAction("test-params", p =>
        {
            receivedParams = p;
            return SocketRpcResponse.Ok();
        });
        _handler.RegisterAction(customAction);

        var request = new SocketRpcRequest { Action = "test-params", Params = null };

        // Act
        _handler.HandleRequest(request);

        // Assert
        Assert.That(receivedParams, Is.Null);
    }

    [Test]
    public void HandleRequest_WithSimpleParams_PassesParamsToAction()
    {
        // Arrange
        JsonElement? receivedParams = null;
        var customAction = new TestAction("test-params", p =>
        {
            receivedParams = p;
            return SocketRpcResponse.Ok();
        });
        _handler.RegisterAction(customAction);

        var paramsJson = JsonDocument.Parse("{\"key\":\"value\"}").RootElement;
        var request = new SocketRpcRequest { Action = "test-params", Params = paramsJson };

        // Act
        _handler.HandleRequest(request);

        // Assert
        Assert.That(receivedParams, Is.Not.Null);
        Assert.That(receivedParams!.Value.GetProperty("key").GetString(), Is.EqualTo("value"));
    }

    [Test]
    public void HandleRequest_WithComplexParams_PassesParamsToAction()
    {
        // Arrange
        JsonElement? receivedParams = null;
        var customAction = new TestAction("test-params", p =>
        {
            receivedParams = p;
            return SocketRpcResponse.Ok();
        });
        _handler.RegisterAction(customAction);

        var paramsJson = JsonDocument.Parse("{\"craftId\":42,\"throttle\":0.75}").RootElement;
        var request = new SocketRpcRequest { Action = "test-params", Params = paramsJson };

        // Act
        _handler.HandleRequest(request);

        // Assert
        Assert.That(receivedParams, Is.Not.Null);
        Assert.That(receivedParams!.Value.GetProperty("craftId").GetInt32(), Is.EqualTo(42));
        Assert.That(receivedParams!.Value.GetProperty("throttle").GetDouble(), Is.EqualTo(0.75).Within(0.001));
    }

    [Test]
    public void HandleRequest_WithNestedParams_PassesParamsToAction()
    {
        // Arrange
        JsonElement? receivedParams = null;
        var customAction = new TestAction("test-params", p =>
        {
            receivedParams = p;
            return SocketRpcResponse.Ok();
        });
        _handler.RegisterAction(customAction);

        var paramsJson = JsonDocument.Parse("{\"craft\":{\"id\":1,\"name\":\"Rocket\"}}").RootElement;
        var request = new SocketRpcRequest { Action = "test-params", Params = paramsJson };

        // Act
        _handler.HandleRequest(request);

        // Assert
        Assert.That(receivedParams, Is.Not.Null);
        var craftProperty = receivedParams!.Value.GetProperty("craft");
        Assert.That(craftProperty.GetProperty("id").GetInt32(), Is.EqualTo(1));
        Assert.That(craftProperty.GetProperty("name").GetString(), Is.EqualTo("Rocket"));
    }

    [Test]
    public void HandleRequest_ActionUsesParams_ReturnsCorrectResponse()
    {
        // Arrange
        var customAction = new TestAction("echo", p =>
        {
            if (p == null)
            {
                return SocketRpcResponse.Fail("No params provided");
            }
            var message = p.Value.GetProperty("message").GetString();
            return SocketRpcResponse.Ok(new { echo = message });
        });
        _handler.RegisterAction(customAction);

        var paramsJson = JsonDocument.Parse("{\"message\":\"Hello World\"}").RootElement;
        var request = new SocketRpcRequest { Action = "echo", Params = paramsJson };

        // Act
        var response = _handler.HandleRequest(request);

        // Assert
        Assert.That(response.Success, Is.True);
        // Response.Data is a JsonElement, need to convert to access properties
        var dataElement = JsonSerializer.SerializeToElement(response.Data);
        Assert.That(dataElement.GetProperty("echo").GetString(), Is.EqualTo("Hello World"));
    }

    #endregion

    #region Default Actions Verification

    [Test]
    public void Constructor_RegistersDefaultActions()
    {
        // Verify that list-crafts exists (will fail with "Unknown action" if not registered)
        var request = new SocketRpcRequest { Action = "list-crafts" };
        var response = _handler.HandleRequest(request);

        // Should not return "Unknown action" error (might return other errors due to missing KSA context)
        if (response.Error != null)
        {
            Assert.That(response.Error, Does.Not.Contain("Unknown action"));
        }
    }

    [Test]
    public void Constructor_RegistersGetCurrentCraftAction()
    {
        // Verify that get-current-craft exists
        var request = new SocketRpcRequest { Action = "get-current-craft" };
        var response = _handler.HandleRequest(request);

        // Should not return "Unknown action" error
        if (response.Error != null)
        {
            Assert.That(response.Error, Does.Not.Contain("Unknown action"));
        }
    }

    #endregion

    #region Test Helper Classes

    /// <summary>
    /// Test implementation of ISocketRpcAction for unit testing.
    /// </summary>
    private class TestAction : ISocketRpcAction
    {
        private readonly SocketRpcResponse _response;
        private readonly Func<JsonElement?, SocketRpcResponse>? _executeFunc;

        public string ActionName { get; }

        public TestAction(string actionName, SocketRpcResponse response)
        {
            ActionName = actionName;
            _response = response;
        }

        public TestAction(string actionName, Func<JsonElement?, SocketRpcResponse> executeFunc)
        {
            ActionName = actionName;
            _executeFunc = executeFunc;
            _response = SocketRpcResponse.Ok(); // fallback
        }

        public SocketRpcResponse Execute(JsonElement? @params)
        {
            return _executeFunc?.Invoke(@params) ?? _response;
        }
    }

    #endregion
}
