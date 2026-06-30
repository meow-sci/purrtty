using purrTTY.Core.Terminal;

namespace purrTTY.Display.Layouts;

/// <summary>
/// Serializable shell description stored inside a saved layout (the on-disk mapping
/// lives in <see cref="LayoutTomlFormat"/>): enough to rebuild the shell (type +
/// custom path/id + args + working directory) plus the optional
/// <see cref="StartupCommand"/> that auto-runs once the shell starts. The standard
/// environment (TERM, etc.) is re-applied by <see cref="ToLaunchOptions"/> from the
/// platform defaults, so it is not persisted.
/// </summary>
public sealed class ShellSpec
{
    public ShellType ShellType { get; set; } = ShellType.Auto;
    public string? CustomShellPath { get; set; }
    public string? CustomShellId { get; set; }
    public List<string> Arguments { get; set; } = new();
    public string? WorkingDirectory { get; set; }
    public string? StartupCommand { get; set; }

    /// <summary>Rebuilds launch options on top of the platform defaults (standard env preserved).</summary>
    public ProcessLaunchOptions ToLaunchOptions()
    {
        var options = ProcessLaunchOptions.CreateDefault();
        options.ShellType = ShellType;
        options.CustomShellPath = CustomShellPath;
        options.CustomShellId = CustomShellId;
        options.Arguments = new List<string>(Arguments);
        if (!string.IsNullOrEmpty(WorkingDirectory))
        {
            options.WorkingDirectory = WorkingDirectory;
        }

        options.StartupCommand = string.IsNullOrEmpty(StartupCommand) ? null : StartupCommand;
        return options;
    }

    /// <summary>Captures a shell spec from live launch options (null = the default shell).</summary>
    public static ShellSpec From(ProcessLaunchOptions? options)
    {
        if (options is null)
        {
            return new ShellSpec();
        }

        return new ShellSpec
        {
            ShellType = options.ShellType,
            CustomShellPath = options.CustomShellPath,
            CustomShellId = options.CustomShellId,
            Arguments = new List<string>(options.Arguments),
            WorkingDirectory = options.WorkingDirectory,
            StartupCommand = options.StartupCommand,
        };
    }
}
