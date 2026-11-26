using NUnit.Framework;
using purrTTY.Core.Terminal;
using purrTTY.Display.Configuration;
using purrTTY.Display.Ghostty;
using purrTTY.Display.Layouts;

namespace purrTTY.Display.Tests;

/// <summary>
/// Pins the layout TOML format and catalog: a mixed in-world + 2D layout round-trips
/// through save/load; window entries never persist cols/rows or in-world placement
/// (the grid is derived live); the shell spec round-trips through ProcessLaunchOptions;
/// and delete works. All file IO is isolated to a temp config directory.
/// </summary>
[TestFixture]
public sealed class LayoutTomlTests
{
    private string _dir = null!;
    private string? _savedOverride;

    [SetUp]
    public void Setup()
    {
        _dir = Directory.CreateTempSubdirectory("purrtty-layout-tests").FullName;
        _savedOverride = ThemeConfiguration.OverrideConfigDirectory;
        ThemeConfiguration.OverrideConfigDirectory = _dir;
    }

    [TearDown]
    public void TearDown()
    {
        ThemeConfiguration.OverrideConfigDirectory = _savedOverride;
        Directory.Delete(_dir, recursive: true);
    }

    private static TerminalLayout SampleLayout() => new()
    {
        Header = new LayoutHeader { Name = "Flight Ops", Description = "test set" },
        Terminals =
        {
            new TerminalEntry
            {
                Name = "Landing Guidance",
                Kind = TerminalKind.InWorld,
                Theme = "Solarized Dark",
                Cols = 100,
                Rows = 30,
                Mode = "part",
                VehicleId = "",
                PartId = "CommandPod",
                PartName = "Command Pod",
                OffsetZ = 2f,
                WidthMeters = 2f,
                HeightMeters = 2f,
                Shell = new ShellSpec
                {
                    ShellType = ShellType.CustomGame,
                    CustomShellId = "gatOS",
                    StartupCommand = "cd /root/land-o-matic && cargo run --release",
                },
            },
            new TerminalEntry
            {
                Name = "Console",
                Kind = TerminalKind.Window,
                Theme = "Default",
                PosX = 80f,
                PosY = 80f,
                Width = 880f,
                Height = 520f,
                Shell = new ShellSpec { ShellType = ShellType.PowerShell },
            },
        },
    };

    [Test]
    public void Layout_RoundTrips_ThroughCatalog()
    {
        var catalog = new LayoutCatalog();
        catalog.Save(SampleLayout());

        Assert.That(catalog.All(), Does.Contain("Flight Ops"));

        var loaded = catalog.Load("Flight Ops");
        Assert.That(loaded, Is.Not.Null);
        Assert.That(loaded!.Header.Description, Is.EqualTo("test set"));
        Assert.That(loaded.Terminals, Has.Count.EqualTo(2));

        var iw = loaded.Terminals[0];
        Assert.That(iw.Name, Is.EqualTo("Landing Guidance"));
        Assert.That(iw.Kind, Is.EqualTo(TerminalKind.InWorld));
        Assert.That(iw.Theme, Is.EqualTo("Solarized Dark"));
        Assert.That(iw.Cols, Is.EqualTo(100));
        Assert.That(iw.Rows, Is.EqualTo(30));
        Assert.That(iw.Mode, Is.EqualTo("part"));
        Assert.That(iw.PartId, Is.EqualTo("CommandPod"));
        Assert.That(iw.PartName, Is.EqualTo("Command Pod"));
        Assert.That(iw.OffsetZ, Is.EqualTo(2f).Within(0.001f));
        Assert.That(iw.Shell.ShellType, Is.EqualTo(ShellType.CustomGame));
        Assert.That(iw.Shell.CustomShellId, Is.EqualTo("gatOS"));
        Assert.That(iw.Shell.StartupCommand, Is.EqualTo("cd /root/land-o-matic && cargo run --release"));

        var win = loaded.Terminals[1];
        Assert.That(win.Kind, Is.EqualTo(TerminalKind.Window));
        Assert.That(win.PosX, Is.EqualTo(80f).Within(0.001f));
        Assert.That(win.Width, Is.EqualTo(880f).Within(0.001f));
        Assert.That(win.Shell.ShellType, Is.EqualTo(ShellType.PowerShell));
    }

