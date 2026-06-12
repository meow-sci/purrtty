using System.Text;
using NUnit.Framework;
using purrTTY.Core.Terminal;

namespace PurrTTY.Terminal.Tests;

/// <summary>
/// Pins the <see cref="CustomShellPtyBridge"/> adapter contract — the sole
/// bridge between <see cref="ICustomShell"/> implementations and the
/// <see cref="IProcessManager"/> session machinery. Pure logic with a stub
/// shell; no game types or real PTYs involved.
/// </summary>
[TestFixture]
public sealed class CustomShellPtyBridgeTests
{
    [Test]
    public async Task StartAsync_ConvertsOptions_AndMarksRunning()
    {
        using var shell = new StubShell();
        using var bridge = new CustomShellPtyBridge(shell);

        var options = ProcessLaunchOptions.CreateCustomGame("StubShell");
        options.InitialWidth = 132;
        options.InitialHeight = 43;
        options.EnvironmentVariables["MARKER"] = "value";

        await bridge.StartAsync(options);

        Assert.That(bridge.IsRunning, Is.True);
        Assert.That(shell.StartOptions, Is.Not.Null);
        Assert.That(shell.StartOptions!.InitialWidth, Is.EqualTo(132));
        Assert.That(shell.StartOptions.InitialHeight, Is.EqualTo(43));
        Assert.That(shell.StartOptions.EnvironmentVariables["MARKER"], Is.EqualTo("value"));
    }

    [Test]
    public async Task StartAsync_WhileStarting_OrRunning_Throws()
    {
        using var shell = new StubShell();
        using var bridge = new CustomShellPtyBridge(shell);
        await bridge.StartAsync(ProcessLaunchOptions.CreateCustomGame("StubShell"));

        Assert.ThrowsAsync<InvalidOperationException>(
            () => bridge.StartAsync(ProcessLaunchOptions.CreateCustomGame("StubShell")));
    }

    [Test]
    public void StartAsync_ShellFailure_WrapsInProcessStartException()
    {
        // IProcessManager's contract: start failures throw ProcessStartException.
        // CustomShellStartException does NOT derive from it, so the bridge must
        // wrap it (an unwrapped escape breaks every caller that catches per the
        // interface contract).
        using var shell = new StubShell { FailStartWith = new CustomShellStartException("boom") };
        using var bridge = new CustomShellPtyBridge(shell);

        var ex = Assert.ThrowsAsync<ProcessStartException>(
            () => bridge.StartAsync(ProcessLaunchOptions.CreateCustomGame("StubShell")));
        Assert.That(ex!.InnerException, Is.TypeOf<CustomShellStartException>());
        Assert.That(bridge.IsRunning, Is.False);
    }

    [Test]
    public async Task ShellOutput_ForwardsAsDataReceived_WithStderrFlag()
    {
        using var shell = new StubShell();
        using var bridge = new CustomShellPtyBridge(shell);
        await bridge.StartAsync(ProcessLaunchOptions.CreateCustomGame("StubShell"));

        var received = new List<(byte[] Data, bool IsError)>();
        bridge.DataReceived += (_, e) => received.Add((e.Data.ToArray(), e.IsError));

        shell.EmitOutput("out"u8.ToArray(), ShellOutputType.Stdout);
        shell.EmitOutput("err"u8.ToArray(), ShellOutputType.Stderr);

        Assert.That(received, Has.Count.EqualTo(2));
        Assert.That(Encoding.UTF8.GetString(received[0].Data), Is.EqualTo("out"));
        Assert.That(received[0].IsError, Is.False);
        Assert.That(Encoding.UTF8.GetString(received[1].Data), Is.EqualTo("err"));
        Assert.That(received[1].IsError, Is.True);
    }

    [Test]
    public async Task Write_ForwardsBytesToShellInput()
    {
        using var shell = new StubShell();
        using var bridge = new CustomShellPtyBridge(shell);
        await bridge.StartAsync(ProcessLaunchOptions.CreateCustomGame("StubShell"));

        bridge.Write("ls\r"u8);

        Assert.That(Encoding.UTF8.GetString(shell.InputBytes.ToArray()), Is.EqualTo("ls\r"));
    }

    [Test]
    public void Write_BeforeStart_Throws()
    {
        using var shell = new StubShell();
        using var bridge = new CustomShellPtyBridge(shell);

        Assert.Throws<InvalidOperationException>(() => bridge.Write("x"u8));
    }

    [Test]
    public async Task Resize_ForwardsToShellNotifyTerminalResize()
    {
        using var shell = new StubShell();
        using var bridge = new CustomShellPtyBridge(shell);
        await bridge.StartAsync(ProcessLaunchOptions.CreateCustomGame("StubShell"));

        bridge.Resize(120, 40);

        Assert.That(shell.Resizes, Is.EqualTo(new[] { (120, 40) }));
    }

