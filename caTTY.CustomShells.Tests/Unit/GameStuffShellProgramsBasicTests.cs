using caTTY.CustomShells.GameStuffShell.Execution;
using caTTY.CustomShells.GameStuffShell.Programs;
using NUnit.Framework;

namespace caTTY.CustomShells.Tests.Unit;

[TestFixture]
public class GameStuffShellProgramsBasicTests
{
    [Test]
    public async Task EchoProgram_NoArgs_PrintsEmptyLine()
    {
        var program = new EchoProgram();
        var (context, stdoutWriter, stderrWriter) = CreateTestContext(new[] { "echo" });

        var exitCode = await program.RunAsync(context, CancellationToken.None);

        Assert.That(exitCode, Is.EqualTo(0));
        Assert.That(stdoutWriter.GetContent(), Is.EqualTo("\n"));
        Assert.That(stderrWriter.GetContent(), Is.Empty);
    }

    [Test]
    public async Task EchoProgram_WithArgs_PrintsArgsWithSpace()
    {
        var program = new EchoProgram();
        var (context, stdoutWriter, stderrWriter) = CreateTestContext(new[] { "echo", "hello", "world" });

        var exitCode = await program.RunAsync(context, CancellationToken.None);

        Assert.That(exitCode, Is.EqualTo(0));
        Assert.That(stdoutWriter.GetContent(), Is.EqualTo("hello world\n"));
        Assert.That(stderrWriter.GetContent(), Is.Empty);
    }

    [Test]
    public async Task EchoProgram_WithMinusN_SuppressesNewline()
    {
        var program = new EchoProgram();
        var (context, stdoutWriter, stderrWriter) = CreateTestContext(new[] { "echo", "-n", "hello" });

        var exitCode = await program.RunAsync(context, CancellationToken.None);

        Assert.That(exitCode, Is.EqualTo(0));
        Assert.That(stdoutWriter.GetContent(), Is.EqualTo("hello"));
        Assert.That(stderrWriter.GetContent(), Is.Empty);
    }

    [Test]
    public async Task EchoProgram_OnlyMinusN_PrintsNothing()
    {
        var program = new EchoProgram();
        var (context, stdoutWriter, stderrWriter) = CreateTestContext(new[] { "echo", "-n" });

        var exitCode = await program.RunAsync(context, CancellationToken.None);

        Assert.That(exitCode, Is.EqualTo(0));
        Assert.That(stdoutWriter.GetContent(), Is.Empty);
        Assert.That(stderrWriter.GetContent(), Is.Empty);
    }

    [Test]
    public async Task SleepProgram_ValidDuration_Succeeds()
    {
        var program = new SleepProgram();
        var (context, stdoutWriter, stderrWriter) = CreateTestContext(new[] { "sleep", "10" });

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var exitCode = await program.RunAsync(context, CancellationToken.None);
        sw.Stop();

        Assert.That(exitCode, Is.EqualTo(0));
        Assert.That(sw.ElapsedMilliseconds, Is.GreaterThanOrEqualTo(9)); // Allow some tolerance
        Assert.That(stdoutWriter.GetContent(), Is.Empty);
        Assert.That(stderrWriter.GetContent(), Is.Empty);
    }

    [Test]
    public async Task SleepProgram_NoArgs_ReturnsError()
    {
        var program = new SleepProgram();
        var (context, stdoutWriter, stderrWriter) = CreateTestContext(new[] { "sleep" });

        var exitCode = await program.RunAsync(context, CancellationToken.None);

        Assert.That(exitCode, Is.EqualTo(2));
        Assert.That(stderrWriter.GetContent(), Does.Contain("missing operand"));
    }

    [Test]
    public async Task SleepProgram_InvalidNumber_ReturnsError()
    {
        var program = new SleepProgram();
        var (context, stdoutWriter, stderrWriter) = CreateTestContext(new[] { "sleep", "abc" });

        var exitCode = await program.RunAsync(context, CancellationToken.None);

        Assert.That(exitCode, Is.EqualTo(2));
        Assert.That(stderrWriter.GetContent(), Does.Contain("invalid time interval"));
    }

    [Test]
    public async Task SleepProgram_NegativeNumber_ReturnsError()
    {
        var program = new SleepProgram();
        var (context, stdoutWriter, stderrWriter) = CreateTestContext(new[] { "sleep", "-5" });

        var exitCode = await program.RunAsync(context, CancellationToken.None);

        Assert.That(exitCode, Is.EqualTo(2));
        Assert.That(stderrWriter.GetContent(), Does.Contain("invalid time interval"));
    }

    [Test]
    public async Task SleepProgram_Cancellation_ExitsCleanly()
    {
        var program = new SleepProgram();
        var (context, stdoutWriter, stderrWriter) = CreateTestContext(new[] { "sleep", "10000" });
        var cts = new CancellationTokenSource();

        var task = program.RunAsync(context, cts.Token);
        await Task.Delay(50); // Let sleep start
        cts.Cancel();

        var exitCode = await task;

        Assert.That(exitCode, Is.EqualTo(0)); // Cancellation is not an error
    }

    [Test]
    public void ProgramRegistry_RegisterAndResolve_Succeeds()
    {
        var registry = new ProgramRegistry();
        var echo = new EchoProgram();
        registry.Register(echo);

        var resolved = registry.TryResolve("echo", out var program);

        Assert.That(resolved, Is.True);
        Assert.That(program, Is.SameAs(echo));
    }

    [Test]
    public void ProgramRegistry_UnknownProgram_ReturnsFalse()
    {
        var registry = new ProgramRegistry();

        var resolved = registry.TryResolve("unknown", out var program);

        Assert.That(resolved, Is.False);
    }

    private static (ProgramContext, BufferedStreamWriter, BufferedStreamWriter) CreateTestContext(string[] argv)
    {
        var stdoutWriter = new BufferedStreamWriter();
        var stderrWriter = new BufferedStreamWriter();
        var streams = new StreamSet(
            EmptyStreamReader.Instance,
            stdoutWriter,
            stderrWriter);

        var context = new ProgramContext(
            argv: argv,
            streams: streams,
            programResolver: new ProgramRegistry(),
            gameApi: null,
            environment: new Dictionary<string, string>(),
            terminalWidth: 80,
            terminalHeight: 24);

        return (context, stdoutWriter, stderrWriter);
    }
}
