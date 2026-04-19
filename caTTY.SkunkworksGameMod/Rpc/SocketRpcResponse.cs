using System.Text.Json.Serialization;

namespace caTTY.SkunkworksGameMod.Rpc;

/// <summary>
/// Represents an RPC response to send back over the socket.
/// </summary>
public sealed class SocketRpcResponse
{
    /// <summary>
    /// Whether the request was successful.
    /// </summary>
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    /// <summary>
    /// The response data (only present if Success is true).
    /// Can be any JSON-serializable object.
    /// </summary>
    [JsonPropertyName("data")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? Data { get; set; }

    /// <summary>
    /// Error message (only present if Success is false).
    /// </summary>
    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Error { get; set; }

    /// <summary>
    /// Creates a successful response with data.
    /// </summary>
    public static SocketRpcResponse Ok(object? data = null) => new() { Success = true, Data = data };

    /// <summary>
    /// Creates a failed response with an error message.
    /// </summary>
    public static SocketRpcResponse Fail(string error) => new() { Success = false, Error = error };
}
