using caTTY.CustomShells.GameStuffShell.Execution;
using caTTY.CustomShells.GameStuffShell.Lexing;
using caTTY.CustomShells.GameStuffShell.Parsing;
using caTTY.CustomShells.GameStuffShell.Programs;
using NUnit.Framework;

namespace caTTY.CustomShells.Tests.Unit;

/// <summary>
/// Golden end-to-end integration tests that exercise the full shell pipeline:
/// lexer → parser → executor → programs.
/// </summary>
[TestFixture]
public class GameStuffShellGoldenTests
{
    [Test]
    public async Task Golden_EchoPipeXargs_ProducesMultipleLines()
    {
        // echo a b | xargs echo
        // Expected: "a\n" + "b\n" (xargs invokes echo twice)
        var result = await ExecuteCommandLine("echo a b | xargs echo");

        Assert.That(result.ExitCode, Is.EqualTo(0));
        Assert.That(result.Stdout, Is.EqualTo("a\nb\n"));
        Assert.That(result.Stderr, Is.Empty);
    }

    [Test]
    public async Task Golden_RedirectionOrdering_2To1Then1ToNull()
    {
        // echo error 2>&1 1>/dev/null
        // Expected: stderr redirected to original stdout, then stdout redirected to /dev/null
        // Result: "error\n" goes to original stdout (which is our test stdout)
        var result = await ExecuteCommandLine("echo error 2>&1 1>/dev/null");

        Assert.That(result.ExitCode, Is.EqualTo(0));
        // Redirections are applied left-to-right:
        // 1. 2>&1 copies FD 1 to FD 2 (stderr now points to original stdout)
        // 2. 1>/dev/null redirects FD 1 to /dev/null
        // Result: stdout is /dev/null, stderr is original stdout
        // Since echo writes to stdout (FD 1), and it's redirected to /dev/null, nothing appears
        Assert.That(result.Stdout, Is.Empty);
        Assert.That(result.Stderr, Is.Empty);
    }

    [Test]
    public async Task Golden_RedirectionOrdering_1ToNullThen2To1()
    {
        // echo error 1>/dev/null 2>&1
        // Expected: stdout redirected to /dev/null first, then stderr copies that
        // Result: both stdout and stderr go to /dev/null
        var result = await ExecuteCommandLine("echo error 1>/dev/null 2>&1");

        Assert.That(result.ExitCode, Is.EqualTo(0));
        Assert.That(result.Stdout, Is.Empty);
        Assert.That(result.Stderr, Is.Empty);
    }

    [Test]
    public async Task Golden_WordJoining_AdjacentQuotedAndUnquoted()
    {
        // echo a"b"c'd'e
        // Expected: single word "abcde"
        var result = await ExecuteCommandLine("echo a\"b\"c'd'e");

        Assert.That(result.ExitCode, Is.EqualTo(0));
        Assert.That(result.Stdout, Is.EqualTo("abcde\n"));
        Assert.That(result.Stderr, Is.Empty);
    }

    [Test]
    public async Task Golden_EmptyStringArguments_PreservedInQuotes()
    {
        // echo "" '' a
        // Expected: three arguments (two empty, one "a")
        var result = await ExecuteCommandLine("echo \"\" '' a");

        Assert.That(result.ExitCode, Is.EqualTo(0));
        Assert.That(result.Stdout, Is.EqualTo("  a\n")); // Two empty strings become two spaces
        Assert.That(result.Stderr, Is.Empty);
    }

    [Test]
    public async Task Golden_ListSemantics_SequentialExecution()
    {
        // echo first ; echo second ; echo third
        var result = await ExecuteCommandLine("echo first ; echo second ; echo third");

        Assert.That(result.ExitCode, Is.EqualTo(0));
        Assert.That(result.Stdout, Is.EqualTo("first\nsecond\nthird\n"));
        Assert.That(result.Stderr, Is.Empty);
    }