    [Test]
    public async Task ShellTerminated_RaisesProcessExited_AndRetainsExitCode()
    {
        using var shell = new StubShell();
        using var bridge = new CustomShellPtyBridge(shell);
        await bridge.StartAsync(ProcessLaunchOptions.CreateCustomGame("StubShell"));

        ProcessExitedEventArgs? exited = null;
        bridge.ProcessExited += (_, e) => exited = e;

        shell.EmitTerminated(7, "done");

        Assert.That(exited, Is.Not.Null);
        Assert.That(exited!.ExitCode, Is.EqualTo(7));
        Assert.That(bridge.IsRunning, Is.False);
        Assert.That(bridge.ExitCode, Is.EqualTo(7));
    }

    [Test]
    public void TerminatedDuringStart_LeavesBridgeStopped()
    {
        // A shell that dies synchronously inside its own StartAsync must not be
        // resurrected to "running" by the bridge's post-start bookkeeping —
        // and must not be asked for startup output it can no longer deliver.
        using var shell = new StubShell { TerminateDuringStart = true };
        using var bridge = new CustomShellPtyBridge(shell);

        Assert.DoesNotThrowAsync(() => bridge.StartAsync(ProcessLaunchOptions.CreateCustomGame("StubShell")));
        Assert.That(bridge.IsRunning, Is.False);
        Assert.That(bridge.ExitCode, Is.EqualTo(1));
        Assert.That(shell.InitialOutputSends, Is.Zero);
    }

    [Test]
    public async Task StartAsync_TriggersShellInitialOutput()
    {
        // A real PTY shell banners as a consequence of spawning; the bridge
        // must trigger the custom shell's equivalent so Game Console sessions
        // show their banner/prompt without any extra host call.
        using var shell = new StubShell();
        using var bridge = new CustomShellPtyBridge(shell);

        await bridge.StartAsync(ProcessLaunchOptions.CreateCustomGame("StubShell"));

        Assert.That(shell.InitialOutputSends, Is.EqualTo(1));
    }

    [Test]
    public async Task Restart_AfterTerminatingStop_RunsCleanly()
    {
        // GameConsoleShell raises Terminated from its stop hook. The bridge's
        // exit-code source must be per-run: reusing the completed one made a
        // restarted session look already-terminated (IsRunning stuck false →
        // input silently dropped, no banner, stale ExitCode).
        using var shell = new StubShell { TerminateOnStop = true };
        using var bridge = new CustomShellPtyBridge(shell);
        var options = ProcessLaunchOptions.CreateCustomGame("StubShell");

        await bridge.StartAsync(options);
        await bridge.StopAsync();
        Assert.That(bridge.IsRunning, Is.False);
        Assert.That(bridge.ExitCode, Is.EqualTo(0));

        await bridge.StartAsync(options);

        Assert.That(bridge.IsRunning, Is.True);
        Assert.That(bridge.ExitCode, Is.Null, "a fresh run has no exit code until it exits");
        Assert.That(shell.InitialOutputSends, Is.EqualTo(2), "each successful start banners");
        Assert.DoesNotThrow(() => bridge.Write("echo hi\r"u8));
        Assert.That(Encoding.UTF8.GetString(shell.InputBytes.ToArray()), Is.EqualTo("echo hi\r"));
    }

    /// <summary>Minimal scripted <see cref="ICustomShell"/> for adapter tests.</summary>
    private sealed class StubShell : ICustomShell
    {
        public CustomShellStartOptions? StartOptions { get; private set; }
        public List<byte> InputBytes { get; } = new();
        public List<(int Width, int Height)> Resizes { get; } = new();
        public Exception? FailStartWith { get; init; }
        public bool TerminateDuringStart { get; init; }
        public bool TerminateOnStop { get; init; }
        public int InitialOutputSends { get; private set; }

        public CustomShellMetadata Metadata { get; } = CustomShellMetadata.Create(
            name: "Stub Shell",
            description: "bridge contract test shell",
            version: new Version(1, 0, 0),
            author: "tests",
            supportedFeatures: Array.Empty<string>());

        public bool IsRunning { get; private set; }

        public event EventHandler<ShellOutputEventArgs>? OutputReceived;
        public event EventHandler<ShellTerminatedEventArgs>? Terminated;

        public Task StartAsync(CustomShellStartOptions options, CancellationToken cancellationToken = default)
        {
            if (FailStartWith is not null)
            {
                throw FailStartWith;
            }

            StartOptions = options;
            IsRunning = true;
            if (TerminateDuringStart)
            {
                EmitTerminated(1, "died on start");
            }

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            IsRunning = false;
            if (TerminateOnStop)
            {
                // Mirrors GameConsoleShell, whose stop hook raises Terminated.
                EmitTerminated(0, "stopped");
            }

            return Task.CompletedTask;
        }

        public Task WriteInputAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
        {
            InputBytes.AddRange(data.ToArray());
            return Task.CompletedTask;
        }

        public void NotifyTerminalResize(int width, int height) => Resizes.Add((width, height));

        public void SendInitialOutput() => InitialOutputSends++;

        public void RequestCancellation()
        {
        }

        public void EmitOutput(byte[] data, ShellOutputType type)
            => OutputReceived?.Invoke(this, new ShellOutputEventArgs(data, type));

        public void EmitTerminated(int exitCode, string reason)
        {
            IsRunning = false;
            Terminated?.Invoke(this, new ShellTerminatedEventArgs(exitCode, reason));
        }

        public void Dispose()
        {
        }
    }
}
