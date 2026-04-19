using caTTY.Core.Rpc;
using caTTY.TermSequenceRpc.VehicleCommands;
using NUnit.Framework;

namespace caTTY.TermSequenceRpc.Tests.Unit;

/// <summary>
/// Unit tests for KSA-specific vehicle command implementations.
/// Tests the IgniteMainThrottle, ShutdownMainEngine, and GetThrottleStatus commands.
/// </summary>
[TestFixture]
[Category("Unit")]
public class VehicleCommandTests
{
    private RpcParameters _emptyParameters = null!;

    [SetUp]
    public void SetUp()
    {
        _emptyParameters = new RpcParameters();
    }

    #region IgniteMainThrottle Command Tests

    [Test]
    public void IgniteMainThrottleCommand_ShouldBeFireAndForget()
    {
        // Arrange & Act
        var command = new IgniteMainThrottleCommand();

        // Assert
        Assert.That(command.IsFireAndForget, Is.True, "IgniteMainThrottle should be a fire-and-forget command");
        Assert.That(command.Timeout, Is.EqualTo(TimeSpan.Zero), "Fire-and-forget commands should have zero timeout");
    }

    [Test]
    public void IgniteMainThrottleCommand_ShouldHaveCorrectDescription()
    {
        // Arrange & Act
        var command = new IgniteMainThrottleCommand();

        // Assert
        Assert.That(command.Description, Is.EqualTo("Ignite Main Throttle"), "Command should have correct description");
    }

    [Test]
    public async Task IgniteMainThrottleCommand_ExecuteAsync_ShouldReturnNull()
    {
        // Arrange
        var command = new IgniteMainThrottleCommand();

        // Act
        var result = await command.ExecuteAsync(_emptyParameters);

        // Assert
        Assert.That(result, Is.Null, "Fire-and-forget commands should return null");
    }

    [Test]
    public async Task IgniteMainThrottleCommand_ExecuteAsync_ShouldAcceptEmptyParameters()
    {
        // Arrange
        var command = new IgniteMainThrottleCommand();

        // Act & Assert
        Assert.DoesNotThrowAsync(async () => await command.ExecuteAsync(_emptyParameters),
            "Command should accept empty parameters");
    }

    #endregion

    #region ShutdownMainEngine Command Tests

    [Test]
    public void ShutdownMainEngineCommand_ShouldBeFireAndForget()
    {
        // Arrange & Act
        var command = new ShutdownMainEngineCommand();

        // Assert
        Assert.That(command.IsFireAndForget, Is.True, "ShutdownMainEngine should be a fire-and-forget command");
        Assert.That(command.Timeout, Is.EqualTo(TimeSpan.Zero), "Fire-and-forget commands should have zero timeout");
    }

    [Test]
    public void ShutdownMainEngineCommand_ShouldHaveCorrectDescription()
    {
        // Arrange & Act
        var command = new ShutdownMainEngineCommand();

        // Assert
        Assert.That(command.Description, Is.EqualTo("Shutdown Main Engine"), "Command should have correct description");
    }

    [Test]
    public async Task ShutdownMainEngineCommand_ExecuteAsync_ShouldReturnNull()
    {
        // Arrange
        var command = new ShutdownMainEngineCommand();

        // Act
        var result = await command.ExecuteAsync(_emptyParameters);

        // Assert
        Assert.That(result, Is.Null, "Fire-and-forget commands should return null");
    }

    [Test]
    public async Task ShutdownMainEngineCommand_ExecuteAsync_ShouldAcceptEmptyParameters()
    {
        // Arrange
        var command = new ShutdownMainEngineCommand();

        // Act & Assert
        Assert.DoesNotThrowAsync(async () => await command.ExecuteAsync(_emptyParameters),
            "Command should accept empty parameters");
    }

    #endregion

    #region GetThrottleStatus Query Tests

    [Test]
    public void GetThrottleStatusQuery_ShouldBeQueryCommand()
    {
        // Arrange & Act
        var command = new GetThrottleStatusQuery();

        // Assert
        Assert.That(command.IsFireAndForget, Is.False, "GetThrottleStatus should be a query command");
        Assert.That(command.Timeout, Is.EqualTo(TimeSpan.FromSeconds(3)), "Query should have 3-second timeout");
    }

    [Test]
    public void GetThrottleStatusQuery_ShouldHaveCorrectDescription()
    {
        // Arrange & Act
        var command = new GetThrottleStatusQuery();

        // Assert
        Assert.That(command.Description, Is.EqualTo("Get Throttle Status"), "Command should have correct description");
    }

    [Test]
    public async Task GetThrottleStatusQuery_ExecuteAsync_ShouldReturnThrottleData()
    {
        // Arrange
        var command = new GetThrottleStatusQuery();

        // Act
        var result = await command.ExecuteAsync(_emptyParameters);

        // Assert
        Assert.That(result, Is.Not.Null, "Query should return data");

        // Verify the structure of the returned data
        var resultDict = result as Dictionary<string, object?>;
        Assert.That(resultDict, Is.Not.Null, "Result should be a dictionary");
        Assert.That(resultDict!.ContainsKey("status"), Is.True, "Result should contain status");
        Assert.That(resultDict.ContainsKey("value"), Is.True, "Result should contain value");
        Assert.That(resultDict.ContainsKey("data"), Is.True, "Result should contain additional data");

        // Verify the mock data values
        Assert.That(resultDict["status"], Is.EqualTo("enabled"), "Status should be 'enabled'");
        Assert.That(resultDict["value"], Is.EqualTo(75), "Value should be 75 (mock throttle level)");

        // Verify the additional data structure
        var additionalData = resultDict["data"];
        Assert.That(additionalData, Is.Not.Null, "Additional data should not be null");
    }

    [Test]
    public async Task GetThrottleStatusQuery_ExecuteAsync_ShouldAcceptEmptyParameters()
    {
        // Arrange
        var command = new GetThrottleStatusQuery();

        // Act & Assert
        Assert.DoesNotThrowAsync(async () => await command.ExecuteAsync(_emptyParameters),
            "Query should accept empty parameters");
    }

    [Test]
    public async Task GetThrottleStatusQuery_ExecuteAsync_ShouldReturnConsistentMockData()
    {
        // Arrange
        var command = new GetThrottleStatusQuery();

        // Act - Execute multiple times
        var result1 = await command.ExecuteAsync(_emptyParameters);
        var result2 = await command.ExecuteAsync(_emptyParameters);

        // Assert - Results should be consistent (same mock data)
        Assert.That(result1, Is.Not.Null);
        Assert.That(result2, Is.Not.Null);

        var dict1 = result1 as Dictionary<string, object?>;
        var dict2 = result2 as Dictionary<string, object?>;

        Assert.That(dict1!["status"], Is.EqualTo(dict2!["status"]), "Status should be consistent");
        Assert.That(dict1["value"], Is.EqualTo(dict2["value"]), "Value should be consistent");
    }

    #endregion
}
