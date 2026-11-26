using NUnit.Framework;
using purrTTY.Kfs.Runtime;

namespace purrTTY.Kfs.Tools.Tests.Projects;

/// <summary>
/// <see cref="ProjectResolver"/> over an in-memory workspace: the game workbench's project rules
/// (<c>Workspace.ProjectOf</c>) plus the overlay of unsaved buffers. No game needed. The same
/// shapes on disk are compared with the game's own <c>ProjectOf</c> in <c>ProjectParityTests</c>.
/// </summary>
[TestFixture]
public sealed class ProjectResolverTests
{
    private static readonly Dictionary<string, string> NoOverlay = new(StringComparer.Ordinal);

    private static KfsProject Project(MemoryWorkspaceFiles files, string file, Dictionary<string, string>? overlay = null)
    {
        ProjectResolution r = new ProjectResolver(files).Resolve(file, overlay ?? NoOverlay);
        Assert.That(r.Project, Is.Not.Null, () => $"no project for {file}: {r.Reason}");
        return r.Project!;
    }

    private static string[] Names(KfsProject p) => p.Files.Select(f => f.Name).ToArray();

    [Test]
    public void ProjectResolver_Folder_Project_Is_Every_Source_Directly_In_The_Folder()
    {
        var ws = new MemoryWorkspaceFiles()
            .Add("proj/main.c", "main").Add("proj/util.h", "util").Add("proj/b.kasm", "b").Add("proj/c.kinc", "c")
            .Add("proj/notes.txt", "x").Add("proj/README", "x").Add("proj/sub/deep.c", "deep").Add("proj/.hidden.h", "hidden")
            .Add("other/x.c", "x").Add("root.c", "root");

        KfsProject p = Project(ws, "proj/main.c");

        // Ordinal order ('.' < 'b' < 'c' < 'm' < 'u'); one level only; hidden files are not skipped (the game's ProjectOf).
        Assert.That(Names(p), Is.EqualTo(new[] { ".hidden.h", "b.kasm", "c.kinc", "main.c", "util.h" }));
        Assert.That(p.Files.Select(f => f.Path), Is.EqualTo(new[] { "proj/.hidden.h", "proj/b.kasm", "proj/c.kinc", "proj/main.c", "proj/util.h" }));
        Assert.That((p.Folder, p.Name, p.Language, p.File), Is.EqualTo(("proj", "proj", KfsProjectLanguage.Kc, "proj/main.c")));
        Assert.That(p.FileMap["util.h"], Is.EqualTo("util"));
        Assert.That(p.FromOverlay, Is.Empty);

        // Any file of the folder (a header too) gives the same project; the file need not exist.
        Assert.That(Names(Project(ws, "proj/util.h")), Is.EqualTo(Names(p)));
        Assert.That(Names(Project(ws, "proj/not-there.c")), Is.EqualTo(Names(p)));

        // Nested folders: the folder is everything before the last '/', the name its last segment.
        KfsProject deep = Project(ws, "proj/sub/deep.c");
        Assert.That((deep.Folder, deep.Name, Names(deep).Single()), Is.EqualTo(("proj/sub", "sub", "deep.c")));

        // No .c file: pure kASM.
        var kasm = Project(new MemoryWorkspaceFiles().Add("k/a.kasm", "").Add("k/b.kinc", ""), "k/a.kasm");
        Assert.That(kasm.Language, Is.EqualTo(KfsProjectLanguage.Kasm));
        Assert.That(kasm.KasmSources.Select(f => f.Name), Is.EqualTo(new[] { "a.kasm" }));
    }

