// RULE FOR THIS FILE: no game type.

using System.Text.RegularExpressions;
using purrTTY.Kfs.Lsp.Index;

namespace purrTTY.Kfs.Lsp.Features;

/// <summary>Finding the word under the cursor and resolving member chains (<c>commands.engine[0].</c>).</summary>
internal static partial class Words
{
    /// <summary>
    /// The word containing UTF-16 column <paramref name="cursor"/> of <paramref name="line"/> (or ending
    /// just before it): kC words are identifiers; kASM words also hold dots (<c>sys.cycles</c>,
    /// <c>.func</c>). Null when there is none.
    /// </summary>
    public static (int Start, int End)? At(string line, int cursor, SourceLanguage language)
    {
        Func<char, bool> part = language == SourceLanguage.Kasm ? c => SourceScan.IsIdentifierChar(c) || c == '.' : SourceScan.IsIdentifierChar;
        int at = cursor < line.Length && part(line[cursor]) ? cursor : cursor > 0 && part(line[cursor - 1]) ? cursor - 1 : -1;
        if (at < 0) return null;
        int start = at, end = at + 1;
        while (start > 0 && part(line[start - 1])) start--;
        while (end < line.Length && part(line[end])) end++;
        if (language == SourceLanguage.Kasm)
        {
            while (end > start && line[end - 1] == '.') end--;
            if (end <= start) return null;
        }
        return (start, end);
    }

    /// <summary>A diagnostic code (<c>KC-E003</c>, <c>KA-W711</c>) containing the cursor, or null.</summary>
    public static (int Start, int End, string Code)? DiagnosticCode(string line, int cursor)
    {
        foreach (Match m in Code().Matches(line))
        {
            if (cursor >= m.Index && cursor <= m.Index + m.Length) return (m.Index, m.Index + m.Length, m.Value);
        }
        return null;
    }

    /// <summary>
    /// The member chain ending at UTF-16 column <paramref name="end"/> (just after a <c>.</c> or
    /// <c>-&gt;</c>): the root identifier and the member names after it, indices dropped
    /// (<c>vp.thruster[i].force_body_N.</c> → <c>vp</c>, [<c>thruster</c>, <c>force_body_N</c>]). Null
    /// when the expression before the operator is not a plain chain (a call, a cast, a literal).
    /// </summary>
    public static (string Root, List<string> Members, int RootStart)? Chain(string line, int end)
    {
        var members = new List<string>();
        int i = end;
        while (true)
        {
            // i is just after an operator: step over it.
            if (i >= 1 && line[i - 1] == '.') i--;
            else if (i >= 2 && line[i - 1] == '>' && line[i - 2] == '-') i -= 2;
            else return null;
            while (i > 0 && char.IsWhiteSpace(line[i - 1])) i--;
            // Index brackets.
            while (i > 0 && line[i - 1] == ']')
            {
                int depth = 0;
                int j = i - 1;
                for (; j >= 0; j--)
                {
                    if (line[j] == ']') depth++;
                    else if (line[j] == '[' && --depth == 0) break;
                }
                if (j < 0) return null;
                i = j;
                while (i > 0 && char.IsWhiteSpace(line[i - 1])) i--;
            }
            int stop = i;
            while (i > 0 && SourceScan.IsIdentifierChar(line[i - 1])) i--;
            if (i == stop || char.IsDigit(line[i])) return null;
            string name = line[i..stop];
            int before = i;
            while (before > 0 && char.IsWhiteSpace(line[before - 1])) before--;
            bool more = before >= 1 && line[before - 1] == '.' && !(before >= 2 && char.IsDigit(line[before - 2]))
                || before >= 2 && line[before - 1] == '>' && line[before - 2] == '-';
            if (!more)
            {
                members.Reverse();
                return (name, members, i);
            }
            members.Add(name);
            i = before;
        }
    }

    /// <summary>The shape a chain resolves to: the root's shape <paramref name="root"/>, then each member in turn; null when one is unknown.</summary>
    public static MemberShape? Resolve(MemberShape? root, IEnumerable<string> members)
    {
        MemberShape? shape = root;
        foreach (string member in members)
        {
            if (shape?.Find(member) is not { } m) return null;
            shape = m.Shape;
        }
        return shape;
    }

    [GeneratedRegex(@"\b[A-Z]{2,3}-[EW]\d{3}\b")]
    private static partial Regex Code();
}
