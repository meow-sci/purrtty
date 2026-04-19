using caTTY.Core.Terminal;
using FsCheck;
using NUnit.Framework;
using System.Text;

namespace caTTY.Core.Tests.Property;

/// <summary>
///     Property-based tests for PTY bridge integration.
///     These tests verify that the CustomShellPtyBridge correctly routes input/output 
///     between custom shells and the PTY system.
///     **Feature: custom-game-shells, Property 2: PTY Bridge Integration**
///     **Validates: Requirements 1.2, 1.4, 3.3**
/// </summary>
[TestFixture]
[Category("Property")]
public class PtyBridgeIntegrationProperties
{
    private static readonly object SharedLock = new();
    private static CustomShellPtyBridge? _sharedBridge;
    private static MockCustomShell? _sharedShell;

    /// <summary>
    ///     Gets or creates a shared bridge instance for faster test execution.
    ///     This avoids the overhead of creating/disposing bridges for each test iteration.
    /// </summary>
    private static CustomShellPtyBridge GetSharedBridge()
    {
        lock (SharedLock)
        {
            if (_sharedBridge == null || !_sharedBridge.IsRunning)
            {
                // Clean up previous instance if needed
                _sharedBridge?.Dispose();
                _sharedShell?.Dispose();

                // Create new shared instances
                _sharedShell = new MockCustomShell();
                _sharedBridge = new CustomShellPtyBridge(_sharedShell);

                // Start the bridge once
                var options = ProcessLaunchOptions.CreateCustomGame("shared-test-shell");
                var startTask = _sharedBridge.StartAsync(options);
                
                // Use shorter timeout for faster test execution
                if (!startTask.Wait(TimeSpan.FromMilliseconds(100)))
                {
                    throw new TimeoutException("Failed to start shared bridge within timeout");
                }

                if (!_sharedBridge.IsRunning)
                {
                    throw new InvalidOperationException("Shared bridge failed to start");
                }
            }

            return _sharedBridge;
        }
    }

    /// <summary>
    ///     Gets the shared mock shell instance.
    /// </summary>
    private static MockCustomShell GetSharedShell()
    {
        lock (SharedLock)
        {
            GetSharedBridge(); // Ensure bridge is initialized
            return _sharedShell!;
        }
    }

    /// <summary>
    ///     Cleanup shared resources after all tests complete.
    /// </summary>
    [OneTimeTearDown]
    public static void CleanupSharedResources()
    {
        lock (SharedLock)
        {
            _sharedBridge?.Dispose();
            _sharedShell?.Dispose();
            _sharedBridge = null;
            _sharedShell = null;
        }
    }

    /// <summary>
    ///     Generator for valid input data.
    /// </summary>
    public static Arbitrary<byte[]> InputDataArb =>
        Arb.From(Gen.Choose(1, 100).SelectMany(length =>
            Gen.ArrayOf(length, Gen.Choose(32, 126).Select(i => (byte)i))));

    /// <summary>
    ///     Generator for valid text input.
    /// </summary>
    public static Arbitrary<string> TextInputArb =>
        Arb.From(Gen.Choose(1, 50).SelectMany(length =>
            Gen.ArrayOf(length, Gen.Choose(32, 126).Select(i => (char)i))
                .Select(chars => new string(chars))));

    /// <summary>
    ///     Generator for terminal dimensions.
    /// </summary>
    public static Arbitrary<(int width, int height)> TerminalDimensionsArb =>
        Arb.From(Gen.Choose(10, 200).SelectMany(width =>
            Gen.Choose(5, 100).Select(height => (width, height))));

    /// <summary>
    ///     Generator for exit codes.
    /// </summary>
    public static Arbitrary<int> ExitCodeArb =>
        Arb.From(Gen.Choose(-1, 255));

