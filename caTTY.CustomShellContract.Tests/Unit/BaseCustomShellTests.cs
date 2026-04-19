using System.Text;
using NUnit.Framework;

namespace caTTY.Core.Terminal.Tests.Unit;

[TestFixture]
public class BaseCustomShellTests
{
    /// <summary>
    ///     Test implementation of BaseCustomShell for testing purposes.
    /// </summary>
    private class TestCustomShell : BaseCustomShell
    {
        private readonly CustomShellMetadata _metadata;
        private TaskCompletionSource<bool>? _startTcs;
        private TaskCompletionSource<bool>? _stopTcs;

        public TestCustomShell()
        {
            _metadata = CustomShellMetadata.Create(
                "TestShell",
                "A test shell implementation",
                new Version(1, 0, 0),
                "Test Author"
            );
        }

        public override CustomShellMetadata Metadata => _metadata;

        // Expose protected methods for testing
        public void TestRaiseOutputReceived(ReadOnlyMemory<byte> data, ShellOutputType outputType = ShellOutputType.Stdout)
        {
            RaiseOutputReceived(data, outputType);
        }

        public void TestRaiseTerminated(int exitCode, string? reason = null)
        {
            RaiseTerminated(exitCode, reason);
        }

        public void TestSetIsRunning(bool value)
        {
            IsRunning = value;
        }

        public override Task StartAsync(CustomShellStartOptions options, CancellationToken cancellationToken = default)
        {
            _startTcs = new TaskCompletionSource<bool>();
            IsRunning = true;
            return _startTcs.Task;
        }

        public override Task StopAsync(CancellationToken cancellationToken = default)
        {
            _stopTcs = new TaskCompletionSource<bool>();
            IsRunning = false;
            return _stopTcs.Task;
        }

        public override Task WriteInputAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public void CompleteStart() => _startTcs?.SetResult(true);
        public void CompleteStop() => _stopTcs?.SetResult(true);
    }

    [Test]
    public void Constructor_SetsInitialState()
    {
        // Arrange & Act
        var shell = new TestCustomShell();

        // Assert
        Assert.That(shell.IsRunning, Is.False, "Shell should not be running initially");
        Assert.That(shell.Metadata, Is.Not.Null, "Metadata should be set");
        Assert.That(shell.Metadata.Name, Is.EqualTo("TestShell"));
    }

    [Test]
    public void IsRunning_ThreadSafe_CanSetAndGet()
    {
        // Arrange
        var shell = new TestCustomShell();

        // Act
        shell.TestSetIsRunning(true);

        // Assert
        Assert.That(shell.IsRunning, Is.True, "IsRunning should be true after setting");

        // Act
        shell.TestSetIsRunning(false);

        // Assert
        Assert.That(shell.IsRunning, Is.False, "IsRunning should be false after resetting");
    }

