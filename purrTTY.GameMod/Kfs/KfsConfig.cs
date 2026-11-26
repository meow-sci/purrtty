using purrTTY.Display.Configuration;
using purrTTY.Kfs.Runtime;
using purrTTY.Logging;
using Tomlyn;
using Tomlyn.Model;

namespace purrTTY.GameMod.Kfs;

/// <summary>
/// The kFS extension's own configuration file, <c>&lt;config&gt;/.purrTTY/kfs.toml</c> (plan §7.5),
/// next to <c>purrtty.toml</c> but separate from it: that file is a typed model re-serialised on
/// every save, which would drop a table it does not know, and the extension must stay switchable
/// without touching the terminal's settings.
/// </summary>
/// <remarks>
/// <para>Keys (unknown keys are ignored, so later keys can be added without breaking older files):</para>
/// <list type="bullet">
/// <item><c>enabled</c> (bool, default <c>true</c>): install the extension at start-up. Read once, when the mod loads.</item>
/// <item><c>nvim_path</c> (string, default empty): the absolute path of the <c>nvim</c> executable;
/// empty searches the game's <c>PATH</c>, then (macOS/Linux) the login shell's. Read at each
/// Neovim launch.</item>
/// <item><c>nvim_open_in</c> (<c>"window"</c> | <c>"inworld"</c>, default <c>"window"</c>): where
/// <c>purr nvim</c> and the kFS menu open Neovim. Read at each Neovim launch.</item>
/// <item><c>lsp_enabled</c> (bool, default <c>true</c>): run the kC/kASM language server inside the
/// game (plan §6.7). Read when the server starts (mod load, <c>purr lsp restart</c>).</item>
/// <item><c>lsp_port</c> (integer 0–65535, default <c>0</c> = pick a free port): its loopback port.
/// Read when it starts.</item>
/// <item><c>lsp_allow_vehicle_commands</c> (bool, default <c>false</c>): let the editor run any
/// <c>kfs</c> command line (<c>kfs.verb</c>); the fixed commands (build, load, start, stop, reset,
/// target, manual, parts) are always available. Read when it starts.</item>
/// </list>
/// <para>A missing file gives the defaults and is written with them (and a comment per key) on
/// first load. A file that is not valid TOML, or a key of the wrong type, gives the default for
/// what could not be read plus one log line, and the file is left as the player wrote it. Loading
/// never throws.</para>
/// </remarks>
internal sealed class KfsConfig
{
    /// <summary>The file name inside <c>&lt;config&gt;/.purrTTY/</c>.</summary>
    public const string FileName = "kfs.toml";

    /// <summary>The text written when the file does not exist.</summary>
    internal const string DefaultText =
        "# purrTTY kFS settings: the x_ services extension, the Neovim launcher and the language server.\n" +
        "\n" +
        "# enabled: install the x_ services for lights and part animations in kFS programs (true) or\n" +
        "# leave stock kFS untouched (false). Read once when the game starts; restart the game after\n" +
        "# a change.\n" +
        "enabled = true\n" +
        "\n" +
        "# Neovim (purrTTY menu > kFS > Edit in Neovim, or purr nvim <file>). These two keys are\n" +
        "# read at each launch, no restart needed.\n" +
        "# nvim_path: absolute path of the nvim executable. Empty: search the game's PATH, then\n" +
        "# (macOS/Linux) your login shell's PATH. Windows: point it at nvim.exe, not a .cmd shim,\n" +
        "# in single quotes so the backslashes stay: 'C:\\Program Files\\Neovim\\bin\\nvim.exe'.\n" +
        "nvim_path = \"\"\n" +
        "# nvim_open_in: \"window\" (a purrTTY window) or \"inworld\" (an in-world terminal).\n" +
        "nvim_open_in = \"window\"\n" +
        "\n" +
        "# The kC/kASM language server inside the game (Neovim attaches to it). These three keys are\n" +
        "# read when the server starts: at game start, or after `purr lsp restart`.\n" +
        "# lsp_enabled: run it (true) or not (false).\n" +
        "lsp_enabled = true\n" +
        "# lsp_port: its port on 127.0.0.1; 0 picks a free one each time (the address is in purr lsp\n" +
        "# and in .purrTTY/kfs-lsp.json).\n" +
        "lsp_port = 0\n" +
        "# lsp_allow_vehicle_commands: let the editor run ANY kfs command (kfs.verb). Build, load,\n" +
        "# start, stop, reset, target, manual and parts work without it.\n" +
        "lsp_allow_vehicle_commands = false\n";

