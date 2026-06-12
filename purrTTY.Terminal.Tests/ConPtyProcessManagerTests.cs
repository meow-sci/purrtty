using System.Text;
using NUnit.Framework;
using purrTTY.Core.Terminal;

namespace PurrTTY.Terminal.Tests;

/// <summary>
/// Integration tests for the ConPTY backend (<see cref="ProcessManager"/>): real
/// cmd.exe children on a real pseudoconsole. The Windows twin of
/// <see cref="UnixProcessManagerTests"/>; skipped on Linux/macOS. Pins the
/// unified fast-exit contract: a shell that spawns and dies promptly is reported
/// once, via ProcessExited with its real exit code — never as a start failure.
/// (The former 100 ms post-spawn validation raced the Exited callback, so a fast
/// exit nondeterministically produced a ProcessStartException, a ProcessExited,
/// or both.)
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class ConPtyProcessManagerTests
{
    [SetUp]
    public void SkipOnNonWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            // ConPTY backend is Windows-only - skip silently
            Assert.Ignore();
        }
    }

    private sealed class Harness : IDisposable
    {
        public ProcessManager Manager { get; } = new();
        public TaskCompletionSource<int> Exited { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly StringBuilder _output = new();
        private readonly object _outputLock = new();
        private int _exitEvents;

        public Harness()
        {
            Manager.DataReceived += (_, e) =>
            {
                lock (_outputLock)
                {
                    _output.Append(Encoding.UTF8.GetString(e.Data.Span));
                }
            };
            Manager.ProcessExited += (_, e) =>
            {
                Interlocked.Increment(ref _exitEvents);
                Exited.TrySetResult(e.ExitCode);
            };
        }

        public int ExitEvents => Volatile.Read(ref _exitEvents);

        public string Output
        {
            get
            {
                lock (_outputLock)
                {
                    return _output.ToString();
                }
            }
        }

        public async Task WaitForOutput(string fragment, int timeoutMs = 10_000)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                if (Output.Contains(fragment))
                {
                    return;
                }

                await Task.Delay(25);
            }

            Assert.Fail($"Timed out waiting for output containing '{fragment}'. Output so far: {Output}");
        }

        public void Dispose() => Manager.Dispose();
    }

    private static ProcessLaunchOptions CmdOptions(params string[] args)
        => ProcessLaunchOptions.CreateCustom("cmd.exe", ["/c", .. args]);

    [Test]
    public async Task FastExit_ReportsOnceViaProcessExited_NotAsStartFailure()
    {
        using var harness = new Harness();

        // The exit must surface on the exit path with the real code — StartAsync
        // succeeding is the contract under test.
        await harness.Manager.StartAsync(CmdOptions("exit", "7"));

        int exitCode = await harness.Exited.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.That(exitCode, Is.EqualTo(7));
        Assert.That(harness.ExitEvents, Is.EqualTo(1));
        Assert.That(harness.Manager.IsRunning, Is.False);
        Assert.That(harness.Manager.ExitCode, Is.EqualTo(7), "exit code is retained past cleanup");
    }

    [Test]
    public async Task FastExit_TailOutput_IsDelivered()
    {
        using var harness = new Harness();

        // Teardown closes the pseudoconsole first so conhost flushes the dying
        // shell's final output through the pump — the user must see a fast-dying
        // shell's error text, not an empty grid.
        await harness.Manager.StartAsync(CmdOptions("echo", "purr-dying-words"));

        await harness.WaitForOutput("purr-dying-words");
        await harness.Exited.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }
}
