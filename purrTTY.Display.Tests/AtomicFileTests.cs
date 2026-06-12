using NUnit.Framework;
using purrTTY.Display.Configuration;

namespace purrTTY.Display.Tests;

/// <summary>
/// Pins the crash-safe write contract config and theme files rely on: content
/// lands via temp-file + rename, an existing file is replaced, and no temp
/// residue is left behind.
/// </summary>
[TestFixture]
public sealed class AtomicFileTests
{
    private string _dir = null!;

    [SetUp]
    public void Setup() => _dir = Directory.CreateTempSubdirectory("purrtty-atomicfile-tests").FullName;

    [TearDown]
    public void TearDown() => Directory.Delete(_dir, recursive: true);

    [Test]
    public void WriteAllText_CreatesFile_WithoutTempResidue()
    {
        string path = Path.Combine(_dir, "config.toml");

        AtomicFile.WriteAllText(path, "key = 1");

        Assert.That(File.ReadAllText(path), Is.EqualTo("key = 1"));
        Assert.That(Directory.GetFiles(_dir), Has.Length.EqualTo(1), "no .tmp file may remain after a write");
    }

    [Test]
    public void WriteAllText_ReplacesExistingContent()
    {
        string path = Path.Combine(_dir, "config.toml");
        AtomicFile.WriteAllText(path, "old");

        AtomicFile.WriteAllText(path, "new");

        Assert.That(File.ReadAllText(path), Is.EqualTo("new"));
        Assert.That(Directory.GetFiles(_dir), Has.Length.EqualTo(1));
    }
}
