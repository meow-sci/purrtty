using System.Reflection;
using System.Runtime.CompilerServices;
using KSA.Kfs.KAsm;
using KSA.Kfs.Vm.Tables;
using NUnit.Framework;
using purrTTY.Kfs.Tools.Tests.Nvim;

namespace purrTTY.Kfs.Tools.Tests.Runtime.Nvim;

/// <summary>
/// <c>purrTTY.GameMod/nvim/syntax/kasm.vim</c> against the game: regenerated from the opcode
/// table's mnemonics and the assembler's directives and compared with the checked-in file (copied
/// to the output folder). <c>PURRTTY_UPDATE_SNAPSHOTS=1</c> rewrites the source file instead.
/// </summary>
[TestFixture]
public sealed class KasmSyntaxTests : ToolsRuntimeFixture
{
    private const string UpdateVariable = "PURRTTY_UPDATE_SNAPSHOTS";

    [Test]
    public void Kasm_Syntax_Matches_The_Game()
    {
        (List<string> mnemonics, SortedSet<string> directives, List<string> irOnly, List<string> includes) = FromGame(Runtime.Tables);

        // The extraction itself: the directives every kASM program uses, and nothing that is not one.
        Assert.That(mnemonics, Has.Member("PUSH32").And.Member("SYSCALL").And.Member("RET").And.Member("YIELD"));
        Assert.That(directives, Is.SupersetOf(new[] { ".func", ".endfunc", ".task", ".tick_hz", ".include", ".macro", ".endm", ".equ", ".format", ".local64", ".loop_bound", ".word" }));
        Assert.That(directives, Has.None.EqualTo(".size").And.None.EqualTo(".arg"));
        Assert.That(irOnly, Is.EquivalentTo(new[] { ".lang", ".loop", ".endloop" }));
        Assert.That(directives, Is.SupersetOf(irOnly), "the IR-only names are literals of the assembler too");

        string actual = KasmSyntax.Generate(mnemonics, directives, irOnly, includes);
        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            string root = FindRepositoryRoot(AppContext.BaseDirectory)
                ?? throw new InvalidOperationException("cannot find purrtty.slnx above the test output folder to update kasm.vim");
            foreach (string path in new[] { Path.Combine(root, "purrTTY.GameMod", "nvim", "syntax", "kasm.vim"), Path.Combine(NvimFiles.Shipped, "syntax", "kasm.vim") })
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, actual);
            }
            return;
        }

        string expectedPath = Path.Combine(NvimFiles.Shipped, "syntax", "kasm.vim");
        Assert.That(File.Exists(expectedPath), Is.True, $"missing {expectedPath}; create it with {UpdateVariable}=1");
        string expected = File.ReadAllText(expectedPath).Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.That(actual, Is.EqualTo(expected),
            $"purrTTY.GameMod/nvim/syntax/kasm.vim no longer matches the game's opcode table and assembler. Run the tests with {UpdateVariable}=1 and commit the new file.");
    }

    // Game types only in this helper's body and parameter (ModuleInit.cs).
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (List<string>, SortedSet<string>, List<string>, List<string>) FromGame(GameTables tables)
    {
        var mnemonics = tables.OpcodesByMnemonic.Keys.ToList();
        SortedSet<string> directives = KasmSyntax.DirectiveLiterals(typeof(Assembler));
        var irOnly = ((IEnumerable<string>)typeof(Assembler).GetField("IrOnly", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!).ToList();
        var includes = BuiltinIncludes.Names.ToList();
        return (mnemonics, directives, irOnly, includes);
    }

    private static string? FindRepositoryRoot(string start)
    {
        for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "purrtty.slnx"))) return dir.FullName;
        }
        return null;
    }
}
