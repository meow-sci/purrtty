using NUnit.Framework;
using purrTTY.Kfs.Catalog;

namespace purrTTY.Kfs.Tests;

/// <summary>
/// The emitted artefacts against checked-in snapshots in <c>purrTTY.Kfs.Tests/Snapshots/</c>
/// (copied to the output folder). A change to the catalogue shows up here as a diff to review:
/// these lists are part of the image format.
/// </summary>
/// <remarks>
/// <para>To regenerate after an intended change, run the tests once with
/// <c>PURRTTY_UPDATE_SNAPSHOTS=1</c>: each snapshot test then writes its file into the source
/// folder (found by walking up from the output folder to <c>purrtty.slnx</c>) and passes. Review
/// the diff and commit it. Off by default.</para>
/// <para>Snapshots are stored and compared with <c>\n</c> line ends, so a checkout that converts
/// line ends does not matter; the header's own <c>\r\n</c> line ends are asserted in
/// <see cref="CatalogTests"/>.</para>
/// </remarks>
[TestFixture]
public sealed class SnapshotTests
{
    /// <summary>Set to <c>1</c> to rewrite the snapshot files instead of comparing.</summary>
    public const string UpdateVariable = "PURRTTY_UPDATE_SNAPSHOTS";

    [Test]
    public void Snapshot_Header() => AssertSnapshot("purrtty.h", CatalogEmitter.Header(XCatalog.Catalog));

    [Test]
    public void Snapshot_Kinc() => AssertSnapshot("purrtty.kinc", CatalogEmitter.Kinc(XCatalog.Catalog));

    [Test]
    public void Snapshot_Service_Rows() => AssertSnapshot("services.rows.json", CatalogEmitter.ServiceRowsJson(XCatalog.Catalog));

    [Test]
    public void Snapshot_Enum_Entries() => AssertSnapshot("enums.entries.json", CatalogEmitter.EnumsJson(XCatalog.Catalog));

    private static void AssertSnapshot(string file, string actual)
    {
        actual = Normalize(actual);
        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            string root = FindRepositoryRoot(AppContext.BaseDirectory)
                ?? throw new InvalidOperationException("cannot find purrtty.slnx above the test output folder to update snapshots");
            // The source file (to commit) and the output copy (so a run without a rebuild agrees).
            foreach (string directory in new[] { Path.Combine(root, "purrTTY.Kfs.Tests", "Snapshots"), Path.Combine(AppContext.BaseDirectory, "Snapshots") })
            {
                Directory.CreateDirectory(directory);
                File.WriteAllText(Path.Combine(directory, file), actual);
            }
            return;
        }

        string expectedPath = Path.Combine(AppContext.BaseDirectory, "Snapshots", file);
        Assert.That(File.Exists(expectedPath), Is.True, $"missing snapshot {expectedPath}; create it with {UpdateVariable}=1");
        string expected = Normalize(File.ReadAllText(expectedPath));
        Assert.That(actual, Is.EqualTo(expected),
            $"{file} differs from its snapshot. If the change is intended, run the tests with {UpdateVariable}=1 and commit the new snapshot.");
    }

    private static string Normalize(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string? FindRepositoryRoot(string start)
    {
        for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "purrtty.slnx"))) return dir.FullName;
        }
        return null;
    }
}
