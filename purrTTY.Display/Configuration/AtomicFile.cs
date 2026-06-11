namespace purrTTY.Display.Configuration;

/// <summary>
/// Crash-safe file writes for config/theme files: the content is written to a
/// sibling temp file and renamed over the destination, so an interrupted write
/// can never leave a truncated file behind.
/// </summary>
internal static class AtomicFile
{
    public static void WriteAllText(string path, string contents)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, contents);
        File.Move(tmp, path, overwrite: true);
    }
}
