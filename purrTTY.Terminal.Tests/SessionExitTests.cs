using System.Text;
using NUnit.Framework;
using PurrTTY.Terminal.Rendering;
using PurrTTY.Terminal.Sessions;
using purrTTY.Core.Terminal;

namespace PurrTTY.Terminal.Tests;

/// <summary>
/// Session-level exit handling against real child processes (POSIX pty on Linux/macOS,
/// ConPTY on Windows — one test body, the command chosen per OS): the
/// <see cref="ProcessLaunchOptions.CloseOnExit"/> option closes and removes an exited
/// session on the tick thread (<see cref="SessionManager.CloseExitedSessions"/>); without
/// it the session stays with its final screen and exit code and survives resize, input,
/// scroll and close. The test thread plays the frontend tick thread: it alone calls
/// <c>BuildFrame</c>/<c>CloseExitedSessions</c>, polling at a short interval inside a
/// deadline-bounded condition loop (a real external process offers no completion signal
/// on this thread).
/// </summary>
[TestFixture]
[NonParallelizable]
public sealed class SessionExitTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    /// <summary>A one-shot program: <paramref name="unixScript"/> under /bin/sh, or cmd.exe /c <paramref name="cmdArgs"/>.</summary>
    private static ProcessLaunchOptions OneShot(string unixScript, params string[] cmdArgs)
        => OperatingSystem.IsWindows()
            ? ProcessLaunchOptions.CreateCustom("cmd.exe", ["/c", .. cmdArgs])
            : ProcessLaunchOptions.CreateCustom("/bin/sh", "-c", unixScript);

    private static SessionManager NewManager() => new(4, ProcessLaunchOptions.CreateDefault());

    /// <summary>One frontend tick: close exited CloseOnExit sessions, then tick every surface.</summary>
    private static IReadOnlyList<Guid> Tick(SessionManager manager)
    {
        var closed = manager.CloseExitedSessions();
        var sessions = manager.Sessions;
        for (int i = 0; i < sessions.Count; i++)
        {
            sessions[i].Surface.BuildFrame();
        }

        return closed;
    }

    private static Task TickUntil(SessionManager manager, Func<bool> condition, string what, List<Guid>? closed = null)
        => TickUntil(manager, condition, () => what, closed);

    private static async Task TickUntil(SessionManager manager, Func<bool> condition, Func<string> what, List<Guid>? closed = null)
    {
        var deadline = DateTime.UtcNow + Deadline;
        while (DateTime.UtcNow < deadline)
        {
            var closedThisTick = Tick(manager);
            closed?.AddRange(closedThisTick);
            if (condition())
            {
                return;
            }

            await Task.Delay(10); // polling interval of a deadline-bounded wait on a real process
        }

        Assert.Fail($"Timed out waiting for {what()}. Sessions: {manager.SessionCount}");
    }

    private static string ScreenText(TerminalFrame frame)
    {
        var sb = new StringBuilder();
        for (int r = 0; r < frame.Rows; r++)
        {
            for (int c = 0; c < frame.Cols; c++)
            {
                sb.Append(frame.RowData[r].Cells[c].Grapheme ?? " ");
            }

            sb.Append('\n');
        }

        return sb.ToString();
    }

    [Test]
    public void CloseOnExit_DefaultsOff_AndRoundTripsThroughClone()
    {
        var options = ProcessLaunchOptions.CreateCustom("nvim", "file.kc");
        Assert.That(options.CloseOnExit, Is.False, "existing launches must keep today's behaviour");
        Assert.That(options.Clone().CloseOnExit, Is.False);

        options.CloseOnExit = true;
        var clone = options.Clone();
        Assert.That(clone.CloseOnExit, Is.True, "SessionManager clones before stamping dimensions; the option must survive");
        Assert.That(clone, Is.Not.SameAs(options));
    }

    [Test]
    public async Task CloseOnExit_Closes_Window()
    {
        // At this layer "the window closes" is: the session leaves its manager, closed
        // and disposed, on the tick thread, with its exit code still observable. The
        // window's own reaction (retire when no tab is left) is SessionExitPolicy.
        using var manager = NewManager();
        var options = OneShot("exit 3", "exit", "3");
        options.CloseOnExit = true;

        TerminalSession? closedEventSession = null;
        manager.SessionClosed += (_, e) => closedEventSession = e.Session;

        var session = await manager.CreateSessionAsync("one-shot", options);
        Assert.That(session.CloseOnExit, Is.True);

        var closed = new List<Guid>();
        await TickUntil(manager, () => manager.SessionCount == 0, "the exited CloseOnExit session to be closed", closed);

        Assert.That(closed, Is.EqualTo(new[] { session.Id }), "CloseExitedSessions reports exactly the session it closed");
        Assert.That(manager.ActiveSession, Is.Null);
        Assert.That(manager.Sessions, Is.Empty);
        Assert.That(session.State, Is.EqualTo(SessionState.Disposed));
        Assert.That(closedEventSession, Is.SameAs(session), "the normal SessionClosed path is used");
        Assert.That(session.HasProcessExited, Is.True);
        Assert.That(session.ExitCode, Is.EqualTo(3), "the exit code stays observable after the close");
        Assert.That(Tick(manager), Is.Empty, "nothing left to close");
    }

    [Test]
    public async Task CloseOnExit_ClosesOnlyTheExitedSession()
    {
        using var manager = NewManager();
        var shell = OneShot("cat", "more"); // stays alive until closed
        var oneShot = OneShot("exit 0", "exit", "0");
        oneShot.CloseOnExit = true;

        var survivor = await manager.CreateSessionAsync("shell", shell);
        var dying = await manager.CreateSessionAsync("one-shot", oneShot);

        await TickUntil(manager, () => manager.SessionCount == 1,
            () => $"the one-shot tab to close (latch {dying.HasProcessExited}, running {dying.ProcessManager.IsRunning}, code {dying.ProcessManager.ExitCode}, state {dying.State}, closeOnExit {dying.CloseOnExit})");

        Assert.That(manager.Sessions, Is.EqualTo(new[] { survivor }));
        Assert.That(manager.ActiveSession, Is.SameAs(survivor), "the remaining tab becomes active, as for a user close");
        Assert.That(dying.State, Is.EqualTo(SessionState.Disposed));
        Assert.That(survivor.HasProcessExited, Is.False);
    }

    [Test]
    public async Task WithoutCloseOnExit_Session_Stays_WithFinalScreen_AndSurvivesUse()
    {
        using var manager = NewManager();
        var options = OneShot("printf 'purr-final-screen'; exit 4", "echo", "purr-final-screen&", "exit", "4");

        var session = await manager.CreateSessionAsync("stays", options);
        Assert.That(session.CloseOnExit, Is.False);

        // Wait on the latch only — no ticking — so the very next BuildFrame proves the
        // tail was already in the inbox when the exit was reported.
        var deadline = DateTime.UtcNow + Deadline;
        while (!session.HasProcessExited && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.That(session.HasProcessExited, Is.True, "the process never reported its exit");
        Assert.That(Tick(manager), Is.Empty, "a session without CloseOnExit is never closed on exit");

        string screen = ScreenText(session.Surface.BuildFrame());
        Assert.That(screen, Does.Contain("purr-final-screen"), $"the program's final output must be on screen. Screen:\n{screen}");

        Assert.That(manager.SessionCount, Is.EqualTo(1));
        Assert.That(session.State, Is.Not.EqualTo(SessionState.Disposed));
        Assert.That(session.ExitCode, Is.EqualTo(4));
        Assert.That(session.ProcessManager.ExitCode, Is.EqualTo(4), "what the tab label's \" (exit N)\" reads");

        // A dead session is safe to resize (surface still reflows), type into, scroll and close.
        Assert.DoesNotThrow(() => session.Resize(100, 30, 8, 16));
        Assert.That((session.Surface.Cols, session.Surface.Rows), Is.EqualTo((100, 30)));
        Assert.That((session.Settings.Columns, session.Settings.Rows), Is.EqualTo((100, 30)));
        Assert.DoesNotThrow(() => session.SendInput("ignored\r"u8));
        Assert.DoesNotThrow(() => session.Surface.ScrollBy(-5));
        Assert.DoesNotThrow(() => session.Surface.ScrollToBottom());
        Assert.DoesNotThrow(() => session.Surface.BuildFrame());
        Assert.DoesNotThrow(() => manager.CloseSessionAsync(session.Id).GetAwaiter().GetResult());
        Assert.That(manager.SessionCount, Is.Zero);
    }

    [Test]
    public void CloseOnSuccessfulExit_DefaultsOff_AndRoundTripsThroughClone()
    {
        var options = ProcessLaunchOptions.CreateCustom("nvim", "file.kc");
        Assert.That(options.CloseOnSuccessfulExit, Is.False);
        options.CloseOnSuccessfulExit = true;
        options.RemovedEnvironmentVariables.Add("FORCE_COLOR");
        var clone = options.Clone();
        Assert.That((clone.CloseOnSuccessfulExit, clone.CloseOnExit), Is.EqualTo((true, false)));
        Assert.That(clone.RemovedEnvironmentVariables, Is.EqualTo(new[] { "FORCE_COLOR" }));
        Assert.That(clone.RemovedEnvironmentVariables, Is.Not.SameAs(options.RemovedEnvironmentVariables));
    }

    [Test]
    public async Task CloseOnSuccessfulExit_Closes_After_Exit_Zero()
    {
        using var manager = NewManager();
        var options = OneShot("exit 0", "exit", "0");
        options.CloseOnSuccessfulExit = true;

        var session = await manager.CreateSessionAsync("editor", options);
        await TickUntil(manager, () => manager.SessionCount == 0, "the successful one-shot to be closed");

        Assert.That(session.State, Is.EqualTo(SessionState.Disposed));
        Assert.That(session.ExitCode, Is.Zero);
    }

    [Test]
    public async Task CloseOnSuccessfulExit_Keeps_A_Failed_Process_With_Its_Final_Screen()
    {
        // An editor that crashes at start, or a wrapper whose target is missing: the window must
        // stay with the error text and " (exit N)", as a terminal without the option does.
        using var manager = NewManager();
        var options = OneShot("printf 'purr-cannot-start'; exit 3", "echo", "purr-cannot-start&", "exit", "3");
        options.CloseOnSuccessfulExit = true;

        var session = await manager.CreateSessionAsync("editor", options);
        await TickUntil(manager, () => session.HasProcessExited, "the failing one-shot to exit");
        for (int i = 0; i < 5; i++)
        {
            Assert.That(Tick(manager), Is.Empty, "a failed process is never closed by CloseOnSuccessfulExit");
        }

        string screen = ScreenText(session.Surface.BuildFrame());
        Assert.That(screen, Does.Contain("purr-cannot-start"), $"the error stays readable. Screen:\n{screen}");
        Assert.That((manager.SessionCount, session.ExitCode, session.ProcessManager.ExitCode), Is.EqualTo((1, (int?)3, (int?)3)));
        Assert.That(session.State, Is.Not.EqualTo(SessionState.Disposed));
    }

    [Test]
    public async Task CloseOnExit_Closes_A_Hidden_Session_That_Is_Never_Rendered()
    {
        // A hidden window drains its sessions on a low cadence and never builds their frames:
        // CloseExitedSessions alone must retire the exited one (TerminalWindow.DrainSessions).
        using var manager = NewManager();
        var options = OneShot("printf 'purr-hidden'; exit 0", "echo", "purr-hidden&", "exit", "0");
        options.CloseOnExit = true;
        var session = await manager.CreateSessionAsync("hidden", options);

        var closed = new List<Guid>();
        var deadline = DateTime.UtcNow + Deadline;
        while (manager.SessionCount > 0 && DateTime.UtcNow < deadline)
        {
            closed.AddRange(manager.CloseExitedSessions());
            await Task.Delay(10); // polling interval of a deadline-bounded wait on a real process
        }

        Assert.That(closed, Is.EqualTo(new[] { session.Id }), $"latch {session.HasProcessExited}, sessions {manager.SessionCount}");
        Assert.That(session.State, Is.EqualTo(SessionState.Disposed));
    }

    [TestCase(0, true)]
    [TestCase(9, false)]
    public async Task A_Process_That_Exits_Before_Publication_Is_Handled_At_The_Next_Tick(int exitCode, bool closes)
    {
        // The shell ends inside its own start, before CreateSessionAsync publishes the session:
        // the latch is already set when the frontend first sees it.
        using var manager = new SessionManager(4, ProcessLaunchOptions.CreateCustomGame(ExitAtStartShell.Id));
        var options = ProcessLaunchOptions.CreateCustomGame(ExitAtStartShell.Id);
        options.EnvironmentVariables[ExitAtStartShell.CodeVariable] = exitCode.ToString(System.Globalization.CultureInfo.InvariantCulture);
        options.CloseOnSuccessfulExit = true;

        var session = await manager.CreateSessionAsync("quick", options);
        Assert.That((session.HasProcessExited, session.ExitCode, manager.SessionCount), Is.EqualTo((true, (int?)exitCode, 1)),
            "published already exited");

        IReadOnlyList<Guid> closed = Tick(manager);
        Assert.That(closed.Count, Is.EqualTo(closes ? 1 : 0));
        Assert.That(manager.SessionCount, Is.EqualTo(closes ? 0 : 1));
    }

    [OneTimeSetUp]
    public void RegisterExitAtStartShell()
    {
        try
        {
            purrTTY.Core.Terminal.CustomShellRegistry.Instance.RegisterShell(ExitAtStartShell.Id, () => new ExitAtStartShell());
        }
        catch (ArgumentException)
        {
            // Already registered by a previous run in the same process.
        }
    }

    /// <summary>A headless custom shell that terminates inside its <c>StartAsync</c> with the code in <see cref="CodeVariable"/>.</summary>
    private sealed class ExitAtStartShell : ICustomShell
    {
        public const string Id = nameof(ExitAtStartShell);
        public const string CodeVariable = "PURRTTY_TEST_EXIT_CODE";

        public CustomShellMetadata Metadata { get; } = CustomShellMetadata.Create(
            name: "Exit At Start Shell", description: "ends during its own start", version: new Version(1, 0, 0),
            author: "tests", supportedFeatures: Array.Empty<string>());

        public bool IsRunning => false;

#pragma warning disable CS0067 // Required by the interface; unused here.
        public event EventHandler<ShellOutputEventArgs>? OutputReceived;
#pragma warning restore CS0067
        public event EventHandler<ShellTerminatedEventArgs>? Terminated;

        public Task StartAsync(CustomShellStartOptions options, CancellationToken cancellationToken = default)
        {
            int code = options.EnvironmentVariables.TryGetValue(CodeVariable, out string? text) ? int.Parse(text, System.Globalization.CultureInfo.InvariantCulture) : 0;
            Terminated?.Invoke(this, new ShellTerminatedEventArgs(code));
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task WriteInputAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void NotifyTerminalResize(int width, int height)
        {
        }

        public void SendInitialOutput()
        {
        }

        public void RequestCancellation()
        {
        }

        public void Dispose()
        {
        }
    }

    [Test]
    public void CloseOnExit_DoesNothing_ForASessionThatFailedToStart()
    {
        using var manager = NewManager();
        var options = ProcessLaunchOptions.CreateCustom(
            Path.Combine(Path.GetTempPath(), "purrtty-no-such-program-" + Guid.NewGuid().ToString("N")));
        options.CloseOnExit = true;

        // A start failure is not an exit: the session is never published (the window
        // shows the error text instead), so there is nothing to close.
        Assert.That(async () => await manager.CreateSessionAsync("bad", options), Throws.InstanceOf<ProcessStartException>());
        Assert.That(manager.SessionCount, Is.Zero);
        Assert.That(Tick(manager), Is.Empty);
    }
}