    /// <summary>
    ///     **Feature: custom-game-shells, Property 2: PTY Bridge Integration**
    ///     **Validates: Requirements 1.2, 1.4, 3.3**
    ///     Property: For any custom shell instance, when connected through the PTY bridge, 
    ///     input from the terminal should reach the shell's input handler and output from 
    ///     the shell should reach the terminal emulator.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property PtyBridgeRoutesInputOutputCorrectly()
    {
        return Prop.ForAll(InputDataArb, inputData =>
        {
            try
            {
                // Use shared bridge for faster execution
                var bridge = GetSharedBridge();
                var mockShell = GetSharedShell();

                // Reset shell state for this test iteration
                mockShell.Reset();

                var dataReceived = false;
                var receivedData = Array.Empty<byte>();

                // Use local event handler to avoid interference between test iterations
                void OnDataReceived(object? sender, DataReceivedEventArgs e)
                {
                    dataReceived = true;
                    receivedData = e.Data.ToArray();
                }

                bridge.DataReceived += OnDataReceived;

                try
                {
                    // Send input to the bridge
                    bridge.Write(inputData.AsSpan());

                    // Trigger output from mock shell (synchronous for speed)
                    mockShell.TriggerOutput(inputData);

                    // Minimal wait for async operations - reduced from 50ms to 5ms
                    Thread.Sleep(5);

                    // Assert: Verify input reached the shell and output reached the bridge
                    bool inputReachedShell = mockShell.ReceivedInput.SequenceEqual(inputData);
                    bool outputReachedBridge = dataReceived && receivedData.SequenceEqual(inputData);

                    return inputReachedShell && outputReachedBridge;
                }
                finally
                {
                    bridge.DataReceived -= OnDataReceived;
                }
            }
            catch (Exception)
            {
                // Return false for any exceptions to avoid test framework issues
                return false;
            }
        });
    }

    /// <summary>
    ///     **Feature: custom-game-shells, Property 5: Terminal Resize Notification**
    ///     **Validates: Requirements 2.4, 2.5**
    ///     Property: For any terminal resize operation, all active custom shells should 
    ///     receive notification of the new dimensions through their NotifyTerminalResize method.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property PtyBridgeForwardsResizeNotifications()
    {
        return Prop.ForAll(TerminalDimensionsArb, dimensions =>
        {
            try
            {
                var (width, height) = dimensions;

                // Use shared bridge for faster execution
                var bridge = GetSharedBridge();
                var mockShell = GetSharedShell();

                // Reset shell state for this test iteration
                mockShell.Reset();

                // Send resize notification
                bridge.Resize(width, height);

                // Assert: Verify resize notification reached the shell
                return mockShell.LastResizeWidth == width && mockShell.LastResizeHeight == height;
            }
            catch (Exception)
            {
                // Return false for any exceptions to avoid test framework issues
                return false;
            }
        });
    }

    /// <summary>
    ///     Generator for lists of input data.
    /// </summary>
    public static Arbitrary<byte[][]> InputArrayArb =>
        Arb.From(Gen.Choose(2, 5).SelectMany(count =>
            Gen.ArrayOf(count, InputDataArb.Generator)));

    /// <summary>
    ///     **Feature: custom-game-shells, Property 12: Concurrent Operation Safety**
    ///     **Validates: Requirements 6.4**
    ///     Property: For any custom shell, the PTY bridge should handle concurrent 
    ///     input and output operations safely without data corruption or race conditions.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 20, QuietOnSuccess = true)]
    public FsCheck.Property PtyBridgeHandlesConcurrentOperationsSafely()
    {
        return Prop.ForAll(InputArrayArb, inputArray =>
        {
            try
            {
                // Use shared bridge for faster execution
                var bridge = GetSharedBridge();
                var mockShell = GetSharedShell();

                // Reset shell state for this test iteration
                mockShell.Reset();

                var receivedOutputs = new List<byte[]>();
                var lockObject = new object();

                // Use local event handler to avoid interference between test iterations
                void OnDataReceived(object? sender, DataReceivedEventArgs e)
                {
                    lock (lockObject)
                    {
                        receivedOutputs.Add(e.Data.ToArray());
                    }
                }

                bridge.DataReceived += OnDataReceived;

                try
                {
                    // Send multiple inputs concurrently and trigger outputs
                    var tasks = inputArray.Select(input => Task.Run(() =>
                    {
                        bridge.Write(input.AsSpan());
                        // Reduced delay for faster execution
                        Thread.Sleep(1);
                        mockShell.TriggerOutput(input);
                    })).ToArray();

                    // Reduced timeout for faster execution
                    Task.WaitAll(tasks, TimeSpan.FromMilliseconds(200));

                    // Reduced wait for all outputs to be processed
                    Thread.Sleep(10);

                    // Assert: Verify all inputs were received and all outputs were generated
                    lock (lockObject)
                    {
                        var allInputsReceived = mockShell.AllReceivedInputs;
                        
                        // Check that we received the expected number of inputs and outputs
                        bool correctInputCount = allInputsReceived.Count == inputArray.Length;
                        bool correctOutputCount = receivedOutputs.Count == inputArray.Length;
                        
                        // Check that all expected inputs are present (order may vary due to concurrency)
                        bool allInputsPresent = inputArray.All(input =>
                            allInputsReceived.Any(received => received.SequenceEqual(input)));
                        
                        // Check that all expected outputs are present (order may vary due to concurrency)
                        bool allOutputsPresent = inputArray.All(input =>
                            receivedOutputs.Any(output => output.SequenceEqual(input)));

                        return correctInputCount && correctOutputCount && allInputsPresent && allOutputsPresent;
                    }
                }
                finally
                {
                    bridge.DataReceived -= OnDataReceived;
                }
            }
            catch (Exception)
            {
                // Return false for any exceptions to avoid test framework issues
                return false;
            }
        });
    }

