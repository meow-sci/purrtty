using System.Text;
using caTTY.Core.Terminal;
using NUnit.Framework;

namespace caTTY.CustomShells.Tests.Unit;

/// <summary>
/// Unit tests for BaseChannelOutputShell typed output functionality (stdout vs stderr).
/// Tests verify that shells can emit both stdout and stderr through the output channel
/// and that the correct output type is propagated through OutputReceived events.
/// </summary>
[TestFixture]
public class BaseChannelOutputShellTypedOutputTests
{
    private TestTypedOutputShell? _shell;
    private List<(byte[] Data, ShellOutputType Type)> _capturedOutput = new();

    [SetUp]
    public void Setup()
    {
        _capturedOutput = new List<(byte[] Data, ShellOutputType Type)>();
        _shell = new TestTypedOutputShell(_capturedOutput);
    }

    [TearDown]
    public async Task TearDown()
    {
        if (_shell != null && _shell.IsRunning)
        {
            await _shell.StopAsync();
        }
        _shell?.Dispose();
        _shell = null;
    }

    #region Stdout Tests

    [Test]
    public async Task QueueOutput_ByteArray_SendsAsStdout()
    {
        // Arrange
        var options = CustomShellStartOptions.CreateWithDimensions(80, 24);
        await _shell!.StartAsync(options);
        var testData = Encoding.UTF8.GetBytes("test output");

        // Act
        _shell.QueueOutputPublic(testData);
        await Task.Delay(100); // Give output pump time to process

        // Assert
        Assert.That(_capturedOutput, Has.Count.EqualTo(1));
        Assert.That(_capturedOutput[0].Data, Is.EqualTo(testData));
        Assert.That(_capturedOutput[0].Type, Is.EqualTo(ShellOutputType.Stdout));
    }

    [Test]
    public async Task QueueOutput_String_SendsAsStdout()
    {
        // Arrange
        var options = CustomShellStartOptions.CreateWithDimensions(80, 24);
        await _shell!.StartAsync(options);
        var testText = "test output";

        // Act
        _shell.QueueOutputPublic(testText);
        await Task.Delay(100);

        // Assert
        Assert.That(_capturedOutput, Has.Count.EqualTo(1));
        var capturedText = Encoding.UTF8.GetString(_capturedOutput[0].Data);
        Assert.That(capturedText, Is.EqualTo(testText));
        Assert.That(_capturedOutput[0].Type, Is.EqualTo(ShellOutputType.Stdout));
    }

    #endregion

    #region Stderr Tests

    [Test]
    public async Task QueueOutput_ByteArrayWithStderrType_SendsAsStderr()
    {
        // Arrange
        var options = CustomShellStartOptions.CreateWithDimensions(80, 24);
        await _shell!.StartAsync(options);
        var testData = Encoding.UTF8.GetBytes("error output");

        // Act
        _shell.QueueOutputPublic(testData, ShellOutputType.Stderr);
        await Task.Delay(100);

        // Assert
        Assert.That(_capturedOutput, Has.Count.EqualTo(1));
        Assert.That(_capturedOutput[0].Data, Is.EqualTo(testData));
        Assert.That(_capturedOutput[0].Type, Is.EqualTo(ShellOutputType.Stderr));
    }

    [Test]
    public async Task QueueOutput_StringWithStderrType_SendsAsStderr()
    {
        // Arrange
        var options = CustomShellStartOptions.CreateWithDimensions(80, 24);
        await _shell!.StartAsync(options);
        var testText = "error output";

        // Act
        _shell.QueueOutputPublic(testText, ShellOutputType.Stderr);
        await Task.Delay(100);

        // Assert
        Assert.That(_capturedOutput, Has.Count.EqualTo(1));
        var capturedText = Encoding.UTF8.GetString(_capturedOutput[0].Data);
        Assert.That(capturedText, Is.EqualTo(testText));
        Assert.That(_capturedOutput[0].Type, Is.EqualTo(ShellOutputType.Stderr));
    }

    #endregion

    #region Mixed Output Tests

