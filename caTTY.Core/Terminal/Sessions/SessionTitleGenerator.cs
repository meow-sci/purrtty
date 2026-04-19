namespace caTTY.Core.Terminal;

/// <summary>
///     Generates unique titles for terminal sessions.
/// </summary>
internal class SessionTitleGenerator
{
    /// <summary>
    ///     Generates a unique session title based on current session count.
    /// </summary>
    /// <param name="sessionCount">Current number of sessions</param>
    /// <returns>A unique session title</returns>
    public static string GenerateSessionTitle(int sessionCount)
    {
        var sessionNumber = sessionCount + 1;
        return $"Terminal {sessionNumber}";
    }
}
