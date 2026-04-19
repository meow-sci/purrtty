using caTTY.CustomShells.GameStuffShell.Lexing;

namespace caTTY.CustomShells.GameStuffShell.Parsing;

public sealed class Parser
{
    private readonly IReadOnlyList<Token> _tokens;
    private int _index;

    public Parser(IReadOnlyList<Token> tokens)
    {
        _tokens = tokens;
    }

    public ListNode ParseList()
    {
        var items = new List<ListItem>();
        var pipeline = ParsePipeline();
        ListOperator? op = null;

        while (true)
        {
            var current = Peek();
            if (current.Kind == TokenKind.Semicolon)
            {
                op = ListOperator.Sequential;
                Advance();
                if (IsAtEnd())
                {
                    items.Add(new ListItem(pipeline, OperatorToNext: null));
                    break;
                }

                items.Add(new ListItem(pipeline, op));
                if (Peek().Kind == TokenKind.Semicolon)
                {
                    throw new ParserException("Empty command between list separators", current.Span);
                }

                pipeline = ParsePipeline();
                op = null;
                continue;
            }

            if (current.Kind == TokenKind.AndIf || current.Kind == TokenKind.OrIf)
            {
                op = current.Kind == TokenKind.AndIf ? ListOperator.AndIf : ListOperator.OrIf;
                Advance();
                items.Add(new ListItem(pipeline, op));
                if (IsAtEnd())
                {
                    throw new ParserException("Missing command after list operator", current.Span);
                }

                pipeline = ParsePipeline();
                op = null;
                continue;
            }

            break;
        }

        items.Add(new ListItem(pipeline, OperatorToNext: null));
        return new ListNode(items);
    }

    private PipelineNode ParsePipeline()
    {
        var commands = new List<CommandNode> { ParseCommand() };

        while (Peek().Kind == TokenKind.Pipe)
        {
            var pipeToken = Advance();
            if (IsAtEnd())
            {
                throw new ParserException("Missing command after pipe", pipeToken.Span);
            }

            commands.Add(ParseCommand());
        }

        return new PipelineNode(commands);
    }

    private CommandNode ParseCommand()
    {
        var argv = new List<string>();
        var redirections = new List<RedirectionNode>();

        if (Peek().Kind != TokenKind.Word)
        {
            throw new ParserException("Expected command", Peek().Span);
        }

        argv.Add(Advance().Text);

        while (true)
        {
            var token = Peek();
            if (token.Kind == TokenKind.Word)
            {
                argv.Add(Advance().Text);
                continue;
            }

            if (TryParseRedirection(out var redirection))
            {
                redirections.Add(redirection);
                continue;
            }

            break;
        }

        return new CommandNode(argv, redirections);
    }

    private bool TryParseRedirection(out RedirectionNode redirection)
    {
        redirection = null!;
        var ioNumber = default(int?);
        var redirToken = Peek();

        if (redirToken.Kind == TokenKind.IoNumber)
        {
            ioNumber = ParseIoNumber(Advance());
            redirToken = Peek();
        }

        if (redirToken.Kind is not (TokenKind.RedirectOut or TokenKind.RedirectOutAppend or TokenKind.RedirectIn or TokenKind.RedirectDupOut or TokenKind.RedirectDupIn))
        {
            if (ioNumber.HasValue)
            {
                throw new ParserException("Expected redirection after IO number", redirToken.Span);
            }

            return false;
        }

        var operatorToken = Advance();
        if (Peek().Kind != TokenKind.Word)
        {
            throw new ParserException("Expected redirection target", Peek().Span);
        }

        var targetToken = Advance();
        var kind = operatorToken.Kind switch
        {
            TokenKind.RedirectOut => RedirectionKind.Out,
            TokenKind.RedirectOutAppend => RedirectionKind.OutAppend,
            TokenKind.RedirectIn => RedirectionKind.In,
            TokenKind.RedirectDupOut => RedirectionKind.DupOut,
            TokenKind.RedirectDupIn => RedirectionKind.DupIn,
            _ => throw new InvalidOperationException("Unexpected redirection token")
        };

        redirection = new RedirectionNode(ioNumber, kind, targetToken.Text, operatorToken.Span);
        return true;
    }

    private static int ParseIoNumber(Token token)
    {
        if (int.TryParse(token.Text, out var value))
        {
            return value;
        }

        throw new ParserException("Invalid IO number", token.Span);
    }

    private Token Peek() => _tokens[_index];

    private Token Advance() => _tokens[_index++];

    private bool IsAtEnd() => Peek().Kind == TokenKind.End;
}

public sealed class ParserException : Exception
{
    public ParserException(string message, TextSpan span)
        : base(message)
    {
        Span = span;
    }

    public TextSpan Span { get; }
}
