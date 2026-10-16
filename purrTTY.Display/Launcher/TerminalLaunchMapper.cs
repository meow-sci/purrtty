using purrTTY.Core.Terminal;
using purrTTY.Display.Ghostty;
using float2 = Brutal.Numerics.float2;

namespace purrTTY.Display.Launcher;

/// <summary>
/// Pure request-to-purrTTY mapping for the cross-mod terminal launcher: contract POCOs
/// (<see cref="TerminalProgramSpec"/>, <see cref="TerminalWindowOptions"/>,
/// <see cref="TerminalInWorldOptions"/>) to launch options, a window layout record, and a
/// resolved in-world placement. No ImGui, no game types, no I/O — everything here is unit-tested in
/// <c>purrTTY.Display.Tests</c>. The GameMod launcher supplies the live inputs (font cell size,
/// chrome height, default launch options) and consumes the outputs.
/// </summary>
public static class TerminalLaunchMapper
{
    /// <summary>Smallest in-world grid width in columns (same bound as the in-world manager).</summary>
    public const int MinCols = 8;

    /// <summary>Largest in-world grid width in columns.</summary>
    public const int MaxCols = 400;

    /// <summary>Smallest in-world grid height in rows.</summary>
    public const int MinRows = 4;

    /// <summary>Largest in-world grid height in rows.</summary>
    public const int MaxRows = 200;

    /// <summary>Smallest in-world quad size in metres (the manager UI's drag minimum).</summary>
    public const float MinMeters = 0.05f;

    /// <summary>Largest in-world quad size in metres (the manager UI's drag maximum).</summary>
    public const float MaxMeters = 100f;

    /// <summary>Smallest billboard distance from the camera in metres.</summary>
    public const float MinBillboardDistance = 0.2f;

    /// <summary>Largest billboard distance from the camera in metres.</summary>
    public const float MaxBillboardDistance = 200f;

    // The contract defaults, used when a consumer passes a non-finite number.
    private const float DefaultWidthMeters = 0.6f;
    private const float DefaultHeightMeters = 0.2f;
    private const float DefaultBillboardDistance = 5f;

    /// <summary>
    /// Maps what should run in a terminal to <see cref="ProcessLaunchOptions"/>.
    /// <list type="number">
    /// <item>A non-blank <see cref="TerminalProgramSpec.CustomShellId"/> selects
    /// <see cref="ProcessLaunchOptions.CreateCustomGame"/>.</item>
    /// <item>Otherwise a non-blank <see cref="TerminalProgramSpec.Program"/> selects
    /// <see cref="ProcessLaunchOptions.CreateCustom"/> with the arguments.</item>
    /// <item>Otherwise <paramref name="defaultFactory"/> supplies the configured default shell
    /// (invoked only in this case; it must return a fresh, caller-owned instance).</item>
    /// </list>
    /// Then the working directory, startup command, environment (merged over the defaults, spec
    /// entries win) and custom-shell configuration are applied. Arguments are always honoured for a
    /// <c>Program</c>; for a custom shell or the default shell they are appended to whatever
    /// arguments that launch already carries (custom shells receive no argument vector, so this only
    /// affects the default shell).
    /// </summary>
    /// <param name="spec">What to run.</param>
    /// <param name="defaultFactory">Creates the configured default-shell launch options.</param>
    /// <returns>Launch options owned by the caller.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="spec"/> or <paramref name="defaultFactory"/> is null.</exception>
    public static ProcessLaunchOptions ToLaunchOptions(TerminalProgramSpec spec, Func<ProcessLaunchOptions> defaultFactory)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(defaultFactory);

        ProcessLaunchOptions options;
        if (!string.IsNullOrWhiteSpace(spec.CustomShellId))
        {
            options = ProcessLaunchOptions.CreateCustomGame(spec.CustomShellId);
            AppendArguments(options, spec.Arguments);
        }
        else if (!string.IsNullOrWhiteSpace(spec.Program))
        {
            options = ProcessLaunchOptions.CreateCustom(spec.Program, (spec.Arguments ?? new List<string>()).ToArray());
        }
        else
        {
            options = defaultFactory();
            AppendArguments(options, spec.Arguments);
        }

        if (spec.WorkingDirectory is not null)
        {
            options.WorkingDirectory = spec.WorkingDirectory;
        }

        if (spec.Environment is not null)
        {
            foreach (var pair in spec.Environment)
            {
                options.EnvironmentVariables[pair.Key] = pair.Value;
            }
        }

        if (spec.StartupCommand is not null)
        {
            options.StartupCommand = spec.StartupCommand;
        }

        if (spec.Configuration is not null)
        {
            foreach (var pair in spec.Configuration)
            {
                options.CustomShellConfiguration[pair.Key] = pair.Value;
            }
        }

