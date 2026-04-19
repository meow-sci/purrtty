using caTTY.CustomShells.GameStuffShell.Execution;
using caTTY.CustomShells.GameStuffShell.Programs;
using NUnit.Framework;

namespace caTTY.CustomShells.Tests.Unit;

[TestFixture]
public class GameStuffShellProgramsXargsTests
{
    [Test]
    public async Task XargsProgram_WithTokens_InvokesProgramForEach()
    {
        var registry = new ProgramRegistry();
        registry.Register(new EchoProgram());
        registry.Register(new XargsProgram());

        var program = new XargsProgram();
        var (context, stdoutWriter, stderrWriter) = CreateTestContextWithStdin(
            argv: new[] { "xargs", "echo" },
            stdinContent: "a b c",
            registry: registry);

        var exitCode = await program.RunAsync(context, CancellationToken.None);

        Assert.That(exitCode, Is.EqualTo(0));
        Assert.That(stdoutWriter.GetContent(), Is.EqualTo("a\nb\nc\n"));
        Assert.That(stderrWriter.GetContent(), Is.Empty);
    }

    [Test]
    public async Task XargsProgram_WithFixedArgs_PassesThemToEachInvocation()
    {
        var registry = new ProgramRegistry();
        registry.Register(new EchoProgram());
        registry.Register(new XargsProgram());

        var program = new XargsProgram();
        var (context, stdoutWriter, stderrWriter) = CreateTestContextWithStdin(
            argv: new[] { "xargs", "echo", "-n" },
            stdinContent: "foo bar",
            registry: registry);

        var exitCode = await program.RunAsync(context, CancellationToken.None);

        Assert.That(exitCode, Is.EqualTo(0));
        Assert.That(stdoutWriter.GetContent(), Is.EqualTo("foobar")); // -n suppresses newlines
        Assert.That(stderrWriter.GetContent(), Is.Empty);
    }

    [Test]
    public async Task XargsProgram_NoArgs_ReturnsUsageError()
    {
        var program = new XargsProgram();
        var (context, stdoutWriter, stderrWriter) = CreateTestContextWithStdin(
            argv: new[] { "xargs" },
            stdinContent: "token",
            registry: new ProgramRegistry());

        var exitCode = await program.RunAsync(context, CancellationToken.None);

        Assert.That(exitCode, Is.EqualTo(2));
        Assert.That(stdoutWriter.GetContent(), Is.Empty);
        Assert.That(stderrWriter.GetContent(), Does.Contain("missing program name"));
    }

    [Test]
    public async Task XargsProgram_EmptyStdin_ReturnsError()
    {
        var registry = new ProgramRegistry();
        registry.Register(new EchoProgram());
        registry.Register(new XargsProgram());

        var program = new XargsProgram();
        var (context, stdoutWriter, stderrWriter) = CreateTestContextWithStdin(
            argv: new[] { "xargs", "echo" },
            stdinContent: "",
            registry: registry);

        var exitCode = await program.RunAsync(context, CancellationToken.None);

        Assert.That(exitCode, Is.EqualTo(2));
        Assert.That(stdoutWriter.GetContent(), Is.Empty);
        Assert.That(stderrWriter.GetContent(), Does.Contain("no input"));
    }

    [Test]
    public async Task XargsProgram_WhitespaceOnlyStdin_ReturnsError()
    {
        var registry = new ProgramRegistry();
        registry.Register(new EchoProgram());
        registry.Register(new XargsProgram());

        var program = new XargsProgram();
        var (context, stdoutWriter, stderrWriter) = CreateTestContextWithStdin(
            argv: new[] { "xargs", "echo" },
            stdinContent: "   \n\t  \n  ",
            registry: registry);

        var exitCode = await program.RunAsync(context, CancellationToken.None);

        Assert.That(exitCode, Is.EqualTo(2));
        Assert.That(stdoutWriter.GetContent(), Is.Empty);
        Assert.That(stderrWriter.GetContent(), Does.Contain("no input"));
    }

