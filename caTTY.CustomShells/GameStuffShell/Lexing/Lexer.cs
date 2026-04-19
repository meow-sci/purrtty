using System.Text;

namespace caTTY.CustomShells.GameStuffShell.Lexing;

public sealed class Lexer
{
    private readonly string _input;
    private int _index;

    public Lexer(string input)
    {
        _input = input ?? string.Empty;
    }

    public IReadOnlyList<Token> Lex()
    {
        var tokens = new List<Token>();

        while (true)
        {
            SkipWhitespaceAndComments();

            if (IsAtEnd())
            {
                tokens.Add(new Token(TokenKind.End, string.Empty, new TextSpan(_index, 0)));
                return tokens;
            }

            var start = _index;
            var ch = _input[_index];

            if (ch == '|')
            {
                if (Match("||"))
                {
                    tokens.Add(new Token(TokenKind.OrIf, "||", new TextSpan(start, 2)));
                }
                else
                {
                    _index++;
                    tokens.Add(new Token(TokenKind.Pipe, "|", new TextSpan(start, 1)));
                }

                continue;
            }

            if (ch == '&')
            {
                if (Match("&&"))
                {
                    tokens.Add(new Token(TokenKind.AndIf, "&&", new TextSpan(start, 2)));
                    continue;
                }

                throw new LexerException("Unexpected '&'", new TextSpan(start, 1));
            }

            if (ch == ';')
            {
                _index++;
                tokens.Add(new Token(TokenKind.Semicolon, ";", new TextSpan(start, 1)));
                continue;
            }

            if (char.IsDigit(ch) && TryPeekRedirectionAfterDigits(out var digitToken, out var redirectToken))
            {
                tokens.Add(digitToken);
                tokens.Add(redirectToken);
                continue;
            }

            if (TryLexRedirection(out var redirectionToken))
            {
                tokens.Add(redirectionToken);
                continue;
            }

            tokens.Add(LexWord());
        }
    }

    private void SkipWhitespaceAndComments()
    {
        while (!IsAtEnd())
        {
            if (char.IsWhiteSpace(_input[_index]))
            {
                _index++;
                continue;
            }

            if (_input[_index] == '#')
            {
                while (!IsAtEnd() && _input[_index] != '\n')
                {
                    _index++;
                }

                continue;
            }

            break;
        }
    }

    private Token LexWord()
    {
        var spanStart = _index;
        var builder = new StringBuilder();
        var consumedSegment = false;

        while (!IsAtEnd())
        {
            var ch = _input[_index];

            if (char.IsWhiteSpace(ch) || IsOperatorStart(ch) || ch == '#')
            {
                break;
            }

            if (ch == '\'' || ch == '"')
            {
                builder.Append(LexQuotedSegment(ch));
                consumedSegment = true;
                continue;
            }

            if (ch == '\\')
            {
                builder.Append(LexEscapedCharacter());
                consumedSegment = true;
                continue;
            }

            builder.Append(ch);
            _index++;
            consumedSegment = true;
        }

        if (!consumedSegment)
        {
            throw new LexerException("Unexpected token", new TextSpan(spanStart, 1));
        }

        return new Token(TokenKind.Word, builder.ToString(), new TextSpan(spanStart, _index - spanStart));
    }

    private string LexQuotedSegment(char quote)
    {
        var start = _index;
        _index++;
        var builder = new StringBuilder();

        while (!IsAtEnd())
        {
            var ch = _input[_index];
            if (ch == quote)
            {
                _index++;
                return builder.ToString();
            }

            if (quote == '"' && ch == '\\')
            {
                if (_index + 1 >= _input.Length)
                {
                    throw new LexerException("Unterminated escape sequence", new TextSpan(start, _index - start + 1));
                }

                var next = _input[_index + 1];
                if (next == '\\' || next == '"')
                {
                    builder.Append(next);
                    _index += 2;
                    continue;
                }
            }

            builder.Append(ch);
            _index++;
        }

        throw new LexerException("Unterminated quote", new TextSpan(start, _index - start));
    }

    private char LexEscapedCharacter()
    {
        var start = _index;
        if (_index + 1 >= _input.Length)
        {
            throw new LexerException("Unterminated escape sequence", new TextSpan(start, 1));
        }

        var next = _input[_index + 1];
        if (next is ' ' or '|' or '&' or ';' or '>' or '<' or '#' or '\\')
        {
            _index += 2;
            return next;
        }

        _index += 2;
        return next;
    }

    private bool TryLexRedirection(out Token token)
    {
        token = default;
        if (IsAtEnd())
        {
            return false;
        }

        var start = _index;
        var ch = _input[_index];

        if (ch == '>')
        {
            if (Match(">>"))
            {
                token = new Token(TokenKind.RedirectOutAppend, ">>", new TextSpan(start, 2));
                return true;
            }

            if (Match(">&"))
            {
                token = new Token(TokenKind.RedirectDupOut, ">&", new TextSpan(start, 2));
                return true;
            }

            _index++;
            token = new Token(TokenKind.RedirectOut, ">", new TextSpan(start, 1));
            return true;
        }

        if (ch == '<')
        {
            if (Match("<&"))
            {
                token = new Token(TokenKind.RedirectDupIn, "<&", new TextSpan(start, 2));
                return true;
            }

            _index++;
            token = new Token(TokenKind.RedirectIn, "<", new TextSpan(start, 1));
            return true;
        }

        return false;
    }

    private bool TryPeekRedirectionAfterDigits(out Token ioNumber, out Token redirectToken)
    {
        ioNumber = default;
        redirectToken = default;
        var start = _index;
        var digitsStart = _index;

        while (_index < _input.Length && char.IsDigit(_input[_index]))
        {
            _index++;
        }

        if (_index == digitsStart)
        {
            return false;
        }

        if (_index >= _input.Length)
        {
            _index = digitsStart;
            return false;
        }

        var redirectStart = _index;
        if (_input[_index] == '>' || _input[_index] == '<')
        {
            var digits = _input.Substring(digitsStart, _index - digitsStart);
            ioNumber = new Token(TokenKind.IoNumber, digits, new TextSpan(digitsStart, digits.Length));

            if (TryLexRedirection(out redirectToken))
            {
                return true;
            }

            _index = digitsStart;
            return false;
        }

        _index = digitsStart;
        return false;
    }

    private bool Match(string text)
    {
        if (_index + text.Length > _input.Length)
        {
            return false;
        }

        for (var i = 0; i < text.Length; i++)
        {
            if (_input[_index + i] != text[i])
            {
                return false;
            }
        }

        _index += text.Length;
        return true;
    }

    private bool IsAtEnd() => _index >= _input.Length;

    private static bool IsOperatorStart(char ch)
    {
        return ch is '|' or '&' or ';' or '>' or '<';
    }
}