    [Test]
    public void ProjectResolver_Folder_Project_Excludes_Vehicle_H_And_Vehicle_Kinc()
    {
        var ws = new MemoryWorkspaceFiles()
            .Add("proj/main.c", "").Add("proj/vehicle.h", "generated").Add("proj/vehicle.kinc", "generated");
        var overlay = new Dictionary<string, string> { ["proj/vehicle.h"] = "unsaved", ["proj/vehicle.kinc"] = "unsaved" };

        Assert.That(Names(Project(ws, "proj/main.c")), Is.EqualTo(new[] { "main.c" }));
        Assert.That(Names(Project(ws, "proj/main.c", overlay)), Is.EqualTo(new[] { "main.c" }), "an overlay cannot bring them back");
        Assert.That(ProjectResolver.IsProjectSource("vehicle.h"), Is.False);
        Assert.That(ProjectResolver.IsProjectSource("vehicle.kinc"), Is.False);
    }

    [Test]
    public void ProjectResolver_Root_C_Takes_The_Transitive_Closure_Of_Its_Quoted_Includes()
    {
        var ws = new MemoryWorkspaceFiles()
            .Add("main.c", """
                #include <kfs.h>
                #include "a.h"
                #include "sub/x.h"
                #include "missing.h"
                  #  include   "skipped.h"
                /* gap */
                  #  include   "dot.kasm.h"
                // #include "commented.h"
                /*
                #include "incomment.h"
                */
                """)
            .Add("a.h", "#include \"b.h\"\n")
            .Add("b.h", "#include \"a.h\"\n#include \"c.h\"\n")
            .Add("c.h", "/* leaf */\n")
            .Add("dot.kasm", "; #include \"never.h\" is not scanned: a .kasm is not walked\n")
            .Add("sub/x.h", "").Add("commented.h", "").Add("incomment.h", "").Add("never.h", "").Add("skipped.h", "")
            .Add("unrelated.h", "");

        KfsProject p = Project(ws, "main.c");

        // Breadth-first: main.c's matches in order (a.h, dot.kasm, incomment.h — the game's regex
        // matches inside a block comment, not after "//"), then a.h's (b.h), then b.h's (c.h).
        // skipped.h is missed exactly as the game misses it: the trailing \s* of the regex's match on
        // the line before consumes the newline and this line's indentation, so '^' no longer holds.
        Assert.That(Names(p), Is.EqualTo(new[] { "main.c", "a.h", "dot.kasm", "incomment.h", "b.h", "c.h" }));
        Assert.That((p.Folder, p.Name, p.Language), Is.EqualTo(("", "main", KfsProjectLanguage.Kc)));
        Assert.That(p.Files.Select(f => f.Path), Is.EqualTo(Names(p)), "at the root a name is its path");
    }

    [Test]
    public void ProjectResolver_Unsaved_New_Include_Is_Followed_Through_The_Overlay()
    {
        var ws = new MemoryWorkspaceFiles()
            .Add("main.c", "#include <kfs.h>\n")
            .Add("x.h", "#include \"y.h\"\n");
        var overlay = new Dictionary<string, string>
        {
            ["main.c"] = "#include <kfs.h>\n#include \"x.h\"\n",   // the unsaved new include
            ["y.h"] = "/* never saved */\n",                     // reached from x.h on disk; exists only in the overlay
        };

        Assert.That(Names(Project(ws, "main.c")), Is.EqualTo(new[] { "main.c" }), "the disk text has no include");

        KfsProject p = Project(ws, "main.c", overlay);
        Assert.That(Names(p), Is.EqualTo(new[] { "main.c", "x.h", "y.h" }));
        Assert.That(p.FromOverlay, Is.EquivalentTo(new[] { "main.c", "y.h" }));
        Assert.That(p.Files.Select(f => f.FromOverlay), Is.EqualTo(new[] { true, false, true }));
        Assert.That(p.FileMap["main.c"], Does.Contain("#include \"x.h\""));
    }

