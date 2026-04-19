using caTTY.CustomShells.GameStuffShell.Lexing;
using caTTY.CustomShells.GameStuffShell.Parsing;
using NUnit.Framework;

namespace caTTY.CustomShells.Tests.Unit;

[TestFixture]
public class GameStuffShellParserTests
{
    [Test]
    public void ParseList_BuildsPrecedence()
    {
        var tokens = new Lexer("a | b && c ; d").Lex();
        var parser = new Parser(tokens);

        var list = parser.ParseList();

        Assert.That(list.Items.Count, Is.EqualTo(3));
        Assert.That(list.Items[0].Pipeline.Commands.Count, Is.EqualTo(2));
        Assert.That(list.Items[0].OperatorToNext, Is.EqualTo(ListOperator.AndIf));
        Assert.That(list.Items[1].OperatorToNext, Is.EqualTo(ListOperator.Sequential));
        Assert.That(list.Items[2].OperatorToNext, Is.Null);
    }

    [Test]
    public void ParseList_AttachesRedirectionsToCommand()
    {
        var tokens = new Lexer("a 2>&1 | b").Lex();
        var parser = new Parser(tokens);

        var list = parser.ParseList();

        var firstCommand = list.Items[0].Pipeline.Commands[0];
        Assert.That(firstCommand.Redirections.Count, Is.EqualTo(1));
        Assert.That(firstCommand.Redirections[0].Kind, Is.EqualTo(RedirectionKind.DupOut));
        Assert.That(firstCommand.Redirections[0].IoNumber, Is.EqualTo(2));
    }

    [Test]
    public void ParseList_EmptyCommandBetweenSemicolonsThrows()
    {
        var tokens = new Lexer("a ; ; b").Lex();
        var parser = new Parser(tokens);

        Assert.Throws<ParserException>(() => parser.ParseList());
    }
}
