using System.Text;
using System.Threading.Channels;

namespace caTTY.Core.Terminal;

/// <summary>
///     Base class for custom shells that use a channel-based async output pump pattern.
///     This mimics PTY stdout behavior where output is queued to a channel and
///     a background task pumps it to OutputReceived events.
/// </summary>
public abstract class BaseChannelOutputShell : BaseCustomShell
{
    /// <summary>
    ///     Output channel that mimics PTY stdout/stderr - unbounded like a real pipe buffer.
    ///     Carries both data and output type (stdout vs stderr).
    /// </summary>
    private Channel<(byte[] Data, ShellOutputType Type)>? _outputChannel;

    /// <summary>
    ///     Background task that pumps output from the channel to OutputReceived events.
    /// </summary>
    private Task? _outputPumpTask;

    /// <summary>
    ///     Cancellation token source for the output pump task.
    /// </summary>
    private CancellationTokenSource? _outputPumpCancellation;

    /// <summary>
    ///     Hook called during StartAsync, before the output pump is started.
    ///     Derived classes should perform their initialization here.
    /// </summary>
    /// <param name="options">Start options for the shell</param>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>A task that completes when initialization is done</returns>
    protected abstract Task OnStartingAsync(CustomShellStartOptions options, CancellationToken cancellationToken);

    /// <summary>
    ///     Hook called during StopAsync, before the output channel is completed.
    ///     Derived classes should perform their cleanup here.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>A task that completes when cleanup is done</returns>
    protected abstract Task OnStoppingAsync(CancellationToken cancellationToken);

    /// <inheritdoc />
    public override async Task StartAsync(CustomShellStartOptions options, CancellationToken cancellationToken = default)
    {
        if (IsRunning)
        {
            throw new InvalidOperationException("Shell is already running");
        }

        // Create PTY-style output channel - unbounded like a real pipe buffer
        // This channel acts as the "stdout/stderr pipes" that a real shell would write to
        _outputChannel = Channel.CreateUnbounded<(byte[] Data, ShellOutputType Type)>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false // Multiple sources can write (shell, command output, etc.)
        });

        // Call derived class initialization hook
        await OnStartingAsync(options, cancellationToken);

        // Start the output pump - this is like ProcessManager's ReadOutputAsync task
        // It runs on a background thread and raises OutputReceived events as data arrives
        _outputPumpCancellation = new CancellationTokenSource();
        _outputPumpTask = Task.Run(() => OutputPumpAsync(_outputPumpCancellation.Token), cancellationToken);

        IsRunning = true;

        // Shell is now ready to accept input with cursor at 0,0
        // Initial output (banner/prompt) will be sent via SendInitialOutput()
        // AFTER the session is fully initialized and wired up
    }

    /// <inheritdoc />
    public override async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!IsRunning)
        {
            return;
        }

        IsRunning = false;

        // Call derived class cleanup hook
        await OnStoppingAsync(cancellationToken);

        // Complete the output channel - this signals the pump to finish after draining
        _outputChannel?.Writer.Complete();

        // Wait for the output pump to finish processing remaining items
        if (_outputPumpTask != null)
        {
            try
            {
                // Wait for pump to drain, but don't wait forever
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(2));
                await _outputPumpTask.WaitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                // Pump didn't finish in time, cancel it
                _outputPumpCancellation?.Cancel();
            }
        }

        // Cleanup
        _outputPumpCancellation?.Dispose();
        _outputPumpCancellation = null;
        _outputPumpTask = null;
    }

    /// <summary>
    ///     Background task that pumps output from the channel to OutputReceived events.
    ///     This mimics how ProcessManager's ReadOutputAsync reads from the ConPTY pipe.
    /// </summary>
    private async Task OutputPumpAsync(CancellationToken cancellationToken)
    {
        if (_outputChannel == null) return;

        try
        {
            await foreach (var (data, type) in _outputChannel.Reader.ReadAllAsync(cancellationToken))
            {
                try
                {
                    // Raise the event from this background thread, just like ProcessManager does
                    RaiseOutputReceived(data, type);
                }
                catch (Exception)
                {
                    // Silently handle errors to avoid disrupting the output pump
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when shell is stopped
        }
        catch (Exception)
        {
            // Silently handle errors to avoid crashing the background task
        }
    }

    /// <summary>
    ///     Queues raw byte data to the output channel for asynchronous delivery.
    ///     This is like a shell writing to its stdout file descriptor.
    ///     Defaults to stdout.
    /// </summary>
    /// <param name="data">The data to queue</param>
    protected void QueueOutput(byte[] data)
    {
        QueueOutput(data, ShellOutputType.Stdout);
    }

    /// <summary>
    ///     Queues text to the output channel for asynchronous delivery.
    ///     Text is converted to UTF-8 bytes.
    ///     Defaults to stdout.
    /// </summary>
    /// <param name="text">The text to queue</param>
    protected void QueueOutput(string text)
    {
        QueueOutput(text, ShellOutputType.Stdout);
    }

    /// <summary>
    ///     Queues raw byte data to the output channel for asynchronous delivery with specified output type.
    ///     This is like a shell writing to its stdout or stderr file descriptor.
    /// </summary>
    /// <param name="data">The data to queue</param>
    /// <param name="type">The output type (stdout or stderr)</param>
    protected void QueueOutput(byte[] data, ShellOutputType type)
    {
        _outputChannel?.Writer.TryWrite((data, type));
    }

    /// <summary>
    ///     Queues text to the output channel for asynchronous delivery with specified output type.
    ///     Text is converted to UTF-8 bytes.
    /// </summary>
    /// <param name="text">The text to queue</param>
    /// <param name="type">The output type (stdout or stderr)</param>
    protected void QueueOutput(string text, ShellOutputType type)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        QueueOutput(bytes, type);
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        if (IsRunning)
        {
            // Complete the channel to stop the pump
            _outputChannel?.Writer.TryComplete();

            // Cancel the pump immediately
            _outputPumpCancellation?.Cancel();

            // Wait briefly for cleanup
            try
            {
                _outputPumpTask?.Wait(TimeSpan.FromSeconds(1));
            }
            catch
            {
                // Ignore errors during disposal
            }

            _outputPumpCancellation?.Dispose();
            _outputPumpCancellation = null;
            _outputPumpTask = null;

            IsRunning = false;
        }

        base.Dispose();
    }
}
