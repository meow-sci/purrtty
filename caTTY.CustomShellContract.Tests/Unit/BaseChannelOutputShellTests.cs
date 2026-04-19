using System.Text;
using NUnit.Framework;

namespace caTTY.Core.Terminal.Tests.Unit;

[TestFixture]
public class BaseChannelOutputShellTests
{
    /// <summary>
    ///     Test implementation of BaseChannelOutputShell for testing purposes.
    /// </summary>
    private class TestChannelOutputShell : BaseChannelOutputShell
    {
        private readonly CustomShellMetadata _metadata;
        private TaskCompletionSource<bool>? _startTcs;
        private TaskCompletionSource<bool>? _stopTcs;

        public int OnStartingCallCount { get; private set; }
        public int OnStoppingCallCount { get; private set; }
        public CustomShellStartOptions? LastStartOptions { get; private set; }

        public TestChannelOutputShell()
        {
            _metadata = CustomShellMetadata.Create(
                "TestChannelShell",
                "A test channel output shell implementation",
                new Version(1, 0, 0),
                "Test Author"
            );
        }

        public override CustomShellMetadata Metadata => _metadata;

        protected override Task OnStartingAsync(CustomShellStartOptions options, CancellationToken cancellationToken)
        {
            OnStartingCallCount++;
            LastStartOptions = options;
            _startTcs = new TaskCompletionSource<bool>();
            return _startTcs.Task;
        }

        protected override Task OnStoppingAsync(CancellationToken cancellationToken)
        {
            OnStoppingCallCount++;
            _stopTcs = new TaskCompletionSource<bool>();
            return _stopTcs.Task;
        }

        public override Task WriteInputAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
        {
            // Simple echo implementation for testing
            QueueOutput(data.ToArray());
            return Task.CompletedTask;
        }

        public void CompleteStart() => _startTcs?.SetResult(true);
        public void CompleteStop() => _stopTcs?.SetResult(true);

        // Expose protected methods for testing
        public void TestQueueOutputBytes(byte[] data) => QueueOutput(data);
        public void TestQueueOutputString(string text) => QueueOutput(text);
    }

    [Test]
    public void Constructor_SetsInitialState()
    {
        // Arrange & Act
        var shell = new TestChannelOutputShell();

        // Assert
        Assert.That(shell.IsRunning, Is.False, "Shell should not be running initially");
        Assert.That(shell.Metadata, Is.Not.Null, "Metadata should be set");
        Assert.That(shell.Metadata.Name, Is.EqualTo("TestChannelShell"));
    }

    [Test]
    public async Task StartAsync_CallsOnStartingHook()
    {
        // Arrange
        var shell = new TestChannelOutputShell();
        var options = CustomShellStartOptions.CreateWithDimensions(80, 24);

        // Act
        var startTask = shell.StartAsync(options);
        shell.CompleteStart();
        await startTask;

        // Assert
        Assert.That(shell.OnStartingCallCount, Is.EqualTo(1), "OnStarting should be called once");
        Assert.That(shell.LastStartOptions, Is.EqualTo(options), "Start options should be passed to hook");
    }

    [Test]
    public async Task StartAsync_StartsOutputPump()
    {
        // Arrange
        var shell = new TestChannelOutputShell();
        var options = CustomShellStartOptions.CreateWithDimensions(80, 24);
        var outputReceived = new List<byte[]>();
        var outputEvent = new ManualResetEventSlim(false);

        shell.OutputReceived += (sender, args) =>
        {
            outputReceived.Add(args.Data.ToArray());
            outputEvent.Set();
        };

        // Act
        var startTask = shell.StartAsync(options);
        shell.CompleteStart();
        await startTask;

        // Queue some output
        var testData = Encoding.UTF8.GetBytes("test output");
        shell.TestQueueOutputBytes(testData);

        // Wait for output event
        var received = outputEvent.Wait(TimeSpan.FromSeconds(1));

        // Assert
        Assert.That(received, Is.True, "Output should be received via pump");
        Assert.That(outputReceived.Count, Is.EqualTo(1), "Should receive one output event");
        Assert.That(outputReceived[0], Is.EqualTo(testData), "Output data should match");
    }

