using caTTY.CustomShells.GameStuffShell.Execution;
using caTTY.CustomShells.GameStuffShell.Parsing;
using NUnit.Framework;

namespace caTTY.CustomShells.Tests.Unit;

[TestFixture]
public class GameStuffShellExecutorListTests
{
    [Test]
    public async Task ExecuteList_Sequential_RunsAllCommands()
    {
        var (resolver, context) = CreateTestContext();
        var executor = new Executor();

        resolver.Register(new FakeProgram("a", exitCode: 0));
        resolver.Register(new FakeProgram("b", exitCode: 0));
        resolver.Register(new FakeProgram("c", exitCode: 0));

        var list = new ListNode(new[]
        {
            new ListItem(Pipeline("a"), ListOperator.Sequential),
            new ListItem(Pipeline("b"), ListOperator.Sequential),
            new ListItem(Pipeline("c"), OperatorToNext: null)
        });

        var exitCode = await executor.ExecuteListAsync(list, context, CancellationToken.None);

        Assert.That(exitCode, Is.EqualTo(0));
        Assert.That(resolver.GetProgram("a").Invocations, Is.EqualTo(1));
        Assert.That(resolver.GetProgram("b").Invocations, Is.EqualTo(1));
        Assert.That(resolver.GetProgram("c").Invocations, Is.EqualTo(1));
    }

    [Test]
    public async Task ExecuteList_AndIf_SkipsAfterFailure()
    {
        var (resolver, context) = CreateTestContext();
        var executor = new Executor();

        resolver.Register(new FakeProgram("a", exitCode: 1));
        resolver.Register(new FakeProgram("b", exitCode: 0));

        var list = new ListNode(new[]
        {
            new ListItem(Pipeline("a"), ListOperator.AndIf),
            new ListItem(Pipeline("b"), OperatorToNext: null)
        });

        var exitCode = await executor.ExecuteListAsync(list, context, CancellationToken.None);

        Assert.That(exitCode, Is.EqualTo(1));
        Assert.That(resolver.GetProgram("a").Invocations, Is.EqualTo(1));
        Assert.That(resolver.GetProgram("b").Invocations, Is.EqualTo(0));
    }

    [Test]
    public async Task ExecuteList_AndIf_RunsAfterSuccess()
    {
        var (resolver, context) = CreateTestContext();
        var executor = new Executor();

        resolver.Register(new FakeProgram("a", exitCode: 0));
        resolver.Register(new FakeProgram("b", exitCode: 0));

        var list = new ListNode(new[]
        {
            new ListItem(Pipeline("a"), ListOperator.AndIf),
            new ListItem(Pipeline("b"), OperatorToNext: null)
        });

        var exitCode = await executor.ExecuteListAsync(list, context, CancellationToken.None);

        Assert.That(exitCode, Is.EqualTo(0));
        Assert.That(resolver.GetProgram("a").Invocations, Is.EqualTo(1));
        Assert.That(resolver.GetProgram("b").Invocations, Is.EqualTo(1));
    }

    [Test]
    public async Task ExecuteList_OrIf_SkipsAfterSuccess()
    {
        var (resolver, context) = CreateTestContext();
        var executor = new Executor();

        resolver.Register(new FakeProgram("a", exitCode: 0));
        resolver.Register(new FakeProgram("b", exitCode: 0));

        var list = new ListNode(new[]
        {
            new ListItem(Pipeline("a"), ListOperator.OrIf),
            new ListItem(Pipeline("b"), OperatorToNext: null)
        });

        var exitCode = await executor.ExecuteListAsync(list, context, CancellationToken.None);

        Assert.That(exitCode, Is.EqualTo(0));
        Assert.That(resolver.GetProgram("a").Invocations, Is.EqualTo(1));
        Assert.That(resolver.GetProgram("b").Invocations, Is.EqualTo(0));
    }

    [Test]
    public async Task ExecuteList_OrIf_RunsAfterFailure()
    {
        var (resolver, context) = CreateTestContext();
        var executor = new Executor();

        resolver.Register(new FakeProgram("a", exitCode: 1));
        resolver.Register(new FakeProgram("b", exitCode: 0));

        var list = new ListNode(new[]
        {
            new ListItem(Pipeline("a"), ListOperator.OrIf),
            new ListItem(Pipeline("b"), OperatorToNext: null)
        });

        var exitCode = await executor.ExecuteListAsync(list, context, CancellationToken.None);

        Assert.That(exitCode, Is.EqualTo(0));
        Assert.That(resolver.GetProgram("a").Invocations, Is.EqualTo(1));
        Assert.That(resolver.GetProgram("b").Invocations, Is.EqualTo(1));
    }

    [Test]
    public async Task ExecutePipeline_ForwardsStdoutToNextStdin()
    {
        var (resolver, context) = CreateTestContext();
        var executor = new Executor();

        resolver.Register(new FakeProgram("a", stdoutOutput: "hello\n"));
        resolver.Register(new FakeProgram("b", stdoutOutput: "world\n"));

        var pipeline = new PipelineNode(new[]
        {
            Command("a"),
            Command("b")
        });

        await executor.ExecutePipelineAsync(pipeline, context, CancellationToken.None);

        var bProgram = resolver.GetProgram("b");
        Assert.That(bProgram.LastStdin, Is.EqualTo("hello\n"));
    }

