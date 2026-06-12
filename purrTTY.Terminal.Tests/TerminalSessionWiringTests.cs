using System.Text;
using NUnit.Framework;
using PurrTTY.Terminal.Ghostty;
using PurrTTY.Terminal.Sessions;
using purrTTY.Core.Terminal;

namespace PurrTTY.Terminal.Tests;

/// <summary>
/// Validates the wiring in <see cref="TerminalSession"/>: PTY output reaches the
/// surface, engine replies reach the PTY, titles propagate, and user input is
/// written to the PTY. Uses a fake process manager so no real shell is launched.
/// </summary>
[TestFixture]
public sealed class TerminalSessionWiringTests
{
    private static (TerminalSession session, FakeProcessManager pty) NewSession()
    {
        var surface = new GhosttyTerminalSurface(40, 10);
        var pty = new FakeProcessManager();
        var session = new TerminalSession(Guid.NewGuid(), "Test", surface, pty);
        return (session, pty);
    }

    private static string RowText(PurrTTY.Terminal.Rendering.TerminalFrame frame, int row)
    {
        var sb = new StringBuilder();
        for (int c = 0; c < frame.Cols; c++)
        {
            sb.Append(frame.RowData[row].Cells[c].Grapheme ?? " ");
        }

        return sb.ToString().TrimEnd();
    }

    [Test]
    public async Task PtyOutput_ReachesSurfaceFrame()
    {
        var (session, pty) = NewSession();
        using (session)
        {
            await session.InitializeAsync(ProcessLaunchOptions.CreateDefault());
            pty.EmitOutput(Encoding.UTF8.GetBytes("hello session"));

            var frame = session.Surface.BuildFrame();
            Assert.That(RowText(frame, 0), Is.EqualTo("hello session"));
        }
    }

    [Test]
    public async Task EngineReply_IsWrittenToPty()
    {
        var (session, pty) = NewSession();
        using (session)
        {
            await session.InitializeAsync(ProcessLaunchOptions.CreateDefault());
            pty.EmitOutput(Encoding.UTF8.GetBytes("\x1b[6n")); // DSR cursor report
            session.Surface.BuildFrame();

            Assert.That(pty.Written, Is.Not.Empty);
            Assert.That(pty.Written[0], Is.EqualTo((byte)0x1b));
        }
    }

    [Test]
    public async Task Title_PropagatesToSession()
    {
        var (session, pty) = NewSession();
        using (session)
        {
            await session.InitializeAsync(ProcessLaunchOptions.CreateDefault());
            pty.EmitOutput(Encoding.UTF8.GetBytes("\x1b]0;Session Title\x07"));
            session.Surface.BuildFrame();

            Assert.That(session.Title, Is.EqualTo("Session Title"));
        }
    }

    [Test]
    public async Task SendInput_WritesToPty()
    {
        var (session, pty) = NewSession();
        using (session)
        {
            await session.InitializeAsync(ProcessLaunchOptions.CreateDefault());
            session.SendInput("ls\r"u8);

            Assert.That(Encoding.ASCII.GetString(pty.Written.ToArray()), Is.EqualTo("ls\r"));
        }
    }

    [Test]
    public async Task ProcessExit_RaisesSessionEvent()
    {
        var (session, pty) = NewSession();
        using (session)
        {
            await session.InitializeAsync(ProcessLaunchOptions.CreateDefault());
            SessionProcessExitedEventArgs? captured = null;
            session.ProcessExited += (_, e) => captured = e;

            pty.EmitExit(42, 1234);

            Assert.That(captured, Is.Not.Null);
            Assert.That(captured!.ExitCode, Is.EqualTo(42));
            Assert.That(captured.ProcessId, Is.EqualTo(1234));
        }
    }

    [Test]
    public async Task UpdateTerminalDimensions_UpdatesSettings_AndRejectsInvalidSilently()
    {
        var (session, _) = NewSession();
        using (session)
        {
            await session.InitializeAsync(ProcessLaunchOptions.CreateDefault());

            session.UpdateTerminalDimensions(120, 40);
            Assert.That(session.Settings.Columns, Is.EqualTo(120));
            Assert.That(session.Settings.Rows, Is.EqualTo(40));

            // Invalid dimensions are logged, not thrown, and leave state intact.
            session.UpdateTerminalDimensions(0, -1);
            Assert.That(session.Settings.Columns, Is.EqualTo(120));
            Assert.That(session.Settings.Rows, Is.EqualTo(40));
        }
    }

    // ---- SessionManager lifecycle (uses a registered headless custom shell so
    // ---- TerminalSessionFactory builds a CustomShellPtyBridge, never a real PTY).

    private const string LifecycleShellId = nameof(LifecycleTestShell);

