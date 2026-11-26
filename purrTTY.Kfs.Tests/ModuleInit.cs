// ============================================================================================
// HOW GAME ASSEMBLIES REACH THESE TESTS — AND THE RULE FOR WRITING TESTS HERE (MUST)
// ============================================================================================
// KSA.dll and the Brutal.* assemblies are referenced with Private=false and are never in this
// project's output folder. They are loaded at run time by KsaAssemblyResolver (purrTTY.Kfs.Testing),
// which the module initializer below installs before any code in this assembly runs.
//
// But NUnit reflects over this assembly (Assembly.GetTypes, test-method signatures, attribute
// arguments) BEFORE any of its code runs, so before the resolver exists. Anything that makes that
// reflection need a game type fails. Established under `dotnet test` (arm64, NUnit 3.14 /
// adapter 4.6; the same on x64 because the file is simply not in the output folder):
//
//   Game types are allowed ONLY
//     - inside method bodies (locals, calls, object creation, lambdas), and
//     - in the parameter and return types of non-test helper methods.
//   Never
//     - as the type of a field or auto-property (static or instance). A game struct or enum
//       there makes NUnit fail to load the WHOLE assembly; class-typed ones happen to work, but
//       keep game state in locals or in purrTTY.Kfs.Testing types instead;
//     - as a game struct/enum local that the compiler hoists into a field: one captured by a
//       lambda or local function, or any such local in an async method or iterator (Debug
//       builds hoist every async local) — whole assembly fails. Class-typed captures are fine;
//       move struct-handling code into a synchronous helper method;
//     - as a base type or interface (incl. generic arguments) of a type declared here — whole
//       assembly fails. Fakes that implement game interfaces belong in purrTTY.Kfs.Testing;
//     - as the element type of an iterator (`yield return` of a game type generates a nested
//       class implementing IEnumerable<GameType>) — whole assembly fails;
//     - as a [Test]/[TestCase]/[TestCaseSource] method parameter, or in an attribute argument
//       (e.g. a game enum in [TestCase]) — that test fails with "Failure building Test …
//       Could not load file or assembly 'KSA…'".
//
// A "whole assembly fails" mistake would otherwise report zero tests with exit code 0; the
// project's purrTTY.Kfs.Tests.runsettings sets TreatNoTestsAsError so the run fails instead
// (at normal verbosity: "No test is available in …purrTTY.Kfs.Tests.dll").
//
// A [SetUpFixture] is NOT an alternative to the module initializer: TestCaseSource methods run
// during discovery, before any OneTimeSetUp, so a source body touching a game type fails there.
// Fixtures that need the game derive from GameFixture, which skips silently when no game
// directory is configured and fails when one is configured but broken.
// ============================================================================================

using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using purrTTY.Kfs.Testing;

namespace purrTTY.Kfs.Tests;

/// <summary>Installs <see cref="KsaAssemblyResolver"/> before anything else in this assembly runs.</summary>
internal static class ModuleInit
{
    /// <summary>The configured game assembly directory, or null when none is configured.</summary>
    internal static string? Directory { get; private set; }

    /// <summary>Why installing the resolver failed although a directory was configured.</summary>
    internal static Exception? Failure { get; private set; }

    /// <summary>SHA-256 of the configured KSA.dll, taken before the resolver could load it.</summary>
    internal static byte[]? KsaDllHashBeforeInstall { get; private set; }

    [ModuleInitializer]
    internal static void Install()
    {
        // Never throw from here: an exception in a module initializer turns every later use of
        // this assembly into a TypeInitializationException. GameFixture reports it instead.
        try
        {
            Directory = KsaAssemblyResolver.FindDirectory(typeof(ModuleInit).Assembly);
            if (Directory is null) return;

            string ksaDll = Path.Combine(Directory, "KSA.dll");
            if (File.Exists(ksaDll)) KsaDllHashBeforeInstall = Hash(ksaDll);

            KsaAssemblyResolver.Install(Directory);
        }
        catch (Exception e)
        {
            Failure = e;
        }
    }

    internal static byte[] Hash(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return SHA256.HashData(stream);
    }
}