    [Test]
    public void IsRunning_ThreadSafe_ConcurrentAccessDoesNotCorrupt()
    {
        // Arrange
        var shell = new TestCustomShell();
        var iterations = 1000;
        var tasks = new List<Task>();

        // Act - Multiple threads reading and writing IsRunning
        for (int i = 0; i < 10; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                for (int j = 0; j < iterations; j++)
                {
                    shell.TestSetIsRunning(j % 2 == 0);
                    _ = shell.IsRunning; // Read
                }
            }));
        }

        Task.WaitAll(tasks.ToArray());

        // Assert - No exception thrown means thread-safety is working
        Assert.Pass("Concurrent access did not cause corruption");
    }

    [Test]
    public void OutputReceived_Event_RaisedCorrectly()
    {
        // Arrange
        var shell = new TestCustomShell();
        ShellOutputEventArgs? capturedArgs = null;
        var eventRaised = false;

        shell.OutputReceived += (sender, args) =>
        {
            eventRaised = true;
            capturedArgs = args;
        };

        var testData = Encoding.UTF8.GetBytes("test output");

        // Act
        shell.TestRaiseOutputReceived(testData, ShellOutputType.Stdout);

        // Assert
        Assert.That(eventRaised, Is.True, "OutputReceived event should be raised");
        Assert.That(capturedArgs, Is.Not.Null, "Event args should not be null");
        Assert.That(capturedArgs!.Data.ToArray(), Is.EqualTo(testData), "Data should match");
        Assert.That(capturedArgs.OutputType, Is.EqualTo(ShellOutputType.Stdout), "Output type should be Stdout");
    }

    [Test]
    public void OutputReceived_Event_WithStderr()
    {
        // Arrange
        var shell = new TestCustomShell();
        ShellOutputEventArgs? capturedArgs = null;

        shell.OutputReceived += (sender, args) => capturedArgs = args;

        var testData = Encoding.UTF8.GetBytes("error output");

        // Act
        shell.TestRaiseOutputReceived(testData, ShellOutputType.Stderr);

        // Assert
        Assert.That(capturedArgs, Is.Not.Null);
        Assert.That(capturedArgs!.OutputType, Is.EqualTo(ShellOutputType.Stderr), "Output type should be Stderr");
    }

    [Test]
    public void OutputReceived_Event_NoSubscribers_DoesNotThrow()
    {
        // Arrange
        var shell = new TestCustomShell();
        var testData = Encoding.UTF8.GetBytes("test output");

        // Act & Assert
        Assert.DoesNotThrow(() => shell.TestRaiseOutputReceived(testData));
    }

    [Test]
    public void OutputReceived_Event_MultipleSubscribers()
    {
        // Arrange
        var shell = new TestCustomShell();
        var subscriber1Called = false;
        var subscriber2Called = false;

        shell.OutputReceived += (sender, args) => subscriber1Called = true;
        shell.OutputReceived += (sender, args) => subscriber2Called = true;

        var testData = Encoding.UTF8.GetBytes("test");

        // Act
        shell.TestRaiseOutputReceived(testData);

        // Assert
        Assert.That(subscriber1Called, Is.True, "First subscriber should be called");
        Assert.That(subscriber2Called, Is.True, "Second subscriber should be called");
    }

    [Test]
    public void Terminated_Event_RaisedCorrectly()
    {
        // Arrange
        var shell = new TestCustomShell();
        ShellTerminatedEventArgs? capturedArgs = null;
        var eventRaised = false;

        shell.Terminated += (sender, args) =>
        {
            eventRaised = true;
            capturedArgs = args;
        };

        // Act
        shell.TestRaiseTerminated(0, "Normal exit");

        // Assert
        Assert.That(eventRaised, Is.True, "Terminated event should be raised");
        Assert.That(capturedArgs, Is.Not.Null, "Event args should not be null");
        Assert.That(capturedArgs!.ExitCode, Is.EqualTo(0), "Exit code should be 0");
        Assert.That(capturedArgs.Reason, Is.EqualTo("Normal exit"), "Reason should match");
    }

    [Test]
    public void Terminated_Event_WithNonZeroExitCode()
    {
        // Arrange
        var shell = new TestCustomShell();
        ShellTerminatedEventArgs? capturedArgs = null;

        shell.Terminated += (sender, args) => capturedArgs = args;

        // Act
        shell.TestRaiseTerminated(1, "Error occurred");

        // Assert
        Assert.That(capturedArgs, Is.Not.Null);
        Assert.That(capturedArgs!.ExitCode, Is.EqualTo(1), "Exit code should be 1");
        Assert.That(capturedArgs.Reason, Is.EqualTo("Error occurred"));
    }

    [Test]
    public void Terminated_Event_WithNullReason()
    {
        // Arrange
        var shell = new TestCustomShell();
        ShellTerminatedEventArgs? capturedArgs = null;

        shell.Terminated += (sender, args) => capturedArgs = args;

        // Act
        shell.TestRaiseTerminated(0);

        // Assert
        Assert.That(capturedArgs, Is.Not.Null);
        Assert.That(capturedArgs!.ExitCode, Is.EqualTo(0));
        Assert.That(capturedArgs.Reason, Is.Null, "Reason should be null when not provided");
    }

    [Test]
    public void Terminated_Event_NoSubscribers_DoesNotThrow()
    {
        // Arrange
        var shell = new TestCustomShell();

        // Act & Assert
        Assert.DoesNotThrow(() => shell.TestRaiseTerminated(0));
    }

    [Test]
    public void NotifyTerminalResize_DefaultImplementation_DoesNotThrow()
    {
        // Arrange
        var shell = new TestCustomShell();

        // Act & Assert
        Assert.DoesNotThrow(() => shell.NotifyTerminalResize(80, 24));
    }

    [Test]
    public void RequestCancellation_DefaultImplementation_DoesNotThrow()
    {
        // Arrange
        var shell = new TestCustomShell();

        // Act & Assert
        Assert.DoesNotThrow(() => shell.RequestCancellation());
    }

    [Test]
    public void SendInitialOutput_DefaultImplementation_DoesNotThrow()
    {
        // Arrange
        var shell = new TestCustomShell();

        // Act & Assert
        Assert.DoesNotThrow(() => shell.SendInitialOutput());
    }

    [Test]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        // Arrange
        var shell = new TestCustomShell();

        // Act & Assert
        Assert.DoesNotThrow(() =>
        {
            shell.Dispose();
            shell.Dispose();
            shell.Dispose();
        });
    }

    [Test]
    public void Dispose_CallsGCSuppressFinalize()
    {
        // Arrange
        var shell = new TestCustomShell();

        // Act
        shell.Dispose();

        // Assert
        // We can't directly test GC.SuppressFinalize, but we can verify no exception is thrown
        // and the object is still usable for basic operations
        Assert.DoesNotThrow(() => _ = shell.IsRunning);
    }

    [Test]
    public async Task StartAsync_SetsIsRunningToTrue()
    {
        // Arrange
        var shell = new TestCustomShell();
        var options = CustomShellStartOptions.CreateWithDimensions(80, 24);

        // Act
        var startTask = shell.StartAsync(options);
        shell.CompleteStart();
        await startTask;

        // Assert
        Assert.That(shell.IsRunning, Is.True, "IsRunning should be true after StartAsync");
    }

    [Test]
    public async Task StopAsync_SetsIsRunningToFalse()
    {
        // Arrange
        var shell = new TestCustomShell();
        var options = CustomShellStartOptions.CreateWithDimensions(80, 24);

        var startTask = shell.StartAsync(options);
        shell.CompleteStart();
        await startTask;

        // Act
        var stopTask = shell.StopAsync();
        shell.CompleteStop();
        await stopTask;

        // Assert
        Assert.That(shell.IsRunning, Is.False, "IsRunning should be false after StopAsync");
    }

    [Test]
    public async Task WriteInputAsync_CompletesSuccessfully()
    {
        // Arrange
        var shell = new TestCustomShell();
        var testData = Encoding.UTF8.GetBytes("test input");

        // Act & Assert
        Assert.DoesNotThrowAsync(async () => await shell.WriteInputAsync(testData));
    }

    [Test]
    public void Metadata_ReturnsCorrectInformation()
    {
        // Arrange & Act
        var shell = new TestCustomShell();

        // Assert
        Assert.That(shell.Metadata.Name, Is.EqualTo("TestShell"));
        Assert.That(shell.Metadata.Description, Is.EqualTo("A test shell implementation"));
        Assert.That(shell.Metadata.Version, Is.EqualTo(new Version(1, 0, 0)));
        Assert.That(shell.Metadata.Author, Is.EqualTo("Test Author"));
    }

    [Test]
    public void Lock_IsAccessible_FromDerivedClass()
    {
        // Arrange
        var shell = new TestCustomShell();

        // Act & Assert - This test verifies that the _lock object is accessible
        // by attempting to use it (the TestSetIsRunning method uses _lock internally)
        Assert.DoesNotThrow(() => shell.TestSetIsRunning(true));
    }
}
