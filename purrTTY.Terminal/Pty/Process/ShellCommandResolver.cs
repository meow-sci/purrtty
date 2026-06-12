namespace purrTTY.Core.Terminal.Process;

/// <summary>
///     Resolves shell commands and paths for different shell types.
///     Handles auto-detection, WSL, PowerShell, cmd, and custom shell paths.
/// </summary>
internal static class ShellCommandResolver
{
    /// <summary>
    ///     Resolves the shell command and arguments based on the launch options, built
    ///     into a single CreateProcess command line with each token quoted per the
    ///     Windows argv rules — a bare-space join is the join-then-split bug the Unix
    ///     side documents: an unquoted <c>C:\Program Files\...</c> path or any argument
    ///     containing spaces gets re-tokenized by the child.
    /// </summary>
    /// <param name="options">The launch options</param>
    /// <returns>A tuple of shell path and the full quoted command line (including argv[0])</returns>
    /// <exception cref="ProcessStartException">Thrown if the shell cannot be resolved</exception>
    internal static (string shellPath, string commandLine) ResolveShellCommandLine(ProcessLaunchOptions options)
    {
        (string shellPath, string[] argv) = ResolveShellCommandArgv(options);

        var commandLine = new System.Text.StringBuilder();
        AppendQuotedArgument(commandLine, shellPath);
        foreach (string argument in argv)
        {
            commandLine.Append(' ');
            AppendQuotedArgument(commandLine, argument);
        }

        return (shellPath, commandLine.ToString());
    }

    private static readonly char[] CharsRequiringQuotes = [' ', '\t', '"'];

    /// <summary>
    ///     Appends one argument quoted per the MSVCRT/CommandLineToArgvW rules:
    ///     backslashes are literal except when they precede a double quote, where each
    ///     is doubled and the quote itself is backslash-escaped.
    /// </summary>
    internal static void AppendQuotedArgument(System.Text.StringBuilder builder, string argument)
    {
        if (argument.Length > 0 && argument.IndexOfAny(CharsRequiringQuotes) < 0)
        {
            builder.Append(argument);
            return;
        }

        builder.Append('"');
        int pendingBackslashes = 0;
        foreach (char c in argument)
        {
            if (c == '\\')
            {
                pendingBackslashes++;
                continue;
            }

            if (c == '"')
            {
                builder.Append('\\', pendingBackslashes * 2 + 1);
                builder.Append('"');
                pendingBackslashes = 0;
                continue;
            }

            builder.Append('\\', pendingBackslashes);
            builder.Append(c);
            pendingBackslashes = 0;
        }

        // Trailing backslashes must be doubled so they don't escape the closing quote.
        builder.Append('\\', pendingBackslashes * 2);
        builder.Append('"');
    }

    /// <summary>
    ///     Resolves the shell command with arguments kept as discrete argv entries
    ///     (Unix exec form — joining and re-splitting would corrupt arguments that
    ///     contain spaces, e.g. <c>-c "some script"</c>).
    /// </summary>
    /// <param name="options">The launch options</param>
    /// <returns>A tuple of shell path and argument vector (without argv[0])</returns>
    /// <exception cref="ProcessStartException">Thrown if the shell cannot be resolved</exception>
    internal static (string shellPath, string[] argv) ResolveShellCommandArgv(ProcessLaunchOptions options)
    {
        (string shellPath, var argsArray) = options.ShellType switch
        {
            ShellType.Auto => ResolveAutoShell(options),
            ShellType.Wsl => ResolveWsl(options),
            ShellType.PowerShell => ResolvePowerShell(options),
            ShellType.PowerShellCore => ResolvePowerShellCore(options),
            ShellType.Cmd => ResolveCmd(options),
            ShellType.Custom => ResolveCustomShell(options),
            ShellType.CustomGame => throw new ProcessStartException($"Custom game shells must be handled by CustomShellPtyBridge, not ProcessManager. ShellId: {options.CustomShellId}"),
            _ => throw new ProcessStartException($"Unsupported shell type: {options.ShellType}")
        };

        return (shellPath, argsArray ?? Array.Empty<string>());
    }

    /// <summary>
    ///     Resolves the best shell for the current platform automatically.
    /// </summary>
    private static (string shellPath, string[] arguments) ResolveAutoShell(ProcessLaunchOptions options)
    {
        if (OperatingSystem.IsWindows())
        {
            // Try WSL first (new default), then PowerShell, then cmd. WSL is only
            // eligible when at least one distribution is detected: wsl.exe ships with
            // stock Windows even when WSL was never set up, so executable presence is
            // not evidence of a working WSL and a bare `wsl` yields a dead session.
            try
            {
                if (WslDistributionDetector.GetInstalledDistributions().Count > 0)
                {
                    return ResolveWsl(options);
                }
            }
            catch
            {
                // Detection failure ⇒ treat as no usable WSL and fall through.
            }

            try
            {
                return ResolvePowerShell(options);
            }
            catch
            {
                return ResolveCmd(options);
            }
        }

        // Try user's preferred shell, then common shells
        string? shell = Environment.GetEnvironmentVariable("SHELL");
        if (!string.IsNullOrEmpty(shell) && File.Exists(shell))
        {
            return (shell, options.Arguments?.ToArray() ?? Array.Empty<string>());
        }

        // Try common shells
        string[] commonShells = ["zsh", "bash", "sh"];
        foreach (string shellName in commonShells)
        {
            string? shellPath = FindExecutableInPath(shellName);
            if (shellPath != null)
            {
                return (shellPath, options.Arguments?.ToArray() ?? Array.Empty<string>());
            }
        }

        throw new ProcessStartException("No suitable shell found on this system");
    }

