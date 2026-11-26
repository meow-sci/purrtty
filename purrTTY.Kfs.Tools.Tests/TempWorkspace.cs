namespace purrTTY.Kfs.Tools.Tests;

/// <summary>A kFS workspace folder on disk in a unique temporary directory, deleted on dispose.</summary>
internal sealed class TempWorkspace : IDisposable
{
    public TempWorkspace()
    {
        Root = Path.Combine(Path.GetTempPath(), "purr-kfs-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    /// <summary>The folder, absolute.</summary>
    public string Root { get; }

    /// <summary>Writes a file (workspace-relative path, forward slashes), creating its folders.</summary>
    public TempWorkspace Write(string path, string text)
    {
        string full = Full(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text);
        return this;
    }

    /// <summary>Creates an empty folder.</summary>
    public TempWorkspace Folder(string path)
    {
        Directory.CreateDirectory(Full(path));
        return this;
    }

    /// <summary>The absolute path of a workspace-relative path.</summary>
    public string Full(string path) => Path.Combine(Root, path.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>Every file below the root with its length and last write time, for "nothing was written" checks.</summary>
    public IReadOnlyList<string> Snapshot() =>
        Directory.EnumerateFileSystemEntries(Root, "*", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .Select(p => File.Exists(p) ? $"{p} {new FileInfo(p).Length} {File.GetLastWriteTimeUtc(p).Ticks}" : p + "/")
            .ToList();

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // Best effort: a temp folder left behind is harmless.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
