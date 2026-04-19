using caTTY.CustomShells.GameStuffShell.Parsing;

namespace caTTY.CustomShells.GameStuffShell.Execution;

/// <summary>
/// Executes parsed AST nodes (lists, pipelines, commands).
/// </summary>
public sealed class Executor
{
    /// <summary>
    /// Executes a list node.
    /// </summary>
    /// <param name="list">The list to execute.</param>
    /// <param name="context">The execution context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The exit code of the final executed command.</returns>
    public async Task<int> ExecuteListAsync(
        ListNode list,
        ExecContext context,
        CancellationToken cancellationToken)
    {
        var lastExitCode = 0;

        foreach (var item in list.Items)
        {
            lastExitCode = await ExecutePipelineAsync(item.Pipeline, context, cancellationToken);

            if (item.OperatorToNext is null)
            {
                break;
            }

            var shouldRunNext = item.OperatorToNext switch
            {
                ListOperator.Sequential => true,
                ListOperator.AndIf => lastExitCode == 0,
                ListOperator.OrIf => lastExitCode != 0,
                _ => throw new InvalidOperationException($"Unknown list operator: {item.OperatorToNext}")
            };

            if (!shouldRunNext)
            {
                break;
            }
        }

        return lastExitCode;
    }

    /// <summary>
    /// Executes a pipeline node.
    /// </summary>
    /// <param name="pipeline">The pipeline to execute.</param>
    /// <param name="context">The execution context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The exit code of the last command in the pipeline.</returns>
    public async Task<int> ExecutePipelineAsync(
        PipelineNode pipeline,
        ExecContext context,
        CancellationToken cancellationToken)
    {
        if (pipeline.Commands.Count == 0)
        {
            return 0;
        }

        var stdinContent = string.Empty;
        var lastExitCode = 0;

        for (var i = 0; i < pipeline.Commands.Count; i++)
        {
            var command = pipeline.Commands[i];
            var stdoutCapture = new BufferedStreamWriter();
            var stderrCapture = new BufferedStreamWriter();

            var streams = new StreamSet(
                stdin: new BufferedStreamReader(stdinContent),
                stdout: stdoutCapture,
                stderr: stderrCapture
            );

            lastExitCode = await ExecuteCommandAsync(command, streams, context, cancellationToken);

            // Emit captured stderr to terminal
            var stderrContent = stderrCapture.GetContent();
            if (!string.IsNullOrEmpty(stderrContent))
            {
                context.TerminalOutputCallback(stderrContent, true);
            }

            // Pass stdout to next command's stdin (or to terminal if this is the last command)
            stdinContent = stdoutCapture.GetContent();
            if (i == pipeline.Commands.Count - 1 && !string.IsNullOrEmpty(stdinContent))
            {
                context.TerminalOutputCallback(stdinContent, false);
            }
        }

        return lastExitCode;
    }

    /// <summary>
    /// Executes a single command.
    /// </summary>
    /// <param name="command">The command to execute.</param>
    /// <param name="baseStreams">The base stream set (before redirections).</param>
    /// <param name="context">The execution context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The exit code of the command.</returns>
    public async Task<int> ExecuteCommandAsync(
        CommandNode command,
        StreamSet baseStreams,
        ExecContext context,
        CancellationToken cancellationToken)
    {
        if (command.Argv.Count == 0)
        {
            return 0;
        }

        var programName = command.Argv[0];

        if (!context.ProgramResolver.TryResolve(programName, out var program))
        {
            var errorMsg = $"{programName}: command not found\n";
            await baseStreams.Stderr.WriteAsync(errorMsg, cancellationToken);
            return 127;
        }

        // Apply redirections
        var streams = await RedirectionApplier.ApplyRedirectionsAsync(
            command.Redirections,
            baseStreams,
            context,
            errorWriter: baseStreams.Stderr);

        if (streams is null)
        {
            // Redirection error already reported to stderr
            return 2;
        }

        var programContext = new ProgramContext(
            argv: command.Argv,
            streams: streams,
            programResolver: context.ProgramResolver,
            gameApi: context.GameApi,
            environment: context.Environment,
            terminalWidth: context.TerminalWidth,
            terminalHeight: context.TerminalHeight
        );

        try
        {
            return await program.RunAsync(programContext, cancellationToken);
        }
        catch (Exception ex)
        {
            var errorMsg = $"{programName}: {ex.Message}\n";
            await baseStreams.Stderr.WriteAsync(errorMsg, cancellationToken);
            return 1;
        }
    }
}