    /// <summary>
    ///     Property: PTY bridge should handle shell termination correctly.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property PtyBridgeHandlesShellTermination()
    {
        return Prop.ForAll(ExitCodeArb, exitCode =>
        {
            try
            {
                // Create dedicated bridge for termination testing (can't reuse shared bridge)
                var mockShell = new MockCustomShell();
                using var bridge = new CustomShellPtyBridge(mockShell);

                var processExited = false;
                var receivedExitCode = -999;

                bridge.ProcessExited += (sender, e) =>
                {
                    processExited = true;
                    receivedExitCode = e.ExitCode;
                };

                // Start the bridge with reduced timeout
                var options = ProcessLaunchOptions.CreateCustomGame("termination-test-shell");
                var startTask = bridge.StartAsync(options);
                
                if (!startTask.Wait(TimeSpan.FromMilliseconds(50)))
                {
                    return false;
                }

                if (!bridge.IsRunning)
                {
                    return false;
                }

                // Trigger shell termination
                mockShell.TriggerTermination(exitCode);

                // Reduced wait time for termination processing
                Thread.Sleep(5);

                // Assert: Verify termination was handled correctly
                return processExited && receivedExitCode == exitCode && !bridge.IsRunning;
            }
            catch (Exception)
            {
                // Return false for any exceptions to avoid test framework issues
                return false;
            }
        });
    }

    /// <summary>
    ///     Property: PTY bridge should handle text input correctly.
    /// </summary>
    [FsCheck.NUnit.Property(MaxTest = 100, QuietOnSuccess = true)]
    public FsCheck.Property PtyBridgeHandlesTextInput()
    {
        return Prop.ForAll(TextInputArb, textInput =>
        {
            try
            {
                // Use shared bridge for faster execution
                var bridge = GetSharedBridge();
                var mockShell = GetSharedShell();

                // Reset shell state for this test iteration
                mockShell.Reset();

                // Send text input
                bridge.Write(textInput);

                // Reduced wait time for input processing
                Thread.Sleep(2);

                // Assert: Verify text input was converted to bytes correctly
                var expectedBytes = Encoding.UTF8.GetBytes(textInput);
                return mockShell.ReceivedInput.SequenceEqual(expectedBytes);
            }
            catch (Exception)
            {
                // Return false for any exceptions to avoid test framework issues
                return false;
            }
        });
    }

    /// <summary>
    ///     Property: PTY bridge should handle errors gracefully.
    /// </summary>
    [Test]
    public void PtyBridgeHandlesErrorsGracefully()
    {
        // Test with failing shell
        var failingShell = new FailingMockCustomShell();
        using var bridge = new CustomShellPtyBridge(failingShell);

        // Starting should throw CustomShellStartException, not wrap it
        var options = ProcessLaunchOptions.CreateCustomGame("failing-shell");
        Assert.ThrowsAsync<CustomShellStartException>(async () => await bridge.StartAsync(options));

        // Operations on non-running bridge should throw InvalidOperationException
        Assert.Throws<InvalidOperationException>(() => bridge.Write("test"));
        Assert.Throws<InvalidOperationException>(() => bridge.Resize(80, 24));
    }