    [Test]
    public async Task Golden_AndIfLogic_RunsAfterSuccess()
    {
        // echo -n success && echo " continued"
        var result = await ExecuteCommandLine("echo -n success && echo \" continued\"");

        Assert.That(result.ExitCode, Is.EqualTo(0));
        Assert.That(result.Stdout, Is.EqualTo("success continued\n"));
        Assert.That(result.Stderr, Is.Empty);
    }

    [Test]
    public async Task Golden_OrIfLogic_SkipsAfterSuccess()
    {
        // echo -n first || echo second
        var result = await ExecuteCommandLine("echo -n first || echo second");

        Assert.That(result.ExitCode, Is.EqualTo(0));
        Assert.That(result.Stdout, Is.EqualTo("first"));
        Assert.That(result.Stderr, Is.Empty);
    }

    [Test]
    public async Task Golden_ComplexPipeline_EchoXargsWithFixedArgs()
    {
        // echo one two three | xargs echo -n
        // Expected: xargs invokes "echo -n one", "echo -n two", "echo -n three"
        // Result: "onetwothree" (no newlines due to -n)
        var result = await ExecuteCommandLine("echo one two three | xargs echo -n");

        Assert.That(result.ExitCode, Is.EqualTo(0));
        Assert.That(result.Stdout, Is.EqualTo("onetwothree"));
        Assert.That(result.Stderr, Is.Empty);
    }

    [Test]
    public async Task Golden_SubshellExecution_SimpleCommand()
    {
        // sh -c "echo nested"
        var result = await ExecuteCommandLine("sh -c \"echo nested\"");

        Assert.That(result.ExitCode, Is.EqualTo(0));
        Assert.That(result.Stdout, Is.EqualTo("nested\n"));
        Assert.That(result.Stderr, Is.Empty);
    }

    [Test]
    public async Task Golden_SubshellExecution_WithPipeline()
    {
        // sh -c "echo a b | xargs echo"
        var result = await ExecuteCommandLine("sh -c \"echo a b | xargs echo\"");

        Assert.That(result.ExitCode, Is.EqualTo(0));
        Assert.That(result.Stdout, Is.EqualTo("a\nb\n"));
        Assert.That(result.Stderr, Is.Empty);
    }

    [Test]
    public async Task Golden_NestedSubshells_DoubleNesting()
    {
        // sh -c "sh -c 'echo deeply nested'"
        var result = await ExecuteCommandLine("sh -c \"sh -c 'echo deeply nested'\"");

        Assert.That(result.ExitCode, Is.EqualTo(0));
        Assert.That(result.Stdout, Is.EqualTo("deeply nested\n"));
        Assert.That(result.Stderr, Is.Empty);
    }

    [Test]
    public async Task Golden_EscapeSequences_InSingleQuotes()
    {
        // echo 'hello\nworld'
        // Expected: backslash-n preserved literally (no interpretation)
        var result = await ExecuteCommandLine("echo 'hello\\nworld'");

        Assert.That(result.ExitCode, Is.EqualTo(0));
        Assert.That(result.Stdout, Is.EqualTo("hello\\nworld\n"));
        Assert.That(result.Stderr, Is.Empty);
    }

    [Test]
    public async Task Golden_Cancellation_SleepProgram()
    {
        // Test that cancellation works correctly
        var cts = new CancellationTokenSource();
        var execTask = ExecuteCommandLineAsync("sleep 10000", cts.Token);

        await Task.Delay(50); // Let sleep start
        cts.Cancel();

        var result = await execTask;

        Assert.That(result.ExitCode, Is.EqualTo(0)); // Sleep exits cleanly on cancellation
    }

    [Test]
    public async Task Golden_MultipleRedirections_SameFileDescriptor()
    {
        // echo test 1>/dev/null 1>/dev/null
        // Last redirection wins
        var result = await ExecuteCommandLine("echo test 1>/dev/null 1>/dev/null");

        Assert.That(result.ExitCode, Is.EqualTo(0));
        Assert.That(result.Stdout, Is.Empty);
        Assert.That(result.Stderr, Is.Empty);
    }

