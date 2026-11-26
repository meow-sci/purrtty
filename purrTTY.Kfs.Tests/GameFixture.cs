using NUnit.Framework;

namespace purrTTY.Kfs.Tests;

/// <summary>
/// Base class of every fixture that needs the game's assemblies. Its one-time set-up runs before
/// any test method of the fixture is JIT-compiled, so test bodies may use game types freely (see
/// the rule in ModuleInit.cs for everything outside bodies).
/// </summary>
public abstract class GameFixture
{
    [OneTimeSetUp]
    public void RequireGameAssemblies()
    {
        if (ModuleInit.Failure is { } failure)
        {
            // Configured (KSA_DLL_DIR or the stamped KSAFolder exists) but unusable: fail loudly.
            throw new InvalidOperationException(failure.Message, failure);
        }

        if (ModuleInit.Directory is null)
        {
            // No game directory configured at all (a contributor without the game files):
            // skip silently, per docs/build-and-test.md.
            Assert.Ignore();
        }
    }

    /// <summary>The configured game assembly directory; valid after the one-time set-up.</summary>
    protected static string GameDirectory => ModuleInit.Directory!;
}
