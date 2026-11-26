using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using KSA.Kfs.Vm.Tables;
using NUnit.Framework;
using purrTTY.Kfs.Testing;

namespace purrTTY.Kfs.Tests;

/// <summary>Pins how the game's assemblies reach this process: resolved, never copied or modified.</summary>
[TestFixture]
public sealed class ResolverTests : GameFixture
{
    [Test]
    public void Resolver_Loads_Unmodified_Assemblies()
    {
        string ksaDll = Path.GetFullPath(Path.Combine(GameDirectory, "KSA.dll"));

        Assembly ksa = typeof(GameTables).Assembly;      // touches a game type

        Assembly[] loaded = AppDomain.CurrentDomain.GetAssemblies().Where(a => a.GetName().Name == "KSA").ToArray();
        Assert.That(loaded, Is.EqualTo(new[] { ksa }), "exactly one KSA assembly must be loaded");
        Assert.That(AssemblyLoadContext.GetLoadContext(ksa), Is.SameAs(AssemblyLoadContext.Default));
        Assert.That(KsaAssemblyResolver.Resolved.TryGetValue("KSA", out string? resolvedFrom), Is.True,
            "KSA must have been supplied by KsaAssemblyResolver");
        Assert.That(resolvedFrom, Is.EqualTo(ksaDll));
        Assert.That(KsaAssemblyResolver.FindDirectory(), Is.EqualTo(GameDirectory),
            "FindDirectory() must read this assembly's configuration");

        // Patched in memory (stream-loaded, no Location) only where the runtime needs it.
        bool patched = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 && ReadMachine(ksaDll) == 0x8664;
        Assert.That(ksa.Location, Is.EqualTo(patched ? string.Empty : ksaDll));

        Assert.That(ModuleInit.KsaDllHashBeforeInstall, Is.Not.Null);
        Assert.That(ModuleInit.Hash(ksaDll), Is.EqualTo(ModuleInit.KsaDllHashBeforeInstall),
            "KSA.dll on disk must be byte-identical after loading");
        Assert.That(File.Exists(Path.Combine(AppContext.BaseDirectory, "KSA.dll")), Is.False,
            "KSA.dll must not be in the test output folder");
    }

    private static ushort ReadMachine(string path)
    {
        using var reader = new BinaryReader(File.OpenRead(path));
        reader.BaseStream.Position = 0x3C;
        reader.BaseStream.Position = reader.ReadInt32() + 4;
        return reader.ReadUInt16();
    }
}

/// <summary>Resolver behaviour that needs no game files.</summary>
[TestFixture]
public sealed class ResolverConfigurationTests
{
    [Test]
    public void Install_Missing_Directory_Names_Both_Ways_To_Configure()
    {
        string missing = Path.Combine(Path.GetTempPath(), "purrtty-no-such-ksa-" + Guid.NewGuid().ToString("N"));

        var e = Assert.Throws<DirectoryNotFoundException>(() => KsaAssemblyResolver.Install(missing));

        Assert.That(e!.Message, Does.Contain(missing).And.Contain("KSA_DLL_DIR").And.Contain("KSAFolder"));
    }

    [Test]
    public void Install_Directory_Without_KsaDll_Names_Both_Ways_To_Configure()
    {
        string empty = Directory.CreateTempSubdirectory("purrtty-empty-ksa").FullName;
        try
        {
            var e = Assert.Throws<FileNotFoundException>(() => KsaAssemblyResolver.Install(empty));

            Assert.That(e!.Message, Does.Contain("KSA.dll").And.Contain("KSA_DLL_DIR").And.Contain("KSAFolder"));
        }
        finally
        {
            Directory.Delete(empty, recursive: true);
        }
    }

    [Test]
    public void No_Game_Assemblies_In_Outputs()
    {
        var offenders = new List<string>();
        offenders.AddRange(GameAssembliesIn(AppContext.BaseDirectory, recursive: false));

        // The sibling library outputs, when this runs from the repository's own build.
        string? root = FindRepositoryRoot(AppContext.BaseDirectory);
        if (root is not null)
        {
            foreach (string project in new[] { "purrTTY.Kfs", "purrTTY.Kfs.Testing" })
            {
                string bin = Path.Combine(root, project, "bin");
                if (Directory.Exists(bin)) offenders.AddRange(GameAssembliesIn(bin, recursive: true));
            }
        }

        // The packaged mod, when a dist directory was built in this environment: no game assembly,
        // the kFS extension shipped, and neither the test runtime nor any test assembly with it.
        string? dist = Environment.GetEnvironmentVariable("PURRTTY_DIST_DIR");
        string? mod = string.IsNullOrWhiteSpace(dist) ? null : Path.Combine(dist, "purrTTY");
        if (mod is not null && Directory.Exists(mod))
        {
            offenders.AddRange(GameAssembliesIn(mod, recursive: true));
            Assert.That(File.Exists(Path.Combine(mod, "purrTTY.Kfs.dll")), Is.True, "the mod ships the kFS extension");
            var testOnly = new[] { "purrTTY.Kfs.Testing.dll", "*.Tests.dll", "nunit*.dll", "Microsoft.TestPlatform*.dll" }
                .SelectMany(pattern => Directory.EnumerateFiles(mod, pattern, SearchOption.AllDirectories))
                .Select(Path.GetFileName)
                .ToList();
            Assert.That(testOnly, Is.Empty, "no test runtime or test assembly in the mod");
        }

        Assert.That(offenders, Is.Empty, "game assemblies must be resolved at run time, never copied");
    }

    private static IEnumerable<string> GameAssembliesIn(string directory, bool recursive)
    {
        var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        return new[] { "KSA.dll", "Brutal.*.dll", "Planet.*.dll" }
            .SelectMany(pattern => Directory.EnumerateFiles(directory, pattern, option));
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
