namespace caTTY.CustomShells.GameStuffShell.Execution;

/// <summary>
/// Abstraction for game-specific operations.
/// This will be implemented later with actual game integration.
/// </summary>
public interface IGameStuffApi
{
    /// <summary>
    /// Gets the names of all available crafts.
    /// </summary>
    /// <returns>A list of craft names.</returns>
    IReadOnlyList<string> GetCraftNames();

    /// <summary>
    /// Attempts to follow (focus camera on) a craft.
    /// </summary>
    /// <param name="craftName">The name of the craft to follow.</param>
    /// <param name="error">Error message if the operation failed.</param>
    /// <returns>True if successful; otherwise, false.</returns>
    bool TryFollowCraft(string craftName, out string? error);
}