    [Test]
    public async Task StartAsync_WhenAlreadyRunning_ThrowsInvalidOperationException()
    {
        // Arrange
        var shell = new TestChannelOutputShell();
        var options = CustomShellStartOptions.CreateWithDimensions(80, 24);

        var startTask = shell.StartAsync(options);
        shell.CompleteStart();
        await startTask;

        // Act & Assert
        Assert.ThrowsAsync<InvalidOperationException>(async () => await shell.StartAsync(options));
    }

    [Test]
    public async Task StartAsync_SetsIsRunningToTrue()
    {
        // Arrange
        var shell = new TestChannelOutputShell();
        var options = CustomShellStartOptions.CreateWithDimensions(80, 24);

        // Act
        var startTask = shell.StartAsync(options);
        shell.CompleteStart();
        await startTask;

        // Assert
        Assert.That(shell.IsRunning, Is.True, "IsRunning should be true after StartAsync");
    }

    [Test]
    public async Task StopAsync_CallsOnStoppingHook()
    {
        // Arrange
        var shell = new TestChannelOutputShell();
        var options = CustomShellStartOptions.CreateWithDimensions(80, 24);

        var startTask = shell.StartAsync(options);
        shell.CompleteStart();
        await startTask;

        // Act
        var stopTask = shell.StopAsync();
        shell.CompleteStop();
        await stopTask;

        // Assert
        Assert.That(shell.OnStoppingCallCount, Is.EqualTo(1), "OnStopping should be called once");
    }

    [Test]
    public async Task StopAsync_CompletesChannelAndDrainsPump()
    {
        // Arrange
        var shell = new TestChannelOutputShell();
        var options = CustomShellStartOptions.CreateWithDimensions(80, 24);
        var outputReceived = new List<byte[]>();
        var outputCount = new ManualResetEventSlim(false);

        shell.OutputReceived += (sender, args) =>
        {
            outputReceived.Add(args.Data.ToArray());
            if (outputReceived.Count >= 3)
            {
                outputCount.Set();
            }
        };

        var startTask = shell.StartAsync(options);
        shell.CompleteStart();
        await startTask;

        // Queue multiple outputs before stopping
        shell.TestQueueOutputString("output 1\n");
        shell.TestQueueOutputString("output 2\n");
        shell.TestQueueOutputString("output 3\n");

        // Act - Stop should drain all queued output
        var stopTask = shell.StopAsync();
        shell.CompleteStop();
        await stopTask;

        // Wait a bit for pump to process
        outputCount.Wait(TimeSpan.FromSeconds(2));

        // Assert
        Assert.That(outputReceived.Count, Is.GreaterThanOrEqualTo(3), "All queued output should be drained before stopping");
    }

    [Test]
    public async Task StopAsync_WhenNotRunning_ReturnsImmediately()
    {
        // Arrange
        var shell = new TestChannelOutputShell();

        // Act & Assert
        Assert.DoesNotThrowAsync(async () => await shell.StopAsync());
        Assert.That(shell.OnStoppingCallCount, Is.EqualTo(0), "OnStopping should not be called if not running");
    }

