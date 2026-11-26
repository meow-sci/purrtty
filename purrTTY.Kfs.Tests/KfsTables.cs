using KSA.Kfs.Compiler;
using KSA.Kfs.KAsm;
using KSA.Kfs.KC;
using KSA.Kfs.Vm.Tables;
using purrTTY.Kfs.Tables;

namespace purrTTY.Kfs.Tests;

/// <summary>
/// Test helpers over the game's kFS toolchain. Game types appear only in method bodies and helper
/// signatures (ModuleInit.cs); callers must run inside a <see cref="GameFixture"/>.
/// </summary>
internal static class KfsTables
{
    /// <summary>Tables from the untouched stock streams (never the game's shared instance).</summary>
    public static GameTables Stock() => GameTables.Load(TableAugmenter.OpenStock, "test:stock");

    /// <summary>Tables with purrTTY's catalogue merged in.</summary>
    public static GameTables Augmented() => TableAugmenter.Build(TableAugmenter.OpenStock);

    /// <summary>Compiles a kC project (file name → text) for the bench part.</summary>
    public static CompileResult CompileKc(GameTables tables, IoLayout layout, IReadOnlyDictionary<string, string> files,
        bool emitDebug = true)
    {
        KcFrontEnd.Register();
        return Compiler.Compile(new CompileInput
        {
            Files = files,
            Part = tables.BenchPart,
            Game = tables,
            Layout = layout,
            Options = new CompileOptions { EmitDebug = emitDebug },
        });
    }

    /// <summary>Assembles a kASM project (file name → text) for the bench part, as the game's Workbench does.</summary>
    public static AssembleResult Assemble(GameTables tables, IoLayout layout, IReadOnlyDictionary<string, string> files) =>
        new Assembler(tables, tables.BenchPart, layout)
        {
            ProjectFiles = name => files.TryGetValue(name, out var text) ? text : null,
        }.Assemble(files.Where(f => f.Key.EndsWith(".kasm", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f.Key, StringComparer.Ordinal)
            .Select(f => new SourceFile(f.Key, f.Value)).ToList());

    /// <summary>Diagnostics as text, for assertion messages.</summary>
    public static string Text(IEnumerable<Diagnostic> diagnostics) => string.Join("\n", diagnostics.Select(d => d.ToString()));

    /// <summary>True when <paramref name="diagnostics"/> holds an error.</summary>
    public static bool HasErrors(IEnumerable<Diagnostic> diagnostics) => diagnostics.Any(d => d.Severity == Severity.Error);
}
