namespace purrTTY.Core.Terminal;

/// <summary>
///     Well-known environment-variable names used to pass purrTTY configuration
///     into custom shells. Environment variables are the one configuration
///     channel that flows untouched from launch options through the PTY bridge
///     into <see cref="CustomShellStartOptions.EnvironmentVariables"/>, so a
///     shell implementation never needs a reference to the display layer's
///     configuration types.
/// </summary>
public static class WellKnownShellEnvironment
{
    /// <summary>
    ///     The prompt string for the Game Console shell. Stamped by the launcher
    ///     (game menu / configured default) from the user's configuration.
    /// </summary>
    public const string GameShellPrompt = "PURRTTY_GAME_PROMPT";
}
