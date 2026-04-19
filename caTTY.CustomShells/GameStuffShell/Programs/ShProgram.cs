using System.Text;
using System.Text.RegularExpressions;
using caTTY.CustomShells.GameStuffShell.Execution;
using caTTY.CustomShells.GameStuffShell.Lexing;
using caTTY.CustomShells.GameStuffShell.Parsing;

namespace caTTY.CustomShells.GameStuffShell.Programs;

/// <summary>
/// Implements a subshell that parses and executes a command string.
/// </summary>
public sealed partial class ShProgram : IProgram
{
    private readonly Executor _executor = new();

    /// <inheritdoc/>
    public string Name => "sh";

    public async Task<int> RunAsync(ProgramContext context, CancellationToken cancellationToken)
    {
        // sh requires -c flag and a command string
        if (context.Argv.Count < 3)
        {
            await context.Streams.Stderr.WriteAsync("sh: usage: sh -c <command>\n", cancellationToken);
            return 2; // Usage error
        }

        if (context.Argv[1] != "-c")
        {
            await context.Streams.Stderr.WriteAsync("sh: only -c flag is supported\n", cancellationToken);
            return 2; // Usage error
        }

        var commandString = context.Argv[2];

        // Expand positional parameters ($0, $1, ..., $9, $@, $*)
        // Arguments after the command string become positional parameters:
        // sh -c 'echo $0 $1' arg0 arg1 -> echo arg0 arg1
        var positionalArgs = context.Argv.Count > 3
            ? context.Argv.Skip(3).ToList()
            : new List<string>();

        commandString = ExpandPositionalParameters(commandString, positionalArgs);

        // Lex the command string
        var lexer = new Lexer(commandString);
        IReadOnlyList<Token> tokens;
        try
        {
            tokens = lexer.Lex();
        }
        catch (LexerException ex)
        {
            await context.Streams.Stderr.WriteAsync($"sh: lexer error: {ex.Message}\n", cancellationToken);
            return 2;
        }
        catch (Exception ex)
        {
            await context.Streams.Stderr.WriteAsync($"sh: unexpected lexer error: {ex.Message}\n", cancellationToken);
            return 1;
        }

        // Parse the tokens
        var parser = new Parser(tokens);
        ListNode ast;
        try
        {
            ast = parser.ParseList();
        }
        catch (ParserException ex)
        {
            await context.Streams.Stderr.WriteAsync($"sh: parse error: {ex.Message}\n", cancellationToken);
            return 2;
        }
        catch (Exception ex)
        {
            await context.Streams.Stderr.WriteAsync($"sh: unexpected parser error: {ex.Message}\n", cancellationToken);
            return 1;
        }

        // Create execution context (inherit from parent, but create a new ExecContext)
        var execContext = new ExecContext(
            programResolver: context.ProgramResolver,
            gameApi: context.GameApi,
            environment: context.Environment,
            terminalWidth: context.TerminalWidth,
            terminalHeight: context.TerminalHeight,
            terminalOutputCallback: (text, isError) =>
            {
                // Write to parent's streams
                var targetStream = isError ? context.Streams.Stderr : context.Streams.Stdout;
                targetStream.WriteAsync(text, CancellationToken.None).GetAwaiter().GetResult();
            });

        // Execute the AST
        try
        {
            var exitCode = await _executor.ExecuteListAsync(ast, execContext, cancellationToken);
            return exitCode;
        }
        catch (Exception ex)
        {
            await context.Streams.Stderr.WriteAsync($"sh: execution error: {ex.Message}\n", cancellationToken);
            return 1;
        }
    }

    /// <summary>
    /// Expands positional parameters ($0, $1, ..., $9, $@, $*, ${N}) in a command string.
    /// This mimics bash behavior where arguments after 'sh -c command' become positional params.
    /// </summary>
    private static string ExpandPositionalParameters(string command, IReadOnlyList<string> positionalArgs)
    {
        if (string.IsNullOrEmpty(command))
        {
            return command;
        }

        var result = new StringBuilder();
        var i = 0;

        while (i < command.Length)
        {
            // Check for single quote - no expansion inside single quotes
            if (command[i] == '\'')
            {
                var endQuote = command.IndexOf('\'', i + 1);
                if (endQuote == -1)
                {
                    // Unterminated quote - copy rest of string as-is (lexer will catch this)
                    result.Append(command.AsSpan(i));
                    break;
                }

                // Copy entire single-quoted section including quotes
                result.Append(command.AsSpan(i, endQuote - i + 1));
                i = endQuote + 1;
                continue;
            }

            // Check for backslash escape
            if (command[i] == '\\' && i + 1 < command.Length)
            {
                var nextChar = command[i + 1];
                if (nextChar == '$')
                {
                    // \$ escapes the dollar sign - output literal $
                    result.Append('$');
                    i += 2;
                    continue;
                }

                // Copy backslash and next character as-is
                result.Append(command[i]);
                result.Append(nextChar);
                i += 2;
                continue;
            }

            // Check for parameter expansion
            if (command[i] == '$')
            {
                var expanded = TryExpandParameter(command, i, positionalArgs, out var charsConsumed);
                if (expanded != null)
                {
                    result.Append(expanded);
                    i += charsConsumed;
                    continue;
                }
            }

            // Regular character
            result.Append(command[i]);
            i++;
        }

        return result.ToString();
    }

    /// <summary>
    /// Tries to expand a parameter starting at position in the command string.
    /// Returns the expanded value and the number of characters consumed, or null if not a parameter.
    /// </summary>
    private static string? TryExpandParameter(string command, int pos, IReadOnlyList<string> positionalArgs, out int charsConsumed)
    {
        charsConsumed = 0;

        if (pos >= command.Length || command[pos] != '$')
        {
            return null;
        }

        // Need at least one more character after $
        if (pos + 1 >= command.Length)
        {
            return null;
        }

        var nextChar = command[pos + 1];

        // ${N} format - handles multi-digit positional parameters
        if (nextChar == '{')
        {
            var closeBrace = command.IndexOf('}', pos + 2);
            if (closeBrace == -1)
            {
                return null; // Unterminated brace
            }

            var paramName = command.Substring(pos + 2, closeBrace - pos - 2);
            if (int.TryParse(paramName, out var index) && index >= 0)
            {
                charsConsumed = closeBrace - pos + 1;
                return index < positionalArgs.Count ? positionalArgs[index] : string.Empty;
            }

            // Not a numeric parameter - return null to leave it unexpanded
            return null;
        }

        // $@ - all positional parameters, separately quoted (for our purposes, space-separated)
        if (nextChar == '@')
        {
            charsConsumed = 2;
            return string.Join(" ", positionalArgs);
        }

        // $* - all positional parameters as a single word (space-separated)
        if (nextChar == '*')
        {
            charsConsumed = 2;
            return string.Join(" ", positionalArgs);
        }

        // $# - number of positional parameters
        if (nextChar == '#')
        {
            charsConsumed = 2;
            return positionalArgs.Count.ToString();
        }

        // $0-$9 - single digit positional parameters
        if (char.IsDigit(nextChar))
        {
            var index = nextChar - '0';
            charsConsumed = 2;
            return index < positionalArgs.Count ? positionalArgs[index] : string.Empty;
        }

        // Not a recognized parameter pattern
        return null;
    }
}