    [Test]
    public void WindowEntry_DoesNotPersistColsRowsOrInWorldFields()
    {
        var catalog = new LayoutCatalog();
        catalog.Save(SampleLayout());
        var loaded = catalog.Load("Flight Ops")!;

        // The 2D window entry must carry no grid size and no in-world placement.
        var win = loaded.Terminals[1];
        Assert.That(win.Cols, Is.Null, "windows must not persist cols");
        Assert.That(win.Rows, Is.Null, "windows must not persist rows");
        Assert.That(win.Mode, Is.Null);
        Assert.That(win.OffsetZ, Is.Null);
        Assert.That(win.VehicleId, Is.Null);

        // ...and the in-world entry must carry no window pixel geometry.
        var iw = loaded.Terminals[0];
        Assert.That(iw.PosX, Is.Null);
        Assert.That(iw.Width, Is.Null);
    }

    [Test]
    public void ShellSpec_RoundTripsThroughLaunchOptions()
    {
        var original = ProcessLaunchOptions.CreateCustom("ssh", "user@host");
        original.StartupCommand = "htop";

        var rebuilt = ShellSpec.From(original).ToLaunchOptions();

        Assert.That(rebuilt.ShellType, Is.EqualTo(ShellType.Custom));
        Assert.That(rebuilt.CustomShellPath, Is.EqualTo("ssh"));
        Assert.That(rebuilt.Arguments, Is.EqualTo(new[] { "user@host" }));
        Assert.That(rebuilt.StartupCommand, Is.EqualTo("htop"));
    }

    [Test]
    public void ShellSpec_CloseOnExit_RoundTripsThroughLaunchOptions()
    {
        var original = ProcessLaunchOptions.CreateCustom("nvim", "main.kc");
        original.CloseOnExit = true;

        Assert.That(ShellSpec.From(original).ToLaunchOptions().CloseOnExit, Is.True);
        Assert.That(ShellSpec.From(ProcessLaunchOptions.CreateDefault()).ToLaunchOptions().CloseOnExit, Is.False);
        Assert.That(ShellSpec.From(null).CloseOnExit, Is.False, "the default shell does not close on exit");
    }

    [Test]
    public void A_Window_Still_Starting_Is_Captured_With_The_Shell_It_Was_Opened_With()
    {
        // A kFS editor window whose session has not started yet: its launch (with the marker the
        // layout manager leaves out) must be captured, not a default shell under "kFS: x".
        var editor = ProcessLaunchOptions.CreateCustom("/usr/bin/nvim", "--", "t.c");
        editor.EnvironmentVariables["PURRTTY_KFS_ROOT"] = "/ws";
        ProcessLaunchOptions? starting = purrTTY.Display.Ghostty.WindowLayoutRecord.LaunchOf(activeSession: null, openedWith: editor);
        Assert.That(starting, Is.Not.Null.And.Not.SameAs(editor), "a copy");
        Assert.That(starting!.EnvironmentVariables["PURRTTY_KFS_ROOT"], Is.EqualTo("/ws"));

        var shell = ProcessLaunchOptions.CreateCustom("/bin/zsh");
        Assert.That(purrTTY.Display.Ghostty.WindowLayoutRecord.LaunchOf(shell, editor)!.CustomShellPath, Is.EqualTo("/bin/zsh"), "the active session wins");
        Assert.That(purrTTY.Display.Ghostty.WindowLayoutRecord.LaunchOf(null, null), Is.Null, "the default shell");
    }

