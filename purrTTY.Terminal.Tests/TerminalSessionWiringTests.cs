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
