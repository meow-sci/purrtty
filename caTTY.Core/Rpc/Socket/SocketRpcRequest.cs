using System.Text.Json.Serialization;

namespace caTTY.Core.Rpc.Socket;

/// <summary>
/// Represents an RPC request received over the socket.
/// </summary>
public sealed class SocketRpcRequest
{
    /// <summary>
    /// The action name to invoke (e.g., "list-crafts", "ignite-engine").
    /// </summary>
    [JsonPropertyName("action")]
    public string Action { get; set; } = string.Empty;

    /// <summary>
    /// Optional parameters for the action as a JSON element.
    /// </summary>
    [JsonPropertyName("params")]
    public System.Text.Json.JsonElement? Params { get; set; }
}
