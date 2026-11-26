using System.Text;

namespace purrTTY.Kfs.Commands;

/// <summary>
/// ASCII stand-ins for the typographic characters purrTTY's texts use (<c>—</c>, <c>–</c>,
/// <c>→</c>, <c>…</c>, curly quotes …), for text shown in the game's console, whose font may not
/// have them (it cannot be checked from here). <see cref="PurrConsole.AsciiOutput"/> applies it to
/// everything <c>purr</c> prints. Any other character is kept: part names are the game's own.
/// </summary>
public static class ConsoleText
{
    /// <summary>
    /// <paramref name="text"/> with every character of the table replaced by its ASCII stand-in;
    /// the same instance (no allocation) when it has none.
    /// </summary>
    public static string ToAscii(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        int first = -1;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] > '~' && StandIn(text[i]) is not null)
            {
                first = i;
                break;
            }
        }
        if (first < 0) return text;

        var b = new StringBuilder(text.Length + 8);
        b.Append(text, 0, first);
        for (int i = first; i < text.Length; i++)
        {
            char c = text[i];
            if (c > '~' && StandIn(c) is { } ascii) b.Append(ascii);
            else b.Append(c);
        }
        return b.ToString();
    }

    /// <summary>The ASCII stand-in of <paramref name="c"/>, or null to keep it.</summary>
    public static string? StandIn(char c) => c switch
    {
        '—' => "-",     // — em dash
        '–' => "-",     // – en dash
        '−' => "-",     // − minus sign
        '→' => "->",    // →
        '←' => "<-",    // ←
        '…' => "...",   // …
        '‘' or '’' => "'",
        '“' or '”' => "\"",
        ' ' => " ",     // no-break space
        '×' => "x",     // ×
        '≤' => "<=",    // ≤
        '≥' => ">=",    // ≥
        '•' => "*",     // •
        '·' => "-",     // ·
        _ => null,
    };
}