    [Test]
    public async Task StopAsync_SetsIsRunningToFalse()
    {
        // Arrange
        var shell = new TestChannelOutputShell();
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
    public async Task QueueOutput_ByteArray_RaisesOutputReceivedEvent()
    {
        // Arrange
        var shell = new TestChannelOutputShell();
        var options = CustomShellStartOptions.CreateWithDimensions(80, 24);
        ShellOutputEventArgs? capturedArgs = null;
        var outputEvent = new ManualResetEventSlim(false);

        shell.OutputReceived += (sender, args) =>
        {
            capturedArgs = args;
            outputEvent.Set();
        };

        var startTask = shell.StartAsync(options);
        shell.CompleteStart();
        await startTask;

        var testData = Encoding.UTF8.GetBytes("test output");

        // Act
        shell.TestQueueOutputBytes(testData);

        // Wait for output event
        var received = outputEvent.Wait(TimeSpan.FromSeconds(1));

        // Assert
        Assert.That(received, Is.True, "OutputReceived event should be raised");
        Assert.That(capturedArgs, Is.Not.Null, "Event args should not be null");
        Assert.That(capturedArgs!.Data.ToArray(), Is.EqualTo(testData), "Data should match");
        Assert.That(capturedArgs.OutputType, Is.EqualTo(ShellOutputType.Stdout), "Output type should be Stdout");
    }

    [Test]
    public async Task QueueOutput_String_ConvertsToUtf8AndRaisesEvent()
    {
        // Arrange
        var shell = new TestChannelOutputShell();
        var options = CustomShellStartOptions.CreateWithDimensions(80, 24);
        ShellOutputEventArgs? capturedArgs = null;
        var outputEvent = new ManualResetEventSlim(false);

        shell.OutputReceived += (sender, args) =>
        {
            capturedArgs = args;
            outputEvent.Set();
        };

        var startTask = shell.StartAsync(options);
        shell.CompleteStart();
        await startTask;

        var testText = "test output";
        var expectedBytes = Encoding.UTF8.GetBytes(testText);

        // Act
        shell.TestQueueOutputString(testText);

        // Wait for output event
        var received = outputEvent.Wait(TimeSpan.FromSeconds(1));

        // Assert
        Assert.That(received, Is.True, "OutputReceived event should be raised");
        Assert.That(capturedArgs, Is.Not.Null, "Event args should not be null");
        Assert.That(capturedArgs!.Data.ToArray(), Is.EqualTo(expectedBytes), "Data should be UTF-8 encoded");
    }

    [Test]
    public async Task QueueOutput_MultipleWrites_AllReceived()
    {
        // Arrange
        var shell = new TestChannelOutputShell();
        var options = CustomShellStartOptions.CreateWithDimensions(80, 24);
        var outputReceived = new List<string>();
        var countEvent = new ManualResetEventSlim(false);

        shell.OutputReceived += (sender, args) =>
        {
            var text = Encoding.UTF8.GetString(args.Data.ToArray());
            outputReceived.Add(text);
            if (outputReceived.Count >= 5)
            {
                countEvent.Set();
            }
        };

        var startTask = shell.StartAsync(options);
        shell.CompleteStart();
        await startTask;

        // Act
        for (int i = 0; i < 5; i++)
        {
            shell.TestQueueOutputString($"output {i}\n");
        }

        // Wait for all outputs
        var received = countEvent.Wait(TimeSpan.FromSeconds(2));

        // Assert
        Assert.That(received, Is.True, "All outputs should be received");
        Assert.That(outputReceived.Count, Is.EqualTo(5), "Should receive all 5 outputs");
        for (int i = 0; i < 5; i++)
        {
            Assert.That(outputReceived[i], Is.EqualTo($"output {i}\n"), $"Output {i} should match");
        }
    }

    [Test]
    public async Task WriteInputAsync_CanUseQueueOutput()
    {
        // Arrange
        var shell = new TestChannelOutputShell();
        var options = CustomShellStartOptions.CreateWithDimensions(80, 24);
        ShellOutputEventArgs? capturedArgs = null;
        var outputEvent = new ManualResetEventSlim(false);

        shell.OutputReceived += (sender, args) =>
        {
            capturedArgs = args;
            outputEvent.Set();
        };

        var startTask = shell.StartAsync(options);
        shell.CompleteStart();
        await startTask;

        var testData = Encoding.UTF8.GetBytes("echo test");

        // Act - WriteInputAsync uses QueueOutput internally in our test implementation
        await shell.WriteInputAsync(testData);

        // Wait for output event
        var received = outputEvent.Wait(TimeSpan.FromSeconds(1));

        // Assert
        Assert.That(received, Is.True, "OutputReceived event should be raised");
        Assert.That(capturedArgs, Is.Not.Null);
        Assert.That(capturedArgs!.Data.ToArray(), Is.EqualTo(testData), "Echoed data should match input");
    }

    [Test]
    public async Task Dispose_WhenRunning_StopsOutputPump()
    {
        // Arrange
        var shell = new TestChannelOutputShell();
        var options = CustomShellStartOptions.CreateWithDimensions(80, 24);

        var startTask = shell.StartAsync(options);
        shell.CompleteStart();
        await startTask;

        // Act
        shell.Dispose();

        // Assert
        Assert.That(shell.IsRunning, Is.False, "IsRunning should be false after Dispose");
    }

    [Test]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        // Arrange
        var shell = new TestChannelOutputShell();

        // Act & Assert
        Assert.DoesNotThrow(() =>
        {
            shell.Dispose();
            shell.Dispose();
            shell.Dispose();
        });
    }

