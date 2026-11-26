using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using purrTTY.Kfs.Runtime;
using purrTTY.Kfs.Tools.Tests.Lsp;

namespace purrTTY.Kfs.Tools.Tests.Projects;

/// <summary>
/// Finding C1 and D1 for <see cref="ProjectResolver"/> (no game): the include scans are linear and
/// find exactly what the game's regexes find; FIFOs and devices never block a read; an unsaved
/// buffer is held to the game's source limit; a file name the file system refuses is a reason, not
/// an exception.
/// </summary>
[TestFixture]
public sealed class IncludeScanTests
{
    // The game's own patterns (decompiled Workspace.cs:169, Assembler.cs:294).
    private static readonly Regex GameQuoted = new("^\\s*#\\s*include\\s*\"([^\"]+)\"\\s*", RegexOptions.Multiline);
    private static readonly Regex GameKasm = new("^[ \\t]*\\.include[ \\t]+\"([^\"]+)\"", RegexOptions.Multiline);

    private static readonly string[] Pieces =
    [
        "#", "include", "include ", " ", "\t", "\n", "\r\n", "\r", "\"", "a", "b.h", "x.kasm.h", "\u00A0", "\v", "\f", "/*", "*/", ".include", ".include ", "  ", "#include \"u.h\"",
    ];

    private static string Random(Random random, int pieces)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < pieces; i++) sb.Append(Pieces[random.Next(Pieces.Length)]);
        return sb.ToString();
    }

    [Test]
    public void The_Linear_Scans_Find_Exactly_What_The_Game_Regexes_Find()
    {
        var random = new Random(20261125);
        for (int round = 0; round < 20_000; round++)
        {
            string text = Random(random, random.Next(0, 40));
            string[] expected = GameQuoted.Matches(text).Select(m => m.Groups[1].Value).ToArray();
            Assert.That(ProjectResolver.QuotedIncludes(text), Is.EqualTo(expected), () => "kC: " + Escape(text));
            string[] kasm = GameKasm.Matches(text).Select(m => m.Groups[1].Value).ToArray();
            Assert.That(ProjectResolver.KasmQuotedIncludes(text), Is.EqualTo(kasm), () => "kASM: " + Escape(text));
        }
        // The documented quirks, by hand.
        Assert.That(ProjectResolver.QuotedIncludes("#include \"a.h\"\n  #include \"b.h\"\n#include \"c.h\""), Is.EqualTo(new[] { "a.h", "c.h" }),
            "an indented include right after an include line is missed, as in the game");
        Assert.That(ProjectResolver.QuotedIncludes("/* #include \"a.h\" */\n#include \"multi\nline\""), Is.EqualTo(new[] { "multi\nline" }));
    }

    private static string Escape(string s) => s.Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");

    [Test]
    public void The_Scans_Are_Linear_On_The_Inputs_That_Were_Quadratic()
    {
        // R6: 20k/40k/80k blank lines took 572/2092/8430 ms; 400k would take minutes.
        var inputs = new[]
        {
            new string('\n', 400_000) + "int x;\n",
            string.Concat(Enumerable.Repeat("#include \"never closed\n", 50_000)),
            "#" + new string('\n', 400_000) + "x",
            string.Concat(Enumerable.Repeat("  .include \"open\n", 50_000)),
        };
        var resolver = new ProjectResolver(new MemoryWorkspaceFiles());
        var clock = Stopwatch.StartNew();
        foreach (string text in inputs)
        {
            _ = ProjectResolver.QuotedIncludes(text).Count();
            _ = ProjectResolver.KasmQuotedIncludes(text).Count();
        }
        resolver.Resolve("main.c", new Dictionary<string, string> { ["main.c"] = inputs[0] });
        Assert.That(clock.Elapsed, Is.LessThan(TimeSpan.FromSeconds(3)), "linear: well under a second on a laptop; the bound only catches a quadratic scan");
    }

    [Test]
    public void An_Unsaved_Buffer_Over_The_Source_Limit_Is_Refused_With_The_Games_Message()
    {
        var resolver = new ProjectResolver(new MemoryWorkspaceFiles());
        string big = new('x', (int)ProjectResolver.MaxSourceBytes + 1);
        ProjectResolution r = resolver.Resolve("main.c", new Dictionary<string, string> { ["main.c"] = big });
        Assert.That(r.Project, Is.Null);
        Assert.That(r.Reason, Is.EqualTo($"main.c is {ProjectResolver.MaxSourceBytes + 1} bytes; the largest source file kFS reads is {ProjectResolver.MaxSourceBytes} bytes"));
        Assert.That(resolver.Resolve("main.c", new Dictionary<string, string> { ["main.c"] = new string('x', (int)ProjectResolver.MaxSourceBytes) }).Project, Is.Not.Null);
    }

    [Test]
    public void A_Quoted_Include_With_A_Nul_Is_A_Missing_File_Not_An_Exception()
    {
        using var ws = new TempWorkspace().Write("main.c", "#include \"a\0b.h\"\nvoid f(void) { }\n");
        ProjectResolution r = ProjectResolver.ForDisk(ws.Root).Resolve("main.c");
        Assert.That(r.Project, Is.Not.Null, r.Reason);
        Assert.That(r.Project!.Files.Select(f => f.Name), Is.EqualTo(new[] { "main.c" }), "as File.Exists (the game's check): no such file");
        Assert.That(new DiskWorkspaceFiles(ws.Root).Exists("a\0b.h"), Is.False);
    }

    [Test]
    public void A_Fifo_Or_A_Device_In_A_Project_Never_Blocks_A_Read()
    {
        if (OperatingSystem.IsWindows()) Assert.Ignore("no FIFOs");
        using var ws = new TempWorkspace().Write("proj/main.c", "#include \"pipe.h\"\nvoid f(void) { }\n").Write("root.c", "#include \"pipe.h\"\n#include \"zero.h\"\n");
        Mkfifo(ws.Full("proj/pipe.h"));
        Mkfifo(ws.Full("pipe.h"));
        File.CreateSymbolicLink(ws.Full("zero.h"), "/dev/zero");
        File.CreateSymbolicLink(ws.Full("proj/link.h"), ws.Full("proj/pipe.h"));
        var resolver = ProjectResolver.ForDisk(ws.Root);
        Task<(ProjectResolution, ProjectResolution)> both = Task.Run(() => (resolver.Resolve("proj/main.c"), resolver.Resolve("root.c")));
        Assert.That(both.Wait(LspTestClient.Timeout), Is.True, "a FIFO blocked the read (File.ReadAllText waits for a writer forever)");
        (ProjectResolution folder, ProjectResolution root) = both.Result;
        Assert.That(folder.Project!.FileMap["pipe.h"], Is.Empty, "read as an empty file, never opened");
        Assert.That(folder.Project.FileMap["link.h"], Is.Empty, "a link to a FIFO likewise");
        Assert.That(root.Project!.FileMap["zero.h"], Is.Empty, "a device streams forever: read as empty");
    }

    private static void Mkfifo(string path)
    {
        using var p = Process.Start(new ProcessStartInfo("mkfifo", [path]) { UseShellExecute = false })!;
        Assert.That(p.WaitForExit(LspTestClient.Timeout), Is.True);
        Assert.That(p.ExitCode, Is.Zero, "mkfifo");
    }
}