    /// <summary>
    ///     Resolves Windows Subsystem for Linux (wsl.exe).
    /// </summary>
    private static (string shellPath, string[] arguments) ResolveWsl(ProcessLaunchOptions options)
    {
        string? shellPath = FindExecutableInPath("wsl.exe") ?? FindExecutableInPath("wsl");

        if (shellPath == null)
        {
            // Try common WSL installation paths
            string[] wslPaths = [
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "wsl.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "wsl.exe")
            ];

            foreach (string path in wslPaths)
            {
                if (File.Exists(path))
                {
                    shellPath = path;
                    break;
                }
            }
        }

        if (shellPath == null || !File.Exists(shellPath))
        {
            throw new ProcessStartException("WSL (Windows Subsystem for Linux) not found. Please install WSL2 from the Microsoft Store or enable the Windows feature.", shellPath);
        }

        return (shellPath, options.Arguments?.ToArray() ?? Array.Empty<string>());
    }

    /// <summary>
    ///     Resolves Windows PowerShell (powershell.exe).
    /// </summary>
    private static (string shellPath, string[] arguments) ResolvePowerShell(ProcessLaunchOptions options)
    {
        string shellPath = FindExecutableInPath("powershell.exe") ??
                           Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                               "WindowsPowerShell", "v1.0", "powershell.exe");

        if (!File.Exists(shellPath))
        {
            throw new ProcessStartException("PowerShell not found", shellPath);
        }

        return (shellPath, options.Arguments?.ToArray() ?? Array.Empty<string>());
    }

    /// <summary>
    ///     Resolves PowerShell Core (pwsh.exe).
    /// </summary>
    private static (string shellPath, string[] arguments) ResolvePowerShellCore(ProcessLaunchOptions options)
    {
        string? shellPath = FindExecutableInPath("pwsh.exe") ?? FindExecutableInPath("pwsh");

        if (shellPath == null || !File.Exists(shellPath))
        {
            throw new ProcessStartException("PowerShell Core (pwsh) not found", shellPath);
        }

        return (shellPath, options.Arguments?.ToArray() ?? Array.Empty<string>());
    }

    /// <summary>
    ///     Resolves Windows Command Prompt (cmd.exe).
    /// </summary>
    private static (string shellPath, string[] arguments) ResolveCmd(ProcessLaunchOptions options)
    {
        string shellPath = FindExecutableInPath("cmd.exe") ??
                           Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");

        if (!File.Exists(shellPath))
        {
            throw new ProcessStartException("Command Prompt not found", shellPath);
        }

        return (shellPath, options.Arguments?.ToArray() ?? Array.Empty<string>());
    }

    /// <summary>
    ///     Resolves a custom shell: a path is used as-is; a bare executable name
    ///     (no directory component) is resolved via PATH like every other shell
    ///     type — a configured <c>nu</c> or <c>cmd.exe</c> must launch the same
    ///     way it does in any other terminal, not be checked against the game's
    ///     working directory.
    /// </summary>
    private static (string shellPath, string[] arguments) ResolveCustomShell(ProcessLaunchOptions options)
    {
        if (string.IsNullOrEmpty(options.CustomShellPath))
        {
            throw new ProcessStartException("Custom shell path is required when using ShellType.Custom");
        }

        string? shellPath = options.CustomShellPath;
        if (!File.Exists(shellPath))
        {
            bool isBareName = options.CustomShellPath == Path.GetFileName(options.CustomShellPath);
            shellPath = isBareName ? FindExecutableInPath(options.CustomShellPath) : null;

            if (shellPath == null)
            {
                throw new ProcessStartException($"Custom shell not found: {options.CustomShellPath}",
                    options.CustomShellPath);
            }
        }

        return (shellPath, options.Arguments?.ToArray() ?? Array.Empty<string>());
    }

    /// <summary>
    ///     Finds an executable in the system PATH.
    /// </summary>
    /// <param name="executableName">The executable name to find</param>
    /// <returns>The full path to the executable, or null if not found</returns>
    private static string? FindExecutableInPath(string executableName)
    {
        string? pathVariable = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathVariable))
        {
            return null;
        }

        string[] paths = pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

        foreach (string path in paths)
        {
            try
            {
                string fullPath = Path.Combine(path, executableName);
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }

                // On Windows, also try with .exe extension if not already present
                if (OperatingSystem.IsWindows() && !executableName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    string exePath = fullPath + ".exe";
                    if (File.Exists(exePath))
                    {
                        return exePath;
                    }
                }
            }
            catch
            {
                // Ignore errors accessing individual paths
            }
        }

        return null;
    }
}