    /// <summary>Install the extension at start-up.</summary>
    public bool Enabled { get; private init; } = true;

    /// <summary>The absolute path of <c>nvim</c>; empty: search for it (<see cref="NeovimLaunchPlan"/>).</summary>
    public string NvimPath { get; private init; } = "";

    /// <summary>Where Neovim opens.</summary>
    public NeovimOpenIn NvimOpenIn { get; private init; } = NeovimOpenIn.Window;

    /// <summary>Run the in-game language server.</summary>
    public bool LspEnabled { get; private init; } = true;

    /// <summary>The language server's port; 0 picks a free one.</summary>
    public int LspPort { get; private init; }

    /// <summary>Allow <c>kfs.verb</c> (any <c>kfs</c> line from the editor).</summary>
    public bool LspAllowVehicleCommands { get; private init; }

    /// <summary>The defaults.</summary>
    public static KfsConfig Defaults { get; } = new();

    /// <summary>Loads <see cref="DefaultPath"/> (writing the defaults when it does not exist). Never throws.</summary>
    public static KfsConfig Load()
    {
        string path;
        try
        {
            path = DefaultPath();
        }
        catch (Exception ex)
        {
            Log($"purrTTY kFS: no config location ({ex.Message}); using defaults");
            return Defaults;
        }
        return Load(path, Log);
    }

    /// <summary>Loads <paramref name="path"/>; problems go to <paramref name="log"/>, one line each. Never throws.</summary>
    internal static KfsConfig Load(string path, Action<string> log)
    {
        try
        {
            if (!File.Exists(path))
            {
                WriteDefaults(path, log);
                return Defaults;
            }
            return Parse(File.ReadAllText(path), path, log);
        }
        catch (Exception ex)
        {
            log($"purrTTY kFS: cannot read {path} ({ex.Message}); using defaults");
            return Defaults;
        }
    }

    /// <summary>Parses the file's text; a problem gives the defaults for what could not be read and one log line.</summary>
    internal static KfsConfig Parse(string toml, string pathForMessages, Action<string> log)
    {
        TomlTable table;
        try
        {
            table = TomlSerializer.Deserialize<TomlTable>(toml) ?? new TomlTable();
        }
        catch (Exception ex)
        {
            log($"purrTTY kFS: {pathForMessages} is not valid TOML ({FirstLine(ex.Message)}); using defaults");
            return Defaults;
        }

        bool enabled = Defaults.Enabled;
        if (table.TryGetValue("enabled", out object? value))
        {
            if (value is bool b) enabled = b;
            else log($"purrTTY kFS: {pathForMessages}: 'enabled' must be true or false; using {(Defaults.Enabled ? "true" : "false")}");
        }

        string nvimPath = Defaults.NvimPath;
        if (table.TryGetValue("nvim_path", out value))
        {
            if (value is string p) nvimPath = p.Trim();
            else log($"purrTTY kFS: {pathForMessages}: 'nvim_path' must be a string (\"/path/to/nvim\"); searching for nvim instead");
        }

        NeovimOpenIn openIn = Defaults.NvimOpenIn;
        if (table.TryGetValue("nvim_open_in", out value))
        {
            if (value is string o && NeovimLaunchPlan.TryParseOpenIn(o, out NeovimOpenIn parsed)) openIn = parsed;
            else log($"purrTTY kFS: {pathForMessages}: 'nvim_open_in' must be \"window\" or \"inworld\"; using \"window\"");
        }
        bool lspEnabled = ReadBool(table, "lsp_enabled", Defaults.LspEnabled, pathForMessages, log);
        bool allowVehicle = ReadBool(table, "lsp_allow_vehicle_commands", Defaults.LspAllowVehicleCommands, pathForMessages, log);

        int lspPort = Defaults.LspPort;
        if (table.TryGetValue("lsp_port", out value))
        {
            if (value is long n && n is >= 0 and <= 65535) lspPort = (int)n;
            else log($"purrTTY kFS: {pathForMessages}: 'lsp_port' must be a whole number from 0 to 65535 (0 picks a free port); using 0");
        }
        return new KfsConfig
        {
            Enabled = enabled, NvimPath = nvimPath, NvimOpenIn = openIn,
            LspEnabled = lspEnabled, LspPort = lspPort, LspAllowVehicleCommands = allowVehicle,
        };
    }