    [Test]
    public async Task QueueOutput_MixedStdoutAndStderr_PreservesOrder()
    {
        // Arrange
        var options = CustomShellStartOptions.CreateWithDimensions(80, 24);
        await _shell!.StartAsync(options);

        // Act - Queue alternating stdout and stderr
        _shell.QueueOutputPublic("stdout1");
        _shell.QueueOutputPublic("stderr1", ShellOutputType.Stderr);
        _shell.QueueOutputPublic("stdout2");
        _shell.QueueOutputPublic("stderr2", ShellOutputType.Stderr);
        await Task.Delay(100);

        // Assert - Order should be preserved
        Assert.That(_capturedOutput, Has.Count.EqualTo(4));

        Assert.That(Encoding.UTF8.GetString(_capturedOutput[0].Data), Is.EqualTo("stdout1"));
        Assert.That(_capturedOutput[0].Type, Is.EqualTo(ShellOutputType.Stdout));

        Assert.That(Encoding.UTF8.GetString(_capturedOutput[1].Data), Is.EqualTo("stderr1"));
        Assert.That(_capturedOutput[1].Type, Is.EqualTo(ShellOutputType.Stderr));

        Assert.That(Encoding.UTF8.GetString(_capturedOutput[2].Data), Is.EqualTo("stdout2"));
        Assert.That(_capturedOutput[2].Type, Is.EqualTo(ShellOutputType.Stdout));

        Assert.That(Encoding.UTF8.GetString(_capturedOutput[3].Data), Is.EqualTo("stderr2"));
        Assert.That(_capturedOutput[3].Type, Is.EqualTo(ShellOutputType.Stderr));
    }

    [Test]
    public async Task QueueOutput_MultipleStderr_AllReceivedAsStderr()
    {
        // Arrange
        var options = CustomShellStartOptions.CreateWithDimensions(80, 24);
        await _shell!.StartAsync(options);

        // Act - Queue multiple stderr messages
        _shell.QueueOutputPublic("error1", ShellOutputType.Stderr);
        _shell.QueueOutputPublic("error2", ShellOutputType.Stderr);
        _shell.QueueOutputPublic("error3", ShellOutputType.Stderr);
        await Task.Delay(100);

        // Assert - All should be stderr
        Assert.That(_capturedOutput, Has.Count.EqualTo(3));
        Assert.That(_capturedOutput.All(o => o.Type == ShellOutputType.Stderr), Is.True);
    }

    #endregion

    #region Edge Cases

    [Test]
    public async Task QueueOutput_EmptyString_StillSendsWithCorrectType()
    {
        // Arrange
        var options = CustomShellStartOptions.CreateWithDimensions(80, 24);
        await _shell!.StartAsync(options);

        // Act
        _shell.QueueOutputPublic("", ShellOutputType.Stderr);
        await Task.Delay(100);

        // Assert
        Assert.That(_capturedOutput, Has.Count.EqualTo(1));
        Assert.That(_capturedOutput[0].Data.Length, Is.EqualTo(0));
        Assert.That(_capturedOutput[0].Type, Is.EqualTo(ShellOutputType.Stderr));
    }

    [Test]
    public async Task QueueOutput_BeforeStart_DoesNotCrash()
    {
        // Act & Assert - Should not throw
        Assert.DoesNotThrow(() => _shell!.QueueOutputPublic("test"));
    }

    [Test]
    public async Task QueueOutput_AfterStop_DoesNotCrash()
    {
        // Arrange
        var options = CustomShellStartOptions.CreateWithDimensions(80, 24);
        await _shell!.StartAsync(options);
        await _shell.StopAsync();

        // Act & Assert - Should not throw
        Assert.DoesNotThrow(() => _shell.QueueOutputPublic("test"));
    }

    #endregion

    /// <summary>
    /// Test implementation of BaseChannelOutputShell that exposes protected QueueOutput methods
    /// and captures output events for testing.
    /// </summary>
    private class TestTypedOutputShell : BaseChannelOutputShell
    {
        private readonly List<(byte[] Data, ShellOutputType Type)> _capturedOutput;

        public TestTypedOutputShell(List<(byte[] Data, ShellOutputType Type)> capturedOutput)
        {
            _capturedOutput = capturedOutput;

            // Subscribe to output events to capture for assertions
            OutputReceived += (sender, args) =>
            {
                // Copy the data since it's a ReadOnlyMemory
                var dataCopy = args.Data.ToArray();
                _capturedOutput.Add((dataCopy, args.OutputType));
            };
        }

        public override CustomShellMetadata Metadata { get; } = CustomShellMetadata.Create(
            name: "Test Typed Output Shell",
            description: "Test shell for typed output testing",
            version: new Version(1, 0, 0),
            author: "Test",
            supportedFeatures: Array.Empty<string>()
        );

        protected override Task OnStartingAsync(CustomShellStartOptions options, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        protected override Task OnStoppingAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public override void SendInitialOutput()
        {
            // No initial output for tests
        }

        public override Task WriteInputAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
        {
            // Not needed for these tests
            return Task.CompletedTask;
        }

        public override void RequestCancellation()
        {
            // Not needed for these tests
        }

        // Expose protected QueueOutput methods for testing
        public void QueueOutputPublic(byte[] data)
        {
            QueueOutput(data);
        }

        public void QueueOutputPublic(string text)
        {
            QueueOutput(text);
        }

        public void QueueOutputPublic(byte[] data, ShellOutputType type)
        {
            QueueOutput(data, type);
        }

        public void QueueOutputPublic(string text, ShellOutputType type)
        {
            QueueOutput(text, type);
        }
    }
}