    [Test]
    public void ProjectResolver_Overlay_Only_New_File_Belongs_To_Its_Folder()
    {
        var ws = new MemoryWorkspaceFiles().Add("proj/main.c", "main").Add("proj/old.h", "old");
        var overlay = new Dictionary<string, string>
        {
            ["proj/new.h"] = "new",          // never saved
            ["proj/notes.txt"] = "not a source",
            ["proj/sub/inner.c"] = "another folder",
            ["other/z.c"] = "another folder",
        };

        KfsProject p = Project(ws, "proj/main.c", overlay);
        Assert.That(Names(p), Is.EqualTo(new[] { "main.c", "new.h", "old.h" }));
        Assert.That(p.FromOverlay, Is.EquivalentTo(new[] { "new.h" }));

        // A folder that exists only in the overlay is a project too.
        KfsProject fresh = Project(new MemoryWorkspaceFiles(), "fresh/main.c", new Dictionary<string, string> { ["fresh/main.c"] = "x" });
        Assert.That((fresh.Name, Names(fresh).Single(), fresh.FromOverlay.Single()), Is.EqualTo(("fresh", "main.c", "main.c")));

        // An overlay cannot delete: an empty buffer still counts.
        KfsProject emptied = Project(ws, "proj/main.c", new Dictionary<string, string> { ["proj/old.h"] = "" });
        Assert.That((Names(emptied).Length, emptied.FileMap["old.h"]), Is.EqualTo((2, "")));
    }

    [Test]
    public void ProjectResolver_Overlay_Overrides_The_Disk()
    {
        var ws = new MemoryWorkspaceFiles().Add("proj/main.c", "disk").Add("proj/util.h", "disk").Add("root.c", "disk");
        var overlay = new Dictionary<string, string> { ["proj\\util.h"] = "buffer", ["root.c"] = "buffer" };

        KfsProject p = Project(ws, "proj\\main.c", overlay);   // '\' is normalised to '/' in the file and the overlay keys
        Assert.That((p.FileMap["main.c"], p.FileMap["util.h"]), Is.EqualTo(("disk", "buffer")));
        Assert.That(p.File, Is.EqualTo("proj/main.c"));
        Assert.That(Project(ws, "root.c", overlay).FileMap["root.c"], Is.EqualTo("buffer"));
    }

    [Test]
    public void ProjectResolver_Kasm_H_Include_Pulls_In_The_Kasm_File()
    {
        var ws = new MemoryWorkspaceFiles()
            .Add("main.c", "#include \"dot3.kasm.h\"\n#include \"UP.KASM.H\"\n#include \"sub/f.kasm.h\"\n#include \"gone.kasm.h\"\n")
            .Add("dot3.kasm", ".include \"consts.kinc\"\n")
            .Add("UP.KASM", "")
            .Add("sub/f.kasm", "")
            .Add("consts.kinc", "");

        KfsProject p = Project(ws, "main.c");

        // "X.kasm.h" (suffix case-insensitive) adds X.kasm — a '/' allowed on this path — and does
        // not scan it: consts.kinc is not added (inside a kC project the compiler resolves kASM
        // includes from the project files only).
        Assert.That(Names(p), Is.EqualTo(new[] { "main.c", "dot3.kasm", "UP.KASM", "sub/f.kasm" }));
        Assert.That(p.PathOf("sub/f.kasm"), Is.EqualTo("sub/f.kasm"));
        Assert.That(p.PathOf("dot3.kasm.h"), Is.Null, "the generated header is virtual");
        Assert.That(p.Includes, Is.Empty, "no pure-kASM include scan in a kC project");
    }

    [Test]
    public void ProjectResolver_Root_Header_Is_No_Project_With_A_Reason()
    {
        var ws = new MemoryWorkspaceFiles().Add("util.h", "").Add("consts.kinc", "").Add("UPPER.H", "");
        var resolver = new ProjectResolver(ws);

        foreach (string header in new[] { "util.h", "consts.kinc", "UPPER.H" })
        {
            ProjectResolution r = resolver.Resolve(header);
            Assert.That(r.HasProject, Is.False, header);
            Assert.That(r.Reason, Is.EqualTo(header + " is a header: build a .c or .kasm file that includes it (a subfolder builds as one project)"));
        }

        // Missing files and folders: no project, with why.
        Assert.That(resolver.Resolve("nothing.c").Reason, Is.EqualTo("nothing.c: not found in the workspace"));
        Assert.That(resolver.Resolve("nofolder/main.c").Reason, Is.EqualTo("nofolder: no such folder in the workspace"));
    }

