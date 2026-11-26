using NUnit.Framework;
using purrTTY.Kfs.Tables;

namespace purrTTY.Kfs.Tests;

/// <summary>
/// <see cref="StockEditorProbe"/> / <see cref="EditorBuildProbe"/>: the reflected lookup of the stock
/// editor's build state. Against the real game type it resolves and answers "not building" offline;
/// against fakes with a member missing (a game update renamed it) it names the member, logs it
/// once, and answers "may be building", so the uninstall leaves <c>&lt;purrtty.h&gt;</c> registered.
/// </summary>
[TestFixture]
public sealed class StockEditorProbeTests : GameFixture
{
#pragma warning disable CS0169, CS0649, IDE0044, IDE0051 // read by reflection only
    private sealed class Editor
    {
        public bool Building { get; set; }
    }

    private sealed class Workbench
    {
        public Editor? Editor { get; set; } = new();
    }

    private static class Owner
    {
        private static Workbench? _workbench;
        internal static void Set(Workbench? w) => _workbench = w;
    }

    private sealed class WorkbenchWithoutEditor { }

    private static class OwnerOfWorkbenchWithoutEditor
    {
        private static WorkbenchWithoutEditor? _workbench;
    }

    private sealed class EditorWithoutBuilding
    {
        public int Building;   // renamed to something else, or changed type: not the bool we read
    }

    private sealed class WorkbenchOfEditorWithoutBuilding
    {
        public EditorWithoutBuilding Editor = new();
    }

    private static class OwnerOfEditorWithoutBuilding
    {
        private static WorkbenchOfEditorWithoutBuilding? _workbench;
    }
#pragma warning restore CS0169, CS0649, IDE0044, IDE0051

    [Test]
    public void The_Real_Lookup_Resolves_And_Reads_Not_Building_Offline()
    {
        var log = new List<string>();
        EditorBuildProbe probe = EditorBuildProbe.Resolve(typeof(KSA.Kfs.KfsIntegration), "_workbench", log.Add);
        Assert.That(probe.Problem, Is.Null, "KfsIntegration._workbench, Workbench.Editor and EditorModel.Building exist in this build");
        Assert.That(log, Is.Empty);
        Assert.That(probe.Read(), Is.Null, "offline there is no workbench");
        Assert.That(StockEditorProbe.Problem, Is.Null);
        Assert.That(StockEditorProbe.Building(), Is.Null);
    }

    [Test]
    public void A_Complete_Fake_Reads_Each_State()
    {
        var log = new List<string>();
        EditorBuildProbe probe = EditorBuildProbe.Resolve(typeof(Owner), "_workbench", log.Add);
        Assert.That(probe.Problem, Is.Null);
        try
        {
            Owner.Set(null);
            Assert.That(probe.Read(), Is.Null, "no workbench: nothing builds");
            var workbench = new Workbench();
            Owner.Set(workbench);
            Assert.That(probe.Read(), Is.Null, "not building");
            workbench.Editor!.Building = true;
            Assert.That(probe.Read(), Is.EqualTo(StockEditorProbe.BuildingReason));
            workbench.Editor = null;
            Assert.That(probe.Read(), Is.Null, "no editor: nothing builds");
        }
        finally
        {
            Owner.Set(null);
        }
        Assert.That(log, Is.Empty);
    }

    [TestCase(typeof(Owner), "_renamed", "Owner._renamed not found")]
    [TestCase(typeof(OwnerOfWorkbenchWithoutEditor), "_workbench", "WorkbenchWithoutEditor.Editor not found")]
    [TestCase(typeof(OwnerOfEditorWithoutBuilding), "_workbench", "EditorWithoutBuilding.Building (bool) not found")]
    public void A_Missing_Member_Is_Named_Logged_Once_And_Answers_May_Be_Building(Type owner, string field, string missing)
    {
        var log = new List<string>();
        EditorBuildProbe probe = EditorBuildProbe.Resolve(owner, field, log.Add);

        Assert.That(probe.Problem, Does.EndWith(missing));
        Assert.That(log, Has.Count.EqualTo(1));
        Assert.That(log[0], Does.Contain(missing).And.Contain("<purrtty.h> will stay registered"));
        for (int i = 0; i < 3; i++)
            Assert.That(probe.Read(), Is.EqualTo(EditorBuildProbe.UnreadableReason(probe.Problem!)), "the conservative answer, every time");
        Assert.That(log, Has.Count.EqualTo(1), "reading again logs nothing more");
    }

    [Test]
    public void The_Status_Shows_An_Unreadable_Probe()
    {
        var status = new KfsExtensionStatus { State = KfsExtensionState.Active, EditorProbe = "KSA.Kfs.KfsIntegration._workbench not found" };
        Assert.That(status.Describe(), Does.Contain(
            "  editor:   build state unreadable (KSA.Kfs.KfsIntegration._workbench not found); <purrtty.h> stays registered at uninstall\n"));
        Assert.That(new KfsExtensionStatus { State = KfsExtensionState.Active }.Describe(), Does.Not.Contain("editor:"));
    }
}