    [Test]
    public async Task ExecutePipeline_EmitsStderrToTerminal()
    {
        var (resolver, context) = CreateTestContext();
        var executor = new Executor();
        var stderrOutput = new List<string>();

        context = new ExecContext(
            resolver,
            gameApi: null,
            environment: new Dictionary<string, string>(),
            terminalWidth: 80,
            terminalHeight: 24,
            terminalOutputCallback: (text, isError) =>
            {
                if (isError)
                {
                    stderrOutput.Add(text);
                }
            });

        resolver.Register(new FakeProgram("a", stderrOutput: "error text\n"));

        var pipeline = new PipelineNode(new[] { Command("a") });

        await executor.ExecutePipelineAsync(pipeline, context, CancellationToken.None);

        Assert.That(stderrOutput, Has.Count.EqualTo(1));
        Assert.That(stderrOutput[0], Is.EqualTo("error text\n"));
    }

    [Test]
    public async Task ExecuteCommand_UnknownCommand_Returns127()
    {
        var (resolver, context) = CreateTestContext();
        var executor = new Executor();

        var command = Command("unknown");
        var stderrWriter = new BufferedStreamWriter();
        var streams = new StreamSet(
            EmptyStreamReader.Instance,
            new BufferedStreamWriter(),
            stderrWriter);

        var exitCode = await executor.ExecuteCommandAsync(command, streams, context, CancellationToken.None);

        Assert.That(exitCode, Is.EqualTo(127));
        var stderrContent = stderrWriter.GetContent();
        Assert.That(stderrContent, Does.Contain("command not found"));
    }

    [Test]
    public async Task ExecuteCommand_ExceptionInProgram_Returns1()
    {
        var (resolver, context) = CreateTestContext();
        var executor = new Executor();

        resolver.Register(new FakeProgram("boom", throwException: true));

        var command = Command("boom");
        var stderrWriter = new BufferedStreamWriter();
        var streams = new StreamSet(
            EmptyStreamReader.Instance,
            new BufferedStreamWriter(),
            stderrWriter);

        var exitCode = await executor.ExecuteCommandAsync(command, streams, context, CancellationToken.None);

        Assert.That(exitCode, Is.EqualTo(1));
        var stderrContent = stderrWriter.GetContent();
        Assert.That(stderrContent, Does.Contain("boom:"));
    }

    private static (FakeProgramResolver, ExecContext) CreateTestContext()
    {
        var resolver = new FakeProgramResolver();
        var context = new ExecContext(
            resolver,
            gameApi: null,
            environment: new Dictionary<string, string>(),
            terminalWidth: 80,
            terminalHeight: 24,
            terminalOutputCallback: (_, _) => { });

        return (resolver, context);
    }

    private static PipelineNode Pipeline(string commandName)
    {
        return new PipelineNode(new[] { Command(commandName) });
    }

    private static CommandNode Command(string name, params string[] args)
    {
        var argv = new List<string> { name };
        argv.AddRange(args);
        return new CommandNode(argv, Array.Empty<RedirectionNode>());
    }
}

// Test helpers
internal class FakeProgramResolver : IProgramResolver
{
    private readonly Dictionary<string, FakeProgram> _programs = new();

    public void Register(FakeProgram program)
    {
        _programs[program.Name] = program;
    }

    public FakeProgram GetProgram(string name) => _programs[name];

    public bool TryResolve(string name, out IProgram program)
    {
        if (_programs.TryGetValue(name, out var fake))
        {
            program = fake;
            return true;
        }

        program = null!;
        return false;
    }
}

internal class FakeProgram : IProgram
{
    private readonly int _exitCode;
    private readonly string? _stdoutOutput;
    private readonly string? _stderrOutput;
    private readonly bool _throwException;

    public string Name { get; }
    public int Invocations { get; private set; }
    public string? LastStdin { get; private set; }

    public FakeProgram(
        string name,
        int exitCode = 0,
        string? stdoutOutput = null,
        string? stderrOutput = null,
        bool throwException = false)
    {
        Name = name;
        _exitCode = exitCode;
        _stdoutOutput = stdoutOutput;
        _stderrOutput = stderrOutput;
        _throwException = throwException;
    }

    public async Task<int> RunAsync(ProgramContext context, CancellationToken cancellationToken)
    {
        Invocations++;
        LastStdin = await context.Streams.Stdin.ReadAllAsync(cancellationToken);

        if (_throwException)
        {
            throw new InvalidOperationException("Test exception");
        }

        if (_stdoutOutput is not null)
        {
            await context.Streams.Stdout.WriteAsync(_stdoutOutput, cancellationToken);
        }

        if (_stderrOutput is not null)
        {
            await context.Streams.Stderr.WriteAsync(_stderrOutput, cancellationToken);
        }

        return _exitCode;
    }
}
