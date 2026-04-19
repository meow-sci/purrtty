using caTTY.CustomShells.GameStuffShell.Lexing;

namespace caTTY.CustomShells.GameStuffShell.Parsing;

public sealed record ListNode(IReadOnlyList<ListItem> Items);

public sealed record ListItem(PipelineNode Pipeline, ListOperator? OperatorToNext);

public enum ListOperator
{
    Sequential,
    AndIf,
    OrIf
}

public sealed record PipelineNode(IReadOnlyList<CommandNode> Commands);

public sealed record CommandNode(IReadOnlyList<string> Argv, IReadOnlyList<RedirectionNode> Redirections);

public enum RedirectionKind
{
    Out,
    OutAppend,
    In,
    DupOut,
    DupIn
}

public sealed record RedirectionNode(int? IoNumber, RedirectionKind Kind, string Target, TextSpan Span);
