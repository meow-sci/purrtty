namespace caTTY.Core.Terminal;

/// <summary>
///     Prepares launch options for new session creation.
/// </summary>
internal class LaunchOptionsPreparator
{
    /// <summary>
    ///     Prepares effective launch options for a new session by applying current terminal dimensions.
    /// </summary>
    /// <param name="providedOptions">User-provided launch options (or null to use defaults)</param>
    /// <param name="dimensionTracker">Dimension tracker to get defaults and current dimensions</param>
    /// <returns>Launch options with current terminal dimensions applied</returns>
    public static ProcessLaunchOptions PrepareEffectiveLaunchOptions(
        ProcessLaunchOptions? providedOptions,
        SessionDimensionTracker dimensionTracker)
    {
        // Ensure the terminal emulator and PTY process start with the same dimensions.
        // If launchOptions is null, use the last-known/default size (updated via resize handlers).
        Console.WriteLine($"LaunchOptionsPreparator: PrepareEffectiveLaunchOptions called with providedOptions={providedOptions != null}");
        if (providedOptions != null)
        {
            Console.WriteLine($"LaunchOptionsPreparator: Provided shell type: {providedOptions.ShellType}");
            if (providedOptions.ShellType == ShellType.CustomGame)
            {
                Console.WriteLine($"LaunchOptionsPreparator: Provided custom shell ID: {providedOptions.CustomShellId}");
            }
        }

        ProcessLaunchOptions effectiveLaunchOptions = providedOptions != null
            ? SessionDimensionTracker.CloneLaunchOptions(providedOptions)
            : dimensionTracker.GetDefaultLaunchOptionsSnapshot();

        Console.WriteLine($"LaunchOptionsPreparator: Effective shell type after preparation: {effectiveLaunchOptions.ShellType}");

        // Always start new sessions at the last-known UI size.
        // This prevents shell changes (WSL/PowerShell/Cmd) from reverting to 80x24/80x25 defaults.
        var lastKnown = dimensionTracker.LastKnownTerminalDimensions;
        effectiveLaunchOptions.InitialWidth = lastKnown.cols;
        effectiveLaunchOptions.InitialHeight = lastKnown.rows;

        return effectiveLaunchOptions;
    }
}
