namespace purrTTY.Core.Terminal;

/// <summary>
///     Shell type enumeration for process launching.
/// </summary>
public enum ShellType
{
    /// <summary>
    ///     Automatically detect the best shell for the current platform.
    /// </summary>
    Auto,

    /// <summary>
    ///     Windows PowerShell (powershell.exe).
    /// </summary>
    PowerShell,

    /// <summary>
    ///     Windows Subsystem for Linux (wsl.exe). Preferred by <see cref="Auto"/>
    ///     on Windows when at least one distribution is detected.
    /// </summary>
    Wsl,

    /// <summary>
    ///     PowerShell Core (pwsh.exe).
    /// </summary>
    PowerShellCore,

    /// <summary>
    ///     Windows Command Prompt (cmd.exe).
    /// </summary>
    Cmd,

    /// <summary>
    ///     Custom shell with explicit path.
    /// </summary>
    Custom,

    /// <summary>
    ///     Custom game shell implementation.
    /// </summary>
    CustomGame
}

/// <summary>
///     Options for launching a shell process.
/// </summary>
public class ProcessLaunchOptions
{
    /// <summary>
    ///     Gets or sets the shell type to launch.
    /// </summary>
    public ShellType ShellType { get; set; } = ShellType.Auto;

    /// <summary>
    ///     Gets or sets the custom shell path (used when ShellType is Custom).
    /// </summary>
    public string? CustomShellPath { get; set; }

    /// <summary>
    ///     Gets or sets the custom shell ID (used when ShellType is CustomGame).
    /// </summary>
    public string? CustomShellId { get; set; }

    /// <summary>
    ///     Gets or sets additional arguments to pass to the shell.
    /// </summary>
    public List<string> Arguments { get; set; } = new();

    /// <summary>
    ///     Gets or sets the working directory for the shell process.
    ///     If null, uses the current process working directory.
    /// </summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>
    ///     Gets or sets additional environment variables for the shell process.
    ///     These will be added to or override the current process environment.
    /// </summary>
    public Dictionary<string, string> EnvironmentVariables { get; set; } = new();

    /// <summary>
    ///     Gets or sets the initial terminal width in columns.
    /// </summary>
    public int InitialWidth { get; set; } = 80;

    /// <summary>
    ///     Gets or sets the initial terminal height in rows.
    /// </summary>
    public int InitialHeight { get; set; } = 24;

    /// <summary>
    ///     Optional command typed into the shell (as stdin, followed by a newline) once it
    ///     has started — used to auto-launch a TUI/program on connect. Works for every shell
    ///     type, including the gatOS SSH custom shell, because the interactive login PTY is
    ///     already up with its env and size. Null/empty means nothing is sent.
    /// </summary>
    public string? StartupCommand { get; set; }

    /// <summary>
    ///     Creates a copy with independent argument/environment collections.
    ///     Session managers clone before stamping per-session dimensions so a
    ///     shared default (or caller-cached) instance is never mutated.
    /// </summary>
    public ProcessLaunchOptions Clone() => new()
    {
        ShellType = ShellType,
        CustomShellPath = CustomShellPath,
        CustomShellId = CustomShellId,
        Arguments = new List<string>(Arguments),
        WorkingDirectory = WorkingDirectory,
        EnvironmentVariables = new Dictionary<string, string>(EnvironmentVariables),
        InitialWidth = InitialWidth,
        InitialHeight = InitialHeight,
        StartupCommand = StartupCommand,
    };

