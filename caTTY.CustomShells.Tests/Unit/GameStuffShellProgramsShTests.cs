using caTTY.CustomShells.GameStuffShell.Execution;
using caTTY.CustomShells.GameStuffShell.Programs;
using NUnit.Framework;

namespace caTTY.CustomShells.Tests.Unit;

[TestFixture]
public class GameStuffShellProgramsShTests
{
    [Test]
    public async Task ShProgram_SimpleCommand_Executes()
    {
        var registry = new ProgramRegistry();
        registry.Register(new EchoProgram());
        registry.Register(new ShProgram());

        var program = new ShProgram();
        var (context, stdoutWriter, stderrWriter) = CreateTestContext(
            argv: new[] { "sh", "-c", "echo hello" },
            registry: registry);

        var exitCode = await program.RunAsync(context, CancellationToken.None);

        Assert.That(exitCode, Is.EqualTo(0));
        Assert.That(stdoutWriter.GetContent(), Is.EqualTo("hello\n"));
        Assert.That(stderrWriter.GetContent(), Is.Empty);
    }

    [Test]
    public async Task ShProgram_PipelineCommand_Executes()
    {
        var registry = new ProgramRegistry();
        registry.Register(new EchoProgram());
        registry.Register(new XargsProgram());
        registry.Register(new ShProgram());

        var program = new ShProgram();
        var (context, stdoutWriter, stderrWriter) = CreateTestContext(
            argv: new[] { "sh", "-c", "echo a b | xargs echo" },
            registry: registry);

        var exitCode = await program.RunAsync(context, CancellationToken.None);

        Assert.That(exitCode, Is.EqualTo(0));
        Assert.That(stdoutWriter.GetContent(), Is.EqualTo("a\nb\n"));
        Assert.That(stderrWriter.GetContent(), Is.Empty);
    }

    [Test]
    public async Task ShProgram_ListCommand_Executes()
    {
        var registry = new ProgramRegistry();
        registry.Register(new EchoProgram());
        registry.Register(new ShProgram());

        var program = new ShProgram();
        var (context, stdoutWriter, stderrWriter) = CreateTestContext(
            argv: new[] { "sh", "-c", "echo first ; echo second" },
            registry: registry);

        var exitCode = await program.RunAsync(context, CancellationToken.None);

        Assert.That(exitCode, Is.EqualTo(0));
        Assert.That(stdoutWriter.GetContent(), Is.EqualTo("first\nsecond\n"));
        Assert.That(stderrWriter.GetContent(), Is.Empty);
    }

    [Test]
    public async Task ShProgram_AndIfLogic_WorksCorrectly()
    {
        var registry = new ProgramRegistry();
        registry.Register(new EchoProgram());
        registry.Register(new FakeProgram("success", exitCode: 0));
        registry.Register(new FakeProgram("fail", exitCode: 1));
        registry.Register(new ShProgram());

        var program = new ShProgram();
        var (context, stdoutWriter, stderrWriter) = CreateTestContext(
            argv: new[] { "sh", "-c", "success && echo yes" },
            registry: registry);

        var exitCode = await program.RunAsync(context, CancellationToken.None);

        Assert.That(exitCode, Is.EqualTo(0));
        Assert.That(stdoutWriter.GetContent(), Is.EqualTo("yes\n"));
    }

    [Test]
    public async Task ShProgram_NoArgs_ReturnsUsageError()
    {
        var program = new ShProgram();
        var (context, stdoutWriter, stderrWriter) = CreateTestContext(
            argv: new[] { "sh" },
            registry: new ProgramRegistry());

        var exitCode = await program.RunAsync(context, CancellationToken.None);

        Assert.That(exitCode, Is.EqualTo(2));
        Assert.That(stdoutWriter.GetContent(), Is.Empty);
        Assert.That(stderrWriter.GetContent(), Does.Contain("usage"));
    }

    [Test]
    public async Task ShProgram_MissingCommandString_ReturnsUsageError()
    {
        var program = new ShProgram();
        var (context, stdoutWriter, stderrWriter) = CreateTestContext(
            argv: new[] { "sh", "-c" },
            registry: new ProgramRegistry());

        var exitCode = await program.RunAsync(context, CancellationToken.None);

        Assert.That(exitCode, Is.EqualTo(2));
        Assert.That(stdoutWriter.GetContent(), Is.Empty);
        Assert.That(stderrWriter.GetContent(), Does.Contain("usage"));
    }

    [Test]
    public async Task ShProgram_WrongFlag_ReturnsUsageError()
    {
        var program = new ShProgram();
        var (context, stdoutWriter, stderrWriter) = CreateTestContext(
            argv: new[] { "sh", "-x", "echo test" },
            registry: new ProgramRegistry());

        var exitCode = await program.RunAsync(context, CancellationToken.None);

        Assert.That(exitCode, Is.EqualTo(2));
        Assert.That(stdoutWriter.GetContent(), Is.Empty);
        Assert.That(stderrWriter.GetContent(), Does.Contain("only -c flag"));
    }

