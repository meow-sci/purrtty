using NUnit.Framework;
using purrTTY.Core.Terminal;
using purrTTY.Display.Launcher;

namespace purrTTY.Display.Tests;

/// <summary>
/// Pins <see cref="TerminalLaunchMapper"/> — the pure request-to-purrTTY mapping behind the
/// cross-mod terminal launcher: program spec to launch options, in-world option resolution and
/// clamping, window pixel sizing, and the window layout record's geometry precedence.
/// </summary>
[TestFixture]
public sealed class TerminalLaunchMapperTests
{
    private static ProcessLaunchOptions Default() => ProcessLaunchOptions.CreateDefault();

    // ---- ToLaunchOptions ----

    [Test]
    public void ToLaunchOptions_NullSpec_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => TerminalLaunchMapper.ToLaunchOptions(null!, Default));
    }

    [Test]
    public void ToLaunchOptions_NullFactory_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => TerminalLaunchMapper.ToLaunchOptions(new TerminalProgramSpec(), null!));
    }

    [Test]
    public void ToLaunchOptions_CustomShellId_SelectsCustomGameShell()
    {
        bool factoryCalled = false;
        var spec = new TerminalProgramSpec { CustomShellId = "ksa-script" };

        var options = TerminalLaunchMapper.ToLaunchOptions(spec, () => { factoryCalled = true; return Default(); });

        Assert.That(options.ShellType, Is.EqualTo(ShellType.CustomGame));
        Assert.That(options.CustomShellId, Is.EqualTo("ksa-script"));
        Assert.That(factoryCalled, Is.False);
    }

    [Test]
    public void ToLaunchOptions_CustomShellId_WinsOverProgram()
    {
        var spec = new TerminalProgramSpec { CustomShellId = "ksa-script", Program = "/usr/bin/nvim" };

        var options = TerminalLaunchMapper.ToLaunchOptions(spec, Default);

        Assert.That(options.ShellType, Is.EqualTo(ShellType.CustomGame));
        Assert.That(options.CustomShellPath, Is.Null);
    }

    [TestCase("")]
    [TestCase("   ")]
    [TestCase(null)]
    public void ToLaunchOptions_BlankCustomShellId_IsTreatedAsUnset(string? blank)
    {
        var spec = new TerminalProgramSpec { CustomShellId = blank, Program = "/usr/bin/nvim" };

        var options = TerminalLaunchMapper.ToLaunchOptions(spec, Default);

        Assert.That(options.ShellType, Is.EqualTo(ShellType.Custom));
        Assert.That(options.CustomShellPath, Is.EqualTo("/usr/bin/nvim"));
    }

    [Test]
    public void ToLaunchOptions_BlankIdAndNoProgram_FallsBackToTheDefaultFactory()
    {
        int calls = 0;
        var spec = new TerminalProgramSpec { CustomShellId = "  " };

        var options = TerminalLaunchMapper.ToLaunchOptions(spec, () => { calls++; return Default(); });

        Assert.That(calls, Is.EqualTo(1));
        Assert.That(options.CustomShellId, Is.Null);
    }

    [Test]
    public void ToLaunchOptions_Program_SelectsCustomProcessWithArguments()
    {
        var spec = new TerminalProgramSpec
        {
            Program = "/opt/nvim/bin/nvim",
            Arguments = { "-u", "NONE", "notes.txt" },
        };

        var options = TerminalLaunchMapper.ToLaunchOptions(spec, () => throw new InvalidOperationException("factory must not run"));

        Assert.That(options.ShellType, Is.EqualTo(ShellType.Custom));
        Assert.That(options.CustomShellPath, Is.EqualTo("/opt/nvim/bin/nvim"));
        Assert.That(options.Arguments, Is.EqualTo(new[] { "-u", "NONE", "notes.txt" }));
    }

    [Test]
    public void ToLaunchOptions_BothNull_UsesTheDefaultFactoryExactlyOnce()
    {
        int calls = 0;
        var fromFactory = ProcessLaunchOptions.CreateWsl("Ubuntu");

        var options = TerminalLaunchMapper.ToLaunchOptions(new TerminalProgramSpec(), () => { calls++; return fromFactory; });

        Assert.That(calls, Is.EqualTo(1));
        Assert.That(options, Is.SameAs(fromFactory));
        Assert.That(options.ShellType, Is.EqualTo(ShellType.Wsl));
        Assert.That(options.Arguments, Is.EqualTo(new[] { "--distribution", "Ubuntu" }), "no spec arguments: the default's own arguments stay");
    }

    [Test]
    public void ToLaunchOptions_Arguments_AreAppendedForCustomShellAndDefaultShell()
    {
        var customShell = TerminalLaunchMapper.ToLaunchOptions(
            new TerminalProgramSpec { CustomShellId = "s", Arguments = { "--a" } }, Default);
        var defaultShell = TerminalLaunchMapper.ToLaunchOptions(
            new TerminalProgramSpec { Arguments = { "--b" } }, () => ProcessLaunchOptions.CreateWsl("Ubuntu"));

        Assert.That(customShell.Arguments, Is.EqualTo(new[] { "--a" }));
        Assert.That(defaultShell.Arguments, Is.EqualTo(new[] { "--distribution", "Ubuntu", "--b" }));
    }

    [Test]
    public void ToLaunchOptions_Environment_IsMergedOverDefaults_SpecWins()
    {
        var spec = new TerminalProgramSpec
        {
            CustomShellId = "ksa-script",
            Environment = { ["KSA_SCRIPT"] = "door.lua", ["TERM"] = "vt100" },
        };

        var options = TerminalLaunchMapper.ToLaunchOptions(spec, Default);

        Assert.That(options.EnvironmentVariables["KSA_SCRIPT"], Is.EqualTo("door.lua"));
        Assert.That(options.EnvironmentVariables["TERM"], Is.EqualTo("vt100"), "a spec entry overwrites the platform default");
        Assert.That(options.EnvironmentVariables["COLORTERM"], Is.EqualTo("truecolor"), "untouched defaults survive the merge");
    }

    [Test]
    public void ToLaunchOptions_WorkingDirectory_IsCopiedWhenSet_AndDefaultKeptWhenNull()
    {
        var withDir = TerminalLaunchMapper.ToLaunchOptions(
            new TerminalProgramSpec { Program = "p", WorkingDirectory = "/work" }, Default);
        var withoutDir = TerminalLaunchMapper.ToLaunchOptions(new TerminalProgramSpec { Program = "p" }, Default);

        Assert.That(withDir.WorkingDirectory, Is.EqualTo("/work"));
        Assert.That(withoutDir.WorkingDirectory, Is.EqualTo(ProcessLaunchOptions.CreateDefault().WorkingDirectory));
    }

    [Test]
    public void ToLaunchOptions_StartupCommand_IsCopied()
    {
        var options = TerminalLaunchMapper.ToLaunchOptions(
            new TerminalProgramSpec { Program = "p", StartupCommand = "htop" }, Default);
        var none = TerminalLaunchMapper.ToLaunchOptions(new TerminalProgramSpec { Program = "p" }, Default);

        Assert.That(options.StartupCommand, Is.EqualTo("htop"));
        Assert.That(none.StartupCommand, Is.Null);
    }

    [Test]
    public void ToLaunchOptions_Configuration_IsCopiedIntoCustomShellConfiguration()
    {
        var payload = new object();
        var spec = new TerminalProgramSpec
        {
            CustomShellId = "s",
            Configuration = { ["mode"] = "kiosk", ["payload"] = payload },
        };

        var options = TerminalLaunchMapper.ToLaunchOptions(spec, Default);

        Assert.That(options.CustomShellConfiguration, Has.Count.EqualTo(2));
        Assert.That(options.CustomShellConfiguration["mode"], Is.EqualTo("kiosk"));
        Assert.That(options.CustomShellConfiguration["payload"], Is.SameAs(payload));
        Assert.That(options.CustomShellConfiguration, Is.Not.SameAs(spec.Configuration));
    }

    [Test]
    public void ToLaunchOptions_DoesNotAliasTheSpecCollections()
    {
        var spec = new TerminalProgramSpec { Program = "p", Arguments = { "a" }, Environment = { ["K"] = "V" } };

        var options = TerminalLaunchMapper.ToLaunchOptions(spec, Default);
        spec.Arguments.Add("later");
        spec.Environment["K"] = "changed";

        Assert.That(options.Arguments, Is.EqualTo(new[] { "a" }));
        Assert.That(options.EnvironmentVariables["K"], Is.EqualTo("V"));
    }

    // ---- ToInWorldPlacement ----

    [Test]
    public void ToInWorldPlacement_NullOptions_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => TerminalLaunchMapper.ToInWorldPlacement(null!, TerminalPlacementKind.InWorldPart, "x"));
    }

    [Test]
    public void ToInWorldPlacement_CarriesEveryFieldThrough()
    {
        var options = new TerminalInWorldOptions
        {
            Name = "Door Panel",
            ThemeName = "Solarized Dark",
            Cols = 60,
            Rows = 20,
            VehicleId = "Vessel-1",
            PartId = "CommandPod",
            PartInstanceId = 4242u,
            SubPartId = "Hatch",
            OffsetX = 0.1f,
            OffsetY = -0.2f,
            OffsetZ = 0.3f,
            RotationX = 10f,
            RotationY = 20f,
            RotationZ = 30f,
            WidthMeters = 1.5f,
            HeightMeters = 0.75f,
            BackgroundOpacity = 0.5f,
            ForegroundOpacity = 0.6f,
            CellBackgroundOpacity = 0.7f,
            BillboardDistance = 9f,
            BillboardAlwaysOnTop = false,
            BillboardClickToFocus = false,
            Focus = true,
        };

        var p = TerminalLaunchMapper.ToInWorldPlacement(options, TerminalPlacementKind.InWorldPart, "fallback");

        Assert.That(p.Name, Is.EqualTo("Door Panel"));
        Assert.That(p.ThemeName, Is.EqualTo("Solarized Dark"));
        Assert.That(p.Cols, Is.EqualTo(60));
        Assert.That(p.Rows, Is.EqualTo(20));
        Assert.That(p.Mode, Is.EqualTo(InWorldPlacement.ModePart));
        Assert.That(p.IsBillboard, Is.False);
        Assert.That(p.VehicleId, Is.EqualTo("Vessel-1"));
        Assert.That(p.PartId, Is.EqualTo("CommandPod"));
        Assert.That(p.PartInstanceId, Is.EqualTo(4242u));
        Assert.That(p.SubPartId, Is.EqualTo("Hatch"));
        Assert.That(p.OffsetX, Is.EqualTo(0.1f));
        Assert.That(p.OffsetY, Is.EqualTo(-0.2f));
        Assert.That(p.OffsetZ, Is.EqualTo(0.3f));
        Assert.That(p.RotationX, Is.EqualTo(10f));
        Assert.That(p.RotationY, Is.EqualTo(20f));
        Assert.That(p.RotationZ, Is.EqualTo(30f));
        Assert.That(p.WidthMeters, Is.EqualTo(1.5f));
        Assert.That(p.HeightMeters, Is.EqualTo(0.75f));
        Assert.That(p.BackgroundOpacity, Is.EqualTo(0.5f));
        Assert.That(p.ForegroundOpacity, Is.EqualTo(0.6f));
        Assert.That(p.CellBackgroundOpacity, Is.EqualTo(0.7f));
        Assert.That(p.HasOpacityOverride, Is.True);
        Assert.That(p.BillboardDistance, Is.EqualTo(9f));
        Assert.That(p.BillboardAlwaysOnTop, Is.False);
        Assert.That(p.BillboardClickToFocus, Is.False);
        Assert.That(p.Focus, Is.True);
    }

    [Test]
    public void ToInWorldPlacement_DefaultOptions_ProduceContractDefaults()
    {
        var p = TerminalLaunchMapper.ToInWorldPlacement(new TerminalInWorldOptions(), TerminalPlacementKind.InWorldPart, "In-World");

        Assert.That(p.Cols, Is.EqualTo(40));
        Assert.That(p.Rows, Is.EqualTo(12));
        Assert.That(p.OffsetZ, Is.EqualTo(0.05f));
        Assert.That(p.WidthMeters, Is.EqualTo(0.6f));
        Assert.That(p.HeightMeters, Is.EqualTo(0.2f));
        Assert.That(p.PartInstanceId, Is.Null);
        Assert.That(p.BackgroundOpacity, Is.Null);
        Assert.That(p.HasOpacityOverride, Is.False);
        Assert.That(p.BillboardDistance, Is.EqualTo(5f));
        Assert.That(p.BillboardAlwaysOnTop, Is.True);
        Assert.That(p.BillboardClickToFocus, Is.True);
        Assert.That(p.Focus, Is.False);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void ToInWorldPlacement_BlankName_UsesTheFallback(string? name)
    {
        var p = TerminalLaunchMapper.ToInWorldPlacement(
            new TerminalInWorldOptions { Name = name }, TerminalPlacementKind.InWorldPart, "Script Terminal");

        Assert.That(p.Name, Is.EqualTo("Script Terminal"));
    }

    [Test]
    public void ToInWorldPlacement_Name_IsTrimmed()
    {
        var p = TerminalLaunchMapper.ToInWorldPlacement(
            new TerminalInWorldOptions { Name = "  Door  " }, TerminalPlacementKind.InWorldPart, "x");

        Assert.That(p.Name, Is.EqualTo("Door"));
    }

    [Test]
    public void ToInWorldPlacement_BillboardKind_SelectsBillboardMode()
    {
        var p = TerminalLaunchMapper.ToInWorldPlacement(
            new TerminalInWorldOptions(), TerminalPlacementKind.InWorldBillboard, "x");

        Assert.That(p.Mode, Is.EqualTo(InWorldPlacement.ModeBillboard));
        Assert.That(p.IsBillboard, Is.True);
    }

    [Test]
    public void ToInWorldPlacement_BillboardFlag_SelectsBillboardModeEvenForPartKind()
    {
        var p = TerminalLaunchMapper.ToInWorldPlacement(
            new TerminalInWorldOptions { Billboard = true }, TerminalPlacementKind.InWorldPart, "x");

        Assert.That(p.Mode, Is.EqualTo(InWorldPlacement.ModeBillboard));
    }

    [Test]
    public void ToInWorldPlacement_ColsAndRows_AreClampedToTheInWorldRange()
    {
        var tooSmall = TerminalLaunchMapper.ToInWorldPlacement(
            new TerminalInWorldOptions { Cols = -5, Rows = 0 }, TerminalPlacementKind.InWorldPart, "x");
        var tooBig = TerminalLaunchMapper.ToInWorldPlacement(
            new TerminalInWorldOptions { Cols = 10_000, Rows = 10_000 }, TerminalPlacementKind.InWorldPart, "x");

        Assert.That(tooSmall.Cols, Is.EqualTo(TerminalLaunchMapper.MinCols));
        Assert.That(tooSmall.Rows, Is.EqualTo(TerminalLaunchMapper.MinRows));
        Assert.That(tooBig.Cols, Is.EqualTo(TerminalLaunchMapper.MaxCols));
        Assert.That(tooBig.Rows, Is.EqualTo(TerminalLaunchMapper.MaxRows));
        Assert.That((TerminalLaunchMapper.MinCols, TerminalLaunchMapper.MaxCols), Is.EqualTo((8, 400)));
        Assert.That((TerminalLaunchMapper.MinRows, TerminalLaunchMapper.MaxRows), Is.EqualTo((4, 200)));
    }

    [Test]
    public void ToInWorldPlacement_Sizes_AreClampedPositive()
    {
        var tooSmall = TerminalLaunchMapper.ToInWorldPlacement(
            new TerminalInWorldOptions { WidthMeters = 0f, HeightMeters = -3f }, TerminalPlacementKind.InWorldPart, "x");
        var tooBig = TerminalLaunchMapper.ToInWorldPlacement(
            new TerminalInWorldOptions { WidthMeters = 5000f, HeightMeters = 5000f }, TerminalPlacementKind.InWorldPart, "x");

        Assert.That(tooSmall.WidthMeters, Is.EqualTo(TerminalLaunchMapper.MinMeters));
        Assert.That(tooSmall.HeightMeters, Is.EqualTo(TerminalLaunchMapper.MinMeters));
        Assert.That(tooBig.WidthMeters, Is.EqualTo(TerminalLaunchMapper.MaxMeters));
        Assert.That(tooBig.HeightMeters, Is.EqualTo(TerminalLaunchMapper.MaxMeters));
    }

    [Test]
    public void ToInWorldPlacement_BillboardDistance_IsClamped()
    {
        var near = TerminalLaunchMapper.ToInWorldPlacement(
            new TerminalInWorldOptions { BillboardDistance = 0f }, TerminalPlacementKind.InWorldBillboard, "x");
        var far = TerminalLaunchMapper.ToInWorldPlacement(
            new TerminalInWorldOptions { BillboardDistance = 1e6f }, TerminalPlacementKind.InWorldBillboard, "x");

        Assert.That(near.BillboardDistance, Is.EqualTo(TerminalLaunchMapper.MinBillboardDistance));
        Assert.That(far.BillboardDistance, Is.EqualTo(TerminalLaunchMapper.MaxBillboardDistance));
    }

    [Test]
    public void ToInWorldPlacement_Opacities_AreClampedToUnitRange_WhenPresent()
    {
        var p = TerminalLaunchMapper.ToInWorldPlacement(
            new TerminalInWorldOptions { BackgroundOpacity = 1.5f, ForegroundOpacity = -0.5f, CellBackgroundOpacity = 0.25f },
            TerminalPlacementKind.InWorldPart, "x");

        Assert.That(p.BackgroundOpacity, Is.EqualTo(1f));
        Assert.That(p.ForegroundOpacity, Is.EqualTo(0f));
        Assert.That(p.CellBackgroundOpacity, Is.EqualTo(0.25f));
    }

    [Test]
    public void ToInWorldPlacement_NonFiniteNumbers_FallBackToSafeValues()
    {
        var p = TerminalLaunchMapper.ToInWorldPlacement(
            new TerminalInWorldOptions
            {
                OffsetX = float.NaN,
                RotationZ = float.PositiveInfinity,
                WidthMeters = float.NaN,
                HeightMeters = float.NegativeInfinity,
                BillboardDistance = float.NaN,
                BackgroundOpacity = float.NaN,
            },
            TerminalPlacementKind.InWorldPart, "x");

        Assert.That(p.OffsetX, Is.EqualTo(0f));
        Assert.That(p.RotationZ, Is.EqualTo(0f));
        Assert.That(p.WidthMeters, Is.EqualTo(0.6f));
        Assert.That(p.HeightMeters, Is.EqualTo(0.2f));
        Assert.That(p.BillboardDistance, Is.EqualTo(5f));
        Assert.That(p.BackgroundOpacity, Is.Null);
    }

    [Test]
    public void ToInWorldPlacement_NullIdStrings_BecomeEmpty()
    {
        var p = TerminalLaunchMapper.ToInWorldPlacement(
            new TerminalInWorldOptions { VehicleId = null!, PartId = null!, SubPartId = null! },
            TerminalPlacementKind.InWorldPart, "x");

        Assert.That(p.VehicleId, Is.EqualTo(""));
        Assert.That(p.PartId, Is.EqualTo(""));
        Assert.That(p.SubPartId, Is.EqualTo(""));
    }

    // ---- WindowPixelSize ----

    [Test]
    public void WindowPixelSize_AppliesTheDocumentedFormula()
    {
        var (w, h) = TerminalLaunchMapper.WindowPixelSize(
            cols: 80, rows: 24, cellWidth: 9.6f, cellHeight: 19f, chromeHeight: 28f, padding: 4f);

        // 80 * 9.6 + 2 * 4 = 776 ; 24 * 19 + 28 + 2 * 4 = 492
        Assert.That(w, Is.EqualTo(776f));
        Assert.That(h, Is.EqualTo(492f));
    }

    [Test]
    public void WindowPixelSize_RoundsUp()
    {
        var (w, h) = TerminalLaunchMapper.WindowPixelSize(
            cols: 10, rows: 5, cellWidth: 9.625f, cellHeight: 19.25f, chromeHeight: 0f, padding: 0f);

        Assert.That(w, Is.EqualTo(97f)); // 96.25 -> 97
        Assert.That(h, Is.EqualTo(97f)); // 96.25 -> 97
    }

    [Test]
    public void WindowPixelSize_ColsAndRows_AreClampedToAtLeastOne()
    {
        var zero = TerminalLaunchMapper.WindowPixelSize(0, 0, 10f, 20f, 30f, 0f);
        var negative = TerminalLaunchMapper.WindowPixelSize(-4, -9, 10f, 20f, 30f, 0f);
        var one = TerminalLaunchMapper.WindowPixelSize(1, 1, 10f, 20f, 30f, 0f);

        Assert.That(zero, Is.EqualTo(one));
        Assert.That(negative, Is.EqualTo(one));
        Assert.That(one, Is.EqualTo((10f, 50f)));
    }

    // ---- ToWindowRecord ----

    [Test]
    public void ToWindowRecord_NullOptions_UsesFallbackNameAndNoGeometry()
    {
        var launch = ProcessLaunchOptions.CreateDefault();

        var record = TerminalLaunchMapper.ToWindowRecord(null, launch, "Terminal", null);

        Assert.That(record.Name, Is.EqualTo("Terminal"));
        Assert.That(record.Position, Is.Null);
        Assert.That(record.Size, Is.Null);
        Assert.That(record.ThemeName, Is.Null);
        Assert.That(record.Launch, Is.SameAs(launch));
    }

    [Test]
    public void ToWindowRecord_CopiesNameThemeAndLaunch()
    {
        var launch = ProcessLaunchOptions.CreateDefault();
        var options = new TerminalWindowOptions { Name = " Door Panel ", ThemeName = "Nord" };

        var record = TerminalLaunchMapper.ToWindowRecord(options, launch, "fallback", null);

        Assert.That(record.Name, Is.EqualTo("Door Panel"));
        Assert.That(record.ThemeName, Is.EqualTo("Nord"));
        Assert.That(record.Launch, Is.SameAs(launch));
    }

    [Test]
    public void ToWindowRecord_BlankName_UsesTheFallback()
    {
        var record = TerminalLaunchMapper.ToWindowRecord(
            new TerminalWindowOptions { Name = "  " }, ProcessLaunchOptions.CreateDefault(), "fallback", null);

        Assert.That(record.Name, Is.EqualTo("fallback"));
    }

    [Test]
    public void ToWindowRecord_Position_NeedsBothCoordinates()
    {
        var launch = ProcessLaunchOptions.CreateDefault();

        var both = TerminalLaunchMapper.ToWindowRecord(new TerminalWindowOptions { PosX = 10f, PosY = 20f }, launch, "n", null);
        var onlyX = TerminalLaunchMapper.ToWindowRecord(new TerminalWindowOptions { PosX = 10f }, launch, "n", null);
        var onlyY = TerminalLaunchMapper.ToWindowRecord(new TerminalWindowOptions { PosY = 20f }, launch, "n", null);

        Assert.That(both.Position, Is.Not.Null);
        Assert.That(both.Position!.Value.X, Is.EqualTo(10f));
        Assert.That(both.Position!.Value.Y, Is.EqualTo(20f));
        Assert.That(onlyX.Position, Is.Null);
        Assert.That(onlyY.Position, Is.Null);
    }

    [Test]
    public void ToWindowRecord_ExplicitSize_NeedsBothDimensions()
    {
        var launch = ProcessLaunchOptions.CreateDefault();

        var both = TerminalLaunchMapper.ToWindowRecord(new TerminalWindowOptions { Width = 640f, Height = 400f }, launch, "n", null);
        var onlyWidth = TerminalLaunchMapper.ToWindowRecord(new TerminalWindowOptions { Width = 640f }, launch, "n", null);

        Assert.That(both.Size!.Value.X, Is.EqualTo(640f));
        Assert.That(both.Size!.Value.Y, Is.EqualTo(400f));
        Assert.That(onlyWidth.Size, Is.Null);
    }

    [Test]
    public void ToWindowRecord_GridSize_IsUsedWhenColsAndRowsAreRequested()
    {
        var options = new TerminalWindowOptions { Cols = 80, Rows = 24 };

        var record = TerminalLaunchMapper.ToWindowRecord(options, ProcessLaunchOptions.CreateDefault(), "n", (776f, 492f));

        Assert.That(record.Size!.Value.X, Is.EqualTo(776f));
        Assert.That(record.Size!.Value.Y, Is.EqualTo(492f));
    }

    [Test]
    public void ToWindowRecord_ExplicitPixelSize_WinsOverGridSize()
    {
        var options = new TerminalWindowOptions { Width = 500f, Height = 300f, Cols = 80, Rows = 24 };

        var record = TerminalLaunchMapper.ToWindowRecord(options, ProcessLaunchOptions.CreateDefault(), "n", (776f, 492f));

        Assert.That(record.Size!.Value.X, Is.EqualTo(500f));
        Assert.That(record.Size!.Value.Y, Is.EqualTo(300f));
    }

    [Test]
    public void ToWindowRecord_GridSizeWithoutColsAndRows_IsIgnored()
    {
        var launch = ProcessLaunchOptions.CreateDefault();

        var none = TerminalLaunchMapper.ToWindowRecord(new TerminalWindowOptions(), launch, "n", (776f, 492f));
        var onlyCols = TerminalLaunchMapper.ToWindowRecord(new TerminalWindowOptions { Cols = 80 }, launch, "n", (776f, 492f));

        Assert.That(none.Size, Is.Null);
        Assert.That(onlyCols.Size, Is.Null);
    }

    [Test]
    public void ToWindowRecord_ColsAndRowsWithoutComputedGridSize_LeavesSizeNull()
    {
        var record = TerminalLaunchMapper.ToWindowRecord(
            new TerminalWindowOptions { Cols = 80, Rows = 24 }, ProcessLaunchOptions.CreateDefault(), "n", null);

        Assert.That(record.Size, Is.Null);
    }

    [Test]
    public void ToWindowRecord_NullLaunch_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => TerminalLaunchMapper.ToWindowRecord(null, null!, "n", null));
    }
}
