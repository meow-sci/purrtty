using HarmonyLib;
using NUnit.Framework;
using purrTTY.Kfs.Runtime;
using purrTTY.Kfs.Tables;
using purrTTY.Kfs.Testing;

namespace purrTTY.Kfs.Tests.Installer;

/// <summary>
/// Finding B2 at uninstall: the game's <c>SystemHeaders</c> are a plain <c>Dictionary</c> and
/// <c>HashSet</c> that compiles read off the main thread, so the uninstall leaves
/// <c>&lt;purrtty.h&gt;</c> registered (and says so) while one may be reading — a language-server
/// compile still running, or the stock editor's background build (through the game services) —
/// and the next install takes the registration over, so it is never left for good.
/// </summary>
[TestFixture]
public sealed class HeaderInUseTests : GameFixture
{
    private Harmony _harmony = null!;
    private readonly List<string> _log = [];

    /// <summary>Game services whose only behaviour is the answer to <see cref="IKfsGameServices.SystemHeadersInUse"/>.</summary>
    private sealed class BuildingServices : IKfsGameServices
    {
        public string? Building { get; set; }
        public string Name => "test services";
        public string? Prepare() => null;
        public void Attach() { }
        public void Detach() { }
        public string? SystemHeadersInUse() => Building;
    }

    [SetUp]
    public void StartClean()
    {
        Assert.That(KfsExtension.IsActive, Is.False);
        Assert.That(KfsRuntime.Current, Is.Null);
        KfsIntegrationTables.Reset();
        _harmony = new Harmony("purrtty.kfs.tests.header." + Guid.NewGuid().ToString("N"));
        _log.Clear();
    }

    [TearDown]
    public void LeaveClean()
    {
        KfsExtension.Uninstall();
        SystemHeaderRegistration.RestoreLeftover();
        KfsIntegrationTables.Reset();
        Assert.That(SystemHeaderRegistration.IsClean(), Is.True, "<purrtty.h> or a builtin name was left in SystemHeaders");
    }

    private KfsExtensionStatus Install(IKfsGameServices? services) =>
        KfsExtension.Install(_harmony, null, InstallSeams.Default with { Log = _log.Add }, services);

    [Test]
    public void The_Header_Stays_Registered_While_The_Stock_Editor_Builds_And_The_Next_Install_Takes_It_Over()
    {
        var services = new BuildingServices { Building = "the stock kFS editor is building in the background" };
        Assert.That(Install(services).IsActive, Is.True);
        KfsExtension.Uninstall();
        Assert.That(SystemHeaderRegistration.IsClean(), Is.False, "left registered: the game's build may be reading the table");
        Assert.That(KfsExtension.HeaderLeftRegistered, Is.EqualTo("the stock kFS editor is building in the background"));

        // A reload: the next install adopts it, and its uninstall (nothing building now) restores exactly.
        services.Building = null;
        Assert.That(Install(services).IsActive, Is.True);
        Assert.That(KfsExtension.HeaderLeftRegistered, Is.Null);
        KfsExtension.Uninstall();
        Assert.That(SystemHeaderRegistration.IsClean(), Is.True, "the original state is back, as if never left");
    }

    [Test]
    public void The_Header_Stays_Registered_While_A_Language_Server_Compile_Runs()
    {
        Assert.That(Install(null).IsActive, Is.True);
        using (SystemHeaderReaders.EnterCompile())
        {
            KfsExtension.Uninstall();
            Assert.That(SystemHeaderRegistration.IsClean(), Is.False);
            Assert.That(KfsExtension.HeaderLeftRegistered, Is.EqualTo("1 language-server compile is still running"));
        }
        Assert.That(SystemHeaderRegistration.RestoreLeftover(), Is.True);
        Assert.That(SystemHeaderRegistration.IsClean(), Is.True);

        // Nothing reading: removed at once, as before.
        Assert.That(Install(new BuildingServices()).IsActive, Is.True);
        KfsExtension.Uninstall();
        Assert.That(SystemHeaderRegistration.IsClean(), Is.True);
        Assert.That(KfsExtension.HeaderLeftRegistered, Is.Null);
    }

    [Test]
    public void The_Real_Probe_Never_Creates_The_Workbench()
    {
        // The default member reads KfsIntegration._workbench; offline there is none, so: not building.
        IKfsGameServices services = new BuildingServicesDefault();
        Assert.That(services.SystemHeadersInUse(), Is.Null);
        Assert.That(WorkbenchExists(), Is.False, "reading the state did not create the workbench");
    }

    private sealed class BuildingServicesDefault : IKfsGameServices
    {
        public string Name => "default probe";
        public string? Prepare() => null;
        public void Attach() { }
        public void Detach() { }
    }

    private static bool WorkbenchExists() =>
        typeof(KSA.Kfs.KfsIntegration).GetField("_workbench", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetValue(null) is not null;
}
