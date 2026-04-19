using caTTY.CustomShells.GameStuffShell.Execution;
using caTTY.CustomShells.GameStuffShell.Lexing;
using caTTY.CustomShells.GameStuffShell.Parsing;
using NUnit.Framework;

namespace caTTY.CustomShells.Tests.Unit;

[TestFixture]
public class GameStuffShellRedirectionTests
{
    [Test]
    public async Task RedirectStdoutToDevNull_DiscardsOutput()
    {
        // Parse: echo hi 1>/dev/null
        var tokens = new Lexer("echo hi 1>/dev/null").Lex();
        var list = new Parser(tokens).ParseList();
        
        var resolver = new FakeProgramResolver();
        resolver.Register(new FakeProgram("echo", stdoutOutput: "hi\n"));

        var stdoutCapture = new List<string>();
        var context = new ExecContext(
            resolver,
            gameApi: null,
            environment: new Dictionary<string, string>(),
            terminalWidth: 80,
            terminalHeight: 24,
            terminalOutputCallback: (text, isError) =>
            {
                if (!isError)
                {
                    stdoutCapture.Add(text);
                }
            });

        var executor = new Executor();
        var exitCode = await executor.ExecuteListAsync(list, context, CancellationToken.None);

        Assert.That(exitCode, Is.EqualTo(0));
        
        // No output should have been emitted to terminal
        Assert.That(stdoutCapture, Is.Empty);
    }

    [Test]
    public async Task RedirectStderrToStdout_CombinesStreams()
    {
        // Parse: cmd 2>&1
        var tokens = new Lexer("cmd 2>&1").Lex();
        var list = new Parser(tokens).ParseList();
        
        var resolver = new FakeProgramResolver();
        resolver.Register(new FakeProgram("cmd", stdoutOutput: "out\n", stderrOutput: "err\n"));

        var stdoutCapture = new List<string>();
        var context = new ExecContext(
            resolver,
            gameApi: null,
            environment: new Dictionary<string, string>(),
            terminalWidth: 80,
            terminalHeight: 24,
            terminalOutputCallback: (text, isError) =>
            {
                if (!isError)
                {
                    stdoutCapture.Add(text);
                }
            });

        var executor = new Executor();
        var exitCode = await executor.ExecuteListAsync(list, context, CancellationToken.None);

        Assert.That(exitCode, Is.EqualTo(0));
        
        // Both stdout and former stderr should be in stdout
        var allStdout = string.Join("", stdoutCapture);
        Assert.That(allStdout, Does.Contain("out\n"));
        Assert.That(allStdout, Does.Contain("err\n"));
    }

    [Test]
    public async Task RedirectOrderMatters_2To1Then1ToNull()
    {
        // Parse: cmd 2>&1 1>/dev/null
        // This should: 
        //   1. Make stderr point to current stdout (terminal)
        //   2. Then redirect stdout to /dev/null
        // Result: original stderr goes to terminal stdout, original stdout is discarded
        var tokens = new Lexer("cmd 2>&1 1>/dev/null").Lex();
        var list = new Parser(tokens).ParseList();
        
        var resolver = new FakeProgramResolver();
        resolver.Register(new FakeProgram("cmd", stdoutOutput: "out\n", stderrOutput: "err\n"));

        var stdoutCapture = new List<string>();
        var context = new ExecContext(
            resolver,
            gameApi: null,
            environment: new Dictionary<string, string>(),
            terminalWidth: 80,
            terminalHeight: 24,
            terminalOutputCallback: (text, isError) =>
            {
                if (!isError)
                {
                    stdoutCapture.Add(text);
                }
            });

        var executor = new Executor();
        var exitCode = await executor.ExecuteListAsync(list, context, CancellationToken.None);

        Assert.That(exitCode, Is.EqualTo(0));
        
        // Only former stderr should appear in stdout
        var allStdout = string.Join("", stdoutCapture);
        Assert.That(allStdout, Does.Contain("err"));
        Assert.That(allStdout, Does.Not.Contain("out"));
    }

    [Test]
    public async Task RedirectStderrToDevNull_DiscardsErrors()
    {
        // Parse: cmd 2>/dev/null
        var tokens = new Lexer("cmd 2>/dev/null").Lex();
        var list = new Parser(tokens).ParseList();
        
        var resolver = new FakeProgramResolver();
        resolver.Register(new FakeProgram("cmd", stdoutOutput: "out\n", stderrOutput: "err\n"));

        var stdoutCapture = new List<string>();
        var stderrCapture = new List<string>();
        var context = new ExecContext(
            resolver,
            gameApi: null,
            environment: new Dictionary<string, string>(),
            terminalWidth: 80,
            terminalHeight: 24,
            terminalOutputCallback: (text, isError) =>
            {
                if (isError)
                {
                    stderrCapture.Add(text);
                }
                else
                {
                    stdoutCapture.Add(text);
                }
            });

        var executor = new Executor();
        var exitCode = await executor.ExecuteListAsync(list, context, CancellationToken.None);

        Assert.That(exitCode, Is.EqualTo(0));
        
        var allStdout = string.Join("", stdoutCapture);
        Assert.That(allStdout, Does.Contain("out"));
        Assert.That(stderrCapture, Is.Empty);
    }

    [Test]
    public async Task InvalidRedirectionTarget_ReturnsError()
    {
        // Parse: echo hi 1>foo (should fail - only /dev/null is supported)
        var tokens = new Lexer("echo hi 1>foo").Lex();
        var list = new Parser(tokens).ParseList();
        
        var resolver = new FakeProgramResolver();
        resolver.Register(new FakeProgram("echo", stdoutOutput: "hi\n"));

        var stderrCapture = new List<string>();
        var context = new ExecContext(
            resolver,
            gameApi: null,
            environment: new Dictionary<string, string>(),
            terminalWidth: 80,
            terminalHeight: 24,
            terminalOutputCallback: (text, isError) =>
            {
                if (isError)
                {
                    stderrCapture.Add(text);
                }
            });

        var executor = new Executor();
        var exitCode = await executor.ExecuteListAsync(list, context, CancellationToken.None);

        Assert.That(exitCode, Is.EqualTo(2));
        
        var allStderr = string.Join("", stderrCapture);
        Assert.That(allStderr, Does.Contain("only /dev/null is supported"));
    }

    [Test]
    public async Task InvalidFileDescriptor_ReturnsError()
    {
        // Parse: echo hi 9>&1 (FD 9 is not supported, only 0, 1, 2)
        var tokens = new Lexer("echo hi 9>&1").Lex();
        var list = new Parser(tokens).ParseList();
        
        var resolver = new FakeProgramResolver();
        resolver.Register(new FakeProgram("echo", stdoutOutput: "hi\n"));

        var stderrCapture = new List<string>();
        var context = new ExecContext(
            resolver,
            gameApi: null,
            environment: new Dictionary<string, string>(),
            terminalWidth: 80,
            terminalHeight: 24,
            terminalOutputCallback: (text, isError) =>
            {
                if (isError)
                {
                    stderrCapture.Add(text);
                }
            });

        var executor = new Executor();
        var exitCode = await executor.ExecuteListAsync(list, context, CancellationToken.None);

        Assert.That(exitCode, Is.EqualTo(2));
        
        var allStderr = string.Join("", stderrCapture);
        Assert.That(allStderr, Does.Contain("Bad file descriptor"));
    }
}

// Note: We're reusing FakeProgram and FakeProgramResolver from GameStuffShellExecutorListTests
// If those are internal, they should be made public or duplicated here
