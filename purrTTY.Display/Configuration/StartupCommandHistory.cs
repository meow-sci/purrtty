using purrTTY.Logging;
using Tomlyn;
using Tomlyn.Serialization;

namespace purrTTY.Display.Configuration;

/// <summary>
/// A most-recently-used list of previously typed terminal startup commands (see
/// <c>ProcessLaunchOptions.StartupCommand</c>), persisted to
/// <c>&lt;config&gt;/.purrTTY/startup-command-history.toml</c> next to <c>purrtty.toml</c>.
/// <para>
/// Loaded from disk once, on first access, and kept as an in-memory list from then on — an
/// ImGui history combo drawn every frame never touches the file system. <see cref="Record"/>
/// updates the cache and rewrites the file, but is only ever called on an explicit, infrequent
/// user action (creating a terminal), so the write cost never lands in the per-frame UI path.
/// </para>
/// </summary>
public sealed class StartupCommandHistory
{
    private const int MaxEntries = 20;

    private static readonly TomlSerializerOptions TomlOptions = new()
    {
        WriteIndented = true,
        IndentSize = 2,
    };

    private List<string>? _cache;

    /// <summary>Most-recently-used first. Triggers the one-time disk load on first access.</summary>
    public IReadOnlyList<string> Entries => _cache ??= Load();

    /// <summary>
    /// Moves <paramref name="command"/> to the front of the history (deduplicating an exact
    /// existing match), trims to <see cref="MaxEntries"/>, and persists. Blank input is a no-op.
    /// </summary>
    public void Record(string command)
    {
        command = command.Trim();
        if (command.Length == 0)
        {
            return;
        }

        var entries = _cache ??= Load();
        entries.RemoveAll(e => string.Equals(e, command, StringComparison.Ordinal));
        entries.Insert(0, command);
        if (entries.Count > MaxEntries)
        {
            entries.RemoveRange(MaxEntries, entries.Count - MaxEntries);
        }

        Save(entries);
    }

    public static string GetHistoryFilePath()
    {
        var configDir = Path.GetDirectoryName(ThemeConfiguration.GetConfigFilePath())
                        ?? Environment.CurrentDirectory;
        return Path.Combine(configDir, "startup-command-history.toml");
    }

    private static List<string> Load()
    {
        try
        {
            var path = GetHistoryFilePath();
            if (!File.Exists(path))
            {
                return new List<string>();
            }

            var toml = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(toml))
            {
                return new List<string>();
            }

            var file = TomlSerializer.Deserialize<HistoryFile>(toml, TomlOptions);
            return file?.Commands ?? new List<string>();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TomlException)
        {
            ModLog.Log.Debug($"Error loading startup command history: {ex.Message}");
            return new List<string>();
        }
    }

    private static void Save(List<string> entries)
    {
        try
        {
            var path = GetHistoryFilePath();
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var toml = TomlSerializer.Serialize(new HistoryFile { Commands = entries }, TomlOptions);
            AtomicFile.WriteAllText(path, toml);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TomlException)
        {
            ModLog.Log.Debug($"Error saving startup command history: {ex.Message}");
        }
    }

    private sealed class HistoryFile
    {
        public List<string> Commands { get; set; } = new();
    }
}
