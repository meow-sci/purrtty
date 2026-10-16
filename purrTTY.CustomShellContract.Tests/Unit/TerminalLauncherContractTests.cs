using NUnit.Framework;

namespace purrTTY.Core.Terminal.Tests.Unit;

/// <summary>
/// Pins the default values of the launcher request POCOs: consumers rely on these
/// (they are part of the published ABI), and the object-initializer shape must stay stable.
/// </summary>
[TestFixture]
public class TerminalLauncherContractTests
{
    [Test]
    public void TerminalPlacementKind_ValuesAreStable()
    {
        Assert.That((int)TerminalPlacementKind.Window, Is.EqualTo(0));
        Assert.That((int)TerminalPlacementKind.Tab, Is.EqualTo(1));
        Assert.That((int)TerminalPlacementKind.InWorldPart, Is.EqualTo(2));
        Assert.That((int)TerminalPlacementKind.InWorldBillboard, Is.EqualTo(3));
    }

    [Test]
    public void TerminalLifecycleEvent_ValuesAreStable()
    {
        Assert.That((int)TerminalLifecycleEvent.Created, Is.EqualTo(0));
        Assert.That((int)TerminalLifecycleEvent.Started, Is.EqualTo(1));
        Assert.That((int)TerminalLifecycleEvent.Focused, Is.EqualTo(2));
        Assert.That((int)TerminalLifecycleEvent.Unfocused, Is.EqualTo(3));
        Assert.That((int)TerminalLifecycleEvent.Resized, Is.EqualTo(4));
        Assert.That((int)TerminalLifecycleEvent.Exited, Is.EqualTo(5));
        Assert.That((int)TerminalLifecycleEvent.Closed, Is.EqualTo(6));
    }

    [Test]
    public void ProgramSpec_Defaults()
    {
        var spec = new TerminalProgramSpec();

        Assert.That(spec.CustomShellId, Is.Null);
        Assert.That(spec.Program, Is.Null);
        Assert.That(spec.Arguments, Is.Not.Null.And.Empty);
        Assert.That(spec.WorkingDirectory, Is.Null);
        Assert.That(spec.Environment, Is.Not.Null.And.Empty);
        Assert.That(spec.StartupCommand, Is.Null);
        Assert.That(spec.Configuration, Is.Not.Null.And.Empty);
    }

    [Test]
    public void ProgramSpec_CollectionsAreIndependentPerInstance()
    {
        var a = new TerminalProgramSpec();
        var b = new TerminalProgramSpec();

        a.Arguments.Add("x");
        a.Environment["K"] = "V";
        a.Configuration["c"] = 1;

        Assert.That(b.Arguments, Is.Empty);
        Assert.That(b.Environment, Is.Empty);
        Assert.That(b.Configuration, Is.Empty);
    }

    [Test]
    public void WindowOptions_Defaults()
    {
        var o = new TerminalWindowOptions();

        Assert.That(o.Name, Is.Null);
        Assert.That(o.Title, Is.Null);
        Assert.That(o.ThemeName, Is.Null);
        Assert.That(o.PosX, Is.Null);
        Assert.That(o.PosY, Is.Null);
        Assert.That(o.Width, Is.Null);
        Assert.That(o.Height, Is.Null);
        Assert.That(o.Cols, Is.Null);
        Assert.That(o.Rows, Is.Null);
        Assert.That(o.LockMode, Is.Null);
        Assert.That(o.HideChrome, Is.Null);
        Assert.That(o.NoResize, Is.Null);
        Assert.That(o.Focus, Is.True);
        Assert.That(o.ShowTerminal, Is.True);
    }

    [Test]
    public void InWorldOptions_Defaults()
    {
        var o = new TerminalInWorldOptions();

        Assert.That(o.Name, Is.Null);
        Assert.That(o.ThemeName, Is.Null);
        Assert.That(o.Cols, Is.EqualTo(40));
        Assert.That(o.Rows, Is.EqualTo(12));
        Assert.That(o.VehicleId, Is.EqualTo(""));
        Assert.That(o.PartId, Is.EqualTo(""));
        Assert.That(o.PartInstanceId, Is.Null);
        Assert.That(o.SubPartId, Is.EqualTo(""));
        Assert.That(o.OffsetX, Is.EqualTo(0f));
        Assert.That(o.OffsetY, Is.EqualTo(0f));
        Assert.That(o.OffsetZ, Is.EqualTo(0.05f));
        Assert.That(o.RotationX, Is.EqualTo(0f));
        Assert.That(o.RotationY, Is.EqualTo(0f));
        Assert.That(o.RotationZ, Is.EqualTo(0f));
        Assert.That(o.WidthMeters, Is.EqualTo(0.6f));
        Assert.That(o.HeightMeters, Is.EqualTo(0.2f));
        Assert.That(o.BackgroundOpacity, Is.Null);
        Assert.That(o.ForegroundOpacity, Is.Null);
        Assert.That(o.CellBackgroundOpacity, Is.Null);
        Assert.That(o.Billboard, Is.False);
        Assert.That(o.BillboardDistance, Is.EqualTo(5f));
        Assert.That(o.BillboardAlwaysOnTop, Is.True);
        Assert.That(o.BillboardClickToFocus, Is.True);
        Assert.That(o.Focus, Is.False);
    }

    [Test]
    public void LaunchRequest_Defaults()
    {
        var request = new TerminalLaunchRequest();

        Assert.That(request.Placement, Is.EqualTo(TerminalPlacementKind.Window));
        Assert.That(request.Program, Is.Not.Null);
        Assert.That(request.Window, Is.Null);
        Assert.That(request.InWorld, Is.Null);
    }

    [Test]
    public void LaunchRequest_SupportsObjectInitializerSyntax()
    {
        var request = new TerminalLaunchRequest
        {
            Placement = TerminalPlacementKind.InWorldPart,
            Program = new TerminalProgramSpec
            {
                CustomShellId = "ksa-script",
                Environment = { ["KSA_SCRIPT"] = "door.lua" },
            },
            InWorld = new TerminalInWorldOptions { Cols = 40, Rows = 12, PartInstanceId = 77u },
        };

        Assert.That(request.Program.Environment["KSA_SCRIPT"], Is.EqualTo("door.lua"));
        Assert.That(request.InWorld!.PartInstanceId, Is.EqualTo(77u));
    }
}
