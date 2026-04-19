namespace caTTY.CustomShells.GameStuffShell.Lexing;

public enum TokenKind
{
    Word,
    Pipe,
    AndIf,
    OrIf,
    Semicolon,
    RedirectOut,
    RedirectOutAppend,
    RedirectIn,
    RedirectDupOut,
    RedirectDupIn,
    IoNumber,
    End
}

public readonly record struct Token(TokenKind Kind, string Text, TextSpan Span);

public readonly record struct TextSpan(int Start, int Length)
{
    public int End => Start + Length;
}

public sealed class LexerException : Exception
{
    public LexerException(string message, TextSpan span)
        : base(message)
    {
        Span = span;
    }

    public TextSpan Span { get; }
}
