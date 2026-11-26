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
                // Snapshot first: the tail-before-exit contract is about what had
                // been delivered when the event fired.
                OutputAtExit = Output;
                Exited.TrySetResult(e.ExitCode);
            };
            Manager.ProcessError += (_, e) => Interlocked.Increment(ref _errors);
        }

        private int _errors;

        public int ExitEvents => Volatile.Read(ref _exitEvents);

        /// <summary>ProcessError events raised so far.</summary>
        public int Errors => Volatile.Read(ref _errors);

        /// <summary>Output delivered before ProcessExited was raised.</summary>
        public string? OutputAtExit { get; private set; }

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

        // Cleanup (pseudoconsole close + pump drain) runs before ProcessExited is
        // raised, so the tail was delivered by then — the session's exit latch and
        // close-on-exit rely on this ordering.
        Assert.That(harness.OutputAtExit, Does.Contain("purr-dying-words"),
            $"tail output must be delivered before ProcessExited. Output at exit: '{harness.OutputAtExit}'");
    }

    [Test]
    public async Task RemovedEnvironmentVariables_Are_Absent_Inherited_Or_Default()
    {
        // Twin of the Unix test. `set NAME` lists matching variables and fails when there are none.
        const string Inherited = "PURRTTY_TEST_INHERITED_VARIABLE";
        Environment.SetEnvironmentVariable(Inherited, "from-parent");
        try
        {
            using var harness = new Harness();
            var options = CmdOptions("(set", "FORCE_COLOR", "||", "echo", "fc-unset)", "&", "(set", Inherited, "||", "echo", "inh-unset)", "&", "echo", "ct=%COLORTERM%");
            options.RemovedEnvironmentVariables.AddRange(["FORCE_COLOR", Inherited]);
            await harness.Manager.StartAsync(options);

            await harness.WaitForOutput("ct=truecolor");
            Assert.That(harness.Output, Does.Contain("fc-unset").And.Contain("inh-unset"), harness.Output);
            Assert.That(harness.Output, Does.Not.Contain("from-parent"), harness.Output);
        }
        finally
        {
            Environment.SetEnvironmentVariable(Inherited, null);
        }
    }

    [Test]
    public async Task Resize_After_Exit_Is_Ignored()
    {
        using var harness = new Harness();
        await harness.Manager.StartAsync(CmdOptions("exit", "3"));

        int exitCode = await harness.Exited.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // Twin of the Unix test: a dead terminal keeps being resized by its window;
        // that is a silent no-op (it threw "No process is running" before).
        Assert.DoesNotThrow(() => harness.Manager.Resize(120, 40));
        Assert.DoesNotThrow(() => harness.Manager.Resize(80, 24));
        Assert.That(exitCode, Is.EqualTo(3));
        Assert.That(harness.Manager.ExitCode, Is.EqualTo(3), "a no-op resize must not disturb the exit code");
        Assert.That(harness.Errors, Is.Zero, "a resize after exit is not an error");
    }

    [Test]
    public void Resize_Before_Start_And_After_Dispose()
    {
        var manager = new ProcessManager();
        Assert.DoesNotThrow(() => manager.Resize(100, 30), "before start: no pseudoconsole, a no-op");

        manager.Dispose();
        Assert.Throws<ObjectDisposedException>(() => manager.Resize(80, 24));
    }

    [Test]
    public async Task Resize_Racing_Exit_Never_Throws()
    {
        // Resize as fast as possible while the Exited callback runs cleanup on the
        // threadpool. The decision is made on the pseudoconsole handle under the
        // manager's lock (Process.HasExited used to be checked first and threw in the
        // window between the client's death and cleanup), so no interleaving throws.
        // Twin of the Unix test: the child waits for a line (set /p), so the first resizes reach
        // it alive; the line then lets it exit at once while the resizes go on.
        for (int round = 0; round < 10; round++)
        {
            using var harness = new Harness();
            await harness.Manager.StartAsync(CmdOptions("set", "/p", "x=", "&", "exit", "0"));

            int resizes = 0;
            for (int i = 0; i < 20; i++)
            {
                short alive = (short)(80 + (resizes++ % 40));
                Assert.DoesNotThrow(() => harness.Manager.Resize(alive, 24), $"round {round}, resize {resizes} (child alive)");
            }

            Assert.That(harness.Exited.Task.IsCompleted, Is.False, $"round {round}: the child must still be alive, waiting for its line");
            Assert.That(harness.Manager.IsRunning, Is.True);
            harness.Manager.Write("\r\n");

            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!harness.Exited.Task.IsCompleted && DateTime.UtcNow < deadline)
            {
                short cols = (short)(80 + (resizes++ % 40));
                Assert.DoesNotThrow(() => harness.Manager.Resize(cols, 24), $"round {round}, resize {resizes}");
            }

            Assert.That(harness.Exited.Task.IsCompleted, Is.True, $"round {round}: the child never reported its exit");
            Assert.DoesNotThrow(() => harness.Manager.Resize(81, 25), $"round {round}: resize after exit");
        }
    }
}
