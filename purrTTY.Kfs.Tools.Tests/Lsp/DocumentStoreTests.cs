using NUnit.Framework;
using purrTTY.Kfs.Lsp.Documents;
using purrTTY.Kfs.Lsp.Protocol;

namespace purrTTY.Kfs.Tools.Tests.Lsp;

/// <summary><see cref="WorkspaceUris"/> (URI ↔ workspace path) and <see cref="DocumentStore"/>.</summary>
[TestFixture]
public sealed class DocumentStoreTests
{
    [Test]
    public void Uris_Decode_Percent_Encoding_And_Stay_Inside_The_Workspace()
    {
        var uris = new WorkspaceUris("/home/kat/My Games/kfs", caseInsensitive: false, windows: false);

        Assert.That(uris.ToPath("file:///home/kat/My%20Games/kfs/proj/main.c"), Is.EqualTo("proj/main.c"));
        Assert.That(uris.ToPath("file:///home/kat/My Games/kfs/main.c"), Is.EqualTo("main.c"), "an unencoded space (some clients)");
        Assert.That(uris.ToPath("file://localhost/home/kat/My%20Games/kfs/%C3%A9t%C3%A9.c"), Is.EqualTo("été.c"), "UTF-8 percent-encoding, localhost authority");
        Assert.That(uris.ToPath("file:///home/kat/My%20games/kfs/main.c"), Is.Null, "case matters on a case-sensitive file system");
        Assert.That(uris.ToPath("file:///home/kat/My%20Games/kfs"), Is.Null, "the root itself is no file");
        Assert.That(uris.ToPath("file:///home/kat/My%20Games/kfsx/main.c"), Is.Null, "a sibling with the same prefix");
        Assert.That(uris.ToPath("file:///home/kat/My%20Games/kfs/../secret.c"), Is.Null);
        Assert.That(uris.ToPath("untitled:Untitled-1"), Is.Null);
        Assert.That(uris.ToPath("file:///home/kat/My%20Games/kfs/bad%ZZ.c"), Is.Null, "malformed escapes");

        Assert.That(uris.ToUri("proj/a b.c"), Is.EqualTo("file:///home/kat/My%20Games/kfs/proj/a%20b.c"));
        Assert.That(uris.ToPath(uris.ToUri("sub/été #1.c")), Is.EqualTo("sub/été #1.c"));
    }

    [Test]
    public void Uris_Handle_Windows_Drive_Letters_And_Case()
    {
        var uris = new WorkspaceUris(@"C:\Users\Kat\Documents\My Games\Kitten Space Agency\kfs", windows: true);
        Assert.That(uris.CaseInsensitive, Is.True);

        foreach (string uri in new[]
        {
            "file:///C:/Users/Kat/Documents/My%20Games/Kitten%20Space%20Agency/kfs/proj/main.c",   // Neovim
            "file:///c%3A/Users/Kat/Documents/My%20Games/Kitten%20Space%20Agency/kfs/proj/main.c", // VS Code
            "file:///c:/users/kat/documents/my%20games/kitten%20space%20agency/KFS/proj/main.c",   // other case
            "file:///C|/Users/Kat/Documents/My%20Games/Kitten%20Space%20Agency/kfs/proj/main.c",
        })
        {
            Assert.That(uris.ToPath(uri), Is.EqualTo("proj/main.c"), uri);
        }
        Assert.That(uris.ToPath("file:///D:/Users/Kat/Documents/My%20Games/Kitten%20Space%20Agency/kfs/x.c"), Is.Null, "another drive");
        Assert.That(uris.ToUri("proj/main.c"), Is.EqualTo("file:///C:/Users/Kat/Documents/My%20Games/Kitten%20Space%20Agency/kfs/proj/main.c"));

        var unc = new WorkspaceUris(@"\\server\share\kfs", windows: true);
        Assert.That(unc.ToPath("file://server/share/kfs/a.c"), Is.EqualTo("a.c"));
        Assert.That(unc.ToUri("a.c"), Is.EqualTo("file://server/share/kfs/a.c"));
    }

    [Test]
    public void Uris_Map_Through_Symbolic_Links_And_To_Disk_Case()
    {
        using var ws = new TempWorkspace().Write("Proj/Main.c", "");
        var uris = new WorkspaceUris(ws.Root);

        Assert.That(uris.ToPath(new Uri(ws.Full("Proj/Main.c")).AbsoluteUri), Is.EqualTo("Proj/Main.c"));
        if (uris.CaseInsensitive)
        {
            // A client that spells the file in another case gets the disk's spelling, so the overlay
            // replaces the file the compiler would read instead of adding a second one.
            Assert.That(uris.ToPath(new Uri(ws.Full("proj/main.c")).AbsoluteUri), Is.EqualTo("Proj/Main.c"));
        }

        // macOS: the temp folder is reached through /var → /private/var; a client may send either.
        string? real = RealPath(ws.Root);
        if (real is not null && real != ws.Root)
            Assert.That(uris.ToPath(new Uri(Path.Combine(real, "Proj", "Main.c")).AbsoluteUri), Is.EqualTo("Proj/Main.c"));
    }

    [Test]
    public void Store_Keeps_Versions_Generations_And_Applies_Edits()
    {
        var store = new DocumentStore(new WorkspaceUris("/ws", caseInsensitive: false, windows: false));
        LspDocument a = store.Open("file:///ws/a.c", "kc", 1, "hello\nworld\n");
        Assert.That((a.Path, a.Version), Is.EqualTo(("a.c", 1)));

        LspDocument? b = store.Apply("file:///ws/a.c", 2, [(LspRange.OnLine(1, 0, 5), "there"), (null, "replaced"), (LspRange.OnLine(0, 0, 0), ">")], PositionEncoding.Utf16);
        Assert.That(b!.Text, Is.EqualTo(">replaced"));
        Assert.That(b.Generation, Is.GreaterThan(a.Generation));
        Assert.That(store.ByPath("a.c"), Is.SameAs(b));

        LspDocument? c = store.Apply("file:///ws/a.c", 3, [(new LspRange(new LspPosition(0, 1), new LspPosition(0, 9)), "🐱")], PositionEncoding.Utf8);
        Assert.That(c!.Text, Is.EqualTo(">🐱"));

        store.Open("file:///elsewhere/x.c", "kc", 1, "");
        Assert.That(store.Overlay().Keys, Is.EqualTo(new[] { "a.c" }), "a document outside the workspace is not overlaid");
        Assert.That(store.UriOf("a.c"), Is.EqualTo("file:///ws/a.c"));
        Assert.That(store.UriOf("b.h"), Is.EqualTo("file:///ws/b.h"));

        Assert.That(store.Close("file:///ws/a.c")?.Path, Is.EqualTo("a.c"));
        Assert.That(store.ByPath("a.c"), Is.Null);
        Assert.That(store.Change("file:///ws/a.c", 9, "x"), Is.Null, "a closed document is not changed");
    }

    private static string? RealPath(string path)
    {
        try
        {
            string current = Path.GetPathRoot(path)!;
            foreach (string segment in path[current.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
            {
                string next = Path.Combine(current, segment);
                current = new DirectoryInfo(next).ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? next;
            }
            return Path.TrimEndingDirectorySeparator(current);
        }
        catch (IOException)
        {
            return null;
        }
    }
}
