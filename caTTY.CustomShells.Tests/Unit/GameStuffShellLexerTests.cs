using caTTY.CustomShells.GameStuffShell.Lexing;
using NUnit.Framework;

namespace caTTY.CustomShells.Tests.Unit;

[TestFixture]
public class GameStuffShellLexerTests
{
    [Test]
    public void Lex_WordsAndPipes()
    {
        var tokens = new Lexer("crafts|xargs lookat").Lex();

        AssertTokenKinds(tokens, TokenKind.Word, TokenKind.Pipe, TokenKind.Word, TokenKind.Word, TokenKind.End);
    }

    [Test]
    public void Lex_LogicalAndOrSemicolon()
    {
        var tokens = new Lexer("a && b || c ; d").Lex();

        AssertTokenKinds(tokens,
            TokenKind.Word,
            TokenKind.AndIf,
            TokenKind.Word,
            TokenKind.OrIf,
            TokenKind.Word,
            TokenKind.Semicolon,
            TokenKind.Word,
            TokenKind.End);
    }

    [Test]
    public void Lex_QuotedAndEscapedWords()
    {
        var tokens = new Lexer("echo \"a b\" 'c d' e\\ f").Lex();

        Assert.That(tokens[0].Text, Is.EqualTo("echo"));
        Assert.That(tokens[1].Text, Is.EqualTo("a b"));
        Assert.That(tokens[2].Text, Is.EqualTo("c d"));
        Assert.That(tokens[3].Text, Is.EqualTo("e f"));
    }

    [Test]
    public void Lex_WordJoining()
    {
        var tokens = new Lexer("\"a\"'b'c").Lex();

        AssertTokenKinds(tokens, TokenKind.Word, TokenKind.End);
        Assert.That(tokens[0].Text, Is.EqualTo("abc"));
    }

    [Test]
    public void Lex_EmptyStrings()
    {
        var tokens = new Lexer("echo \"\" ''").Lex();

        Assert.That(tokens[1].Text, Is.Empty);
        Assert.That(tokens[2].Text, Is.Empty);
    }

    [Test]
    public void Lex_RedirectionsWithIoNumber()
    {
        var tokens = new Lexer("2>&1 1>/dev/null").Lex();

        AssertTokenKinds(tokens,
            TokenKind.IoNumber,
            TokenKind.RedirectDupOut,
            TokenKind.Word,
            TokenKind.IoNumber,
            TokenKind.RedirectOut,
            TokenKind.Word,
            TokenKind.End);

        Assert.That(tokens[0].Text, Is.EqualTo("2"));
        Assert.That(tokens[2].Text, Is.EqualTo("1"));
        Assert.That(tokens[5].Text, Is.EqualTo("/dev/null"));
    }

    [Test]
    public void Lex_UnterminatedQuote_Throws()
    {
        var exception = Assert.Throws<LexerException>(() => new Lexer("echo \"unterminated").Lex());

        Assert.That(exception!.Message, Does.Contain("Unterminated"));
    }

    private static void AssertTokenKinds(IReadOnlyList<Token> tokens, params TokenKind[] kinds)
    {
        Assert.That(tokens.Count, Is.EqualTo(kinds.Length));
        for (var i = 0; i < kinds.Length; i++)
        {
            Assert.That(tokens[i].Kind, Is.EqualTo(kinds[i]), $"Token {i} kind");
        }
    }
}
