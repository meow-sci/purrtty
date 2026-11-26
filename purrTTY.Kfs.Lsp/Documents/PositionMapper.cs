// RULE FOR THIS FILE: no game type.

using System.Text;
using purrTTY.Kfs.Lsp.Protocol;

namespace purrTTY.Kfs.Lsp.Documents;

/// <summary>
/// The lines of a text as LSP sees them: terminated by <c>\r\n</c>, <c>\n</c> or a lone <c>\r</c>.
/// kFS's kC lexer rewrites <c>\r\n</c> and a lone <c>\r</c> to <c>\n</c> before it counts, so line
/// <c>n</c> here is kFS line <c>n + 1</c> and the characters of a line are the same.
/// </summary>
public sealed class LineIndex
{
    private readonly string _text;
    private readonly int[] _starts;
    private readonly int[] _ends;

    /// <summary>Indexes <paramref name="text"/>.</summary>
    public LineIndex(string text)
    {
        _text = text ?? throw new ArgumentNullException(nameof(text));
        var starts = new List<int> { 0 };
        var ends = new List<int>();
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c != '\n' && c != '\r') continue;
            ends.Add(i);
            if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
            starts.Add(i + 1);
        }
        ends.Add(text.Length);
        _starts = starts.ToArray();
        _ends = ends.ToArray();
    }

    /// <summary>The text.</summary>
    public string Text => _text;

    /// <summary>Lines (a text ending in a terminator has an empty last line; an empty text has one).</summary>
    public int Count => _starts.Length;

    /// <summary>The 0-based line's text without its terminator; empty for a line past the end.</summary>
    public string Line(int line) => line >= 0 && line < Count ? _text[_starts[line].._ends[line]] : "";

    /// <summary>The offset of the line's first character in <see cref="Text"/> (clamped to the text).</summary>
    public int Start(int line) => line < 0 ? 0 : line >= Count ? _text.Length : _starts[line];

    /// <summary>The offset just past the line's last character (before its terminator).</summary>
    public int End(int line) => line < 0 ? 0 : line >= Count ? _text.Length : _ends[line];
}

/// <summary>
/// Converts between kFS positions and LSP positions. kFS reports a <b>point</b>: a 1-based line and
/// a 1-based column counted in UTF-16 code units of the text after CRLF→LF normalisation (a tab is
/// one column; an astral character is two), and no end. LSP counts 0-based lines and 0-based
/// characters in the negotiated encoding: UTF-16 code units (kFS's own unit), UTF-8 bytes or UTF-32
/// code points. Every conversion clamps to the line.
/// </summary>
public static class PositionMapper
{
    /// <summary>The LSP character of the UTF-16 index <paramref name="utf16"/> in <paramref name="line"/>.</summary>
    public static int ToCharacter(string line, int utf16, PositionEncoding encoding)
    {
        ArgumentNullException.ThrowIfNull(line);
        utf16 = Math.Clamp(utf16, 0, line.Length);
        switch (encoding)
        {
            case PositionEncoding.Utf8:
                return Encoding.UTF8.GetByteCount(line.AsSpan(0, utf16));
            case PositionEncoding.Utf32:
                int points = 0;
                for (int i = 0; i < utf16; i++)
                {
                    if (char.IsHighSurrogate(line[i]) && i + 1 < utf16 && char.IsLowSurrogate(line[i + 1])) i++;
                    points++;
                }
                return points;
            default:
                return utf16;
        }
    }

    /// <summary>The UTF-16 index in <paramref name="line"/> of the LSP character <paramref name="character"/> (one inside a character rounds up past it).</summary>
    public static int ToUtf16(string line, int character, PositionEncoding encoding)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (character <= 0) return 0;
        if (encoding == PositionEncoding.Utf16) return Math.Min(character, line.Length);
        int i = 0, count = 0;
        while (i < line.Length && count < character)
        {
            int width = char.IsSurrogatePair(line, i) ? 2 : 1;
            count += encoding == PositionEncoding.Utf8 ? Encoding.UTF8.GetByteCount(line.AsSpan(i, width)) : 1;
            i += width;
        }
        return i;
    }

    /// <summary>
    /// The LSP position of the kFS point (<paramref name="line"/>, <paramref name="column"/>), both
    /// 1-based; line 0 ("no position") is the start of the text.
    /// </summary>
    public static LspPosition FromKfs(LineIndex lines, int line, int column, PositionEncoding encoding)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (line <= 0) return new LspPosition(0, 0);
        int l = Math.Min(line - 1, lines.Count - 1);
        string text = lines.Line(l);
        return new LspPosition(l, ToCharacter(text, column - 1, encoding));
    }

    /// <summary>The kFS point (1-based line and column, UTF-16) of an LSP position.</summary>
    public static (int Line, int Column) ToKfs(LineIndex lines, LspPosition position, PositionEncoding encoding)
    {
        ArgumentNullException.ThrowIfNull(lines);
        int l = Math.Clamp(position.Line, 0, lines.Count - 1);
        return (l + 1, ToUtf16(lines.Line(l), position.Character, encoding) + 1);
    }

    /// <summary>The offset in <see cref="LineIndex.Text"/> of an LSP position (for incremental edits).</summary>
    public static int Offset(LineIndex lines, LspPosition position, PositionEncoding encoding)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (position.Line < 0) return 0;
        if (position.Line >= lines.Count) return lines.Text.Length;
        return lines.Start(position.Line) + ToUtf16(lines.Line(position.Line), position.Character, encoding);
    }

    /// <summary>An LSP range on one line from UTF-16 indexes.</summary>
    public static LspRange Range(LineIndex lines, int line, int startUtf16, int endUtf16, PositionEncoding encoding)
    {
        ArgumentNullException.ThrowIfNull(lines);
        string text = lines.Line(line);
        return LspRange.OnLine(line, ToCharacter(text, startUtf16, encoding), ToCharacter(text, Math.Max(startUtf16, endUtf16), encoding));
    }
}
