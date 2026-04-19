namespace caTTY.Core.Terminal;

/// <summary>
///     Options for starting a custom shell.
/// </summary>
public class CustomShellStartOptions
{
    /// <summary>
    ///     Gets or sets the initial terminal width in columns.
    /// </summary>
    public int InitialWidth { get; set; } = 80;

    /// <summary>
    ///     Gets or sets the initial terminal height in rows.
    /// </summary>
    public int InitialHeight { get; set; } = 24;

    /// <summary>
    ///     Gets or sets the working directory for the shell.
    ///     If null, uses the current process working directory.
    /// </summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>
    ///     Gets or sets environment variables for the shell.
    ///     These will be available to the custom shell implementation.
    /// </summary>
    public Dictionary<string, string> EnvironmentVariables { get; set; } = new();

    /// <summary>
    ///     Gets or sets additional configuration data for the shell.
    ///     This can be used to pass shell-specific configuration.
    /// </summary>
    public Dictionary<string, object> Configuration { get; set; } = new();

    /// <summary>
    ///     Creates default start options for a custom shell.
    /// </summary>
    /// <returns>Default start options</returns>
    public static CustomShellStartOptions CreateDefault()
    {
        var options = new CustomShellStartOptions
        {
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        };

        // Add common environment variables
        options.EnvironmentVariables["TERM"] = "xterm-256color";
        options.EnvironmentVariables["TERM_PROGRAM"] = "caTTY";
        options.EnvironmentVariables["COLORTERM"] = "truecolor";
        options.EnvironmentVariables["FORCE_COLOR"] = "1";
        options.EnvironmentVariables["CLICOLOR_FORCE"] = "1";

        return options;
    }

    /// <summary>
    ///     Creates start options with specific terminal dimensions.
    /// </summary>
    /// <param name="width">Terminal width in columns</param>
    /// <param name="height">Terminal height in rows</param>
    /// <returns>Start options with specified dimensions</returns>
    public static CustomShellStartOptions CreateWithDimensions(int width, int height)
    {
        var options = CreateDefault();
        options.InitialWidth = width;
        options.InitialHeight = height;
        return options;
    }

    /// <summary>
    ///     Creates start options with a specific working directory.
    /// </summary>
    /// <param name="workingDirectory">The working directory</param>
    /// <returns>Start options with specified working directory</returns>
    public static CustomShellStartOptions CreateWithWorkingDirectory(string workingDirectory)
    {
        var options = CreateDefault();
        options.WorkingDirectory = workingDirectory;
        return options;
    }
}