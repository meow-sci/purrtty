using System.Text.Json;

namespace caTTY.SkunkworksGameMod.Rpc;

/// <summary>
/// Interface for individual socket RPC actions.
/// Each action handles a specific command (e.g., "camera-orbit").
/// </summary>
public interface ISocketRpcAction
{
    /// <summary>
    /// The action name this handler responds to.
    /// </summary>
    string ActionName { get; }

    /// <summary>
    /// Executes the action and returns a response.
    /// </summary>
    /// <param name="params">Optional parameters from the request</param>
    /// <returns>The response to send back</returns>
    SocketRpcResponse Execute(JsonElement? @params);
}
