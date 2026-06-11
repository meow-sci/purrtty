using System.Globalization;
using PurrTTY.Terminal.Rendering;
using purrTTY.Logging;
using Tomlyn;
using Tomlyn.Model;

namespace purrTTY.Display.Theming;

/// <summary>
/// Reads and writes theme TOML files. The on-disk format is the alacritty-style
/// color scheme the bundled themes already use:
///
/// <code>
/// [colors.normal]  black/red/green/yellow/blue/magenta/cyan/white = '#rrggbb'
/// [colors.bright]  ...
/// [colors.primary] background/foreground
/// [colors.cursor]  cursor            (optional; defaults to foreground)
/// [colors.selection] background      (optional)
/// </code>
///
/// User-saved themes additionally carry the full window appearance and their
/// display name (the filename is sanitized, so it cannot round-trip the name):
///
/// <code>
/// [meta]   name = "My Theme!"
/// [font]   family = "Hack"  size = 32.0
/// [window] background_opacity / foreground_opacity / cell_background_opacity
/// </code>
/// </summary>
internal static class ThemeTomlFormat
{
    private static readonly TomlSerializerOptions TomlOptions = new()
    {
        WriteIndented = true,
        IndentSize = 2,
    };

    private static readonly string[] AnsiNames =
        { "black", "red", "green", "yellow", "blue", "magenta", "cyan", "white" };

    public static ThemeDefinition? Load(string filePath, ThemeSource source)
    {
        try
        {
            var text = File.ReadAllText(filePath);
            if (TomlSerializer.Deserialize<TomlTable>(text, TomlOptions) is not { } root
                || GetTable(root, "colors") is not { } colors
                || GetTable(colors, "normal") is not { } normal
                || GetTable(colors, "bright") is not { } bright
                || GetTable(colors, "primary") is not { } primary)
            {
                ModLog.Log.Debug($"ThemeTomlFormat: '{filePath}' is missing required [colors.*] sections; skipped");
                return null;
            }

            var themeColors = new ThemeColors();
            for (int i = 0; i < 8; i++)
            {
                themeColors.Ansi[i] = ParseHexColor(GetString(normal, AnsiNames[i]));
                themeColors.Ansi[i + 8] = ParseHexColor(GetString(bright, AnsiNames[i]));
            }

            themeColors.Foreground = ParseHexColor(GetString(primary, "foreground"));
            themeColors.Background = ParseHexColor(GetString(primary, "background"));
            themeColors.Cursor = GetTable(colors, "cursor") is { } cursor && GetString(cursor, "cursor") is { } c
                ? ParseHexColor(c)
                : themeColors.Foreground;
            themeColors.SelectionBackground =
                GetTable(colors, "selection") is { } selection && GetString(selection, "background") is { } s
                    ? ParseHexColor(s)
                    : new RgbaColor(0x60, 0x60, 0x60);

            var font = GetTable(root, "font");
            var window = GetTable(root, "window");

            // User-saved themes carry their display name in [meta]: the filename
            // is sanitized on save, so deriving the name from it would not
            // round-trip (the saved theme then fails to resolve on next launch).
            // Bundled/alacritty themes have no [meta] and fall back to the filename.
            string? metaName = GetTable(root, "meta") is { } meta ? GetString(meta, "name") : null;

            return new ThemeDefinition
            {
                Name = string.IsNullOrWhiteSpace(metaName) ? Path.GetFileNameWithoutExtension(filePath) : metaName,
                Source = source,
                FilePath = filePath,
                Colors = themeColors,
                FontFamily = font is null ? null : GetString(font, "family"),
                FontSize = font is null ? null : GetFloat(font, "size"),
                BackgroundOpacity = window is null ? null : GetFloat(window, "background_opacity"),
                ForegroundOpacity = window is null ? null : GetFloat(window, "foreground_opacity"),
                CellBackgroundOpacity = window is null ? null : GetFloat(window, "cell_background_opacity"),
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TomlException
                                       or ArgumentException or FormatException)
        {
            ModLog.Log.Debug($"ThemeTomlFormat: failed to load theme '{filePath}': {ex.Message}");
            return null;
        }
    }

    public static void Save(string filePath, ThemeDefinition theme)
    {
        var normal = new TomlTable();
        var bright = new TomlTable();
        for (int i = 0; i < 8; i++)
        {
            normal[AnsiNames[i]] = ToHex(theme.Colors.Ansi[i]);
            bright[AnsiNames[i]] = ToHex(theme.Colors.Ansi[i + 8]);
        }

        var root = new TomlTable
        {
            ["meta"] = new TomlTable { ["name"] = theme.Name },
            ["colors"] = new TomlTable
            {
                ["normal"] = normal,
                ["bright"] = bright,
                ["primary"] = new TomlTable
                {
                    ["background"] = ToHex(theme.Colors.Background),
                    ["foreground"] = ToHex(theme.Colors.Foreground),
                },
                ["cursor"] = new TomlTable { ["cursor"] = ToHex(theme.Colors.Cursor) },
                ["selection"] = new TomlTable { ["background"] = ToHex(theme.Colors.SelectionBackground) },
            },
        };

        if (theme.FontFamily is not null || theme.FontSize is not null)
        {
            var font = new TomlTable();
            if (theme.FontFamily is not null)
            {
                font["family"] = theme.FontFamily;
            }

            if (theme.FontSize is { } size)
            {
                font["size"] = (double)size;
            }

            root["font"] = font;
        }

        if (theme.BackgroundOpacity is not null || theme.ForegroundOpacity is not null
            || theme.CellBackgroundOpacity is not null)
        {
            var window = new TomlTable();
            if (theme.BackgroundOpacity is { } bg)
            {
                window["background_opacity"] = (double)bg;
            }

            if (theme.ForegroundOpacity is { } fg)
            {
                window["foreground_opacity"] = (double)fg;
            }

            if (theme.CellBackgroundOpacity is { } cell)
            {
                window["cell_background_opacity"] = (double)cell;
            }

            root["window"] = window;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        purrTTY.Display.Configuration.AtomicFile.WriteAllText(filePath, TomlSerializer.Serialize(root, TomlOptions));
    }

    public static RgbaColor ParseHexColor(string? hex)
    {
        if (hex is null || hex.Length != 7 || hex[0] != '#')
        {
            throw new FormatException($"Invalid hex color '{hex}'; expected #rrggbb");
        }

        byte r = byte.Parse(hex.AsSpan(1, 2), NumberStyles.HexNumber);
        byte g = byte.Parse(hex.AsSpan(3, 2), NumberStyles.HexNumber);
        byte b = byte.Parse(hex.AsSpan(5, 2), NumberStyles.HexNumber);
        return new RgbaColor(r, g, b);
    }

    public static string ToHex(RgbaColor c) => $"#{c.R:x2}{c.G:x2}{c.B:x2}";

    private static TomlTable? GetTable(TomlTable parent, string key)
        => parent.TryGetValue(key, out var value) && value is TomlTable table ? table : null;

    private static string? GetString(TomlTable table, string key)
        => table.TryGetValue(key, out var value) && value is string s ? s : null;

    private static float? GetFloat(TomlTable table, string key)
        => table.TryGetValue(key, out var value)
            ? value switch
            {
                double d => (float)d,
                long l => l,
                _ => null,
            }
            : null;
}
