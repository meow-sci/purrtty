using KSA.Kfs.Compiler;
using KSA.Kfs.Host;
using KSA.Kfs.KC;
using KSA.Kfs.Vm.Tables;
using NUnit.Framework;

namespace purrTTY.Kfs.Tests;

/// <summary>
/// The game's stock kFS — tables, kC compiler, VM and host — running in this process with no game.
/// These pin the baseline later tasks extend; they use stock tables built with
/// <see cref="GameTables.Load(Func{string, Stream?}, string)"/>, never the game's shared
/// <c>KfsIntegration.Tables</c> (which the extension augments).
/// </summary>
[TestFixture]
public sealed class StockToolchainTests : GameFixture
{
    [Test]
    public void Stock_Tables_Load_Offline()
    {
        GameTables tables = LoadStockTables();

        ushort[] expected = [1, 2, 3, 16, 17, 32, 48, 49, 50, 51, 52, 53, 54, 55, 56, 57, 64, 65, 66, 67];
        Assert.That(tables.Services.Keys.Order(), Is.EqualTo(expected), "stock wired services");
        Assert.That(tables.ServiceTableVersion, Is.EqualTo(1));
        Assert.That(tables.MessageCatalogueVersion, Is.EqualTo(4));
        Assert.That(tables.Services.Keys.Any(KfsBuildInfo.Owns), Is.False, "the stock game must not use purrTTY's id block");
    }

    [Test]
    public void Kfs_Toolchain_Compiles_And_Runs_Offline()
    {
        GameTables tables = LoadStockTables();
        KcFrontEnd.Register();
        var fake = new FakeVehicle(tables, thrusters: 4, name: "kfs-smoke");

        const string source = """
            #include <kfs.h>
            #pragma kfs tick_hz 10
            #pragma kfs task run rate_hz=10
            uint32_t n;
            void run(void) {
                n++;
                if (n == 2u) kfs_logf("smoke tick %u", n);
            }
            """;
        CompileResult compiled = Compiler.Compile(new CompileInput
        {
            Files = new Dictionary<string, string> { ["smoke.c"] = source },
            Part = tables.BenchPart,
            Game = tables,
            Layout = fake.Layout,
        });
        Assert.That(compiled.Ok, Is.True, () => "compile failed:\n" + string.Join("\n", compiled.Diagnostics));

        var load = fake.Computer.Load(compiled.Image!, fake.Layout);
        Assert.That(load.Ok, Is.True, () => "load rejected: " + load);
        fake.Computer.Start(0L);

        // Simulated time only: ten frames of 0.1 s, one 10 Hz tick each.
        for (int frame = 0; frame < 10; frame++)
        {
            fake.Dispatch(frame * 100_000_000L, default(TruthSample));
        }

        var log = fake.Computer.Console.Snapshot().Select(l => l.Text).ToList();
        Assert.That(log, Has.Some.Contains("smoke tick 2"), () => "program log:\n" + string.Join("\n", log));
    }

    private static GameTables LoadStockTables()
    {
        var ksa = typeof(GameTables).Assembly;
        return GameTables.Load(file => ksa.GetManifestResourceStream(GameTables.RESOURCE_PREFIX + file), "test:stock");
    }
}
