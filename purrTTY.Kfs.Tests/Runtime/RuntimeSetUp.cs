using System.Runtime.CompilerServices;
using NUnit.Framework;
using purrTTY.Kfs.Testing;

namespace purrTTY.Kfs.Tests.Runtime;

/// <summary>
/// Owns the process's one <see cref="KfsRuntime"/> for every fixture in this namespace: started
/// before the first of them through the real installer (<see cref="KfsExtension.Install"/>),
/// disposed after the last (extension uninstalled: header, builtin names, host hooks and registry
/// bindings removed; <c>KfsIntegration._tables</c> reset). Fixtures outside this namespace — <c>CatalogCompileTests</c>,
/// which registers the header itself and asserts the statics are clean, and the install tests
/// (<c>purrTTY.Kfs.Tests.Installer</c>), which install and uninstall the extension themselves —
/// therefore never run while the runtime is alive (the assembly is non-parallelizable; see <c>AssemblyInfo.cs</c>).
/// </summary>
/// <remarks>
/// When no game directory is configured, or it is broken, nothing is started here and each
/// fixture's <see cref="GameFixture"/> set-up skips or fails it.
/// </remarks>
[SetUpFixture]
public sealed class RuntimeSetUp
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
        Assert.That(HeaderIsClean(), Is.True, "disposing the runtime must remove <purrtty.h> and its builtins");
        Assert.That(KfsExtension.IsActive, Is.False, "disposing the runtime must uninstall the extension");
        Assert.That(TablesAreReset(), Is.True, "disposing the runtime must reset KfsIntegration._tables");
    }

    // Separate methods so the set-up methods themselves never mention a game type.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static KfsRuntime Start() => KfsRuntime.Start();

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool HeaderIsClean() => SystemHeaderScope.IsClean();

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool TablesAreReset() => KfsIntegrationTables.IsNull;
}

/// <summary>Base of the fixtures that run programs on the shared <see cref="KfsRuntime"/>.</summary>
public abstract class RuntimeFixture : GameFixture
{
    /// <summary>The process's runtime.</summary>
    protected static KfsRuntime Runtime => RuntimeSetUp.Runtime;

    /// <summary>A simulated vehicle; dispose it (<c>using</c>) to unbind it.</summary>
    protected static SimVehicle Vehicle(int lights = 0, int deployables = 0, int actuators = 0, string name = "sim") =>
        Runtime.CreateVehicle(SimVehicleSpec.With(lights, deployables, actuators, name));

    /// <summary>A simulated vehicle from a full spec.</summary>
    protected static SimVehicle Vehicle(SimVehicleSpec spec) => Runtime.CreateVehicle(spec);
}

/// <summary>kC program text for the tests: one 10 Hz task named <c>run</c>.</summary>
internal static class Kc
{
    /// <summary>The includes and pragmas every test program starts with.</summary>
    public const string Head = "#include <kfs.h>\n#include <purrtty.h>\n#pragma kfs tick_hz 10\n#pragma kfs task run rate_hz=10\n";

    /// <summary>
    /// A program whose task body sees <c>k</c>, the number of the current tick counted from 0
    /// (one tick per run at 10 Hz).
    /// </summary>
    public static string Ticked(string body, string globals = "") =>
        Head + "uint32_t n;\n" + globals + "\nvoid run(void)\n{\n    uint32_t k = n;\n    n = n + 1u;\n" + body + "\n}\n";
}
