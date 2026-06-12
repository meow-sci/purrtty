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
/// [cursor] style = "block"|"bar"|"underline"  blink = true
/// [focus]  border_on_focus / border_on_hover / border_opacity
/// [lock]   enabled / hot_zone / hot_zone_placement ("top-left".."bottom-right") /
///          hot_zone_width / hot_zone_height / hot_zone_color / hot_zone_opacity /
///          hot_zone_hover_opacity
/// </code>
///
/// All non-color sections are optional; a missing value means "keep the
/// window's current setting" when the theme is applied.
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
            var cursorTable = GetTable(root, "cursor");
            var focus = GetTable(root, "focus");
            var lockTable = GetTable(root, "lock");

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
                CursorStyle = cursorTable is null ? null : ParseCursorStyle(GetString(cursorTable, "style")),
                CursorBlink = cursorTable is null ? null : GetBool(cursorTable, "blink"),
                BorderOnFocus = focus is null ? null : GetBool(focus, "border_on_focus"),
                BorderOnHover = focus is null ? null : GetBool(focus, "border_on_hover"),
                BorderOpacity = focus is null ? null : GetFloat(focus, "border_opacity"),
                LockMode = lockTable is null ? null : GetBool(lockTable, "enabled"),
                HotZoneEnabled = lockTable is null ? null : GetBool(lockTable, "hot_zone"),
                HotZonePlacement = lockTable is null ? null : ParsePlacement(GetString(lockTable, "hot_zone_placement")),
                HotZoneWidth = lockTable is null ? null : GetFloat(lockTable, "hot_zone_width"),
                HotZoneHeight = lockTable is null ? null : GetFloat(lockTable, "hot_zone_height"),
                HotZoneColor = lockTable is null ? null
                    : TryParseHexColor(GetString(lockTable, "hot_zone_color"), out var zoneColor) ? zoneColor : (RgbaColor?)null,
                HotZoneOpacity = lockTable is null ? null : GetFloat(lockTable, "hot_zone_opacity"),
                HotZoneHoverOpacity = lockTable is null ? null : GetFloat(lockTable, "hot_zone_hover_opacity"),
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

        if (theme.CursorStyle is not null || theme.CursorBlink is not null)
        {
            var cursorTable = new TomlTable();
            if (theme.CursorStyle is { } style)
            {
                cursorTable["style"] = CursorStyleToString(style);
            }

            if (theme.CursorBlink is { } blink)
            {
                cursorTable["blink"] = blink;
            }

            root["cursor"] = cursorTable;
        }

        if (theme.BorderOnFocus is not null || theme.BorderOnHover is not null || theme.BorderOpacity is not null)
        {
            var focus = new TomlTable();
            if (theme.BorderOnFocus is { } onFocus)
            {
                focus["border_on_focus"] = onFocus;
            }

            if (theme.BorderOnHover is { } onHover)
            {
                focus["border_on_hover"] = onHover;
            }

            if (theme.BorderOpacity is { } borderOpacity)
            {
                focus["border_opacity"] = (double)borderOpacity;
            }

            root["focus"] = focus;
        }

        if (theme.LockMode is not null || theme.HotZoneEnabled is not null || theme.HotZonePlacement is not null
            || theme.HotZoneWidth is not null || theme.HotZoneHeight is not null || theme.HotZoneColor is not null
            || theme.HotZoneOpacity is not null || theme.HotZoneHoverOpacity is not null)
        {
            var lockTable = new TomlTable();
            if (theme.LockMode is { } lockMode)
            {
                lockTable["enabled"] = lockMode;
            }

            if (theme.HotZoneEnabled is { } hotZone)
            {
                lockTable["hot_zone"] = hotZone;
            }

            if (theme.HotZonePlacement is { } placement)
            {
                lockTable["hot_zone_placement"] = PlacementToString(placement);
            }

            if (theme.HotZoneWidth is { } zoneWidth)
            {
                lockTable["hot_zone_width"] = (double)zoneWidth;
            }

            if (theme.HotZoneHeight is { } zoneHeight)
            {
                lockTable["hot_zone_height"] = (double)zoneHeight;
            }

            if (theme.HotZoneColor is { } zoneColor)
            {
                lockTable["hot_zone_color"] = ToHex(zoneColor);
            }

            if (theme.HotZoneOpacity is { } zoneOpacity)
            {
                lockTable["hot_zone_opacity"] = (double)zoneOpacity;
            }

            if (theme.HotZoneHoverOpacity is { } zoneHoverOpacity)
            {
                lockTable["hot_zone_hover_opacity"] = (double)zoneHoverOpacity;
            }

            root["lock"] = lockTable;
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

    /// <summary>Non-throwing <see cref="ParseHexColor"/> for optional color fields.</summary>
    public static bool TryParseHexColor(string? hex, out RgbaColor color)
    {
        if (hex is { Length: 7 } && hex[0] == '#'
            && byte.TryParse(hex.AsSpan(1, 2), NumberStyles.HexNumber, null, out byte r)
            && byte.TryParse(hex.AsSpan(3, 2), NumberStyles.HexNumber, null, out byte g)
            && byte.TryParse(hex.AsSpan(5, 2), NumberStyles.HexNumber, null, out byte b))
        {
            color = new RgbaColor(r, g, b);
            return true;
        }

        color = default;
        return false;
    }

    /// <summary>Maps a theme cursor style string to a shape; null for unknown values.</summary>
    public static CursorShape? ParseCursorStyle(string? value) => value?.ToLowerInvariant() switch
    {
        "block" => CursorShape.Block,
        "bar" => CursorShape.Bar,
        "underline" => CursorShape.Underline,
        _ => null,
    };

    public static string CursorStyleToString(CursorShape shape) => shape switch
    {
        CursorShape.Bar => "bar",
        CursorShape.Underline => "underline",
        _ => "block",
    };

    /// <summary>Maps a hot-zone placement string to the enum; null for unknown values.</summary>
    public static HotZonePlacement? ParsePlacement(string? value) => value?.ToLowerInvariant() switch
    {
        "top-left" => HotZonePlacement.TopLeft,
        "top-center" => HotZonePlacement.TopCenter,
        "top-right" => HotZonePlacement.TopRight,
        "middle-left" => HotZonePlacement.MiddleLeft,
        "middle-right" => HotZonePlacement.MiddleRight,
        "bottom-left" => HotZonePlacement.BottomLeft,
        "bottom-center" => HotZonePlacement.BottomCenter,
        "bottom-right" => HotZonePlacement.BottomRight,
        _ => null,
    };

    public static string PlacementToString(HotZonePlacement placement) => placement switch
    {
        HotZonePlacement.TopLeft => "top-left",
        HotZonePlacement.TopCenter => "top-center",
        HotZonePlacement.MiddleLeft => "middle-left",
        HotZonePlacement.MiddleRight => "middle-right",
        HotZonePlacement.BottomLeft => "bottom-left",
        HotZonePlacement.BottomCenter => "bottom-center",
        HotZonePlacement.BottomRight => "bottom-right",
        _ => "top-right",
    };

    private static TomlTable? GetTable(TomlTable parent, string key)
        => parent.TryGetValue(key, out var value) && value is TomlTable table ? table : null;

    private static string? GetString(TomlTable table, string key)
        => table.TryGetValue(key, out var value) && value is string s ? s : null;

    private static bool? GetBool(TomlTable table, string key)
        => table.TryGetValue(key, out var value) && value is bool b ? b : null;

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
