using System.Text;
using NUnit.Framework;
using purrTTY.Core.Terminal;

namespace PurrTTY.Terminal.Tests;

/// <summary>
/// Integration tests for the POSIX pty backend (<see cref="UnixProcessManager"/>):
/// real /bin/sh children on a real pty. These run on macOS (dev) and linux-x64 (CI)
/// and are skipped on Windows — together with the Windows manual testing this is the
/// cross-platform coverage for shell launching.
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class UnixProcessManagerTests
{
    [SetUp]
    public void SkipOnWindows()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("POSIX pty backend is Linux/macOS only");
        }
    }

    private sealed class Harness : IDisposable
    {
        public UnixProcessManager Manager { get; } = new();
        public TaskCompletionSource<int> Exited { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly StringBuilder _output = new();
        private readonly object _outputLock = new();

        public Harness()
        {
            Manager.DataReceived += (_, e) =>
            {
                lock (_outputLock)
                {
                    _output.Append(Encoding.UTF8.GetString(e.Data.Span));
                }
            };
            Manager.ProcessExited += (_, e) => Exited.TrySetResult(e.ExitCode);
        }

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

    private static ProcessLaunchOptions ShOptions(string script)
        => ProcessLaunchOptions.CreateCustom("/bin/sh", "-c", script);

    [Test]
    public async Task RunsCommand_CapturesOutput_AndExitCode()
    {
        using var harness = new Harness();
        await harness.Manager.StartAsync(ShOptions("echo purr-$((20+3)); exit 7"));

        await harness.WaitForOutput("purr-23");
        int exitCode = await harness.Exited.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.That(exitCode, Is.EqualTo(7));
        Assert.That(harness.Manager.IsRunning, Is.False);
        Assert.That(harness.Manager.ExitCode, Is.EqualTo(7));
    }

    [Test]
    public async Task Write_RoundTripsThroughPty_AndStopKillsChild()
    {
        using var harness = new Harness();
        await harness.Manager.StartAsync(ShOptions("cat"));
        Assert.That(harness.Manager.IsRunning, Is.True);
        Assert.That(harness.Manager.ProcessId, Is.Not.Null);

        harness.Manager.Write("meow\n");
        // pty echo + cat's own output both come back on the master
        await harness.WaitForOutput("meow");

        await harness.Manager.StopAsync();
        Assert.That(harness.Manager.IsRunning, Is.False);
        await harness.Exited.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Test]
    public async Task InitialWinsize_IsAppliedBeforeShellStarts()
    {
        using var harness = new Harness();
        var options = ShOptions("stty size");
        options.InitialWidth = 101;
        options.InitialHeight = 31;
        await harness.Manager.StartAsync(options);

        await harness.WaitForOutput("31 101");
    }

    [Test]
    public async Task WorkingDirectory_IsApplied()
    {
        string dir = Directory.CreateTempSubdirectory("purrtty-pty-test").FullName;
        try
        {
            using var harness = new Harness();
            var options = ShOptions("pwd");
            options.WorkingDirectory = dir;
            await harness.Manager.StartAsync(options);

            // Compare by basename: macOS temp paths traverse symlinks (/var → /private/var)
            await harness.WaitForOutput(Path.GetFileName(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Test]
    public async Task Resize_WhileRunning_DoesNotThrow()
    {
        using var harness = new Harness();
        await harness.Manager.StartAsync(ShOptions("cat"));

        Assert.DoesNotThrow(() => harness.Manager.Resize(120, 40));

        await harness.Manager.StopAsync();
    }

    [Test]
    public async Task AutoShell_ResolvesAndRuns()
    {
        using var harness = new Harness();
        // Auto resolves $SHELL or zsh/bash/sh; launch interactively-less by feeding exit
        await harness.Manager.StartAsync(ProcessLaunchOptions.CreateDefault());
        Assert.That(harness.Manager.IsRunning, Is.True);

        harness.Manager.Write("exit 0\n");
        int exitCode = await harness.Exited.Task.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.That(exitCode, Is.EqualTo(0));
    }

    [Test]
    public void UnixShellDetector_FindsAtLeastOneShell_WithDefaultFirst()
    {
        var shells = UnixShellDetector.GetInstalledShells(forceRefresh: true);
        Assert.That(shells, Is.Not.Empty);
        Assert.That(shells.Select(s => s.Path), Is.All.Matches<string>(File.Exists));

        if (Environment.GetEnvironmentVariable("SHELL") is { Length: > 0 } shell && File.Exists(shell))
        {
            Assert.That(shells[0].IsDefault, Is.True);
        }
    }
}