    /// <summary>
    ///     Property: PTY bridge should handle disposal correctly.
    /// </summary>
    [Test]
    public void PtyBridgeHandlesDisposalCorrectly()
    {
        var mockShell = new MockCustomShell();
        var bridge = new CustomShellPtyBridge(mockShell);

        // Start the bridge
        var options = ProcessLaunchOptions.CreateCustomGame("test-shell");
        var startTask = bridge.StartAsync(options);
        startTask.Wait(TimeSpan.FromSeconds(1));

        Assert.That(bridge.IsRunning, Is.True);

        // Dispose should not throw
        Assert.DoesNotThrow(() => bridge.Dispose());

        // Bridge should no longer be running
        Assert.That(bridge.IsRunning, Is.False);

        // Operations after disposal should throw ObjectDisposedException
        Assert.Throws<ObjectDisposedException>(() => bridge.Write("test"));
        Assert.Throws<ObjectDisposedException>(() => bridge.Resize(80, 24));

        // Multiple dispose calls should not throw
        Assert.DoesNotThrow(() => bridge.Dispose());
    }
}

/// <summary>
///     Mock implementation of ICustomShell for testing.
/// </summary>
internal class MockCustomShell : ICustomShell
{
    private bool _isRunning;
    private readonly List<byte[]> _allReceivedInputs = new();
    private readonly object _lockObject = new();

    public CustomShellMetadata Metadata { get; } = CustomShellMetadata.Create("MockShell", "Test shell for property tests");
    public bool IsRunning => _isRunning;
    public byte[] ReceivedInput { get; private set; } = Array.Empty<byte>();
    public List<byte[]> AllReceivedInputs 
    { 
        get 
        { 
            lock (_lockObject) 
            { 
                return _allReceivedInputs.ToList(); 
            } 
        } 
    }
    public int LastResizeWidth { get; private set; }
    public int LastResizeHeight { get; private set; }

    public event EventHandler<ShellOutputEventArgs>? OutputReceived;
    public event EventHandler<ShellTerminatedEventArgs>? Terminated;

    /// <summary>
    ///     Resets the shell state for reuse in multiple test iterations.
    /// </summary>
    public void Reset()
    {
        lock (_lockObject)
        {
            ReceivedInput = Array.Empty<byte>();
            _allReceivedInputs.Clear();
            LastResizeWidth = 0;
            LastResizeHeight = 0;
        }
    }

    public Task StartAsync(CustomShellStartOptions options, CancellationToken cancellationToken = default)
    {
        _isRunning = true;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        _isRunning = false;
        return Task.CompletedTask;
    }

    public Task WriteInputAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        lock (_lockObject)
        {
            ReceivedInput = data.ToArray();
            _allReceivedInputs.Add(data.ToArray());
        }
        return Task.CompletedTask;
    }

    public void NotifyTerminalResize(int width, int height)
    {
        LastResizeWidth = width;
        LastResizeHeight = height;
    }

    public void RequestCancellation()
    {
        // Mock implementation - do nothing
    }

    public void SendInitialOutput()
    {
        // Mock implementation - no initial output for tests
    }

    public void TriggerOutput(byte[] data)
    {
        // Trigger output synchronously for faster test execution
        OutputReceived?.Invoke(this, new ShellOutputEventArgs(data));
    }

    public void TriggerTermination(int exitCode)
    {
        _isRunning = false;
        Terminated?.Invoke(this, new ShellTerminatedEventArgs(exitCode));
    }

    public void Dispose()
    {
        _isRunning = false;
    }
}

/// <summary>
///     Mock implementation that fails operations for error testing.
/// </summary>
internal class FailingMockCustomShell : ICustomShell
{
    public CustomShellMetadata Metadata { get; } = CustomShellMetadata.Create("FailingShell", "Shell that fails operations");
    public bool IsRunning => false;

    public event EventHandler<ShellOutputEventArgs>? OutputReceived
    {
        add { }
        remove { }
    }
    
    public event EventHandler<ShellTerminatedEventArgs>? Terminated
    {
        add { }
        remove { }
    }

    public Task StartAsync(CustomShellStartOptions options, CancellationToken cancellationToken = default)
    {
        throw new CustomShellStartException("Mock shell start failure", "failing-shell");
    }

    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException("Mock shell stop failure");
    }

    public Task WriteInputAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException("Mock shell write failure");
    }

    public void NotifyTerminalResize(int width, int height)
    {
        throw new InvalidOperationException("Mock shell resize failure");
    }

    public void RequestCancellation()
    {
        // Mock implementation - do nothing
    }

    public void SendInitialOutput()
    {
        // Mock implementation - do nothing
    }

    public void Dispose()
    {
        // Mock implementation - do nothing
    }
}