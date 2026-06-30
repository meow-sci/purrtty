using System.Globalization;
using System.Text;
using purrTTY.Core.Terminal;
using purrTTY.Display.Configuration;
using purrTTY.Display.Ghostty;
using Tomlyn;
using Tomlyn.Model;

namespace purrTTY.Display.Layouts;

/// <summary>
/// Reads and writes layout TOML files. Loading parses to the Tomlyn DOM
/// (<see cref="TomlTable"/>); saving emits the TOML text directly. Both deliberately
/// avoid Tomlyn's reflection-based POCO (de)serializer, whose writer pulls
/// Microsoft.Extensions.ObjectPool from the host runtime — present in the game but
/// absent from the reference-assembly set used by tests/CI.
///
/// On-disk shape (one file per layout):
/// <code>
/// [layout]      name / description
/// [[terminal]]  name / kind ("window"|"inworld") / theme / placement
///   [terminal.shell] shell_type / custom_shell_path / custom_shell_id / arguments /
///                    working_directory / startup_command
/// </code>
/// 2D windows store position + size (px) only — never cols/rows or font. In-world
/// entries store cols/rows (authoritative) plus the anchor + transform. Absent keys
/// map to null / the GameMod mapper's defaults.
/// </summary>
internal static class LayoutTomlFormat
{
    private static readonly TomlSerializerOptions TomlOptions = new()
    {
        WriteIndented = true,
        IndentSize = 2,
    };

    // ---- Load (DOM parse) ----

    public static TerminalLayout? Load(string filePath)
    {
        try
        {
            var text = File.ReadAllText(filePath);
            if (TomlSerializer.Deserialize<TomlTable>(text, TomlOptions) is not { } root)
            {
                return null;
            }

            var layout = new TerminalLayout();
            if (GetTable(root, "layout") is { } header)
            {
                layout.Header.Name = GetString(header, "name") ?? string.Empty;
                layout.Header.Description = GetString(header, "description");
            }

            if (root.TryGetValue("terminal", out var value) && value is TomlTableArray terminals)
            {
                foreach (var entry in terminals)
                {
                    layout.Terminals.Add(ReadEntry(entry));
                }
            }

            return layout;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TomlException
                                       or ArgumentException or FormatException)
        {
            return null;
        }
    }

    private static TerminalEntry ReadEntry(TomlTable t)
    {
        var entry = new TerminalEntry
        {
            Name = GetString(t, "name") ?? string.Empty,
            Kind = string.Equals(GetString(t, "kind"), "inworld", StringComparison.OrdinalIgnoreCase)
                ? TerminalKind.InWorld
                : TerminalKind.Window,
            Theme = GetString(t, "theme"),
            PosX = GetFloat(t, "pos_x"),
            PosY = GetFloat(t, "pos_y"),
            Width = GetFloat(t, "width"),
            Height = GetFloat(t, "height"),
            Cols = GetInt(t, "cols"),
            Rows = GetInt(t, "rows"),
            Mode = GetString(t, "mode"),
            VehicleId = GetString(t, "vehicle_id"),
            PartId = GetString(t, "part_id"),
            PartName = GetString(t, "part_name"),
            SubPartId = GetString(t, "sub_part_id"),
            OffsetX = GetFloat(t, "offset_x"),
            OffsetY = GetFloat(t, "offset_y"),
            OffsetZ = GetFloat(t, "offset_z"),
            RotationX = GetFloat(t, "rotation_x"),
            RotationY = GetFloat(t, "rotation_y"),
            RotationZ = GetFloat(t, "rotation_z"),
            WidthMeters = GetFloat(t, "width_meters"),
            HeightMeters = GetFloat(t, "height_meters"),
            BillboardDistance = GetFloat(t, "billboard_distance"),
            BillboardOffsetX = GetFloat(t, "billboard_offset_x"),
            BillboardOffsetY = GetFloat(t, "billboard_offset_y"),
            BillboardWidthMeters = GetFloat(t, "billboard_width_meters"),
            BillboardHeightMeters = GetFloat(t, "billboard_height_meters"),
            BillboardAlwaysOnTop = GetBool(t, "billboard_always_on_top"),
        };

        if (GetTable(t, "shell") is { } shell)
        {
            entry.Shell = new ShellSpec
            {
                ShellType = Enum.TryParse<ShellType>(GetString(shell, "shell_type"), ignoreCase: true, out var st)
                    ? st
                    : ShellType.Auto,
                CustomShellPath = GetString(shell, "custom_shell_path"),
                CustomShellId = GetString(shell, "custom_shell_id"),
                Arguments = GetStringList(shell, "arguments"),
                WorkingDirectory = GetString(shell, "working_directory"),
                StartupCommand = GetString(shell, "startup_command"),
            };
        }

        return entry;
    }

    // ---- Save (text emit) ----

