using System.Text.Json;
using caTTY.Core.Rpc.Socket;

namespace caTTY.TermSequenceRpc.SocketRpc.Actions;

/// <summary>
/// Interface for individual socket RPC actions.
/// Each action handles a specific command (e.g., "list-crafts").
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
