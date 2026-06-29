using purrTTY.Display.Configuration;
using purrTTY.Logging;
using Tomlyn;
using Tomlyn.Model;
using Tomlyn.Serialization;

namespace purrTTY.GameMod.InWorld.Settings;

/// <summary>
///     Persisted settings for the in-world (render-to-texture) terminal feature.
///     Two anchor modes share the same quad: <see cref="ModePart"/> anchors to a
///     vehicle Part/SubPart with offset/rotation/scale (occludes correctly), and
///     <see cref="ModeBillboard"/> pins the quad in the camera's view (HUD panel).
///     <para>
///         Persisted as TOML beside the theme config (<c>.purrTTY/purrtty-inworld.toml</c>)
///         under <see cref="ThemeConfiguration.OverrideConfigDirectory"/>, using the
///         same Tomlyn round-trip. Properties are public get/set primitives so the
///         round-trip is trivial.
///     </para>
/// </summary>
public sealed class InWorldSettings
{
    public const string ModePart = "part";
    public const string ModeBillboard = "billboard";

    private const string FileName = "purrtty-inworld.toml";

    private static readonly TomlSerializerOptions TomlOptions = new();

    /// <summary>Master toggle: whether the in-world terminal is active.</summary>
    public bool Enabled { get; set; }

    /// <summary>Anchor mode: <see cref="ModePart"/> or <see cref="ModeBillboard"/>.</summary>
    public string Mode { get; set; } = ModePart;

    /// <summary>Off-screen texture resolution (fixed at build time; runtime resize is deferred).</summary>
    public int TextureWidth { get; set; } = 1024;
    public int TextureHeight { get; set; } = 1024;

    // --- Part-anchored mode ---

    /// <summary>Id of the anchor Part/SubPart. Empty = the controlled vessel's first part.</summary>
    public string TargetPartId { get; set; } = "";

    public float PartOffsetX { get; set; }
    public float PartOffsetY { get; set; }
    public float PartOffsetZ { get; set; } = 2f;
    public float PartRotationX { get; set; }
    public float PartRotationY { get; set; }
    public float PartRotationZ { get; set; }
    public float PartWidthMeters { get; set; } = 2f;
    public float PartHeightMeters { get; set; } = 2f;

    // --- Camera-billboard mode ---

    /// <summary>Distance from the camera, in metres, the billboard panel sits at.</summary>
    public float BillboardDistance { get; set; } = 5f;
    public float BillboardOffsetX { get; set; }
    public float BillboardOffsetY { get; set; }
    public float BillboardWidthMeters { get; set; } = 3f;
    public float BillboardHeightMeters { get; set; } = 2f;

    /// <summary>When true the billboard ignores depth (always-on-top HUD); otherwise it is occluded.</summary>
    public bool BillboardAlwaysOnTop { get; set; } = true;

    /// <summary>True when <see cref="Mode"/> selects the camera-billboard anchor.</summary>
    public bool IsBillboard => string.Equals(Mode, ModeBillboard, StringComparison.OrdinalIgnoreCase);

    public static InWorldSettings LoadOrDefault()
    {
        try
        {
            string path = GetConfigFilePath();
            if (!File.Exists(path))
            {
                return new InWorldSettings();
            }

            string toml = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(toml))
            {
                return new InWorldSettings();
            }

            return TomlSerializer.Deserialize<InWorldSettings>(toml, TomlOptions) ?? new InWorldSettings();
        }
        catch (Exception ex)
        {
            ModLog.Log.Debug($"purrTTY in-world: failed to load settings, using defaults ({ex.Message})");
            return new InWorldSettings();
        }
    }

    public void Save()
    {
        try
        {
            string path = GetConfigFilePath();
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string toml = TomlSerializer.Serialize(this, TomlOptions);

            // Atomic write: temp file then replace, so a crash mid-write can't
            // leave a truncated config.
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, toml);
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception ex)
        {
            ModLog.Log.Debug($"purrTTY in-world: failed to save settings ({ex.Message})");
        }
    }

    // Co-located with the theme config under the same override directory (set by
    // TerminalMod), mirroring ThemeConfiguration.GetConfigFilePath.
    private static string GetConfigFilePath()
    {
        string baseDir = ThemeConfiguration.OverrideConfigDirectory is { Length: > 0 } overridden
            ? overridden
            : Path.Combine(Path.GetTempPath(), "purrTTY_config_default");
        return Path.Combine(baseDir, ".purrTTY", FileName);
    }
}