    [Test]
    public async Task Golden_PipelineWithRedirection_CombinedStreams()
    {
        // echo foo 2>&1 | xargs echo
        // Combines stderr with stdout, then pipes to xargs
        var result = await ExecuteCommandLine("echo foo 2>&1 | xargs echo");

        Assert.That(result.ExitCode, Is.EqualTo(0));
        Assert.That(result.Stdout, Is.EqualTo("foo\n"));
        Assert.That(result.Stderr, Is.Empty);
    }

    // ============================================================
    // Positional parameter tests (sh -c with $0, $1, etc.)
    // ============================================================

    [Test]
    public async Task Golden_ShPositionalParam_Dollar0()
    {
        // sh -c 'echo $0' foo
        // $0 should be "foo"
        var result = await ExecuteCommandLine("sh -c 'echo $0' foo");

        Assert.That(result.ExitCode, Is.EqualTo(0));
        Assert.That(result.Stdout, Is.EqualTo("foo\n"));
        Assert.That(result.Stderr, Is.Empty);
    }

    [Test]
    public async Task Golden_ShPositionalParam_Dollar0And1()
    {
        // sh -c 'echo $0 $1' first second
        var result = await ExecuteCommandLine("sh -c 'echo $0 $1' first second");

        Assert.That(result.ExitCode, Is.EqualTo(0));
        Assert.That(result.Stdout, Is.EqualTo("first second\n"));
        Assert.That(result.Stderr, Is.Empty);
    }

    [Test]
    public async Task Golden_ShPositionalParam_WithXargs()
    {
        // echo "1000 2000" | xargs sh -c 'echo delay: $0'
        // xargs invokes sh twice: once with "1000", once with "2000"
        var result = await ExecuteCommandLine("echo 1000 2000 | xargs sh -c 'echo delay: $0'");

        Assert.That(result.ExitCode, Is.EqualTo(0));
        Assert.That(result.Stdout, Is.EqualTo("delay: 1000\ndelay: 2000\n"));
        Assert.That(result.Stderr, Is.Empty);
    }

    [Test]
    public async Task Golden_ShPositionalParam_WithSleep()
    {
        // echo 10 | xargs sh -c 'sleep $0'
        // Should complete without error (10ms sleep)
        var result = await ExecuteCommandLine("echo 10 | xargs sh -c 'sleep $0'");

        Assert.That(result.ExitCode, Is.EqualTo(0));
        Assert.That(result.Stderr, Is.Empty);
    }

    [Test]
    public async Task Golden_ShPositionalParam_DollarAt()
    {
        // sh -c 'echo $@' a b c
        // $@ should be "a b c"
        var result = await ExecuteCommandLine("sh -c 'echo $@' a b c");

        Assert.That(result.ExitCode, Is.EqualTo(0));
        Assert.That(result.Stdout, Is.EqualTo("a b c\n"));
        Assert.That(result.Stderr, Is.Empty);
    }

    [Test]
    public async Task Golden_ShPositionalParam_DollarHash()
    {
        // sh -c 'echo $#' a b c
        // $# should be "3"
        var result = await ExecuteCommandLine("sh -c 'echo $#' a b c");

        Assert.That(result.ExitCode, Is.EqualTo(0));
        Assert.That(result.Stdout, Is.EqualTo("3\n"));
        Assert.That(result.Stderr, Is.Empty);
    }

    // ============================================================
    // Multi-stage pipeline tests
    // ============================================================

    [Test]
    public async Task Golden_ThreeStagePipeline()
    {
        // echo "a b c" | xargs echo | xargs echo
        // First xargs: echo a, echo b, echo c -> "a\nb\nc\n"
        // Second xargs: echo a, echo b, echo c -> "a\nb\nc\n"
        var result = await ExecuteCommandLine("echo a b c | xargs echo | xargs echo");

        Assert.That(result.ExitCode, Is.EqualTo(0));
        Assert.That(result.Stdout, Is.EqualTo("a\nb\nc\n"));
        Assert.That(result.Stderr, Is.Empty);
    }