    private static bool ReadBool(TomlTable table, string key, bool fallback, string pathForMessages, Action<string> log)
    {
        if (!table.TryGetValue(key, out object? value)) return fallback;
        if (value is bool b) return b;
        log($"purrTTY kFS: {pathForMessages}: '{key}' must be true or false; using {(fallback ? "true" : "false")}");
        return fallback;
    }

    /// <summary>
    /// <c>&lt;config&gt;/.purrTTY/kfs.toml</c>, where <c>&lt;config&gt;</c> is the directory
    /// <c>purrtty.toml</c> lives in. The kFS extension is installed from <c>Patcher.patch()</c>,
    /// before <c>TerminalMod</c> sets <see cref="ThemeConfiguration.OverrideConfigDirectory"/>, so
    /// the production root is derived here the same way (<c>TerminalMod.SetProductionConfigPath</c>:
    /// <c>My Documents/My Games/Kitten Space Agency</c>) when no override is set yet.
    /// </summary>
    public static string DefaultPath()
    {
        string? root = ThemeConfiguration.OverrideConfigDirectory;
        if (string.IsNullOrEmpty(root))
        {
            string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (!string.IsNullOrEmpty(documents)) root = Path.Combine(documents, "My Games", "Kitten Space Agency");
        }
        string directory = string.IsNullOrEmpty(root)
            ? Path.GetDirectoryName(ThemeConfiguration.GetConfigFilePath()) ?? Environment.CurrentDirectory
            : Path.Combine(root, ".purrTTY");
        return Path.Combine(directory, FileName);
    }

    /// <summary>
    /// The folder that contains <c>.purrTTY/</c> (the parent of <see cref="DefaultPath"/>'s folder):
    /// what <c>PURRTTY_CONFIG_DIR</c> means to the Neovim runtime files, which read the language
    /// server's info file at <c>$PURRTTY_CONFIG_DIR/.purrTTY/kfs-lsp.json</c>. Null when it cannot be
    /// determined. The info file (<see cref="KfsLspService"/>) is written in the same folder as this file.
    /// </summary>
    public static string? ConfigRoot()
    {
        try
        {
            string? purrtty = Path.GetDirectoryName(DefaultPath());
            string? root = string.IsNullOrEmpty(purrtty) ? null : Path.GetDirectoryName(purrtty);
            return string.IsNullOrEmpty(root) ? null : Path.GetFullPath(root);
        }
        catch (Exception ex)
        {
            Log($"purrTTY kFS: no config location ({ex.Message})");
            return null;
        }
    }

    private static void WriteDefaults(string path, Action<string> log)
    {
        try
        {
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            // Crash-safe like purrTTY.Display's (internal) AtomicFile: a sibling temp file renamed
            // over the destination, so an interrupted write never leaves a truncated file.
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, DefaultText);
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception ex)
        {
            log($"purrTTY kFS: cannot write {path} ({ex.Message}); using defaults");
        }
    }

    private static string FirstLine(string text)
    {
        int newline = text.IndexOfAny(['\r', '\n']);
        return newline < 0 ? text : text[..newline];
    }

    private static void Log(string message)
    {
        try { ModLog.Log.Info($"{message}"); }
        catch { /* logging never fails the mod */ }
    }
}