    [Test]
    public async Task ShProgram_LexerError_ReturnsError()
    {
        var program = new ShProgram();
        var (context, stdoutWriter, stderrWriter) = CreateTestContext(
            argv: new[] { "sh", "-c", "echo 'unterminated" },
            registry: new ProgramRegistry());

        var exitCode = await program.RunAsync(context, CancellationToken.None);

        Assert.That(exitCode, Is.EqualTo(2));
        Assert.That(stdoutWriter.GetContent(), Is.Empty);
        Assert.That(stderrWriter.GetContent(), Does.Contain("lexer error"));
    }

    [Test]
    public async Task ShProgram_CommandFailure_PropagatesExitCode()
    {
        var registry = new ProgramRegistry();
        registry.Register(new FakeProgram("fail", exitCode: 42));
        registry.Register(new ShProgram());

        var program = new ShProgram();
        var (context, stdoutWriter, stderrWriter) = CreateTestContext(
            argv: new[] { "sh", "-c", "fail" },
            registry: registry);

        var exitCode = await program.RunAsync(context, CancellationToken.None);

        Assert.That(exitCode, Is.EqualTo(42));
    }

    [Test]
    public async Task ShProgram_NestedSubshell_Executes()
    {
        var registry = new ProgramRegistry();
        registry.Register(new EchoProgram());
        registry.Register(new ShProgram());

        var program = new ShProgram();
        var (context, stdoutWriter, stderrWriter) = CreateTestContext(
            argv: new[] { "sh", "-c", "sh -c 'echo nested'" },
            registry: registry);

        var exitCode = await program.RunAsync(context, CancellationToken.None);

        Assert.That(exitCode, Is.EqualTo(0));
        Assert.That(stdoutWriter.GetContent(), Is.EqualTo("nested\n"));
        Assert.That(stderrWriter.GetContent(), Is.Empty);
    }

    [Test]
    public async Task ShProgram_PositionalParameter_Dollar0()
    {
        var registry = new ProgramRegistry();
        registry.Register(new EchoProgram());
        registry.Register(new ShProgram());

        var program = new ShProgram();
        // sh -c 'echo $0' myarg -> echo myarg
        var (context, stdoutWriter, stderrWriter) = CreateTestContext(
            argv: new[] { "sh", "-c", "echo $0", "myarg" },
            registry: registry);

        var exitCode = await program.RunAsync(context, CancellationToken.None);

        Assert.That(exitCode, Is.EqualTo(0));
        Assert.That(stdoutWriter.GetContent(), Is.EqualTo("myarg\n"));
        Assert.That(stderrWriter.GetContent(), Is.Empty);
    }

    [Test]
    public async Task ShProgram_PositionalParameter_Dollar0And1()
    {
        var registry = new ProgramRegistry();
        registry.Register(new EchoProgram());
        registry.Register(new ShProgram());

        var program = new ShProgram();
        // sh -c 'echo $0 $1' first second -> echo first second
        var (context, stdoutWriter, stderrWriter) = CreateTestContext(
            argv: new[] { "sh", "-c", "echo $0 $1", "first", "second" },
            registry: registry);

        var exitCode = await program.RunAsync(context, CancellationToken.None);

        Assert.That(exitCode, Is.EqualTo(0));
        Assert.That(stdoutWriter.GetContent(), Is.EqualTo("first second\n"));
        Assert.That(stderrWriter.GetContent(), Is.Empty);
    }

    [Test]
    public async Task ShProgram_PositionalParameter_MissingArgExpandsToEmpty()
    {
        var registry = new ProgramRegistry();
        registry.Register(new EchoProgram());
        registry.Register(new ShProgram());

        var program = new ShProgram();
        // sh -c 'echo [$1]' arg0 -> echo [] (only $0 provided, $1 is empty)
        var (context, stdoutWriter, stderrWriter) = CreateTestContext(
            argv: new[] { "sh", "-c", "echo [$1]", "arg0" },
            registry: registry);

        var exitCode = await program.RunAsync(context, CancellationToken.None);

        Assert.That(exitCode, Is.EqualTo(0));
        Assert.That(stdoutWriter.GetContent(), Is.EqualTo("[]\n"));
        Assert.That(stderrWriter.GetContent(), Is.Empty);
    }

    [Test]
    public async Task ShProgram_PositionalParameter_DollarAt()
    {
        var registry = new ProgramRegistry();
        registry.Register(new EchoProgram());
        registry.Register(new ShProgram());

        var program = new ShProgram();
        // sh -c 'echo $@' a b c -> echo a b c
        var (context, stdoutWriter, stderrWriter) = CreateTestContext(
            argv: new[] { "sh", "-c", "echo $@", "a", "b", "c" },
            registry: registry);

        var exitCode = await program.RunAsync(context, CancellationToken.None);

        Assert.That(exitCode, Is.EqualTo(0));
        Assert.That(stdoutWriter.GetContent(), Is.EqualTo("a b c\n"));
        Assert.That(stderrWriter.GetContent(), Is.Empty);
    }