    [Test]
    public async Task Golden_PipelinePreservesOrder()
    {
        // echo "1 2 3" | xargs echo -n
        // Should produce "123" in order
        var result = await ExecuteCommandLine("echo 1 2 3 | xargs echo -n");

        Assert.That(result.ExitCode, Is.EqualTo(0));
        Assert.That(result.Stdout, Is.EqualTo("123"));
        Assert.That(result.Stderr, Is.Empty);
    }

    // ============================================================
    // Complex redirection ordering tests
    // ============================================================

    [Test]
    public async Task Golden_Redirection_1ToNullOnly()
    {
        // echo hello 1>/dev/null
        // stdout goes to /dev/null, nothing visible
        var result = await ExecuteCommandLine("echo hello 1>/dev/null");

        Assert.That(result.ExitCode, Is.EqualTo(0));
        Assert.That(result.Stdout, Is.Empty);
        Assert.That(result.Stderr, Is.Empty);
    }

    [Test]
    public async Task Golden_Redirection_2ToNullOnly()
    {
        // Need a command that produces stderr - use sh -c with nonexistent command
        // sh -c 'badcmd' 2>/dev/null
        var result = await ExecuteCommandLine("sh -c 'badcmd' 2>/dev/null");

        // badcmd returns 127, stderr suppressed
        Assert.That(result.ExitCode, Is.EqualTo(127));
        Assert.That(result.Stderr, Is.Empty);
    }

    [Test]
    public async Task Golden_Redirection_BothToNull()
    {
        // echo hello 1>/dev/null 2>/dev/null
        var result = await ExecuteCommandLine("echo hello 1>/dev/null 2>/dev/null");

        Assert.That(result.ExitCode, Is.EqualTo(0));
        Assert.That(result.Stdout, Is.Empty);
        Assert.That(result.Stderr, Is.Empty);
    }

    [Test]
    public async Task Golden_Redirection_DefaultFdIsStdout()
    {
        // echo hello >/dev/null  (no FD number means 1)
        var result = await ExecuteCommandLine("echo hello >/dev/null");

        Assert.That(result.ExitCode, Is.EqualTo(0));
        Assert.That(result.Stdout, Is.Empty);
        Assert.That(result.Stderr, Is.Empty);
    }

    // ============================================================
    // List operator tests with various exit codes
    // ============================================================

    [Test]
    public async Task Golden_AndIf_ChainOfSuccess()
    {
        // echo a && echo b && echo c
        // All succeed, all run
        var result = await ExecuteCommandLine("echo a && echo b && echo c");

        Assert.That(result.ExitCode, Is.EqualTo(0));
        Assert.That(result.Stdout, Is.EqualTo("a\nb\nc\n"));
        Assert.That(result.Stderr, Is.Empty);
    }

    [Test]
    public async Task Golden_AndIf_StopsOnFirstFailure()
    {
        // echo a && false && echo c
        // First succeeds, second fails (false returns 1), third skipped
        var result = await ExecuteCommandLine("echo a && false && echo c");

        Assert.That(result.ExitCode, Is.EqualTo(1));
        Assert.That(result.Stdout, Is.EqualTo("a\n"));
        Assert.That(result.Stderr, Is.Empty);
    }

    [Test]
    public async Task Golden_OrIf_SkipsAfterSuccess()
    {
        // echo a || echo b || echo c
        // First succeeds, rest skipped
        var result = await ExecuteCommandLine("echo a || echo b || echo c");

        Assert.That(result.ExitCode, Is.EqualTo(0));
        Assert.That(result.Stdout, Is.EqualTo("a\n"));
        Assert.That(result.Stderr, Is.Empty);
    }