    [OneTimeSetUp]
    public void RegisterLifecycleShell()
    {
        try
        {
            CustomShellRegistry.Instance.RegisterShell(LifecycleShellId, () => new LifecycleTestShell());
        }
        catch (ArgumentException)
        {
            // Already registered by a previous run in the same process.
        }
    }

    private static SessionManager NewManager(int maxSessions = 4)
        => new(maxSessions, ProcessLaunchOptions.CreateCustomGame(LifecycleShellId));

    [Test]
    public async Task CreateSession_RunsConfiguratorBeforePublication()
    {
        using var manager = NewManager();

        bool sawUnpublishedSurface = false;
        bool createdFired = false;
        manager.SessionConfigurator = s =>
        {
            // Pre-publication: the session must not be visible to consumers yet
            // (this is what makes theme push / event wiring race-free).
            sawUnpublishedSurface = !createdFired && manager.ActiveSession is null && manager.SessionCount == 0;
            s.Surface.SetCursorStyle(PurrTTY.Terminal.Rendering.CursorShape.Bar, blink: false);
        };
        manager.SessionCreated += (_, _) => createdFired = true;

        var session = await manager.CreateSessionAsync();

        Assert.That(sawUnpublishedSurface, Is.True, "configurator must run before the session is published");
        Assert.That(createdFired, Is.True);
        Assert.That(manager.ActiveSession, Is.SameAs(session));
        Assert.That(session.Surface.BuildFrame().Cursor.Shape,
            Is.EqualTo(PurrTTY.Terminal.Rendering.CursorShape.Bar),
            "configurator side effects (cursor default) must be live on the published session");
    }

    [Test]
    public async Task RestartSession_ReusesSurfaceAndProcessManager()
    {
        using var manager = NewManager();
        var session = await manager.CreateSessionAsync();
        var surface = session.Surface;
        var processManager = session.ProcessManager;

        await manager.RestartSessionAsync(session.Id);

        Assert.That(manager.ActiveSession, Is.SameAs(session), "restart must not mint a new session");
        Assert.That(session.Surface, Is.SameAs(surface), "restart reuses the surface (scrollback survives)");
        Assert.That(session.ProcessManager, Is.SameAs(processManager));
        Assert.That(processManager.IsRunning, Is.True, "the shell must be running again after restart");
    }

    /// <summary>Headless shell for SessionManager lifecycle tests (no KSA, no PTY).</summary>
    private sealed class LifecycleTestShell : ICustomShell
    {
        public CustomShellMetadata Metadata { get; } = CustomShellMetadata.Create(
            name: "Lifecycle Test Shell",
            description: "session lifecycle test shell",
            version: new Version(1, 0, 0),
            author: "tests",
            supportedFeatures: Array.Empty<string>());

        public bool IsRunning { get; private set; }

#pragma warning disable CS0067 // Required by the interface; unused here.
        public event EventHandler<ShellOutputEventArgs>? OutputReceived;
        public event EventHandler<ShellTerminatedEventArgs>? Terminated;
#pragma warning restore CS0067

        public Task StartAsync(CustomShellStartOptions options, CancellationToken cancellationToken = default)
        {
            IsRunning = true;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            IsRunning = false;
            return Task.CompletedTask;
        }

        public Task WriteInputAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

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

    /// <summary>A minimal in-memory <see cref="IProcessManager"/> for wiring tests.</summary>
    private sealed class FakeProcessManager : IProcessManager
    {
        private readonly List<byte> _written = new();

        public List<byte> Written => _written;
        public bool IsRunning { get; private set; }
        public int? ProcessId { get; private set; }
        public int? ExitCode { get; private set; }

        public event EventHandler<DataReceivedEventArgs>? DataReceived;
        public event EventHandler<ProcessExitedEventArgs>? ProcessExited;
#pragma warning disable CS0067 // Required by the interface; unused in tests.
        public event EventHandler<ProcessErrorEventArgs>? ProcessError;
#pragma warning restore CS0067

        public Task StartAsync(ProcessLaunchOptions options, CancellationToken cancellationToken = default)
        {
            IsRunning = true;
            ProcessId = 1234;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            IsRunning = false;
            return Task.CompletedTask;
        }

        public void Write(ReadOnlySpan<byte> data) => _written.AddRange(data.ToArray());

        public void Write(string text) => Write(Encoding.UTF8.GetBytes(text));

        public void Resize(int width, int height) { }

        public void EmitOutput(byte[] data)
            => DataReceived?.Invoke(this, new DataReceivedEventArgs(data));

        public void EmitExit(int exitCode, int processId)
        {
            IsRunning = false;
            ExitCode = exitCode;
            ProcessExited?.Invoke(this, new ProcessExitedEventArgs(exitCode, processId));
        }

        public void Dispose() { }
    }
}