    /// <summary>
    ///     Creates default launch options for the current platform.
    /// </summary>
    /// <returns>Default launch options</returns>
    public static ProcessLaunchOptions CreateDefault()
    {
        var options = new ProcessLaunchOptions();

        // Set platform-specific defaults
        if (OperatingSystem.IsWindows())
        {
            // PowerShell: present on every supported Windows, no WSL setup needed
            options.ShellType = ShellType.PowerShell;
            options.WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }
        else if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            options.ShellType = ShellType.Auto; // Will detect bash/zsh/sh
            options.WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        // Add common environment variables.
        //
        // Advertise as Ghostty: purrTTY's VT engine *is* libghostty-vt (Ghostty's
        // engine) and implements the kitty graphics + kitty keyboard protocols, so
        // capability detectors should treat us as Ghostty. Image tools key off the
        // terminal identity, NOT an escape-sequence probe — chafa matches
        // TERM_PROGRAM=ghostty exactly to enable the kitty graphics protocol; without
        // it chafa/viu/yazi fall back to sixel/ASCII even though our kitty rendering
        // works. We keep TERM=xterm-256color (universally present) rather than
        // xterm-ghostty so no host/guest ever needs Ghostty terminfo and ncurses apps
        // don't degrade. NOTE: TERM_PROGRAM is not forwarded over SSH — for a remote
        // (e.g. gatOS) guest it must also be exported guest-side.
        options.EnvironmentVariables["TERM"] = "xterm-256color";
        options.EnvironmentVariables["TERM_PROGRAM"] = "ghostty";
        options.EnvironmentVariables["TERM_PROGRAM_VERSION"] = "1.3.2";
        options.EnvironmentVariables["COLORTERM"] = "truecolor";
        options.EnvironmentVariables["FORCE_COLOR"] = "1";
        options.EnvironmentVariables["CLICOLOR_FORCE"] = "1";

        return options;
    }

    /// <summary>
    ///     Creates launch options for PowerShell on Windows.
    /// </summary>
    /// <returns>PowerShell launch options</returns>
    public static ProcessLaunchOptions CreatePowerShell()
    {
        ProcessLaunchOptions options = CreateDefault();
        options.ShellType = ShellType.PowerShell;
        options.Arguments.Clear();
        // options.Arguments.AddRange([]);
        return options;
    }

    /// <summary>
    ///     Creates launch options for PowerShell Core (pwsh).
    /// </summary>
    /// <returns>PowerShell Core launch options</returns>
    public static ProcessLaunchOptions CreatePowerShellCore()
    {
        ProcessLaunchOptions options = CreateDefault();
        options.ShellType = ShellType.PowerShellCore;
        options.Arguments.Clear();
        options.Arguments.AddRange(["-NoLogo", "-NoProfile"]);
        return options;
    }

    /// <summary>
    ///     Creates launch options for Windows Command Prompt.
    /// </summary>
    /// <returns>Command Prompt launch options</returns>
    public static ProcessLaunchOptions CreateCmd()
    {
        ProcessLaunchOptions options = CreateDefault();
        options.ShellType = ShellType.Cmd;
        options.Arguments.Clear();
        return options;
    }

    /// <summary>
    ///     Creates launch options for Windows Subsystem for Linux (WSL2).
    /// </summary>
    /// <param name="distribution">Optional WSL distribution name (e.g., "Ubuntu", "Debian"). If null, uses default distribution.</param>
    /// <param name="workingDirectory">Optional working directory within WSL. If null, uses user home directory.</param>
    /// <returns>WSL launch options</returns>
    public static ProcessLaunchOptions CreateWsl(string? distribution = null, string? workingDirectory = null)
    {
        ProcessLaunchOptions options = CreateDefault();
        options.ShellType = ShellType.Wsl;
        options.Arguments.Clear();
        
        // Add distribution argument if specified
        if (!string.IsNullOrEmpty(distribution))
        {
            options.Arguments.AddRange(["--distribution", distribution]);
        }
        
        // Add working directory argument if specified
        if (!string.IsNullOrEmpty(workingDirectory))
        {
            options.Arguments.AddRange(["--cd", workingDirectory]);
        }
        
        return options;
    }

    /// <summary>
    ///     Creates launch options for a custom shell.
    /// </summary>
    /// <param name="shellPath">Path to the shell executable</param>
    /// <param name="arguments">Arguments to pass to the shell</param>
    /// <returns>Custom shell launch options</returns>
    public static ProcessLaunchOptions CreateCustom(string shellPath, params string[] arguments)
    {
        ProcessLaunchOptions options = CreateDefault();
        options.ShellType = ShellType.Custom;
        options.CustomShellPath = shellPath;
        options.Arguments.Clear();
        options.Arguments.AddRange(arguments);
        return options;
    }

    /// <summary>
    ///     Creates launch options for a custom game shell.
    /// </summary>
    /// <param name="customShellId">ID of the custom game shell to launch</param>
    /// <returns>Custom game shell launch options</returns>
    public static ProcessLaunchOptions CreateCustomGame(string customShellId)
    {
        ProcessLaunchOptions options = CreateDefault();
        options.ShellType = ShellType.CustomGame;
        options.CustomShellId = customShellId;
        options.Arguments.Clear();
        return options;
    }
}