    [Test]
    public void ProjectResolver_Pure_Kasm_Reads_Includes_From_Folder_Then_Root()
    {
        var ws = new MemoryWorkspaceFiles()
            .Add("prog.kasm", ".include <kfs.kinc>\n.include \"purrtty.kinc\"\n  .include \"lib/extra.kinc\" ; a comment\n")
            .Add("purrtty.kinc", "; constants\n.include \"nested.kinc\"\n")
            .Add("nested.kinc", "")
            .Add("lib/extra.kinc", "")
            .Add("k/main.kasm", ".include \"local.kinc\"\n.include \"shared.kinc\"\n")
            .Add("k/local.kinc", "local")
            .Add("shared.kinc", "root shared")
            .Add("local.kinc", "root local");

        KfsProject root = Project(ws, "prog.kasm");
        Assert.That((Names(root).Single(), root.Language, root.Name), Is.EqualTo(("prog.kasm", KfsProjectLanguage.Kasm, "prog")));
        // The scan follows .include "…" (not <…>) transitively; the build reads through ReadInclude.
        Assert.That(root.Includes, Is.EquivalentTo(new Dictionary<string, string>
        {
            ["purrtty.kinc"] = "purrtty.kinc",
            ["nested.kinc"] = "nested.kinc",
            ["lib/extra.kinc"] = "lib/extra.kinc",
        }));
        Assert.That(root.ReadInclude("purrtty.kinc"), Does.StartWith("; constants"));
        Assert.That(root.ReadInclude("kfs.kinc"), Is.Null);
        Assert.That(root.PathOf("nested.kinc"), Is.EqualTo("nested.kinc"));

        // In a folder: folder/name first, then root-level name (Workbench.BuildText's ProjectFiles).
        KfsProject folder = Project(ws, "k/main.kasm");
        Assert.That(Names(folder), Is.EqualTo(new[] { "local.kinc", "main.kasm" }));
        Assert.That(folder.ReadInclude("local.kinc"), Is.EqualTo("local"));
        Assert.That(folder.ReadInclude("shared.kinc"), Is.EqualTo("root shared"));
        Assert.That(folder.PathOf("shared.kinc"), Is.EqualTo("shared.kinc"));
        Assert.That(folder.PathOf("local.kinc"), Is.EqualTo("k/local.kinc"));

        // The overlay wins here too, and can supply an include that was never saved.
        KfsProject overlaid = Project(ws, "k/main.kasm", new Dictionary<string, string> { ["shared.kinc"] = "buffer", ["k/new.kinc"] = "new" });
        Assert.That((overlaid.ReadInclude("shared.kinc"), overlaid.ReadInclude("new.kinc")), Is.EqualTo(("buffer", "new")));
    }

