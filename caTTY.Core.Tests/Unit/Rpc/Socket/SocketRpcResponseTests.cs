using caTTY.Core.Rpc.Socket;
using NUnit.Framework;

namespace caTTY.Core.Tests.Unit.Rpc.Socket;

[TestFixture]
[Category("Unit")]
public class SocketRpcResponseTests
{
    [Test]
    public void Ok_WithoutData_CreatesSuccessResponse()
    {
        // Act
        var response = SocketRpcResponse.Ok();

        // Assert
        Assert.That(response.Success, Is.True);
        Assert.That(response.Data, Is.Null);
        Assert.That(response.Error, Is.Null);
    }

    [Test]
    public void Ok_WithData_IncludesData()
    {
        // Arrange
        var data = new { name = "Rocket-1" };

        // Act
        var response = SocketRpcResponse.Ok(data);

        // Assert
        Assert.That(response.Success, Is.True);
        Assert.That(response.Data, Is.EqualTo(data));
        Assert.That(response.Error, Is.Null);
    }

    [Test]
    public void Fail_CreatesErrorResponse()
    {
        // Act
        var response = SocketRpcResponse.Fail("Something went wrong");

        // Assert
        Assert.That(response.Success, Is.False);
        Assert.That(response.Data, Is.Null);
        Assert.That(response.Error, Is.EqualTo("Something went wrong"));
    }

    [Test]
    public void Ok_WithNullData_CreatesSuccessResponseWithNullData()
    {
        // Act
        var response = SocketRpcResponse.Ok(null);

        // Assert
        Assert.That(response.Success, Is.True);
        Assert.That(response.Data, Is.Null);
        Assert.That(response.Error, Is.Null);
    }
}
