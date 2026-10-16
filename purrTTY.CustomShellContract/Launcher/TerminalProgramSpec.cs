namespace purrTTY.Core.Terminal;

/// <summary>
///     What runs inside a launched terminal: a registered <see cref="ICustomShell"/>, a real
///     process, or (when both are unset) the user's configured default shell. A POCO so the
///     contract assembly stays free of purrTTY's launch-option types.
///     <para>
///         Selection order: <see cref="CustomShellId"/> (non-blank) wins, then
///         <see cref="Program"/>, otherwise the configured default shell.
///     </para>
/// </summary>
public sealed class TerminalProgramSpec
{
    /// <summary>
    ///     Id of a shell registered with <see cref="CustomShellRegistry"/>. When non-blank the
    ///     terminal hosts that custom shell instead of a real process.
    /// </summary>
    public string? CustomShellId { get; set; }

    /// <summary>
    ///     Path (or bare executable name, resolved through <c>PATH</c>) of a real process to run.
    ///     Prefer an absolute path: a game launched from a desktop environment often has a
    ///     minimal <c>PATH</c>. Both this and <see cref="CustomShellId"/> unset selects the
    ///     configured default shell.
    /// </summary>
    public string? Program { get; set; }

    /// <summary>
    ///     Command-line arguments for <see cref="Program"/>. Custom shells receive no argument
    ///     vector (the shell start options carry none); pass shell input through
    ///     <see cref="Environment"/> or <see cref="Configuration"/> instead.
    /// </summary>
    public List<string> Arguments { get; set; } = new();

    /// <summary>Working directory for the process or shell; null keeps purrTTY's default (the user's home).</summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>
    ///     Extra environment variables, merged over purrTTY's defaults (entries here win). This is
    ///     how a mod passes an identity to a registered custom shell, whose factory takes no
    ///     arguments: the values arrive in <see cref="CustomShellStartOptions.EnvironmentVariables"/>.
    /// </summary>
    public Dictionary<string, string> Environment { get; set; } = new();

    /// <summary>Optional command typed into the shell (newline-terminated) once it has started.</summary>
    public string? StartupCommand { get; set; }

    /// <summary>
    ///     Opaque shell-specific configuration, forwarded to
    ///     <see cref="CustomShellStartOptions.Configuration"/> for custom shells. Ignored for real processes.
    /// </summary>
    public Dictionary<string, object> Configuration { get; set; } = new();
}