    [Test]
    public async Task Golden_OrIf_RunsAfterFailure()
    {
        // false || echo recovered
        var result = await ExecuteCommandLine("false || echo recovered");

        Assert.That(result.ExitCode, Is.EqualTo(0));
        Assert.That(result.Stdout, Is.EqualTo("recovered\n"));
        Assert.That(result.Stderr, Is.Empty);
    }

    [Test]
    public async Task Golden_OrIf_ChainUntilSuccess()
    {
        // false || exit 2 || echo finally
        var result = await ExecuteCommandLine("false || exit 2 || echo finally");

        Assert.That(result.ExitCode, Is.EqualTo(0));
        Assert.That(result.Stdout, Is.EqualTo("finally\n"));
        Assert.That(result.Stderr, Is.Empty);
    }

    [Test]
    public async Task Golden_MixedOperators_AndThenOr()
    {
        // echo a && false || echo fallback
        // a succeeds, false fails, fallback runs due to ||
        var result = await ExecuteCommandLine("echo a && false || echo fallback");

        Assert.That(result.ExitCode, Is.EqualTo(0));
        Assert.That(result.Stdout, Is.EqualTo("a\nfallback\n"));
        Assert.That(result.Stderr, Is.Empty);
    }

    [Test]
    public async Task Golden_MixedOperators_OrThenAnd()
    {
        // false || echo recovered && echo continued
        // false fails, recovered runs, continued runs
        var result = await ExecuteCommandLine("false || echo recovered && echo continued");

        Assert.That(result.ExitCode, Is.EqualTo(0));
        Assert.That(result.Stdout, Is.EqualTo("recovered\ncontinued\n"));
        Assert.That(result.Stderr, Is.Empty);
    }

    [Test]
    public async Task Golden_Sequential_AlwaysRunsAll()
    {
        // false ; echo after
        // First fails but second still runs due to ;
        var result = await ExecuteCommandLine("false ; echo after");

        Assert.That(result.ExitCode, Is.EqualTo(0));
        Assert.That(result.Stdout, Is.EqualTo("after\n"));
        Assert.That(result.Stderr, Is.Empty);
    }

    [Test]
    public async Task Golden_TrailingSemicolon_Allowed()
    {
        // echo hello ;
        var result = await ExecuteCommandLine("echo hello ;");

        Assert.That(result.ExitCode, Is.EqualTo(0));
        Assert.That(result.Stdout, Is.EqualTo("hello\n"));
        Assert.That(result.Stderr, Is.Empty);
    }

    // ============================================================
    // Combination tests: pipeline + redirection + list operators
    // ============================================================

    [Test]
    public async Task Golden_PipelineInSequence()
    {
        // echo a | xargs echo ; echo b | xargs echo
        var result = await ExecuteCommandLine("echo a | xargs echo ; echo b | xargs echo");

        Assert.That(result.ExitCode, Is.EqualTo(0));
        Assert.That(result.Stdout, Is.EqualTo("a\nb\n"));
        Assert.That(result.Stderr, Is.Empty);
    }

    [Test]
    public async Task Golden_PipelineWithAndIf()
    {
        // echo a | xargs echo && echo done
        var result = await ExecuteCommandLine("echo a | xargs echo && echo done");

        Assert.That(result.ExitCode, Is.EqualTo(0));
        Assert.That(result.Stdout, Is.EqualTo("a\ndone\n"));
        Assert.That(result.Stderr, Is.Empty);
    }

    [Test]
    public async Task Golden_RedirectionInPipeline()
    {
        // echo a 2>&1 | xargs echo 1>/dev/null
        // First cmd: echo a with stderr merged to stdout -> "a\n" piped
        // Second cmd: xargs echo -> output to /dev/null
        var result = await ExecuteCommandLine("echo a 2>&1 | xargs echo 1>/dev/null");

        Assert.That(result.ExitCode, Is.EqualTo(0));
        Assert.That(result.Stdout, Is.Empty);
        Assert.That(result.Stderr, Is.Empty);
    }