    public static void Save(string filePath, TerminalLayout layout)
    {
        var sb = new StringBuilder();

        sb.Append("[layout]\n");
        sb.Append("name = ").Append(Quote(layout.Header.Name)).Append('\n');
        if (!string.IsNullOrEmpty(layout.Header.Description))
        {
            sb.Append("description = ").Append(Quote(layout.Header.Description)).Append('\n');
        }

        foreach (var e in layout.Terminals)
        {
            sb.Append("\n[[terminal]]\n");
            sb.Append("name = ").Append(Quote(e.Name)).Append('\n');
            sb.Append("kind = ").Append(Quote(e.Kind == TerminalKind.InWorld ? "inworld" : "window")).Append('\n');
            AppendStr(sb, "theme", e.Theme);

            // 2D window placement (px) — position + size only.
            AppendNum(sb, "pos_x", e.PosX);
            AppendNum(sb, "pos_y", e.PosY);
            AppendNum(sb, "width", e.Width);
            AppendNum(sb, "height", e.Height);

            // In-world placement.
            AppendInt(sb, "cols", e.Cols);
            AppendInt(sb, "rows", e.Rows);
            AppendStr(sb, "mode", e.Mode);
            AppendStr(sb, "vehicle_id", e.VehicleId);
            AppendStr(sb, "part_id", e.PartId);
            AppendStr(sb, "part_name", e.PartName);
            AppendStr(sb, "sub_part_id", e.SubPartId);
            AppendNum(sb, "offset_x", e.OffsetX);
            AppendNum(sb, "offset_y", e.OffsetY);
            AppendNum(sb, "offset_z", e.OffsetZ);
            AppendNum(sb, "rotation_x", e.RotationX);
            AppendNum(sb, "rotation_y", e.RotationY);
            AppendNum(sb, "rotation_z", e.RotationZ);
            AppendNum(sb, "width_meters", e.WidthMeters);
            AppendNum(sb, "height_meters", e.HeightMeters);
            AppendNum(sb, "billboard_distance", e.BillboardDistance);
            AppendNum(sb, "billboard_offset_x", e.BillboardOffsetX);
            AppendNum(sb, "billboard_offset_y", e.BillboardOffsetY);
            AppendNum(sb, "billboard_width_meters", e.BillboardWidthMeters);
            AppendNum(sb, "billboard_height_meters", e.BillboardHeightMeters);
            AppendBool(sb, "billboard_always_on_top", e.BillboardAlwaysOnTop);

            // Shell sub-table LAST (TOML requires a table's scalars before its sub-tables).
            sb.Append("\n[terminal.shell]\n");
            sb.Append("shell_type = ").Append(Quote(e.Shell.ShellType.ToString())).Append('\n');
            AppendStr(sb, "custom_shell_path", e.Shell.CustomShellPath);
            AppendStr(sb, "custom_shell_id", e.Shell.CustomShellId);
            if (e.Shell.Arguments.Count > 0)
            {
                sb.Append("arguments = [");
                for (int i = 0; i < e.Shell.Arguments.Count; i++)
                {
                    if (i > 0)
                    {
                        sb.Append(", ");
                    }

                    sb.Append(Quote(e.Shell.Arguments[i]));
                }

                sb.Append("]\n");
            }

            AppendStr(sb, "working_directory", e.Shell.WorkingDirectory);
            AppendStr(sb, "startup_command", e.Shell.StartupCommand);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        AtomicFile.WriteAllText(filePath, sb.ToString());
    }

    private static void AppendStr(StringBuilder sb, string key, string? value)
    {
        if (value is not null)
        {
            sb.Append(key).Append(" = ").Append(Quote(value)).Append('\n');
        }
    }

    private static void AppendInt(StringBuilder sb, string key, int? value)
    {
        if (value is { } v)
        {
            sb.Append(key).Append(" = ").Append(v.ToString(CultureInfo.InvariantCulture)).Append('\n');
        }
    }

    private static void AppendNum(StringBuilder sb, string key, float? value)
    {
        if (value is { } v)
        {
            // Always emit a decimal point so the value parses back as a TOML float.
            sb.Append(key).Append(" = ").Append(((double)v).ToString("0.0##########", CultureInfo.InvariantCulture)).Append('\n');
        }
    }

    private static void AppendBool(StringBuilder sb, string key, bool? value)
    {
        if (value is { } v)
        {
            sb.Append(key).Append(" = ").Append(v ? "true" : "false").Append('\n');
        }
    }

    /// <summary>Emits a TOML basic string with the required escapes.</summary>
    private static string Quote(string value)
    {
        var sb = new StringBuilder(value.Length + 2);
        sb.Append('"');
        foreach (char c in value)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default: sb.Append(c); break;
            }
        }

        sb.Append('"');
        return sb.ToString();
    }

    // ---- DOM read helpers (mirror ThemeTomlFormat) ----

    private static TomlTable? GetTable(TomlTable parent, string key)
        => parent.TryGetValue(key, out var value) && value is TomlTable table ? table : null;

    private static string? GetString(TomlTable table, string key)
        => table.TryGetValue(key, out var value) && value is string s ? s : null;

    private static bool? GetBool(TomlTable table, string key)
        => table.TryGetValue(key, out var value) && value is bool b ? b : null;

    private static int? GetInt(TomlTable table, string key)
        => table.TryGetValue(key, out var value) && value is long l ? (int)l : null;

    private static float? GetFloat(TomlTable table, string key)
        => table.TryGetValue(key, out var value)
            ? value switch
            {
                double d => (float)d,
                long l => l,
                _ => null,
            }
            : null;

    private static List<string> GetStringList(TomlTable table, string key)
    {
        var list = new List<string>();
        if (table.TryGetValue(key, out var value) && value is TomlArray array)
        {
            foreach (var item in array)
            {
                if (item is string s)
                {
                    list.Add(s);
                }
            }
        }

        return list;
    }
}
