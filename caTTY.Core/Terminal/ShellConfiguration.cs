namespace caTTY.Core.Terminal;

/// <summary>
///     Helper class for easy shell configuration and switching.
///     Provides convenient methods to create common shell configurations.
/// </summary>
public static class ShellConfiguration
{
    /// <summary>
    ///     Creates launch options for the default shell (WSL2 on Windows).
    /// </summary>
    /// <returns>Default shell launch options</returns>
    public static ProcessLaunchOptions Default() => ProcessLaunchOptions.CreateDefault();

    /// <summary>
    ///     Creates launch options for WSL2 with default distribution.
    /// </summary>
    /// <returns>WSL2 launch options</returns>
    public static ProcessLaunchOptions Wsl() => ProcessLaunchOptions.CreateWsl();

    /// <summary>
    ///     Creates launch options for WSL2 with specific distribution.
    /// </summary>
    /// <param name="distribution">WSL distribution name (e.g., "Ubuntu", "Debian")</param>
    /// <returns>WSL2 launch options with specified distribution</returns>
    public static ProcessLaunchOptions Wsl(string distribution) => ProcessLaunchOptions.CreateWsl(distribution);

    /// <summary>
    ///     Creates launch options for WSL2 with specific distribution and working directory.
    /// </summary>
    /// <param name="distribution">WSL distribution name (e.g., "Ubuntu", "Debian")</param>
    /// <param name="workingDirectory">Working directory within WSL (e.g., "/home/username")</param>
    /// <returns>WSL2 launch options with specified distribution and directory</returns>
    public static ProcessLaunchOptions Wsl(string distribution, string workingDirectory) => 
        ProcessLaunchOptions.CreateWsl(distribution, workingDirectory);

    /// <summary>
    ///     Creates launch options for Windows PowerShell.
    /// </summary>
    /// <returns>PowerShell launch options</returns>
    public static ProcessLaunchOptions PowerShell() => ProcessLaunchOptions.CreatePowerShell();

    /// <summary>
    ///     Creates launch options for PowerShell Core (pwsh).
    /// </summary>
    /// <returns>PowerShell Core launch options</returns>
    public static ProcessLaunchOptions PowerShellCore() => ProcessLaunchOptions.CreatePowerShellCore();

    /// <summary>
    ///     Creates launch options for Windows Command Prompt.
    /// </summary>
    /// <returns>Command Prompt launch options</returns>
    public static ProcessLaunchOptions Cmd() => ProcessLaunchOptions.CreateCmd();

    /// <summary>
    ///     Creates launch options for a custom shell.
    /// </summary>
    /// <param name="shellPath">Path to the shell executable</param>
    /// <param name="arguments">Arguments to pass to the shell</param>
    /// <returns>Custom shell launch options</returns>
    public static ProcessLaunchOptions Custom(string shellPath, params string[] arguments) => 
        ProcessLaunchOptions.CreateCustom(shellPath, arguments);

    /// <summary>
    ///     Creates launch options for a custom game shell.
    /// </summary>
    /// <param name="customShellId">ID of the custom game shell to launch</param>
    /// <returns>Custom game shell launch options</returns>
    public static ProcessLaunchOptions CustomGame(string customShellId) => 
        ProcessLaunchOptions.CreateCustomGame(customShellId);

    /// <summary>
    ///     Common shell configurations for quick access.
    /// </summary>
    public static class Common
    {
        /// <summary>
        ///     Ubuntu WSL2 configuration.
        /// </summary>
        public static ProcessLaunchOptions Ubuntu => Wsl("Ubuntu");

        /// <summary>
        ///     Debian WSL2 configuration.
        /// </summary>
        public static ProcessLaunchOptions Debian => Wsl("Debian");

        /// <summary>
        ///     Alpine WSL2 configuration.
        /// </summary>
        public static ProcessLaunchOptions Alpine => Wsl("Alpine");

        /// <summary>
        ///     Git Bash configuration (if installed).
        /// </summary>
        public static ProcessLaunchOptions GitBash => Custom(@"C:\Program Files\Git\bin\bash.exe", "--login");

        /// <summary>
        ///     MSYS2 Bash configuration (if installed).
        /// </summary>
        public static ProcessLaunchOptions Msys2Bash => Custom(@"C:\msys64\usr\bin\bash.exe", "--login");

        /// <summary>
        ///     Cygwin Bash configuration (if installed).
        /// </summary>
        public static ProcessLaunchOptions CygwinBash => Custom(@"C:\cygwin64\bin\bash.exe", "--login");
    }
}