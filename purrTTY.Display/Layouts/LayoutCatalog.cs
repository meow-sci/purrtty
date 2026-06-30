using purrTTY.Display.Configuration;

namespace purrTTY.Display.Layouts;

/// <summary>
/// CRUD over saved layouts — one TOML file per layout in the <c>layouts</c> folder
/// next to <c>purrtty.toml</c> (the same config root the theme catalog uses).
/// Mirrors <see cref="Theming.ThemeCatalog"/>: sanitized file names, crash-safe
/// writes, and graceful failure (load returns null, never throws, on a
/// missing/corrupt file). The TOML mapping lives in <see cref="LayoutTomlFormat"/>.
/// </summary>
public sealed class LayoutCatalog
{
    public static string GetLayoutsDirectory()
    {
        var configDir = Path.GetDirectoryName(ThemeConfiguration.GetConfigFilePath())
                        ?? Environment.CurrentDirectory;
        return Path.Combine(configDir, "layouts");
    }

    /// <summary>The names (file base names) of all saved layouts, sorted case-insensitively.</summary>
    public IReadOnlyList<string> All()
    {
        var directory = GetLayoutsDirectory();
        if (!Directory.Exists(directory))
        {
            return Array.Empty<string>();
        }

        string[] files;
        try
        {
            files = Directory.GetFiles(directory, "*.toml", SearchOption.TopDirectoryOnly);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }

        var names = new List<string>(files.Length);
        foreach (var file in files)
        {
            names.Add(Path.GetFileNameWithoutExtension(file));
        }

        names.Sort((a, b) => string.Compare(a, b, StringComparison.OrdinalIgnoreCase));
        return names;
    }

    /// <summary>True when a layout file with this name already exists.</summary>
    public bool Exists(string name) => File.Exists(PathFor(name));

    /// <summary>Loads a layout by name; returns null if missing or unparsable.</summary>
    public TerminalLayout? Load(string name)
    {
        var path = PathFor(name);
        if (!File.Exists(path))
        {
            return null;
        }

        var layout = LayoutTomlFormat.Load(path);
        if (layout is null)
        {
            return null;
        }

        // The file name is the source of truth for the layout's identity, so a
        // renamed/copied file still loads under the right name if [layout].name drifted.
        if (string.IsNullOrWhiteSpace(layout.Header.Name))
        {
            layout.Header.Name = name;
        }

        return layout;
    }

    /// <summary>Writes a layout (atomically), using its header name as the file name.</summary>
    public void Save(TerminalLayout layout)
    {
        var name = string.IsNullOrWhiteSpace(layout.Header.Name) ? "Layout" : layout.Header.Name;
        var path = PathFor(name);
        LayoutTomlFormat.Save(path, layout);
    }

    /// <summary>Deletes a layout file. Returns false if no such layout exists.</summary>
    public bool Delete(string name)
    {
        var path = PathFor(name);
        if (!File.Exists(path))
        {
            return false;
        }

        File.Delete(path);
        return true;
    }

    /// <summary>
    /// Renames the layout <b>file</b> (distinct from renaming a terminal inside it).
    /// Returns false when the source is missing, the target name is blank, or a layout
    /// with the new name already exists.
    /// </summary>
    public bool Rename(string oldName, string newName)
    {
        newName = (newName ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(newName) || Exists(newName))
        {
            return false;
        }

        var layout = Load(oldName);
        if (layout is null)
        {
            return false;
        }

        layout.Header.Name = newName;
        Save(layout);

        var oldPath = PathFor(oldName);
        if (!string.Equals(oldPath, PathFor(newName), StringComparison.OrdinalIgnoreCase) && File.Exists(oldPath))
        {
            File.Delete(oldPath);
        }

        return true;
    }

    private static string PathFor(string name)
        => Path.Combine(GetLayoutsDirectory(), SanitizeFileName(name) + ".toml");

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        return new string(chars);
    }
}