    [Test]
    public async Task Golden_ComplexCombination()
    {
        // echo start && echo a b | xargs echo -n && echo ; echo end
        // 1. echo start -> "start\n"
        // 2. echo a b | xargs echo -n -> "ab" (pipeline)
        // 3. echo (empty) -> "\n"
        // 4. echo end -> "end\n"
        var result = await ExecuteCommandLine("echo start && echo a b | xargs echo -n && echo ; echo end");

        Assert.That(result.ExitCode, Is.EqualTo(0));
        Assert.That(result.Stdout, Is.EqualTo("start\nab\nend\n"));
        Assert.That(result.Stderr, Is.Empty);
    }

    // ============================================================
    // Error handling tests
    // ============================================================

    [Test]
    public async Task Golden_UnknownCommand_Returns127()
    {
        var result = await ExecuteCommandLine("nonexistent_command");

        Assert.That(result.ExitCode, Is.EqualTo(127));
        Assert.That(result.Stderr, Does.Contain("command not found"));
    }

    [Test]
    public async Task Golden_UnknownCommand_OrIfRecovery()
    {
        // badcmd || echo recovered
        var result = await ExecuteCommandLine("badcmd || echo recovered");

        Assert.That(result.ExitCode, Is.EqualTo(0));
        Assert.That(result.Stdout, Is.EqualTo("recovered\n"));
        Assert.That(result.Stderr, Does.Contain("command not found"));
    }

    [Test]
    public async Task Golden_InvalidRedirectionTarget_Returns2()
    {
        // echo hi 1>/tmp/foo (only /dev/null is supported)
        var result = await ExecuteCommandLine("echo hi 1>/tmp/foo");

        Assert.That(result.ExitCode, Is.EqualTo(2));
        Assert.That(result.Stderr, Does.Contain("/dev/null"));
    }

    private async Task<ExecutionResult> ExecuteCommandLine(string commandLine)
    {
        return await ExecuteCommandLineAsync(commandLine, CancellationToken.None);
    }

    private async Task<ExecutionResult> ExecuteCommandLineAsync(string commandLine, CancellationToken cancellationToken)
    {
        // Set up program registry with all programs
        var registry = new ProgramRegistry();
        registry.Register(new EchoProgram());
        registry.Register(new SleepProgram());
        registry.Register(new XargsProgram());
        registry.Register(new ShProgram());
        registry.Register(new ExitProgram());
        registry.Register(new TrueProgram());
        registry.Register(new FalseProgram());

        // Set up streams
        var stdoutWriter = new BufferedStreamWriter();
        var stderrWriter = new BufferedStreamWriter();
        var terminalStdout = new BufferedStreamWriter();
        var terminalStderr = new BufferedStreamWriter();

        // Create execution context
        var execContext = new ExecContext(
            programResolver: registry,
            gameApi: null,
            environment: new Dictionary<string, string>(),
            terminalWidth: 80,
            terminalHeight: 24,
            terminalOutputCallback: (text, isError) =>
            {
                if (isError)
                {
                    terminalStderr.WriteAsync(text, CancellationToken.None).GetAwaiter().GetResult();
                }
                else
                {
                    terminalStdout.WriteAsync(text, CancellationToken.None).GetAwaiter().GetResult();
                }
            });

        // Lex
        var lexer = new Lexer(commandLine);
        var tokens = lexer.Lex();

        // Parse
        var parser = new Parser(tokens);
        var ast = parser.ParseList();

        // Execute
        var executor = new Executor();
        var exitCode = await executor.ExecuteListAsync(ast, execContext, cancellationToken);

        return new ExecutionResult(
            exitCode,
            terminalStdout.GetContent(),
            terminalStderr.GetContent());
    }

    private record ExecutionResult(int ExitCode, string Stdout, string Stderr);
}
