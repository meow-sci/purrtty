using System.Reflection;
using purrTTY.Display.Configuration;
using purrTTY.Logging;
using PurrTTY.Terminal.Rendering;

namespace purrTTY.Display.Theming;

/// <summary>
/// The set of themes available to the UI: the code-built default, the bundled
/// TOML themes (TerminalThemes/ beside the assemblies), and user-saved themes
/// (the <c>themes</c> folder next to purrtty.toml in the config directory).
/// </summary>
public sealed class ThemeCatalog
{
    private readonly List<ThemeDefinition> _builtIn = new();
    private readonly List<ThemeDefinition> _user = new();

    public ThemeDefinition Default { get; } = CreateDefaultDefinition();

    /// <summary>Bundled themes (including <see cref="Default"/>), sorted by name.</summary>
    public IReadOnlyList<ThemeDefinition> BuiltInThemes => _builtIn;

    /// <summary>User-saved themes, sorted by name.</summary>
    public IReadOnlyList<ThemeDefinition> UserThemes => _user;

    public ThemeCatalog()
    {
        Refresh();
    }

    /// <summary>Reloads both theme directories.</summary>
    public void Refresh()
    {
        _builtIn.Clear();
        _builtIn.Add(Default);
        _builtIn.AddRange(LoadDirectory(GetBuiltInThemesDirectory(), ThemeSource.BuiltIn));
        _builtIn.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

        _user.Clear();
        _user.AddRange(LoadDirectory(GetUserThemesDirectory(), ThemeSource.UserFile));
        _user.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Finds a theme by name; user themes take precedence over built-ins.</summary>
    public ThemeDefinition? Find(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return _user.FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
               ?? _builtIn.FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>True when a user theme with this name already exists (it would be overwritten).</summary>
    public bool UserThemeExists(string name)
        => _user.Any(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Persists a user theme to the config themes directory and refreshes the
    /// catalog. Returns the saved definition (re-read from disk).
    /// </summary>
    public ThemeDefinition SaveUserTheme(ThemeDefinition theme)
    {
        var fileName = SanitizeFileName(theme.Name) + ".toml";
        var filePath = Path.Combine(GetUserThemesDirectory(), fileName);
        ThemeTomlFormat.Save(filePath, theme);
        ModLog.Log.Debug($"ThemeCatalog: saved user theme '{theme.Name}' to {filePath}");
        Refresh();
        return Find(theme.Name) ?? theme;
    }

    /// <summary>
    /// Deletes a user theme's backing file and refreshes the catalog. Returns
    /// true when a matching user theme was found and removed; false if no such
    /// user theme exists. Throws on filesystem failure so callers can surface it.
    /// </summary>
    public bool DeleteUserTheme(string name)
    {
        var theme = _user.FirstOrDefault(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (theme?.FilePath is not { } filePath)
        {
            return false;
        }

        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }

        ModLog.Log.Debug($"ThemeCatalog: deleted user theme '{theme.Name}' ({filePath})");
        Refresh();
        return true;
    }

    public static string GetBuiltInThemesDirectory()
    {
        var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
                          ?? Environment.CurrentDirectory;
        return Path.Combine(assemblyDir, "TerminalThemes");
    }

    public static string GetUserThemesDirectory()
    {
        var configDir = Path.GetDirectoryName(ThemeConfiguration.GetConfigFilePath())
                        ?? Environment.CurrentDirectory;
        return Path.Combine(configDir, "themes");
    }

    private static IEnumerable<ThemeDefinition> LoadDirectory(string directory, ThemeSource source)
    {
        if (!Directory.Exists(directory))
        {
            yield break;
        }

        string[] files;
        try
        {
            files = Directory.GetFiles(directory, "*.toml", SearchOption.TopDirectoryOnly);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ModLog.Log.Debug($"ThemeCatalog: cannot read themes directory '{directory}': {ex.Message}");
            yield break;
        }

        foreach (var file in files)
        {
            if (ThemeTomlFormat.Load(file, source) is { } theme)
            {
                yield return theme;
            }
        }
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        return new string(chars);
    }

    /// <summary>The same xterm defaults the engine fallback theme used.</summary>
    private static ThemeDefinition CreateDefaultDefinition()
    {
        var colors = new ThemeColors
        {
            Foreground = new RgbaColor(0xCC, 0xCC, 0xCC),
            Background = new RgbaColor(0x0A, 0x0A, 0x0A),
            Cursor = new RgbaColor(0xCC, 0xCC, 0xCC),
            SelectionBackground = new RgbaColor(0x33, 0x55, 0x88),
        };

        (byte r, byte g, byte b)[] base16 =
        {
            (0, 0, 0), (205, 0, 0), (0, 205, 0), (205, 205, 0),
            (0, 0, 238), (205, 0, 205), (0, 205, 205), (229, 229, 229),
            (127, 127, 127), (255, 0, 0), (0, 255, 0), (255, 255, 0),
            (92, 92, 255), (255, 0, 255), (0, 255, 255), (255, 255, 255),
        };
        for (int i = 0; i < 16; i++)
        {
            colors.Ansi[i] = new RgbaColor(base16[i].r, base16[i].g, base16[i].b);
        }

        return new ThemeDefinition
        {
            Name = "Default",
            Source = ThemeSource.BuiltIn,
            Colors = colors,
        };
    }
}
