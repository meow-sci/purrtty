using NUnit.Framework;
using caTTY.Core.Rpc;

namespace caTTY.Core.Tests.Unit.Rpc;

[TestFixture]
[Category("Unit")]
public class QueryCommandHandlerTests
{
    [Test]
    public void QueryCommandHandler_Constructor_SetsPropertiesCorrectly()
    {
        // Arrange & Act
        var handler = new TestQueryHandler("Test Query Command", TimeSpan.FromSeconds(10));

        // Assert
        Assert.That(handler.Description, Is.EqualTo("Test Query Command"));
        Assert.That(handler.IsFireAndForget, Is.False);
        Assert.That(handler.Timeout, Is.EqualTo(TimeSpan.FromSeconds(10)));
    }

    [Test]
    public void QueryCommandHandler_Constructor_WithDefaultTimeout_SetsDefaultValue()
    {
        // Arrange & Act
        var handler = new TestQueryHandler("Test Query Command");

        // Assert
        Assert.That(handler.Timeout, Is.EqualTo(TimeSpan.FromSeconds(5)));
    }

    [Test]
    public async Task QueryCommandHandler_ExecuteAsync_ReturnsQueryResult()
    {
        // Arrange
        var expectedResult = new { status = "active", value = 75 };
        var handler = new TestQueryHandler("Test Command", result: expectedResult);
        var parameters = new RpcParameters();

        // Act
        var result = await handler.ExecuteAsync(parameters);

        // Assert
        Assert.That(result, Is.EqualTo(expectedResult));
    }

    [Test]
    public async Task QueryCommandHandler_ExecuteAsync_CallsExecuteQueryAsync()
    {
        // Arrange
        var handler = new TestQueryHandler("Test Command");
        var parameters = new RpcParameters();

        // Act
        await handler.ExecuteAsync(parameters);

        // Assert
        Assert.That(handler.ExecuteQueryAsyncCalled, Is.True);
        Assert.That(handler.ReceivedParameters, Is.EqualTo(parameters));
    }

    [Test]
    public async Task QueryCommandHandler_ExecuteAsync_WithSynchronousImplementation_CallsExecuteQuery()
    {
        // Arrange
        var handler = new TestSynchronousQueryHandler("Test Command");
        var parameters = new RpcParameters();

        // Act
        await handler.ExecuteAsync(parameters);

        // Assert
        Assert.That(handler.ExecuteQueryCalled, Is.True);
        Assert.That(handler.ReceivedParameters, Is.EqualTo(parameters));
    }

    [Test]
    public void QueryCommandHandler_ExecuteAsync_WithInvalidParameters_ThrowsArgumentException()
    {
        // Arrange
        var handler = new TestQueryHandler("Test Command");

        // Act & Assert
        var ex = Assert.ThrowsAsync<ArgumentNullException>(async () => 
            await handler.ExecuteAsync(null!));
        Assert.That(ex.ParamName, Is.EqualTo("parameters"));
    }

    [Test]
    public void QueryCommandHandler_CreateResponse_ReturnsStructuredResponse()
    {
        // Arrange & Act
        var response = TestQueryHandler.TestCreateResponse("active", 75, new { extra = "data" });

        // Assert
        Assert.That(response, Is.TypeOf<Dictionary<string, object?>>());
        var dict = (Dictionary<string, object?>)response;
        Assert.That(dict["status"], Is.EqualTo("active"));
        Assert.That(dict["value"], Is.EqualTo(75));
        Assert.That(dict["data"], Is.Not.Null);
    }

    [Test]
    public void QueryCommandHandler_CreateValueResponse_ReturnsValueWrapper()
    {
        // Arrange & Act
        var response = TestQueryHandler.TestCreateValueResponse(42);

        // Assert
        Assert.That(response, Has.Property("value").EqualTo(42));
    }

    [Test]
    public void QueryCommandHandler_CreateErrorResponse_ReturnsErrorStructure()
    {
        // Arrange & Act
        var response = TestQueryHandler.TestCreateErrorResponse("Something went wrong", 500);

        // Assert
        Assert.That(response, Is.TypeOf<Dictionary<string, object?>>());
        var dict = (Dictionary<string, object?>)response;
        Assert.That(dict["error"], Is.EqualTo("Something went wrong"));
        Assert.That(dict["errorCode"], Is.EqualTo(500));
    }

    // Test helper classes
    private class TestQueryHandler : QueryCommandHandler
    {
        private readonly object? _result;
        public bool ExecuteQueryAsyncCalled { get; private set; }
        public RpcParameters? ReceivedParameters { get; private set; }

        public TestQueryHandler(string description, TimeSpan timeout = default, object? result = null) 
            : base(description, timeout)
        {
            _result = result;
        }

        protected override Task<object?> ExecuteQueryAsync(RpcParameters parameters)
        {
            ExecuteQueryAsyncCalled = true;
            ReceivedParameters = parameters;
            return Task.FromResult(_result);
        }

        // Expose protected static methods for testing
        public static object TestCreateResponse(string status, object? value, object? additionalData = null)
        {
            return CreateResponse(status, value, additionalData);
        }

        public static object TestCreateValueResponse(object? value)
        {
            return CreateValueResponse(value);
        }

        public static object TestCreateErrorResponse(string errorMessage, int? errorCode = null)
        {
            return CreateErrorResponse(errorMessage, errorCode);
        }
    }

    private class TestSynchronousQueryHandler : QueryCommandHandler
    {
        public bool ExecuteQueryCalled { get; private set; }
        public RpcParameters? ReceivedParameters { get; private set; }

        public TestSynchronousQueryHandler(string description) : base(description)
        {
        }

        protected override object? ExecuteQuery(RpcParameters parameters)
        {
            ExecuteQueryCalled = true;
            ReceivedParameters = parameters;
            return "sync result";
        }
    }
}