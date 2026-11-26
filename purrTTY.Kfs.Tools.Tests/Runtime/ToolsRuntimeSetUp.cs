using System.Runtime.CompilerServices;
using NUnit.Framework;
using purrTTY.Kfs.Testing;

namespace purrTTY.Kfs.Tools.Tests.Runtime;

/// <summary>
/// Owns the process's one <see cref="KfsRuntime"/> for every fixture in this namespace: started
/// before the first of them through the real installer, disposed after the last. The CLI's commands
/// find it through <see cref="KfsRuntime.Current"/> and leave it running. Fixtures outside this
/// namespace never run while it is alive (the assembly is non-parallelizable).
/// </summary>
/// <remarks>
/// When no game directory is configured, or it is broken, nothing is started here and each
/// fixture's <see cref="GameFixture"/> set-up skips or fails it.
/// </remarks>
[SetUpFixture]
public sealed class ToolsRuntimeSetUp
{
    private static KfsRuntime? _runtime;

    /// <summary>The runtime; valid inside fixtures of this namespace after their <see cref="GameFixture"/> set-up.</summary>
    internal static KfsRuntime Runtime =>
        _runtime ?? throw new InvalidOperationException("the kFS test runtime was not started (no usable game directory)");

    [OneTimeSetUp]
    public void StartRuntime()
    {
        if (ModuleInit.Failure is not null || ModuleInit.Directory is null) return;
        _runtime = Start();
    }

    [OneTimeTearDown]
    public void StopRuntime()
    {
        if (_runtime is null) return;
        _runtime.Dispose();
        _runtime = null;
        Assert.That(ExtensionIsActive(), Is.False, "disposing the runtime must uninstall the extension");
        Assert.That(TablesAreReset(), Is.True, "disposing the runtime must reset KfsIntegration._tables");
    }

    // Separate methods so the set-up methods themselves never mention a game type.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static KfsRuntime Start() => KfsRuntime.Start();

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool ExtensionIsActive() => KfsExtension.IsActive;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool TablesAreReset() => KfsIntegrationTables.IsNull;
}

/// <summary>Base of the fixtures that need the shared <see cref="KfsRuntime"/>.</summary>
public abstract class ToolsRuntimeFixture : GameFixture
{
    /// <summary>The process's runtime.</summary>
    protected static KfsRuntime Runtime => ToolsRuntimeSetUp.Runtime;
}