    [Test]
    public void CloseOnSuccessfulExit_RoundTripsThroughLaunchOptionsAndToml_AndIsOnlyWrittenWhenSet()
    {
        var original = ProcessLaunchOptions.CreateCustom("nvim", "main.kc");
        original.CloseOnSuccessfulExit = true;
        Assert.That(ShellSpec.From(original).ToLaunchOptions().CloseOnSuccessfulExit, Is.True);
        Assert.That(ShellSpec.From(original).ToLaunchOptions().CloseOnExit, Is.False, "the two options are independent");
        Assert.That(ShellSpec.From(null).CloseOnSuccessfulExit, Is.False);

        var layout = SampleLayout();
        layout.Terminals[1].Shell.CloseOnSuccessfulExit = true;
        var catalog = new LayoutCatalog();
        catalog.Save(layout);

        var loaded = catalog.Load("Flight Ops");
        Assert.That(loaded!.Terminals[0].Shell.CloseOnSuccessfulExit, Is.False, "absent key loads as false (older files)");
        Assert.That(loaded.Terminals[1].Shell.CloseOnSuccessfulExit, Is.True);
        Assert.That(loaded.Terminals[1].Shell.CloseOnExit, Is.False);
        string text = File.ReadAllText(Directory.GetFiles(LayoutCatalog.GetLayoutsDirectory(), "*.toml").Single());
        Assert.That(text.Split("close_on_successful_exit = true").Length - 1, Is.EqualTo(1), text);
        Assert.That(text, Does.Not.Contain("close_on_exit"), text);
    }

    [Test]
    public void CloseOnExit_RoundTripsThroughToml_AndIsOnlyWrittenWhenSet()
    {
        var layout = SampleLayout();
        layout.Terminals[1].Shell.CloseOnExit = true; // the window entry

        var catalog = new LayoutCatalog();
        catalog.Save(layout);

        var loaded = catalog.Load("Flight Ops");
        Assert.That(loaded, Is.Not.Null);
        Assert.That(loaded!.Terminals[0].Shell.CloseOnExit, Is.False, "absent key loads as false (older files)");
        Assert.That(loaded.Terminals[1].Shell.CloseOnExit, Is.True);

        // Emitted only when true, so layouts that never use it keep their exact text.
        string text = File.ReadAllText(Directory.GetFiles(LayoutCatalog.GetLayoutsDirectory(), "*.toml").Single());
        Assert.That(text.Split("close_on_exit").Length - 1, Is.EqualTo(1), text);
        Assert.That(text, Does.Contain("close_on_exit = true"), text);
    }

    [Test]
    public void Delete_RemovesLayout()
    {
        var catalog = new LayoutCatalog();
        catalog.Save(SampleLayout());

        Assert.That(catalog.Delete("Flight Ops"), Is.True);
        Assert.That(catalog.All(), Does.Not.Contain("Flight Ops"));
        Assert.That(catalog.Delete("Flight Ops"), Is.False, "deleting a missing layout returns false");
    }

    [Test]
    public void UnknownShellType_FallsBackToAuto()
    {
        // A hand-edited file with a bogus shell_type must degrade to Auto, not throw.
        var dir = LayoutCatalog.GetLayoutsDirectory();
        Directory.CreateDirectory(dir);
        File.WriteAllText(
            Path.Combine(dir, "Raw.toml"),
            "[layout]\nname = \"Raw\"\n\n[[terminal]]\nname = \"T\"\nkind = \"window\"\n\n[terminal.shell]\nshell_type = \"NotARealShell\"\n");

        var loaded = new LayoutCatalog().Load("Raw");

        Assert.That(loaded, Is.Not.Null);
        Assert.That(loaded!.Terminals, Has.Count.EqualTo(1));
        Assert.That(loaded.Terminals[0].Shell.ShellType, Is.EqualTo(ShellType.Auto));
    }
}