        return options;
    }

    /// <summary>
    /// Resolves and clamps in-world options into an <see cref="InWorldPlacement"/>: the name falls
    /// back to <paramref name="fallbackName"/>; the grid is clamped to
    /// <see cref="MinCols"/>..<see cref="MaxCols"/> x <see cref="MinRows"/>..<see cref="MaxRows"/>;
    /// sizes to <see cref="MinMeters"/>..<see cref="MaxMeters"/>; the billboard distance to
    /// <see cref="MinBillboardDistance"/>..<see cref="MaxBillboardDistance"/>; opacities to 0..1;
    /// non-finite numbers fall back to their defaults. The mode is billboard when
    /// <paramref name="placement"/> is <see cref="TerminalPlacementKind.InWorldBillboard"/> or
    /// <see cref="TerminalInWorldOptions.Billboard"/> is set, otherwise part.
    /// </summary>
    /// <param name="options">The consumer's in-world options.</param>
    /// <param name="placement">The requested placement kind.</param>
    /// <param name="fallbackName">Name used when <see cref="TerminalInWorldOptions.Name"/> is blank.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is null.</exception>
    public static InWorldPlacement ToInWorldPlacement(
        TerminalInWorldOptions options, TerminalPlacementKind placement, string fallbackName)
    {
        ArgumentNullException.ThrowIfNull(options);

        bool billboard = placement == TerminalPlacementKind.InWorldBillboard || options.Billboard;
        return new InWorldPlacement
        {
            Name = string.IsNullOrWhiteSpace(options.Name) ? fallbackName : options.Name.Trim(),
            ThemeName = options.ThemeName,
            Cols = Math.Clamp(options.Cols, MinCols, MaxCols),
            Rows = Math.Clamp(options.Rows, MinRows, MaxRows),
            Mode = billboard ? InWorldPlacement.ModeBillboard : InWorldPlacement.ModePart,
            VehicleId = options.VehicleId ?? string.Empty,
            PartId = options.PartId ?? string.Empty,
            PartInstanceId = options.PartInstanceId,
            SubPartId = options.SubPartId ?? string.Empty,
            OffsetX = Finite(options.OffsetX, 0f),
            OffsetY = Finite(options.OffsetY, 0f),
            OffsetZ = Finite(options.OffsetZ, 0f),
            RotationX = Finite(options.RotationX, 0f),
            RotationY = Finite(options.RotationY, 0f),
            RotationZ = Finite(options.RotationZ, 0f),
            WidthMeters = Math.Clamp(Finite(options.WidthMeters, DefaultWidthMeters), MinMeters, MaxMeters),
            HeightMeters = Math.Clamp(Finite(options.HeightMeters, DefaultHeightMeters), MinMeters, MaxMeters),
            BackgroundOpacity = ClampOpacity(options.BackgroundOpacity),
            ForegroundOpacity = ClampOpacity(options.ForegroundOpacity),
            CellBackgroundOpacity = ClampOpacity(options.CellBackgroundOpacity),
            BillboardDistance = Math.Clamp(
                Finite(options.BillboardDistance, DefaultBillboardDistance), MinBillboardDistance, MaxBillboardDistance),
            BillboardAlwaysOnTop = options.BillboardAlwaysOnTop,
            BillboardClickToFocus = options.BillboardClickToFocus,
            Focus = options.Focus,
        };
    }

    /// <summary>
    /// Approximate outer window size in pixels for a requested grid:
    /// <c>width = cols * cellWidth + 2 * padding</c>,
    /// <c>height = rows * cellHeight + chromeHeight + 2 * padding</c>, each rounded up. Columns and
    /// rows are clamped to at least 1.
    /// </summary>
    /// <param name="cols">Requested columns.</param>
    /// <param name="rows">Requested rows.</param>
    /// <param name="cellWidth">Font cell width in pixels.</param>
    /// <param name="cellHeight">Font cell height in pixels.</param>
    /// <param name="chromeHeight">Height of the always-laid-out chrome above the grid (the title strip).</param>
    /// <param name="padding">Window padding per side.</param>
    public static (float Width, float Height) WindowPixelSize(
        int cols, int rows, float cellWidth, float cellHeight, float chromeHeight, float padding)
    {
        cols = Math.Max(1, cols);
        rows = Math.Max(1, rows);
        float width = cols * cellWidth + 2f * padding;
        float height = rows * cellHeight + chromeHeight + 2f * padding;
        return (MathF.Ceiling(width), MathF.Ceiling(height));
    }

    /// <summary>
    /// Builds the layout record <c>GhosttyTerminalController.CreateConfiguredWindow</c> consumes.
    /// Geometry precedence: an explicit position needs both X and Y; an explicit size needs both
    /// width and height and wins over the grid; otherwise <paramref name="pixelSizeFromGrid"/> is
    /// used when both <see cref="TerminalWindowOptions.Cols"/> and <see cref="TerminalWindowOptions.Rows"/>
    /// were requested; otherwise the size stays null.
    /// </summary>
    /// <param name="options">Window options, or null for all defaults.</param>
    /// <param name="launch">The mapped launch options.</param>
    /// <param name="fallbackName">Name used when the options carry no name.</param>
    /// <param name="pixelSizeFromGrid">The pixel size for the requested grid (see <see cref="WindowPixelSize"/>), if computed.</param>
    public static WindowLayoutRecord ToWindowRecord(
        TerminalWindowOptions? options,
        ProcessLaunchOptions launch,
        string fallbackName,
        (float W, float H)? pixelSizeFromGrid)
    {
        ArgumentNullException.ThrowIfNull(launch);

        float2? position = null;
        float2? size = null;
        if (options is not null)
        {
            if (options.PosX is { } x && options.PosY is { } y)
            {
                position = new float2(x, y);
            }

            if (options.Width is { } w && options.Height is { } h)
            {
                size = new float2(w, h);
            }
            else if (options.Cols.HasValue && options.Rows.HasValue && pixelSizeFromGrid is { } grid)
            {
                size = new float2(grid.W, grid.H);
            }
        }

        return new WindowLayoutRecord
        {
            Name = options is not null && !string.IsNullOrWhiteSpace(options.Name) ? options.Name.Trim() : fallbackName,
            Position = position,
            Size = size,
            ThemeName = options?.ThemeName,
            Launch = launch,
        };
    }

    private static void AppendArguments(ProcessLaunchOptions options, List<string>? arguments)
    {
        if (arguments is { Count: > 0 })
        {
            options.Arguments.AddRange(arguments);
        }
    }

    private static float Finite(float value, float fallback) => float.IsFinite(value) ? value : fallback;

    private static float? ClampOpacity(float? value)
        => value is { } v && float.IsFinite(v) ? Math.Clamp(v, 0f, 1f) : null;
}
