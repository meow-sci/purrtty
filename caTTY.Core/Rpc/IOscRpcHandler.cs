namespace caTTY.Core.Rpc;

/// <summary>
///     Interface for handling OSC-based RPC commands.
///     OSC sequences in the private-use range (1000+) are used for game integration
///     because they pass through ConPTY unlike DCS sequences.
/// </summary>
public interface IOscRpcHandler
{
    /// <summary>
    ///     Determines if the given OSC command number is a private RPC command.
    /// </summary>
    /// <param name="command">The OSC command number</param>
    /// <returns>True if this is a private RPC command (1000+)</returns>
    bool IsPrivateCommand(int command);

    /// <summary>
    ///     Handles a private OSC RPC command.
    /// </summary>
    /// <param name="command">The OSC command number (e.g., 1010)</param>
    /// <param name="payload">The payload string (typically JSON)</param>
    void HandleCommand(int command, string? payload);
}