    [Test]
    public async Task ShProgram_PositionalParameter_DollarStar()
    {
        var registry = new ProgramRegistry();
        registry.Register(new EchoProgram());
        registry.Register(new ShProgram());

        var program = new ShProgram();
        // sh -c 'echo $*' a b c -> echo a b c
        var (context, stdoutWriter, stderrWriter) = CreateTestContext(
            argv: new[] { "sh", "-c", "echo $*", "a", "b", "c" },
            registry: registry);

        var exitCode = await program.RunAsync(context, CancellationToken.None);

        Assert.That(exitCode, Is.EqualTo(0));
        Assert.That(stdoutWriter.GetContent(), Is.EqualTo("a b c\n"));
        Assert.That(stderrWriter.GetContent(), Is.Empty);
    }

    [Test]
    public async Task ShProgram_PositionalParameter_DollarHash()
    {
        var registry = new ProgramRegistry();
        registry.Register(new EchoProgram());
        registry.Register(new ShProgram());

        var program = new ShProgram();
        // sh -c 'echo $#' a b c -> echo 3
        var (context, stdoutWriter, stderrWriter) = CreateTestContext(
            argv: new[] { "sh", "-c", "echo $#", "a", "b", "c" },
            registry: registry);

        var exitCode = await program.RunAsync(context, CancellationToken.None);

        Assert.That(exitCode, Is.EqualTo(0));
        Assert.That(stdoutWriter.GetContent(), Is.EqualTo("3\n"));
        Assert.That(stderrWriter.GetContent(), Is.Empty);
    }

    [Test]
    public async Task ShProgram_PositionalParameter_InSingleQuotesNotExpanded()
    {
        var registry = new ProgramRegistry();
        registry.Register(new EchoProgram());
        registry.Register(new ShProgram());

        var program = new ShProgram();
        // sh -c "echo '$0'" myarg -> echo $0 (literal $0 because single quotes)
        var (context, stdoutWriter, stderrWriter) = CreateTestContext(
            argv: new[] { "sh", "-c", "echo '$0'", "myarg" },
            registry: registry);

        var exitCode = await program.RunAsync(context, CancellationToken.None);

        Assert.That(exitCode, Is.EqualTo(0));
        Assert.That(stdoutWriter.GetContent(), Is.EqualTo("$0\n"));
        Assert.That(stderrWriter.GetContent(), Is.Empty);
    }

    [Test]
    public async Task ShProgram_PositionalParameter_EscapedDollarNotExpanded()
    {
        var registry = new ProgramRegistry();
        registry.Register(new EchoProgram());
        registry.Register(new ShProgram());

        var program = new ShProgram();
        // sh -c 'echo \$0' myarg -> echo $0 (escaped)
        var (context, stdoutWriter, stderrWriter) = CreateTestContext(
            argv: new[] { "sh", "-c", "echo \\$0", "myarg" },
            registry: registry);

        var exitCode = await program.RunAsync(context, CancellationToken.None);

        Assert.That(exitCode, Is.EqualTo(0));
        Assert.That(stdoutWriter.GetContent(), Is.EqualTo("$0\n"));
        Assert.That(stderrWriter.GetContent(), Is.Empty);
    }

    [Test]
    public async Task ShProgram_PositionalParameter_BraceSyntax()
    {
        var registry = new ProgramRegistry();
        registry.Register(new EchoProgram());
        registry.Register(new ShProgram());

        var program = new ShProgram();
        // sh -c 'echo ${0}' myarg -> echo myarg
        var (context, stdoutWriter, stderrWriter) = CreateTestContext(
            argv: new[] { "sh", "-c", "echo ${0}", "myarg" },
            registry: registry);

        var exitCode = await program.RunAsync(context, CancellationToken.None);

        Assert.That(exitCode, Is.EqualTo(0));
        Assert.That(stdoutWriter.GetContent(), Is.EqualTo("myarg\n"));
        Assert.That(stderrWriter.GetContent(), Is.Empty);
    }

    [Test]
    public async Task ShProgram_PositionalParameter_UsedWithSleep()
    {
        var registry = new ProgramRegistry();
        registry.Register(new SleepProgram());
        registry.Register(new ShProgram());

        var program = new ShProgram();
        // sh -c 'sleep $0' 10 -> sleep 10 (10ms)
        var (context, stdoutWriter, stderrWriter) = CreateTestContext(
            argv: new[] { "sh", "-c", "sleep $0", "10" },
            registry: registry);

        var exitCode = await program.RunAsync(context, CancellationToken.None);

        Assert.That(exitCode, Is.EqualTo(0));
        Assert.That(stderrWriter.GetContent(), Is.Empty);
    }

    private static (ProgramContext, BufferedStreamWriter, BufferedStreamWriter) CreateTestContext(
        string[] argv,
        IProgramResolver registry)
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
}