    [Test]
    public void ProjectResolver_Case_Rules_Match_The_Game()
    {
        var ws = new MemoryWorkspaceFiles()
            .Add("MAIN.C", "#include \"Util.h\"\n#include \"Other.h\"\n").Add("util.h", "").Add("Other.h", "")
            .Add("p/Main.C", "").Add("p/Vehicle.h", "").Add("p/LIB.H", "").Add("p/x.KASM", "").Add("p/alpha.c", "").Add("p/Zeta.h", "");

        // Root: the extension is compared case-insensitively (".C" is kC); include names are exact
        // lookups (in this case-sensitive store "Util.h" does not find util.h).
        KfsProject root = Project(ws, "MAIN.C");
        Assert.That((root.Language, root.Name), Is.EqualTo((KfsProjectLanguage.Kc, "MAIN")));
        Assert.That(Names(root), Is.EqualTo(new[] { "MAIN.C", "Other.h" }));

        // Folder: extensions case-insensitive, vehicle.h exclusion case-sensitive, ordinal order
        // (upper case first).
        KfsProject folder = Project(ws, "p/alpha.c");
        Assert.That(Names(folder), Is.EqualTo(new[] { "LIB.H", "Main.C", "Vehicle.h", "Zeta.h", "alpha.c", "x.KASM" }));
        Assert.That(folder.KasmSources.Select(f => f.Name), Is.EqualTo(new[] { "x.KASM" }));

        // Overlay keys are ordinal: "P/alpha.c" is another folder's file.
        KfsProject overlaid = Project(ws, "p/alpha.c", new Dictionary<string, string> { ["P/alpha.c"] = "other", ["p/ALPHA.c"] = "new file" });
        Assert.That((overlaid.FileMap["alpha.c"], overlaid.FileMap["ALPHA.c"]), Is.EqualTo(("", "new file")));
        Assert.That(overlaid.PathOf("Alpha.c"), Is.Null, "names map exactly");
    }

    [Test]
    public void ProjectResolver_Maps_Names_To_Paths_And_Back()
    {
        var ws = new MemoryWorkspaceFiles().Add("proj/main.c", "").Add("proj/util.h", "").Add("a.c", "#include \"b.h\"\n").Add("b.h", "");

        KfsProject folder = Project(ws, "proj/main.c");
        Assert.That(folder.PathOf("main.c"), Is.EqualTo("proj/main.c"));
        Assert.That(folder.PathOf("util.h"), Is.EqualTo("proj/util.h"));
        Assert.That(folder.WorkspacePath("main.c"), Is.EqualTo("proj/main.c"));
        foreach (string virtualName in new[] { "<kfs.h>", "<purrtty.h>", "<vehicle.h>", "<predefined>", "x.kasm.h", "proj", "proj/main.c" })
        {
            Assert.That(folder.PathOf(virtualName), Is.Null, virtualName);
        }
        Assert.That(folder.NameOf("proj/main.c"), Is.EqualTo("main.c"));
        Assert.That(folder.NameOf("proj\\util.h"), Is.EqualTo("util.h"));
        Assert.That(folder.NameOf("a.c"), Is.Null);

        KfsProject root = Project(ws, "a.c");
        Assert.That((root.PathOf("a.c"), root.PathOf("b.h"), root.NameOf("b.h")), Is.EqualTo(("a.c", "b.h", "b.h")));
    }

    [Test]
    public void ProjectResolver_On_Disk_Stays_In_The_Workspace_And_Refuses_Huge_Files()
    {
        using var ws = new TempWorkspace();
        string inside = Path.Combine(ws.Root, "inner");
        Directory.CreateDirectory(inside);
        File.WriteAllText(Path.Combine(ws.Root, "outside.kinc"), "; outside the workspace\n");
        File.WriteAllText(Path.Combine(inside, "prog.kasm"), ".include \"../outside.kinc\"\n");
        File.WriteAllText(Path.Combine(inside, "big.c"), "");
        using (var big = new FileStream(Path.Combine(inside, "big.c"), FileMode.Open)) big.SetLength(ProjectResolver.MaxSourceBytes + 1);

        var resolver = ProjectResolver.ForDisk(inside);
        KfsProject p = resolver.Resolve("prog.kasm").Project!;
        Assert.That(p.ReadInclude("../outside.kinc"), Is.Null, "the game would read above its workspace; the tool does not");
        Assert.That(p.ReadInclude(Path.Combine(ws.Root, "outside.kinc")), Is.Null, "nor a rooted path");
        Assert.That(resolver.Resolve("big.c").Reason, Is.EqualTo($"big.c is {ProjectResolver.MaxSourceBytes + 1} bytes; the largest source file kFS reads is {ProjectResolver.MaxSourceBytes} bytes"));
    }
}
