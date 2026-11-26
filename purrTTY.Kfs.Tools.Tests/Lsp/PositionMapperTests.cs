using NUnit.Framework;
using purrTTY.Kfs.Lsp.Documents;
using purrTTY.Kfs.Lsp.Protocol;

namespace purrTTY.Kfs.Tools.Tests.Lsp;

/// <summary><see cref="PositionMapper"/> and <see cref="LineIndex"/>: kFS points (1-based, UTF-16, CRLF normalised) ↔ LSP positions in three encodings.</summary>
[TestFixture]
public sealed class PositionMapperTests
{
    // Each case: the text, the kFS point of the word "oops" in it, and the expected LSP position of
    // that point in utf-16, utf-8 and utf-32.
    private static readonly (string Name, string Text, int Line, int Column, int Utf16, int Utf8, int Utf32)[] Cases =
    [
        ("ascii", "int x;\nint y = oops;\n", 2, 9, 8, 8, 8),
        ("tab", "\t\toops();\n", 1, 3, 2, 2, 2),
        // 🐱 is two UTF-16 units, four UTF-8 bytes and one code point; é is one unit and two bytes.
        ("astral", "/* 🐱 é */ oops();", 1, 12, 11, 14, 10),
        ("crlf", "a\r\nb\r\n\tz é oops\r\n", 3, 6, 5, 6, 5),
        ("lone cr", "a\rb\r\tz oops", 3, 4, 3, 3, 3),
        ("spike unicode.c", "\t/* 🐱 é */ kfs_logf(\"ü\"); oops();", 1, 28, 27, 31, 26),
    ];

    [Test]
    public void PositionMapper_Kfs_To_Lsp_In_Three_Encodings()
    {
        var failures = new List<string>();
        foreach (var c in Cases)
        {
            var lines = new LineIndex(c.Text);
            Assert.That(lines.Line(c.Line - 1).Substring(c.Column - 1, 4), Is.EqualTo("oops"), $"{c.Name}: the case's kFS column is on 'oops'");
            foreach ((PositionEncoding encoding, int expected) in new[] { (PositionEncoding.Utf16, c.Utf16), (PositionEncoding.Utf8, c.Utf8), (PositionEncoding.Utf32, c.Utf32) })
            {
                LspPosition p = PositionMapper.FromKfs(lines, c.Line, c.Column, encoding);
                if (p != new LspPosition(c.Line - 1, expected)) failures.Add($"{c.Name} {encoding.Name()}: {p}, expected {c.Line - 1}:{expected}");
            }
        }
        Assert.That(failures, Is.Empty, () => string.Join("\n", failures));
    }

    [Test]
    public void PositionMapper_Lsp_To_Kfs_Round_Trips()
    {
        var failures = new List<string>();
        foreach (var c in Cases)
        {
            var lines = new LineIndex(c.Text);
            foreach ((PositionEncoding encoding, int character) in new[] { (PositionEncoding.Utf16, c.Utf16), (PositionEncoding.Utf8, c.Utf8), (PositionEncoding.Utf32, c.Utf32) })
            {
                (int line, int column) = PositionMapper.ToKfs(lines, new LspPosition(c.Line - 1, character), encoding);
                if ((line, column) != (c.Line, c.Column)) failures.Add($"{c.Name} {encoding.Name()}: {line}:{column}, expected {c.Line}:{c.Column}");
                if (PositionMapper.Offset(lines, new LspPosition(c.Line - 1, character), encoding) != c.Text.IndexOf("oops", StringComparison.Ordinal))
                    failures.Add($"{c.Name} {encoding.Name()}: offset");
            }
        }
        Assert.That(failures, Is.Empty, () => string.Join("\n", failures));
    }

    [Test]
    public void PositionMapper_Clamps_And_Rounds_Inside_A_Character()
    {
        const string line = "a🐱b";
        // Inside the surrogate pair (utf-16 1..2) or inside the 4 UTF-8 bytes: the index moves past the character.
        Assert.That(PositionMapper.ToUtf16(line, 2, PositionEncoding.Utf8), Is.EqualTo(3));
        Assert.That(PositionMapper.ToUtf16(line, 2, PositionEncoding.Utf32), Is.EqualTo(3));
        Assert.That(PositionMapper.ToUtf16(line, 99, PositionEncoding.Utf16), Is.EqualTo(4));
        Assert.That(PositionMapper.ToCharacter(line, 99, PositionEncoding.Utf8), Is.EqualTo(6));
        Assert.That(PositionMapper.ToCharacter(line, -5, PositionEncoding.Utf32), Is.EqualTo(0));

        var lines = new LineIndex("x\ny");
        Assert.That(PositionMapper.FromKfs(lines, 0, 0, PositionEncoding.Utf16), Is.EqualTo(new LspPosition(0, 0)), "line 0 is 'no position'");
        Assert.That(PositionMapper.FromKfs(lines, 9, 1, PositionEncoding.Utf16), Is.EqualTo(new LspPosition(1, 0)), "a line past the end is the last line");
    }

    [Test]
    public void LineIndex_Splits_As_Lsp_Does()
    {
        var lines = new LineIndex("a\r\nb\rc\nd");
        Assert.That(Enumerable.Range(0, lines.Count).Select(lines.Line), Is.EqualTo(new[] { "a", "b", "c", "d" }));
        Assert.That(new LineIndex("").Count, Is.EqualTo(1));
        Assert.That(new LineIndex("x\n").Count, Is.EqualTo(2));
        Assert.That(lines.Start(1), Is.EqualTo(3));
        Assert.That(lines.End(1), Is.EqualTo(4));
    }

    [Test]
    public void PositionEncoding_Negotiation_Prefers_Utf16()
    {
        Assert.That(PositionEncodings.Negotiate(null), Is.EqualTo(PositionEncoding.Utf16));
        Assert.That(PositionEncodings.Negotiate([]), Is.EqualTo(PositionEncoding.Utf16));
        Assert.That(PositionEncodings.Negotiate(["utf-8", "utf-16", "utf-32"]), Is.EqualTo(PositionEncoding.Utf16));
        Assert.That(PositionEncodings.Negotiate(["utf-8"]), Is.EqualTo(PositionEncoding.Utf8));
        Assert.That(PositionEncodings.Negotiate(["utf-32", "utf-8"]), Is.EqualTo(PositionEncoding.Utf32));
        Assert.That(PositionEncodings.Negotiate(["utf-7"]), Is.EqualTo(PositionEncoding.Utf16));
    }
}
