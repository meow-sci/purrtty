using NUnit.Framework;
using purrTTY.Display.Configuration;

namespace purrTTY.Display.Tests;

/// <summary>
/// Pins the startup-command history contract: entries persist across instances
/// (most-recently-used first), re-recording an existing command moves it to the
/// front instead of duplicating it, and — the point of the feature — once an
/// instance has loaded, it never re-reads the file, so later on-disk changes made
/// behind its back are invisible until a fresh instance loads. All file IO is
/// isolated to a temp config directory.
/// </summary>
[TestFixture]
public sealed class StartupCommandHistoryTests
{
    private string _dir = null!;
    private string? _savedOverride;

    [SetUp]
    public void Setup()
    {
        _dir = Directory.CreateTempSubdirectory("purrtty-startup-history-tests").FullName;
        _savedOverride = ThemeConfiguration.OverrideConfigDirectory;
        ThemeConfiguration.OverrideConfigDirectory = _dir;
    }

    [TearDown]
    public void TearDown()
    {
        ThemeConfiguration.OverrideConfigDirectory = _savedOverride;
        Directory.Delete(_dir, recursive: true);
    }

    [Test]
    public void Entries_WhenNoFileExists_IsEmpty()
    {
        var history = new StartupCommandHistory();

        Assert.That(history.Entries, Is.Empty);
    }

    [Test]
    public void Record_PersistsAcrossInstances_MostRecentFirst()
    {
        new StartupCommandHistory().Record("cd /root/land-o-matic && cargo run --release");
        new StartupCommandHistory().Record("watch -n 0.2 cat /sim/vessels/active/telemetry");

        var loaded = new StartupCommandHistory().Entries;

        Assert.That(loaded, Is.EqualTo(new[]
        {
            "watch -n 0.2 cat /sim/vessels/active/telemetry",
            "cd /root/land-o-matic && cargo run --release",
        }));
    }

    [Test]
    public void Record_ExistingCommand_MovesToFrontWithoutDuplicating()
    {
        var history = new StartupCommandHistory();
        history.Record("first");
        history.Record("second");

        history.Record("first");

        Assert.That(history.Entries, Is.EqualTo(new[] { "first", "second" }));
    }

    [Test]
    public void Record_BlankCommand_IsNoOp()
    {
        var history = new StartupCommandHistory();

        history.Record("   ");

        Assert.That(history.Entries, Is.Empty);
        Assert.That(File.Exists(StartupCommandHistory.GetHistoryFilePath()), Is.False);
    }

    [Test]
    public void Record_BeyondCapacity_DropsOldestEntries()
    {
        var history = new StartupCommandHistory();
        for (int i = 0; i < 25; i++)
        {
            history.Record($"cmd-{i}");
        }

        Assert.That(history.Entries, Has.Count.EqualTo(20));
        Assert.That(history.Entries[0], Is.EqualTo("cmd-24"));
        Assert.That(history.Entries, Does.Not.Contain("cmd-0"));
    }

    [Test]
    public void Entries_OnceLoaded_DoesNotReReadFileFromDisk()
    {
        new StartupCommandHistory().Record("original");
        var history = new StartupCommandHistory();
        _ = history.Entries; // triggers the one-time load

        // Mutate the file behind the cached instance's back.
        File.WriteAllText(StartupCommandHistory.GetHistoryFilePath(), "commands = [\"tampered\"]");

        Assert.That(history.Entries, Is.EqualTo(new[] { "original" }));
    }
}