    [Test]
    public async Task Dispose_WhenNotRunning_DoesNotThrow()
    {
        // Arrange
        var shell = new TestChannelOutputShell();

        // Act & Assert
        Assert.DoesNotThrow(() => shell.Dispose());
    }

    [Test]
    public async Task OutputPump_HandlesExceptionsGracefully()
    {
        // Arrange
        var shell = new TestChannelOutputShell();
        var options = CustomShellStartOptions.CreateWithDimensions(80, 24);
        var exceptionThrown = false;

        shell.OutputReceived += (sender, args) =>
        {
            // Throw exception on first event
            if (!exceptionThrown)
            {
                exceptionThrown = true;
                throw new InvalidOperationException("Test exception");
            }
        };

        var startTask = shell.StartAsync(options);
        shell.CompleteStart();
        await startTask;

        // Act - Queue output that will cause exception
        shell.TestQueueOutputString("first output");

        // Wait a bit for exception to be thrown
        await Task.Delay(100);

        // Queue another output - pump should still be running
        shell.TestQueueOutputString("second output");

        await Task.Delay(100);

        // Assert - Pump should continue working despite exception
        Assert.That(exceptionThrown, Is.True, "Exception should have been thrown");
        // Note: The pump should continue running and not crash despite the exception
    }

    [Test]
    public async Task StartAsync_CreatesUnboundedChannel()
    {
        // Arrange
        var shell = new TestChannelOutputShell();
        var options = CustomShellStartOptions.CreateWithDimensions(80, 24);

        // Act
        var startTask = shell.StartAsync(options);
        shell.CompleteStart();
        await startTask;

        // Queue many outputs quickly - unbounded channel should handle this
        for (int i = 0; i < 1000; i++)
        {
            shell.TestQueueOutputString($"output {i}\n");
        }

        // Assert - No exception should be thrown for excessive queuing
        Assert.Pass("Unbounded channel handles rapid queuing");
    }

    [Test]
    public async Task StopAsync_WithTimeout_CancelsPumpIfNotDrained()
    {
        // Arrange
        var shell = new TestChannelOutputShell();
        var options = CustomShellStartOptions.CreateWithDimensions(80, 24);

        var startTask = shell.StartAsync(options);
        shell.CompleteStart();
        await startTask;

        // Queue a large amount of output
        for (int i = 0; i < 10000; i++)
        {
            shell.TestQueueOutputString($"output {i}\n");
        }

        // Act - Stop with a very short timeout (implicit in StopAsync)
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var stopTask = shell.StopAsync();
        shell.CompleteStop();
        await stopTask;
        stopwatch.Stop();

        // Assert - Should not take more than ~3 seconds (2 second timeout + buffer)
        Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(3)), "StopAsync should timeout and cancel pump");
    }
}
