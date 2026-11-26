using NUnit.Framework;
using purrTTY.Kfs.Testing;
using purrTTY.Kfs.Tests.Runtime;

namespace purrTTY.Kfs.Tests;

/// <summary>
/// One program run on the game's workbench computer (<c>BenchMachine</c>: no vehicle, no syscall
/// host): two getters and a setter in tick 0, the machine's rejected-request count in tick 1.
/// Shared by the bench tests with and without the optional bench hook.
/// </summary>
internal static class BenchProgram
{
    /// <summary>The program's kC source.</summary>
    public static string Source => Kc.Ticked("""
            if (k == 0u) {
                kfs_logf("bench count=%u present=%u", x_light_count(), x_light_geti(0u, X_LIGHT_PRESENT));
                x_light_set_on(0u, 1u);
            }
            if (k == 1u) {
                kfs_logf("bench rejected=%u", sys.rejected_requests);
            }
        """);

    /// <summary>Builds and runs the program for three ticks on a fresh bench of <paramref name="runtime"/>; returns the program log.</summary>
    public static IReadOnlyList<string> Run(KfsRuntime runtime)
    {
        BuildResult build = runtime.BuildKc(Source, runtime.Tables.BenchLayout);
        Assert.That(build.Ok, Is.True, build.Text);

        var bench = runtime.CreateBench();
        Assert.That(bench.LoadBench(build.Image!).Ok, Is.True);
        bench.Start();
        Assert.That(bench.Tick(3), Is.EqualTo(3));
        Assert.That(bench.HostFault, Is.Null);
        return bench.Console.Snapshot().Select(l => l.Text).ToList();
    }
}