    [Test]
    public async Task XargsProgram_UnknownTargetProgram_Returns127()
    {
        var program = new XargsProgram();
        var (context, stdoutWriter, stderrWriter) = CreateTestContextWithStdin(
            argv: new[] { "xargs", "unknown" },
            stdinContent: "token",
            registry: new ProgramRegistry());

        var exitCode = await program.RunAsync(context, CancellationToken.None);

        Assert.That(exitCode, Is.EqualTo(127));
        Assert.That(stdoutWriter.GetContent(), Is.Empty);
        Assert.That(stderrWriter.GetContent(), Does.Contain("command not found"));
    }

    [Test]
    public async Task XargsProgram_TargetProgramFails_ReturnsFirstNonZero()
    {
        var registry = new ProgramRegistry();
        var failingProgram = new FakeProgram("fail", exitCode: 42);
        registry.Register(failingProgram);
        registry.Register(new XargsProgram());

        var program = new XargsProgram();
        var (context, stdoutWriter, stderrWriter) = CreateTestContextWithStdin(
            argv: new[] { "xargs", "fail" },
            stdinContent: "a b c",
            registry: registry);

        var exitCode = await program.RunAsync(context, CancellationToken.None);

        Assert.That(exitCode, Is.EqualTo(42)); // First non-zero exit code
    }

    [Test]
    public async Task XargsProgram_MixedExitCodes_ReturnsFirstNonZero()
    {
        var registry = new ProgramRegistry();
        var successThenFail = new ConditionalProgram("cond",
            successCondition: (argv) => argv.Contains("a"),
            failExitCode: 5);
        registry.Register(successThenFail);
        registry.Register(new XargsProgram());

        var program = new XargsProgram();
        var (context, stdoutWriter, stderrWriter) = CreateTestContextWithStdin(
            argv: new[] { "xargs", "cond" },
            stdinContent: "a b c", // "a" succeeds, "b" and "c" fail with exit 5
            registry: registry);

        var exitCode = await program.RunAsync(context, CancellationToken.None);

        Assert.That(exitCode, Is.EqualTo(5)); // First non-zero
    }

    private static (ProgramContext, BufferedStreamWriter, BufferedStreamWriter) CreateTestContextWithStdin(
        string[] argv,
        string stdinContent,
        IProgramResolver registry)
    {
        var stdinReader = new BufferedStreamReader(stdinContent);
        var stdoutWriter = new BufferedStreamWriter();
        var stderrWriter = new BufferedStreamWriter();
        var streams = new StreamSet(stdinReader, stdoutWriter, stderrWriter);

        var context = new ProgramContext(
            argv: argv,
            streams: streams,
            programResolver: registry,
            gameApi: null,
            environment: new Dictionary<string, string>(),
            terminalWidth: 80,
            terminalHeight: 24);

        return (context, stdoutWriter, stderrWriter);
    }

    /// <summary>
    /// Fake program that always returns a specific exit code.
    /// </summary>
    private sealed class FakeProgram : IProgram
    {
        private readonly int _exitCode;

        public string Name { get; }

        public FakeProgram(string name, int exitCode)
        {
            Name = name;
            _exitCode = exitCode;
        }

        public Task<int> RunAsync(ProgramContext context, CancellationToken cancellationToken)
        {
            return Task.FromResult(_exitCode);
        }
    }

    /// <summary>
    /// Fake program that returns 0 if condition is met, otherwise a specific exit code.
    /// </summary>
    private sealed class ConditionalProgram : IProgram
    {
        private readonly Func<IReadOnlyList<string>, bool> _successCondition;
        private readonly int _failExitCode;

        public string Name { get; }

        public ConditionalProgram(string name, Func<IReadOnlyList<string>, bool> successCondition, int failExitCode)
        {
            Name = name;
            _successCondition = successCondition;
            _failExitCode = failExitCode;
        }

        public Task<int> RunAsync(ProgramContext context, CancellationToken cancellationToken)
        {
            var exitCode = _successCondition(context.Argv) ? 0 : _failExitCode;
            return Task.FromResult(exitCode);
        }
    }
}
