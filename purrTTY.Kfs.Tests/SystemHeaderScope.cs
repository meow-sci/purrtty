using purrTTY.Kfs.Tables;

namespace purrTTY.Kfs.Tests;

/// <summary>
/// Puts <c>&lt;purrtty.h&gt;</c> and the builtin names into the game's process-wide header statics
/// for the duration of a <c>using</c> block and restores both exactly on dispose. A thin test-side
/// name for <see cref="SystemHeaderRegistration"/>, the one mechanism the test runtime
/// (<c>KfsRuntime</c>) and the installer use as well; tests must never leave the header behind.
/// </summary>
/// <remarks>
/// Fields hold only non-game types: game types may appear in method bodies only (ModuleInit.cs).
/// The tests under <c>purrTTY.Kfs.Tests.Runtime</c> run with a <c>KfsRuntime</c> that keeps the
/// header registered for their whole namespace (see <c>Runtime/RuntimeSetUp.cs</c>); the fixtures
/// that use this scope live outside that namespace, so the two never overlap.
/// </remarks>
internal sealed class SystemHeaderScope : IDisposable
{
    private readonly SystemHeaderRegistration _registration;

    private SystemHeaderScope(SystemHeaderRegistration registration) => _registration = registration;

    /// <summary>Installs the real catalogue's header and builtins.</summary>
    public static SystemHeaderScope Install() => new(SystemHeaderRegistration.Install());

    /// <summary>Installs <paramref name="text"/> as <c>purrtty.h</c> with <paramref name="builtins"/>.</summary>
    public static SystemHeaderScope Install(string text, IEnumerable<string> builtins) =>
        new(SystemHeaderRegistration.Install(text, builtins));

    /// <summary>True when nothing of purrTTY's is in the game's header statics.</summary>
    public static bool IsClean() => SystemHeaderRegistration.IsClean();

    public void Dispose() => _registration.Dispose();
}
